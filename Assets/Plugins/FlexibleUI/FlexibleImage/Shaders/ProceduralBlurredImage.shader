Shader "Hidden/JeffGrawAssets/ProceduralBlurredImage"
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
        _SrcBlend("Src Blend", Float) = 5  // SrcAlpha = 5
        _DstBlend("Dst Blend", Float) = 10 // OneMinusSrcAlpha = 10
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
        /// DO NOT MANUALLY EDIT THIS SECTION!!!
        /// SOFTMASK_PRESENT
        // [PerRendererData] _SoftMask ("Mask", 2D) = "white" {}
        /// SOFTMASK_END
        /// END OF NO TOUCH ZONE :)
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
        Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
        ColorMask[_ColorMask]

        Pass
        {
            name "ProceduralBlurredImage"

            CGPROGRAM

            #if SHADER_API_METAL || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_WEBGPU
                #define UINT_WORKAROUND
            #endif

            /// DO NOT MANUALLY EDIT THIS SECTION!!!
            /// Feature Flags - These can be be enabled/disabled via Window->FlexibleUI->FlexibleImage.
            #define FeatureSkew
            #define FeatureStroke
            #define FeatureCutout
            #define SubFeatureCutoutSimple
            #define SubFeatureCutoutSDF
            #define FeatureOutline
            #define FeatureProceduralGradient
            #define SubFeatureProceduralGradientSDF
            #define SubFeatureProceduralGradientAngle
            #define SubFeatureProceduralGradientRadial
            #define SubFeatureProceduralGradientConical
            #define SubFeatureProceduralGradientNoise
            #define SubFeatureProceduralGradientScreenSpaceOption
            #define SubFeatureProceduralGradientPointerAdjustPosOption
            #define FeaturePattern
            #define SubFeaturePatternLine
            #define SubFeaturePatternShape
            #define SubFeaturePatternGrid
            #define SubFeaturePatternFractal
            #define SubFeaturePatternSprite
            #define SubFeaturePatternScreenSpaceOption
            /// End Feature Flags
            /// END OF NO TOUCH ZONE :)

            #if !defined(SubFeatureProceduralGradientSDF) && !defined(SubFeatureProceduralGradientAngle) && !defined(SubFeatureProceduralGradientRadial) && !defined(SubFeatureProceduralGradientConical) && !defined(SubFeatureProceduralGradientNoise)
                #undef FeatureProceduralGradient
            #endif

            #if !defined(SubFeaturePatternLine) && !defined(SubFeaturePatternShape) && !defined(SubFeaturePatternGrid) && !defined(SubFeaturePatternFractal) && !defined(SubFeaturePatternSprite)
                #undef FeaturePattern
            #endif

            #if !defined(SubFeatureCutoutSimple) && !defined(SubFeatureCutoutSDF)
                #undef FeatureCutout
            #endif

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            /// DO NOT MANUALLY EDIT THIS SECTION!!!
            /// SOFTMASK_PRESENT
            // #include "Packages/com.olegknyazev.softmask/Assets/Shaders/Resources/SoftMask.cginc"
            /// SOFTMASK_END
            /// END OF NO TOUCH ZONE :)

            #pragma multi_compile_local __ HAS_BLUR
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            /// DO NOT MANUALLY EDIT THIS SECTION!!!
            /// SOFTMASK_PRESENT
            // #pragma multi_compile __ UNITY_UI_CLIP_RECT SOFTMASK_SIMPLE SOFTMASK_SLICED SOFTMASK_TILED
            /// SOFTMASK_NOT_PRESENT
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            /// SOFTMASK_END
            /// END OF NO TOUCH ZONE :)'

            static const float2 PatternShapeOriginLookup[5] = { float2(0.5, 0.5), float2(0., 0.5), float2(1., 0.5), float2(0.5, 1.0), float2(0.5, 0.0) };

            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float2 _MainTex_TexelSize;
            CBUFFER_END
            sampler2D _MainTex;
            float4 _ClipRect;
            float4 _ScreenSpacePointerPos;
            float4 _ScaledScreenParams;
            fixed4 _TextureSampleAdd;
            int _UIVertexColorAlwaysGammaSpace;

            #ifdef UNITY_UI_CLIP_RECT
                float _UIMaskSoftnessX;
                float _UIMaskSoftnessY;
            #endif

            #ifdef HAS_BLUR
                UNITY_DECLARE_SCREENSPACE_TEXTURE(_BlurTex);
            #endif

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                #ifdef UINT_WORKAROUND
                float4 uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW : TEXCOORD0;
                float4 proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW : TEXCOORD1;
                float4 chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW : TEXCOORD2;
                float4 sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW : TEXCOORD3;
                #else
                uint4 uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW : TEXCOORD0;
                uint4 proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW : TEXCOORD1;
                uint4 chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW : TEXCOORD2;
                uint4 sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW : TEXCOORD3;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            inline float2 UnpackFloat16_16_FixedPoint(const uint packed, int fracBits)
            {
                float invScale = exp2(-fracBits);
                uint hi = packed >> 16  & 65535;
                uint lo = packed        & 65535;
                return float2((float)hi * invScale, (float)lo * invScale);
            }

            inline float2 UnpackFloat15_15_FixedPoint(const uint packed, int fracBits)
            {
                float invScale = exp2(-fracBits);
                uint hi = (packed >> 24 & 127) << 8 | packed >> 15 & 255;
                uint lo = packed        & 32767;
                return float2((float)hi * invScale, (float)lo * invScale);
            }

            inline float2 UnpackFloat16_16(const uint packed)
            {
                uint a = packed >> 16 & 65535;
                uint b = packed & 65535;
                return float2(a / 65535., b / 65535.);
            }

            inline float4 UnpackFloat12_12_5_3(const uint packed)
            {
                uint a = packed >> 20 & 4095;
                uint b = packed >> 8 & 4095;
                uint c = packed >> 3 & 31;
                uint d = packed & 7;
                return float4(a / 4095., b / 4095., c / 31., d / 7.);
            }

            inline float3 UnpackFloat16_14_2(const uint packed)
            {
                uint a = packed >> 16 & 65535;
                uint b = packed >> 2 & 16383;
                uint c = packed & 3;
                return float3(a / 65535., b / 16383., c / 3.);
            }

            inline float3 UnpackFloat12_12_8(const uint packed)
            {
                uint2 ab = uint2(packed >> 20 & 4095, packed >> 8 & 4095);
                uint c = packed & 255;
                return float3(ab / 4095., c / 255.);
            }

            inline float3 UnpackFloat8_12_12(const uint packed)
            {
                uint a = packed >> 24 & 255;
                uint2 bc = uint2(packed >> 12 & 4095, packed & 4095);
                return float3(a / 255., bc / 4095.); 
            }

            inline float4 UnpackFloat8_8_8_8(const uint packed)
            {
                uint4 abcd = uint4(
                    packed >> 24 & 255,
                    packed >> 16 & 255,
                    packed >> 8 & 255,
                    packed & 255
                );
                return abcd / 255.;
            }

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR0;
                fixed4 outlineColor : COLOR1;
                fixed4 proceduralGradientColor : COLOR2;
                fixed4 patternColor : COLOR3;
                nointerpolation float4 proceduralGradientParams : COLOR4;
                nointerpolation float4 concavity : COLOR5;
                nointerpolation int4 flagsAndEdgeCollapseIdx : COLOR6;
                float4 texcoordAndInnerTexCoord : TEXCOORD0;
                float4 screenCoordEdgeCollapsePosAndEdgeCollapseAmt : TEXCOORD1;
                nointerpolation float4 strokeSoftnessSourceImageFadeAndAlphaBlend : TEXCOORD2;
                nointerpolation float4 chamfer : TEXCOORD3;
                nointerpolation float4 cutout : TEXCOORD4;
                nointerpolation float4 gradientStrengthPatternParam1And2AndOutlineWidth : TEXCOORD5;
                nointerpolation float4 widthHeightAndAspectAndPrecalc : TEXCOORD6;

                #ifdef UNITY_UI_CLIP_RECT
                    float4 mask : TEXCOORD7;
                #elif defined(__SOFTMASK_ENABLE)
                    SOFTMASK_COORDS(7)
                #else
                    nointerpolation float4 precalcs : TEXCOORD7;
                #endif

                UNITY_VERTEX_OUTPUT_STEREO
            };

            #define CutoutRuleIsOr(v)                   (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 0))
            #define InvertCutout(v)                     (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 1))
            #define CutoutOnlyAffectsOutline(v)         (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 2))
            #define ProceduralGradientAlphaIsBlend(v)   (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 3))
            #define SDFCutoutIsSquircle(v)              (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 3))
            #define GradientAffectsInterior(v)          (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 4))
            #define GradientAffectsOutline(v)           (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 5))
            #define SDFCutoutMirrorHorizontal(v)        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 4))
            #define SDFCutoutMirrorVertical(v)          (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 5))
            #define ScreenSpaceProceduralGradient(v)    (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 6))
            #define SDFCutoutBothInteriorAndOutline(v)  (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 6))
            #define OutlineAlphaIsBlend(v)              (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 7))
            #define FadeOutlineToPerimeter(v)           (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 8))
            #define OutlineExpandsOutwards(v)           (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 9))
            #define PatternAffectsInterior(v)           (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 10))
            #define PatternAffectsOutline(v)            (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 11))
            #define ScreenSpacePattern(v)               (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 12))
            #define CollapseToParallelogram(v)          (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 13))
            #define OutlineAccommodatesCollapsedEdge(v) (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 14))
            #define InvertGradient(v)                   (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 15))
            #define SDFCutoutMirrorIsDiagonal(v)        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 15))
            #define PatterAlphaIsBlend(v)               (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 16))
            #define HasSecondInteriorOutline(v)         (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 17))
            #define ShowSprite(v)                       (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 18))
            #define ProceduralGradientPosFromPointer(v) (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 19))
            #define IsSquircle(v)                       (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 20))
            #define UsingSecondaryPatternMode(v)        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 21))
            #define SoftPattern(v)                      (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 22))
            #define PatternSpriteAngleIsUpper(v)        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 22))
            #define MirrorCollapse(v)                   (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 23))
            #define RotateSpritePatternOffset(v)        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 24))
            #define HasStroke(v)                        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 25))
            #define HasSkew(v)                          (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 26))
            #define HasChamfer(v)                       (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 27))
            #define HasCutout(v)                        (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 29))
            #define HasPattern(v)                       (bool)(v.flagsAndEdgeCollapseIdx.x & (1 << 30))
            #define GradientType(v)                     v.flagsAndEdgeCollapseIdx.y
            #define IsSDFOrNoiseGradient(v)             (bool)((GradientType(v) & 3) == 0)
            #define IsSDFGradient(v)                    (bool)(GradientType(v) == 0)
            #define IsAngleGradient(v)                  (bool)(GradientType(v) == 1)
            #define IsRadialGradient(v)                 (bool)(GradientType(v) == 2)
            #define IsConicalGradient(v)                (bool)(GradientType(v) == 3)
            #define IsNoiseGradient(v)                  (bool)(GradientType(v) == 4)
            #define HasProceduralGradient(v)            (bool)(GradientType(v) < 6)

            #define HasSDFCutout(v)                     (bool)(GradientType(v) == 6)
            #define SDFCutoutChamfer(v)                 v.cutout
            #define SDFCutoutConcavity(v)               v.proceduralGradientColor

            #define PatternType(v)                      v.flagsAndEdgeCollapseIdx.z
            #define EdgeCollapseIdx(v)                  v.flagsAndEdgeCollapseIdx.w
            #define AlphaBlend(v)                       v.strokeSoftnessSourceImageFadeAndAlphaBlend.w
            #define PatternLineThickness(v)             v.strokeSoftnessSourceImageFadeAndAlphaBlend.w
            #define PatternSpriteAngle(v)               v.strokeSoftnessSourceImageFadeAndAlphaBlend.w
            #define ScreenCoord(v)                      v.screenCoordEdgeCollapsePosAndEdgeCollapseAmt.xy
            #define Stroke(v)                           v.strokeSoftnessSourceImageFadeAndAlphaBlend.x
            #define Softness(v)                         v.strokeSoftnessSourceImageFadeAndAlphaBlend.y
            #define SourceImageFade(v)                  v.strokeSoftnessSourceImageFadeAndAlphaBlend.z
            #define GradientStrength(v)                 v.gradientStrengthPatternParam1And2AndOutlineWidth.x
            #define Angle(v)                            v.gradientStrengthPatternParam1And2AndOutlineWidth.x
            #define PatternParam2(v)                    v.gradientStrengthPatternParam1And2AndOutlineWidth.y
            #define PatternParam1(v)                    v.gradientStrengthPatternParam1And2AndOutlineWidth.z
            #define OutlineWidth(v)                     v.gradientStrengthPatternParam1And2AndOutlineWidth.w
            #define EdgeCollapsePos(v)                  v.screenCoordEdgeCollapsePosAndEdgeCollapseAmt.z
            #define EdgeCollapseAmt(v)                  v.screenCoordEdgeCollapsePosAndEdgeCollapseAmt.w
            #define TexCoord(v)                         v.texcoordAndInnerTexCoord.xy
            #define InnerTexCoord(v)                    v.texcoordAndInnerTexCoord.zw
            #define TexWidthHeight(v)                   v.widthHeightAndAspectAndPrecalc.xy
            #define InnerAspect(v)                      v.widthHeightAndAspectAndPrecalc.z
            #define OutlineAdjustedWidth(v)             v.widthHeightAndAspectAndPrecalc.w

            v2f vert(const appdata_t v)
            {
                v2f OUT;

                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.vertex = UnityObjectToClipPos(v.vertex);

                #if UNITY_UI_CLIP_RECT
                    const float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                    float2 pixelSize = OUT.vertex.w;
                    pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScaledScreenParams.xy));
                    OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                #elif !defined(__SOFTMASK_ENABLE)
                    OUT.precalcs = float4(1, 1, 1, 1);
                #endif

                ScreenCoord(OUT) = ComputeNonStereoScreenPos(OUT.vertex).xy;

                #ifdef UINT_WORKAROUND
                    const uint vProceduralGradientParamsXY = asuint(v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.z);
                    const uint vProceduralGradientParamsZW = asuint(v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.w);
                    const uint vConcavity = asuint(v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.z);
                    const uint vChamferXW = asuint(v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.x);
                    const uint vChamferZW = asuint(v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.y);
                    const uint vTexCoordAndFlagsOne = asuint(v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.x);
                #else
                    const uint vProceduralGradientParamsXY = v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.z;
                    const uint vProceduralGradientParamsZW = v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.w;
                    const uint vConcavity = v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.z;
                    const uint vChamferXW = v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.x;
                    const uint vChamferZW = v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.y;
                    const uint vTexCoordAndFlagsOne = v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.x;
                #endif

                OUT.proceduralGradientParams.xy = UnpackFloat16_16(vProceduralGradientParamsXY);
                OUT.proceduralGradientParams.zw = UnpackFloat16_16(vProceduralGradientParamsZW);
                OUT.concavity = UnpackFloat8_8_8_8(vConcavity);
                OUT.chamfer.xy = UnpackFloat16_16(vChamferXW);
                OUT.chamfer.zw = UnpackFloat16_16(vChamferZW);
                OUT.chamfer *= 4095.9375;
                float3 texCoodAndFlagsOne = UnpackFloat12_12_8(vTexCoordAndFlagsOne);
                texCoodAndFlagsOne.xy *= 3;
                texCoodAndFlagsOne.xy -= 1;
                TexCoord(OUT) = TRANSFORM_TEX(texCoodAndFlagsOne.xy, _MainTex);
                OUT.flagsAndEdgeCollapseIdx.x = texCoodAndFlagsOne.z * 255;

                #ifdef UINT_WORKAROUND
                    const uint vGradientStrengthPatternParam2AndEdgeCollapseIdx = asuint(v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.w);
                    const uint vPatternParam1OutlineWidthPatternModeGradientMode = asuint(v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.w);
                #else
                    const uint vGradientStrengthPatternParam2AndEdgeCollapseIdx = v.chamferXY_concavityZ_GradientStrengthPatternParam2EdgeIdxW.w;
                    const uint vPatternParam1OutlineWidthPatternModeGradientMode = v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.w;
                #endif

                const float3 gradientStrengthPatternParam2AndEdgeCollapseIdx = UnpackFloat16_14_2(vGradientStrengthPatternParam2AndEdgeCollapseIdx);
                GradientStrength(OUT) = gradientStrengthPatternParam2AndEdgeCollapseIdx.x;
                PatternParam2(OUT) = gradientStrengthPatternParam2AndEdgeCollapseIdx.y;
                EdgeCollapseIdx(OUT) = gradientStrengthPatternParam2AndEdgeCollapseIdx.z * 3;

                const float4 patternParam1OutlineWidthPatternModeGradientMode = UnpackFloat12_12_5_3(vPatternParam1OutlineWidthPatternModeGradientMode);
                PatternParam1(OUT) = patternParam1OutlineWidthPatternModeGradientMode.x;
                OutlineWidth(OUT) = patternParam1OutlineWidthPatternModeGradientMode.y * 511.875;
                PatternType(OUT) = patternParam1OutlineWidthPatternModeGradientMode.z * 31. - 1;
                GradientType(OUT) = patternParam1OutlineWidthPatternModeGradientMode.w * 7;

                #ifdef UINT_WORKAROUND
                    const uint vEdgeCollapsePosCollapseAmtFlagsTwo = asuint(v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.y);
                    const uint vWidthHeight = asuint(v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.w);
                #else
                    const uint vEdgeCollapsePosCollapseAmtFlagsTwo = v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.y;
                    const uint vWidthHeight = v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.w;
                #endif

                const float3 edgeCollapsePosCollapseAmtFlagsTwo = UnpackFloat12_12_8(vEdgeCollapsePosCollapseAmtFlagsTwo);
                EdgeCollapsePos(OUT) = edgeCollapsePosCollapseAmtFlagsTwo.x;
                EdgeCollapseAmt(OUT) = edgeCollapsePosCollapseAmtFlagsTwo.y;
                OUT.flagsAndEdgeCollapseIdx.x |= (uint)(edgeCollapsePosCollapseAmtFlagsTwo.z * 255) << 8;
                TexWidthHeight(OUT) = UnpackFloat16_16_FixedPoint(vWidthHeight, 2);
                InnerAspect(OUT) = max(TexWidthHeight(OUT).x, TexWidthHeight(OUT).y) / min(TexWidthHeight(OUT).x, TexWidthHeight(OUT).y);

                #ifdef UINT_WORKAROUND
                    const uint vSourceImageFadeCutoutXY = asuint(v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.x);
                    const uint vAlphaBlendCutoutZW = asuint(v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.y);
                    const uint vStrokeSoftnessFlagsThree = asuint(v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.z);
                #else
                    const uint vSourceImageFadeCutoutXY = v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.x;
                    const uint vAlphaBlendCutoutZW = v.sourceImageFadeCutoutOneX_alphaBlendCutoutTwoY_procedualGradientParamsZW.y;
                    const uint vStrokeSoftnessFlagsThree = v.uvFlagsOneX_collapsePositionAmountFlagsTwoY_StrokeSoftnessFlagsThreeZ_WidthHeightW.z;
                #endif

                const float3 sourceImageFadeCutoutXY = UnpackFloat8_12_12(vSourceImageFadeCutoutXY);
                OUT.strokeSoftnessSourceImageFadeAndAlphaBlend.z = sourceImageFadeCutoutXY.x;
                OUT.cutout.xy = sourceImageFadeCutoutXY.yz;
                const float3 alphaBlendCutoutZW = UnpackFloat8_12_12(vAlphaBlendCutoutZW);
                OUT.strokeSoftnessSourceImageFadeAndAlphaBlend.w = alphaBlendCutoutZW.x;
                OUT.cutout.zw = alphaBlendCutoutZW.yz;

                [branch] if (HasSDFCutout(OUT))
                    OUT.cutout *= 1023.75;
                else
                    OUT.cutout = mad(OUT.cutout, 1023.75, -0.25);

                const float3 strokeSoftnessFlagsThree = UnpackFloat12_12_8(vStrokeSoftnessFlagsThree);

                OUT.strokeSoftnessSourceImageFadeAndAlphaBlend.xy = strokeSoftnessFlagsThree.xy;
                OUT.flagsAndEdgeCollapseIdx.x |= (uint)(strokeSoftnessFlagsThree.z * 255) << 16;
                OUT.flagsAndEdgeCollapseIdx.x |= bool(Stroke(OUT) < 1.) << 25;
                OUT.flagsAndEdgeCollapseIdx.x |= bool(EdgeCollapseAmt(OUT) > 0 || OutlineAccommodatesCollapsedEdge(OUT)) << 26;
                OUT.flagsAndEdgeCollapseIdx.x |= any(OUT.chamfer > 0) << 27;
                OUT.flagsAndEdgeCollapseIdx.x |= (!HasSkew(OUT) && !HasChamfer(OUT)) << 28;
                OUT.flagsAndEdgeCollapseIdx.x |= (bool)PatternParam2(OUT) << 30;

                Softness(OUT) *= 255.9375;
                Softness(OUT) = max(Softness(OUT), 0.001h);
                Stroke(OUT) *= 0.25 * min(TexWidthHeight(OUT).x, TexWidthHeight(OUT).y) + (OutlineExpandsOutwards(OUT)
                                                                                        ? OutlineWidth(OUT) * 0.5
                                                                                        : 0);
                [branch] if (IsSquircle(OUT))
                    OUT.concavity = lerp(2., 10., OUT.concavity);
                else
                    OUT.concavity *= 1.9921875;

