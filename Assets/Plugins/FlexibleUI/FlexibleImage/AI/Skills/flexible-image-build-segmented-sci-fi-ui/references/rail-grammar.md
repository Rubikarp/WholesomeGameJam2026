# Segmented Rail Grammar

Treat the interface as a routed rail system, not a collection of pills around content.

## Parts

- **Spine:** a vertical run that carries stacked segments.
- **Arm:** a horizontal run leaving an elbow.
- **Elbow:** the continuous turn between spine and arm, with one convex outer bend and one concave inner bend.
- **Seam:** a deliberate, consistent black gap between segments.
- **Terminal:** the exposed end of a run. A terminal may be rounded, pill-capped, or asymmetrical.

## Join rule

Before setting corners, classify each short edge:

- **Join:** another rail element continues past this edge. Set both corners on this edge to square. The adjacent element's mating edge must also be square. Match their thickness and alignment; use either no gap or the same narrow seam used elsewhere in that run.
- **Terminal:** the run ends at this edge. Round only the two exposed corners, or use another intentional cap treatment.

A rounded edge touching a square continuation is a broken join. In particular, the bottom edge of an upper-left elbow leg remains square when a vertical navigation stack continues beneath it; the first navigation segment's top edge is square as well.

Free-standing controls may be pills. Rail segments generally are not pills unless the entire segment is visually detached from the rail skeleton.

## Softened joins

- For separate touching segments, begin with square mating edges, matching Bidirectional Softness around `.5–1`, and no geometric overlap. The paired fades meet across the boundary for smooth continuous coverage; two Inwards fades can reveal the backdrop even when their RectTransform boundaries match exactly.
- Inspect every join at 1:1 at each target resolution. Canvas scaling can move an exact UI boundary between physical pixels; `Pixel Perfect` can help some static screen-space layouts but does not fix an unsuitable feather mode or excessive Softness.
- If a hairline remains, add only the smallest overlap that removes it. Probe small fractional canvas-unit steps such as `.25` then `.5`; these are starting points, not a fixed recipe. Larger overlaps make sibling order and alpha blending define the seam.
- Prefer one continuous Flexible Image or SDF-cutout elbow where practical. Use Outwards only where expanded coverage cannot overpaint adjacent colors, text, masks, or translucent effects.

## Elbows

- Preserve rail thickness through the bend.
- Use a clear convex outer radius and a smaller concave inner radius rather than rounding every corner of an L-shaped block.
- A one-piece SDF-cutout elbow avoids overlap seams. Extend the cutout through the two open edges.
- Keep any arm/spine continuation edges square even though the elbow's exposed outer corner is rounded.
- For a composite elbow, butt the arm and spine together on square edges; do not overlap translucent pieces.

## Proportion and hierarchy

- Preserve rail thickness, elbow radius, terminal radius, and seam width across responsive layouts. Adapt run lengths and content space instead of uniformly scaling the whole rail system.
- Let rails organize navigation, status, and content regions. Decorative rails that do not explain hierarchy weaken the design language.
- Use flat high-contrast colors against black or near-black negative space. Vary segment length and color by function or state rather than producing a uniform button grid.
- Keep labels aligned within their owning segment. Dense small numerics may support the system readout, but primary controls remain legible.

## Visual audit

Trace each rail from one terminal to the other:

1. Thickness remains coherent through every segment and elbow.
2. Both sides of every seam arrive square and aligned.
3. Only genuine terminals are rounded.
4. Elbow legs do not end in rounded corners when the rail visibly continues.
5. Black gaps are intentional and consistent, not accidental misalignment or inward feathering.
