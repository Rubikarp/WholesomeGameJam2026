using UnityEngine;
using UnityEngine.UI;
namespace LightSide
{
    /// <summary>
    /// The outline family a <see cref="UniShape"/> renders. Resolved by an <c>IShapeProvider</c> and evaluated as
    /// a signed distance field in the shader, so every kind still anti-aliases and takes the shared fill / stroke
    /// / shadow layer stack. The numeric values are the shader-side dispatch ids and must stay in sync with the
    /// analytic library in <c>LightSideShapeField.hlsl</c>.
    /// <para>
    /// Every kind sizes itself from the shape bounds. Those that can fill any box undistorted
    /// (<see cref="RoundedRect"/>, <see cref="Ellipse"/>, <see cref="Capsule"/>, <see cref="Parallelogram"/>,
    /// <see cref="Trapezoid"/>, <see cref="Rhombus"/>) span them exactly; the rest keep
    /// their aspect, take the largest size that fits the part of them the shape's fit mode names, and leave the
    /// slack for the rect's pivot to split, the way Preserve Aspect does. Whatever the fit, each turns about its
    /// balance point — the centre its own construction radiates from. The circular kinds (<see cref="Circle"/>,
    /// <see cref="Pie"/>, <see cref="Arc"/>, <see cref="Ring"/>, <see cref="CutDisk"/>) size to the inscribed
    /// circle, so sweeping an aperture or a chord neither resizes nor moves them.
    /// </para>
    /// </summary>
    public enum ShapeKind
    {
        /// <summary>Rounded rectangle with independent per-corner radii and corner smoothing. The default.</summary>
        RoundedRect = 0,

        /// <summary>Circle of a single radius.</summary>
        Circle = 1,

        /// <summary>Axis-aligned ellipse filling the shape bounds.</summary>
        Ellipse = 2,

        /// <summary>Stadium / capsule: a rectangle with fully semicircular ends.</summary>
        Capsule = 3,

        /// <summary>Equilateral triangle.</summary>
        Triangle = 4,

        /// <summary>Regular pentagon.</summary>
        Pentagon = 5,

        /// <summary>Regular hexagon.</summary>
        Hexagon = 6,

        /// <summary>Regular octagon.</summary>
        Octagon = 7,

        /// <summary>N-pointed star with adjustable point count and sharpness.</summary>
        Star = 8,

        /// <summary>Pie slice / circular sector, swept between a start and an end angle.</summary>
        Pie = 9,

        /// <summary>Circular arc band of a given thickness, swept between a start and an end angle.</summary>
        Arc = 10,

        /// <summary>Annulus (ring): a circular outline of a given thickness.</summary>
        Ring = 11,

        /// <summary>Disk cut by a straight chord (e.g. a half-disk).</summary>
        CutDisk = 12,

        /// <summary>Parallelogram spanning the shape bounds, its sides leaning by a skew angle.</summary>
        Parallelogram = 13,

        /// <summary>Isosceles trapezoid spanning the shape bounds, its sides leaning inward by a taper angle; the narrow edge may close into a point — a taper past what the bounds hold is how a triangle that spans them is drawn.</summary>
        Trapezoid = 14,

        /// <summary>Rhombus whose four points span the shape bounds.</summary>
        Rhombus = 15,

        /// <summary>Plus sign of adjustable arm thickness, spanning the square the bounds hold; turned a quarter turn it is an X.</summary>
        Cross = 16,

        /// <summary>Heart, keeping its aspect within the shape bounds.</summary>
        Heart = 17,

        /// <summary>An arbitrary simple polygon whose vertices stream from the shape-vertex atlas. Produced by <c>VectorShapeProvider</c> from a closed path.</summary>
        [HideFromTypeSelector] Polygon = 18,

        /// <summary>An open chain of vertices stroked to a band of a given width — a line, streamed from the shape-vertex atlas. Produced by <c>VectorShapeProvider</c> from an open path.</summary>
        [HideFromTypeSelector] Polyline = 19,

        /// <summary>
        /// A boolean combination of other outlines, streamed from the shape-vertex atlas and folded left to right
        /// in the shader — still one distance field, so every layer reads the combined silhouette. Produced by
        /// <c>CompositeShapeProvider</c>; never nests inside another combination.
        /// </summary>
        [HideFromTypeSelector] Composite = 20,
    }
}
