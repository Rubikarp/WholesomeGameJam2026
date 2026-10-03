using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Body drawer of one sequencer clip: the parameter picker fixes the endpoint value type, so
    /// only each endpoint's own fields render — no type UI.
    /// </summary>
    internal sealed class UniTextDriverClipDrawer
    {
        private readonly struct BindingOption
        {
            public readonly ModifierNodeId nodeId;
            public readonly string parameterId;
            public readonly string label;
            public readonly string group;
            public readonly string display;
            public readonly Type rootType;
            public readonly Type nodeType;
            public readonly Type valueType;

            public BindingOption(ModifierNodeId nodeId, ParameterDescriptor parameter, string path,
                Type rootType, Type nodeType)
            {
                this.nodeId = nodeId;
                parameterId = parameter.Id;
                label = $"{path} / {parameter.DisplayName}";
                group = path.Replace(" / ", "/");
                display = parameter.DisplayName;
                this.rootType = rootType;
                this.nodeType = nodeType;
                valueType = parameter.ValueType;
            }
        }

        private const string NoneLabel = "Select parameter...";

        private static readonly List<BindingOption> options = new();
        private static readonly HashSet<BaseModifier> visited = new();

        /// <summary>Grouped parameter picker: options nest by modifier node with the type's
        /// accent colour and icon; the none entry stays at the root. The chosen value reads as its
        /// bound node — the node's icon and accent — while the rows keep their own.</summary>
        private sealed class TargetSelectorField : SelectorField<string>
        {
            public TargetSelectorField(Func<Selector.SelectorItem[]> items)
                : base(null, NoneLabel, items, true, null, null, FormatValue) { }

            private static string FormatValue(string value, Selector.SelectorItem[] items)
                => string.IsNullOrEmpty(value) ? NoneLabel : value;

            /// <inheritdoc/>
            protected override object ValueAccentKey(string current,
                Selector.SelectorItem[] available) => NodeTypeOf(current);

            /// <inheritdoc/>
            protected override Texture ValueIcon(string current,
                Selector.SelectorItem[] available)
                => NodeTypeOf(current) is { } type ? EditorResources.GetTypeIcon(type) : null;

            private static Type NodeTypeOf(string label)
            {
                for (var i = 0; i < options.Count; i++)
                    if (string.Equals(options[i].label, label, StringComparison.Ordinal))
                        return options[i].nodeType;
                return null;
            }
        }

        private static Selector.SelectorItem[] BuildTargetItems()
        {
            var items = new Selector.SelectorItem[options.Count + 1];
            items[0] = new Selector.SelectorItem
            {
                displayName = NoneLabel,
                value = NoneLabel,
            };
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                items[i + 1] = new Selector.SelectorItem
                {
                    displayName = option.display,
                    searchText = option.label,
                    groupName = option.group,
                    groupOrder = i,
                    groupIcon = EditorResources.GetTypeIcon(option.rootType),
                    subGroupIcon = EditorResources.GetTypeIcon(option.nodeType),
                    groupAccentKey = option.rootType,
                    subGroupAccentKey = option.nodeType,
                    value = option.label,
                };
            }
            return items;
        }

        [InitializeOnLoadMethod]
        private static void Register()
        {
            var drawer = new UniTextDriverClipDrawer();
            SerializedPropertyField.RegisterRenderer<UniTextDriverClip>(
                context => drawer.CreatePropertyGUI(context.Property));
        }

        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);

            var targetNode = InspectorHelpers.RequireRelative(property, "targetNode");
            var parameterId = InspectorHelpers.RequireRelative(property, "parameterId");
            var picker = new TargetSelectorField(() =>
            {
                BuildOptions(property.serializedObject.targetObject);
                return BuildTargetItems();
            });
            picker.style.flexGrow = 1f;
            SerializedPropertyField.AddPrefabOverrideIndicator(picker,
                new SerializedPropertyBinding(targetNode), true);
            SerializedPropertyField.AddPrefabOverrideIndicator(picker,
                new SerializedPropertyBinding(parameterId));
            var progressTrack = new VisualElement { pickingMode = PickingMode.Ignore };
            progressTrack.style.height = 3f;
            var progressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            progressFill.style.height = Length.Percent(100f);
            progressFill.style.width = Length.Percent(0f);
            progressTrack.Add(progressFill);
            root.Add(progressTrack);

            var header = new InspectorFoldoutField("Target");
            header.Actions.Add(picker);
            root.Add(header);

            var body = InspectorVisuals.CreateHierarchyBody(header);
            root.Add(body);

            var timing = InspectorVisuals.CreateEqualRow();
            timing.Add(SerializedPropertyField.CreateRelative(property, "start"));
            timing.Add(SerializedPropertyField.CreateRelative(property, "memberDuration", "Duration"));
            body.Add(timing);

            var endpoints = InspectorVisuals.CreateEqualRow();
            body.Add(endpoints);
            AddField(body, property, "easing");
            var clipDefaults = new UniTextDriverClip();
            body.Add(BuildQueryList(property, clipDefaults));
            body.Add(OptInParameters.CreateList(property, OptInParameters.SchemaFrom(clipDefaults, new[]
            {
                ("composition", "Composition"),
                ("priority", "Priority"),
                ("stagger", "Stagger"),
            }), "Advanced"));

            var message = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            message.style.display = DisplayStyle.None;
            body.Add(message);

            header.RegisterValueChangedCallback(evt =>
            {
                property.isExpanded = evt.newValue;
                InspectorMotion.SetExpanded(body, evt.newValue);
            });
            UniTextDriver progressDriver = null;
            UniTextDriverClip progressClip = null;
            var resolved = false;
            var shownFill = float.NaN;

            void ResolveProgressTarget()
            {
                progressDriver = property.serializedObject.targetObject as UniTextDriver;
                progressClip = SerializedPropertyBinding.ResolveInstance(
                    property.serializedObject.targetObject, property.propertyPath)
                    as UniTextDriverClip;
                resolved = true;
                shownFill = float.NaN;
            }

            progressTrack.schedule.Execute(() =>
            {
                if (!resolved) ResolveProgressTarget();
                var local = progressDriver != null && progressClip != null
                    ? progressDriver.ClipProgress(progressClip)
                    : 0f;
                if (local.Equals(shownFill)) return;
                shownFill = local;
                progressFill.style.width = Length.Percent(local * 100f);
            }).Every(60);

            void Refresh()
            {
                var current = property.serializedObject;
                current.Update();
                ResolveProgressTarget();
                BuildOptions(current.targetObject);
                var node = (ModifierNodeId)targetNode.boxedValue;
                var selected = FindSelected(node, parameterId.stringValue) + 1;
                picker.SetValueWithoutNotify(
                    selected > 0 ? options[selected - 1].label : NoneLabel);

                if (selected > 0)
                {
                    EnsureEndpoint(property, "from", options[selected - 1].valueType);
                    EnsureEndpoint(property, "to", options[selected - 1].valueType);
                    current.ApplyModifiedProperties();
                }
                RebuildEndpoints(property, endpoints,
                    selected > 0 ? options[selected - 1] : (BindingOption?)null);

                string error = null;
                if (!node.IsValid || string.IsNullOrWhiteSpace(parameterId.stringValue))
                    error = "Pick the parameter to drive.";
                else if (selected == 0)
                    error = "The selected parameter is not present in the sibling text's styles.";
                else if (InspectorHelpers.RequireRelative(property, "from").managedReferenceValue
                         is not RuleValue)
                    error = $"{options[selected - 1].label} cannot be driven: " +
                            $"{options[selected - 1].valueType.Name} has no serializable endpoint.";
                message.text = error ?? string.Empty;
                message.style.display = string.IsNullOrEmpty(error)
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;

                header.SetExpandedWithoutNotify(property.isExpanded);
                InspectorMotion.SetExpanded(body, property.isExpanded, animate: false);
                var accent = selected > 0
                    ? EditorResources.GetAccentColor(options[selected - 1].nodeType, null)
                    : EditorResources.IconColor;
                var track = accent;
                track.a = 0.12f;
                var fill = accent;
                fill.a = 0.55f;
                progressTrack.style.backgroundColor = track;
                progressFill.style.backgroundColor = fill;
            }

            picker.RegisterValueChangedCallback(evt =>
            {
                var index = -1;
                for (var i = 0; i < options.Count; i++)
                {
                    if (!string.Equals(options[i].label, evt.newValue, StringComparison.Ordinal))
                        continue;
                    index = i;
                    break;
                }
                var current = property.serializedObject;
                current.Update();
                if ((uint)index < (uint)options.Count)
                {
                    var picked = options[index];
                    targetNode.boxedValue = picked.nodeId;
                    parameterId.stringValue = picked.parameterId;
                    EnsureEndpoint(property, "from", picked.valueType);
                    EnsureEndpoint(property, "to", picked.valueType);
                }
                else
                {
                    targetNode.boxedValue = default(ModifierNodeId);
                    parameterId.stringValue = string.Empty;
                }
                current.ApplyModifiedProperties();
                Refresh();
            });

            SerializedPropertyField.Observe(root, Refresh, targetNode, parameterId);
            if (property.serializedObject.targetObject is UniTextDriver driver &&
                driver.GetComponent<UniTextBase>() is { } siblingText)
            {
                Action graphChanged = Refresh;
                root.RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    siblingText.StyleGraphChanged -= graphChanged;
                    siblingText.StyleGraphChanged += graphChanged;
                    Refresh();
                });
                root.RegisterCallback<DetachFromPanelEvent>(_ =>
                    siblingText.StyleGraphChanged -= graphChanged);
            }
            return root;
        }

        private static void AddField(VisualElement root, SerializedProperty property, string name)
            => root.Add(SerializedPropertyField.CreateRelative(property, name));

        private static VisualElement BuildQueryList(SerializedProperty property, object defaults)
        {
            var query = InspectorHelpers.RequireRelative(property, "query");
            var entries = new List<(string path, string label)>();
            var end = query.GetEndProperty();
            var child = query.Copy();
            if (child.NextVisible(true))
                while (!SerializedProperty.EqualContents(child, end))
                {
                    entries.Add(("query." + child.name, child.displayName));
                    if (!child.NextVisible(false)) break;
                }
            return InspectorVisuals.LabelStringFields(OptInParameters.CreateList(
                property, OptInParameters.SchemaFrom(defaults, entries), "Query"));
        }

        private const string FromTooltip = "Value every member starts its ramp from.";
        private const string ToTooltip = "Value every member ramps to.";

        private static void RebuildEndpoints(SerializedProperty property, InspectorRow host,
            BindingOption? option)
        {
            InspectorVisuals.ClearContent(host);
            var from = InspectorHelpers.RequireRelative(property, "from");
            var to = InspectorHelpers.RequireRelative(property, "to");
            if (from.managedReferenceValue is not RuleValue ||
                to.managedReferenceValue is not RuleValue) return;
            var units = option is { } picked
                ? ParameterFieldUtility.ParameterUnits(picked.nodeType, picked.parameterId)
                : null;
            if (units != null)
            {
                AddUnitEndpoints(host, from, to, units);
                return;
            }
            AddEndpoint(host, from, "From", FromTooltip);
            AddEndpoint(host, to, "To", ToTooltip);
        }

        private static void AddEndpoint(InspectorRow host, SerializedProperty endpoint,
            string label, string tooltip)
        {
            var field = SerializedPropertyField.Create(
                InspectorHelpers.RequireRelative(endpoint, "value"), label);
            field.tooltip = tooltip;
            host.Add(field);
        }

        /// <summary>
        /// Endpoint pair of a unit-typed parameter: both values interpolate in one unit, so a
        /// single trailing selector owns the unit of From and To together.
        /// </summary>
        private static void AddUnitEndpoints(InspectorRow host, SerializedProperty from,
            SerializedProperty to, string units)
        {
            var fromValue = InspectorHelpers.RequireRelative(from, "value");
            var toValue = InspectorHelpers.RequireRelative(to, "value");
            var fromNumber = SerializedPropertyField.CreateRelative(fromValue, "value", "From");
            fromNumber.tooltip = FromTooltip;
            host.Add(fromNumber);

            var toNumber = SerializedPropertyField.CreateRelative(toValue, "value", "To");
            toNumber.tooltip = ToTooltip;

            var fromUnit = InspectorHelpers.RequireRelative(fromValue, "unit");
            var toUnit = InspectorHelpers.RequireRelative(toValue, "unit");
            var names = ParameterFieldUtility.GetUnitNames(units);
            var index = ParameterFieldUtility.FindUnitIndex(names, (UnitKind)fromUnit.intValue);
            var unit = new SelectorField<string>(names, Mathf.Max(0, index));
            unit.RegisterValueChangedCallback(evt =>
            {
                var owner = fromUnit.serializedObject;
                owner.Update();
                var kind = (int)UnitNames.Kind(evt.newValue);
                fromUnit.intValue = kind;
                toUnit.intValue = kind;
                owner.ApplyModifiedProperties();
            });
            SerializedPropertyField.OnChange(unit, () => unit.SetValueWithoutNotify(
                    ParameterFieldUtility.NameFor((UnitKind)fromUnit.intValue, names)),
                fromUnit, toUnit);
            SerializedPropertyField.AddPrefabOverrideIndicator(unit,
                new SerializedPropertyBinding(fromUnit), true);
            SerializedPropertyField.AddPrefabOverrideIndicator(unit,
                new SerializedPropertyBinding(toUnit));
            var pair = InspectorVisuals.CreateCompactRow();
            UniTextInspectorTheme.Initialize(pair);
            toNumber.AddToClassList("unitext-inspector-field-row__value");
            unit.AddToClassList("unitext-inspector-field-row__unit");
            pair.Add(toNumber);
            pair.Add(unit);
            host.Add(pair);
        }

        private static void EnsureEndpoint(SerializedProperty property, string name, Type valueType)
        {
            var endpoint = InspectorHelpers.RequireRelative(property, name);
            if (endpoint.managedReferenceValue is RuleValue existing &&
                existing.ValueType == valueType) return;
            endpoint.managedReferenceValue = ParameterRuleDrawer.CreateValue(valueType);
        }

        private static int FindSelected(ModifierNodeId nodeId, string parameterId)
        {
            for (var i = 0; i < options.Count; i++)
                if (options[i].nodeId == nodeId && options[i].parameterId == parameterId)
                    return i;
            return -1;
        }

        /// <summary>
        /// Rebuilds the shared binding options from one driver's style graph. Precedes any batch of
        /// <see cref="TryDescribeBinding"/> calls, which read the options rather than collect them.
        /// </summary>
        internal static void CollectBindings(UniTextDriver driver) => BuildOptions(driver);

        /// <summary>
        /// Resolves one clip binding to its presentation: the parameter's display name and the
        /// bound node's runtime type, against the options <see cref="CollectBindings"/> last
        /// collected. False when the binding is empty or absent from them.
        /// </summary>
        internal static bool TryDescribeBinding(ModifierNodeId node,
            string parameterId, out string display, out Type nodeType)
        {
            var index = FindSelected(node, parameterId);
            if (index < 0)
            {
                display = null;
                nodeType = null;
                return false;
            }
            display = options[index].display;
            nodeType = options[index].nodeType;
            return true;
        }

        private static void BuildOptions(UnityEngine.Object target)
        {
            options.Clear();
            visited.Clear();
            if (target is not UniTextDriver driver) return;
            var text = driver.GetComponent<UniTextBase>();
            if (text == null) return;
            CollectStyles(text.Styles, "Styles");
            var presets = text.StylePresets;
            for (var i = 0; i < presets.Count; i++)
                if (presets[i] != null)
                    CollectStyles(presets[i].Styles, $"Style Presets[{i}]");
            if (text.UseGlobalStylePreset && UniTextSettings.GlobalStylePreset != null)
                CollectStyles(UniTextSettings.GlobalStylePreset.Styles, "Global Styles");
        }

        private static void CollectStyles(IReadOnlyList<Style> styles, string prefix)
        {
            for (var i = 0; i < styles.Count; i++)
            {
                var root = styles[i]?.Modifier;
                if (root == null) continue;
                Collect(root, $"{prefix}[{i}] {DisplayNameOf(root)}", root.GetType());
            }
        }

        private static string DisplayNameOf(BaseModifier modifier)
            => ManagedReferenceTypeMenu.TrimSuffix(modifier.GetType().Name, modifier.GetType());

        private static void Collect(BaseModifier modifier, string path, Type rootType)
        {
            if (!visited.Add(modifier)) return;
            var parameters = modifier.Descriptors;
            for (var i = 0; i < parameters.Length; i++)
                options.Add(new BindingOption(modifier.NodeId, parameters[i], path, rootType,
                    modifier.GetType()));
            if (modifier is ModifierGraphModifier reference)
            {
                if (reference.AuthoredRoot != null)
                    Collect(reference.AuthoredRoot, $"{path} / Preset", rootType);
                return;
            }
            var children = modifier.Children;
            if (children == null) return;
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == null) continue;
                Collect(child, $"{path} / {DisplayNameOf(child)}", rootType);
            }
        }
    }
}
