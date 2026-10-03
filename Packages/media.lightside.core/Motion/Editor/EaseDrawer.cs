using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// One popup over every built-in curve plus the two custom shapes; choosing a custom shape reveals the
    /// pencil that opens the curve editor.
    /// </summary>
    [CustomPropertyDrawer(typeof(Ease))]
    internal sealed class EaseDrawer : LightSidePropertyDrawer<Ease>
    {
        private const string CubicLabel = "Bézier…";
        private const string CurveLabel = "Curve…";
        private const string CustomGroup = "Custom";

        /// <summary>
        /// Curve families in the order they progress from gentlest to sharpest, which is the order an
        /// author picks along and the one the enum declares — <see cref="Enum.GetNames"/> returns
        /// values, not declarations, so the progression has to be restated here. A family absent from
        /// the list still lists, after the known ones.
        /// </summary>
        private static readonly string[] families =
        {
            "Sine", "Quadratic", "Cubic", "Quartic", "Quintic",
            "Exponential", "Circular", "Back", "Elastic", "Bounce",
        };

        /// <summary>Name endings that make a curve one direction of a family rather than a curve of its own; longest first.</summary>
        private static readonly string[] directions = { "InOut", "Out", "In" };

        private static readonly string[] builtinNames = Enum.GetNames(typeof(EasingType));
        private static readonly Array builtinValues = Enum.GetValues(typeof(EasingType));
        private static readonly Selector.SelectorItem[] choices = BuildChoices();

        private static Selector.SelectorItem[] BuildChoices()
        {
            var result = new Selector.SelectorItem[builtinNames.Length + 2];

            for (var i = 0; i < builtinNames.Length; i++)
            {
                var family = FamilyOf(builtinNames[i], out var direction);
                result[i] = new Selector.SelectorItem
                {
                    displayName = ObjectNames.NicifyVariableName(direction),
                    searchText = ObjectNames.NicifyVariableName(builtinNames[i]),
                    groupName = family,
                    groupOrder = OrderOf(family),
                    value = i,
                };
            }

            result[builtinNames.Length] = Custom(CubicLabel, builtinNames.Length);
            result[builtinNames.Length + 1] = Custom(CurveLabel, builtinNames.Length + 1);
            return result;

            static Selector.SelectorItem Custom(string label, int index) => new()
            {
                displayName = label,
                groupName = CustomGroup,
                groupOrder = int.MaxValue,
                value = index,
            };
        }

        private static int OrderOf(string family)
        {
            var order = Array.IndexOf(families, family);
            return order >= 0 ? order : families.Length;
        }

        /// <summary>
        /// Splits a curve name into the family that groups it and the direction that names it inside
        /// that group. A curve belonging to no family — <c>Linear</c>, the smoothsteps, the steps —
        /// reports an empty family and keeps its whole name, which places it at the root.
        /// </summary>
        private static string FamilyOf(string name, out string direction)
        {
            for (var i = 0; i < directions.Length; i++)
            {
                var suffix = directions[i];
                if (name.Length <= suffix.Length ||
                    !name.EndsWith(suffix, StringComparison.Ordinal)) continue;

                direction = suffix;
                return name.Substring(0, name.Length - suffix.Length);
            }

            direction = name;
            return string.Empty;
        }

        /// <summary>
        /// Names the chosen curve in full on the closed field, where no group header stands beside it
        /// to say which family the direction belongs to.
        /// </summary>
        private sealed class EaseSelectorField : SelectorField<int>
        {
            public EaseSelectorField(string label, int value)
                : base(label, value, () => choices, true, null, null, Format)
            {
            }

            private static string Format(int current, Selector.SelectorItem[] available)
            {
                for (var i = 0; i < available.Length; i++)
                {
                    if (!Equals(available[i].value, current)) continue;
                    return string.IsNullOrEmpty(available[i].groupName)
                        ? available[i].displayName
                        : available[i].groupName + " " + available[i].displayName;
                }
                return string.Empty;
            }
        }

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Binding.FindSerializedProperty();
            if (property == null)
                throw new InvalidOperationException("Ease property is unavailable.");

            var field = new EaseSelectorField(context.Label,
                IndexOf(InspectorHelpers.RequireRelative(property, "kind"),
                    InspectorHelpers.RequireRelative(property, "type")));
            var pencil = new InspectorIconButton { tooltip = "Edit curve" };
            var row = InspectorVisuals.CreateFieldActionRow(field, pencil);

            void Refresh()
            {
                var current = context.Binding.FindSerializedProperty();
                if (current == null) return;
                var kind = InspectorHelpers.RequireRelative(current, "kind");
                var type = InspectorHelpers.RequireRelative(current, "type");

                field.SetValueWithoutNotify(IndexOf(kind, type));
                field.showMixedValue = context.Binding.HasMultipleValues;

                var custom = (EaseKind)kind.intValue != EaseKind.Builtin;
                pencil.style.display = custom ? DisplayStyle.Flex : DisplayStyle.None;
                row.EnableInClassList("lightside-field-action-row--plain", !custom);
                pencil.SetState(false, EditorResources.ToggleAccent, "edit");
            }

            pencil.clicked += () =>
            {
                var current = context.Binding.FindSerializedProperty();
                if (current != null) EaseCurvePopupWindow.Show(pencil.worldBound, current);
            };

            field.RegisterValueChangedCallback(evt =>
            {
                var index = evt.newValue;
                if ((uint)index >= (uint)choices.Length) return;
                context.Edit(current => Apply(current, index), "Change Ease");
                Refresh();
            });

            return context.Observe(row, Refresh);
        }

        private static void Apply(SerializedProperty ease, int index)
        {
            var kind = InspectorHelpers.RequireRelative(ease, "kind");

            if (index < builtinNames.Length)
            {
                kind.intValue = (int)EaseKind.Builtin;
                InspectorHelpers.RequireRelative(ease, "type").intValue =
                    (int)(EasingType)builtinValues.GetValue(index);
            }
            else if (index == builtinNames.Length)
            {
                kind.intValue = (int)EaseKind.Cubic;
                var controls = InspectorHelpers.RequireRelative(ease, "controls");
                if (controls.vector4Value == UnityEngine.Vector4.zero)
                    controls.vector4Value = new UnityEngine.Vector4(0.25f, 0.1f, 0.25f, 1f);
            }
            else
            {
                kind.intValue = (int)EaseKind.Keyed;
                SeedKeys(InspectorHelpers.RequireRelative(ease, "keys"));
            }
        }

        private static int IndexOf(SerializedProperty kind, SerializedProperty type)
        {
            switch ((EaseKind)kind.intValue)
            {
                case EaseKind.Cubic:
                    return builtinNames.Length;
                case EaseKind.Keyed:
                    return builtinNames.Length + 1;
                default:
                    for (var i = 0; i < builtinValues.Length; i++)
                        if ((int)(EasingType)builtinValues.GetValue(i) == type.intValue)
                            return i;
                    return 0;
            }
        }

        /// <summary>Gives a curve switched on for the first time the two knots an editor can drag.</summary>
        private static void SeedKeys(SerializedProperty keys)
        {
            if (keys.arraySize >= 2) return;
            keys.arraySize = 2;
            Knot(keys.GetArrayElementAtIndex(0), 0f, 0f);
            Knot(keys.GetArrayElementAtIndex(1), 1f, 1f);
        }

        private static void Knot(SerializedProperty knot, float x, float y)
        {
            var point = new UnityEngine.Vector2(x, y);
            knot.FindPropertyRelative("position").vector2Value = point;
            knot.FindPropertyRelative("inHandle").vector2Value = point;
            knot.FindPropertyRelative("outHandle").vector2Value = point;
            knot.FindPropertyRelative("mode").intValue = (int)TangentMode.Aligned;
        }
    }
}
