using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class UIBlurCommon
{
    public enum BlurReferencesFrom { Self, ReferenceProvider }

    public BlurReferencesFrom blurReferencesFrom = BlurReferencesFrom.Self;
    public Camera cameraReference;
    public int featureNumber;

    [Range(0, 1)] public float blurStrength = 1f;
    public int unrankedLayer;
    public int priority;

    public BlurPreset blurPreset;
    public BlurSettings blurInstanceSettings = new();

    private BlurSettings _activeSettings;
    public BlurSettings ActiveSettings => _activeSettings;

    public Camera WorldCamera { get; private set; }

    public Vector4 BlurRegion { get; private set; }
    public Vector4 BlurRegionRight { get; private set; }

    public Matrix4x4 TransformationMatrix { get; private set; }

    public int LayerRank { get; private set; }
    public bool PresentInBlurList { get; private set; }
    public bool IsAngled { get; private set; }

    private IBlur blur;
    private Dictionary<(Camera camera, int featureNumber), List<IBlur>> blurDict;
    private Dictionary<(Camera camera, int featureNumber), int> layersPerCameraDict;

    public readonly Vector3[] worldCorners = new Vector3[4];
    private readonly Vector3[] blitRegionCornersArray = new Vector3[4];

    public Vector4[] ScreenCorners { get; }  = new Vector4[4];
    public Vector4[] ScreenCornersRight { get; }  = new Vector4[4];
    private readonly Vector4[] prevScreenCorners = new Vector4[4];

    private static readonly Vector4 TransformationMatrixColumn2 = new (0f, 0f, 1f, 0f);

    private (Camera prevCamera, int prevFeatureNumber) prevKey;
    private bool hasVisiblePixels, hasVisiblePixelsRight;
    public bool HasVisiblePixels(bool right = false) => right ? hasVisiblePixelsRight : hasVisiblePixels;

    private Vector2 xrScale;
    private bool useXR;
    private int prevPriority, prevUnrankedLayer;
    private float minX, maxX, minY, maxY, minXRight, maxXRight, minYRight, maxYRight;
    public float MinX(bool right = false) => right ? minXRight : minX;
    public float MaxX(bool right = false) => right ? maxXRight : maxX;
    public float MinY(bool right = false) => right ? minYRight : minY;
    public float MaxY(bool right = false) => right ? maxYRight : maxY;

    private bool hasCachedBlur;
    private Canvas cachedCanvas;
    private RectTransform cachedRectTransform;
    private Vector2 cachedOffset, cachedShapePadding;
    private float cachedRotation, cachedBlurPadding;
    private bool cachedUseFilterPadding, cachedFitRotatedImageWithinBounds, cachedFillWholeScreen;

    public void Init(IBlur blur, Dictionary<(Camera, int), List<IBlur>> blurDict, Dictionary<(Camera, int), int> layersPerCamera) => (this.blur, this.blurDict, this.layersPerCameraDict) = (blur, blurDict, layersPerCamera);

    public void CopyPresetToInstanceSettings()
    {
        if (blurPreset == null)
        {
            Debug.LogWarning("Cannot copy from the preset, because no preset has been assigned!");
            return;
        }

        blurInstanceSettings.CopySettings(blurPreset.Settings[QualitySettings.GetQualityLevel()]);
    }

    public void ValidateBlur()
    {
        if (blurPreset == null)
        {
            _activeSettings = blurInstanceSettings;
        }
        else
        {
            var qualityLvl = QualitySettings.GetQualityLevel();
            _activeSettings = blurPreset.Settings.Count > qualityLvl
                            ? blurPreset.Settings[qualityLvl]
                            : blurPreset.Settings[^1];
        }
    }

    public void PlaceInBlurList((Camera camera, int featureNumber) key)
    {
        if (blur is UIBlur && blur.Alpha == 0 && !blur.ActiveAtZeroAlpha)
            return;

        if (!blurDict.TryGetValue(key, out var blurList))
            blurList = blurDict[key] = new List<IBlur>();

        if (blurList.Count == 0)
        {
            if (!layersPerCameraDict.TryAdd(key, 1))
                layersPerCameraDict[key] += 1;

            blurList.Add(blur);
            LayerRank = 0;
            prevKey = key;
            prevPriority = priority;
            prevUnrankedLayer = unrankedLayer;
            PresentInBlurList = true;
            return;
        }

        var idx = 0;
        var currentUnrankedLayer = blurList[0].Common.prevUnrankedLayer;
        var currentLayerRank = 0;

        for (; idx < blurList.Count; idx++)
        {
            var otherUnrankedLayer = blurList[idx].Common.prevUnrankedLayer;

            if (currentUnrankedLayer != otherUnrankedLayer)
            {
                currentUnrankedLayer = otherUnrankedLayer;
                currentLayerRank++;
            }

            if (unrankedLayer <= otherUnrankedLayer)
                break;
        }

        if (unrankedLayer == currentUnrankedLayer)
        {
            for (; idx < blurList.Count; idx++)
            {
                var otherBlur = blurList[idx];
                if (priority < otherBlur.Priority || unrankedLayer < otherBlur.Common.prevUnrankedLayer)
                    break;
            }
        }
        else
        {
            if (idx == blurList.Count)
                currentLayerRank++;

            if (!layersPerCameraDict.TryAdd(key, 1))
                layersPerCameraDict[key] += 1;

            for (int i = idx; i < blurList.Count; i++)
                blurList[i].Common.LayerRank++;
        }

        blurList.Insert(idx, blur);
        LayerRank = currentLayerRank;
        prevKey = key;
        prevPriority = priority;
        prevUnrankedLayer = unrankedLayer;
        PresentInBlurList = true;
    }

    public void RemoveFromBlurList()
    {
        hasCachedBlur = false;

        if (!PresentInBlurList || !blurDict.TryGetValue(prevKey, out var blurList))
        {
            prevKey = (null, 0);
            PresentInBlurList = false;
            return;
        }

        var idx = blurList.IndexOf(blur);
        blurList.RemoveAt(idx);

        var prevElementSharesLayer = idx > 0 && blurList[idx - 1].Common.LayerRank == LayerRank;
        var nextElementSharesLayer = idx < blurList.Count && blurList[idx].Common.LayerRank == LayerRank;
        var layerRemoved = !prevElementSharesLayer && !nextElementSharesLayer;
        if (!layerRemoved)
        {
            prevKey = (null, 0);
            PresentInBlurList = false;
            return;
        }

        if (blurList.Count == 0)
            blurDict.Remove(prevKey);
        else for (int i = idx; i < blurList.Count; i++)
            blurList[i].Common.LayerRank--;

        var cameraLayers = --layersPerCameraDict[prevKey];
        if (cameraLayers == 0)
            layersPerCameraDict.Remove(prevKey);

        prevKey = (null, 0);
        PresentInBlurList = false;
    }

    public (Camera camera, int featureNumber) GetCameraFeatureKey(Canvas canvas)
    {
        if (blurReferencesFrom == BlurReferencesFrom.ReferenceProvider && BlurReferenceProvider.CameraReferenceDict.TryGetValue(canvas, out var result) && result.camera != null)
            return result;

        return cameraReference ? (cameraReference, featureIndex: featureNumber) : (Camera.main, featureIndex: featureNumber);
    }

    bool TryGetIntersection(Plane plane, Vector3 point1, Vector3 point2, out Vector3 intersection)
    {
        var lineDirection = point2 - point1;
        var ray = new Ray(point1, lineDirection.normalized);

        if (plane.Raycast(ray, out var enter))
        {
            intersection = point1 + enter * lineDirection.normalized;
            return true;
        }

        intersection = Vector3.zero;
        return false;
    }

    private void GetScaledWorldCorners(RectTransform rectTransform, Vector2 offset, float rotation, bool fitRotationInsideOriginalBounds, Vector2 shapePadding, float blurPadding, Vector3[] worldCornersArray)
    {
        rectTransform.GetLocalCorners(blitRegionCornersArray);
        var (rectWidth, rectHeight) = (rectTransform.rect.width, rectTransform.rect.height);
        var rotationQuat = Quaternion.Euler(0, 0, rotation);
        var shapePaddingFactor = new Vector3((rectWidth + shapePadding.x) / rectWidth, (rectHeight + shapePadding.y) / rectHeight, 1);
        var totalPaddingFactor = new Vector3((rectWidth +  shapePadding.x + blurPadding) / rectWidth, (rectHeight + shapePadding.y + blurPadding) / rectHeight, 1);
        var pivotAdjust = new Vector3(rectWidth* (0.5f - rectTransform.pivot.x), rectHeight * (0.5f - rectTransform.pivot.y), 0);

        // fitRotationInsideOriginalBounds scales the rotated image so that it fits inside the bounds of the non-rotated image.
        float scaleFactor = 1f;
        if (fitRotationInsideOriginalBounds && rotation != 0f)
        {
            var angleRad = rotation * Mathf.Deg2Rad;
            var cosTheta = Mathf.Abs(Mathf.Cos(angleRad));
            var sinTheta = Mathf.Abs(Mathf.Sin(angleRad));
            var scaleX = rectWidth / (rectWidth * cosTheta + rectHeight * sinTheta);
            var scaleY = rectHeight / (rectWidth * sinTheta + rectHeight * cosTheta);
            scaleFactor = Mathf.Min(scaleX, scaleY);
        }

        var localToWorldMatrix = rectTransform.localToWorldMatrix;
        for (int i = 0; i < 4; i++)
        {
            var localCorner = blitRegionCornersArray[i];
            localCorner -= pivotAdjust;

            var paddedBlurCorner = Vector3.Scale(localCorner, totalPaddingFactor);
            paddedBlurCorner = rotationQuat * paddedBlurCorner * scaleFactor;
            paddedBlurCorner += (Vector3)offset;
            paddedBlurCorner += pivotAdjust;
            worldCornersArray[i] = localToWorldMatrix.MultiplyPoint(paddedBlurCorner);

            var paddedBlitCorner = Vector3.Scale(localCorner, shapePaddingFactor);
            paddedBlitCorner = rotationQuat * paddedBlitCorner * scaleFactor;
            paddedBlitCorner += (Vector3)offset;
            paddedBlitCorner += pivotAdjust;
            blitRegionCornersArray[i] = localToWorldMatrix.MultiplyPoint(paddedBlitCorner);
        }

        var worldCenter = (blitRegionCornersArray[0] + blitRegionCornersArray[1] + blitRegionCornersArray[2] + blitRegionCornersArray[3]) * 0.25f;
        var deltaQ1 = (blitRegionCornersArray[3] - blitRegionCornersArray[0]) * 0.5f;
        var deltaQ2 = (blitRegionCornersArray[1] - blitRegionCornersArray[0]) * 0.5f;
        var transformationMatrix = new Matrix4x4();

        transformationMatrix.SetColumn(0, new Vector4(deltaQ1.x, deltaQ1.y, deltaQ1.z, 0f));
        transformationMatrix.SetColumn(1, new Vector4(deltaQ2.x, deltaQ2.y, deltaQ2.z, 0f));
        transformationMatrix.SetColumn(2, TransformationMatrixColumn2);
        transformationMatrix.SetColumn(3, new Vector4(worldCenter.x, worldCenter.y, worldCenter.z, 1f));
        TransformationMatrix = transformationMatrix;
    }

    public void CacheBlur(Canvas canvas, RectTransform rectTransform, Vector2 offset, float rotation, bool fitRotatedImageWithinBounds, Vector2 shapePadding, float blurPadding  = 0f, bool useFilterPadding = true, bool fillWholeScreen = false)
    {
        (cachedCanvas, cachedRectTransform, cachedOffset, cachedRotation, cachedFitRotatedImageWithinBounds, cachedShapePadding, cachedBlurPadding, cachedUseFilterPadding, cachedFillWholeScreen) = (canvas, rectTransform, offset, rotation, fitRotatedImageWithinBounds, shapePadding, blurPadding, useFilterPadding, fillWholeScreen);
        hasCachedBlur = true;
    }

    public void ComputeBlur((Camera, int) key, float filteringPadding)
    {
        if (!hasCachedBlur || key != GetCameraFeatureKey(cachedCanvas))
            return;

        ComputeBlurCommon(cachedCanvas, cachedRectTransform, cachedOffset, cachedRotation, cachedFitRotatedImageWithinBounds, cachedShapePadding, cachedBlurPadding, cachedUseFilterPadding ? filteringPadding : 0f, cachedFillWholeScreen);
        hasCachedBlur = false;
    }

    public void ComputeBlurCommon(Canvas canvas, RectTransform rectTransform, Vector2 offset, float rotation, bool fitRotationInsideOriginalBounds, Vector2 shapePadding, float blurPadding = 0f, float filteringPadding = 0f, bool fillWholeScreen = false)
    {
        var key = GetCameraFeatureKey(canvas);
        var camera = key.camera;

        if (!camera || !canvas || !canvas.isActiveAndEnabled || !rectTransform.gameObject.activeInHierarchy)
        {
            RemoveFromBlurList();
            return;
        }
        if (PresentInBlurList && (key != prevKey || priority != prevPriority || unrankedLayer != prevUnrankedLayer))
        {
            RemoveFromBlurList();
            PlaceInBlurList(key);
        }
        
        if (fillWholeScreen)
        {
            if (!PresentInBlurList)
                PlaceInBlurList(key);

            return;
        }

        blurPadding = (blurPadding + filteringPadding) / canvas.scaleFactor;
        shapePadding = (shapePadding + new Vector2(filteringPadding, filteringPadding)) / canvas.scaleFactor;

        if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
            WorldCamera = canvas.worldCamera;
        else if (canvas.renderMode == RenderMode.WorldSpace)
            WorldCamera = canvas.worldCamera ?? key.camera;
        else
            WorldCamera = null;

        if (blurPreset)
            _activeSettings = blurPreset.Settings[blurPreset.preview >= 0 ? blurPreset.preview : QualitySettings.GetQualityLevel()];

        GetScaledWorldCorners(rectTransform, offset, rotation, fitRotationInsideOriginalBounds, shapePadding, blurPadding, worldCorners);

        var canvasCamera = WorldCamera ?? key.camera;

#if XR_MANAGEMENT_INSTALLED
        useXR = XRSettings.enabled && canvasCamera.stereoTargetEye == StereoTargetEyeMask.Both;
        var xrScale = useXR ? new((float)XRSettings.eyeTextureWidth / canvasCamera.pixelWidth, (float)XRSettings.eyeTextureHeight / canvasCamera.pixelHeight) : Vector2.one;
#else
        useXR = false;
        var xrScale = Vector2.one;
#endif

        var cameraRectOffset = new Vector3(-canvasCamera.pixelRect.x, -canvasCamera.pixelRect.y);
        var cornersChanged = false;

        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            for (int i = 0; i < 4; i++)
            {
                var vp = canvasCamera.ScreenToViewportPoint(worldCorners[i]);
                ScreenCorners[i] = canvasCamera.ViewportToScreenPoint(vp) + cameraRectOffset;
                if (prevScreenCorners[i] == ScreenCorners[i])
                    continue;

                prevScreenCorners[i] = ScreenCorners[i];
                cornersChanged = true;
            }
        }
        else
        {
            var hasVerticesInFrontOfCamera = false;
            var camPlane = new Plane(canvasCamera.transform.forward, canvasCamera.transform.position);
            const float vertexEpsilon = 1e-5f;

            for (int i = 0; i < 4; i++)
            {
                var dot = Vector3.Dot(canvasCamera.transform.forward, canvasCamera.transform.position - worldCorners[i]);
                if (dot < 0)
                {
                    hasVerticesInFrontOfCamera = true;
                    continue;
                }

                var leftIdx = (int)Mathf.Repeat(i - 1, 4);
                var rightIdx = (int)Mathf.Repeat(i + 1, 4);
                bool hasLeftIntersect = TryGetIntersection(camPlane, worldCorners[i], worldCorners[leftIdx], out var intersect);
                bool hasRightIntersect = TryGetIntersection(camPlane, worldCorners[i], worldCorners[rightIdx], out var otherIntersect);
                if (hasLeftIntersect && hasRightIntersect)
                {
                    var leftDot = Vector3.Dot((worldCorners[i] - worldCorners[leftIdx]).normalized, canvasCamera.transform.forward);
                    var rightDot = Vector3.Dot((worldCorners[i] - worldCorners[rightIdx]).normalized, canvasCamera.transform.forward);
                    worldCorners[i] = camPlane.normal * vertexEpsilon + (leftDot < rightDot ? intersect : otherIntersect);
                }
                else if (hasLeftIntersect)
                {
                    worldCorners[i] = camPlane.normal * vertexEpsilon + intersect;
                }
                else if (hasRightIntersect)
                {
                    worldCorners[i] = camPlane.normal * vertexEpsilon + otherIntersect;
                }
                else if (TryGetIntersection(camPlane, worldCorners[i], worldCorners[(int)Mathf.Repeat(i + 2, 4)], out intersect))
                {
                    worldCorners[i] = camPlane.normal * vertexEpsilon + intersect;
                }
            }

            if (!hasVerticesInFrontOfCamera)
            {
                RemoveFromBlurList();
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                if (useXR)
                {
                    ScreenCorners[i] = xrScale * (canvasCamera.WorldToScreenPoint(worldCorners[i], Camera.MonoOrStereoscopicEye.Left) + cameraRectOffset);
                    ScreenCornersRight[i] = xrScale * (canvasCamera.WorldToScreenPoint(worldCorners[i], Camera.MonoOrStereoscopicEye.Right) + cameraRectOffset);
                }
                else
                {
                    ScreenCorners[i] = canvasCamera.WorldToScreenPoint(worldCorners[i]) + cameraRectOffset;
                }

                if (prevScreenCorners[i] == ScreenCorners[i])
                    continue;

                prevScreenCorners[i] = ScreenCorners[i];
                cornersChanged = true;
            }
        }

        if (!cornersChanged)
        {
            if ((hasVisiblePixels || hasVisiblePixelsRight) && !PresentInBlurList)
                PlaceInBlurList(key);

            return;
        }

#if XR_MANAGEMENT_INSTALLED
        var pixelWidth = useXR ? XRSettings.eyeTextureWidth : canvasCamera.pixelWidth;
        var pixelHeight = useXR ? XRSettings.eyeTextureHeight : canvasCamera.pixelHeight;
#else
        var pixelWidth = canvasCamera.pixelWidth;
        var pixelHeight = canvasCamera.pixelHeight;
#endif

        minX = minY = float.PositiveInfinity;
        maxX = maxY = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            var point = ScreenCorners[i];
            minX = Mathf.Min(point.x, minX);
            minY = Mathf.Min(point.y, minY);
            maxX = Mathf.Max(point.x, maxX);
            maxY = Mathf.Max(point.y, maxY);
        }

        minX = Mathf.Max(minX, 0);
        maxX = Mathf.Min(maxX, pixelWidth);
        minY = Mathf.Max(minY, 0);
        maxY = Mathf.Min(maxY, pixelHeight);

        hasVisiblePixels = maxX > 0 && maxY > 0 && minX < pixelWidth && minY < pixelHeight && maxX - minX > 0 && maxY - minY > 0;

        if (useXR)
        {
            minXRight = minYRight = float.PositiveInfinity;
            maxXRight = maxYRight = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                var point = ScreenCornersRight[i];
                minXRight = Mathf.Min(point.x, minXRight);
                minYRight = Mathf.Min(point.y, minYRight);
                maxXRight = Mathf.Max(point.x, maxXRight);
                maxYRight = Mathf.Max(point.y, maxYRight);
            }

            minXRight = Mathf.Max(minXRight, 0);
            maxXRight = Mathf.Min(maxXRight, pixelWidth);
            minYRight = Mathf.Max(minYRight, 0);
            maxYRight = Mathf.Min(maxYRight, pixelHeight);

            hasVisiblePixelsRight = maxXRight > 0 && maxYRight > 0 && minXRight < pixelWidth && minYRight < pixelHeight && maxXRight - minXRight > 0 && maxYRight - minYRight > 0;
        }
        else
        {
            hasVisiblePixelsRight = false;
        }

        switch (hasVisiblePixels || hasVisiblePixelsRight)
        {
            case true when !PresentInBlurList:
                PlaceInBlurList(key);
                break;
            case false when PresentInBlurList:
                RemoveFromBlurList();
                break;
        }

        if (!hasVisiblePixels && !hasVisiblePixelsRight)
            return;

        const float epsilon = 1e-4f;
        IsAngled = useXR ||
                   Math.Abs(ScreenCorners[0].x - ScreenCorners[1].x) > epsilon ||
                   Math.Abs(ScreenCorners[1].y - ScreenCorners[2].y) > epsilon ||
                   Math.Abs(ScreenCorners[2].x - ScreenCorners[3].x) > epsilon ||
                   Math.Abs(ScreenCorners[3].y - ScreenCorners[0].y) > epsilon;

        prevKey = key;
        BlurRegion = ComputeBlurRegion(minX, minY, maxX, maxY);
        if (useXR)
            BlurRegionRight = ComputeBlurRegion(minXRight, minYRight, maxXRight, maxYRight);
    }

    public static Vector4 ComputeBlurRegion(float minX, float minY, float maxX, float maxY)
    {
        var (intMinX, intMinY, intMaxX, intMaxY) = (Mathf.RoundToInt(minX), Mathf.RoundToInt(minY), Mathf.RoundToInt(maxX), Mathf.RoundToInt(maxY));
        var blurRegion = new Vector4(intMinX, intMinY, intMaxX - intMinX, intMaxY - intMinY);
        return blurRegion;
    }

    public Vector4 ComputeBlurRegion(float renderScale = 1f)
    {
        var (intMinX, intMinY, intMaxX, intMaxY) = (Mathf.RoundToInt(minX * renderScale), Mathf.RoundToInt(minY * renderScale), Mathf.RoundToInt(maxX * renderScale), Mathf.RoundToInt(maxY * renderScale));
        var blurRegion = new Vector4(intMinX, intMinY, intMaxX - intMinX, intMaxY - intMinY);
        return blurRegion;
    }
}
}