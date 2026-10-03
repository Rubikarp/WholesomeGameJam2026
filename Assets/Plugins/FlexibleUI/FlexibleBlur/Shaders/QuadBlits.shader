Shader "Hidden/JeffGrawAssets/QuadBlits"
{
    SubShader
    {
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #pragma vertex vert

        struct Attributes
        {
            float3 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 positionHCS : SV_POSITION;
            float4 screenPos : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f vert (Attributes input)
        {
            v2f output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionHCS = TransformObjectToHClip(float3(input.positionOS));
            output.screenPos = ComputeScreenPos(output.positionHCS);
            return output;
        }

        inline half interleavedGradientNoise(float2 pix)
        {
            return (frac(52.9829189h * frac(dot(pix, half2(0.06711056h, 0.00583715h)))) - 0.5h) * 0.00392156862h;
        }

        TEXTURE2D_X(_MainTex);
        SAMPLER(sampler_MainTex);
        half4 _MainTex_ST;
        float4 _MainTex_TexelSize;
        float4 _BlurRegion;
        float4 _BlurRegionRight;
        half4 _Tint;
        half _Vibrancy;
        half _Brightness;
        half _Contrast;
        half _DitherStrength;

        ENDHLSL

        Pass
        {
            Name "Blit"

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma fragment frag

            half4 frag(v2f input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                const float2 screenSpaceUV = input.screenPos.xy / input.screenPos.w;
                const float4 regionToUse = lerp(_BlurRegion, _BlurRegionRight, unity_StereoEyeIndex);
                float2 adjustedUV = screenSpaceUV - regionToUse.xy;
                adjustedUV /= regionToUse.zw;
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, adjustedUV);
                col.rgb = _Brightness + (col.rgb - 0.5) * _Contrast + 0.5;
                const half avg = (col.r + col.g + col.b) / 3.h;
                col.rgb = lerp(avg, 2 * col.rgb - avg, _Vibrancy);
                col.rgb = lerp(col.rgb, _Tint.rgb, _Tint.a);
                col += _DitherStrength * interleavedGradientNoise(input.positionHCS.xy);
                return col;
            }
            ENDHLSL
        }
    }
}