using System;
using Alchemy.Inspector;
using UnityEngine;
using UnityEngine.Events;

public enum ECaptureState
{
	Wait,
	Capturing,
	Ended
}

public class HorseCaptureHandler : MonoBehaviour
{
	[Header("Value")]
	public ECaptureState state;
	
	[Header("References")]
	[SerializeField] private CaptureBar capture;
	[SerializeField] private HorseStress stress;
	[SerializeField] private HorseApproach approach;
	
	[Header("Event")]
	public UnityEvent<bool> onCaptureResult;

	private void Awake()
	{
		stress.onHorseFled.AddListener(OnFled);
		approach.onHorseReached.AddListener(OnHorseReached);
	}

	[Button]
	public void LaunchCapture(HorseData horse)
	{
		stress.Initialize(horse);
		capture.Initialize(horse);
		approach.Initialize(horse);	
		
		state = ECaptureState.Capturing;
	}
	
	public void Update()
	{
		switch(state)
		{
			case ECaptureState.Wait:
				return;
			case ECaptureState.Capturing:
				capture.CaptureProgress(Time.deltaTime);
				stress.StressProgress(Time.deltaTime, capture.IsPlayerInZone);
				approach.ApproachProgress(Time.deltaTime, capture.IsPlayerInZone);
				return;
			case ECaptureState.Ended:
				return;
		}
		
	}
	
	public void OnFled()
	{
		EndCapture(false);
	}
	public void OnHorseReached()
	{
		EndCapture(true);
	}
	
	public void EndCapture(bool success)
	{
		state = ECaptureState.Ended;
		onCaptureResult?.Invoke(success);
	}
}