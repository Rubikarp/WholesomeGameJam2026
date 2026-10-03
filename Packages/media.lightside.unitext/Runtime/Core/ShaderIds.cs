using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Cached <see cref="Shader.PropertyToID"/> constants for every shader property the engine
    /// touches from C#, grouped by shader family — the single home for property names so ids
    /// never drift between call sites.
    /// </summary>
    internal static class ShaderIds
    {
        /// <summary>Glyph-atlas globals. The samplers themselves live in <see cref="LightSideShaderIds"/>; this is the sampling parameter UniText owns.</summary>
        internal static class Atlas
        {
            public static readonly int ColorMaxLod = Shader.PropertyToID("_LightSideGlyphColorMaxLod");
        }

        /// <summary>Lit text surface shaders.</summary>
        internal static class Lit
        {
            public static readonly int LightInfluence = Shader.PropertyToID("_LightInfluence");
        }

        /// <summary>User materials cloned by <see cref="MaterialModifier"/>.</summary>
        internal static class Custom
        {
            public static readonly int MeshPadding = Shader.PropertyToID("_LightSideMeshPadding");
        }

        /// <summary>Global glyph transform table binding (<c>Shader.SetGlobalTexture</c>).</summary>
        internal static class GlyphTable
        {
            public static readonly int Table = Shader.PropertyToID("_LightSideGlyphTable");
        }

        /// <summary>Editor font-atlas preview shader.</summary>
        internal static class AtlasPreview
        {
            public static readonly int SliceIndex = Shader.PropertyToID("_SliceIndex");
            public static readonly int Mode = Shader.PropertyToID("_Mode");
            public static readonly int Rendered = Shader.PropertyToID("_Rendered");
        }
    }
}
