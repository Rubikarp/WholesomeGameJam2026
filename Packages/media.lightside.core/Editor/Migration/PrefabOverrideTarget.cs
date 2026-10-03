using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>Discovers and identifies prefab overrides that belong to one serialized script field.</summary>
    public sealed class PrefabOverrideTarget
    {
        private readonly string scriptGuid;
        private readonly long scriptFileId;
        private readonly string propertyPath;
        private readonly string[] tokens;
        private readonly Dictionary<(string guid, long fileId), (string guid, long fileId)> resolved = new();

        /// <summary>Targets one serialized field belonging to the identified component script.</summary>
        public PrefabOverrideTarget(string scriptGuid, long scriptFileId, string propertyPath)
        {
            var script = MigrationTokens.Script(scriptGuid);
            var property = MigrationTokens.PrefabOverride(propertyPath);
            if (!MigrationTokens.IsValid(script))
                throw new ArgumentException(
                    "A script GUID must contain 32 hexadecimal characters.", nameof(scriptGuid));
            if (scriptFileId == 0)
                throw new ArgumentOutOfRangeException(nameof(scriptFileId));
            if (!MigrationTokens.IsValid(property))
                throw new ArgumentException("A serialized property path is required.", nameof(propertyPath));

            this.scriptGuid = MigrationTokens.ScriptGuid(script);
            this.scriptFileId = scriptFileId;
            this.propertyPath = propertyPath;
            tokens = new[] { script, property };
        }

        /// <summary>Discovery tokens covering direct components and scene-only prefab overrides.</summary>
        public IReadOnlyList<string> Tokens => tokens;

        /// <summary>Reports whether a document is serialized by this script.</summary>
        public bool Matches(YamlDocument document)
        {
            var script = document?.Body?["m_Script"];
            return string.Equals(UnityYaml.Unquote(script?["guid"]?.Scalar), scriptGuid,
                       StringComparison.OrdinalIgnoreCase) && UnityYaml.FileId(script) == scriptFileId;
        }

        /// <summary>Reports whether a modification targets this field on a component backed by this script.</summary>
        public bool Matches(YamlNode modification)
        {
            if (modification?.Kind != YamlKind.Map ||
                UnityYaml.Unquote(modification["propertyPath"]?.Scalar) != propertyPath)
                return false;

            var reference = modification["target"];
            var guid = UnityYaml.Unquote(reference?["guid"]?.Scalar);
            var fileId = UnityYaml.FileId(reference);
            if (string.IsNullOrEmpty(guid) || fileId == 0) return false;

            var key = (guid, fileId);
            if (!resolved.TryGetValue(key, out var source))
                resolved[key] = source = Resolve(guid, fileId);
            return string.Equals(source.guid, scriptGuid, StringComparison.OrdinalIgnoreCase) &&
                   source.fileId == scriptFileId;
        }

        private static (string guid, long fileId) Resolve(string guid, long fileId)
        {
            var objectId = unchecked((ulong)fileId);
            if (!GlobalObjectId.TryParse($"GlobalObjectId_V1-3-{guid}-{objectId}-0", out var id))
                throw new InvalidOperationException(
                    $"Prefab override target '{guid}:{fileId}' is not a valid object identity.");
            if (GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) is not MonoBehaviour component)
                return default;

            var script = MonoScript.FromMonoBehaviour(component);
            if (script == null ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out var resolvedGuid,
                    out long resolvedFileId))
                return default;
            return (resolvedGuid, resolvedFileId);
        }
    }
}