#ifdef FeatureProceduralGradient
#if defined(FeatureCutout) && defined(SubFeatureCutoutSDF)
                [branch] if (HasSDFCutout(OUT))
                {
                    OUT.proceduralGradientParams.xy = UnpackFloat15_15_FixedPoint(vProceduralGradientParamsXY, 1) - 8192;
                    OUT.proceduralGradientParams.zw = UnpackFloat15_15_FixedPoint(vProceduralGradientParamsZW, 1);
                    Angle(OUT) *= -UNITY_TWO_PI;
                    #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                        sincos(Angle(OUT), Angle(OUT), OUT.precalcs.y);
                    #endif
                }
                else
#endif // FeatureCutout && SubFeatureCutoutSDF
                [branch]
#if defined(SubFeatureProceduralGradientSDF) || defined(SubFeatureProceduralGradientNoise)
                if (IsSDFOrNoiseGradient(OUT))
                {
                    [branch] if (IsSDFGradient(OUT))
                    {
                        OUT.proceduralGradientParams.xy *= 4095.9375;
                        OUT.proceduralGradientParams.zw = max(Softness(OUT) + 0.001h, OUT.proceduralGradientParams.zw * 2160);
                        GradientStrength(OUT) = mad(GradientStrength(OUT), 2, 0.1);
                        if (!GradientAffectsOutline(OUT) && !ScreenSpaceProceduralGradient(OUT))
                            OUT.proceduralGradientParams.xy += OutlineWidth(OUT);
                    }
                    else // noise gradient
                    {
                        OUT.proceduralGradientParams.y *= 0.01;
                        OUT.proceduralGradientParams.y += 0.1031;
                        OUT.proceduralGradientParams.z *= 8;
                        OUT.proceduralGradientParams.w *= 2;
                        OUT.proceduralGradientParams.w -= 1;
                    }

                    #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                        if (ProceduralGradientPosFromPointer(OUT))
                            OUT.precalcs.y = ScreenSpaceProceduralGradient(OUT) ? _ScaledScreenParams.x / _ScaledScreenParams.y : TexWidthHeight(OUT).x / TexWidthHeight(OUT).y;
                    #endif
                }
                else
