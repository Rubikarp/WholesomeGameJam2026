using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Drawer_SimpleTimer_ColorOverTime : MonoBehaviour
{
	[SerializeField] SimpleTimer timer;
	[Space(10)]
	public Image[] images;
	public TextMeshProUGUI[] tmps;

	public ColorTime[] paliers;

	private void Update()
	{
		var order = paliers.OrderByDescending(x => x.time).ToArray();
		for (int i = 0; i < paliers.Length; i++)
		{
			if (timer.TimeLeft < order[i].time) continue;

			foreach (var image in images)
				image.color = order[i].color;

			foreach (var tmp in tmps)
                tmp.color = order[i].color;


            return;
		}
	}
}

[System.Serializable]
public class ColorTime
{
	public float time;
	public Color color;
}