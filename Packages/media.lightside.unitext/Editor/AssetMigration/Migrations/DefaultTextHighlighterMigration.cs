using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Converts the retired click/hover highlighter directly into ordinary range effects. The
    /// component-level field had no range source and is removed; modifier-owned instances preserve
    /// their colours and fade duration as HighlightModifier effect graphs.
    /// </summary>
    internal sealed class DefaultTextHighlighterMigration : IMigration
    {
        private static readonly TypeSignature from =
            new("DefaultTextHighlighter", "LightSide", "LightSide.UniText");

        private const string uniTextGuid = "beaa34cb0e58d624bb3a264b28600785";
        private const string uniTextWorldGuid = "f82394fefa9244d49e439daa1fb85977";

        private static readonly Color defaultClick = new(0.2f, 0.5f, 1f, 0.6f);
        private static readonly Color defaultHover = new(0.2f, 0.5f, 1f, 0.1f);
        private const float defaultFade = 0.25f;

        private readonly string[] tokens = { from.Token };

        public string Id => "rename/LightSide.UniText/DefaultTextHighlighter->StateHighlightStyler";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => tokens;

        public void Migrate(MigrationContext ctx)
        {
            var usedRids = new HashSet<long>();
            foreach (var document in ctx.Documents)
                foreach (var reference in document.ManagedReferences())
                    usedRids.Add(InteractiveEffectYaml.Rid(reference));

            foreach (var document in ctx.Documents)
                MigrateDocument(ctx, document, usedRids);
        }

        private static void MigrateDocument(MigrationContext ctx, YamlDocument document,
            HashSet<long> usedRids)
        {
            var legacy = new Dictionary<long, YamlNode>();
            foreach (var reference in document.ManagedReferences())
            {
                var type = reference["type"];
                if (type?["class"]?.Scalar == from.Class &&
                    type["ns"]?.Scalar == from.Namespace &&
                    type["asm"]?.Scalar == from.Assembly)
                    legacy[InteractiveEffectYaml.Rid(reference)] = reference;
            }
            if (legacy.Count == 0) return;

            var componentRid = 0L;
            if (document.ScriptGuid == uniTextGuid || document.ScriptGuid == uniTextWorldGuid)
            {
                var body = document.Root?.Entries is { Count: > 0 } entries
                    ? entries[0].Value
                    : null;
                var field = body?.Entry("highlighter");
                if (field != null && InteractiveEffectYaml.TryRid(field.Value, out componentRid) &&
                    legacy.TryGetValue(componentRid, out var componentHighlighter))
                {
                    ReadColors(componentHighlighter["data"], out var click, out var hover, out var fade);
                    if (!IsDefault(click, hover, fade))
                        Debug.LogWarning(
                            $"[UniText] '{ctx.AssetPath}': removed the old component-level highlighter " +
                            $"(hover {Hex(hover)}, click {Hex(click)}, fade {fade}s). It had no range " +
                            "source; recreate intentional global marks as a Style with HighlightModifier.");
                    ctx.Edit.Delete(field.Start, field.End);
                }
            }

            var converted = new HashSet<long>();
            foreach (var owner in document.ManagedReferences())
            {
                if (legacy.ContainsValue(owner)) continue;
                var data = owner["data"];
                var field = data?.Entry("highlighter");
                if (field == null || !InteractiveEffectYaml.TryRid(field.Value, out var rid) ||
                    !legacy.TryGetValue(rid, out var highlighter))
                    continue;
                if (!converted.Add(rid))
                    throw new InvalidOperationException(
                        $"[UniText] '{ctx.AssetPath}' shares one DefaultTextHighlighter between modifiers.");

                ReadColors(highlighter["data"], out var click, out var hover, out var fade);
                var conversion = InteractiveEffectYaml.FromLegacyHighlighter(
                    ctx.Edit.Source, highlighter, usedRids, hover, click, fade);
                InteractiveEffectYaml.ReplaceEffectField(
                    ctx, data, field, "effects", conversion.EffectRids);
                ctx.Edit.Replace(highlighter.Start, highlighter.End, conversion.References);
            }

            foreach (var pair in legacy)
                if (!converted.Contains(pair.Key))
                    ctx.Edit.Delete(pair.Value.Start, pair.Value.End);
        }

        private static void ReadColors(YamlNode data, out Color click, out Color hover, out float fade)
        {
            click = ReadColor(data?["clickColor"], defaultClick);
            hover = ReadColor(data?["hoverColor"], defaultHover);
            fade = ReadFloat(data?["fadeDuration"], defaultFade);
        }

        private static Color ReadColor(YamlNode node, Color fallback) => node?.Entries == null
            ? fallback
            : new Color(ReadFloat(node["r"], fallback.r), ReadFloat(node["g"], fallback.g),
                ReadFloat(node["b"], fallback.b), ReadFloat(node["a"], fallback.a));

        private static float ReadFloat(YamlNode node, float fallback)
            => node?.Scalar is { } value && float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

        private static bool IsDefault(Color click, Color hover, float fade)
            => Approximately(click, defaultClick) && Approximately(hover, defaultHover) &&
               Math.Abs(fade - defaultFade) < 1e-4f;

        private static bool Approximately(Color left, Color right)
            => Math.Abs(left.r - right.r) < 1e-4f &&
               Math.Abs(left.g - right.g) < 1e-4f &&
               Math.Abs(left.b - right.b) < 1e-4f &&
               Math.Abs(left.a - right.a) < 1e-4f;

        private static string Hex(Color color)
            => $"#{Byte(color.r):X2}{Byte(color.g):X2}{Byte(color.b):X2}{Byte(color.a):X2}";

        private static int Byte(float value)
            => Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255);
    }
}
