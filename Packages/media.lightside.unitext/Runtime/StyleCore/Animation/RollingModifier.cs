using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Characters roll on a cyclic glyph wheel: every covered character found in <see cref="Wheel"/>
    /// renders as its wheel position minus the effective roll, with the fractional part shown as two
    /// stacked wheel glyphs sliding through the character's cell (one em of travel per step, clipped
    /// to the wheel's ink band). The default digit wheel is an odometer; a letter wheel with a
    /// trailing space is a split-flap board. Characters outside the wheel pass through untouched.
    /// Drive <see cref="Roll"/> toward 0 to settle on the real text; at 0 the modifier costs nothing.
    /// </summary>
    [Serializable]
    [TypeGroup("Animation", 11)]
    [TypeDescription("Characters roll on a glyph wheel — odometer digits, split-flap letters; drive Roll toward 0.")]
    [GenerateParameters]
    public partial class RollingModifier : BaseModifier
    {
        /// <summary>Wheel steps still to travel. Negative values roll the opposite direction. Setting it costs nothing while no range is active.</summary>
        [SerializeField, SlotlessParameter, StateProperty(nameof(MarkRollDirty))]
        private float roll;

        /// <summary>Roll reduction per rolling character within a range — later characters settle later.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private float spread = 0.3f;

        /// <summary>Cyclic wheel alphabet, in rolling order (BMP characters only). Only characters found here roll.</summary>
        [SerializeField, StateProperty(nameof(MarkTextDirty))]
        private string wheel = "0123456789";

        private void MarkRollDirty()
        {
            if (ranges != null && ranges.Count > 0) MarkMeshDirty();
        }

        private PooledList<(int start, int end, float spread, float roll)> ranges;
        private PooledBuffer<float> effRolls;
        private PooledBuffer<float> clusterBaselines;
        private PooledList<int> wheelGlyphIds;
        private float bandTop;
        private float bandPad;
        private Action beforeGenerateMeshCallback;
        private Action onGlyphCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<(int, int, float, float)>(4);
            ranges.FakeClear();
            wheelGlyphIds ??= new PooledList<int>(16);
            wheelGlyphIds.FakeClear();
            effRolls.Rent(64);
            clusterBaselines.Rent(64);
            beforeGenerateMeshCallback ??= OnBeforeGenerateMesh;
            uniText.BeforeGenerateMesh.Subscribe(beforeGenerateMeshCallback);
            onGlyphCallback ??= OnGlyph;
            uniText.MeshGenerator.onGlyph.Subscribe(onGlyphCallback);
        }

        protected override void OnDisable()
        {
            uniText.BeforeGenerateMesh.Unsubscribe(beforeGenerateMeshCallback);
            uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
        }

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
            wheelGlyphIds?.Return();
            wheelGlyphIds = null;
            effRolls.Return();
            clusterBaselines.Return();
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            end = Math.Min(end, buffers.codepoints.count);
            if (start >= end || string.IsNullOrEmpty(wheel)) return;

            ranges.Add((start, end,
                Param.Spread.Resolve(this, in context),
                Param.Roll.Resolve(this, in context)));

            for (var i = 0; i < wheel.Length; i++)
                buffers.RequestVirtualCodepoint(wheel[i]);
        }

        private int WheelIndex(int cp)
            => cp > char.MaxValue ? -1 : wheel.IndexOf((char)cp);

        private void OnBeforeGenerateMesh()
        {
            wheelGlyphIds.FakeClear();

            if (ranges == null || ranges.Count == 0 || string.IsNullOrEmpty(wheel))
                return;

            ClearOwnFlags();
            var anyRolling = false;
            for (var r = 0; r < ranges.Count; r++)
                if (ranges[r].roll != 0f)
                {
                    anyRolling = true;
                    break;
                }
            if (!anyRolling)
                return;

            var flags = buffers.PrepareHiddenClusters();
            if (flags.IsEmpty) return;

            var cps = buffers.codepoints.data;
            var cpCount = buffers.codepoints.count;
            effRolls.EnsureCapacity(cpCount);
            var eff = effRolls.data;

            var any = false;

            for (var r = 0; r < ranges.Count; r++)
            {
                var (start, end, spr, rangeRoll) = ranges[r];
                if (rangeRoll == 0f) continue;
                var sign = rangeRoll < 0f ? -1f : 1f;
                var absRoll = Math.Abs(rangeRoll);
                end = Math.Min(end, flags.Length);
                var ordinal = 0;
                for (var i = start; i < end; i++)
                {
                    if (WheelIndex(cps[i]) < 0) continue;

                    var er = absRoll - ordinal * spr;
                    ordinal++;
                    if (er <= 0f) continue;

                    flags[i] |= HiddenClusterBits.Rolling;
                    eff[i] = sign * er;
                    any = true;
                }
            }

            if (any)
                InjectWheelGlyphs();
        }

        private void ClearOwnFlags()
        {
            var count = buffers.hiddenClusters.count;
            if (count == 0 || ranges == null) return;

            var flags = buffers.hiddenClusters.data;
            for (var r = 0; r < ranges.Count; r++)
            {
                var (start, end, _, _) = ranges[r];
                var max = Math.Min(end, count);
                for (var c = start; c < max; c++)
                    flags[c] &= unchecked((byte)~HiddenClusterBits.Rolling);
            }
        }

        private void InjectWheelGlyphs()
        {
            var fontProvider = uniText.FontProvider;
            if (fontProvider == null) return;

            var cell = uniText.CurrentFontSize;
            bandPad = cell * 0.05f;
            bandTop = MeasureWheelBand(fontProvider, cell);

            var positioned = buffers.positionedGlyphs.data;
            var positionedCount = buffers.positionedGlyphs.count;
            var shaped = buffers.shapedGlyphs.data;
            var flags = buffers.hiddenClusters.data;
            var flagCount = buffers.hiddenClusters.count;
            var cps = buffers.codepoints.data;
            var eff = effRolls.data;
            var glyphScale = buffers.GetGlyphScale(cell);
            var window = bandTop + bandPad;
            var wheelLength = wheel.Length;
            clusterBaselines.EnsureCapacity(buffers.codepoints.count);
            var baselines = clusterBaselines.data;

            var lastCluster = -1;
            for (var i = 0; i < positionedCount; i++)
            {
                ref readonly var pg = ref positioned[i];
                var cluster = pg.cluster;
                if (pg.shapedGlyphIndex < 0 || cluster == lastCluster) continue;
                if ((uint)cluster >= (uint)flagCount || (flags[cluster] & HiddenClusterBits.Rolling) == 0) continue;
                lastCluster = cluster;

                var index = WheelIndex(cps[cluster]);
                if (index < 0) continue;

                var value = index - eff[cluster];
                var low = Mathf.FloorToInt(value);
                var frac = value - low;
                var lowIndex = ((low % wheelLength) + wheelLength) % wheelLength;
                var highIndex = (lowIndex + 1) % wheelLength;

                ref readonly var sh = ref shaped[pg.shapedGlyphIndex];
                var baselineX = pg.x - sh.offsetX * glyphScale;
                var baselineY = pg.y + sh.offsetY * glyphScale;
                baselines[cluster] = baselineY;

                if (frac * cell < window)
                    Inject(fontProvider, wheel[lowIndex], in pg, baselineX, baselineY, -frac * cell);
                if ((1f - frac) * cell < window)
                    Inject(fontProvider, wheel[highIndex], in pg, baselineX, baselineY, (1f - frac) * cell);
            }
        }

        /// <summary>Ink top of the rolling band above the baseline — the tallest wheel glyph sets it for all.</summary>
        private float MeasureWheelBand(UniTextFontProvider fontProvider, float cell)
        {
            var top = 0f;
            for (var i = 0; i < wheel.Length; i++)
            {
                var fontId = fontProvider.FindFontForCodepoint(wheel[i]);
                var font = fontProvider.GetFont(fontId);
                if (font == null) continue;

                var glyphIndex = font.GetGlyphIndexForUnicode(wheel[i]);
                var lookup = font.GlyphLookupTable;
                if (glyphIndex == 0 || lookup == null || !lookup.TryGetValue(font.GlyphKey(glyphIndex), out var entry))
                    continue;

                var glyphTop = entry.metrics.horizontalBearingY * fontProvider.MetricScale(font, cell);
                if (glyphTop > top) top = glyphTop;
            }
            return top > 0f ? top : cell * 0.72f;
        }

        private void Inject(UniTextFontProvider fontProvider, char c, in PositionedGlyph pg,
            float baselineX, float baselineY, float visualOffset)
        {
            if (!buffers.TryResolveInjectedGlyph(fontProvider, c, pg.cluster, uniText.CurrentFontSize,
                    out var wheelGlyph))
                return;

            buffers.virtualPositionedGlyphs.Add(new PositionedGlyph
            {
                glyphId = (int)wheelGlyph.GlyphIndex,
                cluster = pg.cluster,
                x = baselineX,
                y = baselineY - visualOffset,
                fontId = wheelGlyph.FontId,
                shapedGlyphIndex = -1,
                left = pg.left,
                right = pg.right,
                top = pg.top,
                bottom = pg.bottom
            });

            if (!wheelGlyphIds.Contains((int)wheelGlyph.GlyphIndex))
                wheelGlyphIds.Add((int)wheelGlyph.GlyphIndex);
        }

        /// <summary>
        /// Identifies this modifier's quads by evidence on the quad itself (virtual + rolling cluster +
        /// wheel glyph) instead of emission order — the generator legitimately skips some injected quads
        /// (blank wheel entries, glyphs not yet in the atlas), so any order-based pairing would stall.
        /// </summary>
        private void OnGlyph()
        {
            if (wheelGlyphIds == null || wheelGlyphIds.Count == 0) return;

            var gen = uniText.MeshGenerator;
            if (!gen.isVirtualGlyph) return;

            var cluster = gen.currentCluster;
            var flags = buffers.hiddenClusters.data;
            if (flags == null || (uint)cluster >= (uint)buffers.hiddenClusters.count) return;
            if ((flags[cluster] & HiddenClusterBits.Rolling) == 0) return;
            if (!wheelGlyphIds.Contains(gen.currentGlyphId)) return;

            var origBaseline = gen.offsetY - clusterBaselines.data[cluster];
            var minY = origBaseline - bandPad;
            var maxY = origBaseline + bandTop + bandPad;

            var verts = gen.Vertices;
            var uvs = gen.Uvs0;
            var baseIdx = gen.faceBaseIdx;
            CropColumnY(verts, uvs, baseIdx, baseIdx + 1, minY, maxY);
            CropColumnY(verts, uvs, baseIdx + 3, baseIdx + 2, minY, maxY);
        }

        /// <summary>Clamps one vertical quad edge (bottom/top vertex pair) into the band, cropping UVs proportionally.</summary>
        private static void CropColumnY(Vector3[] verts, Vector4[] uvs, int bottomIdx, int topIdx, float minY, float maxY)
        {
            ref var vb = ref verts[bottomIdx];
            ref var vt = ref verts[topIdx];
            var y0 = vb.y;
            var y1 = vt.y;
            var span = y1 - y0;
            if (span <= 0f) return;

            var ny0 = Mathf.Clamp(y0, minY, maxY);
            var ny1 = Mathf.Clamp(y1, minY, maxY);

            ref var ub = ref uvs[bottomIdx];
            ref var ut = ref uvs[topIdx];
            var uvSpan = ut.y - ub.y;
            var newBottom = ub.y + uvSpan * (ny0 - y0) / span;
            var newTop = ub.y + uvSpan * (ny1 - y0) / span;

            vb.y = ny0;
            vt.y = ny1;
            ub.y = newBottom;
            ut.y = newTop;
        }
    }
}
