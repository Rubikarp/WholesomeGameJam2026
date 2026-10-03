using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A rectangle in screen space, as window placement requires.
    /// </summary>
    /// <remarks>
    /// Panel coordinates convert correctly only inside the layout pass that produced them, because the
    /// conversion adds the origin of whichever window is drawing. Carrying the space in the type is what
    /// keeps a panel rectangle from reaching a placement API one event too late, where it resolves against
    /// the wrong window and places the popup somewhere else entirely.
    /// </remarks>
    public readonly struct ScreenRect : IEquatable<ScreenRect>
    {
        /// <summary>Wraps a rectangle already expressed in screen space.</summary>
        public ScreenRect(Rect screenSpace) => Value = screenSpace;

        /// <summary>The screen-space rectangle.</summary>
        public Rect Value { get; }

        /// <summary>The rectangle's width.</summary>
        public float Width => Value.width;

        /// <summary>The rectangle's height.</summary>
        public float Height => Value.height;

        /// <summary>The rectangle's centre.</summary>
        public Vector2 Center => Value.center;

        /// <summary>
        /// Converts a panel rectangle, which is valid only while the layout pass that produced it is still
        /// running.
        /// </summary>
        public static ScreenRect FromPanel(Rect panelRect) =>
            new(GUIUtility.GUIToScreenRect(panelRect));

        /// <inheritdoc/>
        public bool Equals(ScreenRect other) => Value.Equals(other.Value);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ScreenRect other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Value.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => Value.ToString();

        /// <summary>Compares two screen rectangles for equality.</summary>
        public static bool operator ==(ScreenRect left, ScreenRect right) => left.Equals(right);

        /// <summary>Compares two screen rectangles for inequality.</summary>
        public static bool operator !=(ScreenRect left, ScreenRect right) => !left.Equals(right);
    }
}
