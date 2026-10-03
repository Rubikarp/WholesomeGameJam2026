using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Static registry of per-type clipboard schemas: which HTML elements, Markdown
    /// delimiters, and canonical source tag each <see cref="BaseModifier"/> type speaks.
    /// The schema is type-level metadata — instances have no say in it — so it lives here,
    /// not on the modifier: modifiers stay clipboard-ignorant, registration is explicit
    /// (no assembly scan, nothing for IL2CPP stripping to break), and the first paste
    /// costs nothing. Built-ins are pre-registered; integrator modifiers join via
    /// <see cref="Register{T}"/>.
    /// </summary>
    /// <remarks>
    /// Consulted by <see cref="TagHtmlClipboardAdapter"/> and
    /// <see cref="MarkdownClipboardAdapter"/> on copy (schema of each range's modifier) and on paste,
    /// where the canonical tag / marker of each format is matched against the destination's styles: a
    /// format the field renders is kept, one it does not is stripped to plain.
    /// </remarks>
    public static class ClipboardModifierBindMap
    {
        /// <summary>One entry in the bind map: the modifier type plus its schema.</summary>
        public readonly struct BindEntry
        {
            public Type ModifierType { get; }
            public ModifierClipboardSchema Schema { get; }

            public BindEntry(Type modifierType, ModifierClipboardSchema schema)
            {
                ModifierType = modifierType;
                Schema = schema;
            }
        }

        private static readonly object initLock = new();
        private static volatile bool initialized;
        private static readonly List<BindEntry> entries = new(16);
        private static readonly IReadOnlyList<BindEntry> entriesView = entries.AsReadOnly();
        private static readonly Dictionary<Type, BindEntry> byType = new(16);

        /// <summary>Every registered modifier schema entry, in registration order.</summary>
        public static IReadOnlyList<BindEntry> Entries
        {
            get
            {
                EnsureInitialized();
                return entriesView;
            }
        }

        /// <summary>The schema registered for the modifier's type, or <see langword="null"/>.</summary>
        public static ModifierClipboardSchema GetSchema(BaseModifier modifier)
            => modifier == null ? null : GetSchema(modifier.GetType());

        /// <summary>The schema registered for the type, or <see langword="null"/>.</summary>
        public static ModifierClipboardSchema GetSchema(Type modifierType)
        {
            EnsureInitialized();
            return modifierType != null && byType.TryGetValue(modifierType, out var entry)
                ? entry.Schema
                : null;
        }

        /// <summary>
        /// Registers a clipboard schema for a modifier type. Built-ins are pre-registered;
        /// call once (a static initializer is a good place) for an integrator modifier —
        /// without registration its ranges copy as plain text and rich pastes strip its
        /// formatting to plain text. A schema needs a
        /// <see cref="ModifierClipboardSchema.CanonicalTagName"/> or
        /// <see cref="ModifierClipboardSchema.MatchesSourceTagName"/> to land — anything
        /// else is rejected with a warning; repeat registrations of the same type are
        /// no-ops. Main thread only.
        /// </summary>
        public static void Register<T>(ModifierClipboardSchema schema) where T : BaseModifier
        {
            EnsureInitialized();
            lock (initLock)
            {
                AddEntry(typeof(T), schema);
            }
        }

        /// <summary>Forces built-in registration if it has not happened yet. Idempotent.</summary>
        public static void EnsureInitialized()
        {
            if (initialized) return;
            lock (initLock)
            {
                if (initialized) return;
                RegisterBuiltIns();
                initialized = true;
            }
        }

        private static void RegisterBuiltIns()
        {
            AddEntry(typeof(BoldModifier), ModifierClipboardSchema.InlineFormatStyled(new ModifierMarkdownSchema("**", "**", "__"), ModifierClipboardSchema.fontWeightToggle, "b", "strong", "span"));
            AddEntry(typeof(ItalicModifier), ModifierClipboardSchema.InlineFormatStyled(new ModifierMarkdownSchema("*", "*", "_"), ModifierClipboardSchema.fontStyleItalicToggle, "i", "em", "span"));
            AddEntry(typeof(UnderlineModifier), ModifierClipboardSchema.InlineFormatStyled(null, ModifierClipboardSchema.TextDecorationToggle("underline"), "u", "ins", "span"));
            AddEntry(typeof(StrikethroughModifier), ModifierClipboardSchema.InlineFormatStyled(new ModifierMarkdownSchema("~~"), ModifierClipboardSchema.TextDecorationToggle("line-through"), "s", "del", "strike", "span"));
            AddEntry(typeof(ColorModifier), ModifierClipboardSchema.InlineStyle("color", CssValueFormat.Color));
            AddEntry(typeof(SizeModifier), ModifierClipboardSchema.InlineStyle("font-size", CssValueFormat.Length));
            AddEntry(typeof(FontModifier), ModifierClipboardSchema.InlineStyle("font-family"));
            AddEntry(typeof(LinkModifier), ModifierClipboardSchema.InlineWithAttribute("a", "href", canonicalTagName: "link", markdown: ModifierMarkdownSchema.LinkSyntax));
            AddEntry(typeof(LanguageModifier), ModifierClipboardSchema.InlineWithAttribute("span", "lang", canonicalTagName: "lang"));
            AddEntry(typeof(LineHeightModifier), ModifierClipboardSchema.InlineStyle("line-height", CssValueFormat.LineHeight));
            AddEntry(typeof(LetterSpacingModifier), ModifierClipboardSchema.InlineStyle("letter-spacing", CssValueFormat.Length));
            AddEntry(typeof(ScriptPositionModifier), ModifierClipboardSchema.InlineMatchingElements("sup", "sub"));
            AddEntry(typeof(SmallCapsModifier), ModifierClipboardSchema.InlineStyleValue("font-variant", "small-caps"));
            AddEntry(typeof(FontFeatureModifier), ModifierClipboardSchema.InlineStyle("font-feature-settings"));
            AddEntry(typeof(LowercaseModifier), ModifierClipboardSchema.InlineStyleValue("text-transform", "lowercase"));
            AddEntry(typeof(UppercaseModifier), ModifierClipboardSchema.InlineStyleValue("text-transform", "uppercase"));
        }

        private static void AddEntry(Type type, ModifierClipboardSchema schema)
        {
            if (schema == null || (string.IsNullOrEmpty(schema.CanonicalTagName) && !schema.MatchesSourceTagName))
            {
                UnityEngine.Debug.LogWarning($"[UniText] Clipboard schema for {type?.Name} rejected: it declares neither CanonicalTagName nor MatchesSourceTagName, so the paste gate cannot match it against the field's styles.");
                return;
            }
            if (byType.ContainsKey(type)) return;

            var entry = new BindEntry(type, schema);
            entries.Add(entry);
            byType[type] = entry;
        }
    }
}
