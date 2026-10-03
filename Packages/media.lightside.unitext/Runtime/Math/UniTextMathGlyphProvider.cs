using System;

namespace LightSide
{
    internal sealed class UniTextMathGlyphProvider : IMathGlyphProvider, IDisposable
    {
        private static readonly uint SstyTag = HB.MakeTag('s', 's', 't', 'y');
        private static readonly uint DtlsTag = HB.MakeTag('d', 't', 'l', 's');
        private static readonly uint FlacTag = HB.MakeTag('f', 'l', 'a', 'c');

        private static readonly HB.hb_feature_t[] SstyLevelOneFeatures =
        {
            Feature(SstyTag, 1)
        };

        private static readonly HB.hb_feature_t[] SstyLevelTwoFeatures =
        {
            Feature(SstyTag, 2)
        };

        private static readonly HB.hb_feature_t[] DtlsFeatures =
        {
            Feature(DtlsTag, 1)
        };

        private static readonly HB.hb_feature_t[] FlacFeatures =
        {
            Feature(FlacTag, 1)
        };

        private static readonly HB.hb_feature_t[] SstyLevelOneDtlsFeatures =
        {
            Feature(SstyTag, 1), Feature(DtlsTag, 1)
        };

        private static readonly HB.hb_feature_t[] SstyLevelTwoDtlsFeatures =
        {
            Feature(SstyTag, 2), Feature(DtlsTag, 1)
        };

        private static readonly HB.hb_feature_t[] SstyLevelOneFlacFeatures =
        {
            Feature(SstyTag, 1), Feature(FlacTag, 1)
        };

        private static readonly HB.hb_feature_t[] SstyLevelTwoFlacFeatures =
        {
            Feature(SstyTag, 2), Feature(FlacTag, 1)
        };

        private readonly int fontId;
        private readonly UniTextFontProvider fontProvider;
        private readonly TextProcessor textProcessor;
        private readonly Shaper.FontCacheEntry cache;
        private Shaper.FontCacheEntry.Lease cacheLease;
        private Shaper.FontCacheEntry.SubFontLease variationLease;
        private PooledBuffer<int> textCodepoints;
        private IntPtr mathFont;
        private uint extentsGlyph = uint.MaxValue;
        private HB.hb_glyph_extents_t glyphExtents;
        private int textMetricsFontId = int.MinValue;
        private Shaper.FontCacheEntry.Lease textMetricsLease;
        private Shaper.FontCacheEntry.SubFontLease textMetricsVariationLease;
        private IntPtr textMetricsFont;

