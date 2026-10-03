using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;

namespace LightSide
{
    internal static partial class GpuUploadAbi
    {
        internal const uint Magic = 0x3647534C;
        internal const ushort Major = 6;
        internal const ushort Minor = 0;
        internal const uint ContractFingerprint = 0x4336534C;
        internal const int UploadEvent = 0;
        internal const int BoundaryEvent = 1;
        internal const int RetireEvent = 2;
        internal const int PollEvent = 3;
        internal const uint EventCount = 4;
        internal const int SlotBytes = 1048576;
        internal const int MaxResidentSlots = 3;
        internal const int MaxRegionsPerSubmission = 1024;
        internal const int HistoryCapacity = 258;
        internal const int MaxSubmissionStagingBytes = 268435456;

        internal enum Format : uint
        {
            Unknown = 0,
            R8SRgb = 1,
            RG8SRgb = 2,
            RGB8SRgb = 3,
            RGBA8SRgb = 4,
            R8UNorm = 5,
            RG8UNorm = 6,
            RGB8UNorm = 7,
            RGBA8UNorm = 8,
            R8SNorm = 9,
            RG8SNorm = 10,
            RGB8SNorm = 11,
            RGBA8SNorm = 12,
            R8UInt = 13,
            RG8UInt = 14,
            RGB8UInt = 15,
            RGBA8UInt = 16,
            R8SInt = 17,
            RG8SInt = 18,
            RGB8SInt = 19,
            RGBA8SInt = 20,
            R16UNorm = 21,
            RG16UNorm = 22,
            RGB16UNorm = 23,
            RGBA16UNorm = 24,
            R16SNorm = 25,
            RG16SNorm = 26,
            RGB16SNorm = 27,
            RGBA16SNorm = 28,
            R16UInt = 29,
            RG16UInt = 30,
            RGB16UInt = 31,
            RGBA16UInt = 32,
            R16SInt = 33,
            RG16SInt = 34,
            RGB16SInt = 35,
            RGBA16SInt = 36,
            R32UInt = 37,
            RG32UInt = 38,
            RGB32UInt = 39,
            RGBA32UInt = 40,
            R32SInt = 41,
            RG32SInt = 42,
            RGB32SInt = 43,
            RGBA32SInt = 44,
            R16SFloat = 45,
            RG16SFloat = 46,
            RGB16SFloat = 47,
            RGBA16SFloat = 48,
            R32SFloat = 49,
            RG32SFloat = 50,
            RGB32SFloat = 51,
            RGBA32SFloat = 52,
            BGR8SRgb = 53,
            BGRA8SRgb = 54,
            BGR8UNorm = 55,
            BGRA8UNorm = 56,
            BGR8SNorm = 57,
            BGRA8SNorm = 58,
            BGR8UInt = 59,
            BGRA8UInt = 60,
            BGR8SInt = 61,
            BGRA8SInt = 62,
            R4G4B4A4UNormPack16 = 63,
            B4G4R4A4UNormPack16 = 64,
            R5G6B5UNormPack16 = 65,
            B5G6R5UNormPack16 = 66,
            R5G5B5A1UNormPack16 = 67,
            B5G5R5A1UNormPack16 = 68,
            A1R5G5B5UNormPack16 = 69,
            E5B9G9R9UFloatPack32 = 70,
            B10G11R11UFloatPack32 = 71,
            A2B10G10R10UNormPack32 = 72,
            A2B10G10R10UIntPack32 = 73,
            A2B10G10R10SIntPack32 = 74,
            A2R10G10B10UNormPack32 = 75,
            A2R10G10B10UIntPack32 = 76,
            A2R10G10B10SIntPack32 = 77,
            D16UNorm = 78,
            D24UNorm = 79,
            D24UNormS8UInt = 80,
            D32SFloat = 81,
            D32SFloatS8UInt = 82,
            S8UInt = 83,
            RGBADXT1SRgb = 84,
            RGBADXT1UNorm = 85,
            RGBADXT3SRgb = 86,
            RGBADXT3UNorm = 87,
            RGBADXT5SRgb = 88,
            RGBADXT5UNorm = 89,
            RBC4SNorm = 90,
            RBC4UNorm = 91,
            RGBC5SNorm = 92,
            RGBC5UNorm = 93,
            RGBBC6HSFloat = 94,
            RGBBC6HUFloat = 95,
            RGBABC7SRgb = 96,
            RGBABC7UNorm = 97,
            RGBPVRTC2BppSRgb = 98,
            RGBPVRTC2BppUNorm = 99,
            RGBPVRTC4BppSRgb = 100,
            RGBPVRTC4BppUNorm = 101,
            RGBAPVRTC2BppSRgb = 102,
            RGBAPVRTC2BppUNorm = 103,
            RGBAPVRTC4BppSRgb = 104,
            RGBAPVRTC4BppUNorm = 105,
            RGBETCUNorm = 106,
            RGBETC2SRgb = 107,
            RGBETC2UNorm = 108,
            RGBA1ETC2SRgb = 109,
            RGBA1ETC2UNorm = 110,
            RGBAETC2SRgb = 111,
            RGBAETC2UNorm = 112,
            REACSNorm = 113,
            REACUNorm = 114,
            RGEACSNorm = 115,
            RGEACUNorm = 116,
            RGBAASTC4X4SRgb = 117,
            RGBAASTC4X4UNorm = 118,
            RGBAASTC5X5SRgb = 119,
            RGBAASTC5X5UNorm = 120,
            RGBAASTC6X6SRgb = 121,
            RGBAASTC6X6UNorm = 122,
            RGBAASTC8X8SRgb = 123,
            RGBAASTC8X8UNorm = 124,
            RGBAASTC10X10SRgb = 125,
            RGBAASTC10X10UNorm = 126,
            RGBAASTC12X12SRgb = 127,
            RGBAASTC12X12UNorm = 128,
            RGBAASTC4X4UFloat = 129,
            RGBAASTC5X5UFloat = 130,
            RGBAASTC6X6UFloat = 131,
            RGBAASTC8X8UFloat = 132,
            RGBAASTC10X10UFloat = 133,
            RGBAASTC12X12UFloat = 134,
            YUV2 = 135,
            R10G10B10XRSRgbPack32 = 136,
            R10G10B10XRUNormPack32 = 137,
            A10R10G10B10XRSRgbPack32 = 138,
            A10R10G10B10XRUNormPack32 = 139,
            A2R10G10B10XRSRgbPack32 = 140,
            A2R10G10B10XRUNormPack32 = 141,
            D16UNormS8UInt = 142,
            Count = 143,
        }

        internal enum Dimension : uint
        {
            Unknown = 0,
            Texture2D = 1,
            Texture2DArray = 2,
            Texture3D = 3,
            Cube = 4,
            CubeArray = 5,
            Count = 6,
        }

        internal enum Aspect : uint
        {
            Color = 0,
            Depth = 1,
            Stencil = 2,
        }

        internal enum ResourceKind : uint
        {
            Texture = 0,
            DepthStencil = 1,
        }

        [Flags]
        internal enum AspectMask : uint
        {
            None = 0,
            Color = 1,
            Depth = 2,
            Stencil = 4,
        }

        [Flags]
        internal enum FormatFeatures : uint
        {
            None = 0,
            Compressed = 1,
            Depth = 2,
            Stencil = 4,
            Pvrtc = 8,
        }

        [Flags]
        internal enum SupportFlags : uint
        {
            None = 0,
            Regional = 1,
            WholeMipOnly = 2,
            EdgeRemainder = 4,
            ExplicitPitches = 8,
            PowerOfTwo = 16,
            TopMipBlockMultiple = 32,
            PaddedRowRepack = 64,
        }

        internal enum Error : int
        {
            None = 0,
            NotInitialized = 1,
            AbiMismatch = 2,
            UnsupportedBackend = 3,
            UnsupportedFeature = 4,
            InvalidArgument = 5,
            InvalidLayout = 6,
            UnsupportedTexture = 7,
            UnsupportedDimension = 8,
            UnsupportedFormat = 9,
            UnsupportedSampleCount = 10,
            TargetNotFound = 11,
            TargetStale = 12,
            TargetBusy = 13,
            TargetClosing = 14,
            SourceOutOfRange = 15,
            Backpressure = 16,
            DeviceLost = 17,
            BackendFailed = 18,
            ContextLost = 19,
            StateRestoreFailed = 20,
            OutOfMemory = 21,
            SubmissionNotFound = 22,
            SubmissionClosing = 23,
            InternalError = 24,
            SequenceNotFound = 25,
            SequenceClosing = 26,
            SequenceOrder = 27,
            SlotNotFound = 28,
            SlotBusy = 29,
        }

