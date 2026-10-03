# Flexible Image color Inspector map

Use this map when choosing values, not merely locating fields. Ranges describe the current Inspector; runtime setters may accept values outside them and should not be used to bypass the intended limits.

## Shared color-grid controls

Primary Color, Outline, Procedural Gradient, and Pattern use the same color-grid facility.

### Color Preset

| Control | Guidance |
|---|---|
| Color Preset | Optional shared `ColorPreset` containing one color for Primary, Outline, Procedural Gradient, and Pattern. It supplies palette targets, not grid dimensions, geometry, family configuration, or animation. |
| New | Creates and assigns a ColorPreset asset, then exposes its four colors inline. Use a project-owned asset path suitable for every intended consumer. |
| Preset foldout | Edits the assigned preset itself. Changes affect every referencing Flexible Image, so duplicate the asset before a one-off variation. |
| Preset Mix | Per-section blend from the evaluated local grid (`0`) to that section's preset color (`1`). It is shared configuration and does not animate by substate. |

### Dimensions and cells

| Control | Range or choices | Guidance |
|---|---|---|
| Dimensions X/Y | `1` to `3` independently | `1 x 1` is flat. `2 x 1` and `1 x 2` are efficient horizontal and vertical gradients. Use `2 x 2` for corner lighting; reserve `3 x 3` for a controlled center or extra bends. |
| Color cells | Up to nine | Columns run left-to-right and rows top-to-bottom. The backing array always contains nine cells; shrinking Dimensions hides cells without deleting them. |
| Preset Mix | `0` to `1` when a ColorPreset is assigned | `0` is the local grid, `1` is the preset color, and intermediate values tint the local result toward the preset. Each color section has an independent mix. |

Dimensions are structural quad data and cannot vary by animation substate. Cells can animate. Keep code-authored color arrays at `ProceduralProperties.Colors1dArrayLength` (`9`), even for `1 x 1` grids.

For Outline, Procedural Gradient, and Pattern, `Alpha Is Blend` turns cell alpha into effect weight against the complete color result accumulated below that section. The relevant order comes from Flexible Image Global Settings. A low-alpha `1 x 1` color therefore mixes the entire effect subtly into changing lower colors without requiring a vertex-color gradient. Use a `2 x 1` or `1 x 2` grid only when that weight should vary spatially: keep the effect RGB stable, move alpha from `0` to the intended maximum, and rotate the grid for a diagonal fade. Unrelated endpoint hues add a color shift as well as a strength fade.

Right-click a cell for Copy/Paste, Paste All, Lighten/Darken, row/column copy and inversion when applicable, and whole-grid copy when both axes have multiple cells. Right-click the Primary Color header to copy/paste its complete grid or adjust every stored cell. Right-click an optional module header to copy/paste that module's complete configuration and animation values; its Lighten/Darken entries affect all nine stored section cells, including hidden ones.

### Advanced grid placement

Advanced controls appear when more than one cell is active.

| Control | Range or choices | Guidance |
|---|---|---|
| Wrap X/Y | Clamp, Repeat, Mirror, PingPong | Clamp holds edge colors. Repeat jumps back to the first interval. Mirror includes an endpoint interval before reversing. PingPong reverses continuously at each endpoint. Only axes with multiple cells matter. |
| Pos X/Y | Normalized UV offset | Moves sampling without moving geometry. Use small values first; repeated wrapping can turn an offset into animated travel. |
| Rotation | Degrees | Rotates the grid around the quad center, not the procedural shape. |
| Scale X/Y | Minimum `0.1` | Values below `1` sample more of the grid and can expose wrapping; values above `1` magnify a smaller central portion. |
| Reset | Button | Restores zero position/rotation, unit scale, and Clamp wrapping when structural settings are editable. |

Grid cells, position, rotation, and scale can vary by animation state. Dimensions, wrapping, and Preset Mix are shared configuration.

