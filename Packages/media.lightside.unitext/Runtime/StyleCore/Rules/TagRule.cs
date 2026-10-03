using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Universal tag parse rule configured via a serialized tag name.
    /// Replaces all individual tag parse rule classes (BoldParseRule, ColorParseRule, etc.).
    /// </summary>
    /// <remarks>
    /// Parameters are always optional. Self-closing is syntax-driven via /&gt;.
    /// <list type="bullet">
    /// <item><c>&lt;tag&gt;text&lt;/tag&gt;</c> — range with no parameter</item>
    /// <item><c>&lt;tag=value&gt;text&lt;/tag&gt;</c> — range with parameter</item>
    /// <item><c>&lt;tag/&gt;</c> — self-closing, no parameter</item>
    /// <item><c>&lt;tag=value/&gt;</c> — self-closing with parameter</item>
    /// </list>
    /// When <see cref="DefaultParameter"/> is set, tags without parameters use it as a fallback.
    /// Tags with partial parameters merge with the default (tag values take priority).
    /// </remarks>
    [Serializable]
    [TypeGroup("Tags", 0)]
    [TypeDescription("Activates the modifier using an XML-like tag with a configurable name.")]
    public partial class TagRule : TagParseRule
    {
        /// <summary>Tag name matched without angle brackets.</summary>
        [FormerlySerializedAs("tagName")]
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField] private string tag = "tag";
        /// <summary>
        /// Fallback merged token-wise into matched ranges when the tag carries no value or a
        /// partial one (authored value wins each slot). Empty = the modifier's own field defaults.
        /// </summary>
        [StateProperty(nameof(NotifyChanged))]
        [SerializeField, DefaultParameter] private string defaultParameter;

        public TagRule() { }

        public TagRule(string tagName) => tag = tagName;

        /// <summary>
        /// Configured tag name (without angle brackets). Consumers walking the active
        /// style set use this to pair the rule with its modifier — clipboard adapters
        /// emit / accept tags by this name, custom editors and inspectors render it,
        /// markup-to-HTML round-trip code reads it to derive the external element.
        /// </summary>
        public string Name => tag;

        public override string TagName => tag;

        public override string ToString()
            => string.IsNullOrEmpty(tag) ? "Tag" : $"Tag [{tag}]";

        public override int TryMatch(ReadOnlySpan<char> text, int index, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(DefaultParameter))
                return base.TryMatch(text, index, results);

            var countBefore = results.Count;
            var result = base.TryMatch(text, index, results);
            if (results.Count > countBefore)
                ApplyDefaults(results, countBefore);
            return result;
        }

        public override void Finalize(ReadOnlySpan<char> text, PooledList<ParsedRange> results)
        {
            if (string.IsNullOrEmpty(DefaultParameter))
            {
                base.Finalize(text, results);
                return;
            }

            var countBefore = results.Count;
            base.Finalize(text, results);
            if (results.Count > countBefore)
                ApplyDefaults(results, countBefore);
        }

        private void ApplyDefaults(PooledList<ParsedRange> results, int fromIndex)
        {
            for (var i = fromIndex; i < results.Count; i++)
            {
                ref var range = ref results[i];
                range.SetDefaultParameter(defaultParameter);
            }
        }
    }
}
