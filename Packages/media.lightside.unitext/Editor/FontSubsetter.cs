using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Font subsetting utility using HarfBuzz subset.
    /// Creates minimal font files containing only the specified characters.
    /// Editor-only.
    /// </summary>
    internal static class FontSubsetter
    {
        private const string LibraryName = "unitext_native_editor";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint subset_font(
            IntPtr fontData, uint fontDataSize, uint faceIndex,
            uint[] codepoints, uint codepointCount,
            IntPtr outData, uint outDataCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint subset_font_remove_codepoints(
            IntPtr fontData, uint fontDataSize, uint faceIndex,
            uint[] codepoints, uint codepointCount,
            IntPtr outData, uint outDataCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint get_glyph_count(
            IntPtr fontData, uint fontDataSize, uint faceIndex);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint subset_font_remove_glyphs(
            IntPtr fontData, uint fontDataSize, uint faceIndex,
            uint[] glyphIds, uint glyphCount,
            IntPtr outData, uint outDataCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint shape_text(
            IntPtr fontData, uint fontDataSize, uint faceIndex,
            uint[] codepoints, uint codepointCount,
            uint[] outGlyphIds, uint outCapacity);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint get_font_codepoints(
            IntPtr fontData, uint fontDataSize, uint faceIndex,
            uint[] outCodepoints, uint outCapacity);

        /// <summary>
        /// Creates a subset font containing only the specified Unicode codepoints.
        /// </summary>
        /// <param name="fontData">Original font file bytes (TTF/OTF)</param>
        /// <param name="codepoints">Unicode codepoints to include</param>
        /// <returns>Subset font bytes, or null on failure</returns>
        public static byte[] Subset(byte[] fontData, IReadOnlyList<int> codepoints, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0 || codepoints == null || codepoints.Count == 0)
                return null;

            var codepointsArray = new uint[codepoints.Count];
            for (int i = 0; i < codepoints.Count; i++)
                codepointsArray[i] = (uint)codepoints[i];

            return SubsetInternal(fontData, codepointsArray, faceIndex);
        }

        /// <summary>
        /// Creates a subset font containing only the characters from the specified text.
        /// </summary>
        /// <param name="fontData">Original font file bytes (TTF/OTF)</param>
        /// <param name="text">Text containing characters to include</param>
        /// <returns>Subset font bytes, or null on failure</returns>
        public static byte[] Subset(byte[] fontData, string text, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0 || string.IsNullOrEmpty(text))
                return null;

            var codepoints = new HashSet<uint>();
            for (int i = 0; i < text.Length; i++)
            {
                var codepoint = Utf16.DecodeAt(text, i, out var size);
                if (!Utf16.IsUnicodeScalar(codepoint))
                    throw new ArgumentException(
                        $"Text contains an unpaired UTF-16 surrogate at index {i}.", nameof(text));
                codepoints.Add((uint)codepoint);
                i += size - 1;
            }

            var codepointsArray = new uint[codepoints.Count];
            codepoints.CopyTo(codepointsArray);
            return SubsetInternal(fontData, codepointsArray, faceIndex);
        }

        /// <summary>
        /// Removes specific Unicode codepoints from a font.
        /// Uses the font's actual cmap to determine supported codepoints, then removes the specified ones.
        /// GSUB closure is applied, so contextual forms of removed codepoints are also removed.
        /// </summary>
        /// <param name="fontData">Original font file bytes</param>
        /// <param name="codepointsToRemove">Unicode codepoints to remove</param>
        /// <returns>Subset font bytes, or null on failure</returns>
        public static byte[] RemoveCodepoints(byte[] fontData, HashSet<int> codepointsToRemove, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0 ||
                codepointsToRemove == null || codepointsToRemove.Count == 0)
                return null;

            var cpArray = new uint[codepointsToRemove.Count];
            int idx = 0;
            foreach (var cp in codepointsToRemove)
                cpArray[idx++] = (uint)cp;

            var fontHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                var fontPtr = fontHandle.AddrOfPinnedObject();

                uint size = subset_font_remove_codepoints(fontPtr, (uint)fontData.Length, (uint)faceIndex,
                    cpArray, (uint)cpArray.Length, IntPtr.Zero, 0);
                if (size == 0)
                {
                    Debug.LogError("FontSubsetter: Failed to remove codepoints");
                    return null;
                }

                var result = new byte[size];
                var resultHandle = GCHandle.Alloc(result, GCHandleType.Pinned);
                try
                {
                    uint written = subset_font_remove_codepoints(fontPtr, (uint)fontData.Length, (uint)faceIndex,
                        cpArray, (uint)cpArray.Length, resultHandle.AddrOfPinnedObject(), size);
                    if (written != size)
                    {
                        Debug.LogError($"FontSubsetter: RemoveCodepoints size mismatch - expected {size}, got {written}");
                        return null;
                    }
                    return result;
                }
                finally
                {
                    resultHandle.Free();
                }
            }
            finally
            {
                fontHandle.Free();
            }
        }

        /// <summary>
        /// Returns the total glyph count in a font.
        /// </summary>
        public static int GetGlyphCount(byte[] fontData, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0)
                return 0;

            var handle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                return (int)get_glyph_count(handle.AddrOfPinnedObject(), (uint)fontData.Length, (uint)faceIndex);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// Removes specific glyphs from a font by glyph ID.
        /// Uses HB_SUBSET_FLAGS_NO_LAYOUT_CLOSURE to prevent GSUB from re-adding removed glyphs.
        /// </summary>
        /// <param name="fontData">Original font file bytes</param>
        /// <param name="glyphIdsToRemove">Glyph IDs to remove</param>
        /// <returns>Subset font bytes, or null on failure</returns>
        public static byte[] RemoveGlyphs(byte[] fontData, uint[] glyphIdsToRemove, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0 ||
                glyphIdsToRemove == null || glyphIdsToRemove.Length == 0)
                return null;

            var fontHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                var fontPtr = fontHandle.AddrOfPinnedObject();

                uint size = subset_font_remove_glyphs(fontPtr, (uint)fontData.Length, (uint)faceIndex,
                    glyphIdsToRemove, (uint)glyphIdsToRemove.Length,
                    IntPtr.Zero, 0);
                if (size == 0)
                {
                    Debug.LogError("FontSubsetter: Failed to remove glyphs");
                    return null;
                }

                var result = new byte[size];
                var resultHandle = GCHandle.Alloc(result, GCHandleType.Pinned);
                try
                {
                    uint written = subset_font_remove_glyphs(fontPtr, (uint)fontData.Length, (uint)faceIndex,
                        glyphIdsToRemove, (uint)glyphIdsToRemove.Length,
                        resultHandle.AddrOfPinnedObject(), size);
                    if (written != size)
                    {
                        Debug.LogError($"FontSubsetter: RemoveGlyphs size mismatch - expected {size}, got {written}");
                        return null;
                    }
                    return result;
                }
                finally
                {
                    resultHandle.Free();
                }
            }
            finally
            {
                fontHandle.Free();
            }
        }

        /// <summary>
        /// Shapes a sequence of codepoints and returns the resulting glyph IDs.
        /// </summary>
        /// <param name="fontData">Font file bytes</param>
        /// <param name="codepoints">Codepoints to shape (e.g. a grapheme cluster)</param>
        /// <returns>Array of glyph IDs, or null on failure</returns>
        public static uint[] ShapeText(byte[] fontData, int[] codepoints, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0 ||
                codepoints == null || codepoints.Length == 0)
                return null;

            var uCodepoints = new uint[codepoints.Length];
            for (int i = 0; i < codepoints.Length; i++)
                uCodepoints[i] = (uint)codepoints[i];

            var fontHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                var fontPtr = fontHandle.AddrOfPinnedObject();

                uint count = shape_text(fontPtr, (uint)fontData.Length, (uint)faceIndex,
                    uCodepoints, (uint)uCodepoints.Length,
                    null, 0);
                if (count == 0)
                    return null;

                var outGlyphs = new uint[count];
                uint written = shape_text(fontPtr, (uint)fontData.Length, (uint)faceIndex,
                    uCodepoints, (uint)uCodepoints.Length,
                    outGlyphs, count);
                if (written != count)
                    return null;

                return outGlyphs;
            }
            finally
            {
                fontHandle.Free();
            }
        }

        /// <summary>
        /// Returns all Unicode codepoints supported by the font (from cmap table).
        /// </summary>
        public static uint[] GetCodepoints(byte[] fontData, int faceIndex = 0)
        {
            if (fontData == null || fontData.Length == 0)
                return null;

            var handle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                var ptr = handle.AddrOfPinnedObject();

                uint count = get_font_codepoints(ptr, (uint)fontData.Length, (uint)faceIndex, null, 0);
                if (count == 0)
                    return null;

                var result = new uint[count];
                uint written = get_font_codepoints(ptr, (uint)fontData.Length, (uint)faceIndex, result, count);
                if (written != count)
                    return null;

                return result;
            }
            finally
            {
                handle.Free();
            }
        }

        private static byte[] SubsetInternal(byte[] fontData, uint[] codepoints, int faceIndex)
        {
            var fontDataHandle = GCHandle.Alloc(fontData, GCHandleType.Pinned);
            try
            {
                var fontDataPtr = fontDataHandle.AddrOfPinnedObject();

                uint size = subset_font(fontDataPtr, (uint)fontData.Length, (uint)faceIndex,
                                        codepoints, (uint)codepoints.Length,
                                        IntPtr.Zero, 0);
                if (size == 0)
                {
                    Debug.LogError("FontSubsetter: Failed to create subset font");
                    return null;
                }

                var result = new byte[size];
                var resultHandle = GCHandle.Alloc(result, GCHandleType.Pinned);
                try
                {
                    uint written = subset_font(fontDataPtr, (uint)fontData.Length, (uint)faceIndex,
                                               codepoints, (uint)codepoints.Length,
                                               resultHandle.AddrOfPinnedObject(), size);
                    if (written != size)
                    {
                        Debug.LogError($"FontSubsetter: Size mismatch - expected {size}, got {written}");
                        return null;
                    }
                    return result;
                }
                finally
                {
                    resultHandle.Free();
                }
            }
            finally
            {
                fontDataHandle.Free();
            }
        }
    }
}
