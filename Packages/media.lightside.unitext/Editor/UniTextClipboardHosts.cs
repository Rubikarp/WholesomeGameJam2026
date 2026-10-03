namespace LightSide
{
    /// <summary>Clipboard editing surfaces for UniText's serialized value types.</summary>
    internal sealed class PaintSwatchClipboardHost : ClipboardValueHost<PaintSwatch> { }

    internal sealed class PaintRefClipboardHost : ClipboardValueHost<PaintRef> { }

    internal sealed class UnitValueClipboardHost : ClipboardValueHost<UnitValue> { }

    internal sealed class UnitVector2ClipboardHost : ClipboardValueHost<UnitVector2> { }

    internal sealed class SpriteColorRefClipboardHost : ClipboardValueHost<SpriteColorRef> { }

    internal sealed class FontFamilyClipboardHost : ClipboardValueHost<FontFamily> { }

    internal sealed class GlyphOverrideClipboardHost
        : ClipboardValueHost<UniTextFont.GlyphOverride> { }

    internal sealed class AxisDefaultClipboardHost
        : ClipboardValueHost<UniTextFont.AxisDefault> { }

    internal sealed class FaceInfoOverrideClipboardHost
        : ClipboardValueHost<UniTextSystemFont.FaceInfoOverride> { }
}
