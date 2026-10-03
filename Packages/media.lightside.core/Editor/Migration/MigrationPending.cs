using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Editor-local queue of assets whose migration writes were deferred while the editor held unsaved
    /// changes for them; every pass retries its entries until they migrate or leave the index. Lives in
    /// <c>Library/</c> beside the migration index: an unreadable file forces a full pass, which restores
    /// the queue from real state.
    /// </summary>
    [Serializable]
    internal sealed class MigrationPending
    {
        const int CurrentFormat = 1;
        const string Path = "Library/LightSide/MigrationPending.json";

        public int format = CurrentFormat;
        public List<string> assetPaths = new();

        /// <summary>
        /// Reads the queue; a missing file is an empty queue. Returns <see langword="false"/> when the file
        /// exists but cannot be trusted — the caller must fall back to a full pass.
        /// </summary>
        public static bool TryLoad(out HashSet<string> pending)
        {
            pending = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(Path)) return true;
            try
            {
                var json = File.ReadAllText(Path);
                if (json.IndexOf("\"format\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"assetPaths\"", StringComparison.Ordinal) < 0)
                    return false;
                var state = JsonUtility.FromJson<MigrationPending>(json);
                if (state == null || state.format != CurrentFormat || state.assetPaths == null)
                    return false;
                foreach (var assetPath in state.assetPaths)
                {
                    if (string.IsNullOrEmpty(assetPath)) return false;
                    pending.Add(assetPath);
                }
                return true;
            }
            catch
            {
                pending.Clear();
                return false;
            }
        }

        public static void Save(IEnumerable<string> pending)
        {
            var state = new MigrationPending();
            state.assetPaths.AddRange(pending);
            state.assetPaths.Sort(StringComparer.Ordinal);
            if (state.assetPaths.Count == 0)
            {
                if (File.Exists(Path)) File.Delete(Path);
                return;
            }
            MigrationFile.WriteAllText(Path, JsonUtility.ToJson(state));
        }
    }
}
