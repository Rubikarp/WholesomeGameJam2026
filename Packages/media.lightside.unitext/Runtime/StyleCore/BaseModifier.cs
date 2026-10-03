using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base class for logic that alters the appearance, layout, semantics, or behavior of ranges
    /// emitted by a <see cref="RangeSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subclass and override the protected hooks (<see cref="OnEnable"/>, <see cref="OnDisable"/>,
    /// <see cref="OnDestroy"/>, <see cref="OnApply"/>, optionally
    /// <see cref="PrepareForParallel"/>). Lifecycle dispatching is driven by the framework
    /// (<see cref="Style"/>, the parse pipeline, <see cref="CompositeModifier"/>) — user code
    /// instantiates modifiers and hands them to <see cref="Style"/>; it should not invoke the
    /// lifecycle itself.
    /// </para>
    /// <para>
    /// Cycle (per parse pass on a UniText component):
    /// <list type="number">
    /// <item><see cref="OnEnable"/> runs once when the modifier first participates in a cycle —
    /// allocate pooled buffers and subscribe to UniText events here.</item>
    /// <item><see cref="OnApply"/> runs for every tag/range matched in the current text.</item>
    /// <item><see cref="OnDisable"/> runs at the start of the next cycle to unsubscribe event
    /// handlers; allocated buffers should stay alive so the next <see cref="OnEnable"/> can reuse
    /// them.</item>
    /// <item><see cref="OnDestroy"/> runs when the modifier is removed from the component
    /// (style changed, font reload, component destroyed) — release pooled buffers and any other
    /// resources <see cref="OnEnable"/> acquired. Guaranteed to run exactly once per
    /// <see cref="OnEnable"/>-initiated resource acquisition, even when an intervening
    /// <see cref="OnDisable"/> has already cleared the subscription state.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Granular reapplication re-runs <see cref="OnApply"/> over retained spans without a re-parse,
    /// once per span — so accumulators <see cref="OnApply"/> appends to must be
    /// cleared once per cycle, never at the start of <see cref="OnApply"/> itself. Override
    /// <see cref="BeforeApply"/> for that: the framework compares the host's apply-cycle stamp on
    /// each dispatch and calls it lazily before the modifier's first range of a new cycle (full
    /// parse and granular re-apply alike). There is no separate reset phase.
    /// </para>
    /// </remarks>
    /// <seealso cref="GlyphModifier{T}"/>
    /// <seealso cref="InteractiveModifier"/>
    /// <seealso cref="Style"/>
    [Serializable]
    [StateHierarchy]
    [TypeMenuSuffix("Modifier")]
    public abstract partial class BaseModifier
    {
        [SerializeField, HideInInspector, StatePassive]
        private ModifierNodeId modifierNodeId = ModifierNodeId.Create();

        /// <summary>
        /// The UniText component this modifier is bound to. Assigned by the framework via
        /// <c>SetOwner</c>; available to subclasses from <see cref="OnEnable"/> onward.
        /// </summary>
        [NonSerialized] protected UniTextBase uniText;

        /// <summary>
        /// Shared text-processing buffers (codepoints, runs, shaped glyphs, attribute storage).
        /// Assigned by the framework alongside <see cref="uniText"/>.
        /// </summary>
        [NonSerialized] protected UniTextBuffers buffers;

        [NonSerialized] private IModifierChangeSink changeSink;
        [NonSerialized] private BaseModifier attachmentParent;

        [NonSerialized] private bool isInitialized;
        [NonSerialized] private bool hasResources;
        [NonSerialized] private int lastAppliedCycle;

        /// <summary>
        /// <see langword="true"/> between <see cref="OnEnable"/> and the next
        /// <see cref="OnDisable"/>/<see cref="OnDestroy"/>: event handlers are currently
        /// subscribed. Useful for diagnostics and for nested composites that mirror the
        /// child lifecycle.
        /// </summary>
        public bool IsInitialized => isInitialized;

        internal ModifierNodeId NodeId => modifierNodeId.IsValid
            ? modifierNodeId
            : modifierNodeId = ModifierNodeId.Create();

        internal UniTextBase Owner => uniText;

        internal IModifierChangeSink ChangeSink => changeSink;

        internal BaseModifier AttachmentParent => attachmentParent;

        /// <summary>
        /// The shared owner whose per-cycle state this modifier feeds, or null when it owns everything
        /// it writes. Granular re-apply replays every modifier reporting the same owner together, so a
        /// shared collection is never rebuilt from part of its writers.
        /// </summary>
        internal virtual object ChannelIdentity => null;

        internal void SetAttachmentParent(BaseModifier parent)
        {
            if (ReferenceEquals(attachmentParent, parent)) return;
            attachmentParent = parent;
        }

        internal void Prepare()
        {
            if (isInitialized) return;
            if (uniText == null)
                throw new InvalidOperationException(
                    $"{GetType().FullName} cannot be prepared without a UniText owner.");
            if (attachmentParent == null) ValidateGraph(this);
            isInitialized = true;
            hasResources = true;
            try
            {
                OnEnable();
            }
            catch
            {
                try
                {
                    Destroy();
                }
                catch (Exception cleanupException)
                {
                    UnityEngine.Debug.LogException(cleanupException);
                }
                throw;
            }
        }

        /// <summary>
        /// Non-virtual dispatch entry — parser spans and <see cref="CompositeModifier"/> children both
        /// route through here. On the first range of a new <see cref="AttributeParser.ApplyCycle"/> it
        /// calls <see cref="BeforeApply"/>, then stamps the cycle; later ranges of the same cycle go
        /// straight to <see cref="OnApply"/>. Runs on the rebuild worker; the stamp is per-instance
        /// state touched by one host's cycle on one thread, so no synchronization is needed.
        /// </summary>
        internal void Apply(in RangeApplyContext context)
            => Apply(in context, uniText.AttributeParser.ApplyCycle);

        internal void Apply(in RangeApplyContext context, int cycle)
        {
            BeginApply(cycle);
            OnApply(in context);
        }

        internal void BeginApply(int cycle)
        {
            if (lastAppliedCycle == cycle) return;
            lastAppliedCycle = cycle;
            BeforeApply();
            if (Children is not { } children) return;
            for (var i = 0; i < children.Count; i++) children[i]?.BeginApply(cycle);
        }

        protected int CurrentApplyCycle => lastAppliedCycle;

        /// <summary>Aggregated child modifiers, or null for a leaf — walkers traverse through this instead of naming aggregate types.</summary>
        internal virtual IReadOnlyList<BaseModifier> Children => null;

        /// <summary>
        /// The parameter descriptors this modifier declares. Override with the type's aggregated
        /// <c>Param.All</c> set; the default declares none.
        /// </summary>
        protected internal virtual ParameterDescriptor[] Descriptors => ParameterDescriptor.None;

        /// <summary>
        /// Collects this modifier's materialized ranges from the current parse, in text order.
        /// </summary>
        /// <exception cref="InvalidOperationException">The modifier is not attached to a text
        /// component.</exception>
        public void GetRanges(List<ModifierRange> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            var parser = uniText != null ? uniText.AttributeParser : null;
            if (parser == null)
                throw new InvalidOperationException(
                    $"{GetType().Name} is not attached to a text component.");
            parser.CollectRangesFor(this, results);
        }

        /// <summary>
        /// Starts a query over this modifier's materialized ranges; chain filters, then
        /// materialize with <see cref="RangeQuery.Collect"/> or take standing ownership with
        /// <see cref="RangeQuery.Own{TModifier,TValue}"/>.
        /// </summary>
        public RangeQuery Ranges() => RangeQuery.For(this);

        /// <summary>
        /// Takes standing ownership of one parameter across every range of this modifier, now and
        /// after every future parse.
        /// </summary>
        /// <exception cref="InvalidOperationException">The modifier is not attached to a text
        /// component.</exception>
        public OwnedParameterSet<TModifier, TValue> Own<TModifier, TValue>(
            ParameterDescriptor<TModifier, TValue> parameter,
            ParameterComposition composition = ParameterComposition.Replace, int priority = 0)
            where TModifier : BaseModifier
            => RangeQuery.For(this).Own(parameter, composition, priority);

        internal virtual IReadOnlyList<BaseModifier> OwnedChildren => Children;

        /// <summary>
        /// Whether <paramref name="other"/> shares this modifier's style identity — the same concrete type by
        /// default; <see cref="CompositeModifier"/> overrides it to compare ordered child types, so {Bold, Italic}
        /// and {Italic, Bold} are distinct. Resolves which configured style a command targets.
        /// </summary>
        internal virtual bool SignatureMatches(BaseModifier other) => other != null && other.GetType() == GetType();

        /// <summary>
        /// Serialisable form of <see cref="SignatureMatches"/> — equal strings ⟺ matching signatures. The
        /// structured clipboard carries this to resolve the destination field's modifier by identity.
        /// </summary>
        internal virtual string Signature => GetType().FullName;

        internal virtual void SetOwner(UniTextBase owner, IModifierChangeSink sink = null)
        {
            var resolvedSink = sink ?? owner?.AttributeParser;
            if (owner != null && attachmentParent == null)
                ValidateGraph(this);
            var resolvedBuffers = owner?.Buffers;
            _ = NodeId;
            ReconcileStateLinks(resolvedSink != null && owner is null);
            if (!ReferenceEquals(uniText, owner))
                lastAppliedCycle = 0;
            uniText = owner;
            buffers = resolvedBuffers;
            changeSink = resolvedSink;
        }

        internal virtual void SetChangeSink(IModifierChangeSink sink)
        {
            if (sink != null && attachmentParent == null)
                ValidateGraph(this);
            SetChangeSinkAfterValidation(sink);
        }

        /// <summary>Assigns a sink after the caller has validated graph topology.</summary>
        private protected void SetChangeSinkAfterValidation(IModifierChangeSink sink)
        {
            _ = NodeId;
            ReconcileStateLinks(sink != null && uniText is null);
            changeSink = sink;
        }

        private void ReconcileStateLinks(bool shouldRetain)
        {
            var retained = changeSink != null && uniText is null;
            if (retained == shouldRetain || this is not IStateLinkGraph links) return;
            if (shouldRetain) links.RetainStateLinks();
            else links.ReleaseStateLinks();
        }

        private protected void ValidateRangeChannel(RangeChannel channel)
            => ModifierGraphValidator.ThrowIfInvalid(channel);

        internal static void ValidateGraph(BaseModifier root)
            => ModifierGraphValidator.ThrowIfInvalid(root, validateConfiguration: false);

        internal static void ValidateGraphs(in StateListMutation<BaseModifier> roots)
            => ModifierGraphValidator.ThrowIfInvalid(roots, validateConfiguration: false);

        internal static void CollectGraphNodes(BaseModifier root, List<BaseModifier> result,
            bool ownedOnly = false)
            => ModifierGraphValidator.CollectNodes(root, result, ownedOnly);

        internal static void RegenerateGraphIdentities(BaseModifier root)
        {
            if (root == null) return;
            var nodes = new List<BaseModifier>();
            CollectGraphNodes(root, nodes, ownedOnly: true);
            var identities = new Dictionary<ModifierNodeId, ModifierNodeId>();
            for (var i = 0; i < nodes.Count; i++)
            {
                var previous = nodes[i].NodeId;
                var current = ModifierNodeId.Create();
                identities.Add(previous, current);
                nodes[i].modifierNodeId = current;
            }
            for (var i = 0; i < nodes.Count; i++)
                if (nodes[i] is InteractiveModifier interactive)
                    interactive.RemapRuleTargets(identities);
        }

        internal static BaseModifier FindGraphNode(BaseModifier root, ModifierNodeId nodeId)
        {
            if (root == null) return null;
            if (root.NodeId == nodeId) return root;
            if (root.Children is not { } children) return null;
            for (var i = 0; i < children.Count; i++)
                if (FindGraphNode(children[i], nodeId) is { } match) return match;
            return null;
        }

        internal static bool TryReplayGraphState(BaseModifier root,
            BaseModifier authoredModifier, IStateMemberReplay source, StateMember member)
        {
            var target = FindGraphNode(root, authoredModifier.NodeId);
            if (target is IStateMemberReplay targetState &&
                targetState.ReplayStateMember(source, member)) return true;
            if (source is IStateChangeSource stateSource &&
                authoredModifier is IStateLinkGraph sourceGraph &&
                target is IStateLinkGraph targetGraph &&
                StateGraphReplay.TryReplay(targetGraph, sourceGraph,
                    stateSource, member)) return true;
            return target?.ReplayNestedStateMember(authoredModifier, source, member) == true;
        }

        internal virtual bool ReplayNestedStateMember(BaseModifier authoredModifier,
            IStateMemberReplay source, StateMember member) => false;

        protected void MarkMeshDirty(StateMember member = default)
            => changeSink?.MarkModifierChanged(this, UniTextDirty.Mesh,
                this as IStateMemberReplay, member);

        protected void MarkPositionsDirty(StateMember member = default)
            => changeSink?.MarkModifierChanged(this, UniTextDirty.Positions,
                this as IStateMemberReplay, member);

        protected void MarkLayoutDirty(StateMember member = default)
            => changeSink?.MarkModifierChanged(this, UniTextDirty.Layout,
                this as IStateMemberReplay, member);

        protected void MarkTextDirty(StateMember member = default)
            => changeSink?.MarkModifierChanged(this, UniTextDirty.Text,
                this as IStateMemberReplay, member);

        /// <summary>
        /// Marks the mesh stage dirty without queueing a range re-apply — for members whose applied
        /// values the modifier re-resolves before each rebuild. Does nothing when
        /// <paramref name="hasRanges"/> is <see langword="false"/>; an unowned instance raises the
        /// structural mesh notification instead, so authoring-time edits still reach consumers.
        /// </summary>
        protected void MarkRenderDirty(bool hasRanges)
        {
            if (uniText == null) MarkMeshDirty();
            else if (hasRanges) uniText.SetDirty(UniTextDirty.Mesh);
        }

        /// <summary>Forwards an exact transition owned by nested reusable state.</summary>
        protected void MarkNestedStateChanged(UniTextDirty flags,
            IStateChangeSource source, in StateChange change)
            => changeSink?.MarkModifierChanged(this, flags,
                source as IStateMemberReplay, change.Member);

        protected void MarkStructureChanged()
            => changeSink?.MarkModifierChanged(this, UniTextDirty.Text,
                this as IStateMemberReplay, structural: true);

        internal void Destroy()
        {
            try
            {
                if (isInitialized) OnDisable();
            }
            finally
            {
                try
                {
                    if (hasResources) OnDestroy();
                }
                finally
                {
                    isInitialized = false;
                    hasResources = false;
                }
            }
        }

        internal void Disable()
        {
            if (!isInitialized) return;
            try
            {
                OnDisable();
            }
            finally
            {
                isInitialized = false;
            }
        }

        /// <summary>
        /// Override to cache main-thread-only values before parallel processing. Runs on the
        /// main thread immediately before each rebuild that may dispatch work to worker threads.
        /// </summary>
        /// <remarks>
        /// Use this hook when <see cref="OnApply"/> needs values from Unity APIs that cannot be
        /// touched off the main thread (<c>Material.GetFloat</c>, transform reads, asset
        /// lookups). Read those values here and store them in instance fields; the worker pass
        /// consumes the cached snapshot.
        /// </remarks>
        protected internal virtual void PrepareForParallel() { }

        /// <summary>
        /// Runs once per apply cycle (full parse and granular re-apply alike), lazily before this
        /// modifier's first <see cref="OnApply"/> of the cycle. Bring accumulated state back to a
        /// valid start: clear buffers <see cref="OnApply"/> appends to (<c>ranges?.FakeClear()</c>),
        /// release externally held resources the re-run re-acquires (gradient ramp rows), restore
        /// derived flags. A granular re-apply replays the cycle inside the mesh phase without
        /// re-running the parse, shaping, or — while positions stay valid — layout, so state whose
        /// only producer is one of those phases must never be cleared or overwritten here and
        /// belongs in a container this method does not touch. Runs on the rebuild worker — keep it
        /// idempotent and free of Unity API calls. Overwrite-style attribute buffers need nothing
        /// here.
        /// </summary>
        protected virtual void BeforeApply() { }

        /// <summary>
        /// Allocate buffers, subscribe to UniText events, and bring the modifier into the active
        /// state. Pairs one-to-one with <see cref="OnDisable"/> and <see cref="OnDestroy"/>.
        /// </summary>
        protected virtual void OnEnable() { }

        /// <summary>
        /// Unsubscribe from UniText events. Buffers and persistent state should remain allocated
        /// — <see cref="OnDestroy"/> is responsible for releasing them. Runs once per active
        /// cycle (at the start of the next parse pass).
        /// </summary>
        protected virtual void OnDisable() { }

        /// <summary>
        /// Release resources allocated by <see cref="OnEnable"/> (pooled buffers, attribute data,
        /// registry subscriptions). Always runs after <see cref="OnEnable"/> ran at least once,
        /// even when the most recent cycle already drained subscriptions via
        /// <see cref="OnDisable"/>.
        /// </summary>
        protected virtual void OnDestroy() { }

        /// <summary>
        /// Applies this modifier to one entity segment. Override this hook to access identity,
        /// payload, source, revision and layered parameters without materializing a merged value.
        /// </summary>
        /// <param name="context">Synchronous immutable application context.</param>
        protected virtual void OnApply(in RangeApplyContext context) { }
    }
}
