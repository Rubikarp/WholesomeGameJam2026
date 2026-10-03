---
name: flexible-image-setup-and-overview
description: "Use this skill whenever the user is starting with Flexible Image, upgrading from v2, changing its global shader features, or asks 'is Flexible Image installed', 'add a Flexible Image', 'migrate my old prefabs', or 'disable features I do not use'. Covers installation checks, first component creation, global feature configuration, and v2-to-v3 text-YAML migration. Do NOT use for detailed color design (see flexible-image-create-color-effects), shape construction (see flexible-image-create-shapes-and-cutouts), or blur integration (see flexible-image-add-blur). When in doubt whether a procedural uGUI request could involve Flexible Image, use this skill first."
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

# Set Up Flexible Image

Use Flexible Image as a drop-in uGUI Image replacement, confirm the correct version is installed, configure only the shader features the project needs, and safely discover or migrate v2 serialized assets.

## When to use this skill

- "add a procedural image to this canvas"
- "convert this Image to Flexible Image"
- "which Flexible Image features can I disable?"
- "migrate my v2 scenes and prefabs to v3"
- "why is this Flexible Image control missing?"

Not for:

- Designing colors, gradients, outlines, or patterns; see `flexible-image-create-color-effects`.
- Designing geometry or cutouts; see `flexible-image-create-shapes-and-cutouts`.
- Adding blur; see `flexible-image-add-blur`.

Skill index: use `flexible-image-create-color-effects` for color, `flexible-image-create-shapes-and-cutouts` for geometry, `flexible-image-compose-multiple-quads` for layered compositions, `flexible-image-animate-interactive-ui` for states, `flexible-image-build-segmented-sci-fi-ui` for segmented rails, layered consoles, and node-network systems, `flexible-image-use-presets-and-export` for reuse/export, and `flexible-image-add-blur` for the optional Flexible Blur integration.

## Prerequisites

- Flexible Image `3.0.0` and Unity `2022.3.62` or newer.
- uGUI. Flexible Image does not provide a UI Toolkit control.
- Any render pipeline: Built-in, URP, or HDRP.
- Confirm `JeffGrawAssets.FlexibleUI.FlexibleImage` resolves, or that `Tools > FlexibleUI > Flexible Image Global Settings` exists.
- If neither check succeeds, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652).

For v2 migration, require `Edit > Project Settings > Editor > Asset Serialization > Mode` to have been set to `Force Text` while the project still used Flexible Image v2. Binary assets cannot be recovered by this migrator.

## Quick start

1. Select a Canvas or a child of a Canvas.
2. In Unity 6.3 or newer, choose `GameObject > UI (Canvas) > Flexible Image`. In older supported versions, choose `GameObject > UI > Flexible Image`.
3. If no Canvas exists, the menu command creates a screen-space overlay Canvas and EventSystem.
4. In the Inspector, leave `Data Mode` at `Single` for one ordinary procedural element.
5. Expand `Color`, set `Primary Color`, then expand `Shape` and set the corner chamfer.

Expected result: the Hierarchy contains a `FlexibleImage` object with a `RectTransform`, `CanvasRenderer`, and `JeffGrawAssets.FlexibleUI.FlexibleImage`, rendered through the asset's shared hidden shader.

Read [references/component-inspector-map.md](references/component-inspector-map.md) when interpreting component-level Image, raycast, data-mode, module-selector, or context-menu controls.

## Workflows

### Workflow: Convert an existing uGUI Image

**Goal:** Preserve ordinary Image properties while replacing the component.

**Steps:**

1. Open the existing `Image` component's context menu.
2. Choose `Convert to FlexibleImage`.
3. Confirm the sprite, material, raycast target, maskable state, Image type, fill settings, aspect setting, and color were carried across.
4. Configure procedural data in the Flexible Image Inspector.

**Expected result:** the GameObject has one `FlexibleImage` in place of the original `Image`; its former color is the primary procedural color.

### Workflow: Build or automate a complete UI scene

