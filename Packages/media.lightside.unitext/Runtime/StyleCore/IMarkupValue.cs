namespace LightSide
{
    /// <summary>
    /// A serializable parameter value with a custom single-token markup form. Implement on a struct used as a
    /// <see cref="ParameterAttribute"/> field so the inspector and markup round-trip it through one token
    /// (e.g. a colour-or-swatch, or a mode-plus-value union). The editor discovers this by interface, so a
    /// user-defined modifier can add its own parameter types without touching the package. Simple types
    /// (numbers, enums, colours, <see cref="UnitValue"/>) don't need this — they round-trip generically.
    /// </summary>
    public interface IMarkupValue
    {
        /// <summary>Serialize to the markup token (empty string for the default/unset form).</summary>
        string ToToken();

        /// <summary>Restore from a markup token — as produced by <see cref="ToToken"/> or authored in markup.</summary>
        void FromToken(string token);
    }
}
