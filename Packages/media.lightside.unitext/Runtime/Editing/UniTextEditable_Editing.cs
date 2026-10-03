using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace LightSide
{
    public partial class UniTextEditable
    {
        private static readonly CatZone log = Cat.Zone("Editing");
        private readonly List<SourceAnnotation> literalAnnotationScratch = new();
        private readonly List<SourceAnnotation> typedMarkupAnnotationScratch = new();

        /// <summary>
        /// Inserts text at the current caret position, replacing any active selection.
        /// Convenience overload that accepts a string. See <see cref="InsertText(ReadOnlySpan{char})"/>.
        /// </summary>
        /// <param name="text">The text to insert. Null or empty strings are ignored.</param>
        public void InsertText(string text) => InsertText(text, TextChangeReason.Type);

        /// <summary>
        /// Inserts text at the current caret position, replacing any active selection.
        /// Applies the configured input-filter chain and records the accepted edit for undo.
        /// </summary>
        /// <param name="text">The text to insert. Empty spans are ignored.</param>
        public void InsertText(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return;
            InsertTextCore(text, null, TextChangeReason.Type);
        }

        internal void InsertText(ReadOnlySpan<char> text, string reason)
        {
            if (text.IsEmpty) return;
            InsertTextCore(text, null, reason);
        }

        internal void InsertText(string text, string reason)
        {
            if (string.IsNullOrEmpty(text)) return;
            InsertTextCore(text.AsSpan(), text, reason);
        }

        private bool InsertTextCore(ReadOnlySpan<char> text, string original, string reason,
            TextSelection? replacement = null)
        {
            if (readOnly) return false;
            EndCompositionBeforeDocumentMutation();
            var sel = replacement ?? Selection;
            if (text.Length == 0 && sel.IsCollapsed) return false;
            var exact = true;

            if (NeedsSanitize(text))
            {
                exact = false;
                var clean = new char[text.Length];
                var cleanLen = Sanitize(text, clean);
                text = clean.AsSpan(0, cleanLen);
                original = null;
                if (cleanLen == 0 && sel.IsCollapsed) return false;
            }

            if (!AcceptsNewlines)
            {
                for (var i = 0; i < text.Length; i++)
                {
                    if (!UnicodeData.IsMandatoryBreakChar(text[i])) continue;
                    exact = false;
                    original = UnicodeData.StripMandatoryBreaks(original ?? text.ToString());
                    text = original.AsSpan();
                    break;
                }
            }

            if (text.Length == 0 && sel.IsCollapsed) return false;
            int insertStart, replacedCp;
            if (sel.IsCollapsed) { insertStart = ResolveInsertionPosition(sel.Start); replacedCp = 0; }
            else { ResolveDocumentRange(sel, out insertStart, out var selEnd); replacedCp = selEnd - insertStart; }
            if (replacement.HasValue &&
                (insertStart != sel.Start || replacedCp != sel.Length))
                exact = false;

            var wrapTypingStyles = HasPendingTypingStyles && reason == TextChangeReason.Type;
            var mutationVersion = textMutationVersion;
            var insertedCodepoints = UnicodeData.CountCodepoints(text);
            var expectedSelection = TextSelection.Caret(insertStart + insertedCodepoints);

            if (InputFilter != null)
            {
                if (!RunFilteredEdit(original ?? text.ToString(), insertStart, replacedCp, reason, out var edit))
                    return false;
                exact &= textMutationVersion == mutationVersion &&
                         edit.insertCodepointIndex == insertStart &&
                         edit.replacedCodepoints == replacedCp &&
                         edit.caret == DefaultCaret &&
                         edit.text.AsSpan().SequenceEqual(text);
                var filteredCp = UnicodeData.CountCodepoints(edit.text.AsSpan());
                wrapTypingStyles &= filteredCp > 0;
                if (wrapTypingStyles) undoStack.BeginGroup(coalesces: HasMarkupView);
                var filteredRange = ApplyReplace(edit, sel, reason);
                if (wrapTypingStyles)
                {
                    ApplyPendingTypingStyles(filteredRange.Start, filteredRange.End);
                    undoStack.EndGroup();
                }
                return exact && IsExactTextMutation(mutationVersion, insertStart, replacedCp,
                    insertedCodepoints, expectedSelection);
            }

            if (wrapTypingStyles) undoStack.BeginGroup(coalesces: HasMarkupView);
            var insertedRange = ApplyEdit(insertStart, replacedCp, text, DefaultCaret, sel, reason);
            if (wrapTypingStyles)
            {
                ApplyPendingTypingStyles(insertedRange.Start, insertedRange.End);
                undoStack.EndGroup();
            }
            return exact && IsExactTextMutation(mutationVersion, insertStart, replacedCp,
                insertedCodepoints, expectedSelection);
        }

        private bool RunFilteredEdit(string text, int insertStart, int replacedCp, string reason,
            out InputEdit edit, List<SourceAnnotation> annotations = null)
        {
            var textStamp = TextStorageStamp;
            var version = documentVersion;
            var selection = Selection;
            edit = new InputEdit
            {
                text = text,
                insertCodepointIndex = insertStart,
                replacedCodepoints = replacedCp,
                document = this,
                caret = -1,
                Reason = reason,
                annotations = annotations,
            };
            inputFilter?.Invoke(ref edit);
            if (documentVersion != version || TextStorageStamp != textStamp ||
                Selection != selection)
            {
                edit = default;
                return false;
            }
            if (edit.Rejected) return false;
            return !string.IsNullOrEmpty(edit.text) || edit.replacedCodepoints != 0;
        }

        private TextSelection ApplyReplace(in InputEdit edit, TextSelection sel, string reason)
        {
            int countBefore = ViewText.CodepointCount;
            int index = Mathf.Clamp(edit.insertCodepointIndex, 0, countBefore);
            int replaced = Mathf.Clamp(edit.replacedCodepoints, 0, countBefore - index);
            return ApplyEdit(index, replaced, edit.text.AsSpan(), edit.caret >= 0 ? edit.caret : DefaultCaret,
                sel, reason);
        }

        private const int DefaultCaret = -1;
        private const int KeepCaret = -2;

        private TextSelection ApplyEdit(int index, int replacedCp, ReadOnlySpan<char> insert, int caretAfter,
            TextSelection selBefore, string reason, bool preserveComposition = false)
        {
            if (!preserveComposition) EndCompositionBeforeDocumentMutation();
            if (replacedCp < 0) replacedCp = 0;
            if (replacedCp == 0 && insert.Length == 0) return TextSelection.Caret(index);

            var docCount = ViewText.CodepointCount;
            if (index < 0 || index + replacedCp > docCount)
            {
                log.MeowErrorFormat("[UniTextEditable] Edit range [{0}, +{1}) exceeds document ({2} cp), reason={3} — clamped.",
                    index, replacedCp, docCount, reason);
                index = Mathf.Clamp(index, 0, docCount);
                replacedCp = Mathf.Clamp(replacedCp, 0, docCount - index);
                if (replacedCp == 0 && insert.Length == 0) return TextSelection.Caret(index);
            }

            if (HasMarkupView)
                return ApplyMarkupViewEdit(index, replacedCp, insert, caretAfter, selBefore, reason);

            if (typingMarkup == TypingMarkupPolicy.Parse
                && EditsTypedMarkup(reason)
                && TryApplyTypedMarkupEdit(index, replacedCp, insert, caretAfter, selBefore, reason,
                    out var typedRange))
                return typedRange;

            var insertedCp = UnicodeData.CountCodepoints(insert);
            BuildLiteralAnnotations(insert);
            var needsAttributedUndo = literalAnnotationScratch.Count > 0
                                      || replacedCp > 0 && EditNeedsAnnotationSnapshot(index, index + replacedCp);
            var stateBefore = needsAttributedUndo ? document.CaptureState() : null;
            var maxChars = replacedCp * 2;
            char[] rentedDeleted = null;
            Span<char> deletedBuffer = maxChars <= 128
                ? stackalloc char[128]
                : (rentedDeleted = ArrayPool<char>.Rent(maxChars));
            EditShape shape;
            try
            {
                var deletedLength = replacedCp > 0
                    ? ViewText.CopyCodepointRange(index, replacedCp, deletedBuffer)
                    : 0;
                var deleted = deletedBuffer.Slice(0, deletedLength);

                if (!needsAttributedUndo)
                {
                    if (replacedCp > 0 && insert.Length > 0) undoStack.RecordReplace(index, deleted, insert, selBefore);
                    else if (replacedCp > 0) undoStack.RecordDelete(index, deleted, selBefore);
                    else undoStack.RecordInsert(index, insert, selBefore);
                }

                shape = document.Replace(index, replacedCp, insert,
                    literalAnnotationScratch.Count > 0 ? literalAnnotationScratch : null);
                if (needsAttributedUndo)
                {
                    var nextCaret = caretAfter >= 0
                        ? Math.Clamp(caretAfter, 0, ViewText.CodepointCount)
                        : index + insertedCp;
                    undoStack.RecordAttributed(index, deleted, insert, stateBefore, document.CaptureState(),
                        selBefore, nextCaret);
                }
            }
            finally
            {
                if (rentedDeleted != null) ArrayPool<char>.Return(rentedDeleted);
            }
            ApplyShapeToDerivedState(in shape);

            if (caretAfter != KeepCaret)
            {
                var caret = caretAfter >= 0
                    ? Mathf.Clamp(caretAfter, 0, ViewText.CodepointCount)
                    : index + insertedCp;
                Selectable.SetSelectionInternal(TextSelection.Caret(caret), SelectionChangeReason.Input);
            }

            MarkDocumentChanged(reason);
            return new TextSelection(index, index + insertedCp, CaretAffinity.Downstream);
        }

        private void BuildLiteralAnnotations(ReadOnlySpan<char> inserted)
        {
            literalAnnotationScratch.Clear();
            if (typingMarkup != TypingMarkupPolicy.Literal || inserted.IsEmpty) return;
            AddLiteralAnnotations(inserted, literalAnnotationScratch);
        }

        private void AddLiteralAnnotations(ReadOnlySpan<char> inserted, List<SourceAnnotation> into)
        {
            if (inserted.IsEmpty) return;
            var parser = TextComponent?.AttributeParser;
            var escape = parser?.LiteralEscapeRule();
            if (escape == null) return;
            var triggers = parser.MarkupTriggers;
            var cp = 0;
            for (var i = 0; i < inserted.Length;)
            {
                var size = UnicodeData.SizeAt(inserted, i);
                if (size == 1)
                {
                    var c = inserted[i];
                    var protect = c == escape.EscapePrefix
                                  || ((triggers == null || triggers.IndexOf(c) >= 0) && escape.IsEscapable(c));
                    if (protect)
                    {
                        var literal = c.ToString();
                        into.Add(new SourceAnnotation(cp, cp + 1, null, escape, escape,
                            null, escape.EscapePrefix + literal, string.Empty, literal,
                            SourceAnnotationKind.Replacement, 0));
                    }
                }
                cp++;
                i += size;
            }
        }

        private TextSelection ApplyMarkupViewEdit(int index, int replacedCp, ReadOnlySpan<char> insert,
            int caretAfter, TextSelection selectionBefore, string reason)
        {
            var inserted = UnicodeData.CountCodepoints(insert);
            undoStack.BeginGroup(coalesces: true);
            char[] rentedRemoved = null;
            try
            {
                var maxRemovedChars = replacedCp * 2;
                Span<char> removedBuffer = maxRemovedChars <= 128
                    ? stackalloc char[128]
                    : (rentedRemoved = ArrayPool<char>.Rent(maxRemovedChars));
                var removedLength = replacedCp > 0
                    ? markupViewBuffer.CopyCodepointRange(index, replacedCp, removedBuffer)
                    : 0;
                var removed = removedBuffer.Slice(0, removedLength);
                var visibleSelectionBefore = MarkupViewSelectionToVisible(selectionBefore);
                var visibleBefore = document.Text.ToString();
                var stateBefore = document.CaptureState();
                if (replacedCp > 0) markupViewBuffer.DeleteAtCodepoint(index, replacedCp);
                if (!insert.IsEmpty) markupViewBuffer.InsertAtCodepoint(index, insert);
                var sourceAfter = markupViewBuffer.ToString();
                var documentShape = ImportMarkup(sourceAfter);

                var nextCaret = caretAfter >= 0
                    ? Math.Clamp(caretAfter, 0, markupViewBuffer.CodepointCount)
                    : index + inserted;
                var visibleCaretAfter = MarkupViewPositionToVisible(nextCaret);
                undoStack.RecordAttributedPresentation(visibleBefore.AsSpan(), document.Text.ToString().AsSpan(),
                    stateBefore, document.CaptureState(), visibleSelectionBefore, visibleCaretAfter,
                    selectionBefore, nextCaret, index, removed, insert);
                var viewShape = new EditShape(index, replacedCp, inserted);
                markupViewMap.ApplyEditShape(in viewShape);
                ApplyShapeToDerivedState(in viewShape, in documentShape);
                if (caretAfter != KeepCaret)
                    Selectable.SetSelectionInternal(TextSelection.Caret(nextCaret), SelectionChangeReason.Input);
                MarkDocumentChanged(reason);
                return new TextSelection(index, index + inserted, CaretAffinity.Downstream);
            }
            finally
            {
                if (rentedRemoved != null) ArrayPool<char>.Return(rentedRemoved);
                undoStack.EndGroup();
            }
        }

        private bool EditNeedsAnnotationSnapshot(int start, int end)
        {
            var annotations = document.Annotations;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.start >= end) break;
                if (annotation.end <= start) continue;
                if (annotation.IsAtomic || annotation.start >= start || annotation.end <= end) return true;
            }
            return false;
        }

        private static bool EditsTypedMarkup(string reason)
            => reason != null
               && (reason.StartsWith(TextChangeReason.Type, StringComparison.Ordinal)
                   || reason.StartsWith(TextChangeReason.Delete, StringComparison.Ordinal)
                   || reason == TextChangeReason.Cut);

        /// <summary>
        /// Recognizes newly typed source syntax as one atomic attributed-document transaction. Parser input is
        /// the user's visible text plus non-style source atoms; existing style spans are mapped across the parse
        /// and merged back, so formatting delimiters synthesized by export never enter Hidden editing.
        /// </summary>
        private bool TryApplyTypedMarkupEdit(int index, int replacedCp, ReadOnlySpan<char> insert,
            int caretAfter, TextSelection selectionBefore, string reason, out TextSelection insertedRange)
        {
            insertedRange = default;
            var parser = TextComponent?.AttributeParser;
            if (parser == null) return false;
            var triggers = parser.TypingTriggers;
            if (triggers != null && triggers.Length == 0) return false;
            var triggered = triggers == null;
            if (!triggered)
            {
                for (var i = 0; i < insert.Length && !triggered; i++)
                    triggered = triggers.IndexOf(insert[i]) >= 0;
            }
            if (!triggered)
            {
                var probeStart = Math.Max(0, index - 1);
                var probeEnd = Math.Min(ViewText.CodepointCount, index + replacedCp + 1);
                for (var i = probeStart; i < probeEnd && !triggered; i++)
                {
                    var codepoint = ((ITextDocument)this).GetCodepointAt(i);
                    triggered = codepoint <= char.MaxValue && triggers.IndexOf((char)codepoint) >= 0;
                }
            }
            if (!triggered) return false;

            var visibleBefore = ViewText.ToString();
            var stateBefore = document.CaptureState();
            var charStart = ViewText.CodepointToCharIndex(index);
            var charEnd = ViewText.CodepointToCharIndex(index + replacedCp);
            var editedBuilder = new StringBuilder(visibleBefore.Length - (charEnd - charStart) + insert.Length);
            editedBuilder.Append(visibleBefore, 0, charStart);
            editedBuilder.Append(insert);
            editedBuilder.Append(visibleBefore, charEnd, visibleBefore.Length - charEnd);
            var edited = editedBuilder.ToString();
            var inserted = UnicodeData.CountCodepoints(insert);
            var editedCount = ViewText.CodepointCount - replacedCp + inserted;

            document.CopyAnnotationsAfterEdit(index, replacedCp, inserted, typedMarkupAnnotationScratch);
            var projection = AttributedDocumentMarkup.BuildSyntaxInputProjection(edited,
                typedMarkupAnnotationScratch);
            var visible = AttributedDocumentMarkup.Import(parser, projection.source, importAnnotations,
                importMatches, out var sourceToVisible);

            for (var i = 0; i < typedMarkupAnnotationScratch.Count; i++)
            {
                var annotation = typedMarkupAnnotationScratch[i];
                if (annotation.kind != SourceAnnotationKind.Style) continue;
                annotation.start = MapSyntaxInputPosition(annotation.start, in projection, sourceToVisible);
                annotation.end = MapSyntaxInputPosition(annotation.end, in projection, sourceToVisible);
                if (annotation.end > annotation.start) importAnnotations.Add(annotation);
            }

            var editedCaret = caretAfter >= 0
                ? Math.Clamp(caretAfter, 0, editedCount)
                : caretAfter == KeepCaret
                    ? new EditShape(index, replacedCp, inserted).MapIndex(selectionBefore.Focus)
                    : index + inserted;
            var nextCaret = MapSyntaxInputPosition(editedCaret, in projection, sourceToVisible);
            var insertedStart = MapSyntaxInputPosition(index, in projection, sourceToVisible);
            var insertedEnd = MapSyntaxInputPosition(index + inserted, in projection, sourceToVisible);
            var shape = document.Set(visible.AsSpan(), importAnnotations);
            undoStack.RecordAttributed(visibleBefore.AsSpan(), visible.AsSpan(), stateBefore,
                document.CaptureState(), selectionBefore, nextCaret);
            ApplyShapeToDerivedState(in shape);
            if (caretAfter != KeepCaret || nextCaret != selectionBefore.Focus)
                Selectable.SetSelectionInternal(TextSelection.Caret(nextCaret), SelectionChangeReason.Input);
            MarkDocumentChanged(reason);
            insertedRange = new TextSelection(insertedStart, insertedEnd, CaretAffinity.Downstream);
            return true;
        }

        private static int MapSyntaxInputPosition(int position,
            in AttributedDocumentMarkup.Projection projection, int[] sourceToVisible)
        {
            var source = projection.insertion[Math.Clamp(position, 0, projection.insertion.Length - 1)];
            return sourceToVisible[Math.Clamp(source, 0, sourceToVisible.Length - 1)];
        }

        private int SourceInsertionPosition(in AttributedDocumentMarkup.Projection projection, int index)
            => projection.insertion[Math.Clamp(index, 0, projection.insertion.Length - 1)];

        private bool BeginCollapsedDelete(out TextSelection sel)
        {
            if (readOnly)
            {
                sel = default;
                return false;
            }
            EndCompositionBeforeDocumentMutation();
            sel = Selection;
            if (sel.IsCollapsed) return true;
            DeleteSelection();
            return false;
        }

        /// <summary>Deletes the current selection or the grapheme cluster before the caret.</summary>
        public void DeletePrevious()
        {
            if (!BeginCollapsedDelete(out var sel)) return;

            int vFocus = DocumentToRendered(sel.Focus);
            if (vFocus <= 0) return;
            var contentEnd = RenderedToDocument(vFocus, MarkupViewStick.Before);
            var prev = SnapOutOfHiddenSyntax(PreviousGraphemeInDocument(contentEnd), backward: true);
            if (prev >= contentEnd) return;

            DeleteRange(prev, contentEnd - prev, prev, sel, TextChangeReason.DeleteBackward);
        }

        /// <summary>
        /// Deletes the grapheme cluster after the caret, or deletes the current selection.
        /// Equivalent to the Delete key. Document-space cluster resolution — see
        /// <see cref="DeletePrevious"/>.
        /// </summary>
        public void DeleteNext()
        {
            if (!BeginCollapsedDelete(out var sel)) return;

            int vFocus = DocumentToRendered(sel.Focus);
            if (vFocus >= RenderedDocumentLength) return;
            var contentStart = RenderedToDocument(vFocus, MarkupViewStick.After);
            var next = SnapOutOfHiddenSyntax(NextGraphemeInDocument(contentStart), backward: false);
            if (next <= contentStart) return;

            DeleteRange(contentStart, next - contentStart, -1, sel, TextChangeReason.DeleteForward);
        }

        /// <summary>
        /// Deletes the currently selected text range and collapses the selection to the
        /// start of the deleted range. Does nothing if there is no selection.
        /// </summary>
        public void DeleteSelection() => DeleteSelection(TextChangeReason.Delete);

        internal void DeleteSelection(string reason)
        {
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            var sel = Selection;
            if (sel.IsCollapsed) return;

            ResolveDocumentRange(sel, out var start, out var end);
            if (end <= start) return;
            DeleteRange(start, end - start, start, sel, reason);
        }

        private void DeleteRange(int start, int count, int caretAfter, TextSelection sel, string reason)
        {
            if (InputFilter != null)
            {
                if (!RunFilteredEdit(string.Empty, start, count, reason, out var edit)) return;
                if (!string.IsNullOrEmpty(edit.text) || edit.insertCodepointIndex != start
                    || edit.replacedCodepoints != count || edit.caret >= 0)
                {
                    ApplyReplace(edit, sel, reason);
                    return;
                }
            }

            DeleteRangeUnfiltered(start, count, caretAfter, sel, reason);
        }

        /// <summary>
        /// Unfiltered deletion tail. Also called directly for the composition-start selection
        /// replacement — IME machinery <see cref="InputFilter"/> must never see (the committed
        /// text is filtered instead).
        /// </summary>
        internal void DeleteRangeUnfiltered(int start, int count, int caretAfter, TextSelection sel, string reason,
            bool preserveComposition = false)
        {
            if ((!isComposing || preserveComposition) && !HasMarkupView)
                ExpandDeleteOverAtomicAnnotations(ref start, ref count, ref caretAfter);
            ApplyEdit(start, count, ReadOnlySpan<char>.Empty, caretAfter >= 0 ? caretAfter : KeepCaret, sel, reason,
                preserveComposition);
        }

        /// <summary>
        /// Deletes from the caret to the previous word boundary, or deletes the selection.
        /// Equivalent to Ctrl+Backspace (Windows) / Option+Backspace (macOS).
        /// </summary>
        public void DeleteWordPrevious()
        {
            if (!BeginCollapsedDelete(out var sel)) return;

            if (sel.Focus == 0) return;

            var wordStart = FindWordBoundaryPrevious(sel.Focus);
            var deleteCount = sel.Focus - wordStart;

            if (deleteCount <= 0) return;

            DeleteRange(wordStart, deleteCount, wordStart, sel, TextChangeReason.DeleteBackward);
        }

        /// <summary>
        /// Deletes from the caret to the next word boundary, or deletes the selection.
        /// Equivalent to Ctrl+Delete (Windows) / Option+Delete (macOS).
        /// </summary>
        public void DeleteWordNext()
        {
            if (!BeginCollapsedDelete(out var sel)) return;

            var cpCount = ViewText.CodepointCount;
            if (sel.Focus >= cpCount) return;

            var wordEnd = FindWordBoundaryNext(sel.Focus);
            var deleteCount = wordEnd - sel.Focus;

            if (deleteCount <= 0) return;

            DeleteRange(sel.Focus, deleteCount, -1, sel, TextChangeReason.DeleteForward);
        }

        /// <summary>
        /// Deletes from the caret to the start of the current line.
        /// Equivalent to Cmd+Backspace (macOS).
        /// </summary>
        public void DeleteToLineStart()
        {
            if (!BeginCollapsedDelete(out var sel)) return;

            var lineStart = FindLineStart(sel.Focus);

            if (lineStart >= sel.Focus) return;

            DeleteRange(lineStart, sel.Focus - lineStart, lineStart, sel, TextChangeReason.DeleteBackward);
        }

        /// <summary>
        /// Deletes from the caret to the end of the current line.
        /// Equivalent to Ctrl+K (macOS Emacs kill-line).
        /// </summary>
        public void DeleteToLineEnd()
        {
            if (!BeginCollapsedDelete(out var sel)) return;

            var lineEnd = FindLineEnd(sel.Focus);

            if (lineEnd <= sel.Focus) return;

            DeleteRange(sel.Focus, lineEnd - sel.Focus, -1, sel, TextChangeReason.DeleteForward);
        }

        /// <summary>
        /// Swaps the two grapheme clusters before the caret. If the caret is at position 0,
        /// does nothing. If the caret is at position 1 (only one cluster before it),
        /// does nothing. Equivalent to Ctrl+T (macOS Emacs transpose-chars).
        /// </summary>
        /// <remarks>
        /// Outer boundaries stick outward (first <see cref="MarkupViewStick.Before"/>, second <see cref="MarkupViewStick.After"/>)
        /// so each cluster carries its own adjacent formatting tags through the swap; the shared inner boundary
        /// sticks <see cref="MarkupViewStick.Before"/> on both sides so the two source ranges stay contiguous.
        /// </remarks>
        public void TransposeCharacters()
        {
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();

            var sel = Selection;
            var caretPos = sel.Focus;

            if (!sel.IsCollapsed)
            {
                caretPos = sel.End;
                Selectable.SetSelectionInternal(TextSelection.Caret(caretPos), SelectionChangeReason.Input);
            }

            if (caretPos == 0) return;

            int vCaret = DocumentToRendered(caretPos);
            int vCount = RenderedDocumentLength;
            int inner, secondEnd;

            if (vCaret >= vCount)
            {
                secondEnd = RenderedToDocument(vCount, MarkupViewStick.After);
                inner = SnapOutOfHiddenSyntax(
                    PreviousGraphemeInDocument(RenderedToDocument(vCount, MarkupViewStick.Before)), backward: true);
            }
            else
            {
                inner = RenderedToDocument(vCaret, MarkupViewStick.Before);
                secondEnd = SnapOutOfHiddenSyntax(
                    NextGraphemeInDocument(RenderedToDocument(vCaret, MarkupViewStick.After)), backward: false);
                secondEnd = SnapThroughRendered(secondEnd, MarkupViewStick.After);
            }

            if (inner <= 0) return;
            var firstStart = SnapOutOfHiddenSyntax(PreviousGraphemeInDocument(inner), backward: true);
            firstStart = SnapThroughRendered(firstStart, MarkupViewStick.Before);

            var firstCount = inner - firstStart;
            var secondCount = secondEnd - inner;

            if (firstCount <= 0 || secondCount <= 0) return;

            var firstMaxChars = firstCount * 2;
            var secondMaxChars = secondCount * 2;
            var scratchChars = (firstMaxChars + secondMaxChars) * 2;
            var totalCodepoints = firstCount + secondCount;

            char[] rented = null;
            Span<char> scratch = scratchChars <= 128
                ? stackalloc char[128]
                : (rented = ArrayPool<char>.Rent(scratchChars));
            try
            {
                var firstLen = ViewText.CopyCodepointRange(firstStart, firstCount, scratch.Slice(0, firstMaxChars));
                var secondLen = ViewText.CopyCodepointRange(inner, secondCount, scratch.Slice(firstMaxChars, secondMaxChars));

                var transposed = scratch.Slice(firstMaxChars + secondMaxChars, firstLen + secondLen);
                scratch.Slice(firstMaxChars, secondLen).CopyTo(transposed);
                scratch.Slice(0, firstLen).CopyTo(transposed.Slice(secondLen));

                if (InputFilter != null)
                {
                    if (!RunFilteredEdit(new string(transposed), firstStart, totalCodepoints, TextChangeReason.Type, out var edit))
                        return;
                    ApplyReplace(edit, Selection, TextChangeReason.Type);
                    return;
                }

                ApplyEdit(firstStart, totalCodepoints, transposed, firstStart + totalCodepoints, Selection, TextChangeReason.Type);
            }
            finally
            {
                if (rented != null) ArrayPool<char>.Return(rented);
            }
        }

        /// <summary>
        /// Copies the selected text to the system clipboard. Does nothing if there is no
        /// selection or if the field is in password mode. Walks every built-in clipboard
        /// adapter and writes the resulting items as one
        /// atomic multi-format clipboard transaction (plain text + custom UniText format +
        /// optional HTML / Markdown), so the consumer's paste picks the richest format it
        /// understands. When the selection's markup is visible to the user — Raw, or a revealed
        /// range under <see cref="MarkupVisibility.RevealActiveRange"/> — the copy carries only that
        /// visible text as plain text and no semantic channels: what is on screen is what pastes.
        /// </summary>
        public void Copy()
        {
            var sel = Selection;
            if (sel.IsCollapsed) return;
            if (!IsCopyAllowed()) return;
            var adapters = ActiveAdapters;
            if (adapters.Count == 0) return;

            var selStart = sel.Start;
            var selEnd = sel.Start + sel.Length;
            var plain = GetRenderedSelectionText(selStart, selEnd);

            if (!SelectionCarriesConcealedSyntax(selStart, selEnd))
            {
                if (string.IsNullOrEmpty(plain)) return;
                UniTextClipboard.Provider.SetItems(new List<ClipboardItem>(1) { new ClipboardItem(ClipboardFormat.PlainText, plain) });
                return;
            }

            var documentSelection = HasMarkupView ? MarkupViewSelectionToVisible(sel) : sel;
            var context = BuildClipboardContext(documentSelection.Start, documentSelection.End,
                plain, selStart, sel.Length);

            var items = new List<ClipboardItem>(adapters.Count);
            for (int i = 0; i < adapters.Count; i++)
            {
                var adapter = adapters[i];
                if (adapter == null) continue;
                var payload = adapter.SerializeCopy(context);
                if (string.IsNullOrEmpty(payload)) continue;
                items.Add(new ClipboardItem(adapter.Format, payload));
            }

            if (items.Count == 0) return;
            UniTextClipboard.Provider.SetItems(items);
        }

        private readonly List<SourceAnnotation> copyAnnotationScratch = new();
        private readonly List<ClipboardSpan> copySpanScratch = new();
        private GapBuffer copyTextScratch;

        /// <summary>
        /// Builds every clipboard representation from one clipped attributed-document snapshot. Source syntax
        /// is an output for custom adapters; built-in rich formats consume <see cref="ClipboardCopyContext.VisibleText"/>
        /// and <see cref="ClipboardCopyContext.Spans"/> directly and never parse it back.
        /// </summary>
        private ClipboardCopyContext BuildClipboardContext(int selStart, int selEnd, string plain,
            int viewStart, int viewLength)
        {
            var visible = document.Text.GetCodepointRange(selStart, selEnd - selStart);
            copyTextScratch ??= new GapBuffer(Math.Max(64, visible.Length));
            copyTextScratch.SetText(visible.AsSpan());
            copyAnnotationScratch.Clear();
            copySpanScratch.Clear();

            var selectionCharStart = document.Text.CodepointToCharIndex(selStart);
            var annotations = document.Annotations;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.end <= selStart) continue;
                if (annotation.start >= selEnd) break;
                if (annotation.IsAtomic && (annotation.start < selStart || annotation.end > selEnd)) continue;

                var start = Math.Max(annotation.start, selStart);
                var end = Math.Min(annotation.end, selEnd);
                annotation.start = start - selStart;
                annotation.end = end - selStart;
                copyAnnotationScratch.Add(annotation);
                if (annotation.modifier == null || end <= start) continue;

                var offset = document.Text.CodepointToCharIndex(start) - selectionCharStart;
                var length = document.Text.CodepointToCharIndex(end)
                             - document.Text.CodepointToCharIndex(start);
                var sourceRule = annotation.sourceRule ?? annotation.rule;
                copySpanScratch.Add(new ClipboardSpan(offset, length, annotation.modifier,
                    annotation.rule, annotation.parameter, annotation.IsAtomic,
                    sourceRule?.SourceToken));
            }

            copySpanScratch.Sort(static (a, b) => a.Offset != b.Offset
                ? a.Offset.CompareTo(b.Offset)
                : (b.Offset + b.Length).CompareTo(a.Offset + a.Length));
            var source = AttributedDocumentMarkup.Export(copyTextScratch, copyAnnotationScratch);
            return new ClipboardCopyContext(this, source, plain, visible, copySpanScratch.ToArray(),
                viewStart, viewLength);
        }

        /// <summary>
        /// The selection's visible text exactly as the user sees it — the same source→visible projection the
        /// synthesized markup view uses, so hidden tags drop out while revealed
        /// (<see cref="MarkupVisibility.RevealActiveRange"/>) and Raw tags stay as the literal characters on
        /// screen. This is what the plain-text clipboard channel carries.
        /// </summary>
        private string GetRenderedSelectionText(int selStart, int selEnd)
        {
            if (!HasMarkupView)
                return ViewText.GetCodepointRange(selStart, selEnd - selStart);
            var regions = markupViewMap.Regions;
            if (regions == null || regions.Count == 0)
                return ViewText.GetCodepointRange(selStart, selEnd - selStart);

            var sb = new System.Text.StringBuilder(selEnd - selStart);
            var cursor = selStart;
            for (var i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r.End <= cursor) continue;
                if (r.start >= selEnd) break;
                if (r.start > cursor) sb.Append(ViewText.GetCodepointRange(cursor, r.start - cursor));
                if (r.visible != null) sb.Append(r.visible);
                cursor = r.End;
                if (cursor >= selEnd) break;
            }
            if (cursor < selEnd) sb.Append(ViewText.GetCodepointRange(cursor, selEnd - cursor));
            return sb.ToString();
        }

        /// <summary>
        /// Whether the selection's content is covered by a recognised markup pair whose tags are stripped from the
        /// render — the signal that the copy must carry semantic channels. A selection inside a styled run counts
        /// even when it touches none of the tag characters. False under Raw and for revealed pairs, where the user
        /// sees the markup as text.
        /// </summary>
        private bool SelectionCarriesConcealedSyntax(int selStart, int selEnd)
        {
            if (!HasMarkupView)
            {
                var annotations = document.Annotations;
                for (var i = 0; i < annotations.Count; i++)
                {
                    var annotation = annotations[i];
                    if (annotation.start >= selEnd) break;
                    if (annotation.end > selStart) return true;
                }
                return false;
            }
            if (!TryGetMarkupViewMatches(out _, out var matches)) return false;

            for (var i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                if ((!m.IsComplete && !m.range.IsSelfClosing) || m.modifier == null) continue;
                var spanStart = ViewText.CharToCodepointIndex(m.range.IsSelfClosing ? m.range.openStart : m.range.start);
                var spanEnd = ViewText.CharToCodepointIndex(m.range.IsSelfClosing ? m.range.openEnd : m.range.end);
                if (spanEnd <= selStart || spanStart >= selEnd) continue;
                if (markupViewMap.IsInsideHiddenSyntax(ViewText.CharToCodepointIndex(m.range.openStart), out _)) return true;
            }
            return false;
        }

        /// <summary>
        /// Cuts the selected text to the system clipboard (copy + delete selection).
        /// Does nothing if there is no selection or if the field is in password mode.
        /// </summary>
        public void Cut()
        {
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            if (Selection.IsCollapsed) return;
            if (!IsCopyAllowed()) return;

            Copy();
            DeleteSelection(TextChangeReason.Cut);
        }

        /// <summary>
        /// Pastes text from the system clipboard at the current caret position,
        /// replacing any active selection. Probes the built-in clipboard adapters
        /// by <see cref="IClipboardAdapter.Priority"/>
        /// descending — a cheap <see cref="IClipboardProvider.HasFormat"/> availability
        /// check, then one payload transfer, then deserialize; the first success wins,
        /// so only one format ever crosses the clipboard. UniText source (lossless
        /// custom format) wins over HTML / Markdown, which win over plain text. The
        /// adapter resolves the payload to visible text and destination formatting spans,
        /// then inserts both in one document transaction. A format transfers only when
        /// the field has a style that renders it; unmatched formatting degrades to plain
        /// text. Plain text inserts verbatim.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Applies a hard upper bound on paste size to prevent out-of-memory conditions: a
        /// safety cap of 1,000,000 characters before insertion. Any attached
        /// <see cref="LengthLimitBehavior"/> then truncates to the real limit. This prevents
        /// <see cref="GapBuffer.EnsureCapacity"/> from doubling to 128MB+ when an
        /// extremely large clipboard payload (e.g. 100MB+ log file) is pasted.
        /// </para>
        /// </remarks>
        public void Paste()
        {
            if (readOnly) return;
            if (!RichPaste) { PastePlain(); return; }

            var provider = UniTextClipboard.Provider;
            if (provider == null) return;

            if (MediaReceived != null)
            {
                var content = new MediaContent(this, provider, MediaSource.Paste);
                MediaReceived(ref content);
                if (content.Handled) return;
            }

            if (ResolveDisplayMask(out _))
            {
                PastePlain();
                return;
            }

            var ordered = SortAdaptersByPriority();
            var pasteContext = new ClipboardPasteContext(this);
            for (int i = 0; i < ordered.Count; i++)
            {
                var adapter = ordered[i];
                if (adapter == null) continue;
                if (!provider.HasFormat(adapter.Format)) continue;
                if (!provider.TryGetText(adapter.Format, out var payload) || string.IsNullOrEmpty(payload)) continue;
                if (TryPasteAdapter(adapter, payload, pasteContext)) return;
            }
        }

        /// <summary>
        /// Pastes the plain-text clipboard channel only, ignoring rich formats — the
        /// paste-without-formatting command (Ctrl+Shift+V / Shift+Cmd+V).
        /// </summary>
        public void PastePlain()
        {
            if (readOnly) return;

            var provider = UniTextClipboard.Provider;
            if (provider == null) return;
            if (!provider.TryGetText(ClipboardFormat.PlainText, out var plain)) return;

            PasteFromItems(new[] { new ClipboardItem(ClipboardFormat.PlainText, plain) });
        }

        internal void DispatchPaste(bool plain)
        {
            _ = plain ? PastePlainAsync() : PasteAsync();
        }

        /// <summary>
        /// Async variant of <see cref="Paste"/>. Required on WebGL for programmatic paste
        /// (toolbar icon, context menu, autofill). This is the batched collect-all read: every
        /// requested format arrives in ONE underlying clipboard access via
        /// <see cref="IAsyncClipboardProvider.GetItemsAsync"/> (WebGL's single-user-activation
        /// rule allows exactly one read); must be awaited from a user-activation context on
        /// WebGL. A provider without the async seam degrades to the sync <see cref="Paste"/>
        /// probe. Runs the same pipeline, including the <see cref="MediaReceived"/> hook.
        /// </summary>
        public async Task PasteAsync()
        {
            if (readOnly) return;
            if (!RichPaste) { await PastePlainAsync(); return; }

            var provider = UniTextClipboard.Provider;
            if (provider == null) return;

            if (provider is not IAsyncClipboardProvider asyncProvider)
            {
                Paste();
                return;
            }

            if (MediaReceived != null)
            {
                var content = new MediaContent(this, provider, MediaSource.Paste);
                MediaReceived(ref content);
                if (content.Handled) return;
            }

            var adapters = ActiveAdapters;
            if (adapters.Count == 0) return;

            var formats = new List<ClipboardFormat>(adapters.Count);
            for (int i = 0; i < adapters.Count; i++)
                if (adapters[i] != null) formats.Add(adapters[i].Format);
            var items = await asyncProvider.GetItemsAsync(formats);

            if (this == null) return;

            PasteFromItems(items);
        }

        /// <summary>
        /// Async variant of <see cref="PastePlain"/> — the WebGL-safe paste-without-formatting
        /// command. Same activation-context contract as <see cref="PasteAsync"/>.
        /// </summary>
        public async Task PastePlainAsync()
        {
            if (readOnly) return;

            var provider = UniTextClipboard.Provider;
            if (provider == null) return;

            if (provider is not IAsyncClipboardProvider asyncProvider)
            {
                PastePlain();
                return;
            }

            plainFormatRequest ??= new List<ClipboardFormat>(1) { ClipboardFormat.PlainText };
            var items = await asyncProvider.GetItemsAsync(plainFormatRequest);

            if (this == null) return;

            var plain = FindItemText(items, ClipboardFormat.PlainText);
            if (string.IsNullOrEmpty(plain)) return;
            PasteFromItems(new[] { new ClipboardItem(ClipboardFormat.PlainText, plain) });
        }

        private List<ClipboardFormat> plainFormatRequest;

        /// <summary>
        /// Pastes pre-extracted clipboard items through the adapter pipeline, bypassing
        /// <see cref="UniTextClipboard.Provider"/>. Used by iOS <c>UIPasteControl</c> and
        /// drag-drop drop sites to avoid a second pasteboard read (which on iOS would
        /// surface the system permission prompt). A format transfers only when the field
        /// has a style that renders it.
        /// </summary>
        public void PasteFromItems(IReadOnlyList<ClipboardItem> items)
        {
            if (readOnly) return;
            if (items == null || items.Count == 0) return;

            if (ResolveDisplayMask(out _))
            {
                var masked = FindItemText(items, ClipboardFormat.PlainText);
                if (!string.IsNullOrEmpty(masked))
                {
                    if (EffectivePlainTextPaste == PlainTextPastePolicy.Parse) InsertPasteText(masked);
                    else InsertPasteContent(new ClipboardPasteContent(masked));
                }
                return;
            }

            var ordered = SortAdaptersByPriority();
            var pasteContext = new ClipboardPasteContext(this);
            for (int i = 0; i < ordered.Count; i++)
            {
                var adapter = ordered[i];
                if (adapter == null) continue;

                var payload = FindItemText(items, adapter.Format);
                if (string.IsNullOrEmpty(payload)) continue;
                if (TryPasteAdapter(adapter, payload, pasteContext)) return;
            }
        }

        private List<IClipboardAdapter> SortAdaptersByPriority()
        {
            var ordered = new List<IClipboardAdapter>(ActiveAdapters);
            ordered.Sort(static (a, b) =>
            {
                if (a == null) return b == null ? 0 : 1;
                if (b == null) return -1;
                return b.Priority.CompareTo(a.Priority);
            });
            return ordered;
        }

        /// <summary>Deserializes one adapter's payload and inserts it, applying the plain-text
        /// literal policy. False when the payload does not deserialize — the caller falls
        /// through to the next-best format.</summary>
        private bool TryPasteAdapter(IClipboardAdapter adapter, string payload, ClipboardPasteContext context)
        {
            var content = adapter.DeserializePaste(payload, context);
            if (content == null || string.IsNullOrEmpty(content.Text)) return false;

            if (adapter.Format == ClipboardFormat.PlainText
                && EffectivePlainTextPaste == PlainTextPastePolicy.Parse)
                InsertPasteText(content.Text);
            else
                InsertPasteContent(content);
            return true;
        }

        private PlainTextPastePolicy plainTextPaste = PlainTextPastePolicy.Auto;

        /// <summary>
        /// How plain-text paste is interpreted: <see cref="PlainTextPastePolicy.Literal"/> inserts it verbatim,
        /// <see cref="PlainTextPastePolicy.Parse"/> reparses it as this field's markup,
        /// <see cref="PlainTextPastePolicy.Auto"/> (default) picks by <see cref="MarkupVisibility"/>.
        /// </summary>
        public PlainTextPastePolicy PlainTextPaste { get => plainTextPaste; set => plainTextPaste = value; }

        /// <summary>
        /// Whether paste reads the formatting channels (UniText source, HTML, Markdown). Off — every
        /// paste inserts the clipboard's plain text only, regardless of what the source app offers.
        /// </summary>
        public bool RichPaste { get; set; } = true;

        private PlainTextPastePolicy EffectivePlainTextPaste
            => plainTextPaste != PlainTextPastePolicy.Auto ? plainTextPaste
                : MarkupVisibility == MarkupVisibility.Raw ? PlainTextPastePolicy.Parse : PlainTextPastePolicy.Literal;

        private TypingMarkupPolicy typingMarkup = TypingMarkupPolicy.Parse;

        /// <summary>Whether hand-typed markup is parsed or kept literal — see <see cref="TypingMarkupPolicy"/>. Default <see cref="TypingMarkupPolicy.Parse"/>.</summary>
        public TypingMarkupPolicy TypingMarkup { get => typingMarkup; set => typingMarkup = value; }

        private static string FindItemText(IReadOnlyList<ClipboardItem> items, ClipboardFormat format)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Format == format) return items[i].Text;
            }
            return null;
        }

        private GapBuffer pasteTextScratch;

        private void InsertPasteContent(ClipboardPasteContent content)
        {
            var text = PreparePasteText(content.Text, out var sourceOffset);
            if (text.Length == 0) return;
            EndCompositionBeforeDocumentMutation();

            undoStack.BreakCoalescing();
            var restoreMarkupView = HasMarkupView;
            if (restoreMarkupView) ExitMarkupView();
            try
            {
                BuildPasteAnnotations(content.Spans.Span, sourceOffset, text);
                var selection = Selection;
                ResolveDocumentRange(selection, out var start, out var end);
                var replaced = end - start;
                var original = text;
                if (InputFilter != null)
                {
                    if (!RunFilteredEdit(text, start, replaced, TextChangeReason.Paste,
                            out var edit, pasteAnnotationScratch)) return;
                    RemapPasteAnnotations(original, in edit, start, replaced);
                    text = edit.text ?? string.Empty;
                    start = Math.Clamp(edit.insertCodepointIndex, 0, document.Text.CodepointCount);
                    replaced = Math.Clamp(edit.replacedCodepoints, 0,
                        document.Text.CodepointCount - start);
                    AddLiteralAnnotations(text.AsSpan(), pasteAnnotationScratch);
                    ApplyAttributedPaste(text, start, replaced, edit.caret, selection);
                }
                else
                {
                    AddLiteralAnnotations(text.AsSpan(), pasteAnnotationScratch);
                    ApplyAttributedPaste(text, start, replaced, DefaultCaret, selection);
                }
            }
            finally
            {
                if (restoreMarkupView) EnterMarkupView();
                undoStack.BreakCoalescing();
            }
        }

        private static string PreparePasteText(string text, out int sourceOffset)
        {
            sourceOffset = 0;
            while (sourceOffset < text.Length && text[sourceOffset] is
                   (char)UnicodeData.ByteOrderMark or
                   (char)UnicodeData.ZeroWidthSpace or
                   (char)UnicodeData.ReversedByteOrderMark)
                sourceOffset++;

            var source = text.AsSpan(sourceOffset);
            var length = Utf16.SafePrefixLength(source, ClipboardBudget.MaxOutputChars);
            return sourceOffset == 0 && length == text.Length
                ? text
                : length > 0 ? text.Substring(sourceOffset, length) : string.Empty;
        }

        private void BuildPasteAnnotations(ReadOnlySpan<ClipboardSpan> spans, int sourceOffset,
            string text)
        {
            pasteAnnotationScratch.Clear();
            if (spans.IsEmpty) return;
            pasteTextScratch ??= new GapBuffer(Math.Max(64, text.Length));
            pasteTextScratch.SetText(text.AsSpan());
            var sourceEnd = sourceOffset + text.Length;
            for (var i = 0; i < spans.Length; i++)
            {
                var span = spans[i];
                var spanEnd = span.Offset + span.Length;
                if (spanEnd <= sourceOffset || span.Offset >= sourceEnd) continue;
                if (span.IsAtomic && (span.Offset < sourceOffset || spanEnd > sourceEnd)) continue;
                var charStart = Math.Max(span.Offset, sourceOffset) - sourceOffset;
                var charEnd = Math.Min(spanEnd, sourceEnd) - sourceOffset;
                var start = pasteTextScratch.CharToCodepointIndex(charStart);
                var end = pasteTextScratch.CharToCodepointIndex(charEnd);
                if (end <= start) continue;

                if (span.IsAtomic)
                {
                    var sourceRule = CompositeParseRule.FindLeaf<TagParseRule>(span.Rule);
                    var prefix = sourceRule?.SelfClosing(span.Parameter);
                    if (string.IsNullOrEmpty(prefix)) continue;
                    pasteAnnotationScratch.Add(new SourceAnnotation(start, end, span.Modifier,
                        span.Rule, sourceRule, span.Parameter, prefix, null,
                        text.Substring(charStart, charEnd - charStart),
                        SourceAnnotationKind.Replacement, i));
                }
                else
                {
                    pasteAnnotationScratch.Add(new SourceAnnotation(start, end, span.Modifier,
                        span.Rule, span.Rule, span.Parameter, null, null, null,
                        SourceAnnotationKind.Style, i));
                }
            }
        }

        private void RemapPasteAnnotations(string original, in InputEdit edit,
            int originalStart, int originalReplaced)
        {
            if (edit.insertCodepointIndex != originalStart
                || edit.replacedCodepoints != originalReplaced)
            {
                pasteAnnotationScratch.Clear();
                return;
            }
            var filtered = edit.text ?? string.Empty;
            if (filtered == original) return;
            var originalCount = UnicodeData.CountCodepoints(original.AsSpan());
            var filteredCount = UnicodeData.CountCodepoints(filtered.AsSpan());
            if (filteredCount == originalCount) return;
            if (!original.StartsWith(filtered, StringComparison.Ordinal))
            {
                pasteAnnotationScratch.Clear();
                return;
            }
            for (var i = pasteAnnotationScratch.Count - 1; i >= 0; i--)
            {
                var annotation = pasteAnnotationScratch[i];
                if (annotation.start >= filteredCount
                    || annotation.IsAtomic && annotation.end > filteredCount)
                {
                    pasteAnnotationScratch.RemoveAt(i);
                    continue;
                }
                annotation.end = Math.Min(annotation.end, filteredCount);
                if (annotation.end <= annotation.start) pasteAnnotationScratch.RemoveAt(i);
                else pasteAnnotationScratch[i] = annotation;
            }
        }

        private void ApplyAttributedPaste(string text, int start, int replaced, int caretAfter,
            TextSelection selection)
        {
            if (text.Length == 0 && replaced == 0) return;
            var visibleBefore = document.Text.ToString();
            var stateBefore = document.CaptureState();
            var shape = document.Replace(start, replaced, text.AsSpan(),
                pasteAnnotationScratch.Count > 0 ? pasteAnnotationScratch : null);
            var caret = caretAfter >= 0
                ? Math.Clamp(caretAfter, 0, document.Text.CodepointCount)
                : start + UnicodeData.CountCodepoints(text.AsSpan());
            undoStack.RecordAttributed(visibleBefore.AsSpan(), document.Text.ToString().AsSpan(),
                stateBefore, document.CaptureState(), selection, caret);
            ApplyShapeToDerivedState(in shape);
            Selectable.SetSelectionInternal(TextSelection.Caret(caret), SelectionChangeReason.Input);
            MarkDocumentChanged(TextChangeReason.Paste);
        }

        private void InsertPasteText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            text = PreparePasteText(text, out _);
            if (text.Length == 0) return;

            undoStack.BreakCoalescing();
            InsertMarkupText(text, TextChangeReason.Paste);
            undoStack.BreakCoalescing();
        }

        /// <summary>Per-splice shape consumer for undo/redo replay — a group replays several splices,
        /// each of which must patch derived indexed state before the next applies.</summary>
        private readonly List<SourceAnnotation> pasteAnnotationScratch = new();
        private readonly List<AttributeParser.MarkupMatch> pasteMatchScratch = new();

        private void InsertMarkupText(string source, string reason)
        {
            EndCompositionBeforeDocumentMutation();
            if (HasMarkupView)
            {
                InsertText(source, reason);
                return;
            }

            var parser = TextComponent?.AttributeParser;
            if (parser == null)
            {
                InsertText(source, reason);
                return;
            }

            var visible = AttributedDocumentMarkup.Import(parser, source, pasteAnnotationScratch, pasteMatchScratch);
            var selection = Selection;
            ResolveDocumentRange(selection, out var start, out var end);
            var replaced = end - start;
            if (InputFilter != null)
            {
                if (!RunFilteredEdit(visible, start, replaced, reason, out var edit)) return;
                if (edit.insertCodepointIndex != start || edit.replacedCodepoints != replaced || edit.text != visible)
                {
                    ApplyReplace(edit, selection, reason);
                    return;
                }
            }

            var visibleBefore = document.Text.ToString();
            var stateBefore = document.CaptureState();
            var shape = document.Replace(start, replaced, visible.AsSpan(), pasteAnnotationScratch);
            var caret = start + UnicodeData.CountCodepoints(visible.AsSpan());
            undoStack.RecordAttributed(visibleBefore.AsSpan(), document.Text.ToString().AsSpan(), stateBefore,
                document.CaptureState(), selection, caret);
            ApplyShapeToDerivedState(in shape);
            Selectable.SetSelectionInternal(TextSelection.Caret(caret), SelectionChangeReason.Input);
            MarkDocumentChanged(reason);
        }

        private Action<EditShape> replayShapeCallback;

        private Action<EditShape> ReplayShapeCallback
            => replayShapeCallback ??= shape => ApplyShapeToDerivedState(in shape);

        private bool ReplaySplice(int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add, out EditShape shape)
        {
            var removed = UnicodeData.CountCodepoints(remove);
            if (index < 0 || index + removed > ViewText.CodepointCount)
            {
                shape = default;
                return false;
            }
            shape = document.Replace(index, removed, add);
            return true;
        }

        private bool ReplayAttributedSplice(int index, ReadOnlySpan<char> remove, ReadOnlySpan<char> add,
            AttributedDocumentState state, out EditShape shape)
        {
            var removed = UnicodeData.CountCodepoints(remove);
            if (index < 0 || index + removed > ViewText.CodepointCount
                || !ViewText.GetCodepointRange(index, removed).AsSpan().SequenceEqual(remove))
            {
                shape = default;
                return false;
            }
            shape = document.Replace(index, removed, add, state);
            return true;
        }

        /// <summary>Undoes the most recent edit (a whole transaction group counts as one edit) and
        /// restores the pre-edit selection (anchor / focus / affinity).</summary>
        public void Undo()
        {
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            var restoreMarkupView = HasMarkupView;
            if (restoreMarkupView) ExitMarkupView();
            if (!undoStack.Undo(ReplaySplice, ReplayAttributedSplice, ReplayShapeCallback, out var restored,
                    out var presentationRestored))
            {
                if (restoreMarkupView) EnterMarkupView();
                return;
            }
            Selectable.SetSelectionInternal(restored, SelectionChangeReason.Restore);
            if (restoreMarkupView)
            {
                EnterMarkupView();
                if (presentationRestored.HasValue)
                    Selectable.SetSelectionInternal(presentationRestored.Value.Clamp(codepointCount),
                        SelectionChangeReason.Restore);
            }
            MarkDocumentChanged(TextChangeReason.Restore);
        }

        /// <summary>Redoes the most recently undone edit (a whole transaction group counts as one edit),
        /// placing the caret at the end of the re-applied text.</summary>
        public void Redo()
        {
            if (readOnly) return;
            EndCompositionBeforeDocumentMutation();
            var restoreMarkupView = HasMarkupView;
            if (restoreMarkupView) ExitMarkupView();
            if (!undoStack.Redo(ReplaySplice, ReplayAttributedSplice, ReplayShapeCallback, out var caretAfter,
                    out var presentationCaretAfter))
            {
                if (restoreMarkupView) EnterMarkupView();
                return;
            }
            Selectable.SetSelectionInternal(TextSelection.Caret(caretAfter), SelectionChangeReason.Restore);
            if (restoreMarkupView)
            {
                EnterMarkupView();
                if (presentationCaretAfter.HasValue)
                    Selectable.SetSelectionInternal(TextSelection.Caret(
                            Math.Clamp(presentationCaretAfter.Value, 0, codepointCount)),
                        SelectionChangeReason.Restore);
            }
            MarkDocumentChanged(TextChangeReason.Restore);
        }

        /// <summary>
        /// Selects the entire text content (anchor = 0, focus = codepointCount).
        /// </summary>
        public void SelectAll()
        {
            Selectable.SelectAll();
            MarkSelectionDirty();
        }

        /// <summary>
        /// Returns true if the span contains anything <see cref="Sanitize"/> must rewrite or drop:
        /// unpaired (lone) surrogates or a disallowed control character (carriage return included).
        /// </summary>
        private bool NeedsSanitize(ReadOnlySpan<char> text)
        {
            for (var i = 0; i < text.Length;)
            {
                int size = UnicodeData.SizeAt(text, i);
                var c = text[i];
                if (size == 1 && (char.IsSurrogate(c) || IsDisallowedControl(c)))
                    return true;
                i += size;
            }

            return false;
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="dest"/>, rewriting every CRLF pair
        /// and lone carriage return as a single line feed and dropping lone surrogates and disallowed
        /// control characters. Returns the number of chars written.
        /// </summary>
        private int Sanitize(ReadOnlySpan<char> source, Span<char> dest)
        {
            var written = 0;
            for (var i = 0; i < source.Length;)
            {
                int size = UnicodeData.SizeAt(source, i);
                var c = source[i];
                if (size == 2)
                {
                    dest[written++] = c;
                    dest[written++] = source[i + 1];
                }
                else if (c == (char)UnicodeData.CarriageReturn)
                {
                    if (!UnicodeData.IsCrlfAt(source, i)) dest[written++] = (char)UnicodeData.LineFeed;
                }
                else if (!char.IsSurrogate(c) && !IsDisallowedControl(c))
                {
                    dest[written++] = c;
                }
                i += size;
            }
            return written;
        }

        private bool IsDisallowedControl(char c)
            => UnicodeData.IsC0ControlOrDelete(c) && c != '\t' && c != '\n';

        private const int GraphemeContextWindow = 64;

        private int PreviousGraphemeInDocument(int codepointIndex)
        {
            var docCount = ViewText.CodepointCount;
            if (codepointIndex > docCount) codepointIndex = docCount;
            if (codepointIndex <= 0) return 0;

            var windowStart = Mathf.Max(0, codepointIndex - GraphemeContextWindow);
            var len = codepointIndex - windowStart;
            Span<int> cps = stackalloc int[GraphemeContextWindow];
            DecodeDocumentRange(windowStart, len, cps);
            Span<bool> breaks = stackalloc bool[GraphemeContextWindow + 1];
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(cps.Slice(0, len), breaks.Slice(0, len + 1));

            for (var i = len - 1; i >= 1; i--)
                if (breaks[i]) return windowStart + i;
            return windowStart;
        }

        /// <summary>Forward counterpart of <see cref="PreviousGraphemeInDocument"/>.</summary>
        private int NextGraphemeInDocument(int codepointIndex)
        {
            var docCount = ViewText.CodepointCount;
            if (codepointIndex < 0) codepointIndex = 0;
            if (codepointIndex >= docCount) return docCount;

            var windowStart = Mathf.Max(0, codepointIndex - GraphemeContextWindow);
            var windowEnd = Mathf.Min(docCount, codepointIndex + GraphemeContextWindow);
            var len = windowEnd - windowStart;
            Span<int> cps = stackalloc int[GraphemeContextWindow * 2];
            DecodeDocumentRange(windowStart, len, cps);
            Span<bool> breaks = stackalloc bool[GraphemeContextWindow * 2 + 1];
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(cps.Slice(0, len), breaks.Slice(0, len + 1));

            var rel = codepointIndex - windowStart;
            for (var i = rel + 1; i < len; i++)
                if (breaks[i]) return windowStart + i;
            return windowEnd;
        }

        private void DecodeDocumentRange(int startCodepoint, int codepointCount, Span<int> into)
        {
            Span<char> chars = stackalloc char[GraphemeContextWindow * 4];
            var written = ViewText.CopyCodepointRange(startCodepoint, codepointCount, chars);
            var k = 0;
            for (var i = 0; i < written;)
            {
                into[k++] = (int)UnicodeData.DecodeAt(chars, i, out var size);
                i += size;
            }
        }
    }
}
