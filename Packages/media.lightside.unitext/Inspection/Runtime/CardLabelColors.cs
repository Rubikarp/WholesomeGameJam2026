namespace LightSide.Inspection
{
    /// <summary>Shared label → color map for inspector cards. Singular (hover card) and plural (stats card) names of the same concept share a color.</summary>
    internal static class CardLabelColors
    {
        public static string For(string label) => label switch
        {
            "Category" => "#c792ea",
            "Chars" => "#c792ea",
            "Glyph ID" => "#82e08a",
            "Glyphs" => "#82e08a",
            "Font" => "#4fc3f7",
            "Fonts" => "#4fc3f7",
            "Script" => "#ffb86c",
            "Scripts" => "#ffb86c",
            "Dir" => "#f6f06b",
            "Direction" => "#f6f06b",
            "Advance" => "#7fdbca",
            "Graphemes" => "#7fdbca",
            "Offset" => "#ff9aa2",
            "Size" => "#ff9aa2",
            "Cluster" => "#7c83ff",
            "Languages" => "#7c83ff",
            "Line" => "#ff79c6",
            "Lines" => "#ff79c6",
            "Run" => "#aeea00",
            "Runs" => "#aeea00",
            "Modifiers" => "#ffd54f",
            "Fallback" => "#ff9a3c",
            ".notdef" => "#ff5a47",
            _ => "#cfd3dc"
        };
    }
}
