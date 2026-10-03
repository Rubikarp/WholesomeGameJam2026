using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serializable identity of a <see cref="Component"/> type. The default value represents
    /// <see cref="Component"/> itself; every other value stores the assembly-qualified type name.
    /// </summary>
    [Serializable]
    public struct ComponentTypeToken : ISerializationCallbackReceiver, IEquatable<ComponentTypeToken>,
        IStateSnapshot<ComponentTypeToken>
    {
        [SerializeField] private string assemblyQualifiedName;

        [NonSerialized] private string resolvedIdentity;
        [NonSerialized] private Type resolvedType;
        [NonSerialized] private Exception resolutionFailure;

        /// <summary>
        /// Creates a token for <paramref name="componentType"/>. A null value selects
        /// <see cref="Component"/>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// <paramref name="componentType"/> is not a closed <see cref="Component"/> type.
        /// </exception>
        public ComponentTypeToken(Type componentType)
        {
            componentType ??= typeof(Component);
            Validate(componentType, nameof(componentType));
            assemblyQualifiedName = componentType == typeof(Component)
                ? null
                : componentType.AssemblyQualifiedName ??
                  throw new ArgumentException(
                      $"Component type '{componentType.FullName}' has no assembly-qualified identity.",
                      nameof(componentType));
            resolvedIdentity = assemblyQualifiedName;
            resolvedType = componentType;
            resolutionFailure = null;
        }

        /// <summary>
        /// The represented component type. Missing identities and identities that no longer name a closed
        /// component type fail here instead of being treated as <see cref="Component"/>.
        /// </summary>
        /// <exception cref="TypeLoadException">The stored identity cannot be resolved.</exception>
        /// <exception cref="InvalidOperationException">
        /// The resolved identity does not name a closed <see cref="Component"/> type.
        /// </exception>
        public Type ComponentType => Resolve();

        /// <inheritdoc/>
        public ComponentTypeToken CaptureStateSnapshot() => new()
        {
            assemblyQualifiedName = Normalize(assemblyQualifiedName)
        };

        /// <inheritdoc/>
        public bool StateEquals(in ComponentTypeToken snapshot) => Equals(snapshot);

        /// <inheritdoc/>
        public bool Equals(ComponentTypeToken other) =>
            string.Equals(Normalize(assemblyQualifiedName), Normalize(other.assemblyQualifiedName),
                StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ComponentTypeToken other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            Normalize(assemblyQualifiedName)?.GetHashCode() ?? 0;

        /// <inheritdoc/>
        public override string ToString() => ComponentType.FullName;

        internal bool TryResolve(out Type type, out string error)
        {
            var identity = Normalize(assemblyQualifiedName);
            if (string.Equals(identity, resolvedIdentity, StringComparison.Ordinal) &&
                resolvedType != null)
            {
                type = resolvedType;
                error = null;
                resolutionFailure = null;
                return true;
            }

            if (identity == null)
            {
                resolvedIdentity = null;
                resolvedType = typeof(Component);
                type = resolvedType;
                error = null;
                resolutionFailure = null;
                return true;
            }

            try
            {
                type = Type.GetType(identity, throwOnError: false);
            }
            catch (Exception failure) when (failure is ArgumentException or TypeLoadException or
                                            System.IO.IOException or BadImageFormatException)
            {
                type = null;
                resolutionFailure = failure;
                error = $"Component type '{identity}' cannot be resolved: {failure.Message}";
                return false;
            }
            if (type == null)
            {
                resolutionFailure = null;
                error = $"Component type '{identity}' cannot be resolved.";
                return false;
            }

            if (!typeof(Component).IsAssignableFrom(type) || type.ContainsGenericParameters)
            {
                resolutionFailure = null;
                error = $"Type '{type.FullName}' is not a closed Unity component type.";
                return false;
            }

            resolvedIdentity = identity;
            resolvedType = type;
            resolutionFailure = null;
            error = null;
            return true;
        }

        private Type Resolve()
        {
            if (TryResolve(out var type, out var error)) return type;
            if (type == null) throw new TypeLoadException(error, resolutionFailure);
            throw new InvalidOperationException(error);
        }

        private static void Validate(Type type, string parameterName)
        {
            if (!typeof(Component).IsAssignableFrom(type) || type.ContainsGenericParameters)
                throw new ArgumentException(
                    $"Type '{type.FullName}' is not a closed Unity component type.", parameterName);
        }

        private static string Normalize(string identity) =>
            string.IsNullOrEmpty(identity) ? null : identity;

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            assemblyQualifiedName = Normalize(assemblyQualifiedName);
            _ = Resolve();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            assemblyQualifiedName = Normalize(assemblyQualifiedName);
            resolvedIdentity = null;
            resolvedType = null;
            resolutionFailure = null;
            _ = Resolve();
        }
    }
}
