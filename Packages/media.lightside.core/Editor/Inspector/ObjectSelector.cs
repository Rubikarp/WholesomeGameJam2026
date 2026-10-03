using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
using ObjectField = UnityEditor.UIElements.ObjectField;

namespace LightSide
{
    internal sealed class ObjectSelector : InspectorPopupWindow
    {
        private enum RowKind
        {
            None,
            Group,
            Object,
        }

        private sealed class Entry
        {
            public SearchItem searchItem;
            public GlobalObjectId assetId;
            public Object sceneObject;
            public string id;
            public string path;
            public string groupPath;
            public string sortName;
            public Type type;
            public int rank;
            public Texture icon;
            public bool isFolder;
            public bool hasAssetId;
            private string displayName;

            public string DisplayName
            {
                get
                {
                    if (displayName != null) return displayName;
                    if (sceneObject != null) return displayName = sceneObject.name;
                    var label = searchItem?.GetLabel(null, true);
                    return displayName = string.IsNullOrWhiteSpace(label)
                        ? Path.GetFileNameWithoutExtension(path)
                        : label;
                }
            }
        }

        private sealed class Catalog
        {
            public Catalog(Entry[] entries, GroupNode root)
            {
                Entries = entries;
                Root = root;
            }

            public Entry[] Entries { get; }
            public GroupNode Root { get; }
        }

        private sealed class GroupNode
        {
            public string name;
            public string key;
            public int count;
            public bool sorted;
            public bool typeRoot;
            public Type type;
            public Texture icon;
            public GroupNode parent;
            public readonly List<GroupNode> children = new();
            public readonly Dictionary<string, GroupNode> childMap =
                new(StringComparer.OrdinalIgnoreCase);
            public readonly List<Entry> entries = new();
        }

        private readonly struct Row
        {
            public Row(RowKind kind, Entry entry, GroupNode group,
                string label, int depth, bool showContext, int groupCount = 0)
            {
                Kind = kind;
                Entry = entry;
                Group = group;
                Label = label;
                Depth = depth;
                ShowContext = showContext;
                GroupCount = groupCount;
            }

            public RowKind Kind { get; }
            public Entry Entry { get; }
            public GroupNode Group { get; }
            public string Label { get; }
            public int Depth { get; }
            public bool ShowContext { get; }
            public int GroupCount { get; }
        }

        private sealed class RowElement : VisualElement
        {
            private readonly ObjectSelector owner;
            private readonly Toggle disclosure = InspectorFoldoutHeader.CreateDisclosureToggle();
            private readonly Image icon = new();
            private readonly Label title = new();
            private readonly Label context = new();
            private readonly Label secondary = new();
            private readonly Label count = InspectorVisuals.CreateCountChip();
            private readonly VisualElement check;
            private int index = -1;
            private Color accent;
            private bool selected;

