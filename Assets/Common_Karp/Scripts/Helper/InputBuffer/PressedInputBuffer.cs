using UnityEngine;

public class PressedInputBuffer<T> : TemporalInputBuffer<bool?> { }

public class DirectionInputBuffer<T> : TemporalInputBuffer<Vector2>
{
	public override void BufferInput(Vector2 value)
	{
		if( value.sqrMagnitude > 0f) value.Normalize();
		base.BufferInput(value);
	}
}

