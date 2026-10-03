using System;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class StrokeAnimData
{
    public float stroke;
}

[Serializable]
public class StrokeConfig
{
    public QuadData.StrokeOriginLocation strokeOrigin = QuadData.StrokeOriginLocation.Center;
}
}
