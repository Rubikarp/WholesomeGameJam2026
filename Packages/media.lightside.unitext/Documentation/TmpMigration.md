# Migrating from TextMesh Pro

`Tools → UniText → Migration` opens a workspace that finds every TextMesh Pro usage in the project and converts it, a batch at a time, under your control. It is not a "convert everything" button: it scans, ranks the work, and applies exactly what you select.

Nothing in this window runs by itself. Nothing runs during a build, and nothing runs on project open.

---

## 1. Before you start

| Requirement | Why |
|---|---|
| **Asset Serialization = Force Text** (`Edit → Project Settings → Editor`) | The scanner reads scenes and prefabs as YAML. Binary assets would report zero TMP usage. Scanning is refused until this is set. |
| **Commit to version control** | Migration rewrites prefabs, scenes and scripts in place. The diff is your real undo. |
| **Keep TextMesh Pro installed** | Components are read through the TMP assemblies. Remove the package only after the last finding is handled. |
| **A short-lived branch** | A migration that runs for weeks collides with everything else in the project. |
| **A Style preset in `Project Settings → UniText`** | UniText has no built-in tag vocabulary. Without a project-wide preset, migrated text renders `<b>`, `<color>` and the rest as literal characters — and the slot is empty in a new project. The Dashboard builds one carrying every TMP tag UniText can reproduce; see §3. |

**What the scan reads.** Everything under `Assets/` and `Packages/`: scenes, prefabs, C# files, `.asset`, `.mat`, `.anim`, `.asmdef`, and `.csv`/`.json`/`.txt` carrying markup. `.ttf`/`.otf` files are indexed as font sources rather than reported as work. UniText's own packages are never scanned — they name every TMP GUID on purpose, and reporting the tool as work to do would be nonsense. `Library`, `Temp`, `Logs`, `obj` and dot-folders are skipped, as is anything on the Settings tab's exclusion list.

A file is read once as bytes and matched against plain ASCII markers before anything is decoded, so a multi-megabyte atlas or dictionary costs nothing. Reading and matching run on worker threads. A first scan of a large project is the slow one; every later scan re-reads only what changed.

---

## 2. The four stages

The Dashboard shows them in order, with the current one marked. The order is not advice — each stage depends on the one before it.

**1 · Fonts.** Every TMP font used in the project needs a UniText font stack. A migrated component points at the *stack*, not at a font, so a component whose font is still unmapped comes out without a font. Component migration stays blocked until every font is mapped or skipped.

Mapping a font **creates**; it never deletes. `TMP_FontAsset` files stay exactly where they are, with every reference intact — the two sets live side by side until you remove TextMesh Pro yourself. That is deliberate: TMP has to stay installed while any component is still on it.

**Fallbacks are carried over.** TMP keeps two lists: one on each font asset (`m_FallbackFontAssetTable`, or `fallbackFontAssets` on assets written by older TMP versions) and one project-wide (`TMP_Settings`). Both are rebuilt, because a UniText stack resolves a codepoint by walking its families in order:

| TMP | UniText |
|---|---|
| A font's own fallback table | the families behind that stack's primary, flattened depth-first in TMP's order, each font visited once so a cyclic table terminates |
| `TMP_Settings` project-wide list | one shared stack — `UniText Project Fallbacks.asset`, created beside your UniText settings asset — chained onto every mapped stack through `fallbackStack`, so it stays a single list to edit, as it was in TMP |

A chain is only as complete as its fonts: a fallback that has no UniText font of its own drops out of it, and both the card and the Log name it. That is what to check first if migrated text shows boxes where CJK or emoji used to be.

Stacks are rewritten only while they still look the way the migration wrote them — a bare primary per family. Give a family a name, a language hint or extra faces and the stack becomes yours; later runs leave it alone and say so in the Log.

**2 · Components.** Prefabs first, leaf prefabs before the prefabs that nest them, scenes last. The tool computes that order from the prefab dependency graph and follows it for you — a nested prefab is rewritten before its parents so the parents pick up the change instead of overriding it. A `<sprite>` tag written in a component's own serialized text turns its TMP sprite asset into a shared `UniTextSprites` catalog under the project's UniText folder, built before that component is replaced; the TMP assets remain untouched. A component that has a sprite asset assigned but writes no sprite tag — because its text arrives from script or localisation — gets no catalog and says so in the Log: what each occurrence needs can only be decided against markup that exists. Give those a `SpriteModifier` with an `AssetSpriteProvider`, bound through an `InlineTagRule("sprite")` in the Style.

