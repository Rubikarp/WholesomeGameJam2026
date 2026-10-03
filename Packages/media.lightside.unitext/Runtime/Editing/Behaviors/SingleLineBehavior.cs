using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Single-line form-field semantics, matching the web <c>&lt;input&gt;</c> contract: every Enter
    /// press submits, newlines are stripped from all inserted text (typing, paste, IME commit —
    /// multi-line pastes are joined), and submit releases focus unless <see cref="KeepFocusOnSubmit"/>.
    /// Without it the editor is a document — Enter inserts a newline. Pair with the UniText's
    /// Word Wrap turned off for the classic horizontally-scrolling field.
    /// </summary>
    [Serializable]
    [TypeDescription("Single-line: Enter submits, newlines are stripped (form field)")]
    [TypeGroup("Field", 4)]
    public partial class SingleLineBehavior : InputBehavior
    {
        /// <summary>Whether focus survives submission.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Keep focus after submit. Off (default) releases focus — the form-field convention; " +
                 "chat composers use SubmitKeyBehavior, which always keeps focus.")]
        private bool keepFocusOnSubmit;

        protected override void OnEnable()
        {
            editable.SuppressNewlines();
            editable.Submitted += OnSubmit;
        }

        protected override void OnDisable()
        {
            editable.Submitted -= OnSubmit;
            editable.ReleaseNewlineSuppression();
        }

        private void OnSubmit(string text)
        {
            if (!keepFocusOnSubmit) editable.RequestDefocusAfterSubmit();
        }
    }
}
