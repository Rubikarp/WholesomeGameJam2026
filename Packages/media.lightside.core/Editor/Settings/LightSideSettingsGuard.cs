using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Keeps a package's settings asset alive: seeds it and its sibling default assets from the
    /// package's <c>Defaults</c> folder, recreates it after deletion, and restores the asset references
    /// a user had chosen from a backup kept outside <c>Assets</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A settings asset lives in the project, not the package, so a user can edit it — which also means
    /// they can delete it, and a fresh one would otherwise come back empty, silently dropping every
    /// reference they had assigned. The backup lives in <c>ProjectSettings/</c>, which asset deletion
    /// does not reach, and holds GUIDs of the guarded fields only.
    /// </para>
    /// <para>Every LightSide package guards its own settings through this; the per-package parts are the arguments.</para>
    /// </remarks>
    public static class LightSideSettingsGuard
    {
        [Serializable]
        private sealed class Backup
        {
            public List<string> names = new();
            public List<string> guids = new();
        }

        /// <summary>
        /// Returns the project's settings asset of type <typeparamref name="T"/>, creating or repairing it
        /// as needed: sibling defaults are copied in, a deleted asset is recreated from the package default
        /// (or empty when the package ships none), and guarded references are restored from the backup.
        /// </summary>
        /// <param name="assetName">File name of the settings asset, e.g. <c>UniTextSettings.asset</c>.</param>
        /// <param name="projectFolder">Folder under <c>Assets/</c> holding the package's project-side content; the asset lives in its <c>Resources</c>.</param>
        /// <param name="packageFolder">The package's own folder, searched for <c>Defaults</c> when the project has none.</param>
        /// <param name="backupName">Path under <c>ProjectSettings/</c> for the reference backup, e.g. <c>UniText/Settings.json</c>.</param>
        /// <param name="guardedFields">Serialized object-reference fields whose assignment survives deletion of the asset.</param>
        public static T Ensure<T>(string assetName, string projectFolder, string packageFolder,
            string backupName, params string[] guardedFields) where T : ScriptableObject
        {
            var defaultsFolder = FindDefaultsFolder(projectFolder, packageFolder);
            if (defaultsFolder != null && !defaultsFolder.StartsWith("Assets/", StringComparison.Ordinal))
                CopyMissingDefaults(defaultsFolder, projectFolder, assetName);

            var settings = EnsureInResources<T>(defaultsFolder, projectFolder, assetName);
            if (settings == null) return null;

            FillMissingReferences(settings, defaultsFolder, assetName);

            if (guardedFields is { Length: > 0 })
            {
                var so = new SerializedObject(settings);
                if (Restore(so, backupName, guardedFields))
                {
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[LightSide] Restored {assetName} references from backup.");
                }
                Save(new SerializedObject(settings), backupName, guardedFields);
            }

            return settings;
        }

        /// <summary>Writes the guarded references of <paramref name="settings"/> to its backup. Call after the user changes one.</summary>
        public static void Capture(ScriptableObject settings, string backupName, params string[] guardedFields)
        {
            if (settings == null || guardedFields is not { Length: > 0 }) return;
            Save(new SerializedObject(settings), backupName, guardedFields);
        }

        /// <summary>The package's <c>Defaults</c> folder, preferring one the project already carries.</summary>
        public static string FindDefaultsFolder(string projectFolder, string packageFolder)
        {
            var local = projectFolder + "/Defaults";
            if (AssetDatabase.IsValidFolder(local)) return local;
            var packaged = packageFolder + "/Defaults";
            return AssetDatabase.IsValidFolder(packaged) ? packaged : null;
        }

        private static T EnsureInResources<T>(string defaultsFolder, string projectFolder, string assetName)
            where T : ScriptableObject
        {
            var resourcesPath = projectFolder + "/Resources";
            var assetPath = resourcesPath + "/" + assetName;

            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null) return existing;

            foreach (var guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                var found = AssetDatabase.LoadAssetAtPath<T>(path);
                if (found != null) return found;
            }

            EnsureAssetFolder(resourcesPath);

            if (defaultsFolder != null)
            {
                var source = defaultsFolder + "/" + assetName;
                if (AssetDatabase.LoadAssetAtPath<T>(source) != null &&
                    AssetDatabase.CopyAsset(source, assetPath))
                {
                    AssetDatabase.SaveAssets();
                    var copied = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                    if (copied != null) return copied;
                }
            }

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<T>(), assetPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        /// <summary>
        /// Copies each asset reference the package default holds into the project's settings wherever the
        /// project's is still empty, leaving every assigned one alone.
        /// </summary>
        /// <remarks>
        /// This is how a default authored after a project already has its settings asset reaches that
        /// project: the asset itself is never overwritten, so a field the user cleared on purpose stays
        /// cleared only until the next pass — assign it deliberately, or the package default returns.
        /// </remarks>
        private static void FillMissingReferences(ScriptableObject settings, string defaultsFolder, string assetName)
        {
            if (settings == null || defaultsFolder == null) return;

            var defaults = AssetDatabase.LoadAssetAtPath<ScriptableObject>(defaultsFolder + "/" + assetName);
            if (defaults == null || defaults.GetType() != settings.GetType()) return;

            var source = new SerializedObject(defaults);
            var destination = new SerializedObject(settings);
            var property = source.GetIterator();
            var changed = false;
            var enterChildren = true;

            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null)
                    continue;

                var target = destination.FindProperty(property.propertyPath);
                if (target == null || target.propertyType != SerializedPropertyType.ObjectReference ||
                    target.objectReferenceValue != null)
                    continue;

                target.objectReferenceValue = property.objectReferenceValue;
                changed = true;
            }

            if (!changed) return;
            destination.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void CopyMissingDefaults(string defaultsFolder, string projectFolder, string assetName)
        {
            var copied = 0;
            foreach (var guid in AssetDatabase.FindAssets("", new[] { defaultsFolder }))
            {
                var sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(sourcePath)) continue;

                var relativePath = sourcePath.Substring(defaultsFolder.Length + 1);
                var destPath = relativePath == assetName
                    ? projectFolder + "/Resources/" + assetName
                    : projectFolder + "/" + relativePath;

                if (AssetDatabase.LoadAssetAtPath<Object>(destPath) != null) continue;

                EnsureAssetFolder(Path.GetDirectoryName(destPath)?.Replace('\\', '/'));
                if (AssetDatabase.CopyAsset(sourcePath, destPath)) copied++;
                else Debug.LogError($"[LightSide] Failed to copy {sourcePath}");
            }

            if (copied == 0) return;
            AssetDatabase.SaveAssets();
            Debug.Log($"[LightSide] Copied {copied} default asset(s) to {projectFolder}/.");
        }

        private static void EnsureAssetFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private static string BackupPath(string backupName) => "ProjectSettings/" + backupName;

        private static void Save(SerializedObject so, string backupName, string[] fields)
        {
            var data = new Backup();
            var any = false;

            foreach (var field in fields)
            {
                var prop = so.FindProperty(field);
                var guid = prop?.objectReferenceValue != null
                    ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prop.objectReferenceValue))
                    : "";
                data.names.Add(field);
                data.guids.Add(guid);
                if (!string.IsNullOrEmpty(guid)) any = true;
            }

            var path = BackupPath(backupName);
            if (!any && File.Exists(path)) return;

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonUtility.ToJson(data, true));
        }

        private static bool Restore(SerializedObject so, string backupName, string[] fields)
        {
            var path = BackupPath(backupName);
            if (!File.Exists(path)) return false;

            Backup data;
            try { data = JsonUtility.FromJson<Backup>(File.ReadAllText(path)); }
            catch { return false; }
            if (data?.names == null || data.guids == null) return false;

            var restored = false;
            foreach (var field in fields)
            {
                var index = data.names.IndexOf(field);
                if (index < 0 || index >= data.guids.Count) continue;

                var guid = data.guids[index];
                if (string.IsNullOrEmpty(guid)) continue;

                var prop = so.FindProperty(field);
                if (prop == null || prop.objectReferenceValue != null) continue;

                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;

                var obj = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (obj == null) continue;

                prop.objectReferenceValue = obj;
                restored = true;
            }

            if (restored) so.ApplyModifiedPropertiesWithoutUndo();
            return restored;
        }
    }
}
