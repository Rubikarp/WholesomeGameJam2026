using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>How colour is interpolated between gradient stops.</summary>
    public enum GradientInterpolation : byte
    {
        /// <summary>Blend in sRGB (gamma) space — a plain per-channel lerp of the authored colours.</summary>
        Smooth,
        /// <summary>Blend in linear light — physically even, avoids the dark midpoint a sRGB blend gives.</summary>
        Linear,
        /// <summary>Blend in Oklab — perceptually uniform, no muddy or hue-shifted midtones.</summary>
        Perceptual,
        /// <summary>Hard steps — each segment takes the colour of its upper stop, no blending.</summary>
        Stepped,
    }

    /// <summary>An RGBA colour pinned at a normalized position along a <see cref="Gradient"/>.</summary>
    [Serializable]
    public struct GradientStop
    {
        [Range(0f, 1f)] public float time;
        public Color color;

        public GradientStop(float time, Color color)
        {
            this.time = time;
            this.color = color;
        }
    }

    /// <summary>
    /// Colour gradient with any number of RGBA stops and a selectable interpolation space. Replaces
    /// <c>UnityEngine.Gradient</c>: no 8-key cap, colour and alpha share one stop list, and it
    /// serializes as plain inspectable data. Sample with <see cref="Evaluate"/>. Ascending
    /// <see cref="GradientStop.time"/> order is an invariant the type enforces itself — on construction
    /// and after deserialization (inspector edits, asset loads, JSON) — so consumers may assume sorted
    /// stops. <see cref="Stops"/> exposes allocation-free read-only storage; replacement methods
    /// return a new gradient and preserve the ordering invariant.
    /// </summary>
    [Serializable]
    public struct Gradient : IEquatable<Gradient>, IStateSnapshot<Gradient>
    {
        [SerializeField] private GradientStop[] stops;
        [SerializeField] private GradientInterpolation interpolation;

        /// <summary>Allocation-free read-only view of the ordered stops.</summary>
        public ReadOnlyArray<GradientStop> Stops => new(stops);

        public GradientInterpolation Interpolation
        {
            get => interpolation;
            set => interpolation = value;
        }

        /// <summary>False when there are no stops to sample — treat as no paint.</summary>
        public bool IsValid => stops != null && stops.Length > 0;

        /// <summary>Two-stop black→white smooth gradient, used to seed a freshly created gradient value.</summary>
        public static Gradient Default => new(
            new[] { new GradientStop(0f, Color.black), new GradientStop(1f, Color.white) },
            GradientInterpolation.Smooth, true);

        public Gradient(GradientStop[] stops, GradientInterpolation interpolation)
            : this(stops, interpolation, false)
        {
        }

        private Gradient(GradientStop[] stops, GradientInterpolation interpolation, bool takeOwnership)
        {
            this.stops = stops == null || takeOwnership ? stops : (GradientStop[])stops.Clone();
            this.interpolation = interpolation;
            SortStops();
        }

        /// <summary>Returns a gradient with the supplied detached stop collection.</summary>
        public Gradient WithStops(GradientStop[] value) => new(value, interpolation);

        /// <summary>Returns a gradient with one stop replaced and the result sorted by time.</summary>
        public Gradient WithStop(int index, GradientStop value)
        {
            var replacement = Stops.ToArray();
            if ((uint)index >= (uint)replacement.Length) throw new ArgumentOutOfRangeException(nameof(index));
            replacement[index] = value;
            return new Gradient(replacement, interpolation, true);
        }

        /// <summary>Returns a gradient with one additional stop.</summary>
        public Gradient WithAddedStop(GradientStop value)
        {
            var replacement = StateArray.Add(stops, value);
            return new Gradient(replacement, interpolation, true);
        }

        /// <summary>Returns a gradient without the stop at the requested index.</summary>
        public Gradient WithoutStopAt(int index)
        {
            var replacement = StateArray.RemoveAt(stops, index);
            return new Gradient(replacement, interpolation, true);
        }

        /// <summary>
        /// Colour at <paramref name="t"/> (clamped to [0,1]). Outside the first/last stop returns that
        /// stop's colour; an empty gradient returns opaque white. Assumes stops are time-sorted.
        /// </summary>
        public Color Evaluate(float t)
        {
            if (stops == null || stops.Length == 0) return Color.white;
            if (stops.Length == 1) return stops[0].color;

            t = Mathf.Clamp01(t);
            if (t <= stops[0].time) return stops[0].color;
            var last = stops.Length - 1;
            if (t >= stops[last].time) return stops[last].color;

            var i = 0;
            while (i < last && stops[i + 1].time < t) i++;

            var lo = stops[i];
            var hi = stops[i + 1];
            if (interpolation == GradientInterpolation.Stepped) return hi.color;

            var span = hi.time - lo.time;
            var f = span > 1e-6f ? (t - lo.time) / span : 0f;
            return Blend(lo.color, hi.color, f, interpolation);
        }

        /// <summary>
        /// Colour at <paramref name="t"/> after <paramref name="spread"/> folds it into the ramp's
        /// domain: under <see cref="PaintSpread.Repeat"/> 1.5 yields the colour at 0.5, under
        /// <see cref="PaintSpread.Mirror"/> the colour at 0.5 counted back from the end.
        /// </summary>
        public Color Evaluate(float t, PaintSpread spread) => Evaluate(spread.Wrap(t));

        private sealed class StopTimeComparer : IComparer<GradientStop>
        {
            public static readonly StopTimeComparer instance = new();
            public int Compare(GradientStop x, GradientStop y) => x.time.CompareTo(y.time);
        }

        /// <summary>
        /// Sorts the stops by time in place when out of order, enforcing the sorted invariant
        /// <see cref="Evaluate"/> — and any consumer's order-dependent fast path — relies on.
        /// Runs automatically on construction. No-op when already sorted.
        /// </summary>
        public void SortStops()
        {
            if (stops == null || stops.Length < 2) return;
            var sorted = true;
            for (var i = 1; i < stops.Length; i++)
            {
                if (stops[i].time < stops[i - 1].time)
                {
                    sorted = false;
                    break;
                }
            }
            if (!sorted) Array.Sort(stops, StopTimeComparer.instance);
        }

        /// <summary>Deep copy — safe to retain while the source is edited in place. Sorted via the constructor invariant.</summary>
        public Gradient Clone()
        {
            return new Gradient(stops, interpolation);
        }

        /// <inheritdoc/>
        public Gradient CaptureStateSnapshot() => Clone();

        /// <inheritdoc/>
        public bool StateEquals(in Gradient snapshot) => Equals(snapshot);

        private static Color Blend(Color a, Color b, float f, GradientInterpolation mode)
        {
            var alpha = Mathf.Lerp(a.a, b.a, f);
            Color c;
            switch (mode)
            {
                case GradientInterpolation.Linear:
                    c = Color.Lerp(a.linear, b.linear, f).gamma;
                    break;
                case GradientInterpolation.Perceptual:
                    c = OklabMix(a.linear, b.linear, f).gamma;
                    break;
                default:
                    c = Color.Lerp(a, b, f);
                    break;
            }
            c.a = alpha;
            return c;
        }

        private static Color OklabMix(Color aLin, Color bLin, float t)
        {
            LinearToLab(aLin, out var l1, out var a1, out var b1);
            LinearToLab(bLin, out var l2, out var a2, out var b2);
            LabToLinear(
                Mathf.Lerp(l1, l2, t), Mathf.Lerp(a1, a2, t), Mathf.Lerp(b1, b2, t),
                out var r, out var g, out var b);
            return new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), 1f);
        }

        private static void LinearToLab(Color c, out float L, out float A, out float B)
        {
            var l = 0.4122214708f * c.r + 0.5363325363f * c.g + 0.0514459929f * c.b;
            var m = 0.2119034982f * c.r + 0.6806995451f * c.g + 0.1073969566f * c.b;
            var s = 0.0883024619f * c.r + 0.2817188376f * c.g + 0.6299787005f * c.b;

            var l_ = Cbrt(l);
            var m_ = Cbrt(m);
            var s_ = Cbrt(s);

            L = 0.2104542553f * l_ + 0.7936177850f * m_ - 0.0040720468f * s_;
            A = 1.9779984951f * l_ - 2.4285922050f * m_ + 0.4505937099f * s_;
            B = 0.0259040371f * l_ + 0.7827717662f * m_ - 0.8086757660f * s_;
        }

        private static void LabToLinear(float L, float A, float B, out float r, out float g, out float b)
        {
            var l_ = L + 0.3963377774f * A + 0.2158037573f * B;
            var m_ = L - 0.1055613458f * A - 0.0638541728f * B;
            var s_ = L - 0.0894841775f * A - 1.2914855480f * B;

            var l = l_ * l_ * l_;
            var m = m_ * m_ * m_;
            var s = s_ * s_ * s_;

            r = 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
            g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
            b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;
        }

        private static float Cbrt(float x) => x < 0f ? -Mathf.Pow(-x, 1f / 3f) : Mathf.Pow(x, 1f / 3f);

        public bool Equals(Gradient other)
        {
            if (interpolation != other.interpolation) return false;
            var n = stops?.Length ?? 0;
            if (n != (other.stops?.Length ?? 0)) return false;
            for (var i = 0; i < n; i++)
            {
                if (stops[i].time != other.stops[i].time) return false;
                var x = stops[i].color;
                var y = other.stops[i].color;
                if (x.r != y.r || x.g != y.g || x.b != y.b || x.a != y.a) return false;
            }
            return true;
        }

        public override bool Equals(object obj) => obj is Gradient g && Equals(g);

        public override int GetHashCode()
        {
            var h = new HashCode();
            h.Add((int)interpolation);
            if (stops != null)
                for (var i = 0; i < stops.Length; i++)
                {
                    h.Add(stops[i].time);
                    h.Add(stops[i].color.r);
                    h.Add(stops[i].color.g);
                    h.Add(stops[i].color.b);
                    h.Add(stops[i].color.a);
                }
            return h.ToHashCode();
        }
    }
}
