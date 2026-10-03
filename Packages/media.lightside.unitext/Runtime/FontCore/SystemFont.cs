using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;

namespace LightSide
{
    using Core = UniTextFont.Core;

    /// <summary>Snapshot of SystemFont cache ownership. Counts include active and warm inactive entries; mapped file length is not reported as resident memory.</summary>
    public readonly struct SystemFontMemoryStats
    {
        /// <summary>Number of resolved source faces currently owned by SystemFont.</summary>
        public int SourceCount { get; }
        /// <summary>Number of source faces leased by live text providers or an in-flight resolution pass.</summary>
        public int ActiveSourceCount { get; }
        /// <summary>Number of cached sequence/style resolution results.</summary>
        public int RequestCount { get; }
        /// <summary>Number of shared file-backed or memory-backed source entries.</summary>
        public int ByteEntryCount { get; }
        /// <summary>Total bytes owned by memory-backed source entries. File mappings report zero because their virtual length is not resident memory.</summary>
        public long RetainedByteCount { get; }

        internal SystemFontMemoryStats(int sourceCount, int activeSourceCount, int requestCount,
            int byteEntryCount, long retainedByteCount)
        {
            SourceCount = sourceCount;
            ActiveSourceCount = activeSourceCount;
            RequestCount = requestCount;
            ByteEntryCount = byteEntryCount;
            RetainedByteCount = retainedByteCount;
        }
    }

    /// <summary>
    /// Resolves complete Unicode sequences through the platform font cascade and caches the exact
    /// source face plus variable-axis coordinates selected by the operating system. Returned runtimes
    /// are cache-owned and must not be disposed by callers.
    /// </summary>
    public static class SystemFont
    {
        private static readonly ReaderWriterLockSlim rwLock = new(LockRecursionPolicy.NoRecursion);
        private static readonly ReaderWriterLockSlim platformLifecycleLock = new(LockRecursionPolicy.NoRecursion);
        private static readonly Dictionary<SystemFontRequestKey, Core> requestCache = new();
        private static readonly Dictionary<SystemFontSourceKey, SourceEntry> sourceCache = new();
        private static readonly Dictionary<Core, SourceEntry> sourceOwners = new();
        private const int RequestCacheCapacity = 131072;
        private const int ThreadCacheCapacity = 4096;
        private static int inactiveSourceCapacity = 32;
        private const int LockStripeCount = 256;
        private static volatile int globalVersion;
        private static long useClock;
        private static readonly object[] requestLocks = CreateLockStripes();
        private static readonly object[] sourceLocks = CreateLockStripes();

        [ThreadStatic] private static ThreadCacheState tlsState;
        [ThreadStatic] private static ResolutionBatch activeBatch;
        [ThreadStatic] private static ResolutionBatch threadBatch;
        private static readonly object threadStatesLock = new();
        private static readonly List<WeakReference<ThreadCacheState>> threadStates = new();

        private sealed class SourceEntry
        {
            internal SystemFontSourceKey key;
            internal Core font;
            internal string descriptor;
            internal int leases;
            internal long lastUse;
        }

        private readonly struct ThreadCacheValue
        {
            private readonly WeakReference<Core> font;
            private readonly bool nullValue;

            internal ThreadCacheValue(Core value)
            {
                nullValue = value == null;
                font = value != null ? new WeakReference<Core>(value) : null;
            }

            internal bool TryGet(out Core value)
            {
                if (nullValue)
                {
                    value = null;
                    return true;
                }

                value = null;
                return font != null && font.TryGetTarget(out value);
            }
        }

        private sealed class ThreadCacheState
        {
            internal Dictionary<SystemFontRequestKey, ThreadCacheValue> cache;
            internal int version;
        }

        /// <summary>Maximum number of resolved source faces retained after their last text provider releases them. Active faces are never evicted. Set to zero to keep no inactive source faces.</summary>
        public static int InactiveSourceCapacity
        {
            get => Volatile.Read(ref inactiveSourceCapacity);
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                Volatile.Write(ref inactiveSourceCapacity, value);
                Trim();
            }
        }

        /// <summary>Maximum owned bytes retained by inactive memory-backed source faces and warm entries. Mapped files are released with their last native consumer and do not consume this budget.</summary>
        public static long InactiveByteBudget
        {
            get => SystemFontByteCache.InactiveByteBudget;
            set
            {
                SystemFontByteCache.InactiveByteBudget = value;
                Trim();
            }
        }

        /// <summary>Returns a consistent snapshot of SystemFont ownership and memory-backed retained bytes.</summary>
        public static SystemFontMemoryStats MemoryStats
        {
            get
            {
                int sources;
                int activeSources = 0;
                int requests;
                rwLock.EnterReadLock();
                try
                {
                    sources = sourceCache.Count;
                    requests = requestCache.Count;
                    foreach (var entry in sourceCache.Values)
                        if (Volatile.Read(ref entry.leases) > 0) activeSources++;
                }
                finally { rwLock.ExitReadLock(); }
                var bytes = SystemFontByteCache.GetStats();
                return new SystemFontMemoryStats(sources, activeSources, requests,
                    bytes.entryCount, bytes.byteCount);
            }
        }

        static SystemFont()
        {
            Application.lowMemory += TrimUnused;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += ResetForReload;
#endif
        }

        internal enum ResolutionMode : byte
        {
            Standard,
            CoveringSequence,
            Contextual,
        }

        internal readonly struct PendingRequest
        {
            internal readonly SystemFontRequestKey key;
            internal readonly int sequenceStart;

            internal PendingRequest(SystemFontRequestKey key, int sequenceStart)
            {
                this.key = key;
                this.sequenceStart = sequenceStart;
            }
        }

        internal sealed class ResolutionBatch : IDisposable
        {
            private ResolutionBatch previous;
            private readonly List<PendingRequest> pending = new(64);
            private readonly HashSet<Core> protectedSources = new();
            private HashSet<SystemFontRequestKey> independentPending;
            private Dictionary<BatchedRequestKey, Core> batchedResults;
            private int batchedVersion;
            private bool disposed;
            private bool needsTrim;

            internal void Begin()
            {
                disposed = false;
                needsTrim = false;
                independentPending?.Clear();
                batchedResults?.Clear();
                batchedVersion = globalVersion;
                previous = activeBatch;
                activeBatch = this;
            }

            internal void Add(in PendingRequest request)
            {
                if (request.key.mode != ResolutionMode.Contextual
                    && !(independentPending ??= new HashSet<SystemFontRequestKey>()).Add(request.key))
                    return;
                pending.Add(request);
            }

            internal void MarkCacheDirty() => needsTrim = true;

            internal bool TryGetBatched(SystemFontRequestKey key, int sequenceStart, out Core source)
            {
                source = null;
                if (batchedVersion == globalVersion)
                    return batchedResults != null && batchedResults.TryGetValue(
                        new BatchedRequestKey(key, sequenceStart), out source);
                batchedResults?.Clear();
                batchedVersion = globalVersion;
                return false;
            }

            internal void StoreBatched(SystemFontRequestKey key, int sequenceStart,
                Core source, int version)
            {
                if (version != globalVersion) return;
                if (batchedVersion != version)
                {
                    batchedResults?.Clear();
                    batchedVersion = version;
                }
                (batchedResults ??= new Dictionary<BatchedRequestKey, Core>())[
                    new BatchedRequestKey(key, sequenceStart)] = source;
            }

