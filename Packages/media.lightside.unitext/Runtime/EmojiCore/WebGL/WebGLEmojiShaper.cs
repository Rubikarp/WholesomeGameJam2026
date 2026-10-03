#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Text shaper for emoji sequences in WebGL builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Converts emoji codepoint sequences into shaped glyphs for rendering.
    /// Handles grapheme cluster segmentation and ZWJ sequence fallback.
    /// </para>
    /// <para>
    /// When a ZWJ sequence is not supported by the browser, falls back to
    /// rendering individual emoji components separately.
    /// </para>
    /// </remarks>
    /// <seealso cref="WebGLEmoji"/>
    /// <seealso cref="EmojiFont"/>
    internal static class WebGLEmojiShaper
    {
        [ThreadStatic] private static ShapedGlyph[] outputBuffer;
        [ThreadStatic] private static bool[] graphemeBreaks;
        [ThreadStatic] private static int[] presentationForcedBuffer;

        private static readonly FastIntDictionary<bool> zwjSupportCache = new();

        /// <summary>
        /// Default-text emoji symbols (Extended_Pictographic=Yes, Emoji_Presentation=No)
        /// need U+FE0F (VS16) appended so Canvas 2D font-selection picks the emoji font
        /// instead of monochrome text fallback. Desktop HarfBuzz path does not need this
        /// because shaping is performed directly against the emoji font.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool NeedsEmojiPresentationForcing(int codepoint)
        {
            var provider = UnicodeData.Provider;
            return provider.IsExtendedPictographic(codepoint) && !provider.IsEmojiPresentation(codepoint);
        }

        /// <summary>
        /// Returns cluster with VS16 appended if it's a single default-text emoji codepoint
        /// without an existing variation selector; otherwise returns the original cluster.
        /// </summary>
        private static ReadOnlySpan<int> EnsureEmojiPresentation(ReadOnlySpan<int> cluster)
        {
            if (cluster.Length != 1 || !NeedsEmojiPresentationForcing(cluster[0]))
                return cluster;

            if (presentationForcedBuffer == null || presentationForcedBuffer.Length < 2)
                presentationForcedBuffer = new int[2];

            presentationForcedBuffer[0] = cluster[0];
            presentationForcedBuffer[1] = UnicodeData.VariationSelector16;
            return presentationForcedBuffer.AsSpan(0, 2);
        }

        private static ShapedGlyph[] EnsureOutputBuffer(int capacity)
        {
            if (outputBuffer == null || outputBuffer.Length < capacity)
                outputBuffer = new ShapedGlyph[Math.Max(capacity, 64)];
            return outputBuffer;
        }

        /// <summary>Layout advance of an emoji sequence: the browser's measured pixel width scaled to layout units, so line-breaking matches the rendered glyph. Falls back to one em when the browser cannot measure it.</summary>
        private static float MeasuredAdvance(ReadOnlySpan<int> sequence, int pixelSize, float fontSize)
        {
            var widthPx = WebGLEmoji.MeasureEmoji(sequence, pixelSize);
            return widthPx > 0f ? widthPx * fontSize / pixelSize : fontSize;
        }

        /// <summary>Shapes emoji codepoints into positioned glyphs.</summary>
        /// <param name="codepoints">Input emoji codepoint sequence.</param>
        /// <param name="fontSize">Desired font size.</param>
        /// <param name="upem">Units per em of the font.</param>
        /// <returns>Shaping result with positioned glyphs and total advance.</returns>
        /// <remarks>
        /// Segments input by grapheme clusters. For unsupported ZWJ sequences,
        /// splits into individual components for separate rendering.
        /// </remarks>
        public static ShapingResult Shape(
            ReadOnlySpan<int> codepoints,
            float fontSize,
            int upem,
            int clusterOffset = 0)
        {
            if (codepoints.IsEmpty || !WebGLEmoji.IsSupported)
                return new ShapingResult(ReadOnlySpan<ShapedGlyph>.Empty, 0);

            int cpCount = codepoints.Length;

            if (graphemeBreaks == null || graphemeBreaks.Length < cpCount + 1)
                graphemeBreaks = new bool[Math.Max(cpCount + 1, 256)];

            Array.Clear(graphemeBreaks, 0, cpCount + 1);
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(codepoints, graphemeBreaks.AsSpan(0, cpCount + 1));

            int clusterCount = 0;
            int maxPossibleGlyphs = 0;
            int tempStart = 0;
            for (int i = 1; i <= cpCount; i++)
            {
                if (graphemeBreaks[i])
                {
                    clusterCount++;
                    int clusterLen = i - tempStart;
                    maxPossibleGlyphs += Math.Max(1, CountZWJ(codepoints.Slice(tempStart, clusterLen)) + 1);
                    tempStart = i;
                }
            }

            if (clusterCount == 0)
            {
                clusterCount = 1;
                maxPossibleGlyphs = 1;
            }

            var outBuf = EnsureOutputBuffer(maxPossibleGlyphs);
            float totalAdvance = 0;
            int glyphIndex = 0;
            int clusterStart = 0;
            int pixelSize = EmojiFont.Instance?.ColorPixelSize ?? EmojiFont.DefaultSize;

            for (int i = 1; i <= cpCount; i++)
            {
                if (!graphemeBreaks[i])
                    continue;

                var cluster = codepoints.Slice(clusterStart, i - clusterStart);

                if (ContainsZWJ(cluster) && !IsZwjSequenceSupported(cluster))
                {
                    int subStart = 0;
                    for (int j = 0; j < cluster.Length; j++)
                    {
                        bool isZwj = cluster[j] == UnicodeData.ZeroWidthJoiner;
                        bool isLast = j == cluster.Length - 1;

                        if (isZwj || isLast)
                        {
                            int subEnd = isZwj ? j : j + 1;
                            if (subEnd > subStart)
                            {
                                var subcluster = cluster.Slice(subStart, subEnd - subStart);
                                var subPres = EnsureEmojiPresentation(subcluster);
                                uint subHash = WebGLEmoji.RegisterSequence(subPres);
                                float subAdvance = MeasuredAdvance(subPres, pixelSize, fontSize);

                                outBuf[glyphIndex++] = new ShapedGlyph
                                {
                                    glyphId = (int)subHash,
                                    cluster = clusterOffset + clusterStart + subStart,
                                    advanceX = subAdvance,
                                    advanceY = 0,
                                    offsetX = 0,
                                    offsetY = 0
                                };
                                totalAdvance += subAdvance;
                            }
                            subStart = j + 1;
                        }
                    }
                }
                else
                {
                    var registerCluster = EnsureEmojiPresentation(cluster);
                    uint hash = WebGLEmoji.RegisterSequence(registerCluster);
                    float advance = MeasuredAdvance(registerCluster, pixelSize, fontSize);

                    outBuf[glyphIndex++] = new ShapedGlyph
                    {
                        glyphId = (int)hash,
                        cluster = clusterOffset + clusterStart,
                        advanceX = advance,
                        advanceY = 0,
                        offsetX = 0,
                        offsetY = 0
                    };
                    totalAdvance += advance;
                }

                clusterStart = i;
            }

            return new ShapingResult(outBuf.AsSpan(0, glyphIndex), totalAdvance);
        }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsZwjSequenceSupported(ReadOnlySpan<int> cluster)
        {
            uint hash = WebGLEmoji.ComputeSequenceHash(cluster);
            int hashInt = (int)hash;

            if (zwjSupportCache.TryGetValue(hashInt, out bool cached))
                return cached;

            int pixelSize = EmojiFont.Instance?.ColorPixelSize ?? EmojiFont.DefaultSize;
            bool isSupported = WebGLEmoji.IsZwjSupported(cluster, pixelSize);

            zwjSupportCache[hashInt] = isSupported;
            return isSupported;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ContainsZWJ(ReadOnlySpan<int> cluster)
        {
            for (int i = 1; i < cluster.Length; i++)
            {
                if (cluster[i] == UnicodeData.ZeroWidthJoiner)
                    return true;
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountZWJ(ReadOnlySpan<int> cluster)
        {
            int count = 0;
            for (int i = 1; i < cluster.Length; i++)
            {
                if (cluster[i] == UnicodeData.ZeroWidthJoiner)
                    count++;
            }
            return count;
        }

        /// <summary>Gets a glyph index for a single codepoint.</summary>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <returns>Sequential glyph ID, or 0 if unsupported.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetGlyphIndex(uint codepoint)
        {
            if (!WebGLEmoji.IsSupported)
                return 0;

            Span<int> single = stackalloc int[1] { (int)codepoint };
            return WebGLEmoji.RegisterSequence(EnsureEmojiPresentation(single));
        }

        /// <summary>Gets glyph information for a single codepoint.</summary>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <param name="fontSize">Desired font size.</param>
        /// <param name="upem">Units per em of the font.</param>
        /// <param name="glyphIndex">Output glyph hash/ID.</param>
        /// <param name="advance">Output horizontal advance in font units.</param>
        /// <returns>True if glyph info was successfully retrieved.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetGlyphInfo(uint codepoint, float fontSize, int upem, out uint glyphIndex, out float advance)
        {
            glyphIndex = 0;
            advance = 0;

            if (!WebGLEmoji.IsSupported)
                return false;

            Span<int> single = stackalloc int[1] { (int)codepoint };
            var sequence = EnsureEmojiPresentation(single);

            glyphIndex = WebGLEmoji.RegisterSequence(sequence);

            int pixelSize = EmojiFont.Instance?.ColorPixelSize ?? EmojiFont.DefaultSize;
            float browserWidth = WebGLEmoji.MeasureEmoji(sequence, pixelSize);
            if (browserWidth <= 0)
                return false;

            float advanceInDesignUnits = browserWidth * upem / pixelSize;
            advance = advanceInDesignUnits * fontSize / upem;

            return true;
        }
    }
}
#endif
