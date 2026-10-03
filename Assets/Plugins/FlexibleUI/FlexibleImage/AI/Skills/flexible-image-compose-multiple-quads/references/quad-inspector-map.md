# Flexible Image quad composition Inspector map

## Data Mode

| Control | Guidance |
|---|---|
| Single | Renders only the primary instance quad. Use for an ordinary one-shape control and the smallest local data model. |
| Multiple | Renders the enabled instance quads in list order. Use when one Graphic should contain several independently styled layers. |
| Preset | Renders the `QuadDataPreset` container. All referencing components share its quad configuration and animation data while retaining their own RectTransforms and runtime animation progress. |
| Data Preset | Assigns the active QuadDataPreset in Preset mode. A missing preset falls back to instance data rather than providing a valid shared composition. |
| New | Copies the current instance quad collection into a new QuadDataPreset asset and assigns it. |
| Delete Instance Data | Removes inactive fallback instance data to reduce serialization. Use only when returning to Single/Multiple is not currently required; local data will be recreated if needed later. |

## Quad list

| Control | Guidance |
|---|---|
| Enabled | Excludes the quad from mesh generation without deleting its values. Disabled quads still count as serialized data. |
| Name | Use role names such as `Background`, `Fill`, `Icon`, or `Highlight`; `GetQuadData(string)` searches this value. Avoid depending on auto-generated `Quad0` names in gameplay code. |
| Add | Appends a complete new QuadData. Optional modules are absent until enabled. |
| Remove | Deletes the selected quad. Removal adjusts the primary index when necessary; update any external `AnimationClipAdapter.quadIdx` bindings. |
| Reorder | Controls triangle/list order and therefore overlap. Verify the intended front/back result in Game view after dragging. |
| Set Primary | Makes the selected quad the component's `PrimaryQuadData`. The primary quad owns the convenience API surface and advanced raycast shape. |

Single mode can retain additional serialized quads but renders only the primary one. Switch to Multiple when all enabled list entries should draw.

## Quad placement

Placement follows RectTransform-like anchor math inside the component RectTransform.

| Control | Guidance |
|---|---|
| Anchor Min/Max | Normalized positions in the component rect. Equal values create a fixed anchor. Different values define a stretch span that changes with parent size. |
| Anchored Position | Canvas-unit offset from the center of the anchor span to the quad pivot. |
| Size Delta | Added to the anchor-span size. With equal anchors it is the quad's size; with split anchors it grows/shrinks the stretched result. |
| Pivot | Normalized point inside the quad used by placement and procedural rotation. `.5,.5` is centered; edge pivots make edge anchoring easier to reason about. |

The resulting size adjustment is `parentSize * (AnchorMax - AnchorMin - 1) + SizeDelta`, because Flexible Image starts from the parent-sized base mesh. The position adjustment uses the anchor-span center, Anchored Position, and pivot correction. Use Inspector semantics rather than writing the formula unless authoring tools.

Common placements:

- Full parent: Anchor Min `(0,0)`, Anchor Max `(1,1)`, Position `(0,0)`, Size Delta `(0,0)`, Pivot `(.5,.5)`.
- Fixed centered badge: both anchors `(.5,.5)`, centered pivot, Size Delta equal to badge size.
- Fixed right-edge accent: both anchors `(1,.5)`, Pivot `(1,.5)`, then use a small negative X position to inset it.
- Stretch with margins: anchors `(0,0)` to `(1,1)`, centered pivot, negative Size Delta equal to total horizontal/vertical margin, then offset if margins are asymmetric.

Quad placement is structural configuration. Animate the quad's procedural Offset, Size Modifier, or Rotation when a visual layer should move without changing its anchor contract.

## Per-quad flags

| Flag | Guidance |
|---|---|
| Disable Sprite | Prevents this quad from drawing the component-wide Source Image. Use for solid/procedural auxiliary layers. With Flexible Blur integration, it also disables blur on that quad. |
| Force Simple Mesh | Makes the quad use Simple image geometry even when the component Image Type is Sliced, Tiled, or Filled. Use when only one quad should inherit sprite-specific mesh behavior. |

## Primary quad and input

Only one index is primary. `FlexibleImage.PrimaryQuadData`, the component-level color/shape convenience properties, and advanced raycast geometry refer to it. Decorative quads do not expand the hit region merely because they render outside the primary quad.

Choose the primary quad based on interaction, not visual prominence. A full-size panel can remain primary while a brighter badge draws later in the list.

## Module availability

Every quad has independent module data, but global features are shared project-wide. If a quad contains a module that is globally disabled, the Inspector reports it as unavailable. Enable the global feature or remove the unavailable quad module deliberately; adding the serialized reference alone cannot compile shader support.
