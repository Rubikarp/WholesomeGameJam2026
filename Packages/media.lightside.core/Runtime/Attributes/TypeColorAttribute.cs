using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Pins the editor accent colour for a type. Without it the colour is hashed from the
    /// type name, which can collide; set this when related types must stay visually distinct. Value is an
    /// HTML colour string (<c>#RRGGBB</c> or <c>#RRGGBBAA</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class TypeColorAttribute : Attribute
    {
        public Color Color { get; }

        public TypeColorAttribute(string hex)
        {
            Color = ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;
        }
    }
}