            internal bool Protect(Core source)
            {
                var owner = source?.SystemFontSource;
                if (owner == null || protectedSources.Contains(owner)) return true;
                rwLock.EnterReadLock();
                try
                {
                    if (!sourceOwners.TryGetValue(owner, out var entry)) return false;
                    if (!protectedSources.Add(owner)) return true;
                    Interlocked.Increment(ref entry.leases);
                    Interlocked.Exchange(ref entry.lastUse, Interlocked.Increment(ref useClock));
                    return true;
                }
                finally { rwLock.ExitReadLock(); }
            }

            internal void ProtectLocked(Core source)
            {
                var owner = source?.SystemFontSource;
                if (owner == null || protectedSources.Contains(owner)
                                  || !sourceOwners.TryGetValue(owner, out var entry)) return;
                protectedSources.Add(owner);
                Interlocked.Increment(ref entry.leases);
                Interlocked.Exchange(ref entry.lastUse, Interlocked.Increment(ref useClock));
            }

            internal bool ResolvePending()
            {
                if (pending.Count == 0) return false;
                ResolveBatch(pending);
                pending.Clear();
                independentPending?.Clear();
                return true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                activeBatch = previous;
                pending.Clear();
                independentPending?.Clear();
                if (Release(protectedSources) || needsTrim) Trim();
                protectedSources.Clear();
                batchedResults?.Clear();
            }
        }

        internal readonly struct CodepointSequenceKey : IEquatable<CodepointSequenceKey>
        {
            private readonly int length;
            private readonly int a;
            private readonly int b;
            private readonly int c;
            private readonly int d;
            private readonly string overflow;

            internal int Length => length;

            internal CodepointSequenceKey(ReadOnlySpan<int> sequence)
            {
                length = sequence.Length;
                a = sequence.Length > 0 ? sequence[0] : 0;
                b = sequence.Length > 1 ? sequence[1] : 0;
                c = sequence.Length > 2 ? sequence[2] : 0;
                d = sequence.Length > 3 ? sequence[3] : 0;
                overflow = sequence.Length > 4 ? EncodeSequence(sequence) : null;
            }

            public bool Equals(CodepointSequenceKey other)
                => length == other.length && a == other.a && b == other.b && c == other.c && d == other.d
                   && string.Equals(overflow, other.overflow, StringComparison.Ordinal);

            public override bool Equals(object obj) => obj is CodepointSequenceKey other && Equals(other);

            public override int GetHashCode()
                => overflow != null
                    ? HashCode.Combine(length, StringComparer.Ordinal.GetHashCode(overflow))
                    : HashCode.Combine(length, a, b, c, d);

            internal string GetText()
            {
                if (overflow != null) return overflow;
                if (length == 0) return string.Empty;
                if (length == 1) return char.ConvertFromUtf32(a);
                var builder = new StringBuilder(length * 2);
                AppendCodepoint(builder, a);
                AppendCodepoint(builder, b);
                if (length > 2) AppendCodepoint(builder, c);
                if (length > 3) AppendCodepoint(builder, d);
                return builder.ToString();
            }

            internal int Utf16Length
            {
                get
                {
                    if (overflow != null) return overflow.Length;
                    var result = 0;
                    if (length > 0) result += a > 0xFFFF ? 2 : 1;
                    if (length > 1) result += b > 0xFFFF ? 2 : 1;
                    if (length > 2) result += c > 0xFFFF ? 2 : 1;
                    if (length > 3) result += d > 0xFFFF ? 2 : 1;
                    return result;
                }
            }

            internal void AppendTo(StringBuilder builder)
            {
                if (overflow != null)
                {
                    builder.Append(overflow);
                    return;
                }
                if (length > 0) AppendCodepoint(builder, a);
                if (length > 1) AppendCodepoint(builder, b);
                if (length > 2) AppendCodepoint(builder, c);
                if (length > 3) AppendCodepoint(builder, d);
            }

        }

        internal readonly struct SystemFontRequestKey : IEquatable<SystemFontRequestKey>
        {
            internal readonly CodepointSequenceKey sequence;
            internal readonly string language;
            internal readonly string family;
            internal readonly int weight;
            internal readonly bool italic;
            internal readonly bool requireFamily;
            internal readonly ResolutionMode mode;

            internal SystemFontRequestKey(in CodepointSequenceKey sequence,
                string language, string family,
                int weight, bool italic, bool requireFamily, ResolutionMode mode)
            {
                this.sequence = sequence;
                this.language = language ?? string.Empty;
                this.family = family ?? string.Empty;
                this.weight = weight;
                this.italic = italic;
                this.requireFamily = requireFamily;
                this.mode = mode;
            }

            public bool Equals(SystemFontRequestKey other)
                => weight == other.weight && italic == other.italic
                   && requireFamily == other.requireFamily && mode == other.mode
                   && sequence.Equals(other.sequence)
                   && string.Equals(language, other.language, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(family, other.family, StringComparison.OrdinalIgnoreCase);

            public override bool Equals(object obj) => obj is SystemFontRequestKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(sequence.GetHashCode(),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(language),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(family),
                    weight, italic, requireFamily, (byte)mode);
        }

        private readonly struct BatchedRequestKey : IEquatable<BatchedRequestKey>
        {
            private readonly SystemFontRequestKey request;
            private readonly int sequenceStart;

            internal BatchedRequestKey(SystemFontRequestKey request, int sequenceStart)
            {
                this.request = request;
                this.sequenceStart = sequenceStart;
            }

            public bool Equals(BatchedRequestKey other)
                => sequenceStart == other.sequenceStart && request.Equals(other.request);

            public override bool Equals(object obj)
                => obj is BatchedRequestKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(request, sequenceStart);
        }

        private readonly struct SystemFontSourceKey : IEquatable<SystemFontSourceKey>
        {
            private readonly string descriptor;
            private readonly int faceIndex;
            private readonly UniTextFont.AxisDefault[] axes;
            private readonly int axesHash;
            private readonly int scaleBits;

            internal SystemFontSourceKey(string descriptor, int faceIndex,
                UniTextFont.AxisDefault[] axes, float scale)
            {
                this.descriptor = descriptor ?? string.Empty;
                this.faceIndex = faceIndex;
                this.axes = axes;
                var hash = 17;
                if (axes != null)
                    for (var i = 0; i < axes.Length; i++)
                        hash = unchecked(hash * 31 + HashCode.Combine(axes[i].tag,
                            BitConverter.SingleToInt32Bits(axes[i].value)));
                axesHash = hash;
                scaleBits = BitConverter.SingleToInt32Bits(scale);
            }

            public bool Equals(SystemFontSourceKey other)
                => faceIndex == other.faceIndex && scaleBits == other.scaleBits
                   && DescriptorComparer.Equals(descriptor, other.descriptor)
                   && AxesEqual(axes, other.axes);

            public override bool Equals(object obj) => obj is SystemFontSourceKey other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(DescriptorComparer.GetHashCode(descriptor), faceIndex,
                    axesHash, scaleBits);

            private static bool AxesEqual(UniTextFont.AxisDefault[] left,
                UniTextFont.AxisDefault[] right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left == null || right == null || left.Length != right.Length) return false;
                for (var i = 0; i < left.Length; i++)
                    if (left[i].tag != right[i].tag
                        || BitConverter.SingleToInt32Bits(left[i].value)
                        != BitConverter.SingleToInt32Bits(right[i].value))
                        return false;
                return true;
            }
        }

#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
        private static StringComparer DescriptorComparer => StringComparer.OrdinalIgnoreCase;
#else
        private static StringComparer DescriptorComparer => StringComparer.Ordinal;
#endif

