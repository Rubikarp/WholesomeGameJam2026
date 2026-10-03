using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Defines an inline sprite that can be embedded within text flow.
    /// </summary>
    /// <remarks>
    /// Pure data: the host <see cref="SpriteModifier"/> owns per-occurrence state, so the same
    /// <see cref="InlineSprite"/> instance can safely be referenced by multiple modifiers.
    /// </remarks>
    [Serializable]
    public partial class InlineSprite : InlineMedia
    {
        /// <summary>The sprite to draw.</summary>
        [SerializeField, StateProperty]
        private Sprite sprite;
        /// <summary>Tint color multiplied with the sprite.</summary>
        [SerializeField, StateProperty]
        private Color color = Color.white;
        /// <summary>If true, the sprite is letterboxed inside the <c>width x height</c> box to keep its native aspect ratio.</summary>
        [SerializeField, StateProperty]
        private bool preserveAspect = true;
    }

    /// <summary>Tri-state bool override: <see cref="Inherit"/> falls back to the weaker layer's value (the catalog entry).</summary>
    public enum InheritBool : byte
    {
        Inherit,
        True,
        False
    }

    /// <summary>Where an inline sprite gets its rendered color.</summary>
    public enum SpriteColorSource : byte
    {
        /// <summary>The sprite entry's own <see cref="InlineSprite.Color"/> (atlas/asset colors).</summary>
        Original,
        /// <summary>The host component's color (CSS <c>currentColor</c>).</summary>
        Inherit,
        /// <summary>An explicit per-entry or per-tag color.</summary>
        Override
    }

    /// <summary>An inline sprite's colour choice: its own, the component's, or an explicit override. Serializes to a single markup token — empty for original, <c>i</c> for inherit, <c>#RRGGBBAA</c> for override.</summary>
    [Serializable]
    public struct SpriteColorRef : IMarkupValue
    {
        public SpriteColorSource source;
        public Color32 color;

        public string ToToken() => source switch
        {
            SpriteColorSource.Inherit => "i",
            SpriteColorSource.Override => $"#{color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}",
            _ => ""
        };

        public void FromToken(string token) => this = Parse(token.AsSpan());

        /// <summary>Single grammar shared by markup parsing and the clipboard token form.</summary>
        public static SpriteColorRef Parse(ReadOnlySpan<char> token)
        {
            if (token.IsEmpty) return default;
            if (token.EqualsIgnoreCase("i")) return new SpriteColorRef { source = SpriteColorSource.Inherit };
            if (ColorParsing.TryParse(token, out var c)) return new SpriteColorRef { source = SpriteColorSource.Override, color = c };
            return default;
        }
    }

    /// <summary>Presentation overrides for one sprite resolved from the modifier's provider.</summary>
    [Serializable]
    public sealed partial class InlineSpriteOverride : InlineMediaOverride
    {
        /// <summary>Color override for this provider key.</summary>
        [SerializeField, Parameter, Variant("Original|Inherit=i|Override=color:#FFFFFFFF", Discriminator = nameof(SpriteColorRef.source)), StateProperty]
        private SpriteColorRef color;

        /// <summary>Aspect-ratio override for this provider key.</summary>
        [SerializeField, Parameter, StateProperty]
        private InheritBool preserveAspect;
    }

    /// <summary>
    /// Embeds <see cref="Sprite"/> assets inline with text as glyphs of the text mesh. The set of named
    /// sprites available to <c>&lt;sprite=name&gt;</c> tags is supplied by an <see cref="ISpriteProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Tag syntax:
    /// <c>&lt;sprite=name[,colorArg][,aspect][,size][,offset][,advance][,lineHeightAbove][,lineHeightBelow][,pivot][,rotation]&gt;</c>.
    /// The optional second positional argument controls the rendered color of this occurrence:
    /// </para>
    /// <list type="bullet">
    /// <item>omitted — use the keyed override when present, otherwise the provider entry's own
    /// <see cref="InlineSprite.Color"/>.</item>
    /// <item><c>i</c> — inherit the host UniText component's <see cref="UnityEngine.UI.Graphic.color"/>.
    /// Equivalent to CSS <c>currentColor</c>.</item>
    /// <item><c>#RGB</c>/<c>#RRGGBB</c>/<c>#RRGGBBAA</c> or a named color (red, blue, …) — explicit
    /// per-occurrence override.</item>
    /// </list>
    /// <para>
    /// Examples: <c>&lt;sprite=heart&gt;</c>, <c>&lt;sprite=heart,i&gt;</c>, <c>&lt;sprite=heart,#FF0000&gt;</c>.
    /// </para>
    /// <para>
    /// <c>aspect</c> (true/false) and the shared presentation overrides (size, bearing offset,
    /// advance, line space above/below — em units; pivot — normalized; rotation — degrees)
    /// resolve through the override hierarchy, weakest → strongest: provider entry → keyed override
    /// → default parameters → tag attribute.
    /// </para>
    /// <para>
    /// Every sprite is a glyph of the process-wide <see cref="SpriteFont"/>: it takes its place in the
    /// layer stack, renders in world-space text, and receives the layer effects (shadow, glow, stroke, …)
    /// whose colour-glyph policy applies to it. It draws from its own texture at native resolution —
    /// one sub-mesh per distinct texture, so the sprites of one atlas share a draw call — and a tightly
    /// packed sprite draws by its own outline mesh.
    /// </para>
    /// <para>
    /// The sprite name resolves through <see cref="Provider"/>; a matching entry in
    /// <see cref="Overrides"/> changes only its presentation. Built-in providers:
    /// <see cref="InlineSpriteProvider"/> (default — inline list on the modifier),
    /// <see cref="AssetSpriteProvider"/> (shared <see cref="UniTextSprites"/> asset). For dynamic
    /// catalogs — input-prompt icon services, localisation, item icons — implement
    /// <see cref="ISpriteProvider"/> directly and raise its
    /// <see cref="INamedCatalog{TEntry}.Changed"/> event when the resolution result changes.
    /// </para>
    /// </remarks>
    /// <seealso cref="ObjModifier"/>
    /// <seealso cref="ISpriteProvider"/>
    /// <seealso cref="InlineSprite"/>
    [Serializable]
    [TypeGroup("Inline", 5)]
    [TypeDescription("Embeds an inline sprite (no prefab required) within text.")]
    public sealed partial class SpriteModifier : InlineMediaModifier<InlineSprite, InlineSpriteOverride>
    {
        /// <summary>Source of named inline sprites.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyProviderChange)),
         StateLink(nameof(OnProviderStateChanged))]
        [Tooltip("Source of named sprites for <sprite=name> tags handled by this modifier.")]
        private ISpriteProvider provider = new InlineSpriteProvider();

        private struct SpriteOverrides
        {
            public SpriteColorRef color;
            public InheritBool aspect;
        }

        /// <summary>
        /// One occurrence in the mesh: its sprite glyph, the uniform scale of the one-em nominal glyph
        /// box, the horizontal stretch that fills a box without aspect preservation, and its tint and
        /// transform.
        /// </summary>
        private struct Placement
        {
            public uint glyphId;
            public float scale;
            public float xScale;
            public Vector2 pivot;
            public float rotation;
            public Color32 color;
        }

        /// <summary>A face quad whose picture is copied into its texture's sub-mesh at main-pass finalization, at the layer position the face held.</summary>
        private struct PendingCopy
        {
            public int face;
            public uint glyphId;
            public int sequence;
            public LayerBlend blend;
        }

        private FastIntDictionary<SpriteOverrides> clusterOverrides;
        private FastIntDictionary<Placement> placements;
        private SpriteFont font;
        private int fontId;
        private Action layoutCallback;
        private Action glyphCallback;
        private Action rebuildStartCallback;
        private Action finalizeCallback;
        private readonly PaintEmitter emitter = new();
        private PooledBuffer<PendingCopy> copies;

        private void ApplyProviderChange(ISpriteProvider previous, ISpriteProvider current)
        {
            RebindCatalog(previous, current);
            MarkTextDirty();
        }

        protected override INamedCatalog<InlineSprite> Catalog => provider;

        protected override bool HasRenderable(InlineSprite entry) => entry.Sprite is not null;

        /// <summary>
        /// Registers the sprite font with the component's font provider and every catalog sprite with
        /// the font, so worker-side placement resolves glyph ids without touching Unity objects.
        /// </summary>
        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            font = SpriteFont.Instance;
            fontId = UniTextFontProvider.GetFontId(font);
            uniText?.FontProvider?.RegisterFont(fontId, font);
            foreach (var entry in SnapshotEntries)
            {
                var sprite = entry?.Sprite;
                if (sprite != null) font.Register(sprite);
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            clusterOverrides?.Clear();
            placements ??= new FastIntDictionary<Placement>(16);
            placements.Clear();
            layoutCallback ??= OnLayoutComplete;
            glyphCallback ??= OnGlyph;
            rebuildStartCallback ??= OnRebuildStart;
            finalizeCallback ??= OnMainPassFinalize;
            uniText.TextProcessor.LayoutComplete.Subscribe(layoutCallback, 1000);
            var gen = uniText.MeshGenerator;
            gen.onRebuildStart.Subscribe(rebuildStartCallback);
            gen.onGlyph.Subscribe(glyphCallback);
            gen.onMainPassFinalize.Subscribe(finalizeCallback);
            emitter.Attach(uniText, this, null);
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.LayoutComplete.Unsubscribe(layoutCallback);
            var gen = uniText.MeshGenerator;
            gen.onRebuildStart.Unsubscribe(rebuildStartCallback);
            gen.onGlyph.Unsubscribe(glyphCallback);
            gen.onMainPassFinalize.Unsubscribe(finalizeCallback);
            emitter.Detach();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            placements?.Clear();
            placements = null;
            clusterOverrides?.Clear();
            copies.Return();
            emitter.Return();
            layoutCallback = null;
            glyphCallback = null;
            rebuildStartCallback = null;
            finalizeCallback = null;
            base.OnDestroy();
        }

        private void OnRebuildStart()
        {
            copies.FakeClear();
            emitter.Clear();
        }

        protected override void OnShapingStarted() => placements.Clear();

        /// <summary>
        /// Resolves the occurrence's glyph and box: the sprite keeps its aspect inside the box, centred,
        /// or stretches to fill it; the offsets land in the shaping slot, the scales in the placement. The
        /// glyph is requested with the silhouette field any effect asked for on this cluster — the only
        /// atlas work a sprite ever needs.
        /// </summary>
        protected override void OnMediaShaped(int cluster, in ResolvedMedia media, ref ShapedGlyph glyph,
            float fontSize)
        {
            var entry = media.entry;
            var sprite = entry.Sprite;
            if (sprite is null || font == null || !font.TryGetGlyph(sprite, out var glyphId, out var aspect))
            {
                placements.Remove(cluster);
                return;
            }

            SpriteOverrides ov = default;
            var hasOverride = clusterOverrides != null && clusterOverrides.TryGetValue(cluster, out ov);
            var preserveAspect = hasOverride && ov.aspect != InheritBool.Inherit
                ? ov.aspect == InheritBool.True
                : entry.PreserveAspect;
            Color32 color = hasOverride && ov.color.source != SpriteColorSource.Original
                ? ov.color.source == SpriteColorSource.Inherit ? (Color32)uniText.color : ov.color.color
                : (Color32)entry.Color;

            if (aspect <= 0f) aspect = 1f;
            var box = media.size;
            float scale, xScale = 1f, offsetX = 0f, offsetY = 0f;
            if (preserveAspect)
            {
                scale = Mathf.Min(box.x / aspect, box.y);
                offsetX = (box.x - aspect * scale) * 0.5f;
                offsetY = (box.y - scale) * 0.5f;
            }
            else
            {
                scale = box.y;
                xScale = box.y > 0f ? box.x / (aspect * box.y) : 1f;
            }
            glyph.offsetX += offsetX * fontSize;
            glyph.offsetY += offsetY * fontSize;

            placements[cluster] = new Placement
            {
                glyphId = glyphId,
                scale = scale,
                xScale = xScale,
                pivot = media.pivot,
                rotation = media.rotation,
                color = color,
            };

            var attribute = buffers.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.ColorGlyphField);
            var requests = attribute is { Count: > 0 } ? attribute.buffer.data : null;
            var extent = requests != null && (uint)cluster < (uint)attribute.Count ? requests[cluster] : (byte)0;
            buffers.RequestVirtualGlyph(fontId, glyphId, extent);
        }

        /// <summary>
        /// Turns each occurrence's placeholder into its sprite glyph once positions exist: the glyph id
        /// and font of the sprite, and the box scale folded into the glyph's own. Fires with every
        /// positioning pass; a glyph already converted keeps its values.
        /// </summary>
        private void OnLayoutComplete()
        {
            if (placements == null || placements.Count == 0) return;
            uniText.FontProvider?.RegisterFont(fontId, font);

            var glyphs = buffers.positionedGlyphs.data;
            var count = buffers.positionedGlyphs.count;
            for (var i = 0; i < count; i++)
            {
                ref var glyph = ref glyphs[i];
                if (glyph.glyphId != ShapedGlyph.NoGlyph || glyph.shapedGlyphIndex < 0) continue;
                if (!placements.TryGetValue(glyph.cluster, out var placement)) continue;
                glyph.glyphId = (int)placement.glyphId;
                glyph.fontId = fontId;
                glyph.scale = (glyph.scale > 0f ? glyph.scale : 1f) * placement.scale;
            }
        }

        /// <summary>
        /// Applies the occurrence's tint, horizontal stretch and rotation to its face quad, then takes the
        /// face over: the base mesh keeps it only as the anchor for effects, and its picture is copied into
        /// the sprite texture's sub-mesh once every modifier has shaped it.
        /// </summary>
        private void OnGlyph()
        {
            var gen = uniText.MeshGenerator;
            if (!ReferenceEquals(gen.font, font) || gen.isVirtualGlyph) return;
            if (!placements.TryGetValue(gen.currentCluster, out var placement)) return;

            var baseIdx = gen.faceBaseIdx;
            var color = placement.color;
            color.a = (byte)((color.a * gen.defaultColor.a + 127) / 255);
            var colors = gen.Colors;
            colors[baseIdx] = color;
            colors[baseIdx + 1] = color;
            colors[baseIdx + 2] = color;
            colors[baseIdx + 3] = color;

            if (placement.xScale != 1f) gen.XScaleFace(baseIdx, gen.cursorX, placement.xScale);
            if (placement.rotation != 0f) RotateFace(gen.Vertices, baseIdx, placement.pivot, placement.rotation);

            gen.baseFaceClaimed = true;
            copies.Add(new PendingCopy
            {
                face = baseIdx,
                glyphId = placement.glyphId,
                sequence = gen.claimedFillSequence,
                blend = gen.claimedFillBlend
            });
        }

        /// <summary>Copies every taken-over face into its texture's sub-mesh, except one a fill layer claimed for its own paint.</summary>
        private void OnMainPassFinalize()
        {
            if (copies.count == 0) return;
            var gen = uniText.MeshGenerator;
            var data = copies.data;
            for (var i = 0; i < copies.count; i++)
            {
                ref readonly var copy = ref data[i];
                if (gen.WasFillClaimed(copy.face) || !font.TryGetSpriteGlyph(copy.glyphId, out var sprite)) continue;
                emitter.AppendTexturedFace(gen, copy.face, sprite.texture, copy.sequence, copy.blend,
                    sprite.uvMin, sprite.uvMax, sprite.meshPositions, sprite.meshUv, sprite.meshTriangles);
            }
            copies.FakeClear();
        }

        private static void RotateFace(Vector3[] verts, int baseIdx, Vector2 pivot, float degrees)
        {
            var bl = verts[baseIdx];
            var tl = verts[baseIdx + 1];
            var br = verts[baseIdx + 3];
            var px = bl.x + (br.x - bl.x) * pivot.x + (tl.x - bl.x) * pivot.y;
            var py = bl.y + (br.y - bl.y) * pivot.x + (tl.y - bl.y) * pivot.y;
            var radians = degrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(radians);
            var sin = Mathf.Sin(radians);
            for (var i = 0; i < 4; i++)
            {
                ref var v = ref verts[baseIdx + i];
                var dx = v.x - px;
                var dy = v.y - py;
                v.x = px + dx * cos - dy * sin;
                v.y = py + dx * sin + dy * cos;
            }
        }

        protected override void OnExtraTokens(int cluster, InlineSpriteOverride mediaOverride,
            ref ParameterReader reader)
        {
            var fallbackColor = mediaOverride == null ? default : mediaOverride.Color;
            var fallbackAspect = mediaOverride == null
                ? InheritBool.Inherit
                : mediaOverride.PreserveAspect;
            var binding = reader.Next(out var token) && !token.IsEmpty
                ? SpriteColorRef.Parse(token)
                : fallbackColor;
            reader.NextEnum(out InheritBool aspect, fallbackAspect);

            if (binding.source == SpriteColorSource.Original && aspect == InheritBool.Inherit)
            {
                clusterOverrides?.Remove(cluster);
            }
            else
            {
                clusterOverrides ??= new FastIntDictionary<SpriteOverrides>(8);
                clusterOverrides[cluster] = new SpriteOverrides { color = binding, aspect = aspect };
            }
        }
    }
}
