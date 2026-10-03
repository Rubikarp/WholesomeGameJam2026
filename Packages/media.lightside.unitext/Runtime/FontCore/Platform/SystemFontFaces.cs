using System;

namespace LightSide
{
    internal static unsafe class SystemFontFaces
    {
        internal static bool TryReadSource(string path, in SystemFontSourceMatch match, string text,
            out SystemFontMaterializedSource source)
        {
            var fontSource = SystemFontByteCache.Read(path);
            return TryMaterialize(fontSource, in match, text, out source);
        }

        internal static bool TryMaterialize(FontSource fontSource, in SystemFontSourceMatch match, string text,
            out SystemFontMaterializedSource source)
        {
            source = default;
            if (fontSource == null || fontSource.Length == 0) return false;

            FaceInfo info;
            if (match.faceIndex < 0)
            {
                if (!TrySelectCoveringFace(fontSource, text, out info, -1,
                        match.requestedWeight, match.requestedItalic, match.postScriptName))
                    return false;
            }
            else if (!TryReadFace(fontSource, match.faceIndex, text, out info))
            {
                return false;
            }

            source = new SystemFontMaterializedSource { fontSource = fontSource, faceInfo = info };
            return true;
        }

        /// <summary>
        /// Reports whether a matched face's shaping tables can render <paramref name="text"/> against
        /// outlines of <paramref name="expectedUnitsPerEm"/>, naming the mismatch in
        /// <paramref name="reason"/> when they cannot. A platform match the shaper cannot use is an
        /// ordinary outcome of a fallback query, not a broken invariant: the caller drops the
        /// candidate and the text keeps resolving.
        /// </summary>
        internal static bool TryValidateShapingSource(FontSource fontSource, int faceIndex,
            string text, int expectedUnitsPerEm, out string reason)
        {
            using var font = TryOpen(fontSource, faceIndex);
            if (font == null)
            {
                reason = "it exposes no shaping tables";
                return false;
            }
            if (font.upem <= 0)
            {
                reason = "its shaping tables carry no units-per-em";
                return false;
            }
            if (expectedUnitsPerEm <= 0 || font.upem != expectedUnitsPerEm)
            {
                reason = $"its shaping and outline units-per-em differ ({font.upem} vs {expectedUnitsPerEm})";
                return false;
            }
            if (!string.IsNullOrEmpty(text) && !Covers(font, text, out var missing))
            {
                reason = $"its shaping tables do not cover U+{missing:X4}";
                return false;
            }
            reason = null;
            return true;
        }

        internal static bool TryFindExactPostScriptFace(FontSource fontSource, string postScriptName,
            out int faceIndex)
        {
            faceIndex = -1;
            if (fontSource == null || fontSource.Length == 0
                || string.IsNullOrEmpty(postScriptName)) return false;

            using var sourceLease = fontSource.Open();
            var sourceData = new ReadOnlySpan<byte>((void*)sourceLease.Pointer, sourceLease.Length);
            var faceCount = GetDeclaredFaceCount(sourceData);
            if (faceCount <= 0) return false;
            for (var candidate = 0; candidate < faceCount; candidate++)
            {
                if (!PostScriptNameMatches(sourceData, candidate, postScriptName)) continue;
                if (faceIndex >= 0)
                {
                    faceIndex = -1;
                    return false;
                }
                faceIndex = candidate;
            }
            return faceIndex >= 0;
        }

        /// <summary>
        /// Reports whether HarfBuzz maps every non-ignorable codepoint in a materialized system font.
        /// </summary>
        internal static bool Covers(UniTextFont.Core font, string text)
        {
            if (font == null) return false;
            if (string.IsNullOrEmpty(text)) return true;
            for (var offset = 0; offset < text.Length;)
            {
                var codepoint = char.ConvertToUtf32(text, offset);
                offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
                if (UnicodeData.IsDefaultIgnorable(codepoint)) continue;
                if (Shaper.GetGlyphIndex(font, (uint)codepoint) == 0) return false;
            }
            return true;
        }

