using System;

namespace LightSide
{
    /// <summary>
    /// Supplies one font face without requiring an SFNT byte source. Implementations keep native resources
    /// alive until every retained backend and active native call has released them, expose a non-empty identity and
    /// positive design units, and keep <see cref="FaceInfo"/> immutable for that identity.
    /// </summary>
    internal interface IFontFaceBackend : IDisposable
    {
        string Identity { get; }
        FaceInfo FaceInfo { get; }
        int UnitsPerEm { get; }

        /// <summary>Returns an independently disposable reference to the same immutable face.</summary>
        IFontFaceBackend Retain();
        bool TryGetGlyph(uint codepoint, out uint glyphIndex);
        int GetGlyphAdvance(uint glyphIndex);

        /// <summary>
        /// Writes glyphs in design units. Returned clusters index <paramref name="context"/>; the caller
        /// owns scaling, style overrides and final cluster rebasing. A negative result reports the required
        /// output capacity without writing partial data.
        /// </summary>
        int Shape(ReadOnlySpan<int> context, int itemOffset, int itemLength,
            RawShapedGlyph[] output);
    }

    internal interface IColorGlyphBackend
    {
        /// <summary>Transfers any pooled pixel buffer in a successful result to the caller.</summary>
        bool TryRenderGlyph(uint glyphIndex, int pixelSize,
            out FreeType.RenderedGlyph result);
    }
}
