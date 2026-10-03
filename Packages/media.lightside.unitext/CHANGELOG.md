# Changelog

All notable changes to UniText will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.5.0] - 2026-09-02

### Added

- **Layer effects on colour glyphs** — stroke, shadow, glow, inner shadow, extrude, glitch fringes
  and a silhouette fill now reach emoji and inline sprites: every effect modifier gains a
  `Color Glyphs` parameter (`ColorGlyphPolicy` — `Inherit`, `Skip`, `Apply`) deciding whether it
  decorates them through their silhouette.
  - Shadow and glow apply to colour glyphs by default; every other effect skips them until set to
    `Apply`, per style, per rule parameter or per tag like any other effect parameter.
  - A `FillModifier` set to `Apply` paints the glyph as a silhouette in its paint — solid, gradient
    or texture — in place of the picture.
  - Glitch bursts jerk and blink colour glyphs as they do text; with `Apply` the magenta and cyan
    fringes appear as silhouettes around the picture.
- **Inline sprites as glyphs of the text** — `SpriteModifier` draws its sprites inside the text mesh
  instead of through child `Image` objects:
  - they render in `UniTextWorld`, and TMP migration no longer refuses world-space texts with
    `<sprite>` tags;
  - they take their place in the style stack, so paint layers and effects stack above or below them
    in order, and they receive layer effects through the `Color Glyphs` policy;
  - a sprite draws from its own texture at native resolution — one draw call per distinct texture,
    so the sprites of one atlas share one — needs no Read/Write access, and a runtime-updated or
    swapped sprite draws live;
  - a tightly packed atlas sprite draws by its own outline mesh, so its neighbours never show.

### Changed

- **Breaking:** **wrapper-based inline media derive from `InlineObjectModifier<TEntry, TWrapper, TOverride>`**:
  a custom media modifier that instantiates a prefab per occurrence renames its base class from
  `InlineMediaModifier<TEntry, TWrapper, TOverride>`; `InlineMediaModifier<TEntry>` and
  `InlineMediaModifier<TEntry, TOverride>` are now the bases for media rendered as glyphs of the
  text, with `OnShapingStarted` and `OnMediaShaped` hooks.
- **Migration window and unreadable assets**: an asset the scan cannot read no longer marks the whole
  scan as partial — only a cancelled or failed scan does; the dashboard lists such assets, says what
  they hold back, and excludes them all in one click so the run can finish.
- **Migration window empty state**: an empty finding list now says which of three things it means —
  nothing scanned yet, a scan recorded by another UniText release, or a project with no TextMesh Pro
  in it — and the component stage warns when it opened files and completed no component.
- **Script rewrite warns on unresolved receivers**: a member access the rewrite cannot resolve
  because the receiver is declared in another file is reported with the rename to make by hand,
  instead of passing without a word.

### Fixed

- **Colour glyphs drawn slightly small**: emoji and other colour glyphs rendered about 1–2% smaller
  than their metrics and a fraction of a pixel off their origin, because the atlas padding around
  them was applied in design units instead of pixels.
- **Clicks inside a Selectable fired twice with the Input System UI module**: pressing and releasing
  on a text sitting inside a `Button` or another `Selectable` delivered the host's click twice when
  the scene used `InputSystemUIInputModule`.
- **TMP migration and nested prefab instances**: a text inside a prefab instance nested in a scene or
  another prefab logged "the last scan has no finding for it" and was skipped, or its stripped stub
  counted as a component of its own; such texts now migrate through their source prefab.
- **Script rewrite adding an ambiguous `using LightSide;`**: a file that already used a type of its
  own named like a LightSide type got `using LightSide;` and stopped compiling on the ambiguity;
  UniText types are now written qualified in such files, with a note in the log.

## [3.4.0] - 2026-09-01

### Added

- **`FilterModifier`** — an adjustment layer for text: within its ranges a `ColorFilter` recolours
  everything rendered below it in the style stack — the glyph face, every paint layer, outline,
  shadow, glow, underline, extrusion and glitch fringe stacked beneath it, texture paints and colour
  emoji — while layers with a non-Normal blend keep their blend against the backdrop.
  - Ships with `GrayscaleFilter`, `SaturationFilter`, `HueRotateFilter`, `BrightnessFilter`,
    `ContrastFilter`, `ExposureFilter`, `InvertFilter`, `SepiaFilter`, `TintFilter` and a raw
    `ColorMatrixFilter`; subclass `ColorFilter` for a named filter of your own.
  - `Strength` fades the filter toward no change and takes a per-range value — `<sepia=0.5>` with
    a sepia filter bound to the `sepia` tag applies it at half.
  - Stacked filters compose bottom-up, nested ranges of one filter compose outer-to-inner, and a
    filter at the end of the style list recolours the whole styled text.
- **`ShapedGlyph.NoGlyph`**: the glyph id of a shaped slot a modifier renders itself — an inline
  sprite, object or math formula; a custom modifier reading shaped or positioned glyphs must skip it
  before any font or atlas query.

### Changed

- **LightSide Core 3.4.0 required**: the package now depends on `media.lightside.core` 3.4.0, up
  from 3.3.2.
- **Provider names in the type picker**: sprite, object, paint and reveal-handler providers list as
  `Asset`, `Inline` and `Global Settings` instead of their full class names.
- **Canvas no longer asked for normals**: UniText added the `Normal` channel to a Canvas'
  Additional Shader Channels without ever reading it; only `TexCoord1`–`TexCoord3` are requested now.

### Fixed

- **Italic text with inline media at a line edge**: any `<i>` in a text put an inline sprite, object
  or math formula at the start or end of a line through a font query it has no glyph for, failing
  the rebuild with an exception.
- **Range animations starting during a background rebuild**: a rule that began ticking while the
  text was processed on a worker thread touched the component from that thread and could fail the
  rebuild with a main-thread exception.

## [3.3.4] - 2026-09-01

### Fixed

- **Shift characters on Linux with the Input System package**: 3.3.3 announced Linux typing as fully
  restored, but shifted characters still arrived unshifted — Shift+A typed `a`, Shift+1 typed `1`;
  the shifted, layout-resolved character now arrives, so Shift, AltGr and non-US layouts type
  correctly with Active Input Handling on Input System Package alone.
- **Per-keystroke logging on Linux**: 3.3.3 wrote a log line for every keyboard event and every
  focused frame in players typing through the managed input backend.
- **Rippled effect edges along slanted strokes**: the outer edge of a thick outline, shadow or glow
  showed periodic bumps along straight diagonal glyph edges — clearest on large text with slopes
  around 60° — while the glyph fill stayed clean; effect edges now stay as smooth as the fill at any
  width, in both SDF and MSDF modes.
- **Phantom unsaved changes from decorations**: drawing a selection highlight, underline or any other
  range decoration in edit mode marked the Canvas as modified, so a scene could report unsaved
  changes just from being viewed — and a prefab open in Prefab Mode was marked again on every load,
  with nothing to save away.

## [3.3.3] - 2026-08-31

### Fixed

- **Typing on Linux with the Input System package**: a focused field took editing keys but no
  characters, so 3.3.2 asked for Active Input Handling to be set to Both — characters, Shift, keyboard
  layouts and dead keys now work on the Input System package alone, as they do on every other
  platform, and that setting is no longer needed.
- **Held keys repeating at the wrong rate**: a held Backspace, Delete or arrow key repeated on a fixed
  schedule of UniText's own on platforms without a native transport, ignoring the operating system's
  keyboard delay and rate; the system's own repeat now drives it.
- **Editing keys arriving out of order with typed text**: on platforms without a native transport, a
  keystroke pressed in the same frame as typed text could apply before it, so typing `ab`, Backspace
  and `c` in one frame could leave `ac`.
- **Synthetic italic drifting right of its box**: text slanted by `<i>` on a font with no italic cut
  sat up to a tenth of the font size right of where it belonged, so centred text looked off-centre and
  left-aligned text carried a gap the upright text did not have.
- **Synthetic italic escaping its rect**: the leaning edges of that text fell outside the
  RectTransform under Auto Size, and `ContentSizeFitter` and layout groups were handed a width that
  ignored them.
- **`Italic Style` and `<i=N>` documented as degrees**: the value is a shear in percent of height, so
  `<i=20>` leans by a fifth of the glyph's height — about 11°, not 20°.

## [3.3.2] - 2026-08-31

### Changed

- **Caret object hidden in the hierarchy**: the caret an editable text creates no longer appears as a
  child object.
- **Linux needs the legacy input manager for typing**: Unity delivers no text events on Linux when
  Active Input Handling is Input System Package only, so a focused field takes editing keys and no
  characters — set Active Input Handling to Both, which 3.3.1 claimed was unnecessary.

### Fixed

- **Text vanishing after leaving a `RectMask2D`**: pulling a text out from under a mask — or disabling
  the mask — left the former mask's region governing its culling, so the text could disappear on its
  next rebuild or stay hidden if it had been scrolled out of the viewport when removed.
- **Components stuck on a text after Scene view editing**: the `UniTextEditable`, `UniTextSelectable`
  and caret that inline editing adds could outlive the session — hidden from the inspector, and
  leaving the `UniText` component impossible to remove.
- **Duplicating a text while editing it in the Scene view**: the new object carried a copy of the
  editing session's hidden components, which stayed on it and left its `UniText` component impossible
  to remove.
- **Shift and keyboard layout ignored while typing on Linux**: 3.3.1 read Linux keystrokes from a
  channel that carries no layout, so Shift+A typed `a` and Shift+1 typed `1`; characters now come from
  the platform's own text channel, which resolves layout, Shift and dead keys.
- **A held key not repeating without a native transport**: holding Backspace, Delete or an arrow key
  acted once per press on Linux and on any other platform served by the managed input backend.
- **Silence when a platform sends no characters**: a field that took editing keys but never typed gave
  no clue why; it now reports the missing text channel and the setting that restores it.

## [3.3.1] - 2026-08-29

### Added

- **HDRP lighting for world-space text**: Lit world text now receives real HDRP lighting, fog and
  shadow casting through the `LightSide/World Lit HDRP` Shader Graph, which installs itself into
  `Assets/LightSide/HDRP` in HDRP projects; `Light Influence` works as on the other pipelines, while
  `Ambient Strength` and `Directional Strength` have no HDRP counterpart — light intensities are the
  scene's there.
- **HDRP in the custom world shader templates**: the world shader template and the Rainbow, Hologram
  and Dissolve examples now carry an HDRP SubShader — regenerate a custom shader from its template to
  pick it up; already-generated shaders keep rendering under HDRP as before.
- **Folder tree in the migration Analysis tab**: pick a folder to narrow the finding list to that part
  of the project, with the count each folder holds under the current filters, instead of typing paths
  into the search box.
- **Leaving an asset out of the migration**: Exclude on an Analysis row, and Settings ▸ Add asset…,
  take a single scene, prefab or asset out — so a run finishes around one you keep on TMP or one
  nothing can read, and the tree's Exclude does the same for a whole folder of third-party content.
- **Mesh-only invalidation for custom animated modifiers**: `GlyphParamModifier` re-resolves its
  parameters before every mesh rebuild, so subclass parameter fields point their state callbacks at
  the new protected `MarkParamsDirty()`, and modifiers that re-resolve their own per-range state the
  same way raise the new protected `BaseModifier.MarkRenderDirty(bool)` instead of a full re-apply.

### Changed

- **Exclusions now cover the whole migration, not only the scan**: nothing under an excluded path is
  migrated or reference-repaired either, so a reference inside it to a component the migration
  replaced is yours to fix — the Dashboard and Settings state this while any path is excluded.
- **The side lists read as panels**: the folder tree and the Script Preview file list sit on their own
  surface rather than floating against the window.
- **Per-frame cost of animated text**: writing an animation parameter each frame — a glyph effect's
  `Phase`, `RevealModifier`'s front, `ScrambleModifier`'s progress, or anything a `UniTextDriver`
  drives — now rebuilds only the mesh instead of re-applying every style on the text, so texts
  carrying several styles animate many times cheaper.
- **Less steady-state garbage**: per-frame text rebuilds in players, color-token parsing, and the
  validation of each newly spawned styled text no longer allocate.

### Fixed

- **Buttons not firing under UniText**: a `Button` or any other `Selectable` holding UniText activated
  only while the pointer neither moved nor crossed between the text and the rest of the control, so a
  press that slid a few pixels, or one that started on the text and ended beside it, produced no click.
- **World-space text magenta under HDRP**: `UniTextWorld` text, world decorations and world vector
  animation drew the magenta error shader in HDRP projects.
- **Prefabs refused over an asset that was not theirs**: migrating a prefab holding a `TMP_InputField`
  failed with a message naming an unrelated scene the tool could not read; the blocker now names the
  asset that actually stopped it and how to get past it.
- **Scripts rewritten without the whole project checked**: an asset the migration could not read was
  passed over in silence, so a field pointing at a TMP component from inside it could be dropped by
  the rewrite without a trace.
- **Analysis list jumping to the top**: Skip, Migrate or Re-check on a row sent the list back to the
  first entry, so working down a long list meant scrolling back after every press.
- **An exclusion swallowing a neighbouring folder**: excluding `Assets/UI` also excluded
  `Assets/UIKit`.
- **File names centred in the Script Preview list**: the rows were drawn centred instead of reading
  from the left edge.
- **Linux typing with the Input System package**: printable characters never reached a focused field
  on Linux — editing keys worked, letters did not; typing now works under every Active Input Handling
  setting.
- **`ScrambleModifier` progress frozen when driven alone**: changing `Progress` from code or a driver
  without also churning `Phase` left the decode where it was — it caught up only when something else
  made the text re-apply its styles.
- **Blend materials lost or leaked on layer surfaces**: a layer with a non-`Normal` blend could drop
  its material shortly after its text was disabled and re-enabled, and changing such a layer's blend
  mode or material left the previous blend variant alive for the rest of the session.
- **Invisible keyboard on Android 12/12L**: tapping a field could leave the software keyboard off
  screen while the system reported it shown — retapping never helped and only a screen power cycle
  brought it up; hits devices whose final OS is Android 12, such as the Pixel 3a.
- **Held Backspace deleting one character on Android**: holding the delete key erased a single
  character instead of repeating for as long as it was held.
- **Keyboard blink on Android**: the keyboard hid and immediately re-showed when the first character
  was typed into an empty field and when the last one was erased.

## [3.3.0] - 2026-08-25

### Added

- **Custom native field presenters**: the surface of a platform-native text replica can be built by
  your own code on Android, iOS and WebGL, while `UniTextEditable` stays the only text authority —
  documented in `Documentation~/NativeFieldPresentation.md`:
  - `NativeFieldOverlayBehavior.PresenterId` picks a presenter registered from an Android library
    (`UniTextNativeInput.registerNativeFieldPresenter`), an iOS plugin
    (`UniTextNativeInput_RegisterNativeFieldPresenter`, declared in the shipped
    `UniTextNativeInput.h`) or a page script (`window.UniTextNativeFieldPresenters`).
  - `Identifier` sets the platform control identity — Android tag, iOS accessibility identifier, DOM
    id — and `PresenterData` carries an opaque per-field string only that presenter interprets.
  - The reserved `system` presenter follows the host's own appearance and cannot be replaced or
    unregistered; a presenter owns layout and appearance only and must keep the supplied editor's
    input connection, delegates and text path intact.
  - The native placeholder is the text of the object on `PlaceholderDecorator`, so one placeholder
    serves both the Unity field and its replica.
- **Semantic editor actions from a native control**: `NativeEditorAction` gained `Cancel`, `Copy`,
  `Cut`, `Paste`, `PastePlain`, `Undo`, `Redo` and `Return` beside `Submit`, `Next` and `Previous`,
  with `IsSupported` to test one — a presenter's own buttons send an action rather than touching the
  native value or the platform clipboard.
- **Android autofill hints reach the native field**: the autofill hint on a `NativeKeyboardBehavior`
  now sets the Android control's own autofill hints, where it was honoured on iOS and WebGL only.
- **`AttributeChannel`**: the pipeline passes reading one attribute key now belong to a channel that
  runs once per rebuild however many modifiers write that key:
  - A `PooledAttributeModifier<T>` subclass declares one from `CreateChannel` and reaches it through
    `SharedChannel`; `OnActivate`, `OnDeactivate` and `OnRelease` bracket the writers' active span,
    `OnBeginCycle` resets state once per apply cycle, and `OnProviderChanged` rebinds what the
    channel captured from a writer that left.
  - `UniTextBuffers.ActivateChannel` registers a writer against a key directly.
- **`PooledArrayAttribute<T>.WritableSpan`**: the extent prepared by `Prepare`, or one range of it,
  as a writable span, so a modifier can fill an attribute without a value-per-element call.
- **`UniTextMeshGenerator.currentPositionedIndex`**: the positioned glyph behind the quad being
  emitted, or `-1` for a quad its emitter authored directly in final pixel space (a decoration line
  slice) — a modifier that scales a quad about its own pen must skip those.
- **`FontFeatureModifier`** (default tag name `feature`): turns OpenType features on or off over a
  text range — `kern 0` to drop kerning, `-liga`, `tnum` for tabular figures, `ss01 2`, or several
  separated by commas; nested feature ranges merge, and the innermost value wins a shared tag.
- **`UniTextBuffers.AddFontFeatures`**: a modifier of your own can hand OpenType features to
  shaping, building the set with `FontFeature` and interning it through `FontFeatureRegistry`.
- **`UniTextBuffers.TryResolveInjectedGlyph`**: resolves a character a modifier draws outside the
  document text — an ellipsis dot, a list marker, a wheel symbol — in the face the text around it
  resolved to, returning the glyph and its advance as an `InjectedGlyph`.
- **`Overlay` on underline and strikethrough**: draws the line together with every layer applied to
  it — stroke, shadow, glow — above the whole text instead of stacking each of those layers at its
  own position in `Styles`, so a line over outlined text keeps its own outline and reads as a
  separate mark rather than merging into the glyph faces.
- **`UniTextMeshGenerator.sequenceBias`**: raises the layer sequences a modifier records while it is
  set, so a modifier drawing virtual glyphs of its own can lift their whole stack into a band above
  the text; layers fold it in where they capture the sequence, during `onGlyph`.
- **Text structure queries**: count and walk a text by `TextUnit` — clusters, words, lines or
  paragraphs — through `Units`, `CountUnits`, `UnitAt` and `TextSpan` on the text component, or
  `Units` and `CountUnits` on a `ModifierRange` for one range's own share; words follow UAX #29 and
  any installed dictionary, so Thai, Lao, Khmer, Myanmar and CJK divide correctly, and enumerating
  allocates nothing.
- **`Unit` on `RevealModifier`** (third tag parameter, `<reveal=fade,50%,line>`): the frontier
  advances by cluster, word, line or paragraph, so a whole word or line arrives in one movement
  instead of letter by letter; changing it rewrites an absolute `Front` to keep the same text
  revealed.
- **`TextProcessor.Analyzed`**: a pipeline hook that runs once script, direction, break and word
  analysis is final and before shaping begins — the phase for work that needs those results and must
  still reach shaping, hiding characters from it above all.
- **`RevealHandler.SupportedUnits`**: an appearance effect declares the granularities it is
  meaningful at as a `TextUnits` set; a reveal counting in another one reports the mismatch and
  plays the effect unchanged.
- **`RevealGlyphInfo.unit` and `RevealGlyphInfo.text`**: an appearance effect can read the codepoint
  span of the glyph's whole unit and reach the text it belongs to, so an effect can move a word or a
  line as one body.
- **`GeometricRevealHandler`**: base for eased appearance effects that turn the glyph quad around a
  point, carrying the authored `Pivot` a custom effect would otherwise declare for itself.
- **Configurable pivot on the spiral, burst, rain and chaos reveal effects**: their transform point
  was fixed at the glyph centre.

### Changed

- **LightSide Core 3.1.0 required**: the package now depends on `media.lightside.core` 3.1.0, up
  from 3.0.0.
- **Glyph atlas upload memory**: steady-state CPU memory for glyph delivery drops from two
  page-sized buffers per atlas (16 MiB SDF, 64 MiB MSDF) to a small shared pool proportional to the
  frame's actual glyph traffic, released when text is idle.
- **Font file storage**: a font asset's compressed file bytes now live in a hidden sub-asset and, in
  players, on disk instead of the managed heap — rendering reads a memory-mapped cache, so font
  bytes never stay resident and fonts that are never displayed are never decompressed; existing
  font assets convert themselves on the first editor load after the upgrade.
- **Fonts in AssetBundles**: content built with the Scriptable Build Pipeline (Addressables
  included) delivers each font through an on-demand entry — loading a bundle no longer
  deserializes font bytes, the payload is fetched and cached once per installation, and the bundle
  can be unloaded immediately afterwards.
- **Font payloads ship beside the player for exactly the fonts a build packs**: the set is taken
  from the build's own packed content; an incremental build that reuses content without reporting
  it reuses the payload published for that content, or, failing that, packages every font in the
  project and says so in the console — a clean build restores the minimal set.
- **Default text context menu is smaller**: the bundled selection context-menu prefab drops to a
  22 px label on 48 px rows in a 160 px-wide panel, from 30 px on 72 px rows at 300 px; a project
  using its own context-menu prefab is unaffected.
- **A custom modifier's pipeline passes move to a channel**: a `PooledAttributeModifier<T>` subclass
  that subscribes its passes in `OnEnable` runs them once per instance instead of once per text —
  declare them in `CreateChannel` instead; a subclass that overrides `OnDisable` or `BeforeApply`
  must now call the base implementation, and state produced only by the parse, shaping or layout
  phase must not be cleared from `BeforeApply`, which a granular re-apply runs without re-running
  those phases.
- **`UniTextMeshGenerator.ExpandQuad` derives its own scale**: the method no longer takes a `scale`
  argument and reads the quad's own UV0-to-pixel ratio instead, so an expansion lands on the same
  iso-line whatever size the quad was built or scaled to; `SubMeshModifier.ExpandSubMeshQuad` takes
  its `delta` in the same UV0 units.
- **Mixing appended and range writes on one pooled attribute throws**: `PooledArrayAttribute<T>`
  now rejects an `Add` after a `FillRange` since the last `Prepare`, and the reverse, with an
  `InvalidOperationException`.
- **`StylePreset.Changed` removed**: subscribe to `DeltaChanged`, which reports the same mutations
  with their affected node, member and operation.
- **`UniTextFont.FontData` returns a fresh copy**: each access copies the font's bytes rather than
  handing out the asset's own array, and `CopyFontData()` names that cost at the call site.
- **An unpaired UTF-16 surrogate reads as U+FFFD**: `UnicodeData.DecodeAt` and the parsed codepoint
  buffer replace a lone surrogate with the replacement character instead of passing its raw value
  through to a parse rule.
- **A word that needs an operating-system font is resolved as a whole on macOS**: when no assigned
  font covers a word, the whole word is offered to the system cascade first, so it lands on one face
  instead of switching typeface between its characters.
- **The system emoji font is no longer copied into memory on iOS**: the face is held by the
  operating system and queried for coverage, advances, shaping and rasterization in place, where
  Apple Color Emoji's whole file used to be rebuilt into a managed array on first use.
- **`SystemFont.MemoryStats` counts only memory-backed sources**: a system font delivered as a
  mapped file reports no retained bytes and does not consume `InactiveByteBudget`, so both numbers
  now track resident memory rather than font size.
- **Native input is a session rather than a global switch**: `UniTextNativeInput` no longer exposes
  `ShowKeyboard`, `HideKeyboard`, `SetEnabled`, `CompleteComposition`, `CancelComposition`,
  `Context`, or the static `KeyDown`, `TextInput`, `DeleteBackward`, `EditorAction`,
  `CompositionChanged`, `CompositionEnded`, `SelectionChanged`, `NativeFieldSubmitted`,
  `NativeFieldCanceled`, `NativeFieldTextChanged` and `NativeFieldCreated` events:
  - `INativeInputBackend` is rebuilt around `OpenInput`, `QuiesceInput`, `CloseInput` and
    `AbortInput` taking a `NativeInputOpenRequest` and reporting through a `NativeInputReporter`, so
    a custom backend must be rewritten; the `RegisterBackend(INativeInputBackend, int)` overload is
    gone and a backend registers through its factory overload.
  - `CompositionChangedHandler` is gone with the events it typed, and `UniTextEditable` no longer
    implements `ITextInputContext`.
  - `UniTextEditable.TouchKeyboardVisibilityChanged` is gone — read
    `UniTextNativeInput.IsKeyboardVisible` or subscribe to
    `UniTextNativeInput.KeyboardVisibilityChanged`.
  - `NativeEditorAction.Newline` is now `NativeEditorAction.Return`.
- **`NativeFieldOverlayStyle` and `NativeFieldHandle` removed**: per-platform styling of the native
  replica and the raw handle to its view give way to the presenter contract, taking
  `NativeFieldOverlayBehavior.Style` and `KeyboardRequest.overlay` with them, and a behavior
  authored in 3.2 comes up on the `system` presenter without its styling:
  - `Placeholder` is now the text of the object on `PlaceholderDecorator`, and `CharacterLimit` is
    `LengthLimitBehavior`, which the replica now obeys like any other input rule.
  - `IOSAccessibilityIdentifier` and `WebGLId` are the one `Identifier` field.
  - `AndroidBackgroundResource`, `AndroidTextAppearanceResource`, `AndroidThemeResource`,
    `WebGLCssClass` and `WebGLInlineStyle` have no direct replacement — a custom presenter builds
    the surface instead, and `system` follows the host's own appearance.
- **`UniTextEditable.OnDetachedFromCell` takes a completion callback**: a virtualised-list host
  passes an `Action` and recycles the object when it runs, instead of reusing the cell the moment
  the call returns while a native session is still closing.
- **Faster rebuilds of text using OpenType features**: a range with small caps, superscript,
  subscript or font features is now as cheap to re-shape as plain text.
- **`UniTextBuffers.RequestVirtualCodepoint` covers every face**: a character declared for drawing
  outside the document text is now prepared for every font the text resolves to, not only the one
  the font stack picks for that character on its own.
- **A reveal pivot is a normalized `Vector2`**: the nine named corners give way to a point over the
  glyph quad — `(0,0)` its bottom-left, `(1,1)` its top-right, outside the unit square beside it —
  so a pivot can sit anywhere and be driven like any other vector; a pivot authored in 3.2 comes up
  at its effect's default.
- **`RevealGlyphInfo` is restated rather than rebuilt**: an appearance effect wrapping others calls
  `WithProgress` to give a child its own timeline or `WithFront` to restate the frontier, where it
  used to construct a second `RevealGlyphInfo`; that constructor is gone.
- **Faster reveal, scramble and rolling animations**: text is re-shaped only when the set of
  characters taken out of the layout changes, instead of on every frame of any of those animations.
- **Auto Size lands on an exact size**: the fitted size is the largest one the box actually allows
  rather than the nearest half point, so `CurrentFontSize` reports fractional values and follows a
  resize smoothly instead of in visible steps.
- **Auto Size is significantly faster**: a fit settles with much less work per rebuild — the gain
  grows with the text length and with the span between Min and Max — and a Fit Steps ladder now
  costs a fraction of what it did, a line-height step next to nothing.

### Fixed

- **Auto Size flipped between two font sizes**: in a box sized to the text's own preferred height —
  what a `ContentSizeFitter` produces — the size alternated between rebuilds and jumped by half a
  point at a time, and the text was drawn up to half a point smaller than the box allowed.
- **Auto Size overflowed the width of an indented paragraph**: a paragraph's start margin, from
  `<indent>` or a list marker, was left out of the fit's width test, so the text was sized as if
  that space were free.
- **Crash loop on 32-bit Android devices**: `[GlyphAtlas:SDF] GPU upload failed (SourceOutOfRange)`
  could recur on every launch on armeabi-v7a phones, permanently breaking text rendering.
- **A second modifier of the same kind applied its effect twice**: two modifiers writing one
  attribute — two letter-spacing modifiers, a bold in the component's styles and another in a style
  preset, a `<sup>` rule beside a `<sub>` rule — each ran that attribute's pipeline pass over the
  shared buffer, so tracking was added twice, synthetic bold expanded twice and an arc bent twice.
- **Two underline, strikethrough or glitch modifiers on one text drew each other's ranges**: each
  read the shared range marks with its own parameter list, so both ranges came out with the wrong
  thickness, offset and colour.
- **`<math>` formulas and `<ruby>` annotations vanished when a neighbouring style changed**: a hover
  state, a driver clip or an inspector edit on a modifier overlapping the range discarded the
  formula's layout or the annotation's shaped glyphs, leaving the range blank until the next full
  rebuild; inline media in the same situation was measured against a stale shaping position.
- **`<indent>` grew every time a neighbouring style changed**: the start margin was added again on
  each re-apply instead of replacing the previous one, so a hover state or a driver clip pushed the
  paragraph further right on every change.
- **A `<reveal>` with `Collapse` snapped to its end state when a neighbouring style changed**: an
  in-flight appear or hide animation was completed early by any overlapping modifier's parameter
  change, and the frontier lost the previous-frame comparison it animates against.
- **Math fraction bars and radical bars ignored the range's size**: inside a `<size>`-scaled or
  small-caps range the bars kept the base size's width and thickness, then took the range's scale a
  second time, landing detached from the formula.
- **Stroke and outline thickness on texture-painted scaled text**: an effect expanding a quad drawn
  from a paint texture measured its step against the base font size, so inside a `<size>`-scaled or
  small-caps range the outline came out too thin on enlarged text and too thick on shrunken text; a
  custom `SubMeshModifier` calling `ExpandSubMeshQuad` had the same error.
- **macOS 15 system faces rendered nothing**: the faces macOS 15 ships with their outlines outside
  the font file cannot be rasterized from disk, so an operating-system font resolving to one — the
  default UI face included — produced blank text; those outlines now come from the operating system
  itself.
- **macOS system fonts with no readable font file were skipped**: a family the operating system
  exposes without a file path failed to resolve and fell through to the next candidate, where it now
  loads like any other.
- **COLRv1 colour fonts rendered flat**: a font was judged COLRv1-capable by whether its grinning-face
  emoji carried a paint and by having no bitmap strikes, so a COLRv1 icon or symbol font with no
  emoji in it, and a font shipping both COLRv1 and bitmap glyphs, lost their colour layers; capability
  is now read from the font's own colour table and decided per glyph, with the bitmap or outline path
  taking the glyphs COLRv1 does not cover.
- **A native field replica bypassed the editor's own input rules**: with a
  `NativeFieldOverlayBehavior` attached, whatever the platform control produced was written into the
  document wholesale, so `LengthLimitBehavior`, `InputMaskBehavior`, `CaseTransformBehavior`,
  `AutoValidateBehavior`, every `InputFilter` hook, the undo history and the paste policy never saw
  it; typed, dictated, autofilled, pasted, deleted and autocorrected text now enters the same
  pipeline as keyboard input, and a programmatic document or selection change supersedes the native
  control's optimistic state.
- **A native field replica ignored `ReadOnly` and lost track of secure entry**: the platform control
  was never told the editor was read-only and so accepted edits, and password mode, keyboard traits
  and the placeholder were captured once at focus — changing any of them while the replica was open
  had no effect until it closed and reopened.
- **`<smallcaps>` spread past the tagged range**: the rest of the paragraph in the same font and
  script was rendered as small capitals too.
- **`<sup>` and `<sub>` had no effect after a font, script or direction change in the same
  paragraph**: on a font carrying real superscript or subscript glyphs the tagged characters kept
  their full-size baseline form instead of being raised or lowered.
- **Characters drawn outside the text ignored its font**: the `…` of `EllipsisModifier`, list
  markers and bullets, and the symbols of the rolling and scramble animations always came from the
  font stack's own pick, so bold, italic, a font override or a variable instance never reached them.
