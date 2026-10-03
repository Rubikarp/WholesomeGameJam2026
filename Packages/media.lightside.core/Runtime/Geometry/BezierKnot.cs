using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>How a knot's two Bézier handles relate to each other.</summary>
    public enum TangentMode
    {
        /// <summary>Handles move independently (broken corner).</summary>
        Free,

        /// <summary>Handles stay collinear through the anchor; the opposite keeps its own length.</summary>
        Aligned,

        /// <summary>Handles stay collinear and equal length (a smooth symmetric knot).</summary>
        Mirrored,

        /// <summary>Handles are derived automatically from the neighbouring anchors (Catmull-Rom-like).</summary>
        Auto,

        /// <summary>Handles point at the neighbouring anchors (straight-ish segments).</summary>
        Vector,
    }

    /// <summary>Which of a knot's two handles is being referred to.</summary>
    public enum HandleSide
    {
        In,
        Out,
    }

    /// <summary>One control point of a Bézier chain: an on-curve anchor plus its incoming and
    /// outgoing Bézier handles (stored as absolute positions in the chain's own space) and the mode
    /// that relates them.</summary>
    [Serializable]
    public struct BezierKnot
    {
        /// <summary>On-curve anchor position.</summary>
        public Vector2 position;

        /// <summary>Incoming handle (controls the segment arriving at this knot), absolute.</summary>
        public Vector2 inHandle;

        /// <summary>Outgoing handle (controls the segment leaving this knot), absolute.</summary>
        public Vector2 outHandle;

        /// <summary>How the two handles are constrained to each other.</summary>
        public TangentMode mode;

        /// <summary>A knot with both handles collapsed onto the anchor (a sharp corner), in <see cref="TangentMode.Aligned"/>.</summary>
        public BezierKnot(Vector2 position)
        {
            this.position = position;
            inHandle = position;
            outHandle = position;
            mode = TangentMode.Aligned;
        }

        public BezierKnot(Vector2 position, Vector2 inHandle, Vector2 outHandle, TangentMode mode)
        {
            this.position = position;
            this.inHandle = inHandle;
            this.outHandle = outHandle;
            this.mode = mode;
        }
    }

    /// <summary>Knot constraint and subdivision math shared by every Bézier knot container and editor.</summary>
    public static class BezierKnotEdit
    {
        /// <summary>
        /// Re-establishes the collinearity constraint after the <paramref name="driving"/> handle moved:
        /// the opposite handle is placed on the driving handle's line through the anchor, keeping its own
        /// length when <paramref name="keepLength"/>, mirroring the driving length otherwise. A driving
        /// handle collapsed onto its anchor leaves the knot unchanged.
        /// </summary>
        public static void AlignOpposite(ref BezierKnot k, HandleSide driving, bool keepLength)
        {
            Vector2 drive = (driving == HandleSide.Out ? k.outHandle : k.inHandle) - k.position;
            if (drive.sqrMagnitude < 1e-12f) return;

            Vector2 opposite;
            if (keepLength)
            {
                float oppLen = ((driving == HandleSide.Out ? k.inHandle : k.outHandle) - k.position).magnitude;
                opposite = k.position - drive.normalized * oppLen;
            }
            else
            {
                opposite = k.position - drive;
            }

            if (driving == HandleSide.Out) k.inHandle = opposite;
            else k.outHandle = opposite;
        }

        /// <summary>Derives both handles from the neighbouring anchors (Catmull-Rom-like), for <see cref="TangentMode.Auto"/>.</summary>
        public static void Auto(ref BezierKnot k, Vector2 previousAnchor, Vector2 nextAnchor)
        {
            Vector2 tangent = (nextAnchor - previousAnchor) * (1f / 6f);
            k.outHandle = k.position + tangent;
            k.inHandle = k.position - tangent;
        }

        /// <summary>Points each handle a third of the way to its neighbouring anchor, for <see cref="TangentMode.Vector"/>.</summary>
        public static void Vector(ref BezierKnot k, Vector2 previousAnchor, Vector2 nextAnchor)
        {
            k.outHandle = k.position + (nextAnchor - k.position) / 3f;
            k.inHandle = k.position + (previousAnchor - k.position) / 3f;
        }

        /// <summary>
        /// Splits the segment between <paramref name="a"/> and <paramref name="b"/> at parameter
        /// <paramref name="t"/> without changing the curve's shape (De Casteljau), returning the new middle
        /// knot. Both flanking knots' facing handles are trimmed and their modes become
        /// <see cref="TangentMode.Free"/> so the recomputed handles are preserved exactly.
        /// </summary>
        public static BezierKnot Split(ref BezierKnot a, ref BezierKnot b, float t)
        {
            Vector2 q0 = Vector2.Lerp(a.position, a.outHandle, t);
            Vector2 q1 = Vector2.Lerp(a.outHandle, b.inHandle, t);
            Vector2 q2 = Vector2.Lerp(b.inHandle, b.position, t);
            Vector2 r0 = Vector2.Lerp(q0, q1, t);
            Vector2 r1 = Vector2.Lerp(q1, q2, t);
            Vector2 s = Vector2.Lerp(r0, r1, t);

            a.outHandle = q0;
            a.mode = TangentMode.Free;
            b.inHandle = q2;
            b.mode = TangentMode.Free;
            return new BezierKnot(s, r0, r1, TangentMode.Aligned);
        }
    }
}
