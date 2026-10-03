#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace LightSide
{
    /// <summary>Language-aware parser for the platform fallback graph used below Android API 29.</summary>
    internal static class AndroidFontsXmlResolver
    {
        private struct FontEntry
        {
            public string path;
            public int faceIndex;
            public int weight;
            public bool italic;
            public string fallbackFor;
            public UniTextFont.AxisDefault[] axes;
        }

        private struct Family
        {
            public string name;
            public string lang;
            public List<FontEntry> fonts;
        }

        private struct Alias
        {
            public string to;
            public int weight;
        }

        private readonly struct FontEntryKey : IEquatable<FontEntryKey>
        {
            private readonly string path;
            private readonly int faceIndex;

            internal FontEntryKey(string path, int faceIndex)
            {
                this.path = path;
                this.faceIndex = faceIndex;
            }

            public bool Equals(FontEntryKey other)
                => faceIndex == other.faceIndex
                   && string.Equals(path, other.path, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is FontEntryKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(StringComparer.Ordinal.GetHashCode(path), faceIndex);
        }

        private sealed class CoverageSession : IDisposable
        {
            private readonly Dictionary<FontEntryKey, FreeTypeFace> faces = new();

            internal IntPtr GetFace(string path, int faceIndex)
            {
                var key = new FontEntryKey(path, faceIndex);
                if (faces.TryGetValue(key, out var face)) return face?.Pointer ?? IntPtr.Zero;

                FontSource fontSource;
                try { fontSource = SystemFontByteCache.Read(path); }
                catch
                {
                    faces[key] = null;
                    return IntPtr.Zero;
                }
                if (fontSource == null || fontSource.Length == 0)
                {
                    faces[key] = null;
                    return IntPtr.Zero;
                }
                if (!FT.IsInitialized) FT.Initialize();
                face = FreeTypeFace.TryCreate(fontSource, faceIndex);
                faces[key] = face;
                return face?.Pointer ?? IntPtr.Zero;
            }

            public void Dispose()
            {
                foreach (var face in faces.Values)
                    face?.Dispose();
                faces.Clear();
            }
        }

        private const string FontsXmlPath = "/system/etc/fonts.xml";
        private const string FontsDir = "/system/fonts/";

        private static volatile List<Family> families;
        private static Dictionary<string, Alias> aliases;
        private static readonly object loadLock = new();

        internal static bool TryResolve(string text, string language, string family,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
        {
            match = default;
            if (string.IsNullOrEmpty(text) || !EnsureLoaded()) return false;

            using var coverage = new CoverageSession();
            return TryResolveRange(text, 0, text.Length, language, family,
                requestWeight, requestItalic, coverage, out match);
        }

        private static bool TryResolveRange(string text, int textStart, int textLength,
            string language, string family, int requestWeight, bool requestItalic,
            CoverageSession coverage, out SystemFontSourceMatch match)
        {
            match = default;

            var weight = requestWeight > 0 ? requestWeight : 400;
            var requestedFamily = ResolveAlias(family, ref weight);

            if (!string.IsNullOrEmpty(requestedFamily))
            {
                for (var i = 0; i < families.Count; i++)
                {
                    if (!string.Equals(families[i].name, requestedFamily, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (TryFamily(families[i], text, textStart, textLength,
                            family, requestedFamily, weight, requestItalic, coverage, out match))
                        return true;
                }
            }

            if (!string.IsNullOrEmpty(language))
            {
                for (var i = 0; i < families.Count; i++)
                {
                    if (!FamilyLanguageMatches(families[i].lang, language)) continue;
                    if (TryFamily(families[i], text, textStart, textLength,
                            family, requestedFamily, weight, requestItalic, coverage, out match))
                        return true;
                }
            }

            for (var i = 0; i < families.Count; i++)
            {
                if (!string.IsNullOrEmpty(requestedFamily)
                    && string.Equals(families[i].name, requestedFamily, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(language) && FamilyLanguageMatches(families[i].lang, language))
                    continue;
                if (TryFamily(families[i], text, textStart, textLength,
                        family, requestedFamily, weight, requestItalic, coverage, out match))
                    return true;
            }

            return false;
        }

        internal static bool TryResolveBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
        {
            if (string.IsNullOrEmpty(text) || count <= 0 || !EnsureLoaded()) return false;
            using var coverage = new CoverageSession();
            for (var i = 0; i < count; i++)
            {
                var length = offsets[i + 1] - offsets[i];
                TryResolveRange(text, offsets[i], length, language, family,
                    requestWeight, requestItalic, coverage, out matches[i]);
            }
            return true;
        }

        private static bool TryFamily(Family family, string text, int textStart, int textLength,
            string requestedName, string resolvedName, int weight, bool italic,
            CoverageSession coverage,
            out SystemFontSourceMatch match)
        {
            match = default;
            var bestScore = int.MaxValue;
            FontEntry best = default;

            for (var i = 0; i < family.fonts.Count; i++)
            {
                var entry = family.fonts[i];
                if (!FallbackTargetMatches(entry.fallbackFor, requestedName, resolvedName)) continue;
                var score = FontStyleEncoding.CssWeightMatchScore(entry.weight, weight)
                            + (entry.italic != italic ? 100_000 : 0);
                if (score >= bestScore
                    || !ProbeCoverage(coverage.GetFace(entry.path, entry.faceIndex),
                        text, textStart, textLength)) continue;
                bestScore = score;
                best = entry;
            }

            if (best.path == null) return false;
            match = new SystemFontSourceMatch
            {
                descriptor = best.path,
                faceIndex = best.faceIndex,
                axes = best.axes,
                coveredUtf16Length = textLength,
                scale = 1f,
            };
            return true;
        }

        private static bool ProbeCoverage(IntPtr face, string text, int textStart, int textLength)
        {
            if (face == IntPtr.Zero) return false;
            var textEnd = textStart + textLength;
            for (var offset = textStart; offset < textEnd;)
            {
                var codepoint = char.ConvertToUtf32(text, offset);
                offset += char.IsSurrogatePair(text, offset) ? 2 : 1;
                if (UnicodeData.IsDefaultIgnorable(codepoint)) continue;
                if (FT.GetCharIndex(face, (uint)codepoint) == 0) return false;
            }
            return true;
        }

        private static bool FallbackTargetMatches(string fallbackFor, string requested, string resolved)
        {
            if (string.IsNullOrEmpty(fallbackFor)) return true;
            return string.Equals(fallbackFor, requested, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(fallbackFor, resolved, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveAlias(string family, ref int weight)
        {
            var name = family;
            for (var hops = 0; hops < 8 && !string.IsNullOrEmpty(name)
                 && aliases.TryGetValue(name, out var alias); hops++)
            {
                name = alias.to;
                if (alias.weight > 0) weight = alias.weight;
            }
            return name;
        }

        private static bool FamilyLanguageMatches(string familyLanguages, string requested)
        {
            if (string.IsNullOrEmpty(familyLanguages)) return false;
            var start = 0;
            while (start < familyLanguages.Length)
            {
                var separator = familyLanguages.IndexOf(',', start);
                var end = separator >= 0 ? separator : familyLanguages.Length;
                while (start < end && char.IsWhiteSpace(familyLanguages[start])) start++;
                while (end > start && char.IsWhiteSpace(familyLanguages[end - 1])) end--;
                var candidate = familyLanguages.AsSpan(start, end - start);
                var requestedSpan = requested.AsSpan();
                if (LanguagePrefixMatches(candidate, requestedSpan)
                    || LanguagePrefixMatches(requestedSpan, candidate)) return true;
                start = separator >= 0 ? separator + 1 : familyLanguages.Length;
            }
            return false;
        }

        private static bool LanguagePrefixMatches(ReadOnlySpan<char> longer, ReadOnlySpan<char> shorter)
        {
            if (shorter.IsEmpty || longer.Length < shorter.Length
                || !longer.Slice(0, shorter.Length).Equals(shorter,
                    StringComparison.OrdinalIgnoreCase)) return false;
            return longer.Length == shorter.Length || longer[shorter.Length] == '-';
        }

        private static bool EnsureLoaded()
        {
            if (families != null) return families.Count > 0;
            lock (loadLock)
            {
                if (families != null) return families.Count > 0;
                var parsedFamilies = new List<Family>();
                var parsedAliases = new Dictionary<string, Alias>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    Parse(FontsXmlPath, parsedFamilies, parsedAliases);
                }
                catch (Exception e)
                {
                    CatZones.fontBackend.MeowWarn($"[AndroidFontsXml] parse failed '{FontsXmlPath}': {e.Message}");
                    parsedFamilies.Clear();
                }
                aliases = parsedAliases;
                families = parsedFamilies;
                return families.Count > 0;
            }
        }

        internal static void Shutdown()
        {
            lock (loadLock)
            {
                families = null;
                aliases = null;
            }
        }

        private static void Parse(string path, List<Family> outFamilies, Dictionary<string, Alias> outAliases)
        {
            using var stream = File.OpenRead(path);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                DtdProcessing = DtdProcessing.Ignore
            });

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Name == "family")
                {
                    ParseFamily(reader, outFamilies);
                }
                else if (reader.Name == "alias")
                {
                    var name = reader.GetAttribute("name");
                    var to = reader.GetAttribute("to");
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(to))
                        outAliases[name] = new Alias
                        {
                            to = to,
                            weight = ParseInt(reader.GetAttribute("weight"), 0)
                        };
                }
            }
        }

        private static void ParseFamily(XmlReader reader, List<Family> outFamilies)
        {
            var family = new Family
            {
                name = reader.GetAttribute("name"),
                lang = reader.GetAttribute("lang"),
                fonts = new List<FontEntry>()
            };
            if (reader.IsEmptyElement)
            {
                outFamilies.Add(family);
                return;
            }

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Name == "family") break;
                if (reader.NodeType != XmlNodeType.Element || reader.Name != "font") continue;

                var entry = new FontEntry
                {
                    weight = ParseInt(reader.GetAttribute("weight"), 400),
                    italic = reader.GetAttribute("style") == "italic",
                    faceIndex = ParseInt(reader.GetAttribute("index"), 0),
                    fallbackFor = reader.GetAttribute("fallbackFor")
                };

                ReadFont(reader, out var file, out entry.axes);
                if (string.IsNullOrEmpty(file)) continue;
                entry.path = file[0] == '/' ? file : FontsDir + file;
                family.fonts.Add(entry);
            }

            outFamilies.Add(family);
        }

        private static void ReadFont(XmlReader reader, out string file,
            out UniTextFont.AxisDefault[] axes)
        {
            file = null;
            axes = null;
            if (reader.IsEmptyElement) return;

            List<UniTextFont.AxisDefault> parsedAxes = null;
            using (var sub = reader.ReadSubtree())
            {
                while (sub.Read())
                {
                    if (sub.NodeType == XmlNodeType.Text)
                    {
                        file = (file ?? string.Empty) + sub.Value;
                    }
                    else if (sub.NodeType == XmlNodeType.Element && sub.Name == "axis")
                    {
                        var tag = PackTag(sub.GetAttribute("tag"));
                        if (tag == 0 || !float.TryParse(sub.GetAttribute("stylevalue"),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var value))
                            continue;
                        parsedAxes ??= new List<UniTextFont.AxisDefault>();
                        parsedAxes.Add(new UniTextFont.AxisDefault { tag = tag, value = value });
                    }
                }
            }

            file = file?.Trim();
            axes = parsedAxes?.ToArray();
        }

        private static int PackTag(string tag)
        {
            if (tag == null || tag.Length != 4) return 0;
            return tag[0] << 24 | tag[1] << 16 | tag[2] << 8 | tag[3];
        }

        private static int ParseInt(string value, int fallback)
            => int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
#endif
