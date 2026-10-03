using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Builds the shared Paste command: the row pastes the most recent compatible entry, and its
    /// trailing control opens the clipboard as a searchable picker whose rows expand into a preview
    /// of what was copied.
    /// </summary>
    public static class PropertyClipboardMenu
    {
        /// <summary>Shared by <see cref="ClipboardPreviewHost"/> and <see cref="ClipboardValueHost{T}"/>.</summary>
        private const string ValuePath = nameof(ClipboardPreviewHost.value);
        private const string ValuesPath = nameof(ClipboardPreviewHost.values);

        /// <summary>
        /// Appends the Paste command for a control that edits <paramref name="valueType"/> through a
        /// channel of its own rather than a serialized property. The pasted value arrives detached
        /// and in <paramref name="valueType"/>'s own encoding. Nothing is appended while the
        /// clipboard holds no compatible value.
        /// </summary>
        public static void AppendPaste(DropdownMenu menu, string name, Type valueType,
            Action<object> paste)
        {
            if (valueType == null) throw new ArgumentNullException(nameof(valueType));
            if (paste == null) throw new ArgumentNullException(nameof(paste));
            AppendPaste(menu, name, new ClipboardTarget(valueType, ClipboardTargetKind.Value),
                entry => paste(PropertyClipboard.TakeValue(valueType, entry)));
        }

        /// <summary>
        /// Appends the Paste command for <paramref name="target"/>, or nothing when the clipboard
        /// holds no entry it accepts.
        /// </summary>
        internal static void AppendPaste(DropdownMenu menu, string name, ClipboardTarget target,
            Action<ClipboardEntry> paste)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            if (paste == null) throw new ArgumentNullException(nameof(paste));
            var compatible = PropertyClipboard.Compatible(target);
            if (compatible.Count == 0) return;
            var newest = compatible[0];
            menu.AppendAction(name, _ => paste(newest),
                _ => DropdownMenuAction.Status.Normal,
                new Selector.SelectorItem
                {
                    icon = InspectorContextMenu.PasteIcon,
                    tintIcon = true,
                    chip = newest.Label,
                    createDecorator = CreateDecorator(newest),
                    createAction = () => CreateOpenAction(compatible, paste),
                });
        }

        /// <summary>
        /// Value destination for one bound property, carrying every selected target's current value
        /// so an adapter that restricts replacement can be consulted.
        /// </summary>
        internal static ClipboardTarget ValueTarget(SerializedPropertyBinding binding)
        {
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            var current = new List<object>();
            binding.VisitTargetProperties((_, property) =>
                current.Add(SerializedPropertyBinding.Read(property, binding.ValueType)));
            return new ClipboardTarget(binding.ValueType, ClipboardTargetKind.Value, current);
        }

        private static VisualElement CreateOpenAction(List<ClipboardEntry> compatible,
            Action<ClipboardEntry> paste)
        {
            var button = new Button { tooltip = "Open clipboard" };
            var icon = InspectorContextMenu.ClipboardIcon;
            if (icon != null)
                button.Add(EditorResources.CreateIcon(icon, EditorResources.IconColor));
            else
                button.text = "…";
            button.clicked += () => Show(button.worldBound, compatible, paste);
            return button;
        }

        private static void Show(Rect anchor, List<ClipboardEntry> compatible,
            Action<ClipboardEntry> paste)
        {
            var items = new Selector.SelectorItem[compatible.Count];
            for (var i = 0; i < compatible.Count; i++)
            {
                var entry = compatible[i];
                var label = entry.Label;
                items[i] = new Selector.SelectorItem
                {
                    displayName = label.PlainText,
                    searchText = $"{label.PlainText} {entry.Type.FullName}",
                    icon = label.Image,
                    description = ElementLabelUtility.Description(entry.Type),
                    accentKey = label.AccentKey ?? entry.Type,
                    value = entry,
                    createDecorator = CreateDecorator(entry),
                    createBody = HasPreview(entry) ? () => CreatePreview(entry) : null,
                };
            }
            Selector.Show(anchor, items, null, value => paste((ClipboardEntry)value),
                emptyMessage: "The clipboard has nothing to paste here.", closeOwner: true);
        }

        /// <summary>Values a row can show at a glance instead of only naming.</summary>
        private static Func<VisualElement> CreateDecorator(ClipboardEntry entry)
        {
            if (entry.Count != 1) return null;
            switch (entry.Values[0])
            {
                case Color color: return () => InspectorVisuals.CreateColorSwatch(color);
                case Color32 color: return () => InspectorVisuals.CreateColorSwatch(color);
                case Gradient gradient: return () => GradientDrawer.CreatePreview(gradient);
                default: return null;
            }
        }

        private static bool HasPreview(ClipboardEntry entry)
            => entry.IsCollection || entry.Count > 1 || entry.Values[0] != null;

        /// <summary>
        /// Builds the entry as the same editor its value came from. An edit reaches the entry as it
        /// is written, not when the popup closes: the row that pastes runs before the popup tears
        /// down. A value type without a <see cref="ClipboardValueHost{T}"/> falls back to read-only
        /// rows.
        /// </summary>
        private static VisualElement CreatePreview(ClipboardEntry entry)
        {
            var root = InspectorVisuals.CreateStack();
            root.AddToClassList("lightside-clipboard__preview");
            if (!TryBuildEditor(root, entry))
                for (var i = 0; i < entry.Values.Length; i++)
                {
                    var row = new Label(ElementLabelUtility.Compose(entry.Values[i]).PlainText);
                    row.AddToClassList("lightside-clipboard__preview-value");
                    root.Add(row);
                }
            return InspectorVisuals.AlignFields(root);
        }

        private static bool TryBuildEditor(VisualElement root, ClipboardEntry entry)
        {
            Object host;
            Func<object[]> read;
            var elementType = entry.DeclaredType;
            if (IsManagedReference(elementType))
            {
                var references = ScriptableObject.CreateInstance<ClipboardPreviewHost>();
                if (entry.IsCollection)
                {
                    for (var i = 0; i < entry.Values.Length; i++)
                        references.values.Add(PropertyClipboard.CloneObject(entry.Values[i]));
                    root.Add(SerializedPropertyField.CreateCollection(
                        new SerializedObject(references).FindProperty(ValuesPath),
                        Label(elementType),
                        (anchor, insert) =>
                        {
                            if (elementType.IsAbstract || elementType.IsInterface)
                                ManagedReferenceTypeMenu.Show(anchor, elementType, null,
                                    type => insert(ManagedReferenceTypeMenu.Create(type)), false);
                            else
                                insert(Activator.CreateInstance(elementType, true));
                        }));
                    read = () => references.values.ToArray();
                }
                else
                {
                    references.value = PropertyClipboard.CloneObject(entry.Values[0]);
                    var property = new SerializedObject(references).FindProperty(ValuePath);
                    if (SerializedPropertyField.HasRenderer(references.value?.GetType()))
                        root.Add(SerializedPropertyField.Create(property, Label(elementType)));
                    else
                        foreach (var child in InspectorHelpers.VisibleChildren(property))
                            root.Add(SerializedPropertyField.Create(child));
                    read = () => new[] { references.value };
                }
                host = references;
            }
            else
            {
                var hostType = ClipboardValueHosts.Find(elementType);
                if (hostType == null) return false;
                var values = (ClipboardValueHostBase)ScriptableObject.CreateInstance(hostType);
                values.Load(entry.Values, entry.IsCollection);
                root.Add(SerializedPropertyField.Create(
                    new SerializedObject(values).FindProperty(
                        entry.IsCollection ? ValuesPath : ValuePath),
                    Label(elementType)));
                read = () => values.Store(entry.IsCollection);
                host = values;
            }
            host.hideFlags = HideFlags.HideAndDontSave;

            void Commit(IReadOnlyCollection<Object> targets)
            {
                foreach (var target in targets)
                    if (ReferenceEquals(target, host))
                        entry.SetValues(read());
            }

            SerializedPropertyBinding.Written += Commit;
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                SerializedPropertyBinding.Written -= Commit;
                Object.DestroyImmediate(host);
            });
            return true;
        }

        private static bool IsManagedReference(Type type)
            => type.IsClass && type != typeof(string) && !typeof(Object).IsAssignableFrom(type);

        private static string Label(Type type)
            => ObjectNames.NicifyVariableName(ManagedReferenceTypeMenu.TrimSuffix(type.Name, type));
    }
}
