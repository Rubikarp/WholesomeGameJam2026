using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine;
using Alchemy.Inspector;

public class CaptureBar : MonoBehaviour
{
	[Header("UI")] 
	[SerializeField] private Scrollbar zoneBar;
	[SerializeField] private Scrollbar playerBar;

	[Header("Pattern")] 
	[SerializeField] private float patternSpeed = 1f;
	[SerializeField] private float patternDuration;
	
	[Header("Zone")] 
	[SerializeField, Range(0.01f, 1f)] private float minZoneSize = 0.1f;
	[SerializeField, Range(0.01f, .5f)] private float maxZoneSize = 0.5f;

	[Header("Marqueur joueur")] 
	[SerializeField, Range(0.01f, 0.3f)] private float markerSize = 0.05f;

	[SerializeField, Min(0f)] private float liftAcceleration = 4f;
	[SerializeField, Min(0f)] private float gravity = 3f;

	[field : SerializeField] public bool IsPlayerInZone { get; private set; }

	[Header("Debug")] 
	[SerializeField] private AnimationCurve approachPattern;
	[SerializeField] private AnimationCurve tolerancePattern;
	[SerializeField] private float patternTime;
	[SerializeField] private float zoneCenter;
	[SerializeField] private float zoneSize;
	[SerializeField] private float playerPosition;

	[Button]
	public void Initialize(HorseData horse)
	{
		approachPattern = horse.approchePattern;
		tolerancePattern = horse.tolerancePattern;

		zoneBar.interactable = false;
		playerBar.interactable = false;
		playerBar.size = markerSize;

		patternTime = 0f;
		ZoneMouvement(0f);
		playerPosition = zoneCenter;
		IsPlayerInZone = true;
		Refresh();

		enabled = true;
	}

	public void CaptureProgress(float deltaTime)
	{
		ZoneMouvement(deltaTime);
		MovePlayer(deltaTime);

		IsPlayerInZone = Mathf.Abs(playerPosition - zoneCenter) <= zoneSize * 0.5f;
		Refresh();
	}

	private void ZoneMouvement(float deltaTime)
	{
		patternTime += deltaTime * patternSpeed;
		patternTime %= patternDuration;

		//Zone Valide
		float tolerance = Mathf.Clamp01(tolerancePattern.Evaluate(patternTime));
		zoneSize = Mathf.Lerp(minZoneSize, maxZoneSize, tolerance);

		//Position de la Zone
		float approach = Mathf.Clamp01(approachPattern.Evaluate(patternTime));
		float halfZone = zoneSize * 0.5f;
		zoneCenter = Mathf.Lerp(halfZone, 1f - halfZone, approach);
	}

	private void MovePlayer(float deltaTime)
	{
		float acceleration = -gravity;
		if(Keyboard.current.spaceKey.IsPressed() || Mouse.current.leftButton.IsPressed())
		{
			acceleration = liftAcceleration;
		}
		playerPosition += acceleration * deltaTime;
		playerPosition = Mathf.Clamp01(playerPosition);
	}
	
	private void Refresh()
	{
		zoneBar.size = zoneSize;
		zoneBar.value = ToScrollbarValue(zoneCenter, zoneSize);
		playerBar.value = ToScrollbarValue(playerPosition, markerSize);
	}
	
	// Place le bord du handle sur la portion libre (1 - size) de la piste, pas son centre.
	private static float ToScrollbarValue(float center, float zoneSize)
	{
		float espaceLibre = 1f - zoneSize;

		if (espaceLibre <= 0f)
		{
			return 0f;
		}

		return Mathf.Clamp01((center - zoneSize * 0.5f) / espaceLibre);
	}

	private void OnValidate()
	{
		patternTime = 0f;
		
		playerBar.size = markerSize;
		playerPosition = zoneCenter = .5f;
		
		zoneSize = Mathf.Lerp(minZoneSize, maxZoneSize, 0.5f);
	}
}