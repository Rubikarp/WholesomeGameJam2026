using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Caps the field length and publishes the cap for a counter to show. Excess input is truncated on a
    /// boundary (never splitting a surrogate pair, multi-byte codepoint, or grapheme cluster); input with no
    /// room left is rejected; deletions always pass. <see cref="Unit"/> chooses what a unit of length means —
    /// by default <see cref="TextLengthUnit.Graphemes"/>, so a family emoji counts as one.
    /// </summary>
    [Serializable]
    [TypeDescription("Cap the field length (grapheme clusters by default) and publish the cap for a counter")]
    [TypeGroup("Filtering", 0)]
    public sealed partial class LengthLimitBehavior : InputBehavior
    {
        /// <summary>Maximum length in <see cref="Unit"/>; zero disables the cap.</summary>
        [SerializeField, StateProperty(nameof(ApplyLimitChange))]
        [Tooltip("Maximum length. 0 = unlimited.")]
        private int limit;

        /// <summary>Unit used to measure <see cref="Limit"/>.</summary>
        [SerializeField, StateProperty(nameof(ApplyUnitChange))]
        [Tooltip("What a unit of length means. Graphemes = user-perceived characters (recommended).")]
        private TextLengthUnit unit = TextLengthUnit.Graphemes;

        [NonSerialized] private GraphemeCountCache graphemeCache;

        protected override void OnEnable()
        {
            editable.InputFilter.Subscribe(Filter);
            editable.EditApplied += OnEditApplied;
            Publish();
        }

        protected override void OnDisable()
        {
            editable.InputFilter.Unsubscribe(Filter);
            editable.EditApplied -= OnEditApplied;
            editable.LengthLimit = default;
            graphemeCache.Invalidate();
        }

        private void Publish()
        {
            if (IsEnabled) editable.LengthLimit = new LengthLimit(limit, unit);
        }

        private void ApplyLimitChange(int previous, ref int current)
        {
            current = Mathf.Max(0, current);
            if (previous == current) return;
            Publish();
            NotifyChanged(Members.Limit);
        }

        private void ApplyUnitChange()
        {
            Publish();
            NotifyChanged(Members.Unit);
        }

        private void OnEditApplied(EditShape shape) => graphemeCache.CommitEdit(editable, in shape);

        private void Filter(ref InputEdit edit)
        {
            if (edit.Rejected) return;
            if (limit <= 0 || string.IsNullOrEmpty(edit.text)) return;
            var replaced = new TextRange(edit.insertCodepointIndex, edit.replacedCodepoints);

            if (unit == TextLengthUnit.Graphemes)
            {
                if (graphemeCache.PredictCount(edit.document, replaced, edit.text.AsSpan()) <= limit) return;

                int available = limit
                              - graphemeCache.Count(edit.document)
                              + TextMeasure.CountRange(edit.document, replaced.start, replaced.length, unit);
                if (available <= 0) { edit.Rejected = true; return; }
                int keep = TextMeasure.TruncatedCharLength(edit.text.AsSpan(), available, unit);
                if (keep <= 0) { edit.Rejected = true; return; }
                edit.text = edit.text.Substring(0, keep);
                return;
            }

            int room = limit
                     - TextMeasure.Count(edit.document, unit)
                     + TextMeasure.CountRange(edit.document, replaced.start, replaced.length, unit);
            if (room <= 0) { edit.Rejected = true; return; }

            if (TextMeasure.Count(edit.text.AsSpan(), unit) > room)
            {
                int keep = TextMeasure.TruncatedCharLength(edit.text.AsSpan(), room, unit);
                if (keep <= 0) { edit.Rejected = true; return; }
                edit.text = edit.text.Substring(0, keep);
            }
        }
    }
}
