---
name: flexible-image-animate-interactive-ui
description: "Use this skill whenever the user wants Flexible Image interaction or procedural animation — e.g. 'animate this button on hover', 'make the press state squish', 'animate adding health pips', 'drive the image from script', 'loop this gradient', 'add substates', or 'make text follow the deformation'. Covers Selectable- and script-driven states, substates, playback and unwind settings, editor preview, AnimationClipAdapter, and Graphic/TMP followers. Do NOT use for static styling (see flexible-image-create-color-effects or flexible-image-create-shapes-and-cutouts). When procedural UI should react or move, use this skill."
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

# Animate Interactive Flexible Images

Animate procedural values directly between states, drive them from a Unity Selectable or script, and keep child graphics aligned with the deformation.

## When to use this skill

- "animate this Flexible Image button on hover and press"
- "loop a moving pattern while selected"
- "switch procedural states from code"
- "animate pips being added to or removed from a meter"
- "make the label follow skew and rotation"
- "adapt an Animation Clip to Flexible Image data"

Not for:

- Static color design; see `flexible-image-create-color-effects`.
- Static shape design; see `flexible-image-create-shapes-and-cutouts`.
- Ordinary Transform animation that does not change Flexible Image data.

## Prerequisites

- Flexible Image `3.0.0`, Unity `2022.3.62` or newer, and any supported render pipeline.
- Confirm `JeffGrawAssets.FlexibleUI.ProceduralAnimationState` and `JeffGrawAssets.FlexibleUI.FlexibleImage` resolve.
- Every optional module animated by a state must exist in that QuadData and have its corresponding global parent feature and selected subfeature enabled. Animation data does not enable shader support.
- For Selectable-driven animation, place a `Selectable` such as Button on the same GameObject and assign it in the Flexible Image Inspector.
- For TMP following, TextMesh Pro must be present so `TextMeshProFollower` compiles.
- If Flexible Image is absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-image-procedural-ui-that-always-batches-338652).

## Quick start

1. Add or select a Button whose target Graphic is a Flexible Image.
2. In the Flexible Image animation section, set `Driven By` to `Selectable` and assign the Button.
3. Decide what the interaction should communicate: hover may lift or illuminate the control; press may compress it or reveal energy at the pointer.
4. Select `Highlighted` and coordinate two related changes, such as Offset plus Size Modifier, or edge-light Gradient alpha plus corner shape. Add an anticipation and settle substate when the motion benefits from overshoot.
5. Select `Pressed` and make the response faster and more decisive than hover, such as compression plus darkening, or a hidden Radial Gradient growing into view.
6. Use the Inspector playback controls to preview transitions, then enter Play mode and move/press the pointer.

Expected result: the same Flexible Image interpolates between Normal, Highlighted, Pressed, Selected, and Disabled data without an Animator Controller.

## Workflows

### Workflow: Configure multi-stage state animation

**Goal:** Give one interaction state a sequence rather than one destination.

**Steps:**

1. Select a state and add procedural substates in the animation list.
2. Give each substate its own values, Duration, and Interpolation.
3. Set Playback to `Once`, `Repeat`, or `PingPong`.
4. For repeating modes, set `Start Idx` to the first substate included in the loop.
5. Set `Unwind Idx` and `Rate` when leaving the state should traverse toward a selected substate instead of snapping or taking the ordinary transition.
6. Preview the whole sequence in the Inspector before entering Play mode.

**Expected result:** the state follows the configured sequence and leaves it using the configured unwind behavior.

### Workflow: Choreograph a visible interaction

**Goal:** Make built-in animation read as deliberate motion rather than a property change that happens to interpolate.

**Steps:**

1. Choose a visual verb before choosing properties: lift, compress, illuminate, sweep, unfold, focus, or recede.
2. Animate two or three related signals that support that verb. Useful combinations include Offset plus Size Modifier, Size Modifier plus corner chamfer, primary color plus Outline, Radial Gradient size plus alpha, or cutout geometry plus a color change.
3. Keep hover and press distinct. Hover usually has room for anticipation and settle; press should respond more quickly and land clearly.
4. For multi-quad controls, animate the layers as one object. Moving a face and its shadow/backplate in opposite directions creates depth without moving the RectTransform.
5. For an ambient loop, use an entrance substate followed by a longer repeating span. Set `Start Idx` after the entrance. A slow Angle Gradient position sweep can supply moving light without defaulting to Pattern Speed.
6. Verify at least two intermediate runtime frames. Endpoint-only captures do not prove the timing, easing, overshoot, or independent playback.

