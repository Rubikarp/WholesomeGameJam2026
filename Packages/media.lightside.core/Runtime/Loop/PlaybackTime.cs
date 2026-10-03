using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>Time source a playback advances on.</summary>
    public enum PlaybackClock : byte
    {
        /// <summary>Game time, affected by <see cref="UnityEngine.Time.timeScale"/>.</summary>
        Scaled,

        /// <summary>Wall time, unaffected by <see cref="UnityEngine.Time.timeScale"/>.</summary>
        Unscaled,

        /// <summary>Advances only when its owner is ticked explicitly.</summary>
        Manual,
    }

    /// <summary>
    /// Stable identity of a built-in or runtime-defined playback time source. The default value is invalid;
    /// source identities are never reused during a domain lifetime.
    /// </summary>
    public readonly struct PlaybackTimeSource : IEquatable<PlaybackTimeSource>
    {
        internal readonly ushort code;

        internal PlaybackTimeSource(int index) => code = checked((ushort)(index + 1));

        internal int Index => code - 1;

        /// <summary>Game time, affected by <see cref="UnityEngine.Time.timeScale"/>.</summary>
        public static PlaybackTimeSource Scaled => new((int)PlaybackClock.Scaled);

        /// <summary>Wall time, unaffected by <see cref="UnityEngine.Time.timeScale"/>.</summary>
        public static PlaybackTimeSource Unscaled => new((int)PlaybackClock.Unscaled);

        /// <summary>A source advanced only by its owner.</summary>
        public static PlaybackTimeSource Manual => new((int)PlaybackClock.Manual);

        /// <summary>Whether this is the default value, which identifies no source.</summary>
        public bool IsDefault => code == 0;

        /// <inheritdoc/>
        public bool Equals(PlaybackTimeSource other) => code == other.code;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is PlaybackTimeSource other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => code;

        /// <inheritdoc/>
        public override string ToString() => IsDefault ? "<none>" : PlaybackTime.Name(this);

        /// <summary>Converts a serialized built-in clock to its time source.</summary>
        public static implicit operator PlaybackTimeSource(PlaybackClock clock) => PlaybackTime.Source(clock);

        /// <summary>Whether two handles identify the same time source.</summary>
        public static bool operator ==(PlaybackTimeSource left, PlaybackTimeSource right) => left.Equals(right);

        /// <summary>Whether two handles identify different time sources.</summary>
        public static bool operator !=(PlaybackTimeSource left, PlaybackTimeSource right) => !left.Equals(right);
    }

    /// <summary>Time-source identity, sampling and per-frame playback steps.</summary>
    /// <remarks>
    /// Built-in play-mode clocks preserve the full Unity delta. Editor-preview and runtime-defined source steps
    /// are clamped to <see cref="MaxStep"/> so a domain reload, asset import or breakpoint cannot complete every
    /// running preview at once.
    /// </remarks>
    public static class PlaybackTime
    {
        private const int BuiltinCount = 3;

        private static string[] names = new string[4];
        private static Func<float>[] sources = new Func<float>[4];
        private static float[] samples = new float[4];
        private static ulong[] sampledAt = CreateSampleStamps(4);
        private static int count = BuiltinCount;

        /// <summary>Longest normalized preview or runtime-defined source step, in seconds.</summary>
        public const float MaxStep = 0.034f;

        /// <summary>Number of built-in and runtime-defined sources.</summary>
        public static int Count => count;

        /// <summary>Returns the source represented by a serialized built-in <paramref name="clock"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="clock"/> is not defined.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PlaybackTimeSource Source(PlaybackClock clock)
        {
            Validate(clock);
            return new PlaybackTimeSource((int)clock);
        }

        /// <summary>
        /// Defines a named time source for the current domain. Equal names resolve to the same source only when
        /// they carry the same callback; identities remain stable and are never recycled.
        /// </summary>
        /// <remarks>
        /// The callback is sampled at most once per scheduling epoch, not once per consumer. Update and LateUpdate
        /// share one epoch; every FixedUpdate pass and editor-preview update has its own. Return elapsed seconds;
        /// the result is normalized through <see cref="Clamp"/>.
        /// </remarks>
        /// <exception cref="ArgumentException"><paramref name="name"/> is empty or already belongs to another source.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The source identity space is exhausted.</exception>
        public static PlaybackTimeSource Define(string name, Func<float> source)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A playback time source needs a name.", nameof(name));
            if (source == null) throw new ArgumentNullException(nameof(source));

            for (var i = 0; i < BuiltinCount; i++)
                if (string.Equals(name, BuiltinName(i), StringComparison.Ordinal))
                    throw new ArgumentException($"'{name}' is a built-in playback time source.", nameof(name));

            for (var i = BuiltinCount; i < count; i++)
            {
                if (!string.Equals(names[i], name, StringComparison.Ordinal)) continue;
                if (sources[i] != source)
                    throw new ArgumentException(
                        $"Playback time source '{name}' is already defined by another callback.", nameof(name));
                return new PlaybackTimeSource(i);
            }

            if (count >= ushort.MaxValue)
                throw new InvalidOperationException("All playback time source identities are in use.");
            EnsureCapacity(count + 1);
            names[count] = name;
            sources[count] = source;
            sampledAt[count] = ulong.MaxValue;
            return new PlaybackTimeSource(count++);
        }

        /// <summary>Name of <paramref name="source"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is not defined.</exception>
        public static string Name(PlaybackTimeSource source)
        {
            Validate(source);
            var index = source.Index;
            return index < BuiltinCount ? BuiltinName(index) : names[index];
        }

        /// <summary>
        /// Seconds <paramref name="clock"/> advanced this frame. <see cref="PlaybackClock.Manual"/> reports zero,
        /// because a manual owner supplies its own step.
        /// </summary>
        /// <exception cref="System.ArgumentOutOfRangeException"><paramref name="clock"/> is not defined.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Delta(PlaybackClock clock) => Delta(Source(clock));

        /// <summary>
        /// Seconds <paramref name="source"/> advanced this frame. <see cref="PlaybackTimeSource.Manual"/>
        /// reports zero because a manual owner supplies its own step.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="source"/> is not defined.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Delta(PlaybackTimeSource source)
        {
            Validate(source);
            var isPlaying = Application.isPlaying;
            var editorStep = CoreLoop.DeltaTime;
            return ResolveDelta(source,
                isPlaying ? Time.deltaTime : editorStep,
                isPlaying ? Time.unscaledDeltaTime : editorStep,
                !isPlaying);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static float Delta(PlaybackTimeSource source, bool fixedStep)
        {
            Validate(source);
            var isPlaying = Application.isPlaying;
            var editorStep = CoreLoop.DeltaTime;
            return ResolveDelta(source,
                isPlaying ? fixedStep ? Time.fixedDeltaTime : Time.deltaTime : editorStep,
                isPlaying ? fixedStep ? Time.fixedUnscaledDeltaTime : Time.unscaledDeltaTime : editorStep,
                !isPlaying);
        }

        private static float ResolveDelta(PlaybackTimeSource source, float scaled, float unscaled,
            bool normalizeBuiltins)
        {
            switch (source.Index)
            {
                case (int)PlaybackClock.Scaled:
                    return normalizeBuiltins ? Clamp(scaled) : scaled;

                case (int)PlaybackClock.Unscaled:
                    return normalizeBuiltins ? Clamp(unscaled) : unscaled;

                case (int)PlaybackClock.Manual:
                    return 0f;

                default:
                {
                    var index = source.Index;
                    var epoch = CoreLoop.SampleEpoch;
                    if (sampledAt[index] == epoch) return samples[index];
                    var sample = Clamp(sources[index]());
                    samples[index] = sample;
                    sampledAt[index] = epoch;
                    return sample;
                }
            }
        }

        /// <summary>Whether <paramref name="clock"/> names a time source this package defines.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDefined(PlaybackClock clock) => (uint)clock < BuiltinCount;

        /// <summary>Whether <paramref name="source"/> identifies a current time source.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsDefined(PlaybackTimeSource source) =>
            source.code != 0 && (uint)source.Index < (uint)count;

        internal static void Validate(PlaybackClock clock)
        {
            if (!IsDefined(clock))
                throw new ArgumentOutOfRangeException(nameof(clock), clock, null);
        }

        internal static void Validate(PlaybackTimeSource source)
        {
            if (!IsDefined(source))
                throw new ArgumentOutOfRangeException(nameof(source), source, null);
        }

        /// <summary>Limits <paramref name="seconds"/> to <see cref="MaxStep"/>, dropping negatives and NaN to zero.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Clamp(float seconds) =>
            seconds > 0f ? (seconds < MaxStep ? seconds : MaxStep) : 0f;

        private static string BuiltinName(int index) => ((PlaybackClock)index).ToString();

        private static void EnsureCapacity(int required)
        {
            if (required <= names.Length) return;
            var capacity = names.Length * 2;
            while (capacity < required) capacity *= 2;
            if (capacity > ushort.MaxValue) capacity = ushort.MaxValue;
            Array.Resize(ref names, capacity);
            Array.Resize(ref sources, capacity);
            Array.Resize(ref samples, capacity);
            var previous = sampledAt.Length;
            Array.Resize(ref sampledAt, capacity);
            for (var i = previous; i < capacity; i++) sampledAt[i] = ulong.MaxValue;
        }

        private static ulong[] CreateSampleStamps(int capacity)
        {
            var stamps = new ulong[capacity];
            for (var i = 0; i < stamps.Length; i++) stamps[i] = ulong.MaxValue;
            return stamps;
        }
    }
}
