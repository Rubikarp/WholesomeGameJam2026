# Basic Usage Sample

Demonstrates core UniText features with interactive examples.

## Features Demonstrated

### Markup System
- **Bold**: `<b>text</b>`
- **Italic**: `<i>text</i>`
- **Underline**: `<u>text</u>`
- **Strikethrough**: `<s>text</s>`
- **Color**: `<color=#FF0000>text</color>`
- **Size**: `<size=150%>text</size>`
- **Letter Spacing**: `<cspace=10>text</cspace>`
- **Character Width**: `<cwidth=1em>text</cwidth>`

### RTL Languages
- Arabic (العربية)
- Hebrew (עברית)
- Bidirectional mixed text

### Interactive Links
- Click events with `LinkModifier.LinkClicked`
- Hover events with `LinkModifier.LinkEntered` / `LinkModifier.LinkExited`

### Mentions, Hashtags, Spoilers
- Mention chip: `CompositeModifier` containing persistent `HighlightModifier`, `FillModifier`, and `InteractiveModifier`, selected by `TriggerWordParseRule("@")`
- Hashtag: `InteractiveModifier` + `TriggerWordParseRule("#")`, with a gradient/glyph-mapped hover highlight and a `PropertyEffect` that animates its persistent fill to a dark colour
- One `InteractiveModifier.Interaction` subscription receives activation, context/long press, hover, state, identity, pointer coordinates, payload, hit segment, and anchor bounds
- `SpoilerModifier` + `TagRule("spoiler")` — tap-to-reveal state published through `SpoilerModifier.RevealedSignal`; the cover is an ordinary signal-driven `HighlightModifier`

### Find-in-Page (search API + range styles)
- `UniTextBase.FindAll(query, comparison, results)` — codepoint-correct case-insensitive search over the rendered text, zero-alloc
- Two `MutableRangeSource`s hold all matches and the current match
- Each source is paired with an ordinary `HighlightModifier` Style; Paint, gradients, textures, geometry mapping and priority use the same authoring path as every other visual modifier

### Highlight Geometry Mapping
- `GeometryMapping.Glyph` — one rounded surface per shaped glyph cluster
- `GeometryMapping.Line` — independently rounded surfaces for every wrapped visual line fragment
- `GeometryMapping.Range` — line fragments with rounded logical endpoints and square internal wrap edges
- `GeometryMapping.Block` — one connected multi-line mesh without internal seams
- The slide animates container width while keeping `PaintMapping.Range`, isolating topology from paint projection

### Highlight Paint and Animation
- Named gradient and texture swatches come from the sample's `Demo Paints` asset
- The slide demonstrates range-mapped neon, a moving shine band, tiled texture scrolling, and animated tint
- Text fill is composed with each highlight so every paint remains legible

### Language (`<lang>` + `UniText.Language`)
- Per-range OpenType `locl` activation
- Whole-text language via component property

### Font (`<font>` + `FontFamily.name`)
- Per-range font override from a named family in the FontStack
- Whole-text family via `SetWholeText<FontModifier>`

### Mathematical typesetting
- OpenType MATH metrics, glyph variants and delimiter assemblies
- Fractions, radicals, scripts, limits, accents, rules, matrices and aligned environments
- Complete command walls for every symbol registered by the parser

The sample includes `Fonts/STIXTwoMath-Regular.otf` under the SIL Open Font License 1.1
and a ready-to-use `STIXTwoMath-Regular` `UniTextFont` assigned to `BasicUsageExample.Math Font`.

### System Font Fallback
- Automatic OS font resolution when both Font and Font Stack are unassigned
- Mixed-script Unicode stress coverage across modern, historic, RTL, CJK, emoji, and symbol ranges

## CJK / locl demo fonts

The Language example renders the same Han ideographs four times with different
language tags. To see visible regional glyph differences you need a font that
ships `locl` GSUB substitutions for CJK ideographs.

A ready subset of **Adobe Source Han Sans** (Japanese default + locl covering
`ZHS`/`ZHT`/`ZHH`/`KOR`) is included as `Fonts/SourceHanSans-Demo.otf` (~96 KB).
It covers 15 hand-picked CJK codepoints with strong visual differences between
regions: `直骨雪今家字漢社海高神真食言會學`.

To use it:
1. Create a `UniTextFont` asset from the `.otf` (UniText → Tools → Import Font).
2. Add it as the primary of a `FontFamily` in your `UniTextFontStack`.
3. Navigate to the Language example — the four rows should render distinct glyphs.

The subset license (SIL OFL 1.1) lives next to the file as
`SourceHanSans-LICENSE.txt` and is redistributable under the same terms.

## Scene Setup

1. Create a Canvas (UI → Canvas)
2. Add two UniText components:
   - **DemoText** — Main text display (center of screen)
   - **StatusText** — Status bar (bottom of screen)
3. Add `BasicUsageExample` script to any GameObject
4. Assign both UniText components to the script

## Controls

- **Space** or **→** — Next example
- **←** — Previous example
- **Click** on links — Opens URL

## Key Code Concepts

### Registering Modifiers at Runtime

```csharp
var style = new Style
{
    Modifier = new ColorModifier(),
    Source = new TagRule("color")
};
uniText.Styles.Add(style);
```

### Handling Link Events

```csharp
var linkModifier = new LinkModifier();
uniText.Styles.Add(new Style
{
    Modifier = linkModifier,
    Source = new TagRule("link")
});

linkModifier.LinkClicked += url => Debug.Log($"Clicked: {url}");
linkModifier.LinkEntered += url => Debug.Log($"Hovering: {url}");
linkModifier.LinkExited += () => Debug.Log("Exited link");

linkModifier.Interaction += interaction =>
    Debug.Log($"{interaction.Kind}: {interaction.Range.PrimaryValue} at {interaction.AnchorRect}");
```

### Changing Text at Runtime

```csharp
uniText.Text = "<b>Bold</b> and <color=#FF0000>Red</color>";
```

## Scripts

- `BasicUsageExample.cs` — Scene entry point
- `BasicUsageExampleBase.cs` — Shared navigation and text-input paths
- `Slides/*Slide.cs` — One file per slide, including its text and behavior
