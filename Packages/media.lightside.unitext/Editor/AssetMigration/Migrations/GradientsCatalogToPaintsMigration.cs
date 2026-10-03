using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Converts a legacy <c>UniTextGradients</c> catalog asset into a <see cref="UniTextPaints"/> sibling. The
    /// source script type is gone, so the swatches are read from the asset's YAML text and a new asset is
    /// produced via <see cref="IAssetActions"/>. Idempotent: skips when the sibling already exists. The old
    /// asset is left in place — references to it are re-pointed by hand (logged), since the paint model differs.
    /// </summary>
    internal sealed class GradientsCatalogToPaintsMigration : IMigration
    {
        const string LegacyScriptGuid = "9341473c725a0b74199225eca5849e78";

        readonly string[] tokens = { MigrationTokens.Script(LegacyScriptGuid) };

        public string Id => "asset/UniTextGradients->UniTextPaints";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            bool isCatalog = false;
            foreach (var doc in ctx.Documents)
                if (doc.ScriptGuid == LegacyScriptGuid) { isCatalog = true; break; }
            if (!isCatalog) return;

            var targetPath = Path.ChangeExtension(ctx.AssetPath, null) + "-Paints.asset";
            if (AssetDatabase.LoadAssetAtPath<UniTextPaints>(targetPath) != null) return;

            var swatches = ParseLegacyGradients(ctx.Edit.Source);
            if (swatches.Count == 0)
            {
                Debug.LogWarning($"[UniText] '{ctx.AssetPath}' is a legacy UniTextGradients asset but no entries " +
                                 "could be parsed — convert it to UniTextPaints manually.");
                return;
            }

            var paints = ScriptableObject.CreateInstance<UniTextPaints>();
            foreach (var swatch in swatches)
                paints.Entries.Add(swatch);

            ctx.Assets.Create(paints, targetPath);
            Debug.Log($"[UniText] Converted '{ctx.AssetPath}' → '{targetPath}' ({swatches.Count} swatch(es)). " +
                      "Reference the new UniTextPaints in UniTextSettings (or an AssetPaintProvider), then delete the legacy asset.");
        }

        static List<PaintSwatch> ParseLegacyGradients(string content)
        {
            var result = new List<PaintSwatch>();
            var lines = content.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (!trimmed.StartsWith("- name: ", System.StringComparison.Ordinal)) continue;

                var name = trimmed.Substring("- name: ".Length).Trim();
                if (i + 1 >= lines.Length || lines[i + 1].Trim() != "gradient:") continue;

                var gradient = LegacyGradientYaml.ParseUnityGradientBlock(lines, i + 2, out var mode);
                if (gradient == null) continue;

                result.Add(new PaintSwatch
                {
                    name = name,
                    paint = new Paint
                    {
                        source = new PaintSource
                        {
                            kind = PaintSourceKind.Gradient,
                            color = Color.white,
                            gradient = LegacyGradientYaml.ToGradient(gradient, mode),
                        },
                        projection = PaintProjection.Default,
                        blend = LayerBlend.Normal,
                    },
                });
            }

            return result;
        }
    }
}
