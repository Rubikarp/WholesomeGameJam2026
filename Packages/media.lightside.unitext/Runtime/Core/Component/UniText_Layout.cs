using UnityEngine.UI;

namespace LightSide
{
    public partial class UniText : ILayoutElement, ILayoutController
    {
        private float cachedPreferredWidth;

        #region ILayoutElement

        void ILayoutElement.CalculateLayoutInputHorizontal()
        {
            EnsureFirstPassComplete();

            UniTextDebug.BeginSample("UniText.CalculateLayoutInputHorizontal");

            cachedPreferredWidth = 0;

            if (!sourceText.IsEmpty && textProcessor != null && textProcessor.HasValidFirstPassData)
            {
                var effectiveFontSize = autoSize ? maxFontSize : fontSize;
                cachedPreferredWidth = textProcessor.GetPreferredWidth(effectiveFontSize,
                    MeasureTrailingWhitespace);
            }

            UniTextDebug.EndSample();
        }

        void ILayoutElement.CalculateLayoutInputVertical()
        {
            UniTextDebug.BeginSample("UniText.CalculateLayoutInputVertical");
            EnsureLayoutComputed();
            UniTextDebug.EndSample();
        }

        float ILayoutElement.minWidth => 0;
        float ILayoutElement.preferredWidth => cachedPreferredWidth + Padding.x + Padding.z;
        float ILayoutElement.flexibleWidth => -1;

        float ILayoutElement.minHeight => 0;
        float ILayoutElement.preferredHeight => PreferredHeight + Padding.y + Padding.w;
        float ILayoutElement.flexibleHeight => -1;

#if UNITEXT_UGUI_2_6
        float ILayoutElement.maxWidth => LayoutUtility.DefaultMaxSize;
        float ILayoutElement.maxHeight => LayoutUtility.DefaultMaxSize;
#endif

        int ILayoutElement.layoutPriority => 0;

        #endregion

        #region ILayoutController

        void ILayoutController.SetLayoutHorizontal() { }

        void ILayoutController.SetLayoutVertical() => EnsureLayoutFit();

        #endregion
    }
}
