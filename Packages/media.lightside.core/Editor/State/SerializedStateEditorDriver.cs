using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class SerializedStateEditorDriver
    {
        private static readonly object gate = new();
        private static readonly Dictionary<Type, FieldInfo[]> serializedFields = new();
        private static readonly Dictionary<Type, TraversedField[]> traversedFields = new();
        private static readonly Dictionary<Type, bool> stateContainers = new();
        private static readonly Dictionary<Type, bool> hostStateContainers = new();
        private static readonly HashSet<Object> pendingHosts = new(ReferenceIdentityComparer<Object>.Instance);
        private static ConditionalWeakTable<Object, SerializedPropertyState> propertyStates = new();
        internal static event Action<IReadOnlyCollection<Object>> ObjectsChanged;

        static SerializedStateEditorDriver()
        {
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            ObjectChangeEvents.changesPublished -= OnChangesPublished;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            StateEditorReceptor.Bind(QueueCapturedOwner);
            EditorLifecycle.PlayModeEntering += ResetState;
            EditorLifecycle.ManagedCleaning += ResetState;
        }

        private static void Pump() => DrainPendingHosts();

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            var changed = new HashSet<Object>(ReferenceIdentityComparer<Object>.Instance);
            for (var i = 0; i < stream.length; i++)
            {
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                        stream.GetCreateGameObjectHierarchyEvent(i, out var createdHierarchy);
#if UNITY_6000_4_OR_NEWER
                        AddHierarchy(createdHierarchy.entityId, changed);
#else
                        AddHierarchy(createdHierarchy.instanceId, changed);
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                        stream.GetChangeGameObjectStructureHierarchyEvent(i, out var changedHierarchy);
#if UNITY_6000_4_OR_NEWER
                        AddHierarchy(changedHierarchy.entityId, changed);
#else
                        AddHierarchy(changedHierarchy.instanceId, changed);
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectStructure:
                        stream.GetChangeGameObjectStructureEvent(i, out var structureChange);
#if UNITY_6000_4_OR_NEWER
                        Add(structureChange.entityId, changed);
#else
                        Add(structureChange.instanceId, changed);
#endif
                        break;
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var componentChange);
#if UNITY_6000_4_OR_NEWER
                        Add(componentChange.entityId, changed);
#else
                        Add(componentChange.instanceId, changed);
#endif
                        break;
                    case ObjectChangeKind.CreateAssetObject:
                        stream.GetCreateAssetObjectEvent(i, out var createdAsset);
#if UNITY_6000_4_OR_NEWER
                        Add(createdAsset.entityId, changed);
#else
                        Add(createdAsset.instanceId, changed);
#endif
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var assetChange);
#if UNITY_6000_4_OR_NEWER
                        Add(assetChange.entityId, changed);
#else
                        Add(assetChange.instanceId, changed);
#endif
                        break;
                    case ObjectChangeKind.UpdatePrefabInstances:
                        stream.GetUpdatePrefabInstancesEvent(i, out var prefabChange);
#if UNITY_6000_4_OR_NEWER
                        foreach (var entityId in prefabChange.entityIds) AddHierarchy(entityId, changed);
#else
                        foreach (var instanceId in prefabChange.instanceIds) AddHierarchy(instanceId, changed);
#endif
                        break;
                }
            }
            foreach (var host in changed) QueueHost(host);
            if (changed.Count != 0 && MainThread.IsCurrent) DrainPendingHosts();
            if (changed.Count != 0) ObjectsChanged?.Invoke(changed);
        }

#if UNITY_6000_4_OR_NEWER
        private static void Add(EntityId entityId, HashSet<Object> changed)
        {
            var target = ObjectRefCompat.Resolve(entityId);
            if (target != null) changed.Add(target);
        }

        private static void AddHierarchy(EntityId entityId, HashSet<Object> changed)
            => AddHierarchy(ObjectRefCompat.Resolve(entityId), changed);
