using Alchemy.Inspector;
using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class DragElementFollow : MonoBehaviour
{
    public RectTransform RectTransform => rectTransform;
    private RectTransform rectTransform;

    [Header("Components")]
    [SerializeField, Required] protected DragElement linkedElement;

    [Header("Settings")]
    [Tooltip("How quickly it accelerates toward the target.")]
    [SerializeField] protected float motionStiffness = 200f;
    [Tooltip("Damping ratio for motion resistance.")]
    [SerializeField][Range(0f, 2f)] private float dampingRatio = 0.6f;
    [Space]
    [SerializeField] public Vector3 offset = Vector3.zero;
    [SerializeField, Min(1)] public float maxVelocity = 1000f;

    [Header("Info")]
    [SerializeField, ReadOnly] public Vector3 targetPos;
    [SerializeField, ReadOnly] public Vector3 trajectory;
    [SerializeField, ReadOnly] public float velocitySpeed;
    [field: SerializeField, ReadOnly] public Vector3 Velocity { get; private set; }

    protected virtual void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }
    protected virtual void Update()
    {
        SyncWithLinkedElement();
    }
    protected virtual void FixedUpdate()
    {
        FollowElement();
    }
    protected virtual void SyncWithLinkedElement()
    {
        //Force same parent
        if (transform.parent != linkedElement.RectTransform.parent)
        {
            transform.SetParent(linkedElement.RectTransform.parent, true);
        }
        //Force to be next in parent order
        if (transform.GetSiblingIndex() != linkedElement.RectTransform.GetSiblingIndex() + 1)
        {
            transform.SetSiblingIndex(linkedElement.RectTransform.GetSiblingIndex() + 1);
        }
        //Force name
        if (transform.name != $"{linkedElement.name}-Visual")
        {
            transform.name = $"{linkedElement.name}-Visual";
        }
    }
    protected virtual void FollowElement()
    {
        targetPos = linkedElement.RectTransform.position + offset;
        trajectory = targetPos - RectTransform.position;

        // Check if we've reached the target and should stop
        if (HasReachedTarget())
        {
            SnapToTarget();
            return;
        }

        ApplyPhysicsMotion();
        velocitySpeed = Velocity.magnitude;
    }

    private bool HasReachedTarget()
    {
        const float POSITION_THRESHOLD = 0.1f;
        const float VELOCITY_THRESHOLD = 0.1f;

        return trajectory.sqrMagnitude < POSITION_THRESHOLD &&
               Velocity.sqrMagnitude < VELOCITY_THRESHOLD;
    }

    private void SnapToTarget()
    {
        Velocity = Vector3.zero;
        RectTransform.position = targetPos;
    }

    private void ApplyPhysicsMotion()
    {
        // Spring-damper system using damping ratio
        // dampingRatio < 1 = underdamped (bouncy)
        // dampingRatio = 1 = critically damped (smooth, no overshoot)
        // dampingRatio > 1 = overdamped (slow convergence)
        float criticalDamping = 2f * Mathf.Sqrt(motionStiffness);
        float actualDamping = dampingRatio * criticalDamping;

        Vector3 springForce = trajectory * motionStiffness;
        Vector3 dampingForce = -Velocity * actualDamping;
        Vector3 acceleration = springForce + dampingForce;

        Velocity += acceleration * Time.deltaTime;
        ClampVelocity();

        RectTransform.position += Velocity * Time.deltaTime;
    }

    private void ClampVelocity()
    {
        float sqrMaxVelocity = maxVelocity * maxVelocity;
        if (Velocity.sqrMagnitude > sqrMaxVelocity)
        {
            Velocity = Velocity.normalized * maxVelocity;
        }
    }

}