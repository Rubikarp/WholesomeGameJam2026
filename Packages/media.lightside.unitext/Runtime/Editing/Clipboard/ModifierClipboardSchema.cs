using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Per-modifier-type declaration of how the modifier's semantic effect maps onto
    /// external clipboard formats, registered in <see cref="ClipboardModifierBindMap"/> to
    /// opt the type into HTML / Markdown round-trip; the active clipboard adapters
    /// (<see cref="IClipboardAdapter"/>) walk this schema set when serializing copy and
    /// matching elements on paste.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The schema is the semantic ↔ external-syntax bridge. Source-side syntax
    /// (<c>&lt;b&gt;...&lt;/b&gt;</c> for <see cref="TagParseRule"/>,
    /// <c>**...**</c> for a Markdown rule) stays a rule-side concern — multiple rules can
    /// drive the same modifier, and only the rule knows its own tag name. The modifier
    /// owns the external mapping because the external mapping is determined by what the
    /// modifier <em>means</em>, not by how it is written in UniText source.
    /// </para>
    /// <para>
    /// Industry parallels: ProseMirror's <c>MarkSpec.toDOM/parseDOM</c> and Lexical's
    /// <c>LexicalNode.exportDOM/importDOM</c> put the external-format mapping on the
    /// semantic unit, not on the parser — UniText follows the same shape.
    /// </para>
    /// </remarks>
    public sealed class ModifierClipboardSchema
    {
        /// <summary>HTML element mapping. <see langword="null"/> when the modifier has no HTML representation.</summary>
        public ModifierHtmlSchema Html { get; }

        /// <summary>Markdown delimiter mapping. <see langword="null"/> when the modifier has no Markdown representation.</summary>
        public ModifierMarkdownSchema Markdown { get; }

        /// <summary>
        /// UniText source-side tag name for this modifier — the name the paste gate matches against the field's
        /// styles. E.g. <c>"b"</c> for bold,
        /// <c>"color"</c> for color, <c>"link"</c> for links. Differs from
        /// <see cref="ModifierHtmlSchema.RecognizedElements"/> when the HTML element name does not match the
        /// conventional UniText tag (color uses <c>&lt;span style="color:..."&gt;</c> in HTML but
        /// <c>&lt;color=...&gt;</c> in UniText source). <see langword="null"/> when the modifier has no canonical tag.
        /// </summary>
        public string CanonicalTagName { get; }

        /// <summary>
        /// When set, the modifier's UniText tag names ARE its HTML element names, per style
        /// (<see cref="InlineMatchingElements"/>): copy emits the matched style's own tag as
        /// the element via the <c>{tagName}</c> template placeholder, and paste of an element
        /// applies only the style whose tag equals it. Such a schema has no single
        /// <see cref="CanonicalTagName"/> — one modifier drives several paired styles
        /// (<c>sup</c> and <c>sub</c>).
        /// </summary>
        public bool MatchesSourceTagName { get; }

        public ModifierClipboardSchema(ModifierHtmlSchema html = null, ModifierMarkdownSchema markdown = null, string canonicalTagName = null, bool matchesSourceTagName = false)
        {
            Html = html;
            Markdown = markdown;
            CanonicalTagName = canonicalTagName;
            MatchesSourceTagName = matchesSourceTagName;
        }

        /// <summary>
        /// Shortcut for a schema that only declares an HTML representation. Suits the
        /// common case of inline modifiers paired with a single open/close element pair.
        /// </summary>
        public static ModifierClipboardSchema DoHtml(string openSyntax, string closeSyntax, params string[] recognizedElements)
            => new(html: new ModifierHtmlSchema(openSyntax, closeSyntax, recognizedElements));

        /// <summary>
        /// Shortcut for parameterless inline modifiers with HTML representation only.
        /// The first recognised element name builds the open and close tags. Pass
        /// synonyms (e.g. <c>"b", "strong"</c>) after the primary element.
        /// </summary>
        public static ModifierClipboardSchema HtmlInline(params string[] recognizedElements)
            => new(html: BuildInlineHtml(recognizedElements), canonicalTagName: CanonicalFrom(recognizedElements));

        /// <summary>
        /// Shortcut for parameterless inline modifiers with both HTML and Markdown
        /// representations. The HTML side mirrors <see cref="HtmlInline"/>; the
        /// Markdown side wraps the inner text with <paramref name="markdownMarker"/>
        /// on both ends (e.g. <c>**</c> for bold, <c>*</c> for italic,
        /// <c>~~</c> for strikethrough).
        /// </summary>
        public static ModifierClipboardSchema InlineFormat(string markdownMarker, params string[] recognizedElements)
            => new(
                html: BuildInlineHtml(recognizedElements),
                markdown: new ModifierMarkdownSchema(markdownMarker),
                canonicalTagName: CanonicalFrom(recognizedElements));

        /// <summary>
        /// Inline format recognised by its semantic elements AND by CSS on a <c>span</c>
        /// (the Google-Docs clipboard convention), where the CSS off-value also CANCELS a
        /// semantic element match — the Docs <c>&lt;b style="font-weight:normal"&gt;</c>
        /// fragment wrapper must not bold its content. Copy emits the first element;
        /// <paramref name="markdown"/> may be <see langword="null"/> for HTML-only
        /// formats (underline).
        /// </summary>
        public static ModifierClipboardSchema InlineFormatStyled(ModifierMarkdownSchema markdown, HtmlParameterExtractor extractParameter, params string[] recognizedElements)
        {
            var html = BuildInlineHtml(recognizedElements);
            return new(
                html: new ModifierHtmlSchema(html.OpenTagTemplate, html.CloseTag, extractParameter, recognizedElements),
                markdown: markdown,
                canonicalTagName: CanonicalFrom(recognizedElements));
        }

        /// <summary>
        /// Bold via <c>font-weight</c>: <c>bold</c>/<c>bolder</c> or a numeric weight ≥ 600
        /// matches; an explicit lighter value cancels even on <c>b</c>/<c>strong</c>; a
        /// <c>span</c> without the property is not bold.
        /// </summary>
        internal static readonly HtmlParameterExtractor fontWeightToggle = static (element, attrs) =>
        {
            var value = ExtractCssProperty(attrs, "font-weight");
            if (value == null) return IsSpan(element) ? null : string.Empty;
            if (value.IndexOf("bold", StringComparison.OrdinalIgnoreCase) >= 0) return string.Empty;
            return int.TryParse(value, out var weight) && weight >= 600 ? string.Empty : null;
        };

        internal static readonly HtmlParameterExtractor fontStyleItalicToggle = static (element, attrs) =>
        {
            var value = ExtractCssProperty(attrs, "font-style");
            if (value == null) return IsSpan(element) ? null : string.Empty;
            return value.IndexOf("italic", StringComparison.OrdinalIgnoreCase) >= 0
                   || value.IndexOf("oblique", StringComparison.OrdinalIgnoreCase) >= 0
                ? string.Empty
                : null;
        };

        internal static HtmlParameterExtractor TextDecorationToggle(string token) => (element, attrs) =>
        {
            var value = ExtractCssProperty(attrs, "text-decoration-line")
                        ?? ExtractCssProperty(attrs, "text-decoration");
            if (value == null) return IsSpan(element) ? null : string.Empty;
            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ? string.Empty : null;
        };

        private static bool IsSpan(string element)
            => element.EqualsIgnoreCase("span");

        /// <summary>
        /// Shortcut for a schema that only declares a Markdown representation. Same open
        /// and close marker for both sides (e.g. <c>**</c> for bold, <c>*</c> for italic).
        /// </summary>
        public static ModifierClipboardSchema MarkdownDelimited(string marker, string canonicalTagName = null)
            => new(markdown: new ModifierMarkdownSchema(marker), canonicalTagName: canonicalTagName);

        private static string CanonicalFrom(string[] recognizedElements)
            => recognizedElements != null && recognizedElements.Length > 0 ? recognizedElements[0] : null;

        /// <summary>
        /// Shortcut for parameterised modifiers that map to a single CSS property on a
        /// <c>&lt;span&gt;</c>. Copy emits <c>&lt;span style="PROP:VALUE"&gt;</c> with the
        /// modifier's tag parameter as <c>VALUE</c>; paste recognises any
        /// <c>&lt;span&gt;</c> whose <c>style</c> attribute carries <c>PROP:</c> and
        /// returns the property value back as the UniText tag parameter. Used by color,
        /// size, letter-spacing, line-height, etc. <paramref name="valueFormat"/> selects the
        /// value converters bridging UniText's unit grammar and CSS in both directions —
        /// values with no valid rendering on the other side are skipped, never emitted
        /// invalid or fed unparseable into a rule.
        /// </summary>
        public static ModifierClipboardSchema InlineStyle(string cssProperty, params string[] additionalElements)
            => InlineStyle(cssProperty, CssValueFormat.Verbatim, additionalElements);

        /// <inheritdoc cref="InlineStyle(string, string[])"/>
        public static ModifierClipboardSchema InlineStyle(string cssProperty, CssValueFormat valueFormat, params string[] additionalElements)
        {
            var elements = additionalElements != null && additionalElements.Length > 0
                ? StateArray.Insert(additionalElements, 0, "span")
                : new[] { "span" };
            var fromCss = CssValueConversion.FromCssConverter(valueFormat);
            return new(
                html: new ModifierHtmlSchema(
                    "<span style=\"" + cssProperty + ":{value}\">",
                    "</span>",
                    (element, attrs) =>
                    {
                        var value = ExtractCssProperty(attrs, cssProperty);
                        if (value == null || fromCss == null) return value;
                        return fromCss(value);
                    },
                    CssValueConversion.ToCssConverter(valueFormat),
                    elements),
                canonicalTagName: cssProperty);
        }

        /// <summary>
        /// Shortcut for modifiers whose UniText tag name matches the HTML element
        /// name directly (one schema covers <c>&lt;sup&gt;/&lt;sub&gt;</c>,
        /// <c>&lt;mark&gt;</c>, <c>&lt;kbd&gt;</c>, etc.). The active style's tag name is
        /// spliced into the open and close tag templates via the literal placeholder
        /// <c>{tagName}</c>, so the same modifier can drive multiple paired styles
        /// (one Style for <c>sup</c>, another for <c>sub</c>) without separate schemas.
        /// </summary>
        public static ModifierClipboardSchema InlineMatchingElements(params string[] recognizedElements)
        {
            if (recognizedElements == null || recognizedElements.Length == 0)
                throw new ArgumentException("Must provide at least one element name.", nameof(recognizedElements));
            return new(
                html: new ModifierHtmlSchema("<{tagName}>", "</{tagName}>", recognizedElements),
                matchesSourceTagName: true);
        }

        /// <summary>
        /// Shortcut for parameterless modifiers whose presence is signalled by a fixed
        /// CSS property/value pair on a <c>&lt;span&gt;</c> — e.g. uppercase via
        /// <c>text-transform:uppercase</c>, small-caps via <c>font-variant:small-caps</c>.
        /// Copy emits a literal <c>&lt;span style="PROP:VALUE"&gt;</c>; paste matches a
        /// <c>&lt;span&gt;</c> whose style declares the same property with the same
        /// value (case-insensitive comparison).
        /// </summary>
        public static ModifierClipboardSchema InlineStyleValue(string cssProperty, string cssValue, params string[] additionalElements)
        {
            var elements = additionalElements != null && additionalElements.Length > 0
                ? StateArray.Insert(additionalElements, 0, "span")
                : new[] { "span" };
            return new(
                html: new ModifierHtmlSchema(
                    "<span style=\"" + cssProperty + ":" + cssValue + "\">",
                    "</span>",
                    (element, attrs) => ExtractCssProperty(attrs, cssProperty).EqualsIgnoreCase(cssValue)
                        ? string.Empty
                        : null,
                    elements),
                canonicalTagName: cssValue);
        }

        /// <summary>
        /// Shortcut for inline modifiers that map to a single HTML attribute on a
        /// dedicated element — e.g. <c>&lt;a href="URL"&gt;</c> for link,
        /// <c>&lt;abbr title="…"&gt;</c> for abbreviation, <c>&lt;cite&gt;</c> for citation.
        /// Copy emits <c>&lt;ELEMENT ATTR="VALUE"&gt;</c>; paste returns the attribute
        /// value back as the UniText tag parameter.
        /// </summary>
        public static ModifierClipboardSchema InlineWithAttribute(string element, string attribute,
            string canonicalTagName = null, ModifierMarkdownSchema markdown = null)
            => new(
                html: new ModifierHtmlSchema(
                    "<" + element + " " + attribute + "=\"{value}\">",
                    "</" + element + ">",
                    (el, attrs) => ExtractAttribute(attrs, attribute),
                    element),
                markdown: markdown,
                canonicalTagName: canonicalTagName ?? element);

        /// <summary>
        /// Reads a single HTML attribute value from a raw element attribute slice. Handles
        /// surrounding whitespace (including the newline-separated attributes Word emits),
        /// both quote styles around the value, and name substrings inside other attribute
        /// names or values (<c>data-style</c>, <c>class="header-style"</c>) — a candidate
        /// that fails the boundary or <c>=</c> check moves on to the next occurrence instead
        /// of giving up. The value is entity-decoded. Returns <see langword="null"/> when
        /// the attribute is not present.
        /// </summary>
        public static string ExtractAttribute(string attributesRaw, string attribute)
        {
            if (string.IsNullOrEmpty(attributesRaw) || string.IsNullOrEmpty(attribute)) return null;
            int idx = IndexOfIgnoreCase(attributesRaw, attribute);
            while (idx >= 0)
            {
                int before = idx - 1;
                if (before >= 0 && !Ascii.IsWhitespace(attributesRaw[before]))
                {
                    idx = IndexOfIgnoreCase(attributesRaw, attribute, idx + attribute.Length);
                    continue;
                }
                int after = idx + attribute.Length;
                while (after < attributesRaw.Length && Ascii.IsWhitespace(attributesRaw[after])) after++;
                if (after >= attributesRaw.Length || attributesRaw[after] != '=')
                {
                    idx = IndexOfIgnoreCase(attributesRaw, attribute, idx + attribute.Length);
                    continue;
                }
                after++;
                while (after < attributesRaw.Length && Ascii.IsWhitespace(attributesRaw[after])) after++;
                if (after < attributesRaw.Length && (attributesRaw[after] == '"' || attributesRaw[after] == '\''))
                {
                    char quote = attributesRaw[after];
                    int valStart = after + 1;
                    int valEnd = attributesRaw.IndexOf(quote, valStart);
                    if (valEnd >= 0)
                        return System.Net.WebUtility.HtmlDecode(attributesRaw.Substring(valStart, valEnd - valStart));
                }
                idx = IndexOfIgnoreCase(attributesRaw, attribute, idx + attribute.Length);
            }
            return null;
        }

        /// <summary>
        /// Reads a single CSS property value from a raw element attribute slice: locates the
        /// real <c>style</c> attribute via <see cref="ExtractAttribute"/> (boundary-checked and
        /// entity-decoded), then scans its semicolon-separated declarations for
        /// <paramref name="property"/> with a word-boundary check (so <c>font-size</c> never
        /// matches inside <c>x-font-size</c>). Returns <see langword="null"/> when the property
        /// is not present.
        /// </summary>
        public static string ExtractCssProperty(string attributesRaw, string property)
        {
            if (string.IsNullOrEmpty(property)) return null;
            var styleValue = ExtractAttribute(attributesRaw, "style");
            if (string.IsNullOrEmpty(styleValue)) return null;

            int propIdx = IndexOfIgnoreCase(styleValue, property);
            while (propIdx >= 0)
            {
                int afterProp = propIdx + property.Length;
                while (afterProp < styleValue.Length && Ascii.IsWhitespace(styleValue[afterProp])) afterProp++;
                if (afterProp < styleValue.Length && styleValue[afterProp] == ':'
                    && (propIdx == 0 || styleValue[propIdx - 1] == ';' || Ascii.IsWhitespace(styleValue[propIdx - 1])))
                {
                    int valueStart = afterProp + 1;
                    int valueEnd = styleValue.IndexOf(';', valueStart);
                    if (valueEnd < 0) valueEnd = styleValue.Length;
                    return styleValue.Substring(valueStart, valueEnd - valueStart).Trim();
                }
                propIdx = IndexOfIgnoreCase(styleValue, property, propIdx + property.Length);
            }
            return null;
        }

        private static int IndexOfIgnoreCase(string source, string value, int startIndex = 0)
            => source.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);

        private static ModifierHtmlSchema BuildInlineHtml(string[] recognizedElements)
        {
            if (recognizedElements == null || recognizedElements.Length == 0)
                throw new ArgumentException("Must provide at least one element name.", nameof(recognizedElements));
            var primary = recognizedElements[0];
            return new ModifierHtmlSchema("<" + primary + ">", "</" + primary + ">", recognizedElements);
        }
    }

    /// <summary>
    /// HTML element pair that a modifier produces on copy and recognises on paste.
    /// Wraps <see cref="OpenTagTemplate"/> + <see cref="CloseTag"/> around the inner
    /// text on copy; matches any of <see cref="RecognizedElements"/> on paste.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Templates may include the literal placeholder <c>{value}</c> which is substituted
    /// with the modifier's tag parameter at copy time (e.g. <c>&lt;span style="color:{value}"&gt;</c>
    /// emits <c>&lt;span style="color:red"&gt;</c> when the modifier was applied with
    /// parameter <c>red</c>). Parameterless modifiers (bold, italic) leave the template
    /// without a placeholder.
    /// </para>
    /// <para>
    /// <see cref="RecognizedElements"/> are case-insensitive HTML element names accepted
    /// on paste. A bold modifier typically declares <c>{"b", "strong"}</c>; an italic
    /// modifier <c>{"i", "em"}</c>. The first match wins — list semantically equivalent
    /// element names together rather than registering one schema per name.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Pulls a modifier parameter out of a matched HTML element. Receives the element name
    /// and the raw attribute slice between it and the closing <c>&gt;</c> (e.g. <c>span</c>
    /// and <c> style="color:red"</c> for <c>&lt;span style="color:red"&gt;</c>). Returns the
    /// parameter to splice into the UniText source tag (<see cref="string.Empty"/> for a
    /// parameterless match), or <see langword="null"/> when the element does not actually
    /// carry this modifier (a <c>&lt;span&gt;</c> without the expected CSS property, or a
    /// semantic element whose inline style cancels it).
    /// </summary>
    public delegate string HtmlParameterExtractor(string element, string attributesRaw);

    /// <summary>
    /// Converts a value between UniText's parameter grammar and a CSS value. Returns the
    /// converted value, or <see langword="null"/> when no valid rendering exists on the
    /// other side — the caller then skips the mapping instead of emitting invalid CSS or
    /// feeding an unparseable parameter into a rule.
    /// </summary>
    public delegate string CssValueConverter(string value);

    public sealed class ModifierHtmlSchema
    {
        /// <summary>HTML open tag emitted on copy. May contain the literal placeholder <c>{value}</c> for the modifier's tag parameter.</summary>
        public string OpenTagTemplate { get; }

        /// <summary>HTML close tag emitted on copy. Typically the matching <c>&lt;/element&gt;</c>.</summary>
        public string CloseTag { get; }

        /// <summary>Case-insensitive HTML element names this schema matches on paste. Never <see langword="null"/>; never empty.</summary>
        public IReadOnlyList<string> RecognizedElements { get; }

        /// <summary>
        /// Optional callback that derives the UniText modifier parameter from the raw
        /// attribute slice of a matched HTML element on paste. Receives the substring
        /// between the element name and the closing <c>&gt;</c> — e.g. for
        /// <c>&lt;span style="color:red"&gt;</c> the callback receives
        /// <c> style="color:red"</c>. Returns the parameter value to splice into the
        /// UniText source tag (e.g. <c>"red"</c>), or <see langword="null"/> when the
        /// element does not actually express this modifier (in which case the adapter
        /// keeps the inner content but does not wrap it). <see langword="null"/>
        /// callback means the modifier is parameterless.
        /// </summary>
        public HtmlParameterExtractor ExtractParameter { get; }

        /// <summary>
        /// Optional converter applied to the modifier's tag parameter before it substitutes
        /// <c>{value}</c> on copy. Returning <see langword="null"/> skips this schema's
        /// wrapper for the span (the content still copies) — invalid CSS is never emitted.
        /// <see langword="null"/> converter means the parameter is already a valid CSS value.
        /// </summary>
        public CssValueConverter ToCss { get; }

        /// <summary>Whether <see cref="OpenTagTemplate"/> carries the <c>{value}</c> placeholder — such a schema cannot emit without a parameter.</summary>
        public bool HasValuePlaceholder { get; }

        public ModifierHtmlSchema(string openTagTemplate, string closeSyntax, params string[] recognizedElements)
            : this(openTagTemplate, closeSyntax, null, null, recognizedElements) { }

        public ModifierHtmlSchema(string openTagTemplate, string closeSyntax, HtmlParameterExtractor extractParameter, params string[] recognizedElements)
            : this(openTagTemplate, closeSyntax, extractParameter, null, recognizedElements) { }

        public ModifierHtmlSchema(string openTagTemplate, string closeSyntax, HtmlParameterExtractor extractParameter, CssValueConverter toCss, params string[] recognizedElements)
        {
            OpenTagTemplate = openTagTemplate ?? string.Empty;
            CloseTag = closeSyntax ?? string.Empty;
            ExtractParameter = extractParameter;
            ToCss = toCss;
            HasValuePlaceholder = OpenTagTemplate.IndexOf("{value}", StringComparison.Ordinal) >= 0;
            RecognizedElements = recognizedElements != null && recognizedElements.Length > 0
                ? recognizedElements
                : new[] { ExtractElementName(openTagTemplate) };
        }

        private static string ExtractElementName(string openSyntax)
        {
            if (string.IsNullOrEmpty(openSyntax)) return string.Empty;
            int start = openSyntax.IndexOf('<') + 1;
            if (start <= 0 || start >= openSyntax.Length) return string.Empty;
            int end = start;
            while (end < openSyntax.Length)
            {
                char c = openSyntax[end];
                if ((c < 'a' || c > 'z') && (c < 'A' || c > 'Z') && (c < '0' || c > '9') && c != '-')
                    break;
                end++;
            }
            return end > start ? openSyntax.Substring(start, end - start).ToLowerInvariant() : string.Empty;
        }
    }

    /// <summary>
    /// How a modifier's Markdown form is written: a symmetric wrapping delimiter
    /// (<see cref="Wrap"/> — <c>**bold**</c>) or the inline link syntax
    /// (<see cref="Link"/> — <c>[text](url)</c>, where the URL is the modifier parameter).
    /// </summary>
    public enum MarkdownSyntaxKind { Wrap, Link }

    /// <summary>
    /// Markdown form a modifier produces on copy and recognises on paste. Wrapping formats
    /// use a delimiter pair (bold <c>**</c>, italic <c>*</c>, strike <c>~~</c>); the link form
    /// (<see cref="LinkSyntax"/>) carries the modifier parameter as the URL of
    /// <c>[text](url)</c>.
    /// </summary>
    public sealed class ModifierMarkdownSchema
    {
        /// <summary>Delimiter emitted before the inner text on copy. Empty for <see cref="MarkdownSyntaxKind.Link"/>.</summary>
        public string OpenMarker { get; }

        /// <summary>Delimiter emitted after the inner text on copy. Empty for <see cref="MarkdownSyntaxKind.Link"/>.</summary>
        public string CloseMarker { get; }

        /// <summary>
        /// Alternative symmetric delimiters recognised on paste for the same format —
        /// CommonMark's underscore aliases (<c>_</c> for <c>*</c>, <c>__</c> for <c>**</c>).
        /// Copy always emits <see cref="OpenMarker"/>/<see cref="CloseMarker"/>. Never
        /// <see langword="null"/>; empty when the format has one syntax.
        /// </summary>
        public IReadOnlyList<string> AliasMarkers { get; }

        /// <summary>Which Markdown syntax this modifier maps to.</summary>
        public MarkdownSyntaxKind Kind { get; }

        /// <summary>The <c>[text](url)</c> link form; the modifier parameter is the URL.</summary>
        public static readonly ModifierMarkdownSchema LinkSyntax = new(MarkdownSyntaxKind.Link);

        public ModifierMarkdownSchema(string marker) : this(marker, marker) { }

        public ModifierMarkdownSchema(string openMarker, string closeMarker, params string[] aliasMarkers)
        {
            OpenMarker = openMarker ?? string.Empty;
            CloseMarker = closeMarker ?? string.Empty;
            AliasMarkers = aliasMarkers ?? Array.Empty<string>();
            Kind = MarkdownSyntaxKind.Wrap;
        }

        private ModifierMarkdownSchema(MarkdownSyntaxKind kind)
        {
            OpenMarker = string.Empty;
            CloseMarker = string.Empty;
            AliasMarkers = Array.Empty<string>();
            Kind = kind;
        }
    }

    /// <summary>
    /// The value shape a CSS property carries, selecting the built-in converters that bridge
    /// UniText's unit grammar and CSS in both directions on the
    /// <see cref="ModifierClipboardSchema.InlineStyle(string, CssValueFormat, string[])"/> channels.
    /// </summary>
    public enum CssValueFormat
    {
        /// <summary>The parameter is already a valid CSS value and vice versa (font-family, color names / hex).</summary>
        Verbatim,
        /// <summary>A CSS length (font-size, letter-spacing): <c>px</c>/<c>%</c>/<c>em</c> pass through (a bare UniText px number gains <c>px</c> on copy; CSS <c>pt</c>/<c>rem</c> approximate to <c>px</c> on paste); deltas and keywords have no rendering.</summary>
        Length,
        /// <summary>A CSS line-height: like <see cref="Length"/>, but a unitless CSS value is the multiplier form and maps to UniText <c>em</c>.</summary>
        LineHeight,
        /// <summary>A CSS color: <c>rgb()</c>/<c>rgba()</c> functions convert to hex on paste; hex and named colors pass through.</summary>
        Color,
    }

    /// <summary>
    /// Built-in <see cref="CssValueConverter"/>s for <see cref="CssValueFormat"/>. UniText's unit
    /// grammar and CSS overlap on <c>px</c> / <c>%</c> / <c>em</c>; beyond that each direction
    /// converts what it can (pt and rem approximate to px, unitless line-height becomes <c>em</c>,
    /// <c>rgb()</c> becomes hex) and returns <see langword="null"/> for the rest — a value with no
    /// valid rendering on the other side is skipped, never emitted invalid.
    /// </summary>
    internal static class CssValueConversion
    {
        internal static CssValueConverter ToCssConverter(CssValueFormat format) => format switch
        {
            CssValueFormat.Length => LengthToCss,
            CssValueFormat.LineHeight => LengthToCss,
            _ => null,
        };

        internal static CssValueConverter FromCssConverter(CssValueFormat format) => format switch
        {
            CssValueFormat.Length => LengthFromCss,
            CssValueFormat.LineHeight => LineHeightFromCss,
            CssValueFormat.Color => ColorFromCss,
            _ => null,
        };

        private static string LengthToCss(string value)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v[^1] == '%')
                return ParsesAsNumber(v.AsSpan(0, v.Length - 1)) ? v : null;
            if (v.Length > 2 &&
                (v.EndsWith("em", StringComparison.OrdinalIgnoreCase) ||
                 v.EndsWith("px", StringComparison.OrdinalIgnoreCase)))
                return ParsesAsNumber(v.AsSpan(0, v.Length - 2)) ? v : null;
            if (v[0] == '+' || v[0] == '-') return null;
            return ParsesAsNumber(v) ? v + "px" : null;
        }

        private static string LengthFromCss(string value) => FromCssLength(value, unitlessIsMultiplier: false);

        private static string LineHeightFromCss(string value) => FromCssLength(value, unitlessIsMultiplier: true);

        private static string FromCssLength(string value, bool unitlessIsMultiplier)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                return ParsesAsNumber(v.AsSpan(0, v.Length - 2)) ? v : null;
            if (v.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
                return TryNumber(v.AsSpan(0, v.Length - 2), out var pt) ? Format(pt * (96f / 72f)) + "px" : null;
            if (v.EndsWith("rem", StringComparison.OrdinalIgnoreCase))
                return TryNumber(v.AsSpan(0, v.Length - 3), out var rem) ? Format(rem * 16f) + "px" : null;
            if (v.EndsWith("em", StringComparison.OrdinalIgnoreCase))
                return ParsesAsNumber(v.AsSpan(0, v.Length - 2)) ? v : null;
            if (v[^1] == '%')
                return ParsesAsNumber(v.AsSpan(0, v.Length - 1)) ? v : null;
            if (!TryNumber(v, out var bare)) return null;
            return Format(bare) + (unitlessIsMultiplier ? "em" : "px");
        }

        private static string ColorFromCss(string value)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (!v.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)) return v;

            var open = v.IndexOf('(');
            var close = v.LastIndexOf(')');
            if (open < 0 || close <= open) return null;
            var parts = v.Substring(open + 1, close - open - 1)
                .Replace('/', ' ')
                .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return null;

            if (!TryColorChannel(parts[0], out var r)
                || !TryColorChannel(parts[1], out var g)
                || !TryColorChannel(parts[2], out var b)) return null;

            var sb = new System.Text.StringBuilder(9);
            sb.Append('#').Append(r.ToString("X2")).Append(g.ToString("X2")).Append(b.ToString("X2"));
            if (parts.Length >= 4 && TryAlphaChannel(parts[3], out var a) && a < 255)
                sb.Append(a.ToString("X2"));
            return sb.ToString();
        }

        private static bool TryColorChannel(string token, out int channel)
        {
            channel = 0;
            var t = token.Trim();
            var percent = t.Length > 0 && t[^1] == '%';
            if (!TryNumber(percent ? t.AsSpan(0, t.Length - 1) : t.AsSpan(), out var f)) return false;
            channel = Math.Clamp((int)MathF.Round(percent ? f * 2.55f : f), 0, 255);
            return true;
        }

        private static bool TryAlphaChannel(string token, out int alpha)
        {
            alpha = 255;
            var t = token.Trim();
            var percent = t.Length > 0 && t[^1] == '%';
            if (!TryNumber(percent ? t.AsSpan(0, t.Length - 1) : t.AsSpan(), out var f)) return false;
            alpha = Math.Clamp((int)MathF.Round(percent ? f * 2.55f : f * 255f), 0, 255);
            return true;
        }

        private static bool ParsesAsNumber(ReadOnlySpan<char> token)
            => float.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

        private static bool TryNumber(ReadOnlySpan<char> token, out float value)
            => float.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);

        private static string Format(float value)
            => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
