---
name: flexible-blur-tune-quality-and-performance
description: "Use this skill whenever the user wants Flexible Blur quality or performance tuning — e.g. 'make blur cheaper on mobile', 'remove banding', 'which kernel should I use', 'use compute shaders', 'set per-platform blur settings', 'lower the layer resolution', or 'why is blur expensive'. Covers BlurPreset sections, all nine algorithms, iterations, sample distance, separable samples, reference resolution, resampling, dither, renderer formats, compute, layer resolution, platform overrides, and profiling tradeoffs. Do NOT use for initial setup (see flexible-blur-setup-and-overview) or stack semantics (see flexible-blur-layer-stack-and-punch-through)."
metadata:
  asset: "Flexible Blur"
  publisher: "Jeff Graw Assets"
  asset-version: "1.3.0"
  skill-version: "1.0.0"
  unity: "2022.3.62+"
  render-pipelines: "URP"
  category: "tools/gui"
  asset-store-url: "https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648"
  support-url: "https://discord.gg/PhqKsRhZ4D"
  last-verified: "2026-08-21"
---

# Tune Flexible Blur Quality and Performance

Control blur cost and appearance at both preset and renderer-feature levels, with settings that can vary by Unity Quality level and build platform.

## When to use this skill

- "make this blur cheaper on mobile"
- "which blur algorithm should I choose?"
- "remove banding or temporal noise"
- "configure compute per platform"
- "cap blur layers at 720p"
- "why are these blur panels expensive?"

Not for:

- Installing the renderer feature or fixing camera references; see `flexible-blur-setup-and-overview`.
- Ordering layered captures or punch-through; see `flexible-blur-layer-stack-and-punch-through`.
- Purely local alpha, source fade, or batching choices; see `flexible-blur-create-and-configure-effects`.

## Prerequisites

