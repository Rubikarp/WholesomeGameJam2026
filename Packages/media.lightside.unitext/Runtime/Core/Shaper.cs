using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Text shaper that converts codepoints to positioned glyphs through a runtime font face.
    /// SFNT-backed fonts use HarfBuzz for full OpenType shaping; platform-native faces supply
    /// equivalent glyph and positioning results through their backend.
    /// </summary>
    public sealed class Shaper
    {
        private static Shaper instance;

        private static FastIntDictionary<WeakReference<UniTextFont.Core>> fontOwners = new();
        private static readonly object fontCacheLock = new();
        private static readonly object bufferLock = new();
        private static readonly List<IntPtr> buffers = new();

        private const int SmcpBit = 0;
        private const int SupsBit = 2;
        private const int SubsBit = 4;

        [ThreadStatic] private static IntPtr reusableBuffer;
        [ThreadStatic] private static RawShapedGlyph[] rawScratch;
        [ThreadStatic] private static WordTok[] tokScratch;

    #if UNITY_EDITOR
        static Shaper()
        {
            EditorLifecycle.ManagedCleaned += DisposeAll;
        }
    #endif

        private static void DisposeAll()
        {
            instance = null;
            ClearAllCaches();
            WordShapeCache.Clear();
            rawScratch = null;
            tokScratch = null;

            lock (bufferLock)
            {
                for (var i = 0; i < buffers.Count; i++)
                    HB.DestroyBuffer(buffers[i]);
                buffers.Clear();
                reusableBuffer = IntPtr.Zero;
            }
        }

        /// <summary>Gets the singleton shaper instance.</summary>
        public static Shaper Instance
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => instance ??= new Shaper();
        }

        #region FontCacheEntry

        internal sealed class FontCacheEntry : IDisposable
        {
            private const int Active = 0;
            private const int Retired = 1;
            private const int Destroyed = 2;

            public readonly IntPtr hbFont;
            public readonly int upem;
            private int lifetimeState;
            private int activeUsers;

            public bool IsValid => Volatile.Read(ref lifetimeState) == Active && hbFont != IntPtr.Zero;
            internal bool IsDestroyed => Volatile.Read(ref lifetimeState) == Destroyed;

            private readonly FastIntDictionary<uint> glyphCache = new();
            private readonly FastIntDictionary<int> advanceCache = new();
            private readonly FastIntDictionary<HB.hb_glyph_extents_t> inkCache = new();
            private readonly object cacheLock = new();
            private Dictionary<ulong, uint> alternateGlyphCache;
            private byte featureSupport;
            private FastIntDictionary<byte> supsCodepointCache;
            private FastIntDictionary<byte> subsCodepointCache;

            private readonly FontBackingLease fontBacking;
            private readonly IntPtr hbBlob;
            private readonly IntPtr hbFace;

            internal readonly struct Lease : IDisposable
            {
                private readonly FontCacheEntry owner;
                internal IntPtr Font => owner?.hbFont ?? IntPtr.Zero;
                internal bool IsFor(FontCacheEntry entry) => ReferenceEquals(owner, entry);

                internal Lease(FontCacheEntry owner) => this.owner = owner;

                public void Dispose() => owner?.Release();
            }

            internal sealed class SubFont
            {
                internal IntPtr pointer;
                internal int users;
                internal bool retired;
            }

            internal readonly struct SubFontLease : IDisposable
            {
                private readonly FontCacheEntry owner;
                private readonly SubFont entry;
                internal IntPtr Pointer => entry?.pointer ?? IntPtr.Zero;

                internal SubFontLease(FontCacheEntry owner, SubFont entry)
                {
                    this.owner = owner;
                    this.entry = entry;
                }

                public void Dispose()
                {
                    if (entry != null) owner.ReleaseSubFont(entry);
                }
            }

            private readonly FastIntDictionary<SubFont> subFonts = new();
            private readonly object subFontsLock = new();

            public FontCacheEntry(FontBackingLease fontBacking, int faceIndex = 0)
            {
                this.fontBacking = fontBacking ?? throw new ArgumentNullException(nameof(fontBacking));
                hbFont = HB.CreateFont(IntPtr.Zero, fontBacking.Pointer, fontBacking.Length,
                    out hbBlob, out hbFace, out upem, faceIndex);
                if (hbFont == IntPtr.Zero)
                {
                    fontBacking.Dispose();
                    throw new InvalidOperationException("[HarfBuzz] Failed to create font.");
                }

                HB.MakeFaceImmutable(hbFace);
                HB.MakeFontImmutable(hbFont);
            }

            ~FontCacheEntry() => Dispose();

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref lifetimeState, Retired, Active) != Active) return;
                GC.SuppressFinalize(this);

                lock (subFontsLock)
                {
                    foreach (var kvp in subFonts)
                    {
                        kvp.Value.retired = true;
                        if (kvp.Value.users == 0) DestroySubFont(kvp.Value);
                    }
                    subFonts.Clear();
                }

                if (Volatile.Read(ref activeUsers) == 0) CompleteDispose();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal bool TryAcquire(out Lease lease)
            {
                while (Volatile.Read(ref lifetimeState) == Active)
                {
                    Interlocked.Increment(ref activeUsers);
                    if (Volatile.Read(ref lifetimeState) == Active)
                    {
                        lease = new Lease(this);
                        return true;
                    }
                    Release();
                }

                lease = default;
                return false;
            }

            /// <summary>
            /// Acquires an immutable variation sub-font under a base-font lease that must remain active for the sub-font lease's lifetime. Retirement removes it from lookup immediately and defers native destruction until the final active shape releases it.
            /// </summary>
            internal unsafe SubFontLease AcquireSubFont(Lease fontLease, int varKey, HB.hb_variation_t[] variations)
            {
                if (!fontLease.IsFor(this))
                    throw new InvalidOperationException("A variation sub-font requires an active lease for its base font.");
                lock (subFontsLock)
                {
                    if (subFonts.TryGetValue(varKey, out var existing))
                    {
                        existing.users++;
                        return new SubFontLease(this, existing);
                    }

                    var sub = HB.CreateSubFont(hbFont);
                    if (sub == IntPtr.Zero)
                        throw new InvalidOperationException("[HarfBuzz] Failed to create variation sub-font.");
                    try
                    {
                        if (variations != null && variations.Length > 0)
                        {
                            fixed (HB.hb_variation_t* ptr = variations)
                            {
                                HB.SetVariations(sub, ptr, variations.Length);
                            }
                        }

                        HB.MakeFontImmutable(sub);
                        var entry = new SubFont { pointer = sub, users = 1 };
                        subFonts[varKey] = entry;
                        sub = IntPtr.Zero;
                        return new SubFontLease(this, entry);
                    }
                    finally
                    {
                        if (sub != IntPtr.Zero) HB.DestroyFontOnly(sub);
                    }
                }
            }

            internal void RetireSubFont(int varKey)
            {
                lock (subFontsLock)
                {
                    if (!subFonts.TryGetValue(varKey, out var entry)) return;
                    subFonts.Remove(varKey);
                    entry.retired = true;
                    if (entry.users == 0) DestroySubFont(entry);
                }
            }

            private void ReleaseSubFont(SubFont entry)
            {
                lock (subFontsLock)
                {
                    entry.users--;
                    if (entry.retired && entry.users == 0) DestroySubFont(entry);
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void Release()
            {
                if (Interlocked.Decrement(ref activeUsers) == 0
                    && Volatile.Read(ref lifetimeState) == Retired)
                    CompleteDispose();
            }

            private void CompleteDispose()
            {
                if (Interlocked.CompareExchange(ref lifetimeState, Destroyed, Retired) != Retired) return;
                lock (subFontsLock)
                {
                    foreach (var kvp in subFonts)
                        DestroySubFont(kvp.Value);
                    subFonts.Clear();
                }
                HB.DestroyFont(hbFont, hbBlob, hbFace);
                fontBacking.Dispose();
            }

            private static void DestroySubFont(SubFont entry)
            {
                if (entry.pointer == IntPtr.Zero) return;
                HB.DestroyFontOnly(entry.pointer);
                entry.pointer = IntPtr.Zero;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryGetGlyph(uint codepoint, out uint glyphIndex)
            {
                var key = (int)codepoint;
                if (glyphCache.TryGetValue(key, out glyphIndex))
                    return glyphIndex != 0;

                if (!TryAcquire(out var lease))
                {
                    glyphIndex = 0;
                    return false;
                }
                try { HB.TryGetGlyph(hbFont, codepoint, out glyphIndex); }
                finally { lease.Dispose(); }
                lock (cacheLock) { glyphCache[key] = glyphIndex; }
                return glyphIndex != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int GetGlyphAdvance(uint glyphIndex)
            {
                var key = (int)glyphIndex;
                if (advanceCache.TryGetValue(key, out var cached))
                    return cached;

                if (!TryAcquire(out var lease)) return 0;
                int advance;
                try { advance = HB.GetGlyphAdvance(hbFont, glyphIndex); }
                finally { lease.Dispose(); }
                lock (cacheLock) { advanceCache[key] = advance; }
                return advance;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryGetGlyphInk(uint glyphIndex, out HB.hb_glyph_extents_t extents)
            {
                var key = (int)glyphIndex;
                if (inkCache.TryGetValue(key, out extents))
                    return extents.width != 0 && extents.height != 0;

                if (!TryAcquire(out var lease))
                {
                    extents = default;
                    return false;
                }
                try { HB.TryGetGlyphExtents(hbFont, glyphIndex, out extents); }
                finally { lease.Dispose(); }
                lock (cacheLock) { inkCache[key] = extents; }
                return extents.width != 0 && extents.height != 0;
            }

            /// <summary>
            /// Shapes one codepoint through a caller-owned OpenType feature set and caches the resulting
            /// alternate independently by font variation, script, and complete feature range.
            /// </summary>
            internal unsafe bool TryGetAlternateGlyph(IntPtr font, int variationKey, uint codepoint,
                uint script, HB.hb_feature_t[] features, out uint glyphIndex)
            {
                if (features == null || features.Length == 0)
                    return TryGetGlyph(codepoint, out glyphIndex);

                var key = XxHash64.Combine((uint)variationKey, codepoint);
                key = XxHash64.Combine(key, script);
                for (var i = 0; i < features.Length; i++)
                {
                    key = XxHash64.Combine(key, ((ulong)features[i].tag << 32) | features[i].value);
                    key = XxHash64.Combine(key, ((ulong)features[i].start << 32) | features[i].end);
                }
                key = XxHash64.Combine(key, (uint)features.Length);
                lock (cacheLock)
                    if (alternateGlyphCache != null && alternateGlyphCache.TryGetValue(key, out glyphIndex))
                        return glyphIndex != 0;

                if (!TryAcquire(out var lease))
                {
                    glyphIndex = 0;
                    return false;
                }

                try
                {
                    Span<int> input = stackalloc int[1];
                    input[0] = (int)codepoint;
                    var count = HB.ShapeRun(font, EnsureBuffer(), input, 0, 1,
                        HB.DIRECTION_LTR, script, HB.BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES,
                        features, features.Length, out var infos, out _);
                    glyphIndex = count == 1 ? infos[0].codepoint : 0;
                }
                finally
                {
                    lease.Dispose();
                }

                lock (cacheLock)
                {
                    alternateGlyphCache ??= new Dictionary<ulong, uint>();
                    alternateGlyphCache[key] = glyphIndex;
                }
                return glyphIndex != 0;
            }

            internal bool HasMathData()
            {
                if (!TryAcquire(out var lease))
                    throw new ObjectDisposedException(nameof(FontCacheEntry));
                try { return HB.MathHasData(hbFace); }
                finally { lease.Dispose(); }
            }

            private HB.hb_ot_var_axis_info_t[] cachedAxisInfos;
            private bool axisInfosQueried;

            public int GetAxisCount()
            {
                if (!TryAcquire(out var lease)) return 0;
                try { return (int)HB.GetAxisCount(hbFace); }
                finally { lease.Dispose(); }
            }

            public HB.hb_ot_var_axis_info_t[] GetAxisInfos()
            {
                if (Volatile.Read(ref axisInfosQueried))
                    return Volatile.Read(ref cachedAxisInfos);
                lock (cacheLock)
                {
                    if (axisInfosQueried) return cachedAxisInfos;

                    HB.hb_ot_var_axis_info_t[] result = null;
                    if (TryAcquire(out var lease))
                    {
                        try
                        {
                            var count = (int)HB.GetAxisCount(hbFace);
                            if (count > 0)
                            {
                                var buffer = new HB.hb_ot_var_axis_info_t[count];
                                var actual = HB.GetAxisInfos(hbFace, buffer);
                                if (actual > 0)
                                {
                                    if (actual < count)
                                        Array.Resize(ref buffer, actual);
                                    result = buffer;
                                }
                            }
                        }
                        finally { lease.Dispose(); }
                    }
                    Volatile.Write(ref cachedAxisInfos, result);
                    Volatile.Write(ref axisInfosQueried, true);
                    return result;
                }
            }

            internal bool TryGetFeature(int checkedBit, int supportedBit, out bool supported)
            {
                lock (cacheLock)
                {
                    supported = (featureSupport & supportedBit) != 0;
                    return (featureSupport & checkedBit) != 0;
                }
            }

            internal void SetFeature(int checkedBit, int supportedBit, bool supported)
            {
                lock (cacheLock)
                {
                    featureSupport |= (byte)checkedBit;
                    if (supported) featureSupport |= (byte)supportedBit;
                }
            }

            internal bool TryGetFeatureForCodepoint(int codepoint, bool superscript, out bool supported)
            {
                lock (cacheLock)
                {
                    var cache = superscript ? supsCodepointCache : subsCodepointCache;
                    if (cache != null && cache.TryGetValue(codepoint, out var value))
                    {
                        supported = value == 1;
                        return true;
                    }
                }

                supported = false;
                return false;
            }

            internal void SetFeatureForCodepoint(int codepoint, bool superscript, bool supported)
            {
                lock (cacheLock)
                {
                    if (superscript)
                    {
                        supsCodepointCache ??= new FastIntDictionary<byte>();
                        supsCodepointCache[codepoint] = supported ? (byte)1 : (byte)2;
                    }
                    else
                    {
                        subsCodepointCache ??= new FastIntDictionary<byte>();
                        subsCodepointCache[codepoint] = supported ? (byte)1 : (byte)2;
                    }
                }
            }
        }

        #endregion

        #region Cache Management

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FontCacheEntry GetOrCreateCoreCache(UniTextFont.Core font)
        {
            if (font == null || !font.HasFontData)
                return null;

            var entry = Volatile.Read(ref font.shaperCache);
            if (entry is { IsValid: true }) return entry;

            lock (fontCacheLock)
            {
                entry = font.shaperCache;
                if (entry is { IsValid: true }) return entry;
                entry?.Dispose();

                var fontHash = font.FontDataHash;
                if (fontHash == 0)
                    return null;

                var backing = font.OpenFontData();
                if (backing == null)
                    return null;
                try
                {
                    entry = new FontCacheEntry(backing, font.FaceInfo.faceIndex);
                    backing = null;
                }
                finally
                {
                    backing?.Dispose();
                }
                Volatile.Write(ref font.shaperCache, entry);
                TrimDeadFontOwners();
                fontOwners[fontHash] = new WeakReference<UniTextFont.Core>(font);
                return entry;
            }
        }

        private static void TrimDeadFontOwners()
        {
            if (fontOwners.Count < 1024 || (fontOwners.Count & 255) != 0) return;
            var live = new FastIntDictionary<WeakReference<UniTextFont.Core>>();
            foreach (var pair in fontOwners)
                if (pair.Value.TryGetTarget(out _)) live[pair.Key] = pair.Value;
            fontOwners = live;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAcquireCoreCache(UniTextFont.Core font, out FontCacheEntry entry, out FontCacheEntry.Lease lease)
        {
            while (true)
            {
                entry = GetOrCreateCoreCache(font);
                if (entry == null)
                {
                    lease = default;
                    return false;
                }
                if (entry.TryAcquire(out lease)) return true;
                Interlocked.CompareExchange(ref font.shaperCache, null, entry);
            }
        }

        private static void RetireCoreCache(UniTextFont.Core font)
        {
            FontCacheEntry entry;
            lock (fontCacheLock)
            {
                entry = Interlocked.Exchange(ref font.shaperCache, null);
                var fontHash = font.ExistingRuntimeFontId;
                if (fontHash != 0) fontOwners.Remove(fontHash);
            }
            entry?.Dispose();
        }

        #endregion

        #region Static API

        /// <summary>Returns the variable font axis infos for a font, or null if not variable.</summary>
        internal static HB.hb_ot_var_axis_info_t[] GetVariableAxisInfos(UniTextFont.Core font)
        {
            var cache = GetOrCreateCoreCache(font);
            if (cache == null) return null;
            var result = cache.GetAxisInfos();
            if (cache.IsValid) return result;
            return GetOrCreateCoreCache(font)?.GetAxisInfos();
        }

        /// <summary>Gets the glyph index for a codepoint in the specified font.</summary>
        /// <param name="font">The runtime font core.</param>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <returns>Glyph index, or 0 if not found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint GetGlyphIndex(UniTextFont.Core font, uint codepoint)
        {
            var backend = font?.FontBackend;
            if (backend != null)
                return backend.TryGetGlyph(codepoint, out var backendGlyph) ? backendGlyph : 0;

            var cache = GetOrCreateCoreCache(font);
            if (cache == null) return 0;
            if (cache.TryGetGlyph(codepoint, out var glyphIndex)) return glyphIndex;
            if (cache.IsValid) return 0;
            cache = GetOrCreateCoreCache(font);
            return cache != null && cache.TryGetGlyph(codepoint, out glyphIndex) ? glyphIndex : 0u;
        }

        /// <summary>Gets glyph index and advance width for a codepoint.</summary>
        /// <param name="font">The runtime font core.</param>
        /// <param name="codepoint">The Unicode codepoint.</param>
        /// <param name="fontSize">Font size for advance calculation.</param>
        /// <param name="glyphIndex">Output glyph index.</param>
        /// <param name="advance">Output horizontal advance in font units.</param>
        /// <returns>True if the glyph was found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetGlyphInfo(UniTextFont.Core font, uint codepoint, float fontSize,
            out uint glyphIndex, out float advance, float normalizationScale = 1f)
        {
            glyphIndex = 0;
            advance = 0;

            var backend = font?.FontBackend;
            if (backend != null)
            {
                if (!backend.TryGetGlyph(codepoint, out glyphIndex)) return false;
                var upem = backend.UnitsPerEm;
                var backendAdvanceUnits = font.ApplyAdvanceOverride(glyphIndex,
                    backend.GetGlyphAdvance(glyphIndex));
                advance = backendAdvanceUnits * fontSize * font.FontScale * normalizationScale / upem;
                return true;
            }

            var cache = GetOrCreateCoreCache(font);
            if (cache == null) return false;
            if (!cache.TryGetGlyph(codepoint, out glyphIndex))
            {
                if (cache.IsValid) return false;
                cache = GetOrCreateCoreCache(font);
                if (cache == null || !cache.TryGetGlyph(codepoint, out glyphIndex)) return false;
            }

            var advanceUnits = font.ApplyAdvanceOverride(glyphIndex, cache.GetGlyphAdvance(glyphIndex));
            if (!cache.IsValid)
            {
                cache = GetOrCreateCoreCache(font);
                if (cache == null) return false;
                advanceUnits = font.ApplyAdvanceOverride(glyphIndex, cache.GetGlyphAdvance(glyphIndex));
            }
            advance = advanceUnits * fontSize * font.FontScale * normalizationScale / cache.upem;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetGlyphAdvance(UniTextFont.Core font, uint glyphIndex)
        {
            var backend = font?.FontBackend;
            if (backend != null) return backend.GetGlyphAdvance(glyphIndex);

            var cache = GetOrCreateCoreCache(font);
            if (cache == null) return 0;
            var advance = cache.GetGlyphAdvance(glyphIndex);
            if (cache.IsValid) return advance;
            return GetOrCreateCoreCache(font)?.GetGlyphAdvance(glyphIndex) ?? 0;
        }

        /// <summary>Ink bounding box of one glyph in design units, cached per font. False for a glyph with no outline (space, mark-less blank) and for a font served by a backend that exposes no outlines.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryGetGlyphInk(UniTextFont.Core font, uint glyphIndex,
            out HB.hb_glyph_extents_t extents)
        {
            if (font?.FontBackend != null)
            {
                extents = default;
                return false;
            }

            var cache = GetOrCreateCoreCache(font);
            if (cache == null)
            {
                extents = default;
                return false;
            }
            var found = cache.TryGetGlyphInk(glyphIndex, out extents);
            if (cache.IsValid) return found;
            cache = GetOrCreateCoreCache(font);
            return cache != null && cache.TryGetGlyphInk(glyphIndex, out extents);
        }

        /// <summary>Reads units-per-em directly from font data without caching. Returns zero for missing input and propagates invalid-font or native ABI failures.</summary>
        public static int GetUpemFromFontData(byte[] fontData)
        {
            if (fontData == null || fontData.Length == 0)
            {
                Debug.LogWarning("[GetUpemFromFontData] fontData is null or empty");
                return 0;
            }

            var entry = new FontCacheEntry(new ArrayFontSource(fontData).Open());
            try
            {
                return entry.upem;
            }
            finally { entry.Dispose(); }
        }

        internal static int GetUpem(UniTextFont.Core font)
        {
            var backend = font?.FontBackend;
            if (backend != null) return backend.UnitsPerEm;
            var entry = GetOrCreateCoreCache(font);
            return entry?.upem ?? 0;
        }

        /// <summary>Clears the shaping cache for a specific runtime font id.</summary>
        /// <param name="fontId">Process-unique runtime font id, or <see cref="EmojiFont.FontId"/>.</param>
        public static void ClearCache(int fontId)
        {
            FontCacheEntry entry = null;
            lock (fontCacheLock)
            {
                var fontHash = FontCacheKey(fontId);
                if (fontOwners.TryGetValue(fontHash, out var weak)
                    && weak.TryGetTarget(out var font))
                {
                    entry = Interlocked.Exchange(ref font.shaperCache, null);
                }
                fontOwners.Remove(fontHash);
            }
            entry?.Dispose();
        }

        internal static void ClearCache(UniTextFont.Core font)
        {
            if (font == null) return;
            RetireCoreCache(font);
        }

        /// <summary>Clears all shaping caches for all fonts.</summary>
        public static void ClearAllCaches()
        {
            var entries = new List<FontCacheEntry>();
            lock (fontCacheLock)
            {
                foreach (var kvp in fontOwners)
                    if (kvp.Value.TryGetTarget(out var font))
                    {
                        var entry = Interlocked.Exchange(ref font.shaperCache, null);
                        if (entry != null) entries.Add(entry);
                    }
                fontOwners = new FastIntDictionary<WeakReference<UniTextFont.Core>>();
            }
            for (var i = 0; i < entries.Count; i++) entries[i].Dispose();
        }

        #endregion

        #region Shaping


        /// <summary>Maps the emoji run sentinel to its runtime owner's positive identity.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FontCacheKey(int fontId)
            => fontId == EmojiFont.FontId ? EmojiFont.ExistingEmojiFontId : fontId;

        internal static void RetireVariation(UniTextFont.Core font, int fontId)
        {
            if (font == null) return;
            Volatile.Read(ref font.shaperCache)?.RetireSubFont(fontId);
        }

        /// <summary>
        /// Computes per-font shaping parameters in a single font lookup: the
        /// <paramref name="spacingOffsetUnits"/> baseline (from <see cref="UniTextFont.SpacingOffset"/>)
        /// and per-glyph <paramref name="fakeBoldAdvancePx"/> bonus
        /// (from <see cref="UniTextFont.FakeBoldWeight"/>) alongside the returned design-unit-to-pixel
        /// scale (incorporates <see cref="UniTextFont.FontScale"/>). Callers cache all three per
        /// run-fontId change.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ComputeShapeParams(UniTextFontProvider fontProvider, int fontId,
            out int spacingOffsetUnits, out float fakeBoldAdvancePx)
        {
            spacingOffsetUnits = 0;
            fakeBoldAdvancePx = 0f;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (fontId == EmojiFont.FontId)
                return 1f;
#endif
            var font = fontProvider.GetFont(fontId);
            var upem = GetUpem(font);
            if (upem <= 0)
                throw new InvalidOperationException($"Cannot shape font {fontId}: its runtime core has no usable font face.");
            if (font != null)
            {
                spacingOffsetUnits = font.SpacingOffset;
                if (font.FakeBoldWeight > 0f)
                    fakeBoldAdvancePx = fontProvider.FontSize * FontStyleEncoding.EmboldenRatio * font.FakeBoldWeight;
            }
            return fontProvider.FontSize * (font?.FontScale ?? 1f) * fontProvider.GetNormalizationScale(font) / upem;
        }

        /// <summary>
        /// Shapes text directly into the target buffer. No intermediate copy.
        /// Returns the number of glyphs written.
        /// </summary>
        /// <param name="clusterOffset">Added to every cluster the shaping backend reports. Pass the slice start when
        /// <paramref name="context"/> is a window into a larger codepoint buffer, so clusters stay absolute.</param>
        internal unsafe int ShapeInto(
            ref PooledBuffer<ShapedGlyph> output,
            ReadOnlySpan<int> context,
            int itemOffset,
            int itemLength,
            UniTextFontProvider fontProvider,
            int fontId,
            UnicodeScript script,
            TextDirection direction,
            float scale,
            int spacingOffsetUnits,
            float fakeBoldAdvancePx,
            out float totalAdvanceOut,
            HB.hb_variation_t[] variations = null,
            HB.hb_feature_t[] features = null,
            int featureCount = -1,
            IntPtr language = default,
            int clusterOffset = 0)
        {
            totalAdvanceOut = 0;

            if (itemLength == 0)
                return 0;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (fontId == EmojiFont.FontId)
            {
                var result = WebGLEmojiShaper.Shape(context.Slice(itemOffset, itemLength), fontProvider.FontSize, 2048, clusterOffset + itemOffset);
                var glyphs = result.Glyphs;
                var emojiStart = output.count;
                output.EnsureCapacity(emojiStart + glyphs.Length);
                glyphs.CopyTo(output.data.AsSpan(emojiStart));
                output.count = emojiStart + glyphs.Length;
                totalAdvanceOut = result.TotalAdvance;
                return glyphs.Length;
            }
#endif

            var font = fontProvider.GetFont(fontId);
            var backend = font?.FontBackend;
            if (backend != null)
            {
                var raw = EnsureRawScratch(itemLength);
                int glyphCount;
                while ((glyphCount = backend.Shape(context, itemOffset, itemLength, raw)) < 0)
                {
                    var required = checked(-glyphCount);
                    if (required <= raw.Length)
                        throw new InvalidOperationException(
                            $"Font backend '{backend.Identity}' returned an invalid shaping capacity.");
                    raw = EnsureRawScratch(required);
                }
                if (glyphCount == 0) return 0;
                if (glyphCount > raw.Length)
                    throw new InvalidOperationException(
                        $"Font backend '{backend.Identity}' exceeded its shaping output capacity.");

                ApplyReadback(raw.AsSpan(0, glyphCount), ref output, context, scale,
                    spacingOffsetUnits, fakeBoldAdvancePx, font,
                    clusterOffset, out totalAdvanceOut);
                return glyphCount;
            }

            if (!TryAcquireCoreCache(font, out var fontEntry, out var fontLease))
                throw new InvalidOperationException($"Cannot shape font {fontId}: its runtime core has no font data.");

            try
            {
                IntPtr buffer = EnsureBuffer();

                var fCount = featureCount >= 0 ? featureCount : (features?.Length ?? 0);

                IntPtr shapingFont = fontEntry.hbFont;
                FontCacheEntry.SubFontLease subFontLease = default;
                if (variations != null && variations.Length > 0)
                {
                    subFontLease = fontEntry.AcquireSubFont(fontLease, fontId, variations);
                    shapingFont = subFontLease.Pointer;
                }

                int glyphCount;
                HB.hb_glyph_info_t* nativeInfos;
                HB.hb_glyph_position_t* nativePositions;
                try
                {
                    UniTextDebug.BeginSample("Shape.Native");
                    try
                    {
                        glyphCount = HB.ShapeRun(
                            shapingFont, buffer,
                            context, itemOffset, itemLength,
                            direction == TextDirection.RightToLeft ? HB.DIRECTION_RTL : HB.DIRECTION_LTR,
                            MapScript(script),
                            language,
                            HB.BUFFER_FLAG_REMOVE_DEFAULT_IGNORABLES,
                            features, fCount,
                            out nativeInfos, out nativePositions);
                    }
                    finally { UniTextDebug.EndSample(); }
                }
                finally { subFontLease.Dispose(); }

                if (glyphCount == 0)
                    return 0;

                var raw = EnsureRawScratch(glyphCount);
                for (int i = 0; i < glyphCount; i++)
                {
                    raw[i].glyphId = (int)nativeInfos[i].codepoint;
                    raw[i].cluster = (int)nativeInfos[i].cluster;
                    raw[i].xAdvance = nativePositions[i].x_advance;
                    raw[i].yAdvance = nativePositions[i].y_advance;
                    raw[i].xOffset = nativePositions[i].x_offset;
                    raw[i].yOffset = nativePositions[i].y_offset;
                    raw[i].flags = (int)(nativeInfos[i].mask & HB.GLYPH_FLAG_DEFINED);
                }

                ApplyReadback(raw.AsSpan(0, glyphCount), ref output, context, scale,
                    spacingOffsetUnits, fakeBoldAdvancePx, font,
                    clusterOffset, out totalAdvanceOut);
                return glyphCount;
            }
            finally { fontLease.Dispose(); }
        }

        /// <summary>
        /// Emits raw font-unit glyphs (from a fresh shape or the word cache) as positioned
        /// <see cref="ShapedGlyph"/>s appended to <paramref name="output"/>, applying scale, tracking/
        /// fake-bold advance, per-glyph advance overrides, the space-advance override, and the cluster
        /// offset. The single source of truth for readback, so cached and freshly-shaped output match.
        /// </summary>
        private static void ApplyReadback(
            ReadOnlySpan<RawShapedGlyph> raw, ref PooledBuffer<ShapedGlyph> output,
            ReadOnlySpan<int> context, float scale, int spacingOffsetUnits, float fakeBoldAdvancePx,
            UniTextFont.Core glyphOvFont, int clusterOffset, out float totalAdvanceOut)
        {
            var glyphCount = raw.Length;
            var writeStart = output.count;
            var required = writeStart + glyphCount;
            if (output.Capacity < required)
                output.EnsureCapacity(required);

            var data = output.data;
            float totalAdvance = 0;

            float advanceBonusPx = spacingOffsetUnits * scale + fakeBoldAdvancePx;
            var hasGlyphAdvanceOverrides = glyphOvFont != null && glyphOvFont.HasGlyphMetricOverrides;
            var spaceAdvancePx = glyphOvFont != null && glyphOvFont.SpaceAdvance >= 0
                ? glyphOvFont.SpaceAdvance * scale
                : -1f;

            for (int i = 0; i < glyphCount; i++)
            {
                float advanceX = raw[i].xAdvance * scale;
                if (advanceX != 0f && advanceBonusPx != 0f)
                    advanceX += advanceBonusPx;
                if (hasGlyphAdvanceOverrides)
                    advanceX *= glyphOvFont.GetGlyphAdvanceScale((uint)raw[i].glyphId);
                if (spaceAdvancePx >= 0f)
                {
                    var srcCluster = raw[i].cluster;
                    if ((uint)srcCluster < (uint)context.Length && context[srcCluster] == UnicodeData.Space)
                        advanceX = spaceAdvancePx;
                }
                data[writeStart + i] = new ShapedGlyph
                {
                    glyphId = raw[i].glyphId,
                    cluster = raw[i].cluster + clusterOffset,
                    advanceX = advanceX,
                    advanceY = raw[i].yAdvance * scale,
                    offsetX = raw[i].xOffset * scale,
                    offsetY = raw[i].yOffset * scale
                };
                totalAdvance += advanceX;
            }

            output.count = required;
            totalAdvanceOut = totalAdvance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static RawShapedGlyph[] EnsureRawScratch(int count)
        {
            if (rawScratch == null || rawScratch.Length < count)
                rawScratch = new RawShapedGlyph[Math.Max(count, 64)];
            return rawScratch;
        }

        private struct WordTok
        {
            public int start;
            public int len;
            public long key;
            public RawShapedGlyph[] glyphs;
            public int count;
            public bool hit;
            public bool cacheable;
        }

        /// <summary>
        /// Shapes one run through the word cache (Blink CachingWordShaper model). Splits on U+0020; if every
        /// word is cached the run is spliced with no shaping call (RTL words emitted in visual order). Any miss
        /// shapes the WHOLE run once in context — byte-identical to <see cref="ShapeInto"/>, no cold regression —
        /// then caches each word HarfBuzz marks safe-to-break on both boundaries — the exact per-font condition
        /// under which an isolated splice equals in-context shaping.
        /// Word segmentation on partial edits is the incremental-reshape layer's job, not this cache's.
        /// </summary>
        /// <param name="featureIds">Per-codepoint feature-set ids of the whole text, or <see langword="null"/>;
        /// a word spanning more than one id is shaped but never cached.</param>
        /// <param name="features">The run's expanded feature settings, applied when a miss reshapes the run.</param>
        internal int ShapeWordCachedRun(
            ref PooledBuffer<ShapedGlyph> output,
            ReadOnlySpan<int> cp,
            int runStart, int runLength,
            int ctxStart, int ctxEnd,
            UniTextFontProvider fontProvider, int fontId,
            UnicodeScript script, TextDirection direction,
            float scale, int spacingOffsetUnits, float fakeBoldAdvancePx,
            byte language, HB.hb_variation_t[] variations,
            byte[] featureIds, HB.hb_feature_t[] features, int featureCount,
            out float totalAdvanceOut)
        {
            totalAdvanceOut = 0f;
            if (runLength == 0)
                return 0;

            var glyphOvFont = fontProvider.GetFont(fontId);
            int varHash = variations != null && variations.Length > 0 ? HashVariations(variations) : 0;
            byte scriptByte = (byte)script;
            byte dirByte = (byte)direction;
            int runEnd = runStart + runLength;
            var languageHandle = LanguageRegistry.GetHandle(language);

            var toks = EnsureTokScratch(runLength);
            int nTok = 0;
            int t = runStart;
            while (t < runEnd)
            {
                int ts = t;
                if (cp[t] == UnicodeData.Space) t++;
                else while (t < runEnd && cp[t] != UnicodeData.Space) t++;

                var tok = new WordTok { start = ts, len = t - ts };
                var featureSet = UniformFeatureSet(featureIds, ts, t);
                tok.cacheable = featureSet >= 0 && WordShapeCache.IsCacheableLength(tok.len);
                if (tok.cacheable)
                {
                    tok.key = WordShapeCache.ComputeKey(cp.Slice(ts, tok.len), fontId, varHash,
                        scriptByte, dirByte, language, (byte)featureSet);
                    tok.hit = WordShapeCache.TryGet(tok.key, out tok.glyphs, out tok.count);
                }
                toks[nTok++] = tok;
            }

            bool allHit = true;
            for (int i = 0; i < nTok; i++)
                if (!toks[i].hit) { allHit = false; break; }

            if (allHit)
            {
                float total = 0f;
                int spliced = 0;
                for (int i = 0; i < nTok; i++)
                {
                    ref var tok = ref toks[direction == TextDirection.RightToLeft ? nTok - 1 - i : i];
                    ApplyReadback(tok.glyphs.AsSpan(0, tok.count), ref output, cp.Slice(tok.start, tok.len),
                        scale, spacingOffsetUnits, fakeBoldAdvancePx, glyphOvFont, tok.start, out var adv);
                    total += adv;
                    spliced += tok.count;
                }
                totalAdvanceOut = total;
                return spliced;
            }

            int gc = ShapeInto(ref output, cp.Slice(ctxStart, ctxEnd - ctxStart),
                runStart - ctxStart, runLength, fontProvider, fontId, script, direction,
                scale, spacingOffsetUnits, fakeBoldAdvancePx, out totalAdvanceOut,
                variations, features, featureCount, languageHandle, ctxStart);

            if (gc > 0)
                CacheSafeWords(toks, nTok, gc, ctxStart, runStart, runEnd);

            return gc;
        }

        /// <summary>
        /// Returns the feature-set id shared by every codepoint of a token, or -1 when the token spans
        /// more than one — the case an isolated splice could not reproduce.
        /// </summary>
        private static int UniformFeatureSet(byte[] featureIds, int start, int end)
        {
            if (featureIds == null) return FontFeatureRegistry.Unset;

            var first = (uint)start < (uint)featureIds.Length ? featureIds[start] : FontFeatureRegistry.Unset;
            for (var i = start + 1; i < end; i++)
            {
                var id = (uint)i < (uint)featureIds.Length ? featureIds[i] : FontFeatureRegistry.Unset;
                if (id != first) return -1;
            }
            return first;
        }

        /// <summary>
        /// Stores each fresh-shaped word whose both boundaries HarfBuzz marked safe-to-break — the guarantee
        /// that the word shaped in isolation equals it in context, so a later cache splice is byte-identical.
        /// Glyphs are gathered in emission order (visual for RTL) and rebased to the word start.
        /// </summary>
        private static void CacheSafeWords(WordTok[] toks, int nTok, int gc, int ctxStart, int runStart, int runEnd)
        {
            var raw = rawScratch;
            for (int k = 0; k < nTok; k++)
            {
                ref var tok = ref toks[k];
                if (!tok.cacheable || tok.hit) continue;

                int lo = tok.start - ctxStart;
                int hi = lo + tok.len;
                bool startSafe = tok.start == runStart || !IsUnsafeBreakAt(raw, gc, lo);
                bool endSafe = tok.start + tok.len == runEnd || !IsUnsafeBreakAt(raw, gc, hi);
                if (!startSafe || !endSafe) continue;

                int count = 0;
                for (int g = 0; g < gc; g++)
                    if (raw[g].cluster >= lo && raw[g].cluster < hi) count++;

                var arr = new RawShapedGlyph[count];
                int w = 0;
                for (int g = 0; g < gc; g++)
                {
                    if (raw[g].cluster < lo || raw[g].cluster >= hi) continue;
                    arr[w] = raw[g];
                    arr[w].cluster -= lo;
                    w++;
                }
                WordShapeCache.Store(tok.key, arr, count);
            }
        }

        private static bool IsUnsafeBreakAt(RawShapedGlyph[] raw, int gc, int clusterPos)
        {
            for (int g = 0; g < gc; g++)
                if (raw[g].cluster == clusterPos && (raw[g].flags & HB.GLYPH_FLAG_UNSAFE_TO_BREAK) != 0)
                    return true;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static WordTok[] EnsureTokScratch(int count)
        {
            if (tokScratch == null || tokScratch.Length < count)
                tokScratch = new WordTok[Math.Max(count, 32)];
            return tokScratch;
        }

        /// <summary>Ensures a reusable buffer exists. Does NOT clear — ShapeRun handles clearing internally.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IntPtr EnsureBuffer()
        {
            if (reusableBuffer != IntPtr.Zero)
                return reusableBuffer;

            reusableBuffer = HB.CreateBuffer();
            if (reusableBuffer == IntPtr.Zero)
                throw new InvalidOperationException("[HarfBuzz] Failed to create a shaping buffer.");
            lock (bufferLock) buffers.Add(reusableBuffer);
            return reusableBuffer;
        }

        /// <summary>
        /// Stable 31-bit hash of a variation set, used as <see cref="FontCacheEntry"/> sub-font cache key.
        /// Top bit cleared so the value can never equal <c>FastIntDictionary</c>'s reserved <c>-1</c> sentinel.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HashVariations(HB.hb_variation_t[] variations)
        {
            int h = 17;
            for (int i = 0; i < variations.Length; i++)
            {
                ref var v = ref variations[i];
                h = h * 31 + (int)v.tag;
                h = h * 31 + BitConverter.SingleToInt32Bits(v.value);
            }
            return h & 0x7FFFFFFF;
        }

        internal static readonly uint SmcpTag = HB.MakeTag('s', 'm', 'c', 'p');
        internal static readonly uint SupsTag = HB.MakeTag('s', 'u', 'p', 's');
        internal static readonly uint SubsTag = HB.MakeTag('s', 'u', 'b', 's');

        internal static readonly HB.hb_feature_t[] SmcpFeatures =
        {
            new HB.hb_feature_t { tag = SmcpTag, value = 1, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END }
        };

        internal static readonly HB.hb_feature_t[] SupsFeatures =
        {
            new HB.hb_feature_t { tag = SupsTag, value = 1, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END }
        };

        internal static readonly HB.hb_feature_t[] SubsFeatures =
        {
            new HB.hb_feature_t { tag = SubsTag, value = 1, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END }
        };

        internal bool HasSmcpFeature(UniTextFont.Core font) => HasFeature(font, SmcpBit, SmcpFeatures, 'a');
        internal bool HasSupsFeature(UniTextFont.Core font) => HasFeature(font, SupsBit, SupsFeatures, '2');
        internal bool HasSubsFeature(UniTextFont.Core font) => HasFeature(font, SubsBit, SubsFeatures, '2');

        /// <summary>
        /// Checks if a font has an OpenType 'sups' alternate for a specific codepoint.
        /// Results are cached per (fontId, codepoint). Call HasSupsFeature first as a fast-path.
        /// </summary>
        internal bool HasSupsForCodepoint(UniTextFont.Core font, int codepoint)
            => HasFeatureForCodepoint(font, codepoint, SupsFeatures, true);

        /// <summary>
        /// Checks if a font has an OpenType 'subs' alternate for a specific codepoint.
        /// Results are cached per (fontId, codepoint). Call HasSubsFeature first as a fast-path.
        /// </summary>
        internal bool HasSubsForCodepoint(UniTextFont.Core font, int codepoint)
            => HasFeatureForCodepoint(font, codepoint, SubsFeatures, false);

        /// <summary>
        /// Checks if a font supports an OpenType feature by test-shaping a character with and without it.
        /// Result is cached per fontId using bit flags.
        /// </summary>
        private bool HasFeature(UniTextFont.Core font, int bitOffset, HB.hb_feature_t[] testFeatures, int testChar)
        {
            if (font == null) return false;

            int checkedBit = 1 << bitOffset;
            int supportedBit = 1 << (bitOffset + 1);

            var fontEntry = GetOrCreateCoreCache(font);
            if (fontEntry == null) return false;
            if (fontEntry.TryGetFeature(checkedBit, supportedBit, out var cached)) return cached;
            if (!fontEntry.TryAcquire(out var lease))
            {
                fontEntry = GetOrCreateCoreCache(font);
                if (fontEntry == null || !fontEntry.TryAcquire(out lease)) return false;
            }

            bool supported;
            try { supported = TestFeatureSubstitution(fontEntry.hbFont, testChar, testFeatures); }
            finally { lease.Dispose(); }
            fontEntry.SetFeature(checkedBit, supportedBit, supported);
            return supported;
        }

        private bool HasFeatureForCodepoint(
            UniTextFont.Core font, int codepoint,
            HB.hb_feature_t[] testFeatures,
            bool superscript)
        {
            if (font == null) return false;
            var fontEntry = GetOrCreateCoreCache(font);
            if (fontEntry == null) return false;
            if (fontEntry.TryGetFeatureForCodepoint(codepoint, superscript, out var cached)) return cached;
            if (!fontEntry.TryAcquire(out var lease))
            {
                fontEntry = GetOrCreateCoreCache(font);
                if (fontEntry == null || !fontEntry.TryAcquire(out lease)) return false;
            }

            bool supported;
            try { supported = TestFeatureSubstitution(fontEntry.hbFont, codepoint, testFeatures); }
            finally { lease.Dispose(); }
            fontEntry.SetFeatureForCodepoint(codepoint, superscript, supported);
            return supported;
        }

        /// <summary>
        /// Test-shapes a single codepoint with and without a feature.
        /// Returns true if the feature caused a glyph substitution.
        /// </summary>
        private static bool TestFeatureSubstitution(IntPtr hbFont, int codepoint, HB.hb_feature_t[] testFeatures)
        {
            Span<int> testCp = stackalloc int[] { codepoint };

            var buf1 = IntPtr.Zero;
            var buf2 = IntPtr.Zero;
            try
            {
                buf1 = HB.CreateBuffer();
                buf2 = HB.CreateBuffer();
                if (buf1 == IntPtr.Zero || buf2 == IntPtr.Zero)
                    throw new InvalidOperationException("[HarfBuzz] Failed to create feature-probe buffers.");

                HB.SetDirection(buf1, HB.DIRECTION_LTR);
                HB.SetScript(buf1, HB.Script.Latin);
                HB.AddCodepoints(buf1, testCp, 0, 1);
                HB.Shape(hbFont, buf1);
                var infos1 = HB.GetGlyphInfos(buf1);
                uint glyph1 = infos1.Length > 0 ? infos1[0].glyphId : 0;

                HB.SetDirection(buf2, HB.DIRECTION_LTR);
                HB.SetScript(buf2, HB.Script.Latin);
                HB.AddCodepoints(buf2, testCp, 0, 1);
                HB.Shape(hbFont, buf2, testFeatures);
                var infos2 = HB.GetGlyphInfos(buf2);
                uint glyph2 = infos2.Length > 0 ? infos2[0].glyphId : 0;

                return glyph1 != glyph2 && glyph2 != 0;
            }
            finally
            {
                if (buf1 != IntPtr.Zero) HB.DestroyBuffer(buf1);
                if (buf2 != IntPtr.Zero) HB.DestroyBuffer(buf2);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint MapScript(UnicodeScript script)
        {
            return script switch
            {
                UnicodeScript.Arabic => HB.Script.Arabic,
                UnicodeScript.Armenian => HB.Script.Armenian,
                UnicodeScript.Bengali => HB.Script.Bengali,
                UnicodeScript.Cyrillic => HB.Script.Cyrillic,
                UnicodeScript.Devanagari => HB.Script.Devanagari,
                UnicodeScript.Georgian => HB.Script.Georgian,
                UnicodeScript.Greek => HB.Script.Greek,
                UnicodeScript.Gujarati => HB.Script.Gujarati,
                UnicodeScript.Gurmukhi => HB.Script.Gurmukhi,
                UnicodeScript.Han => HB.Script.Han,
                UnicodeScript.Hangul => HB.Script.Hangul,
                UnicodeScript.Hebrew => HB.Script.Hebrew,
                UnicodeScript.Hiragana => HB.Script.Hiragana,
                UnicodeScript.Kannada => HB.Script.Kannada,
                UnicodeScript.Katakana => HB.Script.Katakana,
                UnicodeScript.Khmer => HB.Script.Khmer,
                UnicodeScript.Lao => HB.Script.Lao,
                UnicodeScript.Latin => HB.Script.Latin,
                UnicodeScript.Malayalam => HB.Script.Malayalam,
                UnicodeScript.Myanmar => HB.Script.Myanmar,
                UnicodeScript.Oriya => HB.Script.Oriya,
                UnicodeScript.Sinhala => HB.Script.Sinhala,
                UnicodeScript.Tamil => HB.Script.Tamil,
                UnicodeScript.Telugu => HB.Script.Telugu,
                UnicodeScript.Thai => HB.Script.Thai,
                UnicodeScript.Tibetan => HB.Script.Tibetan,
                _ => HB.Script.Common
            };
        }

        #endregion
    }
}
