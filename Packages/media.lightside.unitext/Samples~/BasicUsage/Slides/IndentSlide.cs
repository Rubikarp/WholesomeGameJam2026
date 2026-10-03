namespace LightSide.Samples
{
    internal sealed class IndentSlide : BasicUsageSlide
    {
        public override string Text =>
            "⇥ <b>Indent — holds across wrapped lines</b>\n\n" +
            "Reference line at the container edge.\n" +
            "<indent=2em>Indented 2em: the margin persists across every soft-wrapped line of this long paragraph until the closing tag, then the next line snaps back to the edge.</indent>\n" +
            "<indent=1.5em>outer 1.5em <indent=1.5em>nested adds to 3em</indent></indent>\n" +
            "<indent=8%>eight percent of the box width</indent>";
    }
}
