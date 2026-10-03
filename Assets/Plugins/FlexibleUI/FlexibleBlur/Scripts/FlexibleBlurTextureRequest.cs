using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class FlexibleBlurTextureRequest : IDisposable
{
    internal Camera Camera { get; private set; }
    internal int FeatureIndex { get; private set; }
    internal RTHandle Source { get; private set; }
    internal RTHandle Destination { get; private set; }
    internal BlurSettings Settings { get; private set; }
    internal RectInt Bounds { get; private set; }
    internal float Strength { get; private set; }
    internal int MipLevels { get; private set; }

    /// <summary>Schedules a blur from the camera color target, or an optional camera-sized source, into a caller-owned destination. Bounds are in camera pixels; requested mip levels are generated when supported by the destination.</summary>
    public bool Update(Camera camera, int featureIndex, RTHandle destination, BlurSettings settings, RectInt bounds, float strength = 1f, RTHandle source = null, int mipLevels = 0)
    {
        if (!camera || destination == null || settings == null || bounds.width <= 0 || bounds.height <= 0 || source == destination)
        {
            Clear();
            return false;
        }

        if (Camera != camera || FeatureIndex != featureIndex)
            FlexibleBlurPass.UnregisterTextureRequest(this);

        Camera = camera;
        FeatureIndex = Mathf.Max(0, featureIndex);
        Source = source;
        Destination = destination;
        Settings = settings;
        var minX = Mathf.Clamp(bounds.xMin, 0, camera.pixelWidth);
        var minY = Mathf.Clamp(bounds.yMin, 0, camera.pixelHeight);
        var maxX = Mathf.Clamp(bounds.xMax, 0, camera.pixelWidth);
        var maxY = Mathf.Clamp(bounds.yMax, 0, camera.pixelHeight);
        if (maxX <= minX || maxY <= minY)
        {
            Clear();
            return false;
        }
        Bounds = new RectInt(minX, minY, maxX - minX, maxY - minY);
        Strength = Mathf.Clamp01(strength);
        MipLevels = destination.rt && destination.rt.useMipMap ? Mathf.Min(Mathf.Max(0, mipLevels), destination.rt.mipmapCount - 1) : 0;
        FlexibleBlurPass.RegisterTextureRequest(this);
        return FlexibleBlurPass.HasFeature(camera, FeatureIndex);
    }

    public void Clear()
    {
        FlexibleBlurPass.UnregisterTextureRequest(this);
        Camera = null;
        Source = null;
        Destination = null;
        Settings = null;
        Bounds = default;
        Strength = 0f;
        MipLevels = 0;
    }

    public void Dispose() => Clear();
}

public partial class FlexibleBlurPass
{
    private static readonly Dictionary<(Camera camera, int featureIndex), List<FlexibleBlurTextureRequest>> TextureRequests = new();
    private static int textureRequestsPrunedFrame = -1;

    internal static void RegisterTextureRequest(FlexibleBlurTextureRequest request)
    {
        var key = (request.Camera, request.FeatureIndex);
        if (!TextureRequests.TryGetValue(key, out var requests))
            requests = TextureRequests[key] = new List<FlexibleBlurTextureRequest>();
        if (!requests.Contains(request))
            requests.Add(request);
    }

    internal static void UnregisterTextureRequest(FlexibleBlurTextureRequest request)
    {
        if (ReferenceEquals(request.Camera, null) || !TextureRequests.TryGetValue((request.Camera, request.FeatureIndex), out var requests))
            return;

        requests.Remove(request);
        if (requests.Count == 0)
            TextureRequests.Remove((request.Camera, request.FeatureIndex));
    }

    internal static bool TryGetTextureRequests(Camera camera, int featureIndex, out List<FlexibleBlurTextureRequest> requests)
    {
        PruneTextureRequests();
        var key = (camera, featureIndex);
        if (!TextureRequests.TryGetValue(key, out requests))
            return false;

        for (int i = requests.Count - 1; i >= 0; i--)
        {
            var request = requests[i];
            if (request == null || request.Camera != camera || request.Destination == null || request.Settings == null)
                requests.RemoveAt(i);
        }

        if (requests.Count > 0)
            return true;

        TextureRequests.Remove(key);
        requests = null;
        return false;
    }

    public static bool HasFeature(Camera camera, int featureIndex) =>
        camera && FlexibleBlurFeature.GlobalFlexibleBlurPassDict.ContainsKey((camera, Mathf.Max(0, featureIndex)));

    public static float EstimateBlurReach(BlurSettings settings)
    {
        if (settings == null)
            return 0f;

        var reach = 2f;
        if (settings.downscaleSections != null)
        {
            foreach (var section in settings.downscaleSections)
                if (section != null)
                    reach += Mathf.Max(0, section.iterations) * Mathf.Max(0f, section.sampleDistance);
        }
        if (settings.blurSections != null)
        {
            var iteration = 0;
            foreach (var section in settings.blurSections)
            {
                if (section == null)
                    continue;
                for (int i = 0; i < Mathf.Max(0, section.iterations); i++, iteration++)
                    reach += Mathf.Max(0f, section.sampleDistance + settings.blurAdditionalDistancePerIteration * iteration);
            }
        }
        return reach;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearTextureRequests()
    {
        TextureRequests.Clear();
        textureRequestsPrunedFrame = -1;
    }

    private static void PruneTextureRequests()
    {
        if (textureRequestsPrunedFrame == Time.frameCount)
            return;
        textureRequestsPrunedFrame = Time.frameCount;
        for (int i = 0; i < TextureRequests.Count; i++)
        {
            var key = TextureRequests.ElementAt(i).Key;
            if (key.camera)
                continue;
            TextureRequests.Remove(key);
            i--;
        }
    }
}
}