        internal enum AdmissionStage : uint
        {
            None = 0,
            Layout = 1,
            Slot = 2,
            Target = 3,
            Region = 4,
            Marker = 5,
            Overlap = 6,
            Backend = 7,
        }

        internal enum SubmissionState : uint
        {
            Invalid = 0,
            Pending = 1,
            Encoded = 2,
            Rejected = 3,
            BackendFailed = 4,
            DeviceLost = 5,
            Cancelled = 6,
            Retired = 7,
        }

        internal enum GpuState : uint
        {
            Unsupported = 0,
            Pending = 1,
            Complete = 2,
            Failed = 3,
        }

        internal enum ContentState : uint
        {
            Unchanged = 0,
            Changed = 1,
            MayHaveChanged = 2,
        }

        internal enum SequenceState : uint
        {
            Invalid = 0,
            Open = 1,
            Sealed = 2,
            Failed = 3,
            Aborted = 4,
            Complete = 5,
        }

        internal enum PublicationState : uint
        {
            None = 0,
            Pending = 1,
            Suppressed = 2,
            Published = 3,
            Unknown = 4,
        }

        [Flags]
        internal enum SequenceFlags : uint
        {
            None = 0,
            PublicationMarker = 1,
        }

        [Flags]
        internal enum SequenceStatusFlags : uint
        {
            CloseRequested = 1,
            TargetLeasesReleased = 2,
        }

        internal enum TargetState : uint
        {
            Invalid = 0,
            Active = 1,
            Closing = 2,
            Retired = 3,
            Stale = 4,
        }

        [Flags]
        internal enum StatusFlags : uint
        {
            SourceConsumed = 1,
            CloseRequested = 2,
            RetireObserved = 4,
        }

        [Flags]
        internal enum TargetFlags : uint
        {
            CpuReadable = 1,
            Memoryless = 2,
            Streaming = 4,
            DynamicSize = 8,
            DepthStencil = 16,
        }

        [Flags]
        internal enum DeviceFlags : uint
        {
            CommandBuffer = 1,
            Immediate = 2,
            SubmissionBoundary = 4,
            MultiTarget = 16,
            ExplicitMips = 32,
            GpuCompletion = 64,
            TargetRefresh = 128,
            CompletionPoll = 256,
        }

        [Flags]
        internal enum BatchFlags : uint
        {
            ValidateNonOverlapping = 0,
            AssumeNonOverlapping = 1,
            OrderedOverlaps = 2,
            ObserveSharedGlErrors = 4,
        }

        [Flags]
        internal enum RegionFlags : uint
        {
            PublicationMarker = 1,
        }

        internal const uint FormatCount = (uint)Format.Count;
        internal const uint DimensionCount = (uint)Dimension.Count;

        internal const int BatchSize = 96;
        internal const int BatchMagicOffset = 0;
        internal const int BatchAbiMajorOffset = 4;
        internal const int BatchAbiMinorOffset = 6;
        internal const int BatchTotalBytesOffset = 8;
        internal const int BatchFlagsOffset = 12;
        internal const int BatchSerialOffset = 16;
        internal const int BatchGraphicsDeviceEpochOffset = 24;
        internal const int BatchSequenceSerialOffset = 32;
        internal const int BatchSequenceOrdinalOffset = 40;
        internal const int BatchSlotIdOffset = 44;
        internal const int BatchSlotGenerationOffset = 48;
        internal const int BatchWrittenBytesOffset = 52;
        internal const int BatchTargetTableOffsetOffset = 56;
        internal const int BatchTargetCountOffset = 60;
        internal const int BatchRegionTableOffsetOffset = 64;
        internal const int BatchRegionCountOffset = 68;
        internal const int BatchResultCodeOffset = 72;
        internal const int BatchFailedRegionOffset = 76;
        internal const int BatchAdmissionStageOffset = 80;
        internal const int BatchBackendDetailOffset = 84;
        internal const int BatchStateOffset = 88;
        internal const int BatchReserved0Offset = 92;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct Batch
        {
            internal uint magic;
            internal ushort abiMajor;
            internal ushort abiMinor;
            internal uint totalBytes;
            internal uint flags;
            internal ulong serial;
            internal ulong graphicsDeviceEpoch;
            internal ulong sequenceSerial;
            internal uint sequenceOrdinal;
            internal uint slotId;
            internal uint slotGeneration;
            internal uint writtenBytes;
            internal uint targetTableOffset;
            internal uint targetCount;
            internal uint regionTableOffset;
            internal uint regionCount;
            internal int resultCode;
            internal int failedRegion;
            internal uint admissionStage;
            internal uint backendDetail;
            internal uint state;
            internal uint reserved0;
        }

        internal const int SequenceDescSize = 24;
        internal const int SequenceDescStructSizeOffset = 0;
        internal const int SequenceDescFlagsOffset = 4;
        internal const int SequenceDescGraphicsDeviceEpochOffset = 8;
        internal const int SequenceDescReserved0Offset = 16;
        internal const int SequenceDescReserved1Offset = 20;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct SequenceDesc
        {
            internal uint structSize;
            internal uint flags;
            internal ulong graphicsDeviceEpoch;
            internal uint reserved0;
            internal uint reserved1;
        }

        internal const int TargetSize = 48;
        internal const int TargetTokenOffset = 0;
        internal const int TargetGenerationOffset = 8;
        internal const int TargetWidthOffset = 12;
        internal const int TargetHeightOffset = 16;
        internal const int TargetDepthOffset = 20;
        internal const int TargetLayersOffset = 24;
        internal const int TargetMipCountOffset = 28;
        internal const int TargetFormatOffset = 32;
        internal const int TargetDimensionOffset = 36;
        internal const int TargetSampleCountOffset = 40;
        internal const int TargetFlagsOffset = 44;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct Target
        {
            internal ulong token;
            internal uint generation;
            internal uint width;
            internal uint height;
            internal uint depth;
            internal uint layers;
            internal uint mipCount;
            internal uint format;
            internal uint dimension;
            internal uint sampleCount;
            internal uint flags;
        }

        internal const int RegionSize = 64;
        internal const int RegionTargetIndexOffset = 0;
        internal const int RegionMipLevelOffset = 4;
        internal const int RegionAspectOffset = 8;
        internal const int RegionDestinationXOffset = 12;
        internal const int RegionDestinationYOffset = 16;
        internal const int RegionDestinationZOffset = 20;
        internal const int RegionWidthOffset = 24;
        internal const int RegionHeightOffset = 28;
        internal const int RegionDepthOffset = 32;
        internal const int RegionBaseLayerOffset = 36;
        internal const int RegionLayerCountOffset = 40;
        internal const int RegionSlotOffsetOffset = 44;
        internal const int RegionSourceRowPitchOffset = 48;
        internal const int RegionSourceImagePitchOffset = 52;
        internal const int RegionSourceLayerPitchOffset = 56;
        internal const int RegionFlagsOffset = 60;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct Region
        {
            internal uint targetIndex;
            internal uint mipLevel;
            internal uint aspect;
            internal int destinationX;
            internal int destinationY;
            internal int destinationZ;
            internal uint width;
            internal uint height;
            internal uint depth;
            internal uint baseLayer;
            internal uint layerCount;
            internal uint slotOffset;
            internal uint sourceRowPitch;
            internal uint sourceImagePitch;
            internal uint sourceLayerPitch;
            internal uint flags;
        }

        internal const int DeviceInfoSize = 64;
        internal const int DeviceInfoStructSizeOffset = 0;
        internal const int DeviceInfoAbiMajorOffset = 4;
        internal const int DeviceInfoAbiMinorOffset = 6;
        internal const int DeviceInfoRendererOffset = 8;
        internal const int DeviceInfoFlagsOffset = 12;
        internal const int DeviceInfoEventBaseOffset = 16;
        internal const int DeviceInfoEventCountOffset = 20;
        internal const int DeviceInfoGraphicsDeviceEpochOffset = 24;
        internal const int DeviceInfoFormatCountOffset = 32;
        internal const int DeviceInfoDimensionCountOffset = 36;
        internal const int DeviceInfoMaxStagingBytesOffset = 40;
        internal const int DeviceInfoMaxConcurrentSubmissionsOffset = 48;
        internal const int DeviceInfoContractFingerprintOffset = 52;
        internal const int DeviceInfoSlotBytesOffset = 56;
        internal const int DeviceInfoMaxRegionsPerSubmissionOffset = 60;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct DeviceInfo
        {
            internal uint structSize;
            internal ushort abiMajor;
            internal ushort abiMinor;
            internal uint renderer;
            internal uint flags;
            internal int eventBase;
            internal uint eventCount;
            internal ulong graphicsDeviceEpoch;
            internal uint formatCount;
            internal uint dimensionCount;
            internal ulong maxStagingBytes;
            internal uint maxConcurrentSubmissions;
            internal uint contractFingerprint;
            internal uint slotBytes;
            internal uint maxRegionsPerSubmission;
        }

