using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Adapts a timing curve's knots to <see cref="IEditablePointSet"/>. Each knot exposes three editable points
    /// — anchor, in-handle, out-handle — at indices <c>3k</c>, <c>3k+1</c>, <c>3k+2</c>, matching every other
    /// knot editor in the project.
    /// </summary>
    /// <remarks>
    /// Writes enforce the anchor skeleton of a time function: the first anchor stays at x 0, the last at x 1,
    /// and interior anchors stay between their neighbours. Moving a pinned coordinate is a defined no-op, not a
    /// rejected edit. Handles move freely — <see cref="Ease"/> clamps them for evaluation while the authored
    /// positions persist.
    /// <para>
    /// Positions are data space (x is progress, y is output) lifted into <see cref="Vector3"/>, which is what
    /// <see cref="RectEditSurface"/> treats as world.
    /// </para>
    /// </remarks>
    public sealed class EasePointSet : IEditablePointSet
    {
        private readonly BezierKnot[] knots;
        private readonly bool pinEndValues;

        /// <summary>
        /// Wraps <paramref name="knots"/> in place. <paramref name="pinEndValues"/> also pins the first and last
        /// anchors' y — what a single cubic Bézier in the CSS sense requires, and what a multi-key curve does not.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="knots"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Fewer than two knots.</exception>
        public EasePointSet(BezierKnot[] knots, bool pinEndValues, UnityEngine.Object undoContext)
        {
            this.knots = knots ?? throw new ArgumentNullException(nameof(knots));
            if (knots.Length < 2)
                throw new ArgumentException("A timing curve needs at least two knots.", nameof(knots));
            this.pinEndValues = pinEndValues;
            UndoContext = undoContext;
        }

        /// <summary>The edited knots, for the renderer.</summary>
        public IReadOnlyList<BezierKnot> Knots => knots;

        /// <inheritdoc/>
        public int Count => knots.Length * 3;

        /// <inheritdoc/>
        public UnityEngine.Object UndoContext { get; }

        /// <inheritdoc/>
        public Vector3 GetPosition(int index)
        {
            var point = Local(index);
            return new Vector3(point.x, point.y, 0f);
        }

        /// <inheritdoc/>
        public void SetPosition(int index, Vector3 world)
        {
            var knotIndex = index / 3;
            var value = new Vector2(world.x, world.y);

            switch (index % 3)
            {
                case 1:
                    knots[knotIndex].inHandle = value;
                    return;
                case 2:
                    knots[knotIndex].outHandle = value;
                    return;
                default:
                    knots[knotIndex].position = ClampAnchor(value, knotIndex);
                    return;
            }
        }

        /// <inheritdoc/>
        public void GetRigidDependents(int index, List<int> into)
        {
            if (index % 3 != 0) return;
            into.Add(index + 1);
            into.Add(index + 2);
        }

        /// <summary>The knot a point index belongs to.</summary>
        public static int KnotIndex(int pointIndex) => pointIndex / 3;

        /// <summary>Whether a point index is a knot anchor rather than one of its handles.</summary>
        public static bool IsAnchor(int pointIndex) => pointIndex % 3 == 0;

        private Vector2 Local(int index)
        {
            var knot = knots[index / 3];
            return (index % 3) switch
            {
                1 => knot.inHandle,
                2 => knot.outHandle,
                _ => knot.position,
            };
        }

        private Vector2 ClampAnchor(Vector2 value, int knotIndex)
        {
            var previous = knots[knotIndex].position;
            if (knotIndex == 0)
                return new Vector2(0f, pinEndValues ? previous.y : value.y);
            if (knotIndex == knots.Length - 1)
                return new Vector2(1f, pinEndValues ? previous.y : value.y);

            return new Vector2(
                Mathf.Clamp(value.x, knots[knotIndex - 1].position.x, knots[knotIndex + 1].position.x),
                value.y);
        }
    }
}
