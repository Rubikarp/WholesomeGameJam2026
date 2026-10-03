using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;

namespace LightSide
{
    /// <summary>How an asset answered a migration's read.</summary>
    public enum YamlReadResult : byte
    {
        /// <summary>The file's text is available.</summary>
        Text,

        /// <summary>
        /// The file opens on no YAML directive. A binary payload carries none of the documents a migration
        /// reads, so nothing about it is left unknown.
        /// </summary>
        Binary,

        /// <summary>
        /// The file is text-serialized and could not be read — past the size limit, not valid UTF-8, or
        /// refused by the process. Whatever it holds is neither found nor ruled out.
        /// </summary>
        Unreadable,
    }

    /// <summary>
    /// Enumerates the project's text-serialized scenes, prefabs, assets, and materials and resolves their
    /// on-disk paths. Immutable package caches are skipped (they cannot be rewritten); embedded/local packages resolve through
    /// <see cref="UnityEditor.PackageManager.PackageInfo.resolvedPath"/> because their virtual <c>Packages/</c>
    /// path has no filesystem counterpart.
    /// </summary>
    public static class ProjectYamlFiles
    {
        const long MaxFileBytes = 256L * 1024 * 1024;

        static readonly UTF8Encoding utf8 = new(false, true);

        public readonly struct TargetFile
        {
            public readonly string assetPath;
            public readonly string fsPath;

            public TargetFile(string assetPath, string fsPath)
            {
                this.assetPath = assetPath;
                this.fsPath = fsPath;
            }
        }

        public static List<TargetFile> Collect() => CollectWhere(HasYamlExtension);

        /// <summary>
        /// Every asset <paramref name="include"/> accepts, paired with the file it occupies.
        /// Enumeration runs through the asset database rather than the disk, so a path Unity
        /// ignores on import — anything behind a <c>~</c> suffix or a leading dot — is never
        /// produced, and a local package resolves to the folder it really lives in instead of to
        /// its virtual <c>Packages/</c> path, which has no filesystem counterpart.
        /// </summary>
        public static List<TargetFile> CollectWhere(Func<string, bool> include)
        {
            if (include == null) throw new ArgumentNullException(nameof(include));

            var result = new List<TargetFile>();
            var packageRoots = new Dictionary<string, string>();

            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!include(path)) continue;

                var fsPath = ToFsPath(path, packageRoots);
                if (fsPath != null && File.Exists(fsPath))
                    result.Add(new TargetFile(path, fsPath));
            }

            return result;
        }

        public static string ResolvePackagePath(string assetPath, Dictionary<string, string> cache)
        {
            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                return null;

            var slash = assetPath.IndexOf('/', "Packages/".Length);
            var pkgRoot = slash < 0 ? assetPath : assetPath.Substring(0, slash);
            if (cache == null || !cache.TryGetValue(pkgRoot, out var resolvedRoot))
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                resolvedRoot = info != null &&
                               (info.source == UnityEditor.PackageManager.PackageSource.Embedded ||
                                info.source == UnityEditor.PackageManager.PackageSource.Local)
                    ? info.resolvedPath
                    : null;
                if (cache != null) cache[pkgRoot] = resolvedRoot;
            }

            return resolvedRoot == null ? null : resolvedRoot + assetPath.Substring(pkgRoot.Length);
        }

        public static string ToFsPath(string assetPath, Dictionary<string, string> packageRoots = null) =>
            assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                ? Path.GetFullPath(assetPath)
                : ResolvePackagePath(assetPath, packageRoots);

        /// <summary>
        /// Reports whether an asset path carries a text-serialized asset extension. Names only: a folder
        /// carries one too (a package named <c>com.vendor.unity</c>), so a source must also be an existing file.
        /// </summary>
        public static bool HasYamlExtension(string assetPath)
        {
            var extension = Path.GetExtension(assetPath);
            return extension.Equals(".unity", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".asset", StringComparison.OrdinalIgnoreCase) ||
                   extension.Equals(".mat", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads a text-format YAML asset. <paramref name="reason"/> states what stopped an
        /// <see cref="YamlReadResult.Unreadable"/> read and is null otherwise. Whether an unreadable file
        /// is a skip or a failure belongs to the caller: only that answer leaves the file's content unknown.
        /// </summary>
        public static YamlReadResult ReadYaml(string fsPath, out string content, out string reason)
        {
            content = null;
            reason = null;
            try
            {
                using var stream = new FileStream(fsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!OpensYamlDirective(stream)) return YamlReadResult.Binary;
                if (stream.Length > MaxFileBytes)
                {
                    reason = $"larger than the {MaxFileBytes >> 20} MB migration limit";
                    return YamlReadResult.Unreadable;
                }

                stream.Position = 0;
                content = ReadText(stream);
                return YamlReadResult.Text;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                reason = e.Message;
                return YamlReadResult.Unreadable;
            }
        }

        /// <summary>
        /// Text of an asset file, with a leading UTF-8 BOM kept as its <c>U+FEFF</c> character so that writing
        /// the string back reproduces the file's bytes; undecodable bytes throw instead of becoming replacement
        /// characters. <see cref="File.ReadAllText(string)"/> discards the mark, so text it returns cannot be
        /// compared against text read here.
        /// </summary>
        public static string ReadText(string fsPath)
        {
            using var stream = new FileStream(fsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ReadText(stream);
        }

        static string ReadText(Stream stream)
        {
            using var reader = new StreamReader(stream, utf8, detectEncodingFromByteOrderMarks: false);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Whether the stream opens on a YAML directive, which a UTF-8 BOM may precede. Unity imports a
        /// BOM-prefixed asset as text-serialized YAML, so the mark cannot stand for a binary payload.
        /// </summary>
        static bool OpensYamlDirective(Stream stream)
        {
            Span<byte> header = stackalloc byte[8];
            int read = 0, step;
            while (read < header.Length && (step = stream.Read(header.Slice(read))) > 0) read += step;

            var at = read >= 3 && header[0] == 0xEF && header[1] == 0xBB && header[2] == 0xBF ? 3 : 0;
            return read >= at + 5 &&
                   header[at] == (byte)'%' && header[at + 1] == (byte)'Y' && header[at + 2] == (byte)'A' &&
                   header[at + 3] == (byte)'M' && header[at + 4] == (byte)'L';
        }
    }
}
