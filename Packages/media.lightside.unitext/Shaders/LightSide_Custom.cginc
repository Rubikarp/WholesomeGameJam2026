// LightSide_Custom.cginc — prelude for user-authored LightSide material shaders.
//
// Contract (enforced automatically when you include this file):
//   * _LightSideGlyphSdf (SDF), _LightSideGlyphMsdf (MSDF) and _LightSideGlyphColor (color) are declared as 2DArrays and
//     published as GLOBAL shader bindings — never declare them in a Properties block, a material-level
//     value shadows the global and pins the texture into the material's identity, splitting batches.
//     The per-glyph mode in the vertex stream (UV1.w, see LightSideGlyphMode) selects which one a fragment
//     samples. There are NO atlas-mode keywords: one material and one variant serve SDF, MSDF and color alike.
//   * Standard Canvas UI clip/mask helpers are provided (ComputeMask, ApplyClipping).
//
// Canvas vs World: this file serves both built-in-pipeline contexts. World shells (LightSideWorld,
// rendered through WorldBatcher) must `#define LIGHTSIDE_WORLD` BEFORE the include — it swaps
// the Canvas clip-mask interpolator for real world position (LightSideFrag.positionWS) + scene fog
// support, matching the URP world prelude. Without the define you get the Canvas layout
// (mask filled, positionWS = 0).
//
// Required Properties block in your .shader (the glyph atlases are global — they belong here NOT at all):
//   _ClipRect     ("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
//   _MaskSoftnessX("Mask SoftnessX", float) = 0
//   _MaskSoftnessY("Mask SoftnessY", float) = 0
//   _Stencil*     (stencil properties for UI masks)
//   _ColorMask    ("Color Mask", Float) = 15
//
// Required SubShader Tags:
//   "Queue"="Transparent", "IgnoreProjector"="True", "RenderType"="Transparent"
//
// Required Pass pragmas:
//   Canvas passes:
//   #pragma multi_compile __ UNITY_UI_CLIP_RECT
//   #pragma multi_compile __ UNITY_UI_ALPHACLIP
//   World passes:
//   #pragma multi_compile_fog
//
// Per-vertex custom data — TEXCOORD2/3 semantics (two disjoint vertex streams, never both on one quad):
//   * MaterialModifier sub-mesh quads (the stream every custom shader bound through MaterialModifier
//     receives): TEXCOORD2/3 = user channels A/B (constant-from-inspector / delegate / subclass
//     override). Paint layers (stroke/shadow/gradient) on the same text range render as SEPARATE
//     base-mesh quads through the base SDF shader — they never write into the custom sub-mesh.
//   * Base-mesh quads (only if a custom shader is used as the base text material): TEXCOORD2 =
//     (coverageMode, p0, p1, softness), TEXCOORD3 = paint — read with LightSideCoverage(...) helpers.
//   Base-mesh vertex channels (uv0-uv3 + color) and the prelude interpolators are fully claimed
//   (meta.w carries the packed tileHash) — per-glyph custom effect data must ride the
//   MaterialModifier user channels, or be produced in the LIGHTSIDE_EFFECT_VERT hook (see below).
//
// Color space — wrap v.color in LightSideGammaToLinearIfNeeded(v.color). Canvas owns the
// vertex-color stream and converts sRGB->linear on the CPU side; the helper handles the
// Canvas opt-in (_UIVertexColorAlwaysGammaSpace) where the conversion is deferred to the
// shader. Both face and effect (outline / shadow / extrude) quads ship their colour in
// v.color — effect quads have alpha pre-multiplied by face alpha on the CPU side, so face
// alpha modulation flows through automatically.

#ifndef LightSide_CUSTOM_INCLUDED
#define LightSide_CUSTOM_INCLUDED

#include "UnityCG.cginc"
#include "UnityUI.cginc"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideAtlasDecode.hlsl"

// Glyph atlases — all three bound at runtime; the per-glyph vertex-stream mode selects one.
UNITY_DECLARE_TEX2DARRAY(_LightSideGlyphSdf);   // SDF  (RHalf)
UNITY_DECLARE_TEX2DARRAY(_LightSideGlyphMsdf);   // MSDF (RGBAHalf)
UNITY_DECLARE_TEX2DARRAY(_LightSideGlyphColor);  // Color (RGBA32 sRGB + mips)

