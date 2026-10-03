using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Builds the shared prefab-override commands: Apply writes the override into the prefab the
    /// instance was created from, and its trailing control opens the rest of the prefab chain so the
    /// override can be written further up; Revert restores the value the prefab defines.
    /// </summary>
    internal static class PrefabOverrideMenu
    {
        /// <summary>
        /// Appends the prefab-override commands for <paramref name="binding"/> behind a separator,
        /// or nothing when the property carries no override.
        /// </summary>
        internal static void Append(DropdownMenu menu, SerializedPropertyBinding binding,
            Action changed = null)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (!binding.HasPrefabOverrides) return;
            menu.AppendSeparator(string.Empty);
            AppendApply(menu, binding, changed);
            menu.AppendAction("Revert", _ =>
            {
                binding.RevertPrefabOverrides();
                PrefabOverrideVisual.Invalidate();
                changed?.Invoke();
            });
        }

        private static void AppendApply(DropdownMenu menu, SerializedPropertyBinding binding,
            Action changed)
        {
            var targets = binding.GetPrefabApplyTargets();
            if (targets.Count == 0) return;
            var item = new Selector.SelectorItem { chip = Label(targets[0]) };
            if (targets.Count > 1)
                item.createAction = () => CreateChooseAction(binding, targets, changed);
            menu.AppendAction("Apply", _ => Apply(binding, targets[0], changed),
                _ => DropdownMenuAction.Status.Normal, item);
        }

        private static void Apply(SerializedPropertyBinding binding, PrefabApplyTarget target,
            Action changed)
        {
            binding.ApplyPrefabOverrides(target.AssetPath);
            PrefabOverrideVisual.Invalidate();
            changed?.Invoke();
        }

        private static VisualElement CreateChooseAction(SerializedPropertyBinding binding,
            IReadOnlyList<PrefabApplyTarget> targets, Action changed)
        {
            var button = new Button { tooltip = "Apply to another prefab" };
            var icon = EditorResources.GetTexture("list");
            if (icon != null)
                button.Add(EditorResources.CreateIcon(icon, EditorResources.IconColor));
            else
                button.text = "…";
            button.clicked += () => Show(button.worldBound, binding, targets, changed);
            return button;
        }

        private static void Show(Rect anchor, SerializedPropertyBinding binding,
            IReadOnlyList<PrefabApplyTarget> targets, Action changed)
        {
            var items = new Selector.SelectorItem[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                items[i] = new Selector.SelectorItem
                {
                    displayName = target.Name,
                    searchText = $"{target.Name} {target.AssetPath}",
                    icon = AssetDatabase.GetCachedIcon(target.AssetPath),
                    description = target.RecordsOverride
                        ? "Stored as an override in this prefab."
                        : "Changes the value this prefab defines.",
                    accentKey = target.AssetPath,
                    value = target,
                };
            }
            Selector.Show(anchor, items, null,
                value => Apply(binding, (PrefabApplyTarget)value, changed), closeOwner: true);
        }

        private static InspectorLabel Label(PrefabApplyTarget target)
            => new(target.Name, AssetDatabase.GetCachedIcon(target.AssetPath) as Texture2D,
                target.Name, target.AssetPath);
    }
}
