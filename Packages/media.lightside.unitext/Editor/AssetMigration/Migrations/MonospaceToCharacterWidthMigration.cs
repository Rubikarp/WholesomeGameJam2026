using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Rehomes the retired <c>monospace</c> flag of LetterSpacingModifier: every style whose flag was
    /// set keeps its tracking and gains a CharacterWidthModifier of measured width over the same
    /// range — a sibling style entry under a <c>cwidth</c> tag for rule-driven entries, a sibling of
    /// the same source for whole-text ones, an extra child for a modifier nested in a composite.
    /// Markup that carried the flag in the second <c>cspace</c> slot is authored data and keeps its
    /// stale token; <c>&lt;cwidth=auto&gt;</c> replaces it by hand.
    /// </summary>
    internal sealed class MonospaceToCharacterWidthMigration : IMigration
    {
        static readonly TypeSignature letterSpacing =
            new("LetterSpacingModifier", "LightSide", "LightSide.UniText");

        /// <summary>The rid Unity serializes for a null managed reference.</summary>
        const long NullReference = -2;

        readonly string[] tokens = { letterSpacing.Token };

        public string Id => "field/LetterSpacingModifier.monospace->CharacterWidthModifier";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            var usedRids = UsedRids(ctx.Documents);

            foreach (var document in ctx.Documents)
            {
                var body = document.Body;
                var referenceList = body?["references"]?.Entry("RefIds");
                if (referenceList?.Value is not { Kind: YamlKind.Sequence, Items: { Count: > 0 } })
                    continue;

                var flagged = Flagged(ctx, document);
                if (flagged.Count == 0) continue;

                var pad = UnityYaml.Indent(ctx.Edit.Source, referenceList.Value.Items[0].Start);
                var addedReferences = new StringBuilder();

                foreach (var spacingRid in flagged)
                {
                    var widthRid = UnityYaml.AllocateId(usedRids, long.MaxValue);
                    AppendCharacterWidth(addedReferences, pad, widthRid);
                    Attach(ctx, document, body, spacingRid, widthRid, usedRids, pad, addedReferences);
                }

                UnityYaml.AppendSequence(ctx, referenceList, addedReferences.ToString(),
                    "Serialized reference list is missing.",
                    "Serialized reference list is not a sequence.");
            }
        }

        /// <summary>Deletes every set <c>monospace</c> line and reports the modifiers that carried one.</summary>
        static List<long> Flagged(MigrationContext ctx, YamlDocument document)
        {
            var result = new List<long>();
            foreach (var reference in document.ManagedReferences())
            {
                if (reference["type"]?["class"]?.Scalar != letterSpacing.Class) continue;
                var flag = reference["data"]?.Entry("monospace");
                if (flag?.Value?.Scalar != "1") continue;

                ctx.Edit.Delete(flag.Start, flag.End);
                result.Add(Rid(reference));
            }
            return result;
        }

        /// <summary>
        /// Gives the new modifier the reach the flag had: a copy of the owning style entry, or a place
        /// beside its siblings when the flagged modifier is a composite's child.
        /// </summary>
        static void Attach(MigrationContext ctx, YamlDocument document, YamlNode body,
            long spacingRid, long widthRid, HashSet<long> usedRids, string pad,
            StringBuilder addedReferences)
        {
            var styles = body?["styles"]?.Entry("items");
            if (styles?.Value is { Kind: YamlKind.Sequence, Items: { } entries })
                foreach (var entry in entries)
                {
                    if (Rid(entry["modifier"]) != spacingRid) continue;

                    var sourceRid = Rid(entry["source"]);
                    if (sourceRid >= 0)
                    {
                        sourceRid = UnityYaml.AllocateId(usedRids, long.MaxValue);
                        AppendTagRule(addedReferences, pad, sourceRid);
                    }
                    else sourceRid = NullReference;

                    var itemPad = UnityYaml.Indent(ctx.Edit.Source, entry.Start);
                    ctx.Edit.Insert(entry.End, new StringBuilder()
                        .Append(itemPad).Append("- modifier:\n")
                        .Append(itemPad).Append("    rid: ").Append(widthRid).Append('\n')
                        .Append(itemPad).Append("  source:\n")
                        .Append(itemPad).Append("    rid: ").Append(sourceRid).Append('\n')
                        .Append(itemPad).Append("  defaultParameter: \n")
                        .Append(itemPad).Append("  disabled: ")
                        .Append(entry["disabled"]?.Scalar ?? "0").Append('\n')
                        .ToString());
                    return;
                }

            foreach (var reference in document.ManagedReferences())
            {
                var children = reference["data"]?["modifiers"]?.Entry("items");
                if (children?.Value is not { Kind: YamlKind.Sequence, Items: { } items }) continue;

                foreach (var item in items)
                {
                    if (Rid(item) != spacingRid) continue;
                    var itemPad = UnityYaml.Indent(ctx.Edit.Source, item.Start);
                    ctx.Edit.Insert(item.End, itemPad + "- rid: " + widthRid + "\n");
                    return;
                }
            }

            throw new InvalidOperationException(
                $"[UniText] Cannot migrate monospace in '{ctx.AssetPath}': letter-spacing modifier " +
                $"{spacingRid} of object {document.FileId} belongs to no style entry or modifier list.");
        }

        static void AppendCharacterWidth(StringBuilder output, string pad, long rid) =>
            output.Append(pad).Append("- rid: ").Append(rid).Append('\n')
                .Append(pad).Append("  type: {class: CharacterWidthModifier, ns: LightSide, asm: LightSide.UniText}\n")
                .Append(pad).Append("  data:\n")
                .Append(pad).Append("    width:\n")
                .Append(pad).Append("      value: 0\n")
                .Append(pad).Append("      unit: 0\n");

        static void AppendTagRule(StringBuilder output, string pad, long rid) =>
            output.Append(pad).Append("- rid: ").Append(rid).Append('\n')
                .Append(pad).Append("  type: {class: TagRule, ns: LightSide, asm: LightSide.UniText}\n")
                .Append(pad).Append("  data:\n")
                .Append(pad).Append("    tagName: cwidth\n")
                .Append(pad).Append("    defaultParameter: \n");

        static HashSet<long> UsedRids(IReadOnlyList<YamlDocument> documents)
        {
            var result = new HashSet<long>();
            foreach (var document in documents)
                foreach (var reference in document.ManagedReferences())
                    result.Add(Rid(reference));
            return result;
        }

        static long Rid(YamlNode node) =>
            node?["rid"]?.Scalar is { } value && long.TryParse(value, out var rid) ? rid : long.MinValue;
    }
}