A glyph the TMP asset itself cannot render — a rect reaching past its sheet, which is what replacing the texture under an existing sprite asset leaves behind — is left out of the catalog and named in the Log. It refuses only the text that writes that index; every other component using the same asset migrates. TMP renders such a glyph wrong too, so nothing is lost that was working.

Migration is per file: pressing `Migrate` on one row rewrites every TMP component in the prefab or scene that row lives in, not just that one component.

Before changing a file, the tool inspects every sibling component's inherited `RequireComponent` declarations, and splits them in two.

A sibling requiring a type the UniText replacement **also satisfies** — a shadow, outline or gradient decorator declaring `RequireComponent(UnityEngine.UI.Graphic)` — is *set aside for the swap and put back afterwards with the values it carried*, because `Graphic` is `DisallowMultipleComponent` and the new component cannot be added while the old one is still there. Its position among the object's components is preserved. The one thing it cannot get back is a serialized reference to the text component that was replaced; the Log warns whenever it finds one.

A sibling requiring a type **UniText can never be** — `TMPro.TMP_Text` itself, as TMP-specific text animators and localisers declare — is *removed*, together with whatever required it in turn, deepest dependent first. Nothing else can happen: no UniText component is a `TMP_Text`, so that object could never lose its TMP text while such a sibling stayed. Before each removal the component's full serialized state is captured and written to the **Removed** tab and to `ProjectSettings/UniText/RemovedComponents.json` — the component does not come back, and that record is what you rebuild from. Fields on other components of the same object that pointed at it are listed too, since they are empty now; references from *other* assets are outside what this checks. A component Unity refuses to remove leaves its object untouched and becomes **Failed** with the reason. `Re-check` re-runs every gate the migration itself applies — for an input field that includes its serialized identities, the components it owns and any prefab override on them — and never migrates the object. `Re-check all failed` does the same for every failed row in one pass, which is what clears a project-wide cause such as a missing Input Field Prefab. `Mark handled` closes the review after the object was resolved by hand; a component still present in its asset returns to Pending, because the script stage is judged on the file's bytes rather than on a status.

A row whose TMP component has no UniText counterpart — a `TMP_Dropdown` — offers no `Migrate` button at all. Rebuild it from Unity's `Dropdown` and press `Skip`.

An asset the scan could not read is listed as its own finding rather than passing for clean, and a scan that ended early blocks every later stage until it is run again.

**3 · Scripts, with the assemblies that compile them.** Only after components: a rewrite renames `TextMeshProUGUI` to `UniText`, and if the scene still holds a TMP component, the serialized reference in that scene has nothing left to point at.

Assembly definitions belong to this stage, not to cleanup. An `.asmdef` that references `Unity.TextMeshPro` has to reference UniText **before** its scripts are rewritten — a script that names UniText types does not compile until its assembly can see them. The one-pass run refuses to touch scripts while any assembly definition is still pending, for exactly that reason.

**4 · Cleanup.** Everything the three ordered stages do not claim: materials, animation curves, text assets carrying TMP markup, compiled assemblies, and anything else the scan reported. All of it is handled by hand — the tool lists it and records that you dealt with it.

---

## 3. The tabs

### Dashboard

Scan, the four stages, and the actions that move the current one forward. Its cards, top to bottom:

