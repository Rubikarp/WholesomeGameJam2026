using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LightSide
{
    /// <summary>Receives the direct serialized nodes owned by one generated state container.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateOwnershipVisitor
    {
        /// <summary>Visits one direct owned node. Null references are ignored.</summary>
        void Visit(object node);
    }

    /// <summary>Exposes generated serialized ownership edges to the state runtime.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IStateOwnershipContainer
    {
        /// <summary>Enumerates direct owned nodes without traversing their descendants.</summary>
        void VisitOwnedState(IStateOwnershipVisitor visitor);
    }

    /// <summary>
     /// Validates and reconciles exclusive serialized ownership from declared graph edges.
     /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class StateOwnership
    {
        private static readonly ConditionalWeakTable<object, OwnershipSlot> owners = new();
        private static readonly object ownershipGate = new();
#if UNITY_EDITOR
        [ThreadStatic] private static Dictionary<object, object> claims;
        [ThreadStatic] private static HashSet<object> releases;
        [ThreadStatic] private static EditorTransactionPhase transactionPhase;
#endif

#if UNITY_EDITOR
        private static void Validate(object node, object requestedOwner)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (requestedOwner == null) throw new ArgumentNullException(nameof(requestedOwner));
            if (transactionPhase == EditorTransactionPhase.Rollback) return;
            if (transactionPhase == EditorTransactionPhase.Commit)
            {
                if (!claims.TryGetValue(node, out var finalOwner))
                {
                    var currentOwner = OwnerOf(node);
                    if (currentOwner != null && !ReferenceEquals(currentOwner, requestedOwner))
                        throw new InvalidOperationException(
                            $"'{node.GetType().FullName}' already belongs to " +
                            $"'{currentOwner.GetType().FullName}'.");
                    Claim(node, requestedOwner);
                    ValidateAcyclic(node, requestedOwner);
                    return;
                }
                if (ReferenceEquals(finalOwner, requestedOwner)) return;
                throw new InvalidOperationException(
                    $"Serialized node '{node.GetType().FullName}' was committed to a different owner.");
            }
            Claim(node, requestedOwner);
        }
#endif

        /// <summary>Validates the final owner of one scalar edge and records its release when replaced.</summary>
        public static void ValidateTransition<T>(T previous, T current, object owner)
            where T : class
        {
#if UNITY_EDITOR
            if (claims != null && transactionPhase == EditorTransactionPhase.Validation &&
                previous != null && !ReferenceEquals(previous, current))
                ClaimRelease(previous);
            if (claims != null)
            {
                if (current != null) Validate(current, owner);
                return;
            }
#endif
            if (ReferenceEquals(previous, current)) return;
            ValidateGraphTransition(CaptureGraph(previous, owner), CaptureGraph(current, owner));
        }

        /// <summary>Validates the final owners and reference uniqueness of one collection edge.</summary>
        public static void ValidateTransition<T>(IReadOnlyList<T> previous,
            IReadOnlyList<T> current, object owner, string fieldName)
            where T : class
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
#if UNITY_EDITOR
            if (claims != null && transactionPhase == EditorTransactionPhase.Validation && previous != null)
                for (var i = 0; i < previous.Count; i++)
                {
                    var node = previous[i];
                    if (node != null && !ReferenceIdentity.Contains(current, node)) ClaimRelease(node);
                }
#endif
            for (var i = 0; i < current.Count; i++)
            {
                var node = current[i];
                if (node == null) continue;
                for (var previousIndex = 0; previousIndex < i; previousIndex++)
                    if (ReferenceEquals(current[previousIndex], node))
                        throw new ArgumentException(
                            $"Owned state collection '{fieldName}' cannot contain the same instance more than once.");
#if UNITY_EDITOR
                if (claims != null)
                {
                    Validate(node, owner);
                    continue;
                }
#endif
            }
#if UNITY_EDITOR
            if (claims != null) return;
#endif
            ValidateGraphTransition(CaptureRemovedGraph(previous, current, owner),
                CaptureAddedGraph(previous, current, owner));
        }

        /// <summary>Applies one validated scalar ownership transition.</summary>
        public static void Reconcile(object previous, object current, object owner)
        {
            if (ReferenceEquals(previous, current)) return;
            ApplyGraphTransition(CaptureGraph(previous, owner), CaptureGraph(current, owner));
        }

        /// <summary>Validates and attaches the complete declared graph of one generated state container.</summary>
        public static void Initialize(IStateOwnershipContainer owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            ApplyGraphTransition(new OwnershipGraph(), CaptureOwnedGraph(owner), true);
        }

        /// <summary>Commits an ownership transition produced by a canonicalizing state callback.</summary>
        public static void ReconcileCanonicalized<T>(T previous, T current, object owner)
            where T : class
        {
#if UNITY_EDITOR
            if (claims != null && transactionPhase == EditorTransactionPhase.Commit &&
                !ReferenceEquals(previous, current))
            {
                if (previous != null)
                {
                    releases.Add(previous);
                    if (claims.TryGetValue(previous, out var claimedOwner) &&
                        ReferenceEquals(claimedOwner, owner)) claims[previous] = null;
                }
                if (current != null)
                    Claim(current, owner, true);
            }
#endif
            Reconcile(previous, current, owner);
        }

        /// <summary>Applies one validated collection ownership transition by reference identity.</summary>
        public static void Reconcile<T>(IReadOnlyList<T> previous,
            IReadOnlyList<T> current, object owner)
            where T : class
        {
            ApplyGraphTransition(CaptureRemovedGraph(previous, current, owner),
                CaptureAddedGraph(previous, current, owner));
        }

