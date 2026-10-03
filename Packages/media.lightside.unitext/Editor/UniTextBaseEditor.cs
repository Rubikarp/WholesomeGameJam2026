using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    [Flags]
    internal enum TextViewMode
    {
        None = 0,
        Serialized = 1 << 0,
        Rendered = 1 << 1,
        Clean = 1 << 2,
    }

    internal abstract partial class UniTextBaseEditor : FullWidthEditor
    {
        private const string TextAreaFontSizePreference =
            "LightSide.UniText.Inspector.TextAreaFontSize";
        private const string TextViewsPreference =
            "LightSide.UniText.Inspector.TextViews";

        protected UniTextBase uniText;
        protected SerializedProperty textProp;
        protected SerializedProperty fontProp;
        protected SerializedProperty fontStackProp;
        protected SerializedProperty fontSizeProp;
        protected SerializedProperty wordWrapProp;
        protected SerializedProperty paddingProp;
        protected SerializedProperty horizontalAlignmentProp;
        protected SerializedProperty verticalAlignmentProp;
        protected SerializedProperty autoSizeProp;
        protected SerializedProperty minFontSizeProp;
        protected SerializedProperty maxFontSizeProp;
        protected SerializedProperty fitStepsProp;
        protected SerializedProperty stylesProp;
        protected SerializedProperty stylePresetsProp;
        protected SerializedProperty useGlobalStylePresetProp;
        protected SerializedProperty renderModeProp;

        private static bool textAreaExpand;
        private static int textAreaFontSize = 16;
        private static TextViewMode textViewMode = TextViewMode.Serialized;
        private static readonly Color dotColorNone = new(0.70f, 0.70f, 0.70f);
        private static readonly Color dotColorSetText = new(0.98f, 0.64f, 0.20f);
        private static readonly Color dotColorResolver = new(0.36f, 0.82f, 0.42f);

        protected virtual void OnEnable()
        {
            textAreaFontSize = EditorPrefs.GetInt(TextAreaFontSizePreference, 16);
            textViewMode = (TextViewMode)EditorPrefs.GetInt(
                TextViewsPreference, (int)TextViewMode.Serialized);

            SerializedProperty P(string name) =>
                InspectorHelpers.RequireProperty(serializedObject, name);

            textProp = P("text");
            fontProp = P("font");
            fontStackProp = P("fontStack");
            fontSizeProp = P("fontSize");
            wordWrapProp = P("wordWrap");
            paddingProp = P("padding");
            horizontalAlignmentProp = P("horizontalAlignment");
            verticalAlignmentProp = P("verticalAlignment");
            autoSizeProp = P("autoSize");
            minFontSizeProp = P("minFontSize");
            maxFontSizeProp = P("maxFontSize");
            fitStepsProp = P("fitSteps");
            stylesProp = P("styles");
            stylePresetsProp = P("stylePresets");
            useGlobalStylePresetProp = P("useGlobalStylePreset");
            renderModeProp = P("renderMode");

            SceneView.duringSceneGui += DrawPaddingAreaOnSceneView;
        }

        protected virtual void OnDisable()
        {
            SceneView.duringSceneGui -= DrawPaddingAreaOnSceneView;
            previewRefresh?.Pause();
        }

        private void DrawPaddingAreaOnSceneView(SceneView sceneView)
        {
            if (!target || targets.Length > 1 || !sceneView.drawGizmos) return;
            if (target is not UniTextBase value || value.Padding == Vector4.zero) return;

            var rectTransform = value.rectTransform;
            var previous = Handles.color;
            Handles.color = new Color(0.30f, 0.85f, 1.00f, 0.85f);
            DrawInsetRect(rectTransform.rect, rectTransform.transform, value.Padding);
            Handles.color = previous;
        }

        protected static void DrawInsetRect(Rect rect, Transform space, Vector4 inset)
        {
            var p0 = space.TransformPoint(new Vector2(rect.x + inset.x, rect.y + inset.y));
            var p1 = space.TransformPoint(new Vector2(rect.x + inset.x, rect.yMax - inset.w));
            var p2 = space.TransformPoint(new Vector2(rect.xMax - inset.z, rect.yMax - inset.w));
            var p3 = space.TransformPoint(new Vector2(rect.xMax - inset.z, rect.y + inset.y));
            Handles.DrawLine(p0, p1);
            Handles.DrawLine(p1, p2);
            Handles.DrawLine(p2, p3);
            Handles.DrawLine(p3, p0);
        }

        private static string LabelForOverride(TextOverrideSource state)
        {
            var resolver = (state & TextOverrideSource.Resolver) != 0;
            var setText = (state & TextOverrideSource.SetText) != 0;
            if (resolver && setText)
                return RichDot(dotColorResolver) + " Rendered (Resolver + SetText)";
            if (resolver)
                return RichDot(dotColorResolver) + " Rendered (Resolver override)";
            if (setText)
                return RichDot(dotColorSetText) + " Rendered (SetText override)";
            return RichDot(dotColorNone) + " Rendered (no override — same as Text)";
        }

        private static string RichDot(Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>●</color>";

        private static string ViewModeLabel(TextViewMode mode)
        {
            const TextViewMode all =
                TextViewMode.Serialized | TextViewMode.Rendered | TextViewMode.Clean;
            if (mode == TextViewMode.None) return "None";
            if (mode == all) return "All";
            var label = string.Empty;
            if ((mode & TextViewMode.Serialized) != 0) label = "Serialized";
            if ((mode & TextViewMode.Rendered) != 0)
                label += label.Length > 0 ? ", Rendered" : "Rendered";
            if ((mode & TextViewMode.Clean) != 0)
                label += label.Length > 0 ? ", Clean" : "Clean";
            return label;
        }

        private void ComputeStyleCountRange(out int minimum, out int maximum,
            out int globalCount)
        {
            var global = UniTextSettings.GlobalStylePreset;
            globalCount = global?.Styles.Count ?? 0;
            minimum = int.MaxValue;
            maximum = 0;
            for (var targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                var text = (UniTextBase)targets[targetIndex];
                var count = text.Styles.Count;
                var attached = text.StylePresets;
                for (var i = 0; i < attached.Count; i++)
                    if (attached[i] != null)
                        count += attached[i].Styles.Count;
                if (text.UseGlobalStylePreset && global != null) count += globalCount;
                minimum = Mathf.Min(minimum, count);
                maximum = Mathf.Max(maximum, count);
            }
            if (minimum == int.MaxValue) minimum = 0;
        }

        #region Style Presets

        private static Selector.SelectorItem[] stylePresetItems;
        private static Selector.SelectorItem[] toggleRowItems;

        private const string WholeTextGroup = "Whole Text";

        /// <summary>Toggle-row presets ordered by <see cref="StylePresetEntry.visualGroup"/>, independent of the category-sorted dropdown order of <see cref="GetStylePresetItems"/>.</summary>
        private static Selector.SelectorItem[] GetToggleRowItems()
        {
            if (toggleRowItems != null) return toggleRowItems;

            var row = new List<Selector.SelectorItem>();
            foreach (var item in GetStylePresetItems())
                if (((StylePresetEntry)item.value).visualGroup >= 0)
                    row.Add(item);
            StableSortBy(row, i => ((StylePresetEntry)i.value).visualGroup);

            toggleRowItems = row.ToArray();
            return toggleRowItems;
        }

        internal static Selector.SelectorItem[] GetStylePresetItems()
        {
            if (stylePresetItems != null) return stylePresetItems;

            var b = new PresetBuilder();
            b.Mod<BoldModifier>("Bold", common: true).WholeText(0).Tag("b").Markdown("**");
            b.Mod<ItalicModifier>("Italic", common: true).WholeText(0).Tag("i").Markdown("*");
            b.Mod<SizeModifier>("Size", common: true).WholeText().Tag("size");
            b.Mod<UppercaseModifier>("Uppercase").WholeText(1).Tag("upper");
            b.Mod<LowercaseModifier>("Lowercase").WholeText(1).Tag("lower");
            b.Mod<SmallCapsModifier>("Small Caps").WholeText(1).Tag("smallcaps");
            b.Mod<ColorModifier>("Color", common: true).WholeText().Tag("color");
            b.Mod<LetterSpacingModifier>("Letter Spacing").WholeText(1).Tag("cspace");
            b.Mod<CharacterWidthModifier>("Character Width").WholeText().Tag("cwidth");
            b.Mod<WordSpacingModifier>("Word Spacing").WholeText().Tag("wspace");
            b.Mod<LineHeightModifier>("Line Height").WholeText().Tag("line-height");
            b.Mod<ParagraphSpacingModifier>("Paragraph Spacing").WholeText().Tag("pspace");
            b.Mod<NoBreakModifier>("No Break").WholeText().Tag("nobr");
            b.Mod<SeparatorModifier>("Separator").Tag<SeparatorParseRule>();
            b.Mod<UnderlineModifier>("Underline", common: true).WholeText(3).Tag("u").Markdown("++");
            b.Mod<StrikethroughModifier>("Strikethrough", common: true).WholeText(3).Tag("s").Markdown("~~");
            b.LinkPresets("#4285F4");
            b.Mod<SpoilerModifier>("Spoiler").Tag("spoiler");
            b.Mod<EllipsisModifier>("Ellipsis").WholeText().Tag("ellipsis");
            b.Mod<TruncateModifier>("Truncate").WholeText().Tag("truncate");
            b.Mod<RevealModifier>("Reveal").WholeText().Tag("reveal");
            b.Mod<FillModifier>("Fill", common: true).WholeText(4);
            b.Mod<StrokeModifier>("Stroke", common: true).WholeText(4);
            b.Mod<ShadowModifier>("Shadow", common: true).WholeText(4);
            b.Mod<GlowModifier>("Glow", common: true).WholeText();
            b.Mod<InnerShadowModifier>("Inner Shadow", common: true).WholeText();
            b.Mod<ExtrudeModifier>("Extrude", common: true).WholeText();
            b.Mod<VariationModifier>("Variation").WholeText().Tag("var");
            b.Mod<FontFeatureModifier>("Font Features").WholeText().Tag("feature");
            b.Mod<FontModifier>("Font").WholeText().Tag("font");
            b.Mod<LanguageModifier>("Language").WholeText().Tag("lang");
            b.Mod<MathModifier>("Math").Tag<MathParseRule>();
            b.Mod<MaterialModifier>("Material").WholeText().Tag("mat");
            b.Mod<ObjModifier>("Inline Object").InlineTag("obj");
            b.Mod<SpriteModifier>("Inline Sprite").InlineTag("sprite");
            b.Mod<ListModifier>("List").Markdown<MarkdownListParseRule>();
            b.Mod<InteractiveModifier>("Interactive").Detect<TriggerWordParseRule>();
            b.CompleteStandalone();
            b.CompleteWholeText();

            stylePresetItems = b.Build().ToArray();
            return stylePresetItems;
        }

        /// <summary>Modifier categories in display order. "Common" is the curated most-used set; "Custom" holds the blank entry and modifiers without a <see cref="TypeGroupAttribute"/>.</summary>
        private static readonly string[] categoryOrder =
            { "Common", "Text Style", "Decoration", "Appearance", "Layout", "Interactive", "Inline", "Utility", "Animation", "Custom" };

        private static int CategoryRank(string category)
        {
            var i = Array.IndexOf(categoryOrder, category);
            return i >= 0 ? i : categoryOrder.Length;
        }

        private static string CategoryOf(Type modifierType) =>
            modifierType?.GetCustomAttribute<TypeGroupAttribute>()?.GroupName ?? "Custom";

        private static void StableSortBy(List<Selector.SelectorItem> list, Func<Selector.SelectorItem, int> key)
        {
            var keyed = new (int key, int index, Selector.SelectorItem item)[list.Count];
            for (var i = 0; i < list.Count; i++)
                keyed[i] = (key(list[i]), i, list[i]);
            Array.Sort(keyed, (a, b) => a.key != b.key ? a.key.CompareTo(b.key) : a.index.CompareTo(b.index));
            for (var i = 0; i < list.Count; i++)
                list[i] = keyed[i].item;
        }

        private static string FormatModifierDisplayName(Type type) =>
            ObjectNames.NicifyVariableName(ManagedReferenceTypeMenu.TrimSuffix(type.Name, type));

        private static bool IsStandaloneRule(Type type)
            => ((ParseRule)ManagedReferenceTypeMenu.Create(type)).IsStandalone;

        internal sealed class StylePresetEntry : ISelectorAccentSource
        {
            public Type modifierType;
            public Type ruleType;
            public string iconName;
            public Func<BaseModifier> createModifier;
            public Func<ParseRule> createRule;
            public Style existingStyle;
            public int visualGroup;
            /// <summary>Non-null only for an "already on this component" entry: the style's parameter, so applying it to a selection carries the value instead of a bare tag.</summary>
            public string defaultParameter;

            public object SelectorAccentKey => (object)modifierType ?? ruleType;
        }

        /// <summary>
        /// Collects entries into stable group buckets consumed by the shared selector hierarchy
        /// and its ranked search view.
        /// </summary>
        private sealed class PresetBuilder
        {
            public readonly List<Selector.SelectorItem> wholeText = new();
            public readonly List<Selector.SelectorItem> tags = new();
            public readonly List<Selector.SelectorItem> inlineTags = new();
            public readonly List<Selector.SelectorItem> markdown = new();
            private readonly List<Selector.SelectorItem> protection = new();

            public ModBuilder<TM> Mod<TM>(string name, string icon = null, bool common = false) where TM : BaseModifier, new() =>
                new(this, name, icon ?? EditorResources.GetTypeIconName(typeof(TM)), common);

            /// <summary>
            /// Emits the Link presets as a <see cref="CompositeModifier"/> of
            /// <see cref="LinkModifier"/> + <see cref="ColorModifier"/> + <see cref="UnderlineModifier"/>,
            /// so a link's colour and underline are ordinary, fully editable styles rather than baked into
            /// the link. The rule's default parameter (<c>;color</c>) seeds the colour slot; the authored
            /// URL fills the first slot, leaving the visual entirely user-controlled.
            /// </summary>
            public void LinkPresets(string color)
            {
                var icon = EditorResources.GetTypeIconName(typeof(LinkModifier));
                var def = ";" + color;

                static BaseModifier Make()
                {
                    var c = new CompositeModifier();
                    c.Modifiers.ReplaceAll(new BaseModifier[]
                    {
                        new LinkModifier(),
                        new ColorModifier(),
                        new UnderlineModifier(),
                    });
                    return c;
                }

                tags.Add(MakePreset("Link", "Tags/Common", CategoryRank("Common"),
                    typeof(LinkModifier), typeof(TagRule),
                    Make, () => new TagRule("link") { DefaultParameter = def }, icon,
                    secondaryText: "Tag · <link>"));

                markdown.Add(MakePreset("Link", "Markdown", 2,
                    typeof(LinkModifier), typeof(MarkdownLinkParseRule),
                    Make, () => new MarkdownLinkParseRule { DefaultParameter = def }, icon,
                    secondaryText: "Markdown · […](url)"));

                markdown.Add(MakePreset("Link (Raw URL)", "Markdown", 2,
                    typeof(LinkModifier), typeof(RawUrlParseRule),
                    Make, () => new RawUrlParseRule { DefaultParameter = def }, icon,
                    secondaryText: "Auto-detect · URL"));
            }

            /// <summary>Adds every standalone <see cref="ParseRule"/> not already curated, reusing <see cref="ManagedReferenceTypeMenu"/>'s discovery and grouping; re-sorted by group so each emits a single header in the trailing section.</summary>
            public void CompleteStandalone()
            {
                var presented = new HashSet<Type>();
                CollectRuleTypes(wholeText); CollectRuleTypes(tags); CollectRuleTypes(inlineTags);
                CollectRuleTypes(markdown); CollectRuleTypes(protection);

                var standalone = new List<ManagedReferenceTypeMenu.TypeEntry>();
                foreach (var e in ManagedReferenceTypeMenu.GetConcreteTypes(typeof(ParseRule)))
                    if (!presented.Contains(e.type) && IsStandaloneRule(e.type))
                        standalone.Add(e);
                standalone.Sort((a, b) =>
                {
                    var byGroup = string.Compare(a.groupName, b.groupName, StringComparison.Ordinal);
                    return byGroup != 0 ? byGroup : string.Compare(a.displayName, b.displayName, StringComparison.Ordinal);
                });

                foreach (var e in standalone)
                {
                    var capturedType = e.type;
                    protection.Add(MakePreset(e.displayName, e.groupName, e.groupOrder,
                        null, capturedType, null, () => (ParseRule)ManagedReferenceTypeMenu.Create(capturedType)));
                }

                void CollectRuleTypes(List<Selector.SelectorItem> bucket)
                {
                    foreach (var item in bucket)
                        if (item.value is StylePresetEntry sp && sp.ruleType != null)
                            presented.Add(sp.ruleType);
                }
            }

            /// <summary>Adds every concrete <see cref="BaseModifier"/> subclass that did not appear in a curated preset as a source-less whole-text style in its category, plus the blank "Custom..." entry.</summary>
            public void CompleteWholeText()
            {
                var presented = new HashSet<Type>();
                AddPresented(wholeText);
                AddPresented(tags);
                AddPresented(inlineTags);
                AddPresented(markdown);
                AddPresented(protection);

                var pending = new List<(string name, Type type)>();
                foreach (var entry in ManagedReferenceTypeMenu.GetConcreteTypes(typeof(BaseModifier)))
                {
                    if (presented.Contains(entry.type)) continue;
                    pending.Add((FormatModifierDisplayName(entry.type), entry.type));
                }
                pending.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

                foreach (var (name, type) in pending)
                {
                    var capturedType = type;
                    var category = CategoryOf(capturedType);
                    wholeText.Add(MakePreset(name, WholeTextGroup + "/" + category, -1 + CategoryRank(category),
                        capturedType, null,
                        () => (BaseModifier)ManagedReferenceTypeMenu.Create(capturedType),
                        null,
                        EditorResources.GetTypeIconName(capturedType),
                        secondaryText: WholeTextGroup));
                }

                wholeText.Add(MakePreset("Custom...", WholeTextGroup + "/Custom", -1 + CategoryRank("Custom"),
                    null, null, null, null, "custom", secondaryText: "Custom"));

                void AddPresented(List<Selector.SelectorItem> bucket)
                {
                    foreach (var item in bucket)
                        if (item.value is StylePresetEntry sp && sp.modifierType != null)
                            presented.Add(sp.modifierType);
                }
            }

            public List<Selector.SelectorItem> Build()
            {
                StableSortBy(wholeText, i => i.groupOrder);
                StableSortBy(tags, i => i.groupOrder);

                var items = new List<Selector.SelectorItem>(
                    wholeText.Count + tags.Count + inlineTags.Count + markdown.Count + protection.Count);
                items.AddRange(wholeText);
                items.AddRange(tags);
                items.AddRange(inlineTags);
                items.AddRange(markdown);
                items.AddRange(protection);
                return items;
            }
        }

        private readonly struct ModBuilder<TM> where TM : BaseModifier, new()
        {
            private readonly PresetBuilder presets;
            private readonly string name;
            private readonly string icon;
            private readonly bool common;

            public ModBuilder(PresetBuilder presets, string name, string icon, bool common)
            {
                this.presets = presets;
                this.name = name;
                this.icon = icon;
                this.common = common;
            }

            private string Category => common ? "Common" : CategoryOf(typeof(TM));

            public ModBuilder<TM> WholeText(int visualGroup = -1)
            {
                var category = Category;
                presets.wholeText.Add(MakePreset(name, WholeTextGroup + "/" + category, -1 + CategoryRank(category),
                    typeof(TM), null, () => new TM(), null, icon, visualGroup, WholeTextGroup));
                return this;
            }

            public ModBuilder<TM> Tag(string tag)
            {
                var category = Category;
                presets.tags.Add(MakePreset(name, "Tags/" + category, CategoryRank(category),
                    typeof(TM), typeof(TagRule), () => new TM(), () => new TagRule(tag), icon,
                    secondaryText: "Tag · <" + tag + ">"));
                return this;
            }

            public ModBuilder<TM> Tag<TR>() where TR : ParseRule, new()
            {
                var category = Category;
                presets.tags.Add(MakePreset(name, "Tags/" + category, CategoryRank(category),
                    typeof(TM), typeof(TR), () => new TM(), () => new TR(), icon,
                    secondaryText: "Tag · " + FormatRuleDisplayName(typeof(TR))));
                return this;
            }

            public ModBuilder<TM> InlineTag(string tag)
            {
                presets.inlineTags.Add(MakePreset(name, "Inline Tags", 1, typeof(TM), typeof(InlineTagRule),
                    () => new TM(), () => new InlineTagRule(tag), icon,
                    secondaryText: "Inline · <" + tag + ">"));
                return this;
            }

            public ModBuilder<TM> Detect<TR>() where TR : ParseRule, new()
            {
                presets.markdown.Add(MakePreset(name, "Auto-detect", 2, typeof(TM), typeof(TR),
                    () => new TM(), () => new TR(), icon,
                    secondaryText: "Auto-detect · " + FormatRuleDisplayName(typeof(TR))));
                return this;
            }

            public ModBuilder<TM> Markdown(string marker)
            {
                presets.markdown.Add(MakePreset(name, "Markdown", 2, typeof(TM), typeof(MarkdownWrapRule),
                    () => new TM(), () => new MarkdownWrapRule { Marker = marker }, icon,
                    secondaryText: "Markdown · " + marker + "…" + marker));
                return this;
            }

            public ModBuilder<TM> Markdown<TR>(string displayName = null) where TR : ParseRule, new()
            {
                presets.markdown.Add(MakePreset(displayName ?? name, "Markdown", 2, typeof(TM), typeof(TR),
                    () => new TM(), () => new TR(), icon,
                    secondaryText: "Markdown · " + FormatRuleDisplayName(typeof(TR))));
                return this;
            }
        }

        private static Selector.SelectorItem MakePreset(string name, string group, int order,
            Type modifierType, Type ruleType,
            Func<BaseModifier> createModifier, Func<ParseRule> createRule,
            string iconName = null, int visualGroup = -1, string secondaryText = null)
        {
            var modifierDescription = TypeDescription(modifierType);
            var ruleDescription = TypeDescription(ruleType);
            secondaryText ??= modifierType == null
                ? null
                : ruleType == null
                    ? WholeTextGroup
                    : FormatRuleDisplayName(ruleType);
            return new Selector.SelectorItem
            {
                displayName = name,
                searchText = BuildSearchText(name, group, secondaryText,
                    modifierType, ruleType, modifierDescription, ruleDescription),
                groupName = group,
                groupOrder = order,
                description = modifierDescription ?? ruleDescription,
                secondaryText = secondaryText,
                secondaryAccentKey = ruleType,
                details = BuildDetails(modifierType, ruleType,
                    modifierDescription, ruleDescription),
                value = new StylePresetEntry
                {
                    modifierType = modifierType,
                    ruleType = ruleType,
                    iconName = iconName,
                    createModifier = createModifier,
                    createRule = createRule,
                    visualGroup = visualGroup,
                }
            };
        }

        private static string TypeDescription(Type type)
            => ElementLabelUtility.Description(type);

        private static string FormatRuleDisplayName(Type type)
            => type == null
                ? WholeTextGroup
                : ObjectNames.NicifyVariableName(
                    ManagedReferenceTypeMenu.TrimSuffix(type.Name, type));

        private static string BuildSearchText(string name, string group, string secondary,
            Type modifierType, Type ruleType, string modifierDescription, string ruleDescription)
            => string.Join(" ", new[]
            {
                name,
                group,
                secondary,
                modifierType?.Name,
                ruleType?.Name,
                modifierDescription,
                ruleDescription,
            });

        private static string BuildDetails(Type modifierType, Type ruleType,
            string modifierDescription, string ruleDescription)
        {
            var modifier = modifierType == null
                ? null
                : "Modifier · " + FormatModifierDisplayName(modifierType) +
                  (string.IsNullOrWhiteSpace(modifierDescription)
                      ? string.Empty
                      : "\n" + modifierDescription);
            var rule = ruleType == null
                ? null
                : "Rule · " + FormatRuleDisplayName(ruleType) +
                  (string.IsNullOrWhiteSpace(ruleDescription)
                      ? string.Empty
                      : "\n" + ruleDescription);
            if (modifier != null && rule != null) return modifier + "\n\n" + rule;
            return modifier ?? rule;
        }

        internal static void ShowStylePresetSelector(Rect buttonRect, Object[] targets,
            SerializedObject so, Action<Object, Style> addStyle)
            => ShowStylePresetSelector(buttonRect, targets, so, addStyle, wrappableOnly: false, includeComponentStyles: false);

        internal static void ReplaceSerializedStyle(Object target, string propertyPath, Style style)
        {
            var state = new SerializedObject(target);
            state.UpdateIfRequiredOrScript();
            var property = InspectorHelpers.RequireProperty(state, propertyPath);
            new SerializedPropertyBinding(property).SetValue(style, "Change Style");
        }

        internal static void InsertSerializedStyle(Object target, string arrayPath, int index,
            Style style)
        {
            var state = new SerializedObject(target);
            state.UpdateIfRequiredOrScript();
            var property = InspectorHelpers.RequireProperty(state, arrayPath);
            new SerializedPropertyBinding(property).EditSerializedProperties(array =>
            {
                var insertion = Mathf.Clamp(index, 0, array.arraySize);
                array.InsertArrayElementAtIndex(insertion);
                WriteSerializedStyle(array.GetArrayElementAtIndex(insertion), style);
            }, "Change Style");
        }

        private static Style ReadSerializedStyle(SerializedProperty property)
            => property.boxedValue as Style;

        private static void WriteSerializedStyle(SerializedProperty property, Style style)
            => property.boxedValue = PropertyClipboard.CloneObject(style);

        private static Style ResolvePresetStyle(StylePresetEntry preset)
        {
            if (preset.existingStyle != null) return preset.existingStyle;
            return new Style
            {
                Modifier = preset.createModifier?.Invoke(),
                Source = preset.createRule?.Invoke(),
                DefaultParameter = preset.defaultParameter,
            };
        }

        /// <summary>
        /// Opens the shared style picker. <paramref name="wrappableOnly"/> keeps only entries whose rule can wrap
        /// a selection (the "apply to a range" case); <paramref name="includeComponentStyles"/> prepends the
        /// single target's styles already configured locally / via presets / global, so a range can reuse them.
        /// </summary>
        internal static void ShowStylePresetSelector(Rect buttonRect, Object[] targets, SerializedObject so,
            Action<Object, Style> addStyle, bool wrappableOnly, bool includeComponentStyles,
            Action onChanged = null)
        {
            var items = BuildStyleSelectorItems(targets, wrappableOnly, includeComponentStyles);
            var capturedTargets = targets;
            Selector.Show(buttonRect, items.ToArray(), null, value =>
            {
                var preset = (StylePresetEntry)value;
                foreach (var t in capturedTargets)
                {
                    if (t == null) continue;
                    addStyle(t, ResolvePresetStyle(preset));
                }
                so.UpdateIfRequiredOrScript();
                onChanged?.Invoke();
            });
        }

        private static List<Selector.SelectorItem> BuildStyleSelectorItems(Object[] targets, bool wrappableOnly, bool includeComponentStyles)
        {
            var items = new List<Selector.SelectorItem>();

            if (includeComponentStyles && targets.Length == 1 && targets[0] is UniTextBase text)
                AppendComponentStyleItems(items, text);

            foreach (var item in GetStylePresetItems())
            {
                var mp = (StylePresetEntry)item.value;
                if (wrappableOnly && !PresetIsWrappable(mp)) continue;

                var decorated = item;
                decorated.icon = mp.iconName != null
                    ? EditorResources.GetColoredTexture(mp.iconName,
                        EditorResources.GetAccentColor(mp, decorated.displayName))
                    : null;
                var path = decorated.groupName;
                var slash = string.IsNullOrEmpty(path) ? -1 : path.IndexOf('/');
                decorated.groupIcon = EditorResources.GetGroupIcon(slash >= 0 ? path.Substring(0, slash) : path);
                decorated.subGroupIcon = slash >= 0 ? EditorResources.GetGroupIcon(path.Substring(slash + 1)) : null;
                items.Add(decorated);
            }
            return items;
        }

        private static bool PresetIsWrappable(StylePresetEntry entry)
        {
            var rule = entry.createRule?.Invoke();
            return rule != null && rule.CanWrap;
        }

        private const string OnComponentGroup = "Already on this text";

        /// <summary>The component's live styles (local, preset, global) that can wrap a selection, each with a display name — the single source both the style picker and the scene-editing context menu list under "already on this text".</summary>
        internal static IEnumerable<(Style style, string displayName)> WrappableLiveStyles(UniTextBase text)
        {
            foreach (var style in text.EnumerateLiveStylesForEditor())
            {
                var modifier = style?.Modifier;
                if (modifier == null || style.Source is not ParseRule rule || !rule.CanWrap) continue;
                var name = FormatModifierDisplayName(modifier.GetType());
                if (!string.IsNullOrEmpty(style.DefaultParameter)) name += $"  ({style.DefaultParameter})";
                yield return (style, name);
            }
        }

        private static void AppendComponentStyleItems(List<Selector.SelectorItem> items, UniTextBase text)
        {
            foreach (var (style, name) in WrappableLiveStyles(text))
            {
                var modType = style.Modifier.GetType();
                var rule = (ParseRule)style.Source;
                var ruleType = rule.GetType();
                var param = style.DefaultParameter;
                var captured = style;
                var modifierDescription = TypeDescription(modType);
                var ruleDescription = TypeDescription(ruleType);
                var secondaryText = DescribeRule(rule);

                var preset = new StylePresetEntry
                {
                    modifierType = modType,
                    ruleType = ruleType,
                    existingStyle = captured,
                    defaultParameter = param,
                };
                var item = new Selector.SelectorItem
                {
                    displayName = name,
                    searchText = BuildSearchText(name, OnComponentGroup, secondaryText,
                        modType, ruleType, modifierDescription, ruleDescription),
                    groupName = OnComponentGroup,
                    groupOrder = -1,
                    description = modifierDescription,
                    secondaryText = secondaryText,
                    secondaryAccentKey = ruleType,
                    details = BuildDetails(modType, ruleType,
                        modifierDescription, ruleDescription),
                    icon = EditorResources.GetColoredTexture(
                        EditorResources.GetTypeIconName(modType),
                        EditorResources.GetAccentColor(preset, name)),
                    groupIcon = EditorResources.GetGroupIcon(OnComponentGroup),
                    value = preset,
                };
                if (style.Modifier is ColorModifier && ColorParsing.TryParse(param, out var color))
                {
                    var swatch = (Color)color;
                    item.createDecorator = () => new VisualElement
                    {
                        style = { backgroundColor = swatch },
                    };
                }
                items.Add(item);
            }
        }

        private static string DescribeRule(ParseRule rule)
        {
            if (rule is InlineTagRule inline)
                return "Inline · <" + inline.TagName + ">";
            if (rule is TagParseRule tag)
                return "Tag · <" + tag.TagName + ">";
            if (rule is MarkdownWrapRule markdown)
                return "Markdown · " + markdown.Marker + "…" + markdown.Marker;
            if (rule is MarkdownLinkParseRule)
                return "Markdown · […](url)";
            if (rule is RawUrlParseRule)
                return "Auto-detect · URL";
            return FormatRuleDisplayName(rule.GetType());
        }

        private enum ToggleState { None, All, Mixed }

        private ToggleState GetToggleState(StylePresetEntry preset)
        {
            int activeCount = 0;
            foreach (var t in targets)
                if (HasPreset((UniTextBase)t, preset))
                    activeCount++;
            if (activeCount == 0) return ToggleState.None;
            if (activeCount == targets.Length) return ToggleState.All;
            return ToggleState.Mixed;
        }

        /// <summary>
        /// Checks the component's OWN styles only — the same domain <see cref="AddPreset"/> and
        /// <see cref="RemovePreset"/> operate on — so a toggle can never show "on" for a style that
        /// arrived from a StylePreset asset it cannot remove. Plain for-loop: this runs per button
        /// per repaint and must not allocate.
        /// </summary>
        private static bool HasPreset(UniTextBase ut, StylePresetEntry preset)
        {
            var styles = ut.Styles;
            for (int i = 0; i < styles.Count; i++)
            {
                var s = styles[i];
                if (s?.Modifier == null || !s.Enabled) continue;
                if (!preset.modifierType.IsInstanceOfType(s.Modifier)) continue;
                if (!RuleMatchesPreset(s.Source, preset)) continue;
                return true;
            }
            return false;
        }

        private static bool RuleMatchesPreset(RangeSource source, StylePresetEntry preset)
        {
            if (preset.ruleType == null) return source == null;
            return source?.GetType() == preset.ruleType;
        }

        private void AddPreset(StylePresetEntry preset)
        {
            var items = InspectorHelpers.RequireRelative(stylesProp, "items");
            new SerializedPropertyBinding(items).EditSerializedProperties(array =>
            {
                for (var i = 0; i < array.arraySize; i++)
                {
                    var element = array.GetArrayElementAtIndex(i);
                    var existing = ReadSerializedStyle(element);
                    if (!MatchesPreset(existing, preset)) continue;
                    if (!existing.Enabled)
                        InspectorHelpers.RequireRelative(element, "disabled").boolValue = false;
                    return;
                }
                var style = ResolvePresetStyle(preset);
                var index = array.arraySize;
                array.InsertArrayElementAtIndex(index);
                WriteSerializedStyle(array.GetArrayElementAtIndex(index), style);
            }, "Add Style");
            serializedObject.Update();
        }

        private void RemovePreset(StylePresetEntry preset)
        {
            var items = InspectorHelpers.RequireRelative(stylesProp, "items");
            new SerializedPropertyBinding(items).EditSerializedProperties(array =>
            {
                for (var i = array.arraySize - 1; i >= 0; i--)
                    if (MatchesPreset(ReadSerializedStyle(
                            array.GetArrayElementAtIndex(i)), preset))
                        array.DeleteArrayElementAtIndex(i);
            }, "Remove Style");
            serializedObject.Update();
        }

        private static bool MatchesPreset(Style style, StylePresetEntry preset)
            => style?.Modifier != null &&
               preset.modifierType.IsInstanceOfType(style.Modifier) &&
               RuleMatchesPreset(style.Source, preset);

        #endregion
    }
}
