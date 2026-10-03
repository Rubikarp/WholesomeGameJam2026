using System;
using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Renumbers paint override fields onto the shared negative inheritance sentinel after the separate
    /// override enums were folded into their resolved counterparts. An asset holding any value only the
    /// folded vocabulary can express is left untouched entirely: one save writes one vocabulary, so a single
    /// such value proves the whole asset current.
    /// </summary>
    internal sealed class PaintOverrideSentinelMigration : IMigration
    {
        private const string PresentationEntry = "presentation";
        private const string SelectionEntry = "selectionHighlight";

        private static readonly string[] effectFields = { "paintSpread" };
        private static readonly string[] projectionFields = { "shape", "fit" };
        private static readonly string[] presentationFields = { "shape", "fit", "spread" };

        private readonly string[] tokens;
        private readonly HashSet<TypeSignature> blendTypes = new();
        private readonly HashSet<TypeSignature> projectionTypes = new();
        private readonly HashSet<TypeSignature> presentationTypes = new();
        private readonly string selectableScriptGuid;

        public PaintOverrideSentinelMigration()
        {
            var discovered = new List<string>();
            Collect<EffectModifier>(blendTypes, discovered);
            Collect<BaseLineModifier>(blendTypes, discovered);
            Collect<PaintLayerModifier>(projectionTypes, discovered);
            Collect<BaseRangeDecorationModifier>(presentationTypes, discovered);
            selectableScriptGuid = ScriptGuidOf(typeof(UniTextSelectable));
            discovered.Add(MigrationTokens.Script(selectableScriptGuid));
            tokens = discovered.ToArray();
        }

        public string Id => "value/LightSide.UniText/PaintOverrideInherit->-1";
        public bool Idempotent => false;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            if (!Visit(ctx, false)) Visit(ctx, true);
        }

        /// <summary>
        /// Reports whether any override field already holds a post-fold value, rewriting the visited fields
        /// through their vocabulary map when <paramref name="apply"/> is set.
        /// </summary>
        private bool Visit(MigrationContext ctx, bool apply)
        {
            foreach (var document in ctx.Documents)
            {
                if (document.ScriptGuid == selectableScriptGuid &&
                    VisitPresentation(document.Body?[SelectionEntry], ctx, apply))
                    return true;

                foreach (var reference in document.ManagedReferences())
                {
                    var type = reference["type"];
                    var signature = new TypeSignature(type?["class"]?.Scalar,
                        type?["ns"]?.Scalar, type?["asm"]?.Scalar);
                    var data = reference["data"];
                    if (data == null) continue;

                    if (blendTypes.Contains(signature))
                    {
                        if (Shift(data, "blend", Blend, ctx, apply)) return true;
                        foreach (var field in effectFields)
                            if (Shift(data, field, Sentinel, ctx, apply)) return true;
                    }
                    if (projectionTypes.Contains(signature))
                        foreach (var field in projectionFields)
                            if (Shift(data, field, Sentinel, ctx, apply)) return true;
                    if (presentationTypes.Contains(signature) &&
                        VisitPresentation(data[PresentationEntry], ctx, apply))
                        return true;
                }
            }
            return false;
        }

        private static bool VisitPresentation(YamlNode presentation, MigrationContext ctx, bool apply)
        {
            if (presentation == null) return false;
            foreach (var field in presentationFields)
                if (Shift(presentation, field, Sentinel, ctx, apply)) return true;
            return Shift(presentation, "blend", Blend, ctx, apply);
        }

        /// <summary>
        /// Rewrites one scalar entry through <paramref name="map"/>, reconstructing the line so the splice
        /// never depends on how the parser bounds a scalar, and reports a post-fold value. Absent entries are
        /// left untouched — an asset saved before the field existed carries the new default already.
        /// </summary>
        private static bool Shift(YamlNode owner, string field,
            Func<int, MigrationContext, string, int?> map, MigrationContext ctx, bool apply)
        {
            var entry = owner?.Entry(field);
            if (entry?.Value is not { Kind: YamlKind.Scalar } value) return false;
            if (!int.TryParse(value.Scalar, out var current))
                throw new InvalidOperationException(
                    $"[UniText] '{ctx.AssetPath}' has a non-numeric '{field}' paint override.");
            if (map(current, ctx, field) is not { } shifted) return true;
            if (!apply) return false;

            var pad = UnityYaml.Indent(ctx.Edit.Source, entry.Start);
            ctx.Edit.Replace(entry.Start, entry.End, $"{pad}{field}: {shifted}\n");
            return false;
        }

        /// <summary>
        /// Shifts a former leading <c>Inherit = 0</c> member onto the shared sentinel; null once the value is
        /// the sentinel, which the pre-fold vocabulary could not express.
        /// </summary>
        private static int? Sentinel(int value, MigrationContext ctx, string field)
        {
            if (value == -1) return null;
            if (value is < 0 or > 4)
                throw new InvalidOperationException(
                    $"[UniText] '{ctx.AssetPath}' has an out-of-range '{field}' paint override ({value}).");
            return value - 1;
        }

        /// <summary>
        /// Maps the former dense blend-override vocabulary onto <see cref="LayerBlend"/>, which reserves
        /// 4 and 5 for the removed Darken and Lighten modes; null for a value only <see cref="LayerBlend"/>
        /// can express.
        /// </summary>
        private static int? Blend(int value, MigrationContext ctx, string field) => value switch
        {
            -1 or 6 => null,
            0 => -1,
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            5 => 6,
            _ => throw new InvalidOperationException(
                $"[UniText] '{ctx.AssetPath}' has an out-of-range '{field}' blend override ({value})."),
        };

        private static void Collect<T>(HashSet<TypeSignature> target, List<string> discovered)
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (type.IsAbstract) continue;
                var signature = TypeSignature.Of(type);
                if (!target.Add(signature)) continue;
                var token = signature.Token;
                if (!discovered.Contains(token)) discovered.Add(token);
            }
        }

        private static string ScriptGuidOf(Type type)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (script != null && script.GetClass() == type) return guid;
            }

            throw new InvalidOperationException(
                $"[UniText] The script asset for '{type.Name}' could not be resolved for migration discovery.");
        }
    }
}
