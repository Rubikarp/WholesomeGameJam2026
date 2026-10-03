using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LightSide
{
    internal struct GpuUploadTargetSupports
    {
        internal GpuUploadSupportInfo color;
        internal GpuUploadSupportInfo depth;
        internal GpuUploadSupportInfo stencil;

        internal GpuUploadAspectMask SupportedAspects
        {
            get
            {
                GpuUploadAspectMask result = GpuUploadAspectMask.None;
                if (color.Supported) result |= GpuUploadAspectMask.Color;
                if (depth.Supported) result |= GpuUploadAspectMask.Depth;
                if (stencil.Supported) result |= GpuUploadAspectMask.Stencil;
                return result;
            }
        }

        internal bool TryGet(GpuUploadAspect aspect, out GpuUploadSupportInfo support)
        {
            switch (aspect)
            {
                case GpuUploadAspect.Color:
                    support = color;
                    return support.Format != GpuUploadFormat.Unknown;
                case GpuUploadAspect.Depth:
                    support = depth;
                    return support.Format != GpuUploadFormat.Unknown;
                case GpuUploadAspect.Stencil:
                    support = stencil;
                    return support.Format != GpuUploadFormat.Unknown;
                default:
                    support = default;
                    return false;
            }
        }
    }

    public static partial class GpuUpload
    {
        private const uint KnownSupportFlags = (1u << 7) - 1;
        private const uint KnownAspectMask = (1u << 3) - 1;

        /// <summary>Returns backend-independent block geometry for one Unity format aspect.</summary>
        public static bool TryGetFormatLayout(GraphicsFormat format, GpuUploadAspect aspect,
            out GpuUploadFormatLayout layout)
        {
            if (!TryMapFormat(format, out var uploadFormat))
            {
                layout = default;
                return false;
            }
            return TryGetFormatLayout(uploadFormat, aspect, out layout);
        }

        /// <summary>Returns backend-independent block geometry for one stable upload format aspect.</summary>
        public static bool TryGetFormatLayout(GpuUploadFormat format, GpuUploadAspect aspect,
            out GpuUploadFormatLayout layout) =>
            TryGetFormatLayoutCore(format, aspect, out layout);

        /// <summary>
        /// Queries exact active-backend support from Unity format types without allocating. This
        /// facade maps both values exactly and delegates to the stable upload contract.
        /// </summary>
        public static bool TryQuerySupport(in GpuUploadSupportQuery query,
            out GpuUploadSupportInfo supportInfo, out GpuUploadError error)
        {
            supportInfo = default;
            if (!TryMapFormat(query.Format, out var format))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            if (!TryMapDimension(query.Dimension, out var dimension)
                || !IsDimensionSupportedByUnity(query.Dimension))
            {
                error = GpuUploadError.UnsupportedDimension;
                return false;
            }
            var description = new GpuUploadResourceDescription(format, dimension,
                query.ResourceKind, query.Width, query.Height, query.Depth, query.Layers,
                query.MipCount, query.SampleCount);
            return TryQuerySupport(description, query.Aspect, out supportInfo, out error);
        }

        /// <summary>
        /// Queries format-level support directly from stable ABI values without allocating or
        /// depending on Unity format enum mappings.
        /// </summary>
        public static bool TryQuerySupport(GpuUploadFormat format, GpuUploadDimension dimension,
            GpuUploadResourceKind resourceKind, GpuUploadAspect aspect,
            out GpuUploadSupportInfo supportInfo, out GpuUploadError error) =>
            TryQuerySupport(format, dimension, resourceKind, aspect,
                0, 0, 0, 0, 0, 1, out supportInfo, out error);

        /// <summary>
        /// Queries exact active-backend support for a reusable storage description and one
        /// independently addressable aspect without allocating.
        /// </summary>
        public static bool TryQuerySupport(in GpuUploadResourceDescription description,
            GpuUploadAspect aspect, out GpuUploadSupportInfo supportInfo,
            out GpuUploadError error) =>
            TryQuerySupport(description.Format, description.Dimension,
                description.ResourceKind, aspect, description.Width, description.Height,
                description.Depth, description.Layers, description.MipCount,
                description.SampleCount, out supportInfo, out error);

        /// <summary>
        /// Queries one concrete storage shape directly from stable ABI values. All dimensions,
        /// layer counts, mip counts, and the sample count must be positive.
        /// </summary>
        public static bool TryQuerySupport(GpuUploadFormat format, GpuUploadDimension dimension,
            GpuUploadResourceKind resourceKind, GpuUploadAspect aspect, int width, int height,
            int depth, int layers, int mipCount, int sampleCount,
            out GpuUploadSupportInfo supportInfo, out GpuUploadError error)
        {
            supportInfo = default;
            Initialize();
            CheckDeviceEpoch();
            if (!CanAdmitNewWork)
            {
                error = AdmissionError;
                return false;
            }
            if (!TryGetFormatLayoutCore(format, aspect, out _))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            if (dimension == GpuUploadDimension.Unknown
                || (uint)dimension >= GpuUploadAbi.DimensionCount)
            {
                error = GpuUploadError.UnsupportedDimension;
                return false;
            }
            if (!TryValidateSupportShape(resourceKind, aspect, dimension,
                    width, height, depth, layers, mipCount, sampleCount, out error))
                return false;
            try
            {
                return TryQuerySupportCore(format, dimension, resourceKind, aspect,
                    width, height, depth, layers, mipCount, sampleCount,
                    out supportInfo, out error);
            }
            catch (Exception exception) when (exception is DllNotFoundException
                                               || exception is EntryPointNotFoundException
                                               || exception is BadImageFormatException)
            {
                RecordBindingFailure(exception);
                supported = false;
                bindingUnavailable = true;
                availabilityError = GpuUploadError.UnsupportedBackend;
                PreserveUnprovenSession();
                error = AdmissionError;
                return false;
            }
        }

        private static bool TrySelectPrimaryAspect(Texture texture,
            out GpuUploadAspect aspect, out GpuUploadError error)
        {
            aspect = GpuUploadAspect.Color;
            if (texture == null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            if (TryMapFormat(texture.graphicsFormat, out var colorFormat)
                && TryGetFormatLayoutCore(colorFormat, GpuUploadAspect.Color, out _))
            {
                error = GpuUploadError.None;
                return true;
            }
#if UNITY_2021_2_OR_NEWER
            if (texture is RenderTexture renderTexture && renderTexture.IsCreated()
                && TryMapFormat(renderTexture.depthStencilFormat, out var depthFormat)
                && TryGetFormatLayoutCore(depthFormat, GpuUploadAspect.Depth, out _))
            {
                aspect = GpuUploadAspect.Depth;
                error = GpuUploadError.None;
                return true;
            }
            if (texture is RenderTexture stencilTexture && stencilTexture.IsCreated()
                && TryMapFormat(stencilTexture.depthStencilFormat, out var stencilFormat)
                && TryGetFormatLayoutCore(stencilFormat, GpuUploadAspect.Stencil, out _))
            {
                aspect = GpuUploadAspect.Stencil;
                error = GpuUploadError.None;
                return true;
            }
#endif
            error = GpuUploadError.UnsupportedTexture;
            return false;
        }

        /// <summary>Calculates source span and physical staging requirements for one registered target region.</summary>
        public static bool TryGetRegionLayout(GpuUploadTarget target, in GpuUploadRegion region,
            out GpuUploadRegionLayout layout, out GpuUploadError error)
        {
            layout = default;
            if (target == null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            if (!target.supports.TryGet(region.Aspect, out var supportInfo))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            var description = target.info.Description;
            return TryCalculateRegionLayout(description, supportInfo, region, out layout, out error);
        }

        /// <summary>Calculates source span and physical staging requirements from reusable target capabilities.</summary>
        public static bool TryGetRegionLayout(in GpuUploadTargetInfo target,
            in GpuUploadSupportInfo supportInfo, in GpuUploadRegion region,
            out GpuUploadRegionLayout layout, out GpuUploadError error)
        {
            var description = target.Description;
            return TryCalculateRegionLayout(description, supportInfo, region,
                out layout, out error);
        }

        /// <summary>
        /// Calculates source span and physical staging requirements before texture creation from
        /// one reusable storage description and its cached support descriptor.
        /// </summary>
        public static bool TryGetRegionLayout(in GpuUploadResourceDescription description,
            in GpuUploadSupportInfo supportInfo, in GpuUploadRegion region,
            out GpuUploadRegionLayout layout, out GpuUploadError error) =>
            TryCalculateRegionLayout(description, supportInfo, region, out layout, out error);

        /// <summary>Calculates physical staging capacity for an ordered region span of one registered target.</summary>
        public static bool TryEstimateStagingBytes(GpuUploadTarget target,
            ReadOnlySpan<GpuUploadRegion> regions, out ulong stagingBytes,
            out GpuUploadError error)
        {
            stagingBytes = 0;
            if (target == null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            var description = target.info.Description;
            for (int i = 0; i < regions.Length; i++)
            {
                ref readonly var region = ref regions[i];
                if (!target.supports.TryGet(region.Aspect, out var supportInfo))
                {
                    stagingBytes = 0;
                    error = GpuUploadError.UnsupportedFormat;
                    return false;
                }
                if (!TryAccumulateStagingCore(description, supportInfo, region,
                        ref stagingBytes, out error))
                {
                    stagingBytes = 0;
                    return false;
                }
            }
            error = GpuUploadError.None;
            return true;
        }

        /// <summary>Calculates physical staging capacity for regions sharing one reusable support descriptor.</summary>
        public static bool TryEstimateStagingBytes(in GpuUploadTargetInfo target,
            in GpuUploadSupportInfo supportInfo, ReadOnlySpan<GpuUploadRegion> regions,
            out ulong stagingBytes, out GpuUploadError error)
        {
            var description = target.Description;
            return TryEstimateStagingBytes(description, supportInfo, regions,
                out stagingBytes, out error);
        }

        /// <summary>
        /// Calculates physical staging capacity before texture creation for an ordered region span
        /// sharing one cached support descriptor.
        /// </summary>
        public static bool TryEstimateStagingBytes(in GpuUploadResourceDescription description,
            in GpuUploadSupportInfo supportInfo, ReadOnlySpan<GpuUploadRegion> regions,
            out ulong stagingBytes, out GpuUploadError error)
        {
            stagingBytes = 0;
            for (int i = 0; i < regions.Length; i++)
                if (!TryAccumulateStagingCore(description, supportInfo, regions[i],
                        ref stagingBytes, out error))
                {
                    stagingBytes = 0;
                    return false;
                }
            error = GpuUploadError.None;
            return true;
        }

        /// <summary>
        /// Adds one aligned physical footprint to an incremental batch estimate. The accumulator
        /// remains unchanged when validation fails.
        /// </summary>
        public static bool TryAccumulateStagingBytes(GpuUploadTarget target,
            in GpuUploadRegion region, ref ulong stagingBytes, out GpuUploadError error)
        {
            if (target == null)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            if (!target.supports.TryGet(region.Aspect, out var supportInfo))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            var description = target.info.Description;
            return TryAccumulateStagingCore(description, supportInfo, region,
                ref stagingBytes, out error);
        }

        /// <summary>
        /// Adds one aligned physical footprint to a pre-creation batch estimate. The accumulator
        /// remains unchanged when the description, support descriptor, or region is invalid.
        /// </summary>
        public static bool TryAccumulateStagingBytes(
            in GpuUploadResourceDescription description,
            in GpuUploadSupportInfo supportInfo, in GpuUploadRegion region,
            ref ulong stagingBytes, out GpuUploadError error) =>
            TryAccumulateStagingCore(description, supportInfo, region,
                ref stagingBytes, out error);

        private static bool TryAccumulateStagingCore(in GpuUploadResourceDescription target,
            in GpuUploadSupportInfo supportInfo, in GpuUploadRegion region,
            ref ulong stagingBytes, out GpuUploadError error)
        {
            if (!TryCalculateRegionLayout(target, supportInfo, region, out var layout, out error)
                || !TryAlignUp(stagingBytes, (uint)supportInfo.StagingOffsetAlignment,
                    out ulong aligned)
                || aligned > ulong.MaxValue - layout.StagingBytes)
            {
                if (error == GpuUploadError.None) error = GpuUploadError.InvalidLayout;
                return false;
            }
            stagingBytes = aligned + layout.StagingBytes;
            return true;
        }

        private static bool TryQuerySupportCore(GpuUploadFormat format, GpuUploadDimension dimension,
            GpuUploadResourceKind resourceKind, GpuUploadAspect aspect, int width, int height,
            int depth, int layers, int mipCount, int sampleCount,
            out GpuUploadSupportInfo supportInfo, out GpuUploadError error)
        {
            supportInfo = default;
            if (!TryGetFormatLayoutCore(format, aspect, out var layout)
                || !IsResourceAspect(resourceKind, aspect))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            var query = new GpuUploadAbi.SupportQuery
            {
                format = (uint)format,
                dimension = (uint)dimension,
                resourceKind = (uint)resourceKind,
                aspect = (uint)aspect,
                width = (uint)width,
                height = (uint)height,
                depth = (uint)depth,
                layers = (uint)layers,
                mipCount = (uint)mipCount,
                sampleCount = (uint)sampleCount
            };
            var native = new GpuUploadAbi.SupportInfo();
            error = GpuUploadAbi.QuerySupport(ref query, ref native);
            if (error != GpuUploadError.None)
            {
                ObserveBackendError(error);
                return false;
            }
            if (!TryParseSupportInfo(native, format, dimension, resourceKind, aspect,
                    layout, out supportInfo))
            {
                error = GpuUploadError.AbiMismatch;
                ObserveBackendError(error);
                return false;
            }
            return true;
        }

        private static bool TryParseSupportInfo(in GpuUploadAbi.SupportInfo native,
            GpuUploadFormat format, GpuUploadDimension dimension,
            GpuUploadResourceKind resourceKind, GpuUploadAspect aspect,
            in GpuUploadFormatLayout layout, out GpuUploadSupportInfo supportInfo)
        {
            supportInfo = default;
            var flags = (GpuUploadSupportFlags)native.flags;
            var aspects = (GpuUploadAspectMask)native.supportedAspects;
            bool isSupported = native.supported != 0;
            bool regionModeValid = !isSupported
                || ((flags & GpuUploadSupportFlags.Regional) != 0)
                != ((flags & GpuUploadSupportFlags.WholeMipOnly) != 0);
            bool alignmentsValid = native.stagingRowPitchAlignment <= int.MaxValue
                && native.stagingOffsetAlignment <= int.MaxValue
                && (!isSupported || IsPowerOfTwo(native.stagingRowPitchAlignment)
                    && IsPowerOfTwo(native.stagingOffsetAlignment));
            if (native.structSize != GpuUploadAbi.SupportInfoSize || native.supported > 1
                || (native.flags & ~KnownSupportFlags) != 0
                || (native.supportedAspects & ~KnownAspectMask) != 0
                || (aspects & ~layout.Aspects) != 0
                || isSupported && (aspects & AspectMask(aspect)) == 0
                || native.blockWidth != layout.BlockWidth
                || native.blockHeight != layout.BlockHeight
                || native.blockDepth != layout.BlockDepth
                || native.bytesPerBlock != layout.BytesPerBlock
                || native.minimumBlocksX != layout.MinimumBlocksX
                || native.minimumBlocksY != layout.MinimumBlocksY
                || native.minimumBlocksZ != layout.MinimumBlocksZ
                || !regionModeValid || !alignmentsValid
                || (flags & GpuUploadSupportFlags.PaddedRowRepack) != 0
                && (flags & GpuUploadSupportFlags.ExplicitPitches) == 0
                || (layout.Features & GpuUploadFormatFeatures.Pvrtc) != 0 && isSupported
                && (flags & GpuUploadSupportFlags.WholeMipOnly) == 0
                || native.reserved0 != 0 || native.reserved1 != 0 || native.reserved2 != 0)
                return false;
            supportInfo = new GpuUploadSupportInfo(isSupported, format, dimension,
                resourceKind, aspect, aspects, layout, flags,
                checked((int)native.stagingRowPitchAlignment),
                checked((int)native.stagingOffsetAlignment));
            return true;
        }

        private static bool TryBuildSupportSet(in GpuUploadTargetInfo target,
            out GpuUploadTargetSupports supports, out GpuUploadError error)
        {
            supports = default;
            if (!TryGetFormatAspects(target.Format, out var aspects))
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            if ((aspects & GpuUploadAspectMask.Color) != 0
                && !TryQuerySupportCore(target.Format, target.Dimension, target.ResourceKind,
                    GpuUploadAspect.Color, target.Width, target.Height, target.Depth,
                    target.Layers, target.MipCount, target.SampleCount,
                    out supports.color, out error))
                return false;
            if ((aspects & GpuUploadAspectMask.Depth) != 0
                && !TryQuerySupportCore(target.Format, target.Dimension, target.ResourceKind,
                    GpuUploadAspect.Depth, target.Width, target.Height, target.Depth,
                    target.Layers, target.MipCount, target.SampleCount,
                    out supports.depth, out error))
                return false;
            if ((aspects & GpuUploadAspectMask.Stencil) != 0
                && !TryQuerySupportCore(target.Format, target.Dimension, target.ResourceKind,
                    GpuUploadAspect.Stencil, target.Width, target.Height, target.Depth,
                    target.Layers, target.MipCount, target.SampleCount,
                    out supports.stencil, out error))
                return false;
            if (supports.SupportedAspects == GpuUploadAspectMask.None)
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            error = GpuUploadError.None;
            return true;
        }

        private static bool TryValidateSupportShape(GpuUploadResourceKind resourceKind,
            GpuUploadAspect aspect, GpuUploadDimension dimension, int width, int height,
            int depth, int layers, int mipCount, int sampleCount, out GpuUploadError error)
        {
            if (!IsResourceAspect(resourceKind, aspect) || width < 0 || height < 0
                || depth < 0 || layers < 0 || mipCount < 0 || sampleCount <= 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            bool unspecified = width == 0 && height == 0 && depth == 0
                               && layers == 0 && mipCount == 0;
            if (unspecified)
            {
                error = GpuUploadError.None;
                return true;
            }
            if (width <= 0 || height <= 0 || depth <= 0 || layers <= 0 || mipCount <= 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            bool validDimension = IsDimensionShape(dimension, depth, layers);
            error = validDimension ? GpuUploadError.None : GpuUploadError.InvalidArgument;
            return validDimension;
        }

        private static bool IsDimensionShape(GpuUploadDimension dimension,
            int depth, int layers) => dimension switch
        {
            GpuUploadDimension.Texture2D => depth == 1 && layers == 1,
            GpuUploadDimension.Texture2DArray => depth == 1,
            GpuUploadDimension.Texture3D => layers == 1,
            GpuUploadDimension.Cube => depth == 1 && layers == 6,
            GpuUploadDimension.CubeArray => depth == 1 && layers % 6 == 0,
            _ => false
        };

        private static bool IsResourceAspect(GpuUploadResourceKind resourceKind,
            GpuUploadAspect aspect) => resourceKind switch
        {
            GpuUploadResourceKind.Texture => aspect == GpuUploadAspect.Color,
            GpuUploadResourceKind.DepthStencil => aspect == GpuUploadAspect.Depth
                                                  || aspect == GpuUploadAspect.Stencil,
            _ => false
        };

        private static GpuUploadAspectMask AspectMask(GpuUploadAspect aspect) => aspect switch
        {
            GpuUploadAspect.Color => GpuUploadAspectMask.Color,
            GpuUploadAspect.Depth => GpuUploadAspectMask.Depth,
            GpuUploadAspect.Stencil => GpuUploadAspectMask.Stencil,
            _ => GpuUploadAspectMask.None
        };

        /// <summary>Maps one concrete Unity storage format to its exact stable upload value.</summary>
        public static bool TryMapFormat(GraphicsFormat format, out GpuUploadFormat result)
        {
            switch (format)
            {
                case GraphicsFormat.R8_SRGB: result = GpuUploadFormat.R8SRgb; return true;
                case GraphicsFormat.R8G8_SRGB: result = GpuUploadFormat.RG8SRgb; return true;
                case GraphicsFormat.R8G8B8_SRGB: result = GpuUploadFormat.RGB8SRgb; return true;
                case GraphicsFormat.R8G8B8A8_SRGB: result = GpuUploadFormat.RGBA8SRgb; return true;
                case GraphicsFormat.R8_UNorm: result = GpuUploadFormat.R8UNorm; return true;
                case GraphicsFormat.R8G8_UNorm: result = GpuUploadFormat.RG8UNorm; return true;
                case GraphicsFormat.R8G8B8_UNorm: result = GpuUploadFormat.RGB8UNorm; return true;
                case GraphicsFormat.R8G8B8A8_UNorm: result = GpuUploadFormat.RGBA8UNorm; return true;
                case GraphicsFormat.R8_SNorm: result = GpuUploadFormat.R8SNorm; return true;
                case GraphicsFormat.R8G8_SNorm: result = GpuUploadFormat.RG8SNorm; return true;
                case GraphicsFormat.R8G8B8_SNorm: result = GpuUploadFormat.RGB8SNorm; return true;
                case GraphicsFormat.R8G8B8A8_SNorm: result = GpuUploadFormat.RGBA8SNorm; return true;
                case GraphicsFormat.R8_UInt: result = GpuUploadFormat.R8UInt; return true;
                case GraphicsFormat.R8G8_UInt: result = GpuUploadFormat.RG8UInt; return true;
                case GraphicsFormat.R8G8B8_UInt: result = GpuUploadFormat.RGB8UInt; return true;
                case GraphicsFormat.R8G8B8A8_UInt: result = GpuUploadFormat.RGBA8UInt; return true;
                case GraphicsFormat.R8_SInt: result = GpuUploadFormat.R8SInt; return true;
                case GraphicsFormat.R8G8_SInt: result = GpuUploadFormat.RG8SInt; return true;
                case GraphicsFormat.R8G8B8_SInt: result = GpuUploadFormat.RGB8SInt; return true;
                case GraphicsFormat.R8G8B8A8_SInt: result = GpuUploadFormat.RGBA8SInt; return true;
                case GraphicsFormat.R16_UNorm: result = GpuUploadFormat.R16UNorm; return true;
                case GraphicsFormat.R16G16_UNorm: result = GpuUploadFormat.RG16UNorm; return true;
                case GraphicsFormat.R16G16B16_UNorm: result = GpuUploadFormat.RGB16UNorm; return true;
                case GraphicsFormat.R16G16B16A16_UNorm: result = GpuUploadFormat.RGBA16UNorm; return true;
                case GraphicsFormat.R16_SNorm: result = GpuUploadFormat.R16SNorm; return true;
                case GraphicsFormat.R16G16_SNorm: result = GpuUploadFormat.RG16SNorm; return true;
                case GraphicsFormat.R16G16B16_SNorm: result = GpuUploadFormat.RGB16SNorm; return true;
                case GraphicsFormat.R16G16B16A16_SNorm: result = GpuUploadFormat.RGBA16SNorm; return true;
                case GraphicsFormat.R16_UInt: result = GpuUploadFormat.R16UInt; return true;
                case GraphicsFormat.R16G16_UInt: result = GpuUploadFormat.RG16UInt; return true;
                case GraphicsFormat.R16G16B16_UInt: result = GpuUploadFormat.RGB16UInt; return true;
                case GraphicsFormat.R16G16B16A16_UInt: result = GpuUploadFormat.RGBA16UInt; return true;
                case GraphicsFormat.R16_SInt: result = GpuUploadFormat.R16SInt; return true;
                case GraphicsFormat.R16G16_SInt: result = GpuUploadFormat.RG16SInt; return true;
                case GraphicsFormat.R16G16B16_SInt: result = GpuUploadFormat.RGB16SInt; return true;
                case GraphicsFormat.R16G16B16A16_SInt: result = GpuUploadFormat.RGBA16SInt; return true;
                case GraphicsFormat.R32_UInt: result = GpuUploadFormat.R32UInt; return true;
                case GraphicsFormat.R32G32_UInt: result = GpuUploadFormat.RG32UInt; return true;
                case GraphicsFormat.R32G32B32_UInt: result = GpuUploadFormat.RGB32UInt; return true;
                case GraphicsFormat.R32G32B32A32_UInt: result = GpuUploadFormat.RGBA32UInt; return true;
                case GraphicsFormat.R32_SInt: result = GpuUploadFormat.R32SInt; return true;
                case GraphicsFormat.R32G32_SInt: result = GpuUploadFormat.RG32SInt; return true;
                case GraphicsFormat.R32G32B32_SInt: result = GpuUploadFormat.RGB32SInt; return true;
                case GraphicsFormat.R32G32B32A32_SInt: result = GpuUploadFormat.RGBA32SInt; return true;
                case GraphicsFormat.R16_SFloat: result = GpuUploadFormat.R16SFloat; return true;
                case GraphicsFormat.R16G16_SFloat: result = GpuUploadFormat.RG16SFloat; return true;
                case GraphicsFormat.R16G16B16_SFloat: result = GpuUploadFormat.RGB16SFloat; return true;
                case GraphicsFormat.R16G16B16A16_SFloat: result = GpuUploadFormat.RGBA16SFloat; return true;
                case GraphicsFormat.R32_SFloat: result = GpuUploadFormat.R32SFloat; return true;
                case GraphicsFormat.R32G32_SFloat: result = GpuUploadFormat.RG32SFloat; return true;
                case GraphicsFormat.R32G32B32_SFloat: result = GpuUploadFormat.RGB32SFloat; return true;
                case GraphicsFormat.R32G32B32A32_SFloat: result = GpuUploadFormat.RGBA32SFloat; return true;
                case GraphicsFormat.B8G8R8_SRGB: result = GpuUploadFormat.BGR8SRgb; return true;
                case GraphicsFormat.B8G8R8A8_SRGB: result = GpuUploadFormat.BGRA8SRgb; return true;
                case GraphicsFormat.B8G8R8_UNorm: result = GpuUploadFormat.BGR8UNorm; return true;
                case GraphicsFormat.B8G8R8A8_UNorm: result = GpuUploadFormat.BGRA8UNorm; return true;
                case GraphicsFormat.B8G8R8_SNorm: result = GpuUploadFormat.BGR8SNorm; return true;
                case GraphicsFormat.B8G8R8A8_SNorm: result = GpuUploadFormat.BGRA8SNorm; return true;
                case GraphicsFormat.B8G8R8_UInt: result = GpuUploadFormat.BGR8UInt; return true;
                case GraphicsFormat.B8G8R8A8_UInt: result = GpuUploadFormat.BGRA8UInt; return true;
                case GraphicsFormat.B8G8R8_SInt: result = GpuUploadFormat.BGR8SInt; return true;
                case GraphicsFormat.B8G8R8A8_SInt: result = GpuUploadFormat.BGRA8SInt; return true;
                case GraphicsFormat.R4G4B4A4_UNormPack16: result = GpuUploadFormat.R4G4B4A4UNormPack16; return true;
                case GraphicsFormat.B4G4R4A4_UNormPack16: result = GpuUploadFormat.B4G4R4A4UNormPack16; return true;
                case GraphicsFormat.R5G6B5_UNormPack16: result = GpuUploadFormat.R5G6B5UNormPack16; return true;
                case GraphicsFormat.B5G6R5_UNormPack16: result = GpuUploadFormat.B5G6R5UNormPack16; return true;
                case GraphicsFormat.R5G5B5A1_UNormPack16: result = GpuUploadFormat.R5G5B5A1UNormPack16; return true;
                case GraphicsFormat.B5G5R5A1_UNormPack16: result = GpuUploadFormat.B5G5R5A1UNormPack16; return true;
                case GraphicsFormat.A1R5G5B5_UNormPack16: result = GpuUploadFormat.A1R5G5B5UNormPack16; return true;
                case GraphicsFormat.E5B9G9R9_UFloatPack32: result = GpuUploadFormat.E5B9G9R9UFloatPack32; return true;
                case GraphicsFormat.B10G11R11_UFloatPack32: result = GpuUploadFormat.B10G11R11UFloatPack32; return true;
                case GraphicsFormat.A2B10G10R10_UNormPack32: result = GpuUploadFormat.A2B10G10R10UNormPack32; return true;
                case GraphicsFormat.A2B10G10R10_UIntPack32: result = GpuUploadFormat.A2B10G10R10UIntPack32; return true;
                case GraphicsFormat.A2B10G10R10_SIntPack32: result = GpuUploadFormat.A2B10G10R10SIntPack32; return true;
                case GraphicsFormat.A2R10G10B10_UNormPack32: result = GpuUploadFormat.A2R10G10B10UNormPack32; return true;
                case GraphicsFormat.A2R10G10B10_UIntPack32: result = GpuUploadFormat.A2R10G10B10UIntPack32; return true;
                case GraphicsFormat.A2R10G10B10_SIntPack32: result = GpuUploadFormat.A2R10G10B10SIntPack32; return true;
#if UNITY_2021_2_OR_NEWER
                case GraphicsFormat.D16_UNorm: result = GpuUploadFormat.D16UNorm; return true;
                case GraphicsFormat.D24_UNorm: result = GpuUploadFormat.D24UNorm; return true;
                case GraphicsFormat.D24_UNorm_S8_UInt: result = GpuUploadFormat.D24UNormS8UInt; return true;
                case GraphicsFormat.D32_SFloat: result = GpuUploadFormat.D32SFloat; return true;
                case GraphicsFormat.D32_SFloat_S8_UInt: result = GpuUploadFormat.D32SFloatS8UInt; return true;
                case GraphicsFormat.S8_UInt: result = GpuUploadFormat.S8UInt; return true;
#if UNITY_2022_2_OR_NEWER
                case GraphicsFormat.D16_UNorm_S8_UInt: result = GpuUploadFormat.D16UNormS8UInt; return true;
#endif
#endif
                case GraphicsFormat.RGBA_DXT1_SRGB: result = GpuUploadFormat.RGBADXT1SRgb; return true;
                case GraphicsFormat.RGBA_DXT1_UNorm: result = GpuUploadFormat.RGBADXT1UNorm; return true;
                case GraphicsFormat.RGBA_DXT3_SRGB: result = GpuUploadFormat.RGBADXT3SRgb; return true;
                case GraphicsFormat.RGBA_DXT3_UNorm: result = GpuUploadFormat.RGBADXT3UNorm; return true;
                case GraphicsFormat.RGBA_DXT5_SRGB: result = GpuUploadFormat.RGBADXT5SRgb; return true;
                case GraphicsFormat.RGBA_DXT5_UNorm: result = GpuUploadFormat.RGBADXT5UNorm; return true;
                case GraphicsFormat.R_BC4_SNorm: result = GpuUploadFormat.RBC4SNorm; return true;
                case GraphicsFormat.R_BC4_UNorm: result = GpuUploadFormat.RBC4UNorm; return true;
                case GraphicsFormat.RG_BC5_SNorm: result = GpuUploadFormat.RGBC5SNorm; return true;
                case GraphicsFormat.RG_BC5_UNorm: result = GpuUploadFormat.RGBC5UNorm; return true;
                case GraphicsFormat.RGB_BC6H_SFloat: result = GpuUploadFormat.RGBBC6HSFloat; return true;
                case GraphicsFormat.RGB_BC6H_UFloat: result = GpuUploadFormat.RGBBC6HUFloat; return true;
                case GraphicsFormat.RGBA_BC7_SRGB: result = GpuUploadFormat.RGBABC7SRgb; return true;
                case GraphicsFormat.RGBA_BC7_UNorm: result = GpuUploadFormat.RGBABC7UNorm; return true;
#pragma warning disable 618
                case GraphicsFormat.RGB_PVRTC_2Bpp_SRGB: result = GpuUploadFormat.RGBPVRTC2BppSRgb; return true;
                case GraphicsFormat.RGB_PVRTC_2Bpp_UNorm: result = GpuUploadFormat.RGBPVRTC2BppUNorm; return true;
                case GraphicsFormat.RGB_PVRTC_4Bpp_SRGB: result = GpuUploadFormat.RGBPVRTC4BppSRgb; return true;
                case GraphicsFormat.RGB_PVRTC_4Bpp_UNorm: result = GpuUploadFormat.RGBPVRTC4BppUNorm; return true;
                case GraphicsFormat.RGBA_PVRTC_2Bpp_SRGB: result = GpuUploadFormat.RGBAPVRTC2BppSRgb; return true;
                case GraphicsFormat.RGBA_PVRTC_2Bpp_UNorm: result = GpuUploadFormat.RGBAPVRTC2BppUNorm; return true;
                case GraphicsFormat.RGBA_PVRTC_4Bpp_SRGB: result = GpuUploadFormat.RGBAPVRTC4BppSRgb; return true;
                case GraphicsFormat.RGBA_PVRTC_4Bpp_UNorm: result = GpuUploadFormat.RGBAPVRTC4BppUNorm; return true;
#pragma warning restore 618
                case GraphicsFormat.RGB_ETC_UNorm: result = GpuUploadFormat.RGBETCUNorm; return true;
                case GraphicsFormat.RGB_ETC2_SRGB: result = GpuUploadFormat.RGBETC2SRgb; return true;
                case GraphicsFormat.RGB_ETC2_UNorm: result = GpuUploadFormat.RGBETC2UNorm; return true;
                case GraphicsFormat.RGB_A1_ETC2_SRGB: result = GpuUploadFormat.RGBA1ETC2SRgb; return true;
                case GraphicsFormat.RGB_A1_ETC2_UNorm: result = GpuUploadFormat.RGBA1ETC2UNorm; return true;
                case GraphicsFormat.RGBA_ETC2_SRGB: result = GpuUploadFormat.RGBAETC2SRgb; return true;
                case GraphicsFormat.RGBA_ETC2_UNorm: result = GpuUploadFormat.RGBAETC2UNorm; return true;
                case GraphicsFormat.R_EAC_SNorm: result = GpuUploadFormat.REACSNorm; return true;
                case GraphicsFormat.R_EAC_UNorm: result = GpuUploadFormat.REACUNorm; return true;
                case GraphicsFormat.RG_EAC_SNorm: result = GpuUploadFormat.RGEACSNorm; return true;
                case GraphicsFormat.RG_EAC_UNorm: result = GpuUploadFormat.RGEACUNorm; return true;
                case GraphicsFormat.RGBA_ASTC4X4_SRGB: result = GpuUploadFormat.RGBAASTC4X4SRgb; return true;
                case GraphicsFormat.RGBA_ASTC4X4_UNorm: result = GpuUploadFormat.RGBAASTC4X4UNorm; return true;
                case GraphicsFormat.RGBA_ASTC5X5_SRGB: result = GpuUploadFormat.RGBAASTC5X5SRgb; return true;
                case GraphicsFormat.RGBA_ASTC5X5_UNorm: result = GpuUploadFormat.RGBAASTC5X5UNorm; return true;
                case GraphicsFormat.RGBA_ASTC6X6_SRGB: result = GpuUploadFormat.RGBAASTC6X6SRgb; return true;
                case GraphicsFormat.RGBA_ASTC6X6_UNorm: result = GpuUploadFormat.RGBAASTC6X6UNorm; return true;
                case GraphicsFormat.RGBA_ASTC8X8_SRGB: result = GpuUploadFormat.RGBAASTC8X8SRgb; return true;
                case GraphicsFormat.RGBA_ASTC8X8_UNorm: result = GpuUploadFormat.RGBAASTC8X8UNorm; return true;
                case GraphicsFormat.RGBA_ASTC10X10_SRGB: result = GpuUploadFormat.RGBAASTC10X10SRgb; return true;
                case GraphicsFormat.RGBA_ASTC10X10_UNorm: result = GpuUploadFormat.RGBAASTC10X10UNorm; return true;
                case GraphicsFormat.RGBA_ASTC12X12_SRGB: result = GpuUploadFormat.RGBAASTC12X12SRgb; return true;
                case GraphicsFormat.RGBA_ASTC12X12_UNorm: result = GpuUploadFormat.RGBAASTC12X12UNorm; return true;
                case GraphicsFormat.RGBA_ASTC4X4_UFloat: result = GpuUploadFormat.RGBAASTC4X4UFloat; return true;
                case GraphicsFormat.RGBA_ASTC5X5_UFloat: result = GpuUploadFormat.RGBAASTC5X5UFloat; return true;
                case GraphicsFormat.RGBA_ASTC6X6_UFloat: result = GpuUploadFormat.RGBAASTC6X6UFloat; return true;
                case GraphicsFormat.RGBA_ASTC8X8_UFloat: result = GpuUploadFormat.RGBAASTC8X8UFloat; return true;
                case GraphicsFormat.RGBA_ASTC10X10_UFloat: result = GpuUploadFormat.RGBAASTC10X10UFloat; return true;
                case GraphicsFormat.RGBA_ASTC12X12_UFloat: result = GpuUploadFormat.RGBAASTC12X12UFloat; return true;
                case GraphicsFormat.YUV2: result = GpuUploadFormat.YUV2; return true;
                case GraphicsFormat.R10G10B10_XRSRGBPack32: result = GpuUploadFormat.R10G10B10XRSRgbPack32; return true;
                case GraphicsFormat.R10G10B10_XRUNormPack32: result = GpuUploadFormat.R10G10B10XRUNormPack32; return true;
                case GraphicsFormat.A10R10G10B10_XRSRGBPack32: result = GpuUploadFormat.A10R10G10B10XRSRgbPack32; return true;
                case GraphicsFormat.A10R10G10B10_XRUNormPack32: result = GpuUploadFormat.A10R10G10B10XRUNormPack32; return true;
                case GraphicsFormat.A2R10G10B10_XRSRGBPack32: result = GpuUploadFormat.A2R10G10B10XRSRgbPack32; return true;
                case GraphicsFormat.A2R10G10B10_XRUNormPack32: result = GpuUploadFormat.A2R10G10B10XRUNormPack32; return true;
                default: result = GpuUploadFormat.Unknown; return false;
            }
        }

        /// <summary>Maps one Unity texture dimension to its stable upload storage organization.</summary>
        public static bool TryMapDimension(TextureDimension dimension,
            out GpuUploadDimension result)
        {
            switch (dimension)
            {
                case TextureDimension.Tex2D: result = GpuUploadDimension.Texture2D; return true;
                case TextureDimension.Tex2DArray: result = GpuUploadDimension.Texture2DArray; return true;
                case TextureDimension.Tex3D: result = GpuUploadDimension.Texture3D; return true;
                case TextureDimension.Cube: result = GpuUploadDimension.Cube; return true;
                case TextureDimension.CubeArray: result = GpuUploadDimension.CubeArray; return true;
                default: result = GpuUploadDimension.Unknown; return false;
            }
        }

        private static bool IsDimensionSupportedByUnity(TextureDimension dimension)
        {
            switch (dimension)
            {
                case TextureDimension.Tex2D:
                case TextureDimension.Cube:
                    return true;
                case TextureDimension.Tex2DArray:
                    return SystemInfo.supports2DArrayTextures;
                case TextureDimension.Tex3D:
                    return SystemInfo.supports3DTextures;
                case TextureDimension.CubeArray:
                    return SystemInfo.supportsCubemapArrayTextures;
                default:
                    return false;
            }
        }

        private static bool TryGetFormatAspects(GpuUploadFormat format,
            out GpuUploadAspectMask aspects)
        {
            if (TryGetFormatLayoutCore(format, GpuUploadAspect.Color, out var layout)
                || TryGetFormatLayoutCore(format, GpuUploadAspect.Depth, out layout)
                || TryGetFormatLayoutCore(format, GpuUploadAspect.Stencil, out layout))
            {
                aspects = layout.Aspects;
                return true;
            }
            aspects = GpuUploadAspectMask.None;
            return false;
        }

        private static bool TryGetFormatLayoutCore(GpuUploadFormat format,
            GpuUploadAspect aspect, out GpuUploadFormatLayout layout)
        {
            uint value = (uint)format;
            if (aspect == GpuUploadAspect.Color && value >= 1 && value <= 52)
            {
                int componentBytes = value <= 20 ? 1
                    : value <= 36 ? 2
                    : value <= 44 ? 4
                    : value <= 48 ? 2 : 4;
                return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                    GpuUploadFormatFeatures.None, 1, 1, 1,
                    (int)(((value - 1) & 3) + 1) * componentBytes, 1, 1, 1,
                    out layout);
            }
            if (aspect == GpuUploadAspect.Color && value >= 53 && value <= 62)
                return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                    GpuUploadFormatFeatures.None, 1, 1, 1,
                    (value & 1) == 0 ? 4 : 3, 1, 1, 1, out layout);
            if (aspect == GpuUploadAspect.Color && value >= 63 && value <= 69)
                return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                    GpuUploadFormatFeatures.None, 1, 1, 1, 2, 1, 1, 1, out layout);
            if (aspect == GpuUploadAspect.Color && value >= 70 && value <= 77)
                return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                    GpuUploadFormatFeatures.None, 1, 1, 1, 4, 1, 1, 1, out layout);

            switch (format)
            {
                case GpuUploadFormat.D16UNorm when aspect == GpuUploadAspect.Depth:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Depth,
                        GpuUploadFormatFeatures.Depth, 1, 1, 1, 2, 1, 1, 1, out layout);
                case GpuUploadFormat.D24UNorm when aspect == GpuUploadAspect.Depth:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Depth,
                        GpuUploadFormatFeatures.Depth, 1, 1, 1, 4, 1, 1, 1, out layout);
                case GpuUploadFormat.D32SFloat when aspect == GpuUploadAspect.Depth:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Depth,
                        GpuUploadFormatFeatures.Depth, 1, 1, 1, 4, 1, 1, 1, out layout);
                case GpuUploadFormat.S8UInt when aspect == GpuUploadAspect.Stencil:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Stencil,
                        GpuUploadFormatFeatures.Stencil, 1, 1, 1, 1, 1, 1, 1, out layout);
                case GpuUploadFormat.D24UNormS8UInt:
                case GpuUploadFormat.D32SFloatS8UInt:
                    if (aspect == GpuUploadAspect.Depth)
                        return SetLayout(format, aspect,
                            GpuUploadAspectMask.Depth | GpuUploadAspectMask.Stencil,
                            GpuUploadFormatFeatures.Depth | GpuUploadFormatFeatures.Stencil,
                            1, 1, 1, 4, 1, 1, 1, out layout);
                    if (aspect == GpuUploadAspect.Stencil)
                        return SetLayout(format, aspect,
                            GpuUploadAspectMask.Depth | GpuUploadAspectMask.Stencil,
                            GpuUploadFormatFeatures.Depth | GpuUploadFormatFeatures.Stencil,
                            1, 1, 1, 1, 1, 1, 1, out layout);
                    break;
                case GpuUploadFormat.D16UNormS8UInt:
                    if (aspect == GpuUploadAspect.Depth)
                        return SetLayout(format, aspect,
                            GpuUploadAspectMask.Depth | GpuUploadAspectMask.Stencil,
                            GpuUploadFormatFeatures.Depth | GpuUploadFormatFeatures.Stencil,
                            1, 1, 1, 2, 1, 1, 1, out layout);
                    if (aspect == GpuUploadAspect.Stencil)
                        return SetLayout(format, aspect,
                            GpuUploadAspectMask.Depth | GpuUploadAspectMask.Stencil,
                            GpuUploadFormatFeatures.Depth | GpuUploadFormatFeatures.Stencil,
                            1, 1, 1, 1, 1, 1, 1, out layout);
                    break;
                case GpuUploadFormat.RGBADXT1SRgb:
                case GpuUploadFormat.RGBADXT1UNorm:
                case GpuUploadFormat.RBC4SNorm:
                case GpuUploadFormat.RBC4UNorm:
                case GpuUploadFormat.RGBETCUNorm:
                case GpuUploadFormat.RGBETC2SRgb:
                case GpuUploadFormat.RGBETC2UNorm:
                case GpuUploadFormat.RGBA1ETC2SRgb:
                case GpuUploadFormat.RGBA1ETC2UNorm:
                case GpuUploadFormat.REACSNorm:
                case GpuUploadFormat.REACUNorm:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 4, 4, 8, out layout);
                    break;
                case GpuUploadFormat.RGBADXT3SRgb:
                case GpuUploadFormat.RGBADXT3UNorm:
                case GpuUploadFormat.RGBADXT5SRgb:
                case GpuUploadFormat.RGBADXT5UNorm:
                case GpuUploadFormat.RGBC5SNorm:
                case GpuUploadFormat.RGBC5UNorm:
                case GpuUploadFormat.RGBBC6HSFloat:
                case GpuUploadFormat.RGBBC6HUFloat:
                case GpuUploadFormat.RGBABC7SRgb:
                case GpuUploadFormat.RGBABC7UNorm:
                case GpuUploadFormat.RGBAETC2SRgb:
                case GpuUploadFormat.RGBAETC2UNorm:
                case GpuUploadFormat.RGEACSNorm:
                case GpuUploadFormat.RGEACUNorm:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 4, 4, 16, out layout);
                    break;
                case GpuUploadFormat.RGBPVRTC2BppSRgb:
                case GpuUploadFormat.RGBPVRTC2BppUNorm:
                case GpuUploadFormat.RGBAPVRTC2BppSRgb:
                case GpuUploadFormat.RGBAPVRTC2BppUNorm:
                    if (aspect == GpuUploadAspect.Color)
                        return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                            GpuUploadFormatFeatures.Compressed | GpuUploadFormatFeatures.Pvrtc,
                            8, 4, 1, 8, 2, 2, 1, out layout);
                    break;
                case GpuUploadFormat.RGBPVRTC4BppSRgb:
                case GpuUploadFormat.RGBPVRTC4BppUNorm:
                case GpuUploadFormat.RGBAPVRTC4BppSRgb:
                case GpuUploadFormat.RGBAPVRTC4BppUNorm:
                    if (aspect == GpuUploadAspect.Color)
                        return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                            GpuUploadFormatFeatures.Compressed | GpuUploadFormatFeatures.Pvrtc,
                            4, 4, 1, 8, 2, 2, 1, out layout);
                    break;
                case GpuUploadFormat.RGBAASTC4X4SRgb:
                case GpuUploadFormat.RGBAASTC4X4UNorm:
                case GpuUploadFormat.RGBAASTC4X4UFloat:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 4, 4, 16, out layout);
                    break;
                case GpuUploadFormat.RGBAASTC5X5SRgb:
                case GpuUploadFormat.RGBAASTC5X5UNorm:
                case GpuUploadFormat.RGBAASTC5X5UFloat:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 5, 5, 16, out layout);
                    break;
                case GpuUploadFormat.RGBAASTC6X6SRgb:
                case GpuUploadFormat.RGBAASTC6X6UNorm:
                case GpuUploadFormat.RGBAASTC6X6UFloat:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 6, 6, 16, out layout);
                    break;
                case GpuUploadFormat.RGBAASTC8X8SRgb:
                case GpuUploadFormat.RGBAASTC8X8UNorm:
                case GpuUploadFormat.RGBAASTC8X8UFloat:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 8, 8, 16, out layout);
                    break;
                case GpuUploadFormat.RGBAASTC10X10SRgb:
                case GpuUploadFormat.RGBAASTC10X10UNorm:
                case GpuUploadFormat.RGBAASTC10X10UFloat:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 10, 10, 16, out layout);
                    break;
                case GpuUploadFormat.RGBAASTC12X12SRgb:
                case GpuUploadFormat.RGBAASTC12X12UNorm:
                case GpuUploadFormat.RGBAASTC12X12UFloat:
                    if (aspect == GpuUploadAspect.Color)
                        return SetCompressedLayout(format, 12, 12, 16, out layout);
                    break;
                case GpuUploadFormat.YUV2 when aspect == GpuUploadAspect.Color:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                        GpuUploadFormatFeatures.None, 2, 1, 1, 4, 1, 1, 1, out layout);
                case GpuUploadFormat.R10G10B10XRSRgbPack32 when aspect == GpuUploadAspect.Color:
                case GpuUploadFormat.R10G10B10XRUNormPack32 when aspect == GpuUploadAspect.Color:
                case GpuUploadFormat.A2R10G10B10XRSRgbPack32 when aspect == GpuUploadAspect.Color:
                case GpuUploadFormat.A2R10G10B10XRUNormPack32 when aspect == GpuUploadAspect.Color:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                        GpuUploadFormatFeatures.None, 1, 1, 1, 4, 1, 1, 1, out layout);
                case GpuUploadFormat.A10R10G10B10XRSRgbPack32 when aspect == GpuUploadAspect.Color:
                case GpuUploadFormat.A10R10G10B10XRUNormPack32 when aspect == GpuUploadAspect.Color:
                    return SetLayout(format, aspect, GpuUploadAspectMask.Color,
                        GpuUploadFormatFeatures.None, 1, 1, 1, 8, 1, 1, 1, out layout);
            }
            layout = default;
            return false;
        }

        private static bool SetCompressedLayout(GpuUploadFormat format, int blockWidth,
            int blockHeight, int bytesPerBlock, out GpuUploadFormatLayout layout) =>
            SetLayout(format, GpuUploadAspect.Color, GpuUploadAspectMask.Color,
                GpuUploadFormatFeatures.Compressed, blockWidth, blockHeight, 1,
                bytesPerBlock, 1, 1, 1, out layout);

        private static bool SetLayout(GpuUploadFormat format, GpuUploadAspect aspect,
            GpuUploadAspectMask aspects, GpuUploadFormatFeatures features, int blockWidth,
            int blockHeight, int blockDepth, int bytesPerBlock, int minimumBlocksX,
            int minimumBlocksY, int minimumBlocksZ, out GpuUploadFormatLayout layout)
        {
            layout = new GpuUploadFormatLayout(format, aspect, aspects, features,
                blockWidth, blockHeight, blockDepth, bytesPerBlock, minimumBlocksX,
                minimumBlocksY, minimumBlocksZ);
            return true;
        }

        private static bool TryCalculateRegionLayout(in GpuUploadResourceDescription target,
            in GpuUploadSupportInfo supportInfo, in GpuUploadRegion region,
            out GpuUploadRegionLayout layout, out GpuUploadError error)
        {
            layout = default;
            if (target.Width <= 0 || target.Height <= 0 || target.Depth <= 0
                || target.Layers <= 0 || target.MipCount <= 0 || target.SampleCount <= 0
                || !IsDimensionShape(target.Dimension, target.Depth, target.Layers))
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            if (!supportInfo.Supported || supportInfo.Format != target.Format
                || supportInfo.Dimension != target.Dimension
                || supportInfo.ResourceKind != target.ResourceKind
                || supportInfo.Aspect != region.Aspect)
            {
                error = GpuUploadError.UnsupportedFormat;
                return false;
            }
            if (target.SampleCount != 1)
            {
                error = GpuUploadError.UnsupportedSampleCount;
                return false;
            }
            if (region.MipLevel < 0 || region.MipLevel >= target.MipCount
                || region.DestinationX < 0 || region.DestinationY < 0 || region.DestinationZ < 0
                || region.Width <= 0 || region.Height <= 0 || region.Depth <= 0
                || region.BaseLayer < 0 || region.LayerCount <= 0 || region.SlotOffset < 0
                || region.SourceRowPitch < 0 || region.SourceImagePitch < 0
                || region.SourceLayerPitch < 0)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            int mipWidth = MipSize(target.Width, region.MipLevel);
            int mipHeight = MipSize(target.Height, region.MipLevel);
            int mipDepth = MipSize(target.Depth, region.MipLevel);
            if ((long)region.DestinationX + region.Width > mipWidth
                || (long)region.DestinationY + region.Height > mipHeight)
            {
                error = GpuUploadError.InvalidArgument;
                return false;
            }
            switch (target.Dimension)
            {
                case GpuUploadDimension.Texture2D:
                    if (region.DestinationZ != 0 || region.Depth != 1 || region.BaseLayer != 0
                        || region.LayerCount != 1)
                    {
                        error = GpuUploadError.UnsupportedDimension;
                        return false;
                    }
                    break;
                case GpuUploadDimension.Texture2DArray:
                case GpuUploadDimension.Cube:
                case GpuUploadDimension.CubeArray:
                    if (region.DestinationZ != 0 || region.Depth != 1
                        || (long)region.BaseLayer + region.LayerCount > target.Layers)
                    {
                        error = GpuUploadError.InvalidArgument;
                        return false;
                    }
                    break;
                case GpuUploadDimension.Texture3D:
                    if (region.BaseLayer != 0 || region.LayerCount != 1
                        || (long)region.DestinationZ + region.Depth > mipDepth)
                    {
                        error = GpuUploadError.InvalidArgument;
                        return false;
                    }
                    break;
                default:
                    error = GpuUploadError.UnsupportedDimension;
                    return false;
            }
            var format = supportInfo.Layout;
            bool edgeRemainder = (supportInfo.Flags & GpuUploadSupportFlags.EdgeRemainder) != 0;
            if (!IsBlockRangeValid(region.DestinationX, region.Width, mipWidth,
                    format.BlockWidth, edgeRemainder)
                || !IsBlockRangeValid(region.DestinationY, region.Height, mipHeight,
                    format.BlockHeight, edgeRemainder)
                || !IsBlockRangeValid(region.DestinationZ, region.Depth, mipDepth,
                    format.BlockDepth, edgeRemainder))
            {
                error = GpuUploadError.InvalidLayout;
                return false;
            }
            bool wholeMip = region.DestinationX == 0 && region.Width == mipWidth
                            && region.DestinationY == 0 && region.Height == mipHeight
                            && region.DestinationZ == 0 && region.Depth == mipDepth;
            if (((supportInfo.Flags & GpuUploadSupportFlags.WholeMipOnly) != 0
                 || (format.Features & GpuUploadFormatFeatures.Pvrtc) != 0) && !wholeMip)
            {
                error = GpuUploadError.InvalidLayout;
                return false;
            }
            if ((supportInfo.Flags & GpuUploadSupportFlags.PowerOfTwo) != 0
                && (!IsPowerOfTwo((uint)target.Width) || !IsPowerOfTwo((uint)target.Height)))
            {
                error = GpuUploadError.UnsupportedTexture;
                return false;
            }
            if ((supportInfo.Flags & GpuUploadSupportFlags.TopMipBlockMultiple) != 0
                && (target.Width % format.BlockWidth != 0
                    || target.Height % format.BlockHeight != 0
                    || target.Depth % format.BlockDepth != 0))
            {
                error = GpuUploadError.UnsupportedTexture;
                return false;
            }
            try
            {
                int blockColumns = Math.Max(format.MinimumBlocksX,
                    checked((int)(((long)region.Width + format.BlockWidth - 1)
                                  / format.BlockWidth)));
                int blockRows = Math.Max(format.MinimumBlocksY,
                    checked((int)(((long)region.Height + format.BlockHeight - 1)
                                  / format.BlockHeight)));
                int blockSlices = Math.Max(format.MinimumBlocksZ,
                    checked((int)(((long)region.Depth + format.BlockDepth - 1)
                                  / format.BlockDepth)));
                ulong rowBytes = checked((ulong)blockColumns * (uint)format.BytesPerBlock);
                ulong sourceRowPitch = region.SourceRowPitch == 0
                    ? rowBytes
                    : (uint)region.SourceRowPitch;
                ulong minimumImagePitch = checked(sourceRowPitch * (uint)blockRows);
                ulong sourceImagePitch = region.SourceImagePitch == 0
                    ? minimumImagePitch
                    : (uint)region.SourceImagePitch;
                ulong minimumLayerPitch = checked(sourceImagePitch * (uint)blockSlices);
                ulong sourceLayerPitch = region.SourceLayerPitch == 0
                    ? minimumLayerPitch
                    : (uint)region.SourceLayerPitch;
                if (sourceRowPitch < rowBytes || sourceImagePitch < minimumImagePitch
                    || sourceLayerPitch < minimumLayerPitch)
                {
                    error = GpuUploadError.InvalidLayout;
                    return false;
                }
                bool padded = sourceRowPitch != rowBytes
                              || sourceImagePitch != minimumImagePitch
                              || sourceLayerPitch != minimumLayerPitch;
                if (padded && (supportInfo.Flags & GpuUploadSupportFlags.ExplicitPitches) == 0)
                {
                    error = GpuUploadError.UnsupportedFeature;
                    return false;
                }
                ulong sourceSpan = checked((ulong)(region.LayerCount - 1) * sourceLayerPitch
                                           + (ulong)(blockSlices - 1) * sourceImagePitch
                                           + (ulong)(blockRows - 1) * sourceRowPitch + rowBytes);
                ulong payloadBytes = checked(rowBytes * (uint)blockRows * (uint)blockSlices
                                             * (uint)region.LayerCount);
                if (!TryAlignUp(rowBytes, (uint)supportInfo.StagingRowPitchAlignment,
                        out ulong stagingRowPitch))
                {
                    error = GpuUploadError.InvalidLayout;
                    return false;
                }
                ulong stagingImagePitch = checked(stagingRowPitch * (uint)blockRows);
                ulong stagingLayerBytes = checked(stagingImagePitch * (uint)blockSlices);
                if (!TryAlignUp(stagingLayerBytes, (uint)supportInfo.StagingOffsetAlignment,
                        out ulong stagingLayerPitch))
                {
                    error = GpuUploadError.InvalidLayout;
                    return false;
                }
                ulong stagingBytes = checked((ulong)(region.LayerCount - 1) * stagingLayerPitch
                                             + stagingLayerBytes);
                layout = new GpuUploadRegionLayout(blockColumns, blockRows, blockSlices,
                    rowBytes, sourceRowPitch, sourceImagePitch, sourceLayerPitch,
                    sourceSpan, payloadBytes, stagingRowPitch, stagingImagePitch,
                    stagingLayerPitch, stagingBytes, padded
                        && supportInfo.RequiresPaddedRowRepack);
                error = GpuUploadError.None;
                return true;
            }
            catch (OverflowException)
            {
                error = GpuUploadError.InvalidLayout;
                return false;
            }
        }

        private static bool IsBlockRangeValid(int offset, int extent, int mipExtent,
            int blockExtent, bool edgeRemainder)
        {
            if (offset % blockExtent != 0) return false;
            if (extent % blockExtent == 0) return true;
            return edgeRemainder
                ? (long)offset + extent == mipExtent
                : offset == 0 && extent == mipExtent;
        }

        private static bool IsPowerOfTwo(uint value) => value != 0 && (value & (value - 1)) == 0;
    }
}
