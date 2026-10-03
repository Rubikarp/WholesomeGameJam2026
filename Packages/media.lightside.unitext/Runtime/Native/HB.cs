using System;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.CallingConvention;

namespace LightSide
{
    internal static unsafe class HB
    {
        #region Enums & Constants

        public const int DIRECTION_LTR = 4;
        public const int DIRECTION_RTL = 5;
        public const int DIRECTION_TTB = 6;
        public const int DIRECTION_BTT = 7;

        public const uint BUFFER_FLAG_DEFAULT = 0x0;
        public const uint BUFFER_FLAG_BOT = 0x1;
        public const uint BUFFER_FLAG_EOT = 0x2;
        public const uint BUFFER_FLAG_PRESERVE_DEFAULT_IGNORABLES = 0x4;
        public const uint BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES = 0x8;

        /// <summary>Glyph-flag bit (in <c>hb_glyph_info_t.mask</c>) marking a cluster where breaking the text is unsafe — its absence guarantees that splitting there and shaping the two sides separately equals shaping them together. Produced by default.</summary>
        public const uint GLYPH_FLAG_UNSAFE_TO_BREAK = 0x1;

        /// <summary>Mask of all currently-defined glyph flags in <c>hb_glyph_info_t.mask</c> (unsafe-to-break, unsafe-to-concat, safe-to-insert-tatweel). The high bits carry HarfBuzz's internal feature masks and must be stripped before reading flags — mirrors <c>hb_glyph_info_get_glyph_flags</c>.</summary>
        public const uint GLYPH_FLAG_DEFINED = 0x7;

        public static class Script
        {
            public const uint Common = ('Z' << 24) | ('y' << 16) | ('y' << 8) | 'y';
            public const uint Math = ('Z' << 24) | ('m' << 16) | ('t' << 8) | 'h';
            public const uint Arabic = ('A' << 24) | ('r' << 16) | ('a' << 8) | 'b';
            public const uint Armenian = ('A' << 24) | ('r' << 16) | ('m' << 8) | 'n';
            public const uint Bengali = ('B' << 24) | ('e' << 16) | ('n' << 8) | 'g';
            public const uint Cyrillic = ('C' << 24) | ('y' << 16) | ('r' << 8) | 'l';
            public const uint Devanagari = ('D' << 24) | ('e' << 16) | ('v' << 8) | 'a';
            public const uint Georgian = ('G' << 24) | ('e' << 16) | ('o' << 8) | 'r';
            public const uint Greek = ('G' << 24) | ('r' << 16) | ('e' << 8) | 'k';
            public const uint Gujarati = ('G' << 24) | ('u' << 16) | ('j' << 8) | 'r';
            public const uint Gurmukhi = ('G' << 24) | ('u' << 16) | ('r' << 8) | 'u';
            public const uint Han = ('H' << 24) | ('a' << 16) | ('n' << 8) | 'i';
            public const uint Hangul = ('H' << 24) | ('a' << 16) | ('n' << 8) | 'g';
            public const uint Hebrew = ('H' << 24) | ('e' << 16) | ('b' << 8) | 'r';
            public const uint Hiragana = ('H' << 24) | ('i' << 16) | ('r' << 8) | 'a';
            public const uint Kannada = ('K' << 24) | ('n' << 16) | ('d' << 8) | 'a';
            public const uint Katakana = ('K' << 24) | ('a' << 16) | ('n' << 8) | 'a';
            public const uint Khmer = ('K' << 24) | ('h' << 16) | ('m' << 8) | 'r';
            public const uint Lao = ('L' << 24) | ('a' << 16) | ('o' << 8) | 'o';
            public const uint Latin = ('L' << 24) | ('a' << 16) | ('t' << 8) | 'n';
            public const uint Malayalam = ('M' << 24) | ('l' << 16) | ('y' << 8) | 'm';
            public const uint Myanmar = ('M' << 24) | ('y' << 16) | ('m' << 8) | 'r';
            public const uint Oriya = ('O' << 24) | ('r' << 16) | ('y' << 8) | 'a';
            public const uint Sinhala = ('S' << 24) | ('i' << 16) | ('n' << 8) | 'h';
            public const uint Tamil = ('T' << 24) | ('a' << 16) | ('m' << 8) | 'l';
            public const uint Telugu = ('T' << 24) | ('e' << 16) | ('l' << 8) | 'u';
            public const uint Thai = ('T' << 24) | ('h' << 16) | ('a' << 8) | 'i';
            public const uint Tibetan = ('T' << 24) | ('i' << 16) | ('b' << 8) | 't';
        }

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        public struct GlyphInfo
        {
            public uint glyphId;
            public uint cluster;
            public int xAdvance;
            public int yAdvance;
            public int xOffset;
            public int yOffset;
        }