- **`<ellipsis>` behaved erratically over several paragraphs**: with word wrap on and more text than
  the box could show, the marker landed on the wrong line or alone on an empty one, and shrinking
  the box could show *more* text than a larger box had.
- **`<ellipsis>` ignored the box height whenever the text was also too wide**: with both dimensions
  exceeded only the width was honoured, leaving the overflowing paragraphs on screen.
- **`<ellipsis>` gave up a character it did not have to**: the marker replaced at least one
  character even on a line with room to spare for it.
- **Text after an `<ellipsis>` range kept its old place**: the content following a truncated range
  stayed on the line it occupied before truncation, leaving a gap after the marker.
- **`<ellipsis>` reserved the wrong marker width under Auto Size**: the space freed for the marker
  was scaled wrongly whenever the rendered size differed from the shaping size.
- **`RevealModifier` in Collapse mode kept the lines of hidden text**: paragraph breaks inside text
  the reveal had not reached still held their lines open, so the block stayed as tall as the whole
  text instead of shrinking to what it showed.
- **`<reveal>` animations stuttered outside Play mode**: a running appear or hide effect advanced in
  visible steps in the editor, and how smoothly it ran depended on whether the scene happened to
  hold an enabled `UniTextDriver`.
- **Caret stood inside a letter instead of on its edge**: text carrying an invisible formatting
  character — a zero-width space, a joiner, a directional mark, a byte-order mark — or a range a
  modifier had hidden placed the caret part-way across the neighbouring glyph.

## [3.2.0] - 2026-08-14

Every serialized field of every modifier becomes an addressable parameter: readable, writable,
interpolatable and composable from code, targetable by a range rule, and drivable per range from
a new sequencer component with its own Timeline window. Reaching a single range is now a query
(`<b #intro>` names one, `Ranges().WhereLabel("intro")` finds it), and the interaction-state
vocabulary is renamed from "effect" to "rule" throughout. Scenes, prefabs and presets convert
themselves on the first editor load after the upgrade; C# does not.

### Added

