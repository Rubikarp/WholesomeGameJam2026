using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Display-density scaling for gesture thresholds (long-press promotion, desktop multi-click,
    /// <see cref="TouchGestureRecognizer"/> touch taps). Thresholds are authored in
    /// density-independent pixels (Android dp, 160 dpi baseline); <see cref="SlopPx"/> converts
    /// them to screen pixels at use — never cached, so display switches and editor device
    /// simulation stay correct.
    /// </summary>
    public static class GestureMetrics
    {
        /// <summary>Density baseline of the Android dp and iOS pt conventions.</summary>
        public const float TouchDensityBaseline = 160f;

        /// <summary>Density baseline of the CSS reference pixel, shared by desktop UI scales.</summary>
        public const float PointerDensityBaseline = 96f;

        /// <summary>
        /// Density-independent unit → px scale for the current display: <c>Screen.dpi</c> over
        /// <paramref name="baselineDpi"/> when the reported dpi is plausible; otherwise
        /// <paramref name="fallback"/> — pass the root canvas scale factor (see
        /// <see cref="CanvasScale"/>) so callers still track the UI scale on platforms where
        /// <c>Screen.dpi</c> is 0 (some Android devices, most WebGL browsers). Re-read on every
        /// use — display switches invalidate any cached value.
        /// </summary>
        public static float DpiScale(float fallback = 1f,
            float baselineDpi = TouchDensityBaseline)
        {
            var dpi = Screen.dpi;
            return dpi > 25f ? dpi / baselineDpi : fallback;
        }

        /// <summary>Density fallback for <see cref="DpiScale"/>: the root canvas scale factor, 1 without a canvas.</summary>
        public static float CanvasScale(Canvas canvas)
            => canvas != null ? canvas.rootCanvas.scaleFactor : 1f;

        /// <summary>Converts a dp threshold to screen pixels for the display <paramref name="canvas"/> lives on; <see langword="null"/> canvas falls back to 1 px per dp when <c>Screen.dpi</c> is unreliable.</summary>
        public static float SlopPx(float slopDp, Canvas canvas)
            => slopDp * DpiScale(CanvasScale(canvas));
    }

    /// <summary>
    /// Tap / click chain counter shared by desktop multi-click and touch multi-tap: a press within
    /// the time window and distance slop of the last recorded tap continues the chain, anything
    /// else restarts it at 1. Thresholds are passed per call so desktop and touch keep their own
    /// platform conventions over one mechanism.
    /// </summary>
    public struct TapChain
    {
        private int count;
        private float lastTime;
        private Vector2 lastPosition;

        /// <summary>Current chain length; 0 until the first <see cref="Advance"/> after a reset.</summary>
        public int Count => count;

        /// <summary>Advances the chain for a new press and returns the new count. Records the press as the new chain reference point.</summary>
        public int Advance(Vector2 position, float time, float window, float slopPx)
        {
            count = count > 0
                    && time - lastTime <= window
                    && Vector2.Distance(position, lastPosition) <= slopPx
                ? count + 1
                : 1;
            lastTime = time;
            lastPosition = position;
            return count;
        }

        /// <summary>
        /// Re-stamps the chain reference point without changing the count. Touch convention
        /// measures the multi-tap window from the previous tap's RELEASE (Android
        /// <c>DOUBLE_TAP_TIMEOUT</c>), so a completed tap re-stamps here on pointer-up;
        /// desktop measures press-to-press and needs only <see cref="Advance"/>.
        /// </summary>
        public void Stamp(Vector2 position, float time)
        {
            lastTime = time;
            lastPosition = position;
        }

        /// <summary>Resets the chain. A non-zero <paramref name="count"/> forces the counter for cross-pipeline sync; the cleared timestamp makes the next press restart the chain.</summary>
        public void Reset(int count = 0)
        {
            this.count = count;
            lastTime = 0f;
            lastPosition = default;
        }
    }
}