### Cross-element screen-space effects

Screen Space changes the procedural mask coordinates, but each element still supplies object-level vertex colors. For a continuous gradient or pattern across several controls:

- Match the procedural family and all its parameters.
- Match the section's dimensions, cells, offset, rotation, scale, wrapping, and Preset Mix across elements and animation states that should remain continuous.
- Prefer a matching `1 x 1` section color when the procedural mask itself should provide the cross-element progression. A multicolor vertex grid restarts on every quad and can create a boundary even when copied identically.
- Keep the underlying Primary Color treatment compatible; a different base color changes the composited result even when the screen-space mask matches.

### Vertex color geometry

Any grid larger than `1 x 1` exposes shared mesh controls:

| Control | Range or choices | Guidance |
|---|---|---|
| Mesh Subdivisions | `0` to `5` | Adds interpolation vertices. Start at `0`; add only when a color grid needs more control. Values above `3` are intended mainly for large static elements that are not dirtied regularly. |
| Topology | Original, Flipped, X | Original and Flipped choose opposite quad diagonals and can reveal different interpolation bias. X adds a center and is usually more symmetric at additional vertex cost. |

## Primary Color

| Control | Range | Guidance |
|---|---|---|
| Color grid | Shared grid controls | Supplies the base color under Outline, Procedural Gradient, and Pattern. Use `1 x 1` unless vertex interpolation is part of the design. |
| Fade | `0` to `255` | Visibility independent of color alpha: `0` hides the primary layer and `255` shows it fully. With Flexible Blur integration it controls how much primary color is drawn over the blur. Use alpha when compositing transparency matters; use Fade to suppress only this layer. |
| Preset Mix | `0` to `1` | Blends the evaluated grid toward the ColorPreset primary color. |

## Outline

The Outline module and global Outline feature must both exist. Width `0` is effectively off.

| Control | Range or choices | Guidance |
|---|---|---|
| Color grid | Shared grid controls | Colors the outline. A transparent outline remains transparent unless Alpha Is Blend changes the alpha meaning. |
| Width | `0` to `511.875` canvas units | Start around `1–4` for a crisp UI border. Larger widths support bold rings, glow regions, and soft shadows. |
| Alpha Is Blend | Boolean | Off: outline alpha is transparency. On: alpha mixes outline RGB into the accumulated lower treatment. A low-alpha `1 x 1` outline gives uniform restrained blending; a transparent-to-visible grid makes that strength vary spatially. |
| Add Interior Outline | Boolean | Adds a second outline at the Stroke boundary. It is meaningful when Stroke hollows the shape; use it to border both outside and inside edges. |
| Expand Outwards | Boolean | Grows the outline beyond the ordinary shape instead of consuming interior space. Useful for glows and shadows; check layout clipping and raycast handling. |
| Accommodate Skew | Boolean, shown with expanded outline/skew | Enlarges the expanded outline enough to avoid clipping collapsed-edge corners. It also pushes vertex gradients farther outside the original border, so recheck multicolor grids. |
| Fade To Perimeter | Boolean | Fades toward the outer perimeter. Combine with Expand Outwards and a wide outline for glow/shadow treatments. |
| Massage Chamfer | Boolean | Adjusts chamfer to keep outline thickness visually consistent and reduce Mach-band artifacts. It has no effect when outline direction is zero; compare on/off when thick outlines meet strong corner curvature. |
| Preset Mix | `0` to `1` | Blends outline grid color toward the preset outline color. |

In dense nested interfaces, reserve the brightest or widest outline for focus, selection, or the primary action. Let inactive rows and large containers rely more on fill value, spacing, or a quieter structural edge. Equal-strength outlines around every level flatten hierarchy and create visual noise.

