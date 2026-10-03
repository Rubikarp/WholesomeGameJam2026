using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Publishes the glyph atlases and hands out the shared surface materials text renders through.
    /// </summary>
    /// <remarks>
    /// The materials themselves are process-wide and shared with every other LightSide package
    /// (<see cref="LightSideMaterials"/>) — text batches with shapes and vector animation because they are
    /// literally the same material object. The per-glyph mode in UV1.w selects among the three atlas
    /// samplers, each published globally by its own <see cref="AtlasGlobalBinding"/> so no material carries
    /// an atlas in its identity.
    /// </remarks>
    internal static class UniTextMaterialCache
    {
        private static readonly AtlasGlobalBinding sdfAtlas = new(LightSideShaderIds.GlyphSdf);
        private static readonly AtlasGlobalBinding msdfAtlas = new(LightSideShaderIds.GlyphMsdf);
        private static readonly AtlasGlobalBinding colorAtlas = new(LightSideShaderIds.GlyphColor);

        /// <summary>Canvas (UI) text material — the shared LightSide surface instance.</summary>
        public static Material Text => LightSideMaterials.Ui;

        /// <summary>
        /// World-space text material: the unlit depth-tested world shader, or the lit one with
        /// <c>_LightInfluence</c> at 1. Lit and unlit are separate shaders, so two adjacent components
        /// can independently receive or ignore scene light and an all-unlit project ships no lighting
        /// variants. Throws <see cref="System.InvalidOperationException"/> when the required shader is absent.
        /// </summary>
        public static Material TextWorld(bool lit)
        {
            var mat = LightSideMaterials.World(lit);
            if (lit && mat.GetFloat(ShaderIds.Lit.LightInfluence) <= 0f)
                mat.SetFloat(ShaderIds.Lit.LightInfluence, 1f);
            return mat;
        }

        /// <summary>
        /// Keeps the three atlas globals current: subscribes each sampler's binding to the live atlas
        /// instance of its mode. Instances appear lazily (first glyph of that mode), so this is re-checked
        /// on every mesh apply — a no-op when nothing changed.
        /// </summary>
        internal static void EnsureAtlasSubscription()
        {
            sdfAtlas.EnsureSubscription(GlyphAtlas.TryGetExistingInstance(UniTextRenderMode.SDF, out var sdf) ? sdf : null);
            msdfAtlas.EnsureSubscription(GlyphAtlas.TryGetExistingInstance(UniTextRenderMode.MSDF, out var msdf) ? msdf : null);
            colorAtlas.EnsureSubscription(GlyphAtlas.Color);
        }
    }
}
