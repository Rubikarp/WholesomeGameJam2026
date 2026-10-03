using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>A point in the shared frame loop where ordered work can run.</summary>
    public enum CoreLoopPhase : byte
    {
        /// <summary>At the start of Unity <c>PreUpdate</c>, before maintenance and script <c>Update</c>.</summary>
        BeforeUpdate = 0,

        /// <summary>Once per logical frame after <see cref="BeforeUpdate"/>.</summary>
        Maintenance = 1,

        /// <summary>Immediately before Unity script <c>Update</c> callbacks.</summary>
        Update = 2,

        /// <summary>Once per rendered frame before Built-in or SRP culling.</summary>
        PreRender = 3,

        /// <summary>Whenever Unity prepares canvases for rendering.</summary>
        CanvasPreRender = 4,

        /// <summary>During active edit-mode updates while at least one listener is registered.</summary>
        EditorUpdate = 5,

        /// <summary>Immediately before Unity script <c>FixedUpdate</c> callbacks.</summary>
        FixedUpdate = 6,

        /// <summary>Immediately before Unity script <c>LateUpdate</c> callbacks.</summary>
        LateUpdate = 7,

        /// <summary>Immediately after Unity script <c>Update</c> callbacks.</summary>
        AfterUpdate = 8,

        /// <summary>At the start of each Unity <c>FixedUpdate</c> phase.</summary>
        BeforeFixedUpdate = 9,
    }

    /// <summary>
    /// Stable identity of one ordered <see cref="CoreLoop"/> registration. Disposing a stale copy is an
    /// idempotent no-op and never removes a later registration.
    /// </summary>
    public readonly struct CoreLoopHandle : IDisposable, IEquatable<CoreLoopHandle>
    {
        internal readonly CoreLoopPhase phase;
        internal readonly long id;

        internal CoreLoopHandle(CoreLoopPhase phase, long id)
        {
            this.phase = phase;
            this.id = id;
        }

        /// <summary>Whether this is the default handle, which the loop never issues.</summary>
        public bool IsDefault => id == 0;

        /// <summary>Removes the represented registration if it is still live.</summary>
        public void Dispose() => CoreLoop.Unregister(this);

        /// <inheritdoc/>
        public bool Equals(CoreLoopHandle other) => phase == other.phase && id == other.id;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is CoreLoopHandle other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine((byte)phase, id);

        /// <summary>Whether two handles represent the same registration.</summary>
        public static bool operator ==(CoreLoopHandle left, CoreLoopHandle right) => left.Equals(right);

        /// <summary>Whether two handles represent different registrations.</summary>
        public static bool operator !=(CoreLoopHandle left, CoreLoopHandle right) => !left.Equals(right);
    }

    /// <summary>Shared frame and rendering phases for runtime systems and editor previews.</summary>
    public static class CoreLoop
    {
        private struct BeforeFixedLoopMarker { }
        private struct FixedLoopMarker { }
        private struct BeforeUpdateLoopMarker { }
        private struct UpdateLoopMarker { }
        private struct AfterUpdateLoopMarker { }
        private struct LateLoopMarker { }

        private enum CallbackKind : byte
        {
            Plain,
            Frame,
            Clock,
        }

        private sealed class Registration
        {
            public long id;
            public long sequence;
            public int order;
            public bool live;
            public CallbackKind kind;
            public Delegate callback;
            public PlaybackTimeSource clock;
        }

        private sealed class Registry
        {
            public readonly List<Registration> entries = new();
            public int invoking;
            public bool dirty;
            public bool needsSort;
        }

        private static readonly Registry[] registries = CreateRegistries();
        private static readonly Comparison<Registration> registrationOrder = CompareRegistrations;
        private static long nextRegistrationId;
        private static long nextSequence;

        /// <summary>Occurs at the start of Unity PreUpdate, before maintenance and script Update callbacks.</summary>
        public static event Action BeforeUpdating
        {
            add => RegisterLegacy(CoreLoopPhase.BeforeUpdate, value, CallbackKind.Plain);
            remove => UnregisterLegacy(CoreLoopPhase.BeforeUpdate, value, CallbackKind.Plain);
        }

        /// <summary>Occurs at the start of each Unity FixedUpdate phase.</summary>
        public static event Action BeforeFixedUpdating
        {
            add => RegisterLegacy(CoreLoopPhase.BeforeFixedUpdate, value, CallbackKind.Plain);
            remove => UnregisterLegacy(CoreLoopPhase.BeforeFixedUpdate, value, CallbackKind.Plain);
        }

        /// <summary>Occurs immediately before each Unity script FixedUpdate pass.</summary>
        public static event Action FixedUpdating
        {
            add => RegisterLegacy(CoreLoopPhase.FixedUpdate, value, CallbackKind.Plain);
            remove => UnregisterLegacy(CoreLoopPhase.FixedUpdate, value, CallbackKind.Plain);
        }

        /// <summary>Occurs once per runtime or editor-preview frame with its monotonic frame number.</summary>
        public static event Action<int> Maintaining
        {
            add => RegisterLegacy(CoreLoopPhase.Maintenance, value, CallbackKind.Frame);
            remove => UnregisterLegacy(CoreLoopPhase.Maintenance, value, CallbackKind.Frame);
        }

        /// <summary>
        /// Ticks once per frame immediately before script Update callbacks — every player-loop
        /// frame, and in edit mode every editor tick while it has subscribers. An edit-mode
        /// subscriber keeps the editor pumping frames until removed.
        /// </summary>
        public static readonly Phase Updating = new();

        /// <summary>Occurs once per runtime frame immediately after script Update callbacks.</summary>
        public static event Action AfterUpdating
        {
            add => RegisterLegacy(CoreLoopPhase.AfterUpdate, value, CallbackKind.Plain);
            remove => UnregisterLegacy(CoreLoopPhase.AfterUpdate, value, CallbackKind.Plain);
        }

        /// <summary>Occurs once per runtime frame immediately before script LateUpdate callbacks.</summary>
        public static event Action LateUpdating
        {
            add => RegisterLegacy(CoreLoopPhase.LateUpdate, value, CallbackKind.Plain);
            remove => UnregisterLegacy(CoreLoopPhase.LateUpdate, value, CallbackKind.Plain);
        }

        /// <summary>Ticks once per rendered frame before Built-in or SRP culling.</summary>
        public static readonly Phase PreRendering = new();

        /// <summary>Ticks whenever Unity prepares canvases for rendering.</summary>
        public static readonly Phase CanvasPreRendering = new();

#if UNITY_EDITOR
        /// <summary>Occurs during active edit-mode updates while at least one listener is registered.</summary>
        public static event Action EditorUpdating
        {
            add => RegisterLegacy(CoreLoopPhase.EditorUpdate, value, CallbackKind.Plain);
            remove => UnregisterLegacy(CoreLoopPhase.EditorUpdate, value, CallbackKind.Plain);
        }
#endif

        /// <summary>The current runtime or editor-preview frame duration in seconds.</summary>
        public static float DeltaTime { get; private set; }

        /// <summary>
        /// The current frame duration in seconds unaffected by <see cref="Time.timeScale"/>;
        /// in edit mode equals <see cref="DeltaTime"/>.
        /// </summary>
        public static float UnscaledDeltaTime { get; private set; }
        internal static int Frame { get; private set; }
        internal static ulong SampleEpoch { get; private set; }

        /// <summary>
        /// Registers <paramref name="callback"/> at <paramref name="phase"/>. Lower orders run first; equal
        /// orders preserve registration order. A callback added during the phase begins on its next invocation.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> is not defined.</exception>
        public static CoreLoopHandle Register(CoreLoopPhase phase, Action callback, int order = 0)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return Add(phase, callback, CallbackKind.Plain, default, order);
        }

        /// <summary>
        /// Registers frame maintenance. Lower orders run first; equal orders preserve registration order.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
        public static CoreLoopHandle RegisterMaintenance(Action<int> callback, int order = 0)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return Add(CoreLoopPhase.Maintenance, callback, CallbackKind.Frame, default, order);
        }

        /// <summary>
        /// Registers clocked work at <paramref name="phase"/>. The callback receives that clock's elapsed step;
        /// <see cref="PlaybackClock.Manual"/> registrations never run automatically.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> or <paramref name="clock"/> is not defined.</exception>
        public static CoreLoopHandle Register(CoreLoopPhase phase, PlaybackClock clock, Action<float> callback,
            int order = 0) => Register(phase, PlaybackTime.Source(clock), callback, order);

        /// <summary>
        /// Registers clocked work at <paramref name="phase"/>. The callback receives that source's elapsed step;
        /// <see cref="PlaybackTimeSource.Manual"/> registrations never run automatically.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="callback"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="phase"/> or <paramref name="source"/> is not defined.</exception>
        public static CoreLoopHandle Register(CoreLoopPhase phase, PlaybackTimeSource source,
            Action<float> callback, int order = 0)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            PlaybackTime.Validate(source);
            return Add(phase, callback, CallbackKind.Clock, source, order);
        }

        /// <summary>
        /// Removes <paramref name="handle"/>. Removal during dispatch takes effect before the next callback is
        /// selected, including when a callback removes itself or one that has not run yet.
        /// </summary>
        /// <returns><see langword="true"/> when a live registration was removed.</returns>
        public static bool Unregister(in CoreLoopHandle handle)
        {
            if (handle.IsDefault || !IsDefined(handle.phase)) return false;
            var registry = registries[(int)handle.phase];
            var entries = registry.entries;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (!entry.live || entry.id != handle.id) continue;
                entry.live = false;
                RemoveOrDefer(registry, i);
                return true;
            }
            return false;
        }

        private static int renderedFrame = -1;
