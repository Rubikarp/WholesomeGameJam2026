using UnityEngine;

namespace WG.Common
{
    public class FPSLimiter : MonoBehaviour
    {
        [SerializeField] private int frameRate = 30;

        private void Start()
        {
            Application.targetFrameRate = frameRate;
        }
    }
}