float4 _ClipRect;
float _MaskSoftnessX;
float _MaskSoftnessY;
float _UIMaskSoftnessX;
float _UIMaskSoftnessY;
int _UIVertexColorAlwaysGammaSpace;

// ============================================================================
// Vertex input layout written by LightSide mesh generator.
// Matches EffectModifier / base SDF contract exactly.
//
// IMPORTANT — v.vertex.xy is NOT a size/position-invariant coord.
//   * Canvas path:  v.vertex.xy is in RectTransform-local UI space (starts near 0,0).
//   * World path:   WorldBatcher combines many LightSideWorld components into one mesh
//                   and pre-transforms their vertices into the batcher's local space, so
//                   v.vertex.xy ends up shifted by each component's world position.
// If you need a per-glyph identifier stable between Canvas/World and independent of text
// size/position, use the fragment's `i.tileId` / `i.tileHash` (or raw `v.texcoord0.z` in a vertex
// hook) — it is the glyph's stable atlas handle, unchanged across pad-tier growth, tile-size
// upgrades and compaction; `v.texcoord0.xy` (glyph-local UV) is also stable.
// See Rainbow/Dissolve/Hologram examples.
// ============================================================================
struct LightSide_appdata
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 vertex    : POSITION;
    float3 normal    : NORMAL;
    fixed4 color     : COLOR;
    float4 texcoord0 : TEXCOORD0; // xy = glyph UV (color: normalized atlas UV), z = encoded tile id (color: page layer), w = glyphH (color: 0)
    float4 texcoord1 : TEXCOORD1; // x = aspect, y = faceDilate, z = cluster index, w = intra-glyph X fraction (0..1) + 2*glyphMode (LightSideGlyphMode)
    float4 texcoord2 : TEXCOORD2; // user channel A (MaterialModifier constant / delegate / override)
    float4 texcoord3 : TEXCOORD3; // user channel B
};

// ============================================================================
// Atlas UV transform — LightSideComputeAtlasUV lives in LightSideAtlasDecode.hlsl (one shared copy).
//
// For SDF/MSDF glyphs the mesh generator writes `encodedTile` and `glyphH` into UV0.zw, so the
// "atlas-local UV" has to be transformed by LightSideComputeAtlasUV before sampling. Color quads
// carry normalized atlas UV directly in UV0.xy and the page layer in UV0.z;
// LightSideComputeAtlasUV branches on the vertex-stream mode so you can call it uniformly.
// ============================================================================

#define LIGHTSIDE_DILATE_SCALE LIGHTSIDE_SDF_PAD

// ============================================================================
// Atlas sampling — every fetch is gradient-explicit: the per-glyph mode branch is quad-constant,
// and implicit-derivative fetches inside it would force the compiler to flatten it into three
// samples per fragment.
//   LightSideSampleAtlas — raw RGBA texel of the glyph's own atlas (color: the color tile;
//                        SDF/MSDF: the raw distance texel).
//   LightSideSampleSdf   — signed distance in [-0.5, +0.5] (0 for color — no distance field there).
// ============================================================================

#define LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(uv, dx, dy) _LightSideGlyphSdf.SampleGrad(sampler_LightSideGlyphSdf, (uv), (dx), (dy))
#define LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(uv, dx, dy) _LightSideGlyphMsdf.SampleGrad(sampler_LightSideGlyphMsdf, (uv), (dx), (dy))
#define LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(uv, dx, dy) _LightSideGlyphColor.SampleLevel(sampler_LightSideGlyphColor, (uv), LightSideColorLod((dx), (dy)))
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideSdfSample.hlsl"

half4 LightSideSampleAtlas(float3 atlasUV, float glyphMode)
{
    float2 dx = ddx(atlasUV.xy);
    float2 dy = ddy(atlasUV.xy);
    half4 result = 0.0;
    if (glyphMode > 1.5)
        result = LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(atlasUV, dx, dy);
    else if (glyphMode < 0.5)
        result = LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(atlasUV, dx, dy);
    else
        result = LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(atlasUV, dx, dy);
    return result;
}

