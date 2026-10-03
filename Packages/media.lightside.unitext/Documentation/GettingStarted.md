# Getting Started

UniText is a complete text engine for Unity: Unicode analysis, shaping (HarfBuzz), layout, glyph rasterization (FreeType), atlas management, rendering — and, since 3.0, selection, editing and system integration.

This guide is task-ordered. Read §1–§4 to put styled text on screen; the rest is reference by subsystem.

**Two packages.** `media.lightside.unitext` depends on `media.lightside.core` (MIT, free) — pooling, worker threads, math, catalogs, the inspector toolkit, the asset-migration framework, and the value types the UniText API exposes directly: `Ease`, `Gradient`, `UnitValue`, `PaintProjectionKind`, `PaintFit`, `PaintSpread` and `LayerBlend`. Installing UniText pulls Core automatically.

**Coming from TextMesh Pro?** `Tools → UniText → Migration` converts an existing project incrementally; [TmpMigration.md](TmpMigration.md) is its guide.

---

## 1. Adding UniText to a Scene

### 1.1 Canvas text — `UniText`

`GameObject → UI (Canvas) → UniText → Text`. The menu instantiates the prefab in `UniTextSettings.TextPrefab` — the package ships one — so a designer's configured prefab is what appears, not a code-built hierarchy. With the slot empty the menu builds the object in code instead: a `UniText` on a 220×50 `RectTransform` under the scene's canvas, creating that canvas and an `EventSystem` if there is none.

`UniText` derives from `MaskableGraphic`, so it behaves like any uGUI graphic: masks, layout groups, `ContentSizeFitter`, `RectMask2D`, canvas batching all apply.

```csharp
var text = gameObject.AddComponent<UniText>();
text.Styles.Add(Style.Tag(new BoldModifier(), "b"));   // <b> exists only because this line says so
text.Text = "Hello <b>world</b>";
text.FontSize = 24;
text.color = Color.white;
```

No tag is built in. A component with no matching style renders `<b>world</b>` as literal characters — markup is a style list, not a fixed vocabulary (§3). The shipped prefabs and the Styles picker wire the common tags for you.

The same submenu creates `Button`, `Selectable Text`, `Editable Text` and `Input Field`, each from its own prefab slot in `UniTextSettings` — assembled components, not separate types (§12, §13).

### 1.2 World-space text — `UniTextWorld`

`GameObject → UI (World) → UniText → World Text`. Renders through a mesh batcher rather than a `CanvasRenderer`, so it takes part in the scene's normal render queue: sorting layers, sorting order, shadow casting.

World texts that share a sorting context — sorting layer, order in layer, `SortingGroup`, Unity layer, scene, shadow casting — merge into one mesh behind one renderer, and one renderer has one depth-sorting position: nothing can draw between two texts of the same batch. Enable `Standalone` on a text to give it a renderer of its own; it then sorts by its own distance to the camera against every other transparent renderer in the scene, at the cost of one draw call.

Pointer input for world text is routed by `UniTextWorldRaycaster` — add it to the camera that should see the text (see §10.4).

### 1.3 Which one to use

| | `UniText` | `UniTextWorld` |
|---|---|---|
| Parent | Canvas | any Transform |
| Culling / clipping | Canvas mask, `RectMask2D` | frustum, camera layers |
| Sorting | canvas draw order | `SortingLayerID`, `SortingOrder` |
| Batching | per canvas | per sorting context; `Standalone` opts out |
| Lighting | unlit | unlit or lit shaders |

Everything else — markup, fonts, paints, animation, selection, editing — is identical; both derive from `UniTextBase`.

---

## 2. Fonts

UniText does not use Unity font assets. It reads OpenType files directly through FreeType and HarfBuzz, so OpenType features, variable axes and complex scripts work as the font author intended.

### 2.1 Creating a font asset

`Tools → UniText → Tools → Create Font Asset`, or right-click a `.ttf`/`.otf`/`.ttc`/`.otc` file — or a Unity `Font` — → `Create → UniText → Font Asset`.

A `UniTextFont` wraps one font file. It carries the face metrics (`FaceInfo`), its rasterization tuning, variable-axis defaults and per-glyph overrides; the glyph atlas itself is shared by every font of a render mode. The font bytes are Zstd-compressed into the asset, so the source file does not have to ship.

Key inspector settings:

- **Render mode** — `UniTextRenderMode.SDF` (rounded corners on effects) or `MSDF` (sharp corners). Set per component on `UniTextBase.RenderMode`, not on the font asset. MSDF costs more atlas memory; use it for sharp-cornered display type.
- **Rasterization** — `SDF Detail` and `Tile Size Offset` pick the raster tile a glyph lands in (64, 128 or 256 px); raise them for hairline or calligraphic faces. Page size is fixed and the atlas is shared by every font of a render mode.
- **Sizing** — `Font Scale` rescales a face drawn too small or too large by design; `Normalize Size` (on by default) keeps the face in font-size normalization, matching its x-height or cap-height to the primary font. The metric itself is a project setting (Project Settings → UniText → Text Defaults → **Font Size Match**, `FontNormalizeMetric`); `FontSizeMatchModifier` overrides it on a component.
- **Metrics** and **Face Info** — line height, ascender, descender, cap and mean line, underline, strikethrough and sub/superscript offsets, all read from the file and editable when a font ships bad values; `Reset Metrics` restores the file's own. Family name, style, weight and italic flag are read-only unless the asset is a `UniTextFontVariant`. Per-platform overrides (`FaceInfoOverride`) exist only on `UniTextSystemFont` (§2.5).
- **Spacing & Style** — `Spacing Offset` and `Space Width` in design units for faces that render too tight or too loose; `Italic Style` (synthetic slant, percent of height) and `Fake Bold Weight` (CSS weight steps) stand in for a missing italic or bold cut.

### 2.2 Font stacks and families

A `UniTextFontStack` goes in a component's `FontStack` slot; a single face can go in `Font` instead, and when both are set `Font` is the primary while the stack supplies the fallbacks. A stack holds **families**; a `FontFamily` groups a primary plus faces that differ only by style (bold, italic, bold-italic, variable), carries a `name` used by the `<font>` tag, and an optional `preferredLanguage` BCP 47 tag that wins resolution for runs marked with it (§15.2).

Resolution is three-tier, in order:

1. **Requested family** — the family selected by markup or by the component.
2. **Fallback chain** — the next families in the stack, tried in order for characters the first family lacks.
3. **System font** — the OS font, the last resort on every platform except WebGL, which has no OS font access (§2.5).

Emoji-presentation codepoints bypass this chain: an assigned `UniTextColorFont` in the stack wins, otherwise the platform emoji cascade (§17).

This is why a stack with `Inter` + `NotoSansArabic` + `NotoSansCJK` renders mixed English/Arabic/Chinese correctly: each run picks the first family that covers it.

Two stack shapes:

- **Combined** — one stack, families grouped inside. The common case.
- **Per font** — one stack per family, chained. Use when several components need different primary families but the same fallbacks.

Both are created from a font-asset selection: `Create → UniText → Font Stack (Combined)` merges two or more fonts into one stack, `Create → UniText → Font Stack (Per Font)` makes one stack per selected font. A stack chains to the next through its `Fallback Stack` slot.

### 2.3 Variable fonts

A variable font exposes axes (`wght`, `wdth`, `ital`, `slnt`, `opsz`). UniText reads them and can instance any point in the design space. Set defaults per font asset (`UniTextFont.AxisDefault`, inspector **Variable Font Axes**) and override per range with `<var>` (§4.1).

`UniTextFontVariant` (`Create → UniText → Font Variant`) borrows another font's bytes and owns everything else — metrics, rasterization, axis defaults, glyph overrides — with its own atlas and shaper cache, so pinned axis defaults give a reusable design-space point without duplicating the file.

Variable instancing is memory-aware — `UniTextFontProvider.VariationMemoryStats` returns a `FontVariationMemoryStats` snapshot of what the variation cache holds.

### 2.4 Tools window

`Tools → UniText → Tools`:

- **Create Font Asset** — batch-create `UniTextFont` assets from font files.
- **Font Subsetter** — strip unused glyphs from a font to shrink the build. Pick **Keep** or **Remove**, give it character sets, ranges or a text corpus, and it emits either a reduced `.ttf` file or a `UniTextFont` asset built from it.
- **Dictionary Builder** — builds the word-segmentation dictionary used for line breaking in scripts without spaces (Thai, Lao, Khmer, Myanmar, Chinese, Japanese) — see `WordSegmentationDictionary`. A built dictionary takes effect only once it is listed in Project Settings → UniText → Word Segmentation (`UniTextSettings.Dictionaries`).

### 2.5 System fonts

**Automatic OS fallback** is on by default: any codepoint no family in the stack covers is rendered from the OS font, so a project that ships only a Latin font still renders Japanese text pasted by a user. Project Settings → UniText → Fonts → **Disable System Font Fallback** turns it off (`UniTextSettings.SystemFontDisabled`, runtime mirror `SystemFont.Disabled`); explicitly assigned `UniTextSystemFont` assets keep working. WebGL has no OS font access — assign a regular `UniTextFont` there.

**Explicit system font** — `Create → UniText → System Font Asset` makes a `UniTextSystemFont`: a font whose bytes come from the OS, picked per platform (Common, Windows, macOS, Linux, iOS, Android) with per-platform face-metric (`FaceInfoOverride`) and rasterization overrides. Put it in a stack to use the OS font as a named family, e.g. to match the platform's UI font. Project Settings → UniText → Fonts → **Default System Font** additionally makes one the primary for components with no `Font` and no `FontStack`.

An entry is requested from the operating system **by family name**, so it resolves wherever the machine keeps that family installed. On Android the entries resolve to the device's own sans-serif, serif and monospace roles, so `Roboto`, `Noto Sans` and `Droid Sans` all yield the real UI face of the device, manufacturer replacements included. A resolved face carries the variable-axis values the OS bound to it. Lookup happens the first time the asset is drawn or its data is read, so `ResolvedFontName`, `ResolvedPath`, `ResolvedPlatform` and `ResolveFailed` stay unset until then.

`SystemFont.MemoryStats` returns a `SystemFontMemoryStats` snapshot of what the system-font cache holds; `SystemFont.InactiveSourceCapacity` and `SystemFont.InactiveByteBudget` bound what it keeps after the last text releases it.

### 2.6 Color fonts

`UniTextColorFont` (`Create → UniText → Color Font Asset`) handles color glyph formats: CBDT/sbix bitmap and COLRv0/COLRv1 vector. Emoji are the common case (§17) — the always-on emoji font stays the provider for emoji-presentation codepoints — but the same path renders any color font. **Color Pixel Size** sets the rasterization size in the shared color atlas; bitmap faces snap to the nearest strike. SVG-in-OT is detected but not rendered, and WebGL rasterizes no embedded color font.

### 2.7 Materials

Materials are shared, not per asset: one canvas material draws SDF, MSDF and color glyphs together, and world text adds a lit and an unlit variant. Custom shaders are covered in §18; assigning a custom material to a range is `MaterialModifier` (§4.1).

---

## 3. Markup: Sources and Modifiers

UniText's markup has no fixed tag table. It separates **which ranges exist** from **what happens to them**:

- **`RangeSource`** produces logical ranges. `ParseRule` is the subtype that finds them by parsing text syntax.
- **`BaseModifier`** applies an effect to those ranges.

A **`Style`** is one Source + Modifier pair. A component holds a list of them.

