using System;
using System.Collections.Generic;
using UnityEngine;
namespace LightSide
{
    /// <summary>
    /// Runtime for a color font (CBDT/sbix bitmap or COLRv0/COLRv1 vector). Plain
    /// <see cref="UniTextFont.Core"/> subclass — not a ScriptableObject; constructable on worker threads.
    /// Owns the color-glyph rasterization pipeline: color-format detection, FreeType/Blend2D rendering into
    /// the shared color atlas, and color glyph metrics. The system emoji font (<see cref="EmojiFont"/>) and
    /// the <see cref="UniTextColorFont"/> asset are both built on this base. A glyph an effect decorates also
    /// gets a silhouette field in the SDF atlas, derived from the same bitmap's alpha when it is packed.
    /// </summary>
    public class ColorFontCore : UniTextFont.Core
    {
        /// <summary>Default color-glyph pixel size — 128 on desktop/mobile, 64 on WebGL.</summary>
        public const int DefaultSize =
#if !UNITY_WEBGL || UNITY_EDITOR
            128
#else
            64
#endif
        ;

        protected int colorPixelSize = DefaultSize;
        private int upem = 2048;
#pragma warning disable CS0414
        private bool fontLoaded;
#pragma warning restore CS0414
        private int loadedFaceIndex;

        private bool isSbix;
        private bool isBitmapColor;
        internal bool HasColorFormat { get; private set; }

#if !UNITY_WEBGL || UNITY_EDITOR
        private bool canRenderCOLRv1;
        private ColorGlyphRendererPool rendererPool;
#endif

        #region Atlas Fields

        /// <summary>Color atlas page side in pixels (square).</summary>
        public int AtlasSize => GlyphAtlas.PageSize;
        public override int AtlasPadding => 1;
        public override bool IsColor => true;

        /// <summary>Whether this font can rasterize its color glyphs on the current platform. False for color-font assets on WebGL (no Blend2D); the system emoji font is always true.</summary>
        internal virtual bool CanRasterizeColor =>
#if UNITY_WEBGL && !UNITY_EDITOR
            false;
#else
            true;
#endif

        /// <summary>Whether color glyphs rasterize through the engine's FreeType/Blend2D pools (vs a native path). The system emoji font renders natively on iOS (CoreText) and WebGL (browser) and overrides this to false there.</summary>
        private protected virtual bool RasterizesViaPools => true;

        #endregion

        /// <summary>Pixel size at which color glyphs are rasterised.</summary>
        public int ColorPixelSize => colorPixelSize;

        #region RenderedGlyphData

        /// <summary>One rendered colour bitmap in the batch pipeline's common form: premultiplied pixels in RGBA or BGRA order, rows top-down, pooled until the atlas takes them.</summary>
        protected struct RenderedGlyphData
        {
            public int width;
            public int height;
            public float bearingX;
            public float bearingY;
            public float advanceX;
            public byte[] rgbaPixels;
            public bool isBGRA;
        }

        #endregion

        #region Construction

        internal ColorFontCore() : base() { }

        /// <summary>Diagnostic name prefix for this runtime's <see cref="UniTextFont.Core.Name"/>. Lets the system emoji font keep its own label.</summary>
        protected virtual string DiagnosticNamePrefix => "ColorFont";

        #endregion

        #region Factory Methods

        /// <summary>Creates a color-font runtime from a font file path (.ttf, .ttc, .otf).</summary>
        public static ColorFontCore CreateFromPath(string fontPath, int faceIndex = 0, int pixelSize = DefaultSize)
        {
            var font = new ColorFontCore();
            return font.LoadFromPath(fontPath, faceIndex, pixelSize) ? font : null;
        }

        /// <summary>Maps a font file and loads it into this runtime. Returns false if the file cannot be opened or decoded. Shared by the system emoji font and the color-font factory.</summary>
        protected bool LoadFromPath(string fontPath, int faceIndex = 0, int pixelSize = DefaultSize)
            => LoadFromPath(fontPath, faceIndex, pixelSize, false);

        internal bool LoadFromSystemPath(string fontPath, int faceIndex = 0,
            int pixelSize = DefaultSize)
            => LoadFromPath(fontPath, faceIndex, pixelSize, true);

        private bool LoadFromPath(string fontPath, int faceIndex, int pixelSize,
            bool stableSystemFile)
        {
            if (string.IsNullOrEmpty(fontPath))
                return false;
            FontSource source;
            try
            {
                source = stableSystemFile
                    ? FileFontSource.OpenFile(fontPath)
                    : FontFileCache.OpenSnapshot(fontPath);
            }
            catch (Exception ex)
            {
                CatZones.emoji.MeowError($"[ColorFont] Failed to open font file '{fontPath}': {ex.Message}");
                return false;
            }
            if (!LoadFromSource(source, faceIndex, pixelSize, fontPath))
                return false;
            CatZones.emoji.MeowFormat("[ColorFont] Loaded from: {0}", fontPath);
            return true;
        }

