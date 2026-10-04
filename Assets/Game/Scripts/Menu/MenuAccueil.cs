using UnityEngine;
using UnityEngine.SceneManagement;

public class MenuAccueil : MonoBehaviour
{
	public string sceneName = "Map";
	
	public void Play()
	{
		SceneManager.LoadScene(sceneName);
	}

	public void Quit()
	{
		Application.Quit();
	}
}