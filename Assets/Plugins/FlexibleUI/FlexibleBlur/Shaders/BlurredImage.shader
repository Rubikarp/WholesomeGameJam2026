    Shader "Hidden/JeffGrawAssets/BlurredImage"
    {
        Properties
        {
            [PerRendererData] _MainTex("Sprite Texture", 2D) = "black" {}
            _StencilComp("Stencil Comparison", Float) = 8
            _Stencil("Stencil ID", Float) = 0
            _StencilOp("Stencil Operation", Float) = 0
            _StencilWriteMask("Stencil Write Mask", Float) = 255
            _StencilReadMask("Stencil Read Mask", Float) = 255
            _ColorMask("Color Mask", Float) = 15
            [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
        }

        SubShader
        {
            Tags
            {
                "Queue"= "Transparent"
                "IgnoreProjector"= "True"
                "RenderType"= "Transparent"
                "PreviewType"= "Plane"
                "CanUseSpriteAtlas"= "True"
            }
            Stencil
            {
                Ref[_Stencil]
                Comp[_StencilComp]
                Pass[_StencilOp]
                ReadMask[_StencilReadMask]
                WriteMask[_StencilWriteMask]
            }

            Cull Off
            Lighting Off
            ZWrite Off
            ZTest[unity_GUIZTestMode]
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask[_ColorMask]
            
            Pass
            {
                name "BlurredImage"

                CGPROGRAM

                #pragma vertex vert
                #pragma fragment frag
                #pragma target 2.0

                #include "UnityCG.cginc"
                #include "UnityUI.cginc"

                #pragma multi_compile_local __ HAS_BLUR
                #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
                #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
     
                CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                CBUFFER_END
                sampler2D _MainTex;
                float4 _ClipRect;
                fixed4 _TextureSampleAdd;
                int _UIVertexColorAlwaysGammaSpace;

                #ifdef UNITY_UI_CLIP_RECT
                float _UIMaskSoftnessX;
                float _UIMaskSoftnessY;
                #endif

                UNITY_DECLARE_SCREENSPACE_TEXTURE(_BlurTex);

                struct appdata_t
                {
                    float4 vertex : POSITION;
                    fixed4 color : COLOR;
                    float4 uvSourceImageFadeAndAlphaBlend: TEXCOORD0;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct v2f
                {
                    float4 vertex : SV_POSITION;
                    fixed4 color : COLOR0;
                    float4 texCoordAndScreenCoord : TEXCOORD0;
                    nointerpolation half2 SourceImageFadeAndAlphaBlend : TEXCOORD1;

                    #ifdef UNITY_UI_CLIP_RECT
                    float4 mask : TEXCOORD2;
                    #endif

                    UNITY_VERTEX_OUTPUT_STEREO
                };

                #define SourceImageFade(v) v.SourceImageFadeAndAlphaBlend.x
                #define AlphaBlend(v)      v.SourceImageFadeAndAlphaBlend.y
                #define TexCoord(v)        v.texCoordAndScreenCoord.xy
                #define ScreenCoord(v)     v.texCoordAndScreenCoord.zw

                v2f vert(const appdata_t v)
                {
                    v2f OUT;

                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_OUTPUT(v2f, OUT);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                    OUT.vertex = UnityObjectToClipPos(v.vertex);

                    #ifdef UNITY_UI_CLIP_RECT
                    const float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                    float2 pixelSize = OUT.vertex.w;
                    pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                    OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                    #endif

                    ScreenCoord(OUT) = ComputeNonStereoScreenPos(OUT.vertex).xy;
                    TexCoord(OUT) = TRANSFORM_TEX(v.uvSourceImageFadeAndAlphaBlend.xy, _MainTex);
                    SourceImageFade(OUT) = v.uvSourceImageFadeAndAlphaBlend.z;
                    AlphaBlend(OUT) = v.uvSourceImageFadeAndAlphaBlend.w;

                    OUT.color = v.color;
                    [branch] if (!IsGammaSpace() && _UIVertexColorAlwaysGammaSpace)
                        OUT.color.rgb = UIGammaToLinear(OUT.color.rgb);

                    return OUT;
                }

                half4 frag(v2f IN) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                    half4 spriteColor = tex2D(_MainTex, TexCoord(IN)) + _TextureSampleAdd;
                    half3 blurColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_BlurTex, UnityStereoTransformScreenSpaceTex(ScreenCoord(IN) * rcp(IN.vertex.w))).rgb;

                    half4 color;
                    [branch] if (IN.color.a >= 1)
                    {
                        color.rgb = lerp(blurColor, spriteColor.rgb * IN.color.rgb, SourceImageFade(IN));
                        color.a = spriteColor.a;
                    }
                    else
                    {
                        const half blurMaskAlpha = spriteColor.a * lerp(1, IN.color.a, AlphaBlend(IN));
                        const half3 blurLayer = blurColor * blurMaskAlpha;
                        spriteColor *= IN.color;
                        spriteColor.rgb *= spriteColor.a;
                        const half3 blendedRGB = lerp(blurLayer, spriteColor, SourceImageFade(IN));
                        const half blendedAlpha = lerp(blurMaskAlpha, spriteColor.a, SourceImageFade(IN));
                        color = half4(blendedRGB / blendedAlpha, blendedAlpha);
                    }

                    #ifdef UNITY_UI_CLIP_RECT
                    const half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                    color.a *= m.x * m.y;
                    #endif

                    #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                    #endif

                    return color;
                }
                ENDCG
            }
        }
    }