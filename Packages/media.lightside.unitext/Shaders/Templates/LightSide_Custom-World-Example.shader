// World shell for a custom LightSide effect (LightSideWorld).
// You don't edit the programs below — write your visual logic in LightSide_Effect-Example.hlsl (the SAME
// file the Canvas shell includes) and list your Properties here. Three SubShaders: URP, HDRP and
// built-in, selected by RenderPipeline tag. World text is depth-tested (ZTest LEqual), not
// Canvas-clipped. URP and built-in carry their own ShadowCaster pass casting the plain face
// silhouette (a Fallback caster would misread TEXCOORD2 — MaterialModifier user data — as coverage
// data); HDRP has none — its untagged color pass renders via SRPDefaultUnlit, and the CG caster
// reads bias globals HDRP never populates.

Shader "LightSide/Custom/World Example"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
        [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
        [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
        [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0


        _Tint       ("Tint", Color) = (1, 1, 1, 1)

        _CullMode   ("Cull Mode",  Float) = 0
        _ColorMask  ("Color Mask", Float) = 15
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

            #include "../LightSide_Custom-URP.hlsl"
            #include "LightSide_Effect-Example.hlsl"

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
            #include "../LightSide_Custom-URP.hlsl"
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
            Name "LightSide_CUSTOM_WORLD_EXAMPLE_HDRP"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5

            #define LIGHTSIDE_WORLD
            #include "../LightSide_Custom.cginc"
            #include "LightSide_Effect-Example.hlsl"

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
            Name "LightSide_CUSTOM_WORLD_EXAMPLE"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5
            #pragma multi_compile_fog

            #define LIGHTSIDE_WORLD
            #include "../LightSide_Custom.cginc"
            #include "LightSide_Effect-Example.hlsl"

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
            #include "../LightSide_Custom.cginc"
            ENDCG
        }
    }
}
