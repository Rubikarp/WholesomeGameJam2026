using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.UIElements.UxmlAttributeDescription;
public enum EGradientDir
{
    LeftToRight,
    TopToBottom,
    RightToLeft,
    BottomToTop,
}

[AddComponentMenu("UI/Image Gradient")]
public class ImageGradient : Image
{
    [Header("Corner Colors")]
    public Color fromColor = Color.white;
    public Color toColor = Color.white;
    public EGradientDir gradientDirection = EGradientDir.LeftToRight;

    public Color TopLeft
    {
        get
        {
            switch (gradientDirection)
            {
                case EGradientDir.LeftToRight:
                case EGradientDir.TopToBottom:
                default:
                    return fromColor;
                case EGradientDir.RightToLeft:
                case EGradientDir.BottomToTop:
                    return toColor;
            }
        }
    }
    public Color TopRight
    {
        get
        {
            switch (gradientDirection)
            {
                case EGradientDir.TopToBottom:
                case EGradientDir.RightToLeft:
                default:
                    return fromColor;
                case EGradientDir.LeftToRight:
                case EGradientDir.BottomToTop:
                    return toColor;
            }
        }
    }
    public Color BottomLeft
    {
        get
        {
            switch (gradientDirection)
            {
                case EGradientDir.LeftToRight:
                case EGradientDir.BottomToTop:
                default:
                    return fromColor;
                case EGradientDir.TopToBottom:
                case EGradientDir.RightToLeft:
                    return toColor;
            }
        }
    }
    public Color BottomRight
    {
        get
        {
            switch (gradientDirection)
            {
                case EGradientDir.BottomToTop:
                case EGradientDir.RightToLeft:
                default:
                    return fromColor;
                case EGradientDir.TopToBottom:
                case EGradientDir.LeftToRight:
                    return toColor;
            }
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        // Let Image generate its base mesh
        base.OnPopulateMesh(vh);

        if (vh.currentVertCount == 0)
            return;

        // Get rect info
        Rect rect = rectTransform.rect;
        Vector2 min = rect.min;
        Vector2 size = rect.size;
        if (size.x <= 0f) size.x = 1f;
        if (size.y <= 0f) size.y = 1f;

        // Recolor all vertices
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            UIVertex v = new UIVertex();
            vh.PopulateUIVertex(ref v, i);

            // v.position is in local rect space. Normalize to [0,1]
            Vector2 localPos = v.position;
            float nx = (localPos.x - min.x) / size.x; // 0..1 left->right
            float ny = (localPos.y - min.y) / size.y; // 0..1 bottom->top

            // bilinear interpolation of four corner colors
            Color bottom = Color.Lerp(BottomLeft, BottomRight, nx);
            Color top = Color.Lerp(TopLeft, TopRight, nx);
            Color final = Color.Lerp(bottom, top, ny);

            v.color = final * color;

            vh.SetUIVertex(v, i);
        }
    }
}