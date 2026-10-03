namespace LightSide
{
    /// <summary>
    /// Shared hard limits for parsing clipboard payloads. Clipboard content is
    /// attacker-controlled cross-app input — any web page or process can place megabytes
    /// of degenerate markup on the clipboard — so every paste parser (HTML, Markdown,
    /// vendor fragment) enforces the same budget: oversized input degrades to plain-text
    /// extraction, nesting past <see cref="MaxDepth"/> flattens, and output past
    /// <see cref="MaxOutputChars"/> truncates. Same posture Chromium applies to
    /// <c>DataTransfer</c> payloads.
    /// </summary>
    internal static class ClipboardBudget
    {
        internal const int MaxInputChars = 4 * 1024 * 1024;

        internal const int MaxDepth = 64;

        /// <summary>Matches the editable's own paste insertion cap, so nothing past it is ever parsed for nothing.</summary>
        internal const int MaxOutputChars = 1_000_000;
    }
}
