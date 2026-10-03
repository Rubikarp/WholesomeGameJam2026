// Dissolve (Canvas) — burn-away reveal/hide. Logic lives in LightSide_Effect-Dissolve.hlsl; this shell
// only lists Properties and wires the Canvas pass. World variant: LightSide_Custom-World-Dissolve.shader.

Shader "LightSide/Custom/Dissolve"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
        [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
        [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
        [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0


        _NoiseTex       ("Noise",          2D)    = "white" {}
        _NoiseScale     ("Noise Scale",    Float) = 1.0
        _NoiseScroll    ("Noise Scroll (xy = speed, zw = static offset)", Vector) = (0, 0, 0, 0)

        _EdgeWidth      ("Edge Width",     Range(0, 0.3)) = 0.06
        _EdgeSoftness   ("Edge Softness",  Range(0, 0.2)) = 0.015
        [HDR] _EdgeColor ("Edge Color",    Color) = (2, 0.7, 0.1, 1)

        [HideInInspector] _LightSideInstUv2X ("Progress",            Range(0,1)) = 1
        [HideInInspector] _LightSideInstUv2Y ("Noise Scroll Offset", Float)      = 0

        _ClipRect       ("Clip Rect",       Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX  ("Mask SoftnessX",  Float) = 0
        _MaskSoftnessY  ("Mask SoftnessY",  Float) = 0

        _StencilComp    ("Stencil Comparison", Float) = 8
        _Stencil        ("Stencil ID",         Float) = 0
        _StencilOp      ("Stencil Operation",  Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask  ("Stencil Read Mask",  Float) = 255

        _CullMode       ("Cull Mode",  Float) = 0
        _ColorMask      ("Color Mask", Float) = 15
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
            Name "LightSide_CUSTOM_DISSOLVE"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../../LightSide_Custom.cginc"
            #include "LightSide_Effect-Dissolve.hlsl"

            fixed4 frag(LightSideVaryings i) : SV_Target
            {
                return LightSideApplyClipping(LightSideEffect(LightSideBuildFrag(i)), i.mask);
            }
            ENDCG
        }
    }
}
