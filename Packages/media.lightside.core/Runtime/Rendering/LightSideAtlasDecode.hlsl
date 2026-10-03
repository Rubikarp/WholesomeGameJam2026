// The ONE copy of the glyph-atlas addressing, shared by every pipeline (built-in + URP) and every
// consumer (base SDF shaders + custom-effect preludes). Pipeline-agnostic: Texture2D.Load is plain
// HLSL, compiles identically in CGPROGRAM and HLSLPROGRAM and cross-compiles to texelFetch on
// GLSL ES 3.00 (core in the vertex stage).
//
// Addressing contract:
//   UV0.z = stable glyph HANDLE (allocated once per atlas entry, unchanged across pad-tier upgrades,
//   tile-size upgrades and compaction). The handle indexes _LightSideGlyphTable — one RGBA32F texel per
//   glyph holding the atlas transform (isotropic scale, offset.xy, page layer), produced by
//   GlyphAtlas.WriteTransformRow (the single owner of the placement math). Relocations rewrite the
//   texel; meshes are never patched. Color quads bypass the table: normalized atlas UV in UV0.xy,
//   page layer in UV0.z, UV0.w == 0.

#ifndef LIGHTSIDE_ATLAS_DECODE_INCLUDED
#define LIGHTSIDE_ATLAS_DECODE_INCLUDED

#define LIGHTSIDE_SDF_PAD 0.5

// Glyph transform table — global (Shader.SetGlobalTexture), RGBA32F, FilterMode.Point (a hard
// requirement: float32 textures are non-filterable on the GLES3/WebGL2 floor and any other filter
// state makes the texture incomplete). Load-only — no SamplerState, no derivative rules, safe in
// the vertex stage.
// SYNC: width mirrors GlyphTransformTable.Width.
#define LIGHTSIDE_GLYPH_TABLE_WIDTH 1024.0
Texture2D<float4> _LightSideGlyphTable;

// Per-glyph render mode, packed by the mesh generator into UV1.w = intraX + 2*mode:
//   0 = SDF (_LightSideGlyphSdf, RHalf), 1 = MSDF (_LightSideGlyphMsdf, RGBAHalf), 2 = color (_LightSideGlyphColor, RGBA32 sRGB + mips).
// Quad-constant by construction (all four corners carry the same mode), so interpolation of UV1.w
// stays inside [2*mode, 2*mode + 1] and both halves decode exactly at any pixel.
float LightSideGlyphMode(float uv1w)
{
    return floor(uv1w * 0.5);
}

// The intra-glyph X fraction (0 left edge, 1 right edge) that shares UV1.w with the mode.
float LightSideIntraX(float uv1w)
{
    return uv1w - 2.0 * floor(uv1w * 0.5);
}

// The glyph's atlas transform: atlasUV = glyphUV * x + yz on page layer w. Vertex-stage fetch;
// the handle is fp32-exact and non-negative.
float4 LightSideLoadGlyphTransform(float handle)
{
    float row = floor(handle / LIGHTSIDE_GLYPH_TABLE_WIDTH);
    float col = handle - row * LIGHTSIDE_GLYPH_TABLE_WIDTH;
    return _LightSideGlyphTable.Load(int3(col, row, 0));
}

// Color-atlas mip selection — computed explicitly and clamped to the written mip window.
// The color atlas carries a truncated, tile-aligned mip chain; on Vulkan/Metal the physical
// image holds a full chain whose tail levels are never written, and the sampler's own LOD
// clamp cannot be relied on across backends — an unclamped minified fetch blends unwritten
// (transparent-black) levels and fades the glyph. _LightSideColorMaxLod = written mips - 1,
// set globally by GlyphAtlas at color-storage creation. The -0.5 sharpen bias lives here
// (explicit-LOD sampling bypasses the texture's own mipMapBias).
// SYNC: 2048.0 mirrors GlyphAtlas.PageSize.
float _LightSideColorMaxLod;

float LightSideColorLod(float2 dx, float2 dy)
{
    float2 px = dx * 2048.0;
    float2 py = dy * 2048.0;
    float d = max(dot(px, px), dot(py, py));
    return min(0.5 * log2(max(d, 1e-12)) - 0.5, _LightSideColorMaxLod);
}

// Uniform atlas-UV entry for the base shaders and custom preludes, driven by the per-glyph mode
// (LightSideGlyphMode of UV1.w): SDF/MSDF glyphs resolve through the transform table, color quads
// carry normalized UV + page layer directly in UV0.xyz. Vertex-stage only.
float3 LightSideComputeAtlasUV(float4 texcoord0, float4 texcoord1)
{
    if (LightSideGlyphMode(texcoord1.w) > 1.5)
        return texcoord0.xyz;
    float4 t = LightSideLoadGlyphTransform(texcoord0.z);
    return float3(texcoord0.xy * t.x + t.yz, t.w);
}

#endif
