using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(Style))]
    internal class StyleDrawer : LightSidePropertyDrawer<Style>
    {
        private static readonly BoundedMemo<(int, long), bool> expandedStates = new(512);

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var binding = context.Binding;
            var root = new VisualElement();
            UniTextInspectorTheme.Initialize(root);
            root.AddToClassList("unitext-style");
            var header = new InspectorFoldoutHeader();
            header.AddToClassList("unitext-style__header");
            var change = InspectorSelectorButton.IconOnly();
            change.tooltip = "Change style";
            header.Actions.Add(change);
            var enabled = new InspectorEyeToggle(binding, "disabled", storesDisabled: true, "style",
                header);
            header.Actions.Add(enabled.Button);
            root.Add(header);
            var body = InspectorVisuals.CreateHierarchyBody(header);
            body.AddToClassList("unitext-style__body");
            root.Add(body);

            RetainedBody retained = null;

            object Structure(SerializedProperty current)
            {
                var source = InspectorHelpers.RequireRelative(current, "source");
                var modifier = InspectorHelpers.RequireRelative(current, "modifier");
                var sourceTypeMixed = new SerializedPropertyBinding(source)
                    .HaveDifferentValues(value => value?.GetType());
                return (
                    modifier.managedReferenceValue == null ? 0 : modifier.managedReferenceId,
                    source.managedReferenceValue == null ? 0 : source.managedReferenceId,
                    ParameterFieldUtility.GetModifierSignature(modifier),
                    !sourceTypeMixed && IsStandaloneSource(source),
                    sourceTypeMixed,
                    !sourceTypeMixed && IsSourceNull(source));
            }

            void BuildBody(SerializedProperty current)
            {
                var source = InspectorHelpers.RequireRelative(current, "source");
                var modifier = InspectorHelpers.RequireRelative(current, "modifier");
                var parameter = InspectorHelpers.RequireRelative(current, "defaultParameter");
                var sourceTypeMixed = new SerializedPropertyBinding(source)
                    .HaveDifferentValues(value => value?.GetType());
                var standalone = !sourceTypeMixed && IsStandaloneSource(source);
                var sourceNull = !sourceTypeMixed && IsSourceNull(source);

                if (!standalone)
                    body.Add(SerializedPropertyField.Create(modifier, "Modifier"));

                if (sourceNull && !standalone)
                {
                    var sourceRow = InspectorVisuals.CreateRow();
                    sourceRow.AddToClassList("unitext-style__source-row");
                    var sourceField = SerializedPropertyField.Create(source, "Source");
                    sourceField.AddToClassList("unitext-style__source-field");
                    sourceRow.Add(sourceField);
                    if (SerializedPropertyField.TryCreateHeaderAction(parameter, out var action))
                        sourceRow.Add(action);
                    body.Add(sourceRow);
                    body.Add(SerializedPropertyField.Create(parameter));
                }
                else
                {
                    body.Add(SerializedPropertyField.Create(source, "Source"));
                }
            }

            void Refresh()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                var source = InspectorHelpers.RequireRelative(current, "source");
                var modifier = InspectorHelpers.RequireRelative(current, "modifier");
                var expanded = GetExpanded(current, modifier, source);

                change.style.display = ElementLabelUtility.IsArrayElement(current)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                var content = BuildCommonLabel(binding, current, out var mixedLabel);
                header.SetContent(content.Text, content.Image);
                header.tooltip = BuildDescription(modifier, source);
                change.SetValueAccent(
                    mixedLabel
                        ? typeof(Style)
                        : modifier.managedReferenceValue?.GetType() ??
                          source.managedReferenceValue?.GetType(), content.Text);
                header.SetExpandedWithoutNotify(expanded);
                enabled.Refresh();

                if (retained.Refresh(current, expanded))
                    UpdateParameterShapes(current);
                InspectorMotion.SetExpanded(body, expanded);
            }

            void RefreshBody()
            {
                retained.Invalidate();
                Refresh();
            }

            retained = new RetainedBody(body, Structure, BuildBody);

            header.Changed += value =>
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                current.isExpanded = value;
                StoreExpanded(current,
                    InspectorHelpers.RequireRelative(current, "modifier"),
                    InspectorHelpers.RequireRelative(current, "source"), value);
                Refresh();
            };
            change.clicked += () =>
            {
                var current = binding.FindSerializedProperty();
                if (current == null || !ElementLabelUtility.IsArrayElement(current)) return;
                var currentPath = current.propertyPath;
                UniTextBaseEditor.ShowStylePresetSelector(
                    change.worldBound,
                    current.serializedObject.targetObjects,
                    current.serializedObject,
                    (target, style) => UniTextBaseEditor.ReplaceSerializedStyle(
                        target, currentPath, style),
                    false,
                    false,
                    RefreshBody);
            };
            root.RegisterCallback<ChangeEvent<Type>>(_ => Refresh());
            root.RegisterCallback<ModifierStructureChangedEvent>(_ => Refresh());
            root.RegisterCallback<ChangeEvent<bool>>(evt =>
            {
                if (evt.target is VisualElement element &&
                    element.ClassListContains("unitext-default-parameters__reveal"))
                    RefreshBody();
            });
            return context.Observe(root, Refresh);
        }

        private static string BuildDescription(SerializedProperty modifier,
            SerializedProperty source)
        {
            var modifierDescription = ElementLabelUtility.Description(
                modifier.managedReferenceValue?.GetType());
            var sourceDescription = ElementLabelUtility.Description(
                source.managedReferenceValue?.GetType());
            if (string.IsNullOrWhiteSpace(modifierDescription)) return sourceDescription;
            if (string.IsNullOrWhiteSpace(sourceDescription)) return modifierDescription;
            return modifierDescription + "\n\n" + sourceDescription;
        }

        private static bool GetExpanded(SerializedProperty style,
            SerializedProperty modifier, SerializedProperty source)
        {
            var id = StableReferenceId(modifier, source);
            if (id == 0) return style.isExpanded;
            var key = (ObjectUtils.GetInstanceIdCompat(
                style.serializedObject.targetObject), id);
            if (expandedStates.TryGetValue(key, out var expanded)) return expanded;
            expandedStates[key] = style.isExpanded;
            return style.isExpanded;
        }

        private static void StoreExpanded(SerializedProperty style,
            SerializedProperty modifier, SerializedProperty source, bool value)
        {
            var id = StableReferenceId(modifier, source);
            if (id == 0) return;
            expandedStates[(ObjectUtils.GetInstanceIdCompat(
                style.serializedObject.targetObject), id)] = value;
        }

        private static long StableReferenceId(SerializedProperty modifier,
            SerializedProperty source)
        {
            if (modifier.managedReferenceValue != null) return modifier.managedReferenceId;
            return source.managedReferenceValue == null ? 0 : source.managedReferenceId;
        }

        private static InspectorLabel BuildCommonLabel(SerializedPropertyBinding binding,
            SerializedProperty primary, out bool mixed)
        {
            var content = BuildLabel(primary);
            var differs = binding.AnyTargetProperty((_, property) =>
            {
                var target = BuildLabel(property);
                return target.Text != content.Text || target.Image != content.Image;
            });
            if (differs)
            {
                mixed = true;
                return new InspectorLabel("\u2014");
            }
            mixed = false;
            return content;
        }

        private static InspectorLabel BuildLabel(SerializedProperty property)
        {
            var style = SerializedPropertyBinding.ResolveInstance(
                property.serializedObject.targetObject, property.propertyPath) as Style;
            if (style == null || (style.Modifier == null && style.Source == null))
                return new InspectorLabel("(Empty)");
            return ElementLabelUtility.Compose(style);
        }

        private static bool IsStandaloneSource(SerializedProperty sourceProp)
            => sourceProp?.managedReferenceValue is ParseRule rule && rule.IsStandalone;

        private static bool IsSourceNull(SerializedProperty sourceProp)
            => sourceProp != null && sourceProp.managedReferenceValue == null;

        private static void UpdateParameterShapes(SerializedProperty styleProperty)
        {
            var targets = styleProperty.serializedObject.targetObjects;
            if (targets.Length == 1)
            {
                UpdateParameterShapesForStyle(styleProperty);
                return;
            }
            new SerializedPropertyBinding(styleProperty)
                .VisitTargetProperties((_, style) => UpdateParameterShapesForStyle(style));
        }

        private static void UpdateParameterShapesForStyle(SerializedProperty style)
        {
            var source = InspectorHelpers.RequireRelative(style, "source");
            var modifier = InspectorHelpers.RequireRelative(style, "modifier");
            TryReshape(InspectorHelpers.RequireRelative(style, "defaultParameter"), modifier);
            if (source.managedReferenceValue == null) return;
            foreach (var field in ParameterFieldUtility.GetDefaultParameterFieldInfos(
                         source.managedReferenceValue.GetType()))
                TryReshape(source.FindPropertyRelative(field.Name), modifier);

            var ranges = source.FindPropertyRelative("ranges");
            if (ranges == null || !ranges.isArray) return;
            for (var i = 0; i < ranges.arraySize; i++)
                TryReshape(ranges.GetArrayElementAtIndex(i)
                    .FindPropertyRelative("parameter"), modifier);
        }

        private static void TryReshape(SerializedProperty parameterProp, SerializedProperty modifierProp)
        {
            if (parameterProp == null ||
                parameterProp.propertyType != SerializedPropertyType.String)
                return;
            ParameterFieldUtility.UpdateParameterShape(parameterProp, modifierProp);
        }
    }
}
