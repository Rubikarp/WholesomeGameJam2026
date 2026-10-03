using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Presentation of a Bézier knot chain — the shared renderer behind every knot editor, whatever container
    /// the knots came from and whatever surface they are drawn on. Sizes are screen pixels, so a knot reads the
    /// same in a canvas and in the SceneView at any zoom.
    /// </summary>
    public static class BezierKnotVisuals
    {
        private const float AnchorRadius = 4.5f;
        private const float HandleRadius = 3f;
        private const float HandleLineWidth = 1.5f;

        /// <summary>Colours and toggles of one presentation pass.</summary>
        public struct Options
        {
            public bool showHandles;
            public Color curveColor;
            public Color anchorColor;
            public Color handleColor;
            public Color selectedColor;
            public float curveWidth;

            /// <summary>Whether the chain itself is stroked; a host that paints its own curve turns this off and keeps the knots.</summary>
            public bool showCurve;

            /// <summary>Whether the first knot's incoming handle and the last one's outgoing handle are drawn.</summary>
            /// <remarks>They steer nothing on an open chain; a timing curve hides them, a closed path needs them.</remarks>
            public bool showOuterHandles;

            public static Options Default => Accented(EditorResources.ToggleAccent, EditorResources.ToggleAccent);
        }

        /// <summary>
        /// The project's knot palette: <paramref name="accent"/> carries the curve and the handles, a selected
        /// point takes <paramref name="hover"/>, and anchors stand apart in the icon colour. Every knot editor
        /// opens from here, so one accent restyles them all together.
        /// </summary>
        public static Options Accented(Color accent, Color hover) => new()
        {
            showHandles = false,
            curveColor = accent,
            anchorColor = EditorResources.IconColor,
            handleColor = new Color(accent.r, accent.g, accent.b, 0.6f),
            selectedColor = hover,
            curveWidth = 2f,
            showCurve = true,
            showOuterHandles = true,
        };

        /// <summary>Marquee fill that belongs with the palette <paramref name="accent"/> steers.</summary>
        public static Color MarqueeFill(Color accent) => new(accent.r, accent.g, accent.b, 0.14f);

        /// <summary>Marquee outline that belongs with the palette <paramref name="accent"/> steers.</summary>
        public static Color MarqueeOutline(Color accent) => new(accent.r, accent.g, accent.b, 0.55f);

        /// <summary>
        /// Draws the chain: each segment's curve, then the handles, then the anchors. Knots are read in the space
        /// <paramref name="toPlane"/> maps to world, which the surface projects onto its own plane — identity where
        /// the two already agree. Point indices follow the three-per-knot layout (<c>3k</c> anchor, <c>3k+1</c> in,
        /// <c>3k+2</c> out) that <see cref="PointSelection"/> is expected to hold.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="surface"/> is <see langword="null"/>.</exception>
        public static void Draw(IEditSurface surface, IReadOnlyList<BezierKnot> knots, bool closed,
            PointSelection selection, Options options, Matrix4x4 toPlane)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            var count = knots?.Count ?? 0;
            if (count == 0) return;

            Vector2 Place(Vector2 point)
                => surface.WorldToPlane(toPlane.MultiplyPoint3x4(point));

            if (options.showCurve)
            {
                var segments = closed ? count : count - 1;
                for (var segment = 0; segment < segments; segment++)
                {
                    var a = knots[segment];
                    var b = knots[(segment + 1) % count];
                    surface.DrawCurve(Place(a.position), Place(a.outHandle), Place(b.inHandle),
                        Place(b.position), options.curveColor, options.curveWidth);
                }
            }

            if (options.showHandles)
                for (var index = 0; index < count; index++)
                {
                    var knot = knots[index];
                    var anchor = Place(knot.position);
                    if (Lives(index, HandleSide.In, count, closed, options) &&
                        (knot.inHandle - knot.position).sqrMagnitude > 1e-6f)
                        surface.DrawLine(anchor, Place(knot.inHandle), options.handleColor, HandleLineWidth);
                    if (Lives(index, HandleSide.Out, count, closed, options) &&
                        (knot.outHandle - knot.position).sqrMagnitude > 1e-6f)
                        surface.DrawLine(anchor, Place(knot.outHandle), options.handleColor, HandleLineWidth);
                }

            for (var index = 0; index < count; index++)
            {
                var knot = knots[index];
                if (options.showHandles)
                {
                    if (Lives(index, HandleSide.In, count, closed, options))
                        surface.DrawDot(Place(knot.inHandle), HandleRadius,
                            Tint(selection, index * 3 + 1, options.handleColor, options));
                    if (Lives(index, HandleSide.Out, count, closed, options))
                        surface.DrawDot(Place(knot.outHandle), HandleRadius,
                            Tint(selection, index * 3 + 2, options.handleColor, options));
                }
                surface.DrawDot(Place(knot.position), AnchorRadius,
                    Tint(selection, index * 3, options.anchorColor, options));
            }
        }

        /// <summary>Whether a handle steers a segment that exists.</summary>
        public static bool Lives(int knotIndex, HandleSide side, int count, bool closed)
            => closed || (side == HandleSide.In ? knotIndex > 0 : knotIndex < count - 1);

        private static Color Tint(PointSelection selection, int point, Color color, Options options)
            => selection != null && selection.Contains(point) ? options.selectedColor : color;

        private static bool Lives(int knotIndex, HandleSide side, int count, bool closed, Options options)
            => options.showOuterHandles || Lives(knotIndex, side, count, closed);
    }
}
