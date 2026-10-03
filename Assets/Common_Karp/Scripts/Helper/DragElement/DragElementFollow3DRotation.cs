using UnityEngine;

/// <summary>
/// Je me suis rendu compte qu'on allez rotate sur Z mais je me l'ai mis de coté pour l'ajouter dans mes scripts réutilisables
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(DragElementFollow))]
public class DragElementFollow3DRotation : MonoBehaviour
{
    private DragElementFollow followComponent;

    public RectTransform RectTransform => rectTransform;
    private RectTransform rectTransform;

    [Header("Settings")]
    [Tooltip("How sensitive the rotation is to the card's movement.")]
    [SerializeField] private float rotationSensibility = 0f;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        followComponent = GetComponent<DragElementFollow>();
    }

    private void Update()
    {
        Vector3 velocityRatio = followComponent.Velocity / followComponent.maxVelocity;
        Quaternion targetRotation = Quaternion.LookRotation(Vector3.forward - (velocityRatio * rotationSensibility), Vector3.up);
        RectTransform.rotation = targetRotation;
    }
}
