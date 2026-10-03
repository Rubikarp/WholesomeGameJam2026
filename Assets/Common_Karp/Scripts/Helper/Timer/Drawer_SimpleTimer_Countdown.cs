using TMPro;
using UnityEngine;

public class Drawer_SimpleTimer_Countdown : MonoBehaviour
{
	[SerializeField] SimpleTimer timer;

	[SerializeField] string beforeText;
	[SerializeField] string afterText;
	[SerializeField] private string format = "0:0";
	[SerializeField] TextMeshProUGUI chronoText;

	public void Update()
	{
		if (chronoText != null) chronoText.text = beforeText + string.Format($"{{{format}}}", timer.TimeLeft) + afterText;
	}

}
