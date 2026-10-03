using System;

namespace LightSide
{
    /// <summary>
    /// Incremental grapheme-cluster count for a document — the per-keystroke companion of
    /// <see cref="TextMeasure.Count(ITextDocument, TextLengthUnit)"/>. The count is cached by
    /// <see cref="ITextDocument.Version"/>; <see cref="PredictCount"/> evaluates a pending edit by
    /// re-segmenting only a boundary-safe window around it, and <see cref="CommitEdit"/> promotes
    /// that prediction to the new cached count when the applied <see cref="EditShape"/> matches.
    /// Any mutation the cache was not told about degrades to one full recount on the next query.
    /// </summary>
    /// <remarks>
    /// Window edges are placed on boundaries that UAX #29 guarantees regardless of surrounding
    /// context (no RI-parity, ZWJ-emoji, or Indic-conjunct chain can cross them), so clusters
    /// outside the window are provably unaffected by the edit. When no safe edge exists within
    /// <see cref="MaxAnchorScan"/> codepoints (a pathological run of joiners), the prediction
    /// falls back to per-part arithmetic and the commit invalidates instead.
    /// </remarks>
    public struct GraphemeCountCache
    {
        private const int MaxAnchorScan = 64;
        private const int StackCodepointLimit = 256;

        private int version;
        private int count;
        private bool valid;

        private bool hasPrediction;
        private int predictedStart;
        private int predictedRemoved;
        private int predictedInserted;
        private int predictedCount;
        private int predictedHash;

        /// <summary>
        /// Grapheme-cluster count of the whole document. O(1) while the cached version matches;
        /// one full segmentation (pooled, allocation-free steady state) otherwise.
        /// </summary>
        public int Count(ITextDocument document)
        {
            if (valid && version == document.Version) return count;
            count = Recount(document);
            version = document.Version;
            valid = true;
            hasPrediction = false;
            return count;
        }

        /// <summary>
        /// Grapheme-cluster count of the document AFTER applying the proposed edit, computed from a
        /// boundary-safe window around it — never a whole-document pass while the cache is warm.
        /// Follow up with <see cref="CommitEdit"/> once the edit is applied so the prediction
        /// becomes the cached count.
        /// </summary>
        public int PredictCount(ITextDocument document, TextRange replacedRange, ReadOnlySpan<char> inserted)
        {
            var current = Count(document);
            var docCount = document.CodepointCount;
            var editStart = Math.Clamp(replacedRange.start, 0, docCount);
            var editEnd = Math.Clamp(replacedRange.End, editStart, docCount);
            var insertedCp = UnicodeData.CountCodepoints(inserted);

            hasPrediction = false;
            var predicted = PredictWindowed(document, editStart, editEnd, inserted, insertedCp, docCount, current);
            if (predicted < 0)
            {
                return current
                       - TextMeasure.CountRange(document, editStart, editEnd - editStart, TextLengthUnit.Graphemes)
                       + TextMeasure.Count(inserted, TextLengthUnit.Graphemes);
            }

            hasPrediction = true;
            predictedStart = editStart;
            predictedRemoved = editEnd - editStart;
            predictedInserted = insertedCp;
            predictedCount = predicted;
            predictedHash = HashInserted(inserted);
            return predicted;
        }

        /// <summary>
        /// Applies a mutation to the cached count: an edit matching the last prediction — same shape
        /// AND same inserted content (a later hook may have rewritten the text) — promotes it;
        /// anything else invalidates, deferring to a full recount on the next query.
        /// </summary>
        public void CommitEdit(ITextDocument document, in EditShape shape)
        {
            if (hasPrediction && valid
                && shape.Start == predictedStart
                && shape.Removed == predictedRemoved
                && shape.Inserted == predictedInserted
                && predictedHash == HashApplied(document, shape.Start, shape.Inserted))
            {
                count = predictedCount;
                version = document.Version;
            }
            else if (version != document.Version)
            {
                valid = false;
            }
            hasPrediction = false;
        }

        private static int HashInserted(ReadOnlySpan<char> inserted)
        {
            var hash = unchecked((int)2166136261);
            for (var offset = 0; offset < inserted.Length;)
            {
                var cp = (int)UnicodeData.DecodeAt(inserted, offset, out var size);
                hash = unchecked((hash ^ cp) * 16777619);
                offset += size;
            }
            return hash;
        }