- **Every modifier field is a parameter**: a modifier's serialized fields are now typed parameters addressable by id — read, written, interpolated and composed from code, targetable by a range rule, and drivable per range — where 3.1.0 exposed only the four properties three built-in modifiers hand-declared:
  - **`ParameterDescriptor`** / `ParameterDescriptor<TModifier,TValue>` carries `Id`, `DisplayName`, `Slot`, `SlotOn`, `ValueType`, `ModifierType` and `SupportedCompositions`, and offers `Resolve`, `ResolveCascade`, `TryResolve`, `ResolveNext`, `ReadRoot`/`SetRoot`, `Lerp` and `Compose`; `ParameterDescriptor.Find` looks one up by id and the generated `Param.All` lists a modifier's full set.
  - A parameter resolves one cascade — an explicit markup token first, then the rule default, then the modifier's own field — with owned values composed on top.
  - **Parameter code generation**: a partial modifier marked `[GenerateParameters]` gets its nested `Param` class, `Param.All` and its `Descriptors` override written for it, in your assemblies as well as UniText's, with authoring mistakes reported as compiler errors UTP001–UTP003.
  - `[Parameter]` gained `Parser` (a token vocabulary of the field's own), `Invalidate` and `Descriptor`; `[SlotlessParameter]` joins a field to the parameter surface without giving it a positional markup slot; `[ParameterContainer]` gained `Invalidate`.
  - A rule's Target picker now lists every parameter of every modifier in the graph, and `IntRuleValue`, `BoolRuleValue`, `StringRuleValue` and `EnumRuleValue<TEnum>` join the float, unit, vector, unit-vector and colour rule values, with `RuleValue<TValue>` as a public base for a project's own unmanaged value type.
  - Every parameter of a paint layer is now rule- and range-drivable — stroke width, alignment and softness, shadow offset, blur and spread, glow radius, fill softness and dilate, corner style and miter limit, tint, blend, and the paint's mapping, shape, fit, angle, scale, offset and spread — while the paint slot itself stays authored-only, on paint layers, line decorations and highlights alike.
- **Range queries and parameter ownership**: code can select the live ranges a modifier applies to and take one of their parameters into its own hands, writing it directly while the authored value stays underneath:
  - `BaseModifier.Ranges()` starts a `RangeQuery` whose filters combine with AND — `WhereLabel`, `WhereParameter`, `WhereToken`, `WhereBare`, `WherePrimaryValue`, `WhereChannel`, `Intersecting`, `Within`, `Where` (a custom `ModifierRangePredicate`), `Skip` and `Take` — and `Collect` returns the matches in text order; `BaseModifier.GetRanges(List<ModifierRange>)` collects them unfiltered.
  - `ModifierRange` carries `Identity`, `Segment`, `Range`, `PrimaryValue`, `Label`, `Channel` and `IsValid`, and reads or takes over one parameter of that single range through `Resolve` and `Own`.
  - **Named tag ranges**: an opening tag can carry a `#name` anchor written after a single space — `<b #intro>…</b>`, `<color #warn=red>…</color>`, `<sprite #icon/>` — naming that one occurrence for `RangeQuery.WhereLabel`; letters, digits, `_` and `-` are accepted, paired, self-closing and void tags all take one, and `ParsedRange.label` with the trailing `label` argument on `ParsedRange.SelfClosing` lets a custom parse rule emit one.
  - `BaseModifier.Own`, `RangeQuery.Own`, `ModifierRange.Own` and `UniTextRanges.Own` return an `OwnedParameter<TValue>` (`Value`, `Baseline`, `Composition`, `IsAlive`, `Release`/`Dispose`) for one range, or an `OwnedParameterSet<TModifier,TValue>` for a whole query — whose `Value` broadcasts to every member, `SetValue(index, …)` writes one, `Withhold` deactivates them all without giving up membership, `Count`/`GetRange` walk them in text order, and `Changed` reports membership changes after each reparse.
  - An owned value combines with the authored one under `ParameterComposition.Replace`, `Add`, `Multiply` or `Custom`, at a caller-chosen priority.
  - `RangeQueryDefinition` is the serialized form of the same filters, built into a live query against a target modifier so its matches follow later text edits.
- **`UniTextDriver`**: a sequencer component (Add Component ▸ UniText ▸ UniText Driver) that animates modifier parameters over a shared timeline — each clip drives one parameter across the ranges its query matches, ramping every match from a `From` value to a `To` value inside its own window, with clips free to overlap and to drive different parameters of different modifiers:
  - Each `UniTextDriverClip` carries `Start`, `MemberDuration`, `Stagger` (matched ranges ramp one after another in text order), `Easing`, the typed `From`/`To` endpoints, `Composition`, `Priority` against other owners of the same parameter, a `Query` narrowing which ranges it drives, and `Disabled`.
  - Transport is `Play`, `Pause`, `Stop`, `Seek` and `Advance`, with `Progress` (normalized) and `Playhead` (seconds) both settable to render one exact state, alongside `IsPlaying`, `TimelineLength`, `Duration` (0 derives it from the clips), `Speed` (negative runs backwards), `DriverLoop.Once`/`Loop`/`PingPong`, a scaled, unscaled or manual clock, and `Rebind`.
  - A clip can target any parameter of any modifier in the component's `Styles`, its `StylePresets` or the project-wide style preset, and the picker groups the choices by modifier.
  - `Clips` is a live list and `UniTextDriverClip.SetTarget` binds a clip from code, so a sequence can be built or retargeted at runtime; each clip's ranges follow its query across text edits and return to their cascade when the driver is disabled.
  - Among `Replace` clips of the same parameter and priority the latest clip whose start the playhead has passed wins, while `Add`, `Multiply` and `Custom` clips compose alongside it, so a clip holds its boundary value until the next one takes over.
  - Playback and scrubbing work in the editor without entering Play mode, while `Play On Enable` starts only in Play mode; `Progress` is serialized, so a Unity `Animator` or a Timeline animation track can scrub the whole sequence.
- **Timeline window for a driver**: the driver inspector's timeline button opens the sequence in a window where clips are arranged on tracks and dragged, resized, split at the playhead, duplicated, copied, pasted, muted and marquee-selected, with a scrubbable ruler, snapping, zoom and pan, and transport synced to the driver:
  - Double-clicking a clip opens its full editor in place, and a multi-clip selection edits Start, Duration, Stagger, Easing, Composition, Priority, Mute and the query's label, skip and take together.
  - Tracks can be added and removed, double-clicking an empty lane creates a clip there, and splitting a clip gives both halves the value its easing reaches at the split.
  - Dragging the end marker sets the sequence's fixed duration.
- **`ModifierFieldsAnimationHandler`**: one handler on `UniTextAnimationBridge` animates the fields of every modifier in the component's `Styles`, nested children included, after a Unity `Animator` writes them — where each modifier type previously needed a handler of its own:
  - It rebinds when the style graph changes, so modifiers added or swapped at runtime are picked up.
  - It covers the component's own `Styles` only; a modifier living in a `StylePreset` or the project-wide preset is not animated by it.
  - A field whose change notification needs a value transition is skipped — drive it with a `UniTextDriver` clip instead.
- **`CharacterWidthModifier`** (default tag name `cwidth`): fits every character of a range into a fixed-width cell with the glyph centred inside it, from a width in em or pixels (`1em` for a full-width CJK cell, `0.5em` for a half-width one) or `auto`, which measures the fitted text's own widest glyph:
  - Letter and word spacing add their tracking on top of the finished cell, so tracking and fixed cells combine.
  - It appears in the style picker as **Character Width**, and TextMeshPro's `<mspace>` imports as one.
- **Gradient spread**: a gradient can now repeat or mirror where `Scale` or `Offset` leaves part of the surface uncovered, through a `Spread` setting — `Clamp`, `Repeat` or `Mirror`, with `Clamp`, the 3.1.0 behaviour, as the default:
  - The setting reads `Spread` on a paint swatch and on a range highlight — authored or the live text selection reached through `UniTextSelectable.SelectionHighlight` — and `Paint Spread` on every effect, honoured by the fill, stroke, shadow, glow and inner-shadow layers, and occupying each effect's final parameter slot so markup can set it positionally.
  - A swatch hides it for an `Angular` gradient, whose sweep already spans exactly one period, and a texture keeps taking its wrap from the texture asset.
  - `PaintSpread`, `PaintProjection.spread`, `TextPaint.spread`, `TextPaint.ApplySpread`, `PaintSpreadExtensions.Wrap`, `EffectModifier.ResolveSpread` and a `Gradient.Evaluate` overload taking a spread mode expose it to script.
- **`Fit` for radial and angular gradients**: `Fit` is no longer texture-only — `Contain` and `Cover` keep a radial gradient's ring circular and an angular gradient's angles true inside a non-square area, `Tile` sizes square cells, and the row appears on any gradient swatch whose Shape is not `Linear`; a swatch still carrying a non-`Stretch` `Fit` from when its source was a texture now applies that fit to its gradient, where 3.1.0 discarded it.
- **`Playback`**: a public, allocation-free weight animation any type implementing `IPlaybackHost` can drive — instant sets, eased transitions, one-shot pulses, deferred release and reduced-motion completion, on a scaled, unscaled or manual clock.
- **Reveal clock and manual time**: `RevealModifier.Clock` picks the time source appear and hide effects advance on — `Unscaled` (default), `Scaled` so `Time.timeScale` slow motion and pauses reach reveal effects, or `Manual`, which advances only through `AdvanceTime(float)` for frame-exact or externally driven reveals.
- **Reveal hide animations**: when the reveal frontier recedes, text now animates out instead of vanishing — the cluster keeps its place in the mesh, and under `Collapse` in layout, until its effect finishes:
  - **`HideHandler`** on a named reveal entry picks the effect for the way out; left unset, the entry's own handler replays backwards, so every built-in effect works in both directions.
  - Reversing mid-flight continues from the glyph's current state instead of restarting it.
- **`RevealGlyphInfo.hiding`**: `GlyphRevealing` subscribers can tell a glyph that is leaving from one that is arriving — `Progress` is the settled glyph at 1 in both directions.
- **Public `RevealGlyphInfo` constructor taking an explicit progress**: a reveal handler that wraps other handlers can give each child its own remapped timeline, the way `CompositeRevealHandler` does.
- **`RollingModifier.Spread`**: the rolling wheel's per-character settle offset is now an authorable parameter — editable in the inspector, targetable by a rule and drivable by a driver clip — where 3.1.0 accepted it only as the tag's first value.
- **`ParameterProviders.Invalidate`**: a custom parameter-option provider whose catalog lives outside Unity's object graph must now call it for open inspectors to rebuild their dropdowns; catalogs inside the object graph refresh on object, project and undo changes instead of on every editor tick.
- **Resolved parameter values in the inspection card**: the text inspection card now lists each modifier's declared parameters with the values resolved on the probed cluster, beneath the modifier list it already showed.

### Changed

- **Assets authored in 3.1 upgrade themselves**: the first editor load after the update rewrites scenes, prefabs and presets in place — interactive ranges with their selectors and playbacks, the reveal frontier, easing values, the retired letter-spacing monospace flag and paint overrides all land on their new serialized shape, with no prompt and no menu step:
  - Markup you typed is left as written: a `<cspace=…,true>` keeps its now-ignored second token and has to be re-authored as `<cwidth=auto>`.
  - Only serialized data upgrades — code naming a renamed type or member still has to be updated by hand.
  - A prefab instance that overrode one of the renamed serialized fields loses that override and falls back to the prefab's value.
  - A `UniTextPhaseDriver` left in a scene or prefab is not converted; the component no longer exists and its slot shows as a missing script.
- **Interaction-state effects are now rules**: the whole interaction-state vocabulary is renamed from "effect" to "rule", so every authored state graph and every line of code that touches one uses new type and member names:
  - Definitions: `RangeStateEffect` → `RangeStateRule`, `ModifierEffect` → `ModifierRule`, `PropertyEffect` → `ParameterRule` with `PropertyId` → `ParameterId`, and `InteractiveModifier.Effects` → `InteractiveModifier.Rules`.
  - Drivers became playbacks: `RangeEffectDriver` → `RangeStatePlayback`, `InstantEffectDriver` → `InstantPlayback`, `BuiltInPropertyDriver` → `TransitionPlayback`, `SignalProgressEffectDriver` → `SignalProgressPlayback`, `ManualEffectDriver` → `ManualPlayback`, and a rule's `Driver` → `Playback`.
  - Selectors: `RangeEffectSelector` → `RangeStateSelector`, with `Scalar…`, `All…`, `Any…`, `Not…`, `Interaction…` and `SpoilerConcealed…` taking the matching `…RangeStateSelector` names.
  - Rule values: `RangeEffectValue` → `RuleValue`, with `Float…`, `Unit…`, `Vector2…`, `UnitVector2…` and `Color…` becoming `FloatRuleValue`, `UnitRuleValue`, `Vector2RuleValue`, `UnitVector2RuleValue` and `ColorRuleValue`.
  - Supporting types: `RangeEffectScope` → `RangeRuleScope`, `RangeEffectEvent` → `RangeRuleEvent`, `RangeEffectClock` → `PlaybackClock`, `RangeEffectContext` → `RangeRuleContext`, `RangeEffectLifecycleHandler` → `RangeRuleLifecycleHandler`, and `IModifierEffectWeightReceiver.SetEffectWeight` → `IModifierRuleWeightReceiver.SetRuleWeight`.
  - The runtime: `UniTextEffects.For(text)` → `UniTextRanges.For(text)`, `RangeEffectInstance` → `RangeRuleInstance` with `GetProperty` → `GetParameter`, and the events `EffectEntered`/`EffectExited`/`EffectUpdated`/`EffectTriggered` → `RuleEntered`/`RuleExited`/`RuleUpdated`/`RuleTriggered`.
  - Consumer-facing reads: `RangeDecorationContext.EffectWeight` → `RuleWeight`, `UniTextRangeDebugSnapshot.Effects` → `Rules` of `RangeRuleDiagnostic` whose `DriverType` is now `PlaybackType`, and `InteractiveModifier.Ranges` → `InteractiveModifier.InteractiveRanges`.
- **Modifier properties are now parameters**: `ModifierProperty` / `ModifierProperty<TModifier,TValue>` became `ParameterDescriptor` / `ParameterDescriptor<TModifier,TValue>`, `ModifierPropertyComposition` / `ModifierPropertyCompositions` became `ParameterComposition` / `ParameterCompositions`, the handle `EffectProperty<TValue>` became `OwnedParameter<TValue>`, and `ParameterRule.SetTarget<TModifier,TValue>` takes a `ParameterDescriptor` where it took a `ModifierProperty`.
- **Reveal frontier is one `Front` parameter**: `RevealModifier` expresses the frontier as a single `Front` value carrying its own unit — a percentage of the covered text or an absolute grapheme-cluster position — in place of the separate `Fill` and `VisibleClusters`:
  - `Front` is the second parameter of the reveal tag, so markup can set it: `<reveal=fade,50%>` shows half and `<reveal=fade,12abs>` twelve clusters; a number with no suffix takes the field's own unit.
  - `Fill`, `VisibleClusters`, `FillProperty` and `VisibleClustersProperty` are gone — code that set them sets `Front` instead (`UnitValue.Percent(50f)`, `UnitValue.Absolute(12f)`).
  - An absolute frontier is fractional, so a position between two clusters blends the frontier cluster, where `VisibleClusters` was a whole count.
- **Easing values take custom curves**: `EasedRevealHandler.Easing`, `TransitionPlayback.Easing` and `SignalProgressPlayback.ExitEasing` now hold an `Ease` rather than a bare `EasingType`, so every built-in reveal effect and every rule transition can run a cubic-Bézier or authored keyed curve; code assigning an `EasingType` must wrap it (`Ease.Of(EasingType.CubicOut)`).
- **Paint override enums folded into the resolved enums**: `GradientShape`, `LayerBlendOverride` and `PaintTextureFit` are gone — shape, fit and blend are authored with `PaintProjectionKind`, `PaintFit` and `LayerBlend`, each of which now carries the shared `Inherit` member — so `HighlightModifier.Shape` and `Blend`, `BaseLineModifier.Blend` (underline and strikethrough) and `RangeDecorationPaint.Fit` all change type for code that assigns them.
- **Paint projection API takes resolved values**: for custom paint consumers `TextPaint.ApplyProjectionDetails` no longer reads tokens itself but takes already-resolved projection values, `ApplyProjection` gained a reader-less overload beside its reader form, `ApplyBlend` takes a `LayerBlend`, `ApplySpread` is new, `EffectModifier.ResolveBlend` takes the range context beside the reader with a reader-less overload and a matching `ResolveSpread`, and `BaseRangeDecorationModifier.ResolveDecorationPaint` takes a `PaintProjectionKind` where it took a `GradientShape`.
- **Custom shaders reading the paint channel**: the paint interpolator's fourth channel now carries the gradient's spread mode alongside the paint kind, so a hand-written shader that reads that channel directly must unpack it; a shader built from the shipped templates picks the change up unchanged.
- **Custom glyph animation modifiers**: `GlyphParamModifier<TParams>` replaced `ParseParams(ref ParameterReader)` with `ResolveParams(in RangeApplyContext)` and `OnGlyph` now takes the range's resolved phase as a final argument, so a hand-written animation modifier declares its values as `[Parameter]` fields, carries `[GenerateParameters]`, resolves through the passed context and uses the passed phase instead of the shared field.
- **Animation inputs resolve per range**: the externally driven inputs of the animation modifiers — `Phase` on `WaveModifier`, `WobbleModifier`, `BounceModifier`, `FloatModifier`, `PulseModifier`, `ShakeModifier`, `SpinModifier`, `PendulumModifier`, `GlitchModifier` and `ScrambleModifier`, plus `Progress` and `Rate` on `ScrambleModifier` and `Roll` on `RollingModifier` — now resolve for each range the modifier covers, so a driver clip can put every matched range at its own point; with nothing driving them all ranges resolve the same value and look exactly as before.
- **Attribute stores prepare once per parse**: a custom attribute store must now implement `IAttributeData.Prepare(int)`, which UniText calls once per parse so the store is always indexed by the codepoints of the text currently being laid out; `PooledArrayAttribute<T>.EnsureCountAndClear` is renamed `Prepare`.
- **`[Inheritable]` now comes from LightSide Core**: the attribute and its inspector override checkbox are no longer declared by UniText — unchanged in name, namespace and constructors, they ship in LightSide Core 2.0.5, which UniText now requires.
- **Reveal text animates out by default**: a named reveal entry whose clusters become hidden now plays its effect backwards, where the text used to disappear instantly; a `HideHandler` with `Duration` 0 keeps the instant hide.
- **An unreadable parameter value falls back to the style's own setting**: a tag whose value cannot be read — `<color=nonsense>`, a superscript tag with an unrecognised placement — now applies the value configured on the modifier, where 3.1.0 left that range unstyled.
- **255 painted ranges per paint layer**: one fill, stroke, shadow, glow or inner-shadow modifier now paints at most 255 applied ranges in a single text; glyphs covered only by ranges past that keep whichever range already won.
- **Cheaper style parameter changes on heavily styled text**: parameter changes at runtime — a hover state flipping, a driver animating a value, an inspector edit — are significantly cheaper on text carrying many styled ranges.
- **Colour rules animate without allocating**: rules driving a `Color32` parameter, and custom `Color32` range signals, no longer allocate while comparing values, so a running colour transition adds no garbage.
- **Operating-system fonts are picked by family, not by file location**: every entry in a `UniTextSystemFont`'s platform tabs — and the project's default operating-system face, the one used when no font is assigned and at the end of every fallback chain — is now requested from the operating system by family name instead of being looked for at a fixed set of font-file locations, so an entry resolves wherever the machine keeps that family installed:
  - When neither the platform tab's font nor the Common fallback is installed, the remaining catalog entries are tried in order and then the operating system's own default face, where the last resort used to be the first font file found in the system font folder.
  - A resolved face carries the variable-font axis values the operating system bound to it, so a variable system font starts from the instance the OS chose rather than the font file's own defaults.
- **Android system fonts follow the device's own roles**: the Android tab's entries now resolve to the device's own sans-serif, serif or monospace face, so `Roboto`, `Noto Sans` and `Droid Sans` all yield the real sans-serif of the device — a manufacturer-replaced UI font included — instead of failing when the device does not ship that exact font file.
- **System fonts resolve on first use**: a `UniTextSystemFont` now looks its font up the first time it is drawn or its data is read rather than when the asset loads, so `ResolvedFontName`, `ResolvedPath`, `ResolvedPlatform` and `ResolveFailed` stay unset until then.
- **TextMeshPro `<mspace>` survives conversion**: converting a TextMeshPro component whose text uses `<mspace>` now adds a `CharacterWidthModifier` bound to an `mspace` tag rule, where the tag was previously reported as having no UniText equivalent and left to render as literal text; the conversion report warns that TMP reads a unitless `mspace` value as font units while UniText reads it as pixels.
- **Default parameter rows copy and paste typed values**: copying a default-parameter row now puts the modifier field's own typed value on the shared inspector clipboard — pasteable into any field of that type, with only compatible values offered — where 3.1.0 exchanged the raw token text through the system clipboard.
- **`UniTextAnimationBridge` icon**: the component now shows an icon of its own in the Inspector, Hierarchy and Add Component menu.
- **One draw call for LightSide UI**: text, its highlights and underlines, UniShapes shapes and UniLottie animations now render through one shared material on a Canvas, so a run of them collapses into a single draw call instead of one per component type; a shape filled with a traced sprite and a paint filled with a texture still take one call each.
- **Shaders renamed**: `UniText/SDF`, `UniText/Lit/SDF`, `UniText/RangeDecoration` and `UniText/UI/RangeDecoration` are replaced by `LightSide/UI`, `LightSide/World` and `LightSide/World Lit`; materials in scenes and prefabs follow automatically, while a `Shader.Find` call naming an old shader in your own code has to be updated.
- **Custom text shaders**: the prelude includes are now `LightSide_Custom.cginc` and `LightSide_Custom-URP.hlsl`, the glyph atlases are global bindings that must NOT be declared in a Properties block, the paint texture and its keyword are named `_LightSidePaintTexture` and `LIGHTSIDE_PAINT_TEXTURE`, and the coverage helpers are `LightSideCoverage` and `LightSideInside`; existing custom shaders need these renames and stop compiling until then.
- **World text no longer needs the Lit shader**: unlit world text has a shader of its own, so excluding the Lit one leaves it rendering; a `UniTextWorld` with `Lit` on now warns in the Inspector when the project excludes the Lit shader.
- **Project Settings ▸ LightSide**: settings shared by every LightSide package live here, with `Include Lit Shaders` — moved from Project Settings ▸ UniText — and a list of which shaders the project carries into builds; UniText's own page is now nested under it.

### Fixed

- **World text rendered nothing in a build with Include Lit Shaders off**: turning the setting off left every `UniTextWorld` throwing at render time in a player, whether or not it was Lit, while the editor kept working.
- **`UniTextSystemFont` produced no text on iOS**: in an iOS player a `UniTextSystemFont` resolved to nothing whatever family its iOS or Common tab selected, warning that no usable font was found and leaving every text that used the asset unrendered.
- **Operating-system font lookup failed in the macOS and Linux editors under a foreign build target**: switching the active build target to Windows Standalone on a macOS or Linux editor — or to macOS on a Linux editor — made every operating-system font lookup fail with a missing-native-plugin error instead of using the host machine's fonts, so characters absent from the assigned font stopped falling back until the build target was switched back.
- **Linux DejaVu selections rendered in another family**: on Debian- and Ubuntu-based systems, choosing `DejaVu Sans`, `DejaVu Serif` or `DejaVu Sans Mono` — including through the Common tab's logical families, which map to them on Linux — rendered in a different family with a console warning even though the fonts were installed.
- **Paint layers ignored per-glyph scaling**: on glyphs scaled by `SizeModifier`, small caps, sub/superscript or ruby, pixel-sized layer geometry — stroke width, shadow offset and blur, glow radius, fill dilate and softness — grew or shrank with the glyph instead of holding the requested pixel size, and a texture paint's offset and outward growth did not follow the glyph at all, so its shadow fell short and a wide stroke or glow was clipped.
- **`ExtrudeModifier` Fixed Pixel Size drifted on scaled glyphs**: with Fixed Pixel Size on, the extrusion's offset, dilate and softness grew or shrank together with any glyph the text had already scaled — superscript and subscript, small caps, `SizeModifier`, ruby — instead of holding the authored on-screen size.
- **`FontModifier` ignored its configured family**: a whole-text Font style, or a bare tag carrying no family name, left the range in the base font — the family set on the modifier applied only when it was also spelled out inside the tag.
- **`LanguageModifier` ignored its configured language tag**: a whole-text Language style, or a bare tag, activated no region-specific glyph variants — the BCP 47 tag set on the modifier took effect only when repeated inside the tag.
- **Reveal effect kept playing on replaced text**: changing the text or moving a revealed range while an appearance effect was still running left that effect playing at the same positions in the new text, so already-settled glyphs briefly faded, dropped or scaled in for no reason.
- **Reveal effects stalled in the editor**: in edit mode a reveal effect only advanced while that reveal modifier's own section was on screen in the Inspector, so a reveal driven from anywhere else — an animation preview, an `[ExecuteAlways]` script — could leave glyphs frozen part-way through their effect.
- **Fit steps dead without Word Wrap**: with Word Wrap off, Auto Size ignored the `FitSteps` ladder entirely — the font just shrank with no letter-spacing or glyph-width compression, and no compression ever appeared while narrowing the box.
- **`MeasureText` reported an oversized width under Auto Size fit steps**: with Auto Size on and `FitSteps` configured, `MeasureText` returned a width wider than the text actually draws, because the letter-spacing and glyph-width compression the fit had already chosen was missing from the measurement.
- **Parameter dropdown showed a value the field did not hold**: a parameter or default-parameter dropdown whose stored token was not among its options — a paint or catalog entry that had since been renamed or removed — displayed the first option instead of the value actually stored.
- **Parameter dropdown ignored changes to its catalog**: a parameter dropdown whose option catalog was empty when the inspector drew it stayed a plain text box after entries were added to the catalog.
- **Unit selector named the wrong unit**: a unit-valued parameter whose modifier names its absolute unit `abs` — `VariationModifier`'s weight, width, slant, italic and optical size — showed `px` in the unit selector, and the token written for it spelled `px` too.
- **Scene visibility overlay drawn without its icon**: UniText's Scene-view visibility overlay showed no icon in the Scene view's overlay menu when UniText was installed as a package.

### Removed

- **`UniTextPhaseDriver`**: the phase-advancing component is gone — a looping `UniTextDriver` clip on an animation modifier's `Phase` parameter is the replacement — and scenes or prefabs that carry one lose the component, with no migration re-creating the motion.
- **`IPhaseDriven`**: the interface a phase-driven modifier implemented is gone; the animation modifiers still expose a `Phase` parameter, and a custom modifier that implemented the interface only needs to drop it from its declaration.
- **Per-modifier Animator handlers**: `ModifierAnimationHandler` and the shipped `ColorAnimationHandler`, `SizeAnimationHandler`, `LetterSpacingAnimationHandler`, `GlowAnimationHandler`, `ShadowAnimationHandler`, `StrokeAnimationHandler` and `PhaseAnimationHandler` are gone; no migration converts them, so a `UniTextAnimationBridge` listing one loses that entry on load and must be pointed at `ModifierFieldsAnimationHandler`, which covers the fields of every modifier in the component's `Styles`.
- **`IHasModifierProperties` and the per-modifier `…Property` descriptors**: the interface a modifier implemented to expose animatable properties is gone, together with `RevealModifier.FillProperty`, `RevealModifier.VisibleClustersProperty`, `HighlightModifier.TintProperty`, `PaintLayerModifier.TintProperty` and `StrokeModifier.WidthProperty` — a modifier's parameters are reached through its generated `Param` class and `ParameterDescriptor.Find` instead.
- **`LetterSpacingModifier.Monospace`**: the monospace flag on letter spacing is gone in favour of `CharacterWidthModifier`; existing assets are converted automatically into a Character Width entry of measured width covering the same text, while markup that switched it on through the letter-spacing tag's second token has to be re-authored as `<cwidth=auto>`.
- **Layout-invalidation warning on animated parameters**: the text, Style Preset and Modifier Graph Preset inspectors and the Range Debugger no longer warn when an animated parameter of a custom modifier invalidates text or layout.
- **Glyph-compression inspector warning**: the Auto Size section no longer shows a warning when a `GlyphScaleFitStep` limit exceeds 3%; the field tooltip still names the distortion threshold.

## [3.1.0] - 2026-08-10

### Added

- **Auto Size fit steps** (`FitSteps` list next to Auto Size): budgets Auto Size spends, in list order, before it starts reducing the font size — and only as far as each actually buys font size back:
  - **`TrackingFitStep`**: tightens letter spacing, up to a limit in em (default 0.02, the tightening iOS applies before truncating).
  - **`GlyphScaleFitStep`**: compresses glyph width, quads and advances together (default limit 2% — the hz/InDesign print norm; above ~3% letterforms visibly distort, and the inspector warns).
  - **`LineHeightFitStep`**: compresses line height down to a percentage of natural (default 90%).
- **Justification spacing budgets** (`AlignmentModifier`): justified paragraphs now distribute space the way print engines do — word spaces stretch up to `WordSpacingMax` before letter spacing (`LetterSpacingMin`/`Max`, % of em) and glyph scaling (`GlyphScalingMin`/`Max`) spend their budgets, with space beyond every budget still landing on word spaces; the defaults reproduce the previous appearance exactly.

### Changed

- **Line height follows the text size on the line**: every line-height mode now measures the line at the largest font size on it, so a line whose visible content is entirely scaled by `SizeModifier` shrinks or grows with it while a line that also carries unscaled text keeps its base height — previously only enlargement moved the line, and it opened more space than the scaled text needed.
- **Overflowing justified lines compress toward the margin**: a justified line wider than its box (an unbreakable word, a long URL) now pulls word spaces in — down to `WordSpacingMin`, 80% by default — and, when letter-spacing or glyph-scaling budgets are configured, spends those too, instead of always sticking out past the edge.

### Fixed

- **Font inspector status lines unreadable on the light editor skin**: the font-data, compression and system-font resolution lines were drawn in dark-skin greens, blues and ambers that washed out against a light background.
- **Clear actions unreadable on the light editor skin**: the red marking "Clear Runtime Data" and the clipboard window's Clear as destructive was a dark-skin red.
- **Negative paragraph spacing ignored**: `ParagraphSpacingModifier` accepted a negative `Before` or `After` value and then silently discarded it, so paragraphs could only be pushed apart, never pulled closer together.
- **Phantom `WholeText` entry in a style's source picker**: the `Source` dropdown listed a second `WholeText` option beside the "Whole Text" default, and choosing it failed with a missing-constructor error instead of selecting anything — leaving `Source` unset remains the way to style the whole text.

## [3.0.1] - 2026-08-09

### Added

- **`UniTextWorld.Standalone`**: draws the text through a renderer of its own instead of merging into the shared batch mesh, so it takes its own depth-sorting position and sprites, Entities Graphics instances and other transparent renderers can sort between it and neighbouring world texts — at the cost of one draw call.

### Changed

- **World range decorations draw with their text**: highlights, selections and other range decorations on `UniTextWorld` are now part of that text's own mesh instead of adding a renderer of their own, and they follow it when `Standalone` is on.

### Fixed

- **Range decorations on world text covering their neighbours**: a highlight or selection on one `UniTextWorld` was drawn over — or under — every other world text sharing its sorting order instead of only its own.
- **Range decorations left on screen by `Hide`**: hiding a `UniTextWorld` kept its highlights and selection visible.
- **`SortingGroup` ignored when added to live world text**: a `SortingGroup` added, removed or disabled after a `UniTextWorld` had become active never took effect — the only way to make one apply was to add it before the object entered the hierarchy — and a disabled group won over an enabled one higher up.
- **Per-glyph fades ignored under a gradient fill**: a typewriter reveal or a per-character alpha ramp had no effect on text painted by a gradient `FillModifier` — the whole range stayed fully opaque, while the same text under a solid fill faded correctly.
- **`SpoilerModifier` cover showed the text through it**: the concealing cover was drawn at 96% opacity, leaving the hidden words faintly legible underneath.
- **`HighlightModifier` block geometry covering neighbouring words**: with `GeometryMapping.Block`, a highlight whose wrapped lines did not overlap horizontally stretched itself across words outside the range in order to join them into one shape, and the further apart the lines were — or the larger the `CornerRadius` — the more of the neighbouring text it swallowed. Such lines are now drawn as separate surfaces, each covering only its own range, with the paint still mapped across the range as a whole.
- **Android: system Back did nothing while a text field was focused**: the keyboard-dismiss button — the ∨ that replaces Back in the navigation bar while the keyboard is up — left the keyboard open, and the app's own back navigation never received the key either; builds with Unity's Predictive Back Support enabled, and Android 13 and 14, were unaffected.

## [3.0.0] - 2026-08-07

This is the largest release in UniText's history. It does three things at once:

1. **Renderer → editor.** UniText becomes a full interactive rich-text *editor*: selectable text, an editable document model, a multi-format system clipboard, caret/selection UI, IME composition, OS input that works regardless of the project's Active Input Handling setting, and a composable, fully extensible input model.
2. **A new visual layer.** A unified paint system (solid / gradient / texture) drives fills, real rim strokes, glows, drop shadows and inner shadows; a phase-driven kinetic-text animation suite replaces the single 2.x wobble; and custom shader effects now compile for Canvas, world-space, Built-in **and** URP from one source.
3. **A faster foundation, split out.** Cold-glyph rasterization now costs the main thread almost nothing, the Unicode analysis pipeline is Burst-compiled and per-paragraph parallel, unchanged paragraphs are never re-analyzed or re-shaped, and the whole non-text foundation is extracted into a new, free, MIT-licensed **LightSide.Core** package that UniText now sits on top of.

Behaviors follow first-source platform conventions (iOS UIKit, Android, Web/HTML, macOS AppKit)
rather than reimplementing any single one. Every variation point — input behaviors, field
decorators, context-menu items, input filters/validators, range stylers, parse rules, paint
providers, and glyph animations — is an author-your-own `[SerializeReference]` extension point
that appears in the inspector type picker alongside the built-ins; clipboard serialization
schemas are the one code-registered seam (`ClipboardModifierBindMap.Register<T>`, IL2CPP-safe).

### Added

#### Two packages: UniText + LightSide.Core

- **New `media.lightside.core` package** ("LightSide Core", v1.0.0, Unity 2022.3+, **MIT / free**). UniText's non-text foundation now lives in a standalone package — object pooling (`ArrayPool`), the worker-thread pool (`WorkerPool`, `MainThread`), math/hashing (`XxHash64`, `HashNoise`), collections (`FastIntDictionary`, `SpanIntern`), named catalogs (`INamedCatalog`/`AssetNamedCatalog`/`InlineNamedCatalog`), color/gradient parsing (`ColorParsing`, `Gradient`), the `Cat` logging system, the touch-gesture recognizer, a full custom-inspector toolkit, and the byte-preserving asset-migration framework (`IMigration`, `IMigratedPackage`, `RenameManagedType`/`MoveFieldsToStruct`, per-package ledger) — the machinery behind UniText's automatic v2→3 upgrades, open to any package on Core. Assemblies `LightSide.Core` / `LightSide.Core.Editor`, root namespace `LightSide`.
- **Open-core boundary.** The shared foundation is MIT and free; UniText's text engine stays the commercial package. The two version independently, and UniText 3.0.0 takes a hard dependency on `media.lightside.core` 1.0.0, resolved automatically through the `media.lightside` scoped registry — one install pulls both.
- **Source-compatible split (two renames).** Every moved type kept its `namespace LightSide`, so existing `using LightSide;` code still resolves. Two utilities were renamed in the move — `UniTextArrayPool<T>` → `ArrayPool<T>` and `RangeEx.WholeText`/`IsWholeText` → `RangeEx.All`/`IsAll`, no aliases — everything else compiles unchanged.
- **Reusable inspector toolkit in Core.** The `[SerializeReference]` type picker (`Selector`, `TypeSelectorDrawer`, `ManagedReferenceTypeMenu`), reorderable styled lists (`StyledListUtility`, `ListReorder`), and section/vector/padding widgets that power UniText's inspectors are now a Core editor toolkit any project can drop into its own editors.
- **GPU glyph-upload promoted to Core.** The regional texture-upload backend (native plugin renamed `unitext_gpu` → `lightside_gpu`) became a public `GpuUpload` API in `LightSide.Core/Runtime/Gpu`, rebuilt around a device-epoch/device-reset-aware pool with a synchronous WebGL2 bridge — shared infrastructure across LightSide packages.
- **Built-in profiler (advanced/developer tooling).** Core ships `Prof` — a lock-free per-thread Enter/Exit sink that reconstructs the call tree with self-time and per-method allocation — driven by `ProfWeaver`, a Unity `ILPostProcessor` (Mono.Cecil) that auto-wraps method bodies, inert unless `PROF_ENABLE` is set and the assembly is allowlisted (`Window > LightSide > Profiler Weaver` → `ProjectSettings/ProfWeaver.txt`). `ProfSampler` adds a zero-per-call statistical backend for IL2CPP players (a native plugin walks the real stack into a lock-free ring), and development-build captures stream to the editor over the player connection into `Window > LightSide > Profiler`.
- **One LightSide editor menu.** Shared tools consolidated under `Tools/LightSide/` — the Noise Generator moved there from `Tools/UniText/`, joined by a new `Log Zones` window (live on/off panel for the `Cat` logging system's zones).

#### Selectable & editable text components

- **`UniTextSelectable`**: read-only text selection for both Canvas (uGUI) and world-space text. Click to place a caret, double-click to select a word (a drag continuing from it extends by whole words), triple-click a paragraph, drag to select, Shift to extend, right-click/long-press to select-word-and-open-menu, keyboard Copy and Select-All while focused. Public mutators (`SetCaret`, `SetSelection`, `ExtendSelection`, `SelectWord`/`SelectLine`/`SelectParagraph`, `SelectAll`, `ClearSelection`, `DragSelectionHandle`, each returning `bool`) plus `GetSelectedText` and gesture drivers (`HandlePrimaryClick`, `DispatchTap`/`DispatchDoubleTap`/`DispatchTripleTap`, `BeginDrag`/`UpdateDrag`/`EndDrag`). Single-selection-per-document with EventSystem-driven defocus.
- **`UniTextEditable`**: editable text — a sibling of `UniTextSelectable` on the same GameObject. Implements `ITextDocument` and `ISavedStateProvider`. It tracks the text size through the text component's `ILayoutElement` — add a `ContentSizeFitter` or place it under a layout group. Field chrome (background, viewport, scrolling, placeholder, labels) is assembled from standard Unity layout components plus field decorators, not a fixed component. Content typing is composed from input behaviors/filters/validators, not a content-type enum. All OS input is delivered independently of Unity's input system.
- **Zero-alloc text access**: `CharCount`/`CodepointCount`, `CopyTextTo(Span<char>)`, `TextEquals(ReadOnlySpan<char>)`, with surrogate-pair-safe codepoint indexing — poll and compare a field every frame without touching the GC (the `Text` getter is the opt-in allocating path).

#### Text selection model & BiDi

- **`TextSelection`** (readonly struct): `Anchor`/`Focus`/`Affinity` over codepoint indices, with `IsCollapsed`, `Start`/`End`/`Length`, `Clamp`, a `Caret(index, affinity)` factory, and full value equality. Direction is implicit in anchor/focus ordering, modeled on the W3C Selection API / Flutter `TextSelection`.
- **`CaretAffinity`** (`Downstream`/`Upstream`): one unified hint that disambiguates caret rendering at both soft-wrap line breaks and LTR↔RTL BiDi run boundaries.
- **BiDi-aware hit-testing** (`SelectionHitTest`, `UniTextBase.HitTestCaret`/`IsOverText`): edge-snapped caret placement (left half of a glyph → cluster N, right half → N+1), closest-edge resolution with swapped left/right semantics inside RTL runs, grapheme-cluster snapping, and a web line-box "over text" model for the I-beam affordance. Line resolution binary-searches a per-line advance prefix and the within-line scan touches only that line's glyphs — a click costs O(log lines + line), never the document.
- **Word/line/paragraph boundaries**: UAX #29-aware word segmentation with VS Code / Windows / macOS Ctrl/Option+Arrow semantics — apostrophes in words (`don't`, and the curly `don’t`), digit separators (`3.14`, `1,000`), Katakana as its own script run, Han ideographs and Hiragana broken per cluster, Thai/Lao/Khmer/Myanmar snapped to dictionary word boundaries (Thai dictionary in the box; register others via `UniTextSettings.Dictionaries`), emoji/ZWJ/variation-selector clusters never split.
- **Selection events**: `SelectionChanging` (vetoable — `Cancel` or reassign `Proposed` to clamp a proposed change out of an atomic pill or read-only region), `SelectionChanged` (with a reason), and a 14-value `SelectionChangeReason` open-string vocabulary (`select.pointer`, `select.word`, `select.extend`, `select.clamp`, …) matching the CodeMirror 6 convention.

#### Caret navigation & rendering

- **Grapheme-cluster caret movement**: one arrow press or one Backspace crosses a whole emoji, ZWJ sequence, or base+combining-mark cluster — caret positions always sit on grapheme boundaries.
- **Vertical movement with a preserved column anchor**: Up/Down/PageUp/PageDown keep a desired-X column across consecutive vertical moves, reset on any horizontal/word/edit/click.
- **RTL-base-aware horizontal movement**: in an RTL line, Left/Right step toward logical start/end (Android `getParagraphDirection` convention); word movement obeys the same flip. The empty last line after a trailing newline follows the preceding paragraph's direction, matching mainstream editors.
- **`InputCaretRenderer`**: the caret component — a `RectMask2D`-clipped quad with blink-on-interval, blink-idle-timeout-then-steady, immediate geometry on type (no one-frame lag), and configurable width/color; subclass it (override `OnPopulateMesh`) for a block, underline, or gradient caret.

#### Editing & document model

- **Edit operations** on `UniTextEditable`: `InsertText` (string and `ReadOnlySpan<char>`), `DeletePrevious`/`DeleteNext`, `DeleteSelection`, `DeleteWordPrevious`/`DeleteWordNext`, `DeleteToLineStart`/`DeleteToLineEnd`, `TransposeCharacters` (carries adjacent formatting through the swap), `SelectAll`, `Select`, `MoveCaretTo`. Delete/transpose boundaries are computed over the document with a bounded UAX #29 window — never a stale previous-frame layout — so key-repeat and frame hitches can't delete the wrong cluster.
- **Undo/redo**: `Undo`/`Redo`/`CanUndo`/`CanRedo`/`ClearUndoHistory`, with word-aware token-class coalescing (the VS Code / CodeMirror 6 word↔separator rule, held-Backspace merged, Replace never coalesced), a configurable time window (UniText settings, default 0.5 s), and an allocation-free store — undo text lives in a shared `char[]` referenced by slices, capped by the settings' undo memory limit (default 1 MB, oldest-first eviction, the newest entry always survives; 0 = unlimited). Restores the pre-edit selection on undo; multi-step commands (styled typing, IME commit over a selection) group into one undo step.
- **Programmatic set-text with explicit undo policy**: `SetText` (replace, reset selection, clear history) vs `SetTextProgrammatic(value, recordUndo = false, preserveHistory = false, reason = null)` — `preserveHistory:true` rebases existing undo entries through the swap's minimal diff, so a collaborative/network echo that only appends keeps the user's local undo (CodeMirror `addToHistory:false`); `reason` tags the event (e.g. `sync.network`).
- **`ITextDocument`**: a read-only, codepoint-indexed document abstraction (`CodepointCount`, `CharCount`, `Version`, `CopyCodepointRange`, `GetCodepointAt`) that validators, find/replace, and accessibility consumers depend on instead of the concrete editor.
- **`EditApplied` + `EditShape`**: every mutation raises `EditApplied(EditShape)` synchronously (after `Version` advances, before the frame-coalesced `TextChanged`) carrying `Start`/`Removed`/`Inserted`/`Delta`/`MapIndex` — the zero-alloc damage hint for keeping indexed state (search results, CRDT positions, highlight ranges) in sync without rescanning.
- **Change-reason vocabulary** (`TextChangeReason`): open dotted-prefix strings carried by `DocumentChanged` — `input.type`, `input.type.compose`, `input.paste`, `input.delete[.backward|.forward]`, `input.cut`, `input.format`, `input.restore` (`input.*` is exclusively user input), `program.set` for programmatic mutation, `sync.network` for collaborative sync, and `edit.style` for formatting-command tag rewrites — so integrators react differently to typing vs paste vs programmatic vs IME edits with one prefix check.
- **Events**: `TextChanged` (zero-alloc), `ValueChanged`, `DocumentChanged` (with reason), `SelectionChanged` (frame-coalesced), `Submitted`, `Cancelled`, `Focused`/`Defocused`, `CompositionStateChanged`, `TouchKeyboardVisibilityChanged`, `ValidationChanged`.
- **`ISavedStateProvider`**: `SaveState`/`RestoreState` persist text, selection, affinity, and scroll into a `unitext.*`-prefixed bundle for host-driven view recreation (virtualized lists, mobile process-death restore, Web bfcache).
- **Input sanitization**: lone surrogates and C0/DEL control chars dropped, `\r\n` collapsed to `\n`, NUL rejected, with a 1,000,000-char paste safety cap — the field is safe to point at arbitrary pasted data.
- **`GraphemeCountCache`**: grapheme-cluster count cached by document `Version`, predicting a pending edit by re-segmenting only a boundary-safe window — live counters and grapheme length limits stay O(window) per keystroke instead of a full rescan.

#### Keyboard input & edit actions

- **`EditAction`** enum: the full editing action set — movement (char/word/line/page/document), selection variants of each, delete (prev/next/word/line), `TransposeChars`, `InsertNewline`/`InsertTab`, Copy/Cut/Paste/PasteAsPlain, Undo/Redo, Submit/Cancel — plus `Ignore` (consume a key and block its platform default) and range constants for permission filtering.
- **Platform-aware key resolution** (`PlatformKeySemantics` + a built-in key map): macOS (Cmd shortcuts, Option word-nav, Cmd+←/→ line, Cmd+↑/↓ document, plus default Emacs bindings Ctrl+A/E/F/B/P/N/K/T/D/H) vs Windows/Linux (Ctrl shortcuts, Ctrl+Y redo, Ctrl+Home/End, Shift+Insert/Ctrl+Insert/Shift+Delete, AltGr-safe Ctrl+Alt), with Cmd-vs-Ctrl resolved at runtime (a macOS-browser WebGL build and an iPad hardware keyboard get Cmd). Read-only mode runs only navigation/selection/Copy/Submit/Cancel; mutating actions are suppressed during IME composition.

#### IME & composition

- **Non-destructive composition**: in-progress IME text renders without being written to the document until the IME commits, and the commit lands as a single undo entry. CJK candidate windows, dead keys (´+e→é), and accent menus work correctly; composition renders unmasked in password fields. The overlay commits synchronously on defocus/`SetText` so text is never lost when the user taps away or switches apps, and late echoes from a killed session are discarded.
- **Clause model** (`CompositionData`, `CompositionClauseStyle`): per-clause attributes (macOS/iOS `NSMarkedClauseSegment` runs, Android composing spans) are normalized into one neutral clause vocabulary — `Unconverted` / `TargetConverted` / `Converted` / `TargetNotConverted` / `Error`, the Windows IMM `ATTR_*` taxonomy — so integrators reason about conversion state without per-platform code; a platform that delivers no clause detail reports one `Unconverted` clause.
- **Candidate-window placement**: the caret position is reported in native client space (`UniTextNativeInput.SetCursorScreenPos`, Editor Game-View letterbox-compensated), and `SetImeCaretScreenRect` pins the window for inline **Scene-view** editing — so the candidate/emoji/accent window sits on the caret in a build, the Editor Game View, and the Scene View.
- **Text-input-context queries answered from UniText's own layout**: `TryGetCharRangeRect` (first-rect-for-range), `HitTestChar` (closest position to point), `WritingDirection` (per-codepoint UAX #9 levels), and a grapheme/word `TokenizerQuery` — feeding macOS `NSTextInputClient`, iOS `UITextInput`, dictation, VoiceOver character/word reading, and candidate placement from real shaped glyphs, not an approximation.
- **`ITextInputContext`**: the neutral document-context seam the native backends query. `UniTextEditable` installs itself while active; a console, terminal, or custom text host installs its own context and receives full native IME. `CompleteComposition`/`CancelComposition` give hosts programmatic commit/discard; IME-driven caret moves (Gboard spacebar-swipe, `InputConnection.setSelection`) are applied as input.
- **Android IME context mirror**: the focused document and selection are mirrored into the platform `InputConnection` — the whole text when ≤ 1,024 UTF-16 chars, else a ~512-char window centered on the selection and snapped to grapheme boundaries, coalesced to one push per frame and cached by document version — so Gboard suggestions, autocorrect, and spacebar cursor control see real surrounding context with zero per-keystroke allocation; pull-model platforms (Windows, macOS, iOS) skip the push entirely.

#### Native input & soft keyboard

- **`UniTextNativeInput`**: delivers OS key, text, composition, selection, and keyboard-visibility events independently of Unity's input system (no `Event.PopEvent`), so **editing works regardless of the project's Active Input Handling** (Legacy / New Input System / Both) — removing the single biggest friction point of every Unity input-field asset. Control keys arrive only via key-down; printable text only after the OS resolves layout/dead-keys/IME. Cross-platform `NativeKeyCode` and `[Flags] NativeModifiers`.
- **Windows Text Services Framework text store**: the new `UniTextNativeInputWindows.dll` (x86_64 + ARM64) hosts an `ITextStoreACP` document, so the Win11 emoji/GIF panel (Win+.), clipboard history (Win+V), and modern TSF IMEs insert into the field with the candidate window anchored at the caret — even under the New Input System.
- **Pluggable input backends** (`INativeInputBackend`, `RegisterBackend`): a priority-ranked registry with a `ManagedInputBackend` fallback registered on every platform, so a missing/failed native plugin (or an unsupported platform) still types, and studios can add first-class input for consoles/TVs without forking the package.
- **`NativeKeyboardConfig`**: soft-keyboard traits the OS reads on show — `KeyboardType`, `ReturnKeyType`, `AutoCapitalization`, `AutoCorrection`, `SpellChecking`, `AutofillHint`, iOS smart-quotes/dashes/insert-delete opt-out, `KeyboardAppearance`, return-key auto-dim, Done toolbar, password rules, `AndroidImeFlags`, and raw iOS keyboard/return-key overrides. Every option defaults to the user's OS preference. (iOS coverage is full; Android maps type/caps/correction/spell/return/ime-flags; WebGL maps inputmode/enterkeyhint/autocapitalize/autocorrect/spellcheck/autocomplete.)
- **`NativeFieldOverlayStyle` / `NativeFieldHandle`**: opt into the OS's own field (`UITextField`/`UITextView`, `EditText`, DOM `<input>`/`<textarea>`) for system selection handles, autofill chrome, voice input, and per-platform styling — the only route to true native UX inside a WebGL browser sandbox. `NativeFieldHandle` returns the raw native-view pointer for further customization (functional on iOS today).
- **Keyboard-avoidance data** (`KeyboardEvent`, `KeyboardEventPhase`, `KeyboardEasing`): will-show/animation-progress/did-show/will-hide/did-hide/will-change-frame phases with duration, easing, and a frame-synced animation fraction where the OS exposes it (Android API 30+), plus a documented client-side fallback (iOS, Android <30, WebGL).
- **Soft-keyboard action button** (`NativeEditorAction`): the keyboard's action key is delivered as an open vocabulary — `Submit`/`Next`/`Previous`/`Newline`, with Go/Search/Send/Done all delivering `Submit` — so a chat composer submits and a form field advances focus, degrading to a synthesized Return with no subscriber.
- **Native plugins shipped and CI-built**: Android `UniTextInput.aar`, macOS `UniTextNativeInputMacOS.dylib` (an `NSTextInputClient` view), Windows `UniTextNativeInputWindows.dll` (x64 + ARM64), iOS `UniTextNativeInput.mm` (a `UITextInput` view), and WebGL `.jslib` — full native IME and soft-keyboard support in the box, no external SDK.
- **Automatic hardware cursor**: an I-beam appears over text and a hand over links, from a 25-shape internal cursor table mapped to per-platform IDs (Windows `OCR_*`, macOS `NSCursor`, WebGL CSS `cursor`; Linux keeps the default arrow); an interactive range's hover cursor is configurable per modifier in the inspector (serialized `hoverCursor`, default: link hand).

#### Clipboard

- **Multi-format, multi-channel clipboard**: one Copy writes many formats atomically and Paste auto-selects the richest understood one — `ClipboardFormat.PlainText`, `Html`, `Markdown`, `UniTextSource` (lossless UniText vendor format), `Url`, and `Png`/`Jpeg`/`Gif` images. `ClipboardItem` pairs a MIME-string `ClipboardFormat` (with `Custom(string)` and `TryDetectImage`) with bytes; bypasses `GUIUtility.systemCopyBuffer` on the six supported platforms (a plain-text fallback covers anything else).
- **Format adapters** (`IClipboardAdapter`): per-format translators between UniText markup and external formats, ordered by `Priority` on paste (lossless UniText source 100, HTML 50, Markdown 40, plain text 0 — the richest present format wins). Four shipping stateless built-ins: `UniTextSourceClipboardAdapter`, `TagHtmlClipboardAdapter`, `MarkdownClipboardAdapter`, `PlainTextClipboardAdapter`.
- **Data-driven modifier serialization schemas** (`ModifierClipboardSchema`, `ClipboardModifierBindMap`): each modifier type declares how it maps to HTML (semantic element, CSS-on-`<span>`, or attribute) and Markdown (marker or link), registered per type with no assembly scan (IL2CPP-safe). Pre-registered for the 15 shipping modifiers (bold, italic, underline, strikethrough, color, size, font, link, language, line-height, letter-spacing, script position, small-caps, upper/lowercase). Register a custom modifier's mapping with one `ClipboardModifierBindMap.Register<T>` call — this is the per-modifier extension seam, not custom adapters.
- **Word / Outlook / Google-Docs interop**: the HTML paste parser is a single forward pass with an explicit element stack — it strips leaked `<style>`/`<script>` blocks, collapses whitespace the way CSS renders it, resolves entities, closes mis-nested tags, and honors the Windows CF_HTML fragment header — so pasted rich text comes in clean, not as a wall of CSS and blank lines. CSS unit bridging (`CssValueFormat`) converts `px`/`pt`/`rem`, unitless line-heights, and `rgb()`/`rgba()` colors into UniText's own units on paste and emits valid CSS units on copy — including Google-Docs `font-weight` conventions.
- **Lossless UniText round-trip** (`ClipboardFormat.UniTextSource`, `application/vnd.lightside.unitext`): a JSON fragment keyed by modifier signature re-resolves each span by modifier identity and re-emits it in the destination field's own syntax — copy between two of your fields and keep every style byte-perfect, even styles no HTML or Markdown could express.
- **Style-safe partial copy**: copying part of a styled run re-synthesizes the open or close tag the selection cut off, in the pair's original syntax — a half-selected `<b>` span (or `*bold*` marker run) copies as valid, still-styled markup instead of a dangling tag.
- **Paste policies & commands**: `PlainTextPastePolicy` (`Auto`/`Literal`/`Parse`), `Paste()`, `PastePlain()` (paste-and-match-style), `Cut()`/`Copy()`, async `PasteAsync()`/`PastePlainAsync()` (required for programmatic paste on WebGL), and `PasteFromItems(IReadOnlyList<ClipboardItem>)`.
- **Media clipboard** (`MediaContent`, `MediaSource`, `IMediaClipboardProvider`, the `MediaReceived` hook): receive pasted images and files before the text channels run, and read their bytes per platform (CF_HDROP paths on desktop, `content://` on Android, browser blob on web; large files off-thread) — paste a screenshot into a chat field and upload it.
- **Windows screenshot & image interop**: copies write the registered `PNG` format plus a synthesized `CF_DIBV5`, and reads convert `CF_DIB`/`CF_DIBV5` back to PNG — so a copied image pastes into Paint/Word and a PrintScreen capture pastes straight into a field.
- **Provider abstraction** (`UniTextClipboard` facade, `IClipboardProvider` / `IAsyncClipboardProvider`): swap the backend for tests or a custom platform behind one interface; static `GetText`/`SetText`/`GetTextAsync` cover the plain-text fast path.
- **Six native backends**: Windows CF_HTML + registered formats + CF_DIBV5 + CF_HDROP; macOS `NSPasteboard` multi-format writes via the Obj-C runtime (no compiled plugin); iOS `UIPasteboard` items; Android `newHtmlText` + a bundled `content://` image provider (no androidx dependency); WebGL Async Clipboard API + Web Custom Formats; Linux xclip/xsel/wl-copy — a real OS clipboard everywhere, not Unity's plain-text-only buffer.
- **Hostile-input hardening** (`ClipboardBudget`): every paste parser caps at 4 MB / nesting depth 64 / 1,000,000 chars and stays linear on degenerate input, so a malicious clipboard payload can't hang or OOM the app.

#### Rich-text formatting (editor)

- **Apply/toggle styles over a selection**, in the style's own markup syntax: `ApplyStyle<T>`, `ApplyStyleRange<T>`, `SetStyle<T>` (range on/off), `ToggleStyle<T>`, `ToggleStyle(BaseModifier, …)`, `RemoveStyleRange<T>`, `ClearFormatting`, `InsertObject<T>`. **Reference-only** — a style the field has no configured rule for is a no-op; nothing is ever minted. Toggle-off splits runs so the style survives outside the range, with single-undo semantics.
- **Pending typing styles** (ProseMirror stored-marks model): at a collapsed caret a style command stores a pending style that wraps the next typed text; caret movement discards it, and `ArrowKeyEscapesFormatting` (off on a bare editable; `TextFormattingBehavior` turns it on) peels one formatted range at a time at its edge — the ProseMirror/Lexical mark-boundary model.
- **Toolbar-state queries**: `IsStyleActive<T>()`, `TryGetStyleParameter<T>(out …)` (`false` on a mixed selection), `ModifiersAtCaret`, and a frame-coalesced `CaretContextChanged` event delivering a `CaretContext` diffed by modifier signature — bind a formatting toolbar's pressed/color state without per-frame polling.
- **`MarkupVisibility`** — `Hidden` (tags hidden and atomic but preserved so styles round-trip), `RevealActiveRange` (reveal the markup of the span under the caret, like a code editor), `Raw` (literal source view). Caret, selection, hit-testing, and deletion stay correct while tags are hidden — deleting the last styled char removes the whole `<b>…</b>` pair in one undo step.
- **`TypingMarkupPolicy`** (`Parse`/`Literal`): markup the user types by hand is either recognized or escaped to literal text; affects only keystroke/IME input, not formatting commands or paste.
- **`ChromeRule` + `IMarkupSelector`**: style the visible tag delimiters when revealed (`AnyMarkup` / `ByModifier` / `ByRule`, by specificity), with tag characters protected from the surrounding document style; runtime-mutable via `MarkupChrome` + `RefreshMarkup`.
- **Parse rules describe and re-emit their own syntax** (`ParseRule.Identity`, `SourceToken`, `MarkupTriggers`/`ScanTriggers`, `CanWrap`, `Apply`, plus the literal-escape contract `EscapePrefix`/`IsEscapable`): this is the mechanism that lets one formatting command drive a tag rule (`<b>`) and a Markdown marker rule (`*bold*`) alike — the core "it is now an editor" story.
- **Style identity by modifier signature**: toggling Bold finds the field's own `<b>` style instead of minting one — styles are matched by concrete modifier type, a composite by its ordered child types ({Bold,Italic} distinct from {Italic,Bold}) — and composite styles round-trip losslessly through the clipboard.
- **Multi-syntax binding** (`UniTextBase.EnsureStyleFor`): one semantic modifier reacts to multiple input syntaxes (`<i>`, `*…*`, pasted `<em>`), merged into a `CompositeParseRule` on demand — no duplicate style entries.
- **`IFormatStyleSource` / `ReferencedStyle`**: the serialized seam a formatting command toggles — pick a modifier in the inspector and a toolbar button applies it in that style's own syntax, inert on fields that lack it.
- **`TriggerWordParseRule`**: built-in `@mention` / `#hashtag` auto-detection with a configurable trigger character, runs at negative priority so explicit markup wins.
- **Link rules carry a default style**: `MarkdownLinkParseRule` and `RawUrlParseRule` gained a `DefaultParameter` merged positionally behind the matched URL, so an auto-detected or `[text](url)` link applies a fixed visual preset (e.g. a Link;Color;Underline composite) configured on the rule.
- **Markdown markers follow CommonMark flanking rules** (`MarkdownWrapRule`): a marker opens only when left-flanking and closes only when right-flanking (CommonMark §6.2), and `_`-markers never toggle inside a word — `2 * 3` and `snake_case_name` stay literal. In 2.x every marker occurrence toggled.
- **Per-style enable toggle** (`Style.Enabled`, an eye button on each style row): disable a style non-destructively — skipped by the parser, rendering, and every style query while its configuration is preserved; re-enabling restores its authored precedence among overlapping styles.

#### Input Behavior system

- **Composable, serialized, user-extensible behavior system** (`InputBehavior`): drop-in policy units attached to a field as a `[SerializeReference]` type-picker list, authored like style modifiers — subclass the base, add serialized fields, override `OnEnable`/`OnDisable` to subscribe the editor's extension events. Teardown is fully reversible (an internal `Saved<T>` slot restores host state on disable).
- **Documented transaction seam**: the four hooks a behavior subscribes — `InputFilter` (`InputEdit`), `KeyResolver` (`KeyResolve`), `KeyboardResolver` (`KeyboardRequest`), `MediaReceived` (`MediaContent`). `InputEdit` carries the inserted text, target range, caret, and a `TextChangeReason`, with a sticky `Rejected` — so a whole-field mask can rewrite the entire transaction. Hooks run in order, each seeing the previous result, only on committed text (never IME composition, undo replay, or programmatic writes).
- **Runtime hot-swap**: `AddBehavior`, `RemoveBehavior`, `GetBehavior<T>()`, `GetBehaviors<T>(List<T>)` flip a field's policy live; shared presets attach via `AddBehaviorPreset`/`RemoveBehaviorPreset`.
- **`InputBehaviorPreset`**: a reusable `ScriptableObject` field archetype (chat composer, password field, form field). Each editor instantiates a runtime copy so per-instance state never leaks back to the asset; editing the preset asset raises `Changed` and rebuilds every live field using it in place.
- **Built-in behaviors** (15 concretes; `KeyboardBehavior` and `MediaInputBehavior` ship as ready-made bases for authoring keyboard-lifecycle and media-handling behaviors): `PasswordBehavior` (caret-aligned masking + secure entry + copy blocking + reveal toggle), `SingleLineBehavior`, `LengthLimitBehavior` (grapheme-counted, boundary-safe), `CaseTransformBehavior` (upper/lower/title), `InputMaskBehavior` (live `(###) ###-####`-style formatting, paste- and mid-field-edit aware, with `RawText`), `SubmitKeyBehavior` (Enter or Ctrl/Cmd+Enter), `TabKeyBehavior`, `NativeKeyboardBehavior`, `NativeFieldOverlayBehavior`, `KeyboardAvoidanceBehavior` (animated lift, canvas-scale and projection correct), `TextFormattingBehavior`, `AutoValidateBehavior`, `LinkOnPasteBehavior`, `StripFormatOnPasteBehavior`, `CaretContextBehavior`.
- **`TextFormattingBehavior`**: a field's whole formatting layer on one behavior — a configurable `Commands` list (default B/I/U via `FormatCommand`), a clear-formatting shortcut (Ctrl/Cmd+\ by default), `Toggle(string)`/`Toggle(int)` for toolbar buttons, and ownership of `MarkupVisibility` / `RichPaste` / paste policy / `TypingMarkup` / arrow-key escape / tag chrome while enabled.
- **`CaretContextBehavior` + `StyleStateHandler`**: wire a Bold button's highlight with zero code — an edge-triggered handler fires a `UnityEvent<bool>` only when "would typing now produce this style?" flips.

#### Input filters & validators

- **`InputFilterBase`**: a self-wiring behavior that rejects characters as typed by judging the whole post-edit span (surrogate/grapheme-safe, native-multi-char aware) and requests a preferred mobile keyboard. Built-ins: `IntegerFilter`, `DecimalFilter`, `AlphanumericFilter` (Unicode letters + ASCII digits), `EmailFilter`, `NameFilter` (letters + space/hyphen/apostrophe). Numeric filters expose `AllowNegative`, which both accepts a leading minus and switches the requested keyboard to one that has a minus key.
- **`InputValidatorBase` + `ValidationState`**: judge the whole value and publish an open-string status (`ValidationStatus.Invalid`/`Pending`; valid = empty status) + message, scheduled by `AutoValidateBehavior` (`OnValueChanged`/`OnUnfocus`/`OnSubmit`/`Always`) and wired straight into the field's error visuals. Async-capable (`Pending`).
- **`TextMeasure` / `TextLengthUnit`**: measure and boundary-safe-truncate text by `Graphemes` (default — a family emoji counts as one), `Utf16Units`, `Utf8Bytes`, or `Codepoints`; `LengthLimit` published to counters — never the wrong number `string.Length` gives.

#### Field decorators

- **Decorator system** (`FieldDecorator`): composable field chrome reacting to a pushed `FieldState` snapshot (`IsEmpty`, `IsFocused`, `IsComposing`, `LengthLimit`, `Validation`, `Box`) with attach/dispatch/tick/detach lifecycle; an idle decorator holds no per-frame subscription. Author your own via the type picker.
- **Built-in decorators**: `PlaceholderDecorator`, `FloatingLabelDecorator` (Material-style animated label with easing curve/duration, snapping instantly under Reduce Motion), `SupportingTextDecorator` (helper text that swaps to the validation error message/color), `CharacterCounterDecorator` (grapheme-aware `count`/`count/limit`, recolors at the limit, cached by document version).

#### Context menu

- **`UniTextContextMenu`** (`ITextContextMenu`): a data-driven, bring-your-own-UI context menu — you build and style the panel/buttons in the scene, the component wires controls to commands, hides inapplicable items, positions and clamps the panel on-screen with an edge-aware pivot, and dismisses on outside click via a generated full-screen blocker. Visibility runs through a `CanvasGroup` (never deactivating the GameObject) so a `FocusGuard` on the panel keeps the editor focused.
- **Menu items** (`ContextMenuItem`, `BuiltInContextMenuItems`): `Cut`/`Copy`/`Paste`/`SelectAll` plus custom `ActionContextMenuItem` (a `Button` → `UnityEvent`) and `ToggleContextMenuItem`, each with per-item applicability rules (`onlyWithSelection` on the custom items). Standard commands route through the last-shown presenter so one menu instance safely serves every field; custom items fire their own `UnityEvent`. Raised on right-click or long-press via `UniTextSelectable.RequestContextMenu`, or subscribe `ContextMenuRequested`/`ContextMenuDismissRequested` for a fully custom or OS-native menu (capability-gated — no copy from a password field).

#### Touch & mobile interaction

- **Gesture recognition** (`TouchGestureRecognizer`, a public state machine now in LightSide.Core): platform-standard single/double/triple tap, long-press, and drag — disambiguating drag-to-select from swipe-to-scroll by tap count, with no single-tap flicker under a double-tap — and time-scale independent. Thresholds are serialized on `UniTextSettings`, dp-based and DPI-aware: long-press 0.5 s, drag slop 10 dp, multi-tap window 0.3 s / slop 100 dp, desktop multi-click 0.5 s / 8 dp (matching Android `DOUBLE_TAP_*`, Flutter `kDoubleTap*`, Windows `GetDoubleClickTime`).
- **Magnifier loupe** (`IMagnifier`, shipped `DefaultMagnifier`): an iOS-style magnification bubble during long-press caret placement and handle dragging, rendered through a secondary camera into a render texture (Built-in RP via `Camera.Render`, URP/HDRP via `SubmitRenderRequest` on 2023.1+), with a documented graceful hide where capture isn't possible. Replaceable via the interface.
- **Selection handles** (`ISelectionHandles` + `IInsertionHandle`, shipped `DefaultSelectionHandles`): draggable teardrop start/end handles that extend the selection cluster-by-cluster — never collapsing, swapping roles past the fixed endpoint, showing the loupe during their drag — plus a caret knob that drags the collapsed caret and taps to reopen the menu, all rendered in a canvas-level overlay so the field mask never clips them. Replaceable via the interfaces.
- **Replaceable touch UI**: `SelectionHandles`, `Magnifier`, and `ContextMenu` are component slots on `UniTextSelectable` — assign the shipped defaults, your own custom-rendered components, or OS-native ones without subclassing; `UniTextEditable` only drives what's assigned.
- **`UniTextPasteControl`**: on iOS 16+ overlays a native `UIPasteControl` to bypass the system paste-permission prompt and capture plain/HTML/lossless-UniText in one tap; falls back to a `Button → Paste()` elsewhere, raising `Pasted` either way; display and corner styles (`PasteControlDisplayMode`/`PasteControlCornerStyle`) map to Apple's UIPasteControl configuration.

#### Pointer events

- **`TextPointerEvent`**: a consumable, reused (zero-alloc) pointer event on `UniTextBase` carrying the hit result, a `PointerTrigger` (PrimaryClick/SecondaryClick/LongPress/Hover), a `PointerKind` (Mouse/Touch/**Pen**, resolved authoritatively from the new Input System), screen position, camera, and live `[Flags] PointerModifiers`. Setting `Consumed` claims the click so a parent ScrollRect never sees it.
- **New events on `UniTextBase`**: `ContextRequested` (right-click or touch/pen long-press, mirrors HTML `contextmenu`/WinUI/SwiftUI), `TextLongPressProgress` (0→1 for loupe/ring feedback, mouse holds excluded), `PointerPressed`/`Released`, `PointerEntered`/`Exited` (topmost-raycast model — an occluding child correctly suppresses the hover). No per-frame cost when unsubscribed; Canvas and world-space text raise the same events.
- **Drag-to-select events** on `UniTextSelectable` (`SelectionDragStarted`/`SelectionDragUpdated`/`SelectionDragEnded`, anchored at the original press position) — kept off `UniTextBase` so a plain label never captures drags from an enclosing ScrollRect.

#### Highlight layers, find-in-page & interactive ranges

- **`UniTextHighlights`**: named highlight layers over any text component — the CSS Custom Highlight model (selection, find-in-page, hover feedback, custom range marks). One-liner entry: `UniTextHighlights.GetOrAdd(text).GetOrCreate("find-results")`; layers can also be authored in the inspector. All layers of one render side draw as a single mesh on both Canvas and world-space text; an idle component costs nothing per frame.
- **`HighlightLayer`**: codepoint ranges (`Add`/`SetRange`/`SetRanges`/`Clear`) painted as a merged union (overlaps never double-blend). Integer `Priority` with documented anchors (`HighlightPriorities`: `Interactive` −10, `Custom` 0, `Selection` 100, `FindCurrent` 200, `Diagnostics` 300), `Order` (behind/above text), `Visible`, animation-friendly `Opacity` (re-tints in place, no geometry rebuild), and **edit-sticky ranges** — `Stickiness` (`GrowsAtEdges` (Monaco), `Shrinks`, `RemoveOnOverlap`) maps ranges through every edit of a sibling `UniTextEditable`, tracked in document space and remapped under hidden markup (public `MapThroughEdit` lets an external document backend — a CRDT, a network host — drive the same mapping).
- **`HighlightStyle`**: the layer's presentation — `Paint` (an inline colour or a solid/gradient swatch from the paint catalog), `Height` (`LineBox` / `Content` / `LineAdvance`), padding, corner radius, merge threshold, and `BoxBreak` (`Slice`/`Clone`/`Block` — Block collapses a range to one rounded rect for the code-block look).
- **Handle-based render backend** (`TextHighlightRenderer`, `HighlightGroup`, `HighlightRect`): individually mutable rects with per-rect color, rounded corners, gradient paint, and a free `CustomData` (uv1) channel for authoring custom highlight shaders — yet a side's layers collapse into one draw call. Steady-state repaints mutate live rects in place, and a stale handle throws `ObjectDisposedException` instead of silently aliasing another rect.
- **Find & read API on every text component**: `UniTextBase.FindAll(query, comparison, results)` — allocation-free for queries up to 64 chars, codepoint-correct, case-sensitive **or** -insensitive per the passed `StringComparison` (bundled Unicode simple case mappings; culture values compare ordinally), returning ranges that feed highlight layers and `GetRangeBounds` directly — plus the `CodepointCount`/`RenderedCodepoints` read surface.
- **Interactive ranges are now configuration, not a subclass**: `InteractiveModifier` is a concrete, inspector-pickable modifier (`new InteractiveModifier { RangeType = "mention" }`) running a per-range `Normal`/`Hovered`/`Pressed` state machine, consuming pointer events, and exposing six events (`RangeStateChanged`, `RangeClicked`, `RangeContextRequested`, `RangeEntered`, `RangeExited`, `RangeLongPressProgress`) plus `GetRangeState` and a protected `IsRangeEnabled` override to disable ranges dynamically (visited links, permission gates) — the web LVHA / UIKit `UITextItem` / Android `ClickableSpan` model with no registry.
- **Range stylers** (`RangeStyler`): the presentation seam assigned to `InteractiveModifier.Styler` — state transitions, activation flashes, and long-press progress arrive as hooks, typically drawn through highlight layers; idle stylers hold no per-frame subscription. `StateHighlightStyler` is the shipped default: animated hover tint, instant pressed fill, activation flash, optional long-press buildup — all `Opacity` fades, no geometry rebuild — plus a `NormalEnabled` persistent chip layer under the state layers (the Material state-layer model: a mention chip is one `InteractiveModifier` with this on, rebuilt at parse rate, never per frame).
- **Built-in interactive modifiers**: the new `SpoilerModifier` (Discord/Telegram tap-to-reveal covers — `RevealChanged`, `SetRevealed`/`ConcealAll`/`IsRevealed`, per-range reveal reconciled across re-parses, animated covers via the shipped `SpoilerCoverStyler`) and `LinkModifier` rebuilt on the state machine: base colour/underline styling moved out to composition, an iOS-style grey pressed flash via its default `StateHighlightStyler` (its 2.x `LinkClicked`/`LinkEntered`/`LinkExited` events and `AutoOpenUrl` — on by default — carry over) — both worked examples for authoring your own stateful interactive text.
- **Selection style**: `UniTextSelectable.SelectionStyle` (a `HighlightStyle`) drives the reserved `"selection"` layer at `HighlightPriorities.Selection` — recolor, round, or gradient-fill the selection bar from one serialized field.

#### Text effects & paint

- **One paint system for every layer** (`UniTextPaints` catalog, `PaintSwatch`, `IPaintProvider`): a named swatch is a solid color, a `Gradient`, **or** a `Texture2D`, referenced straight from markup (`<fill=ember>`, `<stroke=gold>`) or configured on a modifier. The same paint drives fills, strokes, shadows, glows, and underlines.
- **Texture-filled text**: any layer can be painted with an image — chrome, marble, foil, animated sheets — with `PaintFit` (`Stretch`/`Contain`/`Cover`/`Tile`).
- **Gradients rebuilt per-pixel**: 2.x evaluated a gradient at the four corners of each glyph quad; gradients now sample a shared ramp atlas per fragment, so radial and conic shapes stay correct inside every glyph. New `PaintMapping` frames (`Block` across the text, `Line` per line, `Glyph` per glyph, `Range` across the styled span) plus `scale`/`offset` join the 2.x `Linear`/`Radial`/`Angular` shapes and `angle`.
- **`FillModifier`**: fills the glyph interior with a paint; the first fill claims the suppressed base quad (no extra geometry), additional fills stack above it.
- **`StrokeModifier`** (replaces the old outline blob): a true rim band with continuous `Align` (−1 inside → 0 centered → +1 outside; legacy `inside`/`center`/`outside` keywords still parse), `Width`, `Softness`, and a `CornerStyle` (artifact-free `Round`, or `Sharp` mitered up to `MiterLimit` — shared by the layer base, so shadows and glows get the same corner treatment) — from an MTSDF two-distance read, works with the fill off, and takes any paint.
- **`GlowModifier`** (paintable soft halo, `Radius`), **`ShadowModifier`** rebuilt with independent `Offset`/`Blur`/`Spread` and any paint (a real blurred/spread/gradient shadow, not a hard offset copy), and **`InnerShadowModifier`** (inset shadow via a second SDF tap — embossed/letterpress depth, crisp at any size).
- **Paintable decorations** (`BaseLineModifier`): underline & strikethrough gained per-range paint (color/gradient/texture, defaulting to the text fill like CSS `text-decoration-color: currentColor`), and the 2.x line-style set (`solid`/`double`/`dotted`/`dashed`/`wavy`, thickness, offset, skip-ink) gained serialized defaults and public properties (`Style`, `Thickness`, `Offset`, `SkipInk`) — CSS Text Decoration Level 4.
- **`GlyphResolutionModifier`**: raises the atlas tile resolution of the glyphs in its range (`ResolutionBoost` 0–2) for crisp large headings without bumping the whole font's atlas.
- **`UnitValue` / `UnitVector2`**: the px/em/%/delta parameter grammar became a serialized value+unit type — an inspector default now carries its unit, and every effect geometry parameter (stroke width, glow radius, shadow offset/blur/spread) takes px or em: pin to px for constant on-screen size, or em to scale with the glyph.
- **Serialized defaults + public property + per-range override on every modifier**: a bare tag (`<b>`, `<glow>`, `<color>`) takes the inspector-configured default; an inline value overrides it — and each parameter is now a real C# property (`ColorModifier.Color`, `BoldModifier.Weight`/`Mode`, `SizeModifier.Size`).
- **`PaintLayerModifier`** extension point: fill/stroke/shadow/glow/inner-shadow all derive from one layer that resolves the paint, precomputes gradient/texture mapping per range, and stamps coverage quads — a custom effect gets swatch resolution and the full override chain for free, and a layer's overlapping ranges (nested tags) resolve innermost-wins once per codepoint (O(1) per glyph, not per glyph×range).

#### Kinetic text animation

- **Phase-driven model** (`IPhaseDriven`): a modifier's visual state is a pure function of its `Phase` and never advances itself — drive it from the drop-in `UniTextPhaseDriver` (free-running `Speed`, optional `UnscaledTime`, scrubbable `Phase`), a tween, Timeline, an `Animator` (`PhaseAnimationHandler`), or code. The same phase always renders the same frame, so scrubbing, rewinding, and multi-instance sync all work.
- **Eleven built-in glyph animations** (up from one wobble in 2.x), each configurable per range and most staggered along the text by a per-cluster `Spread`: `WaveModifier`, `BounceModifier`, `PulseModifier`, `ShakeModifier`, `WobbleModifier`, `SpinModifier`, `PendulumModifier`, `FloatModifier`, plus three effects that normally need custom shaders — `GlitchModifier` (RGB-split bursts), `ScrambleModifier` (hacker-style decode, grapheme-correct, no reflow), and `RollingModifier` (odometer / split-flap wheels — drive `Roll` toward 0 to settle).
- **Per-glyph reveal animation** (`RevealModifier.GlyphRevealing`): the 2.8 typewriter modifier gains a mesh-build hook — each rendered glyph of a revealing range arrives as a `RevealGlyphInfo` (`ordinal`, `count`, `front`, fractional `Progress`), so fade/scale/drop-in appearance is a few lines of quad mutation; while subscribed, the frontier cluster renders at fractional progress instead of popping in whole.
- **Author your own** (`GlyphParamModifier<TParams>` + the static `GlyphQuad` helpers): a phase-driven per-character effect in ~50 lines, riding the same driver/scrub/Timeline machinery as the built-ins; `OnGlyph` runs allocation-free during mesh generation, worker-thread-safe.

#### Custom shader effects

- **Write once, run everywhere**: author a single `half4 UniTextEffect(UniTextFrag i)` holding only visual logic and the template shells wire it into every pass — Canvas + world-space, Built-in + **URP**, SDF/MSDF/emoji. The `UniTextFrag` struct exposes `sdfAlpha`/`signedDist`/`glyphUV`/`tileId`/`tileHash`/`userA`/`userB`/`positionWS`, plus an optional vertex hook and a world shadow-caster program. **Custom effects on world text — Built-in or URP — did not exist in 2.x.**
- **New authoring templates**: every bundled effect (Rainbow, Dissolve, Hologram) now ships as a Canvas shell **and** a world/URP shell over a reusable logic include, plus a blank `UniText_Effect-Example.hlsl` starting point; shared building blocks live in `UniText_EffectLib.hlsl`.
- **One-click scaffolding** (`Assets > Create > UniText > Custom Effect`, renamed from *Custom Material Shader*): one click writes the full trio into the selected folder — the pipeline-neutral effect include plus pre-wired Canvas and World shader shells, include paths resolved and a unique shader name stamped in — so a new effect compiles for every pass the moment it lands.

#### Performance — glyph rasterization, rebuilt

- **Cold-glyph rasterization now costs the main thread almost nothing.** The async GPU rasterizer (shipped in 2.10.0) is rebuilt end-to-end — Burst outline extraction and contour union, CPU-precomputed 2D binning, per-font batching — leaving the main thread only a short dispatch while the GPU fills the atlas asynchronously.
  - *Measured (Galaxy S21 FE 5G, Adreno 660, Vulkan, IL2CPP release build), cold-atlas fill of a 3,029-glyph multilingual NotoSans corpus (TextMeshPro/UI Toolkit filled 3,091/3,092):* **UniText ≈ 33 ms of main-thread dispatch** vs **TextMeshPro 8,635 ms** and **UI Toolkit 6,446 ms** of synchronous main-thread work — a **263× / 196× reduction in main-thread cost** (per-glyph: 10.8 µs vs 2,793.6 / 2,084.8 µs). The atlas finishes filling ≈ 2.1 s later without ever blocking a frame. A decorative 609-glyph RubikStorm fill: 96 ms vs 2,025 ms (TMP) / 826 ms (UI Toolkit).
- **Binned GPU rasterizer** (`UniTextGlyphRaster.compute` + `GpuRasterPrep`): CPU-precomputed 2D binning — 16 row-bands and a 16×16 control-hull cell grid — so each GPU texel evaluates only the local candidate segments instead of the glyph's whole outline. Heavy fonts (CJK, decorative, complex Arabic) rasterize far faster than v2's single 1D scan.
- **Burst contour resolution** (`ContourUnionBurst`): a new zero-allocation `[BurstCompile]` stage on raw pointers resolves self-intersecting and stroked outlines up front; cleanly-resolved glyphs are tagged so the SDF/MSDF sampler skips the per-pixel silhouette heuristic 2.x ran for every glyph.
- **Parallel CPU rasterization** (`GlyphBatchRasterizer` + per-font worker fan-out): a frame's glyph requests batch per font; fonts render concurrently, and within one font outline extraction and Burst SDF generation spread across every core — *≈ 9.7× over single-threaded on a 12-core desktop (3,029 NotoSans glyphs: 220 ms vs 2,138 ms; Editor measurement of the CPU-raster path)*. Platforms without compute support (WebGL) take this path automatically.
- **GPU raster robustness**: `GraphicsFence`-tracked completion polled at frame granularity, a submission-cost budget (250 ms mobile / 500 ms desktop), and a `DeviceResetSentinel` that re-arms the atlas after a graphics-device loss or scene-reload — text survives alt-tab/driver resets without corruption.
- **Atlas growth tapers**: past 16 slices the glyph atlas grows +25 % per step instead of doubling, and pre-allocation discounts reusable page area — large multilingual atlases stop overshooting their memory footprint, and a double-allocation crash risk on the CPU-raster path with very large glyph sets is gone.

#### Performance — Unicode analysis, shaping & incremental rebuilds

- **Burst-compiled analysis with a byte-identical fallback**: the four Unicode analysis passes — UAX #9 BiDi, #24 script, #29 grapheme, #14 line-break — each run as a single `[BurstCompile]`, auto-vectorized kernel exposed as a `FunctionPointer`, with the exact same source available as managed IL where Burst is unavailable. Output is bit-for-bit identical either way (integer-only logic) — speed with zero behavioral risk. (These are Burst auto-vectorized kernels, not hand-written SIMD intrinsics.)
- **Per-paragraph, parallel, cached analysis**: because the kernels are `FunctionPointer`s (not Unity `IJob`s) they run on the `WorkerPool` threads; analysis is paragraph-scoped, and a content-addressed `ParagraphAnalysisCache` (`XxHash64`) reuses resolved levels/scripts/line-break and grapheme boundaries so editing one line never re-analyzes the document. In 2.x each document was analyzed whole, on one thread, uncached.
- **Unchanged paragraphs are never re-shaped** (`ParagraphShapeCache`): each paragraph is fingerprinted over its codepoints plus everything that feeds shaping (font size, per-range font/language data, hidden markup), and a hit replays the stored pristine HarfBuzz runs and glyphs — the Blink shape-cache model. Typing re-shapes only the edited paragraph. *Android release build, 100 multilingual objects: an incremental edit rebuilds in 43 ms vs 69 ms for the all-new cold path.*
- **Cross-document word-shape cache** (`WordShapeCache`): shaped words are reused across every text on the engine (`XxHash64`-keyed by codepoints, font, variation axes, script, direction, language) — changing labels, counters, and chat logs that share vocabulary splice stored glyphs with no HarfBuzz call at all. Only words HarfBuzz marks safe-to-break on both boundaries are cached — the exact per-font condition under which the isolated word shapes identically to its in-context form — and entries hold font-unit glyphs, so they survive font-size changes; a miss shapes the whole run in context, byte-identical to the uncached path.
- **One document parallelizes too**: the parallel pipeline (on by default since 2.x) fanned out per component; analysis, shaping, and layout are now paragraph-scoped, so one large text also spreads its bidi/script/break/shaping work across every core — per-paragraph shape jobs from every dirty component flatten into a single worker-pool run. *Release build (Galaxy S21 FE 5G, Vulkan, IL2CPP), cold rebuild of 100 multilingual objects: UniText 69 ms vs UI Toolkit 167 ms — both HarfBuzz-shaped, like-for-like — and TextMeshPro 455 ms (TMP does not shape Arabic/bidi; its output is not equivalent). Editor, 12-core Ryzen 9 5900X: 92 ms parallel vs 351 ms single-threaded (≈ 3.8×).*
- **Viewport-windowed rendering** (`UniTextBase.VisibleWindow`): a local-space window bounds mesh emission — paragraphs fully outside it produce no quads, while layout, selection, caret, and hit-testing stay whole-document. Canvas text feeds it automatically from the enclosing `RectMask2D` clip rect, so a long editable document or ScrollRect only meshes the visible band (a half-window hysteresis margin makes small scrolls free); set it explicitly for custom virtualized scrollers (chat, logs) — `null` renders everything.
- **Pure-LTR BiDi fast path**: all-LTR text (Latin, Cyrillic, CJK — the common case) short-circuits the full X1–L1 bidirectional cascade after one classification scan, a fast path 2.x had no equivalent of.
- **Native-memory Unicode tables**: the whole Unicode character database moved into `NativeArray<T>` (Persistent) — off the GC heap, shared by both the Burst and fallback paths.
- **Trigger-scan markup parsing**: every `ParseRule` declares the characters that can start its match, and the parser bakes their union into an ASCII jump table — the match walk skips runs of plain text without probing any rule, degrading to the 2.x linear walk for exotic trigger sets.
- **Single source per algorithm**: each UAX rule now exists in exactly one place, collapsing the engine classes to thin wrappers (BidiEngine 2,287 → 402 lines, LineBreakAlgorithm 680 → 122) — less surface to break, zero drift between fast path and fallback.

#### Rendering & shaders

- **Unified coverage × paint SDF pipeline**: every glyph quad picks a `CoverageMode` (Fill/Stroke/Shadow/InnerShadow) and a `PaintKind` (Solid/Gradient/Texture); plain glyphs leave both at 0 and pay nothing, while styled text composes Photoshop-style layer effects on the GPU with one draw call per material. Backed by `UniText_Coverage.hlsl` + `UniText_Paint.hlsl` and shared decode includes (`UniText_AtlasDecode.hlsl`, `UniText_SdfSample.hlsl`) that de-duplicate four hand-copied decode paths.
- **Lit world text is a checkbox** (`UniTextWorld.Lit`): one per-component toggle renders the text with scene lighting — the lit SDF/emoji materials are selected automatically, and adjacent instances can independently be lit or unlit. In 2.x this required hand-assigning the shipped lit materials through a custom-material style. **`CastShadows`** (independent of `Lit`, off by default) opts the batched renderer into shadow casting, resolved per sorting context — 2.x lit text always cast, with no supported way to turn it off.
- **Unlit fast path on the Lit shaders**: `_LightInfluence` 0 branches around the whole lighting computation (`UNITY_BRANCH` on a uniform — no extra keyword or variant), instead of 2.x's compute-then-lerp — a world-text material dialed to unlit pays no per-fragment lighting cost.
- **World shaders ship via the settings asset**: `UniTextSettings` now retains the world-space `UniText/Lit/SDF` / `UniText/Lit/Emoji` and Canvas `UniText/UI/Highlight` shaders (six required-shader slots, up from three), so world text and highlight materials resolve in builds without touching Always Included Shaders; the new **Include Lit Shaders** toggle (Project Settings → UniText → Rendering, default on) drops the Lit pair from builds that use no lit text.
- **Shared gradient-ramp atlas** (`GradientRampAtlas`): one 256-wide row per unique gradient, deduped by content and ref-counted, uploaded row-granularly — crisp gradients that stay allocation-free and animate at one texture-row upload per frame, shared across all text and highlights on the device.
- **Rounded, gradient highlight shaders**: the world `UniText/Highlight` gained a rounded-rect SDF with gradient-ramp paint, and a new Canvas `UniText/UI/Highlight` provides the uGUI counterpart — rounded, gradient-filled selection/find highlights on both backends.

#### Animator integration

- **`UniTextAnimationBridge`**: a sibling component that makes Unity `Animator`-driven changes update the text — add it next to a `UniText`/`UniTextWorld` and add the handlers for the fields and modifiers you animate; text without the component does no per-frame diff work.
- **Component field handlers** — `UniTextFieldsAnimationHandler` (font size, colour, word wrap, auto-size with min/max, alignment) and `UniTextWorldFieldsAnimationHandler` (the same, plus sorting order and layer).
- **Modifier handlers** — `ColorAnimationHandler`, `SizeAnimationHandler`, `LetterSpacingAnimationHandler`, `GlowAnimationHandler`, `ShadowAnimationHandler`, `StrokeAnimationHandler`, and `PhaseAnimationHandler` (keyframe a phase-driven glyph animation — binds the host's first `IPhaseDriven` modifier), each with the correct per-parameter invalidation.
- **`AnimationHandler` / `ModifierAnimationHandler`**: `[SerializeReference]` extension points — subclass to animate a custom component's fields or a custom modifier's `[Parameter]` fields from an `Animator`.

#### Editor tooling

- **Inline Scene-view text editing**: double-click any `UniTextBase` in the Scene, press F2, run `Tools/UniText/Edit Text in Scene`, or click the inspector's "✎ Edit in Scene View" button to open a live editing session — caret, selection, drag-select, word/paragraph click, keyboard nav, clipboard, undo/redo, and OS IME (candidate window anchored to the Scene-view caret) — all driven by the runtime engine, with a floating formatting overlay (B/I/U, colour, markup visibility) and a right-click menu. On exit the edited markup is written back through `Undo.RecordObject`, and every helper component the session spawned is removed, leaving the GameObject graph exactly as it was.
- **Automatic v2.x → 3.0 asset migrations**: nine concrete `IMigration` classes upgrade serialized scenes/prefabs/assets on first load (byte-preserving YAML splices, auto-run on version change, no prompts, CI excluded) — `Outline→Stroke`, `Gradient→Fill`, `Wobble→Wave`, the gradient-provider trio → `InlinePaintProvider`/`AssetPaintProvider`/`GlobalSettingsPaintProvider`, a bold-weight sentinel fix, `DefaultTextHighlighter` → `StateHighlightStyler`, and legacy `UniTextGradients` catalogs → `UniTextPaints`. A breaking major upgrade that costs the user nothing.
- **Custom inspectors** for `UniTextEditable`, `UniTextSelectable`, and `InputBehaviorPreset`, backed by the shared `[SerializeReference]` type picker with grouped, icon-labelled, description-carrying "Add" menus for behaviors, decorators, filters/validators, and context-menu items. `InputBehaviorPresetEditor` audits preset assets for behaviors pointing at scene objects.
- **New property drawers**: `PaintSwatchDrawer`, `UnitValueDrawer` (value + unit dropdown), `InheritableFieldDrawer` (NaN inherit sentinel), `ChromeRuleDrawer`, `FormatCommandDrawer`, and `ModifierBodyDrawer` (shows a modifier's `[Parameter]` fields as an opt-in override list — a field appears only when it differs from the default, and edits route through the live C# property for scoped invalidation).
- **Clipboard Inspector window** (`Tools/UniText/Clipboard Inspector`): shows every format a copy wrote — plain text, UniText source, HTML, Markdown, URL, custom MIME, images (with a decoded preview) and files — with counts, decoded payload, and whitespace visualization.

#### Focus & accessibility

- **`FocusGuard`** (in LightSide.Core): marks a UI hierarchy (formatting toolbar, context panel) focus-preserving, so pressing its controls neither defocuses the editor nor drops the selection — the toolbar contract of a word processor. One drop-in component; `FocusGuard.PointerIsOverGuarded()` recovers the press target.
- **`UniTextFocusable`**: a hidden, runtime-managed `Selectable` that makes an editable surface a Tab stop (`Navigation.Mode.Automatic`) while a selection-only surface stays pointer-focusable but Tab-excluded (the web model) — reconciled automatically, never added by hand.
- **Accessibility preferences**: `Accessibility.PrefersReducedMotion` (LightSide.Core, with a change event; honored by `FloatingLabelDecorator` and any driver that consults it — maps to iOS/macOS Reduce Motion, Android Remove Animations, web `prefers-reduced-motion`) and `InputCaretRenderer.PrefersNonBlinkingCaret` (suppresses caret blink — iOS "Prefer Non-Blinking Cursor"). The app sets the flags from platform prefs.

#### Samples, scaffolding & CI

- **One-click scene scaffolding**: `GameObject → UI (Canvas) → UniText → Selectable Text / Editable Text` instantiate the project's own prefab from `UniTextSettings.{SelectableText,EditableText}Prefab` (the Unity way — a designer's configured prefab, not a code-built hierarchy), provisioning a Canvas + EventSystem (wiring `InputSystemUIInputModule` under the new Input System) and warning when a slot is empty.
- **Shipped `Defaults/` assets**: `Selectable Text`, `Editable Text`, `Input Field` prefabs plus the three selection-handle sprites for the touch UI.
- **New `EditableText` sample**: a complete working editor scene — the selectable → editable ladder, an `InputBehaviorPreset`, a password field with a reveal toggle, a caret-context formatting toolbar, and chat-style media paste turning pasted images into attachment cards.
- **Expanded Basic Usage sample**: auto-detected `@mention`/`#hashtag` chips, tap-to-reveal spoilers, a live-swappable custom `RangeStyler`, Find-in-Page over `FindAll` painted onto highlight layers, and a `LanguageFonts/` set covering 17 non-Latin scripts. The `Modifiers` sample is renamed `Styles`; `FontExamples` gains a variable-font `EdgeCasesFonts/` set (Comfortaa and Outfit variable-weight fonts).
- **CI**: a GitHub Actions `urp-compile-check` matrix imports the package into URP 12.1 / 14.0 / 17.0 projects and fails on any shader import error, and three workflows build the Windows/macOS/Android native input plugins reproducibly from committed source; LightSide.Core adds its own workflows for the GPU-upload and profiler-sampler plugins — shaders proven across three URP majors, and native binaries transparent, not opaque blobs.
- **Optional Input System**: documented as an optional dependency (`UNITEXT_INPUTSYSTEM` via `versionDefines`) — works with the old Input Manager, the new Input System, or both. Native text input works regardless either way.

### Changed

- **UniText now depends on `media.lightside.core` 1.0.0** and is layered on top of the shared foundation extracted into that package (see *Two packages*). Source-compatible apart from two utility renames (`UniTextArrayPool<T>` → `ArrayPool<T>`, `RangeEx.WholeText`/`IsWholeText` → `RangeEx.All`/`IsAll`); the moved types kept `namespace LightSide`.
- **`IParseRule` interface replaced by the `ParseRule` abstract class** (breaking, no alias). Custom parse rules now subclass `ParseRule` and override `TryMatch`; the base carries the syntax-introspection contract (`Identity`, `SourceToken`, `MarkupTriggers`/`ScanTriggers`, `CanWrap`, `Apply`) that lets the editor apply/toggle/round-trip a style in its own markup.
- **StyleCore gained editor-grade introspection**: `ParseRule` now describes and re-emits its own syntax (all public — see *Rich-text formatting*), and `UniTextBase` gained `EnsureStyleFor` and `GetModifier(Type)` — the surface that powers the formatting toolbar. Style addressing is by modifier signature (a concrete exemplar, resolved internally); there is no name-based style lookup.
- **Pipeline events are per-component now**: new `UniTextBase` instance events — `FrameUpdated` (once per frame while the component updates; the tick range stylers and modifier animations subscribe to) and `LayoutCommitted` (fires only when *this* component reprocessed and its glyph geometry is final — the moment to rebuild coordinate maps, caret geometry, or overlay positioning) — replace the removed public static `UniTextBase.BeforeProcess`/`MeshApplied`/`AfterProcess` (breaking, no alias). A subscriber now pays only for the component it watches, never for every text update in the scene.
- **`InteractiveModifier` went from an abstract, subclass-required handler to a concrete, inspector-pickable modifier** with a per-range state machine, six range events, and a `RangeStyler` seam (see *Highlight layers, find-in-page & interactive ranges*). Range pointer events moved off `UniTextBase` onto the modifier.
- **Pointer/click events on `UniTextBase` use the `TextPointerEvent` type.** `TextClicked` now carries a consumable `TextPointerEvent` (device kind, modifiers, trigger, consume flag) instead of a bare `TextHitResult`; the previous base range pointer events (`RangeClicked`/`RangeEntered`/`RangeExited`, `IsHoveringRange`/`CurrentHoverRange`) were removed in favor of `InteractiveModifier`'s events.
- **`UniTextBase.HitTest`/`HitTestScreen` renamed to `HitTestRange`** (range/bounding-box "which entity is here" semantics), distinct from the new caret hit-testing (`HitTestCaret`).
- **Style modifiers rebuilt around the paint system**: the whole visual half of StyleCore now runs on one paint abstraction (fills/strokes/shadows/glows/decorations take any solid/gradient/texture paint), every parameter gained a serialized default + public property + per-range override, and files reorganized into `TextStyle/Layout/Appearance/Utility/Decoration/Effects/Media` subfolders (see *Text effects & paint*).
- **Highlight rendering** moved from a flat single-color rect list to named, prioritized highlight layers: the `TextHighlighter`/`DefaultTextHighlighter` family and the serialized highlighter field on `UniTextBase` were removed — selection visuals via `UniTextSelectable.SelectionStyle`, interactive-range feedback via `RangeStyler`, everything else through `UniTextHighlights`.
- **The world batcher groups by sorting context, not material**: a batch is keyed by (sorting layer, order, `SortingGroup`, GameObject layer, scene) with materials demoted to sub-meshes, so one component's cross-material layers — emoji beside SDF glyphs, texture-paint fills, per-style custom materials — draw in exact Styles order, the way Canvas sibling order does (in 2.x each material was its own batch mesh, with unspecified relative order). Inside a context, entries pack into size-bounded shards (target raised 8,192 → 16,384 vertices, tunable via `UniTextSettings.WorldBatcherShardTargetVertexCount`) so a structural change re-bakes only the owning shard; index buffers escalate UInt16 → UInt32 automatically past 65,535 vertices.
- **`UniTextDirtyFlags` renamed to `UniTextDirty`** (breaking, no alias) and re-cut to name pipeline stages instead of properties: `Color` → `Mesh`, `Alignment` → `Positions`, `FontSize` folded into the existing `Layout`; `Text`/`Font`/`Direction`/`Material` keep their names; `Sorting` and the `LayoutRebuild` composite were removed (sorting propagates through events, not a rebuild flag). Recompile against the new enum; do not persist raw values.
- **Text animation is now phase-driven, not self-animating**: v2's `WobbleAnimationModifier` (read `Time.time`, rebuilt every frame) is renamed `WaveModifier` (auto-migrated, `speed` → `frequency`) and re-cut as a pure function of `Phase` — controllable and scrubbable instead of a fire-and-forget frame-eater (see *Kinetic text animation*).
- **Animator support is opt-in via `UniTextAnimationBridge`**: animating a component's own fields now requires the bridge with the matching field handler, instead of every Animator-driven component diffing its fields automatically.
- **The GPU glyph-upload backend was rebuilt (ABI v3) and moved to LightSide.Core** as the public `GpuUpload` API, with device-loss and multi-backend (D3D/Metal/Vulkan/WebGL2) handling.
- **3D text shadows match the visual**: the shadow casters now cast each visible layer's coverage, so an outlined or glowing glyph throws a shadow shaped like what you see, not the bare core glyph.

### Removed

- **Legacy gradient & outline modifiers**: `GradientModifier`, `OutlineModifier`, `IGradientProvider`/`IHasGradientProvider`, and the `Inline`/`Asset`/`GlobalSettings` gradient-provider family — superseded by gradient paints on any layer via `IPaintProvider` and by `StrokeModifier` — along with the `UniTextGradients` catalog asset and `UniTextSettings.Gradients` (superseded by `UniTextPaints` and `UniTextSettings.Paints`; a migration converts each legacy catalog into a `-Paints` sibling asset). The `<gradient>`/`<outline>` tags survive as deprecated parse rules so existing markup still renders; the removed types have no alias (asset data auto-migrates).
- **`TextHighlighter` / `DefaultTextHighlighter`** and the serialized `Highlighter` field on `UniTextBase` — replaced by highlight layers, `SelectionStyle`, and `RangeStyler`.
- **Interactive-range dispatch types** `InteractiveRangeRegistry`, `IInteractiveRangeProvider`, `IInteractiveRangeHandler`, and `InteractiveRange.priority` — `InteractiveModifier` now self-subscribes to the pointer surface; overlapping ranges from different modifiers all dispatch, in registration order.
- **Static pipeline events** `UniTextBase.BeforeProcess`/`MeshApplied`/`AfterProcess` (breaking, no alias) — replaced by the per-component `FrameUpdated`/`LayoutCommitted` (see *Changed*).
- **`AnimationHandlerBase<T>`**: extending the Animator diff is now done by adding a field/modifier handler to a `UniTextAnimationBridge`.

## [2.12.9] - 2026-07-02

### Fixed

- **Some fonts rendered as scrambled outlines**: glyphs from fonts FreeType classifies as "tricky" (certain hinted CJK fonts, e.g. Nowar C²) came out as a tangle of lines instead of the correct shape.

## [2.12.8] - 2026-06-21

### Added

- **`RangeCollectingModifier`**: base class for custom modifiers that collect their tagged ranges and act on them once the text is shaped — override `ApplyRange` to edit line-break opportunities or add segment breaks; used by `NoBreakModifier` and `SeparatorModifier`.

### Changed

- **`NoBreakModifier` keeps its range together as a single word**: it now also lets the line wrap right before and after the range, so the range drops to the next line whole even when nothing separates it from the surrounding text (e.g. a visible markup tag), instead of only suppressing breaks inside it.
- **Named gradients edit on one line**: each entry in a `UniTextGradients` asset shows its name and gradient side by side in the inspector.

### Fixed

- **Composite modifier dropdowns showed options for the wrong child**: dynamic-option dropdowns and variant fields on a `CompositeModifier`'s children resolved against the composite as a whole instead of the individual child, so they could list the wrong options and let per-field editor state bleed between children.

## [2.12.7] - 2026-06-20

### Fixed

- **Add (+) on a nested type-selector list inserted a blank entry**: on a polymorphic `[SerializeReference]` list nested inside another (e.g. a `CompositeParseRule`'s child rules), + created an empty element instead of opening the type picker.

## [2.12.6] - 2026-06-20

### Added

- **Standalone rules in the Add Style menu**: standalone parse rules — such as `<br>` and the markup-protection rules — now appear in a component's Add Style (+) menu grouped by category, instead of only in the raw type picker.

### Changed

- **`<br>` is now a soft line break**: it wraps the line but stays in the same paragraph — keeping the surrounding text direction and taking no paragraph spacing — matching HTML `<br>`; a literal newline still starts a new paragraph.

## [2.12.5] - 2026-06-20

### Fixed

- **Per-glyph overrides ignored on list markers, ellipsis and ruby**: a `UniTextFont` glyph's advance, offset and size overrides applied to regular text but not to list bullets/numbers, ellipsis dots, or ruby annotations rendering the same glyph — these now honor them too.

## [2.12.4] - 2026-06-20

### Added

- **Signed package**: UniText is now cryptographically signed under the Light Side publisher organization, so Unity 6.3+ no longer shows the "can't verify this package because it doesn't have a signature" warning in the Package Manager.

## [2.12.3] - 2026-06-19

### Fixed

- **Nested child graphics drew behind the text**: a UI graphic (e.g. an `Image`) parented under a UniText GameObject rendered beneath UniText's text and inline sprites instead of on top of them, contrary to uGUI's child-over-parent draw order.

## [2.12.2] - 2026-06-19

### Fixed

- **Reordering styles reset their parameter values**: dragging a style to a new slot in a Styles list (on a component or a `StylePreset`) reset its Default Parameter — and any `RangeRule` per-range parameters — to type defaults.

## [2.12.1] - 2026-06-17

### Fixed

- **`UniTextWorld` not rendering with Domain Reload disabled**: world-space text stayed blank in Play Mode when "Reload Domain" was turned off in Enter Play Mode Options, until a manual Reload Domain.

## [2.12.0] - 2026-06-16

### Added

- **Show / hide text without rebuilding — `Show()`, `Hide()`, `IsVisible`** (on `UniText` and `UniTextWorld`): turn text off and back on instantly and allocation-free, reusing the already-built result instead of reprocessing it, with hidden text drawing nothing and ignoring pointer events; use this for frequently toggled text (pooled lists, tooltips, HUD) instead of disabling the GameObject/component or clearing the text, which forces a full rebuild on every re-show.

## [2.11.0] - 2026-06-16

### Added

- **Word spacing — `WordSpacingModifier`** (default tag name `wspace`): adjusts the gap at spaces between words — the regular space and no-break space — without touching letter-to-letter spacing (CSS `word-spacing`); `<wspace=0.25em>` or a raw pixel value, negative tightens, tabs and fixed-width spaces are left untouched.
- **Per-glyph metric overrides on `UniTextFont`**: a glyph override can now also scale the glyph's advance width — changing layout, line breaking and caret positions, e.g. to tame an over-wide icon-font glyph — and shift or resize it visually, on top of the existing SDF tile-size override; edits apply live in the inspector.
- **`UniTextFont.SpaceAdvance` ("Space Width")**: override the advance width of the space (U+0020) per font, in design units — for fonts whose space is too wide or too narrow, or that ship without a space glyph; default auto-fills from the font.
- **Change a style's type in place**: each entry in a Styles list (on a component or a `StylePreset`) now has a dropdown to swap its modifier/rule or preset without removing and re-adding it; any `[SerializeReference]` list also gets the grouped type picker on its "+" automatically.
- **`StylePreset.ReplaceStyle(index, style)`**: replace a preset's style by index at runtime.
- **`TagRule.Name`**: read a rule's configured tag name (for clipboard, markup round-trip, or custom inspectors).

### Changed

- **Inline media size and offset are now `Vector2`** (breaking): `InlineSprite` / `InlineObject` entries replace `width` + `height` with `size`, and `bearingX` + `bearingY` with `bearingOffset`. Existing sprite/object catalogs keep working but lose any customized size or bearing (these reset to default) — re-enter those values after upgrading.
- **`UniTextFont` inspector reorganized**: settings are grouped into Metrics, Spacing & Style, Sizing and Rasterization; vertical metrics (line height, ascent, descent) are now shown and editable; glyph overrides render as cards with a live glyph preview.

### Fixed

- **Shared inline-media catalog rendered incorrectly across multiple texts**: when one `UniTextSprites` (or inline-object) catalog fed several visible components at once, their sprite/object instances could collide — wrong position, count or content — and could leak GameObjects when the text emptied or the component was destroyed.
- **SDF/MSDF text rendered incorrectly on GPUs without integer shader support**: glyph-atlas decoding relied on integer operations some graphics APIs (e.g. WebGL 1 / OpenGL ES 2) don't support, giving wrong or missing glyphs; it now uses float math with identical results everywhere else.
- **Editor errors during script recompilation or Play Mode entry**: text processing, font initialization and glyph-atlas upkeep no longer run while the editor is reloading assemblies.

## [2.10.0] - 2026-06-13

### Changed

- **GPU glyph rasterization**: where compute shaders are supported, SDF/MSDF glyphs are rasterized on the GPU instead of the CPU, making first-frame and on-demand glyph generation significantly faster — most noticeable on large multilingual or CJK text; platforms without compute support (notably WebGL) automatically fall back to the existing CPU rasterizer with matching results.
- **Lower SDF/MSDF atlas memory**: on platforms using GPU rasterization the glyph atlas no longer keeps a CPU-side copy, roughly halving its memory footprint.

## [2.9.0] - 2026-06-12

### Added

- **Collapsible separators — `SeparatorModifier` + `SeparatorParseRule`** (default tag name `sep`): text between separators wraps as whole segments — a segment either fits entirely on the current line or moves whole to a new line and the separator before it disappears (`Model<sep>Skin` renders `Model | Skin` on one line, or two clean lines when it doesn't fit); the rule inserts a configurable separator string (default `" | "`), `<sep=" ● ">` overrides it per occurrence with quotes preserving spaces, and a segment wider than the text box wraps inside itself without sharing its lines with following segments.
- **`UniTextBuffers.segmentBreaks`**: custom modifiers can mark segment-level break opportunities — text between entries wraps as a unit, and an entry's range collapses when the line break is taken on it (this is what `SeparatorModifier` produces).
- **`MeasureText` + `TextMeasureOptions`**: measure the size text would occupy — the current text or any markup string — without touching the displayed text, layout or mesh; options set box constraints (`maxWidth` / `maxHeight`) and override component settings (font size, auto-size with its min/max, word wrap, padding), each null option falling back to the component's value; auto-size fits the font into the box like in a real rect, and dimensions include `Padding`, so the result maps directly to a `RectTransform`.

## [2.8.3] - 2026-06-12

### Changed

- **Android system-font diagnostics** (`UNITEXT_DEBUG`): a failed OS font fallback now logs why — the matched font file is missing or unreadable, the file has no glyph for the codepoint, or no installed font covers it — instead of silently rendering a missing-glyph box.

### Fixed

- **Text invisible in Play Mode on Unity 6000.6+**: with domain reload disabled (Enter Play Mode Options), entering Play Mode left every UniText component blank until the next script recompile.
- **Glyphs lost under atlas pressure**: rasterizing many glyphs in one frame (large multilingual text, slower devices) could permanently drop some of them — blank gaps until the text changed; freshly rasterized glyphs now survive the full frame, and a text that finds its glyph evicted re-rasterizes it automatically.
- **Switching `RenderMode` could evict other texts' glyphs**: after a render-mode change a component released its glyph references into the new mode's atlas, so glyphs still used by other texts could disappear or atlas memory could leak.
- **Clearing a font's rasterized data mid-frame**: components kept stale or blank glyphs until something else rebuilt them, and glyphs rasterized that frame could render another glyph's pixels; affected texts now rebuild automatically.
- **Flag emoji bypassed the font cache**: every flag (regional-indicator pair) re-ran fallback font resolution and spammed `Fallback font registered` / `Cache hit but fontAssets miss` debug warnings.

## [2.8.1] - 2026-06-10

### Fixed

- **Compile errors on uGUI 2.6+**: the package failed to compile on newer Unity 6 editors where `ILayoutElement` gained max-size members; UniText now implements them and reports unconstrained `maxWidth` / `maxHeight`.
- **Bundled settings asset overrode project defaults**: selecting or reimporting the package's built-in `UniTextSettings` asset silently reset the global default line height (mode and scale) to factory values.

## [2.8.0] - 2026-06-10

### Added

- **`RevealModifier`** (default tag name `reveal`): shows the first part of each covered range and hides the rest — drive `Fill` (0–1) or `VisibleClusters` (absolute count) from code or the inspector for a typewriter effect; `Collapse` picks how to hide — keep the hidden text's space (CSS `visibility: hidden`) or reflow and reshape as if it were absent (CSS `display: none`), so with `Collapse` Arabic joining forms update at the reveal edge exactly as when typing; reveals whole grapheme clusters in logical order and never hides line breaks.
- **Typed modifier lookup on `UniTextBase`**: `GetModifier<T>()`, `TryGetModifier<T>(out modifier)` / `(out modifier, out Style style)`, and allocation-free `GetModifiers<T>(List<T>)` find modifiers by type across local styles, attached style presets, and `CompositeModifier` children — `text.GetModifier<RevealModifier>().Fill = 0.5f;` with no casting.

### Changed

- **Style queries see presets immediately**: `HasModifier`, `TryGetStyle`, `TryGetWholeTextStyle`, and `GetStylesOfType` now include styles from attached presets even before the component's first text rebuild.

### Fixed

- **Inline media, ruby and list markers survived truncation**: sprites/objects, ruby annotations and list markers kept rendering inside text ranges hidden by `EllipsisModifier` / `TruncateModifier`.

## [2.7.0] - 2026-06-10

### Added

- **`NoBreakModifier`** (default tag name `nobr`): keeps the tagged range on one line by suppressing automatic wrapping inside it (CSS `white-space: nowrap`); explicit newlines and `<br>` still break, and a range wider than the text box splits as a last resort, like any unbreakable word.
- **`ParagraphSpacingModifier`** (default tag name `pspace`): adds vertical space between paragraphs on top of line height — `<pspace=10>` puts 10 px after every covered paragraph, a second value adds space before it (`<pspace=10,4>`), `0.5em` / `50%` scale with the font size; where two paragraphs meet the sides add up, and the block's first and last lines get nothing.
- **`TruncateModifier`** (default tag name `truncate`): cuts overflowing text without drawing the `...` marker — `EllipsisModifier` minus the dots, including the position parameter (`<truncate=0>` removes from the start, `0.5` from the middle, end by default).
- **Inline media minimum line height**: `InlineMedia.lineHeight` (em units) makes every line containing that media at least the given height, so a tall inline image opens up its line instead of overlapping its neighbours; `0` (default) leaves line height untouched.

### Changed

- ***Add Style* picker reorganized by category**: *Whole Text* and *Tags* entries are grouped into nested categories with icons (Common, Text Style, Decoration, Appearance, Layout, …), every entry shows its description as a hover tooltip, the first category opens preselected, and search also matches a query typed in the wrong keyboard layout (Russian ЙЦУКЕН — "ищдв" finds *Bold*).

### Fixed

- **Inspector dropdowns jumped away from their button**: a popup (*Add Style*, type or unit pickers) could relocate or mis-size itself when a category submenu opened or the search filter changed, instead of resizing in place under its button.

## [2.6.3] - 2026-06-06

### Added

- **Ruby / furigana annotations — `RubyModifier` + `RubyParseRule`** (default tags `ruby` / `rt`): set a small reading or meaning over or under a base run with `<ruby>漢字<rt>かんじ</rt></ruby>`, per-character mono-ruby `<ruby>東<rt>とう</rt>京<rt>きょう</rt></ruby>`, or the shorthand `<ruby=かんじ>漢字</ruby>`; the reading is fully shaped, so complex-script readings (Arabic, Indic) join and combine correctly; annotations on a line share a common baseline and follow CSS Ruby / browser placement, with `Position` (over / under), `Align` (space-around / center / space-between / start), and `RubyScale`.

## [2.6.2] - 2026-06-04

### Fixed

- **`UniTextWorld` vanished when switching prefabs in Prefab Mode**: opening one prefab directly from another (without leaving Prefab Mode) left the newly opened prefab's world-space text invisible.
- **`SetText` value lost on disable/enable**: text assigned through any `SetText` overload reverted to the serialized `Text` field after the GameObject was disabled and re-enabled.

## [2.6.1] - 2026-06-03

### Added

- **Project-wide text defaults** (Project Settings → UniText → *Text Defaults*): set the default line-height mode and scale, and the default font-size match (x-height / cap-height), applied to every `UniText` / `UniTextWorld` without adding a `LineHeightModifier` or `FontSizeMatchModifier` per component.

### Changed

- **Default line height is now `Scaled` × 1.4**: text with no line-height override uses a fixed 1.4 × font-size line height (uniform rows, matching common UI conventions) instead of growing to the tallest font on each line; restore the old behavior by setting the default mode to `Content` in Project Settings → UniText.

## [2.6.0] - 2026-06-03

### Added

- **`AlignmentModifier`**: sets horizontal alignment, justify mode, and last-line alignment per paragraph — paragraphs in one text object can be individually left-, center-, right-aligned or justified, applied whole-text or to a range.
- **`DirectionModifier`**: sets the base writing direction — auto, left-to-right, or right-to-left (UAX #9 paragraph direction).
- **`TextBoxTrimModifier`**: trims space above and below the text to chosen metrics — e.g. cap-height over baseline for optically centered UI labels (CSS `text-box-trim` / `text-box-edge`).
- **`FontSizeMatchModifier`**: matches mixed fonts to a shared visual size by x-height or cap-height, so a fallback or secondary font no longer looks oversized or undersized next to the primary (CSS `font-size-adjust`); opt a font out with the new **Normalize Size** toggle on `UniTextFont`.
- **Line-height mode on `LineHeightModifier`**: picks how each line's height is set — `Content` (grows to the tallest font on the line, default), `Primary` (primary font only, so fallback glyphs never enlarge the line), or `Scaled` (exactly scale × font size) — plus leading distribution (half-leading / above / below); line height can vary per paragraph.
- **Per-paragraph layout hooks for custom modifiers**: `TextProcessor.ConfigureSettings`, `OnResolveLineStyle`, and `OnResolveLineHeight` let a custom modifier override base direction, box-trim, leading, alignment, justify, and line-height per paragraph (alongside the existing `Shaped` / `LinesBroken` hooks).

### Removed

- **Paragraph layout properties removed from `UniText` / `UniTextWorld`** (breaking): `BaseDirection`, `TextJustify`, `LastLineAlignment`, `OverEdge`, `UnderEdge`, and `LeadingDistribution` now live in `DirectionModifier`, `AlignmentModifier`, `TextBoxTrimModifier`, and `LineHeightModifier` — apply those whole-text or per range. `HorizontalAlignment` and `VerticalAlignment` stay on the component.

## [2.5.1] - 2026-06-02

### Added

- **`InputUtils`**: public helper for backend-safe input reads (keyboard, mouse, touch, typed text) that work under both the legacy Input Manager and the Input System package.

### Fixed

- **`Mask` on a `UniText` didn't clip its children**: child graphics under a `UniText` carrying Unity's `Mask` component rendered unclipped; they are now masked to the text's glyph silhouette, respecting *Show Mask Graphic* and nested masks.
- **Input System (New) compatibility**: a project with Active Input Handling set to the Input System package only threw `InvalidOperationException` every frame and lost text selection, click modifiers, the inspector overlay, and IME positioning; these now work under either input backend.

## [2.5.0] - 2026-06-02

### Added

- **System fonts — automatic OS fallback**: any codepoint your assigned fonts don't cover is now resolved from the operating system's installed fonts and cached, as the last link in the fallback chain; a component with no `FontStack` renders with the OS default sans-serif, so text shows up with no font setup. Available on Windows, macOS, Linux, iOS, and Android (WebGL has no OS font access). Turn it off with `SystemFont.Disabled`.
- **`UniTextSystemFont` asset**: a font asset whose bytes load from the host OS at runtime instead of being embedded, created via *Assets > Create > UniText > System Font Asset*; per-platform tabs (Common / Windows / macOS / Linux / iOS / Android) pick a font guaranteed to ship with each platform, with optional per-platform face-metric and SDF/tile overrides. Add it to a font stack like any other font.
- **`UniTextFontVariant` asset**: reuses another font's raw bytes but defines its own face metrics, render settings, and glyph overrides, created via *Assets > Create > UniText > Font Variant*; render one TTF/OTF two different ways without duplicating the bytes — each variant keeps its own atlas.
- **In-scene inspection overlay**: `Tools > UniText > Inspection Mode` (`Ctrl+Shift+I`) draws per-glyph, per-run, per-line, and per-modifier data over live text for debugging shaping, BiDi, fallback, and layout; also runs in play mode and player builds (toggle `F8`, pin `P`) and is fully scriptable through the static `UniTextInspector` (layers, filter, BiDi arrows, statistics card, explicit target).
- **Variable-font per-axis defaults**: the `UniTextFont` (and `UniTextFontVariant`) inspector now lists every axis the font defines and lets you set a default value per axis; that value drives rendering and shaping when no `<var>` tag is present, and is the base for `<var>` percentage/delta. Any axis the font exposes can be pinned (e.g. `GRAD`, `opsz`), not just the five `<var>` axes.
- **Font collections (`.ttc` / `.otc`)**: choose which face of a TrueType/OpenType Collection to render — a `UniTextFontVariant` exposes a *Face* selector that points at any sub-face while sharing the source's bytes, and the *UniText Tools* window subsets a chosen face to a standalone font. OS fonts that ship as collections (Apple Color Emoji, `Helvetica.ttc`) resolve their covering face automatically.
- **Real OS Bold/Italic on system fonts**: `<b>` and `<i>` over text drawn with a system font (the OS default or a `UniTextSystemFont`) now pull the matching installed cut from the OS — e.g. `Times New Roman` → `Times New Roman Bold` — instead of synthesizing weight from the regular face; synthetic styling is used only when the OS has no real cut covering the text.
- **`<b>` real-only mode** (default tag name `b`): a second parameter `r` (`<b=700,r>`) uses a real bold face only and stays at the natural weight when none matches, never synthesizing — alongside the existing force-synthetic `f`. The inspector *Mode* dropdown gains *Real only*.

### Changed

- **New components no longer need a default font stack**: the *Default Font Stack* editor setting was removed; a `UniText` / `UniTextWorld` created with no font assigned now renders with the OS default font instead of staying blank (except on WebGL, which has no OS font access — assign a regular `UniTextFont` there).
- **`<var>`, `<b>`, `<i>` layer on the font's configured axis defaults**: a variation tag, bold, or italic now adjusts only the axes it names and leaves the rest at the font's configured defaults, instead of snapping unnamed axes back to the font's built-in defaults.
- **`UniTextFont.ItalicStyle` is now settable** (was read-only), matching `FontScale`, `SpacingOffset`, and `FakeBoldWeight`.
- **Font asset render settings apply immediately**: editing SDF detail, tile size offset, glyph overrides, or axis defaults on a `UniTextFont` updates the scene live; the inspector's *Apply* / *Revert* button is gone.

### Removed

- **`UniTextFontStack.FindFontForCodepoint(...)`** (both overloads): per-codepoint font resolution now lives only on `UniTextFontProvider.FindFontForCodepoint` — resolve through the provider instead.
- **`UniTextFontProvider` no longer implements `IDisposable`**: its `Dispose()` was removed; drop any calls to it (the provider holds no resources to release).

### Fixed

- **Emoji text rebuilt its font on every update**: text containing emoji re-created the emoji font's shaping data each time it laid out and never freed the previous copy, so frequently-changing emoji text (chat, counters, typewriter effects) leaked native memory and wasted CPU over a session.
- **Crash when several fonts loaded at once**: parallel layout that loaded or freed font faces from multiple threads simultaneously (scenes with many components, variable fonts, or font variants) could corrupt memory and silently crash the editor or player.


## [2.4.2] - 2026-05-27

### Fixed

- **Duplicate `UniTextSettings.asset` in builds**: the package shipped a copy of `UniTextSettings.asset` in its `Resources/` folder while the install-time defaults restore also placed one under `Assets/UniText/Resources/`, so both ended up in `Resources` and the player build included the asset twice. The package no longer ships the `Resources/` copy — the user-side asset is restored from `Defaults/` as before.

## [2.4.1] - 2026-05-27

### Added

- **`UniTextFont.FakeBoldWeight`**: per-font asset baseline synthetic bold (range `0..2`, measured in CSS weight steps from the font's own weight, where `1 ≈ Regular → Bold`); renders thicker via SDF dilate and compensates glyph advance so layout stays consistent. Exposed in the inspector under *Face Info*, next to *Italic Style* and *Spacing Offset*. Useful for giving light/regular faces extra weight when a true bold cut is unavailable.

### Fixed

- **Variable-font axis values changed glyph shape but not spacing**: `<var=…>` with non-default `wdth` / `wght` / `opsz` resized the glyph outlines but left advance widths at the font's defaults, so a condensed or narrow run still occupied default-width horizontal space.
- **Letter-spacing shifted centered and right-aligned text off-axis**: positive or negative `<cspace=…>` on a centered or right-aligned line counted the spacing after the last glyph in the line width, so the line drifted toward the start edge as spacing grew (e.g. two centered glyphs — only the first moved, the second stayed put). Trailing letter-spacing is now stripped at line edges, matching CSS Text §letter-spacing.

### Added

- **`TextProcessor.LinesBroken` event**: fires after line breaking, before glyph positioning — for modifiers that need to adjust `UniTextBuffers.lines` widths in a line-aware way (mirrors the existing `Shaped` / `LayoutComplete` hooks).

## [2.4.0] - 2026-05-24

### Added

- **`Style.DefaultParameter` and rule-less whole-text styles**: `Style` now supports `Rule == null` to mean "apply this modifier to the entire text" — the new `Style.DefaultParameter` string is forwarded to the modifier on every cycle, and `Style.WholeText(modifier, parameter)` builds this form; in the inspector the rule dropdown shows *Whole Text* instead of *(None)* and the default-parameter field appears under the rule slot.
- **Box-model Padding / Raycast Padding popup** (Modern): clicking the *Padding* or *Raycast Padding* cell opens a Chrome / Firefox DevTools-style box editor — outlined rectangle with one numeric field per side and drag-to-scrub on every edge (Shift = ×10); Classic / New Classic keep the inline L/B/R/T row.
- **Inspector Style selector (Modern / Classic / New Classic)**: the component context menu replaces the single *Use Classic Inspector* toggle with a three-way *Inspector Style* submenu — *Modern* (default), *Classic* (flat URP-Camera-style headers with the modern coloured toggles), *New Classic* (full Unity-default widgets — the previous opt-in). The existing `UniText.UseClassicInspector` preference auto-migrates on first read.
- **Live style counts in section headers and list labels**: the *Style* section header reads `Style (N)`, the *Styles* list `Styles (M)`, *Style Presets* `Style Presets (K)`, and the *Use Global Style Preset* toggle now includes its preset's style count.
- **Use Global Style Preset toggle on the preset-buttons row** (Modern): the toggle is placed next to the Bold / Italic / Underline preset buttons and shown only when a global preset is assigned and carries styles.
- **Text Size fill-slider** (Modern): the in-inspector text-preview *Size N* control becomes a rounded pill with an accent-coloured fill that grows with the value; Classic / New Classic keep the stock `IntSlider`.
- **Public `RangeRule.IsWholeText` and `UniTextBase.IsWholeTextStyle(Style)`**: predicates that recognise both forms of a whole-text style (rule-less and whole-text `RangeRule`), so editor tooling and custom preset menus can match the runtime semantics.

### Changed

- **Whole-text preset styles use the rule-less form** instead of building a whole-text `RangeRule`: clicking *Bold*, *Italic*, *Underline*, etc. from the *Whole Text* group in the *Add Style* picker now produces `Style { Modifier, Rule = null, DefaultParameter }`. Previously-serialised whole-text `RangeRule` styles continue to work at runtime — both forms are recognised.
- **`Style.IsValid`** is now `modifier != null || (rule != null && rule.IsStandalone)`: a style becomes valid as soon as it has a modifier, even when its rule is null (whole-text); standalone rules without a modifier stay valid.
- **Modifier-signature change preserves parameter segments**: when a child of a `CompositeModifier` is added / removed / reordered / swapped in the inspector, existing `;`-separated parameter segments are migrated by type instead of being reset to defaults; only swapping the root modifier type still resets the whole parameter.
- **Breaking — `BaseModifier` lifecycle API access tightened**: the `uniText` field is now `protected` (was `public`); `Prepare`, `Apply`, `SetOwner`, `Destroy`, `Disable` are now `internal` (framework-only); `PrepareForParallel` is now `protected internal` (was `public`). Custom modifier subclasses that override `PrepareForParallel` need to change `public override` to `protected internal override`; external code that read `modifier.uniText` should access the component via its own public API instead.
- **Inspector section headers redesigned** (Modern): expanded sections render the title on a 24 px vertical tinted strip on the left edge of the helpBox (rotated to read bottom-to-top), click anywhere on the strip to collapse; collapsed sections keep the previous top-bar layout. The helpBox now bleeds to full inspector width.
- **`UniTextBuffers.GetOrCreateAttributeData<T>` and `ReleaseAttributeData(key)` are reference-counted**: multiple modifier instances sharing one attribute key are now safe — the underlying buffer is freed only when the count drops to zero (one `Release` per `GetOrCreate`).

### Fixed

- **Modifier resources leaked when a style was removed right after a text change**: removing a style whose modifier had already gone through `OnDisable` (text change earlier in the same frame) would skip `OnDestroy`, leaving pooled buffers and attribute-data subscriptions allocated; `OnDestroy` now runs exactly once for every `OnEnable`.
- **`CompositeModifier` called `OnDisable` / `OnDestroy` on children that were never initialized**, producing null-state confusion in custom subclasses; only initialized children are now tracked and disposed.
- **Style entry inside a list had a duplicate left indent on expand**: the foldout arrow space was added on top of the list's drag-handle gutter, shifting expanded content one column too far right.

## [2.3.2] - 2026-05-23

### Fixed

- **Editor and player silent crash with parallel text shaping**: scenes with several `UniText` or `UniTextWorld` components sharing the same font could trigger a silent native process termination during shaping on Windows / macOS / Linux — symptom was `STATUS_HEAP_CORRUPTION` (Windows Error Reporting) or an unexplained editor exit during Play Mode with no managed exception in the log; parallel shaping is now safe regardless of font, variations, or number of components.
- **Variable-font variations cross-contaminated between components**: parallel shapes of the same variable font with different `<font ax=…>` variation sets could mix settings between callers, so a component could end up shaped with another's variations.

## [2.3.1] - 2026-05-21

### Added

- **Force-synthetic flag on `BoldModifier`** (default tag name `b`): second positional parameter — the literal `f`, e.g. `<b=700,f>` — forces synthetic bold (SDF dilate + advance correction) for the range and ignores real bold faces in the Font Family and the variable-font `wght` axis even when available.

### Fixed

- **Editor crash on first frame with parallel text processing**: scenes with several `UniText` components using `<gradient=…>`, `<sprite=…>`, `<obj=…>`, or `<outline=name>` could throw `NullReferenceException` from inside `Dictionary` on the first frame after enabling, when the named-entry lookup was built simultaneously by multiple worker threads.

## [2.3.0] - 2026-05-21

### Added

- **Custom `Style` inspector label via `ToString()`**: any rule or modifier that overrides `object.ToString()` has that text rendered as its label inside the `Style` array — `TagRule` now uses it to show the configured tag name, e.g. `Underline (Rule: Tag 'u')` instead of `Underline (Rule: Tag)`; types that don't override keep the previous trimmed-type-name label.
- **Justified text alignment**: new `HorizontalAlignment.Justify` value stretches each line to fill the available width, with last-line and script-aware behaviour modelled on CSS Text Module 3.
  - **`TextJustify`** (`Auto` / `InterWord` / `InterCharacter` / `None`): controls how the extra space is distributed; `Auto` picks inter-word for scripts that use whitespace separators (Latin, Cyrillic, Greek, Korean) and falls back to inter-character on lines without whitespace (pure CJK, Thai phrases without phrase-spaces).
  - **`LastLineAlignment`** (`Auto` / `Start` / `End` / `Center` / `Justify`): controls how the paragraph-terminating line is aligned; `Auto` matches CSS `text-align-last: auto` so the last line stays start-aligned instead of being stretched, and a single-line paragraph is treated as its own last line.
  - 4th `Justify` button joins Left / Center / Right in the alignment toolbar; the two sub-properties are exposed in Layout → Advanced and disabled when the alignment is not Justify.
- **`UnicodeData.IsJustifiableWordSeparator(int)`**: public predicate that returns true for the CSS-defined inter-word expansion targets (ASCII space, TAB, ideographic space U+3000, and other Unicode Zs space separators) while excluding no-break variants (NBSP, NARROW NBSP, FIGURE SPACE).
- **`TextLine.endedByMandatoryBreak`**: public field on each layout line indicating whether it was terminated by a UAX #14 mandatory break (CR / LF / NEL / LS / PS / VT / FF), letting custom layout consumers reason about paragraph boundaries.

### Changed

- **Breaking — `TextLayout.Layout(...)` signature**: now takes an extra `ReadOnlySpan<int> codepoints` parameter (between `glyphs` and `perLineAdvances`) used to identify word-separator glyphs during justification; direct callers need a one-line update to pass `buffers.codepoints.Span`.
- **Breaking — `TextProcessor.CanReusePositions(...)` signature**: gains `TextJustify` and `LastLineAlignment` arguments so the positioned-glyph cache invalidates correctly when justify settings change.

## [2.2.17] - 2026-05-21

### Added

- **Collapsible inspector sections**: Text, Font, Layout, Style, Interaction, Rendering, and Debug headers are now click-to-toggle — click anywhere on the header row to expand or collapse; state persists per user across Editor sessions.

### Changed

- **Inspector section headers redesigned**: centered bold title, full-row click target, hover-brightens, semi-transparent dark backing behind the header when expanded, hand cursor on hover; collapsed sections use a muted label so the open one stands out.
- **Row-fill layout in Layout and Interaction sections**: Word Wrap, alignment buttons, and (when shown) Raycast Padding all match the row's tallest element; alignment buttons are square 1:1.
- **Padding and Raycast Padding cells widened**: inline L/B/R/T fields and their drag-scrub labels are larger, making mouse drag-to-adjust easier to hit.
- **Hand cursor on rounded inspector toggles**: alignment buttons, style preset buttons, Word Wrap / Maskable / Raycast Target / Auto Size / Use Global Style Preset, and Expand / Highlight / Rendered now show the hand cursor on hover.
- **Classic Inspector layout matches Unity-default conventions**: bool toggles render as label-left + checkbox-at-field-column rows, Padding / Raycast Padding fit on a single row (label-left, L/B/R/T inline), the alignment row uses native segmented buttons with the standard blue-when-active highlight.

## [2.2.16] - 2026-05-20

### Added

- **`Padding` property on `UniText` / `UniTextWorld`**: Vector4 inner inset `(Left, Bottom, Right, Top)` — same component order as `Graphic.raycastPadding` — that shrinks the text layout area inside the RectTransform without changing the hit-test area. Mirrors CSS `padding`, UIKit `textContainerInset`, and WPF `TextBlock.Padding`; use it for SDF outline / shadow bleed, caret / IME insets, or container breathing space without nesting another transform.
- **Padding inline editor and Scene view outline**: the Layout section gains an L/B/R/T padding row alongside Word Wrap and Alignment (wrap-flow when the inspector is narrow), and the Scene view draws a cyan outline of the padded text area whenever Padding differs from zero.

### Changed

- **Font Size row collapses Auto Size fields**: when Auto Size is on, Min / Max / Current Size now replace the Font Size field on the same row instead of appearing on a second row below — toggling Auto Size no longer shifts the rows above and below.
- **Inline padding labels are part of their row**: Padding (new) and Raycast Padding mini-labels are now drawn inside their inspector row's vertical extent (label on top, L/B/R/T cells below, the whole block centered with adjacent toggles), instead of floating above the row line where they could clip into the row above.

## [2.2.15] - 2026-05-20

### Fixed

- **Synthetic italic broke character spacing**: text styled with `<i>` (or `ItalicModifier`) on a non-italic font shifted characters horizontally by inconsistent amounts depending on glyph height and descenders, producing visibly uneven gaps between letters; spacing now stays uniform regardless of glyph shape.

## [2.1.14] - 2026-05-20

### Added

- **Gradient outlines**: `OutlineModifier` now accepts a gradient name in its colour slot — `<outline=rainbow>`, `<outline=rainbow,0.3,radial,45>`, `<outline=rainbow,,radial,45>` — when an `IGradientProvider` is attached via the new `OutlineModifier.Provider` property; the inspector exposes a *Color / Gradient* dropdown with conditional Shape and Angle fields.
- **`IHasGradientProvider` interface**: any modifier can opt into the inspector's `enum:@gradients` dropdown by exposing an `IGradientProvider`; implemented by `GradientModifier` and `OutlineModifier`.
- **`GradientShape`, `GradientGeometry`, `GradientUtil`**: public helpers for precomputing and evaluating linear / radial / angular gradients from custom modifiers (extracted from `GradientModifier` and now shared with `OutlineModifier`).
- **`EffectQuadOps` helpers**: `WriteSolidEffect`, `WriteGradientEffect`, `ApplyOffset`, `ModulateAlpha`, `WriteEffectUV2` cover the common per-vertex writes for custom `EffectModifier` subclasses without locking them into a fixed data layout.
- **Conditional `[ParameterField]` visibility**: new `VisibleWhen = "slotIndex:label"` argument hides a tag-parameter field in the inspector based on another slot's value and drops the hidden slot from the serialised tag automatically (used by `OutlineModifier`'s Shape and Angle fields, which appear only in *Gradient* mode).
- **Raycast Padding and Maskable in `UniText` inspector**: when *Raycast Target* is on, the inspector shows a compact L/B/R/T `raycastPadding` row and the Scene view draws a matching outline of the active raycast rect; the `Graphic.maskable` toggle is now exposed in the same wrap-flow row.

### Changed

- **Outline tag now positional `<outline=color-or-gradient,dilate,shape,angle>`**: `<outline=#FF0000>`, `<outline=#FF0000,0.3>`, and the default `<outline>` keep working; the single-argument dilate form (`<outline=0.3>`) is replaced by `<outline=,0.3>` (leading comma) so that slots 1–3 can carry dilate, gradient shape, and angle.
- **Breaking — `EffectModifier` API for custom effect modifiers**: `EnqueueEffectQuad(sourceBaseIdx, effectUv, offsetX, offsetY, expandDelta)` is replaced by `EnqueueDuplicate(sourceBaseIdx, payload)` plus a new abstract `OnEmitQuad(sourceBaseIdx, destBaseIdx, payload)` override that writes the per-vertex data via the `EffectQuadOps` helpers; `ApplyOwnRequests` is renamed to `OnFlush`, `AppendSharedEffectQuad` to `ReserveQuad` (now geometry-only — no `Colors` / `Uvs2` writes), and the `EffectPacking` class is removed.
- **Breaking — custom SDF shaders reading the effect-colour UV2 packing**: effect quads now carry their colour in the standard vertex `color` attribute (alpha pre-multiplied by source face alpha on the CPU side), and `texcoord2` becomes `(dilate, effectMode, 0, softness)` where `effectMode` is the per-quad face/effect flag (0 / 1). The `UnpackEffectColor` and `UniTextUnpackEffectColor` helpers are removed — read `v.color` directly and branch on `v.texcoord2.y > 0.5`. Bundled shaders are updated; custom SDF shaders need a one-line port.
- **Breaking — `ObjectUtils.FindFirst<T>` removed**: call `ObjectUtils.FindAny<T>` instead (same Unity-version dispatch, unordered).

## [2.2.13] - 2026-05-20

### Added

- **`UniText.SetText(StringBuilder)` overload**: assign text from a `StringBuilder` without allocating a `string`, matching TextMeshPro's `SetText(StringBuilder)` for drop-in migration; the `StringBuilder` may be mutated freely after the call.

## [2.2.12] - 2026-05-19

### Fixed

- **Line height couldn't shrink below the font's natural ascent+descent**: `<line-height>`, the `LineSpacing` property, and `FaceInfo.lineHeight` values smaller than `ascentLine + |descentLine|` were silently clamped up to that floor; line boxes now honour the requested smaller value, letting glyphs overlap on purpose (matching CSS, Flutter, UIKit, and Android behaviour).

## [2.2.11] - 2026-05-19

### Fixed

- **SDF/MSDF artifacts on fonts built from overlapping subpaths**: glyphs composed of multiple overlapping or self-intersecting contours (e.g. `H` in Teko-Medium, built from three overlapping rectangles) showed broken outline strokes around inner corners and faint ghost lines along the seams where contours meet.

## [2.2.10] - 2026-05-19

### Added

- **`Font` property on `UniText` / `UniTextWorld`**: optional explicit primary font that overrides the primary derived from `FontStack` — when set, `Font` provides strut metrics and the default face while every family in `FontStack` participates only as fallback; `FontStack` may also be omitted entirely for a single-font setup with no fallback chain.
- **`Localize` context menu**: right-click on `UniText` or `UniTextWorld` adds a `LocalizeStringEvent` and wires it to update the component's text whenever the active locale changes; available when the Unity Localization package (`com.unity.localization`) is installed.

## [2.2.9] - 2026-05-18

### Added

- **Sub-1 Font Size warning**: the inspector now warns when Font Size (or Min/Max Size with Auto Size on) drops below 1, recommending lowering the GameObject's Scale instead.

### Changed

- **Scene Visibility overlay hidden by default**: the UniText eye-icon overlay no longer appears in the SceneView toolbar; enable it from *Tools → UniText → Show Scene Visibility Overlay* (preference persists per user across all projects).

## [2.2.8] - 2026-05-18

### Added

- **Classic Inspector toggle**: opt-in switch in the component's three-dot context menu draws inspector boolean toggles in default Unity label-and-checkbox style; preference persists per user across all projects.

### Changed

- **Softer toggle "off" colour**: colored inspector toggles use a dark gray background in the unchecked state instead of black.

## [2.2.7] - 2026-05-15

### Fixed

- **`.otf` fonts rendered with distorted curve shapes**: most `.otf` files (those using PostScript/CFF outlines) drew glyph curves with visible distortion versus the font's design; they now match the design accurately.

## [2.2.6] - 2026-05-15

### Fixed

- **`<sprite>`, `<obj>`, `<mat>`, and emoji crashed on Unity 6+ in batched scenes**: scenes with several `UniText` components using inline sprites, inline objects, custom materials, or emoji threw `InvalidOperationException: EnsureRunningOnMainThread` instead of rendering the affected glyphs.

## [2.2.5] - 2026-05-15

### Added

- **Per-occurrence sprite color**: `<sprite=name,i>` tints the inline sprite with the host UniText component's color (CSS `currentColor` equivalent); `<sprite=name,#FF0000>` (or any hex / named color) overrides per occurrence. Omitting the second argument keeps the sprite's original colors as before.
- **Sprite color inspector**: the `SpriteModifier` parameter editor exposes an *Original / Inherit / Override* dropdown — choosing *Override* reveals a color picker — that serializes directly into the tag.
- **`InlineMediaModifier.OnExtraTokens` hook**: custom inline-media modifier subclasses can parse additional comma-separated arguments after the entry name (per-occurrence tag attributes) without touching the base class.
- **`variant:` `[ParameterField]` type**: declare an enum of options where each option is either a literal token or a typed sub-field (`color`, `float`, `int`, `bool`), so a single tag slot shows a dropdown plus a conditional value editor in the parameter inspector.

### Changed

- **`InlineMediaModifier<TEntry, TWrapper>.ConfigureWrapper` now takes the cluster index** as a third parameter (breaking for custom inline-media modifier subclasses): use it to apply per-occurrence overrides parsed in `OnExtraTokens`.

### Fixed

- **`ShadowModifier.FixedPixelSize` offset uneven per glyph**: with `FixedPixelSize` on, each glyph's shadow shifted by a different distance instead of all glyphs sharing one on-screen offset.

## [2.2.4] - 2026-05-13

### Added

- **Add Component inherits project default prefab settings**: adding `UniText` or `UniTextWorld` via *Add Component* (or *Reset* in the inspector) now seeds its serialized fields from the prefab assigned in *Project Settings → UniText* (`Text Prefab` / `World Text Prefab`).

## [2.2.3] - 2026-05-13

### Changed

- **Compact font-size inspector row**: Font Size and Auto Size now share a single row, and when Auto Size is on, Min, Max, and Current Size share one row instead of three.
- **Layout "Advanced" foldout**: Base Direction, Over Edge, Under Edge, and Leading Distribution moved into a collapsible *Advanced* foldout inside the Layout section, leaving wrap and alignment as the only fields visible by default.

### Fixed

- **Tooltips missing on colored toggle buttons**: the *Auto Size*, *Word Wrap*, alignment, preset, and *Expand* / *Highlight* / *Rendered* toggles in the inspector showed no tooltip on hover even when the underlying serialized field declared one.

## [2.2.2] - 2026-05-12

### Fixed

- **Tag attribute values lost `<` and `>` literals**: a quoted attribute value containing `<` or `>` (e.g. Unity Input System binding paths like `<sprite="<Keyboard>/a">`) was truncated at the first inner `>`; values wrapped in `"…"` or `'…'` now preserve any `<>` characters verbatim with no entity escaping.

## [2.2.1] - 2026-05-12

### Fixed

- **Edits to a `StylePreset` skipped components that had it empty at load**: when components were initialized with the preset still empty, later additions to the preset (inspector or `AddStyle`) had no effect on those components until they were disabled and re-enabled.

## [2.2.0] - 2026-05-12

### Added

- **`SpriteModifier`** (default tag name `sprite`): embed `Sprite` assets inline with text without authoring a prefab per icon. The catalog of named sprites is supplied by an `ISpriteProvider` — built-in providers are `InlineSpriteProvider` (default — list edited on the modifier) and `AssetSpriteProvider` (shared `UniTextSprites` asset). Custom providers (input-prompt icons, localisation, item icons) implement `ISpriteProvider` directly and raise `Changed` when their resolution result changes.
- **Shared inline-object catalog** (`UniTextObjects` asset, *Assets → Create → UniText → Objects*): named inline-prefab catalog for `<obj=name>` tags shared across components — alternative to editing the inline list per modifier. Mirrors the existing `UniTextGradients` and the new `UniTextSprites` (*Assets → Create → UniText → Sprites*) patterns.
- **Project-wide style preset** (`UniTextSettings.GlobalStylePreset`): a `StylePreset` configured once in *Project Settings → UniText* applies to every component automatically. A new per-component `UseGlobalStylePreset` toggle (default on) opts a single component out — useful for debug overlays or technical text where global markup rules would interfere. Local `Styles` and local `StylePresets` register first and keep override priority on parse-rule conflicts.
- **`UniTextBase.BeforeProcess`** static event: fires before any text processing in the canvas-update cycle, alongside the existing `MeshApplied` and `AfterProcess`.
- **`InlineTagRule`**: tag rule with HTML void-element semantics — every `<tag>` / `<tag=value>` is treated as a single self-closing insertion, the `/>` shorthand is no longer required, and stray closing tags are silently stripped. Used by the new *Inline Sprite* and *Inline Object* presets.
- **`StylePreset` runtime mutation API**: `AddStyle`, `RemoveStyle`, `RemoveStyleAt`, `ClearStyles`, plus an always-on `Changed` event (was editor-only). Components subscribed to the preset rebuild on the next frame.
- **Custom modifiers automatically appear in the inspector preset picker**: any concrete `BaseModifier` subclass without a curated entry is listed alphabetically under a new *Custom* group, paired with a full-text `RangeRule` for immediate use.
- **Per-modifier accent colours** in the inspector: each modifier type gets a stable colour used for its icon tint, the colored label in style entry headers, and the preset toggle button background.
- **Unified rounded toggle-button style** in the inspector with hover outline — alignment buttons, preset toggles, `Auto Size`, `Word Wrap`, `Use Global Style Preset`, and the *Expand* / *Highlight* / *Rendered* toggles now share one visual; the *Rendered* toggle's accent colour reflects the text-override state (green for Resolver, orange for `SetText`).

### Changed

- **`UniTextSettings.Changed` is now `Action<string>`** (breaking): the payload is the property name that changed (`nameof(Gradients)`, `nameof(GlobalStylePreset)`, `nameof(Language)`, `nameof(Dictionaries)`) or the new `UniTextSettings.All` sentinel when the whole asset is replaced via `SetInstance`. Filter with the new `UniTextSettings.Affects(changed, interested)` helper; existing handlers must add a `string` parameter.
- **`IGradientProvider` now derives from `INamedCatalog<UniTextGradients.NamedGradient>`** (breaking for custom providers): replace `TryGetGradient(string, out Gradient)` with `TryGet(string, out UniTextGradients.NamedGradient)` — the entry exposes the `gradient` field. Callers that read from a `UniTextGradients` asset directly are unaffected.
- **`StylePreset.styles` public field removed** (breaking): use the new `Styles` read-only list and the `AddStyle` / `RemoveStyle` / `ClearStyles` mutation API.
- **Modifier configuration fields converted to properties** (breaking): `OutlineModifier.fixedPixelSize`, `ShadowModifier.fixedPixelSize`, `ExtrudeModifier.steps` / `bevel` / `fixedPixelSize`, `ListModifier.markerPlacement` / `bulletMarkers` / `orderedStyles`, `CompositeModifier.modifiers`, and `MaterialModifier`'s render order / sort index / emoji material / quad padding fields are no longer public — assign through the matching PascalCase property (`FixedPixelSize`, `Steps`, `MarkerPlacement`, `RenderOrder`, etc.) so the setter requeues the right rebuild stage. Serialized values migrate automatically; only direct field access from runtime code needs updating.
- **Nested list items indent under their own parent marker**, not under the widest sibling: when a list mixes marker widths between siblings (e.g. an ordered sublist nested under one bullet but not another), the nested column shifts to match its parent's marker width instead of the maximum across the entire list. Matches Google Docs / MS Word; `Outside` placement and level-0 unchanged.
- **Inline Object and Inline Sprite presets** use `InlineTagRule` — `<obj=name>` and `<sprite=name>` no longer need the `/>` suffix to self-close.
- **`GlobalSettingsGradientProvider`** rebuilds only when the project's gradient asset reference or its entries change; unrelated `UniTextSettings` edits no longer trigger a gradient rebuild.

### Fixed

- **`GradientModifier.Provider` setter ignored the catalog `Changed` subscription**: assigning a new provider at runtime left edits to the new provider unnoticed and kept the old provider driving rebuilds.

### Removed

- **`GradientNotifier` static class** (`AnyChanged` event, `NotifyChanged` method): replaced by per-instance `INamedCatalog<TEntry>.Changed` events on each provider. Custom gradient providers should raise their own `Changed` when their resolution result changes.

## [2.1.10] - 2026-05-08

### Added

- **WebGL builds now match the active Emscripten toolchain**: the package ships native WebGL binaries built for legacy Emscripten 3.1 (Unity 2021–6000.4) and modern Emscripten 4.0 (Unity 6000.5+), each in default and WebAssembly 2023 long-jump variants, and the editor picks the right one automatically based on Unity version and the WebGL `wasm2023` PlayerSetting.
- **`ListMarkerPlacement` enum and `ListModifier.markerPlacement` field**: pick between `Inside` (default — marker takes inline space at the line start, content shifts past it; matches Google Docs, MS Word, Discord) and `Outside` (marker right-aligned to a fixed content column, hangs into the leading margin if wider; equivalent to CSS `list-style-position: outside`), direction-aware in both LTR and RTL paragraphs.

### Changed

- **`ListModifier.indentPerLevel` field removed** (breaking — serialized values are dropped by Unity): the per-nesting-level indent step is now derived from the widest marker in the list instead of a fixed `0.55em × fontSize`, so columns auto-fit any marker style without manual tuning. Visible difference is at nested levels — wider for lists with multi-digit numbers or long bullets, narrower for short ones; level-0 layout is unchanged.
- **`<indent>` now indents from the tag opening, not just from the next line**: when the tag opens mid-line, content following the open boundary visibly shifts within the current line; previously only wrapped lines whose first codepoint sat inside the tag were pushed.

### Fixed

- **Markdown list items folded a non-indented following paragraph**: a plain-text line at the same or lower indent than a `-`/`*`/`+`/numbered item was absorbed into the previous list item instead of breaking out into its own paragraph.
- **`CreateAssetWithContent` obsolete-API warning on Unity 6000.4+**: creating a custom UniText shader from `Assets > Create > UniText > Custom Shader` raised a deprecation warning on Unity 6000.4+.

## [2.1.9] - 2026-05-07

### Added

- **`<br>` hard line break** (`LineBreakParseRule`, standalone — register without a modifier): forces a line break wherever `<br>`, `<br/>`, or `<br />` appears in the source, with HTML void-element semantics (no closing tag, no nested content); the new line keeps the surrounding paragraph's direction, alignment, and styling.
- **`IndentModifier`** (default tag name `indent`): adds left indent to every line whose first codepoint falls inside the tagged range, accepting `px`, `em`, `%` (of the host RectTransform width, matching CSS `text-indent` semantics), and signed deltas; nested or overlapping tags compose additively like CSS `margin-left`.

## [2.1.8] - 2026-05-06

### Added

- **`UniTextUnpackEffectColor`** (in `UniText_Custom.cginc`): decodes a `Color32` packed by `EffectPacking.PackColor` from a UV channel with the correct sRGB-to-linear conversion for the active color space — custom material shaders that read effect colors should call it instead of unpacking by hand.

### Fixed

- **Effect colors looked washed out in Linear color space**: outline, shadow, extrude, and other effect-modifier colors rendered noticeably brighter than the face text in projects using Linear rendering.

## [2.1.7] - 2026-05-02

### Fixed

- **Family emoji and other ZWJ sequences misplaced in RTL paragraphs**: parts of multi-glyph emoji (e.g. the children in 👨‍👩‍👧‍👦) drifted away from the main composition when the surrounding text was Arabic or Hebrew, while the same emoji rendered correctly in LTR text.

## [2.1.6] - 2026-04-30

### Fixed

- **Text inside `RectMask2D` clipped off too early**: a UniText whose glyphs extended past its RectTransform (long words without word wrap, outline / glow, modifier offsets) disappeared as soon as the rect itself left the mask, even though the rendered text was still inside the clip area.

## [2.1.5] - 2026-04-29

### Added

- **`UniTextWorld.RaycastTarget`** (default `true`, inspector + property): turn off on purely decorative world-space text and the camera's `UniTextWorldRaycaster` skips it entirely, mirroring Canvas `Graphic.raycastTarget`.
- **One-time warning when an interactive `UniTextWorld` plays in a scene without a `UniTextWorldRaycaster`**: instead of pointer events silently doing nothing, a single Console warning points at the camera that needs the raycaster (or at `RaycastTarget = false` for decorative text).
- **`UniTextBase.CollectRangeEntries(int, int, PooledList<LineRangeEntry>)`** + public `LineRangeEntry` struct: one entry per contiguous glyph run within a line for a cluster range, with X clamped to the line's visible content extent — usable by custom modifiers and tools that need glyph-accurate spans, not just bounding rects.
- **`TextLine.glyphStart` / `glyphCount` / `widthPx` / `IsRtl`** (public): the positioned-glyph range and mesh-local content width are now exposed on each line for custom modifiers reading layout output, along with a paragraph-direction flag from the BiDi level.
- **`CanvasHighlightRenderer` and `WorldHighlightRenderer` are now subclassable** (public): plug a custom Canvas-side or world-space highlight visual without reimplementing the lifecycle.
- **Type-safe per-backend `TextHighlighter` extension points**: subclasses now override `CreateHighlightRenderer(UniText, ...)` and / or `CreateHighlightRenderer(UniTextWorld, ...)` to plug a custom visual on the chosen backend; subclassing `DefaultTextHighlighter` keeps its click / hover / selection logic and only swaps the visual.

### Changed

- **`UniTextBase.CreateHighlightRenderer(string, HighlightOrder)` removed** (breaking for custom highlighter authors): the abstract owner-side hook is gone — implement the typed `TextHighlighter.CreateHighlightRenderer(UniText, ...)` / `CreateHighlightRenderer(UniTextWorld, ...)` overloads instead, and call the protected untyped `CreateHighlightRenderer(name, order)` from event handlers.
- **`TextHighlighter.OnSelectionChanged` removed** (breaking for custom highlighter authors): drive selection visuals from your own state — `DefaultTextHighlighter.SetSelection` shows the intended pattern.
- **`GameObject > UI (World) > UniText > World Text` no longer auto-adds `UniTextWorldRaycaster` to `Camera.main`**: pick the camera explicitly; the new in-scene warning will tell you when it's missing.
- **Double-line decoration**: `<u double>` / `<s double>` now renders each sub-line at the full requested thickness with a same-thickness gap (was: 35% / 30% / 35% summing to the requested thickness), making double underlines and strikethroughs noticeably bolder.
- **Underline / strikethrough end-caps scale with the line's thickness instead of the underscore-glyph height**: thinner lines get proportionally narrower caps for cleaner edges; thick lines keep close to the previous look.

### Fixed

- **AutoSize text shrunk and didn't grow back when the rect grew taller**: after the shrink-to-fit pass reduced the effective font size to fit the height, increasing the rect's height (without changing the width) left the text shrunken until the width changed.
- **Thick underlines / strikethroughs clipped at the top and bottom edges**: when the requested line thickness exceeded the underscore glyph's natural rendered height, SDF sampling fell off the glyph and lost ink near the top / bottom of the line; the sampled region now grows with the requested thickness.
- **Dotted and dashed underlines / strikethroughs drew a partial last mark past the line end**: the pattern loop now stops at the last mark that fits entirely within the segment.

## [2.1.4] - 2026-04-29

### Added

- **Underline / strikethrough styles**: `UnderlineModifier` and `StrikethroughModifier` accept a 5-field parameter — thickness (em or px), offset (em or px), style (`solid`, `double`, `dotted`, `dashed`), skip-ink (line breaks around descenders like g, j, p, q, y), and overlay (line draws above the text instead of behind it); bare `<u>`/`<s>` still defaults to a solid line at the font's metrics.
- **Scene Visibility opt-out**: a Scene view overlay and `Tools > UniText > Respect Scene Visibility` menu toggle whether hiding a UniText / UniTextWorld GameObject in the Hierarchy clears its rendered text (default: on; per-developer, stored in `EditorPrefs`).
- **`UniTextMeshGenerator.EffectPass`** + **`currentEffectPass`** (for custom effect modifiers): a modifier can place its duplicate quads above (`PostFace`) or below (`PreFace`) the face of the current glyph, so e.g. an outline modifier draws around an overlay decoration line on top of the text rather than behind it.
- **`UniTextMeshGenerator.isVirtualGlyph`** (for custom modifiers): the per-glyph callback now fires for modifier-injected quads (decoration lines, kashida, list markers); read this flag to skip them when only real shaped glyphs matter.
- **`UniTextMeshGenerator.QueueEffectTriangle`** + **`RequestBandUpgradeIfNeeded`** (for custom modifiers that emit their own quads): public helpers to route effect triangles through the shared pre/post-face buffer and to request a wider SDF tile for the current quad without duplicating internal logic.
- **`LineRenderHelper.DrawDot`** (for custom decoration modifiers): emits one bullet-shaped dot quad sampled from the font's bullet glyph (U+2022), with a stretched-underscore fallback when the font has no bullet.
- **`UniText/Lit/SDF` and `UniText/Lit/Emoji` now render under URP**: world-space lit text works in Universal Render Pipeline projects from URP 12 (Unity 2021.3 LTS) through URP 17 (Unity 6), receives main-light shadows and additional-light shadows, and uses Forward+ cluster lighting on URP 14+.
- **Lit shaders cast shadows**: world-space text using `UniText/Lit/SDF` or `UniText/Lit/Emoji` now contributes to shadow maps in both Built-in and URP — SDF silhouette is driven by glyph dilate (effect-mode outlines also cast their inflated shape), and emoji uses bitmap alpha with the new `_ShadowCutoff` material property.
- **Lit shaders react to nearby point and spot lights**: additional non-important point/spot lights now affect world-space lit text in both pipelines (up to 4 vertex-evaluated in Built-in; per-pixel with shadow attenuation in URP).

### Changed

- **`UniTextMeshGenerator.Current` removed** (breaking for custom modifiers): replace with the per-component instance `uniText.MeshGenerator`.
- **`UniTextMeshGenerator.onAfterPage` split** (breaking for custom modifiers) into `onMainPassComplete` (emit decoration geometry — also runs through the per-glyph pipeline) and `onMainPassFinalize` (effect modifier flushes); subscribe to whichever your previous handler was for.
- **`LineRenderHelper.DrawLine` signature** (breaking for custom decoration-line modifiers): now takes the generator, cluster, UV cap range, and an explicit thickness override; color is no longer a parameter — it flows through the per-glyph color / gradient / effect pipeline.

### Fixed

- **Parameter field reset when switching the inspector between objects sharing the same Style layout**: when two assets or components each had a Style at the same index (e.g. a `StylePreset` and a `UniText`), switching selection between them reset the newly-selected object's parameter field to the modifier's defaults.
- **`<b>` / `<i>` / `<var>` ignored the family chosen by `FontModifier`**: combining `<font=X>` with `<b>`, `<i>`, or `<var>` resolved the bold/italic face or variable axis from the fallback family instead of the family named by `FontModifier`, so e.g. `<font=Roboto><b>` could render Roboto Regular instead of Roboto Bold.
- **`UniTextWorld` reported infinite mesh bounds**: every world-space text shard reported a 2 km axis-aligned bounding box, breaking frustum culling, shadow caster volumes, and `Renderer.bounds` queries; bounds are now computed from the actual mesh vertices in each shard.
- **`UniTextWorld` ignored GameObject Layer**: world-space text drew through every camera regardless of culling masks, because the batcher merged all components into one shared layer; the batch now keys on the component's Layer (and re-routes when the Layer changes at runtime), so cameras honor their culling mask.
- **Underline, strikethrough, and Arabic kashida ignored per-glyph effects**: gradient, outline, shadow, and custom-material modifiers applied to text but skipped its decoration lines and kashida elongation; decoration geometry now runs through the same per-glyph pipeline and picks up all active modifiers uniformly.

## [2.1.3] - 2026-04-27

### Added

- **`ExtrudeModifier`**: adds a 3D extrude / bevel stack behind the text with a per-step color gradient from near to far, configurable offset, dilate, and softness; an optional bevel mode adds intermediate side-faces for chamfered depth. Step count and bevel toggle live on the modifier; tag parameter format: `offsetX,offsetY,#nearColor,#farColor,dilate,softness`.
- **`EffectModifier` per-layer flush hooks** (for custom multi-layer effect subclasses): `ApplyOwnRequests` is now `protected virtual` and `AppendSharedEffectQuad` is `protected static`, so a subclass can buffer its own per-layer requests and flush them in painter order across all glyphs instead of the default per-glyph order.

### Changed

- **`EffectPacking.PackColor` returns `Vector2`** (breaking for custom effect modifiers and shaders): packed color now occupies `texcoord2.y` and `texcoord2.z`, and custom shaders must call `UnpackColor(input.texcoord2.y, input.texcoord2.z)` — the single-float `UnpackColor(float)` overload is gone.
- **Color alpha is composited with the component's base alpha**: `<color=#RRGGBBAA>` ranges, gradient stops, and underline / strikethrough colours now multiply their alpha with the component alpha instead of discarding it, so `<color=#FF000080>` renders at 50% opacity (was: forced to component alpha). Use a fully opaque parameter to restore the previous look.
- **`LineHeightModifier`: single value parameter, no mode**: the inspector now shows one `Value` field, and `<lh=N>` always sets line height. Existing `<lh=h,N>` markup still parses unchanged; existing `<lh=s,N>` parses but now sets height — use `<lh=+N>` for the additive equivalent.
- **`Glyph Diagnostic` menu moved**: now under `Tools > UniText > Glyph Diagnostic` (was top-level `UniText > Glyph Diagnostic`).

### Fixed

- **Outline and shadow color randomly tinted on some GPUs**: the previous color packing produced NaN/Inf bit patterns that some drivers canonicalize at the vertex–fragment interpolator boundary, randomly altering the green channel as colors crossed certain thresholds.
- **Multiple `GradientModifier` instances on one component overwrote each other**: each instance kept a private 1-based gradient list and stomped the shared per-codepoint index buffer, so the second modifier silently replaced the first one's gradient assignments.
- **Parameter field stuck on stale values after changing the modifier type**: switching a `Style`'s `Modifier` (or a child of `CompositeModifier`) in the inspector now resets the parameter field to the new modifier's defaults instead of keeping the previous modifier's text.

## [2.1.2] - 2026-04-26

### Added

- **`UniTextBase.Animated` event**: raised after Unity Animator applies animated property values to a `UniText` / `UniTextWorld`; modifiers with their own animatable fields can subscribe, diff their state, and call `SetDirty` with the matching `UniTextDirtyFlags`.
- **`AnimationHandlerBase<T>`**: public base class for extending the built-in Animator diff with subclass-specific animatable fields when authoring a custom `UniTextBase` subclass.

### Fixed

- **Unity Animator did not update rendered text**: animating `fontSize`, `color`, `wordWrap`, `autoSize`, `minFontSize` / `maxFontSize`, `baseDirection`, `horizontalAlignment` / `verticalAlignment`, `overEdge` / `underEdge`, `leadingDistribution` — and on `UniTextWorld` also `sortingOrder` / `sortingLayerID` — silently had no visual effect.

## [2.1.1] - 2026-04-26

### Added

- **`IGradientProvider`** with three built-in implementations — `GlobalSettingsGradientProvider` (default, reads `UniTextSettings.Gradients`), `AssetGradientProvider` (per-modifier asset reference), `InlineGradientProvider` (inline list edited on the modifier itself); pick the source for each `GradientModifier` from the inspector.
- **Live gradient preview in the `GradientModifier` parameter dropdown**: each row shows the actual gradient swatch on the right and reflects the provider currently assigned to that modifier, not just the project-wide settings.
- **`GradientNotifier`**: static `AnyChanged` event raised when any gradient source visible to `GradientModifier` is edited (asset, inline list, or a custom provider invoking `NotifyChanged`); affected text rebuilds on the next frame without manual refresh.
- **Public Unicode character properties for modifier / parse-rule authors**: `UnicodeData.GetSimpleUppercase` / `GetSimpleLowercase` / `GetSimpleTitlecase`, `GetGeneralCategory` (+ public `GeneralCategory` enum), `GetScript`, `IsExtendedPictographic` / `IsEmojiPresentation` / `IsEmojiModifierBase`, `IsDefaultIgnorable` — backed by bundled UCD tables and identical across Mono, IL2CPP, and standard .NET.
- **`ParameterOption` + `ContextualParameterOptionsProvider`**: extension API for `[ParameterField("enum:@key")]` dropdowns — options can carry a display label, a per-row preview decorator, and a description, and can be derived from the owning modifier instance.

### Changed

- **`UppercaseModifier` / `LowercaseModifier` / `SmallCapsModifier` resolve case via the bundled Unicode case mapping table** instead of `char.ToUpper/LowerInvariant`; behavior is identical across Mono, IL2CPP, and standard .NET runtimes.

### Fixed

- **Emoji rendered as `.notdef` inside a `FontModifier` range**: emoji codepoints in a range covered by `FontModifier` were forced through the chosen text font, which has no emoji glyphs; emoji now always resolve to the emoji font regardless of any explicit font override.
- **`FontModifier` did not fall back to the FontStack chain**: codepoints not covered by the named family produced `.notdef` instead of falling through the standard fallback chain (as the docs already promised).
- **`UppercaseModifier` skipped Greek final sigma (U+03C2 ς)**: the last character of words like "πόνος" was left lowercase due to a runtime gap in Mono's case tables.

## [2.1.0] - 2026-04-25

### Added

- **Language-aware shaping (BCP 47 + OpenType `locl`)**: fonts with language-specific glyphs (pan-CJK like Noto Sans CJK / Source Han Sans) now render the correct regional forms. Apply per-range with `LanguageModifier`, per-component via `UniText.Language`, or project-wide via `UniTextSettings.Language`.
- **`FontModifier`**: override the font on a text range by referencing a `FontFamily.name` from the component's `UniTextFontStack`. A matched family wins over both `preferredLanguage` selection and the default fallback chain; the normal chain still kicks in for codepoints the chosen family can't render. Unknown names log a one-time warning.
- **Per-family language hint**: `FontFamily.preferredLanguage` — one font stack can hold region-specific cuts (SC/TC/JP/KR) and pick the right one automatically from the active language.
- **Named font families**: `FontFamily.name` lets you address a family directly from `FontModifier` or code instead of relying on fallback order.
- **World-space batcher shard size**: `UniTextSettings.WorldBatcherShardTargetVertexCount` to tune batching granularity vs. rebuild cost for dense world-space scenes.
- **Custom sub-mesh emission**: a modifier can now emit its own geometry with a custom material/atlas that renders `Under`, `Above`, or alongside the base text, ordered by a `sortIndex` — via `UniTextMeshGenerator.onCollectSubMeshes` and `UniTextRenderData`.
- **Quad expansion API**: `UniTextMeshGenerator.ExpandQuad` + `faceBaseIdx` + `DefaultSdfPadding` — a supported way for effect modifiers to grow a glyph quad so wide outlines / fake-bold / soft shadow don't clip at the quad edge.
- **Text-model properties on `UniTextBase`**: four zero-alloc views covering the full pipeline from authored text to what's drawn.
  - `Text` — the serialized authored value.
  - `RawText` (`ReadOnlyMemory<char>`) — the runtime source assigned via `Text`/`SetText` before any resolver substitution.
  - `RenderedText` (`ReadOnlyMemory<char>`) — what's actually fed into parsing/shaping/layout: the resolver's output if one is active, otherwise `RawText`.
  - `CleanText` (`ReadOnlySpan<char>`) — `RenderedText` with markup stripped.
  - `TextOverride` — flags (`SetText` / `Resolver`) indicating which runtime sources currently diverge from the serialized `Text`.
- **Text resolver hook (`IUniTextResolver` + `UniTextBase.TextResolver`)**: override a component's source text (localization preview, template expansion, key-to-string lookup) without writing to the serialized `text` field, so scenes and prefabs don't get marked dirty.
- **`SetText(ReadOnlyMemory<char>)` / `SetText(string)`**: assign text at runtime without writing to the serialized field and without marking the scene/prefab dirty.
- **`UniText.Language` property**: one-line way to apply a BCP 47 language to the whole text from code, without building a style manually.
- **Click / hover / selection highlighting on `UniTextWorld`**: the `Highlighter` slot now lives on `UniTextBase` and works unchanged on both Canvas and world-space text.
- **Custom highlighter API**: `TextHighlighter` subclasses can now target both Canvas and world-space text by requesting a backend-agnostic surface — `owner.CreateHighlightRenderer(name, HighlightOrder.Behind | Above)` returns a `TextHighlightRenderer` with `Color`, `SetRects(...)`, `Clear()`, `Destroy()`.
- **Style/modifier query and mutation API on `UniTextBase`**: `HasModifier<T>()`, `TryGetStyle<T>()`, `SetWholeText<T>(parameter)`, `ClearWholeText<T>()`, `ToggleWholeText<T>(parameter)`, `GetWholeTextParameter<T>()` and non-generic `Type` overloads. Replaces the manual `new Style { Rule = new RangeRule { data = ... }, Modifier = ... }` boilerplate for programmatic styling.
- **`UniTextWorld` public events + active registry**: static `Activated` / `Deactivated`, per-instance `RenderDataAvailable` / `RenderDataCleared` / `SortingChanged` / `ParentChanged`, and a `UniTextWorld.Active` list of currently enabled instances. Observe world-space text state without scene scans.
- **Click / hover on `UniTextWorld`**: add a `UniTextWorldRaycaster` component to a `Camera` and world-space text receives the same pointer events that worked on Canvas — `RangeClicked` / `RangeEntered` / `RangeExited`, link and hashtag events. No per-text colliders needed. Optional `BlockingObjects` setting to respect 2D/3D physical geometry as occluders.
- **`UniText` in Add Component menu**: discoverable under `UI (Canvas) > UniText` in the inspector's Add Component dropdown.
- **`MaterialModifier`**: apply a custom `Material` to a text range. Shader gets the glyph atlas as a `Texture2DArray`, two constant per-text UV4 channels (`ConstantUv2`/`ConstantUv3`) for runtime-animated shader params, and an optional per-glyph UV writer for staggered effects. Three compose modes — `Replace` (hide the base text on the range), `Over`, `Under`. Parameter is an optional tint color. Separate `emojiMaterial` slot for emoji glyphs inside the range.
- **Protection parse rules**: three standalone rules that shield content from any other parse rule (no pairing modifier needed) — `NoparseTagRule` (`<noparse>…</noparse>`, forgiving close), `CodeSpanRule` (balanced backtick runs per CommonMark §6.1: `` `x` ``, ` ``x`` `, ` ```x``` `), and `BackslashEscapeRule` (`\*`, `\[`, …, full CommonMark ASCII punctuation set).
- **Standalone parse rules**: a rule can be registered on `UniText` without a paired modifier (opt-in via `IParseRule.IsStandalone`) — it applies its effect on its own. Used by the three protection rules above.
- **`Style` static builders**: `Style.WholeText(modifier, parameter)`, `Style.Range(modifier, start, end, parameter)`, `Style.Tag(modifier, tagName, defaultParameter)` — replace the `new Style { Modifier = ..., Rule = new RangeRule { data = new() { ... } } }` boilerplate when building styles in code.
- **`RangeEx.WholeText` / `RangeEx.IsWholeText(...)`**: canonical `".."` constant and a predicate that accepts any equivalent syntactic form (`".."`, `"..^0"`, `"0.."`) — useful when building rules from user input.
- **`SubMeshModifier` abstract base class**: base class for writing your own modifiers that produce a separate sub-mesh with its own material (the same surface `MaterialModifier` is built on).
- **Custom shader authoring**: `Assets/Create > UniText > Custom Material Shader` menu scaffolds a new shader pre-wired for `MaterialModifier` (uses `UniText_Custom.cginc`, binds the glyph atlas `Texture2DArray`). Three example shaders ship as starting points (Dissolve, Hologram, Rainbow).
- **Noise generator**: `Tools > UniText > Noise Generator` window produces seamless grayscale value / FBM PNG textures (64–1024 px, configurable seed / frequency / octaves / lacunarity / gain / invert / tileable). Used by the example shaders; available for any procedural need.
- **Lit shaders for world-space text**: `UniText/Lit/SDF` and `UniText/Lit/Emoji` pick up ambient + a single directional light + fog, suitable for `UniTextWorld` in a 3D scene.
- **Default materials**: ready-to-use `UniTextLit`, `UniTextEmojiLit`, `UniTextDisolve`, `UniTextHologram`, `UniTextRainbow` materials in `Defaults/Materials/` (drop on a `MaterialModifier` or assign as the material of a `UniTextWorld`).
- **`GameObject > UI (World) > UniText > World Text` menu item**: creates a ready-to-go `UniTextWorld` object and auto-adds a `UniTextWorldRaycaster` to `Camera.main` so pointer events work out of the box.
- **Basic Usage sample extended**: new Language and Font sections, plus a bundled Source Han Sans subset (`Fonts/SourceHanSans-Demo.otf`, ~96 KB, SIL OFL 1.1) that actually shows CJK regional-glyph differences in the Language example.
- **Language APIs (public)**: `LanguageRegistry.Register/GetHandle/GetTag`, `LanguageMatching.Matches`, `Shaper.ShapeInto(..., IntPtr language)` overload, `HB.LanguageFromString` / `HB.SetLanguage` / `HB.ShapeRun(..., IntPtr language, ...)`, `UniTextFontStack.FindFontForCodepoint(uint, string preferredLanguage, ...)`, `UniTextFontProvider.FindFontForCodepoint(int, byte language)` — for code that drives shaping manually.
- **`UniTextFontStack.TryGetFamilyByName(name, out family)` / `UniTextFontProvider.TryGetFontIdByFamilyName(name)`**: resolve a `FontFamily.name` to a family or fontId at runtime.
- **`SharedFontCache.TryGet` / `Set` language overloads**: per-codepoint font-cache key now includes the active language, so the same codepoint can cache different results under different language tags.
- **`UniTextBuffers.PrepareStartMargins()`**: for modifier authors writing start-margin values (list indentation etc.) — lazily allocates the buffer to fit the current codepoint count.
- **`PooledBuffer<T>.ClearAll()` / `PooledArrayAttribute<T>.ClearAll()`**: clear the entire backing array (not just the `[0..count)` prefix) — matches the modifier-attribute usage pattern where the buffer is read at arbitrary indices.
- **`UniTextMaterialCache.Highlight`**: shared flat-colour transparent material used for range highlights (exposed for custom highlighter renderers).
- **`Run.language` / `ShapedRun.language`** (public struct fields): carries the language-registry index through the pipeline.
- **Project-wide language in Project Settings**: a Localization section in the UniText Settings panel edits `UniTextSettings.Language` without writing code.
- **`Tile Size Offset` per-font setting** (UniTextFont inspector): nudge the auto-classified SDF atlas tile size up or down by ±2 steps to force higher quality or save atlas memory; ignored on glyphs that have an explicit per-glyph tile override.

### Changed

- **Faster first-frame glyph generation**: SDF/MSDF preparation scales much better with glyph complexity — the internal contour-overlap check is now linear instead of quadratic in the number of curve segments per glyph. Biggest wins on CJK, decorative, and symbol fonts.
- **Inspector modifier/rule picker**: the popup now sizes to its content and resizes when groups expand/collapse, instead of truncating at 15 items.
- **FontStack inspector (collapsed family row)**: shows `name` and `primary` inline so you can rename / swap fonts without expanding the foldout.
- **Dirty flags / render mode enums lifted to top level** (breaking): `UniTextBase.DirtyFlags` → `UniTextDirtyFlags`, `UniTextBase.RenderModee` → `UniTextRenderMode`. Code referencing the old nested enums will not compile.
- **`CleanText` return type** (breaking): `string` → `ReadOnlySpan<char>`. The backing buffer is pooled and may be rewritten on the next rebuild; copy via `new string(span)` if you need a stable string.
- **`Text` getter semantics** (potentially breaking): always returns the serialized authored value, even when a buffer-based `SetText` has overridden the runtime text. Read the runtime-assigned text via `RawText` (or `RenderedText` if a resolver is in play).
- **`TextHighlighter.Initialize(UniText)` → `Initialize(UniTextBase)`** (breaking for custom highlighter subclasses). The `owner` field type switched from `UniText` to `UniTextBase` for the same reason — highlighter now works on both Canvas and world-space text.
- **Per-tag parse-rule classes are now `internal [Obsolete]`** (breaking if referenced in code): `BoldParseRule`, `ItalicParseRule`, `ColorParseRule`, `SizeParseRule`, `UnderlineParseRule`, `StrikethroughParseRule`, `CSpaceParseRule`, `LineSpacingParseRule`, `LineHeightParseRule`, `OutlineParseRule`, `ShadowParseRule`, `ObjParseRule`, `EllipsisTagRule`, `UppercaseParseRule`, `GradientParseRule`, `LinkTagParseRule`. Existing serialized assets still deserialize; new code should use `TagRule` (directly or via `Style.Tag(modifier, "name")`).
- **Custom `EffectModifier` subclasses** (breaking): the extension hook is now `OnGlyphEffect()` + `EnqueueEffectQuad(...)` instead of `RecordEffectGlyph(...)`, and the `HasVertexShifts()` override is gone. Built-in outline/shadow are unchanged for consumers; this only matters if you wrote your own effect subclass.

### Fixed

- **Auto-size on `UniTextWorld`**: `autoSize` on world-space text silently fell back to `maxFontSize` — the size-fitting step was Canvas-only. It now runs for world-space components too.
- **`UniTextWorld` sorting vs. other renderers**: world-space text was batched into one mesh regardless of each instance's sorting layer/order, so it rendered in front of or behind `SpriteRenderer` and other renderers as one block instead of interleaving per-instance. The batcher now groups by `(material, SortingLayer, OrderInLayer, SortingGroup)`, so each group becomes its own draw with the correct sorting.
- **Outline / shadow artifacts on emoji**: `OutlineModifier` and `ShadowModifier` applied their effect passes to emoji glyphs too, which rendered color bitmaps through an SDF effect shader and produced garbage. Both now skip color bitmap glyphs.



## [2.0.15] - 2026-04-20

### Fixed

- **WebGL emoji disappearing after text change**: Reusing the same emoji in a later text update left a transparent gap where the emoji should be rendered.
- **WebGL emoji missing when not at the start of text**: Emoji appearing after any preceding characters — including dual-presentation symbols like ⬅, ➡, ❤, ☀ — were not rendered and took no layout space.
- **`<size>` tag spreading letters apart at non-100% scales**: Letters inside a size-scaled range kept their original spacing and bearings while only the glyph quads shrank or grew, so small scales (e.g. `<size=10%>`) produced tiny letters scattered across the original word width instead of a proportionally compact word.
- **Diacritics detached from base letter inside `<size>`**: Arabic and other combining marks floated far from their base glyph when a per-range size scale was applied, because mark offsets were not scaled along with the base advance.

## [2.0.14] - 2026-04-18

### Fixed

- **RTL list marker position unstable**: Bullet and number markers in right-to-left lists (Arabic, Hebrew) shifted unpredictably when the item text changed and could render in the wrong position depending on the first character of the line.

## [2.0.13] - 2026-04-18

### Changed

- **Temporarily disabled native atlas upload path**: Atlas texture updates fall back to the standard upload to avoid crashes on macOS and glyph corruption seen in 2.0.0–2.0.12. Native path will return once stabilized.

## [2.0.12] - 2026-04-17

### Fixed

- **Korean text breaking mid-word**: The line breaker could split Korean (Hangul) text between adjacent syllables at optional break points, producing broken line wraps inside words.
- **SDF/MSDF artifacts inside glyphs with holes**: Bridge regions between overlapping contours (letters such as O, A, B, D, e) produced false distance gradients, visible as faint streaks or specks inside the hollow areas of the glyph.
- **`enableWordWrap` toggle not re-flowing text**: Switching the word-wrap setting between updates could reuse the cached line layout from the previous mode, leaving text wrapped (or unwrapped) incorrectly until another layout-invalidating change.

## [2.0.11] - 2026-04-15

### Fixed

- **Emoji ignoring RectMask2D soft edges**: Emoji glyphs were clipped with a hard edge under a `RectMask2D` with non-zero softness, while surrounding UI elements faded smoothly across the same boundary.

## [2.0.10] - 2026-04-15

### Fixed

- **Style preset effects leaking onto other text**: Modifiers inside a `StylePreset` (bold/italic/underline used via `CompositeModifier`) kept their attribute flags and event subscriptions between text updates, so italic, underline, and bold visibly appeared on unrelated characters after switching to text that did not use the preset tags.

## [2.0.9] - 2026-04-15

### Fixed

- **Underline/Strikethrough skipping lines at small font sizes**: At Font Size ≤3.6, every other line rendered without its underline or strikethrough — lines 1 and 3 got a line, lines 2 and 4 were bare.
- **UniText Text/Button created outside prefab in Prefab Mode**: Right-clicking empty space in the Hierarchy while editing a prefab placed the new `UniText - Text` or `UniText - Button` under a Canvas in the open scene instead of inside the prefab.

## [2.0.8] - 2026-04-14

### Added

- **`ParameterReader` exposed as public API**: Custom modifier authors can now parse tag parameters (floats, unit floats, colors, tokens) using the same locale-safe reader as built-in modifiers.
- **`GlyphAtlas` read-only introspection API**: The SDF and MSDF atlases are now reachable via `GlyphAtlas.GetInstance(RenderMode)`, with `TryGetEntry` returning a public `GlyphEntry` (page index, encoded tile, glyph metrics, pixel size). Key-building helpers `MakeKey`, `DefaultVarHash`, `ComputeVarHash48`, the `TileSizeFromEncoded` decoder, and constants `Pad`/`PageStride`/`DefaultBandPixels` are also accessible for tooling and custom renderers.

### Changed

- **Editor menus reorganized**: `Tools/UniText Tools` and `Tools/UniText Migration` consolidated under the `Tools/UniText/` submenu. GameObject creation moved from `GameObject/UI/UniText - Text|Button` to `GameObject/UI (Canvas)/UniText/Text|Button`.

### Fixed

- **Trailing empty line missing from range bounds**: `GetRangeBounds` skipped the empty line produced by a trailing newline (e.g. `"abc\n"`), so selection highlighting and link/hashtag bounds covering that line returned no rectangle.
- **Stencil material showing wrong atlas texture**: Text under a `Mask` could render with a stale or mismatched atlas texture after an atlas resize, especially when a renderer group mixed text and emoji glyphs.
- **Atlas shrink corrupting glyphs**: Trimming unused atlas pages used a mixed GPU/CPU copy path that could leave stale slice data and drop or corrupt glyphs after the atlas compacted.

## [2.0.7] - 2026-04-08

### Fixed

- **Font Subsetter dropping OpenType layout tables**: Subset fonts lost all GSUB/GPOS/GDEF/kern tables, breaking contextual shaping (Arabic connected forms, Indic conjuncts, ligatures, kerning).
- **Android 16KB page size compatibility**: Native GPU library failed to load on Android 15+ devices with 16KB memory pages.

## [2.0.3] - 2026-04-07

### Added

- **Runtime Style Preset API**: `AddStylePreset()`, `RemoveStylePreset()`, and `ClearStylePresets()` methods for assigning shared style presets to text components through code.

### Fixed

- **Text invisible after reparenting out of a Mask**: Moving a UniText object from under a `Mask` at runtime via `SetParent` left stale stencil material, causing text to disappear.
- **Editor errors when reverting a prefab with nested UniText**: "Revert All" on a prefab containing a nested UniText component produced `MissingReferenceException` for destroyed `CanvasRenderer`.
- **Click events not reaching parent Button**: UniText nested inside a Button blocked `OnPointerClick` from reaching the parent. Now pointer events propagate to the parent unless an interactive range (link, hashtag, etc.) is clicked.
- **RTL list marker offset on wrapped lines**: List markers shifted closer to text when an RTL line was wrapped by the line breaking algorithm.

## [2.0.2] - 2026-04-06

### Fixed

- **iOS emoji sequences not combining**: Emoji with skin tone modifiers, ZWJ sequences, and flag sequences rendered as separate glyphs on iOS. Fixed by shaping emoji through CoreText with `kCTTypesetterOptionAllowUnboundedLayout`.
- **`<lh>` delta units**: Delta values (`<lh=+5px>`) were treated as absolute instead of adding to the default line advance.
- **`<lh>` overridden by globalMinAdvance**: Custom line heights set by `<lh>` were silently replaced by the global minimum advance. Now only applies to lines with default height.

## [2.0.1] - 2026-04-04

### Fixed

- **GPU texture upload on Vulkan (Windows)**: Native GPU upload path was disabled for Vulkan, falling back to `Texture2D.Apply()`.
- **GlyphAtlas resize crash**: Atlas resize with 1 slice caused Unity to collapse `Texture2DArray` into `Texture2D`, breaking native upload.
- **GlyphAtlas resize losing glyphs**: Resize used `Graphics.CopyTexture` which could fail. Now re-uploads dirty slices via native GPU upload.
- **Unity 2021 compatibility**: `TextureCreationFlags.DontInitializePixels | DontUploadUponCreate` guarded behind `UNITY_2022_1_OR_NEWER`.
- **Scene Visibility not hiding UniText/UniTextWorld**: Eye icon toggle in hierarchy had no effect.
- **UniTextWorld invisible in Prefab Stage**: World-space text was invisible when editing prefabs. Batcher now creates a separate instance per Prefab Stage

## [2.0.0] - 2026-04-01

### Added

#### SDF/MSDF Rendering Pipeline

- **GlyphAtlas** (`Runtime/FontCore/GlyphAtlas.cs`): Shared `Texture2DArray`-backed glyph atlas with two singleton instances — one for SDF (`RHalf`) and one for MSDF (`RGBAHalf`). Features adaptive tile sizes (64/128/256 based on glyph complexity), shelf-based packing within 2048x2048 pages, reference counting with LRU eviction, automatic page recycling, and atlas shrinking.
- **SdfGenerator** (`Runtime/FontCore/SdfGenerator.cs`): Burst-compiled `IJobParallelFor` that generates single-channel SDF tiles using contour-seeded vector propagation (8SSEDT). Operates on raw quadratic Bezier curves — no bitmap rasterization.
- **MsdfGenerator** (`Runtime/FontCore/MsdfGenerator.cs`): Burst-compiled `IJobParallelFor` that generates multi-channel SDF tiles in `RGBAHalf` format. Three per-channel seed+propagate passes with tangent carry for pseudo-distance encoding, plus a fourth channel-agnostic error correction pass.
- **SdfCore** (`Runtime/FontCore/SdfCore.cs`): Shared types and reference implementations of SDF/MSDF algorithms — `GlyphTask` struct (used by both generators), tile transforms, Y-monotone splitting, winding number computation, 8SSEDT propagation (with and without tangent), Newton refinement, and quadratic solver. Both `SdfJob` and `MsdfJob` inline their own copies of the algorithms for optimal Burst codegen.
- **GlyphCurveCache** (`Runtime/FontCore/GlyphCurveCache.cs`): Per-font lazy extraction of glyph outlines as quadratic Bezier segments via FreeType `OutlineDecompose`. Normalizes curves to [0,1] glyph space, computes per-contour winding, runs edge coloring, and sorts segments by Y. Includes a thread-safe FreeType face pool for parallel extraction.
- **EdgeColoring** (`Runtime/FontCore/EdgeColoring.cs`): Port of msdfgen's `edgeColoringSimple` — assigns per-edge RGB channel masks for MSDF rendering. Detects corners via cross/dot product thresholds and cycles colors at corner vertices. Computes bisector vectors and corner flags for each segment.
- **RenderMode** enum on `UniText` component: `SDF` (single-channel) or `MSDF` (multi-channel) — controls which atlas mode the component uses.
- **SDF Detail Multiplier** on `UniTextFont`: Controls tile size classification — higher values force larger atlas tiles for fonts with thin strokes (e.g. calligraphic).
- **Glyph Overrides** on `UniTextFont`: Per-glyph tile size overrides (Auto/64/128/256) for fine-tuning quality on specific glyphs.

#### Font Family Architecture

- **FontFamily struct** on `UniTextFontStack`: `families[]` array replaces old flat `fonts` + `variants` lists. Each family has a `primary` font and optional `faces[]` (bold, italic, light, etc.) with a pre-computed `FontFaceLookup` for fast weight/style matching.
- **FontFaceLookup**: Sorted weight arrays, variable font slots (upright + italic), CSS §5.2 weight matching via BinarySearch. Pre-computed at initialization.
- **Variable font support**: `VariationModifier` with `<var>` tag for direct axis control (wght, wdth, ital, slnt, opsz). `UniTextFont.VariableAxes` exposes axis metadata. `IsVariable` property. Variable font axis enumeration via HarfBuzz (`hb_ot_var_get_axis_infos`) and variation setting via `hb_font_set_variations`.
- **Three-tier face resolution** in `ResolveFontFaces()`: (1) Variable font axes — if font has wght/ital/slnt, set axes directly; (2) Static font face — CSS §5.2 weight matching via `FontFaceLookup.FindFace()`; (3) Synthesis — fake bold/italic buffers remain non-zero for shader-based synthesis.
- **`<b>`/`<i>` semantic tags**: Automatically resolve to variable axes when available, fall back to static faces, then to synthesis. `<var>` tag provides direct axis control without fallback.
- **CSS font-weight scale for bold**: `BoldModifier` uses weight scale 100-900 encoded as a byte per codepoint. Smart default: `max(700, baseWeight + 300)`. Explicit parameter: `<b=500>` for CSS weight 500. Fake bold applied via SDF shader dilate (`UV1.y`) and per-glyph advance correction using FreeType's embolden ratio (em/24).
- **Variation run tracking**: `VariationRunInfo` struct and `variationMap` dictionary in TextProcessor track per-run axis values. `Shaper.Shape()` accepts `HB.hb_variation_t[]` parameter. FreeType coordinates set via `FT.SetVarDesignCoordinates()`.
- **FaceInfo auto-population** (editor): `familyName`, `styleName`, `weightClass`, and `isItalic` are automatically extracted from font data via FreeType on `OnEnable`/`OnValidate` and kept in sync. Fields are read-only in the inspector.
- **Native variable font API**: HarfBuzz axis enumeration/variation setting and FreeType Multiple Masters support (`FT.GetMMVar`, `FT.SetVarDesignCoordinates`) in `FT.cs` and `HB.cs`.

#### Word Segmentation for SE Asian Scripts

- **WordSegmentationProcessor** (`Runtime/Unicode/WordBreak/WordSegmentationProcessor.cs`): Post-processes UAX#14 line breaks — dispatches contiguous SA-class script runs (Thai, Lao, etc.) to registered word segmenters.
- **BestPathSegmenter** (`Runtime/Unicode/WordBreak/BestPathSegmenter.cs`): Dictionary-based best-path (maximal matching) DP algorithm — same approach as ICU Thai. Inserts `Optional` break opportunities at word boundaries.
- **DoubleArrayTrie** (`Runtime/Unicode/WordBreak/DoubleArrayTrie.cs`): Read-only compact double-array trie for fast dictionary lookup. Thread-safe after construction.
- **WordSegmentationDictionary** (`Runtime/Unicode/WordBreak/WordSegmentationDictionary.cs`): ScriptableObject holding compiled trie data for a specific script. Configured via `UniTextSettings.dictionaries[]`.
- **Dictionary Builder** tab in UniText Tools window: Builds dictionary assets from word list text files. Supports drag-and-drop, multi-file selection, target script selection, and automatic trie compilation.

#### Effect System (Outline, Shadow)

- **EffectModifier** (`Runtime/ModCore/EffectModifier.cs`): Abstract base class for modifiers that render an additional effect pass behind the face. Registers `EffectPass` (apply/revert callbacks) on the mesh generator. Provides `RecordEffectGlyph()` to store per-glyph UV and offset data, and `ApplyToMesh()`/`RevertFromMesh()` to write effect data to UV2 channel with vertex position offsets.
- **OutlineModifier** (`Runtime/ModCore/Modifiers/OutlineModifier.cs`): Outline effect via `<outline=dilate>`, `<outline=#color>`, or `<outline=dilate,#color>`. Supports fixed pixel size mode. Defaults: dilate=0.2, color=black.
- **ShadowModifier** (`Runtime/ModCore/Modifiers/ShadowModifier.cs`): Shadow/underlay effect via `<shadow=#color>`, `<shadow=dilate,#color>`, or `<shadow=dilate,#color,offsetX,offsetY,softness>`. Supports vertex shifts for offset shadows and fixed pixel size mode. Defaults: dilate=0, color=black 50% alpha.
- **EffectPacking** (`Runtime/Core/EffectPacking.cs`): Static utility for packing `Color32` into a single `float` via bit reinterpretation for shader unpacking.
- **UV2/UV3 buffers** on `UniTextMeshGenerator`: On-demand allocation of additional UV channels for effect layer data.
- **Multi-pass rendering** in `UniText.UpdateSubMeshes`: Effect passes rendered before the face pass using separate materials (Base shader). Each pass applies and reverts its mesh modifications via callbacks.

#### Material Management

- **UniTextMaterialCache** (`Runtime/Core/UniTextMaterialCache.cs`): Static cache that lazily creates and manages shared materials — SDF Face, SDF Base, MSDF Face, MSDF Base. MSDF variants use the `UNITEXT_MSDF` shader keyword. Subscribes to atlas texture changes and syncs `_MainTex` automatically.
- **Shader references on UniTextSettings**: `requiredShaders[]` array stores references to Base, Face, and Emoji shaders. `GetShader(int index)` provides runtime access. Settings provider auto-populates these on editor load.

#### Tag System Overhaul

- **TagRule** (`Runtime/ModCore/Rules/TagRule.cs`): Universal configurable tag parse rule that replaces all individual per-tag rule classes. A single sealed class with a serialized `tagName` field. Supports `defaultParameter` for fallback values and automatic parameter merging (tag-supplied values take priority, remaining fields filled from default).
- **MarkdownWrapRule** (`Runtime/ModCore/Rules/MarkdownWrapRule.cs`): Parse rule for Markdown-style symmetric wrap markers (`**`, `*`, `~~`, `++`). Configurable marker string, stack-based open/close matching, priority by marker length.
- **Simplified TagParseRule base**: Parameters are now always optional (no `HasParameter` virtual). Self-closing is purely syntax-driven (`<tag/>` or `<tag=value/>`). Removed `HasParameter`, `IsSelfClosing`, `InsertString` virtual properties.
- **DeprecatedTagRules** (`Runtime/ModCore/Rules/DeprecatedTagRules.cs`): All 16 tag parse rule classes (14 old + 2 new for outline/shadow) consolidated as hidden one-liner definitions marked with `[HideFromTypeSelector]` for backward-compatible deserialization.

#### Editor UX

- **Selector** (`Editor/Selector.cs`): Full-featured searchable popup selector with grouped mode (expandable group headers with submenu panels), flat search mode (multi-word tokenized, case-insensitive), keyboard navigation, description panels, theme-aware icons, auto-close on focus loss, and optional search field toggle.
- **Mod Register Presets**: The modifier list in the UniText inspector now opens a `Selector` with ~30 predefined presets (Bold, Italic, Outline, Shadow, Markdown variants, etc.) with icons and descriptions. Presets auto-configure both modifier and parse rule.
- **RangeRuleDataDrawer** (`Editor/RangeRuleDataDrawer.cs`): Custom property drawer for `RangeRule.Data` that generates structured UI for modifier parameters based on `ParameterFieldAttribute` metadata. Supports float, int, color, bool, string, enum, and unit (px/em/%) field types.
- **UniTextFontStackEditor** (`Editor/UniTextFontStackEditor.cs`): Custom inspector for `UniTextFontStack` with a Font Families section — each family displayed as a foldable group with primary font, faces list, family name mismatch warnings, weight/italic labels, add/remove buttons, and drag-and-drop zone.
- **Glyph Picker** in font editor: Type text to preview glyph rendering, select individual glyphs, and add tile size overrides directly from the preview grid.
- **Variable Axes Info** in font editor: Displays detected variable font axis metadata (tag, name, min/default/max) when a variable font is loaded.
- **UniTextObjectMenu** (`Editor/UniTextObjectMenu.cs`): `GameObject/UI/` menu items for creating UniText Text and Button objects. Supports prefab overrides via `UniTextSettings`. Creates Canvas/EventSystem if needed.
- **Atlas preview tabs**: Font editor preview split into SDF, MSDF, and Emoji tabs. Uses a `Hidden/UniText/AtlasPreview` shader to display raw distance field textures (grayscale for SDF, RGB for MSDF) from `Texture2DArray` slices.
- **Theme-aware editor icons**: `UniTextEditorResources` provides tinted icon caching for dark/light theme, with per-group and per-type icon mappings.
- **Text selection highlight**: `DefaultTextHighlighter` gains a `selectionGraphic` for programmatic text selection display via `SetSelection()`/`ClearSelection()`.

#### Metadata Attributes

- **ParameterFieldAttribute** (`Runtime/Attributes/ParameterFieldAttribute.cs`): Declares modifier parameter metadata (order, name, type, default) for auto-generating editor UI. Applied to all parameterized modifiers.
- **TypeDescriptionAttribute** (`Runtime/Attributes/TypeDescriptionAttribute.cs`): Human-readable description for types, shown in the Selector popup. Applied to all modifiers and parse rules.
- **HideFromTypeSelectorAttribute** (`Runtime/Attributes/TypeSelectorAttribute.cs`): Hides a type from the type selector dropdown while keeping it deserializable.

#### Virtual Glyph Injection

- **`virtualPositionedGlyphs` buffer** on `UniTextBuffers`: Separate buffer for glyphs injected by modifiers (ellipsis dots, list markers). Does not affect hit testing or selection.
- **`BeforeGenerateMesh` event** on `UniText`: Raised after glyph positioning but before mesh generation, allowing modifiers to inject virtual glyphs.
- `EllipsisModifier` and `ListModifier` now inject `PositionedGlyph` entries into the virtual buffer instead of drawing directly during mesh generation.

#### UniTextWorld (3D Text Rendering)

- **UniTextWorld** (`Runtime/Core/Component/UniTextWorld.cs`): World-space text rendering component. Provides the same text processing pipeline as `UniText` (Unicode, BiDi, shaping, line breaking, modifiers, emoji, font fallback, variable fonts) but renders via MeshRenderer + MeshFilter instead of CanvasRenderer. No Canvas required.
- **UniTextBase** (`Runtime/Core/Component/UniTextBase.cs`): Extracted shared base class from `UniText` — all text processing, modifier management, dirty flags, lifecycle, and parallel batch pipeline now live in `UniTextBase`. Both `UniText` (Canvas) and `UniTextWorld` (MeshRenderer) inherit from it.
- **UniTextBase_Parallel** (`Runtime/Core/Component/UniTextBase_Parallel.cs`): Extracted parallel batch processing pipeline (component collection, glyph batching, atlas rasterization, mesh generation, apply) from `UniText_Parallel` into a shared base partial class.
- **Per-instance owned sub-meshes**: Each effect pass and face segment renders via a dedicated child GameObject (`-_UTWSM_-`) with its own MeshFilter + MeshRenderer + per-instance Mesh (`HideFlags.HideAndDontSave`). Sorting order controls render layering (effects behind face).
- **Phased mesh upload**: Base vertex data (positions, UV0, UV1, UV3, colors, triangles) written once to all SDF sub-meshes; effect passes then overwrite only changed channels (UV2 + vertex shifts). Skips `Mesh.Clear()` when vertex count is unchanged between frames.
- **UniTextWorldEditor** (`Editor/UniTextWorldEditor.cs`): Custom inspector for `UniTextWorld` with sorting order and sorting layer controls.
- **UniTextBaseEditor** (`Editor/UniTextBaseEditor.cs`): Extracted shared editor base class from `UniTextEditor` for reuse by both `UniTextEditor` and `UniTextWorldEditor`.

#### SmallCaps and Lowercase Modifiers

- **SmallCapsModifier** (`Runtime/ModCore/Modifiers/SmallCapsModifier.cs`): Renders lowercase letters as small capitals. Two-tier approach: (1) Native — activates OpenType `smcp` feature via HarfBuzz for proper small cap glyphs; (2) Synthesis — converts to uppercase and scales down by 0.8x (fallback for fonts without `smcp`). Per-codepoint attribute byte: 0 = unchanged, 1 = native, 2 = synthesis. Synthesis adjusts both vertex positions and shaped glyph advances.
- **LowercaseModifier** (`Runtime/ModCore/Modifiers/LowercaseModifier.cs`): Transforms text to lowercase within marked ranges. Applied during modifier Apply phase before shaping.
- **`smcp` feature detection** in `Shaper`: `HasSmcpFeature()` test-shapes `'a'` with and without `smcp` feature, compares glyph IDs. Result cached per font ID in `smcpSupportCache`.
- **HarfBuzz feature support**: `hb_feature_t` struct and `Shape(font, buffer, features)` overload for passing OpenType features to shaping. `MakeTag()` utility for constructing OpenType tag values.
- **Shaper features parameter**: `Shaper.Shape()` now accepts optional `hb_feature_t[]` for per-run OpenType feature activation (used by SmallCaps for `smcp`).

#### Other

- **UI Creation Prefabs** on `UniTextSettings`: `textPrefab` and `buttonPrefab` fields for customizing `GameObject/UI/` menu item creation.
- **FreeType `OutlineDecompose`**: New native API that decomposes glyph outlines into quadratic Bezier segments in design units, replacing the old SDF bitmap rendering path.
- **FaceInfo extensions**: Added `weightClass` (CSS 100-900 from OS/2 `usWeightClass`) and `isItalic` (from FreeType `style_flags`) to the `FaceInfo` struct.
- **DefaultParameterAttribute** (`Runtime/Attributes/DefaultParameterAttribute.cs`): Declares default parameter values for modifiers, enabling parameter auto-fill in the editor.
- **ParameterFieldUtility** (`Editor/ParameterFieldUtility.cs`): Extracted shared parameter field drawing logic from `RangeRuleDataDrawer` for reuse by `DefaultParameterDrawer` and other editors.
- **Emoji atlas Texture2DArray**: `EmojiFont` now maintains a `Texture2DArray` synced from staging `Texture2D` pages, with incremental dirty-page sync.
- **ColorParsing** (`Runtime/ModCore/ColorParsing.cs`): Shared static utility for parsing hex (#RGB, #RRGGBB, #RRGGBBAA) and 21 named colors. Extracted from `ColorModifier` for reuse by OutlineModifier, ShadowModifier, and RangeRuleDataDrawer.

### Changed

#### UniTextWorld Rendering

- `UniText` component refactored: shared logic (text processing, modifier management, dirty flags, lifecycle, parallel pipeline) extracted to `UniTextBase`. `UniText` retains only Canvas-specific rendering (`CanvasRenderer`, stencil, `UpdateGeometry`).
- `UniText_Parallel` refactored: batch pipeline logic extracted to `UniTextBase_Parallel`. `UniText_Parallel` retains only Canvas-specific click handling.
- Mesh generator callbacks renamed to camelCase: `OnGlyph` → `onGlyph`, `OnAfterPage` → `onAfterPage`, `OnRebuildStart` → `onRebuildStart`, `OnRebuildEnd` → `onRebuildEnd`.
- Mesh generator: removed unused public fields (`currentShapedGlyphIndex`, `x`, `y`, `width`, `xScale`, `atlasSize`, `gradientScale`, `spreadRatio`, `rectWidth`, `hAlignment`, `currentFontId`). `SetHorizontalAlignment()` method removed.
- `UniTextFontProvider`: renamed `MainFont` → `PrimaryFont`, `MainFontId` → `PrinaryFontId`. Internal field names updated accordingly.
- `EmojiFont`: emoji atlas textures now use mipmaps (`Texture2D` and `Texture2DArray` created with `mipmap=true`). Filter mode changed to `Trilinear` with `mipMapBias = -0.5f`. Packing spacing increased from 1 to 4 pixels to prevent mipmap bleeding.
- All modifier base classes updated to use renamed `UniTextBase` references instead of `UniText`.

#### Rendering Pipeline

- Mesh generator rewritten from group-by-font-then-atlas iteration to single-pass loop over all positioned glyphs. SDF glyphs look up tiles in the shared `GlyphAtlas`; emoji glyphs processed separately in `GenerateEmojiSegment`.
- UV encoding changed: UV0.zw = `(tileIdx, glyphH)` for atlas tile lookup; UV1 = `(aspect, faceDilate)` as `Vector2` (was `Vector4`).
- Glyph metrics now use design units directly throughout the pipeline — removed `pointSize`-based `metricsConversion` factor.
- `UniTextRenderData` simplified to carry only mesh and font ID; materials assigned externally via `UniTextMaterialCache`.
- Multi-pass effect rendering in `UpdateSubMeshes`: effect passes render before the face pass, each with apply/revert callbacks modifying UV2 and vertex positions.
- Required canvas shader channels extended to include `TexCoord2` and `TexCoord3` for effect layers.
- Glyph reference counting: `UniText` component tracks `currentGlyphKeys` and calls `AddRef`/`Release` on the atlas, enabling accurate eviction.
- Atlas pre-allocation: estimated tile area per atlas mode calculated before rendering, enabling `GlyphAtlas.PreAllocate()`.
- Periodic atlas maintenance: page recycling every 60 frames, atlas shrinking every 300 frames.
- Mesh generator glyph lookup changed from `fontHash` (int) to `varHash48` (long) — supports variable font axis variation. `variationMap` from buffers used to resolve per-run variation hashes.

#### Font System

- `UniTextFont` no longer owns atlas textures — all atlas management delegated to `GlyphAtlas` singletons.
- Glyph preparation/rendering pipeline rewritten: `PrepareGlyphBatch` filters via `GlyphAtlas.TryGetEntry` and protects existing entries with `AddRef`; `RenderPreparedBatch` extracts curves via `GlyphCurveCache` (supports parallel extraction); `PackRenderedBatch` queues segments to `GlyphAtlas.EnsureGlyph`.
- `CreateFontAsset()` simplified — removed `samplingPointSize`, `spreadStrength`, `renderMode`, `atlasSize` parameters.
- `ClearDynamicData()` disposes curve cache and clears font entries from the shared atlas instead of destroying per-font textures.
- `OnDestroy()` now calls `Shaper.ClearCache()` to properly release HarfBuzz native data (was previously leaking).
- `FaceInfo.pointSize` removed; replaced by `weightClass` and `isItalic` fields.
- HarfBuzz memory: `Shaper.FontCacheEntry` now pins the managed `byte[]` via `GCHandle` instead of copying to unmanaged memory via `Marshal.AllocHGlobal`, eliminating the duplicate font data in memory.
- Glyph lookup key changed from `uint glyphIndex` to `long glyphKey` (48-bit variation hash + glyph index) via `GlyphAtlas.MakeKey(varHash48, glyphIndex)`. Enables the same font to cache different glyph shapes for different variable font axis values.
- `PrepareGlyphBatch` and `RenderPreparedBatch` now accept `varHash48` and `ftCoords` parameters for variable font rendering. FreeType design coordinates set before glyph extraction.

#### Font Provider

- Removed `Appearance` property and `GetMaterials()` method from `UniTextFontProvider`.
- Constructor no longer takes an `appearance` parameter.
- Constructor now calls `BuildResolvedFamilies()` to flatten the entire fallback chain into a `resolvedFamilies[]` array with `fontIdToFamilyIndex` dictionary for O(1) family lookup.
- `HasVariants`/`FindVariant()` replaced by `HasFaces` property, `GetFamilyIndex(int fontId)` and `GetFamilyLookup(ushort familyIndex)` for direct access to `FontFaceLookup`.

#### Parallel Pipeline

- Font batch key changed from `UniTextFont` reference to `(UniTextFont, RenderModee, varHash48)` struct — variable font runs with different axis values are batched separately.
- Glyph collection no longer filters already-atlased glyphs at collection time.
- `RasterizeGlyphBatches` extracted as a separate method with per-batch timing diagnostics.
- `DoGenerateMeshData` now clears virtual glyphs buffer, invokes `BeforeGenerateMesh`, and passes virtual glyphs alongside regular glyphs to `GenerateMeshDataOnly`.
- `PeriodicAtlasMaintenance()` extracted as a separate static method, called before component processing instead of after.

#### Modifier System

- `BaseLineModifier` refactored: line segment computation extracted into `ComputeLineSegments()`, executed once then rendered per page. No longer restricted to matching the current font. Event hook changed from `OnAfterGlyphsPerFont` to `OnAfterPage`.
- `LineRenderHelper` rewritten from 3-quad atlas-based rendering (12 vertices) to 1-quad tile-based rendering (4 vertices) using `GlyphAtlas.TryGetEntry` for underscore glyph lookup.
- `EllipsisModifier` changed from immediate mesh drawing (`GlyphRenderHelper.DrawString`) to virtual glyph injection into `virtualPositionedGlyphs`. Event hook changed from `OnAfterGlyphsPerFont` to `BeforeGenerateMesh`.
- `ListModifier` changed from immediate mesh drawing to virtual glyph injection, same pattern as `EllipsisModifier`. Parameter separator changed from `:` to `,`.
- `LineHeightModifier` parameter format changed from `s:value` to `s,value` (comma-separated).
- `ColorModifier` color parsing logic extracted to shared `ColorParsing` utility class.
- `ItalicModifier` now skips vertex shear when the resolved font is already natively italic (`FaceInfo.isItalic`).
- `BoldModifier` `ParameterField` format changed from `"int"` to `"int(100,900)"` for range-constrained editor UI.

#### Editor

- `UniTextFontToolsWindow` renamed to `UniTextToolsWindow`; menu item changed to `Tools/UniText Tools`. File list refactored into reusable `DrawFileList()` method.
- Font editor: removed Atlas Settings section (point size, atlas size, spread, render mode). Replaced with Settings section (font scale, SDF detail multiplier). Atlas preview changed from per-font `Texture2D` to shared `Texture2DArray` slices.
- Type selector dropdown replaced by `Selector` popup with icons, descriptions, and group navigation.
- Editor resource path changed from `Icons/{name}` to `UniText/Icons/{name}`.
- Settings provider no longer draws `defaultAppearance`; now draws UI Creation Prefabs and Word Segmentation sections.
- `EmojiFont` material shader changed from `UI/Default` to `UniText/Emoji` (via `UniTextSettings.GetShader`).
- `SearchableSelector` renamed to `Selector` (file and class). Added `showSearch` parameter to `Show()` for hiding the search field.
- Font editor: added Apply/Revert buttons for rebuild-required properties (`sdfDetailMultiplier`, `glyphOverrides`). Changes are staged as pending until explicitly applied.
- `RangeRuleDataDrawer`: shared parameter field drawing logic extracted into `ParameterFieldUtility` for reuse.

### Removed

- **UniTextAppearance** (`Runtime/FontCore/UniTextAppearance.cs`): Deleted. ScriptableObject that mapped fonts to rendering materials with per-frame property delta caching. Material management replaced by `UniTextMaterialCache`.
- **SDF rendering classes from FreeTypeParallel** (`Runtime/FontCore/FreeTypeParallel.cs`): `SdfRenderedGlyph` struct and `SdfGlyphRenderer` class removed. `FreeTypeFacePool` rewritten — SDF bitmap rendering via `FT.RenderSdfGlyph()` removed, class retained for color bitmap/emoji rendering only. SDF generation replaced by curve-based `GlyphCurveCache` + Burst SDF/MSDF jobs.
- **GlyphRenderHelper** (`Runtime/ModCore/Modifiers/GlyphRenderHelper.cs`): Deleted. Immediate glyph mesh generation utility (`DrawGlyph`, `DrawString`, `MeasureString`). Replaced by virtual glyph injection pattern.
- **UniTextRenderMode enum** (`Runtime/FontCore/FontTypes.cs`): Removed (had values: SDF, Smooth, Mono). Replaced by `UniText.RenderModee` enum (SDF, MSDF) on the component.
- **AtlasMode enum** (`Runtime/FontCore/GlyphAtlas.cs`): Removed. `GlyphAtlas.GetInstance()` now takes `UniText.RenderModee` directly.
- **Per-font atlas textures**: `atlasTextures`, `atlasSize`, `spreadStrength`, `atlasRenderMode`, `usedGlyphRects`, `freeGlyphRects`, and shelf packing state removed from `UniTextFont`.
- **FreeType SDF native API**: `ut_ft_set_sdf_spread`, `ut_ft_render_sdf_glyph`, `ut_ft_free_sdf_buffer` P/Invoke declarations and wrappers removed from `FT.cs`.
- **Shader GUIs**: `UniText_BitmapShaderGUI.cs` and `UniText_SDFShaderGUI.cs` deleted (old custom ShaderGUI for bitmap and SDF shader inspectors).
- **Individual tag parse rule files** (14 files): `BoldParseRule.cs`, `ItalicParseRule.cs`, `ColorParseRule.cs`, `SizeParseRule.cs`, `UnderlineParseRule.cs`, `StrikethroughParseRule.cs`, `CSpaceParseRule.cs`, `LineSpacingParseRule.cs`, `LineHeightParseRule.cs`, `GradientParseRule.cs`, `EllipsisTagRule.cs`, `ObjParseRule.cs`, `Link/LinkTagParseRule.cs`, `UppercaseParseRule.cs`. All consolidated into `TagRule` with backward-compatible stubs in `DeprecatedTagRules.cs`.
- **GeneratedMeshSegment struct**: Removed from `UniTextMeshGenerator`. Replaced by `EffectPass` struct for multi-pass rendering.
- **`defaultAppearance`** from `UniTextSettings` and its backup system.
- **`GlyphsByFont`** grouping from `SharedPipelineComponents` (no longer needed with single-pass mesh generation).
- **`sourceFontFilePath`** from `UniTextFont`.
- **`fonts` and `variants`** from `UniTextFontStack`: Flat `StyledList<UniTextFont>` fonts list and `UniTextFont[]` variants array replaced by `FontFamily[]` families.
- **`FindClosestVariant()`** from `UniTextFontStack`: Replaced by `FontFaceLookup.FindFace()` with CSS §5.2 directional weight matching.
- **`CurrentAtlasMode`** property from `UniText`: Removed. `GlyphAtlas.GetInstance()` now takes `RenderMode` directly.

#### Zstd Font Compression

- **Zstd-compressed font data**: Font bytes stored in `UniTextFont` assets are now compressed with Zstandard (level 22) at import time. Decompression is lazy (on first `FontData` access) with zero per-frame cost. Benchmarks: **~600 MB/s on desktop, ~175 MB/s on low-end Android**. Typical Latin font (600 KB) decompresses in <1 ms. Build size reduction: **~2.7x for Latin/Arabic fonts, ~1.3x for CJK fonts**.
- **Zstd native integration**: Decompression (`ut_zstd_decompress`, `ut_zstd_get_frame_content_size`) built into the runtime `unitext_native` library across all platforms (Windows, Linux, macOS, Android, iOS, tvOS, WebGL). Runtime library built with `-DZSTD_BUILD_COMPRESSION=OFF` for minimal size (~80 KB).
- **Editor-only compression**: `ut_zstd_compress` and `ut_zstd_compress_bound` live in `unitext_native_editor` (desktop only). `Zstd.Compress()` is available only under `#if UNITY_EDITOR`.
- **Automatic migration**: `OnValidate` detects uncompressed font data via Zstd magic bytes (`0x28B52FFD`) and compresses in-place. No manual migration step needed.
- **Memory optimization**: In runtime builds, compressed `fontData` is freed after decompression to avoid keeping both copies in memory.
- **Burst dependency**: Added `com.unity.burst` >= 1.6.0 to package dependencies.

### Fixed

- **HarfBuzz memory leak on font destroy**: `UniTextFont.OnDestroy()` now calls `Shaper.ClearCache()` to release HarfBuzz native data (unmanaged font copy, hb_blob, hb_face, hb_font). Previously, these resources leaked in the static `fontCache` until domain reload.
- **Duplicate font data in memory**: HarfBuzz `FontCacheEntry` now pins the managed `byte[]` via `GCHandle` instead of allocating a separate unmanaged copy, halving per-font memory overhead.
- **FontSize minimum too restrictive**: `fontSize`, `minFontSize`, `maxFontSize` setters clamped to `1f` minimum, preventing small text in world-space. Changed minimum to `0.01f`.
- **UniTextSettings resilience**: Fixed settings loss on package reinstallation.
- **Unity 2021/2022 compatibility**: Fixed compiler errors for older Unity versions.