**Expected result:** the animation has a readable action and hierarchy while the layout and RectTransform remain stable.

### Workflow: Drive states from script

**Goal:** Select procedural animation states without a Unity Selectable.

**Steps:**

1. Set `Driven By` to `Script`.
2. Author the numbered states in the Inspector and preview each state/substate.
3. At runtime, assign `FlexibleImage.scriptDrivenAnimationState` to the desired zero-based state index.
4. Change the value only when the requested state changes; the component performs interpolation internally.

```csharp
using JeffGrawAssets.FlexibleUI;
using UnityEngine;

public sealed class FlexibleImageStateDriver : MonoBehaviour
{
    [SerializeField] FlexibleImage image;

    public void SetState(int state)
    {
        image.scriptDrivenAnimationState = state;
    }
}
```

**Expected result:** changing the integer selects the configured procedural state in Play mode.

### Example: Build an animated pip meter

**Goal:** Give repeated health, charge, or capacity pips independent procedural add/remove reactions.

**Steps:**

1. Keep the scope to the requested meter; do not turn a standalone pip bar into a full HUD, card, or presentation unless asked. When pips require independent states, consider a Horizontal or Vertical Layout Group with one Flexible Image child per pip. A single multi-quad Flexible Image remains reasonable when the pips do not need independent state machines.
2. Reuse a pip `QuadDataPreset` when the children share styling and authored states; each Flexible Image still maintains its own runtime animation progress and script-selected state.
3. Consider Squircle corners when their softer profile suits the pip design; ordinary chamfer/concavity remains valid.
4. Set each independently controlled pip to Script mode. Use stable empty/filled states plus multi-substate add and remove reactions. For example, an add can brighten and expand past its final size before settling, while a remove can flash or recede before collapsing. Coordinate geometry with color, outline, or gradient rather than animating only one faint value.
5. When authoring states from code, clone `QuadData.DefaultProceduralProps` or another complete substate before changing values. A blank `new ProceduralProperties()` does not contain the initialized color grid or enabled module data.
6. Store each state's substates in `ProceduralAnimationState.proceduralProperties`; configure `playbackType`, `loopStartIdx`, `unwindToIdx`, and `unwindRate` on that state container.
7. Keep every procedural color array at `ProceduralProperties.Colors1dArrayLength` (nine cells), even when the visible grid is 1x1. Prefer `Set...Color` methods or a cloned complete substate over replacing an enabled module's array with a one-element array.
8. Keep each layout slot stable by animating Flexible Image data rather than Layout Group-owned RectTransform dimensions. Change `scriptDrivenAnimationState` only on the affected pips and stagger changes when a cascade is desired.
9. A shared Screen Space gradient or pattern can create a first-to-last progression across the row. Keep that section's color grid and grid settings identical between pips and across states that should remain visually continuous; see `flexible-image-create-color-effects`.

**Expected result:** the Layout Group remains stable while individual pips enter, leave, pulse, or settle through Flexible Image's own procedural state system.

Read [references/animation-inspector-map.md](references/animation-inspector-map.md) when choosing easing, loop/unwind indices, or deciding whether a value is animatable versus shared configuration.

### Workflow: Keep child graphics aligned

**Goal:** Apply the Flexible Image's final procedural transformation to a label or icon.

**Steps:**

1. Put the child Graphic or TMP text under the Flexible Image.
2. Add `GraphicFollower` to a uGUI Graphic, or `TextMeshProFollower` to TMP text.
3. Verify the follower resolves the intended parent Flexible Image.
4. Animate offset, scale, rotation, skew, or related deformation and confirm the child vertices follow.

**Expected result:** the child graphic follows the parent Flexible Image's mesh transformation while remaining a separate Graphic.

