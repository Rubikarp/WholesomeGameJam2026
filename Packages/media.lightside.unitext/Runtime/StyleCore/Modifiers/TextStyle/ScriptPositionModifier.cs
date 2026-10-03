using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies superscript or subscript formatting to text ranges.
    /// </summary>
    /// <remarks>
    /// Two-tier approach:
    /// <list type="bullet">
    /// <item>Native: activates OpenType 'sups'/'subs' feature via HarfBuzz (proper glyphs).</item>
    /// <item>Synthesis: scales down and shifts vertically using OS/2 metrics (fallback).</item>
    /// </list>
    ///
    /// Attribute sbyte encoding: 0 = unchanged, +1 = native super, -1 = native sub, +2 = synth super, -2 = synth sub.
    ///
    /// Create two styles to support both tags:
    /// <list type="bullet">
    /// <item>Style 1: ScriptPositionModifier + TagRule("sup") with defaultParameter = "super"</item>
    /// <item>Style 2: ScriptPositionModifier + TagRule("sub") with defaultParameter = "sub"</item>
    /// </list>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Applies superscript or subscript formatting.")]
    [GenerateParameters]
    public partial class ScriptPositionModifier : PooledAttributeModifier<sbyte>
    {
        /// <summary>Superscript vs subscript placement.</summary>
        public enum Placement
        {
            /// <summary>Raised, reduced glyphs (OpenType 'sups' or synthesized).</summary>
            Super,
            /// <summary>Lowered, reduced glyphs (OpenType 'subs' or synthesized).</summary>
            Sub
        }

        /// <summary>Super vs sub placement. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter(Parser = nameof(ParseMode)), StateProperty(nameof(MarkTextDirty))] private Placement mode = Placement.Super;

        private static bool ParseMode(ReadOnlySpan<char> token, out Placement value)
        {
            if (token.EqualsIgnoreCase("super") || token.EqualsIgnoreCase("sup"))
            {
                value = Placement.Super;
                return true;
            }
            if (token.EqualsIgnoreCase("sub"))
            {
                value = Placement.Sub;
                return true;
            }
            value = Placement.Super;
            return false;
        }

        private const sbyte NativeSuper = 1;
        private const sbyte NativeSub = -1;
        private const sbyte SynthSuper = 2;
        private const sbyte SynthSub = -2;

        private static readonly byte supsFeature = FontFeatureRegistry.Register("sups");
        private static readonly byte subsFeature = FontFeatureRegistry.Register("subs");

        protected sealed override string AttributeKey => AttributeKeys.ScriptPosition;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var isSuper = Param.Mode.Resolve(this, in context) == Placement.Super;

            var mainFont = uniText.PrimaryFontCore;
            var shaper = Shaper.Instance;

            bool fontHasFeature = isSuper
                ? shaper.HasSupsFeature(mainFont)
                : shaper.HasSubsFeature(mainFont);

            var range = context.Segment.Range;

            if (!fontHasFeature)
            {
                var synthMode = isSuper ? SynthSuper : SynthSub;
                attribute.FillRange(range, synthMode);
            }
            else
            {
                var codepoints = buffers.codepoints.Span.Slice(range.start, range.length);
                var modes = attribute.buffer.data.AsSpan(range.start, range.length);
                for (var i = 0; i < range.length; i++)
                {
                    var cp = codepoints[i];
                    bool hasNative = isSuper
                        ? shaper.HasSupsForCodepoint(mainFont, cp)
                        : shaper.HasSubsForCodepoint(mainFont, cp);
                    modes[i] = isSuper
                        ? (hasNative ? NativeSuper : SynthSuper)
                        : (hasNative ? NativeSub : SynthSub);
                }

                var nativeMode = isSuper ? NativeSuper : NativeSub;
                var feature = isSuper ? supsFeature : subsFeature;
                for (var i = 0; i < range.length;)
                {
                    if (modes[i] != nativeMode) { i++; continue; }

                    var spanStart = i;
                    while (i < range.length && modes[i] == nativeMode) i++;
                    buffers.AddFontFeatures(new TextRange(range.start + spanStart, i - spanStart), feature);
                }
            }
        }

        private static float GetScale(UniTextFont.Core font, bool isSuper)
        {
            var fi = font.FaceInfo;
            var size = isSuper ? fi.superscriptSize : fi.subscriptSize;
            if (size <= 0)
                size = isSuper ? fi.subscriptSize : fi.superscriptSize;
            if (size <= 0 || size >= fi.unitsPerEm)
                return 0.7f;

            return size / (float)fi.unitsPerEm;
        }

        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<sbyte> attribute;

            private Action shapedCallback;
            private Action glyphCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<sbyte>>(Key);

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
                var bufLen = attribute.buffer.Capacity;
                var fontProvider = uniText.FontProvider;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    var widthDirty = false;
                    var superScale = 0f;
                    var subScale = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster >= (uint)bufLen)
                            continue;

                        var mode = attribute.buffer[cluster];
                        if (mode == 0 || mode == NativeSuper || mode == NativeSub)
                            continue;

                        float scale;
                        if (mode == SynthSuper)
                        {
                            if (superScale == 0f)
                            {
                                var font = fontProvider.GetFont(run.fontId);
                                superScale = font != null ? GetScale(font, true) : 0.7f;
                            }
                            scale = superScale;
                        }
                        else
                        {
                            if (subScale == 0f)
                            {
                                var font = fontProvider.GetFont(run.fontId);
                                subScale = font != null ? GetScale(font, false) : 0.7f;
                            }
                            scale = subScale;
                        }

                        glyphs[g].advanceX *= scale;
                        widthDirty = true;
                    }

                    if (widthDirty)
                    {
                        float width = 0f;
                        for (var g = run.glyphStart; g < glyphEnd; g++)
                            width += glyphs[g].advanceX;
                        run.width = width;
                    }
                }
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                if (gen.isVirtualGlyph) return;
                var cluster = gen.currentCluster;

                if ((uint)cluster >= (uint)attribute.buffer.Capacity)
                    return;

                var mode = attribute.buffer[cluster];
                if (mode == 0 || mode == NativeSuper || mode == NativeSub)
                    return;

                var font = gen.font;
                var fi = font.FaceInfo;
                var upem = (float)fi.unitsPerEm;
                var metricScale = uniText.FontProvider.MetricScale(font, gen.FontSize);
                var isSuper = mode > 0;

                var scale = GetScale(font, isSuper);
                var rawOffset = isSuper ? fi.superscriptOffset : fi.subscriptOffset;
                if (rawOffset <= 0)
                    rawOffset = isSuper ? (int)(upem * 0.35f) : (int)(upem * 0.12f);
                var offset = (isSuper ? 1f : -1f) * rawOffset * metricScale;

                gen.ScaleFace(gen.faceBaseIdx, gen.cursorX, gen.baselineY, scale, offset);
            }
        }
    }
}
