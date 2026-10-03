using System;
using UnityEngine;

namespace LightSide
{
    internal struct RangeDecorationApplication
    {
        public RangeIdentity identity;
        public RangeSegment segment;
        public RangeChannel channel;
        public object payload;
        public RangeSource source;
        public TextRevision revision;
        public ModifierParameters parameters;
        public float ruleWeight;
    }

    /// <summary>
    /// Base class for modifier-authored geometry attached to final visual text ranges. It owns range
    /// accumulation, paint resolution, renderer handles and main-thread rebuild timing; subclasses only
    /// translate one immutable <see cref="RangeDecorationContext"/> into a <see cref="RangeMeshWriter"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="OnBuildDecoration"/> runs synchronously after final layout. Fragment spans and the
    /// writer are borrowed for that call and must not be retained. Vertices, indices and render-key
    /// batches are copied into modifier-owned pooled buffers.
    /// </para>
    /// <para>
    /// Modifier apply may run on a rebuild worker, so Unity objects are neither created nor destroyed
    /// there. Backend handles are acquired lazily by the writer on the layout commit and released by a
    /// post-commit action or by the owning UniText component's main-thread teardown.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract partial class BaseRangeDecorationModifier : BaseModifier, IRangeDecorationMeshOwner,
        IModifierRuleWeightReceiver, IModifierCommitChanges
    {
        /// <summary>Draw priority among decorations emitted by this modifier.</summary>
        [SerializeField, StateProperty(nameof(ApplyPriorityChange))]
        [Tooltip("Draw priority among range decorations on the same side of the text. Higher values paint above lower values.")]
        private int priority = RangeDecorationPriorities.Custom;

        [NonSerialized] private PooledBuffer<RangeDecorationApplication> applications;
        [NonSerialized] private RangeDecorationHandle behindHandle;
        [NonSerialized] private RangeDecorationHandle aboveHandle;
        [NonSerialized] private Action<UniTextCommitChanges> commitCallback;
        [NonSerialized] private NamedCatalogChangedHandler<PaintSwatch> providerChangedCallback;
        [NonSerialized] private Action hideCallback;
        [NonSerialized] private Action releaseCallback;
        [NonSerialized] private IPaintProvider subscribedProvider;
        [NonSerialized] private RangeDecorationHost decorationHost;
        [NonSerialized] private RangeGeometryIndex geometry;
        [NonSerialized] private bool releaseQueued;
        [NonSerialized] private float currentRuleWeight = 1f;
        [NonSerialized] private bool geometryCaptureRetained;
        [NonSerialized] private bool outputDirty;

        private readonly PaintResolver paintResolver = new();

        private void ApplyPriorityChange()
        {
            behindHandle?.SetPriority(priority);
            aboveHandle?.SetPriority(priority);
        }

        /// <summary>Paint catalog used by <see cref="ResolveDecorationPaint"/>.</summary>
        protected abstract IPaintProvider DecorationPaintProvider { get; }

        /// <summary>Whether this modifier consumes exact final quads through <see cref="RangeDecorationContext.VisualGlyphs"/>.</summary>
        protected virtual bool RequiresExactVisualGlyphs => true;

        protected sealed override void OnEnable()
        {
            applications.FakeClear();
            releaseQueued = false;
            commitCallback ??= OnCommitted;
            providerChangedCallback ??= OnPaintProviderChanged;
            hideCallback ??= HideOutput;
            releaseCallback ??= ReleaseOutput;
            uniText.Committed += commitCallback;
            decorationHost = RangeDecorationHost.For(uniText);
            geometry = RangeGeometryIndex.For(uniText);
            geometry.Retain(RequiresExactVisualGlyphs);
            geometryCaptureRetained = true;
            SubscribeProvider();
            OnDecorationEnable();
        }

        protected sealed override void OnDisable()
        {
            try
            {
                OnDecorationDisable();
            }
            finally
            {
                uniText.Committed -= commitCallback;
                if (geometryCaptureRetained)
                {
                    geometry?.Release(RequiresExactVisualGlyphs);
                    geometryCaptureRetained = false;
                }
                UnsubscribeProvider();
                paintResolver.ResetResolution();
                uniText.QueuePostCommit(hideCallback);
            }
        }

        protected sealed override void OnDestroy()
        {
            try
            {
                OnDecorationDestroy();
            }
            finally
            {
                applications.Return();
                paintResolver.Return();
                if (!releaseQueued)
                {
                    releaseQueued = true;
                    uniText.QueuePostCommit(releaseCallback);
                }
                decorationHost = null;
                geometry = null;
            }
        }

        protected sealed override void BeforeApply()
        {
            applications.FakeClear();
            paintResolver.ResetResolution();
            outputDirty = true;
            OnBeforeDecorationApply();
        }

        protected sealed override void OnApply(in RangeApplyContext context)
        {
            applications.Add(new RangeDecorationApplication
            {
                identity = context.Identity,
                segment = context.Segment,
                channel = context.Channel,
                payload = context.Payload.Value,
                source = context.Source,
                revision = context.Revision,
                parameters = context.Parameters,
                ruleWeight = currentRuleWeight,
            });
            OnDecorationApply(in context);
        }

        void IModifierRuleWeightReceiver.SetRuleWeight(float weight)
            => currentRuleWeight = Mathf.Clamp01(weight);

        /// <summary>
        /// Requeues the decoration output and repaints without replaying range applications —
        /// the invalidation decoration parameters declare.
        /// </summary>
        internal void InvalidateDecorationOutput()
        {
            outputDirty = true;
            uniText?.SetDirty(UniTextDirty.Mesh, UniTextCommitChanges.Appearance);
        }

        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;

        protected internal sealed override void PrepareForParallel()
        {
            if (!IsInitialized) paintResolver.MarkSourceDirty();
            paintResolver.PrepareForParallel(DecorationPaintProvider);
            OnPrepareDecorationForParallel();
        }

        /// <summary>Called after the base has subscribed its lifetime and paint receptors.</summary>
        protected virtual void OnDecorationEnable() { }

        /// <summary>Called before the base detaches its lifetime and paint receptors.</summary>
        protected virtual void OnDecorationDisable() { }

        /// <summary>Releases subclass-owned pooled or managed resources.</summary>
        protected virtual void OnDecorationDestroy() { }

        /// <summary>Resets subclass-owned apply-cycle accumulators.</summary>
        protected virtual void OnBeforeDecorationApply() { }

        /// <summary>Observes one worker-side range application after the base has retained its immutable data.</summary>
        protected virtual void OnDecorationApply(in RangeApplyContext context) { }

        /// <summary>Caches subclass main-thread state before parallel processing.</summary>
        protected virtual void OnPrepareDecorationForParallel() { }

        /// <summary>Builds final geometry for one applied entity segment.</summary>
        protected abstract void OnBuildDecoration(in RangeDecorationContext context,
            ref RangeMeshWriter writer);

        /// <summary>
        /// Resolves an optional leading paint token or the authored field, applies the common projection
        /// override chain, and retains any gradient-ramp row until the next decoration rebuild.
        /// </summary>
        protected RangeDecorationPaint ResolveDecorationPaint(ref ParameterReader reader,
            in PaintRef authored, Color32 defaultColor, PaintMapping mapping, PaintProjectionKind shape,
            PaintFit fit, float angle, float scale, Vector2 offset)
        {
            var paint = ResolveDecorationPaintSource(ref reader, in authored, defaultColor, out var rampRow);
            paint.ApplyProjection(ref reader, mapping, shape, fit, angle, scale, offset);
            return new RangeDecorationPaint(in paint, rampRow);
        }

        /// <summary>Resolves only the optional leading paint token or authored paint field.</summary>
        protected TextPaint ResolveDecorationPaintSource(ref ParameterReader reader,
            in PaintRef authored, Color32 defaultColor, out int rampRow)
        {
            TextPaint paint;
            if (reader.TryPeekOptional(out var token) && paintResolver.IsPaintToken(token))
            {
                reader.Next(out _);
                paint = paintResolver.ResolvePaint(token, out rampRow);
            }
            else
            {
                paint = paintResolver.ResolvePaint(in authored, out rampRow);
                if (authored.IsDefault) paint.color = defaultColor;
            }
            return paint;
        }

        /// <summary>Wraps a resolved paint and its owned ramp row for the geometry writer.</summary>
        protected RangeDecorationPaint CreateDecorationPaint(in TextPaint paint, int rampRow)
            => new(in paint, rampRow);

        /// <summary>
        /// Reconciles provider subscriptions and invalidates decoration output after presentation changes.
        /// </summary>
        protected void DecorationPresentationChanged()
        {
            var providerChanged = !ReferenceEquals(subscribedProvider,
                DecorationPaintProvider);
            if (IsInitialized && providerChanged)
            {
                UnsubscribeProvider();
                SubscribeProvider();
            }
            if (providerChanged) paintResolver.MarkSourceDirty();
            outputDirty = true;
            MarkMeshDirty();
        }

        internal RangeDecorationMesh GetMesh(RangeDecorationOrder order)
        {
            ref var handle = ref (order == RangeDecorationOrder.Above ? ref aboveHandle : ref behindHandle);
            handle ??= (decorationHost ?? throw new InvalidOperationException(
                "Range decoration output was requested outside the modifier's active lifecycle."))
                .Acquire(order, priority);
            return handle.Mesh;
        }

        RangeDecorationMesh IRangeDecorationMeshOwner.GetMesh(RangeDecorationOrder order)
            => GetMesh(order);

        private void OnCommitted(UniTextCommitChanges changes)
        {
            if (!outputDirty && (changes & UniTextCommitChanges.GlyphGeometry) == 0) return;
            RebuildDecorations();
            outputDirty = false;
        }

        private void RebuildDecorations()
        {
            ClearOutput();
            paintResolver.ResetResolution();
            if (applications.count == 0) return;

            var writer = new RangeMeshWriter(this);
            try
            {
                for (var i = 0; i < applications.count; i++)
                {
                    var context = new RangeDecorationContext(geometry ?? throw new InvalidOperationException(
                        "Range decoration geometry was rebuilt outside the modifier's active lifecycle."),
                        in applications[i]);
                    OnBuildDecoration(in context, ref writer);
                }
                writer.Complete();
                GradientRampAtlas.Instance.Flush();
                behindHandle?.MarkDirty();
                aboveHandle?.MarkDirty();
            }
            catch
            {
                writer.Complete();
                ClearOutput();
                throw;
            }
        }

        private void SubscribeProvider()
        {
            subscribedProvider = DecorationPaintProvider;
            if (subscribedProvider != null)
                subscribedProvider.Changed += providerChangedCallback;
        }

        private void UnsubscribeProvider()
        {
            if (subscribedProvider != null)
                subscribedProvider.Changed -= providerChangedCallback;
            subscribedProvider = null;
        }

        private void OnPaintProviderChanged(INamedCatalog<PaintSwatch> _,
            in NamedCatalogChange<PaintSwatch> change)
        {
            if (!paintResolver.ApplySourceChange(in change)) return;
            outputDirty = true;
            MarkMeshDirty();
        }

        private void ClearOutput()
        {
            behindHandle?.ExistingGroup?.Clear();
            aboveHandle?.ExistingGroup?.Clear();
        }

        private void HideOutput()
        {
            if (releaseQueued) return;
            ClearOutput();
        }

        private void ReleaseOutput()
        {
            behindHandle?.Dispose();
            aboveHandle?.Dispose();
            behindHandle = null;
            aboveHandle = null;
        }
    }
}
