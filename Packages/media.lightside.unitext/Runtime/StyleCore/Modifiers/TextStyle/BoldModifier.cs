using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>How bold is realized. Auto prefers a real bold face and synthesizes only as a fallback.</summary>
    public enum BoldMode : byte
    {
        /// <summary>Prefer a real bold face (family cut or variable wght axis); synthesize only when none matches.</summary>
        Auto,
        /// <summary>Keep the current real face and synthesize only the positive weight difference needed to reach the target.</summary>
        Synthetic,
        /// <summary>Use a real bold face only; stay at the natural weight when none matches.</summary>
        Real
    }

    /// <summary>
    /// Applies bold styling to text using the CSS font-weight scale (1-1000).
    /// </summary>
    /// <remarks>
    /// First parameter: CSS font-weight 1-1000; 0 or omitted = auto (<c>max(700, baseWeight+300)</c>).
    /// Second parameter — mode: <c>f</c> (<c>&lt;b=700,f&gt;</c>) keeps the current real face and supplies
    /// any missing weight synthetically (SDF dilate + advance correction); <c>r</c> (<c>&lt;b=700,r&gt;</c>)
    /// uses a real bold face only, staying at the natural weight when none matches. Default (no mode) prefers a real face — Font Family static cut
    /// or variable wght axis — and synthesizes only as a fallback. One buffer carries both: the top
    /// 2 bits select the mode, the low 10 bits the weight.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Makes text thicker by expanding the distance field via shader dilate and adjusting glyph advances.")]
    [GenerateParameters]
    public partial class BoldModifier : PooledAttributeModifier<ushort>
    {
        /// <summary>CSS font-weight 1–1000, or 0 (the default) for auto — <c>max(700, base weight + 300)</c>, so bold never falls below the font's own weight. Any explicit 1–1000 (including 700) is honoured exactly. A per-range value overrides it.</summary>
        [SerializeField, Parameter, Range(0, 1000), StateProperty(nameof(MarkTextDirty))] private int weight;
        /// <summary>How bold is realized. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter(Parser = nameof(ParseMode)), Variant("Auto|Synthetic=f|Real=r"), StateProperty(nameof(MarkTextDirty))] private BoldMode mode = BoldMode.Auto;

        protected sealed override string AttributeKey => AttributeKeys.Bold;

        private static bool ParseMode(ReadOnlySpan<char> token, out BoldMode value)
        {
            if (token.EqualsIgnoreCase("f"))
            {
                value = BoldMode.Synthetic;
                return true;
            }
            if (token.EqualsIgnoreCase("r"))
            {
                value = BoldMode.Real;
                return true;
            }
            value = BoldMode.Auto;
            return true;
        }

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var w = Param.Weight.Resolve(this, in context);

            int cssWeight;
            if (w <= 0)
            {
                var baseWeight = uniText.PrimaryFontCore?.DefaultWeight ?? 400;
                cssWeight = Math.Min(Math.Max(700, baseWeight + 300), 1000);
            }
            else
            {
                cssWeight = Math.Clamp(w, 1, 1000);
            }

            var modeFlag = Param.Mode.Resolve(this, in context) switch
            {
                BoldMode.Synthetic => FontStyleEncoding.BoldModeFake,
                BoldMode.Real => FontStyleEncoding.BoldModeRealOnly,
                _ => (ushort)0,
            };

            var encoded = FontStyleEncoding.EncodeCssWeight(cssWeight, modeFlag);
            attribute.FillRange(context.Segment.Range, encoded);
        }

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<ushort> attribute;
            private Action onGlyphCallback;
            private Action shapedCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<ushort>>(Key);

                onGlyphCallback ??= OnGlyph;
                uniText.MeshGenerator.onGlyph.Subscribe(onGlyphCallback);
                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
                uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
                attribute?.ClearAll();
            }

            protected override void OnRelease()
            {
                attribute = null;
                onGlyphCallback = null;
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                if (gen.font.IsColor) return;

                var buf = attribute.buffer.data;
                var cluster = gen.currentCluster;
                if (!buf.HasFlag(cluster)) return;

                var raw = buf[cluster];
                var realization = buffers.fontStyleRealizations.data;
                if ((uint)cluster >= (uint)buffers.fontStyleRealizations.count
                    || ((FontStyleRealization)realization[cluster] & FontStyleRealization.SyntheticWeight) == 0)
                    return;

                var cssWeight = FontStyleEncoding.DecodeCssWeight(raw);
                var resolvedWeight = (uint)cluster < (uint)buffers.fontStyleWeights.count
                    ? buffers.fontStyleWeights[cluster]
                    : (ushort)0;
                var baseWeight = resolvedWeight > 0
                    ? resolvedWeight
                    : gen.font.DefaultWeight;
                var fakeBoldWeight = Math.Max(0f, (cssWeight - baseWeight) / 300f);
                var dilate = fakeBoldWeight * FontStyleEncoding.EmboldenRatio;
                var baseIdx = gen.faceBaseIdx;
                var uvs1 = gen.Uvs1;

                uvs1[baseIdx].y = dilate;
                uvs1[baseIdx + 1].y = dilate;
                uvs1[baseIdx + 2].y = dilate;
                uvs1[baseIdx + 3].y = dilate;

                var glyphH = gen.Uvs0[baseIdx].w;
                if (glyphH < 1e-6f) return;

                var padGlyph = GlyphAtlas.Pad / glyphH;
                var facePad = dilate * padGlyph;
                var effectivePad = facePad < padGlyph ? facePad : padGlyph;

                var delta = effectivePad - UniTextMeshGenerator.DefaultSdfPadding;
                if (delta > 0f)
                    gen.ExpandQuad(baseIdx, delta);
            }

            private void OnShaped()
            {
                var data = attribute?.buffer.data;
                if (!data.HasAnyFlags()) return;

                var glyphs = buffers.shapedGlyphs.data;
                var runs = buffers.shapedRuns.data;
                var runCount = buffers.shapedRuns.count;
                var len = data.Length;
                var fontSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
                var fp = uniText.FontProvider;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    var runFont = fp.GetFont(run.fontId);
                    var runWeight = runFont?.DefaultWeight ?? 400;
                    float width = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster < (uint)len)
                        {
                            var raw = data[cluster];
                            var realization = (uint)cluster < (uint)buffers.fontStyleRealizations.count
                                ? (FontStyleRealization)buffers.fontStyleRealizations[cluster]
                                : FontStyleRealization.None;
                            if (raw != 0 && (realization & FontStyleRealization.SyntheticWeight) != 0)
                            {
                                var resolvedWeight = (uint)cluster < (uint)buffers.fontStyleWeights.count
                                    ? buffers.fontStyleWeights[cluster]
                                    : (ushort)0;
                                var baseWeight = resolvedWeight > 0
                                    ? resolvedWeight
                                    : runWeight;
                                var cssWeight = FontStyleEncoding.DecodeCssWeight(raw);
                                var fakeBoldWeight = Math.Max(0f, (cssWeight - baseWeight) / 300f);
                                if (glyphs[g].advanceX != 0f)
                                    glyphs[g].advanceX += fontSize * FontStyleEncoding.EmboldenRatio * fakeBoldWeight;
                            }
                        }
                        width += glyphs[g].advanceX;
                    }

                    run.width = width;
                }
            }
        }
    }
}
