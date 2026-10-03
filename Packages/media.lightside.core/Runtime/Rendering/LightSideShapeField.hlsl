#ifndef LIGHTSIDE_SHAPE_FIELD_INCLUDED
#define LIGHTSIDE_SHAPE_FIELD_INCLUDED

// Analytic 2D signed-distance primitives for every LightSide analytic surface.
// Each returns the exact signed distance from p (shape-local, origin-centred) to the outline:
// negative inside, positive outside, magnitude in p's units. Formulas after Inigo Quilez
// (iquilezles.org/articles/distfunctions2d), ported GLSL -> HLSL. The fragment stage dispatches
// through evalShapeSdf (bottom of file); the kind ids mirror ShapeKind.cs.

#define UI_SDF_MAX_POLY_VERTS 64
#define UI_SDF_VERT_TEX_WIDTH 32
#define UI_SDF_MAX_COMPOSITE_ELEMENTS 8
#define UI_SDF_COMPOSITE_STRIDE 4

// Polygon outlines are fetched through LIGHTSIDE_SAMPLE_SHAPE_VERTS, defined by the including pipeline
// header against its own sampler — the legacy tex2D intrinsics this used to call do not exist in an
// HLSLPROGRAM, so declaring the texture here would restrict the file to the built-in pipeline.
#ifndef LIGHTSIDE_SAMPLE_SHAPE_VERTS
    #error "Include a LightSide pipeline header (LightSideGlyphField.cginc or LightSideGlyphFieldURP.hlsl) before LightSideShapeField.hlsl."
#endif

// Published row count of the shape vertex atlas; the vertex stream carries stable row indices and the
// V is derived here, so the atlas can grow without touching baked meshes.
float _LightSideShapeVertexRows;

float LightSideShapeRowV(float row) { return (row + 0.5) / max(_LightSideShapeVertexRows, 1.0); }

// GLSL mod(): x - y*floor(x/y). HLSL fmod() truncates toward zero and differs for negative x,
// which the angular folds below depend on.
float sdShapeMod(float a, float b) { return a - b * floor(a / b); }

float sdShapeCircle(float2 p, float r)
{
    return length(p) - r;
}

// Unsigned distance to segment a-b. Capsule = this minus the radius.
float sdShapeSegment(float2 p, float2 a, float2 b)
{
    float2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}

float sdShapeCapsule(float2 p, float2 a, float2 b, float r)
{
    return sdShapeSegment(p, a, b) - r;
}

// Axis-aligned ellipse with radii ab. Exact nearest-point solve.
float sdShapeEllipse(float2 p, float2 ab)
{
    if (abs(ab.x - ab.y) < 1e-4) return length(p) - min(ab.x, ab.y);
    p = abs(p);
    if (p.x > p.y) { p = p.yx; ab = ab.yx; }
    float l = ab.y * ab.y - ab.x * ab.x;
    float m = ab.x * p.x / l;
    float m2 = m * m;
    float n = ab.y * p.y / l;
    float n2 = n * n;
    float c = (m2 + n2 - 1.0) / 3.0;
    float c3 = c * c * c;
    float q = c3 + m2 * n2 * 2.0;
    float d = c3 + m2 * n2;
    float g = m + m * n2;
    float co;
    if (d < 0.0)
    {
        float h = acos(q / c3) / 3.0;
        float s = cos(h);
        float t = sin(h) * 1.7320508;
        float rx = sqrt(-c * (s + t + 2.0) + m2);
        float ry = sqrt(-c * (s - t + 2.0) + m2);
        co = (ry + sign(l) * rx + abs(g) / (rx * ry) - m) / 2.0;
    }
    else
    {
        float h = 2.0 * m * n * sqrt(d);
        float s = sign(q + h) * pow(abs(q + h), 1.0 / 3.0);
        float u = sign(q - h) * pow(abs(q - h), 1.0 / 3.0);
        float rx = -s - u - c * 4.0 + 2.0 * m2;
        float ry = (s - u) * 1.7320508;
        float rm = sqrt(rx * rx + ry * ry);
        co = (ry / sqrt(rm - rx) + 2.0 * g / rm - m) / 2.0;
    }
    float2 r = ab * float2(co, sqrt(1.0 - co * co));
    return length(r - p) * sign(p.y - r.y);
}

