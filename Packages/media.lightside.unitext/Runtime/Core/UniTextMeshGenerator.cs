using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace LightSide
{
    /// <summary>Provides the shared output list populated by ordered sub-mesh collectors.</summary>
    public readonly struct SubMeshCollectionContext
    {
        /// <summary>Render-data segments to append before the generator's base segments.</summary>
        public List<UniTextRenderData> Results { get; }

        internal SubMeshCollectionContext(List<UniTextRenderData> results) => Results = results;
    }

    internal readonly struct GlyphFace
    {
        public readonly Vector2 bottomLeft;
        public readonly Vector2 topLeft;
        public readonly Vector2 topRight;
        public readonly Vector2 bottomRight;

        public GlyphFace(Vector2 bottomLeft, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight)
        {
            this.bottomLeft = bottomLeft;
            this.topLeft = topLeft;
            this.topRight = topRight;
            this.bottomRight = bottomRight;
        }

        public static GlyphFace Read(Vector3[] vertices, int baseIndex)
            => new(ToVector2(vertices[baseIndex]), ToVector2(vertices[baseIndex + 1]),
                ToVector2(vertices[baseIndex + 2]), ToVector2(vertices[baseIndex + 3]));

        public Rect Bounds
        {
            get
            {
                var minX = Mathf.Min(Mathf.Min(bottomLeft.x, topLeft.x), Mathf.Min(topRight.x, bottomRight.x));
                var minY = Mathf.Min(Mathf.Min(bottomLeft.y, topLeft.y), Mathf.Min(topRight.y, bottomRight.y));
                var maxX = Mathf.Max(Mathf.Max(bottomLeft.x, topLeft.x), Mathf.Max(topRight.x, bottomRight.x));
                var maxY = Mathf.Max(Mathf.Max(bottomLeft.y, topLeft.y), Mathf.Max(topRight.y, bottomRight.y));
                return Rect.MinMaxRect(minX, minY, maxX, maxY);
            }
        }

        private static Vector2 ToVector2(in Vector3 value) => new(value.x, value.y);
    }

    /// <summary>
    /// Maps a glyph face before the per-glyph modifier chain to the resulting quad. Bilinear mapping
    /// preserves arbitrary vertex deformation allowed by <see cref="UniTextMeshGenerator.onGlyph"/>.
    /// </summary>
    internal readonly struct GlyphFaceTransform
    {
        private readonly bool initialized;
        private readonly Vector2 sourceOrigin;
        private readonly Vector2 sourceXAxis;
        private readonly Vector2 sourceYAxis;
        public readonly GlyphFace final;

        public GlyphFaceTransform(in GlyphFace source, in GlyphFace final)
        {
            initialized = true;
            sourceOrigin = source.bottomLeft;
            sourceXAxis = source.bottomRight - source.bottomLeft;
            sourceYAxis = source.topLeft - source.bottomLeft;
            this.final = final;
        }

        public bool IsIdentity => !initialized || Approximately(sourceOrigin, final.bottomLeft) &&
                                  Approximately(sourceOrigin + sourceYAxis, final.topLeft) &&
                                  Approximately(sourceOrigin + sourceXAxis + sourceYAxis, final.topRight) &&
                                  Approximately(sourceOrigin + sourceXAxis, final.bottomRight);

        public Vector2 TransformPoint(Vector2 point)
        {
            if (!initialized) return point;
            var relative = point - sourceOrigin;
            var determinant = sourceXAxis.x * sourceYAxis.y - sourceXAxis.y * sourceYAxis.x;
            if (Mathf.Abs(determinant) <= 1e-7f)
                throw new InvalidOperationException("A resolved glyph transform has a degenerate source quad.");

            var inverse = 1f / determinant;
            var u = (relative.x * sourceYAxis.y - relative.y * sourceYAxis.x) * inverse;
            var v = (sourceXAxis.x * relative.y - sourceXAxis.y * relative.x) * inverse;
            var bottom = Vector2.LerpUnclamped(final.bottomLeft, final.bottomRight, u);
            var top = Vector2.LerpUnclamped(final.topLeft, final.topRight, u);
            return Vector2.LerpUnclamped(bottom, top, v);
        }

        public Rect TransformBounds(Rect bounds)
        {
            var a = TransformPoint(new Vector2(bounds.xMin, bounds.yMin));
            var b = TransformPoint(new Vector2(bounds.xMin, bounds.yMax));
            var c = TransformPoint(new Vector2(bounds.xMax, bounds.yMax));
            var d = TransformPoint(new Vector2(bounds.xMax, bounds.yMin));
            var minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            var minY = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            var maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            var maxY = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        public bool EquivalentTo(in GlyphFaceTransform other)
            => Approximately(TransformPoint(Vector2.zero), other.TransformPoint(Vector2.zero)) &&
               Approximately(TransformPoint(Vector2.right), other.TransformPoint(Vector2.right)) &&
               Approximately(TransformPoint(Vector2.up), other.TransformPoint(Vector2.up)) &&
               Approximately(TransformPoint(Vector2.one), other.TransformPoint(Vector2.one));

        private static bool Approximately(Vector2 a, Vector2 b) => (a - b).sqrMagnitude <= 1e-8f;
    }

    internal readonly struct GlyphVisualGeometry
    {
        public readonly int positionedGlyphIndex;
        public readonly int cluster;
        public readonly bool isVirtual;
        public readonly GlyphFaceTransform transform;

        public GlyphVisualGeometry(int positionedGlyphIndex, int cluster, bool isVirtual,
            in GlyphFace source, in GlyphFace final)
        {
            this.positionedGlyphIndex = positionedGlyphIndex;
            this.cluster = cluster;
            this.isVirtual = isVirtual;
            transform = new GlyphFaceTransform(in source, in final);
        }
    }

    /// <summary>
    /// Raw geometry slice describing a single text/color/sub-mesh render segment.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Holds <b>references</b> to pooled vertex/UV/color/index arrays plus offset+count into them.
    /// The consumer (canvas: <see cref="UniText.UpdateSubMeshes"/>; world:
    /// <see cref="UniTextWorldBatcher"/>) does whatever it needs with this data — either uploads into
    /// a reusable <see cref="Mesh"/> for <c>CanvasRenderer</c>, or copies into its combined-mesh
    /// buffers for world-space batching.
    /// </para>
    /// <para>
    /// Array references are valid <b>only until the next collect cycle</b> on the same generator —
    /// pooled buffers can be returned or regrown. Consumers must read/copy immediately and not retain
    /// references across frames.
    /// </para>
    /// </remarks>
    public struct UniTextRenderData
    {
        /// <summary>The font identifier this render data belongs to.</summary>
        public int fontId;

        /// <summary>
        /// Optional custom material. When <see langword="null"/>, the renderer uses the default
        /// SDF/MSDF or color material for this <see cref="fontId"/>.
        /// </summary>
        public Material materialOverride;

        /// <summary>
        /// Optional atlas binding for <see cref="materialOverride"/>. When <see langword="null"/>,
        /// the custom material keeps its own texture and does not follow atlas replacements.
        /// </summary>
        public GlyphAtlas atlasOverride;

        /// <summary>
        /// The single draw-order axis — the emitting layer's <see cref="ILayer.LayerSequence"/>
        /// (style-stack position). Every segment (base SDF runs, color, paint-layer sub-meshes, inline
        /// media, custom materials) sorts by this, so stacking follows the order layers appear in
        /// <c>Styles</c>. The base text / default fill sits at <see cref="DefaultFillSequence"/> (bottom).
        /// </summary>
        public int sequence;

        /// <summary>Stable sort key within the same <see cref="sequence"/> (lower renders first).</summary>
        public int sortIndex;

        /// <summary>How this draw composites its premultiplied output with the rendered backdrop.</summary>
        public LayerBlend blend;

        /// <summary>Vertex positions array (pooled). Read <c>[vertexOffset, vertexOffset+vertexCount)</c>.</summary>
        public Vector3[] vertices;
        /// <summary>UV0 array (pooled). Must always be valid when <see cref="vertexCount"/> &gt; 0.</summary>
        public Vector4[] uvs0;
        /// <summary>UV1 array (pooled). <see langword="null"/> or ignored when <see cref="hasUv1"/> is false.</summary>
        public Vector4[] uvs1;
        /// <summary>UV2 array (pooled). <see langword="null"/> or ignored when <see cref="hasUv2"/> is false.</summary>
        public Vector4[] uvs2;
        /// <summary>UV3 array (pooled). <see langword="null"/> or ignored when <see cref="hasUv3"/> is false.</summary>
        public Vector4[] uvs3;
        /// <summary>Vertex colors array (pooled).</summary>
        public Color32[] colors;
        /// <summary>Triangle indices array (pooled). Indices are relative to <see cref="vertexOffset"/> —
        /// they index into this segment's vertex slice, not into the whole <see cref="vertices"/> array.
        /// Read <c>[triangleOffset, triangleOffset+triangleCount)</c>.</summary>
        public int[] triangles;

        /// <summary>Start index in <see cref="vertices"/>/<see cref="uvs0"/>/<see cref="colors"/> etc.</summary>
        public int vertexOffset;
        /// <summary>Number of vertices in this segment (starting at <see cref="vertexOffset"/>).</summary>
        public int vertexCount;
        /// <summary>Start index in <see cref="triangles"/>.</summary>
        public int triangleOffset;
        /// <summary>Number of triangle indices in this segment.</summary>
        public int triangleCount;

        /// <summary>When <see langword="true"/>, <see cref="uvs1"/> is valid and should be uploaded.</summary>
        public bool hasUv1;
        /// <summary>When <see langword="true"/>, <see cref="uvs2"/> is valid and should be uploaded.</summary>
        public bool hasUv2;
        /// <summary>When <see langword="true"/>, <see cref="uvs3"/> is valid and should be uploaded.</summary>
        public bool hasUv3;
    }


    /// <summary>
    /// Converts positioned glyphs into Unity mesh data for text rendering.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the final stage of the text processing pipeline. It takes <see cref="PositionedGlyph"/>
    /// data from <see cref="TextProcessor"/> and generates vertex, UV, color, and triangle data
    /// suitable for Unity's mesh system.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item>Groups glyphs by rendering target to minimize draw calls: one segment per font (Texture2DArray atlas)</item>
    /// <item>Uses pooled buffers from <see cref="ArrayPool{T}"/> for zero allocations</item>
    /// <item>Provides callbacks for text modifiers to inject custom processing</item>
    /// </list>
    /// </para>
    /// <para>
    /// Typical usage:
    /// <code>
    /// generator.SetRectOffset(rect);
    /// generator.GenerateMeshDataOnly(positionedGlyphs);
    /// var renderData = generator.CollectRenderData();
    /// // Use renderData to render each segment
    /// generator.ReturnInstanceBuffers();
    /// </code>
    /// </para>
    /// </remarks>
    /// <seealso cref="TextProcessor"/>
    /// <seealso cref="PositionedGlyph"/>
    /// <seealso cref="UniTextRenderData"/>
    public class UniTextMeshGenerator
    {
        /// <summary>
        /// Base UV-space padding (normalized by glyph height) allocated around every face quad.
        /// Face and effect modifiers that expand the quad must subtract this baseline from their
        /// requested extent when computing the expansion delta.
        /// </summary>
        public const float DefaultSdfPadding = 0.02f;

        /// <summary>Order for callbacks that consume the completed glyph quad to derive additional geometry.</summary>
        public const int GlyphGeometryConsumerOrder = int.MaxValue;

        /// <summary>The cluster index of the glyph currently being processed.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Maps back to codepoint indices.</remarks>
        public int currentCluster;

        /// <summary>Font glyph id of the glyph currently being processed — identifies which glyph a quad renders when the cluster alone is ambiguous (virtual glyphs).</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public int currentGlyphId;

        /// <summary>Height of the current glyph including padding.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float height;

        /// <summary>Y coordinate of the text baseline for the current glyph.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float baselineY;

        /// <summary>X coordinate of the cursor position (pen position) for the current glyph.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Use as pivot for per-glyph scaling
        /// so that bearing and width scale proportionally with per-cluster advance changes.</remarks>
        public float cursorX;

        /// <summary>Design-units → pixels for the current glyph's font.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback.</remarks>
        public float scale;

        /// <summary>FontSize * FontScale — converts normalized glyph metrics to UI-space units. Constant per font.</summary>
        /// <remarks>Valid during <see cref="onGlyph"/> callback. Fixed-pixel-size effect geometry converts through
        /// this factor times <see cref="currentGlyphScale"/>; the factor alone ignores per-glyph scaling and pins
        /// the result to the component font size.</remarks>
        public float fontMetricFactor;

        /// <summary>Default vertex color applied to all glyphs.</summary>
        public Color32 defaultColor;

        /// <summary>Current font being processed.</summary>
        /// <remarks>Valid during mesh generation for the current font segment.</remarks>
        public UniTextFont.Core font;

        /// <summary>X offset from the rect origin.</summary>
        public float offsetX;

        /// <summary>Y offset from the rect origin.</summary>
        public float offsetY;

        /// <summary>Current number of vertices in the mesh buffers.</summary>
        public int vertexCount;

        /// <summary>Current number of triangle indices in the mesh buffers.</summary>
        public int triangleCount;

        /// <summary>
        /// Index of the first vertex of the face quad of the glyph currently being processed.
        /// Stable across all <see cref="onGlyph"/> invocations for a single glyph, even when
        /// modifiers append additional geometry that grows <see cref="vertexCount"/>.
        /// </summary>
        public int faceBaseIdx;


        private PooledBuffer<Vector3> vertices;
        private PooledBuffer<Vector4> uvs0;
        private PooledBuffer<Vector4> uvs1;
        private PooledBuffer<Vector4> uvs2;
        private PooledBuffer<Vector4> uvs3;
        private PooledBuffer<Color32> colors;
        private PooledBuffer<int> triangles;
        private bool hasGeneratedData;

        private int sdfVertexCount;
        private int sdfFontId;

        private PooledBuffer<SdfRun> sdfRuns;
        private int sdfRunCount;

        /// <summary>Set by the first <see cref="CollectRenderData"/> after a generation: run triangles are
        /// rebased in place from absolute vertex indices to run-slice-relative ones (see
        /// <see cref="AddSegmentRuns"/>), and the emitted base-segment entries are cached. A later collect
        /// of the same generation (canvas apply + world batcher capture both collect) replays the cached
        /// entries verbatim instead of recomputing run merging from that call's sub-mesh list — the rebase
        /// is destructive, so the emitted layout must be byte-identical across same-generation collects
        /// even if a sub-mesh provider misbehaves.</summary>
        private bool runsRebased;

        /// <summary>Base SDF/color segment entries emitted by the first collect of the current generation, replayed on later same-generation collects.</summary>
        private readonly List<UniTextRenderData> cachedSegmentEntries = new();

#if UNITEXT_DEBUG
        /// <summary>Sub-mesh sequences observed at the first collect — later same-generation collects are checked against it (the determinism contract of <see cref="onCollectSubMeshes"/>).</summary>
        private readonly List<int> firstCollectSubMeshSequences = new();
#endif

        /// <summary>Sequence assigned to a glyph face when no explicit fill claimed it — below every layer (default fill = bottom of the stack).</summary>
        public const int DefaultFillSequence = -1;

        /// <summary>
        /// Set during <see cref="onGlyph"/> by the fill that claims the current glyph's base quad, to its
        /// <see cref="ILayer.LayerSequence"/>; left at <see cref="DefaultFillSequence"/> when none does.
        /// The face quad is ordered at this sequence, so an explicit fill stacks the glyph at its layer position.
        /// </summary>
        public int claimedFillSequence;

        /// <summary>Blend mode paired with <see cref="claimedFillSequence"/> for the current glyph face.</summary>
        public LayerBlend claimedFillBlend;

        internal bool hasClaimedFillLayerOverride;
        internal bool hasClaimedFillBlendOverride;
        internal int claimedFillSequenceOverride;
        internal LayerBlend claimedFillBlendOverride;

        /// <summary>
        /// Added to every layer sequence a modifier captures while it is set, lifting a whole stack —
        /// face, stroke, shadow, glow — into a higher band together instead of leaving each layer at its
        /// own style-stack position. Zero for shaped text; a virtual-glyph emitter raises it around its
        /// own emission and restores it afterwards. A layer folds it in where it <em>captures</em> the
        /// sequence, during <see cref="onGlyph"/> — never where it flushes, by which time the emitter
        /// has restored it.
        /// </summary>
        public int sequenceBias;

        /// <summary>Bias clearing the whole stamped layer range, so a stack raised by it draws above every unbiased quad. Stamped once per rebuild.</summary>
        internal int overlayBias;

        /// <summary>The component's colour-filter registry and per-rebuild composed-matrix table; emitters resolve the transform covering a cluster above their stamped sequence through it.</summary>
        internal readonly ColorFilterStack filters = new();

        /// <summary>Lowest sequence a stack raised by <see cref="overlayBias"/> can reach; every unbiased quad sits below it. Stamped once per rebuild.</summary>
        internal int overlayBandStart;

        /// <summary>Glyph atlas keys used in the last mesh generation (for reference counting).</summary>
        internal PooledBuffer<long> usedGlyphKeys;
        internal PooledBuffer<long> usedColorKeys;

        /// <summary>SDF-atlas keys of the colour-glyph silhouette fields the last mesh generation sampled (for reference counting).</summary>
        internal PooledBuffer<long> usedFieldKeys;

        /// <summary>
        /// The silhouette field behind a colour face quad, keyed by the face's base vertex: the SDF tile
        /// an effect duplicate samples in place of the bitmap, and the padding fractions that place an
        /// SDF quad over the face's final corners.
        /// </summary>
        internal struct ColorFaceField
        {
            public int handle;
            public float glyphH;
            public float aspect;
            /// <summary>Share of the face quad's width and height the colour tile's padding takes on each side.</summary>
            public float padFracX, padFracY;
            public byte padTier;
            public long key;
            public long fieldVarHash;
            public uint glyphIndex;
            public UniTextFont.Core font;
        }

        private FastIntDictionary<ColorFaceField> colorFaceFields;
        private bool hasCurrentColorField;

        /// <summary>Whether the colour glyph currently in <see cref="onGlyph"/> carries a silhouette field for effects to sample; always false for an outline glyph.</summary>
        public bool HasColorFaceField => hasCurrentColorField;

        internal bool TryGetColorFaceField(int faceBaseIdx, out ColorFaceField field)
        {
            if (colorFaceFields != null && colorFaceFields.Count > 0)
                return colorFaceFields.TryGetValue(faceBaseIdx, out field);
            field = default;
            return false;
        }

        /// <summary>Height in em of the field a face quad samples: the glyph's for an outline face, the silhouette's for a colour face with a field, 0 for a colour face without one.</summary>
        public float FaceGlyphH(int baseIdx)
        {
            if (colorFaceFields != null && colorFaceFields.Count > 0
                && colorFaceFields.TryGetValue(baseIdx, out var field))
                return field.glyphH;
            return uvs0.data[baseIdx].w;
        }

        /// <summary>Set when a known non-empty glyph had no atlas entry during generation (evicted or cleared). The component reacts by re-collecting atlas requests.</summary>
        internal bool missingAtlasGlyphs;

        internal string debugName;

        private struct SdfQuad { public int sequence; public int baseIdx; public LayerBlend blend; }

        private struct SortedSdfQuad { public int baseIdx; public LayerBlend blend; }

        private struct GroupedSdfQuad
        {
            public int baseIdx;
            public int sequence;
            public int group;
            public LayerBlend blend;
            public bool glyphMajor;
        }

        /// <summary>One maximal block of same-sequence, same-blend quads after materialization. <see cref="triStart"/>
        /// is absolute in the shared triangle buffer; <see cref="vertMin"/>/<see cref="vertMax"/> bound the
        /// vertex indices its triangles reference, so <see cref="CollectRenderData"/> can emit the run with
        /// a tight vertex slice instead of re-submitting the whole segment's vertex set per run.</summary>
        private struct SdfRun
        {
            public int sequence;
            public int triStart;
            public int vertMin;
            public int vertMax;
            public LayerBlend blend;
        }

        /// <summary>
        /// Worker-thread scratch, shared by every generator that runs on the thread. Rented lazily, never
        /// returned — the buffers live for the thread's lifetime and are valid only within one
        /// <see cref="GenerateMeshDataOnly"/> call (each call resets the counts before use).
        /// </summary>
        [ThreadStatic] private static FastLongDictionary<GlyphAtlas.GlyphEntry> glyphEntryCache;
        [ThreadStatic] private static PooledBuffer<SdfQuad> sdfQuads;
        [ThreadStatic] private static PooledBuffer<SortedSdfQuad> sdfSortedQuads;
        [ThreadStatic] private static PooledBuffer<int> sdfSeqCounts;
        [ThreadStatic] private static PooledBuffer<GroupedSdfQuad> sdfGroupedQuads;
        [ThreadStatic] private static PooledBuffer<GroupedSdfQuad> sdfGroupedSorted;
        [ThreadStatic] private static PooledBuffer<int> sdfClusterGroups;
        [ThreadStatic] private static PooledBuffer<int> sdfGroupCounts;
        [ThreadStatic] private static PooledBuffer<GlyphQuadInput> quadInputs;
        [ThreadStatic] private static PooledBuffer<int> quadSrc;

        /// <summary>
        /// Looks up a glyph entry from the per-frame cache. Returns true if found (repeated glyph).
        /// On miss, the caller should look up the atlas and call <see cref="CacheGlyphEntry"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryGetCachedGlyphEntry(long glyphKey, out GlyphAtlas.GlyphEntry entry)
        {
            return glyphEntryCache.TryGetValue(glyphKey, out entry);
        }

        /// <summary>
        /// Ref-returning variant of <see cref="TryGetCachedGlyphEntry"/>: on a hit returns a reference into
        /// the cache's backing array, avoiding a copy of the multi-word <see cref="GlyphAtlas.GlyphEntry"/>.
        /// The reference is invalidated by the next insert into the cache (<see cref="CacheGlyphEntry"/> /
        /// <see cref="TrackGlyphKey"/>), so read it before caching another entry and never keep it beyond the
        /// current glyph. On a miss <paramref name="found"/> is <c>false</c> and the reference must not be read.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref GlyphAtlas.GlyphEntry TryGetCachedGlyphEntryRef(long glyphKey, out bool found)
        {
            return ref glyphEntryCache.TryGetValueRef(glyphKey, out found);
        }

        /// <summary>
        /// Stores a glyph entry in the cache and tracks the key for atlas ref counting.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CacheGlyphEntry(long glyphKey, in GlyphAtlas.GlyphEntry entry)
        {
            glyphEntryCache.AddOrUpdate(glyphKey, entry);
            usedGlyphKeys.Add(glyphKey);
        }

        /// <summary>
        /// Tracks a glyph key for atlas ref counting. Deduplicates automatically.
        /// Use for modifier glyphs that don't need cached entry data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrackGlyphKey(long glyphKey)
        {
            if (glyphEntryCache.ContainsKey(glyphKey))
                return false;
            glyphEntryCache.AddOrUpdate(glyphKey, default);
            usedGlyphKeys.Add(glyphKey);
            return true;
        }

        private readonly UniTextFontProvider fontProvider;
        private readonly UniTextBuffers buf;

        /// <summary>
        /// Invoked after the main glyph loop completes, before any finalization phase.
        /// Subscribers may emit additional quads into the open vertex stream; if they call
        /// <see cref="onGlyph"/> for each emitted quad with <see cref="isVirtualGlyph"/> set,
        /// per-glyph modifiers (color, gradient, bold) and effect modifiers (outline, shadow,
        /// extrude) pick up the new quads through the standard pipeline.
        /// </summary>
        /// <remarks>
        /// Used by decoration modifiers (underline, strikethrough, kashida) to add their
        /// geometry while staying within the same effect/color/etc pipeline as primary glyphs.
        /// </remarks>
        private OrderedEvent mainPassComplete;

        /// <summary>Runs after every face quad — text and colour glyphs alike — and before main-pass finalization.</summary>
        public OrderedEvent onMainPassComplete => mainPassComplete ??= new OrderedEvent();

        /// <summary>
        /// Finalization phase for the main glyph pass. Runs after <see cref="onMainPassComplete"/>,
        /// once every face quad (text and colour) exists, and before triangle materialization.
        /// </summary>
        /// <remarks>
        /// Effect modifiers flush queued effect requests here, appending duplicate quads (outline,
        /// shadow, extrude) into the base segment — a colour glyph's duplicates sample its silhouette
        /// field. Draw order follows each quad's layer sequence — text and color share one segment
        /// and one triangle materialization.
        /// </remarks>
        private OrderedEvent mainPassFinalize;

        /// <summary>Runs after <see cref="onMainPassComplete"/>, with every face quad emitted, before triangle materialization.</summary>
        public OrderedEvent onMainPassFinalize => mainPassFinalize ??= new OrderedEvent();

        /// <summary>Invoked for each glyph during mesh generation.</summary>
        /// <remarks>
        /// Primary callback for text modifiers to apply per-glyph effects.
        /// Access current glyph data via the public state fields on this generator instance.
        /// </remarks>
        private OrderedEvent glyphEvent;

        /// <summary>Runs for each glyph during mesh generation.</summary>
        public OrderedEvent onGlyph => glyphEvent ??= new OrderedEvent();

        /// <summary>
        /// Internal tail receptor carrying a primary glyph's source and final face quad after every
        /// <see cref="onGlyph"/> subscriber has run. Range geometry and hit testing use it without
        /// coupling the core to concrete deformation modifiers.
        /// </summary>
        internal Action<GlyphVisualGeometry> onGlyphComplete;
        internal Action onGlyphGeometryRebuildStart;
        internal bool captureGlyphGeometry;
        internal bool captureIdentityGlyphGeometry;

        private OrderedEvent rebuildEnd;

        /// <summary>Runs after all mesh generation is complete.</summary>
        public OrderedEvent onRebuildEnd => rebuildEnd ??= new OrderedEvent();

        private OrderedEvent rebuildStart;

        /// <summary>Runs before mesh generation starts.</summary>
        public OrderedEvent onRebuildStart => rebuildStart ??= new OrderedEvent();

        /// <summary>
        /// Invoked by <see cref="CollectRenderData"/> before the base SDF/color segments are written.
        /// Subscribers append their own <see cref="UniTextRenderData"/> entries (each with a custom
        /// <see cref="UniTextRenderData.materialOverride"/>, <see cref="UniTextRenderData.atlasOverride"/>,
        /// <see cref="UniTextRenderData.sequence"/> and <see cref="UniTextRenderData.sortIndex"/>) to the list.
        /// </summary>
        /// <remarks>
        /// The result buffer is stable-sorted by (<see cref="UniTextRenderData.sequence"/>,
        /// <see cref="UniTextRenderData.sortIndex"/>), which determines sibling order of
        /// <c>-_UTSM_-</c> renderers in <see cref="UniText.UpdateSubMeshes"/>.
        /// </remarks>
        private OrderedEvent<SubMeshCollectionContext> collectSubMeshes;

        /// <summary>Collects custom render-data segments before the base segments are appended.</summary>
        public OrderedEvent<SubMeshCollectionContext> onCollectSubMeshes
            => collectSubMeshes ??= new OrderedEvent<SubMeshCollectionContext>();

        /// <summary>
        /// Maximum UV-space padding requested for the current glyph by any modifier.
        /// Reset to 0 before each <see cref="onGlyph"/> invocation. Subscribers accumulate via max.
        /// Read after <see cref="onGlyph"/> to decide atlas tier upgrades.
        /// </summary>
        public float currentMaxGlyphExtent;

        /// <summary>Largest resolution boost (0–2 tile-size classes above the glyph's default) a resolution modifier requested for the current glyph. Reset to 0 before each <see cref="onGlyph"/>; accumulated via max, read after to request a grow-only atlas tile-size upgrade. See <see cref="RequestTileSizeUpgradeIfNeeded"/>.</summary>
        public int currentTileSizeBoost;

        /// <summary>
        /// Product of every per-glyph scale applied to the current face quad (size, small-caps,
        /// sub/superscript, ruby, font metric overrides) via <see cref="ScaleFace"/>. Reset to 1 before
        /// each <see cref="onGlyph"/>; <see cref="GlyphScale"/> reads it back per quad once the glyph
        /// completes. Em → pixels for an emitted quad is <see cref="fontMetricFactor"/> times this:
        /// em geometry multiplies by it and absolute (px) geometry divides by it, so a px value keeps a
        /// constant on-screen size while the glyph scales.
        /// </summary>
        public float currentGlyphScale = 1f;

        /// <summary>
        /// Reset to <see langword="false"/> before each <see cref="onGlyph"/>; a fill layer sets it
        /// when it claims (recolours) the glyph's base quad, so later fills on the same glyph stack
        /// as duplicates instead of fighting over the base.
        /// </summary>
        public bool fillClaimedThisGlyph;

        /// <summary>
        /// Set when a layer takes over rendering the current glyph's base face in its own sub-mesh — a
        /// texture fill, or a <see cref="MaterialModifier"/> in <c>Replace</c> mode. The base-mesh face quad
        /// must then <b>not</b> be recorded (it would sit at the claimed sequence as a transparent placeholder
        /// and stop the base mesh from slicing there, breaking layer order). Reset before each
        /// <see cref="onGlyph"/>.
        /// </summary>
        public bool baseFaceClaimed;

        /// <summary>
        /// Set by a virtual-glyph emitter (decoration line with its own paint) around its
        /// <see cref="onGlyph"/> so the inherited text fill stands down entirely — neither claiming the
        /// base nor stacking a duplicate over it. Unlike <see cref="fillClaimedThisGlyph"/> (which only
        /// loses the base race and still overlays), this fully suppresses base fills, letting the line's
        /// own paint replace the inherited one (CSS <c>text-decoration-color</c>). Non-base layers
        /// (shadow, glow, stroke) are unaffected and still decorate the line.
        /// </summary>
        public bool suppressInheritedFill;

        /// <summary>
        /// True when the currently processed glyph is virtual (injected by a modifier — list marker,
        /// ellipsis dot — and has no <see cref="ShapedGlyph"/> behind it).
        /// </summary>
        /// <remarks>
        /// Modifiers that drive their behavior from shaping data (truncation flags, super/sub
        /// position) must early-out on virtual glyphs to avoid acting on cluster indices that
        /// belong to source text rather than the injected decoration.
        /// </remarks>
        public bool isVirtualGlyph;

        /// <summary>
        /// Index into the positioned-glyph array of the glyph the current quad renders, or <c>-1</c>
        /// when no positioned glyph stands behind it.
        /// </summary>
        /// <remarks>
        /// Valid during <see cref="onGlyph"/>; reset to <c>-1</c> by <see cref="ResetPerGlyphState"/>,
        /// so an emitter that fires <see cref="onGlyph"/> without publishing a source glyph reports
        /// <c>-1</c>. A <c>-1</c> quad was authored directly in final pixel space by its emitter
        /// (decoration line slice, kashida): its origin, extent and thickness already carry every
        /// layout and per-cluster scale, so a modifier that scales a quad about its own pen must
        /// stand down — the scale is already in the numbers, and a multi-quad decoration would come
        /// apart at the joins. Positioned glyphs injected by a modifier (list marker, ruby, math
        /// sub-glyph) carry a real index and are scaled like any other glyph; use
        /// <see cref="isVirtualGlyph"/> to tell those from shaped text.
        /// </remarks>
        public int currentPositionedIndex = -1;

        internal struct TierUpgradeRequest
        {
            public long glyphKey;
            public uint glyphIndex;
            public byte requiredTier;
            public UniTextFont.Core font;
            public long varHash48;
            public int[] ftCoords;
            public UniTextRenderMode mode;
        }

        internal readonly List<TierUpgradeRequest> tierUpgradeRequests = new();

        internal struct TileSizeUpgradeRequest
        {
            public long glyphKey;
            public uint glyphIndex;
            public int tileSizeBoost;
            public UniTextFont.Core font;
            public long varHash48;
            public int[] ftCoords;
            public UniTextRenderMode mode;
        }

        internal readonly List<TileSizeUpgradeRequest> tileSizeUpgradeRequests = new();

        private Rect rectOffset;

        /// <summary>
        /// Initializes a new instance of the <see cref="UniTextMeshGenerator"/> class.
        /// </summary>
        /// <param name="fontProvider">The font provider for accessing font assets and materials.</param>
        /// <param name="uniTextBuffers">The shared buffer container from text processing.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="fontProvider"/> or <paramref name="uniTextBuffers"/> is <see langword="null"/>.
        /// </exception>
        public UniTextMeshGenerator(UniTextFontProvider fontProvider, UniTextBuffers uniTextBuffers)
        {
            this.fontProvider = fontProvider ?? throw new ArgumentNullException(nameof(fontProvider));
            buf = uniTextBuffers ?? throw new ArgumentNullException(nameof(uniTextBuffers));
        }

        /// <summary>Gets or sets the font size in points for mesh generation.</summary>
        public float FontSize { get; set; } = 36f;

        /// <summary>Gets or sets the atlas mode (SDF or MSDF) for glyph lookup and material selection.</summary>
        public UniTextRenderMode RenderMode { get; set; }

        /// <summary>
        /// Glyph-mode offset packed into UV1.w alongside the intra-glyph X fraction — the shader-side
        /// selector between the SDF/MSDF/color samplers of the unified material (LightSideGlyphMode in
        /// LightSideAtlasDecode.hlsl). Every text-quad writer (base kernel, kashida, decorations) adds
        /// this to its UV1.w; color quads use <see cref="ColorUv1wBias"/>.
        /// </summary>
        internal float TextUv1wBias => RenderMode == UniTextRenderMode.MSDF ? 2f : 0f;

        /// <summary>UV1.w glyph-mode offset for color quads (mode 2 — the color sampler).</summary>
        internal const float ColorUv1wBias = 4f;

        /// <summary>Gets a value indicating whether mesh data has been generated and is available.</summary>
        public bool HasGeneratedData => hasGeneratedData;


        /// <summary>Gets the vertex position buffer (X, Y, Z coordinates).</summary>
        public Vector3[] Vertices => vertices.data;

        /// <summary>
        /// Scales a glyph quad (4 vertices) around the cursor position and baseline.
        /// Used by SizeModifier, SmallCapsModifier, ScriptPositionModifier.
        /// </summary>
        /// <param name="pivotX">Cursor position (pen X) — bearing and width scale proportionally from this pivot.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ScaleGlyphQuad(Vector3[] verts, int baseIdx, float pivotX, float baselineY, float scale, float yOffset = 0f, float xOffset = 0f)
        {
            var pivotY = baselineY + yOffset;
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x = pivotX + (v.x - pivotX) * scale + xOffset;
                v.y = pivotY + (v.y - baselineY) * scale;
            }
        }

        /// <summary>
        /// Scales the current face quad and records the factor into <see cref="currentGlyphScale"/>.
        /// Every per-glyph size change (size tag, small-caps, sub/superscript, ruby, font metric
        /// overrides) must pass through here rather than <see cref="ScaleGlyphQuad"/> directly — a scale
        /// the generator does not see leaves paint-layer offset and quad growth stuck at the base size.
        /// </summary>
        public void ScaleFace(int baseIdx, float pivotX, float baselineY, float scale, float yOffset = 0f, float xOffset = 0f)
        {
            ScaleGlyphQuad(vertices.data, baseIdx, pivotX, baselineY, scale, yOffset, xOffset);
            currentGlyphScale *= scale;
        }

        /// <summary>
        /// Scales a face quad horizontally around the pen position — the fit / justification
        /// glyph-compression channel. Deliberately not folded into <see cref="currentGlyphScale"/>:
        /// paint-layer growth stays isotropic while the rendered glyph narrows.
        /// </summary>
        public void XScaleFace(int baseIdx, float pivotX, float scale)
        {
            var verts = vertices.data;
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                v.x = pivotX + (v.x - pivotX) * scale;
            }
        }

        /// <summary>Gets the primary UV buffer (texture coordinates and scale in W component).</summary>
        public Vector4[] Uvs0 => uvs0.data;

        /// <summary>Gets the vertex color buffer.</summary>
        public Color32[] Colors => colors.data;

        /// <summary>Gets the triangle index buffer.</summary>
        public int[] Triangles => triangles.data;

        /// <summary>Gets the UV1 buffer: x = aspect (glyphW/glyphH), y = faceDilate,
        /// z = per-glyph cluster index (monotonic per-line, transform-invariant),
        /// w = intra-glyph X fraction (0 on the left edge, 1 on the right — interpolated by GPU).</summary>
        public Vector4[] Uvs1 => uvs1.data;

        /// <summary>Gets the UV2 buffer — the COVERAGE contract (TEXCOORD2).</summary>
        /// <remarks>
        /// Layout (must match <c>UniText_Coverage.hlsl</c>, written by <see cref="CoverageQuadOps"/>):
        /// x = coverage mode + 16·cornerCode (see <see cref="CoverageMode.WithCorner"/>),
        /// y = p0 (width/offset), z = p1 (mode-specific), w = softness. Unallocated reads as 0 →
        /// Fill + legacy field, so plain text pays
        /// nothing. Effect colour lives in the standard vertex <see cref="Colors"/> attribute with
        /// alpha pre-multiplied by the source face vertex alpha; offsets are applied via mesh vertex
        /// displacement, not UV. Not allocated by default — call <see cref="EnsureUvBuffer"/> before
        /// writing. Custom effects must not repurpose this channel on painted text.
        /// </remarks>
        public Vector4[] Uvs2 => uvs2.data;

        /// <summary>Gets the UV3 buffer — the PAINT contract (TEXCOORD3).</summary>
        /// <remarks>
        /// Layout (must match the paint decode in the shader family, written by
        /// <see cref="CoverageQuadOps"/>): xy = paint coordinate in the mapping frame,
        /// z = gradient ramp row, w = paint kind code (0 solid, 1..3 gradient shapes, 4/5 texture).
        /// On a quad whose colour is sampled from a texture the CPU cannot recolour — a texture
        /// paint or a colour glyph — z instead carries a <see cref="ColorMatrixAtlas"/> row + 1
        /// (≤ 0 = unfiltered). Unallocated reads as 0 → solid. Not allocated by default — call
        /// <see cref="EnsureUvBuffer"/> before writing. Custom effects must not repurpose this
        /// channel on painted text.
        /// </remarks>
        public Vector4[] Uvs3 => uvs3.data;

        /// <summary>
        /// Allocates and zero-clears a UV effect buffer (channel 2 or 3) if not already allocated.
        /// </summary>
        /// <param name="channel">UV channel: 2 or 3.</param>
        public void EnsureUvBuffer(int channel)
        {
            ref var buf = ref (channel == 3 ? ref uvs3 : ref uvs2);
            if (buf.EnsureCleared(vertices.Capacity)) buf.count = vertexCount;
        }

        private PooledBuffer<ushort> preClaimAlpha;

        /// <summary>
        /// Records a face quad's per-vertex alpha as it is BEFORE a claiming fill recolours it, so
        /// layer duplicates (stroke, shadow, extrude) modulate against the glyph's own alpha rather
        /// than another layer's paint alpha. Entries are <c>0x100 | alpha</c>; 0 = never claimed.
        /// </summary>
        internal void StashPreClaimAlpha(int baseIdx)
        {
            preClaimAlpha.EnsureCleared(vertices.Capacity);
            var cols = colors.data;
            for (var i = 0; i < 4; i++)
                preClaimAlpha.data[baseIdx + i] = (ushort)(0x100 | cols[baseIdx + i].a);
        }

        /// <summary>The glyph's own alpha at this face vertex: the pre-claim stash when a fill claimed the quad, else the live vertex alpha.</summary>
        internal byte FaceAlpha(int idx)
        {
            var stash = preClaimAlpha.data;
            if (stash != null)
            {
                var v = stash[idx];
                if (v != 0) return (byte)v;
            }
            return colors.data[idx].a;
        }

        /// <summary>Whether a fill layer claimed the face quad at <paramref name="baseIdx"/> this generation, so its picture stands down for the fill's paint.</summary>
        internal bool WasFillClaimed(int baseIdx)
        {
            var stash = preClaimAlpha.data;
            return stash != null && (uint)baseIdx < (uint)stash.Length && stash[baseIdx] != 0;
        }

        private PooledBuffer<float> glyphScales;

        /// <summary>Records the finalised <see cref="currentGlyphScale"/> for a scaled glyph so deferred paint-layer duplicates (flushed after all per-glyph mutation) can read it by source vertex. Lazily rented; only scaled glyphs write, unscaled read back as 1.</summary>
        internal void StashGlyphScale(int baseIdx)
        {
            glyphScales.EnsureCleared(vertices.Capacity);
            glyphScales.data[baseIdx] = currentGlyphScale;
        }

        /// <summary>Per-glyph scale for the face quad starting at <paramref name="baseIdx"/> (1 when the glyph was never scaled). Read by paint layers at emit time.</summary>
        public float GlyphScale(int baseIdx)
        {
            var s = glyphScales.data;
            if (s == null || (uint)baseIdx >= (uint)s.Length) return 1f;
            var v = s[baseIdx];
            return v > 0f ? v : 1f;
        }

        #region Instance Buffer Management

        /// <summary>
        /// Rents the primary buffers and returns the lazily-allocated side buffers (UV2/UV3,
        /// pre-claim alpha, glyph scales), so a generation always starts with every vertex-indexed
        /// buffer either unallocated or sized to <see cref="vertices"/> — the previous cycle's
        /// buffers may still be rented when its apply was skipped.
        /// </summary>
        private void RentInstanceBuffers(int estimatedVertices, int estimatedTriangles)
        {
            vertices.Rent(estimatedVertices);
            uvs0.Rent(estimatedVertices);
            uvs1.Rent(estimatedVertices);
            colors.Rent(estimatedVertices);
            triangles.Rent(estimatedTriangles);
            uvs2.Return();
            uvs3.Return();
            preClaimAlpha.Return();
            glyphScales.Return();
        }

        /// <summary>
        /// Returns all instance buffers to the pool and clears the generated data flag.
        /// </summary>
        /// <remarks>
        /// Must be called after mesh generation is complete and data has been applied to Unity meshes.
        /// Failing to call this method will result in buffer leaks.
        /// </remarks>
        public void ReturnInstanceBuffers()
        {
            vertices.Return();
            uvs0.Return();
            uvs1.Return();
            uvs2.Return();
            uvs3.Return();
            preClaimAlpha.Return();
            glyphScales.Return();
            colors.Return();
            triangles.Return();
            sdfRuns.Return();
            hasGeneratedData = false;
        }

        /// <summary>
        /// Releases all pooled resources. Call when the generator is no longer needed.
        /// </summary>
        public void Dispose()
        {
            ReturnInstanceBuffers();
            usedGlyphKeys.Return();
            usedColorKeys.Return();
            usedFieldKeys.Return();
            mainPassComplete?.Release();
            mainPassFinalize?.Release();
            glyphEvent?.Release();
            rebuildStart?.Release();
            rebuildEnd?.Release();
            collectSubMeshes?.Release();
        }

        /// <summary>
        /// Ensures the vertex and triangle buffers have capacity for additional data.
        /// </summary>
        /// <param name="additionalVertices">Number of additional vertices needed.</param>
        /// <param name="additionalTriangles">Number of additional triangle indices needed.</param>
        /// <remarks>
        /// Called by text modifiers when they need to add geometry beyond the base glyph quads.
        /// Automatically grows buffers using the pooled array system if needed.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void EnsureCapacity(int additionalVertices, int additionalTriangles)
        {
            var requiredVertices = vertexCount + additionalVertices;
            var requiredTriangles = triangleCount + additionalTriangles;

            if (requiredVertices > vertices.Capacity)
                GrowVertexBuffers(requiredVertices);

            if (requiredTriangles > triangles.Capacity)
                GrowTriangleBuffer(requiredTriangles);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowVertexBuffers(int required)
        {
            var newCapacity = Math.Max(required, vertices.Capacity * 2);
            var currentCount = vertexCount;

            vertices.Grow(newCapacity, currentCount);
            uvs0.Grow(newCapacity, currentCount);
            uvs1.Grow(newCapacity, currentCount);
            uvs2.GrowCleared(newCapacity, currentCount);
            uvs3.GrowCleared(newCapacity, currentCount);
            preClaimAlpha.GrowCleared(newCapacity, currentCount);
            glyphScales.GrowCleared(newCapacity, currentCount);
            colors.Grow(newCapacity, currentCount);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void GrowTriangleBuffer(int required)
        {
            var newCapacity = Math.Max(required, triangles.Capacity * 2);
            triangles.Grow(newCapacity, triangleCount);
        }

        #endregion

        #region Sequenced SDF Triangles

        /// <summary>
        /// Resets the per-glyph state that <see cref="onGlyph"/> modifiers read and write — the claim race
        /// (<see cref="fillClaimedThisGlyph"/>/<see cref="baseFaceClaimed"/>), the claimed
        /// <see cref="claimedFillSequence"/>/<see cref="claimedFillBlend"/>, and the tier-upgrade
        /// <see cref="currentMaxGlyphExtent"/>.
        /// Every emitter of a glyph quad (face, kashida, decoration line) calls this before firing onGlyph.
        /// </summary>
        public void ResetPerGlyphState()
        {
            currentMaxGlyphExtent = 0f;
            currentTileSizeBoost = 0;
            currentGlyphScale = 1f;
            fillClaimedThisGlyph = false;
            claimedFillSequence = DefaultFillSequence;
            claimedFillBlend = LayerBlend.Normal;
            baseFaceClaimed = false;
            currentPositionedIndex = -1;
            hasCurrentColorField = false;
        }

        /// <summary>
        /// Records a quad (4 vertices at <paramref name="baseVertexIdx"/>) of the current segment (SDF or
        /// color) to be drawn at layer <paramref name="sequence"/> with <paramref name="blend"/>. Triangles
        /// are written in sequence order by <see cref="MaterializeQuadTriangles"/>, so painter order follows
        /// the layer stack; glyph-major ranges (<see cref="UniTextBuffers.paintOrders"/>) group a glyph's
        /// quads together first, ordering layers within each glyph. The quad's owning cluster is read from
        /// its UV1 <c>z</c> channel (see <see cref="Uvs1"/>), which duplicate reservation copies from the
        /// source face.
        /// </summary>
        public void AddSdfQuad(int sequence, int baseVertexIdx, LayerBlend blend = LayerBlend.Normal)
        {
            sdfQuads.Add(new SdfQuad { sequence = sequence, baseIdx = baseVertexIdx, blend = blend });
        }

        /// <summary>
        /// Fully-plain fast path: when no per-glyph modifier, post-pass decoration hook or color glyph can
        /// contribute a quad, every recorded quad would be the contiguous face quad at
        /// <see cref="DefaultFillSequence"/> with base vertex <c>k*4</c>. Emits their triangles directly and
        /// records the single full-span run — byte-identical to routing them through <see cref="AddSdfQuad"/>
        /// then <see cref="MaterializeQuadTriangles"/>, but without the intermediate sdfQuads round-trip and
        /// its min/max scan.
        /// </summary>
        private void EmitContiguousQuadRun(int quadCount)
        {
            sdfRunCount = 0;
            if (quadCount == 0) return;

            var triStart = triangleCount;
            EnsureCapacity(0, quadCount * 6);
            var tris = triangles.data;
            var t = triStart;
            for (var k = 0; k < quadCount; k++)
            {
                var b = k * 4;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b + 2; tris[t + 4] = b + 3; tris[t + 5] = b;
                t += 6;
            }
            triangleCount = t;

            sdfRuns.EnsureCapacity(1);
            sdfRuns.data[0] = new SdfRun
            {
                sequence = DefaultFillSequence,
                triStart = triStart,
                vertMin = 0,
                vertMax = quadCount * 4,
                blend = LayerBlend.Normal,
            };
            sdfRunCount = 1;
        }

        /// <summary>Guard for the counting-sort bucket array: legitimate sequences are dense (style-stack
        /// positions), so the range never approaches this. A rogue <see cref="claimedFillSequence"/> from a
        /// third-party modifier (e.g. 1 000 000) would otherwise rent O(range) ints per worker thread;
        /// instead sequences past the cap collapse into the last bucket (stable among themselves) with a
        /// one-time warning.</summary>
        private const int MaxSequenceRange = 4096;

        /// <summary>
        /// Stable counting-sort of recorded quads by layer sequence; appends their triangles (with
        /// ABSOLUTE vertex indices — rebased to slice-relative at collect) at the current
        /// <see cref="triangleCount"/> and records per-sequence run boundaries plus each run's vertex span
        /// (consumed by <see cref="CollectRenderData"/> to slice the mesh where a separate-material
        /// sub-mesh interleaves, uploading only the vertices a run references). The ordering axis is
        /// layer order; ranges marked glyph-major in <see cref="UniTextBuffers.paintOrders"/> sort by
        /// (composition group, layer) instead, so each such glyph stacks its own layers before the next
        /// glyph draws. Consumes and resets the recorded quads, so the SDF and color segments call it
        /// once each. When every quad shares one layer sequence and blend (the common no-effects text), a fast
        /// path skips the sort and emits a single full-span run — byte-identical to the sorted result.
        /// </summary>
        private void MaterializeQuadTriangles(ref PooledBuffer<SdfRun> runsBuffer, out int runCount)
        {
            runCount = 0;
            var n = sdfQuads.count;
            if (n == 0) return;
            sdfQuads.FakeClear();

            var quads = sdfQuads.data;
            var minSeq = int.MaxValue;
            var maxSeq = int.MinValue;
            var firstBlend = quads[0].blend;
            var singleBlend = true;
            for (var i = 0; i < n; i++)
            {
                var s = quads[i].sequence;
                if (s < minSeq) minSeq = s;
                if (s > maxSeq) maxSeq = s;
                if (quads[i].blend != firstBlend) singleBlend = false;
            }

            if (minSeq == maxSeq && singleBlend)
            {
                var fastTriStart = triangleCount;
                EnsureCapacity(0, n * 6);
                var fastTris = triangles.data;
                var fastT = fastTriStart;
                var vMin = int.MaxValue;
                var vMax = 0;
                for (var i = 0; i < n; i++)
                {
                    var b = quads[i].baseIdx;
                    fastTris[fastT] = b; fastTris[fastT + 1] = b + 1; fastTris[fastT + 2] = b + 2;
                    fastTris[fastT + 3] = b + 2; fastTris[fastT + 4] = b + 3; fastTris[fastT + 5] = b;
                    fastT += 6;
                    if (b < vMin) vMin = b;
                    if (b + 4 > vMax) vMax = b + 4;
                }
                triangleCount = fastT;

                runsBuffer.EnsureCapacity(1);
                runsBuffer.data[0] = new SdfRun
                {
                    sequence = minSeq,
                    triStart = fastTriStart,
                    vertMin = vMin,
                    vertMax = vMax,
                    blend = firstBlend,
                };
                runCount = 1;
                return;
            }

            if (TryMaterializeGlyphMajor(quads, n, minSeq, maxSeq, ref runsBuffer, out runCount))
                return;

            var range = SequenceBucketRange(minSeq, maxSeq);

            sdfSeqCounts.EnsureCapacity(range);
            var counts = sdfSeqCounts.data;
            for (var i = 0; i < range; i++) counts[i] = 0;
            for (var i = 0; i < n; i++)
            {
                var idx = quads[i].sequence - minSeq;
                if ((uint)idx >= (uint)range) idx = range - 1;
                counts[idx]++;
            }

            var acc = 0;
            for (var i = 0; i < range; i++) { var c = counts[i]; counts[i] = acc; acc += c; }

            sdfSortedQuads.EnsureCapacity(n);
            var sorted = sdfSortedQuads.data;
            for (var i = 0; i < n; i++)
            {
                ref readonly var q = ref quads[i];
                var idx = q.sequence - minSeq;
                if ((uint)idx >= (uint)range) idx = range - 1;
                sorted[counts[idx]++] = new SortedSdfQuad { baseIdx = q.baseIdx, blend = q.blend };
            }

            var triStartBase = triangleCount;
            EnsureCapacity(0, n * 6);
            var tris = triangles.data;
            var t = triStartBase;
            for (var i = 0; i < n; i++)
            {
                var b = sorted[i].baseIdx;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b + 2; tris[t + 4] = b + 3; tris[t + 5] = b;
                t += 6;
            }
            triangleCount = t;

            runsBuffer.EnsureCapacity(n);
            var runs = runsBuffer.data;
            var prevEnd = 0;
            for (var i = 0; i < range; i++)
            {
                var end = counts[i];
                var runStart = prevEnd;
                while (runStart < end)
                {
                    var blend = sorted[runStart].blend;
                    var runEnd = runStart + 1;
                    while (runEnd < end && sorted[runEnd].blend == blend) runEnd++;

                    var vMin = int.MaxValue;
                    var vMax = 0;
                    for (var k = runStart; k < runEnd; k++)
                    {
                        var b = sorted[k].baseIdx;
                        if (b < vMin) vMin = b;
                        if (b + 4 > vMax) vMax = b + 4;
                    }
                    runs[runCount++] = new SdfRun
                    {
                        sequence = minSeq + i,
                        triStart = triStartBase + runStart * 6,
                        vertMin = vMin,
                        vertMax = vMax,
                        blend = blend,
                    };
                    runStart = runEnd;
                }
                prevEnd = end;
            }
        }

        /// <summary>Counting-sort bucket range for a sequence span, clamped to <see cref="MaxSequenceRange"/> with the one-time outlier warning.</summary>
        private static int SequenceBucketRange(int minSeq, int maxSeq)
        {
            var range = maxSeq - minSeq + 1;
            if (range > MaxSequenceRange || range < 0)
            {
                CatZones.meshGenerator.MeowWarnOnce("MeshGen.SeqRange",
                    "[MeshGenerator] Layer sequence range {0} (min {1}, max {2}) exceeds {3}; outlier sequences share the top layer bucket",
                    range, minSeq, maxSeq, MaxSequenceRange);
                range = MaxSequenceRange;
            }
            return range;
        }

        /// <summary>
        /// Glyph-major branch of <see cref="MaterializeQuadTriangles"/>, taken when
        /// <see cref="UniTextBuffers.paintOrders"/> marks any codepoint <see cref="PaintOrder.Glyph"/>.
        /// Quads sort by (composition group, layer sequence): each contiguous layer-major span is one
        /// group ordered by layer inside, and each glyph-major codepoint is its own group, so a later
        /// glyph's layers draw over every earlier glyph. Clusters outside the mode buffer join the
        /// trailing layer-major group, and quads in the overlay band (<see cref="overlayBandStart"/>)
        /// form the final group above every glyph. Runs split on blend changes always, and on sequence changes only
        /// outside glyph-major quads (preserving separate-material interleaving there); emitted run
        /// sequences are clamped monotonic so <see cref="CollectRenderData"/>'s stable sequence sort
        /// keeps draw order.
        /// </summary>
        private bool TryMaterializeGlyphMajor(SdfQuad[] quads, int n, int minSeq, int maxSeq,
            ref PooledBuffer<SdfRun> runsBuffer, out int runCount)
        {
            runCount = 0;
            var modeCount = buf.paintOrders.count;
            if (modeCount == 0) return false;

            var modeData = buf.paintOrders.data;
            var anyGlyph = false;
            for (var c = 0; c < modeCount; c++)
            {
                if (modeData[c] != (byte)PaintOrder.Glyph) continue;
                anyGlyph = true;
                break;
            }
            if (!anyGlyph) return false;

            sdfClusterGroups.EnsureCapacity(modeCount);
            var groupOf = sdfClusterGroups.data;
            var group = 0;
            for (var c = 0; c < modeCount; c++)
            {
                if (c > 0 && (modeData[c] != modeData[c - 1] || modeData[c] == (byte)PaintOrder.Glyph))
                    group++;
                groupOf[c] = group;
            }
            var trailingGroup = modeData[modeCount - 1] == (byte)PaintOrder.Glyph ? group + 1 : group;
            var overlayGroup = trailingGroup + 1;
            var groupCount = overlayGroup + 1;

            var seqRange = SequenceBucketRange(minSeq, maxSeq);
            sdfSeqCounts.EnsureCapacity(seqRange);
            var counts = sdfSeqCounts.data;
            for (var i = 0; i < seqRange; i++) counts[i] = 0;
            for (var i = 0; i < n; i++)
            {
                var idx = quads[i].sequence - minSeq;
                if ((uint)idx >= (uint)seqRange) idx = seqRange - 1;
                counts[idx]++;
            }
            var acc = 0;
            for (var i = 0; i < seqRange; i++) { var c = counts[i]; counts[i] = acc; acc += c; }

            sdfGroupedQuads.EnsureCapacity(n);
            var bySeq = sdfGroupedQuads.data;
            var uv1 = uvs1.data;
            for (var i = 0; i < n; i++)
            {
                ref readonly var q = ref quads[i];
                var idx = q.sequence - minSeq;
                if ((uint)idx >= (uint)seqRange) idx = seqRange - 1;

                var cluster = (int)uv1[q.baseIdx].z;
                int quadGroup;
                var glyphMajor = false;
                if (q.sequence >= overlayBandStart)
                {
                    quadGroup = overlayGroup;
                }
                else if ((uint)cluster < (uint)modeCount)
                {
                    quadGroup = groupOf[cluster];
                    glyphMajor = modeData[cluster] == (byte)PaintOrder.Glyph;
                }
                else
                {
                    quadGroup = trailingGroup;
                }

                bySeq[counts[idx]++] = new GroupedSdfQuad
                {
                    baseIdx = q.baseIdx,
                    sequence = q.sequence,
                    group = quadGroup,
                    blend = q.blend,
                    glyphMajor = glyphMajor,
                };
            }

            sdfGroupCounts.EnsureCapacity(groupCount);
            var gCounts = sdfGroupCounts.data;
            for (var i = 0; i < groupCount; i++) gCounts[i] = 0;
            for (var i = 0; i < n; i++) gCounts[bySeq[i].group]++;
            acc = 0;
            for (var i = 0; i < groupCount; i++) { var c = gCounts[i]; gCounts[i] = acc; acc += c; }

            sdfGroupedSorted.EnsureCapacity(n);
            var sorted = sdfGroupedSorted.data;
            for (var i = 0; i < n; i++)
                sorted[gCounts[bySeq[i].group]++] = bySeq[i];

            var triStartBase = triangleCount;
            EnsureCapacity(0, n * 6);
            var tris = triangles.data;
            var t = triStartBase;
            for (var i = 0; i < n; i++)
            {
                var b = sorted[i].baseIdx;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b + 2; tris[t + 4] = b + 3; tris[t + 5] = b;
                t += 6;
            }
            triangleCount = t;

            runsBuffer.EnsureCapacity(n);
            var runs = runsBuffer.data;
            var runStart = 0;
            var lastEmittedSeq = int.MinValue;
            while (runStart < n)
            {
                ref readonly var first = ref sorted[runStart];
                var runEnd = runStart + 1;
                var vMin = first.baseIdx;
                var vMax = first.baseIdx + 4;
                while (runEnd < n)
                {
                    ref readonly var cur = ref sorted[runEnd];
                    ref readonly var prev = ref sorted[runEnd - 1];
                    if (cur.blend != prev.blend ||
                        cur.sequence != prev.sequence && !(cur.glyphMajor && prev.glyphMajor))
                        break;
                    if (cur.baseIdx < vMin) vMin = cur.baseIdx;
                    if (cur.baseIdx + 4 > vMax) vMax = cur.baseIdx + 4;
                    runEnd++;
                }

                var emitSeq = first.sequence > lastEmittedSeq ? first.sequence : lastEmittedSeq;
                lastEmittedSeq = emitSeq;
                runs[runCount++] = new SdfRun
                {
                    sequence = emitSeq,
                    triStart = triStartBase + runStart * 6,
                    vertMin = vMin,
                    vertMax = vMax,
                    blend = first.blend,
                };
                runStart = runEnd;
            }
            return true;
        }

        #endregion

        /// <summary>
        /// Raises the current glyph's atlas pad tier when a modifier asked for more outward rim than the
        /// tier reserves — the glyph is re-rasterized smaller in its shared tile so the extra distance field
        /// (outline / shadow / bold) has room, plus <see cref="GlyphAtlas.TierSeamMarginNorm"/> of AA-ramp
        /// headroom so the effect's outer edge samples inside computed field, not at the tile seam. Only the
        /// request is recorded here; the sweep re-rasterizes the tile and re-meshes every consumer of it at
        /// the new tier in the same frame (see UniTextBase_Parallel). Grow-only.
        /// </summary>
        /// <remarks>
        /// Call immediately after <see cref="onGlyph"/>. The required rim is the face dilate (bold, from
        /// <see cref="Uvs1"/>[<see cref="faceBaseIdx"/>].y) plus <see cref="currentMaxGlyphExtent"/> (the
        /// effects' outward extent) — so bold and effects stack regardless of modifier order.
        /// </remarks>
        public void RequestTierUpgradeIfNeeded(long glyphKey, uint glyphIndex, in GlyphAtlas.GlyphEntry entry,
            UniTextFont.Core font, long varHash48, int[] ftCoords, float glyphH, float aspect)
        {
            if (glyphH < 1e-6f) return;

            var faceDilate = uvs1[faceBaseIdx].y;
            var requiredPad = faceDilate * (GlyphAtlas.Pad / glyphH) + currentMaxGlyphExtent;
            if (requiredPad <= DefaultSdfPadding) return;

            var requiredTier = (byte)GlyphAtlas.PadTierForExtent(
                requiredPad + GlyphAtlas.TierSeamMarginNorm, glyphH);
            if (requiredTier <= entry.padTier) return;

            tierUpgradeRequests.Add(new TierUpgradeRequest
            {
                glyphKey = glyphKey,
                glyphIndex = glyphIndex,
                requiredTier = requiredTier,
                font = font,
                varHash48 = varHash48,
                ftCoords = ftCoords,
                mode = RenderMode
            });
        }

        /// <summary>
        /// The colour-face counterpart of <see cref="RequestTierUpgradeIfNeeded"/> and
        /// <see cref="RequestTileSizeUpgradeIfNeeded"/>: records the pad-tier and tile-size upgrades the
        /// current glyph's silhouette field needs after its effects ran. A colour face carries no dilate,
        /// so the required rim is the effects' outward extent alone; the requests address the SDF atlas.
        /// </summary>
        private void RequestFieldUpgradesIfNeeded(in ColorFaceField field)
        {
            if (currentMaxGlyphExtent > DefaultSdfPadding)
            {
                var requiredTier = (byte)GlyphAtlas.PadTierForExtent(
                    currentMaxGlyphExtent + GlyphAtlas.TierSeamMarginNorm, field.glyphH);
                if (requiredTier > field.padTier)
                    tierUpgradeRequests.Add(new TierUpgradeRequest
                    {
                        glyphKey = field.key,
                        glyphIndex = field.glyphIndex,
                        requiredTier = requiredTier,
                        font = field.font,
                        varHash48 = field.fieldVarHash,
                        ftCoords = null,
                        mode = UniTextRenderMode.SDF
                    });
            }

            if (currentTileSizeBoost > 0)
                tileSizeUpgradeRequests.Add(new TileSizeUpgradeRequest
                {
                    glyphKey = field.key,
                    glyphIndex = field.glyphIndex,
                    tileSizeBoost = currentTileSizeBoost,
                    font = field.font,
                    varHash48 = field.fieldVarHash,
                    ftCoords = null,
                    mode = UniTextRenderMode.SDF
                });
        }

        /// <summary>
        /// Records a grow-only atlas tile-size upgrade for the current glyph when a resolution modifier asked
        /// for more detail / a larger tile than the font default. The sweep relocates the shared tile to the
        /// larger class and re-meshes its consumers the same frame (see UniTextBase_Parallel). The glyph's
        /// atlas key ignores tile size, so all consumers share one tile at the max requested resolution.
        /// Call immediately after <see cref="onGlyph"/>.
        /// </summary>
        public void RequestTileSizeUpgradeIfNeeded(long glyphKey, uint glyphIndex,
            UniTextFont.Core font, long varHash48, int[] ftCoords)
        {
            if (currentTileSizeBoost <= 0) return;
            tileSizeUpgradeRequests.Add(new TileSizeUpgradeRequest
            {
                glyphKey = glyphKey,
                glyphIndex = glyphIndex,
                tileSizeBoost = currentTileSizeBoost,
                font = font,
                varHash48 = varHash48,
                ftCoords = ftCoords,
                mode = RenderMode
            });
        }

        #region Quad Modification API

        /// <summary>
        /// Writes the font-level fake-bold dilate into the quad's UV1 channel and, when the
        /// dilation would sample outside the default SDF band, expands the quad to fit.
        /// </summary>
        private void ApplyFontFakeBold(Vector4[] uv1Data, int baseIdx, float glyphH, float dilate)
        {
            uv1Data[baseIdx].y     = dilate;
            uv1Data[baseIdx + 1].y = dilate;
            uv1Data[baseIdx + 2].y = dilate;
            uv1Data[baseIdx + 3].y = dilate;

            if (glyphH < 1e-6f) return;

            var padGlyph = GlyphAtlas.Pad / glyphH;
            var facePad = dilate * padGlyph;
            var effectivePad = facePad < padGlyph ? facePad : padGlyph;

            var delta = effectivePad - DefaultSdfPadding;
            if (delta > 0f)
                ExpandQuad(baseIdx, delta);
        }

        /// <summary>
        /// Expands a 4-vertex quad outward along its current local axes: UV0 by <paramref name="delta"/>
        /// (atlas-space, normalized by glyph height), positions by the matching distance in the quad's own
        /// UV0→pixel scale, so the SDF sample stays attached to the geometry whatever size the quad was
        /// built or scaled to.
        /// </summary>
        public void ExpandQuad(int baseIdx, float delta)
        {
            if (delta <= 0f) return;

            ExpandQuad(vertices.data, uvs0.data, baseIdx, delta);
        }

        /// <summary>
        /// Expands a 4-vertex quad outward on all sides by <paramref name="uvDelta"/> UV0 units, moving each
        /// corner by that step through the quad's own UV0→pixel scale — its vertical pixel extent over its
        /// vertical UV0 extent. A quad sized independently of the glyph it samples (a decoration line built
        /// at an explicit thickness) therefore reaches its own iso-line instead of the sampled glyph's. The
        /// scale is invariant under this operation, so repeated expansions of one quad compose exactly.
        /// </summary>
        internal static void ExpandQuad(Vector3[] verts, Vector4[] uvData, int baseIdx, float uvDelta)
        {
            ref var bottomLeft = ref verts[baseIdx];
            ref var topLeft = ref verts[baseIdx + 1];
            ref var topRight = ref verts[baseIdx + 2];
            ref var bottomRight = ref verts[baseIdx + 3];

            var horizontalAxis = NormalizeAxis(
                bottomRight.x - bottomLeft.x + topRight.x - topLeft.x,
                bottomRight.y - bottomLeft.y + topRight.y - topLeft.y, out _);
            var verticalAxis = NormalizeAxis(
                topLeft.x - bottomLeft.x + topRight.x - bottomRight.x,
                topLeft.y - bottomLeft.y + topRight.y - bottomRight.y, out var verticalLength);

            var pixelsPerUv = verticalLength * 0.5f / (uvData[baseIdx + 1].y - uvData[baseIdx].y);
            var positionDelta = uvDelta * pixelsPerUv;

            var horizontal = horizontalAxis * positionDelta;
            var vertical = verticalAxis * positionDelta;

            Offset(ref bottomLeft, -horizontal.x - vertical.x, -horizontal.y - vertical.y);
            Offset(ref topLeft, vertical.x - horizontal.x, vertical.y - horizontal.y);
            Offset(ref topRight, horizontal.x + vertical.x, horizontal.y + vertical.y);
            Offset(ref bottomRight, horizontal.x - vertical.x, horizontal.y - vertical.y);

            uvData[baseIdx].x -= uvDelta;
            uvData[baseIdx].y -= uvDelta;
            uvData[baseIdx + 1].x -= uvDelta;
            uvData[baseIdx + 1].y += uvDelta;
            uvData[baseIdx + 2].x += uvDelta;
            uvData[baseIdx + 2].y += uvDelta;
            uvData[baseIdx + 3].x += uvDelta;
            uvData[baseIdx + 3].y -= uvDelta;
        }

        private static Vector2 NormalizeAxis(float x, float y, out float length)
        {
            if (y == 0f)
            {
                length = x < 0f ? -x : x;
                return x == 0f ? default : new Vector2(x > 0f ? 1f : -1f, 0f);
            }
            if (x == 0f)
            {
                length = y < 0f ? -y : y;
                return new Vector2(0f, y > 0f ? 1f : -1f);
            }
            length = Mathf.Sqrt(x * x + y * y);
            var inverseLength = 1f / length;
            return new Vector2(x * inverseLength, y * inverseLength);
        }

        private static void Offset(ref Vector3 vertex, float x, float y)
        {
            vertex.x += x;
            vertex.y += y;
        }

        #endregion

        /// <summary>
        /// Sets the layout rectangle for text positioning.
        /// </summary>
        /// <param name="rect">The rect defining the text layout bounds.</param>
        public void SetRectOffset(Rect rect)
        {
            rectOffset = rect;
        }

        #region Parallel Mesh Generation

#if UNITEXT_TESTS
        /// <summary>Quads emitted by the last <see cref="GenerateMeshDataOnly"/> for glyphs with a real glyph id; .notdef (glyphId 0) is excluded.</summary>
        public int RenderedGlyphCount { get; private set; }
#endif

        private float visibleLocalYMin = float.NegativeInfinity;
        private float visibleLocalYMax = float.PositiveInfinity;

        /// <summary>
        /// Bounds quad emission to a local-space vertical band (viewport culling). Geometry queries,
        /// selection and hit-testing are untouched — positioned glyphs exist for every paragraph;
        /// only mesh emission skips bands outside the window. Reset to infinite for no culling.
        /// </summary>
        public void SetVisibleBand(float localYMin, float localYMax)
        {
            visibleLocalYMin = localYMin;
            visibleLocalYMax = localYMax;
        }

        /// <summary>True when a local-space vertical span intersects the visible band — the one culling test every emission site shares (glyph paragraphs, virtual glyphs, decoration lines).</summary>
        public bool IsLocalBandVisible(float localYLow, float localYHigh)
            => localYHigh >= visibleLocalYMin && localYLow <= visibleLocalYMax;

        /// <summary>
        /// Generates mesh data (vertices, UVs, colors, triangles) from positioned glyphs.
        /// Groups by rendering target: SDF fonts in one segment (Texture2DArray), color separately.
        /// </summary>
        public void GenerateMeshDataOnly(ReadOnlySpan<PositionedGlyph> glyphs, ReadOnlySpan<PositionedGlyph> virtualGlyphs)
            => GenerateMeshDataOnly(glyphs, virtualGlyphs, ReadOnlySpan<Paragraph>.Empty);

        /// <summary>Pipeline entry: same as the public overload, plus viewport culling — paragraphs whose vertical band misses the visible window emit nothing.</summary>
        internal void GenerateMeshDataOnly(ReadOnlySpan<PositionedGlyph> glyphs, ReadOnlySpan<PositionedGlyph> virtualGlyphs,
            ReadOnlySpan<Paragraph> paragraphs)
        {
            sdfQuads.FakeClear();
            sdfRunCount = 0;
            runsRebased = false;
            cachedSegmentEntries.Clear();
            filters.BeginRebuild();
            rebuildStart?.Invoke();
            if (captureGlyphGeometry) onGlyphGeometryRebuildStart?.Invoke();
            var glyphLen = glyphs.Length + virtualGlyphs.Length;
            usedGlyphKeys.FakeClear();
            usedGlyphKeys.EnsureCapacity(glyphLen);
            usedColorKeys.FakeClear();
            usedColorKeys.EnsureCapacity(glyphLen);
            usedFieldKeys.FakeClear();
            colorFaceFields?.Clear();
            hasCurrentColorField = false;
            missingAtlasGlyphs = false;
            tierUpgradeRequests.Clear();
            tileSizeUpgradeRequests.Clear();

            glyphEntryCache ??= new FastLongDictionary<GlyphAtlas.GlyphEntry>(512);
            glyphEntryCache.ClearFast();
            var estimatedVertices = glyphLen * 4;
            var estimatedTriangles = glyphLen * 6;
            PositionedGlyph[] allGlyphs = null;
            PooledList<int> colorGlyphList = null;
            var completed = false;
            try
            {
            RentInstanceBuffers(estimatedVertices, estimatedTriangles);

            allGlyphs = ArrayPool<PositionedGlyph>.Rent(glyphLen);
            glyphs.CopyTo(allGlyphs);
            if (virtualGlyphs.Length > 0)
                virtualGlyphs.CopyTo(allGlyphs.AsSpan(glyphs.Length));

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;
            offsetX = offX;
            offsetY = offY;
            vertexCount = 0;
            triangleCount = 0;
#if UNITEXT_TESTS
            RenderedGlyphCount = 0;
#endif

            var atlas = GlyphAtlas.GetInstance(RenderMode);
            var skippedGlyphs = 0;
            var lastSdfFontId = int.MinValue;

            var lastFontId = int.MinValue;
            UniTextFont.Core lastFont = null;
            long lastVarHash = 0;
            int[] lastFtCoords = null;
            var lastIsColor = false;

            float upem = 0, invUpem = 0, metricsFactor = 0;
            float fontFakeBoldDilate = 0f;
            var glyphColor = defaultColor;

            var hiddenFlags = buf.hiddenClusters.data;
            var hiddenCount = buf.hiddenClusters.count;

            var xScales = buf.glyphXScales.count > 0 ? buf.glyphXScales.data : null;
            var xScaleCount = buf.glyphXScales.count;

            var cullActive = !paragraphs.IsEmpty &&
                             (!float.IsNegativeInfinity(visibleLocalYMin) || !float.IsPositiveInfinity(visibleLocalYMax));
            var cullParaIdx = 0;
            var cullPad = FontSize * 4f;

            if (cullActive && CatZones.meshGenerator.Enabled)
            {
                float paraLocalTop = offY - paragraphs[0].topY;
                float paraLocalBottom = offY - paragraphs[paragraphs.Length - 1].bottomY;
                CatZones.meshGenerator.MeowFormat(
                    "[MeshGen] cull band '{0}': band=[{1:F1},{2:F1}], offY={3:F1}, cullPad={4:F1}, paraCount={5}, paraLocalY=[{6:F1},{7:F1}]",
                    debugName, visibleLocalYMin, visibleLocalYMax, offY, cullPad, paragraphs.Length, paraLocalBottom, paraLocalTop);
            }

            var needsExtras = glyphEvent?.HasSubscribers == true || !filters.IsEmpty;
            quadInputs.FakeClear();
            quadInputs.EnsureCapacity(glyphLen);
            quadSrc.FakeClear();
            quadSrc.EnsureCapacity(glyphLen);

            UniTextDebug.BeginSample("Mesh.PreResolve");
            for (var i = 0; i < glyphLen; i++)
            {
                if (cullActive)
                {
                    if (i < glyphs.Length)
                    {
                        while (cullParaIdx < paragraphs.Length &&
                               i >= paragraphs[cullParaIdx].posStart + paragraphs[cullParaIdx].posCount)
                            cullParaIdx++;

                        if (cullParaIdx < paragraphs.Length)
                        {
                            ref readonly var para = ref paragraphs[cullParaIdx];
                            if (!IsLocalBandVisible(offY - para.bottomY - cullPad, offY - para.topY + cullPad))
                            {
                                i = para.posStart + para.posCount - 1;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        var localY = offY - allGlyphs[i].y;
                        if (localY < visibleLocalYMin - cullPad || localY > visibleLocalYMax + cullPad)
                            continue;
                    }
                }

                ref var glyph = ref allGlyphs[i];

                if (glyph.scale > 0f && glyph.scale != 1f)
                    needsExtras = true;

                if (xScales != null && (uint)glyph.cluster < (uint)xScaleCount)
                {
                    var xs = xScales[glyph.cluster];
                    if (xs != 0f && xs != 1f)
                        needsExtras = true;
                }

                if (glyph.glyphId == ShapedGlyph.NoGlyph ||
                    (glyph.shapedGlyphIndex >= 0 &&
                     (uint)glyph.cluster < (uint)hiddenCount &&
                     hiddenFlags[glyph.cluster] != 0))
                    continue;

                var glyphFontId = glyph.fontId;

                if (glyphFontId != lastFontId)
                {
                    lastFontId = glyphFontId;
                    lastFont = fontProvider.GetFont(glyphFontId);
                    if (buf.variationMap != null &&
                        buf.variationMap.TryGetValue(glyphFontId, out var varRunInfo))
                    {
                        lastVarHash = varRunInfo.varHash48;
                        lastFtCoords = varRunInfo.ftCoords;
                    }
                    else
                    {
                        lastVarHash = lastFont.DefaultVarHash48;
                        lastFtCoords = null;
                    }
                    lastIsColor = lastFont.IsColor;

                    if (!lastIsColor)
                    {
                        font = lastFont;

                        upem = lastFont.UnitsPerEm;
                        invUpem = upem > 0f ? 1f / upem : 0f;
                        scale = fontProvider.MetricScale(lastFont, FontSize);
                        metricsFactor = scale * upem;
                        fontMetricFactor = metricsFactor;
                        fontFakeBoldDilate = lastFont.FakeBoldWeight > 0f
                            ? lastFont.FakeBoldWeight * FontStyleEncoding.EmboldenRatio
                            : 0f;

                        glyphColor = lastFont.IsColor
                            ? new Color32(255, 255, 255, defaultColor.a)
                            : defaultColor;

                        if (fontFakeBoldDilate > 0f || lastFont.HasGlyphMetricOverrides)
                            needsExtras = true;
                    }
                }

                if (lastIsColor)
                {
                    colorGlyphList ??= SharedPipelineComponents.AcquireGlyphIndexList(glyphLen);
                    colorGlyphList.buffer[colorGlyphList.buffer.count++] = i;
                    continue;
                }

                var glyphId = (uint)glyph.glyphId;
                var glyphKey = GlyphAtlas.MakeKey(lastVarHash, glyphId);

                ref var entry = ref TryGetCachedGlyphEntryRef(glyphKey, out var cached);
                GlyphMetrics metrics;
                int tileHandle;
                if (cached)
                {
                    metrics = entry.metrics;
                    tileHandle = entry.handle;
                }
                else
                {
                    ref var atlasEntry = ref atlas.TryGetEntryRef(glyphKey, out var inAtlas);
                    if (!inAtlas || atlasEntry.encodedTile < 0)
                    {
                        var diagEncodedTile = inAtlas ? atlasEntry.encodedTile : -1;
                        skippedGlyphs++;
                        if (lastFont.IsNonEmptyGlyph(glyphKey))
                            missingAtlasGlyphs = true;
                        if (skippedGlyphs <= 6 && CatZones.meshGenerator.Enabled)
                        {
                            var defaultVar = lastFont.DefaultVarHash48;
                            bool foundUnderDefaultVar = lastVarHash != defaultVar
                                && atlas.TryGetEntry(GlyphAtlas.MakeKey(defaultVar, glyphId), out _);
                            CatZones.meshGenerator.MeowFormat(
                                "[MeshGen] skip '{0}': font={1}, gid={2}, lookupVarHash=0x{3:X}, defaultVarHash=0x{4:X}, hadEntry={5}, encodedTile={6}, foundUnderDefaultVar={7}",
                                debugName, lastFont.Name, glyphId, lastVarHash, defaultVar, inAtlas, diagEncodedTile, foundUnderDefaultVar);
                        }
                        continue;
                    }
                    CacheGlyphEntry(glyphKey, in atlasEntry);
                    metrics = atlasEntry.metrics;
                    tileHandle = atlasEntry.handle;
                }

                lastSdfFontId = glyphFontId;

                var glyphW = metrics.width * invUpem;
                var glyphH = metrics.height * invUpem;

                quadInputs.Add(new GlyphQuadInput
                {
                    x = glyph.x,
                    y = glyph.y,
                    cluster = glyph.cluster,
                    tileIdx = tileHandle,
                    bearingXNorm = metrics.horizontalBearingX * invUpem,
                    bearingYNorm = metrics.horizontalBearingY * invUpem,
                    glyphH = glyphH,
                    aspect = glyphH > 1e-6f ? glyphW / glyphH : 1f,
                    metricsFactor = metricsFactor,
                    color = glyphColor
                });
                quadSrc.Add(i);
#if UNITEXT_TESTS
                if (glyphId != 0) RenderedGlyphCount++;
#endif
            }
            UniTextDebug.EndSample();

            var quadCount = quadInputs.count;

            UniTextDebug.BeginSample("Mesh.BurstBuild");
            BuildBaseQuadsBurst(quadCount, offX, offY);
            vertexCount = quadCount * 4;
            if (!needsExtras && captureGlyphGeometry && captureIdentityGlyphGeometry &&
                onGlyphComplete != null)
                CaptureUndeformedGlyphGeometry(quadCount, allGlyphs);
            UniTextDebug.EndSample();

            var pureContiguous = !needsExtras && colorGlyphList == null
                                 && mainPassComplete?.HasSubscribers != true
                                 && mainPassFinalize?.HasSubscribers != true;
            if (pureContiguous)
            {
                EmitContiguousQuadRun(quadCount);
            }
            else if (!needsExtras)
            {
                for (var k = 0; k < quadCount; k++)
                    AddSdfQuad(DefaultFillSequence, k * 4);
            }
            else
            {
                UniTextDebug.BeginSample("Mesh.Extras");
                ApplyGlyphExtras(quadCount, allGlyphs, offX, offY);
                UniTextDebug.EndSample();
            }

            if (skippedGlyphs > 0)
                CatZones.meshGenerator.MeowFormat("[MeshGenerator] '{0}' SKIPPED {1} glyphs (not in atlas)", debugName, skippedGlyphs);

            if (vertexCount > 0)
                sdfFontId = lastSdfFontId;

            sdfVertexCount = vertexCount;

            var textFont = font;
            var textScale = scale;
            var textMetricFactor = fontMetricFactor;

            if (colorGlyphList != null)
            {
                UniTextDebug.BeginSample("Mesh.Color");
                var firstColorId = allGlyphs[colorGlyphList[0]].fontId;

                var colorCount = colorGlyphList.Count;
                var singleColorFont = true;
                for (var c = 1; c < colorCount; c++)
                    if (allGlyphs[colorGlyphList[c]].fontId != firstColorId) { singleColorFont = false; break; }

                if (singleColorFont)
                {
                    GenerateColorSegment(colorGlyphList, allGlyphs, fontProvider.GetFont(firstColorId));
                }
                else
                {
                    var group = SharedPipelineComponents.AcquireGlyphIndexList(colorCount);
                    try
                    {
                        for (var c = 0; c < colorCount; c++)
                        {
                            var gi = colorGlyphList[c];
                            if (gi < 0) continue;
                            var fid = allGlyphs[gi].fontId;
                            group.buffer.count = 0;
                            group.buffer[group.buffer.count++] = gi;
                            colorGlyphList.buffer[c] = -1;
                            for (var d = c + 1; d < colorCount; d++)
                            {
                                var gj = colorGlyphList[d];
                                if (gj < 0 || allGlyphs[gj].fontId != fid) continue;
                                group.buffer[group.buffer.count++] = gj;
                                colorGlyphList.buffer[d] = -1;
                            }
                            GenerateColorSegment(group, allGlyphs, fontProvider.GetFont(fid));
                        }
                    }
                    finally
                    {
                        SharedPipelineComponents.ReleaseGlyphIndexList(group);
                    }
                }

                if (sdfVertexCount == 0)
                    sdfFontId = firstColorId;

                SharedPipelineComponents.ReleaseGlyphIndexList(colorGlyphList);
                colorGlyphList = null;
                UniTextDebug.EndSample();

                font = textFont;
                scale = textScale;
                fontMetricFactor = textMetricFactor;
            }

            UniTextDebug.BeginSample("Mesh.PostPasses");
            mainPassComplete?.Invoke();
            mainPassFinalize?.Invoke();
            UniTextDebug.EndSample();

            if (!pureContiguous)
            {
                UniTextDebug.BeginSample("Mesh.Materialize");
                MaterializeQuadTriangles(ref sdfRuns, out sdfRunCount);
                UniTextDebug.EndSample();
            }

            vertices.count = vertexCount;
            uvs0.count = vertexCount;
            uvs1.count = vertexCount;
            if (uvs2.data != null) uvs2.count = vertexCount;
            if (uvs3.data != null) uvs3.count = vertexCount;
            colors.count = vertexCount;
            triangles.count = triangleCount;

            buf.hasValidGlyphCache = true;
            hasGeneratedData = true;

            CatZones.meshGenerator.MeowFormat("[MeshGenerator] '{0}' Generated: {1} verts, {2} tris, textFaces={3}, other={4}",
                debugName, vertices.count, triangles.count, sdfVertexCount, vertexCount - sdfVertexCount);

            rebuildEnd?.Invoke();
            completed = true;
            }
            finally
            {
                if (allGlyphs != null) ArrayPool<PositionedGlyph>.Return(allGlyphs);
                if (colorGlyphList != null) SharedPipelineComponents.ReleaseGlyphIndexList(colorGlyphList);
                if (!completed)
                {
                    buf.hasValidGlyphCache = false;
                    ReturnInstanceBuffers();
                }
            }
        }

        private unsafe void BuildBaseQuadsBurst(int count, float offX, float offY)
        {
            if (count <= 0) return;
            fixed (GlyphQuadInput* pin = quadInputs.data)
            fixed (Vector3* pv = vertices.data)
            fixed (Vector4* pu0 = uvs0.data)
            fixed (Vector4* pu1 = uvs1.data)
            fixed (Color32* pc = colors.data)
                UniTextQuadBurst.Build(pin, count, pv, pu0, pu1, pc, offX, offY, TextUv1wBias);
        }

        /// <summary>
        /// Applies the per-glyph receptor tail on top of the Burst-built base quads — font-metric overrides,
        /// font fake-bold, <see cref="onGlyph"/> modifiers, and tier/tile-size requests. Runs only when a font
        /// or modifier needs it; the pure path bulk-records the quads and skips this. Font context is
        /// re-resolved by source glyph index rather than carried through the blittable quad input.
        /// </summary>
        private void ApplyGlyphExtras(int count, PositionedGlyph[] allGlyphs, float offX, float offY)
        {
            var lastFid = int.MinValue;
            UniTextFont.Core efont = null;
            long evar = 0;
            int[] eftc = null;
            float eupem = 0, emf = 0, efbd = 0;

            var xScales = buf.glyphXScales.count > 0 ? buf.glyphXScales.data : null;
            var xScaleCount = buf.glyphXScales.count;

            for (var k = 0; k < count; k++)
            {
                ref var glyph = ref allGlyphs[quadSrc[k]];
                var fid = glyph.fontId;

                if (fid != lastFid)
                {
                    lastFid = fid;
                    efont = fontProvider.GetFont(fid);
                    if (buf.variationMap != null && buf.variationMap.TryGetValue(fid, out var varRunInfo))
                    {
                        evar = varRunInfo.varHash48;
                        eftc = varRunInfo.ftCoords;
                    }
                    else
                    {
                        evar = efont.DefaultVarHash48;
                        eftc = null;
                    }
                    font = efont;
                    eupem = efont.UnitsPerEm;
                    scale = fontProvider.MetricScale(efont, FontSize);
                    emf = scale * eupem;
                    fontMetricFactor = emf;
                    efbd = efont.FakeBoldWeight > 0f ? efont.FakeBoldWeight * FontStyleEncoding.EmboldenRatio : 0f;
                }

                var gid = (uint)glyph.glyphId;
                var gkey = GlyphAtlas.MakeKey(evar, gid);
                if (!TryGetCachedGlyphEntry(gkey, out var entry))
                    continue;

                var metrics = entry.metrics;
                var glyphW = metrics.width / eupem;
                var glyphH = metrics.height / eupem;
                var aspect = glyphH > 1e-6f ? glyphW / glyphH : 1f;
                var maxDim = MathF.Max(aspect, 1f);
                var baseIdx = k * 4;

                currentCluster = glyph.cluster;
                currentGlyphId = (int)gid;
                height = (maxDim + DefaultSdfPadding * 2f) * glyphH * emf;
                baselineY = offY - glyph.y;
                cursorX = offX + glyph.x;
                faceBaseIdx = baseIdx;
                ResetPerGlyphState();
                isVirtualGlyph = glyph.shapedGlyphIndex < 0;

                var intrinsicScale = glyph.scale > 0f ? glyph.scale : 1f;
                if (intrinsicScale != 1f)
                    ScaleFace(baseIdx, cursorX, baselineY, intrinsicScale);

                if (xScales != null && (uint)glyph.cluster < (uint)xScaleCount)
                {
                    var xs = xScales[glyph.cluster];
                    if (xs != 0f && xs != 1f)
                        XScaleFace(baseIdx, cursorX, xs);
                }

                if (efont.HasGlyphMetricOverrides &&
                    efont.TryGetGlyphQuadOverride(gid, out var govScale, out var govOffX, out var govOffY))
                    ScaleFace(baseIdx, cursorX, baselineY, govScale,
                        govOffY * emf * intrinsicScale, govOffX * emf * intrinsicScale);

                if (efbd > 0f)
                    ApplyFontFakeBold(uvs1.data, baseIdx, glyphH, efbd);

                InvokeGlyphModifiersAndComplete(quadSrc[k]);
                if (currentGlyphScale != 1f) StashGlyphScale(baseIdx);
                if (!baseFaceClaimed) AddSdfQuad(claimedFillSequence, baseIdx, claimedFillBlend);

                RequestTierUpgradeIfNeeded(gkey, gid, in entry, efont, evar, eftc, glyphH, aspect);
                RequestTileSizeUpgradeIfNeeded(gkey, gid, efont, evar, eftc);
            }
        }

        private void CaptureUndeformedGlyphGeometry(int count, PositionedGlyph[] allGlyphs)
        {
            for (var k = 0; k < count; k++)
            {
                var positionedIndex = quadSrc[k];
                ref readonly var glyph = ref allGlyphs[positionedIndex];
                var baseIndex = k * 4;
                var face = GlyphFace.Read(vertices.data, baseIndex);
                onGlyphComplete(new GlyphVisualGeometry(positionedIndex, glyph.cluster,
                    glyph.shapedGlyphIndex < 0, in face, in face));
            }
        }

        private void GenerateColorSegment(PooledList<int> glyphIndices, PositionedGlyph[] positionedGlyphs, UniTextFont.Core font)
        {
            var glyphCount = glyphIndices.Count;
            var colorVarHash = GlyphAtlas.DefaultVarHash(font.FontDataHash);

            var upem = font.UnitsPerEm;
            var fontScaleMul = font.FontScale;
            var scaleVal = FontSize * fontScaleMul / upem;
            var atlasSizeVal = GlyphAtlas.PageSize;

            var paddingPixels = font.AtlasPadding;
            var invAtlasSize = 1f / atlasSizeVal;

            var offX = rectOffset.xMin;
            var offY = rectOffset.yMax;

            scale = scaleVal;
            offsetX = offX;
            offsetY = offY;
            this.font = font;
            fontMetricFactor = scaleVal * upem;

            EnsureCapacity(glyphCount * 4, glyphCount * 6);

            var isColorFont = font.IsColor;
            var glyphColor = isColorFont
                ? new Color32(255, 255, 255, defaultColor.a)
                : defaultColor;

            buf.glyphDataCache.EnsureCapacity(buf.shapedGlyphs.count);
            var glyphCache = buf.glyphDataCache.data;

            var verts = vertices.data;
            var uvData = uvs0.data;
            var uv1Data = uvs1.data;
            var cols = colors.data;

            var skippedGlyphs = 0;
            var zeroRectGlyphs = 0;
            var transientData = default(CachedGlyphData);

            var xScales = buf.glyphXScales.count > 0 ? buf.glyphXScales.data : null;
            var xScaleCount = buf.glyphXScales.count;

            var fieldAttribute = buf.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.ColorGlyphField);
            var fieldRequests = fieldAttribute is { Count: > 0 } ? fieldAttribute.buffer.data : null;
            var fieldRequestCount = fieldRequests != null ? fieldAttribute.Count : 0;
            GlyphAtlas fieldAtlas = null;
            var fieldVarHash = 0L;
            if (fieldRequests != null && GlyphAtlas.TryGetExistingInstance(UniTextRenderMode.SDF, out fieldAtlas))
                fieldVarHash = GlyphAtlas.FieldVarHash48(font.FontDataHash);

            for (var i = 0; i < glyphCount; i++)
            {
                var glyphIndex = glyphIndices[i];
                ref var glyph = ref positionedGlyphs[glyphIndex];
                var cacheIndex = glyph.shapedGlyphIndex;

                ref var cachedData = ref (cacheIndex >= 0
                    ? ref glyphCache[cacheIndex]
                    : ref transientData);
                var colorGlyphId = (uint)glyph.glyphId;
                var colorKey = GlyphAtlas.MakeKey(colorVarHash, colorGlyphId);

                int padPx;
                float uvBLx, uvBLy, uvTLy, uvTRx, layerZ;
                if (font.TryGetColorTexture(colorGlyphId, out _, out var uvMin, out var uvMax, out var externalMetrics))
                {
                    cachedData = new CachedGlyphData
                    {
                        rectWidth = 1,
                        rectHeight = 1,
                        bearingX = externalMetrics.horizontalBearingX,
                        bearingY = externalMetrics.horizontalBearingY,
                        width = externalMetrics.width,
                        height = externalMetrics.height,
                        isValid = true
                    };
                    padPx = 0;
                    uvBLx = uvMin.x;
                    uvBLy = uvMin.y;
                    uvTLy = uvMax.y;
                    uvTRx = uvMax.x;
                    layerZ = 0f;
                }
                else
                {
                    var colorAtlas = GlyphAtlas.Color;
                    if (colorAtlas == null || !colorAtlas.TryGetEntry(colorKey, out var entry) || entry.encodedTile < 0)
                    {
                        skippedGlyphs++;
                        cachedData.isValid = false;
                        if (colorAtlas != null && font.IsNonEmptyGlyph(colorKey))
                            missingAtlasGlyphs = true;
                        if (skippedGlyphs <= 6 && CatZones.meshGenerator.Enabled)
                        {
                            var reason = colorAtlas == null ? "atlas-null"
                                : !colorAtlas.TryGetEntry(colorKey, out var diagEntry) ? "no-entry"
                                : $"tile={diagEntry.encodedTile}";
                            CatZones.meshGenerator.Meow($"[EmojiDiag] color skip: font={font.Name}, gid={colorGlyphId}, {reason}");
                        }
                        continue;
                    }

                    int tileSize = colorAtlas.TileSizeFromEncoded(entry.encodedTile);
                    colorAtlas.DecodeTileXY(entry.encodedTile, tileSize, out int tileX, out int tileY);
                    int g = colorAtlas.TileGutter;
                    var metrics = entry.metrics;
                    cachedData.rectX = tileX + g;
                    cachedData.rectY = tileY + g;
                    cachedData.rectWidth = entry.pixelWidth;
                    cachedData.rectHeight = entry.pixelHeight;
                    cachedData.bearingX = metrics.horizontalBearingX;
                    cachedData.bearingY = metrics.horizontalBearingY;
                    cachedData.width = metrics.width;
                    cachedData.height = metrics.height;
                    cachedData.atlasIndex = entry.pageIndex;
                    cachedData.isValid = true;

                    usedColorKeys.Add(colorKey);

                    if (cachedData.rectWidth == 0 || cachedData.rectHeight == 0)
                    {
                        zeroRectGlyphs++;
                        continue;
                    }

                    padPx = paddingPixels;
                    uvBLx = (cachedData.rectX - padPx) * invAtlasSize;
                    uvBLy = (cachedData.rectY - padPx) * invAtlasSize;
                    uvTLy = (cachedData.rectY + cachedData.rectHeight + padPx) * invAtlasSize;
                    uvTRx = (cachedData.rectX + cachedData.rectWidth + padPx) * invAtlasSize;
                    layerZ = cachedData.atlasIndex;
                }

                var cluster = glyph.cluster;
                var intrinsicScale = glyph.scale > 0f ? glyph.scale : 1f;
                var glyphScale = scale * intrinsicScale;

                var paddingDesign = padPx * (cachedData.width / cachedData.rectWidth);
                var bearingXScaled = (cachedData.bearingX - paddingDesign) * glyphScale;
                var bearingYScaled = (cachedData.bearingY + paddingDesign) * glyphScale;
                var heightScaled = (cachedData.height + 2f * paddingDesign) * glyphScale;
                var widthScaled = (cachedData.width + 2f * paddingDesign) * glyphScale;

                var tlX = offX + glyph.x + bearingXScaled;
                var tlY = offY - glyph.y + bearingYScaled;
                var blY = tlY - heightScaled;
                var trX = tlX + widthScaled;

                if (xScales != null && (uint)cluster < (uint)xScaleCount)
                {
                    var xs = xScales[cluster];
                    if (xs != 0f && xs != 1f)
                    {
                        var penX = offX + glyph.x;
                        tlX = penX + (tlX - penX) * xs;
                        trX = penX + (trX - penX) * xs;
                    }
                }

                var i0 = vertexCount;
                var i1 = vertexCount + 1;
                var i2 = vertexCount + 2;
                var i3 = vertexCount + 3;

                ref var v0 = ref verts[i0];
                v0.x = tlX; v0.y = blY; v0.z = 0;
                ref var v1 = ref verts[i1];
                v1.x = tlX; v1.y = tlY; v1.z = 0;
                ref var v2 = ref verts[i2];
                v2.x = trX; v2.y = tlY; v2.z = 0;
                ref var v3 = ref verts[i3];
                v3.x = trX; v3.y = blY; v3.z = 0;

                ref var uv0 = ref uvData[i0];
                uv0.x = uvBLx; uv0.y = uvBLy; uv0.z = layerZ; uv0.w = 0;
                ref var uv1 = ref uvData[i1];
                uv1.x = uvBLx; uv1.y = uvTLy; uv1.z = layerZ; uv1.w = 0;
                ref var uv2 = ref uvData[i2];
                uv2.x = uvTRx; uv2.y = uvTLy; uv2.z = layerZ; uv2.w = 0;
                ref var uv3 = ref uvData[i3];
                uv3.x = uvTRx; uv3.y = uvBLy; uv3.z = layerZ; uv3.w = 0;

                var colorAspect = cachedData.height > 0
                    ? (float)cachedData.width / cachedData.height
                    : 1f;
                var clusterF = (float)cluster;
                uv1Data[i0] = new Vector4(colorAspect, 0f, clusterF, ColorUv1wBias);
                uv1Data[i1] = new Vector4(colorAspect, 0f, clusterF, ColorUv1wBias);
                uv1Data[i2] = new Vector4(colorAspect, 0f, clusterF, ColorUv1wBias + 1f);
                uv1Data[i3] = new Vector4(colorAspect, 0f, clusterF, ColorUv1wBias + 1f);

                cols[i0] = glyphColor;
                cols[i1] = glyphColor;
                cols[i2] = glyphColor;
                cols[i3] = glyphColor;

                currentCluster = cluster;
                currentGlyphId = (int)colorGlyphId;
                height = heightScaled;
                baselineY = offY - glyph.y;
                cursorX = offX + glyph.x;

                vertexCount += 4;
#if UNITEXT_TESTS
                if (colorGlyphId != 0) RenderedGlyphCount++;
#endif

                faceBaseIdx = i0;
                ResetPerGlyphState();
                isVirtualGlyph = glyph.shapedGlyphIndex < 0;
                currentGlyphScale = intrinsicScale;

                if (fieldRequests != null && (uint)cluster < (uint)fieldRequestCount && fieldRequests[cluster] != 0)
                {
                    var fieldKey = GlyphAtlas.MakeKey(fieldVarHash, colorGlyphId);
                    if (fieldAtlas != null && fieldAtlas.TryGetEntry(fieldKey, out var fieldEntry)
                        && fieldEntry.encodedTile >= 0 && fieldEntry.handle >= 0 && fieldEntry.metrics.height > 0f)
                    {
                        colorFaceFields ??= new FastIntDictionary<ColorFaceField>(16);
                        colorFaceFields[i0] = new ColorFaceField
                        {
                            handle = fieldEntry.handle,
                            glyphH = fieldEntry.metrics.height / upem,
                            aspect = fieldEntry.metrics.width / fieldEntry.metrics.height,
                            padFracX = padPx / (float)(cachedData.rectWidth + 2 * padPx),
                            padFracY = padPx / (float)(cachedData.rectHeight + 2 * padPx),
                            padTier = fieldEntry.padTier,
                            key = fieldKey,
                            fieldVarHash = fieldVarHash,
                            glyphIndex = colorGlyphId,
                            font = font,
                        };
                        usedFieldKeys.Add(fieldKey);
                        hasCurrentColorField = true;
                    }
                    else
                        missingAtlasGlyphs = true;
                }

                InvokeGlyphModifiersAndComplete(glyphIndex);
                if (!baseFaceClaimed) AddSdfQuad(claimedFillSequence, i0, claimedFillBlend);
                if (hasCurrentColorField) RequestFieldUpgradesIfNeeded(colorFaceFields[i0]);

                verts = vertices.data;
                uvData = uvs0.data;
                uv1Data = uvs1.data;
                cols = colors.data;
            }

            if (skippedGlyphs > 0)
                CatZones.meshGenerator.MeowFormat("[GenerateColorSegment] {0}: SKIPPED {1} glyphs", font.Name, skippedGlyphs);
            if (zeroRectGlyphs > 0)
                CatZones.meshGenerator.MeowFormat("[GenerateColorSegment] {0}: ZERO RECT {1} glyphs", font.Name, zeroRectGlyphs);
        }

        /// <summary>
        /// Runs the per-glyph modifier chain and publishes the completed semantic glyph face. Manual
        /// emitters call this only for virtual geometry that represents text itself; decorative
        /// strokes keep using <see cref="onGlyph"/> directly so they do not enlarge range bounds.
        /// </summary>
        internal void InvokeGlyphModifiersAndComplete(int positionedGlyphIndex)
        {
            currentPositionedIndex = positionedGlyphIndex;
            var baseIndex = faceBaseIdx;
            var capture = captureGlyphGeometry && onGlyphComplete != null;
            var source = capture ? GlyphFace.Read(vertices.data, baseIndex) : default;
            glyphEvent?.Invoke();
            filters.ApplyToFace(this);
            if (!capture) return;
            var final = GlyphFace.Read(vertices.data, baseIndex);
            var geometry = new GlyphVisualGeometry(positionedGlyphIndex, currentCluster,
                isVirtualGlyph, in source, in final);
            if (captureIdentityGlyphGeometry || isVirtualGlyph || !geometry.transform.IsIdentity)
                onGlyphComplete(geometry);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void InvokeGlyphModifiers()
        {
            glyphEvent?.Invoke();
            filters.ApplyToFace(this);
        }


        /// <summary>
        /// Collects raw render data (vertex/UV/triangle array slices + material/order metadata) for every
        /// segment produced by the latest mesh generation. Does <b>not</b> build Unity <see cref="Mesh"/>
        /// objects — consumers (canvas <c>UpdateSubMeshes</c>, world batcher) decide what to do with
        /// the raw data.
        /// </summary>
        /// <returns>
        /// Shared list of <see cref="UniTextRenderData"/> entries — the base segment's layer runs (text and
        /// color quads share one segment; the per-glyph mode in UV1.w picks the sampler) plus any sub-meshes
        /// appended by <see cref="onCollectSubMeshes"/> subscribers. The list is reused on the next call;
        /// consumers must use/copy immediately.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Text and color quads live in the same pooled buffers (<see cref="Vertices"/>, <see cref="Uvs0"/>,
        /// <see cref="Colors"/>, <see cref="Triangles"/>) as contiguous ranges: text faces first, colour
        /// faces next, then the effect and decoration quads appended at main-pass finalization. Each entry carries <c>vertexOffset</c>/<c>vertexCount</c>
        /// bounding only the vertices its triangles reference (a tight span, not the whole segment), with
        /// triangle indices relative to that span — consumers read exactly the slice, so interleaved layer
        /// runs do not duplicate the segment's vertex data per run.
        /// </para>
        /// <para>
        /// Array references remain valid only until the next <see cref="GenerateMeshDataOnly"/> /
        /// <see cref="CollectRenderData"/> cycle on the same generator (pooled buffers may be regrown
        /// or returned). Consumers must not retain references across frames.
        /// </para>
        /// </remarks>
        public List<UniTextRenderData> CollectRenderData()
        {
            var resultBuffer = SharedPipelineComponents.MeshResultBuffer;
            resultBuffer.Clear();

            if (!hasGeneratedData)
                return resultBuffer;

            var collectionContext = new SubMeshCollectionContext(resultBuffer);
            collectSubMeshes?.Invoke(ref collectionContext);
            var subMeshCount = resultBuffer.Count;

            if (!runsRebased)
            {
                cachedSegmentEntries.Clear();
#if UNITEXT_DEBUG
                firstCollectSubMeshSequences.Clear();
                for (var i = 0; i < subMeshCount; i++)
                    firstCollectSubMeshSequences.Add(resultBuffer[i].sequence);
#endif
                if (vertexCount > 0)
                    AddSegmentRuns(resultBuffer, subMeshCount, cachedSegmentEntries, sdfRuns.data, sdfRunCount,
                        triangleCount, 0, vertexCount, sdfFontId);

                runsRebased = true;
            }
#if UNITEXT_DEBUG
            else
            {
                AssertSubMeshSequencesStable(resultBuffer, subMeshCount);
            }
#endif

            for (var i = 0; i < cachedSegmentEntries.Count; i++)
                resultBuffer.Add(cachedSegmentEntries[i]);

            StableSortBySequence(resultBuffer);
            return resultBuffer;
        }

#if UNITEXT_DEBUG
        private void AssertSubMeshSequencesStable(List<UniTextRenderData> buffer, int subMeshCount)
        {
            var stable = subMeshCount == firstCollectSubMeshSequences.Count;
            if (stable)
                for (var i = 0; i < subMeshCount; i++)
                    if (buffer[i].sequence != firstCollectSubMeshSequences[i]) { stable = false; break; }
            if (!stable)
                CatZones.meshGenerator.MeowWarn(
                    "[MeshGenerator] onCollectSubMeshes emitted a different sub-mesh sequence set on a same-generation collect — providers must be deterministic within a generation (the base runs were already rebased against the first collect's layout).");
        }
#endif

        /// <summary>
        /// Emits the base segment as one draw per maximal run with one blend and no separate-material
        /// sub-mesh between its consecutive layer sequences; a sub-mesh whose sequence falls between two
        /// layers splits the run there so it interleaves at its place in the stack. None between → a
        /// single draw. Each emitted entry carries only the vertex span its triangles reference (the
        /// union of the merged runs' spans), and the triangle sub-range is rebased in place from absolute
        /// to span-relative indices — so consumers upload/copy a fraction of the segment's vertices per
        /// run instead of the full set per run. Runs only on the FIRST collect of a generation; the
        /// emitted entries go into <paramref name="output"/> and are replayed on later collects.
        /// </summary>
        private void AddSegmentRuns(List<UniTextRenderData> buffer, int subMeshCount, List<UniTextRenderData> output,
            SdfRun[] runs, int runCount, int segTriEnd, int segVertOffset, int segVertCount,
            int fontId)
        {
            if (runCount == 0)
            {
                if (segTriEnd > 0)
                    AddSegmentEntry(output, fontId, DefaultFillSequence,
                        LayerBlend.Normal, segVertOffset, segVertCount, 0, segTriEnd);
                return;
            }

            var i = 0;
            while (i < runCount)
            {
                var j = i;
                var vMin = runs[i].vertMin;
                var vMax = runs[i].vertMax;
                while (j + 1 < runCount && runs[j].blend == runs[j + 1].blend &&
                       !SubMeshSplitsRun(buffer, subMeshCount, runs[j].sequence, runs[j + 1].sequence))
                {
                    j++;
                    if (runs[j].vertMin < vMin) vMin = runs[j].vertMin;
                    if (runs[j].vertMax > vMax) vMax = runs[j].vertMax;
                }
                var startTri = runs[i].triStart;
                var endTri = j + 1 < runCount ? runs[j + 1].triStart : segTriEnd;

                if (vMin != 0)
                {
                    var tris = triangles.data;
                    for (var t = startTri; t < endTri; t++) tris[t] -= vMin;
                }

                AddSegmentEntry(output, fontId, runs[i].sequence, runs[i].blend,
                    vMin, vMax - vMin, startTri, endTri - startTri);
                i = j + 1;
            }
        }

        /// <summary>
        /// True when a separate-material entry's complete sort key lies after the lower base run but before
        /// the upper base run, forcing the runs to stay in separate draws. Base runs use sort index zero;
        /// equal-key sub-meshes sort before them because collection appends base entries afterward.
        /// </summary>
        private static bool SubMeshSplitsRun(List<UniTextRenderData> buffer, int subMeshCount, int seqLow, int seqHigh)
        {
            for (var k = 0; k < subMeshCount; k++)
            {
                var subMesh = buffer[k];
                var afterLower = subMesh.sequence > seqLow ||
                                 subMesh.sequence == seqLow && subMesh.sortIndex > 0;
                var beforeUpper = subMesh.sequence < seqHigh ||
                                  subMesh.sequence == seqHigh && subMesh.sortIndex <= 0;
                if (afterLower && beforeUpper)
                    return true;
            }
            return false;
        }

        private void AddSegmentEntry(List<UniTextRenderData> buffer, int fontId, int sequence,
            LayerBlend blend,
            int vertexOffset, int vertexCount, int triangleOffset, int triangleCount)
        {
            buffer.Add(new UniTextRenderData
            {
                fontId         = fontId,
                sequence       = sequence,
                blend          = blend,
                vertices       = vertices.data,
                uvs0           = uvs0.data,
                uvs1           = uvs1.data,
                uvs2           = uvs2.data,
                uvs3           = uvs3.data,
                colors         = colors.data,
                triangles      = triangles.data,
                vertexOffset   = vertexOffset,
                vertexCount    = vertexCount,
                triangleOffset = triangleOffset,
                triangleCount  = triangleCount,
                hasUv1         = true,
                hasUv2         = uvs2.data != null,
                hasUv3         = uvs3.data != null,
            });
        }

        /// <summary>
        /// In-place stable insertion sort of <paramref name="list"/> by
        /// (<see cref="UniTextRenderData.sequence"/>, <see cref="UniTextRenderData.sortIndex"/>) ascending.
        /// Typical list size is 1–5 entries (SDF + optional color + a few sub-mesh providers), so insertion
        /// sort is the right choice: zero allocations, cache-friendly, minimal overhead on ordered input.
        /// </summary>
        private static void StableSortBySequence(List<UniTextRenderData> list)
        {
            var n = list.Count;
            if (n < 2) return;
            for (var i = 1; i < n; i++)
            {
                var x = list[i];
                var j = i - 1;
                while (j >= 0)
                {
                    var y = list[j];
                    if (y.sequence < x.sequence) break;
                    if (y.sequence == x.sequence && y.sortIndex <= x.sortIndex) break;
                    list[j + 1] = y;
                    j--;
                }
                list[j + 1] = x;
            }
        }


        #endregion
    }

}
