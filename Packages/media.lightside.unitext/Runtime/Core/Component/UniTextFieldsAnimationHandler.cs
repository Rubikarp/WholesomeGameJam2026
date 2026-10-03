using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Animates a text component's own fields from a Unity <c>Animator</c>: font size, colour, word
    /// wrap, auto-size and its min/max, and horizontal/vertical alignment. Add it to the
    /// <see cref="UniTextAnimationBridge"/> on a <see cref="UniText"/>; use
    /// <see cref="UniTextWorldFieldsAnimationHandler"/> for a <see cref="UniTextWorld"/>.
    /// </summary>
    [Serializable]
    [TypeGroup("Component Fields", 0)]
    [TypeDescription("Component font size, colour, wrap, auto-size, and alignment.")]
    public class UniTextFieldsAnimationHandler : AnimationHandler
    {
        private float fontSize;
        private Color color;
        private bool wordWrap;
        private bool autoSize;
        private float minFontSize;
        private float maxFontSize;
        private HorizontalAlignment horizontalAlignment;
        private VerticalAlignment verticalAlignment;

        /// <inheritdoc/>
        protected override void OnBind(UniTextBase host)
        {
            fontSize = host.FontSize;
            color = host.color;
            wordWrap = host.WordWrap;
            autoSize = host.AutoSize;
            minFontSize = host.MinFontSize;
            maxFontSize = host.MaxFontSize;
            horizontalAlignment = host.HorizontalAlignment;
            verticalAlignment = host.VerticalAlignment;
            OnBindExtra(host);
        }

        /// <inheritdoc/>
        protected override void OnDiff(UniTextBase host)
        {
            var flags = UniTextDirty.None;
            var appearanceChanged = false;

            if (!Mathf.Approximately(fontSize, host.FontSize)) { fontSize = host.FontSize; flags |= UniTextDirty.Layout; }
            if (color != host.color) { color = host.color; appearanceChanged = true; }
            if (wordWrap != host.WordWrap) { wordWrap = host.WordWrap; flags |= UniTextDirty.Layout; }
            if (autoSize != host.AutoSize) { autoSize = host.AutoSize; flags |= UniTextDirty.Layout; }
            if (!Mathf.Approximately(minFontSize, host.MinFontSize)) { minFontSize = host.MinFontSize; if (host.AutoSize) flags |= UniTextDirty.Layout; }
            if (!Mathf.Approximately(maxFontSize, host.MaxFontSize)) { maxFontSize = host.MaxFontSize; if (host.AutoSize) flags |= UniTextDirty.Layout; }
            if (horizontalAlignment != host.HorizontalAlignment) { horizontalAlignment = host.HorizontalAlignment; flags |= UniTextDirty.Positions; }
            if (verticalAlignment != host.VerticalAlignment) { verticalAlignment = host.VerticalAlignment; flags |= UniTextDirty.Positions; }

            flags |= OnDiffExtra(host);

            if (flags != UniTextDirty.None) host.SetDirty(flags);
            else if (appearanceChanged) host.SetAppearanceDirty();
        }

        /// <summary>Snapshots a subclass's extra fields into the baseline. Mirror of <see cref="OnDiffExtra"/>.</summary>
        protected virtual void OnBindExtra(UniTextBase host) { }

        /// <summary>Diffs a subclass's extra fields, refreshing their baseline, and returns the flags to fold into the single <see cref="UniTextBase.SetDirty"/> the base issues.</summary>
        protected virtual UniTextDirty OnDiffExtra(UniTextBase host) => UniTextDirty.None;
    }
}
