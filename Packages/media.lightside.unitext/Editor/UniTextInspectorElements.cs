using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal static class UniTextInspectorTheme
    {
        private const string StyleSheetPath = "UniText/UniTextInspector";
        private const string RootClass = "unitext-inspector";
        private const string AppliedClass = "unitext-inspector-styles--applied";
        private static StyleSheet styleSheet;

        public static VisualElement CreateRoot()
        {
            var root = InspectorVisuals.CreateRoot();
            root.name = RootClass;
            Initialize(root);
            return root;
        }

        public static void Initialize(VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            InspectorVisuals.Attach(root);
            if (!root.ClassListContains(RootClass))
            {
                root.AddToClassList(RootClass);
                root.RegisterCallback<AttachToPanelEvent>(OnAttached);
            }
            ApplySkin(root);
            if (root.panel != null) Apply(root);
        }

        private static void OnAttached(AttachToPanelEvent evt)
        {
            var root = (VisualElement)evt.currentTarget;
            Apply(root);
        }

        private static VisualElement StyleRoot(VisualElement element)
        {
            var root = element;
            for (var current = element.parent; current != null; current = current.parent)
                if (current.ClassListContains(RootClass)) root = current;
            return root;
        }

        private static void Apply(VisualElement element)
        {
            var root = StyleRoot(element);
            var panelRoot = element.panel.visualTree;
            if (panelRoot != root && panelRoot.ClassListContains(AppliedClass))
            {
                panelRoot.styleSheets.Remove(SharedStyleSheet);
                panelRoot.RemoveFromClassList(AppliedClass);
            }
            if (root.ClassListContains(AppliedClass)) return;
            root.styleSheets.Add(SharedStyleSheet);
            root.AddToClassList(AppliedClass);
        }

        private static StyleSheet SharedStyleSheet =>
            styleSheet ??= Resources.Load<StyleSheet>(StyleSheetPath)
                           ?? throw new InvalidOperationException(
                               $"Required inspector stylesheet '{StyleSheetPath}' is missing.");

        public static void ApplySkin(VisualElement root)
        {
            root.EnableInClassList("unitext-inspector--dark", EditorGUIUtility.isProSkin);
            root.EnableInClassList("unitext-inspector--light", !EditorGUIUtility.isProSkin);
        }

    }

}
