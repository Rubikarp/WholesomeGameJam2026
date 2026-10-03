namespace LightSide
{
    /// <summary>
    /// Whether markup the user types by hand is recognized or kept literal. Applies to direct keyboard / IME
    /// editing of that text; formatting commands, programmatic styling, inline objects, and paste are unaffected.
    /// </summary>
    public enum TypingMarkupPolicy
    {
        /// <summary>Typed markup is parsed into styles (default): <c>&lt;b&gt;x&lt;/b&gt;</c> renders bold.</summary>
        Parse,

        /// <summary>Typed markup triggers are escaped to literal text: <c>&lt;b&gt;x&lt;/b&gt;</c> stays visible characters.</summary>
        Literal,
    }
}
