#if UNITY_IOS && !UNITY_EDITOR
using System;

namespace LightSide
{
    /// <summary>
    /// Owns an independently retained CoreText system emoji face reference and shares lookup caches with sibling retains.
    /// </summary>
    internal sealed class CoreTextEmojiFontBackend : IFontFaceBackend, IColorGlyphBackend
    {
        private sealed class SharedCaches
        {
            internal readonly object gate = new();
            internal readonly FastIntDictionary<uint> glyphs = new();
            internal readonly FastIntDictionary<int> advances = new();
        }

        private readonly object faceLock = new();
        private readonly SharedCaches caches;
        private IntPtr face;

        [ThreadStatic] private static uint[] glyphIdBuffer;
        [ThreadStatic] private static int[] advanceBuffer;
        [ThreadStatic] private static int[] clusterBuffer;
        [ThreadStatic] private static int[] presentationForcedBuffer;
        [ThreadStatic] private static int[] presentationClusterBuffer;
        [ThreadStatic] private static bool[] presentationGraphemeBuffer;

        public string Identity { get; }
        public FaceInfo FaceInfo { get; }
        public int UnitsPerEm => FaceInfo.unitsPerEm;

        private CoreTextEmojiFontBackend(IntPtr face, string identity, FaceInfo faceInfo,
            SharedCaches caches)
        {
            this.face = face;
            Identity = identity;
            FaceInfo = faceInfo;
            this.caches = caches;
        }

        internal static CoreTextEmojiFontBackend Open()
        {
            var face = NativeFontReader.OpenSystemEmojiFace(out var identity, out var faceInfo);
            try
            {
                return new CoreTextEmojiFontBackend(face, identity, faceInfo, new SharedCaches());
            }
            catch
            {
                NativeFontReader.ReleaseSystemEmojiFace(face);
                throw;
            }
        }

        public IFontFaceBackend Retain()
        {
            lock (faceLock)
            {
                if (face == IntPtr.Zero)
                    throw new ObjectDisposedException(nameof(CoreTextEmojiFontBackend));
                var retained = NativeFontReader.RetainSystemEmojiFace(face);
                try { return new CoreTextEmojiFontBackend(retained, Identity, FaceInfo, caches); }
                catch
                {
                    NativeFontReader.ReleaseSystemEmojiFace(retained);
                    throw;
                }
            }
        }

        public bool TryGetGlyph(uint codepoint, out uint glyphIndex)
        {
            EnsureActive();
            var key = (int)codepoint;
            lock (caches.gate)
                if (caches.glyphs.TryGetValue(key, out glyphIndex))
                    return glyphIndex != 0;

            using var lease = AcquireFace();
            var found = NativeFontReader.TryGetSystemEmojiGlyph(lease.Handle, codepoint,
                out glyphIndex, out var advance);
            lock (caches.gate)
            {
                caches.glyphs[key] = found ? glyphIndex : 0;
                if (found) caches.advances[(int)glyphIndex] = advance;
            }
            return found;
        }

        public int GetGlyphAdvance(uint glyphIndex)
        {
            EnsureActive();
            if (glyphIndex == 0) return 0;
            var key = (int)glyphIndex;
            lock (caches.gate)
                if (caches.advances.TryGetValue(key, out var cached))
                    return cached;

            using var lease = AcquireFace();
            var advance = NativeFontReader.GetSystemEmojiGlyphAdvance(lease.Handle, glyphIndex);
            lock (caches.gate) caches.advances[key] = advance;
            return advance;
        }

        public unsafe int Shape(ReadOnlySpan<int> context, int itemOffset, int itemLength,
            RawShapedGlyph[] output)
        {
            if ((uint)itemOffset > (uint)context.Length
                || itemLength < 0 || itemLength > context.Length - itemOffset)
                throw new ArgumentOutOfRangeException(nameof(itemLength));
            if (itemLength == 0) return 0;
            if (output == null) throw new ArgumentNullException(nameof(output));

            var shapeContext = context.Slice(itemOffset, itemLength);
            var presentationLength = PrepareEmojiPresentation(shapeContext);
            if (presentationLength != 0)
                shapeContext = presentationForcedBuffer.AsSpan(0, presentationLength);

            EnsureBuffers(output.Length);
            int glyphCount;
            using (var lease = AcquireFace())
            {
                fixed (int* codepoints = shapeContext)
                fixed (uint* glyphIds = glyphIdBuffer)
                fixed (int* advances = advanceBuffer)
                fixed (int* clusters = clusterBuffer)
                {
                    glyphCount = NativeFontReader.ShapeSystemEmojiRun(lease.Handle,
                        codepoints, shapeContext.Length,
                        glyphIds, advances, clusters, output.Length);
                }
            }

            if (glyphCount <= 0) return glyphCount;
            if (glyphCount > output.Length)
                throw new InvalidOperationException("CoreText exceeded the system emoji shaping buffer.");

            for (var i = 0; i < glyphCount; i++)
            {
                var cluster = clusterBuffer[i];
                if ((uint)cluster >= (uint)shapeContext.Length)
                    throw new InvalidOperationException("CoreText returned an invalid system emoji cluster.");
                if (presentationLength != 0) cluster = presentationClusterBuffer[cluster];
                var glyph = glyphIdBuffer[i];
                output[i] = new RawShapedGlyph
                {
                    glyphId = (int)glyph,
                    cluster = cluster + itemOffset,
                    xAdvance = advanceBuffer[i]
                };
            }
            return glyphCount;
        }

