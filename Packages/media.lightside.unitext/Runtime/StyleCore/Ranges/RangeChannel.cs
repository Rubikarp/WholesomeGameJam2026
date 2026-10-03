using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Stable project asset used to route range entities and their typed payloads.</summary>
    [CreateAssetMenu(fileName = "RangeChannel", menuName = UniTextMenu.CreateAsset.RangeChannel)]
    public sealed partial class RangeChannel : ScriptableObject
    {
        [StateField(nameof(ApplyPayloadTypeNameChange))]
        [SerializeField, HideInInspector] private string payloadTypeName;

        [NonSerialized] private string cachedTypeName;
        [NonSerialized] private Type cachedType;

        /// <summary>Required payload type, or null when this channel carries no payload.</summary>
        public Type PayloadType
        {
            get
            {
                if (string.IsNullOrEmpty(payloadTypeName)) return null;
                if (cachedType != null && cachedTypeName == payloadTypeName) return cachedType;

                var resolved = Type.GetType(payloadTypeName, false);
                if (resolved == null)
                    throw new InvalidOperationException(
                        $"Range channel '{name}' references unavailable payload type '{payloadTypeName}'.");
                cachedTypeName = payloadTypeName;
                cachedType = resolved;
                return resolved;
            }
        }

        /// <summary>Sets the payload contract stored by this asset. Open generic types are invalid.</summary>
        public void SetPayloadType(Type type)
        {
            if (type != null && type.ContainsGenericParameters)
                throw new ArgumentException("Open generic payload types are not supported.", nameof(type));

            SetPayloadTypeNameState(type?.AssemblyQualifiedName);
            cachedTypeName = payloadTypeName;
            cachedType = type;
        }

        private void ApplyPayloadTypeNameChange()
        {
            cachedTypeName = null;
            cachedType = null;
        }

        /// <summary>Validates this asset's contract and returns its typed runtime facade.</summary>
        public RangeChannel<T> As<T>() => new(this);

        internal void ValidatePayload(object payload)
        {
            var required = PayloadType;
            if (required == null)
            {
                if (payload != null)
                    throw new InvalidOperationException(
                        $"Range channel '{name}' does not declare a payload type.");
                return;
            }

            if (payload == null || !required.IsInstanceOfType(payload))
                throw new InvalidOperationException(payload == null
                    ? $"Range channel '{name}' requires payload type {required.FullName}."
                    : $"Range channel '{name}' requires {required.FullName}, received {payload.GetType().FullName}.");
        }

    }

    /// <summary>Typed runtime facade over a serialized <see cref="RangeChannel"/> asset.</summary>
    public readonly struct RangeChannel<T>
    {
        /// <summary>The serialized channel asset shared with Inspector-authored configuration.</summary>
        public RangeChannel Untyped { get; }

        internal RangeChannel(RangeChannel channel)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            var payloadType = channel.PayloadType;
            if (payloadType != typeof(T))
                throw new InvalidOperationException(
                    $"Range channel '{channel.name}' carries {payloadType?.FullName ?? "no payload"}, requested {typeof(T).FullName}.");
            Untyped = channel;
        }
    }
}