// Equilateral triangle, apex up. r is half the base: the outline spans 2r x sqrt(3)*r around the
// centroid, so its box centre sits r/(2*sqrt(3)) above the origin.
float sdShapeEquilateralTriangle(float2 p, float r)
{
    float k = 1.7320508;
    p.x = abs(p.x) - r;
    p.y = p.y + r / k;
    if (p.x + k * p.y > 0.0) p = float2(p.x - k * p.y, -k * p.x - p.y) * 0.5;
    p.x -= clamp(p.x, -2.0 * r, 0.0);
    return -length(p) * sign(p.y);
}

// Regular pentagon, flat edge up. r is the apothem (centre to edge); the circumradius is r/cos(36deg).
float sdShapePentagon(float2 p, float r)
{
    float3 k = float3(0.809016994, 0.587785252, 0.726542528);
    p.x = abs(p.x);
    p -= 2.0 * min(dot(float2(-k.x, k.y), p), 0.0) * float2(-k.x, k.y);
    p -= 2.0 * min(dot(float2( k.x, k.y), p), 0.0) * float2( k.x, k.y);
    p -= float2(clamp(p.x, -r * k.z, r * k.z), r);
    return length(p) * sign(p.y);
}

// Regular hexagon, flat edges up and down. r is the apothem; the circumradius r/cos(30deg) spans the width.
float sdShapeHexagon(float2 p, float r)
{
    float3 k = float3(-0.866025404, 0.5, 0.577350269);
    p = abs(p);
    p -= 2.0 * min(dot(k.xy, p), 0.0) * k.xy;
    p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
    return length(p) * sign(p.y);
}

// Regular octagon, flat edges up and down. r is the apothem, and the box it spans is exactly 2r x 2r.
float sdShapeOctagon(float2 p, float r)
{
    float3 k = float3(-0.9238795325, 0.3826834323, 0.4142135623);
    p = abs(p);
    p -= 2.0 * min(dot(float2( k.x, k.y), p), 0.0) * float2( k.x, k.y);
    p -= 2.0 * min(dot(float2(-k.x, k.y), p), 0.0) * float2(-k.x, k.y);
    p -= float2(clamp(p.x, -k.z * r, k.z * r), r);
    return length(p) * sign(p.y);
}

// n-pointed star, one point up. r = outer radius, n = point count, m in (1,n] = sharpness (2 = deepest).
float sdShapeStar(float2 p, float r, float n, float m)
{
    float an = 3.1415926 / n;
    float en = 3.1415926 / m;
    float2 acs = float2(cos(an), sin(an));
    float2 ecs = float2(cos(en), sin(en));
    float bn = sdShapeMod(atan2(p.x, p.y), 2.0 * an) - an;
    p = length(p) * float2(cos(bn), abs(sin(bn)));
    p -= r * acs;
    p += ecs * clamp(-dot(p, ecs), 0.0, r * acs.y / ecs.y);
    return length(p) * sign(p.x);
}

// Pie / circular sector. c = (sin,cos) of the half-aperture, r = radius.
float sdShapePie(float2 p, float2 c, float r)
{
    p.x = abs(p.x);
    float l = length(p) - r;
    float m = length(p - c * clamp(dot(p, c), 0.0, r));
    return max(l, m * sign(c.y * p.x - c.x * p.y));
}

// Circular arc band. sc = (sin,cos) of the half-aperture, ra = arc radius, rb = half-thickness.
float sdShapeArc(float2 p, float2 sc, float ra, float rb)
{
    p.x = abs(p.x);
    return ((sc.y * p.x > sc.x * p.y) ? length(p - sc * ra) : abs(length(p) - ra)) - rb;
}

// Arc band cut straight across at both ends: the annulus kept where the sector opens, so each end is a
// radial edge instead of the swept disk's semicircular cap. sc = (sin,cos) of the half-aperture.
float sdShapeArcFlat(float2 p, float2 sc, float ra, float rb)
{
    p.x = abs(p.x);
    float band = abs(length(p) - ra) - rb;
    float m = length(p - sc * max(dot(p, sc), 0.0));
    return max(band, m * sign(sc.y * p.x - sc.x * p.y));
}

// Annulus / ring: circle of radius r with half-thickness w.
float sdShapeRing(float2 p, float r, float w)
{
    return abs(length(p) - r) - w;
}

