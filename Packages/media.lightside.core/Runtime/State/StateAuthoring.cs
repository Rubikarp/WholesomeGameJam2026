#if UNITY_EDITOR
using UnityEngine;
#endif

namespace LightSide
{
    /// <summary>Ambient provenance of the state change being applied on the current thread.</summary>
    public static class StateAuthoring
    {
        /// <summary>
        /// Whether the current change manipulates the authored document rather than live player
        /// state: any edit-mode change, or a play-mode change arriving through the editor's
        /// serialized ingress (Inspector, Undo/Redo, prefab operations). Constant false in
        /// players. Read it during the change itself — deferred work observes the value recorded
        /// at the change, not this property.
        /// </summary>
        public static bool IsActive =>
#if UNITY_EDITOR
            !Application.isPlaying || StateOwnership.InEditorTransaction;
#else
            false;
#endif
    }
}