Nothing couples a tag name to an effect. `<b>` runs `BoldModifier` only because a preset wired it that way. The same modifier can be driven by a tag, a Markdown marker, an authored range, or your own source:

| Source | Syntax | Modifier |
|---|---|---|
| `TagRule("b")` | `<b>bold</b>` | `BoldModifier` |
| `TagRule("strong")` | `<strong>bold</strong>` | `BoldModifier` |
| `MarkdownWrapRule { Marker = "**" }` | `**bold**` | `BoldModifier` |
| `Style.WholeText(…)` | *(entire text, no markup)* | `BoldModifier` |

### 3.1 Adding styles

**Inspector.** Expand **Styles** on the component, press **+**. A searchable picker opens, grouped first by how the style is driven — Whole Text, Tags, Inline Tags, Markdown, Auto-detect, Protection — and under Whole Text and Tags by modifier category (Common, Text Style, Decoration, Appearance, Layout, Interactive, Inline, Utility, Animation, Custom). Picking a preset configures both sides; you can then edit either independently.

**Code.** `Style` exposes four static builders:

```csharp
Style.Tag(modifier, "b", defaultParameter: null)   // driven by <b>…</b>
Style.WholeText(modifier, parameter: null)         // the entire text, no markup
Style.Range(modifier, start, end, parameter: null) // one authored codepoint span
Style.FromSource(source, modifier)                 // any RangeSource you like
```

```csharp
text.Styles.Add(Style.Tag(new BoldModifier(), "b"));
text.Styles.Add(Style.Tag(new ColorModifier(), "warning", "#FF0000"));
```

A style can be switched off without removing it: `Enabled = false` skips it entirely — never parsed, applied or rendered — and preserves its configuration.

### 3.2 Custom tags with default parameters

`TagRule.defaultParameter` pre-fills the modifier's parameters, so the text stays clean:

```csharp
Style.Tag(new ColorModifier(), "warning", "#FF0000")
```

- `<warning>error</warning>` — red from the default
- `<warning=#FFA500>caution</warning>` — the tag wins

For multi-parameter modifiers the merge is per-slot: values present in the tag win, missing slots come from the default. Slots inside one modifier are comma-separated, and an empty slot takes the default — `<pspace=,4>` sets only the second. A `CompositeModifier` splits its parameter by `;`, one segment per child, before each child reads its own slots. `MarkdownWrapRule` supports the same field.

That merge is one stage of the parameter cascade — modifier field, rule default, markup token, owned value (§5.1).

### 3.3 Parse rule types

**Tag rules.** `TagRule` with a configurable name. Parameters are optional. Self-closing is syntax-driven: `<tag/>`, `<tag=value/>`. `InlineTagRule` is the self-closing variant used by inline media.

An opening tag can carry a `#label` anchor, written after the tag name and before any value:

```
<b #intro>named range</b>
<color #warn=#FF0000>named range with a value</color>
<obj #icon=star/>
```

The label names that one occurrence. It is stripped with the tag, travels with the range as `ModifierRange.Label`, and selects it in a range query — `modifier.Ranges().WhereLabel("intro")` (§19.6). Label characters are letters, digits, `_` and `-`. The closing tag stays plain: `</b>`.

**Markdown rules.**

| Rule | Syntax |
|---|---|
| `MarkdownWrapRule`, `Marker = "**"` | `**bold**` |
| `MarkdownWrapRule`, `Marker = "*"` | `*italic*` |
| `MarkdownWrapRule`, `Marker = "~~"` | `~~strike~~` |
| `MarkdownWrapRule`, `Marker = "++"` | `++underline++` |
| `MarkdownLinkParseRule` | `[text](url)` |
| `MarkdownListParseRule` | `- item`, `* item`, `+ item`, `1. item`, `1) item` |
| `RawUrlParseRule` | auto-detects bare URLs: `http(s)://`, `ftp(s)://`, `file://`, `mailto:`, `tel:`, `www.` |

**Utility sources.**

| Source | Purpose |
|---|---|
| `FixedRangeSource` | apply a modifier to authored codepoint ranges, no markup |
| `MutableRangeSource` | ranges maintained at runtime that survive edits (§12.4) |
| `StringParseRule` | match a list of literal patterns (case-sensitive), optionally replacing each with one fixed string |
| `CompositeParseRule` | group several rules under one modifier |
| `TriggerWordParseRule` | auto-detects `<trigger>word` tokens (`@name`, `#tag`) — one configurable trigger character, the word becomes the parameter; commonly paired with `InteractiveModifier` |
| `SeparatorParseRule` | void `<sep>` tag replaced with a configurable separator string (`<sep=" ● ">` overrides it); pair with `SeparatorModifier` |
| `RubyParseRule` | ruby / furigana annotations |
| `LineBreakParseRule` | explicit `<br>`, `<br/>` — inserts a soft line break (U+2028) that wraps inside the paragraph; standalone, no modifier |
| `MathParseRule` | math formula spans (§4.7) |

**Protection rules** shield their content from every other rule. They are *standalone* — registered without a modifier:

| Rule | Syntax | Behavior |
|---|---|---|
| `NoparseTagRule` | `<noparse>…</noparse>` | contents are literal; a missing closer protects the rest |
| `CodeSpanRule` | `` `x` ``, ` ``x`` ` | balanced backtick runs, CommonMark §6.1 |
| `BackslashEscapeRule` | `\*`, `\[`, `\#` | escapes one ASCII punctuation character |

```csharp
text.AddRule(new NoparseTagRule());
text.AddRule(new BackslashEscapeRule());
text.RemoveRule(myRule);
```

`AddRule` accepts standalone rules only. Your own rule opts in with `IsStandalone => true`.

### 3.4 Priority

Ranges from different sources can overlap. Priority belongs to the rule, not the `Style`: `ParseRule.Priority` orders the rules highest first, and the first rule that consumes at a position claims it. Protection rules sit at `int.MaxValue` and always win. Most rules keep the default `0`, including `TagRule`; `MarkdownWrapRule` uses its marker length, `MathParseRule` `10`, `LineBreakParseRule` and `SeparatorParseRule` `1`. Auto-detection is negative — `RawUrlParseRule` and `TriggerWordParseRule` at `-100` — so explicit markup claims a position before a detector does. A custom rule overrides `Priority`.

### 3.5 Style presets

A `StylePreset` is a project asset holding a configured style list. Assign presets to a component (`StylePresets`) to share one markup vocabulary across many components; `UniTextSettings.GlobalStylePreset` applies project-wide when `UseGlobalStylePreset` is on. Local styles compose on top.

Editing a preset asset rebuilds every live component using it.

---

## 4. Built-in Modifiers

Default tag names below are conventions, not constraints — the Styles picker wires most of them, and any modifier takes any tag through `Style.Tag(...)` (§3.1).

Every value a tag carries lands in one of the modifier's **parameters**: §5 is how they resolve, how a modifier of your own declares them, and how code addresses them.

### 4.1 Text style

| Tag | Modifier | Notes |
|---|---|---|
| `<b>`, `**…**` | `BoldModifier` | `<b=700>` picks a weight; `<b=700,f>` synthesizes it, `<b=700,r>` uses a real face only |
| `<i>`, `*…*` | `ItalicModifier` | real italic face, or synthesized slant |
| `<upper>` | `UppercaseModifier` | |
| `<lower>` | `LowercaseModifier` | |
| `<smallcaps>` | `SmallCapsModifier` | uses OpenType `smcp` when the font has it |
| `<size>` | `SizeModifier` | `24`, `150%`, `+10`, `-5` |
| `<color>` | `ColorModifier` | `#RGB`, `#RRGGBB`, `#RRGGBBAA`, or a named colour |
| `<var>` | `VariationModifier` | variable-font axes |
| `<feature>` | `FontFeatureModifier` | OpenType features: `kern 0`, `-liga`, `tnum`, `ss01 2` |
| `<font>` | `FontModifier` | selects a `FontFamily.name` |
| `<lang>` | `LanguageModifier` | BCP 47 tag |
| `<mat>` | `MaterialModifier` | custom material, optional tint |
| `<ruby>`, `<ruby=かんじ>` | `RubyModifier` | furigana above a base run; pair with `RubyParseRule` |
| `<sup>`, `<sub>` | `ScriptPositionModifier` | OpenType `sups`/`subs`, synthesized when the font lacks them |
| — | `GlyphResolutionModifier` | raises the atlas tile resolution of the range's glyphs; grow-only and shared |

Named colours: white, black, red, green, blue, yellow, cyan, magenta, orange, purple, gray, lime, brown, pink, navy, teal, olive, maroon, silver, gold.

**Variable axes** are positional in the order `wght, wdth, ital, slnt, opsz`; `~` skips an axis:

```
<var=700>          weight 700
<var=150%>         150% of the default weight
<var=+200>         +200 from default
<var=700,80>       weight 700, width 80
<var=~,~,~,-12>    slant only
```

**OpenType features** are a comma-separated list of four-character tags. A bare tag means on, a `-` prefix means off, and a value may follow a space, colon or equals sign:

```
<feature=kern 0>       no kerning
<feature=-liga>        no standard ligatures
<feature=tnum>         tabular figures
<feature=ss01 2>       second alternate of stylistic set 1
<feature=-kern,+dlig>  several at once
```

Feature ranges merge: nesting `<feature>` inside `<smallcaps>` or `<sup>` keeps both, and the innermost value wins a tag both set. Tags the font does not carry are ignored.

### 4.2 Layout

| Tag | Modifier | Notes |
|---|---|---|
| `<cspace>` | `LetterSpacingModifier` | `5`, `0.1em`. On cursive scripts positive spacing renders tatweel so joins survive |
| `<cwidth>` | `CharacterWidthModifier` | fits each character into a cell of one width and centers the glyph in it: `1em` full-width, `0.5em` half-width, `auto` the widest glyph of the range |
| `<wspace>` | `WordSpacingModifier` | |
| `<line-height>` | `LineHeightModifier` | |
| `<pspace>` | `ParagraphSpacingModifier` | `<pspace=10>` after, `<pspace=10,4>` after and before; never applied at the block edges |
| `<nobr>` | `NoBreakModifier` | keeps the range on one line, and lets the line break immediately before and after it |
| `<ellipsis>` | `EllipsisModifier` | `1` end, `0` start, `0.5` middle |
| `<truncate>` | `TruncateModifier` | same positions, no `…` marker |
| `<li>`, `- item` | `ListModifier` | markers, indentation, ordered/unordered |
| `<indent>` | `IndentModifier` | |
| `<align>` | `AlignmentModifier` | per-range alignment |
| `<dir>` | `DirectionModifier` | whole-text base direction |
| `<arc>` | `ArcModifier` | curves the baseline |
| — | `SeparatorModifier` | keeps each run between tagged separators whole across wrap; pair with `SeparatorParseRule` |
| — | `TextBoxTrimModifier` | trims space above and below to chosen metrics (CSS `text-box-trim`) |
| — | `FontSizeMatchModifier` | matches mixed fonts on x-height or cap-height (CSS `font-size-adjust`) |

The monospace flag on `<cspace>` is gone. `<cwidth=auto>` replaces it; a second `<cspace>` token is ignored.

### 4.3 Paint layers

The 2.x `<gradient>` and `<outline>` tags are gone. Fills, strokes, shadows and glows are now **paint layers** over a shared paint system (§6). They ship as whole-text style presets rather than tags, because their parameter set is richer than a tag comfortably carries — add them from the Styles picker, and give one your own tag with `Style.Tag(...)` if you want markup control.

