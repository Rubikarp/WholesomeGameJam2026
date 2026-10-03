using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Decode effect: unresolved characters render as random glyphs from <see cref="Charset"/> and
    /// settle into the real text left-to-right as <see cref="Progress"/> grows.
    /// <see cref="Phase"/> churns the random picks. Both inputs are external and deterministic; the
    /// layout never reflows — replacements draw in the real glyphs' cells.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 10)]
    [TypeDescription("Characters decode from random glyphs; drive Progress and Phase.")]
    [GenerateParameters]
    public partial class ScrambleModifier : BaseModifier
    {
        /// <summary>Resolved fraction of each range: 0 fully scrambled, 1 real text (and zero per-frame cost).</summary>
        [SerializeField, SlotlessParameter(Invalidate = nameof(MarkParamsDirty)),
         Range(0f, 1f), StateProperty(nameof(ApplyProgressChange))]
        private float progress = 1f;

        /// <summary>Random re-picks per phase unit.</summary>
        [SerializeField, SlotlessParameter, StateProperty(nameof(MarkParamsDirty))]
        private float rate = 12f;

        /// <summary>Glyph pool for unresolved characters (BMP characters only).</summary>
        [SerializeField, StateProperty(nameof(MarkTextDirty))]
        private string charset = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&";

        /// <summary>Animation input in abstract time units churning the random picks. Wholly external — a driver, tween, or Animator writes it.</summary>
        [SerializeField, SlotlessParameter, StateProperty(nameof(MarkParamsDirty))]
        private float phase;

        private void MarkParamsDirty() => MarkRenderDirty(HasRanges);

        private void ApplyProgressChange(float previous, ref float current)
        {
            current = Mathf.Clamp01(current);
            if (previous != current) MarkParamsDirty();
        }

        private bool HasRanges => ranges != null && ranges.Count > 0;

        private struct ScrambleRange
        {
            public int start;
            public int end;
            public float progress;
            public float phase;
            public float rate;
            public RangeApplyMemo memo;
        }

        private PooledList<ScrambleRange> ranges;
        private PooledBuffer<bool> graphemeBreaks;
        private Action beforeGenerateMeshCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<ScrambleRange>(4);
            ranges.FakeClear();
            graphemeBreaks.Rent(64);
            beforeGenerateMeshCallback ??= OnBeforeGenerateMesh;
            uniText.BeforeGenerateMesh.Subscribe(beforeGenerateMeshCallback);
        }

        protected override void OnDisable()
        {
            uniText.BeforeGenerateMesh.Unsubscribe(beforeGenerateMeshCallback);
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
            graphemeBreaks.Return();
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            end = Math.Min(end, buffers.codepoints.count);
            if (start >= end || string.IsNullOrEmpty(charset)) return;

            var range = new ScrambleRange { start = start, end = end, memo = context.Retain() };
            Resolve(ref range, in context);
            ranges.Add(range);
            for (var i = 0; i < charset.Length; i++)
                buffers.RequestVirtualCodepoint(charset[i]);
        }

        private void Resolve(ref ScrambleRange range, in RangeApplyContext context)
        {
            range.progress = Mathf.Clamp01(Param.Progress.Resolve(this, in context));
            range.phase = Param.Phase.Resolve(this, in context);
            range.rate = Param.Rate.Resolve(this, in context);
        }

        /// <summary>Re-resolves each range's inputs from its retained context, so progress, phase and rate changes reach the mesh without a re-apply.</summary>
        protected internal override void PrepareForParallel()
        {
            if (!IsInitialized || ranges == null) return;
            for (var i = 0; i < ranges.Count; i++)
            {
                ref var range = ref ranges[i];
                var context = range.memo.ToContext();
                Resolve(ref range, in context);
            }
        }

        private void OnBeforeGenerateMesh()
        {
            if (ranges == null || ranges.Count == 0 || string.IsNullOrEmpty(charset))
                return;

            ClearOwnFlags();
            var anyScrambled = false;
            for (var r = 0; r < ranges.Count; r++)
                if (ranges[r].progress < 1f)
                {
                    anyScrambled = true;
                    break;
                }
            if (!anyScrambled)
                return;

            var flags = buffers.PrepareHiddenClusters();
            if (flags.IsEmpty) return;

            for (var r = 0; r < ranges.Count; r++)
                WriteScrambleFlags(ranges[r].start, ranges[r].end, ranges[r].progress, flags);

            InjectReplacements();
        }

        private void ClearOwnFlags()
        {
            var count = buffers.hiddenClusters.count;
            if (count == 0 || ranges == null) return;

            var flags = buffers.hiddenClusters.data;
            for (var r = 0; r < ranges.Count; r++)
            {
                ref readonly var range = ref ranges[r];
                var max = Math.Min(range.end, count);
                for (var c = range.start; c < max; c++)
                    flags[c] &= unchecked((byte)~HiddenClusterBits.Scramble);
            }
        }

        /// <summary>
        /// Grapheme-correct marking, mirroring <see cref="RevealModifier"/>: whole clusters scramble at
        /// once. Spaces and mandatory breaks stay real and are excluded from the resolve count so the
        /// decode never visibly touches them.
        /// </summary>
        private void WriteScrambleFlags(int start, int end, float progress, Span<byte> flags)
        {
            end = Math.Min(end, flags.Length);
            if (start >= end) return;

            var cps = buffers.codepoints.data;
            var len = end - start;

            graphemeBreaks.EnsureCapacity(len + 1);
            var breaks = graphemeBreaks.data.AsSpan(0, len + 1);
            var span = cps.AsSpan(start, len);
            var walk = RevealableClusterWalk.Over(span, breaks, excludeSpaces: true);
            var total = RevealableClusterWalk.CountEligible(span, breaks, excludeSpaces: true);
            var resolved = (int)(progress * total + 1e-4f);

            while (walk.MoveNext())
                if (walk.Eligible && walk.Ordinal >= resolved)
                    flags[start + walk.Index] |= HiddenClusterBits.Scramble;
        }

        private void InjectReplacements()
        {
            var fontProvider = uniText.FontProvider;
            if (fontProvider == null) return;

            var positioned = buffers.positionedGlyphs.data;
            var positionedCount = buffers.positionedGlyphs.count;
            var shaped = buffers.shapedGlyphs.data;
            var flags = buffers.hiddenClusters.data;
            var flagCount = buffers.hiddenClusters.count;
            var glyphScale = buffers.GetGlyphScale(uniText.CurrentFontSize);

            var lastCluster = -1;
            for (var i = 0; i < positionedCount; i++)
            {
                ref readonly var pg = ref positioned[i];
                var cluster = pg.cluster;
                if (pg.shapedGlyphIndex < 0 || cluster == lastCluster) continue;
                if ((uint)cluster >= (uint)flagCount || (flags[cluster] & HiddenClusterBits.Scramble) == 0) continue;
                lastCluster = cluster;

                var step = 0;
                for (var r = 0; r < ranges.Count; r++)
                {
                    ref readonly var range = ref ranges[r];
                    if (cluster < range.start || cluster >= range.end) continue;
                    step = Mathf.FloorToInt(range.phase * range.rate);
                    break;
                }

                var pick = charset[(int)(HashNoise.Hash01(cluster, step, 7) * charset.Length)];
                if (!buffers.TryResolveInjectedGlyph(fontProvider, pick, cluster, uniText.CurrentFontSize,
                        out var pickGlyph))
                    continue;

                ref readonly var sh = ref shaped[pg.shapedGlyphIndex];
                var baselineX = pg.x - sh.offsetX * glyphScale;
                var baselineY = pg.y + sh.offsetY * glyphScale;

                buffers.virtualPositionedGlyphs.Add(new PositionedGlyph
                {
                    glyphId = (int)pickGlyph.GlyphIndex,
                    cluster = cluster,
                    x = baselineX,
                    y = baselineY,
                    fontId = pickGlyph.FontId,
                    shapedGlyphIndex = -1,
                    left = pg.left,
                    right = pg.right,
                    top = pg.top,
                    bottom = pg.bottom
                });
            }
        }
    }
}