- Flexible Blur `1.3.0`, Unity `2022.3.62` or newer, URP, and a working baseline blur.
- Confirm `JeffGrawAssets.FlexibleUI.BlurPreset`, `BlurSettings`, and `BlurAlgorithm` resolve.
- Profile a representative build on target hardware; Editor draw-call and timing results are not sufficient for final platform decisions.
- If Flexible Blur is absent, stop and direct the user to the [Asset Store listing](https://marketplace.unity.com/packages/tools/gui/flexible-blur-ui-blur-framework-that-solves-hard-problems-338648).

## Quick start

1. Create `Create > FlexibleUI > BlurPreset` and assign it to representative blur components.
2. Start from the default Downscale section: `5-Tap Star`, 2 iterations, distance 1.5.
3. Start from the default Blur section: `4-Tap Cross`, 4 iterations, distance 1.5.
4. Leave Reference Resolution at 1080, HQ Resample off, Additional Distance/Iteration at 1, and Dither at 0.25 for the baseline.
5. Profile GPU time, processed area, draw/dispatch count, and memory on the target platform before changing one variable at a time.

Expected result: the project has a reproducible baseline preset for quality/performance comparisons.

## Workflows

### Workflow: Choose an algorithm

**Goal:** Match blur shape and cost to the target.

**Steps:**

1. Choose among `3-Tap Checkerboard`, `4-Tap Corners`, `4-Tap Cross`, `5-Tap Star`, `7-Tap Hexagonal`, `8-Tap Corners+Cross`, `9-Tap Octagonal`, `Quadratic`, and `Gaussian`.
2. Treat Quadratic and Gaussian as separable algorithms: each enabled horizontal/vertical pass incurs work, and Samples Per Side controls `2n + 1` samples for that pass.
3. Use iterations to build stronger blur. As iteration count grows, differences among kernels become less dominant.
4. Tune Sample Distance; half-step values often exploit bilinear filtering effectively.
5. Prefer a measured cheaper fixed kernel before increasing separable sample counts on constrained devices.

**Expected result:** the selected kernel and iteration structure meet the visual target at a measured cost.

### Workflow: Reduce mobile cost

**Goal:** Lower pixels and samples processed before sacrificing all blur character.

**Steps:**

1. Lower Reference Resolution toward the lowest common display resolution. This has a large performance effect; do not set it above 2160 without evidence.
2. Lower the renderer feature's Layer Resolution Ratio and/or Max Layer Resolution; the final layer size is constrained by both.
3. Reduce iterations and use a low-tap fixed kernel.
4. Enable HQ Resample only if the lower reference resolution produces unacceptable temporal noise; it adds a 7-tap hexagonal resample.
5. Test Compute Shaders on the target. Compute reduces draw calls and is likely faster on many platforms, but it must remain a measured platform choice.
6. Reassess blur area and image batching, because processed pixel count can dominate kernel arithmetic.

**Expected result:** GPU cost and transient texture size decrease while the blur remains stable enough for the product.

### Workflow: Correct banding or subtle gradients

**Goal:** Improve color stability without blindly selecting the largest texture format.

**Steps:**

1. Increase Dither Strength modestly from the default `0.25` when strong blur or low color depth bands.
2. Compare the feature's 32-bit quick setting with its 32-bit result/64-bit blur option.
3. Change Result Format or Blur Format only to formats supported by the target; the feature verifies support and falls back with a warning.
4. Use Brightness, Contrast, Vibrancy, and Tint only as deliberate artistic grading, not as a substitute for correct source color handling.
5. Verify SDR and HDR output separately where the project supports both.

**Expected result:** banding is reduced without unnecessary bandwidth or unsupported-format warnings.

### Workflow: Configure quality and platform variants

**Goal:** Keep deliberate settings for every Unity Quality level and build target.

**Steps:**

1. In BlurPreset, configure each Quality level; new levels inherit a copy of the last available settings.
2. In FlexibleBlurFeature, select a platform, set Compute, Result/Blur formats, Layer Resolution Ratio, and Max Layer Resolution, then save that platform configuration.
3. Verify the build helper applies the current build target's stored feature values.
4. Test every shipped Quality level and platform configuration rather than assuming the Editor's current values represent the build.

**Expected result:** presets select settings by active Quality level and renderer features use the saved target-platform configuration.

## Verification

- Every BlurPreset has at least one settings entry and enough entries for current Quality levels.
- No BlurSection has zero iterations unless it is intentionally inactive.
- Separable sample counts and enabled passes are intentional.
- Reference Resolution, Layer Resolution Ratio, and Max Layer Resolution produce the expected texture dimensions.
- Chosen formats are supported or emit only the expected one-time fallback warning.
- Profiler data from a target build shows the expected change in GPU time, memory, and calls.

## API quick reference

| Entry point | Type | What it does |
|---|---|---|
| `BlurPreset.Settings` | `List<BlurSettings>` | Stores one settings set per Quality level |
| `BlurSettings.downscaleSections` | list | Runs algorithms during the downscale phase |
| `BlurSettings.blurSections` | list | Runs the main blur sequence |
| `BlurSection.SetAlgorithm(...)` | method | Selects one of the verified BlurAlgorithm values |
| `BlurAlgorithm.All` | array | Lists the nine supported algorithms |
| `FlexibleBlurFeature.UsePlatformSettings(...)` | Editor method | Applies saved settings for a BuildTarget |
| `FlexibleBlurFeature.VerifyResultFormat(...)` | method | Resolves an unsupported result format to a fallback |

## Common issues

- **Blur changes between Quality levels** → The preset stores separate settings per level → Configure and test every level.
- **A separable blur is unexpectedly expensive** → Both passes and high samples-per-side are enabled → Reduce samples or disable an unneeded pass by setting its samples to zero.
- **Low resolution flickers or crawls** → Reference/layer resolution is too low for simple resampling → Raise it or enable HQ Resample.
- **Banding remains** → Dither/precision is insufficient → Increase dither modestly, then test a higher blur format.
- **Compute fails or looks different on one target** → Platform support/driver behavior differs → Store fragment mode for that platform after profiling.
- **Format selection is ignored** → The target cannot render that format → Read the fallback warning and choose a supported format.

## Boundaries

- No single setting is fastest on every GPU; platform profiling is required.
- Compute shaders reduce blur draw calls but do not remove required blits.
- Higher precision, more samples, more iterations, and larger textures all increase cost in different ways.
- A strong full-screen blur can remain expensive even with a cheap kernel because processed area matters.
