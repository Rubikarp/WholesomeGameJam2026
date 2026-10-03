using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Removes the retired highlight registry component. Inspector-authored layer presentation is
    /// preserved as direct Styles containing a named MutableRangeSource and HighlightModifier on the
    /// sibling text component; runtime code can populate those sources through normal transactions.
    /// </summary>
    internal sealed class RemoveUniTextHighlightsMigration : IMigration
    {
        private const string legacyGuid = "b9ca3044021c9704092878382d2ddb0a";
        private const string uniTextGuid = "beaa34cb0e58d624bb3a264b28600785";
        private const string uniTextWorldGuid = "f82394fefa9244d49e439daa1fb85977";

        private readonly string[] tokens = { MigrationTokens.Script(legacyGuid) };

        public string Id => "component/UniTextHighlights->RangeSource+HighlightModifier";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            var documents = new Dictionary<long, YamlDocument>();
            var usedRids = new HashSet<long>();
            foreach (var document in ctx.Documents)
            {
                documents[document.FileId] = document;
                foreach (var reference in document.ManagedReferences())
                    usedRids.Add(InteractiveEffectYaml.Rid(reference));
            }

            foreach (var legacy in ctx.Documents)
            {
                if (legacy.ScriptGuid != legacyGuid) continue;
                var body = legacy.Body ?? throw Broken(ctx, legacy, "component body is missing");
                var gameObjectId = UnityYaml.FileId(body["m_GameObject"]);
                if (!documents.TryGetValue(gameObjectId, out var gameObject))
                    throw Broken(ctx, legacy, "GameObject cannot be resolved");
                var text = FindText(gameObject, documents)
                    ?? throw Broken(ctx, legacy, "sibling UniText component cannot be resolved");

                MoveLayers(ctx, body, text.Body, usedRids);
                RemoveComponent(ctx, gameObject.Body, legacy.FileId);
                ctx.Edit.Delete(UnityYaml.DocumentStart(ctx.Edit.Source, legacy), legacy.Root.End);
            }
        }

        private static void MoveLayers(MigrationContext ctx, YamlNode legacy, YamlNode text,
            HashSet<long> usedRids)
        {
            var layers = legacy?["layers"]?["items"];
            if (layers is not { Kind: YamlKind.Sequence, Items: { Count: > 0 } items }) return;

            var stylesEntry = text?["styles"]?.Entry("items")
                ?? throw new InvalidOperationException("UniText styles list is missing.");
            var referencesEntry = text?["references"]?.Entry("RefIds")
                ?? throw new InvalidOperationException("UniText managed-reference list is missing.");
            var stylePad = UnityYaml.Indent(ctx.Edit.Source, stylesEntry.Start);
            var referencePad = UnityYaml.Indent(ctx.Edit.Source, referencesEntry.Start);
            var styles = new StringBuilder();
            var references = new StringBuilder();
            foreach (var layer in items)
            {
                var name = layer?["name"]?.Scalar ?? "custom";
                var priority = Int(layer?["priority"], RangeDecorationPriorities.Custom);
                var order = Int(layer?["order"], 0);
                var sourceRid = UnityYaml.AllocateId(usedRids, long.MaxValue - 3_500_000);
                var style = InteractiveEffectYaml.ReadHighlightStyle(layer?["style"]);
                references.Append(InteractiveEffectYaml.BuildHighlightReference(
                    ctx.Edit.Source, referencesEntry.Start, usedRids, style, priority, order,
                    out var modifierRid));
                AppendSourceReference(references, referencePad, sourceRid, name);
                AppendStyle(styles, stylePad, modifierRid, sourceRid);
            }

            UnityYaml.AppendSequence(ctx, stylesEntry, styles.ToString(),
                "UniText styles list has an invalid YAML shape.");
            UnityYaml.AppendSequence(ctx, referencesEntry, references.ToString(),
                "UniText managed-reference list has an invalid YAML shape.");
        }

        private static void AppendStyle(StringBuilder output, string pad, long modifierRid,
            long sourceRid)
        {
            output.Append(pad).Append("- modifier:\n")
                .Append(pad).Append("    rid: ").Append(modifierRid).Append('\n')
                .Append(pad).Append("  source:\n")
                .Append(pad).Append("    rid: ").Append(sourceRid).Append('\n')
                .Append(pad).Append("  defaultParameter: \n")
                .Append(pad).Append("  disabled: 0\n");
        }

        private static void AppendSourceReference(StringBuilder output, string pad, long rid,
            string identity)
        {
            output.Append(pad).Append("- rid: ").Append(rid).Append('\n')
                .Append(pad).Append("  type: {class: MutableRangeSource, ns: LightSide, asm: LightSide.UniText}\n")
                .Append(pad).Append("  data:\n")
                .Append(pad).Append("    identity: ").Append(YamlString(identity)).Append('\n');
        }

        private static YamlDocument FindText(YamlDocument gameObject,
            IReadOnlyDictionary<long, YamlDocument> documents)
        {
            var components = gameObject.Body?["m_Component"];
            if (components is not { Kind: YamlKind.Sequence, Items: { } items }) return null;
            foreach (var item in items)
            {
                var id = UnityYaml.FileId(item?["component"]);
                if (!documents.TryGetValue(id, out var component)) continue;
                if (component.ScriptGuid == uniTextGuid || component.ScriptGuid == uniTextWorldGuid)
                    return component;
            }
            return null;
        }

        private static void RemoveComponent(MigrationContext ctx, YamlNode gameObject, long componentId)
        {
            var components = gameObject?["m_Component"];
            if (components is not { Kind: YamlKind.Sequence, Items: { } items })
                throw new InvalidOperationException("GameObject component list is missing.");
            foreach (var item in items)
                if (UnityYaml.FileId(item?["component"]) == componentId)
                {
                    ctx.Edit.Delete(item.Start, item.End);
                    return;
                }
            throw new InvalidOperationException(
                $"GameObject does not contain retired component {componentId}.");
        }

        private static int Int(YamlNode node, int fallback)
            => node?.Scalar is { } value && int.TryParse(value, out var result)
                ? result
                : fallback;

        private static string YamlString(string value)
            => "'" + (value ?? "").Replace("'", "''") + "'";

        private static InvalidOperationException Broken(MigrationContext ctx,
            YamlDocument document, string reason)
            => new($"[UniText] Cannot migrate UniTextHighlights {document.FileId} in " +
                   $"'{ctx.AssetPath}': {reason}.");
    }
}