// Disk of radius r cut by a horizontal chord at height h.
float sdShapeCutDisk(float2 p, float r, float h)
{
    float w = sqrt(r * r - h * h);
    p.x = abs(p.x);
    float s = max((h - r) * p.x * p.x + w * w * (h + r - 2.0 * p.y), h * p.x - w * p.y);
    return (s < 0.0) ? length(p) - r : (p.x < w) ? h - p.y : length(p - float2(w, h));
}

// Parallelogram (iq): wi = half-length of the horizontal edges, he = half-height, sk = how far the centre of the
// top edge sits to the right of the origin (the bottom edge's sits as far to the left).
float sdShapeParallelogram(float2 p, float wi, float he, float sk)
{
    float2 e = float2(sk, he);
    p = (p.y < 0.0) ? -p : p;
    float2 w = p - e; w.x -= clamp(w.x, -wi, wi);
    float2 d = float2(dot(w, w), -w.y);
    float s = p.x * e.y - p.y * e.x;
    p = (s < 0.0) ? -p : p;
    float2 v = p - float2(wi, 0.0); v -= e * clamp(dot(v, e) / max(dot(e, e), 1e-8), -1.0, 1.0);
    d = min(d, float2(dot(v, v), wi * he - abs(s)));
    return sqrt(d.x) * sign(-d.y);
}

// Isosceles trapezoid (iq): r1 = bottom half-width, r2 = top half-width, he = half-height.
float sdShapeTrapezoid(float2 p, float r1, float r2, float he)
{
    float2 k1 = float2(r2, he);
    float2 k2 = float2(r2 - r1, 2.0 * he);
    p.x = abs(p.x);
    float2 ca = float2(p.x - min(p.x, (p.y < 0.0) ? r1 : r2), abs(p.y) - he);
    float2 cb = p - k1 + k2 * clamp(dot(k1 - p, k2) / max(dot(k2, k2), 1e-8), 0.0, 1.0);
    float s = (cb.x < 0.0 && ca.y < 0.0) ? -1.0 : 1.0;
    return s * sqrt(min(dot(ca, ca), dot(cb, cb)));
}

// Rhombus (iq): b = half-extents of the box its four points span.
float sdShapeRhombus(float2 p, float2 b)
{
    b.y = -b.y;
    p = abs(p);
    float h = clamp((dot(b, p) + b.y * b.y) / max(dot(b, b), 1e-8), 0.0, 1.0);
    p -= b * float2(h, h - 1.0);
    return length(p) * sign(p.x);
}

// Plus / cross (iq): b = (half-length of an arm, half-thickness of an arm), r shifts the outline, so a
// negative r rounds every corner it turns on — the convex tips and the concave armpits alike. Exact
// outside; inside it is a bound, which a bevel or inner shadow reading the interior distance can show.
float sdShapeCross(float2 p, float2 b, float r)
{
    p = abs(p);
    p = (p.y > p.x) ? p.yx : p.xy;
    float2 q = p - b;
    float k = max(q.y, q.x);
    float2 w = (k > 0.0) ? q : float2(b.y - p.x, -k);
    return sign(k) * length(max(w, 0.0)) + r;
}

// Heart (iq), in its own units: two lobes on circles of radius sqrt(2)/4 about (+-0.25, 0.75) over a V
// closing at the origin, so it spans x in +-0.60355 and y in 0..1.10355 and the origin is its point.
float sdShapeHeart(float2 p)
{
    p.x = abs(p.x);
    if (p.y + p.x > 1.0) return length(p - float2(0.25, 0.75)) - 0.35355339;
    float2 v = p - 0.5 * max(p.x + p.y, 0.0);
    return sqrt(min(dot(p - float2(0.0, 1.0), p - float2(0.0, 1.0)), dot(v, v))) * sign(p.x - p.y);
}

// What a corner style takes out of the corner at C, whose two edges leave it along the unit directions e1
// and e2, for a radius r: the half-plane a bevel cuts, or the disk a scoop bites. Positive outside what is
// left, so a max() against the outline carves it. The bevel meets the edges where a fillet of the same
// radius would touch them, so switching style keeps the corner the same size.
float sdShapeCornerCarve(float2 p, float2 C, float2 e1, float2 e2, float r, float style)
{
    if (style > 1.5) return r - length(p - C);
    float2 sum = e1 + e2;
    float2 u = -sum / max(length(sum), 1e-5);
    float c = -dot(e1, u);
    float s = sqrt(max(1.0 - c * c, 1e-6));
    return dot(p - C, u) + r * c * c / s;
}

