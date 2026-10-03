using UnityEngine;
using UnityEngine.UI;
using System;

namespace JeffGrawAssets.FlexibleUI
{
public sealed class GlassDemoClock : MonoBehaviour
{
    public Text label;

    private void OnEnable()
    {
        UpdateClock();
        InvokeRepeating(nameof(UpdateClock), 1f, 1f);
    }

    private void OnDisable() => CancelInvoke();

    private void UpdateClock()
    {
        if (label)
            label.text = DateTime.Now.ToString("ddd d MMM  h:mm tt");
    }
}
}
