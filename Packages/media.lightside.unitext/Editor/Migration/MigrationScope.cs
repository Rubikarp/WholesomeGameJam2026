using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Which of the project's assets the migration may read and rewrite. An asset outside the scope
    /// is left whole for its owner: it is never scanned, migrated, or reference-repaired, and a
    /// reference it holds to a replaced component keeps naming the id that component no longer has.
    /// </summary>
    internal static class MigrationScope
    {
        /// <summary>
        /// Packages that ship the migration itself: they name every TMP GUID and type on purpose,
        /// so scanning them would report the tool as work to do.
        /// </summary>
        static readonly string[] ownPackagePrefixes = { "Packages/media.lightside." };

        /// <summary>Every in-scope text-serialized asset, paired with the file it occupies.</summary>
        public static List<ProjectYamlFiles.TargetFile> Collect(List<string> excluded) =>
            CollectWhere(ProjectYamlFiles.HasYamlExtension, excluded);

        /// <summary>Every in-scope asset <paramref name="include"/> accepts.</summary>
        public static List<ProjectYamlFiles.TargetFile> CollectWhere(
            Func<string, bool> include, List<string> excluded)
        {
            if (include == null) throw new ArgumentNullException(nameof(include));
            return ProjectYamlFiles.CollectWhere(
                path => include(path) && !Excludes(path, excluded));
        }

        /// <summary>
        /// Whether the migration leaves <paramref name="assetPath"/> alone: an entry covers the
        /// asset it names and everything under it as a folder.
        /// </summary>
        public static bool Excludes(string assetPath, List<string> excluded)
        {
            for (var i = 0; i < ownPackagePrefixes.Length; i++)
            {
                if (assetPath.StartsWith(ownPackagePrefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            if (excluded == null) return false;
            for (var i = 0; i < excluded.Count; i++)
            {
                if (Covers(excluded[i], assetPath)) return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="assetPath"/> is <paramref name="root"/> itself or sits under it
        /// as a folder. A name <paramref name="root"/> merely prefixes — <c>Assets/UIKit</c> under
        /// <c>Assets/UI</c> — is a different asset and does not match.
        /// </summary>
        public static bool Covers(string root, string assetPath)
        {
            if (string.IsNullOrEmpty(root) || assetPath.Length < root.Length ||
                string.Compare(assetPath, 0, root, 0, root.Length,
                    StringComparison.OrdinalIgnoreCase) != 0) return false;
            return assetPath.Length == root.Length ||
                   root[root.Length - 1] == '/' || assetPath[root.Length] == '/';
        }
    }
}
