using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Base for the hard case: fields shared by a <c>[SerializeReference]</c> base type were extracted into
    /// a nested <c>[Serializable]</c> struct field. Moves those keys from a reference's <c>data</c> map one
    /// indent deeper under <see cref="StructField"/>, in place. Authored once against the base:
    /// <see cref="Tokens"/> covers every concrete type in that hierarchy via reflection, and
    /// <see cref="FieldNames"/> is the struct's own field set, so only the base's fields move and each
    /// concrete's own fields stay at the top level.
    /// </summary>
    /// <remarks>
    /// A concrete is a few lines:
    /// <code>
    /// internal sealed class ShadowFieldsToStruct : MoveFieldsToStruct
    /// {
    ///     protected override Type BaseType => typeof(ShadowEffectBase);
    ///     protected override string StructField => "shadow";
    ///     protected override HashSet&lt;string&gt; FieldNames => new() { "offset", "blur", "tint" };
    /// }
    /// </code>
    /// </remarks>
    public abstract class MoveFieldsToStruct : IMigration
    {
        readonly string[] tokens;
        readonly HashSet<TypeSignature> targetSignatures = new();

        protected MoveFieldsToStruct()
        {
            var list = new List<string>();
            var baseType = BaseType;
            if (!baseType.IsAbstract) Include(baseType, list);
            foreach (var derived in TypeCache.GetTypesDerivedFrom(baseType))
                if (!derived.IsAbstract) Include(derived, list);
            tokens = list.ToArray();
        }

        void Include(Type type, List<string> collected)
        {
            var signature = TypeSignature.Of(type);
            if (targetSignatures.Add(signature)) collected.Add(signature.Token);
        }

        protected abstract Type BaseType { get; }
        protected abstract string StructField { get; }
        protected abstract HashSet<string> FieldNames { get; }

        public string Id => $"reshape/{BaseType.Name}->{StructField}";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var doc in ctx.Documents)
                foreach (var reference in doc.ManagedReferences())
                {
                    var type = reference["type"];
                    var sig = new TypeSignature(type?["class"]?.Scalar, type?["ns"]?.Scalar, type?["asm"]?.Scalar);
                    if (targetSignatures.Contains(sig))
                        Reshape(reference, ctx.Edit);
                }
        }

        void Reshape(YamlNode reference, YamlEdit edit)
        {
            var data = reference["data"];
            if (data?.Entries == null) return;

            var moved = new List<YamlEntry>();
            foreach (var e in data.Entries)
                if (FieldNames.Contains(e.Key)) moved.Add(e);
            if (moved.Count == 0) return;

            var first = moved[0];
            int indent = first.KeyStart - first.Start;

            var block = new StringBuilder();
            block.Append(' ', indent).Append(StructField).Append(":\n");
            foreach (var e in moved)
                UnityYaml.AppendReindented(edit.Slice(e.Start, e.End), 2, block);

            edit.Replace(first.Start, first.End, block.ToString());
            for (int i = 1; i < moved.Count; i++)
                edit.Delete(moved[i].Start, moved[i].End);
        }

    }
}
