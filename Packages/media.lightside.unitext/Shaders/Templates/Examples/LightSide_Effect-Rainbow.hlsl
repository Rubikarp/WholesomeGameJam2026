// Rainbow effect — smooth hue gradient flowing across and within letters, shimmering over time.
// Included by LightSide_Custom-Rainbow.shader (Canvas) and LightSide_Custom-World-Rainbow.shader.
//
// lineFlow (cluster index + intra-glyph X) gives a value that increases smoothly across the line, so
// the rainbow is stable at any size/zoom/position. Per-text hue offset: userA.x.

float _HueScale, _HueOffset, _HueSpeed, _Saturation, _Brightness;

half4 LightSideEffect(LightSideFrag i)
{
    float hue = (i.lineFlow.x + i.lineFlow.y) * _HueScale + _HueOffset + i.userA.x + _Time.y * _HueSpeed;
    half3 rgb = LightSideCosPalette(hue, _Saturation, _Brightness);

    if (i.glyphMode > 1.5)
    {
        half4 col = LightSideTintPremultiplied(i.atlasColor, i.color);
        col.rgb *= rgb;
        return col;
    }

    half a = i.color.a * i.sdfAlpha;
    half4 col;
    col.rgb = rgb * i.color.rgb * a;
    col.a   = a;
    return col;

}
