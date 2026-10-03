using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Binds the Enter key to submit instead of inserting a newline: <see cref="SubmitKey.Enter"/> submits
    /// on Enter (Shift+Enter makes a newline), <see cref="SubmitKey.ModifierEnter"/> submits on Ctrl/Cmd+Enter
    /// (Enter makes a newline). Fits chat composers, comment boxes, prompt and search fields alike. Focus
    /// survives submit by default; turn <see cref="KeepFocusOnSubmit"/> off for submit-and-close surfaces
    /// (comment forms, modal editors). For a field that accepts no newlines at all, use
    /// <see cref="SingleLineBehavior"/> instead.
    /// </summary>
    [Serializable]
    [TypeDescription("Bind Enter / Ctrl+Enter to submit instead of newline")]
    [TypeGroup("Keys", 3)]
    public sealed partial class SubmitKeyBehavior : InputBehavior
    {
        /// <summary>Key combination that submits the editor.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Which key combination submits.")]
        private SubmitKey submit = SubmitKey.Enter;

        /// <summary>Whether focus survives submission.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Keep focus after submit. On (default): focus stays, composers keep typing. Off: " +
                 "release focus — for submit-and-close surfaces (comment forms, modal editors).")]
        private bool keepFocusOnSubmit = true;

        protected override void OnEnable()
        {
            editable.KeyResolver.Subscribe(Resolve);
            editable.Submitted += OnSubmit;
        }

        protected override void OnDisable()
        {
            editable.KeyResolver.Unsubscribe(Resolve);
            editable.Submitted -= OnSubmit;
        }

        private void Resolve(ref KeyResolve key)
        {
            if (key.action != EditAction.None) return;
            if (key.key != NativeKeyCode.Return && key.key != NativeKeyCode.KeypadEnter) return;

            bool shift = (key.modifiers & NativeModifiers.Shift) != 0;
            bool modifier = (key.modifiers & (NativeModifiers.Ctrl | NativeModifiers.Cmd)) != 0;
            key.action = submit == SubmitKey.ModifierEnter
                ? (modifier ? EditAction.Submit : EditAction.InsertNewline)
                : (shift ? EditAction.InsertNewline : EditAction.Submit);
        }

        private void OnSubmit(string text)
        {
            if (!keepFocusOnSubmit) editable.RequestDefocusAfterSubmit();
        }
    }

    /// <summary>Key combination that submits in <see cref="SubmitKeyBehavior"/>.</summary>
    public enum SubmitKey
    {
        /// <summary>Enter submits; Shift+Enter inserts a newline (Slack / Discord default).</summary>
        Enter,

        /// <summary>Ctrl/Cmd+Enter submits; Enter inserts a newline.</summary>
        ModifierEnter,
    }

}
