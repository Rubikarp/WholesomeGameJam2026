using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Parses markup and coordinates modifiers to apply formatting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The central parsing engine that processes text through registered <see cref="ParseRule"/> instances,
    /// extracts markup tags, and maps them to <see cref="BaseModifier"/> instances for rendering.
    /// </para>
    /// <para>
    /// Parsing flow:
    /// 1. <see cref="Register"/> pairs rules with modifiers
    /// 2. <see cref="Parse"/> scans text, matches rules, removes/replaces tags
    /// 3. <see cref="Apply"/> invokes modifiers with cleaned text ranges
    /// </para>
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    /// <seealso cref="BaseModifier"/>
    internal sealed class AttributeParser : IModifierChangeSink
    {
        private readonly struct Registration
        {
            public readonly RangeSource source;
            public readonly ParseRule rule;
            public readonly bool wholeText;
            public readonly BaseModifier modifier;
            public readonly Style ownerStyle;

            public Registration(RangeSource source, bool wholeText, BaseModifier modifier, Style ownerStyle)
            {
                this.source = source;
                rule = source as ParseRule;
                this.wholeText = wholeText;
                this.modifier = modifier;
                this.ownerStyle = ownerStyle;
            }
        }

        private readonly struct ParserRangeKey : IEquatable<ParserRangeKey>
        {
            private readonly RangeSourceId source;
            private readonly int start;
            private readonly int end;
            private readonly string matchKey;
            private readonly int ordinal;

            public ParserRangeKey(RangeSource source, int start, int end, string matchKey, int ordinal)
            {
                this.source = source.RuntimeId;
                this.start = matchKey == null ? start : -1;
                this.end = matchKey == null ? end : -1;
                this.matchKey = matchKey;
                this.ordinal = ordinal;
            }

            public bool Equals(ParserRangeKey other)
                => source == other.source && start == other.start && end == other.end &&
                   ordinal == other.ordinal && string.Equals(matchKey, other.matchKey, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is ParserRangeKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(source, start, end, matchKey, ordinal);

            public bool TryMap(in TextEditMap edit, out ParserRangeKey mapped)
            {
                if (matchKey != null)
                {
                    mapped = this;
                    return true;
                }

                if (edit.TryMapUnchangedRange(start, end, out var mappedStart, out var mappedEnd))
                {
                    mapped = new ParserRangeKey(source, mappedStart, mappedEnd, null, ordinal);
                    return true;
                }

                mapped = default;
                return false;
            }

            private ParserRangeKey(RangeSourceId source, int start, int end, string matchKey, int ordinal)
            {
                this.source = source;
                this.start = start;
                this.end = end;
                this.matchKey = matchKey;
                this.ordinal = ordinal;
            }
        }

        private readonly struct PreviousRange
        {
            public readonly RangeId rangeId;
            public readonly RangeSegmentId segmentId;

            public PreviousRange(RangeId rangeId, RangeSegmentId segmentId)
            {
                this.rangeId = rangeId;
                this.segmentId = segmentId;
            }
        }

        private readonly struct DerivedRegistration
        {
            public readonly ParseRule rule;
            public readonly BaseModifier modifier;
            public readonly int order;

            public DerivedRegistration(ParseRule rule, BaseModifier modifier, int order)
            {
                this.rule = rule;
                this.modifier = modifier;
                this.order = order;
            }
        }

        private readonly List<Registration> registrations = new();
        private int managedStyleCount;
        private readonly UniTextBase owner;
        private readonly List<DerivedRegistration> derivedRegistrations = new();
        private readonly bool[] derivedTriggerTable = new bool[128];
        private bool derivedScanEnabled;
        private bool hasDerivedMatchers;

        internal readonly PooledList<AttributeSpan> spans = new();
        private readonly PooledList<AttributeSpan> spanClipScratch = new();
        private readonly List<(int start, int end)> protectedClipScratch = new();
        private readonly List<(int start, int end)> unionScratch = new();
        private readonly List<BaseModifier> pendingReapply = new(4);
        private readonly List<BaseModifier> reapplyScratch = new();
        private readonly List<BaseModifier> unionRejectedScratch = new();
        private readonly List<object> componentModifiers = new();
        private readonly List<int> componentModifierNodes = new();
        private int reapplyComponentsGeneration = -1;
        private PooledBuffer<int> componentKeys;
        private PooledBuffer<int> componentOrder;
        private PooledBuffer<int> componentParent;

        private readonly List<ParseRule> allRules = new();
        private readonly Dictionary<ParseRule, BaseModifier> ruleToModifier = new();
        private bool rulesDirty;

        private readonly PooledList<ParsedRange> tempRanges = new(32);
        private List<RangeSourceMatch> sourceMatches;
        private HashSet<RangeId> sourceRangeIds;
        private HashSet<RangeSource> snapshotSources;
        private Dictionary<ParserRangeKey, PreviousRange> previousRanges = new();
        private Dictionary<ParserRangeKey, PreviousRange> currentRanges = new();
        private Dictionary<ParserRangeKey, int> duplicateRangeOrdinals;
        private HashSet<ParserRangeKey> ambiguousRanges;
        private Dictionary<ParserRangeKey, bool> matchKeyDuplicates;

        private readonly PooledList<(int start, int end)> tagRemovals = new();
        private readonly PooledList<(int start, int end, string insert)> tagInsertions = new();

        private static readonly RemovalComparerImpl removalComparer = new();
        private static readonly InsertionComparerImpl insertionComparer = new();
        private static readonly RulePriorityComparerImpl rulePriorityComparer = new();
        private static readonly DerivedPriorityComparerImpl derivedPriorityComparer = new();
        private static readonly ProjectedRangeComparerImpl projectedRangeComparer = new();

        private sealed class RulePriorityComparerImpl : IComparer<ParseRule>
        {
            public int Compare(ParseRule a, ParseRule b)
            {
                return b.Priority.CompareTo(a.Priority);
            }
        }

        private sealed class RemovalComparerImpl : IComparer<(int start, int end)>
        {
            public int Compare((int start, int end) a, (int start, int end) b)
            {
                return a.start.CompareTo(b.start);
            }
        }

        private sealed class InsertionComparerImpl : IComparer<(int start, int end, string insert)>
        {
            public int Compare((int start, int end, string insert) a, (int start, int end, string insert) b)
            {
                return a.start.CompareTo(b.start);
            }
        }

        private sealed class ProjectedRangeComparerImpl : IComparer<ProjectedRange>
        {
            public int Compare(ProjectedRange a, ProjectedRange b)
            {
                return a.start.CompareTo(b.start);
            }
        }

        private PooledBuffer<int> indexMapBuffer;
        private PooledBuffer<char> cleanTextBuffer;
        private PooledBuffer<int> charToCpMap;
        private PooledBuffer<int> codepointToCharMap;
        private PooledBuffer<char> revisionTextBuffer;
        private bool charToCpMapDirty = true;
        private bool hasExternalSources;
        private bool snapshotTrackingEnabled;

        private TextSnapshot textSnapshot;
        private string snapshotText;
        private TextRevision textRevision;
        private ulong nextTextRevision;

        private int applyCycle;

        public AttributeParser(UniTextBase owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
            => owner.MarkModifierDirty(modifier, flags);

        /// <summary>
        /// Monotonic apply-cycle stamp, bumped once per <see cref="Apply"/> and once per
        /// <see cref="ReApply"/>. <see cref="BaseModifier.Apply"/> compares it against the
        /// modifier's last-applied stamp to lazily clear per-apply accumulators on the first
        /// range of each cycle. Starts at 0 and only ever bumps before dispatch, so 0 means
        /// "never applied".
        /// </summary>
        internal int ApplyCycle => applyCycle;

        /// <summary>
        /// Monotonic counter of span-list rebuilds. Unchanged by granular re-apply, so consumers
        /// caching views derived from the span list compare it to skip recollection.
        /// </summary>
        internal int ParseGeneration => parseGeneration;

        private int parseGeneration;

        /// <summary>
        /// Emits every attributed range whose modifier graph contains <paramref name="target"/>,
        /// in codepoint space and text order. Spans without attribution are skipped.
        /// </summary>
        internal void CollectRangesFor(BaseModifier target, List<ModifierRange> results)
        {
            if (spans.Count == 0 || target == null) return;
            BuildCharToCodepointMap();
            var map = charToCpMap.data;
            var mapCount = charToCpMap.count;
            for (var i = 0; i < spans.Count; i++)
            {
                ref readonly var span = ref spans[i];
                if (span.modifier == null || !span.HasAttribution) continue;
                if (span.start < 0 || span.end >= mapCount) continue;
                if (!ContainsModifier(span.modifier, target)) continue;
                var attribution = span.Attribution;
                var start = map[span.start];
                var end = map[span.end];
                results.Add(new ModifierRange(target, attribution.identity, span.segmentId,
                    new TextRange(start, end - start), span.parameters.PrimaryValue, span.channel,
                    in span.parameters));
            }
        }

        /// <summary>
        /// Codepoint span of the first range carrying <paramref name="label"/>, in text order,
        /// whatever modifier authored it.
        /// </summary>
        internal bool TryGetLabeledSpan(string label, out TextRange span)
        {
            span = default;
            if (spans.Count == 0 || string.IsNullOrEmpty(label)) return false;

            BuildCharToCodepointMap();
            var map = charToCpMap.data;
            var mapCount = charToCpMap.count;
            for (var i = 0; i < spans.Count; i++)
            {
                ref readonly var entry = ref spans[i];
                if (entry.start < 0 || entry.end >= mapCount) continue;
                if (!string.Equals(entry.parameters.Label, label, StringComparison.Ordinal)) continue;

                var start = map[entry.start];
                span = new TextRange(start, map[entry.end] - start);
                return true;
            }

            return false;
        }

        internal int GetModifierRegistrationOrder(BaseModifier target)
        {
            if (target == null) return -1;
            for (var i = 0; i < registrations.Count; i++)
                if (ContainsModifier(registrations[i].modifier, target)) return i;
            return -1;
        }

        internal BaseModifier ResolveModifierNode(BaseModifier anchor, ModifierNodeId nodeId)
        {
            if (anchor == null) throw new ArgumentNullException(nameof(anchor));
            if (!nodeId.IsValid)
                throw new ArgumentException("Modifier node identity is invalid.", nameof(nodeId));
            for (var i = 0; i < registrations.Count; i++)
            {
                var root = registrations[i].modifier;
                if (!ContainsModifier(root, anchor)) continue;
                return BaseModifier.FindGraphNode(root, nodeId) ??
                       throw new InvalidOperationException(
                           $"Modifier node '{nodeId}' was not found in the graph.");
            }
            throw new InvalidOperationException(
                $"{anchor.GetType().Name} is not attached to a registered modifier graph.");
        }

        private static bool ContainsModifier(BaseModifier root, BaseModifier target)
        {
            if (ReferenceEquals(root, target)) return true;
            if (root?.Children is not { } children) return false;
            for (var i = 0; i < children.Count; i++)
                if (ContainsModifier(children[i], target)) return true;
            return false;
        }

        /// <summary>Gets the parsed text with all markup tags removed (as Span, zero-allocation).</summary>
        public ReadOnlySpan<char> CleanTextSpan => cleanTextBuffer.Span;

        internal TextSnapshot TextSnapshot
        {
            get
            {
                if (!textRevision.IsValid) return default;
                snapshotTrackingEnabled = true;
                EnsureTextSnapshot();
                snapshotSources ??= new HashSet<RangeSource>();
                snapshotSources.Clear();
                for (var i = 0; i < registrations.Count; i++)
                    SetSourceSnapshot(registrations[i].source, snapshotSources);
                snapshotSources.Clear();
                return textSnapshot;
            }
        }

        private string cachedCleanTextString;

        /// <summary>Gets the parsed text with all markup tags removed (allocates string on first access).</summary>
        public string CleanText
        {
            get
            {
                if (cachedCleanTextString == null && cleanTextBuffer.count > 0)
                    cachedCleanTextString = new string(cleanTextBuffer.data, 0, cleanTextBuffer.count);
                return cachedCleanTextString ?? string.Empty;
            }
        }


        /// <summary>Appends one independent runtime style outside the component's managed style prefix.</summary>
        public void Register(Style style)
        {
            if (style == null) return;
            for (var i = 0; i < registrations.Count; i++)
            {
                var registered = registrations[i];
                if (!ReferenceEquals(registered.ownerStyle, style)) continue;
                if (RegistrationMatches(registered, style)) return;
                throw new InvalidOperationException(
                    "A registered style cannot change its source or modifier before it is released.");
            }

            registrations.Add(Attach(style));
            RebuildRegistrationIndexes();
        }

        private Registration Attach(Style style)
        {
            var source = style.EffectiveSource;
            var modifier = style.Modifier;
            modifier?.SetOwner(owner, style);
            try
            {
                source.Bind(this, OnSourceChanged, GetTextSnapshotForSource, in textSnapshot);
            }
            catch
            {
                try
                {
                    source.Unbind(this);
                }
                finally
                {
                    modifier?.SetOwner(null);
                }
                throw;
            }
            return new Registration(source, style.Source == null, modifier, style);
        }

        /// <summary>Reconciles the managed registration prefix without disturbing independent runtime styles.</summary>
        internal bool ReconcileStyles(IReadOnlyList<Style> current)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));

            var changed = false;
            var targetIndex = 0;
            try
            {
                for (var i = managedStyleCount - 1; i >= 0; i--)
                {
                    var registration = registrations[i];
                    if (IsCurrent(registration, current)) continue;
                    changed = true;
                    DetachAt(i, !ContainsCurrentModifier(current, registration.modifier));
                }

                for (var i = 0; i < current.Count; i++)
                {
                    var style = current[i];
                    if (!style.Enabled || !style.IsValid) continue;
                    var registrationIndex = RegistrationIndex(style);
                    if (registrationIndex >= 0 &&
                        !RegistrationMatches(registrations[registrationIndex], style))
                    {
                        changed = true;
                        DetachAt(registrationIndex);
                        registrationIndex = -1;
                    }
                    if (registrationIndex < 0)
                    {
                        registrations.Insert(targetIndex, Attach(style));
                        managedStyleCount++;
                        changed = true;
                    }
                    else
                    {
                        var wasManaged = registrationIndex < managedStyleCount;
                        if (registrationIndex != targetIndex)
                        {
                            var registration = registrations[registrationIndex];
                            registrations.RemoveAt(registrationIndex);
                            registrations.Insert(targetIndex, registration);
                            changed = true;
                        }
                        if (!wasManaged)
                        {
                            managedStyleCount++;
                            changed = true;
                        }
                    }
                    targetIndex++;
                }

                while (managedStyleCount > targetIndex)
                {
                    changed = true;
                    DetachAt(managedStyleCount - 1);
                }
                return changed;
            }
            finally
            {
                if (changed) RebuildRegistrationIndexes();
            }
        }

        private int RegistrationIndex(Style style)
        {
            for (var i = 0; i < registrations.Count; i++)
                if (ReferenceEquals(registrations[i].ownerStyle, style)) return i;
            return -1;
        }

        private static bool RegistrationMatches(Registration registration, Style style)
            => ReferenceEquals(registration.source, style.EffectiveSource) &&
               ReferenceEquals(registration.modifier, style.Modifier) &&
               registration.wholeText == (style.Source == null);

        private static bool IsCurrent(Registration registration, IReadOnlyList<Style> current)
        {
            for (var i = 0; i < current.Count; i++)
            {
                var style = current[i];
                if (ReferenceEquals(registration.ownerStyle, style))
                    return style.Enabled && style.IsValid && RegistrationMatches(registration, style);
            }
            return false;
        }

        private static bool ContainsCurrentModifier(IReadOnlyList<Style> current,
            BaseModifier modifier)
        {
            if (modifier == null) return false;
            for (var i = 0; i < current.Count; i++)
            {
                var style = current[i];
                if (style.Enabled && style.IsValid && ReferenceEquals(style.Modifier, modifier))
                    return true;
            }
            return false;
        }

        private void RebuildRegistrationIndexes()
        {
            allRules.Clear();
            ruleToModifier.Clear();
            hasExternalSources = false;
            for (var i = 0; i < registrations.Count; i++)
            {
                var registration = registrations[i];
                if (registration.rule != null)
                {
                    allRules.Add(registration.rule);
                    ruleToModifier[registration.rule] = registration.modifier;
                }
                else if (!registration.wholeText)
                    hasExternalSources = true;
            }
            rulesDirty = true;
        }

        private void DetachAt(int index, bool releaseModifier = true)
        {
            var registration = registrations[index];
            registrations.RemoveAt(index);
            if (index < managedStyleCount) managedStyleCount--;
            RemoveModifierWork(registration.modifier);
            try
            {
                registration.source.Unbind(this);
            }
            finally
            {
                if (releaseModifier) ReleaseModifier(registration.modifier);
            }
        }

        private void RemoveModifierWork(BaseModifier modifier)
        {
            if (modifier == null) return;

            for (var i = pendingReapply.Count - 1; i >= 0; i--)
                if (ContainsModifier(modifier, pendingReapply[i]))
                    pendingReapply.RemoveAt(i);
            for (var i = spans.Count - 1; i >= 0; i--)
                if (ContainsModifier(modifier, spans[i].modifier))
                    spans.RemoveAt(i);
        }

        private static void ReleaseModifier(BaseModifier modifier)
        {
            if (modifier == null) return;
            try
            {
                modifier.Destroy();
            }
            finally
            {
                modifier.SetOwner(null);
            }
        }

        /// <summary>Deinitializes all registered modifiers.</summary>
        public void DeinitializeModifiers()
        {
            DisableAppliedModifiers();
            for (var i = 0; i < registrations.Count; i++)
                registrations[i].modifier?.Destroy();
        }

        /// <summary>Releases registrations and pooled buffers.</summary>
        public void Release()
        {
            while (registrations.Count != 0) DetachAt(registrations.Count - 1);
            managedStyleCount = 0;
            pendingReapply.Clear();
            spans.Return();
            spanClipScratch.Return();
            tempRanges.Return();
            tagRemovals.Return();
            tagInsertions.Return();
            indexMapBuffer.Return();
            cleanTextBuffer.Return();
            charToCpMap.Return();
            codepointToCharMap.Return();
            revisionTextBuffer.Return();
            componentKeys.Return();
            componentOrder.Return();
            componentParent.Return();
            previousRanges.Clear();
            currentRanges.Clear();
            duplicateRangeOrdinals?.Clear();
            ambiguousRanges?.Clear();
            matchKeyDuplicates?.Clear();
            sourceMatches?.Clear();
            sourceRangeIds?.Clear();
            snapshotSources?.Clear();
            charToCpMapDirty = true;
        }

        private void OnSourceChanged()
        {
            rulesDirty = true;
            owner.SetDirty(UniTextDirty.Text);
        }

        private TextSnapshot GetTextSnapshotForSource() => TextSnapshot;

        /// <summary>
        /// Disables every modifier the previous cycle applied. The spans are the authoritative
        /// record of who painted: config lists (markup chrome) can lose members between parses,
        /// and an unlisted modifier would otherwise stay subscribed with a stale buffer.
        /// </summary>
        private void DisableAppliedModifiers()
        {
            for (var i = 0; i < spans.Count; i++)
                spans[i].modifier?.Disable();
        }

        private string markupTriggers = "";
        private string typingTriggers = "";
        private readonly bool[] triggerTable = new bool[128];
        private bool scanEnabled;

        /// <summary>
        /// Union of the registered rules' <see cref="ParseRule.MarkupTriggers"/> — the characters that can
        /// start a match that alters the visible text; <see langword="null"/> when any rule reports unknown.
        /// A literal run containing none of these characters cannot be consumed as markup, so
        /// literal-paste escaping and autoformat skip it without allocating.
        /// </summary>
        internal string MarkupTriggers
        {
            get
            {
                EnsureRulesPrepared();
                return markupTriggers;
            }
        }

        /// <summary>Cached union used by editable hosts to decide whether a direct typing edit can change source markup.</summary>
        internal string TypingTriggers
        {
            get
            {
                EnsureRulesPrepared();
                return typingTriggers;
            }
        }

        private void EnsureRulesPrepared()
        {
            if (!rulesDirty) return;
            allRules.Sort(rulePriorityComparer);
            BuildDerivedRegistrations();
            markupTriggers = ParseRule.MarkupTriggerUnion(allRules);
            typingTriggers = ParseRule.TypingTriggerUnion(allRules);
            BuildTriggerTable();
            rulesDirty = false;
        }

        private void BuildDerivedRegistrations()
        {
            derivedRegistrations.Clear();
            var order = 0;
            for (var i = 0; i < registrations.Count; i++)
            {
                var registration = registrations[i];
                if (registration.modifier == null) continue;
                AddDerivedLeaves(registration.rule, registration.modifier, ref order);
            }
            derivedRegistrations.Sort(derivedPriorityComparer);
            BuildDerivedTriggerTable();
        }

        private void BuildDerivedTriggerTable()
        {
            Array.Clear(derivedTriggerTable, 0, derivedTriggerTable.Length);
            derivedScanEnabled = true;
            hasDerivedMatchers = false;
            for (var i = 0; i < derivedRegistrations.Count; i++)
            {
                var triggers = derivedRegistrations[i].rule.ScanTriggers;
                if (triggers == null)
                {
                    hasDerivedMatchers = true;
                    derivedScanEnabled = false;
                    return;
                }
                if (triggers.Length == 0) continue;
                hasDerivedMatchers = true;
                for (var j = 0; j < triggers.Length; j++)
                {
                    var c = triggers[j];
                    if (c >= derivedTriggerTable.Length)
                    {
                        derivedScanEnabled = false;
                        return;
                    }
                    derivedTriggerTable[c] = true;
                }
            }
        }

        private void AddDerivedLeaves(ParseRule rule, BaseModifier modifier, ref int order)
        {
            if (rule is CompositeParseRule composite)
            {
                for (var i = 0; i < composite.Rules.Count; i++)
                    AddDerivedLeaves(composite.Rules[i], modifier, ref order);
                return;
            }
            if (rule?.MarkupTriggers == string.Empty)
                derivedRegistrations.Add(new DerivedRegistration(rule, modifier, order++));
        }

        /// <summary>
        /// Bakes the rules' <see cref="ParseRule.ScanTriggers"/> into an ASCII jump table so the match walk
        /// skips runs of plain text without probing every rule at every character. Disabled (linear walk)
        /// when any rule reports an unknown or non-ASCII trigger set — correctness never depends on the scan.
        /// </summary>
        private void BuildTriggerTable()
        {
            Array.Clear(triggerTable, 0, triggerTable.Length);
            scanEnabled = true;
            for (var i = 0; i < allRules.Count; i++)
            {
                var triggers = allRules[i].ScanTriggers;
                if (triggers == null)
                {
                    scanEnabled = false;
                    return;
                }
                for (var j = 0; j < triggers.Length; j++)
                {
                    var c = triggers[j];
                    if (c >= 128)
                    {
                        scanEnabled = false;
                        return;
                    }
                    triggerTable[c] = true;
                }
            }
        }

        private int SkipToTrigger(ReadOnlySpan<char> text, int index)
        {
            var table = triggerTable;
            for (var i = index; i < text.Length; i++)
            {
                var c = text[i];
                if (c < 128 && table[c]) return i;
            }
            return text.Length;
        }

        /// <summary>The registered rule providing the literal-escape capability (<see cref="ParseRule.EscapePrefix"/>), or null.</summary>
        internal ParseRule LiteralEscapeRule()
        {
            for (var i = 0; i < allRules.Count; i++)
                if (allRules[i].ProvidesLiteralEscape) return allRules[i];
            return null;
        }

        /// <summary>Applies all parsed attribute spans to their modifiers.</summary>
        /// <remarks>
        /// <para>
        /// Two-pass application:
        /// 1. Prepare pass - initializes and clears buffers for used modifiers
        /// 2. Apply pass - writes data to buffers in reverse order
        /// </para>
        /// <para>
        /// Converts span indices from UTF-16 code units to Unicode codepoints
        /// before passing to modifiers, since the text processing pipeline
        /// works in codepoint space (HarfBuzz, cluster indices, etc.).
        /// </para>
        /// </remarks>
        public void Apply()
        {
            if (spans.Count == 0)
                return;

            applyCycle++;
            BuildCharToCodepointMap();
            if (UniTextRanges.TryGet(owner, out var ranges))
                ranges.ReconcileOwnedMembership(suppressInvalidation: true);

            for (var i = 0; i < spans.Count; i++)
            {
                if (!spans[i].modifier.IsInitialized)
                    spans[i].modifier.Prepare();
            }

            var map = charToCpMap.data;
            for (var i = spans.Count - 1; i >= 0; i--)
            {
                ref readonly var span = ref spans[i];
                var cpStart = map[span.start];
                var cpEnd = map[span.end];
                var segment = new RangeSegment(span.segmentId,
                    new TextRange(cpStart, cpEnd - cpStart), span.pointAffinity, span.tracking);
                var attribution = span.Attribution;
                var context = new RangeApplyContext(in attribution, in segment, in span.parameters);
                span.modifier.Apply(in context);
            }
        }

        /// <summary>
        /// Re-runs <see cref="BaseModifier.OnApply"/> over retained spans without re-parsing, for
        /// parameter changes that leave the text and markup untouched. All dirty modifiers of the
        /// frame replay in ONE pass: every modifier whose spans transitively overlap a dirty one
        /// re-applies together in the same REVERSE global order as <see cref="Apply"/> — a lone
        /// re-apply would let a dirty modifier win contested buffer slots it lost at parse time,
        /// flipping the visual on the next full rebuild. Overlap components are computed once by a
        /// start-sorted sweep plus union-find (spans sharing a modifier, or an
        /// <see cref="AttributeChannel"/> whose per-cycle state they jointly fill, are linked), so
        /// the cost is O(n log n) in span count instead of a quadratic fixpoint per dirty modifier.
        /// Non-overlapping modifiers never re-fire, so unrelated side effects (injected virtual
        /// glyphs, provider mutations) stay untouched. Clearing per-apply accumulators happens
        /// lazily inside <see cref="BaseModifier.Apply"/> on each modifier's first range of the
        /// bumped <see cref="ApplyCycle"/> — spans are unchanged here, so every replayed modifier
        /// receives at least one range and therefore resets.
        /// </summary>
        private void ReApply(IReadOnlyList<BaseModifier> modifiers)
        {
            var spanCount = spans.Count;
            if (modifiers == null || modifiers.Count == 0 || spanCount == 0) return;

            reapplyScratch.Clear();
            for (var i = 0; i < modifiers.Count; i++)
            {
                var m = modifiers[i];
                if (m != null && m.IsInitialized && !ReferenceIdentity.Contains(reapplyScratch, m))
                    reapplyScratch.Add(m);
            }
            if (reapplyScratch.Count == 0) return;

            if (reapplyComponentsGeneration != parseGeneration)
            {
                reapplyComponentsGeneration = parseGeneration;
                componentKeys.EnsureCapacity(spanCount);
                componentOrder.EnsureCapacity(spanCount);
                componentParent.EnsureCapacity(spanCount);
                var sortKeys = componentKeys.data;
                var order = componentOrder.data;
                var buildParent = componentParent.data;
                for (var i = 0; i < spanCount; i++)
                {
                    sortKeys[i] = spans[i].start;
                    order[i] = i;
                    buildParent[i] = i;
                }
                Array.Sort(sortKeys, order, 0, spanCount);

                var clusterRoot = -1;
                var clusterEnd = int.MinValue;
                for (var k = 0; k < spanCount; k++)
                {
                    var s = order[k];
                    ref readonly var span = ref spans[s];
                    if (span.modifier == null || !span.modifier.IsInitialized) continue;
                    if (clusterRoot < 0 || span.start >= clusterEnd)
                    {
                        clusterRoot = s;
                        clusterEnd = span.end;
                        continue;
                    }
                    UnionNodes(buildParent, clusterRoot, s);
                    if (span.end > clusterEnd) clusterEnd = span.end;
                }

                componentModifiers.Clear();
                componentModifierNodes.Clear();
                for (var i = 0; i < spanCount; i++)
                {
                    var m = spans[i].modifier;
                    if (m == null || !m.IsInitialized) continue;
                    var identity = m.ChannelIdentity ?? m;
                    var idx = ReferenceIdentity.IndexOf(componentModifiers, identity);
                    if (idx < 0)
                    {
                        componentModifiers.Add(identity);
                        componentModifierNodes.Add(i);
                    }
                    else
                    {
                        UnionNodes(buildParent, componentModifierNodes[idx], i);
                    }
                }
            }

            var keys = componentKeys.data;
            var parent = componentParent.data;
            for (var i = 0; i < spanCount; i++) keys[i] = 0;
            for (var i = 0; i < spanCount; i++)
            {
                var m = spans[i].modifier;
                if (m == null || !ContainsRefOrDescendant(m, reapplyScratch)) continue;
                keys[FindNode(parent, i)] = 1;
            }

            applyCycle++;
            BuildCharToCodepointMap();
            var map = charToCpMap.data;

            for (var i = spanCount - 1; i >= 0; i--)
            {
                ref readonly var span = ref spans[i];
                var m = span.modifier;
                if (m == null || !m.IsInitialized) continue;
                if (keys[FindNode(parent, i)] == 0) continue;
                var cpStart = map[span.start];
                var cpEnd = map[span.end];
                var segment = new RangeSegment(span.segmentId,
                    new TextRange(cpStart, cpEnd - cpStart), span.pointAffinity, span.tracking);
                var attribution = span.Attribution;
                var context = new RangeApplyContext(in attribution, in segment, in span.parameters);
                m.Apply(in context);
            }
        }

        internal bool HasPendingReapply => pendingReapply.Count != 0;

        internal void QueueReapply(BaseModifier modifier)
        {
            if (!ReferenceIdentity.Contains(pendingReapply, modifier))
                pendingReapply.Add(modifier);
        }

        internal void ClearPendingReapply() => pendingReapply.Clear();

        internal void ReApplyPending()
        {
            ReApply(pendingReapply);
            pendingReapply.Clear();
        }

        private static int FindNode(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void UnionNodes(int[] parent, int a, int b)
        {
            a = FindNode(parent, a);
            b = FindNode(parent, b);
            if (a != b) parent[b] = a;
        }

        /// <summary>
        /// Whether <paramref name="modifier"/> itself or any transitive child is in the dirty set. The parser
        /// holds one root modifier per span (a <see cref="CompositeModifier"/> registers once), but a parameter
        /// change is reported by the leaf whose field moved — its setter calls
        /// <c>MarkModifierDirty</c> with the child, never the composite. Walking down through
        /// <see cref="BaseModifier.Children"/> is what pairs a dirty child back to its owning span so the
        /// composite (and therefore the child) actually replays; a plain reference match would silently skip it.
        /// </summary>
        private static bool ContainsRefOrDescendant(BaseModifier modifier, List<BaseModifier> set)
        {
            if (ReferenceIdentity.Contains(set, modifier)) return true;
            var children = modifier.Children;
            if (children == null) return false;
            for (var i = 0; i < children.Count; i++)
                if (children[i] != null && ContainsRefOrDescendant(children[i], set)) return true;
            return false;
        }

        /// <summary>
        /// Emits every parsed span in codepoint space (start/end converted from clean-text UTF-16
        /// char indices to match <see cref="PositionedGlyph.cluster"/>) for inspection tooling.
        /// A codepoint is covered when <c>start &lt;= cp &lt; end</c>.
        /// </summary>
        internal void CollectSpansForInspection(List<(int start, int end, BaseModifier modifier, string parameter)> output)
        {
            output.Clear();
            if (spans.Count == 0) return;

            BuildCharToCodepointMap();
            var map = charToCpMap.data;

            for (var i = 0; i < spans.Count; i++)
            {
                ref readonly var s = ref spans[i];
                output.Add((map[s.start], map[s.end], s.modifier, s.parameter));
            }
        }

        /// <summary>
        /// The source-text char ranges the last <see cref="Parse"/> projected away from the visible text —
        /// stripped tags (visible width 0) and self-closing insertions (an object/escape glyph, visible width
        /// = inserted length) — sorted by start, non-overlapping. Empty after a plain-text parse with nothing
        /// hidden. The editing layer turns these into the synthesized view's <see cref="MarkupViewMap"/>, so a tag
        /// that renders a glyph is counted as that glyph rather than its source characters.
        /// </summary>
        internal void CollectProjectedRanges(List<ProjectedRange> into)
        {
            into.Clear();
            for (var i = 0; i < tagRemovals.Count; i++)
                into.Add(new ProjectedRange(tagRemovals[i].start, tagRemovals[i].end - tagRemovals[i].start, null));
            for (var i = 0; i < tagInsertions.Count; i++)
            {
                var ins = tagInsertions[i];
                into.Add(new ProjectedRange(ins.start, ins.end - ins.start, ins.insert));
            }
            into.Sort(projectedRangeComparer);
        }

        /// <summary>
        /// Collects the modifiers whose span unions cover the codepoint range whole
        /// (<paramref name="endCp"/> exclusive; the range must be non-empty).
        /// </summary>
        internal void CollectModifiersCovering(int startCp, int endCp,
            List<BaseModifier> into, List<(string parameter, bool uniform)> parameters)
        {
            if (spans.Count == 0) return;

            BuildCharToCodepointMap();
            var map = charToCpMap.data;
            var mapLen = charToCpMap.count;

            unionRejectedScratch.Clear();
            for (var i = 0; i < spans.Count; i++)
            {
                ref readonly var span = ref spans[i];
                if (span.modifier == null || into.Contains(span.modifier)) continue;
                if (ReferenceIdentity.Contains(unionRejectedScratch, span.modifier)) continue;
                if (span.start < 0 || span.end > mapLen - 1) continue;
                var cpStart = map[span.start];
                var cpEnd = map[span.end];

                if (cpStart > startCp || cpEnd <= startCp) continue;
                if (UnionCovers(span.modifier, startCp, endCp, map, mapLen, out var parameter, out var uniform))
                {
                    into.Add(span.modifier);
                    parameters.Add((parameter, uniform));
                }
                else
                {
                    unionRejectedScratch.Add(span.modifier);
                }
            }
        }

        /// <summary>
        /// Whether the UNION of the modifier's spans covers the codepoint range — split
        /// same-style runs (two adjacent bold spans) count as continuous coverage, matching
        /// <c>IsRangeFullyStyled</c>'s sweep. <paramref name="parameter"/> carries
        /// the spans' shared value; <paramref name="uniform"/> is <see langword="false"/>
        /// (and the parameter <see langword="null"/>) when intersecting spans disagree.
        /// </summary>
        private bool UnionCovers(BaseModifier modifier, int startCp, int endCp, int[] map, int mapLen,
            out string parameter, out bool uniform)
        {
            parameter = null;
            uniform = true;
            unionScratch.Clear();

            var seen = false;
            for (var i = 0; i < spans.Count; i++)
            {
                ref readonly var span = ref spans[i];
                if (!ReferenceEquals(span.modifier, modifier)) continue;
                if (span.start < 0 || span.end > mapLen - 1) continue;
                var cpStart = map[span.start];
                var cpEnd = map[span.end];
                if (cpEnd <= startCp || cpStart >= endCp) continue;

                if (!seen)
                {
                    parameter = span.parameter;
                    seen = true;
                }
                else if (uniform && !string.Equals(parameter, span.parameter, StringComparison.Ordinal))
                {
                    uniform = false;
                }

                unionScratch.Add((cpStart, cpEnd));
            }
            if (!seen) return false;

            unionScratch.Sort(removalComparer);
            var cursor = startCp;
            for (var i = 0; i < unionScratch.Count && cursor < endCp; i++)
            {
                var (s, e) = unionScratch[i];
                if (s > cursor) break;
                if (e > cursor) cursor = e;
            }
            if (cursor < endCp) return false;
            if (!uniform) parameter = null;
            return true;
        }

        /// <summary>
        /// Builds mapping from UTF-16 char indices to Unicode codepoint indices.
        /// Surrogate pairs (2 chars) map to the same codepoint index. The clean text only changes
        /// inside <see cref="Parse"/>, so the map is version-stamped and rebuilt at most once per
        /// parse — caret-context queries between parses reuse the cached map.
        /// </summary>
        private void BuildCharToCodepointMap()
        {
            if (!charToCpMapDirty) return;
            charToCpMapDirty = false;

            var text = cleanTextBuffer.Span;
            var len = text.Length;

            charToCpMap.EnsureCapacity(len + 1);
            var map = charToCpMap.data;

            var cpIndex = 0;
            for (var i = 0; i < len; i++)
            {
                map[i] = cpIndex;
                if (UnicodeData.SizeAt(text, i) == 2)
                {
                    i++;
                    map[i] = cpIndex;
                }
                cpIndex++;
            }
            map[len] = cpIndex;
            charToCpMap.count = len + 1;
        }

        /// <summary>One markup occurrence a registered rule matched: its modifier plus the matched range (char space).</summary>
        internal readonly struct MarkupMatch
        {
            public readonly BaseModifier modifier;
            public readonly ParseRule rule;
            public readonly ParsedRange range;

            /// <summary>
            /// False for an unclosed match (still being typed) — its markup must stay. A negative
            /// close start means the syntax has no close marker by design (list items), which
            /// counts as complete; consumers must not slice close-tag chars in that case.
            /// </summary>
            public bool IsComplete => (range.sourceRule ?? rule).IsCompleteMatch(in range);

            public MarkupMatch(BaseModifier modifier, ParseRule rule, in ParsedRange range)
            {
                this.modifier = modifier;
                this.rule = rule;
                this.range = range;
            }
        }

        /// <summary>
        /// Match-only pass over the registered rules — whatever syntax each rule speaks (tags,
        /// markers, custom) — reporting every markup occurrence without producing spans or
        /// clean text. The autoformat conversion consumes this, so it converts exactly what the
        /// component would style, with the rule's own modifier and merged parameters. Runs the
        /// same walk as <see cref="Parse"/> (<see cref="MatchWalk"/>), so the two can never diverge.
        /// </summary>
        internal void CollectMarkupMatches(ReadOnlySpan<char> text, List<MarkupMatch> into, bool includeSelfClosing = false)
        {
            into.Clear();
            if (text.IsEmpty) return;
            MatchWalk(text, default, into, includeSelfClosing);
        }

        private void AddMarkupMatches(ParseRule rule, List<MarkupMatch> into, bool includeSelfClosing)
        {
            for (var j = 0; j < tempRanges.Count; j++)
            {
                ref readonly var range = ref tempRanges[j];
                if (!range.HasMarkup) continue;
                if (!includeSelfClosing && !range.IsRemovableMarkup) continue;
                ruleToModifier.TryGetValue(rule, out var modifier);
                into.Add(new MarkupMatch(modifier, rule, in range));
            }
        }

        private sealed class DerivedPriorityComparerImpl : IComparer<DerivedRegistration>
        {
            public int Compare(DerivedRegistration a, DerivedRegistration b)
            {
                var priority = b.rule.Priority.CompareTo(a.rule.Priority);
                return priority != 0 ? priority : a.order.CompareTo(b.order);
            }
        }

        /// <summary>Parses the input text, extracting markup and building clean text.</summary>
        /// <param name="text">The raw text with markup tags to parse.</param>
        /// <param name="projection">
        /// Editable-host rendering projection (plain-text mode, completed-tag stripping, reveal window,
        /// markup chrome). Default = static parse. Passed per call — the parser holds no editing-layer
        /// state between parses.
        /// </param>
        public void Parse(ReadOnlySpan<char> text, in ParseProjection projection = default)
        {
            DisableAppliedModifiers();
            spans.FakeClear();
            tagRemovals.FakeClear();
            tagInsertions.FakeClear();
            cleanTextBuffer.FakeClear();
            cachedCleanTextString = null;
            charToCpMapDirty = true;
            parseGeneration++;

            MatchWalk(text, in projection, null, false);
            BuildCleanTextAndRemapIndices(text);

            if (!text.IsEmpty)
            {
                for (var r = 0; r < allRules.Count; r++)
                {
                    var rule = allRules[r];
                    tempRanges.FakeClear();
                    rule.PostParse(cleanTextBuffer.Span, tempRanges);

                    for (var j = 0; j < tempRanges.Count; j++)
                    {
                        ref readonly var range = ref tempRanges[j];
                        CreateSpanForRule(rule, in range);
                    }
                }
            }

            var cleanLen = cleanTextBuffer.count;
            if (cleanLen > 0)
            {
                for (var i = 0; i < registrations.Count; i++)
                {
                    var reg = registrations[i];
                    if (reg.rule != null) continue;
                    if (!reg.wholeText) continue;
                    if (reg.modifier == null) continue;
                    spans.Add(new AttributeSpan(0, cleanLen, reg.modifier,
                        ModifierParameters.Default(reg.ownerStyle?.DefaultParameter), reg.source));
                }
            }

            FinalizeRangeSources();
            ProtectChromeSpans(projection.chrome);
        }

        /// <summary>Builds render attributes from visible text plus persistent document ranges.</summary>
        internal void ParseAttributed(ReadOnlySpan<char> text, IReadOnlyList<AttributeSpan> persistent,
            IReadOnlyList<(int start, int end)> protectedRanges)
        {
            DisableAppliedModifiers();
            spans.FakeClear();
            tagRemovals.FakeClear();
            tagInsertions.FakeClear();
            cleanTextBuffer.FakeClear();
            cachedCleanTextString = null;
            charToCpMapDirty = true;
            parseGeneration++;

            cleanTextBuffer.EnsureCapacity(text.Length);
            text.CopyTo(cleanTextBuffer.data.AsSpan(0, text.Length));
            cleanTextBuffer.count = text.Length;

            if (persistent != null)
            {
                for (var i = 0; i < persistent.Count; i++)
                {
                    var span = persistent[i];
                    if (span.modifier != null && span.end > span.start)
                        spans.Add(span);
                }
            }

            EnsureRulesPrepared();
            for (var i = 0; i < derivedRegistrations.Count; i++)
                derivedRegistrations[i].rule.Reset();

            var index = 0;
            while (hasDerivedMatchers && index < text.Length)
            {
                if (derivedScanEnabled)
                {
                    while (index < text.Length)
                    {
                        var c = text[index];
                        if (c < derivedTriggerTable.Length && derivedTriggerTable[c]) break;
                        index++;
                    }
                    if (index >= text.Length) break;
                }
                if (TryProtectedEnd(index, protectedRanges, out var protectedEnd))
                {
                    index = protectedEnd;
                    continue;
                }

                var next = index;
                for (var i = 0; i < derivedRegistrations.Count; i++)
                {
                    var registration = derivedRegistrations[i];
                    tempRanges.FakeClear();
                    var result = registration.rule.TryMatch(text, index, tempRanges);
                    if (result <= index) continue;
                    AddDerivedRanges(registration.rule, registration.modifier, protectedRanges);
                    next = result;
                    break;
                }
                index = next > index ? next : index + 1;
            }

            for (var i = 0; i < derivedRegistrations.Count; i++)
            {
                var registration = derivedRegistrations[i];
                tempRanges.FakeClear();
                registration.rule.Finalize(text, tempRanges);
                AddDerivedRanges(registration.rule, registration.modifier, protectedRanges);
                tempRanges.FakeClear();
                registration.rule.PostParse(text, tempRanges);
                AddDerivedRanges(registration.rule, registration.modifier, protectedRanges);
            }

            if (!text.IsEmpty)
            {
                for (var i = 0; i < registrations.Count; i++)
                {
                    var registration = registrations[i];
                    if (registration.wholeText && registration.modifier != null)
                        spans.Add(new AttributeSpan(0, text.Length, registration.modifier,
                            ModifierParameters.Default(registration.ownerStyle?.DefaultParameter), registration.source));
                }
            }

            FinalizeRangeSources();
        }

        private void AddDerivedRanges(ParseRule rule, BaseModifier modifier,
            IReadOnlyList<(int start, int end)> protectedRanges)
        {
            for (var i = 0; i < tempRanges.Count; i++)
            {
                ref readonly var range = ref tempRanges[i];
                if (range.end <= range.start || OverlapsProtected(range.start, range.end, protectedRanges)) continue;
                var source = range.sourceRule ?? rule;
                spans.Add(new AttributeSpan(range.start, range.end, modifier, range.Parameters,
                    source, range.channel, range.payload, range.matchKey));
            }
        }

        private void FinalizeRangeSources()
        {
            UpdateTextSnapshot();
            AddExternalSourceRanges();
            AssignRangeAttributions();
        }

        private void UpdateTextSnapshot()
        {
            var cleanText = cleanTextBuffer.Span;
            var changed = !textRevision.IsValid || !revisionTextBuffer.Span.SequenceEqual(cleanText);
            if (changed)
            {
                if (textRevision.IsValid)
                {
                    if (previousRanges.Count != 0)
                    {
                        var edit = TextEditMap.Detect(revisionTextBuffer.Span, cleanText);
                        RemapPreviousRanges(in edit);
                    }
                }
                revisionTextBuffer.EnsureCapacity(cleanText.Length);
                cleanText.CopyTo(revisionTextBuffer.data.AsSpan(0, cleanText.Length));
                revisionTextBuffer.count = cleanText.Length;
                nextTextRevision++;
                if (nextTextRevision == 0) nextTextRevision++;
                textRevision = new TextRevision(nextTextRevision);
                textSnapshot = default;
                snapshotText = null;
            }

            if (hasExternalSources || snapshotTrackingEnabled) EnsureTextSnapshot();
            if (!textSnapshot.IsValid) return;

            snapshotSources ??= new HashSet<RangeSource>();
            snapshotSources.Clear();
            for (var i = 0; i < registrations.Count; i++)
                SetSourceSnapshot(registrations[i].source, snapshotSources);
            snapshotSources.Clear();
        }

        private void EnsureTextSnapshot()
        {
            if (textSnapshot.IsValid) return;
            if (!textRevision.IsValid)
                throw new InvalidOperationException("A text snapshot is unavailable before the first parse.");
            snapshotText = revisionTextBuffer.count == 0
                ? string.Empty
                : new string(revisionTextBuffer.data, 0, revisionTextBuffer.count);
            textSnapshot = new TextSnapshot(snapshotText, textRevision);
        }

        private void RemapPreviousRanges(in TextEditMap edit)
        {
            currentRanges.Clear();
            ambiguousRanges?.Clear();
            foreach (var pair in previousRanges)
            {
                if (!pair.Key.TryMap(in edit, out var mapped) || ambiguousRanges?.Contains(mapped) == true)
                    continue;
                if (!currentRanges.TryAdd(mapped, pair.Value))
                {
                    currentRanges.Remove(mapped);
                    (ambiguousRanges ??= new HashSet<ParserRangeKey>()).Add(mapped);
                }
            }

            ambiguousRanges?.Clear();
            var swap = previousRanges;
            previousRanges = currentRanges;
            currentRanges = swap;
            currentRanges.Clear();
        }

        private void SetSourceSnapshot(RangeSource source, HashSet<RangeSource> visited)
        {
            if (source == null || !visited.Add(source)) return;
            source.SetSnapshot(in textSnapshot);
            if (source.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++)
                SetSourceSnapshot(children[i], visited);
        }

        private void AddExternalSourceRanges()
        {
            if (!hasExternalSources) return;
            BuildCodepointToCharMap();
            var cpToChar = codepointToCharMap.data;
            sourceMatches ??= new List<RangeSourceMatch>();
            sourceRangeIds ??= new HashSet<RangeId>();

            for (var i = 0; i < registrations.Count; i++)
            {
                var registration = registrations[i];
                if (registration.rule != null || registration.wholeText || registration.modifier == null)
                    continue;

                sourceMatches.Clear();
                registration.source.Collect(in textSnapshot, sourceMatches);
                sourceRangeIds.Clear();
                for (var m = 0; m < sourceMatches.Count; m++)
                {
                    var match = sourceMatches[m];
                    if (match.hasRangeId && !sourceRangeIds.Add(match.rangeId))
                        throw new InvalidOperationException(
                            $"Range source '{registration.source.Identity}' emitted duplicate identity {match.rangeId}.");
                    RangeAttribution attribution = default;
                    if (match.hasRangeId)
                    {
                        attribution = new RangeAttribution(registration.source,
                            new RangeIdentity(registration.source.RuntimeId, match.rangeId),
                            match.channel, match.payload, match.revision);
                    }

                    for (var s = 0; s < match.segments.Length; s++)
                    {
                        var segment = match.segments[s];
                        var start = cpToChar[segment.Range.start];
                        var end = cpToChar[segment.Range.End];
                        if (!attribution.IsValid)
                        {
                            spans.Add(new AttributeSpan(start, end, registration.modifier,
                                match.parameters, registration.source, match.channel, match.payload,
                                match.matchKey, segment.PointAffinity, segment.Tracking));
                        }
                        else
                        {
                            spans.Add(new AttributeSpan(start, end, registration.modifier,
                                match.parameters, attribution, segment.Id,
                                segment.PointAffinity, segment.Tracking));
                        }
                    }
                }
            }
            sourceMatches.Clear();
            sourceRangeIds.Clear();
        }

        private void AssignRangeAttributions()
        {
            if (spans.Count == 0)
            {
                previousRanges.Clear();
                currentRanges.Clear();
                duplicateRangeOrdinals?.Clear();
                ambiguousRanges?.Clear();
                matchKeyDuplicates?.Clear();
                return;
            }
            BuildCharToCodepointMap();
            var charToCp = charToCpMap.data;
            currentRanges.Clear();
            duplicateRangeOrdinals?.Clear();
            matchKeyDuplicates?.Clear();

            for (var i = 0; i < spans.Count; i++)
            {
                ref readonly var span = ref spans[i];
                if (span.HasAttribution || span.source == null || span.matchKey == null) continue;
                var stableKey = new ParserRangeKey(span.source, 0, 0, span.matchKey, 0);
                matchKeyDuplicates ??= new Dictionary<ParserRangeKey, bool>();
                if (!matchKeyDuplicates.TryAdd(stableKey, false))
                    matchKeyDuplicates[stableKey] = true;
            }

            for (var i = 0; i < spans.Count; i++)
            {
                ref var span = ref spans[i];
                if (span.HasAttribution) continue;
                var source = span.source ?? throw new InvalidOperationException(
                    $"Attribute span {span} has no owning RangeSource.");
                var matchStart = charToCp[span.start];
                var matchEnd = charToCp[span.end];
                if (source is WholeTextRangeSource wholeText)
                {
                    var attribution = new RangeAttribution(source,
                        new RangeIdentity(source.RuntimeId, wholeText.RangeId), span.channel,
                        span.payload, textRevision);
                    span.AssignAttribution(in attribution, wholeText.SegmentId);
                    continue;
                }

                var stableKey = span.matchKey == null
                    ? default
                    : new ParserRangeKey(source, 0, 0, span.matchKey, 0);
                RangeSegmentId segmentId;
                if (span.matchKey != null && matchKeyDuplicates != null &&
                    matchKeyDuplicates.TryGetValue(stableKey, out var duplicate) && duplicate)
                {
                    var ambiguousIdentity = new RangeIdentity(source.RuntimeId, source.AllocateRangeId());
                    segmentId = source.AllocateSegmentId();
                    var attribution = new RangeAttribution(source, ambiguousIdentity, span.channel,
                        span.payload, textRevision);
                    span.AssignAttribution(in attribution, segmentId);
                    continue;
                }

                var key = new ParserRangeKey(source, matchStart, matchEnd, span.matchKey, 0);
                if (currentRanges.ContainsKey(key))
                {
                    duplicateRangeOrdinals ??= new Dictionary<ParserRangeKey, int>();
                    duplicateRangeOrdinals.TryGetValue(key, out var ordinal);
                    ordinal++;
                    duplicateRangeOrdinals[key] = ordinal;
                    key = new ParserRangeKey(source, matchStart, matchEnd, span.matchKey, ordinal);
                }

                RangeIdentity identity;

                if (previousRanges.TryGetValue(key, out var previous))
                {
                    identity = new RangeIdentity(source.RuntimeId, previous.rangeId);
                    segmentId = previous.segmentId;
                }
                else
                {
                    identity = new RangeIdentity(source.RuntimeId, source.AllocateRangeId());
                    segmentId = source.AllocateSegmentId();
                }

                var resolvedAttribution = new RangeAttribution(source, identity, span.channel, span.payload,
                    textRevision);
                span.AssignAttribution(in resolvedAttribution, segmentId);
                currentRanges.Add(key, new PreviousRange(identity.Range, segmentId));
            }

            var swap = previousRanges;
            previousRanges = currentRanges;
            currentRanges = swap;
            currentRanges.Clear();
            duplicateRangeOrdinals?.Clear();
            matchKeyDuplicates?.Clear();
        }

        private void BuildCodepointToCharMap()
        {
            BuildCharToCodepointMap();
            var codepointCount = charToCpMap.data[cleanTextBuffer.count];
            codepointToCharMap.EnsureCapacity(codepointCount + 1);
            var text = cleanTextBuffer.Span;
            for (var i = 0; i <= codepointCount; i++) codepointToCharMap.data[i] = i;
            var map = codepointToCharMap.data.AsSpan(0, codepointCount + 1);
            UnicodeData.MapCodepointsToChars(text, map, map);
            codepointToCharMap.count = codepointCount + 1;
        }

        private static bool TryProtectedEnd(int index, IReadOnlyList<(int start, int end)> ranges, out int end)
        {
            if (ranges != null)
            {
                for (var i = 0; i < ranges.Count; i++)
                {
                    var range = ranges[i];
                    if (index < range.start) break;
                    if (index < range.end)
                    {
                        end = range.end;
                        return true;
                    }
                }
            }
            end = index;
            return false;
        }

        private static bool OverlapsProtected(int start, int end, IReadOnlyList<(int start, int end)> ranges)
        {
            if (ranges == null) return false;
            for (var i = 0; i < ranges.Count; i++)
            {
                var range = ranges[i];
                if (range.start >= end) break;
                if (range.end > start) return true;
            }
            return false;
        }

        /// <summary>
        /// The single matching walk shared by <see cref="Parse"/> and <see cref="CollectMarkupMatches"/>:
        /// priority order, trigger-table jump scan, TryMatch advance, Finalize sweep. Collect mode
        /// (<paramref name="collectInto"/> non-null) reports matches; parse mode routes ranges into
        /// span/strip/chrome processing under <paramref name="projection"/>. Uses the shared per-instance
        /// scratch (<see cref="tempRanges"/>, rule open-tag stacks) — never run concurrently with itself.
        /// </summary>
        private void MatchWalk(ReadOnlySpan<char> text, in ParseProjection projection,
            List<MarkupMatch> collectInto, bool includeSelfClosing)
        {
            if (System.Threading.Interlocked.CompareExchange(ref walkActive, 1, 0) != 0)
                throw new InvalidOperationException(
                    "AttributeParser cannot run concurrent or reentrant match walks because each instance owns one scratch workspace.");
            try
            {
                EnsureRulesPrepared();

                for (var r = 0; r < allRules.Count; r++)
                    allRules[r].Reset();

                if (text.IsEmpty)
                    return;

                var collect = collectInto != null;
                var index = 0;
                while (index < text.Length)
                {
                    if (scanEnabled)
                    {
                        index = SkipToTrigger(text, index);
                        if (index >= text.Length) break;
                    }

                    var newIndex = index;
                    for (var r = 0; r < allRules.Count; r++)
                    {
                        var rule = allRules[r];
                        tempRanges.FakeClear();
                        var result = rule.TryMatch(text, index, tempRanges);
                        if (result > index)
                        {
                            newIndex = result;
                            if (collect) AddMarkupMatches(rule, collectInto, includeSelfClosing);
                            else ProcessMatchedRanges(rule, in projection);
                            break;
                        }
                    }

                    index = newIndex > index ? newIndex : index + 1;
                }

                for (var r = 0; r < allRules.Count; r++)
                {
                    var rule = allRules[r];
                    tempRanges.FakeClear();
                    rule.Finalize(text, tempRanges);
                    if (collect) AddMarkupMatches(rule, collectInto, includeSelfClosing);
                    else ProcessFinalizedRanges(rule, in projection);
                }
            }
            finally
            {
                System.Threading.Volatile.Write(ref walkActive, 0);
            }
        }

        private int walkActive;

        private void ProcessMatchedRanges(ParseRule rule, in ParseProjection p)
        {
            for (var j = 0; j < tempRanges.Count; j++)
            {
                ref readonly var range = ref tempRanges[j];
                var extentEnd = range.closeEnd >= range.openStart ? range.closeEnd : range.end;
                var revealed = p.revealStart >= 0 && MarkupReveal.Includes(p.revealStart, p.revealEnd, range.openStart, extentEnd);
                var strip = !p.plainText || (p.stripCompleted && !revealed);
                if (strip) ProcessRange(in range);
                if (strip || !range.IsSelfClosing) CreateSpanForRule(rule, in range);
                if (p.plainText && !strip) CreateChromeSpans(rule, in range, p.chrome);
            }
        }

        private void ProcessFinalizedRanges(ParseRule rule, in ParseProjection p)
        {
            for (var j = 0; j < tempRanges.Count; j++)
            {
                ref readonly var range = ref tempRanges[j];
                if (p.plainText && p.stripCompleted) continue;
                if (!p.plainText) ProcessRange(in range);
                CreateSpanForRule(rule, in range);
                if (p.plainText) CreateChromeSpans(rule, in range, p.chrome);
            }
        }

        /// <summary>
        /// Removes visible markup tag characters (Raw / revealed) from every non-chrome span, so the syntax
        /// shows only its chrome styling and never inherits the enclosing document style.
        /// Runs only with chrome configured; a no-op for stripped (Hidden) tags and static rendering.
        /// </summary>
        private void ProtectChromeSpans(List<ChromeRule> chrome)
        {
            if (chrome == null || chrome.Count == 0 || spans.Count == 0) return;

            protectedClipScratch.Clear();
            for (var i = 0; i < spans.Count; i++)
                if (IsChromeModifier(spans[i].modifier, chrome))
                    protectedClipScratch.Add((spans[i].start, spans[i].end));
            if (protectedClipScratch.Count == 0) return;
            protectedClipScratch.Sort(removalComparer);

            spanClipScratch.FakeClear();
            for (var i = 0; i < spans.Count; i++)
            {
                var s = spans[i];
                if (IsChromeModifier(s.modifier, chrome)) { spanClipScratch.Add(s); continue; }

                var cur = s.start;
                var emitted = false;
                for (var p = 0; p < protectedClipScratch.Count && cur < s.end; p++)
                {
                    var (ps, pe) = protectedClipScratch[p];
                    if (pe <= cur) continue;
                    if (ps >= s.end) break;
                    if (ps > cur)
                    {
                        var segmentId = emitted ? s.source.AllocateSegmentId() : s.segmentId;
                        var attribution = s.Attribution;
                        spanClipScratch.Add(new AttributeSpan(cur, ps, s.modifier, s.parameters,
                            attribution, segmentId, s.pointAffinity, s.tracking));
                        emitted = true;
                    }
                    if (pe > cur) cur = pe;
                }
                if (cur < s.end)
                {
                    var segmentId = emitted ? s.source.AllocateSegmentId() : s.segmentId;
                    var attribution = s.Attribution;
                    spanClipScratch.Add(new AttributeSpan(cur, s.end, s.modifier, s.parameters,
                        attribution, segmentId, s.pointAffinity, s.tracking));
                }
            }

            spans.FakeClear();
            for (var i = 0; i < spanClipScratch.Count; i++)
                spans.Add(spanClipScratch[i]);
        }

        private static bool IsChromeModifier(BaseModifier modifier, List<ChromeRule> chrome)
        {
            if (modifier == null) return false;
            for (var i = 0; i < chrome.Count; i++)
                if (ReferenceEquals(chrome[i]?.Style, modifier)) return true;
            return false;
        }

        private void ProcessRange(in ParsedRange range)
        {
            if (!range.HasMarkup) return;

            if (range.IsSelfClosing)
            {
                tagInsertions.Add((range.openStart, range.openEnd, range.insertString));
            }
            else
            {
                tagRemovals.Add((range.openStart, range.openEnd));
                if (range.closeStart >= 0 && range.closeStart != range.closeEnd)
                    tagRemovals.Add((range.closeStart, range.closeEnd));
            }
        }

        private void CreateSpanForRule(ParseRule rule, in ParsedRange range)
        {
            if (ruleToModifier.TryGetValue(rule, out var modifier) && modifier != null)
            {
                var source = range.sourceRule ?? rule;
                spans.Add(new AttributeSpan(range.start, range.end, modifier, range.Parameters,
                    source, range.channel, range.payload, range.matchKey));
            }
        }

        private void CreateChromeSpans(ParseRule rule, in ParsedRange range, List<ChromeRule> chrome)
        {
            if (chrome == null || chrome.Count == 0 || !range.HasMarkup) return;

            var syntax = range.sourceRule ?? rule;
            if (!ruleToModifier.TryGetValue(syntax, out var modifier) || modifier == null)
                ruleToModifier.TryGetValue(rule, out modifier);

            for (var i = 0; i < chrome.Count; i++)
            {
                var c = chrome[i];
                if (c?.Style == null || c.Selector == null || !c.Selector.Matches(syntax, modifier)) continue;
                if (!ChromeWins(chrome, c, i, syntax, modifier)) continue;

                spans.Add(new AttributeSpan(range.openStart, range.openEnd, c.Style,
                    ModifierParameters.Explicit(c.Parameter), syntax));
                if (range.closeStart < range.closeEnd)
                    spans.Add(new AttributeSpan(range.closeStart, range.closeEnd, c.Style,
                        ModifierParameters.Explicit(c.Parameter), syntax));
            }
        }

        /// <summary>
        /// Whether <paramref name="candidate"/> is the chrome rule to paint for its concern on this markup:
        /// same-kind rules (their style modifiers match by signature) resolve to the highest
        /// <see cref="IMarkupSelector.Specificity"/>, ties to the first listed; different-kind rules compose, so
        /// each kind's winner paints independently.
        /// </summary>
        private static bool ChromeWins(List<ChromeRule> chrome, ChromeRule candidate, int index, ParseRule rule, BaseModifier modifier)
        {
            for (var j = 0; j < chrome.Count; j++)
            {
                if (j == index) continue;
                var other = chrome[j];
                if (other?.Style == null || other.Selector == null) continue;
                if (!candidate.Style.SignatureMatches(other.Style)) continue;
                if (!other.Selector.Matches(rule, modifier)) continue;
                if (other.Selector.Specificity > candidate.Selector.Specificity ||
                    (other.Selector.Specificity == candidate.Selector.Specificity && j < index))
                    return false;
            }
            return true;
        }

        private void BuildCleanTextAndRemapIndices(ReadOnlySpan<char> text)
        {
            if (tagRemovals.Count == 0 && tagInsertions.Count == 0)
            {
                cleanTextBuffer.EnsureCapacity(text.Length);
                text.CopyTo(cleanTextBuffer.data.AsSpan(0, text.Length));
                cleanTextBuffer.count = text.Length;
                ClampSpanIndices(spans, text.Length);
                return;
            }

            tagRemovals.Sort(0, tagRemovals.Count, removalComparer);
            tagInsertions.Sort(0, tagInsertions.Count, insertionComparer);

            var mapSize = text.Length + 1;

            indexMapBuffer.EnsureCapacity(mapSize);
            cleanTextBuffer.EnsureCapacity(text.Length);

            var indexMap = indexMapBuffer.data;

            var offset = 0;
            var removalIdx = 0;
            var insertionIdx = 0;

            for (var i = 0; i <= text.Length; i++)
            {
                while (removalIdx < tagRemovals.Count && i >= tagRemovals[removalIdx].end)
                    removalIdx++;

                if (insertionIdx < tagInsertions.Count && i == tagInsertions[insertionIdx].start)
                {
                    var ins = tagInsertions[insertionIdx];
                    indexMap[i] = cleanTextBuffer.count;
                    AppendToCleanText(ins.insert);
                    offset += ins.end - ins.start - ins.insert.Length;
                    i = ins.end - 1;
                    insertionIdx++;
                    continue;
                }

                if (removalIdx < tagRemovals.Count && i >= tagRemovals[removalIdx].start && i < tagRemovals[removalIdx].end)
                {
                    offset++;
                    indexMap[i] = -1;
                }
                else
                {
                    indexMap[i] = i - offset;
                    if (i < text.Length)
                        cleanTextBuffer[cleanTextBuffer.count++] = text[i];
                }
            }

            RemapSpanIndices(spans, indexMap, mapSize, cleanTextBuffer.count);
        }

        private void AppendToCleanText(string str)
        {
            if (string.IsNullOrEmpty(str)) return;

            cleanTextBuffer.EnsureCapacity(cleanTextBuffer.count + str.Length);
            str.AsSpan().CopyTo(cleanTextBuffer.data.AsSpan(cleanTextBuffer.count));
            cleanTextBuffer.count += str.Length;
        }

        private static int ResolveMappedIndex(int[] indexMap, int idx, int mapLength)
        {
            for (var j = idx; j < mapLength; j++)
                if (indexMap[j] >= 0) return indexMap[j];
            for (var j = idx - 1; j >= 0; j--)
                if (indexMap[j] >= 0) return indexMap[j];
            return -1;
        }

        private static void ClampSpanIndices(PooledList<AttributeSpan> spans, int textLength)
        {
            for (var i = 0; i < spans.Count; i++)
            {
                ref var span = ref spans[i];

                var newStart = span.start < 0 ? 0 : span.start > textLength ? textLength : span.start;
                var newEnd = span.end < 0 ? 0 : span.end > textLength ? textLength : span.end;
                if (newStart > newEnd) newStart = newEnd;

                span.start = newStart;
                span.end = newEnd;
            }
        }

        private static void RemapSpanIndices(PooledList<AttributeSpan> spans, int[] indexMap, int mapLength,
            int cleanTextLength)
        {
            for (var i = 0; i < spans.Count; i++)
            {
                ref var span = ref spans[i];

                var startIdx = span.start < 0 ? 0 : span.start >= mapLength ? mapLength - 1 : span.start;
                var endIdx = span.end < 0 ? 0 : span.end >= mapLength ? mapLength - 1 : span.end;

                var newStart = indexMap[startIdx];
                var newEnd = indexMap[endIdx];

                if (newStart < 0) newStart = ResolveMappedIndex(indexMap, startIdx, mapLength);
                if (newEnd < 0) newEnd = ResolveMappedIndex(indexMap, endIdx, mapLength);

                if (newStart < 0) newStart = 0;
                if (newEnd < 0) newEnd = cleanTextLength;
                if (newEnd > cleanTextLength) newEnd = cleanTextLength;
                if (newStart > newEnd) newStart = newEnd;

                span.start = newStart;
                span.end = newEnd;
            }
        }
    }
}
