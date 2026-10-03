using System;
using UnityEngine;

namespace LightSide
{
    public partial class UniTextEditable
    {
        private TextSelection compositionInputSelection;
        private UndoStack compositionReplacementUndo;
        private UndoStack compositionUndoScratch;

        internal void HandleCompositionChanged(CompositionData data)
        {
            if (readOnly) return;

            var started = false;
            if (!isComposing)
            {
                if (data.text.IsEmpty) return;

                compositionInputSelection = Selection;
                compositionOverlay.Set(
                    compositionInputSelection.Start,
                    data.text,
                    data.clauses,
                    data.cursorPosition);
                started = true;
                try
                {
                    SetComposing(true);
                    if (!isComposing) return;
                    if (!compositionInputSelection.IsCollapsed)
                    {
                        BeginCompositionReplacement(compositionInputSelection);
                        compositionOverlay.insertionCodepoint = Selection.Focus;
                    }
                    if (!isComposing) return;
                }
                catch
                {
                    compositionOverlay.Clear();
                    compositionInputSelection = default;
                    if (isComposing)
                    {
                        try
                        {
                            SetComposing(false);
                        }
                        catch (Exception cleanupError)
                        {
                            Debug.LogException(cleanupError, this);
                        }
                    }
                    throw;
                }
            }

            if (data.text.IsEmpty)
            {
                CancelComposition();
                return;
            }

            if (!started)
            {
                compositionOverlay.Set(
                    compositionOverlay.insertionCodepoint,
                    data.text,
                    data.clauses,
                    data.cursorPosition);
            }

            textDirty = true;
            selectionDirty = true;

        }

        private bool CancelComposition()
        {
            var restored = RestoreCompositionReplacement();
            var resolvedVersion = documentVersion;
            FinishCompositionState();
            SetComposing(false);
            return restored && documentVersion == resolvedVersion;
        }

        private void BeginCompositionReplacement(TextSelection selection)
        {
            var history = undoStack;
            var transient = compositionUndoScratch ?? new UndoStack();
            compositionUndoScratch = null;
            transient.Clear();
            undoStack = transient;
            compositionReplacementUndo = transient;
            var versionBefore = documentVersion;
            try
            {
                DeleteRangeUnfiltered(selection.Start, selection.Length, selection.Start,
                    selection, TextChangeReason.TypeCompose, preserveComposition: true);
            }
            catch
            {
                undoStack = history;
                if (documentVersion != versionBefore && transient.CanUndo)
                {
                    try
                    {
                        RestoreCompositionReplacement();
                    }
                    catch (Exception cleanupError)
                    {
                        Debug.LogException(cleanupError, this);
                    }
                }
                else
                {
                    compositionReplacementUndo = null;
                    transient.Clear();
                    compositionUndoScratch = transient;
                }
                compositionInputSelection = default;
                throw;
            }
            finally
            {
                undoStack = history;
            }
        }

        private bool RestoreCompositionReplacement()
        {
            var transient = compositionReplacementUndo;
            if (transient == null) return true;
            compositionReplacementUndo = null;

            var restoreMarkupView = HasMarkupView;
            if (restoreMarkupView) ExitMarkupView();
            try
            {
                var versionBefore = documentVersion;
                if (!transient.Undo(ReplaySplice, ReplayAttributedSplice, ReplayShapeCallback,
                        out var restored, out var presentationRestored))
                    throw new InvalidOperationException("The composition replacement transaction cannot be restored.");
                if (documentVersion != unchecked(versionBefore + 1)) return false;

                Selectable.SetSelectionInternal(restored, SelectionChangeReason.Restore);
                if (restoreMarkupView)
                {
                    EnterMarkupView();
                    if (presentationRestored.HasValue)
                        Selectable.SetSelectionInternal(presentationRestored.Value.Clamp(codepointCount),
                            SelectionChangeReason.Restore);
                }
                MarkDocumentChanged(TextChangeReason.Restore);
                return true;
            }
            finally
            {
                if (restoreMarkupView && !HasMarkupView) EnterMarkupView();
                transient.Clear();
                compositionUndoScratch = transient;
            }
        }

