# Single-camera staged UI stacking

Use this reference only when ordinary Flexible Blur layer settings cannot preserve sharp UI between blur stages.

## Why staging is required

A FlexibleBlurFeature can only capture what the camera has rendered before that feature executes. Ordinary Canvas rendering does not interleave arbitrary UI groups with several captures. URP Render Objects features provide those explicit draw stages.

## Renderer outline

For three stages, the relevant portion of UniversalRendererData should be ordered like this:

1. FlexibleBlurFeature #0
2. Render Objects: UI layer 0
3. FlexibleBlurFeature #1
4. Render Objects: UI layer 1
5. FlexibleBlurFeature #2
6. Render Objects: UI layer 2

Use the same Render Pass Event for the paired features; the demonstrated setup uses `After Rendering Post Processing`.

## Layer and Render Objects rules

- Give each stage a distinct Unity layer and assign its Canvas/Graphics consistently.
- Remove every staged UI layer from UniversalRendererData's ordinary Transparent Layer Mask.
- In each Render Objects feature, use Render Queue `Transparent`, select only its stage's layer, enable Depth override, and set Test to `Always`.
- Do not confuse Unity GameObject layers with Flexible Blur's numeric Layer field. The former controls when UI is drawn; the latter controls blur-result layering inside a feature.

## Feature numbering

`featureNumber` is the zero-based position among FlexibleBlurFeature instances, not the raw renderer-feature list index. A BlurReferenceProvider can publish one camera/feature pair to an entire Canvas.

## Verification sequence

1. Disable all but Render Objects stage 0 and verify only that UI layer draws.
2. Restore stage 1 and verify it draws after FlexibleBlurFeature #1.
3. Continue one stage at a time.
4. Verify each blur references the intended feature number.
5. Watch Game view for several seconds; recursive capture usually appears as increasing blur or brightness.

## Cost

Each meaningful stage adds renderer work. Cross-layer blur options can add a blit per layer, and image-based layers can require an additional render texture. Keep the stage count tied to visible composition needs.
