using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Backend-agnostic primitive sink for debug overlays. All coordinates are world-space;
    /// each backend (editor Handles, runtime GL) maps them to its target surface.
    /// </summary>
    public interface IDebugDraw
    {
        void Line(Vector3 a, Vector3 b, Color color, float thickness);
        void Box(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Color color, bool filled);
    }
}