#endif // SubFeatureProceduralGradientSDF || SubFeatureProceduralGradientNoise
#ifdef SubFeatureProceduralGradientConical
                if (IsConicalGradient(OUT))
                {
                    OUT.flagsAndEdgeCollapseIdx.x ^= 1u << 15;
                    OUT.proceduralGradientParams.z = 2 * (1 - OUT.proceduralGradientParams.z);
                    OUT.proceduralGradientParams.w = (OUT.proceduralGradientParams.w - 0.5) * 320;
                    Angle(OUT) *= -UNITY_TWO_PI;
                    #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                        OUT.precalcs.y = ScreenSpaceProceduralGradient(OUT) ? _ScaledScreenParams.x / _ScaledScreenParams.y : TexWidthHeight(OUT).x / TexWidthHeight(OUT).y;
                    #endif
                }
                else // angle or radial gradient
#endif // SubFeatureProceduralGradientConical
                {
                    if (!InvertGradient(OUT) && IsAngleGradient(OUT)) // if one dimension has a value, assign at least a small value to the other dimension so that something is visible.
                        OUT.proceduralGradientParams.zw = max(OUT.proceduralGradientParams.zw, 0.005);
                
                    OUT.flagsAndEdgeCollapseIdx.x ^= 1u << 15;
                    OUT.proceduralGradientParams.xy = mad(OUT.proceduralGradientParams.xy, 2, -0.5);

#if defined(SubFeatureProceduralGradientScreenSpaceOption) && defined(SubFeatureProceduralGradientPointerAdjustPosOption)
                    if (ScreenSpaceProceduralGradient(OUT) && ProceduralGradientPosFromPointer(OUT))
                        OUT.proceduralGradientParams.xy += _ScreenSpacePointerPos.zw - 0.5;
#endif //SubFeatureProceduralGradientScreenSpaceOption || SubFeatureProceduralGradientPointerAdjustPosOption 

                    OUT.proceduralGradientParams.zw = max(OUT.proceduralGradientParams.zw, 1e-12); // avoid div-by-zero
                    OUT.proceduralGradientParams.zw = (rcp(OUT.proceduralGradientParams.zw) - 1) * 5;
#ifdef SubFeatureProceduralGradientAngle
                    [branch] if (IsAngleGradient(OUT))
                    {
                        Angle(OUT) *= -UNITY_TWO_PI;
                        #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                            sincos(Angle(OUT), Angle(OUT), OUT.precalcs.y);
                        #endif
                    }
                    else // radial gradient
#endif // SubFeatureProceduralGradientAngle
                    {
                        GradientStrength(OUT) *= 2;
                    }
                }
#endif // FeatureProceduralGradient

                float adjustedSoftness = Softness(OUT);
                [branch] if (OutlineAccommodatesCollapsedEdge(OUT))
                {
                    float accommodationMod = 2.41421356237 * InnerAspect(OUT);
                    OutlineWidth(OUT) *= accommodationMod;
                    adjustedSoftness *= accommodationMod;
                }
                InnerAspect(OUT) = 0.41421356237 / InnerAspect(OUT); // precalculating adjustment that is used in every subsequent usage in the fragment program.

                OutlineAdjustedWidth(OUT) = (OutlineWidth(OUT) + adjustedSoftness * smoothstep(0.h, 4.h, Softness(OUT))) * 0.5h;

                [branch] if (OutlineExpandsOutwards(OUT))
                {
                    const float2 baseSize = TexWidthHeight(OUT);
                    TexWidthHeight(OUT) += 2.0 * OutlineWidth(OUT);
                    const float2 outlineExpansionScale = TexWidthHeight(OUT) / baseSize;
                    InnerTexCoord(OUT) = mad(TexCoord(OUT) - 0.5, outlineExpansionScale, 0.5);
                    
                    [branch] if (OutlineAccommodatesCollapsedEdge(OUT) && CollapseToParallelogram(OUT))
                        EdgeCollapseAmt(OUT) = min(EdgeCollapseAmt(OUT), 0.9999);

                    [branch] if (HasSecondInteriorOutline(OUT))
                        Stroke(OUT) += OutlineAdjustedWidth(OUT);
                }
                else
                {
                    InnerTexCoord(OUT) = TexCoord(OUT);
                    [branch] if (MirrorCollapse(OUT))
                        EdgeCollapseAmt(OUT) = min(EdgeCollapseAmt(OUT), 0.9999);
                }

                #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                    if (OutlineAccommodatesCollapsedEdge(OUT))
                        OUT.precalcs.x = mad(InnerAspect(OUT), 2, -1);
                #endif
                
#if defined(FeatureCutout) && defined(SubFeatureCutoutSDF)
                [branch] if (HasSDFCutout(OUT))
                {
                    OUT.proceduralGradientParams.zw += 2 + Softness(OUT);
                    [branch] if (SDFCutoutMirrorIsDiagonal(OUT))
                        OUT.proceduralGradientParams.zw = OUT.proceduralGradientParams.wz;
                }
                else