        /// <summary>Raised whenever implicit system-font availability or resolved platform font sources change.</summary>
        public static event Action Changed;

        /// <summary>Raised when <see cref="Disabled"/> changes.</summary>
        public static event Action DisableChanged;
        private static volatile bool disabled;

        /// <summary>Turns off the automatically appended OS fallback and the OS-default primary used when no font is assigned. Explicitly assigned <see cref="UniTextSystemFont"/> assets remain active. Changing the value invalidates resolution caches and raises <see cref="DisableChanged"/>.</summary>
        public static bool Disabled
        {
            get => disabled;
            set
            {
                if (disabled == value) return;
                disabled = value;
                rwLock.EnterWriteLock();
                try { globalVersion++; }
                finally { rwLock.ExitWriteLock(); }
                ClearThreadCaches();
                SharedFontCache.InvalidateAll();
                Changed?.Invoke();
                DisableChanged?.Invoke();
            }
        }

        private static Core defaultFont;
        private static volatile bool defaultFontResolved;
        private static readonly object defaultLock = new();

        /// <summary>The configured project system font, or the platform default sans-serif runtime used as the final family and as the primary when no font is assigned. Null when implicit fallback is disabled or unavailable.</summary>
        public static Core Default
        {
            get
            {
                if (Disabled) return null;

                var configured = UniTextSettings.SystemFont;
                if (configured != null)
                {
                    var runtime = configured.Runtime;
                    if (runtime != null) return runtime;
                }

                if (!defaultFontResolved)
                {
                    lock (defaultLock)
                        if (!defaultFontResolved)
                        {
                            defaultFont = ResolveDefaultFont();
                            defaultFontResolved = true;
                        }
                }
                return defaultFont;
            }
        }

        private static Core ResolveDefaultFont()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null;
#else
            var family = SystemFontCatalog.FamilyOf(SystemFontCatalog.ResolveCommon(
                SystemFontCatalog.CommonSansSerif, SystemFontCatalog.CurrentPlatform()));
            if (!string.IsNullOrEmpty(family))
            {
                Span<int> defaultCluster = stackalloc int[1];
                defaultCluster[0] = 'A';
                var source = TryResolveSequence(defaultCluster, string.Empty, 0, false, null, family);
                if (source != null) return source;
            }
            return TryResolve('A');
#endif
        }

        /// <summary>Returns the platform cascade face covering a codepoint, optionally carrying another system font's render settings.</summary>
        public static Core TryResolve(uint codepoint, Core stylePrimary = null)
        {
            if (!IsUnicodeScalar(codepoint)) return null;
            Span<int> cluster = stackalloc int[1];
            cluster[0] = (int)codepoint;
            return TryResolveSequence(cluster, string.Empty, 0, false, stylePrimary, null);
        }

        /// <summary>Returns the closest platform cascade face for the requested CSS weight and slant. The returned face may be regular when the family has no real styled cut.</summary>
        public static Core TryResolveStyled(uint codepoint, int weight, bool italic, Core stylePrimary)
        {
            if (weight <= 0 || !IsUnicodeScalar(codepoint)) return null;
            Span<int> cluster = stackalloc int[1];
            cluster[0] = (int)codepoint;
            return TryResolveSequence(cluster, string.Empty, weight, italic, stylePrimary, null);
        }

        /// <summary>Returns a covering cut of the named installed family, or null when the platform substitutes another family or no cut covers the codepoint.</summary>
        public static Core TryResolveStyledFamily(string family, uint codepoint, int weight, bool italic,
            Core stylePrimary)
        {
            if (weight <= 0 || string.IsNullOrEmpty(family) || !IsUnicodeScalar(codepoint)) return null;
            Span<int> cluster = stackalloc int[1];
            cluster[0] = (int)codepoint;
            return Resolve(cluster, string.Empty, family, weight, italic, true, stylePrimary,
                ResolutionMode.Standard, -1, out _);
        }

        internal static Core TryResolveSequence(ReadOnlySpan<int> sequence, string language,
            int weight, bool italic, Core stylePrimary, string baseFamily)
        {
            if (sequence.IsEmpty) return null;
            return Resolve(sequence, language, baseFamily, weight, italic, false, stylePrimary,
                ResolutionMode.Standard, -1, out _);
        }

        internal static Core TryResolveSequence(ReadOnlySpan<int> sequence, string language,
            int weight, bool italic, Core stylePrimary, string baseFamily,
            int sequenceStart, bool contextual, out bool deferred)
        {
            if (sequence.IsEmpty)
            {
                deferred = false;
                return null;
            }
            return Resolve(sequence, language, baseFamily, weight, italic, false, stylePrimary,
                contextual ? ResolutionMode.Contextual : ResolutionMode.Standard,
                sequenceStart, out deferred);
        }

        internal static Core TryResolveCoveringSequence(ReadOnlySpan<int> sequence, string language,
            int weight, bool italic, Core stylePrimary, string baseFamily,
            int sequenceStart, out bool deferred)
        {
            if (sequence.IsEmpty)
            {
                deferred = false;
                return null;
            }
            return Resolve(sequence, language, baseFamily, weight, italic, false, stylePrimary,
                ResolutionMode.CoveringSequence, sequenceStart, out deferred);
        }

        internal static bool SupportsCoherentSequenceResolution
        {
            get
            {
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                return true;
#else
                return false;
#endif
            }
        }

        internal static ResolutionBatch BeginResolutionBatch()
        {
            var batch = activeBatch == null
                ? (threadBatch ??= new ResolutionBatch())
                : new ResolutionBatch();
            batch.Begin();
            return batch;
        }

        private static Core Resolve(ReadOnlySpan<int> sequence, string language, string family,
            int weight, bool italic,
            bool requireFamily, Core stylePrimary,
            ResolutionMode mode, int sequenceStart, out bool deferred)
        {
            deferred = false;
            if (weight > 0) weight = Math.Clamp(weight, 1, 1000);
            language = language?.Trim() ?? string.Empty;
            family = family?.Trim() ?? string.Empty;
            var sequenceKey = new CodepointSequenceKey(sequence);
            var key = new SystemFontRequestKey(in sequenceKey, language, family,
                weight, italic, requireFamily, mode);
            var version = globalVersion;
            var positioned = mode != ResolutionMode.Standard;
            if (positioned && (activeBatch == null || sequenceStart < 0))
                throw new InvalidOperationException(
                    "Positioned system-font resolution requires an active batch.");
            var resolutionBatch = mode == ResolutionMode.Contextual ? activeBatch : null;

            Core original = null;
            if (resolutionBatch != null)
            {
                if (resolutionBatch.TryGetBatched(key, sequenceStart, out original))
                {
                    resolutionBatch.Protect(original);
                    UniTextDebug.Increment(ref UniTextDebug.SystemFont_RequestHitCount);
                    return Style(stylePrimary, original);
                }
            }
            else
            {
                var state = tlsState;
                var tls = state != null ? Volatile.Read(ref state.cache) : null;
                if (tls != null && Volatile.Read(ref state.version) == version
                    && tls.TryGetValue(key, out var tlsValue)
                    && tlsValue.TryGet(out var tlsFont)
                    && (activeBatch != null ? activeBatch.Protect(tlsFont) : TryTouchSource(tlsFont)))
                {
                    UniTextDebug.Increment(ref UniTextDebug.SystemFont_RequestHitCount);
                    return Style(stylePrimary, tlsFont);
                }

                rwLock.EnterReadLock();
                try
                {
                    if (requestCache.TryGetValue(key, out original))
                    {
                        if (activeBatch != null) activeBatch.ProtectLocked(original);
                        else TouchSourceLocked(original);
                        UniTextDebug.Increment(ref UniTextDebug.SystemFont_RequestHitCount);
                        StoreTls(key, original, version);
                        return Style(stylePrimary, original);
                    }
                }
                finally { rwLock.ExitReadLock(); }
            }

            if (activeBatch != null && sequenceStart >= 0)
            {
                activeBatch.Add(new PendingRequest(key, sequenceStart));
                UniTextDebug.Increment(ref UniTextDebug.SystemFont_DeferredRequestCount);
                deferred = true;
                return null;
            }

            lock (requestLocks[key.GetHashCode() & (LockStripeCount - 1)])
            {
                rwLock.EnterReadLock();
                try
                {
                    if (requestCache.TryGetValue(key, out original))
                    {
                        if (activeBatch != null) activeBatch.ProtectLocked(original);
                        else TouchSourceLocked(original);
                        UniTextDebug.Increment(ref UniTextDebug.SystemFont_RequestHitCount);
                        StoreTls(key, original, globalVersion);
                        return Style(stylePrimary, original);
                    }
                }
                finally { rwLock.ExitReadLock(); }

                var text = sequenceKey.GetText();
                original = ResolveRequest(text, language, family,
                    weight, italic, requireFamily, version);
                StoreRequest(key, original, version);
                if (!TryGetRequest(key, out original))
                    return Resolve(sequence, language, family, weight, italic, requireFamily,
                        stylePrimary, mode,
                        sequenceStart, out deferred);
            }

            if (activeBatch == null)
            {
                var resolved = original;
                Trim();
                if (TryGetRequest(key, out var cached)) StoreTls(key, cached, globalVersion);
                return Style(stylePrimary, resolved);
            }

            StoreTls(key, original, globalVersion);
            return Style(stylePrimary, original);
        }

        private static Core Style(Core stylePrimary, Core original)
            => original != null && stylePrimary != null ? stylePrimary.GetStyledFallback(original) : original;

        private static void StoreTls(SystemFontRequestKey key, Core value, int version)
        {
            var state = GetThreadCacheState();
            var cache = Volatile.Read(ref state.cache);
            if (cache == null || Volatile.Read(ref state.version) != version)
            {
                cache = new Dictionary<SystemFontRequestKey, ThreadCacheValue>(64);
                Volatile.Write(ref state.cache, cache);
                Volatile.Write(ref state.version, version);
            }
            else if (cache.Count >= ThreadCacheCapacity)
            {
                cache.Clear();
            }
            cache[key] = new ThreadCacheValue(value);
        }

        private static ThreadCacheState GetThreadCacheState()
        {
            var state = tlsState;
            if (state != null) return state;
            state = new ThreadCacheState();
            tlsState = state;
            lock (threadStatesLock) threadStates.Add(new WeakReference<ThreadCacheState>(state));
            return state;
        }

        private static void ClearThreadCaches()
        {
            lock (threadStatesLock)
                for (var i = threadStates.Count - 1; i >= 0; i--)
                    if (threadStates[i].TryGetTarget(out var state))
                    {
                        Volatile.Write(ref state.cache, null);
                        Volatile.Write(ref state.version, globalVersion);
                    }
                    else threadStates.RemoveAt(i);
        }

        private static Core ResolveRequest(string text, string language, string family,
            int weight, bool italic, bool requireFamily, int version)
        {
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            var source = ResolvePlatformRun(text, language, family,
                weight, italic, requireFamily, version);
            if (source == null)
                source = ResolvePlatformMatch(text, language, family,
                    weight, italic, requireFamily, version);
            if (source == null && !requireFamily)
                source = ResolveScriptFont(text, GetSequenceScript(text),
                    weight, italic, family, version);
            return source;
#else
            return ResolvePlatformMatch(text, language, family,
                weight, italic, requireFamily, version);
#endif
        }

        private static Core ResolvePlatformMatch(string text, string language, string family,
            int weight, bool italic, bool requireFamily, int version)
        {
            if (!TryPlatformResolve(text, language, family, weight, italic, out var match)) return null;
            try { return ResolveSource(ref match, text, family, requireFamily, version); }
            finally { match.glyphOutlineSource?.Dispose(); }
        }