        internal const int TargetRegistrationSize = 64;
        internal const int TargetRegistrationStructSizeOffset = 0;
        internal const int TargetRegistrationFlagsOffset = 4;
        internal const int TargetRegistrationGraphicsDeviceEpochOffset = 8;
        internal const int TargetRegistrationNativeResourceOffset = 16;
        internal const int TargetRegistrationWidthOffset = 24;
        internal const int TargetRegistrationHeightOffset = 28;
        internal const int TargetRegistrationDepthOffset = 32;
        internal const int TargetRegistrationLayersOffset = 36;
        internal const int TargetRegistrationMipCountOffset = 40;
        internal const int TargetRegistrationFormatOffset = 44;
        internal const int TargetRegistrationDimensionOffset = 48;
        internal const int TargetRegistrationSampleCountOffset = 52;
        internal const int TargetRegistrationResourceKindOffset = 56;
        internal const int TargetRegistrationReserved1Offset = 60;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct TargetRegistration
        {
            internal uint structSize;
            internal uint flags;
            internal ulong graphicsDeviceEpoch;
            internal ulong nativeResource;
            internal uint width;
            internal uint height;
            internal uint depth;
            internal uint layers;
            internal uint mipCount;
            internal uint format;
            internal uint dimension;
            internal uint sampleCount;
            internal uint resourceKind;
            internal uint reserved1;
        }

        internal const int SupportQuerySize = 48;
        internal const int SupportQueryStructSizeOffset = 0;
        internal const int SupportQueryFormatOffset = 4;
        internal const int SupportQueryDimensionOffset = 8;
        internal const int SupportQueryResourceKindOffset = 12;
        internal const int SupportQueryAspectOffset = 16;
        internal const int SupportQueryWidthOffset = 20;
        internal const int SupportQueryHeightOffset = 24;
        internal const int SupportQueryDepthOffset = 28;
        internal const int SupportQueryLayersOffset = 32;
        internal const int SupportQueryMipCountOffset = 36;
        internal const int SupportQuerySampleCountOffset = 40;
        internal const int SupportQueryReservedOffset = 44;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct SupportQuery
        {
            internal uint structSize;
            internal uint format;
            internal uint dimension;
            internal uint resourceKind;
            internal uint aspect;
            internal uint width;
            internal uint height;
            internal uint depth;
            internal uint layers;
            internal uint mipCount;
            internal uint sampleCount;
            internal uint reserved;
        }

        internal const int SupportInfoSize = 64;
        internal const int SupportInfoStructSizeOffset = 0;
        internal const int SupportInfoSupportedOffset = 4;
        internal const int SupportInfoFlagsOffset = 8;
        internal const int SupportInfoSupportedAspectsOffset = 12;
        internal const int SupportInfoBlockWidthOffset = 16;
        internal const int SupportInfoBlockHeightOffset = 20;
        internal const int SupportInfoBlockDepthOffset = 24;
        internal const int SupportInfoBytesPerBlockOffset = 28;
        internal const int SupportInfoMinimumBlocksXOffset = 32;
        internal const int SupportInfoMinimumBlocksYOffset = 36;
        internal const int SupportInfoMinimumBlocksZOffset = 40;
        internal const int SupportInfoStagingRowPitchAlignmentOffset = 44;
        internal const int SupportInfoStagingOffsetAlignmentOffset = 48;
        internal const int SupportInfoReserved0Offset = 52;
        internal const int SupportInfoReserved1Offset = 56;
        internal const int SupportInfoReserved2Offset = 60;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct SupportInfo
        {
            internal uint structSize;
            internal uint supported;
            internal uint flags;
            internal uint supportedAspects;
            internal uint blockWidth;
            internal uint blockHeight;
            internal uint blockDepth;
            internal uint bytesPerBlock;
            internal uint minimumBlocksX;
            internal uint minimumBlocksY;
            internal uint minimumBlocksZ;
            internal uint stagingRowPitchAlignment;
            internal uint stagingOffsetAlignment;
            internal uint reserved0;
            internal uint reserved1;
            internal uint reserved2;
        }

        internal const int TargetHandleSize = 16;
        internal const int TargetHandleTokenOffset = 0;
        internal const int TargetHandleGenerationOffset = 8;
        internal const int TargetHandleReservedOffset = 12;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct TargetHandle
        {
            internal ulong token;
            internal uint generation;
            internal uint reserved;
        }

        internal const int TargetStatusSize = 32;
        internal const int TargetStatusStructSizeOffset = 0;
        internal const int TargetStatusStateOffset = 4;
        internal const int TargetStatusGenerationOffset = 8;
        internal const int TargetStatusOutstandingLeasesOffset = 12;
        internal const int TargetStatusGraphicsDeviceEpochOffset = 16;
        internal const int TargetStatusErrorOffset = 24;
        internal const int TargetStatusBackendDetailOffset = 28;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct TargetStatus
        {
            internal uint structSize;
            internal uint state;
            internal uint generation;
            internal uint outstandingLeases;
            internal ulong graphicsDeviceEpoch;
            internal int error;
            internal uint backendDetail;
        }

        internal const int SubmissionStatusSize = 48;
        internal const int SubmissionStatusStructSizeOffset = 0;
        internal const int SubmissionStatusStateOffset = 4;
        internal const int SubmissionStatusGpuStateOffset = 8;
        internal const int SubmissionStatusContentStateOffset = 12;
        internal const int SubmissionStatusResultCodeOffset = 16;
        internal const int SubmissionStatusFailedRegionOffset = 20;
        internal const int SubmissionStatusBackendDetailOffset = 24;
        internal const int SubmissionStatusFlagsOffset = 28;
        internal const int SubmissionStatusSerialOffset = 32;
        internal const int SubmissionStatusGraphicsDeviceEpochOffset = 40;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct SubmissionStatus
        {
            internal uint structSize;
            internal uint state;
            internal uint gpuState;
            internal uint contentState;
            internal int resultCode;
            internal int failedRegion;
            internal uint backendDetail;
            internal uint flags;
            internal ulong serial;
            internal ulong graphicsDeviceEpoch;
        }

        internal const int SequenceStatusSize = 80;
        internal const int SequenceStatusStructSizeOffset = 0;
        internal const int SequenceStatusStateOffset = 4;
        internal const int SequenceStatusGpuStateOffset = 8;
        internal const int SequenceStatusContentStateOffset = 12;
        internal const int SequenceStatusPublicationStateOffset = 16;
        internal const int SequenceStatusResultCodeOffset = 20;
        internal const int SequenceStatusFailedBatchOffset = 24;
        internal const int SequenceStatusFailedRegionOffset = 28;
        internal const int SequenceStatusBackendDetailOffset = 32;
        internal const int SequenceStatusFlagsOffset = 36;
        internal const int SequenceStatusExpectedBatchCountOffset = 40;
        internal const int SequenceStatusAdmittedBatchCountOffset = 44;
        internal const int SequenceStatusSourceConsumedBatchCountOffset = 48;
        internal const int SequenceStatusEncodedBatchCountOffset = 52;
        internal const int SequenceStatusGpuTerminalBatchCountOffset = 56;
        internal const int SequenceStatusRetiredBatchCountOffset = 60;
        internal const int SequenceStatusSerialOffset = 64;
        internal const int SequenceStatusGraphicsDeviceEpochOffset = 72;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct SequenceStatus
        {
            internal uint structSize;
            internal uint state;
            internal uint gpuState;
            internal uint contentState;
            internal uint publicationState;
            internal int resultCode;
            internal int failedBatch;
            internal int failedRegion;
            internal uint backendDetail;
            internal uint flags;
            internal uint expectedBatchCount;
            internal uint admittedBatchCount;
            internal uint sourceConsumedBatchCount;
            internal uint encodedBatchCount;
            internal uint gpuTerminalBatchCount;
            internal uint retiredBatchCount;
            internal ulong serial;
            internal ulong graphicsDeviceEpoch;
        }

