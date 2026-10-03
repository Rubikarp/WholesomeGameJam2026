using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Ways the LightSide palette opens: the Shortcuts Manager binding, an entry in the Hierarchy and
    /// Project context menus, and secondary-click with Shift over the Hierarchy, Project or Scene
    /// view, which claims the click instead of the native menu. Unity's own menus stay untouched.
    /// </summary>
    [InitializeOnLoad]
    internal static class CommandEntryPoints
    {
        /// <summary>Fraction of the host window's height placing the palette clearly in its upper half, so it opens downward.</summary>
        private const float FallbackAnchorOffset = 0.25f;

        private static ScreenRect pointer;
        private static EditorWindow pointerWindow;

        private static bool claiming;
        private static CommandSurface claimSurface;
        private static Vector2 claimPosition;
        private static GameObject claimTarget;

        /// <summary>
        /// The hierarchy row callback carries an <c>EntityId</c> from 6000.4 and an instance id
        /// before it; a lambda takes the id type from the delegate it joins. Resolving the row to its
        /// object belongs behind <see cref="HoldsClaim"/> — the callback runs for every row of every
        /// repaint.
        /// </summary>
        static CommandEntryPoints()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += (entityId, rect) =>
            {
                SamplePointer(rect);
                TryClaim(CommandSurface.Hierarchy);
                if (HoldsClaim(CommandSurface.Hierarchy, rect))
                    claimTarget = ObjectRefCompat.Resolve(entityId) as GameObject;
            };
#else
            EditorApplication.hierarchyWindowItemOnGUI += (instanceId, rect) =>
            {
                SamplePointer(rect);
                TryClaim(CommandSurface.Hierarchy);
                if (HoldsClaim(CommandSurface.Hierarchy, rect))
                    claimTarget = ObjectRefCompat.Resolve(instanceId) as GameObject;
            };
#endif
            EditorApplication.projectWindowItemOnGUI += (_, rect) =>
            {
                SamplePointer(rect);
                TryClaim(CommandSurface.Project);
            };
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>Alt+L, rebindable in the Shortcuts Manager. Alt+Space is not available — Windows reserves it for the window menu.</summary>
        [Shortcut(LightSideMenu.Palette.ShortcutId, KeyCode.L, ShortcutModifiers.Alt)]
        private static void OpenGlobal() => Open(CommandSurface.Global, Anchor(), null);

        [MenuItem(LightSideMenu.Palette.Hierarchy, false, LightSideMenu.Palette.HierarchyPriority)]
        private static void OpenOverHierarchy(MenuCommand command)
            => Open(CommandSurface.Hierarchy, Anchor(), command.context as GameObject);

        [MenuItem(LightSideMenu.Palette.Project, false, LightSideMenu.Palette.ProjectPriority)]
        private static void OpenOverProject() => Open(CommandSurface.Project, Anchor(), null);

        /// <summary>
        /// The scene view has no per-item callback, so the click is claimed and the object under the
        /// pointer picked in one place. <see cref="Handles.BeginGUI"/> restores plain GUI space, which
        /// the screen conversion needs and the handles matrix in force here does not provide.
        /// </summary>
        private static void OnSceneGui(SceneView view)
        {
            var current = Event.current;
            if (current == null || current.type != EventType.ContextClick || !current.shift) return;

            Handles.BeginGUI();
            var anchor = ScreenRect.FromPanel(new Rect(current.mousePosition, Vector2.zero));
            Handles.EndGUI();

            var target = HandleUtility.PickGameObject(current.mousePosition, false);
            pointer = anchor;
            pointerWindow = view;
            current.Use();
            Open(CommandSurface.SceneView, anchor, target);
        }

        /// <summary>
        /// Claims the Shift-qualified secondary click, consuming it so the native menu stays closed.
        /// The palette opens after the pass that claimed it, by which time every row callback has run
        /// and <see cref="claimTarget"/> holds the row under the pointer — none, for a click that
        /// landed between rows or past the last one.
        /// </summary>
        private static void TryClaim(CommandSurface surface)
        {
            var current = Event.current;
            if (current == null || current.type != EventType.ContextClick || !current.shift) return;

            claiming = true;
            claimSurface = surface;
            claimPosition = current.mousePosition;
            claimTarget = null;
            var anchor = ScreenRect.FromPanel(new Rect(claimPosition, Vector2.zero));
            current.Use();

            EditorApplication.delayCall += () =>
            {
                claiming = false;
                LightSideCommands.Open(surface, anchor, claimTarget);
            };
        }

        /// <summary>Whether a row being visited is the one the pending claim was made over.</summary>
        private static bool HoldsClaim(CommandSurface surface, Rect rowRect)
            => claiming && claimSurface == surface && rowRect.Contains(claimPosition);

        /// <summary>Records where the pointer is on every click over an item, for the entries that open without one.</summary>
        private static void SamplePointer(Rect rowRect)
        {
            var current = Event.current;
            if (current == null) return;
            if (current.type != EventType.MouseDown && current.type != EventType.ContextClick) return;
            if (!rowRect.Contains(current.mousePosition)) return;

            pointer = ScreenRect.FromPanel(new Rect(current.mousePosition, Vector2.zero));
            pointerWindow = EditorWindow.mouseOverWindow;
        }

        /// <summary>
        /// The last click's position while the palette opens over the window that click landed in,
        /// so a palette reached through the context menu still meets the pointer; otherwise the upper
        /// area of the focused window.
        /// </summary>
        private static ScreenRect Anchor()
        {
            if (pointerWindow != null && pointerWindow == EditorWindow.focusedWindow) return pointer;

            var host = EditorWindow.focusedWindow;
            var area = host != null ? host.position : EditorGUIUtility.GetMainWindowPosition();
            return new ScreenRect(new Rect(
                area.center.x - LightSideCommands.MinimumWidth * 0.5f,
                area.y + area.height * FallbackAnchorOffset, LightSideCommands.MinimumWidth, 0f));
        }

        /// <summary>
        /// Opens after the event that asked for it has finished, so the palette never appears while
        /// the window that spawned it is still drawing. The anchor is captured beforehand: panel
        /// coordinates no longer convert once that pass has ended.
        /// </summary>
        private static void Open(CommandSurface surface, ScreenRect anchor, GameObject target)
            => EditorApplication.delayCall +=
                () => LightSideCommands.Open(surface, anchor, target);
    }
}
