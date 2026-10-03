using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    internal enum SourceAnnotationKind : byte
    {
        Style,
        Replacement,
        Protection
    }

    internal struct SourceAnnotation
    {
        public int start;
        public int end;
        public BaseModifier modifier;
        public ParseRule rule;
        public ParseRule sourceRule;
        public string parameter;
        public string prefix;
        public string suffix;
        public string replacement;
        public SourceAnnotationKind kind;
        public long order;

        public readonly bool IsAtomic => kind == SourceAnnotationKind.Replacement;

        public SourceAnnotation(int start, int end, BaseModifier modifier, ParseRule rule,
            ParseRule sourceRule, string parameter, string prefix, string suffix, string replacement,
            SourceAnnotationKind kind, long order)
        {
            this.start = start;
            this.end = end;
            this.modifier = modifier;
            this.rule = rule;
            this.sourceRule = sourceRule;
            this.parameter = parameter;
            this.prefix = prefix;
            this.suffix = suffix;
            this.replacement = replacement;
            this.kind = kind;
            this.order = order;
        }
    }

    /// <summary>
    /// Immutable attribution state retained by an undo entry. The optional source is only the
    /// document's already-materialized lossless serialization cache; capturing a state never exports.
    /// </summary>
    internal sealed class AttributedDocumentState
    {
        public readonly SourceAnnotation[] annotations;
        public readonly string source;

        public int EstimatedBytes => annotations.Length * 80;

        public AttributedDocumentState(SourceAnnotation[] annotations, string source)
        {
            this.annotations = annotations ?? Array.Empty<SourceAnnotation>();
            this.source = source;
        }
    }

    /// <summary>Visible text and the source constructs that produced its attributed ranges.</summary>
    internal sealed class AttributedDocument
    {
        private readonly GapBuffer text;
        private readonly List<SourceAnnotation> annotations = new();
        private long nextOrder;
        private int version;
        private string sourceText;

        public GapBuffer Text => text;
        public IReadOnlyList<SourceAnnotation> Annotations => annotations;
        public bool IsInitialized { get; private set; }
        public int Version => version;
        public string SourceText => sourceText ??= AttributedDocumentMarkup.Export(text, annotations);

        public AttributedDocument(int capacity = 64)
        {
            text = new GapBuffer(capacity);
        }

        public AttributedDocumentState CaptureState()
        {
            var snapshot = new SourceAnnotation[annotations.Count];
            annotations.CopyTo(snapshot);
            return new AttributedDocumentState(snapshot, sourceText);
        }

        public EditShape Set(ReadOnlySpan<char> visibleText, List<SourceAnnotation> sourceAnnotations, string source = null)
        {
            var shape = text.SetText(visibleText);
            annotations.Clear();
            if (sourceAnnotations != null)
            {
                for (var i = 0; i < sourceAnnotations.Count; i++)
                {
                    var annotation = sourceAnnotations[i];
                    annotation.order = nextOrder++;
                    annotations.Add(annotation);
                }
                annotations.Sort(CompareAnnotations);
            }
            sourceText = source;
            IsInitialized = true;
            version++;
            return shape;
        }

        public EditShape Replace(int start, int removed, ReadOnlySpan<char> inserted,
            IReadOnlyList<SourceAnnotation> insertedAnnotations = null)
        {
            var count = text.CodepointCount;
            start = Math.Clamp(start, 0, count);
            removed = Math.Clamp(removed, 0, count - start);
            var insertedCount = UnicodeData.CountCodepoints(inserted);
            if (removed == 0 && insertedCount == 0 && (insertedAnnotations == null || insertedAnnotations.Count == 0))
                return default;

            var reorder = RemapAnnotations(start, removed, insertedCount);
            if (removed > 0) text.DeleteAtCodepoint(start, removed);
            if (!inserted.IsEmpty) text.InsertAtCodepoint(start, inserted);

            if (insertedAnnotations != null)
            {
                for (var i = 0; i < insertedAnnotations.Count; i++)
                {
                    var annotation = insertedAnnotations[i];
                    annotation.start += start;
                    annotation.end += start;
                    annotation.order = nextOrder++;
                    annotations.Add(annotation);
                }
                reorder = insertedAnnotations.Count > 0 || reorder;
            }

            if (reorder) annotations.Sort(CompareAnnotations);
            if (removed > 0 || insertedAnnotations != null && insertedAnnotations.Count > 0)
                MergeAdjacentStyles();
            sourceText = null;
            version++;
            return new EditShape(start, removed, insertedCount);
        }

        public EditShape Replace(int start, int removed, ReadOnlySpan<char> inserted,
            AttributedDocumentState restoredState)
        {
            var count = text.CodepointCount;
            start = Math.Clamp(start, 0, count);
            removed = Math.Clamp(removed, 0, count - start);
            var insertedCount = UnicodeData.CountCodepoints(inserted);
            if (removed > 0) text.DeleteAtCodepoint(start, removed);
            if (!inserted.IsEmpty) text.InsertAtCodepoint(start, inserted);

            annotations.Clear();
            if (restoredState != null)
                annotations.AddRange(restoredState.annotations);
            sourceText = restoredState?.source;
            version++;
            return new EditShape(start, removed, insertedCount);
        }

        public void AddAnnotation(SourceAnnotation annotation)
        {
            annotation.start = Math.Clamp(annotation.start, 0, text.CodepointCount);
            annotation.end = Math.Clamp(annotation.end, annotation.start, text.CodepointCount);
            annotation.order = nextOrder++;
            annotations.Add(annotation);
            annotations.Sort(CompareAnnotations);
            MergeAdjacentStyles();
            sourceText = null;
            version++;
        }

        /// <summary>
        /// Copies attribution as it would look after an edit without mutating the document. Typed-syntax
        /// recognition uses this to stage one atomic text-plus-annotations transaction through the same boundary
        /// mapping as an ordinary replacement.
        /// </summary>
        public void CopyAnnotationsAfterEdit(int start, int removed, int inserted,
            List<SourceAnnotation> destination)
        {
            destination.Clear();
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (TryRemapAnnotation(ref annotation, start, removed, inserted))
                    destination.Add(annotation);
            }
            destination.Sort(CompareAnnotations);
        }

        public bool RemoveStyles(int start, int end, Predicate<SourceAnnotation> matches)
        {
            var changed = false;
            for (var i = annotations.Count - 1; i >= 0; i--)
            {
                var annotation = annotations[i];
                if (annotation.kind != SourceAnnotationKind.Style || annotation.end <= start || annotation.start >= end)
                    continue;
                if (matches != null && !matches(annotation)) continue;

                annotations.RemoveAt(i);
                changed = true;
                var canSplit = annotation.prefix == null
                    ? annotation.rule?.CanWrap == true
                    : annotation.suffix != null;
                if (!canSplit) continue;
                if (annotation.start < start)
                {
                    var before = annotation;
                    before.end = start;
                    annotations.Add(before);
                }
                if (annotation.end > end)
                {
                    var after = annotation;
                    after.start = end;
                    annotations.Add(after);
                }
            }
            if (!changed) return false;
            annotations.Sort(CompareAnnotations);
            MergeAdjacentStyles();
            sourceText = null;
            version++;
            return true;
        }

        public int RemoveAnnotations(Predicate<SourceAnnotation> predicate)
        {
            var removed = annotations.RemoveAll(predicate);
            if (removed > 0)
            {
                sourceText = null;
                version++;
            }
            return removed;
        }

        public bool ExtendStylesEndingAt(int position, int end, Predicate<SourceAnnotation> matches)
        {
            var changed = false;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.start > position) break;
                if (annotation.kind != SourceAnnotationKind.Style || annotation.end != position
                    || matches != null && !matches(annotation)) continue;
                annotation.end = end;
                annotations[i] = annotation;
                changed = true;
            }
            if (!changed) return false;
            sourceText = null;
            version++;
            return true;
        }

        public bool ExtendStylesStartingAt(int position, int start, Predicate<SourceAnnotation> matches)
        {
            var changed = false;
            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (annotation.start > position) break;
                if (annotation.kind != SourceAnnotationKind.Style || annotation.start != position
                    || matches != null && !matches(annotation)) continue;
                annotation.start = start;
                annotations[i] = annotation;
                changed = true;
            }
            if (!changed) return false;
            annotations.Sort(CompareAnnotations);
            sourceText = null;
            version++;
            return true;
        }

        private bool RemapAnnotations(int editStart, int removed, int inserted)
        {
            var reorder = false;
            var editEnd = editStart + removed;
            for (var i = annotations.Count - 1; i >= 0; i--)
            {
                var annotation = annotations[i];
                var oldStart = annotation.start;
                var oldEnd = annotation.end;
                if (!TryRemapAnnotation(ref annotation, editStart, removed, inserted))
                {
                    annotations.RemoveAt(i);
                    reorder = true;
                    continue;
                }
                annotations[i] = annotation;
                if (removed > 0 && oldEnd > editStart && oldStart < editEnd)
                    reorder = true;
            }
            return reorder;
        }

        private static bool TryRemapAnnotation(ref SourceAnnotation annotation, int editStart,
            int removed, int inserted)
        {
            if (removed == 0)
            {
                if (annotation.end <= editStart) return true;
                if (annotation.start >= editStart)
                {
                    annotation.start += inserted;
                    annotation.end += inserted;
                }
                else if (!annotation.IsAtomic)
                {
                    annotation.end += inserted;
                }
                return true;
            }

            var editEnd = editStart + removed;
            var delta = inserted - removed;
            if (annotation.end <= editStart) return true;
            if (annotation.start >= editEnd)
            {
                annotation.start += delta;
                annotation.end += delta;
                return true;
            }
            if (annotation.IsAtomic) return false;

            annotation.start = annotation.start < editStart ? annotation.start : editStart;
            annotation.end = annotation.end > editEnd ? annotation.end + delta : editStart + inserted;
            return annotation.end > annotation.start;
        }

        private static int CompareAnnotations(SourceAnnotation a, SourceAnnotation b)
        {
            var byStart = a.start.CompareTo(b.start);
            if (byStart != 0) return byStart;
            var byEnd = b.end.CompareTo(a.end);
            return byEnd != 0 ? byEnd : b.order.CompareTo(a.order);
        }

        private void MergeAdjacentStyles()
        {
            for (var i = annotations.Count - 1; i > 0; i--)
            {
                var right = annotations[i];
                var left = annotations[i - 1];
                if (left.kind != SourceAnnotationKind.Style || right.kind != SourceAnnotationKind.Style
                    || left.end != right.start || !ReferenceEquals(left.modifier, right.modifier)
                    || !ReferenceEquals(left.rule, right.rule) || !ReferenceEquals(left.sourceRule, right.sourceRule)
                    || left.order != right.order
                    || left.parameter != right.parameter
                    || left.prefix != right.prefix || left.suffix != right.suffix) continue;
                left.end = right.end;
                annotations[i - 1] = left;
                annotations.RemoveAt(i);
            }
        }
    }

    internal static class AttributedDocumentMarkup
    {
        internal readonly struct Projection
        {
            public readonly string source;
            public readonly int[] before;
            public readonly int[] insertion;
            public readonly int[] after;
            public readonly int[] sourceViewToDocument;

            public Projection(string source, int[] before, int[] insertion, int[] after)
            {
                this.source = source;
                this.before = before;
                this.insertion = insertion;
                this.after = after;
                sourceViewToDocument = BuildSourceViewToDocument(source.Length, before);
            }
        }

        private readonly struct Removal
        {
            public readonly int start;
            public readonly int end;

            public Removal(int start, int end)
            {
                this.start = start;
                this.end = end;
            }
        }

        public static string Import(AttributeParser parser, string source, List<SourceAnnotation> annotations,
            List<AttributeParser.MarkupMatch> matches)
            => Import(parser, source, annotations, matches, out _, out _);

        public static string Import(AttributeParser parser, string source, List<SourceAnnotation> annotations,
            List<AttributeParser.MarkupMatch> matches, out int[] sourceViewToDocument)
            => Import(parser, source, annotations, matches, out sourceViewToDocument, out _);

        public static string Import(AttributeParser parser, string source, List<SourceAnnotation> annotations,
            List<AttributeParser.MarkupMatch> matches, out int[] sourceViewToDocument,
            out int[] documentToSourceViewInsertion)
        {
            annotations.Clear();
            matches.Clear();
            if (string.IsNullOrEmpty(source))
            {
                sourceViewToDocument = new[] { 0 };
                documentToSourceViewInsertion = new[] { 0 };
                return string.Empty;
            }

            parser.CollectMarkupMatches(source.AsSpan(), matches, includeSelfClosing: true);
            var removals = new List<Removal>(matches.Count * 2);
            var insertionStarts = new int[matches.Count];
            var insertionEnds = new int[matches.Count];
            Array.Fill(insertionStarts, -1);

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.IsComplete) continue;
                ref readonly var range = ref match.range;
                if (range.openEnd > range.openStart)
                    removals.Add(new Removal(range.openStart, range.openEnd));
                if (!range.IsSelfClosing && range.closeStart >= 0 && range.closeEnd > range.closeStart)
                    removals.Add(new Removal(range.closeStart, range.closeEnd));
            }
            removals.Sort(static (a, b) => a.start.CompareTo(b.start));

            var boundaries = new int[source.Length + 1];
            var visible = new StringBuilder(source.Length);
            var removalIndex = 0;
            var removedUntil = -1;
            var cp = 0;
            var insertion = new List<int> { 0 };
            var matchAt = new Dictionary<int, int>();
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (match.IsComplete && match.range.IsSelfClosing)
                    matchAt[match.range.openStart] = i;
            }

            for (var i = 0; i < source.Length;)
            {
                boundaries[i] = cp;
                while (removalIndex < removals.Count && removals[removalIndex].start <= i)
                {
                    if (removals[removalIndex].end > removedUntil) removedUntil = removals[removalIndex].end;
                    removalIndex++;
                }

                if (matchAt.TryGetValue(i, out var matchIndex))
                {
                    var replacement = matches[matchIndex].range.insertString ?? string.Empty;
                    insertionStarts[matchIndex] = cp;
                    visible.Append(replacement);
                    var replacementCount = UnicodeData.CountCodepoints(replacement.AsSpan());
                    cp += replacementCount;
                    for (var replacementCp = 0; replacementCp < replacementCount; replacementCp++)
                        insertion.Add(matches[matchIndex].range.openEnd);
                    insertionEnds[matchIndex] = cp;
                }

                if (i < removedUntil)
                {
                    i++;
                    continue;
                }

                var size = UnicodeData.SizeAt(source.AsSpan(), i);
                visible.Append(source, i, size);
                cp++;
                insertion.Add(i + size);
                for (var c = 1; c <= size; c++) boundaries[i + c] = cp;
                i += size;
            }
            boundaries[source.Length] = cp;
            sourceViewToDocument = boundaries;

            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.IsComplete) continue;
                ref readonly var range = ref match.range;
                var sourceRule = range.sourceRule ?? match.rule;
                var selfClosing = range.IsSelfClosing;
                var start = selfClosing ? insertionStarts[i] : boundaries[Math.Clamp(range.start, 0, source.Length)];
                var end = selfClosing ? insertionEnds[i] : boundaries[Math.Clamp(range.end, 0, source.Length)];
                var prefix = Slice(source, range.openStart, range.openEnd);
                var suffix = range.closeStart >= 0 ? Slice(source, range.closeStart, range.closeEnd) : null;
                var kind = selfClosing
                    ? SourceAnnotationKind.Replacement
                    : match.modifier == null ? SourceAnnotationKind.Protection : SourceAnnotationKind.Style;
                annotations.Add(new SourceAnnotation(start, end, match.modifier, match.rule, sourceRule,
                    range.Parameters.ResolvedValue, prefix, suffix, range.insertString, kind, i));

                if (selfClosing)
                {
                    if (end > start) insertion[end] = Math.Max(insertion[end], range.openEnd);
                }
                else if (end > start && range.closeEnd > range.closeStart)
                {
                    insertion[end] = Math.Max(insertion[end], range.closeEnd);
                }
            }

            documentToSourceViewInsertion = insertion.ToArray();
            return visible.ToString();
        }

        public static string Export(GapBuffer text, IReadOnlyList<SourceAnnotation> annotations)
            => BuildProjection(text, annotations).source;

        public static Projection BuildProjection(GapBuffer text, IReadOnlyList<SourceAnnotation> annotations)
            => BuildProjection(text.ToString(), annotations, includeStyles: true);

        /// <summary>
        /// Builds transient parser input from visible text and source-bearing atoms / protection only. Persistent
        /// styles stay out so their export delimiters cannot participate in newly typed syntax.
        /// </summary>
        public static Projection BuildSyntaxInputProjection(string visible,
            IReadOnlyList<SourceAnnotation> annotations)
            => BuildProjection(visible, annotations, includeStyles: false);

        private static Projection BuildProjection(string visible, IReadOnlyList<SourceAnnotation> annotations,
            bool includeStyles)
        {
            var annotationCount = 0;
            if (annotations != null)
            {
                for (var i = 0; i < annotations.Count; i++)
                    if (includeStyles || annotations[i].kind != SourceAnnotationKind.Style)
                        annotationCount++;
            }
            if (annotationCount == 0)
            {
                var identity = BuildCodepointMap(visible);
                return new Projection(visible, identity, identity, identity);
            }

            var cpToChar = BuildCodepointMap(visible);
            var before = new int[cpToChar.Length];
            var insertion = new int[cpToChar.Length];
            var after = new int[cpToChar.Length];
            var starts = new List<SourceAnnotation>[cpToChar.Length];
            var ends = new List<SourceAnnotation>[cpToChar.Length];
            var oneSided = new List<SourceAnnotation>[cpToChar.Length];
            var empty = new List<SourceAnnotation>[cpToChar.Length];
            var replacements = new Dictionary<int, SourceAnnotation>();

            for (var i = 0; i < annotations.Count; i++)
            {
                var annotation = annotations[i];
                if (!includeStyles && annotation.kind == SourceAnnotationKind.Style) continue;
                if (annotation.kind == SourceAnnotationKind.Style && annotation.prefix == null)
                {
                    var sourceRule = annotation.sourceRule ?? annotation.rule;
                    if (sourceRule == null
                        || !TryGetExportSyntax(sourceRule, annotation.parameter, out annotation.prefix,
                            out annotation.suffix))
                        continue;
                }
                var start = Math.Clamp(annotation.start, 0, cpToChar.Length - 1);
                var end = Math.Clamp(annotation.end, start, cpToChar.Length - 1);
                if (annotation.IsAtomic)
                {
                    replacements[start] = annotation;
                    continue;
                }
                if (start == end)
                {
                    (empty[start] ??= new List<SourceAnnotation>()).Add(annotation);
                    continue;
                }
                if (string.IsNullOrEmpty(annotation.suffix))
                {
                    (oneSided[start] ??= new List<SourceAnnotation>()).Add(annotation);
                    continue;
                }
                (starts[start] ??= new List<SourceAnnotation>()).Add(annotation);
                (ends[end] ??= new List<SourceAnnotation>()).Add(annotation);
            }

            var result = new StringBuilder(visible.Length + annotationCount * 4);
            var active = new List<SourceAnnotation>();
            var stack = new List<SourceAnnotation>();
            for (var cp = 0; cp < cpToChar.Length; cp++)
            {
                before[cp] = result.Length;
                if (ends[cp] != null)
                {
                    for (var i = 0; i < ends[cp].Count; i++)
                        RemoveByOrder(active, ends[cp][i].order);
                }
                TransitionStack(result, stack, active);
                insertion[cp] = result.Length;
                if (starts[cp] != null)
                {
                    active.AddRange(starts[cp]);
                    active.Sort(CompareStackOrder);
                }
                TransitionStack(result, stack, active);

                if (oneSided[cp] != null)
                {
                    oneSided[cp].Sort(CompareStackOrder);
                    for (var i = 0; i < oneSided[cp].Count; i++) result.Append(oneSided[cp][i].prefix);
                }
                if (empty[cp] != null)
                {
                    empty[cp].Sort(CompareStackOrder);
                    for (var i = 0; i < empty[cp].Count; i++) result.Append(empty[cp][i].prefix);
                    for (var i = empty[cp].Count - 1; i >= 0; i--) result.Append(empty[cp][i].suffix);
                }
                after[cp] = result.Length;
                if (cp == cpToChar.Length - 1) break;
                if (replacements.TryGetValue(cp, out var replacement))
                {
                    result.Append(replacement.prefix);
                    for (var covered = cp + 1; covered < replacement.end && covered < before.Length; covered++)
                        before[covered] = insertion[covered] = after[covered] = result.Length;
                    cp = Math.Max(cp, replacement.end - 1);
                    continue;
                }
                result.Append(visible, cpToChar[cp], cpToChar[cp + 1] - cpToChar[cp]);
            }
            return new Projection(result.ToString(), before, insertion, after);
        }

        private static void TransitionStack(StringBuilder result, List<SourceAnnotation> stack,
            List<SourceAnnotation> target)
        {
            var common = 0;
            while (common < stack.Count && common < target.Count && stack[common].order == target[common].order)
                common++;
            for (var i = stack.Count - 1; i >= common; i--) result.Append(stack[i].suffix);
            if (common < stack.Count) stack.RemoveRange(common, stack.Count - common);
            for (var i = common; i < target.Count; i++)
            {
                result.Append(target[i].prefix);
                stack.Add(target[i]);
            }
        }

        private static int CompareStackOrder(SourceAnnotation a, SourceAnnotation b)
        {
            var byStart = a.start.CompareTo(b.start);
            if (byStart != 0) return byStart;
            var byEnd = b.end.CompareTo(a.end);
            return byEnd != 0 ? byEnd : b.order.CompareTo(a.order);
        }

        private static void RemoveByOrder(List<SourceAnnotation> annotations, long order)
        {
            for (var i = annotations.Count - 1; i >= 0; i--)
            {
                if (annotations[i].order != order) continue;
                annotations.RemoveAt(i);
                return;
            }
        }

        private static int[] BuildCodepointMap(string text)
        {
            var count = UnicodeData.CountCodepoints(text.AsSpan());
            var map = new int[count + 1];
            for (var i = 0; i <= count; i++) map[i] = i;
            UnicodeData.MapCodepointsToChars(text.AsSpan(), map, map);
            return map;
        }

        private static int[] BuildSourceViewToDocument(int sourceLength, int[] documentToSourceView)
        {
            var map = new int[sourceLength + 1];
            var document = 0;
            for (var source = 0; source <= sourceLength; source++)
            {
                while (document + 1 < documentToSourceView.Length
                       && documentToSourceView[document + 1] <= source)
                    document++;
                map[source] = document;
            }
            return map;
        }

        private static string Slice(string source, int start, int end)
        {
            start = Math.Clamp(start, 0, source.Length);
            end = Math.Clamp(end, start, source.Length);
            return source.Substring(start, end - start);
        }

        /// <summary>
        /// Extracts delimiters only while serializing a programmatically-created span. Imported spans retain
        /// their exact source tokens, and editing never inserts the probe or any returned syntax into text.
        /// </summary>
        private static bool TryGetExportSyntax(ParseRule rule, string parameter, out string prefix,
            out string suffix)
        {
            const char contentAnchor = '\uFFFF';
            Span<char> content = stackalloc char[1];
            content[0] = contentAnchor;
            var source = rule.Apply(content, parameter);
            var split = source?.IndexOf(contentAnchor) ?? -1;
            if (split < 0)
            {
                prefix = null;
                suffix = null;
                return false;
            }
            prefix = source.Substring(0, split);
            suffix = source.Substring(split + 1);
            return true;
        }
    }
}