        internal const int StatsSize = 120;
        internal const int StatsStructSizeOffset = 0;
        internal const int StatsReservedOffset = 4;
        internal const int StatsGraphicsDeviceEpochOffset = 8;
        internal const int StatsSubmissionsAcceptedOffset = 16;
        internal const int StatsSubmissionsRejectedOffset = 24;
        internal const int StatsSubmissionsEncodedOffset = 32;
        internal const int StatsDuplicateCallbacksOffset = 40;
        internal const int StatsStaleCallbacksOffset = 48;
        internal const int StatsBackpressureCountOffset = 56;
        internal const int StatsEncodedPayloadBytesOffset = 64;
        internal const int StatsPoolNodesOffset = 72;
        internal const int StatsPoolNodesFreeOffset = 80;
        internal const int StatsPoolNodesInFlightOffset = 88;
        internal const int StatsPoolStagingCapacityBytesOffset = 96;
        internal const int StatsPoolStagingFreeBytesOffset = 104;
        internal const int StatsPoolStagingInFlightBytesOffset = 112;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct Stats
        {
            internal uint structSize;
            internal uint reserved;
            internal ulong graphicsDeviceEpoch;
            internal ulong submissionsAccepted;
            internal ulong submissionsRejected;
            internal ulong submissionsEncoded;
            internal ulong duplicateCallbacks;
            internal ulong staleCallbacks;
            internal ulong backpressureCount;
            internal ulong encodedPayloadBytes;
            internal ulong poolNodes;
            internal ulong poolNodesFree;
            internal ulong poolNodesInFlight;
            internal ulong poolStagingCapacityBytes;
            internal ulong poolStagingFreeBytes;
            internal ulong poolStagingInFlightBytes;
        }

