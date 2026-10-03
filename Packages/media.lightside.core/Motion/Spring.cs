using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Closed-form damped harmonic oscillator, evaluated from elapsed time rather than integrated per frame, so a
    /// value is identical whatever the frame rate and whatever frame it is first asked for.
    /// </summary>
    /// <remarks>
    /// The physical response may be sampled at any instant, in any order, from any time source — a variable frame
    /// clock, a fixed offline step, a scrubbed or resumed timeline — and the same elapsed time always yields the same
    /// value. A bounded cache shared by equal physical coefficients memoizes duration searches and never affects
    /// evaluation.
    /// <para>
    /// A spring that overshoots travels backwards. For anything that rises and fades, keep the damping ratio at or
    /// above 1 — <see cref="Rise"/> and <see cref="Smooth"/> do — or the element visibly bounces off its target.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct Spring : IEquatable<Spring>
    {
        private const float CriticalBand = 1e-3f;
        private const float SettleThreshold = 1e-3f;
        private const float DefaultStiffness = 210f;
        private const float DefaultDamping = 20f;
        private const float DefaultMass = 1f;
        private const int SettleCacheCapacity = 64;

        [SerializeField] private float stiffness;
        [SerializeField] private float damping;
        [SerializeField] private float mass;

        private static readonly object settleCacheLock = new();
        private static readonly float[] cachedFrequencies = new float[SettleCacheCapacity];
        private static readonly float[] cachedRatios = new float[SettleCacheCapacity];
        private static readonly float[] cachedSettleTimes = new float[SettleCacheCapacity];

        /// <summary>Creates a physical response from stiffness, damping and mass, normalizing invalid magnitudes.</summary>
        public Spring(float stiffness, float damping, float mass = 1f)
        {
            if (!IsFinite(stiffness) || !IsFinite(damping) || !IsFinite(mass))
            {
                this.stiffness = DefaultStiffness;
                this.damping = DefaultDamping;
                this.mass = DefaultMass;
            }
            else
            {
                this.stiffness = Mathf.Max(1e-4f, stiffness);
                this.damping = Mathf.Max(0f, damping);
                this.mass = Mathf.Max(1e-4f, mass);
            }
        }

        /// <summary>Damping ratio: below 1 overshoots, 1 is critical, above 1 crawls in without overshoot.</summary>
        public float Ratio
        {
            get
            {
                var response = Normalized();
                return response.NormalizedRatio;
            }
        }

        /// <summary>Undamped angular frequency in radians per second — how fast the response would oscillate unopposed.</summary>
        public float AngularFrequency
        {
            get
            {
                var response = Normalized();
                return response.NormalizedFrequency;
            }
        }

        /// <summary>Whether two springs carry the same physical response; memoized duration is not identity.</summary>
        public bool Equals(Spring other) =>
            stiffness.Equals(other.stiffness) && damping.Equals(other.damping) && mass.Equals(other.mass);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Spring other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(stiffness, damping, mass);

        /// <summary>Whether two springs carry the same physical response.</summary>
        public static bool operator ==(Spring a, Spring b) => a.Equals(b);

        /// <summary>Whether two springs carry different physical responses.</summary>
        public static bool operator !=(Spring a, Spring b) => !a.Equals(b);

        /// <summary>The damping that makes this stiffness and mass critically damped.</summary>
        public static float Critical(float stiffness, float mass = 1f) => 2f * Mathf.Sqrt(stiffness * mass);

        /// <summary>
        /// Creates a unit-mass response from perceptual duration and bounce instead of physical coefficients.
        /// </summary>
        /// <remarks>
        /// <paramref name="duration"/> sets the response's pace and oscillation period; it is not a promise that
        /// the response has settled by that instant. Use <see cref="SettleTime"/> when a finite timeline length is
        /// required. A zero <paramref name="bounce"/> is critically damped, positive values overshoot, and negative
        /// values are overdamped. The conversion uses an angular frequency of <c>2π / duration</c> and a damping
        /// ratio of <c>1 - bounce</c>.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="duration"/> is not finite and positive, cannot be represented by this response, or
        /// <paramref name="bounce"/> is not finite or outside [-1, 1].
        /// </exception>
        public static Spring FromDuration(float duration, float bounce = 0f)
        {
            if (!IsFinite(duration) || duration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "Perceptual spring duration must be finite and positive.");
            if (!IsFinite(bounce) || bounce < -1f || bounce > 1f)
                throw new ArgumentOutOfRangeException(nameof(bounce), bounce,
                    "Spring bounce must be finite and between -1 and 1.");

            var frequency = 2d * Math.PI / duration;
            var stiffness = frequency * frequency;
            var damping = 2d * (1d - bounce) * frequency;
            if (stiffness < 1e-4d || stiffness > float.MaxValue || damping > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(duration), duration,
                    "Perceptual spring duration produces coefficients outside the supported range.");

            return new Spring((float)stiffness, (float)damping);
        }

        /// <summary>Critically damped half-second perceptual response with no overshoot.</summary>
        public static Spring Smooth => FromDuration(0.5f);

        /// <summary>Half-second perceptual response with a small amount of overshoot.</summary>
        public static Spring Snappy => FromDuration(0.5f, 0.15f);

        /// <summary>Half-second perceptual response with a pronounced but controlled bounce.</summary>
        public static Spring Bouncy => FromDuration(0.5f, 0.3f);

        /// <summary>The original fast pop response, retained for compatibility with existing motion.</summary>
        public static Spring Pop => new Spring(260f, 24f);

        /// <summary>The physical fallback for default-constructed and invalid serialized spring data.</summary>
        public static Spring Stiff => new Spring(DefaultStiffness, DefaultDamping, DefaultMass);

        /// <summary>Deliberate and weighty. Large panels and window moves.</summary>
        public static Spring Heavy => new Spring(95f, 30f, 2.2f);

        /// <summary>Critically damped rise, for anything that must not travel backwards.</summary>
        public static Spring Rise => new Spring(120f, Critical(120f));

        /// <summary>Fast and bounce-free, for exits.</summary>
        public static Spring Exit => new Spring(300f, 35f);

        /// <summary>
        /// Position at <paramref name="seconds"/> after release, for a unit step from 0 to 1 starting at rest.
        /// Overshoots past 1 whenever <see cref="Ratio"/> is below 1.
        /// </summary>
        public float Evaluate(float seconds)
        {
            if (seconds <= 0f) return 0f;

            var response = Normalized();
            return EvaluateNormalized(seconds, response.NormalizedFrequency, response.NormalizedRatio);
        }

        /// <summary>
        /// Returns a finite physical response for data that may have entered through default construction or
        /// Unity serialization, where the public constructor's normalization did not run.
        /// </summary>
        internal Spring Normalized()
        {
            if (IsFinite(stiffness) && IsFinite(damping) && IsFinite(mass) &&
                stiffness >= 1e-4f && damping >= 0f && mass >= 1e-4f)
            {
                var frequency = NormalizedFrequency;
                var ratio = NormalizedRatio;
                if (IsFinite(frequency) && IsFinite(ratio) &&
                    IsFinite(ratio * ratio) && IsFinite(ratio * frequency))
                    return this;
                return Stiff;
            }

            if (stiffness == 0f && damping == 0f && mass == 0f) return Stiff;
            if (!IsFinite(stiffness) || !IsFinite(damping) || !IsFinite(mass)) return Stiff;
            return new Spring(stiffness, damping, mass);
        }

        private static float EvaluateNormalized(float seconds, float omega, float zeta)
        {
            if (zeta < 1f - CriticalBand)
            {
                var wd = omega * Mathf.Sqrt(1f - zeta * zeta);
                var decay = Mathf.Exp(-zeta * omega * seconds);
                return 1f - decay * (Mathf.Cos(wd * seconds) + zeta * omega / wd * Mathf.Sin(wd * seconds));
            }

            if (zeta <= 1f + CriticalBand)
            {
                var decay = Mathf.Exp(-omega * seconds);
                return 1f - decay * (1f + omega * seconds);
            }

            var root = omega * Mathf.Sqrt(zeta * zeta - 1f);
            var r1 = -zeta * omega + root;
            var r2 = -zeta * omega - root;
            return 1f - (r1 * Mathf.Exp(r2 * seconds) - r2 * Mathf.Exp(r1 * seconds)) / (r1 - r2);
        }

        /// <summary>Evaluates the spring and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public float Lerp(float a, float b, float seconds) => a + (b - a) * Evaluate(seconds);

        /// <summary>Evaluates the spring and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public Vector2 Lerp(Vector2 a, Vector2 b, float seconds) => Vector2.LerpUnclamped(a, b, Evaluate(seconds));

        /// <summary>Evaluates the spring and interpolates <paramref name="a"/> to <paramref name="b"/>.</summary>
        public Vector3 Lerp(Vector3 a, Vector3 b, float seconds) => Vector3.LerpUnclamped(a, b, Evaluate(seconds));

        /// <summary>
        /// Seconds at which the response enters — and thereafter stays within — one thousandth of its target,
        /// sampled at 240 Hz and capped at ten seconds. Derives a duration from the motion instead of one being
        /// typed beside it.
        /// </summary>
        /// <remarks>
        /// The first calculation for a physical response searches up to 2400 samples; equal coefficients then use
        /// a bounded shared result. Call it where the spring is authored or a sequence is assembled, never per
        /// frame.
        /// <para>The returned instant is when the band was entered, not when the 50 ms confirmation window closed.</para>
        /// </remarks>
        public float SettleTime()
        {
            const float step = 1f / 240f;
            const float cap = 10f;
            const float confirm = 0.05f;

            var response = Normalized();
            var omega = response.NormalizedFrequency;
            var zeta = response.NormalizedRatio;
            var index = (omega.GetHashCode() * 397 ^ zeta.GetHashCode()) & (SettleCacheCapacity - 1);

            lock (settleCacheLock)
            {
                if (cachedSettleTimes[index] > 0f &&
                    cachedFrequencies[index].Equals(omega) && cachedRatios[index].Equals(zeta))
                    return cachedSettleTimes[index];

                var quiet = 0f;
                var entered = 0f;
                for (var t = step; t < cap; t += step)
                {
                    if (Mathf.Abs(EvaluateNormalized(t, omega, zeta) - 1f) < SettleThreshold)
                    {
                        if (quiet <= 0f) entered = t;
                        quiet += step;
                        if (quiet < confirm) continue;

                        cachedFrequencies[index] = omega;
                        cachedRatios[index] = zeta;
                        cachedSettleTimes[index] = entered;
                        return entered;
                    }
                    quiet = 0f;
                }

                cachedFrequencies[index] = omega;
                cachedRatios[index] = zeta;
                cachedSettleTimes[index] = cap;
                return cap;
            }
        }

        private float NormalizedFrequency => Mathf.Sqrt(stiffness) / Mathf.Sqrt(mass);

        private float NormalizedRatio =>
            (float)((double)damping / (2d * Mathf.Sqrt(stiffness) * Mathf.Sqrt(mass)));

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
