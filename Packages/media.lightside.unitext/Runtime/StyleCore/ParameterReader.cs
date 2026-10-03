using System;
using System.Globalization;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Steady-state allocation-free tokenizer for comma-separated modifier parameters, including
    /// quoted tokens and escaped delimiters.
    /// </summary>
    /// <example>
    /// <code>
    /// var reader = new ParameterReader(parameter);
    /// reader.NextString(out var name);
    /// reader.NextFloat(out var angle);
    /// reader.NextColor(out var color);
    /// </code>
    /// </example>
    public ref struct ParameterReader
    {
        private ReadOnlySpan<char> explicitRemaining;
        private ReadOnlySpan<char> defaultRemaining;
        private string decodedToken;
        private bool lastTokenIsSet;

        public ParameterReader(ReadOnlySpan<char> parameter)
        {
            explicitRemaining = parameter;
            defaultRemaining = default;
            decodedToken = null;
            lastTokenIsSet = false;
        }

        public ParameterReader(string parameter) : this(parameter.AsSpan()) { }

        internal ParameterReader(ReadOnlySpan<char> explicitValue, ReadOnlySpan<char> defaultValue)
        {
            explicitRemaining = explicitValue;
            defaultRemaining = defaultValue;
            decodedToken = null;
            lastTokenIsSet = false;
        }

        /// <summary>True if there are no more tokens to read.</summary>
        public bool IsEmpty => explicitRemaining.IsEmpty && defaultRemaining.IsEmpty;

        /// <summary>Reads the next comma-delimited token after quote and escape decoding.</summary>
        public bool Next(out ReadOnlySpan<char> token)
            => Next(out token, out _);

        /// <summary>
        /// Reads the next token and reports whether the slot was explicitly set. A bare empty slot
        /// is unset; <c>""</c> and <c>''</c> are set empty strings.
        /// </summary>
        public bool Next(out ReadOnlySpan<char> token, out bool isSet)
        {
            if (explicitRemaining.IsEmpty && defaultRemaining.IsEmpty)
            {
                token = default;
                isSet = false;
                lastTokenIsSet = false;
                return false;
            }

            var hasExplicit = !explicitRemaining.IsEmpty;
            ReadOnlySpan<char> explicitRaw = default;
            if (hasExplicit)
                explicitRemaining = explicitRemaining.Slice(
                    ParameterTokenizer.Next(explicitRemaining, ',', out explicitRaw));
            var hasDefault = !defaultRemaining.IsEmpty;
            ReadOnlySpan<char> defaultRaw = default;
            if (hasDefault)
                defaultRemaining = defaultRemaining.Slice(
                    ParameterTokenizer.Next(defaultRemaining, ',', out defaultRaw));

            if (hasExplicit)
            {
                token = ParameterTokenizer.Decode(explicitRaw, out var decoded, out isSet);
                if (isSet)
                {
                    decodedToken = decoded;
                    lastTokenIsSet = true;
                    return true;
                }
            }

            if (hasDefault)
            {
                token = ParameterTokenizer.Decode(defaultRaw, out decodedToken, out isSet);
                lastTokenIsSet = isSet;
                return true;
            }

            token = default;
            decodedToken = null;
            isSet = false;
            lastTokenIsSet = false;
            return true;
        }

        /// <summary>
        /// Reads the next comma-delimited token (trimmed) without consuming it — a following
        /// <see cref="Next"/> returns the same token. Returns false if no tokens remain. The
        /// optional-slot idiom: peek, test, and call <see cref="Next"/> only on a match, so a
        /// non-matching token flows to the following slot instead of being eaten.
        /// </summary>
        public bool TryPeek(out ReadOnlySpan<char> token)
        {
            var copy = this;
            return copy.Next(out token);
        }

        /// <summary>
        /// Peek for an optional positional slot. An explicitly-empty slot (a bare comma) is a
        /// positional skip: it is consumed here and reported as absent (returns <see langword="false"/>).
        /// A non-empty token is left in place — the caller tests it and either claims it with
        /// <see cref="Next"/> or lets it flow to the following slot. Centralizes the empty-skip so a
        /// leading empty slot can never silently shift the remaining positional values.
        /// </summary>
        public bool TryPeekOptional(out ReadOnlySpan<char> token)
        {
            var copy = this;
            if (!copy.Next(out token, out var isSet)) return false;
            if (!isSet)
                Next(out _);
            return isSet;
        }

        /// <summary>
        /// Parses a span as float using InvariantCulture. For pre-extracted tokens where Next() was
        /// already called. <c>NaN</c> is recognized explicitly (the inherit sentinel of override
        /// tokens) — runtimes disagree on whether <c>float.TryParse</c> accepts it.
        /// </summary>
        public static bool ParseFloat(ReadOnlySpan<char> s, out float value)
        {
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;
            if (s.EqualsIgnoreCase("NaN"))
            {
                value = float.NaN;
                return true;
            }
            return false;
        }

        internal static bool ParseInt(ReadOnlySpan<char> token, out int value)
            => int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        /// <summary>Reads the next token as a float. Returns false if missing or unparseable; on failure <paramref name="value"/> is left at <paramref name="defaultValue"/>.</summary>
        public bool NextFloat(out float value, float defaultValue = 0f)
        {
            value = defaultValue;
            if (Next(out var token) && !token.IsEmpty && ParseFloat(token, out var parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        /// <summary>Reads the next token as a space-separated <c>"x y"</c> vector; each unparseable axis keeps <paramref name="defaultValue"/>'s. Returns false when the token is missing/empty.</summary>
        public bool NextVector2(out Vector2 value, Vector2 defaultValue = default)
        {
            value = defaultValue;
            if (!Next(out var token) || token.IsEmpty) return false;
            var space = token.IndexOf(' ');
            if (ParseFloat((space < 0 ? token : token[..space]).Trim(), out var x)) value.x = x;
            if (space >= 0 && ParseFloat(token[(space + 1)..].Trim(), out var y)) value.y = y;
            return true;
        }

        /// <summary>Reads the next token as an int. Returns false if missing or unparseable; on failure <paramref name="value"/> is left at <paramref name="defaultValue"/>.</summary>
        public bool NextInt(out int value, int defaultValue = 0)
        {
            value = defaultValue;
            if (Next(out var token) && !token.IsEmpty
                && ParseInt(token, out var parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        /// <summary>Reads the next token as a Color32 via <see cref="ColorParsing"/>. Returns false if missing or unparseable; on failure <paramref name="value"/> is left at <paramref name="defaultValue"/>.</summary>
        public bool NextColor(out Color32 value, Color32 defaultValue = default)
        {
            value = defaultValue;
            if (Next(out var token) && !token.IsEmpty && ColorParsing.TryParse(token, out var parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads the next token as a value of enum <typeparamref name="T"/>. Matches the full
        /// enum-member name case-insensitively; for single-character tokens, also matches against
        /// the first letter of any member whose initial is unique within the enum (e.g. <c>r</c>
        /// → <c>Radial</c> when no other member starts with R). Members whose initial collides
        /// with another member's keep working under their full name but lose the short alias.
        /// Returns false if missing or unmatched; on failure <paramref name="value"/> is left at
        /// <paramref name="defaultValue"/>.
        /// </summary>
        public bool NextEnum<T>(out T value, T defaultValue = default) where T : struct, Enum
        {
            value = defaultValue;
            if (!Next(out var token) || token.IsEmpty) return false;

            var initial = token.Length == 1 ? EnumCache<T>.InitialIndex(token[0]) : -1;
            if (ParameterTokenExtensions.TryMatchEnum(
                    token, EnumCache<T>.Names, initial, out var index))
            {
                value = EnumCache<T>.Values[index];
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses a single token's value and unit suffix: <c>24</c>/<c>10px</c> (absolute),
        /// <c>150%</c>, <c>0.5em</c>, <c>+10</c>/<c>-5</c> (delta). A suffix-less number uses
        /// <paramref name="defaultUnit"/>. A leading sign means delta only in absolute space;
        /// in every other space it remains the value's sign.
        /// </summary>
        private static bool TryParseUnit(ReadOnlySpan<char> token, UnitKind defaultUnit,
            out float value, out UnitKind unit)
        {
            value = 0f;
            unit = defaultUnit;
            if (token.IsEmpty) return false;

            foreach (var (name, kind) in UnitNames.All)
            {
                if (token.Length <= name.Length ||
                    !token.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    continue;
                unit = kind;
                return ParseFloat(token[..^name.Length], out value);
            }

            if (defaultUnit == UnitKind.Absolute &&
                (token[0] == '+' || token[0] == '-' && token.Length > 1))
                unit = UnitKind.Delta;

            return ParseFloat(token, out value);
        }

        /// <summary>
        /// Reads the next token as a float with an optional unit suffix.
        /// Missing or unparseable → <paramref name="defaultValue"/>, <see cref="UnitKind.Absolute"/>, false.
        /// </summary>
        public bool NextUnitFloat(out float value, out UnitKind unit, float defaultValue = 0f)
        {
            if (Next(out var token) && !token.IsEmpty && TryParseUnit(
                    token, UnitKind.Absolute, out value, out unit))
                return true;
            value = defaultValue;
            unit = UnitKind.Absolute;
            return false;
        }

        /// <summary>Reads the next unit token, falling back to <paramref name="fallback"/>'s value and unit when absent/unparseable. The fallback's unit is also the default coordinate space for a suffix-less number.</summary>
        /// <returns><see langword="true"/> only when a token was parsed; the fallback is still written to the output values when this returns <see langword="false"/>.</returns>
        public bool NextUnitFloat(out float value, out UnitKind unit, UnitValue fallback)
        {
            if (Next(out var token) && !token.IsEmpty && TryParseUnit(
                    token, fallback.unit, out value, out unit))
                return true;
            value = fallback.value;
            unit = fallback.unit;
            return false;
        }

        /// <summary>
        /// Reads the next token as a space-separated <c>"x y"</c> vector where each axis may carry a unit
        /// suffix (<c>0.1em -0.1em</c>). The reported <paramref name="unit"/> is <see cref="UnitKind.Em"/>
        /// if either axis is em; every other combination resolves to <see cref="UnitKind.Absolute"/>.
        /// Missing/empty token keeps <paramref name="defaultValue"/> and returns false.
        /// </summary>
        public bool NextUnitVector2(out Vector2 value, out UnitKind unit, Vector2 defaultValue = default)
            => NextUnitVector2(out value, out unit, defaultValue, UnitKind.Absolute);

        /// <summary>Reads the next unit-vector token, falling back to <paramref name="fallback"/>'s value and unit when absent. The fallback's unit is also the default coordinate space for a suffix-less axis.</summary>
        /// <returns><see langword="true"/> only when a token was parsed; the fallback is still written to the output values when this returns <see langword="false"/>.</returns>
        public bool NextUnitVector2(out Vector2 value, out UnitKind unit, UnitVector2 fallback)
        {
            if (NextUnitVector2(out value, out unit, fallback.value, fallback.unit))
                return true;
            unit = fallback.unit;
            return false;
        }

        private bool NextUnitVector2(out Vector2 value, out UnitKind unit, Vector2 defaultValue, UnitKind defaultUnit)
        {
            value = defaultValue;
            unit = defaultUnit;
            if (!Next(out var token) || token.IsEmpty) return false;

            var space = token.IndexOf(' ');
            var xToken = (space < 0 ? token : token[..space]).Trim();
            var yToken = space < 0 ? default : token[(space + 1)..].Trim();
            var xUnit = defaultUnit;
            var yUnit = defaultUnit;
            if (TryParseUnit(xToken, defaultUnit, out var x, out xUnit)) value.x = x;
            if (!yToken.IsEmpty && TryParseUnit(yToken, defaultUnit, out var y, out yUnit))
                value.y = y;
            unit = xUnit == UnitKind.Em || yUnit == UnitKind.Em
                ? UnitKind.Em
                : UnitKind.Absolute;
            return true;
        }

        /// <summary>Reads the next token as a string (allocates). Returns false if missing or empty.</summary>
        public bool NextString(out string value)
        {
            if (Next(out var token) && lastTokenIsSet)
            {
                value = token.ToString();
                return true;
            }

            value = null;
            return false;
        }
    }

    /// <summary>
    /// Keyword-matching helpers for modifier/rule parameter tokens (equality lives in
    /// <see cref="StringExtensions"/>), so parsers never hand-roll <c>StringComparison</c> boilerplate.
    /// </summary>
    public static class ParameterTokenExtensions
    {
        internal const int EnumInitialMapSize = 128;

        /// <summary>True when <paramref name="token"/> is a non-empty case-insensitive prefix of <paramref name="word"/> — so <c>dot</c> matches <c>dotted</c>. Empty never matches; use for abbreviated keywords.</summary>
        public static bool IsPrefixOf(this ReadOnlySpan<char> token, ReadOnlySpan<char> word)
            => !token.IsEmpty && word.StartsWith(token, StringComparison.OrdinalIgnoreCase);

        /// <summary>Parses a boolean keyword by prefix (<c>t</c>/<c>true</c>/<c>yes</c>/<c>on</c>/<c>1</c> → true; <c>f</c>/<c>false</c>/<c>no</c>/<c>off</c>/<c>0</c> → false), preferring true on an ambiguous prefix. Unmatched → <paramref name="defaultValue"/>.</summary>
        public static bool AsBool(this ReadOnlySpan<char> token, bool defaultValue = false)
        {
            return TryParseBool(token, out var value) ? value : defaultValue;
        }

        internal static bool TryParseBool(ReadOnlySpan<char> token, out bool value)
        {
            if (token.IsPrefixOf("true") || token.IsPrefixOf("yes") ||
                token.IsPrefixOf("on") || token.IsPrefixOf("1"))
            {
                value = true;
                return true;
            }

            if (token.IsPrefixOf("false") || token.IsPrefixOf("no") ||
                token.IsPrefixOf("off") || token.IsPrefixOf("0"))
            {
                value = false;
                return true;
            }

            value = false;
            return false;
        }

        internal static bool TryMatchEnum(ReadOnlySpan<char> token, string[] names,
            out int index)
        {
            index = -1;
            for (var i = 0; i < names.Length; i++)
            {
                if (!token.Equals(names[i].AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
                index = i;
                return true;
            }

            if (token.Length != 1) return false;
            var initial = char.ToLowerInvariant(token[0]);
            if (initial >= EnumInitialMapSize) return false;
            for (var i = 0; i < names.Length; i++)
            {
                if (names[i].Length == 0 || char.ToLowerInvariant(names[i][0]) != initial) continue;
                if (index >= 0)
                {
                    index = -1;
                    return false;
                }
                index = i;
            }
            return index >= 0;
        }

        internal static bool TryMatchEnum(ReadOnlySpan<char> token, string[] names,
            int uniqueInitialIndex, out int index)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (!token.Equals(names[i].AsSpan(), StringComparison.OrdinalIgnoreCase)) continue;
                index = i;
                return true;
            }

            index = token.Length == 1 ? uniqueInitialIndex : -1;
            return index >= 0;
        }
    }

    internal static class EnumCache<T> where T : struct, Enum
    {
        public static readonly string[] Names = Enum.GetNames(typeof(T));
        public static readonly T[] Values = (T[])Enum.GetValues(typeof(T));

        private static readonly int[] initialToIndex = BuildInitialMap(Names);

        public static int InitialIndex(char c)
        {
            var lower = (uint)char.ToLowerInvariant(c);
            return lower < ParameterTokenExtensions.EnumInitialMapSize
                ? initialToIndex[lower]
                : -1;
        }

        private static int[] BuildInitialMap(string[] names)
        {
            var map = new int[ParameterTokenExtensions.EnumInitialMapSize];
            for (var i = 0; i < map.Length; i++) map[i] = -1;

            for (var i = 0; i < names.Length; i++)
            {
                if (names[i].Length == 0) continue;
                var c = (uint)char.ToLowerInvariant(names[i][0]);
                if (c >= ParameterTokenExtensions.EnumInitialMapSize) continue;

                if (map[c] == -1) map[c] = i;
                else map[c] = -2;
            }

            for (var i = 0; i < map.Length; i++)
                if (map[i] == -2) map[i] = -1;

            return map;
        }
    }
}