- **Scan.** `Scan project` / `Re-scan project` (a re-scan keeps every status you already set), `Stop scan` while one is running, `Verify`, and `Export report`. A scan stopped early is marked as partial beside the timestamp.
- **Migration order.** The four stages with a count each, the current one marked `▶` and finished ones `✔`, the reason the stage is blocked if it is, and the two bulk actions: `Migrate simple components` for the safest batch and `Migrate all pending` for everything regardless of complexity. Both name the reason when they are unavailable. `Open <tab>` jumps to wherever the next step is performed.
- **Markup vocabulary.** Appears while no project-wide preset is assigned. It builds a `StylePreset` carrying **every** TMP tag UniText can reproduce — not only the tags this scan saw — and assigns it in `Project Settings → UniText`. One asset, one setting, every component covered; nothing is added to the components themselves. Building the whole vocabulary is the point: text that reaches a component at runtime, from localisation or from script, is never scanned, and its markup has to render too. `<link>` is wired to the package's `LinkPreset` modifier graph, which carries the link's interaction rules and states rather than a bare modifier. Four tags stay out because they need an asset only you can choose — `<sprite>` and `<quad>` (their catalog is built per component), `<material>` and `<gradient>` — and the Log names anything else it could not wire.
- **Run it in one pass.** For projects that do not want to walk the stages by hand. It builds every font it has a source file for, migrates every pending component, then rewrites every pending script, in that order. It stops at the first stage it cannot finish and says why: a font with no source to build from, a component still on TMP, or an assembly definition still pointing at `Unity.TextMeshPro`. It asks for confirmation, names every count first, and states how many findings will still be waiting afterwards — dropdowns, materials, animation curves and rich-text assets are never touched, and an input field it cannot represent exactly stops at a review rather than being half-converted.
- **Progress**, **What was found**, **How hard each finding is.** The inventory rows carry a `Show` that opens Analysis filtered to that kind.

`Verify` re-reads every component marked Completed and returns to Pending anything that still contains a TMP script — use it after reverting files in version control.

### Analysis

Every finding, filtered by kind, status and free text. This is where work is selected.

`Select everything shown` ticks exactly what the filter is showing; `Clear` unticks everything, including rows the filter is hiding. The two bulk buttons — `Migrate ticked components` and `Skip ticked` — say how many rows they will act on. Ticked rows of any kind other than a component are left alone by the first one.

Each row carries its status, its complexity, a `⚠` when the analyser attached warnings, and up to three buttons: `Migrate` (components only — rewrites the whole prefab or scene), `Skip` (marks the finding handled without touching a file), and `Open` (selects the asset in the Project window). Hovering the row shows the path, who does the work for that kind of finding, and every warning.

### Font Mapping

One card per TMP font, with the count mapped so far in the header. `Create every missing font` builds all of them at once. Per card: `Create font + stack` builds both assets beside the original TTF/OTF, `Browse…` points at that file when the scan could not find it (a file outside `Assets` is copied in), and `Skip` / `Unskip` declares the font handled — for fonts nothing uses any more. The `UniText font` and `Font stack` slots also take assets you made yourself; what migrated components are pointed at is the **stack**.

A font with a TMP fallback table shows the chain, in order, with `⚠` on every link that has no UniText font yet.

### Script Preview

The per-file diff of the C# rewrite before it is written. Lines starting with `-`/`+` are rewritten; lines marked `^` are warnings the tool will not act on. `Apply this file` and `Apply all` write the changes, each keeping the original beside it as `.bak`; `Mark handled` takes a file off the list untouched, for one you port by hand. A file whose findings are all warnings cannot be applied. The tab warns while any component is still on TMP.

### Settings

Folders excluded from the scan (effective on the next scan), the guard, and the recommended order of work.

The **guard** raises a dialog whenever a scene or prefab is imported carrying a TMP text, 3D text or input-field component, so new TMP usage does not slip in behind the scan. Its dialog offers `OK`, `Don't warn again` — which turns the guard off — and `Open Migration Tool`.

### Log

Everything the tool did, filterable by severity, exportable, and clearable. Each rewritten script carries a `Restore` that puts the `.bak` back and returns the file to Pending. Clearing the log drops those buttons; the `.bak` files themselves stay on disk.

---

## 4. Reading the numbers

### Status

| | |
|---|---|
| **Pending** | Not touched. |
| **Completed** | Rewritten by the tool. |
| **Skipped** | Declared handled by you. Skipping is how work done by hand leaves the list. |
| **Failed** | The tool could not rewrite it; Analysis keeps the durable reason, required action and `Re-check`. |

### Complexity

| | |
|---|---|
| **Simple** | The migrator reproduces it exactly. |
| **Moderate** | Either something has no UniText equivalent and was defaulted, or a value converts onto a scale UniText measures differently — check the result. |
| **Complex** | Only partly mechanical; the rest is rebuilt by hand. |
| **Manual** | No mechanical path at all. |

What earns each grade, per kind of finding:

