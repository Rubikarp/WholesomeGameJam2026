using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace LightSide
{
    internal static class FontSourceId
    {
        /// <summary>Container address of a font's payload entry inside its AssetBundle, shared by build injection and runtime fetch.</summary>
        internal static string PayloadAddress(int token)
            => "unitext/fonts/payload/" + unchecked((uint)token).ToString("x8");

        internal static string ToHex(byte[] bytes)
        {
            const string digits = "0123456789abcdef";
            var characters = new char[bytes.Length * 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = digits[bytes[i] >> 4];
                characters[i * 2 + 1] = digits[bytes[i] & 15];
            }
            return new string(characters);
        }
    }

#if !UNITY_EDITOR && !UNITY_WEBGL
    /// <summary>
    /// Token-keyed registry of embedded font sources: the Player's packaged StreamingAssets catalog plus
    /// every <see cref="UniTextFontPayload"/> delivered by loaded content. Content payload bytes are
    /// fetched on demand through their bundle's <c>unitext/fonts/payload/&lt;token&gt;</c> container
    /// entry, so loading a bundle never deserializes font bytes; scene-packed payloads register
    /// themselves while their scene loads. First initialization is main-thread; <see cref="Resolve"/>
    /// is thread-safe afterwards.
    /// </summary>
    internal static class EmbeddedFontCatalog
    {
        private const int CatalogMagic = 0x31465455;
        private const int CatalogVersion = 1;
        private const int SourceIdSize = 32;
        private const string PackagedRoot = "UniText/Fonts";
        private const int StreamBufferSize = 64 * 1024;

        private static readonly object gate = new();
        private static readonly Dictionary<int, CachedFontSource> entries = new();
        private static readonly Dictionary<string, CachedFontSource> sources =
            new(StringComparer.Ordinal);
        private static bool initialized;
        private static bool packagedCatalogFound;
        private static string cacheRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeScenes() => EnsureInitialized();

        /// <summary>
        /// Resolves a font's source from its serialized payload identity. The packaged catalog wins for
        /// tokens it ships; otherwise the source is cache-backed under the supplied identity, and when
        /// its cache files do not exist yet a main-thread prefetch of the payload's container entry is
        /// scheduled. A token that is neither packaged nor accompanied by an identity cannot exist.
        /// </summary>
        internal static FontSource Resolve(int token, byte[] sourceHash, int rawLength)
        {
            EnsureInitialized();
            lock (gate)
            {
                if (entries.TryGetValue(token, out var existing))
                {
                    if (sourceHash is { Length: SourceIdSize }
                        && !existing.Matches(FontSourceId.ToHex(sourceHash), rawLength))
                        throw new InvalidDataException(
                            $"UniText font lookup token {token} identifies different payloads.");
                    return existing;
                }

                if (sourceHash is not { Length: SourceIdSize } || rawLength <= 0)
                    throw new InvalidOperationException(
                        $"Embedded font source {token} carries no payload identity and the packaged"
                        + $" catalog {(packagedCatalogFound ? "does not list it" : $"is absent from '{PackagedRoot}'")}."
                        + " The asset predates its payload migration or was built without one.");

                var sourceId = FontSourceId.ToHex(sourceHash);
                if (sources.TryGetValue(sourceId, out var source))
                {
                    if (source.Length != rawLength)
                        throw new InvalidDataException(
                            $"UniText font source '{sourceId}' has conflicting raw lengths.");
                }
                else
                {
                    source = new CachedFontSource(sourceId, sourceHash, rawLength,
                        SourceLocation.FromDirectory(cacheRoot), cacheRoot, token);
                    sources.Add(sourceId, source);
                }

                entries.Add(token, source);
                if (!source.HasBacking) MainThread.Post(source.Prefetch);
                return source;
            }
        }

        /// <summary>
        /// Loads the payload container entry for <paramref name="token"/> from whichever loaded bundle
        /// provides it, then unloads the loaded object: a <see cref="UniTextFontPayload"/> registers its
        /// bytes only while deserializing, and only an unloaded entry deserializes again — so a retry
        /// after a failed or evicted cache write receives a fresh payload, never the spent instance.
        /// Main thread only.
        /// </summary>
        internal static void FetchPayload(int token)
        {
            var address = FontSourceId.PayloadAddress(token);
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (!bundle.Contains(address)) continue;
                var payload = bundle.LoadAsset<UniTextFontPayload>(address);
                if (payload == null) continue;
                Resources.UnloadAsset(payload);
                return;
            }
            throw new InvalidOperationException(
                $"No loaded content provides '{address}' for embedded font source {token}. The content"
                + " delivering this font is unloaded, or it was built without the UniText payload entry.");
        }

        /// <summary>
        /// Adopts one content-delivered payload. Safe to call again for the same token with identical
        /// bytes; a token that names a different payload than already registered is corrupt content.
        /// A source without cache backing is persisted compressed here, so the caller may release the
        /// array afterwards; decompression stays deferred to the font's first use.
        /// </summary>
        internal static void RegisterPayload(int token, byte[] sourceHash, int rawLength, byte[] data)
        {
            if (token == 0)
                throw new InvalidDataException("A UniText font payload has an empty lookup token.");
            if (sourceHash is not { Length: SourceIdSize })
                throw new InvalidDataException(
                    $"UniText font payload {token} has an invalid source identity.");
            if (rawLength <= 0)
                throw new InvalidDataException(
                    $"UniText font payload {token} has an invalid raw length.");
            if (data is not { Length: > 0 })
                throw new InvalidDataException($"UniText font payload {token} is empty.");

            EnsureInitialized();
            lock (gate)
            {
                var sourceId = FontSourceId.ToHex(sourceHash);
                if (entries.TryGetValue(token, out var existing))
                {
                    if (!existing.Matches(sourceId, rawLength))
                        throw new InvalidDataException(
                            $"UniText font lookup token {token} identifies different payloads.");
                    existing.AdoptCompressed(data);
                    return;
                }

                if (sources.TryGetValue(sourceId, out var source))
                {
                    if (source.Length != rawLength)
                        throw new InvalidDataException(
                            $"UniText font source '{sourceId}' has conflicting raw lengths.");
                }
                else
                {
                    source = new CachedFontSource(sourceId, sourceHash, rawLength,
                        SourceLocation.FromDirectory(cacheRoot), cacheRoot, token);
                    sources.Add(sourceId, source);
                }

                source.AdoptCompressed(data);
                entries.Add(token, source);
            }
        }

        private static void EnsureInitialized()
        {
            if (Volatile.Read(ref initialized)) return;
            if (!MainThread.IsCurrent)
                throw new InvalidOperationException(
                    "The embedded font catalog was accessed before its main-thread initialization.");

            lock (gate)
            {
                if (initialized) return;
                cacheRoot = Path.Combine(Application.temporaryCachePath,
                    "UniText", "Fonts", $"v{CatalogVersion}");

#if UNITY_ANDROID
                Zstd.InitializeAndroidAssetManager();
                try
                {
                    using var stream = Zstd.OpenAndroidAsset(PackagedRoot + "/catalog.bin");
                    RegisterLocked(stream, SourceLocation.PackagedAndroid(PackagedRoot));
                    packagedCatalogFound = true;
                }
                catch (FileNotFoundException) { }
#else
                var packagedDirectory = Path.Combine(Application.streamingAssetsPath,
                    "UniText", "Fonts");
                var catalogPath = Path.Combine(packagedDirectory, "catalog.bin");
                if (File.Exists(catalogPath))
                {
                    using var stream = new FileStream(catalogPath, FileMode.Open,
                        FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
                    RegisterLocked(stream, SourceLocation.FromDirectory(packagedDirectory));
                    packagedCatalogFound = true;
                }
#endif
                Volatile.Write(ref initialized, true);
            }
        }

        private static void RegisterLocked(Stream stream, SourceLocation location)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            if (reader.ReadInt32() != CatalogMagic)
                throw new InvalidDataException("UniText font catalog has an invalid signature.");
            if (reader.ReadInt32() != CatalogVersion)
                throw new InvalidDataException("UniText font catalog has an unsupported version.");

            int count = reader.ReadInt32();
            if (count < 0)
                throw new InvalidDataException("UniText font catalog has a negative entry count.");
            long expectedLength = 12L + count * (8L + SourceIdSize);
            if (stream.Length != expectedLength)
                throw new InvalidDataException("UniText font catalog length does not match its entry count.");

            var pending = new CatalogEntry[count];
            for (var i = 0; i < count; i++)
            {
                int token = reader.ReadInt32();
                int rawLength = reader.ReadInt32();
                byte[] sourceId = reader.ReadBytes(SourceIdSize);
                if (token == 0)
                    throw new InvalidDataException("UniText font catalog contains an empty lookup token.");
                if (rawLength <= 0)
                    throw new InvalidDataException("UniText font catalog contains an invalid raw length.");
                if (sourceId.Length != SourceIdSize)
                    throw new EndOfStreamException("UniText font catalog ended inside a source identity.");
                pending[i] = new CatalogEntry(token, rawLength, sourceId);
            }
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("UniText font catalog contains trailing data.");

            var catalogSources = new Dictionary<string, CachedFontSource>(StringComparer.Ordinal);
            var catalogEntries = new Dictionary<int, CachedFontSource>();
            for (var i = 0; i < pending.Length; i++)
            {
                var entry = pending[i];
                string sourceId = FontSourceId.ToHex(entry.sourceId);
                if (!sources.TryGetValue(sourceId, out var source)
                    && !catalogSources.TryGetValue(sourceId, out source))
                {
                    source = new CachedFontSource(sourceId, entry.sourceId,
                        entry.rawLength, location, cacheRoot, 0);
                    catalogSources.Add(sourceId, source);
                }
                else if (source.Length != entry.rawLength)
                {
                    throw new InvalidDataException(
                        $"UniText font source '{sourceId}' has conflicting raw lengths.");
                }

                if (entries.TryGetValue(entry.token, out var existing)
                    || catalogEntries.TryGetValue(entry.token, out existing))
                {
                    if (!ReferenceEquals(existing, source))
                        throw new InvalidDataException(
                            $"UniText font lookup token {entry.token} identifies different payloads.");
                }
                else catalogEntries.Add(entry.token, source);
            }

            foreach (var source in catalogSources) sources.Add(source.Key, source.Value);
            foreach (var entry in catalogEntries) entries.Add(entry.Key, entry.Value);
        }

        private static void PublishTemporaryFile(string temporaryPath, string finalPath,
            long expectedLength, string sourceId)
        {
            if (FileHasLength(finalPath, expectedLength)) return;
            if (File.Exists(finalPath))
            {
                try { File.Replace(temporaryPath, finalPath, null); }
                catch (IOException) when (FileHasLength(finalPath, expectedLength)) { return; }
            }
            else
            {
                try { File.Move(temporaryPath, finalPath); }
                catch (IOException) when (FileHasLength(finalPath, expectedLength)) { return; }
            }
            if (!FileHasLength(finalPath, expectedLength))
                throw new InvalidDataException(
                    $"UniText font cache entry '{sourceId}' was not published completely.");
        }

        private static bool FileHasLength(string path, long expectedLength)
            => File.Exists(path) && new FileInfo(path).Length == expectedLength;

        private readonly struct CatalogEntry
        {
            internal readonly int token;
            internal readonly int rawLength;
            internal readonly byte[] sourceId;

            internal CatalogEntry(int token, int rawLength, byte[] sourceId)
            {
                this.token = token;
                this.rawLength = rawLength;
                this.sourceId = sourceId;
            }
        }

        private readonly struct SourceLocation
        {
            private readonly string root;
            private readonly bool androidAsset;

            private SourceLocation(string root, bool androidAsset)
            {
                this.root = root;
                this.androidAsset = androidAsset;
            }

            internal static SourceLocation FromDirectory(string root)
                => new(Path.GetFullPath(root), false);

            internal static SourceLocation PackagedAndroid(string root)
                => new(root, true);

            internal Stream Open(string sourceId)
            {
                string fileName = sourceId + ".ufontz";
#if UNITY_ANDROID
                if (androidAsset) return Zstd.OpenAndroidAsset(root + "/" + fileName);
#endif
                return new FileStream(Path.Combine(root, fileName), FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            }
        }

        /// <summary>
        /// One deduplicated compressed source, extracted once into an immutable cache file and opened as
        /// a mapping. The compressed form stays reachable — packaged in the player, persisted in the
        /// cache, or fetched on the main thread from its content's payload entry — so a missing
        /// extracted file re-extracts instead of failing.
        /// </summary>
        private sealed class CachedFontSource : FontSource
        {
            private readonly object sourceGate = new();
            private readonly string sourceId;
            private readonly byte[] compressedSha256;
            private readonly int rawLength;
            private readonly SourceLocation location;
            private readonly string finalPath;
            private readonly string compressedPath;
            private readonly int fetchToken;
            private WeakReference<FileFontSource> extracted;

            internal CachedFontSource(string sourceId, byte[] compressedSha256,
                int rawLength, SourceLocation location, string cacheRoot, int fetchToken)
            {
                this.sourceId = sourceId;
                this.compressedSha256 = compressedSha256;
                this.rawLength = rawLength;
                this.location = location;
                this.fetchToken = fetchToken;
                finalPath = Path.Combine(cacheRoot, sourceId + ".sfnt");
                compressedPath = Path.Combine(cacheRoot, sourceId + ".ufontz");
            }

            internal override string Identity => "embedded:" + sourceId;
            internal override int Length => rawLength;
            internal override long OwnedByteCount => 0;

            internal bool Matches(string otherSourceId, int otherRawLength)
                => sourceId == otherSourceId && rawLength == otherRawLength;

            internal bool HasBacking
                => fetchToken == 0
                   || FileHasLength(finalPath, rawLength)
                   || File.Exists(compressedPath);

            /// <summary>Persists content-delivered compressed bytes into the cache once; adopted or
            /// already-backed sources ignore further copies.</summary>
            internal void AdoptCompressed(byte[] data)
            {
                if (fetchToken == 0 || HasBacking) return;
                Directory.CreateDirectory(Path.GetDirectoryName(compressedPath));
                var temporaryPath = Path.Combine(Path.GetDirectoryName(compressedPath),
                    $"{sourceId}.{Guid.NewGuid():N}.tmp");
                try
                {
                    using (var output = new FileStream(temporaryPath, FileMode.CreateNew,
                               FileAccess.Write, FileShare.None, StreamBufferSize,
                               FileOptions.SequentialScan))
                    {
                        output.Write(data, 0, data.Length);
                        output.Flush(true);
                    }
                    PublishTemporaryFile(temporaryPath, compressedPath, data.Length, sourceId);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }

            /// <summary>Best-effort early fetch: a failure here stays silent because the same fetch
            /// reruns — and fails precisely — at the first use of the source.</summary>
            internal void Prefetch()
            {
                if (HasBacking) return;
                try
                {
                    FetchPayload(fetchToken);
                }
                catch
                {
                }
            }

            internal override FontBackingLease Open()
            {
                var reference = Volatile.Read(ref extracted);
                if (reference == null || !reference.TryGetTarget(out var source))
                {
                    lock (sourceGate)
                    {
                        reference = extracted;
                        if (reference == null || !reference.TryGetTarget(out source))
                        {
                            EnsureExtracted();
                            source = FileFontSource.OpenFile(finalPath);
                            Volatile.Write(ref extracted,
                                new WeakReference<FileFontSource>(source));
                        }
                    }
                }
                return source.Open();
            }

            private void EnsureExtracted()
            {
                if (FileHasLength(finalPath, rawLength)) return;
                if (fetchToken != 0 && !File.Exists(compressedPath))
                {
                    if (!MainThread.IsCurrent)
                        throw new InvalidOperationException(
                            $"Embedded font source '{sourceId}' is not cached yet and its payload can"
                            + " only be fetched on the main thread. The first use of a freshly"
                            + " delivered font must reach the main thread once.");
                    FetchPayload(fetchToken);
                    if (!File.Exists(compressedPath))
                        throw new InvalidOperationException(
                            $"Fetching the payload of embedded font source '{sourceId}' produced no cache entry.");
                }
                using var input = location.Open(sourceId);
                Extract(input);
            }

            private void Extract(Stream input)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                string temporaryPath = Path.Combine(Path.GetDirectoryName(finalPath),
                    $"{sourceId}.{Guid.NewGuid():N}.tmp");
                try
                {
                    Zstd.DecompressStreamToFile(input, temporaryPath, rawLength, compressedSha256);
                    PublishTemporaryFile(temporaryPath, finalPath, rawLength, sourceId);
                }
                finally
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
            }
        }
    }

#else
    internal static class EmbeddedFontCatalog
    {
        internal static void RegisterPayload(int token, byte[] sourceHash, int rawLength, byte[] data)
            => throw new PlatformNotSupportedException();
    }
#endif
}
