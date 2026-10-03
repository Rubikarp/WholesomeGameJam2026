using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Lifecycle phase of a keyboard event delivered by the native layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WillShow"/>, <see cref="WillHide"/> and <see cref="WillChangeFrame"/> arrive
    /// <em>before</em> the system animation starts and carry the destination rect plus animation
    /// metadata (<see cref="KeyboardEvent.animationDuration"/>, <see cref="KeyboardEvent.easing"/>).
    /// Use these to schedule an animation that runs in lockstep with the system.
    /// </para>
    /// <para>
    /// <see cref="AnimationProgress"/> is delivered every frame while the system animates the
    /// keyboard, with <see cref="KeyboardEvent.animationFraction"/> ramping from 0 to 1. Only
    /// available on platforms where <see cref="KeyboardEvent.hasFrameSyncedAnimation"/> is true
    /// (Android API 30+). On platforms without per-frame progress, no <see cref="AnimationProgress"/>
    /// events are delivered — drive the animation manually using duration+easing from the Will-phase.
    /// </para>
    /// <para>
    /// <see cref="DidShow"/> and <see cref="DidHide"/> arrive once the system animation has fully
    /// completed and the keyboard is in its final state.
    /// </para>
    /// </remarks>
    public enum KeyboardEventPhase
    {
        /// <summary>System is about to show the keyboard. Animation has not started yet.</summary>
        WillShow,

        /// <summary>System is animating keyboard show/hide/resize. Per-frame; only on platforms with frame-synced support.</summary>
        AnimationProgress,

        /// <summary>System has finished showing the keyboard.</summary>
        DidShow,

        /// <summary>System is about to hide the keyboard. Animation has not started yet.</summary>
        WillHide,

        /// <summary>System has finished hiding the keyboard.</summary>
        DidHide,

        /// <summary>
        /// Keyboard frame is changing without a hide/show cycle. Examples: split keyboard toggle on
        /// iPad, hardware keyboard connect/disconnect, device rotation, language switch.
        /// </summary>
        WillChangeFrame,
    }

    /// <summary>
    /// Standard easing modes that map to the system keyboard animation curves.
    /// </summary>
    /// <remarks>
    /// On iOS this corresponds to <c>UIViewAnimationCurve</c>. On Android API 30+ the system
    /// interpolator is approximated to the closest standard easing. On platforms without curve
    /// information (Android &lt;30, WebGL) the value is <see cref="EaseInOut"/>.
    /// </remarks>
    public enum KeyboardEasing
    {
        /// <summary>No easing — constant velocity.</summary>
        Linear,
        /// <summary>Slow start, fast end (accelerate).</summary>
        EaseIn,
        /// <summary>Fast start, slow end (decelerate). Typical for keyboard show on iOS.</summary>
        EaseOut,
        /// <summary>Slow start and end, fast middle. Typical Android default.</summary>
        EaseInOut,
    }

    /// <summary>
    /// Snapshot of a single keyboard lifecycle event delivered by the native platform layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Coordinates use Unity screen-space conventions: pixels, origin at bottom-left, Y increases
    /// upward. <see cref="area"/> represents the keyboard rect in those coordinates; for a hidden
    /// keyboard <see cref="area"/> is <see cref="Rect.zero"/>. In an embedded WebGL player the rect
    /// remains relative to the canvas and can extend beyond its bounds when the browser keyboard
    /// overlaps only part of the page.
    /// </para>
    /// </remarks>
    public struct KeyboardEvent
    {
        /// <summary>The lifecycle phase that triggered this event.</summary>
        public KeyboardEventPhase phase;

        /// <summary>
        /// Keyboard rect in Unity screen pixels (Y-up, origin bottom-left). For the
        /// <see cref="KeyboardEventPhase.WillShow"/> / <see cref="KeyboardEventPhase.WillChangeFrame"/>
        /// phases this is the destination rect (where the keyboard will be after the animation).
        /// </summary>
        public Rect area;

        /// <summary>
        /// System animation duration in seconds. Zero when the system did not provide animation
        /// metadata (Android &lt;30, WebGL). Apply a sensible default (≈0.25s) when zero.
        /// </summary>
        public float animationDuration;

        /// <summary>
        /// Closest standard easing matching the system curve. Falls back to
        /// <see cref="KeyboardEasing.EaseInOut"/> when the platform does not expose curve data.
        /// </summary>
        public KeyboardEasing easing;

        /// <summary>
        /// Animation progress in the range 0..1. Only meaningful in
        /// <see cref="KeyboardEventPhase.AnimationProgress"/> events. The value is the system's
        /// interpolated fraction — it already incorporates <see cref="easing"/>.
        /// </summary>
        public float animationFraction;

        /// <summary>
        /// True on platforms that emit per-frame <see cref="KeyboardEventPhase.AnimationProgress"/>
        /// events (Android API 30+). False on platforms where the keyboard animation must be
        /// driven client-side from <see cref="animationDuration"/> + <see cref="easing"/>.
        /// </summary>
        public bool hasFrameSyncedAnimation;

        /// <summary>True when the phase represents the keyboard becoming or being visible.</summary>
        public bool IsShowingPhase =>
            phase == KeyboardEventPhase.WillShow ||
            phase == KeyboardEventPhase.DidShow;

        /// <summary>True when the phase represents the keyboard becoming or being hidden.</summary>
        public bool IsHidingPhase =>
            phase == KeyboardEventPhase.WillHide ||
            phase == KeyboardEventPhase.DidHide;
    }
}
