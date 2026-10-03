using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Where a feature gets the objects it operates on. One source stands for one authored slot — "the button",
    /// "every enemy in the wave" — and reports both what it yields and what type that is, so a consumer can be
    /// offered only the sources it can actually use.
    /// </summary>
    /// <remarks>
    /// Serialized polymorphically through <c>[SerializeReference, TypeSelector]</c>. A source is asked for its
    /// objects whenever a consumer needs them and must be cheap enough to call on demand; it is not a cache.
    /// </remarks>
    [Serializable]
    [StateHierarchy]
    [StateSource]
    [TypeMenuSuffix("Source")]
    [SelectorNoneLabel("Nothing")]
    public abstract partial class ObjectSource
    {
        /// <summary>The most derived type every object this source yields is assignable to.</summary>
        public abstract Type ProvidedType { get; }

        /// <summary>Appends this source's objects to <paramref name="results"/>, appending nothing when it has none.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is <see langword="null"/>.</exception>
        public abstract void Collect(List<object> results);

        /// <summary>Whether a consumer expecting <paramref name="wanted"/> can use this source.</summary>
        public bool Provides(Type wanted) => wanted != null && wanted.IsAssignableFrom(ProvidedType);
    }

    /// <summary>One component, of the type chosen alongside the source.</summary>
    [Serializable]
    [TypeGroup("Component", 0)]
    [TypeDescription("One component of the chosen type.")]
    public partial class ComponentSource<T> : ObjectSource where T : Component
    {
        /// <summary>The component, or null while the slot is unfilled.</summary>
        [SerializeField, StateProperty]
        [Tooltip("Component this slot stands for.")]
        private T component;

        /// <inheritdoc/>
        public override Type ProvidedType => typeof(T);

        /// <inheritdoc/>
        public override void Collect(List<object> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (component != null) results.Add(component);
        }
    }

    /// <summary>Every component of the chosen type under one root, in hierarchy order.</summary>
    [Serializable]
    [TypeGroup("Component", 1)]
    [TypeDescription("Every component of the chosen type found under a root.")]
    public partial class ComponentTreeSource<T> : ObjectSource where T : Component
    {
        /// <summary>Root the search starts from, or null while the slot is unfilled.</summary>
        [SerializeField, StateProperty]
        [Tooltip("Root the search starts from.")]
        private Transform root;

        /// <summary>Whether components on inactive objects are included.</summary>
        [SerializeField, StateProperty]
        [Tooltip("Whether components on inactive objects are included.")]
        private bool includeInactive = true;

        [NonSerialized] private List<T> scratch;

        /// <inheritdoc/>
        public override Type ProvidedType => typeof(T);

        /// <inheritdoc/>
        public override void Collect(List<object> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (root == null) return;

            scratch ??= new List<T>();
            root.GetComponentsInChildren(includeInactive, scratch);
            for (var i = 0; i < scratch.Count; i++) results.Add(scratch[i]);
            scratch.Clear();
        }
    }

    /// <summary>Every object a list holds, of the chosen type.</summary>
    [Serializable]
    [TypeGroup("Component", 2)]
    [TypeDescription("An explicit list of components of the chosen type.")]
    public partial class ComponentListSource<T> : ObjectSource where T : Component
    {
        /// <summary>The components, in the order a consumer receives them.</summary>
        [SerializeField, StateList(AllowNullItems = false)]
        [Tooltip("Components this slot stands for, in order.")]
        private List<T> components = new();

        /// <inheritdoc/>
        public override Type ProvidedType => typeof(T);

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// A serialized entry is null or references a destroyed component.
        /// </exception>
        public override void Collect(List<object> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            for (var i = 0; i < components.Count; i++)
                if (components[i] == null)
                    throw new InvalidOperationException(
                        $"Component list entry {i} is null or destroyed.");
            for (var i = 0; i < components.Count; i++) results.Add(components[i]);
        }
    }

    /// <summary>
    /// One component constrained by an authored runtime type. This non-generic form keeps
    /// <c>[SerializeReference]</c> authoring available on Unity versions that cannot serialize closed
    /// generic managed-reference instances.
    /// </summary>
#if UNITY_2023_1_OR_NEWER
    [HideFromTypeSelector]
