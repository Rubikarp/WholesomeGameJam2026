using System.Threading;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WorldGame.SceneManagement
{
    public class LoadingScreenController : MonoBehaviour, ITransitionEffect
    {
        [Header("Refs")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Slider _progressBar;
        [SerializeField] private TMP_Text _progressLabel;
 
        [Header("Config")]
        [SerializeField, Range(0.1f, 1f)] private float _fadeDuration = 0.25f;
        
        private void Awake()
        {
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }

        public async Awaitable PlayInAsync(CancellationToken ct)
        {
            gameObject.SetActive(true);
            ReactToProgress(0f);
            _canvasGroup.blocksRaycasts = true;
            await Tween.Alpha(_canvasGroup, 1f, _fadeDuration).SetCancellationToken(ct);
        }
        
        public void ReactToProgress(float progress)
        {
            _progressBar.value = progress;
            _progressLabel.text = $"{Mathf.RoundToInt(progress * 100f)} %";
        }

        public async Awaitable PlayOutAsync(CancellationToken ct)
        {
            ReactToProgress(1f);
            await Tween.Alpha(_canvasGroup, 0f, _fadeDuration).SetCancellationToken(ct);
            _canvasGroup.blocksRaycasts = false;
            gameObject.SetActive(false);
        }
    }
}