#if UNITY_EDITOR
        private static int editorTick;
        private static int renderedEditorTick = -1;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitializeRuntime()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            Remove(ref loop);
            if (!Prepend(ref loop, typeof(UnityEngine.PlayerLoop.PreUpdate),
                    typeof(BeforeUpdateLoopMarker), TickBeforeRuntime))
                throw new InvalidOperationException("Unity's player loop does not contain PreUpdate.");
            if (!Prepend(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate),
                    typeof(BeforeFixedLoopMarker), TickBeforeFixed))
                throw new InvalidOperationException("Unity's player loop does not contain FixedUpdate.");
            if (!Insert(ref loop, typeof(UnityEngine.PlayerLoop.FixedUpdate),
                    typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunBehaviourFixedUpdate),
                    typeof(FixedLoopMarker), TickFixed))
                throw new InvalidOperationException("Unity's player loop does not contain ScriptRunBehaviourFixedUpdate.");
            if (!Insert(ref loop, typeof(UnityEngine.PlayerLoop.Update),
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                    typeof(UpdateLoopMarker), TickRuntime))
                throw new InvalidOperationException("Unity's player loop does not contain ScriptRunBehaviourUpdate.");
            if (!InsertAfter(ref loop, typeof(UnityEngine.PlayerLoop.Update),
                    typeof(UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate),
                    typeof(AfterUpdateLoopMarker), TickAfterUpdate))
                throw new InvalidOperationException("Unity's player loop does not contain ScriptRunBehaviourUpdate.");
            if (!Insert(ref loop, typeof(UnityEngine.PlayerLoop.PreLateUpdate),
                    typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate),
                    typeof(LateLoopMarker), TickLate))
                throw new InvalidOperationException("Unity's player loop does not contain ScriptRunBehaviourLateUpdate.");
            PlayerLoop.SetPlayerLoop(loop);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeRendering() => BindRendering();

        private static void TickBeforeRuntime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            SampleEpoch = unchecked(SampleEpoch + 1);
            DeltaTime = Time.deltaTime;
            UnscaledDeltaTime = Time.unscaledDeltaTime;
            Invoke(CoreLoopPhase.BeforeUpdate);
        }

        private static void TickRuntime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            Maintain();
            Invoke(CoreLoopPhase.Update);
            Updating.Tick();
        }

        private static void TickAfterUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            DeltaTime = Time.deltaTime;
            UnscaledDeltaTime = Time.unscaledDeltaTime;
            Invoke(CoreLoopPhase.AfterUpdate);
        }

        private static void TickBeforeFixed()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            SampleEpoch = unchecked(SampleEpoch + 1);
            DeltaTime = Time.fixedDeltaTime;
            UnscaledDeltaTime = Time.fixedUnscaledDeltaTime;
            Invoke(CoreLoopPhase.BeforeFixedUpdate);
        }

        private static void TickFixed()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            Invoke(CoreLoopPhase.FixedUpdate);
        }

        private static void TickLate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            DeltaTime = Time.deltaTime;
            UnscaledDeltaTime = Time.unscaledDeltaTime;
            Invoke(CoreLoopPhase.LateUpdate);
        }

        private static void Maintain()
        {
            Frame = unchecked(Frame + 1);
            Invoke(CoreLoopPhase.Maintenance);
        }

        private static void BindRendering()
        {
            renderedFrame = -1;
#if UNITY_EDITOR
            renderedEditorTick = -1;
#endif
            Camera.onPreCull -= OnCameraPreCull;
            Camera.onPreCull += OnCameraPreCull;
            RenderPipelineManager.beginContextRendering -= OnContextRendering;
            RenderPipelineManager.beginContextRendering += OnContextRendering;
            Canvas.preWillRenderCanvases -= OnCanvasPreRendering;
            Canvas.preWillRenderCanvases += OnCanvasPreRendering;
        }

        private static void OnCameraPreCull(Camera _) => OnPreRendering();

        private static void OnContextRendering(ScriptableRenderContext _, List<Camera> __) =>
            OnPreRendering();

        private static void OnPreRendering()
        {
            if (!TryBeginRendering()) return;
            Invoke(CoreLoopPhase.PreRender);
            PreRendering.Tick();
        }

        private static bool TryBeginRendering()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying || EditorApplication.isPaused)
            {
                if (renderedEditorTick == editorTick) return false;
                renderedEditorTick = editorTick;
                return true;
            }
