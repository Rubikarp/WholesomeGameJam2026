using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// One styling rule for visible markup tag characters: a <see cref="Selector"/> choosing which markup it
    /// targets and the <see cref="Style"/> modifier (plus its <see cref="Parameter"/>) painted onto those tags.
    /// The tag characters are protected from the enclosing document style so only this styling shows. Across a
    /// list, rules with different modifier kinds compose; same-kind rules resolve by selector specificity, ties
    /// to the first listed.
    /// </summary>
    [Serializable]
    public sealed partial class ChromeRule : IModifierChangeSink
    {
        /// <summary>Markup selector determining which visible tag characters are targeted.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplySelectorChange), Owned = true)]
        private IMarkupSelector selector;

        /// <summary>Modifier applied to matching visible tag characters.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyStyleChange),
            Validator = nameof(ValidateStyle), Owned = true)]
        private BaseModifier style;

        /// <summary>Default parameter passed to the chrome modifier.</summary>
        [SerializeField, DefaultParameter, StateProperty(nameof(NotifyChanged))]
        private string parameter;

        [NonSerialized] private Action changed;

        internal void SetChangeCallback(Action callback)
        {
            if (changed != null && callback != null && changed.Equals(callback)) return;
            ValidateChangeCallback(callback);
            if (selector != null) selector.Changed -= NotifyChanged;
            style?.SetChangeSink(null);
            changed = callback;
            if (callback == null) return;
            BindSelector(selector);
            style?.SetChangeSink(this);
        }

        internal void ValidateChangeCallback(Action callback)
        {
            if (callback != null)
            {
                BaseModifier.ValidateGraph(style);
            }
        }

        internal bool HasChangeCallback(Action callback) => changed?.Equals(callback) == true;

        private void ApplySelectorChange(IMarkupSelector previous, IMarkupSelector current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (previous != null) previous.Changed -= NotifyChanged;
            if (changed != null)
            {
                if (current != null) current.Changed -= NotifyChanged;
                BindSelector(current);
            }
            NotifyChanged();
        }

        private void BindSelector(IMarkupSelector current)
        {
            if (current == null) return;
            current.Changed += NotifyChanged;
        }

        private void ApplyStyleChange(BaseModifier previous, ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            if (changed != null) current?.SetChangeSink(this);
            NotifyChanged();
        }

        private void ValidateStyle(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        private void NotifyChanged() => changed?.Invoke();

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
            => NotifyChanged();
    }
}
