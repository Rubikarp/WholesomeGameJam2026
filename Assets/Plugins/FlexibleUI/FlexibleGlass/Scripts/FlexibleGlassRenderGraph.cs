#if UNITY_2023_3_OR_NEWER
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleGlassPass
{
    private static readonly string[] KawaseTextureNames =
    {
        "Flexible Glass Down 0",
        "Flexible Glass Down 1",
        "Flexible Glass Down 2",
        "Flexible Glass Down 3",
        "Flexible Glass Down 4",
        "Flexible Glass Down 5"
    };

    private sealed class GlassPassData
    {
        public FlexibleGlassPass owner;
        public FrameInfo frame;
        public TextureHandle source;
        public TextureHandle capture;
        public TextureHandle backdrop;
        public TextureHandle imageOutput;
        public TextureHandle sdfAtlas;
        public TextureHandle sdfMask;
        public TextureHandle sdfSeedA;
        public TextureHandle sdfSeedB;
        public TextureHandle sdfMipScratch;
        public readonly TextureHandle[] blurTextures = new TextureHandle[MaxKawaseTextures];
        public readonly Vector2Int[] blurDimensions = new Vector2Int[MaxKawaseTextures];
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        var cameraData = frameData.Get<UniversalCameraData>();
        ConfigureStereo(cameraData.camera, cameraData.xr);
        if (cameraData.isPreviewCamera || !PrepareFrame(cameraData.camera, cameraData.cameraTargetDescriptor, out var frame))
            return;
        var hasPendingSdfJobs = sdfCache.HasPendingJobs;
        if (frame.sdfOnly && !hasPendingSdfJobs && !sdfCache.HasPendingRaycasts)
            return;
        var descriptor = cameraData.cameraTargetDescriptor;
        descriptor.depthBufferBits = 0;
        descriptor.depthStencilFormat = GraphicsFormat.None;
        descriptor.msaaSamples = 1;
        descriptor.enableRandomWrite = false;
        var backdropReconstructionLevels = GetBackdropReconstructionLevelCount(frame);
        descriptor.useMipMap = backdropReconstructionLevels > 0;
        descriptor.autoGenerateMips = false;
        descriptor.mipCount = backdropReconstructionLevels + 1;
        descriptor.width = frame.targetWidth;
        descriptor.height = frame.targetHeight;

        using var builder = renderGraph.AddUnsafePass<GlassPassData>(ProfilerTag, out var passData);
        builder.AllowGlobalStateModification(true);
        builder.AllowPassCulling(false);

        passData.owner = this;
        passData.frame = frame;
        passData.source = frameData.Get<UniversalResourceData>().activeColorTexture;
        builder.UseTexture(passData.source, AccessFlags.ReadWrite);

        if (frame.hasRetainedFields)
        {
            passData.sdfAtlas = renderGraph.ImportTexture(sdfCache.FieldAtlas);
            builder.UseTexture(passData.sdfAtlas, hasPendingSdfJobs ? AccessFlags.ReadWrite : AccessFlags.Read);
            if (hasPendingSdfJobs)
            {
                passData.sdfMask = renderGraph.ImportTexture(sdfCache.Mask);
                passData.sdfSeedA = renderGraph.ImportTexture(sdfCache.SeedA);
                passData.sdfSeedB = renderGraph.ImportTexture(sdfCache.SeedB);
                passData.sdfMipScratch = renderGraph.ImportTexture(sdfCache.MipScratch);
                builder.UseTexture(passData.sdfMask, AccessFlags.ReadWrite);
                builder.UseTexture(passData.sdfSeedA, AccessFlags.ReadWrite);
                builder.UseTexture(passData.sdfSeedB, AccessFlags.ReadWrite);
                builder.UseTexture(passData.sdfMipScratch, AccessFlags.ReadWrite);
            }
        }

        if (frame.sdfOnly)
        {
            builder.SetRenderFunc(static (GlassPassData data, UnsafeGraphContext context) => data.owner.ExecuteRenderGraph(data, CommandBufferHelpers.GetNativeCommandBuffer(context.cmd)));
            return;
        }

        if (NeedsSharpBackdrop(frame))
        {
            var backdropDescriptor = new TextureDesc(descriptor)
            {
                name = "Flexible Glass Backdrop",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            passData.backdrop = renderGraph.CreateTexture(backdropDescriptor);
            builder.UseTexture(passData.backdrop, AccessFlags.ReadWrite);
        }

        descriptor.useMipMap = false;
        descriptor.mipCount = 1;
        if (frame.blurPlan.integrated)
        {
            passData.imageOutput = renderGraph.ImportTexture(GetImageOutput(cameraData.camera, cameraData.cameraTargetDescriptor, GetBlurReconstructionLevelCount(frame)));
            builder.UseTexture(passData.imageOutput, AccessFlags.Read);
        }
        else
        {
            descriptor.graphicsFormat = frame.blurFormat;
            descriptor.width = frame.blurRegion.width;
            descriptor.height = frame.blurRegion.height;
            var captureMipLevels = frame.blurPlan.kawaseIterations > 0 ? GetBlurReconstructionLevelCount(frame) : 0;
            descriptor.useMipMap = captureMipLevels > 0;
            descriptor.mipCount = captureMipLevels + 1;
            passData.capture = renderGraph.CreateTexture(new TextureDesc(descriptor)
            {
                name = "Flexible Glass Capture",
                filterMode = captureMipLevels > 0 ? FilterMode.Trilinear : FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            });
            builder.UseTexture(passData.capture, AccessFlags.ReadWrite);
            descriptor.useMipMap = false;
            descriptor.mipCount = 1;
            if (frame.hasGlassImages)
            {
                passData.imageOutput = renderGraph.ImportTexture(GetImageOutput(cameraData.camera, cameraData.cameraTargetDescriptor, GetImageReconstructionLevelCount(frame)));
                builder.UseTexture(passData.imageOutput, AccessFlags.ReadWrite);
            }
        }

        CreateKawaseTextures(renderGraph, builder, descriptor, frame, passData);
        builder.SetRenderFunc(static (GlassPassData data, UnsafeGraphContext context) => data.owner.ExecuteRenderGraph(data, CommandBufferHelpers.GetNativeCommandBuffer(context.cmd)));
    }

    private static void CreateKawaseTextures(RenderGraph renderGraph, IUnsafeRenderGraphBuilder builder, RenderTextureDescriptor descriptor, FrameInfo frame, GlassPassData passData)
    {
        var plan = frame.blurPlan;
        if (plan.integrated)
            return;

        for (int i = 0; i < plan.kawaseIterations; i++)
        {
            var width = GetKawaseDimension(frame.blurRegion.width, i);
            var height = GetKawaseDimension(frame.blurRegion.height, i);
            var dimensions = new Vector2Int(width, height);
            descriptor.width = width;
            descriptor.height = height;
            var texture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor, KawaseTextureNames[i], false, FilterMode.Bilinear);
            passData.blurTextures[i] = texture;
            passData.blurDimensions[i] = dimensions;
            builder.UseTexture(texture, AccessFlags.ReadWrite);
        }

    }

    private void ExecuteRenderGraph(GlassPassData data, CommandBuffer cmd)
    {
        sdfCache.GeneratePending(cmd);
        if (data.frame.sdfOnly)
            return;
        if (NeedsSharpBackdrop(data.frame))
            CaptureBackdrop(cmd, data.source, data.backdrop, data.frame);

        RenderTargetIdentifier blurred;
        var plan = data.frame.blurPlan;
        if (plan.integrated)
        {
            blurred = data.imageOutput;
        }
        else
        {
            ExtractRegion(cmd, data.source, data.capture, data.frame);
            blurred = data.capture;
            var sourceDimensions = new Vector2Int(data.frame.blurRegion.width, data.frame.blurRegion.height);
            for (int i = 0; i < plan.kawaseIterations; i++)
            {
                var dimensions = data.blurDimensions[i];
                var destination = data.blurTextures[i];
                KawaseBlit(cmd, blurred, destination, sourceDimensions.x, sourceDimensions.y, dimensions.x, dimensions.y, plan.kawaseRadius, KawaseDownPass, 0f, default);
                blurred = destination;
                sourceDimensions = dimensions;
            }

            for (int i = 0; i < plan.kawaseIterations; i++)
            {
                var sourceLevel = plan.kawaseIterations - i - 2;
                var finalUpsample = i == plan.kawaseIterations - 1;
                var destination = finalUpsample ? data.capture : data.blurTextures[sourceLevel];
                var dimensions = finalUpsample ? new Vector2Int(data.frame.blurRegion.width, data.frame.blurRegion.height) : data.blurDimensions[sourceLevel];
                KawaseBlit(cmd, blurred, destination, sourceDimensions.x, sourceDimensions.y, dimensions.x, dimensions.y, plan.kawaseRadius, KawaseUpPass, finalUpsample ? plan.kawaseDitherStrength : 0f, finalUpsample ? data.frame.blurRegion.position : default);
                blurred = destination;
                sourceDimensions = dimensions;
            }

            if (GetBlurReconstructionLevelCount(data.frame) > 0)
                cmd.GenerateMips(blurred);

            if (data.frame.hasGlassImages)
                PublishImageBlur(cmd, blurred, data.imageOutput, data.frame);
        }

        if (data.frame.elementCount > 0)
        {
            if (NeedsSharpBackdrop(data.frame))
                Composite(cmd, data.source, data.backdrop, blurred, data.frame);
            else
                Composite(cmd, data.source, blurred, blurred, data.frame);
        }
    }
}
}
#endif
