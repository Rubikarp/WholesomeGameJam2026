using System;
using UnityEngine;

[Serializable]
public class TemporalInputBuffer<T>
{
	public T Value => bufferedValue;
	[SerializeField] private T bufferedValue;
	[Tooltip( "Time in seconds to keep the last input")]
	[field: SerializeField, Min(0f)] public float BufferWindow { get; private set; } = 0.125f;
	[field: SerializeField] public bool HasBufferedValue { get; private set; } = false;
	
	private float expireTiming = float.NegativeInfinity;
	
	public virtual void BufferInput(T value)
	{
		bufferedValue = value;
		expireTiming = Time.time + BufferWindow;
		HasBufferedValue = true;
	}
	public void ClearBuffer()
	{
		bufferedValue = default;
		expireTiming = float.NegativeInfinity;
		HasBufferedValue = false;
	}
	
	public bool IsBufferValid()
	{
		if(Time.time > expireTiming)
		{
			ClearBuffer();
			return false;
		}
		return HasBufferedValue;
	}
	public bool TryConsume(out T value)
	{
		value = default;
		if(!IsBufferValid()) return false;
		
		value = bufferedValue;
		ClearBuffer();
		return true;
	}
}
