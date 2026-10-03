using System;

namespace LightSide
{
    /// <summary>
    /// Base for input behaviors that react to the software keyboard's lifecycle — appearing, hiding,
    /// animating, or changing frame — while the field is focused. Subclass to build a reaction: keep
    /// the field above the keyboard (<see cref="KeyboardAvoidanceBehavior"/>), dock a toolbar to the
    /// keyboard top, resize a scroll container, dim a backdrop, and so on. Desktop emits no keyboard
    /// events, so every subclass is inert there.
    /// </summary>
    /// <remarks>
    /// The base owns the wiring: it routes <see cref="UniTextNativeInput.KeyboardChanged"/> into the
    /// per-phase hooks below while the owning editor is focused, and drives <see cref="OnKeyboardUpdate"/>
    /// once per frame. The per-frame tick outlives focus by the tail of a client-side exit animation:
    /// while <see cref="IsAnimating"/> is set after <see cref="OnFocusLost"/>, ticks keep coming so the
    /// reaction can animate back to rest instead of snapping. Subclasses override the hooks they need and
    /// add their own serialized fields. Animation has two paths by platform (see
    /// <see cref="OnKeyboardAnimationProgress"/> vs <see cref="OnKeyboardUpdate"/>); a cross-platform
    /// subclass implements both.
    /// </remarks>
    [Serializable]
    public abstract class KeyboardBehavior : InputBehavior
    {
        private bool routing;
        private bool ticking;
        private bool tearingDown;

        /// <summary>Whether the reaction is mid client-side animation and still needs
        /// <see cref="OnKeyboardUpdate"/> ticks — including an exit animation started in
        /// <see cref="OnFocusLost"/> that must finish after focus is already gone. The base keeps the
        /// per-frame tick alive while this holds, then releases it, so the field animates to rest rather
        /// than snapping.</summary>
        protected virtual bool IsAnimating => false;

        /// <summary>True only while the behavior is being disabled/destroyed. An exit reaction must settle
        /// to rest immediately here — no further tick can run to animate it.</summary>
        protected bool IsTearingDown => tearingDown;

        protected override void OnEnable()
        {
            editable.Focused += HandleFocusGained;
            editable.Defocused += HandleEditingEnded;
            if (editable.IsActive) HandleFocusGained();
        }

        protected override void OnDisable()
        {
            tearingDown = true;
            editable.Focused -= HandleFocusGained;
            editable.Defocused -= HandleEditingEnded;
            HandleFocusLost();
            StopTicking();
            tearingDown = false;
        }

        private void HandleFocusGained()
        {
            if (routing) return;
            routing = true;
            UniTextNativeInput.KeyboardChanged += OnKeyboardEvent;
            if (!ticking)
            {
                ticking = true;
                editable.FrameTicked += Tick;
            }
            OnFocusGained();
            if (UniTextNativeInput.IsKeyboardVisible)
                OnKeyboardWillChangeFrame(new KeyboardEvent
                {
                    phase = KeyboardEventPhase.WillChangeFrame,
                    area = UniTextNativeInput.KeyboardArea,
                });
        }

        private void HandleEditingEnded(EditingEndReason _) => HandleFocusLost();

        private void HandleFocusLost()
        {
            if (!routing) return;
            routing = false;
            UniTextNativeInput.KeyboardChanged -= OnKeyboardEvent;
            OnFocusLost();
            if (!IsAnimating) StopTicking();
        }

        private void Tick(float deltaTime)
        {
            OnKeyboardUpdate(deltaTime);
            if (!routing && !IsAnimating) StopTicking();
        }

        private void StopTicking()
        {
            if (!ticking) return;
            ticking = false;
            editable.FrameTicked -= Tick;
        }

        private void OnKeyboardEvent(KeyboardEvent e)
        {
            switch (e.phase)
            {
                case KeyboardEventPhase.WillShow:          OnKeyboardWillShow(e); break;
                case KeyboardEventPhase.AnimationProgress: OnKeyboardAnimationProgress(e); break;
                case KeyboardEventPhase.DidShow:           OnKeyboardDidShow(e); break;
                case KeyboardEventPhase.WillHide:          OnKeyboardWillHide(e); break;
                case KeyboardEventPhase.DidHide:           OnKeyboardDidHide(e); break;
                case KeyboardEventPhase.WillChangeFrame:   OnKeyboardWillChangeFrame(e); break;
            }
        }

        /// <summary>The owning field gained focus. Capture any baseline state to restore on focus loss.
        /// If the keyboard is already up (switching fields under an open keyboard) a synthetic
        /// <see cref="OnKeyboardWillChangeFrame"/> follows immediately so the reaction can position itself.</summary>
        protected virtual void OnFocusGained() { }

        /// <summary>The owning field lost focus. Restore any baseline state captured in <see cref="OnFocusGained"/>.</summary>
        protected virtual void OnFocusLost() { }

        /// <summary>System is about to show the keyboard; <paramref name="e"/> carries the destination rect
        /// plus animation duration/easing. Start any client-driven animation here.</summary>
        protected virtual void OnKeyboardWillShow(KeyboardEvent e) { }

        /// <summary>Per-frame system animation fraction, only on platforms reporting frame-synced animation
        /// (<see cref="KeyboardEvent.hasFrameSyncedAnimation"/> = Android API 30+). Elsewhere this never
        /// fires — animate from <see cref="OnKeyboardUpdate"/> instead.</summary>
        protected virtual void OnKeyboardAnimationProgress(KeyboardEvent e) { }

        /// <summary>The show animation finished and the keyboard is fully visible.</summary>
        protected virtual void OnKeyboardDidShow(KeyboardEvent e) { }

        /// <summary>System is about to hide the keyboard. Start any client-driven hide animation here.</summary>
        protected virtual void OnKeyboardWillHide(KeyboardEvent e) { }

        /// <summary>The hide animation finished and the keyboard is fully gone.</summary>
        protected virtual void OnKeyboardDidHide(KeyboardEvent e) { }

        /// <summary>Keyboard frame changed without a hide/show cycle — iPad split-keyboard toggle, hardware
        /// keyboard connect/disconnect, rotation, language switch, or a field switch under an open keyboard.</summary>
        protected virtual void OnKeyboardWillChangeFrame(KeyboardEvent e) { }

        /// <summary>Per-frame tick (unscaled delta) while focused, plus the tail of a client-side exit
        /// animation after focus loss (as long as <see cref="IsAnimating"/> holds). Drive client-side
        /// animation here on platforms without frame-synced progress (iOS, Android &lt;30, WebGL).</summary>
        protected virtual void OnKeyboardUpdate(float deltaTime) { }
    }
}
