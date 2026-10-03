using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    using Core = UniTextFont.Core;

    internal readonly struct FontFaceDescriptor
    {
        internal readonly FontRuntimeSlot slot;
        internal readonly FaceInfo faceInfo;
        internal readonly HB.hb_ot_var_axis_info_t[] variableAxes;
        internal readonly float[] defaultAxisValues;
        internal readonly bool variableAxesKnown;
        internal readonly bool isSystemFont;
        internal readonly bool isColor;

        internal FontFaceDescriptor(UniTextFont asset)
        {
            slot = asset.CaptureRuntimeSlot();
            var existing = slot.ExistingRuntime;
            faceInfo = existing?.FaceInfo ?? asset.FaceInfo;
            var usesSerializedStyle = asset.GetType() == typeof(UniTextFont)
                                      || asset.GetType() == typeof(UniTextFontVariant)
                                      || asset.GetType() == typeof(UniTextColorFont);
            HB.hb_ot_var_axis_info_t[] axes = null;
            float[] defaults = null;
            variableAxesKnown = usesSerializedStyle
                                && asset.TryCaptureVariableAxisMetadata(
                                    out axes, out defaults);
            variableAxes = axes;
            defaultAxisValues = defaults;
            isSystemFont = asset is UniTextSystemFont || existing?.isSystemFont == true;
            isColor = asset is UniTextColorFont || existing is ColorFontCore;
        }

        internal FontFaceDescriptor(Core font)
        {
            slot = new FontRuntimeSlot(font);
            faceInfo = font?.FaceInfo ?? default;
            variableAxes = null;
            defaultAxisValues = null;
            variableAxesKnown = false;
            isSystemFont = font?.isSystemFont == true;
            isColor = font is ColorFontCore;
        }

        internal bool IsValid => slot != null;
        internal bool IsMaterialized => slot?.IsMaterialized == true;
        internal Core ExistingRuntime => slot?.ExistingRuntime;
        internal Core Runtime => slot?.Runtime;
        internal bool IsKnownVariable => variableAxesKnown && variableAxes is { Length: > 0 };
    }

    internal struct FontFaceLookup
    {
        internal FontFaceDescriptor[] fonts;
        internal bool hasPrimary;
        internal bool hasFaces;

        internal FontFaceDescriptor Primary
            => hasPrimary && fonts is { Length: > 0 } ? fonts[0] : default;

        internal static FontFaceLookup Build(UniTextFont primaryAsset, ReadOnlyArray<UniTextFont> facesAsset)
        {
            var primary = primaryAsset != null
                ? new FontFaceDescriptor(primaryAsset)
                : default;
            var upright = new List<FontFaceDescriptor>();
            var italic = new List<FontFaceDescriptor>();

            for (var i = 0; i < facesAsset.Count; i++)
            {
                var asset = facesAsset[i];
                if (asset == null || ReferenceEquals(asset, primaryAsset)) continue;
                var descriptor = new FontFaceDescriptor(asset);
                if (descriptor.faceInfo.isItalic) italic.Add(descriptor);
                else upright.Add(descriptor);
            }

            var count = (primary.IsValid ? 1 : 0) + upright.Count + italic.Count;
            var result = new FontFaceLookup
            {
                fonts = count == 0 ? null : new FontFaceDescriptor[count],
                hasPrimary = primary.IsValid,
                hasFaces = upright.Count != 0 || italic.Count != 0
            };
            var destination = 0;
            if (primary.IsValid) result.fonts[destination++] = primary;
            for (var i = 0; i < upright.Count; i++) result.fonts[destination++] = upright[i];
            for (var i = 0; i < italic.Count; i++) result.fonts[destination++] = italic[i];

            if (!result.hasFaces && primary.IsKnownVariable) result.hasFaces = true;
            return result;
        }

        internal static FontFaceLookup Build(Core primary)
        {
            if (primary == null) return default;
            return new FontFaceLookup
            {
                fonts = new[] { new FontFaceDescriptor(primary) },
                hasPrimary = true,
                hasFaces = false
            };
        }

        internal bool ResolveHasFaces()
        {
            if (hasFaces) return true;
            var primary = Primary;
            if (!primary.IsValid) return false;
            if (primary.variableAxesKnown) return primary.IsKnownVariable;
            return primary.Runtime?.IsVariable == true;
        }
    }

    /// <summary>A font family: primary font plus optional variant faces (Bold, Italic, Variable, etc.).</summary>
    [Serializable]
    public struct FontFamily : IStateSnapshot<FontFamily>
    {
        /// <summary>Optional identifier referenced by <c>&lt;font=...&gt;</c> rich-text tags. Case-sensitive; duplicates across a stack trigger a warning and first-wins lookup. Leave empty for families that should not be addressable by name.</summary>
        [Tooltip("User-facing identifier used by <font=...> rich-text tags (case-sensitive, unique per stack). Leave empty to make this family unaddressable by name.")]
        public string name;

        /// <summary>Primary font asset. Provides strut metrics (if first family) and codepoint lookup.</summary>
        public UniTextFont primary;

        /// <summary>Additional faces: Bold, Italic, BoldItalic, Variable, etc.</summary>
        [SerializeField] private UniTextFont[] faces;

        /// <summary>Additional faces such as bold, italic, and variable variants.</summary>
        public ReadOnlyArray<UniTextFont> Faces => new(faces);

        /// <summary>Optional BCP 47 language hint (e.g. <c>zh-Hans</c>, <c>ja</c>, <c>en-GB</c>). When a text run carries a matching language tag, this family is preferred during codepoint-to-font resolution. Use for single-stack multi-locale setups; leave empty for locale-agnostic families.</summary>
        [Tooltip("Optional BCP 47 tag (e.g. zh-Hans, zh-Hant, ja, ko, en-US). When text carries a matching language tag, this family wins during codepoint-to-font resolution. Leave empty for locale-agnostic families.")]
        public string preferredLanguage;

        [NonSerialized] internal FontFaceLookup lookup;

        /// <summary>Creates a family with detached face storage.</summary>
        public FontFamily(string name, UniTextFont primary, UniTextFont[] faces = null,
            string preferredLanguage = null)
        {
            this.name = name;
            this.primary = primary;
            this.faces = faces == null ? null : (UniTextFont[])faces.Clone();
            this.preferredLanguage = preferredLanguage;
            lookup = default;
        }

        /// <summary>Returns a copy of this family with detached replacement faces.</summary>
        public FontFamily WithFaces(UniTextFont[] value)
        {
            var result = this;
            result.faces = value == null ? null : (UniTextFont[])value.Clone();
            return result;
        }

        /// <summary>Captures the family and its face-array storage without retaining mutable aliases.</summary>
        public FontFamily CaptureStateSnapshot()
        {
            var snapshot = this;
            snapshot.faces = faces == null ? null : (UniTextFont[])faces.Clone();
            return snapshot;
        }

        /// <summary>Compares serialized family configuration while ignoring the derived runtime lookup.</summary>
        public bool StateEquals(in FontFamily snapshot)
        {
            if (!string.Equals(name, snapshot.name, StringComparison.Ordinal) ||
                primary != snapshot.primary ||
                !string.Equals(preferredLanguage, snapshot.preferredLanguage, StringComparison.Ordinal))
                return false;
            if (ReferenceEquals(faces, snapshot.faces)) return true;
            if (faces == null || snapshot.faces == null || faces.Length != snapshot.faces.Length) return false;
            for (var i = 0; i < faces.Length; i++)
                if (faces[i] != snapshot.faces[i]) return false;
            return true;
        }
    }

    /// <summary>BCP 47 language tag matching: shorter tag matches longer tag sharing the same prefix (e.g. <c>zh</c> ↔ <c>zh-Hans</c>).</summary>
    public static class LanguageMatching
    {
        /// <summary>True if the two BCP 47 tags are equal or one is a prefix of the other.</summary>
        public static bool Matches(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                return false;
            return MatchPrefix(a, b) || MatchPrefix(b, a);
        }

        private static bool MatchPrefix(string longer, string shorter)
        {
            if (longer.Length < shorter.Length) return false;
            if (!longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase)) return false;
            return longer.Length == shorter.Length || longer[shorter.Length] == '-';
        }
    }

    /// <summary>Ordered list of font families plus an optional fallback stack. The first family's primary provides strut metrics (line height, ascent, descent). Face matching uses pre-computed lookup tables with CSS Fonts Level 4 §5.2 weight matching.</summary>
    [StateSource]
    public partial class UniTextFontStack : ScriptableObject
    {
        /// <summary>Ordered font families searched by this stack.</summary>
        [SerializeField, StateList(nameof(RefreshBindings))]
        private FontFamily[] families;

        /// <summary>Gets or sets the optional fallback stack searched after this stack's own families.</summary>
        [SerializeField, StateProperty(nameof(ApplyFallbackStackChange))]
        private UniTextFontStack fallbackStack;

        /// <summary>Primary font asset of the first family.</summary>
        public UniTextFont PrimaryFont => families is { Length: > 0 } ? families[0].primary : null;

        /// <summary>True if any family in the resolved chain carries variant faces or a variable font.</summary>
        public bool HasFaces
        {
            get
            {
                var resolved = BuildResolvedFamilies();
                for (var i = 0; i < resolved.Length; i++)
                    if (resolved[i].lookup.ResolveHasFaces()) return true;
                return false;
            }
        }

        /// <summary>Looks up a family by its <see cref="FontFamily.name"/> across this stack and its fallback chain. Case-sensitive, first-wins.</summary>
        public bool TryGetFamilyByName(string name, out FontFamily family)
        {
            family = default;
            if (string.IsNullOrEmpty(name)) return false;

            var families = BuildResolvedFamilies();
            for (var i = 0; i < families.Length; i++)
            {
                if (families[i].name == name)
                {
                    family = families[i];
                    return true;
                }
            }
            return false;
        }

        [NonSerialized] private FontFamily[] cachedResolvedFamilies;

        internal FontFamily[] BuildResolvedFamilies()
        {
            if (cachedResolvedFamilies != null)
                return cachedResolvedFamilies;

            var list = new List<FontFamily>();
            var visited = new HashSet<UniTextFontStack>();
            CollectFamilies(this, list, visited);
            cachedResolvedFamilies = list.ToArray();
            WarnOnDuplicateFamilyNames(cachedResolvedFamilies);
            return cachedResolvedFamilies;
        }

        private static void WarnOnDuplicateFamilyNames(FontFamily[] families)
        {
            HashSet<string> seen = null;
            for (var i = 0; i < families.Length; i++)
            {
                var name = families[i].name;
                if (string.IsNullOrEmpty(name)) continue;

                seen ??= new HashSet<string>();
                if (!seen.Add(name))
                {
                    Debug.LogWarning(
                        $"[UniTextFontStack] Duplicate family name \"{name}\" — first occurrence wins in <font={name}> lookups. " +
                        "Use unique names per stack for predictable behavior.");
                }
            }
        }

        private static void CollectFamilies(UniTextFontStack stack, List<FontFamily> list,
            HashSet<UniTextFontStack> visited)
        {
            while (true)
            {
                if (stack == null || !visited.Add(stack)) return;
                stack.TryInit();
                if (stack.families != null)
                {
                    for (int i = 0; i < stack.families.Length; i++)
                        list.Add(stack.families[i]);
                }
                stack = stack.fallbackStack;
            }
        }

        [NonSerialized] private bool isInitialized;
        private readonly List<UniTextFont> boundFonts = new();
        [NonSerialized] private UniTextFontStack boundFallbackStack;

        private void TryInit()
        {
            if (isInitialized) return;
            isInitialized = true;
            RebuildLookups();
            BindDependencies();
        }

        private void OnEnable()
        {
            if (CreatesFallbackCycle(fallbackStack))
                throw new InvalidOperationException($"Font stack '{name}' contains a fallback cycle.");
            TryInit();
        }

        private void OnDisable() { DeInit(); }
        private void OnDestroy() { DeInit(); }

        private void DeInit()
        {
            isInitialized = false;
            cachedResolvedFamilies = null;

            for (var i = 0; i < boundFonts.Count; i++) boundFonts[i].Changed -= CallChanged;
            boundFonts.Clear();

            if (boundFallbackStack != null) boundFallbackStack.Changed -= CallChanged;
            boundFallbackStack = null;
        }

        private void RefreshBindings(in StateListMutation<FontFamily> mutation)
        {
            DeInit();
            TryInit();
            PublishStateChange(this, mutation.ToStateChange(Members.Families));
        }

        private void ApplyFallbackStackChange(StateMember member,
            UniTextFontStack previous, ref UniTextFontStack current)
        {
            if (CreatesFallbackCycle(current))
            {
                current = previous;
                throw new InvalidOperationException($"Font stack '{name}' cannot use a fallback chain that points back to itself.");
            }
            DeInit();
            TryInit();
            var change = new StateChange(member);
            PublishStateChange(this, in change);
        }

        private bool CreatesFallbackCycle(UniTextFontStack candidate)
        {
            var visited = new HashSet<UniTextFontStack>();
            for (var current = candidate; current != null; current = current.fallbackStack)
            {
                if (current == this) return true;
                if (!visited.Add(current)) return true;
            }
            return false;
        }

        private void CallChanged(IStateChangeSource source, in StateChange change)
        {
            if (source is UniTextFont changedFont && InvalidatesFaceLookup(in change))
            {
                cachedResolvedFamilies = null;
                if (boundFonts.Contains(changedFont)) RebuildLookups();
            }
            else if (source is UniTextFontStack)
                cachedResolvedFamilies = null;
            PublishStateChange(source, in change);
        }

        private static bool InvalidatesFaceLookup(in StateChange change)
            => change.Kind == StateChangeKind.Reset ||
               change.Member == UniTextFont.Members.FontDataHash ||
               change.Member == UniTextFont.Members.FaceInfo ||
               change.Member == UniTextFont.Members.UnitsPerEm ||
               change.Member == UniTextFont.Members.FontScale ||
               change.Member == UniTextFont.Members.ParticipatesInNormalization ||
               change.Member == UniTextFont.Members.SdfDetailMultiplier ||
               change.Member == UniTextFont.Members.TileSizeOffset ||
               change.Member == UniTextFont.Members.ItalicStyle ||
               change.Member == UniTextFont.Members.SpacingOffset ||
               change.Member == UniTextFont.Members.SpaceAdvance ||
               change.Member == UniTextFont.Members.FakeBoldWeight ||
               change.Member == UniTextFont.Members.GlyphOverrides ||
               change.Member == UniTextFont.Members.AxisDefaults ||
               change.Member == UniTextFontVariant.Members.Source ||
               change.Member == UniTextFontVariant.Members.FaceIndex ||
               change.Member == UniTextColorFont.Members.ColorPixelSize ||
               change.Member == UniTextSystemFont.Members.Common ||
               change.Member == UniTextSystemFont.Members.Windows ||
               change.Member == UniTextSystemFont.Members.Macos ||
               change.Member == UniTextSystemFont.Members.Linux ||
               change.Member == UniTextSystemFont.Members.Ios ||
               change.Member == UniTextSystemFont.Members.Android;

        private void RebuildLookups()
        {
            if (families != null)
                for (var i = 0; i < families.Length; i++)
                    families[i].lookup = FontFaceLookup.Build(families[i].primary, families[i].Faces);
        }

        private void BindDependencies()
        {
            if (families != null)
            {
                for (var i = 0; i < families.Length; i++)
                {
                    BindFont(families[i].primary);
                    var faces = families[i].Faces;
                    for (var j = 0; j < faces.Count; j++) BindFont(faces[j]);
                }
            }

            if (fallbackStack == null || fallbackStack == this) return;
            boundFallbackStack = fallbackStack;
            boundFallbackStack.Changed += CallChanged;
        }

        private void BindFont(UniTextFont value)
        {
            if (value == null || boundFonts.Contains(value)) return;
            boundFonts.Add(value);
            value.Changed += CallChanged;
        }

    }
}
