using UnityEngine;
using UnityEngine.UI;

public class Drawer_SimpleTimer_Slider : MonoBehaviour
{
	[SerializeField] SimpleTimer timer;

	[SerializeField] Slider slider;

	public void Update()
	{
		slider.maxValue = timer.duration;
		slider.value = timer.TimeLeft;
	}

}