#else
        private static void Add(int instanceId, HashSet<Object> changed)
        {
            var target = ObjectRefCompat.Resolve(instanceId);
            if (target != null) changed.Add(target);
        }

        private static void AddHierarchy(int instanceId, HashSet<Object> changed)
            => AddHierarchy(ObjectRefCompat.Resolve(instanceId), changed);
#endif

        private static void AddHierarchy(Object target, HashSet<Object> changed)
        {
            if (target is not GameObject root)
            {
                if (target != null) changed.Add(target);
                return;
            }
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                changed.Add(transform.gameObject);
        }

        internal static void QueueCapturedOwner(IEditorSerializedStateOwner owner)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (owner is not Object host || ReferenceEquals(host, null)) return;
            QueuePendingHost(host);
        }

        internal static void QueueSerializedWrite(Object host) => QueueHost(host);

        internal static void PrepareSerializedEdit(Object host, IEditorSerializedStateOwner owner)
        {
            if (!ObjectRefCompat.IsRegistered(host)) throw new ArgumentNullException(nameof(host));
            EnsurePropertyState(host);
            var owners = CollectHostOwners(host);
            if (owners.Count == 0)
            {
                if (owner != null)
                    throw new InvalidOperationException(
                        $"Serialized state owner '{owner.GetType().FullName}' is not reachable from host '{host.name}'.");
                return;
            }
            if (owner != null && !ReferenceIdentity.Contains(owners, owner))
                throw new InvalidOperationException(
                    $"Serialized state owner '{owner.GetType().FullName}' is not part of host '{host.name}'.");
            if (AllInitialized(owners))
                return;
            InitializeHost(owners, true);
        }

        internal static void DrainPendingHosts()
        {
            if (!MainThread.IsCurrent)
                throw new InvalidOperationException("Serialized state reconciliation must run on the main thread.");

            var hosts = TakePendingHosts();
            List<Exception> failures = null;
            for (var i = 0; i < hosts.Count; i++)
            {
                var host = hosts[i];
                if (!ObjectRefCompat.IsRegistered(host)) continue;
                try
                {
                    ReconcileHost(host);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }
            if (failures == null) return;
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Multiple serialized-state host transactions failed.", failures);
        }

        internal static void ResetState()
        {
            lock (gate)
            {
                pendingHosts.Clear();
                propertyStates = new ConditionalWeakTable<Object, SerializedPropertyState>();
            }
        }

        private static void QueueHost(Object host)
        {
            if (!ObjectRefCompat.IsRegistered(host)) return;
            if (host is GameObject gameObject)
            {
                foreach (var component in gameObject.GetComponents<Component>())
                    if (component != null) QueueHost(component);
                return;
            }
            if (!HostCanContainStateOwner(host.GetType())) return;
            QueuePendingHost(host);
        }

        private static void QueuePendingHost(Object host)
        {
            lock (gate) pendingHosts.Add(host);
        }

        private static List<Object> TakePendingHosts()
        {
            lock (gate)
            {
                var result = new List<Object>(pendingHosts.Count);
                foreach (var host in pendingHosts) result.Add(host);
                pendingHosts.Clear();
                return result;
            }
        }

        private static void ReconcileHost(Object host)
        {
            var owners = CollectHostOwners(host);
            if (owners.Count == 0) return;
            var propertyState = EnsurePropertyState(host);
            if (!AllInitialized(owners))
            {
                var existing = false;
                for (var i = 0; i < owners.Count; i++) existing |= owners[i].EditorStateInitialized;
                if (!existing)
                {
                    InitializeHost(owners);
                    return;
                }
            }

            LockOwners(owners);
            var validationScope = false;
            var rejected = false;
            object[] candidates = null;
            object[] propertyCandidate = null;
            var newer = false;
            try
            {
                for (var i = 0; i < owners.Count; i++) owners[i].CaptureEditorSerializedState();
                candidates = CreateCandidates(owners);
                if (!ObjectRefCompat.IsRegistered(host)) return;
                propertyCandidate = propertyState?.Capture(host);
                StateOwnership.EnterEditorTransaction();
                validationScope = true;
                ValidateOwnersCore(owners, candidates);
                StateOwnership.BeginEditorCommit();
                for (var i = 0; i < owners.Count; i++)
                    if (!owners[i].EditorStateInitialized)
                        owners[i].InitializeEditorSerializedState(candidates[i]);

                var context = new EditorStateMutationContext();
                try
                {
                    for (var i = 0; i < owners.Count; i++)
                        owners[i].StageEditorSerializedState(candidates[i], ref context);
                    propertyState?.Stage(propertyCandidate, ref context);
                    try
                    {
                        context.Invoke();
                    }
                    catch (Exception callbackFailure)
                    {
                        StateOwnership.BeginEditorRollback();
                        rejected = true;
                        var rejectionFailure = RejectHostState(host, owners, candidates,
                            propertyState, propertyCandidate);
                        Exception rollbackFailure = null;
                        try
                        {
                            context.Rollback();
                        }
                        catch (Exception exception)
                        {
                            rollbackFailure = exception;
                        }
                        if (rejectionFailure != null || rollbackFailure != null)
                        {
                            var failures = new List<Exception> { callbackFailure };
                            if (rejectionFailure != null) failures.Add(rejectionFailure);
                            if (rollbackFailure != null) failures.Add(rollbackFailure);
                            throw new AggregateException(
                                "Serialized-state callback failed and rollback did not complete cleanly.",
                                failures);
                        }
                        throw;
                    }
                    for (var i = 0; i < owners.Count; i++)
                        newer |= owners[i].AcceptEditorSerializedState(candidates[i]);
                    propertyState?.Accept(propertyCandidate);
                }
                finally
                {
                    context.Dispose();
                }
            }
            catch (Exception failure)
            {
                if (!rejected)
                {
                    if (validationScope) StateOwnership.BeginEditorRollback();
                    rejected = true;
                    var rejectionFailure = RejectHostState(host, owners, candidates,
                        propertyState, propertyCandidate);
                    if (rejectionFailure != null)
                    {
                        RestoreUnityView(host);
                        throw new AggregateException(
                            "Serialized-state transaction failed and authored state could not be fully restored.",
                            failure, rejectionFailure);
                    }
                }
                RestoreUnityView(host);
                throw;
            }
            finally
            {
                if (validationScope) StateOwnership.ExitEditorTransaction();
                UnlockOwners(owners);
            }
            if (newer) QueueHost(host);
        }

        private static Exception RejectHostState(Object host,
            List<IEditorSerializedStateOwner> owners, object[] candidates,
            SerializedPropertyState propertyState, object[] propertyCandidate)
        {
            List<Exception> failures = null;
            try
            {
                RejectOwners(owners, candidates);
            }
            catch (Exception exception)
            {
                failures = new List<Exception> { exception };
            }
            try
            {
                propertyState?.Restore(host, propertyCandidate);
            }
            catch (Exception exception)
            {
                failures ??= new List<Exception>();
                failures.Add(exception);
            }
            if (failures == null) return null;
            return failures.Count == 1
                ? failures[0]
                : new AggregateException("Authored state restoration failed.", failures);
        }

        private static void RejectOwners(List<IEditorSerializedStateOwner> owners,
            object[] candidates)
        {
            if (candidates == null) return;
            for (var i = 0; i < owners.Count; i++)
                if (owners[i].EditorStateInitialized)
                    owners[i].RejectEditorSerializedState(candidates[i]);
        }

        private static void InitializeHost(List<IEditorSerializedStateOwner> owners,
            bool allowRepair = false)
        {
            LockOwners(owners);
            try
            {
                for (var i = 0; i < owners.Count; i++) owners[i].CaptureEditorSerializedState();
                var candidates = CreateCandidates(owners);
                try
                {
                    ValidateOwners(owners, candidates);
                }
                catch when (allowRepair)
                {
                    return;
                }
                for (var i = 0; i < owners.Count; i++)
                    owners[i].InitializeEditorSerializedState(candidates[i]);
            }
            finally
            {
                UnlockOwners(owners);
            }
        }

        private static object[] CreateCandidates(List<IEditorSerializedStateOwner> owners)
        {
            var candidates = new object[owners.Count];
            for (var i = 0; i < owners.Count; i++)
                candidates[i] = owners[i].CreateEditorStateCandidate();
            return candidates;
        }

        private static void ValidateOwners(List<IEditorSerializedStateOwner> owners,
            object[] candidates)
        {
            StateOwnership.EnterEditorTransaction();
            try
            {
                ValidateOwnersCore(owners, candidates);
                StateOwnership.BeginEditorCommit();
            }
            finally
            {
                StateOwnership.ExitEditorTransaction();
            }
        }

        private static void ValidateOwnersCore(List<IEditorSerializedStateOwner> owners,
            object[] candidates)
        {
            for (var i = 0; i < owners.Count; i++)
                owners[i].ValidateEditorSerializedState(candidates[i]);
        }

        private static void RestoreUnityView(Object host)
        {
            if (!ObjectRefCompat.IsRegistered(host)) return;
            if (host is Component component && PrefabUtility.IsPartOfPrefabInstance(component))
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            EditorUtility.SetDirty(host);
        }

        private static List<IEditorSerializedStateOwner> CollectHostOwners(Object host)
        {
            var owners = new List<IEditorSerializedStateOwner>();
            var visited = new HashSet<object>(ReferenceIdentityComparer<object>.Instance);
            CaptureGraph(host, true, false, visited, owners);
            return owners;
        }

        private static void CaptureGraph(object value, bool traverseUnityObject,
            bool polymorphicCollection, HashSet<object> visited,
            List<IEditorSerializedStateOwner> owners)
        {
            if (value == null) return;
            var type = value.GetType();
            if (IsLeaf(type)) return;
            if (value is Object && !traverseUnityObject) return;
            if (!type.IsValueType && !visited.Add(value)) return;
            if (value is IEditorSerializedStateOwner owner) owners.Add(owner);

            if (value is IList list)
            {
                var elementType = GetCollectionElementType(type);
                if (!polymorphicCollection && !UsesManagedReferenceElements(type)
                    && elementType != null && !CanContainStateOwner(elementType)) return;
                for (var i = 0; i < list.Count; i++)
                    CaptureGraph(list[i], false, false, visited, owners);
                return;
            }
            foreach (var field in GetTraversedFields(type))
                CaptureGraph(field.Info.GetValue(value), false, field.Polymorphic, visited, owners);
        }

        private static FieldInfo[] GetSerializedFields(Type type)
        {
            if (serializedFields.TryGetValue(type, out var cached)) return cached;
            var result = new List<FieldInfo>();
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!SerializedReflection.IsSerializedField(field)) continue;
                    result.Add(field);
                }
            cached = result.ToArray();
            serializedFields.Add(type, cached);
            return cached;
        }

        private static TraversedField[] GetTraversedFields(Type type)
        {
            if (traversedFields.TryGetValue(type, out var cached)) return cached;
            var result = new List<TraversedField>();
            foreach (var field in GetSerializedFields(type))
                if (field.IsDefined(typeof(SerializeReference), true) || CanContainStateOwner(field.FieldType))
                    result.Add(new TraversedField(field));
            cached = result.ToArray();
            traversedFields.Add(type, cached);
            return cached;
        }

        private static bool CanContainStateOwner(Type type)
        {
            if (stateContainers.TryGetValue(type, out var cached)) return cached;
            var result = CanContainStateOwner(type, new HashSet<Type>());
            stateContainers.Add(type, result);
            return result;
        }

        private static bool HostCanContainStateOwner(Type type)
        {
            if (hostStateContainers.TryGetValue(type, out var cached)) return cached;
            var result = typeof(IEditorSerializedStateOwner).IsAssignableFrom(type);
            if (!result)
                foreach (var field in GetSerializedFields(type))
                    if (field.IsDefined(typeof(SerializeReference), true) ||
                        CanContainStateOwner(field.FieldType))
                    {
                        result = true;
                        break;
                    }
            hostStateContainers.Add(type, result);
            return result;
        }

        private static bool CanContainStateOwner(Type type, HashSet<Type> visiting)
        {
            if (stateContainers.TryGetValue(type, out var cached)) return cached;
            if (typeof(Object).IsAssignableFrom(type) || IsLeaf(type)) return false;
            if (typeof(IEditorSerializedStateOwner).IsAssignableFrom(type)) return true;
            if (typeof(IList).IsAssignableFrom(type))
            {
                if (UsesManagedReferenceElements(type)) return true;
                var elementType = GetCollectionElementType(type);
                return elementType == null || CanContainStateOwner(elementType, visiting);
            }
            if (!visiting.Add(type)) return false;
            foreach (var field in GetSerializedFields(type))
                if (field.IsDefined(typeof(SerializeReference), true) ||
                    CanContainStateOwner(field.FieldType, visiting))
                {
                    visiting.Remove(type);
                    return true;
                }
            visiting.Remove(type);
            return false;
        }

        private static Type GetCollectionElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
                return type.GetGenericArguments()[0];
            foreach (var contract in type.GetInterfaces())
                if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IList<>))
                    return contract.GetGenericArguments()[0];
            return null;
        }

        private static bool UsesManagedReferenceElements(Type type)
        {
            for (var current = type; current != null && current != typeof(object);
                 current = current.BaseType)
            {
                if (current.IsDefined(typeof(StateCollectionElementReferenceAttribute), true))
                    return true;
                foreach (var field in current.GetFields(BindingFlags.Instance |
                                                        BindingFlags.Public |
                                                        BindingFlags.NonPublic |
                                                        BindingFlags.DeclaredOnly))
                    if (!field.IsStatic && field.IsDefined(typeof(SerializeReference), true) &&
                        GetCollectionElementType(field.FieldType) != null)
                        return true;
            }
            return false;
        }

        private static void LockOwners(List<IEditorSerializedStateOwner> owners)
        {
            for (var i = 0; i < owners.Count; i++) Monitor.Enter(owners[i].EditorStateSyncRoot);
        }

        private static void UnlockOwners(List<IEditorSerializedStateOwner> owners)
        {
            for (var i = owners.Count - 1; i >= 0; i--) Monitor.Exit(owners[i].EditorStateSyncRoot);
        }

        private static bool AllInitialized(List<IEditorSerializedStateOwner> owners)
        {
            for (var i = 0; i < owners.Count; i++)
                if (!owners[i].EditorStateInitialized) return false;
            return true;
        }

        private static SerializedPropertyState EnsurePropertyState(Object host)
        {
            if (host is not IEditorSerializedPropertyStateOwner) return null;
            return propertyStates.GetValue(host, CreatePropertyState);
        }

        private static SerializedPropertyState CreatePropertyState(Object host) => new(host);

        private static SerializedObject CreateSerializedObject(Object host)
        {
            if (!ObjectRefCompat.IsRegistered(host))
                throw new MissingReferenceException(
                    "Serialized-state host is no longer registered with Unity.");
            return new SerializedObject(host);
        }

        private static bool IsLeaf(Type type)
            => type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal);

        private sealed class SerializedPropertyState
        {
            private readonly IEditorSerializedPropertyStateOwner owner;
            private readonly SerializedPropertyRoute[] routes;
            private object[] applied;

            internal SerializedPropertyState(Object host)
            {
                owner = (IEditorSerializedPropertyStateOwner)host;
                var count = owner.EditorSerializedPropertyCount;
                if (count < 0)
                    throw new InvalidOperationException(
                        $"'{host.GetType().FullName}' returned a negative serialized-property count.");
                routes = new SerializedPropertyRoute[count];
                applied = new object[count];
                for (var i = 0; i < count; i++)
                {
                    owner.GetEditorSerializedProperty(i, out var path, out var callback);
                    if (string.IsNullOrEmpty(path))
                        throw new InvalidOperationException(
                            $"'{host.GetType().FullName}' returned an empty serialized-property path at index {i}.");
                    for (var previous = 0; previous < i; previous++)
                        if (routes[previous].Path == path)
                            throw new InvalidOperationException(
                                $"Serialized-property route '{path}' is declared more than once on '{host.GetType().FullName}'.");

                    SerializedPropertyBinding.ResolveMetadata(host, path, out _, out var type);
                    if (!type.IsValueType && type != typeof(string) &&
                        !typeof(Object).IsAssignableFrom(type))
                        throw new InvalidOperationException(
                            $"Serialized-property route '{path}' on '{host.GetType().FullName}' must be a value type, string, or Unity object reference.");
                    if (callback < 0)
                        throw new InvalidOperationException(
                            $"Serialized-property route '{path}' on '{host.GetType().FullName}' has a negative callback identity.");
                    routes[i] = new SerializedPropertyRoute(path, type, callback);
                }
                using var serialized = CreateSerializedObject(host);
                serialized.UpdateIfRequiredOrScript();
                for (var i = 0; i < count; i++)
                {
                    var route = routes[i];
                    var property = Require(serialized, host, route.Path);
                    if (property.isArray && route.Type != typeof(string))
                        throw new InvalidOperationException(
                            $"Serialized-property route '{route.Path}' on '{host.GetType().FullName}' must be a scalar property.");
                    applied[i] = ReadDetached(property, route.Type);
                }
            }

            internal object[] Capture(Object host)
            {
                using var serialized = CreateSerializedObject(host);
                serialized.UpdateIfRequiredOrScript();
                var values = new object[routes.Length];
                for (var i = 0; i < routes.Length; i++)
                {
                    var route = routes[i];
                    values[i] = ReadDetached(Require(serialized, host, route.Path), route.Type);
                }
                return values;
            }

            internal void Stage(object[] candidate,
                ref EditorStateMutationContext context)
            {
                for (var i = 0; i < routes.Length; i++)
                    if (!ValueEquals(applied[i], candidate[i]))
                        context.Schedule(owner, routes[i].Callback);
            }

            internal void Accept(object[] candidate) => applied = candidate;

            internal void Restore(Object host, object[] candidate)
            {
                if (candidate == null || !ObjectRefCompat.IsRegistered(host)) return;
                using var serialized = CreateSerializedObject(host);
                serialized.UpdateIfRequiredOrScript();
                var changed = false;
                for (var i = 0; i < routes.Length; i++)
                {
                    if (ValueEquals(applied[i], candidate[i])) continue;
                    var route = routes[i];
                    SerializedPropertyBinding.Write(
                        Require(serialized, host, route.Path), applied[i], route.Type);
                    changed = true;
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private static SerializedProperty Require(SerializedObject serialized, Object host,
                string path)
                => serialized.FindProperty(path) ?? throw new InvalidOperationException(
                    $"Serialized-property route '{path}' does not exist on '{host.name}'.");

            private static object ReadDetached(SerializedProperty property, Type type)
                => PropertyClipboard.CloneObject(SerializedPropertyBinding.Read(property, type));

            private static bool ValueEquals(object left, object right)
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is Object || right is Object) return false;
                return Equals(left, right);
            }
        }

        private readonly struct SerializedPropertyRoute
        {
            internal SerializedPropertyRoute(string path, Type type, int callback)
            {
                Path = path;
                Type = type;
                Callback = callback;
            }

            internal string Path { get; }
            internal Type Type { get; }
            internal int Callback { get; }
        }

        private readonly struct TraversedField
        {
            internal TraversedField(FieldInfo info)
            {
                Info = info;
                Polymorphic = info.IsDefined(typeof(SerializeReference), true);
            }

            internal FieldInfo Info { get; }
            internal bool Polymorphic { get; }
        }

    }
}