For a glow or shadow, begin with a wide expanded Outline, `Fade To Perimeter`, `Massage Chamfer`, `Alpha Is Blend`, and a `1 x 1` effect color. Its alpha controls the overall blend strength against the lower surface. Add a `2 x 1` or `1 x 2` alpha ramp only when the treatment should be directional. `Massage Chamfer` is valuable here because expansion changes the corner relationship. A non-expanded Outline can produce a clean inset edge glow with `Fade To Perimeter` and Blend; it does not need `Massage Chamfer` unless the rendered corner thickness actually requires correction.

## Procedural Gradient

The Procedural Gradient module, its global parent feature, and the selected family must all be enabled. An SDF Cutout makes the entire Procedural Gradient section unavailable on that quad.

### Common controls

| Control | Choices | Guidance |
|---|---|---|
| Color grid | Shared grid controls | Supplies the secondary color applied by the procedural mask. A single color makes the family controls easiest to reason about. |
| Region | Interior, Outline, All | Outline needs the global Outline feature and nonzero Width. All applies to both regions. |
| Alpha Is Blend | Boolean | Off: gradient alpha is transparency. On: alpha is the mix weight against the complete lower color result at this point in the configured section order. Use a low-alpha `1 x 1` color for uniform tint strength or an alpha grid for a spatial taper; leave Blend off for genuinely translucent light or gaps in coverage. |
| Family | SDF, Angle, Radial, Conical, Noise | Selects the mask generator. Only globally enabled families can be chosen. |
| Invert | Boolean | Swaps selected and unselected parts of the mask. |
| Screen Space | Boolean, global option required | Uses screen coordinates. Match participating objects as described above. |
| Pointer behavior | Global option required | Angle/Radial/Conical use `Pointer Adjusts Pos`; SDF/Noise use `Revealed By Pointer` plus Reveal Strength. Verify in Play mode. |

Procedural Gradient applies a small interleaved-noise offset to the final gradient blend value: roughly one `1/255` step peak-to-peak. This automatic dithering has no Inspector toggle and makes Angle and Radial preferable to a multicolor Primary grid for subtle low-contrast ramps. For quiet surface shading, use a `1 x 1` base, a `1 x 1` related secondary color, `Alpha Is Blend`, and secondary alpha around `.08–.25` as a starting point.

### SDF

SDF selects a distance band measured inward from the procedural perimeter:

- Outer Position moves the outside boundary inward.
- Inner Position moves the inside boundary inward; the Inspector keeps `Outer ≤ Inner`.
- Outer Reach softens the outer boundary; Inner Reach softens the inner boundary.
- With Outer near `0` and a finite Inner, the result is an edge band. Raising Outer turns it into an inset band. Invert selects everything outside the band.

| Control | Range | Guidance |
|---|---|---|
| Pos O/I | `0` to `4095.875` local canvas-distance units | Use values that fit the rect. On a 150-unit-tall card, useful positions are often `0–75`; thousands are for extremely large geometry. |
| Reach O/I | `0` to `1` | Controls the transition width in UI geometry space, so its apparent width depends on the RectTransform and Canvas setup. Internally the normalized value is multiplied by `2160` before comparison with the local SDF distance; this is not a fixed pixel width. Begin around `.002–.01` for ordinary controls. Values like `.1–1` can soften across the whole rect. Screen Space uses a different distance basis, so retune after switching modes. |
| Invert | Boolean | Selects the complementary distance region. |
| Screen Space | Boolean | Replaces object SDF distance with distance to screen edges. This is a different mask, not merely cross-object alignment. |
| Revealed By Pointer | Boolean | Modulates the SDF mask from pointer position. |
| Reveal Strength | `0` to `1`, default `.5` | Controls pointer reveal contribution. |

Start tuning with one bright gradient color over a dark flat Primary Color, hard reaches, and clearly separated Outer/Inner positions. Add reach only after the band is visible.

### Angle

