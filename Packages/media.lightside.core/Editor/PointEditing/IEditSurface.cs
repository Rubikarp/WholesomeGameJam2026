using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The surface seam for <see cref="PointEditor"/>: maps between world space and the 2-D working plane that
    /// the modal transform and box-select operate in, and answers screen-space hit tests. A SceneView
    /// implementation is <see cref="SceneEditSurface"/>; a 2-D graph or node canvas can supply its own.
    /// </summary>
    public interface IEditSurface
    {
        /// <summary>The current pointer position projected into the working plane.</summary>
        Vector2 PointerPlane { get; }

        /// <summary>Projects a world position onto the working plane.</summary>
        Vector2 WorldToPlane(Vector3 world);

        /// <summary>Lifts a working-plane position back into world space.</summary>
        Vector3 PlaneToWorld(Vector2 plane);

        /// <summary>
        /// Screen-space distance in pixels from the current pointer to <paramref name="world"/>. Used for
        /// zoom-independent point picking (a fixed pixel radius regardless of camera distance or zoom).
        /// </summary>
        float PointerScreenDistance(Vector3 world);

        /// <summary>
        /// Draws the box-select marquee between two working-plane corners in the surface's active
        /// presentation pass.
        /// </summary>
        void DrawMarquee(Vector2 planeA, Vector2 planeB, Color fill, Color outline);

        /// <summary>
        /// Strokes a cubic Bézier through four working-plane control points, <paramref name="widthPixels"/>
        /// screen pixels wide, in the surface's active presentation pass.
        /// </summary>
        void DrawCurve(Vector2 from, Vector2 fromHandle, Vector2 toHandle, Vector2 to, Color color,
            float widthPixels);

        /// <summary>Strokes a straight line between two working-plane points, <paramref name="widthPixels"/> screen pixels wide.</summary>
        void DrawLine(Vector2 planeA, Vector2 planeB, Color color, float widthPixels);

        /// <summary>Fills a disc of <paramref name="radiusPixels"/> screen pixels about a working-plane point — a size that holds at any zoom or camera distance.</summary>
        void DrawDot(Vector2 plane, float radiusPixels, Color color);
    }
}
