using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Base class for text parsing rules that identify modifier application ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parse rules scan text for markup (XML tags, Markdown, custom markers) and produce
    /// <see cref="ParsedRange"/> entries that specify where modifiers should be applied.
    /// </para>
    /// <para>
    /// Rules are matched in priority order (highest first). Use higher priority for explicit
    /// markup rules (tags, Markdown) and lower priority for auto-detection rules (raw URLs).
    /// </para>
    /// </remarks>
    /// <seealso cref="TagParseRule"/>
    /// <seealso cref="AttributeParser"/>
    [Serializable]
    [TypeMenuSuffix("ParseRule", "Rule")]
    [SelectorNoneLabel("Whole Text")]
    public abstract class ParseRule : RangeSource
    {
        /// <summary>
        /// Gets the matching priority. Higher values are matched first.
        /// Default is 0. Use positive values for explicit markup, negative for auto-detection.
        /// </summary>
        public virtual int Priority => 0;

        /// <summary>
        /// The literal-escape capability: a rule that consumes an escape prefix to protect the
        /// following character from markup interpretation returns that prefix; <c>'\0'</c>
        /// (default) = the rule provides no escaping. Consumers (literal-paste serialization,
        /// escaping injection) check this contract instead of naming a concrete rule type, so a
        /// custom escaping rule participates by overriding it.
        /// </summary>
        public virtual char EscapePrefix => '\0';

        /// <summary>Whether this rule provides literal escaping — see <see cref="EscapePrefix"/>.</summary>
        public bool ProvidesLiteralEscape => EscapePrefix != '\0';

        /// <summary>Whether <see cref="EscapePrefix"/> can protect <paramref name="c"/> — the escapable set of this rule's grammar. Meaningful only when <see cref="ProvidesLiteralEscape"/>.</summary>
        public virtual bool IsEscapable(char c) => false;

        /// <summary>
        /// Whether <see cref="Apply"/> emits a content-wrapping form — i.e. the rule can express an
        /// inline style application. Rules that override <see cref="Apply"/> to return syntax must
        /// also override this; consumers gate on it instead of probing <see cref="Apply"/> with
        /// empty input.
        /// </summary>
        public virtual bool CanWrap => false;

        /// <summary>
        /// Indicates whether this rule operates without a modifier (e.g., protection rules like noparse).
        /// When <see langword="true"/>, the rule can be registered via <c>UniText.RegisterRule</c> without
        /// pairing it with a <c>BaseModifier</c>.
        /// </summary>
        public virtual bool IsStandalone => false;

        /// <summary>
        /// The source-markup token this rule's syntax is externally known by — whatever form the
        /// syntax takes (<c>color</c> for a tag rule, a marker for a marker rule) — or
        /// <see langword="null"/> for a markerless rule. Interop consumers (clipboard bindings)
        /// pair rules with format schemas by this token instead of testing syntax families.
        /// </summary>
        public virtual string SourceToken => null;

        /// <summary>
        /// Stable identity shared by rule instances that match the same syntax, compared
        /// case-insensitively — pairs separately configured instances (style merging,
        /// <see cref="ByRule"/> chrome selectors). The default (full type name) is
        /// correct for configuration-free rules. A rule whose instances can be configured to
        /// match different syntaxes MUST qualify it (<see cref="TagRule"/> → <c>"tag:" +
        /// name</c>, marker rule → <c>"marker:" + marker</c>) or return <see langword="null"/>
        /// (identity by reference only) — otherwise differently configured instances are
        /// falsely treated as the same rule. Called on hot paths — cache the string, don't
        /// rebuild it per access.
        /// </summary>
        public override string Identity => GetType().FullName;

        /// <summary>
        /// Characters of which at least one must start any match of this rule that alters the visible
        /// text — strips markup (tags, markers), replaces it, or inserts content. Empty = the rule
        /// never consumes text (style-only matches like raw-URL detection); <see langword="null"/>
        /// (default) = unknown, callers must assume any text can be consumed. Literal-paste escaping
        /// protects exactly these characters, so under-reporting silently mutates pasted text.
        /// </summary>
        public virtual string MarkupTriggers => null;

        /// <summary>
        /// Characters whose insertion or removal can make this rule begin or complete a consuming markup
        /// match. The editable document parses typed syntax only when an edit contains one of these characters
        /// or touches its immediate boundary; empty means this rule has no source syntax, while
        /// <see langword="null"/> conservatively parses every direct typing edit. Include closing delimiters and
        /// required separators, not only match starts.
        /// </summary>
        public virtual string TypingTriggers => null;

        /// <summary>
        /// Characters of which at least one must be at the match position for <see cref="TryMatch"/> to
        /// succeed at all (consuming or style-only). Defaults to <see cref="MarkupTriggers"/> — override
        /// when the rule matches at characters beyond its consuming set (raw-URL scheme letters).
        /// The parser bakes the union into a jump table and skips runs of non-trigger text, so an
        /// accurate set here is a large parse speedup; <see langword="null"/> disables the fast scan
        /// for the whole component.
        /// </summary>
        public virtual string ScanTriggers => MarkupTriggers;

        /// <summary>
        /// Deduplicated union of the rules' <see cref="MarkupTriggers"/>, or <see langword="null"/> when any
        /// rule reports unknown — shared by the parser and <see cref="CompositeParseRule"/> to advertise the
        /// combined trigger set. Null rules are skipped.
        /// </summary>
        public static string MarkupTriggerUnion(IReadOnlyList<ParseRule> rules) => TriggerUnion(rules, TriggerKind.Markup);

        /// <summary>Deduplicated union of the rules' <see cref="ScanTriggers"/>; null when any rule reports unknown.</summary>
        public static string ScanTriggerUnion(IReadOnlyList<ParseRule> rules) => TriggerUnion(rules, TriggerKind.Scan);

        /// <summary>Deduplicated union of the rules' <see cref="TypingTriggers"/>; null when every direct typing edit must be considered.</summary>
        public static string TypingTriggerUnion(IReadOnlyList<ParseRule> rules) => TriggerUnion(rules, TriggerKind.Typing);

        private enum TriggerKind : byte { Markup, Scan, Typing }

        private static string TriggerUnion(IReadOnlyList<ParseRule> rules, TriggerKind kind)
        {
            var union = "";
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null) continue;
                var triggers = kind switch
                {
                    TriggerKind.Scan => rule.ScanTriggers,
                    TriggerKind.Typing => rule.TypingTriggers,
                    _ => rule.MarkupTriggers,
                };
                if (triggers == null) return null;
                for (var j = 0; j < triggers.Length; j++)
                    if (union.IndexOf(triggers[j]) < 0)
                        union += triggers[j];
            }
            return union;
        }

        /// <summary>
        /// <see cref="Identity"/> builder for configurable rules: returns <c>prefix + name</c> cached
        /// against the name's string instance, so hot-path Identity reads allocate only when the
        /// configured name actually changes. Null/empty name → <see langword="null"/> identity.
        /// </summary>
        protected static string CachedIdentity(string prefix, string name, ref string cacheKey, ref string cacheValue)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!ReferenceEquals(cacheKey, name))
            {
                cacheKey = name;
                cacheValue = prefix + name;
            }
            return cacheValue;
        }

        /// <summary>
        /// Attempts to match a pattern starting at the specified index.
        /// </summary>
        /// <param name="text">The text to scan.</param>
        /// <param name="index">Starting character index.</param>
        /// <param name="results">List to add parsed ranges to.</param>
        /// <returns>Index after the match, or same index if no match.</returns>
        public abstract int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results);

        /// <summary>Called after parsing completes to finalize any pending ranges (e.g., unclosed tags).</summary>
        public virtual void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results) { }

        /// <summary>Called after tag stripping to add ranges in clean-text space.</summary>
        public virtual void PostParse(ReadOnlySpan<char> cleanText, PooledList<ParsedRange> results) { }

        /// <summary>Whether a matched occurrence is complete enough to become persistent document markup.</summary>
        public virtual bool IsCompleteMatch(in ParsedRange range)
            => range.IsSelfClosing || range.closeEnd > range.closeStart || range.closeStart < 0;

        /// <summary>Resets the rule state for a new parse operation.</summary>
        public virtual void Reset() { }

        /// <summary>
        /// Emits this rule's source syntax wrapping <paramref name="content"/> — the inverse of parsing,
        /// used by the editing layer to apply a style and to reconstruct copied markup. Returns
        /// <see langword="null"/> when the rule has no content-wrapping form (void, block, or composite
        /// rules), meaning it cannot be applied as an inline style.
        /// </summary>
        public virtual string Apply(ReadOnlySpan<char> content, string parameter) => null;

    }
}