#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        private static Core ResolvePlatformRun(string text, string language, string family,
            int weight, bool italic, bool requireFamily, int version)
        {
            SystemFontSourceMatch match;
            platformLifecycleLock.EnterReadLock();
            try
            {
                UniTextDebug.Increment(ref UniTextDebug.SystemFont_PlatformSingleCount);
                using var runs = NativeFontReader.OpenSystemFontRuns(text, language, family,
                    weight, italic);
                if (runs.Count != 1) return null;
                runs.GetRange(0, out var start, out var length);
                if (start != 0 || length != text.Length) return null;
                if (!runs.TryRead(0, length, out match))
                {
                    match.glyphOutlineSource?.Dispose();
                    return null;
                }
            }
            finally { platformLifecycleLock.ExitReadLock(); }
            try { return ResolveSource(ref match, text, family, requireFamily, version); }
            finally { match.glyphOutlineSource?.Dispose(); }
        }

        private static Core ResolveScriptFont(string text, UnicodeScript script,
            int weight, bool italic, string family, int version)
        {
            for (var index = 0;; index++)
            {
                if (version != globalVersion) return null;
                var candidate = NativeFontReader.GetScriptFontCandidate(script, index);
                if (candidate == null) return null;

                platformLifecycleLock.EnterReadLock();
                SystemFontSourceMatch match;
                try
                {
                    if (!NativeFontReader.TryResolveNamedSystemFont(text, candidate,
                            weight, italic, out match))
                        continue;
                }
                finally { platformLifecycleLock.ExitReadLock(); }
                try
                {
                    var source = ResolveSource(ref match, text, family,
                        false, version, reportFailure: false);
                    if (source != null) return source;
                }
                finally { match.glyphOutlineSource?.Dispose(); }
            }
        }

