using System;
using System.Collections.Generic;
using System.Globalization;

namespace LightSide
{
    /// <summary>
    /// Splits the driver's former single <c>loop</c> switch into the cycle count and repeat mode the rest of
    /// the family uses: <c>Once</c> becomes one cycle, <c>Loop</c> becomes an endless restart, and
    /// <c>PingPong</c> becomes an endless mirror. Prefab-instance overrides of the retired field are rewritten
    /// alongside the components themselves.
    /// </summary>
    internal sealed class DriverRepeatMigration : IMigration
    {
        private const string driverScriptGuid = "92fc24a3414e32945b300c09233a7ad5";
        private const long driverScriptFileId = 11500000;

        /// <summary>Values the retired <c>loop</c> enum serialized.</summary>
        private const string legacyOnce = "0";
        private const string legacyLoop = "1";
        private const string legacyPingPong = "2";

        private readonly PrefabOverrideTarget target =
            new(driverScriptGuid, driverScriptFileId, "loop");

        public string Id => "field/UniTextDriver.loop->cycles+cycleMode";
        public bool Idempotent => true;
        public IReadOnlyList<string> Tokens => target.Tokens;

        public void Migrate(MigrationContext ctx)
        {
            foreach (var document in ctx.Documents)
            {
                if (document.Stripped) continue;

                if (target.Matches(document)) MigrateComponent(ctx, document);
                MigrateOverrides(ctx, document);
            }
        }

        private static void MigrateComponent(MigrationContext ctx, YamlDocument document)
        {
            var entry = document.Body?.Entry("loop");
            if (entry?.Value is not { Kind: YamlKind.Scalar } value) return;

            var (cycles, mode) = Translate(value.Scalar);
            var pad = UnityYaml.Indent(ctx.Edit.Source, entry.Start);
            var source = ctx.Edit.Slice(entry.Start, entry.End);
            var newline = source.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var trailing = source.EndsWith("\n", StringComparison.Ordinal) ? newline : "";
            ctx.Edit.Replace(entry.Start, entry.End,
                pad + "cycles: " + cycles.ToString(CultureInfo.InvariantCulture) + newline +
                pad + "cycleMode: " + ((int)mode).ToString(CultureInfo.InvariantCulture) + trailing);
        }

        /// <summary>
        /// Rewrites each prefab-instance override of the retired field into the two it became, preserving the
        /// entry's own target and object reference so the override keeps pointing at the same component.
        /// </summary>
        private void MigrateOverrides(MigrationContext ctx, YamlDocument document)
        {
            if (document.Body?["m_Modification"]?["m_Modifications"] is not
                { Kind: YamlKind.Sequence, Items: { } items }) return;

            foreach (var item in items)
            {
                if (item.Kind != YamlKind.Map) continue;
                if (!target.Matches(item)) continue;
                if (item["value"] is not { IsScalar: true } value) continue;

                var (cycles, mode) = Translate(value.Scalar);
                var source = ctx.Edit.Source.Substring(item.Start, item.End - item.Start);

                ctx.Edit.Replace(item.Start, item.End,
                    Retarget(source, item, "cycles", cycles.ToString(CultureInfo.InvariantCulture)) +
                    Retarget(source, item, "cycleMode", ((int)mode).ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>One modification entry rewritten to name <paramref name="path"/> and carry <paramref name="value"/>.</summary>
        private static string Retarget(string entry, YamlNode item, string path, string value)
        {
            var edit = new YamlEdit(entry);
            var property = item["propertyPath"];
            var serializedValue = item["value"];
            edit.Replace(property.Start - item.Start, property.End - item.Start, path);
            edit.Replace(serializedValue.Start - item.Start, serializedValue.End - item.Start, value);
            return edit.Apply();
        }

        /// <summary>
        /// The count and mode one retired value stood for.
        /// </summary>
        private static (int cycles, MotionCycle mode) Translate(string legacy) => legacy switch
        {
            legacyLoop => (MotionCycles.Infinite, MotionCycle.Restart),
            legacyPingPong => (MotionCycles.Infinite, MotionCycle.PingPong),
            legacyOnce => (1, MotionCycle.Restart),
            _ => throw new InvalidOperationException(
                $"[UniText] Unknown legacy driver loop value '{legacy}'."),
        };
    }
}
