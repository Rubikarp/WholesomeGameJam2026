---
name: flexible-image-build-segmented-sci-fi-ui
description: "Use this skill whenever the user wants an LCARS-like, segmented starship, layered mission-console, cybernetic node-map, retro-futurist, or rail-and-elbow interface built with Flexible Image. Covers responsive rail composition, translucent console shells, sparse node networks, one-piece elbows, capsules, asymmetric end treatments, color systems, squircles, repeated controls, and restrained procedural interaction. Do NOT use for an isolated ordinary button or panel; route those to the color, shape, multiple-quad, or animation skill directly."
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

# Build Segmented Sci-Fi Interfaces

Build an original segmented interface from responsive rails, elbows, capsules, status elements, and content regions while using Flexible Image data rather than custom sprites or per-element materials.

## When to use this skill

- "build an LCARS-type command screen"
- "make a segmented starship console"
- "create a translucent starship mission log"
- "build a cybernetic skill tree or branching node map"
- "create colored rails around a futuristic dashboard"
- "design a retro-futurist navigation interface"
- "connect horizontal and vertical UI bars with rounded elbows"

Not for:

- One isolated shape; use `flexible-image-create-shapes-and-cutouts`.
- A general-purpose color treatment; use `flexible-image-create-color-effects`.
- Independent positioned shapes within one component; use `flexible-image-compose-multiple-quads`.
- Animation without this layout language; use `flexible-image-animate-interactive-ui`.

## Prerequisites

- Flexible Image `3.0.0`, Unity `2022.3.62+`, and uGUI.
- Confirm `JeffGrawAssets.FlexibleUI.FlexibleImage` exists and read `flexible-image-setup-and-overview` before automating a complete scene.
- Confirm every optional section and family the design will use is enabled under `Tools > FlexibleUI > Flexible Image Global Settings`. Module or preset data does not enable shader support.
- A one-piece SDF-cutout elbow requires Cutout and its SDF subfeature. SDF Cutout makes the entire Procedural Gradient section unavailable on that same quad.
- Keep the result original. Use segmented rails and retro-futurist proportions as a visual grammar without reproducing protected logos, exact labels, or an existing screen verbatim.
- Read [references/rail-grammar.md](references/rail-grammar.md) before constructing an LCARS-like rail skeleton, [references/layered-console-grammar.md](references/layered-console-grammar.md) before constructing a translucent framed console, or [references/node-network-grammar.md](references/node-network-grammar.md) before constructing a branching skill tree, technology web, or node map.

## Quick start

1. Choose the visual grammar: a dominant rail path for an LCARS-like composition, a nested translucent shell for a layered console, or a sparse topology of nodes and links for a skill map.
2. Establish the large-scale architecture before styling: responsive Layout Group rails, a nested shell/content stack, or separately anchored graph, counter, and detail regions.
3. Build the defining relationships next. For rails, classify joins and terminals and construct elbows; for a node map, draw meaningful connectors behind the nodes and hide their endpoints beneath node fills.
4. Use a small, intentional palette and reserve the strongest fill/Outline contrast for current state or priority.
5. Add readable content and restrained status treatment without filling deliberate negative space.

Expected result: a recognizable original sci-fi interface with clean structural continuity, readable negative space, and no dependency on custom panel sprites.

## Workflows

### Workflow: Plan the responsive rail composition

**Goal:** Create a coherent screen architecture before styling individual controls.

**Steps:**

1. Divide the Canvas into a navigation rail, one or two horizontal command/status rails, corner elbows, and a flexible central content region.
2. Use anchors and Layout Groups so the rails retain thickness while their long axis adapts to resolution.
3. Let black or near-black negative space separate functional groups. Avoid filling every gap with decoration.
4. Vary segment lengths according to hierarchy rather than repeating a uniform button grid.
5. Keep labels inside or immediately beside the segment they identify and test legibility at the target resolution.

**Expected result:** resizing the Canvas changes rail lengths and content space without breaking thickness, gaps, or elbow alignment.

### Workflow: Build a layered translucent mission console

**Goal:** Reproduce the hierarchy of a polished glassy starship console without flattening every foreground and background element into one decorative layer.

