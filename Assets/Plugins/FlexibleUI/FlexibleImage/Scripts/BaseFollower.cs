using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public abstract class BaseFollower : BaseMeshEffect
{
#if UNITY_EDITOR
    public abstract void RefreshFromInspector();
#endif

    public enum ScaleBehavior { UseLesserScale, UseVerticalScale, UseHorizontalScale, Stretch }

    [SerializeField] public ScaleBehavior scaleBehavior = ScaleBehavior.UseLesserScale;
    [SerializeField] public FlexibleImage toFollow;

    protected ScaleBehavior previousScaleBehaviour;
    protected Matrix4x4 matrix;

    protected Matrix4x4 GetModifiedMatrix(Matrix4x4 originalMatrix, Vector2 scaleBehaviour)
    {
        if (scaleBehavior == ScaleBehavior.Stretch || Mathf.Abs(scaleBehaviour.x - scaleBehaviour.y) < 1e-4f)
            return originalMatrix;

        var targetScale = scaleBehavior switch
        {
            ScaleBehavior.UseVerticalScale => scaleBehaviour.y,
            ScaleBehavior.UseHorizontalScale => scaleBehaviour.x,
            ScaleBehavior.UseLesserScale => Mathf.Min(scaleBehaviour.x, scaleBehaviour.y),
            _ => scaleBehaviour.x
        };

        var scaleXVector = new Vector3(originalMatrix.m00, originalMatrix.m10, originalMatrix.m20);
        var scaleYVector = new Vector3(originalMatrix.m01, originalMatrix.m11, originalMatrix.m21);
        var currentScaleX = scaleXVector.magnitude;
        var currentScaleY = scaleYVector.magnitude;
        var modifiedMatrix = originalMatrix;

        if (currentScaleX > 1e-4)
        {
            var normalizedScaleX = scaleXVector / currentScaleX;
            modifiedMatrix.m00 = normalizedScaleX.x * targetScale;
            modifiedMatrix.m10 = normalizedScaleX.y * targetScale;
            modifiedMatrix.m20 = normalizedScaleX.z * targetScale;
        }
        if (currentScaleY > 1e-4)
        {
            var normalizedScaleY = scaleYVector / currentScaleY;
            modifiedMatrix.m01 = normalizedScaleY.x * targetScale;
            modifiedMatrix.m11 = normalizedScaleY.y * targetScale;
            modifiedMatrix.m21 = normalizedScaleY.z * targetScale;
        }
        return modifiedMatrix;
    }
}
}