#endif

        private static Core ResolveSource(ref SystemFontSourceMatch match,
            string text, string family, bool requireFamily, int version, bool reportFailure = true)
        {
            if (version != globalVersion || string.IsNullOrEmpty(match.descriptor)
                || match.coveredUtf16Length < text.Length)
                return null;

            match.axes = NormalizeAxes(match.axes);
            match.scale = NormalizeScale(match.scale);
            var materialized = default(SystemFontMaterializedSource);
            var isMaterialized = false;
            var faceIndex = match.faceIndex;
            if (faceIndex < 0)
            {
                if (!TryMaterializeSource(in match, text, out materialized, reportFailure))
                    return null;
                isMaterialized = true;
                faceIndex = Math.Max(0, materialized.faceInfo.faceIndex);
            }

            var sourceKey = new SystemFontSourceKey(match.descriptor, faceIndex,
                match.axes, match.scale);

            SourceEntry entry;
            rwLock.EnterReadLock();
            try { sourceCache.TryGetValue(sourceKey, out entry); }
            finally { rwLock.ExitReadLock(); }
            var source = entry?.font;
            if (source != null)
            {
                Interlocked.Exchange(ref entry.lastUse, Interlocked.Increment(ref useClock));
                UniTextDebug.Increment(ref UniTextDebug.SystemFont_SourceHitCount);
            }

            if (source == null)
            {
                lock (sourceLocks[sourceKey.GetHashCode() & (LockStripeCount - 1)])
                {
                    rwLock.EnterReadLock();
                    try { sourceCache.TryGetValue(sourceKey, out entry); }
                    finally { rwLock.ExitReadLock(); }
                    source = entry?.font;
                    if (source != null)
                    {
                        Interlocked.Exchange(ref entry.lastUse, Interlocked.Increment(ref useClock));
                        UniTextDebug.Increment(ref UniTextDebug.SystemFont_SourceHitCount);
                    }

                    if (source == null)
                    {
                        UniTextDebug.Increment(ref UniTextDebug.SystemFont_SourceLoadCount);
                        if (!isMaterialized
                            && !TryMaterializeSource(in match, text, out materialized, reportFailure))
                            return null;
                        var built = BuildCompanion(in match, in materialized);
                        if (built == null) return null;
                        rwLock.EnterWriteLock();
                        try
                        {
                            if (version == globalVersion)
                            {
                                if (!sourceCache.TryGetValue(sourceKey, out entry))
                                {
                                    source = built;
                                    entry = new SourceEntry
                                    {
                                        key = sourceKey,
                                        font = source,
                                        descriptor = match.descriptor,
                                        lastUse = Interlocked.Increment(ref useClock),
                                    };
                                    sourceCache[sourceKey] = entry;
                                    sourceOwners[source] = entry;
                                    SystemFontByteCache.Retain(match.descriptor, source.Source);
                                    activeBatch?.MarkCacheDirty();
                                }
                                else source = entry.font;
                            }
                        }
                        finally { rwLock.ExitWriteLock(); }
                        if (!ReferenceEquals(source, built)) built.Dispose();
                        if (source == null) return null;
                    }
                }
            }

            if (IsLastResort(source.FaceInfo.familyName)
                || requireFamily && !FamilyMatches(source.FaceInfo.familyName, family)
                || !SystemFontFaces.Covers(source, text))
                return null;
            if (activeBatch != null && !activeBatch.Protect(source)) return null;
            return source;
        }

        private static bool TryMaterializeSource(in SystemFontSourceMatch match, string text,
            out SystemFontMaterializedSource source, bool reportFailure = true)
        {
#if (UNITY_IOS && !UNITY_EDITOR) || UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            string reason;
            if (match.glyphOutlineSource != null)
            {
                var faceInfo = match.glyphOutlineSource.FaceInfo;
                if (SystemFontFaces.TryValidateShapingSource(match.fontSource, match.faceIndex,
                        text, faceInfo.unitsPerEm, out reason))
                {
                    source = new SystemFontMaterializedSource
                    {
                        fontSource = match.fontSource,
                        faceInfo = faceInfo,
                        glyphOutlineSource = match.glyphOutlineSource,
                    };
                    return true;
                }
            }
            else
            {
                if (SystemFontFaces.TryMaterialize(match.fontSource, in match, text, out source)) return true;
                reason = "its shaping tables do not cover the text";
            }

            source = default;
            if (reportFailure)
                CatZones.systemFont.MeowWarn(
                    $"[SystemFont] Skipping CoreText match '{match.postScriptName}' for \"{text}\": {reason}.");
            return false;
#else
            return SystemFontFaces.TryReadSource(match.descriptor, in match, text, out source);
#endif
        }

        /// <summary>Materializes whatever face the platform binds to <paramref name="family"/>, or its default face when <paramref name="family"/> is empty, independently of <see cref="Disabled"/> and of the resolution caches. The platform may substitute another family; callers that require an exact family compare <see cref="FaceInfo.familyName"/> themselves. The returned <see cref="SystemFontMaterializedSource.glyphOutlineSource"/> is owned by the caller.</summary>
        internal static bool TryMaterializeFamily(string family, out SystemFontMaterializedSource source,
            out UniTextFont.AxisDefault[] axes, out string descriptor)
        {
            source = default;
            axes = null;
            descriptor = null;
            if (!TryPlatformResolve("A", string.Empty, family?.Trim() ?? string.Empty, 0, false, out var match))
                return false;
            try
            {
                if (string.IsNullOrEmpty(match.descriptor)
                    || !TryMaterializeSource(in match, "A", out var materialized))
                    return false;
                source = new SystemFontMaterializedSource
                {
                    fontSource = materialized.fontSource,
                    faceInfo = materialized.faceInfo,
                    glyphOutlineSource = materialized.glyphOutlineSource?.Retain(),
                };
                axes = NormalizeAxes(match.axes);
                descriptor = match.descriptor;
                return true;
            }
            finally { match.glyphOutlineSource?.Dispose(); }
        }

        private static void ResolveBatch(List<PendingRequest> pending)
        {
            var index = 0;
            while (index < pending.Count)
            {
                var end = index + 1;
                while (end < pending.Count && CanBatch(pending[end - 1], pending[end])) end++;
                ResolveBatchGroup(pending, index, end);
                index = end;
            }
        }

        private static bool CanBatch(PendingRequest left, PendingRequest right)
            => (left.key.mode == ResolutionMode.CoveringSequence
                || left.sequenceStart + left.key.sequence.Length == right.sequenceStart)
               && left.key.weight == right.key.weight && left.key.italic == right.key.italic
               && left.key.requireFamily == right.key.requireFamily
               && left.key.mode == right.key.mode
               && string.Equals(left.key.language, right.key.language, StringComparison.OrdinalIgnoreCase)
               && string.Equals(left.key.family, right.key.family, StringComparison.OrdinalIgnoreCase);

        private static void ResolveBatchGroup(List<PendingRequest> pending, int start, int end)
        {
            var key = pending[start].key;
            if (key.mode == ResolutionMode.Contextual)
            {
                ResolveBatchGroupCore(pending, start, end);
                return;
            }
            lock (requestLocks[key.GetHashCode() & (LockStripeCount - 1)])
            {
                var unresolved = false;
                for (var i = start; i < end; i++)
                {
                    var request = pending[i];
                    if (!TryGetResolvedRequest(in request, out _))
                    {
                        unresolved = true;
                        break;
                    }
                }
                if (unresolved) ResolveBatchGroupCore(pending, start, end);
            }
        }

        private static void ResolveBatchGroupCore(List<PendingRequest> pending, int start, int end)
        {
            var version = globalVersion;
            var count = end - start;
            var offsets = ArrayPool<int>.Rent(count + 1);
            SystemFontSourceMatch[] matches = null;
            var builder = new StringBuilder();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    offsets[i] = builder.Length;
                    pending[start + i].key.sequence.AppendTo(builder);
                }
                offsets[count] = builder.Length;

                var first = pending[start];
                var text = builder.ToString();
                UniTextDebug.Increment(ref UniTextDebug.SystemFont_PlatformBatchCount);
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                if (first.key.mode == ResolutionMode.Contextual)
                {
                    ResolvePlatformRuns(pending, start, end, text, offsets, version);
                    var hasUnresolved = false;
                    for (var i = start; i < end; i++)
                    {
                        var request = pending[i];
                        if (!TryGetResolvedRequest(in request, out _))
                        {
                            hasUnresolved = true;
                            break;
                        }
                    }
                    if (!hasUnresolved) return;
                }