        /// <summary>Creates a color-font runtime from raw font file bytes. Detects the colour format (sbix, CBDT, COLRv0/v1) and picks the closest bitmap strike to <paramref name="pixelSize"/>.</summary>
        public static ColorFontCore CreateFromData(byte[] fontData, int faceIndex = 0, int pixelSize = DefaultSize, string sourceName = null)
        {
            var font = new ColorFontCore();
            return font.LoadFromData(fontData, faceIndex, pixelSize, sourceName) ? font : null;
        }

        /// <summary>Loads and configures this runtime from raw font bytes: detects the colour format (sbix, CBDT, COLRv0/v1), picks the closest bitmap strike to <paramref name="pixelSize"/>, builds face metrics, and creates the shared color atlas. Returns false when the bytes cannot be loaded. Shared by the system emoji font and the color-font asset.</summary>
        protected bool LoadFromData(byte[] fontData, int faceIndex, int pixelSize, string sourceName = null)
            => fontData != null && fontData.Length != 0
               && LoadFromSource(new ArrayFontSource(fontData), faceIndex, pixelSize, sourceName);

        internal bool LoadFromSource(FontSource fontSource, int faceIndex, int pixelSize,
            string sourceName = null)
        {
            if (fontSource == null) return false;

            if (!FreeType.Initialize())
            {
                CatZones.emoji.MeowError("[ColorFont] Failed to load font from data");
                return false;
            }

            using var metadataFace = FreeTypeFace.TryCreate(fontSource, faceIndex);
            if (metadataFace == null)
            {
                CatZones.emoji.MeowError("[ColorFont] Failed to load font from data");
                return false;
            }

            var face = metadataFace.Pointer;
            SetFontSource(fontSource);

            var ftInfo = FreeType.GetFaceInfo(face);
            HasColorFormat = ftInfo.hasColor;
            Name = $"{DiagnosticNamePrefix} ({sourceName ?? ftInfo.familyName ?? "Data"})";
            fontLoaded = true;
            loadedFaceIndex = faceIndex;

            int fontUpem = Shaper.GetUpem(this);
            if (fontUpem <= 0)
                fontUpem = ftInfo.unitsPerEm > 0 ? ftInfo.unitsPerEm : 2048;
            int[] availableSizes = ftInfo.hasFixedSizes ? ftInfo.availableSizes : null;

            var rawFtInfo = FT.GetFaceInfo(face);
            ConfigureFont(this, fontUpem, pixelSize, availableSizes, rawFtInfo.ascender, rawFtInfo.descender);
            isSbix = ftInfo.hasSbix;
            isBitmapColor = ftInfo.hasFixedSizes && !ftInfo.hasSbix;

#if !UNITY_WEBGL || UNITY_EDITOR
            canRenderCOLRv1 = BL.IsSupported && ftInfo.hasColor && ftInfo.isScalable
                && FT.HasCOLRTable(face);
#endif

            GlyphAtlas.CreateColorInstance(colorPixelSize);
            return true;
        }

        /// <summary>Initializes a native-backed color runtime and retains the supplied face independently of its caller.</summary>
        internal void LoadFromBackend(IFontFaceBackend backend, int pixelSize,
            string sourceName = null)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            if (backend is not IColorGlyphBackend)
                throw new ArgumentException("A color font backend must rasterize its glyphs.", nameof(backend));

            SetFontBackend(backend);
            var info = backend.FaceInfo;
            info.unitsPerEm = backend.UnitsPerEm;
            upem = backend.UnitsPerEm;
            UnitsPerEm = upem;
            FaceInfo = info;
            colorPixelSize = pixelSize > 0 ? pixelSize : DefaultSize;
            HasColorFormat = true;
            Name = $"{DiagnosticNamePrefix} ({sourceName ?? info.familyName ?? backend.Identity})";
            fontLoaded = true;
            loadedFaceIndex = Math.Max(0, info.faceIndex);
            ReadFontDefinition();
            GlyphAtlas.CreateColorInstance(colorPixelSize);
        }

        protected static void ConfigureFont(ColorFontCore font, int fontUpem, int pixelSize, int[] availableSizes,
            short fontAscender = 0, short fontDescender = 0)
        {
            font.upem = fontUpem;
            font.UnitsPerEm = fontUpem;

            if (availableSizes != null && availableSizes.Length > 0)
            {
                int bestSize = pixelSize;
                int bestDiff = int.MaxValue;
                foreach (var size in availableSizes)
                {
                    int diff = Math.Abs(size - pixelSize);
                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestSize = size;
                    }
                }
                font.colorPixelSize = bestSize;
            }
            else
            {
                font.colorPixelSize = pixelSize;
            }

