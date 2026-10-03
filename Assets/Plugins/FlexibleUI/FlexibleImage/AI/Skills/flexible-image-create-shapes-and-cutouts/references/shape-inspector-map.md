# Flexible Image shape Inspector map

Use this map to choose and combine geometry values. Optional modules require both serialized module data and the corresponding global feature/subfeature.

## Corners

The four fields are ordered NW, NE, SW, SE in the Inspector and stored as a `Vector4`.

| Control | Range or choices | Guidance |
|---|---|---|
| Normalize | Boolean | Constrains each chamfer to at most half the image dimension, preventing oversized neighboring corners from consuming the shape. It is unavailable with mirrored collapse because that geometry changes the corner relationship. |
| Chamfer | `0` to `4095.9375` canvas units | `0` is square. Increasing values round or cut farther into the corner. On a short rectangle, roughly half the height produces capsule ends when Normalize is enabled. Without normalization, oversized values can intentionally collapse the shape into a lens. |
| Chamfer `-5` / `+5` | Buttons | Adjust all corners without leaving the valid range. A corner at a fractional maximum is rounded to the usable step before decrementing; the applicable button disables when every corner is already at its limit. |
| Squircle | Boolean | Repurposes Concavity as Smoothing. Use for a superellipse-like profile rather than a circular corner arc. |
| Concavity | `0` to `1.9921875` | `0` is convex/rounded, `1` is a flat straight chamfer, and values approaching `2` become increasingly concave. These anchors are more useful than treating the field as arbitrary strength. |
| Smoothing | `0` to `1` in Squircle mode | `0` retains the rounder profile; larger values flatten/square the sides while keeping a soft continuous corner. |
| Concavity/Smoothing `-0.5` / `+0.5` | Buttons | Adjust all corners and stop at the active range. |

When a non-parallelogram, non-mirrored edge is fully collapsed, the two collapsed corners become one visible apex. The Inspector then exposes the collapsed-corner Chamfer and Concavity/Smoothing values for that apex.

## Edge feathering

| Control | Range or choices | Guidance |
|---|---|---|
| Feather Mode | Inwards, Outwards, Bidirectional | Inwards keeps the fade inside but two inward-feathered mating edges can expose a dark seam. Bidirectional straddles the boundary; matching its Softness on two flush shapes lets their fades meet smoothly without a gap or the heavy overlap of two Outwards fades. Outwards expands beyond the nominal boundary; use it cautiously because later siblings can overpaint neighboring colors, text, masks, or translucent effects. |
| Softness | `0` to `255.9375` canvas units | `0` is hard. About `.5–1` is effective antialiasing; larger values are deliberate feather/blur widths. Tune after final rect size because it is distance-based. |

### Adjacent softened seams

Square and align both mating edges, then give them matching Bidirectional Softness around `.5–1` and start with zero geometric overlap. The two fades meet across the boundary, preserving smooth antialiasing without exposing the backdrop or producing a hard doubled-coverage band. Inspect the rendered result at 1:1 at every target resolution: exact Canvas coordinates can still land at a fractional physical-pixel phase after Canvas scaling. Canvas `Pixel Perfect` can regularize some of those screen-space cases, but it does not repair two Inwards fades or Softness large enough to become a visible feather.

If a target resolution still shows a hairline, overlap only until it disappears; `.25` then `.5` canvas units are useful starting probes, not a fixed recipe. Larger overlaps let draw order and alpha blending define the join and can create a different color or opacity seam. Prefer one continuous Flexible Image/SDF silhouette where practical. Softness `0` is appropriate for a deliberately hard axis-aligned internal edge, not as a general substitute for antialiasing exposed rounded or diagonal edges.

## Local procedural transform

These controls alter the generated shape inside its RectTransform. They do not change layout dimensions or child transforms.

| Control | Range or choices | Guidance |
|---|---|---|
| Offset X/Y | Canvas units | Moves the procedural quad inside the layout rect. Use instead of RectTransform movement when layout must remain stable or the value should animate as Flexible Image data. |
| Size Modifier X/Y | Canvas units | Positive values grow and negative values shrink the procedural shape. Avoid shrinking through zero. |
| Size Modifier Aspect Correction | Boolean | When X and Y modifiers match on a non-square rect, scales the shorter dimension's modifier by the rect aspect so the overall aspect does not drift. It is compensation for equal modifiers, not a general keep-aspect toggle. |
| Rotation | Degrees | Rotates the procedural quad around its center while the RectTransform remains unrotated. |
| Fit Original Rect | Boolean | Scales a rotated shape down so its rotated bounds fit inside the original unrotated rect. Leave off when overhang is intentional. |
| UV Rect X/Y/W/H | X/Y unrestricted fields; W/H clamped `0` to `2`; default `(0,0,1,1)` | Selects texture UVs like a viewport rect. It also changes the procedural boundary, so cropping to a sub-rect can alter which corners and edges are represented. |

Procedural Rotation affects only this quad, while RectTransform or parent rotation also rotates layout and children. Choose the control that matches the intended behavior.

