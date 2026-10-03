using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Folds the reveal's former <c>fill</c> fraction and <c>visibleClusters</c> count into the
    /// unit-carrying <c>front</c> parameter: a set cluster count becomes an absolute front, an
    /// unset one carries the fraction over as a percentage.
    /// </summary>
    internal sealed class RevealFrontMigration : IMigration
    {
        private readonly string[] tokens;
        private readonly HashSet<TypeSignature> revealTypes = new();

        public RevealFrontMigration()
        {
            var discovered = new List<string>();
            var signature = TypeSignature.Of(typeof(RevealModifier));
            revealTypes.Add(signature);
            discovered.Add(signature.Token);
            foreach (var type in TypeCache.GetTypesDerivedFrom<RevealModifier>())
            {
                if (type.IsAbstract) continue;
                var derived = TypeSignature.Of(type);
                if (revealTypes.Add(derived)) discovered.Add(derived.Token);
            }
            tokens = discovered.ToArray();
        }

        public string Id => "value/LightSide.UniText/RevealFillVisibleClusters->Front";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var document in ctx.Documents)
            {
                foreach (var reference in document.ManagedReferences())
                {
                    var type = reference["type"];
                    var signature = new TypeSignature(type?["class"]?.Scalar,
                        type?["ns"]?.Scalar, type?["asm"]?.Scalar);
                    if (!revealTypes.Contains(signature)) continue;
                    var data = reference["data"];
                    if (data == null) continue;
                    MigrateReveal(data, ctx);
                }
            }
        }

        private static void MigrateReveal(YamlNode data, MigrationContext ctx)
        {
            var fillEntry = data.Entry("fill");
            var visibleEntry = data.Entry("visibleClusters");
            if (fillEntry?.Value is not { Kind: YamlKind.Scalar } fillValue) return;

            var fill = float.TryParse(fillValue.Scalar, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsedFill) ? parsedFill : 1f;
            var visible = -1;
            if (visibleEntry?.Value is { Kind: YamlKind.Scalar } visibleValue)
                int.TryParse(visibleValue.Scalar, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out visible);

            var value = visible >= 0 ? visible : fill * 100f;
            var unit = visible >= 0 ? 0 : 1;
            var pad = UnityYaml.Indent(ctx.Edit.Source, fillEntry.Start);
            ctx.Edit.Replace(fillEntry.Start, fillEntry.End,
                pad + "front: {value: " +
                value.ToString("R", CultureInfo.InvariantCulture) +
                ", unit: " + unit.ToString(CultureInfo.InvariantCulture) + "}\n");
            if (visibleEntry != null)
                ctx.Edit.Replace(visibleEntry.Start, visibleEntry.End, string.Empty);
        }
    }
}
