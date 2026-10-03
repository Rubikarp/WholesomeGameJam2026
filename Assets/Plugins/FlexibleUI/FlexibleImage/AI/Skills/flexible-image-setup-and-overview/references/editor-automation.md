# Flexible Image editor automation

Read this reference when an agent creates or edits a complete Flexible Image UI through MCP, an editor command, or another Unity automation facility.

## Preserve the working scene

- Do not replace, close, or save the user's dirty active scene.
- Create test scenes additively and save them to the user-approved destination.
- Use non-interactive APIs. If an operation can display a save, overwrite, or import dialog, choose a non-destructive path or stop for the user.
- Do not create persistent generator scripts or hand-edit Unity YAML unless the user explicitly requests that workflow.

## Establish a visible Canvas first

Configure and render a minimal panel before building the rest of the hierarchy.

- Use Screen Space Overlay when the UI has no camera-dependent blur or capture requirement.
- For camera-rendered UI, assign `Canvas.worldCamera` where applicable, use a sensible plane distance, and ensure both the camera culling mask and any active SRP renderer layer filters include every layer used by the Canvas and its children. A populated Canvas can otherwise disappear while the camera still clears or renders unrelated geometry.
- Do not introduce a second UI camera or URP camera stack merely for decoration. When a stack is required, confirm that the base camera contains the overlay camera, the overlay camera renders the UI layer, and the Canvas references the intended camera.
- Set a CanvasScaler reference resolution and screen-match policy intentionally. Verify the root rect and one known child at the target Game-view resolution before proceeding.

## Create Flexible Images through public data

- Create a GameObject with a RectTransform, CanvasRenderer, and `JeffGrawAssets.FlexibleUI.FlexibleImage`, parent it with world-position preservation disabled, then set anchors, pivot, anchored position, and size.
- Leave `FlexibleImage.material` unset for routine styling. The component supplies its shared procedural material. The Editor may later serialize that shared default into `m_Material`; audit sharing by resolved object identity and, for persistent materials, asset path rather than treating every non-null field as a unique material or requiring the raw field to remain null.
- Use `DataMode = FlexibleImage.QuadDataMode.Single` for ordinary elements and configure `PrimaryQuadData` through public properties.
- Call `EnableOutline()`, `EnableGradient()`, `EnablePattern()`, `EnableSkew()`, `EnableStroke()`, or `EnableCutout()` before assigning values for that module. These methods create both configuration and animation data.
- Prefer public setters such as `PrimaryColors`, `PrimaryColorDimensions`, `CornerChamfer`, `ProceduralGradientType`, `ProceduralGradientColors`, and `Pattern` over reflection or private serialized fields. Set grid dimensions before assigning individual cells.
- After batched changes, call `SetVerticesDirty()` and mark the scene dirty before saving.

## Leave a maintainable hierarchy

- Let hierarchy express the interface's actual ownership and layout rather than merely draw order. Parent each control's surface, content, and indicators under its logical owner, and name objects and quads for their purpose.
- After the composition is established, Multiple mode can consolidate passive layers that share one owning RectTransform and Graphic-level material, mask/sort, and input context. Keep separate objects for independent layout, text, masks, materials or sprites, raycast targets, Selectables, or lifecycles.
- Do not add a wrapper for every primitive or combine an entire screen merely to reduce object count. Preserve the intended draw order within the resulting structure.

## Compose the screen before decorating it

