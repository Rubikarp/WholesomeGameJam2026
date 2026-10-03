using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    public partial class UniText
    {
        /// <inheritdoc/>
        protected override Camera ResolveEventCamera(PointerEventData eventData) =>
            CanvasUtil.GetCanvasCamera(canvas);
    }
}
