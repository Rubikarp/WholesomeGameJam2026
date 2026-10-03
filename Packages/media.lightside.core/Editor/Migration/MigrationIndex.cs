using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Persistent reverse index <c>token → asset guids</c> for migration discovery. Managed-type tokens are
    /// indexed for every serialized reference and every prefab-instance override that names one; script,
    /// referenced-asset, and override-path tokens only for identities named by a registered migration. The
    /// index therefore stays sparse instead of listing every serialized object.
    /// </summary>
    /// <remarks>
    /// Kept current by <see cref="MigrationIndexPostprocessor"/> and stored in <c>Library/</c> as a rebuildable
    /// cache. Its artifact-dependency version is validated before every migration pass; a stale or malformed
    /// cache is rebuilt from source assets. Tokens exist only for assets the index has read; every full pass
    /// scans the project YAML assets it has not covered and drops those that have left, so an incomplete scan
    /// cannot present itself as a complete one.
    /// </remarks>
    internal sealed class MigrationIndex : ICandidateSource
    {
        const int FormatVersion = 7;
        const string Path = "Library/LightSide/MigrationIndex.json";

        static readonly Regex managedRegex = new(
            @"\{class: ([^,{}]+), ns: ([^,{}]*), asm: ([^{}]+)\}", RegexOptions.Compiled);

        static readonly Regex managedOverrideRegex = new(
            @"propertyPath: '?managedReferences\[-?\d+\]'?\r?\n *value: ([^\r\n]+)", RegexOptions.Compiled);

        static readonly Regex scriptRegex = new(
            @"m_Script:\s*\{[^}]*guid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        static readonly Regex assetReferenceRegex = new(
            @"\bguid:\s*([0-9a-fA-F]{32})", RegexOptions.Compiled);

        static readonly Regex prefabOverrideRegex = new(
            @"(?:^|\r?\n) *- target:\s*\{[^}\r\n]+\}\r?\n *propertyPath:\s*([^\r\n]+)",
            RegexOptions.Compiled);

        readonly Dictionary<string, List<string>> guidToTokens = new();
        readonly Dictionary<string, HashSet<string>> tokenToGuids = new();
        readonly HashSet<string> coveredGuids = new(StringComparer.Ordinal);
        readonly List<string> uninspected = new();

        HashSet<string> targetedScriptGuids = new();
        HashSet<string> targetedReferenceGuids = new();
        HashSet<string> targetedOverridePaths = new(StringComparer.Ordinal);
        string vocabulary;
        uint dependencyVersion;

        static MigrationIndex instance;

        public static MigrationIndex Get(IReadOnlyCollection<string> targetedScripts,
            IReadOnlyCollection<string> targetedReferences,
            IReadOnlyCollection<string> targetedOverrides)
        {
            var vocab = Vocab(targetedScripts, targetedReferences, targetedOverrides);
            var currentDependencyVersion = AssetDatabase.GlobalArtifactDependencyVersion;
            var cached = IsCurrent(instance, vocab, currentDependencyVersion) ? instance : Load();
            if (IsCurrent(cached, vocab, currentDependencyVersion))
            {
                cached.CoverProject();
                return instance = cached;
            }

            var rebuilt = new MigrationIndex
            {
                targetedScriptGuids = new HashSet<string>(targetedScripts),
                targetedReferenceGuids = new HashSet<string>(targetedReferences),
                targetedOverridePaths = new HashSet<string>(targetedOverrides, StringComparer.Ordinal),
                vocabulary = vocab,
            };
            rebuilt.Rebuild();
            return instance = rebuilt;
        }

        static bool IsCurrent(MigrationIndex index, string vocabulary, uint dependencyVersion) =>
            index != null && index.vocabulary == vocabulary && index.dependencyVersion == dependencyVersion;

        /// <summary>
        /// Whether discovery state exists for this project copy. Only a completed pass creates it, so its
        /// absence proves no migration has ever run here — independently of the ledger's version stamps.
        /// </summary>
        public static bool Exists
        {
            get
            {
                if (instance != null) return true;
                if (!File.Exists(Path)) return false;
                instance = Load();
                return instance != null;
            }
        }

        public static MigrationIndex Loaded()
        {
            if (instance != null) return instance;
            if (File.Exists(Path)) instance = Load();
            return instance;
        }

        public IEnumerable<string> FindCandidates(IReadOnlyList<string> tokens)
        {
            var guids = new HashSet<string>();
            foreach (var token in tokens)
                if (tokenToGuids.TryGetValue(token, out var set))
                    guids.UnionWith(set);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    yield return path;
            }
        }

        public IReadOnlyList<string> TokensOf(string guid) =>
            guidToTokens.TryGetValue(guid, out var tokens) ? tokens : Array.Empty<string>();

        /// <summary>
        /// Sources the last scan could not read, each as <c>path — reason</c>. They stay uncovered, so every
        /// later scan retries them; the list holds only what the most recent one rejected.
        /// </summary>
        public IReadOnlyList<string> Uninspected => uninspected;

        public void Rebuild()
        {
            guidToTokens.Clear();
            tokenToGuids.Clear();
            coveredGuids.Clear();
            uninspected.Clear();

            Scan(Resolve(ProjectYamlFiles.Collect()));

            dependencyVersion = AssetDatabase.GlobalArtifactDependencyVersion;
            Save();
        }

        /// <summary>
        /// Reconciles coverage with the project's current YAML assets, reading those never scanned and
        /// forgetting those that left. Enumerates every project asset; belongs to a full pass, not an import.
        /// </summary>
        void CoverProject()
        {
            var present = new HashSet<string>(StringComparer.Ordinal);
            var missing = new List<ScanTarget>();
            foreach (var target in Resolve(ProjectYamlFiles.Collect()))
            {
                present.Add(target.guid);
                if (!coveredGuids.Contains(target.guid)) missing.Add(target);
            }

            var vanished = new List<string>();
            foreach (var guid in coveredGuids)
                if (!present.Contains(guid)) vanished.Add(guid);
            uninspected.Clear();
            if (missing.Count == 0 && vanished.Count == 0) return;

            foreach (var guid in vanished)
            {
                Remove(guid);
                coveredGuids.Remove(guid);
            }
            Scan(missing);
            if (vanished.Count > 0 || missing.Count > uninspected.Count) Save();
        }

        static List<ScanTarget> Resolve(List<ProjectYamlFiles.TargetFile> files)
        {
            var targets = new List<ScanTarget>(files.Count);
            foreach (var file in files)
            {
                var guid = AssetDatabase.AssetPathToGUID(file.assetPath);
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException(
                        $"[LightSide] Migration source '{file.assetPath}' has no asset identity.");
                targets.Add(new ScanTarget(file, guid));
            }
            return targets;
        }

        void Scan(List<ScanTarget> targets)
        {
            var scanned = new ConcurrentBag<(string guid, List<string> tokens)>();
            var skipped = new ConcurrentBag<(string guid, string report)>();
            var targetedScripts = targetedScriptGuids;
            var targetedReferences = targetedReferenceGuids;
            var targetedOverrides = targetedOverridePaths;

            Parallel.ForEach(targets, target =>
            {
                try
                {
                    var read = ProjectYamlFiles.ReadYaml(target.fsPath, out var content, out var reason);
                    if (read == YamlReadResult.Unreadable)
                    {
                        skipped.Add((target.guid, $"{target.assetPath} — {reason}"));
                        return;
                    }
                    if (read == YamlReadResult.Binary) return;

                    var tokens = Extract(content, targetedScripts, targetedReferences, targetedOverrides);
                    if (tokens.Count > 0)
                        scanned.Add((target.guid, tokens));
                }
                catch (Exception e)
                {
                    throw new IOException(
                        $"[LightSide] Cannot index migration source '{target.assetPath}'.", e);
                }
            });

            foreach (var (guid, tokens) in scanned) Put(guid, tokens);

            var skippedGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (guid, report) in skipped)
            {
                skippedGuids.Add(guid);
                uninspected.Add(report);
            }
            uninspected.Sort(StringComparer.Ordinal);
            foreach (var target in targets)
                if (!skippedGuids.Contains(target.guid)) coveredGuids.Add(target.guid);
        }

        readonly struct ScanTarget
        {
            public readonly string assetPath;
            public readonly string fsPath;
            public readonly string guid;

            public ScanTarget(ProjectYamlFiles.TargetFile file, string guid)
            {
                assetPath = file.assetPath;
                fsPath = file.fsPath;
                this.guid = guid;
            }
        }

        public void UpdateAssets(IEnumerable<string> assetPaths)
        {
            bool dirty = false;
            foreach (var assetPath in assetPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    var missingPath = ProjectYamlFiles.ToFsPath(assetPath);
                    if (missingPath != null && (File.Exists(missingPath) || Directory.Exists(missingPath)))
                        throw new InvalidOperationException(
                            $"[LightSide] Imported migration source '{assetPath}' has no asset identity.");
                    continue;
                }

                if (!ProjectYamlFiles.HasYamlExtension(assetPath))
                {
                    dirty |= Uncover(guid);
                    continue;
                }

                var fsPath = ProjectYamlFiles.ToFsPath(assetPath);
                if (fsPath == null || Directory.Exists(fsPath))
                {
                    dirty |= Uncover(guid);
                    continue;
                }
                if (!File.Exists(fsPath))
                    throw new FileNotFoundException(
                        $"[LightSide] Imported migration source '{assetPath}' is missing on disk.", fsPath);
                var read = ProjectYamlFiles.ReadYaml(fsPath, out var content, out _);
                if (read == YamlReadResult.Unreadable)
                {
                    dirty |= Uncover(guid);
                    continue;
                }

                var tokens = read == YamlReadResult.Binary
                    ? new List<string>()
                    : Extract(content, targetedScriptGuids, targetedReferenceGuids,
                        targetedOverridePaths);
                Remove(guid);
                if (tokens.Count > 0) Put(guid, tokens);
                coveredGuids.Add(guid);
                dirty = true;
            }
            var currentDependencyVersion = AssetDatabase.GlobalArtifactDependencyVersion;
            if (dirty || dependencyVersion != currentDependencyVersion)
            {
                dependencyVersion = currentDependencyVersion;
                Save();
            }
        }

        static List<string> Extract(string content, HashSet<string> targetedScripts,
            HashSet<string> targetedReferences, HashSet<string> targetedOverrides)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();

            foreach (Match m in managedRegex.Matches(content))
            {
                var token = MigrationTokens.ManagedType(
                    m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim(), m.Groups[3].Value.Trim());
                if (seen.Add(token)) result.Add(token);
            }

            foreach (Match m in managedOverrideRegex.Matches(content))
                if (TypeSignature.TryParse(m.Groups[1].Value, out var signature) &&
                    seen.Add(signature.Token))
                    result.Add(signature.Token);

            if (targetedScripts.Count > 0)
                foreach (Match m in scriptRegex.Matches(content))
                {
                    var guid = m.Groups[1].Value;
                    if (targetedScripts.Contains(guid))
                    {
                        var token = MigrationTokens.Script(guid);
                        if (seen.Add(token)) result.Add(token);
                    }
                }

            if (targetedReferences.Count > 0)
                foreach (Match m in assetReferenceRegex.Matches(content))
                {
                    var guid = m.Groups[1].Value;
                    if (targetedReferences.Contains(guid))
                    {
                        var token = MigrationTokens.AssetReference(guid);
                        if (seen.Add(token)) result.Add(token);
                    }
                }

            if (targetedOverrides.Count > 0)
                foreach (Match m in prefabOverrideRegex.Matches(content))
                {
                    var path = UnityYaml.Unquote(m.Groups[1].Value.Trim());
                    if (targetedOverrides.Contains(path))
                    {
                        var token = MigrationTokens.PrefabOverride(path);
                        if (seen.Add(token)) result.Add(token);
                    }
                }

            return result;
        }

        void Put(string guid, List<string> tokens)
        {
            guidToTokens[guid] = tokens;
            foreach (var token in tokens)
            {
                if (!tokenToGuids.TryGetValue(token, out var set))
                    tokenToGuids[token] = set = new HashSet<string>();
                set.Add(guid);
            }
        }

        bool Uncover(string guid)
        {
            var covered = coveredGuids.Remove(guid);
            if (!guidToTokens.ContainsKey(guid)) return covered;
            Remove(guid);
            return true;
        }

        void Remove(string guid)
        {
            if (!guidToTokens.TryGetValue(guid, out var tokens)) return;
            foreach (var token in tokens)
                if (tokenToGuids.TryGetValue(token, out var set) && set.Remove(guid) && set.Count == 0)
                    tokenToGuids.Remove(token);
            guidToTokens.Remove(guid);
        }

        static string Vocab(IReadOnlyCollection<string> targetedScripts,
            IReadOnlyCollection<string> targetedReferences,
            IReadOnlyCollection<string> targetedOverrides)
        {
            var sb = new StringBuilder().Append(FormatVersion).Append('|').Append(Application.unityVersion);
            AppendVocabulary(sb, 's', targetedScripts);
            AppendVocabulary(sb, 'r', targetedReferences);
            AppendVocabulary(sb, 'o', targetedOverrides);
            return sb.ToString();
        }

        static void AppendVocabulary(StringBuilder builder, char kind, IReadOnlyCollection<string> values)
        {
            var sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);
            foreach (var value in sorted) builder.Append('|').Append(kind).Append(':').Append(value);
        }

        [Serializable]
        class IndexFile
        {
            public string vocabulary;
            public long dependencyVersion;
            public List<string> targetedScriptGuids = new();
            public List<string> targetedReferenceGuids = new();
            public List<string> targetedOverridePaths = new();
            public List<string> coveredGuids = new();
            public List<Entry> entries = new();
        }

        [Serializable]
        class Entry
        {
            public string guid;
            public List<string> tokens = new();
        }

        static MigrationIndex Load()
        {
            if (!File.Exists(Path)) return null;
            try
            {
                var file = JsonUtility.FromJson<IndexFile>(File.ReadAllText(Path));
                if (file?.entries == null || file.coveredGuids == null ||
                    file.vocabulary?.StartsWith(FormatVersion + "|", StringComparison.Ordinal) != true)
                    return null;

                var index = new MigrationIndex
                {
                    vocabulary = file.vocabulary,
                    dependencyVersion = checked((uint)file.dependencyVersion),
                    targetedScriptGuids = new HashSet<string>(file.targetedScriptGuids ?? new List<string>()),
                    targetedReferenceGuids = new HashSet<string>(file.targetedReferenceGuids ?? new List<string>()),
                    targetedOverridePaths = new HashSet<string>(
                        file.targetedOverridePaths ?? new List<string>(), StringComparer.Ordinal),
                };
                foreach (var guid in file.coveredGuids)
                {
                    if (string.IsNullOrEmpty(guid)) return null;
                    index.coveredGuids.Add(guid);
                }
                foreach (var entry in file.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.guid) || entry.tokens == null ||
                        !index.coveredGuids.Contains(entry.guid))
                        return null;
                    if (entry.tokens.Count > 0) index.Put(entry.guid, entry.tokens);
                }
                return index;
            }
            catch { return null; }
        }

        void Save()
        {
            var file = new IndexFile
            {
                vocabulary = vocabulary,
                dependencyVersion = dependencyVersion,
                targetedScriptGuids = new List<string>(targetedScriptGuids),
                targetedReferenceGuids = new List<string>(targetedReferenceGuids),
                targetedOverridePaths = new List<string>(targetedOverridePaths),
                coveredGuids = new List<string>(coveredGuids),
            };
            foreach (var kv in guidToTokens)
                file.entries.Add(new Entry { guid = kv.Key, tokens = kv.Value });

            try { MigrationFile.WriteAllText(Path, JsonUtility.ToJson(file)); }
            catch (Exception saveFailure)
            {
                try
                {
                    if (File.Exists(Path)) File.Delete(Path);
                    Debug.LogWarning(
                        $"[LightSide] Migration index cache could not be saved and was discarded " +
                        $"({saveFailure.Message}).");
                }
                catch (Exception cleanupFailure)
                {
                    Debug.LogWarning(
                        $"[LightSide] Migration index cache could not be saved or discarded " +
                        $"({saveFailure.Message}; {cleanupFailure.Message}).");
                }
            }
        }
    }
}