#endif
            var frame = Time.frameCount;
            if (renderedFrame == frame) return false;
            renderedFrame = frame;
            return true;
        }

        private static void OnCanvasPreRendering()
        {
            Invoke(CoreLoopPhase.CanvasPreRender);
            CanvasPreRendering.Tick();
        }

        private static bool Insert(ref PlayerLoopSystem system, Type phaseType, Type anchorType, Type markerType,
            PlayerLoopSystem.UpdateFunction callback)
        {
            if (system.type == phaseType)
            {
                var children = system.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var index = Array.FindIndex(children, child => child.type == anchorType);
                if (index < 0) return false;
                var inserted = new PlayerLoopSystem[children.Length + 1];
                Array.Copy(children, 0, inserted, 0, index);
                inserted[index] = new PlayerLoopSystem
                {
                    type = markerType,
                    updateDelegate = callback,
                };
                Array.Copy(children, index, inserted, index + 1, children.Length - index);
                system.subSystemList = inserted;
                return true;
            }

            var descendants = system.subSystemList;
            if (descendants == null) return false;
            for (var i = 0; i < descendants.Length; i++)
            {
                if (!Insert(ref descendants[i], phaseType, anchorType, markerType, callback)) continue;
                system.subSystemList = descendants;
                return true;
            }
            return false;
        }

        private static bool Prepend(ref PlayerLoopSystem system, Type phaseType, Type markerType,
            PlayerLoopSystem.UpdateFunction callback)
        {
            if (system.type == phaseType)
            {
                var children = system.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var inserted = new PlayerLoopSystem[children.Length + 1];
                inserted[0] = new PlayerLoopSystem
                {
                    type = markerType,
                    updateDelegate = callback,
                };
                Array.Copy(children, 0, inserted, 1, children.Length);
                system.subSystemList = inserted;
                return true;
            }

            var descendants = system.subSystemList;
            if (descendants == null) return false;
            for (var i = 0; i < descendants.Length; i++)
            {
                if (!Prepend(ref descendants[i], phaseType, markerType, callback)) continue;
                system.subSystemList = descendants;
                return true;
            }
            return false;
        }

        private static bool InsertAfter(ref PlayerLoopSystem system, Type phaseType, Type anchorType,
            Type markerType, PlayerLoopSystem.UpdateFunction callback)
        {
            if (system.type == phaseType)
            {
                var children = system.subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var anchor = Array.FindIndex(children, child => child.type == anchorType);
                if (anchor < 0) return false;
                var index = anchor + 1;
                var inserted = new PlayerLoopSystem[children.Length + 1];
                Array.Copy(children, 0, inserted, 0, index);
                inserted[index] = new PlayerLoopSystem
                {
                    type = markerType,
                    updateDelegate = callback,
                };
                Array.Copy(children, index, inserted, index + 1, children.Length - index);
                system.subSystemList = inserted;
                return true;
            }

            var descendants = system.subSystemList;
            if (descendants == null) return false;
            for (var i = 0; i < descendants.Length; i++)
            {
                if (!InsertAfter(ref descendants[i], phaseType, anchorType, markerType, callback)) continue;
                system.subSystemList = descendants;
                return true;
            }
            return false;
        }

        private static void Remove(ref PlayerLoopSystem system)
        {
            var children = system.subSystemList;
            if (children == null) return;
            var retained = 0;
            for (var i = 0; i < children.Length; i++)
            {
                Remove(ref children[i]);
                if (!IsLoopMarker(children[i].type)) retained++;
            }
            if (retained == children.Length)
            {
                system.subSystemList = children;
                return;
            }

            var filtered = new PlayerLoopSystem[retained];
            var destination = 0;
            for (var i = 0; i < children.Length; i++)
                if (!IsLoopMarker(children[i].type))
                    filtered[destination++] = children[i];
            system.subSystemList = filtered;
        }

        private static bool IsLoopMarker(Type type) =>
            type == typeof(CoreLoop) || type == typeof(BeforeFixedLoopMarker) ||
            type == typeof(FixedLoopMarker) || type == typeof(BeforeUpdateLoopMarker) ||
            type == typeof(UpdateLoopMarker) || type == typeof(AfterUpdateLoopMarker) ||
            type == typeof(LateLoopMarker);

