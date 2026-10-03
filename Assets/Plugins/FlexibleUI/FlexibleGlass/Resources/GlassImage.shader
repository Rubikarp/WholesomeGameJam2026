Shader "Hidden/JeffGrawAssets/GlassImage"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
        [HideInInspector] _GlassEdgeLighting("Edge Lighting", Vector) = (0.5, 0.8660254, 8.75, 0)
        [HideInInspector] _GlassEdgeHighlight("Edge Highlight", Color) = (1, 1, 1, 0.12)
        [HideInInspector] _GlassEdgeShadow("Edge Shadow", Color) = (0, 0, 0, 0)
        [HideInInspector] _GlassBlurMaxLod("Blur Max LOD", Float) = 0
        [HideInInspector] _GlassImageSourceScale("Glass Image Source Scale", Vector) = (1, 1, 1, 1)
        [HideInInspector] [NoScaleOffset] _GlassImageTex("Glass Image Texture", 2D) = "black" {}
        [HideInInspector] [NoScaleOffset] _GlassImageTexArray("Stereo Glass Image Texture", 2DArray) = "" {}
        [HideInInspector] [NoScaleOffset] _GlassSdfAtlas("Retained SDF Atlas", 2DArray) = "" {}
        [HideInInspector] _GlassSdfResolution("Retained SDF Resolution", Float) = 256
        [HideInInspector] _GlassSdfMaxLod("Retained SDF Max LOD", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        ColorMask[_ColorMask]

        Pass
        {
            Name "GlassImage"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile_local __ HAS_BLUR
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #pragma multi_compile_local_fragment _ FLEXIBLE_GLASS_EDGE_OPPOSING FLEXIBLE_GLASS_EDGE_POINT
            #pragma multi_compile_local_fragment __ FLEXIBLE_GLASS_EDGE_DISABLED

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "FlexibleGlassLighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            float4 _GlassEdgeLighting;
            half4 _GlassEdgeHighlight;
            half4 _GlassEdgeShadow;
            float _GlassBlurMaxLod;
            float4 _GlassImageSourceScale;
            float _GlassSdfResolution;
            float _GlassSdfMaxLod;
            CBUFFER_END
            UNITY_DECLARE_TEX2DARRAY(_GlassSdfAtlas);
            #define FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(uv, slice, lod) UNITY_SAMPLE_TEX2DARRAY_LOD(_GlassSdfAtlas, float3(uv, slice), lod)
            #include "GlassRetainedFieldSampling.hlsl"
            float4 _ClipRect;
            int _UIVertexColorAlwaysGammaSpace;
            #ifdef UNITY_UI_CLIP_RECT
                float _UIMaskSoftnessX;
                float _UIMaskSoftnessY;
            #endif
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
            UNITY_DECLARE_TEX2DARRAY(_GlassImageTexArray);
            float4 _GlassImageTexArray_TexelSize;
            #define _GlassImageTex_TexelSize _GlassImageTexArray_TexelSize
            #define SAMPLE_GLASS_IMAGE_LOD(uv, lod) UNITY_SAMPLE_TEX2DARRAY_LOD(_GlassImageTexArray, float3((uv).xy, (float)unity_StereoEyeIndex), lod)
#else
            sampler2D _GlassImageTex;
            float4 _GlassImageTex_TexelSize;
            #define SAMPLE_GLASS_IMAGE_LOD(uv, lod) tex2Dlod(_GlassImageTex, float4((uv).xy, 0.0, lod))
#endif
            static const float ShadowFalloffExtent = 6.0;

            struct Attributes
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uvSourceLocal : TEXCOORD0;
                float4 sizeRadii : TEXCOORD1;
                float4 packedAppearance : TEXCOORD2;
                float4 shadowControls : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                nointerpolation fixed4 color : COLOR0;
                noperspective float4 screenUvFill : TEXCOORD0;
                float4 fieldUvDomain : TEXCOORD1;
                nointerpolation float4 sdfControls : TEXCOORD2;
                nointerpolation float4 appearance0 : TEXCOORD3;
                nointerpolation float4 appearance1 : TEXCOORD4;
                nointerpolation float4 screenCenterLods : TEXCOORD6;
                #ifdef UNITY_UI_CLIP_RECT
                    float4 mask : TEXCOORD5;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 UnpackColor(float packedValue)
            {
                const uint packed = asuint(packedValue);
                return float4((packed >> 24) & 255u, (packed >> 16) & 255u, (packed >> 8) & 255u, packed & 255u) / 255.0;
            }

            half GlassInterleavedGradientNoise(float2 pixelPosition)
            {
                return (frac(52.9829189h * frac(dot(pixelPosition, half2(0.06711056h, 0.00583715h)))) - 0.5h) * 0.00392156862h;
            }

            #include "GlassPhysicalOptics.hlsl"

            half3 SampleGlassImage(float2 uv, float lod, float2 margin)
            {
                return SAMPLE_GLASS_IMAGE_LOD(UnityStereoTransformScreenSpaceTex(clamp(uv, margin, 1.0 - margin)), lod).rgb;
            }

            half3 SampleReconstructedGlassImage(float2 uv, float lod, float2 margin)
            {
                const float2 supersampleAmount = saturate(_GlassImageSourceScale.xy - 1.0);
                [branch] if (max(supersampleAmount.x, supersampleAmount.y) < 1e-3)
                    return SampleGlassImage(uv, lod, margin);

                // GlassImage is rasterized by the Canvas after the camera target has
                // already been supersampled. Reconstruct the source mapping across
                // the Canvas pixel so curved refraction receives the same subpixel
                // integration as glass drawn into the camera target.
                const float2 uvDx = ddx(uv);
                const float2 uvDy = ddy(uv);
                const float sampleLod = max(lod - log2(1.0 + max(supersampleAmount.x, supersampleAmount.y)), 0.0);
                const float2 uvQuarterDx = uvDx * (0.25 * sqrt(supersampleAmount.x));
                const float2 uvQuarterDy = uvDy * (0.25 * sqrt(supersampleAmount.y));
                half3 color = 0.0h;
                color += SampleGlassImage(uv - uvQuarterDx - uvQuarterDy, sampleLod, margin);
                color += SampleGlassImage(uv + uvQuarterDx - uvQuarterDy, sampleLod, margin);
                color += SampleGlassImage(uv - uvQuarterDx + uvQuarterDy, sampleLod, margin);
                color += SampleGlassImage(uv + uvQuarterDx + uvQuarterDy, sampleLod, margin);
                return color * 0.25h;
            }

            half3 ApplyGlassAppearance(half3 glassColor, float transmission, half3 tint, half colorMix)
            {
                glassColor *= transmission;
                return lerp(glassColor, tint, colorMix);
            }

            half4 FinishGlassColor(half3 rgb, half alpha, half2 uiClipMask)
            {
                alpha *= uiClipMask.x * uiClipMask.y;
                #ifdef UNITY_UI_ALPHACLIP
                    clip(alpha - 0.001h);
                #endif
                return half4(rgb, alpha);
            }

            float3 SampleRetainedField(float2 fieldUv, float slice, float lod, float normalizeGradient)
            {
                float4 field = SampleGlassRetainedField(fieldUv, slice, lod);
                const float gradientLengthSquared = dot(field.yz, field.yz);
                const float2 unitGradient = gradientLengthSquared > 1e-10 ? field.yz * rsqrt(gradientLengthSquared) : float2(1.0, 0.0);
                return float3(field.x, lerp(field.yz, unitGradient, normalizeGradient));
            }

            float3 SampleRetainedOpticalField(float2 fieldUv, float slice, float lod)
            {
                const float4 field = SampleGlassRetainedField(fieldUv, slice, lod);
                return float3(field.w, field.yz);
            }

            float3 SampleExtendedRetainedField(float2 fieldUv, float2 domainSize, float slice)
            {
                const float2 clampedUv = saturate(fieldUv);
                const float3 field = SampleRetainedField(clampedUv, slice, 0.0, 1.0);
                const float2 outsideDelta = (fieldUv - clampedUv) * domainSize;
                if (dot(outsideDelta, outsideDelta) <= 1e-8)
                    return field;

                const float2 boundaryVector = field.yz * max(field.x, 0.0) + outsideDelta;
                const float boundaryLength = length(boundaryVector);
                return float3(boundaryLength, boundaryLength > 1e-5 ? boundaryVector / boundaryLength : field.yz);
            }

            float2 FragmentRetainedOpticalLods(float2 domainSize, float2 localDx, float2 localDy, float thickness, float smoothness)
            {
                const float2 localTexelSize = domainSize / max(_GlassSdfResolution, 1.0);
                const float determinant = max(abs(localDx.x * localDy.y - localDx.y * localDy.x), 1e-6);
                const float screenTexelX = length(float2(localDy.y, -localDx.y)) * localTexelSize.x / determinant;
                const float screenTexelY = length(float2(-localDy.x, localDx.x)) * localTexelSize.y / determinant;
                const float screenTexelSize = sqrt(max(screenTexelX * screenTexelY, 1e-6));
                const float thicknessTexels = max(thickness, 0.0) / screenTexelSize;
                return clamp(log2(1.0 + thicknessTexels * float2(max(smoothness, 0.0), 1.0)), 0.0, _GlassSdfMaxLod);
            }

            float ShadowFalloff(float scaledDistance)
            {
                const float cutoff = exp2(-ShadowFalloffExtent);
                const float falloff = exp2(-scaledDistance);
                const float normalized = saturate((falloff - cutoff) / (1.0 - cutoff));
                return normalized * normalized * (3.0 - 2.0 * normalized);
            }

            half3 SamplePhysicalGlassImage(float2 sourceUv, float2 normal, float distance, float lip, float strength, float index, float dispersion, float inverseMagnification, float lod, float2 margin)
            {
                half3 color = 0.0h;
                [unroll] for (int i = -3; i <= 3; i++)
                {
                    const float position = i / 3.0;
                    const float wavelength = lerp(486.1327, 656.2725, position * 0.5 + 0.5);
                    const float channelIndex = PhysicalRefractiveIndex(index, dispersion, wavelength);
                    const float displacement = PhysicalRefractionDisplacement(distance, lip, strength, channelIndex) * inverseMagnification;
                    const half3 sampleColor = SampleReconstructedGlassImage(sourceUv + normal * displacement * _GlassImageTex_TexelSize.xy, lod, margin);
                    const half3 spectralWeight = half3(saturate(position), 1.0 - abs(position), saturate(-position));
                    color += sampleColor * spectralWeight;
                }
                return color * half3(0.5h, 0.3333333h, 0.5h);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                const float4 screenPosition = ComputeNonStereoScreenPos(output.vertex);
                output.screenUvFill = float4(screenPosition.xy, input.uvSourceLocal.xy) / screenPosition.w;
                const float2 localSize = input.sizeRadii.xy;
                const float2 minimumFieldPadding = max(localSize / 32.0, 1.0);
                const float2 minimumFieldDomain = localSize + 2.0 * minimumFieldPadding;
                const float2 fieldDomain = max(max(minimumFieldDomain.x, minimumFieldDomain.y), 1e-5);
                const float2 fieldPadding = (fieldDomain - localSize) * 0.5;
                output.fieldUvDomain = float4((input.uvSourceLocal.zw + fieldPadding) / fieldDomain, fieldDomain);
                const uint packedControls = asuint(input.shadowControls.w);
                const bool shadowOnly = (packedControls & 2u) != 0u;
                const bool hasPrecomputedLods = (packedControls & 1u) != 0u;
                const float fillOperation = (packedControls & 8u) == 0u ? 0.0 : (packedControls & 16u) == 0u ? 1.0 : -1.0;
                // Non-interpolated mode lane: positive = shadow;
                // negative surface bits: 1 = precomputed LODs, 2 = depth fallback.
                const uint surfaceMode = (hasPrecomputedLods ? 1u : 0u) | ((packedControls & 32u) != 0u ? 2u : 0u);
                output.sdfControls = float4(input.sizeRadii.z, fillOperation, ((packedControls >> 8u) & 255u) * (1.0 / 255.0), shadowOnly ? 1.0 : -(float)surfaceMode);
                [branch] if (shadowOnly)
                {
                    output.appearance0 = UnpackColor(input.packedAppearance.z);
                    const float shadowSize = input.shadowControls.x * (1.0 / 64.0);
                    output.appearance1 = float4(rcp(max(shadowSize, 0.5)), input.shadowControls.yz, (packedControls & 4u) != 0u);
                    output.screenCenterLods = float4(max(input.packedAppearance.w, 1e-4), 0.0, 0.0, 0.0);
                }
                else
                {
                    const float thickness = input.shadowControls.x;
                    const uint edgeWidths = asuint(input.shadowControls.z);
                    output.appearance0 = float4(input.packedAppearance.x, input.packedAppearance.z, rcp(input.packedAppearance.w), thickness);
                    output.appearance1 = float4(
                        (edgeWidths & 255u) * (1.0 / 255.0),
                        ((edgeWidths >> 8u) & 255u) * (1.0 / 255.0),
                        input.packedAppearance.y,
                        input.shadowControls.y);
                    float4 centerVertex = input.vertex;
                    centerVertex.xy += input.sizeRadii.xy * 0.5 - input.uvSourceLocal.zw;
                    const float4 centerClip = UnityObjectToClipPos(centerVertex);
                    const float2 retainedLods = hasPrecomputedLods
                        ? float2(input.sizeRadii.w, (packedControls >> 16u) * (_GlassSdfMaxLod / 65535.0))
                        : float2(input.sizeRadii.w, 0.0);
                    output.screenCenterLods = float4(ComputeNonStereoScreenPos(centerClip).xy / centerClip.w, retainedLods);
                }
                output.color = input.color;
                [branch] if (!IsGammaSpace() && _UIVertexColorAlwaysGammaSpace)
                {
                    output.color.rgb = UIGammaToLinear(output.color.rgb);
                    [branch] if (shadowOnly)
                        output.appearance0.rgb = UIGammaToLinear(output.appearance0.rgb + step(0.999, output.appearance0.rgb) * 0.001);
                }

                #ifdef UNITY_UI_CLIP_RECT
                    const float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                    float2 pixelSize = output.vertex.w;
                    pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                    output.mask = float4(input.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                #endif
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const bool shadowOnly = input.sdfControls.w > 0.5;
                const float sdfSlice = input.sdfControls.x;
                clip(sdfSlice + 0.5);
                [branch] if (input.color.a <= 0.0h)
                    discard;
                #ifdef UNITY_UI_CLIP_RECT
                    half2 uiClipMask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                    [branch] if (min(uiClipMask.x, uiClipMask.y) <= 0.0h)
                        discard;
                #else
                    half2 uiClipMask = 1.0h;
                #endif

                [branch] if (input.sdfControls.y != 0.0)
                {
                    const float fillDistance = input.sdfControls.y > 0.0
                        ? min(input.screenUvFill.z, input.screenUvFill.w)
                        : max(input.screenUvFill.z, input.screenUvFill.w);
                    const half fillCoverage = saturate(0.5 + fillDistance / max(fwidth(fillDistance), 1e-6));
                    [branch] if (fillCoverage <= 0.0h)
                        discard;
                    uiClipMask.x *= fillCoverage;
                }

                const float2 fieldUv = input.fieldUvDomain.xy;
                const float2 fieldDomain = input.fieldUvDomain.zw;
                [branch] if (shadowOnly)
                {
                    const float canvasScale = input.screenCenterLods.x;
                    const half4 shadowColor = input.appearance0;
                    float2 shadowFieldUv = fieldUv;
                    [branch] if (input.appearance1.w > 0.5)
                        shadowFieldUv -= input.appearance1.yz / (canvasScale * fieldDomain);
                    const float3 shadowField = SampleExtendedRetainedField(shadowFieldUv, fieldDomain, sdfSlice);
                    const float shadowDistance = shadowField.x * canvasScale;
                    const float scaledShadowDistance = max(shadowDistance, 0.0) * input.appearance1.x;
                    [branch] if (scaledShadowDistance >= ShadowFalloffExtent)
                        discard;
                    half shadowAlpha = shadowColor.a * input.color.a * ShadowFalloff(scaledShadowDistance);
                    [branch] if (shadowAlpha > 0.0h)
                    {
                        const half shadowEnergy = saturate(max(max(shadowColor.r, shadowColor.g), shadowColor.b));
                        const half shadowDitherStrength = lerp(5.0h, 2.0h, shadowEnergy);
                        const half ditherEnvelope = saturate(min(shadowAlpha, 1.0h - shadowAlpha) * 255.0h / shadowDitherStrength);
                        shadowAlpha += ditherEnvelope * shadowDitherStrength * GlassInterleavedGradientNoise(input.vertex.xy);
                    }
                    return FinishGlassColor(shadowColor.rgb, shadowAlpha, uiClipMask);
                }

                const float2 screenUvDirection = sign(float2(ddx(input.screenUvFill.x), ddy(input.screenUvFill.y)));
                const float2 fieldUvDx = ddx(fieldUv) * screenUvDirection.x;
                const float2 fieldUvDy = ddy(fieldUv) * screenUvDirection.y;
                const float2 localDx = fieldUvDx * fieldDomain;
                const float2 localDy = fieldUvDy * fieldDomain;
                const float3 surfaceField = SampleRetainedField(fieldUv, sdfSlice, 0.0, 1.0);
                const float2 surfaceGradient = float2(dot(surfaceField.yz, localDx), dot(surfaceField.yz, localDy));
                const float surfaceGradientLengthSquared = max(dot(surfaceGradient, surfaceGradient), 1e-8);
                const float inverseSurfaceGradientLength = rsqrt(surfaceGradientLengthSquared);
                const float distancePixels = surfaceField.x * inverseSurfaceGradientLength;
                const float antialias = max(fwidth(distancePixels), 0.75);
                const half coverage = saturate(0.5 - distancePixels / antialias);
                [branch] if (coverage <= 0.0h)
                    discard;

                const half opposingEdgeLightStrength = saturate(_GlassEdgeLighting.w);
                const float refraction = input.appearance0.x;
                const float transmission = input.appearance0.y;
                const float inverseMagnification = input.appearance0.z;
                const float2 sourcePixelsPerRasterPixel2 = max(_GlassImageTex_TexelSize.zw / max(_ScreenParams.xy, 1.0), 1e-4);
                const float sourcePixelsPerRasterPixel = sqrt(sourcePixelsPerRasterPixel2.x * sourcePixelsPerRasterPixel2.y);
                const float2 rasterPixelsPerLogicalPixel2 = max(_GlassImageSourceScale.xy / sourcePixelsPerRasterPixel2, 1e-4);
                const float rasterPixelsPerLogicalPixel = sqrt(rasterPixelsPerLogicalPixel2.x * rasterPixelsPerLogicalPixel2.y);
                const float thicknessValue = input.appearance0.w * rasterPixelsPerLogicalPixel;
                const float innerEdgeLightWidth = input.appearance1.x;
                const float outerEdgeLightWidth = input.appearance1.y;
                const float refractiveIndex = input.appearance1.z;
                const float dispersionCoefficient = input.appearance1.w;
                const half colorMix = input.sdfControls.z;
                half3 glassColor = 0;
                #ifdef HAS_BLUR
                    const float2 baseScreenUv = input.screenUvFill.xy;
                    const float2 magnifiedScreenUv = input.screenCenterLods.xy + (baseScreenUv - input.screenCenterLods.xy) * inverseMagnification;
                    #if defined(FLEXIBLE_GLASS_EDGE_DISABLED)
                        const bool needsOpticalField = thicknessValue > 1e-3 && refraction > 1e-4;
                    #else
                        const bool needsOpticalField = thicknessValue > 1e-3 && (refraction > 1e-4 || max(innerEdgeLightWidth, outerEdgeLightWidth) > 1e-4);
                    #endif
                    [branch] if (!needsOpticalField)
                    {
                        glassColor = SampleGlassImage(magnifiedScreenUv, 0.0, 0.5 * _GlassImageTex_TexelSize.xy);
                        glassColor = ApplyGlassAppearance(glassColor, transmission, input.color.rgb, colorMix);
                        return FinishGlassColor(glassColor, coverage * input.color.a, uiClipMask);
                    }
                    {
                        const float thickness = max(thicknessValue, 1e-3);
                        const float geometricDepth = max(-distancePixels, 0.0);
                        const uint surfaceMode = (uint)round(-input.sdfControls.w);
                        const bool hasPrecomputedLods = (surfaceMode & 1u) != 0u;
                        const float depthFallback = (surfaceMode & 2u) != 0u ? 1.0 : 0.0;
                        const float2 retainedLods = hasPrecomputedLods
                            ? input.screenCenterLods.zw
                            : FragmentRetainedOpticalLods(fieldDomain, localDx, localDy, thicknessValue, input.screenCenterLods.z);
                        const float opticalLod = retainedLods.x;
                        const float3 opticalField = SampleRetainedOpticalField(fieldUv, sdfSlice, opticalLod);
                        const float2 opticalGradient = float2(dot(opticalField.yz, localDx), dot(opticalField.yz, localDy));
                        const float opticalGradientLengthSquared = dot(opticalGradient, opticalGradient);
                        const float2 opticalNormal = opticalGradientLengthSquared > 1e-10 ? opticalGradient * rsqrt(max(opticalGradientLengthSquared, surfaceGradientLengthSquared)) : 0.0;
                        const float normalCoherence = saturate(length(opticalNormal));
                        const float smoothedOpticalDepth = -opticalField.x * inverseSurfaceGradientLength;
                        const float interiorBlend = smoothstep(0.0, max(thicknessValue * 0.25, 1.0), geometricDepth);
                        const float opticalDepth = max(lerp(geometricDepth, smoothedOpticalDepth, interiorBlend), 0.0);
                        const float normalizedDepth = thicknessValue > 1e-3 ? ResolveGlassNormalizedDepth(opticalDepth, geometricDepth, thickness, depthFallback) : 1.0;
                        const float bevelPosition = saturate(normalizedDepth);
                        const float opticalDistancePixels = -normalizedDepth * thickness;
                        const float sourceOpticalDistancePixels = opticalDistancePixels * sourcePixelsPerRasterPixel;
                        const float sourceThickness = thickness * sourcePixelsPerRasterPixel;
                        const float referenceDisplacement = PhysicalRefractionDisplacement(sourceOpticalDistancePixels, sourceThickness, refraction, refractiveIndex) * inverseMagnification;
                        const float2 referenceSourcePixels = magnifiedScreenUv * _GlassImageTex_TexelSize.zw + opticalNormal * referenceDisplacement;
                        const float profileReconstructionLod = PhysicalRefractionReconstructionLod(sourceOpticalDistancePixels, sourceThickness, refraction, normalCoherence, refractiveIndex, dispersionCoefficient, rcp(inverseMagnification), _GlassBlurMaxLod);
                        const float mappingReconstructionLod = PhysicalRefractionScreenFootprintLod(referenceSourcePixels, _GlassBlurMaxLod);
                        const float reconstructionLod = max(profileReconstructionLod, mappingReconstructionLod);
                        const float upperReconstructionLod = min(ceil(reconstructionLod), _GlassBlurMaxLod);
                        const float2 reconstructionMargin = exp2(upperReconstructionLod) * 0.5 * _GlassImageTex_TexelSize.xy;
                        [branch] if (dispersionCoefficient > 0.0 && normalizedDepth < 1.0 && normalCoherence > 0.0)
                            glassColor = SamplePhysicalGlassImage(magnifiedScreenUv, opticalNormal, sourceOpticalDistancePixels, sourceThickness, refraction, refractiveIndex, dispersionCoefficient, inverseMagnification, reconstructionLod, reconstructionMargin);
                        else
                        {
                            glassColor = SampleReconstructedGlassImage(magnifiedScreenUv + opticalNormal * referenceDisplacement * _GlassImageTex_TexelSize.xy, reconstructionLod, reconstructionMargin);
                        }
                        glassColor = ApplyGlassAppearance(glassColor, transmission, input.color.rgb, colorMix);
                        #if !defined(FLEXIBLE_GLASS_EDGE_DISABLED)
                            [branch] if (max(innerEdgeLightWidth, outerEdgeLightWidth) > 1e-4)
                            {
                            const float edgeLightAntialias = antialias / thickness;
                            const float innerLipAntialias = 1.25 / thickness;
                            const float innerEdgeLightLip = 1.0 - smoothstep(1.0 - innerLipAntialias * 0.5, 1.0 + innerLipAntialias * 0.5, normalizedDepth);
                            [branch] if (innerEdgeLightLip > 0.0)
                            {
                                float2 edgeLightDirection;
                                float edgeLightAttenuation;
                                GlassLipLightDirection(baseScreenUv, _GlassImageTex_TexelSize.zw,
                                    _GlassEdgeLighting, edgeLightDirection, edgeLightAttenuation);
                            const float innerEdgeLightPixelWidth = thicknessValue * innerEdgeLightWidth;
                            const float innerEdgeLightDistance = (1.0 - bevelPosition) / max(innerEdgeLightWidth, edgeLightAntialias);
                            const float innerEdgeLightProfile = exp2(-innerEdgeLightDistance * innerEdgeLightDistance) * innerEdgeLightLip;
                            const float innerEdgeLightCoverage = saturate(innerEdgeLightPixelWidth / antialias);
                            const float outerEdgeLightPixelWidth = thicknessValue * outerEdgeLightWidth;
                            const float outerEdgeLightDistance = geometricDepth / max(outerEdgeLightPixelWidth, antialias);
                            const float outerEdgeLightProfile = exp2(-outerEdgeLightDistance * outerEdgeLightDistance) * innerEdgeLightLip;
                            const float outerEdgeLightCoverage = saturate(outerEdgeLightPixelWidth / antialias);
                            const float2 surfaceNormal = surfaceGradient * inverseSurfaceGradientLength;
                            float2 lightingNormal = opticalGradientLengthSquared > 1e-10 ? opticalGradient * rsqrt(opticalGradientLengthSquared) : 0.0;
                            const float lightingLod = retainedLods.y;
                            [branch] if (lightingLod > opticalLod + 1e-4 && geometricDepth > 0.0 && geometricDepth < thicknessValue * 1.25)
                            {
                                const float3 lightingField = SampleRetainedField(fieldUv, sdfSlice, lightingLod, 0.0);
                                const float2 lightingGradient = float2(dot(lightingField.yz, localDx), dot(lightingField.yz, localDy));
                                const float lightingGradientLengthSquared = dot(lightingGradient, lightingGradient);
                                if (lightingGradientLengthSquared > 1e-10)
                                    lightingNormal = lightingGradient * rsqrt(lightingGradientLengthSquared);
                            }
                            const float2 facing = float2(dot(surfaceNormal, edgeLightDirection), dot(lightingNormal, edgeLightDirection));
                            float2 lipBeams = GlassLipLightBeams(facing, _GlassEdgeLighting.z, opposingEdgeLightStrength);
                            #if !defined(FLEXIBLE_GLASS_EDGE_POINT)
                                lipBeams *= GlassElementLightFalloff((baseScreenUv - input.screenCenterLods.xy) * _GlassImageTex_TexelSize.zw, _GlassImageTex_TexelSize.w, _GlassEdgeLighting.xy, _GlassEdgeLighting.z);
                            #endif
                            const float edgeLightBeam = lipBeams.x;
                            const float innerEdgeLightBeam = lipBeams.y;
                            const float outerHighlightMask = outerEdgeLightCoverage * outerEdgeLightProfile * edgeLightBeam;
                            const float innerHighlightMask = innerEdgeLightCoverage * innerEdgeLightProfile * innerEdgeLightBeam;
                            const float outerShadowMask = outerEdgeLightCoverage * outerEdgeLightProfile * (1.0 - saturate(edgeLightBeam));
                            const float innerShadowMask = innerEdgeLightCoverage * innerEdgeLightProfile * (1.0 - saturate(innerEdgeLightBeam));
                            const half highlightAmount = edgeLightAttenuation * _GlassEdgeHighlight.a * max(outerHighlightMask, innerHighlightMask);
                            const half shadowAmount = saturate(edgeLightAttenuation * _GlassEdgeShadow.a * max(outerShadowMask, innerShadowMask));
                            glassColor = lerp(glassColor, _GlassEdgeShadow.rgb, shadowAmount);
                            glassColor += _GlassEdgeHighlight.rgb * highlightAmount;
                            }
                            }
                        #endif
                    }
                #endif

                const half blurAlpha = coverage * input.color.a;
                return FinishGlassColor(glassColor, blurAlpha, uiClipMask);
            }
            ENDCG
        }
    }
}
