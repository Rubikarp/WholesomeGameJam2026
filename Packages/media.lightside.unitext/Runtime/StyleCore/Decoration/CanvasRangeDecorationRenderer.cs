using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Batches range decoration meshes into Canvas graphics by texture and blend mode.</summary>
    internal sealed class CanvasRangeDecorationRenderer : RangeDecorationRenderer
    {
        private sealed class Surface
        {
            public GameObject gameObject;
            public RangeDecorationGraphic graphic;
            public MaterialCloneRef<SurfaceMaterialKey> textureMaterialRef;
            public MaterialCloneRef<SurfaceMaterialKey> blendMaterialRef;

            public void Release()
            {
                blendMaterialRef.Release();
                textureMaterialRef.Release();
            }
        }

        private readonly UniText owner;
        private readonly RangeDecorationOrder order;
        private readonly List<Surface> surfaces = new(2);
        private GameObject go;
        private RectTransform rt;
        private Action flushCallback;
        private TickHandle flushHandle;
        private Canvas ensuredCanvas;
        private AdditionalCanvasShaderChannels ensuredChannels;

        public CanvasRangeDecorationRenderer(UniText owner, string name, RangeDecorationOrder order)
        {
            this.owner = owner;
            this.order = order;

            go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(owner.transform, false);
            if (order == RangeDecorationOrder.Behind) go.transform.SetAsFirstSibling();
            else go.transform.SetAsLastSibling();

            var ownerRT = owner.rectTransform;
            rt = go.AddComponent<RectTransform>();
            rt.pivot = ownerRT.pivot;
            rt.StretchToParent();
        }

        protected override void OnDirty()
        {
            if (go == null) return;
            CoreLoop.CanvasPreRendering.Toggle(ref flushHandle, flushCallback ??= FlushDeferred, true);
#if UNITY_EDITOR
            if (!Application.isPlaying) CoreLoop.RequestEditorFrame();
#endif
        }

        private void FlushDeferred()
        {
            CoreLoop.CanvasPreRendering.Toggle(ref flushHandle, flushCallback, false);
            Flush();
        }

        protected override void Rebuild()
        {
            if (go == null) return;
            SyncPivot();
            BuildDrawBatches();
            var baseMaterial = LightSideMaterials.Ui;
            ValidateBlendMaterials(baseMaterial, DrawBatches);
            EnsureSurfaceCount(DrawBatches.Length);
            EnsureCanvasChannels();
            RebuildSurfaces(baseMaterial);
            AssertSiblingOrder();
        }

        private void RebuildSurfaces(Material baseMaterial)
        {
            var batches = DrawBatches;
            for (var i = 0; i < surfaces.Count; i++)
            {
                var surface = surfaces[i];
                var active = i < batches.Length;
                if (surface.gameObject.activeSelf != active)
                    surface.gameObject.SetActive(active);
                if (!active)
                {
                    surface.Release();
                    continue;
                }

                surface.gameObject.transform.SetSiblingIndex(i);
                ref readonly var batch = ref batches[i];
                var material = ResolveMaterial(surface, in batch, baseMaterial);

                if (surface.graphic.material != material)
                    surface.graphic.material = material;
                surface.graphic.Bind(this, i);
                surface.graphic.Rebuild();
            }
        }

        private static Material ResolveMaterial(Surface surface, in RangeDecorationDrawBatch batch,
            Material baseMaterial)
        {
            Material source;
            if (batch.texture == null)
            {
                surface.textureMaterialRef.Release();
                source = baseMaterial;
            }
            else
            {
                var textureKey = new SurfaceMaterialKey(baseMaterial, LayerBlend.Normal, batch.texture);
                source = surface.textureMaterialRef.Bind(
                    SurfaceMaterialPool.Instance, textureKey, baseMaterial);
            }

            if (batch.blend == LayerBlend.Normal)
            {
                surface.blendMaterialRef.Release();
                return source;
            }

            var blendKey = new SurfaceMaterialKey(source, batch.blend);
            return surface.blendMaterialRef.Bind(SurfaceMaterialPool.Instance, blendKey, source);
        }

        private void EnsureSurfaceCount(int count)
        {
            while (surfaces.Count < count)
                surfaces.Add(CreateSurface(surfaces.Count));
        }

        private Surface CreateSurface(int index)
        {
            var surfaceObject = new GameObject($"Batch {index}") { hideFlags = HideFlags.HideAndDontSave };
            surfaceObject.transform.SetParent(go.transform, false);
            var surfaceTransform = surfaceObject.AddComponent<RectTransform>();
            surfaceTransform.pivot = rt.pivot;
            surfaceTransform.StretchToParent();
            return new Surface
            {
                gameObject = surfaceObject,
                graphic = surfaceObject.AddComponent<RangeDecorationGraphic>(),
            };
        }

        /// <summary>
        /// The shared surface contract claims every channel — the kind word alone lives in TEXCOORD1 — so
        /// the Canvas must stream them all rather than the subset a decoration happens to use. The channels
        /// are ensured live on every rebuild and never persisted or dirtied: marking the Canvas dirty from a
        /// render pass would dirty its scene or prefab stage on every load.
        /// </summary>
        private void EnsureCanvasChannels()
        {
            const AdditionalCanvasShaderChannels needed =
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.TexCoord3 |
                AdditionalCanvasShaderChannels.Tangent;

            var canvas = surfaces.Count > 0 ? surfaces[0].graphic.canvas : null;
            if (canvas == null) return;
            if (canvas != ensuredCanvas)
            {
                ensuredCanvas = canvas;
                ensuredChannels = AdditionalCanvasShaderChannels.None;
            }
            if ((ensuredChannels & needed) == needed) return;
            ensuredChannels |= needed;

            CanvasChannels.Ensure(canvas, needed);
        }

        /// <summary>
        /// Sibling order is claimed at construction but the text's render root is created
        /// lazily with <c>SetAsFirstSibling</c> and may appear (or be recreated) after a
        /// Behind highlight exists — so both orders are re-asserted on every rebuild, and
        /// only when actually out of place to avoid churning user-authored children.
        /// </summary>
        private void AssertSiblingOrder()
        {
            if (go == null) return;
            var t = go.transform;
            var parent = t.parent;
            if (parent == null) return;

            if (order == RangeDecorationOrder.Above)
            {
                if (t.GetSiblingIndex() != parent.childCount - 1)
                    t.SetAsLastSibling();
            }
            else if (t.GetSiblingIndex() != 0)
            {
                t.SetAsFirstSibling();
            }
        }

        private void SyncPivot()
        {
            if (rt == null || owner == null) return;
            var ownerPivot = owner.rectTransform.pivot;
            if (rt.pivot == ownerPivot) return;
            rt.pivot = ownerPivot;
            for (var i = 0; i < surfaces.Count; i++)
                ((RectTransform)surfaces[i].gameObject.transform).pivot = ownerPivot;
        }

        internal override void Destroy()
        {
            CoreLoop.CanvasPreRendering.Toggle(ref flushHandle, flushCallback, false);
            for (var i = 0; i < surfaces.Count; i++) surfaces[i].Release();
            surfaces.Clear();
            ReturnDrawBatches();
            ObjectUtils.SafeDestroy(go);
            go = null;
            rt = null;
            ensuredCanvas = null;
        }
    }
}
