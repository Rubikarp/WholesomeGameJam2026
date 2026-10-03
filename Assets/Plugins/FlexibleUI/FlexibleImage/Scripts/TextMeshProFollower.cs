using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{ 
[RequireComponent(typeof(TMP_Text))]
public class TextMeshProFollower : BaseFollower
{
    [SerializeField][HideInInspector] private TMP_Text tmpText;

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        tmpText = GetComponent<TMP_Text>();
        if (toFollow == null)
            toFollow = GetComponentInParent<FlexibleImage>();
    }

    public override void RefreshFromInspector() => LateUpdate();
#endif

    protected override void OnTransformParentChanged()
    {
        base.OnTransformParentChanged();
        tmpText = GetComponent<TMP_Text>();
        if (toFollow == null)
            toFollow = GetComponent<FlexibleImage>();
    }

    protected override void Awake()
    {
        base.Awake();
        tmpText = GetComponent<TMP_Text>();
        if (toFollow == null)
            toFollow = GetComponentInParent<FlexibleImage>();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        tmpText.OnPreRenderText += OnPreRenderText;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        tmpText.OnPreRenderText -= OnPreRenderText;
    }

    void LateUpdate()
    {
        if (!toFollow)
            return;

        var newMatrix = toFollow.FollowerTransformationMatrix;
        if (newMatrix == matrix && scaleBehavior == previousScaleBehaviour)
            return;

        matrix = newMatrix;
        tmpText.ForceMeshUpdate();
    }

    public override void ModifyMesh(VertexHelper vh) { }

    private void OnPreRenderText(TMP_TextInfo textInfo)
    {
        if (!IsActive() || !toFollow)
            return;

        var transformMatrix = GetModifiedMatrix(matrix, toFollow.FinalVertexScale);
        for (int i = 0; i < textInfo.materialCount; i++)
        {
            var meshInfo = textInfo.meshInfo[i];
            var positionInToFollowSpace = new Vector2(transform.localPosition.x / transform.lossyScale.x * toFollow.transform.lossyScale.x, transform.localPosition.y / transform.lossyScale.y * toFollow.transform.lossyScale.y);
            var centerDiff = (Vector3)(positionInToFollowSpace - toFollow.FinalMeshCenter);

            for (int j = 0; j < meshInfo.vertexCount; j++)
            {
                var vertex = meshInfo.vertices[j];
                vertex += centerDiff;
                vertex = transformMatrix.MultiplyPoint3x4(vertex);
                vertex -= centerDiff;
                meshInfo.vertices[j] = vertex;
            }
            meshInfo.mesh.vertices = meshInfo.vertices;
        }
    }
}
}