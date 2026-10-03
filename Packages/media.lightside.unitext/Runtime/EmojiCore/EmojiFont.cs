using System;
using System.Collections.Generic;
using UnityEngine;
namespace LightSide
{
    /// <summary>
    /// Platform system emoji provider. Owns the primary face and any platform fallback faces required by
    /// individual emoji clusters.
    /// </summary>
    /// <remarks>On iOS the face is opaque; a valid instance has no raw <see cref="UniTextFont.Core.FontData"/>.</remarks>
    public class EmojiFont : ColorFontCore
    {
        /// <summary>Reserved font ID for the primary system emoji face (-1).</summary>
        public const int FontId = -1;

        private static EmojiFont instance;
        private static readonly object instanceLock = new();
        private static int createDiagBudget = 3;

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class PlatformEmojiFace : ColorFontCore
        {
            internal static PlatformEmojiFace Create(UniTextFont.Core source)
            {
                var font = new PlatformEmojiFace { ParticipatesInNormalization = false };
                var faceIndex = Math.Max(0, source.FaceInfo.faceIndex);
                if (!font.LoadFromSource(source.Source, faceIndex, DefaultSize, source.Name))
                    throw new InvalidOperationException($"Android selected unreadable emoji font '{source.Name}'.");
                if (font.HasColorFormat) return font;
                font.Dispose();
                return null;
            }
        }

        private static readonly object platformFacesLock = new();
        private static readonly Dictionary<UniTextFont.Core, ColorFontCore> platformFaces = new();
#endif

        /// <summary>Occurs when the <see cref="Disabled"/> property has changed.</summary>
        public static event Action DisableChanged;

        private static bool disabled;

        /// <summary>Global toggle for emoji rendering. Setting invalidates the codepoint cache and raises <see cref="DisableChanged"/>.</summary>
        public static bool Disabled
        {
            get => disabled;
            set
            {
                if (disabled != value)
                {
                    disabled = value;
                    SharedFontCache.InvalidateAll();
                    DisableChanged?.Invoke();
                }
            }
        }

        /// <summary>Primary system emoji runtime. Auto-creates on first access and returns null when disabled or unavailable.</summary>
        /// <exception cref="InvalidOperationException">The platform-selected system emoji face cannot be initialized.</exception>
        public static EmojiFont Instance
        {
            get
            {
                if (Disabled) return null;
                var current = System.Threading.Volatile.Read(ref instance);
                if (current != null) return current;
                lock (instanceLock)
                {
                    if (Disabled) return null;
                    current = instance;
                    if (current == null)
                    {
                        current = CreateSystemEmojiFont();
                        System.Threading.Volatile.Write(ref instance, current);
                        if (current != null || createDiagBudget-- > 0)
                            CatZones.emoji.Meow($"[EmojiDiag] EmojiFont create: ok={current != null}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}, source={current?.FontIdentity ?? "none"}, gid(1F600)={(current != null ? Shaper.GetGlyphIndex(current, 0x1F600) : 0)}");
                    }
                    return current;
                }
            }
        }

        /// <summary>Gets whether emoji rendering is available on this platform, initializing the system face when needed.</summary>
        /// <exception cref="InvalidOperationException">The platform-selected system emoji face cannot be initialized.</exception>
        public static bool IsAvailable => Instance != null;

        /// <summary>True when the singleton already exists, without triggering creation.</summary>
        internal static bool HasInstance => System.Threading.Volatile.Read(ref instance) != null;
        internal static int ExistingEmojiFontId
            => System.Threading.Volatile.Read(ref instance)?.ExistingRuntimeFontId ?? 0;

        internal static UniTextFont.Core ResolveCluster(ReadOnlySpan<int> cluster)
        {
            var primary = Instance;
            if (primary == null || cluster.IsEmpty) return primary;
#if UNITY_ANDROID && !UNITY_EDITOR
            return ResolveAndroidCluster(primary, cluster);
#else
            return primary;
#endif
        }

        internal static bool IsSystemEmojiFont(UniTextFont.Core font)
        {
            if (font is EmojiFont) return true;
#if UNITY_ANDROID && !UNITY_EDITOR
            return font is PlatformEmojiFace;
#else
            return false;
#endif
        }

        /// <summary>Forces eager creation of <see cref="Instance"/>.</summary>
        /// <exception cref="InvalidOperationException">The platform-selected system emoji face cannot be initialized.</exception>
        public static void EnsureInitialized()
        {
            var i = Instance;
        }

        /// <inheritdoc/>
        protected override string DiagnosticNamePrefix => "EmojiFont";

        internal EmojiFont() : base() => ParticipatesInNormalization = false;

