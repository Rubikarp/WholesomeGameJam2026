using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide.SceneEditing
{
    /// <summary>
    /// Retained-mode command and formatting selector for an active SceneView text-edit session.
    /// Applicability comes from the same runtime capability owner as the player context menu.
    /// </summary>
    internal static class SceneTextContextMenu
    {
        internal static void Show(Rect anchor, UniTextBase target,
            UniTextEditable editable)
        {
            var caps = editable.EditorContextCapabilities;
            var items = new List<Selector.SelectorItem>();

            AddCommand(items, "Cut", "Cut", caps.CanCut);
            AddCommand(items, "Copy", "Copy", caps.CanCopy);
            AddCommand(items, "Paste", "Paste", caps.CanPaste);
            AddCommand(items, "Select All", "SelectAll", caps.CanSelectAll);
            AddFormat(items, "Bold", new BoldModifier(), "b", target, editable);
            AddFormat(items, "Italic", new ItalicModifier(), "i", target, editable);
            AddFormat(items, "Underline", new UnderlineModifier(), "u", target, editable);
            AddExistingStyles(items, target, editable);
            AddItem(items, "Clear Formatting", "Format",
                () => SceneTextFormatting.ClearFormatting(editable));
            AddMarkupVisibility(items, editable);
            AddItem(items, "Done Editing", string.Empty, SceneTextEditSession.End);

            Selector.Show(anchor, items.ToArray(), null,
                value => ((Action)value)());
        }

        private static void AddCommand(List<Selector.SelectorItem> items,
            string label, string command, bool available)
        {
            if (!available) return;
            AddItem(items, label, "Edit",
                () => SceneTextEditSession.HandleCommand(command));
        }

        private static void AddFormat(List<Selector.SelectorItem> items,
            string label, BaseModifier exemplar, string tag,
            UniTextBase target, UniTextEditable editable)
        {
            AddItem(items, label, "Format",
                () => SceneTextFormatting.ToggleModifier(
                    target, editable, exemplar, tag),
                selected: SceneTextFormatting.IsActive(editable, exemplar),
                accentKey: exemplar.GetType());
        }

        private static void AddExistingStyles(
            List<Selector.SelectorItem> items,
            UniTextBase target, UniTextEditable editable)
        {
            foreach (var (style, name) in UniTextBaseEditor.WrappableLiveStyles(target))
            {
                var captured = style;
                AddItem(items, name, "Format/Apply Existing",
                    () => SceneTextFormatting.ApplyPickedStyle(
                        target, editable, captured),
                    accentKey: style.Modifier.GetType());
            }
        }

        private static void AddMarkupVisibility(
            List<Selector.SelectorItem> items, UniTextEditable editable)
        {
            var current = editable.MarkupVisibility;
            foreach (MarkupVisibility mode in Enum.GetValues(typeof(MarkupVisibility)))
            {
                var captured = mode;
                AddItem(items, ObjectNames.NicifyVariableName(mode.ToString()), "Markup",
                    () =>
                    {
                        editable.MarkupVisibility = captured;
                        SceneView.RepaintAll();
                    },
                    selected: mode == current,
                    accentKey: mode);
            }
        }

        private static void AddItem(List<Selector.SelectorItem> items,
            string label, string group, Action action,
            bool selected = false, object accentKey = null)
        {
            items.Add(new Selector.SelectorItem
            {
                displayName = label,
                searchText = label,
                groupName = group,
                value = action,
                accentKey = accentKey,
                selected = selected,
            });
        }
    }
}
