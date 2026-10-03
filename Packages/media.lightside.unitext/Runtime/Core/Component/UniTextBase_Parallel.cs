using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;


namespace LightSide
{
    public abstract partial class UniTextBase
    {
        #region Cached Data for Parallel

        /// <summary>
        /// Snapshot of main-thread state captured before parallel processing — Unity APIs
        /// (<see cref="RectTransform.rect"/>, etc.) cannot be touched from worker threads.
        /// </summary>
        public struct CachedTransformData
        {
            /// <summary>Component name for worker-safe diagnostics. Captured on the first rebuild in players; always fresh in the editor.</summary>
            public string name;
            public RectTransform rectTransform;
            /// <summary>The padded inner rect (<see cref="GetPaddedRect"/>) snapshotted on the main thread.</summary>
            public Rect rect;

            /// <summary>Local-space visible band from <see cref="UniTextBase.VisibleWindow"/>, padded with the culling hysteresis margin; ±infinity when culling is off.</summary>
            public float visibleYMin, visibleYMax;
        }

        /// <summary>Cached transform data captured before parallel processing.</summary>
        [NonSerialized] public CachedTransformData cachedTransformData;

        [NonSerialized] private string cachedName;

        [NonSerialized] private List<Action> postCommitActions;
        [NonSerialized] private Exception processingException;

        /// <summary>
        /// Defers <paramref name="action"/> to the main thread, right before this component's
        /// <see cref="LayoutCommitted"/> of the current pipeline pass. Parse-path hooks (which may run
        /// on a rebuild worker) schedule Unity-API side effects here instead of executing them inline.
        /// The queue is per-component and only ever touched by the single thread currently processing
        /// this component, so no synchronization is needed. Duplicate actions coalesce; actions run
        /// once and are dropped.
        /// </summary>
        internal void QueuePostCommit(Action action)
        {
            if (action == null) return;
            postCommitActions ??= new List<Action>(4);
            if (!postCommitActions.Contains(action)) postCommitActions.Add(action);
        }

        private void FlushPostCommitActions()
        {
            if (postCommitActions == null) return;
            try
            {
                for (var i = 0; i < postCommitActions.Count; i++)
                {
#if UNITEXT_PROFILE
                    var sampleDepth = UniTextDebug.SampleDepth;
#endif
                    try
                    {
                        postCommitActions[i]?.Invoke();
                    }
                    catch (Exception exception)
                    {
#if UNITEXT_PROFILE
                        UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                        UnityEngine.Debug.LogException(exception, this);
                    }
                }
            }
            finally
            {
                postCommitActions.Clear();
            }
        }

        protected virtual void PrepareForParallel()
        {
            var visibleYMin = float.NegativeInfinity;
            var visibleYMax = float.PositiveInfinity;
            if (visibleWindow is { } window)
            {
                var pad = window.height * VisibleWindowPadding;
                visibleYMin = window.yMin - pad;
                visibleYMax = window.yMax + pad;
            }

#if UNITY_EDITOR
            cachedName = name;
#else
            cachedName ??= name;
#endif
            cachedTransformData = new CachedTransformData
            {
                name = cachedName,
                rectTransform = rectTransform,
                rect = GetPaddedRect(),
                visibleYMin = visibleYMin,
                visibleYMax = visibleYMax,
            };

            textResolver?.PrepareForParallel();
            StampLayerSequences();
        }

        /// <summary>
        /// Per rebuild, stamps layer ordering and runs each modifier's main-thread
        /// <see cref="BaseModifier.PrepareForParallel"/> capture (aggregates forward to children via their
        /// own override). With no explicit fill (<see cref="ILayer.ClaimsFill"/>), outward effects
        /// (<see cref="ILayer.RendersBehindFill"/>) get a dense band of sequences below the implicit fill so a
        /// lone stroke/shadow renders behind the text; with an explicit fill, every layer keeps plain
        /// style-list order so the author controls stacking. A first pass detects the fill and sizes the
        /// behind band (kept dense for the counting-sort's bucket range). The pass also publishes the
        /// overlay band — the bias that clears every stamped sequence, and the sequence it starts at —
        /// so a virtual-glyph emitter can raise its whole stack above the stack it draws over.
        /// </summary>
        private void StampLayerSequences()
        {
            var hasFill = false;
            var behindCount = 0;
            var pre = EnumerateLiveStyles();
            while (pre.MoveNext())
            {
                var modifier = pre.Current.Modifier;
                if (modifier != null) ScanLayers(modifier, ref hasFill, ref behindCount);
            }
            var chromeModifiers = parseProjection.chromeModifiers;
            if (chromeModifiers != null)
                for (var i = 0; i < chromeModifiers.Count; i++)
                    ScanLayers(chromeModifiers[i], ref hasFill, ref behindCount);

            var frontSeq = 0;
            var behindSeq = -(behindCount + 1);
            var e = EnumerateLiveStyles();
            while (e.MoveNext())
            {
                var modifier = e.Current.Modifier;
                if (modifier == null) continue;
                StampLayer(modifier, hasFill, ref frontSeq, ref behindSeq);
                modifier.PrepareForParallel();
            }
            if (chromeModifiers != null)
                for (var i = 0; i < chromeModifiers.Count; i++)
                {
                    var modifier = chromeModifiers[i];
                    StampLayer(modifier, hasFill, ref frontSeq, ref behindSeq);
                    modifier.PrepareForParallel();
                }

            meshGenerator.overlayBandStart = frontSeq;
            meshGenerator.overlayBias = frontSeq + behindCount + 1;
        }

