namespace LightSide
{
    /// <summary>
    /// How media reached the field — lets a single <see cref="MediaContent"/> handler tell a clipboard paste,
    /// a drag-and-drop, and a picker selection apart when it cares, and treat them the same when it doesn't.
    /// </summary>
    public enum MediaSource
    {
        Paste,
        Drop,
        Pick,
    }
}