        /// <summary>
        /// Forces emoji presentation for single-codepoint default-text pictograph clusters in a run already
        /// assigned to the emoji face, while preserving source cluster indices for the synthetic selectors.
        /// </summary>
        private static int PrepareEmojiPresentation(ReadOnlySpan<int> source)
        {
            var unicode = UnicodeData.Provider;
            if (source.Length == 1)
            {
                if (!NeedsEmojiPresentation(unicode, source[0])) return 0;
                EnsurePresentationBuffers(2);
                presentationForcedBuffer[0] = source[0];
                presentationForcedBuffer[1] = UnicodeData.VariationSelector16;
                presentationClusterBuffer[0] = 0;
                presentationClusterBuffer[1] = 0;
                return 2;
            }

            var hasCandidate = false;
            for (var i = 0; i < source.Length; i++)
                if (NeedsEmojiPresentation(unicode, source[i]))
                {
                    hasCandidate = true;
                    break;
                }
            if (!hasCandidate) return 0;

            EnsurePresentationGraphemeBuffer(source.Length + 1);
            Array.Clear(presentationGraphemeBuffer, 0, source.Length + 1);
            SharedPipelineComponents.GraphemeBreaker.GetBreakOpportunities(source,
                presentationGraphemeBuffer.AsSpan(0, source.Length + 1));

            var additions = 0;
            var clusterStart = 0;
            for (var i = 1; i <= source.Length; i++)
            {
                if (!presentationGraphemeBuffer[i]) continue;
                if (i - clusterStart == 1
                    && NeedsEmojiPresentation(unicode, source[clusterStart]))
                    additions++;
                clusterStart = i;
            }
            if (additions == 0) return 0;

            var length = checked(source.Length + additions);
            EnsurePresentationBuffers(length);
            var target = 0;
            clusterStart = 0;
            for (var i = 1; i <= source.Length; i++)
            {
                if (!presentationGraphemeBuffer[i]) continue;
                for (var j = clusterStart; j < i; j++)
                {
                    presentationForcedBuffer[target] = source[j];
                    presentationClusterBuffer[target++] = j;
                }
                if (i - clusterStart == 1
                    && NeedsEmojiPresentation(unicode, source[clusterStart]))
                {
                    presentationForcedBuffer[target] = UnicodeData.VariationSelector16;
                    presentationClusterBuffer[target++] = clusterStart;
                }
                clusterStart = i;
            }
            return target;
        }

        private static bool NeedsEmojiPresentation(UnicodeDataProvider unicode, int codepoint)
            => !unicode.IsEmojiPresentation(codepoint) && unicode.IsExtendedPictographic(codepoint);

        public bool TryRenderGlyph(uint glyphIndex, int pixelSize,
            out FreeType.RenderedGlyph result)
        {
            using var lease = AcquireFace();
            return NativeFontReader.TryRenderSystemEmojiGlyph(lease.Handle,
                glyphIndex, pixelSize, out result);
        }

        public void Dispose()
        {
            IntPtr current;
            lock (faceLock)
            {
                current = face;
                face = IntPtr.Zero;
            }
            if (current == IntPtr.Zero) return;
            GC.SuppressFinalize(this);
            NativeFontReader.ReleaseSystemEmojiFace(current);
        }

        ~CoreTextEmojiFontBackend() => Dispose();

        private FaceLease AcquireFace()
        {
            lock (faceLock)
            {
                if (face == IntPtr.Zero)
                    throw new ObjectDisposedException(nameof(CoreTextEmojiFontBackend));
                var retained = NativeFontReader.RetainSystemEmojiFace(face);
                return new FaceLease(retained);
            }
        }

        private void EnsureActive()
        {
            lock (faceLock)
                if (face == IntPtr.Zero)
                    throw new ObjectDisposedException(nameof(CoreTextEmojiFontBackend));
        }

        private static void EnsureBuffers(int capacity)
        {
            if (glyphIdBuffer != null && glyphIdBuffer.Length >= capacity) return;
            capacity = Math.Max(capacity, 64);
            var nextGlyphIds = new uint[capacity];
            var nextAdvances = new int[capacity];
            var nextClusters = new int[capacity];
            glyphIdBuffer = nextGlyphIds;
            advanceBuffer = nextAdvances;
            clusterBuffer = nextClusters;
        }

        private static void EnsurePresentationBuffers(int capacity)
        {
            if (presentationForcedBuffer != null && presentationForcedBuffer.Length >= capacity) return;
            capacity = Math.Max(capacity, 64);
            presentationForcedBuffer = new int[capacity];
            presentationClusterBuffer = new int[capacity];
        }

        private static void EnsurePresentationGraphemeBuffer(int capacity)
        {
            if (presentationGraphemeBuffer != null && presentationGraphemeBuffer.Length >= capacity) return;
            presentationGraphemeBuffer = new bool[Math.Max(capacity, 64)];
        }

        private readonly struct FaceLease : IDisposable
        {
            internal readonly IntPtr Handle;
            internal FaceLease(IntPtr handle) => Handle = handle;
            public void Dispose() => NativeFontReader.ReleaseSystemEmojiFace(Handle);
        }
    }
}
#endif
