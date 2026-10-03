using UnityEngine;
using UnityEngine.UI;

public class Drawer_SimpleTimer_ImageFill : MonoBehaviour
{
	[SerializeField] SimpleTimer timer;
	[SerializeField] Image image;

	public void Update()
	{
		image.fillAmount = timer.TimeLeft / timer.duration;
	}

}
