using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
[RequireComponent(typeof(Graphic))]
public class GraphicFollower : BaseFollower
{
#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        if (toFollow == null)
            toFollow = GetComponentInParent<FlexibleImage>();
    }

    public override void RefreshFromInspector() => LateUpdate();
#endif

    protected override void OnTransformParentChanged()
    {
        base.OnTransformParentChanged();
        if (toFollow == null)
            toFollow = GetComponent<FlexibleImage>();
    }

    protected override void Awake()
    {
        base.Awake();
        toFollow = GetComponent<FlexibleImage>();
    }

    void LateUpdate()
    {
        if (!toFollow)
            return;

        var newMatrix = toFollow.FollowerTransformationMatrix;
        if (newMatrix == matrix && scaleBehavior == previousScaleBehaviour)
            return;

        matrix = newMatrix;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || !toFollow)
            return;

        var vertices = ListPool<UIVertex>.Get();
        vh.GetUIVertexStream(vertices);

        var transformMatrix = GetModifiedMatrix(matrix, toFollow.FinalVertexScale);
        var positionInToFollowSpace = new Vector2(transform.localPosition.x / transform.lossyScale.x * toFollow.transform.lossyScale.x, transform.localPosition.y / transform.lossyScale.y * toFollow.transform.lossyScale.y);
        var centerDiff = (Vector3)(positionInToFollowSpace - toFollow.FinalMeshCenter);

        for (int i = 0; i < vertices.Count; i++)
        {

            var vertex = vertices[i];
            var position = vertex.position;
            position += centerDiff;
            position = transformMatrix.MultiplyPoint3x4(position);
            position -= centerDiff;
            vertex.position = position;
            vertices[i] = vertex;
        }

        vh.Clear();
        vh.AddUIVertexTriangleStream(vertices);
        ListPool<UIVertex>.Release(vertices);
    }
}
}