        #endregion

        #region Platform-specific bindings

        [ThreadStatic] private static GlyphInfo[] glyphInfoBuffer;

    #if (UNITY_IOS || UNITY_TVOS || UNITY_VISIONOS || UNITY_WEBGL) && !UNITY_EDITOR
        private const string LibraryName = "__Internal";
    #else
        private const string LibraryName = "unitext_native";
    #endif

        private const int HB_MEMORY_MODE_READONLY = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_glyph_info_t
        {
            public uint codepoint;
            public uint mask;
            public uint cluster;
            private uint var1, var2;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_glyph_position_t
        {
            public int x_advance, y_advance;
            public int x_offset, y_offset;
            private uint var;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_glyph_extents_t
        {
            public int xBearing;
            public int yBearing;
            public int width;
            public int height;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_ot_math_glyph_variant_t
        {
            public uint glyph;
            public int advance;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_ot_math_glyph_part_t
        {
            public uint glyph;
            public int startConnectorLength;
            public int endConnectorLength;
            public int fullAdvance;
            public uint flags;
        }

        internal enum MathConstant
        {
            ScriptPercentScaleDown,
            ScriptScriptPercentScaleDown,
            DelimitedSubFormulaMinHeight,
            DisplayOperatorMinHeight,
            MathLeading,
            AxisHeight,
            AccentBaseHeight,
            FlattenedAccentBaseHeight,
            SubscriptShiftDown,
            SubscriptTopMax,
            SubscriptBaselineDropMin,
            SuperscriptShiftUp,
            SuperscriptShiftUpCramped,
            SuperscriptBottomMin,
            SuperscriptBaselineDropMax,
            SubSuperscriptGapMin,
            SuperscriptBottomMaxWithSubscript,
            SpaceAfterScript,
            UpperLimitGapMin,
            UpperLimitBaselineRiseMin,
            LowerLimitGapMin,
            LowerLimitBaselineDropMin,
            StackTopShiftUp,
            StackTopDisplayStyleShiftUp,
            StackBottomShiftDown,
            StackBottomDisplayStyleShiftDown,
            StackGapMin,
            StackDisplayStyleGapMin,
            StretchStackTopShiftUp,
            StretchStackBottomShiftDown,
            StretchStackGapAboveMin,
            StretchStackGapBelowMin,
            FractionNumeratorShiftUp,
            FractionNumeratorDisplayStyleShiftUp,
            FractionDenominatorShiftDown,
            FractionDenominatorDisplayStyleShiftDown,
            FractionNumeratorGapMin,
            FractionNumDisplayStyleGapMin,
            FractionRuleThickness,
            FractionDenominatorGapMin,
            FractionDenomDisplayStyleGapMin,
            SkewedFractionHorizontalGap,
            SkewedFractionVerticalGap,
            OverbarVerticalGap,
            OverbarRuleThickness,
            OverbarExtraAscender,
            UnderbarVerticalGap,
            UnderbarRuleThickness,
            UnderbarExtraDescender,
            RadicalVerticalGap,
            RadicalDisplayStyleVerticalGap,
            RadicalRuleThickness,
            RadicalExtraAscender,
            RadicalKernBeforeDegree,
            RadicalKernAfterDegree,
            RadicalDegreeBottomRaisePercent
        }

        internal const uint HB_OT_MATH_GLYPH_PART_FLAG_EXTENDER = 1;

        internal enum MathKern
        {
            TopRight,
            TopLeft,
            BottomRight,
            BottomLeft
        }

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_blob_create(IntPtr data, uint length, int mode, IntPtr userData, IntPtr destroy);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_blob_destroy(IntPtr blob);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_face_create(IntPtr blob, uint index);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_face_destroy(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_face_get_upem(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_font_create(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_font_destroy(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_face_make_immutable(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_font_make_immutable(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_font_create_sub_font(IntPtr parent);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_ot_font_set_funcs(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl, EntryPoint = "ut_hb_font_get_glyph_h_advance")] private static extern int ut_hb_font_get_glyph_h_advance(IntPtr font, uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_font_get_glyph_extents(IntPtr font, uint glyph, out hb_glyph_extents_t extents);
        [DllImport(LibraryName, CallingConvention = Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ut_hb_font_get_glyph(IntPtr font, uint unicode, uint variationSelector, out uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_font_get_face(IntPtr font);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_has_data(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_get_constant(IntPtr font, int constant);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_get_glyph_italics_correction(IntPtr font, uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_get_glyph_top_accent_attachment(IntPtr font, uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_is_glyph_extended_shape(IntPtr font, uint glyph);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_get_glyph_kerning(IntPtr font, uint glyph, int kern, int correctionHeight);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_ot_math_get_glyph_variants(IntPtr font, uint glyph, int direction, uint capacity, hb_ot_math_glyph_variant_t* variants);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern int ut_hb_ot_math_get_min_connector_overlap(IntPtr font, int direction);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_ot_math_get_glyph_assembly(IntPtr font, uint glyph, int direction, uint capacity, hb_ot_math_glyph_part_t* parts, out int italicsCorrection);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_buffer_create();
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_destroy(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_clear_contents(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_direction(IntPtr buffer, int direction);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_script(IntPtr buffer, uint script);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_content_type(IntPtr buffer, int contentType);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_flags(IntPtr buffer, uint flags);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_add_codepoints(IntPtr buffer, uint* codepoints, int textLength, uint itemOffset, int itemLength);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_buffer_get_length(IntPtr buffer);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern hb_glyph_info_t* ut_hb_buffer_get_glyph_infos(IntPtr buffer, out uint length);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern hb_glyph_position_t* ut_hb_buffer_get_glyph_positions(IntPtr buffer, out uint length);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_shape(IntPtr font, IntPtr buffer, IntPtr features, uint numFeatures);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern unsafe int ut_hb_shape_run(
            IntPtr font, IntPtr buffer,
            uint* codepoints, int textLength,
            uint itemOffset, int itemLength,
            int direction, uint scriptTag, uint flags,
            hb_feature_t* features, uint numFeatures,
            hb_glyph_info_t** outInfos, hb_glyph_position_t** outPositions);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern IntPtr ut_hb_language_from_string(byte* str, int len);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_buffer_set_language(IntPtr buffer, IntPtr language);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern unsafe int ut_hb_shape_run_lang(
            IntPtr font, IntPtr buffer,
            uint* codepoints, int textLength,
            uint itemOffset, int itemLength,
            int direction, uint scriptTag, IntPtr language, uint flags,
            hb_feature_t* features, uint numFeatures,
            hb_glyph_info_t** outInfos, hb_glyph_position_t** outPositions);

        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_ot_var_get_axis_count(IntPtr face);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern uint ut_hb_ot_var_get_axis_infos(IntPtr face, uint startOffset, ref uint axesCount, hb_ot_var_axis_info_t* axesInfo);
        [DllImport(LibraryName, CallingConvention = Cdecl)] private static extern void ut_hb_font_set_variations(IntPtr font, hb_variation_t* variations, uint variationsLength);

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_ot_var_axis_info_t
        {
            public uint axisIndex;
            public uint tag;
            public uint nameId;
            public uint flags;
            public float minValue;
            public float defaultValue;
            public float maxValue;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_variation_t
        {
            public uint tag;
            public float value;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct hb_feature_t
        {
            public uint tag;
            public uint value;
            public uint start;
            public uint end;

            public const uint GLOBAL_START = 0;
            public const uint GLOBAL_END = 0xFFFFFFFF;
        }

        internal static uint MakeTag(char a, char b, char c, char d)
        {
            return (uint)((a << 24) | (b << 16) | (c << 8) | d);
        }

        #endregion

        #region Unified Font API

        public static IntPtr CreateFont(IntPtr ftFace, IntPtr fontDataPtr, int fontDataLength, out IntPtr blob, out IntPtr hbFace, out int upem, int faceIndex = 0)
        {
            blob = IntPtr.Zero;
            hbFace = IntPtr.Zero;
            upem = 1000;

            if (fontDataPtr == IntPtr.Zero || fontDataLength <= 0)
                return IntPtr.Zero;

            blob = ut_hb_blob_create(fontDataPtr, (uint)fontDataLength, HB_MEMORY_MODE_READONLY, IntPtr.Zero, IntPtr.Zero);
            if (blob == IntPtr.Zero)
                return IntPtr.Zero;

            hbFace = ut_hb_face_create(blob, faceIndex > 0 ? (uint)faceIndex : 0u);
            if (hbFace == IntPtr.Zero)
            {
                ut_hb_blob_destroy(blob);
                blob = IntPtr.Zero;
                return IntPtr.Zero;
            }

            upem = (int)ut_hb_face_get_upem(hbFace);

            IntPtr font = ut_hb_font_create(hbFace);
            if (font == IntPtr.Zero)
            {
                ut_hb_face_destroy(hbFace);
                ut_hb_blob_destroy(blob);
                hbFace = IntPtr.Zero;
                blob = IntPtr.Zero;
                return IntPtr.Zero;
            }

            ut_hb_ot_font_set_funcs(font);
            return font;
        }

        public static void DestroyFont(IntPtr font, IntPtr blob, IntPtr hbFace)
        {
            if (font == IntPtr.Zero) return;

            ut_hb_font_destroy(font);
            if (hbFace != IntPtr.Zero) ut_hb_face_destroy(hbFace);
            if (blob != IntPtr.Zero) ut_hb_blob_destroy(blob);
        }

        public static int GetGlyphAdvance(IntPtr font, uint glyphIndex)
        {
            if (font == IntPtr.Zero) return 0;
            return ut_hb_font_get_glyph_h_advance(font, glyphIndex);
        }

        internal static bool TryGetGlyphExtents(IntPtr font, uint glyphIndex,
            out hb_glyph_extents_t extents)
        {
            extents = default;
            return font != IntPtr.Zero
                   && ut_hb_font_get_glyph_extents(font, glyphIndex, out extents) != 0;
        }

        internal static bool MathHasData(IntPtr face)
            => face != IntPtr.Zero && ut_hb_ot_math_has_data(face) != 0;

        internal static int MathGetConstant(IntPtr font, MathConstant constant)
            => ut_hb_ot_math_get_constant(font, (int)constant);

        internal static int MathGetGlyphItalicsCorrection(IntPtr font, uint glyph)
            => ut_hb_ot_math_get_glyph_italics_correction(font, glyph);

        internal static int MathGetGlyphTopAccentAttachment(IntPtr font, uint glyph)
            => ut_hb_ot_math_get_glyph_top_accent_attachment(font, glyph);

        internal static bool MathIsGlyphExtendedShape(IntPtr font, uint glyph)
            => ut_hb_ot_math_is_glyph_extended_shape(font, glyph) != 0;

        internal static int MathGetGlyphKerning(IntPtr font, uint glyph, int kern, int correctionHeight)
            => ut_hb_ot_math_get_glyph_kerning(font, glyph, kern, correctionHeight);

        internal static int MathGetGlyphVariants(IntPtr font, uint glyph, int direction,
            hb_ot_math_glyph_variant_t[] variants)
        {
            fixed (hb_ot_math_glyph_variant_t* ptr = variants)
                return (int)ut_hb_ot_math_get_glyph_variants(font, glyph, direction, (uint)variants.Length, ptr);
        }

        internal static int MathGetMinConnectorOverlap(IntPtr font, int direction)
            => ut_hb_ot_math_get_min_connector_overlap(font, direction);

        internal static int MathGetGlyphAssembly(IntPtr font, uint glyph, int direction,
            hb_ot_math_glyph_part_t[] parts, out int italicsCorrection)
        {
            fixed (hb_ot_math_glyph_part_t* ptr = parts)
                return (int)ut_hb_ot_math_get_glyph_assembly(font, glyph, direction, (uint)parts.Length,
                    ptr, out italicsCorrection);
        }

        public static bool TryGetGlyph(IntPtr font, uint codepoint, out uint glyphIndex)
        {
            glyphIndex = 0;
            if (font == IntPtr.Zero) return false;
            return ut_hb_font_get_glyph(font, codepoint, 0, out glyphIndex);
        }

        #endregion

        #region Unified Buffer API

        public static IntPtr CreateBuffer()
        {
            return ut_hb_buffer_create();
        }

        public static void DestroyBuffer(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_destroy(buffer);
        }

        public static void ClearBuffer(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_clear_contents(buffer);
        }

        public static void SetDirection(IntPtr buffer, int direction)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_direction(buffer, direction);
        }

        public static void SetScript(IntPtr buffer, uint script)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_script(buffer, script);
        }

        /// <summary>
        /// Converts a BCP 47 language tag string to an opaque hb_language_t handle.
        /// HarfBuzz canonicalizes the string and caches it, so returned pointers for equal
        /// strings are equal. The handle is valid for the process lifetime.
        /// </summary>
        public static IntPtr LanguageFromString(string bcp47)
        {
            if (string.IsNullOrEmpty(bcp47)) return IntPtr.Zero;

            var byteCount = System.Text.Encoding.ASCII.GetByteCount(bcp47);
            Span<byte> buffer = stackalloc byte[byteCount];
            System.Text.Encoding.ASCII.GetBytes(bcp47, buffer);

            fixed (byte* ptr = buffer)
            {
                return ut_hb_language_from_string(ptr, byteCount);
            }
        }

        /// <summary>Sets the language on a HarfBuzz buffer. Must be called before Shape.</summary>
        public static void SetLanguage(IntPtr buffer, IntPtr language)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_language(buffer, language);
        }

        public static void SetFlags(IntPtr buffer, uint flags)
        {
            if (buffer == IntPtr.Zero) return;
            ut_hb_buffer_set_flags(buffer, flags);
        }

        /// <summary>
        /// Adds codepoints to the buffer with context support for cross-run shaping.
        /// </summary>
        /// <param name="buffer">HarfBuzz buffer.</param>
        /// <param name="codepoints">Full text as context.</param>
        /// <param name="itemOffset">Start of the item to shape within codepoints.</param>
        /// <param name="itemLength">Length of the item to shape.</param>
        /// <remarks>
        /// Per HarfBuzz documentation: "When shaping part of a larger text, pass the whole
        /// paragraph and specify item_offset and item_length to enable cross-run Arabic shaping
        /// and proper handling of combining marks at run boundaries."
        /// </remarks>
        public static void AddCodepoints(IntPtr buffer, ReadOnlySpan<int> codepoints, int itemOffset, int itemLength)
        {
            if (buffer == IntPtr.Zero || codepoints.Length == 0) return;

            fixed (int* ptr = codepoints)
            {
                ut_hb_buffer_add_codepoints(buffer, (uint*)ptr, codepoints.Length, (uint)itemOffset, itemLength);
            }
        }

        public static void Shape(IntPtr font, IntPtr buffer)
        {
            if (font == IntPtr.Zero || buffer == IntPtr.Zero) return;
            ut_hb_shape(font, buffer, IntPtr.Zero, 0);
        }

        public static unsafe void Shape(IntPtr font, IntPtr buffer, hb_feature_t[] features)
        {
            if (font == IntPtr.Zero || buffer == IntPtr.Zero) return;
            if (features == null || features.Length == 0)
            {
                ut_hb_shape(font, buffer, IntPtr.Zero, 0);
                return;
            }
            fixed (hb_feature_t* ptr = features)
            {
                ut_hb_shape(font, buffer, (IntPtr)ptr, (uint)features.Length);
            }
        }

        public static unsafe void Shape(IntPtr font, IntPtr buffer, hb_feature_t[] features, int count)
        {
            if (font == IntPtr.Zero || buffer == IntPtr.Zero) return;
            if (features == null || count <= 0)
            {
                ut_hb_shape(font, buffer, IntPtr.Zero, 0);
                return;
            }
            fixed (hb_feature_t* ptr = features)
            {
                ut_hb_shape(font, buffer, (IntPtr)ptr, (uint)count);
            }
        }

        /// <summary>
        /// Batched shape call: clear + setup + shape + get results in one P/Invoke.
        /// Eliminates 6 managed→native transitions per run.
        /// Returns raw native pointers — valid until the next ShapeRun call on the same thread.
        /// </summary>
        public static unsafe int ShapeRun(
            IntPtr font, IntPtr buffer,
            ReadOnlySpan<int> codepoints, int itemOffset, int itemLength,
            int direction, uint scriptTag, uint flags,
            hb_feature_t[] features, int featureCount,
            out hb_glyph_info_t* outInfos, out hb_glyph_position_t* outPositions)
        {
            return ShapeRun(font, buffer, codepoints, itemOffset, itemLength,
                direction, scriptTag, IntPtr.Zero, flags, features, featureCount,
                out outInfos, out outPositions);
        }

        /// <summary>
        /// Batched shape call with explicit language tag. IntPtr.Zero language activates the
        /// old fast path without language, preserving behavior for callers that don't need locl.
        /// </summary>
        public static unsafe int ShapeRun(
            IntPtr font, IntPtr buffer,
            ReadOnlySpan<int> codepoints, int itemOffset, int itemLength,
            int direction, uint scriptTag, IntPtr language, uint flags,
            hb_feature_t[] features, int featureCount,
            out hb_glyph_info_t* outInfos, out hb_glyph_position_t* outPositions)
        {
            if (font == IntPtr.Zero || buffer == IntPtr.Zero)
            {
                outInfos = null;
                outPositions = null;
                return 0;
            }

            fixed (int* cpPtr = codepoints)
            {
                hb_glyph_info_t* infos;
                hb_glyph_position_t* positions;

                var fCount = featureCount > 0 ? (uint)featureCount : 0u;
                var hasFeatures = fCount > 0 && features != null;

                int glyphCount;

                if (language == IntPtr.Zero)
                {
                    if (hasFeatures)
                    {
                        fixed (hb_feature_t* fPtr = features)
                        {
                            glyphCount = ut_hb_shape_run(
                                font, buffer,
                                (uint*)cpPtr, codepoints.Length,
                                (uint)itemOffset, itemLength,
                                direction, scriptTag, flags,
                                fPtr, fCount,
                                &infos, &positions);
                        }
                    }
                    else
                    {
                        glyphCount = ut_hb_shape_run(
                            font, buffer,
                            (uint*)cpPtr, codepoints.Length,
                            (uint)itemOffset, itemLength,
                            direction, scriptTag, flags,
                            null, 0,
                            &infos, &positions);
                    }
                }
                else
                {
                    if (hasFeatures)
                    {
                        fixed (hb_feature_t* fPtr = features)
                        {
                            glyphCount = ut_hb_shape_run_lang(
                                font, buffer,
                                (uint*)cpPtr, codepoints.Length,
                                (uint)itemOffset, itemLength,
                                direction, scriptTag, language, flags,
                                fPtr, fCount,
                                &infos, &positions);
                        }
                    }
                    else
                    {
                        glyphCount = ut_hb_shape_run_lang(
                            font, buffer,
                            (uint*)cpPtr, codepoints.Length,
                            (uint)itemOffset, itemLength,
                            direction, scriptTag, language, flags,
                            null, 0,
                            &infos, &positions);
                    }
                }

                outInfos = infos;
                outPositions = positions;
                return glyphCount;
            }
        }

        public static int GetGlyphCount(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero) return 0;
            return (int)ut_hb_buffer_get_length(buffer);
        }

        public static ReadOnlySpan<GlyphInfo> GetGlyphInfos(IntPtr buffer)
        {
            if (buffer == IntPtr.Zero)
                return ReadOnlySpan<GlyphInfo>.Empty;

            hb_glyph_info_t* infos = ut_hb_buffer_get_glyph_infos(buffer, out uint infoCount);
            hb_glyph_position_t* positions = ut_hb_buffer_get_glyph_positions(buffer, out uint posCount);

            int count = (int)Math.Min(infoCount, posCount);
            if (count == 0)
                return ReadOnlySpan<GlyphInfo>.Empty;

            if (glyphInfoBuffer == null || glyphInfoBuffer.Length < count)
                glyphInfoBuffer = new GlyphInfo[Math.Max(count, 256)];

            for (int i = 0; i < count; i++)
            {
                glyphInfoBuffer[i] = new GlyphInfo
                {
                    glyphId = infos[i].codepoint,
                    cluster = infos[i].cluster,
                    xAdvance = positions[i].x_advance,
                    yAdvance = positions[i].y_advance,
                    xOffset = positions[i].x_offset,
                    yOffset = positions[i].y_offset
                };
            }
            return glyphInfoBuffer.AsSpan(0, count);
        }

        #endregion

        #region Variable Font API

        public static uint GetAxisCount(IntPtr hbFace)
        {
            if (hbFace == IntPtr.Zero) return 0;
            return ut_hb_ot_var_get_axis_count(hbFace);
        }

        public static int GetAxisInfos(IntPtr hbFace, hb_ot_var_axis_info_t[] buffer)
        {
            if (hbFace == IntPtr.Zero || buffer == null) return 0;

            uint count = (uint)buffer.Length;
            fixed (hb_ot_var_axis_info_t* ptr = buffer)
            {
                ut_hb_ot_var_get_axis_infos(hbFace, 0, ref count, ptr);
            }
            return (int)count;
        }

        public static void SetVariations(IntPtr hbFont, hb_variation_t* variations, int count)
        {
            if (hbFont == IntPtr.Zero || count <= 0) return;
            ut_hb_font_set_variations(hbFont, variations, (uint)count);
        }

        /// <summary>
        /// Marks the face immutable. After this, lazy caches inside HarfBuzz use atomic operations,
        /// making the face safe to share across threads. Required for thread-safe shaping per HarfBuzz spec.
        /// </summary>
        public static void MakeFaceImmutable(IntPtr hbFace)
        {
            if (hbFace == IntPtr.Zero) return;
            ut_hb_face_make_immutable(hbFace);
        }

        /// <summary>
        /// Marks the font immutable. After this, lazy caches inside HarfBuzz use atomic operations,
        /// making the font safe to share across threads for concurrent shape calls. Further setter calls
        /// on this font (e.g. <see cref="SetVariations"/>) become no-ops.
        /// </summary>
        public static void MakeFontImmutable(IntPtr hbFont)
        {
            if (hbFont == IntPtr.Zero) return;
            ut_hb_font_make_immutable(hbFont);
        }

        /// <summary>
        /// Creates a sub-font that inherits scale, ppem, and variation coords from the parent.
        /// OT font funcs are installed on the sub-font itself so variation-aware metric queries
        /// (advances, HVAR/GVAR) read the sub-font's own coords instead of delegating to the
        /// parent — without this the parent's default coords would shape advances even when
        /// variations are set on the sub-font.
        /// Caller should set variations then call <see cref="MakeFontImmutable"/> before using the sub-font
        /// for concurrent shaping. Destroy with <see cref="DestroyFontOnly"/>.
        /// </summary>
        public static IntPtr CreateSubFont(IntPtr parent)
        {
            if (parent == IntPtr.Zero) return IntPtr.Zero;
            var sub = ut_hb_font_create_sub_font(parent);
            if (sub == IntPtr.Zero) return IntPtr.Zero;
            ut_hb_ot_font_set_funcs(sub);
            return sub;
        }

        /// <summary>
        /// Destroys a standalone <c>hb_font_t</c> created by <see cref="CreateSubFont"/>.
        /// Does NOT touch face or blob — use <see cref="DestroyFont"/> for the full lifetime cleanup.
        /// </summary>
        public static void DestroyFontOnly(IntPtr font)
        {
            if (font == IntPtr.Zero) return;
            ut_hb_font_destroy(font);
        }

        #endregion
    }

}
