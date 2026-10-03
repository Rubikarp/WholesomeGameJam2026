using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>
    /// Canvas-space text component. Renders into a <see cref="Canvas"/> via
    /// <see cref="CanvasRenderer"/> with full Unicode support — bidirectional scripts
    /// (Arabic, Hebrew), complex shaping (Devanagari, Thai), proper line breaking, color
    /// emoji, and a markup system via <see cref="ParseRule"/>.
    /// </summary>
    /// <remarks>For world-space text (no Canvas), use <see cref="UniTextWorld"/>.</remarks>
    [AddComponentMenu(UniTextMenu.AddComponent.Canvas)]
    [RequireComponent(typeof(CanvasRenderer))]
    public partial class UniText : UniTextBase
    {
        #region Canvas State

        /// <summary>Cached sub-mesh renderer data to avoid GetComponent calls.</summary>
        private sealed class SubMeshRenderer
        {
            public CanvasRenderer renderer;
            public RectTransform rectTransform;
            public GlyphAtlas atlas;
            public MaterialCloneRef<SurfaceMaterialKey> blendMaterialRef;
        }

        private struct CanvasMeshSlice
        {
            public int vertexOffset;
            public int vertexCount;
            public int triangleOffset;
            public int triangleCount;
        }

        private readonly PooledList<SubMeshRenderer> subMeshRenderers = new();
        private readonly PooledList<SubMeshRenderer> maskStampRenderers = new();
        private PooledBuffer<CanvasMeshSlice> canvasMeshSlices;
        private PooledBuffer<ushort> canvasIndices;

        private const int MaxCanvasSubMeshVertices = 16383 * 4;

        private const string RenderRootName = "-_UTRoot_-";
        private const string MaskStampRootName = "-_UTMaskRoot_-";
        private const string MaskStampRendererName = "-_UTMaskStamp_-";
        private RectTransform renderRoot;
        private RectTransform maskStampRoot;

        private struct StencilPair
        {
            public Material stencil;
            public GlyphAtlas atlas;
        }

        private readonly List<StencilPair> stencilPairs = new();

        private Rect cachedClipRect;
        private bool cachedValidClip;
        private Vector4 cachedClipSoftness;
        private int cachedStencilDepth;
        private bool stencilDepthDirty = true;
        private Mask cachedMask;
        private bool maskDirty = true;
        private Vector2 lastSyncedPivot;

        private Bounds cachedMeshLocalBounds;
        private bool hasCachedMeshBounds;
        private bool cachedCullState;

        private Rect lastClipRect;
        private bool lastClipValid;
        private bool hasLastClip;

        private Canvas watchedCanvas;

        private Mesh maskStampMesh;
        private Material maskStampWriteMat;
        private Material maskStampUnmaskMat;
        private bool maskStampActive;

        private static readonly Vector3[] MaskBoundsVertices = new Vector3[4];
        private static readonly Vector4[] MaskBoundsUvs = new Vector4[4];
        private static readonly Color32[] MaskBoundsColors =
        {
            new(255, 255, 255, 255),
            new(255, 255, 255, 255),
            new(255, 255, 255, 255),
            new(255, 255, 255, 255),
        };
        private static readonly ushort[] MaskBoundsTriangles = { 0, 1, 2, 2, 3, 0 };

        #endregion

        #region Public API

        /// <summary>Gets all canvas renderers used for sub-meshes.</summary>
        public IEnumerable<CanvasRenderer> CanvasRenderers
        {
            get
            {
                for (var i = 0; i < subMeshRenderers.Count; i++)
                    yield return subMeshRenderers[i].renderer;
            }
        }

        #endregion

        #region Abstract Implementations

        protected override void UpdateRendering()
        {
            UniTextDebug.BeginSample("UniText.UpdateRendering");

            if (renderData == null || renderData.Count == 0)
            {
                ClearAllRenderers();
                UniTextDebug.EndSample();
                return;
            }

            UpdateSubMeshes();
            RefreshClipCullAfterMesh();

            UniTextDebug.EndSample();
        }

        protected override void ClearAllRenderers()
        {
            for (var i = 0; i < subMeshRenderers.Count; i++)
            {
                ref var surface = ref subMeshRenderers[i];
                var r = surface.renderer;
                if (r != null) r.Clear();
                surface.blendMaterialRef.Release();
            }
            hasCachedMeshBounds = false;
            if (maskStampActive) ClearMaskStamp();
        }

        /// <inheritdoc/>
        protected override void ApplyVisibility() => ApplyEffectiveCull();

        protected override void OnSetDirty(UniTextDirty flags)
        {
            if ((flags & (UniTextDirty.FullRebuild | UniTextDirty.Layout)) != 0)
            {
                LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
            }
        }

        protected override void OnDeInit()
        {
            ReleaseSubMeshStencilMaterials();
            ReleaseSubMeshBlendMaterials();
            canvasMeshSlices.Return();
            canvasIndices.Return();
            ClearMaskStamp();
            ObjectUtils.SafeDestroy(maskStampMesh);
            maskStampMesh = null;
        }

        /// <summary>Translates Canvas masking notifications into a UniText appearance rebuild.</summary>
        public override void SetMaterialDirty()
        {
            stencilDepthDirty = true;
            maskDirty = true;
            ReleaseSubMeshStencilMaterials();
            SetAppearanceDirty();
        }

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            CollectExistingSubMeshRenderers();
            SyncCanvasWatch();
        }

        protected override void OnDisable()
        {
            SyncCanvasWatch();
            base.OnDisable();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            SyncCanvasWatch();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            SyncCanvasWatch();
        }

        private void SyncCanvasWatch()
        {
            var target = isActiveAndEnabled ? canvas : null;
            if (ReferenceEquals(target, watchedCanvas)) return;
            if (!ReferenceEquals(watchedCanvas, null)) CanvasWatcher.Unwatch(this, watchedCanvas);
            watchedCanvas = target;
            if (target != null) CanvasWatcher.Watch(this, target);
        }

        /// <summary>
        /// Per-canvas frame check shared by every enabled <see cref="UniText"/> on that canvas:
        /// keeps the required additional shader channels present and translates a render-mode
        /// change into a positions rebuild of the members, reading each canvas once per frame
        /// instead of once per text.
        /// </summary>
        private static class CanvasWatcher
        {
            private static readonly List<Canvas> canvases = new();
            private static readonly List<RenderMode> modes = new();
            private static readonly List<List<UniText>> members = new();
            private static readonly Stack<List<UniText>> pool = new();
            private static Action tickCallback;
            private static TickHandle tickHandle;

            internal static void Watch(UniText text, Canvas canvas)
            {
                var index = canvases.IndexOf(canvas);
                if (index < 0)
                {
                    index = canvases.Count;
                    canvases.Add(canvas);
                    modes.Add(canvas.renderMode);
                    members.Add(pool.Count > 0 ? pool.Pop() : new List<UniText>(8));
                    EnsureCanvasShaderChannels(canvas);
                    CoreLoop.CanvasPreRendering.Toggle(ref tickHandle, tickCallback ??= Tick, true);
                }
                members[index].Add(text);
            }

            internal static void Unwatch(UniText text, Canvas canvas)
            {
                var index = canvases.IndexOf(canvas);
                if (index < 0) return;
                var list = members[index];
                list.Remove(text);
                if (list.Count == 0) RemoveAt(index);
            }

            private static void RemoveAt(int index)
            {
                var list = members[index];
                list.Clear();
                pool.Push(list);
                var last = canvases.Count - 1;
                canvases[index] = canvases[last];
                modes[index] = modes[last];
                members[index] = members[last];
                canvases.RemoveAt(last);
                modes.RemoveAt(last);
                members.RemoveAt(last);
                if (canvases.Count == 0)
                    CoreLoop.CanvasPreRendering.Toggle(ref tickHandle, tickCallback, false);
            }

            private static void Tick()
            {
                for (var i = canvases.Count - 1; i >= 0; i--)
                {
                    var canvas = canvases[i];
                    if (canvas == null)
                    {
                        RemoveAt(i);
                        continue;
                    }
                    EnsureCanvasShaderChannels(canvas);
                    var mode = canvas.renderMode;
                    if (mode == modes[i]) continue;
                    modes[i] = mode;
                    var list = members[i];
                    for (var t = 0; t < list.Count; t++) list[t].SetDirty(UniTextDirty.Positions);
                }
            }
        }

        private void OnTransformChildrenChanged()
        {
            if (maskStampRoot != null && maskStampRoot.gameObject.activeSelf &&
                maskStampRoot.GetSiblingIndex() != 0)
                maskStampRoot.SetAsFirstSibling();
        }

        private static void EnsureCanvasShaderChannels(Canvas c)
            => CanvasChannels.Ensure(c,
                AdditionalCanvasShaderChannels.TexCoord1 |
                AdditionalCanvasShaderChannels.TexCoord2 |
                AdditionalCanvasShaderChannels.TexCoord3);

        private bool syncingCanvasColor;
        private int crossFadeStartFrame;
        private Color lastCanvasRendererColor = Color.white;

        public override void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale, bool useAlpha)
        {
            base.CrossFadeColor(targetColor, duration, ignoreTimeScale, useAlpha);
            syncingCanvasColor = true;
            crossFadeStartFrame = Time.frameCount;
            RefreshFrameTick();
        }

        public override void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale)
        {
            base.CrossFadeAlpha(alpha, duration, ignoreTimeScale);
            syncingCanvasColor = true;
            crossFadeStartFrame = Time.frameCount;
            RefreshFrameTick();
        }

        /// <inheritdoc/>
        protected override bool NeedsFrameTick => base.NeedsFrameTick || syncingCanvasColor;

        /// <inheritdoc/>
        protected override void OnFrameTick()
        {
            base.OnFrameTick();
            if (!syncingCanvasColor) return;

            var crColor = canvasRenderer.GetColor();
            if (crColor != lastCanvasRendererColor)
            {
                lastCanvasRendererColor = crColor;
                for (var i = 0; i < subMeshRenderers.Count; i++)
                {
                    var r = subMeshRenderers[i].renderer;
                    if (r != null) r.SetColor(crColor);
                }
            }
            else if (Time.frameCount > crossFadeStartFrame + 1)
            {
                syncingCanvasColor = false;
                RefreshFrameTick();
            }
        }

        #endregion

        #region Canvas Masking

        /// <summary>Sets the clipping rectangle for masking, applying to all sub-mesh renderers. Mask removal reaches a clippable only through this call (never <see cref="Cull"/>), so an invalid rect also resets <see cref="UniTextBase.VisibleWindow"/> and the clip-cull state — otherwise the mesh would stay windowed and culled with no mask to excuse it.</summary>
        public override void SetClipRect(Rect clipRect, bool validRect)
        {
            base.SetClipRect(clipRect, validRect);
            cachedClipRect = clipRect;
            cachedValidClip = validRect;
            if (!validRect)
            {
                VisibleWindow = null;
                hasLastClip = false;
                cachedCullState = false;
            }

            ApplyClipRect(subMeshRenderers, clipRect, validRect);
            ApplyClipRect(maskStampRenderers, clipRect, validRect);
        }

        /// <summary>Sets soft clipping edges for smooth mask transitions on all sub-mesh renderers.</summary>
        public override void SetClipSoftness(Vector2 clipSoftness)
        {
            base.SetClipSoftness(clipSoftness);
            cachedClipSoftness = new Vector4(clipSoftness.x, clipSoftness.y, 0, 0);

            ApplyClipSoftness(subMeshRenderers, cachedClipSoftness);
            ApplyClipSoftness(maskStampRenderers, cachedClipSoftness);
        }

        /// <summary>
        /// Culls against the actual rendered mesh bounds rather than the <see cref="RectTransform"/>
        /// (whose corners serve until a mesh exists). Glyphs can extend beyond the rect (long words,
        /// overflow, outline/glow, modifier transforms), so the default rect-corner test would hide
        /// still-visible meshes.
        /// </summary>
        public override void Cull(Rect clipRect, bool validRect)
        {
            FeedVisibleWindow(clipRect, validRect);

            lastClipRect = clipRect;
            lastClipValid = validRect;
            hasLastClip = true;

            ApplyClipCull(clipRect, validRect, fireEvents: true);
        }

        private void RefreshClipCullAfterMesh()
        {
            if (!hasLastClip || !hasCachedMeshBounds) return;
            ApplyClipCull(lastClipRect, lastClipValid, fireEvents: false);
        }

        private void ApplyClipCull(Rect clipRect, bool validRect, bool fireEvents)
        {
            var clipCull = !validRect || !clipRect.Overlaps(ComputeRootCanvasCullRect(), true);

            if (clipCull != cachedCullState)
            {
                cachedCullState = clipCull;
                ApplyEffectiveCull();
                if (fireEvents)
                {
                    if (onCullStateChanged != null) onCullStateChanged.Invoke(clipCull);
                    OnCullingChanged();
                }
            }
            else
            {
                ApplyEffectiveCull();
            }
        }

        private static void ApplyClipRect(PooledList<SubMeshRenderer> renderers, Rect clipRect, bool validRect)
        {
            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i].renderer;
                if (r == null) continue;
                if (validRect) r.EnableRectClipping(clipRect);
                else
                {
                    r.DisableRectClipping();
                    r.cull = false;
                }
            }
        }

        private static void ApplyClipSoftness(PooledList<SubMeshRenderer> renderers, Vector4 softness)
        {
            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i].renderer;
                if (r != null) r.clippingSoftness = softness;
            }
        }

        private static void ApplyCull(PooledList<SubMeshRenderer> renderers, bool cull)
        {
            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i].renderer;
                if (r != null) r.cull = cull;
            }
        }

        /// <summary>
        /// Sets renderer culling to the union of clip-culling (<see cref="cachedCullState"/>) and
        /// <see cref="UniTextBase.RenderSuppressed"/>, so a clip-cull pass never overrides a
        /// <see cref="UniTextBase.Hide"/> or scene-visibility state.
        /// </summary>
        private void ApplyEffectiveCull()
        {
            var cull = cachedCullState || RenderSuppressed;
            if (canvasRenderer != null) canvasRenderer.cull = cull;
            ApplyCull(subMeshRenderers, cull);
            ApplyCull(maskStampRenderers, cull);
        }

        /// <summary>Restores the effective cull — the union of clip-culling and render suppression — on every culling change, so a direct <see cref="CanvasRenderer.cull"/> write from outside never lifts a <see cref="UniTextBase.Hide"/> or scene-visibility state.</summary>
        public override void OnCullingChanged()
        {
            ApplyEffectiveCull();
            base.OnCullingChanged();
        }

        /// <summary>
        /// Feeds <see cref="UniTextBase.VisibleWindow"/> from the mask clip rect (root-canvas space →
        /// this transform's local space, conservative corner bounds under rotation). No mask, or an
        /// invalid rect, renders everything.
        /// </summary>
        private void FeedVisibleWindow(Rect clipRect, bool validRect)
        {
            var c = canvas;
            if (!validRect || c == null)
            {
                VisibleWindow = null;
                return;
            }

            var toWorld = c.rootCanvas.transform.localToWorldMatrix;
            var toLocal = transform.worldToLocalMatrix;

            var c0 = toLocal.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(new Vector3(clipRect.xMin, clipRect.yMin, 0f)));
            var c1 = toLocal.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(new Vector3(clipRect.xMax, clipRect.yMin, 0f)));
            var c2 = toLocal.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(new Vector3(clipRect.xMax, clipRect.yMax, 0f)));
            var c3 = toLocal.MultiplyPoint3x4(toWorld.MultiplyPoint3x4(new Vector3(clipRect.xMin, clipRect.yMax, 0f)));

            var minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
            var minY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
            var maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
            var maxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));

            VisibleWindow = Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private Rect ComputeRootCanvasCullRect()
        {
            Vector3 min, max;
            if (hasCachedMeshBounds)
            {
                min = cachedMeshLocalBounds.min;
                max = cachedMeshLocalBounds.max;
            }
            else
            {
                var rect = rectTransform.rect;
                min = rect.min;
                max = rect.max;
            }

            var l2W = transform.localToWorldMatrix;
            var c0 = l2W.MultiplyPoint3x4(new Vector3(min.x, min.y, 0f));
            var c1 = l2W.MultiplyPoint3x4(new Vector3(max.x, min.y, 0f));
            var c2 = l2W.MultiplyPoint3x4(new Vector3(max.x, max.y, 0f));
            var c3 = l2W.MultiplyPoint3x4(new Vector3(min.x, max.y, 0f));

            var c = canvas;
            if (c != null)
            {
                var w2L = c.rootCanvas.transform.worldToLocalMatrix;
                c0 = w2L.MultiplyPoint3x4(c0);
                c1 = w2L.MultiplyPoint3x4(c1);
                c2 = w2L.MultiplyPoint3x4(c2);
                c3 = w2L.MultiplyPoint3x4(c3);
            }

            var rminX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
            var rminY = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
            var rmaxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
            var rmaxY = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
            return Rect.MinMaxRect(rminX, rminY, rmaxX, rmaxY);
        }

        #endregion

        #region Sub-mesh Management

        private void CollectExistingSubMeshRenderers()
        {
            ReleaseSubMeshBlendMaterials();
            subMeshRenderers.Clear();
            maskStampRenderers.Clear();

            renderRoot = null;
            maskStampRoot = null;
            for (var i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child.name == RenderRootName) renderRoot = child as RectTransform;
                else if (child.name == MaskStampRootName) maskStampRoot = child as RectTransform;
            }

            CollectChildRenderers(renderRoot, "-_UTSM_-", subMeshRenderers);
            CollectChildRenderers(maskStampRoot, MaskStampRendererName, maskStampRenderers);
            maskStampActive = maskStampRoot != null && maskStampRoot.gameObject.activeSelf;
            if (maskStampActive && maskStampRoot.GetSiblingIndex() != 0)
                maskStampRoot.SetAsFirstSibling();
        }

        private static void CollectChildRenderers(RectTransform root, string namePrefix,
            PooledList<SubMeshRenderer> renderers)
        {
            if (root == null) return;
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (!child.name.StartsWith(namePrefix)) continue;

                var r = child.GetComponent<CanvasRenderer>();
                var rt = child.GetComponent<RectTransform>();
                if (r != null) renderers.Add(new SubMeshRenderer { renderer = r, rectTransform = rt });
            }
        }

        private void UpdateSubMeshes()
        {
            UniTextDebug.BeginSample("UniText.UpdateSubMeshes");

            var textMat = UniTextMaterialCache.Text;
            ValidateCanvasMaterials(textMat);

            var existingCount = subMeshRenderers.Count;

            var currentPivot = rectTransform.pivot;
            if (currentPivot != lastSyncedPivot)
            {
                lastSyncedPivot = currentPivot;
                for (var i = 0; i < existingCount; i++)
                    subMeshRenderers[i].rectTransform.pivot = currentPivot;
                if (maskStampRoot != null) maskStampRoot.pivot = currentPivot;
                for (var i = 0; i < maskStampRenderers.Count; i++)
                    maskStampRenderers[i].rectTransform.pivot = currentPivot;
            }

            if (stencilDepthDirty)
            {
                cachedStencilDepth = 0;
                if (maskable)
                {
                    var rootCanvas = MaskUtilities.FindRootSortOverrideCanvas(transform);
                    cachedStencilDepth = MaskUtilities.GetStencilDepth(transform, rootCanvas);
                }
                stencilDepthDirty = false;
            }
            var stencilDepth = cachedStencilDepth;

            if (maskDirty) { cachedMask = GetComponent<Mask>(); maskDirty = false; }
            var maskComp = cachedMask;
            var asMask = maskComp != null && maskComp.MaskEnabled();
            var showText = !asMask || maskComp.showMaskGraphic;

            var aggregateBounds = default(Bounds);
            var hasAggregate = false;
            var sdfData = default(UniTextRenderData);
            var hasSdfData = false;
            var rendererIndex = 0;

            for (var i = 0; i < renderData.Count; i++)
            {
                var data = renderData[i];
                Material mat;
                GlyphAtlas atlas;
                if (data.materialOverride != null)
                {
                    mat = data.materialOverride;
                    atlas = data.atlasOverride;
                }
                else
                {
                    mat = textMat;
                    atlas = null;
                }

                if (data.vertexCount <= MaxCanvasSubMeshVertices || data.triangleCount == 0)
                {
                    var mesh = BuildMeshForSegment(rendererIndex, in data);
                    ApplyCanvasMesh(rendererIndex++, mesh, mat, atlas, data.blend, stencilDepth, showText,
                        ref aggregateBounds, ref hasAggregate);
                }
                else
                {
                    CollectCanvasMeshSlices(in data);
                    for (var sliceIndex = 0; sliceIndex < canvasMeshSlices.count; sliceIndex++)
                    {
                        var mesh = BuildMeshForSlice(rendererIndex, in data, in canvasMeshSlices[sliceIndex]);
                        ApplyCanvasMesh(rendererIndex++, mesh, mat, atlas, data.blend, stencilDepth, showText,
                            ref aggregateBounds, ref hasAggregate);
                    }
                }

                if (asMask && !hasSdfData && data.materialOverride == null
                    && data.vertexCount > 0)
                {
                    sdfData = data;
                    hasSdfData = true;
                }
            }

            for (var i = rendererIndex; i < existingCount; i++)
            {
                ref var surface = ref subMeshRenderers[i];
                var r = surface.renderer;
                if (r != null) { r.Clear(); r.gameObject.SetActive(false); }
                surface.blendMaterialRef.Release();
            }

            cachedMeshLocalBounds = aggregateBounds;
            hasCachedMeshBounds = hasAggregate;

            if (asMask && hasSdfData) BuildMaskStamp(stencilDepth, textMat, in sdfData);
            else if (maskStampActive) ClearMaskStamp();

            ApplyEffectiveCull();
            UniTextDebug.EndSample();
        }

        private void ValidateCanvasMaterials(Material textMaterial)
        {
            for (var i = 0; i < renderData.Count; i++)
            {
                var data = renderData[i];
                if (data.vertexCount == 0) continue;
                var material = data.materialOverride != null ? data.materialOverride : textMaterial;
                if (material == null)
                    throw new InvalidOperationException(
                        $"UniText '{name}' cannot render segment {i} because its required text material is unavailable.");
                SurfaceMaterialPool.ValidateUse(material, data.blend);
            }
        }

        private void ApplyCanvasMesh(int rendererIndex, Mesh mesh, Material material, GlyphAtlas atlas,
            LayerBlend blend, int stencilDepth, bool showText, ref Bounds aggregateBounds,
            ref bool hasAggregate)
        {
            if (mesh != null && mesh.vertexCount > 0)
            {
                var bounds = mesh.bounds;
                if (!hasAggregate) { aggregateBounds = bounds; hasAggregate = true; }
                else aggregateBounds.Encapsulate(bounds);
            }

            AssignCanvasRenderer(rendererIndex, mesh, material, atlas, blend, stencilDepth);
            if (!showText && subMeshRenderers[rendererIndex].renderer != null)
                subMeshRenderers[rendererIndex].renderer.gameObject.SetActive(false);
        }

        private Mesh BuildMeshForSegment(int rendererIndex, in UniTextRenderData data)
            => BuildMeshInto(SharedMeshes.Get(rendererIndex), in data, IndexFormat.UInt16);

        private Mesh BuildMeshForSlice(int rendererIndex, in UniTextRenderData data, in CanvasMeshSlice slice)
            => BuildMeshSliceInto(SharedMeshes.Get(rendererIndex), in data, in slice);

        private Mesh BuildMeshSliceInto(Mesh mesh, in UniTextRenderData data, in CanvasMeshSlice slice)
        {
            SetMeshVertices(mesh, in data, data.vertexOffset + slice.vertexOffset, slice.vertexCount,
                IndexFormat.UInt16);

            canvasIndices.SetCount(slice.triangleCount);
            for (var i = 0; i < slice.triangleCount; i++)
                canvasIndices.data[i] =
                    (ushort)(data.triangles[slice.triangleOffset + i] - slice.vertexOffset);
            mesh.SetTriangles(canvasIndices.data, 0, slice.triangleCount, 0);
            return mesh;
        }

        private static Mesh BuildMeshInto(Mesh mesh, in UniTextRenderData data, IndexFormat indexFormat)
        {
            SetMeshVertices(mesh, in data, data.vertexOffset, data.vertexCount, indexFormat);
            if (data.vertexCount == 0 || data.triangleCount == 0) return mesh;
            mesh.SetTriangles(data.triangles, data.triangleOffset, data.triangleCount, 0);
            return mesh;
        }

        private static void SetMeshVertices(Mesh mesh, in UniTextRenderData data,
            int vertexOffset, int vertexCount, IndexFormat indexFormat)
        {
            mesh.Clear(false);
            if (mesh.indexFormat != indexFormat) mesh.indexFormat = indexFormat;
            if (vertexCount == 0) return;

            mesh.SetVertices(data.vertices, vertexOffset, vertexCount);
            mesh.SetUVs(0, data.uvs0, vertexOffset, vertexCount);
            if (data.hasUv1 && data.uvs1 != null)
                mesh.SetUVs(1, data.uvs1, vertexOffset, vertexCount);
            if (data.hasUv2 && data.uvs2 != null)
                mesh.SetUVs(2, data.uvs2, vertexOffset, vertexCount);
            if (data.hasUv3 && data.uvs3 != null)
                mesh.SetUVs(3, data.uvs3, vertexOffset, vertexCount);
            mesh.SetColors(data.colors, vertexOffset, vertexCount);
        }

        private void CollectCanvasMeshSlices(in UniTextRenderData data)
        {
            if (data.triangleCount % 3 != 0)
                throw new InvalidOperationException("Canvas mesh triangle index count must be divisible by three.");

            canvasMeshSlices.FakeClear();
            var triangles = data.triangles;
            var end = data.triangleOffset + data.triangleCount;
            var sliceStart = data.triangleOffset;
            var sliceMin = int.MaxValue;
            var sliceMax = int.MinValue;

            for (var i = data.triangleOffset; i < end; i += 3)
            {
                var a = triangles[i];
                var b = triangles[i + 1];
                var c = triangles[i + 2];
                if ((uint)a >= (uint)data.vertexCount ||
                    (uint)b >= (uint)data.vertexCount ||
                    (uint)c >= (uint)data.vertexCount)
                    throw new InvalidOperationException("Canvas mesh triangle references a vertex outside its segment.");

                var triangleMin = Math.Min(a, Math.Min(b, c));
                var triangleMax = Math.Max(a, Math.Max(b, c));
                if (triangleMax - triangleMin >= MaxCanvasSubMeshVertices)
                    throw new InvalidOperationException(
                        "A single canvas triangle spans more vertices than a UInt16 mesh can address.");

                var nextMin = Math.Min(sliceMin, triangleMin);
                var nextMax = Math.Max(sliceMax, triangleMax);
                if (sliceMin != int.MaxValue && nextMax - nextMin >= MaxCanvasSubMeshVertices)
                {
                    canvasMeshSlices.Add(new CanvasMeshSlice
                    {
                        vertexOffset = sliceMin,
                        vertexCount = sliceMax - sliceMin + 1,
                        triangleOffset = sliceStart,
                        triangleCount = i - sliceStart,
                    });
                    sliceStart = i;
                    sliceMin = triangleMin;
                    sliceMax = triangleMax;
                }
                else
                {
                    sliceMin = nextMin;
                    sliceMax = nextMax;
                }
            }

            canvasMeshSlices.Add(new CanvasMeshSlice
            {
                vertexOffset = sliceMin,
                vertexCount = sliceMax - sliceMin + 1,
                triangleOffset = sliceStart,
                triangleCount = end - sliceStart,
            });
        }

        private void AssignCanvasRenderer(int crIndex, Mesh mesh, Material mat, GlyphAtlas atlas,
            LayerBlend blend, int stencilDepth)
        {
            var existingCount = subMeshRenderers.Count;
            if (crIndex < existingCount)
            {
                ref var surface = ref subMeshRenderers[crIndex];
                var r = surface.renderer;
                if (r != null)
                {
                    if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
                    SetSubMeshRendererData(ref surface, mesh, mat, atlas, blend, crIndex, stencilDepth);
                    return;
                }
                surface.blendMaterialRef.Release();
            }

            var newR = CreateSubMeshRenderer(crIndex, mesh, mat, atlas, blend, stencilDepth);
            if (crIndex < existingCount) subMeshRenderers[crIndex] = newR;
            else subMeshRenderers.Add(newR);
        }

        private void SetSubMeshRendererData(ref SubMeshRenderer surface, Mesh mesh, Material mat, GlyphAtlas atlas,
            LayerBlend blend, int crIndex, int stencilDepth)
        {
            var r = surface.renderer;
            surface.atlas = atlas;
            if (mesh == null || mesh.vertexCount == 0)
            {
                r.Clear();
                surface.blendMaterialRef.Release();
                return;
            }

            r.SetMesh(mesh);

            if (mat == null)
            {
                surface.blendMaterialRef.Release();
                r.materialCount = 0;
                return;
            }

            if (blend == LayerBlend.Normal)
            {
                surface.blendMaterialRef.Release();
            }
            else
            {
                var key = new SurfaceMaterialKey(mat, blend);
                mat = surface.blendMaterialRef.Bind(SurfaceMaterialPool.Instance, key, mat);
            }

            var matToUse = mat;
            if (stencilDepth > 0)
            {
                var stencilId = (1 << stencilDepth) - 1;
                var stencilMat = StencilMaterial.Add(mat, stencilId, StencilOp.Keep, CompareFunction.Equal, ColorWriteMask.All, stencilId, 0);

                while (stencilPairs.Count <= crIndex) stencilPairs.Add(default);
                if (stencilPairs[crIndex].stencil != null) StencilMaterial.Remove(stencilPairs[crIndex].stencil);

                stencilPairs[crIndex] = new StencilPair { stencil = stencilMat, atlas = atlas };
                matToUse = stencilMat;
            }

            r.materialCount = 1;
            r.SetMaterial(matToUse, 0);
        }

        private SubMeshRenderer CreateSubMeshRenderer(int index, Mesh mesh, Material mat, GlyphAtlas atlas,
            LayerBlend blend, int stencilDepth)
        {
            var go = new GameObject("-_UTSM_-") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(GetOrCreateRenderRoot(), false);

            var rt = go.AddComponent<RectTransform>();
            rt.pivot = rectTransform.pivot;
            rt.StretchToParent();

            var r = go.AddComponent<CanvasRenderer>();
            var surface = new SubMeshRenderer { renderer = r, rectTransform = rt };
            SetSubMeshRendererData(ref surface, mesh, mat, atlas, blend, index, stencilDepth);

            if (cachedValidClip) r.EnableRectClipping(cachedClipRect);
            r.clippingSoftness = cachedClipSoftness;
            r.cull = cachedCullState || RenderSuppressed;

            return surface;
        }

        #endregion

        #region Cleanup

        private void ReleaseSubMeshStencilMaterials()
        {
            for (var i = 0; i < stencilPairs.Count; i++)
            {
                if (stencilPairs[i].stencil != null)
                    StencilMaterial.Remove(stencilPairs[i].stencil);
            }
            stencilPairs.Clear();
        }

        private void ReleaseSubMeshBlendMaterials()
        {
            for (var i = 0; i < subMeshRenderers.Count; i++)
                subMeshRenderers[i].blendMaterialRef.Release();
        }

        #endregion

        #region Mask Stamp

        /// <summary>
        /// Emits the stencil-write pass that lets this component act as a <see cref="UnityEngine.UI.Mask"/>.
        /// The matching pop instruction stays on this component's own <see cref="CanvasRenderer"/> so it
        /// runs after every descendant. A mask that exceeds the UInt16 Canvas limit writes its glyphs
        /// through ordered child renderers while the root renderer clears and later restores the complete
        /// stamp bounds. The stamp never writes color; <see cref="Mask.showMaskGraphic"/> only controls the
        /// visible text renderers.
        /// </summary>
        private void BuildMaskStamp(int stencilDepth, Material baseMat, in UniTextRenderData sdfData)
        {
            if (baseMat == null) { ClearMaskStamp(); return; }
            if (sdfData.vertexCount == 0 || sdfData.triangleCount == 0) { ClearMaskStamp(); return; }

            var bit = 1 << stencilDepth;
            const ColorWriteMask noColor = 0;

            Material write, unmask;
            if (bit == 1)
            {
                write = StencilMaterial.Add(baseMat, 1, StencilOp.Replace, CompareFunction.Always, noColor);
                unmask = StencilMaterial.Add(baseMat, 1, StencilOp.Zero, CompareFunction.Always, noColor);
            }
            else
            {
                var readMask = bit - 1;
                var writeMask = bit | (bit - 1);
                write = StencilMaterial.Add(baseMat, writeMask, StencilOp.Replace, CompareFunction.Equal, noColor, readMask, writeMask);
                unmask = StencilMaterial.Add(baseMat, readMask, StencilOp.Replace, CompareFunction.Equal, noColor, readMask, writeMask);
            }

            if (maskStampWriteMat != null) StencilMaterial.Remove(maskStampWriteMat);
            maskStampWriteMat = write;
            if (maskStampUnmaskMat != null) StencilMaterial.Remove(maskStampUnmaskMat);
            maskStampUnmaskMat = unmask;

            write.EnableKeyword("UNITY_UI_ALPHACLIP");
            unmask.DisableKeyword("UNITY_UI_ALPHACLIP");

            maskStampMesh ??= new Mesh { name = "-_UTSM_-Stamp", hideFlags = HideFlags.HideAndDontSave };
            if (sdfData.vertexCount <= MaxCanvasSubMeshVertices)
            {
                DisableMaskStampRenderers();
                BuildMeshInto(maskStampMesh, in sdfData, IndexFormat.UInt16);
                SetRootMaskStamp(maskStampMesh, maskStampWriteMat);
            }
            else
            {
                BuildSplitMaskStamp(in sdfData);
            }

            maskStampActive = true;
        }

        private void BuildSplitMaskStamp(in UniTextRenderData data)
        {
            CollectCanvasMeshSlices(in data);
            var root = GetOrCreateMaskStampRoot();
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (root.GetSiblingIndex() != 0) root.SetAsFirstSibling();

            var aggregateBounds = default(Bounds);
            var hasAggregate = false;
            var rendererIndex = 0;
            for (var i = 0; i < canvasMeshSlices.count; i++)
            {
                var mesh = BuildMeshSliceInto(maskStampMesh, in data, in canvasMeshSlices[i]);
                var bounds = mesh.bounds;
                if (!hasAggregate) { aggregateBounds = bounds; hasAggregate = true; }
                else aggregateBounds.Encapsulate(bounds);
                AssignMaskStampRenderer(rendererIndex++, mesh, maskStampWriteMat);
            }

            for (var i = rendererIndex; i < maskStampRenderers.Count; i++)
            {
                var r = maskStampRenderers[i].renderer;
                if (r != null) { r.Clear(); r.gameObject.SetActive(false); }
            }

            BuildMaskBoundsMesh(maskStampMesh, aggregateBounds);
            SetRootMaskStamp(maskStampMesh, maskStampUnmaskMat);
        }

        private void SetRootMaskStamp(Mesh mesh, Material material)
        {
            canvasRenderer.SetMesh(mesh);
            canvasRenderer.materialCount = 1;
            canvasRenderer.SetMaterial(material, 0);
            canvasRenderer.hasPopInstruction = true;
            canvasRenderer.popMaterialCount = 1;
            canvasRenderer.SetPopMaterial(maskStampUnmaskMat, 0);
        }

        private void AssignMaskStampRenderer(int index, Mesh mesh, Material material)
        {
            var existingCount = maskStampRenderers.Count;
            if (index < existingCount)
            {
                var renderer = maskStampRenderers[index].renderer;
                if (renderer != null)
                {
                    if (!renderer.gameObject.activeSelf) renderer.gameObject.SetActive(true);
                    SetMaskStampRendererData(renderer, mesh, material);
                    return;
                }
            }

            var newRenderer = CreateMaskStampRenderer(mesh, material);
            if (index < existingCount) maskStampRenderers[index] = newRenderer;
            else maskStampRenderers.Add(newRenderer);
        }

        private SubMeshRenderer CreateMaskStampRenderer(Mesh mesh, Material material)
        {
            var go = new GameObject(MaskStampRendererName) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(GetOrCreateMaskStampRoot(), false);

            var rt = go.AddComponent<RectTransform>();
            rt.pivot = rectTransform.pivot;
            rt.StretchToParent();

            var renderer = go.AddComponent<CanvasRenderer>();
            SetMaskStampRendererData(renderer, mesh, material);
            if (cachedValidClip) renderer.EnableRectClipping(cachedClipRect);
            renderer.clippingSoftness = cachedClipSoftness;
            renderer.cull = cachedCullState || RenderSuppressed;
            return new SubMeshRenderer { renderer = renderer, rectTransform = rt };
        }

        private static void SetMaskStampRendererData(CanvasRenderer renderer, Mesh mesh, Material material)
        {
            renderer.SetMesh(mesh);
            renderer.materialCount = 1;
            renderer.SetMaterial(material, 0);
        }

        private static void BuildMaskBoundsMesh(Mesh mesh, Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            var z = bounds.center.z;
            MaskBoundsVertices[0] = new Vector3(min.x, min.y, z);
            MaskBoundsVertices[1] = new Vector3(min.x, max.y, z);
            MaskBoundsVertices[2] = new Vector3(max.x, max.y, z);
            MaskBoundsVertices[3] = new Vector3(max.x, min.y, z);

            mesh.Clear(false);
            if (mesh.indexFormat != IndexFormat.UInt16) mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertices(MaskBoundsVertices);
            mesh.SetUVs(0, MaskBoundsUvs);
            mesh.SetColors(MaskBoundsColors);
            mesh.SetTriangles(MaskBoundsTriangles, 0);
        }

        private void DisableMaskStampRenderers()
        {
            for (var i = 0; i < maskStampRenderers.Count; i++)
            {
                var r = maskStampRenderers[i].renderer;
                if (r != null) { r.Clear(); r.gameObject.SetActive(false); }
            }
            if (maskStampRoot != null && maskStampRoot.gameObject.activeSelf)
                maskStampRoot.gameObject.SetActive(false);
        }

        private void ClearMaskStamp()
        {
            if (maskStampWriteMat != null) { StencilMaterial.Remove(maskStampWriteMat); maskStampWriteMat = null; }
            if (maskStampUnmaskMat != null) { StencilMaterial.Remove(maskStampUnmaskMat); maskStampUnmaskMat = null; }

            if (canvasRenderer != null)
            {
                canvasRenderer.hasPopInstruction = false;
                canvasRenderer.popMaterialCount = 0;
                canvasRenderer.Clear();
            }

            DisableMaskStampRenderers();
            maskStampActive = false;
        }

        private RectTransform GetOrCreateMaskStampRoot()
        {
            if (maskStampRoot != null) return maskStampRoot;

            var go = new GameObject(MaskStampRootName) { hideFlags = HideFlags.HideAndDontSave };
            maskStampRoot = go.AddComponent<RectTransform>();
            maskStampRoot.SetParent(transform, false);
            maskStampRoot.SetAsFirstSibling();
            maskStampRoot.pivot = rectTransform.pivot;
            maskStampRoot.StretchToParent();
            return maskStampRoot;
        }
        
        private RectTransform GetOrCreateRenderRoot()
        {
            if (renderRoot != null) return renderRoot;

            var go = new GameObject(RenderRootName) { hideFlags = HideFlags.HideAndDontSave };
            renderRoot = go.AddComponent<RectTransform>();
            renderRoot.SetParent(transform, false);
            renderRoot.SetAsFirstSibling();
            renderRoot.pivot = rectTransform.pivot;
            renderRoot.StretchToParent();
            return renderRoot;
        }

        /// <summary>
        /// Single hidden child holding every visible render object this component spawns — glyph sub-meshes
        /// and inline media. Kept before user-authored children, after the optional internal mask stamp, so
        /// the text composites <em>below</em> nested graphics. Highlights still self-order around it
        /// ("Behind" before, "Above" after). One node is positioned instead of each renderer. Main thread
        /// only — creates a GameObject.
        /// </summary>
        public override RectTransform RenderRoot => GetOrCreateRenderRoot();

        #endregion
    }
}
