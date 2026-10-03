using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Caret context after a change: the modifiers covering the caret (full set plus what
    /// entered and left since the previous state), the selection it was computed for, and the
    /// source editor for follow-up queries (<see cref="UniTextEditable.IsStyleActive{T}"/>,
    /// <see cref="UniTextEditable.TryGetStyleParameter{T}"/>, caret geometry). The lists are
    /// reused between dispatches — read them during the event call, copy if you need to keep
    /// them.
    /// </summary>
    public readonly struct CaretContext
    {
        /// <summary>Every modifier whose span covers the caret (or the whole selection).</summary>
        public IReadOnlyList<BaseModifier> Active { get; }

        /// <summary>Modifiers the caret just entered.</summary>
        public IReadOnlyList<BaseModifier> Entered { get; }

        /// <summary>Modifiers the caret just left.</summary>
        public IReadOnlyList<BaseModifier> Exited { get; }

        /// <summary>The selection the context was computed for.</summary>
        public TextSelection Selection { get; }

        /// <summary>Whether an IME composition is in progress (toolbars typically disable).</summary>
        public bool IsComposing { get; }

        /// <summary>The editor that raised the change.</summary>
        public UniTextEditable Editable { get; }

        public CaretContext(IReadOnlyList<BaseModifier> active, IReadOnlyList<BaseModifier> entered, IReadOnlyList<BaseModifier> exited,
            TextSelection selection, bool isComposing, UniTextEditable editable)
        {
            Active = active;
            Entered = entered;
            Exited = exited;
            Selection = selection;
            IsComposing = isComposing;
            Editable = editable;
        }
    }

    /// <summary>
    /// Caret style context: which modifiers (bold, color, link, …) cover the caret or selection.
    /// Drives formatting-toolbar state — light the Bold button while the caret sits in a bold
    /// span. At a hidden boundary, a collapsed caret inherits only spans continuing across both sides;
    /// either range edge is outside. Visible Raw/Reveal syntax retains its exact source position, while a
    /// selection counts spans covering it whole.
    /// </summary>
    public partial class UniTextEditable
    {
        private readonly List<BaseModifier> caretContextActive = new(8);
        private readonly List<BaseModifier> caretContextPrevious = new(8);
        private readonly List<BaseModifier> caretContextEntered = new(4);
        private readonly List<BaseModifier> caretContextExited = new(4);
        private readonly List<(string parameter, bool uniform)> caretContextParams = new(8);
        private readonly List<(string parameter, bool uniform)> caretContextPreviousParams = new(8);

        /// <summary>
        /// Occurs (frame-coalesced) when the set of modifiers covering the caret / selection
        /// has changed — on caret movement, selection change, or reflow — and when a pending
        /// typing style flips (re-query <see cref="IsStyleActive{T}"/>). Subscribe from an
        /// <see cref="InputBehavior"/> or see <see cref="CaretContextBehavior"/> for the
        /// inspector-wired form.
        /// </summary>
        public event Action<CaretContext> CaretContextChanged;

        /// <summary>Modifiers currently covering the caret / selection. Reused list — copy to keep.</summary>
        public IReadOnlyList<BaseModifier> ModifiersAtCaret => caretContextActive;

        internal bool IsModifierActiveAtCaret(BaseModifier exemplar)
        {
            if (exemplar == null) return false;
            for (var i = 0; i < caretContextActive.Count; i++)
                if (MatchesStyle(caretContextActive[i], exemplar)) return true;
            return false;
        }

        private static bool ContainsBySignature(List<BaseModifier> list, BaseModifier modifier)
        {
            if (modifier == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (modifier.SignatureMatches(list[i])) return true;
            return false;
        }

        /// <summary>
        /// Whether typing now would produce <typeparamref name="T"/>-styled text — the toolbar
        /// state query: a pending typing style wins, otherwise the spans at the caret decide.
        /// </summary>
        public bool IsStyleActive<T>() where T : BaseModifier => IsStyleActive(typeof(T));

        /// <inheritdoc cref="IsStyleActive(BaseModifier)"/>
        public bool IsStyleActive(Type modifierType)
            => TryResolveExemplar(modifierType, out var exemplar, out _) && IsStyleActive(exemplar);

        /// <summary>
        /// Whether typing now would produce text matching <paramref name="exemplar"/>'s style (by modifier
        /// signature — a composite matches its ordered child types). A pending typing style wins; otherwise the
        /// spans at the caret decide.
        /// </summary>
        public bool IsStyleActive(BaseModifier exemplar)
        {
            if (exemplar == null) return false;
            if (pendingTypingStyles != null)
            {
                for (int i = 0; i < pendingTypingStyles.Count; i++)
                    if (MatchesStyle(pendingTypingStyles[i].exemplar, exemplar)) return pendingTypingStyles[i].apply;
            }
            return IsModifierActiveAtCaret(exemplar);
        }

        /// <summary>
        /// The parameter the active <typeparamref name="T"/> style carries at the caret /
        /// selection — the color-swatch query for toolbars. A pending typing style wins;
        /// otherwise the covering span's parameter is returned. <see langword="false"/> when
        /// the style is not active.
        /// </summary>
        public bool TryGetStyleParameter<T>(out string parameter) where T : BaseModifier
            => TryGetStyleParameter(typeof(T), out parameter);

        /// <inheritdoc cref="TryGetStyleParameter(BaseModifier, out string)"/>
        public bool TryGetStyleParameter(Type modifierType, out string parameter)
        {
            if (TryResolveExemplar(modifierType, out var exemplar, out _))
                return TryGetStyleParameter(exemplar, out parameter);
            parameter = null;
            return false;
        }

        /// <summary>
        /// The parameter the active style matching <paramref name="exemplar"/> carries at the caret / selection —
        /// the color-swatch query for toolbars. A pending typing style wins; otherwise the covering span's
        /// parameter is returned. <see langword="false"/> when the style is not active or its value is mixed.
        /// </summary>
        public bool TryGetStyleParameter(BaseModifier exemplar, out string parameter)
        {
            parameter = null;
            if (exemplar == null) return false;
            if (pendingTypingStyles != null)
            {
                for (int i = 0; i < pendingTypingStyles.Count; i++)
                {
                    if (!MatchesStyle(pendingTypingStyles[i].exemplar, exemplar)) continue;
                    parameter = pendingTypingStyles[i].parameter;
                    return pendingTypingStyles[i].apply;
                }
            }
            for (int i = 0; i < caretContextActive.Count; i++)
            {
                if (!MatchesStyle(caretContextActive[i], exemplar)) continue;
                if (!caretContextParams[i].uniform) return false;
                parameter = caretContextParams[i].parameter;
                return true;
            }
            return false;
        }

        private bool caretContextPendingDirty;

        /// <summary>
        /// Recomputes the caret context from the last parse and fires
        /// <see cref="CaretContextChanged"/> when the active set changed. Runs after selection
        /// changes and after reflow; spans reflect the most recent completed parse. The diff
        /// compares by modifier SIGNATURE, not reference — a parse may produce fresh modifier
        /// instances, and reference identity would report every modifier as entered + exited on
        /// each reflow (toolbar flicker, event spam).
        /// </summary>
        private void UpdateCaretContext()
        {
            caretContextPrevious.Clear();
            caretContextPrevious.AddRange(caretContextActive);
            caretContextPreviousParams.Clear();
            caretContextPreviousParams.AddRange(caretContextParams);
            caretContextActive.Clear();
            caretContextParams.Clear();

            var sel = Selection;
            var vs = DocumentToRendered(sel.Start);
            var ve = DocumentToRendered(sel.End);
            if (vs == ve)
            {
                var boundaryOutside = !isComposing && (!HasMarkupView
                    || markupViewMap.RenderedToSource(vs, MarkupViewStick.Before)
                    != markupViewMap.RenderedToSource(vs, MarkupViewStick.After));
                if (boundaryOutside)
                {
                    if (vs > 0 && vs < RenderedCodepointCount)
                    {
                        vs--;
                        ve++;
                    }
                }
                else
                {
                    vs--;
                }
            }
            if (vs >= 0 && ve > vs)
                TextComponent.AttributeParser?.CollectModifiersCovering(vs, ve, caretContextActive, caretContextParams);

            caretContextEntered.Clear();
            caretContextExited.Clear();
            for (int i = 0; i < caretContextActive.Count; i++)
                if (!ContainsBySignature(caretContextPrevious, caretContextActive[i]))
                    caretContextEntered.Add(caretContextActive[i]);
            for (int i = 0; i < caretContextPrevious.Count; i++)
                if (!ContainsBySignature(caretContextActive, caretContextPrevious[i]))
                    caretContextExited.Add(caretContextPrevious[i]);

            var paramsChanged = caretContextParams.Count != caretContextPreviousParams.Count;
            if (!paramsChanged)
            {
                for (int i = 0; i < caretContextParams.Count; i++)
                {
                    if (caretContextParams[i] == caretContextPreviousParams[i]) continue;
                    paramsChanged = true;
                    break;
                }
            }

            if (caretContextEntered.Count == 0 && caretContextExited.Count == 0
                && !paramsChanged && !caretContextPendingDirty) return;
            caretContextPendingDirty = false;

            CaretContextChanged?.Invoke(new CaretContext(
                caretContextActive, caretContextEntered, caretContextExited, sel, isComposing, this));
        }
    }
}
