using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Marks an override field that may carry "no value at this layer" instead of a value, deferring to
    /// the weaker layer of its chain. The sentinel is <c>NaN</c> for <see cref="float"/> and
    /// <see cref="Vector2"/> (per axis), and the negative member (see <see cref="PaintInherit.None"/>)
    /// for an enum. Enum popups offer that member only on a field carrying this attribute, so the same
    /// enum serves both a resolved value and an override.
    /// </summary>
    /// <remarks>
    /// The editor draws numeric fields as an override checkbox — unchecked stores the sentinel, checked
    /// exposes the value control initialized from the constructor value (honouring a sibling
    /// <see cref="RangeAttribute"/>). Constructor values apply to numeric fields only.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InheritableAttribute : PropertyAttribute
    {
        /// <summary>Value a numeric field takes when an editor turns the override on.</summary>
        public float ActivationX { get; }

        /// <summary>Second axis of <see cref="ActivationX"/> for a <see cref="Vector2"/> field.</summary>
        public float ActivationY { get; }

        /// <summary>Uses zero as the explicit value when inheritance is disabled in the editor.</summary>
        public InheritableAttribute() : this(0f) { }

        /// <summary>Sets the explicit scalar value, or both vector axes, used when inheritance is disabled in the editor.</summary>
        public InheritableAttribute(float activationValue) : this(activationValue, activationValue) { }

        /// <summary>Sets the explicit vector value used when inheritance is disabled in the editor.</summary>
        public InheritableAttribute(float activationX, float activationY)
        {
            ActivationX = activationX;
            ActivationY = activationY;
            order = -10;
        }
    }
}
