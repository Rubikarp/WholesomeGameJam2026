Shader "Hidden/JeffGrawAssets/Blurs" {
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        half4 positionHCS : POSITION;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f
    {
        half4 positionCS  : SV_POSITION;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    v2f vert(Attributes input)
    {
        v2f output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        output.positionCS = float4(input.positionHCS.xyz, 1);

        #if UNITY_UV_STARTS_AT_TOP
        output.positionCS.y *= -1;
        #endif

        output.uv = input.uv;

        return output;
    }

    TEXTURE2D_X(_MainTex);
    #if UNITY_VERSION < 600000
    SAMPLER(sampler_LinearClamp);
    #endif
    half4 _DestTex_TexelSize;
    half _BlurSampleDistance;
    half2 _SampleOffset;
    bool _OffsetCenter;
    int _BlurIteration;

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "3-Tap"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const float2 texelIndex = floor(i.positionCS.xy);
                    const float checker = 1 - 2 * fmod(floor(0.5 * _BlurIteration) + texelIndex.x + texelIndex.y, 2);
                    const half orientation = fmod(_BlurIteration, 2);
                    const half invOrientation = 1.0h - orientation;
                    const half horOffset = _DestTex_TexelSize.x * _BlurSampleDistance;
                    const half vertOffset = _DestTex_TexelSize.y * _BlurSampleDistance;

                    const half2 offset1 = half2(
                        checker * (0.5h * _DestTex_TexelSize.x * invOrientation + horOffset * orientation),
                        checker * (vertOffset * invOrientation + 0.5h * _DestTex_TexelSize.y * orientation)
                    );

                    const half2 offset2 = half2(
                        lerp(horOffset, -horOffset * checker, orientation),
                        lerp(-vertOffset * checker, vertOffset, orientation)
                    );

                    const half2 offset3 = half2(
                        lerp(-horOffset, -horOffset * checker, orientation),
                        lerp(-vertOffset * checker, -vertOffset, orientation)
                    );

                    return  0.334h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + offset1)
                          + 0.333h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + offset2)
                          + 0.333h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + offset3);
                }
            ENDHLSL
        }

        Pass
        {
            Name "4-Tap Corners"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half2 sampleDist  = _DestTex_TexelSize.xy * _BlurSampleDistance;
                    const half4 color = 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,  sampleDist.y))
                                      + 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,  sampleDist.y))
                                      + 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x, -sampleDist.y))
                                      + 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x, -sampleDist.y));
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "4-Tap Cross"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half2 sampleDist  = _DestTex_TexelSize.xy * _BlurSampleDistance;
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;
                    const half4 color = 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,  halfTexelOffset.y))
                                      + 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,  halfTexelOffset.y))
                                      + 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( halfTexelOffset.x,  sampleDist.y))
                                      + 0.25h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( halfTexelOffset.x, -sampleDist.y));
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "5-Tap Star"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                     UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                     const half2 sampleDist  = _DestTex_TexelSize.xy * _BlurSampleDistance;
                     const half4 color = 0.5h   * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv)
                                       + 0.125h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,  sampleDist.y))
                                       + 0.125h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,  sampleDist.y))
                                       + 0.125h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x, -sampleDist.y))
                                       + 0.125h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x, -sampleDist.y));
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "7-Tap Hexagonal"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half sampleDistanceX = _DestTex_TexelSize.x * _BlurSampleDistance * 0.57735024648h;
                    const half sampleDistanceY = _DestTex_TexelSize.y * _BlurSampleDistance;
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;

                    half4 color = 0.28h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv)
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDistanceX * 2, halfTexelOffset.y))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDistanceX * 2, halfTexelOffset.y))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDistanceX,     sampleDistanceY))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDistanceX,    -sampleDistanceY))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDistanceX,     sampleDistanceY))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDistanceX,    -sampleDistanceY));

                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "8-Tap Corners and Cross"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half2 sampleDist = _DestTex_TexelSize.xy * _BlurSampleDistance;
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;
                    const half4 color = 0.165h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,       sampleDist.y))
                                      + 0.165h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,       sampleDist.y))
                                      + 0.165h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,      -sampleDist.y))
                                      + 0.165h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,      -sampleDist.y))
                                      + 0.085h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( halfTexelOffset.x,  sampleDist.y * 2))
                                      + 0.085h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( halfTexelOffset.x, -sampleDist.y * 2))
                                      + 0.085h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x * 2,   halfTexelOffset.y))
                                      + 0.085h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x * 2,   halfTexelOffset.y));
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "9-Tap Octagonal"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half2 sampleDist = _DestTex_TexelSize.xy * _BlurSampleDistance;
                    const half2 sampleDist2 = sampleDist * 1.414427h;
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;

                    half4 color = 0.2h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv)
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist2.x,      halfTexelOffset.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist2.x,      halfTexelOffset.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( halfTexelOffset.x,  sampleDist2.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( halfTexelOffset.x, -sampleDist2.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,       sampleDist.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,       sampleDist.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDist.x,      -sampleDist.y))
                                + 0.1h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDist.x,      -sampleDist.y));

                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "Quadratic Horizontal"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                uniform uint _TapsPerSideHor;

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;
                    const half sampleDistanceX = _DestTex_TexelSize.x * _BlurSampleDistance;
                    half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(0, halfTexelOffset.y) * _OffsetCenter);
                    half totalWeight = 1.h;

                    for (uint slot = 1; slot <= _TapsPerSideHor; slot++)
                    {
                        const half t = 1 - slot / (_TapsPerSideHor + 1.);
                        const half weight = t * t;
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDistanceX * slot, halfTexelOffset.y));
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDistanceX * slot, halfTexelOffset.y));
                        totalWeight += 2 * weight;
                    }

                    color /= totalWeight;
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "Quadratic Vertical"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                uniform uint _TapsPerSideVert;

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;
                    const half sampleDistanceY = _DestTex_TexelSize.y * _BlurSampleDistance;
                    half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(halfTexelOffset.x, 0) * _OffsetCenter);
                    half totalWeight = 1.h;

                    for (uint slot = 1; slot <= _TapsPerSideVert; slot++)
                    {
                        const half t = 1 - slot / (_TapsPerSideVert + 1.);
                        const half weight = t * t;
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(halfTexelOffset.x,  sampleDistanceY * slot));
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(halfTexelOffset.x, -sampleDistanceY * slot));
                        totalWeight += 2 * weight;
                    }

                    color /= totalWeight;
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "Gaussian Horizontal"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                uniform uint _TapsPerSideHor;

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half lowTapAdjust = 0.8h * max(0, 6 - _TapsPerSideHor * 0.2h);
                    const half sigma = (lowTapAdjust + _TapsPerSideHor) * 2 / 6.;
                    const half weightExpDivisor = -2 * sigma * sigma;
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;
                    const half sampleDistanceX = _DestTex_TexelSize.x * _BlurSampleDistance;
                    half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(0, halfTexelOffset.y) * _OffsetCenter);
                    half totalWeight = 1.h;

                    for (uint slot = 1; slot <= _TapsPerSideHor; slot++)
                    {
                        const half weight = exp(slot * slot / weightExpDivisor);
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2( sampleDistanceX * slot, halfTexelOffset.y));
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(-sampleDistanceX * slot, halfTexelOffset.y));
                        totalWeight += 2 * weight;
                    }

                    color /= totalWeight;
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "Gaussian Vertical"

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                uniform uint _TapsPerSideVert;

                half4 frag(v2f i) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                    const half lowTapAdjust = 0.8h * max(0, 6 - _TapsPerSideVert * 0.2h);
                    const half sigma = (lowTapAdjust + _TapsPerSideVert) * 2 / 6.;
                    const half weightExpDivisor = -2 * sigma * sigma;
                    const half2 halfTexelOffset = _DestTex_TexelSize.xy * _SampleOffset;
                    const half sampleDistanceY = _DestTex_TexelSize.y * _BlurSampleDistance;
                    half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(halfTexelOffset.x, 0) * _OffsetCenter);
                    half totalWeight = 1.h;

                    for (uint slot = 1; slot <= _TapsPerSideVert; slot++)
                    {
                        const half weight = exp(slot * slot / weightExpDivisor);
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(halfTexelOffset.x,  sampleDistanceY * slot));
                        color += weight * SAMPLE_TEXTURE2D_X(_MainTex, sampler_LinearClamp, i.uv + half2(halfTexelOffset.x, -sampleDistanceY * slot));
                        totalWeight += 2 * weight;
                    }

                    color /= totalWeight;
                    return color;
                }
            ENDHLSL
        }
    }
}