#endif
                matches = ArrayPool<SystemFontSourceMatch>.Rent(count);
                Array.Clear(matches, 0, count);
                var batched = TryPlatformResolveBatch(text, offsets, count, first.key.language,
                    first.key.family, first.key.weight, first.key.italic, matches);

                for (var i = 0; i < count; i++)
                {
                    var request = pending[start + i];
                    if (TryGetResolvedRequest(in request, out _)) continue;

                    var match = matches[i];
                    var requestSequence = request.key.sequence;
                    var requestText = requestSequence.GetText();
                    if (!batched || string.IsNullOrEmpty(match.descriptor)
                                 || match.coveredUtf16Length < requestSequence.Utf16Length)
                    {
#if !UNITY_EDITOR_OSX && !(UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                        var individualSource = ResolveRequest(requestText,
                            request.key.language, request.key.family, request.key.weight, request.key.italic,
                            request.key.requireFamily, version);
                        StoreResolvedRequest(in request, individualSource, version);
#endif
                        continue;
                    }

                    var source = ResolveSource(ref match, requestText,
                        request.key.family, request.key.requireFamily, version);
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                    if (source == null) continue;
#endif
                    StoreResolvedRequest(in request, source, version);
                }
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                for (var i = 0; i < count; i++)
                {
                    var request = pending[start + i];
                    if (TryGetResolvedRequest(in request, out _)) continue;
                    Core source = null;
                    if (!request.key.requireFamily)
                    {
                        var requestText = request.key.sequence.GetText();
                        source = ResolveScriptFont(requestText,
                            GetSequenceScript(requestText), request.key.weight, request.key.italic,
                            request.key.family, version);
                    }
                    StoreResolvedRequest(in request, source, version);
                }
#endif
            }
            finally
            {
                if (matches != null)
                {
                    for (var i = 0; i < count; i++)
                    {
                        matches[i].glyphOutlineSource?.Dispose();
                        matches[i] = default;
                    }
                    ArrayPool<SystemFontSourceMatch>.Return(matches);
                }
                ArrayPool<int>.Return(offsets);
            }
        }

#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        private static void ResolvePlatformRuns(List<PendingRequest> pending, int start, int end,
            string text, int[] offsets, int version)
        {
            var count = end - start;
            var first = pending[start];
            platformLifecycleLock.EnterReadLock();
            try
            {
                using var runs = NativeFontReader.OpenSystemFontRuns(text,
                    first.key.language, first.key.family, first.key.weight, first.key.italic);
                var runOffsets = ArrayPool<int>.Rent(runs.Count + 1);
                try
                {
                    runOffsets[0] = 0;
                    for (var runIndex = 0; runIndex < runs.Count; runIndex++)
                    {
                        runs.GetRange(runIndex, out var runStart, out var runLength);
                        var runEnd = runStart + runLength;
                        if (runStart != runOffsets[runIndex]
                            || runEnd < runStart || runEnd > text.Length
                            || runStart > 0 && char.IsLowSurrogate(text[runStart])
                            || runEnd < text.Length && char.IsLowSurrogate(text[runEnd]))
                            throw new InvalidOperationException(
                                "CoreText returned incomplete or out-of-bounds system-font runs.");
                        runOffsets[runIndex + 1] = runEnd;
                    }
                    if (runOffsets[runs.Count] != text.Length)
                        throw new InvalidOperationException(
                            "CoreText returned an incomplete system-font run partition.");

                    var nextRequest = 0;
                    for (var runIndex = 0; runIndex < runs.Count; runIndex++)
                    {
                        var runStart = runOffsets[runIndex];
                        var runEnd = runOffsets[runIndex + 1];
                        var runLength = runEnd - runStart;
                        var readable = runs.TryRead(runIndex, runLength, out var match);
                        try
                        {
                            var source = readable
                                ? ResolveSource(ref match, text.Substring(runStart, runLength),
                                    first.key.family, first.key.requireFamily, version, reportFailure: false)
                                : null;
                            while (nextRequest < count && offsets[nextRequest + 1] <= runStart)
                                nextRequest++;
                            var requestIndex = nextRequest;
                            while (requestIndex < count && offsets[requestIndex] < runEnd)
                            {
                                if (source != null && offsets[requestIndex] >= runStart
                                                   && offsets[requestIndex + 1] <= runEnd)
                                {
                                    var request = pending[start + requestIndex];
                                    if (!TryGetResolvedRequest(in request, out _))
                                        StoreResolvedRequest(in request, source, version);
                                }
                                requestIndex++;
                            }
                            nextRequest = requestIndex;
                        }
                        finally { match.glyphOutlineSource?.Dispose(); }
                    }
                }
                finally { ArrayPool<int>.Return(runOffsets); }
            }
            finally { platformLifecycleLock.ExitReadLock(); }
        }
#endif

        private static bool TryGetResolvedRequest(in PendingRequest request, out Core source)
        {
            if (request.key.mode != ResolutionMode.Contextual)
                return TryGetRequest(request.key, out source);
            var batch = activeBatch
                        ?? throw new InvalidOperationException(
                            "Positioned system-font results require an active resolution batch.");
            var found = batch.TryGetBatched(request.key, request.sequenceStart, out source);
            if (found) batch.Protect(source);
            return found;
        }

        private static void StoreResolvedRequest(in PendingRequest request, Core source, int version)
        {
            if (request.key.mode != ResolutionMode.Contextual)
            {
                StoreRequest(request.key, source, version);
                return;
            }
            var batch = activeBatch
                        ?? throw new InvalidOperationException(
                            "Positioned system-font results require an active resolution batch.");
            batch.StoreBatched(request.key, request.sequenceStart, source, version);
        }

        private static bool TryGetRequest(SystemFontRequestKey key, out Core source)
        {
            rwLock.EnterReadLock();
            try
            {
                var found = requestCache.TryGetValue(key, out source);
                if (found)
                {
                    if (activeBatch != null) activeBatch.ProtectLocked(source);
                    else TouchSourceLocked(source);
                }
                return found;
            }
            finally { rwLock.ExitReadLock(); }
        }

        private static void StoreRequest(SystemFontRequestKey key, Core source, int version)
        {
            rwLock.EnterWriteLock();
            try
            {
                if (version != globalVersion || requestCache.ContainsKey(key)) return;
                EnsureRequestCapacity();
                requestCache[key] = source;
            }
            finally { rwLock.ExitWriteLock(); }
        }

        private static void EnsureRequestCapacity()
        {
            if (requestCache.Count < RequestCacheCapacity) return;
            requestCache.Clear();
            globalVersion++;
            ClearThreadCaches();
        }

        private static Core BuildCompanion(in SystemFontSourceMatch match,
            in SystemFontMaterializedSource materialized)
        {
            var faceInfo = materialized.faceInfo;

            var upem = faceInfo.unitsPerEm > 0
                ? faceInfo.unitsPerEm
                : SystemFontFaces.GetUpem(materialized.fontSource, match.faceIndex);
            if (upem <= 0) upem = 1000;

            List<UniTextFont.AxisDefault> axes = null;
            if (match.axes is { Length: > 0 })
                axes = new List<UniTextFont.AxisDefault>(match.axes);

            var name = match.postScriptName ?? System.IO.Path.GetFileNameWithoutExtension(match.descriptor);
            return new Core(materialized.fontSource, faceInfo, upem, match.scale, 1f, 0, 0f, 0, 0f,
                null, name, axes, UniTextFont.SpaceAdvanceUninitialized,
                materialized.glyphOutlineSource) { isSystemFont = true };
        }

        private static object[] CreateLockStripes()
        {
            var stripes = new object[LockStripeCount];
            for (var i = 0; i < stripes.Length; i++) stripes[i] = new object();
            return stripes;
        }

        private static string EncodeSequence(ReadOnlySpan<int> sequence)
        {
            var builder = new StringBuilder(sequence.Length * 2);
            for (var i = 0; i < sequence.Length; i++)
                AppendCodepoint(builder, sequence[i]);
            return builder.ToString();
        }

        private static void AppendCodepoint(StringBuilder builder, int codepoint)
        {
            if (!IsUnicodeScalar((uint)codepoint))
                throw new ArgumentOutOfRangeException(nameof(codepoint));
            if (codepoint <= char.MaxValue)
            {
                builder.Append((char)codepoint);
                return;
            }
            codepoint -= 0x10000;
            builder.Append((char)((codepoint >> 10) + 0xD800));
            builder.Append((char)((codepoint & 0x3FF) + 0xDC00));
        }

