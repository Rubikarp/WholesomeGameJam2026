using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Serialized source for a small authored set of codepoint ranges. Runtime-generated or
    /// frequently changing sets belong in <see cref="MutableRangeSource"/>.
    /// </summary>
    [Serializable]
    [TypeGroup("Ranges", 1)]
    [TypeDescription("Applies the modifier to authored codepoint ranges without parsing markup.")]
    public sealed partial class FixedRangeSource : RangeSource
    {
        /// <summary>One authored range expression and its positional modifier parameters.</summary>
        [Serializable]
        public struct Entry
        {
            [SerializeField] private string range;
            [SerializeField, DefaultParameter] private string parameter;

            /// <summary>C#-style codepoint range expression such as <c>0..5</c> or <c>^10..</c>.</summary>
            public string Range
            {
                get => range;
                set => range = value;
            }

            /// <summary>Explicit positional parameter layer forwarded to the modifier graph.</summary>
            public string Parameter
            {
                get => parameter;
                set => parameter = value;
            }

            /// <summary>Creates an authored range entry.</summary>
            public Entry(string range, string parameter = null)
            {
                this.range = Normalize(range);
                this.parameter = parameter;
                RequireValid(this.range);
            }
        }

        /// <summary>Authored fixed range entries.</summary>
        [FormerlySerializedAs("data")]
        [StateList(nameof(ApplyRangesChange), Name = nameof(Entries), Validator = nameof(ValidateRangesMutation))]
        [SerializeField] private List<Entry> ranges = new();

        /// <summary>Creates an empty authored source.</summary>
        public FixedRangeSource() { }

        /// <summary>Creates one fixed codepoint range with an exclusive end.</summary>
        public FixedRangeSource(int start, int end, string parameter = null)
            => ranges.Add(new Entry($"{start}..{end}", parameter));

        /// <summary>Uses instance identity because two authored lists are independent sources.</summary>
        public override string Identity => null;

        private void ValidateRangesMutation(in StateListMutation<Entry> mutation)
        {
            for (var i = 0; i < mutation.Count; i++)
                RequireValid(Normalize(mutation[i].Range));
        }

        private void ApplyRangesChange()
            => NotifyChanged();

        /// <summary>Whether this source contains one entry covering the entire text.</summary>
        public bool IsWholeText
        {
            get
            {
                if (ranges.Count == 1)
                {
                    var expression = Normalize(ranges[0].Range);
                    return RangeEx.IsAll(expression);
                }
                return false;
            }
        }

        protected override void CollectRanges(in TextSnapshot currentSnapshot, RangeMatchWriter writer)
        {
            var len = currentSnapshot.CodepointCount;
            if (len == 0) return;

            for (var i = 0; i < ranges.Count; i++)
            {
                var entry = ranges[i];
                var expression = Normalize(entry.Range);
                if (!RangeEx.TryParse(expression, out var currentRange))
                    throw new FormatException(
                        $"FixedRangeSource entry {i} has invalid range expression '{entry.Range}'.");

                var start = Math.Clamp(currentRange.Start.GetOffset(len), 0, len);
                var end = Math.Clamp(currentRange.End.GetOffset(len), 0, len);
                if (start >= end) continue;

                writer.Add(new TextRange(start, end - start), entry.Parameter);
            }
        }

        private static string Normalize(string range)
            => string.IsNullOrWhiteSpace(range) ? RangeEx.All : range;

        private static void RequireValid(string range)
        {
            if (!RangeEx.TryParse(range, out _))
                throw new FormatException($"Invalid codepoint range expression '{range}'.");
        }
    }
}
