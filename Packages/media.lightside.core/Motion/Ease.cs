using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>Which of <see cref="Ease"/>'s three shapes a value carries.</summary>
    public enum EaseKind : byte
    {
        /// <summary>One of the curves named by <see cref="EasingType"/>.</summary>
        Builtin = 0,

        /// <summary>A single cubic Bézier segment between (0,0) and (1,1).</summary>
        Cubic = 1,

        /// <summary>A chain of cubic Bézier segments through authored keys.</summary>
        Keyed = 2,
    }

    /// <summary>
    /// A normalized timing function: a built-in curve, a single cubic Bézier in the CSS sense, or a chain of
    /// authored keys.
    /// </summary>
    /// <remarks>
    /// The curve is a timing function, not a path: it maps progress to progress and never leaves 1-D.
    /// <para>
    /// A cubic Bézier is a function of time only while its x components stay monotonic. <see cref="Cubic"/>
    /// guarantees this by clamping both control points' x to [0, 1], which is exactly the necessary and
    /// sufficient condition for the pinned form; <see cref="Keyed"/> requires the same of every segment.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct Ease : IEquatable<Ease>, IStateSnapshot<Ease>
    {
        private const int SolveIterations = 12;
        private const float SolveEpsilon = 1e-5f;
        private const float MinSlope = 1e-6f;

        [SerializeField] private EaseKind kind;
        [SerializeField] private EasingType type;
        [SerializeField] private Vector4 controls;
        [SerializeField] private BezierKnot[] keys;
#if UNITY_EDITOR
        [SerializeField] private Vector4 authoredControls;
        [SerializeField] private BezierKnot[] authoredKeys;
#endif

        /// <summary>Which shape this value carries.</summary>
        public EaseKind Kind => kind;

        /// <summary>The built-in curve; meaningful only while <see cref="Kind"/> is <see cref="EaseKind.Builtin"/>.</summary>
        public EasingType Type => type;

        /// <summary>Wraps a built-in curve.</summary>
        public static Ease Of(EasingType type) => new Ease { kind = EaseKind.Builtin, type = type };

        /// <summary>
        /// Lets a built-in curve stand wherever a shape is asked for, so an <see cref="EasingType"/> and an
        /// authored curve are written the same way.
        /// </summary>
        /// <remarks>
        /// This conversion never reaches further than <see cref="Ease"/>: C# applies at most one user-defined
        /// conversion, so an <see cref="EasingType"/> still cannot stand where a whole timing is expected, and
        /// a duration left unwritten stays a compile error rather than becoming a silent default.
        /// </remarks>
        public static implicit operator Ease(EasingType type) => Of(type);

        /// <summary>
        /// A cubic Bézier timing function whose control points are (<paramref name="x1"/>, <paramref name="y1"/>)
        /// and (<paramref name="x2"/>, <paramref name="y2"/>). Evaluation clamps the x components to [0, 1] so the
        /// curve stays a function of time; the supplied positions persist unclamped as the authored shape in the
        /// editor. The y components are free, and values outside [0, 1] overshoot.
        /// </summary>
        public static Ease Cubic(float x1, float y1, float x2, float y2)
        {
            var ease = new Ease
            {
                kind = EaseKind.Cubic,
                controls = new Vector4(Mathf.Clamp01(x1), y1, Mathf.Clamp01(x2), y2),
            };
#if UNITY_EDITOR
            ease.authoredControls = new Vector4(x1, y1, x2, y2);
#endif
            return ease;
        }

        /// <summary>
        /// A curve through <paramref name="knots"/>, which are copied. Anchors must run left to right. Evaluation
        /// clamps each handle into its own segment's x span so every segment stays a function of time; the
        /// supplied handle positions persist unclamped as the authored shape in the editor.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="knots"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Fewer than two knots, or anchors are not in ascending x.</exception>
        public static Ease Keyed(BezierKnot[] knots)
        {
            if (knots == null) throw new ArgumentNullException(nameof(knots));
            if (knots.Length < 2)
                throw new ArgumentException("A keyed curve needs at least two knots.", nameof(knots));

            var authored = new BezierKnot[knots.Length];
            Array.Copy(knots, authored, knots.Length);
            for (var i = 1; i < authored.Length; i++)
                if (authored[i].position.x < authored[i - 1].position.x)
                    throw new ArgumentException("Keyed curve anchors must ascend in x.", nameof(knots));

            var runtime = new BezierKnot[authored.Length];
            Array.Copy(authored, runtime, authored.Length);
            for (var i = 0; i < runtime.Length - 1; i++)
            {
                float lo = runtime[i].position.x, hi = runtime[i + 1].position.x;
                runtime[i].outHandle.x = Mathf.Clamp(runtime[i].outHandle.x, lo, hi);
                runtime[i + 1].inHandle.x = Mathf.Clamp(runtime[i + 1].inHandle.x, lo, hi);
            }

            var ease = new Ease { kind = EaseKind.Keyed, keys = runtime };
#if UNITY_EDITOR
            ease.authoredKeys = authored;
#endif
            return ease;
        }

        /// <summary>Material 3 <c>emphasized</c>: the default for anything that begins and ends on screen.</summary>
        public static Ease Emphasized => Cubic(0.2f, 0f, 0f, 1f);

        /// <summary>Material 3 <c>emphasized decelerate</c>: for elements entering the frame.</summary>
        public static Ease EmphasizedIn => Cubic(0.05f, 0.7f, 0.1f, 1f);

        /// <summary>Material 3 <c>emphasized accelerate</c>: for elements leaving the frame.</summary>
        public static Ease EmphasizedOut => Cubic(0.3f, 0f, 0.8f, 0.15f);

        /// <summary>Material 3 <c>standard decelerate</c>.</summary>
        public static Ease StandardIn => Cubic(0f, 0f, 0f, 1f);

        /// <summary>Material 3 <c>standard accelerate</c>.</summary>
        public static Ease StandardOut => Cubic(0.3f, 0f, 1f, 1f);

        /// <summary>Identity.</summary>
        public static Ease Linear => Of(EasingType.Linear);

        /// <summary>
        /// Knots this curve is made of: none for <see cref="EaseKind.Builtin"/>, two for
        /// <see cref="EaseKind.Cubic"/>, and the authored count for <see cref="EaseKind.Keyed"/>.
        /// </summary>
        public int KnotCount => kind switch
        {
            EaseKind.Cubic => 2,
            EaseKind.Keyed => keys?.Length ?? 0,
            _ => 0,
        };

        /// <summary>
        /// The knot at <paramref name="index"/>, whatever the storage behind it — the single model an editor or
        /// renderer works against.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside <see cref="KnotCount"/>.</exception>
        public BezierKnot GetKnot(int index)
        {
            if (index < 0 || index >= KnotCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);

            if (kind == EaseKind.Keyed) return keys[index];

            return index == 0
                ? new BezierKnot(Vector2.zero, Vector2.zero,
                    new Vector2(controls.x, controls.y), TangentMode.Free)
                : new BezierKnot(Vector2.one,
                    new Vector2(controls.z, controls.w), Vector2.one, TangentMode.Free);
        }

#if UNITY_EDITOR
        /// <summary>
        /// The knot at <paramref name="index"/> with the handle positions as authored, before the runtime
        /// clamp. Falls back to the runtime knot for values that carry no authoring data.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside <see cref="KnotCount"/>.</exception>
        public BezierKnot GetAuthoredKnot(int index)
        {
            if (index < 0 || index >= KnotCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, null);

            if (kind == EaseKind.Keyed)
                return authoredKeys != null && authoredKeys.Length == keys.Length
                    ? authoredKeys[index]
                    : keys[index];

            var c = authoredControls == Vector4.zero ? controls : authoredControls;
            return index == 0
                ? new BezierKnot(Vector2.zero, Vector2.zero, new Vector2(c.x, c.y), TangentMode.Free)
                : new BezierKnot(Vector2.one, new Vector2(c.z, c.w), Vector2.one, TangentMode.Free);
        }
#endif

        /// <summary>Maps <paramref name="t"/> in [0, 1] through the curve. Input is clamped; output may overshoot.</summary>
        /// <exception cref="InvalidOperationException">A <see cref="EaseKind.Keyed"/> curve carries fewer than two knots.</exception>
        public float Evaluate(float t)
        {
            t = Mathf.Clamp01(t);
            switch (kind)
            {
                case EaseKind.Builtin:
                    return type.Evaluate(t);

                case EaseKind.Cubic:
                    if (t <= 0f || t >= 1f) return t;
                    return Axis(0f, controls.y, controls.w, 1f,
                        SolveX(0f, controls.x, controls.z, 1f, t));

                case EaseKind.Keyed:
                    return EvaluateKeyed(t);

                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), kind, null);
            }
        }

        /// <summary>
        /// Progress of a move that begins <paramref name="start"/> seconds into a slide and lasts
        /// <paramref name="seconds"/>, given the slide's own <paramref name="local"/> time. Returns 0 before the
        /// move and 1 after it, and 1 outright when <paramref name="seconds"/> is not positive.
        /// </summary>
        /// <remarks>
        /// Staggering is this and nothing else: <c>Window(local, start + i * step, seconds)</c>.
        /// </remarks>
        public float Window(float local, float start, float seconds) =>
            seconds <= 0f ? 1f : Evaluate((local - start) / seconds);

        /// <summary>Maps <paramref name="t"/> through the curve and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public float Lerp(float a, float b, float t) => a + (b - a) * Evaluate(t);

        /// <summary>Maps <paramref name="t"/> through the curve and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public Vector2 Lerp(Vector2 a, Vector2 b, float t) => Vector2.LerpUnclamped(a, b, Evaluate(t));

        /// <summary>Maps <paramref name="t"/> through the curve and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public Vector3 Lerp(Vector3 a, Vector3 b, float t) => Vector3.LerpUnclamped(a, b, Evaluate(t));

        /// <summary>Maps <paramref name="t"/> through the curve and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public Color Lerp(Color a, Color b, float t) => Color.LerpUnclamped(a, b, Evaluate(t));

        private float EvaluateKeyed(float x)
        {
            var count = keys?.Length ?? 0;
            if (count < 2)
                throw new InvalidOperationException("A keyed Ease carries fewer than two knots.");

            if (x <= keys[0].position.x) return keys[0].position.y;
            if (x >= keys[count - 1].position.x) return keys[count - 1].position.y;

            var lo = 1;
            var hi = count - 1;
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                if (x <= keys[mid].position.x) hi = mid;
                else lo = mid + 1;
            }

            var a = keys[lo - 1];
            var b = keys[lo];
            var t = SolveX(a.position.x, a.outHandle.x, b.inHandle.x, b.position.x, x);
            return Axis(a.position.y, a.outHandle.y, b.inHandle.y, b.position.y, t);
        }

        /// <summary>One axis of a cubic Bézier segment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Axis(float p0, float p1, float p2, float p3, float t)
        {
            var u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float AxisSlope(float p0, float p1, float p2, float p3, float t)
        {
            var u = 1f - t;
            return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        }

        /// <summary>
        /// The curve parameter at which the segment's x reaches <paramref name="x"/>. Newton steps are kept inside a
        /// bisection bracket, so a flat or near-flat tangent costs accuracy rather than correctness.
        /// </summary>
        private static float SolveX(float p0, float p1, float p2, float p3, float x)
        {
            float lo = 0f, hi = 1f;
            var span = p3 - p0;
            var t = span > MinSlope ? Mathf.Clamp01((x - p0) / span) : 0.5f;

            for (var i = 0; i < SolveIterations; i++)
            {
                var error = Axis(p0, p1, p2, p3, t) - x;
                if (error < SolveEpsilon && error > -SolveEpsilon) return t;

                if (error > 0f) hi = t;
                else lo = t;

                var slope = AxisSlope(p0, p1, p2, p3, t);
                var step = slope > MinSlope || slope < -MinSlope ? t - error / slope : float.NaN;
                t = step > lo && step < hi ? step : (lo + hi) * 0.5f;
            }

            return t;
        }

        /// <inheritdoc/>
        public Ease CaptureStateSnapshot()
        {
            if (kind != EaseKind.Keyed || keys == null || keys.Length == 0) return this;
            var snapshot = this;
            var runtime = new BezierKnot[keys.Length];
            Array.Copy(keys, runtime, keys.Length);
            snapshot.keys = runtime;
#if UNITY_EDITOR
            if (authoredKeys is { Length: > 0 })
            {
                var authored = new BezierKnot[authoredKeys.Length];
                Array.Copy(authoredKeys, authored, authoredKeys.Length);
                snapshot.authoredKeys = authored;
            }
#endif
            return snapshot;
        }

        /// <inheritdoc/>
        public bool StateEquals(in Ease snapshot) => Equals(snapshot);

        public bool Equals(Ease other)
        {
            if (kind != other.kind) return false;
            return kind switch
            {
                EaseKind.Builtin => type == other.type,
                EaseKind.Cubic => controls.Equals(other.controls),
                _ => KnotsEqual(keys, other.keys),
            };
        }

        private static bool KnotsEqual(BezierKnot[] a, BezierKnot[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            var countA = a?.Length ?? 0;
            if (countA != (b?.Length ?? 0)) return false;
            for (var i = 0; i < countA; i++)
            {
                if (!a[i].position.Equals(b[i].position)) return false;
                if (!a[i].inHandle.Equals(b[i].inHandle)) return false;
                if (!a[i].outHandle.Equals(b[i].outHandle)) return false;
            }
            return true;
        }

        private static int KnotsHash(BezierKnot[] knots)
        {
            var hash = new HashCode();
            hash.Add(EaseKind.Keyed);
            var count = knots?.Length ?? 0;
            hash.Add(count);
            for (var i = 0; i < count; i++)
            {
                hash.Add(knots[i].position);
                hash.Add(knots[i].inHandle);
                hash.Add(knots[i].outHandle);
            }
            return hash.ToHashCode();
        }

        public override bool Equals(object obj) => obj is Ease other && Equals(other);

        public override int GetHashCode() => kind switch
        {
            EaseKind.Builtin => HashCode.Combine(EaseKind.Builtin, type),
            EaseKind.Cubic => HashCode.Combine(EaseKind.Cubic, controls),
            _ => KnotsHash(keys),
        };

        public static bool operator ==(Ease a, Ease b) => a.Equals(b);

        public static bool operator !=(Ease a, Ease b) => !a.Equals(b);
    }
}
