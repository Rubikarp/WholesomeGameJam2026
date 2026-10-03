using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Style commands over persistent attributed ranges. Source syntax belongs to each range only for serialization;
    /// it never enters the hidden editable document.
    /// </summary>
    public partial class UniTextEditable
    {
        private const string StyleEditReason = "edit.style";

        private List<(BaseModifier exemplar, ParseRule rule, string parameter, bool apply)> pendingTypingStyles;
        private BaseModifier typingStyleMatch;
        private ParseRule typingStyleRule;
        private string typingStyleParameter;
        private Predicate<SourceAnnotation> typingStylePredicate;

        private int markupViewMatchVersion = -1;
        private string markupViewMatchSource;
        private readonly List<AttributeParser.MarkupMatch> markupViewMatches = new(8);

        /// <summary>
        /// Version-keyed matches over the synthesized source serialization, shared by source-view
        /// editing consumers. The string is materialized at most once per attributed-document version;
        /// consumers must not retain the list across edits.
        /// </summary>
        private bool TryGetMarkupViewMatches(out string source, out List<AttributeParser.MarkupMatch> matches)
        {
            source = null;
            matches = null;
            var parser = TextComponent != null ? TextComponent.AttributeParser : null;
            if (parser == null || ViewText == null) return false;

            if (markupViewMatchVersion != document.Version)
            {
                markupViewMatchSource = document.SourceText;
                markupViewMatches.Clear();
                parser.CollectMarkupMatches(markupViewMatchSource.AsSpan(), markupViewMatches, includeSelfClosing: true);
                markupViewMatchVersion = document.Version;
            }

            source = markupViewMatchSource;
            matches = markupViewMatches;
            return true;
        }

        private void InvalidateMarkupViewMatches() => markupViewMatchVersion = -1;

        /// <summary>
        /// The component's configured modifier of <paramref name="modifierType"/> — the exemplar the
        /// type-based public API matches against, plus its style's rule. The exemplar may be a leaf
        /// inside a composite; the rule is that style's own, so wrapping applies the whole composite.
        /// False (no-op) when none is configured.
        /// </summary>
        private bool TryResolveExemplar(Type modifierType, out BaseModifier exemplar, out ParseRule rule)
        {
            exemplar = null;
            rule = null;
            if (TextComponent == null || !TextComponent.TryGetStyle(modifierType, out var s)) return false;
            exemplar = CompositeModifier.FindLeaf(s.Modifier, modifierType);
            rule = s.Source as ParseRule;
            return exemplar != null;
        }

        /// <summary>
        /// Applies <typeparamref name="T"/> to the visible codepoint range. Undoable.
        /// </summary>
        public void ApplyStyleRange<T>(int start, int end, string parameter = null) where T : BaseModifier
        {
            if (TryResolveExemplar(typeof(T), out var exemplar, out var rule))
                ApplyStyleRange(exemplar, rule, start, end, parameter);
        }

        internal void ApplyStyleRange(BaseModifier exemplar, ParseRule rule, int start, int end, string parameter)
            => SetStyle(exemplar, rule, start, end, true, parameter);

        /// <summary>
        /// The single style-edit primitive over the document range: applies the modifier (<paramref name="on"/>
        /// = true) or removes it. Apply, remove, toggle, and clear all route here. A null
        /// <paramref name="exemplar"/> with <paramref name="on"/> false clears every recognised style.
        /// </summary>
        public void SetStyle<T>(int start, int end, bool on, string parameter = null) where T : BaseModifier
        {
            if (TryResolveExemplar(typeof(T), out var exemplar, out var rule))
                SetStyle(exemplar, rule, start, end, on, parameter);
        }

        internal void SetStyle(BaseModifier exemplar, ParseRule explicitRule, int start, int end, bool on, string parameter)
        {
            EnsureInitialized();
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            start = Math.Clamp(start, 0, codepointCount);
            end = Math.Clamp(end, 0, codepointCount);
            if (start >= end) return;
            if (HasMarkupView)
            {
                var visibleStart = MarkupViewPositionToVisible(start);
                var visibleEnd = MarkupViewPositionToVisible(end);
                ExitMarkupView();
                SetStyle(exemplar, explicitRule, visibleStart, visibleEnd, on, parameter);
                EnterMarkupView();
                return;
            }
            if (on)
            {
                if (exemplar == null) return;
                var rule = explicitRule ?? TextComponent?.ResolveWrapRule(exemplar);
                if (rule == null) return;
                AddStyleAnnotation(start, end, exemplar, rule, parameter);
            }
            else RemoveStyleAnnotations(exemplar, start, end);
        }

        /// <summary>
        /// Applies <typeparamref name="T"/> with <paramref name="parameter"/> to the selection, or stores
        /// it as the pending typing style at a collapsed caret (SET semantics for value pickers).
        /// </summary>
        public void ApplyStyle<T>(string parameter = null) where T : BaseModifier
        {
            if (TryResolveExemplar(typeof(T), out var exemplar, out var rule))
                ApplyStyle(exemplar, rule, parameter);
        }

        internal void ApplyStyle(BaseModifier exemplar, ParseRule rule, string parameter)
        {
            EnsureInitialized();
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            var sel = Selection;
            if (sel.IsCollapsed) { SetTypingStyle(exemplar, rule, parameter); return; }
            ResolveDocumentRange(sel, out var start, out var end);
            ApplyStyleRange(exemplar, rule, start, end, parameter);
        }

        /// <summary>Removes <typeparamref name="T"/>'s tags from the codepoint range. Undoable.</summary>
        public void RemoveStyleRange<T>(int start, int end) where T : BaseModifier
        {
            if (TryResolveExemplar(typeof(T), out var exemplar, out _))
                SetStyle(exemplar, null, start, end, false, null);
        }

        /// <summary>
        /// Toggles <typeparamref name="T"/> on the selection — wraps when any part is unstyled, strips
        /// when fully styled. With a collapsed caret, flips the pending typing style. Returns whether on.
        /// </summary>
        public bool ToggleStyle<T>(string parameter = null) where T : BaseModifier
            => TryResolveExemplar(typeof(T), out var exemplar, out var rule) && ToggleStyle(exemplar, rule, parameter);

        /// <summary>
        /// Toggles the component's style matching <paramref name="exemplar"/> (by modifier signature — a composite
        /// matches its ordered child types) on the selection, or the pending typing style at a caret. No-op when
        /// the component has no matching style with a wrappable rule. Returns whether on.
        /// </summary>
        public bool ToggleStyle(BaseModifier exemplar, string parameter = null)
            => ToggleStyle(exemplar, null, parameter);

        internal bool ToggleStyle(BaseModifier exemplar, ParseRule explicitRule, string parameter)
        {
            EnsureInitialized();
            if (exemplar == null) return false;
            if (readOnly) return IsStyleActive(exemplar);
            EndCompositionBeforeDocumentMutation();
            if (HasMarkupView)
            {
                ExitMarkupView();
                var result = ToggleStyle(exemplar, explicitRule, parameter);
                EnterMarkupView();
                return result;
            }
            var sel = Selection;
            if (sel.IsCollapsed) return ToggleTypingStyle(exemplar, explicitRule, parameter);
            ResolveDocumentRange(sel, out var start, out var end);

            if (IsRangeFullyStyled(exemplar, start, end))
            {
                SetStyle(exemplar, explicitRule, start, end, false, parameter);
                return false;
            }

            var rule = explicitRule ?? TextComponent?.ResolveWrapRule(exemplar);
            if (rule == null) return false;
            SetStyle(exemplar, rule, start, end, true, parameter);
            return true;
        }

        /// <summary>
        /// Strips every style tag from the selection (clear-formatting), or resets typing styles at a
        /// collapsed caret so the next inserted text is plain. At a range edge, the default typing context
        /// is already outside; an explicitly retained context is cleared as one set.
        /// </summary>
        public void ClearFormatting()
        {
            EnsureInitialized();
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            var sel = Selection;
            if (sel.IsCollapsed) { ClearTypingStyles(); return; }
            ResolveDocumentRange(sel, out var start, out var end);
            SetStyle((BaseModifier)null, null, start, end, false, null);
        }

        /// <summary>
        /// Inserts an inline object styled by <typeparamref name="T"/> at the caret — a self-closing tag
        /// when the component has a tag style for it, otherwise a bare Object Replacement Character.
        /// </summary>
        public void InsertObject<T>(string parameter) where T : BaseModifier
        {
            EnsureInitialized();
            var syntax = TryResolveExemplar(typeof(T), out _, out var rule)
                ? CompositeParseRule.FindLeaf<TagParseRule>(rule)?.SelfClosing(parameter)
                : null;
            if (syntax != null) InsertMarkupText(syntax, StyleEditReason);
            else InsertText(UnicodeData.ObjectReplacementCharacterString);
        }

        /// <summary>
        /// Adds an attributed range while retaining the rule-owned syntax needed for source export.
        /// </summary>
        private void AddStyleAnnotation(int start, int end, BaseModifier exemplar, ParseRule rule, string parameter)
        {
            if (rule?.CanWrap != true) return;
            var modifier = TextComponent.ResolveStyleModifier(exemplar, rule);
            if (modifier == null) return;
            var stateBefore = document.CaptureState();
            document.AddAnnotation(new SourceAnnotation(start, end, modifier, rule, rule, parameter,
                null, null, null,
                SourceAnnotationKind.Style, 0));
            undoStack.RecordAttributed(start, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, stateBefore,
                document.CaptureState(), Selection, end);
            var shape = new EditShape(start, 0, 0);
            ApplyShapeToDerivedState(in shape);
            Selectable.SetSelectionInternal(new TextSelection(start, end, CaretAffinity.Downstream), SelectionChangeReason.Style);
            MarkDocumentChanged(StyleEditReason);
        }

        /// <summary>
        /// Removes the style from the document range as one undoable edit. A range inside a single flat styled run
        /// splits it so the style survives outside the range (<c>&lt;b&gt;B&lt;/b&gt;o&lt;b&gt;ld&lt;/b&gt;</c>);
        /// otherwise every pair matching <paramref name="exemplar"/>'s signature (or every recognised pair when
        /// <see langword="null"/>) whose content overlaps the range is unwrapped in one edit, non-matching nested
        /// tags kept.
        /// </summary>
        private void RemoveStyleAnnotations(BaseModifier exemplar, int start, int end)
        {
            if (readOnly || start >= end) return;
            var stateBefore = document.CaptureState();
            if (!document.RemoveStyles(start, end,
                    exemplar == null ? null : annotation => MatchesStyle(annotation.modifier, exemplar))) return;
            undoStack.RecordAttributed(start, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, stateBefore,
                document.CaptureState(), Selection, end);
            var shape = new EditShape(start, 0, 0);
            ApplyShapeToDerivedState(in shape);
            MarkDocumentChanged(StyleEditReason);
        }

        /// <summary>
        /// Expands a deletion to cover any atomic replacement annotation it touches.
        /// </summary>
        /// <remarks>
        /// Ordinary style annotations remain range-mapped by <see cref="AttributedDocument.Replace"/>.
        /// </remarks>
        private void ExpandDeleteOverAtomicAnnotations(ref int start, ref int count, ref int caretAfter)
        {
            var end = start + count;
            var annotations = document.Annotations;
            var changed = false;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (!annotation.IsAtomic || annotation.end <= start || annotation.start >= end) continue;
                if (annotation.start < start) start = annotation.start;
                if (annotation.end > end) end = annotation.end;
                changed = true;
            }
            if (!changed) return;
            count = end - start;
            if (caretAfter >= 0) caretAfter = start;
        }

        private bool IsRangeFullyStyled(BaseModifier exemplar, int start, int end,
            ParseRule rule = null, string parameter = null)
        {
            if (exemplar == null || start >= end) return false;
            var covered = start;
            var annotations = document.Annotations;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.start > covered) break;
                if (annotation.kind != SourceAnnotationKind.Style
                    || !MatchesStyle(annotation.modifier, exemplar)
                    || rule != null && (!ReferenceEquals(annotation.rule, rule)
                                        || !string.Equals(annotation.parameter, parameter, StringComparison.Ordinal))
                    || annotation.end <= covered) continue;
                covered = annotation.end;
                if (covered >= end) return true;
            }
            return false;
        }

        private void SetTypingStyle(BaseModifier exemplar, ParseRule rule, string parameter, bool apply = true)
        {
            if (exemplar == null) return;
            pendingTypingStyles ??= new();
            for (var i = pendingTypingStyles.Count - 1; i >= 0; i--)
                if (MatchesStyle(pendingTypingStyles[i].exemplar, exemplar)) pendingTypingStyles.RemoveAt(i);
            pendingTypingStyles.Add((exemplar, rule, parameter, apply));
            caretContextPendingDirty = true;
            selectionDirty = true;
        }

        /// <summary>The pending typing state for the style matching <paramref name="exemplar"/>: on, off, or no entry.</summary>
        private bool? PendingTypingApply(BaseModifier exemplar)
        {
            if (pendingTypingStyles == null) return null;
            for (var i = 0; i < pendingTypingStyles.Count; i++)
                if (MatchesStyle(exemplar, pendingTypingStyles[i].exemplar)) return pendingTypingStyles[i].apply;
            return null;
        }

        /// <summary>
        /// A repeated arrow press at a hidden range boundary consumes an explicitly retained typing style and
        /// clears the complete typing context. With the default non-sticky boundaries no escape is needed: the
        /// first move to either edge already places subsequent input outside every range ending or starting there.
        /// </summary>
        private bool TryConsumeFormattingEscape(bool toPrev)
        {
            if (!arrowKeyEscapesFormatting || readOnly || isComposing || HasMarkupView) return false;
            var focus = Selection.Focus;
            var annotations = document.Annotations;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.kind != SourceAnnotationKind.Style || annotation.modifier == null) continue;
                var boundary = toPrev ? annotation.start == focus : annotation.end == focus;
                if (!boundary || PendingTypingApply(annotation.modifier) != true) continue;
                ClearTypingStyles();
                return true;
            }
            return false;
        }

        private bool ToggleTypingStyle(BaseModifier exemplar, ParseRule rule, string parameter)
        {
            if (exemplar == null) return false;
            pendingTypingStyles ??= new();
            caretContextPendingDirty = true;
            selectionDirty = true;
            for (var i = 0; i < pendingTypingStyles.Count; i++)
            {
                if (!MatchesStyle(pendingTypingStyles[i].exemplar, exemplar)) continue;
                var wasApplying = pendingTypingStyles[i].apply;
                pendingTypingStyles.RemoveAt(i);
                return !wasApplying;
            }
            var active = IsModifierActiveAtCaret(exemplar);
            pendingTypingStyles.Add((exemplar, rule, parameter, !active));
            return !active;
        }

        private void DiscardTypingStyles()
        {
            if (pendingTypingStyles == null || pendingTypingStyles.Count == 0) return;
            pendingTypingStyles.Clear();
            caretContextPendingDirty = true;
        }

        private void ClearTypingStyles()
        {
            pendingTypingStyles ??= new();
            pendingTypingStyles.Clear();
            var focus = HasMarkupView ? MarkupViewPositionToVisible(Selection.Focus) : Selection.Focus;
            var annotations = document.Annotations;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.start > focus) break;
                if (annotation.kind != SourceAnnotationKind.Style || annotation.modifier == null) continue;
                var strictlyInside = annotation.start < focus && annotation.end > focus;
                if (!strictlyInside && (!HasMarkupView || !IsModifierActiveAtCaret(annotation.modifier))) continue;
                SetTypingStyle(annotation.modifier, annotation.rule, annotation.parameter, apply: false);
            }
            caretContextPendingDirty = true;
            selectionDirty = true;
        }

        internal bool HasPendingTypingStyles => pendingTypingStyles != null && pendingTypingStyles.Count > 0;

        /// <summary>
        /// Applies the explicit typing state to a just-inserted range. Positive entries remain active for
        /// continuous typing; negative entries only carve out this insertion because non-sticky boundaries keep
        /// subsequent text outside without retained state.
        /// </summary>
        private void ApplyPendingTypingStyles(int start, int end)
        {
            if (pendingTypingStyles == null || pendingTypingStyles.Count == 0 || start >= end) return;
            if (HasMarkupView)
            {
                var visibleStart = MarkupViewPositionToVisible(start);
                var visibleEnd = MarkupViewPositionToVisible(end);
                ExitMarkupView();
                ApplyPendingTypingStyles(visibleStart, visibleEnd);
                EnterMarkupView();
                return;
            }

            typingApplyScratch ??= new();
            typingApplyScratch.Clear();
            typingApplyScratch.AddRange(pendingTypingStyles);
            pendingTypingStyles.Clear();

            for (var i = 0; i < typingApplyScratch.Count; i++)
            {
                var pending = typingApplyScratch[i];
                if (pending.apply)
                {
                    var rule = pending.rule ?? TextComponent?.ResolveWrapRule(pending.exemplar);
                    if (rule == null) continue;
                    if (!IsRangeFullyStyled(pending.exemplar, start, end, rule, pending.parameter)
                        && !ExtendTypingStyle(pending.exemplar, rule, pending.parameter, start, end))
                        AddStyleAnnotation(start, end, pending.exemplar, rule, pending.parameter);
                }
                else
                {
                    ExcludeStyleFromInsertedRange(pending.exemplar, start, end);
                }
            }
            Selectable.SetSelectionInternal(TextSelection.Caret(end), SelectionChangeReason.Style);
            for (var i = 0; i < typingApplyScratch.Count; i++)
            {
                var pending = typingApplyScratch[i];
                if (pending.apply) pendingTypingStyles.Add(pending);
            }
            if (pendingTypingStyles.Count > 0)
            {
                caretContextPendingDirty = true;
                selectionDirty = true;
            }
        }

        private List<(BaseModifier exemplar, ParseRule rule, string parameter, bool apply)> typingApplyScratch;

        private bool ExtendTypingStyle(BaseModifier exemplar, ParseRule rule, string parameter, int start, int end)
        {
            var stateBefore = document.CaptureState();
            typingStyleMatch = exemplar;
            typingStyleRule = rule;
            typingStyleParameter = parameter;
            typingStylePredicate ??= MatchesCurrentTypingStyle;
            var changed = document.ExtendStylesEndingAt(start, end, typingStylePredicate);
            if (!changed) changed = document.ExtendStylesStartingAt(end, start, typingStylePredicate);
            typingStyleMatch = null;
            typingStyleRule = null;
            typingStyleParameter = null;
            if (!changed) return false;
            undoStack.RecordAttributed(start, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, stateBefore,
                document.CaptureState(), Selection, end);
            var shape = new EditShape(start, 0, 0);
            ApplyShapeToDerivedState(in shape);
            MarkDocumentChanged(StyleEditReason);
            return true;
        }

        private bool MatchesCurrentTypingStyle(SourceAnnotation annotation)
            => MatchesStyle(annotation.modifier, typingStyleMatch)
               && ReferenceEquals(annotation.rule, typingStyleRule)
               && string.Equals(annotation.parameter, typingStyleParameter, StringComparison.Ordinal);

        /// <summary>
        /// Splits the enclosing tag pair matching <paramref name="exemplar"/>'s signature around <c>[start, end)</c>,
        /// leaving that span unwrapped: <c>&lt;b&gt;Bold&lt;/b&gt;</c> around <c>o</c> becomes
        /// <c>&lt;b&gt;B&lt;/b&gt;o&lt;b&gt;ld&lt;/b&gt;</c>. A boundary side with no content drops its half, so a
        /// split at the run's end or start leaves no empty tag. Used both to exclude just-typed text from a run and
        /// to strip a style from a sub-range. Does nothing when the range is not inside one such pair.
        /// </summary>
        private void ExcludeStyleFromInsertedRange(BaseModifier exemplar, int start, int end)
        {
            if (exemplar == null) return;
            var stateBefore = document.CaptureState();
            if (!document.RemoveStyles(start, end,
                    annotation => MatchesStyle(annotation.modifier, exemplar))) return;
            undoStack.RecordAttributed(start, ReadOnlySpan<char>.Empty, ReadOnlySpan<char>.Empty, stateBefore,
                document.CaptureState(), Selection, end);
            var shape = new EditShape(start, 0, 0);
            ApplyShapeToDerivedState(in shape);
            MarkDocumentChanged(StyleEditReason);
        }

        private static bool MatchesStyle(BaseModifier stored, BaseModifier exemplar)
        {
            if (stored == null || exemplar == null) return false;
            if (exemplar.SignatureMatches(stored)) return true;
            return exemplar is not CompositeModifier
                   && CompositeModifier.FindLeaf(stored, exemplar.GetType()) != null;
        }

    }
}