        internal UniTextMathGlyphProvider(UniTextFontProvider fontProvider,
            TextProcessor textProcessor, int fontId)
        {
            this.fontProvider = fontProvider ?? throw new ArgumentNullException(nameof(fontProvider));
            this.textProcessor = textProcessor ?? throw new ArgumentNullException(nameof(textProcessor));
            this.fontId = fontId;
            var font = fontProvider.GetFont(fontId)
                ?? throw new InvalidOperationException($"Math font {fontId} is not registered with this text component.");
            cache = Shaper.GetOrCreateCoreCache(font)
                ?? throw new InvalidOperationException($"Math font '{font.Name}' has no readable font data.");
            if (!cache.HasMathData())
                throw new InvalidOperationException($"Math font '{font.Name}' does not contain an OpenType MATH table.");
            if (!cache.TryAcquire(out cacheLease))
                throw new ObjectDisposedException(nameof(Shaper.FontCacheEntry));

            try
            {
                textCodepoints.Rent(16);
                var variations = font.DefaultHbVariations;
                if (variations != null && variations.Length != 0)
                {
                    variationLease = cache.AcquireSubFont(cacheLease, fontId, variations);
                    mathFont = variationLease.Pointer;
                }
                else
                {
                    mathFont = cacheLease.Font;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public bool TryGetGlyphId(int requestedFontId, int codepoint, int scriptLevel,
            MathGlyphForm form, out uint glyphId)
        {
            EnsureFont(requestedFontId);
            scriptLevel = Math.Max(0, Math.Min(scriptLevel, 2));
            return cache.TryGetAlternateGlyph(mathFont, fontId, (uint)codepoint, HB.Script.Math,
                Features(scriptLevel, form), out glyphId);
        }

        public void ShapeMathText(ReadOnlySpan<char> text, float fontSize, int scriptLevel,
            ref PooledBuffer<ShapedGlyph> glyphs, ref PooledBuffer<int> glyphFonts,
            out float width)
        {
            var glyphStart = glyphs.count;
            var fontStart = glyphFonts.count;
            ShapeTextCore(text, fontSize, ref glyphs, ref glyphFonts, out width, fontId,
                Features(Math.Max(0, Math.Min(scriptLevel, 2)), MathGlyphForm.Default));

            var glyphCount = glyphs.count - glyphStart;
            if (glyphFonts.count - fontStart != glyphCount)
                throw new InvalidOperationException("Math text shaping returned mismatched glyph and font counts.");

            for (var i = 0; i < glyphCount; i++)
            {
                if (glyphFonts[fontStart + i] != fontId)
                    throw new InvalidOperationException("The math font cannot render a named operator without fallback.");
                if (glyphs[glyphStart + i].glyphId == 0)
                    throw new InvalidOperationException("The math font produced a missing glyph for a named operator.");
            }
        }

        public void ShapeText(ReadOnlySpan<char> text, float fontSize,
            ref PooledBuffer<ShapedGlyph> glyphs, ref PooledBuffer<int> glyphFonts,
            out float width)
        {
            ShapeTextCore(text, fontSize, ref glyphs, ref glyphFonts, out width, 0, null);
        }

        public void GetTextLineMetrics(int requestedFontId, float fontSize,
            out float height, out float depth)
        {
            var font = fontProvider.GetFont(requestedFontId)
                ?? throw new InvalidOperationException($"Text shaping returned unknown font {requestedFontId}.");
            var scale = fontProvider.MetricScale(requestedFontId, fontSize);
            height = Math.Max(0f, font.FaceInfo.ascentLine * scale);
            depth = Math.Max(0f, -font.FaceInfo.descentLine * scale);
        }

        public bool TryGetTextGlyphExtents(int requestedFontId, uint glyphId, float fontSize,
            out float height, out float depth)
        {
            var font = fontProvider.GetFont(requestedFontId)
                ?? throw new InvalidOperationException($"Text shaping returned unknown font {requestedFontId}.");
            var hbFont = GetTextMetricsFont(requestedFontId, font);
            if (!HB.TryGetGlyphExtents(hbFont, glyphId, out var extents))
            {
                height = 0f;
                depth = 0f;
                return false;
            }

            var scale = fontProvider.MetricScale(requestedFontId, fontSize);
            var top = extents.yBearing * scale;
            var bottom = (extents.yBearing + extents.height) * scale;
            if (font.TryGetGlyphQuadOverride(glyphId,
                    out var glyphScale, out _, out var offsetY))
            {
                var offset = offsetY * font.UnitsPerEm * scale;
                top = top * glyphScale + offset;
                bottom = bottom * glyphScale + offset;
            }
            height = Math.Max(0f, top);
            depth = Math.Max(0f, -bottom);
            return true;
        }

        public int GetAdvanceWidth(int requestedFontId, uint glyphId)
        {
            EnsureFont(requestedFontId);
            return HB.GetGlyphAdvance(mathFont, glyphId);
        }

        public int GetGlyphHeight(int requestedFontId, uint glyphId)
        {
            EnsureFont(requestedFontId);
            return Math.Max(0, GetGlyphExtents(glyphId).yBearing);
        }

        public int GetGlyphDepth(int requestedFontId, uint glyphId)
        {
            EnsureFont(requestedFontId);
            var extents = GetGlyphExtents(glyphId);
            return Math.Max(0, -(extents.yBearing + extents.height));
        }

        public int GetItalicCorrection(int requestedFontId, uint glyphId)
        {
            EnsureFont(requestedFontId);
            return HB.MathGetGlyphItalicsCorrection(mathFont, glyphId);
        }

        public int GetTopAccentAttachment(int requestedFontId, uint glyphId)
        {
            EnsureFont(requestedFontId);
            return HB.MathGetGlyphTopAccentAttachment(mathFont, glyphId);
        }

        public bool IsGlyphExtendedShape(int requestedFontId, uint glyphId)
        {
            EnsureFont(requestedFontId);
            return HB.MathIsGlyphExtendedShape(mathFont, glyphId);
        }

        public int GetGlyphKerning(int requestedFontId, uint glyphId, HB.MathKern kern,
            int correctionHeight)
        {
            EnsureFont(requestedFontId);
            return HB.MathGetGlyphKerning(mathFont, glyphId, (int)kern, correctionHeight);
        }

        public int GetGlyphVariants(int requestedFontId, uint glyphId, int direction,
            HB.hb_ot_math_glyph_variant_t[] variants)
        {
            EnsureFont(requestedFontId);
            return HB.MathGetGlyphVariants(mathFont, glyphId, direction, variants);
        }

        public int GetMinConnectorOverlap(int requestedFontId, int direction)
        {
            EnsureFont(requestedFontId);
            return HB.MathGetMinConnectorOverlap(mathFont, direction);
        }

        public int GetGlyphAssembly(int requestedFontId, uint glyphId, int direction,
            HB.hb_ot_math_glyph_part_t[] parts, out int italicsCorrection)
        {
            EnsureFont(requestedFontId);
            return HB.MathGetGlyphAssembly(mathFont, glyphId, direction, parts, out italicsCorrection);
        }

        internal MathFontMetrics LoadMetrics()
        {
            var metrics = new MathFontMetrics
            {
                scriptPercentScaleDown = Constant(HB.MathConstant.ScriptPercentScaleDown),
                scriptScriptPercentScaleDown = Constant(HB.MathConstant.ScriptScriptPercentScaleDown),
                displayOperatorMinHeight = Constant(HB.MathConstant.DisplayOperatorMinHeight),
                axisHeight = Constant(HB.MathConstant.AxisHeight),
                accentBaseHeight = Constant(HB.MathConstant.AccentBaseHeight),
                flattenedAccentBaseHeight = Constant(HB.MathConstant.FlattenedAccentBaseHeight),
                subscriptShiftDown = Constant(HB.MathConstant.SubscriptShiftDown),
                subscriptTopMax = Constant(HB.MathConstant.SubscriptTopMax),
                subscriptBaselineDropMin = Constant(HB.MathConstant.SubscriptBaselineDropMin),
                superscriptShiftUp = Constant(HB.MathConstant.SuperscriptShiftUp),
                superscriptShiftUpCramped = Constant(HB.MathConstant.SuperscriptShiftUpCramped),
                superscriptBottomMin = Constant(HB.MathConstant.SuperscriptBottomMin),
                superscriptBaselineDropMax = Constant(HB.MathConstant.SuperscriptBaselineDropMax),
                subSuperscriptGapMin = Constant(HB.MathConstant.SubSuperscriptGapMin),
                superscriptBottomMaxWithSubscript = Constant(HB.MathConstant.SuperscriptBottomMaxWithSubscript),
                spaceAfterScript = Constant(HB.MathConstant.SpaceAfterScript),
                upperLimitGapMin = Constant(HB.MathConstant.UpperLimitGapMin),
                upperLimitBaselineRiseMin = Constant(HB.MathConstant.UpperLimitBaselineRiseMin),
                lowerLimitGapMin = Constant(HB.MathConstant.LowerLimitGapMin),
                lowerLimitBaselineDropMin = Constant(HB.MathConstant.LowerLimitBaselineDropMin),
                stackTopShiftUp = Constant(HB.MathConstant.StackTopShiftUp),
                stackTopDisplayStyleShiftUp = Constant(HB.MathConstant.StackTopDisplayStyleShiftUp),
                stackBottomShiftDown = Constant(HB.MathConstant.StackBottomShiftDown),
                stackBottomDisplayStyleShiftDown = Constant(HB.MathConstant.StackBottomDisplayStyleShiftDown),
                stackGapMin = Constant(HB.MathConstant.StackGapMin),
                stackDisplayStyleGapMin = Constant(HB.MathConstant.StackDisplayStyleGapMin),
                fractionNumeratorShiftUp = Constant(HB.MathConstant.FractionNumeratorShiftUp),
                fractionNumeratorDisplayStyleShiftUp = Constant(HB.MathConstant.FractionNumeratorDisplayStyleShiftUp),
                fractionDenominatorShiftDown = Constant(HB.MathConstant.FractionDenominatorShiftDown),
                fractionDenominatorDisplayStyleShiftDown = Constant(HB.MathConstant.FractionDenominatorDisplayStyleShiftDown),
                fractionNumeratorGapMin = Constant(HB.MathConstant.FractionNumeratorGapMin),
                fractionNumDisplayStyleGapMin = Constant(HB.MathConstant.FractionNumDisplayStyleGapMin),
                fractionRuleThickness = Constant(HB.MathConstant.FractionRuleThickness),
                fractionDenominatorGapMin = Constant(HB.MathConstant.FractionDenominatorGapMin),
                fractionDenomDisplayStyleGapMin = Constant(HB.MathConstant.FractionDenomDisplayStyleGapMin),
                overbarVerticalGap = Constant(HB.MathConstant.OverbarVerticalGap),
                overbarRuleThickness = Constant(HB.MathConstant.OverbarRuleThickness),
                overbarExtraAscender = Constant(HB.MathConstant.OverbarExtraAscender),
                underbarVerticalGap = Constant(HB.MathConstant.UnderbarVerticalGap),
                underbarRuleThickness = Constant(HB.MathConstant.UnderbarRuleThickness),
                underbarExtraDescender = Constant(HB.MathConstant.UnderbarExtraDescender),
                radicalVerticalGap = Constant(HB.MathConstant.RadicalVerticalGap),
                radicalDisplayStyleVerticalGap = Constant(HB.MathConstant.RadicalDisplayStyleVerticalGap),
                radicalRuleThickness = Constant(HB.MathConstant.RadicalRuleThickness),
                radicalExtraAscender = Constant(HB.MathConstant.RadicalExtraAscender),
                radicalKernBeforeDegree = Constant(HB.MathConstant.RadicalKernBeforeDegree),
                radicalKernAfterDegree = Constant(HB.MathConstant.RadicalKernAfterDegree),
                radicalDegreeBottomRaisePercent = Constant(HB.MathConstant.RadicalDegreeBottomRaisePercent)
            };

            if (metrics.scriptPercentScaleDown <= 0 || metrics.scriptScriptPercentScaleDown <= 0)
                throw new InvalidOperationException("Math font contains invalid script scale constants.");
            if (metrics.fractionRuleThickness <= 0 || metrics.radicalRuleThickness <= 0
                || metrics.overbarRuleThickness <= 0 || metrics.underbarRuleThickness <= 0)
                throw new InvalidOperationException("Math font contains invalid rule thickness constants.");
            return metrics;
        }

        private int Constant(HB.MathConstant constant) => HB.MathGetConstant(mathFont, constant);

        private static HB.hb_feature_t Feature(uint tag, uint value)
        {
            return new HB.hb_feature_t
            {
                tag = tag,
                value = value,
                start = HB.hb_feature_t.GLOBAL_START,
                end = HB.hb_feature_t.GLOBAL_END
            };
        }

        private static HB.hb_feature_t[] Features(int scriptLevel, MathGlyphForm form)
        {
            return form switch
            {
                MathGlyphForm.Default => scriptLevel switch
                {
                    0 => null,
                    1 => SstyLevelOneFeatures,
                    _ => SstyLevelTwoFeatures
                },
                MathGlyphForm.Dotless => scriptLevel switch
                {
                    0 => DtlsFeatures,
                    1 => SstyLevelOneDtlsFeatures,
                    _ => SstyLevelTwoDtlsFeatures
                },
                MathGlyphForm.FlattenedAccent => scriptLevel switch
                {
                    0 => FlacFeatures,
                    1 => SstyLevelOneFlacFeatures,
                    _ => SstyLevelTwoFlacFeatures
                },
                _ => throw new ArgumentOutOfRangeException(nameof(form))
            };
        }

        private void ShapeTextCore(ReadOnlySpan<char> text, float fontSize,
            ref PooledBuffer<ShapedGlyph> glyphs, ref PooledBuffer<int> glyphFonts,
            out float width, int uniformFontId, HB.hb_feature_t[] features)
        {
            textCodepoints.FakeClear();
            var codepointCount = UnicodeData.CountCodepoints(text);
            textCodepoints.EnsureCapacity(codepointCount);
            for (var i = 0; i < text.Length;)
            {
                var codepoint = (int)UnicodeData.DecodeAt(text, i, out var utf16Length);
                i += utf16Length;
                textCodepoints.Add(codepoint == '~' ? UnicodeData.Space : codepoint);
            }

            var providerFontSize = fontProvider.FontSize;
            if (!(providerFontSize > 0f))
                throw new InvalidOperationException("Text shaping requires a finite positive provider font size.");
            textProcessor.ShapeIsolatedRun(textCodepoints.Span, fontSize / providerFontSize,
                ref glyphs, ref glyphFonts, out width, uniformFontId, features);
        }

        private HB.hb_glyph_extents_t GetGlyphExtents(uint glyphId)
        {
            if (extentsGlyph == glyphId)
                return glyphExtents;
            if (mathFont == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(UniTextMathGlyphProvider));
            if (!HB.TryGetGlyphExtents(mathFont, glyphId, out glyphExtents))
                throw new InvalidOperationException($"HarfBuzz could not read math glyph {glyphId} extents.");

            extentsGlyph = glyphId;
            return glyphExtents;
        }

        private IntPtr GetTextMetricsFont(int requestedFontId, UniTextFont.Core font)
        {
            if (requestedFontId == fontId)
                return mathFont;
            if (textMetricsFontId == requestedFontId && textMetricsFont != IntPtr.Zero)
                return textMetricsFont;

            ReleaseTextMetricsFont();
            var cacheEntry = Shaper.GetOrCreateCoreCache(font)
                ?? throw new InvalidOperationException($"Text font '{font.Name}' has no readable font data.");
            if (!cacheEntry.TryAcquire(out textMetricsLease))
                throw new ObjectDisposedException(nameof(Shaper.FontCacheEntry));

            try
            {
                textMetricsFont = textMetricsLease.Font;
                var variations = font.DefaultHbVariations;
                if (variations != null && variations.Length != 0)
                {
                    textMetricsVariationLease = cacheEntry.AcquireSubFont(
                        textMetricsLease, requestedFontId, variations);
                    textMetricsFont = textMetricsVariationLease.Pointer;
                }
                textMetricsFontId = requestedFontId;
                return textMetricsFont;
            }
            catch
            {
                ReleaseTextMetricsFont();
                throw;
            }
        }

        private void ReleaseTextMetricsFont()
        {
            textMetricsVariationLease.Dispose();
            textMetricsVariationLease = default;
            textMetricsLease.Dispose();
            textMetricsLease = default;
            textMetricsFont = IntPtr.Zero;
            textMetricsFontId = int.MinValue;
        }

        private void EnsureFont(int requestedFontId)
        {
            if (requestedFontId != fontId)
                throw new InvalidOperationException($"Math layout requested font {requestedFontId}, but this provider owns {fontId}.");
        }

        public void Dispose()
        {
            textCodepoints.Return();
            ReleaseTextMetricsFont();
            mathFont = IntPtr.Zero;
            variationLease.Dispose();
            variationLease = default;
            cacheLease.Dispose();
            cacheLease = default;
            extentsGlyph = uint.MaxValue;
        }
    }
}