#endif // FeatureCutout && SubFeatureCutoutSDF
                {
                    OUT.flagsAndEdgeCollapseIdx.x |= any(OUT.cutout >= 0) << 29;
                    const float2 midExtent = ceil(float2(TexWidthHeight(OUT) * 0.5));
                    OUT.cutout = OUT.cutout < 0. ? midExtent.xxyy : OUT.cutout - 1;
                    OUT.cutout.yz = TexWidthHeight(OUT) - OUT.cutout.yz;
                }

                #ifdef UINT_WORKAROUND
                    const uint vProceduralGradientColor = asuint(v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.x);
                    const uint vOutlineColor = asuint(v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.y);
                    const uint vPatternColor = asuint(v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.z);
                #else
                    const uint vProceduralGradientColor = v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.x;
                    const uint vOutlineColor = v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.y;
                    const uint vPatternColor = v.proceduralGradientColorX_outlineColorY_patterColorZ_patternParam1OutlineWidthPatternModeGradientModeW.z;
                #endif

                OUT.color = v.color;
                OUT.proceduralGradientColor = UnpackFloat8_8_8_8(vProceduralGradientColor);
                [branch] if (HasSDFCutout(OUT))
                {
                    [branch] if (SDFCutoutIsSquircle(OUT))
                        SDFCutoutConcavity(OUT) = lerp(2., 10., SDFCutoutConcavity(OUT));
                    else
                        SDFCutoutConcavity(OUT) *= 1.9921875;
                }
                OUT.outlineColor = UnpackFloat8_8_8_8(vOutlineColor);
                OUT.patternColor = UnpackFloat8_8_8_8(vPatternColor);

                [branch] if (!IsGammaSpace())
                {
                    // UIGammaToLinear will molest values at 255 down to 254, but adding a small espilon will molest many values <254, so we will only add the epsilon for components at ~255.
                    [branch] if (_UIVertexColorAlwaysGammaSpace)
                    {
                        float3 colorHP = OUT.color.rgb + step(0.999, OUT.color.rgb) * 0.001;
                        OUT.color.rgb = UIGammaToLinear(colorHP);
                    }

                    [branch] if (!HasSDFCutout(OUT))
                    {
                        float3 proceduralGradientHP = OUT.proceduralGradientColor.rgb + step(0.999, OUT.proceduralGradientColor.rgb) * 0.001;
                        OUT.proceduralGradientColor.rgb = UIGammaToLinear(proceduralGradientHP);
                    }
                    float3 outlineHP = OUT.outlineColor.rgb + step(0.999, OUT.outlineColor.rgb) * 0.001;
                    OUT.outlineColor.rgb = UIGammaToLinear(outlineHP);
                    float3 patternColorHP = OUT.patternColor.rgb + step(0.999, OUT.patternColor.rgb) * 0.001;
                    OUT.patternColor.rgb = UIGammaToLinear(patternColorHP);
                }

#ifdef FeaturePattern
                [branch] if (PatternParam2(OUT) > 0)
                {
                    #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                        [branch] if (PatternType(OUT) != 25)
                        {
                            const float lineRatio = max(0.001h, PatternLineThickness(OUT));
                            sincos(lineRatio * UNITY_PI, OUT.precalcs.z, OUT.precalcs.w);
                        }
                    #endif

#if defined(SubFeaturePatternSprite) || defined(SubFeaturePatternShape)
                    [branch] if (PatternType(OUT) < 23) // lines or shapes
                    {
                        [branch] if (!UsingSecondaryPatternMode(OUT))
                        {
                            PatternParam1(OUT) = mad(PatternParam1(OUT), 4095./2048., -2047./2048.);
                            PatternParam1(OUT) *= 1080 * _Time;
                        }
                        else
                        {
                            PatternParam1(OUT) *= UNITY_TWO_PI;
                        }

                        if (PatternType(OUT) != 1)
                            PatternParam1(OUT) *= -1;

                        if (PatternType(OUT) == 0 || PatternType(OUT) == 2 || PatternType(OUT) >= 8)
                            PatternParam2(OUT) *= 1.41421356237;

                        PatternParam1(OUT) += 1.57125;
                        PatternParam2(OUT) *= 2;
                    }
                    else
#endif // SubFeaturePatternSprite || SubFeaturePatternShape
#ifdef SubFeaturePatternFractal
                    if (PatternType(OUT) == 24) // fractals
                    {
                        float temp = PatternParam1(OUT);
                        PatternParam1(OUT) = PatternParam2(OUT) * 0.15;
                        PatternParam2(OUT) = temp;
                        PatternParam2(OUT) *= 2;
                        PatternParam2(OUT) -= 1;
                        const float neg1To1Range = abs(PatternParam2(OUT));
                        PatternParam2(OUT) = PatternParam2(OUT) < 0
                            ? lerp(-0.05, -0.5, neg1To1Range)
                            : lerp(0.05,  0.5, neg1To1Range);
                    }
                    else
#endif // SubFeaturePatternFractal
#ifdef SubFeaturePatternSprite
                    if (PatternType(OUT) == 25) // sprite
                    {
                        PatternParam2(OUT) *= 0.1;
                        [branch] if (!UsingSecondaryPatternMode(OUT))
                        {
                            PatternParam1(OUT) = mad(PatternParam1(OUT), 4095./2048., -2047./2048.);
                            PatternParam1(OUT) *= 256 * _Time;
                        }

                        #ifndef HAS_BLUR
                            PatternSpriteAngle(OUT) *= 255;
                            if (PatternSpriteAngleIsUpper(OUT))
                                PatternSpriteAngle(OUT) += 255;
                            PatternSpriteAngle(OUT) /= 360;

                            [branch] if (PatternSpriteAngle(OUT) > 1)
                            {
                                OUT.flagsAndEdgeCollapseIdx.x |= 1 << 24;
                                PatternSpriteAngle(OUT) -= 1.00277777;
                                [branch] if (round(PatternSpriteAngle(OUT) * 360) % 2 != 0)
                                    PatternParam1(OUT) *= 1.41421356237;

                                PatternSpriteAngle(OUT) *= 45;
                            }
                        
                            PatternSpriteAngle(OUT) *= UNITY_TWO_PI;

                            #if !defined(UNITY_UI_CLIP_RECT) && !defined(__SOFTMASK_ENABLE)
                                sincos(PatternSpriteAngle(OUT), OUT.precalcs.z, OUT.precalcs.w);
                                PatternSpriteAngle(OUT) = _MainTex_TexelSize.y * rcp(_MainTex_TexelSize.x);
                                if (RotateSpritePatternOffset(OUT))
                                    OUT.precalcs.zw *= -PatternParam1(OUT);
                            #endif
                        #endif
                    }
                    else //grids
#endif // SubFeaturePatternSprite
                    {
#ifdef SubFeaturePatternGrid
                        PatternParam1(OUT) = lerp(-0.25 * PatternParam2(OUT), 0.5 + 0.25 * PatternParam2(OUT), 1 - PatternParam1(OUT));
                        PatternParam2(OUT) *= (uint)PatternType(OUT) % 2 == 1 ? 0.25 : 0.375;
#endif // SubFeaturePatternGrid
                    }
                }
