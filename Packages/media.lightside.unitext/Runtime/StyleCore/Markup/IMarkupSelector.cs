using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Chooses which visible markup tags a <see cref="ChromeRule"/> styles. Selectors are tested against each
    /// matched markup occurrence (its rule and modifier); chrome rules of different modifier kinds all apply, and
    /// among same-kind rules the highest <see cref="Specificity"/> wins, ties resolving to the first listed.
    /// Implement to target markup by a dimension other than the built-in modifier/rule/any.
    /// </summary>
    [StateHierarchy]
    public interface IMarkupSelector
    {
        /// <summary>
        /// Raised after configuration changes that can affect matching or specificity. Mutable custom
        /// implementations must raise this for every such change so an owning chrome rule can reconcile.
        /// </summary>
        event Action Changed;

        /// <summary>
        /// Tie-break weight when several selectors match the same markup. Higher wins. Use a larger value for a
        /// narrower selector so the specific rule beats the general one.
        /// </summary>
        int Specificity { get; }

        /// <summary>Whether this selector targets the markup parsed by <paramref name="rule"/> into <paramref name="modifier"/>.</summary>
        bool Matches(ParseRule rule, BaseModifier modifier);
    }

    /// <summary>Targets every visible markup tag, regardless of rule or modifier. Lowest specificity — the fallback.</summary>
    [Serializable]
    [TypeDescription("Any markup")]
    public sealed class AnyMarkup : IMarkupSelector
    {
        /// <inheritdoc />
        public event Action Changed
        {
            add { }
            remove { }
        }

        public int Specificity => 0;

        public bool Matches(ParseRule r, BaseModifier m) => true;
    }

    /// <summary>Targets markup whose modifier is of a chosen type.</summary>
    [Serializable]
    [TypeDescription("Markup of a given modifier")]
    public sealed partial class ByModifier : IMarkupSelector, IModifierChangeSink
    {
        /// <summary>Modifier signature matched by this selector.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyModifierChange),
            Validator = nameof(ValidateModifier), Owned = true)]
        private BaseModifier modifier;

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

        public int Specificity => 1;

        public bool Matches(ParseRule r, BaseModifier m) => modifier != null && modifier.SignatureMatches(m);

        private void ApplyModifierChange(BaseModifier previous, ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            if (changed != null) current?.SetChangeSink(this);
            changed?.Invoke();
        }

        private void ValidateModifier(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
            => changed?.Invoke();
    }

    /// <summary>
    /// Targets markup parsed by a rule with a given <see cref="ParseRule.Identity"/> (case-insensitive) —
    /// <c>tag:b</c>, <c>marker:**</c>, or a singleton's full type name. The most specific selector.
    /// </summary>
    [Serializable]
    [TypeDescription("Markup of a given rule (by Identity)")]
    public sealed partial class ByRule : IMarkupSelector
    {
        /// <summary>Case-insensitive parse-rule identity matched by this selector.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        private string identity;

        /// <inheritdoc />
        public event Action Changed;

        public int Specificity => 2;

        public bool Matches(ParseRule r, BaseModifier m)
            => r != null && !string.IsNullOrEmpty(identity) && r.Identity.EqualsIgnoreCase(identity);

        private void NotifyChanged() => Changed?.Invoke();
    }
}
