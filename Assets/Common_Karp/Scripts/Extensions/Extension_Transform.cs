using System.Runtime.CompilerServices;
using UnityEngine;

public static class Extension_Transform
{
    public static void DeleteChildrens(this Transform transform)
    {
        int childCount = transform.childCount;
        if (Application.isPlaying)
            for (int i = childCount - 1; i >= 0; i--)
            {
                Object.Destroy(transform.GetChild(i).gameObject);
            }
        else
            for (int i = childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(transform.GetChild(i).gameObject, true);
            }
    }

    public static void SetTop(this RectTransform rt, float top) => rt.offsetMax = new Vector2(rt.offsetMax.x, -top);
    public static void SetLeft(this RectTransform rt, float left) => rt.offsetMin = new Vector2(left, rt.offsetMin.y);
    public static void SetRight(this RectTransform rt, float right) => rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
    public static void SetBottom(this RectTransform rt, float bottom) => rt.offsetMin = new Vector2(rt.offsetMin.x, bottom);

    public static void AddOffsetHori(this RectTransform rt, float value)
    {
        rt.offsetMin = rt.offsetMax + Vector2.right * value;
        rt.offsetMax = rt.offsetMax + Vector2.right * value;
    }
    public static void AddOffsetVert(this RectTransform rt, float value)
    {
        rt.offsetMin = rt.offsetMax + Vector2.up * value;
        rt.offsetMax = rt.offsetMax + Vector2.up * value;
    }
    public static RectTransform AsRect(this Transform transform)
    {
        if (transform == null) return null;
        if (transform is RectTransform == false)
        {
            Debug.LogError($"Transform {transform.name} is not a RectTransform");
            return null;
        }
        return transform as RectTransform;
    }
}

public static class Extension_RectTransform
{
    public static Vector3 GetWorldPosOfCanvasElement(this RectTransform element)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(element, element.position, Camera.main, out var result);
        return result;
    }

    public static Vector4 ComputePadding(this RectTransform source, RectTransform cropArea) => source.ComputePadding(cropArea, Vector2.zero);
    public static Vector4 ComputePadding(this RectTransform source, RectTransform cropArea, Vector2 offsetPrct)
    {
        // Convert both rects to world corners
        Vector3[] imageCorners = new Vector3[4];
        Vector3[] cropCorners = new Vector3[4];

        // imageCorners and cropCorners order:
        // [0] bottom-left, [1] top-left, [2] top-right, [3] bottom-right
        source.GetWorldCorners(imageCorners);
        cropArea.GetWorldCorners(cropCorners);

        float imageWidth = imageCorners[2].x - imageCorners[0].x;
        float imageHeight = imageCorners[1].y - imageCorners[0].y;

        // Apply offset to cropRect corners (e.g. pixels or local units)
        for (int i = 0; i < 4; i++)
        {
            cropCorners[i].x += offsetPrct.x * imageWidth;
            cropCorners[i].y += offsetPrct.y * imageHeight;
        }

        // Distances between crop and image edges
        float left = Mathf.Max(0, cropCorners[0].x - imageCorners[0].x);
        float right = Mathf.Max(0, imageCorners[2].x - cropCorners[2].x);
        float bottom = Mathf.Max(0, cropCorners[0].y - imageCorners[0].y);
        float top = Mathf.Max(0, imageCorners[1].y - cropCorners[1].y);

        // Convert to percentages (0–1)
        float leftPct = left / imageWidth;
        float rightPct = right / imageWidth;
        float topPct = top / imageHeight;
        float bottomPct = bottom / imageHeight;

        //Debug.Log($"Left % ={leftPct}, Top % ={topPct}, Down % ={bottomPct}, Right % ={rightPct},");

        // Return as Vector4(left, top, right, bottom)
        return new Vector4(leftPct, topPct, rightPct, bottomPct);
    }

}