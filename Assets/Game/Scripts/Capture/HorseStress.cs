using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

public class HorseCaptureHandler : MonoBehaviour
{
	public enum ECaptureState
	{
		Wait,
		Capturing,
		Ended
	}
	[Header("Value")]
	public ECaptureState state;
	
	[Header("References")]
	public Image horseImage;
	public Scrollbar approcheSlider;
	public Scrollbar playerSlider;
	public Scrollbar stressSlider;
	public Scrollbar progressSlider;

	[Header("Progression")] 
	public float patternTime;
	public float patternSpeed;
	public float patternDuration;
	public AnimationCurve approchePattern;
	public AnimationCurve tolerancePattern;
	
	[Header("Stress")]
	public float stressValue = 0f;
	public float stressThreshold = 100f;
	
	[Header("Rapprochement")]
	public float traveledDistance = 0f;
	public float horseDistance = 30f;
	public float minSize = .3f;
	public float maxSize = 4f;
	
	[Header("Event")]
	public UnityEvent<bool> onCaptureResult;

	public void LaunchCapture()
	{
		horseImage.transform.localScale = Vector3.one * minSize;
	}
	
	public void Update()
	{
		switch(state)
		{
			case ECaptureState.Wait:
				return;
			case ECaptureState.Capturing:
				CaptureProgress(Time.deltaTime);
				return;
			case ECaptureState.Ended:
				return;
		}
		
	}
	
	public void CaptureProgress(float deltaTime)
	{
		patternTime += deltaTime * patternSpeed;
		patternTime %= patternDuration;
		
		var approcheSpeed = approchePattern.Evaluate(patternTime);
		var tolerance = tolerancePattern.Evaluate(patternTime);
		approcheSlider.value = approcheSpeed;
		approcheSlider.size = .5f * tolerance;
		if(Mathf.Abs(playerSlider.value - approcheSlider.value) > approcheSlider.size * .5f)
		{
			stressValue += deltaTime;
			if(stressValue > stressThreshold) EndCapture(false);
		}
		else
		{
			stressValue -= deltaTime;
			if(stressValue < 0) stressValue = 0;
			
			traveledDistance += deltaTime;
		}
		
		if(traveledDistance > horseDistance) EndCapture(true);
		
		//Refresh size
		float progress = traveledDistance / horseDistance;
		horseImage.transform.localScale = Vector3.one * Mathf.Lerp(minSize, maxSize, progress);
	}
	
	public void EndCapture(bool success)
	{
		state = ECaptureState.Ended;
		onCaptureResult?.Invoke(success);
	}
}