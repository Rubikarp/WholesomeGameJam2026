---
name: flexible-image-create-shapes-and-cutouts
description: "Use this skill whenever the user wants to change Flexible Image geometry — e.g. 'make a squircle', 'turn this into a chevron', 'round only two corners', 'hollow out this shape', 'cut a circle from the center', or 'rotate the procedural shape without rotating its transform'. Covers corners, softness, local geometry transforms, skew/collapse, stroke, quick shapes, and simple or SDF cutouts. Do NOT use for color effects (see flexible-image-create-color-effects), multiple independently placed quads (see flexible-image-compose-multiple-quads), or animation (see flexible-image-animate-interactive-ui)."
metadata:
  asset: "Flexible Image"
  publisher: "Jeff Graw Assets"
  asset-version: "3.0.0"
  skill-version: "1.0.0"
  unity: "2022.3.62+"
  render-pipelines: "Built-in, URP, HDRP"
  category: "tools/gui"
  asset-store-url: "https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652"
  support-url: "https://discord.gg/PhqKsRhZ4D"
  last-verified: "2026-08-24"
---

# Create Flexible Image Shapes and Cutouts

Construct rounded, concave, skewed, hollow, collapsed, or cut-out uGUI shapes by modifying procedural quad geometry independently of the GameObject's RectTransform.

## When to use this skill

- "make this button a capsule"
- "create a triangle or chevron without a sprite"
- "put a rounded rectangular hole through this panel"
- "make an outlined ring"
- "offset and rotate the shape inside its rect"

Not for:

- Gradients, patterns, or color grids; see `flexible-image-create-color-effects`.
- Multiple separately anchored shapes; see `flexible-image-compose-multiple-quads`.
- Animated shape transitions; see `flexible-image-animate-interactive-ui` after constructing the default shape.

## Prerequisites

- Flexible Image `3.0.0`, Unity `2022.3.62+`, and uGUI.
- Confirm `JeffGrawAssets.FlexibleUI.FlexibleImage` exists on the target.
- Confirm Skew, Stroke, Cutout, and the required cutout subfeature are globally enabled before using them.
- If the component is absent, use `flexible-image-setup-and-overview` first.

## Quick start

1. Select a Flexible Image and expand `Shape`.
2. Expand `Corners`.
3. Set all Chamfer values to the same positive value and leave Concavity at `0` for rounded convex corners.
4. Set `Softness` around `.5–1` for antialiased procedural edges and leave Feather Mode `Bidirectional` unless the fade must stay on one side of the nominal boundary.
5. Resize the RectTransform and verify the corners behave as intended.

Expected result: the Flexible Image renders as a softened rounded rectangle without requiring a rounded sprite.

## Workflows

### Workflow: Design corners and squircles

**Goal:** Build rounded, flat-chamfered, concave, or squircle corners.

**Steps:**

1. Expand `Shape > Corners`.
2. Enable `Normalize` when each corner must affect at most half of the shape.
3. Set Chamfer independently for NW, NE, SW, and SE, or use the `-5`/`+5` quick controls.
4. Leave `Squircle` disabled and use Concavity from convex toward concave, or enable `Squircle` and set Smoothing within its reduced range.
5. Use the `-0.5`/`+0.5` quick controls for concavity/smoothing. Controls stop at their valid ranges.

**Expected result:** each corner has the requested size and profile without exceeding the Inspector's valid range.

### Workflow: Transform or collapse the procedural shape

**Goal:** Reposition, resize, rotate, skew, or collapse the rendered shape while leaving the RectTransform available for layout.

**Steps:**

1. Use `Offset`, `Size Modifier`, and `Rotation` for local procedural placement.
2. Enable aspect correction or `Fit Original Rect` only when the corresponding automatic compensation is wanted.
3. Enable the Skew module for collapsed-edge controls.
4. Choose the collapsed edge deliberately; its default is `Top`. `Left` or `Right` usually gives UI elements useful horizontal directionality; choose between them from the intended eye flow and surrounding context. Use `Top` or `Bottom` only when the intended silhouette specifically calls for collapsing a horizontal edge, not merely because the composition is vertical. Then set the relative or absolute collapse amount and position.
5. Treat Collapse `Position` as part of the silhouette: `.5` centers an ordinary taper, while `0` or `1` is needed for a directional parallelogram, arrow tab, or chevron. `Mirror` alone produces symmetric arrow-like forms; `Mirror` plus `Parallelogram` produces a concave chevron.
6. For common shapes, use the Shape quick-actions button and select a rounded, sharp, squircle, trapezoid, parallelogram, chevron, or triangle preset, then refine values.

