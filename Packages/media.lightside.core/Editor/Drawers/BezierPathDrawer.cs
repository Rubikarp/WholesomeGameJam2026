using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Draws any serialized <see cref="BezierPath"/> with the project's styled list for its knots (instead of
    /// Unity's raw array UI) plus a <b>Closed</b> toggle. Applies wherever a <see cref="BezierPath"/> is shown as a
    /// property — e.g. a Traced Shape asset's contour. Drawers that intentionally hide the path (the in-scene vector
    /// editor) simply don't render the property, so they are unaffected.
    /// </summary>
    [CustomPropertyDrawer(typeof(BezierPath))]
    internal sealed class BezierPathDrawer : LightSidePropertyDrawer<BezierPath>
    {
        private const string ClosedTooltip =
            "Whether the last knot connects back to the first.";

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Property;
            var knots = InspectorHelpers.RequireRelative(property, "knots");
            var closed = InspectorHelpers.RequireRelative(property, "closed");
            var knotsBinding = new SerializedPropertyBinding(knots);
            var root = InspectorVisuals.CreateStack();
            var listElement = SerializedPropertyField.Create(knots, context.Label);
            if (listElement is not InspectorListView list)
                throw new System.InvalidOperationException(
                    $"Bezier knots '{knots.propertyPath}' did not create a list view.");
            var closedField = SerializedPropertyField.Create(closed, "Closed");
            closedField.tooltip = ClosedTooltip;
            root.Add(list);
            root.Add(closedField);

            void Refresh()
            {
                var current = knotsBinding.FindSerializedProperty() ??
                              throw new System.InvalidOperationException(
                                  $"Bezier knots '{knots.propertyPath}' are unavailable.");
                InspectorMotion.SetExpanded(closedField, current.isExpanded);
            }

            list.ExpandedChanged += _ => Refresh();
            Refresh();
            return root;
        }

    }
}
