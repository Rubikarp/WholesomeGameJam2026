using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public interface IBlur
{
    public UIBlurCommon Common { get; }
    public float Alpha { get; }
    public bool ActiveAtZeroAlpha { get; }
    public bool IsAngled => Common.IsAngled;
    public bool CanBatch { get; }
    public bool FillEntireRenderTexture { get; }
    public int RequestedMipLevels => 0;
    public BlurSettings Settings => Common.ActiveSettings;
    public Matrix4x4 Matrix => Common.TransformationMatrix;
    public int Layer => Common.LayerRank;
    public int Priority => Common.priority;
    public float MinX(bool right = false) => Common.MinX(right);
    public float MaxX(bool right = false) => Common.MaxX(right);
    public float MinY(bool right = false) => Common.MinY(right);
    public float MaxY(bool right = false) => Common.MaxY(right);
    public bool HasVisiblePixels(bool right = false) => Common.HasVisiblePixels(right);
}
}
