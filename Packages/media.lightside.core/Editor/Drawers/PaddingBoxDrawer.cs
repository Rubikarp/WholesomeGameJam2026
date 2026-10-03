using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Edits a four-sided serialized padding value through the shared padding popup.</summary>
    public sealed class PaddingBoxButton : InspectorPillButton
    {
        private readonly SerializedPropertyBinding serializedBinding;
        private readonly Label label;
        private readonly string undoLabel;

        /// <summary>Raised after the serialized padding value changes.</summary>
        public event Action Changed;

        /// <summary>Creates a padding button bound to one serialized Vector4 property.</summary>
        public PaddingBoxButton(SerializedPropertyBinding binding, string undoLabel = null)
        {
            serializedBinding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (binding.ValueType != typeof(Vector4))
                throw new ArgumentException("Padding requires a Vector4 binding.", nameof(binding));
            this.undoLabel = string.IsNullOrEmpty(undoLabel)
                ? $"Change {binding.DisplayName}"
                : undoLabel;

            AddToClassList("lightside-padding-button");
            label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("lightside-padding-button__label");
            Add(label);
            var arrow = InspectorVisuals.CreateDropdownArrow();
            arrow.AddToClassList("lightside-padding-button__arrow");
            Add(arrow);
            clicked += Open;
        }

        /// <summary>Creates a pill button bound to one serialized Vector4 padding field.</summary>
        public static PaddingBoxButton Create(SerializedProperty property, string label = null,
            string undoLabel = null, Action changed = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            var text = label ?? property.displayName;
            var button = new PaddingBoxButton(binding, undoLabel ?? $"Change {text}")
            {
                text = text,
                tooltip = property.tooltip,
            };

            void Refresh()
            {
                var value = (Vector4)binding.Value;
                button.SetState(
                    value != Vector4.zero, binding.HasMultipleValues, EditorResources.ToggleAccent);
            }

            button.Changed += () =>
            {
                Refresh();
                changed?.Invoke();
            };
            return new SerializedPropertyContext(binding, property, text).Bind(button, Refresh);
        }

        /// <summary>The text displayed before the dropdown arrow.</summary>
        public new string text
        {
            get => label.text;
            set => label.text = value;
        }

        /// <inheritdoc/>
        public override void SetState(bool active, bool mixed, Color accent,
            string iconName = null)
        {
            base.SetState(active, mixed, accent, iconName);
            label.style.color = style.color;
        }

        internal void SetTextColor(Color color)
        {
            style.color = color;
            label.style.color = color;
        }

        private void Open()
        {
            var dragUndoGroup = -1;
            PaddingBoxPopupWindow.Show(
                worldBound,
                () => (Vector4)serializedBinding.Value,
                axis => InspectorHelpers.RequireRelative(
                    serializedBinding.RequireSerializedProperty(),
                    AxisName(axis)).hasMultipleDifferentValues,
                (axis, value) =>
                {
                    serializedBinding.TransformValue(current =>
                    {
                        var padding = (Vector4)current;
                        padding[axis] = value;
                        return padding;
                    }, undoLabel);
                    Changed?.Invoke();
                },
                () =>
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName(undoLabel);
                    dragUndoGroup = Undo.GetCurrentGroup();
                },
                delta =>
                {
                    serializedBinding.EditSerializedPropertiesInCurrentUndoGroup(
                        value => value.vector4Value += delta, undoLabel);
                    Changed?.Invoke();
                },
                () =>
                {
                    if (dragUndoGroup >= 0)
                        Undo.CollapseUndoOperations(dragUndoGroup);
                    serializedBinding.SerializedObject.SetIsDifferentCacheDirty();
                    serializedBinding.SerializedObject.UpdateIfRequiredOrScript();
                    Changed?.Invoke();
                });
        }

        private static string AxisName(int axis) => axis switch
        {
            0 => "x",
            1 => "y",
            2 => "z",
            _ => "w",
        };
    }

    [CustomPropertyDrawer(typeof(PaddingBoxAttribute))]
    internal sealed class PaddingBoxDrawer : LightSidePropertyDrawer<PaddingBoxAttribute>
    {
        private static readonly Color idleAccent = new(0.5f, 0.5f, 0.5f, 1f);

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            if (context.Property.propertyType != SerializedPropertyType.Vector4)
                return new HelpBox("[PaddingBox] requires Vector4.", HelpBoxMessageType.Error);
            var binding = context.Binding;
            var field = new PaddingField(context.Label, binding);

            void Refresh()
            {
                var value = (Vector4)binding.Value;
                var isSet = value != Vector4.zero;
                var accent = isSet
                    ? EditorResources.GetTypeColor(typeof(PaddingBoxAttribute))
                    : idleAccent;
                field.Button.text = binding.HasMultipleValues
                    ? "—"
                    : $"{value.x:g}  {value.y:g}  {value.z:g}  {value.w:g}";
                field.Button.SetTextColor(accent);
            }

            field.Button.Changed += Refresh;
            return context.Observe(field, Refresh);
        }

        private sealed class PaddingField : BaseField<Vector4>
        {
            public PaddingField(string label, SerializedPropertyBinding binding)
                : this(label, new PaddingBoxButton(binding, $"Change {label}")) { }

            private PaddingField(string label, PaddingBoxButton button) : base(label, button)
            {
                InspectorVisuals.Attach(this);
                InspectorVisuals.MarkFieldAxis(this);
                AddToClassList("lightside-padding-field");
                button.AddToClassList("lightside-padding-field__button");
                Button = button;
            }

            public PaddingBoxButton Button { get; }
        }
    }
}
