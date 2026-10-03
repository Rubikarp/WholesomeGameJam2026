// LightSide_Custom-URP.hlsl — URP/world prelude for user-authored LightSide material shaders.
//
// URP counterpart of LightSide_Custom.cginc (built-in). Same vertex contract and helper names, but URP
// HLSL API (TEXTURE2D_ARRAY / SAMPLE_TEXTURE2D_ARRAY, TransformObjectToWorld) and no UI clip/stencil —
// world text is depth-tested, not Canvas-clipped. A custom shader carries one SubShader per context:
//   * Canvas (built-in): include "LightSide_Custom.cginc".
//   * World  (URP):       include "LightSide_Custom-URP.hlsl"  (this file).
//   * World  (built-in):  #define LIGHTSIDE_WORLD, then include "LightSide_Custom.cginc".
// Vertex layout, atlas encoding and TEXCOORD2/3 semantics are identical across all three.
//
// Required SubShader Tags (URP): "RenderPipeline"="UniversalPipeline", Queue/RenderType "Transparent".
// Required Pass: LightMode "UniversalForward", HLSLPROGRAM.
// Required Pass pragmas (Unity ignores #pragma inside plain includes — declare in the .shader):
//   #pragma multi_compile_fog
//   #pragma multi_compile_instancing
// There are NO atlas-mode keywords: the per-glyph mode rides the vertex stream (UV1.w) and one
// variant serves SDF, MSDF and color glyphs alike.
//
// TEXCOORD2/3 semantics — two disjoint vertex streams, never both on one quad:
//   * MaterialModifier sub-mesh quads (the stream every custom shader bound through MaterialModifier
//     receives): TEXCOORD2/3 = user channels A/B (ConstantUv2/ConstantUv3/glyphDataWriter). Paint
//     layers (stroke/shadow/gradient) on the same text range render as SEPARATE base-mesh quads through
//     the base SDF shader — they never write into the custom sub-mesh.
//   * Base-mesh quads (only if a custom shader is used as the base text material): TEXCOORD2 =
//     (coverageMode, p0, p1, softness), TEXCOORD3 = (paintU, paintV, rampRow, paintKind); plain glyphs
//     leave them at 0 (fill + solid). Read with LightSideCoverage(...) (LightSideGlyphCoverage.hlsl included below).
//   Base-mesh vertex channels (uv0-uv3 + color) and the prelude interpolators are fully claimed
//   (meta.w carries the packed tileHash) — per-glyph custom effect data must ride the
//   MaterialModifier user channels, or be produced in the LIGHTSIDE_EFFECT_VERT hook (see below).

#ifndef LightSide_CUSTOM_URP_INCLUDED
#define LightSide_CUSTOM_URP_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideAtlasDecode.hlsl"

#define DILATE_SCALE LIGHTSIDE_SDF_PAD
#define LIGHTSIDE_DILATE_SCALE LIGHTSIDE_SDF_PAD

// Glyph atlases — all three bound at runtime; the per-glyph vertex-stream mode selects one.
TEXTURE2D_ARRAY(_LightSideGlyphSdf);   SAMPLER(sampler_LightSideGlyphSdf);   // SDF  (RHalf)
TEXTURE2D_ARRAY(_LightSideGlyphMsdf);   SAMPLER(sampler_LightSideGlyphMsdf);   // MSDF (RGBAHalf)
TEXTURE2D_ARRAY(_LightSideGlyphColor);  SAMPLER(sampler_LightSideGlyphColor);  // Color (RGBA32 sRGB + mips)

struct LightSide_appdata
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 vertex    : POSITION;
    float3 normal    : NORMAL;     // per-quad world-space normal written by WorldBatcher
    float4 color     : COLOR;
    float4 texcoord0 : TEXCOORD0;  // xy = glyph UV (color: normalized atlas UV), z = encodedTile (color: page layer), w = glyphH (color: 0)
    float4 texcoord1 : TEXCOORD1;  // x = aspect, y = faceDilate, z = cluster, w = intra-glyph X fraction (0..1) + 2*glyphMode
    float4 texcoord2 : TEXCOORD2;  // user channel A / coverage (coverageMode, p0, p1, softness)
    float4 texcoord3 : TEXCOORD3;  // user channel B / paint
};

// LightSideComputeAtlasUV lives in LightSideAtlasDecode.hlsl (one shared copy across pipelines).

// Distance decode — one shared copy, parameterized on the URP texel fetch. Every fetch is
// gradient-explicit: the per-glyph mode branch is quad-constant, and implicit-derivative fetches
// inside it would force the compiler to flatten it into three samples per fragment.
#define LIGHTSIDE_SAMPLE_ATLAS_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_GRAD(_LightSideGlyphSdf, sampler_LightSideGlyphSdf, (uv).xy, (uv).z, dx, dy)
#define LIGHTSIDE_SAMPLE_MSDF_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_GRAD(_LightSideGlyphMsdf, sampler_LightSideGlyphMsdf, (uv).xy, (uv).z, dx, dy)
#define LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(uv, dx, dy) SAMPLE_TEXTURE2D_ARRAY_LOD(_LightSideGlyphColor, sampler_LightSideGlyphColor, (uv).xy, (uv).z, LightSideColorLod((dx), (dy)))
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

half4 LightSideGammaToLinearIfNeeded(half4 color)
{
    // World text colours are authored straight; URP fragment output is linear. No Canvas gamma opt-in here.
    return color;
}

