using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Vector transport icon drawn in the inherited text colour, so the host button's accent and
    /// its pressed-state inversion style the glyph without a second colour channel.
    /// </summary>
    public sealed class InspectorTransportGlyph : VisualElement
    {
        /// <summary>Transport symbol the glyph draws.</summary>
        public enum Shape : byte
        {
            Play,
            Pause,
            Stop,
        }

        private Shape shape;

        public InspectorTransportGlyph(Shape shape = Shape.Play)
        {
            this.shape = shape;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>Sets the drawn symbol.</summary>
        public void Set(Shape value)
        {
            if (shape == value) return;
            shape = value;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= 0f || rect.height <= 0f) return;
            var painter = context.painter2D;
            painter.fillColor = resolvedStyle.color;
            var cx = rect.center.x;
            var cy = rect.center.y;
            var s = Mathf.Min(rect.width, rect.height) * 0.22f;
            painter.BeginPath();
            switch (shape)
            {
                case Shape.Play:
                    painter.MoveTo(new Vector2(cx - s * 0.75f, cy - s));
                    painter.LineTo(new Vector2(cx + s, cy));
                    painter.LineTo(new Vector2(cx - s * 0.75f, cy + s));
                    painter.ClosePath();
                    break;
                case Shape.Pause:
                    var barWidth = s * 0.65f;
                    var gap = s * 0.4f;
                    painter.MoveTo(new Vector2(cx - gap - barWidth, cy - s));
                    painter.LineTo(new Vector2(cx - gap, cy - s));
                    painter.LineTo(new Vector2(cx - gap, cy + s));
                    painter.LineTo(new Vector2(cx - gap - barWidth, cy + s));
                    painter.ClosePath();
                    painter.MoveTo(new Vector2(cx + gap, cy - s));
                    painter.LineTo(new Vector2(cx + gap + barWidth, cy - s));
                    painter.LineTo(new Vector2(cx + gap + barWidth, cy + s));
                    painter.LineTo(new Vector2(cx + gap, cy + s));
                    painter.ClosePath();
                    break;
                default:
                    painter.MoveTo(new Vector2(cx - s, cy - s));
                    painter.LineTo(new Vector2(cx + s, cy - s));
                    painter.LineTo(new Vector2(cx + s, cy + s));
                    painter.LineTo(new Vector2(cx - s, cy + s));
                    painter.ClosePath();
                    break;
            }
            painter.Fill();
        }

        /// <summary>
        /// Creates the shared transport button: house accent chrome, square, and a centred glyph
        /// that follows the button's colour through hover and pressed states.
        /// </summary>
        public static Button CreateButton(Shape shape, string tooltip, System.Action clicked,
            out InspectorTransportGlyph glyph)
        {
            var button = new Button(clicked) { tooltip = tooltip };
            button.AddToClassList("lightside-button--accent");
            button.AddToClassList("lightside-transport-button");
            glyph = new InspectorTransportGlyph(shape);
            glyph.style.flexGrow = 1f;
            button.Add(glyph);
            return button;
        }
    }
}
