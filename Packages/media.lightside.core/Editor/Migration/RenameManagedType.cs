using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Base for the common case: a <c>[SerializeReference]</c> type was renamed and/or moved with the same
    /// serialized field layout. Splices the <c>class</c> (and <c>ns</c>/<c>asm</c> if changed) in the type
    /// header, and the identity prefab instances record for their own references; fields carry over
    /// untouched. Idempotent — the old identity is gone after the first pass.
    /// </summary>
    /// <remarks>For a pure field rename inside the type, prefer <c>[FormerlySerializedAs]</c> (free, and it also reaches AssetBundles).</remarks>
    public abstract class RenameManagedType : IMigration
    {
        readonly TypeSignature from;
        readonly TypeSignature to;
        readonly string[] tokens;

        protected RenameManagedType(TypeSignature from, string toClass, string toNamespace = null, string toAssembly = null)
        {
            this.from = from;
            to = new TypeSignature(toClass, toNamespace ?? from.Namespace, toAssembly ?? from.Assembly);
            tokens = new[] { from.Token };
        }

        public string Id => $"rename/{from.Assembly}/{from.Class}->{to.Assembly}/{to.Class}";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var doc in ctx.Documents)
            {
                foreach (var reference in doc.ManagedReferences())
                {
                    var type = reference["type"];
                    if (type?["class"]?.Scalar != from.Class ||
                        type["ns"]?.Scalar != from.Namespace ||
                        type["asm"]?.Scalar != from.Assembly)
                        continue;

                    Splice(ctx.Edit, type["class"], to.Class);
                    if (to.Namespace != from.Namespace) Splice(ctx.Edit, type["ns"], to.Namespace);
                    if (to.Assembly != from.Assembly) Splice(ctx.Edit, type["asm"], to.Assembly);
                }

                foreach (var identity in doc.ManagedReferenceOverrides())
                    if (identity.Type.Equals(from))
                        ctx.Edit.Replace(identity.Value.Start, identity.Value.End, to.Identity);
            }
        }

        static void Splice(YamlEdit edit, YamlNode node, string value)
        {
            if (node != null) edit.Replace(node.Start, node.End, value);
        }
    }
}
