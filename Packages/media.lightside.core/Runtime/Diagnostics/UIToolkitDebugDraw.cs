using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Projects world-space debug primitives into a UI Toolkit panel and emits them through its
    /// current <see cref="Painter2D"/> pass.
    /// </summary>
    public sealed class UIToolkitDebugDraw : IDebugDraw
    {
        private Painter2D painter;
        private Camera camera;
        private float panelHeight;

        /// <summary>Connects this sink to the current vector-paint pass.</summary>
        public void Begin(Painter2D target, Camera projectionCamera, float height)
        {
            painter = target ?? throw new ArgumentNullException(nameof(target));
            camera = projectionCamera;
            panelHeight = height;
        }

        /// <summary>Releases references to the completed paint pass.</summary>
        public void End()
        {
            painter = null;
            camera = null;
            panelHeight = 0f;
        }

        /// <inheritdoc/>
        public void Line(Vector3 a, Vector3 b, Color color, float thickness)
        {
            painter.strokeColor = color;
            painter.lineWidth = thickness;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            painter.MoveTo(ToPanel(a));
            painter.LineTo(ToPanel(b));
            painter.Stroke();
        }

        /// <inheritdoc/>
        public void Box(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Color color, bool filled)
        {
            painter.BeginPath();
            painter.MoveTo(ToPanel(bl));
            painter.LineTo(ToPanel(br));
            painter.LineTo(ToPanel(tr));
            painter.LineTo(ToPanel(tl));
            painter.ClosePath();
            if (filled)
            {
                painter.fillColor = color;
                painter.Fill();
            }
            else
            {
                painter.strokeColor = color;
                painter.lineWidth = 2f;
                painter.Stroke();
            }
        }

        private Vector2 ToPanel(Vector3 world)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(camera, world);
            return new Vector2(screen.x, panelHeight - screen.y);
        }
    }
}
