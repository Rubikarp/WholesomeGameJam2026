using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>Editor <see cref="IDebugDraw"/> drawing world-space primitives with <see cref="Handles"/> in the SceneView.</summary>
    public sealed class HandlesDebugDraw : IDebugDraw
    {
        /// <inheritdoc/>
        public void Line(Vector3 a, Vector3 b, Color color, float thickness)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(thickness, a, b);
        }

        /// <inheritdoc/>
        public void Box(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Color color, bool filled)
        {
            Handles.color = color;
            if (filled)
                Handles.DrawAAConvexPolygon(bl, br, tr, tl);
            else
                Handles.DrawAAPolyLine(2.5f, bl, br, tr, tl, bl);
        }
    }
}
