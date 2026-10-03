---
name: flexible-image-add-blur
description: "Use this skill whenever the user wants Flexible Blur on a Flexible Image — e.g. 'make this procedural panel frosted glass', 'blur behind this Flexible Image', 'why is the Blur checkbox missing', 'fade the sprite over the blur', or 'batch similar procedural blurs'. Covers the optional Flexible Blur integration, camera/feature references, strength, source fade, alpha blending, layers, priorities, presets, padding, and batching. Do NOT use for standalone BlurredImage or UIBlur setup (see flexible-blur-setup-and-overview). When blur must retain Flexible Image's procedural shape, use this skill."
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
  last-verified: "2026-08-23"
---

# Add Flexible Blur to Flexible Image

Use the optional Flexible Blur integration so a procedural Flexible Image can display a URP camera blur inside its generated shape.

## When to use this skill

- "make this Flexible Image a frosted-glass panel"
- "blur behind this rounded procedural card"
- "why isn't Blur shown in the Flexible Image Inspector?"
- "batch these similar Flexible Image blurs"
- "fade the source sprite separately from the blur"

Not for:

- Standalone Flexible Blur installation and renderer-feature setup; see `flexible-blur-setup-and-overview`.
- Complex layer stacks and punch-through behavior; see `flexible-blur-layer-stack-and-punch-through`.
- Static shape or color styling; see the corresponding Flexible Image skills.

## Prerequisites

- Flexible Image `3.0.0` plus compatible Flexible Blur integration, Unity `2022.3.62` or newer, and URP.
- Confirm both `JeffGrawAssets.FlexibleUI.FlexibleImage` and `JeffGrawAssets.FlexibleUI.FlexibleBlurFeature` resolve.
- Any optional Flexible Image section used with the blur still requires its corresponding global parent feature and selected subfeature; enabling blur or adding module data does not enable shader support.
- Confirm the active `UniversalRendererData` has a `FlexibleBlurFeature` and note its zero-based number among FlexibleBlurFeature entries.
- If Flexible Blur is absent, direct the user to the [Flexible Blur listing](https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648). If Flexible Image is absent, use its [listing](https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652).

## Quick start

1. Complete the renderer-feature setup in `flexible-blur-setup-and-overview`.
2. Select the Flexible Image and enable `Blur` in its Inspector.
3. Set References From to `Self`, assign the camera whose output should be captured, and set the matching Feature #.
4. Set Blur Strength and choose a BlurPreset or configure instance settings.
5. Adjust Source Image Fade and Alpha Blend while viewing the Game view.

Expected result: the camera image behind the procedural shape is blurred while Flexible Image color, outline, cutout, and other enabled effects remain available.

## Workflows

### Workflow: Balance blur fade and transparency

**Goal:** Fade a frosted panel without unexpectedly exposing or hiding UI beneath it.

**Steps:**

1. Use `Blur Strength` for the most visually stable blur fade.
2. Use `Source Image Fade` to control how much of the assigned sprite is drawn over the blur.
3. Set `Alpha Blend` to `0` when Flexible Image alpha should adjust blur strength rather than alpha-blend the result.
4. Set `Alpha Blend` to `255` when alpha should blend normally without changing blur strength.
5. Use an intermediate value when both high-quality fading and partial non-occlusion matter.

**Expected result:** component alpha produces the intended compromise between blur-strength fading and conventional transparency.

### Workflow: Share and batch similar blurs

**Goal:** Reduce repeated work for nearby Flexible Images with the same effect.

**Steps:**

1. Create a `BlurPreset` with `Create > FlexibleUI > BlurPreset` and assign it to each compatible Flexible Image.
2. Give compatible images the same Priority and enable `Batch With Similar`.
3. Batch nearby regions; avoid batching a few small regions on opposite screen edges because the combined bounding area may process many more pixels.
4. Enable `Fill RenderTexture` only when a changing batch perimeter creates noise or a compatibility case requires the whole render texture.
5. Increase `Additional Padding` only to correct edge sampling or fast-motion VR artifacts.

**Expected result:** compatible nearby blur areas share work without changing their procedural silhouettes.

### Workflow: Create a shaped punch-through

**Goal:** Reveal a sharp or less-blurred region through a broader blur using Flexible Image's procedural silhouette.

**Steps:**