#endif // FeaturePattern
                #ifdef __SOFTMASK_ENABLE
                    SOFTMASK_CALCULATE_COORDS(OUT, v.vertex);
                #endif

                return OUT;
            }

            inline float solveConcavityCorner(const float2 edgePair, const float chamfer, const float concavity)
            {
                const float curved = chamfer - length(edgePair - chamfer);
                const float flat = mad(2./3., edgePair.x + edgePair.y, -1./3. * chamfer);
                return lerp(curved, flat, concavity);
            }

            inline float solveSquircleCorner(const float2 edgePair, const float chamfer, const float smoothing)
            {
                const float2 d = chamfer - edgePair;
                return chamfer - pow(pow(d.x, smoothing) + pow(d.y, smoothing), rcp(smoothing));
            }

            float sdfRectangle(const float2 pos, const float2 widthHeight, const float4 chamfer, const float4 concavity)
            {
                const float4 edgeDistances = float4(pos, widthHeight - pos);
                float sdf = min(min(edgeDistances.x, edgeDistances.y), min(edgeDistances.z, edgeDistances.w));
                [branch] if (all(edgeDistances.xw < chamfer.x)) // Top-Left
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.xw, chamfer.x, concavity.x));
                [branch] if (all(edgeDistances.zw < chamfer.y)) // Top-Right
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.zw, chamfer.y, concavity.y));
                [branch] if (all(edgeDistances.xy < chamfer.z)) // Bottom-Left
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.xy, chamfer.z, concavity.z));
                [branch] if (all(edgeDistances.zy < chamfer.w)) // Bottom-Right
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.zy, chamfer.w, concavity.w));
                return sdf;
            }

            float sdfSquircle(const float2 pos, const float2 widthHeight, const float4 chamfer, const float4 chamferSmoothing)
            {
                const float4 edgeDistances = float4(pos, widthHeight - pos);
                float sdf = min(min(edgeDistances.x, edgeDistances.y), min(edgeDistances.z, edgeDistances.w));
                [branch] if (all(edgeDistances.xw < chamfer.x)) // Top-Left
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.xw, chamfer.x, chamferSmoothing.x));
                [branch] if (all(edgeDistances.zw < chamfer.y)) // Top-Right
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.zw, chamfer.y, chamferSmoothing.y));
                [branch] if (all(edgeDistances.xy < chamfer.z)) // Bottom-Left
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.xy, chamfer.z, chamferSmoothing.z));
                [branch] if (all(edgeDistances.zy < chamfer.w)) // Bottom-Right
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.zy, chamfer.w, chamferSmoothing.w));
                return sdf;
            }

            float quadrilateralConcavityCorners(float sdf, const uint collapseEdgeIdx, const float collapaseAmount, const float4 chamfer, const float4 concavity, const float4 edgeDistances)
            {
                [branch] if (all(edgeDistances.xz < chamfer.x)) // Top-Left
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.xz, chamfer.x, concavity.x));
                [branch] if (all(edgeDistances.xw < chamfer.y)) // Top-Right
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.xw, chamfer.y, concavity.y));
                [branch] if (all(edgeDistances.yz < chamfer.z)) // Bottom-Left
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.yz, chamfer.z, concavity.z));
                [branch] if (all(edgeDistances.yw < chamfer.w)) // Bottom-Right
                    sdf = min(sdf, solveConcavityCorner(edgeDistances.yw, chamfer.w, concavity.w));

                [branch] if (collapaseAmount >= 1)
                {
                    [branch] if (collapseEdgeIdx == 0 && all(edgeDistances.zw < chamfer.x)) // Top
                        sdf = min(sdf, solveConcavityCorner(edgeDistances.zw, chamfer.x, concavity.x));
                    else if (collapseEdgeIdx == 1 && all(edgeDistances.zw < chamfer.w)) // Bottom
                        sdf = min(sdf, solveConcavityCorner(edgeDistances.zw, chamfer.w, concavity.w));
                    else if (collapseEdgeIdx == 2 && all(edgeDistances.xy < chamfer.x)) // Left
                        sdf = min(sdf, solveConcavityCorner(edgeDistances.xy, chamfer.x, concavity.x));
                    else if (collapseEdgeIdx == 3 && all(edgeDistances.xy < chamfer.w)) // Right
                        sdf = min(sdf, solveConcavityCorner(edgeDistances.xy, chamfer.w, concavity.w));
                }
                return sdf;
            }

            float quadrilateralSquircleCorners(float sdf, const uint collapseEdgeIdx, const float collapaseAmount, const float4 chamfer, const float4 chamferSmoothing, const float4 edgeDistances)
            {
                [branch] if (all(edgeDistances.xz < chamfer.x)) // Top-Left
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.xz, chamfer.x, chamferSmoothing.x));
                [branch] if (all(edgeDistances.xw < chamfer.y))  // Top-Right
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.xw, chamfer.y, chamferSmoothing.y));
                [branch] if (all(edgeDistances.yz < chamfer.z))  // Bottom-Left
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.yz, chamfer.z, chamferSmoothing.z));
                [branch] if (all(edgeDistances.yw < chamfer.w)) // Bottom-Right
                    sdf = min(sdf, solveSquircleCorner(edgeDistances.yw, chamfer.w, chamferSmoothing.w));

                [branch] if (collapaseAmount >= 1)
                {
                    [branch] if (collapseEdgeIdx == 0 && all(edgeDistances.zw < chamfer.x)) // Top
                        sdf = min(sdf, solveSquircleCorner(edgeDistances.zw, chamfer.x, chamferSmoothing.x));
                    else if (collapseEdgeIdx == 1 && all(edgeDistances.zw < chamfer.w)) // Bottom
                        sdf = min(sdf, solveSquircleCorner(edgeDistances.zw, chamfer.w, chamferSmoothing.w));
                    else if (collapseEdgeIdx == 2 && all(edgeDistances.xy < chamfer.x)) // Left
                        sdf = min(sdf, solveSquircleCorner(edgeDistances.xy, chamfer.x, chamferSmoothing.x));
                    else // Right
                        sdf = min(sdf, solveSquircleCorner(edgeDistances.xy, chamfer.w, chamferSmoothing.w));
                }
                return sdf;
            }

            inline float sdLine(const float2 p, const float2 a, const float2 b)
            {
                const float2 ba = b - a;
                const float2 pa = p - a;
                const float squaredLen = dot(ba, ba);
                if (squaredLen <= 1e-6f) // degenerate segment
                    return 1e6;

                return (pa.x * ba.y - pa.y * ba.x) * rsqrt(squaredLen);
            }

            float sdfQuadrilateral(float2 pos, const float2 size, const uint collapseEdgeIdx, const float collapasePoint, float collapaseAmount, const bool collapseToParallelogram, const bool mirror, const bool hasChamfer, float4 chamfer, float4 concavity, const bool isSquircle, const float outline, const bool accommodateCollapsedEdge, const float innerAspect)
            {
                const float2 collapseDelta = (size - 2. * outline) * collapaseAmount;

                [branch] if (mirror)
                {
                    [branch] if (collapseEdgeIdx <=1)
                    {
                        [branch] if (pos.y < size.y * 0.5)
                        {
                            pos.y = size.y - pos.y;
                            chamfer.xyzw = chamfer.zwxy;
                            concavity.xyzw = concavity.zwxy;
                        }
                    }
                    else if (pos.x > size.x * 0.5)
                    {
                        pos.x = size.x - pos.x;
                        chamfer.xzyw = chamfer.ywxz;
                        concavity.xzyw = concavity.ywxz;
                    }
                }
                else if (collapseToParallelogram)
                {
                    collapaseAmount = 0.; // Cannot yield a triangle, so prevent corner function from trying to affect a non-existent corner.
                }

                float2 bl = float2(outline, outline);
                float2 br = float2(size.x - outline, outline);
                float2 tr = float2(size.x - outline, size.y - outline);
                float2 tl = float2(outline, size.y - outline);

                [branch] if (collapseEdgeIdx == 0) // Top
                {
                    tl.x += collapseDelta.x * collapasePoint;
                    tr.x += collapseDelta.x * (collapasePoint - 1);
                    [branch] if (collapseToParallelogram)
                    {
                        br.x -= collapseDelta.x * collapasePoint;
                        bl.x += collapseDelta.x * (1 - collapasePoint);
                    }
                }
                else if (collapseEdgeIdx == 1) // Bottom
                {
                    bl.x += collapseDelta.x * collapasePoint;
                    br.x += collapseDelta.x * (collapasePoint - 1);
                    [branch] if (collapseToParallelogram)
                    {
                        tr.x -= collapseDelta.x * collapasePoint;
                        tl.x += collapseDelta.x * (1 - collapasePoint);
                    }
                }
                else if (collapseEdgeIdx == 2) // Left
                {
                    bl.y += collapseDelta.y * collapasePoint;
                    tl.y += collapseDelta.y * (collapasePoint - 1);
                    [branch] if (collapseToParallelogram)
                    {
                        tr.y -= collapseDelta.y * collapasePoint;
                        br.y += collapseDelta.y * (1 - collapasePoint);
                    }
                }
                else // Right
                {
                    br.y += collapseDelta.y * collapasePoint;
                    tr.y += collapseDelta.y * (collapasePoint - 1);
                    [branch] if (collapseToParallelogram)
                    {
                        tl.y -= collapseDelta.y * collapasePoint;
                        bl.y += collapseDelta.y * (1 - collapasePoint);
                    }
                }

                const float outlineAdjust = accommodateCollapsedEdge ? innerAspect : 1;
                const float4 edgeDistances = outline * outlineAdjust + float4
                (
                    sdLine(pos, tl, tr), // Top
                    sdLine(pos, br, bl), // Bottom
                    sdLine(pos, bl, tl), // Left
                    sdLine(pos, tr, br)  // Right
                );

                float sdf = min(min(edgeDistances.x, edgeDistances.y), min(edgeDistances.z, edgeDistances.w));
                [branch] if (!hasChamfer)
                    return sdf;

                [branch]if (isSquircle)
                    sdf = quadrilateralSquircleCorners(sdf, collapseEdgeIdx, collapaseAmount, chamfer, concavity, edgeDistances);
                else
                    sdf = quadrilateralConcavityCorners(sdf, collapseEdgeIdx, collapaseAmount, chamfer, concavity, edgeDistances);

                return sdf;
            }

            inline half interleavedGradientNoise(const float2 pix)
            {
                return (frac(52.9829189h * frac(dot(pix, half2(0.06711056h, 0.00583715h)))) - 0.5h) * 0.00392156862h;
            }

            inline float noiseHash(const int2 p, const float seed, const float seed2)
            {
                float3 hashMix = frac(p.xyx * seed);
                hashMix += dot(hashMix, hashMix.yzx + seed2);
                return frac((hashMix.x + hashMix.y) * hashMix.z);
            }

            inline half getAspectRatioForProceduralGradient(const v2f IN)
            {
                #if defined(UNITY_UI_CLIP_RECT) || defined(__SOFTMASK_ENABLE)
                    [branch] if (ScreenSpaceProceduralGradient(IN))
                        return _ScaledScreenParams.x / _ScaledScreenParams.y;
                    else if (OutlineExpandsOutwards(IN))
                        return (IN.widthHeightAndAspectAndPrecalc.x - OutlineWidth(IN) - OutlineWidth(IN)) / (IN.widthHeightAndAspectAndPrecalc.y - OutlineWidth(IN) - OutlineWidth(IN));
                    else
                        return IN.widthHeightAndAspectAndPrecalc.x / IN.widthHeightAndAspectAndPrecalc.y;
                #else
                    return IN.precalcs.y;

                #endif
            }

            half4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                const float2 pos = TexCoord(IN) * TexWidthHeight(IN);
                float sdfResult;
#ifdef FeatureSkew
                [branch] if (HasSkew(IN))
                {
                    sdfResult = sdfQuadrilateral(pos, TexWidthHeight(IN), EdgeCollapseIdx(IN), EdgeCollapsePos(IN), EdgeCollapseAmt(IN), CollapseToParallelogram(IN), MirrorCollapse(IN), HasChamfer(IN), IN.chamfer, IN.concavity, IsSquircle(IN), OutlineWidth(IN) * OutlineExpandsOutwards(IN), OutlineAccommodatesCollapsedEdge(IN), InnerAspect(IN));

                    [branch] if (sdfResult < 0)
                        discard;
                } else
#endif //FeatureSkew
                if (HasChamfer(IN))
                {
                    [branch] if (IsSquircle(IN))
                        sdfResult = sdfSquircle(pos, TexWidthHeight(IN), IN.chamfer, IN.concavity);
                    else
                        sdfResult = sdfRectangle(pos, TexWidthHeight(IN), IN.chamfer, IN.concavity);

                    [branch] if (sdfResult < 0)
                        discard;
                }
                else
                {
                    float4 edgeDistances = float4(pos, TexWidthHeight(IN) - pos);
                    sdfResult = min(min(edgeDistances.x, edgeDistances.y), min(edgeDistances.z, edgeDistances.w));
                }

                half cutoutResult = 1;