        private static void ScanLayers(BaseModifier modifier, ref bool hasFill, ref int behindCount)
        {
            if (modifier is ILayer layer)
            {
                if (layer.ClaimsFill) hasFill = true;
                if (layer.RendersBehindFill) behindCount++;
            }
            if (modifier.Children is { } children)
                for (var i = 0; i < children.Count; i++)
                    if (children[i] != null)
                        ScanLayers(children[i], ref hasFill, ref behindCount);
        }

        private static void StampLayer(BaseModifier modifier, bool hasFill, ref int frontSeq, ref int behindSeq)
        {
            if (modifier is ILayer layer)
                layer.LayerSequence = !hasFill && layer.RendersBehindFill ? behindSeq++ : frontSeq++;
            if (modifier.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i] != null)
                    StampLayer(children[i], hasFill, ref frontSeq, ref behindSeq);
            }
        }

        #endregion

        #region Static Batch Processing

        /// <summary>Gets or sets whether parallel processing is enabled for multiple components.</summary>
        public static bool UseParallel { get; set; } = true;

        /// <summary>
        /// Occurs when the current canvas update cycle is about to process text, once before any
        /// processing runs. Fires whether or not any component is dirty. Per-component consumers
        /// must use the instance <see cref="LayoutCommitted"/>/<see cref="FrameUpdated"/> events
        /// instead — a subscriber here pays on every canvas update in the scene.
        /// </summary>
        internal static event Action ProcessingStarted;

        /// <summary>
        /// Occurs when the processing sweep has completed, once before canvas
        /// rendering. Same audience rules as <see cref="ProcessingStarted"/>.
        /// </summary>
        internal static event Action ProcessingEnded;
        private static PooledBuffer<UniTextBase> componentsBuffer;
        private static bool isInitialized;
        private static bool useParallel;
        private static bool prepassSucceeded;
        private static int processingCount;

        private static void InvokeProcessingEvent(Action callbacks)
        {
            if (callbacks == null) return;
#if UNITEXT_PROFILE
            var sampleDepth = UniTextDebug.SampleDepth;
#endif
            try
            {
                callbacks();
            }
            catch (Exception exception)
            {
#if UNITEXT_PROFILE
                UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                UnityEngine.Debug.LogException(exception);
            }
        }

        private static UniTextBase[] parallelComponents;
        private static Action<UniTextBase> parallelAction;

        /// <summary>
        /// Adapter that lets the component batches ride <see cref="WorkerPool"/>'s generic index
        /// dispatch without allocating a closure. Snapshots the static slots into locals so a
        /// straggler worker that outlives a join timeout observes a consistent pair instead of
        /// racing <see cref="ExecuteParallel"/>'s cleanup.
        /// </summary>
        private static readonly Action<int> parallelAdapter = static i =>
        {
            var components = parallelComponents;
            var action = parallelAction;
            if (components == null || action == null) return;
            var comp = components[i];
            if (comp != null) ExecuteComponent(comp, action);
        };

        private static void ExecuteComponent(UniTextBase component, Action<UniTextBase> action)
        {
            if (component.processingException != null) return;
#if UNITEXT_PROFILE
            var sampleDepth = UniTextDebug.SampleDepth;
#endif
            try
            {
                action(component);
            }
            catch (Exception exception)
            {
#if UNITEXT_PROFILE
                UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                component.FailProcessing(exception);
            }
        }

        private void FailProcessing(Exception exception)
        {
            Interlocked.CompareExchange(ref processingException, exception, null);
        }

        private void BeginProcessing()
        {
            isProcessing = true;
            deferredDirtyFlags = UniTextDirty.None;
            deferredCommitChanges = UniTextCommitChanges.None;
            processingCommitChanges = pendingCommitChanges;
            pendingCommitChanges = UniTextCommitChanges.None;
            processingException = null;
        }

        private static void ExecuteParallel(int count, Action<UniTextBase> action)
        {
            parallelComponents = componentsBuffer.data;
            parallelAction = action;
            try
            {
                WorkerPool.Execute(count, parallelAdapter);
            }
            finally
            {
                parallelComponents = null;
                parallelAction = null;
            }
        }

        private static void ExecuteComponents(int count, Action<UniTextBase> action)
        {
            if (useParallel)
            {
                ExecuteParallel(count, action);
                return;
            }

            for (var i = 0; i < count; i++)
                ExecuteComponent(componentsBuffer[i], action);
        }

        #region Parallel Atlas Pipeline

        /// <summary>
        /// Walks each dirty component's shaped runs and virtual glyph buffers (the pump's own
        /// data) and marks the needed glyphs into <see cref="GlyphBatchRasterizer"/>'s batches;
        /// the rasterizer owns everything from there.
        /// </summary>
        private static void CollectGlyphRequestsFromAllComponents(PooledBuffer<UniTextBase> components, int count)
        {
            GlyphBatchRasterizer.BeginCollection();

            for (int c = 0; c < count; c++)
                ExecuteComponent(components[c], static component => component.CollectGlyphRequests());
        }

        private void CollectGlyphRequests()
        {
                var tp = textProcessor;
                if (tp == null || !tp.HasValidFirstPassData) return;
                if (tp.HasValidGlyphsInAtlas)
                {
                    CatZones.raster.MeowFormat("[Collect] '{0}': SKIP re-collection (HasValidGlyphsInAtlas=true)", cachedTransformData.name);
                    return;
                }
                CatZones.raster.MeowFormat("[Collect] '{0}': collecting shaped glyphs (HasValidGlyphsInAtlas=false)", cachedTransformData.name);

                var fontProvider = tp.FontProviderForAtlas;
                if (fontProvider == null) return;

                ReleaseRefsForRebuild();

                var renderMode = RenderMode;
                var shapedRuns = tp.buf.shapedRuns.Span;
                var shapedGlyphs = tp.buf.shapedGlyphs.Span;

                var varMap = tp.buf.variationMap;
                var virtualCodepoints = tp.buf.virtualCodepoints;
                var fieldAttribute = tp.buf.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.ColorGlyphField);
                var fieldRequests = fieldAttribute is { Count: > 0 } ? fieldAttribute.buffer.data : null;
                var fieldRequestCount = fieldRequests != null ? fieldAttribute.Count : 0;

                for (int r = 0; r < shapedRuns.Length; r++)
                {
                    ref readonly var run = ref shapedRuns[r];
                    var font = fontProvider.GetFont(run.fontId);
                    if (font is null) continue;
                    var runFieldRequests = font.IsColor ? fieldRequests : null;

                    long runVarHash = 0;
                    int[] runFtCoords = null;
                    if (varMap != null && varMap.TryGetValue(run.fontId, out var varInfo))
                    {
                        runVarHash = varInfo.varHash48;
                        runFtCoords = varInfo.ftCoords;
                    }

                    var codepoints = tp.buf.codepoints;
                    var provider = UnicodeData.Provider;
                    var end = run.glyphStart + run.glyphCount;

                    ref var batchEntry = ref GlyphBatchRasterizer.GetOrCreateEntry(font, renderMode, runVarHash);
                    if (runFtCoords != null)
                        batchEntry.ftCoords = runFtCoords;

                    for (int i = 0; i < virtualCodepoints.count; i++)
                    {
                        var virtualIndex = font.GetGlyphIndexForUnicode(virtualCodepoints[i]);
                        if (virtualIndex != 0)
                            batchEntry.AddCharacter(virtualCodepoints[i], virtualIndex);
                    }

                    for (int g = run.glyphStart; g < end; g++)
                    {
                        var glyphIndex = (uint)shapedGlyphs[g].glyphId;
                        if (glyphIndex == 0)
                        {
                            var cp = codepoints[shapedGlyphs[g].cluster];
                            var cat = provider.GetGeneralCategory(cp);
                            if (cat is GeneralCategory.Cc or GeneralCategory.Cf
                                or GeneralCategory.Zl or GeneralCategory.Zp)
                                continue;
                        }

                        if (glyphIndex >= GlyphBatchRasterizer.GlyphBitsLength * 64)
                            continue;
                        var cluster = shapedGlyphs[g].cluster;
                        if (runFieldRequests != null && (uint)cluster < (uint)fieldRequestCount
                                                     && runFieldRequests[cluster] != 0)
                            batchEntry.RequestField(glyphIndex, runFieldRequests[cluster]);
                        else
                            batchEntry.Add(glyphIndex);
                    }
                }

                var vc = tp.buf.virtualCodepoints;
                for (int i = 0; i < vc.count; i++)
                {
                    var unicode = vc[i];
                    var fontId = fontProvider.FindFontForCodepoint((int)unicode);
                    var font = fontProvider.GetFont(fontId);
                    if (font == null) continue;

                    var glyphIndex = font.GetGlyphIndexForUnicode(unicode);

                    ref var entry = ref GlyphBatchRasterizer.GetOrCreateEntry(font, renderMode);
                    entry.AddCharacter(unicode, glyphIndex);

                    if (varMap != null)
                    {
                        var baseFontHash = font.FontDataHash;
                        foreach (var kvp in varMap)
                        {
                            if (kvp.Value.baseFontHash != baseFontHash) continue;

                            ref var varEntry = ref GlyphBatchRasterizer.GetOrCreateEntry(font, renderMode, kvp.Value.varHash48);
                            if (varEntry.ftCoords == null)
                                varEntry.ftCoords = kvp.Value.ftCoords;
                            varEntry.AddCharacter(unicode, glyphIndex);
                        }
                    }
                }

                var vGlyphs = tp.buf.virtualGlyphs;
                for (int i = 0; i < vGlyphs.count; i++)
                {
                    var vGlyph = vGlyphs[i];
                    var vFont = fontProvider.GetFont(vGlyph.fontId);
                    if (vFont == null) continue;

                    var vIndex = vGlyph.glyphId;
                    if (vIndex >= GlyphBatchRasterizer.GlyphBitsLength * 64) continue;

                    ref var vEntry = ref GlyphBatchRasterizer.GetOrCreateEntry(vFont, renderMode);
                    if (vGlyph.fieldExtent != 0 && vFont.IsColor)
                        vEntry.RequestField(vIndex, vGlyph.fieldExtent);
                    else
                        vEntry.Add(vIndex);

                    if (varMap != null)
                    {
                        var baseFontHash = vFont.FontDataHash;
                        foreach (var kvp in varMap)
                        {
                            if (kvp.Value.baseFontHash != baseFontHash) continue;

                            ref var vVarEntry = ref GlyphBatchRasterizer.GetOrCreateEntry(vFont, renderMode, kvp.Value.varHash48);
                            if (vVarEntry.ftCoords == null)
                                vVarEntry.ftCoords = kvp.Value.ftCoords;
                            vVarEntry.Add(vIndex);
                        }
                    }
                }
        }

        private static void MarkCollectedGlyphRequests(PooledBuffer<UniTextBase> components, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var component = components[i];
                if (component.processingException != null) continue;
                var tp = component.textProcessor;
                if (tp != null && tp.HasValidFirstPassData && tp.FontProviderForAtlas != null)
                    tp.HasValidGlyphsInAtlas = true;
            }
        }

        #endregion

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsOnLoad()
        {
            Canvas.preWillRenderCanvases -= OnCanvasPreRendering;
            Canvas.willRenderCanvases -= OnCanvasRendering;
            isInitialized = false;
            processingCount = 0;
        }
