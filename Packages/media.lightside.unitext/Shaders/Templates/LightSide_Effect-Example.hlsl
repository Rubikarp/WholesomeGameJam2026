// Your LightSide effect — write ONLY the visual logic here.
//
// This file is included by both shells (LightSide_Custom-Example.shader = Canvas,
// LightSide_Custom-World-Example.shader = World), so the effect compiles for every pass — Canvas/World,
// built-in/URP, SDF/MSDF/color — without repeating it. You never touch the shells except to list
// Properties.
//
// Declare your textures with LightSide_DECLARE_TEX2D and sample with LightSide_SAMPLE_TEX2D (same call on
// both pipelines); declare other Properties as plain uniforms. Return a PREMULTIPLIED colour
// (rgb already multiplied by alpha) — see the helper below.
//
// LightSideFrag i gives you: sdfAlpha, signedDist, glyphUV, atlasUV, atlasColor, color, glyphMeta
// (glyphH, faceDilate), lineFlow (cluster, intra-glyph X), tileId, tileHash (per-glyph random
// pair, precomputed per vertex), userA/userB (MaterialModifier channels), positionWS. Building
// blocks like LightSideCosPalette / LightSideTileHash come from LightSide_EffectLib.hlsl. For per-vertex
// work (displacement, precomputed randomness) see the LIGHTSIDE_EFFECT_VERT hook in the prelude.

half4 _Tint;

half4 LightSideEffect(LightSideFrag i)
{
    if (i.glyphMode > 1.5)
    {
        return LightSideTintPremultiplied(i.atlasColor, i.color);
    }

    half4 c = i.color * _Tint;
    half  a = c.a * i.sdfAlpha;
    return half4(c.rgb * a, a);               // premultiplied

}
