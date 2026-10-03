// Hologram (Canvas) — logic in LightSide_Effect-Hologram.hlsl. World variant: LightSide_Custom-World-Hologram.shader.

Shader "LightSide/Custom/Hologram"
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

        _ClipRect       ("Clip Rect",     Vector) = (-32767, -32767, 32767, 32767)
        _MaskSoftnessX  ("Mask SoftnessX", Float) = 0
        _MaskSoftnessY  ("Mask SoftnessY", Float) = 0

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
            Name "LightSide_CUSTOM_HOLOGRAM"
            CGPROGRAM
            #pragma vertex   LightSideVert
            #pragma fragment frag
            #pragma target   3.5

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "../../LightSide_Custom.cginc"
            #include "LightSide_Effect-Hologram.hlsl"

            fixed4 frag(LightSideVaryings i) : SV_Target
            {
                return LightSideApplyClipping(LightSideEffect(LightSideBuildFrag(i)), i.mask);
            }
            ENDCG
        }
    }
}
