using UnityEngine;

namespace WG.Common
{
    public class Drawer_SimpleTimer_Needle : MonoBehaviour
    {
        [SerializeField] SimpleTimer timer;
        [SerializeField] private float startRotation = -90f;
        [SerializeField] private bool clockwise = true;

        private void Update()
        {
            Vector3 lLocalRotation = transform.localEulerAngles;
            // 0 when just started, 1 when finished
            float lTimerRatio = 1f - (timer.TimeLeft / timer.duration);
            lLocalRotation.z = startRotation + lTimerRatio * 360f * (clockwise ? -1f : 1f);
            transform.localEulerAngles = lLocalRotation;
        }
    }
}