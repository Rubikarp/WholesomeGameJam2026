using System;

namespace LightSide
{
    /// <summary>
    /// Transforms text to lowercase within marked ranges.
    /// </summary>
    /// <remarks>
    /// No parameter. The transformation happens during Apply, after parsing but before shaping,
    /// ensuring correct glyph rendering for lowercase characters. Uses the bundled UCD case
    /// mapping table rather than <c>char.ToLowerInvariant</c> to avoid runtime gaps in Mono/IL2CPP.
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Converts text to lowercase.")]
    public class LowercaseModifier : BaseModifier
    {
        protected override void OnApply(in RangeApplyContext context)
        {
            var range = context.Segment.Range;
            var codepoints = buffers.codepoints.Span.Slice(range.start, range.length);

            for (var i = 0; i < codepoints.Length; i++)
                codepoints[i] = UnicodeData.GetSimpleLowercase(codepoints[i]);
        }
    }
}
