using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable]
public class SkewAnimData
{
    [Range(0, 1)] public float collapseEdgeAmount;
    [Min(0)] public float collapseEdgeAmountAbsolute;
    [Range(0, 1)] public float collapseEdgePosition;
    public float collapsedCornerChamfer;
    public float collapsedCornerConcavity;
}

[Serializable]
public class SkewConfig
{
    public QuadData.CollapsedEdgeType collapsedEdge;
    public bool collapseIntoParallelogram;
    public bool mirrorCollapse;
    public bool edgeCollapseAmountIsAbsolute;
}
}
