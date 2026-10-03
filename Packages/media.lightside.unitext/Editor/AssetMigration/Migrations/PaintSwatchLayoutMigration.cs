using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Moves legacy and intermediate <see cref="PaintSwatch"/> layouts into the shared Core paint value.
    /// </summary>
    internal sealed class PaintSwatchLayoutMigration : IMigration
    {
        private const string PaintsScriptGuid = "1a964f6befc74f048aec5302d125507a";

        private static readonly TypeSignature inlineProvider =
            new("InlinePaintProvider", "LightSide", "LightSide.UniText");

        private readonly string[] tokens =
        {
            MigrationTokens.Script(PaintsScriptGuid),
            inlineProvider.Token,
        };

        public string Id => "reshape/LightSide.UniText/PaintSwatch->Paint";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var document in ctx.Documents)
            {
                if (document.ScriptGuid == PaintsScriptGuid)
                    MigrateEntries(document.Body, ctx);

                foreach (var reference in document.ManagedReferences())
                {
                    var type = reference["type"];
                    if (type?["class"]?.Scalar != inlineProvider.Class ||
                        type["ns"]?.Scalar != inlineProvider.Namespace ||
                        type["asm"]?.Scalar != inlineProvider.Assembly)
                        continue;

                    MigrateEntries(reference["data"], ctx);
                }
            }
        }

        private static void MigrateEntries(YamlNode owner, MigrationContext ctx)
        {
            var entries = owner?["entries"];
            var items = entries?["items"] ?? entries;
            if (items is not { Kind: YamlKind.Sequence, Items: { } list }) return;

            foreach (var item in list)
            {
                if (IsOldSwatch(item))
                    ReshapeOld(item, ctx);
                else if (IsIntermediateSwatch(item))
                    WrapIntermediate(item, ctx);
            }
        }

        private static bool IsOldSwatch(YamlNode item)
        {
            if (item is not { Kind: YamlKind.Map, Entries: { Count: 11 } }) return false;

            return item.Entry("name") != null &&
                   item.Entry("kind") != null &&
                   item.Entry("color") != null &&
                   item.Entry("gradient") != null &&
                   item.Entry("texture") != null &&
                   item.Entry("mapping") != null &&
                   item.Entry("shape") != null &&
                   item.Entry("fit") != null &&
                   item.Entry("angle") != null &&
                   item.Entry("scale") != null &&
                   item.Entry("offset") != null;
        }

        private static bool IsIntermediateSwatch(YamlNode item)
        {
            if (item is not { Kind: YamlKind.Map, Entries: { Count: 4 } }) return false;
            return item.Entry("name") != null &&
                   item.Entry("source") != null &&
                   item.Entry("mapping") != null &&
                   item.Entry("projection") != null;
        }

        private static void ReshapeOld(YamlNode item, MigrationContext ctx)
        {
            var kind = item.Entry("kind");
            var color = item.Entry("color");
            var gradient = item.Entry("gradient");
            var texture = item.Entry("texture");
            var shape = item.Entry("shape");
            var fit = item.Entry("fit");
            var angle = item.Entry("angle");
            var scale = item.Entry("scale");
            var offset = item.Entry("offset");

            var kindValue = ReadEnum(ctx, kind, "paint kind", 0, 2);
            var shapeValue = ReadEnum(ctx, shape, "gradient shape", 0, 3);
            var fitValue = ReadEnum(ctx, fit, "texture fit", 0, 4);

            var pad = UnityYaml.Indent(ctx.Edit.Source, kind.Start);
            var nestedPad = pad + "  ";
            var valuePad = nestedPad + "  ";
            var paint = new StringBuilder()
                .Append(pad).Append("paint:\n")
                .Append(nestedPad).Append("source:\n")
                .Append(valuePad).Append("kind: ").Append(kindValue).Append('\n');
            UnityYaml.AppendReindented(ctx.Edit.Slice(color.Start, color.End), 4, paint);
            UnityYaml.AppendReindented(ctx.Edit.Slice(gradient.Start, gradient.End), 4, paint);
            UnityYaml.AppendReindented(ctx.Edit.Slice(texture.Start, texture.End), 4, paint);
            paint.Append(nestedPad).Append("projection:\n")
                .Append(valuePad).Append("kind: ").Append(RemapShape(shapeValue)).Append('\n')
                .Append(valuePad).Append("fit: ").Append(RemapFit(fitValue)).Append('\n');
            UnityYaml.AppendReindented(ctx.Edit.Slice(angle.Start, angle.End), 4, paint);
            UnityYaml.AppendReindented(ctx.Edit.Slice(scale.Start, scale.End), 4, paint);
            UnityYaml.AppendReindented(ctx.Edit.Slice(offset.Start, offset.End), 4, paint);
            paint.Append(nestedPad).Append("blend: 0\n");

            ctx.Edit.Replace(kind.Start, kind.End, paint.ToString());
            ctx.Edit.Delete(color.Start, color.End);
            ctx.Edit.Delete(gradient.Start, gradient.End);
            ctx.Edit.Delete(texture.Start, texture.End);
            ctx.Edit.Delete(shape.Start, shape.End);
            ctx.Edit.Delete(fit.Start, fit.End);
            ctx.Edit.Delete(angle.Start, angle.End);
            ctx.Edit.Delete(scale.Start, scale.End);
            ctx.Edit.Delete(offset.Start, offset.End);
        }

        private static void WrapIntermediate(YamlNode item, MigrationContext ctx)
        {
            var source = item.Entry("source");
            var projection = item.Entry("projection");
            var pad = UnityYaml.Indent(ctx.Edit.Source, source.Start);
            var paint = new StringBuilder().Append(pad).Append("paint:\n");
            UnityYaml.AppendReindented(ctx.Edit.Slice(source.Start, source.End), 2, paint);
            UnityYaml.AppendReindented(ctx.Edit.Slice(projection.Start, projection.End), 2, paint);
            paint.Append(pad).Append("  blend: 0\n");
            ctx.Edit.Replace(source.Start, source.End, paint.ToString());
            ctx.Edit.Delete(projection.Start, projection.End);
        }

        private static int ReadEnum(MigrationContext ctx, YamlEntry entry, string field,
            int minimum, int maximum)
        {
            if (entry.Value is { Kind: YamlKind.Scalar } value &&
                int.TryParse(value.Scalar, out var result) && result >= minimum && result <= maximum)
                return result;

            throw new InvalidOperationException(
                $"[UniText] '{ctx.AssetPath}' contains an invalid legacy PaintSwatch {field}.");
        }

        private static int RemapShape(int value) => value switch
        {
            0 => 0,
            1 => 0,
            2 => 1,
            3 => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };

        private static int RemapFit(int value) => value switch
        {
            0 => 0,
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }
}
