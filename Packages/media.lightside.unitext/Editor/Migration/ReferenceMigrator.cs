using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Keeps serialized references pointing at what they meant after a migration moved the target.
    /// Replacing a component gives it a new local file id, and swapping a script field's type
    /// leaves its asset reference on the wrong asset; both are repaired here by rewriting the YAML
    /// that carries the reference, so nothing is left resolving to null.
    /// </summary>
    /// <remarks>
    /// Only whole reference nodes are touched — <c>{fileID: n}</c> and <c>{fileID: n, guid: g}</c>
    /// — never a document header, which declares an object rather than pointing at one.
    /// </remarks>
    internal static class ReferenceMigrator
    {
        private static readonly Regex documentRegex =
            new(@"^--- !u!(\d+) &(-?\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex scriptRegex =
            new(@"m_Script:\s*\{[^}]*guid:\s*([a-f0-9]{32})", RegexOptions.Compiled);

        private static readonly Regex gameObjectRegex =
            new(@"m_GameObject:\s*\{fileID:\s*(-?\d+)", RegexOptions.Compiled);

        private static readonly Regex textComponentRegex =
            new(@"^[ \t]*m_TextComponent:[ \t]*\{fileID:[ \t]*(-?\d+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex placeholderRegex =
            new(@"^[ \t]*m_Placeholder:[ \t]*\{fileID:[ \t]*(-?\d+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex prefabModificationTargetRegex =
            new(@"^[ \t]*-[ \t]+target:[ \t]*\{([^}\r\n]*)\}",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex propertyPathRegex =
            new(@"^[ \t]*propertyPath:[ \t]*(.*)$",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex removedComponentsRegex =
            new(@"^[ \t]*m_RemovedComponents:[ \t]*\r?\n" +
                @"(?<entries>(?:[ \t]*-[ \t]*\{[^}\r\n]*\}[ \t]*(?:\r?\n|$))*)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex removedComponentEntryRegex =
            new(@"^[ \t]*-[ \t]*\{([^}\r\n]*)\}",
                RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly Regex referenceFileIdRegex =
            new(@"(?:^|[, \t])fileID:[ \t]*(-?\d+)", RegexOptions.Compiled);

        private static readonly Regex referenceGuidRegex =
            new(@"(?:^|[, \t])guid:[ \t]*([a-fA-F0-9]{32})", RegexOptions.Compiled);

        private static readonly string[] editableScriptGuids =
        {
            MigrationMapping.UniTextEditableGuid,
        };

        /// <summary>Property-path marker identifying a removed-component prefab override.</summary>
        internal const string RemovedComponentPropertyPath = "<removed-component>";

        /// <summary>
        /// Serialized identity of one TMP input field and the text surface that can own its
        /// replacement; zero marks a missing or non-local serialized reference.
        /// </summary>
        internal readonly struct InputFieldSource
        {
            public InputFieldSource(long inputId, long textComponentId, long placeholderId,
                long textOwnerGameObjectId)
            {
                InputId = inputId;
                TextComponentId = textComponentId;
                PlaceholderId = placeholderId;
                TextOwnerGameObjectId = textOwnerGameObjectId;
            }

            public readonly long InputId;
            public readonly long TextComponentId;
            public readonly long PlaceholderId;
            public readonly long TextOwnerGameObjectId;
        }

        /// <summary>One prefab override whose target is a component being replaced.</summary>
        internal readonly struct PrefabModificationTarget
        {
            public PrefabModificationTarget(string assetPath, long prefabInstanceId,
                string targetAssetGuid, long targetFileId, string propertyPath)
            {
                AssetPath = assetPath;
                PrefabInstanceId = prefabInstanceId;
                TargetAssetGuid = targetAssetGuid;
                TargetFileId = targetFileId;
                PropertyPath = propertyPath;
            }

            public readonly string AssetPath;
            public readonly long PrefabInstanceId;
            public readonly string TargetAssetGuid;
            public readonly long TargetFileId;
            public readonly string PropertyPath;
        }

        /// <summary>
        /// Local file ids of the components in <paramref name="assetPath"/> serialized by one of
        /// <paramref name="scriptGuids"/>, keyed by the GameObject each is attached to. The
        /// GameObject id survives a component swap, which is what lets the two sides be paired.
        /// </summary>
        public static Dictionary<long, long> CaptureComponents(string assetPath,
            ICollection<string> scriptGuids)
        {
            var fsPath = ProjectYamlFiles.ToFsPath(assetPath);
            if (fsPath == null) return new Dictionary<long, long>();

            string content;
            try { content = File.ReadAllText(fsPath); }
            catch { return new Dictionary<long, long>(); }

            return CaptureComponentsFromYaml(content, scriptGuids, null);
        }

        private static Dictionary<long, long> CaptureComponentsFromYaml(string content,
            ICollection<string> scriptGuids, string exactAssetPath)
        {
            var result = new Dictionary<long, long>();

            var documents = documentRegex.Matches(content);
            for (var i = 0; i < documents.Count; i++)
            {
                var start = documents[i].Index;
                var end = i + 1 < documents.Count ? documents[i + 1].Index : content.Length;
                var body = content.Substring(start, end - start);

                var script = scriptRegex.Match(body);
                if (!script.Success || !scriptGuids.Contains(script.Groups[1].Value)) continue;

                var owner = gameObjectRegex.Match(body);
                if (!owner.Success) continue;
                if (!long.TryParse(documents[i].Groups[2].Value, out var componentId)) continue;
                if (!long.TryParse(owner.Groups[1].Value, out var gameObjectId)) continue;

                if (exactAssetPath != null && result.ContainsKey(gameObjectId))
                    throw new InvalidDataException(
                        $"Unity YAML asset '{exactAssetPath}' contains more than one matching " +
                        $"component on GameObject {gameObjectId}.");
                result[gameObjectId] = componentId;
            }
            return result;
        }

        /// <summary>
        /// Captures every TMP input field in <paramref name="assetPath"/> together with its text,
        /// placeholder, and the GameObject that owns the referenced text component.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// The asset cannot be resolved to text-serialized Unity YAML.
        /// </exception>
        public static List<InputFieldSource> CaptureInputFieldSources(string assetPath)
        {
            var content = ReadYamlAsset(assetPath);
            var documents = documentRegex.Matches(content);
            var componentOwners = new Dictionary<long, long>();
            var unresolved = new List<(long inputId, long textId, long placeholderId)>();

            for (var i = 0; i < documents.Count; i++)
            {
                var start = documents[i].Index;
                var end = i + 1 < documents.Count ? documents[i + 1].Index : content.Length;
                var body = content.Substring(start, end - start);
                if (!long.TryParse(documents[i].Groups[2].Value, out var componentId))
                    throw new InvalidDataException(
                        $"Unity YAML asset '{assetPath}' contains an invalid document file id.");

                var owner = gameObjectRegex.Match(body);
                if (owner.Success && long.TryParse(owner.Groups[1].Value, out var gameObjectId))
                {
                    if (componentOwners.ContainsKey(componentId))
                        throw new InvalidDataException(
                            $"Unity YAML asset '{assetPath}' repeats component id {componentId}.");
                    componentOwners[componentId] = gameObjectId;
                }

                var script = scriptRegex.Match(body);
                if (!script.Success ||
                    !script.Groups[1].Value.Equals(MigrationMapping.TmpInputFieldGuid,
                        StringComparison.OrdinalIgnoreCase)) continue;

                unresolved.Add((componentId, ReferenceId(body, textComponentRegex),
                    ReferenceId(body, placeholderRegex)));
            }

            var result = new List<InputFieldSource>(unresolved.Count);
            for (var i = 0; i < unresolved.Count; i++)
            {
                var source = unresolved[i];
                componentOwners.TryGetValue(source.textId, out var textOwner);
                result.Add(new InputFieldSource(source.inputId, source.textId,
                    source.placeholderId, textOwner));
            }
            return result;
        }

        /// <summary>
        /// Pairs captured TMP input fields with the editable component saved on their text owner.
        /// Entries whose replacement retained the old id need no redirect and are omitted.
        /// </summary>
        /// <exception cref="InvalidDataException">
        /// A source has no local text owner, the saved editable is absent or ambiguous, or a
        /// source id repeats.
        /// </exception>
        public static Dictionary<long, long> BuildInputFieldRedirects(string assetPath,
            IReadOnlyList<InputFieldSource> sources)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var result = new Dictionary<long, long>();
            if (sources.Count == 0) return result;

            var editables = CaptureComponentsFromYaml(
                ReadYamlAsset(assetPath), editableScriptGuids, assetPath);
            var seen = new HashSet<long>();
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source.InputId == 0 || !seen.Add(source.InputId))
                    throw new InvalidDataException(
                        $"Input-field source identity is invalid or repeated in '{assetPath}'.");
                if (source.TextComponentId == 0 || source.TextOwnerGameObjectId == 0)
                    throw new InvalidDataException(
                        $"TMP input field {source.InputId} in '{assetPath}' has no local text owner.");
                if (!editables.TryGetValue(source.TextOwnerGameObjectId, out var replacement))
                    throw new InvalidDataException(
                        $"TMP input field {source.InputId} in '{assetPath}' has no saved " +
                        $"UniTextEditable on text owner {source.TextOwnerGameObjectId}.");
                if (replacement != source.InputId) result.Add(source.InputId, replacement);
            }
            return result;
        }

        /// <summary>
        /// Finds prefab property modifications and removed-component overrides that target any of
        /// <paramref name="oldFileIds"/> in the exact source asset identified by
        /// <paramref name="targetAssetGuid"/>. An empty result proves no override targets them only
        /// when <paramref name="unreadable"/> is empty too.
        /// </summary>
        /// <param name="unreadable">
        /// Assets whose bytes could not be read, each as <c>path — reason</c>. An override inside
        /// one of them is neither found nor ruled out.
        /// </param>
        /// <exception cref="InvalidDataException">
        /// A matching property modification has no serialized property path or an inspected YAML
        /// asset is invalid.
        /// </exception>
        public static List<PrefabModificationTarget> FindPrefabModifications(
            IEnumerable<ProjectYamlFiles.TargetFile> files, string targetAssetGuid,
            ICollection<long> oldFileIds, out List<string> unreadable)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (string.IsNullOrEmpty(targetAssetGuid))
                throw new ArgumentException("A prefab asset GUID is required.", nameof(targetAssetGuid));
            if (oldFileIds == null) throw new ArgumentNullException(nameof(oldFileIds));

            unreadable = new List<string>();
            var ids = oldFileIds as HashSet<long> ?? new HashSet<long>(oldFileIds);
            var result = new List<PrefabModificationTarget>();
            if (ids.Count == 0) return result;

            foreach (var file in files)
            {
                var read = ProjectYamlFiles.ReadYaml(file.fsPath, out var content, out var reason);
                if (read == YamlReadResult.Unreadable) unreadable.Add($"{file.assetPath} — {reason}");
                if (read != YamlReadResult.Text) continue;

                var documents = documentRegex.Matches(content);
                for (var i = 0; i < documents.Count; i++)
                {
                    if (documents[i].Groups[1].Value != "1001") continue;
                    var start = documents[i].Index;
                    var end = i + 1 < documents.Count ? documents[i + 1].Index : content.Length;
                    var body = content.Substring(start, end - start);
                    if (!long.TryParse(documents[i].Groups[2].Value, out var prefabInstanceId))
                        throw new InvalidDataException(
                            $"Unity YAML asset '{file.assetPath}' contains an invalid prefab-instance id.");

                    var targets = prefabModificationTargetRegex.Matches(body);
                    for (var at = 0; at < targets.Count; at++)
                    {
                        var node = targets[at].Groups[1].Value;
                        if (!MatchesTarget(node, targetAssetGuid, ids, file.assetPath,
                                out var targetFileId)) continue;

                        var propertyStart = targets[at].Index + targets[at].Length;
                        var propertyEnd = at + 1 < targets.Count
                            ? targets[at + 1].Index
                            : body.Length;
                        var property = propertyPathRegex.Match(
                            body, propertyStart, propertyEnd - propertyStart);
                        var propertyPath = property.Success
                            ? property.Groups[1].Value.Trim()
                            : null;
                        if (string.IsNullOrEmpty(propertyPath))
                            throw new InvalidDataException(
                                $"Prefab modification targeting {targetFileId} in " +
                                $"'{file.assetPath}' has no property path.");

                        result.Add(new PrefabModificationTarget(file.assetPath, prefabInstanceId,
                            targetAssetGuid, targetFileId, propertyPath));
                    }

                    var removed = removedComponentsRegex.Match(body);
                    if (!removed.Success) continue;
                    var entries = removedComponentEntryRegex.Matches(
                        removed.Groups["entries"].Value);
                    for (var at = 0; at < entries.Count; at++)
                    {
                        if (!MatchesTarget(entries[at].Groups[1].Value, targetAssetGuid, ids,
                                file.assetPath, out var targetFileId)) continue;
                        result.Add(new PrefabModificationTarget(file.assetPath, prefabInstanceId,
                            targetAssetGuid, targetFileId, RemovedComponentPropertyPath));
                    }
                }
            }
            return result;
        }

        private static bool MatchesTarget(string node, string targetAssetGuid,
            HashSet<long> oldFileIds, string assetPath, out long targetFileId)
        {
            targetFileId = 0;
            var guidMatch = referenceGuidRegex.Match(node);
            if (!guidMatch.Success || !guidMatch.Groups[1].Value.Equals(
                    targetAssetGuid, StringComparison.OrdinalIgnoreCase)) return false;

            var idMatch = referenceFileIdRegex.Match(node);
            if (!idMatch.Success || !long.TryParse(idMatch.Groups[1].Value, out targetFileId))
                throw new InvalidDataException(
                    $"Prefab override in '{assetPath}' has an invalid target id.");
            return oldFileIds.Contains(targetFileId);
        }

        /// <summary>
        /// Pairs the components captured before a migration with the ones that took their place,
        /// by the GameObject both sit on. A GameObject present on only one side is left out: there
        /// is nothing to redirect a reference to.
        /// </summary>
        public static Dictionary<long, long> PairByOwner(
            Dictionary<long, long> before, Dictionary<long, long> after)
        {
            var map = new Dictionary<long, long>();
            foreach (var pair in before)
            {
                if (!after.TryGetValue(pair.Key, out var replacement)) continue;
                if (replacement == pair.Value) continue;
                map[pair.Value] = replacement;
            }
            return map;
        }

        /// <summary>One file's worth of redirects: which ids moved, and inside which asset.</summary>
        internal sealed class ComponentRedirect
        {
            public ComponentRedirect(string assetPath, string assetGuid, Dictionary<long, long> map)
            {
                AssetPath = assetPath;
                AssetGuid = assetGuid;
                Map = map;
            }

            public readonly string AssetPath;
            public readonly string AssetGuid;
            public readonly Dictionary<long, long> Map;
        }

        /// <summary>
        /// Rewrites every reference the migration invalidated across <paramref name="files"/>.
        /// A reference inside the asset that owns the component carries no GUID and is matched by
        /// asset path, while the physical path supplies its bytes; one from another asset carries
        /// the owning asset's GUID. An asset that cannot be read or replaced keeps its stale
        /// references and is named in <see cref="RepairResult.Failed"/>; the pass still covers
        /// every other asset.
        /// </summary>
        public static RepairResult RemapComponents(IEnumerable<ProjectYamlFiles.TargetFile> files,
            IReadOnlyList<ComponentRedirect> redirects)
        {
            if (redirects.Count == 0) return RepairResult.Nothing;

            var byGuid = new Dictionary<string, Dictionary<long, long>>(StringComparer.Ordinal);
            var byPath = new Dictionary<string, Dictionary<long, long>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < redirects.Count; i++)
            {
                if (redirects[i].Map.Count == 0) continue;
                if (!string.IsNullOrEmpty(redirects[i].AssetGuid))
                    byGuid[redirects[i].AssetGuid] = redirects[i].Map;
                byPath[redirects[i].AssetPath] = redirects[i].Map;
            }
            if (byGuid.Count == 0 && byPath.Count == 0) return RepairResult.Nothing;

            var repaired = 0;
            var failed = new List<RepairFailure>();
            foreach (var file in files)
            {
                if (!TryRead(file, failed, out var content)) continue;

                byPath.TryGetValue(file.assetPath, out var local);
                var rewritten = RewriteReferences(content, byGuid, local, out var count);
                if (count == 0) continue;

                if (!TryWrite(file, rewritten, failed)) continue;
                repaired += count;
            }
            return new RepairResult(repaired, failed);
        }

        /// <summary>One asset a repair pass could not rewrite, and what stopped it.</summary>
        internal readonly struct RepairFailure
        {
            public RepairFailure(string assetPath, string message)
            {
                AssetPath = assetPath;
                Message = message;
            }

            public readonly string AssetPath;
            public readonly string Message;
        }

        /// <summary>
        /// What one repair pass rewrote and what it could not. A non-empty <see cref="Failed"/>
        /// means those assets still point at what the migration replaced.
        /// </summary>
        internal readonly struct RepairResult
        {
            public RepairResult(int repaired, List<RepairFailure> failed)
            {
                Repaired = repaired;
                Failed = failed;
            }

            public readonly int Repaired;
            public readonly List<RepairFailure> Failed;

            public static RepairResult Nothing => new(0, new List<RepairFailure>());

            public bool HasFailures => Failed is { Count: > 0 };
        }

        private static bool TryRead(ProjectYamlFiles.TargetFile file, List<RepairFailure> failed,
            out string content)
        {
            try
            {
                content = File.ReadAllText(file.fsPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                content = null;
                failed.Add(new RepairFailure(file.assetPath, exception.Message));
                return false;
            }
        }

        /// <summary>
        /// Replaces one asset's bytes through a temporary file, after asking version control to
        /// make it editable. The target is never truncated first, so a write that fails partway
        /// leaves the original scene or prefab whole.
        /// </summary>
        private static bool TryWrite(ProjectYamlFiles.TargetFile file, string content,
            List<RepairFailure> failed)
        {
            if (!AssetDatabase.IsOpenForEdit(file.assetPath) &&
                !AssetDatabase.MakeEditable(file.assetPath))
            {
                failed.Add(new RepairFailure(file.assetPath,
                    "version control refused to make it editable."));
                return false;
            }

            var temporaryPath = file.fsPath + ".unitext-repair.tmp";
            try
            {
                File.WriteAllText(temporaryPath, content);
                if (File.Exists(file.fsPath)) File.Replace(temporaryPath, file.fsPath, null);
                else File.Move(temporaryPath, file.fsPath);
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed.Add(new RepairFailure(file.assetPath, exception.Message));
                DeleteTemporary(temporaryPath);
                return false;
            }
        }

        private static void DeleteTemporary(string temporaryPath)
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UniText] Migration could not remove its temporary file " +
                    $"'{temporaryPath}': {exception.Message}");
            }
        }

        /// <summary>
        /// Walks the reference nodes of one document. <paramref name="local"/> redirects the
        /// GUID-less nodes of the asset that owns the moved components; <paramref name="byGuid"/>
        /// redirects nodes that name another asset.
        /// </summary>
        private static string RewriteReferences(string content,
            Dictionary<string, Dictionary<long, long>> byGuid,
            Dictionary<long, long> local,
            out int count)
        {
            count = 0;
            var builder = new StringBuilder(content.Length);
            var cursor = 0;
            while (true)
            {
                var open = content.IndexOf("{fileID:", cursor, StringComparison.Ordinal);
                if (open < 0) break;
                var close = content.IndexOf('}', open);
                if (close < 0) break;

                var node = content.Substring(open, close - open + 1);
                var replacement = RewriteNode(node, byGuid, local);
                builder.Append(content, cursor, open - cursor);
                builder.Append(replacement ?? node);
                if (replacement != null) count++;
                cursor = close + 1;
            }
            if (count == 0) return content;
            builder.Append(content, cursor, content.Length - cursor);
            return builder.ToString();
        }

        private static string RewriteNode(string node,
            Dictionary<string, Dictionary<long, long>> byGuid,
            Dictionary<long, long> local)
        {
            var idStart = node.IndexOf(':') + 1;
            var idEnd = idStart;
            while (idEnd < node.Length && (node[idEnd] == ' ' || node[idEnd] == '-' ||
                                           char.IsDigit(node[idEnd]))) idEnd++;
            var idText = node.Substring(idStart, idEnd - idStart).Trim();
            if (idText.Length == 0 || !long.TryParse(idText, out var fileId)) return null;

            Dictionary<long, long> map;
            var guidIndex = node.IndexOf("guid:", StringComparison.Ordinal);
            if (guidIndex < 0)
            {
                map = local;
            }
            else
            {
                var guidStart = guidIndex + 5;
                while (guidStart < node.Length && node[guidStart] == ' ') guidStart++;
                var guidEnd = guidStart;
                while (guidEnd < node.Length && Uri.IsHexDigit(node[guidEnd])) guidEnd++;
                var guid = node.Substring(guidStart, guidEnd - guidStart);
                if (!byGuid.TryGetValue(guid, out map)) return null;
            }

            if (map == null || !map.TryGetValue(fileId, out var replacement)) return null;
            return node.Substring(0, idStart) + " " + replacement + node.Substring(idEnd);
        }

        private static long ReferenceId(string body, Regex property)
        {
            var match = property.Match(body);
            return match.Success && long.TryParse(match.Groups[1].Value, out var fileId)
                ? fileId
                : 0;
        }

        private static string ReadYamlAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("An asset path is required.", nameof(assetPath));
            var fsPath = ProjectYamlFiles.ToFsPath(assetPath);
            if (fsPath == null || !File.Exists(fsPath))
                throw new FileNotFoundException(
                    $"Unity asset '{assetPath}' cannot be resolved to a project file.", fsPath);
            var read = ProjectYamlFiles.ReadYaml(fsPath, out var content, out var unreadable);
            if (read != YamlReadResult.Text)
                throw new InvalidDataException(read == YamlReadResult.Binary
                    ? $"Unity asset '{assetPath}' is not text-serialized YAML."
                    : $"Unity asset '{assetPath}' cannot be read: {unreadable}.");
            return content;
        }

        /// <summary>
        /// Points a serialized asset reference at its UniText counterpart wherever a script field
        /// changed type — a <c>TMP_FontAsset</c> field that became <c>UniTextFont</c> keeps its
        /// reference only if the GUID moves with it.
        /// </summary>
        public static RepairResult RemapAssetReferences(
            IEnumerable<ProjectYamlFiles.TargetFile> files,
            IReadOnlyDictionary<string, string> guidMap,
            ICollection<string> scriptGuids)
        {
            if (guidMap.Count == 0 || scriptGuids.Count == 0) return RepairResult.Nothing;

            var repaired = 0;
            var failed = new List<RepairFailure>();
            foreach (var file in files)
            {
                if (!TryRead(file, failed, out var content)) continue;

                var rewritten = RewriteAssetReferences(content, guidMap, scriptGuids, out var count);
                if (count == 0) continue;

                if (!TryWrite(file, rewritten, failed)) continue;
                repaired += count;
            }
            return new RepairResult(repaired, failed);
        }

        /// <summary>
        /// Rewrites asset GUIDs only inside the documents of the scripts whose fields changed
        /// type. A TMP font is still referenced legitimately by TMP's own assets, so a blanket
        /// GUID swap would break them.
        /// </summary>
        private static string RewriteAssetReferences(string content,
            IReadOnlyDictionary<string, string> guidMap,
            ICollection<string> scriptGuids,
            out int count)
        {
            count = 0;
            var documents = documentRegex.Matches(content);
            if (documents.Count == 0) return content;

            var builder = new StringBuilder(content.Length);
            builder.Append(content, 0, documents[0].Index);

            for (var i = 0; i < documents.Count; i++)
            {
                var start = documents[i].Index;
                var end = i + 1 < documents.Count ? documents[i + 1].Index : content.Length;
                var body = content.Substring(start, end - start);

                var script = scriptRegex.Match(body);
                if (!script.Success || !scriptGuids.Contains(script.Groups[1].Value))
                {
                    builder.Append(body);
                    continue;
                }

                var rewritten = body;
                foreach (var pair in guidMap)
                {
                    var replaced = ReplaceGuidOutsideScript(rewritten, pair.Key, pair.Value, script.Index,
                        script.Length, out var replacements);
                    if (replacements == 0) continue;
                    rewritten = replaced;
                    count += replacements;
                }
                builder.Append(rewritten);
            }
            return count == 0 ? content : builder.ToString();
        }

        /// <summary>Swaps one GUID inside a document, leaving its <c>m_Script</c> node alone.</summary>
        private static string ReplaceGuidOutsideScript(string body, string from, string to,
            int scriptStart, int scriptLength, out int count)
        {
            count = 0;
            var builder = new StringBuilder(body.Length);
            var cursor = 0;
            while (true)
            {
                var index = body.IndexOf(from, cursor, StringComparison.Ordinal);
                if (index < 0) break;
                builder.Append(body, cursor, index - cursor);
                if (index >= scriptStart && index < scriptStart + scriptLength)
                {
                    builder.Append(from);
                }
                else
                {
                    builder.Append(to);
                    count++;
                }
                cursor = index + from.Length;
            }
            if (count == 0) return body;
            builder.Append(body, cursor, body.Length - cursor);
            return builder.ToString();
        }

        /// <summary>
        /// Puts back the references the swap emptied inside the asset it happened in. A field
        /// typed for the TMP component is cleared by Unity the moment that component is destroyed,
        /// and the save writes the emptied field out — so by the time a project-wide redirect runs,
        /// the id it would follow is already gone. The bytes captured before the migration still
        /// hold it, and this writes the replacement id into exactly the fields that lost one.
        /// </summary>
        /// <remarks>
        /// A field is only rewritten when it named a replaced component before and reads
        /// <c>{fileID: 0}</c> now. Anything the user emptied for their own reasons has no entry in
        /// <paramref name="map"/> and is left alone.
        /// </remarks>
        public static string RestoreClearedReferences(string original, string current,
            IReadOnlyDictionary<long, long> map, out int restored)
        {
            restored = 0;
            if (map.Count == 0) return current;

            var wanted = CollectClearedCandidates(original, map);
            if (wanted.Count == 0) return current;

            var builder = new StringBuilder(current.Length);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var document = string.Empty;
            var cursor = 0;

            while (cursor < current.Length)
            {
                var lineEnd = current.IndexOf('\n', cursor);
                if (lineEnd < 0) lineEnd = current.Length;
                var line = current.Substring(cursor, lineEnd - cursor);

                var header = documentRegex.Match(line);
                if (header.Success) document = header.Groups[2].Value;

                var zero = ClearedReferenceStart(line);
                if (zero >= 0 && document.Length > 0)
                {
                    var prefix = line.Substring(0, zero);
                    var occurrence = Occurrence(seen, document, prefix);
                    if (wanted.TryGetValue(Key(document, prefix, occurrence), out var replacement))
                    {
                        line = prefix + line.Substring(zero)
                            .Replace("{fileID: 0", "{fileID: " +
                                replacement.ToString(CultureInfo.InvariantCulture));
                        restored++;
                    }
                }

                builder.Append(line);
                if (lineEnd < current.Length) builder.Append('\n');
                cursor = lineEnd + 1;
            }

            return restored == 0 ? current : builder.ToString();
        }

        /// <summary>
        /// Where each replaced component was named, keyed by document, property and how many times
        /// that property had already appeared in the document.
        /// </summary>
        static Dictionary<string, long> CollectClearedCandidates(string original,
            IReadOnlyDictionary<long, long> map)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var document = string.Empty;
            var cursor = 0;

            while (cursor < original.Length)
            {
                var lineEnd = original.IndexOf('\n', cursor);
                if (lineEnd < 0) lineEnd = original.Length;
                var line = original.Substring(cursor, lineEnd - cursor);
                cursor = lineEnd + 1;

                var header = documentRegex.Match(line);
                if (header.Success)
                {
                    document = header.Groups[2].Value;
                    continue;
                }
                if (document.Length == 0) continue;

                var open = line.IndexOf("{fileID:", StringComparison.Ordinal);
                if (open < 0) continue;
                if (line.IndexOf("guid:", open, StringComparison.Ordinal) >= 0) continue;

                var prefix = line.Substring(0, open);
                var occurrence = Occurrence(seen, document, prefix);
                var idMatch = referenceFileIdRegex.Match(line, open);
                if (!idMatch.Success ||
                    !long.TryParse(idMatch.Groups[1].Value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var id) ||
                    !map.TryGetValue(id, out var replacement)) continue;

                result[Key(document, prefix, occurrence)] = replacement;
            }
            return result;
        }

        /// <summary>Start of a <c>{fileID: 0}</c> node naming nothing, or -1.</summary>
        static int ClearedReferenceStart(string line)
        {
            var open = line.IndexOf("{fileID: 0}", StringComparison.Ordinal);
            return open < 0 || line.IndexOf("guid:", StringComparison.Ordinal) >= 0 ? -1 : open;
        }

        static int Occurrence(Dictionary<string, int> seen, string document, string prefix)
        {
            var key = document + prefix;
            seen.TryGetValue(key, out var count);
            seen[key] = count + 1;
            return count;
        }

        static string Key(string document, string prefix, int occurrence)
            => $"{document}\u0000{prefix}\u0000{occurrence}";

        /// <summary>One serialized reference from a script's field to a component still on TMP.</summary>
        internal readonly struct HeldReference
        {
            public HeldReference(string scriptGuid, string holderPath, string targetPath)
            {
                ScriptGuid = scriptGuid;
                HolderPath = holderPath;
                TargetPath = targetPath;
            }

            /// <summary>Script whose field holds it, and which therefore cannot be retyped yet.</summary>
            public readonly string ScriptGuid;

            /// <summary>Asset the reference is serialized in.</summary>
            public readonly string HolderPath;

            /// <summary>Asset the still-TMP component lives in.</summary>
            public readonly string TargetPath;
        }

        /// <summary>
        /// Finds the scripts that cannot be retyped yet, by looking at what the project actually
        /// serializes. Renaming a field's type from a TMP component to a UniText one drops any
        /// reference it holds to a component that is still TMP: the file id survives, but it no
        /// longer resolves to something of the field's new type, and nothing reports it. A script
        /// no asset points through in that way is free to be rewritten.
        /// </summary>
        /// <param name="unreadable">
        /// Assets whose bytes could not be read, each as <c>path — reason</c>. A field held inside one
        /// of them is neither found nor ruled out, so no script is proven free while it is non-empty.
        /// </param>
        /// <remarks>
        /// Bytes decide, not migration statuses: an asset edited outside the tool counts the same
        /// as one it has not reached yet.
        /// </remarks>
        public static List<HeldReference> FindHeldScripts(
            IEnumerable<ProjectYamlFiles.TargetFile> files, ICollection<string> tmpScriptGuids,
            out List<string> unreadable)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (tmpScriptGuids == null) throw new ArgumentNullException(nameof(tmpScriptGuids));

            unreadable = new List<string>();
            var collected = files as IReadOnlyList<ProjectYamlFiles.TargetFile> ??
                            new List<ProjectYamlFiles.TargetFile>(files);
            var contents = new string[collected.Count];
            var assetGuids = new string[collected.Count];
            var tmpComponents = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < collected.Count; i++)
            {
                var read = ProjectYamlFiles.ReadYaml(collected[i].fsPath, out var content, out var reason);
                if (read == YamlReadResult.Unreadable)
                    unreadable.Add($"{collected[i].assetPath} — {reason}");
                if (read != YamlReadResult.Text) continue;

                contents[i] = content;
                assetGuids[i] = AssetDatabase.AssetPathToGUID(collected[i].assetPath);
                if (string.IsNullOrEmpty(assetGuids[i])) continue;
                CollectTmpComponentKeys(content, assetGuids[i], tmpScriptGuids, tmpComponents);
            }

            var held = new List<HeldReference>();
            if (tmpComponents.Count == 0) return held;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < collected.Count; i++)
            {
                if (contents[i] == null || assetGuids[i] == null) continue;
                CollectHeldScripts(contents[i], assetGuids[i], collected[i].assetPath,
                    tmpComponents, seen, held);
            }
            return held;
        }

        /// <summary>Keys <c>guid|fileID</c> of every component in one asset serialized by a TMP script.</summary>
        static void CollectTmpComponentKeys(string content, string assetGuid,
            ICollection<string> tmpScriptGuids, HashSet<string> result)
        {
            var documents = documentRegex.Matches(content);
            for (var i = 0; i < documents.Count; i++)
            {
                var start = documents[i].Index;
                var end = i + 1 < documents.Count ? documents[i + 1].Index : content.Length;
                var script = scriptRegex.Match(content, start, end - start);
                if (!script.Success || !tmpScriptGuids.Contains(script.Groups[1].Value)) continue;
                result.Add($"{assetGuid}|{documents[i].Groups[2].Value}");
            }
        }

        /// <summary>
        /// Scripts whose documents in one asset point at any of <paramref name="tmpComponents"/>.
        /// A GUID-less node names a component of the same asset, so the holder's own GUID
        /// completes the key.
        /// </summary>
        static void CollectHeldScripts(string content, string assetGuid, string assetPath,
            HashSet<string> tmpComponents, HashSet<string> seen, List<HeldReference> held)
        {
            var documents = documentRegex.Matches(content);
            for (var i = 0; i < documents.Count; i++)
            {
                var start = documents[i].Index;
                var end = i + 1 < documents.Count ? documents[i + 1].Index : content.Length;
                var script = scriptRegex.Match(content, start, end - start);
                if (!script.Success) continue;
                var scriptGuid = script.Groups[1].Value;

                var cursor = start;
                while (cursor < end)
                {
                    var open = content.IndexOf("{fileID:", cursor, end - cursor,
                        StringComparison.Ordinal);
                    if (open < 0) break;
                    var close = content.IndexOf('}', open);
                    if (close < 0 || close >= end) break;

                    var node = content.Substring(open, close - open + 1);
                    cursor = close + 1;
                    if (open >= script.Index && open < script.Index + script.Length) continue;

                    var idMatch = referenceFileIdRegex.Match(node);
                    if (!idMatch.Success) continue;
                    var guidMatch = referenceGuidRegex.Match(node);
                    var owner = guidMatch.Success ? guidMatch.Groups[1].Value : assetGuid;
                    var key = $"{owner}|{idMatch.Groups[1].Value}";
                    if (!tmpComponents.Contains(key)) continue;

                    var targetPath = guidMatch.Success
                        ? AssetDatabase.GUIDToAssetPath(owner)
                        : assetPath;
                    if (!seen.Add($"{scriptGuid}|{key}")) continue;
                    held.Add(new HeldReference(scriptGuid, assetPath,
                        string.IsNullOrEmpty(targetPath) ? assetPath : targetPath));
                }
            }
        }

        /// <summary>GUID of the asset at <paramref name="assetPath"/>, or null when it has none.</summary>
        public static string GuidOf(string assetPath)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            return string.IsNullOrEmpty(guid) ? null : guid;
        }
    }
}