| Control | Range | Guidance |
|---|---|---|
| Pos X/Y | `-0.5` to `1.5` | Places the band center. Values outside `0–1` create edge-biased or off-object lighting. |
| Size L/R | `0` to `1` independently | Controls falloff width on the two sides. Near `0` is tight/hard; values toward `1` broaden and flatten the falloff. Equal values make a balanced band; unequal values make a hard-leading/soft-trailing treatment. |
| Angle | Degrees | Rotates the band. Use visible increments such as `45` or `90` while establishing direction, then refine. |
| Aspect Correction | Boolean | Compensates the angle for a non-square rect. |
| Pointer Adjusts Pos | Boolean | Drives position from the pointer. Object-space pointer gradients can show discontinuities at `≥3` subdivisions, or `2` with X topology; lower subdivisions or Screen Space avoids that edge case. |

Angle measures absolute distance on both sides of its center band. Any part of that zero-distance line that crosses the rect can read as a seam, even when Pos itself is outside because rotation can bring the line through a corner. For a subtle one-way wash, move the whole band beyond the relevant edge or corner, keep only one falloff flank visible, use Size toward the upper end of its range, and use low-alpha Blend color. Centered Angle is better treated as an intentional band effect.

An intentional horizontal overline or underline is a useful centered-band case. On a single Flexible Image, use Angle `90°`, Pos X `.5`, Pos Y near `.025` for the lower edge or `.975` for the upper edge, `Interior` only, `Alpha Is Blend` on, and `Invert` off. For a `76–96`-unit-tall control, equal Size L/R around `.025–.06` produces a narrow balanced rule; unequal values such as `.085/.025` produce an asymmetric fade. These are rendered starting points, so retune at the final Canvas scale. If the rule must be a crisp outline edge rather than a blended band, use Outline plus outline-only Simple Cutout instead of adding a child Graphic or Multiple quad.

### Radial

| Control | Range | Guidance |
|---|---|---|
| Pos X/Y | `-0.5` to `1.5` | Center of the radial mask. Off-rect positions are useful for edge highlights. |
| Size X/Y | `0` to `1` | `0` collapses the lobe; `.2` is a small spot, `.5` a medium lobe, and `1` can cover most of a normal rect. |
| Gradient Strength | `0` to `1`, default `.5` | Shapes the falloff curve rather than acting as simple opacity; `0` is effectively off, `.5` is roughly linear, and larger values push a non-inverted tint toward the edge while strengthening the complementary inverted center. Establish Size first, then tune around `.35–.7`. |
| Aspect Correction | Boolean | With equal X/Y size, On produces a circle on a non-square rect; Off produces an ellipse stretched with the rect. |
| Pointer Adjusts Pos | Boolean | Makes the lobe follow the pointer; verify Canvas render mode and camera in Play mode. |

With `Invert` on, Radial selects the area nearest its origin; with it off, Radial selects away from the origin. A centered origin exposes the radial structure and can read as a spotlight, ring, or dark well. For subtle surface lighting, move Pos outside the rect so only a broad flank enters the component, use a large Size and low-alpha Blend color, then tune Strength. On a wide panel, Pos `.08/1.05`, Size `.95/.9`, Strength `.45`, Invert on, and alpha `.2` are a proven off-edge starting point. Use centered highlights or vignettes only when their visible focal structure is intentional.

### Conical

| Control | Range | Guidance |
|---|---|---|
| Pos X/Y | `-0.5` to `1.5` | Center of angular rotation. Move outside the rect for edge-origin sweeps. |
| Strength | `0` to `1`, default `.5` | Tail/coverage strength. `0` is effectively off. |
| Curvature | `-1` to `1` | `0` is a conventional conical wedge. Negative and positive values bend in opposite directions; either extreme can form strong spiral or concentric structures. |
| Angle | Degrees | Rotates the seam/tail around the chosen position. |
| Pointer Adjusts Pos | Boolean | Moves the conical origin with the pointer. |

