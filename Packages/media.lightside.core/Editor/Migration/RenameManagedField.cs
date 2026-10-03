using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Base for the common case: a serialized field key was renamed inside a set of
    /// <c>[SerializeReference]</c> types. Splices the key in the <c>data</c> map of every reference whose
    /// serialized type is covered; values carry over untouched. Idempotent — the old key is gone after the
    /// first pass. Discovery tokens follow from the type set, so a concrete cannot declare a wrong one.
    /// </summary>
    /// <remarks>
    /// Prefer <c>[FormerlySerializedAs]</c> while the declaring type still resolves: it is engine-level,
    /// free, and reaches AssetBundles. This base covers what it cannot — identities the project no longer
    /// declares. A concrete is one line:
    /// <code>
    /// internal sealed class RulesFieldMigration : RenameManagedField
    /// {
    ///     public RulesFieldMigration() : base("effects", "rules", typeof(InteractiveModifier)) { }
    /// }
    /// </code>
    /// </remarks>
    public abstract class RenameManagedField : IMigration
    {
        readonly HashSet<TypeSignature> types = new();
        readonly string[] tokens;
        readonly string from;
        readonly string to;
        readonly string id;

        /// <summary>
        /// Covers <paramref name="baseType"/> when it is concrete plus every non-abstract type deriving from
        /// it, consumer subclasses included, and names the migration after it.
        /// </summary>
        protected RenameManagedField(string from, string to, Type baseType)
            : this(from, to, Concretes(baseType), TypeSignature.Of(baseType)) { }

        /// <summary>
        /// Covers exactly the supplied serialized identities — the form for types the project no longer
        /// declares, and the one that can carry both sides of a type rename. The first identity names the
        /// migration in <see cref="Id"/>.
        /// </summary>
        protected RenameManagedField(string from, string to, params TypeSignature[] types)
            : this(from, to, types, types is { Length: > 0 } ? types[0] : default) { }

        RenameManagedField(string from, string to, TypeSignature[] types, TypeSignature owner)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                throw new ArgumentException(
                    "A managed field rename needs both the old and the new serialized key.");
            if (types == null || types.Length == 0)
                throw new ArgumentException(
                    "A managed field rename needs at least one serialized type.", nameof(types));

            this.from = from;
            this.to = to;
            foreach (var type in types) this.types.Add(type);

            tokens = new string[this.types.Count];
            var next = 0;
            foreach (var type in this.types) tokens[next++] = type.Token;
            id = $"field/{owner.Assembly}/{owner.Class}.{from}->{to}";
        }

        public string Id => id;
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var doc in ctx.Documents)
                foreach (var reference in doc.ManagedReferences())
                {
                    var type = reference["type"];
                    var signature = new TypeSignature(
                        type?["class"]?.Scalar, type?["ns"]?.Scalar, type?["asm"]?.Scalar);
                    if (!types.Contains(signature)) continue;

                    var entry = reference["data"]?.Entry(from);
                    if (entry != null) ctx.Edit.Replace(entry.KeyStart, entry.KeyEnd, to);
                }
        }

        static TypeSignature[] Concretes(Type baseType)
        {
            if (baseType == null) throw new ArgumentNullException(nameof(baseType));

            var result = new List<TypeSignature>();
            if (!baseType.IsAbstract) result.Add(TypeSignature.Of(baseType));
            foreach (var derived in TypeCache.GetTypesDerivedFrom(baseType))
                if (!derived.IsAbstract) result.Add(TypeSignature.Of(derived));
            return result.ToArray();
        }
    }
}
