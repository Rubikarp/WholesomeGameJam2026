using UnityEngine;

namespace LightSide.Samples
{
    /// <summary>
    /// Binds runtime chat text to the visual message prefab. The prefab owns the complete layout and appearance.
    /// </summary>
    public sealed class DemoChatMessage : MonoBehaviour
    {
        [SerializeField] private UniText text;

        /// <summary>Replaces the message content without changing the prefab-owned presentation.</summary>
        public void SetText(string value) => text.SetText(value);
    }
}
