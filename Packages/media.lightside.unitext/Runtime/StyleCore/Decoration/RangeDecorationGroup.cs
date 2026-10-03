using System;

namespace LightSide
{
    /// <summary>One renderer-owned decoration mesh and its deterministic sort identity.</summary>
    internal sealed class RangeDecorationGroup
    {
        private RangeDecorationRenderer renderer;
        private readonly string id;
        private RangeDecorationMesh decorationMesh;

        private bool alive = true;
        private int sortPriority;
        private int sortSequence;

        internal RangeDecorationGroup(RangeDecorationRenderer renderer, string id)
        {
            this.renderer = renderer;
            this.id = id ?? "";
        }

        public string Id => id;
        public int SortPriority => sortPriority;
        public int SortSequence => sortSequence;

        public void SetSort(int priority, int sequence)
        {
            ThrowIfDisposed();
            if (sortPriority == priority && sortSequence == sequence) return;
            sortPriority = priority;
            sortSequence = sequence;
            renderer.NotifySortChanged(this);
        }

        internal RangeDecorationMesh DecorationMesh
        {
            get
            {
                ThrowIfDisposed();
                return decorationMesh ??= new RangeDecorationMesh();
            }
        }

        internal RangeDecorationMesh ExistingDecorationMesh => decorationMesh;

        public void Clear()
        {
            ThrowIfDisposed();
            if (decorationMesh == null || decorationMesh.IsEmpty) return;
            decorationMesh?.Clear();
            renderer.MarkDirty();
        }

        public void Destroy()
        {
            if (!alive) return;
            Clear();
            decorationMesh?.Return();
            decorationMesh = null;
            renderer.RemoveGroupInternal(this);
            renderer = null;
            alive = false;
        }

        internal void MarkDirty() => renderer?.MarkDirty();

        private void ThrowIfDisposed()
        {
            if (!alive)
                throw new ObjectDisposedException(nameof(RangeDecorationGroup));
        }
    }
}
