using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// Creates an EventSystem with the appropriate input module if none exists in the scene.
    /// Uses InputSystemUIInputModule when the new Input System is active, otherwise StandaloneInputModule.
    /// </summary>
    [AddComponentMenu(LightSideMenu.AddComponent.EventSystemBootstrap)]
    public class EventSystemBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (EventSystem.current != null || ObjectUtils.FindAny<EventSystem>() != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();

#if ENABLE_INPUT_SYSTEM
            var newInputType = System.Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (newInputType != null)
                go.AddComponent(newInputType);
            else
                go.AddComponent<StandaloneInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }
    }
}