        internal static bool TrySelectCoveringFace(FontSource fontSource, string text, out FaceInfo info,
            int preferredIndex = -1, int requestWeight = -1, bool requestItalic = false,
            string postScriptName = null)
        {
            info = default;
            if (fontSource == null || fontSource.Length == 0 || string.IsNullOrEmpty(text)) return false;
            if (!FT.IsInitialized) FT.Initialize();
            using var sourceLease = fontSource.Open();
            var sourceData = new ReadOnlySpan<byte>((void*)sourceLease.Pointer, sourceLease.Length);

            var faceCount = GetFaceCount(fontSource, preferredIndex);
            if (faceCount <= 0) return false;

            var found = false;
            var bestScore = int.MaxValue;
            for (var faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                var probeIndex = faceIndex == 0 && preferredIndex >= 0 && preferredIndex < faceCount
                    ? preferredIndex
                    : faceIndex == preferredIndex ? 0 : faceIndex;
                if (!CoversFace(fontSource, probeIndex, text)) continue;
                using var face = FreeTypeFace.TryCreate(fontSource, probeIndex);
                if (face == null) continue;
                var candidate = UniTextFont.Core.BuildFullFaceInfo(face.Pointer);
                var weight = candidate.weightClass > 0 ? candidate.weightClass : 400;
                var score = requestWeight <= 0 && probeIndex == preferredIndex
                    ? -1
                    : FontStyleEncoding.CssWeightMatchScore(weight,
                          requestWeight > 0 ? requestWeight : 400)
                      + (candidate.isItalic != requestItalic ? 100_000 : 0);
                if (!string.IsNullOrEmpty(postScriptName)
                    && !PostScriptNameMatches(sourceData, probeIndex, postScriptName))
                    score += 1_000_000;
                if (score < bestScore)
                {
                    bestScore = score;
                    info = candidate;
                    found = true;
                }
                if (bestScore <= 0) break;
            }

            return found;
        }

        private static bool TryReadFace(FontSource fontSource, int faceIndex, string text, out FaceInfo info)
        {
            info = default;
            if (fontSource == null || fontSource.Length == 0 || faceIndex < 0) return false;
            if (!string.IsNullOrEmpty(text) && !CoversFace(fontSource, faceIndex, text)) return false;
            if (!FT.IsInitialized) FT.Initialize();
            using var face = FreeTypeFace.TryCreate(fontSource, faceIndex);
            if (face == null) return false;
            info = UniTextFont.Core.BuildFullFaceInfo(face.Pointer);
            return true;
        }

        private static bool Covers(Shaper.FontCacheEntry font, string text, out int missing)
        {
            for (var offset = 0; offset < text.Length;)
            {
                var codepoint = char.ConvertToUtf32(text, offset);
                offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
                if (UnicodeData.IsDefaultIgnorable(codepoint)) continue;
                if (!font.TryGetGlyph((uint)codepoint, out _))
                {
                    missing = codepoint;
                    return false;
                }
            }
            missing = 0;
            return true;
        }

        private static bool CoversFace(FontSource fontSource, int faceIndex, string text)
        {
            using var font = TryOpen(fontSource, faceIndex);
            return font != null && Covers(font, text, out _);
        }

        internal static int GetUpem(FontSource fontSource, int faceIndex)
        {
            using var font = TryOpen(fontSource, faceIndex);
            return font?.upem ?? 0;
        }

