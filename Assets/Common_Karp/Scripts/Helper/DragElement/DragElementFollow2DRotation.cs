using UnityEngine;

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(DragElementFollow))]
public class DragElementFollow2DRotation : MonoBehaviour
{
    private DragElementFollow followComponent;

    public RectTransform RectTransform => rectTransform;
    private RectTransform rectTransform;

    [Header("Settings")]
    [SerializeField] private float maxBendAngle = 60f;
    [SerializeField] private AnimationCurve bendCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        followComponent = GetComponent<DragElementFollow>();
    }

    private void LateUpdate()
    {
        Vector3 velocityRatio = followComponent.Velocity / followComponent.maxVelocity;
        float bendAmount = Mathf.Sign(velocityRatio.x) * bendCurve.Evaluate(Mathf.Abs(velocityRatio.x)) * Mathf.PI * (maxBendAngle / 360);
        RectTransform.rotation = Quaternion.LookRotation(Vector3.forward, new Vector3(Mathf.Sin(bendAmount), Mathf.Cos(bendAmount), 0f));
    }
}