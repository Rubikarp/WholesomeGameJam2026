using System;

namespace LightSide
{
    /// <summary>
    /// <see cref="TagRule"/> with HTML void-element semantics: every <c>&lt;tag&gt;</c> or
    /// <c>&lt;tag=value&gt;</c> behaves as a single self-closing placeholder, the <c>/&gt;</c>
    /// shorthand is no longer required, and any stray <c>&lt;/tag&gt;</c> is silently stripped.
    /// </summary>
    /// <remarks>
    /// Use for inline-media tags (<c>obj</c>, <c>sprite</c>) where range semantics don't apply —
    /// the tag is always a single inline insertion, not a wrapper around following text.
    /// </remarks>
    [Serializable]
    [TypeGroup("Tags", 0)]
    [TypeDescription("XML-like void tag: <name> always behaves as a self-closing single insertion.")]
    public sealed class InlineTagRule : TagRule
    {
        public InlineTagRule() { }
        public InlineTagRule(string tagName) : base(tagName) { }

        protected override bool IsVoid => true;
    }
}