#if UNITY_EDITOR
        private static object OwnerOf(object node)
        {
            lock (ownershipGate) return OwnerOfCore(node);
        }
#endif

        private static object OwnerOfCore(object node)
            => node != null && owners.TryGetValue(node, out var slot) ? slot.Owner : null;

        private static OwnershipGraph CaptureGraph(object node, object owner)
        {
            var graph = new OwnershipGraph();
            graph.AddRoot(node, owner);
            return graph;
        }

        private static OwnershipGraph CaptureOwnedGraph(IStateOwnershipContainer owner)
        {
            var graph = new OwnershipGraph();
            graph.AddChildren(owner);
            return graph;
        }

        private static OwnershipGraph CaptureRemovedGraph<T>(IReadOnlyList<T> previous,
            IReadOnlyList<T> current, object owner) where T : class
        {
            var graph = new OwnershipGraph();
            if (previous == null) return graph;
            for (var i = 0; i < previous.Count; i++)
            {
                var node = previous[i];
                if (node != null && !ReferenceIdentity.Contains(current, node)) graph.AddRoot(node, owner);
            }
            return graph;
        }

        private static OwnershipGraph CaptureAddedGraph<T>(IReadOnlyList<T> previous,
            IReadOnlyList<T> current, object owner) where T : class
        {
            var graph = new OwnershipGraph();
            if (current == null) return graph;
            for (var i = 0; i < current.Count; i++)
            {
                var node = current[i];
                if (node != null && !ReferenceIdentity.Contains(previous, node)) graph.AddRoot(node, owner);
            }
            return graph;
        }

        private static void ValidateGraphTransition(OwnershipGraph removed, OwnershipGraph added)
        {
            lock (ownershipGate) ValidateGraphTransitionCore(removed, added, false);
        }

        private static void ApplyGraphTransition(OwnershipGraph removed, OwnershipGraph added,
            bool allowExistingRoots = false)
        {
            lock (ownershipGate)
            {
                ValidateGraphTransitionCore(removed, added, allowExistingRoots);
                for (var i = removed.Edges.Count - 1; i >= 0; i--)
                {
                    var edge = removed.Edges[i];
                    if (added.TryGetOwner(edge.Node, out var retainedOwner) &&
                        ReferenceEquals(retainedOwner, edge.Owner)) continue;
                    if (owners.TryGetValue(edge.Node, out var slot) &&
                        ReferenceEquals(slot.Owner, edge.Owner)) slot.Owner = null;
                }
                for (var i = 0; i < added.Edges.Count; i++)
                {
                    var edge = added.Edges[i];
                    owners.GetValue(edge.Node, CreateOwnershipSlot).Owner = edge.Owner;
                }
            }
        }

        private static OwnershipSlot CreateOwnershipSlot(object _) => new();

        private static void ValidateGraphTransitionCore(OwnershipGraph removed, OwnershipGraph added,
            bool allowExistingRoots)
        {
            for (var i = 0; i < added.Edges.Count; i++)
            {
                var edge = added.Edges[i];
#if UNITY_EDITOR
                if (claims != null && transactionPhase == EditorTransactionPhase.Commit &&
                    claims.TryGetValue(edge.Node, out var finalOwner) && finalOwner != null &&
                    !ReferenceEquals(finalOwner, edge.Owner))
                    throw new InvalidOperationException(
                        $"Serialized node '{edge.Node.GetType().FullName}' was committed to a different owner.");
#endif
                var currentOwner = OwnerOfCore(edge.Node);
                if (ReferenceEquals(currentOwner, edge.Owner) && edge.IsRoot &&
                    !allowExistingRoots && !CanRetainRoot(edge.Node, edge.Owner, removed))
                    throw new InvalidOperationException(
                        $"Serialized node '{edge.Node.GetType().FullName}' appears more than once in the owned graph of " +
                        $"'{edge.Owner.GetType().FullName}'.");
                if (currentOwner != null && !ReferenceEquals(currentOwner, edge.Owner) &&
                    !CanReplaceOwner(edge.Node, currentOwner, edge.Owner, removed))
                    throw new InvalidOperationException(
                        $"'{edge.Node.GetType().FullName}' already belongs to " +
                        $"'{currentOwner.GetType().FullName}'.");
                ValidateProspectiveCycle(edge.Node, edge.Owner, removed, added);
            }
        }

        private static bool CanRetainRoot(object node, object owner, OwnershipGraph removed)
        {
            if (removed.TryGetOwner(node, out var removedOwner) && ReferenceEquals(removedOwner, owner))
                return true;
#if UNITY_EDITOR
            if (claims != null)
            {
                if (transactionPhase == EditorTransactionPhase.Rollback) return true;
                if (transactionPhase == EditorTransactionPhase.Commit &&
                    claims.TryGetValue(node, out var finalOwner) && ReferenceEquals(finalOwner, owner)) return true;
            }
#endif
            return false;
        }

        private static bool CanReplaceOwner(object node, object currentOwner,
            object requestedOwner, OwnershipGraph removed)
        {
            if (removed.TryGetOwner(node, out var releasedOwner) &&
                ReferenceEquals(releasedOwner, currentOwner)) return true;
#if UNITY_EDITOR
            if (claims != null)
            {
                if (transactionPhase == EditorTransactionPhase.Rollback) return true;
                if (transactionPhase == EditorTransactionPhase.Commit &&
                    claims.TryGetValue(node, out var finalOwner) &&
                    ReferenceEquals(finalOwner, requestedOwner)) return true;
            }
#endif
            return false;
        }

        private static void ValidateProspectiveCycle(object node, object requestedOwner,
            OwnershipGraph removed, OwnershipGraph added)
        {
#if UNITY_EDITOR
            if (claims != null && transactionPhase == EditorTransactionPhase.Rollback) return;
#endif
            var current = requestedOwner;
            HashSet<object> visited = null;
            while (current != null)
            {
                if (ReferenceEquals(current, node))
                    throw new InvalidOperationException(
                        $"Owned serialized graph contains a cycle through '{node.GetType().FullName}'.");
                visited ??= new HashSet<object>(ReferenceIdentityComparer<object>.Instance);
                if (!visited.Add(current))
                    throw new InvalidOperationException(
                        $"Owned serialized graph contains a cycle through '{current.GetType().FullName}'.");
#if UNITY_EDITOR
                if (claims != null && transactionPhase == EditorTransactionPhase.Commit &&
                    claims.TryGetValue(current, out var finalOwner))
                {
                    current = finalOwner;
                    continue;
                }
#endif
                if (added.TryGetOwner(current, out var addedOwner))
                {
                    current = addedOwner;
                    continue;
                }
                var storedOwner = OwnerOfCore(current);
                if (removed.TryGetOwner(current, out var removedOwner) &&
                    ReferenceEquals(storedOwner, removedOwner)) current = null;
                else current = storedOwner;
            }
        }

