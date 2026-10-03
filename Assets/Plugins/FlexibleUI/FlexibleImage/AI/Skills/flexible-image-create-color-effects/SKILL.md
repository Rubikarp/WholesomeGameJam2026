---
name: flexible-image-create-color-effects
description: "Use this skill whenever the user wants to color or shade Flexible Image UI — e.g. 'make this panel a gradient', 'add a glow outline', 'give these buttons a shared screen-space pattern', 'make a noisy hologram', or 'use a color grid'. Covers primary colors, outlines, five procedural gradient families, five pattern families, color grids, blending, presets, and vertex-color mesh controls. Do NOT use for corner geometry or cutouts (see flexible-image-create-shapes-and-cutouts), multiple quads (see flexible-image-compose-multiple-quads), or blur (see flexible-image-add-blur)."
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

# Create Flexible Image Color Effects

Build procedural color treatments directly in the Flexible Image Inspector while preserving the asset's shared-material batching model.

## When to use this skill

- "make this card fade from blue to transparent"
- "add an outward glow without another Image"
- "put a moving grid pattern across these panels"
- "run animated diagonal stripes around the outline of the selected control"
- "make the pointer reveal a radial highlight"
- "use different colors at each corner"

Not for:

- Shape geometry, strokes, skew, or cutouts; see `flexible-image-create-shapes-and-cutouts`.
- Multiple positioned shapes in one component; see `flexible-image-compose-multiple-quads`.
- Blur configuration; see `flexible-image-add-blur`.

## Prerequisites

- Flexible Image `3.0.0`, Unity `2022.3.62+`, and uGUI.
- Confirm the target has `JeffGrawAssets.FlexibleUI.FlexibleImage`.
- Confirm required modules and subfeatures are enabled under `Tools > FlexibleUI > Flexible Image Global Settings`.
- If the type or menu is missing, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652).

## Quick start

1. Select a Flexible Image and expand `Color`.
2. Expand `Primary Color` and set the first color cell.
3. Use the Color module selector to enable `Outline` and `Procedural Gradient` if absent.
4. Set the Outline width and color.
5. In Procedural Gradient, choose a gradient family, set its region and colors, then tune the displayed controls.

Expected result: one Flexible Image displays a primary fill, outline, and procedural gradient without assigning a unique material.

## Workflows

### Workflow: Build primary color and outline layers

**Goal:** Create a conventional fill, border, glow, or shadow treatment.

**Steps:**

1. Set the Primary Color grid dimensions and colors. Leave dimensions at `1 x 1` for a flat color.
2. Adjust `Fade` when visibility must change independently of color alpha.
3. Enable the Outline module from the Color selector.
4. Set outline grid colors and `Width`.
5. Choose `Alpha Is Blend` when outline alpha should mix with the accumulated lower color treatment instead of making the outline transparent.
6. Use `Expand Outwards`, `Fade To Perimeter`, `Massage Chamfer`, and `Add Interior Outline` only for the intended edge treatment.

**Expected result:** the fill and outline combine predictably at both opaque and partially transparent edges.

### Workflow: Add a procedural gradient

**Goal:** Layer an SDF, angle, radial, conical, or noise gradient over the primary color.

**Steps:**

1. Enable `Procedural Gradient` in the Color module selector.
2. Confirm the same quad is not using SDF Cutout. SDF Cutout makes the entire Procedural Gradient section unavailable; use Simple cutout or another quad/component when both are needed.
3. Set its color grid and choose whether it affects `Interior`, `Outline`, or both.
4. Choose `SDF`, `Angle`, `Radial`, `Conical`, or `Noise`.
5. Configure only the controls displayed for that family: distance/reach for SDF, direction for Angle, position/size/strength for Radial, curvature/tail for Conical, or seed/scale/edge/strength for Noise.
6. Use `Invert`, `Alpha Is Blend`, `Screen Space`, `Pointer Adjusts Pos`, or pointer reveal only when the corresponding globally enabled option is available.

**Expected result:** the selected procedural gradient affects the requested region and responds correctly to local, screen, or pointer-relative coordinates.

### Workflow: Add subtle, smooth surface shading

**Goal:** Add quiet depth, edge light, or a soft focal lift without visible banding or decorative texture.

**Steps:**

1. Keep Primary Color and Procedural Gradient at `1 x 1` so the procedural mask, rather than per-vertex interpolation, controls the ramp.
2. Enable `Alpha Is Blend`, choose a related secondary hue, and begin with secondary alpha around `.08–.25`. This mixes a restrained tint into the opaque lower color stack instead of making the panel translucent.
3. Use Angle for a directional wash only after moving its zero-distance center band completely outside the visible rect. A centered band, or a rotated band that still crosses a corner, produces a visible seam. Use one broad flank of the falloff and keep Size toward the upper end of its range.
4. Use inverted Radial for a soft off-edge light. Move its center outside the rect, use a large Size, and tune Strength after placement. As a concrete starting point on a wide panel, Pos `.08/1.05`, Size `.95/.9`, Strength `.45`, and secondary alpha `.2` produce a restrained corner light.
5. Treat centered Radial as an intentional spotlight/bullseye and centered Angle as a band effect, not as default subtle surface shading. Likewise, a non-inverted centered Radial is appropriate only when a visible vignette is wanted.
6. Judge the result at its final rendered size. Angle and Radial receive automatic low-amplitude interleaved dithering in the shader; there is no dither toggle.

**Expected result:** the surface has a smooth, low-contrast lighting treatment that supports hierarchy without reading as an obvious effect.

### Workflow: Use a multidimensional color grid

**Goal:** Create vertex-interpolated colors rather than a flat cell.

