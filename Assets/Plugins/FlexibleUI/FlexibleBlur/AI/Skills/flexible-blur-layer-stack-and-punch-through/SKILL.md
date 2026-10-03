---
name: flexible-blur-layer-stack-and-punch-through
description: "Use this skill whenever the user wants layered or selective Flexible Blur composition — e.g. 'blur the blur underneath', 'keep text sharp between blur layers', 'stack UI blur with one camera', 'make a null blur punch through', 'why does the higher panel miss lower UI', or 'set up several renderer features'. Covers ordinary blur layers, layer/priority semantics, source capture order, null-blur punch-through, and the advanced single-camera Canvas-layer/Render Objects/FlexibleBlurFeature stack. Do NOT use for basic installation (see flexible-blur-setup-and-overview) or kernel tuning (see flexible-blur-tune-quality-and-performance)."
metadata:
  asset: "Flexible Blur"
  publisher: "Jeff Graw Assets"
  asset-version: "1.3.0"
  skill-version: "1.0.0"
  unity: "2022.3.62+"
  render-pipelines: "URP"
  category: "tools/gui"
  asset-store-url: "https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648"
  support-url: "https://discord.gg/PhqKsRhZ4D"
  last-verified: "2026-08-23"
---

# Stack and Punch Through Flexible Blur Layers

Compose blur results in controlled order, including UI-preserving single-camera stacks and zero-strength regions that reveal the captured source beneath later UI.

## When to use this skill

- "make the upper blur include the lower blur"
- "keep this label sharp between frosted panels"
- "stack three UI blur levels with one camera"
- "use a null blur to punch through"
- "why doesn't this blur see the UI underneath?"
- "which layer and priority should I use?"

Not for:

- Adding the first renderer feature or camera reference; see `flexible-blur-setup-and-overview`.
- Choosing algorithms, formats, or resolution; see `flexible-blur-tune-quality-and-performance`.
- Simple component-local alpha and source-image settings; see `flexible-blur-create-and-configure-effects`.

## Prerequisites

- Flexible Blur `1.3.0`, Unity `2022.3.62` or newer, URP, and a working basic blur.
- Confirm `FlexibleBlurFeature`, `UIBlur`, `BlurReferenceProvider`, and URP's `Render Objects` renderer feature are available.
- Identify the exact Canvas sorting/layer plan before adding features; renderer-feature order is part of the result.
- If Flexible Blur is absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648).

## Quick start

1. For ordinary blur-on-blur composition, put lower blur components on a lower `Layer` value and upper components on a higher value.
2. In FlexibleBlurFeature, keep `UIBlur Layers See Lower` enabled for UIBlur stacks.
3. Keep `Blurred Image Layers See Lower` enabled for BlurredImage/FlexibleImage stacks.
4. Keep `Blurred Images See UIBlurs` enabled when image-based blur should include UIBlur results.
5. Use `Priority` only to order blur regions within the same layer.

Expected result: higher ranked blur layers sample the results produced by lower blur layers, with the feature allocating additional layer textures/blits where required.

## Workflows

### Workflow: Create a null-blur punch-through region

**Goal:** Reveal the captured content below a later UI layer without adding blur.

**Steps:**

1. Choose the aperture component: UIBlur for a RectTransform region, BlurredImage for ordinary Image geometry, or an integrated Flexible Image for a procedural silhouette and alpha-faded edges.
2. Set its effective blur strength/alpha contribution to zero so expensive blur computation is skipped.
3. Keep the component active and correctly referenced; the final copy still returns the captured source for the region.
4. Within one blur family, keep the broad and null regions on the same Layer and give the null region a later Priority. A higher Layer with the relevant See Lower option samples the already-blurred lower-layer result instead of restoring the sharp source.
5. To cut a BlurredImage or Flexible Image through a broad UIBlur, disable `Blurred Images See UIBlurs` so image-based blur captures the sharp source, then draw the image-based aperture after the renderer feature. Screen Space Overlay provides this ordering directly; camera-only captures do not include its output.
6. Verify over actual overlapping UI after several rendered Game frames. A null blur cannot reveal content that was excluded from its source capture, and the previous result can remain visible for a frame after ordering or strength changes.

**Expected result:** the region shows the appropriate underlying captured image without additional blur computation. BlurredImage follows its Image geometry; Flexible Image can use corners, chamfer, concavity, squircles, cutouts, and color-grid alpha to shape or feather the reveal.

### Workflow: Preserve intervening UI with one camera

**Goal:** Stack blur panels while keeping selected labels or borders sharp between captures.

**Steps:**

