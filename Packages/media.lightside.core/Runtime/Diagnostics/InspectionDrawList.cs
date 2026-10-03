using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Accumulates overlay primitives in the local space of a RectTransform, then transforms and emits
    /// them to an <see cref="IDebugDraw"/> on render. Building is gated by a caller-computed content
    /// signature, so geometry passes run only when the signature changes — not every frame. Rendering
    /// re-transforms cached local coordinates, so moving the target needs no rebuild.
    /// </summary>
    public sealed class InspectionDrawList
    {
        private struct LinePrim { public Vector2 a, b; public Color c; public float thickness; }
        private struct RectPrim { public Rect r; public Color c; public bool filled; }

        private readonly List<LinePrim> lines = new(64);
        private readonly List<RectPrim> rects = new(64);
        private RectTransform rt;
        private int signature;
        private bool built;

        public bool NeedsRebuild(int sig) => !built || sig != signature;

        public void Begin(RectTransform transform, int sig)
        {
            rt = transform;
            signature = sig;
            built = true;
            lines.Clear();
            rects.Clear();
        }

        public void Invalidate() => built = false;

        public void Line(Vector2 a, Vector2 b, Color c, float thickness = 2f) => lines.Add(new LinePrim { a = a, b = b, c = c, thickness = thickness });

        public void Rect(Rect r, Color c, bool filled) => rects.Add(new RectPrim { r = r, c = c, filled = filled });

        public void Render(IDebugDraw draw)
        {
            if (rt == null || draw == null) return;

            for (var i = 0; i < rects.Count; i++)
            {
                var p = rects[i];
                draw.Box(P(p.r.xMin, p.r.yMin), P(p.r.xMax, p.r.yMin), P(p.r.xMax, p.r.yMax), P(p.r.xMin, p.r.yMax), p.c, p.filled);
            }

            for (var i = 0; i < lines.Count; i++)
            {
                var p = lines[i];
                draw.Line(P(p.a.x, p.a.y), P(p.b.x, p.b.y), p.c, p.thickness);
            }
        }

        private Vector3 P(float x, float y) => rt.TransformPoint(new Vector3(x, y, 0f));
    }
}
