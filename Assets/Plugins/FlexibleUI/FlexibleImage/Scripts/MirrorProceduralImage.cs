using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[RequireComponent(typeof(FlexibleImage))]
public class MirrorProceduralImage : MonoBehaviour
{
    [SerializeField] private FlexibleImage toMirror;
    [SerializeField] private float mirrorOffset;
    [SerializeField] private float mirrorStroke;
    [SerializeField] private float mirrorSizeModifier;
    [SerializeField] private float mirrorSoftness;
    [SerializeField] private float mirrorRotation;
    [SerializeField] private float mirrorUVRect;
    [SerializeField] private float mirrorCutout;
    [SerializeField] private float mirrorSdfCutoutPosition;
    [SerializeField] private float mirrorSdfCutoutSize;
    [SerializeField] private float mirrorSdfCutoutRotation;
    [SerializeField] private float mirrorCornerChamfer;
    [SerializeField] private float mirrorCornerConcavity;
    [SerializeField] private float mirrorCollapsedCornerChamfer;
    [SerializeField] private float mirrorCollapsedCornerConcavity;
    [SerializeField] private float mirrorCollapseEdgeAmount;
    [SerializeField] private float mirrorCollapseEdgePosition;
    [SerializeField] private float mirrorPrimaryColorFade;
    [SerializeField] private float mirrorOutlineWidth;
    [SerializeField] private float mirrorSecondaryGradientPosition;
    [SerializeField] private float mirrorRadialGradientSize;
    [SerializeField] private float mirrorRadialGradientStrength;
    [SerializeField] private float mirrorAngleGradientStrength;
    [SerializeField] private float mirrorAngleGradientAngle;
    [SerializeField] private float mirrorSdfGradientInnerDistance;
    [SerializeField] private float mirrorSdfGradientOuterDistance;
    [SerializeField] private float mirrorSdfGradientInnerReach;
    [SerializeField] private float mirrorSdfGradientOuterReach;
    [SerializeField] private float mirrorPatternDensity;
    [SerializeField] private float mirrorPatternSpeed;
    [SerializeField] private float mirrorPatternCellParam;

    private ProceduralProperties prevTargetProperties = new();
    private FlexibleImage target;

    protected void OnEnable() => target = GetComponent<FlexibleImage>();

#if UNITY_EDITOR
    protected void OnValidate() => target = GetComponent<FlexibleImage>();
#endif

