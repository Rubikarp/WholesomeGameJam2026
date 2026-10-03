# Node-Network Grammar

Use this grammar for skill trees, technology webs, neural maps, upgrade graphs, and other sparse interfaces where relationships matter more than panel density.

## Start with roles

Define the rendering roles before choosing individual colors:

- **Selected/focal node:** strongest fill-to-background separation, brightest structural Outline, clearest label, and optionally one restrained built-in FI focus animation.
- **Available inactive node:** dark tinted fill, readable but quieter Outline, and subordinate metadata.
- **Locked/background node:** lowest contrast that remains legible; do not compete with the active route.
- **Connector:** relationship evidence, not surface decoration. The active path may be brighter; inactive branches stay thin and dim.
- **Detail region:** an independent reading surface for description and actions. It is not another graph node and should not inherit the graph's cramped label anatomy.

Do not make every node bright, filled, or animated. A node map becomes legible when state changes several channels together: fill contrast, Outline contrast, label weight, connector emphasis, and scale.

## Shape nodes without rotating content

- Flat clipped polygons begin with `Concavity = 1` and per-corner Chamfer. Use uniform Chamfer for symmetric technical nodes and per-corner values for directional or asymmetric panels.
- A square FI surface with procedural `Rotation = 45` makes a useful diamond while its text hierarchy remains upright. Prefer this to rotating a parent RectTransform and counter-rotating labels.
- A separate inner FI, or a carefully justified Multiple-mode quad, can form a focal core. Keep descriptive text in its own stable rectangle above the surface.
- Start structural Outlines around `1–3` reference units and Softness around `.5–1` with Bidirectional feathering. Increase contrast before adding more edge layers.
- Reserve rounded corners and squircles for a deliberate softer role; clipped technical nodes should not drift into generic pills.

RectTransform rotation remains reasonable for content-free line segments. The important distinction is that a decorative line has no layout to disturb, while a node surface usually owns upright labels and controls.

## Draw topology before content

1. Place the principal nodes and verify label bounds without any wires.
2. Draw connectors behind all nodes.
3. Route each connector only between semantically related nodes. Use a few horizontal, vertical, and diagonal segments rather than a noisy circuit texture.
4. Extend connector endpoints beneath node fills or terminal caps. Hidden overlap is more robust than attempting pixel-exact contact between two antialiased shapes.
5. Use one or two thickness tiers. At a `1920 x 1080` reference Canvas, roughly `1–3` units is a useful starting region; scale by rendered evidence rather than treating this as a fixed pixel rule.
6. Emphasize the selected route with a brighter color, slightly wider link, or a small bundle of parallel traces. Keep inactive links at visibly lower alpha.

Never let a connector remain visible through a node interior, cross a label, or terminate in the middle of an unrelated panel. If a route has no semantic endpoint, omit it.

## Partition the screen

- Anchor a top command/status rail independently from the graph.
- Keep the graph in a centered group that can scale or shift as one unit.
- Anchor counters or notifications to their own side region.
- Give the detail panel its own bounded column with explicit inner text margins.
- Preserve large quiet areas when the graph is sparse. Edge telemetry may reinforce the theme at very low contrast, but it must not become foreground texture.

This partitioning is more responsive than positioning every item in one coordinate soup. It also prevents graph growth from pushing detail text through panel borders.

## Surface and motion restraint

- A dark Primary fill plus a low-alpha blended Angle or Radial effect can add depth to the selected node or detail panel. Follow the off-edge subtle-gradient guidance in `flexible-image-create-color-effects` so the defining band or origin does not become a visible seam or dark well.
- Blur is not required when a quiet authored background already gives sufficient separation.
- For focus motion, prefer a small built-in Script or Selectable state change in Outline width/color, gradient weight, or procedural size. Keep node positions and connector topology stable.

## Native-resolution review

Inspect the upright rendered target at `1:1`, including crops of the focal node, one branch junction, the top rail, and the detail panel:

- Values and labels do not collide with icons or each other.
- Text stays fully inside its owning node or detail column.
- Connector endpoints disappear under nodes without dark gaps or bright protrusions.
- The active route is identifiable without reading every label.
- No partial translucent overlay ends as a hard horizontal or vertical band in open space.
- Background telemetry remains subordinate when viewed through every translucent region.
- If focus motion is claimed, two Play-mode captures visibly differ while the graph layout remains fixed.