| Finding | Grade |
|---|---|
| `TextMeshProUGUI` | **Simple**, unless something below makes it Moderate |
| ⤷ no UniText equivalent, defaulted | **Moderate**: highlight font style; *Geometry* horizontal alignment; *Baseline*, *Geometry* or *Capline* vertical alignment; the masking, scroll-rect, page and linked overflow modes |
| ⤷ converts onto another scale | **Moderate**: non-zero character, word, line or paragraph spacing; `fontWeight` other than 400; vertex gradient enabled |
| `TextMeshPro` (3D) | **Complex** — it converts, but `Migrate simple components` never picks it up; use `Migrate all pending` or the row's own button |
| `TMP_InputField` | **Complex** — it converts as one composite field; `Migrate simple components` never picks it up. A setting with no UniText counterpart does not stop it — the field migrates and the value is listed under *Not carried over*. Only a composition the tool cannot rebuild is refused. Its own Text and Placeholder children are graded on their own, and are replaced only together with the field |
| `TMP_Dropdown` | **Manual** |
| Script reference | **Simple**; **Moderate** when it names `TextAlignmentOptions`; **Manual** when it names `TMP_SpriteAsset`, `TMP_Dropdown` or `textInfo` |
| `TMP_FontAsset` | **Moderate** |
| `TMP_StyleSheet`, `TMP_Settings` | **Manual** |
| Assembly definition | **Simple** |
| Rich-text asset | **Simple**; **Moderate** when it writes a tag with no UniText modifier, or one needing an asset you must supply |
| Material | **Moderate** |
| Animation clip | **Complex** |
| Compiled assembly | **Manual** |

Complexity is difficulty, not ownership: a Simple script reference still waits for you to press Apply, and a Simple rich-text asset is still edited by hand. Each row's tooltip names who does the work.

**These counts span every kind of finding**, not just components. A project can show `Simple: 311` next to `Migrate simple components (4)` — the 311 includes script references, assembly definitions and text assets, while the button only rewrites components. The two numbers are answering different questions.

The other reason that button can be greyed out is unmapped fonts. Its tooltip always names the reason.

---

## 5. What is converted, and what is not

### Components

| TMP | UniText |
|---|---|
| `TextMeshProUGUI` | `UniText` |
| `TextMeshPro` (3D) | `UniTextWorld` |
| `TMP_SubMesh`, `TMP_SubMeshUI` | removed — UniText needs no sub-meshes |
| `TMP_InputField` | `UniTextSelectable` + `UniTextEditable` on the field's own text object — see *Input fields* below. Anything not exactly representable stops the whole field and is recorded as a durable review. |
| `TMP_Dropdown` | **no equivalent.** Use Unity's standard `Dropdown` with a UniText label. |

The TMP component is removed and the UniText one takes its place on the same GameObject — two `Graphic` components cannot coexist there. Everything read from TMP is read before the removal, and the whole swap is one Unity Undo step per scene. A declared dependency that prevents this swap is never removed or recreated automatically: doing so would change the dependent component's identity, overrides and references.

Carried over directly: text, font size (including the auto-size range), colour, word wrap, auto-size, raycast target, maskable, and the margins — TMP's `margin` (Left, Top, Right, Bottom) becomes UniText's `padding` (Left, Bottom, Right, Top).

Alignment is decomposed into `HorizontalAlignment` + `VerticalAlignment`. TMP's Left and Right map to UniText's `Start` and `End`, which follow each paragraph's writing direction; Center, Top, Middle, Bottom and *Justified* map exactly; *Flush* maps to Justify plus a whole-text `AlignmentModifier` that justifies the last line as well. Use `Left` / `Right` where an edge must stay physical whatever the text direction. TMP's *Geometry* horizontal mode and its *Baseline*, *Geometry* and *Capline* vertical modes have no UniText equivalent — each falls back to Start / Top and is logged.

Anything TMP expressed as a component setting and UniText expresses as a modifier becomes a **whole-text** `Style` entry on the component — applied, not merely made available as markup: font-style flags (bold, italic, underline, strikethrough, upper, lower, small-caps), character spacing, word spacing, line spacing, paragraph spacing, right-to-left direction, and the ellipsis or truncate overflow modes. TMP's character, line and paragraph spacing are measured on scales UniText does not share — the Log flags each for a visual check.

*Superscript* and *Subscript* become a `ScriptPositionModifier`, which uses the font's OpenType `sups`/`subs` glyphs where they exist and synthesizes from OS/2 metrics where they do not. *Highlight* is dropped with a warning: TMP keeps no per-component highlight colour to carry over, so add a `HighlightModifier` and set its paint by hand.

