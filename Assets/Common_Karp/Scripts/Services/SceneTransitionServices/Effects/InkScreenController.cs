using System.Threading;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;
using WorldGame.Tool;
using Alchemy.Inspector;

namespace WorldGame.SceneManagement
{
	public class InkScreenController : MonoBehaviour, ITransitionEffect
	{
		[Header("Refs")]
		[SerializeField] private CanvasGroup _canvasGroup;
		[SerializeField] private RawImage screenshot;
		[SerializeField] private Image fade;

		[SerializeField] private Material mat;
 
		[Header("Config")]
		[SerializeField] private float _fadeDuration = 1.25f;
        
		private void Awake()
		{
			_canvasGroup.alpha = 0f;
			_canvasGroup.blocksRaycasts = false;
			gameObject.SetActive(false);
		}

		public async Awaitable PlayInAsync(CancellationToken ct)
		{
			gameObject.SetActive(true);
			_canvasGroup.alpha = 0f;
			_canvasGroup.blocksRaycasts = true;
			
			screenshot.texture = await ScreenCaptureService.CaptureScreenAsync(ct);
			_canvasGroup.alpha = 1f;
			mat.SetFloat("_Alpha", 0f);
			
			fade.color = fade.color.WithAlpha(0f);
			await Tween.Alpha(fade, .5f, .2f).SetCancellationToken(ct);
		}
        
		public void ReactToProgress(float progress) {}

		public async Awaitable PlayOutAsync(CancellationToken ct)
		{
			await Tween.MaterialProperty(mat, Shader.PropertyToID("_Alpha"), 1f, _fadeDuration).SetCancellationToken(ct);
			_canvasGroup.blocksRaycasts = false;
			gameObject.SetActive(false);
		}
		
		[Button]
		public async void Debug_TakeScreenshot()
		{
			var ct = new CancellationTokenSource();
			screenshot.texture = await ScreenCaptureService.CaptureScreenAsync(ct.Token);
		}
	}
}