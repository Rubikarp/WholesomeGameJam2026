using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A component <see cref="WorldBatcher"/> can batch into combined world-space meshes. Implemented by a
    /// <see cref="UnityEngine.Object"/> (a MonoBehaviour): the engine relies on Unity's fake-null through
    /// <see cref="GameObject"/>/<see cref="Transform"/>, never on interface reference identity — see
    /// <c>WorldBatcher.Alive</c>. Batch-key getters are polled every flush (not events), so they must be cheap.
    /// </summary>
    public interface IWorldBatchSource
    {
        Transform Transform { get; }
        GameObject GameObject { get; }
        int SortingLayerID { get; }
        int SortingOrder { get; }
        bool CastShadows { get; }

        /// <summary>Extra batch-key discriminator: sources reporting different values never share a combined
        /// mesh, and therefore never share a renderer or the single depth-sorting position that comes with it.
        /// Return 0 to take part in normal grouping.</summary>
        int BatchGroupId { get; }

        bool RenderSuppressed { get; }

        /// <summary>Fill <paramref name="buffer"/> (pre-cleared by the engine) with fully-resolved segments. Called ONLY inside the capture window; do not retain <paramref name="buffer"/> or the arrays placed in it.</summary>
        void Collect(List<BatchSegment> buffer);
    }
}
