Shader "Hidden/JeffGrawAssets/FlexibleGlass"
{
    HLSLINCLUDE
    #pragma editor_sync_compilation
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        float4 positionHCS : POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    Varyings Vert(Attributes input)
    {
        Varyings output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
        output.positionCS = mul(GetObjectToWorldMatrix(), float4(input.positionHCS.xyz, 1.0f));
        #if UNITY_UV_STARTS_AT_TOP
            output.positionCS.y *= -1.0f;
        #endif
        output.uv = input.uv;
        return output;
    }

    TEXTURE2D_X(_MainTex);
    TEXTURE2D_X(_GlassSharpTex);
    TEXTURE2D_X(_GlassBlurTex);
    TEXTURE2D_ARRAY(_GlassSdfAtlas);
    SAMPLER(sampler_GlassSdfAtlas);
    #if UNITY_VERSION < 600000
        SAMPLER(sampler_LinearClamp);
        SAMPLER(sampler_TrilinearClamp);
    #endif

    float4 _SourceRegion;
    float4 _SourceTexelSize;
    float _GlassSampleOffset;
    float _GlassDitherStrength;
    float2 _GlassDitherOffset;

    inline half GlassInterleavedGradientNoise(float2 pixelPosition)
    {
        return (frac(52.9829189h * frac(dot(pixelPosition, half2(0.06711056h, 0.00583715h)))) - 0.5h) * 0.00392156862h;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Extract Region"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragExtract

            half4 FragExtract(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, _SourceRegion.xy + input.uv * _SourceRegion.zw);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Kawase Down"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragDown

            half4 FragDown(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const float2 halfPixel = _SourceTexelSize.xy * (_GlassSampleOffset * 0.5f);
                half4 color = SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv, 0.0f) * 0.5h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2( halfPixel.x,  halfPixel.y), 0.0f) * 0.125h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(-halfPixel.x,  halfPixel.y), 0.0f) * 0.125h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2( halfPixel.x, -halfPixel.y), 0.0f) * 0.125h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(-halfPixel.x, -halfPixel.y), 0.0f) * 0.125h;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Kawase Up"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragUp

            half4 FragUp(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const float2 halfPixel = _SourceTexelSize.xy * (_GlassSampleOffset * 0.5f);
                half4 color = 0;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(-halfPixel.x * 2.0f, 0.0f), 0.0f) / 12.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(-halfPixel.x,  halfPixel.y), 0.0f) / 6.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(0.0f,  halfPixel.y * 2.0f), 0.0f) / 12.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2( halfPixel.x,  halfPixel.y), 0.0f) / 6.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2( halfPixel.x * 2.0f, 0.0f), 0.0f) / 12.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2( halfPixel.x, -halfPixel.y), 0.0f) / 6.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(0.0f, -halfPixel.y * 2.0f), 0.0f) / 12.0h;
                color += SAMPLE_TEXTURE2D_X_LOD(_MainTex, sampler_LinearClamp, input.uv + float2(-halfPixel.x, -halfPixel.y), 0.0f) / 6.0h;
                [branch] if (_GlassDitherStrength > 0.0f)
                    color.rgb += _GlassDitherStrength * GlassInterleavedGradientNoise(input.positionCS.xy + _GlassDitherOffset);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Composite Glass"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma multi_compile_local_fragment _ FLEXIBLE_GLASS_EDGE_OPPOSING FLEXIBLE_GLASS_EDGE_POINT
            #pragma multi_compile_local_fragment __ FLEXIBLE_GLASS_EDGE_DISABLED

            struct GlassElement
            {
                float4 screenToUv0;
                float4 screenToUv1;
                float4 screenToUv2;
                float4 sizeOperationShape;
                float4 screenBounds;
                float4 color;
                float4 optics0;
                float4 optics1;
                float4 lighting;
                float4 shadow;
                float4 sdfData;
            };

            StructuredBuffer<GlassElement> _GlassElements;
            int _GlassElementCount;
            float4 _GlassRegion;
            float4 _GlassBlurRegion;
            float4 _GlassTargetSize;
            float _GlassCompositionBlend;
            float _GlassCompositionInverseBlend;
            int _GlassUniformAppearance;
            int _GlassShadowMode;
            float _GlassReconstructionMaxLod;
            float _GlassBlurMaxLod;
            float _GlassUseBlur;
            float _GlassSdfResolution;
            float _GlassSdfMaxLod;
            float4 _GlassEdgeLighting;
            half4 _GlassEdgeHighlight;
            half4 _GlassEdgeShadow;
            static const float ShadowFalloffExtent = 6.0f;

            #define FLEXIBLE_GLASS_SAMPLE_RETAINED_FIELD(uv, slice, lod) SAMPLE_TEXTURE2D_ARRAY_LOD(_GlassSdfAtlas, sampler_GlassSdfAtlas, uv, slice, lod)
            #include "GlassRetainedFieldSampling.hlsl"
            #include "FlexibleGlassLighting.hlsl"

            half4 UnpackColor(float packedValue)
            {
                const uint packed = asuint(packedValue);
                return half4(packed >> 24 & 255, packed >> 16 & 255, packed >> 8 & 255, packed & 255) / 255.0h;
            }

            float PackColor(half4 color)
            {
                const uint4 bytes = (uint4)round(saturate(color) * 255.0h);
                return asfloat(bytes.x << 24 | bytes.y << 16 | bytes.z << 8 | bytes.w);
            }

            struct OpticalField
            {
                float value;
                float2 gradient;
            };

            OpticalField CreateOpticalField(float value, float2 gradient)
            {
                OpticalField field;
                field.value = value;
                field.gradient = gradient;
                return field;
            }

            OpticalField BlendOpticalFields(OpticalField left, OpticalField right, float rightWeight)
            {
                return CreateOpticalField(lerp(left.value, right.value, rightWeight), lerp(left.gradient, right.gradient, rightWeight));
            }

            float OrientedCompositionBlend(float blend, float2 leftGradient, float2 rightGradient)
            {
                const float leftLengthSquared = dot(leftGradient, leftGradient);
                const float rightLengthSquared = dot(rightGradient, rightGradient);
                if (blend <= 1e-4f || min(leftLengthSquared, rightLengthSquared) <= 1e-8f)
                    return blend;

                const float alignment = dot(leftGradient, rightGradient) * rsqrt(leftLengthSquared * rightLengthSquared);
                return blend * sqrt(saturate(0.5f - 0.5f * alignment));
            }

            void ProjectPosition(float2 screenPosition, GlassElement element, out float2 position, out float2 localDx, out float2 localDy, out float valid)
            {
                const float3 screen = float3(screenPosition, 1.0f);
                [branch] if (element.screenToUv2.w > 0.5f)
                {
                    valid = element.screenToUv2.z > 1e-6f ? 1.0f : 0.0f;
                    const float inverseDenominator = rcp(max(element.screenToUv2.z, 1e-6f));
                    position = float2(dot(element.screenToUv0.xyz, screen), dot(element.screenToUv1.xyz, screen)) * inverseDenominator;
                    localDx = float2(element.screenToUv0.x, element.screenToUv1.x) * inverseDenominator;
                    localDy = float2(element.screenToUv0.y, element.screenToUv1.y) * inverseDenominator;
                }
                else
                {
                    const float3 projected = float3
                    (
                        dot(element.screenToUv0.xyz, screen),
                        dot(element.screenToUv1.xyz, screen),
                        dot(element.screenToUv2.xyz, screen)
                    );
                    // The inverse homography is undefined on its projective horizon. Its
                    // denominator changes linearly in screen space, so use the change over
                    // two pixels as a transform-relative exclusion band. This clips only the
                    // numerically unresolved horizon rather than imposing an angle limit.
                    const float denominatorPerPixel = abs(element.screenToUv2.x) + abs(element.screenToUv2.y);
                    const float denominatorGuard = max(denominatorPerPixel * 2.0f, 1e-6f);
                    valid = projected.z > denominatorGuard ? 1.0f : 0.0f;
                    const float denominator = max(projected.z, denominatorGuard);
                    const float inverseDenominator = rcp(denominator);
                    const float2 denominatorGradient = valid > 0.5f ? element.screenToUv2.xy : 0.0f;
                    position = projected.xy * inverseDenominator;
                    localDx = (float2(element.screenToUv0.x, element.screenToUv1.x) - projected.xy * inverseDenominator * denominatorGradient.x) * inverseDenominator;
                    localDy = (float2(element.screenToUv0.y, element.screenToUv1.y) - projected.xy * inverseDenominator * denominatorGradient.y) * inverseDenominator;
                }
            }

            struct DistanceData
            {
                float valid;
                float surfaceValid;
                float surface;
                float shadowSurface;
                float shadow;
                OpticalField optical;
                float2 lighting;
            };

            float3 RetainedField(float2 position, GlassElement element, float lod, float normalizeGradient)
            {
                const float2 padding = max(element.sdfData.xy, 0.0f);
                const float2 domainSize = max(element.sizeOperationShape.xy + 2.0f * padding, 1e-5f);
                const float2 unclampedUv = (position + padding) / domainSize;
                const float2 fieldUv = saturate(unclampedUv);
                float4 field = SampleGlassRetainedField(fieldUv, element.sdfData.z, lod);
                const float gradientLengthSquared = dot(field.yz, field.yz);
                const float2 unitGradient = gradientLengthSquared > 1e-10f ? field.yz * rsqrt(gradientLengthSquared) : float2(1.0f, 0.0f);
                const float2 retainedGradient = lerp(field.yz, unitGradient, normalizeGradient);
                const float2 outsideDelta = (unclampedUv - fieldUv) * domainSize;
                if (dot(outsideDelta, outsideDelta) <= 1e-8f)
                    return float3(field.x, retainedGradient);

                const float2 boundaryVector = unitGradient * max(field.x, 0.0f) + outsideDelta;
                const float boundaryLength = length(boundaryVector);
                return float3(boundaryLength, boundaryLength > 1e-5f ? boundaryVector / boundaryLength : unitGradient);
            }

            float3 RetainedOpticalField(float2 position, GlassElement element, float lod)
            {
                const float2 padding = max(element.sdfData.xy, 0.0f);
                const float2 domainSize = max(element.sizeOperationShape.xy + 2.0f * padding, 1e-5f);
                const float2 fieldUv = saturate((position + padding) / domainSize);
                const float4 field = SampleGlassRetainedField(fieldUv, element.sdfData.z, lod);
                return float3(field.w, field.yz);
            }

            float RetainedDistance(float2 position, GlassElement element)
            {
                const float2 padding = max(element.sdfData.xy, 0.0f);
                const float2 domainSize = max(element.sizeOperationShape.xy + 2.0f * padding, 1e-5f);
                const float2 unclampedUv = (position + padding) / domainSize;
                const float2 fieldUv = saturate(unclampedUv);
                const float3 field = SAMPLE_TEXTURE2D_ARRAY_LOD(_GlassSdfAtlas, sampler_GlassSdfAtlas, fieldUv, element.sdfData.z, 0.0f).xyz;
                const float2 outsideDelta = (unclampedUv - fieldUv) * domainSize;
                if (dot(outsideDelta, outsideDelta) <= 1e-8f)
                    return field.x;

                const float gradientLengthSquared = dot(field.yz, field.yz);
                const float2 unitGradient = gradientLengthSquared > 1e-10f ? field.yz * rsqrt(gradientLengthSquared) : float2(1.0f, 0.0f);
                return length(unitGradient * max(field.x, 0.0f) + outsideDelta);
            }

            float RetainedOpticalLod(GlassElement element, float2 surfaceDx, float2 surfaceDy, float smoothness)
            {
                const float2 domainSize = max(element.sizeOperationShape.xy + 2.0f * max(element.sdfData.xy, 0.0f), 1e-5f);
                const float2 localTexelSize = domainSize / max(_GlassSdfResolution, 1.0f);
                const float determinant = max(abs(surfaceDx.x * surfaceDy.y - surfaceDx.y * surfaceDy.x), 1e-6f);
                const float screenTexelX = length(float2(surfaceDy.y, -surfaceDx.y)) * localTexelSize.x / determinant;
                const float screenTexelY = length(float2(-surfaceDy.x, surfaceDx.x)) * localTexelSize.y / determinant;
                const float screenTexelSize = sqrt(max(screenTexelX * screenTexelY, 1e-6f));
                const float radiusTexels = max(smoothness, 0.0f) * max(element.optics1.x, 0.0f) / screenTexelSize;
                return clamp(log2(1.0f + radiusTexels), 0.0f, _GlassSdfMaxLod);
            }

            DistanceData ElementDistances(float2 screenPosition, GlassElement element)
            {
                float2 surfacePosition;
                float2 surfaceDx;
                float2 surfaceDy;
                float validProjection;
                ProjectPosition(screenPosition, element, surfacePosition, surfaceDx, surfaceDy, validProjection);
                float inverseScreenGradientLength;
                float surfaceLocalDistance;
                float surfaceDistance;
                const float3 surfaceField = RetainedField(surfacePosition, element, 0.0f, 1.0f);
                surfaceLocalDistance = surfaceField.x;
                [branch] if (element.sizeOperationShape.w > 0.0f)
                {
                    inverseScreenGradientLength = element.sizeOperationShape.w;
                    surfaceDistance = surfaceLocalDistance * inverseScreenGradientLength;
                }
                else
                {
                    const float2 screenGradient = float2(dot(surfaceField.yz, surfaceDx), dot(surfaceField.yz, surfaceDy));
                    inverseScreenGradientLength = rsqrt(max(dot(screenGradient, screenGradient), 1e-8f));
                    surfaceDistance = surfaceLocalDistance * inverseScreenGradientLength;
                }
                DistanceData result;
                // The CPU-provided screen bounds already include optical, composition,
                // and shadow support. A second finite cutoff here creates visible element
                // seams, especially when zero-size shadows do not mask the discontinuity.
                result.valid = validProjection;
                result.surfaceValid = validProjection;
                result.surface = surfaceDistance;
                result.shadowSurface = surfaceLocalDistance * element.shadow.x;
                result.shadow = result.shadowSurface;
                const bool centeredShadow = dot(element.shadow.zw, element.shadow.zw) <= 1e-8f;
                [branch] if (_GlassShadowMode > 1 && !centeredShadow)
                {
                    const float2 shadowPosition = surfacePosition - element.shadow.zw;
                    result.shadow = RetainedDistance(shadowPosition, element) * element.shadow.x;
                }
                float opticalLod;
                float lightingLod;
                [branch] if (element.screenToUv2.w > 0.5f)
                {
                    opticalLod = element.screenToUv0.w;
                    lightingLod = element.screenToUv1.w;
                }
                else
                {
                    opticalLod = RetainedOpticalLod(element, surfaceDx, surfaceDy, element.sdfData.w);
                    lightingLod = RetainedOpticalLod(element, surfaceDx, surfaceDy, 1.0f);
                }
                const float3 opticalField = RetainedOpticalField(surfacePosition, element, opticalLod);
                float2 opticalGradient = -float2(dot(opticalField.yz, surfaceDx), dot(opticalField.yz, surfaceDy)) * inverseScreenGradientLength;
                [branch] if (element.sizeOperationShape.w <= 0.0f)
                {
                    const float opticalGradientLengthSquared = dot(opticalGradient, opticalGradient);
                    if (opticalGradientLengthSquared > 1.0f)
                        opticalGradient *= rsqrt(opticalGradientLengthSquared);
                }
                const float rawOpticalValue = -result.surface;
                const float smoothedOpticalValue = -opticalField.x * inverseScreenGradientLength;
                const float interiorBlend = smoothstep(0.0f, 1.0f, rawOpticalValue / max(element.optics1.x * 0.25f, 1.0f));
                result.optical = CreateOpticalField(lerp(rawOpticalValue, smoothedOpticalValue, interiorBlend), opticalGradient);
                result.lighting = opticalGradient;
                const half2 edgeLightWidths = UnpackColor(element.lighting.x).xy;
                const bool needsSmoothedLighting = _GlassEdgeLighting.z > 1e-4f && max(_GlassEdgeHighlight.a, _GlassEdgeShadow.a) > 1e-4h && max(edgeLightWidths.x, edgeLightWidths.y) > 1e-4h && lightingLod > opticalLod + 1e-4f && rawOpticalValue > 0.0f && rawOpticalValue < element.optics1.x * 1.25f;
                [branch] if (needsSmoothedLighting)
                {
                    const float2 padding = max(element.sdfData.xy, 0.0f);
                    const float2 domainSize = max(element.sizeOperationShape.xy + 2.0f * padding, 1e-5f);
                    const float2 lightingUv = saturate((surfacePosition + padding) / domainSize);
                    const float2 lightingFieldGradient = SAMPLE_TEXTURE2D_ARRAY_LOD(_GlassSdfAtlas, sampler_GlassSdfAtlas, lightingUv, element.sdfData.z, lightingLod).yz;
                    result.lighting = -float2(dot(lightingFieldGradient, surfaceDx), dot(lightingFieldGradient, surfaceDy)) * inverseScreenGradientLength;
                }
                return result;
            }

            float SmoothMinimum(float left, float right, float blend, out float rightWeight)
            {
                if (blend <= 1e-4f)
                {
                    rightWeight = right < left ? 1.0f : 0.0f;
                    return min(left, right);
                }

                rightWeight = saturate(0.5f + 0.5f * (left - right) * _GlassCompositionInverseBlend);
                return lerp(left, right, rightWeight) - blend * rightWeight * (1.0f - rightWeight);
            }

            float SmoothMaximum(float left, float right, float blend, out float rightWeight)
            {
                if (blend <= 1e-4f)
                {
                    rightWeight = right > left ? 1.0f : 0.0f;
                    return max(left, right);
                }

                rightWeight = saturate(0.5f + 0.5f * (right - left) * _GlassCompositionInverseBlend);
                return lerp(left, right, rightWeight) + blend * rightWeight * (1.0f - rightWeight);
            }

            float ShadowFalloff(float scaledDistance)
            {
                const float cutoff = exp2(-ShadowFalloffExtent);
                const float falloff = exp2(-scaledDistance);
                const float normalized = saturate((falloff - cutoff) / (1.0f - cutoff));
                return normalized * normalized * (3.0f - 2.0f * normalized);
            }

            #include "GlassPhysicalOptics.hlsl"

            half3 SampleReconstructedBackdrop(float2 uv, float lod, float2 textureSize)
            {
                lod = clamp(lod, 0.0f, _GlassReconstructionMaxLod);
                const float upperLod = min(ceil(lod), _GlassReconstructionMaxLod);
                const float2 margin = exp2(upperLod) * 0.5f / textureSize;
                return SAMPLE_TEXTURE2D_X_LOD(_GlassSharpTex, sampler_TrilinearClamp, clamp(uv, margin, 1.0f - margin), lod).rgb;
            }

            half3 SampleGlassBackdrop(float2 targetUv, float2 regionUv, float reconstructionLod, float2 targetSize, float2 regionUvMargin)
            {
                half3 color = 0.0h;
                [branch] if (_GlassUseBlur > 0.5f)
                {
                    const float lod = clamp(reconstructionLod, 0.0f, _GlassBlurMaxLod);
                    const float upperLod = min(ceil(lod), _GlassBlurMaxLod);
                    const float2 margin = max(regionUvMargin, exp2(upperLod) * 0.5f / max(_GlassBlurRegion.zw, 1.0f));
                    color = SAMPLE_TEXTURE2D_X_LOD(_GlassBlurTex, sampler_TrilinearClamp, clamp(regionUv, margin, 1.0f - margin), lod).rgb;
                }
                else
                    color = SampleReconstructedBackdrop(targetUv, reconstructionLod, targetSize);
                return color;
            }

            half3 SamplePhysicalGlassBackdrop(float2 targetUv, float2 regionUv, float2 normal, float distance, float opticalLip, float strength, float refractiveIndex, float dispersionCoefficient, float magnification, float reconstructionLod, float2 targetSize, float2 regionSize, float2 regionUvMargin)
            {
                const float inverseMagnification = rcp(max(magnification, 1.0f));
                half3 color = 0.0h;
                [unroll] for (int i = -3; i <= 3; i++)
                {
                    const float position = i / 3.0f;
                    const float wavelength = lerp(486.1327f, 656.2725f, position * 0.5f + 0.5f);
                    const float index = PhysicalRefractiveIndex(refractiveIndex, dispersionCoefficient, wavelength);
                    const float displacement = PhysicalRefractionDisplacement(distance, opticalLip, strength, index) * inverseMagnification;
                    const half3 sampleColor = SampleGlassBackdrop(targetUv + normal * displacement / targetSize, regionUv + normal * displacement / regionSize, reconstructionLod, targetSize, regionUvMargin);
                    const half3 spectralWeight = half3(saturate(position), 1.0f - abs(position), saturate(-position));
                    color += sampleColor * spectralWeight;
                }
                return color * half3(0.5h, 0.3333333h, 0.5h);
            }

            struct GlassAppearanceData
            {
                float4 color;
                float4 optics0;
                float4 optics1;
                float4 lighting;
            };

            GlassAppearanceData DecodeAppearance(GlassElement element)
            {
                GlassAppearanceData appearance;
                appearance.color = element.color;
                appearance.optics0 = element.optics0;
                appearance.optics1 = element.optics1;
                appearance.lighting = float4(UnpackColor(element.lighting.x).xy, element.lighting.yz);
                return appearance;
            }

            GlassAppearanceData BlendAppearance(GlassAppearanceData left, GlassElement right, float weight)
            {
                left.color = lerp(left.color, right.color, weight);
                left.optics0 = lerp(left.optics0, right.optics0, weight);
                left.optics1 = lerp(left.optics1, right.optics1, weight);
                left.lighting = lerp(left.lighting, float4(UnpackColor(right.lighting.x).xy, right.lighting.yz), weight);
                return left;
            }

            half4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const float2 screenPosition = _GlassRegion.xy + input.uv * _GlassRegion.zw;
                float additiveDistance = 1e20f;
                float cutoutDistance = 1e20f;
                float additiveShadowSurfaceDistance = 1e20f;
                float cutoutShadowSurfaceDistance = 1e20f;
                float additiveShadowDistance = 1e20f;
                float cutoutShadowDistance = 1e20f;
                OpticalField additiveOptical = CreateOpticalField(0.0f, 0.0f);
                OpticalField cutoutOptical = CreateOpticalField(0.0f, 0.0f);
                float2 additiveLighting = 0.0f;
                float2 cutoutLighting = 0.0f;
                GlassAppearanceData surface;
                float additiveSurfaceValidity = 0.0f;
                float shadowOpacity = 0.0f;
                float shadowSize = 0.0f;
                half4 shadowColor = 0.0h;
                bool hasSurface = false;
                bool hasCutout = false;

                [loop] for (int i = 0; i < _GlassElementCount; i++)
                {
                    #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                        const GlassElement element = _GlassElements[i * 2 + unity_StereoEyeIndex];
                    #else
                        const GlassElement element = _GlassElements[i];
                    #endif
                    [branch] if (any(screenPosition < element.screenBounds.xy) || any(screenPosition > element.screenBounds.zw))
                        continue;
                    const DistanceData elementDistances = ElementDistances(screenPosition, element);
                    [branch] if (elementDistances.valid < 0.5f)
                        continue;
                    const float elementDistance = elementDistances.surface;
                    const float elementShadowDistance = elementDistances.shadow;
                    [branch] if (element.sizeOperationShape.z < 0.5f)
                    {
                        additiveSurfaceValidity = max(additiveSurfaceValidity, elementDistances.surfaceValid);
                        if (!hasSurface)
                        {
                            additiveDistance = elementDistance;
                            additiveOptical = elementDistances.optical;
                            additiveLighting = elementDistances.lighting;
                            surface = DecodeAppearance(element);
                            [branch] if (_GlassShadowMode > 0)
                            {
                                additiveShadowSurfaceDistance = elementDistances.shadowSurface;
                                [branch] if (_GlassShadowMode > 1)
                                {
                                    additiveShadowDistance = elementShadowDistance;
                                    shadowOpacity = element.optics1.z;
                                }
                                shadowSize = element.shadow.y;
                                shadowColor = UnpackColor(element.lighting.w);
                            }
                            hasSurface = true;
                        }
                        else
                        {
                            float appearanceWeight;
                            const float additiveBlend = OrientedCompositionBlend(_GlassCompositionBlend, additiveOptical.gradient, elementDistances.optical.gradient);
                            additiveDistance = SmoothMinimum(additiveDistance, elementDistance, additiveBlend, appearanceWeight);
                            additiveOptical = BlendOpticalFields(additiveOptical, elementDistances.optical, appearanceWeight);
                            additiveLighting = lerp(additiveLighting, elementDistances.lighting, appearanceWeight);
                            float shadowAppearanceWeight = appearanceWeight;
                            [branch] if (_GlassShadowMode > 0)
                            {
                                float unusedShadowSurfaceWeight;
                                additiveShadowSurfaceDistance = SmoothMinimum(additiveShadowSurfaceDistance, elementDistances.shadowSurface, additiveBlend, unusedShadowSurfaceWeight);
                                [branch] if (_GlassShadowMode > 1)
                                    additiveShadowDistance = SmoothMinimum(additiveShadowDistance, elementShadowDistance, additiveBlend, shadowAppearanceWeight);
                            }
                            [branch] if (_GlassUniformAppearance == 0)
                            {
                                surface = BlendAppearance(surface, element, appearanceWeight);
                                [branch] if (_GlassShadowMode > 0)
                                {
                                    [branch] if (_GlassShadowMode > 1)
                                        shadowOpacity = lerp(shadowOpacity, element.optics1.z, shadowAppearanceWeight);
                                    shadowSize = lerp(shadowSize, element.shadow.y, shadowAppearanceWeight);
                                    shadowColor = lerp(shadowColor, UnpackColor(element.lighting.w), shadowAppearanceWeight);
                                }
                            }
                        }
                    }
                    else if (!hasCutout)
                    {
                        cutoutDistance = elementDistance;
                        cutoutOptical = elementDistances.optical;
                        cutoutLighting = elementDistances.lighting;
                        [branch] if (_GlassShadowMode > 0)
                        {
                            cutoutShadowSurfaceDistance = elementDistances.shadowSurface;
                            [branch] if (_GlassShadowMode > 1)
                                cutoutShadowDistance = elementShadowDistance;
                        }
                        hasCutout = true;
                    }
                    else
                    {
                        float appearanceWeight;
                        const float cutoutBlend = OrientedCompositionBlend(_GlassCompositionBlend, cutoutOptical.gradient, elementDistances.optical.gradient);
                        cutoutDistance = SmoothMinimum(cutoutDistance, elementDistance, cutoutBlend, appearanceWeight);
                        cutoutOptical = BlendOpticalFields(cutoutOptical, elementDistances.optical, appearanceWeight);
                        cutoutLighting = lerp(cutoutLighting, elementDistances.lighting, appearanceWeight);
                        [branch] if (_GlassShadowMode > 0)
                        {
                            float unusedShadowSurfaceWeight;
                            cutoutShadowSurfaceDistance = SmoothMinimum(cutoutShadowSurfaceDistance, elementDistances.shadowSurface, cutoutBlend, unusedShadowSurfaceWeight);
                            [branch] if (_GlassShadowMode > 1)
                            {
                                float unusedShadowWeight;
                                cutoutShadowDistance = SmoothMinimum(cutoutShadowDistance, elementShadowDistance, cutoutBlend, unusedShadowWeight);
                            }
                        }
                    }
                }

                if (!hasSurface)
                    discard;

                float cutoutWeight = 0.0f;
                const float subtractionBlend = hasCutout ? OrientedCompositionBlend(_GlassCompositionBlend, additiveOptical.gradient, -cutoutOptical.gradient) : 0.0f;
                const float compositeDistance = hasCutout ? SmoothMaximum(additiveDistance, -cutoutDistance, subtractionBlend, cutoutWeight) : additiveDistance;
                float unusedShadowWeight;
                const float compositeShadowSurfaceDistance = hasCutout ? SmoothMaximum(additiveShadowSurfaceDistance, -cutoutShadowSurfaceDistance, subtractionBlend, unusedShadowWeight) : additiveShadowSurfaceDistance;
                float compositeShadowDistance = compositeShadowSurfaceDistance;
                [branch] if (_GlassShadowMode > 1)
                    compositeShadowDistance = hasCutout ? SmoothMaximum(additiveShadowDistance, -cutoutShadowDistance, subtractionBlend, unusedShadowWeight) : additiveShadowDistance;
                OpticalField optical = additiveOptical;
                if (hasCutout)
                {
                    cutoutOptical.value = -cutoutOptical.value;
                    cutoutOptical.gradient = -cutoutOptical.gradient;
                    optical = BlendOpticalFields(additiveOptical, cutoutOptical, cutoutWeight);
                }
                const float2 lightingGradient = hasCutout ? lerp(additiveLighting, -cutoutLighting, cutoutWeight) : additiveLighting;
                const float distancePixels = compositeDistance;
                const float antialias = max(fwidth(distancePixels), 0.75f);
                const float coverage = saturate(0.5f - distancePixels / antialias) * additiveSurfaceValidity;
                float surfaceAlpha = coverage * surface.optics1.z;
                float shadowAlpha = 0.0f;
                [branch] if (_GlassShadowMode > 0 && coverage < 1.0f)
                {
                    const float resolvedShadowOpacity = _GlassShadowMode > 1 ? shadowOpacity : surface.optics1.z;
                    const float scaledShadowDistance = max(compositeShadowDistance, 0.0f) / max(shadowSize, 0.5f);
                    [branch] if (scaledShadowDistance < ShadowFalloffExtent)
                    {
                        shadowAlpha = resolvedShadowOpacity * shadowColor.a * (1.0f - coverage) * ShadowFalloff(scaledShadowDistance);
                        [branch] if (shadowAlpha > 0.0f)
                        {
                            const float shadowEnergy = saturate(max(max(shadowColor.r, shadowColor.g), shadowColor.b));
                            const float shadowDitherStrength = lerp(5.0f, 2.0f, shadowEnergy);
                            const float ditherEnvelope = saturate(min(shadowAlpha, 1.0f - shadowAlpha) * 255.0f / shadowDitherStrength);
                            shadowAlpha += ditherEnvelope * shadowDitherStrength * GlassInterleavedGradientNoise(screenPosition);
                        }
                    }
                }
                [branch] if (surfaceAlpha <= 1e-4f)
                {
                    if (shadowAlpha <= 1e-4f)
                        discard;
                    return half4(shadowColor.rgb, shadowAlpha);
                }

                const float opticalGradientLength = length(optical.gradient);
                const float normalCoherence = saturate(opticalGradientLength);
                // Filtered normals carry coherence in their length. Keep the continuous
                // zero limit instead of amplifying a vanishing vector to unit length.
                const float2 opticalNormal = -optical.gradient / max(opticalGradientLength, 1.0f);
                const float opticalDepth = max(optical.value, 0.0f);
                const float thicknessValue = surface.optics1.x;
                const float thickness = max(thicknessValue, 1e-3f);
                const float geometricDepth = max(-distancePixels, 0.0f);
                const float normalizedDepth = thicknessValue > 1e-3f ? ResolveGlassNormalizedDepth(opticalDepth, geometricDepth, thickness, surface.lighting.z) : 1.0f;
                const float bevelPosition = saturate(normalizedDepth);
                const float opticalDistancePixels = -normalizedDepth * thickness;
                const float2 regionSize = max(_GlassBlurRegion.zw, 1.0f);
                const float2 targetSize = max(_GlassTargetSize.xy, 1.0f);
                const float2 regionUvMargin = 0.5f / regionSize;
                const float magnification = max(surface.optics0.z, 1.0f);
                const float2 surfaceCenter = float2(surface.optics0.w, surface.optics1.w);
                const float2 sourcePosition = surfaceCenter + (screenPosition - surfaceCenter) / magnification;
                const float2 blurUv = (sourcePosition - _GlassBlurRegion.xy) / regionSize;
                const float2 targetUv = sourcePosition * _GlassTargetSize.zw;
                const float opticalLip = max(thicknessValue, 1e-4f);
                const float refractionStrength = thicknessValue > 1e-3f ? max(surface.optics0.x, 0.0f) : 0.0f;

                half3 glassColor = 0.0h;
                const float refractiveIndex = max(surface.optics1.y, 1.0f);
                const float abbeNumber = clamp(surface.lighting.w, 0.1f, 64.0f);
                const float dispersionCoefficient = PhysicalDispersionCoefficient(refractiveIndex, abbeNumber);
                const float referenceRefractionPixels = PhysicalRefractionDisplacement(opticalDistancePixels, opticalLip, refractionStrength, refractiveIndex) / magnification;
                const float2 referenceSourcePosition = sourcePosition + opticalNormal * referenceRefractionPixels;
                const float profileReconstructionLod = PhysicalRefractionReconstructionLod(opticalDistancePixels, opticalLip, refractionStrength, normalCoherence, refractiveIndex, dispersionCoefficient, magnification, _GlassReconstructionMaxLod);
                const float rawMappingReconstructionLod = PhysicalRefractionScreenFootprintLod(referenceSourcePosition, _GlassReconstructionMaxLod);
                // A derivative quad crossing the silhouette contains inactive lanes, so its
                // mapping footprint is undefined. Use the analytic profile at the boundary,
                // then smoothly admit the 2D footprint once the quad is safely interior.
                const float mappingDerivativeValidity = saturate((geometricDepth - 1.0f) * 0.5f);
                const float reconstructionLod = lerp(profileReconstructionLod, max(profileReconstructionLod, rawMappingReconstructionLod), mappingDerivativeValidity);
                [branch] if (dispersionCoefficient > 0.0f && refractionStrength > 1e-4f && normalizedDepth < 1.0f && normalCoherence > 0.0f)
                    glassColor = SamplePhysicalGlassBackdrop(targetUv, blurUv, opticalNormal, opticalDistancePixels, opticalLip, refractionStrength, refractiveIndex, dispersionCoefficient, magnification, reconstructionLod, targetSize, regionSize, regionUvMargin);
                else
                {
                    glassColor = SampleGlassBackdrop(targetUv + opticalNormal * referenceRefractionPixels / targetSize, blurUv + opticalNormal * referenceRefractionPixels / regionSize, reconstructionLod, targetSize, regionUvMargin);
                }

                float surfaceEffectCoverage = saturate(abs(referenceRefractionPixels) * 2.0f);
                if (_GlassUseBlur > 0.5f || surface.color.a > 1e-4f || abs(surface.optics0.y - 1.0f) > 1e-4f || abs(magnification - 1.0f) > 1e-4f)
                    surfaceEffectCoverage = 1.0f;

                glassColor *= surface.optics0.y;
                glassColor = lerp(glassColor, surface.color.rgb, saturate(surface.color.a));
                #if !defined(FLEXIBLE_GLASS_EDGE_DISABLED)
                    const float2 edgeLightControls = surface.lighting.xy;
                    const float innerLipAntialias = 1.25f / thickness;
                    const float innerEdgeLightLip = 1.0f - smoothstep(1.0f - innerLipAntialias * 0.5f, 1.0f + innerLipAntialias * 0.5f, normalizedDepth);
                    [branch] if (innerEdgeLightLip > 0.0f)
                    {
                        float2 edgeLightDirection;
                        float edgeLightAttenuation;
                        GlassLipLightDirection(screenPosition * _GlassTargetSize.zw, _GlassTargetSize.xy,
                            _GlassEdgeLighting, edgeLightDirection, edgeLightAttenuation);
                        const float lightingGradientLength = length(lightingGradient);
                        const float2 lightingNormal = -lightingGradient / max(lightingGradientLength, 1.0f);
                        const float innerEdgeLightWidth = edgeLightControls.x;
                        const float innerEdgeLightPixelWidth = thicknessValue * innerEdgeLightWidth;
                        const float opticalAntialias = 0.75f;
                        const float edgeLightAntialias = opticalAntialias / thickness;
                        const float innerEdgeLightDistance = (1.0f - bevelPosition) / max(innerEdgeLightWidth, edgeLightAntialias);
                        const float innerEdgeLightProfile = exp2(-innerEdgeLightDistance * innerEdgeLightDistance) * innerEdgeLightLip;
                        const float innerEdgeLightCoverage = saturate(innerEdgeLightPixelWidth / antialias);
                        const float outerEdgeLightPixelWidth = thicknessValue * edgeLightControls.y;
                        const float outerEdgeLightDistance = geometricDepth / max(outerEdgeLightPixelWidth, antialias);
                        const float outerEdgeLightProfile = exp2(-outerEdgeLightDistance * outerEdgeLightDistance) * innerEdgeLightLip;
                        const float outerEdgeLightCoverage = saturate(outerEdgeLightPixelWidth / antialias);
                        const float2 facing = float2(dot(opticalNormal, edgeLightDirection), dot(lightingNormal, edgeLightDirection));
                        float2 lipBeams = GlassLipLightBeams(facing, _GlassEdgeLighting.z, saturate(_GlassEdgeLighting.w));
                        #if !defined(FLEXIBLE_GLASS_EDGE_POINT)
                            lipBeams *= GlassElementLightFalloff(screenPosition - surfaceCenter, _GlassTargetSize.y, _GlassEdgeLighting.xy, _GlassEdgeLighting.z);
                        #endif
                        const float outerEdgeLightBeam = lipBeams.x;
                        const float innerEdgeLightBeam = lipBeams.y;
                        const float outerHighlightMask = outerEdgeLightCoverage * outerEdgeLightProfile * outerEdgeLightBeam;
                        const float innerHighlightMask = innerEdgeLightCoverage * innerEdgeLightProfile * innerEdgeLightBeam;
                        const float outerShadowMask = outerEdgeLightCoverage * outerEdgeLightProfile * (1.0f - saturate(outerEdgeLightBeam));
                        const float innerShadowMask = innerEdgeLightCoverage * innerEdgeLightProfile * (1.0f - saturate(innerEdgeLightBeam));
                        const half highlightAmount = edgeLightAttenuation * _GlassEdgeHighlight.a * max(outerHighlightMask, innerHighlightMask);
                        const half shadowAmount = saturate(edgeLightAttenuation * _GlassEdgeShadow.a * max(outerShadowMask, innerShadowMask));
                        if (max(highlightAmount, shadowAmount) > 1e-4h)
                            surfaceEffectCoverage = 1.0f;
                        glassColor = lerp(glassColor, _GlassEdgeShadow.rgb, shadowAmount);
                        glassColor += _GlassEdgeHighlight.rgb * highlightAmount;
                    }
                #endif

                surfaceAlpha *= surfaceEffectCoverage;

                const float finalAlpha = surfaceAlpha + shadowAlpha * (1.0f - surfaceAlpha);
                if (finalAlpha <= 1e-4f)
                    discard;

                const half3 premultipliedColor = glassColor * surfaceAlpha + shadowColor.rgb * shadowAlpha * (1.0f - surfaceAlpha);
                return half4(premultipliedColor / finalAlpha, finalAlpha);
            }
            ENDHLSL
        }
    }
}
