# Flexible Image animation Inspector map

Flexible Image stores five animation-state containers per quad. Each container holds an ordered list of procedural property snapshots called substates.

## Driver and states

| Control | Choices | Guidance |
|---|---|---|
| Driven By | Selectable, Script | Selectable reads a same-object uGUI Selectable. Script reads `FlexibleImage.scriptDrivenAnimationState`. |
| Selectable | Component reference | Assign the Button, Toggle, or other Selectable whose interaction state should drive the image. Its target Graphic can be the Flexible Image. |
| Highlighted Fix | Boolean | Allows Selected to return to Highlighted while the pointer remains over the control, a transition Unity Selectable normally omits. Disable only when ordinary Selectable behavior is preferred. |
| State buttons | Normal, Highlighted, Pressed, Selected, Disabled; `0–4` in Script mode | Script indices are clamped to a maximum of `4`; a negative index suppresses script-driven updates. Keep a stable meaning for each index in gameplay code. |

Right-click a state button to copy, paste, or clear that state's complete substate sequence. The same menu can copy/paste all five states. Module data still has to exist on the quad; pasting animation values does not add a missing module.

## State playback

| Control | Range or choices | Guidance |
|---|---|---|
| Playback | Once, Repeat, PingPong | Once stops on the final substate. Repeat jumps from the end back to Start Idx. PingPong reverses through the looped span. |
| Start Idx | Existing substate index | First substate included after a repeating or ping-pong sequence reaches the end. Use a later index to keep an entrance animation outside the idle loop. |
| Preview play/stop | Editor buttons | Plays the selected state's sequence without entering Play mode. It is disabled when no useful positive-duration sequence exists. |
| Unwind Idx | `-1` or an existing substate index | `-1` disables unwind. Otherwise, leaving this state reverses toward the beginning of the chosen substate before transitioning. It does nothing if playback is already earlier than that point. |
| Unwind Rate | Positive multiplier; default `1` | `2` unwinds twice as fast; runtime clamps the minimum effective rate to `.01`. |

Unwind reaches the beginning of its target substate, which is also the ending value of the preceding substate. Choose the index based on that boundary, not merely the visible list label.

## Substates

| Control | Guidance |
|---|---|
| List order | Defines the sequence. State entry begins by interpolating from the last reached properties into substate `0`, then between successive substates. |
| Duration | Seconds used to transition into that substate. The shipped default is `.1`. `0` snaps to its values and can make a sequence visually untestable. |
| Interpolation | Easing stored on the destination substate. It shapes the transition into that substate. |
| Add | Clones a complete property set rather than creating an uninitialized visual state. Keep module animation data and nine-cell color arrays intact. |
| Remove/Reorder | Changes playback, loop, and unwind indices. Recheck all three after editing the list. |

Interpolation choices are:

- Linear: constant-rate change.
- Quadratic Ease In, Out, In Out: moderate acceleration/deceleration.
- Sine Ease In, Out, In Out: gentle natural easing.
- Circular Ease In, Out, In Out: stronger rounded acceleration near an endpoint.
- Quintic Ease In, Out, In Out: very strong hold-and-release or settle behavior.

Use Linear for progress-like motion, Sine/Quadratic for ordinary UI response, Circular for a pronounced snap, and Quintic only when a dramatic endpoint bias is intentional.

## Motion patterns from Animated Button Ideas

The shipped `Demos/FlexibleImage/Scenes/Animated Button Ideas.unity` scene demonstrates reusable motion grammars. Adapt the relationships to the requested style rather than copying its colors or exact shape.

| Treatment | Construction | Why it reads |
|---|---|---|
| Anticipate and settle | `Parallelogram Button` enters Highlighted through two substates: `.05 s` Circular Ease In, then `.1 s` Circular Ease Out, with the later state offset upward. | The quick first beat and slower settle make hover feel like motion rather than a tint swap. |
| Intro plus idle sweep | `Strobing Button` uses Normal/Repeat with three substates and `Start Idx = 1`: a `.1 s` entrance followed by `1.25 s` and `.75 s` Angle-gradient positions on opposite outside corners. | The entrance runs once while only the traveling-light span repeats; Pattern is not required for ambient motion. |
| Pressed energy reveal | `Pointer Pressed Glow` keeps Radial Gradient size and alpha at zero until Pressed, then grows Size to about `.85` and reveals its color over `.25 s` Quintic Ease Out. | Press causes a decisive new light event instead of merely changing the permanent fill. |
| Layered parallax | `Animated Shadow Button` moves its shadow quad from `(2,-2)` to `(6,-6)` while its face moves from `(0,0)` to `(-4,4)` over `.1 s`. | Opposing offsets increase apparent separation and depth while the layout slot stays fixed. |
| Reticle morph | `Reticle Button` coordinates Size Modifier, Simple Cutout dimensions, and Pattern color between Normal, Highlighted, and Pressed; hover uses `.15 s` Circular Ease In Out and press `.16 s` Quintic Ease Out. | Geometry and contrast reinforce the same focus/press action. |
| Edge-light focus | `Pointer Edge Shine` coordinates Size Modifier, corner chamfer, and Angle-gradient width/alpha on hover, then darkens the fill and introduces Outline on press. | The highlight appears to travel around and tighten the silhouette instead of covering it with a flat color. |

Use these as starting structures. Not every control needs multiple quads or three animated modules; prefer the smallest combination that makes the requested action legible.

## What can animate

Substates hold `ProceduralProperties`, so these values interpolate:

- Primary color cells, Fade, grid offset/rotation/scale.
- Outline colors, Width, and outline-grid offset/rotation/scale when Outline exists.
- Procedural Gradient colors and the selected family's numeric position, size, strength, reach, seed, curvature, angle, and grid transforms when Gradient exists.
- Pattern colors, density, speed/static offset value, cell/fill/fractal value, line thickness, sprite rotation, and grid transforms when Pattern exists.
- Corners, Softness, Offset, Size Modifier, Rotation, and UV Rect.
- Skew amount/position and collapsed-corner values when Skew exists.
- Stroke width when Stroke exists.
- Simple and SDF cutout numeric geometry when Cutout exists.

Configuration remains shared rather than interpolated: module existence, gradient/pattern/cutout family, region flags, booleans and enum modes, color dimensions/wrapping/Preset Mix, anchors/pivots, mesh subdivisions/topology, and quad ordering. Author those once in default properties, then animate the numeric/color values they expose.

## Built-in animation versus other systems

- Use built-in state animation for procedural visual response, looping substates, and independent repeated controls such as pips.
- Use `AnimationClipAdapter` when an Animation Clip must bind to exposed component fields. Set `quadIdx` after final quad ordering.
- Use Transform/Animator animation for unrelated motion, events, audio, or components outside Flexible Image.
- Add `GraphicFollower` or `TextMeshProFollower` only when a child Graphic's vertices should follow Flexible Image deformation; ordinary parenting is enough for rigid movement.

## Runtime script guidance

Set `animationMode` to `FlexibleImage.AnimationStateDrivenBy.Script` and change `scriptDrivenAnimationState` only when the desired state changes. The component owns interpolation time. Repeated Flexible Images each keep independent runtime progress even when they share a QuadDataPreset.

When authoring states in editor code, clone `DefaultProceduralProps` or another complete substate, then change only intended fields. A blank `new ProceduralProperties()` has uninitialized colors and no enabled-module animation data.
