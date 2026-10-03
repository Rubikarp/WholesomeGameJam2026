using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Optional allocation-free lookup surface for payload types that want named Inspector bindings
    /// without reflection. Returning false is a binding failure rather than an empty value.
    /// </summary>
    public interface IRangePayloadValues
    {
        /// <summary>Tries to read one stable named value from this immutable payload snapshot.</summary>
        bool TryGetRangeValue(string name, out object value);
    }

    /// <summary>Source used by a serialized value that may be constant or payload-driven.</summary>
    public enum RangeValueSource : byte
    {
        /// <summary>Use the value authored on the modifier, effect or action.</summary>
        Authored,
        /// <summary>Read a named member from the range entity's typed payload.</summary>
        PayloadMember,
    }

    /// <summary>
    /// Resolves stable, dot-separated payload paths for semantic, presentation and action bindings.
    /// Payloads may implement <see cref="IRangePayloadValues"/>, expose a string/object dictionary,
    /// or expose public instance fields and non-indexed properties.
    /// </summary>
    public static class RangePayloadBinding
    {
        private readonly struct MemberKey : IEquatable<MemberKey>
        {
            private readonly Type type;
            private readonly string name;

            public MemberKey(Type type, string name)
            {
                this.type = type;
                this.name = name;
            }

            public bool Equals(MemberKey other) => type == other.type && name == other.name;
            public override bool Equals(object obj) => obj is MemberKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(type, name);
        }

        private readonly struct MemberAccessor
        {
            private readonly FieldInfo field;
            private readonly PropertyInfo property;

            public MemberAccessor(FieldInfo field, PropertyInfo property)
            {
                this.field = field;
                this.property = property;
            }

            public bool IsValid => field != null || property != null;
            public object Read(object owner) => field != null ? field.GetValue(owner) : property.GetValue(owner);
        }

        private static readonly Dictionary<MemberKey, MemberAccessor> members = new();
        private static readonly object sync = new();

        /// <summary>Reads a named payload path and requires its final value to be compatible with <typeparamref name="T"/>.</summary>
        public static T Read<T>(RangePayloadView payload, string path)
            => (T)Read(payload, path, typeof(T));

        /// <summary>Reads a named payload path and converts its final value to text.</summary>
        public static string ReadString(RangePayloadView payload, string path)
        {
            var value = ReadObject(payload, path);
            if (value == null)
                throw new InvalidOperationException(
                    $"Payload binding '{path}' resolved null; a string representation is required.");
            return value as string ?? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static object Read(RangePayloadView payload, string path, Type expectedType)
        {
            if (expectedType == null) throw new ArgumentNullException(nameof(expectedType));
            var value = ReadObject(payload, path);
            if (value != null && expectedType.IsInstanceOfType(value)) return value;
            throw new InvalidOperationException(value == null
                ? $"Payload binding '{path}' resolved null; {expectedType.FullName} is required."
                : $"Payload binding '{path}' is {value.GetType().FullName}; {expectedType.FullName} is required.");
        }

        private static object ReadObject(RangePayloadView payload, string path)
        {
            if (!payload.HasValue)
                throw new InvalidOperationException($"Payload binding '{path}' requires a payload.");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Payload member path is empty.", nameof(path));

            var current = payload.Value;
            var start = 0;
            while (start < path.Length)
            {
                var separator = path.IndexOf('.', start);
                var length = (separator < 0 ? path.Length : separator) - start;
                if (length == 0)
                    throw new ArgumentException($"Payload member path '{path}' contains an empty segment.", nameof(path));
                var name = path.Substring(start, length);
                current = ReadMember(current, name, path);
                if (separator < 0) return current;
                if (current == null)
                    throw new InvalidOperationException(
                        $"Payload binding '{path}' reached null at '{name}'.");
                start = separator + 1;
            }
            throw new InvalidOperationException($"Payload binding '{path}' could not be resolved.");
        }

        private static object ReadMember(object owner, string name, string path)
        {
            if (owner is IRangePayloadValues values)
            {
                if (values.TryGetRangeValue(name, out var value)) return value;
                throw new InvalidOperationException(
                    $"Payload {owner.GetType().FullName} does not publish named value '{name}' for binding '{path}'.");
            }
            if (owner is IReadOnlyDictionary<string, object> readOnly)
            {
                if (readOnly.TryGetValue(name, out var value)) return value;
                throw new InvalidOperationException($"Payload dictionary has no key '{name}' for binding '{path}'.");
            }
            if (owner is IDictionary<string, object> dictionary)
            {
                if (dictionary.TryGetValue(name, out var value)) return value;
                throw new InvalidOperationException($"Payload dictionary has no key '{name}' for binding '{path}'.");
            }

            var type = owner.GetType();
            var key = new MemberKey(type, name);
            MemberAccessor accessor;
            lock (sync)
            {
                if (!members.TryGetValue(key, out accessor))
                {
                    var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
                    var property = field == null
                        ? type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                        : null;
                    if (property is { CanRead: true } && property.GetIndexParameters().Length != 0)
                        property = null;
                    accessor = new MemberAccessor(field, property is { CanRead: true } ? property : null);
                    members.Add(key, accessor);
                }
            }
            if (!accessor.IsValid)
                throw new InvalidOperationException(
                    $"Payload {type.FullName} has no public readable member '{name}' for binding '{path}'.");
            return accessor.Read(owner);
        }
    }

    /// <summary>Optional named payload override for one authored string value.</summary>
    [Serializable]
    [TypeDescription("Payload string: bind a semantic or action value by member name")]
    public sealed partial class RangePayloadStringBinding : RangeConfigurationObject
    {
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private string member;

        /// <summary>Resolves the configured member and converts it to invariant text.</summary>
        public string Resolve(RangePayloadView payload) => RangePayloadBinding.ReadString(payload, member);
    }
}
