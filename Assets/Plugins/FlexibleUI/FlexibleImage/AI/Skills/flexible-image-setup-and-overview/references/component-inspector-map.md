# Flexible Image component Inspector map

This map covers component-level controls before the selected quad's Animation, Color, and Shape sections.

## Image-compatible controls

| Control | Guidance |
|---|---|
| Source Image | Optional Sprite shared by every quad unless that quad has Disable Sprite. It is also the source sampled by Sprite Pattern. Leave empty for purely procedural color/shape work. |
| Preserve Aspect | Shown for a Sprite with Simple or Filled Image Type. It follows ordinary uGUI Image behavior and changes the base sprite mesh aspect. |
| Image Type | Simple, Sliced, Tiled, Filled when supported by the Sprite. Flexible Image applies procedural data to the generated uGUI mesh. With no Sprite, Sliced/Tiled warn and fall back visually to Simple. |
| Fill controls | Ordinary uGUI method, origin, amount, and clockwise controls in Filled mode. Use procedural Simple Cutout/CutoutFill when the fill must participate in Flexible Image's cutout geometry instead. |
| Set Native Size | Resizes the RectTransform from the Source Image. It does not account for every additional quad or arbitrary procedural overhang. |
| Maskable | Ordinary uGUI masking participation. Expanded outlines and outwards softness can be clipped by parent masks. |

Flexible Image hides the ordinary Image color workflow: `FlexibleImage.color` maps to the first Primary Color cell. Routine styling belongs in quad data and should keep the shared Flexible Image material.

## Raycast controls

| Control | Guidance |
|---|---|
| Raycast Target | Disabled, Standard, or Advanced. Standard uses ordinary Image rect behavior. Advanced conforms to selected primary-quad procedural operations. |
| Raycast Flags | Size, Chamfer And Collapse, Stroke, Cutout, Ignore Outline, Offset, Rotation, UV Rect. Include only geometry that should affect input; see the shape Inspector map for behavior and parent/child caveats. |
| Base Padding | Left, Bottom, Right, Top canvas-unit padding on every platform. Positive values enlarge the target. |
| Extra IOS/Android Padding | Additional platform-only touch padding added to Base Padding. |

## Data Mode and presets

| Control | Guidance |
|---|---|
| Data Mode | Single renders only the primary instance quad; Multiple renders the enabled instance list; Preset reads a shared QuadDataPreset. |
| Data Preset | Complete shared quad collection used in Preset mode. A missing reference warns and uses instance multiple-mode settings as fallback. |
| New | Copies current instance data into a newly saved QuadDataPreset and assigns it. |
| Delete Instance Data | Removes inactive local fallback data to reduce serialized size. Use only after committing to the preset workflow. |
| Quad selector | Shown for Multiple/Preset collections. Selects which quad the Animation, Color, and Shape sections edit; it does not by itself change Primary. |

Use the multiple-quad skill for placement, ordering, names, Enabled, Set Primary, and flags.

## Animation section

Animation edits the currently selected quad. Driven By remains component-level, while each quad owns its five state/substate sequences. See the animation Inspector map before changing Playback, Start Idx, Unwind, Duration, or Interpolation.

## Color section

Primary Color always exists. The section selector adds/removes globally available Outline, Procedural Gradient, and Pattern modules. Its `x / y` readout counts present versus globally available removable modules; `- / y` means a mixed multi-selection, and `!` reports serialized modules whose global features are disabled. Unavailable entries can be removed from the selector menu.

The display order of Pattern and Procedural Gradient follows their order in Flexible Image Global Settings and therefore matches shader compositing order.

## Shape section

Corners, feathering, procedural transform, and UV Rect always exist. The section selector adds/removes globally available Skew, Stroke, and Cutout modules and reports unavailable serialized data like the Color selector. Shape quick actions are separate from this selector and write coordinated corner/skew values.

## Context menus

- Right-click Outline, Procedural Gradient, Pattern, Skew, Stroke, or Cutout headers to copy/paste that module's configuration plus corresponding animation values.
- Right-click a gradient or pattern family button to copy/paste that family's displayed control group.
- Right-click color cells for cell, row, column, or complete-grid operations as applicable.
- Module removal is available from the Color/Shape selector and header context menus rather than consuming a dedicated Inspector row.

## Global dependency rule

Every optional section needs its global parent feature, and every selectable family needs its matching global subfeature. Serialized data does not compile or enable shader code. After global settings change, wait for shader reimport and inspect any `Unavailable` modules before removing data.
