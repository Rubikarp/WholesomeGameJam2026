using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(WordSegmentationDictionary))]
    [CanEditMultipleObjects]
    internal sealed class WordSegmentationDictionaryEditor : FullWidthEditor
    {
        private const float TableHeight = 260f;

        private readonly List<int> visibleRows = new();
        private List<WordSegmentationDictionaryCompiler.WordEntry> words = new();
        private VisualElement root;
        private ListView table;
        private Label emptyState;
        private Label scriptValue;
        private Label sizeValue;
        private HelpBox errorBox;
        private HelpBox mixedBox;
        private VisualElement wordsEditor;
        private InspectorSearchField searchField;
        private TextField newWordField;
        private IntegerField newCostField;
        private Button addButton;
        private Button revertButton;
        private Button applyButton;
        private string search = string.Empty;
        private string newWord = string.Empty;
        private int newCost = 1;
        private string error;
        private bool wordsChanged;
        private bool mixedDictionaries;

        private void OnEnable()
        {
            ReloadWords();
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            root = UniTextInspectorTheme.CreateRoot();
            var status = InspectorVisuals.CreateSection("Dictionary Status");
            scriptValue = new Label();
            sizeValue = new Label();
            status.Add(scriptValue);
            status.Add(sizeValue);

            errorBox = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            status.Add(errorBox);
            root.Add(status);

            mixedBox = new HelpBox(
                "The selected dictionaries contain different compiled word sets. Select one dictionary, or make their contents equal, before using the staged word editor.",
                HelpBoxMessageType.Info);
            root.Add(mixedBox);

            wordsEditor = InspectorVisuals.CreateSection("Words");
            searchField = new InspectorSearchField("Search words…");
            searchField.RegisterValueChangedCallback(evt =>
            {
                search = evt.newValue ?? string.Empty;
                RefreshTable();
            });
            wordsEditor.Add(searchField);

            var commands = InspectorVisuals.CreateRow();
            commands.AddToClassList("unitext-dictionary__commands");
            newWordField = new TextField("Word");
            newWordField.AddToClassList("unitext-dictionary__word");
            newWordField.RegisterValueChangedCallback(evt =>
            {
                newWord = evt.newValue ?? string.Empty;
                RefreshActions();
            });
            newWordField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter)) return;
                AddWord();
                evt.StopPropagation();
            });
            commands.Add(newWordField);
            newCostField = new IntegerField("Cost") { value = newCost };
            newCostField.AddToClassList("unitext-dictionary__new-cost");
            newCostField.RegisterValueChangedCallback(evt =>
            {
                newCost = Math.Max(0, evt.newValue);
                newCostField.SetValueWithoutNotify(newCost);
            });
            commands.Add(newCostField);
            addButton = new Button(AddWord) { text = "Add" };
            commands.Add(addButton);
            wordsEditor.Add(commands);

            var header = InspectorVisuals.CreateCompactRow();
            header.AddToClassList("unitext-dictionary__header");
            header.Add(CreateColumnLabel("#", "index"));
            header.Add(CreateColumnLabel("Word", "word"));
            header.Add(CreateColumnLabel("Cost", "cost"));
            header.Add(CreateColumnLabel(string.Empty, "action"));
            wordsEditor.Add(header);

            var viewport = new VisualElement();
            viewport.AddToClassList("unitext-dictionary__viewport");
            viewport.style.height = TableHeight;
            table = new ListView(visibleRows, InspectorVisuals.CollectionRowHeight,
                () => new WordRow(this),
                (element, index) => ((WordRow)element).Bind(visibleRows[index]))
            {
                selectionType = SelectionType.None,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                horizontalScrollingEnabled = false,
            };
            table.AddToClassList("unitext-dictionary__table");
            viewport.Add(table);
            emptyState = new Label();
            emptyState.style.unityTextAlign = TextAnchor.MiddleCenter;
            emptyState.style.position = Position.Absolute;
            emptyState.style.left = 0f;
            emptyState.style.right = 0f;
            emptyState.style.top = 0f;
            emptyState.style.bottom = 0f;
            viewport.Add(emptyState);
            wordsEditor.Add(viewport);

            wordsEditor.Add(new HelpBox(
                "Word edits are staged until Apply. The compiled asset does not store a second source-word list.",
                HelpBoxMessageType.Info));
            var actions = InspectorVisuals.CreateRow();
            revertButton = new Button(ReloadWords) { text = "Revert" };
            revertButton.style.flexGrow = 1f;
            applyButton = new Button(ApplyWords) { text = "Apply" };
            applyButton.style.flexGrow = 1f;
            actions.Add(revertButton);
            actions.Add(applyButton);
            wordsEditor.Add(actions);
            root.Add(wordsEditor);

            root.TrackSerializedObjectValue(serializedObject, _ => ReloadWords());
            RefreshView();
            return root;
        }

        private static Label CreateColumnLabel(string text, string column)
        {
            var label = new Label(text);
            label.AddToClassList("unitext-dictionary__column");
            label.AddToClassList("unitext-dictionary__column--" + column);
            return label;
        }

        private void RefreshView()
        {
            if (root == null) return;
            var dictionary = (WordSegmentationDictionary)target;
            var script = dictionary.Script;
            var size = dictionary.trieData?.Length ?? 0;
            var mixedScript = false;
            var mixedSize = false;
            for (var i = 1; i < targets.Length; i++)
            {
                var selected = (WordSegmentationDictionary)targets[i];
                mixedScript |= selected.Script != script;
                mixedSize |= (selected.trieData?.Length ?? 0) != size;
            }
            scriptValue.text = $"Detected Script: {(mixedScript ? "—" : DisplayScript(script))}";
            sizeValue.text = $"Compiled Size: {(mixedSize ? "—" : $"{size:N0} bytes")}";
            mixedBox.style.display = mixedDictionaries
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            wordsEditor.SetEnabled(!mixedDictionaries);
            errorBox.text = error ?? string.Empty;
            errorBox.style.display = string.IsNullOrEmpty(error)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            newWordField.SetValueWithoutNotify(newWord);
            newCostField.SetValueWithoutNotify(newCost);
            searchField.SetValueWithoutNotify(search);
            RefreshTable();
            RefreshActions();
        }

        private void RefreshTable()
        {
            if (table == null) return;
            visibleRows.Clear();
            for (var i = 0; i < words.Count; i++)
                if (MatchesSearch(words[i].Word))
                    visibleRows.Add(i);
            table.Rebuild();
            emptyState.text = words.Count == 0
                ? "Dictionary is empty"
                : "No matching words";
            emptyState.style.display = visibleRows.Count == 0
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        private void RefreshActions()
        {
            addButton?.SetEnabled(!string.IsNullOrWhiteSpace(newWord));
            revertButton?.SetEnabled(wordsChanged);
            applyButton?.SetEnabled(wordsChanged);
        }

        private bool MatchesSearch(string value)
            => search.Length == 0 ||
               (value ?? string.Empty).IndexOf(search,
                   StringComparison.OrdinalIgnoreCase) >= 0;

        private void EditWord(int index, string value)
        {
            var entry = words[index];
            if (entry.Word == value) return;
            var wasVisible = MatchesSearch(entry.Word);
            words[index] = new WordSegmentationDictionaryCompiler.WordEntry(
                value, entry.Cost);
            wordsChanged = true;
            RefreshActions();
            if (wasVisible != MatchesSearch(value))
                root.schedule.Execute(RefreshTable);
        }

        private void EditCost(int index, int value)
        {
            value = Math.Max(0, value);
            var entry = words[index];
            if (entry.Cost == value) return;
            words[index] = new WordSegmentationDictionaryCompiler.WordEntry(
                entry.Word, value);
            wordsChanged = true;
            RefreshActions();
        }

        private void RemoveWord(int index)
        {
            words.RemoveAt(index);
            wordsChanged = true;
            RefreshView();
        }

        private void AddWord()
        {
            var value = newWord.Trim();
            if (value.Length == 0) return;
            if (FindWord(value) >= 0)
            {
                search = value;
                RefreshView();
                return;
            }

            words.Add(new WordSegmentationDictionaryCompiler.WordEntry(value, newCost));
            words.Sort(CompareEntries);
            newWord = string.Empty;
            newCost = 1;
            search = value;
            wordsChanged = true;
            RefreshView();
        }

        private void ApplyWords()
        {
            try
            {
                var trieData = WordSegmentationDictionaryCompiler.Compile(words, out var script);
                var normalizedWords = WordSegmentationDictionaryCompiler.Decompile(trieData);
                SetCompiledData(serializedObject, script, trieData);

                words = normalizedWords;
                wordsChanged = false;
                error = null;
                RefreshView();
            }
            catch (Exception exception)
            {
                error = exception.Message;
                Debug.LogException(exception);
                RefreshView();
            }
        }

        private void ReloadWords()
        {
            var dictionary = (WordSegmentationDictionary)target;
            mixedDictionaries = false;
            for (var i = 1; i < targets.Length; i++)
            {
                var selected = (WordSegmentationDictionary)targets[i];
                if (selected.Script == dictionary.Script &&
                    EqualBytes(selected.trieData, dictionary.trieData)) continue;
                mixedDictionaries = true;
                break;
            }
            words = mixedDictionaries || dictionary.trieData == null || dictionary.trieData.Length == 0
                ? new List<WordSegmentationDictionaryCompiler.WordEntry>()
                : WordSegmentationDictionaryCompiler.Decompile(dictionary.trieData);
            error = null;
            wordsChanged = false;
            RefreshView();
        }

        internal static void SetCompiledData(WordSegmentationDictionary dictionary,
            UnicodeScript script, byte[] trieData)
        {
            var state = new SerializedObject(dictionary);
            SetCompiledData(state, script, trieData);
        }

        private static void SetCompiledData(SerializedObject state,
            UnicodeScript script, byte[] trieData)
        {
            state.UpdateIfRequiredOrScript();
            var scriptProperty = InspectorHelpers.RequireProperty(state, "script");
            new SerializedPropertyBinding(scriptProperty).EditSerializedProperties(property =>
            {
                property.intValue = (int)script;
                var data = InspectorHelpers.RequireProperty(
                    property.serializedObject, "trieData");
                data.arraySize = trieData.Length;
                for (var i = 0; i < trieData.Length; i++)
                    data.GetArrayElementAtIndex(i).intValue = trieData[i];
            }, "Edit Word Segmentation Dictionary");
        }

        private static bool EqualBytes(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private int FindWord(string value)
        {
            for (var i = 0; i < words.Count; i++)
                if (string.Equals(words[i].Word, value, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static int CompareEntries(
            WordSegmentationDictionaryCompiler.WordEntry left,
            WordSegmentationDictionaryCompiler.WordEntry right)
            => StringComparer.Ordinal.Compare(left.Word, right.Word);

        private static string DisplayScript(UnicodeScript script)
            => script == UnicodeScript.Unknown
                ? "Not detected"
                : script == UnicodeScript.Han
                    ? "CJK (Han)"
                    : ObjectNames.NicifyVariableName(script.ToString());

        private sealed class WordRow : InspectorRow
        {
            private readonly WordSegmentationDictionaryEditor owner;
            private readonly Label indexLabel;
            private readonly TextField wordField;
            private readonly IntegerField costField;

            public WordRow(WordSegmentationDictionaryEditor owner) : base(true, false)
            {
                this.owner = owner;
                indexLabel = new Label();
                indexLabel.AddToClassList("unitext-dictionary__column--index");
                Add(indexLabel);
                wordField = new TextField();
                wordField.AddToClassList("unitext-dictionary__column--word");
                wordField.RegisterValueChangedCallback(evt =>
                {
                    if (userData is int index) owner.EditWord(index, evt.newValue);
                });
                Add(wordField);
                costField = new IntegerField();
                costField.AddToClassList("unitext-dictionary__column--cost");
                costField.RegisterValueChangedCallback(evt =>
                {
                    if (userData is int index)
                    {
                        owner.EditCost(index, evt.newValue);
                        costField.SetValueWithoutNotify(Math.Max(0, evt.newValue));
                    }
                });
                Add(costField);
                var remove = InspectorListView.CreateRemoveButton(() =>
                {
                    if (userData is int index) owner.RemoveWord(index);
                }, "Remove word");
                Add(remove);
            }

            public void Bind(int index)
            {
                userData = index;
                var entry = owner.words[index];
                indexLabel.text = (index + 1).ToString();
                wordField.SetValueWithoutNotify(entry.Word);
                costField.SetValueWithoutNotify(entry.Cost);
            }
        }
    }
}