#if defined(FeatureCutout) && defined(SubFeatureCutoutSDF)
                [branch] if (HasSDFCutout(IN))
                {
                    const float2 shapeSize = IN.proceduralGradientParams.zw;
                    float2 shapeCenter = IN.proceduralGradientParams.xy;

                    const bool mirrorH = SDFCutoutMirrorHorizontal(IN);
                    const bool mirrorV = SDFCutoutMirrorVertical(IN);

                    float2 shapePosition;
                    [branch] if (mirrorH || mirrorV)
                    {
                        const float2 texCenter = TexWidthHeight(IN) * 0.5;
                        float2 mirroredPos = pos - texCenter;
                        shapeCenter -= texCenter;

                        const bool diagonalMirror = SDFCutoutMirrorIsDiagonal(IN);
                        [branch] if (diagonalMirror)
                        {
                            mirroredPos = float2(mirroredPos.x - mirroredPos.y, mirroredPos.x + mirroredPos.y);
                            shapeCenter = float2(shapeCenter.x - shapeCenter.y, shapeCenter.x + shapeCenter.y);
                        }

                        [branch] if (mirrorH)
                        {
                            mirroredPos.x = abs(mirroredPos.x);
                            shapeCenter.x = abs(shapeCenter.x);
                        }

                        [branch] if (mirrorV)
                        {
                            mirroredPos.y = abs(mirroredPos.y);
                            shapeCenter.y = abs(shapeCenter.y);
                        }

                        shapePosition = mirroredPos - shapeCenter;
                        [branch] if (diagonalMirror)
                            shapePosition = float2(shapePosition.x + shapePosition.y, shapePosition.y - shapePosition.x) * 0.5;
                    }
                    else
                    {
                        shapePosition = pos - shapeCenter;
                    }

                    half s, c;
                    #if defined(UNITY_UI_CLIP_RECT) || defined(__SOFTMASK_ENABLE)
                        sincos(Angle(IN), s, c);
                    #else
                        s = Angle(IN);
                        c = IN.precalcs.y;
                    #endif

                    shapePosition = float2(shapePosition.x * c + shapePosition.y * s, -shapePosition.x * s + shapePosition.y * c);
                    shapePosition += shapeSize * 0.5;

                    float cutoutSdf;
                    [branch] if (SDFCutoutIsSquircle(IN))
                        cutoutSdf = sdfSquircle(shapePosition, shapeSize, SDFCutoutChamfer(IN), SDFCutoutConcavity(IN));
                    else
                        cutoutSdf = sdfRectangle(shapePosition, shapeSize, SDFCutoutChamfer(IN), SDFCutoutConcavity(IN));

                    cutoutSdf -= 1.5 + Softness(IN) * 0.5;
                    if (!InvertCutout(IN))
                        cutoutSdf = -cutoutSdf;

                    [branch] if (CutoutOnlyAffectsOutline(IN) || SDFCutoutBothInteriorAndOutline(IN))
                        cutoutResult = saturate(cutoutSdf);
                    else
                        sdfResult = min(sdfResult, cutoutSdf);
                }
#endif // FeatureCutout && SubFeatureCutoutSDF
#if defined(FeatureCutout) && defined(SubFeatureCutoutSimple)
                #if defined(SubFeatureCutoutSDF)
                else if (HasCutout(IN))
                #else
                [branch] if (HasCutout(IN))
                #endif // SubFeatureCutoutSDF
                {
                    const half leftCutout = smoothstep(pos.x - Softness(IN), pos.x, IN.cutout.x);
                    const half rightCutout = smoothstep(pos.x + Softness(IN), pos.x, IN.cutout.y);
                    const half topCutout = smoothstep(pos.y + Softness(IN), pos.y, IN.cutout.z);
                    const half bottomCutout = smoothstep(pos.y - Softness(IN), pos.y, IN.cutout.w);

                    [branch] if (CutoutRuleIsOr(IN))
                        cutoutResult = min(leftCutout + rightCutout, topCutout + bottomCutout);
                    else
                        cutoutResult = max(leftCutout + rightCutout, topCutout + bottomCutout);

                    cutoutResult = saturate(cutoutResult);

                    if (InvertCutout(IN))
                        cutoutResult = 1 - cutoutResult;
                }
#endif // FeatureCutout && SubFeatureCutoutSimple

                // float3 isoLineColor = float3(1, 1, 1) - sign(sdfResult) * float3(0.1, 0.4, 0.7);
                // isoLineColor *= 1 - exp(-4 * abs(sdfResult));
                // isoLineColor *= 0.9 + 0.4 * cos(sdfResult);
                // float pixelWidth = 1 / _ScaledScreenParams.y;
                // isoLineColor = lerp(isoLineColor, float3(1,1,1), 1 - smoothstep(0.0, 3.0, (abs(sdfResult) - 0.006) / pixelWidth));
                // return fixed4(isoLineColor, 1);

                #ifdef HAS_BLUR
                    const float2 fragScreenUV = ScreenCoord(IN) * rcp(IN.vertex.w);
                #endif

                half4 spriteColor, color;
                [branch] if (ShowSprite(IN))
                {
                    #ifdef HAS_BLUR
                        [branch] if (PatternType(IN) != 25)
                            spriteColor = tex2D(_MainTex, TexCoord(IN)) + _TextureSampleAdd;
                        else
                            spriteColor = half4(1,1,1,1);

                        const half3 blurColor = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_BlurTex, UnityStereoTransformScreenSpaceTex(fragScreenUV)).rgb;
                        [branch] if (IN.color.a >= 1)
                        {
                            color.rgb = lerp(blurColor, spriteColor.rgb * IN.color.rgb, SourceImageFade(IN));
                            color.a = spriteColor.a;
                        }
                        else
                        {
                            const half blurMaskAlpha = spriteColor.a * lerp(1, IN.color.a, AlphaBlend(IN));
                            spriteColor *= IN.color;
                            const half3 blurLayer = blurColor * blurMaskAlpha;
                            const half3 spriteLayer = spriteColor.rgb * spriteColor.a;
                            const half3 blendedRGB = lerp(blurLayer, spriteLayer, SourceImageFade(IN));
                            const half blendedAlpha = lerp(blurMaskAlpha, spriteColor.a, SourceImageFade(IN));
                            color = half4(blendedRGB / blendedAlpha, blendedAlpha);
                        }
                    #else
                        spriteColor = (tex2D(_MainTex, TexCoord(IN)) + _TextureSampleAdd) * IN.color;
                        color = half4(spriteColor.rgb, spriteColor.a * SourceImageFade(IN));
                    #endif
                }
                else
                {
                    spriteColor = IN.color;
                    color = half4(spriteColor.rgb, spriteColor.a * SourceImageFade(IN));
                }

                color = half4(color.rgb * color.a, color.a);


#ifdef FeatureOutline
                half primaryColorToOutlineColorRatio;
                half4 outlineColor;
                [branch] if (OutlineWidth(IN) > 0)
                {
#ifdef FeatureSkew
                    #if defined(UNITY_UI_CLIP_RECT) || defined(__SOFTMASK_ENABLE)
                        float collapseAdjust;
                        [branch] if (OutlineAccommodatesCollapsedEdge(IN))
                            collapseAdjust = mad(InnerAspect(IN), 2, -1);
                        else
                            collapseAdjust = 1;
                    #else
                        const float collapseAdjust = IN.precalcs.x;
                    #endif
#else // FeatureSkew
                    const float collapseAdjust = 1;
#endif // FeatureSkew

                    float fadeDenom;

                    [branch] if (FadeOutlineToPerimeter(IN))
                    {
#ifdef FeatureSkew
                        fadeDenom = OutlineWidth(IN) * (OutlineAccommodatesCollapsedEdge(IN) ? InnerAspect(IN) : 1);
#else // FeatureSkew
                        fadeDenom = OutlineWidth(IN);
#endif // FeatureSkew
                        IN.outlineColor.a *= saturate(sdfResult / fadeDenom);
                    }
                    else
                    {
                        fadeDenom = 1;
                    }

                    float outlineSDF = abs(OutlineAdjustedWidth(IN) * collapseAdjust - sdfResult);
                    [branch] if (HasSecondInteriorOutline(IN))
                    {
                        const float innerOutlineCenter = Stroke(IN) * 2 - OutlineAdjustedWidth(IN);
#ifdef FeatureSkew
                        [branch] if (OutlineAccommodatesCollapsedEdge(IN))
                            Stroke(IN) -= 0.58594 * OutlineAdjustedWidth(IN);
#endif // FeatureSkew
                        float innerOutlineSDF = abs(innerOutlineCenter - sdfResult);
                        outlineSDF = min(outlineSDF, innerOutlineSDF);
                        [branch] if (FadeOutlineToPerimeter(IN))
                        {
                            const float innerPerimeterSDF = innerOutlineCenter + OutlineAdjustedWidth(IN) * collapseAdjust;
                            IN.outlineColor.a *= saturate((innerPerimeterSDF - sdfResult) / fadeDenom);
                        }
                    }

                    float visibleOutline = (OutlineAdjustedWidth(IN) - outlineSDF + 0.001h) / Softness(IN);
                    const half noOutlineMask = saturate(abs(visibleOutline));
                    visibleOutline = smoothstep(-rcp(Softness(IN)), 0, visibleOutline);
#ifdef FeatureCutout
                    const half outlineCutout = CutoutOnlyAffectsOutline(IN) ? cutoutResult : 1;
                    primaryColorToOutlineColorRatio = lerp(1 - outlineCutout + outlineCutout * noOutlineMask, 1 - outlineCutout, visibleOutline);
#else // FeatureCutout
                    primaryColorToOutlineColorRatio = lerp(noOutlineMask, 0, visibleOutline);
#endif // FeatureCutout

                    [branch] if (OutlineAlphaIsBlend(IN))
                        outlineColor = half4(lerp(OutlineExpandsOutwards(IN) ? IN.color.rgb : color.rgb, IN.outlineColor.rgb, IN.outlineColor.a), IN.color.a);
                    else
                        outlineColor = half4(IN.outlineColor.rgb * IN.outlineColor.a, IN.outlineColor.a * spriteColor.a);
                }
                else
                {
                    primaryColorToOutlineColorRatio = 1;
                    outlineColor = 0;
                }