## Skew module

Skew collapses one edge toward a point or toward a translated opposite edge. It is the main tool for trapezoids, triangles, parallelograms, and chevrons.

| Control | Range or choices | Guidance |
|---|---|---|
| Collapsed Edge | Top, Bottom, Left, Right | Chooses the edge being collapsed; the serialized default is Top. Top/Bottom act across width and Left/Right across height. Left or Right usually gives UI elements useful horizontal directionality; choose between them from the intended eye flow and surrounding context. Use Top or Bottom only when the intended silhouette specifically calls for collapsing a horizontal edge, not merely because the composition is vertical. |
| Collapse Amount mode | Relative or Absolute | Relative is proportional and responds to rect size. Absolute is measured in canvas units. |
| Collapse Amount | Relative `0–1`; Absolute `≥0` canvas units | `0` is unchanged. Relative `1` fully collapses the chosen edge, normally producing a triangle. Intermediate values produce trapezoids. Absolute is converted against the relevant current dimension and saturates at full collapse. |
| Position | `0` to `1` | Point/offset along the chosen edge. `.5` centers an ordinary taper; `0` and `1` move the apex or translated edge to either end. |
| Parallelogram | Boolean | Applies an inverse-position collapse to the opposite edge, producing a parallel slant rather than a single tapered edge. |
| Mirror | Boolean | Mirrors collapse. With ordinary collapse this produces chevrons and related symmetric forms; with Parallelogram it can create a concave chevron. It also disables Normalize. |
| Collapsed Corner Chamfer/Concavity | Same concepts as Corners | Available for a fully collapsed three-sided shape; rounds, flattens, or notches the apex. |

When the design specifically calls for an offset backing plate or compact shadow under a skewed/chamfered surface, duplicate the same FI geometry settings, give the back copy an opaque subordinate color, offset it by only a few canvas units, and keep it behind all foreground text. This preserves the silhouette; an unrelated rectangle or uniformly expanded outline does not. Reserve it for deliberate depth hierarchy rather than repeating it under every surface.

## Stroke module

Stroke hollows the shape after a distance in canvas units. Stroke `0` is off.

| Control | Choices | Guidance |
|---|---|---|
| Stroke | `≥0` canvas units | Increase until the desired interior is removed. Very large values can consume the shape. |
| Origin | Center, Perimeter, Outline | Center places the stroke relative to the shape center; Perimeter measures inward from the shape edge; Outline relates the hollowing to outline placement. Compare origins when an Outline is also present because the visible ring position changes substantially. |

`Outline > Add Interior Outline` adds an outline at the Stroke boundary. If the hollow center must not receive input, Advanced raycasting must include Stroke.

## Cutout module

`Outline Only` applies the cutout to the outline while leaving the interior fill intact. `Invert` turns the cutout definition into the only visible region. Both cutout families support these operations.

### Simple cutout

Simple cutout combines four axis-aligned edge distances. The vector order is Left, Right, Top, Bottom.

| Control | Range or choices | Guidance |
|---|---|---|
| Rule | OR, AND | OR removes the union of enabled edge bands. AND removes only their overlap. Opposite bands in AND mode do nothing until they overlap. |
| Edge enabled | Left, Right, Top, Bottom | Disabled edges do not participate in the rule even if their stored distance is nonzero. |
| Edge amount | `0` to `1023.5` canvas units | Width/depth removed from that edge. Establish enabled edges first, then increase amounts. |
| Outline Only | Boolean | Leaves the fill intact and removes only affected outline pixels. Requires a visible Outline to matter. |
| Invert | Boolean | Keeps the rule's selected region rather than removing it. |

To retain one crisp horizontal outline edge on a single Flexible Image, use `OR`, `Outline Only`, and `Invert`; enable both Top and Bottom while leaving Left and Right disabled. With rect height `H` and an ordinary non-expanded outline width `W`, begin at Top `0`, Bottom `H - 2W` to keep the top edge, or Top `H - 2W`, Bottom `0` to keep the bottom edge. Both opposing edges participate in the inverted range even though only one outline edge remains. Retune the depth at `1:1` when outline expansion, softness, or Canvas scaling changes the apparent line.

`CutoutFill(RectTransform, CutoutFillOrigin, percent)` uses Simple cutout as a progress/fill helper. It supports each edge, horizontal/vertical center or perimeter fills, both-axis variants, crosses, and four corners. `percent >= 1` clears the cutout. The helper is limited by Simple cutout's maximum size and can be awkward with expanded outlines plus Massage Chamfer.

### SDF cutout behavior

SDF Cutout is a second rounded/concave procedural shape. Selecting it makes the entire Procedural Gradient section unavailable on that quad.