### Workflow: Animate through an Animation Clip

**Goal:** Expose modular Flexible Image values as ordinary animatable component fields.

**Steps:**

1. Add `AnimationClipAdapter` to the same GameObject as the Flexible Image.
2. Set `quadIdx` to the target quad index.
3. Animate the adapter's exposed fields in an Animation Clip.
4. Ensure every module the clip changes exists on that quad; the adapter cannot animate data for a missing modular section.
5. Test the clip in Play mode and after prefab instantiation.

**Expected result:** changed adapter fields synchronize into the chosen quad without requiring direct animation bindings to nested modular YAML paths.

## Verification

- The requested state is visibly distinct from Normal.
- The interaction communicates a deliberate action through coordinated properties; a barely visible single-value change is insufficient when richer motion was requested.
- Every substate has a non-negative duration and the intended interpolation.
- Repeat/PingPong start and unwind indices point to existing substates.
- Script mode changes when `scriptDrivenAnimationState` changes.
- Repeated script-driven elements animate independently; changing one pip does not restart every pip.
- Runtime captures or observation show intermediate motion, not only different endpoint states.
- Followers have the required Graphic/TMP component and track the intended Flexible Image.
- AnimationClipAdapter `quadIdx` is less than `ActiveQuadDataContainer.Count`.
- No invalid quad-index warnings or runtime exceptions appear.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `FlexibleImage.animationMode` | `FlexibleImage.AnimationStateDrivenBy` | Chooses Selectable or Script state input |
| `FlexibleImage.scriptDrivenAnimationState` | `int` | Selects the active state in Script mode |
| `QuadData.DefaultProceduralProps` | `ProceduralProperties` | Complete initialized property snapshot suitable for cloning into authored substates |
| `ProceduralAnimationState.proceduralProperties` | `List<ProceduralProperties>` | Ordered substates for one Normal/Highlighted/Pressed/Selected/Disabled or custom script state |
| `ProceduralAnimationState.PlaybackType` | enum | Once, Repeat, or PingPong playback |
| `ProceduralProperties.Colors1dArrayLength` | constant (`9`) | Required backing-array length for every procedural color section |
| `AnimationClipAdapter.quadIdx` | `int` | Chooses the quad receiving adapted clip values |
| `GraphicFollower` | component | Applies Flexible Image deformation to a uGUI Graphic |
| `TextMeshProFollower` | component | Applies Flexible Image deformation to TMP text |

## Common issues

- **Hover does nothing** → No Selectable is assigned, or Driven By is Script → Assign the Selectable and use Selectable mode.
- **`AnimationStateDrivenBy` does not compile** → The enum is nested → Use `FlexibleImage.AnimationStateDrivenBy.Script`.
- **A code-authored state renders transparent or lacks a module** → It began as a blank `ProceduralProperties` → Clone `DefaultProceduralProps` or a complete authored substate, then modify the clone.
- **`ProceduralProperties.Copy` throws while copying an enabled color module** → A backing color array was replaced with fewer than nine cells → Preserve the nine-cell array and set its used cells instead.
- **A state snaps** → Its duration is zero or the transition is being previewed at its endpoint → Set a positive Duration and test the transition.
- **A loop starts at the wrong point** → Start Idx targets the wrong substate → Set it to the first repeated substate.
- **The adapter warns about an invalid index** → The selected quad was removed or reordered → Correct `quadIdx` after confirming the active data mode.
- **An adapted module never appears** → Its v3 module data is absent or its global feature/subfeature is disabled → Add the module explicitly and confirm the required global switches before animating it.
- **Child text does not deform** → It lacks the matching follower component → Add `TextMeshProFollower` rather than `GraphicFollower` to TMP text.

## Boundaries

- Procedural state animation is not an Animator Controller replacement for unrelated transforms, audio, events, or arbitrary components.
- `scriptDrivenAnimationState` is a runtime selector, not durable application state.
- AnimationClipAdapter exposes a fixed compatibility surface and targets one quad by index; reordering quads can require updating it.
- Followers deform vertices; use normal RectTransform parenting when only rigid layout following is needed.
