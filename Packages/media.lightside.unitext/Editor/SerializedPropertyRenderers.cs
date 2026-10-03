using System;
using System.Reflection;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal static class UniTextSerializedPropertyRenderers
    {
        [InitializeOnLoadMethod]
        private static void Register()
        {
            SerializedPropertyField.RegisterRenderer<UnitValue>(CreateUnitValue);
            SerializedPropertyField.RegisterRenderer<UnitVector2>(CreateUnitVector2);
            SerializedPropertyField.RegisterRenderer<IMarkupValue>(CreateMarkupValue);
            SerializedPropertyField.RegisterAttributeHeaderRenderer<DefaultParameterAttribute>(
                DefaultParameterDrawer.CreateToolkitHeaderAction);
        }

        private static VisualElement CreateUnitValue(SerializedPropertyContext context)
            => CreateUnitValue(context, null);

        /// <summary>Renders a UnitValue with an explicit unit vocabulary; null reads the field's own <see cref="UnitAttribute"/>.</summary>
        internal static VisualElement CreateUnitValue(SerializedPropertyContext context, string units)
        {
            var property = context.Property;
            var valueProperty = InspectorHelpers.RequireRelative(property, "value");
            var unitProperty = InspectorHelpers.RequireRelative(property, "unit");
            var unitBinding = new SerializedPropertyBinding(unitProperty);
            var row = CreateUnitRow();
            var number = SerializedPropertyField.Create(valueProperty, context.Label);
            var unitValue = (UnitKind)unitBinding.Value;
            var names = UnitChoices(units ?? UnitsOf(context), unitValue);
            var unit = CreateUnitField(names, unitValue);
            number.AddToClassList("unitext-inspector-field-row__value");
            unit.AddToClassList("unitext-inspector-field-row__unit");
            row.Add(number);
            row.Add(new SerializedPropertyContext(
                    unitBinding, unitProperty, unitProperty.displayName)
                .Bind(unit, name => UnitNames.Kind(name),
                    read: current => ParameterFieldUtility.NameFor((UnitKind)current, names)));
            return row;
        }

        private static VisualElement CreateUnitVector2(SerializedPropertyContext context)
            => CreateUnitVector2(context, null);

        /// <summary>Renders a UnitVector2 with an explicit unit vocabulary; null reads the field's own <see cref="UnitAttribute"/>.</summary>
        internal static VisualElement CreateUnitVector2(SerializedPropertyContext context, string units)
        {
            var property = context.Property;
            var valueProperty = InspectorHelpers.RequireRelative(property, "value");
            var unitProperty = InspectorHelpers.RequireRelative(property, "unit");
            var valueBinding = new SerializedPropertyBinding(valueProperty);
            var unitBinding = new SerializedPropertyBinding(unitProperty);
            var row = CreateUnitRow();
            var vector = new SerializedPropertyContext(
                    valueBinding, valueProperty, context.Label)
                .Bind(new EditorVector2DragField(context.Label),
                    merge: (current, previous, next) =>
                    {
                        var value = (UnityEngine.Vector2)current;
                        if (!next.x.Equals(previous.x)) value.x = next.x;
                        if (!next.y.Equals(previous.y)) value.y = next.y;
                        return value;
                    });
            var unitValue = (UnitKind)unitBinding.Value;
            var names = UnitChoices(units ?? UnitsOf(context), unitValue);
            var unit = CreateUnitField(names, unitValue);
            vector.AddToClassList("unitext-inspector-field-row__value");
            unit.AddToClassList("unitext-inspector-field-row__unit");
            row.Add(vector);
            row.Add(new SerializedPropertyContext(
                    unitBinding, unitProperty, unitProperty.displayName)
                .Bind(unit, name => UnitNames.Kind(name),
                    read: current => ParameterFieldUtility.NameFor((UnitKind)current, names)));
            return row;
        }

        private static VisualElement CreateMarkupValue(SerializedPropertyContext context)
        {
            var field = new TextField(context.Label);
            return context.Bind(field, token =>
                {
                    var next = (IMarkupValue)Activator.CreateInstance(context.ValueType);
                    next.FromToken(token);
                    return next;
                },
                read: current => ((IMarkupValue)current).ToToken());
        }

        private static VisualElement CreateUnitRow()
        {
            var row = InspectorVisuals.CreateCompactRow();
            UniTextInspectorTheme.Initialize(row);
            return row;
        }

        private static string UnitsOf(SerializedPropertyContext context)
            => context.Binding.SerializedField?.GetCustomAttribute<UnitAttribute>()?.Units;

        private static string[] UnitChoices(string units, UnitKind value)
        {
            var names = ParameterFieldUtility.GetUnitNames(units);
            return names.Length == 0 ? new[] { UnitNames.Name(value) } : names;
        }

        private static SelectorField<string> CreateUnitField(string[] names, UnitKind value)
        {
            var index = ParameterFieldUtility.FindUnitIndex(names, value);
            return new SelectorField<string>(names,
                index < 0 ? 0 : index);
        }

    }
}
