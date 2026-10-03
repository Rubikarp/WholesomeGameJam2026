// Rainbow (Canvas) — logic in LightSide_Effect-Rainbow.hlsl. World variant: LightSide_Custom-World-Rainbow.shader.

Shader "LightSide/Custom/Rainbow"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
        [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
        [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
        [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0


        _HueScale   ("Hue Scale (per-glyph step)", Float) = 0.05
        _HueOffset  ("Hue Offset",  Float)   = 0
        _HueSpeed   ("Hue Speed",   Float)   = 0.15
        _Saturation ("Saturation",  Range(0,1)) = 1
        _Brightness ("Brightness",  Range(0,2)) = 1

        [HideInInspector] _LightSideInstUv2X ("Hue Offset", Range(0,1)) = 0

        _ClipRect       ("Clip Rect",         Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX  ("Mask SoftnessX",    Float)  = 0
        _MaskSoftnessY  ("Mask SoftnessY",    Float)  = 0

        _StencilComp    ("Stencil Comparison", Float) = 8
        _Stencil        ("Stencil ID",         Float) = 0
        _StencilOp      ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255

        _CullMode       ("Cull Mode",          Float) = 0
        _ColorMask      ("Color Mask",         Float) = 15
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }

        Stencil
        {
            Ref       [_Stencil]
            Comp      [_StencilComp]
            Pass      [_StencilOp]
            ReadMask  [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull   [_CullMode]
        ZWrite Off
        Lighting Off
        Fog { Mode Off }
        ZTest [unity_GUIZTestMode]
        Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
        BlendOp [_BlendOp], [_BlendOpAlpha]
        ColorMask [_ColorMask]

        Pass
        {
            Name "LightSide_CUSTOM_RAINBOW"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../../LightSide_Custom.cginc"
            #include "LightSide_Effect-Rainbow.hlsl"

            fixed4 frag(LightSideVaryings i) : SV_Target
            {
                return LightSideApplyClipping(LightSideEffect(LightSideBuildFrag(i)), i.mask);
            }
            ENDCG
        }
    }
}
