#if UNITY_2023_3_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleBlurPass
{
    class BlurPassData
    {
        public PooledListDictionary<(BlurSettings blurSettings, float alpha), List<IBlur>, IBlur> batchedBlurs;
        public TextureHandle source;
        public TextureHandle temporaryUIBlurFullScreenHandle;
        public List<TextureHandle> instanceHandles = new();
        public List<int> layerMipLevels = new();
        public List<TextureHandle> requestSources = new();
        public List<TextureHandle> requestDestinations = new();
        public List<TextureHandle> textureHandles = new();
        public List<(int width, int height)> textureDimensions = new();
        public Queue<int> textureIndices = new();
        public List<IBlur> blurAreas, blurredImageAreas;
        public List<FlexibleBlurTextureRequest> textureRequests;
        public Camera camera;
        public int featureIdx, maxStabilizationPixels;
        public float renderScale, originalWidth, originalHeight;
        public bool singlePassVR, multiPassVR, rightEye, haveUIBlurAreas, haveBlurredImageAreas, blurredImageLayersSeeLower, blurredImageSeeUIBlurs, uiBlurLayersSeeLower, useComputeShaders, overlayCompatibilityFix;
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (!blurLayerAdddedThisFrame && (IndividuallyPaused || FlexibleBlurFeature.GloballyPaused))
            return;

        blurLayerAdddedThisFrame = false;
        var cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.isPreviewCamera || cameraData.isSceneViewCamera)
            return;

        var camera = cameraData.camera;
        var key = (camera, featureIdx);

        Setup(cameraData.camera, cameraData.cameraTargetDescriptor, cameraData.renderScale);

        UIBlur.BlurDict.TryGetValue(key, out var blurAreas);
        FlexibleBlurFeature.ImageBasedBlurDict.TryGetValue(key, out var blurredImageAreas);
        TryGetTextureRequests(camera, featureIdx, out var textureRequests);
        var haveUIBlurAreas = blurAreas is { Count: > 0 };
        var haveBlurredImageAreas = blurredImageAreas is { Count: > 0 };
        var haveTextureRequests = textureRequests is { Count: > 0 };
        if (!haveUIBlurAreas && !haveBlurredImageAreas && !haveTextureRequests)
            return;

        var textureDescriptor = cameraData.cameraTargetDescriptor;
        textureDescriptor.enableRandomWrite = useComputeShaders;
        textureDescriptor.msaaSamples = 1;
        textureDescriptor.depthStencilFormat = GraphicsFormat.None;
        textureDescriptor.graphicsFormat = blurGraphicsFormat;

        var (originalWidth, originalHeight) = (textureDescriptor.width, textureDescriptor.height);
        var renderScale = cameraData.renderScale;

#if XR_MANAGEMENT_INSTALLED
        var singlePassVR = XRSettings.enabled && XRSettings.stereoRenderingMode >= XRSettings.StereoRenderingMode.SinglePassInstanced;
        var rightEye = XRSettings.enabled && cameraData.xr.multipassId == 1;
#else
        var (rightEye, singlePassVR) = (false, false);
#endif

        using var builder = renderGraph.AddUnsafePass<BlurPassData>(ProfilerTag, out var passData);
        builder.AllowGlobalStateModification(true);
        builder.AllowPassCulling(false);

        (passData.camera, passData.featureIdx, passData.renderScale, passData.originalWidth, passData.originalHeight, passData.singlePassVR, passData.haveUIBlurAreas, passData.haveBlurredImageAreas, passData.blurAreas, passData.blurredImageAreas, passData.textureRequests, passData.batchedBlurs, passData.blurredImageSeeUIBlurs, passData.blurredImageLayersSeeLower, passData.uiBlurLayersSeeLower, passData.useComputeShaders, passData.overlayCompatibilityFix, passData.maxStabilizationPixels) =
        (         camera,          featureIdx,          renderScale,          originalWidth,          originalHeight,          singlePassVR,          haveUIBlurAreas,          haveBlurredImageAreas,          blurAreas,          blurredImageAreas,          textureRequests,          batchedBlurs,          blurredImageSeeUIBlurs,          blurredImageLayersSeeLower,          uiBlurLayersSeeLower,          useComputeShaders,          overlayCompatibilityFix,          maxStabilizationPixels);

#if XR_MANAGEMENT_INSTALLED
        passData.multiPassVR = XRSettings.enabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass;
#else
        passData.multiPassVR = false;
#endif
        passData.textureIndices.Clear();
        passData.textureHandles.Clear();
        passData.textureDimensions.Clear();
        passData.instanceHandles.Clear();
        passData.layerMipLevels.Clear();
        passData.requestSources.Clear();
        passData.requestDestinations.Clear();
        passData.source = frameData.Get<UniversalResourceData>().activeColorTexture;
        builder.UseTexture(passData.source, AccessFlags.ReadWrite);

        if (currentRTHandleList != null)
        foreach (var rtHandle in currentRTHandleList)
        {
            var texture = renderGraph.ImportTexture(rtHandle);
            passData.instanceHandles.Add(texture);
            builder.UseTexture(texture, AccessFlags.ReadWrite);
        }
        passData.layerMipLevels.AddRange(currentLayerMipLevels);

        if (blurredImageSeeUIBlurs)
        {
            HandleUIBlurs();
            HandleBlurredImages();
        }
        else
        {
            HandleBlurredImages();
            HandleUIBlurs(haveBlurredImageAreas);
        }
        HandleTextureRequests();

        builder.SetRenderFunc(static(BlurPassData data, UnsafeGraphContext context) => Pass(data, context));

        void HandleUIBlurs(bool useTempRT = false)
        {
            if (!haveUIBlurAreas)
                return;

            if (useTempRT)
            {
                textureDescriptor.width = originalWidth;
                textureDescriptor.height = originalHeight;
                passData.temporaryUIBlurFullScreenHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, textureDescriptor, nameof(BlurPassData.temporaryUIBlurFullScreenHandle), false, FilterMode.Bilinear);
                builder.UseTexture(passData.temporaryUIBlurFullScreenHandle, AccessFlags.ReadWrite);
            }

            foreach (var blurArea in blurAreas)
                AddTemporaryTextures(blurArea.Settings, rightEye ? blurArea.Common.BlurRegionRight : blurArea.Common.BlurRegion, blurArea.Alpha, 0);
        }

        void HandleBlurredImages()
        {
            if (!haveBlurredImageAreas)
                return;

            int currentLayer = blurredImageAreas[0].Layer;
            int currentPriority = blurredImageAreas[0].Priority;
            foreach (var blurImage in blurredImageAreas)
            {
                if (blurImage.Layer > currentLayer || blurImage.Priority > currentPriority)
                {
                    TryApplyBatchedBlurs();
                    currentLayer = blurImage.Layer;
                    currentPriority = blurImage.Priority;
                }

                if (blurImage.CanBatch)
                    batchedBlurs.Add((blurImage.Settings, blurImage.Alpha), blurImage);
                else
                    AddTemporaryTextures(blurImage.Settings, rightEye ? blurImage.Common.BlurRegionRight : blurImage.Common.BlurRegion, blurImage.Alpha, maxStabilizationPixels);
            }

            TryApplyBatchedBlurs();

            void TryApplyBatchedBlurs()
            {
                foreach (var kvp in batchedBlurs)
                {
                    var fillEntireRenderTexture = false;
                    foreach (var blur in kvp.Value)
                    {
                        if (!blur.FillEntireRenderTexture)
                            continue;

                        fillEntireRenderTexture = true;
                        break;
                    }

                    if (!fillEntireRenderTexture && kvp.Value.Count == 1)
                    {
                        var blurImage = kvp.Value[0];
                        AddTemporaryTextures(blurImage.Settings, rightEye ? blurImage.Common.BlurRegionRight : blurImage.Common.BlurRegion, blurImage.Alpha, maxStabilizationPixels);
                        continue;
                    }

                    float minX, minY, maxX, maxY;
                    if (fillEntireRenderTexture)
                    {
                        (minX, minY, maxX, maxY) = (0, 0, originalWidth, originalHeight);
                    }
                    else
                    {
                        (minX, minY, maxX, maxY) = (float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);
                        foreach (var batchedBlur in kvp.Value)
                        {
                            minX = Math.Min(minX, batchedBlur.MinX(rightEye));
                            minY = Math.Min(minY, batchedBlur.MinY(rightEye));
                            maxX = Math.Max(maxX, batchedBlur.MaxX(rightEye));
                            maxY = Math.Max(maxY, batchedBlur.MaxY(rightEye));

                            if (!singlePassVR)
                                continue;

                            minX = Math.Min(minX, batchedBlur.MinX(true));
                            minY = Math.Min(minY, batchedBlur.MinY(true));
                            maxX = Math.Max(maxX, batchedBlur.MaxX(true));
                            maxY = Math.Max(maxY, batchedBlur.MaxY(true));
                        }
                    }

                    var blurRegion = UIBlurCommon.ComputeBlurRegion(minX, minY, maxX, maxY);
                    var settings = kvp.Key.blurSettings;
                    AddTemporaryTextures(settings, blurRegion, kvp.Key.alpha, maxStabilizationPixels);
                }

                batchedBlurs.Clear();
            }
        }

        void HandleTextureRequests()
        {
            if (!haveTextureRequests)
                return;

            foreach (var request in textureRequests)
            {
                var source = request.Source != null ? renderGraph.ImportTexture(request.Source) : passData.source;
                var destination = renderGraph.ImportTexture(request.Destination);
                passData.requestSources.Add(source);
                passData.requestDestinations.Add(destination);
                if (request.Source != null)
                    builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(destination, AccessFlags.ReadWrite);
                var bounds = request.Bounds;
                AddTemporaryTextures(request.Settings, new Vector4(bounds.x, bounds.y, bounds.width, bounds.height), request.Strength, maxStabilizationPixels);
            }
        }

        void AddTemporaryTextures(BlurSettings settings, Vector4 blurRegion, float alpha, int maxStabilizationPixels)
        {
            blurRegion = PrepareBlurRegion(blurRegion, settings, alpha, renderScale, originalWidth, originalHeight, maxStabilizationPixels);
            var handles = passData.textureHandles;
            var dimensions = passData.textureDimensions;
            var indices = passData.textureIndices;
            var currentTextureIdx = 0;

            var aspect = blurRegion.z / blurRegion.w;
            var stabilizeImageRegion = maxStabilizationPixels > 0;
            var initialDimensions = GetInitialBlurDimensions(blurRegion.z, blurRegion.w, settings, renderScale, originalWidth, originalHeight, stabilizeImageRegion);
            textureDescriptor.width = initialDimensions.x;
            textureDescriptor.height = initialDimensions.y;
            var referenceWidthForDownScale = textureDescriptor.width;
            var referenceHeightForDownScale = textureDescriptor.height;

            FindExistingHandleOrAddNew();

            bool temp2NeedsInit = true;
            if (alpha <= 0)
                return;

            int totalIterations = 0;
            foreach (var section in settings.downscaleSections)
            {
                var iterations = section.iterations;
                var (isSeparable, _, _, _, _) = section.GetSectionBehaviour();

                for (int i = 0; i < iterations; i++, totalIterations++)
                {
                    var textureDimensions = GetDownscaledBlurDimensions(referenceWidthForDownScale, referenceHeightForDownScale, aspect, alpha, totalIterations, stabilizeImageRegion);
                    textureDescriptor.width = textureDimensions.x;
                    textureDescriptor.height = textureDimensions.y;
                    FindExistingHandleOrAddNew();

                    if (!isSeparable)
                    {
                        temp2NeedsInit = true;
                        continue;
                    }

                    temp2NeedsInit = false;
                    FindExistingHandleOrAddNew();
                }
            }

            if (temp2NeedsInit)
                FindExistingHandleOrAddNew();

            void FindExistingHandleOrAddNew()
            {
                const string textureName = "blurTexture";

                while (true)
                {
                    if (currentTextureIdx >= handles.Count)
                    {
                        var newHandle = UniversalRenderer.CreateRenderGraphTexture(renderGraph, textureDescriptor, textureName, false, FilterMode.Bilinear);
                        dimensions.Add((textureDescriptor.width, textureDescriptor.height));
                        handles.Add(newHandle);
                        builder.UseTexture(newHandle, AccessFlags.ReadWrite);
                        indices.Enqueue(currentTextureIdx++);
                        return;
                    }

                    var existingDimensions = dimensions[currentTextureIdx];
                    if (existingDimensions.width == textureDescriptor.width && existingDimensions.height == textureDescriptor.height)
                    {
                        indices.Enqueue(currentTextureIdx++);
                        return;
                    }

                    currentTextureIdx++;
                }
            }
        }
    }

    private static void Pass(BlurPassData data, UnsafeGraphContext context)
    {
        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);

        var (textureHandle, textureDimensions, textureIndices, camera, featureIdx, renderScale, originalWidth, originalHeight, singlePassVR, multiPassVR, rightEye, haveUIBlurAreas, haveBlurredImageAreas, blurAreas, blurredImageAreas, batchedBlurs, blurredImageLayersSeeLower, blurredImageSeeUIBlurs, uiBlurLayersSeeLower, useComputeShaders, overlayCompatibilityFix, maxStabilizationPixels) =
            (data.textureHandles, data.textureDimensions, data.textureIndices, data.camera, data.featureIdx, data.renderScale, data.originalWidth, data.originalHeight, data.singlePassVR, data.multiPassVR, data.rightEye, data.haveUIBlurAreas, data.haveBlurredImageAreas, data.blurAreas, data.blurredImageAreas,data.batchedBlurs, data.blurredImageLayersSeeLower, data.blurredImageSeeUIBlurs, data.uiBlurLayersSeeLower, data.useComputeShaders, data.overlayCompatibilityFix, data.maxStabilizationPixels);

        var destinationRequiresClear = haveUIBlurAreas && (!haveBlurredImageAreas || blurredImageSeeUIBlurs);
        var key = (camera, featureIdx);

        if (blurredImageSeeUIBlurs)
        {
            HandleUIBlurs();
            HandleBlurredImages();
        }
        else
        {
            HandleBlurredImages();
            HandleUIBlurs(haveBlurredImageAreas);
        }
        HandleTextureRequests();

        void HandleUIBlurs(bool useTempRT = false)
        {
            if (!haveUIBlurAreas)
                return;

            TextureHandle layerHandle;
            if (useTempRT)
            {
                destinationRequiresClear = !blurredImageSeeUIBlurs;
                layerHandle = data.temporaryUIBlurFullScreenHandle;
            }
            else
            {
                layerHandle = data.instanceHandles[0];
            }

            if (uiBlurLayersSeeLower)
            {
                int currentLayer = blurAreas[0].Layer;
                foreach (var blur in blurAreas)
                {
                    if (blur.Layer > currentLayer)
                    {
                        currentLayer = blur.Layer;
                        FullScreenBlit(cmd, layerHandle, data.source, fullScreenBlitsMat, 1);
                    }

                    ApplyBlurRenderGraph(blur, data.source, layerHandle, 0);
                }
            }
            else
            {
                foreach (var x in blurAreas)
                    ApplyBlurRenderGraph(x, data.source, layerHandle, 0);
            }

            if (!destinationRequiresClear)
                FullScreenBlit(cmd, layerHandle, data.source, fullScreenBlitsMat, 1);
        }

        void HandleBlurredImages()
        {
            if (!haveBlurredImageAreas)
                return;

            var numImageLayers = FlexibleBlurFeature.ImageBasedLayersPerCameraDict[key];
            var source = data.source;
            var destinationIdx = 0;
            var destination = data.instanceHandles[destinationIdx];
            if (blurredImageLayersSeeLower && destinationIdx < numImageLayers - 1)
            {
                cmd.SetRenderTarget(destination);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetRenderTarget(source);
                FullScreenBlit(cmd, source, destination, fullScreenBlitsMat);
            }

            var currentLayer = blurredImageAreas[0].Layer;
            var currentPriority = blurredImageAreas[0].Priority;
            foreach (var blurImage in blurredImageAreas)
            {
                if (blurImage.Layer > currentLayer || blurImage.Priority > currentPriority)
                {
                    TryApplyBatchedBlurs();

                    if (blurImage.Layer > currentLayer)
                    {
                        if (destinationIdx < data.layerMipLevels.Count && data.layerMipLevels[destinationIdx] > 0)
                            cmd.GenerateMips(destination);
                        destination = data.instanceHandles[++destinationIdx];
                        if (blurredImageLayersSeeLower)
                        {
                            source = data.instanceHandles[destinationIdx - 1];
                            cmd.SetRenderTarget(source);

                            if (destinationIdx < numImageLayers - 1)
                            {
                                FullScreenBlit(cmd, source, destination, fullScreenBlitsMat);
                            }
                        }
                    }
                    currentLayer = blurImage.Layer;
                    currentPriority = blurImage.Priority;
                }

                if (blurImage.CanBatch)
                    batchedBlurs.Add((blurImage.Settings, blurImage.Alpha), blurImage);
                else
                    ApplyBlurRenderGraph(blurImage, source, destination, maxStabilizationPixels);
            }

            TryApplyBatchedBlurs();

            if (destinationIdx < data.layerMipLevels.Count && data.layerMipLevels[destinationIdx] > 0)
                cmd.GenerateMips(destination);

            if (blurredImageLayersSeeLower)
                cmd.SetRenderTarget(data.source);

            void TryApplyBatchedBlurs()
            {
                foreach (var kvp in batchedBlurs)
                {
                    var fillEntireRenderTexture = false;
                    foreach (var blur in kvp.Value)
                    {
                        if (!blur.FillEntireRenderTexture)
                            continue;

                        fillEntireRenderTexture = true;
                        break;
                    }

                    if (!fillEntireRenderTexture && kvp.Value.Count == 1)
                    {
                        ApplyBlurRenderGraph(kvp.Value[0], source, destination, maxStabilizationPixels);
                        continue;
                    }

                    float minX, minY, maxX, maxY;
                    if (fillEntireRenderTexture)
                    {
                        (minX, minY, maxX, maxY) = (0, 0, originalWidth, originalHeight);
                    }
                    else
                    {
                        (minX, minY, maxX, maxY) = (float.PositiveInfinity, float.PositiveInfinity, float.NegativeInfinity, float.NegativeInfinity);
                        foreach (var batchedBlur in kvp.Value)
                        {
                            minX = Math.Min(minX, batchedBlur.MinX(rightEye));
                            minY = Math.Min(minY, batchedBlur.MinY(rightEye));
                            maxX = Math.Max(maxX, batchedBlur.MaxX(rightEye));
                            maxY = Math.Max(maxY, batchedBlur.MaxY(rightEye));
                        }
                    }

                    if (destinationRequiresClear)
                    {
                        cmd.SetRenderTarget(destination);
                        cmd.ClearRenderTarget(false, true, Color.clear);
                        cmd.SetRenderTarget(source);
                        destinationRequiresClear = false;
                    }

                    var settings = kvp.Key.blurSettings;
                    if (singlePassVR)
                    {
                        // Left eye extents already calculated. Min/Max that with the right eye values to get a region that covers both eyes.
                        foreach (var batchedBlur in kvp.Value)
                        {
                            minX = Math.Min(minX, batchedBlur.MinX(true));
                            minY = Math.Min(minY, batchedBlur.MinY(true));
                            maxX = Math.Max(maxX, batchedBlur.MaxX(true));
                            maxY = Math.Max(maxY, batchedBlur.MaxY(true));
                        }
                    }

                    var blurRegion = UIBlurCommon.ComputeBlurRegion(minX, minY, maxX, maxY);
                    ApplyBlurUnifiedRenderGraph(source, destination, settings, blurRegion, singlePassVR ? blurRegion : null, null, null, kvp.Key.alpha, maxStabilizationPixels, false, Matrix4x4.identity);

                }

                batchedBlurs.Clear();
            }
        }

        void HandleTextureRequests()
        {
            if (data.textureRequests is not { Count: > 0 })
                return;

            for (int i = 0; i < data.textureRequests.Count; i++)
            {
                var request = data.textureRequests[i];
                var source = data.requestSources[i];
                var destination = data.requestDestinations[i];
                cmd.SetRenderTarget(destination);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetRenderTarget(source);
                var bounds = request.Bounds;
                var region = new Vector4(bounds.x, bounds.y, bounds.width, bounds.height);
                ApplyBlurUnifiedRenderGraph(source, destination, request.Settings, region, singlePassVR ? region : null, null, null, request.Strength, maxStabilizationPixels, false, Matrix4x4.identity);
                if (request.MipLevels > 0)
                    cmd.GenerateMips(destination);
            }
        }

        void ApplyBlurRenderGraph(IBlur iBlur, RenderTargetIdentifier source, RenderTargetIdentifier destination, int maxStabilizationPixels)
        {
            if (!iBlur.HasVisiblePixels(rightEye))
                return;

            if (destinationRequiresClear)
            {
                cmd.SetRenderTarget(new RenderTargetIdentifier(destination, 0, CubemapFace.Unknown, -1));
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.SetRenderTarget(source);
                destinationRequiresClear = false;
            }

            if (singlePassVR)
            {
                var (regionRight, cornersRight, region, corners) = (iBlur.Common.BlurRegionRight, iBlur.Common.ScreenCornersRight, iBlur.Common.BlurRegion, iBlur.Common.ScreenCorners);
                ApplyBlurUnifiedRenderGraph(source, destination, iBlur.Settings, region, regionRight, corners, cornersRight, iBlur.Alpha, maxStabilizationPixels, iBlur.IsAngled, iBlur.Matrix, iBlur.Common.WorldCamera);
            }
            else
            {
                var (region, corners) = rightEye ? (iBlur.Common.BlurRegionRight, iBlur.Common.ScreenCornersRight) : (iBlur.Common.BlurRegion, iBlur.Common.ScreenCorners);
                ApplyBlurUnifiedRenderGraph(source, destination, iBlur.Settings, region, null, corners, null, iBlur.Alpha, maxStabilizationPixels, iBlur.IsAngled, iBlur.Matrix, iBlur.Common.WorldCamera);
            }
        }

        void ApplyBlurUnifiedRenderGraph(RenderTargetIdentifier source, RenderTargetIdentifier destination, BlurSettings settings, Vector4 blurRegion, Vector4? blurRegionRight, Vector4[] blurCorners, Vector4[] blurCornersRight, float alpha, int maxStabilizationPixels, bool isAngled, Matrix4x4 transformationMatrix, Camera uiCamera = null)
        {
            var useQuadBlit = UsesQuadBlit(transformationMatrix, uiCamera, overlayCompatibilityFix);
            blurRegion = PrepareBlurRegion(blurRegion, settings, alpha, renderScale, originalWidth, originalHeight, maxStabilizationPixels);

            var hasRightEye = blurRegionRight.HasValue;
            if (hasRightEye)
            {
                blurRegionRight = PrepareBlurRegion(blurRegionRight.Value, settings, alpha, renderScale, originalWidth, originalHeight, maxStabilizationPixels);
                var scaleFactorRight = new Vector2(originalWidth / blurRegionRight.Value.z, originalHeight / blurRegionRight.Value.w);
                cmd.SetGlobalVector(ScaleFactorRightID, scaleFactorRight);
                var offsetRight = scaleFactorRight * new Vector2(blurRegionRight.Value.x / originalWidth, blurRegionRight.Value.y / originalHeight);
                cmd.SetGlobalVector(SourceOffsetRightID, offsetRight);
            }

            var handleIdx = textureIndices.Dequeue();
            var temp1 = textureHandle[handleIdx];
            var temp2 = temp1;
            var temp1Dimensions = textureDimensions[handleIdx];
            var temp2Dimensions = temp1Dimensions;

            var scaleFactor = new Vector2(originalWidth / blurRegion.z, originalHeight / blurRegion.w);
            var offset = scaleFactor * new Vector2(blurRegion.x / originalWidth, blurRegion.y / originalHeight);
            cmd.SetGlobalVector(ScaleFactorID, scaleFactor);
            cmd.SetGlobalVector(SourceOffsetID, offset);

            FullScreenBlit(cmd, source, temp1, regionalBlitsMat, Convert.ToInt32(settings.hqResample));

            if (alpha > 0)
            {
                if (useComputeShaders)
                    ComputeBlur();
                else
                    TraditionalBlur();
            }
            FinalBlitToDestination();

            void ComputeBlur()
            {
                int totalIterations = 0;
                int threadGroupsX;
                int threadGroupsY;
                var computeShader = hasRightEye ? vrComputeBlurs : computeBlurs;

                bool temp2NeedsInit = true;
                if (alpha > 0)
                {
                    cmd.SetComputeIntParam(computeShader, ComputeOffsetCenterID, 0);
                    foreach (var section in settings.downscaleSections)
                    {
                        var (isSeparable, setSamplesPerSide, skip, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();
                        if (skip)
                            continue;

                        if (setSamplesPerSide)
                        {
                            cmd.SetComputeIntParam(computeShader, TapsPerSideHorComputeID, section.horizontalSamplesPerSide);
                            cmd.SetComputeIntParam(computeShader, TapsPerSideVertComputeID, section.verticalSamplesPerSide);
                        }

                        var iterations = section.iterations;
                        var baseSampleDistance = section.sampleDistance;
                        var sectionWidth = temp1Dimensions.width;
                        var sectionHeight = temp1Dimensions.height;

                        for (int i = 0; i < iterations; i++, totalIterations++)
                        {
                            cmd.SetComputeIntParam(computeShader, ComputeBlurIterationID, i);
                            handleIdx = textureIndices.Dequeue();
                            temp2 = textureHandle[handleIdx];
                            temp2Dimensions = textureDimensions[handleIdx];
                            cmd.SetComputeVectorParam(computeShader, ComputeSampleOffsetID, GetDownscaleSampleOffset(sectionWidth, sectionHeight, temp2Dimensions.width, temp2Dimensions.height, i, iterations));

                            cmd.SetComputeVectorParam(computeShader, ComputeResultDimensionsID, new Vector2(temp2Dimensions.width, temp2Dimensions.height));
                            cmd.SetComputeFloatParam(computeShader, ComputeSampleDistID, alpha * baseSampleDistance * renderScale);

                            cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeSourceID, temp1);
                            cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeResultID, temp2);

                            threadGroupsX = (temp2Dimensions.width + ThreadGroupSizeX - 1) / ThreadGroupSizeX;
                            threadGroupsY = (temp2Dimensions.height + ThreadGroupSizeY - 1) / ThreadGroupSizeY;
                            cmd.DispatchCompute(computeShader, firstKernelIdx, threadGroupsX, threadGroupsY, 1);

                            if (!isSeparable)
                            {
                                temp2NeedsInit = true;
                                (temp1, temp1Dimensions, temp2, temp2Dimensions) = (temp2, temp2Dimensions, temp1, temp1Dimensions);
                                continue;
                            }

                            temp2NeedsInit = false;
                            handleIdx = textureIndices.Dequeue();
                            temp1 = textureHandle[handleIdx];
                            temp1Dimensions = textureDimensions[handleIdx];

                            cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeSourceID, temp2);
                            cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeResultID, temp1);
                            threadGroupsX = (temp1Dimensions.width + ThreadGroupSizeX - 1) / ThreadGroupSizeX;
                            threadGroupsY = (temp1Dimensions.height + ThreadGroupSizeY - 1) / ThreadGroupSizeY;
                            cmd.DispatchCompute(computeShader, secondKernelIdx, threadGroupsX, threadGroupsY, 1);
                        }
                    }
                }

                if (temp2NeedsInit)
                {
                    handleIdx = textureIndices.Dequeue();
                    temp2 = textureHandle[handleIdx];
                    temp2Dimensions = textureDimensions[handleIdx];
                    cmd.SetComputeVectorParam(computeShader, ComputeResultDimensionsID, new Vector2(temp2Dimensions.width, temp2Dimensions.height));
                }

                threadGroupsX = (temp2Dimensions.width + ThreadGroupSizeX - 1) / ThreadGroupSizeX;
                threadGroupsY = (temp2Dimensions.height + ThreadGroupSizeY - 1) / ThreadGroupSizeY;
                totalIterations = 0;
                cmd.SetComputeIntParam(computeShader, ComputeOffsetCenterID, 1);
                foreach (var section in settings.blurSections)
                {
                    var (isSeparable, setSamplesPerSide, skip, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();
                    if (skip)
                        continue;

                    if (setSamplesPerSide)
                    {
                        cmd.SetComputeIntParam(computeShader, TapsPerSideHorComputeID, section.horizontalSamplesPerSide);
                        cmd.SetComputeIntParam(computeShader, TapsPerSideVertComputeID, section.verticalSamplesPerSide);
                    }

                    var iterations = section.iterations;
                    var baseSampleDistance = section.sampleDistance;

                    if (!isSeparable)
                    {
                        for (int i = 0; i < iterations; i++, totalIterations++)
                        {
                            cmd.SetComputeVectorParam(computeShader, ComputeSampleOffsetID, GetBlurSampleOffset(i, iterations));

                            cmd.SetComputeIntParam(computeShader, ComputeBlurIterationID, i);
                            var sampleDistance = alpha * (baseSampleDistance + settings.blurAdditionalDistancePerIteration * totalIterations);
                            cmd.SetComputeFloatParam(computeShader, ComputeSampleDistID, sampleDistance * renderScale);
                            cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeSourceID, temp1);
                            cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeResultID, temp2);
                            cmd.DispatchCompute(computeShader, firstKernelIdx, threadGroupsX, threadGroupsY, 1);
                            (temp1, temp1Dimensions, temp2, temp2Dimensions) = (temp2, temp2Dimensions, temp1, temp1Dimensions);
                        }
                    }
                    else
                    {
                        cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeSourceID, temp1);
                        cmd.SetComputeTextureParam(computeShader, firstKernelIdx, ComputeResultID, temp2);
                        cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeSourceID, temp2);
                        cmd.SetComputeTextureParam(computeShader, secondKernelIdx, ComputeResultID, temp1);
                        for (int i = 0; i < iterations; i++, totalIterations++)
                        {
                            cmd.SetComputeVectorParam(computeShader, ComputeSampleOffsetID, GetBlurSampleOffset(i, iterations));
                    
                            var sampleDistance = alpha * (baseSampleDistance + settings.blurAdditionalDistancePerIteration * totalIterations);
                            cmd.SetComputeFloatParam(computeShader, ComputeSampleDistID, sampleDistance * renderScale);
                            cmd.DispatchCompute(computeShader, firstKernelIdx, threadGroupsX, threadGroupsY, 1);
                            cmd.DispatchCompute(computeShader, secondKernelIdx, threadGroupsX, threadGroupsY, 1);
                        }
                    }
                }
            }

            void TraditionalBlur()
            {
                int totalIterations = 0;
                bool temp2NeedsInit = true;
                if (alpha > 0)
                {
                    cmd.SetGlobalInt(OffsetCenterID, 0);
                    foreach (var section in settings.downscaleSections)
                    {
                        var (isSeparable, setSamplesPerSide, _, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();

                        if (setSamplesPerSide)
                        {
                            cmd.SetGlobalInt(TapsPerSideHorID, section.horizontalSamplesPerSide);
                            cmd.SetGlobalInt(TapsPerSideVertID, section.verticalSamplesPerSide);
                        }

                        var iterations = section.iterations;
                        var baseSampleDistance = section.sampleDistance;
                        var sectionWidth = temp1Dimensions.width;
                        var sectionHeight = temp1Dimensions.height;
                        
                        for (int i = 0; i < iterations; i++, totalIterations++)
                        {
                            cmd.SetGlobalInt(BlurIterationID, i);
                            handleIdx = textureIndices.Dequeue();
                            temp2 = textureHandle[handleIdx];
                            temp2Dimensions = textureDimensions[handleIdx];
                            cmd.SetGlobalVector(SampleOffsetID, GetDownscaleSampleOffset(sectionWidth, sectionHeight, temp2Dimensions.width, temp2Dimensions.height, i, iterations));

                            cmd.SetGlobalFloat(BlurSampleDistID, alpha * baseSampleDistance * renderScale);
                            FullScreenBlit(cmd, temp1, temp2, blursMat, firstKernelIdx);

                            if (!isSeparable)
                            {
                                temp2NeedsInit = true;
                                (temp1, temp1Dimensions, temp2, temp2Dimensions) = (temp2, temp2Dimensions, temp1, temp1Dimensions);
                                continue;
                            }

                            temp2NeedsInit = false;
                            handleIdx = textureIndices.Dequeue();
                            temp1 = textureHandle[handleIdx];
                            temp1Dimensions = textureDimensions[handleIdx];
                            FullScreenBlit(cmd, temp2, temp1, blursMat, secondKernelIdx);
                        }
                    }
                }

                if (temp2NeedsInit)
                    temp2 = textureHandle[textureIndices.Dequeue()];


                totalIterations = 0;
                cmd.SetGlobalInt(OffsetCenterID, 1);
                foreach (var section in settings.blurSections)
                {
                    var (isSeparable, setSamplesPerSide, skip, firstKernelIdx, secondKernelIdx) = section.GetSectionBehaviour();
                    if (skip)
                        continue;

                    if (setSamplesPerSide)
                    {
                        cmd.SetGlobalInt(TapsPerSideHorID, section.horizontalSamplesPerSide);
                        cmd.SetGlobalInt(TapsPerSideVertID, section.verticalSamplesPerSide);
                    }

                    var baseSampleDistance = section.sampleDistance;
                    var iterations = section.iterations;

                    for (int i = 0; i < iterations; i++, totalIterations++)
                    {
                        cmd.SetGlobalInt(BlurIterationID, i);
                        cmd.SetGlobalVector(SampleOffsetID, GetBlurSampleOffset(i, iterations));

                        var sampleDistance = alpha * (baseSampleDistance + settings.blurAdditionalDistancePerIteration * totalIterations);
                        cmd.SetGlobalFloat(BlurSampleDistID, sampleDistance * renderScale);
                        FullScreenBlit(cmd, temp1, temp2, blursMat, firstKernelIdx);

                        if (!isSeparable)
                        {
                            (temp1, temp2) = (temp2, temp1);
                            continue;
                        }

                        FullScreenBlit(cmd, temp2, temp1, blursMat, secondKernelIdx);
                    }
                }
            }

            void FinalBlitToDestination()
            {
                cmd.SetGlobalFloat(DitherStrengthID, alpha * settings.ditherStrength);

                cmd.SetGlobalVector(DestinationRegionSizeID, blurRegion);
                if (hasRightEye)
                    cmd.SetGlobalVector(DestinationRegionSizeRightID, blurRegionRight.Value);

                cmd.SetGlobalFloat(VibrancyID, (alpha * settings.vibrancy + 1) * 0.5f);
                cmd.SetGlobalFloat(BrightnessID, alpha * settings.brightness);
                cmd.SetGlobalFloat(ContrastID, alpha * settings.contrast + 1);
                cmd.SetGlobalVector(TintID, alpha * settings.tint);

                if (useQuadBlit)
                {
                    if (multiPassVR)
                    {
                        BlitToQuad(cmd, temp1, destination, quadBlitsMat, transformationMatrix, blurRegion, blurRegionRight, originalWidth, originalHeight, 0);
                    }
                    else
                    {
                        cmd.SetProjectionMatrix(uiCamera?.projectionMatrix ?? OverlayUIProjectionMatrix);
                        cmd.SetViewMatrix(uiCamera?.worldToCameraMatrix ?? Matrix4x4.identity);
                        BlitToQuad(cmd, temp1, destination, quadBlitsMat, transformationMatrix, blurRegion, blurRegionRight, originalWidth, originalHeight, 0);
                        cmd.SetProjectionMatrix(camera.projectionMatrix);
                        cmd.SetViewMatrix(camera.worldToCameraMatrix);
                    }
                }
                else
                {
                    if (isAngled)
                    {
                        cmd.SetGlobalFloat(RenderScaleID, renderScale);
                        cmd.SetGlobalVectorArray(CornersID, blurCorners);
                        if (hasRightEye)
                            cmd.SetGlobalVectorArray(CornersRightID, blurCornersRight);
                    }

                    BlitToRegion(cmd, temp1, destination, regionalBlitsMat, blurRegion, originalWidth, originalHeight, isAngled ? 3 : 2);
                }

                cmd.SetRenderTarget(data.source);
            }
        }
    }
}
}
#endif