// Folds q into the wedge around the nearest corner of a regular n-gon whose first corner stands at `phase`
// radians, leaving that corner on the +x axis — one carve there is the carve at every corner.
float2 sdShapeFoldCorner(float2 q, float n, float phase)
{
    float step = 6.28318531 / n;
    float a = sdShapeMod(atan2(q.y, q.x) - phase + step * 0.5, step) - step * 0.5;
    return length(q) * float2(cos(a), sin(a));
}

// Carves every corner of a regular n-gon of circumradius R, given the outline's distance d at full size.
float sdShapeNgonCarved(float d, float2 q, float R, float n, float phase, float r, float style)
{
    float2 f = sdShapeFoldCorner(q, n, phase);
    float2 next = R * float2(cos(6.28318531 / n), sin(6.28318531 / n));
    float2 e = normalize(next - float2(R, 0.0));
    return max(d, sdShapeCornerCarve(f, float2(R, 0.0), e, float2(e.x, -e.y), r, style));
}

// Rounded rectangle (the default). b = half-extents; r = per-corner radii in (TR, BR, TL, BL)
// order; smoothing 0 = circular corners, 1 = squircle (superellipse ~L4).
float sdRoundedBox(float2 p, float2 b, float4 r, float smoothing)
{
    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    r.x  = (p.y > 0.0) ? r.x  : r.y;
    float2 q = abs(p) - b + r.x;
    float n = lerp(2.0, 4.0, saturate(smoothing));
    float2 qp = max(q, 0.0);
    float corner = pow(pow(qp.x, n) + pow(qp.y, n), 1.0 / n);
    return min(max(q.x, q.y), 0.0) + corner - r.x;
}

// Per-corner radii arrive as (TL, TR, BR, BL); sdRoundedBox wants (TR, BR, TL, BL). A bevel or a scoop
// leaves the rect at full size and carves its corner instead, each corner by its own radius; smoothing is
// the round style's own shape and plays no part in the other two.
float evalRoundedRect(float2 p, float2 b, float4 radii, float smoothing, float style)
{
    float4 r = float4(radii.y, radii.z, radii.x, radii.w);
    if (style < 0.5) return sdRoundedBox(p, b, r, smoothing);

    r.xy = (p.x > 0.0) ? r.xy : r.zw;
    float rc = (p.y > 0.0) ? r.x : r.y;
    float2 q = abs(p) - b;
    float box = min(max(q.x, q.y), 0.0) + length(max(q, 0.0));
    return max(box, sdShapeCornerCarve(abs(p), b, float2(-1.0, 0.0), float2(0.0, -1.0), rc, style));
}

// Stadium filling the box: semicircular caps on the longer axis.
float evalCapsule(float2 p, float2 b)
{
    if (b.x >= b.y) { float e = max(b.x - b.y, 0.0); return sdShapeSegment(p, float2(-e, 0.0), float2(e, 0.0)) - b.y; }
    float e = max(b.y - b.x, 0.0);
    return sdShapeSegment(p, float2(0.0, -e), float2(0.0, e)) - b.x;
}

// Parallelogram filling the box: t = tan of the skew, positive leaning the top to the right. The top edge's offset
// is held to the half-width so the horizontal edges never invert; past that the outline is a needle at the box's
// own slope. Rounding insets the outline exactly — horizontal edges by r, slanted ones by r over the cosine of
// their lean — and takes r off the distance, so every edge stays where it was.
float evalParallelogram(float2 p, float2 b, float t, float r, float style)
{
    float he = b.y;
    float sk = clamp(he * t, -b.x, b.x);
    float te = sk / max(he, 1e-5);
    if (style < 0.5)
    {
        float he2 = max(he - r, 0.0);
        float wi2 = max(b.x - abs(sk) - r * sqrt(1.0 + te * te), 0.0);
        return sdShapeParallelogram(p, wi2, he2, te * he2) - r;
    }

    float wi = max(b.x - abs(sk), 0.0);
    float d = sdShapeParallelogram(p, wi, he, sk);
    float2 q = (p.y < 0.0) ? -p : p;
    float2 e = normalize(float2(-sk, -he) + float2(0.0, -1e-5));
    d = max(d, sdShapeCornerCarve(q, float2(sk + wi, he), float2(-1.0, 0.0), e, r, style));
    return max(d, sdShapeCornerCarve(q, float2(sk - wi, he), float2(1.0, 0.0), e, r, style));
}

