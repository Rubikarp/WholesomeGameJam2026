using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Shared motion vocabulary of the editor UI: one duration, one easing.</summary>
    public static class InspectorMotion
    {
        /// <summary>Duration of every editor motion, in milliseconds.</summary>
        public const int DurationMs = 120;

        /// <summary>The easing every editor motion uses.</summary>
        public static readonly Ease Curve = Ease.Of(EasingType.QuadraticOut);

        /// <summary>
        /// The most animation time one tick may consume. A stalled frame advances the motion by
        /// this much instead of its real duration, so the unroll resumes where it paused rather
        /// than jumping to the end.
        /// </summary>
        private const int MaxStepMs = 34;

        /// <summary>
        /// Establishes the clip at first contact — normally the body's construction — and stays
        /// for its life: the renderer resolves a clip context reliably only when the element is
        /// born with it, never when it appears or toggles mid-life.
        /// </summary>
        private const string ClipClass = "lightside-motion-clip";

        /// <summary>
        /// Eased 0→1 progress over <see cref="DurationMs"/>, robust to stalled frames. The first
        /// value applies inside <see cref="Play"/>, in the caller's own frame.
        /// </summary>
        public sealed class Ticker
        {
            private IVisualElementScheduledItem item;
            private Action<float> apply;
            private float progress;
            private long lastMs;

            public void Play(VisualElement host, Action<float> apply)
            {
                if (host == null) throw new ArgumentNullException(nameof(host));
                if (apply == null) throw new ArgumentNullException(nameof(apply));
                Stop();
                this.apply = apply;
                progress = 0f;
                lastMs = -1;
                item = host.schedule.Execute(state => Step(state.now)).Every(0);
                apply(0f);
            }

            public void Stop()
            {
                item?.Pause();
                item = null;
                apply = null;
            }

            private void Step(long nowMs)
            {
                var delta = lastMs < 0 ? 0f : Mathf.Min(nowMs - lastMs, MaxStepMs);
                lastMs = nowMs;
                progress = Mathf.Min(1f, progress + delta / DurationMs);
                var applied = apply;
                applied(Curve.Evaluate(progress));
                if (progress < 1f || apply != applied) return;
                Stop();
            }
        }

        private sealed class Motion
        {
            public IVisualElementScheduledItem ticker;
            public bool expanding;
            public float start;
            public float target;
            public float progress;
            public long lastMs = -1;
            public Action completed;
            public StyleLength heldMinHeight;
            public readonly List<(VisualElement child, StyleFloat shrink)> held = new();
        }

        private static readonly Dictionary<VisualElement, Motion> motions = new();
        private static MethodInfo validateLayout;

        //TODO: temporary unroll-offset diagnostics — remove after the list expand snap is resolved.
        internal static void Trace(string message)
            => System.IO.File.AppendAllText("Temp/motion-trace.log",
                $"{UnityEditor.EditorApplication.timeSinceStartup:F4} {message}\n");

        /// <summary>
        /// Shows or hides a disclosure body, unrolling its height when <paramref name="animate"/>
        /// is set and the body is attached. Repeating the state a running motion is already heading
        /// for changes nothing — an instant re-assertion never cuts an unroll short. Toggling
        /// mid-motion continues from the current height. Direct children keep their natural height
        /// for the whole motion and are revealed by the body's clip, never compressed by it.
        /// <paramref name="onCompleted"/> runs once the state is fully applied — immediately on an
        /// instant switch, never for a motion that is interrupted or repeats the current state.
        /// </summary>
        public static void SetExpanded(VisualElement body, bool expanded, bool animate = true,
            Action onCompleted = null)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            body.AddToClassList(ClipClass);
            var hasMotion = motions.TryGetValue(body, out var running);
            var current = hasMotion
                ? running.expanding
                : body.style.display.value != DisplayStyle.None;
            if (current == expanded) return;

            if (!animate || body.panel == null)
            {
                StopMotion(body);
                body.style.height = StyleKeyword.Null;
                body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
                onCompleted?.Invoke();
                return;
            }

            var start = hasMotion || !expanded ? body.layout.height : 0f;
            if (float.IsNaN(start)) start = 0f;
            StopMotion(body);

            var motion = new Motion
            {
                expanding = expanded,
                start = start,
                completed = onCompleted,
            };
            foreach (var child in body.Children())
            {
                motion.held.Add((child, child.style.flexShrink));
                child.style.flexShrink = 0f;
            }
            body.style.display = DisplayStyle.Flex;
            if (expanded)
            {
                body.style.height = StyleKeyword.Null;
                CompleteLayout(body.panel);
                motion.target = body.layout.height;
            }
            motion.heldMinHeight = body.style.minHeight;
            body.style.minHeight = 0f;
            body.style.height = start;
            body.RegisterCallback<DetachFromPanelEvent>(OnBodyDetached);
            Trace($"motion {(expanded ? "expand" : "collapse")} start={start:F1} " +
                  $"target={motion.target:F1} worldY={body.worldBound.y:F1} " +
                  $"classes={string.Join(" ", body.GetClasses())}");

            motion.ticker = body.schedule
                .Execute(state => Step(body, motion, state.now))
                .Every(0);
            motions[body] = motion;
        }

        private static void Step(VisualElement body, Motion motion, long nowMs)
        {
            if (!motions.TryGetValue(body, out var current) || current != motion) return;
            var delta = motion.lastMs < 0
                ? 0f
                : Mathf.Min(nowMs - motion.lastMs, MaxStepMs);
            motion.lastMs = nowMs;
            motion.progress = Mathf.Min(1f, motion.progress + delta / DurationMs);
            body.style.height = Curve.Lerp(motion.start, motion.target, motion.progress);
            if (motion.progress < 1f) return;

            StopMotion(body);
            body.style.height = StyleKeyword.Null;
            if (!motion.expanding) body.style.display = DisplayStyle.None;
            Trace($"done target={motion.target:F1}");
            body.schedule.Execute(() => Trace(
                $"settled auto={body.layout.height:F1} worldY={body.worldBound.y:F1}"));
            motion.completed?.Invoke();
        }

        private static void OnBodyDetached(DetachFromPanelEvent evt)
            => StopMotion((VisualElement)evt.target);

        private static void StopMotion(VisualElement body)
        {
            if (!motions.Remove(body, out var motion)) return;
            motion.ticker?.Pause();
            body.style.minHeight = motion.heldMinHeight;
            foreach (var (child, shrink) in motion.held)
                child.style.flexShrink = shrink;
            motion.held.Clear();
        }

        /// <summary>Runs the panel's style and layout passes immediately, without painting.</summary>
        internal static void CompleteLayout(IPanel panel)
        {
            if (panel == null) return;
            validateLayout ??= panel.GetType().GetMethod("ValidateLayout",
                    BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMethodException(panel.GetType().FullName, "ValidateLayout");
            validateLayout.Invoke(panel, null);
        }

        /// <summary>
        /// Height the children occupy, including the trailing margin that <c>layout</c> excludes
        /// and any overflow past the container's own clamped height. Measured from the children: a
        /// container stretched or clipped by its parent reports neither slack nor overflow.
        /// </summary>
        internal static float ContentExtent(VisualElement container)
        {
            var extent = float.NaN;
            foreach (var child in container.Children())
            {
                var bottom = child.layout.yMax + child.resolvedStyle.marginBottom;
                if (float.IsNaN(bottom)) continue;
                if (float.IsNaN(extent) || bottom > extent) extent = bottom;
            }
            if (float.IsNaN(extent)) extent = container.layout.height;
            if (float.IsNaN(extent)) return 0f;
            return extent + container.resolvedStyle.paddingBottom +
                   container.resolvedStyle.borderBottomWidth;
        }
    }
}
