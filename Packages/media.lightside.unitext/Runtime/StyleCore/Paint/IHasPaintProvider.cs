namespace LightSide
{
    /// <summary>Implemented by modifiers that expose an <see cref="IPaintProvider"/>, so the editor can populate the <c>@paints</c> swatch dropdown.</summary>
    public interface IHasPaintProvider
    {
        IPaintProvider PaintProvider { get; }
    }
}
