using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>Stable ABI texel formats accepted by regional upload backends.</summary>
    public enum GpuUploadFormat : uint
    {
        /// <summary>No portable texel layout; never valid for registration.</summary>
        Unknown = 0,
        /// <summary>One 8-bit channel with the sRGB transfer function.</summary>
        R8SRgb = 1,
        /// <summary>Two 8-bit channels with the sRGB transfer function.</summary>
        RG8SRgb = 2,
        /// <summary>Three 8-bit channels with the sRGB transfer function.</summary>
        RGB8SRgb = 3,
        /// <summary>Four 8-bit channels with sRGB RGB transfer and linear alpha.</summary>
        RGBA8SRgb = 4,
        /// <summary>One 8-bit unsigned normalized channel.</summary>
        R8UNorm = 5,
        /// <summary>Two 8-bit unsigned normalized channels.</summary>
        RG8UNorm = 6,
        /// <summary>Three 8-bit unsigned normalized channels.</summary>
        RGB8UNorm = 7,
        /// <summary>Four 8-bit unsigned normalized channels in RGBA order.</summary>
        RGBA8UNorm = 8,
        /// <summary>One 8-bit signed normalized channel.</summary>
        R8SNorm = 9,
        /// <summary>Two 8-bit signed normalized channels.</summary>
        RG8SNorm = 10,
        /// <summary>Three 8-bit signed normalized channels.</summary>
        RGB8SNorm = 11,
        /// <summary>Four 8-bit signed normalized channels in RGBA order.</summary>
        RGBA8SNorm = 12,
        /// <summary>One 8-bit unsigned integer channel.</summary>
        R8UInt = 13,
        /// <summary>Two 8-bit unsigned integer channels.</summary>
        RG8UInt = 14,
        /// <summary>Three 8-bit unsigned integer channels.</summary>
        RGB8UInt = 15,
        /// <summary>Four 8-bit unsigned integer channels in RGBA order.</summary>
        RGBA8UInt = 16,
        /// <summary>One 8-bit signed integer channel.</summary>
        R8SInt = 17,
        /// <summary>Two 8-bit signed integer channels.</summary>
        RG8SInt = 18,
        /// <summary>Three 8-bit signed integer channels.</summary>
        RGB8SInt = 19,
        /// <summary>Four 8-bit signed integer channels in RGBA order.</summary>
        RGBA8SInt = 20,
        /// <summary>One 16-bit unsigned normalized channel.</summary>
        R16UNorm = 21,
        /// <summary>Two 16-bit unsigned normalized channels.</summary>
        RG16UNorm = 22,
        /// <summary>Three 16-bit unsigned normalized channels.</summary>
        RGB16UNorm = 23,
        /// <summary>Four 16-bit unsigned normalized channels in RGBA order.</summary>
        RGBA16UNorm = 24,
        /// <summary>One 16-bit signed normalized channel.</summary>
        R16SNorm = 25,
        /// <summary>Two 16-bit signed normalized channels.</summary>
        RG16SNorm = 26,
        /// <summary>Three 16-bit signed normalized channels.</summary>
        RGB16SNorm = 27,
        /// <summary>Four 16-bit signed normalized channels in RGBA order.</summary>
        RGBA16SNorm = 28,
        /// <summary>One 16-bit unsigned integer channel.</summary>
        R16UInt = 29,
        /// <summary>Two 16-bit unsigned integer channels.</summary>
        RG16UInt = 30,
        /// <summary>Three 16-bit unsigned integer channels.</summary>
        RGB16UInt = 31,
        /// <summary>Four 16-bit unsigned integer channels in RGBA order.</summary>
        RGBA16UInt = 32,
        /// <summary>One 16-bit signed integer channel.</summary>
        R16SInt = 33,
        /// <summary>Two 16-bit signed integer channels.</summary>
        RG16SInt = 34,
        /// <summary>Three 16-bit signed integer channels.</summary>
        RGB16SInt = 35,
        /// <summary>Four 16-bit signed integer channels in RGBA order.</summary>
        RGBA16SInt = 36,
        /// <summary>One 32-bit unsigned integer channel.</summary>
        R32UInt = 37,
        /// <summary>Two 32-bit unsigned integer channels.</summary>
        RG32UInt = 38,
        /// <summary>Three 32-bit unsigned integer channels.</summary>
        RGB32UInt = 39,
        /// <summary>Four 32-bit unsigned integer channels in RGBA order.</summary>
        RGBA32UInt = 40,
        /// <summary>One 32-bit signed integer channel.</summary>
        R32SInt = 41,
        /// <summary>Two 32-bit signed integer channels.</summary>
        RG32SInt = 42,
        /// <summary>Three 32-bit signed integer channels.</summary>
        RGB32SInt = 43,
        /// <summary>Four 32-bit signed integer channels in RGBA order.</summary>
        RGBA32SInt = 44,
        /// <summary>One 16-bit IEEE floating-point channel.</summary>
        R16SFloat = 45,
        /// <summary>Two 16-bit IEEE floating-point channels.</summary>
        RG16SFloat = 46,
        /// <summary>Three 16-bit IEEE floating-point channels.</summary>
        RGB16SFloat = 47,
        /// <summary>Four 16-bit IEEE floating-point channels in RGBA order.</summary>
        RGBA16SFloat = 48,
        /// <summary>One 32-bit IEEE floating-point channel.</summary>
        R32SFloat = 49,
        /// <summary>Two 32-bit IEEE floating-point channels.</summary>
        RG32SFloat = 50,
        /// <summary>Three 32-bit IEEE floating-point channels.</summary>
        RGB32SFloat = 51,
        /// <summary>Four 32-bit IEEE floating-point channels in RGBA order.</summary>
        RGBA32SFloat = 52,
        /// <summary>Three 8-bit BGR channels with the sRGB transfer function.</summary>
        BGR8SRgb = 53,
        /// <summary>Four BGRA8 channels with sRGB RGB transfer and linear alpha.</summary>
        BGRA8SRgb = 54,
        /// <summary>Three 8-bit unsigned normalized channels in BGR order.</summary>
        BGR8UNorm = 55,
        /// <summary>Four 8-bit unsigned normalized channels in BGRA order.</summary>
        BGRA8UNorm = 56,
        /// <summary>Three 8-bit signed normalized channels in BGR order.</summary>
        BGR8SNorm = 57,
        /// <summary>Four 8-bit signed normalized channels in BGRA order.</summary>
        BGRA8SNorm = 58,
        /// <summary>Three 8-bit unsigned integer channels in BGR order.</summary>
        BGR8UInt = 59,
        /// <summary>Four 8-bit unsigned integer channels in BGRA order.</summary>
        BGRA8UInt = 60,
        /// <summary>Three 8-bit signed integer channels in BGR order.</summary>
        BGR8SInt = 61,
        /// <summary>Four 8-bit signed integer channels in BGRA order.</summary>
        BGRA8SInt = 62,
        /// <summary>RGBA channels packed as four unsigned normalized 4-bit fields.</summary>
        R4G4B4A4UNormPack16 = 63,
        /// <summary>BGRA channels packed as four unsigned normalized 4-bit fields.</summary>
        B4G4R4A4UNormPack16 = 64,
        /// <summary>RGB channels packed as unsigned normalized 5:6:5 fields.</summary>
        R5G6B5UNormPack16 = 65,
        /// <summary>BGR channels packed as unsigned normalized 5:6:5 fields.</summary>
        B5G6R5UNormPack16 = 66,
        /// <summary>RGBA channels packed as unsigned normalized 5:5:5:1 fields.</summary>
        R5G5B5A1UNormPack16 = 67,
        /// <summary>BGRA channels packed as unsigned normalized 5:5:5:1 fields.</summary>
        B5G5R5A1UNormPack16 = 68,
        /// <summary>ARGB channels packed as unsigned normalized 1:5:5:5 fields.</summary>
        A1R5G5B5UNormPack16 = 69,
        /// <summary>Shared-exponent RGB floating-point data packed into 32 bits.</summary>
        E5B9G9R9UFloatPack32 = 70,
        /// <summary>Unsigned floating-point BGR channels packed as 10:11:11 fields.</summary>
        B10G11R11UFloatPack32 = 71,
        /// <summary>ABGR channels packed as unsigned normalized 2:10:10:10 fields.</summary>
        A2B10G10R10UNormPack32 = 72,
        /// <summary>ABGR channels packed as unsigned integer 2:10:10:10 fields.</summary>
        A2B10G10R10UIntPack32 = 73,
        /// <summary>ABGR channels packed as signed integer 2:10:10:10 fields.</summary>
        A2B10G10R10SIntPack32 = 74,
        /// <summary>ARGB channels packed as unsigned normalized 2:10:10:10 fields.</summary>
        A2R10G10B10UNormPack32 = 75,
        /// <summary>ARGB channels packed as unsigned integer 2:10:10:10 fields.</summary>
        A2R10G10B10UIntPack32 = 76,
        /// <summary>ARGB channels packed as signed integer 2:10:10:10 fields.</summary>
        A2R10G10B10SIntPack32 = 77,
        /// <summary>One 16-bit unsigned normalized depth value.</summary>
        D16UNorm = 78,
        /// <summary>One 24-bit unsigned normalized depth value stored in 32 bits.</summary>
        D24UNorm = 79,
        /// <summary>Combined 24-bit unsigned normalized depth and 8-bit unsigned stencil storage.</summary>
        D24UNormS8UInt = 80,
        /// <summary>One 32-bit IEEE floating-point depth value.</summary>
        D32SFloat = 81,
        /// <summary>Combined 32-bit IEEE floating-point depth and 8-bit unsigned stencil storage.</summary>
        D32SFloatS8UInt = 82,
        /// <summary>One 8-bit unsigned stencil value.</summary>
        S8UInt = 83,
        /// <summary>BC1 RGBA blocks with the sRGB transfer function.</summary>
        RGBADXT1SRgb = 84,
        /// <summary>BC1 RGBA unsigned normalized blocks.</summary>
        RGBADXT1UNorm = 85,
        /// <summary>BC2 RGBA blocks with the sRGB transfer function.</summary>
        RGBADXT3SRgb = 86,
        /// <summary>BC2 RGBA unsigned normalized blocks.</summary>
        RGBADXT3UNorm = 87,
        /// <summary>BC3 RGBA blocks with the sRGB transfer function.</summary>
        RGBADXT5SRgb = 88,
        /// <summary>BC3 RGBA unsigned normalized blocks.</summary>
        RGBADXT5UNorm = 89,
        /// <summary>BC4 single-channel signed normalized blocks.</summary>
        RBC4SNorm = 90,
        /// <summary>BC4 single-channel unsigned normalized blocks.</summary>
        RBC4UNorm = 91,
        /// <summary>BC5 two-channel signed normalized blocks.</summary>
        RGBC5SNorm = 92,
        /// <summary>BC5 two-channel unsigned normalized blocks.</summary>
        RGBC5UNorm = 93,
        /// <summary>BC6H signed floating-point RGB blocks.</summary>
        RGBBC6HSFloat = 94,
        /// <summary>BC6H unsigned floating-point RGB blocks.</summary>
        RGBBC6HUFloat = 95,
        /// <summary>BC7 RGBA blocks with the sRGB transfer function.</summary>
        RGBABC7SRgb = 96,
        /// <summary>BC7 RGBA unsigned normalized blocks.</summary>
        RGBABC7UNorm = 97,
        /// <summary>PVRTC RGB blocks at two bits per pixel with the sRGB transfer function.</summary>
        RGBPVRTC2BppSRgb = 98,
        /// <summary>PVRTC RGB unsigned normalized blocks at two bits per pixel.</summary>
        RGBPVRTC2BppUNorm = 99,
        /// <summary>PVRTC RGB blocks at four bits per pixel with the sRGB transfer function.</summary>
        RGBPVRTC4BppSRgb = 100,
        /// <summary>PVRTC RGB unsigned normalized blocks at four bits per pixel.</summary>
        RGBPVRTC4BppUNorm = 101,
        /// <summary>PVRTC RGBA blocks at two bits per pixel with the sRGB transfer function.</summary>
        RGBAPVRTC2BppSRgb = 102,
        /// <summary>PVRTC RGBA unsigned normalized blocks at two bits per pixel.</summary>
        RGBAPVRTC2BppUNorm = 103,
        /// <summary>PVRTC RGBA blocks at four bits per pixel with the sRGB transfer function.</summary>
        RGBAPVRTC4BppSRgb = 104,
        /// <summary>PVRTC RGBA unsigned normalized blocks at four bits per pixel.</summary>
        RGBAPVRTC4BppUNorm = 105,
        /// <summary>ETC1 RGB unsigned normalized blocks.</summary>
        RGBETCUNorm = 106,
        /// <summary>ETC2 RGB blocks with the sRGB transfer function.</summary>
        RGBETC2SRgb = 107,
        /// <summary>ETC2 RGB unsigned normalized blocks.</summary>
        RGBETC2UNorm = 108,
        /// <summary>ETC2 RGB blocks with one-bit alpha and the sRGB transfer function.</summary>
        RGBA1ETC2SRgb = 109,
        /// <summary>ETC2 RGB unsigned normalized blocks with one-bit alpha.</summary>
        RGBA1ETC2UNorm = 110,
        /// <summary>ETC2 RGBA blocks with the sRGB transfer function.</summary>
        RGBAETC2SRgb = 111,
        /// <summary>ETC2 RGBA unsigned normalized blocks.</summary>
        RGBAETC2UNorm = 112,
        /// <summary>EAC single-channel signed normalized blocks.</summary>
        REACSNorm = 113,
        /// <summary>EAC single-channel unsigned normalized blocks.</summary>
        REACUNorm = 114,
        /// <summary>EAC two-channel signed normalized blocks.</summary>
        RGEACSNorm = 115,
        /// <summary>EAC two-channel unsigned normalized blocks.</summary>
        RGEACUNorm = 116,
        /// <summary>ASTC 4x4 RGBA blocks with the sRGB transfer function.</summary>
        RGBAASTC4X4SRgb = 117,
        /// <summary>ASTC 4x4 RGBA unsigned normalized blocks.</summary>
        RGBAASTC4X4UNorm = 118,
        /// <summary>ASTC 5x5 RGBA blocks with the sRGB transfer function.</summary>
        RGBAASTC5X5SRgb = 119,
        /// <summary>ASTC 5x5 RGBA unsigned normalized blocks.</summary>
        RGBAASTC5X5UNorm = 120,
        /// <summary>ASTC 6x6 RGBA blocks with the sRGB transfer function.</summary>
        RGBAASTC6X6SRgb = 121,
        /// <summary>ASTC 6x6 RGBA unsigned normalized blocks.</summary>
        RGBAASTC6X6UNorm = 122,
        /// <summary>ASTC 8x8 RGBA blocks with the sRGB transfer function.</summary>
        RGBAASTC8X8SRgb = 123,
        /// <summary>ASTC 8x8 RGBA unsigned normalized blocks.</summary>
        RGBAASTC8X8UNorm = 124,
        /// <summary>ASTC 10x10 RGBA blocks with the sRGB transfer function.</summary>
        RGBAASTC10X10SRgb = 125,
        /// <summary>ASTC 10x10 RGBA unsigned normalized blocks.</summary>
        RGBAASTC10X10UNorm = 126,
        /// <summary>ASTC 12x12 RGBA blocks with the sRGB transfer function.</summary>
        RGBAASTC12X12SRgb = 127,
        /// <summary>ASTC 12x12 RGBA unsigned normalized blocks.</summary>
        RGBAASTC12X12UNorm = 128,
        /// <summary>ASTC 4x4 RGBA unsigned floating-point blocks.</summary>
        RGBAASTC4X4UFloat = 129,
        /// <summary>ASTC 5x5 RGBA unsigned floating-point blocks.</summary>
        RGBAASTC5X5UFloat = 130,
        /// <summary>ASTC 6x6 RGBA unsigned floating-point blocks.</summary>
        RGBAASTC6X6UFloat = 131,
        /// <summary>ASTC 8x8 RGBA unsigned floating-point blocks.</summary>
        RGBAASTC8X8UFloat = 132,
        /// <summary>ASTC 10x10 RGBA unsigned floating-point blocks.</summary>
        RGBAASTC10X10UFloat = 133,
        /// <summary>ASTC 12x12 RGBA unsigned floating-point blocks.</summary>
        RGBAASTC12X12UFloat = 134,
        /// <summary>YUV 4:2:2 data packed as one four-byte block for each horizontal texel pair.</summary>
        YUV2 = 135,
        /// <summary>Three-channel extended-range 10-bit storage with the sRGB transfer function.</summary>
        R10G10B10XRSRgbPack32 = 136,
        /// <summary>Three-channel extended-range 10-bit linear storage.</summary>
        R10G10B10XRUNormPack32 = 137,
        /// <summary>Extended-range 10-bit RGB and 10-bit alpha storage with the sRGB transfer function.</summary>
        A10R10G10B10XRSRgbPack32 = 138,
        /// <summary>Extended-range 10-bit RGB and 10-bit alpha linear storage.</summary>
        A10R10G10B10XRUNormPack32 = 139,
        /// <summary>Extended-range 10-bit RGB and 2-bit alpha storage with the sRGB transfer function.</summary>
        A2R10G10B10XRSRgbPack32 = 140,
        /// <summary>Extended-range 10-bit RGB and 2-bit alpha linear storage.</summary>
        A2R10G10B10XRUNormPack32 = 141,
        /// <summary>Combined 16-bit unsigned normalized depth and 8-bit unsigned stencil storage.</summary>
        D16UNormS8UInt = 142
    }

    /// <summary>Texture storage dimensions represented by the upload ABI.</summary>
    public enum GpuUploadDimension : uint
    {
        /// <summary>No storage dimension; never valid for registration.</summary>
        Unknown = 0,
        /// <summary>One two-dimensional image per mip.</summary>
        Texture2D = 1,
        /// <summary>Multiple two-dimensional layers sharing dimensions and mip count.</summary>
        Texture2DArray = 2,
        /// <summary>One three-dimensional volume per mip.</summary>
        Texture3D = 3,
        /// <summary>Six two-dimensional cube faces per mip.</summary>
        Cube = 4,
        /// <summary>One or more six-face cubes in array storage.</summary>
        CubeArray = 5
    }

    /// <summary>One independently addressable storage aspect of a texture resource.</summary>
    public enum GpuUploadAspect : uint
    {
        /// <summary>Color or encoded compressed-color storage.</summary>
        Color = 0,
        /// <summary>Depth values without modifying a colocated stencil aspect.</summary>
        Depth = 1,
        /// <summary>Stencil values without modifying a colocated depth aspect.</summary>
        Stencil = 2
    }

    /// <summary>Logical native resource selected during registration.</summary>
    public enum GpuUploadResourceKind : uint
    {
        /// <summary>The texture's color storage returned by <see cref="Texture.GetNativeTexturePtr"/>.</summary>
        Texture = 0,
        /// <summary>The realized depth/stencil storage returned by <see cref="RenderTexture.GetNativeDepthBufferPtr"/>.</summary>
        DepthStencil = 1
    }

    /// <summary>Aspects physically represented by one storage format.</summary>
    [Flags]
    public enum GpuUploadAspectMask : uint
    {
        /// <summary>No addressable aspect.</summary>
        None = 0,
        /// <summary>Color storage is addressable.</summary>
        Color = 1 << 0,
        /// <summary>Depth storage is addressable.</summary>
        Depth = 1 << 1,
        /// <summary>Stencil storage is addressable.</summary>
        Stencil = 1 << 2
    }

    /// <summary>Storage-layout properties independent from the active graphics backend.</summary>
    [Flags]
    public enum GpuUploadFormatFeatures : uint
    {
        /// <summary>No special storage property.</summary>
        None = 0,
        /// <summary>Pixels are encoded into fixed-size compressed blocks.</summary>
        Compressed = 1 << 0,
        /// <summary>The format contains depth storage.</summary>
        Depth = 1 << 1,
        /// <summary>The format contains stencil storage.</summary>
        Stencil = 1 << 2,
        /// <summary>The format uses PVRTC minimum-footprint and whole-mip rules.</summary>
        Pvrtc = 1 << 3
    }

    /// <summary>Backend restrictions and fast paths for one exact format, dimension, resource, and aspect.</summary>
    [Flags]
    public enum GpuUploadSupportFlags : uint
    {
        /// <summary>No optional support property.</summary>
        None = 0,
        /// <summary>Subresource regions may be updated subject to block-alignment rules.</summary>
        Regional = 1 << 0,
        /// <summary>Every update must cover the complete mip of each addressed layer.</summary>
        WholeMipOnly = 1 << 1,
        /// <summary>A final partial block may be addressed by a region that reaches the mip edge.</summary>
        EdgeRemainder = 1 << 2,
        /// <summary>Caller source row, image, and layer pitches may exceed their tight values.</summary>
        ExplicitPitches = 1 << 3,
        /// <summary>Base width and height must be powers of two.</summary>
        PowerOfTwo = 1 << 4,
        /// <summary>Base width and height must be multiples of the format block dimensions.</summary>
        TopMipBlockMultiple = 1 << 5,
        /// <summary>Padded caller rows require a backend repack before the upload command.</summary>
        PaddedRowRepack = 1 << 6
    }

    /// <summary>
    /// Aspect-specific storage geometry. Region coordinates remain texels while source row,
    /// image, and layer pitches advance through rows and slices of these blocks.
    /// </summary>
    public readonly struct GpuUploadFormatLayout
    {
        /// <summary>Stable upload format described by this layout.</summary>
        public readonly GpuUploadFormat Format;
        /// <summary>Aspect whose source representation is described.</summary>
        public readonly GpuUploadAspect Aspect;
        /// <summary>All independently addressable aspects physically present in the format.</summary>
        public readonly GpuUploadAspectMask Aspects;
        /// <summary>Backend-independent storage properties.</summary>
        public readonly GpuUploadFormatFeatures Features;
        /// <summary>Texel width represented by one encoded block.</summary>
        public readonly int BlockWidth;
        /// <summary>Texel height represented by one encoded block.</summary>
        public readonly int BlockHeight;
        /// <summary>Texel depth represented by one encoded block.</summary>
        public readonly int BlockDepth;
        /// <summary>Source bytes in one encoded block of the selected aspect.</summary>
        public readonly int BytesPerBlock;
        /// <summary>Minimum stored block columns, including small PVRTC mips.</summary>
        public readonly int MinimumBlocksX;
        /// <summary>Minimum stored block rows, including small PVRTC mips.</summary>
        public readonly int MinimumBlocksY;
        /// <summary>Minimum stored block slices.</summary>
        public readonly int MinimumBlocksZ;
        internal GpuUploadFormatLayout(GpuUploadFormat format, GpuUploadAspect aspect,
            GpuUploadAspectMask aspects, GpuUploadFormatFeatures features, int blockWidth,
            int blockHeight, int blockDepth, int bytesPerBlock, int minimumBlocksX,
            int minimumBlocksY, int minimumBlocksZ)
        {
            Format = format;
            Aspect = aspect;
            Aspects = aspects;
            Features = features;
            BlockWidth = blockWidth;
            BlockHeight = blockHeight;
            BlockDepth = blockDepth;
            BytesPerBlock = bytesPerBlock;
            MinimumBlocksX = minimumBlocksX;
            MinimumBlocksY = minimumBlocksY;
            MinimumBlocksZ = minimumBlocksZ;
        }
    }

    /// <summary>
    /// Backend-neutral texture storage shape used for capability, source-layout, and staging-capacity
    /// planning before a Unity texture exists. Validation is performed by the operation consuming it.
    /// </summary>
    public readonly struct GpuUploadResourceDescription
    {
        /// <summary>Exact stable storage format.</summary>
        public readonly GpuUploadFormat Format;
        /// <summary>Storage dimension and subresource organization.</summary>
        public readonly GpuUploadDimension Dimension;
        /// <summary>Color texture or depth/stencil native resource.</summary>
        public readonly GpuUploadResourceKind ResourceKind;
        /// <summary>Base-mip width in texels.</summary>
        public readonly int Width;
        /// <summary>Base-mip height in texels.</summary>
        public readonly int Height;
        /// <summary>Base-mip depth for a volume; one for non-volume textures.</summary>
        public readonly int Depth;
        /// <summary>Array-layer or cube-face count; one for non-array textures.</summary>
        public readonly int Layers;
        /// <summary>Declared mip window: the levels addressable by regions. The physical resource may hold more.</summary>
        public readonly int MipCount;
        /// <summary>Sample count; regional uploads require one.</summary>
        public readonly int SampleCount;

        /// <summary>Creates an immutable storage shape for allocation-free planning and validation.</summary>
        public GpuUploadResourceDescription(GpuUploadFormat format,
            GpuUploadDimension dimension, GpuUploadResourceKind resourceKind,
            int width, int height, int depth, int layers, int mipCount, int sampleCount = 1)
        {
            Format = format;
            Dimension = dimension;
            ResourceKind = resourceKind;
            Width = width;
            Height = height;
            Depth = depth;
            Layers = layers;
            MipCount = mipCount;
            SampleCount = sampleCount;
        }

        /// <summary>Describes one two-dimensional mip chain with no array layers.</summary>
        public static GpuUploadResourceDescription ForTexture2D(GpuUploadFormat format,
            int width, int height, int mipCount = 1, int sampleCount = 1,
            GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture) =>
            new GpuUploadResourceDescription(format, GpuUploadDimension.Texture2D,
                resourceKind, width, height, 1, 1, mipCount, sampleCount);

        /// <summary>Describes consecutive two-dimensional array layers sharing one mip chain.</summary>
        public static GpuUploadResourceDescription ForTexture2DArray(GpuUploadFormat format,
            int width, int height, int layers, int mipCount = 1, int sampleCount = 1,
            GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture) =>
            new GpuUploadResourceDescription(format, GpuUploadDimension.Texture2DArray,
                resourceKind, width, height, 1, layers, mipCount, sampleCount);

        /// <summary>Describes one three-dimensional volume mip chain.</summary>
        public static GpuUploadResourceDescription ForTexture3D(GpuUploadFormat format,
            int width, int height, int depth, int mipCount = 1, int sampleCount = 1,
            GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture) =>
            new GpuUploadResourceDescription(format, GpuUploadDimension.Texture3D,
                resourceKind, width, height, depth, 1, mipCount, sampleCount);

        /// <summary>Describes one six-face cube mip chain.</summary>
        public static GpuUploadResourceDescription ForCube(GpuUploadFormat format,
            int size, int mipCount = 1, int sampleCount = 1,
            GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture) =>
            new GpuUploadResourceDescription(format, GpuUploadDimension.Cube,
                resourceKind, size, size, 1, 6, mipCount, sampleCount);

        /// <summary>
        /// Describes consecutive six-face cubes. Invalid or overflowing cube counts produce an
        /// invalid description reported by the subsequent capability or layout operation.
        /// </summary>
        public static GpuUploadResourceDescription ForCubeArray(GpuUploadFormat format,
            int size, int cubeCount, int mipCount = 1, int sampleCount = 1,
            GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture) =>
            new GpuUploadResourceDescription(format, GpuUploadDimension.CubeArray,
                resourceKind, size, size, 1,
                cubeCount > 0 && cubeCount <= int.MaxValue / 6 ? cubeCount * 6 : 0,
                mipCount, sampleCount);
    }

    /// <summary>
    /// Detailed support request. Zero storage dimensions request format-level capabilities;
    /// positive values request validation for one concrete storage shape.
    /// </summary>
    public struct GpuUploadSupportQuery
    {
        /// <summary>Unity storage format to map exactly into the upload ABI.</summary>
        public GraphicsFormat Format;
        /// <summary>Unity texture dimension to map exactly into the upload ABI.</summary>
        public TextureDimension Dimension;
        /// <summary>Logical native resource being queried.</summary>
        public GpuUploadResourceKind ResourceKind;
        /// <summary>Independently addressable aspect being queried.</summary>
        public GpuUploadAspect Aspect;
        /// <summary>Base width, or zero for a format-level query.</summary>
        public int Width;
        /// <summary>Base height, or zero for a format-level query.</summary>
        public int Height;
        /// <summary>Base volume depth, or zero for a format-level query.</summary>
        public int Depth;
        /// <summary>Array-layer or cube-face count, or zero for a format-level query.</summary>
        public int Layers;
        /// <summary>Declared mip window, or zero for a format-level query.</summary>
        public int MipCount;
        /// <summary>Sample count; one is required by regional upload targets.</summary>
        public int SampleCount;

        /// <summary>Creates a format-level query without committing to concrete storage dimensions.</summary>
        public static GpuUploadSupportQuery ForFormat(GraphicsFormat format,
            TextureDimension dimension, GpuUploadResourceKind resourceKind = GpuUploadResourceKind.Texture,
            GpuUploadAspect aspect = GpuUploadAspect.Color)
        {
            return new GpuUploadSupportQuery
            {
                Format = format,
                Dimension = dimension,
                ResourceKind = resourceKind,
                Aspect = aspect,
                SampleCount = 1
            };
        }
    }

    /// <summary>Exact active-backend capabilities for one support query.</summary>
    public readonly struct GpuUploadSupportInfo
    {
        /// <summary>Whether the requested combination can be registered and uploaded.</summary>
        public readonly bool Supported;
        /// <summary>Stable format selected by the request.</summary>
        public readonly GpuUploadFormat Format;
        /// <summary>Stable dimension selected by the request.</summary>
        public readonly GpuUploadDimension Dimension;
        /// <summary>Logical native resource selected by the request.</summary>
        public readonly GpuUploadResourceKind ResourceKind;
        /// <summary>Aspect selected by the request.</summary>
        public readonly GpuUploadAspect Aspect;
        /// <summary>All aspects the backend can address, or none when this combination is unsupported.</summary>
        public readonly GpuUploadAspectMask SupportedAspects;
        /// <summary>Aspect-specific storage geometry.</summary>
        public readonly GpuUploadFormatLayout Layout;
        /// <summary>Regional restrictions and backend fast paths; none when unsupported.</summary>
        public readonly GpuUploadSupportFlags Flags;
        /// <summary>
        /// Alignment imposed on physical upload-footprint row pitches, or zero when unsupported. Caller memory is byte
        /// addressed and its pitches need only satisfy block-row minimums because GpuUpload
        /// repacks when required.
        /// </summary>
        public readonly int StagingRowPitchAlignment;
        /// <summary>Alignment of each independently placed physical staging footprint, or zero when unsupported.</summary>
        public readonly int StagingOffsetAlignment;
        /// <summary>Whether padded caller rows require repacking on this backend.</summary>
        public bool RequiresPaddedRowRepack =>
            (Flags & GpuUploadSupportFlags.PaddedRowRepack) != 0;

        internal GpuUploadSupportInfo(bool supported, GpuUploadFormat format,
            GpuUploadDimension dimension, GpuUploadResourceKind resourceKind,
            GpuUploadAspect aspect, GpuUploadAspectMask supportedAspects,
            in GpuUploadFormatLayout layout, GpuUploadSupportFlags flags,
            int stagingRowPitchAlignment, int stagingOffsetAlignment)
        {
            Supported = supported;
            Format = format;
            Dimension = dimension;
            ResourceKind = resourceKind;
            Aspect = aspect;
            SupportedAspects = supported
                ? supportedAspects
                : GpuUploadAspectMask.None;
            Layout = layout;
            Flags = supported ? flags : GpuUploadSupportFlags.None;
            StagingRowPitchAlignment = supported ? stagingRowPitchAlignment : 0;
            StagingOffsetAlignment = supported ? stagingOffsetAlignment : 0;
        }
    }

    /// <summary>Validated source and physical-staging geometry for one region.</summary>
    public readonly struct GpuUploadRegionLayout
    {
        /// <summary>Encoded block columns touched by the source region.</summary>
        public readonly int BlockColumns;
        /// <summary>Encoded block rows touched by the source region.</summary>
        public readonly int BlockRows;
        /// <summary>Encoded block slices touched by the source region.</summary>
        public readonly int BlockSlices;
        /// <summary>Meaningful source bytes in each block row.</summary>
        public readonly ulong RowBytes;
        /// <summary>Resolved caller pitch between block rows.</summary>
        public readonly ulong SourceRowPitch;
        /// <summary>Resolved caller pitch between block slices.</summary>
        public readonly ulong SourceImagePitch;
        /// <summary>Resolved caller pitch between array layers or cube faces.</summary>
        public readonly ulong SourceLayerPitch;
        /// <summary>Caller bytes spanned from the first block through the last meaningful block.</summary>
        public readonly ulong SourceSpanBytes;
        /// <summary>Meaningful encoded bytes excluding caller pitch and backend padding.</summary>
        public readonly ulong PayloadBytes;
        /// <summary>Physical backend row pitch after required staging alignment.</summary>
        public readonly ulong StagingRowPitch;
        /// <summary>Physical backend pitch between block slices.</summary>
        public readonly ulong StagingImagePitch;
        /// <summary>Physical backend stride between separately placed layers.</summary>
        public readonly ulong StagingLayerPitch;
        /// <summary>Physical staging bytes consumed when this region begins at an aligned offset.</summary>
        public readonly ulong StagingBytes;
        /// <summary>Whether non-tight caller pitches force a backend repack.</summary>
        public readonly bool RequiresPaddedRowRepack;

        internal GpuUploadRegionLayout(int blockColumns, int blockRows, int blockSlices,
            ulong rowBytes, ulong sourceRowPitch, ulong sourceImagePitch,
            ulong sourceLayerPitch, ulong sourceSpanBytes, ulong payloadBytes,
            ulong stagingRowPitch, ulong stagingImagePitch, ulong stagingLayerPitch,
            ulong stagingBytes, bool requiresPaddedRowRepack)
        {
            BlockColumns = blockColumns;
            BlockRows = blockRows;
            BlockSlices = blockSlices;
            RowBytes = rowBytes;
            SourceRowPitch = sourceRowPitch;
            SourceImagePitch = sourceImagePitch;
            SourceLayerPitch = sourceLayerPitch;
            SourceSpanBytes = sourceSpanBytes;
            PayloadBytes = payloadBytes;
            StagingRowPitch = stagingRowPitch;
            StagingImagePitch = stagingImagePitch;
            StagingLayerPitch = stagingLayerPitch;
            StagingBytes = stagingBytes;
            RequiresPaddedRowRepack = requiresPaddedRowRepack;
        }
    }

    /// <summary>Stable validation, lifecycle, and backend result codes.</summary>
    public enum GpuUploadError
    {
        /// <summary>The operation completed without a validation or backend error.</summary>
        None = 0,
        /// <summary>The graphics backend or upload session is not initialized.</summary>
        NotInitialized = 1,
        /// <summary>Managed and backend contracts do not match exactly.</summary>
        AbiMismatch = 2,
        /// <summary>No delivery implementation is available: the active Unity renderer is unsupported, or the native binding could not be loaded.</summary>
        UnsupportedBackend = 3,
        /// <summary>The backend exists but does not implement the requested operation.</summary>
        UnsupportedFeature = 4,
        /// <summary>An argument is null, empty, out of range, or otherwise invalid.</summary>
        InvalidArgument = 5,
        /// <summary>A descriptor or batch violates the canonical ABI layout.</summary>
        InvalidLayout = 6,
        /// <summary>The Unity texture storage cannot be registered safely.</summary>
        UnsupportedTexture = 7,
        /// <summary>The requested storage dimension is unavailable on this backend.</summary>
        UnsupportedDimension = 8,
        /// <summary>The exact texel format and dimension pair is unavailable.</summary>
        UnsupportedFormat = 9,
        /// <summary>Regional upload requires single-sample texture storage.</summary>
        UnsupportedSampleCount = 10,
        /// <summary>No live registration matches the supplied target token.</summary>
        TargetNotFound = 11,
        /// <summary>The registration belongs to different texture storage or device epoch.</summary>
        TargetStale = 12,
        /// <summary>An outstanding target operation prevents the request.</summary>
        TargetBusy = 13,
        /// <summary>The target no longer accepts new submissions.</summary>
        TargetClosing = 14,
        /// <summary>A region's slot offset and span do not fit the declared written bytes, or the written bytes exceed the slot capacity.</summary>
        SourceOutOfRange = 15,
        /// <summary>The upload slot ring or batch builder capacity is temporarily exhausted; drain a ticket and retry.</summary>
        Backpressure = 16,
        /// <summary>The graphics device was lost or recreated during the operation.</summary>
        DeviceLost = 17,
        /// <summary>The backend rejected or failed while encoding destination commands.</summary>
        BackendFailed = 18,
        /// <summary>The WebGL context was lost during the operation.</summary>
        ContextLost = 19,
        /// <summary>The WebGL backend could not restore shared GL state.</summary>
        StateRestoreFailed = 20,
        /// <summary>A required managed or native allocation could not be satisfied.</summary>
        OutOfMemory = 21,
        /// <summary>The ticket expired from bounded status history or never existed.</summary>
        SubmissionNotFound = 22,
        /// <summary>Ordered retirement was already requested.</summary>
        SubmissionClosing = 23,
        /// <summary>An invariant failed without a more specific recoverable result.</summary>
        InternalError = 24,
        /// <summary>No live or retained aggregate sequence matches the supplied identity.</summary>
        SequenceNotFound = 25,
        /// <summary>The sequence is sealed, aborted, failed, or closing and accepts no new batches.</summary>
        SequenceClosing = 26,
        /// <summary>A batch reached the render thread outside the sequence's strict admission order.</summary>
        SequenceOrder = 27,
        /// <summary>No live slot matches the supplied slot identity and generation.</summary>
        SlotNotFound = 28,
        /// <summary>The slot exists but is not in the acquired state.</summary>
        SlotBusy = 29
    }

    /// <summary>Admission pipeline stage that produced a submission rejection.</summary>
    public enum GpuUploadAdmissionStage : uint
    {
        /// <summary>The batch was accepted.</summary>
        None = 0,
        /// <summary>Blob structure, magic, ABI version, or table layout validation.</summary>
        Layout = 1,
        /// <summary>Slot identity, generation, state, written bytes, or device epoch validation.</summary>
        Slot = 2,
        /// <summary>Target lookup, generation, epoch, or metadata validation.</summary>
        Target = 3,
        /// <summary>Per-region validation; the failed region index is attributed.</summary>
        Region = 4,
        /// <summary>Publication-marker rules; the failed region index is attributed.</summary>
        Marker = 5,
        /// <summary>Overlap policy validation; the failed region index is attributed.</summary>
        Overlap = 6,
        /// <summary>Backend staging or encode-capacity refusal.</summary>
        Backend = 7
    }

    /// <summary>Render-thread lifecycle of one accepted upload or maintenance request.</summary>
    public enum GpuUploadSubmissionState : uint
    {
        /// <summary>No retained submission state.</summary>
        Invalid = 0,
        /// <summary>Accepted but not yet encoded by the render thread.</summary>
        Pending = 1,
        /// <summary>Successfully encoded into the graphics stream.</summary>
        Encoded = 2,
        /// <summary>Rejected before any target content changed.</summary>
        Rejected = 3,
        /// <summary>Encoding failed and content status must be consulted.</summary>
        BackendFailed = 4,
        /// <summary>Interrupted by graphics-device loss or recreation.</summary>
        DeviceLost = 5,
        /// <summary>Cancelled without encoding target writes.</summary>
        Cancelled = 6,
        /// <summary>Closed and no longer owns native submission resources.</summary>
        Retired = 7
    }

    /// <summary>Optional backend completion state; encoded command order does not require it.</summary>
    public enum GpuUploadGpuState : uint
    {
        /// <summary>The backend cannot report physical GPU completion.</summary>
        Unsupported = 0,
        /// <summary>GPU work may still reference staging or target resources.</summary>
        Pending = 1,
        /// <summary>GPU work and resource references have completed.</summary>
        Complete = 2,
        /// <summary>Physical completion tracking failed.</summary>
        Failed = 3
    }

    /// <summary>Whether a submission changed, may have changed, or did not touch its targets.</summary>
    public enum GpuUploadContentState : uint
    {
        /// <summary>No target texel was modified.</summary>
        Unchanged = 0,
        /// <summary>All requested destination commands were encoded; consult GPU state for physical completion.</summary>
        Changed = 1,
        /// <summary>A partial write or uncertain backend failure may have modified content.</summary>
        MayHaveChanged = 2
    }

    /// <summary>Whether a built batch crossed the submission boundary.</summary>
    public enum GpuUploadAdmission : byte
    {
        /// <summary>The builder remains caller-controlled and the upload slot stays acquired; only a rejection proving the slot identity dead (<see cref="GpuUploadError.SlotNotFound"/> at the slot stage) invalidates the handle.</summary>
        NotAdmitted = 0,
        /// <summary>GpuUpload accepted the batch and took ownership of the submitted slot; the caller's slot handle is invalid.</summary>
        Admitted = 1,
        /// <summary>
        /// The submission boundary or a fatal teardown invalidated the builder before admission
        /// could be represented by a ticket. The upload slot must be treated as consumed and
        /// cannot be rewritten or resubmitted by the caller.
        /// </summary>
        SessionAbandoned = 2
    }

    /// <summary>Controls validation and ordering when destination regions overlap.</summary>
    public enum GpuUploadOverlapPolicy : uint
    {
        /// <summary>Rejects overlapping destination regions before encoding.</summary>
        ValidateNonOverlapping = 0,
        /// <summary>Skips overlap checks; the caller guarantees that destination regions do not overlap.</summary>
        AssumeNonOverlapping = 1,
        /// <summary>Preserves region-list order for overlapping destination writes.</summary>
        OrderedOverlaps = 2
    }

    /// <summary>Optional batch behavior that is independent from region ordering.</summary>
    [Flags]
    public enum GpuUploadBatchOptions : uint
    {
        /// <summary>Uses the backend's normal error-observation policy.</summary>
        None = 0,
        /// <summary>
        /// Observes the process-global GL error channel around the batch. A pre-existing error
        /// from unrelated graphics code is consumed and discarded, never attributed to the batch;
        /// the verdict reflects only the batch's own commands.
        /// </summary>
        ObserveSharedGlErrors = 1 << 0
    }

    /// <summary>Controls Unity texture update-count publication for command-buffer recordings.</summary>
    [Flags]
    public enum GpuUploadRecordOptions : uint
    {
        /// <summary>
        /// Records an ordered update-count command. Every target in the batch must be a
        /// <see cref="RenderTexture"/>.
        /// </summary>
        None = 0,
        /// <summary>
        /// Records no Unity update-count command. Supports every registered texture type; after
        /// scheduling the command buffer for its one execution, the caller must invoke
        /// <see cref="GpuUploadRecordResult.TryPublishUpdateCounts"/> in the same frame.
        /// </summary>
        CallerManagedUpdateCount = 1 << 0
    }

    /// <summary>Registration lifecycle for one immutable texture storage generation.</summary>
    public enum GpuUploadTargetState : uint
    {
        /// <summary>No registration state.</summary>
        Invalid = 0,
        /// <summary>Available for new batches.</summary>
        Active = 1,
        /// <summary>Rejects new batches while existing leases drain.</summary>
        Closing = 2,
        /// <summary>Native target references have been released normally.</summary>
        Retired = 3,
        /// <summary>Invalidated by storage or graphics-device epoch change; native references are released.</summary>
        Stale = 4
    }

    /// <summary>Operations implemented by the active backend and graphics-device epoch.</summary>
    [Flags]
    public enum GpuUploadCapabilities : uint
    {
        /// <summary>No optional backend capability.</summary>
        None = 0,
        /// <summary>Upload events can be recorded into caller-owned command buffers.</summary>
        CommandBuffer = 1 << 0,
        /// <summary>One-shot uploads can be submitted directly.</summary>
        Immediate = 1 << 1,
        /// <summary>A non-blocking graphics-stream submission boundary is available.</summary>
        SubmissionBoundary = 1 << 2,
        /// <summary>One batch may update multiple registered targets.</summary>
        MultiTarget = 1 << 4,
        /// <summary>Regions may address any mip level within the declared window.</summary>
        ExplicitMips = 1 << 5,
        /// <summary>The backend reports physical GPU completion.</summary>
        GpuCompletion = 1 << 6,
        /// <summary>An idle registration may re-resolve the same Unity texture object.</summary>
        TargetRefresh = 1 << 7,
        /// <summary>Pending physical completions advance through the dedicated render-thread poll event.</summary>
        CompletionPoll = 1 << 8
    }

    /// <summary>Capabilities and backend limits for one graphics-device epoch.</summary>
    public readonly struct GpuUploadDeviceInfo
    {
        /// <summary>Exact ABI major implemented by the active backend.</summary>
        public readonly int AbiMajor;
        /// <summary>Exact ABI minor implemented by the active backend.</summary>
        public readonly int AbiMinor;
        /// <summary>Unity graphics API owning the current device epoch.</summary>
        public readonly GraphicsDeviceType Renderer;
        /// <summary>Operations exposed by the active backend.</summary>
        public readonly GpuUploadCapabilities Capabilities;
        /// <summary>Monotonic identity invalidated whenever Unity recreates the graphics device.</summary>
        public readonly ulong GraphicsDeviceEpoch;
        /// <summary>
        /// Per-submission admission ceiling: the largest slot payload and backend staging
        /// footprint (payload plus row and placement repacking) one submission may require.
        /// Acquisition and admission refuse anything larger. Together with
        /// <see cref="MaxRegionsPerBatch"/> it is the chunk-planning bound — consumers never
        /// split a flush by the slot class, so delivery cannot depend on mid-frame GPU
        /// retirement; <see cref="SlotBytes"/> only classifies which acquisitions the resident
        /// ring serves without a transient allocation.
        /// </summary>
        public readonly ulong MaxStagingBytes;
        /// <summary>Current number of native submissions that may own staging concurrently.</summary>
        public readonly int MaxConcurrentSubmissions;
        /// <summary>Byte capacity of a standard upload slot; larger acquisitions allocate transient slots.</summary>
        public readonly int SlotBytes;
        /// <summary>Maximum regional writes accepted in one submission.</summary>
        public readonly int MaxRegionsPerBatch;

        internal GpuUploadDeviceInfo(in GpuUploadAbi.DeviceInfo value)
        {
            AbiMajor = value.abiMajor;
            AbiMinor = value.abiMinor;
            Renderer = SystemInfo.graphicsDeviceType;
            Capabilities = (GpuUploadCapabilities)value.flags;
            GraphicsDeviceEpoch = value.graphicsDeviceEpoch;
            MaxStagingBytes = value.maxStagingBytes;
            MaxConcurrentSubmissions = checked((int)value.maxConcurrentSubmissions);
            SlotBytes = checked((int)value.slotBytes);
            MaxRegionsPerBatch = checked((int)value.maxRegionsPerSubmission);
        }
    }

    /// <summary>Declared target metadata; the backend re-validates before any write that the physical resource holds this shape (mip count as a window, everything else exact).</summary>
    public readonly struct GpuUploadTargetInfo
    {
        /// <summary>Base-mip width in texels.</summary>
        public readonly int Width;
        /// <summary>Base-mip height in texels.</summary>
        public readonly int Height;
        /// <summary>Base-mip depth for a volume; one for non-volume textures.</summary>
        public readonly int Depth;
        /// <summary>Array-layer or cube-face count; one for non-array textures.</summary>
        public readonly int Layers;
        /// <summary>Declared mip window; the physical resource may hold more levels.</summary>
        public readonly int MipCount;
        /// <summary>Exact realized storage format.</summary>
        public readonly GpuUploadFormat Format;
        /// <summary>Realized storage dimension.</summary>
        public readonly GpuUploadDimension Dimension;
        /// <summary>Sample count; registered upload targets always use one sample.</summary>
        public readonly int SampleCount;
        /// <summary>Logical native resource represented by this registration.</summary>
        public readonly GpuUploadResourceKind ResourceKind;
        /// <summary>Aspect selected by the registration request as its primary operation.</summary>
        public readonly GpuUploadAspect PrimaryAspect;
        /// <summary>Aspects of this registered resource confirmed by the active backend.</summary>
        public readonly GpuUploadAspectMask SupportedAspects;
        /// <summary>Whether Unity also retains CPU-readable storage that direct GPU writes do not update.</summary>
        public readonly bool CpuReadable;
        /// <summary>Backend-neutral storage shape suitable for layout and capacity planning.</summary>
        public GpuUploadResourceDescription Description => new GpuUploadResourceDescription(
            Format, Dimension, ResourceKind, Width, Height, Depth, Layers, MipCount, SampleCount);

        internal GpuUploadTargetInfo(int width, int height, int depth, int layers, int mipCount,
            GpuUploadFormat format, GpuUploadDimension dimension, int sampleCount,
            GpuUploadResourceKind resourceKind, GpuUploadAspect primaryAspect,
            GpuUploadAspectMask supportedAspects, bool cpuReadable)
        {
            Width = width;
            Height = height;
            Depth = depth;
            Layers = layers;
            MipCount = mipCount;
            Format = format;
            Dimension = dimension;
            SampleCount = sampleCount;
            ResourceKind = resourceKind;
            PrimaryAspect = primaryAspect;
            SupportedAspects = supportedAspects;
            CpuReadable = cpuReadable;
        }

        internal GpuUploadTargetInfo WithSupportedAspects(GpuUploadAspectMask supportedAspects) =>
            new GpuUploadTargetInfo(Width, Height, Depth, Layers, MipCount, Format, Dimension,
                SampleCount, ResourceKind, PrimaryAspect, supportedAspects, CpuReadable);
    }

    /// <summary>Latest retained state and result for one upload ticket.</summary>
    public readonly struct GpuUploadStatus
    {
        /// <summary>Render-thread submission lifecycle.</summary>
        public readonly GpuUploadSubmissionState State;
        /// <summary>Physical completion state when supported by the backend.</summary>
        public readonly GpuUploadGpuState GpuState;
        /// <summary>Strongest guarantee about target content after processing.</summary>
        public readonly GpuUploadContentState ContentState;
        /// <summary>Validation or backend result code.</summary>
        public readonly GpuUploadError Error;
        /// <summary>
        /// Zero-based region associated with failure detection, or -1 when attribution is outside
        /// the region stream or unknown. Deferred graphics errors may identify the last attempted
        /// region rather than the command that originally caused the error.
        /// </summary>
        public readonly int FailedRegion;
        /// <summary>Backend diagnostic code; guarded GL/WebGL paths preserve the first observed GL error.</summary>
        public readonly uint BackendDetail;
        /// <summary>Whether the native callback no longer reads any attached source bytes.</summary>
        public readonly bool SourceConsumed;
        /// <summary>Whether managed code requested ordered submission retirement.</summary>
        public readonly bool CloseRequested;
        /// <summary>Whether the render thread observed the ordered retirement event.</summary>
        public readonly bool RetireObserved;
        internal GpuUploadStatus(in GpuUploadAbi.SubmissionStatus value)
        {
            State = (GpuUploadSubmissionState)value.state;
            GpuState = (GpuUploadGpuState)value.gpuState;
            ContentState = (GpuUploadContentState)value.contentState;
            Error = (GpuUploadError)value.resultCode;
            FailedRegion = value.failedRegion;
            BackendDetail = value.backendDetail;
            SourceConsumed =
                (value.flags & (uint)GpuUploadAbi.StatusFlags.SourceConsumed) != 0;
            CloseRequested =
                (value.flags & (uint)GpuUploadAbi.StatusFlags.CloseRequested) != 0;
            RetireObserved =
                (value.flags & (uint)GpuUploadAbi.StatusFlags.RetireObserved) != 0;
        }

        internal GpuUploadStatus(GpuUploadSubmissionState state, GpuUploadGpuState gpuState,
            GpuUploadContentState contentState, GpuUploadError error, int failedRegion,
            uint backendDetail, bool sourceConsumed, bool closeRequested, bool retireObserved)
        {
            State = state;
            GpuState = gpuState;
            ContentState = contentState;
            Error = error;
            FailedRegion = failedRegion;
            BackendDetail = backendDetail;
            SourceConsumed = sourceConsumed;
            CloseRequested = closeRequested;
            RetireObserved = retireObserved;
        }
    }

    /// <summary>Submission counters and a current snapshot of upload-slot ring pressure.</summary>
    public readonly struct GpuUploadStats
    {
        /// <summary>Batches admitted into native or WebGL processing.</summary>
        public readonly ulong SubmissionsAccepted;
        /// <summary>Batches rejected by validation or bounded admission before backend encoding began.</summary>
        public readonly ulong SubmissionsRejected;
        /// <summary>Batches whose complete destination command list was encoded.</summary>
        public readonly ulong SubmissionsEncoded;
        /// <summary>Repeated render callbacks suppressed by one-shot claiming.</summary>
        public readonly ulong DuplicateCallbacks;
        /// <summary>Callbacks rejected because their route or device epoch was stale.</summary>
        public readonly ulong StaleCallbacks;
        /// <summary>Admissions rejected by bounded pool capacity.</summary>
        public readonly ulong BackpressureCount;
        /// <summary>
        /// Meaningful destination texel bytes in fully encoded batches. Excludes source pitch and
        /// backend padding and does not imply physical GPU completion.
        /// </summary>
        public readonly ulong EncodedPayloadBytes;
        /// <summary>Upload slots currently resident, including transient oversized slots.</summary>
        public readonly ulong PoolNodes;
        /// <summary>Free standard slots immediately available for acquisition.</summary>
        public readonly ulong PoolNodesFree;
        /// <summary>Slots held acquired by callers or retained by submitted GPU work.</summary>
        public readonly ulong PoolNodesInFlight;
        /// <summary>Total byte capacity of all currently resident upload slots.</summary>
        public readonly ulong PoolStagingCapacityBytes;
        /// <summary>Byte capacity of free standard slots.</summary>
        public readonly ulong PoolStagingFreeBytes;
        /// <summary>Byte capacity of slots held acquired by callers or retained by submitted GPU work.</summary>
        public readonly ulong PoolStagingInFlightBytes;

        internal GpuUploadStats(in GpuUploadAbi.Stats value)
        {
            SubmissionsAccepted = value.submissionsAccepted;
            SubmissionsRejected = value.submissionsRejected;
            SubmissionsEncoded = value.submissionsEncoded;
            DuplicateCallbacks = value.duplicateCallbacks;
            StaleCallbacks = value.staleCallbacks;
            BackpressureCount = value.backpressureCount;
            EncodedPayloadBytes = value.encodedPayloadBytes;
            PoolNodes = value.poolNodes;
            PoolNodesFree = value.poolNodesFree;
            PoolNodesInFlight = value.poolNodesInFlight;
            PoolStagingCapacityBytes = value.poolStagingCapacityBytes;
            PoolStagingFreeBytes = value.poolStagingFreeBytes;
            PoolStagingInFlightBytes = value.poolStagingInFlightBytes;
        }
    }

    /// <summary>
    /// One explicit mip/subresource region. Slot offsets and pitches are bytes relative to the
    /// start of the upload slot submitted with the batch; a zero pitch selects the tightly
    /// packed value.
    /// </summary>
    public struct GpuUploadRegion
    {
        /// <summary>Destination mip level.</summary>
        public int MipLevel;
        /// <summary>Destination aspect; color is the zero-initialized default.</summary>
        public GpuUploadAspect Aspect;
        /// <summary>Destination x coordinate in mip texels.</summary>
        public int DestinationX;
        /// <summary>Destination y coordinate in mip texels.</summary>
        public int DestinationY;
        /// <summary>Destination z coordinate in mip texels for a volume.</summary>
        public int DestinationZ;
        /// <summary>Copied width in texels.</summary>
        public int Width;
        /// <summary>Copied height in texels.</summary>
        public int Height;
        /// <summary>Copied depth in texels for a volume; one for non-volume textures.</summary>
        public int Depth;
        /// <summary>First destination array layer or cube face.</summary>
        public int BaseLayer;
        /// <summary>Number of consecutive array layers or cube faces.</summary>
        public int LayerCount;
        /// <summary>Byte offset from the start of the submitted upload slot; at most <see cref="int.MaxValue"/>.</summary>
        public long SlotOffset;
        /// <summary>Bytes between source rows; zero selects tightly packed rows.</summary>
        public int SourceRowPitch;
        /// <summary>Bytes between source depth slices; zero selects tightly packed images.</summary>
        public int SourceImagePitch;
        /// <summary>Bytes between source array layers; zero selects tightly packed layers.</summary>
        public int SourceLayerPitch;

        /// <summary>Creates one 2D region whose zero derived pitches are resolved from the target format.</summary>
        public static GpuUploadRegion ForTexture2D(int mipLevel, int x, int y, int width, int height,
            long slotOffset = 0, int sourceRowPitch = 0,
            GpuUploadAspect aspect = GpuUploadAspect.Color)
        {
            return new GpuUploadRegion
            {
                MipLevel = mipLevel,
                Aspect = aspect,
                DestinationX = x,
                DestinationY = y,
                Width = width,
                Height = height,
                Depth = 1,
                LayerCount = 1,
                SlotOffset = slotOffset,
                SourceRowPitch = sourceRowPitch
            };
        }

        /// <summary>Creates one region spanning consecutive array layers or cube faces.</summary>
        public static GpuUploadRegion ForLayers(int mipLevel, int baseLayer, int layerCount,
            int x, int y, int width, int height, long slotOffset = 0, int sourceRowPitch = 0,
            int sourceLayerPitch = 0, GpuUploadAspect aspect = GpuUploadAspect.Color)
        {
            return new GpuUploadRegion
            {
                MipLevel = mipLevel,
                Aspect = aspect,
                DestinationX = x,
                DestinationY = y,
                Width = width,
                Height = height,
                Depth = 1,
                BaseLayer = baseLayer,
                LayerCount = layerCount,
                SlotOffset = slotOffset,
                SourceRowPitch = sourceRowPitch,
                SourceLayerPitch = sourceLayerPitch
            };
        }

        /// <summary>Creates one 3D region whose zero derived pitches are resolved from the target format.</summary>
        public static GpuUploadRegion ForTexture3D(int mipLevel, int x, int y, int z, int width,
            int height, int depth, long slotOffset = 0, int sourceRowPitch = 0,
            int sourceImagePitch = 0,
            GpuUploadAspect aspect = GpuUploadAspect.Color)
        {
            return new GpuUploadRegion
            {
                MipLevel = mipLevel,
                Aspect = aspect,
                DestinationX = x,
                DestinationY = y,
                DestinationZ = z,
                Width = width,
                Height = height,
                Depth = depth,
                LayerCount = 1,
                SlotOffset = slotOffset,
                SourceRowPitch = sourceRowPitch,
                SourceImagePitch = sourceImagePitch
            };
        }
    }

    /// <summary>Allocation-free identity for a retained submission result within one device epoch.</summary>
    public readonly struct GpuUploadTicket
    {
        internal readonly ulong sessionGeneration;
        internal readonly ulong epoch;
        internal readonly ulong serial;
        internal readonly int historyIndex;
        internal readonly int builder;
        internal readonly ulong builderGeneration;

        internal GpuUploadTicket(ulong sessionGeneration, ulong epoch, ulong serial,
            int historyIndex, int builder, ulong builderGeneration)
        {
            this.sessionGeneration = sessionGeneration;
            this.epoch = epoch;
            this.serial = serial;
            this.historyIndex = historyIndex;
            this.builder = builder;
            this.builderGeneration = builderGeneration;
        }

        /// <summary>Monotonic submission identity within the ticket's device epoch.</summary>
        public ulong Serial => serial;
        /// <summary>Whether this value identifies a submission.</summary>
        public bool IsValid => sessionGeneration != 0 && serial != 0 && historyIndex >= 0
                               && builder >= 0 && builderGeneration != 0;

        /// <summary>Reads the retained result; false means invalid epoch or expired history.</summary>
        public bool TryGetStatus(out GpuUploadStatus status) => GpuUpload.TryGetStatus(this, out status);
    }

    /// <summary>Immediate outcome of submitting a one-shot batch.</summary>
    public readonly struct GpuUploadSubmitResult
    {
        /// <summary>Whether the batch crossed the submission boundary.</summary>
        public GpuUploadAdmission Admission { get; }
        /// <summary>Retained submission identity when admission succeeded.</summary>
        public GpuUploadTicket Ticket { get; }
        /// <summary>Strongest synchronous guarantee about destination content.</summary>
        public GpuUploadContentState ContentState { get; }
        /// <summary>Synchronous validation, admission, or scheduling result.</summary>
        public GpuUploadError Error { get; }
        /// <summary>Admission pipeline stage that rejected the batch, or None when accepted.</summary>
        public GpuUploadAdmissionStage Stage { get; }
        /// <summary>Zero-based region index attributed to the rejection, or -1 when attribution is not per-region.</summary>
        public int FailedRegion { get; }
        /// <summary>Whether ownership transferred and synchronous scheduling succeeded.</summary>
        public bool Succeeded => Admission == GpuUploadAdmission.Admitted
                                 && Error == GpuUploadError.None;

        internal GpuUploadSubmitResult(GpuUploadAdmission admission, GpuUploadTicket ticket,
            GpuUploadContentState contentState, GpuUploadError error,
            GpuUploadAdmissionStage stage = GpuUploadAdmissionStage.None, int failedRegion = -1)
        {
            Admission = admission;
            Ticket = ticket;
            ContentState = contentState;
            Error = error;
            Stage = stage;
            FailedRegion = failedRegion;
        }
    }

    /// <summary>
    /// Caller-held lease of writable native upload memory addressed by region slot offsets.
    /// The view remains valid until this exact slot generation is submitted or released,
    /// across device loss and graphics shutdown. Jobs may write into the view between
    /// acquisition and submission; acquire, submit, and release are main-thread operations.
    /// </summary>
    public readonly struct GpuUploadSlot
    {
        internal readonly uint id;
        internal readonly uint generation;
        internal readonly ulong sessionGeneration;
        private readonly NativeArray<byte> view;

        internal GpuUploadSlot(uint id, uint generation, ulong sessionGeneration,
            NativeArray<byte> view)
        {
            this.id = id;
            this.generation = generation;
            this.sessionGeneration = sessionGeneration;
            this.view = view;
        }

        /// <summary>Writable view over native slot memory; jobs may fill it until submission.</summary>
        public NativeArray<byte> View => view;
        /// <summary>Writable byte capacity of the slot.</summary>
        public int Capacity => view.Length;
        /// <summary>Whether this handle still identifies an acquired, unsubmitted slot of the current session.</summary>
        public bool IsValid => GpuUpload.IsSlotAcquired(this);
    }

    /// <summary>One-use allocation-free builder that preserves region insertion order.</summary>
    public partial struct GpuUploadBatch : IDisposable
    {
        internal int builder;
        internal ulong generation;

        internal GpuUploadBatch(int builder, ulong generation)
        {
            this.builder = builder;
            this.generation = generation;
        }

        /// <summary>Whether the builder is still mutable and has not crossed admission.</summary>
        public bool IsValid => GpuUpload.IsBatchBuilding(this);

        /// <summary>
        /// Validates region geometry and appends the region addressed by its slot offset. Ordered
        /// overlaps encode in this order, so the last write to an intersecting texel wins. The
        /// slot-offset-plus-span bound is enforced against the written bytes declared at submit.
        /// </summary>
        public bool TryAddRegion(GpuUploadTarget target, in GpuUploadRegion region,
            out GpuUploadError error) =>
            GpuUpload.TryAddRegion(this, target, region, out error);

        /// <summary>
        /// Submits the batch reading the first writtenBytes of the slot, before graphics commands
        /// issued later by the caller. Every job writing into the slot view must be complete. An
        /// admitted or abandoned submission consumes the slot and invalidates the handle; a
        /// rejected submission leaves the slot acquired for rewrite, resubmission, or release.
        /// </summary>
        public GpuUploadSubmitResult Submit(ref GpuUploadSlot slot, int writtenBytes) =>
            GpuUpload.Submit(this, ref slot, writtenBytes);

        /// <summary>
        /// Records the batch at the command buffer's current position for exactly one execution,
        /// consuming the slot exactly as an admitted submit does. Keep the result open while any
        /// owner can execute the buffer, then detach and clear the buffer before closing it. The
        /// default update-count mode supports RenderTexture targets; caller-managed mode supports
        /// every registered texture type.
        /// </summary>
        public GpuUploadRecordResult RecordOnce(ref GpuUploadSlot slot, int writtenBytes,
            CommandBuffer commandBuffer,
            GpuUploadRecordOptions options = GpuUploadRecordOptions.None) =>
            GpuUpload.RecordOnce(this, ref slot, writtenBytes, commandBuffer, options);

        /// <summary>Abandons a non-admitted builder; the upload slot remains caller-owned.</summary>
        public void Dispose()
        {
            GpuUpload.ReleaseBatch(this);
            this = default;
        }
    }

    /// <summary>Submission outcome and lifetime handle for a one-shot command buffer recording.</summary>
    public struct GpuUploadRecordResult
    {
        internal int builder;
        internal ulong generation;
        /// <summary>Whether the batch crossed the submission boundary.</summary>
        public GpuUploadAdmission Admission { get; }
        /// <summary>Retained submission identity when admission succeeded.</summary>
        public GpuUploadTicket Ticket { get; }
        /// <summary>Synchronous validation or command-recording result.</summary>
        public GpuUploadError Error { get; }
        /// <summary>Whether ownership transferred and command recording succeeded.</summary>
        public bool Succeeded => Admission == GpuUploadAdmission.Admitted
                                 && Error == GpuUploadError.None;

        internal GpuUploadRecordResult(int builder, ulong generation,
            GpuUploadAdmission admission, GpuUploadTicket ticket, GpuUploadError error)
        {
            this.builder = builder;
            this.generation = generation;
            Admission = admission;
            Ticket = ticket;
            Error = error;
        }

        /// <summary>Whether native event data must still outlive a recorded command buffer.</summary>
        public bool IsValid => GpuUpload.IsRecordingValid(this);
        /// <summary>Whether ordered retirement has already been requested.</summary>
        public bool IsClosing => GpuUpload.IsRecordingClosing(this);

        /// <summary>
        /// Immediately increments Unity's update counter once for every recorded target. This is
        /// available only for a caller-managed recording and does not wait for GPU completion.
        /// </summary>
        public bool TryPublishUpdateCounts() =>
            GpuUpload.TryPublishRecordingUpdateCounts(this);

        /// <summary>
        /// Queues ordered retirement. Detach the command buffer from every pipeline owner and
        /// clear its recorded upload event before calling this method.
        /// </summary>
        public void Close()
        {
            GpuUpload.CloseRecording(this);
            this = default;
        }
    }

    /// <summary>
    /// Registered immutable texture storage generation. <see cref="IsReleased"/> is required
    /// before explicit Unity destruction or release, but consumers must separately wait for every
    /// draw, compute, copy, or other GPU use they own.
    /// </summary>
    public sealed class GpuUploadTarget
    {
        internal GpuUploadAbi.TargetHandle handle;
        internal GpuUploadTargetState state;
        internal GpuUploadTargetInfo info;
        internal Texture texture;
        internal ulong epoch;
        internal uint abiFlags;
        internal GpuUploadTargetSupports supports;
        internal bool closeRequested;

        internal GpuUploadTarget(Texture texture, in GpuUploadTargetInfo info,
            in GpuUploadAbi.TargetHandle handle, ulong epoch, uint abiFlags,
            in GpuUploadTargetSupports supports)
        {
            this.texture = texture;
            this.info = info;
            this.handle = handle;
            this.epoch = epoch;
            this.abiFlags = abiFlags;
            this.supports = supports;
            state = GpuUploadTargetState.Active;
        }

        /// <summary>Unity object whose current immutable storage is registered.</summary>
        public Texture Texture => texture;
        /// <summary>Metadata captured and validated for this storage generation.</summary>
        public GpuUploadTargetInfo Info => info;
        /// <summary>Native storage generation used to reject stale handles.</summary>
        public uint Generation => handle.generation;
        /// <summary>Latest registration lifecycle state.</summary>
        public GpuUploadTargetState State => state;
        /// <summary>Returns the retained backend capability descriptor for one physical aspect.</summary>
        public bool TryGetSupport(GpuUploadAspect aspect, out GpuUploadSupportInfo supportInfo) =>
            supports.TryGet(aspect, out supportInfo);
        /// <summary>
        /// Whether the registration no longer requires the Unity texture object to remain alive.
        /// This does not prove completion of GPU work submitted by other consumers.
        /// </summary>
        public bool IsReleased => State == GpuUploadTargetState.Retired
                                  || State == GpuUploadTargetState.Stale;
        /// <summary>Re-resolves the same Unity object only when no GpuUpload lease is outstanding.</summary>
        public bool TryRefresh(out GpuUploadError error) => GpuUpload.TryRefreshTarget(this, out error);

        /// <summary>
        /// Closes registration. Keep the Unity texture alive until <see cref="IsReleased"/> and
        /// until all consumer-owned GPU uses have completed.
        /// </summary>
        public void Close() => GpuUpload.CloseTarget(this);
    }
}
