using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// Marks a UI hierarchy (formatting toolbar, context panel) as focus-preserving: pressing
    /// its controls neither deactivates the focused editor nor drops a selection session —
    /// the toolbar contract of every word processor. Put it on the panel root; nothing else
    /// to wire.
    /// </summary>
    [AddComponentMenu(LightSideMenu.AddComponent.FocusGuard)]
    public sealed class FocusGuard : MonoBehaviour
    {
        private static PointerEventData pointerData;
        private static readonly List<RaycastResult> raycastResults = new(8);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pointerData = null;
            raycastResults.Clear();
        }

        /// <summary>
        /// Whether the pointer currently sits over a focus-preserving hierarchy. The deselect
        /// callback carries no pointer payload (uGUI passes plain BaseEventData), so the press
        /// target is recovered by raycasting the pointer position.
        /// </summary>
        public static bool PointerIsOverGuarded()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            pointerData ??= new PointerEventData(eventSystem);
            pointerData.position = InputUtils.MousePosition;
            raycastResults.Clear();
            eventSystem.RaycastAll(pointerData, raycastResults);
            return raycastResults.Count > 0
                   && raycastResults[0].gameObject != null
                   && raycastResults[0].gameObject.GetComponentInParent<FocusGuard>() != null;
        }
    }
}