            float ascent = fontAscender > 0 ? fontAscender : fontUpem * 0.8f;
            float descent = fontDescender < 0 ? fontDescender : -fontUpem * 0.2f;

            font.FaceInfo = new FaceInfo
            {
                unitsPerEm = fontUpem,
                lineHeight = Mathf.RoundToInt(ascent - descent),
                ascentLine = Mathf.RoundToInt(ascent),
                descentLine = Mathf.RoundToInt(descent)
            };

            font.ReadFontDefinition();
        }

        #endregion

        #region Font Data

        /// <summary>Process-unique runtime identity used by the shared color atlas and shaper registries.</summary>
        public override int FontDataHash => base.FontDataHash;

        private int GetFontAdvance(uint glyphIndex)
        {
            if (!HasFontBackend) return -1;
            return Shaper.GetGlyphAdvance(this, glyphIndex);
        }

        #endregion

        #region Glyph Rendering

        public override UniTextFontError LoadFontFace()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return UniTextFontError.Success;
#else
            return fontLoaded ? UniTextFontError.Success : UniTextFontError.InvalidFile;
#endif
        }

        /// <summary>Returns whether the shared color atlas contains the glyph.</summary>
        public override bool HasGlyphInAtlas(uint glyphIndex, UniTextRenderMode mode) =>
            GlyphAtlas.Color != null
            && GlyphAtlas.Color.TryGetEntry(DefaultVarHash48, glyphIndex, out _);

        /// <summary>
        /// Filters the request down to the glyphs that must render: those without a colour tile, and those
        /// whose requested silhouette field the SDF atlas lacks or holds at too tight a pad tier — a field
        /// is derived from a fresh render of the same bitmap. Tiles and fields already sufficient are
        /// pinned for the batch instead.
        /// </summary>
        internal override PreparedBatch? PrepareGlyphBatch(List<uint> glyphIndices, UniTextRenderMode mode,
            long varHash48 = 0, int[] ftCoords = null, FastIntDictionary<byte> fieldRequests = null)
        {
            if (glyphIndices == null || glyphIndices.Count == 0)
                return null;

            var atlas = GlyphAtlas.Color;
            var varHash = DefaultVarHash48;
            var hasFieldRequests = fieldRequests != null && fieldRequests.Count > 0;
            var fieldAtlas = hasFieldRequests ? GlyphAtlas.GetInstance(UniTextRenderMode.SDF) : null;
            var fieldVarHash = hasFieldRequests ? GlyphAtlas.FieldVarHash48(FontDataHash) : 0;

            var filtered = new PooledBuffer<uint>();
            filtered.EnsureCapacity(glyphIndices.Count);
            var extents = new PooledBuffer<byte>();
            extents.EnsureCapacity(glyphIndices.Count);

            for (int i = 0; i < glyphIndices.Count; i++)
            {
                var glyphIndex = glyphIndices[i];
                byte extent = 0;
                var needsField = hasFieldRequests
                                 && fieldRequests.TryGetValue((int)glyphIndex, out extent)
                                 && extent != 0
                                 && !HasSufficientField(fieldAtlas, fieldVarHash, glyphIndex, extent);
                bool isNew = glyphLookupDictionary == null || !glyphLookupDictionary.ContainsKey(GlyphKey(glyphIndex));

                if (!isNew && atlas.TryGetEntry(varHash, glyphIndex, out var existingEntry))
                {
                    if (existingEntry.refCount == 0)
                        atlas.ProtectForBatch(GlyphAtlas.MakeKey(varHash, glyphIndex));
                    if (!needsField) continue;
                }

                filtered.Add(glyphIndex);
                extents.Add(needsField ? extent : (byte)0);
            }

            if (filtered.count == 0)
            {
                filtered.Return();
                extents.Return();
                return null;
            }

            try
            {
                EnsureRendererPool();
            }
            catch
            {
                filtered.Return();
                extents.Return();
                throw;
            }

            return new PreparedBatch
            {
                filteredGlyphs = filtered,
                fieldExtents = extents,
                varHash48 = varHash
            };
        }

        /// <summary>Whether this font's rendered bitmaps become colour-atlas tiles; a font drawn from its own texture renders bitmaps only to derive silhouette fields.</summary>
        private protected virtual bool KeepsColorTile => true;

        /// <summary>Whether the SDF atlas already holds this glyph's silhouette field at the tier the request needs, pinning it for the batch when it does.</summary>
        private protected bool HasSufficientField(GlyphAtlas fieldAtlas, long fieldVarHash, uint glyphIndex, byte extent)
        {
            var key = GlyphAtlas.MakeKey(fieldVarHash, glyphIndex);
            if (!fieldAtlas.TryGetEntry(key, out var entry) || entry.encodedTile < 0) return false;
            if (glyphLookupDictionary == null
                || !glyphLookupDictionary.TryGetValue(GlyphKey(glyphIndex), out var glyph)) return false;
            var glyphH = glyph.metrics.height / (float)UnitsPerEm;
            if (glyphH < 1e-6f) return false;
            if (entry.padTier < ColorGlyphField.TierFor(extent, glyphH)) return false;
            if (entry.refCount == 0) fieldAtlas.ProtectForBatch(key);
            return true;
        }

        private void EnsureRendererPool()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            if (RasterizesViaPools && fontLoaded && HasFontData)
                rendererPool ??= new ColorGlyphRendererPool(Source, loadedFaceIndex,
                    colorPixelSize, canRenderCOLRv1);
