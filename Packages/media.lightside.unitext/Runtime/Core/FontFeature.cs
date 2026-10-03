using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// One OpenType feature setting: a four-character feature tag and the value shaping applies it with.
    /// </summary>
    /// <remarks>
    /// A font that does not carry the tag ignores it. Value 0 disables a feature the shaper would
    /// otherwise apply by default, such as <c>kern</c> or <c>liga</c>.
    /// </remarks>
    public readonly struct FontFeature : IEquatable<FontFeature>
    {
        /// <summary>Packed four-character OpenType tag, first character in the most significant byte.</summary>
        public uint Tag { get; }

        /// <summary>Applied value: 0 disables the feature, 1 enables it, higher values select an alternate the feature defines.</summary>
        public uint Value { get; }

        internal FontFeature(uint tag, uint value)
        {
            Tag = tag;
            Value = value;
        }

        /// <summary>
        /// Creates a setting for a one-to-four character OpenType tag, space-padded to four as the
        /// specification requires.
        /// </summary>
        /// <param name="tag">OpenType feature tag, for example <c>kern</c>, <c>liga</c> or <c>ss01</c>.</param>
        /// <param name="value">Applied value; 0 disables the feature.</param>
        /// <exception cref="ArgumentException"><paramref name="tag"/> is not one to four printable ASCII characters.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
        public FontFeature(string tag, int value = 1)
        {
            if (!TryPackTag(tag.AsSpan(), out var packed))
                throw new ArgumentException(
                    $"'{tag}' is not a one-to-four character printable ASCII OpenType tag.", nameof(tag));
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "An OpenType feature value cannot be negative.");

            Tag = packed;
            Value = (uint)value;
        }

        /// <summary>
        /// Parses one setting written as <c>tag</c>, <c>tag value</c>, <c>-tag</c> or <c>+tag</c>, where the
        /// tag may be quoted and the value may follow a space, colon or equals sign. A bare tag reads as 1,
        /// a minus-prefixed tag as 0.
        /// </summary>
        /// <returns><see langword="true"/> when the whole token was consumed as a valid setting.</returns>
        public static bool TryParse(ReadOnlySpan<char> token, out FontFeature feature)
        {
            feature = default;
            token = token.Trim();
            if (token.IsEmpty) return false;

            uint value = 1;
            if (token[0] == '+' || token[0] == '-')
            {
                if (token[0] == '-') value = 0;
                token = token.Slice(1).TrimStart();
            }

            var split = token.IndexOfAny(' ', ':', '=');
            var tag = split < 0 ? token : token.Slice(0, split).TrimEnd();
            if (split >= 0)
            {
                var number = token.Slice(split + 1).Trim();
                if (number.IsEmpty || !ParameterReader.ParseInt(number, out var parsed) || parsed < 0)
                    return false;
                value = (uint)parsed;
            }

            if (tag.Length >= 2 && (tag[0] == '"' || tag[0] == '\'') && tag[tag.Length - 1] == tag[0])
                tag = tag.Slice(1, tag.Length - 2);

            if (!TryPackTag(tag, out var packed)) return false;

            feature = new FontFeature(packed, value);
            return true;
        }

        private static bool TryPackTag(ReadOnlySpan<char> tag, out uint packed)
        {
            packed = 0;
            if (tag.IsEmpty || tag.Length > 4) return false;

            Span<char> padded = stackalloc char[4] { ' ', ' ', ' ', ' ' };
            for (var i = 0; i < tag.Length; i++)
            {
                if (tag[i] < ' ' || tag[i] > '~') return false;
                padded[i] = tag[i];
            }

            packed = HB.MakeTag(padded[0], padded[1], padded[2], padded[3]);
            return true;
        }

        /// <inheritdoc/>
        public bool Equals(FontFeature other) => Tag == other.Tag && Value == other.Value;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is FontFeature other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Tag, Value);

        /// <inheritdoc/>
        public override string ToString()
        {
            Span<char> tag = stackalloc char[4];
            for (var i = 0; i < 4; i++)
                tag[i] = (char)((Tag >> (24 - i * 8)) & 0xFF);
            return $"{tag.ToString().TrimEnd()} {Value}";
        }
    }

    /// <summary>
    /// Process-wide registry mapping OpenType feature sets to compact byte ids suitable for
    /// per-codepoint attribute storage, and back to the settings the shaper needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registry is additive: once a set is registered its id stays stable for the lifetime of the
    /// process, which is what lets shaping caches key on the id alone. Id 0 is reserved for "no
    /// features". A set is canonical — sorted by tag, one entry per tag — so equal sets always share
    /// an id, and the registry holds at most 255 of them.
    /// </para>
    /// <para>
    /// <see cref="Get"/> is lock-free and safe on shaping worker threads; registration is not and
    /// belongs to the apply phase.
    /// </para>
    /// </remarks>
    public static class FontFeatureRegistry
    {
        /// <summary>Id reserved for "no features". <see cref="Get"/> returns an empty set for it.</summary>
        public const byte Unset = 0;

        private const int MaxSets = 255;

        private static readonly Dictionary<string, byte> idBySpec = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, byte> idByCombination = new();
        private static readonly List<FontFeature> scratch = new(8);
        private static readonly object syncRoot = new();

        private static volatile FontFeature[][] sets = { Array.Empty<FontFeature>() };

        /// <summary>
        /// Registers the set written as a comma-separated list of settings — <c>kern 0</c>,
        /// <c>-kern, +dlig</c>, <c>ss01 2</c> — and returns its id. Unparseable settings are skipped;
        /// a specification that yields none, or a full registry, returns <see cref="Unset"/>.
        /// </summary>
        public static byte Register(string specification)
        {
            if (string.IsNullOrWhiteSpace(specification)) return Unset;

            lock (syncRoot)
            {
                if (idBySpec.TryGetValue(specification, out var cached)) return cached;

                scratch.Clear();
                var reader = new ParameterReader(specification);
                while (reader.Next(out var token))
                    if (FontFeature.TryParse(token, out var feature))
                        scratch.Add(feature);

                var id = RegisterCanonical(scratch);
                idBySpec[specification] = id;
                return id;
            }
        }

        /// <summary>
        /// Registers a set built in code and returns its id, <see cref="Unset"/> when the set is empty
        /// or the registry is full. Later duplicates of a tag win.
        /// </summary>
        public static byte Register(ReadOnlySpan<FontFeature> features)
        {
            if (features.IsEmpty) return Unset;

            lock (syncRoot)
            {
                scratch.Clear();
                for (var i = 0; i < features.Length; i++)
                    scratch.Add(features[i]);
                return RegisterCanonical(scratch);
            }
        }

        /// <summary>
        /// Returns the id of the union of two sets, where <paramref name="second"/> wins a tag both
        /// carry. Either side being <see cref="Unset"/> returns the other.
        /// </summary>
        public static byte Combine(byte first, byte second)
        {
            if (first == Unset) return second;
            if (second == Unset) return first;
            if (first == second) return first;

            lock (syncRoot)
            {
                var key = (first << 8) | second;
                if (idByCombination.TryGetValue(key, out var cached)) return cached;

                var snapshot = sets;
                scratch.Clear();
                if (first < snapshot.Length) scratch.AddRange(snapshot[first]);
                if (second < snapshot.Length) scratch.AddRange(snapshot[second]);

                var id = RegisterCanonical(scratch);
                idByCombination[key] = id;
                return id;
            }
        }

        /// <summary>Returns the settings of a registered id, empty for <see cref="Unset"/> or an unknown id.</summary>
        public static ReadOnlySpan<FontFeature> Get(byte id)
        {
            var snapshot = sets;
            if (id >= snapshot.Length) return ReadOnlySpan<FontFeature>.Empty;
            return snapshot[id];
        }

        private static byte RegisterCanonical(List<FontFeature> features)
        {
            Canonicalize(features);
            if (features.Count == 0) return Unset;

            var snapshot = sets;
            for (var id = 1; id < snapshot.Length; id++)
                if (Matches(snapshot[id], features))
                    return (byte)id;

            if (snapshot.Length > MaxSets) return Unset;

            var grown = new FontFeature[snapshot.Length + 1][];
            Array.Copy(snapshot, grown, snapshot.Length);
            grown[snapshot.Length] = features.ToArray();
            sets = grown;
            return (byte)snapshot.Length;
        }

        /// <summary>Sorts by tag and keeps the last setting of each tag, so equal sets compare element-wise.</summary>
        private static void Canonicalize(List<FontFeature> features)
        {
            for (var i = 1; i < features.Count; i++)
            {
                var current = features[i];
                var j = i - 1;
                while (j >= 0 && features[j].Tag > current.Tag)
                {
                    features[j + 1] = features[j];
                    j--;
                }
                features[j + 1] = current;
            }

            for (var i = features.Count - 1; i > 0; i--)
                if (features[i - 1].Tag == features[i].Tag)
                    features.RemoveAt(i - 1);
        }

        private static bool Matches(FontFeature[] set, List<FontFeature> features)
        {
            if (set.Length != features.Count) return false;
            for (var i = 0; i < set.Length; i++)
                if (!set[i].Equals(features[i]))
                    return false;
            return true;
        }
    }
}