float LightSideSampleSdf(float3 atlasUV, float glyphMode)
{
    if (glyphMode > 1.5)
        return 0.0;
    float2 dx = ddx(atlasUV.xy);
    float2 dy = ddy(atlasUV.xy);
    return SAMPLE_SDF2_MODE_GRAD(glyphMode, atlasUV, dx, dy).x;
}

// ============================================================================
// Gamma, clipping, masking. Matches base LightSide behaviour — call these from your vert/frag.
// ============================================================================

fixed4 LightSideGammaToLinearIfNeeded(fixed4 color)
{
    if (_UIVertexColorAlwaysGammaSpace && !IsGammaSpace())
        color.rgb = UIGammaToLinear(color.rgb);
    return color;
}

half4 LightSideComputeMask(float4 vert, float2 pixelSize)
{
    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
    half2 maskSoftness = half2(max(_UIMaskSoftnessX, _MaskSoftnessX),
                               max(_UIMaskSoftnessY, _MaskSoftnessY));
    return half4(vert.xy * 2 - clampedRect.xy - clampedRect.zw,
                 0.25 / (0.25 * maskSoftness + abs(pixelSize)));
}

half4 LightSideApplyClipping(half4 color, half4 mask)
{
    #if UNITY_UI_CLIP_RECT
    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(mask.xy)) * mask.zw);
    color *= m.x * m.y;
    #endif

    #if UNITY_UI_ALPHACLIP
    clip(color.a - 0.001);
    #endif

    return color;
}

// ============================================================================
// Convenience: SDF face alpha with screen-space AA. Use when you want the standard
// signed-distance-to-coverage conversion in your custom shader.
// ============================================================================
float LightSideSDFAlpha(float signedDist, float faceDilate, float glyphH, float2 glyphUV)
{
    float2 dUV = fwidth(glyphUV);
    float aaWidth = max(dUV.x, dUV.y) * glyphH;
    float faceDist = signedDist - faceDilate * LIGHTSIDE_DILATE_SCALE;
    return saturate(-faceDist / aaWidth + 0.5);
}

// Region coverage (fill / stroke / shadow / inner-shadow) — lets a custom shader shade a specific
// coverage region (the coverage params ride in TEXCOORD2). DILATE_SCALE / SAMPLE_SDF feed the
// resolve helpers in LightSideGlyphCoverage.hlsl.
#define DILATE_SCALE LIGHTSIDE_DILATE_SCALE
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"

// ============================================================================
// Effect-function authoring. Write ONE function — LightSideEffect(LightSideFrag) — holding only your
// visual logic, and let the template shells (LightSide_Custom-Example / -World-Example) wire it into
// every pass: Canvas + World, built-in + URP, SDF / MSDF / color. LightSideVert and LightSideBuildFrag
// below carry all the per-pipeline plumbing so your effect is written once. Sample your own textures
// with LightSide_DECLARE_TEX2D / LightSide_SAMPLE_TEX2D so the same code compiles on both pipelines.
// Shared building blocks (LightSideCosPalette, LightSideTileHash) live in LightSide_EffectLib.hlsl.
// ============================================================================

#include "LightSide_EffectLib.hlsl"

#define LightSide_DECLARE_TEX2D(tex) sampler2D tex
#define LightSide_SAMPLE_TEX2D(tex, uv) tex2D(tex, uv)

struct LightSideFrag
{
    float3 atlasUV;     // sample with LightSideSampleAtlas / LightSideSampleSdf (pass glyphMode)
    float  glyphMode;   // 0 = SDF, 1 = MSDF, 2 = color — quad-constant, from the vertex stream
    float2 glyphUV;     // glyph-local UV (0..1), size-invariant — for patterns / fwidth.
                        // Color: atlas-normalized tile UV (a small sub-range, NOT 0..1) —
                        // UV-driven patterns lose density on color
    float  signedDist;  // SDF signed distance (0 for color)
    float  sdfAlpha;    // anti-aliased face coverage (1 for color)
    half4  atlasColor;
    half4  color;       // vertex colour
    float2 glyphMeta;   // x = glyphH, y = faceDilate
    float2 lineFlow;    // x = cluster index, y = intra-glyph X — smooth flow across/within letters
    float  tileId;      // per-glyph atlas id — stable hash seed.
                        // Color: atlas PAGE index (shared by every color on that page)
    float2 tileHash;    // stable per-glyph random pair, precomputed per vertex
                        // (LightSideTileHash(tileId)) — same color caveat as tileId
    float4 userA;       // TEXCOORD2 (MaterialModifier channel A)
    float4 userB;       // TEXCOORD3 (MaterialModifier channel B)
    float3 positionWS;  // world position (world shaders; 0 on Canvas)
};