| Modifier | Effect |
|---|---|
| `FillModifier` | fills the glyph interior. The first fill claims the base quad; further fills stack |
| `StrokeModifier` | true rim stroke around the glyph outline |
| `ShadowModifier` | offset drop shadow |
| `GlowModifier` | soft halo — a shadow with no offset and a wide edge |
| `InnerShadowModifier` | inset shadow hugging the inner edge |
| `ExtrudeModifier` | extruded depth |
| `PaintOrderModifier` | switches layer-major vs glyph-major compositing (`PaintOrder`) |

`FillModifier`, `StrokeModifier`, `ShadowModifier`, `GlowModifier` and `InnerShadowModifier` each take a colour, a gradient or a texture through the same `PaintRef` (§6). `ExtrudeModifier` shades its slices across a near→far colour pair, and `PaintOrderModifier` carries no paint.

### 4.4 Decoration

| Tag | Modifier | Notes |
|---|---|---|
| `<u>`, `++…++` | `UnderlineModifier` | `LineStyle` follows CSS `text-decoration-style` |
| `<s>`, `~~…~~` | `StrikethroughModifier` | |
| — | `HighlightModifier` | paints a background behind matched ranges; the same presentation renders live selection |

`HighlightModifier` is range geometry, not a glyph effect: `HighlightPresentation` carries its paint, corners and geometry mapping (§7). `UnderlineModifier` and `StrikethroughModifier` draw horizontal lines across the text and carry their own paint, `LineStyle`, thickness, offset, skip-ink and overlay parameters.

### 4.5 Interactive and inline

| Tag | Modifier |
|---|---|
| `<link>`, `[text](url)` | `LinkModifier` |
| `<spoiler>` | `SpoilerModifier` |
| — | `InteractiveModifier` (usually driven by `TriggerWordParseRule`) |
| `<obj=name/>` | `ObjModifier` |
| `<sprite=name/>` | `SpriteModifier` |

### 4.6 Animation

`<reveal>` drives `RevealModifier`; the phase-driven glyph modifiers (`WaveModifier`, `ShakeModifier`, …) are added as styles. Both are covered in §8.

`UniTextDriver` sequences either of them, per range, with no code (§8.5).

### 4.7 Math

`MathModifier` with `MathParseRule` typesets formula spans — parser, layout engine, delimiter builder and symbol tables.

### 4.8 Utility

`CompositeModifier` runs several modifiers in one slot. `EmptyModifier` is a no-op placeholder useful when a source should mark ranges without changing appearance.

---

## 5. Modifier Parameters

Every serialized field a modifier exposes to markup or to runtime addressing is a **parameter**: one named, typed value that resolves separately for each range the modifier covers.

Four surfaces address the same parameter, because all four address the one `ParameterDescriptor` the modifier publishes: a markup token (§3), a range state rule (§9), a driver clip (§8.5), and code (§19.6).

### 5.1 The cascade

A parameter resolves per range, weakest stage first:

| Stage | Set by | Scope |
|---|---|---|
| Field | the modifier's inspector value | every range of the modifier |
| Rule default | the source's default parameter (§3.2) | every range that source produces |
| Markup token | the tag's positional slot | one range |
| Owned value | `Own` — code (§19.6), a driver clip (§8.5), a `ParameterRule` (§9) | one range, or every range a query matches |

A stage that carries nothing falls through to the one below: an absent slot, an unparsable token and a bare empty slot (`<pspace=,4>`) all leave the value where the weaker stage put it. `""` and `''` are set empty strings, not absent.

Owned values do not overwrite the cascade — they compose on top of its result and are released independently, so a range returns to its authored value the moment the owner lets go.

Slots are positional and comma-separated in tag order (`<pspace=10,4>`). A subclass's own parameters precede the ones it inherits, so a base-declared parameter sits at a later slot on a subclass's tag than on the base's.

Tokens parse out of the box for `float`, `int`, `bool`, `string`, `Vector2`, `Color32`, `UnitValue`, `UnitVector2`, and any enum — by member name, or by a single character when it is unique among the members. Any other type parses only through a declared parser (§5.3).

### 5.2 Descriptors

A `ParameterDescriptor` is one parameter's stable identity. Each modifier publishes its own in a nested static `Param` class:

```csharp
ColorModifier.Param.Color        // ParameterDescriptor<ColorModifier, Color32>
WaveModifier.Param.Amplitude     // ParameterDescriptor<WaveModifier, float>
RevealModifier.Param.Front       // ParameterDescriptor<RevealModifier, UnitValue>
```

| Member | Reports |
|---|---|
| `Id` | the backing field name — what serialized bindings persist |
| `DisplayName` | the editor label |
| `Slot` | the positional markup slot, or −1 for a parameter with no markup presence |
| `SlotOn(modifier)` | the slot on a concrete subclass, where own parameters come first |
| `ValueType`, `ModifierType` | the closed types |
| `SupportedCompositions` | which `ParameterComposition` operations owned values may use |

The typed descriptor does the work: `Resolve(modifier, in context)` returns the full cascade including owned values, `ResolveCascade` stops below them, `ReadRoot` / `SetRoot` read and write the field itself, and `Lerp` interpolates under the value type's contract.

`Param.All` is a modifier's full set, and `ParameterDescriptor.Find(modifier, id)` resolves one by `Id` where the modifier type is not known statically.

**Composition.** `ParameterComposition` is how an owned value combines with the cascade result: `Replace`, `Add`, `Multiply`, or the descriptor's declared `Custom` operation. What a parameter supports follows its value type — `float`, `int` and `Vector2` add and multiply, `Color32` multiplies, `UnitValue` and `UnitVector2` add (mixed units cannot combine, and the owned value wins whole), enums, `bool` and `string` replace only.

### 5.3 Declaring parameters

```csharp
[Serializable]
[GenerateParameters]
public partial class WaveModifier : GlyphParamModifier<WaveModifier.Params>
{
    /// <summary>Peak vertical offset in pixels.</summary>
    [SerializeField, Parameter, StateProperty(nameof(MarkParamsDirty))]
    private float amplitude = 3f;
}
```

| Attribute | Declares |
|---|---|
| `[GenerateParameters]` | the type opts in; it must be `partial` |
| `[Parameter]` | a parameter occupying the next positional markup slot, in declaration order |
| `[SlotlessParameter]` | a parameter with cascade, ownership and rules but no markup slot |
| `[ParameterContainer]` | flattens a `[SerializeReference]` object's own `[Parameter]` fields into the owner's schema; it must name `Invalidate`, and containers do not nest |

`[GenerateParameters]` emits the nested `Param` class, the aggregated `Param.All` set (inherited members first) and the `Descriptors` override, at compile time — the generator ships prebuilt as `Analyzers/LightSide.UniText.ParamCodeGen.dll`, source in `tools~/ParamCodeGen`. Authoring mistakes are compiler errors: a type that is not `partial` (UTP001), a marked type with no parameter fields (UTP002), a container naming no invalidation (UTP003).

A parameter field is state-tracked: it carries `[StateProperty]` (or another state attribute) beside `[Parameter]`, and that is also its invalidation — a write through any stage of the cascade raises exactly what a write to the field raises.

`[Parameter]` carries three options. `Parser` names a static `bool (ReadOnlySpan<char>, out T)` method giving the parameter its own token vocabulary. `Invalidate` names an instance parameterless method to raise instead of the field's own notification. `Descriptor = false` keeps the markup slot and the editor schema while declaring no descriptor — no cascade, no ownership, no driving.

A parameter whose root is not a plain field declares its descriptor by hand with `ParameterDescriptor.From`, `FromEnum` or `Custom`; `WithParser` and `WithCustom` refine one.

Four further attributes shape only the inspector: `[Unit("%|abs")]` lists the unit choices of a `UnitValue` / `UnitVector2`, `[Options("@paints")]` fills a `string` parameter's dropdown from a registered provider, `[Variant]` renders a discriminated union, and `[VisibleWhen]` hides a field until another parameter holds a given value. A provider whose catalog lives outside Unity's object graph calls `ParameterProviders.Invalidate` to make open inspectors rebuild their dropdowns.

---

## 6. The Paint System

One model drives every coloured surface: fills, strokes, shadows, glows, inner shadows, decorations and selection.

### 6.1 What a paint is

A **`TextPaint`** is the resolved runtime paint — a solid colour, a gradient or a texture, plus how it projects and composites. It is resolved per application and is never serialized. The authored form is **`Paint`** (source, projection, blend); a **`PaintSwatch`** is a named `Paint` plus its `PaintMapping`, held in a catalogue.

A layer's authored choice is a **`PaintRef`**: an inline colour, a named swatch, or the layer default (`PaintRefKind`).

### 6.2 Where swatches live

Named swatches come from an **`IPaintProvider`**:

| Provider | Use |
|---|---|
| `InlinePaintProvider` | swatches edited directly on the modifier |
| `AssetPaintProvider` | a specific `UniTextPaints` asset |
| `GlobalSettingsPaintProvider` | the project-wide asset in `UniTextSettings.Paints` |

Any modifier implementing `IHasPaintProvider` shows the swatch dropdown in its inspector.

### 6.3 Projection

Four settings decide how a gradient or texture spreads over text:

- **`PaintMapping`** — the frame the paint is measured against: the glyph, the range, the line, the whole block.
- **`PaintFit`** — how the source's own aspect fills that frame: `Stretch`, `Contain`, `Cover`, `Tile`. Textures take their aspect from the sampled pixels; `Radial` and `Angular` gradients are square sources, so anything but `Stretch` keeps a ring circular on a non-square frame. `Linear` ignores it; `Tile` makes `scale` the repeat count.
- **`PaintProjectionKind`** — `Linear` (project onto an axis), `Radial` (distance from centre), `Angular` (conic sweep).
- **`PaintSpread`** — how a gradient continues past the frame: `Clamp` holds the end stops, `Repeat` restarts the ramp, `Mirror` restarts it reversed so adjacent periods meet without a step. Inert for `Angular`, whose sweep already spans one period.

The frame choice is what makes a gradient run across a whole sentence instead of restarting per glyph.

Every projection value resolves through one chain, weakest to strongest: swatch → modifier field → default parameters (§3.2) → tag attribute. `Inherit` on `PaintMapping`, `PaintProjectionKind`, `PaintFit`, `PaintSpread` and `LayerBlend`, and `NaN` on `angle`, `scale` or either `offset` axis, mean no value at that layer. A chain that resolves to nothing falls back to `Block`, `Linear`, `Stretch`, `Clamp` and `Normal`; a non-positive `scale` becomes 1.

### 6.4 Compositing

Layers composite in painter order. `PaintOrder` selects **layer-major** (each layer across all glyphs, then the next — the component default, cheapest) or **glyph-major** (every layer of one glyph, then the next glyph — correct when layers of adjacent glyphs overlap). Glyph-major covers only same-material quads of the base mesh; texture paints and the colour-glyph segment always stack layer-major. `LayerBlend` sets how a resolved paint composites — `Normal`, `Multiply`, `Screen`, `Additive`, `Exclusion` — and its `Inherit` member keeps the mode the swatch authored.

A decoration line is drawn as virtual glyphs, so the same layers reach it — and by default each of those layers stacks at its own position, which leaves the line's stroke under every glyph face. The `overlay` parameter of `UnderlineModifier` / `StrikethroughModifier` raises the line's whole stack — face, stroke, shadow, glow — above the text it crosses, so the line reads as a separate object with its own outline instead of merging into the glyphs.

---

## 7. Range Decorations