#endif
    [Serializable]
    [TypeGroup("Component", 0)]
    [TypeDescription("One component of the chosen type.")]
    public partial class ComponentSource : ObjectSource
    {
        /// <summary>Runtime type the component must satisfy; the default accepts every component.</summary>
        [SerializeField, StateProperty(Validator = nameof(ValidateComponentType))]
        [Tooltip("Runtime type the component must satisfy. Any Component accepts every component type.")]
        private ComponentTypeToken componentType;

        /// <summary>The component, or null while the slot is unfilled.</summary>
        [SerializeField, StateProperty(Validator = nameof(ValidateComponent))]
        [Tooltip("Component this slot stands for.")]
        private Component component;

        /// <inheritdoc/>
        public override Type ProvidedType => componentType.ComponentType;

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// The serialized component does not satisfy <see cref="ProvidedType"/>.
        /// </exception>
        public override void Collect(List<object> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var required = ProvidedType;
            if (component == null) return;

            if (!required.IsInstanceOfType(component))
                throw new InvalidOperationException(
                    $"Component '{component.name}' has type '{component.GetType().FullName}', " +
                    $"which is not assignable to '{required.FullName}'.");
            results.Add(component);
        }

        private void ValidateComponentType(ComponentTypeToken candidate)
        {
            var required = candidate.ComponentType;
            if (component != null && !required.IsInstanceOfType(component))
                throw new ArgumentException(
                    $"Component '{component.name}' is not assignable to " +
                    $"'{required.FullName}'.");
        }

        private void ValidateComponent(Component candidate)
        {
            var required = ProvidedType;
            if (candidate != null && !required.IsInstanceOfType(candidate))
                throw new ArgumentException(
                    $"Component '{candidate.name}' is not assignable to '{required.FullName}'.");
        }
    }

    /// <summary>
    /// Every component of an authored runtime type under one root. The closed generic collector and its
    /// reusable result buffer are retained by this source after the type is first resolved.
    /// </summary>
#if UNITY_2023_1_OR_NEWER
    [HideFromTypeSelector]
#endif
    [Serializable]
    [TypeGroup("Component", 1)]
    [TypeDescription("Every component of the chosen type found under a root.")]
    public partial class ComponentTreeSource : ObjectSource
    {
        /// <summary>Runtime type collected below the root; the default collects every component.</summary>
        [SerializeField, StateProperty]
        [Tooltip("Runtime component type collected below the root. Any Component collects every component type.")]
        private ComponentTypeToken componentType;

        /// <summary>Root the search starts from, or null while the slot is unfilled.</summary>
        [SerializeField, StateProperty]
        [Tooltip("Root the search starts from.")]
        private Transform root;

        /// <summary>Whether components on inactive objects are included.</summary>
        [SerializeField, StateProperty]
        [Tooltip("Whether components on inactive objects are included.")]
        private bool includeInactive = true;

        [NonSerialized] private RuntimeCollector collector;

        /// <inheritdoc/>
        public override Type ProvidedType => componentType.ComponentType;

        /// <inheritdoc/>
        public override void Collect(List<object> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var required = ProvidedType;
            if (root == null) return;

            if (collector == null || collector.ComponentType != required)
                collector = RuntimeCollector.Create(required);
            collector.Collect(root, includeInactive, results);
        }

        private abstract class RuntimeCollector
        {
            protected RuntimeCollector(Type componentType) => ComponentType = componentType;

            public Type ComponentType { get; }

            public abstract void Collect(Transform searchRoot, bool includeInactiveObjects,
                List<object> results);

            public static RuntimeCollector Create(Type componentType) =>
                (RuntimeCollector)Activator.CreateInstance(
                    typeof(RuntimeCollector<>).MakeGenericType(componentType), nonPublic: true);
        }

        private sealed class RuntimeCollector<T> : RuntimeCollector where T : Component
        {
            private readonly List<T> scratch = new();

            public RuntimeCollector() : base(typeof(T)) { }

            public override void Collect(Transform searchRoot, bool includeInactiveObjects,
                List<object> results)
            {
                searchRoot.GetComponentsInChildren(includeInactiveObjects, scratch);
                try
                {
                    for (var i = 0; i < scratch.Count; i++) results.Add(scratch[i]);
                }
                finally
                {
                    scratch.Clear();
                }
            }
        }
    }

    /// <summary>
    /// An explicit component list constrained by an authored runtime type. Incompatible writes and type
    /// changes are rejected without clearing or filtering existing entries.
    /// </summary>
