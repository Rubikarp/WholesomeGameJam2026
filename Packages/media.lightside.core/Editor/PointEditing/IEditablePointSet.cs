using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The data seam for <see cref="PointEditor"/>: any indexed set of points in world space. Implement this to
    /// make group selection + G/S/R editing drive your data — Bézier knots, Transforms, graph nodes, cards.
    /// Positions are world-space; the editor converts to and from its working plane via <see cref="IEditSurface"/>.
    /// </summary>
    public interface IEditablePointSet
    {
        /// <summary>Number of editable points.</summary>
        int Count { get; }

        /// <summary>World-space position of the point at <paramref name="index"/>.</summary>
        Vector3 GetPosition(int index);

        /// <summary>Writes the world-space position of the point at <paramref name="index"/>.</summary>
        void SetPosition(int index, Vector3 world);

        /// <summary>Object to record for undo (the component or asset that owns the points), or <see langword="null"/> to skip undo.</summary>
        UnityEngine.Object UndoContext { get; }

        /// <summary>
        /// Appends the indices that must move rigidly with <paramref name="index"/> when it is dragged or
        /// transformed as a "parent" — e.g. a Bézier knot's two handle points follow its anchor. Leave empty
        /// for independent points. The editor will not double-apply a dependent that is itself selected.
        /// </summary>
        void GetRigidDependents(int index, List<int> into);
    }
}
