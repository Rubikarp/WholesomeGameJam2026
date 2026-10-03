using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Detects loss of GPU-only contents through a small render-texture probe.</summary>
    public sealed class DeviceResetSentinel : IDisposable
    {
        private RenderTexture probe;
        private bool lost;

        /// <summary>Creates and arms a probe for the active graphics device.</summary>
        public DeviceResetSentinel() => Arm();

        private void Arm()
        {
            RenderTexture candidate = null;
            try
            {
                candidate = new RenderTexture(4, 4, 0)
                {
                    name = "LightSide_DeviceResetSentinel",
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (!candidate.Create())
                {
                    candidate.Release();
                    ObjectUtils.SafeDestroy(candidate);
                    lost = true;
                    return;
                }
                probe = candidate;
                lost = false;
            }
            catch
            {
                if (candidate != null)
                {
                    try
                    {
                        candidate.Release();
                    }
                    catch
                    {
                    }
                    ObjectUtils.SafeDestroy(candidate);
                }
                probe = null;
                lost = true;
            }
        }

        /// <summary>True once a device reset has invalidated the probe. Stays true until <see cref="Revive"/>.</summary>
        public bool Lost
        {
            get
            {
                if (!lost)
                    lost = probe == null || !probe.IsCreated();
                return lost;
            }
        }

        /// <summary>Re-arms detection after the reset has been handled.</summary>
        public void Revive()
        {
            Release();
            Arm();
        }

        /// <summary>Releases the probe resources.</summary>
        public void Dispose() => Release();

        private void Release()
        {
            var current = probe;
            probe = null;
            lost = true;
            if (current != null)
            {
                try
                {
                    current.Release();
                }
                finally
                {
                    ObjectUtils.SafeDestroy(current);
                }
            }
        }
    }
}
