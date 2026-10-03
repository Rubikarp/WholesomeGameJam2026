#ifndef LIGHTSIDE_WORLD_URP_INCLUDED
#define LIGHTSIDE_WORLD_URP_INCLUDED

// Shared URP surface stage for every world-space LightSide surface — unlit and lit alike.
// The including pass declares what it needs:
//   LIGHTSIDE_FORWARD_PASS — camera forward pass (fog coordinate).
//   LIGHTSIDE_2D_PASS      — URP 2D renderer pass.
//   LIGHTSIDE_LIT_PASS     — lighting is evaluated (world normal, world position, _LightInfluence).
//   LIGHTSIDE_NO_SHAPES    — compiles the analytic shape surface out of the resolve, for passes
//                            whose meshes never carry shape quads (the HDRP lit graph — decorations
//                            and vector animation always use the unlit world material). Constant
//                            shape inputs would otherwise constant-fold the shape math into
//                            division-by-zero compiler warnings.
// Interpolators past the surface set exist only where their pass declares them, so the unlit
// shader pays no vertex bandwidth for lighting inputs it never reads.

#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphFieldURP.hlsl"

TEXTURE2D(_LightSideGradientRamp); SAMPLER(sampler_LightSideGradientRamp);
float _LightSideGradientRampRows;
TEXTURE2D(_LightSideColorMatrixAtlas); SAMPLER(sampler_LightSideColorMatrixAtlas);
float _LightSideColorMatrixRows;
TEXTURE2D(_LightSidePaintTexture); SAMPLER(sampler_LightSidePaintTexture);
float4 _LightSidePaintTexture_TexelSize;
#define LIGHTSIDE_SAMPLE_RAMP(u, v) SAMPLE_TEXTURE2D_LOD(_LightSideGradientRamp, sampler_LightSideGradientRamp, float2(u, v), 0)
#define LIGHTSIDE_SAMPLE_PAINT(uv) SAMPLE_TEXTURE2D(_LightSidePaintTexture, sampler_LightSidePaintTexture, uv)
#define LIGHTSIDE_SAMPLE_MATRIX(u, v) SAMPLE_TEXTURE2D_LOD(_LightSideColorMatrixAtlas, sampler_LightSideColorMatrixAtlas, float2(u, v), 0)

// Distance paint ramps by the surface's own field; the shape branch assigns it before the resolve.
static float lightSideDistanceT;
#define LIGHTSIDE_PAINT_DISTANCE_T lightSideDistanceT
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"
#include "Packages/media.lightside.core/Runtime/Rendering/LightSidePaint.hlsl"
#if !defined(LIGHTSIDE_NO_SHAPES)
#include "Packages/media.lightside.core/Runtime/Rendering/LightSideShapeSurface.hlsl"
#endif

// Surface kind 4 samples this global array atlas — never a material property, so a vector-animation
// quad and a glyph run share one material and one batch.
TEXTURE2D_ARRAY(_LightSideLottieAtlas); SAMPLER(sampler_LightSideLottieAtlas);

#if defined(LIGHTSIDE_LIT_PASS)
half _LightInfluence;
#endif

