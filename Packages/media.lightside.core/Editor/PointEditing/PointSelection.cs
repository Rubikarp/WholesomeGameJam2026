using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The selection set for <see cref="PointEditor"/> — a list of point indices plus the interaction rules for
    /// changing it: click, shift-toggle, additive box-select, select-all, invert, and snapshot/restore (used to
    /// roll a selection back when a modal transform is cancelled). Pure state; no rendering or input polling.
    /// </summary>
    public sealed class PointSelection
    {
        private readonly List<int> selected = new();

        /// <summary>Currently selected indices, in selection order.</summary>
        public IReadOnlyList<int> Indices => selected;

        /// <summary>How many points are selected.</summary>
        public int Count => selected.Count;

        /// <summary>Whether <paramref name="index"/> is selected.</summary>
        public bool Contains(int index) => selected.Contains(index);

        /// <summary>Clears the selection.</summary>
        public void Clear() => selected.Clear();

        /// <summary>Adds <paramref name="index"/> if absent. Returns whether it was added.</summary>
        public bool Add(int index)
        {
            if (selected.Contains(index)) return false;
            selected.Add(index);
            return true;
        }

        /// <summary>Removes <paramref name="index"/>. Returns whether it was present.</summary>
        public bool Remove(int index) => selected.Remove(index);

        /// <summary>Toggles <paramref name="index"/>. Returns whether it is selected afterwards.</summary>
        public bool Toggle(int index)
        {
            if (selected.Remove(index)) return false;
            selected.Add(index);
            return true;
        }

        /// <summary>Selects every index in <c>[0, count)</c>.</summary>
        public void SelectAll(int count)
        {
            selected.Clear();
            for (int i = 0; i < count; i++) selected.Add(i);
        }

        /// <summary>Replaces the selection with its complement over <c>[0, count)</c>.</summary>
        public void Invert(int count)
        {
            var had = new HashSet<int>(selected);
            selected.Clear();
            for (int i = 0; i < count; i++)
                if (!had.Contains(i)) selected.Add(i);
        }

        /// <summary>
        /// Applies a click. <paramref name="clicked"/> is the hit index or -1 for empty space.
        /// Plain click: empty clears; a point becomes the sole selection unless it is already selected, in which
        /// case the set is kept so the whole group can be dragged. Shift click toggles the hit point (and never
        /// clears on empty).
        /// </summary>
        public void HandleClick(int clicked, bool shift)
        {
            if (clicked < 0)
            {
                if (!shift) Clear();
                return;
            }

            if (shift)
            {
                Toggle(clicked);
                return;
            }

            if (selected.Contains(clicked)) return;
            selected.Clear();
            selected.Add(clicked);
        }

        /// <summary>
        /// Adds every index in <c>[0, count)</c> whose plane position (from <paramref name="planePosition"/>)
        /// falls inside <paramref name="planeRect"/>. When <paramref name="additive"/> is false the selection is
        /// cleared first.
        /// </summary>
        public void SelectInRect(Rect planeRect, int count, Func<int, Vector2> planePosition, bool additive)
        {
            if (!additive) selected.Clear();
            for (int i = 0; i < count; i++)
                if (planeRect.Contains(planePosition(i))) Add(i);
        }

        /// <summary>Copies the current selection into <paramref name="into"/> (for later <see cref="Restore"/>).</summary>
        public void Snapshot(List<int> into)
        {
            into.Clear();
            into.AddRange(selected);
        }

        /// <summary>Restores a selection captured by <see cref="Snapshot"/>.</summary>
        public void Restore(List<int> from)
        {
            selected.Clear();
            selected.AddRange(from);
        }
    }
}
