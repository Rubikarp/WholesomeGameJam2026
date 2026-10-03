using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Animates a label between its resting position (inside the field) and a floated position (typically the
    /// top edge), scaling it down on the way; the label stays visible throughout. For a different motion model
    /// — a tween library, a path — write your own <see cref="FieldDecorator"/>.
    /// </summary>
    [Serializable]
    [TypeGroup("Placeholder", 1)]
    [TypeDescription("Animate a label from inside the field to a floated position on focus or content")]
    public sealed partial class FloatingLabelDecorator : FieldDecorator
    {
        /// <summary>Label whose authored transform is the resting pose.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("The label that floats. Its authored position and scale are the resting state.")]
        private UniTextBase label;

        /// <summary>Transform whose position defines the floated pose.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Where the label moves to when floated — usually a marker RectTransform at the field's top edge.")]
        private RectTransform floatedTo;

        /// <summary>Label scale in the floated pose relative to its resting scale.</summary>
        [SerializeField, Range(0.1f, 1f), NumberStateProperty(nameof(NotifyChanged), Min = 0.1, Max = 1)]
        [Tooltip("Label scale when floated, relative to its resting scale.")]
        private float floatedScale = 0.8f;

        /// <summary>Transition duration in seconds; zero applies the target pose immediately.</summary>
        [SerializeField, Min(0f), NumberStateProperty(nameof(NotifyChanged), Min = 0)]
        [Tooltip("Seconds for the float transition. 0 = instant.")]
        private float duration = 0.12f;

        /// <summary>Curve mapping normalized transition progress to pose interpolation.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [NonSerialized] private Vector2 restingPos;
        [NonSerialized] private Vector3 restingScale;
        [NonSerialized] private float progress;
        [NonSerialized] private float goal;
        [NonSerialized] private bool posed;

        protected override void OnAttach()
        {
            if (label == null) return;
            restingPos = label.rectTransform.anchoredPosition;
            restingScale = label.rectTransform.localScale;
            posed = false;
        }

        protected override void OnDetach()
        {
            if (label == null) return;
            label.rectTransform.anchoredPosition = restingPos;
            label.rectTransform.localScale = restingScale;
        }

        protected override void OnFieldState(in FieldState state)
        {
            goal = state.IsFocused || !state.IsEmpty ? 1f : 0f;
            if (!posed)
            {
                progress = goal;
                posed = true;
                ApplyPose();
                return;
            }
            if (!Mathf.Approximately(progress, goal)) RequestTick();
        }

        protected override void OnTick(float deltaTime)
        {
            if (label == null || Mathf.Approximately(progress, goal))
            {
                StopTick();
                return;
            }
            progress = duration <= 0f || Accessibility.PrefersReducedMotion
                ? goal
                : Mathf.MoveTowards(progress, goal, deltaTime / duration);
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (label == null || floatedTo == null) return;
            float k = easing.Evaluate(progress);
            label.rectTransform.anchoredPosition = Vector2.LerpUnclamped(restingPos, floatedTo.anchoredPosition, k);
            label.rectTransform.localScale = Vector3.LerpUnclamped(restingScale, restingScale * floatedScale, k);
        }
    }
}
