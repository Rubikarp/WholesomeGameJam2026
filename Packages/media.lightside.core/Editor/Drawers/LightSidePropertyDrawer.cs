using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Exposes a drawer's selector and control to the shared serialized-field pipeline.</summary>
    public interface ILightSideDrawer
    {
        /// <summary>The serialized value type, or the field attribute, this drawer is selected by.</summary>
        Type SelectedBy { get; }

        /// <summary>Builds the control for one serialized field.</summary>
        VisualElement CreateControl(SerializedPropertyContext context);
    }

    /// <summary>
    /// Routes Unity's drawer entry point into the LightSide field pipeline and contributes nothing else.
    /// Derive from it where the control is already registered by other means; derive from
    /// <see cref="LightSidePropertyDrawer{TValue}"/> where this drawer owns the control.
    /// </summary>
    public abstract class LightSidePropertyBridge : PropertyDrawer
    {
        /// <summary>
        /// Renders the field through the pipeline. Override only to render a different property than the
        /// one Unity passes — a wrapper that presents its inner collection, for instance.
        /// </summary>
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
            => SerializedPropertyField.Create(property, property.displayName);
    }

    /// <summary>
    /// Base for a drawer whose control the LightSide field pipeline builds. Deriving from it registers the
    /// drawer and supplies Unity's own entry point, so a drawer cannot be reachable from one and not the other.
    /// </summary>
    /// <typeparam name="TValue">
    /// What selects this drawer: a serialized value type, or a <see cref="PropertyAttribute"/> carried by the
    /// field.
    /// </typeparam>
    /// <remarks>
    /// Unity's path and the pipeline's path are disjoint and both are load-bearing.
    /// <see cref="SerializedPropertyField.Create(SerializedProperty,string)"/> dispatches through its own
    /// registries and never consults Unity's drawer table, while a raw <c>PropertyField</c> never reaches the
    /// pipeline. Registering one without the other renders the same type two different ways depending on who
    /// drew it, which is why the bridge here is sealed rather than left to each drawer to remember.
    /// </remarks>
    public abstract class LightSidePropertyDrawer<TValue> : LightSidePropertyBridge, ILightSideDrawer
    {
        /// <inheritdoc/>
        public sealed override VisualElement CreatePropertyGUI(SerializedProperty property)
            => base.CreatePropertyGUI(property);

        /// <summary>Builds the control for one serialized field.</summary>
        protected abstract VisualElement CreateToolkit(SerializedPropertyContext context);

        Type ILightSideDrawer.SelectedBy => typeof(TValue);

        VisualElement ILightSideDrawer.CreateControl(SerializedPropertyContext context) => CreateToolkit(context);
    }

    /// <summary>
    /// Registers every <see cref="LightSidePropertyDrawer{TValue}"/> with the field pipeline at editor load,
    /// and refuses a LightSide drawer that declares Unity's attribute without deriving from it.
    /// </summary>
    internal static class LightSideDrawerDiscovery
    {
        private const string Prefix = "LightSide.";

        [InitializeOnLoadMethod]
        private static void Register()
        {
            Guard();
            var registrations = new List<(Type selector,
                Func<SerializedPropertyContext, VisualElement> renderer)>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<LightSidePropertyBridge>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                if (!typeof(ILightSideDrawer).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException(
                        $"LightSide property drawer '{type.FullName}' requires a public parameterless constructor.");

                var drawer = (ILightSideDrawer)Activator.CreateInstance(type);
                var selector = drawer.SelectedBy ?? throw new InvalidOperationException(
                    $"LightSide property drawer '{type.FullName}' returned no selector.");
                registrations.Add((selector, drawer.CreateControl));
            }

            foreach (var (selector, renderer) in registrations)
            {
                if (typeof(PropertyAttribute).IsAssignableFrom(selector))
                    SerializedPropertyField.RegisterAttributeRenderer(selector, renderer);
                else
                    SerializedPropertyField.RegisterRenderer(selector, renderer);
            }
        }

        /// <summary>
        /// Fails the editor load on any LightSide drawer that Unity can reach but the pipeline cannot.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A type in a LightSide assembly carries <see cref="CustomPropertyDrawer"/> without deriving from
        /// <see cref="LightSidePropertyBridge"/>.
        /// </exception>
        private static void Guard()
        {
            string offenders = null;

            foreach (var type in TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>())
            {
                if (type.IsAbstract) continue;
                if (!type.Assembly.GetName().Name.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                if (typeof(LightSidePropertyBridge).IsAssignableFrom(type)) continue;

                offenders = offenders == null ? type.FullName : offenders + ", " + type.FullName;
            }

            if (offenders != null)
                throw new InvalidOperationException(
                    "These drawers carry [CustomPropertyDrawer] without deriving from LightSidePropertyDrawer<> " +
                    "or LightSidePropertyBridge, so Unity reaches them while the LightSide field pipeline " +
                    "does not, and the same type renders two different ways depending on who drew it: " +
                    offenders);
        }
    }
}
