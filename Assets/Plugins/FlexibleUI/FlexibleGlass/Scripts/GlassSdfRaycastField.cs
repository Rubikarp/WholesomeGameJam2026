using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
internal sealed class GlassSdfRaycastField : IDisposable
{
    private readonly int resolution;
    private readonly Action<AsyncGPUReadbackRequest> readbackCompleted;
    private GlassSdfDescriptor descriptor;
    private Vector3[] samples;
    private uint version, readbackVersion;
    private bool requested, pending, ready, failed, disposed;
    private ReadbackResources pendingResources;

    internal sealed class ReadbackResources : IDisposable
    {
        private RTHandleSystem handles;
        private int readers;
        private bool retired;

        public ReadbackResources(RTHandleSystem handles) => this.handles = handles;
        public void Retain() => readers++;
        public void Release()
        {
            readers--;
            if (retired && readers == 0) Dispose();
        }
        public void Dispose()
        {
            retired = true;
            if (readers != 0) return;
            handles?.Dispose();
            handles = null;
        }
    }

    public bool NeedsReadback => requested && !pending && !ready && !failed && !disposed;

    public GlassSdfRaycastField(int resolution)
    {
        this.resolution = resolution;
        readbackCompleted = CompleteReadback;
    }

    public void Request(GlassSdfDescriptor descriptor)
    {
        this.descriptor = descriptor;
        requested = true;
    }

    public void Invalidate()
    {
        version++;
        requested = ready = failed = false;
    }

    public void QueueReadback(CommandBuffer cmd, Texture atlas, int slice, ReadbackResources resources)
    {
        if (!NeedsReadback)
            return;
        readbackVersion = version;
        pending = true;
        pendingResources = resources;
        resources.Retain();
        cmd.RequestAsyncReadback(atlas, 0, 0, resolution, 0, resolution, slice, 1, TextureFormat.RGBAFloat, readbackCompleted);
    }

    private void CompleteReadback(AsyncGPUReadbackRequest request)
    {
        pending = false;
        pendingResources.Release();
        pendingResources = null;
        if (disposed || readbackVersion != version)
            return;
        if (request.hasError)
        {
            failed = true;
            Debug.LogWarning("GlassImage could not read its cached SDF for raycasting. SDF raycasts are unavailable for this field.");
            return;
        }
        var data = request.GetData<Vector4>();
        samples ??= new Vector3[resolution * resolution];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = data[i];
        ready = true;
    }

    public bool TrySample(GlassSdfDescriptor expected, Vector2 position, out float distance)
    {
        distance = 0f;
        if (!ready || disposed || !descriptor.Equals(expected))
            return false;
        var domain = descriptor.size + descriptor.padding * 2f;
        var uv = new Vector2((position.x + descriptor.padding.x) / domain.x, (position.y + descriptor.padding.y) / domain.y);
        var clampedUv = new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
        var pixel = clampedUv * resolution - Vector2.one * 0.5f;
        var x = Mathf.FloorToInt(pixel.x);
        var y = Mathf.FloorToInt(pixel.y);
        var lower = Vector3.LerpUnclamped(Sample(x, y), Sample(x + 1, y), pixel.x - x);
        var upper = Vector3.LerpUnclamped(Sample(x, y + 1), Sample(x + 1, y + 1), pixel.x - x);
        var field = Vector3.LerpUnclamped(lower, upper, pixel.y - y);
        distance = field.x;
        var outside = Vector2.Scale(uv - clampedUv, domain);
        if (outside.sqrMagnitude > 1e-8f)
        {
            var normal = new Vector2(field.y, field.z);
            normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector2.right;
            distance = (normal * Mathf.Max(distance, 0f) + outside).magnitude;
        }
        return true;
    }

    private Vector3 Sample(int x, int y) => samples[Mathf.Clamp(y, 0, resolution - 1) * resolution + Mathf.Clamp(x, 0, resolution - 1)];

    public void Dispose()
    {
        disposed = true;
        Invalidate();
        samples = null;
    }
}
}