`fontWeight` becomes a `VariationModifier` on the variable-font `wght` axis. A static font has no axis to move, so where TMP relied on a weighted font pair the weight is logged and lost.

### Input fields

A `TMP_InputField` is not one component but a small hierarchy — a field box, a masked viewport, a text
object and usually a placeholder. It migrates as **one composite object or not at all**: its text and
placeholder are taken out of the ordinary per-component pass and converted together with it, so the
field is never left with UniText text inside a TMP field.

What the migrated field looks like: the box and viewport keep their transforms, `UniTextSelectable`
and `UniTextEditable` join the `UniText` on the field's own text object, and the `TMP_InputField`
component is removed. A serialized reference that pointed at the old field is redirected to the new
`UniTextEditable`, so inspector wiring survives.

Everything TMP kept as a field setting becomes an input behavior, copied onto the component from the
Input Field Prefab in `Project Settings → UniText` and then overridden from the source field:

| TMP setting | UniText |
|---|---|
| Content Type `Integer` / `Decimal`-style validation | `IntegerFilter`, negative values allowed for the signed form |
| Character Limit | `LengthLimitBehavior` in UTF-16 units |
| Keyboard Type, Auto-Correct | `NativeKeyboardBehavior` |
| Hide Mobile Input off | `NativeFieldOverlayBehavior` |
| Input Type `Password` | `PasswordBehavior` with TMP's asterisk character |
| Line Type `Single Line` | `SingleLineBehavior` — Enter submits, newlines stripped |
| Line Type `Multi Line Submit` | `SubmitKeyBehavior` bound to Enter, focus released on submit. The text stays multi-line; Shift+Enter now inserts a newline where TMP submitted on it too, and the Log says so |
| Line Type `Multi Line Newline` | nothing — Enter inserts a newline, UniText's own default |
| On Focus Select All | `SelectAllOnFocusBehavior` |
| Restore On ESC | `RestoreOnCancelBehavior`, otherwise `DefocusOnCancelBehavior` |
| Placeholder | `PlaceholderDecorator` pointing at the migrated placeholder text |
| Read Only, enabled state | carried over directly |

**Exactly representable, or nothing.** The field is left untouched and recorded as a durable review
when any of these is true: the text or placeholder is not a `TextMeshProUGUI` or does not sit directly
under the masked viewport; any of the field's UnityEvents has a persistent listener; a custom input
validator is assigned; the character validation, input type, line type or keyboard type has no exact
UniText equivalent; the field uses a line limit, a scrollbar, non-default scroll sensitivity, soft-keyboard
hiding, non-default deactivation or rich-text editing policy; the caret or selection presentation was
customised; the field is non-interactable, or its `Selectable` navigation or transition was changed;
or `Project Settings → UniText` has no Input Field Prefab to take the missing policies from. The review
names the one condition that stopped it — fix it and press `Re-check`.

C# is held to the same line: a file whose `TMP_InputField` use the rewrite cannot fully resolve is
refused as a whole, rather than renamed into something that no longer compiles.

### Rich text in the text itself

Most text is not edited. The migrator adds the `Style` entries the markup needs, taking each entry's default value from the first occurrence of that tag. Sprite attribute forms are the exception described below.

**Kept as written.** Their modifier comes from the project-wide Style preset; the migrator adds a component entry only where no preset supplies the tag.

Several keep the tag but not its value, and the tool names each one it finds: TMP's `<align=justified>` and `<align=flush>` become `Justify` (flush additionally wants `LastLineAlignment = Justify`); `<font=>` names a `FontFamily` inside the component's FontStack rather than a font asset; `<mark=>` takes its colour from the Style entry, not from the tag; `<mspace>` values without a unit are font units in TMP and pixels in UniText.

**Sprites are resolved before the component is changed.** Every TMP character-table position becomes the same numeric key in a generated `UniTextSprites`, so `<sprite=15>` stays byte-for-byte `<sprite=15>`. `<sprite index=15>` and `name=` forms become the equivalent numeric form. A fallback or explicitly named TMP sprite asset receives its own local inline tag and `SpriteModifier`, which prevents index `15` in one catalog from colliding with index `15` in another. Each style uses `AssetSpriteProvider` and `InlineTagRule`, so both `<sprite=15>` and `<sprite=15/>` insert one object rather than opening a range. World-space `TextMeshPro` components containing sprite tags stay on TMP because `SpriteModifier` renders through Canvas UI; the tool records that object as Failed instead of silently losing its sprites.

