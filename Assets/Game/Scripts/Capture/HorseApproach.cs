using Alchemy.Inspector;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;

public class HorseApproach : MonoBehaviour
{
	[Header("Références")]
	[SerializeField] private Image horseImage;
	[SerializeField] private Image progressFill;
 
	[Header("Approche")]
	[SerializeField, Min(0.1f)] private float approachSpeed = 1f;
	[SerializeField] private float minHorseScale = 0.3f;
	[SerializeField] private float maxHorseScale = 4f;
 
	[Header("Event")]
	public UnityEvent onHorseReached;
 
	[SerializeField, ReadOnly] private float distanceTotal;
	[SerializeField, ReadOnly] private float traveledDistance;
 
	public void Initialize(HorseData horse)
	{
		distanceTotal = horse.distanceInitial;
 
		if (distanceTotal <= 0f)
		{
			Debug.LogWarning($"{horse.name} : distanceInitial <= 0, valeur 10 utilisée.", horse);
			distanceTotal = 10f;
		}
 
		horseImage.sprite = horse.Visual;
		traveledDistance = 0f;
		Refresh();
	}
	public void ApproachProgress(float deltaTime, bool isPlayerInZone)
	{
		if (!isPlayerInZone) return;
		
		traveledDistance += approachSpeed * deltaTime;
		Refresh();
 
		if (traveledDistance >= distanceTotal)
		{
			onHorseReached.Invoke();
		}
	}
	
	private void Refresh()
	{
		float progress = Mathf.Clamp01(traveledDistance / distanceTotal);
		progressFill.fillAmount = progress;
		horseImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(minHorseScale, maxHorseScale, progress);
	}
}