For a circular gauge, start with a square circular shape hollowed by Stroke, retain a dim Primary track, and apply Conical to the Interior. Strength controls angular coverage and Angle rotates the seam/start. Establish each visual dimension separately: Curvature `0` plus a `1 x 1` color gives the clearest conventional meter; Curvature `0` plus a carefully rotated `2 x 2` or `3 x 3` grid can place several hues around that conventional ring; a nonzero Curvature plus `1 x 1` bends the sweep into a spiral or gun-barrel treatment. The multidimensional grid is a spatial field over the component rather than ordered stops along the arc. Combine spatial multicolor and Curvature only when the hybrid is deliberate and both parts remain readable at `1:1`.

### Noise

| Control | Range | Guidance |
|---|---|---|
| Strength | `0` to `1`, default `.5` | Overall noise-mask contribution. `0` is effectively off; high values can overfill the object. |
| Scale | `0` to `1`, default `.5` | Sampling frequency. Higher values produce finer, more frequent noise; lower values produce larger regions. |
| Edge | `0` to `1`, default `.5` | Shapes distribution/threshold hardness. Tune after Scale and Strength because their interaction changes perceived contrast. |
| Seed | `0` to `32767` | Chooses a repeatable variation; it is not animation time. |
| Alternate Mode | Boolean | Selects the alternate hash/sign treatment. Compare visually rather than treating it as a quality level. |
| Revealed By Pointer | Boolean | Applies the pointer-reveal path to noise. |
| Reveal Strength | `0` to `1`, default `.5` | Controls reveal contribution. |

## Pattern

The Pattern module, global Pattern feature, and selected family must all be enabled.

Pattern is for repeated texture and rhythm, not the default way to make a surface feel less flat. Use Angle or Radial for subtle depth. Choose a family by context:

| Family | Good fits | Usually avoid |
|---|---|---|
| Line | Scanners, telemetry, speed, signal flow, restrained scan lines | Generic card fills where the lines communicate nothing |
| Shape | Targeting, repeated pips/rings, radiating markers, emblematic motifs | Dense text backgrounds and broad neutral panels |
| Grid | Maps, inventory cells, schematics, technical surfaces | Decorative coverage across every panel |
| Fractal | Energy, distortion, magic, turbulence, focal technical effects | Quiet backplates and readability-critical controls |
| Sprite | Authored icons, branding, or a deliberate repeating motif | Placeholder texture without a suitable source sprite |

### Common controls

| Control | Choices | Guidance |
|---|---|---|
| Color grid | Shared grid controls | Supplies pattern color. Opaque black over a dark base can make a working pattern look absent. |
| Region | Interior, Outline, All | Outline needs global Outline and nonzero Width. |
| Alpha Is Blend | Boolean | Off uses alpha as transparency. On mixes Pattern RGB against the complete lower color result. A low-alpha `1 x 1` color uniformly subdues the Pattern across varied lower colors; a transparent-to-visible grid additionally tapers it across the component. |
| Screen Space | Boolean | Aligns pattern coordinates across components. Match all participating settings and colors. |
| Soft | Boolean, unavailable for Sprite | Softens procedural pattern edges. It does not blur the whole component. |

Density is `0–1` for every family: `0` is effectively off; larger values create more repetitions. Establish Density before the family-specific thickness/fill control.

When a Pattern should add surface texture rather than become the subject, start with one low-alpha color and `Alpha Is Blend`. This lets the same lines, cells, or fractal detail inherit the visual variation already established by Primary Color and lower effects. Lower Density or thickness if the motif still dominates; use a larger color grid only when a spatial fade is itself part of the design.

### Line

| Control | Range or choices | Guidance |
|---|---|---|
| Orientation | Horizontal, Vertical, Slash, Backslash | Chooses line direction. Slash and Backslash use opposite diagonals. |
| Density | `0` to `1` | Number/frequency of bands. |
| Line Thickness | `0` to `255`, default `127` | `0` is absent, about `127` is half duty, and `255` is solid. Start near `64–127`. |
| Speed | Approximately `-1` to `1` | Sign chooses travel direction; magnitude chooses speed. Verify in Play mode or time-separated captures. |
| Static Offset / Offset | Same stored value as Speed | Static mode repurposes Speed as stationary phase; it does not preserve a separate speed value. |

