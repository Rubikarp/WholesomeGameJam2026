using System;

namespace LightSide
{
    /// <summary>
    /// The <see cref="UniTextFieldsAnimationHandler"/> for world-space text: also animates
    /// <see cref="UniTextWorld.SortingOrder"/>, <see cref="UniTextWorld.SortingLayerID"/>, and
    /// <see cref="UniTextWorld.CastShadows"/>, re-keying the batch when the <c>Animator</c> changes them.
    /// </summary>
    [Serializable]
    [TypeGroup("Component Fields", 0)]
    [TypeDescription("UniTextWorld fields, plus sorting order, layer, and shadow casting.")]
    public sealed class UniTextWorldFieldsAnimationHandler : UniTextFieldsAnimationHandler
    {
        private int sortingOrder;
        private int sortingLayerID;
        private bool castShadows;

        /// <inheritdoc/>
        protected override void OnBindExtra(UniTextBase host)
        {
            if (host is not UniTextWorld world) return;
            sortingOrder = world.SortingOrder;
            sortingLayerID = world.SortingLayerID;
            castShadows = world.CastShadows;
        }

        /// <inheritdoc/>
        protected override UniTextDirty OnDiffExtra(UniTextBase host)
        {
            if (host is not UniTextWorld world) return UniTextDirty.None;

            var moved = false;
            if (sortingOrder != world.SortingOrder) { sortingOrder = world.SortingOrder; moved = true; }
            if (sortingLayerID != world.SortingLayerID) { sortingLayerID = world.SortingLayerID; moved = true; }
            if (castShadows != world.CastShadows) { castShadows = world.CastShadows; moved = true; }
            if (moved) world.RaiseSortingChanged();

            return UniTextDirty.None;
        }
    }
}
