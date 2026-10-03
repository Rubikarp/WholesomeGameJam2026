using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LightSide
{
    internal sealed class EmbeddedFontAssetIndex
    {
        private readonly HashSet<GUID> knownFontAssets = new();
        private readonly Dictionary<GUID, bool> mainAssets = new();

        internal EmbeddedFontAssetIndex()
        {
            var values = AssetDatabase.FindAssets(
                "t:UniTextFont t:UniTextColorFont t:UniTextFontVariant t:UniTextSystemFont");
            for (var i = 0; i < values.Length; i++)
                knownFontAssets.Add(new GUID(values[i]));
        }

        /// <summary>Every font asset in the project, regardless of what any build packs.</summary>
        internal IReadOnlyCollection<GUID> All => knownFontAssets;

        internal bool Contains(GUID guid)
        {
            if (knownFontAssets.Contains(guid)) return true;
            if (mainAssets.TryGetValue(guid, out bool result)) return result;
            var type = AssetDatabase.GetMainAssetTypeFromGUID(guid);
            result = type != null && typeof(UniTextFont).IsAssignableFrom(type);
            mainAssets.Add(guid, result);
            return result;
        }
    }

    internal readonly struct EmbeddedFontBuildPayload
    {
        internal int Token { get; }
        internal string SourceId { get; }
        internal byte[] SourceHash { get; }
        internal int RawLength { get; }
        internal byte[] Compressed { get; }
        internal string FontName { get; }

        internal EmbeddedFontBuildPayload(int token, string sourceId,
            byte[] sourceHash, int rawLength, byte[] compressed, string fontName)
        {
            Token = token;
            SourceId = sourceId;
            SourceHash = sourceHash;
            RawLength = rawLength;
            Compressed = compressed;
            FontName = fontName;
        }
    }

    internal sealed class EmbeddedFontBuildPayloadSet
    {
        private readonly Dictionary<int, EmbeddedFontBuildPayload> entries = new();
        private readonly Dictionary<string, EmbeddedFontBuildPayload> sources =
            new(StringComparer.Ordinal);
        private readonly Dictionary<int, EmbeddedFontBuildPayload> fonts = new();

        internal IReadOnlyDictionary<int, EmbeddedFontBuildPayload> Entries => entries;
        internal IReadOnlyDictionary<string, EmbeddedFontBuildPayload> Sources => sources;

        internal bool TryAdd(UniTextFont requestedFont,
            out EmbeddedFontBuildPayload payload)
            => TryAddOwned(requestedFont?.UsesEmbeddedSource == true
                ? requestedFont
                : ResolveEmbeddedSource(requestedFont), out payload);

        internal bool TryAddOwned(UniTextFont font,
            out EmbeddedFontBuildPayload payload)
        {
            payload = default;
            if (font == null) return false;
            if (!font.UsesEmbeddedSource)
            {
                if (font is UniTextFontVariant) ResolveEmbeddedSource(font);
                return false;
            }

            int instanceId = ObjectUtils.GetInstanceIdCompat(font);
            if (fonts.TryGetValue(instanceId, out payload)) return true;

            if (!font.TryGetEmbeddedBuildSource(out int token, out byte[] stored))
                throw new BuildFailedException(
                    $"Embedded font '{font.name}' has no payload.");
            if (token == 0)
                throw new BuildFailedException(
                    $"Embedded font '{font.name}' has no runtime lookup token.");
            if (stored == null || stored.Length == 0)
                throw new BuildFailedException(
                    $"Embedded font '{font.name}' has no payload.");

            byte[] compressed = Zstd.IsCompressed(stored) ? stored : Zstd.Compress(stored);
            long rawLength64 = Zstd.GetFrameContentSize(compressed);
            if (rawLength64 <= 0 || rawLength64 > int.MaxValue)
                throw new BuildFailedException(
                    $"Embedded font '{font.name}' has an invalid Zstd frame.");

            byte[] sourceHash;
            using (var sha = SHA256.Create()) sourceHash = sha.ComputeHash(compressed);
            string sourceId = FontSourceId.ToHex(sourceHash);
            payload = new EmbeddedFontBuildPayload(token, sourceId, sourceHash,
                (int)rawLength64, compressed, font.name);

            if (entries.TryGetValue(token, out var existingEntry)
                && (!string.Equals(existingEntry.SourceId, sourceId,
                        StringComparison.Ordinal)
                    || existingEntry.RawLength != payload.RawLength))
                throw new BuildFailedException(
                    $"Embedded fonts '{existingEntry.FontName}' and '{font.name}' share lookup token {token} but have different payloads.");

            if (sources.TryGetValue(sourceId, out var existingSource))
            {
                if (!existingSource.Compressed.AsSpan().SequenceEqual(compressed))
                    throw new BuildFailedException(
                        "Two embedded font payloads produced the same SHA-256 identity.");
            }
            else sources.Add(sourceId, payload);

            if (!entries.ContainsKey(token)) entries.Add(token, payload);
            fonts.Add(instanceId, payload);
            return true;
        }

        private static UniTextFont ResolveEmbeddedSource(UniTextFont font)
        {
            if (font == null) return null;
            var chain = new HashSet<int>();
            while (font is UniTextFontVariant variant)
            {
                if (!chain.Add(ObjectUtils.GetInstanceIdCompat(font)))
                    throw new BuildFailedException(
                        $"Font variant '{font.name}' has a cyclic source chain.");
                font = variant.Source;
                if (font == null)
                    throw new BuildFailedException(
                        $"Font variant '{variant.name}' has no source font.");
            }
            return font;
        }
    }
}
