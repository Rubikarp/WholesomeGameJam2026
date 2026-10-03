using UnityEngine;
using UnityEngine.EventSystems;
using Alchemy.Inspector;
using Alchemy.Inspector;

[RequireComponent(typeof(ScrollView))]
public class ScrollViewDragController : UIBehaviour
{
    public enum EMovementType
    {
        Clamped,
        Elastic
    }
    
    [Header("References")]
    [SerializeField] private Canvas _canvas;
    [SerializeField] private ScrollView _scrollView;

    [Header("Settings")]
    [SerializeField] private EMovementType _movementType = EMovementType.Clamped;
    [SerializeField, Min(0f)] private float _sensitivity = 1f;
    [SerializeField] private bool _useInertia = true;
    
    [SerializeField, ShowIf("_useInertia"), Range(0f, 1f)] private float _drag = 0.92f;
    [SerializeField, ShowIf("_useInertia"), Min(0f)] private float _minVelocity = 0.5f;

    [SerializeField, ShowIf("IsElastic"), Range(0f, 1f)] private float _elasticityRange = .1f;
    [SerializeField, ShowIf("IsElastic"), Min(0f)] private float _elasticReturnSpeed = 8f;
    private bool IsElastic => _movementType == EMovementType.Elastic;
    
    private Vector2 _velocity;
    private bool _isDecelerating;
    private bool _isDragging;

    protected override void Awake()
    {
        base.Awake();
        if (_scrollView == null) _scrollView = GetComponent<ScrollView>();
        if(_canvas == null) _canvas = GetComponentInParent<Canvas>();
    }

    public void BeginDrag(BaseEventData eventData) => BeginDrag();
    public void BeginDrag(PointerEventData eventData) => BeginDrag();
    public void BeginDrag()
    {
        _velocity = Vector2.zero;
        _isDecelerating = false;
        _isDragging = true;
    }

    public void Drag(BaseEventData eventData) => Drag(eventData as PointerEventData);
    public void Drag(PointerEventData eventData) => Drag(eventData.delta);
    public void Drag(Vector2 displacement)
    {
        float scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;
        _velocity = displacement / scaleFactor * _sensitivity;
        ApplyDelta(_velocity);
    }

    public void EndDrag(BaseEventData eventData) => EndDrag(eventData as PointerEventData);
    public void EndDrag(PointerEventData eventData) => EndDrag(eventData.delta);
    public void EndDrag(Vector2 displacement)
    {
        _isDragging = false;
        if (_useInertia)
        {
            float scaleFactor = _canvas != null ? _canvas.scaleFactor : 1f;
            _velocity = displacement / scaleFactor * _sensitivity;
            _isDecelerating = true;
        }
        else
        {
            _velocity = Vector2.zero;
            _isDecelerating = false;
        }
    }

    private void Update()
    {
        if (IsElastic && !_isDragging)
            ApplyElasticReturn();

        if (!_isDecelerating) return;

        _velocity *= _drag;

        if (_velocity.sqrMagnitude < _minVelocity * _minVelocity)
        {
            _velocity = Vector2.zero;
            _isDecelerating = false;
            return;
        }

        ApplyDelta(_velocity);
    }

    private void ApplyDelta(Vector2 pixels)
    {
        Vector2 rawTarget = _scrollView.Position + new Vector2(-pixels.x, pixels.y);
        Vector2 available = _scrollView.AvailableSpace;
        Vector2 clamped = new Vector2(
            Mathf.Clamp(rawTarget.x, 0f, available.x),
            Mathf.Clamp(rawTarget.y, 0f, available.y)
        );

        switch (_movementType)
        {
            case EMovementType.Elastic:
                Vector2 overflow = rawTarget - clamped;
                _scrollView.Position = clamped + overflow * _elasticityRange;
                break;
            default:
            case EMovementType.Clamped:
                _scrollView.Position = clamped;
                break;
        }
    }

    private void ApplyElasticReturn()
    {
        Vector2 current = _scrollView.Position;
        Vector2 available = _scrollView.AvailableSpace;
        Vector2 clamped = new Vector2(
            Mathf.Clamp(current.x, 0f, available.x),
            Mathf.Clamp(current.y, 0f, available.y)
        );

        if ((current - clamped).sqrMagnitude < 0.01f)
        {
            _scrollView.Position = clamped;
            return;
        }

        Vector2 returned = Vector2.Lerp(current, clamped, 1f - Mathf.Exp(-_elasticReturnSpeed * Time.deltaTime));
        _scrollView.Position = returned;
    }
}