// Hologram (world) — world companion, same LightSide_Effect-Hologram.hlsl. Three SubShaders
// (URP / HDRP / built-in); URP and built-in carry their own ShadowCaster casting the plain face
// silhouette (a Fallback caster would misread TEXCOORD2 — MaterialModifier user data — as coverage
// data), HDRP renders its untagged color pass via SRPDefaultUnlit and casts no shadows.

Shader "LightSide/Custom/World Hologram"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
        [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
        [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
        [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0


        _NoiseTex   ("Noise", 2D) = "white" {}

        [HDR] _Tint ("Tint",       Color) = (1, 1, 1, 1)
        _HueScale   ("Hue Scale",  Float) = 0.003
        _HueSpeed   ("Hue Speed",  Float) = 0.1
        _Saturation ("Saturation", Range(0,1)) = 0.85
        _Brightness ("Brightness", Range(0,3)) = 1.1

        _ScanFreq     ("Scanline Freq",     Float) = 45
        _ScanSpeed    ("Scanline Speed",    Float) = -3
        _ScanContrast ("Scanline Contrast", Range(0,1)) = 0.45

        _FlickerScale  ("Flicker Noise Scale", Float) = 2
        _FlickerSpeed  ("Flicker Speed",       Float) = 0.7
        _FlickerAmount ("Flicker Amount",      Range(0,1)) = 0.25

        [HDR] _EdgeColor ("Edge Glow Color", Color) = (0.6, 0.9, 1.4, 1)
        _EdgeWidth       ("Edge Glow Width", Range(0, 0.3)) = 0.08

        [HideInInspector] _LightSideMeshPadding ("Quad Padding (em)", Float) = 0.12

        [HideInInspector] _LightSideInstUv2X ("Hue Phase Offset",     Range(0,1)) = 0
        [HideInInspector] _LightSideInstUv2Y ("Flicker Phase Offset", Float)      = 0
        [HideInInspector] _LightSideInstUv2Z ("Scan Phase Offset",    Float)      = 0
        [HideInInspector] _LightSideInstUv2W ("Intensity (0 = off)",  Range(0,2)) = 1

        _CullMode       ("Cull Mode",  Float) = 0
        _ColorMask      ("Color Mask", Float) = 15
    }

    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Cull [_CullMode] ZWrite Off ZTest LEqual
        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]
        ColorMask [_ColorMask]

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "../../LightSide_Custom-URP.hlsl"
            #include "LightSide_Effect-Hologram.hlsl"

            half4 frag(LightSideVaryings i) : SV_Target
            {
                half4 col = LightSideEffect(LightSideBuildFrag(i));
                LightSide_APPLY_FOG(col, i);
                return col;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_CullMode]

            HLSLPROGRAM
            #pragma vertex   LightSideShadowVert
            #pragma fragment LightSideShadowFrag
            #pragma target   3.5
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #define LightSide_SHADOW_CASTER
            #include "../../LightSide_Custom-URP.hlsl"
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "RenderPipeline" = "HDRenderPipeline" }

        Cull [_CullMode] ZWrite Off Lighting Off ZTest LEqual
        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]
        ColorMask [_ColorMask]

        Pass
        {
            Name "LightSide_CUSTOM_WORLD_HOLOGRAM_HDRP"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5

            #define LIGHTSIDE_WORLD
            #include "../../LightSide_Custom.cginc"
            #include "LightSide_Effect-Hologram.hlsl"

            fixed4 frag(LightSideVaryings i) : SV_Target
            {
                return LightSideEffect(LightSideBuildFrag(i));
            }
            ENDCG
        }
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Cull [_CullMode] ZWrite Off Lighting Off ZTest LEqual
        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]
        ColorMask [_ColorMask]

        Pass
        {
            Name "LightSide_CUSTOM_WORLD_HOLOGRAM"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5
            #pragma multi_compile_fog

            #define LIGHTSIDE_WORLD
            #include "../../LightSide_Custom.cginc"
            #include "LightSide_Effect-Hologram.hlsl"

            fixed4 frag(LightSideVaryings i) : SV_Target
            {
                fixed4 col = LightSideEffect(LightSideBuildFrag(i));
                LightSide_APPLY_FOG(col, i);
                return col;
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
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

            #define LightSide_SHADOW_CASTER
            #include "../../LightSide_Custom.cginc"
            ENDCG
        }
    }
}