**Expected result:** the procedural mesh forms the requested geometry inside its original layout rect.

### Workflow: Join antialiased shapes without hairline seams

**Goal:** Keep separately rendered shapes visually flush while retaining antialiasing on their exposed edges.

**Steps:**

1. Make both mating edges square, align their RectTransform boundaries, and use one continuous Flexible Image/SDF silhouette instead when the join can be represented as one shape.
2. For separate touching shapes, give both mating edges the same `Softness` around `.5–1`, use Feather Mode `Bidirectional`, and begin with no geometric overlap. Their matched fades meet across the shared boundary, producing continuous antialiased coverage without a harsh overlap band or a gap. Do not use `Inwards` on both edges: each shape fades before the boundary and can reveal a dark line even when the geometry is exact.
3. Inspect the rendered join at 1:1 at every target resolution. Canvas `Pixel Perfect` can correct some fractional screen-pixel placement introduced by Canvas scaling, but it does not correct inward feathering or excessive Softness and must also be tested during resize and motion.
4. If pixel phase still creates a hairline, add only the smallest overlap that removes it; probe small fractional canvas-unit steps such as `.25` and then `.5`. Stop there rather than applying a blanket one-pixel or softness-sized overlap.
5. Use `Outwards` only when expansion beyond the RectTransform is safe. It can close a seam, but later siblings may overpaint neighboring colors, text, masks, or translucent effects. Use Softness `0` only for an intentionally hard internal edge, not as a global antialiasing fix.

**Expected result:** shared boundaries contain no dark backdrop pixels, while exposed rounded, diagonal, and curved edges remain antialiased.

### Workflow: Create a hollow stroke

**Goal:** Remove the interior after a chosen distance to create a ring or stroked shape.

**Steps:**

1. Enable the Stroke module from the Shape selector.
2. Raise Stroke far enough to reach the intended hollow center. For a narrow ring with `Center` origin on a roughly square shape, begin near half the shorter rect dimension, then reduce it to thicken the ring. A merely small positive value leaves most of the shape filled.
3. Choose the stroke origin appropriate to the perimeter, center, or outline relationship.
4. Combine with `Add Interior Outline` only when an additional outline based on the stroke position is intended.
5. Verify advanced raycasting if the hollow center should not receive pointer events.

**Expected result:** the shape renders as a hollow procedural region with the requested edge placement.

### Workflow: Add a simple or SDF cutout

**Goal:** Remove or isolate a rectangular/edge-defined area or a fully shaped SDF area.

**Steps:**

1. Enable the Cutout module and select `Simple` or `SDF`.
2. For Simple, choose the AND/OR rule, enable the relevant left/right/top/bottom edges, and set their amounts. Use `Invert` or `Outline Only` when required.
3. For SDF, choose behavior, mirroring, diagonal mode, inversion, and placement.
4. Do not plan a Procedural Gradient on the same quad: enabling SDF Cutout makes the entire Procedural Gradient section unavailable. Use Simple cutout or another quad/component when both effects are needed.
5. In `Anchors` placement, set position/size, Anchor Min/Max, Pivot, and Rotation like a compact RectTransform. In `Rect` placement, retain the independent Relative/Absolute modes for position and size.
6. Leave `Ignore Expanded Outline` alone unless the cutout should be measured against the inner pre-expanded rect.
7. Configure SDF chamfer and concavity/squircle exactly like a second shape.
8. For code or editor-command authoring, import `JeffGrawAssets.FlexibleUI`, call `EnableCutout()` before setting cutout properties, set `Cutout` to `QuadData.CutoutType.SDF`, then use the public `SDFCutout...` properties. Module-backed setters do nothing when their module is absent.