// Isosceles trapezoid filling the box: t = tan of the taper, positive narrowing the top, negative the bottom; the
// wide edge spans the box and the narrow one closes into a point once the lean asks for more than the half-width.
// Rounding insets the outline exactly, the way the parallelogram's does; where the inset slanted sides meet
// short of the inset top line (or bottom one) the inner outline is the triangle they close.
float evalTrapezoid(float2 p, float2 b, float t, float r, float style)
{
    float he = b.y;
    float lose = min(2.0 * he * abs(t), b.x);
    float r1 = b.x - ((t < 0.0) ? lose : 0.0);
    float r2 = b.x - ((t > 0.0) ? lose : 0.0);
    if (style > 0.5)
    {
        float d = sdShapeTrapezoid(p, r1, r2, he);
        float2 q = float2(abs(p.x), p.y);
        float2 down = normalize(float2(r1 - r2, -2.0 * he));
        float2 up = float2(-down.x, -down.y);
        float2 top = (r2 > 1e-4) ? float2(-1.0, 0.0) : float2(-down.x, down.y);
        float2 bottom = (r1 > 1e-4) ? float2(-1.0, 0.0) : float2(-up.x, up.y);
        d = max(d, sdShapeCornerCarve(q, float2(r2, he), top, down, r, style));
        return max(d, sdShapeCornerCarve(q, float2(r1, -he), bottom, up, r, style));
    }
    float mm = (r2 - r1) / max(2.0 * he, 1e-5);
    float sh = r * sqrt(1.0 + mm * mm);
    float yb = -he + r, yt = he - r;
    float xb = r1 + r * mm - sh;
    float xt = r1 + (2.0 * he - r) * mm - sh;
    float ya = -he + (sh - r1) / (mm + ((mm < 0.0) ? -1e-6 : 1e-6));
    if (xt < 0.0) { yt = ya; xt = 0.0; }
    if (xb < 0.0) { yb = ya; xb = 0.0; }
    float he2 = max(yt - yb, 0.0) * 0.5;
    return sdShapeTrapezoid(p - float2(0.0, (yb + yt) * 0.5), xb, xt, he2) - r;
}

// Rhombus filling the box. Rounding offsets its four edges inward, which moves each half-extent by the
// radius over the sine of that edge's lean, and takes the radius back off the distance.
float evalRhombus(float2 p, float2 b, float r, float style)
{
    if (style > 0.5)
    {
        float d = sdShapeRhombus(p, b);
        float2 q = abs(p);
        float2 ex = normalize(float2(-b.x, b.y));
        float2 ey = normalize(float2(b.x, -b.y));
        d = max(d, sdShapeCornerCarve(q, float2(b.x, 0.0), ex, float2(ex.x, -ex.y), r, style));
        return max(d, sdShapeCornerCarve(q, float2(0.0, b.y), ey, float2(-ey.x, ey.y), r, style));
    }
    float diag = length(b);
    float2 inner = max(b - r * float2(diag / max(b.y, 1e-5), diag / max(b.x, 1e-5)), 0.0);
    return sdShapeRhombus(p, inner) - r;
}

// Plus filling the box, sized by the inscribed square: b = (half-size, half-size * thickness). Rounding
// shrinks the arms and gives the radius back, which rounds the tips and fillets the armpits together.
float evalCross(float2 p, float2 b, float r, float style)
{
    r = min(r, b.y);
    if (style < 0.5) return sdShapeCross(p, max(b - r, 0.0), -r);
    float2 q = abs(p);
    q = (q.y > q.x) ? q.yx : q.xy;
    float d = sdShapeCross(p, b, 0.0);
    return max(d, sdShapeCornerCarve(q, b, float2(0.0, -1.0), float2(-1.0, 0.0), r, style));
}

// Size of an outline whose box half-extents are `box` per unit size, inside the bounds b. The provider sends b
// as the outline's own bounds, so this returns exactly the size it resolved; after a per-layer inset shrinks b
// the outline refits inside it, aspect kept — an anisotropic fit would stop the field being a true distance and
// warp every stroke, shadow and bevel that measures it. Ratios come from ShapeFit.cs, which owns them.
float sdShapeFit(float2 b, float2 box)
{
    return min(b.x / box.x, b.y / box.y);
}

