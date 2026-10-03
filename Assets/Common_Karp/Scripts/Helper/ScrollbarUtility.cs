using System.Collections.Generic;
using System.Collections;
using PrimeTween;
using UnityEngine.UI;
using UnityEngine;
using Alchemy.Inspector;

public class ScrollbarUtility : MonoBehaviour
{
    public Scrollbar bar;

    public void MoveTo(float targetValue)
    {
        Tween.StopAll(transform);

        float currentPos = bar.value;
        float distance = Mathf.Abs(currentPos - targetValue);

        Tween.Custom(bar, bar.value, targetValue, distance, 
            (target, newVal) => bar.value = newVal,
            ease: Ease.InOutSine);
    }

    [Button] public void MoveToStart() => MoveTo(0f);
    [Button] public void MoveToEnd() => MoveTo(1f);
}
