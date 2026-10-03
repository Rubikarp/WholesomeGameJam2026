using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Stable identity of one node within its modifier graph.</summary>
    [Serializable]
    public struct ModifierNodeId : IEquatable<ModifierNodeId>
    {
        [SerializeField] private ulong high;
        [SerializeField] private ulong low;

        /// <summary>Whether this identity has been assigned.</summary>
        public bool IsValid => high != 0 || low != 0;

        private ModifierNodeId(ulong high, ulong low)
        {
            this.high = high;
            this.low = low;
        }

        internal static ModifierNodeId Create()
        {
            Span<byte> bytes = stackalloc byte[16];
            if (!Guid.NewGuid().TryWriteBytes(bytes))
                throw new InvalidOperationException("A GUID could not be written to its fixed-size buffer.");
            return new ModifierNodeId(BitConverter.ToUInt64(bytes),
                BitConverter.ToUInt64(bytes.Slice(8)));
        }

        /// <inheritdoc/>
        public bool Equals(ModifierNodeId other) => high == other.high && low == other.low;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ModifierNodeId other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(high, low);

        /// <inheritdoc/>
        public override string ToString() => $"{high:x16}{low:x16}";

        /// <summary>Compares two node identities.</summary>
        public static bool operator ==(ModifierNodeId left, ModifierNodeId right) => left.Equals(right);

        /// <summary>Compares two node identities.</summary>
        public static bool operator !=(ModifierNodeId left, ModifierNodeId right) => !left.Equals(right);
    }
}
