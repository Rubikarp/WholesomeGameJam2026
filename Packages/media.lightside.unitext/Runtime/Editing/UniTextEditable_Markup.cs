using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Synthesized source presentation for Raw and Reveal. The attributed document remains visible text plus
    /// spans; only this temporary view contains markup characters and needs source↔rendered mapping.
    /// </summary>
    public partial class UniTextEditable
    {
        private readonly MarkupViewMap markupViewMap = new();
        private readonly List<ProjectedRange> projectedRangeScratch = new();
        private MarkupVisibility markupVisibility = MarkupVisibility.Hidden;
        private GapBuffer markupViewBuffer;

        /// <summary>
        /// How this field presents its markup tags — hidden and atomic (default), revealed around the
        /// caret, or shown as raw source. Replaces the former AutoConvert flag.
        /// </summary>
        public MarkupVisibility MarkupVisibility
        {
            get => markupVisibility;
            set
            {
                if (markupVisibility == value) return;
                markupVisibility = value;
                if (value == MarkupVisibility.Hidden) ExitMarkupView();
                else EnterMarkupView();
                lastRevealStart = int.MinValue;
                lastRevealEnd = int.MinValue;
                if (isActiveAndEnabled) SyncPresentation();
                selectionDirty = true;
            }
        }

        private bool HasMarkupView => markupViewBuffer != null;

        private void EnterMarkupView()
        {
            if (!document.IsInitialized || markupViewBuffer != null) return;
            var projection = AttributedDocumentMarkup.BuildProjection(document.Text, document.Annotations);
            sourceViewToDocument = projection.sourceViewToDocument;
            documentToSourceViewInsertion = projection.insertion;
            markupViewBuffer = new GapBuffer(Math.Max(64, projection.source.Length));
            markupViewBuffer.SetText(projection.source.AsSpan());
            var selection = Selection;
            var anchorPosition = Math.Clamp(selection.Anchor, 0, projection.before.Length - 1);
            var focusPosition = Math.Clamp(selection.Focus, 0, projection.before.Length - 1);
            int anchorChar;
            int focusChar;
            if (selection.IsCollapsed)
            {
                anchorChar = focusChar = SourceInsertionPosition(in projection, focusPosition);
            }
            else if (selection.Anchor < selection.Focus)
            {
                anchorChar = projection.after[anchorPosition];
                focusChar = projection.before[focusPosition];
            }
            else
            {
                anchorChar = projection.before[anchorPosition];
                focusChar = projection.after[focusPosition];
            }
            var anchor = markupViewBuffer.CharToCodepointIndex(anchorChar);
            var focus = markupViewBuffer.CharToCodepointIndex(focusChar);
            ViewText = markupViewBuffer;
            codepointCount = ViewText.CodepointCount;
            Selectable.SetSelectionInternal(new TextSelection(anchor, focus, selection.Affinity), SelectionChangeReason.Normalize);
            textDirty = true;
        }

        private void ExitMarkupView()
        {
            if (markupViewBuffer == null) return;
            var selection = Selection;
            var anchor = MarkupViewPositionToVisible(selection.Anchor);
            var focus = MarkupViewPositionToVisible(selection.Focus);
            markupViewBuffer = null;
            ViewText = document.Text;
            codepointCount = ViewText.CodepointCount;
            Selectable.SetSelectionInternal(new TextSelection(anchor, focus, selection.Affinity), SelectionChangeReason.Normalize);
            textDirty = true;
        }

        private int MarkupViewPositionToVisible(int position)
        {
            if (markupViewBuffer == null || sourceViewToDocument == null) return position;
            var sourceChar = markupViewBuffer.CodepointToCharIndex(Math.Clamp(position, 0, markupViewBuffer.CodepointCount));
            return sourceViewToDocument[Math.Clamp(sourceChar, 0, sourceViewToDocument.Length - 1)];
        }

        private TextSelection MarkupViewSelectionToVisible(in TextSelection selection)
            => new(MarkupViewPositionToVisible(selection.Anchor), MarkupViewPositionToVisible(selection.Focus),
                selection.Affinity);

        private readonly List<ChromeRule> markupChrome = new();
        private readonly List<BaseModifier> boundChromeModifiers = new();

        /// <summary>
        /// Rules styling the markup tag characters wherever they are visible — every tag under
        /// <see cref="MarkupVisibility.Raw"/>, the caret's tags under <see cref="MarkupVisibility.RevealActiveRange"/>.
        /// Each rule's <see cref="ChromeRule.Selector"/> picks which markup it targets; rules of different
        /// modifier kinds compose, same-kind rules resolve by specificity. Call <see cref="RefreshMarkup"/> after
        /// mutating at runtime.
        /// </summary>
        public List<ChromeRule> MarkupChrome => markupChrome;

        /// <summary>Re-renders after a <see cref="MarkupChrome"/> mutation.</summary>
        public void RefreshMarkup()
        {
            SyncChromeModifiers();
            TextComponent?.SetDirty(UniTextDirty.Text);
        }

        private void SyncChromeModifiers()
        {
            for (var i = boundChromeModifiers.Count - 1; i >= 0; i--)
            {
                var modifier = boundChromeModifiers[i];
                if (ContainsChromeModifier(modifier)) continue;
                modifier.Destroy();
                modifier.SetOwner(null, modifier.ChangeSink);
                boundChromeModifiers.RemoveAt(i);
            }

            var owner = TextComponent;
            if (owner == null) return;
            for (var i = 0; i < markupChrome.Count; i++)
            {
                var modifier = markupChrome[i]?.Style;
                if (modifier == null || boundChromeModifiers.Contains(modifier)) continue;
                modifier.SetOwner(owner, modifier.ChangeSink);
                boundChromeModifiers.Add(modifier);
            }
        }

        private bool ContainsChromeModifier(BaseModifier modifier)
        {
            for (var i = 0; i < markupChrome.Count; i++)
                if (ReferenceEquals(markupChrome[i]?.Style, modifier)) return true;
            return false;
        }

        private void ReleaseChromeModifiers()
        {
            for (var i = 0; i < boundChromeModifiers.Count; i++)
            {
                boundChromeModifiers[i].Destroy();
                boundChromeModifiers[i].SetOwner(null, boundChromeModifiers[i].ChangeSink);
            }
            boundChromeModifiers.Clear();
        }

        /// <summary>
        /// Pushes the render-layer projection before a parse — one whole <see cref="ParseProjection"/>:
        /// strip policy, the reveal window (the active range's source char span under
        /// <see cref="MarkupVisibility.RevealActiveRange"/>), and tag chrome. Composition suspends reveal
        /// so coordinates stay stable while the IME owns the text.
        /// </summary>
        private void ApplyMarkupRenderState(bool compositionActive)
        {
            var stripCompleted = markupVisibility != MarkupVisibility.Raw && !compositionActive;
            int revealStart = -1, revealEnd = -1;
            if (markupVisibility == MarkupVisibility.RevealActiveRange && !compositionActive)
            {
                RevealWindowChars(out revealStart, out revealEnd);
                lastRevealStart = revealStart;
                lastRevealEnd = revealEnd;
            }
            else
            {
                lastRevealStart = int.MinValue;
                lastRevealEnd = int.MinValue;
            }
            TextComponent.Projection = new ParseProjection(true, stripCompleted, revealStart, revealEnd,
                markupChrome, boundChromeModifiers);
        }

        private int lastRevealStart = int.MinValue;
        private int lastRevealEnd = int.MinValue;

        /// <summary>
        /// The active-range reveal window in source char space, canonicalised through the visible projection
        /// (start sticks left, end right) so it depends on the visible caret position, not which side a tag
        /// boundary was entered from — otherwise the revealed set flickers with caret direction. Selection
        /// endpoints are clamped to the document first: a shrinking edit can leave the selection past the
        /// (edit-patched) map for one event.
        /// </summary>
        private void RevealWindowChars(out int winStart, out int winEnd)
        {
            var sel = Selection;
            var docCount = ViewText.CodepointCount;
            var selStart = Mathf.Min(sel.Start, docCount);
            var selEnd = Mathf.Min(sel.End, docCount);
            winStart = ViewText.CodepointToCharIndex(Mathf.Clamp(SnapThroughRendered(selStart, MarkupViewStick.Before), 0, docCount));
            winEnd = ViewText.CodepointToCharIndex(Mathf.Clamp(SnapThroughRendered(selEnd, MarkupViewStick.After), 0, docCount));
        }

        /// <summary>
        /// Whether the reveal window moved since the last reparse request. Under
        /// <see cref="MarkupVisibility.RevealActiveRange"/> every selection change used to trigger a
        /// full reparse of the document; most caret movements keep the window identical (no tag
        /// boundary crossed), so gate the <see cref="UniTextDirty.Text"/> invalidation on an
        /// actual window change.
        /// </summary>
        private bool RevealWindowChanged()
        {
            RevealWindowChars(out var ws, out var we);
            if (ws == lastRevealStart && we == lastRevealEnd) return false;
            lastRevealStart = ws;
            lastRevealEnd = we;
            return true;
        }

        /// <summary>
        /// Rebuilds the synthesized view map from the parser's projected syntax ranges. Ranges arrive in
        /// source-view char space and are converted to source-view codepoints. Runs in
        /// <see cref="OnCommitted"/>, before caret geometry and context consume it.
        /// </summary>
        private void RebuildMarkupViewMap()
        {
            if (!HasMarkupView)
            {
                markupViewMap.Rebuild(codepointCount, Array.Empty<ProjectedRange>());
                return;
            }
            var parser = TextComponent != null ? TextComponent.AttributeParser : null;
            if (parser == null)
            {
                markupViewMap.Rebuild(codepointCount, Array.Empty<ProjectedRange>());
                return;
            }

            parser.CollectProjectedRanges(projectedRangeScratch);
            for (var i = 0; i < projectedRangeScratch.Count; i++)
            {
                var r = projectedRangeScratch[i];
                var cpStart = ViewText.CharToCodepointIndex(r.start);
                var cpEnd = ViewText.CharToCodepointIndex(r.End);
                projectedRangeScratch[i] = new ProjectedRange(cpStart, cpEnd - cpStart, r.visible);
            }
            markupViewMap.Rebuild(codepointCount, projectedRangeScratch);
            NormalizeMarkupViewCaret();
        }

        /// <summary>
        /// Canonicalizes a collapsed caret at hidden syntax to the document insertion boundary between every
        /// range ending and starting there. Visible source syntax retains its exact caret position.
        /// </summary>
        private void NormalizeMarkupViewCaret()
        {
            var sel = Selection;
            if (!sel.IsCollapsed) return;
            var snapped = RenderedToInsertion(markupViewMap.SourceToRendered(sel.Focus));
            if (snapped != sel.Focus) Selectable.SetSelectionInternal(TextSelection.Caret(snapped), SelectionChangeReason.Normalize);
        }

        internal int HitTestCaretSource(Vector2 screenPosition, Camera camera)
            => HasMarkupView
                ? RenderedToInsertion(TextComponent.HitTestCaret(screenPosition, camera))
                : TextComponent.HitTestCaret(screenPosition, camera);

        internal int HitTestCaretSource(Vector2 screenPosition, Camera camera, out bool upstream)
            => HasMarkupView
                ? RenderedToInsertion(TextComponent.HitTestCaret(screenPosition, camera, out upstream))
                : TextComponent.HitTestCaret(screenPosition, camera, out upstream);

        /// <summary>Caret rect for a source codepoint — maps to the rendered projection first.</summary>
        private Rect CaretRectAtSource(int source) => CalculateCaretRect(DocumentToRendered(source));

        /// <summary>Snaps a source offset to a valid caret position by round-tripping through the visible
        /// projection, so it never lands inside a hidden tag; <paramref name="stick"/> picks the side at a cluster.</summary>
        private int SnapThroughRendered(int source, MarkupViewStick stick)
            => HasMarkupView
                ? markupViewMap.RenderedToSource(markupViewMap.SourceToRendered(source), stick)
                : source;

        /// <summary>
        /// Insertion point for a collapsed-caret type. Hidden closing and opening syntax collapses to one rendered
        /// boundary, whose canonical source position lies between every range ending and starting there. The map
        /// is patched per edit, so the source position remains valid for back-to-back edits within one frame.
        /// </summary>
        private int ResolveInsertionPosition(int source)
            => !HasMarkupView || markupViewMap.SourceLength == ViewText.CodepointCount
                ? RenderedToInsertion(DocumentToRendered(source))
                : source;

        private int RenderedToInsertion(int rendered)
        {
            if (!HasMarkupView) return rendered;
            var before = markupViewMap.RenderedToSource(rendered, MarkupViewStick.Before);
            var after = markupViewMap.RenderedToSource(rendered, MarkupViewStick.After);
            if (before == after) return before;
            var documentPosition = MarkupViewPositionToVisible(before);
            var sourceChar = documentToSourceViewInsertion[
                Math.Clamp(documentPosition, 0, documentToSourceViewInsertion.Length - 1)];
            return markupViewBuffer.CharToCodepointIndex(sourceChar);
        }

        /// <summary>
        /// The source range covering exactly the visible content of <c>[visibleLo, visibleHi)</c>: the start
        /// sticks past leading hidden tags into the content, the end stops before trailing hidden tags.
        /// Width-bearing markup (inline objects, escapes) is kept — only zero-width formatting tags adjacent to
        /// an endpoint drop out. Every visible-selection-driven edit (delete, replace, format) resolves its
        /// mutation range here, so none sweeps in a neighbouring hidden tag the caret merely rested past.
        /// </summary>
        private void ResolveDocumentRange(int visibleLo, int visibleHi, out int start, out int end)
        {
            start = HasMarkupView ? markupViewMap.RenderedToSource(visibleLo, MarkupViewStick.After) : visibleLo;
            end = HasMarkupView ? markupViewMap.RenderedToSource(visibleHi, MarkupViewStick.Before) : visibleHi;
        }

        private void ResolveDocumentRange(in TextSelection sel, out int start, out int end)
            => ResolveDocumentRange(DocumentToRendered(sel.Start), DocumentToRendered(sel.End), out start, out end);

        private int DocumentToRendered(int position)
            => HasMarkupView ? markupViewMap.SourceToRendered(position) : position;

        private int RenderedToDocument(int position, MarkupViewStick stick)
            => HasMarkupView ? markupViewMap.RenderedToSource(position, stick) : position;

        private int SnapOutOfHiddenSyntax(int position, bool backward)
            => HasMarkupView ? markupViewMap.SnapOutOfHiddenSyntax(position, backward) : position;

        private int RenderedDocumentLength => HasMarkupView ? markupViewMap.VisibleLength : codepointCount;
    }
}
