using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Helpers for querying and mutating styles/modifiers on a component.
    /// Query methods search both local <see cref="Styles"/> and shared <see cref="StylePresets"/>
    /// (local first, taking priority). Mutation methods only touch local <see cref="Styles"/> —
    /// shared preset assets are never modified through this API.
    /// </summary>
    public abstract partial class UniTextBase
    {
        #region Query

        /// <summary>
        /// Returns the first modifier of type <typeparamref name="T"/> attached to this component
        /// — local <see cref="Styles"/> first, then <see cref="StylePresets"/> runtime copies,
        /// including <see cref="CompositeModifier"/> children — or <see langword="null"/> if none.
        /// </summary>
        /// <remarks>
        /// The returned reference is the live runtime instance — mutating its public properties is
        /// the canonical way to retune modifier behavior at runtime; each setter routes through
        /// <see cref="SetDirty"/> with the minimum-necessary flag.
        /// </remarks>
        public T GetModifier<T>() where T : BaseModifier
            => TryGetModifier<T>(out var modifier, out _) ? modifier : null;

        /// <summary>Returns the first modifier assignable to <paramref name="modifierType"/>, or <see langword="null"/>. Same search scope as <see cref="GetModifier{T}"/>.</summary>
        public BaseModifier GetModifier(Type modifierType)
        {
            if (modifierType == null) return null;

            var e = EnumerateLiveStyles();
            while (e.MoveNext())
            {
                if (CompositeModifier.FindLeaf(e.Current.Modifier, modifierType) is { } match)
                    return match;
            }
            return null;
        }

        /// <summary>Try-get form of <see cref="GetModifier{T}"/>.</summary>
        public bool TryGetModifier<T>(out T modifier) where T : BaseModifier
            => TryGetModifier(out modifier, out _);

        /// <summary>
        /// Same lookup as <see cref="GetModifier{T}"/>, also returning the <see cref="Style"/> that owns
        /// the found modifier (for a match inside a <see cref="CompositeModifier"/> — the composite's style).
        /// </summary>
        public bool TryGetModifier<T>(out T modifier, out Style style) where T : BaseModifier
        {
            var e = EnumerateLiveStyles();
            while (e.MoveNext())
            {
                if (CompositeModifier.FindLeaf(e.Current.Modifier, typeof(T)) is T match)
                {
                    modifier = match;
                    style = e.Current;
                    return true;
                }
            }

            modifier = null;
            style = null;
            return false;
        }

        /// <summary>
        /// Collects every modifier of type <typeparamref name="T"/> into <paramref name="results"/>
        /// (cleared first) and returns the count. Same search scope as <see cref="GetModifier{T}"/>.
        /// </summary>
        /// <param name="includeChildren">
        /// When true, a matched <see cref="CompositeModifier"/> is still descended into so its
        /// children are collected alongside it — needed when <typeparamref name="T"/> is a base type
        /// (e.g. <see cref="BaseModifier"/>) the composite itself satisfies, which would otherwise hide
        /// its children. When false (default) a match is returned as a unit and its children are skipped.
        /// </param>
        public int GetModifiers<T>(List<T> results, bool includeChildren = false) where T : BaseModifier
        {
            results.Clear();
            var e = EnumerateLiveStyles();
            while (e.MoveNext())
                CollectModifiers(e.Current.Modifier, results, includeChildren);

            return results.Count;
        }

        /// <summary>Returns true if any style on this component has a modifier of type <typeparamref name="T"/>.</summary>
        public bool HasModifier<T>() where T : BaseModifier => HasModifier(typeof(T));

        /// <summary>Returns true if any style on this component has a modifier assignable to <paramref name="modifierType"/>.</summary>
        public bool HasModifier(Type modifierType)
        {
            if (modifierType == null) return false;
            return TryGetStyle(modifierType, out _);
        }

        /// <summary>Finds the first style whose modifier is of type <typeparamref name="T"/>.</summary>
        public bool TryGetStyle<T>(out Style style) where T : BaseModifier => TryGetStyle(typeof(T), out style);

        /// <summary>Finds the first style whose modifier is assignable to <paramref name="modifierType"/>.</summary>
        public bool TryGetStyle(Type modifierType, out Style style)
            => TryGetStyle(modifierType, wholeTextOnly: false, out style);

        /// <summary>
        /// Finds the first whole-text style (range <c>..</c>) whose modifier is of type <typeparamref name="T"/>.
        /// </summary>
        public bool TryGetWholeTextStyle<T>(out Style style) where T : BaseModifier => TryGetWholeTextStyle(typeof(T), out style);

        /// <summary>Finds the first whole-text style whose modifier is assignable to <paramref name="modifierType"/>.</summary>
        public bool TryGetWholeTextStyle(Type modifierType, out Style style)
            => TryGetStyle(modifierType, wholeTextOnly: true, out style);

        private bool TryGetStyle(Type modifierType, bool wholeTextOnly, out Style style)
        {
            if (modifierType != null)
            {
                var e = EnumerateLiveStyles();
                while (e.MoveNext())
                {
                    var s = e.Current;
                    if (CompositeModifier.FindLeaf(s.Modifier, modifierType) == null) continue;
                    if (wholeTextOnly && !IsWholeTextStyle(s)) continue;
                    style = s;
                    return true;
                }
            }
            style = null;
            return false;
        }

        /// <summary>Enumerates every style whose modifier is of type <typeparamref name="T"/>, local first.</summary>
        public IEnumerable<Style> GetStylesOfType<T>() where T : BaseModifier
            => GetStylesOfType(typeof(T));

        /// <summary>Enumerates every style whose modifier is assignable to <paramref name="modifierType"/>, local first.</summary>
        public IEnumerable<Style> GetStylesOfType(Type modifierType)
        {
            if (modifierType == null) yield break;

            var e = EnumerateLiveStyles();
            while (e.MoveNext())
            {
                if (CompositeModifier.FindLeaf(e.Current.Modifier, modifierType) != null)
                    yield return e.Current;
            }
        }

        /// <summary>
        /// Finds the first live style whose modifier's <see cref="BaseModifier.Signature"/> equals
        /// <paramref name="signature"/> — the structured clipboard's by-identity resolution, local styles first.
        /// </summary>
        internal bool TryGetStyleBySignature(string signature, out Style style)
        {
            if (!string.IsNullOrEmpty(signature))
            {
                var e = EnumerateLiveStyles();
                while (e.MoveNext())
                {
                    if (e.Current.Modifier != null && e.Current.Modifier.Signature == signature)
                    {
                        style = e.Current;
                        return true;
                    }
                }
            }
            style = null;
            return false;
        }

        /// <summary>
        /// The configured rule whose syntax a style edit writes for <paramref name="exemplar"/>: the first live
        /// style whose modifier shares the exemplar's signature (a single type, or a composite's ordered child
        /// types) with a wrappable rule (<see cref="ParseRule.CanWrap"/>), across local styles, presets, and the
        /// global preset. Null when none — the reference-only contract lets a command no-op instead of minting.
        /// </summary>
        internal ParseRule ResolveWrapRule(BaseModifier exemplar)
        {
            if (exemplar == null) return null;
            var e = EnumerateLiveStyles();
            while (e.MoveNext())
            {
                var s = e.Current;
                if (s.Source is not ParseRule rule || !exemplar.SignatureMatches(s.Modifier)) continue;
                if (!rule.CanWrap) continue;
                return rule;
            }
            return null;
        }

        internal BaseModifier ResolveStyleModifier(BaseModifier exemplar, ParseRule rule)
        {
            var e = EnumerateLiveStyles();
            while (e.MoveNext())
            {
                var style = e.Current;
                if (rule != null && !ReferenceEquals(style.Source, rule)) continue;
                if (exemplar != null && !exemplar.SignatureMatches(style.Modifier)
                    && CompositeModifier.FindLeaf(style.Modifier, exemplar.GetType()) == null) continue;
                return style.Modifier;
            }
            return exemplar;
        }

        private static void CollectModifiers<T>(BaseModifier modifier, List<T> results, bool includeChildren) where T : BaseModifier
        {
            if (modifier is T match)
            {
                results.Add(match);
                if (!includeChildren) return;
            }
            if (modifier?.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++)
                CollectModifiers(children[i], results, includeChildren);
        }

        internal LiveStyleEnumerator EnumerateLiveStyles()
            => new(this);

#if UNITY_EDITOR
        /// <summary>Every live style (local, then preset copies, then global) — the enumeration the scene-editing style picker lists as "already on this component".</summary>
        internal IEnumerable<Style> EnumerateLiveStylesForEditor()
        {
            var e = EnumerateLiveStyles();
            while (e.MoveNext()) yield return e.Current;
        }
#endif


        /// <summary>
        /// Allocation-free walk over every live non-null style: the component's own list first, then
        /// each style-preset runtime copy. <c>presetIndex == -1</c> is the own-styles phase.
        /// </summary>
        internal struct LiveStyleEnumerator
        {
            private readonly IReadOnlyList<Style> styles;
            private int styleIndex;

            public Style Current { get; private set; }

            public LiveStyleEnumerator(UniTextBase owner)
            {
                styles = owner.RuntimeStyles.Styles;
                styleIndex = 0;
                Current = null;
            }

            public bool MoveNext()
            {
                while (styleIndex < styles.Count)
                {
                    Current = styles[styleIndex++];
                    if (Current?.Enabled == true) return true;
                }
                return false;
            }
        }

        private static bool TryFindStyleIn(IReadOnlyList<Style> list, Type modifierType, bool wholeTextOnly, out Style style)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s?.Modifier == null) continue;
                if (!modifierType.IsInstanceOfType(s.Modifier)) continue;
                if (wholeTextOnly && !IsWholeTextStyle(s)) continue;
                style = s;
                return true;
            }
            style = null;
            return false;
        }

        private static bool TryFindParseStyleIn(IReadOnlyList<Style> list, Type modifierType,
            out Style style)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (candidate?.Modifier == null || candidate.Source is not ParseRule) continue;
                if (!modifierType.IsInstanceOfType(candidate.Modifier)) continue;
                style = candidate;
                return true;
            }
            style = null;
            return false;
        }

        /// <summary>
        /// True when the style targets the entire text — either it has no source (the canonical
        /// no-constraint form created by <see cref="Style.WholeText"/>) or it carries a
        /// <see cref="FixedRangeSource"/> whose single entry resolves to the full range.
        /// </summary>
        public static bool IsWholeTextStyle(Style style)
        {
            if (style == null) return false;
            if (style.Source == null) return true;
            return IsWholeTextSource(style.Source);
        }

        /// <summary>
        /// True only for a <see cref="FixedRangeSource"/> instance that covers the full text. Use
        /// <see cref="IsWholeTextStyle"/> when checking a style — that variant also accepts
        /// the canonical source-less form.
        /// </summary>
        public static bool IsWholeTextSource(RangeSource source)
        {
            return source is FixedRangeSource { IsWholeText: true };
        }

        private static string ReadWholeTextParameter(Style style)
        {
            if (style == null) return null;
            if (style.Source == null) return style.DefaultParameter;
            if (style.Source is FixedRangeSource fixedRanges && fixedRanges.Entries.Count > 0)
                return fixedRanges.Entries[0].Parameter;
            return null;
        }

        private static bool WriteWholeTextParameter(Style style, string parameter)
        {
            if (style == null) return false;
            if (style.Source == null)
            {
                if (style.DefaultParameter == parameter) return false;
                style.DefaultParameter = parameter;
                return true;
            }
            if (style.Source is FixedRangeSource fixedRanges && fixedRanges.Entries.Count > 0)
            {
                var entry = fixedRanges.Entries[0];
                if (entry.Parameter == parameter) return false;
                entry.Parameter = parameter;
                var entries = fixedRanges.Entries;
                entries.Replace(0, entry);
                return true;
            }
            return false;
        }

        #endregion

        #region Whole-text mutations

        /// <summary>
        /// Adds or updates a whole-text style of modifier type <typeparamref name="T"/>.
        /// If an existing local whole-text style exists, its parameter is updated in place;
        /// otherwise a new style is added to <see cref="Styles"/>.
        /// </summary>
        public void SetWholeText<T>(string parameter = null) where T : BaseModifier, new()
            => SetWholeText(typeof(T), parameter, static () => new T());

        /// <summary>
        /// Adds or updates a whole-text style for <paramref name="modifierType"/>, creating
        /// a new modifier via <paramref name="factory"/> when none is found locally.
        /// </summary>
        public void SetWholeText(Type modifierType, string parameter, Func<BaseModifier> factory)
        {
            if (modifierType == null || factory == null) return;

            if (TryFindStyleIn(styles, modifierType, wholeTextOnly: true, out var existing))
            {
                if (WriteWholeTextParameter(existing, parameter))
                    SetDirty(UniTextDirty.Text);
                return;
            }

            var modifier = factory();
            if (modifier == null || !modifierType.IsInstanceOfType(modifier)) return;

            Styles.Add(Style.WholeText(modifier, parameter));
        }

        /// <summary>
        /// Removes the first local whole-text style whose modifier is of type <typeparamref name="T"/>.
        /// Returns true if a style was removed.
        /// </summary>
        public bool ClearWholeText<T>() where T : BaseModifier => ClearWholeText(typeof(T));

        /// <summary>Removes the first local whole-text style whose modifier is assignable to <paramref name="modifierType"/>.</summary>
        public bool ClearWholeText(Type modifierType)
        {
            if (modifierType == null) return false;
            if (!TryFindStyleIn(styles, modifierType, wholeTextOnly: true, out var style)) return false;
            return Styles.Remove(style);
        }

        /// <summary>
        /// Inverts the presence of a whole-text style of type <typeparamref name="T"/>.
        /// Adds the style with <paramref name="parameter"/> when absent, removes it when present.
        /// Returns true if the style is present after the call.
        /// </summary>
        public bool ToggleWholeText<T>(string parameter = null) where T : BaseModifier, new()
            => ToggleWholeText(typeof(T), parameter, static () => new T());

        /// <summary>
        /// Inverts the presence of a whole-text style for <paramref name="modifierType"/>,
        /// creating a new modifier via <paramref name="factory"/> when adding.
        /// </summary>
        public bool ToggleWholeText(Type modifierType, string parameter, Func<BaseModifier> factory)
        {
            if (modifierType == null) return false;

            if (TryFindStyleIn(styles, modifierType, wholeTextOnly: true, out _))
            {
                ClearWholeText(modifierType);
                return false;
            }

            SetWholeText(modifierType, parameter, factory);
            return true;
        }

        /// <summary>Returns the parameter of the first whole-text style of type <typeparamref name="T"/>, or null.</summary>
        public string GetWholeTextParameter<T>() where T : BaseModifier
            => GetWholeTextParameter(typeof(T));

        /// <summary>Returns the parameter of the first whole-text style of <paramref name="modifierType"/>, or null.</summary>
        public string GetWholeTextParameter(Type modifierType)
        {
            if (!TryGetWholeTextStyle(modifierType, out var style)) return null;
            return ReadWholeTextParameter(style);
        }

        #endregion

        #region Multi-rule modifier registration

        /// <summary>
        /// Ensures the component has a <see cref="Style"/> whose modifier is of type
        /// <typeparamref name="T"/> and whose rule covers <paramref name="rule"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use when one semantic modifier (e.g. <see cref="ItalicModifier"/>) needs to
        /// react to multiple input syntaxes — UniText source tags <c>&lt;i&gt;</c>,
        /// Markdown markers <c>*…*</c>, GitHub Flavored <c>&lt;em&gt;</c> in pasted HTML.
        /// The modifier is created on demand; subsequent calls reuse the same modifier
        /// instance and merge the new rule via <see cref="CompositeParseRule"/>, so all
        /// configured rules drive the same underlying modifier output.
        /// </para>
        /// <para>
        /// Rule equality is structural for <see cref="TagRule"/> (by tag name) and
        /// <see cref="MarkdownWrapRule"/> (by marker); other rule types are compared by
        /// reference. Equivalent rules are skipped so repeated calls are idempotent.
        /// </para>
        /// </remarks>
        public Style EnsureStyleFor<T>(ParseRule rule) where T : BaseModifier, new()
            => EnsureStyleForInternal(typeof(T), rule, static () => new T());

        /// <summary>
        /// Non-generic overload of <see cref="EnsureStyleFor{T}(ParseRule)"/> that uses
        /// an externally-constructed modifier when the component does not already have
        /// one of the same type.
        /// </summary>
        public Style EnsureStyleFor(BaseModifier modifier, ParseRule rule)
        {
            if (modifier == null) return null;
            return EnsureStyleForInternal(modifier.GetType(), rule, () => modifier);
        }

        private Style EnsureStyleForInternal(Type modifierType, ParseRule rule, Func<BaseModifier> factory)
        {
            if (modifierType == null || rule == null || factory == null) return null;

            if (TryFindParseStyleIn(styles, modifierType, out var existing))
            {
                var existingRule = (ParseRule)existing.Source;
                if (existingRule is CompositeParseRule composite)
                {
                    for (int i = 0; i < composite.Rules.Count; i++)
                        if (RulesEquivalent(composite.Rules[i], rule)) return existing;

                    var merged = new CompositeParseRule();
                    var mergedRules = new ParseRule[composite.Rules.Count + 1];
                    for (int i = 0; i < composite.Rules.Count; i++)
                        mergedRules[i] = composite.Rules[i];
                    mergedRules[mergedRules.Length - 1] = rule;
                    merged.Rules.ReplaceAll(mergedRules);
                    existing.Source = merged;
                    return existing;
                }

                if (RulesEquivalent(existingRule, rule)) return existing;

                var wrapped = new CompositeParseRule();
                wrapped.Rules.ReplaceAll(new[] { existingRule, rule });
                existing.Source = wrapped;
                return existing;
            }

            var modifier = factory();
            if (modifier == null || !modifierType.IsInstanceOfType(modifier)) return null;

            var newStyle = new Style { Modifier = modifier, Source = rule };
            Styles.Add(newStyle);
            return newStyle;
        }

        private static bool RulesEquivalent(ParseRule a, ParseRule b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            var id = a.Identity;
            return id != null && id.EqualsIgnoreCase(b.Identity);
        }

        #endregion
    }
}
