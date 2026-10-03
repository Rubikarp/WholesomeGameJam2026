using Alchemy.Inspector;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

/// <summary>
/// UiBehavior cliquable qui émet la position sur une grille locale au RectTransform.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GridClickBehavior : MonoBehaviour, IPointerClickHandler, IPointerMoveHandler, IPointerExitHandler
{
    public enum EGridOrigin
    {
        BottomLeft,
        TopLeft,
        TopRight,
        BottomRight,
    }
    
    [field: Header("Grille")]
    [field: SerializeField] public Vector2Int GridSize { get; private set; } = new Vector2Int(4, 4);
    [field: SerializeField] public EGridOrigin GridOrigin { get; private set; } = EGridOrigin.TopLeft;

    [field: Header("Interaction")]
    [field: SerializeField] public bool IsInteractable { get; set; } = true;
 
    [Header("Signal")]
    public UnityEvent<Vector2Int> OnGridCellClicked;
 
    [Header("Debug")]
    [SerializeField , ReadOnly] private Vector2Int HoveredCell = new Vector2Int(-1, -1);
 
    private RectTransform _rectTransform;
    
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }
 
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!IsInteractable) { return; }
 
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }
 
        HoveredCell = LocalPointToGridCell(localPoint);
        OnGridCellClicked.Invoke(HoveredCell);
    }
 
    public void OnPointerMove(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }
 
        HoveredCell = LocalPointToGridCell(localPoint);
    }
 
    public void OnPointerExit(PointerEventData eventData)
    {
        HoveredCell = new Vector2Int(-1, -1);
    }
 
    private Vector2Int LocalPointToGridCell(Vector2 localPoint)
    {
        Rect rect = _rectTransform.rect;
 
        float normalizedX = (localPoint.x - rect.xMin) / rect.width;
        float normalizedY = (localPoint.y - rect.yMin) / rect.height;
 
        // Unity: Y local monte vers le haut — on flip selon l'origine choisie
        float x = (GridOrigin == EGridOrigin.TopRight || GridOrigin == EGridOrigin.BottomRight)
            ? 1f - normalizedX
            : normalizedX;

        float y = (GridOrigin == EGridOrigin.TopLeft || GridOrigin == EGridOrigin.TopRight)
            ? 1f - normalizedY
            : normalizedY;
        
        int column = Mathf.Clamp(Mathf.FloorToInt(x * GridSize.x), 0, GridSize.x - 1);
        int row    = Mathf.Clamp(Mathf.FloorToInt(y * GridSize.y), 0, GridSize.y - 1);
 
        return new Vector2Int(column, row);
    }
}