**Expected result:** the requested region is cut from, isolated within, or applied only to the outline of the source shape.

Read [references/shape-inspector-map.md](references/shape-inspector-map.md) before combining collapsed edges, outlines, cutouts, and raycasting.

## Verification

- Required Shape modules exist and their global features are enabled.
- Corner values stay within the ranges enforced by the Inspector.
- Procedural offset/rotation does not unexpectedly alter child layout.
- SDF anchor placement remains stable when the component rect changes.
- `Ignore Expanded Outline` selects the intended inner versus expanded reference rect.
- Touching shapes are inspected at 1:1 at every target resolution: joins contain no dark hairline and exposed procedural edges remain antialiased.
- Advanced raycasting includes Stroke, Cutout, Offset, Rotation, or Size flags when those visual changes must affect hit testing.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `CornerChamfer`, `CornerConcavity` | `Vector4` properties on `FlexibleImage` | Set the four primary-quad corner values |
| `Offset`, `RawSizeModifier`, `Rotation` | properties on `FlexibleImage` | Transform the procedural primary quad |
| `Stroke`, `Softness`, `SoftnessFeatherMode` | properties on `FlexibleImage` | Control hollowing, edge softness, and whether the fade lies inwards, outwards, or across the nominal boundary |
| `Cutout`, `CutoutEnabled` | properties on `FlexibleImage` | Control simple cutout distances and enabled edges |
| `EnableSkew()`, `EnableStroke()`, `EnableCutout()` | methods on `QuadData` | Add modular shape data |
| `QuadData.CutoutType.SDF` | enum value | Selects SDF rather than Simple cutout after the module exists |
| `QuadData.SDFCutoutBehaviour` | enum | Chooses `MinShape`, `OutlineAndInterior`, or `OutlineOnly` |
| `SDFCutoutUsesAnchors`, `SDFCutoutAnchorMin`, `SDFCutoutAnchorMax`, `SDFCutoutPivot` | properties on `FlexibleImage` | Configure RectTransform-like SDF placement |
| `SDFCutoutPosition`, `SDFCutoutSize`, `SDFCutoutRotation` | properties on `FlexibleImage` | Supply anchor offsets/size delta or Rect placement values |
| `SDFCutoutChamfer`, `SDFCutoutConcavity` | `Vector4` properties on `QuadData` | Set NW, NE, SW, and SE values for the cutout shape |
| `ConvertSDFCutoutPositioning(...)` | method on `QuadData` | Converts Rect/anchor placement while preserving the represented cutout |

## Common issues

- **A module selector shows unavailable data** → Serialized module data exists but its global feature is disabled → Enable the feature or explicitly remove the unavailable module.
- **A code-authored module property has no effect** → Its module data was never added → Call the matching `Enable...()` method before setting module-backed properties.
- **Normalized corners are unavailable** → Mirrored collapse changes the corner relationship → Disable mirror or accept independent chamfer behavior.
- **Procedural Gradient disappears when SDF Cutout is selected** → SDF Cutout makes the entire gradient section unavailable on that quad → Use Simple cutout or another quad/component.
- **A supposed ring still looks filled** → Stroke is small relative to the shape radius → Increase it toward half the shorter rect dimension, then tune back for the desired ring thickness.
- **A dark hairline appears between touching shapes** → Both mating edges feather inwards, Softness is excessive, or Canvas scaling places the join between physical pixels → Use `Bidirectional` around `.5–1`, verify aligned square joins at target resolutions, then add only the smallest overlap still required.
- **Cutout moves when outline expands** → Its reference includes the expanded outline → Enable `Ignore Expanded Outline` only if inner-rect anchoring is desired.
- **Visual hole still blocks input** → Standard raycasting uses the RectTransform → Enable Advanced raycasting and include Cutout/Stroke as appropriate.

## Boundaries

- These controls alter Flexible Image's generated mesh/shader data, not its RectTransform or source sprite.
- SDF Cutout and the entire Procedural Gradient section cannot be used together on the same quad.
- Shape quick actions overwrite relevant shape settings; use Undo if they replace intentional values.
- Do not manipulate private module references directly. Add or remove modules through `QuadData` methods or the Inspector selector.