#if UNITY_2023_1_OR_NEWER
    [HideFromTypeSelector]
#endif
    [Serializable]
    [TypeGroup("Component", 2)]
    [TypeDescription("An explicit list of components of the chosen type.")]
    public partial class ComponentListSource : ObjectSource
    {
        /// <summary>Runtime type every entry must satisfy; the default accepts every component.</summary>
        [SerializeField, StateProperty(Validator = nameof(ValidateComponentType))]
        [Tooltip("Runtime type every list entry must satisfy. Any Component accepts every component type.")]
        private ComponentTypeToken componentType;

        /// <summary>The components, in the order a consumer receives them.</summary>
        [SerializeField, StateList(AllowNullItems = false, Validator = nameof(ValidateComponents))]
        [Tooltip("Components this slot stands for, in order.")]
        private List<Component> components = new();

        /// <inheritdoc/>
        public override Type ProvidedType => componentType.ComponentType;

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// A serialized entry is null or does not satisfy <see cref="ProvidedType"/>.
        /// </exception>
        public override void Collect(List<object> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var required = ProvidedType;
            for (var i = 0; i < components.Count; i++)
            {
                var entry = components[i];
                if (entry == null)
                    throw new InvalidOperationException(
                        $"Component list entry {i} is null.");
                if (!required.IsInstanceOfType(entry))
                    throw new InvalidOperationException(
                        $"Component list entry {i} has type '{entry.GetType().FullName}', " +
                        $"which is not assignable to '{required.FullName}'.");
            }
            for (var i = 0; i < components.Count; i++) results.Add(components[i]);
        }

        private void ValidateComponentType(ComponentTypeToken candidate)
        {
            var current = Components;
            ValidateComponents(in current, candidate.ComponentType);
        }

        private void ValidateComponents(in StateListMutation<Component> candidate) =>
            ValidateComponents(in candidate, ProvidedType);

        private static void ValidateComponents<TList>(in TList candidate, Type required)
            where TList : IReadOnlyList<Component>
        {
            for (var i = 0; i < candidate.Count; i++)
            {
                var entry = candidate[i];
                if (entry == null)
                    throw new ArgumentException($"Component list entry {i} cannot be null.");
                if (!required.IsInstanceOfType(entry))
                    throw new ArgumentException(
                        $"Component list entry {i} has type '{entry.GetType().FullName}', " +
                        $"which is not assignable to '{required.FullName}'.");
            }
        }
    }

    /// <summary>One named slot of a feature's object list.</summary>
    /// <remarks>
    /// The name is how everything else refers to the slot, so renaming it re-points every reference to it and
    /// two slots sharing a name are ambiguous.
    /// </remarks>
    [Serializable]
    public struct ObjectSlot : IStateSnapshot<ObjectSlot>
    {
        [SerializeField, Tooltip("How everything else refers to this slot.")]
        private string name;

        [SerializeReference, TypeSelector, Tooltip("Where the objects in this slot come from.")]
        private ObjectSource source;

        /// <summary>How everything else refers to this slot.</summary>
        public string Name { get => name; set => name = value; }

        /// <summary>Where the objects come from, or null while the slot is unfilled.</summary>
        public ObjectSource Source { get => source; set => source = value; }

        /// <summary>What this slot yields, or <see cref="UnityEngine.Object"/> while it is unfilled.</summary>
        public Type ProvidedType => source?.ProvidedType ?? typeof(UnityEngine.Object);

        /// <summary>Appends this slot's objects to <paramref name="results"/>.</summary>
        public void Collect(List<object> results) => source?.Collect(results);

        /// <inheritdoc/>
        /// <remarks>
        /// A slot's own state is its name and which source it points at. The source's contents are that
        /// source's state, published through its own change surface, so the snapshot does not copy them.
        /// </remarks>
        public ObjectSlot CaptureStateSnapshot() => this;

        /// <inheritdoc/>
        public bool StateEquals(in ObjectSlot snapshot) =>
            string.Equals(name, snapshot.name, StringComparison.Ordinal) &&
            ReferenceEquals(source, snapshot.source);
    }
}
