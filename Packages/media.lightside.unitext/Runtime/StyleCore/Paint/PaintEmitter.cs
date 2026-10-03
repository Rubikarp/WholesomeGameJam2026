using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared paint pipeline for painted quads — the single place that resolves a paint token into a
    /// <see cref="TextPaint"/> (caching paint-texture sizes for worker-thread aspect-fit and registering
    /// gradient ramp rows) and writes it into mesh output: a solid recolour, a gradient/coverage UV stamp,
    /// or a per-texture sub-mesh (<c>background-clip:text</c>). Both <see cref="PaintLayerModifier"/>
    /// and <see cref="BaseLineModifier"/> own one so glyph layers and decoration lines resolve and paint
    /// through one path.
    /// </summary>
    /// <remarks>
    /// The emitter also owns the whole lifecycle choreography its owners used to repeat: <see cref="Attach"/> /
    /// <see cref="Detach"/> wire sub-mesh collection and provider-change invalidation, <see cref="PrepareForParallel"/>
    /// snapshots the provider's resolved catalog (names, texture dimensions) into plain managed state on the
    /// main thread, and every worker-side resolve (<see cref="ResolvePaint(ReadOnlySpan{char}, out int)"/>,
    /// <see cref="IsPaintToken"/>) reads ONLY that snapshot — no <see cref="UnityEngine.Object"/> equality,
    /// no <c>Resources.Load</c>, no settings access is reachable from <c>OnApply</c>.
    /// </remarks>
    internal sealed class PaintEmitter
    {
        private sealed class TextureSubMesh
        {
            public Texture2D texture;
            public int sequence;
            public int sortIndex;
            public LayerBlend blend;
            public PooledBuffer<Vector3> vertices;
            public PooledBuffer<Vector4> uvs0, uvs1, uvs2, uvs3;
            public PooledBuffer<Color32> colors;
            public PooledBuffer<int> triangles;
            public MaterialCloneRef<SurfaceMaterialKey> materialRef;

            public void Clear()
            {
                vertices.FakeClear();
                uvs0.FakeClear();
                uvs1.FakeClear();
                uvs2.FakeClear();
                uvs3.FakeClear();
                colors.FakeClear();
                triangles.FakeClear();
            }

            public void Return()
            {
                vertices.Return();
                uvs0.Return();
                uvs1.Return();
                uvs2.Return();
                uvs3.Return();
                colors.Return();
                triangles.Return();
                materialRef.Release();
            }
        }

        private List<TextureSubMesh> subMeshes;

        private readonly PaintResolver resolver = new();

        private UniTextBase host;
        private BaseModifier modifier;
        private IPaintProvider provider;
        private OrderedEventHandler<SubMeshCollectionContext> collectCallback;
        private NamedCatalogChangedHandler<PaintSwatch> providerChangedCallback;

        /// <summary>
        /// Wires the emitter to its owner for one enable cycle: sub-mesh collection on the host's mesh
        /// generator and change invalidation on the paint provider. Call from the owner's
        /// <c>OnEnable</c>; pair with <see cref="Detach"/> in <c>OnDisable</c>. Both halves are pure
        /// managed event wiring — safe on the rebuild worker.
        /// </summary>
        public void Attach(UniTextBase uniText, BaseModifier owner, IPaintProvider paintProvider)
        {
            host = uniText;
            modifier = owner;
            provider = paintProvider;
            collectCallback ??= OnCollect;
            providerChangedCallback ??= OnProviderChanged;
            uniText.MeshGenerator.onCollectSubMeshes.Subscribe(collectCallback);
            if (provider != null) provider.Changed += providerChangedCallback;
        }

        /// <summary>Unwires <see cref="Attach"/> and releases the held ramp rows. Runs on the rebuild worker (owner <c>OnDisable</c>), so the host check is a reference-null test — never Unity's <c>==</c>.</summary>
        public void Detach()
        {
            if (host is not null) host.MeshGenerator.onCollectSubMeshes.Unsubscribe(collectCallback);
            if (provider != null) provider.Changed -= providerChangedCallback;
            host = null;
            modifier = null;
            provider = null;
            resolver.ResetResolution();
        }

        private void OnCollect(ref SubMeshCollectionContext context)
            => Collect(context.Results, host.MeshGenerator, host);

        /// <summary>
        /// A swatch edit re-captures the snapshot and replays only the owning modifier. Paint-token
        /// recognition and dependent parameter interpretation both happen inside that replay.
        /// </summary>
        private void OnProviderChanged(INamedCatalog<PaintSwatch> _,
            in NamedCatalogChange<PaintSwatch> change)
        {
            if (!resolver.ApplySourceChange(in change)) return;
            host?.MarkModifierDirty(modifier, UniTextDirty.Mesh);
        }

        /// <summary>Resets the per-rebuild state — the per-texture buffers (keeping their allocations) and the colour-filtered ramp rows, whose content the rebuild re-resolves.</summary>
        public void Clear()
        {
            resolver.ReleaseFilteredRows();
            if (subMeshes == null) return;
            for (var i = 0; i < subMeshes.Count; i++)
                subMeshes[i].Clear();
        }

        /// <summary>Releases all buffers, cached material clones, and held ramp rows. Call from the owner's <c>OnDestroy</c>.</summary>
        public void Return()
        {
            resolver.Return();
            if (subMeshes == null) return;
            for (var i = 0; i < subMeshes.Count; i++)
                subMeshes[i].Return();
        }

        /// <summary>Marks the provider snapshot stale; the next <see cref="PrepareForParallel"/> rebuilds it. Call when the paint source changes.</summary>
        public void MarkSourceDirty() => resolver.MarkSourceDirty();

        /// <summary>
        /// Captures the provider's resolved catalog — swatch map and texture pixel sizes — on the main
        /// thread before parallel layout: the worker-thread resolve path must not touch the live
        /// provider (settings access, asset <c>op_Equality</c>) or read <see cref="Texture2D"/>
        /// properties. Takes the owner's serialized provider explicitly because the first capture runs
        /// before the owner's <c>OnEnable</c>/<see cref="Attach"/> (the pipeline snapshots on the main
        /// thread, then initializes modifiers on the worker). Rebuilt only after
        /// <see cref="MarkSourceDirty"/>, a provider swap, or when a cached texture was destroyed (so a
        /// dead reference downgrades to the visible fallback instead of silently hiding glyphs); the
        /// steady state allocates nothing.
        /// </summary>
        public void PrepareForParallel(IPaintProvider paintProvider)
        {
            if (modifier is null) resolver.MarkSourceDirty();
            resolver.PrepareForParallel(paintProvider);
        }

        /// <summary>
        /// Resolves a paint token against the main-thread snapshot of the attached provider, fills
        /// texture pixel sizes, and registers a gradient's ramp row. The worker-unsafe steps all
        /// resolved ahead of time in <see cref="PrepareForParallel"/>, so this is callable from
        /// <c>OnApply</c>. A gradient with no usable ramp row (unconfigured / no stops) is downgraded
        /// to a solid fill, so it can't alias another gradient's row.
        /// </summary>
        public TextPaint ResolvePaint(ReadOnlySpan<char> token, out int rampRow)
            => resolver.ResolvePaint(token, out rampRow);

        /// <summary>Resolves an authored <see cref="PaintRef"/> (its explicit kind) through the same snapshot/ramp finalization as the token overload.</summary>
        public TextPaint ResolvePaint(in PaintRef paintRef, out int rampRow)
            => resolver.ResolvePaint(in paintRef, out rampRow);

        /// <summary>
        /// Whether the token is genuinely a paint (hex literal, known swatch name, or named colour) —
        /// the shared predicate optional paint slots use to rewind non-paint tokens. Reads only the
        /// provider snapshot, so it is worker-safe like <see cref="ResolvePaint(ReadOnlySpan{char}, out int)"/>.
        /// </summary>
        public bool IsPaintToken(ReadOnlySpan<char> token)
            => resolver.IsPaintToken(token);

        /// <summary>Ends the current paint-resolution cycle, clearing tracked swatch dependencies and ramp rows.</summary>
        public void ResetResolution()
            => resolver.ResetResolution();

        /// <summary>
        /// Folds a composed colour filter into a resolved paint before emission: a solid colour
        /// transforms in place, a gradient re-acquires a ramp row baked as
        /// <c>filter(sample × tint)</c> while its vertex colour keeps only alpha, and a texture
        /// paint takes a <see cref="ColorMatrixAtlas"/> row + 1 in the ramp-row slot for the shader.
        /// No-op for filter index 0. Acquired ramp rows live until the next <see cref="Clear"/>.
        /// </summary>
        public void ApplyFilter(UniTextMeshGenerator gen, int filterIdx, ref TextPaint paint,
            ref int rampRow)
        {
            if (filterIdx == 0) return;
            if (paint.kind == PaintSourceKind.Solid)
            {
                paint.color = gen.filters.GetMatrix(filterIdx).Transform(paint.color);
            }
            else if (paint.kind == PaintSourceKind.Gradient)
            {
                rampRow = resolver.AcquireFiltered(in paint.gradient, paint.color,
                    gen.filters.GetMatrix(filterIdx));
                paint.color = new Color32(255, 255, 255, paint.color.a);
            }
            else
            {
                rampRow = gen.filters.GetAtlasRow(filterIdx) + 1;
            }
        }

        /// <summary>
        /// Writes <paramref name="paint"/> into the base-mesh quad at <paramref name="baseIdx"/>: solid
        /// fill recolours it, gradient/non-fill solid stamp the coverage UVs, texture copies it into the
        /// per-texture sub-mesh (and, when <paramref name="claimsBase"/>, zeroes the base quad so only the
        /// textured copy shows). <paramref name="offset"/> and <paramref name="expandDelta"/> apply to
        /// whichever quad carries the paint. The frame is ignored for solid.
        /// <paramref name="sourceBaseIdx"/> (when non-negative and distinct from the destination) is the
        /// face quad this quad duplicates: its per-vertex alpha modulates the written alpha, keeping
        /// per-character fades synchronised between faces and their effect/texture copies.
        /// The solid zero-width recolour fast path is tested BEFORE corner packing — the corner code
        /// selects between two distance fields that agree at the zero-offset edge, so a plain solid fill
        /// never pays the whole-mesh UV2 stream. A claiming fill modulates the quad's own per-vertex alpha
        /// as it stood at claim time (initially the component fade) instead of imposing a uniform one, so a
        /// per-character alpha ramp written by an earlier glyph modifier survives every paint kind — solid,
        /// gradient or texture — and any modifier order.
        /// </summary>
        public void Paint(UniTextMeshGenerator gen, int baseIdx, in TextPaint paint, in PaintFrame frame,
            int rampRow, float coverageMode, float p0, float p1, float softness, byte fade,
            bool claimsBase, int sequence, Vector2 offset, float expandDelta,
            int sourceBaseIdx = -1, float corner = 0f)
        {
            if (claimsBase) gen.StashPreClaimAlpha(baseIdx);

            var glyphScale = sourceBaseIdx >= 0 ? gen.GlyphScale(sourceBaseIdx) : gen.currentGlyphScale;
            var metricFactor = gen.fontMetricFactor * glyphScale;

            if (paint.kind == PaintSourceKind.Texture)
            {
                coverageMode = CoverageMode.WithCorner(coverageMode, corner);
                if (claimsBase) gen.baseFaceClaimed = true;

                var src = sourceBaseIdx >= 0 ? sourceBaseIdx : baseIdx;
                var modulate = sourceBaseIdx >= 0;
                var sub = GetSubMesh(paint.texture, sequence, paint.blend);
                var dst = sub.vertices.count;
                AppendFace(sub, gen, src);
                CoverageQuadOps.Write(sub.colors.data, sub.uvs2.data, sub.uvs3.data, sub.vertices.data,
                    dst, in paint, in frame, rampRow, coverageMode, p0, p1, softness, modulate ? (byte)255 : fade);
                if (modulate)
                    CoverageQuadOps.ModulateAlpha(sub.colors.data, dst, gen, sourceBaseIdx);

                if (offset.x != 0f || offset.y != 0f)
                {
                    var verts = sub.vertices.data;
                    var dx = offset.x * metricFactor;
                    var dy = offset.y * metricFactor;
                    for (var i = 0; i < 4; i++) { verts[dst + i].x += dx; verts[dst + i].y += dy; }
                }
                if (expandDelta > 0f)
                    UniTextMeshGenerator.ExpandQuad(sub.vertices.data, sub.uvs0.data, dst, expandDelta);
                return;
            }

            var modulateBase = sourceBaseIdx >= 0 && sourceBaseIdx != baseIdx;
            var modulateFrom = modulateBase ? sourceBaseIdx : -1;

            if (coverageMode == CoverageMode.Fill && paint.kind == PaintSourceKind.Solid && p0 == 0f && softness == 0f)
            {
                var cols = gen.Colors;
                for (var i = 0; i < 4; i++)
                {
                    var idx = baseIdx + i;
                    var a = modulateBase ? paint.color.a : (byte)((paint.color.a * cols[idx].a + 127) / 255);
                    cols[idx] = new Color32(paint.color.r, paint.color.g, paint.color.b, a);
                }
            }
            else
            {
                coverageMode = CoverageMode.WithCorner(coverageMode, corner);
                gen.EnsureUvBuffer(2);
                if (paint.kind != PaintSourceKind.Solid) gen.EnsureUvBuffer(3);
                if (claimsBase && !modulateBase) modulateFrom = baseIdx;
                CoverageQuadOps.Write(gen, baseIdx, in paint, in frame, rampRow, coverageMode, p0, p1, softness,
                    modulateFrom >= 0 ? (byte)255 : fade);
            }

            if (modulateFrom >= 0)
                CoverageQuadOps.ModulateAlpha(gen.Colors, baseIdx, gen, modulateFrom);

            if (offset.x != 0f || offset.y != 0f)
                CoverageQuadOps.ApplyOffset(gen, baseIdx, offset.x * metricFactor, offset.y * metricFactor);
            if (expandDelta > 0f) gen.ExpandQuad(baseIdx, expandDelta);
        }

        /// <summary>
        /// Appends a plain textured copy of the base-mesh face at <paramref name="srcBase"/> to the
        /// sub-mesh of <paramref name="texture"/>: the face's positions, colours and colour-matrix row,
        /// full coverage through the shape surface's vertex-coverage mode, and texture coordinates
        /// spanning <c>[uvMin, uvMax]</c> across the quad. With <paramref name="meshPositions"/>
        /// (normalized over the face box) the copy is that mesh mapped into the face instead of the
        /// quad — a tightly packed sprite drawn by its own outline.
        /// </summary>
        public void AppendTexturedFace(UniTextMeshGenerator gen, int srcBase, Texture2D texture,
            int sequence, LayerBlend blend, Vector2 uvMin, Vector2 uvMax,
            Vector2[] meshPositions = null, Vector2[] meshUv = null, ushort[] meshTriangles = null)
        {
            var sub = GetSubMesh(texture, sequence, blend);
            var dst = sub.vertices.count;
            var v = gen.Vertices;
            var c = gen.Colors;
            var u1 = gen.Uvs1;
            var u3 = gen.Uvs3;
            var cluster = u1[srcBase].z;
            var aspect = u1[srcBase].x;
            var row = u3 != null ? u3[srcBase].z : 0f;
            var coverage = new Vector4(LightSideShapeCoverageMode.VertexCoverage, 1f, 0f, 0f);

            if (meshPositions == null || meshUv == null || meshTriangles == null)
            {
                for (var i = 0; i < 4; i++)
                {
                    var right = i >= 2;
                    var top = i == 1 || i == 2;
                    sub.vertices.Add(v[srcBase + i]);
                    sub.uvs0.Add(default);
                    sub.uvs1.Add(new Vector4(aspect, 0f, cluster,
                        LightSideSurface.Pack(LightSideSurfaceKind.Shape, right ? 1f : 0f)));
                    sub.uvs2.Add(coverage);
                    sub.uvs3.Add(new Vector4(right ? uvMax.x : uvMin.x, top ? uvMax.y : uvMin.y, row,
                        CoverageQuadOps.TexturePaintKind));
                    sub.colors.Add(c[srcBase + i]);
                }
                sub.triangles.Add(dst);
                sub.triangles.Add(dst + 1);
                sub.triangles.Add(dst + 2);
                sub.triangles.Add(dst + 2);
                sub.triangles.Add(dst + 3);
                sub.triangles.Add(dst);
                return;
            }

            var bl = v[srcBase];
            var tl = v[srcBase + 1];
            var br = v[srcBase + 3];
            var hx = br.x - bl.x;
            var hy = br.y - bl.y;
            var vx = tl.x - bl.x;
            var vy = tl.y - bl.y;
            var color = c[srcBase];
            for (var i = 0; i < meshPositions.Length; i++)
            {
                var p = meshPositions[i];
                sub.vertices.Add(new Vector3(bl.x + hx * p.x + vx * p.y, bl.y + hy * p.x + vy * p.y, bl.z));
                sub.uvs0.Add(default);
                sub.uvs1.Add(new Vector4(aspect, 0f, cluster,
                    LightSideSurface.Pack(LightSideSurfaceKind.Shape, Mathf.Clamp01(p.x))));
                sub.uvs2.Add(coverage);
                sub.uvs3.Add(new Vector4(meshUv[i].x, meshUv[i].y, row, CoverageQuadOps.TexturePaintKind));
                sub.colors.Add(color);
            }
            for (var i = 0; i < meshTriangles.Length; i++)
                sub.triangles.Add(dst + meshTriangles[i]);
        }

        /// <summary>
        /// Emits one <see cref="UniTextRenderData"/> per non-empty texture sub-mesh. Main thread. A
        /// world <paramref name="owner"/> picks the depth-tested world base material (lit per its flag)
        /// for the paint clone instead of the Canvas UI one.
        /// </summary>
        public void Collect(List<UniTextRenderData> results, UniTextMeshGenerator gen, UniTextBase owner)
        {
            if (subMeshes == null) return;

            for (var i = subMeshes.Count - 1; i >= 0; i--)
            {
                var s = subMeshes[i];
                if (s.vertices.count != 0) continue;
                s.Return();
                subMeshes.RemoveAt(i);
            }
            for (var i = 0; i < subMeshes.Count; i++)
                subMeshes[i].sortIndex = i;

            var world = owner as UniTextWorld;
            var isWorld = world != null;
            var baseMaterial = isWorld ? UniTextMaterialCache.TextWorld(world.Lit) : UniTextMaterialCache.Text;

            for (var i = 0; i < subMeshes.Count; i++)
            {
                var s = subMeshes[i];

                if (s.texture == null)
                {
                    s.materialRef.Release();
                    continue;
                }
                var key = new SurfaceMaterialKey(baseMaterial, LayerBlend.Normal, s.texture);
                var material = s.materialRef.Bind(SurfaceMaterialPool.Instance, key, baseMaterial);
                if (material == null) continue;

                results.Add(new UniTextRenderData
                {
                    fontId = 0,
                    materialOverride = material,
                    atlasOverride = null,
                    sequence = s.sequence,
                    sortIndex = s.sortIndex,
                    blend = s.blend,
                    vertices = s.vertices.data,
                    uvs0 = s.uvs0.data,
                    uvs1 = s.uvs1.data,
                    uvs2 = s.uvs2.data,
                    uvs3 = s.uvs3.data,
                    colors = s.colors.data,
                    triangles = s.triangles.data,
                    vertexOffset = 0,
                    vertexCount = s.vertices.count,
                    triangleOffset = 0,
                    triangleCount = s.triangles.count,
                    hasUv1 = true,
                    hasUv2 = true,
                    hasUv3 = true,
                });
            }
        }

        private TextureSubMesh GetSubMesh(Texture2D texture, int sequence, LayerBlend blend)
        {
            subMeshes ??= new List<TextureSubMesh>(1);
            for (var i = 0; i < subMeshes.Count; i++)
                if (ReferenceEquals(subMeshes[i].texture, texture) &&
                    subMeshes[i].sequence == sequence && subMeshes[i].blend == blend)
                    return subMeshes[i];

            var sub = new TextureSubMesh
            {
                texture = texture,
                sequence = sequence,
                sortIndex = subMeshes.Count,
                blend = blend,
            };
            subMeshes.Add(sub);
            return sub;
        }

        /// <summary>Appends the face at <paramref name="srcBase"/> to the sub-mesh — as the SDF quad over its silhouette field when the face is a colour glyph carrying one.</summary>
        private static void AppendFace(TextureSubMesh s, UniTextMeshGenerator gen, int srcBase)
        {
            var dst = s.vertices.count;
            var v = gen.Vertices;
            var u0 = gen.Uvs0;
            var u1 = gen.Uvs1;
            var c = gen.Colors;
            for (var i = 0; i < 4; i++)
            {
                s.vertices.Add(v[srcBase + i]);
                s.uvs0.Add(u0[srcBase + i]);
                s.uvs1.Add(u1[srcBase + i]);
                s.colors.Add(c[srcBase + i]);
                s.uvs2.Add(default);
                s.uvs3.Add(default);
            }
            if (gen.TryGetColorFaceField(srcBase, out var field))
                ColorFieldQuad.Write(v, u1[srcBase].z, s.vertices.data, s.uvs0.data, s.uvs1.data,
                    srcBase, dst, in field);
            s.triangles.Add(dst);
            s.triangles.Add(dst + 1);
            s.triangles.Add(dst + 2);
            s.triangles.Add(dst + 2);
            s.triangles.Add(dst + 3);
            s.triangles.Add(dst);
        }
    }
}