        private static Shaper.FontCacheEntry TryOpen(FontSource fontSource, int faceIndex)
        {
            if (fontSource == null || fontSource.Length == 0 || faceIndex < 0) return null;
            var backing = fontSource.Open();
            try
            {
                var font = new Shaper.FontCacheEntry(backing, faceIndex);
                backing = null;
                return font;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            finally
            {
                backing?.Dispose();
            }
        }

        private static bool PostScriptNameMatches(ReadOnlySpan<byte> data, int faceIndex, string expected)
        {
            if (!TryGetFaceOffset(data, faceIndex, out var faceOffset)
                || faceOffset > data.Length - 12)
                return false;

            var tableCount = ReadUInt16(data, faceOffset + 4);
            var records = faceOffset + 12;
            if (tableCount > (data.Length - records) / 16) return false;
            for (var i = 0; i < tableCount; i++)
            {
                var record = records + i * 16;
                if (record > data.Length - 16) return false;
                if (ReadUInt32(data, record) != 0x6E616D65) continue;

                var table = ReadUInt32(data, record + 8);
                var length = ReadUInt32(data, record + 12);
                if (table > int.MaxValue || length > int.MaxValue) return false;
                return NameTableContains(data, (int)table, (int)length, expected);
            }
            return false;
        }

        private static bool NameTableContains(ReadOnlySpan<byte> data, int table, int tableLength, string expected)
        {
            if (table < 0 || tableLength < 6 || table > data.Length - tableLength) return false;
            var count = ReadUInt16(data, table + 2);
            if (count > (tableLength - 6) / 12) return false;
            var stringStorage = ReadUInt16(data, table + 4);
            if (stringStorage > tableLength) return false;
            for (var i = 0; i < count; i++)
            {
                var record = table + 6 + i * 12;
                if (ReadUInt16(data, record + 6) != 6) continue;

                var platform = ReadUInt16(data, record);
                var length = ReadUInt16(data, record + 8);
                var relativeOffset = stringStorage + ReadUInt16(data, record + 10);
                if (relativeOffset > tableLength - length) continue;
                var offset = table + relativeOffset;
                if (platform == 0 || platform == 3)
                {
                    if (length != expected.Length * 2) continue;
                    var equal = true;
                    for (var c = 0; c < expected.Length; c++)
                        if (ReadUInt16(data, offset + c * 2) != expected[c])
                        {
                            equal = false;
                            break;
                        }
                    if (equal) return true;
                }
                else
                {
                    if (length != expected.Length) continue;
                    var equal = true;
                    for (var c = 0; c < expected.Length; c++)
                        if (data[offset + c] != expected[c])
                        {
                            equal = false;
                            break;
                        }
                    if (equal) return true;
                }
            }
            return false;
        }

        private static bool TryGetFaceOffset(ReadOnlySpan<byte> data, int faceIndex, out int offset)
        {
            offset = 0;
            if (data.Length < 12 || faceIndex < 0) return false;
            if (ReadUInt32(data, 0) != 0x74746366) return faceIndex == 0;

            var count = ReadUInt32(data, 8);
            if ((uint)faceIndex >= count || faceIndex > (data.Length - 16) / 4) return false;
            var value = ReadUInt32(data, 12 + faceIndex * 4);
            if (value > int.MaxValue || value > data.Length - 12) return false;
            offset = (int)value;
            return true;
        }

        private static int GetDeclaredFaceCount(ReadOnlySpan<byte> data)
        {
            if (data.Length < 12) return 0;
            if (ReadUInt32(data, 0) != 0x74746366) return 1;
            var count = ReadUInt32(data, 8);
            if (count == 0 || count > int.MaxValue
                           || count > (uint)((data.Length - 12) / 4)) return 0;
            return (int)count;
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset)
            => (ushort)(data[offset] << 8 | data[offset + 1]);

        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset)
            => (uint)(data[offset] << 24 | data[offset + 1] << 16
                      | data[offset + 2] << 8 | data[offset + 3]);

        private static int GetFaceCount(FontSource fontSource, int preferredIndex)
        {
            var index = preferredIndex >= 0 ? preferredIndex : 0;
            var face = FreeTypeFace.TryCreate(fontSource, index);
            if (face == null && index != 0)
                face = FreeTypeFace.TryCreate(fontSource, 0);
            if (face == null) return 0;
            using (face) return Math.Max(1, FT.GetFaceInfo(face.Pointer).numFaces);
        }
    }
}
