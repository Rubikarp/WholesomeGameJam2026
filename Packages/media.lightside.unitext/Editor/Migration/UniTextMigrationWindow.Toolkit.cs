using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal partial class UniTextMigrationWindow
    {
        private const float RowHeight = InspectorVisuals.CollectionRowHeight;
        private const string ChromeClass = "unitext-migration__chrome";
        private const string ActionClass = "unitext-migration__action";
        private const string GrowClass = "unitext-migration__grow";
        private const string PrimaryClass = "unitext-migration__primary";
        private const string SelectedTabClass = "lightside-choice-chip--selected";

        private readonly List<Button> tabButtons = new();
        private readonly List<int> filteredFindings = new();
        private readonly List<int> filteredLog = new();

        /// <summary>Folder rows the tree currently shows, parents ahead of the children they reveal.</summary>
        private readonly List<FolderNode> folderRows = new();

        /// <summary>Folders the reader has opened. Absent means closed, so a fresh tree starts shut.</summary>
        private readonly HashSet<string> expandedFolders = new(StringComparer.Ordinal);
        private List<FolderNode> folderTree = new();

        /// <summary>Whether the roots have been opened once. A root shut over the whole project hides everything behind one click.</summary>
        private bool foldersSeeded;
        private InspectorStack tabContent;
        private ProgressBar scanProgress;
        private ListView findingList;
        private ListView folderList;
        private Label selectionSummary;
        private Button migrateSelectedButton;
        private Button skipSelectedButton;
        private float displayedScanProgress = -1f;

        /// <summary>
        /// Where the Analysis lists stood when the tab was last torn down. A row action rebuilds
        /// the whole tab, and the reader's place in a list thousands of rows long is not the
        /// rebuild's to discard.
        /// </summary>
        private Vector2 analysisScroll;
        private Vector2 folderScroll;

        private bool IsScanning => analyzer != null && analyzer.IsScanning;

        /// <summary>The scanner reads YAML directly, so binary scenes and prefabs are invisible to it.</summary>
        private static bool IsTextSerialization
            => EditorSettings.serializationMode == SerializationMode.ForceText;

        private void CreateToolkitGUI()
        {
            var panel = rootVisualElement;
            UniTextInspectorTheme.Initialize(panel);
            InspectorVisuals.ClearContent(panel);

            var root = InspectorVisuals.CreateWindowRoot(panel);
            root.AddToClassList("unitext-tools__content");

            if (loadFailure != null)
            {
                root.Add(CreateLoadFailureUI());
                return;
            }

            root.Add(CreateTabBar());

            tabContent = InspectorVisuals.CreateStack();
            tabContent.AddToClassList("unitext-migration__body");
            root.Add(tabContent);

            panel.schedule.Execute(UpdateScanProgress).Every(100);
            RenderToolkitTab();
            if (OutstandingRepairCount() > 0) panel.schedule.Execute(RetryOutstandingRepairs);
        }

        private VisualElement CreateLoadFailureUI()
        {
            var section = InspectorVisuals.CreateSection("The migration cannot read its own state");
            section.Add(new HelpBox(loadFailure, HelpBoxMessageType.Error));
            section.Add(CreateNote(
                "This document lives under ProjectSettings/UniText/ and travels through version " +
                "control. A merge conflict, a hand edit or an older package can leave it " +
                "unreadable. Restoring it keeps every recorded decision; resetting starts over."));

            var actions = InspectorVisuals.CreateRow();
            actions.Add(new Button(() => EditorUtility.RevealInFinder(loadFailurePath))
            {
                text = "Show the file",
                tooltip = loadFailurePath,
            });
            var reset = new Button(ResetUnreadableDocument)
            {
                text = "Delete it and start over",
                tooltip = "Discards every status, manual review and font mapping it recorded.",
            };
            reset.AddToClassList(ActionClass);
            actions.Add(reset);
            section.Add(actions);
            return section;
        }

        private VisualElement CreateTabBar()
        {
            var bar = InspectorVisuals.CreateRow();
            bar.AddToClassList("unitext-migration__tabs");
            bar.AddToClassList(ChromeClass);
            tabButtons.Clear();
            for (var i = 0; i < tabLabels.Length; i++)
            {
                var tab = (Tab)i;
                var button = new Button(() => SelectTab(tab))
                {
                    text = tabLabels[i],
                    tooltip = TabHint(tab),
                };
                button.AddToClassList("lightside-choice-chip");
                tabButtons.Add(button);
                bar.Add(button);
            }
            RefreshTabBar();
            return bar;
        }

        private static string TabHint(Tab tab) => tab switch
        {
            Tab.Dashboard => "Scan results, migration order and the next action.",
            Tab.Analysis => "Every finding, filterable — where work is selected and applied.",
            Tab.FontMapping => "Pairs each TMP font with the UniText font stack that replaces it.",
            Tab.ScriptPreview => "Per-file diff of the C# rewrite before it is written to disk.",
            Tab.Losses =>
                "Components UniText could not satisfy, with everything each was configured with.",
            Tab.Settings => "Folders excluded from scanning, and the new-TMP-usage guard.",
            _ => "Everything the tool did, with restore points for rewritten scripts.",
        };

        private void SelectTab(Tab tab)
        {
            currentTab = tab;
            RefreshTabBar();
            RenderToolkitTab();
        }

        private void RefreshTabBar()
        {
            for (var i = 0; i < tabButtons.Count; i++)
                tabButtons[i].EnableInClassList(SelectedTabClass, i == (int)currentTab);
        }

        private void UpdateScanProgress()
        {
            if (!IsScanning || currentTab != Tab.Dashboard) return;
            if (scanProgress == null)
            {
                RenderToolkitTab();
                return;
            }
            if (Mathf.Approximately(displayedScanProgress, analyzer.Progress)) return;
            displayedScanProgress = analyzer.Progress;
            scanProgress.value = analyzer.Progress * 100f;
            scanProgress.title = $"Scanning… {(int)(analyzer.Progress * 100f)}% — {analyzer.CurrentFile}";
        }

        /// <summary>
        /// Queues the tab rebuild for the next frame, so a row can raise it from inside its own
        /// click without tearing down the hierarchy the event is still travelling through.
        /// </summary>
        private void RenderDeferred()
        {
            tabContent?.schedule.Execute(RenderToolkitTab);
        }

        private void RenderToolkitTab()
        {
            if (tabContent == null) return;
            analysisScroll = findingList?.Q<ScrollView>()?.scrollOffset ?? analysisScroll;
            folderScroll = folderList?.Q<ScrollView>()?.scrollOffset ?? folderScroll;
            scanProgress = null;
            findingList = null;
            folderList = null;
            selectionSummary = null;
            migrateSelectedButton = null;
            skipSelectedButton = null;
            InspectorVisuals.ClearContent(tabContent);
            tabContent.Add(currentTab switch
            {
                Tab.Dashboard => CreateDashboardUI(),
                Tab.Analysis => CreateAnalysisUI(),
                Tab.FontMapping => CreateFontMappingUI(),
                Tab.ScriptPreview => CreateScriptPreviewUI(),
                Tab.Losses => CreateLossesUI(),
                Tab.Settings => CreateSettingsUI(),
                _ => CreateLogUI(),
            });
        }

        private VisualElement CreateDashboardUI()
        {
            var scroll = CreatePane();
            scroll.Add(CreateScanCard());
            if (findings.Count == 0 && !IsScanning)
            {
                scroll.Add(new HelpBox(EmptyStateMessage(), HelpBoxMessageType.Info));
                return scroll;
            }

            summary = MigrationSummary.Compute(findings);
            scroll.Add(CreateOrderCard());
            if (NeedsStylePreset) scroll.Add(CreateVocabularyCard());
            scroll.Add(CreateOnePassCard());
            scroll.Add(CreateProgressCard());
            scroll.Add(CreateInventoryCard());
            scroll.Add(CreateComplexityCard());
            AddWarnings(scroll);
            return scroll;
        }

        /// <summary>
        /// What an empty finding list means, which is three different things: nothing scanned yet,
        /// a scan whose results another UniText release recorded and this one does not reuse, or
        /// a scan that read the project and found no TextMesh Pro in it.
        /// </summary>
        private string EmptyStateMessage()
        {
            if (!string.IsNullOrEmpty(session.scanTime))
                return $"The scan on {session.scanTime} read {session.scannedFiles.Count} file(s) " +
                       "and found no TextMesh Pro usage. It reads what is on disk: a scene or " +
                       "prefab with unsaved changes counts as last saved — save it, then scan again.";
            var invitation = "Scan the project to find every TextMesh Pro usage. A scan only reads " +
                             "files — nothing is changed.";
            return string.IsNullOrEmpty(stateData.lastScanTime)
                ? invitation
                : $"The scan from {stateData.lastScanTime} was recorded by another UniText " +
                  $"release and is not reused. {invitation}";
        }

        private VisualElement CreateScanCard()
        {
            var card = InspectorVisuals.CreateCard();
            if (IsScanning)
            {
                scanProgress = new ProgressBar
                {
                    value = analyzer.Progress * 100f,
                    title = $"Scanning… {(int)(analyzer.Progress * 100f)}% — {analyzer.CurrentFile}",
                };
                card.Add(scanProgress);
                card.Add(new Button(analyzer.Cancel)
                {
                    text = "Stop scan",
                    tooltip = "Keeps whatever has been found so far.",
                });
                return card;
            }

            if (!IsTextSerialization)
                card.Add(new HelpBox(
                    "Asset Serialization is not Force Text. The scan reads scenes and prefabs as " +
                    "text, so binary assets would report no TMP usage at all. Set it in " +
                    "Edit → Project Settings → Editor → Asset Serialization, then scan.",
                    HelpBoxMessageType.Error));

            var actions = InspectorVisuals.CreateRow();
            var scan = new Button(StartScan)
            {
                text = findings.Count == 0 ? "Scan project" : "Re-scan project",
                tooltip = "Reads scenes, prefabs, scripts and assets. Statuses you already set are kept.",
            };
            scan.AddToClassList(PrimaryClass);
            scan.SetEnabled(IsTextSerialization);
            actions.Add(scan);
            if (findings.Count > 0)
                actions.Add(CreateAction("Verify", VerifyMigrations,
                    "Re-reads every completed component and returns to Pending anything that " +
                    "still contains TMP — useful after reverting files in version control."));
            actions.Add(CreateAction("Export report", ExportReport,
                "Writes the full finding list to a text file."));
            card.Add(actions);

            if (!string.IsNullOrEmpty(stateData.lastScanTime))
                card.Add(CreateNote(session.partialScan
                    ? $"Last scan: {stateData.lastScanTime} (interrupted — re-scan for the full " +
                      "picture)"
                    : $"Last scan: {stateData.lastScanTime}"));
            if (!string.IsNullOrEmpty(session.scanFailure))
                card.Add(new HelpBox(
                    $"The last scan stopped on an error and describes only part of the project. " +
                    session.scanFailure, HelpBoxMessageType.Error));
            return card;
        }

        /// <summary>
        /// The four ordered stages with the current one highlighted, carrying the actions that
        /// advance it.
        /// </summary>
        private VisualElement CreateOrderCard()
        {
            var card = InspectorVisuals.CreateSection("Migration order");
            var componentsPending =
                CountByTypeAndStatus(FindingType.Component, MigrationStatus.NotStarted);
            var componentsFailed =
                CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed);
            var componentsUnresolved = componentsPending + componentsFailed;
            var scriptStagePending = ScriptStagePending();
            var cleanupPending = CleanupPending();
            var current = 0;
            if (IsFontMappingComplete())
                current = componentsUnresolved > 0 ? 1 : scriptStagePending > 0 ? 2 : 3;

            var fontCount = fontMappingsData.fontMappings.Count;
            card.Add(CreateOrderStep(0, current, "Fonts — mapped, never converted in place",
                IsFontMappingComplete(),
                IsFontMappingComplete()
                    ? $"{fontCount} mapped"
                    : $"{UnmappedFontCount()} of {fontCount} unmapped"));
            card.Add(CreateOrderStep(1, current, "Components — prefabs bottom-up, then scenes",
                componentsUnresolved == 0,
                componentsFailed == 0
                    ? $"{componentsPending} pending"
                    : $"{componentsPending} pending · {componentsFailed} failed"));
            card.Add(CreateOrderStep(2, current, "Scripts — with the assemblies that compile them",
                scriptStagePending == 0, $"{scriptStagePending} pending"));
            card.Add(CreateOrderStep(3, current, "Cleanup — materials, animations, text assets",
                cleanupPending == 0, $"{cleanupPending} pending"));

            var step = NextStep();
            if (step.done)
                card.Add(InspectorVisuals.CreateStatusLabel(step.title, EditorResources.StatusSuccess));

            var reason = componentsPending > 0 ? ComponentMigrationBlockReason() : null;
            if (reason != null)
                card.Add(InspectorVisuals.CreateStatusLabel(reason, EditorResources.StatusWarning));
            else
                card.Add(CreateNote(step.detail));

            var actions = InspectorVisuals.CreateRow();
            if (componentsPending > 0)
            {
                var simple = CountSimpleComponentsPending();
                var migrate = new Button(MigrateSimpleComponents)
                {
                    text = $"Migrate simple components ({simple})",
                    tooltip = reason ??
                              "Rewrites only the pending components the analyser classed Simple, " +
                              "even where a scene also holds Complex ones. Everything else is " +
                              "migrated from the Analysis tab.",
                };
                migrate.AddToClassList(PrimaryClass);
                migrate.SetEnabled(simple > 0 && reason == null);
                actions.Add(migrate);

                var all = CreateAction($"Migrate all pending ({componentsPending})",
                    MigrateAllComponents,
                    reason ??
                    "Rewrites every pending component, whatever its complexity. Anything the " +
                    "migrator cannot convert stays put and is reported in the Log.");
                all.SetEnabled(reason == null);
                actions.Add(all);
            }
            var outstanding = OutstandingRepairCount();
            if (outstanding > 0)
            {
                actions.Add(CreateAction($"Retry reference repair ({outstanding})",
                    RetryOutstandingRepairs,
                    "Re-runs the redirects an earlier pass could not write. The assets must be " +
                    "writable — check them out of version control first."));
            }
            if (!step.done)
            {
                var go = new Button(() => GoToStep(step.tab))
                {
                    text = $"Open {tabLabels[(int)step.tab]}",
                };
                go.AddToClassList(componentsPending > 0 ? ActionClass : PrimaryClass);
                actions.Add(go);
            }
            if (actions.childCount > 0) card.Add(actions);
            return card;
        }

        private VisualElement CreateOrderStep(int index, int current, string label, bool done, string detail)
        {
            var row = InspectorVisuals.CreateRow();
            var mark = new Label(done ? "✔" : index == current ? "▶" : "•");
            mark.AddToClassList("unitext-migration__mark");
            mark.style.color = done
                ? EditorResources.StatusSuccess
                : index == current
                    ? EditorResources.ToggleAccent
                    : EditorResources.ForSkin(Color.gray);
            row.Add(mark);
            var text = new Label($"{index + 1}. {label}");
            text.AddToClassList("unitext-migration__wrap");
            text.EnableInClassList("unitext-migration__strong", index == current && !done);
            row.Add(text);
            row.Add(CreateValueLabel(detail));
            return row;
        }

        /// <summary>Whether the project still has no Style source behind UniText's markup.</summary>
        private bool NeedsStylePreset
            => findings.Count > 0 && UniTextSettings.GlobalStylePreset == null;

        /// <summary>
        /// The project-wide markup vocabulary, offered while nothing supplies the modifiers behind
        /// UniText's tags.
        /// </summary>
        private VisualElement CreateVocabularyCard()
        {
            var card = InspectorVisuals.CreateSection("Markup vocabulary");
            card.Add(new HelpBox(
                "UniText has no built-in tag vocabulary — a tag works because a Style entry says " +
                "it does. No project-wide Style preset is assigned, so TMP markup renders as " +
                "literal characters in every migrated text.",
                HelpBoxMessageType.Warning));
            card.Add(CreateNote(
                $"The preset carries all {MigrationMapping.TagVocabulary.Count} entries — every " +
                "TMP tag UniText can reproduce, plus the names UniText spells those same tags by " +
                "— not only the ones this scan saw: text that reaches a component at runtime, " +
                "from localisation or from script, is never scanned and its markup has to work " +
                "too. <link> is wired to the package's LinkPreset modifier graph, which carries " +
                "the link's interaction rules and states."));

            if (session.sharedTags.Count > 0)
            {
                card.Add(CreateNote("Found in the scanned text:"));
                var tags = new Label(string.Join(",  ", session.sharedTags))
                {
                    enableRichText = false,
                };
                tags.AddToClassList("unitext-migration__note");
                card.Add(tags);
            }

            var create = new Button(CreateGlobalStylePreset)
            {
                text = $"Create Style preset ({MigrationMapping.TagVocabulary.Count} tags)",
                tooltip = "Creates a StylePreset carrying the whole vocabulary and assigns it in " +
                          "Project Settings → UniText, where every component reads it. Tags " +
                          "needing an asset you must choose — sprite, material, quad, gradient — " +
                          "stay out and are configured per component.",
            };
            create.AddToClassList(PrimaryClass);
            card.Add(create);
            return card;
        }

        /// <summary>
        /// The whole automatic migration behind one button, for projects that do not want to walk
        /// the stages by hand.
        /// </summary>
        private VisualElement CreateOnePassCard()
        {
            var card = InspectorVisuals.CreateSection("Run it in one pass");
            var fonts = CreatableFontCount();
            var components = CountByTypeAndStatus(FindingType.Component, MigrationStatus.NotStarted);
            var failedComponents =
                CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed);
            var scripts = CountByTypeAndStatus(FindingType.ScriptReference, MigrationStatus.NotStarted);
            card.Add(CreateNote(
                "Builds the fonts it can, migrates every pending component, then rewrites every " +
                "pending script — the same order as the stages above, stopping at the first " +
                "stage it cannot finish."));
            card.Add(CreateNote(
                "A TMP_InputField is converted too: the field box becomes UniTextEditable, " +
                "its text child gains UniTextSelectable, the placeholder folds in as a " +
                "PlaceholderDecorator, and the TMP_InputField is removed. Settings with no " +
                "UniText counterpart — a caret width, a per-field blink rate, a scroll policy, " +
                "persistent UnityEvent listeners — do not stop the field: it migrates, and each " +
                "value is listed under Not carried over. Only a field whose composition cannot " +
                "be rebuilt is refused. Dropdowns, materials, animation curves and rich-text " +
                "assets are never touched and stay on the list."));
            var manual = ManualPendingCount();
            if (manual > 0)
                card.Add(CreateNote($"{manual} finding(s) have no automatic path and will still " +
                                    "be waiting afterwards."));
            if (failedComponents > 0)
            {
                card.Add(InspectorVisuals.CreateStatusLabel(
                    $"{failedComponents} failed component(s) stay untouched until an explicit " +
                    "Re-check. Script rewriting remains blocked while they are unresolved.",
                    EditorResources.StatusWarning));
                var shared = DominantFailureNote();
                if (!string.IsNullOrEmpty(shared)) card.Add(CreateNote(shared.TrimStart()));
            }
            var stalled = fonts == 0 && !IsFontMappingComplete();
            if (stalled)
                card.Add(InspectorVisuals.CreateStatusLabel(
                    $"{UnmappedFontCount()} TMP font(s) have no source file to build from — " +
                    "assign or skip them in Font Mapping first.",
                    EditorResources.StatusWarning));
            var run = new Button(MigrateEverything)
            {
                text = $"Run all: {fonts} font(s), {components} component(s), {scripts} script(s)",
                tooltip = "Asks for confirmation and names every count before touching anything. " +
                          "Commit to version control first.",
            };
            run.AddToClassList(PrimaryClass);
            var runnable = fonts + components + (failedComponents == 0 ? scripts : 0);
            run.SetEnabled(!stalled && runnable > 0);
            var actions = InspectorVisuals.CreateRow();
            actions.Add(run);
            card.Add(actions);
            return card;
        }

        /// <summary>
        /// How far the migration has come, counted per stage. One number over every finding reads
        /// as failure while a stage is simply finished and the next has not run, and it can never
        /// reach the end because some findings are hand work by design.
        /// </summary>
        private VisualElement CreateProgressCard()
        {
            var card = InspectorVisuals.CreateSection("Progress");

            var automatic = summary.componentCount + summary.scriptCount + summary.fontCount +
                            summary.asmdefCount;
            var byHand = summary.totalFindings - automatic;
            var handled = summary.completed + summary.skipped;
            var automaticHandled = Math.Min(handled, automatic);
            var ratio = automatic == 0 ? 1f : (float)automaticHandled / automatic;

            card.Add(new ProgressBar
            {
                value = ratio * 100f,
                title = $"{(int)(ratio * 100f)}% of the {automatic} finding(s) the tool migrates",
            });

            AddStageRow(card, "Components", FindingType.Component);
            AddStageRow(card, "Scripts", FindingType.ScriptReference);
            AddStageRow(card, "Fonts", FindingType.FontAsset);
            AddStageRow(card, "Assembly definitions", FindingType.AssemblyDef);

            if (byHand > 0)
                card.Add(CreateNote(
                    $"{byHand} further finding(s) — text assets carrying TMP markup, TMP settings " +
                    "and style sheets, materials and animation curves — are hand work by design " +
                    "and are not counted above. Skip each once you have dealt with it."));
            if (summary.failed > 0)
                card.Add(InspectorVisuals.CreateStatusLabel(
                    $"{summary.failed} finding(s) failed — Analysis keeps the reason for each.",
                    EditorResources.StatusWarning));
            if (stateData.excludedPaths.Count > 0)
                card.Add(InspectorVisuals.CreateStatusLabel(
                    $"{stateData.excludedPaths.Count} path(s) excluded — nothing under them is " +
                    "migrated, and no reference inside them is repaired. Settings lists each one.",
                    EditorResources.StatusWarning));
            if (summary.unreadableFileCount > 0)
            {
                var row = InspectorVisuals.CreateRow();
                var note = InspectorVisuals.CreateStatusLabel(
                    $"{summary.unreadableFileCount} asset(s) could not be read — nothing inside " +
                    "them is scanned, migrated or repaired, and the script rewrite holds until " +
                    "each is excluded or made readable.", EditorResources.StatusWarning);
                note.AddToClassList(GrowClass);
                row.Add(note);
                row.Add(CreateAction($"Exclude {summary.unreadableFileCount} unreadable",
                    ExcludeUnreadable,
                    "Takes every asset the scan could not read out of the migration, so the run " +
                    "finishes without them. Each stays yours to migrate by hand."));
                card.Add(row);
            }
            return card;
        }

        /// <summary>One stage's own tally, so a finished stage reads as finished.</summary>
        private void AddStageRow(InspectorSection card, string label, FindingType type)
        {
            var done = 0;
            var pending = 0;
            var failed = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                if (findings[i].type != type) continue;
                switch (findings[i].status)
                {
                    case MigrationStatus.Completed:
                    case MigrationStatus.Skipped: done++; break;
                    case MigrationStatus.Failed: failed++; break;
                    default: pending++; break;
                }
            }
            var total = done + pending + failed;
            if (total == 0) return;

            var detail = failed == 0
                ? $"{done} of {total}"
                : $"{done} of {total} · {failed} failed";
            var row = InspectorVisuals.CreateRow();
            var name = new Label(label);
            name.AddToClassList("unitext-migration__wrap");
            row.Add(name);
            row.Add(InspectorVisuals.CreateStatusLabel(detail,
                pending == 0 && failed == 0
                    ? EditorResources.StatusSuccess
                    : pending == 0
                        ? EditorResources.StatusWarning
                        : EditorResources.StatusInfo));
            card.Add(row);
        }

        private VisualElement CreateInventoryCard()
        {
            var card = InspectorVisuals.CreateSection("What was found");
            AddInventoryRow(card, "Components", summary.componentCount, FindingType.Component);
            AddInventoryRow(card, "Script references", summary.scriptCount, FindingType.ScriptReference);
            AddInventoryRow(card, "Fonts", summary.fontCount, FindingType.FontAsset);
            AddInventoryRow(card, "Materials", summary.materialCount, FindingType.Material);
            AddInventoryRow(card, "Animations", summary.animationCount, FindingType.Animation);
            AddInventoryRow(card, "Assembly definitions", summary.asmdefCount, FindingType.AssemblyDef);
            AddInventoryRow(card, "Rich-text assets", summary.richTextContentCount, FindingType.RichTextContent);
            AddInventoryRow(card, "Missing scripts", summary.missingScriptCount, FindingType.MissingScript);
            AddInventoryRow(card, "Unreadable files", summary.unreadableFileCount,
                FindingType.UnreadableFile);
            AddInventoryRow(card, "TMP assets", summary.tmpAssetCount, FindingType.TmpAsset);
            return card;
        }

        private void AddInventoryRow(InspectorSection card, string label, int count, FindingType type)
        {
            if (count == 0) return;
            var row = InspectorVisuals.CreateRow();
            var name = new Label(label) { tooltip = TypeHandling(type) };
            name.AddToClassList("unitext-migration__wrap");
            row.Add(name);
            row.Add(CreateValueLabel(count.ToString()));
            row.Add(CreateAction("Show", () => GoToFindings(type),
                "Opens Analysis filtered to this kind of finding."));
            card.Add(row);
        }

        private VisualElement CreateComplexityCard()
        {
            var card = InspectorVisuals.CreateSection("How hard each finding is");
            AddComplexityRow(card, MigrationComplexity.Simple, summary.simpleCount);
            AddComplexityRow(card, MigrationComplexity.Moderate, summary.moderateCount);
            AddComplexityRow(card, MigrationComplexity.Complex, summary.complexCount);
            AddComplexityRow(card, MigrationComplexity.Manual, summary.manualCount);
            card.Add(CreateNote(
                "These counts span every kind of finding — scripts and assets included, not just " +
                "components. That is why they are larger than the number of components any single " +
                "button migrates."));
            return card;
        }

        private void AddComplexityRow(InspectorSection card, MigrationComplexity complexity, int count)
        {
            var row = InspectorVisuals.CreateRow();
            var name = new Label(complexity.ToString());
            name.AddToClassList("unitext-migration__complexity");
            name.AddToClassList("unitext-migration__strong");
            name.style.color = ComplexityColor(complexity);
            row.Add(name);
            var hint = new Label(ComplexityHint(complexity));
            hint.AddToClassList("unitext-migration__wrap");
            row.Add(hint);
            row.Add(CreateValueLabel($"{count} ({Pct(count)})"));
            card.Add(row);
        }

        private void AddWarnings(InspectorScrollStack root)
        {
            if (!IsFontMappingComplete())
                root.Add(new HelpBox(
                    $"{UnmappedFontCount()} TMP font(s) have no UniText font stack yet. " +
                    "Components cannot be migrated until every font is mapped or skipped — " +
                    "open Font Mapping.",
                    HelpBoxMessageType.Warning));
            var manualScripts = findings.Count(finding =>
                finding.type == FindingType.ScriptReference &&
                finding.complexity == MigrationComplexity.Manual);
            if (manualScripts > 0)
                root.Add(new HelpBox(
                    $"{manualScripts} script(s) use TMP APIs with no UniText equivalent " +
                    "(sprite assets, dropdowns, textInfo). The rewrite leaves them alone; " +
                    "port them by hand.",
                    HelpBoxMessageType.Warning));
            var compiledDependencies = findings.Count(finding =>
                finding.type == FindingType.CompiledDependency);
            if (compiledDependencies > 0)
                root.Add(new HelpBox(
                    $"{compiledDependencies} compiled assembly/assemblies expose TMP types. " +
                    "They must be rebuilt against UniText by whoever ships them.",
                    HelpBoxMessageType.Warning));
        }

        private VisualElement CreateAnalysisUI()
        {
            var root = CreatePaneStack();
            if (findings.Count == 0)
            {
                root.Add(new HelpBox("Nothing to show yet — run a scan from the Dashboard.",
                    HelpBoxMessageType.Info));
                return root;
            }

            var filters = InspectorVisuals.CreateRow();
            filters.AddToClassList(ChromeClass);
            var typeIndex = filterType.HasValue ? (int)filterType.Value + 1 : 0;
            var type = new SelectorField<string>("Type", typeFilterOptions, typeIndex);
            type.AddToClassList(ActionClass);
            var statusIndex = filterStatus.HasValue ? (int)filterStatus.Value + 1 : 0;
            var status = new SelectorField<string>("Status", statusFilterOptions, statusIndex);
            status.AddToClassList(ActionClass);
            var search = new InspectorSearchField("Path or description…") { value = searchText };
            search.AddToClassList(GrowClass);
            filters.Add(type);
            filters.Add(status);
            filters.Add(search);
            root.Add(filters);

            root.Add(CreateNote(
                "Only components are rewritten in place. Scripts are applied from Script Preview, " +
                "fonts from Font Mapping; materials, animations, assembly definitions and " +
                "rich-text assets are handled by hand — tick them and press Skip once done. Pick a " +
                "folder on the left to work through one part of the project at a time; Exclude " +
                "leaves a folder out of the migration for good."));

            var workspace = new VisualElement();
            workspace.AddToClassList("unitext-migration__workspace");

            folderList = new ListView(folderRows, RowHeight,
                () => new FolderRow(this),
                (element, index) => ((FolderRow)element).Bind(folderRows[index]))
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                reorderable = false,
                horizontalScrollingEnabled = false,
            };
            folderList.AddToClassList("unitext-migration__folders");
            workspace.Add(folderList);

            var listing = InspectorVisuals.CreateStack();
            listing.AddToClassList("unitext-migration__preview");

            var header = InspectorVisuals.CreateRow();
            header.AddToClassList(ChromeClass);
            var selectAll = new InspectorToggle("Select everything shown")
            {
                tooltip = "Ticking a row includes it in the two bulk actions at the bottom.",
            };
            selectAll.AddToClassList(ActionClass);
            selectAll.RegisterValueChangedCallback(evt => SetSelectionForShown(evt.newValue));
            header.Add(selectAll);
            selectionSummary = new Label();
            selectionSummary.AddToClassList("unitext-migration__summary");
            header.Add(selectionSummary);
            listing.Add(header);

            findingList = new ListView(filteredFindings, RowHeight,
                () => new FindingRow(this),
                (element, index) => ((FindingRow)element).Bind(findings[filteredFindings[index]]))
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                reorderable = false,
                horizontalScrollingEnabled = false,
            };
            findingList.AddToClassList("unitext-migration__list");
            listing.Add(findingList);
            workspace.Add(listing);
            root.Add(workspace);

            var actions = InspectorVisuals.CreateRow();
            actions.AddToClassList(ChromeClass);
            migrateSelectedButton = new Button(MigrateSelected);
            migrateSelectedButton.AddToClassList(PrimaryClass);
            skipSelectedButton = new Button(SkipSelected);
            skipSelectedButton.AddToClassList(ActionClass);
            actions.Add(migrateSelectedButton);
            actions.Add(skipSelectedButton);
            var failed = CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed);
            if (failed > 0)
            {
                actions.Add(CreateAction($"Re-check all failed ({failed})", RecheckAllFailed,
                    "Runs every gate again on each failed component, in one pass. One " +
                    "project-wide cause — a missing Input Field Prefab, a font left unmapped — " +
                    "clears them all at once."));
                actions.Add(CreateAction($"Mark all failed handled ({failed})",
                    MarkAllFailedHandled,
                    "Closes every recorded review at once. A component still present in its " +
                    "asset comes straight back to Pending, so this cannot hide one."));
            }
            actions.Add(CreateAction("Clear", () =>
                {
                    ClearSelection();
                    findingList.RefreshItems();
                    RefreshSelectionState();
                },
                "Unticks every row, including rows hidden by the current filter."));
            root.Add(actions);

            type.RegisterValueChangedCallback(_ =>
            {
                var index = type.Index;
                filterType = index <= 0 ? null : (FindingType?)(index - 1);
                RefreshAnalysis();
            });
            status.RegisterValueChangedCallback(_ =>
            {
                var index = status.Index;
                filterStatus = index <= 0 ? null : (MigrationStatus?)(index - 1);
                RefreshAnalysis();
            });
            search.RegisterValueChangedCallback(evt =>
            {
                searchText = evt.newValue;
                RefreshAnalysis();
            });

            RefreshAnalysis();
            RestoreScroll(findingList, analysisScroll);
            RestoreScroll(folderList, folderScroll);
            return root;
        }

        /// <summary>
        /// Puts a list back where it stood, once it has a height to scroll within. Setting the
        /// offset before the first layout clamps it to zero, which is the very thing being undone.
        /// </summary>
        private static void RestoreScroll(ListView list, Vector2 target)
        {
            if (target == Vector2.zero) return;

            EventCallback<GeometryChangedEvent> apply = null;
            apply = _ =>
            {
                list.UnregisterCallback(apply);
                var scroll = list.Q<ScrollView>();
                if (scroll != null) scroll.scrollOffset = target;
            };
            list.RegisterCallback(apply);
        }

        private void GoToFindings(FindingType type)
        {
            filterType = type;
            filterStatus = null;
            searchText = string.Empty;
            filterFolder = null;
            analysisScroll = Vector2.zero;
            SelectTab(Tab.Analysis);
        }

        private void GoToStep(Tab tab)
        {
            if (tab == Tab.Analysis)
            {
                searchText = string.Empty;
                filterFolder = null;
                analysisScroll = Vector2.zero;
                if (CountByTypeAndStatus(FindingType.Component, MigrationStatus.NotStarted) > 0)
                {
                    filterType = FindingType.Component;
                    filterStatus = MigrationStatus.NotStarted;
                }
                else if (CountByTypeAndStatus(FindingType.Component, MigrationStatus.Failed) > 0)
                {
                    filterType = FindingType.Component;
                    filterStatus = MigrationStatus.Failed;
                }
                else
                {
                    filterType = null;
                    filterStatus = MigrationStatus.NotStarted;
                }
            }
            SelectTab(tab);
        }

        /// <summary>One folder of the Analysis tree, and everything the filters leave under it.</summary>
        private sealed class FolderNode
        {
            public string path;
            public string name;
            public int depth;
            public int count;
            public FolderNode parent;
            public List<FolderNode> children;
        }

        private const float FolderIndent = 12f;

        /// <summary>
        /// Rebuilds both halves of the Analysis tab. The tree counts through every filter but its
        /// own, so a folder stays reachable from inside the selection that hides it.
        /// </summary>
        private void RefreshAnalysis()
        {
            RebuildFolderTree();
            RefreshFilteredFindings();
        }

        private void RebuildFolderTree()
        {
            var byPath = new Dictionary<string, FolderNode>(StringComparer.OrdinalIgnoreCase);
            folderTree = new List<FolderNode>();

            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (string.IsNullOrEmpty(finding.filePath) ||
                    !MatchesRowFilters(finding)) continue;
                var cut = finding.filePath.LastIndexOf('/');
                if (cut <= 0) continue;
                for (var node = EnsureFolder(finding.filePath.Substring(0, cut), byPath);
                     node != null;
                     node = node.parent) node.count++;
            }

            if (!string.IsNullOrEmpty(filterFolder) && !byPath.ContainsKey(filterFolder))
                filterFolder = null;
            if (!foldersSeeded && folderTree.Count > 0)
            {
                foldersSeeded = true;
                for (var i = 0; i < folderTree.Count; i++) expandedFolders.Add(folderTree[i].path);
            }
            SortFolders(folderTree);
            RefreshFolderRows();
        }

        /// <summary>The node for one folder, with every ancestor it needs created ahead of it.</summary>
        private FolderNode EnsureFolder(string path, Dictionary<string, FolderNode> byPath)
        {
            if (byPath.TryGetValue(path, out var existing)) return existing;

            var cut = path.LastIndexOf('/');
            var parent = cut > 0 ? EnsureFolder(path.Substring(0, cut), byPath) : null;
            var node = new FolderNode
            {
                path = path,
                name = cut < 0 ? path : path.Substring(cut + 1),
                depth = parent == null ? 0 : parent.depth + 1,
                parent = parent,
            };
            if (parent == null) folderTree.Add(node);
            else (parent.children ??= new List<FolderNode>()).Add(node);
            byPath[path] = node;
            return node;
        }

        private static void SortFolders(List<FolderNode> nodes)
        {
            nodes.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            for (var i = 0; i < nodes.Count; i++)
                if (nodes[i].children != null) SortFolders(nodes[i].children);
        }

        /// <summary>Flattens the open part of the tree, with the whole-project row ahead of it.</summary>
        private void RefreshFolderRows()
        {
            folderRows.Clear();
            var total = 0;
            for (var i = 0; i < folderTree.Count; i++) total += folderTree[i].count;
            folderRows.Add(new FolderNode { name = "Everything", count = total });
            AddFolderRows(folderTree);
            folderList?.Rebuild();
        }

        private void AddFolderRows(List<FolderNode> nodes)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                folderRows.Add(nodes[i]);
                if (nodes[i].children != null && expandedFolders.Contains(nodes[i].path))
                    AddFolderRows(nodes[i].children);
            }
        }

        /// <summary>Narrows the list to one folder, opening the tree down to it.</summary>
        private void SelectFolder(FolderNode node)
        {
            filterFolder = node.path;
            for (var ancestor = node.parent; ancestor != null; ancestor = ancestor.parent)
                expandedFolders.Add(ancestor.path);
            if (node.path != null && node.children != null) expandedFolders.Add(node.path);
            RefreshFolderRows();
            RefreshFilteredFindings();
        }

        private void ToggleFolder(FolderNode node)
        {
            if (node.path == null || node.children == null) return;
            if (!expandedFolders.Add(node.path)) expandedFolders.Remove(node.path);
            RefreshFolderRows();
        }

        /// <summary>One folder row, rebound as the virtualized tree scrolls.</summary>
        private sealed class FolderRow : InspectorRow
        {
            private readonly UniTextMigrationWindow window;
            private readonly Label fold = new();
            private readonly Button folderName = new();
            private readonly Label count = new();
            private readonly Button exclude;
            private FolderNode current;

            public FolderRow(UniTextMigrationWindow window)
            {
                this.window = window;
                AddToClassList("unitext-migration__row");
                fold.AddToClassList("lightside-disclosure__glyph");
                fold.AddToClassList("unitext-migration__fold");
                fold.RegisterCallback<ClickEvent>(_ =>
                {
                    if (current != null) window.ToggleFolder(current);
                });
                folderName.AddToClassList("unitext-migration__file");
                folderName.AddToClassList(GrowClass);
                folderName.clicked += () =>
                {
                    if (current != null) window.SelectFolder(current);
                };
                count.AddToClassList("unitext-migration__count");
                exclude = CreateAction("Exclude", () =>
                    {
                        if (current != null) window.ExcludeFolder(current.path);
                    },
                    "Leaves the whole folder to you: nothing under it is scanned, migrated or " +
                    "reference-repaired, now or later.");
                Add(fold);
                Add(folderName);
                Add(count);
                Add(exclude);
            }

            public void Bind(FolderNode node)
            {
                current = node;
                var expandable = node.path != null && node.children != null;
                fold.text = expandable
                    ? window.expandedFolders.Contains(node.path) ? "▼" : "▶"
                    : string.Empty;
                fold.style.visibility = expandable ? Visibility.Visible : Visibility.Hidden;
                fold.style.marginLeft = node.depth * FolderIndent;
                folderName.text = node.name;
                folderName.tooltip = node.path ?? "Every folder the current filters leave something in.";
                folderName.EnableInClassList("unitext-migration__file--selected",
                    string.Equals(node.path, window.filterFolder, StringComparison.OrdinalIgnoreCase));
                count.text = node.count.ToString();
                exclude.style.display = node.path == null ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        private void RefreshFilteredFindings()
        {
            filteredFindings.Clear();
            for (var i = 0; i < findings.Count; i++)
                if (MatchesFilter(findings[i])) filteredFindings.Add(i);
            findingList?.Rebuild();
            RefreshSelectionState();
        }

        private void SetSelectionForShown(bool value)
        {
            for (var i = 0; i < filteredFindings.Count; i++)
            {
                var finding = findings[filteredFindings[i]];
                finding.isSelected = value && finding.status == MigrationStatus.NotStarted;
            }
            findingList?.RefreshItems();
            RefreshSelectionState();
        }

        private void RefreshSelectionState()
        {
            if (selectionSummary == null) return;
            var selected = 0;
            var migratable = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (!finding.isSelected) continue;
                if (finding.status != MigrationStatus.NotStarted)
                {
                    finding.isSelected = false;
                    continue;
                }
                selected++;
                if (finding.type == FindingType.Component &&
                    MigrationMapping.IsMigratableComponent(finding.scriptGuid)) migratable++;
            }

            selectionSummary.text =
                $"{filteredFindings.Count} of {findings.Count} shown · {selected} ticked";
            var reason = ComponentMigrationBlockReason();
            migrateSelectedButton.text = $"Migrate ticked components ({migratable})";
            migrateSelectedButton.tooltip = reason ??
                "Rewrites the prefabs and scenes holding the ticked components. " +
                "Ticked rows of any other kind are left alone.";
            migrateSelectedButton.SetEnabled(migratable > 0 && reason == null);
            skipSelectedButton.text = $"Skip ticked ({selected})";
            skipSelectedButton.tooltip =
                "Marks the ticked rows as handled without changing any file.";
            skipSelectedButton.SetEnabled(selected > 0);
        }

        /// <summary>One fixed-height finding row, rebound as the virtualized list scrolls.</summary>
        private sealed class FindingRow : InspectorRow
        {
            private readonly InspectorToggle selected = new();
            private readonly Label status = new();
            private readonly Label complexity = new();
            private readonly Label warning = new("⚠");
            private readonly Label details = new();
            private readonly Button migrate;
            private readonly Button skip;
            private readonly Button exclude;
            private readonly Button open;
            private readonly Button recheck;
            private readonly Button markHandled;
            private MigrationFinding current;

            public FindingRow(UniTextMigrationWindow window)
            {
                AddToClassList("unitext-migration__row");
                selected.AddToClassList(ActionClass);
                selected.RegisterValueChangedCallback(evt =>
                {
                    if (current == null) return;
                    current.isSelected = evt.newValue;
                    window.RefreshSelectionState();
                });
                status.AddToClassList("unitext-migration__status");
                complexity.AddToClassList("unitext-migration__complexity");
                warning.AddToClassList("unitext-migration__warning");
                warning.style.color = EditorResources.StatusWarning;
                details.AddToClassList("unitext-migration__details");
                migrate = CreateAction("Migrate", () =>
                    {
                        if (current != null) window.MigrateFindingFile(current);
                    },
                    "Rewrites the whole prefab or scene this component lives in.");
                skip = CreateAction("Skip", () =>
                    {
                        if (current != null) window.SkipFinding(current);
                    },
                    "Marks this one finding as handled without changing any file.");
                exclude = CreateAction("Exclude", () =>
                    {
                        if (current != null) window.ExcludeFinding(current);
                    },
                    "Leaves the whole asset to you: nothing in it is scanned, migrated or " +
                    "reference-repaired, now or later.");
                open = CreateAction("Open", () =>
                    {
                        if (current != null) PingAsset(current.filePath);
                    },
                    "Selects the asset in the Project window.");
                recheck = CreateAction("Re-check", () =>
                    {
                        if (current != null) window.RecheckFinding(current);
                    },
                    "Checks the asset again without migrating it.");
                markHandled = CreateAction("Mark handled", () =>
                    {
                        if (current != null) window.SkipFinding(current);
                    },
                    "Closes the recorded review after the asset was resolved by hand.");
                Add(selected);
                Add(status);
                Add(complexity);
                Add(warning);
                Add(details);
                Add(migrate);
                Add(skip);
                Add(exclude);
                Add(open);
                Add(recheck);
                Add(markHandled);
            }

            public void Bind(MigrationFinding finding)
            {
                current = finding;
                selected.SetValueWithoutNotify(finding.isSelected);
                status.text = StatusText(finding.status);
                status.style.color = StatusColor(finding.status);
                complexity.text = finding.complexity.ToString();
                complexity.style.color = ComplexityColor(finding.complexity);
                details.text = FindingDetails(finding);
                var hasWarnings = finding.warnings is { Count: > 0 };
                warning.style.visibility = hasWarnings ? Visibility.Visible : Visibility.Hidden;
                var pending = finding.status == MigrationStatus.NotStarted;
                var failedComponent = finding.status == MigrationStatus.Failed &&
                                      finding.type == FindingType.Component;
                selected.SetEnabled(pending);
                selected.style.visibility = pending ? Visibility.Visible : Visibility.Hidden;
                migrate.style.display = pending && finding.type == FindingType.Component &&
                                        MigrationMapping.IsMigratableComponent(finding.scriptGuid)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                skip.style.display = pending ? DisplayStyle.Flex : DisplayStyle.None;
                exclude.style.display = string.IsNullOrEmpty(finding.filePath)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                recheck.style.display = failedComponent ? DisplayStyle.Flex : DisplayStyle.None;
                markHandled.style.display = failedComponent ? DisplayStyle.Flex : DisplayStyle.None;
                tooltip = BuildTooltip(finding);
            }

            private static string BuildTooltip(MigrationFinding finding)
            {
                var text = $"{finding.filePath}\n{finding.details}\n\n" +
                           $"{finding.type} · {ComplexityHint(finding.complexity)}\n" +
                           TypeHandling(finding.type);
                var review = ManualReviewDetails(finding);
                if (!string.IsNullOrEmpty(review)) text += $"\n\n{review}";
                if (finding.warnings == null) return text;
                for (var i = 0; i < finding.warnings.Count; i++)
                    text += $"\n⚠ {finding.warnings[i]}";
                return text;
            }
        }

        private VisualElement CreateFontMappingUI()
        {
            var scroll = CreatePane();
            if (fontMappingsData.fontMappings.Count == 0)
            {
                scroll.Add(new HelpBox(
                    "No TMP fonts discovered yet — run a scan from the Dashboard.",
                    HelpBoxMessageType.Info));
                return scroll;
            }

            var mapped = fontMappingsData.fontMappings.Count(entry => entry.IsMapped);
            var header = InspectorVisuals.CreateRow();
            var title = InspectorVisuals.CreateSubheading("TMP font → UniText font stack");
            title.AddToClassList(GrowClass);
            header.Add(title);
            header.Add(InspectorVisuals.CreateStatusLabel(
                $"{mapped} of {fontMappingsData.fontMappings.Count} mapped",
                mapped == fontMappingsData.fontMappings.Count
                    ? EditorResources.StatusSuccess
                    : EditorResources.StatusWarning));
            scroll.Add(header);
            scroll.Add(CreateNote(
                "A migrated component points at a UniText font stack, so every TMP font needs one. " +
                "Create it from the original TTF/OTF, assign an existing asset, or skip the font " +
                "when nothing in the project still uses it."));
            scroll.Add(CreateNote(
                "TMP's fallback tables are rebuilt as the ordered families behind each stack's " +
                "primary — a UniText stack resolves a codepoint by walking its families in order. " +
                "A chain is only as complete as its fonts: a fallback with no UniText font of its " +
                "own drops out of it."));

            var globals = fontMappingsData.globalFallbackGuids;
            if (globals is { Count: > 0 })
                scroll.Add(CreateNote(
                    $"TMP Settings lists {globals.Count} project-wide fallback font(s). They are " +
                    "built into one shared stack and chained onto every stack here, so the list " +
                    "stays a single thing to edit."));

            var creatable = CreatableFontCount();
            if (creatable > 0)
            {
                var createAll = new Button(CreateMissingFonts)
                {
                    text = $"Finish every mapping the tool can ({creatable})",
                    tooltip = "Builds whatever half is missing: a UniText font from the source " +
                              "file, and the single-family stack a component actually reads. A " +
                              "row you already gave a font gets its stack built here too. Rows " +
                              "with neither a font nor a source are left for you.",
                };
                createAll.AddToClassList(PrimaryClass);
                var createRow = InspectorVisuals.CreateRow();
                createRow.Add(createAll);
                scroll.Add(createRow);
            }

            for (var i = 0; i < fontMappingsData.fontMappings.Count; i++)
                scroll.Add(CreateFontMappingCard(fontMappingsData.fontMappings[i]));
            return scroll;
        }

        private VisualElement CreateFontMappingCard(FontMappingEntry entry)
        {
            var card = InspectorVisuals.CreateSection(string.IsNullOrEmpty(entry.tmpFontName)
                ? "TMP font"
                : entry.tmpFontName);

            var hasFont = !string.IsNullOrEmpty(entry.uniTextFontGuid);
            var state = InspectorVisuals.CreateRow();
            var stateLabel = InspectorVisuals.CreateStatusLabel(
                entry.skipped ? "Skipped"
                : entry.IsMapped ? "Mapped"
                : hasFont ? "Font assigned, no stack — the tool builds one"
                : entry.HasSource ? "Source found — create the font"
                : "Unmapped — assign a font stack, or Skip",
                entry.skipped ? EditorResources.ForSkin(Color.gray)
                : entry.IsMapped ? EditorResources.StatusSuccess
                : hasFont || entry.HasSource ? EditorResources.StatusWarning
                : EditorResources.StatusError);
            stateLabel.AddToClassList(GrowClass);
            state.Add(stateLabel);
            if (!string.IsNullOrEmpty(entry.tmpFamilyName))
                state.Add(CreateNote(entry.tmpFamilyName));
            card.Add(state);
            AddFallbackChain(card, entry);

            var source = InspectorVisuals.CreateRow();
            var sourceLabel = new Label(entry.HasSource
                ? entry.sourceTtfPath
                : "No TTF/OTF found next to the TMP asset")
            {
                tooltip = entry.HasSource ? entry.sourceTtfPath : null,
            };
            sourceLabel.AddToClassList("unitext-migration__details");
            sourceLabel.style.color = entry.HasSource
                ? EditorResources.StatusSuccess
                : EditorResources.StatusError;
            source.Add(sourceLabel);
            source.Add(CreateAction("Browse…", () => BrowseFontSource(entry),
                "Points at the original font file. One outside Assets is copied in."));
            card.Add(source);

            var font = new InspectorObjectField("UniText font", typeof(UniTextFont))
            {
                value = LoadByGuid<UniTextFont>(entry.uniTextFontGuid),
            };
            font.RegisterValueChangedCallback(evt =>
            {
                entry.uniTextFontGuid = GuidOf(evt.newValue);
                FontMappingChanged();
            });
            card.Add(font);

            var stack = new InspectorObjectField("Font stack", typeof(UniTextFontStack))
            {
                value = LoadByGuid<UniTextFontStack>(entry.uniTextFontStackGuid),
                tooltip = "What migrated components are pointed at. A font alone is not enough.",
            };
            stack.RegisterValueChangedCallback(evt =>
            {
                entry.uniTextFontStackGuid = GuidOf(evt.newValue);
                FontMappingChanged();
            });
            card.Add(stack);

            var actions = InspectorVisuals.CreateRow();
            if (entry.HasSource && string.IsNullOrEmpty(entry.uniTextFontGuid))
            {
                var create = new Button(() =>
                {
                    CreateFontFromSource(entry);
                    FontMappingChanged();
                })
                {
                    text = "Create font + stack",
                    tooltip = "Builds a UniText font and a single-family stack beside the source file.",
                };
                create.AddToClassList(PrimaryClass);
                actions.Add(create);
            }
            actions.Add(CreateAction(entry.skipped ? "Unskip" : "Skip", () =>
                {
                    entry.skipped = !entry.skipped;
                    FontMappingChanged();
                },
                entry.skipped
                    ? "Requires this font to be mapped again."
                    : "Treats this font as handled. Components still using it keep no font."));
            card.Add(actions);
            return card;
        }

        /// <summary>
        /// Shows the TMP fallback order this font carries and marks every link the chain would
        /// lose, since a fallback reaches a migrated stack only through a UniText font of its own.
        /// </summary>
        private void AddFallbackChain(InspectorSection card, FontMappingEntry entry)
        {
            if (entry.fallbackGuids == null || entry.fallbackGuids.Count == 0) return;

            var names = new List<string>(entry.fallbackGuids.Count);
            var missing = 0;
            for (var i = 0; i < entry.fallbackGuids.Count; i++)
            {
                var mapping = fontMappingsData.fontMappings.Find(
                    candidate => candidate.tmpFontGuid == entry.fallbackGuids[i]);
                var resolved = mapping != null && !string.IsNullOrEmpty(mapping.uniTextFontGuid);
                if (!resolved) missing++;
                var name = string.IsNullOrEmpty(mapping?.tmpFontName)
                    ? "(font outside the scan)"
                    : mapping.tmpFontName;
                names.Add(resolved ? name : name + " ⚠");
            }

            card.Add(CreateNote("Falls back to:  " + string.Join("  →  ", names)));
            if (missing > 0)
                card.Add(InspectorVisuals.CreateStatusLabel(
                    $"{missing} font(s) in this chain have no UniText font yet — create or map " +
                    "them, or migrated text loses whatever only they could draw.",
                    EditorResources.StatusWarning));
        }

        private static T LoadByGuid<T>(string guid) where T : UnityEngine.Object
            => string.IsNullOrEmpty(guid)
                ? null
                : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));

        private static string GuidOf(UnityEngine.Object asset)
            => asset == null
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        private void BrowseFontSource(FontMappingEntry entry)
        {
            var path = EditorUtility.OpenFilePanel("Select TTF/OTF", "Assets", "ttf,otf");
            if (string.IsNullOrEmpty(path)) return;
            if (!path.Replace('\\', '/').Contains("/Assets/"))
            {
                var destination = "Assets/" + Path.GetFileName(path);
                File.Copy(path, destination, true);
                AssetDatabase.ImportAsset(destination);
                entry.sourceTtfPath = destination;
            }
            else
            {
                entry.sourceTtfPath = "Assets" + path.Substring(path.IndexOf("/Assets/") + 7);
            }
            FontMappingChanged();
        }

        private VisualElement CreateScriptPreviewUI()
        {
            scriptFiles.Clear();
            scriptFiles.AddRange(PendingScriptFiles());
            if (selectedScriptIndex >= scriptFiles.Count) selectedScriptIndex = -1;

            var root = CreatePaneStack();
            if (scriptFiles.Count == 0)
            {
                root.Add(new HelpBox(
                    "No script rewrites are pending. Scan the project, or check the Analysis tab " +
                    "for scripts already migrated or skipped.",
                    HelpBoxMessageType.Info));
                return root;
            }

            var scriptBlockReason = ScriptRewriteBlockReason();
            if (scriptBlockReason != null)
            {
                var warning = new HelpBox(scriptBlockReason, HelpBoxMessageType.Warning);
                warning.AddToClassList(ChromeClass);
                root.Add(warning);
            }
            else
            {
                var outstanding = ComponentsOutstandingNote();
                if (outstanding != null)
                {
                    var note = new HelpBox(outstanding, HelpBoxMessageType.Info);
                    note.AddToClassList(ChromeClass);
                    root.Add(note);
                }
            }
            root.Add(CreateNote(
                "Every applied file keeps its original beside it as a .bak, and the Log offers " +
                "a Restore for each one."));

            var workspace = new VisualElement();
            workspace.AddToClassList("unitext-migration__workspace");

            var files = new ListView(scriptFiles, RowHeight,
                CreateScriptRowButton,
                (element, index) => BindScriptRow((Button)element, index))
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                reorderable = false,
                horizontalScrollingEnabled = false,
            };
            files.AddToClassList("unitext-migration__files");
            workspace.Add(files);

            var preview = InspectorVisuals.CreateStack();
            preview.AddToClassList("unitext-migration__preview");
            if (selectedScriptIndex >= 0)
            {
                var heading = InspectorVisuals.CreateSubheading(scriptFiles[selectedScriptIndex]);
                heading.AddToClassList(ChromeClass);
                heading.AddToClassList("unitext-migration__elide");
                heading.tooltip = scriptFiles[selectedScriptIndex];
                preview.Add(heading);

                var diff = new InspectorTextArea
                {
                    isReadOnly = true,
                    value = string.IsNullOrEmpty(currentDiff)
                        ? "No replacement is proposed for this file."
                        : currentDiff,
                };
                diff.AddToClassList("unitext-migration__diff");
                preview.Add(diff);

                var applicable = ApplicableCount(currentReplacements);
                var blocked = ScriptMigrator.HasBlockers(currentReplacements);
                if (blocked)
                {
                    var note = new HelpBox(
                        "This file contains TMP_InputField uses that cannot be migrated safely. " +
                        "Nothing in it will be applied; port those uses by hand, then mark it handled.",
                        HelpBoxMessageType.Warning);
                    note.AddToClassList(ChromeClass);
                    preview.Add(note);
                }
                else if (applicable == 0)
                {
                    var note = new HelpBox(
                        "Nothing here can be rewritten automatically — the entries above are " +
                        "warnings. Port the file by hand, then mark it handled.",
                        HelpBoxMessageType.Info);
                    note.AddToClassList(ChromeClass);
                    preview.Add(note);
                }

                var path = scriptFiles[selectedScriptIndex];
                var actions = InspectorVisuals.CreateRow();
                actions.AddToClassList(ChromeClass);
                var apply = new Button(ApplySelectedScript)
                {
                    text = $"Apply this file ({applicable})",
                    tooltip = "Rewrites the file on disk and keeps the original as .bak.",
                };
                apply.AddToClassList(PrimaryClass);
                apply.SetEnabled(applicable > 0 && scriptBlockReason == null);
                if (blocked)
                    apply.tooltip = "Resolve every TMP_InputField blocker in this file first.";
                else if (scriptBlockReason != null)
                    apply.tooltip = scriptBlockReason;
                actions.Add(apply);
                var applyAll = CreateAction($"Apply all ({scriptFiles.Count})", ApplyAllScripts,
                    scriptBlockReason ??
                    "Rewrites every safe pending script file, each with its own .bak; blocked files stay pending.");
                applyAll.SetEnabled(scriptBlockReason == null);
                actions.Add(applyAll);
                actions.Add(CreateAction("Mark handled", () => MarkScriptHandled(path),
                    "Takes the file off the list without touching it."));
                preview.Add(actions);
            }
            else
            {
                preview.Add(new HelpBox("Select a file to see what would change.",
                    HelpBoxMessageType.Info));
            }

            workspace.Add(preview);
            root.Add(workspace);
            return root;
        }

        private VisualElement CreateScriptRowButton()
        {
            var button = new Button();
            button.AddToClassList("unitext-migration__file");
            button.clicked += () =>
            {
                if (button.userData is int index) SelectScript(index);
            };
            return button;
        }

        private void BindScriptRow(Button button, int index)
        {
            button.userData = index;
            button.text = Path.GetFileName(scriptFiles[index]);
            button.tooltip = scriptFiles[index];
            button.EnableInClassList("unitext-migration__file--selected", index == selectedScriptIndex);
        }

        private void SelectScript(int index)
        {
            selectedScriptIndex = index;
            currentReplacements = ScriptMigrator.AnalyzeFile(scriptFiles[index]);
            currentDiff = ScriptMigrator.GenerateDiff(scriptFiles[index], currentReplacements);
            RenderDeferred();
        }

        /// <summary>
        /// Everything the migration could not carry over: components it had to take off, and
        /// settings that have no UniText counterpart. The components cannot be restored — the type
        /// they required is gone — so this is where a person reads what to rebuild, and where.
        /// </summary>
        private VisualElement CreateLossesUI()
        {
            var scroll = CreatePane();
            if (lossesData.IsEmpty)
            {
                scroll.Add(new HelpBox(
                    "Nothing was left behind. A component is only taken off when it declares " +
                    "RequireComponent for a TMP type — nothing in UniText is a TMP_Text — or when " +
                    "it requires such a component in turn; a setting is only listed when UniText " +
                    "has no counterpart for it.",
                    HelpBoxMessageType.Info));
                return scroll;
            }

            var actions = InspectorVisuals.CreateRow();
            actions.Add(CreateAction("Export…", ExportLosses,
                "Writes everything below to a text file."));
            actions.Add(CreateAction("Clear the record", ClearLossRecord,
                "Forgets these notes once you have acted on them. Changes nothing in the project."));
            actions.Add(CreateAction("Show the JSON file",
                () => EditorUtility.RevealInFinder(MigrationLossesData.FilePath),
                MigrationLossesData.FilePath));
            scroll.Add(actions);

            if (lossesData.removed.Count > 0) AddRemovedSection(scroll);
            if (lossesData.settings.Count > 0) AddLostSettingsSection(scroll);
            return scroll;
        }

        private void AddRemovedSection(VisualElement scroll)
        {
            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < lossesData.removed.Count; i++)
                assets.Add(lossesData.removed[i].assetPath);

            scroll.Add(new HelpBox(
                $"{lossesData.removed.Count} component(s) of {lossesData.TypeCount()} type(s) " +
                $"were removed across {assets.Count} asset(s). They declared a requirement no " +
                "UniText component satisfies, so the text underneath could not be replaced while " +
                "they were there. Putting them back is not possible — the type they required no " +
                "longer exists on those objects. Everything below is what each one carried.",
                HelpBoxMessageType.Warning));

            var byType = new Dictionary<string, List<RemovedComponent>>(StringComparer.Ordinal);
            for (var i = 0; i < lossesData.removed.Count; i++)
            {
                var entry = lossesData.removed[i];
                var key = entry.componentType ?? "(unknown)";
                if (!byType.TryGetValue(key, out var list))
                    byType[key] = list = new List<RemovedComponent>();
                list.Add(entry);
            }

            foreach (var pair in byType)
                scroll.Add(CreateRemovedTypeCard(pair.Key, pair.Value));
        }

        private void AddLostSettingsSection(VisualElement scroll)
        {
            scroll.Add(new HelpBox(
                $"{lossesData.settings.Count} setting(s) had no UniText counterpart. Their " +
                "components migrated; these values did not come with them.",
                HelpBoxMessageType.Info));

            var byObject = new Dictionary<string, List<LostSetting>>(StringComparer.Ordinal);
            for (var i = 0; i < lossesData.settings.Count; i++)
            {
                var entry = lossesData.settings[i];
                var key = $"{entry.assetPath} :: {entry.objectPath}";
                if (!byObject.TryGetValue(key, out var list))
                    byObject[key] = list = new List<LostSetting>();
                list.Add(entry);
            }

            foreach (var pair in byObject)
            {
                var card = InspectorVisuals.CreateSection(pair.Key);
                card.Add(CreateAction("Open", () => PingAsset(pair.Value[0].assetPath),
                    "Selects the asset in the Project window."));
                for (var i = 0; i < pair.Value.Count; i++)
                {
                    var entry = pair.Value[i];
                    var row = InspectorVisuals.CreateRow();
                    var name = new Label($"{entry.setting} = {entry.value}");
                    name.AddToClassList("unitext-migration__wrap");
                    name.AddToClassList("unitext-migration__strong");
                    row.Add(name);
                    card.Add(row);
                    if (!string.IsNullOrEmpty(entry.advice)) card.Add(CreateNote(entry.advice));
                }
                scroll.Add(card);
            }
        }

        private VisualElement CreateRemovedTypeCard(string type, List<RemovedComponent> entries)
        {
            var card = InspectorVisuals.CreateSection($"{type} — {entries.Count} removed");
            if (!string.IsNullOrEmpty(entries[0].requiredType))
                card.Add(CreateNote($"Declared RequireComponent({entries[0].requiredType}). " +
                                    "Rebuild it on the migrated text with a UniText equivalent, " +
                                    "using the values below."));

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var header = InspectorVisuals.CreateRow();
                var where = new Label($"{entry.assetPath} :: {entry.objectPath}")
                {
                    tooltip = entry.reason,
                };
                where.AddToClassList("unitext-migration__wrap");
                header.Add(where);
                header.Add(CreateAction("Open", () => PingAsset(entry.assetPath),
                    "Selects the asset in the Project window."));
                card.Add(header);

                if (entry.referencedBy is { Count: > 0 })
                    card.Add(InspectorVisuals.CreateStatusLabel(
                        "Left an empty field on: " + string.Join(", ", entry.referencedBy),
                        EditorResources.StatusWarning));

                var state = new TextField
                {
                    value = entry.state,
                    multiline = true,
                    isReadOnly = true,
                };
                state.AddToClassList("unitext-migration__wrap");
                card.Add(state);
            }
            return card;
        }

        private VisualElement CreateSettingsUI()
        {
            var scroll = CreatePane();

            var exclusions = InspectorVisuals.CreateSection("Assets the migration leaves alone");
            exclusions.Add(CreateNote(
                "An excluded asset is yours end to end: nothing under these paths is scanned, " +
                "migrated or reported, and no pass reads or rewrites it. Two things follow. A " +
                "reference it holds to a component the migration replaced keeps naming the id that " +
                "component no longer has, and a TMP component inside it is still TMP after the " +
                "scripts are rewritten — migrate both by hand. Exclusion also lets a run finish " +
                "around an asset nobody can read: an oversized scene, or one you keep on TMP."));
            for (var i = 0; i < stateData.excludedPaths.Count; i++)
            {
                var index = i;
                var row = InspectorVisuals.CreateRow();
                var path = new Label(stateData.excludedPaths[i])
                {
                    tooltip = stateData.excludedPaths[i],
                };
                path.AddToClassList("unitext-migration__details");
                row.Add(path);
                row.Add(CreateAction("Remove", () =>
                {
                    stateData.excludedPaths.RemoveAt(index);
                    stateData.Save();
                    RenderDeferred();
                }, "Brings the path back — a scan has to read it again before anything about it " +
                   "is known."));
                exclusions.Add(row);
            }
            var addRow = InspectorVisuals.CreateEqualRow();
            addRow.Add(new Button(AddExcludedAsset)
            {
                text = "Add asset…",
                tooltip = "Leaves one scene, prefab or asset to you.",
            });
            addRow.Add(new Button(AddExcludedFolder)
            {
                text = "Add folder…",
                tooltip = "Leaves everything under a folder to you.",
            });
            exclusions.Add(addRow);
            scroll.Add(exclusions);

            var guard = InspectorVisuals.CreateSection("Guard against new TMP usage");
            var enabled = new InspectorToggle("Warn when an unscanned TMP component arrives")
            {
                value = MigrationGuard.Enabled,
            };
            enabled.RegisterValueChangedCallback(evt => MigrationGuard.Enabled = evt.newValue);
            guard.Add(enabled);
            guard.Add(CreateNote(
                "An imported scene or prefab raises a dialog when it carries a TMP component the " +
                "last scan has no open finding for — new usage, or a file that came back after " +
                "its migration. Files with pending findings stay quiet. This setting is stored " +
                "per machine, not in version control."));
            scroll.Add(guard);

            var practices = InspectorVisuals.CreateSection("Recommended order of work");
            foreach (var advice in new[]
                     {
                         "Commit to version control before every batch — the tool edits assets in place",
                         "Keep the migration on a short-lived branch",
                         "Map fonts first, then components, then scripts",
                         "Leaf prefabs before the prefabs that nest them; scenes last",
                         "Keep TextMesh Pro installed until the last finding is handled",
                     })
            {
                var line = new Label($"•  {advice}");
                line.AddToClassList("unitext-migration__text");
                practices.Add(line);
            }
            scroll.Add(practices);
            return scroll;
        }

        private void AddExcludedFolder()
            => ExcludeChosen(EditorUtility.OpenFolderPanel("Select folder to exclude", "Assets", ""));

        private void AddExcludedAsset()
            => ExcludeChosen(EditorUtility.OpenFilePanel("Select asset to exclude", "Assets", ""));

        /// <summary>
        /// Excludes what the file browser returned. A path outside the project addresses no asset
        /// the migration could ever read, and is refused rather than stored as an entry that
        /// matches nothing.
        /// </summary>
        private void ExcludeChosen(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return;

            var projectPath = Application.dataPath.Replace("/Assets", "");
            var path = absolutePath.Replace('\\', '/');
            if (path.StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                path = path.Substring(projectPath.Length + 1);

            if (path != "Assets" && !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                Log(LogSeverity.Warning,
                    $"'{absolutePath}' is outside this project, so the migration never reads it " +
                    "and there is nothing to exclude.");
                RenderDeferred();
                return;
            }
            ExcludePath(path);
        }

        private VisualElement CreateLogUI()
        {
            var root = CreatePaneStack();

            var header = InspectorVisuals.CreateRow();
            header.AddToClassList(ChromeClass);
            var title = InspectorVisuals.CreateSubheading("Log");
            title.AddToClassList(GrowClass);
            header.Add(title);
            var filterIndex = logFilter.HasValue ? (int)logFilter.Value + 1 : 0;
            var filter = new SelectorField<string>(logFilterOptions, filterIndex);
            filter.AddToClassList(ActionClass);
            header.Add(filter);
            header.Add(CreateAction("Export", ExportLog, null));
            header.Add(CreateAction("Clear", () =>
                {
                    logEntries.Clear();
                    session.Save();
                    RenderDeferred();
                },
                "Drops the entries, including their Restore buttons. The .bak files stay on disk."));
            root.Add(header);

            if (logEntries.Count == 0)
            {
                root.Add(new HelpBox("Nothing has been logged yet.", HelpBoxMessageType.Info));
                return root;
            }

            var entries = new ListView(filteredLog, RowHeight,
                () => new LogRow(this),
                (element, index) => ((LogRow)element).Bind(logEntries[filteredLog[index]]))
            {
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                reorderable = false,
                horizontalScrollingEnabled = false,
            };
            entries.AddToClassList("unitext-migration__list");
            root.Add(entries);

            void Rebuild()
            {
                filteredLog.Clear();
                for (var i = 0; i < logEntries.Count; i++)
                    if (!logFilter.HasValue || logEntries[i].severity == logFilter.Value)
                        filteredLog.Add(i);
                entries.Rebuild();
            }

            filter.RegisterValueChangedCallback(_ =>
            {
                var index = filter.Index;
                logFilter = index <= 0 ? null : (LogSeverity?)(index - 1);
                Rebuild();
            });
            Rebuild();
            return root;
        }

        /// <summary>One fixed-height log row, rebound as the virtualized list scrolls.</summary>
        private sealed class LogRow : InspectorRow
        {
            private readonly UniTextMigrationWindow window;
            private readonly Label message = new();
            private readonly Button restore;
            private LogEntry current;

            public LogRow(UniTextMigrationWindow window)
            {
                this.window = window;
                AddToClassList("unitext-migration__row");
                message.AddToClassList("unitext-migration__details");
                restore = CreateAction("Restore", Restore,
                    "Puts the .bak back over the rewritten file.");
                Add(message);
                Add(restore);
            }

            public void Bind(LogEntry entry)
            {
                current = entry;
                message.text = $"[{entry.timestamp}] {entry.message}";
                message.tooltip = entry.message;
                message.style.color = entry.severity switch
                {
                    LogSeverity.Error => new StyleColor(EditorResources.StatusError),
                    LogSeverity.Warning => new StyleColor(EditorResources.StatusWarning),
                    _ => new StyleColor(StyleKeyword.Null),
                };
                restore.style.display = string.IsNullOrEmpty(entry.backupPath)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            private void Restore()
            {
                if (current == null || string.IsNullOrEmpty(current.backupPath)) return;
                var path = current.backupPath;
                var restored = ScriptMigrator.RestoreFromBackup(path);
                if (restored)
                {
                    current.backupPath = null;
                    window.ReopenRestoredScript(path);
                }
                window.Log(restored ? LogSeverity.Info : LogSeverity.Error,
                    restored
                        ? $"Restored from backup: {path}"
                        : $"Backup is no longer on disk: {path}");
                AssetDatabase.Refresh();
                window.CommitOperation();
            }
        }

        /// <summary>A scrolling tab body that fills the window.</summary>
        private static InspectorScrollStack CreatePane()
        {
            var scroll = InspectorVisuals.CreateScrollSectionStack();
            scroll.AddToClassList("unitext-migration__pane");
            return scroll;
        }

        /// <summary>A non-scrolling tab body whose own list owns the scrolling.</summary>
        private static InspectorStack CreatePaneStack()
        {
            var stack = InspectorVisuals.CreateStack();
            stack.AddToClassList("unitext-migration__pane");
            return stack;
        }

        private static Button CreateAction(string text, System.Action clicked, string tooltip)
        {
            var button = new Button(clicked) { text = text, tooltip = tooltip };
            button.AddToClassList(ActionClass);
            return button;
        }

        private static Label CreateNote(string text)
        {
            var label = new Label(text);
            label.AddToClassList("unitext-migration__note");
            return label;
        }

        private static Label CreateValueLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("unitext-migration__value");
            return label;
        }

        /// <summary>
        /// Pending script rewrites together with the assembly definitions that compile them: a
        /// rewritten script names UniText types, which its assembly cannot see until the reference
        /// is there, so the two belong to one stage.
        /// </summary>
        private int ScriptStagePending()
            => CountByTypeAndStatus(FindingType.ScriptReference, MigrationStatus.NotStarted) +
               CountByTypeAndStatus(FindingType.AssemblyDef, MigrationStatus.NotStarted);

        /// <summary>Everything the three ordered stages do not claim, so no finding falls outside them.</summary>
        private int CleanupPending()
        {
            var count = 0;
            for (var i = 0; i < findings.Count; i++)
            {
                var finding = findings[i];
                if (finding.status != MigrationStatus.NotStarted) continue;
                if (finding.type is FindingType.Component or FindingType.ScriptReference or
                    FindingType.FontAsset or FindingType.AssemblyDef) continue;
                count++;
            }
            return count;
        }

        private static string StatusText(MigrationStatus status)
            => status == MigrationStatus.NotStarted ? "Pending" : status.ToString();

        private static Color StatusColor(MigrationStatus status) => status switch
        {
            MigrationStatus.Completed => EditorResources.StatusSuccess,
            MigrationStatus.Skipped => EditorResources.ForSkin(Color.gray),
            MigrationStatus.Failed => EditorResources.StatusError,
            _ => EditorResources.StatusInfo,
        };

        private static Color ComplexityColor(MigrationComplexity complexity) => complexity switch
        {
            MigrationComplexity.Simple => EditorResources.StatusSuccess,
            MigrationComplexity.Moderate => EditorResources.StatusInfo,
            MigrationComplexity.Complex => EditorResources.StatusWarning,
            _ => EditorResources.StatusError,
        };

        private static string ComplexityHint(MigrationComplexity complexity) => complexity switch
        {
            MigrationComplexity.Simple => "Nothing here needs a judgement call.",
            MigrationComplexity.Moderate =>
                "Something here has no exact UniText equivalent — check the result.",
            MigrationComplexity.Complex => "Only partly mechanical; the rest is rebuilt by hand.",
            _ => "No mechanical path at all — do it by hand, then skip the row.",
        };

        private static string TypeHandling(FindingType type) => type switch
        {
            FindingType.Component => "Rewritten in place, inside its prefab or scene.",
            FindingType.ScriptReference => "Rewritten from the Script Preview tab.",
            FindingType.FontAsset => "Paired with a UniText font stack in Font Mapping.",
            FindingType.Material => "By hand: UniText draws text with its own materials.",
            FindingType.Animation => "By hand: TMP curves target properties UniText does not have.",
            FindingType.AssemblyDef =>
                "By hand: add a reference to the UniText assembly so the scripts it compiles can " +
                "name UniText types. Drop the TMP reference only once nothing under it uses TMP.",
            FindingType.RichTextContent =>
                "By hand: a text asset carrying TMP markup that UniText reads differently.",
            FindingType.MissingScript => "By hand: clean up the missing script before migrating the file.",
            FindingType.UnreadableFile =>
                "By hand: the scan could not read this asset, so nothing is known about it. " +
                "Exclude it to let the run finish without it, or make it readable and re-scan.",
            FindingType.TmpAsset =>
                "By hand: nothing in UniText replaces it — skip the row once you have moved what " +
                "it held.",
            _ => "By hand: a compiled assembly exposing TMP types must be rebuilt by its owner.",
        };
    }
}