`BaseRangeDecorationModifier` is the base for modifier-authored geometry attached to visual ranges — `HighlightModifier` and your own. It owns range accumulation, paint resolution, renderer handles and rebuild timing; a subclass only turns one logical range into figures. Underlines and strikethroughs take the other path: `BaseLineModifier` emits virtual glyph quads through the mesh pipeline, so per-glyph modifiers reach them like any face glyph.

`GeometryMapping` chooses how a logical range becomes visual figures: `Glyph` (one rounded figure per shaped cluster), `Line` (one per line/BiDi fragment), `Range` (fragments rounded only at the logical endpoints — the default), `Block` (one connected multi-line contour with no internal seams). `RangePaintMapping` picks the paint frame independently of that topology: `InheritGeometry`, `Glyph`, `Line`, `Range`, `TextBlock`. `RangeDecorationCorners` masks which corners round. `RangeDecorationOrder` places a decoration `Behind` or `Above` the text.

Live text selection uses this same path — `HighlightPresentation` is shared between authored highlights and the selection highlight, so styling one teaches you the other.

---

## 8. Animation

Two systems — continuous phase-driven motion and reveal — plus the `UniTextDriver` sequencer that ramps any modifier parameter of either over a timeline.

### 8.1 Phase-driven modifiers

Every animated glyph modifier renders from an external input parameter — `Phase`, or `Roll` on `RollingModifier`: its visual state is a pure function of that input. The modifier never advances time itself. That means the same input always renders the same frame — scrubbing, rewinding and deterministic tests all work. The input is a *slotless* parameter — it takes no positional markup slot, but it cascades and can be owned per range like any other (§5.1).

Feed it from whatever owns time:

- **`UniTextDriver`** — a sequencer component on the text's GameObject. Its clips ramp any modifier parameter, `Phase` included, over a shared timeline (§8.5).
- A tween library, Timeline, or your own code.
- A Unity Animator through `UniTextAnimationBridge` + `ModifierFieldsAnimationHandler` (§8.4), or by animating `UniTextDriver.Progress`.

Built-ins — all `GlyphParamModifier<Params>` subclasses except `GlitchModifier` (an `EffectModifier`) and `RollingModifier` and `ScrambleModifier` (`BaseModifier`s):

| Modifier | Motion |
|---|---|
| `WaveModifier` | vertical sine travelling along the text |
| `ShakeModifier` | deterministic per-glyph jitter, re-rolled `rate` times per phase unit |
| `SpinModifier` | continuous rotation about the glyph centre |
| `PulseModifier` | scale pulsation about the centre |
| `WobbleModifier` | jelly rocking |
| `BounceModifier` | periodic hops off the baseline |
| `FloatModifier` | slow two-axis drift on smooth noise |
| `PendulumModifier` | swing about the top edge |
| `GlitchModifier` | RGB-split glitch bursts |
| `RollingModifier` | characters roll on a cyclic glyph wheel; driven by `Roll` toward 0, not `Phase` |
| `ScrambleModifier` | decode effect settling left to right; driven by `Progress`, with `Phase` churning the random picks |

`spread` decorrelates neighbouring glyphs; `frequency` sets cycles per phase unit. `ShakeModifier`, `GlitchModifier` and `ScrambleModifier` carry neither — their `rate` sets re-rolls per phase unit; `RollingModifier`'s `spread` is roll reduction per character, so later characters settle later.

### 8.2 Writing your own

Subclass `GlyphParamModifier<TParams>`: mark each serialized field `[Parameter]`, tag the class `[GenerateParameters]`, and resolve the range's values through the cascade in `ResolveParams(in RangeApplyContext context)` — one `Param.<Name>.Resolve(this, in context)` call per field. Override `AttributeKey` with a string unique to the modifier type (the built-ins use `AttributeKeys`). Then transform each glyph in `OnGlyph(UniTextMeshGenerator gen, int cluster, in TParams p, float phase)` through `GlyphQuad` (four vertices, order BL-TL-TR-BR). Keep it a pure function of the supplied phase and worker-thread safe.

### 8.3 Reveal

`RevealModifier` shows only the leading part of each covered range — the engine behind typewriter text.

```csharp
text.GetModifier<RevealModifier>().Front = UnitValue.Percent(100f * elapsed / duration);
```

- `Front` (`UnitValue`, default `100%`) — the reveal frontier: a percentage of the frontier axis, or an absolute position in grapheme clusters. Fractional values blend the frontier cluster; positions beyond the length show everything.
- Authorable in markup: `<reveal=fade,50%>` — the first tag parameter names the appearance entry, the second sets `Front`.
- `Collapse` — `false` keeps hidden text's space (CSS `visibility: hidden`), `true` reflows as if absent (`display: none`).
- Reveals whole grapheme clusters in logical order; line breaks are never hidden.
- `PerRange` — `false` (the default) runs one shared frontier over the union of covered clusters in text order, so covered ranges reveal one after another; `true` fills every range independently and simultaneously.
- A cluster covered by overlapping ranges belongs to the innermost one — it alone governs that cluster's visibility and appearance effect.
- `Clock` (`PlaybackClock`, default `Unscaled`) — the time source appear and hide effects advance on. `Manual` advances only through `AdvanceTime(float)`; a clock change mid-flight restarts running phases.

**Appearance** is a separate concern: a `RevealHandler` decides how each glyph arrives. Built-ins include `Fade`, `Slide`, `Scale`, `Spin`, `Flip`, `Skew`, `Stretch`, `Spiral`, `Pop`, `Drop`, `Rain`, `Burst`, `Domino`, `Swing`, `Wave`, `Shake`, `Glitch`, `Chaos`, `Tint`. `CompositeRevealHandler` runs several in one slot; `EasedRevealHandler` is the base for handlers that remap `Progress` through an `Ease` — a built-in curve, a cubic Bézier, or an authored keyed curve.

Handlers are named entries in a catalogue (`RevealHandlerEntry`), resolved per range by the tag parameter, from `InlineRevealHandlerProvider` (edited on the modifier) or `AssetRevealHandlerProvider` / `UniTextRevealHandlers` (a shared project asset). The modifier's `HandlerName` parameter is the default for ranges whose tag carries no parameter; an empty name selects the provider's unnamed entry. Each handler's `Duration` (seconds, default `0.25`) is how long its effect plays for one glyph; `0` makes the change instant.

A receding frontier animates too. The cluster keeps its place in the mesh — and, under `Collapse`, in layout — until its hide effect finishes. Each entry's optional `HideHandler` picks that effect; left unset, the entry's own `Handler` replays backwards. Interrupting one direction with the other continues from the glyph's current state instead of restarting.

A custom handler transforms only the quad handed to `Apply` (`RevealGlyphInfo`), must be worker-thread safe, and **must** resolve to identity at `Progress = 1` — that single rule is what lets every effect serve both directions, since `Progress` is the settled glyph at 1 either way. A handler that wraps others calls `info.WithProgress(t)` to give each child its own remapped timeline, the way `CompositeRevealHandler` does. Effects that turn the quad around a fixed point derive from `GeometricRevealHandler`, which owns the authored `Pivot`; an effect that moves a whole word or line as one body declares `SupportedUnits` and reads `info.unit`.

### 8.4 Animator integration

A Unity Animator writes serialized fields directly, bypassing the property setters that raise `UniTextDirty`. `UniTextAnimationBridge` fixes that: each `AnimationHandler` on it diffs one unit after the Animator writes and converts changes into correct invalidation.

- `UniTextFieldsAnimationHandler` — the component's own fields (size, colour, wrap, auto-size, alignment). `UniTextWorldFieldsAnimationHandler` adds sorting and shadow casting.
- `ModifierFieldsAnimationHandler` — every state field of every modifier in the host's styles, rebound when the style graph changes. It covers the component's own `Styles` only; a modifier living in a `StylePreset` or the project-wide preset is not reached.

Per-range parameter values are not diffable this way — drive them through `UniTextDriver` (§8.5) or parameter ownership (§19.6) instead.

### 8.5 The sequencer

`UniTextDriver` animates parameters (§5) without code: each clip owns one parameter of one modifier across the ranges its query matches and ramps every match between two endpoint values inside its own window on a shared timeline. Add it to the text's own GameObject — `Add Component → UniText → UniText Driver`; it requires a `UniTextBase` there.

Clips are authored in the inspector, or on the timeline its **Open Timeline** button raises: clips sit on tracks, drag to move and resize, split at the playhead, duplicate, copy, paste, mute and marquee-select, with a scrubbable ruler, snapping, zoom and pan. Double-clicking a clip opens its full editor in place; a multi-clip selection edits the shared fields together.

Per clip:

| Setting | Meaning |
|---|---|
| `Start` | the second on the driver's timeline the clip's window opens |
| `Query` | the `RangeQueryDefinition` (§19.6) selecting the ranges this clip drives; its filters follow text edits |
| `From`, `To` | the endpoint values, both `RuleValue`s of the parameter's own type (§9) |
| `Composition` | how the driven value combines with each range's cascade (§5.2) |
| `Priority` | against other owners of the same parameter |
| `MemberDuration` | seconds one member's ramp lasts |
| `Stagger` | seconds between the starts of adjacent members' ramps, in text order |
| `Easing` | the `Ease` the ramp is remapped through |

A clip lasts `MemberDuration + Stagger × (members − 1)`, so its length follows how many ranges its query currently matches.

Outside its window a clip holds its boundary value — `From` before the start, `To` after the end. A `Replace` clip holds only until the playhead passes the start of another `Replace` clip of the same parameter and priority; the latest started one drives. `Add`, `Multiply` and `Custom` clips compose alongside and stay engaged throughout.

Per driver:

| Setting | Meaning |
|---|---|
| Clock | `PlaybackClock` — `Scaled`, `Unscaled`, or `Manual`, which advances only through `Advance(deltaTime)` |
| Cycles | how many times the timeline plays; `-1` repeats until stopped |
| Repeat | `MoveItCycle.Restart` replays from the beginning, `PingPong` runs the timeline back the way it came |
| Duration | fixed timeline length in seconds; clips still extend it, and 0 derives the length from the clips alone |
| Play On Enable | starts playback when the component enables, in play mode only |

`Speed` scales the playhead and runs it backwards when negative. `Progress` is the normalized playhead, 0–1: setting it renders that exact state, so an Animator, a Timeline track or a scrub bar drives the whole sequence through one float. `Playhead` is the same position in seconds — a `PingPong` return reads as the mirrored position, never as a phase past the end — and `TimelineLength` reports the resolved length. `Play()`, `Pause()`, `Stop()`, `Seek(normalized)` and `Advance(seconds)` drive it; `Rebind()` rebuilds every clip's ownership against the current styles, which a style change does on its own.

Ownership follows each clip's query across text edits and is released on disable, returning every range to its cascade. Scrubbing and playback both work in edit mode.

---

## 9. Range State Rules

A reactive layer that drives **any modifier parameter** from **any signal**, per range, without code.

The pieces:

