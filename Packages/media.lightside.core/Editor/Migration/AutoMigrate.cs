using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Schedules migration passes on package registration and on completed asset imports. A stale package
    /// stamp — or a project copy that has never run a pass — takes the full pass; otherwise assets that just
    /// entered the project, plus the deferred queue, take an incremental one. Assets holding unsaved editor
    /// changes are deferred to that queue and retried every pass until saved. Batch mode never runs a pass
    /// and a build defers one, and package stamps advance only after every migration succeeds.
    /// </summary>
    [InitializeOnLoad]
    internal static class AutoMigrate
    {
        const int ListedUninspected = 10;

        static bool scheduled;
        static readonly HashSet<string> importedSincePass = new(StringComparer.Ordinal);

        static AutoMigrate() => Events.registeredPackages += _ => Schedule();

        internal static void Schedule()
        {
            if (Application.isBatchMode || scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += RunScheduled;
        }

        /// <summary>
        /// Records assets that just entered the project so the next pass reaches them while every package
        /// stamp is current. Legacy data also arrives through branches, package imports and restores, which
        /// no version change announces.
        /// </summary>
        internal static void NoteImported(IReadOnlyList<string> assetPaths)
        {
            if (Application.isBatchMode) return;
            for (int i = 0; i < assetPaths.Count; i++)
                importedSincePass.Add(assetPaths[i]);
            Schedule();
        }

        /// <summary>
        /// Defers the pass while a player or AssetBundle build runs: a pass rewrites assets, and the
        /// editor refuses asset mutation once a build has taken over. The pass stays scheduled, so the
        /// deferral costs nothing but the wait.
        /// </summary>
        static void RunScheduled()
        {
            if (BuildPipeline.isBuildingPlayer)
            {
                EditorApplication.delayCall += RunScheduled;
                return;
            }

            scheduled = false;
            try { Run(); }
            catch (Exception e)
            {
                try { AssetDatabase.Refresh(); }
                catch (Exception refreshFailure) { Debug.LogException(refreshFailure); }
                Debug.LogException(e);
                Debug.LogError(
                    "[LightSide] Automatic migration aborted. Package versions were not advanced; " +
                    "fix the reported problem and reload the scripting domain to retry.");
            }
        }

        static void Run()
        {
            var ledger = MigrationLedger.Load();
            MigrationRunner.Recover(ledger);

            var packages = DiscoverPackages();
            if (packages.Count == 0) return;

            var imported = new List<string>(importedSincePass);
            var pendingReadable = MigrationPending.TryLoad(out var pending);
            var stale = new List<string>();
            foreach (var (name, version) in packages)
                if (ledger.StampOf(name) != version)
                    stale.Add($"{name} {version}");

            if (stale.Count > 0 || !MigrationIndex.Exists || !pendingReadable)
                RunFull(ledger, packages, stale, pending);
            else if (imported.Count > 0 || pending.Count > 0)
                RunIncremental(imported, pending);
            else
                return;

            importedSincePass.ExceptWith(imported);
        }

        static void RunFull(MigrationLedger ledger,
            List<(string name, string version)> packages, List<string> stale, HashSet<string> pending)
        {
            var target = string.Join(", ", stale.Count > 0 ? stale : Describe(packages));
            var result = MigrationRunner.RunAll(ledger);
            ReportUninspected(result.Uninspected);
            ReportDeferred(result.Deferred, pending);
            if (!result.Succeeded)
            {
                PersistPending(result.Deferred, pending);
                foreach (var failure in result.Failures)
                    Debug.LogException(failure);
                AssetDatabase.Refresh();
                Debug.LogError(
                    $"[LightSide] {result.Failures.Count} migration error(s) occurred while updating to " +
                    $"{target}. Successful migrations were kept, but package versions were not advanced. " +
                    "Fix the reported assets and reload the scripting domain to retry.");
                return;
            }

            MigrationPending.Save(result.Deferred);
            if (result.Changes > 0)
                AssetDatabase.Refresh();

            var stamped = false;
            foreach (var (name, version) in packages)
                stamped |= ledger.Stamp(name, version);
            if (stamped) ledger.Save();

            if (result.Changes > 0)
                Debug.Log($"[LightSide] Migrated {result.Changes} asset change(s) to {target}. " +
                          "Review the VCS diff before committing.");
        }

        static void RunIncremental(List<string> imported, HashSet<string> pending)
        {
            var attempt = new HashSet<string>(imported, StringComparer.Ordinal);
            attempt.UnionWith(pending);
            var result = MigrationRunner.RunImported(new List<string>(attempt));
            ReportDeferred(result.Deferred, pending);
            if (!result.Succeeded)
            {
                PersistPending(result.Deferred, pending);
                foreach (var failure in result.Failures)
                    Debug.LogException(failure);
                AssetDatabase.Refresh();
                Debug.LogError(
                    $"[LightSide] {result.Failures.Count} migration error(s) occurred while migrating " +
                    "imported assets. Fix the reported assets and reimport them to retry.");
                return;
            }

            MigrationPending.Save(result.Deferred);
            if (result.Changes == 0) return;
            AssetDatabase.Refresh();
            Debug.Log($"[LightSide] Migrated {result.Changes} asset change(s) in imported assets. " +
                      "Review the VCS diff before committing.");
        }

        /// <summary>
        /// A failed pass keeps its previous queue: entries the pass never reached must survive the failure,
        /// and stale entries cost nothing — a retry of a migrated asset is a no-op.
        /// </summary>
        static void PersistPending(IReadOnlyList<string> deferred, HashSet<string> pending)
        {
            var merged = new HashSet<string>(pending, StringComparer.Ordinal);
            merged.UnionWith(deferred);
            MigrationPending.Save(merged);
        }

        /// <summary>
        /// Warns about assets newly deferred for unsaved editor changes. Assets already queued stay silent:
        /// the queue retries every pass, and saving the asset migrates it without any action here.
        /// </summary>
        static void ReportDeferred(IReadOnlyList<string> deferred, HashSet<string> known)
        {
            var fresh = new List<string>();
            foreach (var assetPath in deferred)
                if (!known.Contains(assetPath))
                    fresh.Add(assetPath);
            if (fresh.Count == 0) return;

            var message = new StringBuilder("[LightSide] ")
                .Append(fresh.Count)
                .Append(" asset(s) have unsaved editor changes and were left unmigrated. ")
                .Append("They are migrated automatically once saved:");
            for (int i = 0; i < fresh.Count && i < ListedUninspected; i++)
                message.Append("\n  ").Append(fresh[i]);
            if (fresh.Count > ListedUninspected)
                message.Append("\n  and ").Append(fresh.Count - ListedUninspected).Append(" more.");
            Debug.LogWarning(message.ToString());
        }

        /// <summary>
        /// Warns about sources the pass could not read. They stay outside the index's coverage, so a source
        /// that becomes readable is inspected and migrated by a later pass without any action here.
        /// </summary>
        static void ReportUninspected(IReadOnlyList<string> uninspected)
        {
            if (uninspected.Count == 0) return;

            var message = new StringBuilder("[LightSide] ")
                .Append(uninspected.Count)
                .Append(" asset(s) could not be read and were left uninspected; migrations that belong to them ")
                .Append("were not applied. Re-save them as text-serialized assets to have a later pass cover them:");
            for (int i = 0; i < uninspected.Count && i < ListedUninspected; i++)
                message.Append("\n  ").Append(uninspected[i]);
            if (uninspected.Count > ListedUninspected)
                message.Append("\n  and ").Append(uninspected.Count - ListedUninspected).Append(" more.");
            Debug.LogWarning(message.ToString());
        }

        static List<string> Describe(List<(string name, string version)> packages)
        {
            var result = new List<string>(packages.Count);
            foreach (var (name, version) in packages)
                result.Add($"{name} {version}");
            return result;
        }

        static List<(string name, string version)> DiscoverPackages()
        {
            var result = new List<(string name, string version)>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var type in TypeCache.GetTypesDerivedFrom<IMigratedPackage>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                IMigratedPackage package;
                try { package = (IMigratedPackage)Activator.CreateInstance(type); }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"[LightSide] Package registration '{type.FullName}' could not be instantiated.", e);
                }

                if (string.IsNullOrEmpty(package.PackageName) || string.IsNullOrEmpty(package.Version))
                    throw new InvalidOperationException(
                        $"[LightSide] Package registration '{type.FullName}' has no name or version.");
                if (!names.Add(package.PackageName))
                    throw new InvalidOperationException(
                        $"[LightSide] More than one package registration uses '{package.PackageName}'.");
                result.Add((package.PackageName, package.Version));
            }
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }
    }
}
