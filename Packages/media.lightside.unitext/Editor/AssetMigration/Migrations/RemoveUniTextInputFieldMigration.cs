using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Retires the pre-release UniTextInputField component without leaving missing scripts: decorators move
    /// into the referenced editable's behavior list, cancel-to-defocus remains an explicit behavior, the
    /// editing surface keeps its authored stretch geometry, and the former box is expressed through standard
    /// Unity layout components. A non-zero legacy height cap becomes LayoutElement.maxHeight.
    /// </summary>
    internal sealed class RemoveUniTextInputFieldMigration : IMigration
    {
        const string legacyScriptGuid = "ef56a43311727924f90db00586705ff6";
        const string editableScriptGuid = "3cc1333b5b590af44b4c869dab470954";
        const string layoutElementScriptGuid = "306cc8c2b49d7114eaa3623786fc2126";
        const string verticalLayoutGroupScriptGuid = "59f8146938fff824cb5fd77236b75775";

        readonly string[] tokens = { MigrationTokens.Script(legacyScriptGuid) };

        public string Id => "component/UniTextInputField->UniTextEditable";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            var documents = ById(ctx.Documents);
            var usedIds = UsedIds(ctx.Documents);
            var usedRids = UsedRids(ctx.Documents);
            var migratedEditors = new HashSet<long>();
            var appendedDocuments = new StringBuilder();

            foreach (var wrapper in ctx.Documents)
            {
                if (wrapper.ScriptGuid != legacyScriptGuid || wrapper.Stripped) continue;

                var body = wrapper.Body;
                var editorReference = body?["editor"];
                var editorId = UnityYaml.FileId(editorReference);
                var boxId = UnityYaml.FileId(body?["m_GameObject"]);
                if (editorId == 0)
                    throw Broken(ctx, wrapper, "its editor reference is missing");
                if (editorReference?["guid"] != null)
                    throw Broken(ctx, wrapper, $"its editor reference {editorId} is external");
                if (!documents.TryGetValue(editorId, out var editor))
                    throw Broken(ctx, wrapper, $"its editor reference {editorId} does not resolve locally");
                if (editor.ScriptGuid != editableScriptGuid)
                    throw Broken(ctx, wrapper,
                        $"its editor reference {editorId} resolves to script {editor.ScriptGuid ?? "<none>"}, not UniTextEditable");
                if (!documents.TryGetValue(boxId, out var box) || box.ObjectType != "GameObject")
                    throw Broken(ctx, wrapper, "its field-box GameObject cannot be resolved");
                if (!migratedEditors.Add(editorId))
                    throw Broken(ctx, wrapper, "more than one retired field points to the same UniTextEditable");

                MoveBehaviors(ctx, body, editor.Body, usedRids);
                StretchEditor(ctx, editor, documents);
                ComposeLayout(ctx, body, box, editor, documents, usedIds, appendedDocuments);
                RemoveComponent(ctx, box, wrapper.FileId);
                ctx.Edit.Delete(UnityYaml.DocumentStart(ctx.Edit.Source, wrapper), wrapper.Root.End);
            }

            if (appendedDocuments.Length > 0)
            {
                if (ctx.Edit.Source.Length > 0 && ctx.Edit.Source[^1] != '\n')
                    appendedDocuments.Insert(0, '\n');
                ctx.Edit.Insert(ctx.Edit.Source.Length, appendedDocuments.ToString());
            }
        }

        static void MoveBehaviors(MigrationContext ctx, YamlNode wrapper, YamlNode editor, HashSet<long> usedRids)
        {
            var decoratorItems = wrapper?["decorators"]?["items"];
            var wrapperReferences = ReferencesByRid(wrapper);
            var addedItems = new StringBuilder();
            var addedReferences = new StringBuilder();

            if (decoratorItems is { Kind: YamlKind.Sequence, Items: { } items })
                foreach (var item in items)
                {
                    var oldRid = Rid(item);
                    if (!wrapperReferences.TryGetValue(oldRid, out var reference))
                        throw new InvalidOperationException($"Decorator rid {oldRid} has no serialized reference.");

                    var newRid = UnityYaml.AllocateId(usedRids, long.MaxValue);
                    addedItems.Append("    - rid: ").Append(newRid).Append('\n');
                    addedReferences.Append(RetypeRid(ctx, reference, newRid));
                }

            var defocusRid = UnityYaml.AllocateId(usedRids, long.MaxValue);
            addedItems.Append("    - rid: ").Append(defocusRid).Append('\n');
            addedReferences.Append("    - rid: ").Append(defocusRid).Append('\n')
                .Append("      type: {class: DefocusOnCancelBehavior, ns: LightSide, asm: LightSide.UniText}\n")
                .Append("      data:\n");

            UnityYaml.AppendSequence(ctx, editor?["behaviors"]?.Entry("items"),
                addedItems.ToString(), "Expected serialized list is missing.",
                "Expected serialized list is not a sequence.");
            UnityYaml.AppendSequence(ctx, editor?["references"]?.Entry("RefIds"),
                addedReferences.ToString(), "Expected serialized list is missing.",
                "Expected serialized list is not a sequence.");
        }

        static void StretchEditor(MigrationContext ctx, YamlDocument editor,
            IReadOnlyDictionary<long, YamlDocument> documents)
        {
            var editorObjectId = UnityYaml.FileId(editor.Body?["m_GameObject"]);
            if (!documents.TryGetValue(editorObjectId, out var editorObject))
                throw Broken(ctx, editor, "its GameObject cannot be resolved");

            var rect = FindComponent(editorObject, documents, 224);
            if (rect == null) throw Broken(ctx, editor, "its RectTransform cannot be resolved");
            var body = rect.Body;
            Replace(ctx, body?["m_AnchorMin"], "{x: 0, y: 0}");
            Replace(ctx, body?["m_AnchorMax"], "{x: 1, y: 1}");
            Replace(ctx, body?["m_SizeDelta"], "{x: 0, y: 0}");
            Replace(ctx, body?["m_Pivot"], "{x: 0, y: 1}");
        }

        static void ComposeLayout(MigrationContext ctx, YamlNode legacy, YamlDocument box,
            YamlDocument editor, IReadOnlyDictionary<long, YamlDocument> documents, HashSet<long> usedIds,
            StringBuilder appendedDocuments)
        {
            var boxBody = box.Body;
            var editorObjectId = UnityYaml.FileId(editor.Body?["m_GameObject"]);
            var boxRect = FindComponent(box, documents, 224);
            if (boxRect == null) throw Broken(ctx, box, "its RectTransform cannot be resolved");

            var editorRect = FindComponent(documents[editorObjectId], documents, 224);
            bool editorIsDirectChild = UnityYaml.FileId(editorRect.Body?["m_Father"]) == boxRect.FileId;
            var components = Components(box, documents);
            bool hasLayoutGroup = false;
            YamlDocument layoutElement = null;
            foreach (var component in components)
            {
                var componentBody = component.Body;
                var type = componentBody?["m_EditorClassIdentifier"]?.Scalar;
                if (type != null && type.EndsWith("LayoutGroup", StringComparison.Ordinal))
                    hasLayoutGroup = true;
                if (component.ScriptGuid == layoutElementScriptGuid)
                    layoutElement = component;
            }

            var newComponentIds = new List<long>();
            if (!hasLayoutGroup && editorIsDirectChild)
            {
                var layoutId = UnityYaml.AllocateId(usedIds, long.MaxValue);
                newComponentIds.Add(layoutId);
                AppendVerticalLayoutGroup(appendedDocuments, layoutId, box.FileId);
                IgnoreOverlayChildren(ctx, boxRect, editorObjectId, documents, usedIds,
                    appendedDocuments);
            }

            var maxHeight = legacy?["maxHeight"]?.Scalar;
            if (maxHeight != null && maxHeight != "0" && maxHeight != "0.0")
            {
                if (layoutElement != null)
                    SetMaxHeight(ctx, layoutElement.Body, maxHeight);
                else
                {
                    var layoutElementId = UnityYaml.AllocateId(usedIds, long.MaxValue);
                    newComponentIds.Add(layoutElementId);
                    AppendLayoutElement(appendedDocuments, layoutElementId, box.FileId, false, maxHeight);
                }
            }

            AppendComponents(ctx, boxBody?.Entry("m_Component"), newComponentIds);
        }

        static void IgnoreOverlayChildren(MigrationContext ctx, YamlDocument boxRect, long editorObjectId,
            IReadOnlyDictionary<long, YamlDocument> documents, HashSet<long> usedIds,
            StringBuilder appendedDocuments)
        {
            var children = boxRect.Body?["m_Children"];
            if (children is not { Kind: YamlKind.Sequence, Items: { } items }) return;

            foreach (var item in items)
            {
                var childRectId = UnityYaml.FileId(item);
                if (!documents.TryGetValue(childRectId, out var childRect)) continue;
                var childObjectId = UnityYaml.FileId(childRect.Body?["m_GameObject"]);
                if (childObjectId == editorObjectId) continue;
                if (!documents.TryGetValue(childObjectId, out var childObject)) continue;

                var existing = FindScript(childObject, documents, layoutElementScriptGuid);
                if (existing != null)
                {
                    Replace(ctx, existing.Body?["m_IgnoreLayout"], "1");
                    continue;
                }

                var id = UnityYaml.AllocateId(usedIds, long.MaxValue);
                AppendComponents(ctx, childObject.Body?.Entry("m_Component"), new[] { id });
                AppendLayoutElement(appendedDocuments, id, childObjectId, true, "-1");
            }
        }

        static void SetMaxHeight(MigrationContext ctx, YamlNode body, string maxHeight)
        {
            var existing = body?.Entry("m_MaxHeight");
            if (existing != null)
            {
                Replace(ctx, existing.Value, maxHeight);
                return;
            }

            var after = body?.Entry("m_PreferredHeight") ?? body?.Entry("m_MinHeight");
            if (after == null) throw new InvalidOperationException("LayoutElement has no height fields.");
            ctx.Edit.Insert(after.End, "  m_MaxHeight: " + maxHeight + "\n");
        }

        static void AppendComponents(MigrationContext ctx, YamlEntry entry, IReadOnlyList<long> ids)
        {
            if (ids.Count == 0) return;
            if (entry?.Value is not { Kind: YamlKind.Sequence } components)
                throw new InvalidOperationException("GameObject component list is missing.");
            var added = new StringBuilder();
            for (var i = 0; i < ids.Count; i++)
                added.Append("  - component: {fileID: ").Append(ids[i]).Append("}\n");
            ctx.Edit.Insert(components.End, added.ToString());
        }

        static void RemoveComponent(MigrationContext ctx, YamlDocument gameObject, long componentId)
        {
            var components = gameObject.Body?["m_Component"];
            if (components is not { Kind: YamlKind.Sequence, Items: { } items })
                throw Broken(ctx, gameObject, "its component list is missing");
            foreach (var item in items)
                if (UnityYaml.FileId(item?["component"]) == componentId)
                {
                    ctx.Edit.Delete(item.Start, item.End);
                    return;
                }
            throw Broken(ctx, gameObject, $"component {componentId} is absent from its GameObject");
        }

        static Dictionary<long, YamlDocument> ById(IReadOnlyList<YamlDocument> documents)
        {
            var result = new Dictionary<long, YamlDocument>();
            foreach (var document in documents) result[document.FileId] = document;
            return result;
        }

        static List<YamlDocument> Components(YamlDocument gameObject,
            IReadOnlyDictionary<long, YamlDocument> documents)
        {
            var result = new List<YamlDocument>();
            var components = gameObject.Body?["m_Component"];
            if (components is not { Kind: YamlKind.Sequence, Items: { } items }) return result;
            foreach (var item in items)
                if (documents.TryGetValue(UnityYaml.FileId(item?["component"]), out var component))
                    result.Add(component);
            return result;
        }

        static YamlDocument FindComponent(YamlDocument gameObject,
            IReadOnlyDictionary<long, YamlDocument> documents, long classId)
        {
            foreach (var component in Components(gameObject, documents))
                if (component.ClassId == classId) return component;
            return null;
        }

        static YamlDocument FindScript(YamlDocument gameObject,
            IReadOnlyDictionary<long, YamlDocument> documents, string scriptGuid)
        {
            foreach (var component in Components(gameObject, documents))
                if (component.ScriptGuid == scriptGuid) return component;
            return null;
        }

        static Dictionary<long, YamlNode> ReferencesByRid(YamlNode body)
        {
            var result = new Dictionary<long, YamlNode>();
            var references = body?["references"]?["RefIds"];
            if (references is not { Kind: YamlKind.Sequence, Items: { } items }) return result;
            foreach (var item in items) result[Rid(item)] = item;
            return result;
        }

        static HashSet<long> UsedIds(IReadOnlyList<YamlDocument> documents)
        {
            var result = new HashSet<long>();
            foreach (var document in documents) result.Add(document.FileId);
            return result;
        }

        static HashSet<long> UsedRids(IReadOnlyList<YamlDocument> documents)
        {
            var result = new HashSet<long>();
            foreach (var document in documents)
                foreach (var reference in document.ManagedReferences())
                    result.Add(Rid(reference));
            return result;
        }

        static string RetypeRid(MigrationContext ctx, YamlNode reference, long rid)
        {
            var text = ctx.Edit.Slice(reference.Start, reference.End);
            var old = reference["rid"] ?? throw new InvalidOperationException("Managed reference has no rid.");
            var start = old.Start - reference.Start;
            return text.Substring(0, start) + rid + text.Substring(start + old.End - old.Start);
        }

        static long Rid(YamlNode node) =>
            node?["rid"]?.Scalar is { } value && long.TryParse(value, out var rid)
                ? rid
                : throw new InvalidOperationException("Serialized reference has no numeric rid.");

        static void Replace(MigrationContext ctx, YamlNode node, string value)
        {
            if (node == null) throw new InvalidOperationException("Required serialized field is missing.");
            if (ctx.Edit.Slice(node.Start, node.End) != value)
                ctx.Edit.Replace(node.Start, node.End, value);
        }

        static InvalidOperationException Broken(MigrationContext ctx, YamlDocument document, string reason) =>
            new($"[UniText] Cannot migrate retired field {document.FileId} in '{ctx.AssetPath}': {reason}.");

        static void AppendVerticalLayoutGroup(StringBuilder text, long id, long gameObjectId)
        {
            AppendHeader(text, id, gameObjectId, verticalLayoutGroupScriptGuid,
                "UnityEngine.UI::UnityEngine.UI.VerticalLayoutGroup");
            text.Append("  m_Padding:\n")
                .Append("    m_Left: 0\n    m_Right: 0\n    m_Top: 0\n    m_Bottom: 0\n")
                .Append("  m_ChildAlignment: 0\n  m_Spacing: 0\n")
                .Append("  m_ChildForceExpandWidth: 1\n  m_ChildForceExpandHeight: 1\n")
                .Append("  m_ChildControlWidth: 1\n  m_ChildControlHeight: 1\n")
                .Append("  m_ChildScaleWidth: 0\n  m_ChildScaleHeight: 0\n  m_ReverseArrangement: 0\n");
        }

        static void AppendLayoutElement(StringBuilder text, long id, long gameObjectId,
            bool ignoreLayout, string maxHeight)
        {
            AppendHeader(text, id, gameObjectId, layoutElementScriptGuid,
                "UnityEngine.UI::UnityEngine.UI.LayoutElement");
            text.Append("  m_IgnoreLayout: ").Append(ignoreLayout ? 1 : 0).Append('\n')
                .Append("  m_MinWidth: -1\n  m_MinHeight: -1\n")
                .Append("  m_PreferredWidth: -1\n  m_PreferredHeight: -1\n")
                .Append("  m_MaxWidth: -1\n  m_MaxHeight: ").Append(maxHeight).Append('\n')
                .Append("  m_FlexibleWidth: -1\n  m_FlexibleHeight: -1\n  m_LayoutPriority: 1\n");
        }

        static void AppendHeader(StringBuilder text, long id, long gameObjectId, string scriptGuid,
            string editorClassIdentifier)
        {
            text.Append("--- !u!114 &").Append(id).Append('\n')
                .Append("MonoBehaviour:\n  m_ObjectHideFlags: 0\n")
                .Append("  m_CorrespondingSourceObject: {fileID: 0}\n")
                .Append("  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n")
                .Append("  m_GameObject: {fileID: ").Append(gameObjectId).Append("}\n")
                .Append("  m_Enabled: 1\n  m_EditorHideFlags: 0\n")
                .Append("  m_Script: {fileID: 11500000, guid: ").Append(scriptGuid).Append(", type: 3}\n")
                .Append("  m_Name:\n  m_EditorClassIdentifier: ").Append(editorClassIdentifier).Append('\n');
        }
    }
}