| Control | Choices | Guidance |
|---|---|---|
| Behaviour | Min Shape, Outline And Interior, Outline Only | Min Shape combines source and cutout distance so the cutout can contribute an inner edge/outline. Outline And Interior cuts both regions without the same minimum-shape outline treatment. Outline Only confines the SDF operation to outline behavior. Verify with a visible outline because the distinction is edge-oriented. |
| Mirror | None, Horizontal, Vertical, Both | Duplicates the cutout across the selected axes. Horizontal gives left/right pairs, Vertical top/bottom, Both four-way. |
| Diagonal | Boolean, available with mirroring | Reinterprets the mirrored pairing diagonally, useful for opposing-corner or lobed cuts. |
| Invert | Boolean | Keeps only the SDF cutout shape instead of removing it. |
| Ignore Expanded Outline | Boolean | When Expand Outwards is active, On measures placement from the inner pre-outline rect; Off includes the expanded outer rect. Do not change it incidentally during placement conversion. |
| Rotation | Degrees | Rotates the cutout around its own center/pivot. |

### SDF Rect placement

Rect placement overloads Position and Size directly and retains independent Relative/Absolute choices.

| Control | Range or choices | Guidance |
|---|---|---|
| Position mode | Relative or Absolute | Relative is normalized to the chosen reference rect; Absolute is canvas units from its center convention. Switching mode converts the current value. |
| Pos X/Y | Relative `-1` to `2`; Absolute `-8192` to `8191.5` | Relative `.5,.5` centers the cutout. Values outside `0–1` deliberately move it beyond the rect. |
| Size mode | Relative or Absolute | Relative scales with the reference rect; Absolute remains fixed in canvas units. |
| Size X/Y | Relative `0` to `2`; Absolute `0` to `16383.5` | Relative `1,1` matches the reference size. Zero collapses an axis. |

### SDF Anchor placement

Anchor placement follows RectTransform semantics:

| Control | Guidance |
|---|---|
| Anchor Min/Max | Normalized reference-rect points. Equal values create a fixed anchor; different values stretch the cutout with the rect. |
| Position | Anchored position in canvas units. |
| Size | Size delta added to the anchor span, not an absolute final size when anchors are split. |
| Pivot | Normalized point inside the cutout used for position and rotation. `.5,.5` is centered. |
| Rotation | Degrees around Pivot. |

Changing Placement converts the represented cutout rather than merely flipping a flag. Rect-to-Anchors first resolves Relative values into Absolute geometry; Anchors-to-Rect selects Absolute modes and writes equivalent Position/Size. A preset uses the preset editor's reference parent size because no live RectTransform exists.

### SDF cutout corners

| Control | Range | Guidance |
|---|---|---|
| Normalize | Boolean | Limits cutout chamfer against cutout size. |
| Chamfer | `0` to `1023.75` canvas units | Same corner order and visual meaning as main Chamfer, with a smaller packed range. |
| Squircle | Boolean | Repurposes Concavity as Smoothing. |
| Concavity | `0` to `1.9921875` | `0` convex, `1` flat, near `2` concave. |
| Smoothing | `0` to `1` | Squircle smoothing strength. |

## Shape quick actions

The horizontal square/triangle/circle button writes coordinated shape values for common rounded/sharp corners, squircles, trapezoids, parallelograms, chevrons, and triangles. Treat it as a starting preset: it overwrites the relevant current values, so use Undo if it replaces authored work.

## Raycast Target and padding

| Control | Choices | Guidance |
|---|---|---|
| Raycast Target | Disabled, Standard, Advanced | Disabled ignores pointer events. Standard behaves like uGUI Image rect raycasting. Advanced conforms to selected procedural geometry flags. |
| Raycast Flags | Size, Chamfer And Collapse, Stroke, Cutout, Ignore Outline, Offset, Rotation, UV Rect | Enable only visual operations that should change the hit region. Defaults are Size, Chamfer And Collapse, and UV Rect. See the flag table below. |
| Base Padding | Left, Bottom, Right, Top | Expands or contracts the ordinary raycast region in canvas units on all platforms. |
| Extra IOS/Android Padding | Left, Bottom, Right, Top | Adds platform-specific touch padding on top of Base Padding. Use for finger targets without enlarging desktop input. |

Advanced raycasting is safest on leaf controls. Due to Unity's parent `ICanvasRaycastFilter` behavior, a parent Flexible Image returning false can interfere with child ordinary Image components outside the parent's valid region; child Flexible Images have a workaround path that ordinary Images do not.

| Advanced flag | Guidance |
|---|---|
| Size | Applies Size Modifier to the hit bounds. Disable when visual growth/shrinkage should not change the layout-sized target. |
| Chamfer And Collapse | Rejects rounded/concave corners and collapsed/skewed-away geometry. Disable for a rectangular interaction area around a decorative shape. |
| Stroke | Rejects the hollow center produced by Stroke so input can pass through it. |
| Cutout | Rejects Simple or SDF cutout holes. Without it, the removed pixels can still receive input. |
| Ignore Outline | Rejects pixels belonging only to a visible outline, leaving the filled procedural region interactive. |
| Offset | Moves the hit shape with procedural Offset. |
| Rotation | Rotates the hit shape with procedural Rotation and accounts for Fit Original Rect. |
| UV Rect | Applies UV Rect cropping to the hit shape. This is enabled by default because UV Rect also changes the procedural boundary. |
