using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public class SimpleFPSCounter : MonoBehaviour
{
    private const float PollingIncrement = 0.1f;

    [SerializeField] private Text fpsText;
    private IEnumerator Start()
    {
        while (true)
        {
            var fps = 1f / Time.unscaledDeltaTime;
            fpsText.text = $"{fps:0.0} FPS";
            yield return new WaitForSeconds(PollingIncrement);
        }
    }
}
}