For requested moving stripes around an interactable, restrict Pattern to the Outline region, use Slash or Backslash, and leave Interior off. A `3–6` unit outline, Density `.25–.5`, Line Thickness `55–90`, Speed magnitude `.08–.35`, `Alpha Is Blend`, and local rather than Screen Space coordinates are useful starting points; tune at final size and verify two distinct runtime frames.

### Shape

| Control | Range or choices | Guidance |
|---|---|---|
| Shape | Diamond, Circle, Square, Cross | Chooses the repeated distance contour. |
| Origin | Center, Left, Right, Top, Bottom | Sets the contour source. Left/Right turn Diamond contours into horizontal chevrons; Top/Bottom do the same vertically. Center produces centered diamonds instead of one-way flow. |
| Density | `0` to `1` | Repetition frequency. |
| Line Thickness | `0` to `255`, default `127` | Width/duty of the contour line. This family uses Line Thickness, not the backing `PatternCellParam`. |
| Speed or Static Offset | Approximately `-1` to `1` | With Static Offset off, positive Speed moves phase away from the selected origin and negative Speed moves it toward the origin. Static mode fixes the same stored value as phase. |

For a charge or transfer indicator, use Diamond with a source-edge Origin, establish Density before Line Thickness, and verify motion in two rendered frames. On a wide `1100 x 104` test bar, Density `.018–.022` and Line Thickness `90–110` gave broad chevrons; retune for the actual rect and Canvas. A `1 x 1` Pattern color keeps direction in the motion alone. With `Alpha Is Blend`, a `2 x 1` or `1 x 2` same-RGB alpha grid can make the source quiet and the destination stronger without fading the lower stack; place the low-alpha endpoint at the selected origin and reverse the grid when the origin changes.

### Grid

| Control | Range or choices | Guidance |
|---|---|---|
| Grid | Diamond, Square, Diagonal, Cardinal | Chooses cell/lattice orientation. Compare at moderate Density because high frequency can obscure the distinction. |
| Density | `0` to `1` | Cell frequency. |
| Fill | `0` to `1`, default `.5` | `0` is empty; larger values fill more of each cell. This is the Grid meaning of `PatternCellParam`. |

### Fractal

| Control | Range | Guidance |
|---|---|---|
| Density | `0` to `1` | Repetition frequency. |
| Line Thickness | `0` to `255`, default `127` | Width/duty of the fractal lines. |
| Fractal | `0` to `1`, default `.5` | Changes internal cell structure. Some center combinations can be visually neutral, so test around `.25` and `.75` rather than assuming `.5` proves the effect is absent. |

### Sprite

| Control | Range or choices | Guidance |
|---|---|---|
| Source Image | Sprite | Tiles the Flexible Image Source Image. Prefer an unpacked sprite and Image Type Simple or Filled; packed atlas UVs and Sliced/Tiled behavior can produce unexpected sampling. |
| Rotation mode | Sprite, Offset | Sprite rotates each tile by Rotation. Offset uses fixed travel directions. |
| Rotation | Integer degrees | Available in Sprite mode unless integrated blur reserves the packed data. |
| Offset direction | `0`, `45`, `90`, `135` degrees | Available in Offset mode; selects the tile travel axis. |
| Density | `0` to `1` | Tiling frequency. |
| Speed or Static Offset | Approximately `-1` to `1` | Moves or phases the tiled sprite. |

With Flexible Blur integration, Pattern Line Thickness and Sprite Rotation may be unavailable because those packed channels are used for blur alpha behavior. Follow the Inspector warning; do not force private serialized data.
