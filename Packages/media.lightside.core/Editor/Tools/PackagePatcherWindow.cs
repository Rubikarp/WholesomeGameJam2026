using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Applies LightSide hotfix archives to the installed packages: drop or pick one or more patch
    /// zips, review where each one lands, and apply them in one click. Results survive the script
    /// reload the applied files trigger.
    /// </summary>
    internal sealed class PackagePatcherWindow : EditorWindow
    {
        [Serializable]
        private class Row
        {
            public string path;
            public bool settled;
            public bool failed;
            public string title;
            public string detail;
        }

        [SerializeField] private List<Row> rows = new();

        private readonly Dictionary<string, PackagePatch> patches =
            new(StringComparer.OrdinalIgnoreCase);
        private InspectorScrollStack list;
        private Button apply;

        [MenuItem(LightSideMenu.Tools.PackagePatcher, false, 101)]
        public static void ShowWindow()
        {
            var window = GetWindow<PackagePatcherWindow>("Package Patcher");
            window.minSize = new Vector2(360, 320);
        }

        /// <summary>Builds the retained-mode patch workspace.</summary>
        public void CreateGUI()
        {
            var root = InspectorVisuals.CreateWindowRoot(rootVisualElement);
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            var drop = new Label("Drop patch archives here");
            drop.AddToClassList("lightside-drop-zone");
            drop.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.StopPropagation();
            });
            drop.RegisterCallback<DragPerformEvent>(evt =>
            {
                DragAndDrop.AcceptDrag();
                var paths = DragAndDrop.paths;
                if (paths != null)
                    foreach (var path in paths)
                        if (Path.IsPathRooted(path) &&
                            path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                            AddPatch(path);
                Render();
                evt.StopPropagation();
            });
            root.Add(drop);

            var browse = new Button(Browse) { text = "Add Patch Archive…" };
            root.Add(browse);

            list = InspectorVisuals.CreateScrollStack();
            list.style.flexGrow = 1f;
            root.Add(list);

            apply = new Button(ApplyAll);
            apply.AddToClassList("lightside-primary-action");
            root.Add(apply);

            foreach (var row in rows)
                if (!row.settled)
                    patches[row.path] = PackagePatch.Load(row.path);
            Render();
        }

        private void Browse()
        {
            var path = EditorUtility.OpenFilePanelWithFilters("Add Patch Archive", "",
                new[] { "Patch archive", "zip" });
            if (string.IsNullOrEmpty(path)) return;
            AddPatch(path);
            Render();
        }

        private void AddPatch(string path)
        {
            path = Path.GetFullPath(path);
            if (rows.Exists(row =>
                    string.Equals(row.path, path, StringComparison.OrdinalIgnoreCase))) return;
            rows.Add(new Row { path = path });
            patches[path] = PackagePatch.Load(path);
        }

        private void Remove(Row row)
        {
            rows.Remove(row);
            patches.Remove(row.path);
            Render();
        }

        private void ApplyAll()
        {
            var applied = 0;
            foreach (var row in rows)
            {
                if (row.settled || !patches.TryGetValue(row.path, out var patch)) continue;
                if (patch.Status != PackagePatchStatus.Ready) continue;
                try
                {
                    patch.Apply();
                    applied++;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                row.settled = true;
                row.failed = patch.Status == PackagePatchStatus.Failed;
                row.title = Title(patch);
                row.detail = patch.Detail;
            }
            Render();
            if (applied > 0) AssetDatabase.Refresh();
        }

        private static string Title(PackagePatch patch) => patch.Target != null
            ? $"{patch.Target.displayName} {patch.Target.version}"
            : Path.GetFileName(patch.ZipPath);

        private void Render()
        {
            InspectorVisuals.ClearContent(list);
            var ready = 0;
            foreach (var row in rows)
            {
                var patch = row.settled ? null : patches[row.path];
                var card = InspectorVisuals.CreateCard();
                var header = InspectorVisuals.CreateRow();
                var title = new Label(row.settled ? row.title : Title(patch));
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.flexGrow = 1f;
                var captured = row;
                var remove = new Button(() => Remove(captured)) { text = "✕" };
                remove.style.width = 24f;
                header.Add(title);
                header.Add(remove);
                card.Add(header);
                card.Add(Note(Path.GetFileName(row.path)));

                if (row.settled)
                    card.Add(WrappedStatus(row.detail,
                        row.failed ? EditorResources.StatusError : EditorResources.StatusSuccess));
                else if (patch.Status == PackagePatchStatus.Ready)
                {
                    card.Add(WrappedStatus(patch.Detail, EditorResources.StatusSuccess));
                    card.Add(WindowNote(patch));
                    if (patch.CacheResident)
                        card.Add(WrappedStatus(
                            "Package cache copy — Package Manager restores the original files " +
                            "when it re-resolves the package (update, cleared Library). Enough " +
                            "to verify the fix; re-apply if that happens before the release.",
                            EditorResources.StatusWarning));
                    ready++;
                }
                else card.Add(WrappedStatus(patch.Detail, EditorResources.StatusError));
                list.Add(card);
            }
            apply.text = ready == 0 ? "Apply Patches"
                : ready == 1 ? "Apply 1 Patch" : $"Apply {ready} Patches";
            apply.SetEnabled(ready > 0);
        }

        private static Label WindowNote(PackagePatch patch) => Note(patch.WindowText != null
            ? $"Version window verified: safe for {patch.WindowText}."
            : "Version window not recorded in this archive — check the note that came with the patch.");

        private static Label WrappedStatus(string text, Color color)
        {
            var label = InspectorVisuals.CreateStatusLabel(text, color);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Label Note(string text)
        {
            var label = new Label(text);
            label.style.opacity = 0.6f;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
