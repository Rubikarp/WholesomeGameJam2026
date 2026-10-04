using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.InputSystem;

public class MenuParallax : MonoBehaviour
{
    private Camera _cam;
    [SerializeField, ReadOnly] private Vector3 initialPos;

    private void Start()
    {
        _cam = Camera.main;
        initialPos = transform.position;
    }

    private void Update()
    {
        Vector2 offset = _cam.ScreenToViewportPoint(Mouse.current.position.ReadValue()) * 0.1f;
        float offsetMultiplier = -transform.position.z;
        
        transform.position = initialPos + (Vector3)(offset * offsetMultiplier);
    }
}
