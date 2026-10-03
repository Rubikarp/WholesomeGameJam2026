using System.Collections.Generic;
using System.Text;

namespace LightSide.Inspection
{
    /// <summary>Formats an inspection snapshot into a rich-text card shown by the editor and runtime overlays.</summary>
    public static class InspectionCardFormatter
    {
        private const string Warn = "#ff5a47";
        private const string Soft = "#ff9a3c";
        private const string Accent = "#6fb7ff";
        private const string Index = "#9aa0ff";
        private const string LineGap = "\n<size=7> </size>\n";

        public static string Format(in TextInspectionSnapshot snap, IReadOnlyList<ModifierInspection> modifiers, StringBuilder sb = null)
        {
            if (!snap.hit) return string.Empty;

            sb ??= new StringBuilder(256);
            sb.Clear();

            var g = snap.glyph;
            sb.Append("<b>").Append(CharDisplay(g.codepoint)).Append("  U+").Append(g.codepoint.ToString("X4")).Append("</b>")
              .Append("   <color=").Append(Index).Append(">#").Append(g.glyphIndex).Append("</color>\n");

            Field(sb, "Category"); sb.Append(g.category).Append('\n');

            Field(sb, "Glyph ID");
            sb.Append(g.isNotdef ? "<color=" + Warn + ">0  .notdef (tofu)</color>" : g.glyphId.ToString()).Append('\n');

            Field(sb, "Font");
            sb.Append(string.IsNullOrEmpty(g.fontName) ? g.fontId.ToString() : g.fontName);
            if (g.isFallback) sb.Append("  <color=").Append(Soft).Append(">fallback</color>");
            sb.Append('\n');

            Field(sb, "Script"); sb.Append(g.script);
            Sep(sb); Field(sb, "Dir"); sb.Append(g.direction).Append(" (bidi ").Append(g.bidiLevel).Append(")\n");

            Field(sb, "Advance"); sb.Append(g.advanceX.ToString("0.##"));
            Sep(sb); Field(sb, "Offset"); sb.Append(g.offsetX.ToString("0.##")).Append(", ").Append(g.offsetY.ToString("0.##")).Append('\n');

            Field(sb, "Cluster"); sb.Append(g.cluster);
            Sep(sb); Field(sb, "Line"); sb.Append(g.lineIndex);
            Sep(sb); Field(sb, "Run"); sb.Append(g.runIndex).Append('\n');

            if (snap.run.valid)
            {
                Field(sb, "Run");
                sb.Append(snap.run.script).Append(' ').Append(snap.run.direction)
                  .Append("  font ").Append(snap.run.fontId).Append("  glyphs ").Append(snap.run.glyphCount)
                  .Append("  w ").Append(snap.run.width.ToString("0.#")).Append('\n');
            }

            if (snap.line.valid)
            {
                Field(sb, "Line");
                sb.Append('#').Append(snap.line.lineIndex).Append("  w ").Append(snap.line.widthPx.ToString("0.#"))
                  .Append(snap.line.isRtl ? "  RTL" : string.Empty)
                  .Append(snap.line.endedByMandatoryBreak ? "  ¶" : string.Empty).Append('\n');
            }

            if (modifiers != null && modifiers.Count > 0)
            {
                Field(sb, "Modifiers");
                for (var i = 0; i < modifiers.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("<color=").Append(Accent).Append('>').Append(modifiers[i].modifierType).Append("</color>");
                    if (!string.IsNullOrEmpty(modifiers[i].parameter))
                        sb.Append('(').Append(modifiers[i].parameter).Append(')');
                }
                for (var i = 0; i < modifiers.Count; i++)
                {
                    if (string.IsNullOrEmpty(modifiers[i].resolved)) continue;
                    sb.Append('\n').Append("<color=").Append(Accent).Append('>')
                      .Append(modifiers[i].modifierType).Append("</color>  <color=")
                      .Append(Index).Append('>').Append(modifiers[i].resolved).Append("</color>");
                }
            }

            return sb.Replace("\n", LineGap).ToString();
        }

        private static void Field(StringBuilder sb, string label)
            => sb.Append("<b><color=").Append(ColorFor(label)).Append('>').Append(label).Append(':').Append("</color></b> ");

        private static void Sep(StringBuilder sb) => sb.Append("; ");

        private static string ColorFor(string label) => CardLabelColors.For(label);

        private static string CharDisplay(int codepoint)
        {
            if (codepoint <= 0x20 || codepoint == '<' || codepoint == '>' ||
                !Utf16.IsUnicodeScalar(codepoint))
                return "·";
            return char.ConvertFromUtf32(codepoint);
        }
    }
}