#endif

        /// <summary>Registers completion after uGUI's rebuild callback so late consumers observe committed geometry.</summary>
        private static void EnsureInitialized()
        {
            if (isInitialized) return;
#if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
#endif

            _ = CanvasUpdateRegistry.instance;
            EmojiFont.EnsureInitialized();
            Canvas.preWillRenderCanvases += OnCanvasPreRendering;
            Canvas.willRenderCanvases += OnCanvasRendering;
            componentsBuffer.EnsureCapacity(64);
            isInitialized = true;

            CatZones.lifecycle.Meow("[UniText] Initialized");
        }

        private static void RegisterDirty(UniTextBase component)
        {
            EnsureInitialized();

            if (component.isRegisteredDirty || component.isProcessing)
                return;

            if (!component.isActiveAndEnabled)
                return;

            component.isRegisteredDirty = true;
            componentsBuffer.Add(component);
        }

        private static void UnregisterDirty(UniTextBase component)
        {
            component.isRegisteredDirty = false;
            if (component.isProcessing) return;

            for (var i = componentsBuffer.count - 1; i >= 0; i--)
                if (ReferenceEquals(componentsBuffer[i], component))
                    componentsBuffer.RemoveAt(i);
        }

        private static bool CanWork
        {
            get
            {
#if UNITY_EDITOR
                if (EditorLifecycle.IsReloading) return false;
#endif
                UnicodeData.EnsureInitialized();
                return true;
            }
        }

        private static int FilterAndPrepareComponents(bool validate, int count)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                var comp = componentsBuffer[i];
                if (comp == null || !comp.isActiveAndEnabled || !comp.isRegisteredDirty
                    || (comp.sourceText.IsEmpty && !comp.IsDocumentHost))
                {
                    if (comp != null)
                    {
                        FinalizeProcessingFailure(comp, false);
                        comp.isRegisteredDirty = false;
                        comp.isProcessing = false;
                        comp.deferredDirtyFlags = UniTextDirty.None;
                    }
                    componentsBuffer.RemoveAt(i);
                    count--;
                    continue;
                }

                if (!comp.isProcessing)
                    comp.BeginProcessing();

                if (!validate || comp.processingException != null) continue;