**Steps:**

1. Separate the composition into semantic backdrop, outer shell, inner content plane, navigation/header, bordered viewport, records, and footer. Backdrop planets, vectors, grids, or telemetry remain behind the shell and must stay subordinate through low alpha and low contrast.
2. Give the shell two different edge jobs: a wide expanded Outline with `Fade To Perimeter` for the soft halo, and a separate thin crisp Outline for structural definition. Do not stack several similar translucent borders; their alpha compounds into muddy bands.
3. Keep most foreground fills dark and partially translucent, usually around `.78–.94` alpha. Start structural Outlines around `1–3` units and Softness around `.65–1`; reserve much wider Outlines for the deliberately faded outer glow.
4. Use a low-alpha, blended, inverted Radial gradient with its origin just outside the rect for subtle directional light. A useful wide-panel starting region is Pos near `.02/1.1`, Size near `1/1`, Strength `.38–.48`, and secondary alpha `.18–.32`.
5. Build navigation tabs with a Layout Group and FI Skew using `Collapse Into Parallelogram`, commonly around `.1–.18`. Explicitly choose `Left` or `Right` as the collapsed edge so the slant supports the intended eye flow and surrounding composition. Keep silhouettes consistent and communicate selection through a brighter fill/outline and the built-in Selectable state system.
6. Make the content hierarchy carry the screen: one expanded record, compact repeated rows, a real FI scrollbar, and restrained status color. Keep rows mostly flat.
7. Do not add Flexible Blur automatically. A meaningful authored backdrop showing through alpha may already provide depth and context. Add blur only when the real background competes with text or the requested reference clearly depends on frosted separation.
8. Inspect at every target resolution. Confirm the background remains visibly behind the panel, translucent fills do not accidentally double-darken, tabs stay aligned, and dense labels do not clip.

**Expected result:** the screen reads as a nested translucent instrument panel with crisp navigation and information hierarchy, while ambient geometry remains clearly behind it.

Read [references/layered-console-grammar.md](references/layered-console-grammar.md) for the tested layer stack, representative values, and restraint rules.

### Workflow: Build a sparse node-network interface

**Goal:** Make a branching skill tree, technology web, or system map read through topology and state rather than decorative density.

**Steps:**

1. Partition the screen into independent anchor regions: a stretching command rail, a centered graph group, optional side counters, and a separately bounded detail panel. Keep useful negative space between them.
2. Define visual roles before drawing nodes. Give the selected node the strongest fill/Outline contrast, inactive available nodes a dark fill and restrained Outline, and locked/background nodes lower contrast still.
3. Use flat chamfers (`Concavity = 1`) for clipped technical silhouettes. For diamonds, set Flexible Image's procedural `Rotation` so the rendered shape turns while sibling or child labels stay upright; do not rotate a text-bearing hierarchy merely to turn the surface.
4. Draw connectors before nodes. Route every endpoint beneath a node interior or terminal cap so antialiasing and small alignment errors are hidden, and never let a connector cross labels or continue visibly through a panel.
5. Distinguish the active route with one brighter connector color or a small parallel trace bundle. Keep inactive wiring thin, dim, and sparse enough that it remains topology rather than texture.
6. Put explanation and actions in an independent detail region with explicit text insets. Do not use the selected node's own bounds as a spill area for descriptive copy.
7. Keep most surfaces flat. A low-alpha blended Angle or Radial treatment may support the selected node or detail panel.
8. If motion is appropriate, animate only the focus cue—such as a restrained Outline/size pulse—through built-in FI states without moving the graph layout.

**Expected result:** the selected path is obvious at a glance, connectors explain relationships without competing with labels, and the surrounding screen remains deliberately quiet.

Read [references/node-network-grammar.md](references/node-network-grammar.md) for tested role, connector, geometry, layout, and visual-QA guidance.

### Workflow: Construct a one-piece elbow

**Goal:** Join perpendicular rails with a continuous outer silhouette and controllable inner radius.

**Steps:**

