using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Keeps the migration index current after imports, schedules migration once post-reload importing has
    /// completed, and hands imported YAML assets to the migrator so data that arrives while every package
    /// stamp is current still gets migrated. Index maintenance starts only after the first migration pass
    /// builds the cache.
    /// </summary>
    internal sealed class MigrationIndexPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths, bool didDomainReload)
        {
            if (didDomainReload)
                AutoMigrate.Schedule();

            var index = MigrationIndex.Loaded();
            if (index == null) return;

            var touched = new List<string>(importedAssets.Length + movedAssets.Length);
            touched.AddRange(importedAssets);
            touched.AddRange(movedAssets);
            if (touched.Count == 0) return;

            index.UpdateAssets(touched);

            var candidates = new List<string>();
            foreach (var assetPath in touched)
                if (ProjectYamlFiles.HasYamlExtension(assetPath))
                    candidates.Add(assetPath);
            if (candidates.Count > 0)
                AutoMigrate.NoteImported(candidates);
        }
    }
}
