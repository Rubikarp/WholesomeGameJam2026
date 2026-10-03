using UnityEngine;

namespace LightSide
{
    /// <summary>Applies a <see cref="Playback"/>'s decisions: weight onto outputs, and their release.</summary>
    public interface IPlaybackHost
    {
        /// <summary>Applies the current envelope weight to the host's outputs.</summary>
        void ApplyWeight(float weight);

        /// <summary>Releases the host's outputs after the envelope completed a releasing run.</summary>
        void ReleaseOutputs();
    }

    /// <summary>
    /// Allocation-free weight envelope of one animated application: instant and eased transitions,
    /// pulses, deferred release, and clock routing. Pure state machine — the host interprets every
    /// decision through <see cref="IPlaybackHost"/>, so the same envelope serves any output kind.
    /// </summary>
    public struct Playback
    {
        private float weight;
        private float transitionFrom;
        private float transitionTo;
        private float transitionDuration;
        private float transitionElapsed;
        private Ease transitionEasing;
        private PlaybackClock transitionClock;
        private bool releaseOnComplete;
        private bool transitioning;
        private bool pulseEntering;
        private float pulseExitDuration;
        private bool releaseNextTick;

        /// <summary>Current envelope weight, 0..1.</summary>
        public float Weight => weight;

        /// <summary>Whether a timed transition is in flight.</summary>
        public bool Transitioning => transitioning;

        /// <summary>Clock the in-flight work advances on.</summary>
        public PlaybackClock Clock => transitionClock;

        /// <summary>Whether the envelope has pending work needing ticks on any clock.</summary>
        public bool NeedsTick => releaseNextTick || transitioning;

        /// <summary>Whether pending work advances on a frame clock (scaled or unscaled).</summary>
        public bool UsesFrameClock => NeedsTick && transitionClock != PlaybackClock.Manual;

        /// <summary>Whether pending work advances only through manual ticks.</summary>
        public bool UsesManualClock => NeedsTick && transitionClock == PlaybackClock.Manual;

        /// <summary>Sets the weight immediately, cancelling nothing.</summary>
        public void SetWeight<THost>(THost host, float value) where THost : IPlaybackHost
        {
            weight = Mathf.Clamp01(value);
            host.ApplyWeight(weight);
        }

        /// <summary>
        /// Starts a timed transition toward <paramref name="target"/>; zero duration applies it
        /// immediately. A releasing run releases the host's outputs on completing at zero weight.
        /// </summary>
        public void TransitionTo<THost>(THost host, float target, float duration,
            in Ease easing, PlaybackClock clock, bool release) where THost : IPlaybackHost
        {
            transitionFrom = weight;
            transitionTo = Mathf.Clamp01(target);
            transitionDuration = Mathf.Max(0f, duration);
            transitionElapsed = 0f;
            transitionEasing = easing;
            transitionClock = clock;
            releaseOnComplete = release;
            pulseEntering = false;
            if (transitionDuration <= 0f)
            {
                SetWeight(host, transitionTo);
                if (releaseOnComplete && transitionTo <= 0f) host.ReleaseOutputs();
                transitioning = false;
                return;
            }
            transitioning = true;
        }

        /// <summary>Runs the enter half of a pulse; the exit half starts when the enter completes.</summary>
        public void TriggerPulse<THost>(THost host, float enterDuration, float exitDuration,
            in Ease easing, PlaybackClock clock) where THost : IPlaybackHost
        {
            if (enterDuration <= 0f && exitDuration <= 0f)
            {
                transitionClock = clock;
                SetWeight(host, 1f);
                releaseNextTick = true;
                return;
            }
            pulseEntering = true;
            pulseExitDuration = Mathf.Max(0f, exitDuration);
            transitionFrom = weight;
            transitionTo = 1f;
            transitionDuration = Mathf.Max(0f, enterDuration);
            transitionElapsed = 0f;
            transitionEasing = easing;
            transitionClock = clock;
            releaseOnComplete = false;
            if (transitionDuration <= 0f)
            {
                SetWeight(host, 1f);
                StartPulseExit(host);
                return;
            }
            transitioning = true;
        }

        /// <summary>Applies full weight for exactly one tick of the current clock, then releases.</summary>
        public void HoldOneFrame<THost>(THost host) where THost : IPlaybackHost
        {
            SetWeight(host, 1f);
            releaseNextTick = true;
        }

        /// <summary>Snaps in-flight work to its end state, honoring a reduced-motion preference.</summary>
        public void CompleteForReducedMotion<THost>(THost host) where THost : IPlaybackHost
        {
            if (!transitioning) return;
            transitioning = false;
            if (pulseEntering)
            {
                pulseEntering = false;
                transitionClock = PlaybackClock.Unscaled;
                SetWeight(host, 1f);
                releaseNextTick = true;
                return;
            }
            SetWeight(host, transitionTo);
            if (releaseOnComplete && transitionTo <= 0f) host.ReleaseOutputs();
        }

        /// <summary>
        /// Advances pending work by <paramref name="deltaTime"/> on <paramref name="clock"/>.
        /// Returns whether the envelope still needs ticks.
        /// </summary>
        public bool Tick<THost>(THost host, float deltaTime, PlaybackClock clock)
            where THost : IPlaybackHost
        {
            if (releaseNextTick && transitionClock == clock)
            {
                releaseNextTick = false;
                SetWeight(host, 0f);
                host.ReleaseOutputs();
            }
            if (!transitioning || transitionClock != clock) return NeedsTick;
            transitionElapsed += Mathf.Max(0f, deltaTime);
            var t = transitionDuration <= 0f
                ? 1f
                : Mathf.Clamp01(transitionElapsed / transitionDuration);
            SetWeight(host, Mathf.LerpUnclamped(transitionFrom, transitionTo,
                transitionEasing.Evaluate(t)));
            if (t < 1f) return true;

            transitioning = false;
            SetWeight(host, transitionTo);
            if (pulseEntering)
            {
                pulseEntering = false;
                StartPulseExit(host);
            }
            else if (releaseOnComplete && transitionTo <= 0f)
            {
                host.ReleaseOutputs();
            }
            return NeedsTick;
        }

        /// <summary>Drops all pending work and returns the weight to zero without applying it.</summary>
        public void Reset()
        {
            weight = 0f;
            transitioning = false;
            releaseNextTick = false;
            pulseEntering = false;
        }

        private void StartPulseExit<THost>(THost host) where THost : IPlaybackHost
            => TransitionTo(host, 0f, pulseExitDuration, transitionEasing, transitionClock, true);
    }
}
