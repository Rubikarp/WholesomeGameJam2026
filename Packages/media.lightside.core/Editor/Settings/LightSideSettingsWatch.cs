using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Watches every registered LightSide settings asset: re-runs its package's ensure pass after assets
    /// change, and refuses a move that would take it out of a <c>Resources</c> folder.
    /// </summary>
    /// <remarks>
    /// One watch serves the whole family — a package registers its settings type and its ensure callback
    /// rather than shipping a postprocessor of its own. Register from an
    /// <see cref="InitializeOnLoadMethodAttribute"/>; registering the same type again replaces the callback.
    /// </remarks>
    public static class LightSideSettingsWatch
    {
        private static readonly Dictionary<Type, Action<bool>> watched = new();

        /// <summary>
        /// Registers <paramref name="settingsType"/> for watching. <paramref name="onAssetsProcessed"/> is
        /// invoked after an asset import batch, with <see langword="true"/> when something was deleted or
        /// the domain reloaded — the cases that can leave the asset missing.
        /// </summary>
        public static void Register(Type settingsType, Action<bool> onAssetsProcessed)
        {
            if (settingsType == null || onAssetsProcessed == null) return;
            watched[settingsType] = onAssetsProcessed;
        }

        private static bool IsWatched(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            foreach (var type in watched.Keys)
                if (type.IsInstanceOfType(asset))
                    return true;
            return false;
        }

        private sealed class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
            {
                if (watched.Count == 0) return;
                var mayBeMissing = didDomainReload || deletedAssets.Length != 0;
                foreach (var callback in watched.Values)
                    callback(mayBeMissing);
            }
        }

        private sealed class MoveGuard : AssetModificationProcessor
        {
            private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
            {
                if (destinationPath == sourcePath) return AssetMoveResult.DidNotMove;

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sourcePath);
                if (!IsWatched(asset)) return AssetMoveResult.DidNotMove;

                var destDir = Path.GetDirectoryName(destinationPath)?.Replace('\\', '/') ?? "";
                if (destDir.EndsWith("/Resources", StringComparison.Ordinal) || destDir == "Resources")
                    return AssetMoveResult.DidNotMove;

                Debug.LogWarning(
                    $"[LightSide] {asset.GetType().Name} must stay in a Resources/ folder to load at runtime.");
                return AssetMoveResult.FailedMove;
            }
        }
    }
}
