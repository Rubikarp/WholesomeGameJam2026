using System.Collections.Generic;
using UnityEngine.EventSystems;
using UnityEngine;


public static class Extension_Vector
{
    public static Vector2 With(this Vector2 vector2, float? x = null, float? y = null)
        => new Vector2(x ?? vector2.x, y ?? vector2.y);
    public static Vector3 With(this Vector3 vector, float? x = null, float? y = null, float? z = null)
        => new Vector3(x ?? vector.x, y ?? vector.y, z ?? vector.z);

    public static Vector2 RandomPointAround(this Vector2 origin, float minRadius, float maxRadius)
    {
        float angle = Random.value * Mathf.PI * 2f;
        Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        // Squaring and then square-rooting radii to ensure uniform distribution within the Donut
        float minRadiusSquared = minRadius * minRadius;
        float maxRadiusSquared = maxRadius * maxRadius;
        float distance = Mathf.Sqrt(Random.value * (maxRadiusSquared - minRadiusSquared) + minRadiusSquared);

        return origin + (direction * distance);
    }

    public static Vector3 FlatenToXY(this Vector3 vector) => new Vector3(vector.x, vector.y, 0);
    public static Vector3 FlatenToXZ(this Vector3 vector) => new Vector3(vector.x, 0, vector.z);

    // Swizzle methods
    public static Vector2 XY(this Vector3 vector) => new Vector2(vector.x, vector.y);
    public static Vector2 XZ(this Vector3 vector) => new Vector2(vector.x, vector.z);
    public static Vector2 YZ(this Vector3 vector) => new Vector2(vector.y, vector.z);
    public static Vector2 YX(this Vector3 vector) => new Vector2(vector.y, vector.x);
    public static Vector2 ZX(this Vector3 vector) => new Vector2(vector.z, vector.x);
    public static Vector2 ZY(this Vector3 vector) => new Vector2(vector.z, vector.y);

    private static PointerEventData _eventDataCurrentPos;
    private static List<RaycastResult> _results;
    public static bool IsOverUI(this Vector3 cursorPosition)
    {
        _eventDataCurrentPos = new PointerEventData(EventSystem.current) { position = cursorPosition };
        _results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(_eventDataCurrentPos, _results);
        return _results.Count > 0;
    }

    public static Vector2 GetScreenPosOfGameObject(this Vector3 worldPosition) => Camera.main.WorldToScreenPoint(worldPosition);
}
