using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shows the current length — <c>count</c>, or <c>count/limit</c> when a <see cref="LengthLimitBehavior"/>
    /// publishes a cap — counting in the cap's <see cref="TextLengthUnit"/> (grapheme clusters when there is no cap),
    /// and recolors once the count reaches the limit. Counts the document SOURCE — the same space
    /// <see cref="LengthLimitBehavior"/> enforces in, so count and cap always agree; in a field with
    /// hidden markup the count therefore includes the markup characters, not just the visible text.
    /// </summary>
    [Serializable]
    [TypeGroup("Support", 1)]
    [TypeDescription("Live length count, optionally against the limit")]
    public sealed partial class CharacterCounterDecorator : FieldDecorator
    {
        /// <summary>Text component that displays the current count and optional limit.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))] private UniTextBase target;

        /// <summary>Colour applied when the count reaches the active limit.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Color when the count reaches the limit.")]
        private Color overflowColor = Color.red;

        [NonSerialized] private Color restingColor;
        [NonSerialized] private int cachedVersion = -1;
        [NonSerialized] private TextLengthUnit cachedUnit;
        [NonSerialized] private int cachedCount;
        [NonSerialized] private int appliedCount = -1;
        [NonSerialized] private int appliedMax = -1;

        protected override void OnAttach()
        {
            if (target != null) restingColor = target.color;
            cachedVersion = -1;
            appliedCount = -1;
            appliedMax = -1;
        }

        protected override void OnDetach()
        {
            if (target != null) target.color = restingColor;
        }

        protected override void OnFieldState(in FieldState state)
        {
            if (target == null) return;

            var limit = state.LengthLimit;
            var unit = limit.Max > 0 ? limit.Unit : TextLengthUnit.Graphemes;
            int current = CurrentCount(editable, unit);

            if (current == appliedCount && limit.Max == appliedMax) return;
            appliedCount = current;
            appliedMax = limit.Max;

            if (limit.Max > 0)
            {
                target.Text = $"{current}/{limit.Max}";
                target.color = current >= limit.Max ? overflowColor : restingColor;
            }
            else
            {
                target.Text = current.ToString();
                target.color = restingColor;
            }
        }

        private int CurrentCount(UniTextEditable editor, TextLengthUnit unit)
        {
            if (editor.Version == cachedVersion && unit == cachedUnit) return cachedCount;
            cachedVersion = editor.Version;
            cachedUnit = unit;
            cachedCount = TextMeasure.Count(editor, unit);
            return cachedCount;
        }
    }
}
