using System.Threading;
using Alchemy.Inspector;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

public class ImageDrawer : MonoBehaviour, IVisualDrawer<Sprite>
{
    [Header("References")]
    [SerializeField] private Image _image;
 
    [Header("Settings")]
    [HorizontalGroup("Show Settings")]
    [SerializeField, Range(0.1f, 2f)] private float _showDuration = 0.3f;
    [SerializeField] private Ease _showEase = Ease.InOutSine;
    [HorizontalGroup("Hide Settings")]
    [SerializeField, Range(0.1f, 2f)] private float _hideDuration = 0.3f;
    [SerializeField] private Ease _hideEase = Ease.InOutSine;
    
    private Tween _activeTween;
    
    private void OnDestroy()
    {
        _activeTween.Stop();
    }

    public void ShowImmediate(Sprite sprite)
    {
        _activeTween.Stop();
        _image.SetAlpha(1f);
        
        _image.sprite = sprite;
    }
    public async Awaitable ShowAsync(Sprite sprite, CancellationToken ct = default)
    {
        _activeTween.Stop();
 
        _image.sprite = sprite;
        _activeTween = Tween.Color(_image, Color.white, _showDuration, _showEase).SetCancellationToken(ct);

        await _activeTween;
    }
 
    public void HideImmediate()
    {
        _activeTween.Stop();
        _image.SetAlpha(0f);
    }
    public async Awaitable HideAsync(CancellationToken ct = default)
    {
        _activeTween.Stop();
        _activeTween = Tween.Alpha(_image, 0f, _hideDuration, _hideEase).SetCancellationToken(ct);
        await _activeTween;
    }
}