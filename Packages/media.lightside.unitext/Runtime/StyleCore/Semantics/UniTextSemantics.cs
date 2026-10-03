using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Platform-neutral semantic tree owned by one UniText component. It publishes stable live nodes
    /// and incremental changes; native accessibility packages are adapters over this API.
    /// </summary>
    public sealed class UniTextSemantics : IDisposable
    {
        private static readonly Func<UniTextBase, UniTextSemantics> create =
            static owner => new UniTextSemantics(owner);

        private sealed class NodeBuilder
        {
            public RangeIdentity identity;
            public RangeChannel channel;
            public object payload;
            public TextSemanticRole role;
            public string label;
            public string value;
            public string hint;
            public string language;
            public TextSemanticStates states;
            public TextSemanticActions actions;
            public TextRevision revision;
            public int priority;
            public int styleOrder;
            public bool deriveLabel;
            public bool hasBounds;
            public Rect bounds;
            public readonly List<RangeSegment> segments = new();

            public void Reset(RangeIdentity value)
            {
                identity = value;
                channel = null;
                payload = null;
                role = default;
                label = null;
                this.value = null;
                hint = null;
                language = null;
                states = default;
                actions = default;
                revision = default;
                priority = default;
                styleOrder = default;
                deriveLabel = false;
                hasBounds = false;
                bounds = default;
                segments.Clear();
            }
        }

        private readonly UniTextBase owner;
        private readonly object registrationGate = new();
        private readonly List<SemanticModifier> registrations = new();
        private readonly List<SemanticModifier> registrationSnapshot = new();
        private readonly Dictionary<RangeIdentity, NodeBuilder> builders = new();
        private readonly Stack<NodeBuilder> builderPool = new();
        private readonly Dictionary<RangeIdentity, TextSemanticNode> previousByIdentity = new();
        private readonly HashSet<RangeIdentity> currentIdentities = new();
        private readonly HashSet<RangeIdentity> updatedIdentities = new();
        private readonly List<TextSemanticNode> nodes = new();
        private readonly ReadOnlyCollection<TextSemanticNode> readOnlyNodes;
        private readonly List<TextSemanticNode> nextNodes = new();
        private readonly StringBuilder labelBuilder = new();
        private bool subscribed;
        private bool rebuildQueued;
        private bool disposed;
        private RangeIdentity focusedIdentity;

        /// <summary>Current stable live nodes in logical codepoint order.</summary>
        public IReadOnlyList<TextSemanticNode> Nodes => readOnlyNodes;

        /// <summary>Stable identity currently marked focused, or an invalid value.</summary>
        public RangeIdentity FocusedIdentity => focusedIdentity;

        /// <summary>Occurs once for every added, updated or removed node.</summary>
        public event Action<TextSemanticChange> Changed;

        /// <summary>Occurs after all incremental changes from one layout commit have been emitted.</summary>
        public event Action Committed;

        /// <summary>Occurs before optional built-in Activate or Context routing.</summary>
        public event TextSemanticActionHandler ActionRequested;

        internal UniTextSemantics(UniTextBase owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            readOnlyNodes = nodes.AsReadOnly();
            owner.Deinitializing += OnHostDeinitialized;
        }

        /// <summary>Returns the semantic tree attached to a text component.</summary>
        public static UniTextSemantics For(UniTextBase owner)
            => owner.GetOrCreateAttachment(create);

        /// <summary>Returns an existing semantic tree without creating one.</summary>
        public static bool TryGet(UniTextBase owner, out UniTextSemantics semantics)
        {
            if (owner != null) return owner.TryGetAttachment(out semantics);
            semantics = null;
            return false;
        }

        /// <summary>Finds one node by stable entity identity.</summary>
        public bool TryGet(RangeIdentity identity, out TextSemanticNode node)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].Identity != identity) continue;
                node = nodes[i];
                return true;
            }
            node = null;
            return false;
        }

        /// <summary>
        /// Requests one action declared by a node. Project handlers run first; Activate and Context
        /// then reuse the normal interaction router unless <see cref="TextSemanticActionRequest.PreventDefault"/>
        /// was called.
        /// </summary>
        public bool PerformAction(RangeIdentity identity, TextSemanticActions action)
        {
            var actionBits = (uint)action;
            if (actionBits == 0 || (actionBits & (actionBits - 1)) != 0)
                throw new ArgumentException("Exactly one semantic action must be requested.", nameof(action));
            if (!TryGet(identity, out var node) || (node.Actions & action) == 0) return false;

            var request = new TextSemanticActionRequest(node, action);
            ActionRequested?.Invoke(request);
            if (request.DefaultPrevented) return request.Handled;

            if (action != TextSemanticActions.Activate && action != TextSemanticActions.Context)
                return request.Handled;
            if (!UniTextInteractions.TryGet(owner, out var interactions) ||
                !interactions.Focus(identity)) return request.Handled;
            var performed = action == TextSemanticActions.Activate
                ? interactions.ActivateFocused()
                : interactions.ContextForFocused();
            return request.Handled || performed;
        }

        internal void Register(SemanticModifier modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            ThrowIfDisposed();
            var attach = false;
            lock (registrationGate)
            {
                if (registrations.Contains(modifier)) return;
                registrations.Add(modifier);
                attach = registrations.Count == 1;
            }
            rebuildQueued = true;
            if (attach) Attach();
        }

        internal void Unregister(SemanticModifier modifier)
        {
            if (modifier == null || disposed) return;
            lock (registrationGate)
            {
                if (!registrations.Remove(modifier)) return;
            }
            QueueRebuild();
        }

        internal void OnHostDeinitialized()
        {
            if (disposed) return;
            lock (registrationGate) registrations.Clear();
            RebuildNow();
        }

        internal void SetFocused(RangeIdentity identity)
        {
            if (focusedIdentity == identity) return;
            var previous = focusedIdentity;
            focusedIdentity = identity;
            UpdateFocusState(previous, false);
            UpdateFocusState(identity, true);
            Committed?.Invoke();
        }

        internal void RebuildNow()
        {
            if (disposed) return;
            rebuildQueued = false;
            SnapshotRegistrations();
            ReleaseBuilders();

            for (var m = 0; m < registrationSnapshot.Count; m++)
            {
                var modifier = registrationSnapshot[m];
                if (modifier == null || !modifier.IsInitialized) continue;
                var styleOrder = owner.AttributeParser?.GetModifierRegistrationOrder(modifier) ?? -1;
                var applications = modifier.Applications;
                for (var i = 0; i < applications.Length; i++)
                    Accumulate(in applications[i], styleOrder);
            }

            BuildNextNodes();
            PublishDiff();
            if (registrationSnapshot.Count == 0) Detach();
        }

        /// <summary>Releases geometry capture, subscriptions and tree state.</summary>
        public void Dispose()
        {
            if (disposed) return;
            owner.Deinitializing -= OnHostDeinitialized;
            disposed = true;
            Detach();
            for (var i = 0; i < nodes.Count; i++)
                Changed?.Invoke(new TextSemanticChange(TextSemanticChangeKind.Removed, nodes[i]));
            nodes.Clear();
            nextNodes.Clear();
            ReleaseBuilders();
            builderPool.Clear();
            previousByIdentity.Clear();
            currentIdentities.Clear();
            updatedIdentities.Clear();
            lock (registrationGate) registrations.Clear();
            registrationSnapshot.Clear();
            Committed?.Invoke();
        }

        private void Attach()
        {
            if (subscribed || disposed) return;
            subscribed = true;
            owner.Committed += OnCommitted;
            RangeGeometryIndex.For(owner).Retain();
        }

        private void Detach()
        {
            if (!subscribed) return;
            subscribed = false;
            owner.Committed -= OnCommitted;
            if (RangeGeometryIndex.TryGet(owner, out var geometry)) geometry.Release();
        }

        private void QueueRebuild()
        {
            rebuildQueued = true;
        }

        internal void MarkApplicationsDirty() => rebuildQueued = true;

        private void OnCommitted(UniTextCommitChanges changes)
        {
            if (!rebuildQueued && (changes & UniTextCommitChanges.GlyphGeometry) == 0) return;
            RebuildNow();
        }

        private void SnapshotRegistrations()
        {
            lock (registrationGate)
            {
                registrationSnapshot.Clear();
                registrationSnapshot.AddRange(registrations);
            }
        }

        private void Accumulate(in SemanticApplication application, int styleOrder)
        {
            if (!application.identity.IsValid)
                throw new InvalidOperationException("SemanticModifier requires a stable range identity.");
            if (!builders.TryGetValue(application.identity, out var builder))
            {
                builder = builderPool.Count > 0 ? builderPool.Pop() : new NodeBuilder();
                builder.Reset(application.identity);
                builders.Add(application.identity, builder);
            }

            var duplicateSegment = false;
            for (var i = 0; i < builder.segments.Count; i++)
            {
                if (builder.segments[i].Id != application.segment.Id) continue;
                duplicateSegment = true;
                break;
            }
            if (!duplicateSegment) builder.segments.Add(application.segment);

            var wins = application.priority > builder.priority ||
                       application.priority == builder.priority && styleOrder >= builder.styleOrder;
            if (builder.revision.IsValid && !wins)
            {
                AccumulateBounds(builder, in application.segment);
                return;
            }

            builder.channel = application.channel;
            builder.payload = application.payload;
            builder.role = application.role;
            builder.label = application.label;
            builder.value = application.value;
            builder.hint = application.hint;
            builder.language = application.language;
            builder.states = application.states;
            builder.actions = application.actions;
            builder.revision = application.revision;
            builder.priority = application.priority;
            builder.styleOrder = styleOrder;
            builder.deriveLabel = application.deriveLabel;
            AccumulateBounds(builder, in application.segment);
        }

        private void AccumulateBounds(NodeBuilder builder, in RangeSegment segment)
        {
            var fragments = RangeGeometryIndex.For(owner).GetLineFragments(segment.Range.start,
                segment.Range.End, RangeHeight.LineBox);
            for (var i = 0; i < fragments.Length; i++)
            {
                var bounds = fragments[i].Bounds;
                builder.bounds = builder.hasBounds ? Union(builder.bounds, bounds) : bounds;
                builder.hasBounds = true;
            }
        }

        private void BuildNextNodes()
        {
            nextNodes.Clear();
            updatedIdentities.Clear();
            var orderMayChange = false;
            foreach (var pair in builders)
            {
                var builder = pair.Value;
                if (builder.segments.Count > 1) builder.segments.Sort(CompareSegments);
                var states = builder.states;
                if (builder.identity == focusedIdentity) states |= TextSemanticStates.Focused;
                else states &= ~TextSemanticStates.Focused;
                previousByIdentity.TryGetValue(builder.identity, out var previous);
                var label = builder.deriveLabel
                    ? BuildCoveredText(builder.segments, previous?.Label)
                    : builder.label;
                if (previous != null)
                {
                    if (previous.Update(builder.channel, builder.payload, builder.segments,
                            builder.role, label, builder.value, builder.hint, builder.language,
                            states, builder.actions, builder.bounds, builder.revision,
                            out var topologyChanged))
                        updatedIdentities.Add(builder.identity);
                    orderMayChange |= topologyChanged;
                    nextNodes.Add(previous);
                    continue;
                }
                orderMayChange = true;
                nextNodes.Add(new TextSemanticNode(builder.identity, builder.channel, builder.payload,
                    builder.segments.ToArray(), builder.role, label, builder.value, builder.hint,
                    builder.language, states, builder.actions, builder.bounds, builder.revision));
            }
            if (orderMayChange || !HasPreviousOrder()) nextNodes.Sort(CompareNodes);
        }

        private bool HasPreviousOrder()
        {
            if (nextNodes.Count != nodes.Count) return false;
            for (var i = 0; i < nodes.Count; i++)
                if (nextNodes[i].Identity != nodes[i].Identity) return false;
            return true;
        }

        private void PublishDiff()
        {
            currentIdentities.Clear();
            for (var i = 0; i < nextNodes.Count; i++)
            {
                var candidate = nextNodes[i];
                currentIdentities.Add(candidate.Identity);
                if (!previousByIdentity.TryGetValue(candidate.Identity, out var previous))
                {
                    Changed?.Invoke(new TextSemanticChange(TextSemanticChangeKind.Added, candidate));
                    previousByIdentity.Add(candidate.Identity, candidate);
                    continue;
                }
                if (updatedIdentities.Contains(candidate.Identity))
                    Changed?.Invoke(new TextSemanticChange(TextSemanticChangeKind.Updated, candidate));
            }

            for (var i = 0; i < nodes.Count; i++)
            {
                if (!currentIdentities.Contains(nodes[i].Identity))
                {
                    Changed?.Invoke(new TextSemanticChange(TextSemanticChangeKind.Removed, nodes[i]));
                    previousByIdentity.Remove(nodes[i].Identity);
                }
            }

            nodes.Clear();
            nodes.AddRange(nextNodes);
            Committed?.Invoke();
        }

        private void UpdateFocusState(RangeIdentity identity, bool focused)
        {
            if (!identity.IsValid) return;
            for (var i = 0; i < nodes.Count; i++)
            {
                var current = nodes[i];
                if (current.Identity != identity) continue;
                var states = focused
                    ? current.States | TextSemanticStates.Focused
                    : current.States & ~TextSemanticStates.Focused;
                if (!current.SetStates(states)) return;
                Changed?.Invoke(new TextSemanticChange(TextSemanticChangeKind.Updated, current));
                return;
            }
        }

        private void ReleaseBuilders()
        {
            foreach (var pair in builders)
            {
                pair.Value.Reset(default);
                builderPool.Push(pair.Value);
            }
            builders.Clear();
        }

        private string BuildCoveredText(List<RangeSegment> segments, string previous)
        {
            labelBuilder.Clear();
            var codepoints = owner.Buffers.codepoints;
            for (var s = 0; s < segments.Count; s++)
            {
                if (s > 0 && labelBuilder.Length > 0) labelBuilder.Append(' ');
                var range = segments[s].Range;
                for (var i = range.start; i < range.End && i < codepoints.count; i++)
                    AppendCodepoint(labelBuilder, codepoints.data[i]);
            }
            if (previous != null && StringBuilderEquals(labelBuilder, previous)) return previous;
            return labelBuilder.ToString();
        }

        private static bool StringBuilderEquals(StringBuilder builder, string value)
        {
            if (builder.Length != value.Length) return false;
            for (var i = 0; i < builder.Length; i++)
                if (builder[i] != value[i]) return false;
            return true;
        }

        private static void AppendCodepoint(StringBuilder builder, int codepoint)
        {
            if ((uint)codepoint <= 0xFFFF && (codepoint < 0xD800 || codepoint > 0xDFFF))
            {
                builder.Append((char)codepoint);
                return;
            }
            if ((uint)(codepoint - 0x10000) <= 0xFFFFF)
            {
                var value = codepoint - 0x10000;
                builder.Append((char)(0xD800 + (value >> 10)));
                builder.Append((char)(0xDC00 + (value & 0x3FF)));
                return;
            }
            builder.Append('\uFFFD');
        }

        private static int CompareSegments(RangeSegment a, RangeSegment b)
        {
            var value = a.Range.start.CompareTo(b.Range.start);
            return value != 0 ? value : a.Id.Value.CompareTo(b.Id.Value);
        }

        private static int CompareNodes(TextSemanticNode a, TextSemanticNode b)
        {
            var aStart = a.Segments.Length > 0 ? a.Segments[0].Range.start : int.MaxValue;
            var bStart = b.Segments.Length > 0 ? b.Segments[0].Range.start : int.MaxValue;
            return aStart != bStart ? aStart.CompareTo(bStart) : a.Identity.Range.Value.CompareTo(b.Identity.Range.Value);
        }

        private static Rect Union(Rect a, Rect b)
            => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(UniTextSemantics));
        }
    }
}
