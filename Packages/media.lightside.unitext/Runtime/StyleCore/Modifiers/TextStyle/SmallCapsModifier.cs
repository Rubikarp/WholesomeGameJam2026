using System;

namespace LightSide
{
    /// <summary>
    /// Renders lowercase letters as small capitals.
    /// </summary>
    /// <remarks>
    /// Two-tier approach:
    /// <list type="bullet">
    /// <item>Native: activates OpenType 'smcp' feature via HarfBuzz (proper small cap glyphs).</item>
    /// <item>Synthesis: converts lowercase to uppercase and scales down (fallback for fonts without smcp).</item>
    /// </list>
    /// Attribute byte encoding: 0 = unchanged, 1 = native smcp, 2 = synthesis. The synthesis scale is
    /// applied once per character however many ranges or modifiers marked it.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Renders lowercase letters as small uppercase letters.")]
    public class SmallCapsModifier : PooledAttributeModifier<byte>
    {
        private const float SynthesisScale = 0.8f;

        private const byte Native = 1;
        private const byte Synthesis = 2;

        private static readonly byte smcpFeature = FontFeatureRegistry.Register("smcp");

        protected sealed override string AttributeKey => AttributeKeys.SmallCaps;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var range = context.Segment.Range;
            var codepoints = buffers.codepoints.Span.Slice(range.start, range.length);
            var modes = attribute.buffer.data.AsSpan(range.start, range.length);

            var mainFont = uniText.PrimaryFontCore;
            bool hasSmcp = Shaper.Instance.HasSmcpFeature(mainFont);
            if (hasSmcp) buffers.AddFontFeatures(range, smcpFeature);

            for (var i = 0; i < codepoints.Length; i++)
            {
                var cp = codepoints[i];
                var upper = UnicodeData.GetSimpleUppercase(cp);
                if (upper == cp) continue;

                if (hasSmcp)
                {
                    modes[i] = Native;
                }
                else
                {
                    modes[i] = Synthesis;
                    codepoints[i] = upper;
                }
            }
        }

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<byte> attribute;

            private Action shapedCallback;
            private Action glyphCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(Key);

                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
                glyphCallback ??= OnGlyph;
                uniText.MeshGenerator.onGlyph.Subscribe(glyphCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
                uniText.MeshGenerator.onGlyph.Unsubscribe(glyphCallback);
                attribute?.ClearAll();
            }

            protected override void OnRelease() => attribute = null;

            private void OnShaped()
            {
                var glyphs = buffers.shapedGlyphs.data;
                var runs = buffers.shapedRuns.data;
                var runCount = buffers.shapedRuns.count;
                var attrBuf = attribute.buffer.data;
                var bufLen = attribute.buffer.Capacity;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    float width = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster < (uint)bufLen && attrBuf[cluster] == Synthesis)
                            glyphs[g].advanceX *= SynthesisScale;

                        width += glyphs[g].advanceX;
                    }

                    run.width = width;
                }
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                if (gen.currentPositionedIndex < 0) return;

                var cluster = gen.currentCluster;

                if ((uint)cluster >= (uint)attribute.buffer.Capacity)
                    return;

                if (attribute.buffer[cluster] != Synthesis)
                    return;

                gen.ScaleFace(gen.faceBaseIdx, gen.cursorX, gen.baselineY, SynthesisScale);
            }
        }
    }
}
