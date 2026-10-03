using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Converts the selection-only highlight shape into the shared presentation used by selection
    /// and <see cref="HighlightModifier"/>, preserving every authored visual value.
    /// </summary>
    internal sealed class SelectionHighlightPresentationMigration : IMigration
    {
        private const string selectableGuid = "22327851aa2916949b07f9d7a33d0f72";
        private readonly string[] tokens = { MigrationTokens.Script(selectableGuid) };

        public string Id => "shape/UniTextSelectable/selectionStyle->selectionHighlight";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            var usedRids = new HashSet<long>();
            foreach (var document in ctx.Documents)
                foreach (var reference in document.ManagedReferences())
                    usedRids.Add(InteractiveEffectYaml.Rid(reference));

            foreach (var document in ctx.Documents)
            {
                if (document.ScriptGuid != selectableGuid) continue;
                var body = document.Body;
                var oldField = body?.Entry("selectionStyle");
                if (oldField == null) continue;

                var providerRid = UnityYaml.AllocateId(usedRids, long.MaxValue - 3_600_000);
                var presentation = InteractiveEffectYaml.ReadHighlightStyle(oldField.Value);
                var pad = UnityYaml.Indent(ctx.Edit.Source, oldField.Start);
                var replacement = new StringBuilder();
                replacement.Append(pad).Append("selectionHighlight:\n");
                HighlightPresentationYaml.Append(replacement, pad + "  ", presentation,
                    providerRid, (int)RangeDecorationOrder.Behind);
                ctx.Edit.Replace(oldField.Start, oldField.End, replacement.ToString());
                AppendProviderReference(ctx, body, providerRid);
            }
        }

        private static void AppendProviderReference(MigrationContext ctx, YamlNode body, long rid)
        {
            var item = new StringBuilder();
            var references = body.Entry("references");
            if (references == null)
            {
                var pad = UnityYaml.Indent(ctx.Edit.Source, body.Entries[0].Start);
                item.Append(pad).Append("references:\n")
                    .Append(pad).Append("  version: 2\n")
                    .Append(pad).Append("  RefIds:\n");
                AppendProviderItem(item, pad + "  ", rid);
                ctx.Edit.Insert(body.End, item.ToString());
                return;
            }

            var refIds = references.Value?.Entry("RefIds")
                ?? throw Broken(ctx, "references has no RefIds field");
            var refPad = UnityYaml.Indent(ctx.Edit.Source, refIds.Start);
            AppendProviderItem(item, refPad, rid);
            if (refIds.Value.Kind == YamlKind.Sequence)
            {
                ctx.Edit.Insert(refIds.Value.End, item.ToString());
                return;
            }
            if (refIds.Value.Scalar != "[]")
                throw Broken(ctx, "RefIds has an invalid YAML shape");
            ctx.Edit.Replace(refIds.Start, refIds.End,
                refPad + "RefIds:\n" + item.ToString());
        }

        private static void AppendProviderItem(StringBuilder output, string pad, long rid)
        {
            output.Append(pad).Append("- rid: ").Append(rid).Append('\n')
                .Append(pad).Append("  type: {class: GlobalSettingsPaintProvider, ns: LightSide, asm: LightSide.UniText}\n")
                .Append(pad).Append("  data: \n");
        }

        private static InvalidOperationException Broken(MigrationContext ctx, string reason)
            => new($"[UniText] Cannot migrate UniTextSelectable selection highlight in " +
                   $"'{ctx.AssetPath}': {reason}.");
    }

    internal static class HighlightPresentationYaml
    {
        public static void Append(StringBuilder output, string pad,
            InteractiveEffectYaml.LegacyHighlightStyle style, long providerRid, int order)
        {
            var geometry = style.boxBreak switch
            {
                1 => (int)GeometryMapping.Line,
                2 => (int)GeometryMapping.Block,
                _ => (int)GeometryMapping.Range,
            };
            output.Append(pad).Append("provider:\n")
                .Append(pad).Append("  rid: ").Append(providerRid).Append('\n')
                .Append(pad).Append("paint:\n")
                .Append(pad).Append("  kind: ").Append(style.paintKind).Append('\n')
                .Append(pad).Append("  color:\n")
                .Append(pad).Append("    serializedVersion: 2\n")
                .Append(pad).Append("    rgba: ").Append(style.rgba).Append('\n')
                .Append(pad).Append("  swatch: ").Append(YamlString(style.swatch)).Append('\n')
                .Append(pad).Append("geometryMapping: ").Append(geometry).Append('\n')
                .Append(pad).Append("paintMapping: 0\n")
                .Append(pad).Append("shape: 0\n")
                .Append(pad).Append("fit: 0\n")
                .Append(pad).Append("angle: NaN\n")
                .Append(pad).Append("scale: NaN\n")
                .Append(pad).Append("paintOffset: {x: NaN, y: NaN}\n")
                .Append(pad).Append("height: ").Append(style.height).Append('\n')
                .Append(pad).Append("padding:\n")
                .Append(pad).Append("  value: {x: ").Append(style.paddingX)
                .Append(", y: ").Append(style.paddingY).Append("}\n")
                .Append(pad).Append("  unit: 2\n")
                .Append(pad).Append("cornerRadius:\n")
                .Append(pad).Append("  value: ").Append(style.radius).Append('\n')
                .Append(pad).Append("  unit: 2\n")
                .Append(pad).Append("mergeThreshold:\n")
                .Append(pad).Append("  value: ").Append(style.mergeThreshold).Append('\n')
                .Append(pad).Append("  unit: 2\n")
                .Append(pad).Append("order: ").Append(order).Append('\n')
                .Append(pad).Append("tint:\n")
                .Append(pad).Append("  serializedVersion: 2\n")
                .Append(pad).Append("  rgba: 4294967295\n")
                .Append(pad).Append("blend: 0\n");
        }

        private static string YamlString(string value)
            => string.IsNullOrEmpty(value) ? "" : "'" + value.Replace("'", "''") + "'";
    }
}
