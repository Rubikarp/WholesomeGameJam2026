using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Base for phase-driven glyph modifiers: resolves each range's parameters through the cascade
    /// into a <typeparamref name="TParams"/> set and hands it, with the range's resolved phase, to
    /// <see cref="OnGlyph(UniTextMeshGenerator, int, in TParams, float)"/> for every rendered glyph
    /// the range covers. Renders one exact state per phase value; never animates on its own.
    /// Parameters re-resolve before every mesh rebuild, so driving them re-renders the mesh without
    /// re-applying ranges.
    /// </summary>
    /// <remarks>
    /// Ranges of every modifier sharing the key land in one per-cluster axis — a cluster renders the
    /// parameter set of the range applied last, and the glyph pass runs once for it.
    /// </remarks>
    [Serializable]
    [GenerateParameters]
    public abstract partial class GlyphParamModifier<TParams> : PooledAttributeModifier<byte> where TParams : unmanaged
    {
        /// <summary>Animation input in abstract time units. Wholly external — a driver, tween, or Animator writes it; the modifier only renders it. Setting it costs nothing while no range is active.</summary>
        [SerializeField, SlotlessParameter, StateProperty(nameof(MarkParamsDirty))]
        private float phase;

        [NonSerialized] private Channel pass;

        /// <summary>
        /// Raises the invalidation of a parameter the per-rebuild refresh re-resolves: the mesh
        /// regenerates from the current values without re-applying ranges. Every
        /// <typeparamref name="TParams"/> field's state callback points here.
        /// </summary>
        protected void MarkParamsDirty() => MarkRenderDirty(pass is { HasRanges: true });

        /// <summary>Key for the per-cluster parameter-set index buffer (see <see cref="AttributeKeys"/>). Unique per modifier type.</summary>
        protected abstract override string AttributeKey { get; }

        protected sealed override AttributeChannel CreateChannel() => new Channel();

        protected override void OnEnable()
        {
            base.OnEnable();
            pass = (Channel)SharedChannel;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            pass = null;
        }

        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            pass?.RefreshEntries(this);
        }

        protected sealed override void OnApply(in RangeApplyContext context)
        {
            var paramIndex = pass.AddEntry(this, in context);

            var start = context.Segment.Range.start;
            var end = context.Segment.Range.End;
            var actualEnd = Math.Min(end, buffers.codepoints.count);
            var buffer = attribute.buffer.data;
            for (var i = start; i < actualEnd; i++)
                buffer[i] = paramIndex;
        }

        /// <summary>Resolves this range's parameter set through the cascade. Runs on the main thread,
        /// at range application and again before every mesh rebuild — implementations read state and
        /// cause no side effects.</summary>
        protected abstract TParams ResolveParams(in RangeApplyContext context);

        /// <summary>Transforms the current glyph quad for the resolved parameter set and phase. May run on a worker thread — no Unity API calls.</summary>
        protected abstract void OnGlyph(UniTextMeshGenerator gen, int cluster, in TParams p, float phase);

        private sealed class Channel : AttributeChannel
        {
            private struct Entry
            {
                public GlyphParamModifier<TParams> owner;
                public RangeApplyMemo memo;
                public TParams p;
                public float phase;
            }

            private readonly PooledList<Entry> entries = new();
            private PooledArrayAttribute<byte> attribute;
            private Action onGlyphCallback;

            internal bool HasRanges => entries.Count > 0;

            private static void Resolve(ref Entry entry, in RangeApplyContext context)
            {
                entry.p = entry.owner.ResolveParams(in context);
                entry.phase = Param.Phase.Resolve(entry.owner, in context);
            }

            /// <summary>Stores one range's retained context with its resolved values and returns the per-cluster index, saturating at 255.</summary>
            internal byte AddEntry(GlyphParamModifier<TParams> owner, in RangeApplyContext context)
            {
                var index = entries.Count;
                var entry = new Entry { owner = owner, memo = context.Retain() };
                Resolve(ref entry, in context);
                entries.Add(entry);
                return (byte)Math.Min(index + 1, byte.MaxValue);
            }

            /// <summary>Re-resolves one writer's stored entries from their retained contexts, so parameter and phase changes reach the mesh without a re-apply.</summary>
            internal void RefreshEntries(GlyphParamModifier<TParams> owner)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    ref var entry = ref entries[i];
                    if (!ReferenceEquals(entry.owner, owner)) continue;
                    var context = entry.memo.ToContext();
                    Resolve(ref entry, in context);
                }
            }

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(Key);

                entries.FakeClear();
                onGlyphCallback ??= OnGlyph;
                uniText.MeshGenerator.onGlyph.Subscribe(onGlyphCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);
                attribute?.buffer.data?.AsSpan().Clear();
            }

            protected override void OnBeginCycle() => entries.FakeClear();

            protected override void OnRelease()
            {
                entries.Return();
                attribute = null;
            }

            private void OnGlyph()
            {
                var gen = uniText.MeshGenerator;
                var buffer = attribute.buffer.data;
                var cluster = gen.currentCluster;
                if (buffer == null || (uint)cluster >= (uint)buffer.Length) return;

                int paramIndex = buffer[cluster];
                if (paramIndex == 0 || paramIndex > entries.Count) return;

                ref readonly var entry = ref entries[paramIndex - 1];
                entry.owner.OnGlyph(gen, cluster, in entry.p, entry.phase);
            }
        }
    }
}
