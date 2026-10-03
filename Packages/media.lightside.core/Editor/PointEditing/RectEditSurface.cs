using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Maps a two-dimensional data space to a UI Toolkit canvas while preserving aspect ratio. The owning
    /// element supplies pointer coordinates and a painter for each visual pass.
    /// </summary>
    public sealed class RectEditSurface : IEditSurface
    {
        private Rect content;
        private Rect bounds;
        private Vector2 scale = Vector2.one;
        private Vector2 pointer;
        private Painter2D painter;

        /// <summary>
        /// Fits the edited data bounds into the Toolkit view with an inset margin. With
        /// <paramref name="preserveAspect"/> both axes share one scale and the content centres in the view;
        /// without it each axis stretches to fill — the shape a normalized function plot wants.
        /// </summary>
        public void SetView(Rect view, Rect dataBounds, float margin = 12f, bool preserveAspect = true)
        {
            var inner = new Rect(view.x + margin, view.y + margin,
                Mathf.Max(1f, view.width - 2f * margin),
                Mathf.Max(1f, view.height - 2f * margin));
            var width = Mathf.Max(1e-4f, dataBounds.width);
            var height = Mathf.Max(1e-4f, dataBounds.height);
            scale = new Vector2(inner.width / width, inner.height / height);
            if (preserveAspect)
            {
                var uniform = Mathf.Min(scale.x, scale.y);
                scale = new Vector2(uniform, uniform);
            }
            var contentWidth = width * scale.x;
            var contentHeight = height * scale.y;
            content = new Rect(
                inner.x + (inner.width - contentWidth) * 0.5f,
                inner.y + (inner.height - contentHeight) * 0.5f,
                contentWidth,
                contentHeight);
            bounds = dataBounds;
        }

        /// <summary>The fitted content rectangle in canvas-local points.</summary>
        public Rect Content => content;

        /// <summary>Canvas points per data unit, per axis.</summary>
        public Vector2 Scale => scale;

        /// <summary>Maps a data-space point to canvas-local points with the vertical axis flipped.</summary>
        public Vector2 DataToGui(Vector2 point) =>
            new(content.x + (point.x - bounds.xMin) * scale.x,
                content.y + (bounds.yMax - point.y) * scale.y);

        /// <summary>Maps a canvas-local point back to data space.</summary>
        public Vector2 GuiToData(Vector2 point) =>
            new(bounds.xMin + (point.x - content.x) / scale.x,
                bounds.yMax - (point.y - content.y) / scale.y);

        /// <inheritdoc/>
        public Vector2 PointerPlane => GuiToData(pointer);

        /// <inheritdoc/>
        public Vector2 WorldToPlane(Vector3 world) => new(world.x, world.y);

        /// <inheritdoc/>
        public Vector3 PlaneToWorld(Vector2 plane) => new(plane.x, plane.y, 0f);

        /// <inheritdoc/>
        public float PointerScreenDistance(Vector3 world) =>
            Vector2.Distance(DataToGui(new Vector2(world.x, world.y)), pointer);

        /// <inheritdoc/>
        public void DrawMarquee(Vector2 planeA, Vector2 planeB, Color fill, Color outline)
        {
            var painter = RequirePainter();
            var a = DataToGui(planeA);
            var b = DataToGui(planeB);
            var rect = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            painter.BeginPath();
            painter.MoveTo(rect.min);
            painter.LineTo(new Vector2(rect.xMax, rect.yMin));
            painter.LineTo(rect.max);
            painter.LineTo(new Vector2(rect.xMin, rect.yMax));
            painter.ClosePath();
            painter.fillColor = fill;
            painter.Fill();
            painter.strokeColor = outline;
            painter.lineWidth = 1f;
            painter.Stroke();
        }

        /// <inheritdoc/>
        public void DrawCurve(Vector2 from, Vector2 fromHandle, Vector2 toHandle, Vector2 to, Color color,
            float widthPixels)
        {
            var painter = RequirePainter();
            painter.strokeColor = color;
            painter.lineWidth = widthPixels;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(DataToGui(from));
            painter.BezierCurveTo(DataToGui(fromHandle), DataToGui(toHandle), DataToGui(to));
            painter.Stroke();
        }

        /// <inheritdoc/>
        public void DrawLine(Vector2 planeA, Vector2 planeB, Color color, float widthPixels)
        {
            var painter = RequirePainter();
            painter.strokeColor = color;
            painter.lineWidth = widthPixels;
            painter.BeginPath();
            painter.MoveTo(DataToGui(planeA));
            painter.LineTo(DataToGui(planeB));
            painter.Stroke();
        }

        /// <inheritdoc/>
        public void DrawDot(Vector2 plane, float radiusPixels, Color color)
        {
            var painter = RequirePainter();
            painter.BeginPath();
            painter.Arc(DataToGui(plane), radiusPixels, 0f, 360f);
            painter.fillColor = color;
            painter.Fill();
        }

        private Painter2D RequirePainter()
            => painter ?? throw new InvalidOperationException(
                "Rect edit surface drawing requires an active Painter2D pass.");

        /// <summary>Sets the pointer position in canvas-local points; the owning element feeds this from each pointer event before dispatching it.</summary>
        public void SetPointer(Vector2 value) => pointer = value;

        /// <summary>Opens a paint pass, outside of which <see cref="DrawMarquee"/> throws. Pair every call with <see cref="EndDraw"/>.</summary>
        public void BeginDraw(Painter2D value)
        {
            painter = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>Closes the paint pass opened by <see cref="BeginDraw"/>.</summary>
        public void EndDraw() => painter = null;
    }
}