- **`RangeSignal`** — a value a range emits: hover, press, focus, a scalar you publish (`BuiltInScalarSignal`, `RangeSignals`). A project declares its own with `new RangeSignal<T>(id)` — the `unitext.` id namespace is reserved — and matches it from code with `RangeSignalSelector<T>`.
- **`RangeStateSelector`** — when the rule applies. Compose with `AllRangeStateSelector`, `AnyRangeStateSelector`, `NotRangeStateSelector`, `InteractionRangeStateSelector`, `ScalarRangeStateSelector`.
- **`RangeStatePlayback`** — how the contribution moves over time: `TransitionPlayback` (enter/exit duration, `Ease`, `PlaybackClock`), `InstantPlayback`, `ManualPlayback`, `SignalProgressPlayback`.
- **`ParameterDescriptor` / `OwnedParameter<TValue>`** — the target. Every `[Parameter]` field of a modifier is published as a static descriptor on that modifier's generated `Param` class (§5.2), which is what `ParameterRule.SetTarget` binds to. Writing an `OwnedParameter` changes only that ownership; it never mutates the serialized modifier field.
- **`RuleValue`** — typed targets: `ColorRuleValue`, `FloatRuleValue`, `UnitRuleValue`, `Vector2RuleValue`, `UnitVector2RuleValue`, `IntRuleValue`, `BoolRuleValue`, `StringRuleValue`, `EnumRuleValue<TEnum>`. Each supplies either the value authored on it or, with `RangeValueSource.PayloadMember`, a named member read from the range's payload. `RuleValue<TValue>` is the public base for a project's own unmanaged value type.

`RangeStateRule` wires them together; `ModifierRule` (applies a transient modifier graph while active) and `ParameterRule` (drives one parameter of another modifier in the same graph) are the concrete shapes. Rules are authored on `InteractiveModifier.Rules`; `UniTextRanges` is the per-component runtime that owns their playbacks, reached with `UniTextRanges.For(text)`.

Every rule also carries `Scope` — `RangeRuleScope.Entity` by default, one playback writing to every segment of the entity, or `Segment` for an independent playback per segment — an optional one-shot `Trigger` (`RangeRuleEvent.Activated` or `ContextRequested`) that fires independently of the selector, and a `Priority` deciding which `Replace` contribution wins. A `ParameterRule` adds `Composition` (`Replace`, `Add`, `Multiply`, `Custom`), deciding how its contribution combines with the cascade result and with concurrent rules. The default selector is `InteractionRangeStateSelector` requiring `Hovered`; the default playback is `InstantPlayback`.

From code, `UniTextRanges.For(text)` publishes signals — `SetSignal(entity, RangeSignals.Selected, true)`, the segment-scoped and `RangeChannel` overloads, and `SetSignalForAll` — advances playbacks configured with `PlaybackClock.Manual` through `AdvanceManual(deltaTime)`, and raises `RuleEntered`, `RuleExited`, `RuleUpdated` and `RuleTriggered`, each carrying the `RangeRuleInstance`. `Own` takes one parameter of a materialized range into ownership until the handle is released or the range's identity retires with a text edit (§19.6). `PrefersReducedMotion` collapses every decorative playback to its final value.

`Playback` is the weight envelope every playback runs on: instant and eased transitions, pulses, deferred release and clock routing, allocation-free. A playback of your own drives it through a host implementing `IPlaybackHost` — `ApplyWeight` for the current weight, `ReleaseOutputs` after a releasing run completes.

The practical result: "links glow on hover, animated over 120 ms, and the glow is the link's own colour" is configuration, not a script.

---

## 10. Interaction

### 10.1 Interactive ranges

`InteractiveModifier` makes a range respond to pointer input. Input, geometry, overlap and pointer state belong to one per-component `UniTextInteractions` router the modifier registers with; the modifier owns only authored policy, events and default actions. Overlapping ranges resolve to a single target: highest `InteractionPriority` first, then Style order, then the shorter range, then registration order. A range with `PassThrough` set emits its events without consuming the pointer, so underlying UI still receives the gesture.

`RangeInteraction` is the borrowed event context routed through capture, target and bubble — copy any value needed after the callback returns, because the router reuses the instance. `RangeState` is the per-range machine `Normal → Hovered → Pressed`, plus `Disabled`; read it with `InteractiveModifier.GetRangeState`. The router publishes the same changes as typed signals — `RangeSignals.Hovered`, `Pressed`, `Focused`, `Disabled` — and those, not `RangeState`, are what §9 selectors match.

Code subscribes through the same router: `UniTextInteractions.For(text).Get(channel)` returns the `RangeInteractionChannel` for one `RangeChannel` asset, with `Activated`, `ContextRequested`, `Entered`, `Exited`, `StateChanged`, `LongPressProgress`, `Gesture`, `FocusChanged` and a catch-all `Interaction`. A modifier with no channel of its own inherits the range source's; with neither, routing is modifier-local through `InteractiveModifier.Interaction`.

### 10.2 Actions

A range can carry serialized actions instead of code:

| Action | Effect |
|---|---|
| `OpenUrlAction` | opens the range's URL |
| `CopyRangeTextAction` | copies the range text |
| `SetActiveAction` | sets a GameObject active or inactive |

`RangeAction` is the base — subclass it for your own. `RangeActionContext` carries the text component, the entity, the hit segment, the payload and the pointer data, and stays valid after dispatch returns. Each action declares which routed events run it through `RangeActionEvents` (`Activated`, `ContextRequested`, default `Activated`) and returns `RangeActionFlow` to continue or stop the rest of the list. Actions run in Inspector order after capture, target and bubble handlers, and are skipped when a handler calls `RangeInteraction.PreventDefault`.

### 10.3 Gestures

`RangeGestureRecognizer` and `DragRangeGestureRecognizer` turn raw pointer streams into range gestures. `RangeGestureCompatibility` decides which recognizers may run together.

### 10.4 Viewports and world text

Scrolling and camera setups need to know where the text actually is on screen:

- `ScrollRectRangeViewport` — inside a `ScrollRect`.
- `CameraRangeViewport` — world-space text seen by a camera.
- `RangeViewportAdapter` — write your own.

For world text, add `UniTextWorldRaycaster` to the camera so Unity's EventSystem can hit `UniTextWorld`.

### 10.5 Links

`LinkModifier` + `MarkdownLinkParseRule` + `RawUrlParseRule` cover the usual set: explicit `<link=url>`, Markdown `[text](url)`, and bare URLs. `LinkModifier` opens the resolved URL through `Application.OpenURL` while `AutoOpenUrl` is set, and raises `LinkClicked`, `LinkEntered` and `LinkExited`; use `OpenUrlAction` instead when the URL comes from a payload member or a literal. It carries no colour or underline of its own — compose it with `ColorModifier` (§4.1) and `UnderlineModifier` (§4.4) — and ships two `ModifierRule` entries for pressed and activation feedback; add a §9 rule for hover feedback.

### 10.6 Text resolver

`IUniTextResolver` overrides the source text of a component before it is parsed — without touching the serialized field. This is the localization hook: the scene stores a key, the resolver substitutes the translation, and `TextOverrideSource` reports why the rendered text differs from what is stored.

---

## 11. Inline Media

`<obj=name>` and `<sprite=name>` place an object or sprite in the text flow. Each inserts one codepoint — U+FFFC OBJECT REPLACEMENT CHARACTER — so glyph advance, line breaking, baseline alignment and every codepoint index treat it as a character. Both run on `InlineTagRule` (§3.3): the `/>` shorthand (`<obj=name/>`) is accepted but not required, and a stray `</obj>` is stripped.

Arguments are positional and comma-separated after the name:

```
<obj=name[,size][,offset][,advance][,lineHeightAbove][,lineHeightBelow][,pivot][,rotation]>
<sprite=name[,color][,aspect][,size][,offset][,advance][,lineHeightAbove][,lineHeightBelow][,pivot][,rotation]>
```

Size, offset, advance and line height are em units (1 = font size); pivot is normalized; rotation is degrees. `color` is empty for the entry's own colour, `i` to inherit the component's colour (CSS `currentColor`), or `#RGB` / `#RRGGBB` / `#RRGGBBAA` / a named colour; `aspect` is `true` / `false`. Values resolve weakest → strongest: provider entry → keyed override → default parameter (§3.2) → tag argument.

- `ObjModifier` / `SpriteModifier` — the modifiers.
- `IObjProvider` / `ISpriteProvider` — name → entry resolution, from `InlineObjProvider` / `InlineSpriteProvider` (inline lists) or `AssetObjProvider` / `AssetSpriteProvider` (`UniTextObjects` / `UniTextSprites` assets).
- `MediaWrapper` — how the entry is presented: `PrefabMediaWrapper` instantiates a prefab, `SpriteImageWrapper` draws a sprite.
- `InlineObjectOverride` / `InlineSpriteOverride` — rows on the modifier's `Overrides` list, each matched to one provider entry by `Key`: size, bearing offset, advance, line height above and below (em), pivot (normalized), rotation (degrees). `InlineSpriteOverride` adds colour and preserve-aspect. Each field has an unset state — NaN, `InheritBool.Inherit`, `SpriteColorSource.Original` — that falls back to the provider entry.

`InlineObjectPolicy` on `InteractiveModifier` (§10) decides whether the U+FFFC clusters of inline media take part in an interactive range's hit geometry: `Include` (default), `Exclude`, `Only`.

---

## 12. Selection

`UniTextSelectable` adds read-only selection to Canvas or world text. `GameObject → UI (Canvas) → UniText → Selectable Text` instantiates the prefab assigned in `UniTextSettings`, already wired with handles and a context menu; `Add Component → UniText → Selectable` adds the component to an existing text object. The text component must be there first — `RequireComponent` validates `UniTextBase` but cannot auto-add an abstract type. One per GameObject.

### 12.1 What the user gets

Click places a caret; double-click selects a word (and a drag continuing from it extends by whole words); triple-click selects a paragraph; drag selects; Shift extends; right-click or long-press opens the context menu, promoting a collapsed caret to the word under the pointer and leaving an existing selection intact; Copy and Select-All work from the keyboard while focused. One selection per document, with EventSystem-driven defocus.

### 12.2 The selection model

`TextSelection` is anchor / focus / affinity over codepoint indices:

- **`Anchor`** — where the selection began.
- **`Focus`** — the caret, always where it is rendered.
- **`Affinity`** (`CaretAffinity`) — which visual side the caret takes at an ambiguous boundary: a soft-wrap break (end of line N vs start of N+1) or a BiDi run boundary. Codepoint indices alone cannot disambiguate these, so without affinity the caret position is undefined there.

### 12.3 Code

```csharp
selectable.SetCaret(index);
selectable.SetSelection(anchor, focus);
selectable.ExtendSelection(focus);
selectable.DragSelectionHandle(draggingAnchor, index);
selectable.SelectWord(index);
selectable.SelectLine(index);
selectable.SelectParagraph(index);
selectable.SelectAll();
selectable.ClearSelection();
var s = selectable.GetSelectedText();
```

Mutators return `bool` — `false` means the selection did not change: the request resolved to the state already held, or a `SelectionChanging` subscriber vetoed it. Out-of-range indices are clamped to `[0, codepointCount]`, never rejected. `DragSelectionHandle` never collapses the selection — an endpoint dragged onto the fixed one is clamped a grapheme cluster away, and dragging past it swaps the handles' roles.

Events:

- `SelectionChanging` — vetoable and ordered: `selectable.SelectionChanging.Subscribe(handler, order)`, lower orders first. Inspect `Proposed`, set `Cancel` to block, or assign `Proposed` to clamp the change into a permitted range. Selection moves caused by a text mutation (insert, delete, IME commit, undo) bypass it.
- `SelectionChanged` — carries previous and current state plus a hierarchical `UserEvent` string (`SelectionChangeReason`, CodeMirror 6 convention). Match a family with `reason.StartsWith("select.")`.

`SelectionHitTest` exposes the line/codepoint navigation helpers the caret path uses. `SelectionHighlight` styles the live highlight — the same `HighlightPresentation` authored highlights use (§7); `RefreshHighlight()` re-bakes its rects after an out-of-band reposition.