// Reads vertex i (stored normalized to -1..1) from row rowV of the vertex atlas and scales it to the shape's
// half-extents, so the polygon is sized/positioned from halfSize exactly like the analytic kinds.
float2 sdShapeFetchVert(int i, float rowV, float2 halfSize)
{
    float u = (float((uint)i / 2u) + 0.5) / UI_SDF_VERT_TEX_WIDTH;
    float4 t = LIGHTSIDE_SAMPLE_SHAPE_VERTS(u, rowV);
    float2 nv = ((i & 1) == 0) ? t.xy : t.zw;
    return nv * halfSize;
}

// Exact SDF of a simple polygon (winding-number sign), vertices streamed from the atlas (stored normalized to
// -1..1 and scaled by halfSize here, so per-layer padding / insets shrink the polygon like the analytic kinds).
// After Inigo Quilez sdPolygon. rowV = atlas row; count <= UI_SDF_MAX_POLY_VERTS.
float sdShapePolygon(float2 p, float2 halfSize, float rowV, int count)
{
    if (count < 3) return length(p) - min(halfSize.x, halfSize.y);
    float2 v0 = sdShapeFetchVert(0, rowV, halfSize);
    float2 vj = sdShapeFetchVert(count - 1, rowV, halfSize);
    float d = dot(p - v0, p - v0);
    float s = 1.0;
    for (int i = 0; i < UI_SDF_MAX_POLY_VERTS; i++)
    {
        if (i >= count) break;
        float2 vi = (i == 0) ? v0 : sdShapeFetchVert(i, rowV, halfSize);
        float2 e = vj - vi;
        float2 w = p - vi;
        float2 bb = w - e * clamp(dot(w, e) / dot(e, e), 0.0, 1.0);
        d = min(d, dot(bb, bb));
        bool c1 = p.y >= vi.y;
        bool c2 = p.y <  vj.y;
        bool c3 = e.x * w.y > e.y * w.x;
        if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        vj = vi;
    }
    return s * sqrt(d);
}

// An open chain of vertices stroked to a band of half-width w: the distance to the nearest segment, less that
// width. Round caps and round joins fall out of the minimum itself; a flat cap squares the two outer ends off
// against their own segment (a local cut, so a curl passing near an end is left alone), and a square cap runs
// those ends half a width past instead. Vertices are stored normalized to the band's own extent, so halfSize
// arrives with the width already taken back off.
float sdShapePolyline(float2 p, float2 halfSize, float rowV, int count, float w, float cap)
{
    if (count < 2) return length(p - sdShapeFetchVert(0, rowV, halfSize)) - w;
    int last = count - 1;
    float2 vj = sdShapeFetchVert(0, rowV, halfSize);
    float d = 1e6;
    for (int i = 1; i < UI_SDF_MAX_POLY_VERTS; i++)
    {
        if (i > last) break;
        float2 vi = sdShapeFetchVert(i, rowV, halfSize);
        float2 e = vi - vj;
        float len = max(length(e), 1e-6);
        float2 n = e / len;
        float2 wv = p - vj;
        float a = dot(wv, n);
        float ext = (cap > 1.5) ? w : 0.0;
        float di = length(wv - n * clamp(a, (i == 1) ? -ext : 0.0,
                                        (i == last) ? len + ext : len)) - w;
        if (cap > 0.5 && cap < 1.5)
        {
            if (i == 1)    di = max(di, -a);
            if (i == last) di = max(di, a - len);
        }
        d = min(d, di);
        vj = vi;
    }
    return d;
}

