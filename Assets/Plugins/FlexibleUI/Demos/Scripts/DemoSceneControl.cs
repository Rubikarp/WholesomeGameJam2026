using UnityEngine;
using UnityEngine.SceneManagement;

namespace JeffGrawAssets.FlexibleUI
{
public class DemoSceneControl : MonoBehaviour
{
    public void LoadNextScene(bool prev = false)
    {
        var numScenes = SceneManager.sceneCountInBuildSettings;
        var currentSceneIdx = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene((int)Mathf.Repeat(prev ? ++currentSceneIdx : --currentSceneIdx, numScenes));
    }
}
}