#if UNITEXT_PROFILE
                var sampleDepth = UniTextDebug.SampleDepth;
#endif
                try
                {
                    if (comp.ValidateAndInitialize()) continue;
                }
                catch (Exception exception)
                {
#if UNITEXT_PROFILE
                    UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                    comp.FailProcessing(exception);
                    continue;
                }

                comp.isRegisteredDirty = false;
                comp.isProcessing = false;
                comp.deferredDirtyFlags = UniTextDirty.None;
                componentsBuffer.RemoveAt(i);
                count--;
            }

            for (var i = 0; i < count; i++)
                ExecuteComponent(componentsBuffer[i], static component => component.PrepareForParallel());
            return count;
        }

        private static void OnCanvasPreRendering()
        {
#if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return;
            if (EditorLifecycle.IsReloading) return;
#endif
            prepassSucceeded = false;
            GlyphAtlas.ForEachInstance(static a => a.BeginFrame());
            InvokeProcessingEvent(ProcessingStarted);
            if (componentsBuffer.count == 0) return;
            if (!CanWork) return;

            UniTextDebug.BeginSample("PreWillRender");

            processingCount = FilterAndPrepareComponents(true, componentsBuffer.count);
            var count = processingCount;

            useParallel = UseParallel && WorkerPool.IsParallelSupported;

            LogBatchInfo(count, useParallel);

            UniTextDebug.BeginSample("FirstPass");
            if (useParallel)
            {
                ExecuteComponents(count, static comp => comp.DoFirstPassBeginA());
                DispatchAnalysisJobs(count);
                ExecuteComponents(count, static comp => comp.DoFirstPassBeginB());
                DispatchShapeJobs(count);
                ExecuteComponents(count, static comp => comp.DoFirstPassFinish());
            }
            else
            {
                for (var i = 0; i < count; i++)
                    ExecuteComponent(componentsBuffer[i], static comp => comp.DoFirstPass());
            }
            UniTextDebug.EndSample();

            UniTextDebug.EndSample();

            CatZones.frameFlow.Meow("[UniText] CanvasPreRendering completed");
            prepassSucceeded = true;
        }

        private static void OnCanvasRendering()
        {
            if (PostProcess())
                InvokeProcessingEvent(ProcessingEnded);
        }

        private static bool PostProcess()
        {
#if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer) return true;
            if (EditorLifecycle.IsReloading) return true;
#endif
            if (componentsBuffer.count == 0)
            {
                prepassSucceeded = false;
                processingCount = 0;
                GlyphAtlas.CommitMeshChanges();
                return true;
            }
            if (!prepassSucceeded || !CanWork)
            {
                prepassSucceeded = false;
                return false;
            }
            prepassSucceeded = false;

            UniTextDebug.BeginSample("WillRender");

            UniTextDebug.BeginSample("FilterPrepare");
            processingCount = FilterAndPrepareComponents(false, processingCount);
            UniTextDebug.EndSample();

            var count = processingCount;

            UniTextDebug.BeginSample("ComputeLayout");
            for (var i = 0; i < count; i++)
                ExecuteComponent(componentsBuffer[i], static comp => comp.EnsureLayoutFit());
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("Rasterize");
            RasterizeGlyphBatches(count);
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("GenerateMeshData");
            ExecuteComponents(count, static comp => comp.DoGenerateMeshData());
            UniTextDebug.EndSample();

            bool anyUpgrades = false;
            for (var i = 0; i < count; i++)
            {
                var component = componentsBuffer[i];
                if (component.processingException != null) continue;
                var g = component.meshGenerator;
                if (g != null && (g.tierUpgradeRequests.Count > 0 || g.tileSizeUpgradeRequests.Count > 0))
                { anyUpgrades = true; break; }
            }
            UniTextDebug.BeginSample("AtlasUpgrades+Flush");
            if (anyUpgrades)
            {
                ProcessTileSizeUpgrades(count);
                ProcessTierUpgrades(count);
            }
            bool atlasCommitSucceeded = GlyphAtlas.FlushPendingInstances();
            if (!atlasCommitSucceeded)
            {
                UniTextDebug.EndSample();
                UniTextDebug.EndSample();
                CatZones.glyphAtlas.Meow(
                    "[UniText] Atlas delivery is inert or recovering; renderer meshes were withheld whole and the batch retries next frame");
#if UNITY_EDITOR
                CoreLoop.RequestEditorFrame();
#endif
                return false;
            }
            UniTextDebug.EndSample();

            GradientRampAtlas.Instance.Flush();
            ColorMatrixAtlas.Instance.Flush();

            UniTextDebug.BeginSample("ApplyMeshes");
            for (var i = 0; i < count; i++)
                ExecuteComponent(componentsBuffer[i], static comp => comp.DoApplyMesh());
            GlyphAtlas.CommitMeshChanges();
            for (var i = 0; i < count; i++)
                componentsBuffer[i].CompleteCommit();
            UniTextDebug.EndSample();

            CompleteProcessing();

            UniTextDebug.EndSample();

            CatZones.frameFlow.Meow("[UniText] CanvasRendering completed");
            return true;
        }

        private static void CompleteProcessing()
        {
            var count = componentsBuffer.count;
            var pendingCount = 0;
            for (var i = 0; i < processingCount; i++)
            {
                var component = componentsBuffer[i];
                if (FinalizeProcessingFailure(component, true)) continue;
                component.isProcessing = false;
                component.isRegisteredDirty = false;
                if (component.frameTickRefreshDeferred)
                {
                    component.frameTickRefreshDeferred = false;
                    component.RefreshFrameTick();
                }
                var deferred = component.deferredDirtyFlags;
                component.deferredDirtyFlags = UniTextDirty.None;
                var deferredChanges = component.deferredCommitChanges;
                component.deferredCommitChanges = UniTextCommitChanges.None;
                if (deferred == UniTextDirty.None || !component.isActiveAndEnabled) continue;
                if ((deferred & UniTextDirty.Font) != 0) component.ReleaseFontProvider();
                component.dirtyFlags |= deferred;
                component.pendingCommitChanges |= deferredChanges;
                component.isRegisteredDirty = true;
                componentsBuffer[pendingCount++] = component;
            }
            for (var i = processingCount; i < count; i++)
                componentsBuffer[pendingCount++] = componentsBuffer[i];
            componentsBuffer.count = pendingCount;
            processingCount = 0;
        }

        private static bool FinalizeProcessingFailure(UniTextBase component, bool teardown)
        {
            var exception = component.processingException;
            if (exception == null) return false;

            UnityEngine.Debug.LogException(exception, component);
#if UNITEXT_PROFILE
            var sampleDepth = UniTextDebug.SampleDepth;
#endif
            try
            {
                component.firstPassPending = false;
                component.postCommitActions?.Clear();
                component.textProcessor?.AbortFirstPass();
                component.meshGenerator?.ReturnInstanceBuffers();
                if (teardown)
                    component.DeInit();
            }
            catch (Exception cleanupException)
            {
#if UNITEXT_PROFILE
                UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                UnityEngine.Debug.LogException(cleanupException, component);
            }

            component.processingException = null;
            component.isProcessing = false;
            component.isRegisteredDirty = false;
            component.deferredDirtyFlags = UniTextDirty.None;
            return true;
        }

        private static void RasterizeGlyphBatches(int count)
        {
            UniTextDebug.BeginSample("Rasterization");
            try
            {
                CollectGlyphRequestsFromAllComponents(componentsBuffer, count);
                GlyphBatchRasterizer.Run();
                MarkCollectedGlyphRequests(componentsBuffer, count);
            }
            finally
            {
                UniTextDebug.EndSample();
            }
        }

        
        private static Dictionary<(long, UniTextRenderMode), UniTextMeshGenerator.TierUpgradeRequest> upgradeMap;

        private static void ProcessTierUpgrades(int count)
        {
            upgradeMap ??= new Dictionary<(long, UniTextRenderMode), UniTextMeshGenerator.TierUpgradeRequest>();
            upgradeMap.Clear();

            for (int i = 0; i < count; i++)
            {
                var component = componentsBuffer[i];
                if (component.processingException != null) continue;
                var gen = component.meshGenerator;
                if (gen == null) continue;
                var requests = gen.tierUpgradeRequests;
                for (int j = 0; j < requests.Count; j++)
                {
                    var req = requests[j];
                    var mapKey = (req.glyphKey, req.mode);
                    if (!upgradeMap.TryGetValue(mapKey, out var existing)
                        || req.requiredTier > existing.requiredTier)
                        upgradeMap[mapKey] = req;
                }
            }

            int reExtracted = 0;
            foreach (var kvp in upgradeMap)
            {
                var req = kvp.Value;
                if (GlyphAtlas.GetInstance(req.mode).TryUpgradePendingTier(req.glyphKey, req.requiredTier))
                    continue;
                req.font.ReExtractForTierUpgrade(
                    req.glyphIndex, req.varHash48, req.ftCoords,
                    req.mode, req.requiredTier);
                reExtracted++;
            }

            CatZones.raster.MeowFormat("[UniText] TierUpgrades: {0} unique glyphs ({1} re-extracted)",
                upgradeMap.Count, reExtracted);
        }

        private static Dictionary<(long, UniTextRenderMode), UniTextMeshGenerator.TileSizeUpgradeRequest> sizeUpgradeMap;

        /// <summary>Grow-only atlas tile-size upgrades: per glyph, the max requested boost picks the target class; a glyph that actually grows is relocated by <see cref="GlyphAtlas.UpgradeGlyphTileSize"/>, which publishes through its transform-table row — meshes are untouched. Runs before <see cref="ProcessTierUpgrades"/> so a same-frame tier bump lands in the relocated tile.</summary>
        private static void ProcessTileSizeUpgrades(int count)
        {
            sizeUpgradeMap ??= new Dictionary<(long, UniTextRenderMode), UniTextMeshGenerator.TileSizeUpgradeRequest>();
            sizeUpgradeMap.Clear();

            for (int i = 0; i < count; i++)
            {
                var component = componentsBuffer[i];
                if (component.processingException != null) continue;
                var gen = component.meshGenerator;
                if (gen == null) continue;
                var requests = gen.tileSizeUpgradeRequests;
                for (int j = 0; j < requests.Count; j++)
                {
                    var req = requests[j];
                    var mapKey = (req.glyphKey, req.mode);
                    if (!sizeUpgradeMap.TryGetValue(mapKey, out var existing) || req.tileSizeBoost > existing.tileSizeBoost)
                        sizeUpgradeMap[mapKey] = req;
                }
            }

            int promoted = 0;
            foreach (var kvp in sizeUpgradeMap)
            {
                var req = kvp.Value;
                if (req.font.ReExtractForTileSizeUpgrade(
                        req.glyphIndex, req.varHash48, req.ftCoords, req.mode, req.tileSizeBoost))
                    promoted++;
            }

            if (promoted > 0)
                CatZones.raster.MeowFormat(
                    "[UniText] TileSizeUpgrades: {0} glyph(s) promoted to a larger atlas tile (grow-only, shared)", promoted);
        }

        #endregion

        #region Instance Batch Methods

        /// <summary>
        /// Ensures the first pass (parsing, BiDi, shaping) has run.
        /// No-op in the normal pipeline (Phase 1 already ran).
        /// Enables <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c> to work outside the pipeline.
        /// </summary>
        protected void EnsureFirstPassComplete()
        {
            if (textProcessor != null && textProcessor.HasValidFirstPassData) return;
            if (!CanWork) return;
            if (!ValidateAndInitialize()) return;
            PrepareForParallel();
            DoFirstPass();
        }

        private void DoFirstPass()
        {
            textProcessor.EnsureFirstPass(ParseOrGetParsedAttributes(), FirstPassSettings());
        }

        private TextProcessSettings FirstPassSettings() => new()
        {
            fontSize = autoSize ? maxFontSize : fontSize,
            baseDirection = TextDirection.Auto
        };

        [NonSerialized] private bool firstPassPending;

        /// <summary>Parallel-path half A: parse + analysis prepare, leaving analysis jobs queued for <see cref="DispatchAnalysisJobs"/>.</summary>
        private void DoFirstPassBeginA()
        {
            firstPassPending = false;
            firstPassPending = textProcessor.EnsureFirstPassBeginA(ParseOrGetParsedAttributes(), FirstPassSettings());
        }

        /// <summary>Parallel-path half B: analysis finish + font faces + shape prepare, run after this component's analysis jobs completed. Leaves shape jobs queued.</summary>
        private void DoFirstPassBeginB()
        {
            if (!firstPassPending) return;
            textProcessor.EnsureFirstPassBeginB();
        }

        private int PendingAnalysisJobs => firstPassPending ? textProcessor.PendingAnalysisJobCount : 0;

        private void RunComponentAnalysisJob(int missIdx) => textProcessor.RunAnalysisJob(missIdx);

        private int PendingShapeJobs => firstPassPending ? textProcessor.PendingShapeJobCount : 0;

        private void RunComponentShapeJob(int missIdx) => textProcessor.RunShapeJob(missIdx);

        private void DoFirstPassFinish()
        {
            if (!firstPassPending) return;
            firstPassPending = false;
            textProcessor.EnsureFirstPassFinish();
        }

        private static PooledBuffer<UniTextBase> jobComponents;
        private static PooledBuffer<int> jobIndices;
        private static Action<UniTextBase, int> componentJobAction;

        private static readonly Action<int> componentJobAdapter = static i =>
        {
            var component = jobComponents[i];
            var action = componentJobAction;
            if (component == null || action == null || Volatile.Read(ref component.processingException) != null)
                return;
#if UNITEXT_PROFILE
            var sampleDepth = UniTextDebug.SampleDepth;
#endif
            try
            {
                action(component, jobIndices[i]);
            }
            catch (Exception exception)
            {
#if UNITEXT_PROFILE
                UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                component.FailProcessing(exception);
            }
        };

        /// <summary>
        /// Flattens every dirty component's queued ANALYSIS misses into one (component, job) list and runs it
        /// across the pool — a big multi-paragraph document spreads its bidi/script/break/grapheme work over all
        /// workers instead of one slab. Mirrors <see cref="DispatchShapeJobs"/>; jobs write only their own slot,
        /// the single-threaded BeginB splices + caches. No-op when nothing is queued.
        /// </summary>
        private static void DispatchAnalysisJobs(int count)
        {
            DispatchComponentJobs(count, static component => component.PendingAnalysisJobs,
                static (component, job) => component.RunComponentAnalysisJob(job));
        }

        /// <summary>
        /// Flattens every dirty component's queued shape misses into one (component, job) list and runs
        /// it across the pool — a big multi-paragraph text spreads its HarfBuzz work over all workers
        /// instead of stalling one slab. Barriers around this call come from the Begin/Finish phases.
        /// </summary>
        private static void DispatchShapeJobs(int count)
        {
            DispatchComponentJobs(count, static component => component.PendingShapeJobs,
                static (component, job) => component.RunComponentShapeJob(job));
        }

        private static void DispatchComponentJobs(int count, Func<UniTextBase, int> getJobCount,
            Action<UniTextBase, int> action)
        {
            jobComponents.FakeClear();
            jobIndices.FakeClear();
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var component = componentsBuffer[i];
                    if (component.processingException != null) continue;
                    var jobs = getJobCount(component);
                    for (var j = 0; j < jobs; j++)
                    {
                        jobComponents.Add(component);
                        jobIndices.Add(j);
                    }
                }

                if (jobIndices.count == 0) return;

                componentJobAction = action;
                WorkerPool.Execute(jobIndices.count, componentJobAdapter);
            }
            finally
            {
                componentJobAction = null;
                jobComponents.Clear();
                jobIndices.FakeClear();
            }
        }

        private void DoGenerateMeshData()
        {
            CatZones.frameFlow.MeowFormat("[UniText] DoGenerateMeshData entry: name={0}, tpHash={1}, hasFirstPass={2}, hasPositions={3}",
                cachedTransformData.name, textProcessor?.GetHashCode() ?? 0,
                textProcessor?.HasValidFirstPassData ?? false,
                textProcessor?.HasValidPositionedGlyphs ?? false);
            if (meshGenerator == null) return;
            meshGenerator.captureGlyphGeometry =
                (processingCommitChanges & UniTextCommitChanges.GlyphGeometry) != 0;
            if (textProcessor == null || !textProcessor.HasValidFirstPassData)
            {
                meshGenerator.GenerateMeshDataOnly(default, default, default);
                return;
            }

            Rebuilding?.Invoke();

            ref readonly var cached = ref cachedTransformData;

            var effectiveFontSize = autoSize
                ? (cachedEffectiveFontSize > 0 ? cachedEffectiveFontSize : maxFontSize)
                : fontSize;

            if (attributeParser?.HasPendingReapply == true)
            {
                UniTextDebug.BeginSample("ReApplyModifiers");
                attributeParser.ReApplyPending();
                UniTextDebug.EndSample();
            }

            var positionsInvalid = !textProcessor.HasValidPositionedGlyphs;

            if (positionsInvalid)
            {
                UniTextDebug.BeginSample("EnsurePositions");
                textProcessor.EnsureLines(cached.rect.width, effectiveFontSize, wordWrap);
                var settings = CreateProcessSettings(cached.rect, effectiveFontSize);
                textProcessor.EnsurePositions(settings);
                UniTextDebug.EndSample();
            }

            var glyphs = textProcessor.PositionedGlyphs;
            CatZones.frameFlow.MeowFormat("[UniText] After EnsurePositions: name={0}, tpHash={1}, glyphCount={2}, hasValid={3}",
                cachedTransformData.name, textProcessor.GetHashCode(), glyphs.Length, textProcessor.HasValidPositionedGlyphs);
            buffers.virtualPositionedGlyphs.FakeClear();
            beforeGenerateMesh?.Invoke();

            meshGenerator.debugName = cached.name;
            meshGenerator.FontSize = effectiveFontSize;
            meshGenerator.RenderMode = RenderMode;
            meshGenerator.defaultColor = color;
            meshGenerator.SetRectOffset(cached.rect);
            meshGenerator.SetVisibleBand(cached.visibleYMin, cached.visibleYMax);
            RecordEmittedBand(cached.visibleYMin, cached.visibleYMax);

            var virtualGlyphs = buffers.virtualPositionedGlyphs.data != null
                ? buffers.virtualPositionedGlyphs.Span
                : default;

            UniTextDebug.BeginSample("GenMeshOnly");
            meshGenerator.GenerateMeshDataOnly(glyphs, virtualGlyphs, buffers.paragraphs.Span);
            UniTextDebug.EndSample();
        }

        private void DoApplyMesh()
        {
            if (meshGenerator == null || !meshGenerator.HasGeneratedData)
            {
                DeInit();
                dirtyFlags = UniTextDirty.None;
                return;
            }

            UniTextMaterialCache.EnsureAtlasSubscription();

            if (meshGenerator.missingAtlasGlyphs && textProcessor != null)
            {
                meshGenerator.missingAtlasGlyphs = false;
                textProcessor.HasValidGlyphsInAtlas = false;
                SetAppearanceDirty();
                CatZones.glyphAtlas.MeowFormat("[UniText] '{0}': missing atlas glyphs after mesh gen -> defer mesh refresh", cachedTransformData.name);
            }

            UniTextDebug.BeginSample("ApplyToUnity");
            renderData = meshGenerator.CollectRenderData();
            UniTextDebug.EndSample();

#if UNITEXT_TESTS
            CopyMeshesForTests();
#endif

            if (textProcessor != null)
            {
                resultWidth = textProcessor.ResultWidth;
                resultHeight = textProcessor.ResultHeight;
            }

            UniTextDebug.BeginSample("SetMesh");
            UpdateRendering();
            UniTextDebug.EndSample();
            UpdateGlyphAtlasRefCounts();

            meshGenerator.ReturnInstanceBuffers();

            dirtyFlags = UniTextDirty.None;
        }

        private void CompleteCommit()
        {
            if (processingException != null) return;
            FlushPostCommitActions();
            CommitFinalizing?.Invoke(processingCommitChanges);
#if UNITEXT_PROFILE
            var sampleDepth = UniTextDebug.SampleDepth;
#endif
            try
            {
                Committed?.Invoke(processingCommitChanges);
                LayoutCommitted?.Invoke();
            }
            catch (Exception exception)
            {
#if UNITEXT_PROFILE
                UniTextDebug.RestoreSampleDepth(sampleDepth);
#endif
                UnityEngine.Debug.LogException(exception, this);
            }
        }

        #endregion

        #region Debug

        [Conditional("UNITEXT_DEBUG")]
        private static void LogBatchInfo(int componentCount, bool parallel)
        {
            var totalChars = 0;
            for (var i = 0; i < componentCount; i++)
                totalChars += componentsBuffer[i].sourceText.Length;
            CatZones.frameFlow.MeowFormat("[UniText] Batch: {0} components, {1} chars, parallel={2}", componentCount, totalChars, parallel);
        }

        #endregion
    }
}
