using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies color to text ranges using hex codes or named colors.
    /// </summary>
    /// <remarks>
    /// Parameter: a color value in hex or by name.
    ///
    /// Supported formats:
    /// - Hex: #RGB, #RRGGBB, #RRGGBBAA
    /// - Named colors: white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold
    ///
    /// The alpha channel of the color parameter is multiplied with the component's base alpha (per W3C alpha compositing). A fully opaque parameter inherits the component alpha; a semi-transparent parameter scales it further.
    /// </remarks>
    /// <seealso cref="ParseRule"/>
    [Serializable]
    [TypeGroup("Appearance", 2)]
    [TypeDescription("Changes the color of the text.")]
    [GenerateParameters]
    public partial class ColorModifier : GlyphModifier<uint>, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;
        /// <summary>Default text colour used when a range does not override it.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))]
        private Color32 color = new(255, 255, 255, 255);

        protected override string AttributeKey => AttributeKeys.Color;

        protected override Action GetOnGlyphCallback()
        {
            return OnGlyph;
        }

        protected override void DoApply(in RangeApplyContext context)
        {
            var resolved = Param.Color.Resolve(this, in context);
            var packed = PackColor(resolved);
            attribute.FillRange(context.Segment.Range, packed);
        }

        private void OnGlyph()
        {
            var gen = uniText.MeshGenerator;

            if (gen.font.IsColor) return;

            var buffer = attribute.buffer.data;
            var cluster = gen.currentCluster;
            var packed = buffer.GetValueOrDefault(cluster);
            if (packed == 0)
                return;

            var color = UnpackColor(packed);
            color.a = (byte)((color.a * gen.defaultColor.a + 127) / 255);
            var baseIdx = gen.faceBaseIdx;
            var colors = gen.Colors;

            colors[baseIdx] = color;
            colors[baseIdx + 1] = color;
            colors[baseIdx + 2] = color;
            colors[baseIdx + 3] = color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint PackColor(Color32 c)
        {
            var a = c.a == 0 ? (byte)1 : c.a;
            return ((uint)a << 24) | ((uint)c.r << 16) | ((uint)c.g << 8) | c.b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Color32 UnpackColor(uint packed)
        {
            return new Color32(
                (byte)((packed >> 16) & 0xFF),
                (byte)((packed >> 8) & 0xFF),
                (byte)(packed & 0xFF),
                (byte)((packed >> 24) & 0xFF)
            );
        }

        /// <summary>
        /// Tries to get the custom color for a cluster from the specified buffers.
        /// </summary>
        /// <param name="buffers">The UniText buffers containing color attribute data.</param>
        /// <param name="cluster">The cluster index to look up.</param>
        /// <param name="color">The color if found.</param>
        /// <returns>True if a custom color exists for the cluster; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetColor(UniTextBuffers buffers, int cluster, out Color32 color)
        {
            var buffer = buffers?.GetAttributeData<PooledArrayAttribute<uint>>(AttributeKeys.Color)?.buffer.data;
            var packed = buffer.GetValueOrDefault(cluster);
            if (packed == 0)
            {
                color = default;
                return false;
            }

            color = UnpackColor(packed);
            return true;
        }

    }
}
