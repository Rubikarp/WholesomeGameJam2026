using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal static class KeyboardLayoutSearch
    {
        public static string Alternate(string value)
        {
            const string russian = "ёйцукенгшщзхъфывапролджэячсмитьбю";
            const string english = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
            char[] chars = null;
            for (var i = 0; i < value.Length; i++)
            {
                var index = russian.IndexOf(char.ToLowerInvariant(value[i]));
                if (index < 0) continue;
                chars ??= value.ToCharArray();
                chars[i] = english[index];
            }
            return chars == null ? value : new string(chars);
        }
    }

    /// <summary>Supplies the semantic identity used to colour a compound selector value.</summary>
    public interface ISelectorAccentSource
    {
        /// <summary>Value or type whose stable accent represents this selector value.</summary>
        object SelectorAccentKey { get; }
    }

    /// <summary>
    /// Searchable UI Toolkit popup for typed choices. Items may be arranged into nested groups,
    /// carry icons and descriptions, and contribute a feature-owned trailing visual.
    /// </summary>
    public sealed class Selector : InspectorPopupWindow
    {
        /// <summary>One selectable value and its presentation metadata.</summary>
        public struct SelectorItem
        {
            /// <summary>Primary label shown in the selector.</summary>
            public string displayName;

            /// <summary>Additional text included in search matching.</summary>
            public string searchText;

            /// <summary>Slash-separated group path; empty places the item at the root.</summary>
            public string groupName;

            /// <summary>Sort order of the root group.</summary>
            public int groupOrder;

            /// <summary>Optional item icon.</summary>
            public Texture icon;

            /// <summary>
            /// Colours <see cref="icon"/> with the row's accent, so a command reads as one colour.
            /// Requires a white mask; leave off for an icon that already carries its own colour,
            /// such as a type icon or an asset thumbnail.
            /// </summary>
            public bool tintIcon;

            /// <summary>Optional root-group icon.</summary>
            public Texture groupIcon;

            /// <summary>Optional nested-group icon.</summary>
            public Texture subGroupIcon;

            /// <summary>Optional semantic identity colouring the root-group header.</summary>
            public object groupAccentKey;

            /// <summary>Optional semantic identity colouring nested-group headers.</summary>
            public object subGroupAccentKey;

            /// <summary>Optional explanatory text shown below the item.</summary>
            public string description;

            /// <summary>
            /// Optional value the row acts on, drawn beside the title as a chip in the value's own
            /// icon and accent — a command stays one colour while naming something of another.
            /// </summary>
            public InspectorLabel chip;

            /// <summary>Optional compact qualifier shown as a pill on the detail line under the title, after <see cref="description"/>.</summary>
            public string secondaryText;

            /// <summary>Optional semantic identity used to colour <see cref="secondaryText"/>.</summary>
            public object secondaryAccentKey;

            /// <summary>Optional extended explanation used by search and the item tooltip.</summary>
            public string details;

            /// <summary>Shows the item without allowing it to be chosen.</summary>
            public bool disabled;

            /// <summary>
            /// Marks the item as selected regardless of the popup's current value. Use for a
            /// command or toggle whose checked state is its own, not equality with a value.
            /// </summary>
            public bool selected;

            /// <summary>Draws a divider in place of a choice; only <see cref="groupName"/> applies.</summary>
            public bool separator;

            /// <summary>Value returned when the item is chosen.</summary>
            public object value;

            /// <summary>
            /// Optional semantic identity used for colour when <see cref="value"/> is only a
            /// transport value such as an index or callback.
            /// </summary>
            public object accentKey;

            /// <summary>Optional factory for the trailing row visual.</summary>
            public Func<VisualElement> createDecorator;

            /// <summary>
            /// Optional factory for an interactive control at the trailing edge. Its clicks stay with
            /// the control and never choose the row, so a row may carry its own command.
            /// </summary>
            public Func<VisualElement> createAction;

            /// <summary>
            /// Optional factory for content revealed under the row by a disclosure control. Built on
            /// first expansion; expansion survives search and rebuilds.
            /// </summary>
            public Func<VisualElement> createBody;
        }

        private sealed class GroupNode
        {
            public string displayName;
            public string path;
            public int order;
            public int itemCount;
            public Texture icon;
            public object accentKey;
            public readonly List<GroupNode> children = new();
            public readonly List<int> items = new();
        }

        private readonly struct SearchDocument
        {
            public readonly int index;
            public readonly string title;
            public readonly string group;
            public readonly string secondary;
            public readonly string metadata;
            public readonly string description;
            public readonly string details;

            public SearchDocument(int index, SelectorItem item)
            {
                this.index = index;
                title = Normalize(item.displayName);
                group = Normalize(item.groupName);
                secondary = Normalize(item.secondaryText);
                metadata = Normalize(item.searchText);
                description = Normalize(item.description);
                details = Normalize(item.details);
            }
        }

        private readonly struct SearchMatch
        {
            public readonly int index;
            public readonly int score;

            public SearchMatch(int index, int score)
            {
                this.index = index;
                this.score = score;
            }
        }

        private sealed class ItemNavigation
        {
            public readonly SelectorItem item;
            public readonly Action<bool> setActive;

            public ItemNavigation(SelectorItem item, Action<bool> setActive)
            {
                this.item = item;
                this.setActive = setActive;
            }
        }

        private const float CompactMinWidth = 96f;
        private const float SearchMinWidth = 200f;
        private const float RichMinWidth = 420f;
        private const float CompactMaxWidth = 420f;
        private const float CompactItemChromeWidth = 48f;
        private const float CompactIconWidth = 25f;
        private const float CompactDecoratorWidth = 78f;
        private const float CompactActionWidth = 26f;
        private const float CompactChipChromeWidth = 34f;

        /// <summary>Matches the chip's USS font size; the default label style measures a third wider.</summary>
        private const int ChipFontSize = 10;
        private const float CompactDisclosureWidth = 26f;
        private const float CompactGroupChromeWidth = 34f;
        private const float CompactEmptyChromeWidth = 34f;
        private const float EmptyHeight = 54f;
        private const float CompactItemHeight = 33f;
        private const float DetailedItemHeight = 48f;
        private const float SeparatorHeight = 9f;
        private const float GroupHeight = 27f;
        private const float PopupChromeHeight = 6f;
        private const float SearchHeight = 41f;
        private const int SearchThreshold = 10;

        private static readonly char[] searchSeparators = { ' ' };

        private SelectorItem[] items;
        private object currentValue;
        private Action<object> selected;
        private Func<object, bool> isSelected;
        private bool keepOpenAfterSelection;
        private bool closeOwner;
        private bool showNone;
        private bool showSearch;
        private string noneLabel;
        private string emptyMessage;
        private readonly GroupNode root = new();
        private readonly HashSet<string> expandedGroups = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<object> expandedItems = new();
        private readonly List<Button> visibleButtons = new();
        private readonly List<SelectorItem> visibleItems = new();
        private readonly List<Action<bool>> activeSetters = new();
        private readonly List<SearchMatch> searchMatches = new();
        private SearchDocument[] searchDocuments;
        private bool hasRichContent;
        private int activeIndex = -1;
        private InspectorSearchField search;
        private ScrollView content;

        /// <summary>
        /// Opens the popup at the supplied control rectangle and reports the selected value. Holding
        /// Alt while choosing keeps the popup open for repeated selections. Search is omitted for
        /// ten or fewer choices. Set <paramref name="closeOwner"/> when the selection completes a
        /// command started in the popup that opened this one, so choosing dismisses both.
        /// </summary>
        public static void Show(Rect buttonRect, SelectorItem[] items, object currentValue,
            Action<object> onSelected, bool showNone = false, bool showSearch = true,
            string noneLabel = null, string emptyMessage = null, bool closeOwner = false)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (onSelected == null) throw new ArgumentNullException(nameof(onSelected));
            ShowInternal(ScreenRect.FromPanel(buttonRect), items, currentValue, onSelected, null, false,
                showNone, showSearch, noneLabel, emptyMessage, closeOwner);
        }

        /// <summary>
        /// Opens the popup at a rectangle already in screen coordinates. This is the form a deferred opener
        /// needs: panel coordinates only convert to the screen from inside the layout pass that produced them,
        /// so a popup opened after that pass has ended must carry the screen rectangle it captured earlier.
        /// </summary>
        public static void ShowAtScreenRect(ScreenRect screenRect, SelectorItem[] items, object currentValue,
            Action<object> onSelected, bool showNone = false, bool showSearch = true,
            string noneLabel = null, string emptyMessage = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (onSelected == null) throw new ArgumentNullException(nameof(onSelected));
            ShowInternal(screenRect, items, currentValue, onSelected, null, false,
                showNone, showSearch, noneLabel, emptyMessage);
        }

        /// <summary>
        /// Opens a selector that may display several selected items and remains open while values
        /// are toggled. Search is omitted for ten or fewer choices.
        /// </summary>
        public static void ShowMultiple(Rect buttonRect, SelectorItem[] items,
            Func<object, bool> isSelected, Action<object> onSelected,
            bool showSearch = true, string emptyMessage = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (isSelected == null) throw new ArgumentNullException(nameof(isSelected));
            if (onSelected == null) throw new ArgumentNullException(nameof(onSelected));
            ShowInternal(ScreenRect.FromPanel(buttonRect), items, null, onSelected, isSelected, true,
                false, showSearch, null, emptyMessage);
        }

        private static void ShowInternal(ScreenRect anchor, SelectorItem[] items, object currentValue,
            Action<object> onSelected, Func<object, bool> isSelected,
            bool keepOpenAfterSelection, bool showNone, bool showSearch,
            string noneLabel, string emptyMessage, bool closeOwner = false)
        {
            PreProcessItems(items);
            var window = CreateInstance<Selector>();
            window.items = items;
            window.currentValue = currentValue;
            window.selected = onSelected;
            window.isSelected = isSelected;
            window.keepOpenAfterSelection = keepOpenAfterSelection;
            window.closeOwner = closeOwner;
            window.showNone = showNone;
            window.showSearch = showSearch &&
                                items.Length + (showNone ? 1 : 0) > SearchThreshold;
            window.noneLabel = string.IsNullOrEmpty(noneLabel) ? "(None)" : noneLabel;
            window.emptyMessage = string.IsNullOrEmpty(emptyMessage)
                ? "No items available."
                : emptyMessage;
            window.hasRichContent = HasRichContent(items);
            window.BuildSearchDocuments();
            window.BuildGroups();

            var width = Mathf.Max(anchor.Width,
                window.hasRichContent
                    ? RichMinWidth
                    : PreferredCompactWidth(items, window.showNone, window.noneLabel,
                        window.emptyMessage, window.showSearch));
            var desired = items.Length == 0 && !showNone
                ? EmptyHeight
                : window.EstimatedBrowseHeight() + PopupChromeHeight +
                  (window.showSearch ? SearchHeight : 0f);
            window.ShowAtScreenRect(anchor, width, desired);
        }

        /// <summary>Opens a selector containing every named value of an enum.</summary>
        public static void ShowForEnum<T>(Rect rect, T currentValue,
            Action<T> onSelected) where T : Enum
        {
            if (onSelected == null) throw new ArgumentNullException(nameof(onSelected));
            Show(rect, EnumItems<T>(), currentValue, value => onSelected((T)value));
        }

        /// <summary>
        /// Opens the enum selector at a rectangle already in screen coordinates, with nothing marked current when
        /// <paramref name="currentValue"/> is <see langword="null"/>. This is the form a host outside a layout pass
        /// needs — a Scene view tool, or anything else holding IMGUI coordinates rather than panel ones.
        /// </summary>
        public static void ShowForEnum<T>(ScreenRect screenRect, T? currentValue,
            Action<T> onSelected) where T : struct, Enum
        {
            if (onSelected == null) throw new ArgumentNullException(nameof(onSelected));
            ShowAtScreenRect(screenRect, EnumItems<T>(), currentValue,
                value => onSelected((T)value));
        }

        private static SelectorItem[] EnumItems<T>() where T : Enum
        {
            var names = Enum.GetNames(typeof(T));
            var values = Enum.GetValues(typeof(T));
            var entries = new SelectorItem[names.Length];
            for (var i = 0; i < names.Length; i++)
            {
                var label = ObjectNames.NicifyVariableName(names[i]);
                entries[i] = new SelectorItem
                {
                    displayName = label,
                    searchText = label,
                    value = values.GetValue(i),
                };
            }
            return entries;
        }

        /// <summary>Builds the retained-mode hierarchical selector.</summary>
        public void CreateGUI()
        {
            if (items == null)
            {
                Close();
                return;
            }

            var rootElement = PreparePopup("lightside-selector");
            rootElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    if (search != null && !string.IsNullOrEmpty(search.value))
                    {
                        search.value = string.Empty;
                        evt.StopPropagation();
                        return;
                    }
                    CloseToOwner();
                    evt.StopPropagation();
                    return;
                }
                if (evt.keyCode == KeyCode.DownArrow)
                    MoveActive(1);
                else if (evt.keyCode == KeyCode.UpArrow)
                    MoveActive(-1);
                else if (evt.keyCode == KeyCode.Home)
                    SetActive(0, true);
                else if (evt.keyCode == KeyCode.End)
                    SetActive(visibleButtons.Count - 1, true);
                else if (evt.keyCode == KeyCode.PageDown)
                    MoveActive(5);
                else if (evt.keyCode == KeyCode.PageUp)
                    MoveActive(-5);
                else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    ChooseActive(evt.altKey);
                else
                    return;
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            if (showSearch)
            {
                search = new InspectorSearchField();
                search.AddToClassList("lightside-selector__search");
                search.RegisterValueChangedCallback(_ => Rebuild());
                rootElement.Add(search);
            }
            content = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
            };
            content.AddToClassList("lightside-selector__content");
            rootElement.Add(content);
            Rebuild();
            FitHeightToContent(content);
            search?.Focus();
        }

        private void FocusCurrentItem(bool moveFocus = true)
        {
            if (visibleButtons.Count == 0) return;
            for (var i = 0; i < visibleButtons.Count; i++)
            {
                if (!visibleButtons[i].ClassListContains(
                        "lightside-selector__item--selected")) continue;
                SetActive(i, false, moveFocus);
                return;
            }
            SetActive(0, false, moveFocus);
        }

        private void BuildGroups()
        {
            root.children.Clear();
            root.items.Clear();
            for (var i = 0; i < items.Length; i++)
            {
                var path = items[i].groupName;
                if (string.IsNullOrWhiteSpace(path))
                {
                    root.items.Add(i);
                    continue;
                }

                var node = root;
                var parts = path.Split('/');
                var normalizedPath = string.Empty;
                for (var depth = 0; depth < parts.Length; depth++)
                {
                    var part = parts[depth].Trim();
                    if (part.Length == 0) continue;
                    normalizedPath = normalizedPath.Length == 0
                        ? part
                        : normalizedPath + "/" + part;
                    var child = FindChild(node, part);
                    if (child == null)
                    {
                        child = new GroupNode
                        {
                            displayName = part,
                            path = normalizedPath,
                            order = depth == 0 ? items[i].groupOrder : 0,
                            icon = depth == 0 ? items[i].groupIcon : items[i].subGroupIcon,
                            accentKey = depth == 0
                                ? items[i].groupAccentKey
                                : items[i].subGroupAccentKey,
                        };
                        node.children.Add(child);
                    }
                    node = child;
                }
                node.items.Add(i);
            }
            SortGroups(root);
            CountItems(root);
            ExpandToCurrentValue();
        }

        /// <summary>
        /// Opens the groups standing between the root and the current value, so a popup over a grouped
        /// set shows where the value already is instead of hiding it behind a collapsed header.
        /// </summary>
        private void ExpandToCurrentValue()
        {
            if (currentValue == null) return;

            for (var i = 0; i < items.Length; i++)
            {
                if (!Equals(items[i].value, currentValue)) continue;

                var path = items[i].groupName;
                if (string.IsNullOrWhiteSpace(path)) return;

                var parts = path.Split('/');
                var normalizedPath = string.Empty;
                for (var depth = 0; depth < parts.Length; depth++)
                {
                    var part = parts[depth].Trim();
                    if (part.Length == 0) continue;
                    normalizedPath = normalizedPath.Length == 0
                        ? part
                        : normalizedPath + "/" + part;
                    expandedGroups.Add(normalizedPath);
                }
                return;
            }
        }

        private static GroupNode FindChild(GroupNode parent, string name)
        {
            for (var i = 0; i < parent.children.Count; i++)
                if (string.Equals(parent.children[i].displayName, name,
                        StringComparison.OrdinalIgnoreCase))
                    return parent.children[i];
            return null;
        }

        private static void SortGroups(GroupNode node)
        {
            node.children.Sort((left, right) =>
            {
                var order = left.order.CompareTo(right.order);
                return order != 0
                    ? order
                    : string.Compare(left.displayName, right.displayName,
                        StringComparison.OrdinalIgnoreCase);
            });
            for (var i = 0; i < node.children.Count; i++)
                SortGroups(node.children[i]);
        }

        private static int CountItems(GroupNode node)
        {
            var count = node.items.Count;
            for (var i = 0; i < node.children.Count; i++)
                count += CountItems(node.children[i]);
            node.itemCount = count;
            return count;
        }

        private void Rebuild()
        {
            InspectorVisuals.ClearContent(content);
            visibleButtons.Clear();
            visibleItems.Clear();
            activeSetters.Clear();
            activeIndex = -1;
            if (items.Length == 0 && !showNone)
            {
                AddEmpty(emptyMessage);
                return;
            }
            var query = Normalize(search?.value?.Trim());
            if (!string.IsNullOrEmpty(query))
            {
                var alternate = KeyboardLayoutSearch.Alternate(query);
                var matches = 0;
                if (showNone && Matches(Normalize(noneLabel), query, alternate))
                {
                    AddItem(content, NoneItem(), true);
                    matches++;
                }
                FindMatches(query, alternate);
                for (var i = 0; i < searchMatches.Count; i++)
                {
                    AddItem(content, items[searchMatches[i].index], true);
                    matches++;
                }
                if (matches == 0) AddEmpty("No matching items.");
                RefreshNavigation();
                FocusCurrentItem();
                return;
            }

            if (showNone) AddItem(content, NoneItem(), false);
            for (var i = 0; i < root.items.Count; i++)
                AddItem(content, items[root.items[i]], false);
            for (var i = 0; i < root.children.Count; i++)
                AddGroup(content, root.children[i]);
            RefreshNavigation();
            FocusCurrentItem();
        }

        private SelectorItem NoneItem() => new()
        {
            displayName = noneLabel,
            searchText = noneLabel,
        };

        private void AddEmpty(string message)
        {
            var empty = new Label(message);
            empty.AddToClassList("lightside-selector__empty");
            content.Add(empty);
        }

        private void AddGroup(VisualElement parent, GroupNode group)
        {
            var groupRoot = new VisualElement();
            groupRoot.AddToClassList("lightside-selector__group");
            var header = new InspectorFoldoutHeader(group.displayName);
            header.AddToClassList("lightside-selector__group-header");
            header.SetContent(group.accentKey == null
                ? group.displayName
                : ElementLabelUtility.Colored(group.displayName, group.accentKey), group.icon);
            header.Actions.pickingMode = PickingMode.Ignore;
            var count = InspectorVisuals.CreateCountChip();
            count.text = group.itemCount.ToString();
            count.pickingMode = PickingMode.Ignore;
            count.AddToClassList("lightside-selector__group-count");
            header.Add(count);
            var expanded = expandedGroups.Contains(group.path);
            header.SetExpandedWithoutNotify(expanded);
            var groupBody = InspectorVisuals.CreateHierarchyBody(header);
            groupBody.AddToClassList("lightside-selector__group-body");
            InspectorMotion.SetExpanded(groupBody, expanded, animate: false);
            var populated = false;
            void Populate()
            {
                if (populated) return;
                populated = true;
                for (var i = 0; i < group.items.Count; i++)
                    AddItem(groupBody, items[group.items[i]], false);
                for (var i = 0; i < group.children.Count; i++)
                    AddGroup(groupBody, group.children[i]);
            }
            header.Changed += isExpanded =>
            {
                if (isExpanded)
                {
                    expandedGroups.Add(group.path);
                    Populate();
                }
                else
                    expandedGroups.Remove(group.path);
                InspectorMotion.SetExpanded(groupBody, isExpanded);
                RefreshNavigation();
                FocusCurrentItem(false);
            };
            groupRoot.Add(header);
            groupRoot.Add(groupBody);
            parent.Add(groupRoot);
            if (expanded) Populate();
        }

        private void AddItem(VisualElement parent, SelectorItem item, bool showGroup)
        {
            if (item.separator)
            {
                var divider = new VisualElement { pickingMode = PickingMode.Ignore };
                divider.AddToClassList("lightside-selector__separator");
                parent.Add(divider);
                return;
            }
            var accent = EditorResources.GetAccentColor(
                item.accentKey ?? item.value, item.displayName);
            var selectedItem = IsItemSelected(item);
            var button = new Button();
            button.AddToClassList("lightside-selector__item");
            button.EnableInClassList("lightside-selector__item--detailed",
                !string.IsNullOrWhiteSpace(item.description) ||
                !string.IsNullOrWhiteSpace(item.secondaryText));
            button.tooltip = !string.IsNullOrWhiteSpace(item.details)
                ? item.details
                : item.description;
            button.SetEnabled(!item.disabled);
            if (item.icon != null)
            {
                var image = EditorResources.CreateIcon(item.icon);
                if (item.tintIcon) image.tintColor = accent;
                image.AddToClassList("lightside-selector__item-icon");
                button.Add(image);
            }
            var textColumn = new VisualElement { pickingMode = PickingMode.Ignore };
            textColumn.AddToClassList("lightside-selector__item-text");
            var titleRow = new VisualElement { pickingMode = PickingMode.Ignore };
            titleRow.AddToClassList("lightside-selector__item-title-row");
            var text = new Label(item.displayName)
            {
                pickingMode = PickingMode.Ignore,
                enableRichText = false,
            };
            text.AddToClassList("lightside-selector__item-title");
            text.style.color = accent;
            titleRow.Add(text);
            if (!string.IsNullOrEmpty(item.chip.PlainText))
            {
                text.AddToClassList("lightside-selector__item-title--chipped");
                titleRow.Add(CreateChip(item.chip));
            }
            if (showGroup && !string.IsNullOrWhiteSpace(item.groupName))
            {
                var context = new Label(FormatGroupPath(item.groupName))
                {
                    pickingMode = PickingMode.Ignore,
                    enableRichText = false,
                };
                context.AddToClassList("lightside-selector__item-context");
                titleRow.Add(context);
            }
            textColumn.Add(titleRow);
            if (!string.IsNullOrWhiteSpace(item.description) ||
                !string.IsNullOrWhiteSpace(item.secondaryText))
            {
                var detailRow = new VisualElement { pickingMode = PickingMode.Ignore };
                detailRow.AddToClassList("lightside-selector__item-detail-row");
                if (!string.IsNullOrWhiteSpace(item.description))
                {
                    var description = new Label(item.description)
                    {
                        pickingMode = PickingMode.Ignore,
                        enableRichText = false,
                    };
                    description.AddToClassList("lightside-selector__item-description");
                    detailRow.Add(description);
                }
                if (!string.IsNullOrWhiteSpace(item.secondaryText))
                {
                    var secondary = new Label(item.secondaryText)
                    {
                        pickingMode = PickingMode.Ignore,
                        enableRichText = false,
                    };
                    secondary.AddToClassList("lightside-selector__item-secondary");
                    var secondaryAccent = item.secondaryAccentKey != null
                        ? EditorResources.GetAccentColor(item.secondaryAccentKey,
                            item.secondaryText)
                        : EditorResources.IconColor;
                    secondary.style.color = secondaryAccent;
                    var secondaryBackground = secondaryAccent;
                    secondaryBackground.a = 0.09f;
                    secondary.style.backgroundColor = secondaryBackground;
                    detailRow.Add(secondary);
                }
                textColumn.Add(detailRow);
            }
            button.Add(textColumn);
            if (item.createDecorator?.Invoke() is { } decorator)
            {
                decorator.pickingMode = PickingMode.Ignore;
                decorator.AddToClassList("lightside-selector__item-decorator");
                decorator.style.color = accent;
                button.Add(decorator);
            }
            var body = item.createBody == null ? null : AddDisclosure(button, item);
            if (item.createAction?.Invoke() is { } action)
            {
                action.AddToClassList("lightside-selector__item-action");
                MakeIndependent(action);
                button.Add(action);
            }
            if (selectedItem)
            {
                button.AddToClassList("lightside-selector__item--selected");
                var check = InspectorVisuals.CreateCheckmark();
                check.AddToClassList("lightside-selector__check");
                foreach (var stroke in check.Children())
                    stroke.style.backgroundColor = accent;
                button.Add(check);
            }
            var hovered = false;
            var focused = false;
            var keyboardActive = false;
            void RefreshAccent()
                => SetItemAccent(button, accent, selectedItem,
                    hovered || focused || keyboardActive);
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                hovered = true;
                if (!item.disabled)
                {
                    var index = visibleButtons.IndexOf(button);
                    if (index >= 0) SetActive(index, false, false);
                }
                RefreshAccent();
            });
            button.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hovered = false;
                RefreshAccent();
            });
            button.RegisterCallback<FocusInEvent>(_ =>
            {
                focused = true;
                RefreshAccent();
            });
            button.RegisterCallback<FocusOutEvent>(_ =>
            {
                focused = false;
                RefreshAccent();
            });
            RefreshAccent();
            button.RegisterCallback<ClickEvent>(evt => Choose(item.value, evt.altKey));
            if (!item.disabled)
                button.userData = new ItemNavigation(item, value =>
                {
                    keyboardActive = value;
                    RefreshAccent();
                });
            if (body == null)
            {
                parent.Add(button);
                return;
            }
            var itemRoot = new VisualElement();
            itemRoot.AddToClassList("lightside-selector__item-root");
            itemRoot.Add(button);
            itemRoot.Add(body);
            parent.Add(itemRoot);
        }

        /// <summary>
        /// Creates the trailing type qualifier of a parameter row: a dimmed, right-aligned label the
        /// selector tints with the row's own accent. Hand it to <see cref="SelectorItem.createDecorator"/>
        /// so the qualifier reads on the title's line instead of the detail line under it.
        /// </summary>
        public static VisualElement CreateTypeHint(string text)
        {
            var label = new Label(text)
            {
                pickingMode = PickingMode.Ignore,
                enableRichText = false,
            };
            label.AddToClassList("lightside-selector__item-type-hint");
            return label;
        }

        private static VisualElement CreateChip(InspectorLabel label)
        {
            var accent = EditorResources.GetAccentColor(label.AccentKey, label.PlainText);
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("lightside-selector__item-chip");
            if (label.Image != null)
            {
                var image = EditorResources.CreateIcon(label.Image);
                image.AddToClassList("lightside-selector__item-chip-icon");
                chip.Add(image);
            }
            var text = new Label(label.PlainText)
            {
                pickingMode = PickingMode.Ignore,
                enableRichText = false,
            };
            text.AddToClassList("lightside-selector__item-chip-text");
            text.style.color = accent;
            chip.Add(text);
            var background = accent;
            background.a = 0.11f;
            chip.style.backgroundColor = background;
            return chip;
        }

        private VisualElement AddDisclosure(VisualElement row, SelectorItem item)
        {
            var key = item.value ?? item.displayName;
            var body = new VisualElement();
            body.AddToClassList("lightside-selector__item-body");
            var toggle = InspectorFoldoutHeader.CreateDisclosureToggle();
            var disclosure = new Button();
            disclosure.AddToClassList("lightside-selector__item-disclosure");
            disclosure.Add(toggle);
            MakeIndependent(disclosure);
            var expanded = key != null && expandedItems.Contains(key);
            var populated = false;

            void SetExpanded(bool value, bool animate)
            {
                expanded = value;
                if (value && !populated)
                {
                    populated = true;
                    if (item.createBody() is { } content) body.Add(content);
                }
                if (key != null)
                {
                    if (value) expandedItems.Add(key);
                    else expandedItems.Remove(key);
                }
                toggle.SetValueWithoutNotify(value);
                InspectorMotion.SetExpanded(body, value, animate);
            }

            disclosure.clicked += () =>
            {
                SetExpanded(!expanded, true);
                RefreshNavigation();
            };
            SetExpanded(expanded, false);
            row.Add(disclosure);
            return body;
        }

        /// <summary>Keeps a control's own clicks from reaching the row that hosts it.</summary>
        private static void MakeIndependent(VisualElement element)
        {
            element.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            element.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            element.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        }

        private static void SetItemAccent(VisualElement item, Color accent,
            bool selected, bool highlighted)
        {
            var background = new Color(0.5f, 0.5f, 0.5f, 0.04f);
            var border = Color.clear;
            if (selected || highlighted)
            {
                background = accent;
                background.a = selected ? highlighted ? 0.20f : 0.14f : 0.10f;
                border = accent;
                border.a = selected ? 1f : 0.62f;
            }
            item.style.backgroundColor = background;
            item.style.borderLeftColor = border;
            item.style.borderRightColor = border;
            item.style.borderTopColor = border;
            item.style.borderBottomColor = border;
        }

        private void RefreshNavigation()
        {
            if ((uint)activeIndex < (uint)activeSetters.Count)
                activeSetters[activeIndex](false);
            visibleButtons.Clear();
            visibleItems.Clear();
            activeSetters.Clear();
            activeIndex = -1;
            CollectNavigation(content);
        }

        private void CollectNavigation(VisualElement parent)
        {
            foreach (var child in parent.Children())
            {
                var display = child.style.display;
                if (display.keyword == StyleKeyword.Undefined &&
                    display.value == DisplayStyle.None) continue;
                if (child is Button button && button.userData is ItemNavigation navigation)
                {
                    visibleButtons.Add(button);
                    visibleItems.Add(navigation.item);
                    activeSetters.Add(navigation.setActive);
                }
                CollectNavigation(child);
            }
        }

        private void MoveActive(int direction)
        {
            if (visibleButtons.Count == 0) return;
            var next = activeIndex < 0
                ? direction > 0 ? 0 : visibleButtons.Count - 1
                : Mathf.Clamp(activeIndex + direction, 0, visibleButtons.Count - 1);
            SetActive(next, true);
        }

        private void SetActive(int index, bool reveal, bool moveFocus = true)
        {
            if ((uint)index >= (uint)visibleButtons.Count) return;
            if ((uint)activeIndex < (uint)activeSetters.Count)
                activeSetters[activeIndex](false);
            activeIndex = index;
            activeSetters[activeIndex](true);
            if (reveal) content.ScrollTo(visibleButtons[activeIndex]);
            if (moveFocus && search == null) visibleButtons[activeIndex].Focus();
        }

        private void ChooseActive(bool keepOpen)
        {
            if ((uint)activeIndex >= (uint)visibleItems.Count) return;
            Choose(visibleItems[activeIndex].value, keepOpen);
        }

        private void Choose(object value, bool keepOpen)
        {
            selected(value);
            currentValue = value;
            if (keepOpenAfterSelection || keepOpen)
            {
                Rebuild();
                search?.Focus();
            }
            else if (closeOwner)
                Close();
            else
                CloseToOwner();
        }

        private bool IsItemSelected(SelectorItem item)
            => !item.separator &&
               (item.selected ||
                (isSelected?.Invoke(item.value) ?? Equals(currentValue, item.value)));

        private void BuildSearchDocuments()
        {
            searchDocuments = new SearchDocument[items.Length];
            for (var i = 0; i < items.Length; i++)
                searchDocuments[i] = new SearchDocument(i, items[i]);
        }

        private void FindMatches(string query, string alternate)
        {
            searchMatches.Clear();
            var queryTokens = SearchTokens(query);
            var alternateTokens = alternate == query
                ? queryTokens
                : SearchTokens(alternate);
            for (var i = 0; i < searchDocuments.Length; i++)
            {
                var score = SearchScore(searchDocuments[i], query, queryTokens);
                if (alternate != query)
                    score = Mathf.Min(score, SearchScore(
                        searchDocuments[i], alternate, alternateTokens));
                if (score == int.MaxValue) continue;
                searchMatches.Add(new SearchMatch(searchDocuments[i].index, score));
            }
            searchMatches.Sort((left, right) =>
            {
                var score = left.score.CompareTo(right.score);
                return score != 0 ? score : left.index.CompareTo(right.index);
            });
        }

        private static string[] SearchTokens(string query)
            => query.Split(searchSeparators, StringSplitOptions.RemoveEmptyEntries);

        private static int SearchScore(SearchDocument document, string query,
            string[] tokens)
        {
            if (document.title == query) return 0;
            if (document.title.StartsWith(query, StringComparison.Ordinal)) return 10;
            var titleIndex = document.title.IndexOf(query, StringComparison.Ordinal);
            if (titleIndex >= 0) return 20 + titleIndex;

            var score = 100;
            for (var i = 0; i < tokens.Length; i++)
            {
                var tokenScore = SearchTokenScore(document, tokens[i]);
                if (tokenScore == int.MaxValue) return int.MaxValue;
                score += tokenScore;
            }
            return score;
        }

        private static int SearchTokenScore(SearchDocument document, string token)
        {
            var score = FieldScore(document.title, token, 0);
            score = Mathf.Min(score, FieldScore(document.secondary, token, 40));
            score = Mathf.Min(score, FieldScore(document.group, token, 60));
            score = Mathf.Min(score, FieldScore(document.metadata, token, 80));
            score = Mathf.Min(score, FieldScore(document.description, token, 120));
            return Mathf.Min(score, FieldScore(document.details, token, 160));
        }

        private static int FieldScore(string source, string token, int weight)
        {
            if (source.Length == 0) return int.MaxValue;
            var index = source.IndexOf(token, StringComparison.Ordinal);
            return index < 0 ? int.MaxValue : weight + index;
        }

        private static bool Matches(string source, string query, string alternate)
        {
            if (string.IsNullOrEmpty(source)) return false;
            return source.Contains(query) ||
                   alternate != query && source.Contains(alternate);
        }

        private static string Normalize(string value)
            => string.IsNullOrEmpty(value) ? string.Empty : value.ToLowerInvariant();

        private static string FormatGroupPath(string path)
            => string.IsNullOrEmpty(path) ? string.Empty : path.Replace("/", " › ");

        private static void PreProcessItems(SelectorItem[] source)
        {
            for (var i = 0; i < source.Length; i++)
            {
                var item = source[i];
                if (item.separator) continue;
                item.displayName ??= item.value?.ToString() ?? "(None)";
                item.searchText ??= item.displayName;
                source[i] = item;
            }
        }

        /// <summary>Height of the initial browse tree, whose groups all start collapsed.</summary>
        private float EstimatedBrowseHeight()
        {
            var height = showNone ? CompactItemHeight : 0f;
            for (var i = 0; i < root.items.Count; i++)
                height += ItemHeight(items[root.items[i]]);
            return height + GroupHeight * root.children.Count;
        }

        private static float ItemHeight(SelectorItem item)
            => item.separator
                ? SeparatorHeight
                : string.IsNullOrWhiteSpace(item.description) &&
                  string.IsNullOrWhiteSpace(item.secondaryText)
                    ? CompactItemHeight
                    : DetailedItemHeight;

        private static bool HasRichContent(SelectorItem[] source)
        {
            for (var i = 0; i < source.Length; i++)
                if (!string.IsNullOrWhiteSpace(source[i].description) ||
                    !string.IsNullOrWhiteSpace(source[i].secondaryText) ||
                    source[i].createBody != null) return true;
            return false;
        }

        private static float PreferredCompactWidth(SelectorItem[] source,
            bool showNone, string noneLabel, string emptyMessage, bool showSearch)
        {
            var width = showSearch ? SearchMinWidth : CompactMinWidth;
            if (source.Length == 0 && !showNone)
                return Mathf.Min(CompactMaxWidth, Mathf.Max(width,
                    TextWidth(emptyMessage) + CompactEmptyChromeWidth));
            if (showNone)
                width = Mathf.Max(width,
                    TextWidth(noneLabel) + CompactItemChromeWidth);
            for (var i = 0; i < source.Length; i++)
            {
                var item = source[i];
                if (item.separator) continue;
                var itemWidth = TextWidth(item.displayName) + CompactItemChromeWidth;
                if (item.icon != null) itemWidth += CompactIconWidth;
                if (!string.IsNullOrEmpty(item.chip.PlainText))
                    itemWidth += TextWidth(item.chip.PlainText, ChipStyle) +
                                 CompactChipChromeWidth;
                if (item.createDecorator != null) itemWidth += CompactDecoratorWidth;
                if (item.createAction != null) itemWidth += CompactActionWidth;
                if (item.createBody != null) itemWidth += CompactDisclosureWidth;
                width = Mathf.Max(width, itemWidth);
                if (!string.IsNullOrEmpty(item.groupName))
                    width = Mathf.Max(width,
                        TextWidth(item.groupName) + CompactGroupChromeWidth);
            }
            return Mathf.Min(width, CompactMaxWidth);
        }

        //TODO: EditorStyles members are unset until the first IMGUI pass after a domain reload, and popup
        // sizing reads them from UI Toolkit click callbacks — same defect class the InspectorTooltip
        // resolves by creating styles inside its IMGUI pass.
        private static float TextWidth(string text)
            => TextWidth(text, EditorStyles.label);

        private static float TextWidth(string text, GUIStyle style)
            => style.CalcSize(new GUIContent(text ?? string.Empty)).x;

        private static GUIStyle chipStyle;

        private static GUIStyle ChipStyle =>
            chipStyle ??= new GUIStyle(EditorStyles.label) { fontSize = ChipFontSize };

    }

    /// <summary>Bindable UI Toolkit field that opens <see cref="Selector"/> for rich typed choices.</summary>
    public class SelectorField<TValue> : BaseField<TValue>
    {
        private const string BasePopupClass = "unity-base-popup-field";
        private const string PopupClass = "unity-popup-field";
        private const string BasePopupLabelClass = "unity-base-popup-field__label";
        private const string PopupLabelClass = "unity-popup-field__label";

        private Func<Selector.SelectorItem[]> itemsProvider;
        private bool showSearch;
        private InspectorSelectorButton button;
        private TextElement selectedText;
        private List<TValue> simpleChoices;
        private Func<TValue, object, bool> isSelected;
        private Func<TValue, object, TValue> selectValue;
        private Func<TValue, Selector.SelectorItem[], string> formatValue;

        /// <summary>Creates a typed popup field whose choices are resolved when the popup opens.</summary>
        public SelectorField(string label, TValue value,
            Func<Selector.SelectorItem[]> itemsProvider, bool showSearch = true)
            : base(label, new InspectorSelectorButton())
        {
            this.itemsProvider = itemsProvider ?? throw new ArgumentNullException(nameof(itemsProvider));
            this.showSearch = showSearch;
            Initialize();
            SetValueWithoutNotify(value);
        }

        /// <summary>Creates a typed popup field from a simple ordered set of choices.</summary>
        public SelectorField(string label, IReadOnlyList<TValue> choices, int selectedIndex,
            bool showSearch = true) : base(label, new InspectorSelectorButton())
        {
            Initialize();
            SetChoices(choices, selectedIndex, showSearch);
        }

        /// <summary>Creates an unlabeled typed popup field from a simple ordered set of choices.</summary>
        public SelectorField(IReadOnlyList<TValue> choices, int selectedIndex,
            bool showSearch = true) : this(null, choices, selectedIndex, showSearch)
        {
        }

        /// <summary>Current choice index, or -1 when the value is not in the simple choice set.</summary>
        public int Index
        {
            get
            {
                if (simpleChoices == null) return -1;
                for (var i = 0; i < simpleChoices.Count; i++)
                    if (EqualityComparer<TValue>.Default.Equals(simpleChoices[i], value))
                        return i;
                return -1;
            }
        }

        /// <summary>Replaces the simple choices and selects an item without sending a change event.</summary>
        public void SetChoices(IReadOnlyList<TValue> choices, int selectedIndex,
            bool showSearch = true)
        {
            if (choices == null) throw new ArgumentNullException(nameof(choices));
            if (choices.Count == 0) throw new ArgumentException(
                "A selector field requires at least one choice.", nameof(choices));
            if ((uint)selectedIndex >= (uint)choices.Count)
                throw new ArgumentOutOfRangeException(nameof(selectedIndex));

            simpleChoices ??= new List<TValue>(choices.Count);
            simpleChoices.Clear();
            for (var i = 0; i < choices.Count; i++) simpleChoices.Add(choices[i]);
            itemsProvider = CreateSimpleItems;
            this.showSearch = showSearch;
            SetValueWithoutNotify(simpleChoices[selectedIndex]);
        }

        /// <summary>Creates a field with custom selection and display semantics.</summary>
        protected SelectorField(string label, TValue value,
            Func<Selector.SelectorItem[]> itemsProvider, bool showSearch,
            Func<TValue, object, bool> isSelected,
            Func<TValue, object, TValue> selectValue,
            Func<TValue, Selector.SelectorItem[], string> formatValue)
            : base(label, new InspectorSelectorButton())
        {
            this.itemsProvider = itemsProvider ?? throw new ArgumentNullException(nameof(itemsProvider));
            this.showSearch = showSearch;
            if ((isSelected == null) != (selectValue == null))
                throw new ArgumentException(
                    "Custom selection and value handlers must be supplied together.");
            this.isSelected = isSelected;
            this.selectValue = selectValue;
            this.formatValue = formatValue;
            Initialize();
            SetValueWithoutNotify(value);
        }

        private void Initialize()
        {
            InspectorVisuals.Attach(this);
            AddToClassList(BasePopupClass);
            AddToClassList(PopupClass);
            if (!string.IsNullOrEmpty(label))
                InspectorVisuals.MarkFieldAxis(this);
            else
                AddToClassList(BaseField<TValue>.noLabelVariantUssClassName);
            labelElement.AddToClassList(BasePopupLabelClass);
            labelElement.AddToClassList(PopupLabelClass);
            button = this.Q<InspectorSelectorButton>() ??
                     throw new InvalidOperationException("Selector field input is missing.");
            selectedText = button.ValueElement;
            button.clicked += Open;
        }

        /// <inheritdoc/>
        public override void SetValueWithoutNotify(TValue newValue)
        {
            base.SetValueWithoutNotify(newValue);
            if (!showMixedValue) RefreshDisplayedValue();
        }

        protected override void UpdateMixedValueContent()
        {
            if (selectedText == null) return;
            selectedText.EnableInClassList(
                BaseField<TValue>.mixedValueLabelUssClassName, showMixedValue);
            if (showMixedValue)
            {
                selectedText.text = mixedValueLabel.text;
                selectedText.style.color = new StyleColor(StyleKeyword.Null);
                return;
            }
            RefreshDisplayedValue();
        }

        private void RefreshDisplayedValue()
        {
            var available = GetItems();
            selectedText.text = GetDisplayName(value, available);
            button.SetValueAccent(ValueAccentKey(value, available), selectedText.text);
            button.SetValueIcon(ValueIcon(value, available));
        }

        /// <summary>
        /// Semantic identity colouring the displayed value. Defaults to the matching item's accent
        /// identity; override when the button represents something the rows do not colour.
        /// </summary>
        protected virtual object ValueAccentKey(TValue current, Selector.SelectorItem[] available)
            => GetAccentKey(current, available);

        /// <summary>
        /// Leading icon of the displayed value. Defaults to the matching item's icon; override when
        /// the button carries an icon the rows do not.
        /// </summary>
        protected virtual Texture ValueIcon(TValue current, Selector.SelectorItem[] available)
        {
            for (var i = 0; i < available.Length; i++)
                if (Equals(available[i].value, current))
                    return available[i].icon;
            return null;
        }

        private void Open()
        {
            var available = GetItems();
            if (isSelected != null)
            {
                Selector.ShowMultiple(button.worldBound, available,
                    choice => isSelected(value, choice), choice =>
                    {
                        this.value = selectValue(value, choice);
                    }, showSearch);
                return;
            }
            Selector.Show(button.worldBound, available, value, choice =>
            {
                if (choice is not TValue typed)
                    throw new InvalidOperationException(
                        $"Selector returned {choice?.GetType().FullName ?? "null"} for " +
                        $"{typeof(TValue).FullName} field.");
                CommitSelection(typed);
            }, showSearch: showSearch);
        }

        private void CommitSelection(TValue selected)
        {
            if (!showMixedValue ||
                !EqualityComparer<TValue>.Default.Equals(value, selected))
            {
                value = selected;
                return;
            }

            using var evt = ChangeEvent<TValue>.GetPooled(value, selected);
            evt.target = this;
            SendEvent(evt);
        }

        private Selector.SelectorItem[] GetItems()
            => itemsProvider() ??
               throw new InvalidOperationException("Selector item provider returned null.");

        private Selector.SelectorItem[] CreateSimpleItems()
        {
            var result = new Selector.SelectorItem[simpleChoices.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var choice = simpleChoices[i];
                var label = ReferenceEquals(choice, null) ? "(None)" : choice.ToString();
                result[i] = new Selector.SelectorItem
                {
                    displayName = label,
                    searchText = label,
                    value = choice,
                };
            }
            return result;
        }

        private string GetDisplayName(TValue current, Selector.SelectorItem[] available)
            => formatValue?.Invoke(current, available) ?? DisplayName(current, available);

        private static object GetAccentKey(TValue current,
            Selector.SelectorItem[] available)
        {
            for (var i = 0; i < available.Length; i++)
                if (Equals(available[i].value, current))
                    return available[i].accentKey ?? available[i].value;
            return current;
        }

        private static string DisplayName(TValue value, Selector.SelectorItem[] available)
        {
            for (var i = 0; i < available.Length; i++)
                if (Equals(available[i].value, value))
                    return available[i].displayName ??
                           available[i].value?.ToString() ?? "(None)";
            return ReferenceEquals(value, null) ? "(None)" : value.ToString();
        }
    }

    /// <summary>Selector-backed field for regular and flags enums.</summary>
    public sealed class EnumSelectorField : SelectorField<Enum>
    {
        /// <summary>
        /// Creates a selector using the named values of the supplied enum. <paramref name="include"/>
        /// restricts which members are offered; the current value stays displayed even when excluded.
        /// </summary>
        public EnumSelectorField(string label, Enum value, Func<Enum, bool> include = null)
            : base(label, value, () => CreateItems(GetEnumType(value), include), true,
                SelectionFor(value), SelectionMutationFor(value),
                FormatValue)
        {
        }

        private static Func<Enum, object, bool> SelectionFor(Enum value)
            => IsFlags(GetEnumType(value)) ? IsSelected : null;

        private static Func<Enum, object, Enum> SelectionMutationFor(Enum value)
            => IsFlags(GetEnumType(value)) ? Select : null;

        private static Type GetEnumType(Enum value)
            => value?.GetType() ?? throw new ArgumentNullException(nameof(value));

        private static Selector.SelectorItem[] CreateItems(Type type, Func<Enum, bool> include)
        {
            var names = Enum.GetNames(type);
            var values = Enum.GetValues(type);
            var isFlags = IsFlags(type);
            if (include != null && !isFlags)
            {
                var kept = new List<string>(names.Length);
                var keptValues = new List<Enum>(names.Length);
                for (var i = 0; i < names.Length; i++)
                {
                    var member = (Enum)values.GetValue(i);
                    if (!include(member)) continue;
                    kept.Add(names[i]);
                    keptValues.Add(member);
                }
                var filtered = new Selector.SelectorItem[kept.Count];
                for (var i = 0; i < filtered.Length; i++)
                    filtered[i] = CreateItem(ObjectNames.NicifyVariableName(kept[i]), keptValues[i]);
                return filtered;
            }
            var all = isFlags ? GetAllBits(values) : 0UL;
            var hasZero = false;
            var hasAll = false;
            for (var i = 0; i < values.Length; i++)
            {
                var bits = ToUInt64((Enum)values.GetValue(i));
                hasZero |= bits == 0;
                hasAll |= bits == all;
            }

            var extra = isFlags ? (hasZero ? 0 : 1) + (hasAll || all == 0 ? 0 : 1) : 0;
            var result = new Selector.SelectorItem[names.Length + extra];
            var offset = 0;
            if (isFlags && !hasZero)
                result[offset++] = CreateItem("Nothing", (Enum)Enum.ToObject(type, 0UL));
            for (var i = 0; i < names.Length; i++)
                result[offset++] = CreateItem(ObjectNames.NicifyVariableName(names[i]),
                    (Enum)values.GetValue(i));
            if (isFlags && !hasAll && all != 0)
                result[offset] = CreateItem("Everything", (Enum)Enum.ToObject(type, all));
            return result;
        }

        private static Selector.SelectorItem CreateItem(string label, Enum value)
            => new()
            {
                displayName = label,
                searchText = label,
                value = value,
            };

        private static bool IsFlags(Type type)
            => type.IsDefined(typeof(FlagsAttribute), false);

        private static bool IsSelected(Enum current, object candidate)
        {
            var selected = (Enum)candidate;
            var currentBits = ToUInt64(current);
            var selectedBits = ToUInt64(selected);
            return selectedBits == 0 ? currentBits == 0 : (currentBits & selectedBits) == selectedBits;
        }

        private static Enum Select(Enum current, object candidate)
        {
            var selected = (Enum)candidate;
            var type = current.GetType();
            var currentBits = ToUInt64(current);
            var selectedBits = ToUInt64(selected);
            var all = GetAllBits(Enum.GetValues(type));
            ulong next;
            if (selectedBits == 0)
                next = 0;
            else if (selectedBits == all)
                next = currentBits == all ? 0 : all;
            else
                next = currentBits ^ selectedBits;
            return (Enum)Enum.ToObject(type, next);
        }

        private static string FormatValue(Enum value, Selector.SelectorItem[] items)
        {
            for (var i = 0; i < items.Length; i++)
                if (Equals(items[i].value, value)) return items[i].displayName;
            var text = value.ToString();
            if (string.IsNullOrEmpty(text)) return "Nothing";
            var parts = text.Split(',');
            for (var i = 0; i < parts.Length; i++)
                parts[i] = ObjectNames.NicifyVariableName(parts[i].Trim());
            return string.Join(", ", parts);
        }

        private static ulong GetAllBits(Array values)
        {
            var all = 0UL;
            for (var i = 0; i < values.Length; i++)
                all |= ToUInt64((Enum)values.GetValue(i));
            return all;
        }

        private static ulong ToUInt64(Enum value)
        {
            switch (Type.GetTypeCode(Enum.GetUnderlyingType(value.GetType())))
            {
                case TypeCode.SByte: return unchecked((ulong)Convert.ToSByte(value));
                case TypeCode.Int16: return unchecked((ulong)Convert.ToInt16(value));
                case TypeCode.Int32: return unchecked((ulong)Convert.ToInt32(value));
                case TypeCode.Int64: return unchecked((ulong)Convert.ToInt64(value));
                case TypeCode.Byte: return Convert.ToByte(value);
                case TypeCode.UInt16: return Convert.ToUInt16(value);
                case TypeCode.UInt32: return Convert.ToUInt32(value);
                case TypeCode.UInt64: return Convert.ToUInt64(value);
                default: throw new InvalidOperationException(
                    $"Unsupported enum storage type {Enum.GetUnderlyingType(value.GetType()).FullName}.");
            }
        }
    }
}
