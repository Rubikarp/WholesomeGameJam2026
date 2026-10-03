using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Legacy playhead repeat modes.</summary>
    [Obsolete("Use MotionCycle and MotionCycles.Infinite.")]
    public enum DriverLoop : byte
    {
        Once = 0,
        Loop = 1,
        PingPong = 2,
    }

    /// <summary>
    /// Sequences parameter animation over the sibling text component: each clip owns one modifier
    /// parameter across the ranges its query matches and ramps every member between two endpoint
    /// values inside its own window on the shared timeline. Clips run in order, overlap freely,
    /// and may drive different parameters of different modifiers.
    /// </summary>
    /// <remarks>
    /// The playhead advances on the chosen clock and is fully scrubbable: set
    /// <see cref="Progress"/> from code, an Animator, or a Timeline to render any exact state.
    /// Ownership follows each clip's query across text edits and releases on disable, returning
    /// every range to its cascade. Scrubbing and playback work in edit mode; automatic playback
    /// on enable starts only in play mode.
    /// </remarks>
    [ExecuteAlways]
    [RequireComponent(typeof(UniTextBase))]
    [AddComponentMenu(UniTextMenu.AddComponent.Driver)]
    public sealed partial class UniTextDriver : MonoBehaviour
    {
        [SerializeField, StateList(nameof(OnClipsChanged))]
        [Tooltip("Clips of the sequence, each driving one parameter over its own window.")]
        private List<UniTextDriverClip> clips = new();

        [SerializeField, StatePassive, Tooltip("Time source the playhead advances on; Manual advances only through code or animation.")]
        private PlaybackClock clock = PlaybackClock.Scaled;

        [SerializeField, StatePassive] private float speed = 1f;

        [SerializeField, StatePassive, Tooltip("How many times the timeline plays; -1 repeats until stopped.")]
        private int cycles = 1;

        [SerializeField, StatePassive, Tooltip("What each pass after the first does.")]
        private MotionCycle cycleMode = MotionCycle.Restart;

        [SerializeField, Min(0f), StateProperty(nameof(OnDurationChanged))]
        [Tooltip("Fixed timeline length in seconds; clips still extend it. 0 derives it from the clips alone.")]
        private float duration;

        [SerializeField, StatePassive, Tooltip("Starts playing when the component enables.")]
        private bool playOnEnable = true;

        [SerializeField, HideInInspector, Min(1), StatePassive] private int timelineTracks = 1;

        /// <summary>Authored timeline lane count; clips may reference lanes beyond it.</summary>
        internal int TimelineTracks => timelineTracks;

        [SerializeField, Range(0f, 1f), StateField(nameof(OnProgressChanged))]
        [Tooltip("Normalized playhead over the timeline; scrub it from an Animator or Timeline.")]
        private float progress;

        [NonSerialized] private UniTextBase text;
        [NonSerialized] private bool bound;
        [NonSerialized] private readonly List<ClipDrive> drives = new();
        [NonSerialized] private ReferenceBinding<object> observedClipState;
        [NonSerialized] private List<object> observedScratch;
        [NonSerialized] private StateChangedHandler clipStateChanged;
        [NonSerialized] private Action clipConfigurationChanged;
        [NonSerialized] private Action driveChangedCallback;
        [NonSerialized] private Action styleGraphChangedCallback;
        [NonSerialized] private float phase;
        [NonSerialized] private float evaluated;
        [NonSerialized] private bool playing;
        [NonSerialized] private bool pendingEnablePlay;
        [NonSerialized] private bool publishing;

        private readonly struct ClipDrive
        {
            internal readonly UniTextDriverClip clip;
            internal readonly IParameterDrive drive;

            internal ClipDrive(UniTextDriverClip clip, IParameterDrive drive)
            {
                this.clip = clip;
                this.drive = drive;
            }
        }

        /// <summary>Playhead speed multiplier; negative values run backwards.</summary>
        public float Speed { get => speed; set => speed = value; }

        /// <summary>Whether the playhead is advancing.</summary>
        public bool IsPlaying => playing;

        /// <summary>
        /// Position on the timeline in seconds: where the sequence evaluates, so a
        /// <see cref="MotionCycle.PingPong"/> return reads as the mirrored position and never as a
        /// phase past the end. Setting it seeks the forward pass and renders that state;
        /// <see cref="Progress"/> is the same position normalized.
        /// </summary>
        public float Playhead
        {
            get => evaluated;
            set
            {
                phase = Mathf.Max(0f, value);
                Apply();
            }
        }

        /// <summary>
        /// Normalized playhead over the timeline, 0..1. Setting it renders that exact state —
        /// scrubbing, rewinding and external timelines all work.
        /// </summary>
        public float Progress
        {
            get => progress;
            set => Seek(value);
        }

        /// <summary>
        /// Seconds the full timeline lasts: the latest clip end for the current members, or the
        /// authored fixed length when that is longer.
        /// </summary>
        public float TimelineLength
        {
            get
            {
                var length = duration;
                for (var i = 0; i < clips.Count; i++)
                {
                    var clip = clips[i];
                    if (clip == null) continue;
                    var end = clip.Start + clip.Length(MemberCount(clip));
                    if (end > length) length = end;
                }
                return length;
            }
        }

        private int MemberCount(UniTextDriverClip clip)
        {
            for (var i = 0; i < drives.Count; i++)
                if (ReferenceEquals(drives[i].clip, clip) && drives[i].drive.IsAlive)
                    return drives[i].drive.Count;
            return 0;
        }

        /// <summary>Seconds one clip's window lasts for its current members.</summary>
        internal float ClipLength(UniTextDriverClip clip)
            => clip == null ? 0f : clip.Length(MemberCount(clip));

        /// <summary>Descriptor a clip's binding resolves to against the current styles, or null.</summary>
        internal ParameterDescriptor FindBoundParameter(UniTextDriverClip clip)
        {
            if (clip == null || text == null || !clip.TargetNode.IsValid ||
                string.IsNullOrEmpty(clip.ParameterId)) return null;
            var modifier = ResolveTarget(clip.TargetNode);
            return modifier == null ? null : ParameterDescriptor.Find(modifier, clip.ParameterId);
        }

        /// <summary>Normalized 0..1 position of the playhead inside one clip's window.</summary>
        internal float ClipProgress(UniTextDriverClip clip)
        {
            if (clip == null) return 0f;
            var time = Mathf.Clamp01(progress) * TimelineLength;
            var length = clip.Length(MemberCount(clip));
            if (length <= 0f) return time >= clip.Start ? 1f : 0f;
            return Mathf.Clamp01((time - clip.Start) / length);
        }

        /// <summary>
        /// Starts or resumes playback; a finished finite run restarts at its beginning for positive speed or
        /// its end for negative speed.
        /// </summary>
        public void Play()
        {
            if (!bound)
            {
                Debug.LogError("[UniTextDriver] No clip is bound; pick a parameter first.", this);
                return;
            }
            if (!IsInfinite)
            {
                var run = RunLength(TimelineLength);
                if (ReachedTerminal(run)) phase = speed < 0f ? run : 0f;
            }
            playing = true;
            RefreshTick();
            Apply();
        }

        /// <summary>Pauses playback, keeping the current state.</summary>
        public void Pause()
        {
            playing = false;
            RefreshTick();
        }

        /// <summary>Stops playback and returns the playhead to the beginning.</summary>
        public void Stop()
        {
            playing = false;
            RefreshTick();
            phase = 0f;
            Apply();
        }

        /// <summary>Moves the playhead to a normalized 0..1 position and renders that state.</summary>
        public void Seek(float normalized)
        {
            phase = Mathf.Clamp01(normalized) * TimelineLength;
            Apply();
        }

        /// <summary>Advances the playhead by explicit seconds — the tick source for the Manual clock.</summary>
        public void Advance(float deltaTime)
        {
            phase += deltaTime * speed;
            Apply();
        }

        /// <summary>
        /// Releases and rebuilds every clip's ownership against the current styles. Before the
        /// text component has initialized its style graph this is a deferred no-op; the graph's
        /// build signal re-enters it.
        /// </summary>
        public void Rebind()
        {
            Release();
            if (text == null || text.AttributeParser == null) return;
            driveChangedCallback ??= Apply;
            for (var i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clip == null || clip.Disabled || !clip.TargetNode.IsValid ||
                    string.IsNullOrEmpty(clip.ParameterId)) continue;

                var modifier = ResolveTarget(clip.TargetNode);
                if (modifier == null) continue;
                var parameter = ParameterDescriptor.Find(modifier, clip.ParameterId);
                if (parameter == null)
                {
                    Debug.LogError($"[UniTextDriver] {modifier.GetType().Name} declares no " +
                                   $"parameter '{clip.ParameterId}'.", this);
                    continue;
                }
                if (clip.From == null || clip.To == null ||
                    clip.From.ValueType != parameter.ValueType ||
                    clip.To.ValueType != parameter.ValueType)
                {
                    Debug.LogError($"[UniTextDriver] Endpoint values for '{clip.ParameterId}' " +
                                   $"must both be of type {parameter.ValueType.Name}.", this);
                    continue;
                }

                var query = clip.Query.Build(modifier, parameter);
                var drive = parameter.CreateDrive(in query, clip.From.BoxedValue,
                    clip.To.BoxedValue, clip.Composition, clip.Priority);
                drive.Changed += driveChangedCallback;
                drives.Add(new ClipDrive(clip, drive));
                bound = true;
            }
            RefreshTick();
        }

        private void OnEnable()
        {
            text = GetComponent<UniTextBase>();
            styleGraphChangedCallback ??= OnStyleGraphChanged;
            text.StyleGraphChanged += styleGraphChangedCallback;
            ObserveClips();
            Rebind();
            if (playOnEnable && Application.isPlaying)
            {
                if (bound) Play();
                else pendingEnablePlay = true;
            }
            else
            {
                Apply();
            }
        }

        private void OnStyleGraphChanged()
        {
            Rebind();
            if (pendingEnablePlay && bound)
            {
                pendingEnablePlay = false;
                Play();
            }
            else
            {
                Apply();
            }
        }

        private void OnDisable()
        {
            if (text != null) text.StyleGraphChanged -= styleGraphChangedCallback;
            observedClipState?.Clear();
            playing = false;
            pendingEnablePlay = false;
            Release();
#if UNITY_EDITOR
            CoreLoop.RequestEditorFrame();
#endif
        }

        private void Release()
        {
            bound = false;
            RefreshTick();
            for (var i = 0; i < drives.Count; i++)
            {
                var drive = drives[i].drive;
                if (driveChangedCallback != null) drive.Changed -= driveChangedCallback;
                drive.Release();
            }
            drives.Clear();
        }

        private void OnClipsChanged()
        {
            if (!isActiveAndEnabled || text == null) return;
            ObserveClips();
            Rebind();
            Apply();
        }

        private void ObserveClips()
        {
            observedClipState ??= new ReferenceBinding<object>(Observe, Unobserve);
            observedScratch ??= new List<object>();
            observedScratch.Clear();
            for (var i = 0; i < clips.Count; i++)
            {
                var clip = clips[i];
                if (clip == null) continue;
                observedScratch.Add(clip);
                if (clip.Query != null) observedScratch.Add(clip.Query);
                if (clip.From != null) observedScratch.Add(clip.From);
                if (clip.To != null) observedScratch.Add(clip.To);
            }
            observedClipState.Reconcile(observedScratch);
            observedScratch.Clear();
        }

        private void Observe(object source)
        {
            if (source is IStateChangeSource state)
            {
                clipStateChanged ??= OnClipStateChanged;
                state.Changed += clipStateChanged;
            }
            if (source is IRangeConfigurationSource configuration)
            {
                clipConfigurationChanged ??= OnClipsChanged;
                configuration.AddConfigurationListener(clipConfigurationChanged);
            }
        }

        private void Unobserve(object source)
        {
            if (source is IStateChangeSource state && clipStateChanged != null)
                state.Changed -= clipStateChanged;
            if (source is IRangeConfigurationSource configuration && clipConfigurationChanged != null)
                configuration.RemoveConfigurationListener(clipConfigurationChanged);
        }

        private void OnClipStateChanged(IStateChangeSource source, in StateChange change)
            => OnClipsChanged();

        private void OnProgressChanged()
        {
            if (publishing) return;
            if (!isActiveAndEnabled || text == null) return;
            Seek(progress);
        }

        /// <summary>
        /// Publishes the position the driver resolved. A write to <see cref="Progress"/> from
        /// outside seeks, so the driver's own write must not re-enter that path: the phase it
        /// advances carries the ping-pong leg, which a normalized round trip cannot.
        /// </summary>
        private void Publish(float normalized)
        {
            publishing = true;
            try { SetProgressState(normalized); }
            finally { publishing = false; }
        }

        private void OnDurationChanged()
        {
            if (!isActiveAndEnabled || text == null) return;
            Apply();
        }

        [NonSerialized] private Action tickCallback;
        [NonSerialized] private TickHandle tickHandle;

        private void RefreshTick() =>
            CoreLoop.Updating.Toggle(ref tickHandle, tickCallback ??= OnTick,
                isActiveAndEnabled && playing && bound);

        private void OnTick()
        {
            if (clock == PlaybackClock.Manual) return;
            Advance(PlaybackTime.Delta(clock));
        }

        private void OnDidApplyAnimationProperties()
        {
            phase = Mathf.Clamp01(progress) * TimelineLength;
            Apply();
        }

        private BaseModifier ResolveTarget(ModifierNodeId node)
        {
            var e = text.EnumerateLiveStyles();
            while (e.MoveNext())
                if (e.Current.Modifier is { } root &&
                    BaseModifier.FindGraphNode(root, node) is { } match)
                    return match;
            return null;
        }

        /// <summary>Whether the timeline repeats until stopped.</summary>
        private bool IsInfinite => cycles < 0;

        /// <summary>Seconds a whole run occupies, or infinity when it repeats until stopped.</summary>
        private float RunLength(float length) =>
            IsInfinite ? float.PositiveInfinity : length * Mathf.Max(1, cycles);

        private bool ReachedTerminal(float run) => speed < 0f ? phase <= 0f : phase >= run;

        /// <summary>
        /// Folds the phase into the period its repeat covers — one length, two for a ping-pong, whose return
        /// leg lives in the second half — so it stays finite however long playback runs, and resolves the
        /// timeline position that phase evaluates to.
        /// </summary>
        /// <remarks>
        /// A playhead carries no easing of its own, so <see cref="MotionCycle.Yoyo"/> is the same function as
        /// <see cref="MotionCycle.PingPong"/> here, and <see cref="MotionCycle.Incremental"/> has nothing to
        /// accumulate — both resolve to the two shapes a timeline can actually take.
        /// </remarks>
        private float Fold(float length)
        {
            if (length <= 0f) return 0f;

            var mirrors = cycleMode is MotionCycle.PingPong or MotionCycle.Yoyo;
            var period = mirrors ? length * 2f : length;
            var run = RunLength(length);

            if (!IsInfinite)
            {
                phase = Mathf.Clamp(phase, 0f, run);
                if (phase >= run) return mirrors && (Mathf.Max(1, cycles) & 1) == 0 ? 0f : length;
            }
            else phase = Mathf.Repeat(phase, period);

            return mirrors ? Mathf.PingPong(phase, length) : Mathf.Repeat(phase, length);
        }

        private void Apply()
        {
            var length = TimelineLength;
            var time = Fold(length);
            evaluated = time;
            if (!bound) return;
            Publish(length <= 0f ? 1f : time / length);

            for (var c = 0; c < drives.Count; c++)
            {
                if (drives[c].drive is not { IsAlive: true } drive) continue;
                var clip = drives[c].clip;
                if (!Engaged(c, time))
                {
                    drive.Withhold();
                    continue;
                }
                var local = time - clip.Start;
                var count = drive.Count;
                var easing = clip.Easing;
                var stagger = clip.Stagger;
                var memberDuration = clip.MemberDuration;
                for (var i = 0; i < count; i++)
                {
                    var member = memberDuration <= 0f
                        ? (local >= stagger * i ? 1f : 0f)
                        : Mathf.Clamp01((local - stagger * i) / memberDuration);
                    drive.Apply(i, easing.Evaluate(member));
                }
            }

            if (playing && !IsInfinite && ReachedTerminal(RunLength(length))) playing = false;
        }

        /// <summary>
        /// Sequencer arbitration among Replace clips of one parameter: the latest clip whose start
        /// the playhead has passed drives, so an earlier clip's boundary hold yields the moment the
        /// next clip's window begins; before every start the earliest clip holds its From. Add,
        /// Multiply and Custom clips compose freely and always stay engaged.
        /// </summary>
        private bool Engaged(int index, float time)
        {
            var clip = drives[index].clip;
            if (clip.Composition != ParameterComposition.Replace) return true;
            var bestStart = float.MinValue;
            var best = -1;
            var earliestStart = float.MaxValue;
            var earliest = -1;
            for (var i = 0; i < drives.Count; i++)
            {
                if (drives[i].drive is not { IsAlive: true }) continue;
                var other = drives[i].clip;
                if (other.Composition != ParameterComposition.Replace ||
                    other.Priority != clip.Priority ||
                    other.TargetNode != clip.TargetNode ||
                    other.ParameterId != clip.ParameterId) continue;
                if (other.Start <= time && other.Start >= bestStart)
                {
                    bestStart = other.Start;
                    best = i;
                }
                if (other.Start < earliestStart)
                {
                    earliestStart = other.Start;
                    earliest = i;
                }
            }
            return (best >= 0 ? best : earliest) == index;
        }
    }
}