`color=` without tint and simple component-colour tint are carried over. Animation, color×tint multiplication, and tint under range colours or vertex gradients are not weakened silently: the component remains TMP and the Log states the unsupported contract. Legacy atlases whose glyphs have no `Sprite` references are sliced into persistent Sprite subassets from their TMP glyph rectangles.

**Kept, but you create the `Style` entry:** `<material=>` and `<quad=>`. The modifier exists; the asset it points at cannot be guessed. The migrator deliberately adds nothing here — an unconfigured `MaterialModifier` suppresses the text it covers.

**No UniText modifier — the markup itself has to be rewritten:** `<gradient=>`, `<pos=>`, `<space=>`, `<voffset=>`, `<rotate=>`, `<width=>`, `<noparse>`, `<page>`, `<style=>`, `<margin=>` and `<margin-right=>`. Only the left side of a margin has a mechanism (`IndentModifier`), which is why `<margin-left=>` is in the first list and the other two are here.

**Supplied by the project-wide Style preset, not by the migrator:** every TMP tag with a UniText modifier behind it — `<b>`, `<i>`, `<u>`, `<s>`, `<mark>`, `<sub>`, `<sup>`, `<smallcaps>`, `<lowercase>`, `<uppercase>`, `<allcaps>`, `<nobr>`, `<size>`, `<color>`, `<font>`, `<font-weight>`, `<align>`, `<cspace>`, `<mspace>`, `<indent>`, `<line-indent>`, `<margin-left>`, `<line-height>` and `<link>` — together with the names UniText spells its own way: `<letter-spacing>`, `<upper>`, `<lower>`, `<outline>`, `<stroke>`, `<shadow>`, `<glow>`, `<fill>`, `<ellipsis>`, `<var>` and `<obj>`. Nothing in UniText's tag vocabulary is built in — a tag works because a `Style` entry says it does. Their home is the project-wide Style preset in `Project Settings → UniText`, which every component applies unless it opts out, and **that slot is empty in a new project**. The Dashboard's *Markup vocabulary* card builds the whole set in one asset; while a component already carries a rule for a tag, or the preset supplies it, the migrator adds nothing — two rules for one tag would apply its modifier twice.

TMP's `<gradient=name>` is not among them: it names a `TMP_ColorGradient` asset, and UniText expresses a gradient as a paint on a fill layer rather than as a named tag. It is in the rewrite-by-hand list above.

### Scripts

The rewrite reads each file lexically before it edits anything. String literals — including verbatim, interpolated and raw ones — comments, and TMP-conditional `#if` regions are never touched, and a member is renamed only where the receiver is known to hold a TMP type: a declaration in that same file, a `var` whose initializer carries one, a `GetComponent<T>()` type argument, a cast, an `as`, a `new`, the element type of an array or list the file declares, or the loop variable of a `foreach` over one. What it cannot resolve it reports rather than guesses, so no `.text` on some other class is renamed by accident.

**Types**, wherever they appear, including `TMPro.`-qualified: `TextMeshProUGUI` → `UniText`, `TextMeshPro` → `UniTextWorld`, `TMP_Text` → `UniTextBase`, `TMP_InputField` → `UniTextEditable`, `TMP_FontAsset` → `UniTextFont`.

**Members, on a resolved TMP receiver**: `.text` → `.Text`, `.fontSize` → `.FontSize`, `.fontSizeMin`/`.fontSizeMax` → `.MinFontSize`/`.MaxFontSize`, `.enableAutoSizing` → `.AutoSize`, `.enableWordWrapping` → `.WordWrap`, `.font` → `.Font`. Members reached on a font asset are left alone entirely — the two types share no member names worth guessing at.

**Listeners**: `field.onValueChanged.AddListener(h)` becomes `field.ValueChanged += (h)`, `onSubmit` becomes `Submitted`, and `RemoveListener` becomes `-=`. UniText raises C# events, not `UnityEvent`s, so the call changes shape and not only its name. Only the head of the call is rewritten, so a multi-line handler converts too and keeps its own edits; a call whose head is itself split across lines is reported instead.

