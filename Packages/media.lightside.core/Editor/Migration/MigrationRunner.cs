using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightSide
{
    internal sealed class MigrationRunResult
    {
        public int Changes { get; }
        public IReadOnlyList<Exception> Failures { get; }

        /// <summary>
        /// Sources the pass could not read, each as <c>path — reason</c>. They were neither inspected nor
        /// migrated, and a later pass retries them; they do not make the pass unsuccessful.
        /// </summary>
        public IReadOnlyList<string> Uninspected { get; }

        /// <summary>
        /// Assets whose staged changes were withheld because the editor holds unsaved changes for them.
        /// A later pass retries them; they do not make the pass unsuccessful.
        /// </summary>
        public IReadOnlyList<string> Deferred { get; }

        public bool Succeeded => Failures.Count == 0;

        public MigrationRunResult(int changes, IReadOnlyList<Exception> failures,
            IReadOnlyList<string> uninspected = null, IReadOnlyList<string> deferred = null)
        {
            Changes = changes;
            Failures = failures;
            Uninspected = uninspected ?? Array.Empty<string>();
            Deferred = deferred ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Applies discovered migrations in deterministic order. Idempotent asset-local work isolates each
    /// candidate; one-shot and prepared work is staged across all candidates before commit.
    /// </summary>
    internal static class MigrationRunner
    {
        static List<IMigration> Discover()
        {
            var result = new List<IMigration>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IMigration>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                try { result.Add((IMigration)Activator.CreateInstance(type)); }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"[LightSide] Migration '{type.FullName}' could not be instantiated.", e);
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var migration in result)
            {
                if (string.IsNullOrEmpty(migration.Id))
                    throw new InvalidOperationException(
                        $"[LightSide] Migration '{migration.GetType().FullName}' has no id.");
                if (!ids.Add(migration.Id))
                    throw new InvalidOperationException(
                        $"[LightSide] More than one migration uses id '{migration.Id}'.");
                if (migration.Tokens == null)
                    throw new InvalidOperationException(
                        $"[LightSide] Migration '{migration.Id}' has no token collection.");
                if (migration.Tokens.Count == 0 && migration is not IMigrationPreparation)
                    throw new InvalidOperationException(
                        $"[LightSide] Migration '{migration.Id}' has no discovery tokens or preparation.");
                foreach (var token in migration.Tokens)
                    if (!MigrationTokens.IsValid(token))
                        throw new InvalidOperationException(
                            $"[LightSide] Migration '{migration.Id}' contains invalid discovery token '{token}'.");
            }

            result.Sort((a, b) =>
            {
                if (a.Idempotent != b.Idempotent) return a.Idempotent ? -1 : 1;
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return result;
        }

        public static void Recover(MigrationLedger ledger) => MigrationJournal.Recover(ledger);

        public static MigrationRunResult RunAll(MigrationLedger ledger)
        {
            var migrations = Discover();
            var doneSnapshot = new HashSet<string>(ledger.completedMigrations, StringComparer.Ordinal);
            migrations.RemoveAll(migration => !migration.Idempotent && doneSnapshot.Contains(migration.Id));
            if (migrations.Count == 0)
                return new MigrationRunResult(0, Array.Empty<Exception>());
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                return TextSerializationRequired();

            var targetedScriptGuids = new HashSet<string>();
            var targetedReferenceGuids = new HashSet<string>();
            var targetedOverridePaths = new HashSet<string>();
            foreach (var migration in migrations)
                foreach (var token in migration.Tokens)
                {
                    if (MigrationTokens.IsScript(token))
                        targetedScriptGuids.Add(MigrationTokens.ScriptGuid(token));
                    else if (MigrationTokens.IsAssetReference(token))
                        targetedReferenceGuids.Add(MigrationTokens.AssetReferenceGuid(token));
                    else if (MigrationTokens.IsPrefabOverride(token))
                        targetedOverridePaths.Add(MigrationTokens.PrefabOverridePath(token));
                }

            var dirtyAssets = DirtyAssetPaths();
            var index = MigrationIndex.Get(targetedScriptGuids, targetedReferenceGuids,
                targetedOverridePaths);
            var failures = new List<Exception>();
            var deferred = new HashSet<string>(StringComparer.Ordinal);
            int changes = 0;

            foreach (var migration in migrations)
            {
                if (!migration.Idempotent && failures.Count > 0) break;
                (int changes, bool indexValid) step;
                if (migration.Idempotent && migration is not IMigrationPreparation)
                    step = RunByAsset(migration, index, index, dirtyAssets, deferred, failures);
                else
                    step = RunAtomically(migration, index, index, dirtyAssets, ledger, failures);
                changes += step.changes;
                if (!step.indexValid) break;
            }

            return new MigrationRunResult(changes, failures, index.Uninspected, Sorted(deferred));
        }

        /// <summary>
        /// Applies idempotent asset-local migrations to assets that just entered the project, so legacy data
        /// arriving after a package was stamped is still migrated. One-shot work stays with the full pass.
        /// </summary>
        public static MigrationRunResult RunImported(IReadOnlyList<string> assetPaths)
        {
            var index = MigrationIndex.Loaded();
            if (index == null) return new MigrationRunResult(0, Array.Empty<Exception>());

            var source = new ImportedCandidates(index, assetPaths);
            if (source.IsEmpty) return new MigrationRunResult(0, Array.Empty<Exception>());

            var migrations = Discover();
            migrations.RemoveAll(migration => !migration.Idempotent || migration is IMigrationPreparation);
            if (migrations.Count == 0)
                return new MigrationRunResult(0, Array.Empty<Exception>());
            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                return TextSerializationRequired();

            var dirtyAssets = DirtyAssetPaths();
            var failures = new List<Exception>();
            var deferred = new HashSet<string>(StringComparer.Ordinal);
            int changes = 0;

            foreach (var migration in migrations)
            {
                var step = RunByAsset(migration, source, index, dirtyAssets, deferred, failures);
                changes += step.changes;
                if (!step.indexValid) break;
            }

            return new MigrationRunResult(changes, failures, null, Sorted(deferred));
        }

        static List<string> Sorted(HashSet<string> paths)
        {
            var result = new List<string>(paths);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        static MigrationRunResult TextSerializationRequired() =>
            new(0, new Exception[]
            {
                new InvalidOperationException(
                    "[LightSide] Automatic migration requires Asset Serialization Mode = Force Text. " +
                    "Change the Editor project setting, save the project, and reload the scripting domain."),
            });

        static (int changes, bool indexValid) RunByAsset(IMigration migration, ICandidateSource source,
            MigrationIndex index, HashSet<string> dirtyAssets, HashSet<string> deferred,
            List<Exception> failures)
        {
            int changes = 0;
            bool commitFailed = false;
            var touched = new List<string>();
            foreach (var assetPath in Candidates(migration, source))
            {
                var actions = new QueuedAssetActions(dirtyAssets);
                PendingWrite? write;
                try
                {
                    write = Stage(migration, assetPath, actions);
                }
                catch (Exception e)
                {
                    var cause = actions.Discard(e);
                    failures.Add(new InvalidOperationException(
                        $"[LightSide] Migration '{migration.Id}' could not inspect '{assetPath}'.", cause));
                    continue;
                }

                if (write.HasValue && dirtyAssets.Contains(assetPath))
                {
                    var cleanupFailure = actions.Discard(null);
                    if (cleanupFailure != null)
                        failures.Add(new InvalidOperationException(
                            $"[LightSide] Migration '{migration.Id}' could not discard work deferred for '{assetPath}'.",
                            cleanupFailure));
                    else
                        deferred.Add(assetPath);
                    continue;
                }

                try
                {
                    changes += Commit(migration.Id,
                        write.HasValue ? new[] { write.Value } : Array.Empty<PendingWrite>(), actions);
                    if (write.HasValue) touched.Add(write.Value.assetPath);
                    actions.AppendTouched(touched);
                }
                catch (Exception e)
                {
                    var cause = actions.Discard(e);
                    failures.Add(new InvalidOperationException(
                        $"[LightSide] Migration '{migration.Id}' could not complete for '{assetPath}'.", cause));
                    commitFailed = true;
                    break;
                }
            }

            var indexValid = UpdateIndex(index, touched, migration.Id, failures);
            return (changes, !commitFailed && indexValid);
        }

        static (int changes, bool indexValid) RunAtomically(IMigration migration, ICandidateSource source,
            MigrationIndex index, HashSet<string> dirtyAssets, MigrationLedger ledger, List<Exception> failures)
        {
            var actions = new QueuedAssetActions(dirtyAssets);
            var writes = new List<PendingWrite>();
            int changes = 0;
            bool committed = false;
            bool commitAttempted = false;
            try
            {
                if (migration is IMigrationPreparation preparation)
                    preparation.Prepare(actions);
                foreach (var assetPath in Candidates(migration, source))
                {
                    var write = Stage(migration, assetPath, actions);
                    if (write.HasValue) writes.Add(write.Value);
                }

                foreach (var write in writes)
                    if (dirtyAssets.Contains(write.assetPath))
                        throw new InvalidOperationException(
                            $"[LightSide] Asset '{write.assetPath}' has unsaved editor changes. " +
                            "Save it; the migration retries automatically.");

                commitAttempted = true;
                changes = migration.Idempotent
                    ? Commit(migration.Id, writes, actions)
                    : CommitOneShot(migration.Id, writes, actions, ledger);
                committed = true;

                var touched = new List<string>();
                foreach (var write in writes) touched.Add(write.assetPath);
                actions.AppendTouched(touched);
                return (changes, UpdateIndex(index, touched, migration.Id, failures));
            }
            catch (Exception e)
            {
                var cause = actions.Discard(e);
                failures.Add(new InvalidOperationException(
                    $"[LightSide] Migration '{migration.Id}' could not complete.", cause));
                if (!committed) return (0, !commitAttempted);

                var touched = new List<string>();
                foreach (var write in writes) touched.Add(write.assetPath);
                actions.AppendTouched(touched);
                return (changes, UpdateIndex(index, touched, migration.Id, failures));
            }
        }

        static int CommitOneShot(string migrationId, IReadOnlyList<PendingWrite> writes,
            QueuedAssetActions actions, MigrationLedger ledger)
        {
            if (actions.HasActions)
                throw new InvalidOperationException(
                    $"[LightSide] One-shot migration '{migrationId}' cannot queue asset actions.");

            if (writes.Count == 0)
            {
                ledger.completedMigrations.Add(migrationId);
                try { ledger.Save(); }
                catch
                {
                    ledger.completedMigrations.Remove(migrationId);
                    throw;
                }
                return 0;
            }

            ValidateWrites(writes);
            MigrationJournal.Begin(migrationId, writes);
            try { ValidateWrites(writes); }
            catch (Exception validationFailure)
            {
                try { MigrationJournal.Complete(); }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        $"[LightSide] One-shot migration '{migrationId}' changed before commit and its journal could not be removed.",
                        validationFailure, cleanupFailure);
                }
                throw;
            }

            int changes;
            try { changes = Commit(migrationId, writes, actions, true); }
            catch (Exception commitFailure)
            {
                try { MigrationJournal.Rollback(); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(
                        $"[LightSide] One-shot migration '{migrationId}' failed and its journal rollback failed.",
                        commitFailure, rollbackFailure);
                }
                throw;
            }

            ledger.completedMigrations.Add(migrationId);
            try { ledger.Save(); }
            catch (Exception ledgerFailure)
            {
                ledger.completedMigrations.Remove(migrationId);
                try { MigrationJournal.Rollback(); }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(
                        $"[LightSide] One-shot migration '{migrationId}' could not save completion or roll back.",
                        ledgerFailure, rollbackFailure);
                }
                throw;
            }

            try { MigrationJournal.Complete(); }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[LightSide] Migration '{migrationId}' completed, but its recovery journal " +
                    $"could not be removed ({e.Message}).");
            }
            return changes;
        }

        static List<string> Candidates(IMigration migration, ICandidateSource source)
        {
            var candidates = new List<string>(source.FindCandidates(migration.Tokens));
            candidates.Sort(StringComparer.Ordinal);
            return candidates;
        }

        static PendingWrite? Stage(IMigration migration, string assetPath,
            QueuedAssetActions actions)
        {
            try
            {
                var fsPath = ProjectYamlFiles.ToFsPath(assetPath);
                if (fsPath == null)
                    throw new InvalidOperationException("The candidate is not writable.");
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                    throw new InvalidOperationException("The candidate has no asset identity.");
                var read = ProjectYamlFiles.ReadYaml(fsPath, out var text, out var unreadable);
                if (read != YamlReadResult.Text)
                    throw new InvalidDataException(read == YamlReadResult.Binary
                        ? "The candidate is no longer text-serialized YAML."
                        : $"The candidate cannot be read: {unreadable}.");

                var documents = UnityYaml.ParseDocuments(text);
                var edit = new YamlEdit(text);
                migration.Migrate(new MigrationContext(assetPath, documents, edit, actions));
                return edit.HasChanges
                    ? new PendingWrite(assetPath, fsPath, guid, text, edit.Apply())
                    : null;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    $"[LightSide] Cannot migrate '{assetPath}'.", e);
            }
        }

        internal static HashSet<string> DirtyAssetPaths()
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                    result.Add(string.IsNullOrEmpty(scene.path)
                        ? $"<untitled scene {i + 1}>"
                        : scene.path);
            }

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.isDirty)
                result.Add(string.IsNullOrEmpty(prefabStage.assetPath)
                    ? "<unsaved prefab stage>"
                    : prefabStage.assetPath);

            foreach (var asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
                if (asset != null && EditorUtility.IsDirty(asset))
                {
                    var path = AssetDatabase.GetAssetPath(asset);
                    if (!string.IsNullOrEmpty(path) && ProjectYamlFiles.HasYamlExtension(path))
                        result.Add(path);
                }
            return result;
        }

        static bool UpdateIndex(MigrationIndex index, List<string> touched,
            string migrationId, List<Exception> failures)
        {
            if (touched.Count == 0) return true;
            try
            {
                index.UpdateAssets(touched);
                return true;
            }
            catch (Exception e)
            {
                failures.Add(new InvalidOperationException(
                    $"[LightSide] Could not update the migration index after '{migrationId}'.", e));
                return false;
            }
        }

        static int Commit(string migrationId, IReadOnlyList<PendingWrite> writes,
            QueuedAssetActions actions, bool validated = false)
        {
            if (!validated) ValidateWrites(writes);

            int attempted = -1;
            try
            {
                for (int i = 0; i < writes.Count; i++)
                {
                    attempted = i;
                    MigrationFile.WriteAllText(writes[i].fsPath, writes[i].migrated);
                }
                return writes.Count + actions.Flush();
            }
            catch (Exception failure)
            {
                var rollbackFailures = RollbackWrites(writes, attempted);

                if (rollbackFailures.Count > 0)
                {
                    rollbackFailures.Insert(0, failure);
                    throw new AggregateException(
                        $"[LightSide] Migration '{migrationId}' failed and its YAML rollback was incomplete.",
                        rollbackFailures);
                }

                throw new IOException(
                    $"[LightSide] Migration '{migrationId}' could not commit its changes; YAML writes were restored.",
                    failure);
            }
        }

        static List<Exception> RollbackWrites(IReadOnlyList<PendingWrite> writes, int attempted)
        {
            var pending = new List<PendingWrite>();
            var failures = new List<Exception>();
            for (int i = 0; i <= attempted; i++)
            {
                var write = writes[i];
                try
                {
                    if (AssetDatabase.AssetPathToGUID(write.assetPath) != write.guid)
                        throw new InvalidOperationException(
                            "The asset no longer has its staged identity.");
                    var current = ProjectYamlFiles.ReadText(write.fsPath);
                    if (string.Equals(current, write.original, StringComparison.Ordinal))
                        continue;
                    if (!string.Equals(current, write.migrated, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "The asset no longer contains either staged state.");
                    pending.Add(write);
                }
                catch (Exception e)
                {
                    failures.Add(new IOException(
                        $"Could not validate '{write.assetPath}' for rollback.", e));
                }
            }

            foreach (var write in pending)
                try { MigrationFile.WriteAllText(write.fsPath, write.original); }
                catch (Exception e)
                {
                    failures.Add(new IOException(
                        $"Could not restore '{write.assetPath}'.", e));
                }
            return failures;
        }

        static void ValidateWrites(IReadOnlyList<PendingWrite> writes)
        {
            foreach (var write in writes)
            {
                string current;
                if (AssetDatabase.AssetPathToGUID(write.assetPath) != write.guid)
                    throw new InvalidOperationException(
                        $"[LightSide] Asset '{write.assetPath}' changed identity while its migration was staged.");
                try { current = ProjectYamlFiles.ReadText(write.fsPath); }
                catch (Exception e)
                {
                    throw new IOException(
                        $"[LightSide] Cannot verify staged asset '{write.assetPath}'.", e);
                }
                if (!string.Equals(current, write.original, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"[LightSide] Asset '{write.assetPath}' changed while its migration was staged.");
            }
        }

        readonly struct PendingWrite
        {
            public readonly string assetPath;
            public readonly string fsPath;
            public readonly string guid;
            public readonly string original;
            public readonly string migrated;

            public PendingWrite(string assetPath, string fsPath, string guid, string original, string migrated)
            {
                this.assetPath = assetPath;
                this.fsPath = fsPath;
                this.guid = guid;
                this.original = original;
                this.migrated = migrated;
            }
        }

        static class MigrationJournal
        {
            const int CurrentFormat = 1;
            const string Path = "Library/LightSide/MigrationTransaction.json";

            [Serializable]
            sealed class State
            {
                public int format = CurrentFormat;
                public string migrationId;
                public List<OriginalFile> files = new();
            }

            [Serializable]
            sealed class OriginalFile
            {
                public string assetPath;
                public string guid;
                public string original;
                public string migratedHash;
            }

            public static void Recover(MigrationLedger ledger)
            {
                if (!File.Exists(Path))
                    return;

                var state = Load();
                if (!ledger.completedMigrations.Contains(state.migrationId))
                    try { Restore(state); }
                    finally { AssetDatabase.Refresh(); }
                Complete();
            }

            public static void Begin(string migrationId, IReadOnlyList<PendingWrite> writes)
            {
                if (File.Exists(Path))
                    throw new InvalidOperationException(
                        $"[LightSide] Unresolved migration journal '{Path}' already exists.");

                var state = new State { migrationId = migrationId };
                foreach (var write in writes)
                {
                    state.files.Add(new OriginalFile
                    {
                        assetPath = write.assetPath,
                        guid = write.guid,
                        original = write.original,
                        migratedHash = Digest(write.migrated),
                    });
                }
                MigrationFile.WriteAllText(Path, JsonUtility.ToJson(state));
            }

            public static void Rollback()
            {
                if (!File.Exists(Path))
                    throw new InvalidOperationException(
                        $"[LightSide] Migration journal '{Path}' is missing.");
                Restore(Load());
                Complete();
            }

            public static void Complete()
            {
                if (File.Exists(Path)) File.Delete(Path);
            }

            static State Load()
            {
                State state;
                try
                {
                    var json = File.ReadAllText(Path);
                    if (json.IndexOf("\"format\"", StringComparison.Ordinal) < 0 ||
                        json.IndexOf("\"migrationId\"", StringComparison.Ordinal) < 0 ||
                        json.IndexOf("\"files\"", StringComparison.Ordinal) < 0)
                        throw new InvalidDataException("The required journal fields are missing.");
                    state = JsonUtility.FromJson<State>(json);
                }
                catch (Exception e)
                {
                    throw new InvalidDataException(
                        $"[LightSide] Cannot read migration journal '{Path}'.", e);
                }

                if (state == null || state.format != CurrentFormat ||
                    string.IsNullOrEmpty(state.migrationId) || state.files is not { Count: > 0 })
                    throw new InvalidDataException(
                        $"[LightSide] Migration journal '{Path}' is invalid.");

                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var file in state.files)
                    if (file == null || !IsSafeAssetPath(file.assetPath) || string.IsNullOrEmpty(file.guid) ||
                        file.original == null ||
                        file.migratedHash?.Length != 64 || !paths.Add(file.assetPath))
                        throw new InvalidDataException(
                            $"[LightSide] Migration journal '{Path}' contains an invalid file entry.");
                return state;
            }

            static bool IsSafeAssetPath(string assetPath)
            {
                if (string.IsNullOrEmpty(assetPath) || assetPath.IndexOf('\\') >= 0 ||
                    (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) &&
                     !assetPath.StartsWith("Packages/", StringComparison.Ordinal)))
                    return false;
                foreach (var segment in assetPath.Split('/'))
                    if (segment.Length == 0 || segment == "." || segment == "..")
                        return false;
                return true;
            }

            static void Restore(State state)
            {
                var pending = new List<(OriginalFile file, string fsPath)>();
                var failures = new List<Exception>();
                foreach (var file in state.files)
                    try
                    {
                        var fsPath = ProjectYamlFiles.ToFsPath(file.assetPath);
                        if (fsPath == null)
                            throw new InvalidOperationException("The asset is no longer writable.");
                        if (AssetDatabase.AssetPathToGUID(file.assetPath) != file.guid)
                            throw new InvalidOperationException("The asset no longer has its journaled identity.");
                        var current = ProjectYamlFiles.ReadText(fsPath);
                        if (string.Equals(current, file.original, StringComparison.Ordinal))
                            continue;
                        if (!string.Equals(Digest(current), file.migratedHash, StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                "The asset no longer contains either journaled state.");
                        pending.Add((file, fsPath));
                    }
                    catch (Exception e)
                    {
                        failures.Add(new IOException(
                            $"[LightSide] Cannot validate '{file.assetPath}' against the migration journal.", e));
                    }

                if (failures.Count > 0)
                    throw new AggregateException(
                        $"[LightSide] Migration journal '{Path}' conflicts with the current project.", failures);

                foreach (var (file, fsPath) in pending)
                    try { MigrationFile.WriteAllText(fsPath, file.original); }
                    catch (Exception e)
                    {
                        failures.Add(new IOException(
                            $"[LightSide] Could not restore '{file.assetPath}' from the migration journal.", e));
                    }

                if (failures.Count > 0)
                    throw new AggregateException(
                        $"[LightSide] Migration journal '{Path}' could not be fully restored.", failures);
            }

            static string Digest(string content)
            {
                using var algorithm = SHA256.Create();
                using (var crypto = new CryptoStream(Stream.Null, algorithm, CryptoStreamMode.Write))
                using (var writer = new StreamWriter(crypto, new UTF8Encoding(false)))
                    writer.Write(content);
                return BitConverter.ToString(algorithm.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        sealed class QueuedAssetActions : IAssetActions
        {
            readonly HashSet<string> dirtyAssets;
            readonly List<(UnityEngine.Object asset, string path)> creates = new();
            readonly List<string> deletes = new();
            readonly List<(string from, string to)> moves = new();

            public QueuedAssetActions(HashSet<string> dirtyAssets) => this.dirtyAssets = dirtyAssets;

            public bool HasActions => creates.Count > 0 || deletes.Count > 0 || moves.Count > 0;

            public void Create(UnityEngine.Object asset, string assetPath) => creates.Add((asset, assetPath));
            public void Delete(string assetPath) => deletes.Add(assetPath);
            public void Move(string fromAssetPath, string toAssetPath) => moves.Add((fromAssetPath, toAssetPath));

            public void EnsurePersisted(string assetPath)
            {
                if (dirtyAssets.Contains(assetPath))
                    throw new InvalidOperationException(
                        $"[LightSide] Asset '{assetPath}' has unsaved editor changes. " +
                        "Save it; the migration retries automatically.");
            }

            public void AppendTouched(List<string> paths)
            {
                foreach (var (_, path) in creates) paths.Add(path);
                foreach (var (_, to) in moves) paths.Add(to);
                paths.AddRange(deletes);
            }

            public int Flush()
            {
                Validate(out var moveGuids, out var deleteGuid);
                int n = 0;
                var created = new List<(UnityEngine.Object asset, string path, string guid)>();
                var moved = new List<(string from, string to, string guid)>();
                var createdFolders = new List<(string path, string guid)>();
                try
                {
                    foreach (var (asset, path) in creates)
                    {
                        EnsureParentFolder(path, createdFolders);
                        if (!IsVacantAssetPath(path))
                            throw new InvalidOperationException(
                                $"[LightSide] Migration asset target '{path}' became occupied.");
                        AssetDatabase.CreateAsset(asset, path);
                        var guid = AssetDatabase.AssetPathToGUID(path);
                        if (!string.IsNullOrEmpty(guid)) created.Add((asset, path, guid));
                        if (string.IsNullOrEmpty(guid) || AssetDatabase.GetAssetPath(asset) != path)
                            throw new InvalidOperationException(
                                $"[LightSide] Failed to create asset '{path}'.");
                        n++;
                    }
                    foreach (var (from, to) in moves)
                    {
                        EnsureParentFolder(to, createdFolders);
                        var guid = AssetDatabase.AssetPathToGUID(from);
                        if (guid != moveGuids[from] || !IsVacantAssetPath(to))
                            throw new InvalidOperationException(
                                $"[LightSide] Migration move '{from}' to '{to}' changed before execution.");
                        var error = AssetDatabase.MoveAsset(from, to);
                        if (!string.IsNullOrEmpty(error))
                            throw new InvalidOperationException(
                                $"[LightSide] Failed to move asset '{from}' to '{to}': {error}");
                        moved.Add((from, to, guid));
                        if (AssetDatabase.AssetPathToGUID(to) != guid ||
                            !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(from)))
                            throw new InvalidOperationException(
                                $"[LightSide] Moved asset '{from}' no longer resolves at '{to}'.");
                        n++;
                    }
                    foreach (var path in deletes)
                    {
                        if (AssetDatabase.AssetPathToGUID(path) != deleteGuid)
                            throw new InvalidOperationException(
                                $"[LightSide] Migration delete target '{path}' changed before execution.");
                        if (!AssetDatabase.DeleteAsset(path))
                            throw new InvalidOperationException(
                                $"[LightSide] Failed to delete asset '{path}'.");
                        n++;
                    }
                    return n;
                }
                catch (Exception failure)
                {
                    var rollbackFailures = new List<Exception>();
                    for (int i = moved.Count - 1; i >= 0; i--)
                    {
                        var (from, to, guid) = moved[i];
                        try
                        {
                            if (AssetDatabase.AssetPathToGUID(to) != guid ||
                                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(from)))
                                throw new InvalidOperationException(
                                    "The moved asset no longer has its committed identity.");
                            var error = AssetDatabase.MoveAsset(to, from);
                            if (!string.IsNullOrEmpty(error))
                                throw new InvalidOperationException(error);
                            if (AssetDatabase.AssetPathToGUID(from) != guid)
                                throw new InvalidOperationException(
                                    "The restored asset has a different identity.");
                        }
                        catch (Exception e)
                        {
                            rollbackFailures.Add(new InvalidOperationException(
                                $"Could not move '{to}' back to '{from}'.", e));
                        }
                    }
                    for (int i = created.Count - 1; i >= 0; i--)
                        try
                        {
                            var (asset, path, guid) = created[i];
                            if (AssetDatabase.AssetPathToGUID(path) != guid ||
                                AssetDatabase.GetAssetPath(asset) != path)
                                throw new InvalidOperationException(
                                    "The created asset no longer has its committed identity.");
                            if (!AssetDatabase.DeleteAsset(path))
                                throw new InvalidOperationException("AssetDatabase rejected the deletion.");
                        }
                        catch (Exception e)
                        {
                            rollbackFailures.Add(new InvalidOperationException(
                                $"Could not remove created asset '{created[i].path}'.", e));
                        }
                    for (int i = createdFolders.Count - 1; i >= 0; i--)
                    {
                        var (folder, guid) = createdFolders[i];
                        try
                        {
                            if (!AssetDatabase.IsValidFolder(folder)) continue;
                            if (AssetDatabase.AssetPathToGUID(folder) != guid)
                                throw new InvalidOperationException(
                                    "The created folder no longer has its committed identity.");
                            var fsFolder = ProjectYamlFiles.ToFsPath(folder);
                            if (fsFolder == null || !Directory.Exists(fsFolder) ||
                                Directory.GetFileSystemEntries(fsFolder).Length > 0)
                                throw new InvalidOperationException("The folder is inaccessible or no longer empty.");
                            if (!AssetDatabase.DeleteAsset(folder))
                                throw new InvalidOperationException("AssetDatabase rejected the deletion.");
                        }
                        catch (Exception e)
                        {
                            rollbackFailures.Add(new InvalidOperationException(
                                $"Could not safely remove created folder '{folder}'.", e));
                        }
                    }

                    if (rollbackFailures.Count > 0)
                    {
                        rollbackFailures.Insert(0, failure);
                        throw new AggregateException(
                            "[LightSide] Asset-action rollback was incomplete.", rollbackFailures);
                    }
                    throw;
                }
            }

            /// <summary>
            /// Destroys queued objects that never became assets. Returns the given failure (possibly none),
            /// combined with any cleanup failure.
            /// </summary>
            public Exception Discard(Exception failure)
            {
                var cleanupFailures = new List<Exception>();
                foreach (var (asset, _) in creates)
                    try
                    {
                        if (asset != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                            UnityEngine.Object.DestroyImmediate(asset);
                    }
                    catch (Exception e)
                    {
                        cleanupFailures.Add(e);
                    }
                if (cleanupFailures.Count == 0) return failure;
                if (failure != null) cleanupFailures.Insert(0, failure);
                return new AggregateException(
                    "[LightSide] Migration failed and queued objects could not be fully discarded.",
                    cleanupFailures);
            }

            void Validate(out Dictionary<string, string> moveGuids, out string deleteGuid)
            {
                if (deletes.Count > 1)
                    throw new InvalidOperationException(
                        "[LightSide] One migration transaction cannot delete more than one asset.");

                moveGuids = new Dictionary<string, string>(StringComparer.Ordinal);
                deleteGuid = null;
                var targets = new HashSet<string>(StringComparer.Ordinal);
                foreach (var (_, path) in creates)
                {
                    if (string.IsNullOrEmpty(path) || !targets.Add(path) || !IsVacantAssetPath(path))
                        throw new InvalidOperationException(
                            $"[LightSide] Cannot create migration asset '{path}'.");
                }
                foreach (var (from, to) in moves)
                {
                    EnsurePersisted(from);
                    var guid = AssetDatabase.AssetPathToGUID(from);
                    if (string.IsNullOrEmpty(guid) || !moveGuids.TryAdd(from, guid) ||
                        string.IsNullOrEmpty(to) || !targets.Add(to) || !IsVacantAssetPath(to))
                        throw new InvalidOperationException(
                            $"[LightSide] Cannot move migration asset '{from}' to '{to}'.");
                }
                foreach (var path in deletes)
                {
                    EnsurePersisted(path);
                    var guid = AssetDatabase.AssetPathToGUID(path);
                    if (string.IsNullOrEmpty(path) || !targets.Add(path) || string.IsNullOrEmpty(guid))
                        throw new InvalidOperationException(
                            $"[LightSide] Cannot delete missing migration asset '{path}'.");
                    deleteGuid = guid;
                }
            }

            static bool IsVacantAssetPath(string assetPath)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath))) return false;
                var fsPath = ProjectYamlFiles.ToFsPath(assetPath);
                return fsPath != null && !File.Exists(fsPath) && !File.Exists(fsPath + ".meta") &&
                       !Directory.Exists(fsPath);
            }

            static void EnsureParentFolder(string assetPath,
                List<(string path, string guid)> createdFolders)
            {
                var folder = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
                EnsureFolder(folder, createdFolders);
            }

            static void EnsureFolder(string folder,
                List<(string path, string guid)> createdFolders)
            {
                if (AssetDatabase.IsValidFolder(folder)) return;
                if (!IsVacantAssetPath(folder))
                    throw new InvalidOperationException(
                        $"[LightSide] Migration asset folder '{folder}' is occupied outside AssetDatabase.");

                var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(parent))
                    throw new InvalidOperationException(
                        $"[LightSide] Cannot create asset folder '{folder}'.");

                EnsureFolder(parent, createdFolders);
                if (!IsVacantAssetPath(folder))
                    throw new InvalidOperationException(
                        $"[LightSide] Migration asset folder '{folder}' became occupied.");
                var guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
                if (!string.IsNullOrEmpty(guid)) createdFolders.Add((folder, guid));
                if (string.IsNullOrEmpty(guid) || !AssetDatabase.IsValidFolder(folder) ||
                    AssetDatabase.AssetPathToGUID(folder) != guid)
                    throw new InvalidOperationException(
                        $"[LightSide] Failed to create asset folder '{folder}'.");
            }
        }
    }
}
