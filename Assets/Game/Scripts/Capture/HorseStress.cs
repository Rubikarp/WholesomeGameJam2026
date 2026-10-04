using Alchemy.Inspector;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

public class HorseStress : MonoBehaviour
{
	[Header("Références")]
	[SerializeField] private Image stressFill;
 
	[Header("Stress")]
	[SerializeField, Min(0.1f)] private float stressThreshold = 3f;
	[SerializeField, Min(0f)] private float recoveryRate = 0.5f;
	[SerializeField, ReadOnly] private float currentStress;
	[SerializeField, ReadOnly] private float stressSensibility;
 
	[Header("Event")]
	public UnityEvent onHorseFled;
	
	public void Initialize(HorseData horse)
	{
		stressSensibility = horse.stressSensibility;
 
		if (stressSensibility <= 0f)
		{
			Debug.LogWarning($"{horse.name} : stressSensibility <= 0, valeur 1 utilisée.", horse);
			stressSensibility = 1f;
		}
 
		currentStress = 0f;
		stressFill.fillAmount = Mathf.Clamp01(currentStress / stressThreshold);
		enabled = true;
	}
	
	public void StressProgress(float deltaTime, bool isPlayerInZone)
	{
		if (isPlayerInZone)
		{
			currentStress = Mathf.Max(0f, currentStress - recoveryRate * deltaTime);
		}
		else
		{
			currentStress += stressSensibility * deltaTime;
		}
		stressFill.fillAmount = Mathf.Clamp01(currentStress / stressThreshold);
		if (currentStress >= stressThreshold)
		{
			onHorseFled.Invoke();
		}
	}
}