// Primitive dispatch by ShapeKind (ids mirror ShapeKind.cs) — every kind except Composite, which loops over
// these and so cannot appear inside itself. Analytic kinds size themselves from the half-extents b, so
// Inset only has to shrink b. prm/aux are per-kind (InlineShapeProvider.Resolve): a kind whose box is not square
// carries that box's ratios in prm, and with them how far the box rides above the primitive's origin — for the
// star, whose four slots are spoken for, the rise follows from the box height because its top spike reaches the
// full radius.
//
// aux carries the corner radius for every kind that has corners (the rounded rect reads it as its own corner
// smoothing instead). Rounding a field is subtracting: the outline is built one radius smaller and the radius is
// taken off its distance, which grows every corner into an arc of exactly that radius and leaves the outline
// where it was. A cornered kind was given that room by its fit, so it comes back out of b; one built on a circle
// spends it inward, off the radius the circle was drawn at; the parallelogram and trapezoid, which fill the box
// outright, inset their own edges.
float evalShapePrimitiveSdf(float kind, float2 p, float2 b, float4 prm, float aux, float style)
{
    float rmin = min(b.x, b.y);
    if (kind < 0.5)  return evalRoundedRect(p, b, prm, aux, style);
    if (kind < 1.5)  return sdShapeCircle(p, rmin);
    if (kind < 2.5)  return sdShapeEllipse(p, b);
    if (kind < 3.5)  return evalCapsule(p, b);

    // A carving style leaves the outline at full size, so the fit reserved it no room to grow into and the
    // corner is cut out of the size it already has; the round style keeps building it smaller and dilating.
    float carve = (style > 0.5) ? 0.0 : aux;
    float2 bc = max(b - carve, 0.0);
    float rc = max(rmin - carve, 0.0);
    if (kind < 4.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float2 q = float2(p.x, p.y + prm.z * r);
        float d = sdShapeEquilateralTriangle(q, r) - carve;
        return (style > 0.5) ? sdShapeNgonCarved(d, q, 1.15470054 * r, 3.0, 1.57079633, aux, style) : d;
    }
    if (kind < 5.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float2 q = float2(p.x, p.y + prm.z * r);
        float d = sdShapePentagon(float2(q.x, -q.y), r) - carve;
        return (style > 0.5) ? sdShapeNgonCarved(d, q, 1.23606798 * r, 5.0, 1.57079633, aux, style) : d;
    }
    if (kind < 6.5)
    {
        float r = sdShapeFit(bc, prm.xy);
        float d = sdShapeHexagon(p, r) - carve;
        return (style > 0.5) ? sdShapeNgonCarved(d, p, 1.15470054 * r, 6.0, 0.0, aux, style) : d;
    }
    if (kind < 7.5)
    {
        float d = sdShapeOctagon(p, rc) - carve;
        return (style > 0.5) ? sdShapeNgonCarved(d, p, 1.08239220 * rmin, 8.0, 0.39269908, aux, style) : d;
    }
    if (kind < 8.5)  { float r = sdShapeFit(bc, prm.zw); return sdShapeStar(float2(p.x, p.y + (1.0 - prm.w) * r), r, prm.x, prm.y) - aux; }
    // A wedge of no aperture encloses nothing: report a distance no falloff can reach rather than the seam a
    // zero-angle sector would otherwise leave along its own axis.
    if (kind < 9.5)  return prm.x <= 0.0 ? 1e6 : sdShapePie(p, float2(sin(prm.x), cos(prm.x)), rc) - aux;
    // The arc band is a swept disk, so its caps are semicircles of its own half-thickness already: it turns on
    // no corner a rounding could work on, and aux is left out the way the ring leaves it out.
    if (kind < 10.5)
    {
        if (prm.x <= 0.0) return 1e6;
        float ra = rmin * (1.0 - prm.y * 0.5), rb = rmin * prm.y * 0.5;
        // A square cap runs the band half a thickness past each end, which on an arc is the angle that arc
        // length subtends at its own radius; the sweep is held to the full turn it would otherwise pass.
        float half = min(prm.x + ((style > 1.5) ? rb / max(ra, 1e-5) : 0.0), 3.14159265);
        float2 sc = float2(sin(half), cos(half));
        return (style > 0.5) ? sdShapeArcFlat(p, sc, ra, rb)
                             : sdShapeArc(p, float2(sin(prm.x), cos(prm.x)), ra, rb);
    }
    if (kind < 11.5) return sdShapeRing(p, rmin * (1.0 - prm.x * 0.5), rmin * prm.x * 0.5);
    if (kind < 12.5) { float h = clamp((prm.x * 2.0 - 1.0) * rmin - aux, -rc, rc); return sdShapeCutDisk(p, rc, h) - aux; }
    if (kind < 13.5) return evalParallelogram(p, b, prm.x, aux, style);
    if (kind < 14.5) return evalTrapezoid(p, b, prm.x, aux, style);
    if (kind < 15.5) return evalRhombus(p, b, aux, style);
    if (kind < 16.5) return evalCross(p, float2(rmin, rmin * prm.x), aux, style);
    if (kind < 17.5)
    {
        float r = sdShapeFit(b, prm.xy);
        return sdShapeHeart(float2(p.x, p.y + prm.z * r) / max(r, 1e-5)) * r;
    }
    if (kind < 18.5)
        return sdShapePolygon(p, bc, LightSideShapeRowV(prm.x), (int)(prm.y + 0.5)) - carve; // normalized verts scaled by b less the rounding
    return sdShapePolyline(p, max(b - prm.z, 0.0), LightSideShapeRowV(prm.x), (int)(prm.y + 0.5),
                           prm.z, style);
}