        private void EndCompositionBeforeDocumentMutation()
        {
            if (isComposing) CancelComposition();
        }

        internal void HandleCompositionEnded()
        {
            if (!isComposing) return;

            CancelComposition();
            selectionDirty = true;
        }

        private Vector2 CaretRectToScreen(Rect caretRect, out float screenLineHeight)
        {
            var screenPos = LocalToScreen(caretRect.x, caretRect.yMin);
            var screenTop = LocalToScreen(caretRect.x, caretRect.yMax);
            screenLineHeight = Mathf.Abs(screenTop.y - screenPos.y);
            if (screenLineHeight < 1f) screenLineHeight = TextComponent.CurrentFontSize;

            return screenPos;
        }

        internal void GetInputCaretScreenGeometry(out Vector2 position, out float lineHeight)
        {
            var caretRect = GetOrComputeCaretRect(GetVisibleCaretCodepoint());
            position = CaretRectToScreen(caretRect, out lineHeight);
        }

        internal bool TryGetFirstScreenRectForCharRange(int charStart, int charLength,
            out float x, out float y, out float width, out float height)
        {
            x = y = width = height = 0f;
            if (ViewText == null) return false;

            int totalChars = ViewText.Length;
            if (charStart < 0) charStart = 0;
            if (charStart > totalChars) charStart = totalChars;
            if (charLength < 0) charLength = 0;
            int charEnd = charStart + charLength;
            if (charEnd > totalChars) charEnd = totalChars;

            int codepointStart = ViewText.CharToCodepointIndex(charStart);
            var caretRect = CaretRectAtSource(codepointStart);
            var screenPos = CaretRectToScreen(caretRect, out var lineHeight);
            if (lineHeight <= 0f) return false;

            var endX = screenPos.x;
            if (charEnd > charStart)
            {
                int codepointEnd = ViewText.CharToCodepointIndex(charEnd);
                var lineEnd = FindLineEnd(codepointStart);
                if (codepointEnd > lineEnd) codepointEnd = lineEnd;
                if (codepointEnd > codepointStart)
                {
                    var endRect = CaretRectAtSource(codepointEnd);
                    endX = CaretRectToScreen(endRect, out _).x;
                }
            }

            x = Mathf.Min(screenPos.x, endX);
            y = screenPos.y;
            width = Mathf.Max(1f, Mathf.Abs(endX - screenPos.x));
            height = lineHeight;
            return true;
        }

        internal int ClosestCharIndexAtScreenPoint(float screenX, float screenY)
        {
            if (!HasLayout || ViewText == null) return -1;
            var cam = CanvasUtil.GetCanvasCamera(TextComponent.canvas);

            int codepointIndex = HitTestCaretSource(new Vector2(screenX, screenY), cam);
            if (codepointIndex < 0) return -1;
            return ViewText.CodepointToCharIndex(codepointIndex);
        }

        internal int WritingDirectionAtCharIndex(int charIndex)
        {
            if (!HasLayout || ViewText == null) return 0;
            var buffers = TextComponent.Buffers;
            if (buffers.bidiLevels.data == null || buffers.bidiLevels.count == 0) return 0;

            if (charIndex < 0) charIndex = 0;
            if (charIndex > ViewText.Length) charIndex = ViewText.Length;
            int cpIndex = DocumentToRendered(ViewText.CharToCodepointIndex(charIndex));
            cpIndex = Mathf.Clamp(cpIndex, 0, buffers.bidiLevels.count - 1);
            return buffers.IsRtlLevelAt(cpIndex) ? 2 : 1;
        }

        internal int TokenizerQuery(int charIndex, int granularity, int direction, int action)
        {
            if (ViewText == null) return -1;
            int totalChars = ViewText.Length;
            if (charIndex < 0) charIndex = 0;
            if (charIndex > totalChars) charIndex = totalChars;

            int cpIndex = ViewText.CharToCodepointIndex(charIndex);
            bool forward = direction == 0;

            switch (granularity)
            {
                case 0:
                    return TokenizerCharacter(cpIndex, forward, action);
                case 1:
                    return TokenizerWord(cpIndex, forward, action);
                default:
                    return -1;
            }
        }