**Goal:** Create a scene whose Flexible Images are visibly correct, not merely present in the hierarchy.

**Steps:**

1. Preserve the user's active scene and create the new scene without replacing or closing dirty work.
2. Choose the Canvas render mode deliberately. Screen Space Overlay is the simplest choice when camera-dependent blur is not required. For camera-rendered UI, assign a valid camera and ensure both its culling mask and any active SRP renderer layer filters include every layer used by the Canvas and its children.
3. Use a CanvasScaler with an intentional reference resolution, then give each RectTransform explicit anchors, pivot, position, and size. Check the layout at the target Game-view aspect ratio before adding detail.
4. Group the hierarchy according to the interface's actual ownership and layout. Keep unrelated visual primitives out of the Canvas root, and parent each control's surface, content, and indicators under one understandable owner.
5. Create Flexible Images with their normal shared material. Style them through quad data; do not create unique materials for ordinary colors or procedural effects.
6. Route color, shape, multiple-quad, animation, and blur work to their corresponding skills. Use Multiple mode when already-intended passive layers share one owning RectTransform and Graphic context. Unless the user asks for a static mockup, every interactive or showcase prototype set must include at least one visibly verified runtime motion or procedural state change.
7. Render the result from the same camera path the user will see. Verify recognizable panels, labels, and controls at their intended screen extents. Component counts and serialized module presence are not visual verification.

**Expected result:** the Game view shows the intended complete UI, the Hierarchy communicates its functional regions and controls, its procedural effects are visibly distinguishable, and the user's prior scene remains untouched.

Read [references/editor-automation.md](references/editor-automation.md) before creating a complete scene through MCP, an editor command, or another automation facility.

### Workflow: Configure global shader features

**Goal:** Remove unused shader work without leaving components dependent on disabled features.

**Steps:**

1. Open `Tools > FlexibleUI > Flexible Image Global Settings`.
2. Review current components and presets before disabling anything.
3. Toggle only features and subfeatures the project does not use. Feature changes rewrite and reimport the Flexible Image shader.
4. Treat every section and selectable variant as conditional: Outline, Procedural Gradient, Pattern, Skew, Stroke, Cutout, and each listed gradient/pattern/cutout family require their corresponding global parent feature and subfeature. Adding serialized module data does not enable shader support.
5. Choose whether Pattern or Procedural Gradient appears first; this also changes their shader section order.
6. Use `Reload from Disk` after an external shader edit, `Defaults` to restore the shipped configuration, or `Disable All` only when intentionally rebuilding the configuration.
7. After recompilation, inspect affected components for unavailable-module warnings and use their offered cleanup only when the hidden data is genuinely unwanted.

**Expected result:** the global window and component module selectors agree about available features, and the Console has no shader compilation errors.

Read [references/global-features.md](references/global-features.md) before changing individual flags or SoftMask integration.

### Workflow: Discover and migrate v2 assets

**Goal:** Rewrite eligible v2 scene, prefab, and preset YAML into v3 modular data without loading and resaving each asset.

**Steps:**

1. Commit or back up the project. Save unrelated work. Do not save affected v2 assets after installing v3.
2. Open `Tools > FlexibleUI > Migrate Flexible Image Version 2 Assets` and accept the session warning.
3. Drag one or more project folders into the scope list. Prefer the narrowest folders that contain the assets being migrated.
4. Click `Scan`, then review every row by status and details.
5. Resolve all blocked or failed results. Binary results must be converted using v2 with Force Text; v3 cannot infer their contents.
6. Select eligible scenes, prefabs, and presets, then click `Migrate Selected`.
7. Allow the synchronous reimport and validation to finish. Do not interrupt Unity while asset editing is suspended.

**Expected result:** selected rows report `Version3`/migrated, unused implicit-off modules are omitted, configured modules are preserved, and a backup exists under `Library/FlexibleImage/MigrationBackups/`.

Read [references/v2-migration.md](references/v2-migration.md) before migrating production assets.

## Verification