#endif // FeatureOutline
                #ifndef HAS_BLUR
                    const float2 fragScreenUV = ScreenCoord(IN) * rcp(IN.vertex.w);
                #endif

/// DO NOT TOUCH THE COMMENT BELOW THIS LINE!
/// [SECTION:ProceduralGradient:BEGIN]
#ifdef FeatureProceduralGradient
                [branch] if (HasProceduralGradient(IN))
                {
#ifdef SubFeatureProceduralGradientScreenSpaceOption
                    float2 coordToUse = ScreenSpaceProceduralGradient(IN)
                        ? fragScreenUV
                        : InnerTexCoord(IN);
#else // SubFeatureProceduralGradientScreenSpaceOption
                    float2 coordToUse = InnerTexCoord(IN);
#endif // SubFeatureProceduralGradientScreenSpaceOption

                    float lerpVal;
#if defined(SubFeatureProceduralGradientSDF) || defined(SubFeatureProceduralGradientNoise)
                    [branch] if (IsSDFOrNoiseGradient(IN))
                    {
#ifdef SubFeatureProceduralGradientSDF
                        if (IsSDFGradient(IN))
                        {
                            float sdfToUse;
#ifdef SubFeatureProceduralGradientScreenSpaceOption
                            [branch] if (ScreenSpaceProceduralGradient(IN))
                            {
                                const half aspectRatio = _ScaledScreenParams.x / _ScaledScreenParams.y;
                                const float2 modifiedCoordToUse = half2(aspectRatio * 540, 540) * (1 - abs(mad(-2, coordToUse, 1)));
                                sdfToUse = min(modifiedCoordToUse.x, modifiedCoordToUse.y);
                            }
                            else
#endif // SubFeatureProceduralGradientScreenSpaceOption
                            {
                                sdfToUse = sdfResult;
                            }

                            const float2 alphas = smoothstep(float2(IN.proceduralGradientParams.x - IN.proceduralGradientParams.w, IN.proceduralGradientParams.y), float2(IN.proceduralGradientParams.x, IN.proceduralGradientParams.y + IN.proceduralGradientParams.z), sdfToUse);
                            lerpVal = alphas.x * (1 - alphas.y);
                        }
                        else // noise gradient
#endif // SubFeatureProceduralGradientSDF
                        {
#ifdef SubFeatureProceduralGradientNoise
                            float2 noiseCoord;
#ifdef SubFeatureProceduralGradientScreenSpaceOption
                            [branch] if (ScreenSpaceProceduralGradient(IN))
                                noiseCoord = (fragScreenUV - 0.5) * float2(1080 * (_ScaledScreenParams.x * rcp(_ScaledScreenParams.y)), 1080);
                            else
 #endif // SubFeatureProceduralGradientScreenSpaceOption
                                noiseCoord = (TexCoord(IN) - 0.5) * TexWidthHeight(IN);

                            const half2 p = noiseCoord * IN.proceduralGradientParams.x;
                            int2 i = floor(p);

                            const float seed1 = IN.proceduralGradientParams.y;
                            const float seed2 = seed1 - 32.2302;

                            const half a = noiseHash(i, seed1, seed2);
                            i.x++;
                            const half b = noiseHash(i, seed1, seed2);
                            i.y++;
                            const half c = noiseHash(i, seed1, seed2);
                            i.x--;
                            const half d = noiseHash(i, seed1, seed2);

                            const half2 f = frac(p);
                            const half2 u = f * f * (3 - 2 * f);
                            half n = lerp(lerp(a, b, u.x), lerp(d, c, u.x), u.y);
                            n = pow(2 * abs(n - 0.5), IN.proceduralGradientParams.z) * sign(n - step(IN.proceduralGradientParams.y, 0.1081) * 0.5) * 0.5 + 0.5;
                            lerpVal = n + IN.proceduralGradientParams.w;
#endif // SubFeatureProceduralGradientNoise
                        }
#ifdef SubFeatureProceduralGradientPointerAdjustPosOption
                        [branch] if (ProceduralGradientPosFromPointer(IN))
                        {
                            coordToUse.x -= 0.5;
                            coordToUse.x *= getAspectRatioForProceduralGradient(IN);
                            coordToUse.x += 0.5;
                            float2 gradientPos;
                            [branch] if (ScreenSpaceProceduralGradient(IN))
                            {
                                gradientPos = coordToUse - _ScreenSpacePointerPos.zw;
                            }
                            else
                            {
                                const float2 pixelDelta = (fragScreenUV - _ScreenSpacePointerPos.zw) * _ScaledScreenParams.xy;
                                const float2 dUVdx = ddx(coordToUse);
                                const float2 dUVdy = ddy(coordToUse);
                                gradientPos = pixelDelta.x * dUVdx + pixelDelta.y * dUVdy;
                            }
                            lerpVal = saturate(lerpVal);
                            lerpVal *= GradientStrength(IN) - length(gradientPos);
                        }
#endif // SubFeatureProceduralGradientPointerAdjustPosOption
                        lerpVal = saturate(lerpVal);
                    }
                    else
#endif // SubFeatureProceduralGradientSDF || SubFeatureProceduralGradientNoise
                    {
                        float2 gradientPos;
#ifdef SubFeatureProceduralGradientPointerAdjustPosOption
                        [branch] if (ProceduralGradientPosFromPointer(IN) && !ScreenSpaceProceduralGradient(IN))
                        {
                            const float2 pixelDelta = (fragScreenUV - _ScreenSpacePointerPos.zw) * _ScaledScreenParams.xy;
                            const float2 dUVdx = ddx(coordToUse);
                            const float2 dUVdy = ddy(coordToUse);
                            gradientPos = pixelDelta.x * dUVdx + pixelDelta.y * dUVdy - IN.proceduralGradientParams.xy + 0.5;
                        }
                        else
#endif // SubFeatureProceduralGradientPointerAdjustPosOption
                        {
                            gradientPos = coordToUse - IN.proceduralGradientParams.xy;
                        }
#ifdef SubFeatureProceduralGradientAngle
                        [branch] if (IsAngleGradient(IN))
                        {
                            #if defined(UNITY_UI_CLIP_RECT) || defined(__SOFTMASK_ENABLE)
                                half s, c;
                                sincos(Angle(IN), s, c);
                                const half distAlongDir = dot(gradientPos, half2(c, s));
                            #else
                                const half distAlongDir = dot(gradientPos, half2(IN.precalcs.y, Angle(IN))); // IN.precalcs.w is cos, Angle(IN) is sin
                            #endif

                            const half gradientFactor = abs(distAlongDir);
                            const float scaledGradientFactor = gradientFactor * (step(0, distAlongDir) ? IN.proceduralGradientParams.w : IN.proceduralGradientParams.z);
                            lerpVal = saturate(scaledGradientFactor);
                        }
                        else
#endif // SubFeatureProceduralGradientAngle
#ifdef SubFeatureProceduralGradientConical
                        if (IsConicalGradient(IN))
                        {
                            const half2 correctedPos = half2(gradientPos.x * getAspectRatioForProceduralGradient(IN), gradientPos.y);
                            const half dist = length(correctedPos);
                            const half totalAngle = Angle(IN) + dist * IN.proceduralGradientParams.w;
                            half s, c;
                            sincos(totalAngle, s, c);
                            const half2 rotatedPos = half2(correctedPos.x * c - correctedPos.y * s, correctedPos.x * s + correctedPos.y * c);
                            const half angle = atan2(rotatedPos.y, rotatedPos.x);
                            const half gradient = (angle + UNITY_PI) / UNITY_TWO_PI;
                            lerpVal = (gradient - (1 - IN.proceduralGradientParams.z)) * rcp(clamp(IN.proceduralGradientParams.z, 0.001h, 1));
                            lerpVal = saturate(lerpVal);
                            const half seamDist = abs(rotatedPos.y);
                            const half aaFactor = smoothstep(0, fwidth(rotatedPos.y), seamDist);
                            const half wrapValue = saturate(IN.proceduralGradientParams.z - 1);
                            const half mask = step(rotatedPos.x, 0);
                            lerpVal = lerp(lerpVal, lerp(wrapValue, lerpVal, aaFactor), mask);
                        }
                        else // radial gradient
#endif // SubFeatureProceduralGradientConical
                        {
#ifdef SubFeatureProceduralGradientRadial
                            lerpVal = length(gradientPos * IN.proceduralGradientParams.zw);
                            lerpVal = saturate(pow(lerpVal, GradientStrength(IN)));
# endif // SubFeatureProceduralGradientRadial
                        }
                    }

                    if (InvertGradient(IN))
                        lerpVal = 1 - lerpVal;

                    const float blend = ProceduralGradientAlphaIsBlend(IN) ? IN.proceduralGradientColor.a : 1;
                    const half secondaryAlpha = IN.proceduralGradientColor.a + 1 - blend;
                    const half ditherFactor = interleavedGradientNoise(IN.vertex.xy);
                    const half3 secondaryRGB = IN.proceduralGradientColor.rgb;

                    [branch] if (GradientAffectsInterior(IN))
                    {
                        const half primaryToSecondaryAlpha = secondaryAlpha * spriteColor.a;
                        const half4 baseInteriorSecondary = half4(secondaryRGB * primaryToSecondaryAlpha, primaryToSecondaryAlpha);
                        const half4 primaryToProceduralGradientColor = lerp(color, baseInteriorSecondary, blend);
                        const half primaryDither = ditherFactor / (0.01h + distance(primaryToProceduralGradientColor, color));
                        color = lerp(color, primaryToProceduralGradientColor, lerpVal + primaryDither);
                        spriteColor.a = lerp(spriteColor.a, primaryToSecondaryAlpha, lerpVal);
                    }
#ifdef FeatureOutline
                    [branch] if (GradientAffectsOutline(IN))
                    {
                        const half outlineToSecondaryAlpha = secondaryAlpha * outlineColor.a;
                        const half4 baseOutlineSecondary  = half4(secondaryRGB * outlineToSecondaryAlpha, outlineToSecondaryAlpha);
                        const half4 outlineToProceduralGradientColor = lerp(outlineColor, baseOutlineSecondary, blend);
                        const half outlineDither = ditherFactor / (0.01h + distance(outlineToProceduralGradientColor, outlineColor));
                        outlineColor = lerp(outlineColor, outlineToProceduralGradientColor, lerpVal + outlineDither);
                    }
#endif // FeatureOutline
                }