        internal static readonly bool LayoutMatches =
            UnsafeUtility.SizeOf<Batch>() == BatchSize
            && OffsetMatches<Batch>(nameof(Batch.magic), BatchMagicOffset)
            && OffsetMatches<Batch>(nameof(Batch.abiMajor), BatchAbiMajorOffset)
            && OffsetMatches<Batch>(nameof(Batch.abiMinor), BatchAbiMinorOffset)
            && OffsetMatches<Batch>(nameof(Batch.totalBytes), BatchTotalBytesOffset)
            && OffsetMatches<Batch>(nameof(Batch.flags), BatchFlagsOffset)
            && OffsetMatches<Batch>(nameof(Batch.serial), BatchSerialOffset)
            && OffsetMatches<Batch>(nameof(Batch.graphicsDeviceEpoch), BatchGraphicsDeviceEpochOffset)
            && OffsetMatches<Batch>(nameof(Batch.sequenceSerial), BatchSequenceSerialOffset)
            && OffsetMatches<Batch>(nameof(Batch.sequenceOrdinal), BatchSequenceOrdinalOffset)
            && OffsetMatches<Batch>(nameof(Batch.slotId), BatchSlotIdOffset)
            && OffsetMatches<Batch>(nameof(Batch.slotGeneration), BatchSlotGenerationOffset)
            && OffsetMatches<Batch>(nameof(Batch.writtenBytes), BatchWrittenBytesOffset)
            && OffsetMatches<Batch>(nameof(Batch.targetTableOffset), BatchTargetTableOffsetOffset)
            && OffsetMatches<Batch>(nameof(Batch.targetCount), BatchTargetCountOffset)
            && OffsetMatches<Batch>(nameof(Batch.regionTableOffset), BatchRegionTableOffsetOffset)
            && OffsetMatches<Batch>(nameof(Batch.regionCount), BatchRegionCountOffset)
            && OffsetMatches<Batch>(nameof(Batch.resultCode), BatchResultCodeOffset)
            && OffsetMatches<Batch>(nameof(Batch.failedRegion), BatchFailedRegionOffset)
            && OffsetMatches<Batch>(nameof(Batch.admissionStage), BatchAdmissionStageOffset)
            && OffsetMatches<Batch>(nameof(Batch.backendDetail), BatchBackendDetailOffset)
            && OffsetMatches<Batch>(nameof(Batch.state), BatchStateOffset)
            && OffsetMatches<Batch>(nameof(Batch.reserved0), BatchReserved0Offset)
            && UnsafeUtility.SizeOf<SequenceDesc>() == SequenceDescSize
            && OffsetMatches<SequenceDesc>(nameof(SequenceDesc.structSize), SequenceDescStructSizeOffset)
            && OffsetMatches<SequenceDesc>(nameof(SequenceDesc.flags), SequenceDescFlagsOffset)
            && OffsetMatches<SequenceDesc>(nameof(SequenceDesc.graphicsDeviceEpoch), SequenceDescGraphicsDeviceEpochOffset)
            && OffsetMatches<SequenceDesc>(nameof(SequenceDesc.reserved0), SequenceDescReserved0Offset)
            && OffsetMatches<SequenceDesc>(nameof(SequenceDesc.reserved1), SequenceDescReserved1Offset)
            && UnsafeUtility.SizeOf<Target>() == TargetSize
            && OffsetMatches<Target>(nameof(Target.token), TargetTokenOffset)
            && OffsetMatches<Target>(nameof(Target.generation), TargetGenerationOffset)
            && OffsetMatches<Target>(nameof(Target.width), TargetWidthOffset)
            && OffsetMatches<Target>(nameof(Target.height), TargetHeightOffset)
            && OffsetMatches<Target>(nameof(Target.depth), TargetDepthOffset)
            && OffsetMatches<Target>(nameof(Target.layers), TargetLayersOffset)
            && OffsetMatches<Target>(nameof(Target.mipCount), TargetMipCountOffset)
            && OffsetMatches<Target>(nameof(Target.format), TargetFormatOffset)
            && OffsetMatches<Target>(nameof(Target.dimension), TargetDimensionOffset)
            && OffsetMatches<Target>(nameof(Target.sampleCount), TargetSampleCountOffset)
            && OffsetMatches<Target>(nameof(Target.flags), TargetFlagsOffset)
            && UnsafeUtility.SizeOf<Region>() == RegionSize
            && OffsetMatches<Region>(nameof(Region.targetIndex), RegionTargetIndexOffset)
            && OffsetMatches<Region>(nameof(Region.mipLevel), RegionMipLevelOffset)
            && OffsetMatches<Region>(nameof(Region.aspect), RegionAspectOffset)
            && OffsetMatches<Region>(nameof(Region.destinationX), RegionDestinationXOffset)
            && OffsetMatches<Region>(nameof(Region.destinationY), RegionDestinationYOffset)
            && OffsetMatches<Region>(nameof(Region.destinationZ), RegionDestinationZOffset)
            && OffsetMatches<Region>(nameof(Region.width), RegionWidthOffset)
            && OffsetMatches<Region>(nameof(Region.height), RegionHeightOffset)
            && OffsetMatches<Region>(nameof(Region.depth), RegionDepthOffset)
            && OffsetMatches<Region>(nameof(Region.baseLayer), RegionBaseLayerOffset)
            && OffsetMatches<Region>(nameof(Region.layerCount), RegionLayerCountOffset)
            && OffsetMatches<Region>(nameof(Region.slotOffset), RegionSlotOffsetOffset)
            && OffsetMatches<Region>(nameof(Region.sourceRowPitch), RegionSourceRowPitchOffset)
            && OffsetMatches<Region>(nameof(Region.sourceImagePitch), RegionSourceImagePitchOffset)
            && OffsetMatches<Region>(nameof(Region.sourceLayerPitch), RegionSourceLayerPitchOffset)
            && OffsetMatches<Region>(nameof(Region.flags), RegionFlagsOffset)
            && UnsafeUtility.SizeOf<DeviceInfo>() == DeviceInfoSize
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.structSize), DeviceInfoStructSizeOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.abiMajor), DeviceInfoAbiMajorOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.abiMinor), DeviceInfoAbiMinorOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.renderer), DeviceInfoRendererOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.flags), DeviceInfoFlagsOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.eventBase), DeviceInfoEventBaseOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.eventCount), DeviceInfoEventCountOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.graphicsDeviceEpoch), DeviceInfoGraphicsDeviceEpochOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.formatCount), DeviceInfoFormatCountOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.dimensionCount), DeviceInfoDimensionCountOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.maxStagingBytes), DeviceInfoMaxStagingBytesOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.maxConcurrentSubmissions), DeviceInfoMaxConcurrentSubmissionsOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.contractFingerprint), DeviceInfoContractFingerprintOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.slotBytes), DeviceInfoSlotBytesOffset)
            && OffsetMatches<DeviceInfo>(nameof(DeviceInfo.maxRegionsPerSubmission), DeviceInfoMaxRegionsPerSubmissionOffset)
            && UnsafeUtility.SizeOf<TargetRegistration>() == TargetRegistrationSize
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.structSize), TargetRegistrationStructSizeOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.flags), TargetRegistrationFlagsOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.graphicsDeviceEpoch), TargetRegistrationGraphicsDeviceEpochOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.nativeResource), TargetRegistrationNativeResourceOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.width), TargetRegistrationWidthOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.height), TargetRegistrationHeightOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.depth), TargetRegistrationDepthOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.layers), TargetRegistrationLayersOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.mipCount), TargetRegistrationMipCountOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.format), TargetRegistrationFormatOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.dimension), TargetRegistrationDimensionOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.sampleCount), TargetRegistrationSampleCountOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.resourceKind), TargetRegistrationResourceKindOffset)
            && OffsetMatches<TargetRegistration>(nameof(TargetRegistration.reserved1), TargetRegistrationReserved1Offset)
            && UnsafeUtility.SizeOf<SupportQuery>() == SupportQuerySize
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.structSize), SupportQueryStructSizeOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.format), SupportQueryFormatOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.dimension), SupportQueryDimensionOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.resourceKind), SupportQueryResourceKindOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.aspect), SupportQueryAspectOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.width), SupportQueryWidthOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.height), SupportQueryHeightOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.depth), SupportQueryDepthOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.layers), SupportQueryLayersOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.mipCount), SupportQueryMipCountOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.sampleCount), SupportQuerySampleCountOffset)
            && OffsetMatches<SupportQuery>(nameof(SupportQuery.reserved), SupportQueryReservedOffset)
            && UnsafeUtility.SizeOf<SupportInfo>() == SupportInfoSize
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.structSize), SupportInfoStructSizeOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.supported), SupportInfoSupportedOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.flags), SupportInfoFlagsOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.supportedAspects), SupportInfoSupportedAspectsOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.blockWidth), SupportInfoBlockWidthOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.blockHeight), SupportInfoBlockHeightOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.blockDepth), SupportInfoBlockDepthOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.bytesPerBlock), SupportInfoBytesPerBlockOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.minimumBlocksX), SupportInfoMinimumBlocksXOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.minimumBlocksY), SupportInfoMinimumBlocksYOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.minimumBlocksZ), SupportInfoMinimumBlocksZOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.stagingRowPitchAlignment), SupportInfoStagingRowPitchAlignmentOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.stagingOffsetAlignment), SupportInfoStagingOffsetAlignmentOffset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.reserved0), SupportInfoReserved0Offset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.reserved1), SupportInfoReserved1Offset)
            && OffsetMatches<SupportInfo>(nameof(SupportInfo.reserved2), SupportInfoReserved2Offset)
            && UnsafeUtility.SizeOf<TargetHandle>() == TargetHandleSize
            && OffsetMatches<TargetHandle>(nameof(TargetHandle.token), TargetHandleTokenOffset)
            && OffsetMatches<TargetHandle>(nameof(TargetHandle.generation), TargetHandleGenerationOffset)
            && OffsetMatches<TargetHandle>(nameof(TargetHandle.reserved), TargetHandleReservedOffset)
            && UnsafeUtility.SizeOf<TargetStatus>() == TargetStatusSize
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.structSize), TargetStatusStructSizeOffset)
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.state), TargetStatusStateOffset)
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.generation), TargetStatusGenerationOffset)
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.outstandingLeases), TargetStatusOutstandingLeasesOffset)
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.graphicsDeviceEpoch), TargetStatusGraphicsDeviceEpochOffset)
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.error), TargetStatusErrorOffset)
            && OffsetMatches<TargetStatus>(nameof(TargetStatus.backendDetail), TargetStatusBackendDetailOffset)
            && UnsafeUtility.SizeOf<SubmissionStatus>() == SubmissionStatusSize
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.structSize), SubmissionStatusStructSizeOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.state), SubmissionStatusStateOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.gpuState), SubmissionStatusGpuStateOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.contentState), SubmissionStatusContentStateOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.resultCode), SubmissionStatusResultCodeOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.failedRegion), SubmissionStatusFailedRegionOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.backendDetail), SubmissionStatusBackendDetailOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.flags), SubmissionStatusFlagsOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.serial), SubmissionStatusSerialOffset)
            && OffsetMatches<SubmissionStatus>(nameof(SubmissionStatus.graphicsDeviceEpoch), SubmissionStatusGraphicsDeviceEpochOffset)
            && UnsafeUtility.SizeOf<SequenceStatus>() == SequenceStatusSize
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.structSize), SequenceStatusStructSizeOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.state), SequenceStatusStateOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.gpuState), SequenceStatusGpuStateOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.contentState), SequenceStatusContentStateOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.publicationState), SequenceStatusPublicationStateOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.resultCode), SequenceStatusResultCodeOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.failedBatch), SequenceStatusFailedBatchOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.failedRegion), SequenceStatusFailedRegionOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.backendDetail), SequenceStatusBackendDetailOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.flags), SequenceStatusFlagsOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.expectedBatchCount), SequenceStatusExpectedBatchCountOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.admittedBatchCount), SequenceStatusAdmittedBatchCountOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.sourceConsumedBatchCount), SequenceStatusSourceConsumedBatchCountOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.encodedBatchCount), SequenceStatusEncodedBatchCountOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.gpuTerminalBatchCount), SequenceStatusGpuTerminalBatchCountOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.retiredBatchCount), SequenceStatusRetiredBatchCountOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.serial), SequenceStatusSerialOffset)
            && OffsetMatches<SequenceStatus>(nameof(SequenceStatus.graphicsDeviceEpoch), SequenceStatusGraphicsDeviceEpochOffset)
            && UnsafeUtility.SizeOf<Stats>() == StatsSize
            && OffsetMatches<Stats>(nameof(Stats.structSize), StatsStructSizeOffset)
            && OffsetMatches<Stats>(nameof(Stats.reserved), StatsReservedOffset)
            && OffsetMatches<Stats>(nameof(Stats.graphicsDeviceEpoch), StatsGraphicsDeviceEpochOffset)
            && OffsetMatches<Stats>(nameof(Stats.submissionsAccepted), StatsSubmissionsAcceptedOffset)
            && OffsetMatches<Stats>(nameof(Stats.submissionsRejected), StatsSubmissionsRejectedOffset)
            && OffsetMatches<Stats>(nameof(Stats.submissionsEncoded), StatsSubmissionsEncodedOffset)
            && OffsetMatches<Stats>(nameof(Stats.duplicateCallbacks), StatsDuplicateCallbacksOffset)
            && OffsetMatches<Stats>(nameof(Stats.staleCallbacks), StatsStaleCallbacksOffset)
            && OffsetMatches<Stats>(nameof(Stats.backpressureCount), StatsBackpressureCountOffset)
            && OffsetMatches<Stats>(nameof(Stats.encodedPayloadBytes), StatsEncodedPayloadBytesOffset)
            && OffsetMatches<Stats>(nameof(Stats.poolNodes), StatsPoolNodesOffset)
            && OffsetMatches<Stats>(nameof(Stats.poolNodesFree), StatsPoolNodesFreeOffset)
            && OffsetMatches<Stats>(nameof(Stats.poolNodesInFlight), StatsPoolNodesInFlightOffset)
            && OffsetMatches<Stats>(nameof(Stats.poolStagingCapacityBytes), StatsPoolStagingCapacityBytesOffset)
            && OffsetMatches<Stats>(nameof(Stats.poolStagingFreeBytes), StatsPoolStagingFreeBytesOffset)
            && OffsetMatches<Stats>(nameof(Stats.poolStagingInFlightBytes), StatsPoolStagingInFlightBytesOffset);

        internal static readonly bool ManagedEnumsMatch =
            (uint)GpuUploadFormat.Unknown == (uint)Format.Unknown
            && (uint)GpuUploadFormat.R8SRgb == (uint)Format.R8SRgb
            && (uint)GpuUploadFormat.RG8SRgb == (uint)Format.RG8SRgb
            && (uint)GpuUploadFormat.RGB8SRgb == (uint)Format.RGB8SRgb
            && (uint)GpuUploadFormat.RGBA8SRgb == (uint)Format.RGBA8SRgb
            && (uint)GpuUploadFormat.R8UNorm == (uint)Format.R8UNorm
            && (uint)GpuUploadFormat.RG8UNorm == (uint)Format.RG8UNorm
            && (uint)GpuUploadFormat.RGB8UNorm == (uint)Format.RGB8UNorm
            && (uint)GpuUploadFormat.RGBA8UNorm == (uint)Format.RGBA8UNorm
            && (uint)GpuUploadFormat.R8SNorm == (uint)Format.R8SNorm
            && (uint)GpuUploadFormat.RG8SNorm == (uint)Format.RG8SNorm
            && (uint)GpuUploadFormat.RGB8SNorm == (uint)Format.RGB8SNorm
            && (uint)GpuUploadFormat.RGBA8SNorm == (uint)Format.RGBA8SNorm
            && (uint)GpuUploadFormat.R8UInt == (uint)Format.R8UInt
            && (uint)GpuUploadFormat.RG8UInt == (uint)Format.RG8UInt
            && (uint)GpuUploadFormat.RGB8UInt == (uint)Format.RGB8UInt
            && (uint)GpuUploadFormat.RGBA8UInt == (uint)Format.RGBA8UInt
            && (uint)GpuUploadFormat.R8SInt == (uint)Format.R8SInt
            && (uint)GpuUploadFormat.RG8SInt == (uint)Format.RG8SInt
            && (uint)GpuUploadFormat.RGB8SInt == (uint)Format.RGB8SInt
            && (uint)GpuUploadFormat.RGBA8SInt == (uint)Format.RGBA8SInt
            && (uint)GpuUploadFormat.R16UNorm == (uint)Format.R16UNorm
            && (uint)GpuUploadFormat.RG16UNorm == (uint)Format.RG16UNorm
            && (uint)GpuUploadFormat.RGB16UNorm == (uint)Format.RGB16UNorm
            && (uint)GpuUploadFormat.RGBA16UNorm == (uint)Format.RGBA16UNorm
            && (uint)GpuUploadFormat.R16SNorm == (uint)Format.R16SNorm
            && (uint)GpuUploadFormat.RG16SNorm == (uint)Format.RG16SNorm
            && (uint)GpuUploadFormat.RGB16SNorm == (uint)Format.RGB16SNorm
            && (uint)GpuUploadFormat.RGBA16SNorm == (uint)Format.RGBA16SNorm
            && (uint)GpuUploadFormat.R16UInt == (uint)Format.R16UInt
            && (uint)GpuUploadFormat.RG16UInt == (uint)Format.RG16UInt
            && (uint)GpuUploadFormat.RGB16UInt == (uint)Format.RGB16UInt
            && (uint)GpuUploadFormat.RGBA16UInt == (uint)Format.RGBA16UInt
            && (uint)GpuUploadFormat.R16SInt == (uint)Format.R16SInt
            && (uint)GpuUploadFormat.RG16SInt == (uint)Format.RG16SInt
            && (uint)GpuUploadFormat.RGB16SInt == (uint)Format.RGB16SInt
            && (uint)GpuUploadFormat.RGBA16SInt == (uint)Format.RGBA16SInt
            && (uint)GpuUploadFormat.R32UInt == (uint)Format.R32UInt
            && (uint)GpuUploadFormat.RG32UInt == (uint)Format.RG32UInt
            && (uint)GpuUploadFormat.RGB32UInt == (uint)Format.RGB32UInt
            && (uint)GpuUploadFormat.RGBA32UInt == (uint)Format.RGBA32UInt
            && (uint)GpuUploadFormat.R32SInt == (uint)Format.R32SInt
            && (uint)GpuUploadFormat.RG32SInt == (uint)Format.RG32SInt
            && (uint)GpuUploadFormat.RGB32SInt == (uint)Format.RGB32SInt
            && (uint)GpuUploadFormat.RGBA32SInt == (uint)Format.RGBA32SInt
            && (uint)GpuUploadFormat.R16SFloat == (uint)Format.R16SFloat
            && (uint)GpuUploadFormat.RG16SFloat == (uint)Format.RG16SFloat
            && (uint)GpuUploadFormat.RGB16SFloat == (uint)Format.RGB16SFloat
            && (uint)GpuUploadFormat.RGBA16SFloat == (uint)Format.RGBA16SFloat
            && (uint)GpuUploadFormat.R32SFloat == (uint)Format.R32SFloat
            && (uint)GpuUploadFormat.RG32SFloat == (uint)Format.RG32SFloat
            && (uint)GpuUploadFormat.RGB32SFloat == (uint)Format.RGB32SFloat
            && (uint)GpuUploadFormat.RGBA32SFloat == (uint)Format.RGBA32SFloat
            && (uint)GpuUploadFormat.BGR8SRgb == (uint)Format.BGR8SRgb
            && (uint)GpuUploadFormat.BGRA8SRgb == (uint)Format.BGRA8SRgb
            && (uint)GpuUploadFormat.BGR8UNorm == (uint)Format.BGR8UNorm
            && (uint)GpuUploadFormat.BGRA8UNorm == (uint)Format.BGRA8UNorm
            && (uint)GpuUploadFormat.BGR8SNorm == (uint)Format.BGR8SNorm
            && (uint)GpuUploadFormat.BGRA8SNorm == (uint)Format.BGRA8SNorm
            && (uint)GpuUploadFormat.BGR8UInt == (uint)Format.BGR8UInt
            && (uint)GpuUploadFormat.BGRA8UInt == (uint)Format.BGRA8UInt
            && (uint)GpuUploadFormat.BGR8SInt == (uint)Format.BGR8SInt
            && (uint)GpuUploadFormat.BGRA8SInt == (uint)Format.BGRA8SInt
            && (uint)GpuUploadFormat.R4G4B4A4UNormPack16 == (uint)Format.R4G4B4A4UNormPack16
            && (uint)GpuUploadFormat.B4G4R4A4UNormPack16 == (uint)Format.B4G4R4A4UNormPack16
            && (uint)GpuUploadFormat.R5G6B5UNormPack16 == (uint)Format.R5G6B5UNormPack16
            && (uint)GpuUploadFormat.B5G6R5UNormPack16 == (uint)Format.B5G6R5UNormPack16
            && (uint)GpuUploadFormat.R5G5B5A1UNormPack16 == (uint)Format.R5G5B5A1UNormPack16
            && (uint)GpuUploadFormat.B5G5R5A1UNormPack16 == (uint)Format.B5G5R5A1UNormPack16
            && (uint)GpuUploadFormat.A1R5G5B5UNormPack16 == (uint)Format.A1R5G5B5UNormPack16
            && (uint)GpuUploadFormat.E5B9G9R9UFloatPack32 == (uint)Format.E5B9G9R9UFloatPack32
            && (uint)GpuUploadFormat.B10G11R11UFloatPack32 == (uint)Format.B10G11R11UFloatPack32
            && (uint)GpuUploadFormat.A2B10G10R10UNormPack32 == (uint)Format.A2B10G10R10UNormPack32
            && (uint)GpuUploadFormat.A2B10G10R10UIntPack32 == (uint)Format.A2B10G10R10UIntPack32
            && (uint)GpuUploadFormat.A2B10G10R10SIntPack32 == (uint)Format.A2B10G10R10SIntPack32
            && (uint)GpuUploadFormat.A2R10G10B10UNormPack32 == (uint)Format.A2R10G10B10UNormPack32
            && (uint)GpuUploadFormat.A2R10G10B10UIntPack32 == (uint)Format.A2R10G10B10UIntPack32
            && (uint)GpuUploadFormat.A2R10G10B10SIntPack32 == (uint)Format.A2R10G10B10SIntPack32
            && (uint)GpuUploadFormat.D16UNorm == (uint)Format.D16UNorm
            && (uint)GpuUploadFormat.D24UNorm == (uint)Format.D24UNorm
            && (uint)GpuUploadFormat.D24UNormS8UInt == (uint)Format.D24UNormS8UInt
            && (uint)GpuUploadFormat.D32SFloat == (uint)Format.D32SFloat
            && (uint)GpuUploadFormat.D32SFloatS8UInt == (uint)Format.D32SFloatS8UInt
            && (uint)GpuUploadFormat.S8UInt == (uint)Format.S8UInt
            && (uint)GpuUploadFormat.RGBADXT1SRgb == (uint)Format.RGBADXT1SRgb
            && (uint)GpuUploadFormat.RGBADXT1UNorm == (uint)Format.RGBADXT1UNorm
            && (uint)GpuUploadFormat.RGBADXT3SRgb == (uint)Format.RGBADXT3SRgb
            && (uint)GpuUploadFormat.RGBADXT3UNorm == (uint)Format.RGBADXT3UNorm
            && (uint)GpuUploadFormat.RGBADXT5SRgb == (uint)Format.RGBADXT5SRgb
            && (uint)GpuUploadFormat.RGBADXT5UNorm == (uint)Format.RGBADXT5UNorm
            && (uint)GpuUploadFormat.RBC4SNorm == (uint)Format.RBC4SNorm
            && (uint)GpuUploadFormat.RBC4UNorm == (uint)Format.RBC4UNorm
            && (uint)GpuUploadFormat.RGBC5SNorm == (uint)Format.RGBC5SNorm
            && (uint)GpuUploadFormat.RGBC5UNorm == (uint)Format.RGBC5UNorm
            && (uint)GpuUploadFormat.RGBBC6HSFloat == (uint)Format.RGBBC6HSFloat
            && (uint)GpuUploadFormat.RGBBC6HUFloat == (uint)Format.RGBBC6HUFloat
            && (uint)GpuUploadFormat.RGBABC7SRgb == (uint)Format.RGBABC7SRgb
            && (uint)GpuUploadFormat.RGBABC7UNorm == (uint)Format.RGBABC7UNorm
            && (uint)GpuUploadFormat.RGBPVRTC2BppSRgb == (uint)Format.RGBPVRTC2BppSRgb
            && (uint)GpuUploadFormat.RGBPVRTC2BppUNorm == (uint)Format.RGBPVRTC2BppUNorm
            && (uint)GpuUploadFormat.RGBPVRTC4BppSRgb == (uint)Format.RGBPVRTC4BppSRgb
            && (uint)GpuUploadFormat.RGBPVRTC4BppUNorm == (uint)Format.RGBPVRTC4BppUNorm
            && (uint)GpuUploadFormat.RGBAPVRTC2BppSRgb == (uint)Format.RGBAPVRTC2BppSRgb
            && (uint)GpuUploadFormat.RGBAPVRTC2BppUNorm == (uint)Format.RGBAPVRTC2BppUNorm
            && (uint)GpuUploadFormat.RGBAPVRTC4BppSRgb == (uint)Format.RGBAPVRTC4BppSRgb
            && (uint)GpuUploadFormat.RGBAPVRTC4BppUNorm == (uint)Format.RGBAPVRTC4BppUNorm
            && (uint)GpuUploadFormat.RGBETCUNorm == (uint)Format.RGBETCUNorm
            && (uint)GpuUploadFormat.RGBETC2SRgb == (uint)Format.RGBETC2SRgb
            && (uint)GpuUploadFormat.RGBETC2UNorm == (uint)Format.RGBETC2UNorm
            && (uint)GpuUploadFormat.RGBA1ETC2SRgb == (uint)Format.RGBA1ETC2SRgb
            && (uint)GpuUploadFormat.RGBA1ETC2UNorm == (uint)Format.RGBA1ETC2UNorm
            && (uint)GpuUploadFormat.RGBAETC2SRgb == (uint)Format.RGBAETC2SRgb
            && (uint)GpuUploadFormat.RGBAETC2UNorm == (uint)Format.RGBAETC2UNorm
            && (uint)GpuUploadFormat.REACSNorm == (uint)Format.REACSNorm
            && (uint)GpuUploadFormat.REACUNorm == (uint)Format.REACUNorm
            && (uint)GpuUploadFormat.RGEACSNorm == (uint)Format.RGEACSNorm
            && (uint)GpuUploadFormat.RGEACUNorm == (uint)Format.RGEACUNorm
            && (uint)GpuUploadFormat.RGBAASTC4X4SRgb == (uint)Format.RGBAASTC4X4SRgb
            && (uint)GpuUploadFormat.RGBAASTC4X4UNorm == (uint)Format.RGBAASTC4X4UNorm
            && (uint)GpuUploadFormat.RGBAASTC5X5SRgb == (uint)Format.RGBAASTC5X5SRgb
            && (uint)GpuUploadFormat.RGBAASTC5X5UNorm == (uint)Format.RGBAASTC5X5UNorm
            && (uint)GpuUploadFormat.RGBAASTC6X6SRgb == (uint)Format.RGBAASTC6X6SRgb
            && (uint)GpuUploadFormat.RGBAASTC6X6UNorm == (uint)Format.RGBAASTC6X6UNorm
            && (uint)GpuUploadFormat.RGBAASTC8X8SRgb == (uint)Format.RGBAASTC8X8SRgb
            && (uint)GpuUploadFormat.RGBAASTC8X8UNorm == (uint)Format.RGBAASTC8X8UNorm
            && (uint)GpuUploadFormat.RGBAASTC10X10SRgb == (uint)Format.RGBAASTC10X10SRgb
            && (uint)GpuUploadFormat.RGBAASTC10X10UNorm == (uint)Format.RGBAASTC10X10UNorm
            && (uint)GpuUploadFormat.RGBAASTC12X12SRgb == (uint)Format.RGBAASTC12X12SRgb
            && (uint)GpuUploadFormat.RGBAASTC12X12UNorm == (uint)Format.RGBAASTC12X12UNorm
            && (uint)GpuUploadFormat.RGBAASTC4X4UFloat == (uint)Format.RGBAASTC4X4UFloat
            && (uint)GpuUploadFormat.RGBAASTC5X5UFloat == (uint)Format.RGBAASTC5X5UFloat
            && (uint)GpuUploadFormat.RGBAASTC6X6UFloat == (uint)Format.RGBAASTC6X6UFloat
            && (uint)GpuUploadFormat.RGBAASTC8X8UFloat == (uint)Format.RGBAASTC8X8UFloat
            && (uint)GpuUploadFormat.RGBAASTC10X10UFloat == (uint)Format.RGBAASTC10X10UFloat
            && (uint)GpuUploadFormat.RGBAASTC12X12UFloat == (uint)Format.RGBAASTC12X12UFloat
            && (uint)GpuUploadFormat.YUV2 == (uint)Format.YUV2
            && (uint)GpuUploadFormat.R10G10B10XRSRgbPack32 == (uint)Format.R10G10B10XRSRgbPack32
            && (uint)GpuUploadFormat.R10G10B10XRUNormPack32 == (uint)Format.R10G10B10XRUNormPack32
            && (uint)GpuUploadFormat.A10R10G10B10XRSRgbPack32 == (uint)Format.A10R10G10B10XRSRgbPack32
            && (uint)GpuUploadFormat.A10R10G10B10XRUNormPack32 == (uint)Format.A10R10G10B10XRUNormPack32
            && (uint)GpuUploadFormat.A2R10G10B10XRSRgbPack32 == (uint)Format.A2R10G10B10XRSRgbPack32
            && (uint)GpuUploadFormat.A2R10G10B10XRUNormPack32 == (uint)Format.A2R10G10B10XRUNormPack32
            && (uint)GpuUploadFormat.D16UNormS8UInt == (uint)Format.D16UNormS8UInt
            && (uint)GpuUploadDimension.Unknown == (uint)Dimension.Unknown
            && (uint)GpuUploadDimension.Texture2D == (uint)Dimension.Texture2D
            && (uint)GpuUploadDimension.Texture2DArray == (uint)Dimension.Texture2DArray
            && (uint)GpuUploadDimension.Texture3D == (uint)Dimension.Texture3D
            && (uint)GpuUploadDimension.Cube == (uint)Dimension.Cube
            && (uint)GpuUploadDimension.CubeArray == (uint)Dimension.CubeArray
            && (uint)GpuUploadAspect.Color == (uint)Aspect.Color
            && (uint)GpuUploadAspect.Depth == (uint)Aspect.Depth
            && (uint)GpuUploadAspect.Stencil == (uint)Aspect.Stencil
            && (uint)GpuUploadResourceKind.Texture == (uint)ResourceKind.Texture
            && (uint)GpuUploadResourceKind.DepthStencil == (uint)ResourceKind.DepthStencil
            && (uint)GpuUploadAspectMask.None == (uint)AspectMask.None
            && (uint)GpuUploadAspectMask.Color == (uint)AspectMask.Color
            && (uint)GpuUploadAspectMask.Depth == (uint)AspectMask.Depth
            && (uint)GpuUploadAspectMask.Stencil == (uint)AspectMask.Stencil
            && (uint)GpuUploadFormatFeatures.None == (uint)FormatFeatures.None
            && (uint)GpuUploadFormatFeatures.Compressed == (uint)FormatFeatures.Compressed
            && (uint)GpuUploadFormatFeatures.Depth == (uint)FormatFeatures.Depth
            && (uint)GpuUploadFormatFeatures.Stencil == (uint)FormatFeatures.Stencil
            && (uint)GpuUploadFormatFeatures.Pvrtc == (uint)FormatFeatures.Pvrtc
            && (uint)GpuUploadSupportFlags.None == (uint)SupportFlags.None
            && (uint)GpuUploadSupportFlags.Regional == (uint)SupportFlags.Regional
            && (uint)GpuUploadSupportFlags.WholeMipOnly == (uint)SupportFlags.WholeMipOnly
            && (uint)GpuUploadSupportFlags.EdgeRemainder == (uint)SupportFlags.EdgeRemainder
            && (uint)GpuUploadSupportFlags.ExplicitPitches == (uint)SupportFlags.ExplicitPitches
            && (uint)GpuUploadSupportFlags.PowerOfTwo == (uint)SupportFlags.PowerOfTwo
            && (uint)GpuUploadSupportFlags.TopMipBlockMultiple == (uint)SupportFlags.TopMipBlockMultiple
            && (uint)GpuUploadSupportFlags.PaddedRowRepack == (uint)SupportFlags.PaddedRowRepack
            && (int)GpuUploadError.None == (int)Error.None
            && (int)GpuUploadError.NotInitialized == (int)Error.NotInitialized
            && (int)GpuUploadError.AbiMismatch == (int)Error.AbiMismatch
            && (int)GpuUploadError.UnsupportedBackend == (int)Error.UnsupportedBackend
            && (int)GpuUploadError.UnsupportedFeature == (int)Error.UnsupportedFeature
            && (int)GpuUploadError.InvalidArgument == (int)Error.InvalidArgument
            && (int)GpuUploadError.InvalidLayout == (int)Error.InvalidLayout
            && (int)GpuUploadError.UnsupportedTexture == (int)Error.UnsupportedTexture
            && (int)GpuUploadError.UnsupportedDimension == (int)Error.UnsupportedDimension
            && (int)GpuUploadError.UnsupportedFormat == (int)Error.UnsupportedFormat
            && (int)GpuUploadError.UnsupportedSampleCount == (int)Error.UnsupportedSampleCount
            && (int)GpuUploadError.TargetNotFound == (int)Error.TargetNotFound
            && (int)GpuUploadError.TargetStale == (int)Error.TargetStale
            && (int)GpuUploadError.TargetBusy == (int)Error.TargetBusy
            && (int)GpuUploadError.TargetClosing == (int)Error.TargetClosing
            && (int)GpuUploadError.SourceOutOfRange == (int)Error.SourceOutOfRange
            && (int)GpuUploadError.Backpressure == (int)Error.Backpressure
            && (int)GpuUploadError.DeviceLost == (int)Error.DeviceLost
            && (int)GpuUploadError.BackendFailed == (int)Error.BackendFailed
            && (int)GpuUploadError.ContextLost == (int)Error.ContextLost
            && (int)GpuUploadError.StateRestoreFailed == (int)Error.StateRestoreFailed
            && (int)GpuUploadError.OutOfMemory == (int)Error.OutOfMemory
            && (int)GpuUploadError.SubmissionNotFound == (int)Error.SubmissionNotFound
            && (int)GpuUploadError.SubmissionClosing == (int)Error.SubmissionClosing
            && (int)GpuUploadError.InternalError == (int)Error.InternalError
            && (int)GpuUploadError.SequenceNotFound == (int)Error.SequenceNotFound
            && (int)GpuUploadError.SequenceClosing == (int)Error.SequenceClosing
            && (int)GpuUploadError.SequenceOrder == (int)Error.SequenceOrder
            && (int)GpuUploadError.SlotNotFound == (int)Error.SlotNotFound
            && (int)GpuUploadError.SlotBusy == (int)Error.SlotBusy
            && (uint)GpuUploadAdmissionStage.None == (uint)AdmissionStage.None
            && (uint)GpuUploadAdmissionStage.Layout == (uint)AdmissionStage.Layout
            && (uint)GpuUploadAdmissionStage.Slot == (uint)AdmissionStage.Slot
            && (uint)GpuUploadAdmissionStage.Target == (uint)AdmissionStage.Target
            && (uint)GpuUploadAdmissionStage.Region == (uint)AdmissionStage.Region
            && (uint)GpuUploadAdmissionStage.Marker == (uint)AdmissionStage.Marker
            && (uint)GpuUploadAdmissionStage.Overlap == (uint)AdmissionStage.Overlap
            && (uint)GpuUploadAdmissionStage.Backend == (uint)AdmissionStage.Backend
            && (uint)GpuUploadSubmissionState.Invalid == (uint)SubmissionState.Invalid
            && (uint)GpuUploadSubmissionState.Pending == (uint)SubmissionState.Pending
            && (uint)GpuUploadSubmissionState.Encoded == (uint)SubmissionState.Encoded
            && (uint)GpuUploadSubmissionState.Rejected == (uint)SubmissionState.Rejected
            && (uint)GpuUploadSubmissionState.BackendFailed == (uint)SubmissionState.BackendFailed
            && (uint)GpuUploadSubmissionState.DeviceLost == (uint)SubmissionState.DeviceLost
            && (uint)GpuUploadSubmissionState.Cancelled == (uint)SubmissionState.Cancelled
            && (uint)GpuUploadSubmissionState.Retired == (uint)SubmissionState.Retired
            && (uint)GpuUploadGpuState.Unsupported == (uint)GpuState.Unsupported
            && (uint)GpuUploadGpuState.Pending == (uint)GpuState.Pending
            && (uint)GpuUploadGpuState.Complete == (uint)GpuState.Complete
            && (uint)GpuUploadGpuState.Failed == (uint)GpuState.Failed
            && (uint)GpuUploadContentState.Unchanged == (uint)ContentState.Unchanged
            && (uint)GpuUploadContentState.Changed == (uint)ContentState.Changed
            && (uint)GpuUploadContentState.MayHaveChanged == (uint)ContentState.MayHaveChanged
            && (uint)GpuUploadTargetState.Invalid == (uint)TargetState.Invalid
            && (uint)GpuUploadTargetState.Active == (uint)TargetState.Active
            && (uint)GpuUploadTargetState.Closing == (uint)TargetState.Closing
            && (uint)GpuUploadTargetState.Retired == (uint)TargetState.Retired
            && (uint)GpuUploadTargetState.Stale == (uint)TargetState.Stale
            && (uint)GpuUploadSequenceState.Invalid == (uint)SequenceState.Invalid
            && (uint)GpuUploadSequenceState.Open == (uint)SequenceState.Open
            && (uint)GpuUploadSequenceState.Sealed == (uint)SequenceState.Sealed
            && (uint)GpuUploadSequenceState.Failed == (uint)SequenceState.Failed
            && (uint)GpuUploadSequenceState.Aborted == (uint)SequenceState.Aborted
            && (uint)GpuUploadSequenceState.Complete == (uint)SequenceState.Complete
            && (uint)GpuUploadPublicationState.None == (uint)PublicationState.None
            && (uint)GpuUploadPublicationState.Pending == (uint)PublicationState.Pending
            && (uint)GpuUploadPublicationState.Suppressed == (uint)PublicationState.Suppressed
            && (uint)GpuUploadPublicationState.Published == (uint)PublicationState.Published
            && (uint)GpuUploadPublicationState.Unknown == (uint)PublicationState.Unknown
            && (uint)GpuUploadSequenceOptions.None == (uint)SequenceFlags.None
            && (uint)GpuUploadSequenceOptions.PublicationMarker == (uint)SequenceFlags.PublicationMarker
            && (uint)GpuUploadCapabilities.None == 0u
            && (uint)GpuUploadCapabilities.CommandBuffer == (uint)DeviceFlags.CommandBuffer
            && (uint)GpuUploadCapabilities.Immediate == (uint)DeviceFlags.Immediate
            && (uint)GpuUploadCapabilities.SubmissionBoundary == (uint)DeviceFlags.SubmissionBoundary
            && (uint)GpuUploadCapabilities.MultiTarget == (uint)DeviceFlags.MultiTarget
            && (uint)GpuUploadCapabilities.ExplicitMips == (uint)DeviceFlags.ExplicitMips
            && (uint)GpuUploadCapabilities.GpuCompletion == (uint)DeviceFlags.GpuCompletion
            && (uint)GpuUploadCapabilities.TargetRefresh == (uint)DeviceFlags.TargetRefresh
            && (uint)GpuUploadCapabilities.CompletionPoll == (uint)DeviceFlags.CompletionPoll
            && FormatCount == (uint)Format.Count
            && DimensionCount == (uint)Dimension.Count;

        internal static readonly bool ContractMatches =
            LayoutMatches && ManagedEnumsMatch;
    }
}
