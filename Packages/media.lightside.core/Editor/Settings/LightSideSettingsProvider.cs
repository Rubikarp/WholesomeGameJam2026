using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Keeps <see cref="LightSideSettings"/> in step with the shaders every installed LightSide package
    /// declares through <see cref="ILightSideShaderSet"/>: creates the asset when missing, adds an entry
    /// per included shader, and clears entries a package currently excludes.
    /// </summary>
    /// <remarks>
    /// Runs on editor load and after package registration, before anything renders. Entries are keyed by
    /// shader name, so adding, removing or reordering a package's shaders needs no migration.
    /// </remarks>
    public static class LightSideSettingsProvider
    {
        private const string AssetName = "LightSideSettings.asset";
        private const string ProjectFolder = "Assets/LightSide";
        private const string PackageFolder = "Packages/media.lightside.core";
        private const string BackupName = "LightSide/Settings.json";

        private static bool scheduled;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            UnityEditor.PackageManager.Events.registeredPackages -= OnPackagesRegistered;
            UnityEditor.PackageManager.Events.registeredPackages += OnPackagesRegistered;
            UnityEngine.Rendering.RenderPipelineManager.activeRenderPipelineTypeChanged -= Schedule;
            UnityEngine.Rendering.RenderPipelineManager.activeRenderPipelineTypeChanged += Schedule;
            LightSideSettingsWatch.Register(typeof(LightSideSettings), mayBeMissing =>
            {
                if (mayBeMissing) Schedule();
            });
            Schedule();
        }

        private static void OnPackagesRegistered(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
            => Schedule();

        /// <summary>Re-runs the sync after a package changed a setting that gates one of its shaders.</summary>
        public static void Refresh() => Schedule();

        private static void Schedule()
        {
            if (scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += Ensure;
        }

        private static void Ensure()
        {
            scheduled = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Schedule();
                return;
            }

            LightSideHdrpAssets.Ensure();

            var settings = LoadOrCreate();
            if (settings == null) return;
            LightSideSettings.SetInstance(settings);

            var requested = CollectRequests();
            if (requested.Count == 0) return;

            var entries = new List<LightSideSettings.ShaderEntry>(requested.Count);
            foreach (var pair in requested)
            {
                var shader = pair.Value ? Shader.Find(pair.Key) : null;
                if (pair.Value && shader == null)
                    Debug.LogWarning($"[LightSide] Required shader not found: {pair.Key}");
                entries.Add(new LightSideSettings.ShaderEntry { name = pair.Key, shader = shader });
            }
            entries.Sort(static (a, b) => string.CompareOrdinal(a.name, b.name));

            if (Matches(settings.shaders, entries)) return;
            settings.shaders = entries.ToArray();
            LightSideSettings.SetInstance(settings);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        /// <summary>Shader name to inclusion, merged across packages; a name any package needs included wins.</summary>
        private static Dictionary<string, bool> CollectRequests()
        {
            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var type in TypeCache.GetTypesDerivedFrom<ILightSideShaderSet>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null) continue;
                var set = (ILightSideShaderSet)Activator.CreateInstance(type);
                foreach (var request in set.Shaders)
                {
                    if (string.IsNullOrEmpty(request.Name)) continue;
                    result[request.Name] = result.TryGetValue(request.Name, out var included)
                        ? included || request.Included
                        : request.Included;
                }
            }
            return result;
        }

        private static bool Matches(
            LightSideSettings.ShaderEntry[] current,
            List<LightSideSettings.ShaderEntry> desired)
        {
            if (current == null || current.Length != desired.Count) return false;
            for (var i = 0; i < current.Length; i++)
                if (current[i].name != desired[i].name || current[i].shader != desired[i].shader)
                    return false;
            return true;
        }

        private static LightSideSettings LoadOrCreate()
            => LightSideSettingsGuard.Ensure<LightSideSettings>(
                AssetName, ProjectFolder, PackageFolder, BackupName);
    }
}
