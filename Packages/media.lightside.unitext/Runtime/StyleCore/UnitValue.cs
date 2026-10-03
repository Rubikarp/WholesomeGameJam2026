using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Unit a numeric parameter value is expressed in.</summary>
    public enum UnitKind : byte
    {
        /// <summary>Absolute pixels or a raw axis value (<c>24</c>; explicit <c>24px</c> / <c>24abs</c>).</summary>
        Absolute,
        /// <summary>Percentage of the base value (<c>150%</c>).</summary>
        Percent,
        /// <summary>Multiple of the em / font size (<c>0.5em</c>).</summary>
        Em,
        /// <summary>Signed pixel delta relative to the base (<c>+10</c> / <c>-5</c> when the context default is absolute; explicit <c>10delta</c>).</summary>
        Delta,
    }

    /// <summary>
    /// Suffix vocabulary of the unit grammar: canonical spelling per kind plus accepted aliases.
    /// A suffix-less number stays in its context's default unit; a signed suffix-less number in an
    /// absolute context reads as <see cref="UnitKind.Delta"/>.
    /// </summary>
    internal static class UnitNames
    {
        internal static readonly (string name, UnitKind kind)[] All =
        {
            ("px", UnitKind.Absolute),
            ("%", UnitKind.Percent),
            ("em", UnitKind.Em),
            ("delta", UnitKind.Delta),
            ("abs", UnitKind.Absolute),
        };

        internal static string Name(UnitKind unit) => unit switch
        {
            UnitKind.Percent => "%",
            UnitKind.Em => "em",
            UnitKind.Delta => "delta",
            _ => "px",
        };

        internal static UnitKind Kind(string name)
            => TryKind(name, out var kind)
                ? kind
                : throw new ArgumentException($"Unknown unit name '{name}'.", nameof(name));

        internal static bool TryKind(string name, out UnitKind kind)
        {
            foreach (var (candidate, candidateKind) in All)
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                {
                    kind = candidateKind;
                    return true;
                }
            kind = UnitKind.Absolute;
            return false;
        }
    }

    /// <summary>
    /// A numeric parameter value paired with the unit it is expressed in — the serialized form of a
    /// unit token (<c>24</c>, <c>150%</c>, <c>0.5em</c>, <c>+10</c>). Used as a modifier field type so
    /// the authoring default carries both number and unit in one place.
    /// </summary>
    [Serializable]
    public struct UnitValue : IEquatable<UnitValue>
    {
        public float value;
        public UnitKind unit;

        public UnitValue(float value, UnitKind unit)
        {
            this.value = value;
            this.unit = unit;
        }

        public static UnitValue Absolute(float value) => new(value, UnitKind.Absolute);
        public static UnitValue Percent(float value) => new(value, UnitKind.Percent);
        public static UnitValue Em(float value) => new(value, UnitKind.Em);
        public static UnitValue Delta(float value) => new(value, UnitKind.Delta);

        /// <summary>Resolves to pixels against <paramref name="emSize"/> (the font size): em multiplies, absolute/delta pass through.</summary>
        public float ResolvePx(float emSize) => ResolvePx(value, unit, emSize);

        /// <summary>Loose value/unit overload for values read via <c>ParameterReader.NextUnitFloat</c>.</summary>
        public static float ResolvePx(float value, UnitKind unit, float emSize) => unit == UnitKind.Em ? value * emSize : value;

        public bool Equals(UnitValue other) => value == other.value && unit == other.unit;
        public override bool Equals(object obj) => obj is UnitValue o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(value, unit);
    }

    /// <summary>A <see cref="Vector2"/> parameter value with a shared unit for both axes (e.g. <c>0.1em -0.1em</c>).</summary>
    [Serializable]
    public struct UnitVector2 : IEquatable<UnitVector2>
    {
        public Vector2 value;
        public UnitKind unit;

        public UnitVector2(Vector2 value, UnitKind unit)
        {
            this.value = value;
            this.unit = unit;
        }

        public bool Equals(UnitVector2 other) => value.Equals(other.value) && unit == other.unit;
        public override bool Equals(object obj) => obj is UnitVector2 o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(value, unit);
    }
}
