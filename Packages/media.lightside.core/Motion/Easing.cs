using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Built-in normalized easing curves. The values are a serialization contract and never change; the
    /// declaration order is the order the inspector dropdown lists them in.
    /// </summary>
    public enum EasingType : byte
    {
        Linear = 0,

        SineIn = 10,
        SineOut = 11,
        SineInOut = 12,

        QuadraticIn = 7,
        QuadraticOut = 8,
        QuadraticInOut = 9,

        CubicIn = 2,
        CubicOut = 1,
        CubicInOut = 3,

        QuarticIn = 13,
        QuarticOut = 14,
        QuarticInOut = 15,

        QuinticIn = 16,
        QuinticOut = 17,
        QuinticInOut = 18,

        ExponentialIn = 19,
        ExponentialOut = 20,
        ExponentialInOut = 21,

        CircularIn = 22,
        CircularOut = 23,
        CircularInOut = 24,

        BackIn = 25,
        BackOut = 4,
        BackInOut = 26,

        ElasticIn = 27,
        ElasticOut = 5,
        ElasticInOut = 28,

        BounceIn = 29,
        BounceOut = 6,
        BounceInOut = 30,

        /// <summary>Hermite <c>3t² − 2t³</c>: symmetric, flat at both ends, gentler than <see cref="CubicInOut"/>.</summary>
        Smoothstep = 31,

        /// <summary>Perlin's <c>6t⁵ − 15t⁴ + 10t³</c>: symmetric, flat in both slope and curvature at the ends.</summary>
        Smootherstep = 32,

        /// <summary>Jumps to 1 immediately after 0 — CSS <c>step-start</c>.</summary>
        StepStart = 33,

        /// <summary>Holds 0 until the very end — CSS <c>step-end</c>.</summary>
        StepEnd = 34,
    }

    /// <summary>Evaluation of <see cref="EasingType"/>, without allocations.</summary>
    public static class EasingTypeExtensions
    {
        private const float BackOvershoot = 1.70158f;
        private const float BackOvershootInOut = BackOvershoot * 1.525f;
        private const float ElasticPeriod = 2f * Mathf.PI / 3f;
        private const float ElasticPeriodInOut = 2f * Mathf.PI / 4.5f;
        private const float Ln2 = 0.6931472f;

        /// <summary>
        /// Maps a value in [0, 1] through the selected curve. The input is not clamped, and the back and
        /// elastic curves overshoot past 0 and 1.
        /// </summary>
        public static float Evaluate(this EasingType type, float value)
        {
            switch (type)
            {
                case EasingType.Linear:
                    return value;

                case EasingType.SineIn:
                    return Sine(value);
                case EasingType.SineOut:
                    return 1f - Sine(1f - value);
                case EasingType.SineInOut:
                    return (1f - Mathf.Cos(value * Mathf.PI)) * 0.5f;

                case EasingType.QuadraticIn:
                    return Quadratic(value);
                case EasingType.QuadraticOut:
                    return 1f - Quadratic(1f - value);
                case EasingType.QuadraticInOut:
                    return value < 0.5f
                        ? Quadratic(value + value) * 0.5f
                        : 1f - Quadratic(2f - value - value) * 0.5f;

                case EasingType.CubicIn:
                    return Cubic(value);
                case EasingType.CubicOut:
                    return 1f - Cubic(1f - value);
                case EasingType.CubicInOut:
                    return value < 0.5f
                        ? Cubic(value + value) * 0.5f
                        : 1f - Cubic(2f - value - value) * 0.5f;

                case EasingType.QuarticIn:
                    return Quartic(value);
                case EasingType.QuarticOut:
                    return 1f - Quartic(1f - value);
                case EasingType.QuarticInOut:
                    return value < 0.5f
                        ? Quartic(value + value) * 0.5f
                        : 1f - Quartic(2f - value - value) * 0.5f;

                case EasingType.QuinticIn:
                    return Quintic(value);
                case EasingType.QuinticOut:
                    return 1f - Quintic(1f - value);
                case EasingType.QuinticInOut:
                    return value < 0.5f
                        ? Quintic(value + value) * 0.5f
                        : 1f - Quintic(2f - value - value) * 0.5f;

                case EasingType.ExponentialIn:
                    return Exponential(value);
                case EasingType.ExponentialOut:
                    return 1f - Exponential(1f - value);
                case EasingType.ExponentialInOut:
                    return value < 0.5f
                        ? Exponential(value + value) * 0.5f
                        : 1f - Exponential(2f - value - value) * 0.5f;

                case EasingType.CircularIn:
                    return Circular(value);
                case EasingType.CircularOut:
                    return 1f - Circular(1f - value);
                case EasingType.CircularInOut:
                    return value < 0.5f
                        ? Circular(value + value) * 0.5f
                        : 1f - Circular(2f - value - value) * 0.5f;

                case EasingType.BackIn:
                    return Back(value);
                case EasingType.BackOut:
                    return 1f - Back(1f - value);
                case EasingType.BackInOut:
                {
                    if (value < 0.5f)
                    {
                        var x = value + value;
                        return x * x * ((BackOvershootInOut + 1f) * x - BackOvershootInOut) * 0.5f;
                    }
                    var y = value + value - 2f;
                    return (y * y * ((BackOvershootInOut + 1f) * y + BackOvershootInOut) + 2f) * 0.5f;
                }

                case EasingType.ElasticIn:
                    return Elastic(value);
                case EasingType.ElasticOut:
                    return 1f - Elastic(1f - value);
                case EasingType.ElasticInOut:
                {
                    if (value <= 0f || value >= 1f) return value;
                    var swing = Mathf.Sin((20f * value - 11.125f) * ElasticPeriodInOut);
                    return value < 0.5f
                        ? Exp2(20f * value - 10f) * swing * -0.5f
                        : Exp2(10f - 20f * value) * swing * 0.5f + 1f;
                }

                case EasingType.BounceIn:
                    return 1f - BounceOut(1f - value);
                case EasingType.BounceOut:
                    return BounceOut(value);
                case EasingType.BounceInOut:
                    return value < 0.5f
                        ? (1f - BounceOut(1f - value - value)) * 0.5f
                        : (1f + BounceOut(value + value - 1f)) * 0.5f;

                case EasingType.Smoothstep:
                    return value * value * (3f - value - value);
                case EasingType.Smootherstep:
                    return value * value * value * (value * (value * 6f - 15f) + 10f);

                case EasingType.StepStart:
                    return value > 0f ? 1f : 0f;
                case EasingType.StepEnd:
                    return value >= 1f ? 1f : 0f;

                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Sine(float t) => 1f - Mathf.Cos(t * (Mathf.PI * 0.5f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Quadratic(float t) => t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Cubic(float t) => t * t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Quartic(float t)
        {
            var square = t * t;
            return square * square;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Quintic(float t)
        {
            var square = t * t;
            return square * square * t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Exponential(float t) => t <= 0f ? 0f : Exp2(10f * t - 10f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Circular(float t) => 1f - Mathf.Sqrt(1f - t * t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Back(float t) => ((BackOvershoot + 1f) * t - BackOvershoot) * t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Exp2(float t) => Mathf.Exp(t * Ln2);

        private static float Elastic(float t)
        {
            if (t <= 0f) return 0f;
            if (t >= 1f) return 1f;
            return -Exp2(10f * t - 10f) * Mathf.Sin((10f * t - 10.75f) * ElasticPeriod);
        }

        private static float BounceOut(float value)
        {
            const float n = 7.5625f;
            const float d = 2.75f;
            if (value < 1f / d) return n * value * value;
            if (value < 2f / d)
            {
                value -= 1.5f / d;
                return n * value * value + 0.75f;
            }
            if (value < 2.5f / d)
            {
                value -= 2.25f / d;
                return n * value * value + 0.9375f;
            }
            value -= 2.625f / d;
            return n * value * value + 0.984375f;
        }
    }
}
