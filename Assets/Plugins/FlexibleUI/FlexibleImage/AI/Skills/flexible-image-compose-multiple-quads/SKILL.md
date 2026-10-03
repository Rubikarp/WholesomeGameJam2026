---
name: flexible-image-compose-multiple-quads
description: "Use this skill when one Flexible Image should draw several already-intended layers or shapes that share a RectTransform and Graphic context — e.g. 'build this badge from one component', 'add another quad', 'make a layered card without child Images', 'anchor the icon inside the panel', or 'which quad controls raycasts'. Covers Multiple and Preset data modes, quad ordering, anchors, pivots, primary-quad behavior, and per-quad flags. Do NOT use for designing an individual quad's colors (see flexible-image-create-color-effects) or shape (see flexible-image-create-shapes-and-cutouts)."
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

# Compose Multiple Quads with Flexible Image

Draw a layered procedural control from one Flexible Image by arranging named quads within the component's RectTransform.

## When to use this skill

- "make this badge from one Flexible Image"
- "combine these passive layers into one Flexible Image"
- "anchor a smaller quad to the right edge"
- "share this multi-quad design as a preset"
- "which quad handles input?"

Not for:

- Styling colors, gradients, outlines, or patterns; see `flexible-image-create-color-effects`.
- Styling corners, strokes, skew, or cutouts; see `flexible-image-create-shapes-and-cutouts`.
- Reusing a single color palette without sharing geometry; see `flexible-image-use-presets-and-export`.

## Prerequisites

- Flexible Image `3.0.0`, Unity `2022.3.62` or newer, and any supported render pipeline.
- Confirm `JeffGrawAssets.FlexibleUI.QuadDataContainer` and `JeffGrawAssets.FlexibleUI.FlexibleImage` resolve.
- Confirm the target is under a Canvas and uses a `RectTransform`.
- Every optional section or family used by any quad must have its corresponding global parent feature and subfeature enabled. Adding module data to a quad does not enable shader support.
- If the types are absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652).

## Quick start

1. Select a Flexible Image and set `Data Mode` to `Multiple`.
2. Use the quad list's add control to create a second quad.
3. Give each quad a descriptive name and leave both enabled.
4. Select the new quad, set its anchors, anchored position, size, pivot, and rotation.
5. Reorder the list to establish visual ordering.
6. Select the quad that should define input/raycast behavior and click `Set Primary`.

Expected result: one `FlexibleImage` draws both quads; the selected primary quad defines input/raycasting.

## Workflows

### Workflow: Build an anchored multi-quad control

**Goal:** Combine visual layers that already belong to one control without changing its design or behavior.

**Steps:**

1. Confirm the layers share one owning RectTransform and Graphic-level material, mask/sort, and input context.
2. Set `Data Mode` to `Multiple`, then name the existing and added quads for their actual visual roles.
3. Set each quad's anchors, pivot, position, size, and rotation within the owner's rect.
4. Configure each quad's color and shape independently.
5. Reorder the quads for the intended overlap and set the intended hit region as Primary.

**Expected result:** resizing the Flexible Image preserves the intended relative placement and one Canvas Graphic performs the drawing.

### Workflow: Share the complete composition

**Goal:** Move instance quad data into a reusable `QuadDataPreset`.

**Steps:**

1. With the composed component selected, change `Data Mode` to `Preset`.
2. Click `New` beside `Data Preset`; choose an asset path when prompted.
3. Confirm the created preset contains every instance quad, name, order, enabled state, primary index, and procedural value.
4. Assign the preset to other Flexible Images that should share the composition.
5. Use `Delete Instance Data` only after deciding the component will remain in Preset mode; switching away later recreates instance data.

**Expected result:** all components referencing the preset update when its quad collection changes, while their RectTransforms remain independent.

### Workflow: Use per-quad flags

**Goal:** Let an auxiliary quad ignore source imagery or image-type mesh behavior.

**Steps:**

1. Select the intended quad and open `Flags`.
2. Enable `DisableSprite` when that quad should not draw the component's Source Image. With Flexible Blur integration, this also disables blur on that quad.
3. Enable `ForceSimpleMesh` when the quad must behave as Image Type Simple even if the component is Sliced, Tiled, or Filled.
4. Verify the remaining quads still use the component-wide sprite and Image Type as intended.

**Expected result:** the flag affects only the selected quad.

Read [references/quad-inspector-map.md](references/quad-inspector-map.md) for exact anchor/pivot meanings, data-mode tradeoffs, list controls, and primary-quad behavior.

## Verification

- `FlexibleImage.DataMode` is `Multiple` or `Preset`, not `Single`.
- `ActiveQuadDataContainer.Count` equals the intended number of visible and intentionally disabled quads.
- Quad names are meaningful and ordering matches the desired overlap.
- Exactly one valid `primaryQuadIdx` identifies the intended input quad.
- Resizing the component preserves anchored relationships.
- The Console has no serialization or mesh-generation errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FlexibleImage.DataMode` | `FlexibleImage.QuadDataMode` | Selects Single, Multiple, or Preset data |
| `FlexibleImage.ActiveQuadDataContainer` | `QuadDataContainer` | Gets the currently rendered collection |
| `QuadDataContainer.AddQuadData()` | method | Adds a named quad and dirties dependent images |
| `QuadDataContainer.RemoveQuadData(int)` | method | Removes a non-required quad by index |
| `QuadDataContainer.GetQuadData(string)` | method | Finds a quad by its serialized name |
| `QuadDataPreset.quadDataContainer` | field | Stores a reusable multi-quad composition |

## Common issues

- **Only one quad is available** → Data Mode is Single → Switch to Multiple, or assign a QuadDataPreset in Preset mode.
- **The wrong region receives input** → The decorative quad is Primary → Select the intended hit-region quad and click `Set Primary`.
- **A quad moves incorrectly when resized** → Anchors and pivot do not express the intended relationship → Configure them as with a RectTransform before tuning position and size.
- **Preset edits affect many controls** → The data is shared by design → Use Multiple mode or create another preset when controls must diverge.
- **A sprite appears on an auxiliary quad** → It inherits the component's source image → Enable that quad's `DisableSprite` flag.
- **One quad's module is unavailable or invisible** → Its global parent feature or selected subfeature is disabled → Enable the required global switches or choose an available treatment.

## Boundaries

- Quads share the Flexible Image component, RectTransform, sprite, material, CanvasRenderer, and high-level Graphic state.
- Use separate child Graphics when elements require independent RectTransforms, masks, raycast targets, materials or sprites, or GameObject/Graphic lifecycles. QuadData itself supports per-quad enablement and procedural animation.
- Preset mode intentionally centralizes serialized quad data; editing the preset changes every referencing component.
- The primary quad is the input/raycast reference, not necessarily the first or visually topmost quad.
