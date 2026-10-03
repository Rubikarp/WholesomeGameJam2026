using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.Events;

public class SimpleTimer : MonoBehaviour
{
	[Header("Parameter")]
	public bool pause = false;
	[Space(5)]
	public float duration = 2f;
	public float TimeLeft { get => currentTime; }
	[SerializeField] float currentTime = 2f;

	[Header("Event")]
	public UnityEvent<int> onSecondUpdate;
	public UnityEvent onEnd;

	[Header("Internal")]
	[SerializeField, ReadOnly] int lastDigit = 0;
	
	[Button]
	public void SetDuration() => SetDuration(duration);
	public void SetDuration(float dur)
	{
		duration = dur;
		currentTime = duration;

		lastDigit = Mathf.FloorToInt(currentTime);
		onSecondUpdate?.Invoke(lastDigit);
	}

	void OnEnable()
	{
		currentTime = duration;
	}

	private void OnDisable()
	{
		currentTime = 0;
	}

	void Update()
	{
		if (pause) return;
		if (currentTime <= 0f) return;

		if (currentTime - Time.deltaTime > 0)
		{
			currentTime -= Time.deltaTime;
		}
		else
		{
			currentTime = 0;
			onEnd?.Invoke();
		}

		if (lastDigit != Mathf.FloorToInt(currentTime))
		{
			lastDigit = Mathf.FloorToInt(currentTime);
			onSecondUpdate?.Invoke(lastDigit);
		}
	}
}
