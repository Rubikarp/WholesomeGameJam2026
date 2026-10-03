using Alchemy.Inspector;
using System.Collections;
using UnityEngine.UI;
using UnityEngine;
using PrimeTween;

public class LifePoint : MonoBehaviour
{
    [Header("Setting")]
    public bool IsOn
    {
        get => _isOn;
        private set
        {
            bool change = _isOn != value;

            _isOn = value;

            if (change)
            {
                if (_isOn)
                {
                    var rect = imageLifePoint.transform as RectTransform;
                    rect.localScale = Vector3.one;
                    rect.gameObject.SetActive(true);
                }
                else
                {
                    LosePoint();
                }
            }
        }
    }
    [SerializeField] private bool _isOn = true;

    [Header("Components")]
    [SerializeField, Required] private Image imageLifePoint;
    [HideInInspector] public Image imageBackground;

    [Button] public void Toogle() => IsOn = !_isOn;
    public void SetState(bool state) => IsOn = state;
    private Sequence feedback;

    private void OnValidate()
    {
        IsOn = _isOn;
    }

    private async void LosePoint()
    {
        var rect = imageLifePoint.transform as RectTransform;
        if (Application.isPlaying)
        {
            feedback.Complete();
            feedback = ShakeFeedback(rect);
            await feedback;
        }
        else
        {
            rect.localScale = Vector3.zero;
            rect.gameObject.SetActive(false);
        }
    }
    public Sequence ShakeFeedback(RectTransform rect)
    {
        rect.localScale = Vector3.one;
        rect.gameObject.SetActive(true);
        imageBackground?.gameObject.SetActive(false);
        
        Sequence mySequence = Sequence.Create()
            .Insert(0f, Tween.ShakeLocalPosition(rect, Vector3.one * 2, 10))
            .Insert(0f, Tween.ShakeLocalRotation(rect, Vector3.forward * 30, 15))
            .InsertCallback(15f, () => imageBackground?.gameObject.SetActive(true))
            .Insert(15f, Tween.Scale(rect, Vector3.zero, .5f, Ease.InOutSine))
            .Insert(15f, Tween.Color(imageLifePoint, imageLifePoint.color.WithAlpha(0f), .5f))
            .OnComplete(rect, target => target.gameObject.SetActive(false));

        return mySequence;
    }
}
