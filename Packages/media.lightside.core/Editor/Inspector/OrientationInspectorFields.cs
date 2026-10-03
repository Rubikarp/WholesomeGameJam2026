using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Creates the shared axis-flip and right-angle rotation controls used by LightSide renderers.</summary>
    public static class OrientationInspectorFields
    {
        private const string RootClass = "lightside-orientation-field";

        /// <summary>Creates independent X and Y mirroring toggles without flattening mixed selections.</summary>
        public static VisualElement CreateFlip(SerializedProperty property, string label = "Flip",
            Action changed = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            var context = new SerializedPropertyContext(binding, property, label);
            var field = new OrientationField(label);
            InspectorPillButton x = null;
            InspectorPillButton y = null;
            var refreshing = false;

            void ToggleAxis(int axis)
            {
                var current = binding.RequireSerializedProperty();
                var axisProperty = axis == 0
                    ? FindAxis(current, "x", "m_X")
                    : FindAxis(current, "y", "m_Y");
                SetAxis(axis, axisProperty.hasMultipleDifferentValues || axisProperty.intValue != 1);
            }

            x = CreateAxisToggle("Horizontal", "Mirror horizontally", () => ToggleAxis(0));
            y = CreateAxisToggle("Vertical", "Mirror vertically", () => ToggleAxis(1));
            field.Input.Add(x);
            field.Input.Add(y);

            void Refresh()
            {
                refreshing = true;
                var current = binding.RequireSerializedProperty();
                var xProperty = FindAxis(current, "x", "m_X");
                var yProperty = FindAxis(current, "y", "m_Y");
                x.SetState(xProperty.intValue == 1, xProperty.hasMultipleDifferentValues,
                    EditorResources.ToggleAccent);
                y.SetState(yProperty.intValue == 1, yProperty.hasMultipleDifferentValues,
                    EditorResources.ToggleAccent);
                refreshing = false;
            }

            void SetAxis(int axis, bool value)
            {
                if (refreshing) return;
                binding.EditSerializedProperties(current =>
                {
                    var flip = current.vector2IntValue;
                    if (axis == 0) flip.x = value ? 1 : 0;
                    else flip.y = value ? 1 : 0;
                    current.vector2IntValue = flip;
                }, $"Change {label}");
                Refresh();
                changed?.Invoke();
            }

            return context.Bind(field, Refresh);
        }

        /// <summary>Creates a four-button selector for rotations in 90-degree increments.</summary>
        public static VisualElement CreateQuarterTurns(SerializedProperty property,
            string label = "Rotation", Action changed = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var binding = new SerializedPropertyBinding(property);
            var context = new SerializedPropertyContext(binding, property, label);
            var field = new OrientationField(label);
            var buttons = new InspectorPillButton[4];

            void Refresh()
            {
                var current = binding.HasMultipleValues ? -1 : Convert.ToInt32(binding.Value);
                for (var i = 0; i < buttons.Length; i++)
                {
                    var selected = current == i;
                    buttons[i].text = $"{i * 90}°";
                    buttons[i].SetState(
                        selected, false, EditorResources.ToggleAccent);
                }
            }

            for (var i = 0; i < buttons.Length; i++)
            {
                var value = i;
                var button = new InspectorPillButton(() =>
                {
                    if (!binding.HasMultipleValues && Convert.ToInt32(binding.Value) == value)
                        return;
                    var next = binding.ValueType.IsEnum
                        ? Enum.ToObject(binding.ValueType, value)
                        : (object)value;
                    binding.SetValue(next, $"Change {label}");
                    Refresh();
                    changed?.Invoke();
                });
                button.AddToClassList(RootClass + "__quarter-turn");
                buttons[i] = button;
                field.Input.Add(button);
            }

            return context.Bind(field, Refresh);
        }

        private static InspectorPillButton CreateAxisToggle(string label, string tooltip,
            Action clicked)
        {
            var button = new InspectorPillButton(clicked)
            {
                text = label,
                tooltip = tooltip,
            };
            button.AddToClassList(RootClass + "__axis");
            return button;
        }

        private static SerializedProperty FindAxis(SerializedProperty property, string plain,
            string backing)
            => property.FindPropertyRelative(plain) ?? property.FindPropertyRelative(backing) ??
               throw new InvalidOperationException(
                   $"Vector property '{property.propertyPath}' has no '{plain}' axis.");

        private sealed class OrientationField : BaseField<int>
        {
            public OrientationField(string label) : this(label, new VisualElement()) { }

            private OrientationField(string label, VisualElement input) : base(label, input)
            {
                InspectorVisuals.Attach(this);
                AddToClassList(RootClass);
                InspectorVisuals.MarkFieldAxis(this);
                input.AddToClassList(RootClass + "__input");
                Input = input;
            }

            public VisualElement Input { get; }
        }
    }
}
