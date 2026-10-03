#if PRIME_TWEEN_INSTALLED
using PrimeTween;
using UnityEngine;

namespace PrimeTweenDemo {
    public class HighlightableElementPro : MonoBehaviour {
        #if PRIME_TWEEN_PRO
        [SerializeField] public TweenAnimationComponent clickAnimation;
        [SerializeField] public TweenAnimation highlightAnimation = new TweenAnimation();
        #endif
    }
}
#endif
