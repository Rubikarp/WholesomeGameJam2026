using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// One-time v2.x data fix: <c>weight: 700</c> inside a BoldModifier used to be the implicit Auto default;
    /// the Auto sentinel is now <c>0</c>. Non-idempotent — after the fix an explicit 700 is a real value, so a
    /// second pass would wrongly turn it into Auto. Gated to exactly one run by the completion stamp.
    /// </summary>
    internal sealed class BoldWeightSentinelMigration : IMigration
    {
        static readonly TypeSignature bold = new("BoldModifier", "LightSide", "LightSide.UniText");
        readonly string[] tokens = { bold.Token };

        public string Id => "value/BoldModifier.weight/700->auto";
        public bool Idempotent => false;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var doc in ctx.Documents)
                foreach (var reference in doc.ManagedReferences())
                {
                    if (reference["type"]?["class"]?.Scalar != "BoldModifier") continue;
                    var weight = reference["data"]?["weight"];
                    if (weight?.Scalar == "700")
                        ctx.Edit.Replace(weight.Start, weight.End, "0");
                }
        }
    }
}