        private int TokenizerCharacter(int cpIndex, bool forward, int action)
        {
            var breaks = GetGraphemeBreaks();
            int vIndex = DocumentToRendered(cpIndex);
            int vCount = RenderedDocumentLength;

            if (breaks.IsEmpty)
            {
                switch (action)
                {
                    case 0:
                        return CharForVisible(Mathf.Clamp(forward ? vIndex + 1 : vIndex - 1, 0, vCount), forward ? MarkupViewStick.After : MarkupViewStick.Before);
                    case 1:
                        return 1;
                    case 2:
                        return CharForVisible(Mathf.Clamp(forward ? vIndex : vIndex - 1, 0, vCount), MarkupViewStick.Before);
                    case 3:
                        return CharForVisible(Mathf.Clamp(forward ? vIndex + 1 : vIndex, 0, vCount), MarkupViewStick.After);
                    case 4:
                        return vIndex > 0 && vIndex < vCount ? 1 : 0;
                    default:
                        return -1;
                }
            }

            switch (action)
            {
                case 0:
                {
                    int target = forward
                        ? GraphemeNavigator.NextGraphemeCluster(breaks, vIndex)
                        : GraphemeNavigator.PreviousGraphemeCluster(breaks, vIndex);
                    return CharForVisible(target, forward ? MarkupViewStick.After : MarkupViewStick.Before);
                }
                case 1:
                    if (vIndex <= 0 || vIndex >= vCount) return 1;
                    return (vIndex < breaks.Length && breaks[vIndex]) ? 1 : 0;
                case 2:
                {
                    int start = GraphemeNavigator.PreviousGraphemeCluster(breaks, forward ? vIndex + 1 : vIndex);
                    return CharForVisible(start, MarkupViewStick.Before);
                }
                case 3:
                {
                    int end = GraphemeNavigator.NextGraphemeCluster(breaks, forward ? vIndex : vIndex - 1);
                    return CharForVisible(end, MarkupViewStick.After);
                }
                case 4:
                    return vIndex > 0 && vIndex < vCount ? 1 : 0;
                default:
                    return -1;
            }
        }

        private int CharForVisible(int visible, MarkupViewStick stick)
            => ViewText.CodepointToCharIndex(RenderedToDocument(visible, stick));

        private int TokenizerWord(int cpIndex, bool forward, int action)
        {
            int vIndex = DocumentToRendered(cpIndex);
            int vCount = RenderedDocumentLength;
            switch (action)
            {
                case 0:
                {
                    int target = forward
                        ? SelectionWordBreak.FindWordBoundaryNext(TextComponent, vIndex)
                        : SelectionWordBreak.FindWordBoundaryPrevious(TextComponent, vIndex);
                    return CharForVisible(target, forward ? MarkupViewStick.After : MarkupViewStick.Before);
                }
                case 1:
                {
                    if (vIndex <= 0 || vIndex >= vCount) return 1;
                    int prev = SelectionWordBreak.FindWordBoundaryPrevious(TextComponent, vIndex);
                    int next = SelectionWordBreak.FindWordBoundaryNext(TextComponent, vIndex);
                    return (vIndex == prev || vIndex == next) ? 1 : 0;
                }
                case 2:
                    return CharForVisible(SelectionWordBreak.GetWordRange(TextComponent, vIndex).start, MarkupViewStick.Before);
                case 3:
                    return CharForVisible(SelectionWordBreak.GetWordRange(TextComponent, vIndex).end, MarkupViewStick.After);
                case 4:
                {
                    if (vIndex <= 0 || vIndex >= vCount) return 0;
                    var (start, end) = SelectionWordBreak.GetWordRange(TextComponent, vIndex);
                    return end > start ? 1 : 0;
                }
                default:
                    return -1;
            }
        }
    }
}
