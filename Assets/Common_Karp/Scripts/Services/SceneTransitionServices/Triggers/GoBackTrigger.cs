using UnityEngine.SceneManagement;
using UnityEngine;
using Alchemy.Inspector;
using System.Threading;
using VContainer;

namespace WorldGame.SceneManagement
{
    public class GoBackTrigger : MonoBehaviour
    {
        [Inject] private ISceneTransitionService SceneTransitionService { get; set;}

        [Header("Info")]
        private CancellationTokenSource _cancellationTokenSource = new ();
        private STransitionRequest backUpRequest;
        
        private void Awake()
        {
            string scenePath = SceneUtility.GetScenePathByBuildIndex(0); // "Assets/.../Acceuil/Scenes/Home.unity"
            string sceneName = ExtractSceneName(scenePath);

            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogError("Scène de secours introuvable (build index 0 absent de la Build Settings).");
            }            backUpRequest = new STransitionRequest(sceneName, ETransitionEffect.InkFade, null, false);
        }

        [Button]
        public void GoBack()
        {
            if (!SceneTransitionService.CanGoBack)
            {
                Debug.LogWarning("Cannot go back, no previous scene available");
                _ = SceneTransitionService.LaunchTransitionToAsync(backUpRequest, _cancellationTokenSource.Token);
                return;
            }
            _cancellationTokenSource = new CancellationTokenSource();
            _ = SceneTransitionService.GoBackAsync(_cancellationTokenSource.Token);
        }
        [Button]
        public void CancelGoBack()
        {
            _cancellationTokenSource?.Cancel();
        }
        
        private static string ExtractSceneName(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return string.Empty;
            }
            int start = scenePath.LastIndexOf('/') + 1;
            int dot = scenePath.LastIndexOf('.');
            int end = dot > start ? dot : scenePath.Length;
            return scenePath.Substring(start, end - start);
        }
    }
}