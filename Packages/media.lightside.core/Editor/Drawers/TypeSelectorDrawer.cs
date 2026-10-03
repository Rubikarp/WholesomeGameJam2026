using System;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Renders <c>[SerializeReference, TypeSelector]</c> fields with a grouped type picker,
    /// copy/paste actions, and a retained expandable body.
    /// </summary>
    /// <remarks>
    /// Not registered as an attribute renderer. <c>[TypeSelector]</c> reads two ways: on a managed-reference
    /// field it chooses that field's type, and on a <c>List&lt;T&gt;</c> it declares the elements polymorphic
    /// and the field is still an ordinary collection. Only the pipeline's managed-reference branch can tell
    /// them apart, so it is the pipeline that dispatches here.
    /// </remarks>
    [CustomPropertyDrawer(typeof(TypeSelectorAttribute))]
    public class TypeSelectorDrawer : LightSidePropertyBridge
    {
        private static readonly BoundedMemo<(int, long), bool> expandedStates = new(512);

        internal static VisualElement CreateToolkit(SerializedPropertyContext context)
            => Build(context, embedded: false, out _, out _);

        /// <summary>
        /// Builds the detached selector-header row and configuration body of one
        /// <c>[SerializeReference, TypeSelector]</c> property for a host that composes them into
        /// its own layout — typically a list-element header row plus the element's foldout body.
        /// The header carries the type picker, per-type header content and the copy/paste menu;
        /// the body tracks the selected type and owns the refresh subscription, so both must stay
        /// attached while the property is shown.
        /// </summary>
        public static void CreateEmbedded(SerializedProperty property, out VisualElement header,
            out VisualElement body)
        {
            var context = new SerializedPropertyContext(
                new SerializedPropertyBinding(property), property, null);
            Build(context, embedded: true, out header, out body);
        }

        private static VisualElement Build(SerializedPropertyContext context, bool embedded,
            out VisualElement headerOut, out VisualElement bodyOut)
        {
            headerOut = null;
            bodyOut = null;
            var property = context.Property;
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                var error = new HelpBox("[TypeSelector] requires [SerializeReference].",
                    HelpBoxMessageType.Error);
                if (!embedded) return error;
                headerOut = error;
                bodyOut = new VisualElement();
                return null;
            }

            var baseType = ManagedReferenceTypeMenu.GetBaseType(
                               context.Binding.SerializedField.FieldType) ??
                           throw new InvalidOperationException(
                               $"Cannot resolve the managed-reference base type for '{property.propertyPath}'.");
            var binding = context.Binding;

            var isListElement = !embedded && ElementLabelUtility.IsArrayElement(property);
            var standalone = !embedded && !isListElement && string.IsNullOrEmpty(context.Label);
            VisualElement root = null;
            InspectorFoldoutHeader listHeader = null;
            InspectorFoldoutField fieldHeader = null;
            VisualElement header;
            InspectorRow actions;
            if (embedded)
            {
                actions = InspectorRow.CreateActions();
                header = actions;
                InspectorVisuals.Attach(header);
                header.AddToClassList("lightside-type-selector__header");
                header.AddToClassList("lightside-type-selector__embedded-header");
            }
            else
            {
                root = new VisualElement();
                InspectorVisuals.Attach(root);
                root.AddToClassList("lightside-type-selector");
                listHeader = isListElement ? new InspectorFoldoutHeader() : null;
                fieldHeader = isListElement ? null : new InspectorFoldoutField(context.Label);
                header = listHeader != null ? (VisualElement)listHeader : fieldHeader;
                actions = listHeader != null ? listHeader.ActionRow : fieldHeader.ActionRow;
                header.AddToClassList("lightside-type-selector__header");
                if (isListElement)
                    header.AddToClassList("lightside-type-selector__element-header");
            }

            var selector = isListElement
                ? InspectorSelectorButton.IconOnly()
                : new InspectorSelectorButton();
            selector.AddToClassList("lightside-type-selector__column");
            actions.Add(selector);
            root?.Add(header);

            VisualElement body;
            if (embedded)
            {
                body = InspectorVisuals.CreateStack();
                body.AddToClassList("lightside-type-selector__body");
            }
            else
            {
                body = InspectorVisuals.CreateHierarchyBody(header);
                body.AddToClassList("lightside-type-selector__body");
                root.Add(body);
            }

            long renderedId = long.MinValue;
            Type renderedType = null;
            var renderedMixedType = false;
            var bodyBuilt = false;

            void NotifyTypeChanged(Type previous, Type next)
            {
                var target = root ?? body;
                using var evt = ChangeEvent<Type>.GetPooled(previous, next);
                evt.target = target;
                target.SendEvent(evt);
            }

            void BuildActions(SerializedProperty current, bool mixedType)
            {
                selector.RemoveFromHierarchy();
                InspectorVisuals.ClearContent(actions);
                actions.Add(selector);
                if (mixedType) return;
                if (TypedManagedReferenceDrawerRegistry.TryGet(
                        current.managedReferenceValue?.GetType(), out var custom) &&
                    custom is IManagedReferenceHeaderDrawer headerDrawer)
                {
                    var inlineContent = headerDrawer.CreateHeaderGUI(current) ??
                                        throw new InvalidOperationException(
                                            $"Header drawer for '{current.managedReferenceValue.GetType().FullName}' returned null.");
                    inlineContent.AddToClassList("lightside-type-selector__column");
                    actions.Add(inlineContent);
                }
                foreach (var child in InspectorHelpers.VisibleChildren(current))
                {
                    if (!SerializedPropertyField.TryCreateHeaderAction(child, out var action)) continue;
                    action.RegisterCallback<ChangeEvent<bool>>(_ => RefreshBody());
                    actions.Add(action);
                }
            }

            void BuildBody(SerializedProperty current, Type currentType)
            {
                InspectorVisuals.ClearContent(body);
                if (TypedManagedReferenceDrawerRegistry.TryGet(currentType, out var custom))
                    body.Add(custom.CreatePropertyGUI(current));
                else
                {
                    foreach (var child in InspectorHelpers.VisibleChildren(current))
                        body.Add(SerializedPropertyField.Create(child));
                }
                bodyBuilt = true;
            }

            void RefreshBody()
            {
                bodyBuilt = false;
                Refresh();
            }

            void Refresh()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                var value = current.managedReferenceValue;
                var currentType = CommonRuntimeType(binding, out var mixedType);
                var currentId = value == null ? 0 : current.managedReferenceId;
                var structureChanged = currentId != renderedId || currentType != renderedType ||
                                       mixedType != renderedMixedType;

                var displayName = mixedType
                    ? "\u2014"
                    : ManagedReferenceTypeMenu.DisplayName(currentType, baseType);
                var typeDescription = mixedType
                    ? $"Selected values use different {baseType.Name} types."
                    : ElementLabelUtility.Description(currentType);
                header.tooltip = SerializedPropertyField.Tooltip(binding) ?? typeDescription;
                if (mixedType)
                    selector.SetState(false, true,
                        EditorResources.GetAccentColor(baseType, baseType.Name));
                else
                    selector.SetValueAccent(currentType, displayName);
                if (isListElement)
                {
                    listHeader.SetContent(mixedType
                            ? displayName
                            : value == null
                                ? displayName
                                : ElementLabelUtility.ColoredName(current),
                        mixedType ? null : ElementLabelUtility.Icon(current));
                    selector.text = string.Empty;
                    selector.tooltip = $"Change {displayName}";
                }
                else
                {
                    selector.text = displayName;
                    selector.tooltip = typeDescription;
                }

                if (structureChanged)
                {
                    BuildActions(current, mixedType);
                    InspectorVisuals.ClearContent(body);
                    bodyBuilt = false;
                    renderedId = currentId;
                    renderedType = currentType;
                    renderedMixedType = mixedType;
                }

                if (!mixedType && currentType != null && value != null && !bodyBuilt)
                    BuildBody(current, currentType);
                if (!embedded)
                    InspectorVisuals.RefreshHierarchyBody(header, body,
                        standalone || GetExpanded(current), !standalone);
            }

            void ShowSelector()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                var currentType = CommonRuntimeType(binding, out var mixedType);
                var exclude = binding.SerializedField
                    ?.GetCustomAttribute<TypeSelectorAttribute>()?.Exclude;
                ManagedReferenceTypeMenu.Show(selector.worldBound, baseType, currentType,
                    type =>
                    {
                        binding.SetValue(type == null ? null : ManagedReferenceTypeMenu.Create(type),
                            $"Change {binding.DisplayName}");
                        Refresh();
                        NotifyTypeChanged(mixedType ? null : currentType, type);
                    }, showNone: true, exclude);
            }

            void SetExpanded(bool value)
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                current.isExpanded = value;
                StoreExpanded(current, value);
                Refresh();
            }

            if (listHeader != null)
                listHeader.Changed += SetExpanded;
            else if (fieldHeader != null)
                fieldHeader.RegisterValueChangedCallback(evt => SetExpanded(evt.newValue));
            selector.clicked += ShowSelector;
            header.AddManipulator(new InspectorContextMenuManipulator(menu =>
                PopulateCopyPasteMenu(menu, binding, (previous, next) =>
                {
                    Refresh();
                    NotifyTypeChanged(previous, next);
                })));
            if (embedded)
            {
                headerOut = header;
                bodyOut = context.Observe(body, Refresh);
                return null;
            }
            return context.Observe(root, Refresh);
        }

        private static Type CommonRuntimeType(SerializedPropertyBinding binding,
            out bool mixed)
        {
            mixed = !binding.TryGetCommonValue(value => value?.GetType(), out Type type);
            return mixed ? null : type;
        }

        private static bool GetExpanded(SerializedProperty property)
        {
            if (property.managedReferenceValue == null) return property.isExpanded;
            var key = (ObjectUtils.GetInstanceIdCompat(
                property.serializedObject.targetObject), property.managedReferenceId);
            if (expandedStates.TryGetValue(key, out var expanded)) return expanded;
            expandedStates[key] = property.isExpanded;
            return property.isExpanded;
        }

        private static void StoreExpanded(SerializedProperty property, bool value)
        {
            if (property.managedReferenceValue == null) return;
            expandedStates[(ObjectUtils.GetInstanceIdCompat(
                property.serializedObject.targetObject), property.managedReferenceId)] = value;
        }

        private static void PopulateCopyPasteMenu(DropdownMenu menu,
            SerializedPropertyBinding binding, Action<Type, Type> changed)
        {
            var property = binding.FindSerializedProperty();
            if (property == null) return;
            if (property.managedReferenceValue != null)
                InspectorContextMenu.AppendCommand(menu, "Copy",
                    InspectorContextMenu.CopyIcon, _ =>
                    {
                        var current = binding.FindSerializedProperty();
                        if (current != null) PropertyClipboard.Copy(current, binding.ValueType);
                    });

            var target = PropertyClipboardMenu.ValueTarget(binding);
            PropertyClipboardMenu.AppendPaste(menu, "Paste", target, entry =>
            {
                var previous = binding.Value?.GetType();
                binding.EditSerializedProperties(
                    current => PropertyClipboard.Paste(current, target, entry), "Paste");
                changed?.Invoke(previous, binding.Value?.GetType());
            });

            SerializedPropertyField.AppendDuplicateAction(menu, binding);
            var typeBefore = binding.Value?.GetType();
            PrefabOverrideMenu.Append(menu, binding,
                () => changed?.Invoke(typeBefore, binding.Value?.GetType()));
        }
    }
}
