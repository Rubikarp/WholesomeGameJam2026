# Flexible Image global features

Open `Tools > FlexibleUI > Flexible Image Global Settings`. These switches rewrite the feature-flag block in `ProceduralBlurredImage.shader` and reimport the shader.

## Window controls

| Control | Guidance |
|---|---|
| Color Order | `Procedural Gradient Before Pattern` lets Pattern composite last; `Pattern Before Procedural Gradient` lets Gradient composite last. The same order is used for the component Inspector. Change it only after checking effects that deliberately combine both modules. |
| Feature toggle | Compiles or removes that shader section project-wide. Turning a parent off disables its controls even if child subfeatures remain checked. |
| Subfeature toggle | Compiles an individual Gradient, Pattern, or Cutout family/option. It matters only while its parent feature is enabled. |
| Reload from Disk | Discards the window's cached interpretation and rereads the feature block from the shader. Use after source-control changes or manual shader edits. |
| Defaults | Enables every feature and subfeature, restores Gradient-before-Pattern order, disables SoftMask integration, and recompiles once. It is a compatibility baseline, not a performance preset. |
| Disable All | Disables every optional feature and SoftMask integration, then recompiles once. Existing components are not rewritten, so re-enable required features deliberately. |

## Main features and subfeatures

| Main feature | Subfeatures or options exposed by the current shader |
|---|---|
| Skew | None |
| Stroke | None |
| Cutout | Simple, SDF |
| Outline | None |
| Procedural Gradient | SDF, Angle, Radial, Conical, Noise, Screen Space Option, Pointer Adjust Position Option |
| Pattern | Line, Shape, Grid, Fractal, Sprite, Screen Space Option |

The order is reflected in both the Inspector module ordering and shader compositing order. It is not just an organizational preference.

Every optional section and family requires its corresponding global switch. Serialized module data, preset data, or calls such as `EnableGradient()` add component data only; they do not enable the shader feature. A selected gradient, pattern, or cutout family also requires both its parent feature and that family's subfeature.

SDF Cutout makes the entire Procedural Gradient section unavailable on the same quad, regardless of whether the selected gradient family would have been SDF, Angle, Radial, Conical, or Noise. Use a Simple cutout, another quad, or another Flexible Image when both effects are required.

## Safe procedure

1. Inventory components and `QuadDataPreset` assets that use a feature.
2. Disable child subfeatures before disabling their parent when reducing a build deliberately.
3. Wait for shader import and compilation.
4. Reinspect components. Disabled modules can remain serialized and appear as unavailable; remove their data only if it is not needed later.
5. Test representative UI in every target pipeline and platform.

## SoftMask integration

When the compatible package is present, Enable/Disable controls the shader's `SOFTMASK` conditional block. Without the package, `About` opens its project page and `Add Package` installs the pinned Git package used by this version. Do not add the dependency solely because a scene does not use SoftMask.

## Automation rule

Global settings are project-wide source changes. An agent must not toggle them merely to make one Inspector option appear without telling the user and checking other Flexible Image assets. Prefer enabling a needed feature over silently deleting data that depends on it.
