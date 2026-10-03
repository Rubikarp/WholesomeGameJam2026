---
name: flexible-blur-setup-and-overview
description: "Use this skill whenever the user is starting with Flexible Blur or the effect is missing — e.g. 'set up UI blur', 'add blur to my URP project', 'make a frosted panel', 'why is this an opaque rectangle', 'which camera do I assign', or 'add the renderer feature'. Covers installation checks, URP renderer-feature setup, first BlurredImage/UIBlur creation, camera and feature references, canvas modes, and basic diagnosis. Do NOT use for algorithm tuning (see flexible-blur-tune-quality-and-performance) or complex stacking (see flexible-blur-layer-stack-and-punch-through). When in doubt about a UI blur request, use this skill first."
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
  last-verified: "2026-08-22"
---

# Set Up Flexible Blur

Install the required URP renderer feature, create the appropriate uGUI blur component, and connect it to the camera output that should be sampled.

## When to use this skill

- "add UI blur to this URP project"
- "make a frosted glass panel"
- "create a blur region that isn't an Image"
- "why does the blur render as a solid rectangle?"
- "which camera and feature number should this use?"

Not for:

- Detailed blur algorithms, quality, formats, or performance; see `flexible-blur-tune-quality-and-performance`.
- Multiple blur layers, UI-preserving stacks, or punch-through; see `flexible-blur-layer-stack-and-punch-through`.
- Procedural Flexible Image integration; see `flexible-image-add-blur` when that skill is installed.

Skill index: use `flexible-blur-create-and-configure-effects` for component behavior, `flexible-blur-tune-quality-and-performance` for kernels and platform cost, and `flexible-blur-layer-stack-and-punch-through` for ordered captures and advanced composition.

## Prerequisites

- Flexible Blur `1.3.0`, Unity `2022.3.62` or newer, and the Universal Render Pipeline.
- Confirm `JeffGrawAssets.FlexibleUI.FlexibleBlurFeature`, `BlurredImage`, and `UIBlur` resolve.
- Confirm the active URP asset uses `UniversalRendererData`; Flexible Blur is a URP `ScriptableRendererFeature`.
- If the types are absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648).

## Quick start

1. Select the `UniversalRendererData` used by the camera.
2. In its Renderer Features list, click `Add Renderer Feature` and choose `Flexible Blur Feature`.
3. Leave `Render Pass Event` at `After Rendering Post Processing` for the initial setup.
4. Under a Canvas, choose `GameObject > UI (Canvas) > Blurred Image` in Unity 6.3+, or `GameObject > UI > Blurred Image` in older supported versions.
5. Set References From to `Self`, assign the camera whose output should be blurred, and set Feature # to `0` when this is the first FlexibleBlurFeature on that renderer.
6. Enter Play mode and inspect Game view.

Expected result: the BlurredImage displays a blurred sample of the selected camera inside its RectTransform.

## Workflows

### Workflow: Choose BlurredImage or UIBlur

**Goal:** Use the component whose drawing behavior matches the UI.

**Steps:**

1. Use `BlurredImage` when the blur needs ordinary uGUI Image rendering, sprite/fill behavior, tint, masking, or visible blur output in that Graphic.
2. Use `UIBlur` when a non-Graphic component should compute and place blur into the layer stack for later blur consumers.
3. Create UIBlur through `GameObject > UI (Canvas) > UIBlur` in Unity 6.3+, or `GameObject > UI > UIBlur` on older supported versions.
4. For either component, assign the captured camera and matching FlexibleBlurFeature number.

**Expected result:** the selected component participates in the feature's blur lists without a missing reference.

### Workflow: Share camera references from a Canvas

**Goal:** Avoid repeating camera and feature assignments across many blur components.

**Steps:**

1. Add `BlurReferenceProvider` to the Canvas.
2. Assign its Camera Reference and Feature Number.
3. Set each child blur's References From to `ReferenceProvider`.
4. Verify each blur resolves the provider on its Canvas.

**Expected result:** child blurs use the provider's `(camera, feature number)` pair and update when the provider changes.

### Workflow: Select the correct camera

**Goal:** Capture scene content without repeatedly capturing the UI that displays the blur.

**Steps:**

1. For Screen Space Overlay Canvas, reference the camera rendering the scene beneath the overlay.
2. For Screen Space Camera and camera stacks, prefer the camera immediately below the Canvas camera when the renderer feature runs after transparents; this avoids accumulation and blowout.
3. Confirm that camera uses the renderer data containing the selected FlexibleBlurFeature.
4. For a Screen Space Camera Canvas, confirm the Canvas camera's culling mask includes every layer used by that Canvas hierarchy. A zero mask hides the UI regardless of valid blur references.
5. When multiple FlexibleBlurFeatures exist, count only FlexibleBlurFeature entries from zero and assign the matching Feature #.

**Expected result:** the source is stable frame to frame and does not recursively brighten or blur the UI.

## Verification

- The active camera's UniversalRendererData contains at least one enabled FlexibleBlurFeature.
- Each blur resolves a non-null camera and correct Feature #, directly or through BlurReferenceProvider.
- A Screen Space Camera Canvas is assigned to a camera that renders its layer.
- The component appears in Game view during Play mode.
- The source does not accumulate or blow out over successive frames.
- Converting an Image preserves its ordinary Image properties.
- The Console contains no renderer-feature, shader, compute, or render-graph errors.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FlexibleBlurFeature` | `ScriptableRendererFeature` | Captures, computes, layers, and supplies blur textures |
| `BlurredImage` | `Image` subclass | Displays image-based blur with ordinary Image behavior |
| `UIBlur` | component | Computes a UI blur layer without being a Graphic |
| `UIBlurCommon` | serializable class | Stores references, strength, layer, priority, and settings |
| `BlurReferenceProvider` | Canvas component | Shares camera and feature number with child blurs |
| `FlexibleBlurMenuItems.TrySetBlurCamera(...)` | Editor method | Auto-populates common camera-stack cases |

## Common issues

- **The control is an opaque rectangle** → The blur texture was not supplied, commonly due to a camera/feature mismatch → Verify camera renderer data and Feature #.
- **Blur is absent in Scene view** → The effect depends on the render feature and camera execution → Validate primarily in Play mode Game view.
- **The image grows brighter or blur accumulates** → The captured camera already includes the displaying UI → Reference the camera below it or use the advanced single-camera layer workflow.
- **ReferenceProvider mode does nothing** → The nearest Canvas lacks an enabled BlurReferenceProvider → Add/configure it or switch to Self.
- **The component menu is missing** → Flexible Blur or URP code is not compiling → Resolve Console errors and confirm package files are imported.
- **The scene shows its world backdrop but none of its UI** → The Canvas camera excludes the UI layer → Correct the culling mask before changing blur settings.

## Boundaries

- Flexible Blur `1.3.0` supports URP, not Built-in or HDRP.
- It requires a renderer feature; adding a component alone is insufficient.
- Scene-view behavior is not the authoritative test for a camera render feature.
- Multiple cameras can solve layering but have a real rendering cost; use the single-camera workflow when appropriate.
