using UnityEngine;
using UnityEngine.Events;

public class MonoBehaviourEvents : MonoBehaviour
{
    [SerializeField] private UnityEvent awakeEvent;
    [SerializeField] private UnityEvent startEvent;
    [SerializeField] private UnityEvent onEnableEvent;
    [SerializeField] private UnityEvent onDisableEvent;
    [SerializeField] private UnityEvent onDestroyEvent;

    private void Awake() => awakeEvent?.Invoke();

    private void Start() => startEvent?.Invoke();

    private void OnEnable() => onEnableEvent?.Invoke();

    private void OnDisable() => onDisableEvent?.Invoke();

    private void OnDestroy() => onDestroyEvent?.Invoke();
}