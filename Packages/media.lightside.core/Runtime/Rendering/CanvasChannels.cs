using UnityEngine;

namespace LightSide
{
    /// <summary>Canvas vertex-stream configuration shared by the LightSide Canvas components.</summary>
    public static class CanvasChannels
    {
        /// <summary>
        /// Ensures <paramref name="canvas"/> streams every channel in <paramref name="required"/>, writing
        /// the Canvas only when one is actually missing — an already-configured scene is never dirtied.
        /// </summary>
        public static void Ensure(Canvas canvas, AdditionalCanvasShaderChannels required)
        {
            if (canvas == null) return;
            var current = canvas.additionalShaderChannels;
            var missing = required & ~current;
            if (missing != 0)
                canvas.additionalShaderChannels = current | missing;
        }
    }
}
