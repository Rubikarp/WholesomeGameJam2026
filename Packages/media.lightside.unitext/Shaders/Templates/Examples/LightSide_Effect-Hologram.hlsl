// Hologram effect — iridescent hue shift, horizontal scanlines, noise flicker and soft edge glow.
// Included by LightSide_Custom-Hologram.shader (Canvas) and LightSide_Custom-World-Hologram.shader.
//
// Per-text parameters: userA.x = hue phase, userA.y = flicker phase, userA.z = scan phase,
// userA.w = intensity (0 = use material default).

LightSide_DECLARE_TEX2D(_NoiseTex);
half4 _Tint;
float _HueScale, _HueSpeed, _Saturation, _Brightness;
float _ScanFreq, _ScanSpeed, _ScanContrast;
float _FlickerScale, _FlickerSpeed, _FlickerAmount;
half4 _EdgeColor;
float _EdgeWidth;

half4 LightSideEffect(LightSideFrag i)
{
    // i.tileHash — per-glyph random pair, prelude-computed per vertex (desyncs letters for free).
    float hue = i.tileHash.x + i.glyphUV.y * _HueScale + _Time.y * _HueSpeed + i.userA.x;
    half3 iridescent = LightSideCosPalette(hue, _Saturation, _Brightness) * _Tint.rgb;

    float scan = sin(i.glyphUV.y * _ScanFreq + _Time.y * _ScanSpeed + i.userA.z) * 0.5 + 0.5;
    float scanMul = lerp(1.0 - _ScanContrast, 1.0, scan);

    float2 flickerUV = i.glyphUV * _FlickerScale + i.tileHash + _Time.y * _FlickerSpeed + i.userA.y;
    float flickerN = LightSide_SAMPLE_TEX2D(_NoiseTex, flickerUV).r;
    float flickerMul = lerp(1.0 - _FlickerAmount, 1.0, flickerN);

    float instIntensity = (i.userA.w > 0.0) ? i.userA.w : 1.0;

    if (i.glyphMode > 1.5)
    {
        half4 col = LightSideTintPremultiplied(i.atlasColor, i.color);
        col.rgb *= iridescent * scanMul;
        col *= _Tint.a * flickerMul * instIntensity;
        return col;
    }

    // Face SDF alpha + soft outer edge band for the rim glow (recomputed from signedDist so the band can
    // reach past the face boundary).
    float2 dUV = fwidth(i.glyphUV);
    float aaWidth = max(dUV.x, dUV.y) * i.glyphMeta.x;
    float faceDist = i.signedDist - i.glyphMeta.y * LIGHTSIDE_DILATE_SCALE;

    float faceA = saturate(-faceDist / aaWidth + 0.5);
    float edgeA = saturate(1.0 - abs(faceDist) / (_EdgeWidth + aaWidth));
    edgeA = edgeA * edgeA;
    edgeA *= 1.0 - faceA;

    half3 faceRgb = iridescent * scanMul * flickerMul;
    float faceAlpha = faceA * i.color.a * _Tint.a * flickerMul * instIntensity;

    half3 edgeRgb = _EdgeColor.rgb;
    float edgeAlpha = edgeA * _EdgeColor.a * i.color.a * flickerMul * instIntensity;

    half4 col;
    col.rgb = faceRgb * faceAlpha + edgeRgb * edgeAlpha;
    col.a   = faceAlpha + edgeAlpha;
    return col;

}
