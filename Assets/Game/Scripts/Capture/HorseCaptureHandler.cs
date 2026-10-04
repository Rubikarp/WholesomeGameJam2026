using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine;
using Alchemy.Inspector;

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
	public HorseData currentHorse = null;
	public Image background = null;
	
	[Header("References")]
	[SerializeField] private CaptureBar capture;
	[SerializeField] private HorseStress stress;
	[SerializeField] private HorseApproach approach;
	
	[Header("Event")]
	public UnityEvent<HorseData> onCaptureHorse;
	public UnityEvent onHorseFled;
	
	private void Awake()
	{
		background.sprite = GameManager.Instance.CurrentZone;
		currentHorse = GameManager.Instance.CurrentHorse;
		stress.onHorseFled.AddListener(OnFled);
		approach.onHorseReached.AddListener(OnHorseReached);
	}

	private void Start()
	{
		LaunchCapture(currentHorse);
	}

	[Button]
	public void LaunchCapture(HorseData horse)
	{
		currentHorse = horse;
		
		stress.Initialize(currentHorse);
		capture.Initialize(currentHorse);
		approach.Initialize(currentHorse);	
		
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
		state = ECaptureState.Ended;
		onHorseFled?.Invoke();
	}
	public void OnHorseReached()
	{
		state = ECaptureState.Ended;
		GameManager.Instance.AllLootedHorses.Add(currentHorse);
		onCaptureHorse?.Invoke(currentHorse);
	}
}