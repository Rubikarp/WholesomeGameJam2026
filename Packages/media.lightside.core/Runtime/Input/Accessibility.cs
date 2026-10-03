using System;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>Application-wide accessibility preferences supplied by the host integration.</summary>
    public static class Accessibility
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            prefersReducedMotion = false;
            prefersReducedMotionRevision = 0;
            applyingRevision = 0;
            deferredRevision = 0;
            changing = false;
            changingValue = false;
            dispatchingChanged = false;
            pendingChangedRevision = 0;
            prefersReducedMotionChanged = null;
            PrefersReducedMotionChanging = null;
            PrefersReducedMotionApplying = null;
        }

        private static bool prefersReducedMotion;
        private static bool changing;
        private static bool changingValue;
        private static bool dispatchingChanged;
        private static ulong prefersReducedMotionRevision;
        private static ulong applyingRevision;
        private static ulong deferredRevision;
        private static ulong pendingChangedRevision;
        private static Action prefersReducedMotionChanged;

        /// <summary>Whether nonessential motion is removed before its next presentation.</summary>
        public static bool PrefersReducedMotion
        {
            get => prefersReducedMotion;
            set
            {
                if (prefersReducedMotion == value || changing && changingValue == value) return;
                if (prefersReducedMotionRevision == ulong.MaxValue)
                    throw new InvalidOperationException("The reduced-motion preference revision space is exhausted.");

                changing = true;
                changingValue = value;
                try
                {
                    PrefersReducedMotionChanging?.Invoke();
                }
                finally
                {
                    changing = false;
                }

                prefersReducedMotion = value;
                var revision = unchecked(++prefersReducedMotionRevision);
                deferredRevision = 0;
                var previousApplying = applyingRevision;
                applyingRevision = revision;
                ExceptionDispatchInfo failure = null;
                try
                {
                    var applications = PrefersReducedMotionApplying?.GetInvocationList();
                    if (applications != null)
                        for (var i = 0; i < applications.Length; i++)
                            try
                            {
                                ((Action<ulong>)applications[i])(revision);
                            }
                            catch (Exception exception)
                            {
                                if (failure == null) failure = ExceptionDispatchInfo.Capture(exception);
                                else Debug.LogException(exception);
                            }
                }
                finally
                {
                    applyingRevision = previousApplying;
                }

                if (revision == prefersReducedMotionRevision && deferredRevision != revision)
                    DispatchReducedMotionChanged(revision, ref failure);
                failure?.Throw();
            }
        }

        /// <summary>
        /// Occurs after the latest reduced-motion preference has reached its consumers; reentrant changes may
        /// coalesce.
        /// </summary>
        public static event Action PrefersReducedMotionChanged
        {
            add => prefersReducedMotionChanged += value;
            remove => prefersReducedMotionChanged -= value;
        }

        internal static event Action PrefersReducedMotionChanging;
        internal static event Action<ulong> PrefersReducedMotionApplying;

        /// <summary>Defers public notification for the preference revision currently being applied.</summary>
        internal static void DeferReducedMotionChanged(ulong revision)
        {
            if (revision != applyingRevision || revision != prefersReducedMotionRevision)
                throw new InvalidOperationException("Only the current reduced-motion application can be deferred.");
            deferredRevision = revision;
        }

        /// <summary>Publishes a deferred preference revision if it is still current.</summary>
        internal static void CompleteReducedMotionChange(ulong revision)
        {
            if (revision != deferredRevision || revision != prefersReducedMotionRevision) return;
            deferredRevision = 0;
            if (revision == applyingRevision) return;
            ExceptionDispatchInfo failure = null;
            DispatchReducedMotionChanged(revision, ref failure);
            failure?.Throw();
        }

        internal static bool IsCurrentReducedMotionRevision(ulong revision) =>
            revision == prefersReducedMotionRevision;

        private static void DispatchReducedMotionChanged(ulong revision, ref ExceptionDispatchInfo failure)
        {
            if (dispatchingChanged)
            {
                pendingChangedRevision = revision;
                return;
            }

            dispatchingChanged = true;
            var visits = 0;
            try
            {
                while (revision != 0 && revision == prefersReducedMotionRevision)
                {
                    if (++visits > 32)
                    {
                        var exception = new InvalidOperationException(
                            "Reduced-motion listeners produced an endless reentrant change cycle.");
                        if (failure == null) failure = ExceptionDispatchInfo.Capture(exception);
                        else Debug.LogException(exception);
                        pendingChangedRevision = 0;
                        return;
                    }

                    pendingChangedRevision = 0;
                    var invocationList = prefersReducedMotionChanged?.GetInvocationList();
                    if (invocationList != null)
                        for (var i = 0; i < invocationList.Length && revision == prefersReducedMotionRevision; i++)
                            try
                            {
                                ((Action)invocationList[i])();
                            }
                            catch (Exception exception)
                            {
                                if (failure == null) failure = ExceptionDispatchInfo.Capture(exception);
                                else Debug.LogException(exception);
                            }
                    revision = pendingChangedRevision;
                }
            }
            finally
            {
                dispatchingChanged = false;
            }
        }
    }
}
