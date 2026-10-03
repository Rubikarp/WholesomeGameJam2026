using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Horizontal movement direction for caret navigation.
    /// </summary>
    internal enum CaretMoveDirection
    {
        Left,
        Right
    }

    /// <summary>
    /// Which selection endpoint a live pointer drag is moving. Drives the one auto-scroll mechanism:
    /// while the pointer sits past a viewport edge, <see cref="UniTextEditable.DragAutoScroll"/> re-applies
    /// the same operation at the held position each frame so the drag keeps extending as text scrolls in.
    /// </summary>
    internal enum DragMode
    {
        None,
        Text,
        AnchorHandle,
        FocusHandle,
        Caret
    }


    /// <summary>
    /// Caret-movement and pointer-routing partial. Pointer-state ownership lives on
    /// <see cref="UniTextSelectable"/>; this file translates Unity event-system callbacks
    /// into <see cref="UniTextSelectable"/> API calls and contains the keyboard / vertical
    /// caret navigation that is editing-pipeline specific (depends on layout / line geometry
    /// / column anchor).
    /// </summary>
    public partial class UniTextEditable
    {
        /// <summary>
        /// Desired horizontal position (in text-local coordinates) remembered across
        /// consecutive vertical caret movements. When the user presses Up/Down repeatedly,
        /// the caret tries to maintain this X position rather than drifting to the end of
        /// shorter lines.
        /// </summary>
        /// <remarks>
        /// <b>Reset rules (set to NaN):</b> horizontal arrow, word movement (Ctrl+Arrow),
        /// Home/End, Ctrl+Home/End, click, drag start, double-click word select,
        /// triple-click line select, and any typing/editing (via text change → selection dirty).
        /// <br/>
        /// <b>Preserved:</b> consecutive Up/Down arrow presses. The value is captured from
        /// the caret's current X on the first vertical move and reused until a reset event.
        /// </remarks>
        private float desiredX = float.NaN;

        private bool isDragging;
        private DragMode dragMode;

        private Vector2 lastDragScreenPosition;
        private Camera lastDragCamera;

        private const float AutoScrollRampFraction = 0.5f;
        private const float AutoScrollMaxSpeed = 1500f;

        /// <summary>
        /// Moves the caret one grapheme cluster — or one word when <paramref name="byWord"/> — in the given direction.
        /// </summary>
        /// <param name="direction">Left or Right.</param>
        /// <param name="extend">
        /// If <see langword="true"/>, extends the selection (moves focus only, anchor stays).
        /// If <see langword="false"/> and a selection exists, collapses to the appropriate edge
        /// without moving further.
        /// </param>
        /// <param name="byWord">Step by word boundary instead of grapheme cluster.</param>
        /// <remarks>
        /// Breaks undo coalescing so that subsequent edits start a new undo group.
        /// Resets <see cref="desiredX"/> (column affinity) since this is horizontal movement.
        /// </remarks>
        private void MoveCaret(CaretMoveDirection direction, bool extend, bool byWord)
        {
            undoStack.BreakCoalescing();
            desiredX = float.NaN;

            var sel = Selection;
            bool toPrev = (direction == CaretMoveDirection.Left) != IsRtlBaseAtFocus(sel.Focus, sel.Affinity);

            if (!extend && !sel.IsCollapsed)
            {
                int collapseTo = toPrev ? sel.Start : sel.End;
                collapseTo = ResolveInsertionPosition(collapseTo);
                Selectable.SetCaret(collapseTo, CaretAffinity.Downstream, SelectionChangeReason.Move);
                MarkSelectionDirty();
                return;
            }

            bool escapeEligible = !extend && !byWord && sel.IsCollapsed;
            if (escapeEligible && TryConsumeFormattingEscape(toPrev))
            {
                MarkSelectionDirty();
                return;
            }

            int newFocus;
            if (byWord)
            {
                newFocus = toPrev ? FindWordBoundaryPrevious(sel.Focus) : FindWordBoundaryNext(sel.Focus);
            }
            else
            {
                var graphemeBreaks = GetGraphemeBreaks();
                var visibleFocus = DocumentToRendered(sel.Focus);
                var visibleNew = toPrev
                    ? GraphemeNavigator.PreviousGraphemeCluster(graphemeBreaks, visibleFocus)
                    : GraphemeNavigator.NextGraphemeCluster(graphemeBreaks, visibleFocus);
                newFocus = RenderedToDocument(visibleNew, toPrev ? MarkupViewStick.Before : MarkupViewStick.After);
            }

            if (!extend) newFocus = ResolveInsertionPosition(newFocus);

            if (extend)
                Selectable.ExtendSelection(newFocus, CaretAffinity.Downstream, SelectionChangeReason.Extend);
            else
                Selectable.SetCaret(newFocus, CaretAffinity.Downstream, SelectionChangeReason.Move);

            MarkSelectionDirty();
        }

        /// <summary>
        /// Moves the caret up or down by the specified number of lines, preserving
        /// column affinity (the "desired X" position from the original horizontal location).
        /// </summary>
        private void MoveCaretVertical(int lineOffset, bool extend)
        {
            undoStack.BreakCoalescing();

            if (!HasLayout) return;

            var buffers = TextComponent.Buffers;
            var lines = buffers.lines;
            if (lines.count == 0) return;

            var sel = Selection;
            var currentLine = FindLineAtCodepoint(sel.Focus, lines, sel.Affinity == CaretAffinity.Upstream);

            var targetLine = currentLine + lineOffset;
            if (targetLine < 0) targetLine = 0;
            if (targetLine >= lines.count) targetLine = lines.count - 1;

            if (targetLine == currentLine)
            {
                int edgeFocus = lineOffset < 0 ? 0 : ViewText.CodepointCount;

                if (extend)
                    Selectable.ExtendSelection(edgeFocus, CaretAffinity.Downstream, SelectionChangeReason.Extend);
                else
                    Selectable.SetCaret(edgeFocus, CaretAffinity.Downstream, SelectionChangeReason.Move);

                MarkSelectionDirty();
                return;
            }

            if (float.IsNaN(desiredX))
                desiredX = GetCaretXPosition(sel.Focus);

            var vNewPos = SelectionHitTest.FindCodepointAtX(TextComponent, targetLine, desiredX, lines);

            CaretAffinity affinity = CaretAffinity.Downstream;
            if (vNewPos == lines[targetLine].range.End
                && targetLine + 1 < lines.count
                && vNewPos == lines[targetLine + 1].range.start)
            {
                affinity = CaretAffinity.Upstream;
            }
            var newPos = extend
                ? RenderedToDocument(vNewPos, MarkupViewStick.Before)
                : RenderedToInsertion(vNewPos);

            if (extend)
                Selectable.ExtendSelection(newPos, affinity, SelectionChangeReason.Extend);
            else
                Selectable.SetCaret(newPos, affinity, SelectionChangeReason.Move);

            MarkSelectionDirty();
        }

        /// <summary>
        /// Moves the caret to the start or end of the current line.
        /// </summary>
        private void MoveCaretToLineEdge(bool end, bool extend)
        {
            undoStack.BreakCoalescing();
            desiredX = float.NaN;

            var sel = Selection;
            bool currentUpstream = sel.Affinity == CaretAffinity.Upstream;
            int newFocus = end
                ? FindLineEnd(sel.Focus, currentUpstream)
                : FindLineStart(sel.Focus, currentUpstream);
            if (!extend) newFocus = ResolveInsertionPosition(newFocus);

            var affinity = end ? CaretAffinity.Upstream : CaretAffinity.Downstream;

            if (extend)
                Selectable.ExtendSelection(newFocus, affinity, SelectionChangeReason.Extend);
            else
                Selectable.SetCaret(newFocus, affinity, SelectionChangeReason.Move);

            MarkSelectionDirty();
        }

        /// <summary>
        /// Moves the caret to the start or end of the entire document.
        /// </summary>
        private void MoveCaretToDocEdge(bool end, bool extend)
        {
            undoStack.BreakCoalescing();
            desiredX = float.NaN;

            var newFocus = end ? ViewText.CodepointCount : 0;
            if (!extend) newFocus = ResolveInsertionPosition(newFocus);

            if (extend)
                Selectable.ExtendSelection(newFocus, CaretAffinity.Downstream, SelectionChangeReason.Extend);
            else
                Selectable.SetCaret(newFocus, CaretAffinity.Downstream, SelectionChangeReason.Move);

            MarkSelectionDirty();
        }

        /// <summary>
        /// True when the caret's line has an RTL paragraph base. ←/→ and word movement are
        /// relative to it: in an RTL-base line, Right steps toward the logical start (visually
        /// right), mirroring Android's <c>getParagraphDirection</c> flip. Counter-direction runs
        /// inside the line are not yet visually resolved (logical step within the line).
        /// </summary>
        private bool IsRtlBaseAtFocus(int focus, CaretAffinity affinity)
        {
            if (!HasLayout) return false;
            var lines = TextComponent.Buffers.lines;
            if (lines.count == 0) return false;
            var li = SelectionHitTest.FindLineAtCodepoint(DocumentToRendered(focus), lines, affinity == CaretAffinity.Upstream);
            return lines[li].IsRtl;
        }

        /// <summary>
        /// Codepoint index at the start of the line containing <paramref name="codepointIndex"/>.
        /// </summary>
        private int FindLineStart(int codepointIndex, bool upstream = false)
        {
            if (!HasLayout) return 0;

            var buffers = TextComponent.Buffers;
            if (buffers.lines.count == 0) return 0;

            var lineIndex = SelectionHitTest.FindLineAtCodepoint(DocumentToRendered(codepointIndex), buffers.lines, upstream);
            return RenderedToDocument(buffers.lines[lineIndex].range.start, MarkupViewStick.Before);
        }

        /// <summary>
        /// Codepoint index at the end of the line's visible content. If the line ends with
        /// a hard line break the returned index is at the break (before it). At a real content end it sticks
        /// past trailing hidden tags (<see cref="MarkupViewStick.After"/>) so End lands after the line's formatting;
        /// an empty line or a break-trimmed end sticks before them.
        /// </summary>
        private int FindLineEnd(int codepointIndex, bool upstream = false)
        {
            if (!HasLayout) return ViewText.CodepointCount;

            var buffers = TextComponent.Buffers;
            if (buffers.lines.count == 0) return ViewText.CodepointCount;

            var lineIndex = SelectionHitTest.FindLineAtCodepoint(DocumentToRendered(codepointIndex), buffers.lines, upstream);
            var line = buffers.lines[lineIndex];
            var visibleEnd = SelectionHitTest.LineCaretEnd(in line, buffers);
            return RenderedToDocument(visibleEnd, line.range.length != 0 && visibleEnd == line.range.End ? MarkupViewStick.After : MarkupViewStick.Before);
        }

        /// <summary>
        /// Returns grapheme break flags for the current text, or empty when layout is not
        /// ready.
        /// </summary>
        private ReadOnlySpan<bool> GetGraphemeBreaks()
        {
            return HasLayout ? TextComponent.Buffers.GraphemeBreaksOrEmpty : ReadOnlySpan<bool>.Empty;
        }

        /// <summary>Line index containing a source codepoint, mapped to the rendered projection first.</summary>
        private int FindLineAtCodepoint(int codepointIndex, PooledBuffer<TextLine> lines, bool upstream)
            => SelectionHitTest.FindLineAtCodepoint(DocumentToRendered(codepointIndex), lines, upstream);

        /// <summary>Word boundary at or before the source codepoint, returned in source coordinates.</summary>
        private int FindWordBoundaryPrevious(int codepointIndex)
            => RenderedToDocument(
                SelectionWordBreak.FindWordBoundaryPrevious(TextComponent, DocumentToRendered(codepointIndex)), MarkupViewStick.Before);

        /// <summary>Word boundary at or after the source codepoint, returned in source coordinates.</summary>
        private int FindWordBoundaryNext(int codepointIndex)
            => RenderedToDocument(
                SelectionWordBreak.FindWordBoundaryNext(TextComponent, DocumentToRendered(codepointIndex)), MarkupViewStick.After);

        /// <summary>
        /// Caret X (glyph space) for a source codepoint — the column anchor for vertical movement.
        /// Derived from <see cref="CalculateCaretRect"/> so it matches the rendered caret exactly.
        /// </summary>
        private float GetCaretXPosition(int codepointIndex)
            => CalculateCaretRect(DocumentToRendered(codepointIndex)).x - TextComponent.GetPaddedRect().xMin;

        /// <summary>
        /// Resets all transient interaction state on focus loss, both locally and on the
        /// selection component.
        /// </summary>
        private void ResetInteractionState()
        {
            desiredX = float.NaN;
            isDragging = false;
            dragMode = DragMode.None;
            Selectable?.ResetGestureState();
        }

        /// <summary>
        /// Marks the editor's frame-coalesced redraw as dirty (caret rect, scroll,
        /// frame-level <see cref="UniTextEditable.SelectionChanged"/> int/int event).
        /// Per-mutation events (highlight refresh, low-level
        /// <see cref="UniTextSelectable.SelectionChanged"/>) are emitted by the selection
        /// component itself at the time of mutation; affinity is part of the mutation, not
        /// a post-mutation reset.
        /// </summary>
        private void MarkSelectionDirty()
        {
            selectionDirty = true;
            if (caretRenderer != null)
                caretRenderer.ResetBlink();
        }

        /// <summary>
        /// Explicit caret placement shared by every pointer / handle path: breaks undo
        /// coalescing, resets the vertical-movement column anchor, sets the caret with the
        /// hit-test's affinity bit, and arms the frame-coalesced redraw.
        /// </summary>
        internal void PlaceCaret(int codepointIndex, bool upstream, string userEvent)
        {
            undoStack.BreakCoalescing();
            desiredX = float.NaN;
            Selectable.SetCaret(codepointIndex,
                upstream ? CaretAffinity.Upstream : CaretAffinity.Downstream, userEvent);
            MarkSelectionDirty();
        }

        /// <summary>
        /// Returns the event camera for the canvas, or null for screen-space overlay.
        /// </summary>
        private Camera GetEventCamera() => CanvasUtil.GetCanvasCamera(TextComponent.canvas);

        /// <summary>
        /// While a drag selection extends past a viewport edge, scrolls each frame at a speed
        /// proportional to how far the pointer is beyond the edge (both axes — a clipped
        /// single-line field follows a horizontal drag past its edge, the universal desktop
        /// text-field behavior), extending the selection to the newly revealed text. Suppresses
        /// <see cref="EnsureCaretVisible"/> so it cannot snap straight to the far caret (the
        /// instant jump it would otherwise produce).
        /// </summary>
        private void DragAutoScroll()
        {
            if (!CanScroll) return;
            if (!PointerBeyondViewport(out var direction))
                return;

            var dt = Time.unscaledDeltaTime;
            var view = ViewportRt().rect;
            if (direction.y != 0f)
                scrollOffset.y += Mathf.Sign(direction.y) * AutoScrollSpeed(Mathf.Abs(direction.y), view.height) * dt;
            if (direction.x != 0f)
                scrollOffset.x += Mathf.Sign(direction.x) * AutoScrollSpeed(Mathf.Abs(direction.x), view.width) * dt;
            ClampScrollOffset();
            ApplyScrollOffset();

            ApplyDragTo(lastDragScreenPosition, lastDragCamera);
        }

        /// <summary>
        /// Auto-scroll speed for a pointer overshoot past a viewport edge, ramped to
        /// <see cref="AutoScrollMaxSpeed"/> over <see cref="AutoScrollRampFraction"/> of the viewport
        /// EXTENT. Keying the ramp to the overshoot-as-fraction-of-viewport makes it a dimensionless ratio
        /// (canvas scale and DPI both cancel), so the same relative distance past the edge scrolls at the
        /// same rate on every device and input — a finger no longer saturates to max the instant it passes
        /// the edge, the way an absolute unit-count ramp let it.
        /// </summary>
        private static float AutoScrollSpeed(float overshoot, float viewportExtent)
            => viewportExtent > 0f
                ? AutoScrollMaxSpeed * Mathf.Clamp01(overshoot / (viewportExtent * AutoScrollRampFraction))
                : AutoScrollMaxSpeed;

        /// <summary>
        /// Lines per PageUp / PageDown step: one viewport height minus one line of overlap
        /// (the desktop convention), at least one line.
        /// </summary>
        private int PageLineCount()
        {
            var lineHeight = GetCaretLineHeight();
            if (lineHeight <= 0f) return 1;
            return Mathf.Max(1, Mathf.FloorToInt(GetViewportRect().height / lineHeight) - 1);
        }

        /// <summary>
        /// Signed overshoot of the drag pointer past the viewport edges, in viewport-local
        /// pixels per axis (magnitude = distance beyond the edge; sign = scroll direction for
        /// that axis, matching <see cref="scrollOffset"/> conventions). Zero on an axis whose
        /// edge is not crossed; <see langword="false"/> when the pointer is fully inside.
        /// </summary>
        private bool PointerBeyondViewport(out Vector2 direction)
        {
            direction = Vector2.zero;
            var viewRt = ViewportRt();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewRt, lastDragScreenPosition, lastDragCamera, out var local))
                return false;

            var rect = viewRt.rect;
            if (local.y < rect.yMin) direction.y = rect.yMin - local.y;
            else if (local.y > rect.yMax) direction.y = rect.yMax - local.y;

            if (local.x > rect.xMax) direction.x = rect.xMax - local.x;
            else if (local.x < rect.xMin) direction.x = rect.xMin - local.x;

            return direction != Vector2.zero;
        }

    }
}
