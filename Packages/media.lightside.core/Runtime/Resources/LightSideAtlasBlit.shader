Shader "Hidden/LightSide/AtlasBlit"
{
    SubShader
    {
        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_LsAtlasBlitSource);
            float _LsAtlasBlitSlice;
            float _LsAtlasBlitLod;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return UNITY_SAMPLE_TEX2DARRAY_LOD(_LsAtlasBlitSource,
                    float3(i.uv, _LsAtlasBlitSlice), _LsAtlasBlitLod);
            }
            ENDCG
        }
    }
    Fallback Off
}
