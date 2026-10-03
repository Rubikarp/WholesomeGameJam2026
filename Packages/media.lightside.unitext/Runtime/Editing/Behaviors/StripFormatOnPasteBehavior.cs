using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Drops one formatting modifier from pasted content even though the field renders it — paste a styled
    /// passage and this format alone arrives as plain text. Use when a field offers a format in its own UI but
    /// should not inherit it from the clipboard (e.g. keep authored colors, reject pasted ones). Formats the
    /// field has no style for are dropped already; this opts out one it does have.
    /// </summary>
    [Serializable]
    [TypeDescription("Drop one format from pasted content, keeping the text")]
    [TypeGroup("Formatting", 1)]
    public sealed partial class StripFormatOnPasteBehavior : InputBehavior, IModifierChangeSink
    {
        /// <summary>Modifier signature removed from attributed pasted content.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplyFormatChange),
            Validator = nameof(ValidateFormat), Owned = true)]
        [Tooltip("The modifier whose formatting is removed from pasted content.")]
        private BaseModifier format;

        internal override void ValidateChangeSink(IInputBehaviorChangeSink sink)
        {
            if (sink != null)
                BaseModifier.ValidateGraph(format);
        }

        internal override void OnChangeSinkChanged(
            IInputBehaviorChangeSink previous, IInputBehaviorChangeSink current)
        {
            format?.SetChangeSink(null);
            if (current != null) format?.SetChangeSink(this);
        }

        protected override void OnEnable() => editable.InputFilter.Subscribe(Filter);
        protected override void OnDisable() => editable.InputFilter.Unsubscribe(Filter);

        private void Filter(ref InputEdit edit)
        {
            if (edit.Rejected || edit.Reason != TextChangeReason.Paste) return;
            edit.RemoveFormatting(format);
        }

        private void ApplyFormatChange(BaseModifier previous, ref BaseModifier current)
        {
            if (ReferenceEquals(previous, current)) return;
            if (ReferenceEquals(previous?.ChangeSink, this)) previous.SetChangeSink(null);
            if (HasChangeSink) current?.SetChangeSink(this);
            NotifyStructureChanged(Members.Format);
        }

        private void ValidateFormat(BaseModifier candidate)
            => BaseModifier.ValidateGraph(candidate);

        void IModifierChangeSink.MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source, StateMember member, bool structural)
            => NotifyStructureChanged(Members.Format);
    }
}
