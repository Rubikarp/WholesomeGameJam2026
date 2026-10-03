# Changelog

All notable changes to LightSide Core will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Value scrubbing on `Vector2` drag fields**: dragging a vector field's label now scrubs both
  axes exactly like dragging a numeric field — the cursor hides and stays anchored, sensitivity
  grows with the value's magnitude, Shift speeds up, Alt slows down, and Escape cancels the drag.
- **`TypeSelector` exclusions**: `[TypeSelector(typeof(Foo))]` leaves the named subtypes (and
  anything assignable to them) out of that field's type picker while every other field still
  offers them.
- **`[HideFromTypeSelector]` on enum members**: an enum member carrying the attribute is left out
  of enum dropdowns while remaining a valid serialized value.

- **The LightSide surface shaders**: `LightSide/UI`, `LightSide/World` and `LightSide/World Lit` draw
  every LightSide surface — text glyphs, decorations, vector shapes and vector animation — so a run of
  them on one Canvas shares a material and collapses into a single draw call. What a quad draws rides
  the vertex stream rather than a shader keyword, which is what keeps the material shared.
- **`LightSideSettings`**: project-wide settings shared by every LightSide package, at
  Project Settings ▸ LightSide, holding `Include Lit Shaders` and the references that carry each
  package's shaders into builds. A package declares its own through `ILightSideShaderSet`, and
  `LightSideShaders.Require` resolves one by name, failing with a message that names the setting when
  the project excluded it.
- **`LightSideMenu.ProjectSettingsRoot`**: the Project Settings root each LightSide package nests its
  own page under.
- **`GpuUploadSlot`**: native-owned writable upload memory — acquire with `GpuUpload.TryAcquireSlot`,
  fill its `NativeArray<byte>` view (worker/Burst jobs allowed), then consume it with
  `GpuUploadBatch.Submit(ref slot, writtenBytes)` or return it with `GpuUpload.ReleaseSlot`.
- **Staged rejection diagnostics**: `GpuUploadSubmitResult.Stage` and `FailedRegion` name the
  admission stage and the offending region of a rejected submission instead of a lone error code.
- **`GpuUpload.SlotCapacityBytes` and `GpuUpload.MaxRegionsPerBatch`**: the bounds consumers plan
  upload chunks against, alongside the existing `Info.MaxStagingBytes` ceiling.
- **HDRP world surfaces**: `LightSide/World` and `LightSide/World Lit` render natively under HDRP —
  unlit as-is, and lit through a dedicated `LightSide/World Lit HDRP` Shader Graph (HDRP lighting,
  fog and shadow casting) that installs itself into `Assets/LightSide/HDRP` while HDRP is active and
  serves every lit world surface automatically.

### Changed

- **Breaking:** **`ProjectYamlFiles.TryReadYaml` is now `ReadYaml`**: it answers with a
  `YamlReadResult` — `Text`, `Binary` or `Unreadable`, the last carrying the reason — so a caller can
  tell an asset holding no YAML from one it failed to read; rename the call and branch on the result
  instead of a bool.
- **GPU upload contract (breaking, ABI 6.0)**: uploads are built on slots instead of reservations —
  a batch records regions against one acquired slot, `GpuUploadRegion.SlotOffset` (byte offset inside
  the slot) replaces `SourceOffset`, and `Submit`/`RecordOnce` take the slot they consume.
- **Upload memory is proportional to the dirty batch**: steady-state CPU upload memory is a shared
  ring of three 1 MiB slots trimmed to zero when idle, with larger bursts served by exact-size
  transient slots — replacing two page-sized persistent buffers per atlas.
- **One less CPU copy on Vulkan, D3D12 and Metal**: producers write pixels directly into the memory
  the GPU reads, so tightly-packed uploads no longer pass through an intermediate staging copy.
- **The easing popup groups its curves**: Sine, Quadratic, Cubic, Quartic, Quintic, Exponential,
  Circular, Back, Elastic and Bounce are collapsible families holding their In, Out and In Out
  entries, with Linear, the smoothsteps and the steps at the root and the custom shapes together —
  where every curve used to sit in one flat list, ordered by nothing an author could follow.
- **Popup height changes glide**: a fitted popup — the selector, the object picker — eases to its
  new height whenever search or a group toggle changes the content, where filtering used to snap
  the window in one jump.

### Removed

- **Reservation API**: `GpuUploadReservation`, `GpuUploadRequirements`, `GpuUploadSourceBuffer`,
  `GpuUploadSourceId`, the `GpuUploadBatch` attach/detach/complete members and the one-call
  `Upload(...)` wrappers — the slot API covers every use.

### Fixed

- **`SourceOutOfRange` crash loop on 32-bit Android**: GPU atlas uploads could fail terminally on
  armeabi-v7a devices (`GPU upload failed (SourceOutOfRange)`); memory addresses no longer cross the
  upload contract in any form, so the failure class is structurally impossible.
- **False `BackendFailed` from another plug-in's GL error**: an error left pending in the shared GL
  queue by unrelated code (Unity's own WebGL startup probing was enough) could fail a healthy
  glyph-atlas upload with `GPU upload failed (BackendFailed)`; pre-existing errors are now drained
  and discarded, so an upload verdict reflects only the upload's own commands.
- **A grouped popup opened without showing its current value**: the group holding the selected item
  stayed collapsed, so the list gave no sign of where the value already was.
- **A migration pass died on one asset it could not read**: an oversized or otherwise unreadable
  source aborted the whole pass, leaving every other asset unmigrated; such sources are now reported
  by name and retried by a later pass, while everything readable migrates.
- **`InputUtils.InputString` dying after a keyboard device change**: with the Input System package,
  the text stream went permanently silent once `Keyboard.current` was recreated (device reconnect,
  Input System reset), while key queries kept working.
- **Migration errors repeating for assets with unsaved editor changes**: an asset the editor held
  unsaved changes for failed its pass (`has unsaved editor changes`) on every domain reload —
  unwinnable when something marked it modified again on each load; such assets are now migrated
  automatically once saved, and one whose data is already current is no longer blocked at all.
- **LightSide windows broken right after a domain reload**: a window restored with the layout — the
  Package Patcher among them — could throw a `NullReferenceException` while building its UI ahead of
  the editor's first repaint, coming up empty until reopened.
- **Popup resize artifacts**: a popup resized in one jump could show its previous frame squashed
  into the new bounds on macOS, or a black band at the resized edge for one frame on Windows.

## [1.0.0] - 2026-07-07

### Added

- Initial release: shared runtime utilities, attributes with drawers, and editor tooling.