#endif
        }

        internal override object RenderPreparedBatch(PreparedBatch batch)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            var backend = FontBackend;
            if (backend != null)
            {
                if (backend is not IColorGlyphBackend renderer)
                    throw new InvalidOperationException("The font backend cannot rasterize color glyphs.");
                return RenderPreparedBatchBackend(batch.filteredGlyphs, renderer);
            }
            if (!fontLoaded || !HasFontData)
                return RenderPreparedBatchFreeTypeSequential(batch.filteredGlyphs);
            int renderSize = GlyphAtlas.Color.MaxContentSize;
            return rendererPool.RenderGlyphsBatch(batch.filteredGlyphs, renderSize,
                !GlyphAtlas.forceSingleThreaded);
#else
            return null;
#endif
        }

        /// <summary>
        /// Takes glyph <paramref name="i"/> out of a rendered batch of any backend form, transferring
        /// ownership of its pooled pixels to the caller. COLRv1 renders report the scale their bitmap was
        /// produced at, which their metrics undo.
        /// </summary>
        private static bool TryTakeRendered(object renderedObj, int i, out RenderedGlyphData data,
            out bool renderedByCOLRv1, out float renderScale)
        {
            data = default;
            renderedByCOLRv1 = false;
            renderScale = 1f;
            switch (renderedObj)
            {
                case RenderedGlyphData[] plain:
                {
                    if (i >= plain.Length) return false;
                    data = plain[i];
                    plain[i].rgbaPixels = null;
                    break;
                }
#if !UNITY_WEBGL || UNITY_EDITOR
                case ColorGlyphRendererPool.RenderedGlyph[] pool:
                {
                    if (i >= pool.Length) return false;
                    ref var rendered = ref pool[i];
                    if (!rendered.isValid) return false;
                    data = new RenderedGlyphData
                    {
                        width = rendered.width,
                        height = rendered.height,
                        bearingX = rendered.bearingX,
                        bearingY = rendered.bearingY,
                        advanceX = rendered.advanceX,
                        rgbaPixels = rendered.rgbaPixels,
                        isBGRA = false
                    };
                    renderedByCOLRv1 = rendered.renderedByCOLRv1;
                    renderScale = rendered.renderScale;
                    rendered.rgbaPixels = null;
                    break;
                }
                case FreeType.RenderedGlyph[] freeType:
                {
                    if (i >= freeType.Length) return false;
                    ref var rendered = ref freeType[i];
                    if (!rendered.isValid) return false;
                    data = new RenderedGlyphData
                    {
                        width = rendered.width,
                        height = rendered.height,
                        bearingX = rendered.bearingX,
                        bearingY = rendered.bearingY,
                        advanceX = rendered.advanceX,
                        rgbaPixels = rendered.rgbaPixels,
                        isBGRA = rendered.isBGRA
                    };
                    rendered.rgbaPixels = null;
                    break;
                }
#endif
                default:
                    return false;
            }

            if (data.width > 0 && data.height > 0 && data.rgbaPixels != null) return true;
            if (data.rgbaPixels != null) ArrayPool<byte>.Return(data.rgbaPixels);
            data.rgbaPixels = null;
            return false;
        }

        /// <summary>The metrics input a render maps to: a downscaled COLRv1 bitmap reports its design size, not its pixel size.</summary>
        private static RenderedGlyphData MetricsInput(in RenderedGlyphData data, bool renderedByCOLRv1, float renderScale)
        {
            var input = data;
            if (renderedByCOLRv1 && renderScale < 1f)
            {
                input.width = (int)Math.Ceiling(data.width / renderScale);
                input.height = (int)Math.Ceiling(data.height / renderScale);
            }
            var advance = data.advanceX > 0 ? data.advanceX : data.width;
            input.advanceX = renderedByCOLRv1 ? advance / renderScale : advance;
            return input;
        }

        internal override int PackRenderedBatch(object renderedObj, PreparedBatch batch, UniTextRenderMode mode)
        {
            var atlas = GlyphAtlas.Color;
            bool mutationStarted = false;
            try
            {
                if (renderedObj == null) return 0;

                var toRender = batch.filteredGlyphs;
                var extents = batch.fieldExtents;
                long varHash = DefaultVarHash48;
                int totalAdded = 0;
                int renderFail = 0, atlasReject = 0;

                for (int i = 0; i < toRender.count; i++)
                {
                    if (!TryTakeRendered(renderedObj, i, out var data, out var renderedByCOLRv1, out var renderScale))
                    {
                        renderFail++;
                        continue;
                    }

                    var glyphId = toRender[i];
                    var metrics = ComputeGlyphMetrics(glyphId, MetricsInput(in data, renderedByCOLRv1, renderScale),
                        renderedByCOLRv1);
                    if (!renderedByCOLRv1 && KeepsColorTile)
                        FitBitmapToAtlas(ref data.rgbaPixels, ref data.width, ref data.height, atlas.MaxContentSize);

                    mutationStarted = true;
                    var extent = extents.data != null && i < extents.count ? extents.data[i] : (byte)0;
                    if (extent != 0)
                        EnsureField(glyphId, in data, in metrics, extent);

                    if (!KeepsColorTile)
                    {
                        totalAdded++;
                        continue;
                    }

                    var pixels = data.rgbaPixels;
                    var entry = atlas.EnsureColorGlyph(varHash, glyphId, FontDataHash,
                        pixels, data.width, data.height, data.isBGRA, metrics);
                    if (entry.encodedTile < 0) { atlasReject++; continue; }

                    RegisterGlyphFromAtlas(glyphId, entry);
                    totalAdded++;
                }

                if (renderFail + atlasReject > 0)
                    CatZones.emoji.MeowWarn($"[EmojiDiag] pack {Name}: ok={totalAdded}, renderFail={renderFail}, atlasReject={atlasReject}, requested={toRender.count}");

                return totalAdded;
            }
            catch (Exception failure)
            {
                if (mutationStarted)
                    atlas?.RecoverAfterFailedMutation(failure);
                throw;
            }
        }

        internal override void ReleaseRenderedBatch(object renderedObj)
        {
            switch (renderedObj)
            {
                case RenderedGlyphData[] plain:
                    for (int i = 0; i < plain.Length; i++)
                    {
                        if (plain[i].rgbaPixels == null) continue;
                        ArrayPool<byte>.Return(plain[i].rgbaPixels);
                        plain[i].rgbaPixels = null;
                    }
                    break;
#if !UNITY_WEBGL || UNITY_EDITOR
                case ColorGlyphRendererPool.RenderedGlyph[] colorRendered:
                    for (int i = 0; i < colorRendered.Length; i++)
                    {
                        if (colorRendered[i].rgbaPixels == null) continue;
                        ArrayPool<byte>.Return(colorRendered[i].rgbaPixels);
                        colorRendered[i].rgbaPixels = null;
                    }
                    break;
                case FreeType.RenderedGlyph[] ftRendered:
                    for (int i = 0; i < ftRendered.Length; i++)
                    {
                        if (ftRendered[i].rgbaPixels == null) continue;
                        ArrayPool<byte>.Return(ftRendered[i].rgbaPixels);
                        ftRendered[i].rgbaPixels = null;
                    }
                    break;
#endif
            }
        }

        #endregion

        #region Silhouette fields

        /// <summary>Queues the glyph's silhouette field from the bitmap about to become its colour tile, at the pad tier the request needs.</summary>
        private void EnsureField(uint glyphId, in RenderedGlyphData data, in GlyphMetrics metrics, byte extent)
        {
            if (!(metrics.width > 0f) || !(metrics.height > 0f)) return;
            var glyphH = metrics.height / (float)UnitsPerEm;
            var aspect = metrics.width / metrics.height;
            var alpha = ExtractAlpha(data.rgbaPixels, data.width, data.height);
            try
            {
                GlyphAtlas.GetInstance(UniTextRenderMode.SDF).EnsureFieldGlyph(
                    GlyphAtlas.FieldVarHash48(FontDataHash), glyphId, FontDataHash,
                    alpha.AsSpan(0, data.width * data.height), data.width, data.height,
                    glyphH, aspect, ColorGlyphField.TierFor(extent, glyphH), in metrics);
            }
            finally
            {
                ArrayPool<byte>.Return(alpha);
            }
        }

        /// <summary>The alpha plane of a 4-byte-per-pixel bitmap, in a pooled array the caller returns.</summary>
        private static byte[] ExtractAlpha(byte[] pixels, int width, int height)
        {
            var count = width * height;
            var alpha = ArrayPool<byte>.Rent(count);
            for (int i = 0, p = 3; i < count; i++, p += 4)
                alpha[i] = pixels[p];
            return alpha;
        }

        /// <summary>
        /// Renders one glyph again and hands back its fitted alpha plane with the field placement
        /// values the atlas needs — the source of every silhouette re-rasterization, as the curve cache
        /// is for outline glyphs.
        /// </summary>
        private bool TryRenderSilhouette(uint glyphIndex, out byte[] alpha, out int width, out int height,
            out float glyphH, out float aspect)
        {
            alpha = null;
            width = height = 0;
            glyphH = aspect = 0f;

            EnsureRendererPool();
            var one = new PooledBuffer<uint>();
            one.EnsureCapacity(1);
            one.Add(glyphIndex);
            var batch = new PreparedBatch { filteredGlyphs = one, varHash48 = DefaultVarHash48 };
            object rendered = null;
            try
            {
                rendered = RenderPreparedBatch(batch);
                if (rendered == null
                    || !TryTakeRendered(rendered, 0, out var data, out var renderedByCOLRv1, out var renderScale))
                    return false;
                try
                {
                    var metrics = ComputeGlyphMetrics(glyphIndex, MetricsInput(in data, renderedByCOLRv1, renderScale),
                        renderedByCOLRv1);
                    if (!renderedByCOLRv1 && KeepsColorTile)
                        FitBitmapToAtlas(ref data.rgbaPixels, ref data.width, ref data.height,
                            GlyphAtlas.Color.MaxContentSize);
                    if (!(metrics.width > 0f) || !(metrics.height > 0f)) return false;
                    alpha = ExtractAlpha(data.rgbaPixels, data.width, data.height);
                    width = data.width;
                    height = data.height;
                    glyphH = metrics.height / (float)UnitsPerEm;
                    aspect = metrics.width / metrics.height;
                    return true;
                }
                finally
                {
                    ArrayPool<byte>.Return(data.rgbaPixels);
                }
            }
            finally
            {
                if (rendered != null) ReleaseRenderedBatch(rendered);
                one.Return();
            }
        }

        internal override void ReExtractForTierUpgrade(uint glyphIndex, long varHash48, int[] ftCoords,
            UniTextRenderMode mode, byte requiredTier)
        {
            if (!TryRenderSilhouette(glyphIndex, out var alpha, out var width, out var height,
                    out var glyphH, out var aspect)) return;
            var atlas = GlyphAtlas.GetInstance(mode);
            bool mutationStarted = false;
            try
            {
                mutationStarted = true;
                atlas.UpgradeFieldTier(GlyphAtlas.MakeKey(varHash48, glyphIndex),
                    alpha.AsSpan(0, width * height), width, height, glyphH, aspect, requiredTier);
            }
            catch (Exception failure)
            {
                if (mutationStarted) atlas.RecoverAfterFailedMutation(failure);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Return(alpha);
            }
        }

        internal override bool ReExtractForTileSizeUpgrade(uint glyphIndex, long varHash48, int[] ftCoords,
            UniTextRenderMode mode, int tileSizeBoost)
        {
            if (!TryRenderSilhouette(glyphIndex, out var alpha, out var width, out var height,
                    out var glyphH, out var aspect)) return false;
            var atlas = GlyphAtlas.GetInstance(mode);
            bool mutationStarted = false;
            try
            {
                int target = GlyphAtlas.OffsetTileSize(GlyphAtlas.ClassifyFieldTileSize(width, height), tileSizeBoost);
                mutationStarted = true;
                return atlas.UpgradeFieldTileSize(GlyphAtlas.MakeKey(varHash48, glyphIndex),
                    alpha.AsSpan(0, width * height), width, height, glyphH, aspect, target);
            }
            catch (Exception failure)
            {
                if (mutationStarted) atlas.RecoverAfterFailedMutation(failure);
                throw;
            }
            finally
            {
                ArrayPool<byte>.Return(alpha);
            }
        }

        #endregion

