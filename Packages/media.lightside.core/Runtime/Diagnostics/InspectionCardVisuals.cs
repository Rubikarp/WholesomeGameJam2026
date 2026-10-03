using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Applies the shared visual contract used by retained-mode inspection cards.</summary>
    public static class InspectionCardVisuals
    {
        private static Font monoFont;
        private static Func<string> monoFontName;

        /// <summary>Optional OS monospace family resolver used when the shared font is created.</summary>
        public static Func<string> MonoFontName
        {
            get => monoFontName;
            set
            {
                monoFontName = value;
                monoFont = null;
            }
        }

        /// <summary>Applies the shared card typography, padding, colour, and size constraints.</summary>
        public static void Apply(VisualElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            element.style.maxWidth = 480f;
            element.style.paddingLeft = 14f;
            element.style.paddingRight = 14f;
            element.style.paddingTop = 12f;
            element.style.paddingBottom = 12f;
            element.style.backgroundColor = new Color(0.09f, 0.09f, 0.11f, 0.96f);
            element.style.color = new Color(0.93f, 0.95f, 0.97f);
            element.style.fontSize = 14f;
            element.style.whiteSpace = WhiteSpace.Normal;
            element.style.borderTopLeftRadius = 4f;
            element.style.borderTopRightRadius = 4f;
            element.style.borderBottomLeftRadius = 4f;
            element.style.borderBottomRightRadius = 4f;
            monoFont ??= CreateMonoFont();
            if (monoFont != null) element.style.unityFont = monoFont;
        }

        private static Font CreateMonoFont()
        {
            var native = monoFontName?.Invoke();
            var names = string.IsNullOrEmpty(native)
                ? new[] { "Courier New", "Courier", "monospace" }
                : new[] { native, "Courier New", "Courier", "monospace" };
            var font = Font.CreateDynamicFontFromOSFont(names, 14);
            if (font != null) font.hideFlags = HideFlags.HideAndDontSave;
            return font;
        }
    }
}
