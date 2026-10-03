using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace LightSide
{
    internal sealed class InteractiveEffectsMigration : IMigration
    {
        private static readonly TypeSignature stateStyler =
            new("StateHighlightStyler", "LightSide", "LightSide.UniText");
        private static readonly TypeSignature spoilerStyler =
            new("SpoilerCoverStyler", "LightSide", "LightSide.UniText");
        private static readonly TypeSignature interactiveModifier =
            new("InteractiveModifier", "LightSide", "LightSide.UniText");
        private static readonly TypeSignature linkModifier =
            new("LinkModifier", "LightSide", "LightSide.UniText");
        private static readonly TypeSignature spoilerModifier =
            new("SpoilerModifier", "LightSide", "LightSide.UniText");

        private readonly string[] tokens =
        {
            stateStyler.Token,
            spoilerStyler.Token,
            interactiveModifier.Token,
            linkModifier.Token,
            spoilerModifier.Token,
        };

        public string Id => "shape/LightSide.UniText/RangeStyler->RangeStateEffect";
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
            var references = new Dictionary<long, YamlNode>();
            foreach (var reference in document.ManagedReferences())
                references[InteractiveEffectYaml.Rid(reference)] = reference;

            var converted = new HashSet<long>();
            foreach (var owner in document.ManagedReferences())
            {
                var data = owner["data"];
                var knownOwner = IsInteractiveModifier(owner);
                var rangeTypeEntry = knownOwner ? data?.Entry("rangeType") : null;
                if (rangeTypeEntry != null)
                    ctx.Edit.Delete(rangeTypeEntry.Start, rangeTypeEntry.End);
                var stylerEntry = data?.Entry("styler");
                if (stylerEntry == null || !InteractiveEffectYaml.TryRid(stylerEntry.Value, out var stylerRid))
                    continue;
                if (stylerRid == -2)
                {
                    if (knownOwner) ctx.Edit.Delete(stylerEntry.Start, stylerEntry.End);
                    continue;
                }
                if (!references.TryGetValue(stylerRid, out var styler))
                {
                    if (knownOwner)
                        throw new InvalidOperationException(
                            $"[UniText] '{ctx.AssetPath}' references missing RangeStyler rid {stylerRid}.");
                    continue;
                }
                InteractiveEffectYaml.Conversion conversion;
                if (Is(styler, stateStyler))
                    conversion = InteractiveEffectYaml.FromStateStyler(
                        ctx.Edit.Source, styler, usedRids);
                else if (Is(styler, spoilerStyler))
                    conversion = InteractiveEffectYaml.FromSpoilerStyler(
                        ctx.Edit.Source, styler, usedRids);
                else if (knownOwner)
                    throw new InvalidOperationException(
                        $"[UniText] '{ctx.AssetPath}' uses custom RangeStyler rid {stylerRid}. " +
                        "Replace it with RangeStateRule definitions before upgrading.");
                else
                    continue;
                if (!converted.Add(stylerRid))
                    throw new InvalidOperationException(
                        $"[UniText] '{ctx.AssetPath}' shares one RangeStyler between multiple modifiers.");

                InteractiveEffectYaml.ReplaceStylerField(
                    ctx, data, stylerEntry, conversion.EffectRids);
                ctx.Edit.Replace(styler.Start, styler.End, conversion.References);
            }

            foreach (var pair in references)
            {
                if (converted.Contains(pair.Key)) continue;
                if (Is(pair.Value, stateStyler) || Is(pair.Value, spoilerStyler))
                    ctx.Edit.Delete(pair.Value.Start, pair.Value.End);
            }
        }

        private static bool Is(YamlNode reference, TypeSignature signature)
        {
            var type = reference?["type"];
            return type?["class"]?.Scalar == signature.Class &&
                   type["ns"]?.Scalar == signature.Namespace &&
                   type["asm"]?.Scalar == signature.Assembly;
        }

        private static bool IsInteractiveModifier(YamlNode reference)
            => Is(reference, interactiveModifier) || Is(reference, linkModifier) ||
               Is(reference, spoilerModifier);
    }

    internal static class InteractiveEffectYaml
    {
        internal readonly struct Conversion
        {
            public readonly long[] EffectRids;
            public readonly string References;

            public Conversion(long[] effectRids, string references)
            {
                EffectRids = effectRids;
                References = references;
            }
        }

        internal readonly struct LegacyHighlightStyle
        {
            public readonly int paintKind;
            public readonly uint rgba;
            public readonly string swatch;
            public readonly int height;
            public readonly string paddingX;
            public readonly string paddingY;
            public readonly string radius;
            public readonly int boxBreak;
            public readonly string mergeThreshold;

            public LegacyHighlightStyle(int paintKind, uint rgba, string swatch, int height,
                string paddingX, string paddingY, string radius, int boxBreak,
                string mergeThreshold)
            {
                this.paintKind = paintKind;
                this.rgba = rgba;
                this.swatch = swatch;
                this.height = height;
                this.paddingX = paddingX;
                this.paddingY = paddingY;
                this.radius = radius;
                this.boxBreak = boxBreak;
                this.mergeThreshold = mergeThreshold;
            }

            public static LegacyHighlightStyle Solid(Color color, string paddingX = "0.1",
                string paddingY = "0.06", string radius = "0.15", int boxBreak = 0)
                => new(1, Pack(color), null, 1, paddingX, paddingY, radius, boxBreak, "-1");
        }

        public static Conversion FromStateStyler(string source, YamlNode reference,
            HashSet<long> usedRids)
        {
            var data = reference["data"];
            var builder = new Builder(source, reference, usedRids,
                long.MaxValue - 1_000_000);
            var effects = new List<long>(5);

            if (ReadBool(data?["normalEnabled"], false))
                effects.Add(builder.AddInteractionEffect(0,
                    ReadStyle(data?["normal"], DefaultNormal()), Driver.Instant, 0));

            if (ReadBool(data?["hoverEnabled"], true))
            {
                var enter = ReadScalar(data?["hoverFadeIn"], "0.18");
                var exit = ReadScalar(data?["hoverFadeOut"], "0.1");
                effects.Add(builder.AddAnyInteractionEffect(
                    (int)InteractionSignalMask.Hovered, (int)InteractionSignalMask.Pressed,
                    ReadStyle(data?["hovered"], DefaultHover()),
                    Driver.Transition(enter, exit), 0));
            }

            if (ReadBool(data?["pressedEnabled"], true))
            {
                var exit = ReadScalar(data?["pressedFadeOut"], "0.15");
                var driver = ReadBool(data?["longPressBuildup"], false)
                    ? Driver.LongPressProgress(exit)
                    : Driver.Transition("0", exit);
                var pressed = ReadStyle(data?["pressed"], DefaultPressed());
                effects.Add(builder.AddInteractionEffect((int)InteractionSignalMask.Pressed,
                    pressed, driver, 0));

                var flash = ReadScalar(data?["activatedFlash"], "0.25");
                if (ReadFloat(flash, 0.25f) > 0f)
                    effects.Add(builder.AddEventEffect(RangeRuleEvent.Activated, pressed,
                        Driver.Transition("0", flash), 0));
            }

            return builder.Complete(effects);
        }

        public static Conversion FromSpoilerStyler(string source, YamlNode reference,
            HashSet<long> usedRids)
        {
            var data = reference["data"];
            var builder = new Builder(source, reference, usedRids,
                long.MaxValue - 1_000_000);
            var cover = ReadStyle(data?["cover"], DefaultSpoiler());
            var conceal = ReadScalar(data?["concealFade"], "0.25");
            var reveal = ReadScalar(data?["revealFade"], "0.18");
            var effect = builder.AddSpoilerEffect(cover, Driver.Transition(conceal, reveal));
            return builder.Complete(new List<long> { effect });
        }

        public static Conversion FromLegacyHighlighter(string source, YamlNode reference,
            HashSet<long> usedRids, Color hover, Color click, float fade)
        {
            var builder = new Builder(source, reference, usedRids,
                long.MaxValue - 2_000_000);
            var effects = new List<long>(3)
            {
                builder.AddInteractionEffect((int)InteractionSignalMask.Hovered,
                    LegacyHighlightStyle.Solid(hover), Driver.Transition("0.18", "0.1"), 0),
                builder.AddInteractionEffect((int)InteractionSignalMask.Pressed,
                    LegacyHighlightStyle.Solid(click), Driver.Transition("0", "0.15"), 0),
            };
            if (fade > 0f)
                effects.Add(builder.AddEventEffect(RangeRuleEvent.Activated,
                    LegacyHighlightStyle.Solid(click),
                    Driver.Transition("0", F(fade)), 0));
            return builder.Complete(effects);
        }

        public static string BuildHighlightReference(string source, int anchorStart,
            HashSet<long> usedRids, LegacyHighlightStyle style, int priority, int order,
            out long modifierRid)
        {
            var builder = new Builder(source, anchorStart, usedRids,
                long.MaxValue - 3_000_000);
            modifierRid = builder.AddStandaloneHighlight(style, priority, order);
            return builder.ReferenceText;
        }

        public static LegacyHighlightStyle ReadHighlightStyle(YamlNode node)
            => ReadStyle(node, DefaultNormal());

        public static void ReplaceStylerField(MigrationContext ctx, YamlNode ownerData,
            YamlEntry stylerEntry, IReadOnlyList<long> effectRids)
        {
            ReplaceEffectField(ctx, ownerData, stylerEntry, "rules", effectRids);
        }

        public static void ReplaceEffectField(MigrationContext ctx, YamlNode ownerData,
            YamlEntry oldEntry, string fieldName, IReadOnlyList<long> effectRids)
        {
            var effectsEntry = ownerData.Entry(fieldName);
            if (effectsEntry == null)
            {
                ctx.Edit.Replace(oldEntry.Start, oldEntry.End,
                    EffectField(ctx.Edit.Source, oldEntry, fieldName, effectRids));
                return;
            }

            var additions = EffectItems(ctx.Edit.Source, effectsEntry, effectRids);
            ctx.Edit.Delete(oldEntry.Start, oldEntry.End);
            UnityYaml.AppendSequence(ctx, effectsEntry, additions,
                "InteractiveModifier rules has an invalid YAML shape.");
        }

        public static bool TryRid(YamlNode node, out long rid)
        {
            rid = 0;
            return node?["rid"]?.Scalar is { } value && long.TryParse(value, out rid);
        }

        public static long Rid(YamlNode node)
            => TryRid(node, out var rid)
                ? rid
                : throw new InvalidOperationException("Managed reference has no numeric rid.");

        private static string EffectField(string source, YamlEntry entry, string fieldName,
            IReadOnlyList<long> effectRids)
        {
            var pad = UnityYaml.Indent(source, entry.Start);
            var sb = new StringBuilder();
            if (effectRids.Count == 0)
                return sb.Append(pad).Append(fieldName).Append(": []\n").ToString();
            sb.Append(pad).Append(fieldName).Append(":\n");
            for (var i = 0; i < effectRids.Count; i++)
                sb.Append(pad).Append("- rid: ").Append(effectRids[i]).Append('\n');
            return sb.ToString();
        }

        private static string EffectItems(string source, YamlEntry entry,
            IReadOnlyList<long> effectRids)
        {
            var pad = UnityYaml.Indent(source, entry.Start);
            var sb = new StringBuilder();
            for (var i = 0; i < effectRids.Count; i++)
                sb.Append(pad).Append("- rid: ").Append(effectRids[i]).Append('\n');
            return sb.ToString();
        }

        private static LegacyHighlightStyle ReadStyle(YamlNode node,
            LegacyHighlightStyle fallback)
        {
            if (node == null) return fallback;
            var paint = node["paint"];
            var color = paint?["color"];
            var rgba = ReadColor(color, fallback.rgba);
            return new LegacyHighlightStyle(
                ReadInt(paint?["kind"], fallback.paintKind),
                rgba,
                paint?["swatch"]?.Scalar ?? fallback.swatch,
                ReadInt(node["height"], fallback.height),
                ReadScalar(node["padding"]?["x"], fallback.paddingX),
                ReadScalar(node["padding"]?["y"], fallback.paddingY),
                ReadScalar(node["cornerRadius"], fallback.radius),
                ReadInt(node["boxBreak"], fallback.boxBreak),
                ReadScalar(node["mergeThreshold"], fallback.mergeThreshold));
        }

        private static LegacyHighlightStyle DefaultNormal() =>
            new(1, Pack(new Color32(51, 128, 255, 64)), null, 1,
                "0.15", "0.05", "0.25", 1, "-1");

        private static LegacyHighlightStyle DefaultHover() =>
            new(1, Pack(new Color32(51, 128, 255, 26)), null, 1,
                "0.1", "0.06", "0.15", 0, "-1");

        private static LegacyHighlightStyle DefaultPressed() =>
            new(1, Pack(new Color32(51, 128, 255, 77)), null, 1,
                "0.1", "0.06", "0.15", 0, "-1");

        private static LegacyHighlightStyle DefaultSpoiler() =>
            new(1, Pack(new Color32(32, 34, 42, 246)), null, 1,
                "0.12", "0.08", "0.2", 0, "-1");

        private static string ReadScalar(YamlNode node, string fallback)
            => node?.Scalar ?? fallback;

        private static int ReadInt(YamlNode node, int fallback)
            => node?.Scalar is { } value && int.TryParse(value, out var parsed) ? parsed : fallback;

        private static uint ReadUInt(YamlNode node, uint fallback)
            => node?.Scalar is { } value && uint.TryParse(value, out var parsed) ? parsed : fallback;

        private static uint ReadColor(YamlNode node, uint fallback)
        {
            if (node?["rgba"] != null) return ReadUInt(node["rgba"], fallback);
            if (node?["r"] == null) return fallback;
            var r = ReadFloat(node["r"]?.Scalar, 1f);
            var g = ReadFloat(node["g"]?.Scalar, 1f);
            var b = ReadFloat(node["b"]?.Scalar, 1f);
            var a = ReadFloat(node["a"]?.Scalar, 1f);
            if (r <= 1f && g <= 1f && b <= 1f && a <= 1f)
                return Pack(new Color(r, g, b, a));
            return Pack(new Color32(Byte(r), Byte(g), Byte(b), Byte(a)));
        }

        private static float ReadFloat(string value, float fallback)
            => value != null && float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

        private static bool ReadBool(YamlNode node, bool fallback)
            => node?.Scalar is { } value ? value == "1" || value.Equals("true",
                StringComparison.OrdinalIgnoreCase) : fallback;

        private static uint Pack(Color color) => Pack((Color32)color);

        private static uint Pack(Color32 color)
            => color.r | (uint)color.g << 8 | (uint)color.b << 16 | (uint)color.a << 24;

        private static byte Byte(float value)
            => (byte)Mathf.Clamp(Mathf.RoundToInt(value), byte.MinValue, byte.MaxValue);

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private readonly struct Driver
        {
            public readonly string type;
            public readonly string data;

            private Driver(string type, string data)
            {
                this.type = type;
                this.data = data;
            }

            public static Driver Instant => new("InstantPlayback", null);

            public static Driver Transition(string enter, string exit) =>
                new("TransitionPlayback",
                    "enterDuration: " + enter + "\n" +
                    "exitDuration: " + exit + "\n" +
                    "easing: 1\nclock: 1\nanimateInitialMatch: 0\n");

            public static Driver LongPressProgress(string exitDuration) =>
                new("SignalProgressPlayback",
                    "signal: 0\ninputMin: 0\ninputMax: 1\noutputMin: 0.4\noutputMax: 1\n" +
                    "sampleOnEnter: 0\nenterWeight: 1\nexitDuration: " + exitDuration +
                    "\nexitEasing: 1\nexitClock: 1\n");
        }

        private sealed class Builder
        {
            private readonly HashSet<long> usedRids;
            private readonly string referencePad;
            private readonly StringBuilder references = new();
            private long nextRid;

            public Builder(string source, YamlNode reference, HashSet<long> usedRids,
                long firstRid)
                : this(source, reference.Start, usedRids, firstRid)
            {
            }

            public Builder(string source, int anchorStart, HashSet<long> usedRids,
                long firstRid)
            {
                this.usedRids = usedRids;
                referencePad = UnityYaml.Indent(source, anchorStart);
                nextRid = firstRid;
            }

            public string ReferenceText => references.ToString();

            public long AddStandaloneHighlight(LegacyHighlightStyle style, int priority, int order)
                => AddHighlight(style, priority, order);

            public long AddInteractionEffect(int required, LegacyHighlightStyle style,
                Driver driver, int order)
            {
                var selector = AddInteractionSelector(required);
                return AddModifierRule(selector, driver, style, RangeRuleEvent.None, order);
            }

            public long AddAnyInteractionEffect(int first, int second,
                LegacyHighlightStyle style, Driver driver, int order)
            {
                var firstSelector = AddInteractionSelector(first);
                var secondSelector = AddInteractionSelector(second);
                var selector = AddReference("AnyRangeStateSelector",
                    "selectors:\n- rid: " + firstSelector + "\n- rid: " + secondSelector + "\n");
                return AddModifierRule(selector, driver, style, RangeRuleEvent.None, order);
            }

            public long AddEventEffect(RangeRuleEvent trigger, LegacyHighlightStyle style,
                Driver driver, int order)
                => AddModifierRule(-2, driver, style, trigger, order);

            public long AddSpoilerEffect(LegacyHighlightStyle style, Driver driver)
            {
                var selector = AddReference("SpoilerConcealedRangeStateSelector", null);
                return AddModifierRule(selector, driver, style, RangeRuleEvent.None, 1);
            }

            public Conversion Complete(List<long> effects)
                => new(effects.ToArray(), references.ToString());

            private long AddModifierRule(long selector, Driver driver,
                LegacyHighlightStyle style, RangeRuleEvent trigger, int order)
            {
                var driverRid = AddReference(driver.type, driver.data);
                var highlightRid = AddHighlight(style, RangeDecorationPriorities.Interactive, order);
                return AddReference("ModifierRule",
                    "selector:\n  rid: " + selector + "\n" +
                    "driver:\n  rid: " + driverRid + "\n" +
                    "scope: 1\ntrigger: " + (int)trigger + "\npriority: 0\n" +
                    "modifierTemplate:\n  rid: " + highlightRid + "\n" +
                    "dirtyStage: 1\n");
            }

            private long AddInteractionSelector(int required)
                => AddReference("InteractionRangeStateSelector",
                    "required: " + required + "\nexcluded: 0\n");

            private long AddHighlight(LegacyHighlightStyle style, int priority, int order)
            {
                var providerRid = AddReference("GlobalSettingsPaintProvider", null);
                var data = new StringBuilder();
                data.Append("priority: ").Append(priority).Append('\n')
                    .Append("presentation:\n");
                HighlightPresentationYaml.Append(data, "  ", style, providerRid, order);
                return AddReference("HighlightModifier", data.ToString());
            }

            private long AddReference(string type, string data)
            {
                var rid = Allocate();
                var fieldPad = referencePad + "  ";
                var dataPad = fieldPad + "  ";
                references.Append(referencePad).Append("- rid: ").Append(rid).Append('\n')
                    .Append(fieldPad).Append("type: {class: ").Append(type)
                    .Append(", ns: LightSide, asm: LightSide.UniText}\n")
                    .Append(fieldPad).Append("data:");
                if (string.IsNullOrEmpty(data))
                {
                    references.Append(" \n");
                    return rid;
                }
                references.Append('\n');
                var lineStart = 0;
                while (lineStart < data.Length)
                {
                    var lineEnd = data.IndexOf('\n', lineStart);
                    if (lineEnd < 0) lineEnd = data.Length;
                    references.Append(dataPad).Append(data, lineStart, lineEnd - lineStart)
                        .Append('\n');
                    lineStart = lineEnd + 1;
                }
                return rid;
            }

            private long Allocate()
            {
                while (!usedRids.Add(nextRid)) nextRid--;
                return nextRid--;
            }

        }
    }
}
