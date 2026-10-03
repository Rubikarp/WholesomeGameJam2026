using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>
    /// Base class for modifiers that store per-glyph attribute data.
    /// </summary>
    /// <typeparam name="T">Attribute value type (must be unmanaged).</typeparam>
    /// <remarks>
    /// <para>
    /// GlyphModifier provides automatic buffer management for per-glyph attributes.
    /// Derived classes implement <see cref="GetOnGlyphCallback"/> to receive callbacks
    /// during mesh generation, where they can modify vertex data based on attribute values.
    /// </para>
    /// <para>
    /// Optionally override <see cref="GetOnShapedCallback"/> to also receive a callback
    /// after shaping completes, for modifying glyph advances or run widths.
    /// </para>
    /// </remarks>
    /// <seealso cref="BaseModifier"/>
    [Serializable]
    public abstract class GlyphModifier<T> : PooledAttributeModifier<T> where T : unmanaged
    {
        protected sealed override AttributeChannel CreateChannel() => new Channel();

        protected sealed override void OnEnable() => base.OnEnable();

        protected sealed override void OnDisable() => base.OnDisable();

        protected sealed override void OnDestroy() => base.OnDestroy();

        protected sealed override void OnApply(in RangeApplyContext context) => DoApply(in context);

        /// <summary>
        /// Implements the modifier's effect on one entity segment. The attribute buffer is
        /// guaranteed to have sufficient capacity when this is called.
        /// </summary>
        /// <param name="context">Synchronous immutable application context.</param>
        protected abstract void DoApply(in RangeApplyContext context);

        protected abstract Action GetOnGlyphCallback();

        /// <summary>
        /// Returns a callback to invoke after shaping completes.
        /// Override to modify glyph advances or run widths.
        /// Returns null by default (no shaped callback).
        /// Shaping runs only in a full pass, while the apply cycle also replays without it, so a value
        /// this callback refines into the attribute buffer is erased by the next replay; keep such a
        /// refinement in a buffer the apply cycle does not fill.
        /// </summary>
        protected virtual Action GetOnShapedCallback() => null;

        /// <summary>
        /// Runs one writer's callbacks over the buffer every writer of the key merged into, so the
        /// glyph pass fires once per glyph however many modifiers share the key.
        /// </summary>
        private sealed class Channel : AttributeChannel
        {
            private PooledArrayAttribute<T> attribute;
            private GlyphModifier<T> owner;
            private Action glyphCallback;
            private Action shapedCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<T>>(Key);
                Subscribe();
            }

            protected override void OnDeactivate()
            {
                Unsubscribe();
                attribute?.buffer.data?.AsSpan().Clear();
            }

            protected override void OnProviderChanged()
            {
                Unsubscribe();
                Subscribe();
            }

            protected override void OnRelease()
            {
                attribute = null;
                owner = null;
                glyphCallback = null;
                shapedCallback = null;
            }

            private void Subscribe()
            {
                var provider = (GlyphModifier<T>)Provider;
                if (!ReferenceEquals(owner, provider))
                {
                    owner = provider;
                    glyphCallback = provider.GetOnGlyphCallback();
                    shapedCallback = provider.GetOnShapedCallback();
                }

                uniText.MeshGenerator.onGlyph.Subscribe(glyphCallback);
                if (shapedCallback != null)
                    uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            }

            private void Unsubscribe()
            {
                uniText.MeshGenerator.onGlyph.Unsubscribe(glyphCallback);
                if (shapedCallback != null)
                    uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            }
        }
    }


    /// <summary>
    /// Extension methods for working with modifier attribute buffers.
    /// </summary>
    public static class ModifierBufferExtensions
    {
        /// <summary>Checks if a byte flag is set at the specified index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlag(this byte[] buffer, int index)
        {
            return buffer != null && (uint)index < (uint)buffer.Length && buffer[index] != 0;
        }

        /// <summary>Checks if any flags are set in the buffer.</summary>
        public static bool HasAnyFlags(this byte[] buffer)
        {
            if (buffer == null) return false;
            var len = buffer.Length;
            var i = 0;
            var limit = len - 7;
            for (; i < limit; i += 8)
                if (buffer[i] != 0 || buffer[i + 1] != 0 || buffer[i + 2] != 0 || buffer[i + 3] != 0 ||
                    buffer[i + 4] != 0 || buffer[i + 5] != 0 || buffer[i + 6] != 0 || buffer[i + 7] != 0)
                    return true;
            for (; i < len; i++)
                if (buffer[i] != 0)
                    return true;
            return false;
        }

        /// <summary>Checks if a ushort flag is set at the specified index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlag(this ushort[] buffer, int index)
        {
            return buffer != null && (uint)index < (uint)buffer.Length && buffer[index] != 0;
        }

        /// <summary>Checks if any ushort flags are set in the buffer.</summary>
        public static bool HasAnyFlags(this ushort[] buffer)
        {
            if (buffer == null) return false;
            for (var i = 0; i < buffer.Length; i++)
                if (buffer[i] != 0)
                    return true;
            return false;
        }

        /// <summary>Sets byte flags for a range of indices.</summary>
        public static void SetFlagRange(this byte[] buffer, int start, int end)
        {
            if (buffer == null) return;
            var len = buffer.Length;
            if (start < 0) start = 0;
            if (end > len) end = len;
            for (var i = start; i < end; i++)
                buffer[i] = 1;
        }

        /// <summary>Checks if a uint value is non-zero at the specified index.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasValue(this uint[] buffer, int index)
        {
            return buffer != null && (uint)index < (uint)buffer.Length && buffer[index] != 0;
        }

        /// <summary>Gets a uint value or returns 0 if out of bounds.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetValueOrDefault(this uint[] buffer, int index)
        {
            if (buffer == null || (uint)index >= (uint)buffer.Length)
                return 0;
            return buffer[index];
        }

        /// <summary>Sets uint values for a range of indices.</summary>
        public static void SetValueRange(this uint[] buffer, int start, int end, uint value)
        {
            if (buffer == null) return;
            var len = buffer.Length;
            if (start < 0) start = 0;
            if (end > len) end = len;
            for (var i = start; i < end; i++)
                buffer[i] = value;
        }

    }

}
