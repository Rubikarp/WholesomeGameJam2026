// CGPROGRAM implementation of the world text/shape surface (LightSideWorld, WorldBatcher meshes).
// Serves every SubShader that runs on the legacy CG stack: the Built-in pipeline SubShaders and the
// HDRP baseline SubShaders (whose untagged pass HDRP draws via SRPDefaultUnlit with the legacy
// matrix globals — the same mechanism that renders uGUI and sprites there).
//
// Pass selector defines (set BEFORE the include; pragmas never travel through includes, so each
// pass declares its own):
//   LIGHTSIDE_LIT_PASS      — lit color program: worldNormal/vertexLight interpolators, VFACE
//                             two-sided normal, ambient+directional+vertex lights blended by
//                             _LightInfluence. Requires "LightMode"="ForwardBase" and
//                             `#pragma multi_compile __ VERTEXLIGHT_ON` — the lighting constants
//                             (_LightColor0, SH, unity_4LightPos*) are only populated there.
//   (none)                  — unlit color program. Legal in any pass group, including untagged.
//   LIGHTSIDE_SHADOW_CASTER — ShadowCaster program (LightSideShadowVert/LightSideShadowFrag):
//                             SDF-cutoff silhouette via the coverage cast resolve. Requires
//                             `#pragma multi_compile_shadowcaster`. Built-in pipeline only —
//                             TRANSFER_SHADOW_CASTER reads bias globals no SRP populates.
//   Color passes take `#pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE`, and
//   `#pragma multi_compile_fog` where scene fog exists (Built-in; HDRP never enables FOG_* keywords).

#ifndef LIGHTSIDE_WORLD_CG_INCLUDED
#define LIGHTSIDE_WORLD_CG_INCLUDED

#include "UnityCG.cginc"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphField.cginc"

#ifdef LIGHTSIDE_SHADOW_CASTER

#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"

half _ShadowCutoff;

struct LightSideShadowVaryings
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    V2F_SHADOW_CASTER;
    float4 atlasUV : TEXCOORD1;          // V2F_SHADOW_CASTER claims TEXCOORD0 for SHADOWS_CUBE
    float4 cov     : TEXCOORD2;          // coverageMode, p0, p1, softness
    float4 meta    : TEXCOORD3;          // glyphUV.xy, faceDilate (color: vertex alpha), glyphH
};

LightSideShadowVaryings LightSideShadowVert(LightSideSurfaceVertex v)
{
    LightSideShadowVaryings o;
    UNITY_INITIALIZE_OUTPUT(LightSideShadowVaryings, o);
    UNITY_SETUP_INSTANCE_ID(v);

    // Plain TRANSFER_SHADOW_CASTER (not _NORMALOFFSET): batcher writes world-space
    // normals, the offset variant would re-apply ObjectToWorld to them.
    TRANSFER_SHADOW_CASTER(o)

    float glyphMode  = LightSideGlyphMode(v.texcoord1.w);
    float glyphH     = v.texcoord0.w;
    float metaZ      = v.texcoord1.y;
    float  pageLayer = v.texcoord0.z;
    float2 atlasXY   = v.texcoord0.xy;
    if (glyphMode < 1.5)
    {
        float4 t = LightSideLoadGlyphTransform(v.texcoord0.z);
        atlasXY = v.texcoord0.xy * t.x + t.yz;
        pageLayer = t.w;
    }
    else
    {
        metaZ = v.color.a;
    }

    o.atlasUV = float4(atlasXY, pageLayer, glyphMode);
    o.cov     = v.texcoord2;
    o.meta    = float4(v.texcoord0.xy, metaZ, glyphH);
    return o;
}

float4 LightSideShadowFrag(LightSideShadowVaryings i) : SV_Target
{
    float2 uvDx = ddx(i.atlasUV.xy);
    float2 uvDy = ddy(i.atlasUV.xy);
    float2 dUV  = fwidth(i.meta.xy);

    UNITY_BRANCH
    if (i.atlasUV.w > 1.5)
    {
        // Color casts by its bitmap alpha faded by the vertex tint, hard-cut at the cutoff.
        half4 c = LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(i.atlasUV.xyz, uvDx, uvDy);
        clip(c.a * i.meta.z - _ShadowCutoff);
        SHADOW_CASTER_FRAGMENT(i)
    }

    // Cast the silhouette of each layer's visible coverage — stroke/shadow/glow inflate it
    // to their outer extent, inner-shadow casts as the face.
    float aa = max(dUV.x, dUV.y) * i.meta.w;
    float coverage = LightSideResolveGlyphCastCoverage(i.atlasUV, i.cov, i.meta.z, aa, uvDx, uvDy);
    clip(coverage - 0.5);

    SHADOW_CASTER_FRAGMENT(i)
}

#else // color program