#if UNITY_EDITOR
        internal static bool InEditorTransaction => claims != null;

        internal static void EnterEditorTransaction()
        {
            if (claims != null)
                throw new InvalidOperationException("Serialized-state transactions cannot be nested.");
            claims = new Dictionary<object, object>(ReferenceIdentityComparer<object>.Instance);
            releases = new HashSet<object>(ReferenceIdentityComparer<object>.Instance);
            transactionPhase = EditorTransactionPhase.Validation;
        }

        internal static void BeginEditorCommit()
        {
            if (claims == null || transactionPhase != EditorTransactionPhase.Validation)
                throw new InvalidOperationException("Serialized-state validation is not active.");
            CompleteEditorValidation();
            transactionPhase = EditorTransactionPhase.Commit;
        }

        internal static void BeginEditorRollback()
        {
            if (claims == null || transactionPhase == EditorTransactionPhase.Rollback)
                throw new InvalidOperationException("Serialized-state transaction is not active.");
            transactionPhase = EditorTransactionPhase.Rollback;
        }

        internal static void ExitEditorTransaction()
        {
            claims = null;
            releases = null;
            transactionPhase = default;
        }

        private static void Claim(object node, object owner, bool allowExisting = false)
        {
            if (!claims.TryGetValue(node, out var claimedOwner) || claimedOwner == null)
            {
                claims[node] = owner;
                return;
            }
            if (ReferenceEquals(claimedOwner, owner))
            {
                if (allowExisting) return;
                throw new InvalidOperationException(
                    $"Serialized node '{node.GetType().FullName}' appears more than once in the owned graph of " +
                    $"'{owner.GetType().FullName}'.");
            }
            throw new InvalidOperationException(
                $"Serialized node '{node.GetType().FullName}' cannot belong to both " +
                $"'{claimedOwner.GetType().FullName}' and '{owner.GetType().FullName}'.");
        }

        private static void ClaimRelease(object node)
        {
            releases.Add(node);
            if (!claims.ContainsKey(node)) claims.Add(node, null);
        }

        private static void CompleteEditorValidation()
        {
            foreach (var claim in claims)
            {
                if (claim.Value == null) continue;
                var currentOwner = OwnerOf(claim.Key);
                if (currentOwner != null && !ReferenceEquals(currentOwner, claim.Value) &&
                    !releases.Contains(claim.Key) && !CanTransferReleasedOwner(currentOwner, claim.Value))
                    throw new InvalidOperationException(
                        $"'{claim.Key.GetType().FullName}' already belongs to " +
                        $"'{currentOwner.GetType().FullName}'.");
                ValidateAcyclic(claim.Key, claim.Value);
            }
        }

        private static bool CanTransferReleasedOwner(object currentOwner, object finalOwner)
            => releases.Contains(currentOwner) && claims.TryGetValue(finalOwner, out var owner) &&
               owner != null;

        private enum EditorTransactionPhase : byte
        {
            Validation,
            Commit,
            Rollback
        }

        private static void ValidateAcyclic(object node, object requestedOwner)
        {
            var current = requestedOwner;
            HashSet<object> visited = null;
            while (current != null)
            {
                if (ReferenceEquals(current, node))
                    throw new InvalidOperationException(
                        $"Owned serialized graph contains a cycle through '{node.GetType().FullName}'.");
                visited ??= new HashSet<object>(ReferenceIdentityComparer<object>.Instance);
                if (!visited.Add(current))
                    throw new InvalidOperationException(
                        $"Owned serialized graph contains a cycle through '{current.GetType().FullName}'.");
                if (claims != null && claims.TryGetValue(current, out var finalOwner))
                    current = finalOwner;
                else
                    current = OwnerOf(current);
            }
        }
