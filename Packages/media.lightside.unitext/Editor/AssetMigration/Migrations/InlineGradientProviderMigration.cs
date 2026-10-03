using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// <c>InlineGradientProvider</c> → <c>InlinePaintProvider</c>. Unlike the sibling provider renames the
    /// entry layout changed with it: each inline <c>NamedGradient</c> (a <c>UnityEngine.Gradient</c>) becomes
    /// a <see cref="PaintSwatch"/> with <see cref="Gradient"/> stops, so the items are re-emitted, not
    /// carried over. An invalid legacy gradient fails at the migration boundary because no appearance-preserving
    /// replacement can be inferred.
    /// </summary>
    internal sealed class InlineGradientProviderMigration : IMigration
    {
        static readonly TypeSignature from = new("InlineGradientProvider", "LightSide", "LightSide.UniText");

        readonly string[] tokens = { from.Token };

        public string Id => "rename/LightSide.UniText/InlineGradientProvider->InlinePaintProvider";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var doc in ctx.Documents)
                foreach (var reference in doc.ManagedReferences())
                {
                    var type = reference["type"];
                    if (type?["class"]?.Scalar != from.Class ||
                        type["ns"]?.Scalar != from.Namespace ||
                        type["asm"]?.Scalar != from.Assembly)
                        continue;

                    ctx.Edit.Replace(type["class"].Start, type["class"].End, "InlinePaintProvider");
                    ConvertEntries(ctx, reference["data"]);
                }
        }

        static void ConvertEntries(MigrationContext ctx, YamlNode data)
        {
            var items = data?["entries"]?["items"] ?? data?["entries"];
            if (items is not { Kind: YamlKind.Sequence, Items: { Count: > 0 } list }) return;

            foreach (var item in list)
            {
                var name = item?["name"]?.Scalar;
                if (name == null) continue;

                Gradient gradient = default;
                var gradientEntry = item.Entry("gradient");
                if (gradientEntry != null)
                {
                    var parsed = LegacyGradientYaml.ParseUnityGradientBlock(
                        ctx.Edit.Slice(gradientEntry.Start, gradientEntry.End).Split('\n'), 0, out var mode);
                    if (parsed != null) gradient = LegacyGradientYaml.ToGradient(parsed, mode);
                }
                if (!gradient.IsValid)
                    throw new InvalidOperationException(
                        $"[UniText] '{ctx.AssetPath}' contains an invalid inline gradient '{name}'.");

                ctx.Edit.Replace(item.Start, item.End,
                    BuildSwatch(UnityYaml.Indent(ctx.Edit.Source, item.Start), name, gradient));
            }
        }

        static string BuildSwatch(string dash, string name, Gradient gradient)
        {
            var field = dash + "  ";
            var paintField = field + "  ";
            var sourceField = paintField + "  ";
            var sb = new StringBuilder();
            sb.Append(dash).Append("- name: ").Append(name).Append('\n');
            sb.Append(field).Append("paint:\n");
            sb.Append(paintField).Append("source:\n");

            sb.Append(sourceField).Append("kind: 1\n");
            sb.Append(sourceField).Append("color: {r: 1, g: 1, b: 1, a: 1}\n");
            sb.Append(sourceField).Append("gradient:\n");
            sb.Append(sourceField).Append("  stops:\n");
            foreach (var stop in gradient.Stops)
            {
                sb.Append(sourceField).Append("  - time: ").Append(F(stop.time)).Append('\n');
                sb.Append(sourceField).Append("    color: {r: ").Append(F(stop.color.r))
                  .Append(", g: ").Append(F(stop.color.g))
                  .Append(", b: ").Append(F(stop.color.b))
                  .Append(", a: ").Append(F(stop.color.a)).Append("}\n");
            }
            sb.Append(sourceField).Append("  interpolation: ")
                .Append((byte)gradient.Interpolation).Append('\n');
            sb.Append(sourceField).Append("texture: {fileID: 0}\n");
            sb.Append(paintField).Append("projection:\n");
            sb.Append(sourceField).Append("kind: 0\n");
            sb.Append(sourceField).Append("fit: 0\n");
            sb.Append(sourceField).Append("angle: 0\n");
            sb.Append(sourceField).Append("scale: 1\n");
            sb.Append(sourceField).Append("offset: {x: 0, y: 0}\n");
            sb.Append(paintField).Append("blend: 0\n");
            sb.Append(field).Append("mapping: 0\n");
            return sb.ToString();
        }

        static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    }
}
