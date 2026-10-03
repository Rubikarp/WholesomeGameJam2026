using System;
using System.Collections.Generic;

namespace LightSide
{
    internal interface IInputBehaviorChangeSink
    {
        void MarkInputBehaviorChanged(InputBehavior behavior,
            IStateMemberReplay source, StateMember member, bool structural = false);
    }

    internal delegate void NestedStateChangedHandler(
        IStateMemberReplay source, StateMember member);

    /// <summary>Base class for composable input policies attached to a <see cref="UniTextEditable"/>.</summary>
    [Serializable]
    [TypeMenuSuffix("InputBehavior", "Behavior")]
    [StateHierarchy]
    public abstract class InputBehavior
    {
        /// <summary>The owning editor, assigned before <see cref="OnEnable"/> and retained through <see cref="OnDisable"/>.</summary>
        [NonSerialized] protected UniTextEditable editable;

        [NonSerialized] private bool isEnabled;
        [NonSerialized] private IInputBehaviorChangeSink changeSink;

        /// <summary>Indicates whether the behavior is active, including while either lifecycle hook executes.</summary>
        protected bool IsEnabled => isEnabled;

        /// <summary>Indicates whether authored-state changes can be propagated to an owning state graph.</summary>
        protected bool HasChangeSink => changeSink != null;

        internal UniTextEditable Owner => editable;
        internal IInputBehaviorChangeSink ChangeSink => changeSink;

        internal void SetOwner(UniTextEditable owner, IInputBehaviorChangeSink sink = null)
        {
            if ((owner == null && sink != null) || (owner != null && !ReferenceEquals(owner, sink)))
                throw new ArgumentException(
                    "An editor-owned input behavior must use that UniTextEditable as its change sink.", nameof(sink));
            ValidateAttachment(owner, sink);
            if (isEnabled && !ReferenceEquals(editable, owner))
                throw new InvalidOperationException(
                    $"Enabled input behavior '{GetType().FullName}' cannot change its editor owner. Disable it before transferring ownership.");

            if (ReferenceEquals(editable, owner) && ReferenceEquals(changeSink, sink))
                return;

            editable = owner;
            ApplyChangeSink(sink);
        }

        internal void SetChangeSink(IInputBehaviorChangeSink sink)
        {
            if (editable != null && !ReferenceEquals(editable, sink))
                throw new InvalidOperationException(
                    $"Editor-owned input behavior '{GetType().FullName}' must change its sink together with its editor owner.");
            ValidateAttachment(editable, sink);
            ApplyChangeSink(sink);
        }

        internal void ValidateAttachment(UniTextEditable owner, IInputBehaviorChangeSink sink)
        {
            ValidateChangeSink(sink);
        }

        internal virtual void ValidateChangeSink(IInputBehaviorChangeSink sink) { }

        internal virtual void OnChangeSinkChanged(
            IInputBehaviorChangeSink previous, IInputBehaviorChangeSink current) { }

        private void ApplyChangeSink(IInputBehaviorChangeSink sink)
        {
            if (ReferenceEquals(changeSink, sink))
                return;

            var previous = changeSink;
            changeSink = sink;
            OnChangeSinkChanged(previous, sink);
        }

        internal void Enable()
        {
            if (isEnabled) return;
            isEnabled = true;
            OnEnable();
        }

        internal void Disable()
        {
            if (!isEnabled) return;
            OnDisable();
            isEnabled = false;
        }

        /// <summary>Registers editor hooks when the behavior becomes active.</summary>
        protected virtual void OnEnable() { }

        /// <summary>Releases resources and editor hooks when the behavior becomes inactive.</summary>
        protected virtual void OnDisable() { }

        /// <summary>Reports a value change to the owning state graph.</summary>
        protected void NotifyChanged(StateMember member = default)
            => changeSink?.MarkInputBehaviorChanged(this,
                this as IStateMemberReplay, member);

        /// <summary>Reports an owned-state replacement to the owning state graph.</summary>
        protected void NotifyStructureChanged(StateMember member = default)
            => changeSink?.MarkInputBehaviorChanged(this,
                this as IStateMemberReplay, member, true);

        /// <summary>Reports a nested-state change to the owning state graph.</summary>
        protected void NotifyNestedChanged(IStateMemberReplay source, StateMember member)
            => changeSink?.MarkInputBehaviorChanged(this, source, member);

        /// <summary>Signals that input-session configuration resolved by this behavior has changed.</summary>
        protected void InvalidateInputSessionConfiguration()
        {
            if (isEnabled) editable?.NotifyInputSessionConfigurationChanged();
        }

        internal virtual bool ReplayNestedStateMember(InputBehavior authoredBehavior,
            IStateMemberReplay source, StateMember member) => false;
    }

    /// <summary>
    /// Describes a prospective committed-text replacement passed by reference through
    /// <see cref="UniTextEditable.InputFilter"/>. Filters may rewrite its range, replacement, caret,
    /// and attributed payload or reject it; composition previews, history replay, and programmatic text
    /// assignments do not enter this pipeline.
    /// </summary>
    public struct InputEdit
    {
        /// <summary>Replacement text, or an empty string for a deletion.</summary>
        public string text;

        /// <summary>Codepoint index at which the replacement begins.</summary>
        public int insertCodepointIndex;

        /// <summary>Number of codepoints replaced at <see cref="insertCodepointIndex"/>.</summary>
        public int replacedCodepoints;

        /// <summary>Committed document state preceding the edit.</summary>
        public ITextDocument document;

        /// <summary>Resulting caret codepoint, or <c>-1</c> to place it after the inserted text.</summary>
        public int caret;

        /// <summary>Gets the <see cref="TextChangeReason"/> associated with the edit.</summary>
        public string Reason { get; internal set; }

        private bool rejected;
        internal List<SourceAnnotation> annotations;

        /// <summary>Gets whether the edit is rejected; setting it rejects the edit for the rest of the filter chain.</summary>
        public bool Rejected { get => rejected; set => rejected |= value; }

        /// <summary>Removes matching formatting from attributed pasted content without changing its text.</summary>
        public void RemoveFormatting(BaseModifier modifier)
        {
            if (modifier == null || annotations == null) return;
            for (var i = annotations.Count - 1; i >= 0; i--)
            {
                var annotation = annotations[i];
                if (annotation.kind != SourceAnnotationKind.Style) continue;
                var stored = annotation.modifier;
                if (stored == null) continue;
                if (modifier.SignatureMatches(stored)
                    || modifier is not CompositeModifier
                    && CompositeModifier.FindLeaf(stored, modifier.GetType()) != null)
                    annotations.RemoveAt(i);
            }
        }
    }

    /// <summary>Describes a key press being resolved before the platform key map is applied.</summary>
    public struct KeyResolve
    {
        /// <summary>The platform-independent key code.</summary>
        public NativeKeyCode key;

        /// <summary>The modifier keys held for the key press.</summary>
        public NativeModifiers modifiers;

        /// <summary>The resolved action; <see cref="EditAction.None"/> until a hook sets it.</summary>
        public EditAction action;
    }

    /// <summary>Describes soft-keyboard traits resolved before an input session opens or refreshes.</summary>
    public struct KeyboardRequest
    {
        /// <summary>Keyboard traits to apply; null leaves OS defaults.</summary>
        public NativeKeyboardConfig config;
    }

}
