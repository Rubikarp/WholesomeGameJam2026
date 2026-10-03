using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Content;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Build.Pipeline.Utilities;
using UnityEditor.Build.Pipeline.WriteTypes;
using UnityEditor.Build.Player;
using UnityEngine;

namespace LightSide.Editor.SBP
{
    /// <summary>
    /// Completes Scriptable Build Pipeline content after packing: every serialized file that packs an
    /// embedded <see cref="UniTextFont"/> also packs its <see cref="UniTextFontPayload"/> sub-asset,
    /// reachable only through an injected <c>unitext/fonts/payload/&lt;token&gt;</c> container entry and
    /// stripped from every preload list, so loading the bundle never deserializes font bytes. The
    /// entry's preload carries the payload's MonoScript — players bind a serialized object's class only
    /// to a preloaded script object — placed in the build's MonoScript file when one exists, inline
    /// otherwise. Scene files keep the payload as a plain scene object. WebGL content keeps payloads
    /// reachable through the font itself and is left untouched.
    /// </summary>
    [InitializeOnLoad]
    internal static class UniTextEmbeddedFontSbpIntegration
    {
        private static readonly Func<IBuildParameters, IDependencyData, IWriteData,
            ReturnCode> upstream;

        static UniTextEmbeddedFontSbpIntegration()
        {
            var callbacks = ContentPipeline.BuildCallbacks
                            ?? throw new InvalidOperationException(
                                "The Scriptable Build Pipeline callback registry is unavailable.");
            upstream = callbacks.PostPackingCallback;
            callbacks.PostPackingCallback = Process;
        }

        private static ReturnCode Process(IBuildParameters parameters,
            IDependencyData dependencyData, IWriteData writeData)
        {
            var upstreamResult = upstream?.Invoke(parameters, dependencyData, writeData)
                                 ?? ReturnCode.Success;
            if (upstreamResult < ReturnCode.Success) return upstreamResult;
            if (parameters.Target == BuildTarget.WebGL) return upstreamResult;

            Inject(parameters, writeData);
            return upstreamResult;
        }

        private static void Inject(IBuildParameters parameters, IWriteData writeData)
        {
            if (writeData is not IBundleWriteData bundles) return;
            var target = parameters.Target;

            var fontAssets = new EmbeddedFontAssetIndex();
            var payloads = new Dictionary<GUID, (ObjectIdentifier id, int token)>();
            var injections = new Dictionary<string, List<(ObjectIdentifier payload, int token)>>(
                StringComparer.Ordinal);

            foreach (var fileObjects in bundles.FileToObjects)
            {
                var objects = fileObjects.Value;
                if (objects == null || objects.Count == 0) continue;

                for (var i = 0; i < objects.Count; i++)
                {
                    var identifier = objects[i];
                    if (identifier.guid.Empty() || !fontAssets.Contains(identifier.guid)) continue;
                    if (ObjectIdentifier.ToObject(identifier) is not UniTextFont font
                        || !font.UsesEmbeddedSource) continue;

                    var payload = ResolvePayload(payloads, identifier.guid, target, font);
                    if (!injections.TryGetValue(fileObjects.Key, out var filePayloads))
                    {
                        filePayloads = new List<(ObjectIdentifier, int)>();
                        injections.Add(fileObjects.Key, filePayloads);
                    }
                    if (!filePayloads.Contains(payload)) filePayloads.Add(payload);
                }
            }

            if (injections.Count == 0) return;

            var firstPayload = default(ObjectIdentifier);
            foreach (var pairs in injections.Values)
            {
                firstPayload = pairs[0].payload;
                break;
            }
            var scriptId = ResolvePayloadScript(target, parameters.ScriptInfo, firstPayload);
            var scriptHome = PlacePayloadScript(bundles, scriptId);

            var pendingFiles = new HashSet<string>(injections.Keys, StringComparer.Ordinal);
            for (var i = 0; i < bundles.WriteOperations.Count; i++)
            {
                var operation = bundles.WriteOperations[i]
                                ?? throw new BuildFailedException($"SBP write operation {i} is null.");
                var command = operation.Command
                              ?? throw new BuildFailedException(
                                  $"SBP write operation {i} has no command.");
                if (!pendingFiles.Remove(command.internalName)) continue;

                Apply(operation, command, injections[command.internalName], scriptId, scriptHome);
            }

            if (pendingFiles.Count != 0)
                throw new BuildFailedException(
                    $"No SBP write operation was found for UniText font file(s): {string.Join(", ", pendingFiles)}.");
        }

        /// <summary>
        /// The <see cref="UniTextFontPayload"/> MonoScript object. Injection happens after script
        /// collection, so no MonoScript bundle carries it on its own and it must be placed explicitly.
        /// </summary>
        private static ObjectIdentifier ResolvePayloadScript(BuildTarget target,
            TypeDB scriptInfo, ObjectIdentifier payloadId)
        {
            var dependencies = ContentBuildInterface.GetPlayerDependenciesForObjects(
                new[] { payloadId }, target, scriptInfo);
            for (var i = 0; i < dependencies.Length; i++)
                if (ObjectIdentifier.ToObject(dependencies[i]) is MonoScript)
                    return dependencies[i];
            throw new BuildFailedException(
                $"The {nameof(UniTextFontPayload)} MonoScript dependency is unresolvable.");
        }

