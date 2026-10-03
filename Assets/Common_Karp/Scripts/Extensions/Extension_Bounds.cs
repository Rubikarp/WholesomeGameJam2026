using UnityEngine;

public static class Extension_Bounds
{
	public static Vector2 Corner_LeftUp(this Bounds bounds) => new Vector2(-bounds.extents.x, bounds.extents.y);
	public static Vector2 Corner_RightUp(this Bounds bounds) => new Vector2(bounds.extents.x, bounds.extents.y);
	public static Vector2 Corner_LeftDown(this Bounds bounds) => new Vector2(-bounds.extents.x, -bounds.extents.y);
	public static Vector2 Corner_RightDown(this Bounds bounds) => new Vector2(bounds.extents.x, -bounds.extents.y);

	public static bool IsOutUpBounds(this Bounds bounds, Vector3 pos) => (pos - bounds.center).y > bounds.extents.y;
	public static bool IsOutLeftBounds(this Bounds bounds, Vector3 pos) => (pos - bounds.center).x > bounds.extents.x;
	public static bool IsOutDownBounds(this Bounds bounds, Vector3 pos) => (pos - bounds.center).y < -bounds.extents.y;
	public static bool IsOutRightBounds(this Bounds bounds, Vector3 pos) => (pos - bounds.center).x < -bounds.extents.x;

	public static bool IsOutVerticalBounds(this Bounds bounds, Vector3 pos) => Mathf.Abs((pos - bounds.center).y) > Mathf.Abs(bounds.extents.y);
	public static bool IsOutHorizontalBounds(this Bounds bounds, Vector3 pos) => Mathf.Abs((pos - bounds.center).x) > Mathf.Abs(bounds.extents.x);

	public static Vector3 RandomPointInBounds(this Bounds boundss)
	{
		return boundss.center + new Vector3(
			Random.Range(boundss.min.x, boundss.max.x),
			Random.Range(boundss.min.y, boundss.max.y),
			Random.Range(boundss.min.z, boundss.max.z)
		);
	}
}