        /// <summary>The emoji font is keyed in the resolution chain by its reserved <see cref="FontId"/>, not its byte hash.</summary>
        public override int GetCachedInstanceId() => FontId;

        /// <summary>The system emoji font owns the per-platform color raster paths (iOS CoreText, WebGL browser), so it can always rasterize — unlike an embedded color-font asset.</summary>
        internal override bool CanRasterizeColor => true;

        /// <summary>On iOS/WebGL the system emoji renders natively (CoreText / browser), not through the FreeType/Blend2D pools an embedded color font uses.</summary>
        private protected override bool RasterizesViaPools =>
#if (UNITY_WEBGL || UNITY_IOS) && !UNITY_EDITOR
            false;
#else
            true;
#endif

        static EmojiFont()
        {
            Application.lowMemory += TrimUnused;
#if UNITY_EDITOR
            EditorLifecycle.UnmanagedCleaning += DisposeAll;
#endif
        }

        private static void TrimUnused()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            lock (platformFacesLock)
                foreach (var font in platformFaces.Values)
                    if (!ReferenceEquals(font, instance)) font.DisposeFacePool();
            instance?.DisposeFacePool();
#else
            instance?.DisposeFacePool();
#endif
        }

        private static void DisposeAll()
        {
            lock (instanceLock)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                ColorFontCore[] faces;
                UniTextFont.Core[] sources;
                lock (platformFacesLock)
                {
                    faces = new ColorFontCore[platformFaces.Count];
                    platformFaces.Values.CopyTo(faces, 0);
                    sources = new UniTextFont.Core[platformFaces.Count];
                    platformFaces.Keys.CopyTo(sources, 0);
                    platformFaces.Clear();
                }
                for (var i = 0; i < faces.Length; i++)
                    if (!ReferenceEquals(faces[i], instance)) faces[i].Dispose();
                instance?.Dispose();
                SystemFont.Release(sources);
#else
                instance?.Dispose();
#endif
                System.Threading.Volatile.Write(ref instance, null);
            }
        }

        #region System-emoji acquisition

        private static EmojiFont CreateSystemEmojiFont()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return CreateBrowserBased(DefaultSize);
#elif UNITY_IOS && !UNITY_EDITOR
            using var backend = CoreTextEmojiFontBackend.Open();
            var iosFont = new EmojiFont();
            try
            {
                iosFont.LoadFromBackend(backend, DefaultSize, backend.Identity);
                return iosFont;
            }
            catch
            {
                iosFont.Dispose();
                throw;
            }
#elif UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            if (!NativeFontReader.TryResolveSystemEmojiFont(out var macSource,
                    out var macFaceIndex, out var macPostScriptName))
                return null;
            var macFont = new EmojiFont();
            if (!macFont.LoadFromSource(macSource, macFaceIndex, DefaultSize, macPostScriptName)
                || !macFont.HasColorFormat
                || Shaper.GetGlyphIndex(macFont, UnicodeData.GrinningFaceEmoji) == 0)
            {
                macFont.Dispose();
                return null;
            }
            return macFont;
#elif UNITY_ANDROID && !UNITY_EDITOR
            Span<int> defaultEmoji = stackalloc int[1];
            defaultEmoji[0] = UnicodeData.GrinningFaceEmoji;
            var source = ResolveAndroidSource(defaultEmoji);
            if (source == null) return null;
            var androidFont = new EmojiFont();
            var faceIndex = Math.Max(0, source.FaceInfo.faceIndex);
            if (!androidFont.LoadFromSource(source.Source, faceIndex, DefaultSize, source.Name)
                || !androidFont.HasColorFormat
                || Shaper.GetGlyphIndex(androidFont, UnicodeData.GrinningFaceEmoji) == 0)
            {
                androidFont.Dispose();
                return null;
            }
            RegisterAndroidPrimary(androidFont, source);
            return androidFont;
