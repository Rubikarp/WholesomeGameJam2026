using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Rewrites every serialized easing field from a bare <see cref="EasingType"/> ordinal into an
    /// <see cref="Ease"/> in its built-in mode, keeping the authored curve.
    /// </summary>
    internal sealed class EaseStructMigration : IMigration
    {
        private const string DriverScriptGuid = "92fc24a3414e32945b300c09233a7ad5";

        private readonly string[] tokens;
        private readonly Dictionary<TypeSignature, string> referenceFields = new();

        public EaseStructMigration()
        {
            var discovered = new List<string> { MigrationTokens.Script(DriverScriptGuid) };

            Add(typeof(TransitionPlayback), "easing");
            Add(typeof(SignalProgressPlayback), "exitEasing");
            foreach (var type in TypeCache.GetTypesDerivedFrom<EasedRevealHandler>())
                if (!type.IsAbstract)
                    Add(type, "easing");

            tokens = discovered.ToArray();

            void Add(Type type, string field)
            {
                var signature = TypeSignature.Of(type);
                if (referenceFields.ContainsKey(signature)) return;
                referenceFields.Add(signature, field);
                discovered.Add(signature.Token);
            }
        }

        public string Id => "value/LightSide.UniText/EasingType->Ease";
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
                    if (!referenceFields.TryGetValue(signature, out var field)) continue;
                    Convert(reference["data"]?.Entry(field), ctx);
                }

                if (document.ScriptGuid != DriverScriptGuid) continue;
                if (document.Body?["clips"] is not { Kind: YamlKind.Sequence, Items: { } clips }) continue;
                foreach (var clip in clips)
                    Convert(clip?.Entry("easing"), ctx);
            }
        }

        private static void Convert(YamlEntry entry, MigrationContext ctx)
        {
            if (entry?.Value is not { Kind: YamlKind.Scalar } value) return;
            if (!int.TryParse(value.Scalar, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var curve)) return;

            var pad = UnityYaml.Indent(ctx.Edit.Source, entry.Start);
            ctx.Edit.Replace(entry.Start, entry.End,
                pad + entry.Key + ": {kind: 0, type: " +
                curve.ToString(CultureInfo.InvariantCulture) +
                ", controls: {x: 0, y: 0, z: 0, w: 0}, keys: []}\n");
        }
    }
}
