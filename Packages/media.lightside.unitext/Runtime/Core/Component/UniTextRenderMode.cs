namespace LightSide
{
    /// <summary>
    /// Text rendering mode: SDF (rounded corners on effects) or MSDF (sharp corners).
    /// </summary>
    public enum UniTextRenderMode : byte
    {
        /// <summary>Single-channel SDF. Naturally rounds corners on outline/underlay effects.</summary>
        SDF = 0,
        /// <summary>Multi-channel SDF. Preserves sharp corners on outline/underlay effects.</summary>
        MSDF = 1,
    }
}