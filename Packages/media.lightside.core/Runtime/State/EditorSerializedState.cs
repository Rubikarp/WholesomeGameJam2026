#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace LightSide
{
    /// <summary>Generated snapshot contract consumed by one Unity-host Editor transaction.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IEditorSerializedStateOwner
    {
        /// <summary>Whether the generated Editor baseline has been established.</summary>
        bool EditorStateInitialized { get; }
        /// <summary>Synchronization boundary for deserialize-thread snapshot capture.</summary>
        object EditorStateSyncRoot { get; }
        /// <summary>Captures current serialized fields into the pending Editor snapshot.</summary>
        void CaptureEditorSerializedState();
        /// <summary>Accepts one validated candidate as the initial baseline.</summary>
        void InitializeEditorSerializedState(object candidate);
        /// <summary>Freezes the pending snapshot into storage owned by one host transaction.</summary>
        object CreateEditorStateCandidate();
        /// <summary>Validates pending transitions without changing authored or derived state.</summary>
        void ValidateEditorSerializedState(object candidate);
        /// <summary>Stages candidate fields and schedules their consequences without invoking them.</summary>
        void StageEditorSerializedState(object candidate, ref EditorStateMutationContext context);
        /// <summary>Accepts the staged candidate and returns whether a newer capture is pending.</summary>
        bool AcceptEditorSerializedState(object candidate);
        /// <summary>Restores the applied baseline and preserves any newer pending capture.</summary>
        void RejectEditorSerializedState(object candidate);
        /// <summary>Invokes or compensates one generated field callback token.</summary>
        void InvokeEditorStateCallback(object token, object candidate, bool rollback);
    }

    /// <summary>
    /// Extends a generated state owner with scalar value, string, or Unity-object properties owned
    /// by a base or external type. Callbacks run on the main thread after the current value is
    /// installed, and run again after rollback with the restored value. The route count, order,
    /// paths, and callback identities must remain fixed for the lifetime of the host object.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IEditorSerializedPropertyStateOwner : IEditorSerializedStateOwner
    {
        /// <summary>Number of external serialized properties participating in the transaction.</summary>
        int EditorSerializedPropertyCount { get; }
        /// <summary>Returns one Unity serialized path and its deduplicated callback identity.</summary>
        void GetEditorSerializedProperty(int index, out string path, out int callback);
        /// <summary>Applies the runtime consequence of the property's current serialized value.</summary>
        void InvokeEditorSerializedPropertyChanged(int callback);
    }

    /// <summary>Deduplicates serialized-state callbacks within one Editor reconciliation transaction.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public struct EditorStateMutationContext : IDisposable
    {
        private Callback[] callbacks;
        private int count;
        private int attempted;

        /// <summary>Schedules one owner callback once in first-occurrence order.</summary>
        public void Schedule(IEditorSerializedStateOwner owner, object token, object candidate)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (token == null) throw new ArgumentNullException(nameof(token));
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            for (var i = 0; i < count; i++)
            {
                var callback = callbacks[i];
                if (ReferenceEquals(callback.Owner, owner) && ReferenceEquals(callback.Token, token)) return;
            }
            EnsureCapacity(count + 1);
            callbacks[count++] = new Callback(owner, token, candidate);
        }

        /// <summary>Schedules one external serialized-property callback once.</summary>
        public void Schedule(IEditorSerializedPropertyStateOwner owner, int callback)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (callback < 0) throw new ArgumentOutOfRangeException(nameof(callback));
            for (var i = 0; i < count; i++)
            {
                var pending = callbacks[i];
                if (ReferenceEquals(pending.PropertyOwner, owner) &&
                    pending.PropertyCallback == callback) return;
            }
            EnsureCapacity(count + 1);
            callbacks[count++] = new Callback(owner, callback);
        }

        /// <summary>Invokes every scheduled callback in host graph order.</summary>
        public void Invoke()
        {
            for (var i = 0; i < count; i++)
            {
                var callback = callbacks[i];
                attempted = i + 1;
                callback.Invoke(false);
            }
        }

        /// <summary>Compensates attempted callbacks in reverse order after the authored baseline is restored.</summary>
        public void Rollback()
        {
            List<Exception> failures = null;
            for (var i = attempted - 1; i >= 0; i--)
            {
                var callback = callbacks[i];
                try
                {
                    callback.Invoke(true);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }
            if (failures != null)
                throw new AggregateException("One or more serialized-state rollback callbacks failed.", failures);
        }

        /// <summary>Returns callback storage to its pool.</summary>
        public void Dispose()
        {
            count = 0;
            attempted = 0;
            if (callbacks == null) return;
            global::System.Buffers.ArrayPool<Callback>.Shared.Return(callbacks, true);
            callbacks = null;
        }

        private void EnsureCapacity(int capacity)
        {
            if (callbacks != null && callbacks.Length >= capacity) return;
            var replacement = global::System.Buffers.ArrayPool<Callback>.Shared.Rent(
                Math.Max(capacity, 8));
            if (callbacks != null)
            {
                Array.Copy(callbacks, replacement, count);
                global::System.Buffers.ArrayPool<Callback>.Shared.Return(callbacks, true);
            }
            callbacks = replacement;
        }

        private readonly struct Callback
        {
            internal Callback(IEditorSerializedStateOwner owner, object token, object candidate)
            {
                Owner = owner;
                PropertyOwner = null;
                Token = token;
                Candidate = candidate;
                PropertyCallback = -1;
            }

            internal Callback(IEditorSerializedPropertyStateOwner owner, int callback)
            {
                Owner = null;
                PropertyOwner = owner;
                Token = null;
                Candidate = null;
                PropertyCallback = callback;
            }

            internal IEditorSerializedStateOwner Owner { get; }
            internal IEditorSerializedPropertyStateOwner PropertyOwner { get; }
            internal object Token { get; }
            internal object Candidate { get; }
            internal int PropertyCallback { get; }

            internal void Invoke(bool rollback)
            {
                if (Owner != null)
                {
                    Owner.InvokeEditorStateCallback(Token, Candidate, rollback);
                    return;
                }
                PropertyOwner.InvokeEditorSerializedPropertyChanged(PropertyCallback);
            }
        }
    }

    /// <summary>Forwards generated deserialize callbacks into the Editor-owned queue.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateEditorReceptor
    {
        private static readonly object gate = new();
        private static Action<IEditorSerializedStateOwner> captured;
        private static HashSet<IEditorSerializedStateOwner> pending;

        internal static void Bind(Action<IEditorSerializedStateOwner> receptor)
        {
            if (receptor == null) throw new ArgumentNullException(nameof(receptor));
            IEditorSerializedStateOwner[] buffered;
            lock (gate)
            {
                captured = receptor;
                if (pending == null || pending.Count == 0) return;
                buffered = new IEditorSerializedStateOwner[pending.Count];
                pending.CopyTo(buffered);
                pending = null;
            }
            for (var i = 0; i < buffered.Length; i++) receptor(buffered[i]);
        }

        /// <summary>Publishes one generated serialized-state capture to the Editor driver.</summary>
        public static void Capture(IEditorSerializedStateOwner owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            lock (owner.EditorStateSyncRoot) owner.CaptureEditorSerializedState();
            Action<IEditorSerializedStateOwner> receptor;
            lock (gate)
            {
                receptor = captured;
                if (receptor == null)
                {
                    pending ??= new HashSet<IEditorSerializedStateOwner>(
                        ReferenceIdentityComparer<IEditorSerializedStateOwner>.Instance);
                    pending.Add(owner);
                    return;
                }
            }
            receptor(owner);
        }

    }

}
#endif
