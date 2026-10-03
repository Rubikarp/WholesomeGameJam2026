Shader "Hidden/JeffGrawAssets/FullScreenBlits" {
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
        float4 positionCS  : SV_POSITION;
        float2 uv          : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    v2f vert(Attributes input)
    {
        v2f output;
        UNITY_SETUP_INSTANCE_ID(input);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

        output.positionCS = float4(input.positionHCS.xyz, 1.0);

        #if UNITY_UV_STARTS_AT_TOP
        output.positionCS.y *= -1;
        #endif

        output.uv = input.uv;

        return output;
    }

    TEXTURE2D_X(_MainTex);
    SAMPLER(sampler_MainTex);

    ENDHLSL
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}

        Pass
        {
            Name "Blit"

            Cull Off
            ZWrite Off
            ZTest Always
            
            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    return SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, input.uv);
                }
            ENDHLSL
        }

        Pass
        {
            Name "BlitAlphaBlend"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                half4 frag(v2f input) : SV_Target
                {
                    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                    return SAMPLE_TEXTURE2D_X(_MainTex, sampler_MainTex, input.uv);
                }
            ENDHLSL
        }
    }
}
