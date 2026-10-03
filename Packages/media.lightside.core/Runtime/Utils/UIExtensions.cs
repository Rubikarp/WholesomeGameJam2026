using System;
using UnityEngine;

namespace LightSide
{
    public static class UIExtensions
    {
        /// <summary>Stretches a RectTransform to all four edges of its parent without changing its pivot or sibling order.</summary>
        public static void StretchToParent(this RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        public static void SafeInvoke(this Action action)
        {
            if (action != null)
            {
                foreach (var d in action.GetInvocationList())
                {
                    try
                    {
                        ((Action)d)();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }
        }

        public static Rect Split(this Rect rect, int index, int count)
        {
            var totalWidth = (int)rect.width;
            var width = totalWidth / count;
            var remainder = totalWidth - width * count;
            var x = rect.x + width * index;

            if (index < remainder)
            {
                x += index;
                width += 1;
            }
            else
            {
                x += remainder;
            }

            rect.x = x;
            rect.width = width;
            return rect;
        }

        public static Rect TakeFromLeft(this ref Rect rect, float width)
        {
            var toTake = Math.Min(rect.width, width);
            var result = rect;
            result.width = toTake;
            rect.x += toTake;
            rect.width -= toTake;
            return result;
        }

        public static int ToInt(this bool b) => b ? 1 : 0;
        public static bool ToBool(this int b) => b != 0;
        public static float AspectRatio(this Texture texture) => (float)texture.width / texture.height;
    }
}