#else
            if (!SystemEmojiFont.TryGetDefaultEmojiFont(out var path, out var faceIndex)) return null;
            var font = new EmojiFont();
            if (!font.LoadFromSystemPath(path, faceIndex, DefaultSize)) return null;
            return font;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void RegisterAndroidPrimary(EmojiFont font, UniTextFont.Core source)
        {
            SystemFont.Acquire(source);
            lock (platformFacesLock)
                platformFaces[source] = font;
        }

        private static UniTextFont.Core ResolveAndroidSource(ReadOnlySpan<int> cluster)
            => SystemFont.TryResolveSequence(cluster, "und-Zsye", 400, false, null, "sans-serif");

        private static UniTextFont.Core ResolveAndroidCluster(EmojiFont primary,
            ReadOnlySpan<int> cluster)
        {
            var source = ResolveAndroidSource(cluster);
            if (source == null) return primary;

            lock (platformFacesLock)
            {
                if (platformFaces.TryGetValue(source, out var cached)) return cached;
                var font = PlatformEmojiFace.Create(source);
                if (font == null)
                {
                    SystemFont.Acquire(source);
                    platformFaces.Add(source, primary);
                    return primary;
                }
                SystemFont.Acquire(source);
                platformFaces.Add(source, font);
                CatZones.emoji.Meow($"[EmojiFont] Loaded Android fallback: {source.Name}#{source.FaceInfo.faceIndex}");
                return font;
            }
        }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>Creates a WebGL emoji runtime that renders via the browser's Canvas 2D API. Returns null if the browser cannot provide emoji glyphs.</summary>
        private static EmojiFont CreateBrowserBased(int pixelSize = DefaultSize)
        {
            if (!WebGLEmoji.IsSupported)
            {
                CatZones.emoji.MeowWarn("[EmojiFont] Browser emoji rendering not supported");
                return null;
            }
            var font = new EmojiFont();
            font.Name = "EmojiFont (Browser)";
            ConfigureFont(font, 2048, pixelSize, null);
            GlyphAtlas.CreateColorInstance(font.colorPixelSize);
            CatZones.emoji.Meow($"[EmojiFont] Created browser-based emoji font, size={pixelSize}");
            return font;
        }
#endif

        #endregion

        #region Per-platform system-emoji rendering

#if UNITY_WEBGL && !UNITY_EDITOR
        internal override object RenderPreparedBatch(PreparedBatch batch)
            => RenderPreparedBatchWebGL(batch);

        private List<uint> webGLBatchHashesBuffer;

        private unsafe RenderedGlyphData[] RenderPreparedBatchWebGL(PreparedBatch batch)
        {
            var glyphs = batch.filteredGlyphs;
            if (glyphs.count == 0) return null;

            webGLBatchHashesBuffer ??= new List<uint>(256);
            webGLBatchHashesBuffer.Clear();
            for (int i = 0; i < glyphs.count; i++)
                webGLBatchHashesBuffer.Add(glyphs[i]);

            if (!WebGLEmoji.TryRenderEmojiBatch(
                    webGLBatchHashesBuffer, colorPixelSize, out var batchResult))
            {
                CatZones.emoji.MeowWarn($"[EmojiFont] WebGL batch render failed for {glyphs.count} glyphs");
                return null;
            }

            RenderedGlyphData[] rendered = null;
            try
            {
                rendered = new RenderedGlyphData[glyphs.count];
                int count = Math.Min(batchResult.count, glyphs.count);
                for (int i = 0; i < count; i++)
                {
                    int targetIndex = WebGLEmoji.GetBatchOriginalIndex(i);
                    if ((uint)targetIndex >= (uint)rendered.Length)
                        throw new InvalidOperationException("Browser emoji batch returned an invalid source index.");
                    WebGLEmoji.GetBatchMetrics(i, out int w, out int h, out int bearingX, out int bearingY, out float advanceX);
                    if (w < 0 || h < 0 || w > GlyphAtlas.Color.MaxContentSize
                                      || h > GlyphAtlas.Color.MaxContentSize)
                        throw new InvalidOperationException("Browser emoji batch returned invalid glyph dimensions.");

                    rendered[targetIndex].width = w;
                    rendered[targetIndex].height = h;
                    rendered[targetIndex].bearingX = bearingX;
                    rendered[targetIndex].bearingY = bearingY;
                    rendered[targetIndex].advanceX = advanceX > 0 ? advanceX : w;
                    rendered[targetIndex].isBGRA = false;

                    if (w == 0 || h == 0) continue;

                    int pixelOffset = WebGLEmoji.GetBatchPixelOffset(i);
                    int pixelBytes = checked(w * h * 4);
                    if (batchResult.pixelsPtr == IntPtr.Zero || pixelOffset < 0
                        || pixelOffset > batchResult.totalPixelSize - pixelBytes)
                        throw new InvalidOperationException("Browser emoji batch returned an invalid pixel range.");
                    var pixels = ArrayPool<byte>.Rent(pixelBytes);
                    rendered[targetIndex].rgbaPixels = pixels;
                    byte* srcBase = (byte*)batchResult.pixelsPtr + pixelOffset;
                    fixed (byte* dst = &pixels[0])
                    {
                        Buffer.MemoryCopy(srcBase, dst, pixelBytes, pixelBytes);
                    }
                }
                return rendered;
            }
            catch
            {
                ReleaseRenderedBatch(rendered);
                throw;
            }
            finally
            {
                WebGLEmoji.FreeBatchData();
            }
        }
#endif

        #endregion
    }
}
