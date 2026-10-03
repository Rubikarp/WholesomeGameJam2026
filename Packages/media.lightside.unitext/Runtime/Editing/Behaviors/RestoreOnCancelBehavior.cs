using System;

namespace LightSide
{
    /// <summary>Restores the focused-session text and releases input focus when editing is cancelled.</summary>
    [Serializable]
    [TypeDescription("Restore the focused-session text and release focus when editing is cancelled")]
    [TypeGroup("Field", 4)]
    public sealed class RestoreOnCancelBehavior : InputBehavior
    {
        [NonSerialized] private string originalText;

        protected override void OnEnable()
        {
            editable.Focused += OnFocused;
            editable.Cancelled += OnCancelled;
            if (editable.IsActive) OnFocused();
        }

        protected override void OnDisable()
        {
            editable.Focused -= OnFocused;
            editable.Cancelled -= OnCancelled;
            originalText = null;
        }

        private void OnFocused() => originalText = editable.Text;

        private void OnCancelled()
        {
            editable.SetTextProgrammatic(originalText, reason: TextChangeReason.Restore);
            editable.Defocus();
        }
    }
}