#if !UNITY_WEBGL || UNITY_EDITOR

        private object RenderPreparedBatchBackend(PooledBuffer<uint> glyphs,
            IColorGlyphBackend renderer)
        {
            var rendered = new FreeType.RenderedGlyph[glyphs.count];
            try
            {
                if (GlyphAtlas.forceSingleThreaded || glyphs.count < 16)
                {
                    for (var i = 0; i < glyphs.count; i++)
                        if (renderer.TryRenderGlyph(glyphs[i], colorPixelSize, out var result))
                            rendered[i] = result;
                }
                else
                {
                    System.Threading.Tasks.Parallel.For(0, glyphs.count,
                        new System.Threading.Tasks.ParallelOptions
                            { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        i =>
                        {
                            if (renderer.TryRenderGlyph(glyphs[i], colorPixelSize, out var result))
                                rendered[i] = result;
                        });
                }
                return rendered;
            }
            catch
            {
                ReleaseRenderedBatch(rendered);
                throw;
            }
        }

        private object RenderPreparedBatchFreeTypeSequential(PooledBuffer<uint> glyphs)
        {
            var results = new FreeType.RenderedGlyph[glyphs.count];
            if (!fontLoaded) return results;
            EnsureFreeTypeFontLoaded();
            try
            {
                for (int i = 0; i < glyphs.count; i++)
                {
                    if (!FreeType.TryRenderGlyph(glyphs[i], colorPixelSize,
                            out var rendered, out var failReason))
                    {
                        CatZones.emoji.MeowWarn(
                            $"[ColorFont] Render failed glyph {glyphs[i]}: {failReason}");
                        continue;
                    }
                    int pixelBytes = checked(rendered.width * rendered.height * 4);
                    var pooledPixels = ArrayPool<byte>.Rent(pixelBytes);
                    bool transferred = false;
                    try
                    {
                        Buffer.BlockCopy(rendered.rgbaPixels, 0, pooledPixels, 0, pixelBytes);
                        results[i] = new FreeType.RenderedGlyph
                        {
                            isValid = true,
                            width = rendered.width,
                            height = rendered.height,
                            bearingX = rendered.bearingX,
                            bearingY = rendered.bearingY,
                            advanceX = rendered.advanceX,
                            rgbaPixels = pooledPixels,
                            isBGRA = rendered.isBGRA
                        };
                        transferred = true;
                    }
                    finally
                    {
                        if (!transferred)
                            ArrayPool<byte>.Return(pooledPixels);
                    }
                }
                return results;
            }
            catch
            {
                ReleaseRenderedBatch(results);
                throw;
            }
        }
#endif

        /// <summary>Downscales a bitmap larger than the atlas content size in place (pooled buffers swapped), preserving its aspect.</summary>
        protected static void FitBitmapToAtlas(ref byte[] pixels, ref int width, ref int height,
            int maxSize)
        {
            int sourceWidth = width;
            int sourceHeight = height;
            if (sourceWidth <= maxSize && sourceHeight <= maxSize) return;

            int targetWidth;
            int targetHeight;
            if (sourceWidth >= sourceHeight)
            {
                targetWidth = maxSize;
                targetHeight = Math.Max(1,
                    (int)(((long)sourceHeight * maxSize + sourceWidth / 2) / sourceWidth));
            }
            else
            {
                targetHeight = maxSize;
                targetWidth = Math.Max(1,
                    (int)(((long)sourceWidth * maxSize + sourceHeight / 2) / sourceHeight));
            }

            int targetBytes = checked(targetWidth * targetHeight * 4);
            var target = ArrayPool<byte>.Rent(targetBytes);
            bool transferred = false;
            try
            {
                ResizeBitmap(pixels, sourceWidth, sourceHeight,
                    target, targetWidth, targetHeight);
                var source = pixels;
                pixels = target;
                width = targetWidth;
                height = targetHeight;
                transferred = true;
                ArrayPool<byte>.Return(source);
            }
            finally
            {
                if (!transferred) ArrayPool<byte>.Return(target);
            }
        }

        private static void ResizeBitmap(byte[] source, int sourceWidth, int sourceHeight,
            byte[] target, int targetWidth, int targetHeight)
        {
            for (int y = 0; y < targetHeight; y++)
            {
                long sourceY = (((2L * y + 1) * sourceHeight << 15) / targetHeight) - 32768;
                sourceY = Math.Clamp(sourceY, 0, ((long)sourceHeight - 1) << 16);
                int y0 = (int)(sourceY >> 16);
                int y1 = Math.Min(y0 + 1, sourceHeight - 1);
                int fy = (int)(sourceY & 0xFFFF);
                int wy0 = 65536 - fy;
                int targetRow = y * targetWidth * 4;
                int sourceRow0 = y0 * sourceWidth * 4;
                int sourceRow1 = y1 * sourceWidth * 4;

                for (int x = 0; x < targetWidth; x++)
                {
                    long sourceX = (((2L * x + 1) * sourceWidth << 15) / targetWidth) - 32768;
                    sourceX = Math.Clamp(sourceX, 0, ((long)sourceWidth - 1) << 16);
                    int x0 = (int)(sourceX >> 16);
                    int x1 = Math.Min(x0 + 1, sourceWidth - 1);
                    int fx = (int)(sourceX & 0xFFFF);
                    int wx0 = 65536 - fx;
                    int source0 = sourceRow0 + x0 * 4;
                    int source1 = sourceRow0 + x1 * 4;
                    int source2 = sourceRow1 + x0 * 4;
                    int source3 = sourceRow1 + x1 * 4;
                    int destination = targetRow + x * 4;

                    for (int channel = 0; channel < 4; channel++)
                    {
                        int top = (source[source0 + channel] * wx0
                                   + source[source1 + channel] * fx + 32768) >> 16;
                        int bottom = (source[source2 + channel] * wx0
                                      + source[source3 + channel] * fx + 32768) >> 16;
                        target[destination + channel] = (byte)((top * wy0
                                                               + bottom * fy + 32768) >> 16);
                    }
                }
            }
        }

        /// <summary>Design-unit metrics of a rendered bitmap. The bitmap's pixel-to-design ratio follows the colour format: the COLRv1 render size, the sbix strike, the CBDT advance, or the requested pixel size.</summary>
        protected virtual GlyphMetrics ComputeGlyphMetrics(uint glyphIndex, RenderedGlyphData rendered,
            bool renderedByCOLRv1 = false)
        {
            float pixelsToDesign;
            float bearingYDesign;
            var fi = FaceInfo;

#if !UNITY_WEBGL || UNITY_EDITOR
            int fontAdvance = GetFontAdvance(glyphIndex);
#else
            int fontAdvance = -1;
#endif

            if (renderedByCOLRv1)
            {
                int renderSize = GlyphAtlas.Color.MaxContentSize;
                pixelsToDesign = (float)upem / renderSize;
                bearingYDesign = rendered.bearingY * pixelsToDesign;
            }
            else if (isSbix)
            {
                int actualBitmapSize = Math.Max(rendered.width, rendered.height);
                pixelsToDesign = actualBitmapSize > 0
                    ? (float)upem / actualBitmapSize
                    : (float)upem / colorPixelSize;

                if (rendered.bearingY >= rendered.height)
                {
                    float heightD = rendered.height * pixelsToDesign;
                    float lineExtent = fi.ascentLine - fi.descentLine;
                    bearingYDesign = fi.ascentLine - (lineExtent - heightD) * 0.5f;
                }
                else
                {
                    bearingYDesign = rendered.bearingY * pixelsToDesign;
                }
            }
            else if (isBitmapColor && fontAdvance > 0 && rendered.width > 0)
            {
                pixelsToDesign = (float)fontAdvance / rendered.width;
                bearingYDesign = rendered.bearingY * pixelsToDesign;
            }
            else
            {
                pixelsToDesign = (float)upem / colorPixelSize;
                bearingYDesign = rendered.bearingY * pixelsToDesign;
            }

            float bitmapWidthDesign = rendered.width * pixelsToDesign;
            float bitmapHeightDesign = rendered.height * pixelsToDesign;
            float bearingXDesign = rendered.bearingX * pixelsToDesign;

#if UNITY_WEBGL && !UNITY_EDITOR
            float advanceDesign = rendered.advanceX * ((float)upem / colorPixelSize);
#else
            float advanceDesign = fontAdvance > 0 ? fontAdvance : bitmapWidthDesign;
#endif

            return new GlyphMetrics(bitmapWidthDesign, bitmapHeightDesign, bearingXDesign, bearingYDesign, advanceDesign);
        }

        protected void RegisterGlyphFromAtlas(uint glyphIndex, GlyphAtlas.GlyphEntry entry)
        {
            var atlas = GlyphAtlas.Color;
            atlas.DecodeTileXY(entry.encodedTile, atlas.TileSizeFromEncoded(entry.encodedTile), out int tileX, out int tileY);
            int g = atlas.TileGutter;
            var rect = new GlyphRect(tileX + g, tileY + g, entry.pixelWidth, entry.pixelHeight);
            glyphLookupDictionary ??= new Dictionary<long, Glyph>();
            var key = GlyphKey(glyphIndex);
            if (glyphLookupDictionary.TryGetValue(key, out var glyph))
            {
                glyph.metrics = entry.metrics;
                glyph.glyphRect = rect;
                glyph.atlasIndex = entry.pageIndex;
                return;
            }
            glyph = new Glyph(glyphIndex, entry.metrics, rect, entry.pageIndex);
            glyphTable.Add(glyph);
            glyphLookupDictionary[key] = glyph;
        }

        public override void ClearDynamicData()
        {
            DisposeFacePool();
            base.ClearDynamicData();
        }

        /// <summary>Releases color-renderer face pools together with the base runtime's shaping, FreeType and atlas resources.</summary>
        public override void Dispose()
        {
            DisposeFacePool();
            base.Dispose();
        }

        private void EnsureFreeTypeFontLoaded()
        {
            if (FreeType.IsCurrent(Source, loadedFaceIndex))
                return;
            if (HasFontData)
                FreeType.LoadFontFromSource(Source, loadedFaceIndex);
        }

        /// <summary>Releases the parallel color-renderer pool and the shared sequential FreeType face when it currently represents this font.</summary>
        public void DisposeFacePool()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            rendererPool?.Dispose();
            rendererPool = null;
#endif
            FreeType.UnloadIfCurrent(Source, loadedFaceIndex);
        }
    }

}
