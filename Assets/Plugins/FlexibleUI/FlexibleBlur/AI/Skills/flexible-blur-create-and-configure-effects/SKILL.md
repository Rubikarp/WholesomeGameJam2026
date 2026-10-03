---
name: flexible-blur-create-and-configure-effects
description: "Use this skill whenever the user wants to configure how a Flexible Blur component behaves — e.g. 'make this blur stronger', 'fade the source image', 'share blur settings', 'blur at zero canvas alpha', 'batch these panels', 'change layer or priority', or 'convert this Image to a blur'. Covers BlurredImage and UIBlur component controls, presets versus instance settings, alpha behavior, padding, batching, layers, priorities, and conversions. Do NOT use for renderer installation (see flexible-blur-setup-and-overview) or algorithm/performance tuning (see flexible-blur-tune-quality-and-performance)."
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
  last-verified: "2026-08-21"
---

# Create and Configure Flexible Blur Effects

Choose the appropriate blur component and tune its local contribution, sharing, ordering, and Image behavior without changing global renderer settings unnecessarily.

## When to use this skill

- "make this blur stronger"
- "fade the sprite but keep the blur"
- "keep this UIBlur active at zero Canvas alpha"
- "batch all these nearby frosted panels"
- "put this blur above the other blur"
- "convert this Image to BlurredImage"

Not for:

- First-time renderer-feature and camera setup; see `flexible-blur-setup-and-overview`.
- Algorithm, resolution, compute, or format tuning; see `flexible-blur-tune-quality-and-performance`.
- Advanced UI-preserving stacks and punch-through; see `flexible-blur-layer-stack-and-punch-through`.

## Prerequisites

- Flexible Blur `1.3.0`, Unity `2022.3.62` or newer, URP, and a configured FlexibleBlurFeature.
- Confirm `JeffGrawAssets.FlexibleUI.UIBlurCommon` resolves and the target blur has a valid camera/feature reference.
- If Flexible Blur is absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648).

## Quick start

1. Select a configured BlurredImage.
2. Set Blur Strength to the desired local multiplier.
3. Assign a BlurPreset for shared settings, or leave it empty and edit the instance Downscale and Blur sections.
4. Set Source Image Fade and Alpha Blend for the desired sprite/blur/transparency interaction.
5. Use Game view in Play mode to verify the effect.

Expected result: the blur appearance changes without losing the component's Image behavior or camera reference.

## Workflows

### Workflow: Convert existing UI

**Goal:** Replace an Image with BlurredImage while preserving ordinary Image properties.

**Steps:**

1. Open the existing Image component's context menu.
2. Choose `Convert to BlurredImage`.
3. Verify sprite, material, raycast target, maskable state, Image type, fill settings, aspect, and color.
4. Confirm the blur camera and feature number were populated or set them manually.
5. Convert back with `Convert to Image` when blur is no longer required.

**Expected result:** the GameObject contains one BlurredImage and retains the source Image's visible configuration.

### Workflow: Configure alpha behavior

**Goal:** Decide whether transparency fades blur strength, alpha-blends the result, or mixes both.

**Steps:**

1. Set `Source Image Fade` independently to control the source sprite overlay.
2. Set `Alpha Blend` to `0` for blur-strength fading with no conventional alpha blend.
3. Set it to `255` for conventional alpha blending without alpha-driven blur-strength reduction.
4. Use an intermediate byte value when non-occlusion and smooth blur fading both matter.
5. Test over UI and scene details, not a flat background.

**Expected result:** fading matches the intended relationship with UI behind the blur.

### Workflow: Configure UIBlur lifecycle and ordering

**Goal:** Make an invisible blur operation persist and order predictably.

**Steps:**

1. Use `Active at 0 Canvas Alpha` when UIBlur must continue processing even when inherited Canvas alpha reaches zero.
2. Assign `Layer` to group blur sources whose results should be composited together.
3. Assign `Priority` to order operations within the same layer.
4. Keep layer values consistent across components that should share a layer; the runtime ranks distinct values into contiguous layer textures.

**Expected result:** UIBlur remains active when requested and is processed in the intended layer/priority order.

### Workflow: Batch image-based blurs

**Goal:** Share computation among nearby BlurredImages with identical shared settings.

**Steps:**

1. Assign the same BlurPreset and Priority.
2. Enable `Batch With Similar` on compatible BlurredImages.
3. Keep the batch spatially compact; batching computes the combined region.
4. Use `Fill RenderTexture` only for unstable batch bounds, noise, or a verified compatibility need.
5. Add only enough `Additional Padding` to eliminate edge artifacts.

**Expected result:** compatible images share a blur region and avoid redundant computation where the combined bounds are favorable.

## Verification

- The component type matches the required rendering behavior.
- Blur Strength, Source Image Fade, and Alpha Blend have intentional values.
- Preset-mode components reference the intended BlurPreset; instance-mode components do not.
- Layer and Priority order matches the requested composition.
- Batched regions share preset/priority and are spatially sensible.
- The Console remains free of missing-reference and rendering errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `UIBlurCommon.blurStrength` | `float` | Multiplies local blur strength |
| `UIBlurCommon.blurPreset` | `BlurPreset` | Selects shared per-quality settings |
| `UIBlurCommon.blurInstanceSettings` | `BlurSettings` | Stores local settings when no preset is assigned |
| `UIBlurCommon.unrankedLayer` | `int` | Supplies the requested layer ordering value |
| `UIBlurCommon.priority` | `int` | Orders blur work within a layer |
| `BlurredImage` | component | Adds Image-specific fade, alpha, padding, and batching controls |
| `UIBlur.zeroCanvasAlphaActive` | `bool` | Keeps UIBlur active at zero inherited Canvas alpha |

## Common issues

- **Instance controls disappeared** → A BlurPreset is assigned → Edit the preset or clear it to use instance settings.
- **Alpha makes lower UI vanish** → Alpha Blend favors blur-strength fading → Move it toward conventional alpha blending.
- **A hidden Canvas stops blur work** → Active at 0 Canvas Alpha is disabled → Enable it on UIBlur when the hidden operation must persist.
- **Same-layer ordering is wrong** → Priorities are inverted or equal → Set explicit priorities and retest.
- **Batching costs more** → Combined bounds are much larger than individual regions → Split or disable that batch.
- **Black/unstable edges appear** → Sampling exceeds capture bounds → Increase Additional Padding modestly.

## Boundaries

- BlurredImage is a Graphic; UIBlur is not. They are not interchangeable when masks, sprites, raycasts, or visible Image output matter.
- Layer and Priority order blur processing; Canvas sorting and renderer-feature order still determine what source content exists to capture.
- Presets share BlurSettings, not camera references, layers, priorities, Image properties, or RectTransforms.
- Batching is an area-versus-call-count tradeoff, not an unconditional optimization.
