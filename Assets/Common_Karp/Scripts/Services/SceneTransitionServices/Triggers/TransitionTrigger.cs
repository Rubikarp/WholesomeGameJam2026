using System.Threading;
using Alchemy.Inspector;
using UnityEngine;
using VContainer;
using Scene = NaughtyAttributes.SceneAttribute;

namespace WorldGame.SceneManagement
{
    public class TransitionTrigger : MonoBehaviour
    {
        [Inject] private ISceneTransitionService SceneTransitionService { get; set;}

        [Header("Info")]
        private CancellationTokenSource _cancellationTokenSource = new ();

        [Header("Config")]
        [SerializeField, Scene] private string targetSceneName;
        [SerializeField] private ETransitionEffect effect = ETransitionEffect.InkFade;
        [SerializeField] private ScriptableObject payload;
        [SerializeField] private bool pushToHistory = true;
        
        [Button]
        public void TriggerTransition()
        {
            var request = new STransitionRequest(
                targetSceneName, 
                effect,
                payload,
                pushToHistory);
            
            _ = SceneTransitionService.LaunchTransitionToAsync(request, _cancellationTokenSource.Token);
        }
        [Button]
        public void CancelTransition()
        {
            _cancellationTokenSource?.Cancel();
        }
    }
}