using System;

namespace LightSide
{
    /// <summary>Releases input focus when editing is cancelled by Escape, system back, or EventSystem cancel.</summary>
    [Serializable]
    [TypeDescription("Release focus when editing is cancelled")]
    [TypeGroup("Field", 4)]
    public sealed class DefocusOnCancelBehavior : InputBehavior
    {
        protected override void OnEnable() => editable.Cancelled += OnCancelled;

        protected override void OnDisable() => editable.Cancelled -= OnCancelled;

        private void OnCancelled() => editable.Defocus();
    }
}
