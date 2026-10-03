// Canvas shell for a custom LightSide effect (LightSide on a Canvas).
// You don't edit the program below — write your visual logic in LightSide_Effect-Example.hlsl and list
// your Properties here. For the same effect on world text, ship LightSide_Custom-World-Example.shader
// with the SAME effect include (Canvas and World are separate assets — a Canvas+URP shader would have
// uGUI pick the URP SubShader for Canvas too and break clip/mask).

Shader "LightSide/Custom/Example"
{
    Properties
    {
        [HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
        [HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
        [HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
        [HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
        [HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
        [HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0


        _Tint           ("Tint", Color) = (1, 1, 1, 1)

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
            Name "LightSide_CUSTOM_EXAMPLE"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../LightSide_Custom.cginc"
            #include "LightSide_Effect-Example.hlsl"

            fixed4 frag(LightSideVaryings i) : SV_Target
            {
                return LightSideApplyClipping(LightSideEffect(LightSideBuildFrag(i)), i.mask);
            }
            ENDCG
        }
    }
}
