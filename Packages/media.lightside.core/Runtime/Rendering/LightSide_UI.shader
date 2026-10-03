// Unified text rendering — one material serves SDF, MSDF and color glyphs; the per-glyph mode
// rides the vertex stream (UV1.w, see LightSideGlyphMode). Text quads = coverage(mode) x paint(kind):
//   coverage mode (TEXCOORD2.x): 0 fill, 1 stroke, 2 shadow/glow, 3 inner-shadow.
//   paint kind  (TEXCOORD3.w):  0 solid (vertex colour), 1/2/3 gradient (ramp), 4 texture, 5 tiled texture.
// Plain glyphs carry no UV2/UV3 → 0 → fill + solid. One atlas sample (two for inner-shadow),
// one draw call per material. Color quads bypass coverage/paint: one bitmap sample x alpha tint,
// then the colour-matrix row + 1 their TEXCOORD3.z carries (0 = none), applied premultiplied.

Shader "LightSide/UI" {

Properties {
	_ShaderFlags		("Flags", float) = 0
	[HideInInspector] _LightSidePaintTexture ("Paint Texture", 2D) = "white" {}
	[HideInInspector] _SrcBlend ("Source RGB Blend", Float) = 1
	[HideInInspector] _DstBlend ("Destination RGB Blend", Float) = 10
	[HideInInspector] _SrcBlendAlpha ("Source Alpha Blend", Float) = 1
	[HideInInspector] _DstBlendAlpha ("Destination Alpha Blend", Float) = 10
	[HideInInspector] _BlendOp ("RGB Blend Operation", Float) = 0
	[HideInInspector] _BlendOpAlpha ("Alpha Blend Operation", Float) = 0

	_ScaleX				("Scale X", float) = 1
	_ScaleY				("Scale Y", float) = 1
	_PerspectiveFilter	("Perspective Correction", Range(0, 1)) = 0.875
	_Sharpness			("Sharpness", Range(-1,1)) = 0

	_VertexOffsetX		("Vertex OffsetX", float) = 0
	_VertexOffsetY		("Vertex OffsetY", float) = 0

	_ClipRect			("Clip Rect", vector) = (-32767, -32767, 32767, 32767)
	_MaskSoftnessX		("Mask SoftnessX", float) = 0
	_MaskSoftnessY		("Mask SoftnessY", float) = 0

	_StencilComp		("Stencil Comparison", Float) = 8
	_Stencil			("Stencil ID", Float) = 0
	_StencilOp			("Stencil Operation", Float) = 0
	_StencilWriteMask	("Stencil Write Mask", Float) = 255
	_StencilReadMask	("Stencil Read Mask", Float) = 255

	_CullMode			("Cull Mode", Float) = 0
	_ColorMask			("Color Mask", Float) = 15
}

SubShader {
	Tags
	{
		"Queue"="Transparent"
		"IgnoreProjector"="True"
		"RenderType"="Transparent"
	}

	Stencil
	{
		Ref [_Stencil]
		Comp [_StencilComp]
		Pass [_StencilOp]
		ReadMask [_StencilReadMask]
		WriteMask [_StencilWriteMask]
	}

	Cull [_CullMode]
	ZWrite Off
	Lighting Off
	Fog { Mode Off }
	ZTest [unity_GUIZTestMode]
	Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
	BlendOp [_BlendOp], [_BlendOpAlpha]
	ColorMask [_ColorMask]

	Pass {
		Name "SDF_DECORATION"
		CGPROGRAM
		#pragma vertex VertShader
		#pragma fragment PixShader
		#pragma target 3.5

		#pragma multi_compile __ UNITY_UI_CLIP_RECT
		#pragma multi_compile __ UNITY_UI_ALPHACLIP
		// Fragment-only keyword (alters paint resolve, never the vertex program) —
		// multi_compile_fragment keeps the vertex variant count flat. SDF vs MSDF vs color is
		// NOT a keyword: the per-glyph mode rides the vertex stream (UV1.w).
		#pragma multi_compile_fragment __ LIGHTSIDE_PAINT_TEXTURE

		#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphField.cginc"

		sampler2D _LightSideGradientRamp;
		float _LightSideGradientRampRows;
		sampler2D _LightSidePaintTexture;
		sampler2D _LightSideColorMatrixAtlas;
		float _LightSideColorMatrixRows;
		// Explicit LOD 0: the ramp has no mips — see LightSidePaint.hlsl for why implicit
		// derivatives would flatten the gradient branch.
		#define LIGHTSIDE_SAMPLE_RAMP(u, v) tex2Dlod(_LightSideGradientRamp, float4(u, v, 0, 0))
		#define LIGHTSIDE_SAMPLE_PAINT(uv)  tex2D(_LightSidePaintTexture, uv)
		#define LIGHTSIDE_SAMPLE_MATRIX(u, v) tex2Dlod(_LightSideColorMatrixAtlas, float4(u, v, 0, 0))

		#include "Packages/media.lightside.core/Runtime/Rendering/LightSideGlyphCoverage.hlsl"
		// Distance paint ramps by the surface's own field, which only the fragment knows — the shape
		// branch assigns it before the paint resolve reads it back through the hook.
		static float lightSideDistanceT;
		#define LIGHTSIDE_PAINT_DISTANCE_T lightSideDistanceT
		#include "Packages/media.lightside.core/Runtime/Rendering/LightSidePaint.hlsl"

		#include "Packages/media.lightside.core/Runtime/Rendering/LightSideShapeSurface.hlsl"

		// Surface kind 4 samples this global array atlas; it is never a material property, so a
		// vector-animation quad and a glyph run share one material and one batch.
		UNITY_DECLARE_TEX2DARRAY(_LightSideLottieAtlas);
		float4 _LightSidePaintTexture_TexelSize;

		struct pixel_t
		{
			UNITY_VERTEX_INPUT_INSTANCE_ID
			UNITY_VERTEX_OUTPUT_STEREO
			float4 vertex   : SV_POSITION;
			float4 atlasUV  : TEXCOORD0; // xy = atlas UV, z = page layer, w = glyph mode (quad-constant)
			float2 glyphUV  : TEXCOORD1; // for fwidth AA
			half4  cov      : TEXCOORD2; // coverageMode, p0, p1, softness (em-scale — half is plenty)
			float4 paint    : TEXCOORD3; // paintU, paintV, rampRow, paintKind + 8 * spread (tiled coords exceed half range)
			half4  mask     : TEXCOORD4;
			fixed4 color    : TEXCOORD5; // straight vertex colour (premultiplied in fragment)
			half4  extra    : TEXCOORD6; // glyphs: faceDilate, glyphH, sdfScale.xy — shapes: .x is the paint scale/fit
			float4 shapeGeom   : TEXCOORD7; // halfSize.xy, aux, trailing mode param
			float4 shapeParams : TEXCOORD8; // per-shape params (radii / ratios / counts)
		};

		pixel_t VertShader(LightSideSurfaceVertex input)
		{
			pixel_t output;

			UNITY_INITIALIZE_OUTPUT(pixel_t, output);
			UNITY_SETUP_INSTANCE_ID(input);
			UNITY_TRANSFER_INSTANCE_ID(input, output);
			UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

			float4 vert = ApplyVertexOffset(input.vertex);
			float4 vPosition = UnityObjectToClipPos(vert);

			float2 pixelSize = vPosition.w;
			pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

			float glyphMode = LightSideGlyphMode(input.texcoord1.w);
			float glyphH = input.texcoord0.w;
			float faceDilate = input.texcoord1.y;
			float2 sdfScale = 0;
			float pageLayer = input.texcoord0.z;
			float2 atlasXY = input.texcoord0.xy;
			if (glyphMode < 1.5)
			{
				float4 t = LightSideLoadGlyphTransform(input.texcoord0.z);
				sdfScale = t.xx;
				atlasXY = input.texcoord0.xy * t.x + t.yz;
				pageLayer = t.w;
			}

			output.vertex = vPosition;
			output.atlasUV = float4(atlasXY, pageLayer, glyphMode);
			output.glyphUV = input.texcoord0.xy;
			output.cov = input.texcoord2;
			output.paint = input.texcoord3;
			output.mask = ComputeMask(vert, pixelSize);
			output.color = GammaToLinearIfNeeded(input.color);
			output.extra = float4(faceDilate, glyphH, sdfScale.x, sdfScale.y);
			output.shapeGeom = float4(input.texcoord0.zw, input.texcoord1.x, input.texcoord1.z);
			output.shapeParams = input.tangent;

			return output;
		}

		fixed4 PixShader(pixel_t input) : SV_Target
		{
			UNITY_SETUP_INSTANCE_ID(input);

			// Derivatives and the (keyword-gated) paint resolve run before the mode branch so every
			// fetch inside it can be gradient-explicit — the branch is quad-constant and never flattens.
			float2 uvDx = ddx(input.atlasUV.xy);
			float2 uvDy = ddy(input.atlasUV.xy);
			float2 dUV = fwidth(input.glyphUV);

			// Analytic shapes evaluate their field first: distance paint reads it back through the hook,
			// and one resolve then serves every kind — which is what keeps the atlas fetches below
			// gradient-explicit instead of forcing the mode branch to flatten.
			bool isShape = input.atlasUV.w > 2.5 && input.atlasUV.w < 3.5;
			float shapeD = 0.0;
			float shapeCoverage = 0.0;
			UNITY_BRANCH
			if (isShape)
			{
				float packed = input.cov.x;
				float shapeStyle = floor(packed * 0.001953125);
				packed -= shapeStyle * 512.0;
				float shapeKind = floor(packed * 0.0625);
				shapeCoverage = LightSideShapeCoverage(shapeKind, packed - shapeKind * 16.0,
					input.glyphUV, input.shapeGeom.xy, input.shapeParams, input.shapeGeom.z,
					float4(input.cov.yzw, input.shapeGeom.w), shapeStyle, shapeD);
				lightSideDistanceT = LightSideShapeDistanceT(shapeD, input.shapeGeom.xy, input.extra.x);
			}

			half4 paintCol = LightSideResolvePaint(input.paint, input.color,
				isShape ? input.extra.x : 1.0, _LightSidePaintTexture_TexelSize.xy * 0.5);

			half4 result;
			UNITY_BRANCH
			if (isShape)
			{
				result = LightSideComposePaintCoverage(paintCol, shapeCoverage);
			}
			else if (input.atlasUV.w > 3.5)
			{
				// Atlas quad (vector animation): a straight-alpha tile tinted by the vertex colour,
				// premultiplied here because this shader blends premultiplied.
				half4 c = UNITY_SAMPLE_TEX2DARRAY(_LightSideLottieAtlas, input.atlasUV.xyz) * input.color;
				result = half4(c.rgb * c.a, c.a);
			}
			else if (input.atlasUV.w > 1.5)
			{
				half4 c = LIGHTSIDE_SAMPLE_COLOR_TEXEL_GRAD(input.atlasUV.xyz, uvDx, uvDy);
				result = c * half4(input.color.rgb * input.color.a, input.color.a);
				if (input.paint.z > 0.5)
					result = LightSideApplyColorMatrixPremultiplied(result, input.paint.z - 1.0);
			}
			else
			{
				float aa = max(dUV.x, dUV.y) * input.extra.y;
				float coverage = LightSideResolveGlyphCoverage(input.atlasUV, input.cov, input.extra, aa, uvDx, uvDy);
				result = LightSideComposePaintCoverage(paintCol, coverage);
			}

			return ApplyClipping(result, input.mask);
		}
		ENDCG
	}
}
}
