using System;
using UnityEngine;

public static class Extension_Color
{
    public static Color With(this Color color, float? redChannel = null, float? greenChannel = null, float? blueChannel = null)
        => new Color(redChannel ?? color.r, greenChannel ?? color.g, blueChannel ?? color.b);
    public static Color WithAlpha(this Color color, float alpha) => new(color.r, color.g, color.b, alpha);

    public static string ToHex(this Color color) => $"#{ColorUtility.ToHtmlStringRGBA(color)}";
    public static Color ToHexColor(this string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
        {
            return color;
        }
        throw new ArgumentException("Invalid hex string", nameof(hex));
    }

    public static float Saturation(this Color color)
    {
        Color.RGBToHSV(color, out _, out float s, out _);
        return s;   
    }
    public static float Value(this Color color)
    {
        Color.RGBToHSV(color, out _, out _, out float v);
        return v;  
    }
    public static float Hue(this Color color)
    { 
        Color.RGBToHSV(color, out float h, out _, out _);
        return h;
    }
}
