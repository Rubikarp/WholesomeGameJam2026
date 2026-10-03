using UnityEngine;

public static class Extension_Number
{
    public static float PercentageOf(this int part, int whole)
    {
        if (whole == 0) return 0; // Handling division by zero
        return (float)part / whole;
    }

    public static bool IsOdd(this int i) => i % 2 == 1;
    public static bool IsEven(this int i) => i % 2 == 0;

    public static int AtLeast(this int value, int min) => Mathf.Max(value, min);
    public static float AtLeast(this float value, float min) => Mathf.Max(value, min);

    public static int AtMost(this int value, int max) => Mathf.Min(value, max);
    public static float AtMost(this float value, float max) => Mathf.Min(value, max);

    private static readonly (int Value, string Numeral)[] _RomanMap =
    {
        (1000, "M"), (900, "CM"),
        (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"),
        (5, "V"), (4, "IV"),
        (1, "I")
    };

    public static string ToRoman(this int number)
    {
        if (number <= 0 || number > 3999)
            return number.ToString();

        var sb = new System.Text.StringBuilder(15);

        foreach (var (value, numeral) in _RomanMap)
        {
            while (number >= value)
            {
                sb.Append(numeral);
                number -= value;
            }
        }

        return sb.ToString();
    }
}