### 12.4 Ranges that survive edits

`MutableRangeSource` maintains ranges at runtime and patches their indices through every edit, so a highlight stays on the word it marked even as the user types before it. Under the default `RangeTracking.Content`, a range whose span the edit removed entirely is dropped.

```csharp
using var update = source.BeginUpdate(source.Snapshot);
var id = update.Add(new TextRange(start, length));
update.Commit();

source.SetRanges(source.Snapshot.Revision, ranges);   // replace everything in one notification
```

Every update targets one `TextRevision`: `BeginUpdate` and `Commit` throw when the text moved on, or when the source is not bound to a component with a completed parse.

### 12.5 Context menu

The menu is **your scene UI**. `ContextMenuItem` binds a control you built (a Button, a Toggle) to an action; the menu wires the event and shows or hides the control by `IsApplicable`. Items carry no visuals.

Built-in items: `CopyContextMenuItem`, `CutContextMenuItem`, `PasteContextMenuItem`, `SelectAllContextMenuItem`, plus `ActionContextMenuItem` (a Button raising `Invoked`) and `ToggleContextMenuItem` (a Toggle raising `ValueChanged`); both hide on a bare caret when their Only With Selection flag is set. `CommandContextMenuItem` / `ButtonContextMenuItem` are the bases. `ContextMenuCapabilities` reports which standard actions apply right now — `CanCut`, `CanCopy`, `CanPaste`, `CanSelectAll`, `HasSelection`.

`PrefabTextContextMenu` presents a Unity-UI menu from a prefab shared through the touch overlay; with the slot empty it presents nothing. `Defaults/Editing/ContextMenu.prefab` is the shipped menu, assigned in the shipped `Selectable Text` prefab. Implement `ITextContextMenu` directly for a native or bespoke menu.

An item whose control is not assigned never counts as applicable — an unwired menu cannot open as an invisible input-blocking panel.

### 12.6 Touch affordances

The entity slots live on `UniTextSelectable`, but a sibling `UniTextEditable` (§13) shows and drives them — read-only selection alone presents no handles and no magnifier. The context menu (§12.5) is the one touch affordance `UniTextSelectable` presents by itself.

- `ISelectionHandles` — two draggable endpoints.
- `IInsertionHandle` — a single handle under a collapsed caret.
- `IMagnifier` — the loupe shown during long-press placement and handle dragging.

`PrefabSelectionHandles` and `PrefabMagnifier` present Unity-UI entities from a prefab shared through the touch overlay; an entity whose prefab slot is empty presents nothing. `Defaults/Editing/SelectionHandles.prefab` is the shipped handles prefab, assigned in the shipped `Selectable Text` prefab. No loupe prefab ships — build one from `Add Component → UniText → Magnifier` (`UniTextMagnifier`) and assign it; it captures through a canvas camera, so it stays hidden on a Screen Space - Overlay canvas, which has none. `SelectableEntity` is the base for your own. Each capability is independent — an entity may implement either or both (`ITouchHandles`).

---

## 13. Editing

`GameObject → UI (Canvas) → UniText → Editable Text` and `GameObject → UI (Canvas) → UniText → Input Field` add an editable field to a Canvas. Both instantiate the prefab assigned in `UniTextSettings`, so a project's own prefab is what appears; with an empty slot the menu item creates nothing and warns.

`UniTextEditable` is a sibling of `UniTextSelectable` on the same GameObject. It implements `ITextDocument` (read-only codepoint-indexed view for generic consumers — find/replace, validators, accessibility readers) and `ISavedStateProvider`.

It tracks its size through the text component's `ILayoutElement`: add a `ContentSizeFitter`, or place it under a layout group. Put it directly under a `RectMask2D` to make that parent the clipping viewport and enable internal scrolling when content overflows. The rest of the field chrome — background, placeholder, labels — is assembled from ordinary Unity layout components plus decorators, not a fixed component.

All OS input arrives independently of Unity's input system, so editing works whatever the project's Active Input Handling is set to (§16).

```csharp
editable.Text = "value";              // serialized source; preserved byte-for-byte until an edit
var visible = editable.VisibleText;   // the same document without markup
editable.InsertText("abc");
editable.DeletePrevious();
editable.DeleteWordNext();
editable.Copy(); editable.Cut(); editable.Paste();
editable.PastePlain();                // paste without formatting
editable.Undo(); editable.Redo();
editable.SelectAll();
editable.Activate(); editable.Deactivate();
```

`ReadOnly` keeps selection and copy while refusing every mutation.

### 13.1 Events

```csharp
editable.TextChanged      += () => { };
editable.ValueChanged     += value => { };
editable.DocumentChanged  += reason => { };   // TextChangeReason: input.type, input.paste, program.set, input.restore
editable.Submitted        += value => { };
editable.Cancelled        += () => { };
editable.Focused          += () => { };
editable.Defocused        += () => { };
editable.SelectionChanged += (anchor, focus) => { };
editable.EditApplied      += shape => { };
editable.CompositionStateChanged     += composing => { };
editable.TouchKeyboardVisibilityChanged += visible => { };
editable.CaretContextChanged            += context => { };
editable.ValidationChanged              += state => { };
```

`EditShape` describes one mutation — removed codepoints replaced by inserted codepoints at a start index — so consumers patch derived state instead of recomputing it.

### 13.2 Behaviors

Policy lives in **`InputBehavior`** objects, not in the component. A behavior subscribes to the hooks it needs in `OnEnable` and unsubscribes in `OnDisable`. The base carries no policy; every specific lives in a subclass. This mirrors `BaseModifier`, adapted to the editing pipeline.

An editor holds two slots: `Behaviors`, the local list in hook order, and `BehaviorPresets`, shared preset assets applied after it.

Built-ins:

