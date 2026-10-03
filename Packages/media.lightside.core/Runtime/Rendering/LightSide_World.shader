// Unlit SDF text rendering for LightSideWorld (world-space).
// Depth-tested and fogged like any scene geometry, and casts shadows through the same
// SDF-cutoff ShadowCaster the lit shader uses — it simply never samples scene light.
// This is the default world-space text shader; LightSide/Lit/SDF is the opt-in lit variant
// whose URP lighting keyword space is the reason the two are separate assets.
//
// Three SubShaders below — Unity picks by RenderPipeline tag: URP, HDRP, then the untagged
// built-in one. The shared CG program lives in LightSideWorldCG.cginc.

Shader "LightSide/World" {

Properties {
    [HideInInspector] _LightSidePaintTexture ("Paint Texture", 2D) = "white" {}
    [HideInInspector] _RendererColor ("Renderer Color", Color) = (1, 1, 1, 1)
    [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
    [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
    [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
    [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
    [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
    [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0

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

        #pragma multi_compile_fog
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define LIGHTSIDE_FORWARD_PASS
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldURP.hlsl"

        half4 PixShader(LightSideWorldVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            return LightSideApplyWorldFog(LightSideResolveSurface(input), input.fogCoord);
        }
        ENDHLSL
    }

    // Present so the URP 2D Renderer draws world text at all — it selects by this LightMode.
    // Unlit: no shape-light keywords, no normals pass, the surface goes out as resolved.
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
        #pragma multi_compile_instancing

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #define LIGHTSIDE_2D_PASS
        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldURP.hlsl"

        half4 LightSide2DFragment(LightSideWorldVaryings input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);
            return LightSideResolveSurface(input);
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
// mechanism that renders uGUI and sprites there). No ShadowCaster on purpose: the CG
// caster reads bias globals HDRP never populates. Pure CG — compiles in any project,
// so no PackageRequirements gate is needed.
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
        Name "SDF_WORLD_HDRP"

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

    // Untagged (no LightMode): an unlit transparent pass needs no ForwardBase data, and the
    // untagged form also renders under any SRP's SRPDefaultUnlit group.
    Pass {
        Name "SDF_WORLD"

        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]

        CGPROGRAM
        #pragma vertex   VertShader
        #pragma fragment PixShader
        #pragma target   3.5

        // Fragment-only keyword (paint resolve) — the vertex program never reads it.
        // SDF vs MSDF vs color is NOT a keyword: the per-glyph mode rides the vertex stream (UV1.w).
        #pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE
        #pragma multi_compile_fog

        #include "Packages/media.lightside.core/Runtime/Rendering/LightSideWorldCG.cginc"
        ENDCG
    }

    // SDF-driven hard cutoff. No anti-aliasing in shadow maps — gives clean silhouette
    // shadows (TMP_SDF and Unity Standard cutout do the same).
    Pass {
        Name "SDF_WORLD_SHADOWCASTER"
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
