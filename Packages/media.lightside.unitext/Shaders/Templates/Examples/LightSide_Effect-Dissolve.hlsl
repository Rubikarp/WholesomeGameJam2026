// Dissolve effect — threshold a noise texture against per-text progress, with a glowing edge band.
// Included by both LightSide_Custom-Dissolve.shader (Canvas) and LightSide_Custom-World-Dissolve.shader.
//
// Per-text dynamic parameters: userA.x = progress (0 hidden → 1 visible), userA.y = scroll offset
// (set via MaterialModifier.ConstantUv2 = new Vector4(progress, offset, 0, 0)).

LightSide_DECLARE_TEX2D(_NoiseTex);
float  _NoiseScale;
float4 _NoiseScroll;
float  _EdgeWidth;
float  _EdgeSoftness;
half4  _EdgeColor;

half4 LightSideEffect(LightSideFrag i)
{
    // i.tileHash (prelude-computed per vertex) desyncs the noise pattern between letters, size-invariantly.
    float2 noiseUV = i.glyphUV * _NoiseScale + i.tileHash + _NoiseScroll.xy * _Time.y + _NoiseScroll.zw + i.userA.y;
    float n = LightSide_SAMPLE_TEX2D(_NoiseTex, noiseUV).r;

    float threshold = 1.0 - saturate(i.userA.x);
    float soft = max(_EdgeSoftness, 1e-4);
    float edgeLow  = smoothstep(threshold - soft, threshold + soft, n);
    float edgeHigh = smoothstep(threshold + _EdgeWidth - soft, threshold + _EdgeWidth + soft, n);

    if (i.glyphMode > 1.5)
    {
        half4 face = LightSideTintPremultiplied(i.atlasColor, i.color) * edgeHigh;
        float edgeA = i.atlasColor.a * i.color.a * _EdgeColor.a * (edgeLow - edgeHigh);

        half4 col;
        col.rgb = face.rgb + _EdgeColor.rgb * edgeA;
        col.a   = face.a + edgeA;
        return col;
    }

    float faceA = i.sdfAlpha * i.color.a * edgeHigh;
    float edgeA = i.sdfAlpha * _EdgeColor.a * (edgeLow - edgeHigh);

    half4 col;
    col.rgb = i.color.rgb * faceA + _EdgeColor.rgb * edgeA;
    col.a   = faceA + edgeA;
    return col;

}
