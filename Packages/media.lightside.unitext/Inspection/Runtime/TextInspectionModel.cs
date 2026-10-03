using System.Globalization;

namespace LightSide.Inspection
{
    /// <summary>Inspection data for a single positioned glyph: identity, shaping metrics, ink box, and the run/line it belongs to.</summary>
    public struct GlyphInspection
    {
        public bool valid;
        public int glyphIndex;
        public int cluster;
        public int codepoint;
        public int glyphId;

        /// <summary>True when the glyph resolved to .notdef (glyphId 0) — a missing-glyph "tofu" box.</summary>
        public bool isNotdef;

        public int fontId;
        public string fontName;
        public bool isFallback;
        public UnicodeScript script;
        public byte bidiLevel;
        public TextDirection direction;
        public UnicodeCategory category;

        public float advanceX;
        public float advanceY;

        /// <summary>Shaping offset of the glyph from the pen position (diacritic / mark positioning).</summary>
        public float offsetX;

        /// <summary>Shaping offset of the glyph from the pen position (diacritic / mark positioning).</summary>
        public float offsetY;

        public float left;
        public float top;
        public float right;
        public float bottom;

        public float x;
        public float y;

        public int runIndex;
        public int lineIndex;
    }

    /// <summary>Inspection data for the shaped run a glyph belongs to (uniform script, direction, font, language).</summary>
    public struct RunInspection
    {
        public bool valid;
        public int runIndex;
        public TextRange range;
        public UnicodeScript script;
        public TextDirection direction;
        public byte bidiLevel;
        public byte language;
        public int fontId;
        public int glyphCount;
        public float width;
    }

    /// <summary>Inspection data for the line a glyph belongs to.</summary>
    public struct LineInspection
    {
        public bool valid;
        public int lineIndex;
        public TextRange range;
        public int runCount;
        public int glyphCount;
        public float width;
        public float widthPx;
        public float trailingWhitespace;
        public bool isRtl;
        public float startMargin;
        public bool endedByMandatoryBreak;
    }

    /// <summary>One modifier span covering a queried codepoint, in codepoint space.</summary>
    public readonly struct ModifierInspection
    {
        public readonly string modifierType;
        public readonly string parameter;
        public readonly int start;
        public readonly int end;
        /// <summary>Declared parameters resolved on the probed cluster's range — cascade plus
        /// owned values — as <c>id=value</c> pairs, or null when the modifier declares none.</summary>
        public readonly string resolved;

        public ModifierInspection(string modifierType, string parameter, int start, int end,
            string resolved = null)
        {
            this.modifierType = modifierType;
            this.parameter = parameter;
            this.start = start;
            this.end = end;
            this.resolved = resolved;
        }
    }

    /// <summary>Full result of probing a point in laid-out text: the hit glyph plus its enclosing run and line.</summary>
    public struct TextInspectionSnapshot
    {
        public bool hit;
        public GlyphInspection glyph;
        public RunInspection run;
        public LineInspection line;
    }
}
