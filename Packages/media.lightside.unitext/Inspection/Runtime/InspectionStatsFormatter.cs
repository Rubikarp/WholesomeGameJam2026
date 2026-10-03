using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>Collects and formats whole-component text statistics (chars, runs, fonts, scripts, languages, modifiers, …) as a rich-text card.</summary>
    public static class InspectionStatsFormatter
    {
        private const string LineGap = "\n<size=7> </size>\n";
        private const string Head = "#ffffff";
        private const string Warn = "#ff5a47";
        private const string Val = "#cfd3dc";
        private const string Prim = "#8a90a0";

        private static readonly List<(int id, int count)> fonts = new();
        private static readonly List<(UnicodeScript script, int count)> scripts = new();
        private static readonly List<(byte lang, int count)> langs = new();
        private static readonly List<(string type, int count)> mods = new();
        private static readonly List<(int start, int end, BaseModifier modifier, string parameter)> spanScratch = new();

        public static string Format(UniTextBase text, StringBuilder sb = null)
        {
            if (text == null) return string.Empty;
            var processor = text.TextProcessor;
            if (processor == null) return string.Empty;
            var buf = processor.buf;

            sb ??= new StringBuilder(512);
            sb.Clear();

            var cpCount = buf.codepoints.count;
            var glyphs = text.ResultGlyphs;
            var glyphCount = glyphs.Length;
            var runCount = buf.shapedRuns.count;
            var lineCount = buf.lines.count;

            scripts.Clear();
            var scr = buf.scripts.data;
            var bidi = buf.bidiLevels.data;
            var rtlCp = 0;
            for (var i = 0; i < cpCount; i++)
            {
                if (scr != null && i < scr.Length) Tally(scripts, scr[i]);
                if (bidi != null && i < bidi.Length && (bidi[i] & 1) != 0) rtlCp++;
            }

            fonts.Clear();
            var notdef = 0;
            var provider = text.FontProvider;
            var primary = provider != null ? provider.PrimaryFontId : 0;
            for (var i = 0; i < glyphCount; i++)
            {
                var g = glyphs[i];
                Tally(fonts, g.fontId);
                if (g.glyphId == 0) notdef++;
            }

            langs.Clear();
            var sruns = buf.shapedRuns.Span;
            for (var r = 0; r < sruns.Length; r++)
                if (sruns[r].language != 0)
                    Tally(langs, sruns[r].language);

            mods.Clear();
            var parser = text.AttributeParser;
            if (parser != null)
            {
                parser.CollectSpansForInspection(spanScratch);
                for (var i = 0; i < spanScratch.Count; i++)
                    Tally(mods, spanScratch[i].modifier?.GetType().Name);
            }

            var graphemes = 0;
            var gb = buf.graphemeBreaks.data;
            if (gb != null)
                for (var i = 0; i < cpCount && i < gb.Length; i++)
                    if (gb[i]) graphemes++;

            sb.Append("<b><color=").Append(Head).Append(">Text Statistics</color></b>\n");

            Field(sb, "Chars"); sb.Append(cpCount);
            Sep(sb); Field(sb, "Glyphs"); sb.Append(glyphCount);
            Sep(sb); Field(sb, "Runs"); sb.Append(runCount);
            Sep(sb); Field(sb, "Lines"); sb.Append(lineCount).Append('\n');

            Field(sb, "Graphemes"); sb.Append(graphemes);
            Sep(sb); Field(sb, "Size"); sb.Append(processor.ResultWidth.ToString("0.#")).Append(" × ").Append(processor.ResultHeight.ToString("0.#")).Append('\n');

            Field(sb, "Direction"); sb.Append(buf.baseDirection);
            if (cpCount > 0) sb.Append("  (").Append(Mathf.RoundToInt(100f * rtlCp / cpCount)).Append("% RTL)");
            sb.Append('\n');

            Field(sb, "Scripts"); sb.Append(scripts.Count).Append(":  ");
            for (var i = 0; i < scripts.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                Tag(sb, scripts[i].script.ToString(), scripts[i].count);
            }
            sb.Append('\n');

            Field(sb, "Fonts"); sb.Append(fonts.Count).Append(":  ");
            for (var i = 0; i < fonts.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var font = provider != null ? provider.GetFont(fonts[i].id) : null;
                Tag(sb, font != null && !string.IsNullOrEmpty(font.Name) ? font.Name : fonts[i].id.ToString(), fonts[i].count);
                if (fonts[i].id == primary) sb.Append(" <color=").Append(Prim).Append(">(primary)</color>");
            }
            sb.Append('\n');

            if (langs.Count > 0)
            {
                Field(sb, "Languages");
                for (var i = 0; i < langs.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Tag(sb, LanguageRegistry.GetTag(langs[i].lang), langs[i].count);
                }
                sb.Append('\n');
            }

            if (notdef > 0)
            {
                Field(sb, ".notdef");
                sb.Append("<color=").Append(Warn).Append('>').Append(notdef).Append("</color>\n");
            }

            if (mods.Count > 0)
            {
                Field(sb, "Modifiers");
                for (var i = 0; i < mods.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    Tag(sb, mods[i].type, mods[i].count);
                }
            }

            return sb.Replace("\n", LineGap).ToString();
        }

        private static void Tag(StringBuilder sb, string name, int count)
            => sb.Append("<color=").Append(Val).Append('>').Append(name).Append("</color>×").Append(count);

        private static void Field(StringBuilder sb, string label)
            => sb.Append("<b><color=").Append(CardLabelColors.For(label)).Append('>').Append(label).Append(':').Append("</color></b> ");

        private static void Sep(StringBuilder sb) => sb.Append("; ");

        private static void Tally(List<(int id, int count)> list, int key)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i].id == key) { list[i] = (key, list[i].count + 1); return; }
            list.Add((key, 1));
        }

        private static void Tally(List<(UnicodeScript script, int count)> list, UnicodeScript key)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i].script == key) { list[i] = (key, list[i].count + 1); return; }
            list.Add((key, 1));
        }

        private static void Tally(List<(byte lang, int count)> list, byte key)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i].lang == key) { list[i] = (key, list[i].count + 1); return; }
            list.Add((key, 1));
        }

        private static void Tally(List<(string type, int count)> list, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            for (var i = 0; i < list.Count; i++)
                if (list[i].type == key) { list[i] = (key, list[i].count + 1); return; }
            list.Add((key, 1));
        }
    }
}