struct LightSideVaryings
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
    float4 positionCS : SV_POSITION;
    float4 atlasUV    : TEXCOORD0;  // xy = atlas UV, z = page layer, w = glyph mode (quad-constant)
    float4 uvFlow     : TEXCOORD1;  // glyphUV.xy, lineFlow.xy
    float4 meta       : TEXCOORD2;  // glyphH, faceDilate, tileId, packed tileHash (LightSidePackTileHash)
    half4  color      : TEXCOORD3;
    float4 userA      : TEXCOORD4;
    float4 userB      : TEXCOORD5;
#ifdef LIGHTSIDE_WORLD
    float3 positionWS : TEXCOORD6;  // world shells trade the Canvas mask slot for world position
    UNITY_FOG_COORDS(7)
#else
    half4  mask       : TEXCOORD6;  // Canvas clip mask
#endif
};

// Optional vertex-stage hook: `#define LIGHTSIDE_EFFECT_VERT` in the pass (before this include) and
// implement `void LightSideEffectVert(LightSide_appdata v, inout LightSideVaryings o)` in your effect
// file. Runs at the end of LightSideVert — displace o.positionCS or precompute per-vertex data into
// o.userA / o.userB there instead of paying for it per fragment.
#ifdef LIGHTSIDE_EFFECT_VERT
void LightSideEffectVert(LightSide_appdata v, inout LightSideVaryings o);
#endif

LightSideVaryings LightSideVert(LightSide_appdata v)
{
    LightSideVaryings o;
    UNITY_INITIALIZE_OUTPUT(LightSideVaryings, o);
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float4 clipPos = UnityObjectToClipPos(v.vertex);

    o.positionCS = clipPos;
    o.atlasUV    = float4(LightSideComputeAtlasUV(v.texcoord0, v.texcoord1), LightSideGlyphMode(v.texcoord1.w));
    o.uvFlow     = float4(v.texcoord0.xy, v.texcoord1.z, LightSideIntraX(v.texcoord1.w));
    float tileId = v.texcoord0.z;
    o.meta       = float4(v.texcoord0.w, v.texcoord1.y, tileId,
                          LightSidePackTileHash(LightSideTileHash(tileId)));
    o.color      = LightSideGammaToLinearIfNeeded(v.color);
    o.userA      = v.texcoord2;
    o.userB      = v.texcoord3;
#ifdef LIGHTSIDE_WORLD
    o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
    UNITY_TRANSFER_FOG(o, clipPos);
#else
    float2 pixelSize = clipPos.w / abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
    o.mask       = LightSideComputeMask(v.vertex, pixelSize);
#endif
#ifdef LIGHTSIDE_EFFECT_VERT
    LightSideEffectVert(v, o);
#endif
    return o;
}

LightSideFrag LightSideBuildFrag(LightSideVaryings i)
{
    UNITY_SETUP_INSTANCE_ID(i);

    LightSideFrag f;
    f.atlasUV    = i.atlasUV.xyz;
    f.glyphMode  = i.atlasUV.w;
    f.glyphUV    = i.uvFlow.xy;
    f.lineFlow   = i.uvFlow.zw;
    f.glyphMeta  = i.meta.xy;
    f.tileId     = i.meta.z;
    f.tileHash   = LightSideUnpackTileHash(i.meta.w);
    f.color      = i.color;
    f.userA      = i.userA;
    f.userB      = i.userB;
#ifdef LIGHTSIDE_WORLD
    f.positionWS = i.positionWS;
#else
    f.positionWS = float3(0, 0, 0);
#endif
    // signedDist derives from the already-fetched texel — one atlas fetch per fragment by construction.
    f.atlasColor = LightSideSampleAtlas(f.atlasUV, f.glyphMode);
    if (f.glyphMode > 1.5)
    {
        f.signedDist = 0;
        f.sdfAlpha   = 1;
    }
    else
    {
        f.signedDist = f.glyphMode < 0.5
            ? LightSideSdf2FromSdf(f.atlasColor).x
            : LightSideSdf2FromMsdf(f.atlasColor).x;
        f.sdfAlpha   = LightSideSDFAlpha(f.signedDist, i.meta.y, i.meta.x, i.uvFlow.xy);
    }
    return f;
}

