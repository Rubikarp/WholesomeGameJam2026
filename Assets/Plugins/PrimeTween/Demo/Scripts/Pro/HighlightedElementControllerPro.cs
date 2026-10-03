#if PRIME_TWEEN_INSTALLED
using JetBrains.Annotations;
using PrimeTween;
using UnityEngine;

namespace PrimeTweenDemo {
    public class HighlightedElementControllerPro : MonoBehaviour {
        [SerializeField] Camera mainCamera;
        #if PRIME_TWEEN_PRO
        [SerializeField] TweenAnimationComponent animateAllPartsAnimation;
        HighlightableElementPro current;

        void Awake() {
            #if UNITY_2019_1_OR_NEWER && !PHYSICS_MODULE_INSTALLED
            Debug.LogError("Please install the package needed for Physics.Raycast(): 'Package Manager/Packages/Built-in/Physics' (com.unity.modules.physics).");
            #endif
            #if !UNITY_2020_3_OR_NEWER
            Debug.LogError("Demo (Pro) requires Unity 2020.3 (or newer) for custom animations.");
            #endif
            #if !UNITY_2021_1_OR_NEWER
            Debug.LogError("Demo (Pro) requires Unity 2021.1 (or newer) for MaterialPropertyBlock animations.");
            #endif
        }

        void Update() {
            if (Application.isMobilePlatform && InputController.touchSupported && !InputController.Get()) {
                SetCurrentHighlighted(null);
                return;
            }
            var screenPosition = InputController.screenPosition;
            if (!new Rect(0f, 0f, Screen.width, Screen.height).Contains(screenPosition)) {
                return;
            }
            var ray = mainCamera.ScreenPointToRay(screenPosition);
            var highlightableElement = RaycastHighlightableElement(ray);
            SetCurrentHighlighted(highlightableElement);

            if (current != null && InputController.GetDown() && !animateAllPartsAnimation.animation.isAlive) {
                current.clickAnimation.animation.Trigger();
            }
        }

        [CanBeNull]
        static HighlightableElementPro RaycastHighlightableElement(Ray ray) {
            #if !UNITY_2019_1_OR_NEWER || PHYSICS_MODULE_INSTALLED
                // If you're seeing a compilation error on the next line, please install the package needed for Physics.Raycast(): 'Package Manager/Packages/Built-in/Physics' (com.unity.modules.physics).
                return Physics.Raycast(ray, out var hit) ? hit.collider.GetComponentInParent<HighlightableElementPro>() : null;
            #else
                return null;
            #endif
        }

        void SetCurrentHighlighted([CanBeNull] HighlightableElementPro newHighlighted) {
            if (newHighlighted != current) {
                if (current != null) {
                    current.highlightAnimation.state = false;
                }
                current = newHighlighted;
                if (newHighlighted != null) {
                    newHighlighted.highlightAnimation.state = true;
                }
            }
        }
        #endif

        public void SetFogColor(Color color) => RenderSettings.fogColor = color;
    }
}
#endif
