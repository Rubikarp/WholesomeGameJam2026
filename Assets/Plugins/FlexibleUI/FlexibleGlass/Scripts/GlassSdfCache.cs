using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace JeffGrawAssets.FlexibleUI
{
internal sealed class GlassSdfCache : IDisposable
{
    private const string ComputeResourceName = "FlexibleGlassSdf";
    private const int MaximumSpriteGenerationResolution = 2048;

    private static readonly int GenerationWidthId = Shader.PropertyToID("_GenerationWidth");
    private static readonly int GenerationHeightId = Shader.PropertyToID("_GenerationHeight");
    private static readonly int OutputResolutionId = Shader.PropertyToID("_OutputResolution");
    private static readonly int JumpId = Shader.PropertyToID("_Jump");
    private static readonly int ShapeSourceId = Shader.PropertyToID("_ShapeSource");
    private static readonly int ShapeTypeId = Shader.PropertyToID("_ShapeType");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int PaddingId = Shader.PropertyToID("_Padding");
    private static readonly int CornerRadiiId = Shader.PropertyToID("_CornerRadii");
    private static readonly int CornerShapeId = Shader.PropertyToID("_CornerShape");
    private static readonly int TextureUvId = Shader.PropertyToID("_TextureUv");
    private static readonly int AlphaThresholdId = Shader.PropertyToID("_AlphaThreshold");
    private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
    private static readonly int MaskReadId = Shader.PropertyToID("_MaskRead");
    private static readonly int MaskWriteId = Shader.PropertyToID("_MaskWrite");
    private static readonly int NormalReadId = Shader.PropertyToID("_NormalRead");
    private static readonly int NormalWriteId = Shader.PropertyToID("_NormalWrite");
    private static readonly int SeedReadId = Shader.PropertyToID("_SeedRead");
    private static readonly int SeedWriteId = Shader.PropertyToID("_SeedWrite");
    private static readonly int FieldWriteId = Shader.PropertyToID("_FieldWrite");
    private static readonly int FieldReadId = Shader.PropertyToID("_FieldRead");
    private static readonly int MipWriteId = Shader.PropertyToID("_MipWrite");
    private static readonly int FieldSliceId = Shader.PropertyToID("_FieldSlice");
    private static readonly int MipResolutionId = Shader.PropertyToID("_MipResolution");
    private static readonly int SourceMipId = Shader.PropertyToID("_SourceMip");
    private static readonly int SpriteTrianglesId = Shader.PropertyToID("_SpriteTriangles");
    private static readonly int SpriteTilesId = Shader.PropertyToID("_SpriteTiles");

    private sealed class Entry
    {
        public GlassSdfDescriptor descriptor;
        public int slice;
        public int lastUsedFrame;
        public bool evictionImmune;
        public GlassSdfRaycastField raycastField;
        public bool raycastQueued;
        public ComputeBuffer spriteTriangles;
        public ComputeBuffer spriteTiles;
        public Sprite meshSprite;
        public Texture meshTexture;
        public Vector4 meshUv;
        public uint meshTextureUpdateCount;

        public void ReleaseSpriteBuffers()
        {
            spriteTriangles?.Release();
            spriteTiles?.Release();
            spriteTriangles = spriteTiles = null;
            meshSprite = null;
            meshTexture = null;
        }
    }

    private readonly Dictionary<GlassSdfDescriptor, Entry> entries = new(8);
    private readonly Stack<int> freeSlices = new(8);
    private readonly Stack<Entry> freeEntries = new(8);
    private readonly List<Entry> pendingGenerations = new(8);
    private readonly List<Entry> pendingRaycasts = new(4);
    private readonly List<GlassSdfDescriptor> immuneDescriptors = new(8);
    private readonly List<GlassSpriteMesh.Triangle> spriteTriangles = new();
    private readonly List<Vector4> spriteTriangleBounds = new();
    private readonly List<GlassSpriteMesh.Triangle> tiledSpriteTriangles = new();
    private readonly Vector2Int[] spriteTiles = new Vector2Int[GlassSpriteMesh.TileCount * GlassSpriteMesh.TileCount];
    private int resolution;
    private int pendingResolution;
    private readonly ComputeShader compute;
    private readonly int rasterizeMaskKernel, rasterizeSpriteKernel, initializeKernel, jumpFloodKernel, finalizeKernel, generateMipKernel, filterNormalsHorizontalKernel, filterNormalsVerticalKernel;

    private RTHandleSystem handleSystem;
    private GlassSdfRaycastField.ReadbackResources readbackResources;
    private RTHandle fieldAtlas, mask, seedA, seedB, mipScratch, normalScratch;
    private Vector2Int scratchDimensions;
    private int capacity, currentFrame = -1;
    private bool warnedUnavailable;
    private bool warnedReadbackUnavailable;

    public bool IsAvailable => compute && SystemInfo.supportsComputeShaders;
    public bool HasResources => fieldAtlas != null && fieldAtlas.rt;
    public bool HasPendingJobs => pendingGenerations.Count > 0;
    public bool HasPendingRaycasts => pendingRaycasts.Count > 0;
    public RTHandle FieldAtlas => fieldAtlas;
    public RTHandle Mask => mask;
    public RTHandle SeedA => seedA;
    public RTHandle SeedB => seedB;
    public RTHandle MipScratch => mipScratch;
    public int Resolution => resolution;
    public int MaximumLod => (int)Mathf.Log(resolution, 2f);

    public GlassSdfCache(int resolution)
    {
        this.resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(resolution), 64, 1024);
        pendingResolution = this.resolution;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        compute = Resources.Load<ComputeShader>(ComputeResourceName);
        if (!compute)
            return;

        rasterizeMaskKernel = compute.FindKernel("RasterizeMask");
        rasterizeSpriteKernel = compute.FindKernel("RasterizeSpriteMesh");
        initializeKernel = compute.FindKernel("Initialize");
        jumpFloodKernel = compute.FindKernel("JumpFlood");
        finalizeKernel = compute.FindKernel("Finalize");
        generateMipKernel = compute.FindKernel("GenerateMip");
        filterNormalsHorizontalKernel = compute.FindKernel("FilterNormalsHorizontal");
        filterNormalsVerticalKernel = compute.FindKernel("FilterNormalsVertical");
    }

    public void BeginFrame(int frame, HashSet<GlassSdfDescriptor> activeDescriptors)
    {
        if (!IsAvailable)
        {
            if (!warnedUnavailable)
            {
                warnedUnavailable = true;
                Debug.LogWarning("Flexible Glass retained fields require compute-shader support. UIGlass and GlassImage rendering are disabled on this device.");
            }
            return;
        }

        if (pendingResolution != resolution)
        {
            resolution = pendingResolution;
            ReleaseFieldResources();
        }

        var requiredCapacity = activeDescriptors.Count;
        foreach (var entry in entries.Values)
        {
            if (!entry.evictionImmune || activeDescriptors.Contains(entry.descriptor))
                continue;
            if (ContainsSpriteVariant(activeDescriptors, entry.descriptor))
                entry.evictionImmune = false;
            else
                requiredCapacity++;
        }
        EnsureCapacity(Mathf.NextPowerOfTwo(Mathf.Max(1, requiredCapacity)), GetRequiredScratchDimensions(activeDescriptors));
        currentFrame = frame;
        foreach (var descriptor in activeDescriptors)
            if (entries.TryGetValue(descriptor, out var entry))
                entry.lastUsedFrame = currentFrame;
    }

    public bool TryRequest(GlassSdfDescriptor descriptor, ref GlassElementGpu element)
    {
        if (!TryRequest(descriptor, out var sdfData))
            return false;

        element.sdfData = new Vector4(sdfData.x, sdfData.y, sdfData.z, element.sdfData.w);
        return true;
    }

    public bool TryRequest(GlassSdfDescriptor descriptor, out Vector4 sdfData)
    {
        sdfData = default;
        if (!IsAvailable || !HasResources)
            return false;

        if (!entries.TryGetValue(descriptor, out var entry))
        {
            ClearSpriteEvictionImmunity(descriptor);
            int slice;
            if (freeSlices.Count > 0)
            {
                slice = freeSlices.Pop();
                entry = AcquireEntry();
            }
            else if (!TryEvict(out entry))
                return false;
            else
                slice = entry.slice;

            entry.raycastField?.Invalidate();
            entry.descriptor = descriptor;
            entry.slice = slice;
            entry.lastUsedFrame = currentFrame;
            entry.evictionImmune = false;
            entries.Add(descriptor, entry);
            SetSpriteEvictionImmune(entry);
            pendingGenerations.Add(entry);
        }
        else
        {
            entry.lastUsedFrame = currentFrame;
            SetSpriteEvictionImmune(entry);
        }

        sdfData = new Vector4(descriptor.padding.x, descriptor.padding.y, entry.slice, resolution);
        return true;
    }

    public GlassSdfRaycastField RequestRaycastField(GlassSdfDescriptor descriptor)
    {
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            if (!warnedReadbackUnavailable)
            {
                warnedReadbackUnavailable = true;
                Debug.LogWarning("GlassImage SDF raycasts require asynchronous GPU readback, which is unavailable on this device. Disable SDF Raycast to use standard hit testing.");
            }
            return null;
        }
        if (!entries.TryGetValue(descriptor, out var entry))
            return null;
        var field = entry.raycastField ??= new GlassSdfRaycastField(resolution);
        field.Request(descriptor);
        if (field.NeedsReadback && !entry.raycastQueued)
        {
            pendingRaycasts.Add(entry);
            entry.raycastQueued = true;
        }
        return field;
    }

    public void GeneratePending(CommandBuffer cmd)
    {
        if (!HasResources)
            return;

        foreach (var entry in pendingGenerations)
            Generate(cmd, entry);
        foreach (var entry in pendingGenerations)
            GenerateMips(cmd, entry.slice);
        pendingGenerations.Clear();
        foreach (var entry in pendingRaycasts)
        {
            entry.raycastQueued = false;
            entry.raycastField.QueueReadback(cmd, fieldAtlas.rt, entry.slice, readbackResources);
        }
        pendingRaycasts.Clear();
    }

    public void Dispose()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        ReleaseFieldResources();
        foreach (var entry in freeEntries)
            entry.ReleaseSpriteBuffers();
        freeEntries.Clear();
    }

    public void SetResolution(int value) => pendingResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(value), 64, 1024);

    private void ReleaseFieldResources()
    {
        foreach (var entry in entries.Values)
        {
            entry.raycastField?.Dispose();
            entry.raycastField = null;
            entry.raycastQueued = false;
            freeEntries.Push(entry);
        }
        foreach (var entry in freeEntries)
        {
            entry.raycastField?.Dispose();
            entry.raycastField = null;
        }
        entries.Clear();
        freeSlices.Clear();
        pendingGenerations.Clear();
        pendingRaycasts.Clear();
        immuneDescriptors.Clear();
        readbackResources?.Dispose();
        readbackResources = null;
        handleSystem = null;
        fieldAtlas = mask = seedA = seedB = mipScratch = normalScratch = null;
        scratchDimensions = default;
        capacity = 0;
    }

    private void EnsureCapacity(int requiredCapacity, Vector2Int requiredScratchDimensions)
    {
        if (requiredCapacity <= capacity && HasResources)
        {
            EnsureScratchResources(requiredScratchDimensions);
            return;
        }

        immuneDescriptors.Clear();
        foreach (var entry in entries.Values)
        {
            if (!entry.evictionImmune)
                continue;
            immuneDescriptors.Add(entry.descriptor);
        }

        foreach (var entry in entries.Values)
        {
            entry.raycastField?.Invalidate();
            entry.raycastQueued = false;
            freeEntries.Push(entry);
        }
        entries.Clear();
        freeSlices.Clear();
        pendingGenerations.Clear();
        pendingRaycasts.Clear();
        readbackResources?.Dispose();
        handleSystem = new RTHandleSystem();
        readbackResources = new GlassSdfRaycastField.ReadbackResources(handleSystem);
        handleSystem.Initialize(resolution, resolution);
        fieldAtlas = mask = seedA = seedB = mipScratch = normalScratch = null;
        scratchDimensions = default;
        capacity = requiredCapacity;
        var fieldFormat = SupportsRandomWrite(GraphicsFormat.R16G16B16A16_SFloat)
            ? GraphicsFormat.R16G16B16A16_SFloat
            : GraphicsFormat.R32G32B32A32_SFloat;
        fieldAtlas = handleSystem.Alloc(resolution, resolution, capacity, colorFormat: fieldFormat, filterMode: FilterMode.Trilinear, wrapMode: TextureWrapMode.Clamp, dimension: TextureDimension.Tex2DArray, useMipMap: true, autoGenerateMips: false, enableRandomWrite: true, name: "Flexible Glass Retained Fields");
        mipScratch = handleSystem.Alloc(resolution / 2, resolution / 2, 1, colorFormat: fieldFormat, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, dimension: TextureDimension.Tex2DArray, useMipMap: true, autoGenerateMips: false, enableRandomWrite: true, name: "Flexible Glass Field Mip Scratch");
        EnsureScratchResources(requiredScratchDimensions);
        for (int i = capacity - 1; i >= 0; i--)
            freeSlices.Push(i);
        if (immuneDescriptors.Count == 0)
            return;

        foreach (var descriptor in immuneDescriptors)
        {
            var entry = AcquireEntry();
            entry.descriptor = descriptor;
            entry.slice = freeSlices.Pop();
            entry.lastUsedFrame = currentFrame;
            entry.evictionImmune = true;
            entries.Add(descriptor, entry);
            pendingGenerations.Add(entry);
        }
    }

    private Vector2Int GetRequiredScratchDimensions(HashSet<GlassSdfDescriptor> activeDescriptors)
    {
        var required = new Vector2Int(resolution, resolution);
        foreach (var descriptor in activeDescriptors)
        {
            var dimensions = GetGenerationDimensions(descriptor);
            required.x = Mathf.Max(required.x, dimensions.x);
            required.y = Mathf.Max(required.y, dimensions.y);
        }
        foreach (var entry in entries.Values)
        {
            if (!entry.evictionImmune || activeDescriptors.Contains(entry.descriptor))
                continue;
            var dimensions = GetGenerationDimensions(entry.descriptor);
            required.x = Mathf.Max(required.x, dimensions.x);
            required.y = Mathf.Max(required.y, dimensions.y);
        }
        return required;
    }

    private Vector2Int GetGenerationDimensions(GlassSdfDescriptor descriptor)
    {
        if (descriptor.source != GlassSdfSource.SpriteAlpha || !descriptor.texture)
            return new Vector2Int(resolution, resolution);

        var uvSize = new Vector2(Mathf.Abs(descriptor.textureUv.z - descriptor.textureUv.x), Mathf.Abs(descriptor.textureUv.w - descriptor.textureUv.y));
        var spritePixels = new Vector2(Mathf.Max(1f, descriptor.texture.width * uvSize.x), Mathf.Max(1f, descriptor.texture.height * uvSize.y));
        if (descriptor.packedSprite)
            spritePixels = descriptor.sprite.rect.size * descriptor.sprite.spriteAtlasTextureScale;
        var domainScale = (descriptor.size + 2f * descriptor.padding);
        domainScale = new Vector2(domainScale.x / Mathf.Max(descriptor.size.x, 1e-5f), domainScale.y / Mathf.Max(descriptor.size.y, 1e-5f));
        return new Vector2Int
        (
            Mathf.Clamp(Mathf.CeilToInt(spritePixels.x * domainScale.x), resolution, MaximumSpriteGenerationResolution),
            Mathf.Clamp(Mathf.CeilToInt(spritePixels.y * domainScale.y), resolution, MaximumSpriteGenerationResolution)
        );
    }

    private void EnsureScratchResources(Vector2Int requiredDimensions)
    {
        requiredDimensions = new Vector2Int(Mathf.Max(resolution, requiredDimensions.x), Mathf.Max(resolution, requiredDimensions.y));
        var dimensions = new Vector2Int(Mathf.Max(scratchDimensions.x, requiredDimensions.x), Mathf.Max(scratchDimensions.y, requiredDimensions.y));
        if (mask != null && seedA != null && seedB != null && dimensions == scratchDimensions)
            return;

        mask?.Release();
        seedA?.Release();
        seedB?.Release();
        normalScratch?.Release();
        normalScratch = null;
        scratchDimensions = dimensions;
        var maskFormat = SupportsRandomWrite(GraphicsFormat.R16G16B16A16_SFloat) && SupportsRandomWrite(GraphicsFormat.R16_SFloat) ? GraphicsFormat.R16_SFloat : GraphicsFormat.R32_SFloat;
        mask = handleSystem.Alloc(dimensions.x, dimensions.y, colorFormat: maskFormat, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true, name: "Flexible Glass SDF Mask");
        seedA = handleSystem.Alloc(dimensions.x, dimensions.y, colorFormat: GraphicsFormat.R32G32_SFloat, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true, name: "Flexible Glass SDF Seed A");
        seedB = handleSystem.Alloc(dimensions.x, dimensions.y, colorFormat: GraphicsFormat.R32G32_SFloat, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true, name: "Flexible Glass SDF Seed B");
    }

    private void EnsureNormalScratch()
    {
        // Allocate only when generating a sprite field; shape-only caches never need it.
        normalScratch ??= handleSystem.Alloc(scratchDimensions.x, scratchDimensions.y, colorFormat: GraphicsFormat.R32G32_SFloat, filterMode: FilterMode.Point, wrapMode: TextureWrapMode.Clamp, enableRandomWrite: true, name: "Flexible Glass Filtered Normals");
    }

    private Entry AcquireEntry() => freeEntries.Count > 0 ? freeEntries.Pop() : new Entry();

    private bool TryEvict(out Entry reusable)
    {
        Entry oldest = null;
        foreach (var entry in entries.Values)
        {
            if (entry.evictionImmune || entry.lastUsedFrame == currentFrame || oldest != null && entry.lastUsedFrame >= oldest.lastUsedFrame)
                continue;
            oldest = entry;
        }

        if (oldest == null)
        {
            reusable = null;
            return false;
        }

        entries.Remove(oldest.descriptor);
        if (oldest.raycastQueued)
        {
            pendingRaycasts.Remove(oldest);
            oldest.raycastQueued = false;
        }
        for (int i = pendingGenerations.Count - 1; i >= 0; i--)
            if (pendingGenerations[i] == oldest)
                pendingGenerations.RemoveAt(i);
        reusable = oldest;
        return true;
    }

    private void SetSpriteEvictionImmune(Entry selected)
    {
        if (selected.descriptor.source != GlassSdfSource.SpriteAlpha)
            return;

        ClearSpriteEvictionImmunity(selected.descriptor, selected);
        selected.evictionImmune = true;
    }

    private void ClearSpriteEvictionImmunity(GlassSdfDescriptor descriptor, Entry exception = null)
    {
        if (descriptor.source != GlassSdfSource.SpriteAlpha)
            return;

        foreach (var entry in entries.Values)
            if (entry != exception && IsSameSprite(entry.descriptor, descriptor))
                entry.evictionImmune = false;
    }

    private static bool ContainsSpriteVariant(HashSet<GlassSdfDescriptor> descriptors, GlassSdfDescriptor sprite)
    {
        foreach (var descriptor in descriptors)
            if (IsSameSprite(descriptor, sprite))
                return true;
        return false;
    }

    private static bool IsSameSprite(GlassSdfDescriptor first, GlassSdfDescriptor second) =>
        first.source == GlassSdfSource.SpriteAlpha &&
        second.source == GlassSdfSource.SpriteAlpha &&
        (first.spriteInstanceId != default ? first.spriteInstanceId == second.spriteInstanceId :
            first.textureInstanceId == second.textureInstanceId && first.textureUv == second.textureUv);

    private void PrepareSpriteMesh(Entry entry)
    {
        var descriptor = entry.descriptor;
        if (entry.spriteTriangles != null && entry.meshSprite == descriptor.sprite && entry.meshTexture == descriptor.texture &&
            entry.meshUv == descriptor.textureUv && entry.meshTextureUpdateCount == descriptor.textureUpdateCount)
            return;
        GlassSpriteMesh.Build(descriptor.sprite, spriteTriangles, spriteTriangleBounds);
        tiledSpriteTriangles.Clear();
        const int tiles = GlassSpriteMesh.TileCount;
        for (int y = 0; y < tiles; y++)
        for (int x = 0; x < tiles; x++)
        {
            var start = tiledSpriteTriangles.Count;
            var min = new Vector2((float)x / tiles, (float)y / tiles);
            var max = min + Vector2.one / tiles;
            for (int i = 0; i < spriteTriangles.Count; i++)
            {
                var bounds = spriteTriangleBounds[i];
                if (bounds.z >= min.x && bounds.w >= min.y && bounds.x <= max.x && bounds.y <= max.y)
                    tiledSpriteTriangles.Add(spriteTriangles[i]);
            }
            spriteTiles[y * tiles + x] = new Vector2Int(start, tiledSpriteTriangles.Count - start);
        }
        if (tiledSpriteTriangles.Count == 0)
            tiledSpriteTriangles.Add(default);
        if (entry.spriteTriangles == null || entry.spriteTriangles.count < tiledSpriteTriangles.Count)
        {
            entry.spriteTriangles?.Release();
            entry.spriteTriangles = new ComputeBuffer(Mathf.NextPowerOfTwo(tiledSpriteTriangles.Count), 48);
        }
        entry.spriteTiles ??= new ComputeBuffer(tiles * tiles, 8);
        entry.spriteTriangles.SetData(tiledSpriteTriangles);
        entry.spriteTiles.SetData(spriteTiles);
        entry.meshSprite = descriptor.sprite;
        entry.meshTexture = descriptor.texture;
        entry.meshUv = descriptor.textureUv;
        entry.meshTextureUpdateCount = descriptor.textureUpdateCount;
    }

    private void Generate(CommandBuffer cmd, Entry entry)
    {
        var descriptor = entry.descriptor;
        var generationDimensions = GetGenerationDimensions(descriptor);
        var generationGroupsX = Mathf.CeilToInt(generationDimensions.x / 8f);
        var generationGroupsY = Mathf.CeilToInt(generationDimensions.y / 8f);
        var outputGroups = Mathf.CeilToInt(resolution / 8f);
        cmd.SetComputeIntParam(compute, GenerationWidthId, generationDimensions.x);
        cmd.SetComputeIntParam(compute, GenerationHeightId, generationDimensions.y);
        cmd.SetComputeIntParam(compute, OutputResolutionId, resolution);
        cmd.SetComputeIntParam(compute, ShapeSourceId, (int)descriptor.source);
        cmd.SetComputeIntParam(compute, ShapeTypeId, descriptor.shapeType);
        cmd.SetComputeVectorParam(compute, SizeId, new Vector4(descriptor.size.x, descriptor.size.y, 0f, 0f));
        cmd.SetComputeVectorParam(compute, PaddingId, new Vector4(descriptor.padding.x, descriptor.padding.y, 0f, 0f));
        cmd.SetComputeVectorParam(compute, CornerRadiiId, descriptor.cornerRadii);
        cmd.SetComputeVectorParam(compute, CornerShapeId, descriptor.cornerShape);
        cmd.SetComputeVectorParam(compute, TextureUvId, descriptor.textureUv);
        cmd.SetComputeFloatParam(compute, AlphaThresholdId, descriptor.alphaThreshold);
        cmd.SetComputeIntParam(compute, FieldSliceId, entry.slice);
        var maskKernel = rasterizeMaskKernel;
        if (descriptor.packedSprite)
        {
            PrepareSpriteMesh(entry);
            maskKernel = rasterizeSpriteKernel;
            cmd.SetComputeBufferParam(compute, maskKernel, SpriteTrianglesId, entry.spriteTriangles);
            cmd.SetComputeBufferParam(compute, maskKernel, SpriteTilesId, entry.spriteTiles);
        }
        else
            entry.ReleaseSpriteBuffers();
        cmd.SetComputeTextureParam(compute, maskKernel, MaskTexId, descriptor.texture ? descriptor.texture : Texture2D.whiteTexture);
        cmd.SetComputeTextureParam(compute, maskKernel, MaskWriteId, mask);
        cmd.DispatchCompute(compute, maskKernel, generationGroupsX, generationGroupsY, 1);
        cmd.SetComputeTextureParam(compute, initializeKernel, MaskReadId, mask);
        cmd.SetComputeTextureParam(compute, initializeKernel, SeedWriteId, seedA);
        cmd.DispatchCompute(compute, initializeKernel, generationGroupsX, generationGroupsY, 1);

        var read = seedA;
        var write = seedB;
        for (var jump = Mathf.NextPowerOfTwo(Mathf.Max(generationDimensions.x, generationDimensions.y)) >> 1; jump >= 1; jump >>= 1)
            DispatchJumpFlood(cmd, jump, generationGroupsX, generationGroupsY, ref read, ref write);

        var smoothSpriteNormals = descriptor.source == GlassSdfSource.SpriteAlpha;
        if (smoothSpriteNormals)
        {
            EnsureNormalScratch();
            // JFA has finished: reuse its inactive ping-pong target for the horizontal
            // filter. The winning seeds in 'read' and the original mask stay untouched.
            cmd.SetComputeTextureParam(compute, filterNormalsHorizontalKernel, MaskReadId, mask);
            cmd.SetComputeTextureParam(compute, filterNormalsHorizontalKernel, NormalWriteId, write);
            cmd.DispatchCompute(compute, filterNormalsHorizontalKernel, generationGroupsX, generationGroupsY, 1);
            cmd.SetComputeTextureParam(compute, filterNormalsVerticalKernel, NormalReadId, write);
            cmd.SetComputeTextureParam(compute, filterNormalsVerticalKernel, NormalWriteId, normalScratch);
            cmd.DispatchCompute(compute, filterNormalsVerticalKernel, generationGroupsX, generationGroupsY, 1);
        }

        // Procedural shapes do not sample the filtered sprite normals.
        cmd.SetComputeTextureParam(compute, finalizeKernel, NormalReadId, smoothSpriteNormals ? normalScratch : write);
        cmd.SetComputeTextureParam(compute, finalizeKernel, MaskReadId, mask);
        cmd.SetComputeTextureParam(compute, finalizeKernel, SeedReadId, read);
        cmd.SetComputeTextureParam(compute, finalizeKernel, FieldWriteId, fieldAtlas);
        cmd.DispatchCompute(compute, finalizeKernel, outputGroups, outputGroups, 1);
    }

    private void DispatchJumpFlood(CommandBuffer cmd, int jump, int groupsX, int groupsY, ref RTHandle read, ref RTHandle write)
    {
        cmd.SetComputeIntParam(compute, JumpId, jump);
        cmd.SetComputeTextureParam(compute, jumpFloodKernel, SeedReadId, read);
        cmd.SetComputeTextureParam(compute, jumpFloodKernel, SeedWriteId, write);
        cmd.DispatchCompute(compute, jumpFloodKernel, groupsX, groupsY, 1);
        (read, write) = (write, read);
    }

    private void GenerateMips(CommandBuffer cmd, int slice)
    {
        cmd.SetComputeIntParam(compute, FieldSliceId, slice);
        for (var mip = 1; mip <= MaximumLod; mip++)
        {
            var mipResolution = Mathf.Max(1, resolution >> mip);
            var groups = Mathf.CeilToInt(mipResolution / 8f);
            cmd.SetComputeIntParam(compute, MipResolutionId, mipResolution);
            cmd.SetComputeIntParam(compute, SourceMipId, mip - 1);
            cmd.SetComputeTextureParam(compute, generateMipKernel, FieldReadId, fieldAtlas);
            // A read-only texture binding exposes all mips, conflicting with writes to the same atlas on D3D11.
            cmd.SetComputeTextureParam(compute, generateMipKernel, MipWriteId, mipScratch, mip - 1);
            cmd.DispatchCompute(compute, generateMipKernel, groups, groups, 1);
            cmd.CopyTexture(mipScratch, 0, mip - 1, fieldAtlas, slice, mip);
        }
    }

    private static bool SupportsRandomWrite(GraphicsFormat format)
    {
#if UNITY_2023_2_OR_NEWER
        return SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore);
#else
        return SystemInfo.IsFormatSupported(format, FormatUsage.LoadStore);
#endif
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        foreach (var entry in entries.Values)
            entry.evictionImmune = false;
    }
}
}