// Scene fog for world shells, applied AFTER LightSideEffect so effect functions stay fog-agnostic.
// Premultiplied-alpha mix toward unity_FogColor * alpha (same as LightSide_SDF_Lit's built-in pass);
// UNITY_APPLY_FOG_COLOR handles the per-vertex vs per-pixel fog-factor split. A macro because
// UNITY_FOG_COORDS declares the member only when a fog keyword is active.
#if defined(LIGHTSIDE_WORLD) && (defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2))
    #define LightSide_APPLY_FOG(col, i) { fixed4 LightSideFogCol = unity_FogColor; LightSideFogCol.rgb *= (col).a; UNITY_APPLY_FOG_COLOR((i).fogCoord, col, LightSideFogCol); }
#else
    #define LightSide_APPLY_FOG(col, i)
#endif

// ============================================================================
// ShadowCaster program for world shells (built-in pipeline) — casts the plain face silhouette,
// deliberately ignoring TEXCOORD2/3: on custom-material quads those are MaterialModifier user
// channels, NOT the coverage contract the Lit shaders' casters expect. The shell's ShadowCaster
// pass defines LightSide_SHADOW_CASTER and uses LightSideShadowVert / LightSideShadowFrag directly.
// ============================================================================
#ifdef LightSide_SHADOW_CASTER

struct LightSideShadowVaryings
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    V2F_SHADOW_CASTER;
    float4 atlasUV : TEXCOORD1;   // V2F_SHADOW_CASTER claims TEXCOORD0 for SHADOWS_CUBE; w = glyph mode
    float4 meta    : TEXCOORD2;   // glyphUV.xy, faceDilate, glyphH
    half   tint    : TEXCOORD3;   // vertex alpha modulates the color cutoff
};

LightSideShadowVaryings LightSideShadowVert(LightSide_appdata v)
{
    LightSideShadowVaryings o;
    UNITY_INITIALIZE_OUTPUT(LightSideShadowVaryings, o);
    UNITY_SETUP_INSTANCE_ID(v);

    // Plain TRANSFER_SHADOW_CASTER (not _NORMALOFFSET): the batcher writes world-space normals,
    // the offset variant would re-apply ObjectToWorld to them.
    TRANSFER_SHADOW_CASTER(o)

    o.atlasUV = float4(LightSideComputeAtlasUV(v.texcoord0, v.texcoord1), LightSideGlyphMode(v.texcoord1.w));
    o.meta    = float4(v.texcoord0.xy, v.texcoord1.y, v.texcoord0.w);
    o.tint    = v.color.a;
    return o;
}

float4 LightSideShadowFrag(LightSideShadowVaryings i) : SV_Target
{
    if (i.atlasUV.w > 1.5)
    {
        clip(LightSideSampleAtlas(i.atlasUV.xyz, i.atlasUV.w).a * i.tint - 0.5);
        SHADOW_CASTER_FRAGMENT(i)
    }

    float2 dUV = fwidth(i.meta.xy);
    float aa = max(dUV.x, dUV.y) * i.meta.w;
    float sd = LightSideSampleSdf(i.atlasUV.xyz, i.atlasUV.w) - i.meta.z * LIGHTSIDE_DILATE_SCALE;
    clip(LightSideInside(sd, 0.0, aa) - 0.5);
    SHADOW_CASTER_FRAGMENT(i)
}

#endif // LightSide_SHADOW_CASTER

#endif // LightSide_CUSTOM_INCLUDED
