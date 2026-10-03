using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Single option entry exposed by a <see cref="ParameterProviders"/> rich provider.
    /// Carries the value written into the parameter, an optional display label and an
    /// optional renderer drawn in a fixed-width column on the right side of the dropdown row
    /// (used to draw a live colour/gradient swatch preview, for example).
    /// </summary>
    public readonly struct ParameterOption
    {
        /// <summary>Value emitted into the tag parameter when this option is selected.</summary>
        public readonly string value;

        /// <summary>Label shown in the dropdown. Falls back to <see cref="value"/> when null.</summary>
        public readonly string displayName;

        /// <summary>Optional factory for the preview shown at the right edge of the row.</summary>
        public readonly Func<VisualElement> createPreview;

        /// <summary>Optional description shown in the selector's preview pane.</summary>
        public readonly string description;

        public ParameterOption(string value, string displayName = null,
            Func<VisualElement> createPreview = null, string description = null)
        {
            this.value = value;
            this.displayName = displayName ?? value;
            this.createPreview = createPreview;
            this.description = description;
        }
    }

    /// <summary>
    /// Resolves <see cref="ParameterOption"/> entries for an <c>"enum:@key"</c> field, given the
    /// owning modifier instance. Use this overload when the dropdown content depends on per-modifier
    /// state — e.g. a modifier reading from its assigned <see cref="IHasPaintProvider"/> source.
    /// The <paramref name="modifier"/> may be <see langword="null"/> when no owning modifier could be
    /// resolved (e.g. preview contexts); implementations should fall back to a sensible default.
    /// </summary>
    public delegate IEnumerable<ParameterOption> ContextualParameterOptionsProvider(BaseModifier modifier);

    /// <summary>Editor registry for dynamic <c>[Options("@key")]</c> parameter values.</summary>
    public static class ParameterProviders
    {
        private static readonly Dictionary<string, Func<object, IEnumerable<ParameterOption>>>
            providers = new();

        /// <summary>Advances whenever the registered set changes; option lists cached against it go stale.</summary>
        internal static int Version { get; private set; }

        /// <summary>
        /// Announces that what a registered provider serves has changed. Editor option lists retain
        /// a provider's entries between queries and rebuild them on this signal; a provider whose
        /// catalog lives outside Unity's object graph — a computed list, a plain C# source — is the
        /// only one that can raise it.
        /// </summary>
        public static void Invalidate() => Version++;

        /// <summary>Registers a simple options provider (string values only).</summary>
        public static void Register(string key, Func<IEnumerable<string>> provider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            providers[key] = _ => WrapValues(provider());
            Version++;
        }

        /// <summary>
        /// Registers a rich options provider whose entries can carry per-option display name,
        /// preview decorator and description.
        /// </summary>
        public static void Register(string key, Func<IEnumerable<ParameterOption>> richProvider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (richProvider == null) throw new ArgumentNullException(nameof(richProvider));
            providers[key] = _ => richProvider();
            Version++;
        }

        /// <summary>
        /// Registers a contextual options provider that receives the owning modifier instance and can
        /// produce dropdown content depending on per-modifier state (e.g. an injected provider).
        /// </summary>
        public static void Register(string key, ContextualParameterOptionsProvider contextualProvider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (contextualProvider == null) throw new ArgumentNullException(nameof(contextualProvider));
            providers[key] = owner => contextualProvider(owner as BaseModifier);
            Version++;
        }

        /// <summary>Removes a previously registered provider.</summary>
        public static void Unregister(string key)
        {
            if (key == null) return;
            if (providers.Remove(key)) Version++;
        }

        /// <summary>
        /// Tries to get the current options as raw strings. Adapts rich/contextual providers automatically
        /// by projecting <see cref="ParameterOption.value"/>. Contextual providers are invoked with a
        /// <see langword="null"/> modifier — pass a modifier explicitly via <see cref="TryGetRichOptions(string, BaseModifier, out IEnumerable{ParameterOption})"/>.
        /// </summary>
        public static bool TryGetOptions(string key, out IEnumerable<string> options)
        {
            var found = TryGetRichOptionsForOwner(key, null, out var richOptions);
            options = found ? ProjectValues(richOptions) : null;
            return found;
        }

        /// <summary>
        /// Tries to get the current options as <see cref="ParameterOption"/> entries, optionally bound
        /// to an owning <paramref name="modifier"/>. Contextual providers receive the modifier; rich
        /// and simple providers ignore it. Adapts simple string providers automatically.
        /// </summary>
        public static bool TryGetRichOptions(string key, BaseModifier modifier,
            out IEnumerable<ParameterOption> options)
            => TryGetRichOptionsForOwner(key, modifier, out options);

        internal static void RegisterOwner(string key,
            Func<object, IEnumerable<ParameterOption>> provider)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            providers[key] = provider;
            Version++;
        }

        internal static bool TryGetRichOptionsForOwner(string key, object owner,
            out IEnumerable<ParameterOption> options)
        {
            options = null;
            if (key == null || !providers.TryGetValue(key, out var provider)) return false;
            options = provider(owner);
            return options != null;
        }

        private static IEnumerable<string> ProjectValues(IEnumerable<ParameterOption> source)
        {
            foreach (var opt in source)
                yield return opt.value;
        }

        private static IEnumerable<ParameterOption> WrapValues(IEnumerable<string> source)
        {
            foreach (var v in source)
                yield return new ParameterOption(v);
        }
    }

    [InitializeOnLoad]
    internal static class BuiltInParameterProviders
    {
        private static readonly GlobalSettingsPaintProvider globalPaintFallback = new();
        private static readonly ConditionalWeakTable<object, object> observedPaints = new();
        private static readonly object observed = new();

        static BuiltInParameterProviders()
        {
            ParameterProviders.RegisterOwner("paints", EnumeratePaints);
        }

        private static IEnumerable<ParameterOption> EnumeratePaints(object owner)
        {
            var source = owner is IHasPaintProvider hasProvider
                ? hasProvider.PaintProvider
                : globalPaintFallback;
            if (source == null) yield break;
            Observe(source);

            foreach (var entry in source.Enumerate())
            {
                if (string.IsNullOrEmpty(entry.name))
                    continue;

                var captured = entry;
                yield return new ParameterOption(
                    value: entry.name,
                    displayName: entry.name,
                    createPreview: () => CreatePaintPreview(captured));
            }
        }

        /// <summary>
        /// Subscribes once per paint source consulted, so a catalog edit reaches the option lists
        /// whether or not it passes through Unity's object graph. The table holds the source
        /// weakly; the subscription itself retains nothing.
        /// </summary>
        private static void Observe(IPaintProvider source)
        {
            if (observedPaints.TryGetValue(source, out _)) return;
            observedPaints.Add(source, observed);
            source.Changed += OnPaintsChanged;
        }

        private static void OnPaintsChanged(INamedCatalog<PaintSwatch> catalog,
            in NamedCatalogChange<PaintSwatch> change) => ParameterProviders.Invalidate();

        private static VisualElement CreatePaintPreview(PaintSwatch swatch)
        {
            var source = swatch.paint.source;
            if (source.kind == PaintSourceKind.Gradient && source.gradient.IsValid)
                return GradientDrawer.CreatePreview(source.gradient);
            var preview = new VisualElement();
            if (source.kind == PaintSourceKind.Texture && source.texture != null)
                preview.style.backgroundImage = new StyleBackground(source.texture);
            else
                preview.style.backgroundColor = source.color;
            return preview;
        }
    }
}
