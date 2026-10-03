using System.Threading;
using PrimeTween;
using Alchemy.Inspector;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public class ImageSwapDrawer : MonoBehaviour, IVisualDrawer<Sprite>
{
	[Header("References")] 
	[SerializeField] private Image _imageA;

	[SerializeField] private Image _imageB;

	[Header("Settings")] [HorizontalGroup("Show Settings")] [SerializeField, Range(0.1f, 2f)]
	private float _showDuration = 0.3f;

	[SerializeField] private Ease _showEase = Ease.InOutSine;

	[HorizontalGroup("Hide Settings")] [SerializeField, Range(0.1f, 2f)]
	private float _hideDuration = 0.3f;

	[SerializeField] private Ease _hideEase = Ease.InOutSine;

	[Header("Internal")] private Image _current;
	private Image _next;
	private Sequence _activeTween;

	private void Awake()
	{
		_current = _imageA;
		_next = _imageB;

		_current.SetAlpha(1f);
		_next.SetAlpha(0f);

		_next.transform.SetAsFirstSibling();
		_current.transform.SetAsLastSibling();
	}

	private void OnDestroy()
	{
		_activeTween.Stop();
	}

	public void ShowImmediate(Sprite sprite)
	{
		_activeTween.Complete();

		_current.sprite = sprite;
		_current.SetAlpha(1f);
		_next.SetAlpha(0f);
	}

	public async Awaitable ShowAsync(Sprite sprite, CancellationToken ct = default)
	{
		_activeTween.Complete();

		_next.SetAlpha(0f);
		_next.sprite = sprite;
		_next.transform.SetAsLastSibling();

		_activeTween = Sequence.Create()
			.Chain(Tween.Alpha(_next, 1f, _showDuration, _showEase))
			.Chain(Tween.Alpha(_current, 0f, _hideDuration, _hideEase))
			.OnComplete(target: this, target => (_current, _next) = (_next, _current)) // no allocation
			.SetCancellationToken(ct);

		await _activeTween;
	}

	public void HideImmediate()
	{
		_activeTween.Stop();
		_current.SetAlpha(0f);
		_next.SetAlpha(0f);
	}

	public async Awaitable HideAsync(CancellationToken ct = default)
	{
		_activeTween.Stop();
		_activeTween = Sequence.Create()
			.Chain(Tween.Alpha(_current, 0f, _hideDuration, _hideEase))
			.Chain(Tween.Alpha(_next, 0f, _hideDuration, _hideEase))
			.SetCancellationToken(ct);
		await _activeTween;
	}
}