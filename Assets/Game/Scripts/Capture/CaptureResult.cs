using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CaptureResult : MonoBehaviour
{
	public Image TagadaImage;
	public GameObject ecranVictory;
	public GameObject ecranLooser;
	
	public Button onContinue;

	private void Awake()
	{
		onContinue.onClick.AddListener(OnContinue);
	}

	public void ShowVictory(HorseData horse)
	{
		ecranVictory.SetActive(true);
		ecranLooser.SetActive(false);
		
		TagadaImage.sprite = horse.Visual;
	}
	
	public void ShowLooser()
	{
		ecranVictory.SetActive(false);
		ecranLooser.SetActive(true);
	}
	
	private void OnContinue()
	{
		SceneManager.LoadScene("Map");
	}
}
