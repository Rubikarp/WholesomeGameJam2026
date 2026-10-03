using PrimeTween;
using NaughtyAttributes;
using UnityEngine;
using UnityEngine.EventSystems;
using Alchemy.Inspector;

[RequireComponent(typeof(RectTransform))]
public class ScrollView : UIBehaviour
{
    [Header("References")] 
    [SerializeField] private RectTransform _content;
    [SerializeField] private RectTransform _viewport;
    
    [SerializeField] private Vector2 _normalizedPosition;

    [ShowInInspector] 
    public Vector2 Position
    {
        get
        {
            if (_content == null || _viewport == null) return Vector2.zero;
            Vector2 origin = ComputeOrigin();
            return new Vector2(
                origin.x - _content.anchoredPosition.x,
                _content.anchoredPosition.y - origin.y
            );
        }
        set
        {
            if (_content == null || _viewport == null) return;
            
            Vector2 origin = ComputeOrigin();
            _content.anchoredPosition = new Vector2(
                origin.x - value.x,
                origin.y + value.y
            );
            _normalizedPosition = ToNormalized(_content.anchoredPosition);
        }
    }
    [ShowInInspector] 
    public Vector2 NormalizedPosition
    {
        get
        {
            if (_content == null || _viewport == null) return Vector2.zero;
            _normalizedPosition = ToNormalized(_content.anchoredPosition);
            return _normalizedPosition;
        }
        set
        {
            if (_content == null || _viewport == null) return;
            _content.anchorMin = Vector2.up;
            _content.anchorMax = Vector2.up;
            _content.anchoredPosition = ToAnchoredPosition(value);
            _normalizedPosition = value;
        }
    }
    [ShowNativeProperty] public Vector2 ContentSize => _content != null ? _content.rect.size : Vector2.zero;
    [ShowNativeProperty] public Vector2 ViewportSize => _viewport != null ? _viewport.rect.size : Vector2.zero;
    [ShowNativeProperty] public Vector2 AvailableSpace => Vector2.Max(Vector2.zero, ContentSize - ViewportSize);

    protected override void Awake()
    {
        base.Awake();
        if (_viewport == null) _viewport = GetComponent<RectTransform>();
        if (_content == null && _viewport.childCount > 0)
            _content = _viewport.GetChild(0).GetComponent<RectTransform>();
        _content.anchorMin = Vector2.up;
        _content.anchorMax = Vector2.up;
    }

    private Vector2 ComputeOrigin()
    {
        Vector2 sizeC = ContentSize;
        Vector2 pivC = _content.pivot;
        return new Vector2(
            pivC.x * sizeC.x,
            -(1f - pivC.y) * sizeC.y
        );
    }
    private Vector2 ToAnchoredPosition(Vector2 t)
    {
        Vector2 origin = ComputeOrigin();
        Vector2 available = AvailableSpace;
        return new Vector2(
            origin.x - t.x * available.x,
            origin.y + t.y * available.y
        );
    }
    private Vector2 ToNormalized(Vector2 anchoredPos)
    {
        Vector2 origin = ComputeOrigin();
        Vector2 available = AvailableSpace;
        return new Vector2(
            available.x > 0.001f ? (origin.x - anchoredPos.x) / available.x : 0f,
            available.y > 0.001f ? (anchoredPos.y - origin.y) / available.y : 0f
        );
    }
    public Vector2 GetNormalizedPositionOf(RectTransform target)
    {
        if (_content == null || target == null) return Vector2.zero;

        Vector2 localPos = _content.InverseTransformPoint(target.position);
        Vector2 available = AvailableSpace;
        Vector2 viewportSize = ViewportSize;

        Vector2 scrollTarget = new Vector2(
            localPos.x - viewportSize.x * 0.5f,
            -localPos.y - viewportSize.y * 0.5f
        );

        return new Vector2(
            available.x > 0.001f ? Mathf.Clamp01(scrollTarget.x / available.x) : 0f,
            available.y > 0.001f ? Mathf.Clamp01(scrollTarget.y / available.y) : 0f
        );
    }
}