using System;

namespace LightSide
{
    /// <summary>Monotonic version of the rendered text snapshot used by a range source.</summary>
    public readonly struct TextRevision : IEquatable<TextRevision>
    {
        /// <summary>Monotonic revision value; zero denotes an unavailable snapshot.</summary>
        public ulong Value { get; }

        /// <summary>Whether this revision identifies a completed text snapshot.</summary>
        public bool IsValid => Value != 0;

        /// <summary>Creates a revision value. Zero is reserved for the invalid default value.</summary>
        public TextRevision(ulong value)
        {
            if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        /// <inheritdoc/>
        public bool Equals(TextRevision other) => Value == other.Value;
        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is TextRevision other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();
        /// <inheritdoc/>
        public override string ToString() => Value.ToString();

        /// <summary>Compares two revision values.</summary>
        public static bool operator ==(TextRevision left, TextRevision right) => left.Equals(right);

        /// <summary>Compares two revision values.</summary>
        public static bool operator !=(TextRevision left, TextRevision right) => !left.Equals(right);
    }
}
