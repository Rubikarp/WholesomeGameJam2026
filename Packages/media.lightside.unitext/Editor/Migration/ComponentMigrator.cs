using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LightSide
{
    internal enum ComponentPreflightState : byte
    {
        Ready,
        Blocked,
        TargetMissing,
        Inconclusive,
    }

    /// <summary>
    /// Migrates TMP components to UniText equivalents using reflection (no compile-time TMPro dependency)
    /// and SerializedObject (proper undo, prefab override, and dirty tracking support).
    /// </summary>
    internal class ComponentMigrator
    {
        enum ComponentIdentityState : byte
        {
            Unavailable,
            Owned,
            Foreign,
        }

        Type tmpTextUiType;
        Type tmpText3DType;
        Type tmpInputFieldType;
        Type tmpSubMeshUiType;
        Type tmpSubMeshType;
        Type tmpSpriteAssetType;
        Type tmpSettingsType;

        bool isInitialized;
        public bool IsTmpAvailable => isInitialized;

        readonly List<LogEntry> log;
        readonly FontMappingsData fontMappings;
        readonly MigrationStateData migrationState;
        readonly string projectFolder;
        TmpSpriteMigrator spriteMigrator;
        readonly Dictionary<Type, Type[]> requiredComponentsByType = new();
        /// <summary>
        /// The project's YAML assets as they were when the current batch started. Prefab-override
        /// scanning reads it once per batch rather than once per file; <see cref="BeginBatch"/>
        /// drops it so an override authored earlier in the session is never missed.
        /// </summary>
        List<ProjectYamlFiles.TargetFile> projectYamlFiles;

        sealed class InputFieldPlan
        {
            public Component source;
            public Component text;
            public Component placeholder;

            public MigrationFinding finding;
            public MigrationFinding textFinding;
            public MigrationFinding placeholderFinding;
            public UniTextSelectable templateSelectable;
            public UniTextEditable templateEditable;

            /// <summary>TMP's selection tint, carried onto the UniTextSelectable highlight.</summary>
            public Color selectionColor;

            /// <summary>Settings this field could not take with it.</summary>
            public List<LostSetting> losses;

            public string textValue;
            public int characterLimit;
            public int inputType;
            public int characterValidation;
            public int keyboardType;
            public int lineType;
            public char maskCharacter;
            public bool readOnly;
            public bool onFocusSelectAll;
            public bool restoreOriginalTextOnEscape;
            public bool hideMobileInput;
            public bool enabled;
        }

        static readonly HashSet<string> replacedScriptGuids = new()
        {
            MigrationMapping.TmpTextUiGuid,
            MigrationMapping.TmpText3DGuid,
        };

        static readonly HashSet<string> uniTextScriptGuids = new()
        {
            MigrationMapping.UniTextGuid,
            MigrationMapping.UniTextWorldGuid,
        };

        /// <summary>
        /// Where a replaced component's local file id moved to, per rewritten asset. A reference
        /// held anywhere in the project still names the old id until these are applied.
        /// </summary>
        public List<ReferenceMigrator.ComponentRedirect> Redirects { get; } = new();

        public ComponentMigrator(List<LogEntry> log, FontMappingsData fontMappings,
            MigrationStateData migrationState, string projectFolder)
        {
            this.log = log;
            this.fontMappings = fontMappings;
            this.migrationState = migrationState;
            this.projectFolder = projectFolder;
        }

        /// <summary>
        /// Pairs the components a file held before the rewrite with the ones that replaced them.
        /// Both sides are read from the file itself, because a component living in prefab contents
        /// has no persistent identity to ask for.
        /// </summary>
        /// <summary>
        /// Records where each replaced component moved, and returns that map so the asset it
        /// happened in can have its own emptied references put back.
        /// </summary>
        Dictionary<long, long> RecordRedirect(string assetPath, Dictionary<long, long> before,
            IReadOnlyList<ReferenceMigrator.InputFieldSource> inputSources,
            List<MigrationFinding> completed)
        {
            var after = ReferenceMigrator.CaptureComponents(assetPath, uniTextScriptGuids);
            var map = ReferenceMigrator.PairByOwner(before, after);
            var migratedInputSources = new List<ReferenceMigrator.InputFieldSource>();
            for (int i = 0; i < completed.Count; i++)
            {
                var finding = completed[i];
                if (finding.scriptGuid != MigrationMapping.TmpInputFieldGuid ||
                    !long.TryParse(finding.fileID, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var inputId)) continue;
                for (int sourceIndex = 0; sourceIndex < inputSources.Count; sourceIndex++)
                {
                    if (inputSources[sourceIndex].InputId != inputId) continue;
                    migratedInputSources.Add(inputSources[sourceIndex]);
                    break;
                }
            }
            var inputMap = ReferenceMigrator.BuildInputFieldRedirects(assetPath,
                migratedInputSources);
            foreach (var pair in inputMap)
            {
                if (map.TryGetValue(pair.Key, out var existing) && existing != pair.Value)
                    throw new InvalidOperationException(
                        $"Component {pair.Key} in '{assetPath}' has two replacement targets.");
                map[pair.Key] = pair.Value;
            }
            if (map.Count == 0) return map;
            Redirects.Add(new ReferenceMigrator.ComponentRedirect(
                assetPath, ReferenceMigrator.GuidOf(assetPath), map));
            return map;
        }

        public bool Initialize()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                tmpTextUiType ??= asm.GetType("TMPro.TextMeshProUGUI");
                tmpText3DType ??= asm.GetType("TMPro.TextMeshPro");
                tmpInputFieldType ??= asm.GetType("TMPro.TMP_InputField");
                tmpSubMeshUiType ??= asm.GetType("TMPro.TMP_SubMeshUI");
                tmpSubMeshType ??= asm.GetType("TMPro.TMP_SubMesh");
                tmpSpriteAssetType ??= asm.GetType("TMPro.TMP_SpriteAsset");
                tmpSettingsType ??= asm.GetType("TMPro.TMP_Settings");
            }

            isInitialized = tmpTextUiType != null;
            if (tmpSpriteAssetType != null)
                spriteMigrator = new TmpSpriteMigrator(
                    tmpSpriteAssetType, tmpSettingsType, projectFolder, log);
            return isInitialized;
        }

        /// <summary>
        /// Re-runs every gate the migration itself would run against one failed finding, and
        /// clears it only when they all pass. <paramref name="findings"/> is the whole scan, not
        /// a filtered subset: an input field is judged together with the text and placeholder it
        /// owns, whose findings live in that list.
        /// </summary>
        /// <summary>
        /// What the migration has taken off objects because UniText cannot satisfy what those
        /// components declared they need. Written as it happens, so an interrupted run still
        /// leaves the record of everything it removed.
        /// </summary>
        readonly MigrationLossesData migrationLosses = MigrationLossesData.Load();

        /// <summary>Everything removed so far, for the surface that reports it.</summary>
        public MigrationLossesData Losses => migrationLosses;

        /// <summary>
        /// Whether the objects this pass edits are ones the undo system can reach. Prefab contents
        /// live in a preview scene that is unloaded when the pass ends, and the file itself is
        /// authored by <c>SaveAsPrefabAsset</c> — an undo record for either can never be applied,
        /// so the prefab path edits directly and rolls back from the captured bytes instead.
        /// </summary>
        bool undoable;

        void RecordObject(UnityEngine.Object target, string name)
        {
            if (undoable) Undo.RecordObject(target, name);
        }

        void RecordCompleteObject(UnityEngine.Object target, string name)
        {
            if (undoable) Undo.RegisterCompleteObjectUndo(target, name);
        }

        T AddComponent<T>(GameObject owner) where T : Component
            => undoable ? Undo.AddComponent<T>(owner) : owner.AddComponent<T>();

        Component AddComponent(GameObject owner, Type type)
            => undoable ? Undo.AddComponent(owner, type) : owner.AddComponent(type);

        void DestroyObject(UnityEngine.Object target)
        {
            if (undoable) Undo.DestroyObjectImmediate(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// Starts a fresh unit of work, discarding what the previous one cached about the project.
        /// Required before any pass that inspects prefab overrides, which change as the session
        /// edits assets.
        /// </summary>
        public void BeginBatch()
        {
            projectYamlFiles = null;
            undoable = false;
        }

        public ComponentPreflightState RecheckFinding(MigrationFinding finding,
            List<MigrationFinding> findings)
        {
            BeginBatch();
            if (finding == null) throw new ArgumentNullException(nameof(finding));
            if (findings == null) throw new ArgumentNullException(nameof(findings));
            if (!IsMigratableAsset(finding.filePath)) return ComponentPreflightState.Inconclusive;

            var tmpType = SourceType(finding.scriptGuid);
            if (tmpType == null)
            {
                return MarkInconclusive(finding, SourceTypeName(finding.scriptGuid),
                    "The TMP component type is not loaded.");
            }

            GameObject prefabRoot = null;
            Scene scene = default;
            var closeScene = false;
            try
            {
                GameObject[] roots;
                if (finding.filePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(finding.filePath);
                    if (prefabRoot == null)
                        return MarkInconclusive(finding, TypeName(tmpType),
                            "Unity could not load the prefab contents.");
                    roots = new[] { prefabRoot };
                }
                else if (finding.filePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        var open = SceneManager.GetSceneAt(i);
                        if (open.path != finding.filePath) continue;
                        scene = open;
                        break;
                    }
                    if (!scene.IsValid())
                    {
                        scene = EditorSceneManager.OpenScene(finding.filePath, OpenSceneMode.Additive);
                        closeScene = true;
                    }
                    if (!scene.IsValid())
                        return MarkInconclusive(finding, TypeName(tmpType),
                            "Unity could not open the scene.");
                    roots = scene.GetRootGameObjects();
                }
                else
                {
                    return MarkInconclusive(finding, TypeName(tmpType),
                        "The finding does not belong to a prefab or scene.");
                }

                var components = new List<(Component component, Type tmpType, string targetGuid)>();
                for (int i = 0; i < roots.Length; i++) CollectTmpComponents(roots[i], components);
                var assetGuid = AssetDatabase.AssetPathToGUID(finding.filePath);
                Component target = null;
                for (int i = 0; i < components.Count; i++)
                {
                    if (components[i].tmpType != tmpType) continue;
                    var candidate = components[i].component;
                    var identity = ComponentIdentity(candidate, assetGuid, out var fileId);
                    if (identity == ComponentIdentityState.Foreign) continue;
                    var matches = identity == ComponentIdentityState.Owned
                        ? fileId == finding.fileID
                        : finding.objectPath == candidate.gameObject.name;
                    if (!matches) continue;
                    if (target != null)
                        return MarkInconclusive(finding, TypeName(tmpType),
                            "More than one loaded component matches this migration finding.");
                    target = candidate;
                }

                if (target == null)
                {
                    if (scene.IsValid() && scene.isDirty && !closeScene)
                        return MarkInconclusive(finding, TypeName(tmpType),
                            "The open scene has unsaved changes, so absence from memory does not " +
                            "prove that the TMP component is absent from disk. Save or revert the " +
                            "scene, then re-check this finding.");
                    migrationState.SetStatus(finding, MigrationStatus.Skipped);
                    Log(LogSeverity.Info,
                        $"Handled manually: {TypeName(tmpType)} is no longer present at " +
                        $"'{finding.objectPath}' in '{finding.filePath}'.");
                    return ComponentPreflightState.TargetMissing;
                }

                if (!MigrationMapping.ComponentGuidMap.TryGetValue(finding.scriptGuid, out var targetGuid))
                    return MarkInconclusive(finding, TypeName(tmpType),
                        "No migration target is registered for this TMP component.");

                var ownership = CaptureOwnership(components);
                var composite = tmpType == tmpInputFieldType ||
                                ownership.OwnerOf.ContainsKey(target);
                if (composite && scene.IsValid() && scene.isDirty && !closeScene)
                    return MarkInconclusive(finding, TypeName(tmpType),
                        "The open scene has unsaved changes, and an input field is judged against " +
                        "the identities saved on disk. Save or revert the scene, then re-check " +
                        "this finding.");

                if (tmpType != tmpInputFieldType &&
                    ownership.OwnerOf.TryGetValue(target, out var owner))
                    return RecheckOwnedChild(finding, target, tmpType, owner, findings);

                if (tmpType == tmpInputFieldType)
                {
                    List<ReferenceMigrator.InputFieldSource> inputSources;
                    try
                    {
                        inputSources =
                            ReferenceMigrator.CaptureInputFieldSources(finding.filePath);
                    }
                    catch (Exception sourceException)
                    {
                        return MarkInconclusive(finding, TypeName(tmpType),
                            $"The saved TMP_InputField identities cannot be read: " +
                            sourceException.Message);
                    }
                    if (!TryPrepareInputField(target, finding.filePath, findings, finding,
                            ownership.OwnerCount, inputSources, out _))
                        return ComponentPreflightState.Blocked;
                }
                else if (!TryPrepareComponent(target, tmpType, targetGuid, finding.filePath,
                             finding, out _))
                    return ComponentPreflightState.Blocked;

                migrationState.SetStatus(finding, MigrationStatus.NotStarted);
                Log(LogSeverity.Info,
                    $"Ready: '{finding.objectPath}' in '{finding.filePath}' passes every gate " +
                    "the migration applies.");
                return ComponentPreflightState.Ready;
            }
            catch (Exception ex)
            {
                return MarkInconclusive(finding, TypeName(tmpType), ex.Message);
            }
            finally
            {
                if (prefabRoot != null) PrefabUtility.UnloadPrefabContents(prefabRoot);
                if (closeScene && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>
        /// Reports a text or placeholder component through the input field that owns it. Only the
        /// field can replace such a component, so its own gates say nothing: it clears exactly
        /// when the field does.
        /// </summary>
        ComponentPreflightState RecheckOwnedChild(MigrationFinding finding, Component child,
            Type tmpType, Component owner, List<MigrationFinding> findings)
        {
            var ownerPath = HierarchyPath(owner.transform);
            var childPath = HierarchyPath(child.transform);
            if (!TryFindFinding(owner, tmpInputFieldType, finding.filePath, findings,
                    out var ownerFinding))
                return MarkInconclusive(finding, TypeName(tmpType),
                    $"It belongs to TMP_InputField '{ownerPath}', which the last scan has no " +
                    "finding for. Re-scan the project, then re-check the field.");

            switch (ownerFinding.status)
            {
                case MigrationStatus.Skipped:
                    migrationState.SetStatus(finding, MigrationStatus.Skipped);
                    Log(LogSeverity.Info,
                        $"Skipped '{childPath}' in '{finding.filePath}': TMP_InputField " +
                        $"'{ownerPath}' owns it and was skipped.");
                    return ComponentPreflightState.TargetMissing;

                case MigrationStatus.Completed:
                    return MarkInconclusive(finding, TypeName(tmpType),
                        $"TMP_InputField '{ownerPath}' owns it and reports migrated, yet this " +
                        "component is still on TMP. Re-scan the project.");

                case MigrationStatus.Failed:
                    migrationState.SetFailed(finding, new List<ManualReview>
                    {
                        CreateReview(ManualReviewKind.UnsupportedComponent, finding,
                            finding.filePath, tmpType, childPath,
                            $"TMP_InputField '{ownerPath}' owns it: {OwnerBlockReason(ownerFinding)}",
                            "Resolve the owning TMP_InputField's blocker, then re-check that field."),
                    });
                    Log(LogSeverity.Warning,
                        $"Blocked '{childPath}' in '{finding.filePath}': its owning " +
                        $"TMP_InputField '{ownerPath}' cannot migrate.");
                    return ComponentPreflightState.Blocked;

                default:
                    migrationState.SetStatus(finding, MigrationStatus.NotStarted);
                    Log(LogSeverity.Info,
                        $"Ready through its owner: '{childPath}' in '{finding.filePath}' is " +
                        $"replaced together with TMP_InputField '{ownerPath}'.");
                    return ComponentPreflightState.Ready;
            }
        }

        /// <summary>Why an owning input field was refused, as its own manual review records it.</summary>
        static string OwnerBlockReason(MigrationFinding ownerFinding)
        {
            var reason = ownerFinding?.manualReviews is { Count: > 0 }
                ? ownerFinding.manualReviews[0].reason
                : null;
            return string.IsNullOrEmpty(reason)
                ? "it was refused without a recorded reason."
                : reason;
        }

        ComponentPreflightState MarkInconclusive(MigrationFinding finding, string sourceType,
            string reason)
        {
            migrationState.SetFailed(finding, new List<ManualReview>
            {
                CreateReview(ManualReviewKind.MigrationFailure, finding, finding.filePath,
                    sourceType, finding.objectPath, reason,
                    "Resolve the reported asset or identity problem, then re-check this finding."),
            });
            Log(LogSeverity.Error,
                $"Cannot re-check '{finding.objectPath}' in '{finding.filePath}': {reason}");
            return ComponentPreflightState.Inconclusive;
        }

        /// <summary>
        /// Rewrites one prefab and reports whether it can be left behind. A file with nothing
        /// pending is a success: each finding's own status carries what happened to it, and only
        /// a refusal — an unreadable or unsavable asset, a missing script, a failed preflight —
        /// answers false. <paramref name="allowedFindingIds"/> narrows which pending
        /// findings this pass may start from — null means every one of them. It never narrows
        /// <paramref name="findings"/>, which must stay whole: an input field is judged against
        /// the findings of the text and placeholder it owns.
        /// </summary>
        public bool MigratePrefab(string prefabPath, List<MigrationFinding> findings,
            HashSet<string> allowedFindingIds)
        {
            if (!IsMigratableAsset(prefabPath)) return false;
            if (!TryCaptureAssetBackup(prefabPath, out var prefabFsPath, out var originalBytes,
                    out var backupError))
            {
                Log(LogSeverity.Error,
                    $"Cannot protect '{prefabPath}' before migration: {backupError}");
                return false;
            }

            var before = ReferenceMigrator.CaptureComponents(prefabPath, replacedScriptGuids);
            List<ReferenceMigrator.InputFieldSource> inputSources;
            try
            {
                inputSources = ReferenceMigrator.CaptureInputFieldSources(prefabPath);
            }
            catch (Exception ex)
            {
                Log(LogSeverity.Error,
                    $"Cannot capture TMP_InputField identities in '{prefabPath}': {ex.Message}");
                return false;
            }
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Log(LogSeverity.Error, $"Cannot load prefab: {prefabPath}");
                return false;
            }

            var completed = new List<MigrationFinding>();
            var createdSpriteAssets = new List<string>();
            var savedToDisk = false;
            var restored = false;
            var redirectCount = Redirects.Count;
            undoable = false;
            try
            {
                var broken = DescribeMissingScripts(root);
                if (broken != null)
                {
                    Log(LogSeverity.Error,
                        $"Skipped '{prefabPath}': Unity refuses to save a prefab that carries a " +
                        $"missing script, and this one has {broken}. Restore the script or remove " +
                        "the component, then migrate the prefab again.");
                    return false;
                }

                int migrated = MigrateHierarchies(new[] { root }, prefabPath, findings, completed,
                    inputSources, createdSpriteAssets, allowedFindingIds);
                if (migrated == 0)
                {
                    spriteMigrator?.RollbackCreated(createdSpriteAssets);
                    Log(LogSeverity.Info, $"Prefab '{prefabPath}': nothing to migrate");
                    return true;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var saved);
                if (!saved)
                {
                    spriteMigrator?.RollbackCreated(createdSpriteAssets);
                    FailFindings(completed,
                        "Unity refused to save the prefab; the file on disk is unchanged.");
                    Log(LogSeverity.Error,
                        $"Prefab '{prefabPath}' could not be saved — {migrated} component(s) were " +
                        "left unchanged and recorded for manual review. The Unity console holds the reason.");
                    return false;
                }
                savedToDisk = true;

                var moved = RecordRedirect(prefabPath, before, inputSources, completed);
                RestoreLocalReferences(prefabPath, prefabFsPath, originalBytes, moved);
                CompleteFindings(completed);
                Log(LogSeverity.Info, $"Prefab '{prefabPath}': {migrated} component(s) migrated");
                return true;
            }
            catch (Exception ex)
            {
                spriteMigrator?.RollbackCreated(createdSpriteAssets);
                while (Redirects.Count > redirectCount) Redirects.RemoveAt(Redirects.Count - 1);
                var reason = ex.Message;
                if (savedToDisk)
                {
                    try
                    {
                        System.IO.File.WriteAllBytes(prefabFsPath, originalBytes);
                        restored = true;
                    }
                    catch (Exception restoreException)
                    {
                        reason += $" Restoring the original prefab also failed: " +
                                  restoreException.Message;
                    }
                }
                FailFindings(completed, reason);
                Log(LogSeverity.Error, $"Error migrating prefab '{prefabPath}': {reason}");
                return false;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
                if (restored)
                    AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>
        /// Puts back the references inside one asset that the swap emptied. Unity clears a field
        /// typed for the TMP component the moment that component is destroyed, and the save writes
        /// the emptied field out; the bytes captured before the migration still name it, so the
        /// replacement id goes exactly where one was lost.
        /// </summary>
        void RestoreLocalReferences(string assetPath, string fsPath, byte[] originalBytes,
            Dictionary<long, long> map)
        {
            if (map.Count == 0) return;

            string original;
            string current;
            try
            {
                original = new System.Text.UTF8Encoding(false).GetString(originalBytes);
                current = System.IO.File.ReadAllText(fsPath);
            }
            catch (Exception exception) when (exception is System.IO.IOException or
                                                  UnauthorizedAccessException)
            {
                Log(LogSeverity.Error,
                    $"Cannot re-read '{assetPath}' to restore its own references: {exception.Message}");
                return;
            }

            var repaired = ReferenceMigrator.RestoreClearedReferences(original, current, map,
                out var restored);
            if (restored == 0) return;

            try
            {
                System.IO.File.WriteAllText(fsPath, repaired);
            }
            catch (Exception exception) when (exception is System.IO.IOException or
                                                  UnauthorizedAccessException)
            {
                Log(LogSeverity.Error,
                    $"Cannot write '{assetPath}' back after restoring its references: " +
                    exception.Message);
                return;
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            Log(LogSeverity.Info,
                $"  Restored {restored} reference(s) inside '{assetPath}' that the swap emptied.");
        }

        /// <summary>
        /// Whether a path names something Unity can actually open and the migration is allowed to
        /// rewrite. A GUID alone does not say so: the asset database keeps one for a file that has
        /// been deleted, and every editor call taking an asset path throws on that. A disk walk has
        /// the mirror problem — it sees a package's hidden <c>Samples~</c> folder, which the asset
        /// database does not.
        /// </summary>
        bool IsMigratableAsset(string assetPath)
        {
            if (MigrationScope.Excludes(assetPath, migrationState.excludedPaths))
            {
                Log(LogSeverity.Warning,
                    $"Skipped '{assetPath}': it is excluded, and the migration leaves an excluded " +
                    "asset to its owner.");
                return false;
            }
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) &&
                System.IO.File.Exists(ProjectYamlFiles.ToFsPath(assetPath) ?? assetPath))
                return true;
            Log(LogSeverity.Warning,
                $"Skipped '{assetPath}': there is no such asset — it was deleted, or it is a " +
                "hidden folder or a path outside the project. Nothing there can be migrated.");
            return false;
        }

        /// <summary>Tail naming how many further entries a message left out.</summary>
        static string MoreEntries(int count) => count > 1 ? $" and {count - 1} more" : string.Empty;

        static bool TryCaptureAssetBackup(string assetPath, out string fsPath, out byte[] bytes,
            out string error)
        {
            fsPath = ProjectYamlFiles.ToFsPath(assetPath);
            bytes = null;
            if (fsPath == null)
            {
                error = "The asset has no project filesystem path.";
                return false;
            }
            try
            {
                bytes = System.IO.File.ReadAllBytes(fsPath);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Names the objects carrying a missing script, or null when the hierarchy is sound.</summary>
        static string DescribeMissingScripts(GameObject root)
        {
            var names = new List<string>();
            var total = 0;
            CollectMissingScripts(root, names, ref total);
            if (total == 0) return null;
            var listed = string.Join(", ", names);
            return names.Count < total
                ? $"{total} on '{listed}' and more"
                : $"{total} on '{listed}'";
        }

        static void CollectMissingScripts(GameObject go, List<string> names, ref int total)
        {
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
            if (count > 0)
            {
                total += count;
                if (names.Count < 5) names.Add(go.name);
            }
            for (int i = 0; i < go.transform.childCount; i++)
                CollectMissingScripts(go.transform.GetChild(i).gameObject, names, ref total);
        }

        void CompleteFindings(List<MigrationFinding> completed)
        {
            for (int i = 0; i < completed.Count; i++)
            {
                migrationState.SetStatus(completed[i], MigrationStatus.Completed);
            }
        }

        /// <summary>Marks every claimed rewrite as failed when its asset could not be committed.</summary>
        void FailFindings(List<MigrationFinding> claimed, string reason)
        {
            for (int i = 0; i < claimed.Count; i++)
            {
                var finding = claimed[i];
                migrationState.SetFailed(finding, new List<ManualReview>
                {
                    CreateReview(ManualReviewKind.MigrationFailure, finding, finding.filePath,
                        SourceTypeName(finding.scriptGuid), finding.objectPath, reason,
                        "Resolve the reported migration or save failure, then re-check this finding."),
                });
            }
            claimed.Clear();
        }

        /// <summary>
        /// Rewrites one scene and reports whether it can be left behind. A file with nothing
        /// pending is a success: each finding's own status carries what happened to it, and only
        /// a refusal — an unopenable or unsavable scene, a failed preflight — answers false.
        /// <paramref name="allowedFindingIds"/> narrows which pending findings
        /// this pass may start from — null means every one of them. It never narrows
        /// <paramref name="findings"/>, which must stay whole: an input field is judged against
        /// the findings of the text and placeholder it owns.
        /// </summary>
        public bool MigrateScene(string scenePath, List<MigrationFinding> findings,
            HashSet<string> allowedFindingIds)
        {
            if (!IsMigratableAsset(scenePath)) return false;
            if (!TryCaptureAssetBackup(scenePath, out var sceneFsPath, out var originalBytes,
                    out var backupError))
            {
                Log(LogSeverity.Error,
                    $"Cannot protect '{scenePath}' before migration: {backupError}");
                return false;
            }

            var before = ReferenceMigrator.CaptureComponents(scenePath, replacedScriptGuids);
            List<ReferenceMigrator.InputFieldSource> inputSources;
            try
            {
                inputSources = ReferenceMigrator.CaptureInputFieldSources(scenePath);
            }
            catch (Exception ex)
            {
                Log(LogSeverity.Error,
                    $"Cannot capture TMP_InputField identities in '{scenePath}': {ex.Message}");
                return false;
            }
            Scene scene = default;
            bool wasAlreadyOpen = false;

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.path == scenePath)
                {
                    scene = s;
                    wasAlreadyOpen = true;
                    break;
                }
            }

            if (!wasAlreadyOpen)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid())
                {
                    Log(LogSeverity.Error, $"Cannot open scene: {scenePath}");
                    return false;
                }
            }

            var completed = new List<MigrationFinding>();
            var createdSpriteAssets = new List<string>();
            var undoGroup = -1;
            var savedToDisk = false;
            var restored = false;
            var redirectCount = Redirects.Count;
            undoable = true;
            try
            {
                if (wasAlreadyOpen)
                {
                    Undo.IncrementCurrentGroup();
                    undoGroup = Undo.GetCurrentGroup();
                }
                Undo.SetCurrentGroupName($"Migrate TMP → UniText in {System.IO.Path.GetFileName(scenePath)}");

                var rootObjects = scene.GetRootGameObjects();
                int totalMigrated = MigrateHierarchies(rootObjects, scenePath, findings, completed,
                    inputSources, createdSpriteAssets, allowedFindingIds);

                if (totalMigrated == 0)
                {
                    spriteMigrator?.RollbackCreated(createdSpriteAssets);
                    if (undoGroup >= 0)
                    {
                        Undo.CollapseUndoOperations(undoGroup);
                        undoGroup = -1;
                    }
                    Log(LogSeverity.Info, $"Scene '{scenePath}': nothing to migrate");
                    return true;
                }

                if (!EditorSceneManager.SaveScene(scene))
                {
                    spriteMigrator?.RollbackCreated(createdSpriteAssets);
                    if (undoGroup >= 0)
                    {
                        Undo.RevertAllDownToGroup(undoGroup);
                        undoGroup = -1;
                    }
                    FailFindings(completed,
                        "Unity refused to save the scene; the file on disk is unchanged.");
                    Log(LogSeverity.Error,
                        $"Scene '{scenePath}' could not be saved — {totalMigrated} component(s) " +
                        "were rolled back and recorded for manual review. The Unity console holds the reason.");
                    return false;
                }
                savedToDisk = true;

                var moved = RecordRedirect(scenePath, before, inputSources, completed);
                RestoreLocalReferences(scenePath, sceneFsPath, originalBytes, moved);
                CompleteFindings(completed);
                if (undoGroup >= 0)
                {
                    Undo.CollapseUndoOperations(undoGroup);
                    undoGroup = -1;
                }
                Log(LogSeverity.Info, $"Scene '{scenePath}': {totalMigrated} component(s) migrated");
                return true;
            }
            catch (Exception ex)
            {
                spriteMigrator?.RollbackCreated(createdSpriteAssets);
                if (undoGroup >= 0)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    undoGroup = -1;
                }
                while (Redirects.Count > redirectCount) Redirects.RemoveAt(Redirects.Count - 1);
                var reason = ex.Message;
                if (savedToDisk)
                {
                    try
                    {
                        System.IO.File.WriteAllBytes(sceneFsPath, originalBytes);
                        restored = true;
                    }
                    catch (Exception restoreException)
                    {
                        reason += $" Restoring the original scene also failed: " +
                                  restoreException.Message;
                    }
                }
                FailFindings(completed, reason);
                Log(LogSeverity.Error, $"Error migrating scene '{scenePath}': {reason}");
                return false;
            }
            finally
            {
                if (!wasAlreadyOpen)
                    EditorSceneManager.CloseScene(scene, true);
                if (restored)
                    AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceUpdate);
            }
        }

        int MigrateHierarchies(IReadOnlyList<GameObject> roots, string filePath,
            List<MigrationFinding> findings,
            List<MigrationFinding> completed,
            IReadOnlyList<ReferenceMigrator.InputFieldSource> inputSources,
            List<string> createdSpriteAssets,
            HashSet<string> allowedFindingIds)
        {
            var tmpComponents = new List<(Component component, Type tmpType, string targetGuid)>();
            for (int i = 0; i < roots.Count; i++)
                CollectTmpComponents(roots[i], tmpComponents);

            var ownership = CaptureOwnership(tmpComponents);
            var matchedFindings = new HashSet<MigrationFinding>();

            var inputPlans = new List<InputFieldPlan>();
            for (int i = 0; i < tmpComponents.Count; i++)
            {
                var candidate = tmpComponents[i];
                if (candidate.tmpType != tmpInputFieldType) continue;
                if (!TryMatchFinding(candidate.component, candidate.tmpType, filePath, findings,
                        out var finding)) continue;
                if (finding.status != MigrationStatus.NotStarted || !IsAllowed(finding)) continue;
                ClaimMatch(candidate.component, finding);
                if (TryPrepareInputField(candidate.component, filePath, findings, finding,
                        ownership.OwnerCount, inputSources, out var plan))
                {
                    ClaimMatch(plan.text, plan.textFinding);
                    if (plan.placeholder != null)
                        ClaimMatch(plan.placeholder, plan.placeholderFinding);
                    inputPlans.Add(plan);
                }
            }

            ResolveUnplannedOwned(tmpComponents, ownership, filePath, findings, inputPlans,
                allowedFindingIds);

            var prepared = new List<(Component component, Type tmpType, Type targetType,
                MigrationFinding finding)>();

            foreach (var (component, tmpType, targetGuid) in tmpComponents)
            {
                if (tmpType == tmpInputFieldType ||
                    ownership.OwnerOf.ContainsKey(component)) continue;
                if (!TryMatchFinding(component, tmpType, filePath, findings, out var finding))
                    continue;
                if (finding.status != MigrationStatus.NotStarted || !IsAllowed(finding)) continue;
                ClaimMatch(component, finding);
                Type targetType;
                try
                {
                    if (!TryPrepareComponent(component, tmpType, targetGuid, filePath, finding,
                            out targetType)) continue;
                }
                catch (Exception ex)
                {
                    migrationState.SetFailed(finding, new List<ManualReview>
                    {
                        CreateReview(ManualReviewKind.MigrationFailure, finding, filePath, tmpType,
                            HierarchyPath(component.transform), ex.Message,
                            "Resolve the reported component preflight failure, then re-check this finding."),
                    });
                    throw;
                }
                prepared.Add((component, tmpType, targetType, finding));
            }

            var subMeshes = new HashSet<GameObject>();
            int count = 0;
            for (int i = 0; i < inputPlans.Count; i++)
            {
                var plan = inputPlans[i];
                var ownedSubMeshes = new HashSet<GameObject>();
                CollectSubMeshObjects(plan.text.gameObject, plan.text, ownedSubMeshes);
                if (plan.placeholder != null)
                    CollectSubMeshObjects(plan.placeholder.gameObject, plan.placeholder,
                        ownedSubMeshes);
                count += MigrateInputField(plan, completed, createdSpriteAssets);
                subMeshes.UnionWith(ownedSubMeshes);
            }
            foreach (var (component, tmpType, targetType, finding) in prepared)
            {
                var ownedSubMeshes = new HashSet<GameObject>();
                CollectSubMeshObjects(component.gameObject, component, ownedSubMeshes);
                if (!MigrateComponent(component, tmpType, targetType, finding, completed,
                        createdSpriteAssets)) continue;
                subMeshes.UnionWith(ownedSubMeshes);
                count++;
            }

            if (count > 0) CleanUpSubMeshes(subMeshes);

            return count;

            bool IsAllowed(MigrationFinding finding)
                => allowedFindingIds == null || allowedFindingIds.Contains(finding.id);

            void ClaimMatch(Component component, MigrationFinding finding)
            {
                if (matchedFindings.Add(finding)) return;
                var path = HierarchyPath(component.transform);
                var reason = $"More than one loaded component matches finding '{finding.id}' in " +
                             $"'{filePath}', including '{path}'.";
                migrationState.SetFailed(finding, new List<ManualReview>
                {
                    CreateReview(ManualReviewKind.MigrationFailure, finding, filePath,
                        SourceTypeName(finding.scriptGuid), path, reason,
                        "Resolve the ambiguous serialized component identity, then re-check this finding."),
                });
                throw new InvalidOperationException(reason);
            }
        }

        /// <summary>
        /// Which text and placeholder components the loaded TMP input fields own. An owned
        /// component is never replaced on its own — only its field may replace it, because the
        /// field keeps a serialized reference to it.
        /// </summary>
        readonly struct InputFieldOwnership
        {
            public InputFieldOwnership(Dictionary<Component, Component> ownerOf,
                Dictionary<Component, int> ownerCount)
            {
                OwnerOf = ownerOf;
                OwnerCount = ownerCount;
            }

            /// <summary>The field claiming each owned component; the last one when several do.</summary>
            public readonly Dictionary<Component, Component> OwnerOf;

            /// <summary>How many fields claim each owned component. Anything but one blocks.</summary>
            public readonly Dictionary<Component, int> OwnerCount;
        }

        InputFieldOwnership CaptureOwnership(
            List<(Component component, Type tmpType, string targetGuid)> tmpComponents)
        {
            var ownerOf = new Dictionary<Component, Component>();
            var ownerCount = new Dictionary<Component, int>();
            for (int i = 0; i < tmpComponents.Count; i++)
            {
                var candidate = tmpComponents[i];
                if (candidate.tmpType != tmpInputFieldType) continue;
                var input = new SerializedObject(candidate.component);
                Claim(candidate.component,
                    input.FindProperty("m_TextComponent")?.objectReferenceValue as Component);
                Claim(candidate.component,
                    input.FindProperty("m_Placeholder")?.objectReferenceValue as Component);
            }
            return new InputFieldOwnership(ownerOf, ownerCount);

            void Claim(Component owner, Component owned)
            {
                if (owned == null) return;
                ownerOf[owned] = owner;
                ownerCount.TryGetValue(owned, out var owners);
                ownerCount[owned] = owners + 1;
            }
        }

        /// <summary>
        /// Pairs a loaded component with the finding the scan raised for it. A component of a
        /// nested prefab instance answers false without a word: it belongs to the source prefab,
        /// whose own finding migrates it there.
        /// </summary>
        bool TryMatchFinding(Component component, Type tmpType, string filePath,
            List<MigrationFinding> findings, out MigrationFinding finding)
        {
            if (TryFindFinding(component, tmpType, filePath, findings, out finding,
                    out var identity)) return true;
            if (identity == ComponentIdentityState.Foreign) return false;
            Log(LogSeverity.Warning,
                $"Skipped {TypeName(tmpType)} '{HierarchyPath(component.transform)}' in " +
                $"'{filePath}': the last scan has no finding for it. Re-scan the project so it " +
                "is covered.");
            return false;
        }

        /// <summary>
        /// Accounts for the text and placeholder components of every input field this pass did not
        /// plan. They are excluded from the independent pass because only their field may replace
        /// them, so without a verdict here nothing in the tool could ever move them.
        /// </summary>
        void ResolveUnplannedOwned(
            List<(Component component, Type tmpType, string targetGuid)> tmpComponents,
            InputFieldOwnership ownership, string filePath, List<MigrationFinding> findings,
            List<InputFieldPlan> plans, HashSet<string> allowedFindingIds)
        {
            var planned = new HashSet<Component>();
            for (int i = 0; i < plans.Count; i++)
            {
                planned.Add(plans[i].text);
                if (plans[i].placeholder != null) planned.Add(plans[i].placeholder);
            }

            for (int i = 0; i < tmpComponents.Count; i++)
            {
                var component = tmpComponents[i].component;
                if (planned.Contains(component) ||
                    !ownership.OwnerOf.TryGetValue(component, out var owner)) continue;
                if (!TryFindFinding(component, tmpComponents[i].tmpType, filePath, findings,
                        out var finding) ||
                    finding.status != MigrationStatus.NotStarted) continue;

                var hasOwnerFinding = TryFindFinding(owner, tmpInputFieldType, filePath, findings,
                    out var ownerFinding);
                if (hasOwnerFinding && allowedFindingIds != null &&
                    !allowedFindingIds.Contains(ownerFinding.id)) continue;

                var childPath = HierarchyPath(component.transform);
                var ownerPath = HierarchyPath(owner.transform);
                if (hasOwnerFinding && ownerFinding.status == MigrationStatus.Skipped)
                {
                    migrationState.SetStatus(finding, MigrationStatus.Skipped);
                    Log(LogSeverity.Info,
                        $"Skipped '{childPath}' in '{filePath}': TMP_InputField '{ownerPath}' " +
                        "owns it and was skipped.");
                    continue;
                }

                var reason = hasOwnerFinding
                    ? OwnerBlockReason(ownerFinding)
                    : "the last scan has no finding for that field.";
                migrationState.SetFailed(finding, new List<ManualReview>
                {
                    CreateReview(ManualReviewKind.UnsupportedComponent, finding, filePath,
                        tmpComponents[i].tmpType, childPath,
                        $"TMP_InputField '{ownerPath}' owns it: {reason}",
                        "Resolve the owning TMP_InputField's blocker, then re-check that field."),
                });
                Log(LogSeverity.Warning,
                    $"Skipped '{childPath}' in '{filePath}': only TMP_InputField '{ownerPath}' " +
                    "can replace it, and that field did not migrate.");
            }
        }

        void CollectTmpComponents(GameObject go, List<(Component, Type, string)> result)
        {
            if (tmpTextUiType != null)
            {
                var comp = go.GetComponent(tmpTextUiType);
                if (comp != null)
                    result.Add((comp, tmpTextUiType, MigrationMapping.UniTextGuid));
            }
            if (tmpText3DType != null)
            {
                var comp = go.GetComponent(tmpText3DType);
                if (comp != null)
                    result.Add((comp, tmpText3DType, MigrationMapping.UniTextWorldGuid));
            }
            if (tmpInputFieldType != null)
            {
                var comp = go.GetComponent(tmpInputFieldType);
                if (comp != null)
                    result.Add((comp, tmpInputFieldType, MigrationMapping.UniTextEditableGuid));
            }

            for (int i = 0; i < go.transform.childCount; i++)
                CollectTmpComponents(go.transform.GetChild(i).gameObject, result);
        }

        bool TryFindFinding(Component component, Type tmpType, string filePath,
            List<MigrationFinding> findings, out MigrationFinding match)
            => TryFindFinding(component, tmpType, filePath, findings, out match, out _);

        bool TryFindFinding(Component component, Type tmpType, string filePath,
            List<MigrationFinding> findings, out MigrationFinding match,
            out ComponentIdentityState identity)
        {
            var sourceGuid = SourceGuid(tmpType);
            var assetGuid = AssetDatabase.AssetPathToGUID(filePath);
            identity = ComponentIdentity(component, assetGuid, out var fileId);
            var objectPath = HierarchyPath(component.transform);
            match = null;
            if (identity == ComponentIdentityState.Foreign) return false;
            for (int i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.filePath != filePath || finding.type != FindingType.Component ||
                    finding.scriptGuid != sourceGuid) continue;
                var matches = identity == ComponentIdentityState.Owned
                    ? finding.fileID == fileId
                    : finding.objectPath == component.gameObject.name;
                if (!matches) continue;
                if (match != null)
                    throw new InvalidOperationException(
                        $"More than one migration finding identifies '{objectPath}' in '{filePath}'.");
                match = finding;
            }

            return match != null;
        }

        string SourceGuid(Type tmpType)
        {
            if (tmpType == tmpTextUiType) return MigrationMapping.TmpTextUiGuid;
            if (tmpType == tmpText3DType) return MigrationMapping.TmpText3DGuid;
            if (tmpType == tmpInputFieldType) return MigrationMapping.TmpInputFieldGuid;
            throw new InvalidOperationException($"Unsupported TMP component type '{TypeName(tmpType)}'.");
        }

        Type SourceType(string scriptGuid)
        {
            if (scriptGuid == MigrationMapping.TmpTextUiGuid) return tmpTextUiType;
            if (scriptGuid == MigrationMapping.TmpText3DGuid) return tmpText3DType;
            if (scriptGuid == MigrationMapping.TmpInputFieldGuid) return tmpInputFieldType;
            return null;
        }

        static string SourceTypeName(string scriptGuid)
        {
            if (scriptGuid == MigrationMapping.TmpTextUiGuid) return "TMPro.TextMeshProUGUI";
            if (scriptGuid == MigrationMapping.TmpText3DGuid) return "TMPro.TextMeshPro";
            if (scriptGuid == MigrationMapping.TmpInputFieldGuid) return "TMPro.TMP_InputField";
            return "Unknown TMP component";
        }

        static ComponentIdentityState ComponentIdentity(Component component, string assetGuid,
            out string fileId)
        {
            fileId = null;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(component);
            if (id.identifierType == 0 || id.targetObjectId == 0)
                return PrefabUtility.IsPartOfPrefabInstance(component)
                    ? ComponentIdentityState.Foreign
                    : ComponentIdentityState.Unavailable;
            if (!string.Equals(id.assetGUID.ToString(), assetGuid,
                    StringComparison.OrdinalIgnoreCase) ||
                id.targetPrefabId != 0 &&
                PrefabUtility.GetCorrespondingObjectFromSource(component) != null)
                return ComponentIdentityState.Foreign;
            fileId = unchecked((long)id.targetObjectId).ToString(CultureInfo.InvariantCulture);
            return ComponentIdentityState.Owned;
        }

        static string HierarchyPath(Transform transform)
        {
            var parts = new List<string>();
            while (transform != null)
            {
                parts.Add(transform.name);
                transform = transform.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string TypeName(Type type) => type.FullName ?? type.Name;

        static ManualReview CreateReview(ManualReviewKind kind, MigrationFinding finding,
            string filePath, Type sourceType, string objectPath, string reason, string action,
            Type dependentType = null, Type requiredType = null)
        {
            return CreateReview(kind, finding, filePath, TypeName(sourceType), objectPath, reason,
                action, dependentType, requiredType);
        }

        static ManualReview CreateReview(ManualReviewKind kind, MigrationFinding finding,
            string filePath, string sourceType, string objectPath, string reason, string action,
            Type dependentType = null, Type requiredType = null)
        {
            return new ManualReview
            {
                kind = kind,
                assetGuid = AssetDatabase.AssetPathToGUID(filePath),
                assetPath = filePath,
                targetFileID = finding.fileID,
                sourceType = sourceType,
                objectPath = objectPath,
                dependentType = dependentType == null ? null : TypeName(dependentType),
                requiredType = requiredType == null ? null : TypeName(requiredType),
                reason = reason,
                action = action,
            };
        }

        bool TryPrepareInputField(Component source, string filePath,
            List<MigrationFinding> findings, MigrationFinding finding,
            Dictionary<Component, int> ownershipCount,
            IReadOnlyList<ReferenceMigrator.InputFieldSource> inputSources,
            out InputFieldPlan plan)
        {
            plan = null;
            if (!TryReadInputField(source, out var candidate, out var reason))
                return BlockInputField(source, filePath, finding, reason);

            if (!long.TryParse(finding.fileID, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var inputId))
                return BlockInputField(source, filePath, finding,
                    "The input field has no stable serialized component identity.");
            ReferenceMigrator.InputFieldSource serializedSource = default;
            var sourceMatches = 0;
            for (int i = 0; i < inputSources.Count; i++)
            {
                if (inputSources[i].InputId != inputId) continue;
                serializedSource = inputSources[i];
                sourceMatches++;
            }
            if (sourceMatches != 1 || serializedSource.TextComponentId == 0 ||
                serializedSource.TextOwnerGameObjectId == 0)
                return BlockInputField(source, filePath, finding,
                    "The field's serialized text endpoint is missing or ambiguous.");

            if (!ownershipCount.TryGetValue(candidate.text, out var textOwners) || textOwners != 1)
                return BlockInputField(source, filePath, finding,
                    "The text component is shared by more than one TMP_InputField.");
            if (candidate.placeholder != null &&
                (!ownershipCount.TryGetValue(candidate.placeholder, out var placeholderOwners) ||
                 placeholderOwners != 1))
                return BlockInputField(source, filePath, finding,
                    "The placeholder component is shared by more than one TMP_InputField.");

            if (!TryFindFinding(candidate.text, tmpTextUiType, filePath, findings,
                    out candidate.textFinding) || !IsUntouched(candidate.textFinding.status))
                return BlockInputField(source, filePath, finding,
                    "The owned text component has no pending migration finding in this asset.");
            if (candidate.placeholder != null &&
                (!TryFindFinding(candidate.placeholder, tmpTextUiType, filePath, findings,
                     out candidate.placeholderFinding) ||
                 !IsUntouched(candidate.placeholderFinding.status)))
                return BlockInputField(source, filePath, finding,
                    "The owned placeholder component has no pending migration finding in this asset.");
            if (!long.TryParse(candidate.textFinding.fileID, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var textId) ||
                textId != serializedSource.TextComponentId ||
                candidate.placeholder != null &&
                (!long.TryParse(candidate.placeholderFinding.fileID, NumberStyles.Integer,
                     CultureInfo.InvariantCulture, out var placeholderId) ||
                 placeholderId != serializedSource.PlaceholderId) ||
                candidate.placeholder == null && serializedSource.PlaceholderId != 0)
                return BlockInputField(source, filePath, finding,
                    "The loaded field roles do not match their serialized component identities.");

            if (filePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var ids = new HashSet<long>
                {
                    serializedSource.InputId,
                    serializedSource.TextComponentId,
                };
                if (serializedSource.PlaceholderId != 0) ids.Add(serializedSource.PlaceholderId);
                projectYamlFiles ??= MigrationScope.Collect(migrationState.excludedPaths);
                var modifications = ReferenceMigrator.FindPrefabModifications(projectYamlFiles,
                    ReferenceMigrator.GuidOf(filePath), ids, out var unreadable);
                if (modifications.Count != 0)
                {
                    var modification = modifications[0];
                    return BlockInputField(source, filePath, finding,
                        $"Prefab override '{modification.PropertyPath}' in " +
                        $"'{modification.AssetPath}' targets one of the field's replaced components.");
                }
                if (unreadable.Count != 0)
                    return BlockInputField(source, filePath, finding,
                        $"{unreadable[0]}{MoreEntries(unreadable.Count)}. An override on this field cannot " +
                        "be ruled out while an asset that could hold one is unread — exclude that " +
                        "asset in Settings to migrate this field without it.");
            }

            var textBlockers = UnsatisfiedBlockers(candidate.text, typeof(UniText));
            if (textBlockers.Count != 0)
                return BlockInputField(source, filePath, finding,
                    $"{TypeName(textBlockers[0].dependent.GetType())} requires the owned TMP text " +
                    "component.");
            if (candidate.placeholder != null)
            {
                var placeholderBlockers = UnsatisfiedBlockers(candidate.placeholder, typeof(UniText));
                if (placeholderBlockers.Count != 0)
                    return BlockInputField(source, filePath, finding,
                        $"{TypeName(placeholderBlockers[0].dependent.GetType())} requires the TMP " +
                        "placeholder component.");
            }

            if (!TryProcessSpriteMarkup(new SerializedObject(candidate.text), tmpTextUiType,
                    false, out _, out var spriteError, candidate.textValue))
            {
                ReportBlockedFinding(candidate.textFinding, tmpTextUiType,
                    candidate.text.transform, spriteError);
                return BlockInputField(source, filePath, finding,
                    $"The owned text component cannot migrate its TMP sprites: {spriteError}");
            }
            if (candidate.placeholder != null &&
                !TryProcessSpriteMarkup(new SerializedObject(candidate.placeholder),
                    tmpTextUiType, false, out _, out spriteError))
            {
                ReportBlockedFinding(candidate.placeholderFinding, tmpTextUiType,
                    candidate.placeholder.transform, spriteError);
                return BlockInputField(source, filePath, finding,
                    $"The placeholder component cannot migrate its TMP sprites: {spriteError}");
            }

            candidate.finding = finding;
            plan = candidate;
            return true;
        }

        /// <summary>
        /// Whether a finding's component is still the TMP original. Failed counts: a refusal
        /// records why nothing was written, and an owned component is refused for its field's
        /// reasons rather than its own.
        /// </summary>
        static bool IsUntouched(MigrationStatus status)
            => status is MigrationStatus.NotStarted or MigrationStatus.Failed;

        /// <summary>
        /// Writes what a migrated component could not take with it, once it has actually migrated.
        /// Held until then so a rolled-back file leaves no note of a loss that did not happen.
        /// </summary>
        void RecordLosses(List<LostSetting> losses, string filePath)
        {
            if (losses == null || losses.Count == 0) return;
            for (var i = 0; i < losses.Count; i++)
            {
                losses[i].assetPath = filePath;
                Log(LogSeverity.Warning,
                    $"  Not carried over — {losses[i].setting}: {losses[i].value}" +
                    (losses[i].advice == null ? string.Empty : $" ({losses[i].advice})"));
            }
            migrationLosses.AddLost(losses);
        }

        /// <summary>Notes one TMP setting the migrated field cannot carry, with the value it had.</summary>
        static void Lose(List<LostSetting> losses, string objectPath, string setting, string value,
            string advice)
        {
            losses.Add(new LostSetting
            {
                objectPath = objectPath,
                componentType = "TMP_InputField",
                setting = setting,
                value = value,
                advice = advice,
                lostAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        }

        bool TryReadInputField(Component source, out InputFieldPlan plan, out string reason)
        {
            plan = null;
            if (!TryGetInputFieldRoles(source, out var text, out var placeholder, out var viewport,
                    out reason)) return false;
            if (text.GetType() != tmpTextUiType)
            {
                reason = "The assigned text component is not a TextMeshProUGUI component.";
                return false;
            }
            if (placeholder != null && placeholder.GetType() != tmpTextUiType)
            {
                reason = "The assigned placeholder is not a TextMeshProUGUI component.";
                return false;
            }
            if (ReferenceEquals(text, placeholder))
            {
                reason = "The text and placeholder roles point at the same component.";
                return false;
            }
            if (text.transform.parent != viewport)
            {
                reason = "The editable text is not a direct child of its TMP viewport.";
                return false;
            }
            if (placeholder != null && placeholder.transform.parent != viewport)
            {
                reason = "The placeholder is not a direct child of the TMP viewport.";
                return false;
            }
            if (!viewport.IsChildOf(source.transform) || viewport.GetComponent<RectMask2D>() == null)
            {
                reason = "The assigned viewport is not a masked descendant of the field box.";
                return false;
            }
            if (text.GetComponent<UniTextBase>() != null ||
                text.GetComponent<UniTextSelectable>() != null ||
                text.GetComponent<UniTextEditable>() != null ||
                placeholder != null && placeholder.GetComponent<UniTextBase>() != null)
            {
                reason = "The field contains both TMP and UniText components on one of its owned objects.";
                return false;
            }

            var input = new SerializedObject(source);
            var textValue = input.FindProperty("m_Text");
            var characterLimit = input.FindProperty("m_CharacterLimit");
            var inputType = input.FindProperty("m_InputType");
            var validation = input.FindProperty("m_CharacterValidation");
            var keyboard = input.FindProperty("m_KeyboardType");
            var lineType = input.FindProperty("m_LineType");
            var maskCharacter = input.FindProperty("m_AsteriskChar");
            var readOnly = input.FindProperty("m_ReadOnly");
            var onFocusSelectAll = input.FindProperty("m_OnFocusSelectAll");
            var restoreOnEscape = input.FindProperty("m_RestoreOriginalTextOnEscape");
            var hideMobileInput = input.FindProperty("m_HideMobileInput");
            var hideSoftKeyboard = input.FindProperty("m_HideSoftKeyboard");
            var lineLimit = input.FindProperty("m_LineLimit");
            var verticalScrollbar = input.FindProperty("m_VerticalScrollbar");
            var scrollSensitivity = input.FindProperty("m_ScrollSensitivity");
            var resetOnDeactivation = input.FindProperty("m_ResetOnDeActivation");
            var keepSelectionVisible = input.FindProperty("m_KeepTextSelectionVisible");
            var richText = input.FindProperty("m_RichText");
            var richTextEditing = input.FindProperty("m_isRichTextEditingAllowed");
            var activateOnSelect = input.FindProperty("m_ShouldActivateOnSelect");
            var customCaretColor = input.FindProperty("m_CustomCaretColor");
            var caretBlinkRate = input.FindProperty("m_CaretBlinkRate");
            var caretWidth = input.FindProperty("m_CaretWidth");
            var selectionColor = input.FindProperty("m_SelectionColor");
            var inputValidator = input.FindProperty("m_InputValidator");
            if (textValue == null || characterLimit == null || inputType == null ||
                validation == null || keyboard == null || lineType == null ||
                maskCharacter == null || readOnly == null || onFocusSelectAll == null ||
                restoreOnEscape == null || hideMobileInput == null || hideSoftKeyboard == null ||
                lineLimit == null || verticalScrollbar == null || scrollSensitivity == null ||
                resetOnDeactivation == null || keepSelectionVisible == null || richText == null ||
                richTextEditing == null || activateOnSelect == null || customCaretColor == null ||
                caretBlinkRate == null || caretWidth == null || selectionColor == null ||
                inputValidator == null)
            {
                reason = UnsupportedInputLayout;
                return false;
            }

            var losses = new List<LostSetting>();
            var where = HierarchyPath(source.transform);

            if (HasUnsupportedInputEvent(input, out var eventReason))
            {
                if (eventReason == UnsupportedInputLayout)
                {
                    reason = eventReason;
                    return false;
                }
                Lose(losses, where, "Event listeners", eventReason,
                    "Subscribe to the matching UniTextEditable C# event from code.");
            }

            var validateCallback = tmpInputFieldType.GetField("m_OnValidateInput",
                BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(source);
            if (validateCallback != null || inputValidator.objectReferenceValue != null)
                Lose(losses, where, "Input validator",
                    inputValidator.objectReferenceValue != null
                        ? inputValidator.objectReferenceValue.name
                        : "onValidateInput callback",
                    "Add an InputFilter behavior to UniTextEditable.");

            if (validation.intValue is < 0 or > 2)
                Lose(losses, where, "Character Validation", validation.intValue.ToString(),
                    "Add an InputFilter behavior; only None, Digit and Integer convert on their own.");

            if (inputType.intValue is < 0 or > 2)
                Lose(losses, where, "Input Type", inputType.intValue.ToString(),
                    "The value is outside what TMP defines; the field migrates as Standard.");

            if (lineType.intValue is < 0 or > 2 ||
                inputType.intValue == 2 && lineType.intValue != 0)
                Lose(losses, where, "Line Type",
                    $"{lineType.intValue} with Input Type {inputType.intValue}",
                    "A password field is single-line in UniText; check the newline policy.");

            if (!TryMapKeyboardType(keyboard.intValue, out _))
                Lose(losses, where, "Keyboard Type", keyboard.intValue.ToString(),
                    "No cross-platform UniText equivalent; the field uses the default keyboard.");

            if (hideSoftKeyboard.boolValue)
                Lose(losses, where, "Hide Soft Keyboard", "on", null);
            if (lineLimit.intValue != 0)
                Lose(losses, where, "Line Limit", lineLimit.intValue.ToString(), null);
            if (verticalScrollbar.objectReferenceValue != null)
                Lose(losses, where, "Vertical Scrollbar",
                    verticalScrollbar.objectReferenceValue.name, null);
            if (!Mathf.Approximately(scrollSensitivity.floatValue, 1f))
                Lose(losses, where, "Scroll Sensitivity",
                    scrollSensitivity.floatValue.ToString(CultureInfo.InvariantCulture), null);
            if (!resetOnDeactivation.boolValue)
                Lose(losses, where, "Reset On Deactivation", "off", null);
            if (keepSelectionVisible.boolValue)
                Lose(losses, where, "Keep Text Selection Visible", "on", null);
            if (!richText.boolValue)
                Lose(losses, where, "Rich Text", "off",
                    "UniText has no rich-text switch; markup in this field will render.");
            if (richTextEditing.boolValue)
                Lose(losses, where, "Allow Rich Text Editing", "on",
                    "Set the typing markup policy on UniTextEditable instead.");
            if (!activateOnSelect.boolValue)
                Lose(losses, where, "Should Activate On Select", "off", null);

            if (customCaretColor.boolValue)
                Lose(losses, where, "Custom Caret Color", "on",
                    "Set the colour on the UniTextEditable caret renderer.");
            if (!Mathf.Approximately(caretBlinkRate.floatValue, 0.85f))
                Lose(losses, where, "Caret Blink Rate",
                    caretBlinkRate.floatValue.ToString(CultureInfo.InvariantCulture),
                    "UniText blinks on one project-wide interval, in Project Settings → UniText.");
            if (caretWidth.intValue != 1)
                Lose(losses, where, "Caret Width", caretWidth.intValue.ToString(),
                    "UniText sizes the caret as a fraction of line height, not in pixels.");

            var selectable = (Selectable)source;
            if (!selectable.interactable)
                Lose(losses, where, "Interactable", "off",
                    "UniTextEditable is not a Selectable; gate focus yourself.");

            var navigation = selectable.navigation;
            if (navigation.mode != Navigation.Mode.Automatic || navigation.selectOnUp != null ||
                navigation.selectOnDown != null || navigation.selectOnLeft != null ||
                navigation.selectOnRight != null)
                Lose(losses, where, "Navigation", navigation.mode.ToString(),
                    "UniTextEditable is not a Selectable; keep navigation on a Selectable of your own.");

            if (selectable.transition != Selectable.Transition.None &&
                (selectable.transition != Selectable.Transition.ColorTint ||
                 !selectable.colors.Equals(ColorBlock.defaultColorBlock) ||
                 selectable.targetGraphic != null &&
                 selectable.targetGraphic.gameObject != source.gameObject))
                Lose(losses, where, "Transition", selectable.transition.ToString(),
                    "UniTextEditable is not a Selectable; keep the transition on a Selectable of your own.");

            if (!TryGetInputTemplate(out var templateSelectable, out var templateEditable,
                    out _, out reason)) return false;

            plan = new InputFieldPlan
            {
                source = source,
                text = text,
                placeholder = placeholder,

                templateSelectable = templateSelectable,
                templateEditable = templateEditable,

                textValue = textValue.stringValue ?? string.Empty,
                characterLimit = characterLimit.intValue,
                inputType = inputType.intValue,
                characterValidation = validation.intValue,
                keyboardType = keyboard.intValue,
                lineType = lineType.intValue,
                maskCharacter = (char)maskCharacter.intValue,
                readOnly = readOnly.boolValue,
                onFocusSelectAll = onFocusSelectAll.boolValue,
                restoreOriginalTextOnEscape = restoreOnEscape.boolValue,
                hideMobileInput = hideMobileInput.boolValue,
                enabled = source is Behaviour behaviour && behaviour.enabled,
                selectionColor = selectionColor.colorValue,
                losses = losses,
            };
            reason = null;
            return true;
        }

        bool TryGetInputFieldRoles(Component source, out Component text, out Component placeholder,
            out RectTransform viewport, out string reason)
        {
            var input = new SerializedObject(source);
            var textProperty = input.FindProperty("m_TextComponent");
            var placeholderProperty = input.FindProperty("m_Placeholder");
            var viewportProperty = input.FindProperty("m_TextViewport");
            text = textProperty?.objectReferenceValue as Component;
            placeholder = placeholderProperty?.objectReferenceValue as Component;
            viewport = viewportProperty?.objectReferenceValue as RectTransform;
            if (textProperty == null || placeholderProperty == null || viewportProperty == null)
            {
                reason = "The loaded TMP_InputField role layout is not supported.";
                return false;
            }
            if (text == null || viewport == null)
            {
                reason = "The field has no assigned text component or viewport.";
                return false;
            }
            reason = null;
            return true;
        }

        bool TryGetInputTemplate(out UniTextSelectable selectable, out UniTextEditable editable,
            out UniTextBase placeholder, out string reason)
        {
            selectable = null;
            editable = null;
            placeholder = null;
            var prefab = UniTextSettings.InputFieldPrefab;
            if (prefab == null)
            {
                reason = "UniText Settings has no Input Field Prefab.";
                return false;
            }

            var editables = prefab.GetComponentsInChildren<UniTextEditable>(true);
            if (editables.Length != 1)
            {
                reason = "The configured Input Field Prefab must contain exactly one UniTextEditable.";
                return false;
            }
            editable = editables[0];
            selectable = editable.GetComponent<UniTextSelectable>();
            if (selectable == null || editable.GetComponent<UniText>() == null)
            {
                reason = "The configured editable object must carry UniText and UniTextSelectable.";
                return false;
            }

            var placeholderCount = 0;
            var placeholderIndex = -1;
            for (int i = 0; i < editable.Behaviors.Count; i++)
            {
                if (editable.Behaviors[i] is not PlaceholderDecorator decorator) continue;
                placeholder = decorator.Target;
                placeholderIndex = i;
                placeholderCount++;
            }
            if (placeholderCount > 1)
            {
                reason = "The configured Input Field Prefab has more than one placeholder policy.";
                return false;
            }
            for (int presetIndex = 0; presetIndex < editable.BehaviorPresets.Count; presetIndex++)
            {
                var preset = editable.BehaviorPresets[presetIndex];
                if (preset == null) continue;
                for (int behaviorIndex = 0; behaviorIndex < preset.Behaviors.Count; behaviorIndex++)
                {
                    if (!IsInputSourcePolicy(preset.Behaviors[behaviorIndex])) continue;
                    reason = $"Behavior preset '{preset.name}' owns a policy also serialized by " +
                             "TMP_InputField.";
                    return false;
                }
            }

            var templatePath = AssetDatabase.GetAssetPath(prefab);
            var placeholderPath = placeholderIndex >= 0
                ? $"behaviors.items.Array.data[{placeholderIndex}].target"
                : null;
            if (HasUnsupportedTemplateReference(selectable, templatePath, null, null, out reason) ||
                HasUnsupportedTemplateReference(editable, templatePath, placeholder,
                    placeholderPath, out reason))
                return false;
            reason = null;
            return true;
        }

        static bool HasUnsupportedTemplateReference(Component component, string templatePath,
            UnityEngine.Object allowed, string allowedPath, out string reason)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference ||
                        property.propertyPath is "m_GameObject" or "m_CorrespondingSourceObject" or
                            "m_PrefabInstance" or "m_PrefabAsset" or "m_Script") continue;
                    var value = property.objectReferenceValue;
                    if (value == null || AssetDatabase.GetAssetPath(value) != templatePath) continue;
                    if (value == allowed && property.propertyPath == allowedPath) continue;
                    reason = $"The configured Input Field Prefab contains an unsupported local " +
                             $"reference at '{property.propertyPath}'.";
                    return true;
                } while (property.NextVisible(true));
            }
            reason = null;
            return false;
        }

        const string UnsupportedInputLayout =
            "The loaded TMP_InputField serialized layout is not supported.";

        /// <summary>
        /// Whether any TMP input event carries listeners the migration cannot move, with the
        /// reason to record. An event whose serialized property is absent counts: the layout is
        /// one this tool does not know, and a renamed field could carry listeners unseen.
        /// </summary>
        static bool HasUnsupportedInputEvent(SerializedObject input, out string reason)
        {
            string[] names =
            {
                "m_OnEndEdit", "m_OnSubmit", "m_OnSelect", "m_OnDeselect",
                "m_OnTextSelection", "m_OnEndTextSelection", "m_OnValueChanged",
                "m_OnTouchScreenKeyboardStatusChanged",
            };
            for (int i = 0; i < names.Length; i++)
            {
                var calls = input.FindProperty(names[i] + ".m_PersistentCalls.m_Calls");
                if (calls == null)
                {
                    reason = UnsupportedInputLayout;
                    return true;
                }
                if (calls.arraySize == 0) continue;
                reason = $"{names[i]} has persistent UnityEvent listeners.";
                return true;
            }
            reason = null;
            return false;
        }

        static bool TryMapKeyboardType(int source, out KeyboardType target)
        {
            switch (source)
            {
                case 0: target = KeyboardType.Default; return true;
                case 1: target = KeyboardType.ASCIICapable; return true;
                case 2: target = KeyboardType.NumbersAndPunctuation; return true;
                case 3: target = KeyboardType.URL; return true;
                case 4: target = KeyboardType.NumberPad; return true;
                case 5: target = KeyboardType.PhonePad; return true;
                case 7: target = KeyboardType.EmailAddress; return true;
                case 10: target = KeyboardType.WebSearch; return true;
                case 11: target = KeyboardType.DecimalPad; return true;
                default: target = default; return false;
            }
        }

        bool BlockInputField(Component source, string filePath, MigrationFinding finding,
            string reason)
        {
            var path = HierarchyPath(source.transform);
            migrationState.SetFailed(finding, new List<ManualReview>
            {
                CreateReview(ManualReviewKind.UnsupportedComponent, finding, filePath,
                    tmpInputFieldType, path, reason,
                    "Resolve the reported field composition or policy, then re-check this finding."),
            });
            Log(LogSeverity.Warning,
                $"Skipped TMP_InputField '{path}' in '{filePath}': {reason}");
            return false;
        }

        /// <summary>
        /// Gates one standalone component. A TMP_InputField is a composite and belongs to
        /// <see cref="TryPrepareInputField"/>, which alone checks the identities and the
        /// components it owns.
        /// </summary>
        bool TryPrepareComponent(Component tmpComponent, Type tmpType, string targetGuid,
            string filePath, MigrationFinding finding, out Type targetType)
        {
            targetType = TargetType(targetGuid);
            if (targetType == null)
                throw new InvalidOperationException(
                    $"No component type is registered for migration target GUID '{targetGuid}'.");

            if (TryProcessSpriteMarkup(new SerializedObject(tmpComponent), tmpType,
                    false, out _, out var spriteError)) return true;
            ReportBlockedFinding(finding, tmpType, tmpComponent.transform, spriteError);
            return false;
        }

        /// <summary>
        /// Takes off the components whose declared requirement the replacement can never meet —
        /// a decorator naming <c>TMP_Text</c> is not satisfied by anything in UniText — together
        /// with whatever requires those in turn. Each one's serialized values are written to
        /// <see cref="MigrationLossesData"/> first: the component cannot come back, so the
        /// record is all that is left of how it was configured.
        /// </summary>
        /// <remarks>
        /// Order is what makes it possible: Unity refuses to destroy a component another one still
        /// requires, so the closure comes off leaves first. A component that refuses anyway leaves
        /// the GameObject exactly as it was.
        /// </remarks>
        bool TryRemoveUnsatisfiable(Component source, Type targetType, string filePath,
            out string reason)
        {
            reason = null;
            var blockers = UnsatisfiedBlockers(source, targetType);
            if (blockers.Count == 0) return true;

            var go = source.gameObject;
            var pending = new List<Component>();
            for (var i = 0; i < blockers.Count; i++)
                if (!pending.Contains(blockers[i].dependent)) pending.Add(blockers[i].dependent);
            ExpandRemovalClosure(go, source, pending);

            var undo = new List<DetachedComponent>();
            var records = new List<RemovedComponent>();
            while (pending.Count > 0)
            {
                var next = NextRemovable(go, source, pending);
                if (next < 0)
                {
                    reason = $"{TypeName(pending[0].GetType())} cannot be taken off " +
                             $"'{HierarchyPath(go.transform)}': its own dependents refuse to go " +
                             "first, so the TMP component underneath cannot be replaced.";
                    undo.Sort(static (a, b) => a.Order.CompareTo(b.Order));
                    RestoreDetached(go, undo);
                    return false;
                }

                var dependent = pending[next];
                pending.RemoveAt(next);
                var record = DescribeRemoval(dependent, source, blockers, filePath);
                var name = record.componentType;

                undo.Add(new DetachedComponent(dependent.GetType(), record.state,
                    Array.IndexOf(go.GetComponents<Component>(), dependent)));
                DestroyObject(dependent);
                if (dependent != null)
                {
                    reason = $"Unity refused to take {name} off '{HierarchyPath(go.transform)}'.";
                    undo.RemoveAt(undo.Count - 1);
                    undo.Sort(static (a, b) => a.Order.CompareTo(b.Order));
                    RestoreDetached(go, undo);
                    return false;
                }

                records.Add(record);
                Log(LogSeverity.Warning,
                    $"  Removed {name} from '{record.objectPath}': {record.reason} Its settings " +
                    "are kept in the Removed tab.");
            }

            migrationLosses.AddRemoved(records);
            return true;
        }

        /// <summary>
        /// Grows <paramref name="pending"/> until it holds everything that would still require a
        /// member of it after the removal.
        /// </summary>
        void ExpandRemovalClosure(GameObject go, Component source, List<Component> pending)
        {
            var components = go.GetComponents<Component>();
            bool grew;
            do
            {
                grew = false;
                for (var i = 0; i < components.Length; i++)
                {
                    var candidate = components[i];
                    if (candidate == null || candidate == source ||
                        pending.Contains(candidate)) continue;
                    if (!RequiresAnyOf(components, candidate, pending, source)) continue;
                    pending.Add(candidate);
                    grew = true;
                }
            } while (grew);
        }

        /// <summary>
        /// Index of a member of <paramref name="pending"/> that nothing surviving still requires,
        /// or -1 when every one of them is held by another.
        /// </summary>
        int NextRemovable(GameObject go, Component source, List<Component> pending)
        {
            var components = go.GetComponents<Component>();
            var one = new List<Component>(1);
            for (var i = 0; i < pending.Count; i++)
            {
                one.Clear();
                one.Add(pending[i]);
                var held = false;
                for (var at = 0; at < components.Length && !held; at++)
                {
                    var other = components[at];
                    if (other == null || other == pending[i]) continue;
                    held = RequiresAnyOf(components, other, one, source);
                }
                if (!held) return i;
            }
            return -1;
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> declares a requirement that only a member of
        /// <paramref name="doomed"/> — or the component being replaced — currently satisfies.
        /// </summary>
        bool RequiresAnyOf(Component[] components, Component candidate, List<Component> doomed,
            Component source)
        {
            var requirements = RequiredComponents(candidate.GetType());
            for (var i = 0; i < requirements.Length; i++)
            {
                var required = requirements[i];
                var satisfiedBySurvivor = false;
                var satisfiedByDoomed = false;
                for (var at = 0; at < components.Length; at++)
                {
                    var other = components[at];
                    if (other == null || other == candidate ||
                        !required.IsAssignableFrom(other.GetType())) continue;
                    if (doomed.Contains(other)) satisfiedByDoomed = true;
                    else if (other != source) satisfiedBySurvivor = true;
                }
                if (satisfiedByDoomed && !satisfiedBySurvivor) return true;
            }
            return false;
        }

        RemovedComponent DescribeRemoval(Component dependent, Component source,
            List<(Component dependent, Type requiredType, bool targetCompatible)> blockers,
            string filePath)
        {
            Type required = null;
            for (var i = 0; i < blockers.Count && required == null; i++)
                if (blockers[i].dependent == dependent) required = blockers[i].requiredType;

            var record = new RemovedComponent
            {
                assetPath = filePath,
                objectPath = HierarchyPath(dependent.transform),
                componentType = TypeName(dependent.GetType()),
                requiredType = required == null ? null : TypeName(required),
                reason = required == null
                    ? "It requires a component that had to be removed with the TMP text."
                    : $"It requires {TypeName(required)}, which no UniText component is.",
                state = EditorJsonUtility.ToJson(dependent, true),
                removedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };

            var components = source.gameObject.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (components[i] == null || components[i] == dependent) continue;
                if (ReferencesComponent(components[i], dependent))
                    record.referencedBy.Add(TypeName(components[i].GetType()));
            }
            return record;
        }

        /// <summary>
        /// The project script that declares a component, or null when it comes from a compiled
        /// assembly. A requirement written in source the migration also rewrites is one the user
        /// can lift in a line; one shipped as a DLL is not.
        /// </summary>
        static string DeclaringScriptPath(Component dependent)
        {
            if (dependent is not MonoBehaviour behaviour) return null;
            var script = MonoScript.FromMonoBehaviour(behaviour);
            if (script == null) return null;
            var path = AssetDatabase.GetAssetPath(script);
            return string.IsNullOrEmpty(path) ||
                   !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                ? null
                : path;
        }

        /// <summary>
        /// The dependencies the replacement genuinely breaks. A dependency the target type also
        /// satisfies is not one of them: that component is set aside for the swap and put back,
        /// which leaves its declared requirement met.
        /// </summary>
        List<(Component dependent, Type requiredType, bool targetCompatible)> UnsatisfiedBlockers(
            Component source, Type targetType)
        {
            var blockers = FindRemovalBlockers(source, targetType);
            for (var i = blockers.Count - 1; i >= 0; i--)
                if (blockers[i].targetCompatible) blockers.RemoveAt(i);
            return blockers;
        }

        /// <summary>One component taken off a GameObject so its text component could be replaced.</summary>
        readonly struct DetachedComponent
        {
            public DetachedComponent(Type type, string state, int order)
            {
                Type = type;
                State = state;
                Order = order;
            }

            public readonly Type Type;

            /// <summary>Its serialized values, in the form the editor itself writes them.</summary>
            public readonly string State;

            /// <summary>Its place among the GameObject's components, so restoring keeps their order.</summary>
            public readonly int Order;
        }

        /// <summary>
        /// Takes off the components that block the replacement only because they require a type the
        /// replacement satisfies too — a uGUI decorator declaring <c>RequireComponent(Graphic)</c>
        /// over the text it decorates. <c>Graphic</c> is <c>DisallowMultipleComponent</c>, so the
        /// new component cannot be added before the old one goes, and the decorator has to stand
        /// aside for the swap. Nothing is destroyed unless every one of them comes off; on refusal
        /// the GameObject is left exactly as it was.
        /// </summary>
        bool TryDetachCompatibleDependents(Component source, Type targetType,
            out List<DetachedComponent> detached, out string reason)
        {
            detached = new List<DetachedComponent>();
            reason = null;

            var blockers = FindRemovalBlockers(source, targetType);
            if (blockers.Count == 0) return true;

            var components = source.gameObject.GetComponents<Component>();
            var taken = new List<Component>();
            for (var i = 0; i < blockers.Count; i++)
            {
                var dependent = blockers[i].dependent;
                if (!blockers[i].targetCompatible || taken.Contains(dependent)) continue;
                taken.Add(dependent);

                if (ReferencesComponent(dependent, source))
                    Log(LogSeverity.Warning,
                        $"  {TypeName(dependent.GetType())} holds a serialized reference to the " +
                        "TMP component being replaced; that field comes back empty and needs the " +
                        "new component assigned by hand.");
                detached.Add(new DetachedComponent(dependent.GetType(),
                    EditorJsonUtility.ToJson(dependent), Array.IndexOf(components, dependent)));
            }

            for (var i = 0; i < taken.Count; i++)
            {
                var name = TypeName(taken[i].GetType());
                DestroyObject(taken[i]);
                if (taken[i] == null)
                {
                    Log(LogSeverity.Info,
                        $"  Set {name} aside to replace the text component underneath it");
                    continue;
                }

                reason = $"{name} could not be set aside — another component on this object " +
                         "requires it, so the text component underneath cannot be replaced.";
                var undo = detached.GetRange(0, i);
                detached.Clear();
                undo.Sort(static (a, b) => a.Order.CompareTo(b.Order));
                RestoreDetached(source.gameObject, undo);
                return false;
            }

            detached.Sort(static (a, b) => a.Order.CompareTo(b.Order));
            return true;
        }

        /// <summary>
        /// Whether any serialized field of <paramref name="dependent"/> points at
        /// <paramref name="target"/>. Such a field is the one thing a detach cannot restore: the
        /// object it names stops existing.
        /// </summary>
        static bool ReferencesComponent(Component dependent, Component target)
        {
            var property = new SerializedObject(dependent).GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (ReferenceEquals(property.objectReferenceValue, target)) return true;
            }
            return false;
        }

        /// <summary>
        /// Puts back what <see cref="TryDetachCompatibleDependents"/> took off, in the order it had
        /// and with the values it carried. A serialized reference to the replaced text component is
        /// the one thing that cannot return: nothing points at an object that no longer exists.
        /// </summary>
        void RestoreDetached(GameObject go, List<DetachedComponent> detached)
        {
            for (var i = 0; i < detached.Count; i++)
            {
                var name = TypeName(detached[i].Type);
                var restored = go.GetComponent(detached[i].Type);
                if (restored == null) restored = AddComponent(go, detached[i].Type);
                if (restored == null)
                    throw new InvalidOperationException(
                        $"Unity refused to put {name} back on '{HierarchyPath(go.transform)}'.");
                EditorJsonUtility.FromJsonOverwrite(detached[i].State, restored);
                EditorUtility.SetDirty(restored);
                Log(LogSeverity.Info, $"  Put {name} back with the values it had");
            }
        }

        /// <summary>
        /// The components that would lose a declared requirement if <paramref name="source"/> went,
        /// each marked with whether <paramref name="targetType"/> satisfies that requirement in its
        /// place. A null target means nothing takes its place, so no requirement survives it.
        /// </summary>
        List<(Component dependent, Type requiredType, bool targetCompatible)>
            FindRemovalBlockers(Component source, Type targetType)
        {
            var blockers = new List<(Component, Type, bool)>();
            var components = source.gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                var dependent = components[i];
                if (dependent == null || dependent == source) continue;

                var requirements = RequiredComponents(dependent.GetType());
                for (int required = 0; required < requirements.Length; required++)
                {
                    var requiredType = requirements[required];
                    if (!requiredType.IsAssignableFrom(source.GetType()) ||
                        HasOtherComponent(components, source, requiredType)) continue;

                    var duplicate = false;
                    for (int existing = 0; existing < blockers.Count; existing++)
                    {
                        if (blockers[existing].Item1 != dependent ||
                            blockers[existing].Item2 != requiredType) continue;
                        duplicate = true;
                        break;
                    }
                    if (!duplicate)
                        blockers.Add((dependent, requiredType,
                            targetType != null && requiredType.IsAssignableFrom(targetType)));
                }
            }
            return blockers;
        }

        Type[] RequiredComponents(Type componentType)
        {
            if (requiredComponentsByType.TryGetValue(componentType, out var cached)) return cached;

            var result = new List<Type>();
            for (var type = componentType;
                 type != null && type != typeof(MonoBehaviour);
                 type = type.BaseType)
            {
                var attributes = type.GetCustomAttributes(typeof(RequireComponent), false);
                for (int i = 0; i < attributes.Length; i++)
                {
                    var requirement = (RequireComponent)attributes[i];
                    Add(requirement.m_Type0);
                    Add(requirement.m_Type1);
                    Add(requirement.m_Type2);
                }
            }

            cached = result.ToArray();
            requiredComponentsByType[componentType] = cached;
            return cached;

            void Add(Type requiredType)
            {
                if (requiredType != null) result.Add(requiredType);
            }
        }

        static bool HasOtherComponent(Component[] components, Component source, Type requiredType)
        {
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component != null && component != source &&
                    requiredType.IsAssignableFrom(component.GetType())) return true;
            }
            return false;
        }

        static Type TargetType(string targetGuid)
        {
            if (targetGuid == MigrationMapping.UniTextGuid) return typeof(UniText);
            if (targetGuid == MigrationMapping.UniTextWorldGuid) return typeof(UniTextWorld);
            return null;
        }

        int MigrateInputField(InputFieldPlan plan, List<MigrationFinding> completed,
            List<string> createdSpriteAssets)
        {
            var sourceObject = plan.source.gameObject;
            var textObject = plan.text.gameObject;
            var placeholderObject = plan.placeholder != null ? plan.placeholder.gameObject : null;
            try
            {
                var textSource = new SerializedObject(plan.text);
                var sourceText = textSource.FindProperty("m_text") ??
                                 throw new InvalidOperationException(
                                     "The owned TMP text no longer exposes its serialized source.");
                RecordObject(plan.text, "Migrate TMP → UniText");
                sourceText.stringValue = plan.textValue;
                textSource.ApplyModifiedProperties();

                var migrated = 0;
                if (!MigrateComponent(plan.text, tmpTextUiType, typeof(UniText), plan.textFinding,
                        completed, createdSpriteAssets))
                    throw new InvalidOperationException("The editable text could not be migrated.");
                migrated++;

                UniText placeholder = null;
                if (plan.placeholder != null)
                {
                    if (!MigrateComponent(plan.placeholder, tmpTextUiType, typeof(UniText),
                            plan.placeholderFinding, completed, createdSpriteAssets))
                        throw new InvalidOperationException("The placeholder text could not be migrated.");
                    placeholder = placeholderObject.GetComponent<UniText>();
                    if (placeholder == null)
                        throw new InvalidOperationException(
                            "The migrated placeholder has no UniText component.");
                    migrated++;
                }

                var text = textObject.GetComponent<UniText>();
                if (text == null)
                    throw new InvalidOperationException("The migrated field has no UniText component.");
                text.WordWrap = plan.lineType != 0;

                var selectable = AddComponent<UniTextSelectable>(textObject);
                if (selectable == null)
                    throw new InvalidOperationException(
                        "Unity refused to add UniTextSelectable to the editable text.");
                selectable.enabled = false;
                CopyComponentPolicy(plan.templateSelectable, selectable);
                selectable.SelectionHighlight.Paint =
                    PaintRef.Solid((Color32)plan.selectionColor);

                var editable = AddComponent<UniTextEditable>(textObject);
                if (editable == null)
                    throw new InvalidOperationException(
                        "Unity refused to add UniTextEditable to the editable text.");
                editable.enabled = false;
                CopyComponentPolicy(plan.templateEditable, editable);

                for (int i = editable.Behaviors.Count - 1; i >= 0; i--)
                {
                    if (IsInputSourcePolicy(editable.Behaviors[i])) editable.Behaviors.RemoveAt(i);
                }

                if (plan.characterValidation is 1 or 2)
                {
                    editable.Behaviors.Add(new IntegerFilter
                    {
                        AllowNegative = plan.characterValidation == 2,
                    });
                }
                if (plan.characterLimit > 0)
                {
                    editable.Behaviors.Add(new LengthLimitBehavior
                    {
                        Limit = plan.characterLimit,
                        Unit = TextLengthUnit.Utf16Units,
                    });
                }
                if (plan.keyboardType != 0 || plan.inputType == 1)
                {
                    TryMapKeyboardType(plan.keyboardType, out var keyboardType);
                    var keyboard = new NativeKeyboardBehavior();
                    keyboard.Keyboard.KeyboardType = keyboardType;
                    if (plan.inputType == 1)
                        keyboard.Keyboard.AutoCorrection = AutoCorrection.Enabled;
                    editable.Behaviors.Add(keyboard);
                }
                if (!plan.hideMobileInput)
                    editable.Behaviors.Add(new NativeFieldOverlayBehavior());

                if (plan.inputType == 2)
                {
                    editable.Behaviors.Add(new PasswordBehavior
                    {
                        MaskChar = plan.maskCharacter.ToString(),
                    });
                }
                else if (plan.lineType == 0)
                {
                    editable.Behaviors.Add(new SingleLineBehavior());
                }
                else if (plan.lineType == 1)
                {
                    editable.Behaviors.Add(new SubmitKeyBehavior
                    {
                        Submit = SubmitKey.Enter,
                        KeepFocusOnSubmit = false,
                    });
                    Log(LogSeverity.Warning,
                        "  TMP MultiLineSubmit keeps its text multi-line and binds Enter to submit " +
                        "and release focus. Shift+Enter now inserts a newline, where TMP submitted " +
                        "on that too.");
                }
                if (plan.onFocusSelectAll)
                    editable.Behaviors.Add(new SelectAllOnFocusBehavior());
                editable.Behaviors.Add(plan.restoreOriginalTextOnEscape
                    ? new RestoreOnCancelBehavior()
                    : new DefocusOnCancelBehavior());
                if (placeholder != null)
                {
                    editable.Behaviors.Add(new PlaceholderDecorator
                    {
                        Target = placeholder,
                    });
                }

                editable.ReadOnly = plan.readOnly;
                var active = plan.enabled;
                selectable.enabled = active;
                editable.enabled = active;

                if (!TryRemoveUnsatisfiable(plan.source, null, plan.finding.filePath,
                        out var boxReason))
                    throw new InvalidOperationException(boxReason);
                DestroyObject(plan.source);
                if (plan.source != null)
                    throw new InvalidOperationException(
                        "Unity refused to remove TMP_InputField from the field box.");

                RecordLosses(plan.losses, plan.finding.filePath);
                ClaimFinding(plan.finding, completed);
                migrated++;
                Log(LogSeverity.Info,
                    $"  Migrated TMP_InputField '{HierarchyPath(sourceObject.transform)}' to " +
                    $"UniTextEditable on '{HierarchyPath(textObject.transform)}'.");
                return migrated;
            }
            catch (Exception ex)
            {
                migrationState.SetFailed(plan.finding, new List<ManualReview>
                {
                    CreateReview(ManualReviewKind.MigrationFailure, plan.finding,
                        plan.finding.filePath, tmpInputFieldType,
                        HierarchyPath(sourceObject.transform), ex.Message,
                        "Resolve the reported composite field migration failure, then re-check " +
                        "this finding."),
                });
                throw;
            }
        }

        static bool IsInputSourcePolicy(InputBehavior behavior)
            => behavior is PlaceholderDecorator or InputFilterBase or LengthLimitBehavior or
                SingleLineBehavior or SubmitKeyBehavior or SelectAllOnFocusBehavior or
                RestoreOnCancelBehavior or DefocusOnCancelBehavior or NativeKeyboardBehavior or
                NativeFieldOverlayBehavior;

        static void CopyComponentPolicy(Component source, Component target)
        {
            var sourceState = new SerializedObject(source);
            var targetState = new SerializedObject(target);
            var property = sourceState.GetIterator();
            if (property.NextVisible(true))
            {
                do
                {
                    if (property.propertyPath is "m_ObjectHideFlags" or
                        "m_CorrespondingSourceObject" or "m_PrefabInstance" or "m_PrefabAsset" or
                        "m_GameObject" or "m_Enabled" or "m_EditorHideFlags" or "m_Script" or
                        "m_Name" or "m_EditorClassIdentifier") continue;
                    if (targetState.FindProperty(property.propertyPath) == null)
                        throw new InvalidOperationException(
                            $"The target component has no '{property.propertyPath}' policy slot.");
                    targetState.CopyFromSerializedProperty(property);
                } while (property.NextVisible(false));
            }
            targetState.ApplyModifiedPropertiesWithoutUndo();
        }

        bool TryProcessSpriteMarkup(SerializedObject source, Type tmpType, bool createAssets,
            out TmpSpriteMigrator.ConversionResult result, out string error,
            string textOverride = null)
        {
            var text = textOverride ?? source.FindProperty("m_text")?.stringValue ?? "";
            result = new TmpSpriteMigrator.ConversionResult { text = text };
            error = null;
            var richText = source.FindProperty("m_isRichText")?.boolValue != false;
            if (!richText || text.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) < 0)
            {
                if (createAssets && richText) ReportUnusedSpriteAsset(source);
                return true;
            }

            if (spriteMigrator == null)
            {
                error = "TMP sprite types are unavailable while the text contains <sprite> tags.";
                return false;
            }

            var spriteAsset = source.FindProperty("m_spriteAsset")?.objectReferenceValue;
            var fontAsset = source.FindProperty("m_fontAsset")?.objectReferenceValue;
            var fontColor = source.FindProperty("m_fontColor")?.colorValue ?? Color.white;
            var tintAll = source.FindProperty("m_tintAllSprites")?.boolValue ?? false;
            var hasVertexGradient =
                source.FindProperty("m_enableVertexGradient")?.boolValue == true;

            var converted = createAssets
                ? spriteMigrator.TryConvert(text, spriteAsset, fontAsset, fontColor, tintAll,
                    hasVertexGradient, out result, out error)
                : spriteMigrator.TryValidate(text, spriteAsset, fontAsset, fontColor, tintAll,
                    hasVertexGradient, out error);
            if (!converted || !createAssets || result.warnings == null) return converted;
            for (var i = 0; i < result.warnings.Count; i++)
                Log(LogSeverity.Warning, $"  Sprite: {result.warnings[i]}");
            return true;
        }

        /// <summary>
        /// Warns when a component carries a TMP sprite asset that its serialized text never uses.
        /// No catalog is built for it: every per-occurrence rule the conversion applies — sprite
        /// animation, colour against tint, tint under a gradient, a sprite nested in a font, sup,
        /// sub or voffset tag — can only be judged against markup that exists.
        /// </summary>
        void ReportUnusedSpriteAsset(SerializedObject source)
        {
            var spriteAsset = source.FindProperty("m_spriteAsset")?.objectReferenceValue;
            if (spriteAsset == null) return;
            var owner = source.targetObject as Component;
            var path = owner != null ? HierarchyPath(owner.transform) : source.targetObject.name;
            Log(LogSeverity.Warning,
                $"  '{path}' has TMP sprite asset '{AssetDatabase.GetAssetPath(spriteAsset)}' " +
                "assigned, but its serialized text writes no <sprite> tag, so no sprite catalog " +
                "was built. Text assigned at runtime needs a SpriteModifier with an " +
                "AssetSpriteProvider, bound through an InlineTagRule(\"sprite\") in the Style.");
        }

        bool MigrateComponent(Component tmpComponent, Type tmpType, Type targetType,
            MigrationFinding finding, List<MigrationFinding> completed,
            List<string> createdSpriteAssets)
        {
            var go = tmpComponent.gameObject;
            try
            {
                return MigrateComponentCore(tmpComponent, tmpType, targetType, finding, completed,
                    createdSpriteAssets);
            }
            catch (Exception ex)
            {
                migrationState.SetFailed(finding, new List<ManualReview>
                {
                    CreateReview(ManualReviewKind.MigrationFailure, finding, finding.filePath,
                        tmpType, HierarchyPath(go.transform), ex.Message,
                        "Resolve the reported component replacement failure, then re-check this finding."),
                });
                throw;
            }
        }

        bool MigrateComponentCore(Component tmpComponent, Type tmpType, Type targetType,
            MigrationFinding finding, List<MigrationFinding> completed,
            List<string> createdSpriteAssets)
        {
            var go = tmpComponent.gameObject;
            var so = new SerializedObject(tmpComponent);

            var text = so.FindProperty("m_text")?.stringValue ?? "";
            var fontSizeBase = so.FindProperty("m_fontSizeBase")?.floatValue ?? 36f;
            var fontSize = so.FindProperty("m_fontSize")?.floatValue ?? -99f;
            float effectiveFontSize = fontSize < 0 ? fontSizeBase : fontSize;

            var fontColor = so.FindProperty("m_fontColor")?.colorValue ?? Color.white;
            var alignment = ReadAlignment(so);
            var wrappingMode = so.FindProperty("m_TextWrappingMode")?.intValue ?? 1;
            var enableAutoSizing = so.FindProperty("m_enableAutoSizing")?.boolValue ?? false;
            var fontSizeMin = so.FindProperty("m_fontSizeMin")?.floatValue ?? 10f;
            var fontSizeMax = so.FindProperty("m_fontSizeMax")?.floatValue ?? 72f;
            var fontStyle = so.FindProperty("m_fontStyle")?.intValue ?? 0;
            var isRtl = so.FindProperty("m_isRightToLeft")?.boolValue ?? false;
            var charSpacing = so.FindProperty("m_characterSpacing")?.floatValue ?? 0f;
            var lineSpacing = so.FindProperty("m_lineSpacing")?.floatValue ?? 0f;
            var overflowMode = so.FindProperty("m_overflowMode")?.intValue ?? 0;
            var paragraphSpacing = so.FindProperty("m_paragraphSpacing")?.floatValue ?? 0f;
            var wordSpacing = so.FindProperty("m_wordSpacing")?.floatValue ?? 0f;
            var fontWeight = so.FindProperty("m_fontWeight")?.intValue ?? 400;
            var margin = so.FindProperty("m_margin")?.vector4Value ?? Vector4.zero;
            var raycastTarget = so.FindProperty("m_RaycastTarget")?.boolValue ?? true;
            var maskable = so.FindProperty("m_Maskable")?.boolValue ?? true;
            var richText = so.FindProperty("m_isRichText")?.boolValue ?? true;

            var fontAssetProp = so.FindProperty("m_fontAsset");
            var tmpFontAsset = fontAssetProp?.objectReferenceValue;
            string tmpFontGuid = null;
            if (tmpFontAsset != null)
            {
                var fontPath = AssetDatabase.GetAssetPath(tmpFontAsset);
                tmpFontGuid = AssetDatabase.AssetPathToGUID(fontPath);
            }

            if (!TryProcessSpriteMarkup(so, tmpType, true, out var spriteResult,
                    out var spriteError))
            {
                ReportBlockedFinding(finding, tmpType, go.transform, spriteError);
                return false;
            }
            if (spriteResult.createdAssetPaths != null)
                createdSpriteAssets.AddRange(spriteResult.createdAssetPaths);

            var richTextResult = richText
                ? RichTextConverter.Convert(spriteResult.text)
                : new RichTextConverter.ConversionResult { text = spriteResult.text };
            if (richTextResult.warnings != null)
            {
                foreach (var w in richTextResult.warnings)
                    Log(LogSeverity.Warning, $"  Rich text: {w}");
            }

            var (hAlign, vAlign, flushLastLine, alignWarning) =
                MigrationMapping.DecomposeAlignment(alignment);
            if (alignWarning != null)
                Log(LogSeverity.Warning, $"  {alignWarning}");

            RecordCompleteObject(go, "Migrate TMP → UniText");
            if (!TryRemoveUnsatisfiable(tmpComponent, targetType, finding.filePath,
                    out var removeReason))
            {
                ReportBlockedFinding(finding, tmpType, tmpComponent.transform, removeReason,
                    "Break the dependency between those components, then re-check this finding.");
                return false;
            }

            if (!TryDetachCompatibleDependents(tmpComponent, targetType, out var detached,
                    out var detachReason))
            {
                ReportBlockedFinding(finding, tmpType, tmpComponent.transform, detachReason,
                    "Break the dependency between those two components, then re-check this finding.");
                return false;
            }

            DestroyObject(tmpComponent);
            if (tmpComponent != null)
                throw new InvalidOperationException(
                    $"Unity refused to remove {TypeName(tmpType)} from '{HierarchyPath(go.transform)}'.");

            var newComponent = AddComponent(go, targetType);
            if (newComponent == null)
                throw new InvalidOperationException(
                    $"Unity refused to add {TypeName(targetType)} to '{HierarchyPath(go.transform)}'.");

            var newSo = new SerializedObject(newComponent);

            SetString(newSo, "text", richTextResult.text);
            SetFloat(newSo, "fontSize", effectiveFontSize);
            SetColor(newSo, "m_Color", fontColor);
            SetInt(newSo, "horizontalAlignment", hAlign);
            SetInt(newSo, "verticalAlignment", vAlign);
            SetBool(newSo, "wordWrap", MigrationMapping.ConvertWordWrap(wrappingMode));
            SetBool(newSo, "autoSize", enableAutoSizing);
            SetFloat(newSo, "minFontSize", fontSizeMin);
            SetFloat(newSo, "maxFontSize", fontSizeMax);
            SetBool(newSo, "m_RaycastTarget", raycastTarget);
            SetBool(newSo, "m_Maskable", maskable);
            if (margin != Vector4.zero)
                SetVector4(newSo, "padding",
                    new Vector4(margin.x, margin.w, margin.z, margin.y));

            if (tmpFontGuid == null)
            {
                Log(LogSeverity.Warning,
                    "  The TMP component references no font asset — either none was assigned, or " +
                    "the one it named was deleted. It migrates with no font stack and resolves " +
                    "every codepoint through the system font.");
            }
            else
            {
                var fontStack = FindMappedFontStack(tmpFontGuid);
                if (fontStack != null)
                    SetObjectRef(newSo, "fontStack", fontStack);
                else
                    Log(LogSeverity.Warning, $"  No font mapping for TMP font GUID {tmpFontGuid}");
            }

            newSo.ApplyModifiedProperties();
            RestoreDetached(go, detached);

            var uniTextBase = (UniTextBase)newComponent;

            if (isRtl)
            {
                uniTextBase.Styles.Add(Style.WholeText(new DirectionModifier { Direction = TextDirection.RightToLeft }));
                Log(LogSeverity.Info, "  Added Style: DirectionModifier (RightToLeft) for TMP isRightToLeftText");
            }

            if (spriteResult.styles != null)
            {
                foreach (var spriteStyle in spriteResult.styles)
                {
                    uniTextBase.Styles.Add(new Style
                    {
                        Modifier = spriteStyle.Modifier,
                        Source = new InlineTagRule(spriteStyle.TagName),
                    });
                    Log(LogSeverity.Info,
                        $"  Added Style: SpriteModifier + InlineTagRule(\"{spriteStyle.TagName}\")");
                }
            }

            if (richTextResult.requiredStyles != null)
            {
                foreach (var rs in richTextResult.requiredStyles)
                    AddTagStyle(uniTextBase, rs);
            }

            if (flushLastLine)
            {
                uniTextBase.Styles.Add(Style.WholeText(
                    new AlignmentModifier { LastLine = LastLineAlignment.Justify }));
                Log(LogSeverity.Info, "  Added whole-text AlignmentModifier (TMP Flush justifies the last line too)");
            }

            if (fontStyle != 0)
            {
                foreach (var mapping in MigrationMapping.FontStyleMappings)
                {
                    if ((fontStyle & mapping.flag) != 0)
                        AddWholeTextStyle(uniTextBase, mapping.modifierTypeName);
                }
                if ((fontStyle & MigrationMapping.SuperscriptStyle) != 0)
                    AddWholeTextStyle<ScriptPositionModifier>(uniTextBase,
                        modifier => modifier.Mode = ScriptPositionModifier.Placement.Super);
                if ((fontStyle & MigrationMapping.SubscriptStyle) != 0)
                    AddWholeTextStyle<ScriptPositionModifier>(uniTextBase,
                        modifier => modifier.Mode = ScriptPositionModifier.Placement.Sub);
                if ((fontStyle & MigrationMapping.UnmappedFontStyles) != 0)
                    Log(LogSeverity.Warning,
                        "  Highlight font style dropped — add a HighlightModifier and set its " +
                        "paint, which TMP does not serialize per component");
            }

            if (fontWeight != 400)
            {
                AddWholeTextStyle<VariationModifier>(uniTextBase,
                    modifier => modifier.Weight = UnitValue.Absolute(fontWeight));
                Log(LogSeverity.Warning,
                    $"  Font weight {fontWeight} applied through the variable-font wght axis — " +
                    "it has no effect on a static font, where TMP used a weighted font pair");
            }

            if (charSpacing != 0)
                AddWholeTextStyle<LetterSpacingModifier>(uniTextBase,
                    modifier => modifier.Spacing = EmFromTmpSpacing(charSpacing));

            if (wordSpacing != 0)
                AddWholeTextStyle<WordSpacingModifier>(uniTextBase,
                    modifier => modifier.Spacing = EmFromTmpSpacing(wordSpacing));

            if (lineSpacing != 0)
            {
                AddWholeTextStyle<LineHeightModifier>(uniTextBase,
                    modifier => modifier.HeightValue =
                        UnitValue.Delta(lineSpacing / TmpSpacingPerEm * effectiveFontSize));
                Log(LogSeverity.Warning,
                    "  TMP line spacing scales with the font size; this one is a fixed pixel " +
                    $"delta taken at {effectiveFontSize}px — re-check it if the text auto-sizes");
            }

            if (paragraphSpacing != 0)
                AddWholeTextStyle<ParagraphSpacingModifier>(uniTextBase,
                    modifier => modifier.After = EmFromTmpSpacing(paragraphSpacing));

            if (overflowMode == 1)
                AddWholeTextStyle(uniTextBase, "EllipsisModifier");
            else if (overflowMode == 3)
                AddWholeTextStyle(uniTextBase, "TruncateModifier");

            WarnAboutSharedTags(uniTextBase, richTextResult.sharedTags);

            var goName = go.name;
            Log(LogSeverity.Info, $"  Migrated '{goName}': text=\"{Truncate(text, 40)}\", fontSize={effectiveFontSize}, " +
                                  $"align=({hAlign},{vAlign}), wrap={MigrationMapping.ConvertWordWrap(wrappingMode)}, autoSize={enableAutoSizing}");

            ClaimFinding(finding, completed);

            return true;
        }

        void ReportBlockedFinding(MigrationFinding finding, Type sourceType, Transform transform,
            string reason, string action = null)
        {
            var objectPath = HierarchyPath(transform);
            migrationState.SetFailed(finding, new List<ManualReview>
            {
                CreateReview(ManualReviewKind.MigrationFailure, finding, finding.filePath,
                    sourceType, objectPath, reason,
                    action ??
                    "Resolve the reported rich-text sprite migration problem, then re-check this finding."),
            });
            Log(LogSeverity.Error,
                $"  Kept TMP component '{objectPath}' unchanged: {reason}");
        }

        void CollectSubMeshObjects(GameObject go, Component source, HashSet<GameObject> result)
        {
            if (tmpSubMeshUiType == null && tmpSubMeshType == null) return;
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i).gameObject;
                Component subMesh = null;
                if (tmpSubMeshUiType != null && child.GetComponent(tmpSubMeshUiType) != null)
                    subMesh = child.GetComponent(tmpSubMeshUiType);
                if (tmpSubMeshType != null && child.GetComponent(tmpSubMeshType) != null)
                    subMesh = child.GetComponent(tmpSubMeshType);

                if (subMesh != null)
                {
                    var owner = new SerializedObject(subMesh).FindProperty("m_TextComponent");
                    if (owner?.objectReferenceValue == source) result.Add(child);
                }
                CollectSubMeshObjects(child, source, result);
            }
        }

        void CleanUpSubMeshes(HashSet<GameObject> subMeshes)
        {
            foreach (var obj in subMeshes)
            {
                if (obj == null) continue;
                var name = obj.name;
                DestroyObject(obj);
                if (obj != null)
                    throw new InvalidOperationException(
                        $"Unity refused to remove TMP sub-mesh '{name}'.");
                Log(LogSeverity.Info, $"  Removed TMP sub-mesh: '{name}'");
            }
        }

        UnityEngine.Object FindMappedFontStack(string tmpFontGuid)
        {
            foreach (var entry in fontMappings.fontMappings)
            {
                if (entry.tmpFontGuid != tmpFontGuid) continue;
                if (entry.skipped || string.IsNullOrEmpty(entry.uniTextFontStackGuid)) return null;

                var path = AssetDatabase.GUIDToAssetPath(entry.uniTextFontStackGuid);
                if (string.IsNullOrEmpty(path)) return null;
                return AssetDatabase.LoadAssetAtPath<UniTextFontStack>(path);
            }
            return null;
        }

        /// <summary>
        /// Sets a finding aside as migrated without recording it. The status is written only once
        /// the file it belongs to has actually reached disk.
        /// </summary>
        static void ClaimFinding(MigrationFinding finding, List<MigrationFinding> completed)
        {
            if (!completed.Contains(finding)) completed.Add(finding);
        }

        /// <summary>
        /// Applies a modifier to the whole text. TMP expresses these as component settings, so the
        /// migrated component must carry them as applied styles — binding them to a tag would only
        /// teach the component a markup word nothing in the text writes.
        /// </summary>
        /// <summary>
        /// How many units TMP spends on one em. Every spacing TMP serializes — character, word,
        /// line and paragraph — is multiplied by <c>fontSize * 0.01f</c> before it reaches an
        /// advance, so the stored number is hundredths of an em and converts exactly.
        /// </summary>
        const float TmpSpacingPerEm = 100f;

        /// <summary>The em-relative value behind a TMP spacing setting.</summary>
        static UnitValue EmFromTmpSpacing(float tmpSpacing) =>
            UnitValue.Em(tmpSpacing / TmpSpacingPerEm);

        void AddWholeTextStyle(UniTextBase target, string modifierTypeName)
        {
            var modifier = MigrationMapping.CreateModifier(modifierTypeName);
            if (modifier == null) return;
            target.Styles.Add(Style.WholeText(modifier));
            Log(LogSeverity.Info, $"  Added whole-text {modifier.GetType().Name}");
        }

        /// <summary>
        /// Applies a configured modifier to the whole text. The value is set on the modifier's own
        /// parameter property, so it stays typed all the way: no number is ever spelled into a
        /// string, and no parse — or locale — sits between the TMP setting and the migrated one.
        /// </summary>
        void AddWholeTextStyle<TModifier>(UniTextBase target, Action<TModifier> configure)
            where TModifier : BaseModifier, new()
        {
            var modifier = new TModifier();
            configure(modifier);
            target.Styles.Add(Style.WholeText(modifier));
            Log(LogSeverity.Info, $"  Added whole-text {typeof(TModifier).Name}");
        }

        /// <summary>
        /// Gives one required tag its Style unless something already supplies that tag — the
        /// component's own Styles, or the project-wide preset every component reads. A second
        /// source for one tag applies its effect twice.
        /// </summary>
        void AddTagStyle(UniTextBase target, RichTextConverter.RequiredStyle required)
        {
            var tagName = required.tagName;
            if (HasTagSource(target, tagName))
            {
                Log(LogSeverity.Info, $"  <{tagName}> already has a Style — left as configured");
                return;
            }
            if (GlobalPresetSupplies(target, tagName))
            {
                Log(LogSeverity.Info,
                    $"  <{tagName}> comes from the project-wide Style preset — no component entry added");
                return;
            }

            if (required.standaloneRuleTypeName != null)
            {
                var standalone =
                    MigrationMapping.CreateStandaloneRule(required.standaloneRuleTypeName);
                if (standalone == null) return;
                target.Styles.Add(new Style { Source = standalone });
                Log(LogSeverity.Info,
                    $"  Added Style: {standalone.GetType().Name} carrying <{tagName}>");
                return;
            }

            var modifier = MigrationMapping.CreateModifier(required.modifierTypeName);
            if (modifier == null) return;

            var rule = new TagRule(tagName);
            if (required.defaultParameter != null) rule.DefaultParameter = required.defaultParameter;
            target.Styles.Add(new Style { Modifier = modifier, Source = rule });
            Log(LogSeverity.Info, required.defaultParameter != null
                ? $"  Added Style: {modifier.GetType().Name} + TagRule(\"{tagName}\") default=\"{required.defaultParameter}\""
                : $"  Added Style: {modifier.GetType().Name} + TagRule(\"{tagName}\")");
        }

        /// <summary>
        /// Reports markup left without a modifier behind it. UniText's tag vocabulary is authored,
        /// not built in: with no project-wide Style preset assigned and no entry of its own, a
        /// component renders these tags as literal characters.
        /// </summary>
        void WarnAboutSharedTags(UniTextBase target, List<string> tags)
        {
            if (tags == null || UniTextSettings.GlobalStylePreset != null) return;

            var missing = new List<string>();
            for (int i = 0; i < tags.Count; i++)
                if (!HasTagSource(target, tags[i])) missing.Add($"<{tags[i]}>");
            if (missing.Count == 0) return;

            Log(LogSeverity.Warning,
                $"  {string.Join(", ", missing)} have no Style entry, and no project-wide Style " +
                "preset is assigned in Project Settings → UniText — they render as literal " +
                "characters until one supplies their modifiers.");
        }

        static bool HasTagSource(UniTextBase target, string tagName)
            => HasTagSource(target.Styles, tagName);

        /// <summary>
        /// Whether any style already answers to the tag. Matched on the rule's source token, not
        /// its type: markup a standalone rule carries by itself is taken just as surely as markup
        /// behind a TagRule.
        /// </summary>
        static bool HasTagSource(IReadOnlyList<Style> styles, string tagName)
        {
            for (int i = 0; i < styles.Count; i++)
            {
                if (styles[i]?.Source is ParseRule rule &&
                    string.Equals(rule.SourceToken, tagName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Whether the project-wide preset already teaches this component the tag. A component
        /// that opted out of the preset is not covered by it.
        /// </summary>
        static bool GlobalPresetSupplies(UniTextBase target, string tagName)
        {
            var preset = UniTextSettings.GlobalStylePreset;
            return preset != null && target.UseGlobalStylePreset &&
                   HasTagSource(preset.Styles, tagName);
        }

        static void SetString(SerializedObject so, string prop, string value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.stringValue = value;
        }

        static void SetFloat(SerializedObject so, string prop, float value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.floatValue = value;
        }

        /// <summary>
        /// The serialized property a migrated setting is written to. A name the target does not
        /// carry means the migration silently drops that setting, so the miss is reported instead
        /// of passing for a value that was written.
        /// </summary>
        static SerializedProperty RequireProperty(SerializedObject so, string prop)
        {
            var property = so.FindProperty(prop);
            if (property != null) return property;
            Debug.LogError(
                $"[UniText] Migration cannot write '{prop}' on " +
                $"{so.targetObject.GetType().Name}: no such serialized field. That setting is " +
                "not carried over.");
            return null;
        }

        /// <summary>
        /// TMP's alignment, taken from the two fields that hold it. <c>m_textAlignment</c> is the
        /// legacy field TMP keeps only to upgrade older assets — its live value is the sentinel
        /// <c>Converted</c> (0xFFFF), and TMP itself composes <c>alignment</c> from the horizontal
        /// and vertical fields instead.
        /// </summary>
        static int ReadAlignment(SerializedObject so)
        {
            const int converted = 0xFFFF;
            const int defaultAlignment = 0x101;

            var horizontal = so.FindProperty("m_HorizontalAlignment");
            var vertical = so.FindProperty("m_VerticalAlignment");
            if (horizontal != null && vertical != null)
                return horizontal.intValue | vertical.intValue;

            var legacy = so.FindProperty("m_textAlignment")?.intValue ?? defaultAlignment;
            return legacy == converted ? defaultAlignment : legacy;
        }

        static void SetInt(SerializedObject so, string prop, int value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.intValue = value;
        }

        static void SetBool(SerializedObject so, string prop, bool value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.boolValue = value;
        }

        /// <summary>TMP margins are (Left, Top, Right, Bottom); UniText padding is (Left, Bottom, Right, Top).</summary>
        static void SetVector4(SerializedObject so, string prop, Vector4 value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.vector4Value = value;
        }

        static void SetColor(SerializedObject so, string prop, Color value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.colorValue = value;
        }

        static void SetObjectRef(SerializedObject so, string prop, UnityEngine.Object value)
        {
            var p = RequireProperty(so, prop);
            if (p != null) p.objectReferenceValue = value;
        }

        void Log(LogSeverity severity, string message)
        {
            log.Add(new LogEntry(severity, message));
        }

        static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }
    }
}
