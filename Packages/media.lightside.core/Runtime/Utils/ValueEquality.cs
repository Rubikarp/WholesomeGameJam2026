using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Equality for a generic value that never allocates. <see cref="Color32"/> implements neither
    /// <see cref="IEquatable{T}"/> nor an <c>Equals(object)</c> override, so
    /// <see cref="EqualityComparer{T}.Default"/> resolves it to a comparer that boxes both operands
    /// on every call; it is substituted with a typed one. Every other type — including any further
    /// value type shipped without <see cref="IEquatable{T}"/> — keeps the default comparer.
    /// </summary>
    public static class ValueEquality<T>
    {
        private static readonly IEqualityComparer<T> typed = ResolveTyped();

        /// <summary>Whether two values are equal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Same(T left, T right)
            => typed == null
                ? EqualityComparer<T>.Default.Equals(left, right)
                : typed.Equals(left, right);

        private static IEqualityComparer<T> ResolveTyped()
            => typeof(T) == typeof(Color32)
                ? (IEqualityComparer<T>)(object)Color32Equality.Instance
                : null;
    }

    internal sealed class Color32Equality : IEqualityComparer<Color32>
    {
        internal static readonly Color32Equality Instance = new();

        public bool Equals(Color32 left, Color32 right)
            => left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;

        public int GetHashCode(Color32 value)
            => value.r | (value.g << 8) | (value.b << 16) | (value.a << 24);
    }
}
