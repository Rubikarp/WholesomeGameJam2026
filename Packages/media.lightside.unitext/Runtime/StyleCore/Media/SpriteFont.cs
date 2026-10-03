using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The process-wide colour font whose glyphs are sprites. Every inline sprite of every component is
    /// a glyph of this runtime, so it takes a glyph's place in the text mesh — the layer stack in order,
    /// world-space text, effects through its silhouette field — while drawing from its own texture at
    /// native resolution through the sub-mesh its component keeps per texture; nothing is copied into
    /// an atlas. A silhouette field is derived from a read-back of the sprite's pixels only when an
    /// effect asks for one. Glyph ids are issued per <see cref="Sprite"/> object and stay stable for the
    /// process. The nominal glyph box is 1 em tall on the baseline, its width the sprite's aspect.
    /// </summary>
    public sealed class SpriteFont : ColorFontCore
    {
        private const int DesignUnits = 1000;

        /// <summary>
        /// A registered sprite: its texture, the region of it the sprite's rect covers, the rect's aspect
        /// and — for a tightly packed sprite — its outline mesh, positions normalized over the rect so the
        /// region's neighbours never show.
        /// </summary>
        internal readonly struct SpriteGlyph
        {
            public readonly Sprite sprite;
            public readonly Texture2D texture;
            public readonly Vector2 uvMin;
            public readonly Vector2 uvMax;
            public readonly float aspect;
            public readonly Vector2[] meshPositions;
            public readonly Vector2[] meshUv;
            public readonly ushort[] meshTriangles;

            public SpriteGlyph(Sprite sprite, Texture2D texture, Vector2 uvMin, Vector2 uvMax, float aspect,
                Vector2[] meshPositions, Vector2[] meshUv, ushort[] meshTriangles)
            {
                this.sprite = sprite;
                this.texture = texture;
                this.uvMin = uvMin;
                this.uvMax = uvMax;
                this.aspect = aspect;
                this.meshPositions = meshPositions;
                this.meshUv = meshUv;
                this.meshTriangles = meshTriangles;
            }
        }

        private static SpriteFont instance;
        private static readonly object instanceLock = new();

        private readonly List<SpriteGlyph> glyphs = new();
        private readonly Dictionary<Sprite, uint> ids = new(ReferenceIdentityComparer<Sprite>.Instance);
        private readonly object glyphLock = new();
        private readonly int identity;
        private RenderedGlyphData[] prepared;

        /// <summary>The singleton, created on first access. Main thread on creation.</summary>
        public static SpriteFont Instance
        {
            get
            {
                var current = System.Threading.Volatile.Read(ref instance);
                if (current != null) return current;
                lock (instanceLock)
                {
                    current = instance;
                    if (current == null)
                    {
                        current = new SpriteFont();
                        System.Threading.Volatile.Write(ref instance, current);
                    }
                    return current;
                }
            }
        }

        /// <summary>The singleton if it exists, without creating it.</summary>
        internal static SpriteFont ExistingInstance => System.Threading.Volatile.Read(ref instance);

#if UNITY_EDITOR
        static SpriteFont() => EditorLifecycle.UnmanagedCleaning += DisposeInstance;
#endif

        private SpriteFont() : base()
        {
            identity = AllocateRuntimeFontId();
            Name = "SpriteFont";
            UnitsPerEm = DesignUnits;
            FaceInfo = new FaceInfo
            {
                unitsPerEm = DesignUnits,
                lineHeight = DesignUnits,
                ascentLine = DesignUnits,
                descentLine = 0
            };
            ParticipatesInNormalization = false;
            ReadFontDefinition();
        }

        private static void DisposeInstance()
        {
            lock (instanceLock)
            {
                instance?.Dispose();
                System.Threading.Volatile.Write(ref instance, null);
            }
        }

        public override int FontDataHash => identity;

        protected override string DiagnosticNamePrefix => "SpriteFont";

        internal override bool CanRasterizeColor => true;

        private protected override bool KeepsColorTile => false;

        public override UniTextFontError LoadFontFace() => UniTextFontError.Success;

        /// <summary>A registered sprite is always drawable: its pixels are its own texture.</summary>
        public override bool HasGlyphInAtlas(uint glyphIndex, UniTextRenderMode mode)
        {
            lock (glyphLock)
                return glyphIndex != 0 && glyphIndex <= (uint)glyphs.Count;
        }

        /// <summary>Glyph id of a sprite, issued on first sight. Main thread — reads the sprite's rect, texture and mesh.</summary>
        public uint Register(Sprite sprite)
        {
            lock (glyphLock)
            {
                if (ids.TryGetValue(sprite, out var id)) return id;
                var glyph = Capture(sprite);
                id = (uint)(glyphs.Count + 1);
                glyphs.Add(glyph);
                ids[sprite] = id;
                var entry = new Glyph(id, Metrics(glyph.aspect), GlyphRect.zero, 0);
                glyphTable.Add(entry);
                glyphLookupDictionary ??= new Dictionary<long, Glyph>();
                glyphLookupDictionary[GlyphKey(id)] = entry;
                return id;
            }
        }

        /// <summary>Glyph id and width-over-height aspect of a registered sprite; false for one never registered. Safe on worker threads.</summary>
        public bool TryGetGlyph(Sprite sprite, out uint glyphId, out float aspect)
        {
            lock (glyphLock)
            {
                if (sprite is not null && ids.TryGetValue(sprite, out glyphId))
                {
                    aspect = glyphs[(int)glyphId - 1].aspect;
                    return true;
                }
            }
            glyphId = 0;
            aspect = 1f;
            return false;
        }

        /// <summary>The registered sprite behind a glyph id; false for an id never issued. Safe on worker threads.</summary>
        internal bool TryGetSpriteGlyph(uint glyphId, out SpriteGlyph glyph)
        {
            lock (glyphLock)
            {
                if (glyphId != 0 && glyphId <= (uint)glyphs.Count)
                {
                    glyph = glyphs[(int)glyphId - 1];
                    return true;
                }
            }
            glyph = default;
            return false;
        }

        internal override bool TryGetColorTexture(uint glyphIndex, out Texture2D texture,
            out Vector2 uvMin, out Vector2 uvMax, out GlyphMetrics metrics)
        {
            if (TryGetSpriteGlyph(glyphIndex, out var glyph))
            {
                texture = glyph.texture;
                uvMin = glyph.uvMin;
                uvMax = glyph.uvMax;
                metrics = Metrics(glyph.aspect);
                return true;
            }
            texture = null;
            uvMin = uvMax = default;
            metrics = default;
            return false;
        }

        private static GlyphMetrics Metrics(float aspect)
            => new(DesignUnits * aspect, DesignUnits, 0f, DesignUnits, DesignUnits * aspect);

        /// <summary>
        /// Captures what drawing and field derivation need from the sprite. The texture region is the
        /// sprite's whole rect: a tightly packed sprite's mesh coordinates are extrapolated to it, since
        /// its outline only covers part of the rect and the rect itself is not exposed for packed sprites.
        /// </summary>
        private static SpriteGlyph Capture(Sprite sprite)
        {
            var texture = sprite.texture;
            var uv = sprite.uv;
            var rect = sprite.rect;
            var aspect = rect.height > 0f ? rect.width / rect.height : 1f;

            var uvMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var uvMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < uv.Length; i++)
            {
                uvMin = Vector2.Min(uvMin, uv[i]);
                uvMax = Vector2.Max(uvMax, uv[i]);
            }
            if (!(uvMax.x > uvMin.x) || !(uvMax.y > uvMin.y))
            {
                uvMin = Vector2.zero;
                uvMax = Vector2.one;
            }

            if (!sprite.packed || sprite.packingMode != SpritePackingMode.Tight)
                return new SpriteGlyph(sprite, texture, uvMin, uvMax, aspect, null, null, null);

            var vertices = sprite.vertices;
            var bounds = sprite.bounds;
            var origin = (Vector2)bounds.min;
            var size = (Vector2)bounds.size;
            var positions = new Vector2[vertices.Length];
            var posMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var posMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < vertices.Length; i++)
            {
                var p = new Vector2(
                    size.x > 0f ? (vertices[i].x - origin.x) / size.x : 0f,
                    size.y > 0f ? (vertices[i].y - origin.y) / size.y : 0f);
                positions[i] = p;
                posMin = Vector2.Min(posMin, p);
                posMax = Vector2.Max(posMax, p);
            }

            if (posMax.x > posMin.x && posMax.y > posMin.y)
            {
                var k = new Vector2((uvMax.x - uvMin.x) / (posMax.x - posMin.x), (uvMax.y - uvMin.y) / (posMax.y - posMin.y));
                var rectMin = uvMin - Vector2.Scale(posMin, k);
                uvMax = rectMin + k;
                uvMin = rectMin;
            }

            return new SpriteGlyph(sprite, texture, uvMin, uvMax, aspect, positions, uv, sprite.triangles);
        }

        /// <summary>
        /// Prepares only silhouette fields: the sprites whose requested field the SDF atlas lacks or holds
        /// at too tight a pad tier. Their pixels are read back right away — the read-back is a graphics
        /// call, and the render step may run off the main thread.
        /// </summary>
        internal override PreparedBatch? PrepareGlyphBatch(List<uint> glyphIndices, UniTextRenderMode mode,
            long varHash48 = 0, int[] ftCoords = null, FastIntDictionary<byte> fieldRequests = null)
        {
            if (glyphIndices == null || glyphIndices.Count == 0 || fieldRequests == null || fieldRequests.Count == 0)
                return null;

            var fieldAtlas = GlyphAtlas.GetInstance(UniTextRenderMode.SDF);
            var fieldVarHash = GlyphAtlas.FieldVarHash48(FontDataHash);
            var filtered = new PooledBuffer<uint>();
            filtered.EnsureCapacity(glyphIndices.Count);
            var extents = new PooledBuffer<byte>();
            extents.EnsureCapacity(glyphIndices.Count);

            for (var i = 0; i < glyphIndices.Count; i++)
            {
                var glyphIndex = glyphIndices[i];
                if (!fieldRequests.TryGetValue((int)glyphIndex, out var extent) || extent == 0
                    || HasSufficientField(fieldAtlas, fieldVarHash, glyphIndex, extent))
                    continue;
                filtered.Add(glyphIndex);
                extents.Add(extent);
            }

            if (filtered.count == 0)
            {
                filtered.Return();
                extents.Return();
                return null;
            }

            prepared = Render(filtered);
            return new PreparedBatch
            {
                filteredGlyphs = filtered,
                fieldExtents = extents,
                varHash48 = DefaultVarHash48
            };
        }

        internal override object RenderPreparedBatch(PreparedBatch batch)
        {
            var rendered = prepared;
            prepared = null;
            return rendered ?? Render(batch.filteredGlyphs);
        }

        private RenderedGlyphData[] Render(PooledBuffer<uint> glyphIndices)
        {
            var rendered = new RenderedGlyphData[glyphIndices.count];
            var maxSize = GlyphAtlas.LargestTileSize;
            for (var i = 0; i < glyphIndices.count; i++)
            {
                if (!TryGetSpriteGlyph(glyphIndices[i], out var glyph)) continue;
                var pixels = SpritePixelReader.Read(in glyph, maxSize, out var width, out var height);
                rendered[i] = new RenderedGlyphData
                {
                    width = width,
                    height = height,
                    bearingX = 0f,
                    bearingY = height,
                    advanceX = width,
                    rgbaPixels = pixels,
                    isBGRA = false
                };
            }
            return rendered;
        }

        /// <summary>A sprite glyph is its rect's aspect wide and one em tall, standing on the baseline.</summary>
        protected override GlyphMetrics ComputeGlyphMetrics(uint glyphIndex, RenderedGlyphData rendered,
            bool renderedByCOLRv1 = false)
            => Metrics(TryGetSpriteGlyph(glyphIndex, out var glyph) ? glyph.aspect : 1f);
    }
}
