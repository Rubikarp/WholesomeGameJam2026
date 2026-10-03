using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Optional import-time warning that a scene or prefab carries a TMP component the migration
    /// scan does not already account for — a component added after the file was scanned, or one
    /// whose finding is already Completed. A file with open findings stays silent: those are what
    /// the migration is for. Turned on from the Settings tab of the migration window; the setting
    /// is per machine, not shared through version control.
    /// </summary>
    internal class MigrationGuard : AssetPostprocessor
    {
        /// <summary>
        /// Keyed by project folder, not by a hash of it: a hashed key can change between editor
        /// runs and would silently reset the setting.
        /// </summary>
        static readonly string EnabledKey =
            $"LightSide.UniText.MigrationGuard:{Application.dataPath}";

        /// <summary>Whether an import that adds an unaccounted TMP component raises the dialog.</summary>
        internal static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>
        /// Takes over a toggle stored by an earlier state-file schema, once. The preference wins
        /// wherever it already exists.
        /// </summary>
        internal static void AdoptLegacyEnabled(bool enabled)
        {
            if (enabled && !EditorPrefs.HasKey(EnabledKey)) Enabled = true;
        }

        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (Application.isBatchMode || !Enabled) return;

            List<string> candidates = null;
            foreach (var path in importedAssets)
            {
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".unity", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                (candidates ??= new List<string>()).Add(path);
            }
            if (candidates == null) return;

            var state = TryLoadState();
            if (state == null) return;
            var accounted = AccountedGuidsByPath(state);
            foreach (var path in candidates)
            {
                if (MigrationScope.Excludes(path, state.excludedPaths)) continue;
                if (!HasUnaccountedTmpComponent(path, accounted)) continue;
                Warn(path);
                return;
            }
        }

        /// <summary>
        /// Migration state, or null when it cannot be read. The guard is advisory and runs inside
        /// an import batch, where a throw would fail every unrelated asset in the same batch.
        /// </summary>
        static MigrationStateData TryLoadState()
        {
            try { return MigrationStateData.Load(); }
            catch (InvalidDataException exception)
            {
                Debug.LogError($"[UniText] The migration guard cannot read the migration state, " +
                               $"so it made no judgement about this import. {exception.Message}");
                return null;
            }
        }

        /// <summary>
        /// TMP script GUIDs each scanned file still has an open finding for. A path absent from
        /// the result has nothing pending, so any TMP component in it is unaccounted for.
        /// </summary>
        static Dictionary<string, HashSet<string>> AccountedGuidsByPath(MigrationStateData state)
        {
            var session = MigrationSessionData.Load();
            state.RestoreFindings(session.findings);

            var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < session.findings.Count; i++)
            {
                var finding = session.findings[i];
                if (finding.type != FindingType.Component ||
                    finding.status == MigrationStatus.Completed ||
                    string.IsNullOrEmpty(finding.filePath) ||
                    string.IsNullOrEmpty(finding.scriptGuid)) continue;
                if (!result.TryGetValue(finding.filePath, out var guids))
                    result[finding.filePath] = guids = new HashSet<string>(StringComparer.Ordinal);
                guids.Add(finding.scriptGuid);
            }
            return result;
        }

        static bool HasUnaccountedTmpComponent(string assetPath,
            Dictionary<string, HashSet<string>> accounted)
        {
            var fsPath = ProjectYamlFiles.ToFsPath(assetPath);
            if (fsPath == null) return false;

            string content;
            try { content = File.ReadAllText(fsPath); }
            catch (Exception exception) when (exception is IOException or
                                                 UnauthorizedAccessException) { return false; }

            accounted.TryGetValue(assetPath, out var open);
            foreach (var guid in MigrationMapping.AuthoredTmpComponentGuids)
            {
                if (!content.Contains(guid)) continue;
                if (open != null && open.Contains(guid)) continue;
                return true;
            }
            return false;
        }

        static void Warn(string path)
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "TMP component detected",
                $"'{Path.GetFileName(path)}' carries a TextMesh Pro component the migration scan " +
                "does not have an open finding for.\n\n" +
                "This project is being migrated to UniText — consider using UniText instead, or " +
                "re-scan so the component is covered.",
                "OK",
                "Don't warn again",
                "Open migration tool");

            switch (choice)
            {
                case 1:
                    Enabled = false;
                    break;
                case 2:
                    EditorApplication.ExecuteMenuItem(UniTextMenu.Tools.Migration);
                    break;
            }
        }
    }
}
