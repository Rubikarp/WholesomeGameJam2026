---
name: flexible-image-use-presets-and-export
description: "Use this skill whenever the user wants to reuse or rasterize Flexible Image work — e.g. 'make this palette reusable', 'share this shape across buttons', 'create a quad preset', 'reduce prefab YAML', 'bake this UI to a PNG', or 'export the procedural image'. Covers ColorPreset, QuadDataPreset, Preset data mode, instance-data cleanup, and texture export. Do NOT use for authoring the underlying color effect (see flexible-image-create-color-effects) or multi-quad layout itself (see flexible-image-compose-multiple-quads). When reuse or export is the goal, use this skill."
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

# Reuse Presets and Export Flexible Images

Share palettes or complete procedural compositions across components, keep serialized instance data small, and bake a Flexible Image to a texture asset.

## When to use this skill

- "reuse these colors across every button"
- "turn this multi-quad design into a preset"
- "reduce duplicated Flexible Image data in prefabs"
- "bake this procedural panel to a PNG"
- "export the UI as a sprite"

Not for:

- Creating a color effect from scratch; see `flexible-image-create-color-effects`.
- Arranging the quads in a composition; see `flexible-image-compose-multiple-quads`.
- Migrating v2 serialized data; see `flexible-image-setup-and-overview`.

## Prerequisites

- Flexible Image `3.0.0`, Unity `2022.3.62` or newer, and any supported render pipeline.
- Confirm `JeffGrawAssets.FlexibleUI.ColorPreset` and `JeffGrawAssets.FlexibleUI.QuadDataPreset` resolve.
- Modules and selected families stored in a QuadDataPreset still require their corresponding global parent features and subfeatures. Assigning a preset does not enable shader support.
- Texture export is Editor-only and requires a selected, enabled Flexible Image with valid mesh data.
- If the types are absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652).

## Quick start

1. In the Project window, choose `Create > FlexibleUI > ColorPreset`.
2. Configure the preset's primary, outline, procedural-gradient, and pattern colors.
3. Assign it to a Flexible Image's `Color Preset` field.
4. Adjust each section's Preset Mix to blend preset colors with the quad's local colors.

Expected result: changing the ColorPreset updates every referencing quad while geometry remains local.

## Workflows

### Workflow: Create a complete data preset

**Goal:** Share one or more quads, including color, shape, animation, names, order, and primary selection.

**Steps:**

1. Configure the source Flexible Image in Single or Multiple mode.
2. Switch `Data Mode` to `Preset` and click `New` beside `Data Preset`.
3. Save the new `QuadDataPreset` in the intended project folder.
4. Confirm the preset Inspector contains the copied quad collection.
5. Assign the preset to other Flexible Images and verify all references use Preset mode.
6. When serialized size matters and fallback instance data is no longer needed, use `Delete Instance Data` on the referencing component.

**Expected result:** one ScriptableObject owns the shared procedural composition and referencing components serialize primarily the reference plus component-level state.

### Workflow: Choose the correct preset type

**Goal:** Avoid sharing more data than intended.

**Steps:**

1. Use `ColorPreset` when only a palette should be shared and local geometry, modules, animation, and color-grid layout must remain independent.
2. Use `QuadDataPreset` when the complete quad collection should be shared.
3. Use instance Single/Multiple data when each component must diverge independently.
4. For ColorPreset, tune the four preset-mix controls independently so local and shared colors combine intentionally.

**Expected result:** editing the selected asset changes exactly the intended set of properties and consumers.

### Workflow: Bake to a texture

**Goal:** Rasterize the current Flexible Image into a reusable image asset.

**Steps:**

1. Open the Flexible Image component context menu and choose `Bake To Texture`.
2. In `Texture Exporter`, review the initial Width/Height estimate, then set supersampling, optional padding, and a path relative to `Assets`.
3. Avoid Screen Space Pattern or Screen Space Procedural Gradient for deterministic export; the window warns when either is present.
4. Click `Bake` and wait for AssetDatabase refresh and import.
5. Confirm the new texture is imported as a single Sprite with high-quality compression.

**Expected result:** a PNG, JPG/JPEG, EXR, or TGA is written under Assets; a missing extension becomes PNG and an unsupported extension appends `.png`.

### Texture Exporter controls

