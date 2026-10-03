using System.Threading;
using UnityEngine.Events;
using UnityEngine;

namespace WorldGame.SceneManagement
{
    public interface ISceneTransitionService
    {
        public bool CanGoBack { get; }
 
        public Awaitable LaunchTransitionToAsync(STransitionRequest request, CancellationToken ct);
        public Awaitable GoBackAsync(CancellationToken ct);
        
        public UnityAction<STransitionRequest> OnTransitionStarted { get; set; }
        public UnityAction<ScriptableObject> OnLoadTransitonData{ get; set; }
        public UnityAction OnTransitionCompleted { get; set; }
    }
}