sampler2D _LightSideGradientRamp;
float _LightSideGradientRampRows;
sampler2D _LightSidePaintTexture;
sampler2D _LightSideColorMatrixAtlas;
float _LightSideColorMatrixRows;
// Explicit LOD 0: the ramp has no mips — see LightSidePaint.hlsl for why implicit
// derivatives would flatten the gradient branch.
float4 _LightSidePaintTexture_TexelSize;
#define LIGHTSIDE_SAMPLE_RAMP(u, v) tex2Dlod(_LightSideGradientRamp, float4(u, v, 0, 0))
#define LIGHTSIDE_SAMPLE_PAINT(uv)  tex2D(_LightSidePaintTexture, uv)
#define LIGHTSIDE_SAMPLE_MATRIX(u, v) tex2Dlod(_LightSideColorMatrixAtlas, float4(u, v, 0, 0))

// Distance paint ramps by the surface's own field; the shape branch assigns it before the resolve.
static float lightSideDistanceT;
#define LIGHTSIDE_PAINT_DISTANCE_T lightSideDistanceT
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSidePaint.hlsl"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideShapeSurface.hlsl"

UNITY_DECLARE_TEX2DARRAY(_LightSideLottieAtlas);

#ifdef LIGHTSIDE_LIT_PASS
float4 _LightColor0;

half _LightInfluence;
half _AmbientStrength;
half _DirectStrength;
#endif

struct pixel_t
{
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
    float4 vertex      : SV_POSITION;
    float4 atlasUV     : TEXCOORD0;  // xy = atlas UV, z = page layer, w = glyph mode (quad-constant)
    float2 glyphUV     : TEXCOORD1;  // for fwidth AA
    half4  cov         : TEXCOORD2;  // coverageMode, p0, p1, softness (em-scale — half is plenty)
    float4 paint       : TEXCOORD3;  // paintU, paintV, rampRow, paintKind + 8 * spread (tiled coords exceed half range)
    fixed4 color       : TEXCOORD4;  // straight vertex colour (premultiplied in fragment)
    half4  extra       : TEXCOORD5;  // glyphs: faceDilate, glyphH, sdfScale.xy — shapes: .x is the paint scale/fit
#ifdef LIGHTSIDE_LIT_PASS
    float3 worldNormal : TEXCOORD6;  // per-quad world-space normal written by batcher
    half3  vertexLight : TEXCOORD7;  // 4 nearest non-important lights, per-vertex
    float4 shapeGeom   : TEXCOORD9;  // halfSize.xy, aux, trailing mode param
    float4 shapeParams : TEXCOORD10; // per-shape params (radii / ratios / counts)
#else
    float4 shapeGeom   : TEXCOORD6;  // halfSize.xy, aux, trailing mode param
    float4 shapeParams : TEXCOORD7;  // per-shape params (radii / ratios / counts)
#endif
    UNITY_FOG_COORDS(8)
};

pixel_t VertShader(LightSideSurfaceVertex input)
{
    pixel_t output;

    UNITY_INITIALIZE_OUTPUT(pixel_t, output);
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 vPosition = UnityObjectToClipPos(input.vertex);

    fixed4 color = GammaToLinearIfNeeded(input.color);

    float glyphMode  = LightSideGlyphMode(input.texcoord1.w);
    float glyphH     = input.texcoord0.w;
    float faceDilate = input.texcoord1.y;
    float2 sdfScale  = 0;
    float  pageLayer = input.texcoord0.z;
    float2 atlasXY   = input.texcoord0.xy;
    if (glyphMode < 1.5)
    {
        float4 t = LightSideLoadGlyphTransform(input.texcoord0.z);
        sdfScale = t.xx;
        atlasXY = input.texcoord0.xy * t.x + t.yz;
        pageLayer = t.w;
    }

#ifdef LIGHTSIDE_LIT_PASS
    // WorldBatcher writes per-quad world-space face normal into NORMAL
    // (cross product of actual quad edges — survives per-glyph rotation from modifiers).
    output.worldNormal = input.normal;

    // Per-vertex evaluation of up to 4 nearest non-important point/spot lights.
    // Compiled out when no scene non-important lights affect the object; the uniform
    // branch skips the ~40 ALU when the material is unlit (_LightInfluence == 0).
    output.vertexLight = 0;
    #ifdef VERTEXLIGHT_ON
        UNITY_BRANCH
        if (_LightInfluence > 0)
        {
            float3 worldPos = mul(unity_ObjectToWorld, input.vertex).xyz;
            output.vertexLight = Shade4PointLights(
                unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                unity_LightColor[0].rgb, unity_LightColor[1].rgb,
                unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                unity_4LightAtten0,
                worldPos, input.normal);
        }
    #endif
#endif

    output.vertex      = vPosition;
    output.atlasUV     = float4(atlasXY, pageLayer, glyphMode);
    output.glyphUV     = input.texcoord0.xy;
    output.cov         = input.texcoord2;
    output.paint       = input.texcoord3;
    output.color       = color;
    output.extra       = float4(faceDilate, glyphH, sdfScale.x, sdfScale.y);
    output.shapeGeom   = float4(input.texcoord0.zw, input.texcoord1.x, input.texcoord1.z);
    output.shapeParams = input.tangent;
    UNITY_TRANSFER_FOG(output, vPosition);

    return output;
}

