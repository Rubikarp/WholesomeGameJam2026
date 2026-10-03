using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// An editable cubic-Bézier path in 2-D "path space" — an ordered list of <see cref="BezierKnot"/>s, open or
    /// closed. Pure data + geometry (no Unity transform or animation coupling): tangent-mode enforcement, shape-
    /// preserving segment splitting (De Casteljau), point evaluation, and adaptive flattening to a polyline for
    /// meshing or rendering. The consumer maps path space to world space.
    /// </summary>
    [Serializable]
    [StateSource]
    public sealed partial class BezierPath
    {
        [SerializeField, StateList(nameof(NotifyKnotsChanged), Name = "MutableKnots", IsPublic = false)]
        private List<BezierKnot> knots = new();

        /// <summary>Whether the last knot connects back to the first.</summary>
        [SerializeField, StateProperty(nameof(NotifyClosedChanged))] private bool closed;

        [NonSerialized] private int mutationDepth;
        [NonSerialized] private bool knotsMutationPending;
        [NonSerialized] private bool closedMutationPending;
        [NonSerialized] private StateChange pendingKnotsChange;
        [NonSerialized] private int revision;

        /// <summary>Monotonic content stamp, bumped by every knot or closed-flag transition — the key consumers cache derived geometry by.</summary>
        public int Revision => revision;
        [NonSerialized] private List<BezierKnot> readOnlyKnotSource;
        [NonSerialized] private ReadOnlyCollection<BezierKnot> readOnlyKnots;

        /// <summary>The knots, in path order.</summary>
        public IReadOnlyList<BezierKnot> Knots
        {
            get
            {
                if (readOnlyKnots != null && readOnlyKnotSource == knots)
                    return readOnlyKnots;
                readOnlyKnotSource = knots;
                return readOnlyKnots = knots.AsReadOnly();
            }
        }

        /// <summary>Number of knots.</summary>
        public int Count => knots.Count;

        /// <summary>Number of cubic segments — one per knot when <see cref="Closed"/>, otherwise one fewer.</summary>
        public int SegmentCount => closed ? knots.Count : Mathf.Max(0, knots.Count - 1);

        /// <summary>Knot accessor (a <see cref="BezierKnot"/> is a value type — read, modify, write back).</summary>
        public BezierKnot this[int index]
        {
            get => knots[index];
            set => MutableKnots.Replace(index, value);
        }

        /// <summary>Atomically replaces the complete path and publishes at most one change to its owner.</summary>
        public void Replace(IEnumerable<BezierKnot> replacement, bool isClosed)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            ReplaceOwned(new List<BezierKnot>(replacement), isClosed);
        }

        /// <summary>Appends a knot.</summary>
        public void Add(BezierKnot knot)
            => MutableKnots.Add(knot);

        /// <summary>Inserts a knot at <paramref name="index"/>.</summary>
        public void Insert(int index, BezierKnot knot)
            => MutableKnots.Insert(index, knot);

        /// <summary>Removes the knot at <paramref name="index"/>.</summary>
        public void RemoveAt(int index)
            => MutableKnots.RemoveAt(index);

        /// <summary>Removes all knots.</summary>
        public void Clear()
        {
            if (knots.Count == 0) return;
            MutableKnots.Clear();
        }

        /// <summary>Reverses the path's direction (and thus its winding), swapping each knot's in/out handles so the shape is preserved. Useful for turning a contour into a hole.</summary>
        public void Reverse()
        {
            if (knots.Count < 2) return;
            var replacement = new List<BezierKnot>(knots);
            replacement.Reverse();
            for (int i = 0; i < replacement.Count; i++)
            {
                var k = replacement[i];
                (k.inHandle, k.outHandle) = (k.outHandle, k.inHandle);
                replacement[i] = k;
            }
            MutableKnots.ReplaceAll(replacement);
        }

        /// <summary>Moves every knot (anchor and both handles) by <paramref name="delta"/> in path space.</summary>
        public void Translate(Vector2 delta)
        {
            if (delta == Vector2.zero || knots.Count == 0) return;
            var replacement = new List<BezierKnot>(knots);
            for (int i = 0; i < replacement.Count; i++)
            {
                var k = replacement[i];
                k.position += delta;
                k.inHandle += delta;
                k.outHandle += delta;
                replacement[i] = k;
            }
            MutableKnots.ReplaceAll(replacement);
        }

        /// <summary>
        /// Translates and uniformly scales every knot so the path's <see cref="Bounds"/> sit centred inside
        /// <paramref name="target"/>, inset by <paramref name="padding"/> per side; aspect ratio is preserved. A
        /// one-shot author-time normalization — e.g. after importing an SVG in arbitrary viewBox units, or to reset a
        /// hand-authored outline into the shape's rect — so the contour renders and edits 1:1 (WYSIWYG). The centre
        /// is <c>target.center</c>, matching how the polygon resolve places verts relative to the rect centre, so the
        /// fit is exact regardless of pivot. No-op for an empty or single-point path.
        /// </summary>
        public void FitInto(Rect target, float padding = 0f)
        {
            if (knots.Count == 0) return;

            var b = Bounds();
            if (b.width < 1e-6f && b.height < 1e-6f) return;

            float halfX = Mathf.Max(1e-4f, target.width * 0.5f - padding);
            float halfY = Mathf.Max(1e-4f, target.height * 0.5f - padding);
            float sx = b.width > 1e-6f ? halfX * 2f / b.width : float.MaxValue;
            float sy = b.height > 1e-6f ? halfY * 2f / b.height : float.MaxValue;
            float scale = Mathf.Min(sx, sy);

            Vector2 bc = b.center;
            Vector2 tc = target.center;
            var replacement = new List<BezierKnot>(knots);
            for (int i = 0; i < replacement.Count; i++)
            {
                var k = replacement[i];
                k.position = tc + (k.position - bc) * scale;
                k.inHandle = tc + (k.inHandle - bc) * scale;
                k.outHandle = tc + (k.outHandle - bc) * scale;
                replacement[i] = k;
            }
            MutableKnots.ReplaceAll(replacement);
        }

        /// <summary>Returns the four control points of segment <paramref name="segment"/> (anchor, out-handle, next in-handle, next anchor).</summary>
        public void GetSegment(int segment, out Vector2 p0, out Vector2 p1, out Vector2 p2, out Vector2 p3)
        {
            int a = segment;
            int b = (segment + 1) % knots.Count;
            var ka = knots[a];
            var kb = knots[b];
            p0 = ka.position;
            p1 = ka.outHandle;
            p2 = kb.inHandle;
            p3 = kb.position;
        }

        /// <summary>Evaluates a point on segment <paramref name="segment"/> at parameter <paramref name="t"/> in 0..1.</summary>
        public Vector2 Evaluate(int segment, float t)
        {
            GetSegment(segment, out var p0, out var p1, out var p2, out var p3);
            return EvaluateCubic(p0, p1, p2, p3, t);
        }

        /// <summary>Standard cubic Bézier evaluation.</summary>
        public static Vector2 EvaluateCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        /// <summary>
        /// Re-establishes the tangent-mode constraint after one handle of knot <paramref name="index"/> was moved.
        /// Dragging an <see cref="TangentMode.Auto"/> / <see cref="TangentMode.Vector"/> handle promotes it to
        /// <see cref="TangentMode.Aligned"/> (as in Blender).
        /// </summary>
        public void OnHandleMoved(int index, HandleSide moved)
        {
            var previous = knots[index];
            var k = previous;
            switch (k.mode)
            {
                case TangentMode.Free:
                    break;
                case TangentMode.Aligned:
                    BezierKnotEdit.AlignOpposite(ref k, moved, keepLength: true);
                    break;
                case TangentMode.Mirrored:
                    BezierKnotEdit.AlignOpposite(ref k, moved, keepLength: false);
                    break;
                default:
                    k.mode = TangentMode.Aligned;
                    BezierKnotEdit.AlignOpposite(ref k, moved, keepLength: true);
                    break;
            }
            if (k.Equals(previous)) return;
            MutableKnots.Replace(index, k);
        }

        /// <summary>Recomputes derived handles after the anchor of knot <paramref name="index"/> moved. Refreshes
        /// this knot and its immediate neighbours when they are <see cref="TangentMode.Auto"/> /
        /// <see cref="TangentMode.Vector"/> (their handles depend on neighbouring anchors); other modes keep their
        /// handles, which the editor translates rigidly with the anchor.</summary>
        public void OnAnchorMoved(int index)
        {
            var replacement = new List<BezierKnot>(knots);
            var changed = EnforceAutoOrVector(replacement, index);
            changed |= EnforceAutoOrVector(replacement, NeighborIndex(index, -1));
            changed |= EnforceAutoOrVector(replacement, NeighborIndex(index, 1));
            if (changed) MutableKnots.ReplaceAll(replacement);
        }

        private bool EnforceAutoOrVector(List<BezierKnot> candidate, int index)
        {
            if (index < 0 || index >= candidate.Count) return false;
            var previous = candidate[index];
            var mode = previous.mode;
            if (mode == TangentMode.Auto) RecomputeAuto(candidate, index);
            else if (mode == TangentMode.Vector) RecomputeVector(candidate, index);
            return !candidate[index].Equals(previous);
        }

        private int NeighborIndex(int index, int dir)
        {
            int n = knots.Count;
            if (n == 0) return -1;
            int j = index + dir;
            return closed ? (j % n + n) % n : (j >= 0 && j < n ? j : -1);
        }

        /// <summary>Sets the tangent mode of knot <paramref name="index"/> and immediately enforces it.</summary>
        public void SetMode(int index, TangentMode mode)
        {
            var replacement = new List<BezierKnot>(knots);
            var previous = replacement[index];
            var k = previous;
            k.mode = mode;
            replacement[index] = k;

            switch (mode)
            {
                case TangentMode.Auto: RecomputeAuto(replacement, index); break;
                case TangentMode.Vector: RecomputeVector(replacement, index); break;
                case TangentMode.Mirrored: EnforceCollinear(replacement, index, keepLength: false); break;
                case TangentMode.Aligned: EnforceCollinear(replacement, index, keepLength: true); break;
            }
            if (replacement[index].Equals(previous)) return;
            MutableKnots.ReplaceAll(replacement);
        }

        /// <summary>
        /// Splits segment <paramref name="segment"/> at parameter <paramref name="t"/> without changing the curve's
        /// shape (De Casteljau subdivision), inserting a new knot. Returns the new knot's index. The two flanking
        /// anchors become <see cref="TangentMode.Free"/> so their recomputed handles are preserved exactly.
        /// </summary>
        public int SplitSegment(int segment, float t)
        {
            int a = segment;
            int b = (segment + 1) % knots.Count;
            var replacement = new List<BezierKnot>(knots);

            var ka = replacement[a];
            var kb = replacement[b];
            var middle = BezierKnotEdit.Split(ref ka, ref kb, t);
            replacement[a] = ka;
            replacement[b] = kb;

            replacement.Insert(a + 1, middle);
            MutableKnots.ReplaceAll(replacement);
            return a + 1;
        }

        /// <summary>
        /// Replaces the knots with <paramref name="replacement"/> and sets <see cref="Closed"/>, adopting the list
        /// itself rather than copying it — the caller must hand it over and not touch it again. One change is
        /// published for the whole replacement.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="replacement"/> is <see langword="null"/>.</exception>
        public void ReplaceOwned(List<BezierKnot> replacement, bool isClosed)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            mutationDepth++;
            try
            {
                SetKnotsState(replacement);
                Closed = isClosed;
            }
            finally
            {
                mutationDepth--;
                if (mutationDepth == 0)
                {
                    if (knotsMutationPending)
                    {
                        knotsMutationPending = false;
                        Publish(in pendingKnotsChange);
                    }
                    if (closedMutationPending)
                    {
                        closedMutationPending = false;
                        var change = new StateChange(Members.Closed);
                        Publish(in change);
                    }
                }
            }
        }

        private void NotifyKnotsChanged(in StateListMutation<BezierKnot> mutation)
        {
            revision++;
            var change = mutation.ToStateChange(Members.Knots);
            if (mutationDepth != 0)
            {
                knotsMutationPending = true;
                pendingKnotsChange = change;
                return;
            }
            Publish(in change);
        }

        private void NotifyClosedChanged(StateMember member)
        {
            revision++;
            if (mutationDepth != 0)
            {
                closedMutationPending = true;
                return;
            }
            var change = new StateChange(member);
            Publish(in change);
        }

        private void Publish(in StateChange change)
            => PublishStateChange(this, in change);

        /// <summary>
        /// Appends the flattened polyline of the whole path to <paramref name="output"/> via adaptive subdivision:
        /// each cubic is split until its control points sit within <paramref name="tolerance"/> (path-space units)
        /// of the chord. The first anchor is emitted once, then each segment's far endpoint; a closed path ends
        /// back at the first anchor.
        /// </summary>
        public void Flatten(List<Vector2> output, float tolerance = 0.25f)
        {
            output.Clear();
            if (knots.Count == 0) return;

            output.Add(knots[0].position);
            for (int seg = 0; seg < SegmentCount; seg++)
            {
                GetSegment(seg, out var p0, out var p1, out var p2, out var p3);
                FlattenCubic(p0, p1, p2, p3, tolerance, output, 0);
            }
        }

        /// <summary>
        /// Finds the closest point on the path to <paramref name="point"/> (path space), returning the segment and
        /// its parameter plus the distance. Coarse sampling — precise enough to choose where an insert splits a
        /// segment. Returns <see cref="float.MaxValue"/> and <c>segment = -1</c> for an empty path.
        /// </summary>
        public float ClosestPoint(Vector2 point, out int segment, out float t, int samplesPerSegment = 20)
        {
            segment = -1;
            t = 0f;
            float best = float.MaxValue;

            for (int seg = 0; seg < SegmentCount; seg++)
            {
                GetSegment(seg, out var p0, out var p1, out var p2, out var p3);
                for (int i = 0; i <= samplesPerSegment; i++)
                {
                    float tt = (float)i / samplesPerSegment;
                    float d = (EvaluateCubic(p0, p1, p2, p3, tt) - point).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        segment = seg;
                        t = tt;
                    }
                }
            }

            return segment < 0 ? float.MaxValue : Mathf.Sqrt(best);
        }

        /// <summary>Axis-aligned bounds in path space, covering every knot's anchor and both handles. <see cref="Rect.zero"/> for an empty path.</summary>
        public Rect Bounds()
        {
            if (knots.Count == 0) return Rect.zero;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < knots.Count; i++)
            {
                var k = knots[i];
                Encapsulate(k.position, ref minX, ref minY, ref maxX, ref maxY);
                Encapsulate(k.inHandle, ref minX, ref minY, ref maxX, ref maxY);
                Encapsulate(k.outHandle, ref minX, ref minY, ref maxX, ref maxY);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static void Encapsulate(Vector2 p, ref float minX, ref float minY, ref float maxX, ref float maxY)
        {
            if (p.x < minX) minX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.x > maxX) maxX = p.x;
            if (p.y > maxY) maxY = p.y;
        }

        private void RecomputeAuto(List<BezierKnot> candidate, int index)
        {
            var k = candidate[index];
            BezierKnotEdit.Auto(ref k, Neighbor(candidate, index, -1), Neighbor(candidate, index, 1));
            candidate[index] = k;
        }

        private void RecomputeVector(List<BezierKnot> candidate, int index)
        {
            var k = candidate[index];
            BezierKnotEdit.Vector(ref k, Neighbor(candidate, index, -1), Neighbor(candidate, index, 1));
            candidate[index] = k;
        }

        private static void EnforceCollinear(List<BezierKnot> candidate, int index, bool keepLength)
        {
            var k = candidate[index];
            BezierKnotEdit.AlignOpposite(ref k, HandleSide.Out, keepLength);
            candidate[index] = k;
        }

        private Vector2 Neighbor(List<BezierKnot> candidate, int index, int dir)
        {
            int n = candidate.Count;
            if (n == 0) return Vector2.zero;
            int j = index + dir;
            j = closed ? (j % n + n) % n : Mathf.Clamp(j, 0, n - 1);
            return candidate[j].position;
        }

        private static void FlattenCubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tol, List<Vector2> output, int depth)
        {
            if (depth >= 16 || IsFlat(p0, p1, p2, p3, tol))
            {
                output.Add(p3);
                return;
            }

            Vector2 q0 = (p0 + p1) * 0.5f;
            Vector2 q1 = (p1 + p2) * 0.5f;
            Vector2 q2 = (p2 + p3) * 0.5f;
            Vector2 r0 = (q0 + q1) * 0.5f;
            Vector2 r1 = (q1 + q2) * 0.5f;
            Vector2 s = (r0 + r1) * 0.5f;

            FlattenCubic(p0, q0, r0, s, tol, output, depth + 1);
            FlattenCubic(s, r1, q2, p3, tol, output, depth + 1);
        }

        private static bool IsFlat(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float tol)
        {
            return DistanceToLine(p1, p0, p3) <= tol && DistanceToLine(p2, p0, p3) <= tol;
        }

        private static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.magnitude;
            if (len < 1e-6f) return (p - a).magnitude;
            return Mathf.Abs((p.x - a.x) * ab.y - (p.y - a.y) * ab.x) / len;
        }
    }
}
