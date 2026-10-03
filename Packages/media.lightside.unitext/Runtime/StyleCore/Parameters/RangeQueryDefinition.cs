using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Area constraint of a serialized range query.</summary>
    public enum RangeQueryArea : byte
    {
        /// <summary>No positional constraint.</summary>
        All,
        /// <summary>Only ranges overlapping the codepoint area.</summary>
        Intersecting,
        /// <summary>Only ranges lying entirely inside the codepoint area.</summary>
        Within,
    }

    /// <summary>
    /// Serialized form of a <see cref="RangeQuery"/>: inspector-editable filters built into the
    /// live query against a target modifier on every bind, so the selection follows text edits.
    /// </summary>
    [Serializable]
    [StateSource]
    public sealed partial class RangeQueryDefinition
    {
        [SerializeField, StateProperty, Tooltip("Only ranges whose tag carries this #label anchor (e.g. <wave #hero=2>); empty selects all.")]
        private string label;

        [SerializeField, StateProperty, Tooltip("Only ranges whose source-emitted value (a link URL, a mention id) equals this; empty selects all.")]
        private string primaryValue;

        [SerializeField, StateProperty, Tooltip("Only ranges whose raw markup token for the driven parameter equals this; empty selects all.")]
        private string filterToken;

        [SerializeField, StateProperty, Tooltip("Only ranges written without explicit markup tokens.")]
        private bool bareOnly;

        [SerializeField, StateProperty, Tooltip("Only ranges routed to this channel asset; none selects all.")]
        private RangeChannel channel;

        [SerializeField, StateProperty, Tooltip("Positional constraint against the codepoint area below.")]
        private RangeQueryArea area = RangeQueryArea.All;

        [SerializeField, Min(0), StateProperty, Tooltip("Start of the codepoint area.")]
        private int areaStart;

        [SerializeField, Min(0), StateProperty, Tooltip("Length of the codepoint area.")]
        private int areaLength;

        [SerializeField, Min(0), StateProperty, Tooltip("Skips this many matched ranges, in text order — 1 starts at the second tag.")]
        private int skip;

        [SerializeField, Min(0), StateProperty, Tooltip("Keeps at most this many matched ranges after Skip; 0 keeps all — Skip 2 with Take 1 selects exactly the third tag.")]
        private int take;

        /// <summary>
        /// Builds the live query for one target modifier. <paramref name="parameter"/> scopes the
        /// raw-token filter; a null parameter leaves that filter off.
        /// </summary>
        public RangeQuery Build(BaseModifier target, ParameterDescriptor parameter)
        {
            var query = RangeQuery.For(target);
            if (!string.IsNullOrEmpty(label)) query = query.WhereLabel(label);
            if (!string.IsNullOrEmpty(primaryValue)) query = query.WherePrimaryValue(primaryValue);
            if (!string.IsNullOrEmpty(filterToken) && parameter != null)
                query = query.WhereToken(parameter, filterToken);
            if (bareOnly) query = query.WhereBare();
            if (channel != null) query = query.WhereChannel(channel);
            if (area == RangeQueryArea.Intersecting)
                query = query.Intersecting(new TextRange(areaStart, areaLength));
            else if (area == RangeQueryArea.Within)
                query = query.Within(new TextRange(areaStart, areaLength));
            if (skip > 0) query = query.Skip(skip);
            if (take > 0) query = query.Take(take);
            return query;
        }
    }
}