**Steps:**

1. Increase the relevant grid's X or Y dimensions.
2. Set each displayed color cell.
3. Open `Advanced` to adjust horizontal/vertical wrap modes, offset, rotation, and scale.
4. Right-click a cell to copy or paste a row, column, or the full grid when those operations are applicable.
5. If interpolation needs more geometry, increase `Mesh Subdivisions` conservatively and test `Topology`.

**Expected result:** the Flexible Image mesh interpolates the grid as designed; repeated or transformed grids use the selected wrap behavior.

### Workflow: Configure color effects through automation

**Goal:** Produce the same visible result through public API calls that the Inspector would create.

**Steps:**

1. Start from a visible Flexible Image with an intentional RectTransform and primary color.
2. Call `EnableOutline()`, `EnableGradient()`, or `EnablePattern()` on the target QuadData before assigning that module's values.
3. Use public properties and cell setters. Set color-grid dimensions before assigning cells; avoid reflection and private serialized fields.
4. Give each enabled effect intentional settings that produce the requested visual. A subtle gradient may be low contrast, but it must still visibly improve the rendered surface.
5. For requested motion, route to `flexible-image-animate-interactive-ui`; animate Pattern Speed only when a moving repeated motif suits the design.
6. Render at the target Game-view resolution and confirm the claimed effect changes visible pixels. Verify motion from Play mode or two captures separated in time. Module existence and non-default serialized values do not establish that the effect is visible.

**Expected result:** the rendered image visibly demonstrates each claimed color effect while continuing to use the shared Flexible Image material.

Read [references/color-inspector-map.md](references/color-inspector-map.md) for the complete shared color-grid controls or when a requested effect requires a particular gradient, pattern, or outline treatment.

## Verification

- Required Color modules are present and globally available.
- Each claimed color effect is visible in a rendered Game view, not merely present in serialized data.
- The selected gradient or pattern family matches the requested effect.
- Subtle shading uses a smooth Angle or Radial mask; patterns appear only where repetition has a deliberate visual purpose.
- Region flags affect Interior, Outline, or both as intended.
- Screen-space effects line up across components at runtime.
- Pointer-driven effects update only when their global option is enabled.
- Claimed moving patterns visibly change over time in Play mode.
- High mesh subdivisions are justified and the Console is clean.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `PrimaryQuadData` | `QuadData` | Owns the primary quad's modular color configuration |
| `PrimaryColors` | `Color[]` property on `FlexibleImage` | Replaces the primary color grid and dirties the component |
| `OutlineColors` | `Color[]` property on `FlexibleImage` | Replaces outline colors when the module exists |
| `ProceduralGradientColors` | `Color[]` property on `FlexibleImage` | Replaces procedural-gradient colors when the module exists |
| `PatternColors` | `Color[]` property on `FlexibleImage` | Replaces pattern colors when the module exists |
| `EnableOutline()`, `EnableGradient()`, `EnablePattern()` | methods on `QuadData` | Adds the corresponding modular data |
| `ColorPreset` | `JeffGrawAssets.FlexibleUI.ColorPreset` | Shares four base colors across designs |

## Common issues

- **A module is unavailable** → Its global feature is disabled → Enable it globally and wait for shader import, or choose an enabled effect.
- **Procedural Gradient is unavailable while SDF Cutout is active** → The two sections cannot coexist on one quad → Use a Simple cutout or move the gradient to another quad or Flexible Image.
- **Only one flat color appears** → Grid dimensions remain `1 x 1` or all cells match → Increase dimensions and set distinct cells.
- **A low-contrast ramp bands or looks segmented** → A Primary Color grid is providing the ramp → Use a `1 x 1` primary and a low-alpha blended Angle or Radial gradient to use the procedural-gradient dither path.
- **The interface looks busy or generically textured** → Pattern was used as default decoration → Remove it from neutral surfaces and reserve it for intentional scan lines, cells, repeated symbols, energy, or authored motifs.
- **A moving outline pattern fills the control** → Pattern Interior is still enabled → Disable Interior and enable Outline for the Pattern region.
- **A moving outline reads as a solid or frantic border** → Density, Line Thickness, Speed, or contrast is too strong for the rendered size → Reduce the values together and reserve the stronger treatment for the selected state.
- **The outline pattern does not move** → Static Offset is enabled or Speed is zero → Disable Static Offset, set a nonzero Speed, and verify two runtime frames.
- **A screen-space effect does not align or changes color at element boundaries** → Components use different procedural settings or section color grids → Match the effect settings and the corresponding grid dimensions, cells, transforms, wrapping, and preset mix, then enable the global option.
- **Sprite pattern is wrong or absent** → The sprite is packed, the Image type is Sliced/Tiled, or pattern color is opaque black → Use an unpacked sprite, prefer Simple/Filled, and inspect the pattern color.
- **Pattern controls disappear with blur** → Packed vertex data is reserved by blur integration → Follow the Inspector's unavailable warning instead of forcing the serialized field.
- **The module exists but the image still looks flat** → Its colors, alpha, strength, reach, density, or region leave the default appearance effectively unchanged → Tune the displayed controls and verify the rendered result.

## Boundaries

- Flexible Image color effects are shader-defined uGUI effects; they do not modify source textures.
- Do not create unique materials for routine color variations; component data is designed to batch through the shared shader.
- Enabling a globally disabled feature is a project-wide shader-source change and requires the user's awareness.
- SDF Cutout makes the entire Procedural Gradient section unavailable on the same quad, not only the SDF gradient family.
- Color grids increase vertex-data work. Use only the dimensions and subdivisions the visual needs.
