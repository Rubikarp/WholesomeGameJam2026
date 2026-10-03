Shader "Hidden/JeffGrawAssets/RegionalBlits" {
    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    struct Attributes
    {
        float4 positionHCS : POSITION;
        float2 uv          : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    v2f vert(Attributes input)
    {
        v2f output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        output.positionCS = mul(GetObjectToWorldMatrix(), float4(input.positionHCS.xyz, 1.0));

        #if UNITY_UV_STARTS_AT_TOP
        output.positionCS.y *= -1;
        #endif

        output.uv = input.uv;
        return output;
    }

    inline float interleavedGradientNoise(float2 pix)
    {
        return (frac(52.9829189h * frac(dot(pix, half2(0.06711056h, 0.00583715h)))) - 0.5h) * 0.00392156862h;
    }

    static const half TWOPI = 2 * PI;
    static half Angle2D(half2 p1, half2 p2)
    {
        const half theta1 = atan2(p1.y, p1.x);
        const half theta2 = atan2(p2.y, p2.x);
        half dtheta = theta2 - theta1;
        dtheta = fmod(dtheta + PI, TWOPI);
        dtheta += TWOPI * (1.0h - step(0.h, dtheta)) - PI;
        return dtheta;
    }

    TEXTURE2D_X(_MainTex);
    SAMPLER(sampler_MainTex);
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}

        Pass
        {
            Name "BlitRegionToDest"

            Cull Off
            ZWrite Off
            ZTest Always
            
            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                float2 _SourceOffset;
                float2 _ScaleFactor;

                #ifdef USING_STEREO_MATRICES
                half2 _SourceOffsetRight;
                half2 _ScaleFactorRight;
                #endif

                half4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    #ifdef USING_STEREO_MATRICES
                    const float2 uv = lerp((input.uv + _SourceOffset) / _ScaleFactor, (input.uv + _SourceOffsetRight) / _ScaleFactorRight, unity_StereoEyeIndex);
                    #else
                    const float2 uv = (input.uv + _SourceOffset) / _ScaleFactor;
                    #endif
                    return SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv);
                }
            ENDHLSL
        }

        Pass
        {
            Name "BlitRegionToDestHQ"

            Cull Off
            ZWrite Off
            ZTest Always
            
            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                float2 _SourceOffset;
                float2 _ScaleFactor;
                half4 _DestTex_TexelSize;

                #ifdef USING_STEREO_MATRICES
                half2 _SourceOffsetRight;
                half2 _ScaleFactorRight;
                #endif

                half4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    #ifdef USING_STEREO_MATRICES
                    const float2 uv = lerp((input.uv + _SourceOffset) / _ScaleFactor, (input.uv + _SourceOffsetRight) / _ScaleFactorRight, unity_StereoEyeIndex);
                    #else
                    const float2 uv = (input.uv + _SourceOffset) / _ScaleFactor;
                    #endif
                    const half sampleDistanceX = 0.28867512324h * _DestTex_TexelSize.x / _ScaleFactor.x;
                    const half sampleDistanceY = 0.5h * _DestTex_TexelSize.y / _ScaleFactor.y;

                    half4 color = 0.28h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv)
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + half2( sampleDistanceX * 2, 0))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + half2(-sampleDistanceX * 2, 0))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + half2( sampleDistanceX,     sampleDistanceY))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + half2( sampleDistanceX,    -sampleDistanceY))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + half2(-sampleDistanceX,     sampleDistanceY))
                                + 0.12h * SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, uv + half2(-sampleDistanceX,    -sampleDistanceY));
  
                    return color;
                }
            ENDHLSL
        }

        Pass
        {
            Name "NormalBlit"

            Cull Off
            ZWrite Off
            ZTest Always
            
            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                float4 _DestinationRegionSize;
                half _DitherStrength;
                half4 _Tint;
                half _Vibrancy;
                half _Brightness;
                half _Contrast;

                half4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, input.uv);
                    col.rgb = _Brightness + (col.rgb - 0.5) * _Contrast + 0.5;
                    const half avg = (col.r + col.g + col.b) / 3.h;
                    col.rgb = lerp(avg, 2 * col.rgb - avg, _Vibrancy);
                    col.rgb = lerp(col.rgb, _Tint.rgb, _Tint.a);
                    col += _DitherStrength * interleavedGradientNoise(input.positionCS.xy);
                    return col;
                }
            ENDHLSL
        }

        Pass
        {
            Name "PerspectiveBlit"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                float2 _Corners[4];
                float4 _DestinationRegionSize;
                half _RenderScale;
                half _DitherStrength;
                half4 _Tint;
                half _Vibrancy;
                half _Brightness;
                half _Contrast;

                half4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    const float2 destPos = half2(input.uv.x * _DestinationRegionSize.z + _DestinationRegionSize.x, input.uv.y * _DestinationRegionSize.w + _DestinationRegionSize.y);
                    const float2 angleDestPos = destPos / _RenderScale;
                    const float2 p1 = _Corners[0] - angleDestPos;
                    const float2 p2 = _Corners[1] - angleDestPos;
                    const float2 p3 = _Corners[2] - angleDestPos;
                    const float2 p4 = _Corners[3] - angleDestPos;
                    float angle = Angle2D(p1, p2);
                    angle     += Angle2D(p2, p3);
                    angle     += Angle2D(p3, p4);
                    angle     += Angle2D(p4, p1);
                    const float inBounds = step(PI, abs(angle));

                    half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, input.uv);
                    col.rgb = _Brightness + (col.rgb - 0.5) * _Contrast + 0.5;
                    const half avg = (col.r + col.g + col.b) / 3.h;
                    col.rgb = lerp(avg, 2 * col.rgb - avg, _Vibrancy);
                    col.rgb = lerp(col.rgb, _Tint.rgb, _Tint.a);
                    col += _DitherStrength * interleavedGradientNoise(input.positionCS.xy);
                    return half4(col.rgb, inBounds);
                }
            ENDHLSL
        }
    }
}