**`using`**: `using TMPro;` becomes `using LightSide;` — unless the file still names a TMPro type with no counterpart, in which case both stay and the reason is reported. A file that already had `using LightSide;` simply loses the TMPro line, and a file that only ever wrote `TMPro.Type` gains the directive it now needs.

**Reported, never rewritten**, because the shape or the meaning changes: `alignment`, `horizontalAlignment` and `verticalAlignment` (different value sets), `fontStyle`, `fontWeight`, `characterSpacing`, `wordSpacing`, `lineSpacing`, `paragraphSpacing`, `margin` (different component order), `overflowMode`, `richText`, `isRightToLeftText`, `maxVisibleCharacters`, `textInfo`, `ForceMeshUpdate`, `GetPreferredValues`, `preferredWidth`/`preferredHeight` (UniText implements `ILayoutElement` explicitly), the gradient, outline and material members, `onEndEdit`/`onSelect`/`onDeselect` (their handler signatures differ), every TMPro type with no counterpart, and each `#if` region gated on a TMP symbol — which branch compiles is the symbol's decision, not the tool's.

A file whose findings are all warnings cannot be applied — port it by hand and press `Mark handled`.

### References the rewrite would otherwise break

Replacing a component gives it a new local file id, and a field that changed type points at the wrong kind of asset. Both are repaired automatically, once per batch, across every text-serialized asset in `Assets/` and in embedded or local packages. Immutable package caches are skipped: they cannot be rewritten.

- **Component references.** Every field pointing at a migrated text component — in any scene, prefab or asset — is redirected to the component that replaced it. The pairing is read from the file itself, by the GameObject each component sits on, so it works for prefabs edited through prefab contents as well as for scenes.
- **Font references.** A `TMP_FontAsset` field that became a `UniTextFont` field keeps its value: inside the documents of the scripts the rewrite touched, the TMP font's GUID is moved to its mapped UniText font. TMP's own assets are left alone — they still reference their fonts legitimately.

Per-instance prefab *overrides* on a replaced component are a separate matter and are not carried over; migrate the prefab before the scenes that instance it, and check the diff.

### Materials, animations, assemblies

| Finding | What to do |
|---|---|
| Material on a TMP shader | UniText draws text with its own materials. `_OutlineColor`/`_OutlineWidth` become a `StrokeModifier` or `<stroke=…>`; `_UnderlayColor`/`_UnderlayOffset*` become a `ShadowModifier` or `<shadow=…>`; `_GlowColor` becomes a `GlowModifier` or `<glow=…>`. |
| Animation clip | Colour and font-size curves map onto UniText's properties; every other TMP-specific curve targets something UniText does not have and must be re-authored. |
| Assembly definition referencing `Unity.TextMeshPro` | Reference the UniText assembly instead, then drop the TMP one. |
| Compiled assembly exposing TMP types | Only its owner can fix it — the `.dll` must be rebuilt against UniText. |

---

## 6. Undoing

| Layer | Covers |
|---|---|
| Version control | Everything. Commit before each batch; this is the real undo. |
| Unity Undo | Component migration in a scene that was already open, as one step per scene. A prefab, and a scene the batch opened itself, is written to disk and closed — only version control takes those back. |
| `.bak` files | Script rewrites. The Log's `Restore` puts the original back and returns the file to Pending. |
| `Verify` | Nothing is undone, but statuses are re-checked against what is actually on disk. |

---

## 7. Where the state lives

| Path | Contents | Commit it? |
|---|---|---|
| `ProjectSettings/UniText/MigrationState.json` | Per-finding status, durable manual reviews, excluded folders, guard setting | Yes — this is the team's shared progress and manual checklist. |
| `ProjectSettings/UniText/FontMappings.json` | TMP font → UniText font stack pairs, and the fallback lists behind them | Yes. |
| `Library/UniText/MigrationSession.json` | The scan cache and the log | No. Delete it freely; a re-scan rebuilds it. |

Generated sprite catalogs carry the source TMP asset GUID as a Unity asset label, so later component batches find the same catalog even after it is moved or renamed. Reuse itself is decided by content: every entry's rect, metrics, colour, pivot and line height is compared against the TMP asset, and a mismatch is reported entry by entry. Re-importing the TMP asset does not by itself invalidate a catalog.