            public RowElement(ObjectSelector owner)
            {
                this.owner = owner;
                focusable = true;
                AddToClassList("lightside-object-selector__row");

                disclosure.AddToClassList(InspectorFoldoutHeader.ToggleUssClassName);
                Add(disclosure);

                icon.AddToClassList("lightside-selector__item-icon");
                icon.scaleMode = ScaleMode.ScaleToFit;
                icon.pickingMode = PickingMode.Ignore;
                Add(icon);

                title.AddToClassList("lightside-selector__item-title");
                title.pickingMode = PickingMode.Ignore;
                title.enableRichText = false;
                Add(title);

                context.AddToClassList("lightside-selector__item-context");
                context.enableRichText = false;
                Add(context);

                secondary.AddToClassList("lightside-selector__item-secondary");
                secondary.pickingMode = PickingMode.Ignore;
                secondary.enableRichText = false;
                Add(secondary);

                count.AddToClassList("lightside-selector__group-count");
                count.pickingMode = PickingMode.Ignore;
                Add(count);

                check = InspectorVisuals.CreateCheckmark();
                check.AddToClassList("lightside-selector__check");
                check.pickingMode = PickingMode.Ignore;
                Add(check);

                RegisterCallback<PointerEnterEvent>(_ => owner.SetActive(index));
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || index < 0) return;
                    owner.Activate(index, evt.altKey);
                    evt.StopPropagation();
                });
            }

            public int Index => index;

            public void Bind(in Row row, int rowIndex)
            {
                index = rowIndex;
                style.translate = StyleKeyword.Null;
                style.marginLeft = 2f + row.Depth * 12f;
                EnableInClassList("lightside-selector__group-header",
                    row.Kind == RowKind.Group);
                EnableInClassList("lightside-selector__item",
                    row.Kind != RowKind.Group);
                EnableInClassList("lightside-object-selector__row--group",
                    row.Kind == RowKind.Group);

                if (row.Kind == RowKind.Group)
                {
                    var group = row.Group;
                    disclosure.SetValueWithoutNotify(owner.expandedGroups.Contains(group.key));
                    icon.image = group.icon;
                    title.text = row.Label;
                    context.text = string.Empty;
                    secondary.text = string.Empty;
                    count.text = row.GroupCount.ToString();
                    accent = EditorResources.GetAccentColor(
                        group.type ?? (object)group.key, row.Label);
                    selected = false;
                }
                else
                {
                    var entry = row.Entry;
                    disclosure.SetValueWithoutNotify(false);
                    icon.image = entry == null ? null : owner.GetIcon(entry);
                    title.text = row.Kind == RowKind.None ? owner.noneLabel : entry.DisplayName;
                    context.text = row.ShowContext && entry != null ? entry.path : string.Empty;
                    secondary.text = entry != null && owner.showTypeLabels
                        ? ObjectNames.NicifyVariableName(entry.type.Name)
                        : string.Empty;
                    count.text = string.Empty;
                    accent = EditorResources.GetAccentColor(
                        entry?.type ?? typeof(Object), title.text);
                    selected = owner.IsSelected(row);
                }

                disclosure.style.display = row.Kind == RowKind.Group
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                icon.style.display = icon.image != null ? DisplayStyle.Flex : DisplayStyle.None;
                context.style.display = string.IsNullOrEmpty(context.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                secondary.style.display = string.IsNullOrEmpty(secondary.text)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                count.style.display = row.Kind == RowKind.Group
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                check.style.display = selected ? DisplayStyle.Flex : DisplayStyle.None;
                title.style.color = accent;
                secondary.style.color = accent;
                var secondaryBackground = accent;
                secondaryBackground.a = 0.09f;
                secondary.style.backgroundColor = secondaryBackground;
                foreach (var stroke in check.Children())
                    stroke.style.backgroundColor = accent;
                RefreshAccent();
            }

            public void RefreshAccent()
            {
                var highlighted = owner.activeIndex == index;
                var background = new Color(0.5f, 0.5f, 0.5f, 0.04f);
                var border = Color.clear;
                if (selected || highlighted)
                {
                    background = accent;
                    background.a = selected ? highlighted ? 0.20f : 0.14f : 0.10f;
                    border = accent;
                    border.a = selected ? 1f : 0.62f;
                }
                style.backgroundColor = background;
                style.borderLeftColor = border;
                style.borderRightColor = border;
                style.borderTopColor = border;
                style.borderBottomColor = border;
            }
        }

        private const float RowHeight = 33f;
        private const float MinWidth = 480f;
        private const float MinHeight = 240f;
        private const float SearchHeight = 41f;
        private const float StatusHeight = 29f;
        private const float PopupChromeHeight = 6f;
        private const int CatalogCapacity = 8;
        private const long ProgressiveRefreshMilliseconds = 50;

        private static readonly Dictionary<Type, Catalog> catalogs = new();
        private static Dictionary<Type, string> scriptPaths;
        private static readonly Texture folderIcon =
            EditorGUIUtility.IconContent("Folder Icon").image;

        private Type objectType;
        private Object currentValue;
        private Action<Object> selected;
        private bool showNone;
        private bool allowSceneObjects;
        private string noneLabel;
        private bool hasCurrentAssetId;
        private bool currentIsMainAsset;
        private GlobalObjectId currentAssetId;
        private string currentAssetPath;
        private readonly List<Entry> entries = new();
        private readonly HashSet<string> receivedIds = new(StringComparer.Ordinal);
        private readonly Queue<SearchItem> pendingItems = new();
        private readonly List<Row> rows = new();
        private readonly HashSet<string> expandedGroups =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object noneIdentity = new();
        private readonly Dictionary<object, int> slideSnapshot = new();
        private readonly List<(RowElement cell, object identity, float delta)> slides = new();
        private readonly InspectorMotion.Ticker slideTicker = new();
        private float displayedHeight;
        private InspectorSearchField search;
        private ListView list;
        private Label status;
        private SearchContext requestContext;
        private IVisualElementScheduledItem pendingSearch;
        private IVisualElementScheduledItem pendingProcessing;
        private IVisualElementScheduledItem pendingPresentation;
        private IVisualElementScheduledItem pendingContinuation;
        private int requestGeneration;
        private int presentedEntryCount;
        private bool requestCompleted;
        private bool finalRequest;
        private bool requestGroupByType;
        private string requestQuery;
        private string requestAlternate;
        private int activeIndex = -1;
        private bool showTypeLabels;
        private Catalog browseCatalog;

        static ObjectSelector() => EditorApplication.projectChanged += ClearCatalogs;

        public static void Show(Rect buttonRect, Type objectType, Object currentValue,
            Action<Object> onSelected, bool showNone, bool allowSceneObjects,
            string noneLabel = null)
        {
            if (objectType == null) throw new ArgumentNullException(nameof(objectType));
            if (!typeof(Object).IsAssignableFrom(objectType))
                throw new ArgumentException(
                    $"{objectType.FullName} is not a Unity object type.", nameof(objectType));
            if (onSelected == null) throw new ArgumentNullException(nameof(onSelected));

            var window = CreateInstance<ObjectSelector>();
            window.objectType = objectType;
            window.currentValue = currentValue;
            window.selected = onSelected;
            window.showNone = showNone;
            window.allowSceneObjects = allowSceneObjects;
            window.noneLabel = string.IsNullOrWhiteSpace(noneLabel) ? "(None)" : noneLabel;
            if (currentValue != null && EditorUtility.IsPersistent(currentValue))
            {
                window.hasCurrentAssetId = true;
                window.currentIsMainAsset = AssetDatabase.IsMainAsset(currentValue);
                window.currentAssetId = GlobalObjectId.GetGlobalObjectIdSlow(currentValue);
                window.currentAssetPath = AssetDatabase.GetAssetPath(currentValue);
            }

            var screenRect = InspectorVisuals.CaptureScreenRect(buttonRect);
            var width = Mathf.Max(buttonRect.width, MinWidth);
            window.ShowAtScreenRect(screenRect, width, MinHeight);
        }

        public void CreateGUI()
        {
            if (objectType == null)
            {
                Close();
                return;
            }

            var root = PreparePopup("lightside-selector", "lightside-object-selector");
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

            search = new InspectorSearchField();
            search.AddToClassList("lightside-selector__search");
            search.RegisterValueChangedCallback(_ => ScheduleSearch());
            root.Add(search);

            list = new ListView
            {
                itemsSource = rows,
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                horizontalScrollingEnabled = false,
                makeItem = () => new RowElement(this),
                bindItem = (element, index) =>
                    ((RowElement)element).Bind(rows[index], index),
            };
            list.AddToClassList("lightside-selector__content");
            list.AddToClassList("lightside-object-selector__list");
            root.Add(list);

            status = new Label();
            status.AddToClassList("lightside-object-selector__status");
            root.Add(status);

            displayedHeight = TargetHeight();
            FitHeightToContent(() => displayedHeight, list.Q<ScrollView>());
            RefreshQuery();
            search.Focus();
        }

        private float TargetHeight() => Mathf.Max(MinHeight,
            rows.Count * RowHeight + SearchHeight + StatusHeight + PopupChromeHeight);

        private void FitRows()
        {
            displayedHeight = TargetHeight();
            RequestFit();
        }

        private static object RowIdentity(in Row row) => row.Kind switch
        {
            RowKind.Group => row.Group.key,
            RowKind.None => noneIdentity,
            _ => row.Entry,
        };

        private void BeginRowSlides()
        {
            slideSnapshot.Clear();
            for (var i = 0; i < rows.Count; i++)
                slideSnapshot[RowIdentity(rows[i])] = i;
        }

        /// <summary>
        /// Slides every visible row from its pre-rebuild slot into the new one; rows whose slot
        /// did not move stay still. A cell rebound to another row mid-slide leaves the motion.
        /// </summary>
        private void PlayRowSlides()
        {
            slides.Clear();
            InspectorMotion.CompleteLayout(rootVisualElement.panel);
            list.Query<RowElement>().ForEach(cell =>
            {
                var index = cell.Index;
                if ((uint)index >= (uint)rows.Count) return;
                var identity = RowIdentity(rows[index]);
                if (!slideSnapshot.TryGetValue(identity, out var previous)) return;
                var delta = (previous - index) * RowHeight;
                if (Mathf.Abs(delta) >= 1f) slides.Add((cell, identity, delta));
            });
            slideSnapshot.Clear();
            if (slides.Count == 0) return;
            slideTicker.Play(rootVisualElement, eased =>
            {
                var remaining = 1f - eased;
                for (var i = 0; i < slides.Count; i++)
                {
                    var (cell, identity, delta) = slides[i];
                    var index = cell.Index;
                    if ((uint)index >= (uint)rows.Count ||
                        !Equals(RowIdentity(rows[index]), identity))
                        continue;
                    cell.style.translate = new Translate(0f, delta * remaining, 0f);
                }
                if (eased >= 1f) slides.Clear();
            });
        }

        protected override void OnDisable()
        {
            pendingSearch?.Pause();
            pendingProcessing?.Pause();
            pendingPresentation?.Pause();
            pendingContinuation?.Pause();
            CancelRequest();
            base.OnDisable();
        }

        private void OnProjectChange() => ScheduleSearch();

        private void OnHierarchyChange()
        {
            if (allowSceneObjects) ScheduleSearch();
        }

        private static void ClearCatalogs()
        {
            catalogs.Clear();
            scriptPaths = null;
        }

        private void ScheduleSearch()
        {
            pendingSearch?.Pause();
            pendingSearch = search.schedule.Execute(RefreshQuery).StartingIn(120);
        }

        private void RefreshQuery()
        {
            pendingSearch = null;
            var query = search?.value?.Trim() ?? string.Empty;
            if (query.Length == 0 && catalogs.TryGetValue(objectType, out var catalog))
            {
                CancelRequest();
                SetEntries(catalog.Entries, query, catalog);
                return;
            }
            StartRequest(query);
        }

        private void StartRequest(string query)
        {
            CancelRequest();
            receivedIds.Clear();
            pendingItems.Clear();
            entries.Clear();
            requestCompleted = false;
            finalRequest = false;
            requestQuery = query;
            requestAlternate = KeyboardLayoutSearch.Alternate(query);
            presentedEntryCount = 0;
            showTypeLabels = false;
            requestGroupByType = ShouldGroupByType();
            browseCatalog = query.Length == 0
                ? new Catalog(Array.Empty<Entry>(), new GroupNode { key = string.Empty })
                : null;
            rows.Clear();
            if (showNone)
                rows.Add(new Row(RowKind.None, null, null, noneLabel, 0, false));
            activeIndex = rows.Count == 0 ? -1 : 0;
            list?.Rebuild();
            status.text = "Searching project…";

            var generation = ++requestGeneration;
            StartSearch(generation, requestAlternate == requestQuery);
        }

        private void StartSearch(int generation, bool final)
        {
            if (generation != requestGeneration) return;
            finalRequest = final;
            requestCompleted = false;
            var provider = SearchService.GetProvider("adb") ??
                           throw new InvalidOperationException(
                               "Unity's Asset Database search provider is unavailable.");
            var flags = SearchFlags.Packages | SearchFlags.WantsMore;
#if !UNITY_6000_0_OR_NEWER
            flags |= SearchFlags.FirstBatchAsync;
#endif
            var searchText = AssetQuery();
            var query = final && requestAlternate != requestQuery
                ? requestAlternate
                : requestQuery;
            if (query.Length != 0)
                searchText += " " + query;
            var context = SearchService.CreateContext(
                new[] { provider }, searchText, flags);
            requestContext = context;
            SearchService.Request(context,
                (_, batch) =>
                {
                    if (generation != requestGeneration) return;
                    foreach (var item in batch)
                        pendingItems.Enqueue(item);
                    status.text = $"Searching project… " +
                                  $"{entries.Count + pendingItems.Count:N0}";
                    ScheduleProcessing(generation);
                },
                completedContext =>
                {
                    if (generation != requestGeneration) return;
                    requestContext = null;
                    completedContext.Dispose();
                    requestCompleted = true;
                    ScheduleProcessing(generation);
                }, flags);
        }

        private string AssetQuery()
        {
            if (!typeof(MonoBehaviour).IsAssignableFrom(objectType))
                return $"t:{objectType.Name}";
            scriptPaths ??= RuntimeScriptPaths();
            return scriptPaths.TryGetValue(objectType, out var path)
                ? $"ref:\"{path}\" t:Prefab"
                : $"t:{objectType.Name}";
        }

        private static Dictionary<Type, string> RuntimeScriptPaths()
        {
            var result = new Dictionary<Type, string>();
            var scripts = MonoImporter.GetAllRuntimeMonoScripts();
            for (var i = 0; i < scripts.Length; i++)
            {
                var type = scripts[i].GetClass();
                var path = AssetDatabase.GetAssetPath(scripts[i]);
                if (type != null && path.Length != 0 && !result.ContainsKey(type))
                    result.Add(type, path);
            }
            return result;
        }

        private void ScheduleProcessing(int generation)
        {
            if (pendingProcessing != null) return;
            pendingProcessing = rootVisualElement.schedule.Execute(
                () => ProcessPending(generation)).StartingIn(1);
        }

        private void ProcessPending(int generation)
        {
            pendingProcessing = null;
            if (generation != requestGeneration) return;
            var deadline = EditorApplication.timeSinceStartup + 0.004;
            var changed = false;
            do
            {
                Entry entry = null;
                SearchItem item;
                if (pendingItems.Count != 0)
                {
                    item = pendingItems.Dequeue();
                    if (!receivedIds.Add(item.id)) continue;
                    entry = CreateEntry(item);
                }
                else
                    break;
                if (entry != null &&
                    (entry.id == item.id || receivedIds.Add(entry.id)))
                {
                    AddProgressiveEntry(entry);
                    changed = true;
                }
            }
            while (EditorApplication.timeSinceStartup < deadline);

            if (changed) SchedulePresentation(generation);
            status.text = $"Searching project… " +
                          $"{entries.Count + pendingItems.Count:N0}";
            if (pendingItems.Count != 0)
            {
                ScheduleProcessing(generation);
                return;
            }
            if (!requestCompleted) return;
            if (!finalRequest)
            {
                PresentProgress(generation);
                finalRequest = true;
                requestCompleted = false;
                pendingContinuation = rootVisualElement.schedule.Execute(() =>
                {
                    pendingContinuation = null;
                    StartSearch(generation, true);
                }).StartingIn(ProgressiveRefreshMilliseconds);
                return;
            }
            PresentProgress(generation);
            Catalog catalog = null;
            if (requestQuery.Length == 0)
            {
                var result = entries.ToArray();
                catalog = new Catalog(result, browseCatalog.Root);
                StoreCatalog(catalog);
            }
            SetEntries(entries, requestQuery, catalog);
        }

        private void AddProgressiveEntry(Entry entry)
        {
            entries.Add(entry);
            if (requestQuery.Length == 0)
            {
                AddGroupEntry(browseCatalog.Root, entry, requestGroupByType);
                return;
            }
            if (entries.Count > 1 && entries[0].type != entry.type)
                showTypeLabels = true;
            var rowIndex = rows.Count;
            rows.Add(new Row(RowKind.Object, entry, null, null, 0, true));
            if (IsCurrent(entry)) activeIndex = rowIndex;
            else if (activeIndex < 0) activeIndex = 0;
        }

        private void SchedulePresentation(int generation)
        {
            if (presentedEntryCount == 0)
            {
                PresentProgress(generation);
                return;
            }
            if (pendingPresentation != null) return;
            pendingPresentation = rootVisualElement.schedule.Execute(() =>
            {
                pendingPresentation = null;
                PresentProgress(generation);
            }).StartingIn(ProgressiveRefreshMilliseconds);
        }

        private void PresentProgress(int generation)
        {
            pendingPresentation?.Pause();
            pendingPresentation = null;
            if (generation != requestGeneration ||
                presentedEntryCount == entries.Count) return;
            presentedEntryCount = entries.Count;
            if (requestQuery.Length == 0)
            {
                BuildBrowseRows(browseCatalog);
                return;
            }
            list.RefreshItems();
            FitRows();
        }

        private void CancelRequest()
        {
            requestGeneration++;
            pendingProcessing?.Pause();
            pendingProcessing = null;
            pendingPresentation?.Pause();
            pendingPresentation = null;
            pendingContinuation?.Pause();
            pendingContinuation = null;
            pendingItems.Clear();
            requestCompleted = false;
            requestContext?.Dispose();
            requestContext = null;
        }

        private Entry CreateEntry(SearchItem item)
        {
            if (!GlobalObjectId.TryParse(item.id, out var id))
                return null;
            var path = AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString());
            if (string.IsNullOrEmpty(path))
                return null;
            var type = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                ? AssetDatabase.GetTypeFromPathAndFileID(
                    path, (long)id.targetObjectId)
                : null;
            type ??= AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null)
                return null;
            var componentContainer = type == typeof(GameObject) &&
                                     typeof(Component).IsAssignableFrom(objectType);
            var importer = typeof(AssetImporter).IsAssignableFrom(objectType);
            if (!objectType.IsAssignableFrom(type) &&
                !componentContainer && !importer)
            {
                var asset = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (asset == null || !objectType.IsInstanceOfType(asset))
                    return null;
                type = asset.GetType();
            }
            if (componentContainer)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var component = prefab != null ? prefab.GetComponent(objectType) : null;
                if (component == null)
                    return null;
                id = GlobalObjectId.GetGlobalObjectIdSlow(component);
                type = component.GetType();
            }
            else if (importer)
                type = objectType;
            var isFolder = type == typeof(DefaultAsset) && AssetDatabase.IsValidFolder(path);
            var groupPath = isFolder ? path : ParentPath(path);
            return new Entry
            {
                searchItem = item,
                assetId = id,
                hasAssetId = true,
                id = id.ToString(),
                path = path,
                groupPath = groupPath,
                sortName = Path.GetFileNameWithoutExtension(path),
                type = type,
                rank = item.score,
                isFolder = isFolder,
            };
        }

        private void StoreCatalog(Catalog catalog)
        {
            if (catalogs.Count >= CatalogCapacity && !catalogs.ContainsKey(objectType))
                catalogs.Clear();
            catalogs[objectType] = catalog;
        }

        private void SetEntries(IReadOnlyList<Entry> projectEntries, string query,
            Catalog catalog = null)
        {
            if (!ReferenceEquals(projectEntries, entries))
            {
                entries.Clear();
                for (var i = 0; i < projectEntries.Count; i++)
                    entries.Add(projectEntries[i]);
            }
            if (allowSceneObjects) AddSceneEntries(entries, query);
            if (query.Length == 0)
            {
                browseCatalog = allowSceneObjects ? null : catalog;
                BuildBrowseRows(browseCatalog);
            }
            else
            {
                browseCatalog = null;
                BuildSearchRows();
            }
            status.text = entries.Count == 0
                ? objectType == typeof(Object)
                    ? "No objects found."
                    : $"No {ObjectNames.NicifyVariableName(objectType.Name)} objects found."
                : $"{entries.Count:N0} object{(entries.Count == 1 ? string.Empty : "s")}";
        }

        private void BuildBrowseRows(Catalog catalog = null)
        {
            rows.Clear();
            GroupNode root;
            if (catalog == null)
                root = BuildGroups(entries);
            else
            {
                root = catalog.Root;
                showTypeLabels = false;
            }
            SortGroup(root);

            if (showNone)
                rows.Add(new Row(RowKind.None, null, null, noneLabel, 0, false));
            for (var i = 0; i < root.entries.Count; i++)
                rows.Add(new Row(RowKind.Object, root.entries[i], null,
                    null, 0, false));
            if (root.entries.Count == 0 && root.children.Count == 1 &&
                !root.children[0].typeRoot)
                AddGroupContents(root.children[0], 0);
            else
                for (var i = 0; i < root.children.Count; i++)
                    AddGroupRows(root.children[i], 0);
            activeIndex = FindSelectedRow();
            list.Rebuild();
            FitRows();
        }

        private void BuildSearchRows()
        {
            rows.Clear();
            if (showNone)
                rows.Add(new Row(RowKind.None, null, null, noneLabel, 0, false));
            entries.Sort((left, right) =>
            {
                var rank = left.rank.CompareTo(right.rank);
                return rank != 0 ? rank : string.Compare(left.path, right.path,
                    StringComparison.OrdinalIgnoreCase);
            });
            showTypeLabels = HasSeveralTypes(entries);
            for (var i = 0; i < entries.Count; i++)
                rows.Add(new Row(RowKind.Object, entries[i], null,
                    null, 0, true));
            activeIndex = FindSelectedRow();
            list.Rebuild();
            FitRows();
        }


        private GroupNode BuildGroups(IReadOnlyList<Entry> source)
        {
            var root = new GroupNode { key = string.Empty };
            var groupByType = ShouldGroupByType();
            for (var i = 0; i < source.Count; i++)
                AddGroupEntry(root, source[i], groupByType);
            SortAndCount(root);
            showTypeLabels = false;
            return root;
        }

        private static void AddGroupEntry(GroupNode root, Entry entry, bool groupByType)
        {
            var node = root;
            if (groupByType)
            {
                var typeName = entry.type.FullName ?? entry.type.Name;
                var key = "$type:" + typeName;
                node = GetOrAddGroup(root, typeName, key,
                    ObjectNames.NicifyVariableName(entry.type.Name), true, entry.type);
            }
            node = AddPathGroups(node, entry.groupPath);
            node.entries.Add(entry);
            node.sorted = false;
            for (var current = node; current != null; current = current.parent)
                current.count++;
        }

        private bool ShouldGroupByType()
            => objectType == typeof(Object) || objectType == typeof(ScriptableObject) ||
               objectType.IsAbstract;

        private static GroupNode AddPathGroups(GroupNode node, string path)
        {
            var start = 0;
            while (start < path.Length)
            {
                while (start < path.Length && path[start] == '/') start++;
                if (start >= path.Length) break;
                var end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;
                var part = path.Substring(start, end - start);
                var key = node.key.Length == 0 ? part : node.key + "/" + part;
                node = GetOrAddGroup(node, part, key, part, false, null);
                start = end + 1;
            }
            return node;
        }

        private static GroupNode GetOrAddGroup(GroupNode parent, string lookup,
            string key, string name, bool typeRoot, Type type)
        {
            if (parent.childMap.TryGetValue(lookup, out var existing)) return existing;
            var group = new GroupNode
            {
                name = name,
                key = key,
                typeRoot = typeRoot,
                type = type,
                icon = typeRoot
                    ? EditorGUIUtility.ObjectContent(null, type).image
                    : folderIcon,
                parent = parent,
            };
            parent.childMap.Add(lookup, group);
            parent.children.Add(group);
            parent.sorted = false;
            return group;
        }

        private static int SortAndCount(GroupNode node)
        {
            SortGroup(node);
            var count = node.entries.Count;
            for (var i = 0; i < node.children.Count; i++)
                count += SortAndCount(node.children[i]);
            node.count = count;
            return count;
        }

        private static void SortGroup(GroupNode node)
        {
            if (node.sorted) return;
            node.children.Sort((left, right) =>
            {
                var order = GroupOrder(left).CompareTo(GroupOrder(right));
                return order != 0 ? order : string.Compare(
                    left.name, right.name, StringComparison.OrdinalIgnoreCase);
            });
            node.entries.Sort((left, right) =>
            {
                var name = string.Compare(left.sortName, right.sortName,
                    StringComparison.OrdinalIgnoreCase);
                return name != 0 ? name : string.CompareOrdinal(left.id, right.id);
            });
            node.sorted = true;
        }

        private static int GroupOrder(GroupNode group)
        {
            if (group.typeRoot) return 0;
            if (string.Equals(group.name, "Assets", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(group.name, "Packages", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(group.name, "Scenes", StringComparison.OrdinalIgnoreCase)) return 3;
            return 4;
        }

        private void AddGroupRows(GroupNode group, int depth, string prefix = null)
        {
            SortGroup(group);
            var end = CompressedEnd(group, out var label);
            SortGroup(end);
            if (!string.IsNullOrEmpty(prefix)) label = prefix + label;
            if (!end.typeRoot && !HasEntries(group, end))
            {
                for (var i = 0; i < end.children.Count; i++)
                    AddGroupRows(end.children[i], depth, label + "/");
                return;
            }
            rows.Add(new Row(RowKind.Group, null, end, label,
                depth, false, group.count));
            if (!expandedGroups.Contains(end.key)) return;
            AddGroupContents(group, depth + 1);
        }

        private void AddGroupContents(GroupNode group, int depth)
        {
            SortGroup(group);
            var end = CompressedEnd(group, out _);
            SortGroup(end);
            for (var node = group;; node = node.children[0])
            {
                SortGroup(node);
                for (var i = 0; i < node.entries.Count; i++)
                    rows.Add(new Row(RowKind.Object, node.entries[i], null,
                        null, depth, false));
                if (node == end) break;
            }
            for (var i = 0; i < end.children.Count; i++)
                AddGroupRows(end.children[i], depth);
        }

        private static GroupNode CompressedEnd(GroupNode start, out string label)
        {
            var end = start;
            label = start.name;
            while (!end.typeRoot && HasOnlyFolderEntries(end) && end.children.Count == 1 &&
                   !end.children[0].typeRoot)
            {
                end = end.children[0];
                label += "/" + end.name;
            }
            return end;
        }

        private static bool HasEntries(GroupNode start, GroupNode end)
        {
            for (var node = start;; node = node.children[0])
            {
                if (node.entries.Count != 0) return true;
                if (node == end) return false;
            }
        }

        private static bool HasOnlyFolderEntries(GroupNode group)
        {
            for (var i = 0; i < group.entries.Count; i++)
                if (!group.entries[i].isFolder) return false;
            return true;
        }

        private void AddSceneEntries(List<Entry> target, string query)
        {
            var alternate = KeyboardLayoutSearch.Alternate(query);
            var hierarchyPaths = new Dictionary<Transform, string>();
            for (var sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded) continue;
                var roots = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                {
                    if (objectType == typeof(GameObject))
                    {
                        var transforms = roots[rootIndex].GetComponentsInChildren<Transform>(true);
                        for (var i = 0; i < transforms.Length; i++)
                            AddSceneEntry(target, transforms[i].gameObject, scene, query,
                                alternate, false, hierarchyPaths);
                    }
                    else if (typeof(Component).IsAssignableFrom(objectType))
                    {
                        var components = roots[rootIndex].GetComponentsInChildren(objectType, true);
                        for (var i = 0; i < components.Length; i++)
                            AddSceneEntry(target, components[i], scene, query,
                                alternate, true, hierarchyPaths);
                    }
                }
            }
        }

        private static void AddSceneEntry(List<Entry> target, Object value,
            Scene scene, string query, string alternate, bool groupUnderObject,
            Dictionary<Transform, string> hierarchyPaths)
        {
            var gameObject = value is Component component ? component.gameObject : (GameObject)value;
            var hierarchy = HierarchyPath(gameObject.transform, hierarchyPaths);
            var path = $"Scenes/{scene.name}/{hierarchy}";
            var groupPath = groupUnderObject ? path : ParentPath(path);
            var type = value.GetType();
            if (query.Length != 0 && !Matches(value.name, query, alternate) &&
                !Matches(path, query, alternate) &&
                !Matches(type.Name, query, alternate))
                return;
            target.Add(new Entry
            {
                sceneObject = value,
                id = "scene:" + ObjectRefCompat.Serialize(value),
                path = path,
                groupPath = groupPath,
                sortName = value.name,
                type = type,
                rank = int.MaxValue,
            });
        }

        private static bool Matches(string value, string query, string alternate)
            => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
               alternate != query &&
               value.IndexOf(alternate, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string HierarchyPath(Transform transform,
            Dictionary<Transform, string> cache)
        {
            if (cache.TryGetValue(transform, out var path)) return path;
            path = transform.parent == null
                ? transform.name
                : HierarchyPath(transform.parent, cache) + "/" + transform.name;
            cache.Add(transform, path);
            return path;
        }

        private static string ParentPath(string path)
        {
            var separator = path.LastIndexOf('/');
            return separator < 0 ? string.Empty : path.Substring(0, separator);
        }

        private static bool HasSeveralTypes(IReadOnlyList<Entry> source)
        {
            if (source.Count < 2) return false;
            var first = source[0].type;
            for (var i = 1; i < source.Count; i++)
                if (source[i].type != first) return true;
            return false;
        }

        private Texture GetIcon(Entry entry)
        {
            if (entry.icon != null) return entry.icon;
            if (entry.sceneObject != null)
                return entry.icon = EditorGUIUtility.ObjectContent(
                    entry.sceneObject, entry.type).image;
            return entry.icon = AssetDatabase.GetCachedIcon(entry.path) ??
                                EditorGUIUtility.ObjectContent(null, entry.type).image;
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                if (!string.IsNullOrEmpty(search.value))
                    search.value = string.Empty;
                else
                    CloseToOwner();
                evt.StopPropagation();
                return;
            }
            if (evt.keyCode == KeyCode.DownArrow)
                MoveActive(1);
            else if (evt.keyCode == KeyCode.UpArrow)
                MoveActive(-1);
            else if (evt.keyCode == KeyCode.Home)
                SetActive(rows.Count == 0 ? -1 : 0, true);
            else if (evt.keyCode == KeyCode.End)
                SetActive(rows.Count - 1, true);
            else if (evt.keyCode == KeyCode.PageDown)
                MoveActive(8);
            else if (evt.keyCode == KeyCode.PageUp)
                MoveActive(-8);
            else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                Activate(activeIndex, evt.altKey);
            else
                return;
            evt.StopPropagation();
        }

        private void MoveActive(int delta)
        {
            if (rows.Count == 0) return;
            var next = activeIndex < 0
                ? delta > 0 ? 0 : rows.Count - 1
                : Mathf.Clamp(activeIndex + delta, 0, rows.Count - 1);
            SetActive(next, true);
        }

        private void SetActive(int index, bool reveal = false)
        {
            if ((uint)index >= (uint)rows.Count) return;
            if (activeIndex == index)
            {
                if (reveal) list.ScrollToItem(index);
                return;
            }
            activeIndex = index;
            list.RefreshItems();
            if (reveal) list.ScrollToItem(index);
        }

        private void Activate(int index, bool ping)
        {
            if ((uint)index >= (uint)rows.Count) return;
            var row = rows[index];
            if (row.Kind == RowKind.Group)
            {
                var groupKey = row.Group.key;
                BeginRowSlides();
                if (!expandedGroups.Add(groupKey))
                    expandedGroups.Remove(groupKey);
                BuildBrowseRows(browseCatalog);
                PlayRowSlides();
                for (var i = 0; i < rows.Count; i++)
                    if (rows[i].Kind == RowKind.Group && rows[i].Group.key == groupKey)
                    {
                        SetActive(i, true);
                        break;
                    }
                return;
            }

            if (ping)
            {
                if (row.Kind == RowKind.Object)
                    EditorGUIUtility.PingObject(Resolve(row.Entry));
                return;
            }

            var value = row.Kind == RowKind.None ? null : Resolve(row.Entry);
            selected(value);
            currentValue = value;
            hasCurrentAssetId = value != null && EditorUtility.IsPersistent(value);
            if (hasCurrentAssetId)
            {
                currentIsMainAsset = AssetDatabase.IsMainAsset(value);
                currentAssetId = GlobalObjectId.GetGlobalObjectIdSlow(value);
                currentAssetPath = AssetDatabase.GetAssetPath(value);
            }
            else
            {
                currentIsMainAsset = false;
                currentAssetPath = null;
            }
            CloseToOwner();
        }

        private Object Resolve(Entry entry)
        {
            if (entry.sceneObject != null)
            {
                if (objectType.IsInstanceOfType(entry.sceneObject)) return entry.sceneObject;
                throw new InvalidOperationException(
                    $"Scene object '{entry.path}' is no longer assignable to {objectType.FullName}.");
            }
            if (typeof(AssetImporter).IsAssignableFrom(objectType))
            {
                var importer = AssetImporter.GetAtPath(entry.path);
                if (importer != null && objectType.IsInstanceOfType(importer)) return importer;
            }
            var value = entry.hasAssetId
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(entry.assetId)
                : null;
            if (value != null && objectType.IsInstanceOfType(value)) return value;
            if (value is GameObject gameObject && typeof(Component).IsAssignableFrom(objectType))
            {
                var component = gameObject.GetComponent(objectType);
                if (component != null) return component;
            }
            value = AssetDatabase.LoadAssetAtPath(entry.path, objectType);
            if (value != null) return value;
            catalogs.Remove(objectType);
            throw new InvalidOperationException(
                $"Indexed object '{entry.path}' cannot be resolved as {objectType.FullName}.");
        }

        private int FindSelectedRow()
        {
            for (var i = 0; i < rows.Count; i++)
                if (IsSelected(rows[i])) return i;
            return rows.Count == 0 ? -1 : 0;
        }

        private bool IsSelected(in Row row)
        {
            if (row.Kind == RowKind.None) return currentValue == null;
            return row.Kind == RowKind.Object && IsCurrent(row.Entry);
        }

        private bool IsCurrent(Entry entry)
        {
            if (entry.sceneObject != null) return entry.sceneObject == currentValue;
            return hasCurrentAssetId && (entry.hasAssetId
                ? entry.assetId.Equals(currentAssetId)
                : currentIsMainAsset && string.Equals(entry.path, currentAssetPath,
                    StringComparison.OrdinalIgnoreCase));
        }

    }

    /// <summary>Object field whose picker uses the shared project and scene object selector.</summary>
    public sealed class InspectorObjectField : ObjectField
    {
        private readonly VisualElement selector;
        private readonly VisualElement input;

        /// <summary>Creates a field restricted to the supplied Unity object type.</summary>
        public InspectorObjectField(string label, Type objectType,
            bool allowSceneObjects = false) : base(label)
        {
            if (objectType == null) throw new ArgumentNullException(nameof(objectType));
            if (!typeof(Object).IsAssignableFrom(objectType))
                throw new ArgumentException(
                    $"{objectType.FullName} is not a Unity object type.", nameof(objectType));
            this.objectType = objectType;
            this.allowSceneObjects = allowSceneObjects;
            selector = this.Q<VisualElement>(className: ObjectField.selectorUssClassName) ??
                       throw new InvalidOperationException("Object field selector control is missing.");
            input = this.Q<VisualElement>(className: ObjectField.inputUssClassName) ??
                    throw new InvalidOperationException("Object field input control is missing.");
            selector.AddToClassList("lightside-object-field-selector");
            var ring = new VisualElement { pickingMode = PickingMode.Ignore };
            ring.AddToClassList("lightside-object-field-selector__ring");
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("lightside-object-field-selector__dot");
            ring.Add(dot);
            selector.Add(ring);
            RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button != 0 || evt.target is not VisualElement target ||
                target != selector && !selector.Contains(target)) return;
            Consume(evt);
            ShowSelector();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Space && evt.keyCode != KeyCode.Return &&
                evt.keyCode != KeyCode.KeypadEnter ||
                evt.target is not VisualElement target ||
                !target.ClassListContains(ObjectField.objectUssClassName)) return;
            Consume(evt);
            ShowSelector();
        }

        private void ShowSelector() => ObjectSelector.Show(
            input.worldBound, objectType, value, CommitSelection,
            true, allowSceneObjects);

        private void CommitSelection(Object selected)
        {
            if (!showMixedValue || value != selected)
            {
                value = selected;
                return;
            }

            using var evt = ChangeEvent<Object>.GetPooled(value, selected);
            evt.target = this;
            SendEvent(evt);
        }

        private static void Consume(EventBase evt)
        {
#if !UNITY_2023_2_OR_NEWER
            evt.PreventDefault();
#endif
            evt.StopImmediatePropagation();
        }
    }

}