#endif

        private sealed class OwnershipSlot
        {
            internal object Owner;
        }

        private readonly struct OwnershipEdge
        {
            internal readonly object Node;
            internal readonly object Owner;
            internal readonly bool IsRoot;

            internal OwnershipEdge(object node, object owner, bool isRoot)
            {
                Node = node;
                Owner = owner;
                IsRoot = isRoot;
            }
        }

        private sealed class OwnershipGraph : IStateOwnershipVisitor
        {
            private readonly Dictionary<object, object> ownerByNode =
                new(ReferenceIdentityComparer<object>.Instance);
            private object currentOwner;
            private bool directChildrenAreRoots;

            internal readonly List<OwnershipEdge> Edges = new();

            internal void AddRoot(object node, object owner)
            {
                if (node == null) return;
                if (owner == null) throw new ArgumentNullException(nameof(owner));
                Add(node, owner, true);
            }

            internal void AddChildren(IStateOwnershipContainer owner)
            {
                var previousOwner = currentOwner;
                var previousRoots = directChildrenAreRoots;
                currentOwner = owner;
                directChildrenAreRoots = true;
                try
                {
                    owner.VisitOwnedState(this);
                }
                finally
                {
                    currentOwner = previousOwner;
                    directChildrenAreRoots = previousRoots;
                }
            }

            internal bool TryGetOwner(object node, out object owner)
                => ownerByNode.TryGetValue(node, out owner);

            void IStateOwnershipVisitor.Visit(object node)
            {
                if (node != null) Add(node, currentOwner, directChildrenAreRoots);
            }

            private void Add(object node, object owner, bool isRoot)
            {
                if (ownerByNode.TryGetValue(node, out var existingOwner))
                {
                    if (ReferenceEquals(existingOwner, owner))
                        throw new InvalidOperationException(
                            $"Serialized node '{node.GetType().FullName}' appears more than once in the owned graph of " +
                            $"'{owner.GetType().FullName}'.");
                    throw new InvalidOperationException(
                        $"Serialized node '{node.GetType().FullName}' cannot belong to both " +
                        $"'{existingOwner.GetType().FullName}' and '{owner.GetType().FullName}'.");
                }

                ownerByNode.Add(node, owner);
                Edges.Add(new OwnershipEdge(node, owner, isRoot));
                if (node is not IStateOwnershipContainer container) return;
                var previousOwner = currentOwner;
                var previousRoots = directChildrenAreRoots;
                currentOwner = node;
                directChildrenAreRoots = false;
                try
                {
                    container.VisitOwnedState(this);
                }
                finally
                {
                    currentOwner = previousOwner;
                    directChildrenAreRoots = previousRoots;
                }
            }
        }
    }

    internal sealed class ReferenceIdentityComparer<T> : IEqualityComparer<T> where T : class
    {
        internal static readonly ReferenceIdentityComparer<T> Instance = new();
        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    /// <summary>Provides reference-identity lookup and ordered reconciliation without value equality.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class ReferenceIdentity
    {
        /// <summary>Returns whether a read-only list contains the exact candidate reference.</summary>
        public static bool Contains<T>(IReadOnlyList<T> list, T candidate) where T : class
            => IndexOf(list, candidate) >= 0;

        /// <summary>Returns whether a generated state list contains the exact candidate reference.</summary>
        public static bool Contains<T>(StateList<T> list, T candidate) where T : class
            => IndexOf(list, candidate) >= 0;

        /// <summary>Returns the position of the exact candidate reference, or -1 when absent.</summary>
        public static int IndexOf<T>(IReadOnlyList<T> list, T candidate) where T : class
        {
            if (list == null) return -1;
            return IndexOfCore<IReadOnlyList<T>, T>(list, candidate);
        }

        /// <summary>Returns the position of the exact candidate reference without boxing the state-list view.</summary>
        public static int IndexOf<T>(StateList<T> list, T candidate) where T : class
            => IndexOfCore<StateList<T>, T>(list, candidate);

        /// <summary>Connects additions, disconnects removals, and restores source order by reference identity.</summary>
        public static void Reconcile<TSource, T>(IReadOnlyList<TSource> source, List<T> bound,
            Action<T> connect, Action<T> disconnect) where T : class
        {
            if (bound == null) throw new ArgumentNullException(nameof(bound));
            if (connect == null) throw new ArgumentNullException(nameof(connect));
            if (disconnect == null) throw new ArgumentNullException(nameof(disconnect));

            for (var i = bound.Count - 1; i >= 0; i--)
            {
                var item = bound[i];
                if (Contains(source, item)) continue;
                disconnect(item);
                bound.RemoveAt(i);
            }

            if (source == null) return;
            for (var i = 0; i < source.Count; i++)
            {
                if (source[i] is not T item || item == null || IndexOf(bound, item) >= 0) continue;
                connect(item);
                bound.Add(item);
            }

            var target = 0;
            for (var i = 0; i < source.Count; i++)
            {
                if (source[i] is not T item || item == null) continue;
                var index = IndexOf(bound, item);
                if (index < target) continue;
                if (index != target)
                {
                    bound.RemoveAt(index);
                    bound.Insert(target, item);
                }
                target++;
            }
        }

        /// <summary>Disconnects every bound reference and empties the list.</summary>
        public static void Clear<T>(List<T> bound, Action<T> disconnect) where T : class
        {
            if (bound == null) throw new ArgumentNullException(nameof(bound));
            if (disconnect == null) throw new ArgumentNullException(nameof(disconnect));
            for (var i = 0; i < bound.Count; i++) disconnect(bound[i]);
            bound.Clear();
        }

        private static bool Contains<TSource, T>(IReadOnlyList<TSource> source, T candidate) where T : class
        {
            if (source == null) return false;
            for (var i = 0; i < source.Count; i++)
                if (source[i] is T item && ReferenceEquals(item, candidate)) return true;
            return false;
        }

        private static int IndexOfCore<TList, T>(TList list, T candidate)
            where TList : IReadOnlyList<T>
            where T : class
        {
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], candidate)) return i;
            return -1;
        }
    }

    /// <summary>Keeps reference-identity subscriptions synchronized with an authored collection.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ReferenceBinding<T> : IReadOnlyList<T> where T : class
    {
        private readonly List<T> items = new();
        private readonly Action<T> connect;
        private readonly Action<T> disconnect;

        /// <summary>Creates a binding whose callbacks attach and detach one unique referenced item.</summary>
        public ReferenceBinding(Action<T> connect, Action<T> disconnect)
        {
            this.connect = connect ?? throw new ArgumentNullException(nameof(connect));
            this.disconnect = disconnect ?? throw new ArgumentNullException(nameof(disconnect));
        }

        /// <summary>Gets the number of currently connected unique references.</summary>
        public int Count => items.Count;

        /// <summary>Gets a connected reference in authored order.</summary>
        public T this[int index] => items[index];

        /// <summary>Connects additions, disconnects removals, and restores authored order.</summary>
        public void Reconcile<TSource>(IReadOnlyList<TSource> source)
            => ReferenceIdentity.Reconcile(source, items, connect, disconnect);

        /// <summary>Disconnects every bound reference and empties the binding.</summary>
        public void Clear() => ReferenceIdentity.Clear(items, disconnect);

        /// <summary>Returns whether the binding contains the exact reference.</summary>
        public bool Contains(T candidate) => ReferenceIdentity.Contains(items, candidate);

        /// <summary>Returns the position of the exact reference, or -1 when absent.</summary>
        public int IndexOf(T candidate) => ReferenceIdentity.IndexOf(items, candidate);

        /// <summary>Returns an allocation-free enumerator over connected references.</summary>
        public List<T>.Enumerator GetEnumerator() => items.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
