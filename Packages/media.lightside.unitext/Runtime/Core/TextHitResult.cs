using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Result of a range-style text hit test (bounding-box semantics) — identifies the glyph
    /// whose rect contains the point. Returned by
    /// <see cref="UniTextBase.HitTestRange(Vector2, float)"/> and its screen-coordinate
    /// overload. Use this when asking <em>"which entity is at this position"</em> (links,
    /// hashtags, hover-style ranges). For <em>"where should the caret go"</em> queries call
    /// <see cref="UniTextBase.HitTestCaret(Vector2, Camera)"/> instead — caret semantics need
    /// edge-snap (left half of glyph N → cluster N, right half → cluster N+1) which this
    /// type does not represent.
    /// </summary>
    public readonly struct TextHitResult
    {
        /// <summary>True if a glyph was hit.</summary>
        public readonly bool hit;

        /// <summary>Index of the hit glyph in the positioned glyphs array.</summary>
        public readonly int glyphIndex;

        /// <summary>Cluster index (codepoint offset in source text).</summary>
        public readonly int cluster;

        /// <summary>Position of the hit glyph.</summary>
        public readonly Vector2 glyphPosition;

        /// <summary>Distance from the hit point to the glyph center.</summary>
        public readonly float distance;

        /// <summary>Represents no hit.</summary>
        public static readonly TextHitResult None = new();

        public TextHitResult(int glyphIndex, int cluster, Vector2 glyphPosition, float distance)
        {
            hit = true;
            this.glyphIndex = glyphIndex;
            this.cluster = cluster;
            this.glyphPosition = glyphPosition;
            this.distance = distance;
        }
    }
}