1. Create one Flexible Image covering the elbow's full bounding rectangle.
2. Round or squircle only the exposed outer corners needed by the design.
3. Enable Cutout, select SDF, and use Anchors placement for the removed quadrant.
4. Extend the cutout through the open outer edges so the remaining visible region forms an L rather than a closed hole.
5. Shape the cutout's chamfer and concavity/squircle to form the inner elbow radius. Keep the elbow leg square wherever the spine or arm continues into another segment.
6. Use a solid primary fill or Outline on this quad. Procedural Gradient is unavailable while its SDF Cutout is active.
7. If the elbow requires an independent gradient, interaction, or differently colored arm, use another Flexible Image or a Multiple-mode construction instead.

**Expected result:** the elbow reads as one continuous rail with no overlapping-alpha seam or accidental interior border.

### Workflow: Build segmented rails and capsules

**Goal:** Create flush interactive and decorative rail segments.

**Steps:**

1. Use Horizontal or Vertical Layout Groups for repeated segments and give important controls larger preferred sizes.
2. Before duplicating segments, classify each relevant edge as an internal join, exposed terminal, or intentional canvas bleed. Keep adjoining faces square or complementary, round only exposed ends, and mirror or override corner/skew data when the edge's role changes rather than reusing a left terminal unchanged on the right.
3. For separate touching segments, give both mating edges matching Bidirectional Softness around `.5–1` and begin with no geometric overlap. Their fades meet smoothly across the boundary; Inwards on both edges can instead expose a dark hairline even when their boundaries are exact.
4. Inspect joins at 1:1 at every target resolution. If Canvas scaling still places a join between physical pixels, add only the smallest overlap that removes it; Canvas `Pixel Perfect` may help static screen-space layouts but is not a substitute for the correct feather mode.
5. Use normalized chamfer for classic capsules. Consider Squircle smoothing selectively for softer large panels or a more modern variant.
6. Use a QuadDataPreset when many segments share geometry and states; use local data where end treatment differs.
7. Prefer separate Flexible Images for segments that need independent selection, animation, raycasting, or colors.

**Expected result:** rail segments meet cleanly, exposed ends feel intentional, and the hierarchy remains easy to resize or reorder.

### Workflow: Establish color and surface language

**Goal:** Produce a distinctive segmented palette without losing hierarchy.

**Steps:**

1. Use a near-black background and a restricted set of warm, cool, and neutral rail colors.
2. Keep most segments flat. Reserve the strongest color and surface treatment for focus and system state.
3. Do not put Procedural Gradient on a quad using SDF Cutout. Place it on another rail segment, another quad, or a separate overlay Flexible Image.
4. Style through Flexible Image data and presets rather than creating unique materials.

**Expected result:** color communicates grouping and state while the segmented silhouette remains readable.

### Workflow: Add restrained interaction and status motion

**Goal:** Make the interface feel responsive without animating the layout into instability.

**Steps:**

1. Use Selectable-driven FI states for ordinary interactive rail buttons.
2. Use Script mode for navigation state, alerts, system status, and repeated meters.
3. Consider a brief brightening, procedural expansion, pulse, or light sweep followed by a stable settled state.
4. Animate Flexible Image procedural data rather than Layout Group-owned RectTransform dimensions.
5. Route detailed state/substate authoring to `flexible-image-animate-interactive-ui` and verify motion with two upright Play-mode frames whose visible FI state differs. Configured animation alone is not motion evidence.

**Expected result:** state changes are legible and localized while rails and content remain aligned.

Read `flexible-image-create-shapes-and-cutouts` for cutout and squircle controls, `flexible-image-create-color-effects` for color grids and screen-space effects, and `flexible-image-animate-interactive-ui` for procedural states.

## Verification

