using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Sets the painter order of a range's paint layers (<see cref="PaintOrder"/>): layer-major (the
    /// component default) draws each layer across all glyphs before the next layer; glyph-major
    /// composites every layer of one glyph before the next glyph, so a later glyph's stroke or shadow
    /// overlaps earlier glyphs' fill. The axis is a single per-component channel — all layer modifiers
    /// follow it, overlapping ranges resolve innermost-wins, and unmarked text stays layer-major.
    /// Characters connected by the Unicode cursive joining algorithm (Arabic, Syriac, N'Ko,
    /// Mongolian, …) never split: each connected stretch keeps layer order and composites as one
    /// unit, so glyph-major Arabic stacks per word rather than per letter.
    /// </summary>
    /// <remarks>
    /// Applies to same-material quads of the base mesh. Separate-material output (texture paints,
    /// the color-glyph segment) always stacks layer-major, and a blend-mode change between adjacent
    /// glyph-major quads splits the draw. Stylistic connected Latin fonts (script typefaces joining
    /// via ligatures and contextual alternates) carry no data signal that marks them cursive; their
    /// connections are not detected.
    /// </remarks>
    [Serializable]
    [TypeDescription("Chooses per-layer or per-glyph painter order for a range.")]
    [GenerateParameters]
    public sealed partial class PaintOrderModifier : BaseModifier
    {
        /// <summary>Painter order applied when the markup carries no value. Defaults to glyph-major — the mode the tag exists to enable; pass <c>layer</c> to restore layer order inside a glyph-major range.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        [Tooltip("Layer draws each layer across all glyphs; Glyph composites each glyph fully before the next.")]
        private PaintOrder order = PaintOrder.Glyph;

        private PooledArrayAttribute<long> winners;
        private string attributeKey;

        /// <summary>
        /// Per-codepoint winner buffer key, unique per modifier instance so two instances never alias
        /// each other's winner state.
        /// </summary>
        private string AttributeKey =>
            attributeKey ??= $"paintorder#{RuntimeHelpers.GetHashCode(this):x8}";

        protected override void OnEnable()
        {
            buffers.PrepareAttribute(ref winners, AttributeKey);
            base.OnEnable();
        }

        protected override void OnDestroy()
        {
            buffers?.ReleaseAttributeData(AttributeKey);
            winners = null;
            base.OnDestroy();
        }

        /// <summary>
        /// The winner buffer holds packed range bounds from the previous cycle; a granular re-apply
        /// replays every range, so stale winners must not out-compete the replayed stamps.
        /// </summary>
        protected override void BeforeApply() => winners?.ClearAll();

        /// <summary>
        /// Stamps the range's resolved order into the shared per-codepoint axis
        /// (<see cref="UniTextBuffers.paintOrders"/>); overlapping ranges resolve innermost-wins
        /// through packed (start, end) winner entries. Glyph-major stamping is joining-aware:
        /// a character cursively connected to a neighbour (Transparent characters skipped, edges
        /// probed past the range bounds) keeps layer order, and Transparent characters inherit
        /// their base's mode so a connected stretch stays one contiguous composition group.
        /// </summary>
        protected override void OnApply(in RangeApplyContext context)
        {
            var resolved = Param.Order.Resolve(this, in context);

            var start = Math.Max(context.Segment.Range.start, 0);
            var end = Math.Min(context.Segment.Range.End, buffers.codepoints.count);
            if (start >= end) return;

            var win = winners.buffer.data;
            if (win == null) return;
            var modes = buffers.PreparePaintOrders();
            var packed = ((long)start << 32) | (uint)end;
            var limit = Math.Min(end, win.Length);

            if (resolved == PaintOrder.Layer)
            {
                for (var i = start; i < limit; i++)
                    Stamp(win, modes, i, packed, PaintOrder.Layer);
                return;
            }

            var cp = buffers.codepoints.data;
            var cpCount = buffers.codepoints.count;
            var provider = UnicodeData.Provider;

            var prevJoinsFollowing = PrecedingBaseJoinsFollowing(cp, start, provider);
            var baseMode = PaintOrder.Layer;
            var hasBase = false;
            for (var i = start; i < limit; i++)
            {
                var jt = provider.GetJoiningType(cp[i]);
                if (jt == JoiningType.Transparent)
                {
                    if (hasBase) Stamp(win, modes, i, packed, baseMode);
                    continue;
                }

                var next = i + 1;
                while (next < cpCount && provider.GetJoiningType(cp[next]) == JoiningType.Transparent)
                    next++;

                var connected = prevJoinsFollowing && jt.JoinsPreceding()
                    || next < cpCount && jt.JoinsFollowing() &&
                       provider.GetJoiningType(cp[next]).JoinsPreceding();
                baseMode = connected ? PaintOrder.Layer : PaintOrder.Glyph;
                hasBase = true;
                Stamp(win, modes, i, packed, baseMode);
                prevJoinsFollowing = jt.JoinsFollowing();
            }
        }

        private static void Stamp(long[] win, Span<byte> modes, int i, long packed, PaintOrder mode)
        {
            var prev = win[i];
            if (prev != 0 && !IsInner(packed, prev)) return;
            win[i] = packed;
            modes[i] = (byte)mode;
        }

        /// <summary>Joining ability of the nearest non-transparent character before the range, so a connection crossing the range's left edge is honoured.</summary>
        private static bool PrecedingBaseJoinsFollowing(int[] cp, int start,
            UnicodeDataProvider provider)
        {
            for (var i = start - 1; i >= 0; i--)
            {
                var jt = provider.GetJoiningType(cp[i]);
                if (jt == JoiningType.Transparent) continue;
                return jt.JoinsFollowing();
            }
            return false;
        }

        /// <summary>Innermost-wins for properly nested tag ranges: greater start, then narrower end.</summary>
        private static bool IsInner(long candidate, long current)
        {
            var candidateStart = (int)(candidate >> 32);
            var currentStart = (int)(current >> 32);
            return candidateStart != currentStart
                ? candidateStart > currentStart
                : (uint)candidate < (uint)current;
        }
    }
}