#ifdef LIGHTSIDE_LIT_PASS
half3 ComputeLighting(half3 n, half3 vertexLight)
{
    half  NdotL   = max(0.0, dot(n, _WorldSpaceLightPos0.xyz));
    half3 ambient = ShadeSH9(half4(n, 1.0)) * _AmbientStrength;
    half3 direct  = _LightColor0.rgb * NdotL * _DirectStrength;
    return ambient + direct + vertexLight;
}

fixed4 PixShader(pixel_t input, float facing : VFACE) : SV_Target
#else
fixed4 PixShader(pixel_t input) : SV_Target
#endif
{
    UNITY_SETUP_INSTANCE_ID(input);

    // Derivatives and the (keyword-gated) paint resolve run before the mode branch so every
    // fetch inside it can be gradient-explicit — the branch is quad-constant and never flattens.
    float2 uvDx = ddx(input.atlasUV.xy);
    float2 uvDy = ddy(input.atlasUV.xy);
    float2 dUV = fwidth(input.glyphUV);

    // Analytic shapes evaluate their field first: distance paint reads it back through the hook,
    // and one resolve then serves every kind — which keeps the atlas fetches gradient-explicit.
    bool isShape = input.atlasUV.w > 2.5 && input.atlasUV.w < 3.5;
    float shapeD = 0.0;
    float shapeCoverage = 0.0;
    UNITY_BRANCH
    if (isShape)
    {
        float packed = input.cov.x;
        float shapeStyle = floor(packed * 0.001953125);
        packed -= shapeStyle * 512.0;
        float shapeKind = floor(packed * 0.0625);
        shapeCoverage = LightSideShapeCoverage(shapeKind, packed - shapeKind * 16.0,
            input.glyphUV, input.shapeGeom.xy, input.shapeParams, input.shapeGeom.z,
            float4(input.cov.yzw, input.shapeGeom.w), shapeStyle, shapeD);
        lightSideDistanceT = LightSideShapeDistanceT(shapeD, input.shapeGeom.xy, input.extra.x);
    }

    half4 paintCol = LightSideResolvePaint(input.paint, input.color,
        isShape ? input.extra.x : 1.0, _LightSidePaintTexture_TexelSize.xy * 0.5);

    half4 result;
    UNITY_BRANCH
    if (isShape)
    {
        result = LightSideComposePaintCoverage(paintCol, shapeCoverage);
    }
    else if (input.atlasUV.w > 3.5)
    {
        half4 c = UNITY_SAMPLE_TEX2DARRAY(_LightSideLottieAtlas, input.atlasUV.xyz) * input.color;
        result = half4(c.rgb * c.a, c.a);
    }
    else if (input.atlasUV.w > 1.5)
    {
        half4 c = LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(input.atlasUV.xyz, uvDx, uvDy);
        result = c * half4(input.color.rgb * input.color.a, input.color.a);
        if (input.paint.z > 0.5)
            result = LightSideApplyColorMatrixPremultiplied(result, input.paint.z - 1.0);
    }
    else
    {
        float aa = max(dUV.x, dUV.y) * input.extra.y;
        float coverage = LightSideResolveGlyphCoverage(input.atlasUV, input.cov, input.extra, aa, uvDx, uvDy);
        result = LightSideComposePaintCoverage(paintCol, coverage);
    }

#ifdef LIGHTSIDE_LIT_PASS
    UNITY_BRANCH
    if (_LightInfluence > 0)
    {
        half3 n = normalize(input.worldNormal) * sign(facing);
        half3 lit = ComputeLighting(n, input.vertexLight);
        result.rgb = lerp(result.rgb, result.rgb * lit, _LightInfluence);
    }
#endif

    // Classic fog for premultiplied output: mix toward unity_FogColor premultiplied by alpha,
    // so distant fragments take the scene fog colour while preserving the glyph's alpha shape.
    // UNITY_APPLY_FOG_COLOR handles the per-vertex (mobile: fogCoord = factor) vs per-pixel
    // (desktop SM3+: fogCoord = clip z, factor via UNITY_CALC_FOG_FACTOR) split — raw fogCoord
    // is NOT a usable factor on desktop.
    #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
        fixed4 fogCol = unity_FogColor;
        fogCol.rgb *= result.a;
        UNITY_APPLY_FOG_COLOR(input.fogCoord, result, fogCol);
    #endif

    return result;
}

#endif // LIGHTSIDE_SHADOW_CASTER

#endif // LIGHTSIDE_WORLD_CG_INCLUDED