#if UNITY_EDITOR
        /// <summary>
        /// Requests one edit-mode presentation so a state change made outside the frame loop
        /// reaches the screen without waiting for user input. Repainting editor views alone never
        /// re-runs canvas-driven pipelines — <see cref="Canvas.SendWillRenderCanvases"/> fires only
        /// from an actual canvas update — so on the next editor tick this pumps the canvases
        /// directly, then queues a player-loop tick and repaints every view to present the result.
        /// Requests coalesce; no-op while the player loop is already running (unpaused play mode).
        /// </summary>
        public static void RequestEditorFrame()
        {
            if (Application.isPlaying && !EditorApplication.isPaused) return;
            if (editorFramePending) return;
            editorFramePending = true;
            EditorApplication.delayCall += PresentEditorFrame;
        }

        private static bool editorFramePending;

        private static void PresentEditorFrame()
        {
            editorFramePending = false;
            if (Application.isPlaying && !EditorApplication.isPaused) return;
            if (EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) return;
            Canvas.ForceUpdateCanvases();
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        private static double lastEditorUpdateTime = EditorApplication.timeSinceStartup;

        [InitializeOnLoadMethod]
        private static void InitializeEditor()
        {
            BindRendering();
            lastEditorUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update -= TickEditor;
            EditorApplication.update += TickEditor;
        }

        private static void TickEditor()
        {
            editorTick = unchecked(editorTick + 1);
            var now = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            {
                lastEditorUpdateTime = now;
                return;
            }

            SampleEpoch = unchecked(SampleEpoch + 1);
            Maintain();
            if (!HasAutomatic(CoreLoopPhase.EditorUpdate) && Updating.Count == 0)
            {
                lastEditorUpdateTime = now;
                return;
            }

            DeltaTime = (float)(now - lastEditorUpdateTime);
            UnscaledDeltaTime = DeltaTime;
            lastEditorUpdateTime = now;
            Invoke(CoreLoopPhase.EditorUpdate);
            Updating.Tick();
            EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        private static CoreLoopHandle Add(CoreLoopPhase phase, Delegate callback, CallbackKind kind,
            PlaybackTimeSource clock, int order)
        {
            if (!IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
            var id = unchecked(++nextRegistrationId);
            if (id <= 0) throw new InvalidOperationException("The CoreLoop registration identity space is exhausted.");
            var sequence = unchecked(++nextSequence);
            if (sequence <= 0) throw new InvalidOperationException("The CoreLoop registration order space is exhausted.");

            var registry = registries[(int)phase];
            registry.entries.Add(new Registration
            {
                id = id,
                sequence = sequence,
                order = order,
                live = true,
                kind = kind,
                callback = callback,
                clock = clock,
            });

            if (registry.invoking == 0) registry.entries.Sort(registrationOrder);
            else registry.needsSort = true;
            return new CoreLoopHandle(phase, id);
        }

        private static void RegisterLegacy(CoreLoopPhase phase, Delegate callback, CallbackKind kind)
        {
            if (callback != null) Add(phase, callback, kind, default, 0);
        }

        private static void UnregisterLegacy(CoreLoopPhase phase, Delegate callback, CallbackKind kind)
        {
            if (callback == null) return;
            var registry = registries[(int)phase];
            var entries = registry.entries;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (!entry.live || entry.kind != kind || entry.callback != callback) continue;
                entry.live = false;
                RemoveOrDefer(registry, i);
                return;
            }
        }

        private static void RemoveOrDefer(Registry registry, int index)
        {
            if (registry.invoking != 0)
            {
                registry.dirty = true;
                return;
            }
            registry.entries.RemoveAt(index);
        }

        private static void Invoke(CoreLoopPhase phase)
        {
            var registry = registries[(int)phase];
            var entries = registry.entries;
            var count = entries.Count;
            registry.invoking++;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var entry = entries[i];
                    if (!entry.live) continue;
#if UNITY_EDITOR
                    if (phase == CoreLoopPhase.EditorUpdate)
                    {
                        try
                        {
                            Invoke(entry, phase);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogException(exception);
                        }
                        continue;
                    }
#endif
                    Invoke(entry, phase);
                }
            }
            finally
            {
                registry.invoking--;
                if (registry.invoking == 0 && registry.dirty)
                {
                    entries.RemoveAll(static entry => !entry.live);
                    registry.dirty = false;
                }
                if (registry.invoking == 0 && registry.needsSort)
                {
                    entries.Sort(registrationOrder);
                    registry.needsSort = false;
                }
            }
        }

        private static void Invoke(Registration entry, CoreLoopPhase phase)
        {
            switch (entry.kind)
            {
                case CallbackKind.Frame:
                    ((Action<int>)entry.callback)(Frame);
                    break;

                case CallbackKind.Clock:
                    if (entry.clock != PlaybackTimeSource.Manual)
                        ((Action<float>)entry.callback)(PlaybackTime.Delta(entry.clock,
                            phase == CoreLoopPhase.FixedUpdate || phase == CoreLoopPhase.BeforeFixedUpdate));
                    break;

                default:
                    ((Action)entry.callback)();
                    break;
            }
        }

        private static int CompareRegistrations(Registration left, Registration right)
        {
            var byOrder = left.order.CompareTo(right.order);
            return byOrder != 0 ? byOrder : left.sequence.CompareTo(right.sequence);
        }

        private static Registry[] CreateRegistries()
        {
            var values = (CoreLoopPhase[])Enum.GetValues(typeof(CoreLoopPhase));
            var result = new Registry[values.Length];
            for (var i = 0; i < result.Length; i++) result[i] = new Registry();
            return result;
        }

        private static bool HasAutomatic(CoreLoopPhase phase)
        {
            var entries = registries[(int)phase].entries;
            for (var i = 0; i < entries.Count; i++)
                if (entries[i].live &&
                    (entries[i].kind != CallbackKind.Clock || entries[i].clock != PlaybackTimeSource.Manual))
                    return true;
            return false;
        }

        private static bool IsDefined(CoreLoopPhase phase) => (uint)phase < (uint)registries.Length;
    }
}