- Establish hierarchy, spacing, typography, and primary actions first. Use Flexible Image features to clarify grouping, focus, state, and depth rather than placing effects indiscriminately.
- When working from a reference, separate the interface from the scene, illustration, or other content visible behind it. Use Flexible Image for UI surfaces, controls, masks, indicators, and UI decoration; do not redraw characters, vehicles, environments, or other non-UI subject matter with Flexible Image. Leave that region open, use a restrained placeholder, or use an appropriate non-FI asset when creating it is within scope.
- Treat accent rails, borders, icons, cutout voids, and animated indicators as occupied space. Inset text beyond them by a deliberate gap unless an overlap is intentional and remains legible.
- Preserve integrated horizontal overlines and underlines when they appear in a reference. Before replacing one with a vertical rail or adding a separate child line, try one Single-mode Flexible Image: Angle Gradient for a band or fade, or Outline plus outline-only Simple Cutout for a crisp edge that follows the control shape.
- Give each label a clear owning region and place it decisively inside or outside that region. Do not let text sit half across a panel edge; an intentional edge label needs enough backing or interruption to resolve the border cleanly.
- When a reference specifically uses a small offset plate or silhouette as depth, duplicate the foreground Flexible Image's shape treatment and place an opaque, subordinate-color copy behind it. Keep text and indicators on the foreground surface, and do not repeat the treatment under every surface. A uniformly bright second outline or a large translucent duplicate changes the visual role and can muddy adjacent content.
- Give repeated list rows a stable internal template with reserved regions for leading index/icon, title and subtitle, and trailing status or control. Prefer changing fill, outline, gradient, or animation for selection before changing row geometry; inconsistent bounds make text and badges drift or overflow across the list.
- In a dense list/detail interface, establish value hierarchy before adding effects: large containers quiet, inactive rows intermediate, and the selected row, section header, or primary action highest contrast. Do not outline every nested boundary at the same intensity; spacing, fill value, and one structural edge often group content more clearly.
- When creating several prototypes, vary their underlying composition rather than only their theme and color.
- Use Flexible Image treatments where they serve the composition rather than as feature coverage. Quiet Angle or Radial shading can support depth; Pattern belongs where repeated texture or rhythm has a purpose.
- Keep important panels and controls large enough to read at the target resolution. Very thin Flexible Images are appropriate for dividers, progress tracks, and intentional line art, not as a substitute for the main interface.
- Prefer uGUI elements for a UI prototype. World-space primitives can provide a backdrop, but they must not be the only visible evidence that the scene rendered.
- For a showcase or interactive prototype, route to the color, shape, multiple-quad, animation, and blur skills as needed. Unless the user explicitly requests static output, include at least one runtime-visible motion or state change across the set. Prefer a state change that communicates interaction; use moving Pattern Speed only when motion of the repeated motif is itself appropriate.
- Verify motion in Play mode or with two captures separated in time, and confirm the captures actually differ. Identical captures fail verification; a nonzero serialized speed or authored state alone is not proof that it runs. Confirm `Time.frameCount` advances as well: an unattended Editor can report Play mode active and unpaused while its player loop is stalled. When that happens through an automation bridge, pause and issue a bounded number of `EditorApplication.Step()` calls, then verify both the frame count and rendered pixels advanced before accepting the capture pair.

## Rendered verification is mandatory

1. Save the generated scene and render through the camera path used by its Canvas.
2. Confirm the capture is upright. Some graphics backends invert raw RenderTexture readback; flip the readback rows before PNG encoding when necessary rather than rotating the scene or camera.
3. Inspect all four frame edges and every panel boundary. Confirm the expected panels, text, and controls occupy meaningful screen extents with deliberate margins and are not clipped, collapsed, off-screen, or sitting unintentionally across a border.
4. Inspect tight `1:1` crops of text/decorative intersections, exposed terminals, canvas edges, and dynamic indicators. When a child crosses a container boundary, confirm the boundary is either intentionally continuous or cleanly occluded rather than accidentally showing through part of the child. When background decoration remains visible through transparency, confirm it cannot be mistaken for a foreground border, bar, or indicator.
5. Confirm each claimed gradient, pattern, outline, cutout, blur, or animation is visible at the intended subtlety in the rendered result. Do not exaggerate a quiet lighting gradient merely to prove that it exists.
6. For interactive or showcase work, verify at least one runtime motion/state change unless static output was requested.
7. Check at least the intended aspect ratio; for responsive work, also check one materially different aspect ratio.
8. Check the Console for missing references, rendering errors, and exceptions.

If a rendered capture cannot be obtained, report visual verification as incomplete. Hierarchy counts, component types, and serialized values are useful diagnostics but cannot establish visual quality.

For polished showcase work, consider one final skeptical pass before presentation: look specifically for labels that escape their owning region or enter a neighboring control, background accents that read as foreground data through transparency, and large translucent overlays whose hard edges form unexplained seams across focal content.
