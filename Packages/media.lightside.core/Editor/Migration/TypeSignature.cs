using System;

namespace LightSide
{
    /// <summary>
    /// The identity a <c>[SerializeReference]</c> managed reference is stored under in YAML:
    /// <c>type: {class: X, ns: Y, asm: Z}</c>. This is what survives in serialized assets after
    /// the C# type is renamed or deleted, so migrations target signatures (data) rather than
    /// <see cref="Type"/>s — the old type usually no longer exists to reflect on.
    /// </summary>
    public readonly struct TypeSignature : IEquatable<TypeSignature>
    {
        /// <summary>Unqualified serialized type name.</summary>
        public readonly string Class;

        /// <summary>Serialized namespace, or an empty string for the global namespace.</summary>
        public readonly string Namespace;

        /// <summary>Serialized assembly name without a file extension.</summary>
        public readonly string Assembly;

        /// <summary>Creates a normalized serialized managed-type identity.</summary>
        public TypeSignature(string cls, string ns, string asm)
        {
            Class = cls ?? "";
            Namespace = ns ?? "";
            Assembly = asm ?? "";
        }

        /// <summary>Builds the serialized identity Unity uses for a managed-reference type.</summary>
        public static TypeSignature Of(Type type) =>
            new(type.Name, type.Namespace ?? "", type.Assembly.GetName().Name);

        /// <summary>Builds the serialized identity Unity uses for <typeparamref name="T"/>.</summary>
        public static TypeSignature Of<T>() => Of(typeof(T));

        /// <summary>Index/target token for this managed type.</summary>
        public string Token => MigrationTokens.ManagedType(Class, Namespace, Assembly);

        /// <summary>
        /// The <c>&lt;asm&gt; &lt;ns&gt;.&lt;class&gt;</c> form a prefab-instance override records for this type.
        /// Instance-owned managed references carry their identity this way instead of a <c>type</c> map.
        /// </summary>
        public string Identity => Namespace.Length == 0
            ? $"{Assembly} {Class}"
            : $"{Assembly} {Namespace}.{Class}";

        /// <summary>Reads an <see cref="Identity"/> string. False when the text is not one.</summary>
        public static bool TryParse(string identity, out TypeSignature signature)
        {
            signature = default;
            if (string.IsNullOrEmpty(identity)) return false;

            var text = identity.Trim();
            var space = text.IndexOf(' ');
            if (space <= 0 || space == text.Length - 1) return false;

            var name = text.Substring(space + 1);
            if (name.IndexOf(' ') >= 0) return false;

            var dot = name.LastIndexOf('.');
            if (dot == 0 || dot == name.Length - 1) return false;

            signature = new TypeSignature(
                dot < 0 ? name : name.Substring(dot + 1),
                dot < 0 ? "" : name.Substring(0, dot),
                text.Substring(0, space));
            return true;
        }

        /// <inheritdoc/>
        public bool Equals(TypeSignature other) =>
            Class == other.Class && Namespace == other.Namespace && Assembly == other.Assembly;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is TypeSignature o && Equals(o);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Class, Namespace, Assembly);

        /// <inheritdoc/>
        public override string ToString() => $"{{class: {Class}, ns: {Namespace}, asm: {Assembly}}}";
    }

    /// <summary>
    /// Opaque discovery tokens. A migration lists the tokens that make an asset a candidate; the index maps
    /// tokens to guids. Managed-type tokens are indexed for every serialized reference and every
    /// prefab-instance override that names one; script, referenced-asset, and prefab-override tokens are
    /// indexed only for identities named by a migration, so the index stays sparse.
    /// </summary>
    public static class MigrationTokens
    {
        /// <summary>Targets assets containing the serialized identity of a managed-reference type.</summary>
        public static string ManagedType(string cls, string ns, string asm) => $"t:{cls}|{ns}|{asm}";

        /// <summary>Targets objects serialized by the script with the supplied asset guid, normalized to lowercase.</summary>
        public static string Script(string guid) => $"s:{guid?.ToLowerInvariant()}";

        /// <summary>Targets assets containing a reference to the supplied asset guid, normalized to lowercase.</summary>
        public static string AssetReference(string guid) => $"r:{guid?.ToLowerInvariant()}";

        /// <summary>Targets assets containing a prefab-instance override of the exact serialized property path.</summary>
        public static string PrefabOverride(string propertyPath) => $"o:{propertyPath}";

        /// <summary>Reports whether a token represents a serialized script.</summary>
        public static bool IsScript(string token) => token.StartsWith("s:", System.StringComparison.Ordinal);

        /// <summary>Extracts the guid carried by a script token.</summary>
        public static string ScriptGuid(string token) => token.Substring(2);

        /// <summary>Reports whether a token represents an asset reference.</summary>
        public static bool IsAssetReference(string token) =>
            token.StartsWith("r:", System.StringComparison.Ordinal);

        /// <summary>Extracts the guid carried by an asset-reference token.</summary>
        public static string AssetReferenceGuid(string token) => token.Substring(2);

        /// <summary>Reports whether a token represents a prefab-instance override path.</summary>
        public static bool IsPrefabOverride(string token) =>
            token.StartsWith("o:", System.StringComparison.Ordinal);

        /// <summary>Extracts the serialized property path carried by a prefab-override token.</summary>
        public static string PrefabOverridePath(string token) => token.Substring(2);

        internal static bool IsValid(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (IsScript(token) || IsAssetReference(token))
            {
                if (token.Length != 34) return false;
                for (int i = 2; i < token.Length; i++)
                    if ((token[i] < '0' || token[i] > '9') && (token[i] < 'a' || token[i] > 'f'))
                        return false;
                return true;
            }

            if (IsPrefabOverride(token))
            {
                var path = PrefabOverridePath(token);
                return path.Length > 0 && path == path.Trim() &&
                       path.IndexOfAny(new[] { '\r', '\n', '|' }) < 0;
            }

            if (!token.StartsWith("t:", StringComparison.Ordinal)) return false;
            var first = token.IndexOf('|', 2);
            if (first <= 2) return false;
            var second = token.IndexOf('|', first + 1);
            if (second < 0 || second == token.Length - 1 || token.IndexOf('|', second + 1) >= 0)
                return false;

            var cls = token.Substring(2, first - 2);
            var ns = token.Substring(first + 1, second - first - 1);
            var asm = token.Substring(second + 1);
            return cls == cls.Trim() && ns == ns.Trim() && asm == asm.Trim();
        }
    }
}
