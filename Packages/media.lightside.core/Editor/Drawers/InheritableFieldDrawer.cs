using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Toolkit editor for <see cref="InheritableAttribute"/> fields. Numeric fields get a
    /// checkbox-plus-value row storing the NaN sentinel while unchecked; enum fields get the standard
    /// popup with the inheritance member offered, which plain enum fields hide.
    /// </summary>
    [CustomPropertyDrawer(typeof(InheritableAttribute))]
    internal sealed class InheritableFieldDrawer : LightSidePropertyDrawer<InheritableAttribute>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var inheritable = context.Binding.SerializedField
                                  .GetCustomAttribute<InheritableAttribute>() ??
                              throw new InvalidOperationException(
                                  $"Field '{context.Binding.SerializedField.Name}' has no inheritable attribute.");
            if (context.ValueType.IsEnum)
                return context.Bind(new EnumSelectorField(context.Label, (Enum)context.Binding.Value));
            if (context.ValueType == typeof(float))
            {
                var range = context.Binding.SerializedField.GetCustomAttribute<RangeAttribute>();
                BaseField<float> field = range == null
                    ? new FloatField(context.Label)
                    : new InspectorSlider(context.Label, range.min, range.max);
                return CreateRow(context, field,
                    value => !float.IsNaN((float)value),
                    () => inheritable.ActivationX,
                    () => float.NaN);
            }
            if (context.ValueType == typeof(Vector2))
                return CreateRow(context, new EditorVector2DragField(context.Label),
                    value =>
                    {
                        var vector = (Vector2)value;
                        return !float.IsNaN(vector.x) || !float.IsNaN(vector.y);
                    },
                    () => new Vector2(inheritable.ActivationX, inheritable.ActivationY),
                    () => new Vector2(float.NaN, float.NaN),
                    (current, previous, next) =>
                    {
                        if (!next.x.Equals(previous.x))
                            current.x = next.x;
                        if (!next.y.Equals(previous.y))
                            current.y = next.y;
                        return current;
                    });
            return new HelpBox(
                $"[Inheritable] does not support {context.ValueType.Name}.",
                HelpBoxMessageType.Error);
        }

        private static VisualElement CreateRow<T>(SerializedPropertyContext context,
            BaseField<T> field, Func<object, bool> isActive,
            Func<T> activationValue, Func<T> inheritedValue,
            Func<T, T, T, T> merge = null)
        {
            var toggle = new InspectorToggle { tooltip = "Override inherited value" };
            InspectorVisuals.MarkFieldAxis(field);
            var inherited = new Label("Inherited");
            var row = InspectorVisuals.CreateOverrideRow(toggle, field, inherited);

            void Refresh()
            {
                var targets = context.Binding.SerializedObject.targetObjects;
                var activeCount = 0;
                object firstActive = null;
                for (var i = 0; i < targets.Length; i++)
                {
                    var targetValue = context.Binding.GetValue(targets[i]);
                    if (!isActive(targetValue)) continue;
                    firstActive ??= targetValue;
                    activeCount++;
                }
                var anyActive = activeCount > 0;
                var mixedActivation = anyActive && activeCount < targets.Length;
                toggle.SetValueWithoutNotify(activeCount == targets.Length);
                toggle.showMixedValue = mixedActivation;
                field.style.display = anyActive ? DisplayStyle.Flex : DisplayStyle.None;
                inherited.style.display = anyActive ? DisplayStyle.None : DisplayStyle.Flex;
                if (anyActive) field.SetValueWithoutNotify((T)firstActive);
                field.showMixedValue = context.Binding.HasMultipleValues;
            }

            toggle.RegisterValueChangedCallback(evt =>
                context.Binding.TransformValue(value => evt.newValue
                    ? isActive(value) ? value : activationValue()
                    : inheritedValue()));
            field.RegisterValueChangedCallback(evt =>
            {
                if (merge == null)
                    context.SetValue(evt.newValue);
                else
                    context.Binding.TransformValue(value =>
                        merge((T)value, evt.previousValue, evt.newValue));
            });
            return context.Observe(row, Refresh);
        }
    }
}
