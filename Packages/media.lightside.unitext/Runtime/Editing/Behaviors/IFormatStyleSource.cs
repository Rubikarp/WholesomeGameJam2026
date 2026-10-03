using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Source of the style a <see cref="TextFormattingBehavior"/> command toggles. Applies a style already
    /// configured on the component, in that style's own markup syntax — it never creates or registers anything, so
    /// on a field that has no matching style the command is inert. <see cref="ReferencedStyle"/> addresses by
    /// modifier type. Implement to add a custom resolution.
    /// </summary>
    [StateHierarchy]
    public interface IFormatStyleSource
    {
        /// <summary>
        /// Raised after configuration changes that can affect the resolved or emitted style. Mutable custom
        /// implementations must raise this for every such change so an owning formatting behavior can reconcile.
        /// </summary>
        event Action Changed;

        /// <summary>Toggles the style on the selection (or the pending typing style at a caret). Returns whether it is now on.</summary>
        bool Toggle(UniTextEditable editable);
    }

    /// <summary>
    /// Toggles whichever style the component carries for the chosen modifier type, in that style's own markup
    /// syntax. The optional parameter is passed to the rule, so a parameterised rule (an XML-like tag) writes it
    /// into the markup. Several styles share a modifier type — the first in resolution order wins.
    /// </summary>
    [Serializable]
    [TypeDescription("Apply a style on the component, by modifier")]
    public sealed partial class ReferencedStyle : IFormatStyleSource, IModifierChangeSink
    {
        /// <summary>Modifier signature used to resolve the configured component style.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyModifierChange),
            Validator = nameof(ValidateModifier), Owned = true)]
        private BaseModifier modifier;

        /// <summary>Optional markup parameter passed when the style is toggled.</summary>
        [SerializeField, DefaultParameter, StateProperty(nameof(NotifyChanged))]
        private string parameter;

        [NonSerialized] private Action changed;

        /// <inheritdoc />
        public event Action Changed
        {
            add
            {
                if (changed == null)
                {
                    modifier?.SetChangeSink(this);
                }
                changed += value;
            }
            remove
            {
                changed -= value;
                if (changed == null) modifier?.SetChangeSink(null);
            }
        }

        public bool Toggle(UniTextEditable editable)
            => modifier != null && editable != null && editable.ToggleStyle(modifier, parameter);

        private void ApplyModifierChange(BaseModifier previous, ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            if (changed != null) current?.SetChangeSink(this);
            NotifyChanged();
        }

        private void ValidateModifier(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        private void NotifyChanged() => changed?.Invoke();

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
            => NotifyChanged();
    }
}
