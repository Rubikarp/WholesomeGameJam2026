using TMPro;
using UnityEngine;

public class Drawer_SimpleTimer_Chrono : MonoBehaviour
{
	[SerializeField] private SimpleTimer timer;
	[Space(10)]
	[SerializeField] private TextMeshProUGUI textSlotMinutes;
	[SerializeField] private TextMeshProUGUI textSlotSeconds;

	public void Update()
	{
		if (textSlotSeconds != null) textSlotSeconds.text = string.Format("{0:00}", Mathf.FloorToInt(timer.TimeLeft % 60));
		if (textSlotMinutes != null) textSlotMinutes.text = string.Format("{0:00}", Mathf.FloorToInt(timer.TimeLeft / 60));
	}
}
