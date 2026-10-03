using UnityEngine;

namespace LightSide
{
    /// <summary>Nice-number stepping shared by rulers, grids and axis ticks.</summary>
    public static class AxisScale
    {
        private static readonly float[] multipliers = { 1f, 2f, 5f, 10f };

        /// <summary>
        /// The smallest 1-2-5-decade step no finer than <paramref name="rawStep"/>, in the caller's units.
        /// Values at or below zero — a transient of unresolved layout — are treated as 1e-6.
        /// </summary>
        public static float NiceStep(float rawStep)
        {
            rawStep = Mathf.Max(rawStep, 1e-6f);
            var magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(rawStep)));
            foreach (var multiplier in multipliers)
                if (magnitude * multiplier >= rawStep)
                    return magnitude * multiplier;
            return magnitude * 10f;
        }
    }
}