- `FlexibleImage` exists on the intended GameObject and an ordinary `Image` was not left beside it accidentally. Audit the latter by exact runtime type because `FlexibleImage` inherits `UnityEngine.UI.Image`; `component.GetType() == typeof(UnityEngine.UI.Image)` counts only ordinary Images.
- The Canvas is visible through its configured render path. A Screen Space Camera Canvas has a non-null camera whose culling mask includes the Canvas hierarchy's layers.
- Automated scene work includes a rendered visual check at the intended aspect ratio; hierarchy and component inspection alone do not pass.
- An interactive or showcase prototype set includes at least one runtime-verified motion or procedural state change unless the user explicitly requested static output.
- Routine Flexible Image styling uses quad data and the shared material rather than generated per-element materials.
- Direct Canvas children express the interface's actual top-level ownership or genuinely screen-spanning layers, not an ungrouped list of labels, lines, and decorative primitives.
- GameObject and quad names describe their UI role, and any Multiple-mode consolidation preserves layout, input, and render behavior.
- `Data Mode` is appropriate for the intended workflow: `Single`, `Multiple`, or `Preset`.
- Every module used by a component is enabled globally.
- Shader recompilation finishes without errors after global settings change.
- Migration results contain no `Blocked` or `Failed` row before migration begins.
- Migrated files rediscover as v3 and the Console reports the backup directory.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FlexibleImage` | `JeffGrawAssets.FlexibleUI.FlexibleImage` | uGUI Image replacement and main runtime component |
| `DataMode` | `FlexibleImage.QuadDataMode` | Selects Single, Multiple, or Preset procedural data |
| `ActiveQuadDataContainer` | `QuadDataContainer` | Returns the active instance or preset-owned quad collection |
| `SetVerticesDirty()` | inherited `Graphic` method | Requests a procedural mesh/material refresh after runtime changes |
| `FlexibleImageFeatureManager` | Editor-only static class | Reads and changes global shader features and section order |

## Common issues

- **A control or module is missing** → Its global feature or subfeature is disabled → Open Global Settings, enable it, and wait for shader recompilation.
- **A converted image looks different** → Procedural defaults differ from sprite-driven Image behavior → Verify Image type, sprite, primary color, and procedural modules.
- **Migration reports binary** → The file does not begin with Unity YAML → Return to a v2 backup, enable Force Text, save the affected assets, then reinstall v3 and scan again.
- **Migration is blocked** → The asset contains moved animation bindings, moved prefab overrides, mixed v2/v3 data, or malformed YAML → Resolve the specific detail instead of forcing a rewrite.
- **Runtime property changes do not redraw immediately** → The changed path did not signal geometry dirtiness → Use a public setter that dirties data or call `SetVerticesDirty()` after the change.
- **The hierarchy is populated but Game view shows only background geometry** → The Canvas layer is excluded by the camera culling mask or the active SRP renderer's transparent-layer filter → Include the Canvas layer in both paths, or use Screen Space Overlay when camera rendering is unnecessary.
- **Automation reports gradients or patterns that are not visible** → It verified data instead of rendered output → Inspect the Game view and tune the effect until it changes visible pixels at the target resolution.
- **The Canvas contains dozens of unrelated-looking primitives** → Elements were positioned globally without expressing ownership → Add semantic region/control parents, rename by purpose, and consolidate same-owner passive graphics with Multiple mode.
- **Several prototypes all look like the same dashboard** → Color and labels changed but the composition did not → Reconsider the underlying layout before styling details.

## Boundaries

- Flexible Image targets uGUI, not UI Toolkit.
- Global feature changes modify the shipped shader and trigger compilation; do not perform them incidentally while styling one object.
- The v3 migrator handles text-serialized assets only. It intentionally does not recover binary serialization.
- Migration intentionally omits modules that were implicitly off in v2. Runtime code or Animation Clip Adapters that later activate such features may require the module to be added manually.
- Do not edit serialized YAML by hand when the supplied migration window can classify and validate it.
