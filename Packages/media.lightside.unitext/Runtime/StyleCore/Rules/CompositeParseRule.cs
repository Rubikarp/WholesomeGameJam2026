using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Combines multiple parse rules into a single rule.</summary>
    /// <remarks>
    /// Tries each child rule in order until one matches. Useful for grouping
    /// related rules or creating reusable rule sets.
    /// </remarks>
    [Serializable]
    [TypeGroup("Utility", 3)]
    [TypeDescription("Combines multiple parse rules into one.")]
    public sealed partial class CompositeParseRule : ParseRule
    {
        /// <summary>Child rules to apply in order.</summary>
        [Tooltip("Child rules to apply in order.")]
        [StateList(nameof(ApplyRulesChange), Validator = nameof(ValidateRulesMutation), Owned = true)]
        [SerializeField] private TypedList<ParseRule> rules = new();

        private void ApplyRulesChange() => NotifyStructureChanged();

        private void ValidateRulesMutation(in StateListMutation<ParseRule> mutation)
        {
            for (var i = 0; i < mutation.Count; i++)
                if (ReferenceEquals(mutation[i], this))
                    throw new InvalidOperationException(
                        "A CompositeParseRule cannot contain itself.");
            RangeSource.ValidateGraphs(in mutation);
        }

        internal override IReadOnlyList<RangeSource> Children => rules.Items;

        /// <summary>The children carry the syntax identities; the composite itself has none.</summary>
        public override string Identity => null;

        public override string MarkupTriggers => ParseRule.MarkupTriggerUnion(rules);

        public override string TypingTriggers => ParseRule.TypingTriggerUnion(rules);

        public override string ScanTriggers => ParseRule.ScanTriggerUnion(rules);

        public override string SourceToken
        {
            get
            {
                for (var i = 0; i < rules.Count; i++)
                    if (rules[i]?.SourceToken is { } token) return token;
                return null;
            }
        }

        /// <summary>
        /// The syntax-family lookup mirroring <see cref="CompositeModifier.FindLeaf"/>: returns
        /// <paramref name="root"/> itself, or the first composite child (depth-first), of type
        /// <typeparamref name="T"/>; null when none. Syntax-specific consumers address the family
        /// through here so composites are never invisible.
        /// </summary>
        public static T FindLeaf<T>(ParseRule root) where T : ParseRule
        {
            if (root is T match) return match;
            if (root is not CompositeParseRule composite) return null;
            for (var i = 0; i < composite.rules.Count; i++)
            {
                var leaf = FindLeaf<T>(composite.rules[i]);
                if (leaf != null) return leaf;
            }
            return null;
        }

        /// <summary>First child's escape prefix — the composite carries its children's escaping capability.</summary>
        public override char EscapePrefix
        {
            get
            {
                for (var i = 0; i < rules.Count; i++)
                    if (rules[i] is { } rule && rule.EscapePrefix != '\0') return rule.EscapePrefix;
                return '\0';
            }
        }

        /// <summary>Wrappable when any child is — mirrors <see cref="Apply"/>'s first-wrapping-child delegation.</summary>
        public override bool CanWrap
        {
            get
            {
                for (var i = 0; i < rules.Count; i++)
                    if (rules[i] != null && rules[i].CanWrap) return true;
                return false;
            }
        }

        /// <summary>
        /// Delegates to the first child with a wrapping form, so a composite that merges differently-typed
        /// rules (a tag with a marker) stays inline-applicable in the first listed child's syntax.
        /// </summary>
        public override string Apply(ReadOnlySpan<char> content, string parameter)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var wrapped = rules[i]?.Apply(content, parameter);
                if (wrapped != null) return wrapped;
            }
            return null;
        }

        /// <summary>
        /// Highest child priority (including negative — a composite of auto-detection rules must not be
        /// promoted to explicit-markup priority 0), so longest-match ordering tries the composite as early
        /// as its strongest child. Without it the default 0 lets a shorter standalone marker (a <c>*</c>
        /// rule) preempt a composite that wraps a longer one (<c>**</c>), splitting the longer marker into
        /// the shorter.
        /// </summary>
        public override int Priority
        {
            get
            {
                var max = int.MinValue;
                for (var i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    if (rule != null && rule.Priority > max) max = rule.Priority;
                }
                return max == int.MinValue ? 0 : max;
            }
        }

        public override int TryMatch(ReadOnlySpan<char> text,int index, PooledList<ParsedRange> results)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (rule == null) continue;

                var countBefore = results.Count;
                var result = rule.TryMatch(text, index, results);
                if (result > index)
                {
                    StampSource(rule, results, countBefore);
                    return result;
                }
            }

            return index;
        }

        public override void Finalize(ReadOnlySpan<char> text,PooledList<ParsedRange> results)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                if (rules[i] == null) continue;
                var countBefore = results.Count;
                rules[i].Finalize(text, results);
                StampSource(rules[i], results, countBefore);
            }
        }

        private static void StampSource(ParseRule rule, PooledList<ParsedRange> results, int from)
        {
            for (var i = from; i < results.Count; i++)
            {
                ref var range = ref results[i];
                range.sourceRule ??= rule;
            }
        }

        public override void PostParse(ReadOnlySpan<char> cleanText, PooledList<ParsedRange> results)
        {
            for (var i = 0; i < rules.Count; i++)
                rules[i]?.PostParse(cleanText, results);
        }

        public override void Reset()
        {
            for (var i = 0; i < rules.Count; i++)
                rules[i]?.Reset();
        }
    }

}
