using System;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace LightSide.Samples
{
    /// <summary>
    /// Drives a B/I/U + color formatting panel the Google-Docs way: toggles mirror the caret
    /// state (pending typing styles included) and flip the style on click; the palette icon
    /// tints to the color at the caret, and clicking applies a random pastel from the list.
    /// Add to a <see cref="CaretContextBehavior"/>'s handlers and wire the panel controls.
    /// </summary>
    [Serializable]
    [TypeDescription("Formatting panel: B/I/U toggles + random-pastel color button (Docs-style)")]
    public sealed class FormattingPanelHandler : CaretContextHandler
    {
        [SerializeField, StatePassive] private Toggle boldToggle;
        [SerializeField, StatePassive] private Toggle italicToggle;
        [SerializeField, StatePassive] private Toggle underlineToggle;
        [SerializeField, StatePassive] private Button colorButton;
        [SerializeField, StatePassive] private Graphic paletteIcon;

        [SerializeField, StatePassive]
        [Tooltip("Random pick on color-button click. Bright pastels that read well on dark backgrounds.")]
        private string[] palette =
        {
            "#FFB3BA", "#FFDFBA", "#FFFFBA", "#BAFFC9",
            "#BAE1FF", "#D7BAFF", "#FFBAF2", "#B3FFF1",
        };

        [NonSerialized] private UniTextEditable editable;

        protected override void OnAttach(UniTextEditable owner)
        {
            editable = owner;
            if (boldToggle != null) boldToggle.onValueChanged.AddListener(OnBold);
            if (italicToggle != null) italicToggle.onValueChanged.AddListener(OnItalic);
            if (underlineToggle != null) underlineToggle.onValueChanged.AddListener(OnUnderline);
            if (colorButton != null) colorButton.onClick.AddListener(OnColor);
            Sync();
        }

        protected override void OnDetach(UniTextEditable owner)
        {
            if (boldToggle != null) boldToggle.onValueChanged.RemoveListener(OnBold);
            if (italicToggle != null) italicToggle.onValueChanged.RemoveListener(OnItalic);
            if (underlineToggle != null) underlineToggle.onValueChanged.RemoveListener(OnUnderline);
            if (colorButton != null) colorButton.onClick.RemoveListener(OnColor);
            editable = null;
        }

        public override void OnContextChanged(in CaretContext change) => Sync();

        private void Sync()
        {
            if (editable == null) return;
            boldToggle?.SetIsOnWithoutNotify(editable.IsStyleActive<BoldModifier>());
            italicToggle?.SetIsOnWithoutNotify(editable.IsStyleActive<ItalicModifier>());
            underlineToggle?.SetIsOnWithoutNotify(editable.IsStyleActive<UnderlineModifier>());

            if (paletteIcon == null) return;
            paletteIcon.color = editable.TryGetStyleParameter<ColorModifier>(out var hex)
                                && ColorParsing.TryParse(hex, out var color)
                ? (Color)color
                : Color.white;
        }

        private void OnBold(bool _) => Flip<BoldModifier>(boldToggle);
        private void OnItalic(bool _) => Flip<ItalicModifier>(italicToggle);
        private void OnUnderline(bool _) => Flip<UnderlineModifier>(underlineToggle);

        private void Flip<T>(Toggle toggle) where T : BaseModifier, new()
        {
            if (editable == null) return;
            var on = editable.ToggleStyle<T>();
            toggle.SetIsOnWithoutNotify(on);
        }

        private void OnColor()
        {
            if (editable == null || palette == null || palette.Length == 0) return;
            var hex = palette[Random.Range(0, palette.Length)];
            editable.ApplyStyle<ColorModifier>(hex);
            if (paletteIcon != null && ColorParsing.TryParse(hex, out var color))
                paletteIcon.color = color;
        }
    }
}
