using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adapts a <see cref="BezierPath"/> to <see cref="IEditablePointSet"/> so <see cref="PointEditor"/> can edit
    /// its knots. Each knot exposes three editable points — anchor, in-handle, out-handle — at point indices
    /// <c>3k</c>, <c>3k+1</c>, <c>3k+2</c>. Positions convert between the path's local 2-D space and world through
    /// <see cref="LocalToWorld"/> — the host's own frame for SceneView editing, or the identity to edit the path
    /// directly in its own 2-D space (an inspector preview / asset panel, paired with a 2-D <see cref="IEditSurface"/>).
    /// </summary>
    /// <remarks>
    /// This is a pure mapping layer: tangent-mode enforcement is the host editor's job. After applying an edit the
    /// host calls <see cref="BezierPath.OnHandleMoved"/> (for a moved handle) or <see cref="BezierPath.OnAnchorMoved"/>
    /// (for a moved anchor) on the affected knots, using <see cref="KnotIndex"/> / <see cref="Part"/> to classify
    /// the point indices. Keeping enforcement out of <see cref="SetPosition"/> avoids ordering hazards during the
    /// editor's multi-point apply pass (anchor and its handles are all written in one gesture).
    /// </remarks>
    public sealed class BezierPathPointSet : IEditablePointSet
    {
        private readonly BezierPath path;
        private readonly UnityEngine.Object undoContext;
        private Matrix4x4 localToWorld;
        private Matrix4x4 worldToLocal;

        public BezierPathPointSet(BezierPath path, Matrix4x4 localToWorld, UnityEngine.Object undoContext)
        {
            this.path = path;
            this.undoContext = undoContext;
            LocalToWorld = localToWorld;
        }

        /// <summary>
        /// Frame the path's local 2-D points are placed in. Reassign whenever the host's frame moves — the edited
        /// object's transform or its own rotation — so picking and dragging stay on what is drawn.
        /// </summary>
        public Matrix4x4 LocalToWorld
        {
            get => localToWorld;
            set
            {
                localToWorld = value;
                worldToLocal = value.inverse;
            }
        }

        /// <summary>The wrapped path.</summary>
        public BezierPath Path => path;

        public int Count => path.Count * 3;

        public UnityEngine.Object UndoContext => undoContext;

        public Vector3 GetPosition(int index)
        {
            var local = LocalOf(index);
            return localToWorld.MultiplyPoint3x4(new Vector3(local.x, local.y, 0f));
        }

        public void SetPosition(int index, Vector3 world)
        {
            var local = worldToLocal.MultiplyPoint3x4(world);
            SetLocal(index, new Vector2(local.x, local.y));
        }

        public void GetRigidDependents(int index, List<int> into)
        {
            if (Part(index) != 0) return;
            into.Add(index + 1);
            into.Add(index + 2);
        }

        /// <summary>The knot a point index belongs to.</summary>
        public static int KnotIndex(int pointIndex) => pointIndex / 3;

        /// <summary>Which part of a knot a point index is: 0 = anchor, 1 = in-handle, 2 = out-handle.</summary>
        public static int Part(int pointIndex) => pointIndex % 3;

        /// <summary>Whether a point index is a knot anchor (as opposed to one of its handles).</summary>
        public static bool IsAnchor(int pointIndex) => pointIndex % 3 == 0;

        /// <summary>The point index of a knot's anchor.</summary>
        public static int AnchorPoint(int knotIndex) => knotIndex * 3;

        private Vector2 LocalOf(int index)
        {
            var k = path[KnotIndex(index)];
            return Part(index) switch
            {
                1 => k.inHandle,
                2 => k.outHandle,
                _ => k.position,
            };
        }

        private void SetLocal(int index, Vector2 value)
        {
            int ki = KnotIndex(index);
            var k = path[ki];
            switch (Part(index))
            {
                case 1: k.inHandle = value; break;
                case 2: k.outHandle = value; break;
                default: k.position = value; break;
            }
            path[ki] = k;
        }
    }
}