| Behavior | Effect |
|---|---|
| `PasswordBehavior` | single-line masked field: every codepoint renders as `MaskChar`, line breaks are rejected, copy/cut are blocked unless `AllowCopy`, native input is secure; `Revealed` drives a show-password toggle |
| `SingleLineBehavior` | web `<input>` semantics: Enter submits, newlines are stripped from typing, paste and IME commit (multi-line pastes are joined), and submit releases focus unless `KeepFocusOnSubmit` |
| `LengthLimitBehavior` | caps length, in the chosen `TextLengthUnit` |
| `InputMaskBehavior` | live pattern formatting — `(###) ###-####`, `##/##/####` |
| `CaseTransformBehavior` | forces upper / lower / title case as text is entered |
| `SelectAllOnFocusBehavior` | selects everything on focus |
| `DefocusOnCancelBehavior` | releases focus on Escape / system back |
| `SubmitKeyBehavior`, `TabKeyBehavior` | key bindings |
| `TextFormattingBehavior` | style commands over the selection or as a typing style — bold (B), italic (I), underline (U), clear formatting (`` ` ``) — and the field's markup policy: `MarkupVisibility`, `MarkupChrome`, `RichPaste`, `PlainTextPaste`, `TypingMarkup` |
| `CaretContextBehavior` | hosts `CaretContextHandler`s (toolbar state, formatting bubbles) |
| `StripFormatOnPasteBehavior`, `LinkOnPasteBehavior` | paste policy |
| `MediaInputBehavior` | receives pasted/dropped images and files |
| `NativeKeyboardBehavior`, `NativeFieldOverlayBehavior` | soft-keyboard traits and OS overlay |
| `KeyboardAvoidanceBehavior` | lifts the field above the software keyboard |
| `AutoValidateBehavior` | re-runs validators on a chosen trigger (`AutoValidateMode`: `OnValueChanged`, `OnUnfocus`, `OnSubmit`, `Always`) and publishes the result to `Validation` |

**`InputBehaviorPreset`** is a project asset holding a behavior list — one asset defines a field archetype (chat composer, password field, form field) reused across scenes. Each editor instantiates a runtime copy, so per-instance state never leaks back into the asset.

Markup visibility is the field-level policy for authored tags. `UniTextEditable.MarkupVisibility` is `Hidden` (default — tags never show and the caret steps over them as atomic units), `RevealActiveRange` (the tags of the range the caret is inside reveal as editable source), or `Raw` (every tag shows as literal source text and the caret moves through it); `TextFormattingBehavior` authors it. `ChromeRule` entries in `UniTextEditable.MarkupChrome` style the visible tag characters — different modifier kinds compose, same-kind rules resolve by selector specificity, ties to the first listed.

### 13.3 Filters vs validators

Two different jobs:

- **`InputFilterBase`** rejects characters *as they are typed*. Built-ins: `AlphanumericFilter`, `IntegerFilter`, `DecimalFilter`, `EmailFilter`, `NameFilter`. Override `Allows(in EditProposal)` and judge the post-state: replacing a selection is legal whenever the result is legal. Pure deletions never reach it — a character filter cannot block deleting. `PreferredKeyboardType` supplies a mobile keyboard when no `NativeKeyboardBehavior` overrides it.
- **`InputValidatorBase`** lets input through and judges the *whole value*. Override `Validate(ITextDocument)` and return a `ValidationState`; none ship — validators are per-project. `Status` is an open token, empty when valid (`ValidationStatus.Invalid` / `Pending` are the common ones), and `Message` is what a `SupportingTextDecorator` shows. `AutoValidateBehavior` publishes the result to `UniTextEditable.Validation`.

Filters run only on committed text — never on in-progress IME composition, undo/redo replay, or programmatic `Text` writes.

`InputEdit` is the mutation a filter hook sees, passed by ref: reject it, rewrite `text`, or retarget the whole edit (a CodeMirror-style transaction rewrite — this is how a mask reformats the entire field).

### 13.4 Decorators

`FieldDecorator` is a state-driven visual that receives a `FieldState` snapshot whenever the editor's state changes. Built-ins:

| Decorator | Shows |
|---|---|
| `PlaceholderDecorator` | placeholder while empty |
| `FloatingLabelDecorator` | label animating between resting and floated positions |
| `SupportingTextDecorator` | helper / error text |
| `CharacterCounterDecorator` | `count` or `count/limit`, recoloured at the cap |

The counter counts the document **source** — the same space `LengthLimitBehavior` enforces in — so count and cap always agree. In a field with hidden markup that count includes the markup characters, not just the visible text.

Decorators are `InputBehavior`s, so they appear in the same picker and follow the same lifecycle.

### 13.5 Caret

`InputCaretRenderer` draws the caret as a filled rectangle. It is the extension point: subclass it, override `OnPopulateMesh` reading `CaretRect` and `BlinkVisible` for a block cursor, an underline or a gradient, then assign it to `UniTextEditable.CaretRenderer`; with none assigned, one is created on first activation.

The caret toggles every `UniTextSettings.CaretBlinkInterval` seconds and stops blinking, visible, after `UniTextSettings.CaretBlinkTimeout` seconds of inactivity. The static `InputCaretRenderer.PrefersNonBlinkingCaret` suppresses the blink entirely; the integration layer sets it from the platform preference — iOS 17+ "Prefer Non-Blinking Cursor", the macOS Reduce Motion cursor setting — alongside `Accessibility.PrefersReducedMotion`.

### 13.6 Caret context

`CaretContext` reports which modifiers cover the caret — the full set plus what entered and left since the previous state — with the selection it was computed for and the source editor for follow-up queries (`IsStyleActive<T>`, `TryGetStyleParameter<T>`, caret geometry).

This is what drives a formatting toolbar: `CaretContextBehavior` hosts handlers, `StyleStateHandler` lights up buttons. The lists are reused between dispatches — read them during the call, copy what you need to keep.

### 13.7 Soft keyboard

`NativeKeyboardConfig` carries the portable traits: `KeyboardType`, `ReturnKeyType`, `AutoCapitalization`, `AutoCorrection`, `SpellChecking`, `AutofillHint`. iOS-only: `SmartQuotes`, `SmartDashes`, `SmartInsertDelete` (each a `SmartFeatureMode`), `KeyboardAppearance`, `EnablesReturnKeyAutomatically`, `ShowDoneToolbar`, `PasswordRules`. Android-only: `ImeFlags` (`AndroidImeFlags`).

`KeyboardType` deliberately exposes only values that map across iOS, Android and Web, so a value never silently degrades on another platform; an iOS-only type goes through the iOS override field.

`KeyboardRequest` is passed by ref to resolver hooks each time the field raises the keyboard — a behavior fills in traits and overlay styling; unset fields keep OS defaults.

`NativeEditorAction` delivers the keyboard's action key as an open vocabulary (`Submit` / `Next` / `Previous` / `Newline`, with Go/Search/Send/Done all arriving as `Submit`), so a chat composer submits and a form field advances focus. With no subscriber it degrades to a synthesized Return.

---

## 14. Clipboard

Copy and paste are multi-format and lossless within UniText.

### 14.1 Adapters

An **`IClipboardAdapter`** is one format stage. The built-in set below is fixed per field: on copy every adapter contributes its format as one atomic multi-format write; on paste the highest-`Priority` adapter whose format is present wins. A selection whose markup is visible to the user — `Raw`, or a range revealed under `RevealActiveRange` — copies as plain text only, with no semantic channels: what is on screen is what pastes.

| Adapter | Format | Priority |
|---|---|---|
| `UniTextSourceClipboardAdapter` | `application/vnd.lightside.unitext` — visible text + markup spans | 100 |
| `TagHtmlClipboardAdapter` | HTML | 50 |
| `MarkdownClipboardAdapter` | Markdown | 40 |
| `PlainTextClipboardAdapter` | plain text | 0 — the floor |

Copying a styled selection into an email keeps the formatting; copying it back into UniText restores the exact modifiers.

### 14.2 Teaching a modifier to travel

Formats are declared per modifier **type**, not per instance, in `ClipboardModifierBindMap`:

```csharp
ClipboardModifierBindMap.Register<MyModifier>(schema);
```

`ModifierClipboardSchema` carries `ModifierHtmlSchema` (which elements) and `ModifierMarkdownSchema` (which delimiters — `MarkdownSyntaxKind.Wrap` for `**bold**`, `Link` for `[text](url)` where the parameter is the URL). A schema must declare `CanonicalTagName` — the UniText source tag the paste gate matches against the field's styles — or set `MatchesSourceTagName` when the modifier's own tag names are its element names (`sup` / `sub`); a schema with neither is rejected with a warning. Register once per type, on the main thread; a repeat registration of the same type is a no-op. An unregistered modifier's ranges copy as plain text. Built-ins are pre-registered.

Registration is explicit — no assembly scan, nothing for IL2CPP stripping to break, and the first paste costs nothing.

### 14.3 Plain text

Plain text is the one channel whose intent is unknown: a pasted `<b>` might be markup or literal. `UniTextEditable.PlainTextPaste` decides — `Auto` (default) follows `MarkupVisibility`, parsing under `Raw` and inserting literally otherwise, `Literal` always inserts verbatim, `Parse` always reparses with the field's own rules. `UniTextEditable.TypingMarkup` does the same for hand-typed text — `Parse` (default) or `Literal`; formatting commands, programmatic styling, inline objects and paste are unaffected by it.

`UniTextEditable.RichPaste` (default `true`) gates the structured channels entirely: with it off, a paste always inserts the clipboard's plain text and the HTML / Markdown / UniText formats are ignored. `TextFormattingBehavior` carries all three as authored values and applies them to the editor.

### 14.4 Media

`MediaContent` is offered to `MediaReceived` before the text pipeline runs — from a paste, a drag-and-drop, or a picker (`MediaSource`). A handler probes the formats it cares about (image blobs via `GetData`, files via `GetFiles` / `ReadFile`) and sets `Handled` to consume it; left unhandled, a paste falls through to the text channels. `MediaInputBehavior` is the inherit-and-override entry point — this is how a chat composer turns pasted images into attachment cards.

### 14.5 Paste permission

`UniTextPasteControl` is the cross-platform Paste widget. On iOS 16+ it overlays a native `UIPasteControl` on its own `RectTransform`, so a tap pastes without the system paste-permission prompt; elsewhere it drives `UniTextEditable.Paste` through an optional `Button` on itself or a child. Left unassigned, `Target` resolves from the parent hierarchy and `FallbackButton` from itself or a child. `DisplayMode` (`IconAndLabel` / `IconOnly` / `LabelOnly`) and `CornerStyle` (`Capsule` / `Dynamic` / `Fixed` / `Small`) style the native control; `Pasted` fires on either path. A programmatic `TriggerPaste` on iOS 16+ outside a user-action context surfaces the system prompt.

---

## 15. Language and Internationalization

### 15.1 Three places to set the language

1. **Component** — `Language`, the default BCP 47 tag for all text in it (a whole-text `LanguageModifier`).
2. **`<lang=…>`** — per range, BCP 47: `<lang=zh-Hans>汉字</lang>`.
3. **Font family** — a family may declare one BCP 47 tag it prefers (`FontFamily.preferredLanguage`); a run carrying a matching tag prefers that family during codepoint-to-font resolution. Matching is prefix-wise, so `zh` matches `zh-Hans` (`LanguageMatching`).

Language matters beyond fallback: it drives the OpenType `locl` feature, which selects the correct forms for Han unification (Chinese vs Japanese vs Korean shapes of the same codepoint). Case conversion and line breaking are language-independent: `UppercaseModifier` / `LowercaseModifier` use the bundled simple UCD mappings, which ignore locale-conditional rules such as Turkish dotless I, and break opportunities come from each codepoint's script (UAX #14).

### 15.2 Picking fonts by language

Put language-specific families in the stack and name them; `<font=…>` selects one explicitly, and automatic fallback selects one implicitly for characters the primary family lacks.

### 15.3 Localization

`IUniTextResolver` (§10.6) substitutes text at render time. Combined with `<lang>` and a per-language font stack, one component serves every locale without scene changes.

---

## 16. RTL, BiDi and Platform Input

### 16.1 Bidirectional text

The full Unicode Bidirectional Algorithm runs on every paragraph: Arabic, Hebrew, Syriac and Thaana, mixed with Latin, resolve correctly including nested embeddings, mirrored brackets and neutral runs.

The base direction is `TextDirection.Auto` unless a `DirectionModifier` sets it; `Auto` resolves each paragraph from its own first strong character (UAX #9). `DirectionModifier` applies whole-text — its resolved value becomes the base direction of every paragraph, not of one range. Caret movement, selection and hit-testing are all BiDi-aware — this is why `CaretAffinity` exists (§12.2).

Cursive joining is handled by the shaper, so `LetterSpacingModifier` renders tatweel rather than breaking joins.

### 16.2 Native input

On platforms with a native transport (Windows, macOS, iOS, Android, WebGL), `UniTextNativeInput` delivers OS key, text, composition, selection and keyboard-visibility events **independently of Unity's input system** — no `Event.PopEvent`. Editing therefore works whatever Active Input Handling is set to (Legacy, New Input System, or Both), which removes the biggest friction point of every Unity input-field asset.

Control keys arrive only via key-down; printable text arrives only after the OS resolves layout, dead keys and IME. `NativeKeyCode` and `[Flags] NativeModifiers` are cross-platform.

`INativeInputBackend` is the backend seam; `ManagedInputBackend` is the portable fallback for platforms without one — it reads keys and characters from the OS key events Unity queues, the same source Unity's own input fields read, so layout-resolved characters and system key repeat arrive whatever Active Input Handling is set to. `ITextInputContext` is the per-field context the backend talks to.

### 16.3 IME

Composition is first-class: `CompositionStateChanged` reports when a composition is in flight, composition clauses carry their own styling (`CompositionClause`, `CompositionClauseStyle`), and filters never see in-progress composition text.

---

## 17. Emoji

Colour emoji render from the OS emoji font by default — nothing to configure. Flags, ZWJ sequences (family, profession) and skin-tone modifiers form single glyph clusters as the Unicode spec requires.

- `EmojiFont` — the system emoji runtime, resolved from the OS and served without a stack entry (`Instance`, `IsAvailable`, `Disabled`); reserved font id `-1`.
- `SystemEmojiFont` — locates the OS emoji font file per platform (Segoe UI Emoji, Apple Color Emoji, NotoColorEmoji, …).
- `UniTextColorFont` — a colour-font asset (CBDT/sbix bitmap or COLRv0/COLRv1 vector) added to a stack like any other font.
- `ColorFontCore` — the shared colour-font runtime behind `EmojiFont` and `UniTextColorFont`.

Emoji participate in line breaking, selection and editing as single clusters: one arrow-key press crosses a whole family emoji, and one backspace deletes it.

`Project Settings → UniText → Disable Emoji` turns colour emoji off globally (`UniTextSettings.EmojiDisabled`, mirrored to `EmojiFont.Disabled`). A `UniTextColorFont` in the stack ships a colour font of your own; the system emoji font stays the privileged provider for emoji-presentation codepoints.

---

## 18. Custom Materials and Shaders

### 18.1 Using a ready material

Assign a material to `MaterialModifier` and tag a range with `<mat>`. An optional parameter tints it: `<mat=#FF8800>`.

`RenderOrder` decides how the sub-mesh composes with the base text pass: `Replace` (the default) suppresses the base face on the range, so only the custom material shows; `Keep` renders the custom material as its own layer, ordered by the modifier's position in `Styles`. Paint layers (stroke, shadow, glow) stay separate quads either way.

### 18.2 Authoring a shader

UniText ships shader includes so one source compiles for Canvas, world-space, Built-in **and** URP. Effects read the shared coverage and paint channels rather than reimplementing SDF sampling.

`Assets → Create → UniText → Custom Effect` writes three files: the effect include plus its Canvas and World shells, with the package prelude includes already resolved. The visual logic lives in the include — implement `half4 UniTextEffect(UniTextFrag i)` and return a premultiplied colour; the shells are touched only to declare Properties. `UniTextFrag` carries `sdfAlpha`, `signedDist`, `glyphUV`, `atlasUV`, `atlasColor`, `color`, `glyphMeta`, `lineFlow`, `tileId`, `tileHash`, `userA` / `userB` and `positionWS`; `UniText_EffectLib.hlsl` supplies the building blocks. A material bound through `MaterialModifier` must declare `_MainTex` as a `2DArray` — the atlas is bound automatically.

Vertex channels are a contract, and TEXCOORD2 / TEXCOORD3 carry two disjoint streams that never share a quad. On base-mesh quads they hold the coverage and paint contract (`CoverageMode`, written by `CoverageQuadOps`, read with the `UniTextCoverage` helpers). On a `MaterialModifier` sub-mesh they are user channels A and B — filled from `ConstantUv2` / `ConstantUv3`, the `glyphDataWriter` delegate, or an `OnWriteGlyphUV` override — and reach the fragment as `UniTextFrag.userA` / `userB`.

The prelude's own interpolators are fully claimed (`meta.w` carries the packed tile hash), so per-glyph custom data rides the `MaterialModifier` user channels or is produced in the `UNITEXT_EFFECT_VERT` vertex hook. The paint interpolator's fourth channel packs the gradient's `PaintSpread` alongside the paint kind, so a hand-written shader reading it directly must unpack both.

`MaterialModifier` derives from `SubMeshModifier`, the abstract base that gives a range its own sub-mesh, `CanvasRenderer` and material. A range that needs a genuinely different shader needs only `MaterialModifier`; subclass `SubMeshModifier` when the sub-mesh geometry itself must be built differently.

### 18.3 Noise

`Tools → LightSide → Noise Generator` produces noise textures for shader effects.

---

## 19. Text Model and Runtime API

### 19.1 Assigning text

```csharp
text.Text = "value";                 // serialized field
text.SetText(stringBuilder);         // no allocation from a builder
text.SetText(charSpan);              // from a span
```

`Text` is the serialized field. `RawText` is the runtime source before any resolver substitution, `ResolvedText` the resolver's last output, `RenderedText` what the pipeline actually parses — the resolver's output when one is active, otherwise `RawText` — and `CleanText` the same text with markup stripped. All are zero-alloc; `CleanText`'s span is pooled, so copy it if it must outlive the next parse. `TextOverride` (`TextOverrideSource`) reports why the rendered text differs from the serialized field (a runtime buffer, a resolver, or both).

### 19.2 Measuring

```csharp
var size = text.MeasureText(new TextMeasureOptions { maxWidth = 300f });
```

Every null field in `TextMeasureOptions` falls back to the component's current value, so a default measure is "the current text, as configured, at its natural size". Dimensions are outer — padding included. The text resolver is bypassed, and the call is main-thread only. Measuring another `text` re-parses and re-shapes both texts — cache the result rather than calling it per frame.

### 19.3 Hit testing

`HitTestRange(localPoint, maxDistance)`, and its overload taking a screen point and a `Camera`, returns a `TextHitResult`: the glyph whose box contains the point and its cluster. Use it for entity queries — links, mentions, hover ranges.

`HitTestCaret(screenPoint, camera)` returns the codepoint index a caret should take, edge-snapped (left half of glyph N → N, right half → N+1); the `out bool upstream` overload also reports affinity (§12.2). The two are not interchangeable: caret snapping at a range's trailing edge lands past the range.

`ResultGlyphs` exposes the laid-out glyphs (`PositionedGlyph`), `Buffers.lines` the lines (`TextLine`) and `Buffers.orderedRuns` the runs in visual order (`ShapedRun`); `TextRun` is the pre-shaping itemization run. `TextRange` addresses a span as start plus length. All of these are pooled — copy what must outlive the next rebuild.

### 19.4 Invalidation

`UniTextDirty` names the coarsest pipeline stage a change invalidates; higher stages subsume the cheaper ones. `SetDirty` re-enters at the flag you pass, so pass the least expensive stage that captures your change; its second overload also declares the observable outputs the pass will change. `UniTextCommitChanges` reports what one completed pass actually changed and arrives on `Committed`; `LayoutCommitted` fires when this component's mesh is applied and its layout is final for the frame.

A custom attribute store implements `IAttributeData.Prepare(int)`, which UniText calls once per parse, so the store is always indexed by the codepoints of the text currently being laid out.

### 19.5 Inspecting

The debug overlay visualizes glyph boxes, baselines, advances, run bounds, line bounds, modifier ranges, BiDi direction and pipeline statistics — the first tool to reach for when layout does something unexpected.

`Tools → UniText → Inspection Mode` (Ctrl/Cmd+Shift+I) toggles it in the editor; `UniTextInspector.ToggleKey` (F8) toggles it in play mode and in development builds. `PinKey` (P) freezes the current card so the cursor can move on. `UniTextInspector.Layers` (`InspectionLayers`) selects the drawn layers, `ShowBiDi` and `ShowStats` add direction arrows and the statistics card, and `Target` pins one component instead of following the cursor. The card lists each modifier covering the probed cluster with its parameters resolved there.

### 19.6 Ranges and parameter ownership

A parse materializes a modifier's ranges. Code reads them as `ModifierRange`, selects them with `RangeQuery`, and takes a parameter (§5) over on them with `Own`.

```csharp
var ranges = new List<ModifierRange>();
text.GetModifier<LinkModifier>().GetRanges(ranges);   // text order
```

| `ModifierRange` member | Carries |
|---|---|
| `Range` | the covered codepoints, in rendered text space |
| `Identity`, `Segment` | the stable entity, and the concrete segment of it this range covers |
| `PrimaryValue` | the semantic value the source emitted — a link's URL, a mention's id |
| `Label` | the `#label` anchor authored on the tag (§3.3), or null |
| `Channel` | the `RangeChannel` asset the range is routed to, or null |
| `Resolve(parameter)` | the range's resolved value: cascade plus owned values |
| `Own(parameter, …)` | takes that parameter over on this one range |

A range stays addressable for as long as the parser preserves its `Identity`; an edit inside the range retires the identity, and ownership taken on it is released.

**Queries.** `modifier.Ranges()` starts a `RangeQuery`. It is an immutable value: every filter returns a new query, and filters combine with AND.

| Filter | Keeps |
|---|---|
| `WhereLabel(label)` | ranges whose tag carries that `#label` anchor |
| `WhereParameter(parameter, value)` | ranges whose token for that parameter parses to `value`; ranges without the token never match |
| `WhereToken(parameter, token)` | ranges whose raw token for that parameter matches, ordinally |
| `WhereBare()` | ranges authored with no explicit token at all |
| `WherePrimaryValue(value)` | ranges whose source-emitted value matches |
| `WhereChannel(channel)` | ranges routed to one channel asset |
| `Intersecting(range)`, `Within(range)` | ranges overlapping, or contained by, a codepoint span |
| `Where(predicate)` | ranges a `ModifierRangePredicate` accepts |
| `Skip(n)`, `Take(n)` | a window of the matches, in text order |

A query carries at most one parameter filter — `WhereParameter` and `WhereToken` exclude each other. `Collect(list)` materializes the matches; `Matches(in range)` tests one range against the filters.

`RangeQueryDefinition` is the serialized form of the same filter set, rebuilt into a live query on every bind, so an inspector-authored selection follows text edits. Driver clips (§8.5) are authored this way.

**Ownership.** `Own` composes a value on top of a parameter's cascade until it is released.

```csharp
text.Text = "Stock <color #alert>low</color>, restock <color #alert>today</color>";

var color = text.GetModifier<ColorModifier>();
using var alert = color.Ranges()
    .WhereLabel("alert")
    .Own(ColorModifier.Param.Color);

alert.Value = new Color32(255, 64, 64, 255);        // every matching range
alert.SetValue(0, new Color32(255, 160, 0, 255));   // the first one, until the next parse
```

| Entry point | Owns |
|---|---|
| `ModifierRange.Own` | one parameter of one range → `OwnedParameter<TValue>` |
| `RangeQuery.Own` | one parameter across the query's matches → `OwnedParameterSet<TModifier,TValue>` |
| `BaseModifier.Own` | the same, across every range of the modifier |
| `UniTextRanges.Own` | either shape, from the runtime the component already holds |

`OwnedParameter<TValue>` carries `Value`, the `Baseline` beneath it, its `Composition` and `IsAlive`. `Release` — or `Dispose` — returns the parameter to its cascade; every other member throws `ObjectDisposedException` once the ownership is dead.

`OwnedParameterSet<TModifier,TValue>` is standing ownership. It re-materializes after every parse — ranges that stop matching release, new matches receive the broadcast `Value`, survivors keep theirs — and raises `Changed` when it does. `Count` and `GetRange(i)` follow text order, `SetValue(i, value)` writes one member until the next parse, and `Withhold()` deactivates every member's value without giving up membership.

Code, rules (§9) and driver clips (§8.5) all reach a parameter through this one path, so they compose against each other by `ParameterComposition` and priority instead of overwriting one another.

`Tools → UniText → Range Debugger` captures a `UniTextRangeDebugSnapshot` — the live range entities of a component and the rule playbacks bound to them — and can push signal values by hand.

---

## 20. Accessibility

`UniTextSemantics.For(text)` returns the semantic tree of one component; the static `TryGet` finds an existing one without creating it. `Nodes` lists the live `TextSemanticNode`s in logical codepoint order, each carrying `Identity`, `Role` (`TextSemanticRole`), `Label`, `Value`, `Hint`, `Language`, `States` (`TextSemanticStates`), `Actions` (`TextSemanticActions`), `Segments` and `Bounds`.

`Changed` fires once per added, updated or removed node, `Committed` after one layout commit has emitted them all. `PerformAction(identity, action)` invokes a single declared action; `ActionRequested` runs project handlers first, and `PreventDefault` on the request suppresses the built-in Activate / Context routing.

`SemanticModifier` is the only producer: applied through a `Style` like any modifier (§3.1), it annotates a range with role, label, value, hint, language, states and actions. An empty label derives from the covered text by default.

Nothing enters the tree on its own: a range is described only where a `SemanticModifier` covers it, and its role is whatever that modifier resolves — `Text`, `Link`, `Button`, `Mention`, `Tag`, `Code`, `Math`, `Heading`, `Image`, `Comment`, `Error` or `Status`. The tree is platform-neutral: a native accessibility bridge or an automated test is an adapter over it, and the package ships none.

---

## 21. Recipes

**Clickable links**

```csharp
text.Styles.Add(Style.Tag(new LinkModifier(), "link"));
text.Styles.Add(Style.FromSource(new RawUrlParseRule(), new LinkModifier()));
text.Text = "See <link=https://example.com>the docs</link> or https://example.com";
```

`LinkModifier` carries no base visual styling — compose it with `ColorModifier` and `UnderlineModifier` in a `CompositeModifier` (as the built-in Link preset does) to make links read as links. `AutoOpenUrl` is on by default; subscribe to `LinkClicked` to handle activation instead.

**Typewriter**

```csharp
var reveal = text.GetModifier<RevealModifier>();
reveal.Collapse = false;
for (float t = 0; t < duration; t += Time.deltaTime)
{
    reveal.Front = UnitValue.Percent(100f * t / duration);
    yield return null;
}
```

A `UniTextDriver` clip on `RevealModifier.Param.Front` does the same without a coroutine, and scrubs in the editor (§8.5).

**Whole-text colour without markup**

```csharp
text.Styles.Add(Style.WholeText(new ColorModifier(), "#FF0000"));
text.Styles.Add(Style.Range(new ColorModifier(), 0, 5, "#FF0000"));   // a fixed span
```

**A chat composer**

Add `UniTextSelectable` + `UniTextEditable`, then an `InputBehaviorPreset` holding `SubmitKeyBehavior` (Enter submits, Shift+Enter inserts a newline, focus survives), a `MediaInputBehavior` subclass (pasted images become attachments), `LengthLimitBehavior`, and a `PlaceholderDecorator`.

**A password field**

`PasswordBehavior` + `SelectAllOnFocusBehavior` + `NativeKeyboardBehavior` with `AutofillHint.Password` and `AutoCorrection` off.

**A validated email field**

`EmailFilter` (rejects illegal characters as typed) + a validator subclassing `InputValidatorBase` (judges the whole value) + `AutoValidateBehavior` + `SupportingTextDecorator` to show the message.

**System fonts only, no bundled font**

Leave the stack empty or put a single `UniTextSystemFont` in it. Automatic OS fallback (`SystemFont`) covers everything else. WebGL is the exception — it has no OS font access, so a WebGL build needs a regular `UniTextFont` in the stack.
