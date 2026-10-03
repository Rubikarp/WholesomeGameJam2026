using System;

namespace LightSide
{
    /// <summary>
    /// Truncates text that overflows its container without rendering an ellipsis.
    /// </summary>
    /// <remarks>
    /// Parameter: optional position (0-1) controlling where text is removed:
    /// <c>0</c> — from the start, <c>0.5</c> — from the middle, <c>1</c> (default) — from the end.
    /// </remarks>
    /// <seealso cref="EllipsisModifier"/>
    [Serializable]
    [TypeGroup("Layout", 4)]
    [TypeDescription("Truncates overflowing text without an ellipsis.")]
    public class TruncateModifier : EllipsisModifier
    {
        protected override bool ShowEllipsis => false;
    }
}