struct LightSideWorldVaryings
{
    float4 atlasUV     : TEXCOORD0;
    float2 glyphUV     : TEXCOORD1;
    half4  cov         : TEXCOORD2;
    float4 paint       : TEXCOORD3;
    half4  color       : TEXCOORD4;
    half4  extra       : TEXCOORD5;
#if defined(LIGHTSIDE_LIT_PASS)
    float3 worldNormal : TEXCOORD6;
#endif
#if defined(LIGHTSIDE_LIT_PASS) && defined(LIGHTSIDE_FORWARD_PASS)
    float3 positionWS  : TEXCOORD7;
#endif
#if defined(LIGHTSIDE_FORWARD_PASS)
    float  fogCoord    : TEXCOORD8;
#endif
#if defined(LIGHTSIDE_LIT_PASS) && defined(LIGHTSIDE_2D_PASS)
    half2  lightingUV  : TEXCOORD9;
#endif
    float4 shapeGeom   : TEXCOORD10; // halfSize.xy, aux, trailing mode param
    float4 shapeParams : TEXCOORD11; // per-shape params (radii / ratios / counts)
    float4 positionCS  : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

LightSideWorldVaryings LightSideWorldVertex(LightSideSurfaceVertex input)
{
    LightSideWorldVaryings output = (LightSideWorldVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float4 positionCS = TransformWorldToHClip(positionWS);
    float glyphMode = LightSideGlyphMode(input.texcoord1.w);
    float glyphH = input.texcoord0.w;
    float faceDilate = input.texcoord1.y;
    float2 sdfScale = 0;
    float pageLayer = input.texcoord0.z;
    float2 atlasXY = input.texcoord0.xy;
    if (glyphMode < 1.5)
    {
        float4 glyphTransform = LightSideLoadGlyphTransform(input.texcoord0.z);
        sdfScale = glyphTransform.xx;
        atlasXY = input.texcoord0.xy * glyphTransform.x + glyphTransform.yz;
        pageLayer = glyphTransform.w;
    }

    output.positionCS = positionCS;
    output.atlasUV = float4(atlasXY, pageLayer, glyphMode);
    output.glyphUV = input.texcoord0.xy;
    output.cov = input.texcoord2;
    output.paint = input.texcoord3;
    output.color = input.color;
    output.extra = float4(faceDilate, glyphH, sdfScale.x, sdfScale.y);
    output.shapeGeom = float4(input.texcoord0.zw, input.texcoord1.x, input.texcoord1.z);
    output.shapeParams = input.tangent;
#if defined(LIGHTSIDE_LIT_PASS)
    output.worldNormal = input.normalOS;
#endif
#if defined(LIGHTSIDE_LIT_PASS) && defined(LIGHTSIDE_FORWARD_PASS)
    output.positionWS = positionWS;
#endif
#if defined(LIGHTSIDE_FORWARD_PASS)
    output.fogCoord = ComputeFogFactor(positionCS.z);
#endif
#if defined(LIGHTSIDE_LIT_PASS) && defined(LIGHTSIDE_2D_PASS)
    output.lightingUV = half2(ComputeScreenPos(positionCS / positionCS.w).xy);
#endif
    return output;
}

half4 LightSideResolveSurface(LightSideWorldVaryings input)
{
    float2 uvDx = ddx(input.atlasUV.xy);
    float2 uvDy = ddy(input.atlasUV.xy);
    float2 dUV = fwidth(input.glyphUV);

    // Analytic shapes evaluate their field first: distance paint reads it back through the hook, and
    // one resolve then serves every kind — which keeps the atlas fetches below gradient-explicit.
    float shapeCoverage = 0.0;
#if defined(LIGHTSIDE_NO_SHAPES)
    const bool isShape = false;
#else
    bool isShape = input.atlasUV.w > 2.5 && input.atlasUV.w < 3.5;
    float shapeD = 0.0;
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
#endif

    half4 paintCol = LightSideResolvePaint(input.paint, input.color,
        isShape ? input.extra.x : 1.0, _LightSidePaintTexture_TexelSize.xy * 0.5);
    half4 result = 0;

    UNITY_BRANCH
    if (isShape)
    {
        result = LightSideComposePaintCoverage(paintCol, shapeCoverage);
    }
    else if (input.atlasUV.w > 3.5)
    {
        half4 c = SAMPLE_TEXTURE2D_ARRAY(_LightSideLottieAtlas, sampler_LightSideLottieAtlas,
            input.atlasUV.xy, input.atlasUV.z) * input.color;
        result = half4(c.rgb * c.a, c.a);
    }
    else if (input.atlasUV.w > 1.5)
    {
        half4 color = LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(input.atlasUV.xyz, uvDx, uvDy);
        result = color * half4(input.color.rgb * input.color.a, input.color.a);
        if (input.paint.z > 0.5)
            result = LightSideApplyColorMatrixPremultiplied(result, input.paint.z - 1.0);
    }
    else
    {
        float aa = max(dUV.x, dUV.y) * input.extra.y;
        float coverage = LightSideResolveGlyphCoverage(input.atlasUV, input.cov, input.extra, aa, uvDx, uvDy);
        result = LightSideComposePaintCoverage(paintCol, coverage);
    }
    return result;
}

// Premultiplied-alpha fog: blend toward the fog colour scaled by the fragment's own alpha.
// Plain MixFog drags fully transparent texels toward opaque fog colour, turning glyph quads
// into fog-coloured rectangles at distance. Guarded by the forward selector because MixFogColor
// and unity_FogColor are URP symbols — non-URP consumers of this file (the HDRP Shader Graph
// wrappers) compile without them.
#if defined(LIGHTSIDE_FORWARD_PASS)
half4 LightSideApplyWorldFog(half4 color, float fogCoord)
{
    color.rgb = MixFogColor(color.rgb, unity_FogColor.rgb * color.a, fogCoord);
    return color;
}
#endif

#endif