1. Create distinct Unity layers for each UI stage, such as `UI`, `UI2`, and `UI3`, and assign the corresponding Canvas content.
2. In UniversalRendererData, remove those UI layers from the renderer's ordinary Transparent Layer Mask so they are not drawn in the normal transparent pass.
3. At one common event, normally `After Rendering Post Processing`, alternate renderer features in this order for each stage: `FlexibleBlurFeature`, then `Render Objects` for that stage's UI layer.
4. Configure each Render Objects feature with Render Queue `Transparent`, its one UI Layer Mask, and Depth override `Test: Always`.
5. Number FlexibleBlurFeature instances from zero in their list order. Point blur components in stage 0/1/2 at Feature # 0/1/2 as appropriate.
6. Add `BlurReferenceProvider` to each Canvas when many children share the same camera and feature number.
7. Enter Play mode and verify each later capture includes the UI drawn by preceding Render Objects stages.

**Expected result:** a single camera captures scene, blur, and selected UI in explicit stages, preserving intervening sharp UI between later blur layers.

Read [references/single-camera-stacking.md](references/single-camera-stacking.md) before editing production renderer data.

### Workflow: Diagnose a missing source layer

**Goal:** Determine why an upper blur omits lower blur or UI content.

**Steps:**

1. Confirm both components reference the same camera and intended FlexibleBlurFeature number.
2. Check whether the required feature toggle is enabled: UIBlur Layers See Lower, Blurred Images See UIBlurs, or Blurred Image Layers See Lower.
3. Compare component Layer values, then Priority values within equal layers.
4. Inspect UniversalRendererData feature order and the Render Pass Event.
5. Confirm the missing UI layer is drawn before the FlexibleBlurFeature that must capture it.
6. Check Canvas sorting and Unity Layer Mask assignments.

**Expected result:** the missing content is traced to a specific reference, feature toggle, rank, or capture-order mismatch.

## Verification

- Components resolve the intended `(camera, feature number)` pair.
- Distinct requested Layer values rank in the intended ascending order.
- Same-layer Priorities order the intended regions.
- Every staged UI layer is excluded from the normal transparent mask and included by exactly one matching Render Objects feature.
- Renderer features alternate in the intended capture/draw sequence at the same event.
- A null blur reveals only content present at its capture point.
- Game view remains stable over many frames without accumulation or blowout.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `UIBlurCommon.unrankedLayer` | `int` | Requests relative layer order; runtime maps distinct values to ranks |
| `UIBlurCommon.priority` | `int` | Orders blur regions within a layer |
| `UIBlurCommon.featureNumber` | `int` | Selects one FlexibleBlurFeature among those on the renderer |
| `BlurReferenceProvider` | component | Shares camera and feature number per Canvas |
| `FlexibleBlurFeature` layer toggles | renderer settings | Control cross-layer and cross-type source visibility |
| `FlexibleBlurFeature.GloballyPaused` | static bool | Pauses processing globally while retaining existing results until needed |

## Common issues

- **Upper blur misses lower blur** → The relevant See Lower toggle is off or layers are equal/reversed → Enable it and correct layer order.
- **Upper blur misses labels/borders** → Those Graphics were drawn after its capture → Use staged Render Objects ordering or another camera.
- **UI appears twice** → A staged layer remains in the normal Transparent Layer Mask → Remove it from that mask.
- **Staged UI disappears** → Render Objects uses the wrong layer/queue or depth test → Select the matching layer, Transparent queue, and Test Always.
- **Wrong blur stage is sampled** → Feature # does not match FlexibleBlurFeature order → Renumber the component/provider reference.
- **Punch-through remains blurred** → The null region is on a higher Layer and sees the broad blur below → Put both on the same Layer and give the null region a later Priority.
- **Image aperture over UIBlur remains blurred** → `Blurred Images See UIBlurs` is enabled or the Graphic draws before the feature → Disable that option and draw the BlurredImage/Flexible Image afterward, such as on Screen Space Overlay.
- **FI aperture becomes rectangular** → A broad image-based Graphic and later FI share one image-layer texture; the later rectangular capture region is already visible through the broad Graphic → Use a broad UIBlur with the separated capture/draw ordering above when FI must define the boundary.
- **Punch-through shows the wrong content** → Capture order or Canvas/layer ordering is wrong → Move the capture stage, not the kernel settings.

## Boundaries

- A blur layer can only sample content already rendered into its source at that point.
- Ordinary Flexible Blur layers can blur prior blur results, but they do not automatically preserve arbitrary sharp UI between stages.
- The single-camera workflow modifies renderer data and project layers; back up or version these assets and test every target renderer.
- Each additional layer/capture can require blits and render textures. Preserve only the stages the design needs.
- Multiple cameras remain a valid simpler alternative when their performance and stacking semantics are acceptable.
