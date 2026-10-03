using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Reads the pre-paints <c>UnityEngine.Gradient</c> YAML block (key0/ctime0/m_Mode) from raw text and
    /// converts it to <see cref="Gradient"/>. Shared by every migration that meets legacy gradient
    /// data — the source types are gone, so the values can only be recovered from serialized text.
    /// </summary>
    internal static class LegacyGradientYaml
    {
        public static UnityEngine.Gradient ParseUnityGradientBlock(string[] lines, int start, out int mode)
        {
            mode = 0;
            var colors = new Color[8];
            var ctimes = new float[8];
            var atimes = new float[8];
            var numColor = 2;
            var numAlpha = 2;
            var sawAny = false;

            for (var i = start; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("- name:", System.StringComparison.Ordinal) || line.StartsWith("---", System.StringComparison.Ordinal))
                    break;

                if (line.StartsWith("key", System.StringComparison.Ordinal))
                {
                    var idx = line[3] - '0';
                    if (idx >= 0 && idx < 8 && TryParseInlineColor(line, out var c)) { colors[idx] = c; sawAny = true; }
                }
                else if (line.StartsWith("ctime", System.StringComparison.Ordinal))
                {
                    var idx = line[5] - '0';
                    if (idx >= 0 && idx < 8 && TryParseIntValue(line, out var v)) ctimes[idx] = v / 65535f;
                }
                else if (line.StartsWith("atime", System.StringComparison.Ordinal))
                {
                    var idx = line[5] - '0';
                    if (idx >= 0 && idx < 8 && TryParseIntValue(line, out var v)) atimes[idx] = v / 65535f;
                }
                else if (line.StartsWith("m_Mode:", System.StringComparison.Ordinal)) TryParseIntValue(line, out mode);
                else if (line.StartsWith("m_NumColorKeys:", System.StringComparison.Ordinal)) TryParseIntValue(line, out numColor);
                else if (line.StartsWith("m_NumAlphaKeys:", System.StringComparison.Ordinal)) { TryParseIntValue(line, out numAlpha); break; }
            }

            if (!sawAny) return null;

            numColor = Mathf.Clamp(numColor, 1, 8);
            numAlpha = Mathf.Clamp(numAlpha, 1, 8);

            var colorKeys = new GradientColorKey[numColor];
            for (var i = 0; i < numColor; i++) colorKeys[i] = new GradientColorKey(colors[i], ctimes[i]);

            var alphaKeys = new GradientAlphaKey[numAlpha];
            for (var i = 0; i < numAlpha; i++) alphaKeys[i] = new GradientAlphaKey(colors[i].a, atimes[i]);

            var gradient = new UnityEngine.Gradient { mode = mode == 1 ? GradientMode.Fixed : GradientMode.Blend };
            gradient.SetKeys(colorKeys, alphaKeys);
            return gradient;
        }

        public static bool TryParseInlineColor(string line, out Color color)
        {
            color = default;
            var open = line.IndexOf('{');
            var close = line.IndexOf('}');
            if (open < 0 || close <= open) return false;

            var parts = line.Substring(open + 1, close - open - 1).Split(',');
            var channels = new float[4];
            var found = 0;
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                if (!float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;
                switch (kv[0].Trim())
                {
                    case "r": channels[0] = value; found++; break;
                    case "g": channels[1] = value; found++; break;
                    case "b": channels[2] = value; found++; break;
                    case "a": channels[3] = value; found++; break;
                }
            }

            if (found < 4) return false;
            color = new Color(channels[0], channels[1], channels[2], channels[3]);
            return true;
        }

        public static bool TryParseIntValue(string line, out int value)
        {
            value = 0;
            var colon = line.IndexOf(':');
            return colon >= 0 && int.TryParse(line.Substring(colon + 1).Trim(),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public static Gradient ToGradient(UnityEngine.Gradient gradient, int mode)
        {
            var times = new SortedSet<float>();
            foreach (var key in gradient.colorKeys) times.Add(Mathf.Clamp01(key.time));
            foreach (var key in gradient.alphaKeys) times.Add(Mathf.Clamp01(key.time));

            var stops = new GradientStop[times.Count];
            var i = 0;
            foreach (var t in times)
                stops[i++] = new GradientStop(t, gradient.Evaluate(t));

            return new Gradient(stops,
                mode == 1 ? GradientInterpolation.Stepped : GradientInterpolation.Smooth);
        }
    }
}