// SDF face alpha with screen-space AA — the standard signed-distance-to-coverage conversion.
float LightSideSDFAlpha(float signedDist, float faceDilate, float glyphH, float2 glyphUV)
{
    float2 dUV = fwidth(glyphUV);
    float aaWidth = max(dUV.x, dUV.y) * glyphH;
    float faceDist = signedDist - faceDilate * DILATE_SCALE;
    return saturate(-faceDist / aaWidth + 0.5);
}

// Region coverage (fill / stroke / shadow / inner-shadow) — for shading a specific coverage region.
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"

// ============================================================================
// Effect-function authoring — URP counterpart of the block in LightSide_Custom.cginc. Same LightSideFrag,
// same helper names; the user's LightSideEffect(LightSideFrag) is written once and compiles here too.
// Shared building blocks (LightSideCosPalette, LightSideTileHash) live in LightSide_EffectLib.hlsl.
// ============================================================================

#include "LightSide_EffectLib.hlsl"

#define LightSide_DECLARE_TEX2D(tex) TEXTURE2D(tex); SAMPLER(sampler##tex)
#define LightSide_SAMPLE_TEX2D(tex, uv) SAMPLE_TEXTURE2D(tex, sampler##tex, uv)

// Field semantics documented on the built-in copy (LightSide_Custom.cginc) — including the
// color caveats for glyphUV / tileId / tileHash.
struct LightSideFrag
{
    float3 atlasUV;
    float  glyphMode;   // 0 = SDF, 1 = MSDF, 2 = color — quad-constant, from the vertex stream
    float2 glyphUV;
    float  signedDist;
    float  sdfAlpha;
    half4  atlasColor;
    half4  color;
    float2 glyphMeta;
    float2 lineFlow;
    float  tileId;
    float2 tileHash;
    float4 userA;
    float4 userB;
    float3 positionWS;
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
    float3 positionWS : TEXCOORD6;
    float  fogCoord   : TEXCOORD7;
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
    LightSideVaryings o = (LightSideVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float3 positionWS = TransformObjectToWorld(v.vertex.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.atlasUV    = float4(LightSideComputeAtlasUV(v.texcoord0, v.texcoord1), LightSideGlyphMode(v.texcoord1.w));
    o.uvFlow     = float4(v.texcoord0.xy, v.texcoord1.z, LightSideIntraX(v.texcoord1.w));
    float tileId = v.texcoord0.z;
    o.meta       = float4(v.texcoord0.w, v.texcoord1.y, tileId,
                          LightSidePackTileHash(LightSideTileHash(tileId)));
    o.color      = v.color;
    o.userA      = v.texcoord2;
    o.userB      = v.texcoord3;
    o.positionWS = positionWS;
    o.fogCoord   = ComputeFogFactor(o.positionCS.z);
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
    f.positionWS = i.positionWS;
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

// Scene fog, applied AFTER LightSideEffect so effect functions stay fog-agnostic. Same macro name as
// the built-in prelude so the shells share one frag body. MixFogColor with fog colour premultiplied
// by alpha — plain MixFog would drag transparent texels toward opaque fog colour (visible quads).
#define LightSide_APPLY_FOG(col, i) (col).rgb = MixFogColor((col).rgb, unity_FogColor.rgb * (col).a, (i).fogCoord)

// ============================================================================
// ShadowCaster program for world shells (URP) — casts the plain face silhouette, deliberately
// ignoring TEXCOORD2/3: on custom-material quads those are MaterialModifier user channels, NOT the
// coverage contract the Lit shaders' casters expect. The shell's ShadowCaster pass defines
// LightSide_SHADOW_CASTER and uses LightSideShadowVert / LightSideShadowFrag directly.
// ============================================================================
#ifdef LightSide_SHADOW_CASTER

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideShadowClamp.hlsl"

float3 _LightDirection;
float3 _LightPosition;

struct LightSideShadowVaryings
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    float4 positionCS : SV_POSITION;
    float4 atlasUV    : TEXCOORD0;  // xy = atlas UV, z = page layer, w = glyph mode
    float4 meta       : TEXCOORD1;  // glyphUV.xy, faceDilate, glyphH
    half   tint       : TEXCOORD2;  // vertex alpha modulates the color cutoff
};

LightSideShadowVaryings LightSideShadowVert(LightSide_appdata v)
{
    LightSideShadowVaryings o = (LightSideShadowVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);

    float3 positionWS = TransformObjectToWorld(v.vertex.xyz);
    // Batcher already writes a world-space face normal — use it directly.
    float3 normalWS   = v.normal;

    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
    o.positionCS = LightSideApplyShadowClamping(positionCS);

    o.atlasUV = float4(LightSideComputeAtlasUV(v.texcoord0, v.texcoord1), LightSideGlyphMode(v.texcoord1.w));
    o.meta    = float4(v.texcoord0.xy, v.texcoord1.y, v.texcoord0.w);
    o.tint    = v.color.a;
    return o;
}

half4 LightSideShadowFrag(LightSideShadowVaryings i) : SV_Target
{
    if (i.atlasUV.w > 1.5)
    {
        clip(LightSideSampleAtlas(i.atlasUV.xyz, i.atlasUV.w).a * i.tint - 0.5);
        return 0;
    }

    float2 dUV = fwidth(i.meta.xy);
    float aa = max(dUV.x, dUV.y) * i.meta.w;
    float sd = LightSideSampleSdf(i.atlasUV.xyz, i.atlasUV.w) - i.meta.z * LIGHTSIDE_DILATE_SCALE;
    clip(LightSideInside(sd, 0.0, aa) - 0.5);
    return 0;
}

#endif // LightSide_SHADOW_CASTER

#endif // LightSide_CUSTOM_URP_INCLUDED