        private static int HashApplied(ITextDocument document, int start, int codepointCount)
        {
            var hash = unchecked((int)2166136261);
            for (var i = 0; i < codepointCount; i++)
                hash = unchecked((hash ^ document.GetCodepointAt(start + i)) * 16777619);
            return hash;
        }

        /// <summary>Discards all cached state.</summary>
        public void Invalidate()
        {
            valid = false;
            hasPrediction = false;
        }

        private static int PredictWindowed(
            ITextDocument document, int editStart, int editEnd,
            ReadOnlySpan<char> inserted, int insertedCp, int docCount, int currentCount)
        {
            int[] rented = null;
            var oldReach = editEnd + Math.Min(MaxAnchorScan + 1, docCount - editEnd);
            var oldBase = editStart - Math.Min(MaxAnchorScan + 1, editStart);
            var oldSpanLen = oldReach - oldBase;
            var newSpanMax = oldSpanLen + insertedCp;
            var totalNeeded = oldSpanLen + newSpanMax;

            Span<int> scratch = totalNeeded <= StackCodepointLimit
                ? stackalloc int[StackCodepointLimit]
                : (rented = ArrayPool<int>.Rent(totalNeeded));

            var oldCps = scratch.Slice(0, oldSpanLen);
            for (var i = 0; i < oldSpanLen; i++)
                oldCps[i] = document.GetCodepointAt(oldBase + i);

            var la = FindLeftAnchor(oldCps, oldBase, editStart);
            var ra = FindRightAnchor(oldCps, oldBase, editEnd, oldReach, docCount);
            if (la < 0 || ra < 0)
            {
                if (rented != null) ArrayPool<int>.Return(rented);
                return -1;
            }

            var oldWindow = oldCps.Slice(la - oldBase, ra - la);
            var oldClusters = SharedPipelineComponents.GraphemeBreaker.CountGraphemeClusters(oldWindow);

            var prefixLen = editStart - la;
            var suffixLen = ra - editEnd;
            var newWindow = scratch.Slice(oldSpanLen, prefixLen + insertedCp + suffixLen);
            oldCps.Slice(la - oldBase, prefixLen).CopyTo(newWindow);
            var k = prefixLen;
            for (var offset = 0; offset < inserted.Length;)
            {
                newWindow[k++] = (int)UnicodeData.DecodeAt(inserted, offset, out var size);
                offset += size;
            }
            oldCps.Slice(editEnd - oldBase, suffixLen).CopyTo(newWindow.Slice(k));

            var newClusters = SharedPipelineComponents.GraphemeBreaker.CountGraphemeClusters(newWindow);

            if (rented != null) ArrayPool<int>.Return(rented);
            return currentCount - oldClusters + newClusters;
        }

        /// <summary>
        /// Nearest guaranteed cluster boundary at or left of the edit whose deciding pair lies fully
        /// outside it; document start is an absolute anchor. Returns -1 when the scan cap is hit.
        /// Boundary safety comes from <see cref="GraphemeBreaker.IsContextFreeBoundary"/> — the pair
        /// table lives with the breaker so Unicode updates land in one place.
        /// </summary>
        private static int FindLeftAnchor(ReadOnlySpan<int> oldCps, int oldBase, int editStart)
        {
            var breaker = SharedPipelineComponents.GraphemeBreaker;
            for (var i = editStart - 1; i >= oldBase + 1; i--)
            {
                if (breaker.IsContextFreeBoundary(oldCps[i - 1 - oldBase], oldCps[i - oldBase]))
                    return i;
            }
            return oldBase == 0 ? 0 : -1;
        }

        /// <summary>
        /// Nearest guaranteed cluster boundary at or right of the edit whose deciding pair lies fully
        /// outside it; document end is an absolute anchor. Returns -1 when the scan cap is hit.
        /// </summary>
        private static int FindRightAnchor(ReadOnlySpan<int> oldCps, int oldBase, int editEnd, int oldReach, int docCount)
        {
            var breaker = SharedPipelineComponents.GraphemeBreaker;
            for (var i = editEnd + 1; i <= oldReach - 1; i++)
            {
                if (breaker.IsContextFreeBoundary(oldCps[i - 1 - oldBase], oldCps[i - oldBase]))
                    return i;
            }
            return oldReach == docCount ? docCount : -1;
        }

        private static int Recount(ITextDocument document)
            => TextMeasure.Count(document, TextLengthUnit.Graphemes);
    }
}