    void LateUpdate()
    {
        if (!toMirror)
            return;

        var mirroredProperties = toMirror.PrimaryProceduralProperties;
        var targetProperties = target.PrimaryProceduralProperties;
        prevTargetProperties.Copy(targetProperties);

        if (mirrorOffset != 0) targetProperties.offset = mirroredProperties.offset * mirrorOffset;
        if (mirrorStroke != 0) targetProperties.stroke = mirroredProperties.stroke * mirrorStroke;
        if (mirrorSizeModifier != 0) targetProperties.sizeModifier = mirroredProperties.sizeModifier * mirrorSizeModifier;
        if (mirrorSoftness != 0) targetProperties.softness = mirroredProperties.softness * mirrorSoftness;
        if (mirrorRotation != 0) targetProperties.rotation = mirroredProperties.rotation * mirrorRotation;
        if (mirrorUVRect != 0) targetProperties.uvRect = mirroredProperties.uvRect * mirrorUVRect;
        if (mirrorCutout != 0) targetProperties.simpleCutout = mirroredProperties.simpleCutout * mirrorCutout;
        if (mirrorSdfCutoutPosition != 0) targetProperties.sdfCutoutPosition = mirroredProperties.sdfCutoutPosition * mirrorSdfCutoutPosition;
        if (mirrorSdfCutoutSize != 0) targetProperties.sdfCutoutSize = mirroredProperties.sdfCutoutSize * mirrorSdfCutoutSize;
        if (mirrorSdfCutoutRotation != 0) targetProperties.sdfCutoutRotation = mirroredProperties.sdfCutoutRotation * mirrorSdfCutoutRotation;
        if (mirrorCornerChamfer != 0) targetProperties.cornerChamfer = mirroredProperties.cornerChamfer * mirrorCornerChamfer;
        if (mirrorCornerConcavity != 0) targetProperties.cornerConcavity = mirroredProperties.cornerConcavity * mirrorCornerConcavity;
        if (mirrorCollapsedCornerChamfer != 0) targetProperties.collapsedCornerChamfer = mirroredProperties.collapsedCornerChamfer * mirrorCollapsedCornerChamfer;
        if (mirrorCollapsedCornerConcavity != 0) targetProperties.collapsedCornerConcavity = mirroredProperties.collapsedCornerConcavity * mirrorCollapsedCornerConcavity;
        if (mirrorCollapseEdgeAmount != 0)
        {
            target.EdgeCollapseAmountIsAbsolute = toMirror.EdgeCollapseAmountIsAbsolute;
            if (target.EdgeCollapseAmountIsAbsolute)
                targetProperties.collapseEdgeAmountAbsolute = mirroredProperties.collapseEdgeAmountAbsolute * mirrorCollapseEdgeAmount;
            else
                targetProperties.collapseEdgeAmount = mirroredProperties.collapseEdgeAmount * mirrorCollapseEdgeAmount;
        }
        if (mirrorCollapseEdgePosition != 0) targetProperties.collapseEdgePosition = mirroredProperties.collapseEdgePosition * mirrorCollapseEdgePosition;
        if (mirrorPrimaryColorFade != 0) targetProperties.primaryColorFade = (byte)Mathf.Clamp(mirroredProperties.primaryColorFade * mirrorPrimaryColorFade, 0, 255);
        if (mirrorOutlineWidth != 0) targetProperties.outlineWidth = mirroredProperties.outlineWidth * mirrorOutlineWidth;
        if (mirrorSecondaryGradientPosition != 0) targetProperties.proceduralGradientPosition = mirroredProperties.proceduralGradientPosition * mirrorSecondaryGradientPosition;
        if (mirrorRadialGradientSize != 0) targetProperties.radialGradientSize = mirroredProperties.radialGradientSize * mirrorRadialGradientSize;
        if (mirrorRadialGradientStrength != 0) targetProperties.radialGradientStrength = mirroredProperties.radialGradientStrength * mirrorRadialGradientStrength;
        if (mirrorAngleGradientStrength != 0) targetProperties.angleGradientStrength = mirroredProperties.angleGradientStrength * mirrorAngleGradientStrength;
        if (mirrorAngleGradientAngle != 0) targetProperties.proceduralGradientAngle = mirroredProperties.proceduralGradientAngle * mirrorAngleGradientAngle;
        if (mirrorSdfGradientInnerDistance != 0) targetProperties.sdfGradientInnerDistance = mirroredProperties.sdfGradientInnerDistance * mirrorSdfGradientInnerDistance;
        if (mirrorSdfGradientOuterDistance != 0) targetProperties.sdfGradientOuterDistance = mirroredProperties.sdfGradientOuterDistance * mirrorSdfGradientOuterDistance;
        if (mirrorSdfGradientInnerReach != 0) targetProperties.sdfGradientInnerReach = mirroredProperties.sdfGradientInnerReach * mirrorSdfGradientInnerReach;
        if (mirrorSdfGradientOuterReach != 0) targetProperties.sdfGradientOuterReach = mirroredProperties.sdfGradientOuterReach * mirrorSdfGradientOuterReach;
        if (mirrorPatternDensity != 0) targetProperties.patternDensity = mirroredProperties.patternDensity * mirrorPatternDensity;
        if (mirrorPatternSpeed != 0) targetProperties.patternSpeed = mirroredProperties.patternSpeed * mirrorPatternSpeed;
        if (mirrorPatternCellParam != 0) targetProperties.patternCellParam = mirroredProperties.patternCellParam * mirrorPatternCellParam;

        if (!targetProperties.ValuesEqual(prevTargetProperties))
        {
            target.SetVerticesDirty();
            target.SetRaycastAreaDirty();
        }
    }
}
}