| Control | Guidance |
|---|---|
| Source | Read-only selected Flexible Image name. Reopen the window from another component to change it. |
| Width / Height | Final output pixels, minimum `1`. The initial estimate uses the primary quad's RectTransform size, Size Modifier, Canvas scale factor, and expanded outline. It currently assumes the composition consumes those bounds, so review Multiple/Preset layouts and off-center or overhanging quads manually. |
| Super Sample | Integer `≥1`, default `4`. Renders each axis at this multiple and averages back down. A factor of `n` processes roughly `n²` source pixels; lower it for large exports or limited memory. |
| 1px Padding | Reserves a transparent one-pixel border inside the requested final dimensions. It is ignored when either dimension is `2` or less. Useful when neighboring atlas sampling must not touch visible edge pixels. |
| Save Path (from Assets/) | Relative destination such as `BakedTextures/Panel.png`. Existing files are overwritten after a warning. Keep the path inside the project; the API combines it with `Application.dataPath`. |
| Bake | Enabled only for a nonempty path and positive dimensions. It closes the window, rebuilds the mesh, renders each quad, composites later quads over earlier ones, writes the file, and refreshes the AssetDatabase. |

Newly created outputs are configured as single Sprites with high-quality compression. Overwriting an existing texture preserves that asset's current importer settings.

### Workflow: Export from editor code

**Goal:** Bake a known Flexible Image through a tool or batch editor workflow.

```csharp
using JeffGrawAssets.FlexibleUI;

public static class FlexibleImageBakeExample
{
    public static void Bake(FlexibleImage image)
    {
        image.BakeToTexture(512, 512, 2, true, "Generated/FlexiblePanel.png");
    }
}
```

**Steps:**

1. Compile the call in an Editor assembly.
2. Pass positive width, height, and supersample values.
3. Use a path relative to `Assets`, because the exporter combines it with `Application.dataPath`.

**Expected result:** the texture appears at `Assets/Generated/FlexiblePanel.png` after the delayed bake and refresh.

## Verification

- Each preset asset exists under Assets and has the intended type.
- ColorPreset changes affect colors only; QuadDataPreset changes affect the complete shared composition.
- Preset-mode components reference the expected asset.
- Instance data is deleted only where reverting to local modes is not presently required.
- Exported files exist and have the requested pixel dimensions. Newly created files import as a single Sprite; pre-existing importer settings remain under project control.
- The Console contains no exporter error or unsupported-path exception.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `ColorPreset` | `ScriptableObject` | Shares four color groups across quads |
| `ColorPreset.CopyFrom(ColorPreset)` | method | Copies all palette values from another preset |
| `QuadDataPreset` | `ScriptableObject` | Shares a complete `QuadDataContainer` |
| `FlexibleImage.QuadDataPreset` | property | Assigns the complete-data preset |
| `FlexibleImage.BakeToTexture(...)` | Editor-only method | Schedules raster export at a requested resolution |
| `UIGraphicToTexture.BakeTexture(...)` | Editor-only method | Lower-level mesh/material texture baker |

## Common issues

- **Preset edits change too much** → A QuadDataPreset was used for a palette-only need → Use ColorPreset instead.
- **Preset colors seem weak or absent** → The relevant Preset Mix is low → Increase the mix for Primary, Outline, Procedural Gradient, or Pattern.
- **Changing away from Preset recreates data** → Instance data had been deleted → This is expected; local modes require an instance container.
- **Bake button is disabled** → Path or dimensions are invalid → Use a non-empty Assets-relative path and positive dimensions.
- **Export varies with window or screen state** → A color effect uses screen-space coordinates → Disable screen-space behavior before baking.
- **The file gains `.png` after another suffix** → The suffix is unsupported → Use `.png`, `.jpg`, `.jpeg`, `.exr`, or `.tga`.
- **A preset section is unavailable or absent in rendering** → Its global feature or selected subfeature is disabled → Enable the required global switches or use a preset compatible with the current feature set.

## Boundaries

- ColorPreset does not share color-grid dimensions, transforms, wrap modes, geometry, or animation.
- QuadDataPreset is shared mutable data; duplicate the asset before making a one-off variant.
- Texture export rasterizes the current rendering and is not a reversible conversion back to procedural data.
- The exporter currently assumes quads consume the RectTransform bounds when deriving its initial resolution; verify multi-quad exports manually.