#endif // FeatureProceduralGradient
/// DO NOT TOUCH THE COMMENT BELOW THIS LINE!
/// [SECTION:ProceduralGradient:END]
/// [SECTION:Pattern:BEGIN]
#ifdef FeaturePattern
                [branch] if (HasPattern(IN))
                {
                    float2 coordAnchor = PatternShapeOriginLookup[(uint)clamp(PatternType(IN) - 3, 0, 20) % 5];
                    float2 patternCoord;
#ifdef SubFeaturePatternScreenSpaceOption
                    [branch] if (ScreenSpacePattern(IN))
                        patternCoord = (fragScreenUV - coordAnchor) * float2(1080 * (_ScaledScreenParams.x * rcp(_ScaledScreenParams.y)), 1080);
                    else
#endif // SubFeaturePatternScreenSpaceOption
                        patternCoord = (TexCoord(IN) - coordAnchor) * TexWidthHeight(IN);

                    float patternMask;
#ifdef SubFeaturePatternSprite
                    [branch] if (PatternType(IN) == 25) // tiled main texture
                    {
                        float2 tileUV;
                        #ifdef HAS_BLUR
                            patternCoord.y *= _MainTex_TexelSize.y * rcp(_MainTex_TexelSize.x);
                            tileUV = frac(patternCoord * PatternParam2(IN) + float2(-PatternParam1(IN), 0));
                        #elif defined(UNITY_UI_CLIP_RECT) || defined(__SOFTMASK_ENABLE)
                            float sin, cos;
                            sincos(PatternSpriteAngle(IN), sin, cos);
                            [branch] if (RotateSpritePatternOffset(IN))
                            {
                                patternCoord.y *= _MainTex_TexelSize.y * rcp(_MainTex_TexelSize.x);
                                const float2 offsetDir = float2(cos, sin) * -PatternParam1(IN);
                                tileUV = frac(patternCoord * PatternParam2(IN) + offsetDir);
                            }
                            else
                            {
                                const float2 spritePatternUV = float2
                                (
                                    patternCoord.x * cos - patternCoord.y * -sin,
                                    (patternCoord.x * -sin + patternCoord.y * cos) * _MainTex_TexelSize.y * rcp(_MainTex_TexelSize.x)
                                );
                                tileUV = frac(spritePatternUV * PatternParam2(IN) + float2(-PatternParam1(IN), 0));
                            }
                        #else
                            [branch] if (RotateSpritePatternOffset(IN))
                            {
                                patternCoord.y *= PatternSpriteAngle(IN); // PatternSpriteAngle pre-encoded with sprite aspect ratio
                                tileUV = frac(patternCoord * PatternParam2(IN) + IN.precalcs.wz);
                            }
                            else
                            {
                                const float2 spritePatternUV = float2
                                (
                                    patternCoord.x * IN.precalcs.w - patternCoord.y * -IN.precalcs.z,
                                    (patternCoord.x * -IN.precalcs.z + patternCoord.y * IN.precalcs.w) * PatternSpriteAngle(IN) // PatternSpriteAngle pre-encoded with sprite aspect ratio
                                );
                                tileUV = frac(spritePatternUV * PatternParam2(IN) + float2(-PatternParam1(IN), 0));
                            }
                        #endif

                        IN.patternColor *= tex2D(_MainTex, tileUV) + _TextureSampleAdd;
                        patternMask = 1;
                    }
                    else
#endif // SubFeaturePatternGrid
#ifdef SubFeaturePatternGrid
                    if (PatternType(IN) >= 25)
                    {
                        float2 cellUV = patternCoord * PatternParam2(IN);
                        if ((uint)PatternType(IN) % 2 == 1)
                            cellUV = float2(cellUV.x - cellUV.y, cellUV.x + cellUV.y);
                    
                        const float2 grid = frac(cellUV) - 0.5;
                        float2 width = fwidth(cellUV);
                        if (SoftPattern(IN))
                            width *= UNITY_PI;
                    
                        const float2 square = smoothstep(PatternParam1(IN) - width, PatternParam1(IN) + width, abs(grid));
                        [branch] if (PatternType(IN) >= 29)
                            patternMask = min(square.x, square.y);
                        else
                            patternMask = max(square.x, square.y);
                    }
                    else
#endif // SubFeaturePatternGrid
#if defined(SubFeaturePatternLine) || defined(SubFeaturePatternShape) || defined(SubFeaturePatternFractal)
                    {
                        float spacing, widthBasis;
#ifdef SubFeaturePatternLine
                        [branch] if (PatternType(IN) <= 1)
                            spacing = widthBasis = patternCoord.x - PatternType(IN) * patternCoord.y;
                        else if (PatternType(IN) == 2)
                            spacing = widthBasis = patternCoord.y;
                        else
#endif // SubFeaturePatternLine
#ifdef SubFeaturePatternShape
                        if (PatternType(IN) < 8)
                            spacing = widthBasis = abs(patternCoord.x) + abs(patternCoord.y);
                        else if (PatternType(IN) < 13)
                            spacing = widthBasis = length(patternCoord);
                        else if (PatternType(IN) < 18)
                            spacing = widthBasis = max(abs(patternCoord.x), abs(patternCoord.y));
                        else if (PatternType(IN) < 23)
                            spacing = widthBasis = min(abs(patternCoord.x), abs(patternCoord.y));
                        else
#endif // SubFeaturePatternShape
                        {
#ifdef SubFeaturePatternFractal
                            const half dist = 1 + length(frac(patternCoord * PatternParam1(IN)) - 0.5);
                            spacing = mad(dist, dist, 0.05) * 20;
                            #ifdef HAS_BLUR
                                widthBasis = spacing;
                            #else
                                widthBasis = length(patternCoord) * PatternParam1(IN) * 40;
                            #endif
#endif // SubFeaturePatternFractal
                        }
                        const float phase = mad(spacing, PatternParam2(IN), PatternParam1(IN));

                        #ifdef HAS_BLUR
                            float width = fwidth(phase);
                        #else
                            #if defined(UNITY_UI_CLIP_RECT) || defined(__SOFTMASK_ENABLE)
                                float slopeAtCrossing, thicknessOffset;
                                const float lineRatio = max(0.001h, PatternLineThickness(IN));
                                sincos(lineRatio * UNITY_PI, slopeAtCrossing, thicknessOffset);
                            #else
                                float slopeAtCrossing = IN.precalcs.z;
                                float thicknessOffset = IN.precalcs.w;
                            #endif
                            float width = fwidth(widthBasis * PatternParam2(IN)) * slopeAtCrossing;
                        #endif

                        if (SoftPattern(IN))
                            width *= UNITY_PI;

                        #ifdef HAS_BLUR
                            patternMask = smoothstep(-width, width, sin(phase));
                        #else
                            thicknessOffset *= 1 + width;
                            patternMask = smoothstep(-width, width, sin(phase) + thicknessOffset);
                        #endif
                    }
#else // SubFeaturePatternLine || SubFeaturePatternShape || SubFeaturePatternFractal
                    patternMask = 1;
#endif // SubFeaturePatternLine || SubFeaturePatternShape || SubFeaturePatternFractal
                    const float patternBlend = PatterAlphaIsBlend(IN) ? IN.patternColor.a : 1;
                    const half patternAlpha = IN.patternColor.a + 1 - patternBlend;
                    const half3 patternRGB = IN.patternColor.rgb;

                    [branch] if (PatternAffectsInterior(IN))
                    {
                        const half primaryToPatternAlpha = patternAlpha * spriteColor.a;
                        const half4 baseInteriorPattern = half4(patternRGB * primaryToPatternAlpha, primaryToPatternAlpha);
                        const half4 patternToInteriorColor = lerp(color, baseInteriorPattern, patternBlend);
                        color = lerp(color, patternToInteriorColor, patternMask);
                    }
#ifdef FeatureOutline
                    [branch] if (PatternAffectsOutline(IN))
                    {
                        const half4 baseOutlinePattern  = half4(patternRGB * patternAlpha * outlineColor.a, patternAlpha * outlineColor.a);
                        const half4 patternToOutlineColor = lerp(outlineColor, baseOutlinePattern, patternBlend);
                        outlineColor = lerp(outlineColor, patternToOutlineColor, patternMask);
                    }
#endif // FeatureOutline
                }
#endif // FeaturePattern
/// DO NOT TOUCH THE COMMENT BELOW THIS LINE!
/// [SECTION:Pattern:END]

#ifdef FeatureOutline
                outlineColor = lerp(outlineColor, color, OutlineAlphaIsBlend(IN) * primaryColorToOutlineColorRatio);
                color = lerp(outlineColor, color, primaryColorToOutlineColorRatio);
#endif // FeatureOutline
                color = half4(saturate(color.rgb / max(color.a, 0.001h)), color.a);

                float visible;
#ifdef FeatureStroke
                [branch] if (HasStroke(IN))
                    visible = (Stroke(IN) - abs(Stroke(IN) - sdfResult)) / Softness(IN);
                else
#endif // FeatureStroke
                    visible = sdfResult / Softness(IN);

                color.a *= saturate(visible);
#ifdef FeatureCutout
                color.a *= CutoutOnlyAffectsOutline(IN) ? 1 : cutoutResult;
#endif // FeatureCutout

                #ifdef UNITY_UI_CLIP_RECT
                    const half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                    color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif

                #ifdef __SOFTMASK_ENABLE
                    color.a *= SOFTMASK_GET_MASK(IN);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
