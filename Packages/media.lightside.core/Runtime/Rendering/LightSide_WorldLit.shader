// Lit SDF text rendering for LightSideWorld (world-space).
// Three SubShaders below — Unity picks by RenderPipeline tag: URP, HDRP, then the untagged
// built-in one. The shared CG program lives in LightSideWorldCG.cginc.
//
// HDRP path: the SubShader here is the unlit baseline (SRPDefaultUnlit); HDRP lighting comes
// from the LightSide World HDRP Shader Graphs, which LightSideMaterials selects when present.
//
// Built-in path (second SubShader):
//   Ambient (SH9) + main directional light + up to 4 nearest non-important point/spot
//   lights (vertex-evaluated through Shade4PointLights, batching survives, no ForwardAdd).
//   Casts shadows via a dedicated ShadowCaster pass with SDF alpha cutoff.
//   Receive shadows is NOT supported on transparent in built-in — this is an
//   architectural limit of the pipeline, not a shader gap.
//
// VFACE flips the geometric normal for back-facing fragments — physically correct
// two-sided shading. World-space only — no stencil/clip.

Shader "LightSide/World Lit" {

Properties {
    [HideInInspector] _LightSidePaintTexture ("Paint Texture", 2D) = "white" {}
    [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
    [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
    [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
    [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
    [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
    [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
    [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0

    _LightInfluence     ("Light Influence (0 = unlit, 1 = fully lit)", Range(0, 1)) = 1
    _AmbientStrength    ("Ambient Strength", Range(0, 2)) = 1
    _DirectStrength     ("Directional Light Strength", Range(0, 2)) = 1
    _ShadowCutoff       ("Color Shadow Cutoff", Range(0, 1)) = 0.5

    _CullMode           ("Cull Mode", Float) = 0
    _ColorMask          ("Color Mask", Float) = 15
}

// =================================================================================
// URP SubShader — RenderPipeline tag drives runtime selection; PackageRequirements
// drives compile-time gating, so Built-in / HDRP projects without URP import
// cleanly (Unity skips this SubShader instead of resolving its includes).
// =================================================================================
SubShader {
    PackageRequirements { "com.unity.render-pipelines.universal" }

    Tags
    {
        "Queue"          = "Transparent"
        "IgnoreProjector"= "True"
        "RenderType"     = "Transparent"
        "RenderPipeline" = "UniversalPipeline"
    }

    Cull       [_CullMode]
    ZWrite     Off
    ZTest      LEqual
    ColorMask  [_ColorMask]

    Pass {
        Name "UniversalForward"
        Tags { "LightMode" = "UniversalForward" }

        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]

        HLSLPROGRAM
        #pragma vertex   LightSideWorldVertex
        #pragma fragment PixShader
        #pragma target   3.5

        // Fragment-only keyword (paint resolve) — the vertex program never reads it.
        // SDF vs MSDF vs color is NOT a keyword: the per-glyph mode rides the vertex stream (UV1.w).
        #pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE

        // Lit vs unlit is a uniform branch on _LightInfluence, not a keyword: the interpolator
        // layout is identical either way, shadow taps are comparison/explicit-LOD samples (legal
        // inside a uniform branch), and dropping the keyword halves the variant space of an
        // already ~10-keyword pass.

        // URP main-light shadows (3-way: off / cascade / screen-space).
        #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

        // Additional lights — pixel or vertex.
        #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
        #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

        // Soft shadow filter quality.
        #pragma multi_compile_fragment _ _SHADOWS_SOFT

        // Light cookies.
        #pragma multi_compile_fragment _ _LIGHT_COOKIES

        // Light layers / Rendering Layer Mask.
        #pragma multi_compile _ _LIGHT_LAYERS

        // Forward+ was renamed to Cluster in Unity 6.1.
        #if UNITY_VERSION >= 60001000
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
        #else
            #pragma multi_compile _ _FORWARD_PLUS
        #endif

        #pragma multi_compile_fog
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #define LIGHTSIDE_FORWARD_PASS
        #define LIGHTSIDE_LIT_PASS
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldURP.hlsl"

        #if defined(USE_CLUSTER_LIGHT_LOOP)
            #define LIGHTSIDE_USE_FORWARD_PLUS USE_CLUSTER_LIGHT_LOOP
            #define LIGHTSIDE_FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        #else
            #define LIGHTSIDE_USE_FORWARD_PLUS USE_FORWARD_PLUS
            #define LIGHTSIDE_FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
        #endif

        half _AmbientStrength;
        half _DirectStrength;

        // Adds one URP Light's Lambert contribution, attenuated by its distance and
        // shadow factors. URP's Light struct already encodes all of these — just multiply.
        half3 ApplyLight(Light light, half3 n)
        {
            half NdotL = saturate(dot(n, light.direction));
            return light.color * (NdotL * light.distanceAttenuation * light.shadowAttenuation);
        }

        half4 PixShader(LightSideWorldVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);

            half4 result = LightSideResolveSurface(input);

            UNITY_BRANCH
            if (_LightInfluence > 0)
            {
                // Two-sided lighting: flip the geometric normal for back-facing fragments.
                half3 n = normalize(input.worldNormal) * (IS_FRONT_VFACE(facing, 1.0, -1.0));

                // Main light + cascaded shadows. TransformWorldToShadowCoord handles
                // screen-space and cascade modes internally based on multi-compile keyword.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half4  shadowMask  = half4(1, 1, 1, 1);
                Light  mainLight   = GetMainLight(shadowCoord, input.positionWS, shadowMask);

                #if defined(_LIGHT_LAYERS)
                    #if UNITY_VERSION >= 202220
                        uint meshRenderingLayers = GetMeshRenderingLayer();
                    #else
                        uint meshRenderingLayers = GetMeshRenderingLightLayer();
                    #endif
                #endif

                // Ambient via SH probes — replaces ShadeSH9 in built-in.
                half3 lit = SampleSH(n) * _AmbientStrength;
                #if defined(_LIGHT_LAYERS)
                    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
                #endif
                {
                    lit += ApplyLight(mainLight, n) * _DirectStrength;
                }

                // Additional lights (pixel path). LIGHT_LOOP_BEGIN/END resolves to either a
                // simple loop (URP 12 / non-Forward+) or a cluster iterator (URP 14+ / Forward+).
                #if defined(_ADDITIONAL_LIGHTS)
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    uint pixelLightCount = GetAdditionalLightsCount();

                    #if LIGHTSIDE_USE_FORWARD_PLUS
                        UNITY_LOOP for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            LIGHTSIDE_FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                            Light addLight = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                            #if defined(_LIGHT_LAYERS)
                                if (IsMatchingLightLayer(addLight.layerMask, meshRenderingLayers))
                            #endif
                            {
                                lit += ApplyLight(addLight, n);
                            }
                        }
                    #endif

                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light addLight = GetAdditionalLight(lightIndex, input.positionWS, shadowMask);
                        #if defined(_LIGHT_LAYERS)
                            if (IsMatchingLightLayer(addLight.layerMask, meshRenderingLayers))
                        #endif
                        {
                            lit += ApplyLight(addLight, n);
                        }
                    LIGHT_LOOP_END
                #endif

                // Vertex-path additional lights — URP evaluates these in vertex stage and
                // exposes the result through GetVertexLighting. We don't request a vertex
                // interpolator slot, so call it in fragment with positionWS as input (a few
                // extra ALU per fragment, but keeps the pixel-path Lit shader the same shape).
                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                    lit += VertexLighting(input.positionWS, n);
                #endif

                result.rgb = lerp(result.rgb, result.rgb * lit, _LightInfluence);
            }

            return LightSideApplyWorldFog(result, input.fogCoord);
        }
        ENDHLSL
    }

    Pass {
        Name "Universal2D"
        Tags { "LightMode" = "Universal2D" }

        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]

        HLSLPROGRAM
        #pragma vertex   LightSideWorldVertex
        #pragma fragment LightSide2DFragment
        #pragma target   3.5

        #pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE
        #if UNITY_VERSION >= 60000000
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/ShapeLightShared.hlsl"
        #else
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
        #endif
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/InputData2D.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
        #ifndef COMMON_2D_INPUTS
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
            #if USE_SHAPE_LIGHT_TYPE_0
                SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
                SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
                SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
                SHAPE_LIGHT(3)
            #endif
        #endif
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
        #define LIGHTSIDE_2D_PASS
        #define LIGHTSIDE_LIT_PASS
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldURP.hlsl"

        half4 LightSide2DFragment(LightSideWorldVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            half4 result = LightSideResolveSurface(input);

            UNITY_BRANCH
            if (_LightInfluence <= 0)
                return result;

            SurfaceData2D surfaceData;
            InputData2D inputData;
            InitializeSurfaceData(result.rgb / max(result.a, 1e-4), result.a, half4(1, 1, 1, 1), surfaceData);
            InitializeInputData(input.glyphUV, input.lightingUV, inputData);

            half4 lit = CombinedShapeLightShared(surfaceData, inputData);
            lit.rgb *= lit.a;
            return lerp(result, lit, _LightInfluence);
        }
        ENDHLSL
    }

    Pass {
        Name "NormalsRendering"
        Tags { "LightMode" = "NormalsRendering" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

        HLSLPROGRAM
        #pragma vertex   LightSideWorldVertex
        #pragma fragment LightSideNormalsFragment
        #pragma target   3.5

        #pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define LIGHTSIDE_LIT_PASS
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldURP.hlsl"

        half4 LightSideNormalsFragment(LightSideWorldVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_BRANCH
            if (_LightInfluence <= 0)
                discard;
            half alpha = LightSideResolveSurface(input).a;
            half3 normalWS = normalize(input.worldNormal) * IS_FRONT_VFACE(facing, 1.0, -1.0);
            return half4(normalWS * 0.5 + 0.5, alpha);
        }
        ENDHLSL
    }

    Pass {
        Name "ShadowCaster"
        Tags { "LightMode" = "ShadowCaster" }

        ZWrite On
        ZTest LEqual
        ColorMask 0
        Cull [_CullMode]

        HLSLPROGRAM
        #pragma vertex   ShadowVert
        #pragma fragment ShadowFrag
        #pragma target   3.5

        // Tells URP to use punctual-light normal bias when this caster is in a spot/point
        // shadow map; on directional cascades it stays defined to 0.
        #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphFieldURP.hlsl"
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideShadowClamp.hlsl"

        float3 _LightDirection;
        float3 _LightPosition;
        half   _ShadowCutoff;

        struct ShadowVaryings
        {
            UNITY_VERTEX_INPUT_INSTANCE_ID
            float4 positionCS : SV_POSITION;
            float4 atlasUV    : TEXCOORD0;  // xy = atlas UV, z = page layer, w = glyph mode
            float4 cov        : TEXCOORD1;  // coverageMode, p0, p1, softness
            float4 meta       : TEXCOORD2;  // glyphUV.xy, faceDilate (color: vertex alpha), glyphH
        };

        float4 GetShadowPositionHClip(LightSideSurfaceVertex v)
        {
            float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
            // Batcher already writes a world-space face normal — use it directly.
            float3 normalWS   = v.normalOS;

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

            float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            positionCS = LightSideApplyShadowClamping(positionCS);
            return positionCS;
        }

        ShadowVaryings ShadowVert(LightSideSurfaceVertex v)
        {
            ShadowVaryings o = (ShadowVaryings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_TRANSFER_INSTANCE_ID(v, o);

            o.positionCS = GetShadowPositionHClip(v);

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

        half4 ShadowFrag(ShadowVaryings i) : SV_Target
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
                return 0;
            }

            // Cast the silhouette of each layer's visible coverage — stroke/shadow/glow inflate
            // it to their outer extent, inner-shadow casts as the face.
            float aa = max(dUV.x, dUV.y) * i.meta.w;
            float coverage = LightSideResolveGlyphCastCoverage(i.atlasUV, i.cov, i.meta.z, aa, uvDx, uvDy);
            clip(coverage - 0.5);
            return 0;
        }
        ENDHLSL
    }
}

// =================================================================================
// HDRP SubShader — selected by the RenderPipeline tag. The color pass carries no
// LightMode, so HDRP draws it through its SRPDefaultUnlit transparent path (the same
// mechanism that renders uGUI and sprites there); _LightInfluence is inert here — HDRP
// lighting lives in the LightSide World HDRP Shader Graphs. No ShadowCaster on purpose:
// the CG caster reads bias globals HDRP never populates. Pure CG — compiles in any
// project, so no PackageRequirements gate is needed.
// =================================================================================
SubShader {
    Tags
    {
        "Queue"          = "Transparent"
        "IgnoreProjector"= "True"
        "RenderType"     = "Transparent"
        "RenderPipeline" = "HDRenderPipeline"
    }

    Cull       [_CullMode]
    ZWrite     Off
    Lighting   Off
    ZTest      LEqual
    ColorMask  [_ColorMask]

    Pass {
        Name "SDF_LIT_HDRP"

        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]

        CGPROGRAM
        #pragma vertex   VertShader
        #pragma fragment PixShader
        #pragma target   3.5

        // Fragment-only keyword (paint resolve) — the vertex program never reads it.
        // SDF vs MSDF vs color is NOT a keyword: the per-glyph mode rides the vertex stream (UV1.w).
        #pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE

        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldCG.cginc"
        ENDCG
    }
}

// =================================================================================
// Built-in SubShader — picked when no SRP is active.
// =================================================================================
SubShader {
    Tags
    {
        "Queue"          = "Transparent"
        "IgnoreProjector"= "True"
        "RenderType"     = "Transparent"
    }

    Cull       [_CullMode]
    ZWrite     Off
    Lighting   Off
    ZTest      LEqual
    ColorMask  [_ColorMask]

    Pass {
        Name "SDF_LIT_FORWARDBASE"
        Tags { "LightMode" = "ForwardBase" }

        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]

        CGPROGRAM
        #pragma vertex   VertShader
        #pragma fragment PixShader
        #pragma target   3.5

        // Fragment-only keyword (paint resolve) — the vertex program never reads it.
        // SDF vs MSDF vs color is NOT a keyword: the per-glyph mode rides the vertex stream (UV1.w).
        #pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE
        #pragma multi_compile __ VERTEXLIGHT_ON
        #pragma multi_compile_fog

        #define LIGHTSIDE_LIT_PASS
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldCG.cginc"
        ENDCG
    }

    // SDF-driven hard cutoff. No anti-aliasing in shadow maps — gives clean silhouette
    // shadows (TMP_SDF and Unity Standard cutout do the same).
    Pass {
        Name "SDF_LIT_SHADOWCASTER"
        Tags { "LightMode" = "ShadowCaster" }

        ZWrite On
        ZTest LEqual
        Cull [_CullMode]
        ColorMask 0

        CGPROGRAM
        #pragma vertex   LightSideShadowVert
        #pragma fragment LightSideShadowFrag
        #pragma target   3.5

        #pragma multi_compile_shadowcaster

        #define LIGHTSIDE_SHADOW_CASTER
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldCG.cginc"
        ENDCG
    }
}
Fallback "LightSide/UI"
}
