using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal partial class UniTextToolsWindow
    {
        private readonly List<Button> toolkitTabButtons = new();
        private VisualElement toolkitContent;

        private void CreateToolkitGUI()
        {
            var root = rootVisualElement;
            UniTextInspectorTheme.Initialize(root);
            InspectorVisuals.ClearContent(root);
            root.AddToClassList("unitext-tools");
            var toolbar = new VisualElement();
            toolbar.AddToClassList("lightside-tabs");
            toolkitTabButtons.Clear();
            for (var i = 0; i < tabLabels.Length; i++)
            {
                var tab = (Tab)i;
                var button = new Button(() =>
                {
                    if (currentTab == tab) return;
                    currentTab = tab;
                    for (var index = 0; index < toolkitTabButtons.Count; index++)
                        toolkitTabButtons[index].EnableInClassList(
                            "lightside-tab--selected", index == (int)tab);
                    RenderToolkitTools();
                }) { text = tabLabels[i] };
                button.AddToClassList("lightside-tab");
                button.EnableInClassList("lightside-tab--selected", currentTab == tab);
                toolkitTabButtons.Add(button);
                toolbar.Add(button);
            }
            root.Add(toolbar);
            toolkitContent = new VisualElement();
            toolkitContent.AddToClassList("unitext-tools__content");
            root.Add(toolkitContent);
            RenderToolkitTools();
        }

        private void RenderToolkitTools()
        {
            if (toolkitContent == null) return;
            InspectorVisuals.ClearContent(toolkitContent);
            switch (currentTab)
            {
                case Tab.CreateAsset:
                    toolkitContent.Add(CreateAssetToolkitUI());
                    break;
                case Tab.Subsetter:
                    toolkitContent.Add(CreateSubsetterToolkitUI());
                    break;
                case Tab.DictionaryBuilder:
                    toolkitContent.Add(CreateDictionaryToolkitUI());
                    break;
            }
        }

        private VisualElement CreateAssetToolkitUI()
        {
            var scroll = CreateToolkitScroll();
            var sources = InspectorVisuals.CreateSection("Source Fonts");
            sources.Add(CreateToolkitDropArea(
                "Drag font files here (.ttf / .otf / .ttc / .otc)",
                (asset, path) => TryAddFont(path, asset as Font, false),
                path => TryAddFont(path, null, false)));
            var actions = InspectorVisuals.CreateRow();
            actions.Add(new Button(() => EditorApplication.delayCall += BrowseFiles)
                { text = "Browse Files…" });
            void ClearSources()
            {
                batchEntries.Clear();
                Selection.objects = Array.Empty<UnityEngine.Object>();
                RenderToolkitTools();
            }
            var clear = new Button(ClearSources) { text = "Clear" };
            clear.SetEnabled(batchEntries.Count > 0);
            actions.Add(clear);
            sources.Add(actions);
            if (batchEntries.Count > 0)
            {
                sources.Add(CreateToolkitFileList(batchEntries.Count, index =>
                    new FileRowInfo
                    {
                        name = batchEntries[index].name,
                        size = batchEntries[index].size,
                        pinned = batchEntries[index].fromSelection,
                    }, index =>
                    {
                        batchEntries.RemoveAt(index);
                        RenderToolkitTools();
                    }, () => EditorApplication.delayCall += BrowseFiles, ClearSources));
            }
            else
            {
                sources.Add(new HelpBox(
                    "Drag font files here, select fonts in the Project window, or browse from disk.",
                    HelpBoxMessageType.None));
            }
            scroll.Add(sources);

            var output = InspectorVisuals.CreateSection("Create Assets");
            var create = new Button(() =>
            {
                CreateBatchAssets();
                RenderToolkitTools();
            }) { text = $"Create {batchEntries.Count} UniText Font Asset(s)" };
            create.AddToClassList("lightside-primary-action");
            create.SetEnabled(batchEntries.Count > 0);
            output.Add(create);
            output.Add(new HelpBox(
                "Project fonts are saved next to their source. External fonts request an output " +
                "folder. Font bytes are embedded in the asset.",
                HelpBoxMessageType.None));
            scroll.Add(output);
            return scroll;
        }

        private VisualElement CreateSubsetterToolkitUI()
        {
            var scroll = CreateToolkitScroll();
            scroll.Add(CreateFontSourceToolkitUI(subsetSource, PrefSubsetBrowse));
            AddFaceSelector(scroll);

            var characters = InspectorVisuals.CreateSection(
                subsetMode == SubsetMode.Keep ? "Characters to Keep" : "Characters to Remove");
            var modes = InspectorVisuals.CreateRow();
            var remove = new Button(() =>
            {
                if (subsetMode == SubsetMode.Remove) return;
                subsetMode = SubsetMode.Remove;
                RenderToolkitTools();
            }) { text = "Remove" };
            var keep = new Button(() =>
            {
                if (subsetMode == SubsetMode.Keep) return;
                subsetMode = SubsetMode.Keep;
                RenderToolkitTools();
            }) { text = "Keep" };
            remove.AddToClassList("lightside-choice-chip");
            keep.AddToClassList("lightside-choice-chip");
            remove.EnableInClassList("lightside-choice-chip--selected",
                subsetMode == SubsetMode.Remove);
            keep.EnableInClassList("lightside-choice-chip--selected",
                subsetMode == SubsetMode.Keep);
            modes.Add(remove);
            modes.Add(keep);
            characters.Add(modes);
            characters.Add(new HelpBox(
                subsetMode == SubsetMode.Keep
                    ? "Only selected characters will remain in the subset font."
                    : "Selected scripts and characters will be removed. Composite characters " +
                      "are removed as glyphs while their components remain.",
                HelpBoxMessageType.None));
            var custom = new InspectorTextArea(
                subsetMode == SubsetMode.Keep ? "Custom Text" : "Custom Text (remove these too)")
            {
                value = subsetInputText,
            };
            custom.style.minHeight = 64f;
            characters.Add(custom);
            scroll.Add(characters);

            Action rangesChanged = null;
            var ranges = CreateCharacterRangesToolkitUI(() => rangesChanged?.Invoke());
            scroll.Add(ranges);
            var preview = InspectorVisuals.CreateSection("Preview");
            var previewContent = InspectorVisuals.CreateStack();
            preview.Add(previewContent);
            scroll.Add(preview);
            var output = InspectorVisuals.CreateSection("Output");
            var outputRow = InspectorVisuals.CreateRow();
            var file = new Button(CreateSubsetFile) { text = "Create .ttf File…" };
            file.style.flexGrow = 1f;
            file.AddToClassList("lightside-primary-action");
            var asset = new Button(CreateSubsetAsset) { text = "Create UniText Font" };
            asset.style.flexGrow = 1f;
            asset.AddToClassList("lightside-primary-action");
            outputRow.Add(file);
            outputRow.Add(asset);
            output.Add(outputRow);
            scroll.Add(output);

            void RefreshPreview()
            {
                var validInput = CollectCodepoints();
                InspectorVisuals.ClearContent(previewContent);
                if (!validInput)
                {
                    previewContent.Add(new HelpBox(subsetInputError, HelpBoxMessageType.Error));
                }
                else if (subsetMode == SubsetMode.Keep)
                {
                    previewContent.Add(new Label(
                        $"Characters to keep: {collectedCodepoints.Count:N0}"));
                    if (collectedCodepoints.Count is > 0 and <= 200)
                    {
                        var text = new StringBuilder();
                        foreach (var codepoint in collectedCodepoints.OrderBy(value => value))
                            if (!UnicodeData.IsC0ControlOrDelete(codepoint))
                                text.Append(char.ConvertFromUtf32(codepoint));
                        var sample = new Label(text.ToString());
                        sample.style.whiteSpace = WhiteSpace.Normal;
                        previewContent.Add(sample);
                    }
                    else if (collectedCodepoints.Count > 200)
                    {
                        previewContent.Add(CreateToolkitDimLabel(
                            "Too many characters to display."));
                    }
                }
                else
                {
                    previewContent.Add(new Label(
                        $"Codepoints to remove: {removeCodepointCount:N0}"));
                    previewContent.Add(new Label(
                        $"Compositions to remove: {removeCompositionCount:N0}"));
                }
                var enabled = validInput && subsetSource.HasData && HasSubsetInput;
                file.SetEnabled(enabled);
                asset.SetEnabled(enabled);
            }

            rangesChanged = RefreshPreview;
            custom.RegisterValueChangedCallback(evt =>
            {
                subsetInputText = evt.newValue;
                RefreshPreview();
            });
            RefreshPreview();
            return scroll;
        }

        private VisualElement CreateFontSourceToolkitUI(FontSource source, string preferenceKey)
        {
            var card = InspectorVisuals.CreateSection("Source Font");
            var asset = new InspectorObjectField("Font Asset", typeof(UnityEngine.Object))
            {
                value = source.asset,
            };
            asset.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != null && !IsValidFontObject(evt.newValue))
                {
                    asset.SetValueWithoutNotify(source.asset);
                    return;
                }
                source.asset = evt.newValue;
                source.path = string.Empty;
                source.LoadFromAsset();
                RenderToolkitTools();
            });
            card.Add(asset);
            card.Add(CreateToolkitDimLabel("— or —"));
            var pathRow = InspectorVisuals.CreateRow();
            var path = new TextField("File Path")
            {
                value = source.path,
                isDelayed = true,
            };
            path.style.flexGrow = 1f;
            path.RegisterValueChangedCallback(evt =>
            {
                source.path = evt.newValue;
                source.asset = null;
                source.LoadFromPath();
                RenderToolkitTools();
            });
            pathRow.Add(path);
            pathRow.Add(new Button(() =>
            {
                var filePath = EditorUtility.OpenFilePanel(
                    "Select Font File", GetPrefDir(preferenceKey), "ttf,otf,ttc,otc");
                if (string.IsNullOrEmpty(filePath)) return;
                SavePrefDir(preferenceKey, filePath);
                source.path = filePath;
                source.asset = null;
                source.LoadFromPath();
                RenderToolkitTools();
            }) { text = "Browse…" });
            card.Add(pathRow);
            if (source.HasData)
            {
                card.Add(new HelpBox(
                    $"{source.name}\n{FormatSize(source.size)}", HelpBoxMessageType.Info));
                card.Add(new Button(() => CopyAllCharacters(source.bytes, subsetFaceIndex))
                    { text = "Copy All Characters" });
            }
            else if (!string.IsNullOrEmpty(source.path) || source.asset != null)
            {
                card.Add(new HelpBox("Failed to load font file.", HelpBoxMessageType.Error));
            }
            else
            {
                card.Add(new HelpBox(
                    "Choose a Font, UniTextFont, or font file from disk.",
                    HelpBoxMessageType.None));
            }
            card.Add(CreateToolkitDropArea(
                "Drop a font here",
                (dropped, assetPath) =>
                {
                    if (!IsValidFontObject(dropped)) return;
                    source.asset = dropped;
                    source.path = string.Empty;
                    source.LoadFromAsset();
                },
                droppedPath =>
                {
                    source.path = droppedPath;
                    source.asset = null;
                    source.LoadFromPath();
                }));
            return card;
        }

        private void AddFaceSelector(VisualElement root)
        {
            if (!subsetSource.HasData)
            {
                subsetFaceIndex = 0;
                return;
            }
            if (subsetSource.bytes != subsetFaceLabelsFor)
            {
                subsetFaceLabelsFor = subsetSource.bytes;
                subsetFaceLabels = UniTextFontEditor.BuildFaceLabels(subsetSource.bytes);
                subsetFaceIndex = 0;
            }
            if (subsetFaceLabels == null || subsetFaceLabels.Length == 0)
            {
                subsetFaceIndex = 0;
                return;
            }
            subsetFaceIndex = Mathf.Clamp(subsetFaceIndex, 0, subsetFaceLabels.Length - 1);
            var face = new SelectorField<string>("Face", subsetFaceLabels, subsetFaceIndex);
            face.tooltip = "Which face of the .ttc or .otc collection to subset.";
            face.RegisterValueChangedCallback(_ => subsetFaceIndex = face.Index);
            var card = InspectorVisuals.CreateSection("Collection Face");
            card.Add(face);
            root.Add(card);
        }

        private VisualElement CreateCharacterRangesToolkitUI(Action changed)
        {
            var card = InspectorVisuals.CreateSection("Script Ranges");
            var choices = new List<(Button button, CharacterSet set)>();

            void RefreshChoice(Button button, CharacterSet set)
            {
                var selected = Has(set);
                button.text = (selected ? "✓  " : string.Empty) + FormatSetName(set);
                button.EnableInClassList("lightside-choice-chip--selected", selected);
            }

            void RefreshChoices()
            {
                for (var i = 0; i < choices.Count; i++)
                    RefreshChoice(choices[i].button, choices[i].set);
                changed?.Invoke();
            }

            var actions = InspectorVisuals.CreateRow();
            actions.Add(new Button(() =>
            {
                selectedSets = (CharacterSet)~0;
                RefreshChoices();
            }) { text = "Select All" });
            actions.Add(new Button(() =>
            {
                selectedSets = CharacterSet.None;
                RefreshChoices();
            }) { text = "Deselect All" });
            card.Add(actions);
            var table = new VisualElement();
            table.AddToClassList("lightside-choice-table");
            for (var rowIndex = 0; rowIndex < scriptTableRows.Length; rowIndex++)
            {
                var definition = scriptTableRows[rowIndex];
                var row = new VisualElement();
                row.AddToClassList("lightside-choice-table__row");
                row.EnableInClassList("lightside-choice-table__row--odd",
                    (rowIndex & 1) != 0);
                row.EnableInClassList("lightside-choice-table__row--last",
                    rowIndex == scriptTableRows.Length - 1);
                var group = new Label(definition.label);
                group.AddToClassList("lightside-choice-table__label");
                row.Add(group);
                var rowChoices = new VisualElement();
                rowChoices.AddToClassList("lightside-choice-table__choices");
                for (var i = 0; i < definition.sets.Length; i++)
                {
                    var set = definition.sets[i];
                    var button = new Button(() =>
                    {
                        selectedSets = Has(set) ? selectedSets & ~set : selectedSets | set;
                        RefreshChoices();
                    });
                    button.AddToClassList("lightside-choice-chip");
                    RefreshChoice(button, set);
                    choices.Add((button, set));
                    rowChoices.Add(button);
                }
                row.Add(rowChoices);
                table.Add(row);
            }
            card.Add(table);
            return card;
        }

        private VisualElement CreateDictionaryToolkitUI()
        {
            var scroll = CreateToolkitScroll();
            var filesCard = InspectorVisuals.CreateSection("Word Lists");
            filesCard.Add(CreateToolkitDropArea(
                "Drag word list files here (.txt)",
                (_, path) => TryAddDictFile(Path.GetFullPath(path)),
                TryAddDictFile));
            var actions = InspectorVisuals.CreateRow();
            actions.Add(new Button(() => EditorApplication.delayCall += BrowseDictFiles)
                { text = "Browse Files…" });
            var refresh = new Button(() =>
            {
                RefreshDictionaryFiles();
                RenderToolkitTools();
            }) { text = "Refresh" };
            refresh.SetEnabled(dictFiles.Count > 0);
            actions.Add(refresh);
            void ClearDictionaryFiles()
            {
                dictFiles.Clear();
                dictStatus = null;
                dictError = null;
                RenderToolkitTools();
            }
            var clear = new Button(ClearDictionaryFiles) { text = "Clear" };
            clear.SetEnabled(dictFiles.Count > 0);
            actions.Add(clear);
            filesCard.Add(actions);
            if (dictFiles.Count > 0)
            {
                filesCard.Add(CreateToolkitFileList(dictFiles.Count, index =>
                    new FileRowInfo
                    {
                        name = $"{dictFiles[index].name}  " +
                               $"[{DisplayDictionaryScript(dictFiles[index].script)}]",
                        size = dictFiles[index].size,
                    }, index =>
                    {
                        dictFiles.RemoveAt(index);
                        dictStatus = null;
                        UpdateDictionaryError();
                        RenderToolkitTools();
                    }, () => EditorApplication.delayCall += BrowseDictFiles,
                    ClearDictionaryFiles));
            }
            else
            {
                filesCard.Add(new HelpBox(
                    "Each UTF-8 line contains a word and optional tab-separated non-negative cost. " +
                    "Lines beginning with # are skipped.",
                    HelpBoxMessageType.None));
            }
            scroll.Add(filesCard);

            var build = InspectorVisuals.CreateSection("Build Dictionary");
            build.Add(new Label($"Detected Outputs: {GetDictionaryOutputSummary()}"));
            var buildButton = new Button(() =>
            {
                BuildDictionaryAssets();
                RenderToolkitTools();
            }) { text = "Build Dictionary Assets" };
            buildButton.AddToClassList("lightside-primary-action");
            buildButton.SetEnabled(dictFiles.Count > 0);
            build.Add(buildButton);
            build.Add(new HelpBox(
                "Target scripts are inferred from Unicode properties. Inputs for the same script " +
                "are merged into one WordSegmentationDictionary asset.",
                HelpBoxMessageType.None));
            scroll.Add(build);
            if (!string.IsNullOrEmpty(dictError))
                scroll.Add(new HelpBox(dictError, HelpBoxMessageType.Error));
            else if (!string.IsNullOrEmpty(dictStatus))
                scroll.Add(new HelpBox(dictStatus, HelpBoxMessageType.Info));
            return scroll;
        }

        private VisualElement CreateToolkitDropArea(string caption,
            Action<UnityEngine.Object, string> onAssetDrop, Action<string> onPathDrop)
        {
            var drop = new Label(caption);
            drop.AddToClassList("lightside-drop-zone");
            drop.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            drop.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                var assets = DragAndDrop.objectReferences;
                if (assets != null)
                {
                    for (var i = 0; i < assets.Length; i++)
                    {
                        var asset = assets[i];
                        if (asset == null) continue;
                        var path = AssetDatabase.GetAssetPath(asset);
                        if (!string.IsNullOrEmpty(path)) onAssetDrop(asset, path);
                    }
                }
                var paths = DragAndDrop.paths;
                if (paths != null)
                {
                    for (var i = 0; i < paths.Length; i++)
                        if (Path.IsPathRooted(paths[i])) onPathDrop(paths[i]);
                }
                evt.StopPropagation();
                RenderToolkitTools();
            });
            return drop;
        }

        private static InspectorListView CreateToolkitFileList(int count,
            Func<int, FileRowInfo> getRow, Action<int> remove, Action add, Action clear)
        {
            var list = new InspectorListView("Files",
                () => new ToolkitFileRow(remove),
                (element, index) => ((ToolkitFileRow)element).Bind(getRow(index)),
                reorderable: false);
            list.Header.AddButton.clicked += add;
            list.ClearRequested += clear;
            list.Rebuild(count, true);
            return list;
        }

        private sealed class ToolkitFileRow : VisualElement
        {
            private readonly Label nameLabel;
            private readonly Label size;
            private readonly Label selection;
            private readonly Button remove;

            public ToolkitFileRow(Action<int> removeAt)
            {
                var content = InspectorVisuals.CreateCompactRow();
                content.AddToClassList("lightside-list__content");
                content.AddToClassList("unitext-tools-file-row__content");
                nameLabel = new Label();
                nameLabel.AddToClassList("unitext-tools-file-row__name");
                size = new Label();
                size.AddToClassList("unitext-tools-file-row__meta");
                selection = new Label("Selection");
                selection.AddToClassList("unitext-tools-file-row__meta");
                content.Add(nameLabel);
                content.Add(size);
                content.Add(selection);
                Add(content);
                remove = InspectorListView.CreateRemoveButton(() =>
                {
                    if (userData is int index && index >= 0) removeAt(index);
                }, "Remove file");
                Add(remove);
            }

            public void Bind(FileRowInfo value)
            {
                nameLabel.text = value.name;
                nameLabel.tooltip = value.name;
                size.text = FormatSize(value.size);
                selection.style.display = value.pinned
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                remove.style.display = value.pinned
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        private static ScrollView CreateToolkitScroll()
        {
            var scroll = InspectorVisuals.CreateScrollSectionStack();
            scroll.style.flexGrow = 1f;
            return scroll;
        }

        private static Label CreateToolkitDimLabel(string value)
        {
            var label = new Label(value);
            label.style.opacity = 0.6f;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
