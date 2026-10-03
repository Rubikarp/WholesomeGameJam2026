using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public class FlexibleImageMigrationWindow : EditorWindow
{
    private enum SortColumn { Selected, Type, Status, Size, Name, Path, Detail }
    private const int HeaderVersion = 2;
    private static bool warningAccepted;

    private readonly List<FlexibleImageMigration.MigrationResult> results = new();
    [SerializeField] private List<string> scopePaths = new();
    [SerializeField] private MultiColumnHeaderState headerState;
    [SerializeField] private int headerVersion;
    [NonSerialized] private MultiColumnHeader multiColumnHeader;
    private bool resizeHeader;
    private bool scanned;
    private Vector2 scroll;
    private SortColumn sortColumn = SortColumn.Path;
    private bool ascending = true;
    private GUIStyle centeredBox;

    [MenuItem("Tools/FlexibleUI/Migrate Flexible Image Version 2 Assets")]
    private static void ShowWindow()
    {
        if (!warningAccepted && !EditorUtility.DisplayDialog("Flexible Image Migration", "This rewrites and reimports Flexible Image scene, prefab, and preset YAML. Commit or back up the project first. Save unrelated work, but do not save affected v2 assets after installing v3. Runtime code and Animation Clip Adapters that activate unused modules may require those modules to be added manually.", "Continue", "Cancel")) return;
        warningAccepted = true;
        var window = GetWindow<FlexibleImageMigrationWindow>(true, "Flexible Image v2 Migration");
        window.minSize = new Vector2(900, 460);
        window.Show();
    }

    private void OnGUI()
    {
        DrawScopes();
        if (results.Count > 0 || scanned)
        {
            var ready = results.Where(result => result.type == FlexibleImageMigration.ResultType.Ready).ToArray();
            var summary = new List<string>();
            if (ready.Length > 0) summary.Add($"{ready.Length} migratable");
            var modular = results.Count(result => result.type == FlexibleImageMigration.ResultType.Version3);
            var binary = results.Count(result => result.type == FlexibleImageMigration.ResultType.Binary);
            var blocked = results.Count(result => result.type is FlexibleImageMigration.ResultType.Blocked or FlexibleImageMigration.ResultType.Failed);
            if (modular > 0) summary.Add($"{modular} already modular");
            if (binary > 0) summary.Add($"{binary} uninspectable binary");
            if (blocked > 0) summary.Add($"{blocked} blocked");
            EditorGUILayout.Space(3);
            var summaryRect = GUILayoutUtility.GetRect(0, 25, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(summaryRect, new Color(0, 0, 0, 0.35f));
            summaryRect.xMin += 6;
            EditorGUI.LabelField(summaryRect, summary.Count > 0 ? string.Join(", ", summary) : "No Flexible Image assets found.", EditorStyles.boldLabel);

            if (results.Count == 0) return;
            if (blocked > 0) EditorGUILayout.HelpBox("Resolve blocked results or scan a narrower scope before migrating.", MessageType.Error);
            DrawSelection();
            var headerRect = GUILayoutUtility.GetRect(0, MultiColumnHeader.DefaultGUI.defaultHeight, GUILayout.ExpandWidth(true));
            headerRect.width -= GUI.skin.verticalScrollbar.fixedWidth;
            var tableRect = GUILayoutUtility.GetRect(0, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var rows = SortedResults().ToArray();
            var rowHeight = EditorGUIUtility.singleLineHeight;
            var contentRect = new Rect(0, 0, tableRect.width - GUI.skin.verticalScrollbar.fixedWidth, Mathf.Max(tableRect.height, 4 + rows.Length * rowHeight));
            scroll = GUI.BeginScrollView(tableRect, scroll, contentRect, false, true);
            for (var i = 0; i < rows.Length; i++) DrawRow(rows[i], new Rect(0, 4 + i * rowHeight, contentRect.width, rowHeight));
            GUI.EndScrollView();
            DrawColumnLines(new Rect(tableRect.x, tableRect.y, contentRect.width, tableRect.height), tableRect.yMax);
            DrawHeader(headerRect);
        }
    }

    private void DrawScopes()
    {
        for (var i = scopePaths.Count - 1; i >= 0; i--)
        {
            var child = scopePaths[i];
            var parent = scopePaths.Where((_, index) => index != i).FirstOrDefault(path => Covers(path, child));
            if (parent == null) continue;
            scopePaths.RemoveAt(i);
            ShowNotification(new GUIContent(parent + " already includes " + child + ". Removed the redundant entry."));
        }
        Rect rect;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var scopeRects = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true)), 4, 4, 0, (0, 0, 9999), (72, 0, 0));
            rect = scopeRects[0];
            centeredBox ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter };
            GUI.Box(rect, "Drop folders here", centeredBox);
            EditorGUI.BeginDisabledGroup(scopePaths.Count == 0);
            if (GUI.Button(scopeRects[1], "Scan")) Scan();
            EditorGUI.EndDisabledGroup();
            if (rect.Contains(Event.current.mousePosition) && Event.current.type is EventType.DragUpdated or EventType.DragPerform)
            {
                var folders = DragAndDrop.objectReferences.Select(AssetDatabase.GetAssetPath).Where(path => path == "Assets" || path.StartsWith("Assets/", StringComparison.Ordinal)).Where(AssetDatabase.IsValidFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                DragAndDrop.visualMode = folders.Length > 0 ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (Event.current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    var invalid = folders.Length != DragAndDrop.objectReferences.Length;
                    var messages = new List<string>();
                    if (invalid) messages.Add("Only project folders can be added.");
                    foreach (var folder in folders)
                    {
                        var parent = scopePaths.FirstOrDefault(path => Covers(path, folder));
                        if (parent != null)
                        {
                            messages.Add(parent.Equals(folder, StringComparison.OrdinalIgnoreCase) ? folder + " is already in the scan list." : parent + " already includes " + folder + ".");
                            continue;
                        }
                        var removed = scopePaths.RemoveAll(path => Covers(folder, path));
                        scopePaths.Add(folder);
                        messages.Add(removed == 0 ? "Added " + folder + "." : $"Added {folder} and removed {removed} covered folder{(removed == 1 ? "" : "s")}.");
                    }
                    if (messages.Count > 0) ShowNotification(new GUIContent(string.Join("\n", messages)));
                }
                Event.current.Use();
            }
            if (scopePaths.Count > 0) EditorGUILayout.Space(4);
            for (var i = 0; i < scopePaths.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope(GUI.skin.box))
                {
                    EditorGUILayout.LabelField(scopePaths[i]);
                    if (!GUILayout.Button("X", EditorStyles.miniButton, GUILayout.Width(22))) continue;
                    scopePaths.RemoveAt(i--);
                }
            }
        }
    }

    private void Scan()
    {
        results.Clear();
        scanned = true;
        var paths = FlexibleImageMigration.FindPaths(scopePaths.ToArray());
        var interval = Math.Max(1, paths.Length / 200);
        try
        {
            for (var i = 0; i < paths.Length; i++)
            {
                if (i % interval == 0) EditorUtility.DisplayProgressBar("Scanning Flexible Image Assets", paths[i], (float)i / paths.Length);
                try
                {
                    var result = FlexibleImageMigration.Discover(paths[i]);
                    if (result == null) continue;
                    result.bytes = new FileInfo(paths[i]).Length;
                    results.Add(result);
                }
                catch (Exception exception)
                {
                    results.Add(new FlexibleImageMigration.MigrationResult { assetPath = AssetPath(paths[i]), fullPath = paths[i], assetType = TypeOf(paths[i]), type = FlexibleImageMigration.ResultType.Failed, detail = exception.Message });
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void DrawSelection()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Selection", GUILayout.Width(58));
            if (GUILayout.Button("All", GUILayout.Width(44))) SetSelected(results.Where(Ready), true);
            if (GUILayout.Button("None", GUILayout.Width(44))) SetSelected(results.Where(Ready), false);
            if (GUILayout.Button("Invert", GUILayout.Width(50))) foreach (var result in results.Where(Ready)) result.selected = !result.selected;
            GUILayout.Space(8);
            TypeToggle("Scenes", FlexibleImageMigration.AssetType.Scene);
            TypeToggle("Prefabs", FlexibleImageMigration.AssetType.Prefab);
            TypeToggle("Presets", FlexibleImageMigration.AssetType.Preset);
            GUILayout.FlexibleSpace();
            var selected = results.Count(result => Ready(result) && result.selected);
            EditorGUI.BeginDisabledGroup(selected == 0 || results.Any(result => result.type is FlexibleImageMigration.ResultType.Blocked or FlexibleImageMigration.ResultType.Failed));
            if (GUILayout.Button($"Migrate Selected ({selected})", GUILayout.Width(170))) FlexibleImageMigration.Migrate(results);
            EditorGUI.EndDisabledGroup();
        }
    }

    private void TypeToggle(string label, FlexibleImageMigration.AssetType type)
    {
        var rows = results.Where(result => Ready(result) && result.assetType == type).ToArray();
        var selected = rows.Length > 0 && rows.All(result => result.selected);
        EditorGUI.BeginDisabledGroup(rows.Length == 0);
        var value = GUILayout.Toggle(selected, label, GUI.skin.button, GUILayout.Width(62));
        EditorGUI.EndDisabledGroup();
        if (value != selected) SetSelected(rows, value);
    }

    private void DrawHeader(Rect rect)
    {
        var header = GetHeader();
        rect.width -= 3;
        header.OnGUI(rect, 0);
        if (resizeHeader) { header.ResizeToFit(); resizeHeader = false; Repaint(); }
        if (header.sortedColumnIndex < 0) return;
        sortColumn = (SortColumn)header.sortedColumnIndex;
        ascending = header.IsSortedAscending(header.sortedColumnIndex);
    }

    private MultiColumnHeader GetHeader()
    {
        if (headerVersion != HeaderVersion || headerState?.columns == null || headerState.columns.Length != 7)
        {
            headerState = new MultiColumnHeaderState(new[]
            {
                Column("Sel", 35, 35, 80), Column("Type", 70, 70, 220), Column("Status", 80, 80, 220), Column("Size", 70, 70, 180),
                Column("Name", 160, 100, 320), Column("Path", 300, 180, 9999, true), Column("Details", 130, 100, 400)
            });
            headerVersion = HeaderVersion;
            multiColumnHeader = null;
            resizeHeader = true;
        }
        if (multiColumnHeader != null) return multiColumnHeader;
        multiColumnHeader = new MultiColumnHeader(headerState);
        multiColumnHeader.SetSorting((int)sortColumn, ascending);
        return multiColumnHeader;
    }

    private static MultiColumnHeaderState.Column Column(string name, float width, float minWidth, float maxWidth, bool autoResize = false) => new()
    {
        headerContent = new GUIContent(name), headerTextAlignment = TextAlignment.Left, sortingArrowAlignment = TextAlignment.Right,
        width = width, minWidth = minWidth, maxWidth = maxWidth, autoResize = autoResize, allowToggleVisibility = false, canSort = true
    };

    private void DrawRow(FlexibleImageMigration.MigrationResult result, Rect rect)
    {
        var columns = GetColumnRects(rect);
        for (var i = 0; i < columns.Length; i++) { columns[i].x += 4; columns[i].width -= 8; }
        var toggleRect = columns[0];
        toggleRect.x += (toggleRect.width - 16) / 2;
        toggleRect.width = 16;
        if (Ready(result)) result.selected = EditorGUI.Toggle(toggleRect, result.selected);
        EditorGUI.LabelField(columns[1], result.assetType.ToString());
        EditorGUI.LabelField(columns[2], result.type.ToString());
        EditorGUI.LabelField(columns[3], EditorUtility.FormatBytes(result.bytes));
        EditorGUI.LabelField(columns[4], Path.GetFileNameWithoutExtension(result.assetPath));
        if (GUI.Button(columns[5], result.assetPath, EditorStyles.label)) EditorUtility.RevealInFinder(result.fullPath);
        EditorGUI.LabelField(columns[6], result.detail, EditorStyles.miniLabel);
    }

    private void DrawColumnLines(Rect rect, float bottom)
    {
        var columns = GetColumnRects(rect);
        for (var i = 0; i < columns.Length - 1; i++)
            EditorGUI.DrawRect(new Rect(Mathf.Round(columns[i].xMax), rect.y, 1, bottom - rect.y), new Color(0, 0, 0, 0.2f));
    }

    private Rect[] GetColumnRects(Rect rect)
    {
        GetHeader();
        var columns = new Rect[headerState.columns.Length];
        var x = rect.x;
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i] = new Rect(x, rect.y, headerState.columns[i].width, rect.height);
            x += columns[i].width;
        }
        return columns;
    }

    private IEnumerable<FlexibleImageMigration.MigrationResult> SortedResults()
    {
        Func<FlexibleImageMigration.MigrationResult, object> key = sortColumn switch
        {
            SortColumn.Selected => result => Ready(result) && result.selected,
            SortColumn.Type => result => result.assetType,
            SortColumn.Status => result => result.type,
            SortColumn.Size => result => result.bytes,
            SortColumn.Name => result => Path.GetFileNameWithoutExtension(result.assetPath),
            SortColumn.Detail => result => result.detail,
            _ => result => result.assetPath
        };
        var sorted = ascending ? results.OrderBy(key) : results.OrderByDescending(key);
        return sorted.ThenBy(result => result.assetPath, StringComparer.OrdinalIgnoreCase);
    }

    private static bool Ready(FlexibleImageMigration.MigrationResult result) => result.type == FlexibleImageMigration.ResultType.Ready;
    private static bool Covers(string parent, string child) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    private static void SetSelected(IEnumerable<FlexibleImageMigration.MigrationResult> rows, bool selected) { foreach (var result in rows) result.selected = selected; }
    private static string AssetPath(string fullPath) => fullPath.Replace('\\', '/').Substring(Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/').Length + 1);
    private static FlexibleImageMigration.AssetType TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch { ".unity" => FlexibleImageMigration.AssetType.Scene, ".prefab" => FlexibleImageMigration.AssetType.Prefab, ".anim" => FlexibleImageMigration.AssetType.Animation, _ => FlexibleImageMigration.AssetType.Preset };
}
}
