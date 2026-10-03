using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    using Core = UniTextFont.Core;

    /// <summary>Snapshot of exact variable-font registrations shared across providers.</summary>
    public readonly struct FontVariationMemoryStats
    {
        /// <summary>Total exact coordinate registrations.</summary>
        public int RegistrationCount { get; }
        /// <summary>Registrations leased by at least one live provider.</summary>
        public int ActiveRegistrationCount { get; }

        internal FontVariationMemoryStats(int registrationCount, int activeRegistrationCount)
        {
            RegistrationCount = registrationCount;
            ActiveRegistrationCount = activeRegistrationCount;
        }
    }

    /// <summary>
    /// Owns Unicode-sequence font resolution for a text object: composes the family chain from the assigned font
    /// and font stack, walks it (plus emoji and the always-on <see cref="SystemFont"/> fallback) to pick a
    /// covering runtime and exact variation instance when available, and otherwise returns the primary runtime
    /// as the intentional .notdef endpoint. Asset-backed faces are captured as worker-safe lazy slots; bare runtime
    /// companions and the emoji singleton flow through the same resolution path. The owner must dispose the provider
    /// after all processing and raster work finishes.
    /// </summary>
    public sealed class UniTextFontProvider : IDisposable
    {
        private readonly FastIntDictionary<Core> fonts = new();
        private readonly HashSet<Core> leasedSystemFonts = new();
        private static int nextContextId;
        private readonly int contextId = AllocateContextId();

        private static int AllocateContextId()
        {
            var id = System.Threading.Interlocked.Increment(ref nextContextId);
            if (id <= 0) throw new InvalidOperationException("UniText font-provider identity space exhausted.");
            return id;
        }

        private Core primaryFont;
        private int primaryFontId;

        private float fontSize = 36f;
        private float fontScale = 1f;

        private FontNormalizeMetric normalizeMetric;
        private float normalizeTargetRatio;
        private bool normalizeTargetExplicit;

        private ResolvedFamily[] resolvedFamilies;

        private bool hasColorFamily;
        private bool hasSystemFamily;

        private readonly FastIntDictionary<ushort> fontIdToFamilyIndex = new();

        private static readonly Dictionary<long, List<VariationInstanceRegistration>> variationInstances = new();
        private static readonly Dictionary<int, VariationInstanceRegistration> variationInstancesById = new();
        private static readonly object variationLock = new();
        private static int inactiveVariationCapacity = 256;
        private static int inactiveVariationCount;
        private static long variationUseClock;
        private static int nextVariationFontId = int.MinValue;

        private sealed class VariationInstanceRegistration
        {
            internal WeakReference<Core> font;
            internal int[] ftCoords;
            internal HB.hb_variation_t[] hbVariations;
            internal int fontId;
            internal long varHash48;
            internal long registrationKey;
            internal int leases;
            internal long lastUse;
        }

        private readonly HashSet<int> activeVariationIds = new();
        private readonly HashSet<int> usedVariationIds = new();
        private readonly List<int> releasedVariationIds = new();

        /// <summary>Maximum number of exact variation registrations retained after their last provider releases them. Active registrations are never evicted.</summary>
        public static int InactiveVariationCapacity
        {
            get { lock (variationLock) return inactiveVariationCapacity; }
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                lock (variationLock) inactiveVariationCapacity = value;
                TrimVariationRegistrations();
            }
        }

        /// <summary>Returns a consistent snapshot of exact variation registrations and their ownership.</summary>
        public static FontVariationMemoryStats VariationMemoryStats
        {
            get
            {
                lock (variationLock)
                {
                    var active = 0;
                    foreach (var registration in variationInstancesById.Values)
                        if (registration.leases > 0) active++;
                    return new FontVariationMemoryStats(variationInstancesById.Count, active);
                }
            }
        }

        static UniTextFontProvider() => Application.lowMemory += TrimUnusedVariations;

        private Core lastInstanceFont;
        private FontFaceRequest lastInstanceRequest;
        private ResolvedFontInstance lastInstance;
        private bool hasLastInstance;
        private bool disposed;

        /// <summary>A worker-safe family snapshot built from an assigned font, a <see cref="UniTextFontStack"/> family, or a bare fallback <see cref="Core"/>.</summary>
        private struct ResolvedFamily
        {
            public FontFaceLookup lookup;
            public string name;
            public string preferredLanguage;

            public FontFaceDescriptor Primary => lookup.Primary;
        }

        /// <summary>Current font size in points. Setter recomputes the cached font scale.</summary>
        public float FontSize
        {
            get => fontSize;
            set
            {
                fontSize = value;
                UpdateFontScale();
            }
        }

        /// <summary>Equivalent to setting <see cref="FontSize"/>.</summary>
        public void SetFontSize(float size) { FontSize = size; }

        /// <summary>Resolved primary font runtime. Provides strut metrics and serves as the head of codepoint resolution.</summary>
        public Core PrimaryFont => primaryFont;
        /// <summary>Stable id of <see cref="PrimaryFont"/>.</summary>
        public int PrimaryFontId => primaryFontId;

        /// <summary>True if the resolved family chain carries variant faces (Bold/Italic) or a variable font.</summary>
        public bool HasFaces
        {
            get
            {
                for (var i = 0; i < resolvedFamilies.Length; i++)
                    if (resolvedFamilies[i].lookup.ResolveHasFaces()) return true;
                return false;
            }
        }

        /// <summary>Initializes the provider. The resolution chain is <paramref name="primary"/> (if set), then <paramref name="fontStack"/>'s families, then <see cref="SystemFont.Default"/> (the configured <see cref="UniTextSettings.SystemFont"/> or the OS default) as the always-on final fallback. With nothing assigned, <see cref="SystemFont.Default"/> becomes the primary. Main-thread only.</summary>
        public UniTextFontProvider(UniTextFont primary, UniTextFontStack fontStack, float fontSize = 36f,
            FontNormalizeMetric normalizeMetric = FontNormalizeMetric.None, float normalizeTarget = 0f)
        {
            this.normalizeMetric = normalizeMetric;
            var families = new List<ResolvedFamily>();

            if (primary != null)
            {
                families.Add(new ResolvedFamily
                    { lookup = FontFaceLookup.Build(primary, default) });
            }

            if (fontStack != null)
                foreach (var family in fontStack.BuildResolvedFamilies())
                    families.Add(new ResolvedFamily
                    {
                        lookup = family.lookup,
                        name = family.name,
                        preferredLanguage = family.preferredLanguage
                    });

            var systemFallback = SystemFont.Default;
            if (systemFallback != null)
            {
                var alreadyPresent = false;
                for (var i = 0; i < families.Count; i++)
                    if (ReferenceEquals(families[i].Primary.ExistingRuntime, systemFallback))
                    {
                        alreadyPresent = true;
                        break;
                    }
                if (!alreadyPresent)
                    families.Add(new ResolvedFamily
                        { lookup = FontFaceLookup.Build(systemFallback) });
            }

            resolvedFamilies = families.ToArray();

            primaryFont = null;
            var primaryFamilyIndex = -1;
            for (var i = 0; i < resolvedFamilies.Length; i++)
            {
                var candidate = resolvedFamilies[i].Primary.Runtime;
                if (candidate == null) continue;
                primaryFont = candidate;
                primaryFamilyIndex = i;
                break;
            }

            if (primaryFont == null)
            {
                Debug.LogException(new ArgumentException(
                    "UniTextFontProvider: no font assigned and no OS default font available (e.g. WebGL). Assign a font or font stack.",
                    nameof(primary)));
                resolvedFamilies = Array.Empty<ResolvedFamily>();
                return;
            }

            this.fontSize = fontSize;
            primaryFontId = GetFontId(primaryFont);
            RegisterFamilyFont(primaryFont, (ushort)primaryFamilyIndex);
            UpdateFontScale();

            if (normalizeMetric != FontNormalizeMetric.None)
            {
                normalizeTargetExplicit = normalizeTarget > 0f;
                normalizeTargetRatio = normalizeTargetExplicit ? normalizeTarget : NormalizeRatio(primaryFont);
            }

            for (ushort i = 0; i < resolvedFamilies.Length; i++)
            {
                ref var family = ref resolvedFamilies[i];
                var primaryDescriptor = family.Primary;
                if (!primaryDescriptor.IsValid) continue;
                if (primaryDescriptor.isColor) hasColorFamily = true;
                if (primaryDescriptor.isSystemFont) hasSystemFamily = true;
            }

            CatZones.fontProvider.MeowFormat("[FontProvider] Created: mainFont={0} (id={1}), families={2}",
                primaryFont?.Name ?? "?", primaryFontId, resolvedFamilies.Length);
        }

        internal bool SupportsCoherentSequenceResolution
            => hasSystemFamily && SystemFont.SupportsCoherentSequenceResolution;

        private void UpdateFontScale()
        {
            if (primaryFont == null) return;
            fontScale = RawScale(primaryFont, fontSize);
        }

        /// <summary>Per-font multiplier matching a fallback/secondary font to the primary font's reference metric (CSS <c>font-size-adjust</c>). Derived from the runtime alone, so every id addressing one runtime — base or variation instance — yields the same multiplier. Returns 1 when normalization is off, the font is the primary, the font opts out, or a metric is unavailable.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetNormalizationScale(Core font)
        {
            if (normalizeMetric == FontNormalizeMetric.None || normalizeTargetRatio <= 0f) return 1f;
            if (font == null || !font.ParticipatesInNormalization) return 1f;
            if (!normalizeTargetExplicit && ReferenceEquals(font, primaryFont)) return 1f;
            var ratio = NormalizeRatio(font);
            return ratio > 0f ? normalizeTargetRatio / ratio : 1f;
        }

        /// <summary>Convenience overload resolving the runtime from a font id issued by this provider. Returns 1 for an id it never issued.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetNormalizationScale(int fontId) => GetNormalizationScale(GetFont(fontId));

        /// <summary>Design-units → pixels for <paramref name="fontId"/> at <paramref name="fontSize"/>: folds in the font's <see cref="UniTextFont.FontScale"/> and the active normalization (CSS <c>font-size-adjust</c>). Multiply any design-unit metric (advance, bearing, line height) by the result. Returns 0 when the font is unavailable.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float MetricScale(int fontId, float fontSize)
        {
            var font = GetFont(fontId);
            return font == null ? 0f : RawScale(font, fontSize) * GetNormalizationScale(font);
        }

        /// <inheritdoc cref="MetricScale(int,float)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float MetricScale(Core font, float fontSize)
            => font == null ? 0f : RawScale(font, fontSize) * GetNormalizationScale(font);

        /// <summary>Sets the active normalization metric and target, recomputing the primary reference ratio. Called by a font-size-match modifier so the provider stays modifier-agnostic.</summary>
        public void SetNormalization(FontNormalizeMetric metric, float target)
        {
            normalizeMetric = metric;
            if (metric == FontNormalizeMetric.None || primaryFont == null)
            {
                normalizeTargetRatio = 0f;
                return;
            }
            normalizeTargetExplicit = target > 0f;
            normalizeTargetRatio = normalizeTargetExplicit ? target : NormalizeRatio(primaryFont);
        }

        private float NormalizeRatio(Core font)
        {
            var fi = font.FaceInfo;
            var metric = normalizeMetric == FontNormalizeMetric.CapHeight ? fi.capLine : fi.meanLine;
            return font.UnitsPerEm > 0 && metric > 0 ? (float)metric / font.UnitsPerEm : 0f;
        }

        /// <summary>Design-units → pixels without normalization (<c>fontSize · FontScale / upem</c>). The primary strut uses this directly; <see cref="MetricScale(int,float)"/> layers normalization on top for fallback fonts.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float RawScale(Core font, float fontSize) => fontSize * font.FontScale / font.UnitsPerEm;

        /// <summary>Process-lifetime font identity. The primary system emoji face uses <see cref="EmojiFont.FontId"/>; null uses 0.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFontId(Core font)
        {
            if (font == null) return 0;
            if (font is EmojiFont) return EmojiFont.FontId;
            return font.FontDataHash;
        }

        /// <summary>Process-lifetime font identity from a <see cref="UniTextFont"/> asset.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFontId(UniTextFont font) => GetFontId(font?.Runtime);

        private readonly object registerLock = new();

        /// <summary>Registers a runtime under its process identity. Writes are serialized; reads stay lock-free.</summary>
        public void RegisterFont(int fontId, Core font)
        {
            if (font == null || fontId == 0 || fontId == EmojiFont.FontId) return;
            if (fonts.TryGetValue(fontId, out var existing))
            {
                if (ReferenceEquals(existing, font)) return;
                throw new InvalidOperationException($"fontId {fontId} already belongs to '{existing.Name}'.");
            }
            lock (registerLock)
            {
                if (disposed) throw new ObjectDisposedException(nameof(UniTextFontProvider));
                if (fonts.TryGetValue(fontId, out var registered)
                    && !ReferenceEquals(registered, font))
                    throw new InvalidOperationException($"fontId {fontId} already belongs to '{registered.Name}'.");
                fonts[fontId] = font;
                var source = font.SystemFontSource;
                if (source != null && leasedSystemFonts.Add(source)) SystemFont.Acquire(source);
            }
        }

        /// <summary>Releases all SystemFont source leases and variation registrations owned by this provider. Call only after its processing and raster work has finished.</summary>
        public void Dispose()
        {
            Core[] releases;
            int[] variationReleases;
            lock (registerLock)
            {
                if (disposed) return;
                disposed = true;
                releases = new Core[leasedSystemFonts.Count];
                leasedSystemFonts.CopyTo(releases);
                leasedSystemFonts.Clear();
                variationReleases = new int[activeVariationIds.Count];
                activeVariationIds.CopyTo(variationReleases);
                activeVariationIds.Clear();
                usedVariationIds.Clear();
                releasedVariationIds.Clear();
                fonts.Clear();
                fontIdToFamilyIndex.Clear();
                resolvedFamilies = Array.Empty<ResolvedFamily>();
                primaryFont = null;
                lastInstanceFont = null;
                lastInstance = default;
                hasLastInstance = false;
            }
            for (var i = 0; i < variationReleases.Length; i++) ReleaseVariation(variationReleases[i]);
            TrimVariationRegistrations();
            SystemFont.Release(releases);
        }

        /// <summary>Resolves a font id to its registered runtime, or null when the id has no owner in this provider.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Core GetFont(int fontId)
        {
            if (fontId == primaryFontId) return primaryFont;
            if (fontId == EmojiFont.FontId) return EmojiFont.Instance;
            if (fonts.TryGetValue(fontId, out var font)) return font;
            CatZones.fontProvider.MeowWarnOnce($"unkfont:{fontId}",
                "[FontProvider] Unknown fontId {0}", fontId);
            return null;
        }

        /// <summary>Attempts to resolve a registered font without logging an unknown-id diagnostic.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetFont(int fontId, out Core font)
        {
            if (fontId == primaryFontId)
            {
                font = primaryFont;
                return true;
            }

            if (fontId == EmojiFont.FontId)
            {
                if (EmojiFont.IsAvailable)
                {
                    font = EmojiFont.Instance;
                    return true;
                }
                font = null;
                return false;
            }

            return fonts.TryGetValue(fontId, out font);
        }

        private void RegisterFamilyFont(Core font, ushort familyIndex)
        {
            if (font == null) return;
            var fontId = GetFontId(font);
            RegisterFont(fontId, font);
            if (!fontIdToFamilyIndex.ContainsKey(fontId))
                fontIdToFamilyIndex[fontId] = familyIndex;
        }

        /// <summary>Strut metrics from the primary font, scaled to <paramref name="size"/> points. <paramref name="descender"/> is negative; <paramref name="lineHeight"/> falls back to 1.2 × extent when the font omits it.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetLineMetrics(float size, out float ascender, out float descender, out float lineHeight)
        {
            var faceInfo = primaryFont.FaceInfo;
            var scale = RawScale(primaryFont, size);
            ascender = faceInfo.ascentLine * scale;
            descender = faceInfo.descentLine * scale;
            lineHeight = faceInfo.lineHeight * scale;

            if (lineHeight <= 0)
                lineHeight = (ascender - descender) * 1.2f;
        }

        /// <summary>Cap height (top of capital letters) of the primary font, scaled to <paramref name="size"/> points. Returns 0 when unavailable.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetCapHeight(float size)
        {
            var faceInfo = primaryFont.FaceInfo;
            if (faceInfo.capLine <= 0) return 0f;
            return faceInfo.capLine * RawScale(primaryFont, size);
        }

        /// <summary>x-height (top of lowercase letters) of the primary font, scaled to <paramref name="size"/> points. Returns 0 when unavailable.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float GetXHeight(float size)
        {
            var faceInfo = primaryFont.FaceInfo;
            if (faceInfo.meanLine <= 0) return 0f;
            return faceInfo.meanLine * RawScale(primaryFont, size);
        }

        /// <summary>Typographic ascent/descent (OS/2 sTypo) of the primary font, scaled to <paramref name="size"/> points — the designer's tight content box. Falls back to the selected ascent/descent line when the font omits typo metrics. <paramref name="descent"/> is negative.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void GetTypoMetrics(float size, out float ascent, out float descent)
        {
            var faceInfo = primaryFont.FaceInfo;
            var scale = RawScale(primaryFont, size);
            ascent = (faceInfo.typoAscent > 0 ? faceInfo.typoAscent : faceInfo.ascentLine) * scale;
            descent = (faceInfo.typoDescent < 0 ? faceInfo.typoDescent : faceInfo.descentLine) * scale;
        }

        /// <summary>Resolves a <see cref="FontFamily.name"/> to its primary font id, registering the runtime with this provider if needed. Returns 0 when the name is empty or unknown. Case-sensitive, first-wins across the family chain.</summary>
        public int TryGetFontIdByFamilyName(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            for (var i = 0; i < resolvedFamilies.Length; i++)
            {
                if (resolvedFamilies[i].name != name) continue;
                var primary = resolvedFamilies[i].Primary.Runtime;
                if (primary == null) return 0;
                var fontId = GetFontId(primary);
                if (!fonts.ContainsKey(fontId))
                    RegisterFamilyFont(primary, (ushort)i);
                return fontId;
            }
            return 0;
        }

        /// <summary>Finds the best font id to render <paramref name="codepoint"/> via the fallback chain; result is cached in <see cref="SharedFontCache"/>.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="codepoint"/> is not a Unicode scalar value.</exception>
        public int FindFontForCodepoint(int codepoint) => FindFontForCodepoint(codepoint, 0);

        /// <summary>Same as <see cref="FindFontForCodepoint(int)"/> but prefers families whose <see cref="FontFamily.preferredLanguage"/> matches the given language registry index.</summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="codepoint"/> is not a Unicode scalar value.</exception>
        public int FindFontForCodepoint(int codepoint, byte language)
        {
            if (!Utf16.IsUnicodeScalar(codepoint))
                throw new ArgumentOutOfRangeException(nameof(codepoint), codepoint,
                    "The value must be a Unicode scalar value.");
            Span<int> cluster = stackalloc int[1];
            cluster[0] = codepoint;
            if (UnicodeData.Provider.IsEmojiPresentation(codepoint))
                return ResolveColorClusterFontId(cluster);

            var request = default(FontFaceRequest);
            var fontId = ResolveSequence(cluster, language, 0, in request, out var instance)
                ? instance.fontId
                : primaryFontId;
            return fontId;
        }

        internal bool ResolveSequence(ReadOnlySpan<int> sequence, byte language, int explicitFontId,
            in FontFaceRequest request, out ResolvedFontInstance instance)
            => ResolveSequence(sequence, language, explicitFontId, in request, -1, false,
                out instance, out _);

        internal bool ResolveSequence(ReadOnlySpan<int> sequence, byte language, int explicitFontId,
            in FontFaceRequest request, int sequenceStart, bool contextual,
            out ResolvedFontInstance instance, out bool deferred)
        {
            instance = default;
            deferred = false;
            if (sequence.IsEmpty || primaryFont == null) return false;

            if (sequence.Length != 1 || request.hasVariation)
                return ResolveSequenceUncached(sequence, language, explicitFontId, in request,
                    sequenceStart, contextual ? SystemFont.ResolutionMode.Contextual
                        : SystemFont.ResolutionMode.Standard, out instance, out deferred);

            var styleKey = GetStyleCacheKey(in request);
            var codepoint = sequence[0];
            if (SharedFontCache.TryGet(codepoint, primaryFontId, contextId, language,
                    styleKey, explicitFontId, out var cachedFontId)
                && TryGetFont(cachedFontId, out var cachedFont)
                && (!contextual || !cachedFont.isSystemFont && CoversSequence(cachedFont, sequence))
                && BuildInstance(cachedFont, in request, out instance))
            {
                if (instance.fontId != cachedFontId)
                    SharedFontCache.Set(codepoint, primaryFontId, contextId, language,
                        styleKey, explicitFontId, instance.fontId);
                return true;
            }

            var resolved = ResolveSequenceUncached(sequence, language, explicitFontId, in request,
                sequenceStart, contextual ? SystemFont.ResolutionMode.Contextual
                    : SystemFont.ResolutionMode.Standard, out instance, out deferred);
            if (resolved && (!contextual
                             || !instance.font.isSystemFont && CoversSequence(instance.font, sequence)))
                SharedFontCache.Set(codepoint, primaryFontId, contextId, language,
                    styleKey, explicitFontId, instance.fontId);
            return resolved;
        }

        internal bool TryResolveCoveringSequence(ReadOnlySpan<int> sequence, byte language, int explicitFontId,
            in FontFaceRequest request, int sequenceStart,
            out ResolvedFontInstance instance, out bool deferred)
        {
            instance = default;
            deferred = false;
            return !sequence.IsEmpty && primaryFont != null && SupportsCoherentSequenceResolution
                   && ResolveSequenceUncached(sequence, language, explicitFontId, in request,
                       sequenceStart, SystemFont.ResolutionMode.CoveringSequence,
                       out instance, out deferred);
        }

        private bool ResolveSequenceUncached(ReadOnlySpan<int> sequence, byte language, int explicitFontId,
            in FontFaceRequest request, int sequenceStart,
            SystemFont.ResolutionMode mode,
            out ResolvedFontInstance instance, out bool deferred)
        {
            instance = default;
            deferred = false;
            var requireCoverage = mode == SystemFont.ResolutionMode.CoveringSequence;
            var languageTag = language != 0 ? LanguageRegistry.GetTag(language) : string.Empty;

            if (explicitFontId != 0
                && TryGetFont(explicitFontId, out var explicitFont))
            {
                if (TryGetFamilyIndex(explicitFontId, out var explicitFamily)
                    && explicitFamily < resolvedFamilies.Length)
                {
                    if (explicitFont.isSystemFont)
                    {
                        if (TryResolveSystemFamily(explicitFamily, explicitFont, sequence,
                                languageTag, in request, sequenceStart, mode,
                                out instance, out deferred)) return true;
                        if (deferred) return false;
                    }
                    else if (TryResolveFamily(explicitFamily, sequence, languageTag, in request,
                                 sequenceStart, mode,
                                 out instance, out deferred))
                    {
                        return true;
                    }
                    else if (deferred) return false;
                }
                if (CoversSequence(explicitFont, sequence))
                    return BuildInstance(explicitFont, in request, out instance);
            }

            if (!string.IsNullOrEmpty(languageTag))
            {
                for (ushort i = 0; i < resolvedFamilies.Length; i++)
                {
                    var preferred = resolvedFamilies[i].preferredLanguage;
                    if (!LanguageMatching.Matches(preferred, languageTag)) continue;
                    if (TryResolveFamily(i, sequence, languageTag, in request, sequenceStart,
                            mode, out instance, out deferred)) return true;
                    if (deferred) return false;
                }
            }

            for (ushort i = 0; i < resolvedFamilies.Length; i++)
            {
                var preferred = resolvedFamilies[i].preferredLanguage;
                if (!string.IsNullOrEmpty(languageTag) && LanguageMatching.Matches(preferred, languageTag))
                    continue;
                if (TryResolveFamily(i, sequence, languageTag, in request, sequenceStart,
                        mode, out instance, out deferred)) return true;
                if (deferred) return false;
            }

            return (!requireCoverage || CoversSequence(primaryFont, sequence))
                   && BuildInstance(primaryFont, in request, out instance);
        }

        private static int GetStyleCacheKey(in FontFaceRequest request)
        {
            var key = Math.Clamp(request.weight, 0, 1023);
            if (request.requestsWeight) key |= 1 << 10;
            if (request.requestsSlant) key |= 1 << 11;
            if (request.allowsRealWeight) key |= 1 << 12;
            if (request.allowsRealSlant) key |= 1 << 13;
            if (request.allowsSyntheticWeight) key |= 1 << 14;
            if (request.allowsSyntheticSlant) key |= 1 << 15;
            return key;
        }

        private bool TryResolveFamily(ushort familyIndex, ReadOnlySpan<int> sequence, string language,
            in FontFaceRequest request, int sequenceStart, SystemFont.ResolutionMode mode,
            out ResolvedFontInstance instance, out bool deferred)
        {
            instance = default;
            deferred = false;
            if (familyIndex >= resolvedFamilies.Length) return false;

            ref var family = ref resolvedFamilies[familyIndex];
            var primaryDescriptor = family.Primary;
            if (!primaryDescriptor.IsValid) return false;

            if (primaryDescriptor.isSystemFont)
            {
                var primary = primaryDescriptor.Runtime;
                return primary != null
                       && TryResolveSystemFamily(familyIndex, primary, sequence, language,
                           in request, sequenceStart, mode,
                           out instance, out deferred);
            }

            var best = FindBestCoveringFace(ref family, sequence, in request);
            if (best == null || !BuildInstance(best, in request, out instance)) return false;
            RegisterResolvedFamily(instance.fontId, familyIndex);
            return true;
        }

        private bool TryResolveSystemFamily(ushort familyIndex, Core coveringCandidate,
            ReadOnlySpan<int> sequence, string language, in FontFaceRequest request,
            int sequenceStart, SystemFont.ResolutionMode mode,
            out ResolvedFontInstance instance, out bool deferred)
        {
            instance = default;
            deferred = false;
            DeriveSystemStyle(in request, out var weight, out var italic);
            if (mode == SystemFont.ResolutionMode.CoveringSequence && weight == 0 && !italic
                && CoversSequence(coveringCandidate, sequence)
                && BuildInstance(coveringCandidate, in request, out instance))
            {
                RegisterResolvedFamily(instance.fontId, familyIndex);
                return true;
            }
            var systemFont = ResolveSystemSequence(sequence, language, weight, italic,
                coveringCandidate, coveringCandidate.FaceInfo.familyName, sequenceStart,
                mode, out deferred);
            if (deferred) return false;
            if (systemFont == null && (weight > 0 || italic))
                systemFont = ResolveSystemSequence(sequence, language, 0, false,
                    coveringCandidate, coveringCandidate.FaceInfo.familyName, sequenceStart,
                    mode, out deferred);
            if (deferred || systemFont == null
                         || !BuildInstance(systemFont, in request, out instance)) return false;
            RegisterResolvedFamily(instance.fontId, familyIndex);
            return true;
        }

        private static Core ResolveSystemSequence(ReadOnlySpan<int> sequence, string language,
            int weight, bool italic, Core stylePrimary, string family, int sequenceStart,
            SystemFont.ResolutionMode mode, out bool deferred)
            => mode == SystemFont.ResolutionMode.CoveringSequence
                ? SystemFont.TryResolveCoveringSequence(sequence, language, weight, italic,
                    stylePrimary, family, sequenceStart, out deferred)
                : SystemFont.TryResolveSequence(sequence, language, weight, italic,
                    stylePrimary, family, sequenceStart,
                    mode == SystemFont.ResolutionMode.Contextual, out deferred);

        private void RegisterResolvedFamily(int fontId, ushort familyIndex)
        {
            if (fontIdToFamilyIndex.ContainsKey(fontId)) return;
            lock (registerLock)
                if (!fontIdToFamilyIndex.ContainsKey(fontId))
                    fontIdToFamilyIndex[fontId] = familyIndex;
        }

        private static Core FindBestCoveringFace(ref ResolvedFamily family, ReadOnlySpan<int> sequence,
            in FontFaceRequest request)
        {
            var descriptors = family.lookup.fonts;
            if (descriptors == null || descriptors.Length == 0) return null;

            byte[] rented = null;
            Span<byte> rejected = descriptors.Length <= 64
                ? stackalloc byte[descriptors.Length]
                : (rented = ArrayPool<byte>.Rent(descriptors.Length));
            rejected = rejected.Slice(0, descriptors.Length);
            rejected.Clear();
            try
            {
                while (true)
                {
                    var bestIndex = -1;
                    var bestScore = int.MaxValue;
                    var bestIsMaterialized = false;
                    Core best = null;

                    for (var i = 0; i < descriptors.Length; i++)
                    {
                        if (rejected[i] != 0) continue;
                        var descriptor = descriptors[i];
                        var materialized = descriptor.IsMaterialized;
                        var font = descriptor.ExistingRuntime;
                        if (materialized && font == null)
                        {
                            rejected[i] = 1;
                            continue;
                        }

                        var score = materialized
                            ? ScoreCandidate(font, in request)
                            : descriptor.variableAxesKnown
                                ? ScoreCandidate(in descriptor, in request)
                                : request.NeedsFaceResolution ? -1 : 0;
                        if (score >= bestScore) continue;
                        bestIndex = i;
                        bestScore = score;
                        bestIsMaterialized = materialized;
                        best = font;
                    }

                    if (bestIndex < 0) return null;
                    if (!bestIsMaterialized)
                    {
                        _ = descriptors[bestIndex].Runtime;
                        continue;
                    }
                    if (CoversSequence(best, sequence)) return best;
                    rejected[bestIndex] = 1;
                }
            }
            finally
            {
                if (rented != null) ArrayPool<byte>.Return(rented);
            }
        }

        private static int ScoreCandidate(Core font, in FontFaceRequest request)
        {
            var axes = font.VariableAxes;
            font.GetDefaultStyle(out var defaultWeight, out var effectiveSlant);
            return ScoreCandidate(axes, defaultWeight, effectiveSlant, in request);
        }

        private static int ScoreCandidate(in FontFaceDescriptor font,
            in FontFaceRequest request)
        {
            Core.ResolveDefaultStyle(in font.faceInfo, font.variableAxes,
                font.defaultAxisValues, out var defaultWeight, out var effectiveSlant);
            return ScoreCandidate(font.variableAxes, defaultWeight, effectiveSlant,
                in request);
        }

        private static int ScoreCandidate(HB.hb_ot_var_axis_info_t[] axes,
            int defaultWeight, bool effectiveSlant, in FontFaceRequest request)
        {
            var hasWght = TryGetAxis(axes, FontVariation.axisTags[0], out var weightAxis);
            var canItal = TryGetAxis(axes, FontVariation.axisTags[2], out var italicAxis)
                          && italicAxis.maxValue >= 0.5f;
            var canSlnt = TryGetAxis(axes, FontVariation.axisTags[3], out var slantAxis)
                          && Math.Max(Math.Abs(slantAxis.minValue), Math.Abs(slantAxis.maxValue)) > 0.01f;

            var score = CountUnsupportedAxes(axes, request.variation.mask) * 1_000_000;
            var wantsRealSlant = request.requestsSlant && request.allowsRealSlant;
            if (wantsRealSlant
                    ? !effectiveSlant && !canItal && !canSlnt
                    : effectiveSlant)
                score += 100_000;
            var targetWeight = request.requestsWeight && request.allowsRealWeight ? request.weight : 400;
            var variableTargetWeight = targetWeight;
            if (!request.requestsWeight && request.hasVariation
                && (request.variation.mask & AxisMask.Wght) != 0
                && request.variation.wght.mode == AxisValueMode.Absolute
                && !float.IsNaN(request.variation.wght.value)
                && !float.IsInfinity(request.variation.wght.value))
                variableTargetWeight = Math.Clamp((int)Math.Round(request.variation.wght.value), 1, 1000);
            if (hasWght)
            {
                var resolvesRequestedWeight = request.requestsWeight && request.allowsRealWeight
                                              || request.hasVariation
                                              && (request.variation.mask & AxisMask.Wght) != 0;
                var candidateWeight = resolvesRequestedWeight
                    ? (int)Math.Round(Math.Clamp((float)variableTargetWeight, weightAxis.minValue, weightAxis.maxValue))
                    : defaultWeight;
                score += FontStyleEncoding.CssWeightMatchScore(candidateWeight, variableTargetWeight);
            }
            else
            {
                score += FontStyleEncoding.CssWeightMatchScore(defaultWeight, targetWeight);
            }
            if (request.NeedsFaceResolution && axes is { Length: > 0 }) score--;
            return score;
        }

        private bool BuildInstance(Core font, in FontFaceRequest request, out ResolvedFontInstance instance)
        {
            instance = default;
            if (font == null) return false;
            if (hasLastInstance && ReferenceEquals(lastInstanceFont, font)
                                && lastInstanceRequest.Equals(request))
            {
                instance = lastInstance;
                if (instance.IsVariableInstance) MarkVariationUsed(instance.fontId);
                return true;
            }
            var fontId = GetFontId(font);

            if (!request.hasVariation && !request.requestsWeight && !request.requestsSlant)
            {
                RegisterFont(fontId, font);
                instance = new ResolvedFontInstance
                {
                    font = font,
                    fontId = fontId,
                    effectiveWeight = (ushort)font.DefaultWeight,
                };
                CacheInstance(font, in request, in instance);
                return true;
            }

            var realization = FontStyleRealization.None;
            var axes = font.VariableAxes;
            var hasWght = HasAxis(axes, FontVariation.axisTags[0]);
            var hasItal = HasAxis(axes, FontVariation.axisTags[2]);
            var hasSlnt = HasAxis(axes, FontVariation.axisTags[3]);
            var canItal = TryGetAxis(axes, FontVariation.axisTags[2], out var italicAxis)
                          && italicAxis.maxValue >= 0.5f;
            var canSlnt = TryGetAxis(axes, FontVariation.axisTags[3], out var slantAxis)
                          && Math.Max(Math.Abs(slantAxis.minValue), Math.Abs(slantAxis.maxValue)) > 0.01f;
            var merged = request.hasVariation ? request.variation : default;
            font.GetDefaultStyle(out var effectiveWeight, out var effectiveSlant);
            var defaults = axes != null ? font.DefaultAxisValues : null;
            var defaultSlant = 0f;

            if (hasSlnt && TryGetAxisValue(axes, defaults, FontVariation.axisTags[3], out defaultSlant))
                defaultSlant = Math.Abs(defaultSlant) > 0.01f ? defaultSlant : 0f;

            if (request.requestsWeight && request.allowsRealWeight && hasWght)
            {
                merged.wght = new AxisValue { value = request.weight, mode = AxisValueMode.Absolute };
                merged.mask |= AxisMask.Wght;
            }

            if (request.requestsSlant && request.allowsRealSlant)
            {
                if (canItal)
                {
                    merged.ital = new AxisValue { value = 1f, mode = AxisValueMode.Absolute };
                    merged.mask |= AxisMask.Ital;
                }
                else if (canSlnt)
                {
                    merged.slnt = new AxisValue
                    {
                        value = font.isSystemFont && Math.Abs(defaultSlant) > 0.01f
                            ? defaultSlant
                            : -12f,
                        mode = AxisValueMode.Absolute
                    };
                    merged.mask |= AxisMask.Slnt;
                }
            }

            long varHash48 = 0;
            HB.hb_variation_t[] hbVariations = null;
            int[] ftCoords = null;

            if (axes != null && HasSupportedAxis(axes, merged.mask))
            {
                var resolved = ArrayPool<float>.Rent(axes.Length);
                try
                {
                    FontVariation.ResolveAxes(in merged, axes, font.DefaultAxisValues, resolved);
                    effectiveSlant = hasItal || hasSlnt ? false : font.FaceInfo.isItalic;
                    if (hasWght && TryGetAxisValue(axes, resolved, FontVariation.axisTags[0], out var resolvedWeight))
                        effectiveWeight = Math.Clamp((int)Math.Round(resolvedWeight), 1, 1000);
                    if (hasItal && TryGetAxisValue(axes, resolved, FontVariation.axisTags[2], out var resolvedItalic))
                        effectiveSlant = resolvedItalic >= 0.5f;
                    if (hasSlnt && TryGetAxisValue(axes, resolved, FontVariation.axisTags[3], out var resolvedSlant))
                        effectiveSlant |= Math.Abs(resolvedSlant) > 0.01f;
                    if (AxisValuesEqual(resolved, defaults, axes.Length))
                    {
                        RegisterFont(fontId, font);
                    }
                    else
                    {
                        fontId = RegisterVariationInstance(font, resolved, axes,
                            out varHash48, out hbVariations, out ftCoords);
                    }
                }
                finally
                {
                    ArrayPool<float>.Return(resolved);
                }
            }
            else
            {
                RegisterFont(fontId, font);
            }

            if (request.requestsWeight)
            {
                if (request.allowsRealWeight && effectiveWeight >= request.weight)
                    realization |= FontStyleRealization.RealWeight;
                else if (request.allowsSyntheticWeight && effectiveWeight < request.weight)
                    realization |= FontStyleRealization.SyntheticWeight;
            }

            if (request.requestsSlant)
            {
                if (request.allowsRealSlant && effectiveSlant)
                    realization |= FontStyleRealization.RealSlant;
                else if (request.allowsSyntheticSlant)
                    realization |= FontStyleRealization.SyntheticSlant;
            }

            instance = new ResolvedFontInstance
            {
                font = font,
                fontId = fontId,
                varHash48 = varHash48,
                hbVariations = hbVariations,
                ftCoords = ftCoords,
                realization = realization,
                effectiveWeight = (ushort)effectiveWeight,
            };
            CacheInstance(font, in request, in instance);
            return true;
        }

        private void CacheInstance(Core font, in FontFaceRequest request,
            in ResolvedFontInstance instance)
        {
            lastInstanceFont = font;
            lastInstanceRequest = request;
            lastInstance = instance;
            hasLastInstance = true;
        }

        internal readonly struct VariationPass : IDisposable
        {
            private readonly UniTextFontProvider provider;

            internal VariationPass(UniTextFontProvider provider) => this.provider = provider;

            public void Dispose() => provider?.EndVariationPass();
        }

        internal VariationPass BeginVariationPass()
        {
            usedVariationIds.Clear();
            hasLastInstance = false;
            return new VariationPass(this);
        }

        internal void EndVariationPass()
        {
            if (activeVariationIds.Count == 0) return;
            releasedVariationIds.Clear();
            foreach (var fontId in activeVariationIds)
                if (!usedVariationIds.Contains(fontId)) releasedVariationIds.Add(fontId);
            for (var i = 0; i < releasedVariationIds.Count; i++)
            {
                var fontId = releasedVariationIds[i];
                activeVariationIds.Remove(fontId);
                fonts.Remove(fontId);
                ReleaseVariation(fontId);
            }
            TrimVariationRegistrations();
        }

        private void MarkVariationUsed(int fontId)
        {
            usedVariationIds.Add(fontId);
            if (!activeVariationIds.Add(fontId)) return;
            lock (variationLock)
            {
                if (!variationInstancesById.TryGetValue(fontId, out var registration))
                    throw new InvalidOperationException($"Unknown variation fontId {fontId}.");
                if (registration.leases == 0) inactiveVariationCount--;
                registration.leases++;
                registration.lastUse = ++variationUseClock;
            }
        }

        private static void ReleaseVariation(int fontId)
        {
            lock (variationLock)
                if (variationInstancesById.TryGetValue(fontId, out var registration)
                    && registration.leases > 0)
                {
                    registration.leases--;
                    if (registration.leases == 0) inactiveVariationCount++;
                    registration.lastUse = ++variationUseClock;
                }
        }

        private int RegisterVariationInstance(Core font, float[] resolved,
            HB.hb_ot_var_axis_info_t[] axes, out long varHash48,
            out HB.hb_variation_t[] hbVariations, out int[] ftCoords)
        {
            var registrationKey = ComputeVariationRegistrationKey(font.FontDataHash, resolved, axes.Length);
            VariationInstanceRegistration registration = null;
            lock (variationLock)
            {
                if (!variationInstances.TryGetValue(registrationKey, out var bucket))
                {
                    bucket = new List<VariationInstanceRegistration>(1);
                    variationInstances[registrationKey] = bucket;
                }

                for (var i = bucket.Count - 1; i >= 0; i--)
                {
                    var candidate = bucket[i];
                    if (!candidate.font.TryGetTarget(out var registeredFont))
                    {
                        bucket.RemoveAt(i);
                        variationInstancesById.Remove(candidate.fontId);
                        if (candidate.leases == 0) inactiveVariationCount--;
                        continue;
                    }
                    if (!ReferenceEquals(registeredFont, font)
                        || !CoordinatesEqual(candidate.ftCoords, resolved, axes.Length))
                        continue;
                    registration = candidate;
                    break;
                }

                if (registration == null)
                {
                    FontVariation.BuildInstanceArrays(resolved, axes, out var builtHb, out var builtFt);
                    var fontId = AllocateVariationFontId();
                    registration = new VariationInstanceRegistration
                    {
                        font = new WeakReference<Core>(font),
                        ftCoords = builtFt,
                        hbVariations = builtHb,
                        fontId = fontId,
                        varHash48 = VariationAtlasId(fontId),
                        registrationKey = registrationKey,
                        lastUse = ++variationUseClock,
                    };
                    bucket.Add(registration);
                    variationInstancesById[fontId] = registration;
                    inactiveVariationCount++;
                }
                registration.lastUse = ++variationUseClock;
            }
            RegisterFont(registration.fontId, font);
            MarkVariationUsed(registration.fontId);
            hbVariations = registration.hbVariations;
            ftCoords = registration.ftCoords;
            varHash48 = registration.varHash48;
            TrimVariationRegistrations();
            return registration.fontId;
        }

        /// <summary>Releases every exact variation registration not leased by a live provider. Active shaping and raster data is preserved.</summary>
        public static void TrimUnusedVariations() => TrimVariationRegistrations(true);

        private static void TrimVariationRegistrations(bool aggressive = false)
        {
            List<VariationInstanceRegistration> evicted = null;
            lock (variationLock)
            {
                var target = aggressive ? 0 : inactiveVariationCapacity;
                if (inactiveVariationCount <= target) return;
                var candidates = new List<VariationInstanceRegistration>(inactiveVariationCount);
                foreach (var candidate in variationInstancesById.Values)
                    if (candidate.leases == 0) candidates.Add(candidate);
                candidates.Sort((left, right) => left.lastUse.CompareTo(right.lastUse));
                var removeCount = Math.Min(inactiveVariationCount - target, candidates.Count);
                evicted = new List<VariationInstanceRegistration>(removeCount);
                for (var i = 0; i < removeCount; i++)
                {
                    var oldest = candidates[i];
                    variationInstancesById.Remove(oldest.fontId);
                    if (variationInstances.TryGetValue(oldest.registrationKey, out var bucket))
                    {
                        bucket.Remove(oldest);
                        if (bucket.Count == 0) variationInstances.Remove(oldest.registrationKey);
                    }
                    evicted.Add(oldest);
                    inactiveVariationCount--;
                }
                if (removeCount >= 256 && removeCount >= variationInstancesById.Count)
                {
                    variationInstancesById.TrimExcess();
                    variationInstances.TrimExcess();
                }
            }

            if (evicted == null) return;
            for (var i = 0; i < evicted.Count; i++)
            {
                var registration = evicted[i];
                if (!registration.font.TryGetTarget(out var font)) continue;
                Shaper.RetireVariation(font, registration.fontId);
                font.ClearVariation(registration.varHash48);
            }
        }

        private static long ComputeVariationRegistrationKey(int fontId, float[] coordinates, int count)
        {
            unchecked
            {
                ulong value = ((ulong)(uint)fontId ^ 14695981039346656037UL) * 1099511628211UL;
                for (var i = 0; i < count; i++)
                    value = (value ^ (uint)FontVariation.ToFixed(coordinates[i])) * 1099511628211UL;
                return (long)value;
            }
        }

        private static long VariationAtlasId(int fontId) => 0x1_0000_0000L | (uint)fontId;

        private static int AllocateVariationFontId()
        {
            var fontId = System.Threading.Interlocked.Increment(ref nextVariationFontId);
            if (fontId >= EmojiFont.FontId)
                throw new InvalidOperationException("UniText variation identity space exhausted.");
            return fontId;
        }

        private static bool CoordinatesEqual(int[] left, float[] right, int count)
        {
            if (left == null || right == null || left.Length != count || right.Length < count) return false;
            for (var i = 0; i < count; i++)
                if (left[i] != FontVariation.ToFixed(right[i])) return false;
            return true;
        }

        private static bool AxisValuesEqual(float[] left, float[] right, int count)
        {
            if (left == null || right == null || left.Length < count || right.Length < count) return false;
            for (var i = 0; i < count; i++)
                if (FontVariation.ToFixed(left[i]) != FontVariation.ToFixed(right[i])) return false;
            return true;
        }

        private static bool CoversSequence(Core font, ReadOnlySpan<int> sequence)
        {
            if (font == null) return false;
            var checkedCodepoint = false;
            for (var i = 0; i < sequence.Length; i++)
            {
                var codepoint = sequence[i];
                if (UnicodeData.IsDefaultIgnorable(codepoint)) continue;
                checkedCodepoint = true;
                if (Shaper.GetGlyphIndex(font, (uint)codepoint) == 0) return false;
            }
            return checkedCodepoint || !sequence.IsEmpty;
        }

        private static bool HasSupportedAxis(HB.hb_ot_var_axis_info_t[] axes, AxisMask mask)
        {
            if (axes == null || mask == AxisMask.None) return false;
            for (var i = 0; i < FontVariation.AxisCount; i++)
                if ((mask & (AxisMask)(1 << i)) != 0 && HasAxis(axes, FontVariation.axisTags[i]))
                    return true;
            return false;
        }

        private static int CountUnsupportedAxes(HB.hb_ot_var_axis_info_t[] axes, AxisMask mask)
        {
            var count = 0;
            for (var i = 0; i < FontVariation.AxisCount; i++)
                if ((mask & (AxisMask)(1 << i)) != 0 && !HasAxis(axes, FontVariation.axisTags[i]))
                    count++;
            return count;
        }

        private static bool HasAxis(HB.hb_ot_var_axis_info_t[] axes, uint tag)
        {
            return TryGetAxis(axes, tag, out _);
        }

        private static bool TryGetAxis(HB.hb_ot_var_axis_info_t[] axes, uint tag,
            out HB.hb_ot_var_axis_info_t axis)
        {
            if (axes != null)
                for (var i = 0; i < axes.Length; i++)
                    if (axes[i].tag == tag)
                    {
                        axis = axes[i];
                        return true;
                    }
            axis = default;
            return false;
        }

        private static bool TryGetAxisValue(HB.hb_ot_var_axis_info_t[] axes, float[] values,
            uint tag, out float value)
        {
            value = 0f;
            if (axes == null || values == null) return false;
            for (var i = 0; i < axes.Length && i < values.Length; i++)
                if (axes[i].tag == tag)
                {
                    value = values[i];
                    return true;
                }
            return false;
        }

        private static void DeriveSystemStyle(in FontFaceRequest request, out int weight, out bool italic)
        {
            weight = request.requestsWeight && request.allowsRealWeight ? request.weight : 0;
            italic = request.requestsSlant && request.allowsRealSlant;

            if (!request.hasVariation) return;
            var variation = request.variation;
            if (weight == 0 && (variation.mask & AxisMask.Wght) != 0
                && variation.wght.mode == AxisValueMode.Absolute
                && !float.IsNaN(variation.wght.value)
                && !float.IsInfinity(variation.wght.value))
                weight = Math.Clamp((int)variation.wght.value, 1, 1000);
            if (!italic && (variation.mask & AxisMask.Ital) != 0 && variation.ital.value >= 0.5f)
                italic = true;
            if (!italic && (variation.mask & AxisMask.Slnt) != 0 && Math.Abs(variation.slnt.value) > 0.01f)
                italic = true;
        }

        private Core ResolveAssignedColorFont(uint codepoint)
        {
            for (var i = 0; i < resolvedFamilies.Length; i++)
            {
                var primary = resolvedFamilies[i].Primary;
                if (!primary.isColor) continue;
                var font = primary.Runtime;
                if (font is not ColorFontCore color || !color.CanRasterizeColor) continue;
                if (Shaper.GetGlyphIndex(font, codepoint) != 0) return font;
            }
            return null;
        }

        /// <summary>Font id for an emoji cluster: an assigned color font wins, followed by the platform's system emoji cascade.</summary>
        internal int ResolveColorClusterFontId(ReadOnlySpan<int> cluster)
        {
            if (cluster.IsEmpty) return 0;
            if (hasColorFamily)
            {
                var font = ResolveAssignedColorFont((uint)cluster[0]);
                if (font != null)
                {
                    var id = GetFontId(font);
                    if (id != EmojiFont.FontId && !fonts.ContainsKey(id))
                        RegisterFont(id, font);
                    return id;
                }
            }

            var systemFont = EmojiFont.ResolveCluster(cluster);
            if (systemFont == null) return 0;
            var systemId = GetFontId(systemFont);
            if (systemId != EmojiFont.FontId && !fonts.ContainsKey(systemId))
                RegisterFont(systemId, systemFont);
            return systemId;
        }

        /// <summary>Family index for a font id, or <see cref="ushort.MaxValue"/> when the runtime has no family owner.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetFamilyIndex(int fontId)
        {
            return fontIdToFamilyIndex.TryGetValue(fontId, out var idx) ? idx : ushort.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetFamilyIndex(int fontId, out ushort familyIndex)
            => fontIdToFamilyIndex.TryGetValue(fontId, out familyIndex);

        /// <summary>Creates a managed snapshot of the complete font file for a font id, allocating and copying its full source, or returns null when unavailable.</summary>
        public byte[] GetFontData(int fontId)
        {
            return GetFont(fontId)?.CopyFontData();
        }
    }
}
