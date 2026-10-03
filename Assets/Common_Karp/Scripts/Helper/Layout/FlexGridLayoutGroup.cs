using Alchemy.Inspector;
using UnityEngine.UI;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class FlexGridLayoutGroup : LayoutGroup
{
    public RectTransform RectTransform => rectTransform;

    [Header("Size")]
    public Vector2Int cellCount = Vector2Int.one * 2;
    [field: SerializeField, ReadOnly] public Vector2 CellSize { get; private set; }
    [field: SerializeField, ReadOnly] public Vector2 GridSpace { get; private set; }

    [Header("Spacing")]
    public bool useRatioSpacing = false;
    [ShowIf("useRatioSpacing")] public Vector2 ratioSpacing = Vector2.zero;
    [DisableIf("useRatioSpacing")] public Vector2 rawSpacing = Vector2.zero;

    protected void OnValidate()
    {
        cellCount = new Vector2Int(Mathf.Max(1, cellCount.x), Mathf.Max(1, cellCount.y));

#if UNITY_EDITOR
        base.OnValidate();
#endif
    }


    public override void SetLayoutHorizontal()
    {
        CalculateLayoutInputHorizontal();
    }
    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();

        // Calculate the number of rows and columns based on the number of cells
        GridSpace = new Vector2(
            rectTransform.rect.width - (padding.left + padding.right),
            rectTransform.rect.height - (padding.top + padding.bottom)
        );

        cellCount = new Vector2Int(Mathf.Max(1, cellCount.x), Mathf.Max(1, cellCount.y));
        Vector2Int spaceQuantity = cellCount - Vector2Int.one;

        if (useRatioSpacing)
        {
            rawSpacing = (GridSpace / spaceQuantity) * ratioSpacing;
            if(spaceQuantity.x == 0) rawSpacing.x = 0;
            if(spaceQuantity.y == 0) rawSpacing.y = 0;
        }
        else
        {
            ratioSpacing = (spaceQuantity * rawSpacing) / GridSpace;
        }

        Vector2 CellsTotalSpace = GridSpace - (spaceQuantity * rawSpacing);
        CellSize = CellsTotalSpace / cellCount;

        for (int i = 0; i < rectChildren.Count; i++)
        {
            int column = i % cellCount.x;
            int row = i / cellCount.x;

            var cellRect = rectChildren[i];
            cellRect.anchorMin = Vector2.up;
            cellRect.anchorMax = Vector2.up;

            Vector2 position = new Vector2(
                padding.left + (column * (CellSize.x + rawSpacing.x)),
                padding.top + (row * (CellSize.y + rawSpacing.y))
                );

            SetChildAlongAxis(cellRect, 0, position.x, CellSize.x);
            SetChildAlongAxis(cellRect, 1, position.y, CellSize.y);
        }

        SetMinPrefFlexSize();
    }

    public override void SetLayoutVertical()
    {
        CalculateLayoutInputHorizontal();
    }
    public override void CalculateLayoutInputVertical()
    {
        SetMinPrefFlexSize();
    }

    private void SetMinPrefFlexSize()
    {
        // Axe 0 = horizontal, Axe 1 = vertical
        SetLayoutInputForAxis(rectTransform.rect.width, rectTransform.rect.width, rectTransform.rect.width, 0);
        SetLayoutInputForAxis(rectTransform.rect.height, rectTransform.rect.height, rectTransform.rect.height, 1);
    }
}
