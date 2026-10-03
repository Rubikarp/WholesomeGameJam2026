using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Custom body renderer for <see cref="MaterialModifier"/> inside <c>[SerializeReference, TypeSelector]</c>
    /// fields. Auto-discovers per-text shader parameters marked with the <c>_UniTextInst*</c> prefix and
    /// <c>[HideInInspector]</c>, and draws them as typed controls (slider / float / color / vector)
    /// backed by constantUv2/constantUv3. Raw Vector4 fields are intentionally hidden — only
    /// user-configurable controls are shown.
    /// </summary>
    internal sealed class MaterialModifierDrawer : IManagedReferenceDrawer
    {
        private const string InstPrefix = "_UniTextInst";

        [InitializeOnLoadMethod]
        private static void Register() =>
            TypedManagedReferenceDrawerRegistry.Register(typeof(MaterialModifier),
                new MaterialModifierDrawer());

        private struct InstField
        {
            public string displayName;
            public ShaderPropertyType type;
            public int channel;
            public int componentIdx;
            public Vector2 rangeLimits;
        }

        private sealed class InstLayout
        {
            public readonly List<InstField> fields = new();
            public Vector4 defaultUv2;
            public Vector4 defaultUv3;
        }

        private static readonly InstLayout emptyLayout = new();

        /// <inheritdoc/>
        public VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var binding = new SerializedPropertyBinding(property);
            var context = new SerializedPropertyContext(binding, property, property.displayName);
            var root = InspectorVisuals.CreateStack();
            var renderedShaderId = int.MinValue;
            var renderedMixedShader = false;

            void RefreshStructure()
            {
                var current = binding.FindSerializedProperty();
                if (current == null) return;
                var material = InspectorHelpers.RequireRelative(current, "material");
                var materialBinding = new SerializedPropertyBinding(material);
                var shader = CommonShader(materialBinding, out var mixedShader);
                var shaderId = mixedShader || shader == null
                    ? 0
                    : ObjectUtils.GetInstanceIdCompat(shader);
                if (shaderId == renderedShaderId && mixedShader == renderedMixedShader) return;
                renderedShaderId = shaderId;
                renderedMixedShader = mixedShader;

                InspectorVisuals.ClearContent(root);
                root.Add(CreateMaterialField(binding, material, RefreshStructure));
                AddField(root, current, "tint");
                AddField(root, current, "renderOrder");
                AddField(root, current, "sortIndex");
                AddField(root, current, "cloneMaterial");
                AddField(root, current, "quadPaddingOverride");

                if (mixedShader)
                {
                    root.Add(new HelpBox(
                        "Selected materials use different shaders. Per-text shader parameters become available when their shader layout is the same.",
                        HelpBoxMessageType.Info));
                    return;
                }
                var fields = ResolveInstLayout(shader).fields;
                if (fields.Count == 0) return;
                var title = new Label("Per-Text Shader Parameters");
                InspectorVisuals.MarkFieldAxis(title);
                root.Add(title);
                var uv2 = InspectorHelpers.RequireRelative(current, "constantUv2");
                var uv3 = InspectorHelpers.RequireRelative(current, "constantUv3");
                for (var i = 0; i < fields.Count; i++)
                    root.Add(CreateInstField(fields[i], fields[i].channel == 3 ? uv3 : uv2));
            }

            return context.Observe(root, RefreshStructure);
        }

        private static Shader CommonShader(SerializedPropertyBinding materialBinding,
            out bool mixed)
        {
            mixed = !materialBinding.TryGetCommonValue(
                value => (value as Material)?.shader, out Shader shader);
            return mixed ? null : shader;
        }

        private static VisualElement CreateMaterialField(SerializedPropertyBinding modifierBinding,
            SerializedProperty materialProperty, System.Action rebuild)
        {
            var binding = new SerializedPropertyBinding(materialProperty);
            var field = new InspectorObjectField("Material", typeof(Material));
            InspectorVisuals.MarkFieldAxis(field);
            void Refresh()
            {
                field.SetValueWithoutNotify(binding.Value as Material);
                field.showMixedValue = binding.HasMultipleValues;
            }
            field.RegisterValueChangedCallback(evt =>
            {
                ApplyMaterial(modifierBinding, evt.newValue as Material);
                Refresh();
                rebuild();
            });
            return new SerializedPropertyContext(binding, materialProperty, "Material")
                .Bind(field, Refresh);
        }

        private static void ApplyMaterial(SerializedPropertyBinding modifierBinding, Material material)
        {
            var layout = ResolveInstLayout(material != null ? material.shader : null);
            modifierBinding.EditSerializedProperties(modifierProperty =>
            {
                InspectorHelpers.RequireRelative(modifierProperty, "material").objectReferenceValue = material;
                if (material == null) return;
                InspectorHelpers.RequireRelative(modifierProperty, "constantUv2").vector4Value = layout.defaultUv2;
                InspectorHelpers.RequireRelative(modifierProperty, "constantUv3").vector4Value = layout.defaultUv3;
            }, "Change Material");
        }

        private static void AddField(VisualElement root,
            SerializedProperty property, string relativePath)
            => root.Add(SerializedPropertyField.CreateRelative(property, relativePath));

        private static VisualElement CreateInstField(InstField descriptor,
            SerializedProperty property)
        {
            var binding = new SerializedPropertyBinding(property);
            VisualElement result;
            switch (descriptor.type)
            {
                case ShaderPropertyType.Range:
                case ShaderPropertyType.Float:
                {
                    if (descriptor.componentIdx < 0)
                        return new HelpBox(
                            $"{descriptor.displayName}: Float requires UV slot suffix X/Y/Z/W.",
                            HelpBoxMessageType.Warning);
                    BaseField<float> field;
                    if (descriptor.type == ShaderPropertyType.Range)
                        field = new InspectorSlider(descriptor.displayName, descriptor.rangeLimits.x,
                            descriptor.rangeLimits.y);
                    else
                        field = new FloatField(descriptor.displayName);
                    result = new SerializedPropertyContext(
                            binding, property, descriptor.displayName)
                        .Bind(field,
                            merge: (current, _, next) =>
                            {
                                var value = (Vector4)current;
                                value[descriptor.componentIdx] = next;
                                return value;
                            },
                            read: current => ((Vector4)current)[descriptor.componentIdx],
                            undoName: $"Change {descriptor.displayName}");
                    break;
                }
                case ShaderPropertyType.Color:
                {
                    if (descriptor.componentIdx >= 0)
                        return new HelpBox(
                            $"{descriptor.displayName}: Color requires a whole UV slot.",
                            HelpBoxMessageType.Warning);
                    var field = new ColorField(descriptor.displayName);
                    result = new SerializedPropertyContext(
                            binding, property, descriptor.displayName)
                        .Bind(field,
                            merge: (current, previous, next) =>
                            {
                                var vector = (Vector4)current;
                                if (next.r != previous.r) vector.x = next.r;
                                if (next.g != previous.g) vector.y = next.g;
                                if (next.b != previous.b) vector.z = next.b;
                                if (next.a != previous.a) vector.w = next.a;
                                return vector;
                            },
                            read: current =>
                            {
                                var vector = (Vector4)current;
                                return new Color(vector.x, vector.y, vector.z, vector.w);
                            },
                            undoName: $"Change {descriptor.displayName}");
                    break;
                }
                case ShaderPropertyType.Vector:
                {
                    if (descriptor.componentIdx >= 0)
                        return new HelpBox(
                            $"{descriptor.displayName}: Vector requires a whole UV slot.",
                            HelpBoxMessageType.Warning);
                    var field = new Vector4Field(descriptor.displayName);
                    result = new SerializedPropertyContext(
                            binding, property, descriptor.displayName)
                        .Bind(field,
                            merge: (current, previous, next) =>
                            {
                                var value = (Vector4)current;
                                if (!next.x.Equals(previous.x)) value.x = next.x;
                                if (!next.y.Equals(previous.y)) value.y = next.y;
                                if (!next.z.Equals(previous.z)) value.z = next.z;
                                if (!next.w.Equals(previous.w)) value.w = next.w;
                                return value;
                            },
                            undoName: $"Change {descriptor.displayName}");
                    break;
                }
                default:
                    return new HelpBox(
                        $"{descriptor.displayName}: {descriptor.type} is not supported in UV slots.",
                        HelpBoxMessageType.Warning);
            }
            InspectorVisuals.MarkFieldAxis(result);
            return result;
        }

        private static InstLayout ResolveInstLayout(Shader shader)
        {
            if (shader == null) return emptyLayout;

            var layout = new InstLayout();
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                var name = shader.GetPropertyName(i);
                if (!name.StartsWith(InstPrefix, System.StringComparison.Ordinal)) continue;
                var flags = shader.GetPropertyFlags(i);
                if ((flags & ShaderPropertyFlags.HideInInspector) == 0) continue;
                if (!TryParseSlot(name.Substring(InstPrefix.Length), out var channel, out var componentIdx)) continue;

                var type = shader.GetPropertyType(i);
                var range = type == ShaderPropertyType.Range
                    ? shader.GetPropertyRangeLimits(i)
                    : Vector2.zero;
                layout.fields.Add(new InstField
                {
                    displayName  = shader.GetPropertyDescription(i),
                    type         = type,
                    channel      = channel,
                    componentIdx = componentIdx,
                    rangeLimits  = range,
                });

                ref var target = ref (channel == 3 ? ref layout.defaultUv3 : ref layout.defaultUv2);

                switch (type)
                {
                    case ShaderPropertyType.Range:
                    case ShaderPropertyType.Float:
                        if (componentIdx >= 0)
                            target[componentIdx] = shader.GetPropertyDefaultFloatValue(i);
                        break;
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        if (componentIdx < 0)
                            target = shader.GetPropertyDefaultVectorValue(i);
                        break;
                }
            }

            layout.fields.Sort((a, b) => a.channel != b.channel
                ? a.channel - b.channel
                : a.componentIdx - b.componentIdx);
            return layout;
        }

        private static bool TryParseSlot(string suffix, out int channel, out int componentIdx)
        {
            channel = 0;
            componentIdx = -1;

            if (suffix == "Uv2") { channel = 2; return true; }
            if (suffix == "Uv3") { channel = 3; return true; }
            if (suffix.Length != 4) return false;
            if (suffix[0] != 'U' || suffix[1] != 'v') return false;

            var digit = suffix[2];
            if (digit != '2' && digit != '3') return false;
            channel = digit - '0';

            componentIdx = suffix[3] switch
            {
                'X' => 0,
                'Y' => 1,
                'Z' => 2,
                'W' => 3,
                _ => -2,
            };
            return componentIdx >= 0;
        }
    }
}
