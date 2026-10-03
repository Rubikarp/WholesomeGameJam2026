using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal enum FindingType : byte
    {
        Component,
        ScriptReference,
        FontAsset,
        Material,
        Animation,
        AssemblyDef,
        RichTextContent,
        MissingScript,
        CompiledDependency,

        /// <summary>An asset the scan could not read, so nothing is known about what is inside it.</summary>
        UnreadableFile,

        /// <summary>A TMP asset with no UniText counterpart — project settings, a style sheet.</summary>
        TmpAsset,
    }

    internal enum MigrationStatus : byte
    {
        NotStarted,
        Completed,
        Skipped,
        Failed,
    }

    internal enum MigrationComplexity : byte
    {
        Simple,
        Moderate,
        Complex,
        Manual,
    }

    internal enum LogSeverity : byte
    {
        Info,
        Warning,
        Error,
    }

    internal enum ManualReviewKind : byte
    {
        RequiredComponent,
        MigrationFailure,
        UnsupportedComponent,
    }

    [Serializable]
    internal sealed class ManualReview
    {
        public ManualReviewKind kind;
        public string assetGuid;
        public string assetPath;
        public string targetFileID;
        public string sourceScriptGuid;
        public string sourceType;
        public string targetObjectName;
        public string objectPath;
        public string dependentType;
        public string requiredType;
        public string reason;
        public string action;
    }

    /// <summary>
    /// One discovered TMP usage in the project.
    /// Identity is based on <see cref="id"/> and restored manual-review state follows asset renames.
    /// </summary>
    [Serializable]
    internal class MigrationFinding
    {
        /// <summary>
        /// Scan identity. A component finding is keyed by file path, TMP script GUID and local
        /// file id; every other kind by file path and <see cref="FindingType"/> alone — which is
        /// why one file yields at most one finding of each of those kinds.
        /// </summary>
        public string id;

        /// <summary>Relative path from project root (e.g. "Assets/Scenes/Main.unity").</summary>
        public string filePath;

        public FindingType type;
        public MigrationComplexity complexity;

        /// <summary>Human-readable description (e.g. "TextMeshProUGUI on 'Title'").</summary>
        public string details;

        /// <summary>Transform path inside the scene/prefab (for component findings).</summary>
        public string objectPath;

        /// <summary>The specific TMP script GUID found (for component findings).</summary>
        public string scriptGuid;

        /// <summary>Unity fileID for the object in the YAML file (for stable identity).</summary>
        public string fileID;

        /// <summary>Specific warnings for this finding.</summary>
        public List<string> warnings;

        [NonSerialized] public MigrationStatus status;
        [NonSerialized] public List<ManualReview> manualReviews;
        [NonSerialized] public bool isSelected;

        public static string ComputeId(string filePath, string scriptGuid, string fileID)
        {
            return ComputeHash($"{filePath}|{scriptGuid}|{fileID}");
        }

        public static string ComputeIdForAsset(string filePath, FindingType type)
        {
            return ComputeHash($"{filePath}|{type}");
        }

        static string ComputeHash(string input)
        {
            unchecked
            {
                int hash1 = 5381;
                int hash2 = hash1;
                for (int i = 0; i < input.Length; i += 2)
                {
                    hash1 = ((hash1 << 5) + hash1) ^ input[i];
                    if (i + 1 < input.Length)
                        hash2 = ((hash2 << 5) + hash2) ^ input[i + 1];
                }
                long combined = ((long)(uint)hash1 << 32) | (uint)(hash1 + hash2 * 1566083941);
                return combined.ToString("x16");
            }
        }
    }

    /// <summary>
    /// Maps a TMP_FontAsset to a UniTextFont + UniTextFontStack.
    /// Stored by asset GUID (stable across moves/renames).
    /// </summary>
    [Serializable]
    internal class FontMappingEntry
    {
        /// <summary>GUID of the TMP_FontAsset .asset file.</summary>
        public string tmpFontGuid;
        /// <summary>Project path of the TMP_FontAsset, as the scan found it.</summary>
        public string tmpFontPath;
        /// <summary>Display name from the TMP_FontAsset (for UI).</summary>
        public string tmpFontName;
        /// <summary>Font family name extracted from TMP font metadata.</summary>
        public string tmpFamilyName;
        /// <summary>Auto-detected TTF/OTF source path (may be empty).</summary>
        public string sourceTtfPath;
        /// <summary>GUID of the assigned UniTextFont asset (empty = unmapped).</summary>
        public string uniTextFontGuid;
        /// <summary>GUID of the assigned UniTextFontStack asset (empty = unmapped).</summary>
        public string uniTextFontStackGuid;
        /// <summary>True if user explicitly skipped this font.</summary>
        public bool skipped;
        /// <summary>
        /// TMP font GUIDs this font falls back to, in TMP's own order. Rebuilt as the trailing
        /// families of <see cref="uniTextFontStackGuid"/>, because a UniText stack resolves a
        /// codepoint by walking its families in order.
        /// </summary>
        public List<string> fallbackGuids;

        public bool IsMapped => !string.IsNullOrEmpty(uniTextFontStackGuid) || skipped;
        public bool HasSource => !string.IsNullOrEmpty(sourceTtfPath);
    }

    /// <summary>One proposed text substitution or diagnostic for a .cs file.</summary>
    [Serializable]
    internal class ScriptReplacement
    {
        public int lineNumber;
        public int columnStart;
        public int columnEnd;
        public string original;
        public string replacement;
        public bool isWarningOnly;
        public bool blocksFile;
        public string warningMessage;
        public bool isSelected = true;
    }

    [Serializable]
    internal class LogEntry
    {
        public string timestamp;
        public LogSeverity severity;
        public string message;
        /// <summary>If this is a script backup, the path to the .bak file for restore.</summary>
        public string backupPath;

        public LogEntry() { }

        public LogEntry(LogSeverity severity, string message)
        {
            timestamp = DateTime.Now.ToString("HH:mm:ss");
            this.severity = severity;
            this.message = message;
        }
    }

    /// <summary>
    /// The TMP-font-to-UniText-font decisions the user made, kept under <c>ProjectSettings/</c>
    /// and shared through version control.
    /// </summary>
    [Serializable]
    internal class FontMappingsData
    {
        const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public List<FontMappingEntry> fontMappings = new();

        /// <summary>TMP font GUIDs listed project-wide in <c>TMP_Settings</c>, in order.</summary>
        public List<string> globalFallbackGuids = new();

        /// <summary>The stack built from <see cref="globalFallbackGuids"/> and chained onto every mapped stack.</summary>
        public string fallbackStackGuid;

        const string Path = "ProjectSettings/UniText/FontMappings.json";

        /// <summary>Project-relative location of the document.</summary>
        public static string FilePath => Path;

        /// <exception cref="InvalidDataException">
        /// The file exists but is not a font-mapping document this tool can read. It is never
        /// replaced by an empty table: an empty table reads as "every font is mapped".
        /// </exception>
        public static FontMappingsData Load()
        {
            if (!File.Exists(Path))
                return new FontMappingsData();
            try
            {
                var json = File.ReadAllText(Path);
                if (json.IndexOf("\"schemaVersion\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"fontMappings\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("The required font-mapping fields are missing.");

                var data = JsonUtility.FromJson<FontMappingsData>(json);
                if (data == null)
                    throw new InvalidDataException("The font-mapping document is empty.");

                data.Validate();
                return data;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Cannot read font mappings '{Path}'. Restore or repair it before continuing.",
                    exception);
            }
        }

        public void Save()
        {
            schemaVersion = CurrentSchemaVersion;
            Validate();
            MigrationFile.WriteAtomically(Path, JsonUtility.ToJson(this, true));
        }

        void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException(
                    $"Font mappings '{Path}' use unsupported schema {schemaVersion}.");
            if (fontMappings == null || globalFallbackGuids == null)
                throw new InvalidDataException(
                    $"Font mappings '{Path}' have null collections.");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fontMappings.Count; i++)
            {
                var entry = fontMappings[i];
                if (entry == null || !IsAssetGuid(entry.tmpFontGuid))
                    throw new InvalidDataException(
                        $"Font mappings '{Path}' have an incomplete entry at index {i}.");
                if (!seen.Add(entry.tmpFontGuid))
                    throw new InvalidDataException(
                        $"Font mappings '{Path}' list TMP font '{entry.tmpFontGuid}' twice.");
            }
        }

        static bool IsAssetGuid(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++)
                if (!Uri.IsHexDigit(value[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// One component the migration took off a GameObject because nothing in UniText can satisfy
    /// what it declared it needs, together with everything it was configured with.
    /// </summary>
    [Serializable]
    internal class RemovedComponent
    {
        /// <summary>Prefab or scene it lived in.</summary>
        public string assetPath;

        /// <summary>Transform path inside that asset.</summary>
        public string objectPath;

        public string componentType;

        /// <summary>The TMP type it declared through <c>RequireComponent</c>, when that is why it went.</summary>
        public string requiredType;

        /// <summary>Why it could not stay.</summary>
        public string reason;

        /// <summary>
        /// Its serialized values, as the editor writes them. This is the only surviving record of
        /// how it was set up: the component cannot be put back, because the type it required is
        /// gone from the object.
        /// </summary>
        public string state;

        /// <summary>
        /// Components in the same asset that held a serialized reference to it. Those fields are
        /// empty now. References from other assets are not covered.
        /// </summary>
        public List<string> referencedBy = new();

        public string removedAt;
    }

    /// <summary>
    /// One setting the migration could not carry to the UniText component, with the value it had.
    /// The component migrated; this is the part of it that did not.
    /// </summary>
    [Serializable]
    internal class LostSetting
    {
        public string assetPath;
        public string objectPath;

        /// <summary>TMP component the setting belonged to.</summary>
        public string componentType;

        /// <summary>Setting as its TMP inspector names it.</summary>
        public string setting;

        /// <summary>What it was set to.</summary>
        public string value;

        /// <summary>What to do about it on the migrated component, when there is something to do.</summary>
        public string advice;

        public string lostAt;
    }

    /// <summary>
    /// Everything a migration could not carry over — components it had to remove, and settings that
    /// have no UniText counterpart — with what each one was, kept beside the other migration
    /// documents so it survives the session and travels through version control.
    /// </summary>
    [Serializable]
    internal class MigrationLossesData
    {
        const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public List<RemovedComponent> removed = new();
        public List<LostSetting> settings = new();

        const string Path = "ProjectSettings/UniText/MigrationLosses.json";

        /// <summary>Project-relative location of the document.</summary>
        public static string FilePath => Path;

        /// <summary>
        /// The record, or an empty one when none is readable. A torn document costs the record of
        /// past removals, never a migration: nothing reads it to decide anything.
        /// </summary>
        public static MigrationLossesData Load()
        {
            if (!File.Exists(Path)) return new MigrationLossesData();

            MigrationLossesData data;
            try { data = JsonUtility.FromJson<MigrationLossesData>(File.ReadAllText(Path)); }
            catch (Exception exception) when (exception is IOException or
                                                  UnauthorizedAccessException or ArgumentException)
            {
                Debug.LogWarning($"[UniText] Cannot read '{Path}': {exception.Message}");
                return new MigrationLossesData();
            }

            return data == null || data.schemaVersion != CurrentSchemaVersion ||
                   data.removed == null || data.settings == null
                ? new MigrationLossesData()
                : data;
        }

        public void Save()
        {
            schemaVersion = CurrentSchemaVersion;
            MigrationFile.WriteAtomically(Path, JsonUtility.ToJson(this, true));
        }

        /// <summary>
        /// Records what one component migration removed and writes the document, so an
        /// interrupted run still leaves the note of everything it took off.
        /// </summary>
        public void AddRemoved(List<RemovedComponent> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Count == 0) return;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null)
                    throw new ArgumentException("A removal record cannot be empty.", nameof(entries));
                removed.Add(entries[i]);
            }
            Save();
        }

        /// <summary>Records the settings one migrated component could not take with it.</summary>
        public void AddLost(List<LostSetting> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (entries.Count == 0) return;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null)
                    throw new ArgumentException("A loss record cannot be empty.", nameof(entries));
                settings.Add(entries[i]);
            }
            Save();
        }

        /// <summary>Whether anything at all was left behind.</summary>
        public bool IsEmpty => removed.Count == 0 && settings.Count == 0;

        /// <summary>How many distinct component types the migration has taken off.</summary>
        public int TypeCount()
        {
            var types = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < removed.Count; i++)
                if (!string.IsNullOrEmpty(removed[i].componentType)) types.Add(removed[i].componentType);
            return types.Count;
        }
    }

    /// <summary>Shared file handling for the migration's own settings documents.</summary>
    internal static class MigrationFile
    {
        /// <summary>
        /// Replaces the file at <paramref name="path"/> through a temporary sibling, so a write
        /// that fails partway leaves the previous document intact rather than truncated.
        /// </summary>
        /// <exception cref="IOException">The document could not be replaced.</exception>
        public static void WriteAtomically(string path, string content)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var temporaryPath = System.IO.Path.Combine(
                string.IsNullOrEmpty(directory) ? "." : directory,
                $".{System.IO.Path.GetFileNameWithoutExtension(path)}-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, content);
                if (File.Exists(path)) File.Replace(temporaryPath, path, null);
                else File.Move(temporaryPath, path);
            }
            catch (Exception writeFailure)
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        $"Cannot write '{path}' or remove its temporary file.",
                        writeFailure, cleanupFailure);
                }
                throw new IOException($"Cannot write '{path}'.", writeFailure);
            }
        }
    }

    /// <summary>
    /// What one file looked like when it was last scanned. A file whose timestamp and length still
    /// match is answered from the previous scan instead of being read, so the cost of a re-scan is
    /// proportional to what actually changed. Findings are not repeated here — they are recovered
    /// from <see cref="MigrationSessionData.findings"/> by path.
    /// </summary>
    [Serializable]
    internal class ScannedFileRecord
    {
        public string path;
        public long writeTicks;
        public long length;
        /// <summary>Raw GUIDs of prefabs this one nests, re-resolved to paths on every scan.</summary>
        public List<string> nestedPrefabGuids;
        /// <summary>Shared-vocabulary tags this file writes.</summary>
        public List<string> sharedTags;
        /// <summary>Ordered TMP font GUIDs this font asset falls back to.</summary>
        public List<string> fontFallbackGuids;
        /// <summary>Ordered TMP font GUIDs this settings asset applies project-wide.</summary>
        public List<string> globalFallbackGuids;
        public string fontName;
        public string fontFamily;
        public bool hasFont;
    }

    [Serializable]
    internal class FindingStatusEntry
    {
        public string id;
        public MigrationStatus status;
        public List<ManualReview> reviews;
    }

    /// <summary>
    /// One asset whose components were replaced, and the local file ids that moved. Kept until a
    /// repair pass rewrites every reference to them, so an asset that refused to be written is
    /// retried instead of being left pointing at a component that no longer exists.
    /// </summary>
    [Serializable]
    internal class OutstandingRedirect
    {
        public string assetPath;
        public string assetGuid;
        /// <summary>Local file ids as they were before the migration; paired with <see cref="toIds"/> by index.</summary>
        public List<long> fromIds = new();
        public List<long> toIds = new();
    }

    [Serializable]
    internal class MigrationStateData
    {
        const int CurrentSchemaVersion = 3;

        public int schemaVersion = CurrentSchemaVersion;
        public string lastScanTime;
        public List<FindingStatusEntry> statuses = new();
        public List<string> excludedPaths = new();
        /// <summary>
        /// Where the guard toggle lived in schema 2. <see cref="MigrationGuard"/> owns the live
        /// value; this is read once, while upgrading such a document, and never written again.
        /// </summary>
        public bool migrationGuardEnabled;

        /// <summary>Component redirects no repair pass has finished applying project-wide.</summary>
        public List<OutstandingRedirect> outstandingRedirects = new();

        /// <summary>GUIDs of rewritten scripts whose font references are not yet fully moved.</summary>
        public List<string> outstandingScriptGuids = new();

        [NonSerialized] Dictionary<string, int> index;
        [NonSerialized] bool dirty;

        const string Path = "ProjectSettings/UniText/MigrationState.json";

        /// <summary>Project-relative location of the document.</summary>
        public static string FilePath => Path;

        public static MigrationStateData Load()
        {
            if (!File.Exists(Path))
                return new MigrationStateData();
            try
            {
                var json = File.ReadAllText(Path);
                if (json.IndexOf("\"schemaVersion\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"statuses\"", StringComparison.Ordinal) < 0 ||
                    json.IndexOf("\"excludedPaths\"", StringComparison.Ordinal) < 0)
                    throw new InvalidDataException("The required migration-state fields are missing.");

                var state = JsonUtility.FromJson<MigrationStateData>(json);
                if (state == null)
                    throw new InvalidDataException("The migration-state document is empty.");

                switch (state.schemaVersion)
                {
                    case 1:
                        state.UpgradeFromV1();
                        break;
                    case 2:
                        MigrationGuard.AdoptLegacyEnabled(state.migrationGuardEnabled);
                        state.migrationGuardEnabled = false;
                        state.schemaVersion = CurrentSchemaVersion;
                        state.dirty = true;
                        break;
                    case CurrentSchemaVersion:
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unsupported migration-state schema {state.schemaVersion}.");
                }

                state.Validate();
                return state;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Cannot read migration state '{Path}'. Restore or repair it before continuing.",
                    exception);
            }
        }

        public void Save()
        {
            Validate();
            MigrationFile.WriteAtomically(Path, JsonUtility.ToJson(this, true));
            dirty = false;
        }

        /// <summary>Records redirects the repair pass has not yet applied everywhere.</summary>
        public void SetOutstandingRedirects(List<OutstandingRedirect> redirects)
        {
            outstandingRedirects = redirects ?? new List<OutstandingRedirect>();
            dirty = true;
        }

        /// <summary>Records rewritten scripts whose font references are not yet fully moved.</summary>
        public void SetOutstandingScriptGuids(List<string> scriptGuids)
        {
            outstandingScriptGuids = scriptGuids ?? new List<string>();
            dirty = true;
        }

        /// <summary>Writes the file only when the migration state changed since the last write.</summary>
        public void SaveIfDirty()
        {
            if (dirty) Save();
        }

        public void RestoreFindings(List<MigrationFinding> findings)
        {
            if (findings == null) throw new ArgumentNullException(nameof(findings));

            var restored = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < findings.Count; i++)
            {
                RestoreFinding(findings[i]);
                restored.Add(findings[i].id);
            }

            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                var entry = statuses[i];
                if (entry.status != MigrationStatus.Failed || restored.Contains(entry.id)) continue;
                if (!ReviewTargetExists(entry.reviews[0]))
                {
                    Drop(i);
                    continue;
                }
                var finding = RestoreManualReview(i);
                findings.Add(finding);
                restored.Add(finding.id);
            }
        }

        /// <summary>
        /// Whether the asset a manual review points at is still in the project. A review for an
        /// asset that is gone has nothing left to resolve, and carrying it forward would hold the
        /// migration open on work that cannot be done.
        /// </summary>
        static bool ReviewTargetExists(ManualReview review)
        {
            var path = AssetDatabase.GUIDToAssetPath(review.assetGuid);
            if (string.IsNullOrEmpty(path)) path = review.assetPath;
            return !string.IsNullOrEmpty(path) &&
                   !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)) &&
                   File.Exists(ProjectYamlFiles.ToFsPath(path) ?? path);
        }

        /// <summary>
        /// Forgets what was recorded about each named finding. A status kept for a finding that no
        /// longer exists would be restored onto the next finding computing the same id.
        /// </summary>
        public void Forget(IReadOnlyList<string> findingIds)
        {
            if (findingIds == null || findingIds.Count == 0) return;

            var drop = new HashSet<string>(findingIds, StringComparer.Ordinal);
            for (int i = statuses.Count - 1; i >= 0; i--)
            {
                if (drop.Contains(statuses[i].id)) Drop(i);
            }
        }

        /// <summary>Forgets one recorded status, keeping the id index consistent.</summary>
        void Drop(int at)
        {
            statuses.RemoveAt(at);
            index = null;
            dirty = true;
        }

        void RestoreFinding(MigrationFinding finding)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));
            if (string.IsNullOrEmpty(finding.id))
                throw new ArgumentException("A finding must have a scan identity.", nameof(finding));

            if (!TryFindEntry(finding, out var at))
            {
                finding.status = MigrationStatus.NotStarted;
                finding.manualReviews = null;
                return;
            }

            var entry = statuses[at];
            finding.status = entry.status;
            finding.manualReviews = CopyReviews(entry.reviews);
        }

        MigrationFinding RestoreManualReview(int at)
        {
            var entry = statuses[at];
            var review = entry.reviews[0];
            var currentPath = AssetDatabase.GUIDToAssetPath(review.assetGuid);
            if (string.IsNullOrEmpty(currentPath)) currentPath = review.assetPath;

            var finding = new MigrationFinding
            {
                id = MigrationFinding.ComputeId(
                    currentPath, review.sourceScriptGuid, review.targetFileID),
                filePath = currentPath,
                type = FindingType.Component,
                complexity = MigrationComplexity.Manual,
                details = $"{review.sourceType} on '{review.objectPath}'",
                objectPath = review.targetObjectName,
                scriptGuid = review.sourceScriptGuid,
                fileID = review.targetFileID,
                status = MigrationStatus.Failed,
            };
            ReKey(at, finding.id);
            RefreshReviewIdentity(at, finding);
            finding.manualReviews = CopyReviews(entry.reviews);
            return finding;
        }

        public void SetFailed(MigrationFinding finding, List<ManualReview> reviews)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));
            if (string.IsNullOrEmpty(finding.id))
                throw new ArgumentException("A finding must have a scan identity.", nameof(finding));
            if (finding.type != FindingType.Component)
                throw new ArgumentException(
                    "Only component findings support durable manual review.", nameof(finding));

            var stored = CopyReviews(reviews);
            ApplyFindingIdentity(finding, stored);
            ValidateReviews(stored, finding.id);

            if (Index.TryGetValue(finding.id, out var at) ||
                TryFindCanonicalEntry(stored[0].assetGuid, stored[0].targetFileID, out at))
            {
                ReKey(at, finding.id);
                statuses[at].status = MigrationStatus.Failed;
                statuses[at].reviews = stored;
            }
            else
            {
                Index[finding.id] = statuses.Count;
                statuses.Add(new FindingStatusEntry
                {
                    id = finding.id,
                    status = MigrationStatus.Failed,
                    reviews = stored,
                });
            }

            finding.status = MigrationStatus.Failed;
            finding.manualReviews = CopyReviews(stored);
            dirty = true;
        }

        void UpgradeFromV1()
        {
            schemaVersion = CurrentSchemaVersion;
            if (statuses != null)
            {
                for (int i = 0; i < statuses.Count; i++)
                {
                    var entry = statuses[i];
                    if (entry == null || entry.status != MigrationStatus.Failed) continue;
                    entry.status = MigrationStatus.NotStarted;
                    entry.reviews = null;
                }
            }
            dirty = true;
        }

        bool TryFindEntry(MigrationFinding finding, out int at)
        {
            if (Index.TryGetValue(finding.id, out at))
            {
                RefreshReviewIdentity(at, finding);
                return true;
            }
            if (!TryGetFindingIdentity(finding, out var assetGuid, out _) ||
                !TryFindCanonicalEntry(assetGuid, finding.fileID, out at))
                return false;

            ReKey(at, finding.id);
            RefreshReviewIdentity(at, finding);
            return true;
        }

        bool TryFindCanonicalEntry(string assetGuid, string targetFileID, out int at)
        {
            at = -1;
            if (!TryParseFileId(targetFileID, out var targetFileId)) return false;
            for (int i = 0; i < statuses.Count; i++)
            {
                var entry = statuses[i];
                if (entry?.status != MigrationStatus.Failed || entry.reviews is not { Count: > 0 })
                    continue;
                var review = entry.reviews[0];
                if (review == null ||
                    !string.Equals(review.assetGuid, assetGuid, StringComparison.OrdinalIgnoreCase) ||
                    !TryParseFileId(review.targetFileID, out var candidateId) ||
                    candidateId != targetFileId)
                    continue;
                if (at >= 0)
                    throw new InvalidDataException(
                        $"Migration state '{Path}' has more than one failed finding for asset " +
                        $"'{assetGuid}', fileID {targetFileID}.");
                at = i;
            }
            return at >= 0;
        }

        void ReKey(int at, string findingId)
        {
            var entry = statuses[at];
            if (entry.id == findingId) return;
            if (Index.TryGetValue(findingId, out var existing) && existing != at)
                throw new InvalidDataException(
                    $"Migration state '{Path}' cannot re-key finding '{entry.id}' to existing id '{findingId}'.");
            Index.Remove(entry.id);
            entry.id = findingId;
            Index[findingId] = at;
            dirty = true;
        }

        void RefreshReviewIdentity(int at, MigrationFinding finding)
        {
            var entry = statuses[at];
            if (entry.status != MigrationStatus.Failed || entry.reviews is not { Count: > 0 } ||
                !TryGetFindingIdentity(finding, out var assetGuid, out _))
                return;

            for (int i = 0; i < entry.reviews.Count; i++)
            {
                var review = entry.reviews[i];
                if (review == null) continue;
                if (review.assetGuid == assetGuid && review.assetPath == finding.filePath &&
                    review.targetFileID == finding.fileID &&
                    review.sourceScriptGuid == finding.scriptGuid &&
                    review.targetObjectName == finding.objectPath) continue;
                review.assetGuid = assetGuid;
                review.assetPath = finding.filePath;
                review.targetFileID = finding.fileID;
                review.sourceScriptGuid = finding.scriptGuid;
                review.targetObjectName = finding.objectPath;
                dirty = true;
            }
        }

        static void ApplyFindingIdentity(MigrationFinding finding, List<ManualReview> reviews)
        {
            if (!TryGetFindingIdentity(finding, out var assetGuid, out _))
                throw new InvalidOperationException(
                    $"Finding '{finding.id}' has no canonical asset and component identity.");
            if (reviews == null) return;
            for (int i = 0; i < reviews.Count; i++)
            {
                var review = reviews[i];
                if (review == null) continue;
                review.assetGuid = assetGuid;
                review.assetPath = finding.filePath;
                review.targetFileID = finding.fileID;
                review.sourceScriptGuid = finding.scriptGuid;
                review.targetObjectName = finding.objectPath;
            }
        }

        static bool TryGetFindingIdentity(MigrationFinding finding, out string assetGuid,
            out long targetFileId)
        {
            assetGuid = null;
            targetFileId = 0;
            if (finding == null || string.IsNullOrEmpty(finding.filePath) ||
                string.IsNullOrEmpty(finding.scriptGuid) ||
                !TryParseFileId(finding.fileID, out targetFileId) || targetFileId == 0)
                return false;
            assetGuid = AssetDatabase.AssetPathToGUID(finding.filePath);
            return IsAssetGuid(assetGuid);
        }

        public void SetStatus(MigrationFinding finding, MigrationStatus status)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));
            SetStatus(finding.id, status);
            finding.status = status;
            finding.manualReviews = null;
        }

        /// <summary>Records a status in memory; call <see cref="SaveIfDirty"/> once the operation completes.</summary>
        public void SetStatus(string findingId, MigrationStatus status)
        {
            if (string.IsNullOrEmpty(findingId))
                throw new ArgumentException("A finding identity is required.", nameof(findingId));
            if (!Enum.IsDefined(typeof(MigrationStatus), status))
                throw new ArgumentOutOfRangeException(nameof(status));
            if (status == MigrationStatus.Failed)
                throw new ArgumentException("A failed finding requires manual-review records.", nameof(status));

            if (Index.TryGetValue(findingId, out var at))
            {
                var changed = statuses[at].status != status ||
                              statuses[at].reviews is { Count: > 0 };
                if (!changed) return;
                statuses[at].status = status;
                statuses[at].reviews = null;
                dirty = true;
                return;
            }
            Index[findingId] = statuses.Count;
            statuses.Add(new FindingStatusEntry { id = findingId, status = status });
            dirty = true;
        }

        void Validate()
        {
            if (schemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException(
                    $"Migration state '{Path}' uses unsupported schema {schemaVersion}.");
            if (statuses == null || excludedPaths == null || outstandingRedirects == null ||
                outstandingScriptGuids == null)
                throw new InvalidDataException(
                    $"Migration state '{Path}' has null state collections.");
            for (int i = 0; i < outstandingRedirects.Count; i++)
            {
                var redirect = outstandingRedirects[i];
                if (redirect == null || string.IsNullOrEmpty(redirect.assetPath) ||
                    redirect.fromIds == null || redirect.toIds == null ||
                    redirect.fromIds.Count != redirect.toIds.Count || redirect.fromIds.Count == 0)
                    throw new InvalidDataException(
                        $"Migration state '{Path}' has an incomplete outstanding redirect at index {i}.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var failedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < statuses.Count; i++)
            {
                var entry = statuses[i];
                if (entry == null || string.IsNullOrEmpty(entry.id))
                    throw new InvalidDataException(
                        $"Migration state '{Path}' has an incomplete status at index {i}.");
                if (!ids.Add(entry.id))
                    throw new InvalidDataException(
                        $"Migration state '{Path}' contains duplicate finding '{entry.id}'.");
                if (!Enum.IsDefined(typeof(MigrationStatus), entry.status))
                    throw new InvalidDataException(
                        $"Migration state '{Path}' contains an unknown status for '{entry.id}'.");
                if (entry.status == MigrationStatus.Failed)
                {
                    ValidateReviews(entry.reviews, entry.id);
                    TryParseFileId(entry.reviews[0].targetFileID, out var targetFileId);
                    if (!failedTargets.Add($"{entry.reviews[0].assetGuid}|{targetFileId}"))
                        throw new InvalidDataException(
                            $"Migration state '{Path}' contains duplicate failed target " +
                            $"'{entry.reviews[0].assetGuid}:{targetFileId}'.");
                }
                else if (entry.reviews is { Count: > 0 })
                    throw new InvalidDataException(
                        $"Migration state '{Path}' keeps failure details for non-failed finding '{entry.id}'.");
            }
        }

        static void ValidateReviews(List<ManualReview> reviews, string findingId)
        {
            if (reviews == null || reviews.Count == 0)
                throw new InvalidDataException(
                    $"Failed finding '{findingId}' has no manual-review record.");

            string assetGuid = null;
            long targetFileId = 0;
            for (int i = 0; i < reviews.Count; i++)
            {
                var review = reviews[i];
                if (review == null)
                    throw new InvalidDataException(
                        $"Failed finding '{findingId}' has an empty manual-review record at index {i}.");
                if (!Enum.IsDefined(typeof(ManualReviewKind), review.kind))
                    throw new InvalidDataException(
                        $"Failed finding '{findingId}' has an unknown manual-review kind at index {i}.");
                if (!IsAssetGuid(review.assetGuid) || string.IsNullOrEmpty(review.assetPath) ||
                    !TryParseFileId(review.targetFileID, out var reviewTargetFileId) ||
                    reviewTargetFileId == 0 ||
                    string.IsNullOrEmpty(review.sourceScriptGuid) ||
                    string.IsNullOrEmpty(review.sourceType) ||
                    string.IsNullOrEmpty(review.reason) || string.IsNullOrEmpty(review.action))
                    throw new InvalidDataException(
                        $"Failed finding '{findingId}' has an incomplete manual-review record at index {i}.");
                if (review.kind == ManualReviewKind.RequiredComponent &&
                    (string.IsNullOrEmpty(review.dependentType) || string.IsNullOrEmpty(review.requiredType)))
                    throw new InvalidDataException(
                        $"Required-component review {i} for finding '{findingId}' has no dependent or required type.");
                if (i == 0)
                {
                    assetGuid = review.assetGuid;
                    targetFileId = reviewTargetFileId;
                }
                else if (!string.Equals(review.assetGuid, assetGuid, StringComparison.OrdinalIgnoreCase) ||
                         !TryParseFileId(review.targetFileID, out var candidateId) ||
                         candidateId != targetFileId)
                {
                    throw new InvalidDataException(
                        $"Failed finding '{findingId}' spans more than one asset component.");
                }
            }
        }

        static bool IsAssetGuid(string value)
        {
            if (value == null || value.Length != 32) return false;
            for (int i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f') &&
                    (character < 'A' || character > 'F'))
                    return false;
            }
            return true;
        }

        static bool TryParseFileId(string value, out long fileId) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out fileId);

        static List<ManualReview> CopyReviews(IReadOnlyList<ManualReview> reviews)
        {
            if (reviews == null) return null;
            var copy = new List<ManualReview>(reviews.Count);
            for (int i = 0; i < reviews.Count; i++)
            {
                var source = reviews[i];
                copy.Add(source == null ? null : new ManualReview
                {
                    kind = source.kind,
                    assetGuid = source.assetGuid,
                    assetPath = source.assetPath,
                    targetFileID = source.targetFileID,
                    sourceScriptGuid = source.sourceScriptGuid,
                    sourceType = source.sourceType,
                    targetObjectName = source.targetObjectName,
                    objectPath = source.objectPath,
                    dependentType = source.dependentType,
                    requiredType = source.requiredType,
                    reason = source.reason,
                    action = source.action,
                });
            }
            return copy;
        }

        Dictionary<string, int> Index
        {
            get
            {
                if (index != null) return index;
                index = new Dictionary<string, int>(statuses.Count);
                for (int i = 0; i < statuses.Count; i++)
                    index[statuses[i].id] = i;
                return index;
            }
        }
    }

    /// <summary>
    /// Scan results kept across domain reloads. Lives in <c>Library/</c>: it is a rebuildable
    /// accelerator, while the user's own decisions live in <see cref="MigrationStateData"/>.
    /// Statuses are not stored here — they are re-read from the state file on load.
    /// </summary>
    [Serializable]
    internal class MigrationSessionData
    {
        const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;

        /// <summary>
        /// UniText release that produced these findings. A session written by a different release
        /// is discarded rather than replayed: detectors, blockers and complexity rules travel with
        /// the release, so its verdicts do not describe what this one would find.
        /// </summary>
        public string toolVersion;

        public string scanTime;

        /// <summary>
        /// Whether the scan was interrupted — cancelled or failed — leaving the files it never
        /// reached unknown. A file it could not read is a finding, not this.
        /// </summary>
        public bool partialScan;

        /// <summary>What stopped the last scan, or null when it ran to the end.</summary>
        public string scanFailure;
        public List<MigrationFinding> findings = new();
        public List<string> prefabOrder = new();
        public List<string> sharedTags = new();
        public List<LogEntry> log = new();
        public List<ScannedFileRecord> scannedFiles = new();

        const string Path = "Library/UniText/MigrationSession.json";

        static string toolVersionCache;

        static string ToolVersion => toolVersionCache ??=
            PackageVersion.Resolve(typeof(UniText).Assembly, "LightSide.UniText.asmdef") ?? string.Empty;

        /// <summary>
        /// The stored scan, or an empty session when none is readable. A torn, older-schema or
        /// foreign-release document is discarded without complaint: this file is a rebuildable
        /// accelerator, and a re-scan is its whole cost.
        /// </summary>
        public static MigrationSessionData Load()
        {
            if (!File.Exists(Path))
                return new MigrationSessionData();

            MigrationSessionData session;
            try { session = JsonUtility.FromJson<MigrationSessionData>(File.ReadAllText(Path)); }
            catch (Exception exception) when (exception is IOException or
                                                  UnauthorizedAccessException or ArgumentException)
            {
                return new MigrationSessionData();
            }

            if (session == null || session.schemaVersion != CurrentSchemaVersion ||
                !string.Equals(session.toolVersion, ToolVersion, StringComparison.Ordinal) ||
                session.findings == null || session.prefabOrder == null ||
                session.sharedTags == null || session.log == null || session.scannedFiles == null)
                return new MigrationSessionData();

            return session;
        }

        public void Save()
        {
            schemaVersion = CurrentSchemaVersion;
            toolVersion = ToolVersion;
            var dir = System.IO.Path.GetDirectoryName(Path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(Path, JsonUtility.ToJson(this, false));
        }
    }

    internal struct MigrationSummary
    {
        public int totalFindings;
        public int completed;
        public int pending;
        public int skipped;
        public int failed;

        public int simpleCount;
        public int moderateCount;
        public int complexCount;
        public int manualCount;

        public int componentCount;
        public int scriptCount;
        public int fontCount;
        public int materialCount;
        public int animationCount;
        public int asmdefCount;
        public int richTextContentCount;
        public int missingScriptCount;
        public int unreadableFileCount;
        public int tmpAssetCount;

        public static MigrationSummary Compute(List<MigrationFinding> findings)
        {
            var s = new MigrationSummary { totalFindings = findings.Count };
            for (int i = 0; i < findings.Count; i++)
            {
                var f = findings[i];

                switch (f.status)
                {
                    case MigrationStatus.Completed: s.completed++; break;
                    case MigrationStatus.Skipped: s.skipped++; break;
                    case MigrationStatus.Failed: s.failed++; break;
                    default: s.pending++; break;
                }

                switch (f.complexity)
                {
                    case MigrationComplexity.Simple: s.simpleCount++; break;
                    case MigrationComplexity.Moderate: s.moderateCount++; break;
                    case MigrationComplexity.Complex: s.complexCount++; break;
                    case MigrationComplexity.Manual: s.manualCount++; break;
                }

                switch (f.type)
                {
                    case FindingType.Component: s.componentCount++; break;
                    case FindingType.ScriptReference: s.scriptCount++; break;
                    case FindingType.FontAsset: s.fontCount++; break;
                    case FindingType.Material: s.materialCount++; break;
                    case FindingType.Animation: s.animationCount++; break;
                    case FindingType.AssemblyDef: s.asmdefCount++; break;
                    case FindingType.RichTextContent: s.richTextContentCount++; break;
                    case FindingType.MissingScript: s.missingScriptCount++; break;
                    case FindingType.UnreadableFile: s.unreadableFileCount++; break;
                    case FindingType.TmpAsset: s.tmpAssetCount++; break;
                }
            }
            return s;
        }
    }
}
