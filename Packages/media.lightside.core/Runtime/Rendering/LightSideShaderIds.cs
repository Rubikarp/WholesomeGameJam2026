using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shader property names and ids shared across LightSide rendering packages — the cross-package
    /// binding contract, so text, shapes and vector animation can be sampled by one material.
    /// </summary>
    /// <remarks>
    /// Everything named here except <see cref="PaintTexture"/> is published with
    /// <see cref="Shader.SetGlobalTexture(int, Texture)"/> and must NOT appear in a shader's
    /// <c>Properties</c> block: a material-level value shadows the global and, worse, puts the texture
    /// into the material's identity, which splits Canvas batches.
    /// </remarks>
    public static class LightSideShaderIds
    {
        public const string GlyphSdfName = "_LightSideGlyphSdf";
        public const string GlyphMsdfName = "_LightSideGlyphMsdf";
        public const string GlyphColorName = "_LightSideGlyphColor";
        public const string LottieAtlasName = "_LightSideLottieAtlas";
        public const string ShapeVerticesName = "_LightSideShapeVertices";
        public const string ShapeVertexRowsName = "_LightSideShapeVertexRows";
        public const string PaintTextureName = "_LightSidePaintTexture";

        /// <summary>Keyword enabling the texture branch of paint resolve. Shared by every LightSide surface shader.</summary>
        public const string PaintTextureKeyword = "LIGHTSIDE_PAINT_TEXTURE";

        public static readonly int GlyphSdf = Shader.PropertyToID(GlyphSdfName);
        public static readonly int GlyphMsdf = Shader.PropertyToID(GlyphMsdfName);
        public static readonly int GlyphColor = Shader.PropertyToID(GlyphColorName);
        public static readonly int LottieAtlas = Shader.PropertyToID(LottieAtlasName);
        public static readonly int ShapeVertices = Shader.PropertyToID(ShapeVerticesName);
        public static readonly int ShapeVertexRows = Shader.PropertyToID(ShapeVertexRowsName);

        /// <summary>Per-material paint texture — the one binding here that is deliberately material-level, because its value differs per element.</summary>
        public static readonly int PaintTexture = Shader.PropertyToID(PaintTextureName);
    }
}
