using System;

namespace LightSide
{
    internal sealed class SelectionHighlightLayer : IRangeDecorationMeshOwner, IDisposable
    {
        private readonly UniTextBase text;
        private readonly RangeGeometryIndex geometry;
        private readonly RangeDecorationHost decorationHost;
        private readonly PaintResolver paintResolver = new();
        private readonly HighlightDecorationBuilder builder = new();
        private HighlightPresentation presentation;
        private RangeDecorationHandle behindHandle;
        private RangeDecorationHandle aboveHandle;
        private IPaintProvider subscribedProvider;
        private int start;
        private int end;
        private int geometryVersion = -1;
        private bool hasRange;
        private bool disposed;

        public bool IsAlive => !disposed;

        public HighlightPresentation Presentation
        {
            get => presentation;
            set
            {
                ThrowIfDisposed();
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (ReferenceEquals(presentation, value)) return;
                presentation.Changed -= OnPresentationChanged;
                presentation = value;
                presentation.Changed += OnPresentationChanged;
                ReconcileProvider();
                Repaint();
            }
        }

        public SelectionHighlightLayer(UniTextBase text, HighlightPresentation presentation)
        {
            this.text = text ?? throw new ArgumentNullException(nameof(text));
            this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            geometry = RangeGeometryIndex.For(text);
            geometry.Retain();
            decorationHost = RangeDecorationHost.For(text);
            presentation.Changed += OnPresentationChanged;
            ReconcileProvider();
        }

        public void SetRange(int valueStart, int valueEnd)
        {
            ThrowIfDisposed();
            if (valueEnd <= valueStart)
            {
                Clear();
                return;
            }
            if (hasRange && start == valueStart && end == valueEnd &&
                geometryVersion == geometry.Version) return;
            start = valueStart;
            end = valueEnd;
            hasRange = true;
            Repaint();
        }

        public void Clear()
        {
            if (disposed) return;
            hasRange = false;
            ClearMesh();
            paintResolver.ResetResolution();
        }

        public void Repaint()
        {
            if (disposed) return;
            ClearMesh();
            paintResolver.ResetResolution();
            if (!hasRange) return;

            paintResolver.PrepareForParallel(presentation.Provider);
            var paint = paintResolver.ResolvePaint(presentation.Paint, out var rampRow);
            if (presentation.Paint.IsDefault) paint.color = new UnityEngine.Color32(51, 128, 255, 102);
            var reader = new ParameterReader(string.Empty);
            var resolved = presentation.Resolve(ref reader, paint, text.CurrentFontSize);

            var writer = new RangeMeshWriter(this);
            try
            {
                var emitted = builder.Build(geometry, start, end, text.CurrentFontSize,
                    in resolved, rampRow, ref writer);
                writer.Complete();
                GradientRampAtlas.Instance.Flush();
                geometryVersion = geometry.Version;
                if (emitted) Handle(resolved.order).MarkDirty();
            }
            catch
            {
                writer.Complete();
                ClearMesh();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            presentation.Changed -= OnPresentationChanged;
            UnsubscribeProvider();
            paintResolver.Return();
            builder.Dispose();
            behindHandle?.Dispose();
            aboveHandle?.Dispose();
            geometry.Release();
            presentation = null;
        }

        RangeDecorationMesh IRangeDecorationMeshOwner.GetMesh(RangeDecorationOrder order)
            => Handle(order).Mesh;

        private RangeDecorationHandle Handle(RangeDecorationOrder order)
        {
            ref var handle = ref (order == RangeDecorationOrder.Above ? ref aboveHandle : ref behindHandle);
            return handle ??= decorationHost.Acquire(order, RangeDecorationPriorities.Selection);
        }

        private void OnPresentationChanged(IStateChangeSource _, in StateChange __)
        {
            ReconcileProvider();
            Repaint();
        }

        private void ReconcileProvider()
        {
            var provider = presentation.Provider;
            if (ReferenceEquals(subscribedProvider, provider)) return;
            UnsubscribeProvider();
            subscribedProvider = provider;
            if (subscribedProvider != null) subscribedProvider.Changed += OnProviderChanged;
            paintResolver.MarkSourceDirty();
        }

        private void UnsubscribeProvider()
        {
            if (subscribedProvider != null) subscribedProvider.Changed -= OnProviderChanged;
            subscribedProvider = null;
        }

        private void OnProviderChanged(INamedCatalog<PaintSwatch> _,
            in NamedCatalogChange<PaintSwatch> change)
        {
            if (!paintResolver.ApplySourceChange(in change)) return;
            Repaint();
        }

        private void ClearMesh()
        {
            behindHandle?.ExistingGroup?.ExistingDecorationMesh?.Clear();
            aboveHandle?.ExistingGroup?.ExistingDecorationMesh?.Clear();
            behindHandle?.MarkDirty();
            aboveHandle?.MarkDirty();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SelectionHighlightLayer));
        }
    }
}
