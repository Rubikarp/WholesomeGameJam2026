namespace LightSide.Samples
{
    internal sealed class OverflowSlide : BasicUsageSlide
    {
        public override string Text =>
            "… <b>Ellipsis & truncate on overflow</b>\n\n" +
            "<ellipsis=1>END: this deliberately long passage keeps going well past what the box can show, so the layout clips it and appends an ellipsis marker exactly at the cut to signal that more text follows beyond the visible area, and it keeps going and going and going and going and going.</ellipsis>\n\n" +
            "<truncate=1>TRUNCATE drops the overflow the same way but leaves no marker — the text simply stops where it no longer fits and nothing signals the omission, so keep filling and filling and filling and filling.</truncate>\n\n" +
            "<size=72%><color=#888>Position: 1 = end · 0 = start · 0.5 = middle. Triggers only when content overflows the box.</color></size>";
    }
}