        /// <summary>
        /// Places the payload MonoScript where the build convention puts scripts: the dedicated
        /// MonoScript file when the build has one, otherwise inline in each payload-carrying file.
        /// Returns null when inline placement applies.
        /// </summary>
        private static (string file, long index)? PlacePayloadScript(IBundleWriteData bundles,
            ObjectIdentifier scriptId)
        {
            foreach (var operation in bundles.WriteOperations)
            {
                var command = operation?.Command;
                if (command?.serializeObjects is not { Count: > 0 } objects) continue;

                var monoScriptsOnly = true;
                for (var i = 0; i < objects.Count; i++)
                {
                    if (objects[i].serializationObject == scriptId)
                        return (command.internalName, objects[i].serializationIndex);
                    if (monoScriptsOnly
                        && ObjectIdentifier.ToObject(objects[i].serializationObject) is not MonoScript)
                        monoScriptsOnly = false;
                }
                if (!monoScriptsOnly) continue;

                var usedIndices = new HashSet<long>();
                for (var i = 0; i < objects.Count; i++)
                    usedIndices.Add(objects[i].serializationIndex);
                var index = AllocateIndex(usedIndices, scriptId);
                objects.Add(new SerializationInfo
                {
                    serializationObject = scriptId,
                    serializationIndex = index
                });
                operation.ReferenceMap.AddMapping(command.internalName, index, scriptId);
                return (command.internalName, index);
            }

            return null;
        }

        private static void Apply(IWriteOperation operation, WriteCommand command,
            List<(ObjectIdentifier payload, int token)> filePayloads, ObjectIdentifier scriptId,
            (string file, long index)? scriptHome)
        {
            var usedIndices = new HashSet<long>();
            var presentObjects = new HashSet<ObjectIdentifier>();
            for (var i = 0; i < command.serializeObjects.Count; i++)
            {
                usedIndices.Add(command.serializeObjects[i].serializationIndex);
                presentObjects.Add(command.serializeObjects[i].serializationObject);
            }

            if (scriptHome.HasValue
                && !string.Equals(scriptHome.Value.file, command.internalName, StringComparison.Ordinal))
                operation.ReferenceMap.AddMapping(scriptHome.Value.file, scriptHome.Value.index, scriptId);
            else
                AddObject(operation, command, usedIndices, presentObjects, scriptId);

            foreach (var (payloadId, token) in filePayloads)
            {
                AddObject(operation, command, usedIndices, presentObjects, payloadId);
                if (operation is AssetBundleWriteOperation assetBundle)
                    ExposeAsNamedEntry(assetBundle, payloadId, token, scriptId);
            }
        }

        private static void AddObject(IWriteOperation operation, WriteCommand command,
            HashSet<long> usedIndices, HashSet<ObjectIdentifier> presentObjects,
            ObjectIdentifier identifier)
        {
            if (!presentObjects.Add(identifier)) return;
            var index = AllocateIndex(usedIndices, identifier);
            command.serializeObjects.Add(new SerializationInfo
            {
                serializationObject = identifier,
                serializationIndex = index
            });
            operation.ReferenceMap.AddMapping(command.internalName, index, identifier);
        }

        private static void ExposeAsNamedEntry(AssetBundleWriteOperation operation,
            ObjectIdentifier payloadId, int token, ObjectIdentifier scriptId)
        {
            var bundleAssets = operation.Info?.bundleAssets;
            if (bundleAssets == null)
                throw new BuildFailedException(
                    $"SBP bundle '{operation.Info?.bundleName}' packs a UniText font but lists no assets.");

            var address = FontSourceId.PayloadAddress(token);
            var exists = false;
            for (var i = 0; i < bundleAssets.Count; i++)
            {
                var asset = bundleAssets[i];
                if (string.Equals(asset.address, address, StringComparison.Ordinal))
                {
                    exists = true;
                    continue;
                }
                asset.includedObjects?.Remove(payloadId);
                asset.referencedObjects?.Remove(payloadId);
            }
            if (exists) return;

            bundleAssets.Add(new AssetLoadInfo
            {
                asset = payloadId.guid,
                address = address,
                includedObjects = new List<ObjectIdentifier> { payloadId },
                referencedObjects = new List<ObjectIdentifier> { scriptId }
            });
        }

        private static (ObjectIdentifier id, int token) ResolvePayload(
            Dictionary<GUID, (ObjectIdentifier id, int token)> cache,
            GUID guid, BuildTarget target, UniTextFont font)
        {
            if (cache.TryGetValue(guid, out var payload)) return payload;

            if (!font.TryGetEmbeddedBuildSource(out var token, out _))
                throw MissingPayload(guid, font);

            var identifiers = ContentBuildInterface.GetPlayerObjectIdentifiersInAsset(guid, target);
            var found = false;
            var payloadId = default(ObjectIdentifier);
            for (var i = 0; i < identifiers.Length; i++)
            {
                if (ObjectIdentifier.ToObject(identifiers[i]) is not UniTextFontPayload candidate
                    || candidate.token != token) continue;
                if (found)
                    throw new BuildFailedException(
                        $"'{AssetDatabase.GUIDToAssetPath(guid.ToString())}' contains multiple payload sub-assets for font '{font.name}'.");
                found = true;
                payloadId = identifiers[i];
            }
            if (!found) throw MissingPayload(guid, font);

            payload = (payloadId, token);
            cache.Add(guid, payload);
            return payload;
        }

        private static BuildFailedException MissingPayload(GUID guid, UniTextFont font)
            => new($"Font '{font.name}' has no {nameof(UniTextFontPayload)} sub-asset. Open the project"
                   + $" in the editor once so the UniText migration updates"
                   + $" '{AssetDatabase.GUIDToAssetPath(guid.ToString())}', then rebuild.");

        private static long AllocateIndex(HashSet<long> used, ObjectIdentifier identifier)
        {
            var hash = HashingMethods.Calculate<MD4>(identifier.guid.ToString(),
                identifier.fileType, identifier.localIdentifierInFile);
            var index = BitConverter.ToInt64(hash.ToBytes(), 0);
            while (!used.Add(index)) index++;
            return index;
        }
    }
}