A re-scan only reads files whose timestamp or length changed since the last one; everything else is answered from that cache, and the Log says how much was not re-read. The first scan after deleting it is a full one.

Statuses are keyed by a hash of the file path plus the object's identity, so they survive a re-scan. A Failed component also records the asset GUID and component file ID, so its manual review follows an asset rename and remains visible even when a later scan no longer discovers the component. `Re-check` returns it to Pending after the blocker is gone, or marks it handled when the exact serialized target is absent. Other moved or renamed findings return as Pending.

---

## 8. As a team

Commit `MigrationState.json` and `FontMappings.json` after each session, so nobody re-does finished work. The scan cache is local and never conflicts.

Turn on the guard in Settings for the duration of the migration: it stops new TMP components from arriving behind the scan.

---

## 9. Troubleshooting

**A prefab was skipped because of a missing script.** Unity refuses to save a prefab that carries a component whose script no longer resolves, so the migration cannot rewrite it either — it skips the file and names the object. Restore the script or remove the component, then migrate that prefab again. The scan reports the ones it can see in the file text; a reference to a deleted script asset only shows when the prefab is opened.

**A file failed to save.** Nothing is marked as done: every component the tool had claimed in that file becomes Failed with the reason and recovery action stored in Analysis. Fix the cause and press `Re-check`; the file is never reported as migrated while its write was rolled back.

**Unity says another component depends on `TextMeshProUGUI` or `TextMeshPro`.** Open the Failed row in Analysis. It names every dependent component and the type it requires. Update, replace or remove that dependency, then press `Re-check`; use `Mark handled` only when the object was resolved outside the tool. The record stays in `ProjectSettings/UniText/MigrationState.json` across reloads and re-scans.

**The scan found nothing, or far too little.** Asset Serialization is not Force Text, or the folders in question are on the Settings tab's exclusion list.

**`Migrate simple components` is greyed out.** Its tooltip names the reason: unmapped fonts, or no pending Simple components left. `Migrate all pending` beside it takes every component regardless of complexity — including the 3D texts, which are never Simple — and anything the migrator cannot convert stays put and is reported in the Log.

**A row has no `Migrate` button.** Only components are rewritten in place. Scripts are applied from Script Preview, fonts from Font Mapping; everything else is done by hand and then skipped.

**Migrated text has no font.** The TMP font was skipped, or mapped to a `UniTextFont` without a `UniTextFontStack`. Components point at the stack.

**Migrated text shows boxes where CJK, emoji or another script used to be.** A fallback lost its link: the font it points at has no UniText font yet, so it is not in the rebuilt chain. The Font Mapping card for the primary lists the chain and marks the broken links with ⚠; create or assign those fonts and the chain closes itself.

**Tags render as literal text.** UniText has no built-in tag vocabulary — a tag exists only when a `Style` entry says so, and that slot is empty in a new project. Use the Dashboard's *Markup vocabulary* card, or assign a preset yourself in `Project Settings → UniText`. Because the preset carries the whole vocabulary rather than the scanned subset, text assigned at runtime — from localisation, from script — renders the same markup as text migrated from a component. Only a component that turned **Use Global Style Preset** off is outside it.

**A component with `<sprite>` stayed on TMP.** The Log names the exact unresolved asset, index, glyph or unsupported sprite feature. Nothing on that object was partially converted. Assign or repair the TMP sprite asset, or replace `anim`/compound tint behavior, then migrate the row again.

**An input field was recorded as Failed.** A field migrates only when every one of its settings has an exact UniText equivalent; the review names the one that did not. Resolve it — remove the persistent listener, drop the custom validator, restore the default caret — and press `Re-check`, or rebuild the field by hand and press `Mark handled`. `TMP_Dropdown` has no equivalent at all: rebuild it from Unity's `Dropdown` and press `Skip`.

**Everything went back to Pending.** `Verify` does that when the file on disk still contains TMP — usually after a revert in version control.

**Scripts no longer compile after a rewrite.** Restore the file from the Log, migrate the remaining components first, then rewrite the scripts. If the errors are about UniText types not existing, the assembly definition still has to reference UniText.

**A serialized reference came back empty anyway.** Reference repair runs per batch, over `Assets/` and embedded or local packages. A reference held inside an immutable package cache, or in an asset that is not text-serialized, is outside its reach.
