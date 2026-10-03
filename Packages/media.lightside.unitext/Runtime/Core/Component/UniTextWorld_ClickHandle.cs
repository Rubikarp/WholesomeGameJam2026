using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// World-space pointer interaction. For pointer events to reach this component, attach
    /// <see cref="UniTextWorldRaycaster"/> to the rendering camera — it raycasts directly
    /// against <see cref="UniTextWorld.Active"/> instances, no collider required.
    /// </summary>
    public partial class UniTextWorld
    {
        /// <inheritdoc/>
        protected override Camera ResolveEventCamera(PointerEventData eventData) =>
            eventData.enterEventCamera != null ? eventData.enterEventCamera : eventData.pressEventCamera;
    }
}