1. Use UIBlur for the broad blur, then enable Blur on the Flexible Image that defines the aperture.
2. In FlexibleBlurFeature, disable `Blurred Images See UIBlurs` so the Flexible Image's image layer captures the sharp source instead of the broad UIBlur result.
3. Draw the Flexible Image after the renderer feature. Screen Space Overlay provides this ordering directly; a camera RenderTexture does not include Overlay output, so verify in the actual Game view or with `ScreenCapture`.
4. Set the Flexible Image's Blur Strength to `0` for a null blur, or a lower nonzero value for a reduced-blur region, and keep Source Image Fade at `0` when only the captured image should show.
5. Shape the aperture with corners, chamfer, concavity, squircles, strokes, or cutouts as required.
6. For a feathered aperture, set Alpha Blend to `255` and vary the primary color-grid alpha. Keep the first stored color's alpha above zero so the component is not culled; if the visible fade must begin at zero, reverse or rotate the grid instead. For example, stored alpha `[1, 0.5, 0]` with a `180°` grid rotation produces a visible `0→1` fade.

**Expected result:** the Flexible Image reveals the captured source through its procedural shape, with optional alpha-feathered transitions.

### Workflow: Convert while preserving blur settings

**Goal:** Switch between integrated and standalone blur components.

**Steps:**

1. Open the component context menu.
2. Choose `Convert to BlurredImage` when standard Image behavior is sufficient, or `Convert to UIBlur` for a non-Graphic blur operation.
3. To return, open the BlurredImage or UIBlur context menu and choose `Convert to FlexibleImage`.
4. Verify the camera, feature number, preset/instance settings, layer, and priority after conversion.

**Expected result:** common blur settings and ordinary Image properties, where applicable, are copied to the replacement component.

## Verification

- Both integration types compile and the Flexible Image Inspector shows Blur controls.
- The assigned camera and Feature # identify an actual FlexibleBlurFeature instance.
- For Screen Space Camera, the Canvas camera's culling mask includes the layers used by the Canvas hierarchy.
- The effect is visible in Game view without accumulation or blowout.
- The intended BlurPreset or instance settings are active.
- Batched elements share preset and priority and occupy a sensibly compact combined area.
- A shaped punch-through samples the sharp image layer with `Blurred Images See UIBlurs` disabled and draws after the broad UIBlur.
- Alpha-feathered punch-through uses Alpha Blend `255`, keeps the first stored primary color visible, and reveals only inside the intended FI coverage.
- The Console has no missing-camera, missing-feature, shader, or render-graph errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FlexibleImage.BlurEnabled` | `bool` property | Enables integrated blur rendering |
| `FlexibleImage.Common` | `UIBlurCommon` | Holds camera, feature, strength, layer, priority, and settings |
| `FlexibleImage.AlphaBlend` | `byte` property | Blends alpha behavior between strength fading and alpha blending |
| `FlexibleImage.SourceImageFade` | `byte` property | Controls source-sprite contribution over blur |
| `FlexibleImage.tryBatchWithSimilar` | `bool` field | Allows preset/priority-compatible image blur batching |
| `FlexibleImage.additionalBlurPadding` | `float` field | Expands the captured blur region |

## Common issues

- **Blur controls are absent** → Flexible Blur integration is not installed or compiling → Install the compatible Flexible Blur package/integration and resolve compile errors.
- **Nothing is blurred** → Camera or Feature # does not match the renderer feature → Assign the captured camera and correct feature number.
- **The panel becomes blown out** → The selected camera/render-pass relationship captures the UI repeatedly → Follow the camera guidance in `flexible-blur-setup-and-overview`.
- **Batching is slower** → The combined bounding box covers too much screen area → Disable batching for distant blur regions.
- **One quad should not blur** → The quad shares the component's integrated blur → Enable that quad's `DisableSprite` flag.
- **Edges show artifacts** → Capture bounds are too tight for sampling/motion → Increase Additional Padding modestly.
- **Only world geometry is visible** → The Screen Space Camera Canvas is assigned to a camera that does not render its layer → Correct the camera culling mask before diagnosing the blur itself.
- **Punch-through remains blurred** → The FI image layer captured the broad UIBlur → Disable `Blurred Images See UIBlurs` and draw the FI after the renderer feature.
- **Punch-through is rectangular instead of procedural** → A broad image-based Graphic exposes the later candidate's rectangular shared-RT capture region → Use UIBlur for the broad blur when FI must define the aperture boundary.
- **Alpha feather disappears completely** → The first stored primary color has zero alpha, so FI is culled before blur capture → Keep cell zero nonzero and rotate or reverse the grid to place the transparent end visually.

## Boundaries

- Flexible Blur integration is optional and URP-only even though Flexible Image itself supports Built-in, URP, and HDRP.
- Integrated blur is component-wide but `DisableSprite` can exclude individual quads.
- Flexible Image is not the place to configure the renderer feature's platform formats, resolution limits, or layer-compositing policy; use the Flexible Blur skills.
- Batching trades fewer calls for a potentially larger processed region and is not universally faster.
