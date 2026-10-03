using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Describes one exact structural or nested state transition in a modifier graph preset.</summary>
    public readonly struct ModifierGraphPresetChange
    {
        internal ModifierGraphPresetChange(BaseModifier modifier,
            IStateMemberReplay source, StateMember member, UniTextDirty flags,
            bool structural)
        {
            Modifier = modifier;
            Source = source;
            Member = member;
            Flags = flags;
            Structural = structural;
        }

        /// <summary>Gets the affected authored modifier node.</summary>
        public BaseModifier Modifier { get; }
        /// <summary>Gets the replayable node that owns <see cref="Member"/>, when scalar replay is available.</summary>
        public IStateMemberReplay Source { get; }
        /// <summary>Gets the exact generated member token for a nested scalar transition.</summary>
        public StateMember Member { get; }
        /// <summary>Gets the modifier-owned consequence of the changed parameter.</summary>
        public UniTextDirty Flags { get; }
        /// <summary>Gets whether modifier graph topology changed.</summary>
        public bool Structural { get; }
    }

    /// <summary>Receives one exact committed modifier-graph preset transition.</summary>
    public delegate void ModifierGraphPresetChangedHandler(ModifierGraphPreset preset,
        in ModifierGraphPresetChange change);

    /// <summary>
    /// Shareable modifier-only recipe that can be paired with different range sources through
    /// <see cref="ModifierGraphModifier"/>. Runtime consumers instantiate the asset so modifier
    /// ownership and transient state never leak between UniText components.
    /// </summary>
    [CreateAssetMenu(fileName = "ModifierGraphPreset",
        menuName = UniTextMenu.CreateAsset.ModifierGraphPreset)]
    public sealed partial class ModifierGraphPreset : ScriptableObject, IModifierChangeSink
    {
        /// <summary>Root modifier or Composite applied by every reference to this recipe.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyRootChange),
            Validator = nameof(ValidateRoot), Owned = true)]
        private BaseModifier root;

        [NonSerialized] private bool dependenciesBound;

        /// <summary>Occurs when Inspector or code changes this recipe.</summary>
        public event Action Changed;

        /// <summary>Raised with exact affected-node, member, and replay context.</summary>
        public event ModifierGraphPresetChangedHandler NodeChanged;

        private void ApplyRootChange(StateMember member, BaseModifier previous,
            ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            if (dependenciesBound) current?.SetChangeSink(this);
            var change = new ModifierGraphPresetChange(current, this, member,
                UniTextDirty.Text, true);
            PublishChange(in change);
        }

        private void ValidateRoot(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        /// <summary>
        /// Validates the complete authored graph, including topology, nested presets, channels,
        /// actions, effects, and property bindings.
        /// </summary>
        public void Validate() => ModifierGraphValidator.ThrowIfInvalid(this);

        private void OnEnable() => SetDependenciesBound(true);

        private void OnDisable() => SetDependenciesBound(false);

        internal void PrepareRuntimeCopy() => SetDependenciesBound(false);

        private void SetDependenciesBound(bool value)
        {
            if (value) root?.SetChangeSink(this);
            else if (ReferenceEquals(root?.ChangeSink, this)) root.SetChangeSink(null);
            dependenciesBound = value;
        }

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
        {
            var change = new ModifierGraphPresetChange(modifier, source, member, flags,
                structural);
            PublishChange(in change);
        }

        private void PublishChange(in ModifierGraphPresetChange change)
        {
            NodeChanged?.Invoke(this, in change);
            Changed?.Invoke();
        }

    }

    /// <summary>
    /// Modifier node that instantiates and forwards to a <see cref="ModifierGraphPreset"/> while
    /// preserving the ordinary modifier lifecycle, parameters, effects and graph traversal.
    /// </summary>
    [Serializable]
    [TypeGroup("Utility", 10)]
    [TypeDescription("Modifier graph preset: reuse one appearance or state recipe with any source")]
    public sealed partial class ModifierGraphModifier : BaseModifier
    {
        /// <summary>Shared authored recipe instantiated for this modifier owner.</summary>
        [SerializeField, StateProperty(nameof(ApplyPresetChange), Validator = nameof(ValidatePresetChange))]
        private ModifierGraphPreset preset;

        [NonSerialized] private ModifierGraphPreset runtimePreset;
        [NonSerialized] private BaseModifier runtimeRoot;
        [NonSerialized] private BaseModifier[] runtimeChildren;
        [NonSerialized] private ModifierGraphPreset subscribedPreset;
        [NonSerialized] private bool isAuthoring;

        private void ApplyPresetChange()
        {
            isAuthoring = StateAuthoring.IsActive;
            ReconcilePresetSubscription();
            if (Owner is not null)
                SynchronizeRuntimeGraph(preset, IsInitialized);
            MarkStructureChanged();
        }

        private void ValidatePresetChange(ModifierGraphPreset candidate)
            => BaseModifier.ValidateGraph(candidate?.Root);

        internal BaseModifier AuthoredRoot => preset?.Root;
        internal override IReadOnlyList<BaseModifier> Children => runtimeRoot == null ? null : runtimeChildren;
        internal override IReadOnlyList<BaseModifier> OwnedChildren => null;

        protected internal override void PrepareForParallel()
        {
            if (Owner != null && preset != null && runtimeRoot == null)
                SynchronizeRuntimeGraph(preset, false);
            runtimeRoot?.PrepareForParallel();
        }

        internal override void SetOwner(UniTextBase owner, IModifierChangeSink sink = null)
        {
            if (owner == null)
            {
                DisposeRuntimeGraph();
                base.SetOwner(null, sink);
                ReconcilePresetSubscription();
                return;
            }
            isAuthoring |= StateAuthoring.IsActive;
            if (preset == null && !isAuthoring)
                throw new InvalidOperationException(
                    "ModifierGraphModifier requires a preset.");

            base.SetOwner(owner, sink);
            ReconcilePresetSubscription();
            try
            {
                if (preset != null && runtimeRoot == null)
                    SynchronizeRuntimeGraph(preset, false);
                runtimeRoot?.SetOwner(owner, ChangeSink);
            }
            catch
            {
                DisposeRuntimeGraph();
                base.SetOwner(null);
                ReconcilePresetSubscription();
                throw;
            }
        }

        internal override void SetChangeSink(IModifierChangeSink sink)
        {
            if (sink != null) isAuthoring |= StateAuthoring.IsActive;
            base.SetChangeSink(sink);
            ReconcilePresetSubscription();
        }

        protected override void OnEnable()
        {
            if (preset == null) return;
            if (runtimeRoot == null)
            {
                if (!isAuthoring)
                    throw new InvalidOperationException(
                        "Modifier graph runtime was not created before modifier preparation.");
            }
            else
            {
                if (runtimeRoot.IsInitialized) runtimeRoot.Disable();
                runtimeRoot.Prepare();
            }
        }

        protected override void OnDisable()
        {
            if (runtimeRoot is { IsInitialized: true }) runtimeRoot.Disable();
        }

        protected override void OnDestroy()
        {
            runtimeRoot?.Destroy();
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            if (runtimeRoot == null)
            {
                if (isAuthoring) return;
                throw new InvalidOperationException("ModifierGraphModifier requires a preset.");
            }
            runtimeRoot.Apply(in context, CurrentApplyCycle);
        }

        internal override bool SignatureMatches(BaseModifier other)
            => other is ModifierGraphModifier reference &&
               (runtimeRoot ?? AuthoredRoot)?.SignatureMatches(
                   reference.runtimeRoot ?? reference.AuthoredRoot) == true;

        internal override string Signature =>
            $"preset({(runtimeRoot ?? AuthoredRoot)?.Signature ?? "missing"})";

        private void DisposeRuntimeGraph()
        {
            var previousPreset = runtimePreset;
            var previousRoot = runtimeRoot;
            runtimePreset = null;
            runtimeRoot = null;
            if (runtimeChildren != null) runtimeChildren[0] = null;
            if (ReferenceEquals(previousRoot?.AttachmentParent, this))
                previousRoot.SetAttachmentParent(null);
            ReleaseRuntimeGraph(previousPreset, previousRoot);
        }

        private void OnPresetChanged(ModifierGraphPreset _,
            in ModifierGraphPresetChange change)
        {
            if (!change.Structural && change.Modifier != null &&
                change.Source != null && change.Member.IsValid &&
                BaseModifier.TryReplayGraphState(runtimeRoot, change.Modifier,
                    change.Source, change.Member))
            {
                if (!runtimeRoot.IsInitialized &&
                    !ReferenceEquals(change.Source, change.Modifier))
                    ChangeSink?.MarkModifierChanged(this, change.Flags);
                return;
            }
            isAuthoring = StateAuthoring.IsActive;
            SynchronizeRuntimeGraph(preset, IsInitialized);
            MarkStructureChanged();
        }

        private void ReconcilePresetSubscription()
        {
            var current = ChangeSink == null || Owner is null ? null : preset;
            if (ReferenceEquals(subscribedPreset, current)) return;
            if (subscribedPreset is not null)
                subscribedPreset.NodeChanged -= OnPresetChanged;
            subscribedPreset = current;
            if (subscribedPreset is not null)
                subscribedPreset.NodeChanged += OnPresetChanged;
        }

        private void SynchronizeRuntimeGraph(ModifierGraphPreset source, bool prepare)
        {
            if (isAuthoring && source?.Root == null)
            {
                DisposeRuntimeGraph();
                return;
            }
            ReplaceRuntimeGraph(source, prepare);
        }

        private void ReplaceRuntimeGraph(ModifierGraphPreset source, bool prepare)
        {
            BuildRuntimeGraph(source, prepare, out var candidatePreset, out var candidateRoot);
            var previousPreset = runtimePreset;
            var previousRoot = runtimeRoot;
            runtimePreset = candidatePreset;
            runtimeRoot = candidateRoot;
            runtimeChildren ??= new BaseModifier[1];
            runtimeChildren[0] = candidateRoot;
            candidateRoot.SetAttachmentParent(this);
            if (ReferenceEquals(previousRoot?.AttachmentParent, this))
                previousRoot.SetAttachmentParent(null);
            ReleaseRuntimeGraph(previousPreset, previousRoot);
        }

        private void BuildRuntimeGraph(ModifierGraphPreset source, bool prepare,
            out ModifierGraphPreset candidatePreset, out BaseModifier candidateRoot)
        {
            if (!MainThread.IsCurrent)
                throw new InvalidOperationException(
                    "Modifier graph runtime must be created on the main thread.");
            if (source == null)
                throw new InvalidOperationException("ModifierGraphModifier requires a preset.");
            source.Validate();
            candidatePreset = UnityEngine.Object.Instantiate(source);
            candidatePreset.hideFlags = HideFlags.HideAndDontSave;
            candidateRoot = null;
            try
            {
                candidatePreset.PrepareRuntimeCopy();
                candidateRoot = candidatePreset.Root ?? throw new InvalidOperationException(
                    $"Modifier graph preset '{source.name}' instantiated without a root.");
                candidateRoot.SetOwner(uniText, ChangeSink);
                if (prepare) candidateRoot.Prepare();
            }
            catch
            {
                ReleaseRuntimeGraph(candidatePreset, candidateRoot);
                throw;
            }
        }

        private static void ReleaseRuntimeGraph(ModifierGraphPreset graphPreset,
            BaseModifier graphRoot)
        {
            if (ReferenceEquals(graphPreset, null) && graphRoot == null) return;
            if (!MainThread.IsCurrent)
            {
                MainThread.Post(() => ReleaseRuntimeGraph(graphPreset, graphRoot));
                return;
            }
            try
            {
                if (graphRoot == null) return;
                try
                {
                    graphRoot.Destroy();
                }
                finally
                {
                    try
                    {
                        DisposeReferencedRuntimeGraphs(graphRoot);
                    }
                    finally
                    {
                        graphRoot.SetOwner(null);
                    }
                }
            }
            finally
            {
                ObjectUtils.SafeDestroy(graphPreset);
            }
        }

        private static void DisposeReferencedRuntimeGraphs(BaseModifier root)
        {
            if (root is ModifierGraphModifier reference)
            {
                reference.DisposeRuntimeGraph();
                return;
            }
            if (root.Children is { } children)
                for (var i = 0; i < children.Count; i++)
                    if (children[i] is { } child)
                        DisposeReferencedRuntimeGraphs(child);
        }
    }
}
