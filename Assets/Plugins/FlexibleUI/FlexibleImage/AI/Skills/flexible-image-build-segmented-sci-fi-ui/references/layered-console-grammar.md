# Layered mission-console grammar

Use this reference for translucent starship mission logs, vessel-management screens, and dense framed sci-fi consoles. Treat the values as tested starting points at a `1920 x 1080` Canvas reference resolution, not universal defaults.

## Separate the visual planes

Build the screen in this order:

1. UI backdrop: vector bands, orbit marks, or subdued telemetry. Treat stars, planets, environments, vehicles, and characters as external content rather than rebuilding them with FI.
2. Outer shell glow.
3. Crisp outer shell.
4. Inner content plane.
5. Navigation/header and bordered viewport.
6. One expanded record plus compact repeated rows.
7. Scrollbar, footer controls, and telemetry.

Elements visible through the console's transparency are not automatically part of the foreground UI. Omit unsupplied scene content or use an appropriate external asset. Keep any FI-authored telemetry decoration low-contrast and sparse, and let it disappear beneath dense text regions.

## Tested starting values

| Role | Useful FI construction |
|---|---|
| Outer halo | Primary alpha about `.2`, Softness `1.2`, asymmetric Chamfer around `18–36`, expanded Outline around `13` with alpha about `.44`, and `Fade To Perimeter` on. |
| Crisp shell | Primary alpha about `.85`, Softness `1`, Chamfer around `18–34`, Outline around `3` with alpha about `.92`. A blended inverted Radial at Pos `.02/1.1`, Size `1/1`, Strength `.48`, and secondary alpha about `.32` can light one edge without exposing a centered radial well. |
| Inner content plane | Primary alpha about `.9`, Softness `1`, Chamfer around `12`, Outline `2` with alpha about `.85`. A second off-edge Radial can use Pos `.06/1.06`, Size `1/.92`, Strength `.42`, and secondary alpha about `.26`. |
| Bordered viewport | Primary alpha about `.78`, Softness `.8`, Chamfer around `8`, Outline `2` with alpha about `.86`. Keep this border crisp; it is structure, not glow. |
| Expanded record body | Primary alpha about `.9`, Softness `.7`, exposed-corner Chamfer around `4`, Outline `1` with alpha about `.66`, and optional off-edge Radial secondary alpha around `.18` at Strength `.38`. |
| Compact records | Primary alpha around `.91`, Softness around `.65`, Outline around `1.1` with alpha around `.72`, and rounding only on genuinely exposed ends. Use state color and a small index rail instead of a separate effect on every row. |
| Selected tab | `Collapse Into Parallelogram` around `.16` with a Left or Right collapsed edge chosen for the intended eye flow and surrounding composition, Softness `.8`, Outline around `1.8`, and a brighter related fill. A restrained highlighted state can add roughly `2 x 1` size and a pressed state can subtract roughly `2 x 2`; keep Layout Group-owned positioning stable. |
| Ambient vector band | Low-alpha fill around `.1` and Softness around `.6`. It should be sensed behind the panel rather than read as a foreground control. |

These values came from one successful reference reconstruction and should be scaled and retuned for the actual RectTransforms and Canvas. Preserve their roles even when the numbers change.

## Edge hierarchy

- Use a separate FI for the outer faded glow and the crisp shell. Their different jobs justify the extra plane.
- Inside the shell, prefer one fill plane per semantic region. Several nearly identical semitransparent panels darken each other and produce accidental bands.
- Use thin Outlines for structure, wider faded Outlines for light, and saturated rails for active status. Do not make every border equally bright.
- Keep mating row edges square or matched. Use Bidirectional Softness on flush neighboring segments as described in `rail-grammar.md`.

## Navigation and records

- Use a Horizontal Layout Group for skewed tabs. FI Skew supplies the parallelogram; choose its Left or Right collapsed edge from the intended eye flow and surrounding composition instead of rotating RectTransforms to fake it.
- Put the `Selectable` and its target `FlexibleImage` on the same object and assign `FlexibleImage.Selectable`. Use the built-in FI states for hover/press/selection rather than unique materials.
- A single expanded mission establishes hierarchy more effectively than making every row tall. Compact rows can share geometry while varying status rail, text, and state color.
- Build the scrollbar from FI track, handle, and optional handle-core elements so its styling remains part of the same procedural system.

## Dense loadout and workshop screens

- Divide the interface into distinct jobs: a searchable inventory or data table, a central content viewport with UI overlays and assignment cards, and a narrower action/status rail. Let one region dominate instead of giving all three equal visual weight.
- Treat the vehicle, character, environment, or illustration shown through the viewport as external content. Flexible Image should build the viewport shell, reticle, leader lines, slot cards, masks, and controls around it—not approximate the subject itself. An empty or restrained placeholder is preferable when no source content was supplied.
- Give dense text explicit left and right column boundaries. Place left-aligned rects from a left inset and right-aligned rects from a right inset; do not reuse a center coordinate as though it were an edge. Keep visible gutters between labels, meters, numeric values, rails, and panel borders.
- Reuse one semantic accent for the same equipment or system family across its inventory rail, viewport assignment card, leader, and status. This creates cross-panel association without adding an effect to every row.
- Keep repeated inventory rows mostly flat: one accent rail, a strong item name, a quieter type line, and fixed numeric columns are usually enough.

## Gradients and blur

- For quiet depth, use low-alpha blended Angle or off-edge inverted Radial gradients. Keep the defining band or origin outside the visible rect.
- If a large translucent overlay improves readability, prefer ending it on an existing structural boundary or feathering it rather than leaving an arbitrary hard edge across focal scene content.
- Transparency does not imply Blur. Omit Blur when the authored backdrop remains legible and subordinate through alpha alone. Add Flexible Blur when a live or detailed background competes with foreground information or the requested treatment is explicitly frosted.
- If Blur is used, preserve the same foreground hierarchy; do not compensate for an unreadable composition by increasing every glow and outline.

## Verification

- Inspect at `1920 x 1080` and at least one different target such as `1280 x 800`.
- Confirm backdrop geometry remains visibly behind the shell and does not cross text, bars, indicators, or controls as if it were foreground content.
- Check that shell glow, shell border, viewport border, and row borders have visibly different roles.
- Verify selected tabs through their real Selectable/FI state link.
- Audit ordinary `UnityEngine.UI.Image` by exact runtime type. `FlexibleImage` inherits `Image`, so a broad `FindObjectsByType<Image>()` count is not an ordinary-Image count.
- Continue to use the shared Flexible Image material; the look does not require generated styling materials or panel sprites.
