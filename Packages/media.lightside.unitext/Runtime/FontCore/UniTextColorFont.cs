using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A color-font asset — embedded color-font bytes (CBDT/sbix bitmap or COLRv0/COLRv1 vector) rendered
    /// through the shared color-glyph pipeline. The color analog of <see cref="UniTextFont"/>: add it to a
    /// Font Stack like any other font and it serves the codepoints it covers via the normal resolution chain.
    /// The always-on system emoji font (<see cref="EmojiFont"/>) stays the privileged provider for
    /// emoji-presentation codepoints.
    /// </summary>
    /// <remarks>
    /// All color fonts share one color atlas whose tile pixel size grows to fit the largest color font loaded.
    /// Rasterization runs through FreeType/Blend2D on desktop, editor, iOS and Android. WebGL has no Blend2D, so
    /// an embedded color-font asset does not rasterize there (color-presentation codepoints fall back to the
    /// system emoji font; other color glyphs do not render).
    /// </remarks>
    public partial class UniTextColorFont : UniTextFont
    {
        [SerializeField, StateField(nameof(ApplyColorPixelSizeChange))]
        [Tooltip("Pixel size at which color glyphs rasterize into the shared color atlas. Bitmap (CBDT/sbix) fonts snap to the nearest available strike; vector (COLR) fonts render at this size. The shared atlas grows to fit the largest color font.")]
        [Range(16, 512)]
        private int colorPixelSize = ColorFontCore.DefaultSize;

        /// <summary>Pixel size at which this font's color glyphs rasterize. Bitmap fonts snap to the nearest strike; vector fonts render at this size.</summary>
        public int ColorPixelSize
        {
            get => colorPixelSize > 0 ? colorPixelSize : ColorFontCore.DefaultSize;
            set => SetColorPixelSizeState(value);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetColorPixelSizeState(Mathf.Clamp(colorPixelSize, 8, GlyphAtlas.PageSize - 8));
            CaptureRuntimeSlot();
        }

        protected override Core BuildRuntime(byte[] bytes)
            => BuildRuntime(GetOrCreateSource(bytes));

        protected override Core CreateRuntime()
        {
            var source = CaptureEmbeddedFontSource();
            return source == null
                ? null
                : BuildRuntimeFromSource(source, typeof(UniTextColorFont));
        }

        private protected override Core BuildRuntime(FontSource source)
            => CreateColorRuntime(source, faceInfo.faceIndex, ColorPixelSize, name);

        internal override FontRuntimeSlot CaptureRuntimeSlot()
        {
            if (GetType() != typeof(UniTextColorFont)) return CaptureEagerRuntimeSlot();
            var pixelSize = ColorPixelSize;
            GlyphAtlas.CreateColorInstance(pixelSize);
            return CaptureEmbeddedRuntimeSlot(CaptureColorRuntimeFactory(pixelSize));
        }

        internal override void RefreshUnmaterializedRuntimeSlot()
        {
            if (GetType() != typeof(UniTextColorFont)) return;
            ReplaceUnmaterializedRuntimeFactory(CaptureEmbeddedRuntimeFactory(
                CaptureColorRuntimeFactory(ColorPixelSize)));
        }

        private static Func<FontSource, RuntimeSnapshot, Core> CaptureColorRuntimeFactory(
            int pixelSize)
            => (source, snapshot) => CreateColorRuntime(
                source, snapshot.faceInfo.faceIndex, pixelSize, snapshot.name);

        private static Core CreateColorRuntime(FontSource source, int faceIndex,
            int pixelSize, string sourceName)
        {
            var font = new ColorFontCore();
            return font.LoadFromSource(source, faceIndex < 0 ? 0 : faceIndex,
                pixelSize, sourceName)
                ? font
                : null;
        }

        internal override FontSource CaptureFontSource()
            => GetType() == typeof(UniTextColorFont)
                ? CaptureEmbeddedFontSource()
                : Runtime?.Source;

        private void ApplyColorPixelSizeChange(StateMember member, int previous, ref int current)
        {
            current = Mathf.Clamp(current, 8, GlyphAtlas.PageSize - 8);
            if (current == previous) return;
            InvalidateRuntime();
            PublishStateChange(member);
        }
    }
}
