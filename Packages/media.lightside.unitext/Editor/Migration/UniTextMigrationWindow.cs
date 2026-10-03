using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal partial class UniTextMigrationWindow : EditorWindow
    {
        /// <summary>
        /// The namespace whose presence in a source file means the assembly compiling it needs the
        /// UniText reference. Matched as plain text, so a mention inside a comment counts too — a
        /// reference added for one costs nothing, a reference withheld costs the whole compilation.
        /// </summary>
        private const string UniTextNamespace = "LightSide";

        private enum Tab
        {
            Dashboard,
            Analysis,
            FontMapping,
            ScriptPreview,
            Losses,
            Settings,
            Log,
        }

        private static readonly string[] tabLabels =
        {
            "Dashboard",
            "Analysis",
            "Font Mapping",
            "Script Preview",
            "Not carried over",
            "Settings",
            "Log",
        };

        private static readonly string[] typeFilterOptions =
        {
            "All",
            "Component",
            "Script",
            "Font",
            "Material",
            "Animation",
            "AssemblyDef",
            "RichText",
            "MissingScript",
            "Compiled",
        };

        private static readonly string[] statusFilterOptions =
        {
            "All",
            "Pending",
            "Completed",
            "Skipped",
            "Failed",
        };

        private static readonly string[] logFilterOptions =
        {
            "All",
            "Info",
            "Warning",
            "Error",
        };

        private Tab currentTab;
        private ProjectAnalyzer analyzer;
        private ComponentMigrator componentMigrator;
        private MigrationStateData stateData;
        private FontMappingsData fontMappingsData;
        private MigrationSessionData session = new();
        private List<MigrationFinding> findings = new();
        private List<LogEntry> logEntries = new();
        private Dictionary<string, int> prefabRank;
        private MigrationSummary summary;
        private FindingType? filterType;
        private MigrationStatus? filterStatus;

        /// <summary>Folder the tree narrows the list to, or null for the whole project.</summary>
        private string filterFolder;
        private string searchText = string.Empty;
        private int selectedScriptIndex = -1;
        private readonly List<string> scriptFiles = new();
        private List<ScriptReplacement> currentReplacements = new();
        private string currentDiff = string.Empty;
        private LogSeverity? logFilter;
        private string loadFailure;
        private string loadFailurePath;
        private int unverifiedSkipped = -1;
        private MigrationLossesData lossesData = new();

        /// <summary>Opens the project migration workspace.</summary>
        [MenuItem(UniTextMenu.Tools.Migration)]
        public static void ShowWindow()
        {
            var window = GetWindow<UniTextMigrationWindow>("UniText Migration");
            window.minSize = new Vector2(720f, 520f);
        }

        private void OnEnable()
        {
            loadFailure = null;
            loadFailurePath = null;
            try
            {
                loadFailurePath = MigrationStateData.FilePath;
                stateData = MigrationStateData.Load();
                loadFailurePath = FontMappingsData.FilePath;
                fontMappingsData = FontMappingsData.Load();
            }
            catch (InvalidDataException exception)
            {
                loadFailure = exception.InnerException == null
                    ? exception.Message
                    : $"{exception.Message}\n\n{exception.InnerException.Message}";
                stateData = null;
                fontMappingsData = null;
                return;
            }

            loadFailurePath = null;
            lossesData = MigrationLossesData.Load();
            session = MigrationSessionData.Load();
            findings = new List<MigrationFinding>(session.findings);
            logEntries = session.log;
            prefabRank = null;
            RestoreStatuses();
        }

        private void OnDisable()
        {
            if (analyzer != null && analyzer.IsScanning) analyzer.Cancel();
            if (stateData == null) return;
            stateData.SaveIfDirty();
            session.Save();
        }

        /// <summary>
        /// Deletes the document that would not load and reopens the workspace. Every recorded
        /// status, manual review or font mapping it held is gone; project assets are untouched.
        /// </summary>
        private void ResetUnreadableDocument()
        {
            var path = loadFailurePath;
            if (!EditorUtility.DisplayDialog("Reset the migration document",
                    $"Deletes '{path}'.\n\nEverything it recorded — statuses, manual reviews or " +
                    "font mappings — is lost, and the project needs a fresh scan. No asset in " +
                    "the project is touched.", "Delete", "Cancel"))
                return;

            File.Delete(path);
            OnEnable();
            CreateGUI();
        }

        /// <summary>Builds the retained-mode migration workspace.</summary>
        public void CreateGUI()
        {
            CreateToolkitGUI();
        }

        private void RestoreStatuses()
        {
            stateData.RestoreFindings(findings);
            for (var i = 0; i < findings.Count; i++)
            {
                findings[i].isSelected = false;
            }
        }

        /// <summary>
        /// Notes on each assembly definition whether the migration needs it changed. An assembly
        /// needs the UniText reference from the moment one of the scripts it compiles names UniText
        /// types; one whose scripts none of them reaches keeps its TMP reference untouched.
        /// </summary>
        private void AnnotateAssemblyDefinitions()
        {
            var assemblies = AssemblyDefinitionFolders(AssemblyDefinitionPaths());

            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.AssemblyDef ||
                    string.IsNullOrEmpty(finding.filePath)) continue;

                finding.warnings = new List<string>
                {
                    CompilesUniTextCode(finding.filePath, assemblies)
                        ? "Scripts this assembly compiles name UniText types. The run adds the " +
                          "UniText assembly reference and leaves the TMP one in place."
                        : "No script this assembly compiles names UniText types, so its TMP " +
                          "reference does not have to change for the migration — the run skips it.",
                    "An assembly that ships inside a package is left alone: it is not writable, " +
                    "its own scripts need TMP, and an edit would be lost on the next update.",
                };
            }
        }

        /// <summary>Every assembly definition in the project, as asset paths.</summary>
        private static List<string> AssemblyDefinitionPaths()
        {
            var guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
            var paths = new List<string>(guids.Length);
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path)) paths.Add(path.Replace('\\', '/'));
            }
            return paths;
        }

        /// <summary>
        /// The folder of each assembly definition, longest path first, so the first one a file sits
        /// under is the assembly that compiles it.
        /// </summary>
        private static string[] AssemblyDefinitionFolders(List<string> paths)
        {
            var folders = new List<string>(paths.Count);
            for (var i = 0; i < paths.Count; i++)
            {
                var folder = Path.GetDirectoryName(paths[i])?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder)) folders.Add(folder + "/");
            }
            folders.Sort((left, right) => right.Length.CompareTo(left.Length));
            return folders.ToArray();
        }

        /// <summary>The assembly definition row for <paramref name="asmdefPath"/>, when the scan raised one.</summary>
        private MigrationFinding AssemblyFinding(string asmdefPath)
        {
            for (var i = 0; i < findings.Count; i++)
            {
                if (findings[i].type == FindingType.AssemblyDef &&
                    string.Equals(findings[i].filePath?.Replace('\\', '/'), asmdefPath,
                        StringComparison.OrdinalIgnoreCase))
                    return findings[i];
            }
            return null;
        }

        /// <summary>
        /// The folder of the assembly definition that compiles <paramref name="assetPath"/>, or
        /// null when no assembly definition claims it and the file belongs to a predefined assembly.
        /// </summary>
        private static string OwningAssemblyFolder(string assetPath, string[] assemblies)
        {
            for (var i = 0; i < assemblies.Length; i++)
            {
                if (assetPath.StartsWith(assemblies[i], StringComparison.OrdinalIgnoreCase))
                    return assemblies[i];
            }
            return null;
        }

        /// <summary>
        /// Whether the assembly definition at <paramref name="asmdefPath"/> compiles a script that
        /// names UniText — one this run is about to rewrite, or one an earlier run already did.
        /// </summary>
        /// <remarks>
        /// A rewritten script no longer names TMP and so raises no finding: reading the sources is
        /// what keeps a run that follows an interrupted one from concluding there is nothing to do.
        /// A nested assembly definition owns its own subtree, exactly as Unity resolves ownership.
        /// </remarks>
        private bool CompilesUniTextCode(string asmdefPath, string[] assemblies)
        {
            var directory = Path.GetDirectoryName(asmdefPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory)) return false;
            var owner = directory + "/";

            for (var at = 0; at < findings.Count; at++)
            {
                var finding = findings[at];
                if (finding.type != FindingType.ScriptReference ||
                    finding.status == MigrationStatus.Completed ||
                    finding.filePath == null) continue;
                if (string.Equals(OwningAssemblyFolder(finding.filePath.Replace('\\', '/'), assemblies),
                        owner, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            if (!Directory.Exists(directory)) return false;
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var path = file.Replace('\\', '/');
                if (!string.Equals(OwningAssemblyFolder(path, assemblies), owner,
                        StringComparison.OrdinalIgnoreCase)) continue;

                string text;
                try { text = File.ReadAllText(path); }
                catch { continue; }
                if (text.IndexOf(UniTextNamespace, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// How many assembly definitions the run is about to give the UniText reference to. Counts
        /// what <see cref="MigrateAssemblyDefinitions"/> will actually write, so the figure the
        /// dialog shows and the work the run does cannot drift apart.
        /// </summary>
        private int AssemblyDefinitionsNeedingReference()
        {
            var paths = AssemblyDefinitionPaths();
            var assemblies = AssemblyDefinitionFolders(paths);
            var count = 0;
            for (var i = 0; i < paths.Count; i++)
            {
                if (!IsProjectAsset(paths[i]) ||
                    !CompilesUniTextCode(paths[i], assemblies)) continue;

                string content;
                try { content = File.ReadAllText(paths[i]); }
                catch { continue; }
                if (!AssemblyDefinitionMigrator.ReferencesUniText(content)) count++;
            }
            return count;
        }

        /// <summary>
        /// Points every assembly definition under <c>Assets</c> that compiles UniText-naming code at
        /// the UniText assembly, and skips the rest. Returns how many refused the write and so still
        /// stand between the run and the script rewrite.
        /// </summary>
        /// <remarks>
        /// The pass walks the project's assembly definitions rather than the scan's findings: a
        /// finding is raised only for an assembly that spells <c>Unity.TextMeshPro</c> in its
        /// references, while one that reaches TMP through a GUID — or through another assembly —
        /// compiles rewritten scripts just the same and needs the reference just as much. Nothing
        /// outside <c>Assets</c> is considered: a script the migration rewrites lives under
        /// <c>Assets</c>, and its assembly is an ancestor folder, so it does too. Adding a reference
        /// that is already there writes nothing.
        /// </remarks>
        private int MigrateAssemblyDefinitions()
        {
            var paths = AssemblyDefinitionPaths();
            var assemblies = AssemblyDefinitionFolders(paths);
            var added = 0;
            var skipped = 0;
            var blocked = 0;

            for (var i = 0; i < paths.Count; i++)
            {
                var asmdefPath = paths[i];
                if (!IsProjectAsset(asmdefPath)) continue;

                string manifest;
                try { manifest = File.ReadAllText(asmdefPath); }
                catch { continue; }
                if (AssemblyDefinitionMigrator.ReferencesUniText(manifest)) continue;

                var finding = AssemblyFinding(asmdefPath);

                if (!CompilesUniTextCode(asmdefPath, assemblies))
                {
                    if (finding == null || finding.status != MigrationStatus.NotStarted) continue;
                    stateData.SetStatus(finding, MigrationStatus.Skipped);
                    skipped++;
                    continue;
                }

                var (ok, backupPath, error) =
                    AssemblyDefinitionMigrator.AddUniTextReference(asmdefPath, true);
                if (!ok)
                {
                    blocked++;
                    Log(LogSeverity.Error,
                        $"Assembly definition rewrite failed: {asmdefPath} — {error}", backupPath);
                    continue;
                }

                if (finding != null) stateData.SetStatus(finding, MigrationStatus.Completed);
                if (backupPath == null) continue;

                added++;
                Log(LogSeverity.Info,
                    $"Assembly definition now references UniText: {asmdefPath}", backupPath);
            }

            if (added > 0)
                Log(LogSeverity.Info,
                    $"{added} assembly definition(s) now reference UniText; originals kept as " +
                    ".bak. The TMP reference was left in place — a script the rewrite does not " +
                    "reach still compiles against it.");
            if (skipped > 0)
                Log(LogSeverity.Info,
                    $"{skipped} assembly definition(s) were skipped: no script they compile names " +
                    "UniText types.");
            return blocked;
        }

        /// <summary>Whether the path names an asset this project owns and may rewrite.</summary>
        private static bool IsProjectAsset(string assetPath)
        {
            return assetPath.Replace('\\', '/')
                .StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns to Pending every component the scan just found that a stored status calls
        /// migrated. The scan only reports a component whose TMP script GUID is in the file, so
        /// such a status is provably wrong however it got there — an interrupted write, a revert,
        /// an edit made outside the tool. Only findings the scan itself produced are judged:
        /// reviews recovered from the state file describe assets the scan no longer sees.
        /// </summary>
        private void ReconcileScannedComponents(List<MigrationFinding> scanned)
        {
            var corrected = 0;
            for (var i = 0; i < scanned.Count; i++)
            {
                var finding = scanned[i];
                if (finding.type != FindingType.Component ||
                    finding.status != MigrationStatus.Completed) continue;
                stateData.SetStatus(finding, MigrationStatus.NotStarted);
                corrected++;
            }
            if (corrected == 0) return;

            Log(LogSeverity.Warning,
                $"{corrected} component(s) were recorded as migrated, but this scan found their " +
                "TMP component still in the file. They are Pending again — the record was ahead " +
                "of what is on disk.");
        }

        private void Log(LogSeverity severity, string message, string backupPath = null)
        {
            logEntries.Add(new LogEntry(severity, message) { backupPath = backupPath });
        }

        private void StartScan()
        {
            displayedScanProgress = -1f;
            analyzer = new ProjectAnalyzer(stateData.excludedPaths, session);
            analyzer.StartScan(() =>
            {
                session.findings = analyzer.Findings;
                session.prefabOrder = analyzer.GetPrefabMigrationOrder();
                session.sharedTags = analyzer.SharedVocabularyTags.ToList();
                session.scannedFiles = analyzer.ScannedFiles;
                session.partialScan = analyzer.WasInterrupted;
                session.scanFailure = analyzer.FailureMessage;
                findings = new List<MigrationFinding>(session.findings);
                prefabRank = null;
                RestoreStatuses();
                ReconcileScannedComponents(analyzer.Findings);
                AnnotateAssemblyDefinitions();
                MergeDiscoveredFonts();
                SyncFontFindings();
                stateData.lastScanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                stateData.Save();
                session.scanTime = stateData.lastScanTime;
                Log(analyzer.Failed ? LogSeverity.Error : LogSeverity.Info,
                    $"Scan complete: {findings.Count} finding(s) " +
                    $"({(analyzer.WasInterrupted ? "interrupted" : "full")})");
                if (analyzer.Failed)
                    Log(LogSeverity.Error, $"The scan stopped early: {analyzer.FailureMessage}");
                if (analyzer.UnreadableFiles > 0)
                    Log(LogSeverity.Error,
                        $"{analyzer.UnreadableFiles} asset(s) could not be read and are listed as " +
                        "unreadable findings. Nothing is known about the TMP usage inside them.");
                if (analyzer.ReusedFiles > 0)
                    Log(LogSeverity.Info,
                        $"{analyzer.ReusedFiles} unchanged file(s) answered from the previous scan " +
                        $"— {analyzer.ReusedBytes / (1024 * 1024)} MB not re-read.");
                session.Save();
                RenderToolkitTab();
            });
            RenderToolkitTab();
        }

        /// <summary>
        /// Folds the scan's fonts into the mapping table. A font already mapped keeps the choices
        /// made for it and takes only what the project can have changed since: its path and its
        /// fallback chain.
        /// </summary>
        private void MergeDiscoveredFonts()
        {
            for (var i = 0; i < analyzer.DiscoveredFonts.Count; i++)
            {
                var discovered = analyzer.DiscoveredFonts[i];
                var existing = fontMappingsData.fontMappings.Find(entry =>
                    entry.tmpFontGuid == discovered.tmpFontGuid);
                if (existing == null)
                {
                    fontMappingsData.fontMappings.Add(discovered);
                    continue;
                }
                existing.tmpFontPath = discovered.tmpFontPath;
                existing.fallbackGuids = discovered.fallbackGuids;
                if (string.IsNullOrEmpty(existing.sourceTtfPath))
                    existing.sourceTtfPath = discovered.sourceTtfPath;
            }
            fontMappingsData.globalFallbackGuids = analyzer.GlobalFallbackFontGuids.ToList();
            fontMappingsData.Save();
        }

        private void VerifyMigrations()
        {
            var inconsistencies = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.status != MigrationStatus.Completed ||
                    finding.type != FindingType.Component ||
                    string.IsNullOrEmpty(finding.scriptGuid))
                    continue;
                try
                {
                    if (!File.ReadAllText(finding.filePath).Contains(finding.scriptGuid)) continue;
                    stateData.SetStatus(finding, MigrationStatus.NotStarted);
                    inconsistencies++;
                }
                catch (Exception exception)
                {
                    Log(LogSeverity.Warning,
                        $"Could not verify {finding.filePath}: {exception.Message}");
                }
            }
            Log(inconsistencies > 0 ? LogSeverity.Warning : LogSeverity.Info,
                inconsistencies > 0
                    ? $"Verification reset {inconsistencies} stale completed item(s) to Pending."
                    : "Verification: every completed migration is still in place.");
            CommitOperation();
        }

        private void MigrateSimpleComponents()
        {
            var simple = findings
                .Where(finding => finding.type == FindingType.Component &&
                                  finding.status == MigrationStatus.NotStarted &&
                                  finding.complexity == MigrationComplexity.Simple &&
                                  MigrationMapping.IsMigratableComponent(finding.scriptGuid))
                .ToList();
            MigrateComponentFiles(
                OrderComponentFiles(simple.Select(finding => finding.filePath)),
                new HashSet<string>(simple.Select(finding => finding.id), StringComparer.Ordinal));
        }

        private void MigrateAllComponents()
        {
            MigrateComponentFiles(PendingComponentFiles());
        }

        /// <summary>
        /// Runs the whole automatic path in the order the stages require: fonts, then every
        /// pending component, then the script rewrite. Stops at the first stage that cannot
        /// finish, so the ordering guarantee is never broken half-way.
        /// </summary>
        private void MigrateEverything()
        {
            var fonts = CreatableFontCount();
            var components = PendingComponentFiles();
            var scripts = PendingScriptFiles();
            var manual = ManualPendingCount();

            if (fonts == 0 && components.Count == 0 && scripts.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing left to do automatically",
                    manual == 0
                        ? "Every finding is migrated or skipped."
                        : $"{manual} finding(s) remain, and every one of them needs hands.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Run every automatic step",
                    $"Create {fonts} font asset(s) from their source files.\n" +
                    $"Rewrite {components.Count} prefab(s) and scene(s).\n" +
                    $"Add the UniText reference to {AssemblyDefinitionsNeedingReference()} " +
                    "assembly definition(s), keeping the TMP one.\n" +
                    $"Rewrite {scripts.Count} C# file(s), each keeping a .bak beside it.\n\n" +
                    "A TMP_InputField is converted too: the field box becomes UniTextEditable, " +
                    "its text child gains UniTextSelectable, the placeholder folds in as a " +
                    "PlaceholderDecorator, and the TMP_InputField is removed. Settings with no " +
                    "UniText counterpart do not stop it — the field migrates and each value is " +
                    "listed under Not carried over. Dropdowns, materials, animation curves and " +
                    "rich-text assets are never touched and stay on the list." + "\n\n" +
                    "A component that declares RequireComponent for a TMP type — a TMP-specific " +
                    "text animator or localiser — cannot be satisfied by UniText and is REMOVED " +
                    "from its object, together with whatever required it in turn. Everything each " +
                    "one was configured with is written to the Removed tab and to " +
                    "ProjectSettings/UniText/RemovedComponents.json; the components themselves do " +
                    "not come back.\n\n" +
                    $"{manual} finding(s) have no automatic path and are left untouched.\n\n" +
                    "Assets are edited in place — commit to version control first.",
                    "Run", "Cancel"))
                return;

            CreateMissingFonts();
            if (!IsFontMappingComplete())
            {
                var unmapped = UnmappedFontCount();
                Log(LogSeverity.Warning, $"Stopped before components: {unmapped} " +
                                         "TMP font(s) still have no UniText font stack. Assign or " +
                                         "skip them in Font Mapping, then run again.");
                EditorUtility.DisplayDialog("Stopped before components",
                    $"{unmapped} TMP font(s) still have no UniText font stack, and the tool could " +
                    "not build one for them. Migrating now would leave those components without " +
                    "a font.\n\nIn Font Mapping, give each one a Font stack — the Font field " +
                    "alone is not enough, a component reads the stack — or press Skip. Then run " +
                    "again. The Log names what stopped each row.", "OK");
                CommitOperation();
                return;
            }

            var completedBefore =
                CountByTypeAndStatus(FindingType.Component, MigrationStatus.Completed);
            var componentsRan = RunComponentMigration(components, out var refused);
            if (components.Count > 0 &&
                CountByTypeAndStatus(FindingType.Component, MigrationStatus.Completed) ==
                completedBefore)
                Log(LogSeverity.Warning,
                    $"The component stage opened {components.Count} file(s) and completed no " +
                    "component. The entries above say what each file held.");
            if (!componentsRan)
            {
                if (refused.Count > 0)
                {
                    var named = string.Join("\n", refused.GetRange(0, Math.Min(8, refused.Count)));
                    if (refused.Count > 8) named += $"\n… and {refused.Count - 8} more";
                    Log(LogSeverity.Error,
                        $"Stopped before scripts: {refused.Count} asset(s) were refused and the " +
                        "script rewrite was not reached.");
                    EditorUtility.DisplayDialog("Stopped before scripts",
                        $"{refused.Count} asset(s) could not be migrated, so the script rewrite " +
                        "was not started — rewriting now could drop serialized references those " +
                        $"components still need.\n\n{named}\n\nThe Log holds the reason for each " +
                        "one. When a reason names a different asset — one nothing can read — that " +
                        "asset is what blocks these: exclude it, press Re-check all failed, and " +
                        "run again. An asset you would rather migrate yourself can be excluded " +
                        "from its own row.", "OK");
                }
                CommitOperation();
                return;
            }

            var componentReason = ScriptRewriteBlockReason();
            if (componentReason != null)
            {
                Log(LogSeverity.Warning, $"Stopped before scripts: {componentReason}");
                EditorUtility.DisplayDialog("Stopped before scripts", componentReason +
                    "\n\nNothing is lost — run this again once it is resolved and it picks up " +
                    "where it stopped.", "OK");
                CommitOperation();
                return;
            }

            var outstanding = ComponentsOutstandingNote();
            if (outstanding != null) Log(LogSeverity.Warning, outstanding);

            var assembliesBlocked = MigrateAssemblyDefinitions();
            if (assembliesBlocked > 0 && scripts.Count > 0)
            {
                Log(LogSeverity.Warning,
                    $"Stopped before scripts: {assembliesBlocked} assembly definition(s) could " +
                    "not be pointed at UniText.");
                EditorUtility.DisplayDialog("Stopped before scripts",
                    $"{assembliesBlocked} assembly definition(s) could not be given the UniText " +
                    "reference — they live outside Assets or refused the write. A rewritten " +
                    "script names UniText types, and its assembly cannot see them until the " +
                    "reference is there — the project would not compile.\n\nThe Log names each " +
                    "one. Skip the rows that do not matter, then run again.", "OK");
                CommitOperation();
                return;
            }

            ApplyScriptFiles(scripts);
            Log(LogSeverity.Info, $"Automatic run finished. {ManualPendingCount()} finding(s) " +
                                  "remain for hand work.");
            AssetDatabase.Refresh();
            CommitOperation();
        }

        private void MigrateSelected()
        {
            MigrateComponentFiles(OrderComponentFiles(findings
                .Where(IsMigratableSelection)
                .Select(finding => finding.filePath)));
        }

        private void MigrateFindingFile(MigrationFinding finding)
        {
            MigrateComponentFiles(new List<string> { finding.filePath });
        }

        private void RecheckFinding(MigrationFinding finding)
        {
            if (finding.type != FindingType.Component ||
                finding.status != MigrationStatus.Failed ||
                !PrepareComponentMigration(false)) return;
            componentMigrator.RecheckFinding(finding, findings);
            CommitOperation();
        }

        /// <summary>
        /// Files a component pass can still change. A pending finding the migrator has no target
        /// for — a TMP_Dropdown — is left out: re-queuing its file would rewrite nothing and keep
        /// the run circling the same asset forever.
        /// </summary>
        /// <summary>
        /// Re-runs the migration's own gates against every failed component, one asset at a time.
        /// Each finding keeps whatever verdict its asset now supports; nothing is written.
        /// </summary>
        private void RecheckAllFailed()
        {
            if (!PrepareComponentMigration(false)) return;

            var pending = new List<MigrationFinding>();
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type == FindingType.Component &&
                    finding.status == MigrationStatus.Failed)
                    pending.Add(finding);
            }
            if (pending.Count == 0) return;
            pending.Sort((a, b) => string.CompareOrdinal(a.filePath, b.filePath));

            var cleared = 0;
            try
            {
                for (var i = 0; i < pending.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            $"Re-checking failed components ({i + 1}/{pending.Count})",
                            pending[i].filePath, (float)i / pending.Count))
                    {
                        Log(LogSeverity.Warning,
                            "Re-check cancelled; the rows already checked keep their new state.");
                        break;
                    }
                    componentMigrator.RecheckFinding(pending[i], findings);
                    if (pending[i].status != MigrationStatus.Failed) cleared++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Log(LogSeverity.Info,
                $"Re-checked {pending.Count} failed component(s); {cleared} no longer blocked.");
            CommitOperation();
        }

        /// <summary>Closes every recorded component review without touching any asset.</summary>
        private void MarkAllFailedHandled()
        {
            var count = CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed);
            if (count == 0) return;
            if (!EditorUtility.DisplayDialog("Mark every failed component handled",
                    $"Closes the recorded review on {count} component(s) without checking " +
                    "anything.\n\nAny of them whose TMP component is still in its asset returns " +
                    "to Pending the next time the script stage is judged.", "Mark handled",
                    "Cancel"))
                return;

            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.Component ||
                    finding.status != MigrationStatus.Failed) continue;
                stateData.SetStatus(finding, MigrationStatus.Skipped);
            }
            Log(LogSeverity.Warning, $"Marked {count} failed component(s) handled by hand.");
            CommitOperation();
        }

        private List<string> PendingComponentFiles()
        {
            return OrderComponentFiles(findings
                .Where(finding => finding.type == FindingType.Component &&
                                  finding.status == MigrationStatus.NotStarted &&
                                  MigrationMapping.IsMigratableComponent(finding.scriptGuid))
                .Select(finding => finding.filePath));
        }

        /// <summary>
        /// Runs component migration over whole asset files: a prefab or scene is rewritten once,
        /// covering every finding it contains.
        /// </summary>
        private void MigrateComponentFiles(List<string> files,
            HashSet<string> allowedFindingIds = null)
        {
            RunComponentMigration(files, out _, allowedFindingIds);
            CommitOperation();
        }

        /// <summary>
        /// Rewrites every named file and reports whether the batch can be built on. False means a
        /// file was refused or the user cancelled — <paramref name="refused"/> names the refusals,
        /// and is empty on a cancel. <paramref name="allowedFindingIds"/> narrows which pending
        /// findings each file may start from; null means every one of them.
        /// </summary>
        private bool RunComponentMigration(List<string> files, out List<string> refused,
            HashSet<string> allowedFindingIds = null)
        {
            refused = new List<string>();
            if (files.Count == 0) return true;
            if (!PrepareComponentMigration()) return false;
            componentMigrator.BeginBatch();
            var cancelled = false;
            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            $"Migrating components ({i + 1}/{files.Count})",
                            files[i], (float)i / files.Count))
                    {
                        cancelled = true;
                        break;
                    }
                    if (!MigrateComponentFile(files[i], allowedFindingIds)) refused.Add(files[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                stateData.SaveIfDirty();
            }
            if (cancelled)
            {
                refused.Clear();
                Log(LogSeverity.Warning, "Component migration cancelled; finished files are kept.");
            }
            RepairComponentReferences();
            return !cancelled && refused.Count == 0;
        }

        /// <summary>
        /// Redirects every serialized reference the batch invalidated, together with anything an
        /// earlier pass could not write. A replaced component gets a new local file id, so a field
        /// pointing at it — in any scene, prefab or asset — would otherwise resolve to nothing.
        /// Runs once per batch: the pass is project-wide, and nothing reads those references in
        /// between. Redirects survive in the migration state until a pass writes them everywhere.
        /// </summary>
        private void RepairComponentReferences()
        {
            var pending = PendingRedirects();
            if (pending.Count == 0)
            {
                componentMigrator?.Redirects.Clear();
                return;
            }

            var targets = MigrationScope.Collect(stateData.excludedPaths);
            ReferenceMigrator.RepairResult result;
            try
            {
                EditorUtility.DisplayProgressBar("Repairing references",
                    $"{targets.Count} asset(s)", 0.5f);
                result = ReferenceMigrator.RemapComponents(targets, pending);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            componentMigrator?.Redirects.Clear();
            Log(LogSeverity.Info, result.Repaired == 0
                ? "No serialized reference needed redirecting to a replaced component."
                : $"Redirected {result.Repaired} reference(s) to the components that were replaced.");
            ReportRepairFailures(result,
                "still point at components that no longer exist. Make them writable — check them " +
                "out of version control or clear their read-only flag — then use Retry reference " +
                "repair on the Dashboard.");

            stateData.SetOutstandingRedirects(
                result.HasFailures ? ToOutstanding(pending) : new List<OutstandingRedirect>());
            if (result.Repaired > 0) AssetDatabase.Refresh();
        }

        /// <summary>
        /// Every redirect still owed a project-wide pass: what an earlier pass could not finish,
        /// composed with what this batch just moved. A file id that moved twice is chained, so an
        /// asset left holding the oldest id is sent to the newest.
        /// </summary>
        private List<ReferenceMigrator.ComponentRedirect> PendingRedirects()
        {
            var byPath = new Dictionary<string, (string guid, Dictionary<long, long> map)>(
                StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < stateData.outstandingRedirects.Count; i++)
            {
                var stored = stateData.outstandingRedirects[i];
                var map = new Dictionary<long, long>();
                for (var at = 0; at < stored.fromIds.Count; at++)
                    map[stored.fromIds[at]] = stored.toIds[at];
                byPath[stored.assetPath] = (stored.assetGuid, map);
            }

            var live = componentMigrator?.Redirects;
            for (var i = 0; live != null && i < live.Count; i++)
            {
                var redirect = live[i];
                if (redirect.Map.Count == 0) continue;
                if (!byPath.TryGetValue(redirect.AssetPath, out var existing))
                {
                    byPath[redirect.AssetPath] =
                        (redirect.AssetGuid, new Dictionary<long, long>(redirect.Map));
                    continue;
                }
                Compose(existing.map, redirect.Map);
                byPath[redirect.AssetPath] = (redirect.AssetGuid ?? existing.guid, existing.map);
            }

            var result = new List<ReferenceMigrator.ComponentRedirect>(byPath.Count);
            foreach (var pair in byPath)
            {
                if (pair.Value.map.Count == 0) continue;
                result.Add(new ReferenceMigrator.ComponentRedirect(
                    pair.Key, pair.Value.guid, pair.Value.map));
            }
            return result;
        }

        /// <summary>
        /// Folds a later move into an earlier one. An asset that already took the first move holds
        /// the intermediate id and an asset that missed it holds the original, so both keys stay.
        /// </summary>
        private static void Compose(Dictionary<long, long> accumulated, Dictionary<long, long> later)
        {
            foreach (var move in later)
            {
                var keys = new List<long>(accumulated.Keys);
                for (var i = 0; i < keys.Count; i++)
                    if (accumulated[keys[i]] == move.Key) accumulated[keys[i]] = move.Value;
                accumulated[move.Key] = move.Value;
            }
        }

        private static List<OutstandingRedirect> ToOutstanding(
            List<ReferenceMigrator.ComponentRedirect> redirects)
        {
            var result = new List<OutstandingRedirect>(redirects.Count);
            for (var i = 0; i < redirects.Count; i++)
            {
                var entry = new OutstandingRedirect
                {
                    assetPath = redirects[i].AssetPath,
                    assetGuid = redirects[i].AssetGuid,
                };
                foreach (var move in redirects[i].Map)
                {
                    entry.fromIds.Add(move.Key);
                    entry.toIds.Add(move.Value);
                }
                result.Add(entry);
            }
            return result;
        }

        private void ReportRepairFailures(ReferenceMigrator.RepairResult result, string consequence)
        {
            if (!result.HasFailures) return;
            for (var i = 0; i < result.Failed.Count; i++)
                Log(LogSeverity.Error,
                    $"Could not rewrite {result.Failed[i].AssetPath} — {result.Failed[i].Message}");
            Log(LogSeverity.Error, $"{result.Failed.Count} asset(s) {consequence}");
        }

        /// <summary>Re-runs whatever an earlier reference-repair pass could not write.</summary>
        private void RetryOutstandingRepairs()
        {
            RepairComponentReferences();
            RepairFontReferences(new List<string>());
            CommitOperation();
        }

        /// <summary>How many assets are owed a reference repair that has not been written yet.</summary>
        private int OutstandingRepairCount()
            => stateData.outstandingRedirects.Count + stateData.outstandingScriptGuids.Count;

        /// <summary>
        /// Moves the asset references of the fields whose type the rewrite changed: a
        /// <c>TMP_FontAsset</c> field is a <c>UniTextFont</c> field afterwards, and its serialized
        /// value has to move with it. Only documents serialized by the rewritten scripts are
        /// touched — TMP's own assets still reference their fonts legitimately.
        /// </summary>
        private void RepairFontReferences(List<string> rewrittenScripts)
        {
            var scriptGuids = new HashSet<string>(
                stateData.outstandingScriptGuids, StringComparer.Ordinal);
            for (var i = 0; i < rewrittenScripts.Count; i++)
            {
                var guid = AssetDatabase.AssetPathToGUID(rewrittenScripts[i]);
                if (!string.IsNullOrEmpty(guid)) scriptGuids.Add(guid);
            }
            if (scriptGuids.Count == 0) return;

            var guidMap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < fontMappingsData.fontMappings.Count; i++)
            {
                var entry = fontMappingsData.fontMappings[i];
                if (string.IsNullOrEmpty(entry.tmpFontGuid) ||
                    string.IsNullOrEmpty(entry.uniTextFontGuid)) continue;
                guidMap[entry.tmpFontGuid] = entry.uniTextFontGuid;
            }
            if (guidMap.Count == 0) return;

            var targets = MigrationScope.Collect(stateData.excludedPaths);
            ReferenceMigrator.RepairResult result;
            try
            {
                EditorUtility.DisplayProgressBar("Repairing font references",
                    $"{targets.Count} asset(s)", 0.5f);
                result = ReferenceMigrator.RemapAssetReferences(targets, guidMap, scriptGuids);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Log(LogSeverity.Info, result.Repaired == 0
                ? "No serialized font reference belonged to a rewritten script."
                : $"Moved {result.Repaired} font reference(s) held by rewritten scripts onto " +
                  "their UniText fonts.");
            ReportRepairFailures(result,
                "still hold a TMP font behind a field the rewrite retyped, so those fields load " +
                "as empty. Make them writable, then use Retry reference repair on the Dashboard.");

            stateData.SetOutstandingScriptGuids(
                result.HasFailures ? new List<string>(scriptGuids) : new List<string>());
            if (!result.HasFailures) return;
            EditorUtility.DisplayDialog("Some font references were not moved",
                $"{result.Failed.Count} asset(s) could not be rewritten and still reference TMP " +
                "fonts from fields the script rewrite retyped.\n\nMake them writable and use " +
                "Retry reference repair on the Dashboard. The Log names every one.", "OK");
        }

        private bool PrepareComponentMigration(bool ensureProjectFolder = true)
        {
            if (componentMigrator == null)
            {
                var projectFolder = ensureProjectFolder
                    ? EnsureProjectFolder()
                    : ProjectFolderPath();
                componentMigrator = new ComponentMigrator(
                    logEntries, fontMappingsData, stateData, projectFolder);
                componentMigrator.Initialize();
            }
            else if (ensureProjectFolder)
                EnsureProjectFolder();
            if (componentMigrator.IsTmpAvailable) return true;
            EditorUtility.DisplayDialog("TextMesh Pro not found",
                "TextMesh Pro assemblies are not loaded, so TMP components cannot be read. " +
                "Keep TextMesh Pro installed until the migration is finished.", "OK");
            return false;
        }

        private bool MigrateComponentFile(string path, HashSet<string> allowedFindingIds)
        {
            if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                return componentMigrator.MigratePrefab(path, findings, allowedFindingIds);
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return componentMigrator.MigrateScene(path, findings, allowedFindingIds);
            Log(LogSeverity.Error,
                $"Cannot migrate '{path}': only prefabs and scenes carry components.");
            return false;
        }

        /// <summary>
        /// Orders component files so a nested prefab is rewritten before the prefabs that
        /// instantiate it, and scenes last, after every prefab they reference is current.
        /// </summary>
        private List<string> OrderComponentFiles(IEnumerable<string> files)
        {
            if (prefabRank == null)
            {
                prefabRank = new Dictionary<string, int>(session.prefabOrder.Count);
                for (var i = 0; i < session.prefabOrder.Count; i++)
                    prefabRank[session.prefabOrder[i]] = i;
            }
            return files
                .Distinct()
                .OrderBy(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(path => prefabRank.TryGetValue(path, out var rank) ? rank : int.MaxValue)
                .ToList();
        }

        private void SkipSelected()
        {
            var skipped = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (!finding.isSelected || finding.status != MigrationStatus.NotStarted) continue;
                Skip(finding);
                skipped++;
            }
            if (skipped > 0) Log(LogSeverity.Info, $"Skipped {skipped} finding(s).");
            ClearSelection();
            CommitOperation();
        }

        private void SkipFinding(MigrationFinding finding)
        {
            Skip(finding);
            CommitOperation();
        }

        /// <summary>
        /// Marks one finding handled. A font's decision belongs to the mapping table, which is
        /// what the font findings are derived from — recording it only as a status would be
        /// overwritten the next time they are derived.
        /// </summary>
        private void Skip(MigrationFinding finding)
        {
            if (finding.type == FindingType.FontAsset)
            {
                var guid = AssetDatabase.AssetPathToGUID(finding.filePath);
                var entry = fontMappingsData.fontMappings.Find(
                    candidate => candidate.tmpFontGuid == guid);
                if (entry != null && !entry.skipped)
                {
                    entry.skipped = true;
                    fontMappingsData.Save();
                }
            }
            stateData.SetStatus(finding, MigrationStatus.Skipped);
        }

        private void ExcludeFinding(MigrationFinding finding)
            => ConfirmExclude(finding.filePath, "asset");

        private void ExcludeFolder(string folderPath)
            => ConfirmExclude(folderPath, "folder");

        /// <summary>Takes one path out of the migration, once its owner has read what that costs.</summary>
        private void ConfirmExclude(string projectRelativePath, string subject)
        {
            if (string.IsNullOrEmpty(projectRelativePath)) return;
            if (!EditorUtility.DisplayDialog($"Exclude this {subject}",
                    $"'{projectRelativePath}' drops out of the migration.\n\n" +
                    "Nothing under it is scanned, migrated or reported again, and no pass reads " +
                    "or rewrites it — a run finishes around it. In exchange it is yours end to " +
                    "end: a reference in it to a component an earlier batch replaced keeps naming " +
                    "an id that no longer resolves, and a TMP component inside it is still TMP " +
                    "once the scripts are rewritten.\n\nEverything recorded about it is forgotten, " +
                    "and only a scan can bring it back.", "Exclude", "Cancel"))
                return;
            ExcludePath(projectRelativePath);
        }

        private void ExcludePath(string projectRelativePath)
            => ExcludePaths(new[] { projectRelativePath });

        /// <summary>
        /// Takes every asset the scan could not read out of the migration in one step, once its
        /// owner has read what that costs.
        /// </summary>
        private void ExcludeUnreadable()
        {
            var paths = new List<string>();
            for (var i = 0; i < findings.Count; i++)
            {
                var path = findings[i].filePath;
                if (findings[i].type == FindingType.UnreadableFile &&
                    !string.IsNullOrEmpty(path) && !paths.Contains(path)) paths.Add(path);
            }
            if (paths.Count == 0) return;
            if (!EditorUtility.DisplayDialog("Exclude every unreadable asset",
                    $"{paths.Count} asset(s) the scan could not read drop out of the migration.\n\n" +
                    "Nothing inside them is migrated or repaired, and a reference in one of them " +
                    "to a component the migration replaces keeps naming an id that no longer " +
                    "resolves. The rest of the run stops waiting for them.", "Exclude", "Cancel"))
                return;
            ExcludePaths(paths);
        }

        /// <summary>
        /// Takes assets or folders out of the migration. Nothing under a path is read, migrated
        /// or reference-repaired from here on, so anything inside it that points at a component
        /// an earlier batch replaced keeps naming the id that component no longer has.
        /// </summary>
        private void ExcludePaths(IReadOnlyList<string> projectRelativePaths)
        {
            var added = new List<string>();
            for (var at = 0; at < projectRelativePaths.Count; at++)
            {
                var path = projectRelativePaths[at];
                if (string.IsNullOrEmpty(path) || IsListedExclusion(path)) continue;
                stateData.excludedPaths.Add(path);
                added.Add(path);
                if (!string.IsNullOrEmpty(filterFolder) &&
                    MigrationScope.Covers(path, filterFolder)) filterFolder = null;
            }
            if (added.Count == 0) return;

            var dropped = DropExcluded();
            stateData.Save();
            Log(LogSeverity.Warning,
                $"Excluded '{added[0]}'{MoreEntries(added.Count)} — yours to migrate. {dropped} " +
                "finding(s) left the list, and no pass reads or repairs anything under them any more.");
            CommitOperation();
        }

        private bool IsListedExclusion(string path)
        {
            for (var i = 0; i < stateData.excludedPaths.Count; i++)
            {
                if (string.Equals(stateData.excludedPaths[i], path,
                        StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Forgets everything the session and the recorded statuses hold about assets that now sit
        /// outside the migration, and returns how many findings went. A scan record left behind
        /// keeps counting work in a file no stage will ever reach — an unmapped font among them.
        /// Removing the exclusion brings none of it back: only a scan can, having read the assets
        /// again.
        /// </summary>
        private int DropExcluded()
        {
            var forgotten = new List<string>();
            for (var i = findings.Count - 1; i >= 0; i--)
            {
                if (!IsExcludedPath(findings[i].filePath)) continue;
                forgotten.Add(findings[i].id);
                findings.RemoveAt(i);
            }
            for (var i = session.findings.Count - 1; i >= 0; i--)
            {
                if (IsExcludedPath(session.findings[i].filePath)) session.findings.RemoveAt(i);
            }
            for (var i = session.scannedFiles.Count - 1; i >= 0; i--)
            {
                if (IsExcludedPath(session.scannedFiles[i].path)) session.scannedFiles.RemoveAt(i);
            }
            for (var i = session.prefabOrder.Count - 1; i >= 0; i--)
            {
                if (IsExcludedPath(session.prefabOrder[i])) session.prefabOrder.RemoveAt(i);
            }

            prefabRank = null;
            stateData.Forget(forgotten);
            return forgotten.Count;
        }

        private bool IsExcludedPath(string assetPath)
            => !string.IsNullOrEmpty(assetPath) &&
               MigrationScope.Excludes(assetPath, stateData.excludedPaths);

        /// <summary>Tail naming how many further entries a message left out.</summary>
        private static string MoreEntries(int count)
            => count > 1 ? $" and {count - 1} more" : string.Empty;

        private void ClearSelection()
        {
            for (var i = 0; i < findings.Count; i++) findings[i].isSelected = false;
        }

        private void CommitOperation()
        {
            unverifiedSkipped = -1;
            if (componentMigrator != null) lossesData = componentMigrator.Losses;
            SyncFontFindings();
            stateData.SaveIfDirty();
            session.Save();
            RenderDeferred();
        }

        private void FontMappingChanged()
        {
            fontMappingsData.Save();
            WireFontFallbacks();
            CommitOperation();
        }

        /// <summary>
        /// Rebuilds every migrated stack's family list from the TMP fallback chain the scan
        /// recorded — TMP resolves a codepoint through its fallback table, UniText through the
        /// ordered families of a stack, so the chain becomes families 1..n behind the primary —
        /// and chains the project-wide list onto each as one shared stack.
        /// </summary>
        /// <remarks>
        /// A stack whose families carry a name, a language hint or extra faces is treated as
        /// authored by hand and is never rewritten; the Log names the ones left alone.
        /// </remarks>
        private void WireFontFallbacks()
        {
            var broken = new List<string>();
            var handEdited = new List<string>();
            var chained = 0;
            var globalStack = EnsureGlobalFallbackStack(broken);

            for (var i = 0; i < fontMappingsData.fontMappings.Count; i++)
            {
                var entry = fontMappingsData.fontMappings[i];
                var stack = LoadByGuid<UniTextFontStack>(entry.uniTextFontStackGuid);
                var primary = LoadByGuid<UniTextFont>(entry.uniTextFontGuid);
                if (stack == null || primary == null || stack == globalStack) continue;

                var chain = ResolveFallbackChain(entry.tmpFontGuid, entry.fallbackGuids, broken);
                if (!ApplyFamilies(stack, primary, chain, globalStack))
                {
                    handEdited.Add(entry.tmpFontName);
                    continue;
                }
                if (chain.Count > 0) chained++;
            }

            if (chained == 0 && broken.Count == 0 && handEdited.Count == 0) return;

            AssetDatabase.SaveAssets();
            if (chained > 0)
                Log(LogSeverity.Info,
                    $"Rebuilt the fallback chain of {chained} font stack(s) from TMP's tables.");
            if (broken.Count > 0)
                Log(LogSeverity.Warning,
                    $"{broken.Count} TMP fallback font(s) have no UniText font yet, so they are " +
                    $"missing from the chains that used them: {string.Join(", ", broken)}.");
            if (handEdited.Count > 0)
                Log(LogSeverity.Info,
                    "Left the hand-authored stack(s) alone: " + string.Join(", ", handEdited) + ".");
        }

        /// <summary>
        /// The stack built from <c>TMP_Settings</c>'s project-wide list, created on first need,
        /// or null when the project has no such list.
        /// </summary>
        private UniTextFontStack EnsureGlobalFallbackStack(List<string> broken)
        {
            if (fontMappingsData.globalFallbackGuids == null ||
                fontMappingsData.globalFallbackGuids.Count == 0)
                return null;

            var chain = ResolveFallbackChain(null, fontMappingsData.globalFallbackGuids, broken);
            if (chain.Count == 0) return null;

            var stack = LoadByGuid<UniTextFontStack>(fontMappingsData.fallbackStackGuid);
            if (stack == null)
            {
                var folder = EnsureProjectFolder();
                var path = ClaimAssetPath($"{folder}/UniText Project Fallbacks.asset");
                stack = CreateInstance<UniTextFontStack>();
                AssetDatabase.CreateAsset(stack, path);
                fontMappingsData.fallbackStackGuid = AssetDatabase.AssetPathToGUID(path);
                fontMappingsData.Save();
                Log(LogSeverity.Info,
                    $"Created {path} from TMP's project-wide fallback list ({chain.Count} font(s)).");
            }

            ApplyFamilies(stack, chain[0], chain.GetRange(1, chain.Count - 1), null);
            return stack;
        }

        /// <summary>
        /// Writes primary + chain into a stack's families, refusing when the stack carries authored
        /// detail. Returns false when the stack was left untouched.
        /// </summary>
        private static bool ApplyFamilies(UniTextFontStack stack, UniTextFont primary,
            List<UniTextFont> chain, UniTextFontStack fallbackStack)
        {
            if (!IsMigrationOwned(stack)) return false;

            var families = new FontFamily[1 + chain.Count];
            families[0] = new FontFamily { primary = primary };
            for (var i = 0; i < chain.Count; i++)
                families[i + 1] = new FontFamily { primary = chain[i] };
            stack.Families.ReplaceAll(families);

            if (fallbackStack != null && stack.FallbackStack == null)
                stack.FallbackStack = fallbackStack;

            EditorUtility.SetDirty(stack);
            return true;
        }

        /// <summary>
        /// Whether every family is a bare primary — the only shape the migration ever writes, and
        /// therefore the only one it may overwrite.
        /// </summary>
        private static bool IsMigrationOwned(UniTextFontStack stack)
        {
            foreach (var family in stack.Families)
            {
                if (!string.IsNullOrEmpty(family.name) ||
                    !string.IsNullOrEmpty(family.preferredLanguage) ||
                    family.Faces.Count > 0)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// The fonts a TMP chain resolves to, depth-first in TMP's own order, each visited once so
        /// a cyclic table terminates. Fallbacks with no UniText font are collected into
        /// <paramref name="broken"/> rather than silently dropped.
        /// </summary>
        private List<UniTextFont> ResolveFallbackChain(string rootGuid, List<string> guids,
            List<string> broken)
        {
            var result = new List<UniTextFont>();
            var visited = new HashSet<string>();
            if (!string.IsNullOrEmpty(rootGuid)) visited.Add(rootGuid);
            Visit(guids);
            return result;

            void Visit(List<string> level)
            {
                if (level == null) return;
                for (var i = 0; i < level.Count; i++)
                {
                    var guid = level[i];
                    if (!visited.Add(guid)) continue;
                    var mapping = fontMappingsData.fontMappings.Find(
                        candidate => candidate.tmpFontGuid == guid);
                    var font = mapping == null
                        ? null
                        : LoadByGuid<UniTextFont>(mapping.uniTextFontGuid);
                    if (font == null)
                    {
                        var name = mapping?.tmpFontName;
                        var label = string.IsNullOrEmpty(name) ? guid : name;
                        if (!broken.Contains(label)) broken.Add(label);
                        continue;
                    }
                    result.Add(font);
                    Visit(mapping.fallbackGuids);
                }
            }
        }

        private static string ProjectFolderPath()
        {
            const string fallback = "Assets/UniText";
            var settings = UniTextSettings.Instance;
            var settingsPath = settings == null ? null : AssetDatabase.GetAssetPath(settings);
            var folder = fallback;
            if (!string.IsNullOrEmpty(settingsPath))
            {
                var resources = Path.GetDirectoryName(settingsPath)?.Replace('\\', '/');
                folder = Path.GetDirectoryName(resources)?.Replace('\\', '/') ?? fallback;
            }

            return folder;
        }

        private static string EnsureProjectFolder()
        {
            var folder = ProjectFolderPath();
            if (AssetDatabase.IsValidFolder(folder)) return folder;
            var parts = folder.Split('/');
            var built = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
            return built;
        }

        /// <summary>
        /// Mirrors the font mapping onto the font findings: the mapping table is what decides
        /// whether a TMP font is dealt with, so the finding never disagrees with it.
        /// </summary>
        private void SyncFontFindings()
        {
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.FontAsset) continue;
                var guid = AssetDatabase.AssetPathToGUID(finding.filePath);
                var entry = fontMappingsData.fontMappings.Find(
                    candidate => candidate.tmpFontGuid == guid);
                if (entry == null) continue;
                var status = entry.skipped ? MigrationStatus.Skipped
                    : entry.IsMapped ? MigrationStatus.Completed
                    : MigrationStatus.NotStarted;
                if (finding.status == status) continue;
                stateData.SetStatus(finding, status);
            }
        }

        /// <summary>
        /// Builds the project-wide Style preset and assigns it in <see cref="UniTextSettings"/>.
        /// UniText's markup vocabulary is authored: with no preset, its tags render as literal
        /// characters wherever the text is used. The preset carries the whole of
        /// <see cref="MigrationMapping.TagVocabulary"/> rather than the tags this scan found —
        /// text that arrives at runtime, from localisation or from script, is never scanned, and
        /// its markup has to work too.
        /// </summary>
        private void CreateGlobalStylePreset()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Create UniText Style preset", "UniText Styles", "asset",
                "The preset carries every TMP tag UniText can reproduce.");
            if (string.IsNullOrEmpty(path)) return;

            var preset = CreateInstance<StylePreset>();
            AssetDatabase.CreateAsset(preset, path);

            var added = 0;
            var unresolved = new List<string>();
            foreach (var entry in MigrationMapping.TagVocabulary)
            {
                var style = CreateVocabularyStyle(entry.Key, entry.Value, out var failure);
                if (style == null)
                {
                    unresolved.Add(failure == null
                        ? $"<{entry.Key}>"
                        : $"<{entry.Key}> ({failure})");
                    continue;
                }
                preset.Styles.Add(style);
                added++;
            }

            EditorUtility.SetDirty(preset);
            var settings = UniTextSettings.Instance;
            if (settings == null)
            {
                AssetDatabase.SaveAssets();
                Log(LogSeverity.Error,
                    $"Created a Style preset with {added} tag(s) at {path}, but no UniText " +
                    "settings asset exists to assign it to — set it in Project Settings → UniText.");
                CommitOperation();
                return;
            }

            UniTextSettings.GlobalStylePreset = preset;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Log(LogSeverity.Info,
                $"Created a Style preset with {added} tag(s) at {path} and assigned it as the " +
                "project-wide preset. Every component that keeps Use Global Style Preset reads " +
                "it, so this markup renders wherever its text comes from.");
            if (unresolved.Count > 0)
                Log(LogSeverity.Warning,
                    $"Left out of the preset: {string.Join(", ", unresolved)} — " +
                    "add those entries by hand.");
            CommitOperation();
        }

        /// <summary>
        /// The Style one vocabulary entry puts behind its tag, or null with the reason it could
        /// not be built. A binding naming a standalone rule yields a Style with no modifier — the
        /// rule is the whole effect, and it owns the tag name it matches.
        /// </summary>
        private static Style CreateVocabularyStyle(string tagName,
            MigrationMapping.TagBinding binding, out string failure)
        {
            failure = null;
            if (binding.StandaloneRuleTypeName != null)
            {
                var standalone =
                    MigrationMapping.CreateStandaloneRule(binding.StandaloneRuleTypeName);
                if (standalone != null) return new Style { Source = standalone };
                failure = $"no {binding.StandaloneRuleTypeName} is registered";
                return null;
            }

            var modifier = CreateVocabularyModifier(binding, out failure);
            if (modifier == null) return null;

            TagRule rule = binding.Inline ? new InlineTagRule(tagName) : new TagRule(tagName);
            rule.DefaultParameter = binding.DefaultParameter;
            return new Style { Modifier = modifier, Source = rule };
        }

        /// <summary>
        /// The modifier one vocabulary entry puts behind its tag, or null with the reason it could
        /// not be built. A binding naming a graph preset applies that whole shipped graph through
        /// a <see cref="ModifierGraphModifier"/>: a link is a modifier plus its interaction rules
        /// and states, which no single modifier carries.
        /// </summary>
        private static BaseModifier CreateVocabularyModifier(MigrationMapping.TagBinding binding,
            out string failure)
        {
            failure = null;
            if (binding.GraphPresetName == null)
            {
                var modifier = MigrationMapping.CreateModifier(binding.ModifierTypeName);
                if (modifier == null) failure = $"no {binding.ModifierTypeName} is registered";
                return modifier;
            }

            var preset = FindDefaultGraphPreset(binding.GraphPresetName, out failure);
            return preset == null ? null : new ModifierGraphModifier { Preset = preset };
        }

        /// <summary>
        /// A modifier-graph preset shipped in the package's <c>Defaults</c> folder, found by asset
        /// identity rather than by a fixed path, so it resolves whether the package is installed
        /// through the registry or lives inside the project.
        /// </summary>
        private static ModifierGraphPreset FindDefaultGraphPreset(string name, out string failure)
        {
            var suffix = $"/Defaults/ModifierGraphPresets/{name}.asset";
            ModifierGraphPreset found = null;
            foreach (var guid in AssetDatabase.FindAssets($"{name} t:{nameof(ModifierGraphPreset)}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                var candidate = AssetDatabase.LoadAssetAtPath<ModifierGraphPreset>(path);
                if (candidate == null) continue;
                if (found != null)
                {
                    failure = $"more than one {name} modifier graph is installed";
                    return null;
                }
                found = candidate;
            }

            failure = found == null
                ? $"the shipped {name} modifier graph is missing from the UniText package"
                : null;
            return found;
        }

        /// <summary>Builds a font and stack for every mapping that has a source file and no font yet.</summary>
        private void CreateMissingFonts()
        {
            var pending = fontMappingsData.fontMappings.Where(IsCreatableFont).ToList();
            if (pending.Count == 0) return;
            try
            {
                for (var i = 0; i < pending.Count; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                            $"Creating fonts ({i + 1}/{pending.Count})",
                            pending[i].sourceTtfPath, (float)i / pending.Count))
                        break;
                    CreateFontFromSource(pending[i]);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            FontMappingChanged();
        }

        /// <summary>
        /// Whether the tool can finish this mapping on its own. What a component needs is the
        /// stack, so a row that already names a UniText font still counts: wrapping that font in
        /// a stack takes no decision. Only a row with neither a font nor a source file needs one.
        /// </summary>
        private static bool IsCreatableFont(FontMappingEntry entry)
            => !entry.IsMapped &&
               (entry.HasSource || !string.IsNullOrEmpty(entry.uniTextFontGuid));

        private int CreatableFontCount() => fontMappingsData.fontMappings.Count(IsCreatableFont);

        /// <summary>Pending findings the tool will never rewrite by itself.</summary>
        private int ManualPendingCount()
        {
            var count = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.status == MigrationStatus.Failed)
                {
                    count++;
                    continue;
                }
                if (finding.status != MigrationStatus.NotStarted) continue;
                switch (finding.type)
                {
                    case FindingType.Component:
                        if (!IsAutoMigratableComponent(finding)) count++;
                        break;
                    case FindingType.ScriptReference:
                    case FindingType.FontAsset:
                        break;
                    default:
                        count++;
                        break;
                }
            }
            return count;
        }

        /// <summary>Whether the automatic run covers this finding, or it is left for hand work.</summary>
        private static bool IsAutoMigratableComponent(MigrationFinding finding)
            => MigrationMapping.IsMigratableComponent(finding.scriptGuid);

        private void ApplyAllScripts()
        {
            ApplyScriptFiles(PendingScriptFiles());
            AssetDatabase.Refresh();
            CommitOperation();
        }

        /// <summary>Every distinct C# file with a pending script finding, in scan order.</summary>
        private List<string> PendingScriptFiles()
        {
            var files = new List<string>();
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.ScriptReference ||
                    finding.status != MigrationStatus.NotStarted ||
                    files.Contains(finding.filePath)) continue;
                files.Add(finding.filePath);
            }
            return files;
        }

        private void ApplyScriptFiles(List<string> files)
        {
            if (!CanRewriteScripts()) return;
            var held = HeldScripts(files);
            var applied = 0;
            var rewritten = new List<string>();
            try
            {
                for (var i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            $"Rewriting scripts ({i + 1}/{files.Count})",
                            file, (float)i / files.Count))
                        break;
                    if (held.Contains(file)) continue;
                    var replacements = ScriptMigrator.AnalyzeFile(file);
                    if (ScriptMigrator.HasBlockers(replacements))
                    {
                        Log(LogSeverity.Warning,
                            $"Script rewrite blocked in {file} — migrate its TMP_InputField uses by hand.");
                        continue;
                    }
                    if (ApplicableCount(replacements) == 0)
                    {
                        Log(LogSeverity.Warning,
                            $"Nothing to rewrite automatically in {file} — handle it by hand.");
                        continue;
                    }
                    var (ok, backupPath, error) =
                        ScriptMigrator.ApplyReplacements(file, replacements, true);
                    if (ok)
                    {
                        applied++;
                        rewritten.Add(file);
                        SetScriptFindings(file, MigrationStatus.Completed);
                    }
                    Log(ok ? LogSeverity.Info : LogSeverity.Error,
                        ok ? $"Rewrote script: {file}" : $"Script rewrite failed: {file} — {error}",
                        backupPath);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Log(LogSeverity.Info, $"Rewrote {applied} script file(s); originals kept as .bak.");
            RepairFontReferences(rewritten);
            selectedScriptIndex = -1;
            currentReplacements = new List<ScriptReplacement>();
            currentDiff = string.Empty;
        }

        private void ApplySelectedScript()
        {
            if (!CanRewriteScripts()) return;
            var path = scriptFiles[selectedScriptIndex];
            if (ScriptMigrator.HasBlockers(currentReplacements))
            {
                Log(LogSeverity.Warning,
                    $"Script rewrite blocked in {path} — migrate its TMP_InputField uses by hand.");
                return;
            }
            var (ok, backupPath, error) =
                ScriptMigrator.ApplyReplacements(path, currentReplacements, true);
            if (ok)
            {
                SetScriptFindings(path, MigrationStatus.Completed);
                Log(LogSeverity.Info, $"Rewrote script: {path}", backupPath);
                RepairFontReferences(new List<string> { path });
                AssetDatabase.ImportAsset(path);
                selectedScriptIndex = -1;
                currentReplacements = new List<ScriptReplacement>();
                currentDiff = string.Empty;
            }
            else
            {
                Log(LogSeverity.Error, $"Script rewrite failed: {path} — {error}");
                SelectScript(selectedScriptIndex);
            }
            CommitOperation();
        }

        private void MarkScriptHandled(string path)
        {
            SetScriptFindings(path, MigrationStatus.Skipped);
            Log(LogSeverity.Info, $"Marked as handled by hand: {path}");
            selectedScriptIndex = -1;
            currentReplacements = new List<ScriptReplacement>();
            currentDiff = string.Empty;
            CommitOperation();
        }

        /// <summary>Returns a restored file's script findings to Pending so the rewrite can be re-run.</summary>
        private void ReopenRestoredScript(string backupPath)
        {
            if (!backupPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return;
            var path = backupPath.Substring(0, backupPath.Length - 4);
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.ScriptReference ||
                    finding.filePath != path ||
                    finding.status == MigrationStatus.NotStarted) continue;
                stateData.SetStatus(finding, MigrationStatus.NotStarted);
            }
        }

        private void SetScriptFindings(string path, MigrationStatus status)
        {
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.ScriptReference ||
                    finding.status != MigrationStatus.NotStarted ||
                    finding.filePath != path) continue;
                stateData.SetStatus(finding, status);
            }
        }

        /// <summary>Replacements the rewrite would actually write; the rest are warnings to read.</summary>
        private static int ApplicableCount(List<ScriptReplacement> replacements)
        {
            if (ScriptMigrator.HasBlockers(replacements)) return 0;
            var count = 0;
            for (var i = 0; i < replacements.Count; i++)
            {
                var replacement = replacements[i];
                if (replacement.isSelected && !replacement.isWarningOnly &&
                    replacement.replacement != null)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Brings one mapping to the state a component can use: a UniText font, and the font stack
        /// that points at it. Either half may already be there — a font assigned by hand, or an
        /// asset a previous run created — and only the missing half is built.
        /// </summary>
        private void CreateFontFromSource(FontMappingEntry entry)
        {
            var font = ResolveOrCreateFont(entry, out var fontPath);
            if (font == null) return;

            entry.uniTextFontGuid = AssetDatabase.AssetPathToGUID(fontPath);
            EnsureFontStack(entry, font, fontPath);
            fontMappingsData.Save();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The UniText font this mapping resolves to — the one already assigned, the one already
        /// sitting beside the source file, or a new one built from that file. Null when none of
        /// the three can answer, with the reason logged.
        /// </summary>
        private UniTextFont ResolveOrCreateFont(FontMappingEntry entry, out string fontPath)
        {
            fontPath = AssetDatabase.GUIDToAssetPath(entry.uniTextFontGuid);
            if (!string.IsNullOrEmpty(fontPath))
            {
                var assigned = AssetDatabase.LoadAssetAtPath<UniTextFont>(fontPath);
                if (assigned != null) return assigned;
            }

            if (!entry.HasSource)
            {
                Log(LogSeverity.Error,
                    $"'{entry.tmpFontName}' names a UniText font that no longer exists, and there " +
                    "is no source file to build a new one from. Assign a font stack by hand or " +
                    "skip the row.");
                fontPath = null;
                return null;
            }

            var directory = Path.GetDirectoryName(entry.sourceTtfPath);
            var name = Path.GetFileNameWithoutExtension(entry.sourceTtfPath);
            fontPath = $"{directory}/{name}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<UniTextFont>(fontPath);
            if (existing != null)
            {
                Log(LogSeverity.Info, $"Font already exists: {fontPath}");
                return existing;
            }

            byte[] fontBytes;
            try
            {
                fontBytes = File.ReadAllBytes(entry.sourceTtfPath);
            }
            catch (Exception exception)
            {
                Log(LogSeverity.Error,
                    $"Cannot read font file: {entry.sourceTtfPath} — {exception.Message}");
                fontPath = null;
                return null;
            }

            var created = UniTextFont.CreateFontAsset(fontBytes);
            if (created == null)
            {
                Log(LogSeverity.Error,
                    $"Failed to create font asset from {entry.sourceTtfPath}");
                fontPath = null;
                return null;
            }

            fontPath = ClaimAssetPath(fontPath);
            UniTextFontAssetPersistence.Create(created, fontPath);
            Log(LogSeverity.Info, $"Created font: {fontPath}");
            return created;
        }

        /// <summary>
        /// Points the mapping at a font stack wrapping <paramref name="font"/>, reusing the stack
        /// beside it when one is already there. A component reads a stack, never a bare font, so a
        /// mapping without one is not usable however the font got assigned.
        /// </summary>
        private void EnsureFontStack(FontMappingEntry entry, UniTextFont font, string fontPath)
        {
            if (!string.IsNullOrEmpty(entry.uniTextFontStackGuid) &&
                AssetDatabase.LoadAssetAtPath<UniTextFontStack>(
                    AssetDatabase.GUIDToAssetPath(entry.uniTextFontStackGuid)) != null)
                return;

            var stackPath =
                $"{Path.GetDirectoryName(fontPath)}/{Path.GetFileNameWithoutExtension(fontPath)} Stack.asset";
            var existing = AssetDatabase.LoadAssetAtPath<UniTextFontStack>(stackPath);
            if (existing != null)
            {
                entry.uniTextFontStackGuid = AssetDatabase.AssetPathToGUID(stackPath);
                Log(LogSeverity.Info, $"Font stack already exists: {stackPath}");
                return;
            }

            stackPath = ClaimAssetPath(stackPath);
            var stack = CreateInstance<UniTextFontStack>();
            stack.Families.ReplaceAll(new[] { new FontFamily { primary = font } });
            AssetDatabase.CreateAsset(stack, stackPath);
            entry.uniTextFontStackGuid = AssetDatabase.AssetPathToGUID(stackPath);
            Log(LogSeverity.Info, $"Created font stack: {stackPath}");
        }

        /// <summary>
        /// The path unless another asset already occupies it, in which case a free neighbour:
        /// <see cref="AssetDatabase.CreateAsset"/> deletes whatever sits at the path it is given,
        /// and no TMP asset may be lost to a name collision.
        /// </summary>
        private string ClaimAssetPath(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null) return path;
            var free = AssetDatabase.GenerateUniqueAssetPath(path);
            Log(LogSeverity.Warning,
                $"{path} already holds another asset — writing {free} instead.");
            return free;
        }

        private void ExportReport()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export Migration Report", "", "migration-report.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;
            var output = new System.Text.StringBuilder();
            summary = MigrationSummary.Compute(findings);
            output.AppendLine("# TMP → UniText Migration Report");
            output.AppendLine($"# Generated: {DateTime.Now}");
            output.AppendLine($"# Scan: {stateData.lastScanTime}" +
                              (session.partialScan ? " (partial)" : string.Empty));
            output.AppendLine($"# Findings: {findings.Count} — " +
                              $"{summary.completed} completed, {summary.pending} pending, " +
                              $"{summary.skipped} skipped, {summary.failed} failed");
            output.AppendLine();
            output.AppendLine($"Simple: {summary.simpleCount} ({Pct(summary.simpleCount)})");
            output.AppendLine($"Moderate: {summary.moderateCount} ({Pct(summary.moderateCount)})");
            output.AppendLine($"Complex: {summary.complexCount} ({Pct(summary.complexCount)})");
            output.AppendLine($"Manual: {summary.manualCount} ({Pct(summary.manualCount)})");
            output.AppendLine();
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                output.AppendLine($"[{finding.status}] [{finding.type}] [{finding.complexity}] " +
                                  $"{finding.filePath} — {finding.details}");
                AppendManualReviews(output, finding, "    ");
                if (finding.warnings == null) continue;
                for (var warning = 0; warning < finding.warnings.Count; warning++)
                    output.AppendLine($"    {finding.warnings[warning]}");
            }
            File.WriteAllText(path, output.ToString());
            Log(LogSeverity.Info, $"Report exported to {path}");
            RenderToolkitTab();
        }

        /// <summary>Writes what the migration removed, with each component's settings, to a file.</summary>
        private void ExportLosses()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export removed components", "", "unitext-removed-components.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;

            var output = new System.Text.StringBuilder();
            output.AppendLine("# What the TMP → UniText migration could not carry over");
            output.AppendLine("# Removed components declared a requirement no UniText component");
            output.AppendLine("# satisfies. They cannot be put back; these are the values they had.");
            output.AppendLine();
            for (var i = 0; i < lossesData.removed.Count; i++)
            {
                var entry = lossesData.removed[i];
                output.AppendLine($"{entry.componentType} — {entry.assetPath} :: {entry.objectPath}");
                output.AppendLine($"    {entry.reason}");
                if (entry.referencedBy is { Count: > 0 })
                    output.AppendLine("    Left empty on: " + string.Join(", ", entry.referencedBy));
                output.AppendLine($"    Removed {entry.removedAt}");
                output.AppendLine(entry.state);
                output.AppendLine();
            }

            if (lossesData.settings.Count > 0)
            {
                output.AppendLine();
                output.AppendLine("# Settings with no UniText counterpart");
                output.AppendLine("# Their components migrated; these values did not.");
                output.AppendLine();
                for (var i = 0; i < lossesData.settings.Count; i++)
                {
                    var entry = lossesData.settings[i];
                    output.AppendLine($"{entry.assetPath} :: {entry.objectPath}");
                    output.AppendLine($"    {entry.componentType}.{entry.setting} = {entry.value}");
                    if (!string.IsNullOrEmpty(entry.advice))
                        output.AppendLine($"    {entry.advice}");
                }
            }
            File.WriteAllText(path, output.ToString());
            Log(LogSeverity.Info, $"Removed-component record exported to {path}");
        }

        /// <summary>Drops the record of what was removed, once the user has acted on it.</summary>
        private void ClearLossRecord()
        {
            if (!EditorUtility.DisplayDialog("Clear what was not carried over",
                    $"Forgets {lossesData.removed.Count} removed component(s) and " +
                    $"{lossesData.settings.Count} lost setting(s). Nothing in the project changes " +
                    "and nothing comes back — this only discards the note of what they were." +
                    "\n\nExport it first if you have not acted on it yet.", "Clear", "Cancel"))
                return;

            lossesData.removed.Clear();
            lossesData.settings.Clear();
            lossesData.Save();
            Log(LogSeverity.Info, "Cleared the record of what was not carried over.");
            CommitOperation();
        }

        private void ExportLog()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export Log", "", "migration-log.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;
            var output = new System.Text.StringBuilder();
            for (var i = 0; i < logEntries.Count; i++)
            {
                var entry = logEntries[i];
                output.AppendLine($"[{entry.timestamp}] [{entry.severity}] {entry.message}");
            }
            File.WriteAllText(path, output.ToString());
        }

        private static string FindingDetails(MigrationFinding finding)
        {
            if (finding.manualReviews == null) return finding.details;
            for (var i = 0; i < finding.manualReviews.Count; i++)
            {
                var reason = finding.manualReviews[i]?.reason;
                if (!string.IsNullOrEmpty(reason)) return $"{finding.details} — {reason}";
            }
            return finding.details;
        }

        private static string ManualReviewDetails(MigrationFinding finding)
        {
            if (finding.manualReviews == null || finding.manualReviews.Count == 0)
                return string.Empty;
            var output = new System.Text.StringBuilder();
            AppendManualReviews(output, finding, string.Empty);
            return output.ToString().TrimEnd();
        }

        private static void AppendManualReviews(System.Text.StringBuilder output,
            MigrationFinding finding, string indent)
        {
            if (finding.manualReviews == null) return;
            for (var i = 0; i < finding.manualReviews.Count; i++)
            {
                var review = finding.manualReviews[i];
                if (review == null) continue;
                if (i > 0) output.AppendLine(indent);
                output.AppendLine($"{indent}Review {i + 1} [{review.kind}]");
                output.AppendLine($"{indent}Reason: {review.reason}");
                output.AppendLine($"{indent}Action: {review.action}");
                output.AppendLine($"{indent}Asset: {review.assetPath}");
                output.AppendLine($"{indent}Object: {review.objectPath} (fileID {review.targetFileID})");
                output.AppendLine($"{indent}Source type: {review.sourceType}");
                if (!string.IsNullOrEmpty(review.dependentType))
                    output.AppendLine($"{indent}Dependent type: {review.dependentType}");
                if (!string.IsNullOrEmpty(review.requiredType))
                    output.AppendLine($"{indent}Required type: {review.requiredType}");
            }
        }

        private bool MatchesFilter(MigrationFinding finding)
            => MatchesRowFilters(finding) &&
               (string.IsNullOrEmpty(filterFolder) ||
                MigrationScope.Covers(filterFolder, finding.filePath));

        /// <summary>
        /// Whether the finding survives every filter but the folder tree's own. The tree counts
        /// through this: a folder that reported only what the current folder selection already
        /// shows could never be used to leave it.
        /// </summary>
        private bool MatchesRowFilters(MigrationFinding finding)
        {
            if (filterType.HasValue && finding.type != filterType.Value) return false;
            if (filterStatus.HasValue && finding.status != filterStatus.Value) return false;
            return string.IsNullOrEmpty(searchText) ||
                   finding.filePath.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                   finding.details.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether the component migrator would actually rewrite this selected finding.</summary>
        private static bool IsMigratableSelection(MigrationFinding finding)
            => finding.isSelected && finding.status == MigrationStatus.NotStarted &&
               finding.type == FindingType.Component &&
               MigrationMapping.IsMigratableComponent(finding.scriptGuid);

        /// <summary>
        /// TMP fonts the scan found that no mapping decision covers. Judged against what the scan
        /// recorded, never against the length of the mapping table: a table missing a row would
        /// otherwise read as "everything is mapped".
        /// </summary>
        private int UnmappedFontCount()
        {
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < fontMappingsData.fontMappings.Count; i++)
            {
                var entry = fontMappingsData.fontMappings[i];
                if (entry.IsMapped && !string.IsNullOrEmpty(entry.tmpFontGuid))
                    mapped.Add(entry.tmpFontGuid);
            }

            var unmapped = 0;
            for (var i = 0; i < session.scannedFiles.Count; i++)
            {
                var record = session.scannedFiles[i];
                if (!record.hasFont) continue;
                var guid = AssetDatabase.AssetPathToGUID(record.path);
                if (string.IsNullOrEmpty(guid) || !mapped.Contains(guid)) unmapped++;
            }
            return unmapped;
        }

        private bool IsFontMappingComplete() => UnmappedFontCount() == 0;

        private int CountSimpleComponentsPending()
        {
            return findings.Count(finding =>
                finding.type == FindingType.Component &&
                finding.status == MigrationStatus.NotStarted &&
                finding.complexity == MigrationComplexity.Simple);
        }

        private int CountByTypeAndStatus(FindingType type, MigrationStatus status)
        {
            return findings.Count(finding => finding.type == type && finding.status == status);
        }

        /// <summary>Pending components split by whether the migrator has a target for them.</summary>
        private (int migratable, int skipOnly) PendingComponentSplit()
        {
            var migratable = 0;
            var skipOnly = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.Component ||
                    finding.status != MigrationStatus.NotStarted) continue;
                if (MigrationMapping.IsMigratableComponent(finding.scriptGuid)) migratable++;
                else skipOnly++;
            }
            return (migratable, skipOnly);
        }

        /// <summary>
        /// What the failed components say, when enough of them say the same thing to be worth
        /// reading instead of the list. Hundreds of rows carrying one recorded reason are one
        /// project-wide cause, and naming it turns a wall into a single fix.
        /// </summary>
        private string DominantFailureNote()
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var failed = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.Component ||
                    finding.status != MigrationStatus.Failed) continue;
                failed++;
                if (finding.manualReviews is not { Count: > 0 }) continue;
                var reason = finding.manualReviews[0].reason;
                if (string.IsNullOrEmpty(reason)) continue;
                counts.TryGetValue(reason, out var seen);
                counts[reason] = seen + 1;
            }
            if (failed == 0) return string.Empty;

            var best = 0;
            string dominant = null;
            foreach (var pair in counts)
            {
                if (pair.Value <= best) continue;
                dominant = pair.Key;
                best = pair.Value;
            }

            var tail = failed > 1
                ? "\n\nAnalysis has Re-check all failed and Mark all failed handled — one press " +
                  "covers every row, so a shared cause never has to be cleared one at a time."
                : string.Empty;
            return dominant == null
                ? tail
                : $"\n\n{best} of the {failed} failed component(s) report the same thing:\n" +
                  $"\"{dominant}\"" + tail;
        }

        /// <summary>Why no stage may be treated as finished, or null when the last scan ran to its end.</summary>
        private string PartialScanBlockReason()
            => session.partialScan
                ? "The last scan was interrupted, so the files it never reached are unknown. " +
                  "Re-scan before relying on these counts."
                : null;

        /// <summary>
        /// Why no script may be rewritten at all, or null when the stage can run. A component the
        /// migration has not reached is not one of those reasons: the rewrite holds back exactly
        /// the scripts whose fields point at one, and rewrites the rest. Only a scan that does not
        /// cover the project stops the stage, because then nothing can be checked.
        /// </summary>
        private string ScriptRewriteBlockReason()
        {
            UnverifiedSkippedCount();
            return PartialScanBlockReason();
        }

        /// <summary>What is left on the components stage, to report rather than to block on.</summary>
        private string ComponentsOutstandingNote()
        {
            var (pending, skipOnly) = PendingComponentSplit();
            var failed = CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed);
            if (pending == 0 && skipOnly == 0 && failed == 0) return null;

            var parts = new List<string>(3);
            if (pending > 0) parts.Add($"{pending} pending");
            if (skipOnly > 0) parts.Add($"{skipOnly} with no UniText equivalent, to skip");
            if (failed > 0) parts.Add($"{failed} failed");
            return string.Join(", ", parts) + " component migration(s) are still open. Any script " +
                   "whose field points at one of them is held back until it is resolved.";
        }

        /// <summary>
        /// TMP components a status calls handled that their asset still contains, returned to
        /// Pending as they are found. Skip and Mark handled record a promise nothing checked, and
        /// the rewrite renames the very types those components are — so the bytes decide, not the
        /// status. Counted once per change to the migration state.
        /// </summary>
        private int UnverifiedSkippedCount()
        {
            if (unverifiedSkipped >= 0) return unverifiedSkipped;

            var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var stale = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.type != FindingType.Component ||
                    finding.status != MigrationStatus.Skipped ||
                    !MigrationMapping.IsMigratableComponent(finding.scriptGuid)) continue;

                if (!contents.TryGetValue(finding.filePath, out var content))
                {
                    var fsPath = ProjectYamlFiles.ToFsPath(finding.filePath);
                    try { content = fsPath == null ? null : File.ReadAllText(fsPath); }
                    catch (Exception exception) when (exception is IOException or
                                                          UnauthorizedAccessException)
                    {
                        content = null;
                    }
                    contents[finding.filePath] = content;
                }
                if (content == null || !content.Contains(finding.scriptGuid)) continue;

                stateData.SetStatus(finding, MigrationStatus.NotStarted);
                Log(LogSeverity.Warning,
                    $"'{finding.objectPath}' in '{finding.filePath}' was marked handled, but the " +
                    "asset still holds the TMP component. Returned to Pending — the script " +
                    "rewrite would rename the type it still is.");
                stale++;
            }
            unverifiedSkipped = stale;
            return stale;
        }

        private bool CanRewriteScripts()
        {
            var reason = ScriptRewriteBlockReason();
            if (reason == null) return true;
            Log(LogSeverity.Warning, $"Script rewrite blocked: {reason}");
            EditorUtility.DisplayDialog("Components must be resolved first", reason, "OK");
            return false;
        }

        /// <summary>
        /// The files in <paramref name="candidates"/> that must wait, because something in the
        /// project serializes a reference from one of their fields to a component still on TMP.
        /// Retyping such a field would drop that reference silently, so those scripts alone are
        /// held back — every other file is rewritten. An asset the pass could not read holds back
        /// every candidate instead: no script is proven free while part of the project is unread.
        /// </summary>
        private HashSet<string> HeldScripts(List<string> candidates)
        {
            var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (candidates.Count == 0) return held;

            List<ReferenceMigrator.HeldReference> references;
            List<string> unreadable;
            try
            {
                EditorUtility.DisplayProgressBar("Checking script references",
                    "Looking for fields that still point at TMP components", 0.5f);
                references = ReferenceMigrator.FindHeldScripts(
                    MigrationScope.Collect(stateData.excludedPaths),
                    MigrationMapping.AllTmpComponentGuids, out unreadable);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (unreadable.Count > 0)
            {
                for (var i = 0; i < candidates.Count; i++) held.Add(candidates[i]);
                Log(LogSeverity.Error,
                    $"Held back every script: {unreadable[0]}{MoreEntries(unreadable.Count)}. A " +
                    "field pointing at a TMP component from there would be dropped by the rewrite " +
                    "without a trace. Exclude the asset in Settings to rewrite the scripts without " +
                    "it, or make it readable.");
                return held;
            }
            if (references.Count == 0) return held;

            var byGuid = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < candidates.Count; i++)
            {
                var guid = AssetDatabase.AssetPathToGUID(candidates[i]);
                if (!string.IsNullOrEmpty(guid)) byGuid[guid] = candidates[i];
            }

            for (var i = 0; i < references.Count; i++)
            {
                if (!byGuid.TryGetValue(references[i].ScriptGuid, out var path)) continue;
                if (!held.Add(path)) continue;
                Log(LogSeverity.Warning,
                    $"Held back {path}: '{references[i].HolderPath}' still points one of its " +
                    $"fields at a TMP component in '{references[i].TargetPath}'. Retyping the " +
                    "field now would drop that reference. Migrate that component, then rewrite " +
                    "this script.");
            }
            if (held.Count > 0)
                Log(LogSeverity.Warning,
                    $"{held.Count} script(s) stay Pending — rewrite each once the component its " +
                    "holder points at is migrated.");
            return held;
        }

        /// <summary>Why bulk component migration is unavailable, or null when it can run.</summary>
        private string ComponentMigrationBlockReason()
        {
            if (findings.Count == 0) return "Scan the project first.";
            var partial = PartialScanBlockReason();
            if (partial != null) return partial;
            if (!IsFontMappingComplete())
                return $"Map {UnmappedFontCount()} TMP font(s) in Font Mapping first — " +
                       "migrated components need a UniText font to point at.";
            return null;
        }

        /// <summary>The one action that moves the migration forward, and the tab that performs it.</summary>
        private (string title, string detail, Tab tab, bool done) NextStep()
        {
            if (findings.Count == 0)
                return ("Scan the project", "Nothing has been analysed yet.", Tab.Dashboard, false);
            if (!IsFontMappingComplete())
                return ($"Map {UnmappedFontCount()} TMP font(s)",
                    "Every TMP font needs a UniText font stack before components can be migrated.",
                    Tab.FontMapping, false);
            var (components, skipOnly) = PendingComponentSplit();
            var failedComponents =
                CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed);
            if (components > 0)
                return ($"Migrate {components} pending component(s)",
                    failedComponents == 0
                        ? "Prefabs are rewritten before the scenes that use them."
                        : $"{failedComponents} failed component(s) also require an explicit " +
                          "Re-check or Mark handled.", Tab.Analysis, false);
            if (skipOnly > 0)
                return ($"Skip {skipOnly} TMP component(s) with no UniText equivalent",
                    "A TMP_Dropdown has nothing to become. Skip its row — migrating it would " +
                    "rewrite nothing.", Tab.Analysis, false);
            if (failedComponents > 0)
                return ($"Review {failedComponents} failed component(s)",
                    "Open each asset, correct the recorded blocker, then use Re-check. Mark " +
                    "handled only after resolving it by hand.", Tab.Analysis, false);
            var assemblies = CountByTypeAndStatus(FindingType.AssemblyDef, MigrationStatus.NotStarted);
            if (assemblies > 0)
                return ($"Point {assemblies} assembly definition(s) at UniText",
                    "Do this before the rewrite: a script that names UniText types does not " +
                    "compile until its assembly references them.", Tab.Analysis, false);
            var scripts = CountByTypeAndStatus(FindingType.ScriptReference, MigrationStatus.NotStarted);
            if (scripts > 0)
                return ($"Rewrite {scripts} script reference(s)",
                    "Components first, then scripts — otherwise serialized references break.",
                    Tab.ScriptPreview, false);
            var leftovers = CleanupPending();
            if (leftovers > 0)
                return ($"Clean up {leftovers} remaining item(s)",
                    "Materials, animation curves and rich-text assets are reviewed by hand — " +
                    "migrate or skip each one.", Tab.Analysis, false);
            if (session.partialScan)
                return ("Re-scan the project",
                    "The last scan was interrupted, so nothing here can be called complete.",
                    Tab.Dashboard, false);
            var outstanding = OutstandingRepairCount();
            if (outstanding > 0)
                return ($"Finish {outstanding} outstanding reference repair(s)",
                    "Assets that could not be written still point at what the migration " +
                    "replaced. Make them writable, then retry the repair.", Tab.Dashboard, false);
            return ("Migration complete", "Every finding is migrated or skipped.", Tab.Dashboard, true);
        }

        private string Pct(int count)
        {
            return summary.totalFindings == 0
                ? "0%"
                : $"{count * 100 / summary.totalFindings}%";
        }

        private static void PingAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
