using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Which source a <see cref="PaintRef"/> draws from.</summary>
    public enum PaintRefKind : byte
    {
        /// <summary>No paint chosen — the layer applies its own default (inherit the text fill, or a built-in colour).</summary>
        Default,
        /// <summary>An inline solid colour (<see cref="PaintRef.color"/>).</summary>
        Color,
        /// <summary>A named swatch (<see cref="PaintRef.swatch"/>) resolved through the layer's paint provider — may be solid, gradient, or texture.</summary>
        Swatch
    }

    /// <summary>
    /// A paint layer's paint choice: an inline colour, a named provider swatch, or the layer default.
    /// This is the authored value on the modifier (and the default when markup omits the paint); the resolved
    /// runtime appearance is <see cref="TextPaint"/>.
    /// </summary>
    [Serializable]
    public struct PaintRef : IMarkupValue, IEquatable<PaintRef>
    {
        public PaintRefKind kind;
        public Color32 color;
        public string swatch;

        /// <summary>An inline solid-colour paint.</summary>
        public static PaintRef Solid(Color32 color) => new() { kind = PaintRefKind.Color, color = color };

        /// <summary>A paint referencing a named provider swatch.</summary>
        public static PaintRef Named(string swatch) => new() { kind = PaintRefKind.Swatch, swatch = swatch };

        /// <summary>No explicit paint — the layer falls back to its own default.</summary>
        public bool IsDefault => kind == PaintRefKind.Default;

        public string ToToken() => kind switch
        {
            PaintRefKind.Color => $"#{color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}",
            PaintRefKind.Swatch => swatch ?? "",
            _ => ""
        };

        public void FromToken(string token)
        {
            if (string.IsNullOrEmpty(token)) this = default;
            else if (token[0] == '#' && ColorParsing.TryParse(token, out var c)) this = Solid(c);
            else this = Named(token);
        }

        public bool Equals(PaintRef other) =>
            kind == other.kind && swatch == other.swatch &&
            color.r == other.color.r && color.g == other.color.g &&
            color.b == other.color.b && color.a == other.color.a;

        public override bool Equals(object obj) => obj is PaintRef o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(kind, color.r, color.g, color.b, color.a, swatch);
    }
}
