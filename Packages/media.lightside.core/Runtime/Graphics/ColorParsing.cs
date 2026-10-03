using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared color parsing utilities for modifiers that accept color parameters.
    /// Supports hex (#RGB, #RRGGBB, #RRGGBBAA) and named colors.
    /// </summary>
    public static class ColorParsing
    {
        public static bool TryParse(string value, out Color32 color)
        {
            return TryParse(value.AsSpan(), out color);
        }

        public static bool TryParse(ReadOnlySpan<char> value, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            if (value.IsEmpty)
                return false;
            if (value[0] == '#')
                return TryParseHex(value, out color);
            return TryParseNamed(value, out color);
        }

        public static bool TryParseHex(ReadOnlySpan<char> hex, out Color32 color)
        {
            color = new Color32(255, 255, 255, 255);
            var len = hex.Length - 1;

            if (len == 3)
            {
                if (!Ascii.TryHexDigit(hex[1], out var r) ||
                    !Ascii.TryHexDigit(hex[2], out var g) ||
                    !Ascii.TryHexDigit(hex[3], out var b))
                    return false;

                color = new Color32(
                    (byte)(r * 17),
                    (byte)(g * 17),
                    (byte)(b * 17), 255);
                return true;
            }

            if (len == 6)
            {
                if (!TryParseHexByte(hex[1], hex[2], out var r) ||
                    !TryParseHexByte(hex[3], hex[4], out var g) ||
                    !TryParseHexByte(hex[5], hex[6], out var b))
                    return false;

                color = new Color32(
                    r, g, b, 255);
                return true;
            }

            if (len == 8)
            {
                if (!TryParseHexByte(hex[1], hex[2], out var r) ||
                    !TryParseHexByte(hex[3], hex[4], out var g) ||
                    !TryParseHexByte(hex[5], hex[6], out var b) ||
                    !TryParseHexByte(hex[7], hex[8], out var a))
                    return false;

                color = new Color32(
                    r, g, b, a);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryParseHexByte(char high, char low, out byte value)
        {
            if (Ascii.TryHexDigit(high, out var highDigit) &&
                Ascii.TryHexDigit(low, out var lowDigit))
            {
                value = (byte)(highDigit * 16 + lowDigit);
                return true;
            }

            value = 0;
            return false;
        }

        private static readonly (string name, Color32 color)[] namedColors =
        {
            ("white", new Color32(255, 255, 255, 255)),
            ("black", new Color32(0, 0, 0, 255)),
            ("red", new Color32(255, 0, 0, 255)),
            ("green", new Color32(0, 128, 0, 255)),
            ("blue", new Color32(0, 0, 255, 255)),
            ("yellow", new Color32(255, 255, 0, 255)),
            ("cyan", new Color32(0, 255, 255, 255)),
            ("magenta", new Color32(255, 0, 255, 255)),
            ("orange", new Color32(255, 165, 0, 255)),
            ("purple", new Color32(128, 0, 128, 255)),
            ("gray", new Color32(128, 128, 128, 255)),
            ("grey", new Color32(128, 128, 128, 255)),
            ("lime", new Color32(0, 255, 0, 255)),
            ("brown", new Color32(165, 42, 42, 255)),
            ("pink", new Color32(255, 192, 203, 255)),
            ("navy", new Color32(0, 0, 128, 255)),
            ("teal", new Color32(0, 128, 128, 255)),
            ("olive", new Color32(128, 128, 0, 255)),
            ("maroon", new Color32(128, 0, 0, 255)),
            ("silver", new Color32(192, 192, 192, 255)),
            ("gold", new Color32(255, 215, 0, 255)),
        };

        private static bool TryParseNamed(ReadOnlySpan<char> name, out Color32 color)
        {
            for (var i = 0; i < namedColors.Length; i++)
            {
                ref readonly var entry = ref namedColors[i];
                if (entry.name.Length != name.Length ||
                    !name.EqualsIgnoreCase(entry.name.AsSpan())) continue;
                color = entry.color;
                return true;
            }

            color = default;
            return false;
        }
    }
}
