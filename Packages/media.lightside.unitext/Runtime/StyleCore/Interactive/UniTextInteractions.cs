using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    internal readonly struct RangeGestureArbitration
    {
        public readonly bool claimed;
        public readonly RangeGestureCompatibility compatibility;

        public RangeGestureArbitration(bool claimed, RangeGestureCompatibility compatibility)
        {
            this.claimed = claimed;
            this.compatibility = compatibility;
        }
    }

    /// <summary>
    /// Per-component router for interactive text entities. It owns host input subscriptions,
    /// final-geometry hit testing, overlap arbitration, per-pointer leases and channel routing.
    /// </summary>
    public sealed class UniTextInteractions : IDisposable
    {
        private static readonly Func<UniTextBase, UniTextInteractions> create =
            static owner => new UniTextInteractions(owner);

        private readonly struct TargetKey : IEquatable<TargetKey>
        {
            private readonly InteractiveModifier owner;
            private readonly RangeIdentity identity;
            private readonly RangeSegmentId segment;

            public TargetKey(InteractiveModifier owner, in InteractiveRange range)
            {
                this.owner = owner;
                identity = range.Identity;
                segment = range.Segment.Id;
            }

            public bool Equals(TargetKey other)
            {
                if (!ReferenceEquals(owner, other.owner)) return false;
                return identity == other.identity && segment == other.segment;
            }

            public override bool Equals(object obj) => obj is TargetKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(owner),
                identity, segment);
        }

        private struct InteractionTarget
        {
            public InteractiveModifier owner;
            public InteractiveRange range;
            public TargetKey key;
            public int priority;
            public int styleOrder;
            public int registrationOrder;
            public RangeInteractionScope activationScope;
            public UnitVector2 hitPadding;
            public UnitVector2 minimumHitSize;
            public WrappedGapPolicy gapPolicy;
            public RangeWhitespacePolicy whitespacePolicy;
            public InlineObjectPolicy inlineObjectPolicy;
            public bool passThrough;
        }

        private struct InteractionFragment
        {
            public InteractionTarget target;
            public Rect bounds;
            public int clusterStart;
        }

        private struct RawFragment
        {
            public Rect bounds;
            public int lineIndex;
            public int clusterStart;
        }

        private sealed class PointerSession
        {
            public int pointerId;
            public PointerKind pointerKind;
            public Vector2 screenPosition;
            public Vector2 localPosition;
            public Camera eventCamera;
            public PointerModifiers modifiers;
            public bool hasHover;
            public InteractionTarget hover;
            public InteractionFragment hoverFragment;
            public bool hasPressed;
            public InteractionTarget pressed;
            public InteractionFragment pressedFragment;
            public bool hasActivationOrigin;
            public InteractionTarget activationOrigin;
            public Vector2 downScreenPosition;
            public Vector2 lastGestureScreenPosition;
            public RangeGestureRecognizer gestureRecognizer;
            public RangeGestureCompatibility gestureCompatibility;
        }

        private readonly UniTextBase owner;
        private readonly object registrationGate = new();
        private readonly List<InteractiveModifier> registrations = new();
        private readonly List<InteractiveModifier> registrationSnapshot = new();
        private readonly List<InteractiveModifier> retiredRegistrations = new();
        private readonly Dictionary<InteractiveModifier, int> registrationOrders = new();
        private readonly Dictionary<RangeChannel, RangeInteractionChannel> channels = new();
        private readonly Dictionary<int, PointerSession> pointers = new();
        private readonly List<InteractionTarget> targets = new();
        private readonly List<InteractionTarget> focusTargets = new();
        private readonly List<InteractionFragment> fragments = new();
        private readonly List<RawFragment> rawFragments = new();
        private readonly List<float> fragmentPrefixMaxY = new();
        private readonly Dictionary<RangeIdentity, Rect> entityBounds = new();
        private readonly Stack<RangeInteraction> eventPool = new();
        private readonly TextPointerEvent dragEventScratch = new();
        private int nextRegistrationOrder;
        private bool subscribed;
        private bool rebuildQueued;
        private bool disposed;
        private bool hasFocusedTarget;
        private InteractionTarget focusedTarget;
        private InteractionFragment focusedFragment;

        /// <summary>Runs before a target channel and may stop later routing through <see cref="RangeInteraction.Handled"/>.</summary>
        public event RangeInteractionHandler Capturing;

        /// <summary>Runs after the target channel and modifier unless an earlier handler stopped routing.</summary>
        public event RangeInteractionHandler Bubbling;

        /// <summary>Whether at least one enabled interactive entity currently has hit geometry.</summary>
        public bool HasTargets => targets.Count > 0;

        /// <summary>Stable identity of the currently focused entity, or an invalid value.</summary>
        public RangeIdentity FocusedEntity => hasFocusedTarget ? focusedTarget.range.Identity : default;

        /// <summary>Number of enabled logical entities in navigation order.</summary>
        public int EntityCount => focusTargets.Count;

        /// <summary>
        /// Asks the viewport owner to reveal an entity. The router publishes geometry but does not
        /// assume ScrollRect, world-camera or custom document scrolling ownership.
        /// </summary>
        public event Action<RangeScrollRequest> ScrollIntoViewRequested;

        internal UniTextInteractions(UniTextBase owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            owner.Deinitializing += OnHostDeinitialized;
        }

        /// <summary>Returns the interaction router attached to a text component.</summary>
        public static UniTextInteractions For(UniTextBase owner)
            => owner.GetOrCreateAttachment(create);

        /// <summary>Returns an existing router without creating one.</summary>
        public static bool TryGet(UniTextBase owner, out UniTextInteractions interactions)
        {
            if (owner != null) return owner.TryGetAttachment(out interactions);
            interactions = null;
            return false;
        }

        /// <summary>Returns the stable subscription surface for one semantic range channel.</summary>
        public RangeInteractionChannel Get(RangeChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (channels.TryGetValue(channel, out var existing)) return existing;
            var created = new RangeInteractionChannel(channel);
            channels.Add(channel, created);
            return created;
        }

        /// <summary>Returns a channel after validating its serialized payload contract against <typeparamref name="T"/>.</summary>
        public RangeInteractionChannel Get<T>(RangeChannel<T> channel) => Get(channel.Untyped);

        /// <summary>Returns one representative range in logical navigation order.</summary>
        public InteractiveRange GetEntity(int index)
        {
            if ((uint)index >= (uint)focusTargets.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return focusTargets[index].range;
        }

        internal void AppendDiagnostics(List<RangeEntityDiagnostic> result)
        {
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var target = focusTargets[i];
                var segments = new List<RangeSegment>(2);
                for (var j = 0; j < targets.Count; j++)
                {
                    var candidate = targets[j];
                    if (!SameEntity(in target, in candidate)) continue;
                    var duplicate = false;
                    for (var k = 0; k < segments.Count; k++)
                        if (segments[k].Id == candidate.range.Segment.Id) { duplicate = true; break; }
                    if (!duplicate) segments.Add(candidate.range.Segment);
                }
                var fragmentList = new List<Rect>(2);
                for (var j = 0; j < fragments.Count; j++)
                {
                    var fragment = fragments[j];
                    if (SameEntity(in target, in fragment.target))
                        fragmentList.Add(fragment.bounds);
                }
                result.Add(new RangeEntityDiagnostic(in target.range, segments.ToArray(),
                    fragmentList.ToArray(), ResolveAnchor(in target), target.owner,
                    GetState(target.owner, in target.range),
                    hasFocusedTarget && SameEntity(in focusedTarget, in target), target.priority,
                    target.styleOrder, target.registrationOrder, target.passThrough));
            }
        }

        /// <summary>Returns the union of final hit geometry for one stable entity.</summary>
        public bool TryGetBounds(RangeIdentity identity, out Rect bounds)
        {
            if (!identity.IsValid)
                throw new ArgumentException("Range identity is invalid.", nameof(identity));
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var target = focusTargets[i];
                if (target.range.Identity != identity) continue;
                bounds = ResolveAnchor(in target);
                return true;
            }
            bounds = default;
            return false;
        }

        /// <summary>Publishes a viewport-neutral reveal request for one stable entity.</summary>
        public bool ScrollIntoView(RangeIdentity identity)
        {
            if (!identity.IsValid)
                throw new ArgumentException("Range identity is invalid.", nameof(identity));
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var target = focusTargets[i];
                if (target.range.Identity != identity) continue;
                var handler = ScrollIntoViewRequested;
                if (handler == null) return false;
                handler.Invoke(new RangeScrollRequest(owner, in target.range,
                    ResolveAnchor(in target)));
                return true;
            }
            return false;
        }

        /// <summary>Focuses one entity and selects the owning UniText GameObject in the EventSystem.</summary>
        public bool Focus(RangeIdentity identity)
        {
            if (!identity.IsValid) throw new ArgumentException("Range identity is invalid.", nameof(identity));
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var target = focusTargets[i];
                if (target.range.Identity != identity) continue;
                SetFocusedTarget(in target);
                SelectHost();
                RequestScroll(in target);
                return true;
            }
            return false;
        }

        /// <summary>Focuses the first enabled logical entity.</summary>
        public bool FocusFirst()
        {
            if (focusTargets.Count == 0) return false;
            var target = focusTargets[0];
            SetFocusedTarget(in target);
            SelectHost();
            RequestScroll(in target);
            return true;
        }

        /// <summary>Moves focus by one logical entity; returns false at the outer boundary.</summary>
        public bool MoveFocus(bool previous)
        {
            if (focusTargets.Count == 0) return false;
            var index = hasFocusedTarget ? IndexOfFocusedTarget() : (previous ? focusTargets.Count : -1);
            var next = index + (previous ? -1 : 1);
            if ((uint)next >= (uint)focusTargets.Count) return false;
            var target = focusTargets[next];
            SetFocusedTarget(in target);
            RequestScroll(in target);
            return true;
        }

        /// <summary>
        /// Moves focus in logical or final-geometry order, optionally constrained to one channel.
        /// Home and End select a boundary without requiring an existing focus origin.
        /// </summary>
        public bool MoveFocus(RangeNavigationDirection direction,
            RangeNavigationOrder order = RangeNavigationOrder.Logical,
            RangeChannel channel = null, bool wrap = false)
            => MoveFocus(direction, order, channel, channel != null, wrap);

        private bool MoveFocus(RangeNavigationDirection direction,
            RangeNavigationOrder order, RangeChannel channel, bool restrictChannel, bool wrap)
        {
            if (focusTargets.Count == 0) return false;
            var visualDirection = direction is RangeNavigationDirection.Left or
                RangeNavigationDirection.Right or RangeNavigationDirection.Up or
                RangeNavigationDirection.Down;
            var next = order == RangeNavigationOrder.Visual && visualDirection
                ? FindVisualTarget(direction, channel, restrictChannel, wrap)
                : FindLogicalTarget(direction, channel, restrictChannel, wrap);
            if (next < 0) return false;
            var target = focusTargets[next];
            SetFocusedTarget(in target);
            RequestScroll(in target);
            return true;
        }

        /// <summary>Clears the internal entity focus without changing EventSystem selection.</summary>
        public void Blur() => ClearFocusedTarget();

        /// <summary>Activates the focused entity through the same routed event and default action as a pointer.</summary>
        public bool ActivateFocused()
            => DispatchFocused(RangeInteractionKind.Activated);

        /// <summary>Requests contextual actions for the focused entity.</summary>
        public bool ContextForFocused()
            => DispatchFocused(RangeInteractionKind.ContextRequested);

        /// <summary>
        /// Cancels one platform pointer lifecycle, releasing Pressed/Hovered leases and emitting
        /// Canceled instead of translating capture loss into a pointer exit.
        /// </summary>
        public bool CancelPointer(int pointerId)
        {
            if (!pointers.TryGetValue(pointerId, out var pointer)) return false;
            var evt = PointerEvent(pointer, PointerTrigger.Hover);
            if (pointer.hasPressed) CancelPress(pointer, evt, pointer.localPosition);
            if (pointer.hasHover)
                SetHover(pointer, false, default, evt, pointer.localPosition);
            pointers.Remove(pointerId);
            return true;
        }

        internal void Register(InteractiveModifier modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            ThrowIfDisposed();
            var attach = false;
            lock (registrationGate)
            {
                if (registrations.Contains(modifier)) return;
                registrations.Add(modifier);
                if (!registrationOrders.ContainsKey(modifier))
                    registrationOrders.Add(modifier, nextRegistrationOrder++);
                attach = registrations.Count == 1;
            }
            rebuildQueued = true;
            if (attach) Attach();
        }

        internal void Unregister(InteractiveModifier modifier)
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
            CancelAllPointers();
            ClearFocusedTarget();
            lock (registrationGate) registrations.Clear();
            RebuildNow();
        }

        internal RangeState GetState(InteractiveModifier modifier, in InteractiveRange range)
        {
            if (!modifier.RouterIsEnabled(in range)) return RangeState.Disabled;
            var key = new TargetKey(modifier, in range);
            return ResolvePointerState(in key);
        }

        internal void RebuildNow()
        {
            if (disposed) return;
            rebuildQueued = false;
            SnapshotRegistrations();
            targets.Clear();
            focusTargets.Clear();
            fragments.Clear();
            fragmentPrefixMaxY.Clear();
            entityBounds.Clear();

            for (var m = 0; m < registrationSnapshot.Count; m++)
            {
                var modifier = registrationSnapshot[m];
                if (modifier == null || !modifier.IsInitialized) continue;
                var ranges = modifier.InteractiveRanges;
                var styleOrder = owner.AttributeParser?.GetModifierRegistrationOrder(modifier) ?? -1;
                var registrationOrder = registrationOrders[modifier];
                for (var i = 0; i < ranges.Length; i++)
                {
                    ref readonly var range = ref ranges[i];
                    if (!range.IsValid || !modifier.RouterIsEnabled(in range)) continue;
                    var target = new InteractionTarget
                    {
                        owner = modifier,
                        range = range,
                        key = new TargetKey(modifier, in range),
                        priority = range.settings.priority,
                        styleOrder = styleOrder,
                        registrationOrder = registrationOrder,
                        activationScope = range.settings.activationScope,
                        hitPadding = range.settings.hitPadding,
                        minimumHitSize = range.settings.minimumHitSize,
                        gapPolicy = range.settings.gapPolicy,
                        whitespacePolicy = range.settings.whitespacePolicy,
                        inlineObjectPolicy = range.settings.inlineObjectPolicy,
                        passThrough = range.settings.passThrough,
                    };
                    var fragmentCount = fragments.Count;
                    BuildGeometry(in target);
                    if (fragments.Count > fragmentCount) targets.Add(target);
                }
                modifier.RouterRangesRebuilt();
            }

            fragments.Sort(CompareFragmentY);
            var maxY = float.NegativeInfinity;
            for (var i = 0; i < fragments.Count; i++)
            {
                maxY = Mathf.Max(maxY, fragments[i].bounds.yMax);
                fragmentPrefixMaxY.Add(maxY);
            }

            IndexEntityBounds();

            BuildFocusTargets();
            ReconcileFocus();
            ReconcilePointers();
            ReleaseRetiredRegistrationOrders();
            UniTextFocusable.Sync(owner.gameObject);
            if (registrationSnapshot.Count == 0) Detach();
        }

        /// <summary>Releases pointer leases, host subscriptions and retained geometry capture.</summary>
        public void Dispose()
        {
            if (disposed) return;
            owner.Deinitializing -= OnHostDeinitialized;
            ClearFocusedTarget();
            disposed = true;
            CancelAllPointers();
            Detach();
            lock (registrationGate) registrations.Clear();
            registrationSnapshot.Clear();
            retiredRegistrations.Clear();
            registrationOrders.Clear();
            channels.Clear();
            targets.Clear();
            focusTargets.Clear();
            fragments.Clear();
            rawFragments.Clear();
            fragmentPrefixMaxY.Clear();
            entityBounds.Clear();
            eventPool.Clear();
        }

        private void Attach()
        {
            if (subscribed || disposed) return;
            subscribed = true;
            owner.TextClicked += OnActivated;
            owner.ContextRequested.Subscribe(OnContextRequested, UniTextBase.RangeInteractionEventOrder);
            owner.PointerPressed.Subscribe(OnPointerPressed, UniTextBase.RangeInteractionEventOrder);
            owner.PointerReleased += OnPointerReleased;
            owner.PointerMoved += OnPointerMoved;
            owner.PointerEntered += OnPointerMoved;
            owner.PointerExited += OnPointerExited;
            owner.PointerLongPressProgress += OnLongPressProgress;
            owner.Committed += OnCommitted;
            RangeGeometryIndex.For(owner).Retain();
        }

        private void Detach()
        {
            if (!subscribed) return;
            subscribed = false;
            owner.TextClicked -= OnActivated;
            owner.ContextRequested.Unsubscribe(OnContextRequested);
            owner.PointerPressed.Unsubscribe(OnPointerPressed);
            owner.PointerReleased -= OnPointerReleased;
            owner.PointerMoved -= OnPointerMoved;
            owner.PointerEntered -= OnPointerMoved;
            owner.PointerExited -= OnPointerExited;
            owner.PointerLongPressProgress -= OnLongPressProgress;
            owner.Committed -= OnCommitted;
            if (RangeGeometryIndex.TryGet(owner, out var geometry)) geometry.Release();
        }

        private void SnapshotRegistrations()
        {
            lock (registrationGate)
            {
                registrationSnapshot.Clear();
                registrationSnapshot.AddRange(registrations);
            }
        }

        private void ReleaseRetiredRegistrationOrders()
        {
            retiredRegistrations.Clear();
            lock (registrationGate)
            {
                foreach (var pair in registrationOrders)
                    if (!registrations.Contains(pair.Key)) retiredRegistrations.Add(pair.Key);
                for (var i = 0; i < retiredRegistrations.Count; i++)
                    registrationOrders.Remove(retiredRegistrations[i]);
            }
        }

        private void QueueRebuild()
        {
            rebuildQueued = true;
        }

        internal void MarkRangesDirty() => rebuildQueued = true;

        private void OnCommitted(UniTextCommitChanges changes)
        {
            if (!rebuildQueued && (changes & UniTextCommitChanges.GlyphGeometry) == 0) return;
            RebuildNow();
        }

        private void BuildGeometry(in InteractionTarget target)
        {
            rawFragments.Clear();
            var useGlyphs = target.whitespacePolicy == RangeWhitespacePolicy.VisibleGlyphs ||
                            target.inlineObjectPolicy != InlineObjectPolicy.Include;
            var geometry = RangeGeometryIndex.For(owner);
            var source = useGlyphs
                ? geometry.GetGlyphFragments(target.range.start, target.range.end,
                    RangeHeight.Content)
                : geometry.GetLineFragments(target.range.start, target.range.end,
                    RangeHeight.LineBox);

            var em = owner.CurrentFontSize;
            var padX = Mathf.Max(0f, UnitValue.ResolvePx(target.hitPadding.value.x,
                target.hitPadding.unit, em));
            var padY = Mathf.Max(0f, UnitValue.ResolvePx(target.hitPadding.value.y,
                target.hitPadding.unit, em));
            var minimumX = Mathf.Max(0f, UnitValue.ResolvePx(target.minimumHitSize.value.x,
                target.minimumHitSize.unit, em));
            var minimumY = Mathf.Max(0f, UnitValue.ResolvePx(target.minimumHitSize.value.y,
                target.minimumHitSize.unit, em));

            for (var i = 0; i < source.Length; i++)
            {
                ref readonly var fragment = ref source[i];
                var inline = IsInlineObjectFragment(fragment.ClusterStart, fragment.ClusterEnd);
                if (target.inlineObjectPolicy == InlineObjectPolicy.Exclude && inline) continue;
                if (target.inlineObjectPolicy == InlineObjectPolicy.Only && !inline) continue;
                var bounds = fragment.Bounds;
                bounds.xMin -= padX;
                bounds.xMax += padX;
                bounds.yMin -= padY;
                bounds.yMax += padY;
                ExpandMinimum(ref bounds, minimumX, minimumY);
                rawFragments.Add(new RawFragment
                {
                    bounds = bounds,
                    lineIndex = fragment.LineIndex,
                    clusterStart = fragment.ClusterStart,
                });
            }

            if (rawFragments.Count == 0) return;
            switch (target.gapPolicy)
            {
                case WrappedGapPolicy.Separate:
                    for (var i = 0; i < rawFragments.Count; i++)
                    {
                        var fragment = rawFragments[i];
                        AddFragment(in target, in fragment);
                    }
                    break;
                case WrappedGapPolicy.JoinLineFragments:
                    AddJoinedLineFragments(in target);
                    break;
                case WrappedGapPolicy.BoundingBlock:
                    AddBoundingBlock(in target);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void AddJoinedLineFragments(in InteractionTarget target)
        {
            rawFragments.Sort((a, b) => a.lineIndex.CompareTo(b.lineIndex));
            var current = rawFragments[0];
            for (var i = 1; i < rawFragments.Count; i++)
            {
                var next = rawFragments[i];
                if (next.lineIndex == current.lineIndex)
                {
                    current.bounds = Union(current.bounds, next.bounds);
                    current.clusterStart = Mathf.Min(current.clusterStart, next.clusterStart);
                    continue;
                }
                AddFragment(in target, in current);
                current = next;
            }
            AddFragment(in target, in current);
        }

        private void AddBoundingBlock(in InteractionTarget target)
        {
            var block = rawFragments[0];
            for (var i = 1; i < rawFragments.Count; i++)
            {
                var next = rawFragments[i];
                block.bounds = Union(block.bounds, next.bounds);
                block.clusterStart = Mathf.Min(block.clusterStart, next.clusterStart);
            }
            AddFragment(in target, in block);
        }

        private void AddFragment(in InteractionTarget target, in RawFragment source)
        {
            fragments.Add(new InteractionFragment
            {
                target = target,
                bounds = source.bounds,
                clusterStart = source.clusterStart,
            });
        }

        private bool IsInlineObjectFragment(int start, int end)
        {
            var buffers = owner.Buffers;
            if (buffers == null) return false;
            var codepoints = buffers.codepoints;
            if (codepoints.data == null) return false;
            for (var i = Mathf.Max(0, start); i < end && i < codepoints.count; i++)
                if (codepoints.data[i] == UnicodeData.ObjectReplacementCharacter) return true;
            return false;
        }

        private void BuildFocusTargets()
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                if (target.passThrough) continue;
                var duplicate = false;
                for (var j = 0; j < focusTargets.Count; j++)
                {
                    var candidate = focusTargets[j];
                    if (!SameEntity(in target, in candidate)) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) focusTargets.Add(target);
            }
            focusTargets.Sort(CompareFocusTargets);
        }

        private void IndexEntityBounds()
        {
            for (var i = 0; i < fragments.Count; i++)
            {
                var fragment = fragments[i];
                var identity = fragment.target.range.Identity;
                entityBounds[identity] = entityBounds.TryGetValue(identity, out var bounds)
                    ? Union(bounds, fragment.bounds)
                    : fragment.bounds;
            }
        }

        private void ReconcileFocus()
        {
            if (!hasFocusedTarget) return;
            var oldStart = focusedTarget.range.start;
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var candidate = focusTargets[i];
                if (!SameEntity(in focusedTarget, in candidate)) continue;
                focusedTarget = candidate;
                TryGetFirstFragment(in focusedTarget, out focusedFragment);
                return;
            }

            ClearFocusedTarget();
            if (focusTargets.Count == 0) return;
            var replacement = focusTargets.Count - 1;
            for (var i = 0; i < focusTargets.Count; i++)
            {
                if (focusTargets[i].range.start < oldStart) continue;
                replacement = i;
                break;
            }
            var target = focusTargets[replacement];
            SetFocusedTarget(in target);
        }

        private void SetFocusedTarget(in InteractionTarget target)
        {
            if (hasFocusedTarget && SameEntity(in focusedTarget, in target))
            {
                focusedTarget = target;
                TryGetFirstFragment(in target, out focusedFragment);
                return;
            }

            ClearFocusedTarget();
            if (!TryGetFirstFragment(in target, out var fragment)) return;
            focusedTarget = target;
            focusedFragment = fragment;
            hasFocusedTarget = true;
            if (UniTextSemantics.TryGet(owner, out var semantics))
                semantics.SetFocused(target.range.Identity);
            var state = ResolvePointerState(in target.key);
            DispatchStateChange(in fragment, null, fragment.bounds.center,
                RangeInteractionSignal.Focused, false, true, state, state);
            Dispatch(RangeInteractionKind.Focused, in fragment, null, fragment.bounds.center);
        }

        private void ClearFocusedTarget()
        {
            if (!hasFocusedTarget) return;
            var target = focusedTarget;
            var fragment = focusedFragment;
            var state = ResolvePointerState(in target.key);
            hasFocusedTarget = false;
            if (UniTextSemantics.TryGet(owner, out var semantics))
                semantics.SetFocused(default);
            focusedTarget = default;
            focusedFragment = default;
            DispatchStateChange(in fragment, null, fragment.bounds.center,
                RangeInteractionSignal.Focused, true, false, state, state);
            Dispatch(RangeInteractionKind.Blurred, in fragment, null, fragment.bounds.center);
        }

        private bool DispatchFocused(RangeInteractionKind kind)
        {
            if (!hasFocusedTarget) return false;
            Dispatch(kind, in focusedFragment, null, focusedFragment.bounds.center);
            return true;
        }

        private int IndexOfFocusedTarget()
        {
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var target = focusTargets[i];
                if (SameEntity(in focusedTarget, in target)) return i;
            }
            return -1;
        }

        private int FindLogicalTarget(RangeNavigationDirection direction, RangeChannel channel,
            bool restrictChannel, bool wrap)
        {
            var first = -1;
            var last = -1;
            var current = -1;
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var target = focusTargets[i];
                if (restrictChannel && target.range.Channel != channel) continue;
                if (first < 0) first = i;
                last = i;
                if (hasFocusedTarget && SameEntity(in focusedTarget, in target)) current = i;
            }
            if (first < 0) return -1;
            if (direction == RangeNavigationDirection.Home) return first;
            if (direction == RangeNavigationDirection.End) return last;
            var previous = direction is RangeNavigationDirection.Previous or
                RangeNavigationDirection.Left or RangeNavigationDirection.Up;
            if (current < 0) return previous ? last : first;
            for (var i = current + (previous ? -1 : 1);
                 (uint)i < (uint)focusTargets.Count; i += previous ? -1 : 1)
                if (!restrictChannel || focusTargets[i].range.Channel == channel) return i;
            return wrap ? (previous ? last : first) : -1;
        }

        private int FindVisualTarget(RangeNavigationDirection direction, RangeChannel channel,
            bool restrictChannel, bool wrap)
        {
            if (!hasFocusedTarget)
                return FindLogicalTarget(RangeNavigationDirection.Home, channel, restrictChannel,
                    false);
            var origin = ResolveAnchor(in focusedTarget).center;
            var axis = direction switch
            {
                RangeNavigationDirection.Left => Vector2.left,
                RangeNavigationDirection.Right => Vector2.right,
                RangeNavigationDirection.Up => Vector2.up,
                RangeNavigationDirection.Down => Vector2.down,
                _ => throw new ArgumentOutOfRangeException(nameof(direction)),
            };
            var best = -1;
            var bestScore = float.PositiveInfinity;
            for (var i = 0; i < focusTargets.Count; i++)
            {
                var candidate = focusTargets[i];
                if (SameEntity(in focusedTarget, in candidate) ||
                    restrictChannel && candidate.range.Channel != channel) continue;
                var delta = ResolveAnchor(in candidate).center - origin;
                var forward = Vector2.Dot(delta, axis);
                if (forward <= 0f) continue;
                var perpendicular = Mathf.Abs(delta.x * axis.y - delta.y * axis.x);
                var score = forward + perpendicular * 2f;
                if (score >= bestScore) continue;
                bestScore = score;
                best = i;
            }
            if (best >= 0 || !wrap) return best;
            var fallback = direction is RangeNavigationDirection.Left or RangeNavigationDirection.Up
                ? RangeNavigationDirection.End
                : RangeNavigationDirection.Home;
            return FindLogicalTarget(fallback, channel, restrictChannel, false);
        }

        private void RequestScroll(in InteractionTarget target)
        {
            var handler = ScrollIntoViewRequested;
            if (handler == null) return;
            handler.Invoke(new RangeScrollRequest(owner, in target.range, ResolveAnchor(in target)));
        }

        private void SelectHost()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && eventSystem.currentSelectedGameObject != owner.gameObject)
                eventSystem.SetSelectedGameObject(owner.gameObject);
        }

        internal void OnHostSelected()
        {
            if (!hasFocusedTarget) FocusFirst();
        }

        internal void OnHostDeselected() => ClearFocusedTarget();

        internal bool OnHostMove(bool previous) => MoveFocus(previous);

        internal bool OnHostMove(MoveDirection direction)
        {
            if (!hasFocusedTarget) return FocusFirst();
            var modifier = focusedTarget.owner;
            var restrictChannel = modifier.RouterNavigationGroup == RangeNavigationGroup.Channel;
            var group = restrictChannel ? focusedTarget.range.Channel : null;
            var mapped = direction switch
            {
                MoveDirection.Left => RangeNavigationDirection.Left,
                MoveDirection.Right => RangeNavigationDirection.Right,
                MoveDirection.Up => RangeNavigationDirection.Up,
                MoveDirection.Down => RangeNavigationDirection.Down,
                _ => RangeNavigationDirection.Next,
            };
            return MoveFocus(mapped, modifier.RouterNavigationOrder, group, restrictChannel,
                modifier.RouterWrapNavigation);
        }

        internal bool OnHostSubmit() => ActivateFocused();

        internal RangeGestureArbitration OnHostDrag(PointerEventData eventData)
        {
            if (!pointers.TryGetValue(eventData.pointerId, out var session) ||
                !session.hasPressed) return default;
            var camera = eventData.pressEventCamera != null
                ? eventData.pressEventCamera
                : eventData.enterEventCamera;
            var evt = dragEventScratch.Set(TextHitResult.None, PointerTrigger.PrimaryClick,
                eventData.position, camera, UniTextBase.ReadCurrentModifiers(),
                UniTextBase.ResolvePointerKind(eventData), eventData.pointerId);
            if (!TryGetLocal(evt, out var local)) return CurrentGestureArbitration(session);
            UpdateSessionSnapshot(session, evt, local);
            var slopExceeded = Vector2.Distance(session.downScreenPosition, evt.ScreenPosition) >
                               GestureMetrics.SlopPx(UniTextSettings.DragSlopDp, owner.canvas);
            var claimed = RouteGesture(session, RangeGesturePhase.Moved, evt, local,
                slopExceeded, 0f);
            if (!claimed && slopExceeded) CancelPress(session, evt, local);
            return CurrentGestureArbitration(session);
        }

        internal void OnHostDragEnded(PointerEventData eventData)
        {
            if (!pointers.TryGetValue(eventData.pointerId, out var session) ||
                !session.hasPressed) return;
            var camera = eventData.pressEventCamera != null
                ? eventData.pressEventCamera
                : eventData.enterEventCamera;
            var evt = dragEventScratch.Set(TextHitResult.None, PointerTrigger.PrimaryClick,
                eventData.position, camera, UniTextBase.ReadCurrentModifiers(),
                UniTextBase.ResolvePointerKind(eventData), eventData.pointerId);
            if (!TryGetLocal(evt, out var local)) local = session.localPosition;
            CancelPress(session, evt, local);
        }

        private void OnPointerMoved(TextPointerEvent evt)
        {
            if (!TryGetLocal(evt, out var local)) return;
            var session = GetSession(evt.PointerId);
            UpdateSessionSnapshot(session, evt, local);

            var slopExceeded = session.hasPressed &&
                               Vector2.Distance(session.downScreenPosition, evt.ScreenPosition) >
                               GestureMetrics.SlopPx(UniTextSettings.DragSlopDp, owner.canvas);
            if (session.hasPressed &&
                !RouteGesture(session, RangeGesturePhase.Moved, evt, local, slopExceeded, 0f) &&
                slopExceeded)
            {
                CancelPress(session, evt, local);
            }

            if (evt.Kind == PointerKind.Touch) return;
            var hasHit = TryHit(local, out var hit);
            SetHover(session, hasHit, in hit, evt, local);
        }

        private void OnPointerExited(TextPointerEvent evt)
        {
            if (!pointers.TryGetValue(evt.PointerId, out var session)) return;
            var local = session.localPosition;
            SetHover(session, false, default, evt, local);
        }

        private void OnPointerPressed(TextPointerEvent evt)
        {
            if (evt.Consumed) return;
            if (!TryGetLocal(evt, out var local)) return;
            var session = GetSession(evt.PointerId);
            UpdateSessionSnapshot(session, evt, local);
            session.downScreenPosition = evt.ScreenPosition;
            session.lastGestureScreenPosition = evt.ScreenPosition;
            session.hasActivationOrigin = false;
            session.gestureRecognizer = null;
            session.gestureCompatibility = default;

            if (!TryHit(local, out var hit)) return;
            session.hasActivationOrigin = true;
            session.activationOrigin = hit.target;
            if (hit.target.passThrough)
            {
                Dispatch(RangeInteractionKind.Pressed, in hit, evt, local);
                return;
            }

            evt.Consumed = true;

            SetFocusedTarget(in hit.target);
            SelectHost();

            var oldState = ResolvePointerState(in hit.target.key);
            var wasPressed = IsPressed(in hit.target.key);
            session.hasPressed = true;
            session.pressed = hit.target;
            session.pressedFragment = hit;
            var newState = ResolvePointerState(in hit.target.key);
            DispatchStateChange(in hit, evt, local, RangeInteractionSignal.Pressed,
                wasPressed, IsPressed(in hit.target.key), oldState, newState);
            Dispatch(RangeInteractionKind.Pressed, in hit, evt, local);
            if (RouteGesture(session, RangeGesturePhase.Pressed, evt, local, false, 0f))
                session.hasActivationOrigin = false;
        }

        private void OnPointerReleased(TextPointerEvent evt)
        {
            if (!pointers.TryGetValue(evt.PointerId, out var session)) return;
            if (!TryGetLocal(evt, out var local)) local = session.localPosition;
            UpdateSessionSnapshot(session, evt, local);

            if (!session.hasPressed)
            {
                if (session.hasActivationOrigin)
                    DispatchReleaseForPassThrough(session, evt, local);
                else if (evt.Kind == PointerKind.Touch)
                    pointers.Remove(evt.PointerId);
                return;
            }

            var pressed = session.pressed;
            var fragment = session.pressedFragment;
            var customGesture = session.gestureRecognizer != null;
            if (customGesture)
                RouteGesture(session, RangeGesturePhase.Released, evt, local, false, 1f);
            var oldState = ResolvePointerState(in pressed.key);
            var wasPressed = IsPressed(in pressed.key);
            session.hasPressed = false;
            var newState = ResolvePointerState(in pressed.key);
            DispatchStateChange(in fragment, evt, local, RangeInteractionSignal.Pressed,
                wasPressed, IsPressed(in pressed.key), oldState, newState);

            if (customGesture)
            {
                session.hasActivationOrigin = false;
                Dispatch(RangeInteractionKind.Released, in fragment, evt, local);
                evt.Consumed = true;
                if (evt.Kind == PointerKind.Touch) pointers.Remove(evt.PointerId);
                return;
            }

            var inside = TryHit(local, out var releaseHit) &&
                         SameActivationTarget(in pressed, in releaseHit.target);
            if (inside)
            {
                session.hasActivationOrigin = true;
                session.activationOrigin = pressed;
                Dispatch(RangeInteractionKind.Released, in releaseHit, evt, local);
            }
            else
            {
                session.hasActivationOrigin = false;
                Dispatch(RangeInteractionKind.Canceled, in fragment, evt, local);
                if (evt.Kind == PointerKind.Touch)
                    pointers.Remove(evt.PointerId);
            }
        }

        private void DispatchReleaseForPassThrough(PointerSession session, TextPointerEvent evt,
            Vector2 local)
        {
            if (TryHit(local, out var hit) &&
                SameActivationTarget(in session.activationOrigin, in hit.target))
                Dispatch(RangeInteractionKind.Released, in hit, evt, local);
            else
                session.hasActivationOrigin = false;
        }

        private void OnActivated(TextPointerEvent evt)
        {
            if (!pointers.TryGetValue(evt.PointerId, out var session) ||
                !session.hasActivationOrigin || !TryGetLocal(evt, out var local)) return;
            if (!TryHit(local, out var hit) ||
                !SameActivationTarget(in session.activationOrigin, in hit.target))
            {
                session.hasActivationOrigin = false;
                return;
            }

            session.hasActivationOrigin = false;
            Dispatch(RangeInteractionKind.Activated, in hit, evt, local);
            if (!hit.target.passThrough) evt.Consumed = true;
            if (evt.Kind == PointerKind.Touch) pointers.Remove(evt.PointerId);
        }

        private void OnContextRequested(TextPointerEvent evt)
        {
            if (evt.Consumed) return;
            if (pointers.TryGetValue(evt.PointerId, out var claimedSession) &&
                claimedSession.gestureRecognizer != null)
            {
                evt.Consumed = true;
                return;
            }
            if (!TryGetLocal(evt, out var local) || !TryHit(local, out var hit)) return;
            if (pointers.TryGetValue(evt.PointerId, out var session))
                CancelPress(session, evt, local);
            Dispatch(RangeInteractionKind.ContextRequested, in hit, evt, local);
            if (!hit.target.passThrough) evt.Consumed = true;
        }

        private void OnLongPressProgress(TextPointerEvent evt, float progress)
        {
            if (!pointers.TryGetValue(evt.PointerId, out var session) || !session.hasPressed) return;
            var local = session.localPosition;
            if (RouteGesture(session, RangeGesturePhase.LongPressProgress, evt, local, false,
                    progress))
                evt.Consumed = true;
            Dispatch(RangeInteractionKind.LongPressProgress, in session.pressedFragment,
                evt, local, RangeState.Normal, RangeState.Normal, progress);
        }

        private void SetHover(PointerSession session, bool hasHit, in InteractionFragment hit,
            TextPointerEvent evt, Vector2 local)
        {
            if (session.hasHover && hasHit && session.hover.key.Equals(hit.target.key))
            {
                session.hover = hit.target;
                session.hoverFragment = hit;
                return;
            }

            if (session.hasHover)
            {
                var previous = session.hover;
                var previousFragment = session.hoverFragment;
                var oldState = ResolvePointerState(in previous.key);
                var wasHovered = IsHovered(in previous.key);
                session.hasHover = false;
                var newState = ResolvePointerState(in previous.key);
                DispatchStateChange(in previousFragment, evt, local, RangeInteractionSignal.Hovered,
                    wasHovered, IsHovered(in previous.key), oldState, newState);
                Dispatch(RangeInteractionKind.Exited, in previousFragment, evt, local);
            }

            if (!hasHit) return;
            var before = ResolvePointerState(in hit.target.key);
            var wasHoveredOnTarget = IsHovered(in hit.target.key);
            session.hasHover = true;
            session.hover = hit.target;
            session.hoverFragment = hit;
            var after = ResolvePointerState(in hit.target.key);
            DispatchStateChange(in hit, evt, local, RangeInteractionSignal.Hovered,
                wasHoveredOnTarget, IsHovered(in hit.target.key), before, after);
            Dispatch(RangeInteractionKind.Entered, in hit, evt, local);
        }

        private void CancelPress(PointerSession session, TextPointerEvent evt, Vector2 local)
        {
            session.hasActivationOrigin = false;
            if (!session.hasPressed) return;
            if (session.gestureRecognizer != null)
                RouteGesture(session, RangeGesturePhase.Canceled, evt, local, false, 0f);
            var pressed = session.pressed;
            var fragment = session.pressedFragment;
            var oldState = ResolvePointerState(in pressed.key);
            var wasPressed = IsPressed(in pressed.key);
            session.hasPressed = false;
            var newState = ResolvePointerState(in pressed.key);
            DispatchStateChange(in fragment, evt, local, RangeInteractionSignal.Pressed,
                wasPressed, IsPressed(in pressed.key), oldState, newState);
            Dispatch(RangeInteractionKind.Canceled, in fragment, evt, local);
        }

        private void DispatchStateChange(in InteractionFragment fragment, TextPointerEvent evt,
            Vector2 local, RangeInteractionSignal signal, bool previousSignalValue,
            bool signalValue, RangeState previous, RangeState current)
        {
            if (previousSignalValue == signalValue) return;
            Dispatch(RangeInteractionKind.StateChanged, in fragment, evt, local, previous, current,
                0f, signal, previousSignalValue, signalValue);
            RefreshCursor(fragment.target.owner);
        }

        private void Dispatch(RangeInteractionKind kind, in InteractionFragment fragment,
            TextPointerEvent pointer, Vector2 local, RangeState previous = RangeState.Normal,
            RangeState current = RangeState.Normal, float progress = 0f,
            RangeInteractionSignal signal = RangeInteractionSignal.Hovered,
            bool previousSignalValue = false, bool signalValue = false)
            => Dispatch(kind, in fragment, pointer, local, previous, current, progress, signal,
                previousSignalValue, signalValue, null, default);

        private void Dispatch(RangeInteractionKind kind, in InteractionFragment fragment,
            TextPointerEvent pointer, Vector2 local, RangeState previous, RangeState current,
            float progress, RangeInteractionSignal signal, bool previousSignalValue,
            bool signalValue, RangeGestureRecognizer gestureRecognizer,
            RangeGesturePhase gesturePhase)
        {
            var interaction = RentEvent();
            Populate(interaction, kind, in fragment, pointer, local, previous, current, progress,
                signal, previousSignalValue, signalValue, gestureRecognizer, gesturePhase);
            try
            {
                interaction.Route = RangeInteractionRoute.Capture;
                Capturing?.Invoke(interaction);

                if (!interaction.Handled)
                {
                    interaction.Route = RangeInteractionRoute.Target;
                    if (interaction.Channel != null && channels.TryGetValue(interaction.Channel, out var channel))
                        channel.Raise(interaction);
                    if (!interaction.Handled)
                        fragment.target.owner.RouterDispatch(interaction);
                }

                if (!interaction.Handled)
                {
                    interaction.Route = RangeInteractionRoute.Bubble;
                    Bubbling?.Invoke(interaction);
                }

                if (!interaction.DefaultPrevented)
                    fragment.target.owner.RouterDefaultAction(interaction);
            }
            finally
            {
                eventPool.Push(interaction);
            }
        }

        private void Populate(RangeInteraction result, RangeInteractionKind kind,
            in InteractionFragment fragment, TextPointerEvent pointer, Vector2 local,
            RangeState previous, RangeState current, float progress,
            RangeInteractionSignal signal, bool previousSignalValue, bool signalValue,
            RangeGestureRecognizer gestureRecognizer, RangeGesturePhase gesturePhase)
        {
            result.ResetRouting();
            var range = fragment.target.range;
            result.Kind = kind;
            result.Channel = range.Channel;
            result.Source = range.Source;
            result.Entity = range.Identity;
            result.HitSegment = range.Segment;
            result.Payload = range.Payload;
            result.Revision = range.Revision;
            result.Range = range;
            result.TextHit = ResolveTextHit(pointer, in fragment);
            result.Trigger = pointer?.Trigger ?? PointerTrigger.Keyboard;
            result.PointerKind = pointer?.Kind ?? PointerKind.Mouse;
            result.PointerId = pointer?.PointerId ?? 0;
            result.HasPointer = pointer != null;
            result.ScreenPosition = pointer?.ScreenPosition ?? default;
            result.LocalPosition = local;
            result.EventCamera = pointer?.EventCamera;
            result.Modifiers = pointer?.Modifiers ?? PointerModifiers.None;
            var anchor = ResolveAnchor(in fragment.target);
            result.AnchorRect = anchor.width > 0f || anchor.height > 0f ? anchor : fragment.bounds;
            result.PreviousState = previous;
            result.State = current;
            result.Progress = progress;
            result.Signal = signal;
            result.PreviousSignalValue = previousSignalValue;
            result.SignalValue = signalValue;
            result.GestureRecognizer = gestureRecognizer;
            result.GesturePhase = gesturePhase;
        }

        private bool RouteGesture(PointerSession session, RangeGesturePhase phase,
            TextPointerEvent evt, Vector2 local, bool slopExceeded, float progress)
        {
            var target = session.pressed;
            var delta = evt.ScreenPosition - session.lastGestureScreenPosition;
            session.lastGestureScreenPosition = evt.ScreenPosition;
            if (phase == RangeGesturePhase.Moved && delta.sqrMagnitude <= Mathf.Epsilon)
                return session.gestureRecognizer != null;
            var context = new RangeGestureContext(in target.range, phase, evt.Kind, evt.PointerId,
                session.downScreenPosition, evt.ScreenPosition, local, delta, evt.EventCamera,
                evt.Modifiers, slopExceeded, progress, Time.unscaledTime);

            if (session.gestureRecognizer != null)
            {
                session.gestureRecognizer.Evaluate(in context);
                Dispatch(RangeInteractionKind.Gesture, in session.pressedFragment, evt, local,
                    RangeState.Normal, RangeState.Normal, progress, RangeInteractionSignal.Pressed,
                    true, true, session.gestureRecognizer, phase);
                if (phase == RangeGesturePhase.Released || phase == RangeGesturePhase.Canceled)
                {
                    session.gestureRecognizer = null;
                    session.gestureCompatibility = default;
                }
                return true;
            }

            var recognizers = target.owner.GestureRecognizers;
            RangeGestureRecognizer winner = null;
            for (var i = 0; i < recognizers.Count; i++)
            {
                var recognizer = recognizers[i];
                if (recognizer == null || recognizer.Evaluate(in context) != RangeGestureDecision.Claim)
                    continue;
                if (phase == RangeGesturePhase.Moved && slopExceeded &&
                    recognizer.Priority <= RangeGestureRecognizer.BuiltInDragPriority) continue;
                if (winner != null && recognizer.Priority <= winner.Priority) continue;
                winner = recognizer;
            }
            if (winner == null) return false;

            session.gestureRecognizer = winner;
            session.gestureCompatibility = winner.Compatibility;
            session.hasActivationOrigin = false;
            Dispatch(RangeInteractionKind.Gesture, in session.pressedFragment, evt, local,
                RangeState.Normal, RangeState.Normal, progress, RangeInteractionSignal.Pressed,
                true, true, winner, phase);
            return true;
        }

        private static RangeGestureArbitration CurrentGestureArbitration(PointerSession session)
            => session.gestureRecognizer == null
                ? default
                : new RangeGestureArbitration(true, session.gestureCompatibility);

        private TextHitResult ResolveTextHit(TextPointerEvent pointer,
            in InteractionFragment fragment)
        {
            if (pointer != null && pointer.Hit.hit &&
                pointer.Hit.cluster >= fragment.target.range.start &&
                pointer.Hit.cluster < fragment.target.range.end)
                return pointer.Hit;
            return new TextHitResult(-1, fragment.clusterStart, fragment.bounds.center, 0f);
        }

        private Rect ResolveAnchor(in InteractionTarget target)
        {
            return entityBounds.TryGetValue(target.range.Identity, out var entity)
                ? entity
                : default;
        }

        private RangeInteraction RentEvent()
            => eventPool.Count > 0 ? eventPool.Pop() : new RangeInteraction();

        private bool TryHit(Vector2 local, out InteractionFragment result)
        {
            result = default;
            if (fragments.Count == 0) return false;
            var upper = UpperBoundY(local.y);
            var found = false;
            for (var i = upper - 1; i >= 0; i--)
            {
                if (fragmentPrefixMaxY[i] < local.y) break;
                var candidate = fragments[i];
                if (!candidate.target.owner.IsInitialized || !candidate.bounds.Contains(local)) continue;
                if (found && CompareTargets(in candidate.target, in result.target) >= 0) continue;
                result = candidate;
                found = true;
            }
            return found;
        }

        private int UpperBoundY(float y)
        {
            var low = 0;
            var high = fragments.Count;
            while (low < high)
            {
                var middle = low + ((high - low) >> 1);
                if (fragments[middle].bounds.yMin <= y) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private static int CompareTargets(in InteractionTarget a, in InteractionTarget b)
        {
            var value = b.priority.CompareTo(a.priority);
            if (value != 0) return value;
            value = b.styleOrder.CompareTo(a.styleOrder);
            if (value != 0) return value;
            value = (a.range.end - a.range.start).CompareTo(b.range.end - b.range.start);
            if (value != 0) return value;
            return a.registrationOrder.CompareTo(b.registrationOrder);
        }

        private static int CompareFragmentY(InteractionFragment a, InteractionFragment b)
        {
            var value = a.bounds.yMin.CompareTo(b.bounds.yMin);
            return value != 0 ? value : CompareTargets(in a.target, in b.target);
        }

        private static int CompareFocusTargets(InteractionTarget a, InteractionTarget b)
        {
            var value = a.range.start.CompareTo(b.range.start);
            if (value != 0) return value;
            value = a.range.end.CompareTo(b.range.end);
            return value != 0 ? value : CompareTargets(in a, in b);
        }

        private bool SameActivationTarget(in InteractionTarget origin, in InteractionTarget current)
        {
            switch (origin.activationScope)
            {
                case RangeInteractionScope.Segment:
                    return origin.key.Equals(current.key);
                case RangeInteractionScope.Entity:
                    return SameEntity(in origin, in current);
                case RangeInteractionScope.Channel:
                    return origin.range.Channel != null && origin.range.Channel == current.range.Channel;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static bool SameEntity(in InteractionTarget a, in InteractionTarget b)
        {
            return a.range.Identity == b.range.Identity;
        }

        private RangeState ResolvePointerState(in TargetKey key)
        {
            if (IsPressed(in key)) return RangeState.Pressed;
            return IsHovered(in key) ? RangeState.Hovered : RangeState.Normal;
        }

        private bool IsPressed(in TargetKey key)
        {
            foreach (var pair in pointers)
                if (pair.Value.hasPressed && pair.Value.pressed.key.Equals(key)) return true;
            return false;
        }

        private bool IsHovered(in TargetKey key)
        {
            foreach (var pair in pointers)
                if (pair.Value.hasHover && pair.Value.hover.key.Equals(key)) return true;
            return false;
        }

        private void RefreshCursor(InteractiveModifier modifier)
        {
            var engaged = false;
            foreach (var pair in pointers)
            {
                var pointer = pair.Value;
                if ((pointer.hasPressed && ReferenceEquals(pointer.pressed.owner, modifier)) ||
                    (pointer.hasHover && ReferenceEquals(pointer.hover.owner, modifier)))
                {
                    engaged = true;
                    break;
                }
            }
            modifier.RouterRefreshCursor(engaged);
        }

        private PointerSession GetSession(int pointerId)
        {
            if (pointers.TryGetValue(pointerId, out var existing)) return existing;
            var created = new PointerSession { pointerId = pointerId };
            pointers.Add(pointerId, created);
            return created;
        }

        private static void UpdateSessionSnapshot(PointerSession session, TextPointerEvent evt,
            Vector2 local)
        {
            session.pointerKind = evt.Kind;
            session.screenPosition = evt.ScreenPosition;
            session.localPosition = local;
            session.eventCamera = evt.EventCamera;
            session.modifiers = evt.Modifiers;
        }

        private bool TryGetLocal(TextPointerEvent evt, out Vector2 local)
            => owner.TryPointerScreenToLocal(evt.ScreenPosition, evt.EventCamera, out local);

        private void ReconcilePointers()
        {
            foreach (var pair in pointers)
            {
                var pointer = pair.Value;
                ReconcileTarget(pointer, ref pointer.hasHover, ref pointer.hover,
                    ref pointer.hoverFragment, RangeInteractionKind.Exited);
                ReconcileTarget(pointer, ref pointer.hasPressed, ref pointer.pressed,
                    ref pointer.pressedFragment, RangeInteractionKind.Canceled);
                if (!pointer.hasActivationOrigin) continue;
                if (TryResolveTarget(in pointer.activationOrigin, out var origin))
                    pointer.activationOrigin = origin;
                else
                    pointer.hasActivationOrigin = false;
            }
        }

        private void ReconcileTarget(PointerSession pointer, ref bool present,
            ref InteractionTarget target, ref InteractionFragment fragment,
            RangeInteractionKind disappearanceKind)
        {
            if (!present) return;
            if (TryResolveTarget(in target, out var resolved))
            {
                target = resolved;
                if (TryResolveFragment(in resolved, pointer.localPosition, out var resolvedFragment))
                    fragment = resolvedFragment;
                return;
            }

            var oldState = ResolvePointerState(in target.key);
            var signal = disappearanceKind == RangeInteractionKind.Canceled
                ? RangeInteractionSignal.Pressed
                : RangeInteractionSignal.Hovered;
            var previousSignalValue = signal == RangeInteractionSignal.Pressed
                ? IsPressed(in target.key)
                : IsHovered(in target.key);
            if (disappearanceKind == RangeInteractionKind.Canceled &&
                pointer.gestureRecognizer != null)
            {
                var gestureEvent = PointerEvent(pointer, PointerTrigger.Hover);
                RouteGesture(pointer, RangeGesturePhase.Canceled, gestureEvent,
                    pointer.localPosition, false, 0f);
            }
            present = false;
            var newState = ResolvePointerState(in target.key);
            var evt = PointerEvent(pointer, PointerTrigger.Hover);
            var signalValue = signal == RangeInteractionSignal.Pressed
                ? IsPressed(in target.key)
                : IsHovered(in target.key);
            DispatchStateChange(in fragment, evt, pointer.localPosition, signal,
                previousSignalValue, signalValue, oldState, newState);
            Dispatch(disappearanceKind, in fragment, evt, pointer.localPosition);
            if (disappearanceKind == RangeInteractionKind.Canceled)
                pointer.hasActivationOrigin = false;
        }

        private bool TryResolveTarget(in InteractionTarget previous, out InteractionTarget result)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (!previous.key.Equals(targets[i].key)) continue;
                result = targets[i];
                return true;
            }
            result = default;
            return false;
        }

        private bool TryResolveFragment(in InteractionTarget target, Vector2 local,
            out InteractionFragment result)
        {
            for (var i = 0; i < fragments.Count; i++)
            {
                if (!target.key.Equals(fragments[i].target.key)) continue;
                result = fragments[i];
                if (result.bounds.Contains(local)) return true;
            }
            result = default;
            return false;
        }

        private bool TryGetFirstFragment(in InteractionTarget target,
            out InteractionFragment result)
        {
            for (var i = 0; i < fragments.Count; i++)
            {
                if (!target.key.Equals(fragments[i].target.key)) continue;
                result = fragments[i];
                return true;
            }
            result = default;
            return false;
        }

        private void CancelAllPointers()
        {
            foreach (var pair in pointers)
            {
                var pointer = pair.Value;
                var evt = PointerEvent(pointer, PointerTrigger.Hover);
                if (pointer.hasPressed) CancelPress(pointer, evt, pointer.localPosition);
                if (pointer.hasHover)
                    SetHover(pointer, false, default, evt, pointer.localPosition);
            }
            pointers.Clear();
        }

        private static TextPointerEvent PointerEvent(PointerSession pointer, PointerTrigger trigger)
            => new(TextHitResult.None, trigger, pointer.screenPosition, pointer.eventCamera,
                pointer.modifiers, pointer.pointerKind, pointer.pointerId);

        private static void ExpandMinimum(ref Rect bounds, float width, float height)
        {
            if (bounds.width < width)
            {
                var delta = (width - bounds.width) * 0.5f;
                bounds.xMin -= delta;
                bounds.xMax += delta;
            }
            if (bounds.height < height)
            {
                var delta = (height - bounds.height) * 0.5f;
                bounds.yMin -= delta;
                bounds.yMax += delta;
            }
        }

        private static Rect Union(Rect a, Rect b)
            => Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(UniTextInteractions));
        }
    }
}
