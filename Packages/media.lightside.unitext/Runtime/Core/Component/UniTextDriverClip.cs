using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// One clip of a <see cref="UniTextDriver"/> sequence: a window on the driver's timeline that
    /// owns one parameter across the ranges its query matches, ramping every member between two
    /// endpoint values.
    /// </summary>
    /// <remarks>
    /// Outside its window a clip holds its boundary value — <see cref="From"/> before the start,
    /// <see cref="To"/> after the end. A Replace clip holds only until the playhead passes the
    /// start of another Replace clip of the same parameter and priority — the latest started clip
    /// drives; Add, Multiply and Custom clips compose alongside freely. Releasing the driver
    /// returns the parameters to their cascades. With <see cref="Stagger"/> above zero the members
    /// ramp one after another in text order, and the clip lasts
    /// <c>memberDuration + stagger × (members − 1)</c>.
    /// </remarks>
    [Serializable]
    [StateSource]
    public sealed partial class UniTextDriverClip
    {
        [SerializeField, Min(0f), StateProperty, Tooltip("Second on the driver's timeline this clip starts at.")]
        private float start;

        [SerializeField, HideInInspector, StateProperty] private ModifierNodeId targetNode;
        [SerializeField, HideInInspector, StateProperty] private string parameterId;

        [SerializeField, StateProperty, Tooltip("Ranges this clip drives; filters follow text edits.")]
        private RangeQueryDefinition query = new();

        [SerializeReference, StateProperty] private RuleValue from;
        [SerializeReference, StateProperty] private RuleValue to;

        [SerializeField, StateProperty, Tooltip("How the driven values combine with each range's cascade result.")]
        private ParameterComposition composition = ParameterComposition.Replace;

        [SerializeField, StateProperty, Tooltip("Contribution priority against other owners of the same parameter.")]
        private int priority;

        [SerializeField, Min(0f), StateProperty, Tooltip("Seconds each member's ramp lasts.")]
        private float memberDuration = 0.5f;

        [SerializeField, Min(0f), StateProperty, Tooltip("Seconds between the starts of adjacent members' ramps.")]
        private float stagger;

        [SerializeField, StateProperty] private Ease easing = Ease.Of(EasingType.Linear);

        [SerializeField, HideInInspector, StateProperty] private bool disabled;

        [SerializeField, HideInInspector, Min(0), StatePassive] private int track;

        /// <summary>Timeline lane the clip is arranged on; purely organizational.</summary>
        internal int TimelineTrack => track;

        /// <summary>Seconds from the clip's start to its last member's completion.</summary>
        internal float Length(int members)
            => members <= 0 ? memberDuration : memberDuration + stagger * (members - 1);

        /// <summary>Binds the clip to a modifier parameter; the editor picker routes through this.</summary>
        public void SetTarget(ModifierNodeId node, string parameter)
        {
            TargetNode = node;
            ParameterId = parameter;
        }
    }
}