- The screen is an original composition, not a close reconstruction of an existing branded interface.
- Rail thickness and content structure survive the target aspect ratios.
- Elbows have clean continuous outer and inner edges without alpha-overlap seams.
- Every rail seam has matching thickness, a consistent gap if one is intentional, and square corners on both mating edges. Only exposed ends receive terminal treatment.
- Every flush join is visually checked at 1:1 at each target resolution; coordinate equality alone does not prove that two softened edges cover the same physical pixel.
- Every used optional section and selected family is globally enabled.
- No quad combines SDF Cutout with Procedural Gradient.
- Squircle mode, when claimed, is actually enabled rather than approximated with ordinary concavity.
- Screen-space effects use matching grids/settings across participating elements.
- Interactive or status motion is visibly verified by two different upright Play-mode frames.
- Routine styling continues to use the shared Flexible Image material.
- A layered console keeps ambient geometry behind the shell, uses separate soft-glow and crisp-border roles, and avoids compounded translucent fills.
- A node network has visibly different selected, available, and background roles; connectors terminate beneath nodes and never cross text or detail panels.
- Diamond or rotated node surfaces use procedural Rotation so their labels remain upright.
- Broad translucent atmosphere layers cover the intended region or feather within it; a partial overlay does not end as an unexplained hard seam across open space.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FlexibleImage` | component | Renders each rail, capsule, elbow, or panel |
| `FlexibleImage.QuadDataMode` | enum | Chooses Single, Multiple, or Preset construction |
| `QuadData.EnableCutout()` | method | Adds cutout module data when Cutout is globally available |
| `QuadData.EnableOutline()` | method | Adds outline module data when Outline is globally available |
| `QuadDataPreset` | ScriptableObject | Shares rail geometry, modules, and animation states |
| `ColorPreset` | ScriptableObject | Shares the palette without sharing geometry |
| `HorizontalLayoutGroup`, `VerticalLayoutGroup` | uGUI components | Arrange responsive rail segments |

## Common issues

- **A requested section or family is missing** → Its global parent feature or subfeature is disabled → Enable it with the user's awareness or choose an available treatment.
- **Procedural Gradient disappears on an elbow** → That quad uses SDF Cutout → Use a solid color treatment, Simple cutout, or a separate quad/component for the gradient.
- **An elbow looks like overlapping rectangles** → Separate translucent quads or outlines reveal their join → Use one SDF-cutout silhouette or keep a multi-quad elbow opaque and remove internal outlining.
- **Rail ends look inconsistently rounded** → Internal and exposed edges received the same corner treatment → Square the joins and round only terminal edges.
- **A dark hairline appears at a flush rail join** → Both segments feather inwards, Softness is too broad, or Canvas scaling creates fractional physical-pixel placement → Use Bidirectional Softness `.5–1`, test without overlap first, then add only the smallest target-resolution-specific overlap still needed.
- **The design resembles a generic dashboard** → Rails are decorative frames rather than the navigation hierarchy → Let the segmented rail system determine grouping, navigation, and focal flow.
- **A layered console looks muddy or uniformly blue** → Similar translucent fills and borders were stacked without distinct jobs → Reduce the number of planes, separate soft glow from crisp structure, and restore contrast through content hierarchy.
- **Background decoration competes with mission text** → Ambient geometry was treated like foreground UI → Lower its alpha/contrast and keep it behind the shell.
- **Every node demands equal attention** → Fill, Outline, and connector contrast were assigned by palette instead of state → Establish selected, available, locked/background, and detail roles before styling individual nodes.
- **Circuit lines cut through nodes, labels, or the detail panel** → Links were drawn as decoration after content placement → Draw links first, route only meaningful relationships, and bury their endpoints beneath node interiors.
- **Diamond text is rotated or counter-rotated by hand** → The RectTransform or parent hierarchy was rotated → Rotate the Flexible Image surface procedurally and leave the text layout upright.
- **A dark or colored horizontal band crosses otherwise open space** → A translucent atmosphere/readability overlay ends inside the visible composition → Extend it to the intended screen region or feather its own alpha before the edge.
- **Screen-space color breaks at segment boundaries** → Participating elements use different section grids or settings → Make the corresponding grids and transforms identical.

## Boundaries

- This skill describes a visual construction system, not a requirement to use every Flexible Image feature.
- SDF Cutout disables the entire Procedural Gradient section on the same quad.
- Global feature changes rewrite the shared shader and should not be made silently for one scene.
- Multiple quads share one Graphic's high-level state; use separate children when segments need independent interaction or animation.
- Avoid translucent overlapping geometry where doubled alpha would reveal construction seams.
