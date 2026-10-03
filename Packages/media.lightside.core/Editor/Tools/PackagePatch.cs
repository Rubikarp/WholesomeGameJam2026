using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace LightSide
{
    internal enum PackagePatchStatus
    {
        Ready,
        Rejected,
        Applied,
        Failed,
    }

    /// <summary>
    /// A LightSide hotfix archive staged against this project: a zip whose entry paths start at the
    /// package root, optionally carrying a manifest in the zip archive comment
    /// (<c>{"package":…,"min":…,"max":…}</c>) naming the target package and the release window the
    /// whole-file replacement is safe for. Without a manifest the target is inferred from the file
    /// name or the archive's contents, and the version window is not verified.
    /// </summary>
    internal sealed class PackagePatch
    {
        private const string FamilyPrefix = "media.lightside.";

        public string ZipPath { get; }
        public PackagePatchStatus Status { get; private set; }
        public string Detail { get; private set; }
        public PackageInfo Target { get; private set; }
        public string WindowText { get; private set; }

        /// <summary>Whether the package resolves from the immutable package cache, where Package
        /// Manager restores the original files on the next re-resolve.</summary>
        public bool CacheResident { get; private set; }

        private readonly List<string> files = new();

        private PackagePatch(string zipPath) => ZipPath = zipPath;

        /// <summary>Stages one archive; a broken or untargetable archive comes back
        /// <see cref="PackagePatchStatus.Rejected"/> with the reason in <see cref="Detail"/>.</summary>
        public static PackagePatch Load(string zipPath)
        {
            var patch = new PackagePatch(zipPath);
            try
            {
                patch.Resolve();
            }
            catch (Exception e)
            {
                patch.Status = PackagePatchStatus.Rejected;
                patch.Detail = e.Message;
            }
            return patch;
        }

        private void Resolve()
        {
            using (var zip = ZipFile.OpenRead(ZipPath))
                foreach (var entry in zip.Entries)
                {
                    var path = SafeEntryPath(entry);
                    if (path != null) files.Add(path);
                }
            if (files.Count == 0) throw new InvalidDataException("The archive contains no files.");

            var manifest = ReadArchiveComment(ZipPath);
            var packageName = JsonValue(manifest, "package");
            var family = FamilyPackages();
            if (packageName != null)
            {
                Target = family.Find(p => p.name == packageName);
                if (Target == null)
                    throw new InvalidOperationException(
                        $"This patch is for '{packageName}', which is not installed in this project.");
            }
            else
            {
                Target = MatchByFileName(family) ?? MatchByContent(family) ??
                         throw new InvalidOperationException(
                             "Cannot tell which installed LightSide package this archive is for.");
            }

            CheckWindow(JsonValue(manifest, "min"), JsonValue(manifest, "max"));
            CacheResident = Target.source switch
            {
                PackageSource.Embedded or PackageSource.Local => false,
                PackageSource.Registry or PackageSource.Git or PackageSource.LocalTarball => true,
                _ => throw new InvalidOperationException(
                    $"Package '{Target.name}' comes from an unsupported source ({Target.source})."),
            };
            Status = PackagePatchStatus.Ready;
            Detail = $"{files.Count} file(s) → {DisplayRoot(Target.resolvedPath)}";
        }

        private void CheckWindow(string min, string max)
        {
            if (min == null && max == null) return;
            if (min != null && CompareVersions(Target.version, min) < 0 ||
                max != null && CompareVersions(Target.version, max) > 0)
                throw new InvalidOperationException(
                    $"Built for {WindowLabel(min, max)}; installed is {Target.version}. " +
                    "Ask for a patch built for your version.");
            WindowText = WindowLabel(min, max);
        }

        private static string WindowLabel(string min, string max) => min != null && max != null
            ? min == max ? min : $"{min} – {max}"
            : min != null ? $"{min} or newer" : $"{max} or older";

        /// <summary>Orders two semantic versions, including the rule that a pre-release precedes
        /// its release.</summary>
        private static int CompareVersions(string a, string b)
        {
            SplitVersion(a, out var coreA, out var preA);
            SplitVersion(b, out var coreB, out var preB);
            var partsA = coreA.Split('.');
            var partsB = coreB.Split('.');
            for (var i = 0; i < Math.Max(partsA.Length, partsB.Length); i++)
            {
                var numberA = i < partsA.Length ? int.Parse(partsA[i]) : 0;
                var numberB = i < partsB.Length ? int.Parse(partsB[i]) : 0;
                if (numberA != numberB) return numberA.CompareTo(numberB);
            }
            if (preA == null != (preB == null)) return preA == null ? 1 : -1;
            if (preA == null) return 0;
            var idsA = preA.Split('.');
            var idsB = preB.Split('.');
            for (var i = 0; i < Math.Min(idsA.Length, idsB.Length); i++)
            {
                var bothNumeric = int.TryParse(idsA[i], out var numberA) &
                                  int.TryParse(idsB[i], out var numberB);
                var order = bothNumeric
                    ? numberA.CompareTo(numberB)
                    : string.CompareOrdinal(idsA[i], idsB[i]);
                if (order != 0) return order;
            }
            return idsA.Length.CompareTo(idsB.Length);
        }

        private static void SplitVersion(string version, out string core, out string prerelease)
        {
            var build = version.IndexOf('+');
            if (build >= 0) version = version.Substring(0, build);
            var dash = version.IndexOf('-');
            core = dash < 0 ? version : version.Substring(0, dash);
            prerelease = dash < 0 ? null : version.Substring(dash + 1);
        }

        /// <summary>
        /// Writes the archive's files over the target package root; a failure restores every
        /// touched file before rethrowing. The caller refreshes the asset database after the batch.
        /// </summary>
        public int Apply()
        {
            var root = Target.resolvedPath;
            var backup = Path.Combine(ProjectRoot, FileUtil.GetUniqueTempPathInProject());
            var replaced = new List<string>();
            var added = new List<string>();
            try
            {
                using (var zip = ZipFile.OpenRead(ZipPath))
                    foreach (var entry in zip.Entries)
                    {
                        var file = SafeEntryPath(entry);
                        if (file == null) continue;
                        var destination = Path.Combine(root, file);
                        if (File.Exists(destination))
                        {
                            var saved = Path.Combine(backup, file);
                            Directory.CreateDirectory(Path.GetDirectoryName(saved));
                            File.Copy(destination, saved);
                            File.SetAttributes(destination,
                                File.GetAttributes(destination) & ~FileAttributes.ReadOnly);
                            replaced.Add(file);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destination));
                            added.Add(file);
                        }
                        entry.ExtractToFile(destination, true);
                    }
                Status = PackagePatchStatus.Applied;
                Detail = added.Count > 0
                    ? $"Applied {replaced.Count + added.Count} file(s) ({added.Count} new) to {DisplayRoot(root)}"
                    : $"Applied {replaced.Count} file(s) to {DisplayRoot(root)}";
                return replaced.Count + added.Count;
            }
            catch (Exception e)
            {
                Rollback(root, backup, replaced, added);
                Status = PackagePatchStatus.Failed;
                Detail = e.Message;
                throw;
            }
            finally
            {
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
            }
        }

        private static string ProjectRoot => Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));

        private static void Rollback(string root, string backup,
            List<string> replaced, List<string> added)
        {
            foreach (var file in added)
            {
                var destination = Path.Combine(root, file);
                if (File.Exists(destination)) File.Delete(destination);
                for (var directory = Path.GetDirectoryName(destination);
                     directory != null && directory.Length > root.Length &&
                     Directory.Exists(directory) &&
                     Directory.GetFileSystemEntries(directory).Length == 0;
                     directory = Path.GetDirectoryName(directory))
                    Directory.Delete(directory);
            }
            foreach (var file in replaced)
                File.Copy(Path.Combine(backup, file), Path.Combine(root, file), true);
        }

        /// <summary>Gets the package-relative path of a file entry, null for a directory entry;
        /// refuses a path that could escape the package root.</summary>
        private static string SafeEntryPath(ZipArchiveEntry entry)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.EndsWith("/", StringComparison.Ordinal)) return null;
            if (path.Length == 0 || path[0] == '/' || path.Contains(':') || path == ".." ||
                path.StartsWith("../", StringComparison.Ordinal) ||
                path.EndsWith("/..", StringComparison.Ordinal) || path.Contains("/../"))
                throw new InvalidDataException($"Entry escapes the package root: {entry.FullName}");
            return path;
        }

        private static List<PackageInfo> FamilyPackages()
        {
            var result = new List<PackageInfo>();
            foreach (var package in PackageInfo.GetAllRegisteredPackages())
                if (package.name.StartsWith(FamilyPrefix, StringComparison.Ordinal))
                    result.Add(package);
            return result;
        }

        private PackageInfo MatchByFileName(List<PackageInfo> family)
        {
            var fileName = Path.GetFileNameWithoutExtension(ZipPath);
            PackageInfo best = null;
            foreach (var package in family)
                if (fileName.StartsWith(package.name, StringComparison.OrdinalIgnoreCase) &&
                    (best == null || package.name.Length > best.name.Length))
                    best = package;
            return best;
        }

        /// <summary>The unique installed family package already holding the most of the archive's
        /// paths, or null when no package holds any or the best score is shared.</summary>
        private PackageInfo MatchByContent(List<PackageInfo> family)
        {
            PackageInfo best = null;
            var bestHits = 0;
            var tie = false;
            foreach (var package in family)
            {
                var hits = 0;
                foreach (var file in files)
                    if (File.Exists(Path.Combine(package.resolvedPath, file)))
                        hits++;
                if (hits > bestHits)
                {
                    best = package;
                    bestHits = hits;
                    tie = false;
                }
                else if (hits == bestHits && hits > 0) tie = true;
            }
            return tie ? null : best;
        }

        private static string DisplayRoot(string path)
        {
            var full = Path.GetFullPath(path);
            var project = ProjectRoot;
            return full.StartsWith(project, StringComparison.OrdinalIgnoreCase)
                ? full.Substring(project.Length).TrimStart('\\', '/').Replace('\\', '/')
                : full;
        }

        /// <summary>UTF-8 archive comment from the zip end-of-central-directory record, or null
        /// when the archive carries none.</summary>
        private static string ReadArchiveComment(string path)
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, 22 + ushort.MaxValue);
            if (length < 22) return null;
            var tail = new byte[length];
            stream.Seek(-length, SeekOrigin.End);
            for (var read = 0; read < length;)
            {
                var step = stream.Read(tail, read, length - read);
                if (step <= 0) return null;
                read += step;
            }
            for (var i = length - 22; i >= 0; i--)
            {
                if (tail[i] != 0x50 || tail[i + 1] != 0x4B ||
                    tail[i + 2] != 0x05 || tail[i + 3] != 0x06)
                    continue;
                var size = Math.Min(tail[i + 20] | (tail[i + 21] << 8), length - i - 22);
                return size > 0 ? Encoding.UTF8.GetString(tail, i + 22, size) : null;
            }
            return null;
        }

        private static string JsonValue(string json, string key)
        {
            if (json == null) return null;
            var match = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"([^\"]+)\"");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
