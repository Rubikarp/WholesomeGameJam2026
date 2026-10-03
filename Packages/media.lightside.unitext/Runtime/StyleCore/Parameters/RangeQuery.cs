using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Filters one prospective query result.</summary>
    public delegate bool ModifierRangePredicate(in ModifierRange range);

    /// <summary>
    /// Declarative selection of a modifier's materialized ranges. Immutable; every filter returns
    /// a new query, filters combine with AND. Materialize with <see cref="Collect"/>.
    /// </summary>
    public struct RangeQuery
    {
        private enum AreaFilter : byte
        {
            None,
            Intersecting,
            Within,
        }

        private BaseModifier target;
        private ParameterDescriptor parameter;
        private object parameterValue;
        private string parameterToken;
        private bool bareOnly;
        private string primaryValue;
        private bool primarySet;
        private RangeChannel channel;
        private bool channelSet;
        private AreaFilter areaFilter;
        private TextRange area;
        private ModifierRangePredicate predicate;
        private int skip;
        private int take;
        private string labelFilter;
        private bool labelSet;

        /// <summary>The modifier whose ranges the query selects.</summary>
        public readonly BaseModifier Target => target;

        /// <summary>Selects every materialized range of <paramref name="modifier"/>.</summary>
        public static RangeQuery For(BaseModifier modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            var query = default(RangeQuery);
            query.target = modifier;
            return query;
        }

        /// <summary>
        /// Keeps ranges whose markup token for <paramref name="parameter"/> parses to a value
        /// equal to <paramref name="value"/>. Ranges without the token never match.
        /// </summary>
        public readonly RangeQuery WhereParameter<TModifier, TValue>(
            ParameterDescriptor<TModifier, TValue> parameter, TValue value)
            where TModifier : BaseModifier
        {
            RequireNoParameterFilter();
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            var query = this;
            query.parameter = parameter;
            query.parameterValue = value;
            return query;
        }

        /// <summary>Keeps ranges whose raw markup token for <paramref name="parameter"/> equals <paramref name="token"/> (ordinal).</summary>
        public readonly RangeQuery WhereToken(ParameterDescriptor parameter, string token)
        {
            RequireNoParameterFilter();
            if (parameter == null) throw new ArgumentNullException(nameof(parameter));
            var query = this;
            query.parameter = parameter;
            query.parameterToken = token ?? string.Empty;
            return query;
        }

        /// <summary>Keeps only ranges authored without any explicit parameter token (bare tags).</summary>
        public readonly RangeQuery WhereBare()
        {
            var query = this;
            query.bareOnly = true;
            return query;
        }

        /// <summary>Keeps ranges whose source-emitted semantic value equals <paramref name="value"/> (ordinal).</summary>
        public readonly RangeQuery WherePrimaryValue(string value)
        {
            var query = this;
            query.primaryValue = value;
            query.primarySet = true;
            return query;
        }

        /// <summary>Keeps ranges routed through <paramref name="value"/>.</summary>
        public readonly RangeQuery WhereChannel(RangeChannel value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var query = this;
            query.channel = value;
            query.channelSet = true;
            return query;
        }

        /// <summary>Keeps ranges overlapping <paramref name="value"/> by at least one codepoint.</summary>
        public readonly RangeQuery Intersecting(TextRange value)
        {
            var query = this;
            query.areaFilter = AreaFilter.Intersecting;
            query.area = value;
            return query;
        }

        /// <summary>Keeps ranges lying entirely inside <paramref name="value"/>.</summary>
        public readonly RangeQuery Within(TextRange value)
        {
            var query = this;
            query.areaFilter = AreaFilter.Within;
            query.area = value;
            return query;
        }

        /// <summary>Keeps ranges whose tag carries the <c>#label</c> anchor equal to <paramref name="value"/> (ordinal).</summary>
        public readonly RangeQuery WhereLabel(string value)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("Label is empty.", nameof(value));
            var query = this;
            query.labelFilter = value;
            query.labelSet = true;
            return query;
        }

        /// <summary>Skips the first <paramref name="count"/> ranges that pass every other filter.</summary>
        public readonly RangeQuery Skip(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var query = this;
            query.skip = count;
            return query;
        }

        /// <summary>Keeps at most <paramref name="count"/> ranges after <see cref="Skip"/>; zero keeps all.</summary>
        public readonly RangeQuery Take(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            var query = this;
            query.take = count;
            return query;
        }

        /// <summary>Keeps ranges accepted by <paramref name="filter"/>; multiple calls combine with AND.</summary>
        public readonly RangeQuery Where(ModifierRangePredicate filter)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            var combined = predicate;
            if (combined != null)
            {
                var first = combined;
                combined = (in ModifierRange range) => first(in range) && filter(in range);
            }
            else
            {
                combined = filter;
            }
            var query = this;
            query.predicate = combined;
            return query;
        }

        /// <summary>
        /// Collects the matching ranges of the current parse into <paramref name="results"/>, in
        /// text order.
        /// </summary>
        /// <exception cref="InvalidOperationException">The query has no target, or its modifier is
        /// not attached to a text component.</exception>
        public readonly void Collect(List<ModifierRange> results)
        {
            if (target == null)
                throw new InvalidOperationException(
                    "The query has no target. Build it with RangeQuery.For.");
            target.GetRanges(results);
            var kept = 0;
            var matched = 0;
            for (var i = 0; i < results.Count; i++)
            {
                var range = results[i];
                if (!Matches(in range)) continue;
                if (++matched <= skip) continue;
                if (take > 0 && kept >= take) break;
                results[kept++] = range;
            }
            results.RemoveRange(kept, results.Count - kept);
        }

        /// <summary>
        /// Takes standing ownership of one parameter across every range this query matches, now
        /// and after every future parse; release the set to return them all to their cascades.
        /// </summary>
        /// <exception cref="InvalidOperationException">The query has no target, or its modifier is
        /// not attached to a text component.</exception>
        public readonly OwnedParameterSet<TModifier, TValue> Own<TModifier, TValue>(
            ParameterDescriptor<TModifier, TValue> parameter,
            ParameterComposition composition = ParameterComposition.Replace, int priority = 0)
            where TModifier : BaseModifier
        {
            if (target == null)
                throw new InvalidOperationException(
                    "The query has no target. Build it with RangeQuery.For.");
            var owner = target.Owner ?? throw new InvalidOperationException(
                $"{target.GetType().Name} is not attached to a text component.");
            return UniTextRanges.For(owner).Own(in this, parameter, composition, priority);
        }

        /// <summary>Returns whether one materialized range passes every configured filter.</summary>
        public readonly bool Matches(in ModifierRange range)
        {
            if (channelSet && !ReferenceEquals(range.Channel, channel)) return false;

            if (areaFilter == AreaFilter.Intersecting)
            {
                if (range.Range.End <= area.start || range.Range.start >= area.End) return false;
            }
            else if (areaFilter == AreaFilter.Within)
            {
                if (range.Range.start < area.start || range.Range.End > area.End) return false;
            }

            if (bareOnly && range.Parameters.HasExplicitTokens) return false;
            if (labelSet && !string.Equals(range.Parameters.Label, labelFilter,
                    StringComparison.Ordinal)) return false;
            if (parameterToken != null &&
                !parameter.RawTokenMatches(range.Modifier, range.Parameters, parameterToken))
                return false;
            if (parameterValue != null &&
                !parameter.TokenMatches(range.Modifier, range.Parameters, parameterValue))
                return false;

            if (primarySet && !string.Equals(range.PrimaryValue, primaryValue,
                    StringComparison.Ordinal)) return false;

            return predicate == null || predicate(in range);
        }

        private readonly void RequireNoParameterFilter()
        {
            if (parameter != null)
                throw new InvalidOperationException("The query already has a parameter filter.");
        }
    }
}
