using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Per-parse rendering projection for editable hosts, bundled into one value so the parser holds no
    /// editing-layer state between parses. A <see langword="default"/> instance is a static parse: markup
    /// is stripped and styled, nothing stays visible.
    /// </summary>
    internal readonly struct ParseProjection
    {
        /// <summary>
        /// The characters are final: rules match and their styles apply, but nothing is stripped or
        /// inserted unless <see cref="stripCompleted"/> asks for it — matched tags and markers stay
        /// visible text, so what the user typed is exactly what is on screen.
        /// </summary>
        public readonly bool plainText;

        /// <summary>With <see cref="plainText"/>: complete tag pairs strip and style; incomplete tags stay literal.</summary>
        public readonly bool stripCompleted;

        /// <summary>Source char range whose markup stays visible under <see cref="stripCompleted"/>; -1 = strip every tag.</summary>
        public readonly int revealStart;
        public readonly int revealEnd;

        /// <summary>Rules styling the visible markup tag characters; null leaves them unstyled.</summary>
        public readonly List<ChromeRule> chrome;
        public readonly IReadOnlyList<BaseModifier> chromeModifiers;

        public ParseProjection(bool plainText, bool stripCompleted,
            int revealStart = -1, int revealEnd = -1, List<ChromeRule> chrome = null,
            IReadOnlyList<BaseModifier> chromeModifiers = null)
        {
            this.plainText = plainText;
            this.stripCompleted = stripCompleted;
            this.revealStart = revealStart;
            this.revealEnd = revealEnd;
            this.chrome = chrome;
            this.chromeModifiers = chromeModifiers;
        }
    }
}