#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        private static UnicodeScript GetSequenceScript(string text)
        {
            var fallback = UnicodeScript.Unknown;
            for (var i = 0; i < text.Length; i++)
            {
                var codepoint = char.ConvertToUtf32(text, i);
                if (codepoint > 0xFFFF) i++;
                var script = UnicodeData.GetScript(codepoint);
                if (script != UnicodeScript.Unknown && script != UnicodeScript.Common
                                                    && script != UnicodeScript.Inherited)
                    return script;
                if (fallback == UnicodeScript.Unknown) fallback = script;
            }
            return fallback;
        }
#endif

        private static UniTextFont.AxisDefault[] NormalizeAxes(UniTextFont.AxisDefault[] axes)
        {
            if (axes == null || axes.Length == 0) return null;
            var isNormalized = true;
            for (var i = 0; i < axes.Length; i++)
            {
                var value = axes[i].value;
                var normalizedValue = float.IsNaN(value) || float.IsInfinity(value)
                    ? 0f
                    : FontVariation.FromFixed(FontVariation.ToFixed(value));
                if (float.IsNaN(value) || float.IsInfinity(value)
                    || BitConverter.SingleToInt32Bits(value) != BitConverter.SingleToInt32Bits(normalizedValue)
                    || i > 0 && axes[i - 1].tag >= axes[i].tag)
                {
                    isNormalized = false;
                    break;
                }
            }
            if (isNormalized) return axes;

            axes = (UniTextFont.AxisDefault[])axes.Clone();
            Array.Sort(axes, (left, right) => left.tag.CompareTo(right.tag));
            var count = 0;
            for (var i = 0; i < axes.Length; i++)
            {
                var value = axes[i].value;
                if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                value = FontVariation.FromFixed(FontVariation.ToFixed(value));
                axes[i].value = value;
                if (count > 0 && axes[count - 1].tag == axes[i].tag)
                    axes[count - 1] = axes[i];
                else
                    axes[count++] = axes[i];
            }
            if (count == 0) return null;
            if (count == axes.Length) return axes;
            var normalized = new UniTextFont.AxisDefault[count];
            Array.Copy(axes, normalized, count);
            return normalized;
        }

        private static float NormalizeScale(float scale)
            => scale > 0f && !float.IsInfinity(scale) && !float.IsNaN(scale) ? scale : 1f;

        internal static bool FamilyMatches(string resolved, string requested)
            => !string.IsNullOrEmpty(resolved) && !string.IsNullOrEmpty(requested)
               && resolved.Trim().Equals(requested.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool IsLastResort(string familyName)
            => !string.IsNullOrEmpty(familyName)
               && familyName.IndexOf("LastResort", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsUnicodeScalar(uint codepoint)
            => codepoint <= 0x10FFFF && (codepoint < 0xD800 || codepoint > 0xDFFF);

        private static bool TryTouchSource(Core font)
        {
            var source = font?.SystemFontSource;
            if (source == null) return true;
            rwLock.EnterReadLock();
            try
            {
                if (!sourceOwners.TryGetValue(source, out var entry)) return false;
                Interlocked.Exchange(ref entry.lastUse, Interlocked.Increment(ref useClock));
                return true;
            }
            finally { rwLock.ExitReadLock(); }
        }

        private static void TouchSourceLocked(Core font)
        {
            var source = font?.SystemFontSource;
            if (source != null && sourceOwners.TryGetValue(source, out var entry))
                Interlocked.Exchange(ref entry.lastUse, Interlocked.Increment(ref useClock));
        }

        internal static void Acquire(Core font)
        {
            var source = font?.SystemFontSource;
            if (source == null) return;
            rwLock.EnterWriteLock();
            try
            {
                if (!sourceOwners.TryGetValue(source, out var entry)) return;
                entry.leases++;
                entry.lastUse = Interlocked.Increment(ref useClock);
            }
            finally { rwLock.ExitWriteLock(); }
        }

        internal static void Release(Core[] fonts)
        {
            if (fonts == null || fonts.Length == 0) return;
            var trim = false;
            rwLock.EnterWriteLock();
            try
            {
                for (var i = 0; i < fonts.Length; i++)
                {
                    var source = fonts[i]?.SystemFontSource;
                    if (source == null || !sourceOwners.TryGetValue(source, out var entry)
                                       || entry.leases <= 0)
                        continue;
                    entry.leases--;
                    trim |= entry.leases == 0;
                    entry.lastUse = Interlocked.Increment(ref useClock);
                }
            }
            finally { rwLock.ExitWriteLock(); }
            if (trim) Trim();
        }

        private static bool Release(HashSet<Core> fonts)
        {
            if (fonts == null || fonts.Count == 0) return false;
            var trim = false;
            rwLock.EnterWriteLock();
            try
            {
                foreach (var source in fonts)
                    if (source != null && sourceOwners.TryGetValue(source, out var entry)
                                       && entry.leases > 0)
                    {
                        entry.leases--;
                        trim |= entry.leases == 0;
                        entry.lastUse = Interlocked.Increment(ref useClock);
                    }
            }
            finally { rwLock.ExitWriteLock(); }
            return trim;
        }

        internal static bool NotifyByteCacheGrowth()
        {
            if (activeBatch == null) return false;
            activeBatch.MarkCacheDirty();
            return true;
        }

        /// <summary>Evicts least-recently-used source faces beyond <see cref="InactiveSourceCapacity"/> and trims unreferenced font-file bytes to <see cref="InactiveByteBudget"/>. Faces leased by live providers are preserved.</summary>
        public static void Trim() => Trim(false);

        /// <summary>Releases every inactive source face and unreferenced font-file byte immediately. Active text providers remain valid.</summary>
        public static void TrimUnused() => Trim(true);

        private static void Trim(bool aggressive)
        {
            List<SourceEntry> evicted = null;
            var changedGeneration = false;
            long retainedInactiveBytes = 0;
            var byteBudget = aggressive ? 0 : SystemFontByteCache.InactiveByteBudget;
            lock (defaultLock)
            {
                rwLock.EnterWriteLock();
                try
                {
                    var candidates = new List<SourceEntry>();
                    var activeDescriptors = new HashSet<string>(DescriptorComparer);
                    foreach (var entry in sourceCache.Values)
                        if (entry.leases > 0)
                            activeDescriptors.Add(entry.descriptor);
                        else
                            candidates.Add(entry);

                    var target = aggressive ? 0 : Volatile.Read(ref inactiveSourceCapacity);
                    var descriptorCounts = new Dictionary<string, int>(DescriptorComparer);
                    var descriptorSizes = new Dictionary<string, long>(DescriptorComparer);
                    long inactiveBytes = 0;
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        var candidate = candidates[i];
                        if (activeDescriptors.Contains(candidate.descriptor)) continue;
                        if (descriptorCounts.TryGetValue(candidate.descriptor, out var count))
                        {
                            descriptorCounts[candidate.descriptor] = count + 1;
                            continue;
                        }
                        descriptorCounts[candidate.descriptor] = 1;
                        var size = candidate.font.Source?.OwnedByteCount ?? 0;
                        descriptorSizes[candidate.descriptor] = size;
                        inactiveBytes += size;
                    }

                    if (candidates.Count > target || inactiveBytes > byteBudget)
                    {
                        candidates.Sort((left, right) => left.lastUse.CompareTo(right.lastUse));
                        evicted = new List<SourceEntry>();
                        var remaining = candidates.Count;
                        for (var i = 0; i < candidates.Count
                                        && (remaining > target || inactiveBytes > byteBudget); i++)
                        {
                            var oldest = candidates[i];
                            sourceCache.Remove(oldest.key);
                            sourceOwners.Remove(oldest.font);
                            if (ReferenceEquals(oldest.font, defaultFont))
                            {
                                defaultFont = null;
                                defaultFontResolved = false;
                            }
                            evicted.Add(oldest);
                            remaining--;
                            if (!activeDescriptors.Contains(oldest.descriptor)
                                && descriptorCounts.TryGetValue(oldest.descriptor, out var count))
                            {
                                count--;
                                if (count == 0)
                                {
                                    inactiveBytes -= descriptorSizes[oldest.descriptor];
                                    descriptorCounts.Remove(oldest.descriptor);
                                    descriptorSizes.Remove(oldest.descriptor);
                                }
                                else descriptorCounts[oldest.descriptor] = count;
                            }
                        }
                    }
                    retainedInactiveBytes = inactiveBytes;

                    if (evicted != null)
                    {
                        requestCache.Clear();
                        if (aggressive || evicted.Count >= sourceCache.Count)
                        {
                            sourceCache.TrimExcess();
                            sourceOwners.TrimExcess();
                            requestCache.TrimExcess();
                        }
                        globalVersion++;
                        changedGeneration = true;
                    }
                }
                finally { rwLock.ExitWriteLock(); }
            }

            if (evicted != null)
                for (var i = 0; i < evicted.Count; i++)
                {
                    var entry = evicted[i];
                    Core.ClearDynamicData(entry.font, true);
                    SystemFontByteCache.Release(entry.descriptor);
                }
            SystemFontByteCache.Trim(aggressive, Math.Max(0, byteBudget - retainedInactiveBytes));
            if (changedGeneration)
            {
                ClearThreadCaches();
                SharedFontCache.InvalidateAll();
            }
        }

        /// <summary>Discards all request/source matches, styled companions and platform byte caches so subsequent resolution observes the current installed fonts.</summary>
        public static void Reset() => Reset(true);

        private static void ResetForReload() => Reset(false);

        private static void Reset(bool notify)
        {
            SourceEntry[] sources;
            Core previousDefault;
            var defaultInSourceCache = false;
            lock (defaultLock)
            {
                platformLifecycleLock.EnterWriteLock();
                try
                {
                    rwLock.EnterWriteLock();
                    try
                    {
                        sources = new SourceEntry[sourceCache.Count];
                        sourceCache.Values.CopyTo(sources, 0);
                        previousDefault = defaultFont;
                        for (var i = 0; i < sources.Length; i++)
                            if (ReferenceEquals(sources[i].font, previousDefault))
                            {
                                defaultInSourceCache = true;
                                break;
                            }
                        sourceCache.Clear();
                        sourceOwners.Clear();
                        requestCache.Clear();
                        sourceCache.TrimExcess();
                        sourceOwners.TrimExcess();
                        requestCache.TrimExcess();
                        defaultFont = null;
                        defaultFontResolved = false;
                        globalVersion++;
                    }
                    finally { rwLock.ExitWriteLock(); }

                    SystemFontByteCache.Clear();
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                    WindowsFontBackend.Shutdown();
#elif UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
                    LinuxFontBackend.Shutdown();
#elif UNITY_ANDROID && !UNITY_EDITOR
                    AndroidFontsXmlResolver.Shutdown();
#endif
                }
                finally { platformLifecycleLock.ExitWriteLock(); }
            }

            for (var i = 0; i < sources.Length; i++)
            {
                Core.ClearDynamicData(sources[i].font, notify);
            }
            if (!defaultInSourceCache && previousDefault != null)
                Core.ClearDynamicData(previousDefault, notify);
            ClearThreadCaches();
            SharedFontCache.InvalidateAll();
            if (notify) Changed?.Invoke();
        }

        private static bool TryPlatformResolve(string text, string language, string family,
            int requestWeight, bool requestItalic, out SystemFontSourceMatch match)
        {
            UniTextDebug.Increment(ref UniTextDebug.SystemFont_PlatformSingleCount);
            platformLifecycleLock.EnterReadLock();
            try
            {
#if (UNITY_IOS && !UNITY_EDITOR) || UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                return NativeFontReader.TryResolveSystemFont(text, language, family,
                    requestWeight, requestItalic, out match);
#elif UNITY_ANDROID && !UNITY_EDITOR
                return AndroidFontBackend.TryResolve(text, language, family, requestWeight, requestItalic, out match);
#elif UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                return WindowsFontBackend.TryResolve(text, language, family, requestWeight, requestItalic, out match);
#elif UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
                return LinuxFontBackend.TryResolve(text, language, family, requestWeight, requestItalic, out match);
#else
                match = default;
                return false;
#endif
            }
            finally { platformLifecycleLock.ExitReadLock(); }
        }

        private static bool TryPlatformResolveBatch(string text, int[] offsets, int count,
            string language, string family, int requestWeight, bool requestItalic,
            SystemFontSourceMatch[] matches)
        {
            platformLifecycleLock.EnterReadLock();
            try
            {
#if (UNITY_IOS && !UNITY_EDITOR) || UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
                return NativeFontReader.TryResolveSystemFontBatch(text, offsets, count, language,
                    family, requestWeight, requestItalic, matches);
#elif UNITY_ANDROID && !UNITY_EDITOR
                return AndroidFontBackend.TryResolveBatch(text, offsets, count, language, family,
                    requestWeight, requestItalic, matches);
#elif UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                return WindowsFontBackend.TryResolveBatch(text, offsets, count, language, family,
                    requestWeight, requestItalic, matches);
#elif UNITY_EDITOR_LINUX || (UNITY_STANDALONE_LINUX && !UNITY_EDITOR)
                return LinuxFontBackend.TryResolveBatch(text, offsets, count, language, family,
                    requestWeight, requestItalic, matches);
#else
                return false;
#endif
            }
            finally { platformLifecycleLock.ExitReadLock(); }
        }
    }
}
