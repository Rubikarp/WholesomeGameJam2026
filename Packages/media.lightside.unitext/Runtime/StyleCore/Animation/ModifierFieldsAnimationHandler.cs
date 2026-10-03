using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Animates modifier fields from a Unity <c>Animator</c>: diffs every state field of every
    /// modifier in the host's styles and raises each field's own declared invalidation. Rebinds
    /// itself when the style graph changes.
    /// </summary>
    /// <remarks>
    /// A field whose change notification requires a value transition is not diffed — drive it
    /// through its parameter surface (<see cref="UniTextDriver"/>, ownership) instead.
    /// </remarks>
    [Serializable]
    [TypeGroup("Modifier Fields", 1)]
    [TypeDescription("Every state field of every modifier on the host.")]
    public sealed class ModifierFieldsAnimationHandler : AnimationHandler
    {
        [NonSerialized] private readonly List<Cell> cells = new();
        [NonSerialized] private readonly CellBuilder cellBuilder = new();
        [NonSerialized] private Action graphChangedCallback;

        /// <inheritdoc/>
        protected override void OnBind(UniTextBase host)
        {
            graphChangedCallback ??= Rebuild;
            host.StyleGraphChanged += graphChangedCallback;
            Rebuild();
        }

        /// <inheritdoc/>
        protected override void OnUnbind()
        {
            if (Host != null) Host.StyleGraphChanged -= graphChangedCallback;
            cells.Clear();
        }

        /// <inheritdoc/>
        protected override void OnDiff(UniTextBase host)
        {
            for (var i = 0; i < cells.Count; i++) cells[i].Diff();
        }

        private void Rebuild()
        {
            cells.Clear();
            cellBuilder.Cells = cells;
            var host = Host;
            if (host == null) return;
            var styles = host.Styles;
            for (var i = 0; i < styles.Count; i++)
                Collect(styles[i]?.Modifier);
        }

        private void Collect(BaseModifier modifier)
        {
            if (modifier == null) return;
            if (modifier is IStateAccessSource source)
            {
                var accessors = source.StateAccessors;
                cellBuilder.Target = modifier;
                for (var i = 0; i < accessors.Length; i++)
                    if (accessors[i].CanInvalidate)
                        accessors[i].Accept(cellBuilder);
                cellBuilder.Target = null;
            }
            if (modifier.Children is not { } children) return;
            for (var i = 0; i < children.Count; i++) Collect(children[i]);
        }

        private abstract class Cell
        {
            public abstract void Diff();
        }

        private sealed class Cell<TOwner, TValue> : Cell
        {
            private readonly TOwner owner;
            private readonly StateAccessor<TOwner, TValue> accessor;
            private TValue baseline;

            public Cell(TOwner owner, StateAccessor<TOwner, TValue> accessor)
            {
                this.owner = owner;
                this.accessor = accessor;
                baseline = accessor.Get(owner);
            }

            public override void Diff()
            {
                var current = accessor.Get(owner);
                if (ValueEquality<TValue>.Same(baseline, current)) return;
                baseline = current;
                accessor.Invalidate(owner);
            }
        }

        private sealed class CellBuilder : IStateAccessorVisitor
        {
            public BaseModifier Target;
            public List<Cell> Cells;

            public void Visit<TOwner, TValue>(StateAccessor<TOwner, TValue> accessor)
                => Cells.Add(new Cell<TOwner, TValue>((TOwner)(object)Target, accessor));
        }
    }
}
