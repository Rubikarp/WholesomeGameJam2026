using UnityEngine;

namespace LightSide
{
    /// <summary>Draws a <see cref="Vector2"/> as a draggable label handle (drag scrubs both axes, double-click resets) plus editable X/Y fields, in place of Unity's default two-field row.</summary>
    public sealed class VectorDragFieldAttribute : PropertyAttribute { }
}