// Polynomial smooth minimum (iq): min(a, b) with the crease between them filleted over k. Exact min away from
// the crease, so a vanishing k is a plain union; callers keep k off zero.
float sdShapeSmoothUnion(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / k);
    return lerp(b, a, h) - k * h * (1.0 - h);
}

// The seam two elements leave where they meet, filleted over k: rounded by the polynomial smooth minimum,
// or cut straight across by the chamfer union (Mercury hg_sdf). Every boolean op is built from this one
// join, so a seam style reaches all four the same way.
float sdShapeSeam(float a, float b, float k, float chamfer)
{
    return (chamfer > 0.5) ? min(min(a, b), (a - k + b) * 0.70710678) : sdShapeSmoothUnion(a, b, k);
}

// A combination of primitives, streamed from the atlas at rowV: count elements of UI_SDF_COMPOSITE_STRIDE
// texels — t0 = (kind, op, blend, aux), t1 = (offset.xy of the pre-rotated frame, cos, sin), t2 = (half-extents.xy,
// morph progress, corner style + 4 * seam style), t3 = per-kind params. The first element lays the base and each next folds in by its op (ids
// mirror CompositeOp.cs): every boolean op runs through the smooth union with the element's blend as the fillet
// radius, while Morph moves the field so far toward the element's by its progress — the zero line of the mix is
// the in-between outline, and blend plays no part. Elements were encoded against the authored bounds b0; a layer
// inset hands in smaller b, so the whole combination rescales into it the way the polygon kind does — per axis,
// with the distance conservatively rescaled by the smaller ratio.
float evalCompositeSdf(float2 p, float2 b, float rowV, int count, float2 b0)
{
    float2 s = max(b, 1e-4) / max(b0, 1e-4);
    float2 q = p / s;

    float d = 1e6;
    for (int i = 0; i < UI_SDF_MAX_COMPOSITE_ELEMENTS; i++)
    {
        if (i >= count) break;
        float uBase = float(i * UI_SDF_COMPOSITE_STRIDE);
        float4 t0 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 0.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
        float4 t1 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 1.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
        float4 t2 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 2.5) / UI_SDF_VERT_TEX_WIDTH, rowV);
        float4 t3 = LIGHTSIDE_SAMPLE_SHAPE_VERTS((uBase + 3.5) / UI_SDF_VERT_TEX_WIDTH, rowV);

        float seam = floor(t2.w * 0.25);
        float2 cp = float2(t1.z * q.x + t1.w * q.y, -t1.w * q.x + t1.z * q.y) + t1.xy;
        float di = evalShapePrimitiveSdf(t0.x, cp, t2.xy, t3, t0.w, t2.w - seam * 4.0);

        float k = max(t0.z, 1e-4);
        if (i == 0)           d = di;
        else if (t0.y < 0.5)  d = sdShapeSeam(d, di, k, seam);
        else if (t0.y < 2.5)  d = -sdShapeSeam(-d, t0.y < 1.5 ? di : -di, k, seam);
        else if (t0.y < 3.5)
        {
            float u = sdShapeSeam(d, di, k, seam);
            float x = -sdShapeSeam(-d, -di, k, seam);
            d = -sdShapeSeam(-u, x, k, seam);
        }
        else                  d = lerp(d, di, t2.z);
    }
    return d * min(s.x, s.y);
}

// Full dispatch: the primitives, plus the composite that folds them.
float evalShapeSdf(float kind, float2 p, float2 b, float4 prm, float aux, float style)
{
    if (kind < 19.5) return evalShapePrimitiveSdf(kind, p, b, prm, aux, style);
    return evalCompositeSdf(p, b, LightSideShapeRowV(prm.x), (int)(prm.y + 0.5), prm.zw);
}

// Value noise in [0,1] for the Noise effect layer (smooth-interpolated hash grid).
float uiSdfHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float uiSdfValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = uiSdfHash(i);
    float b = uiSdfHash(i + float2(1.0, 0.0));
    float c = uiSdfHash(i + float2(0.0, 1.0));
    float d = uiSdfHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

#endif
