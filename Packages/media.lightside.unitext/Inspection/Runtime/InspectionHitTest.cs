using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>Cheap broad-phase: is a screen point inside a text's real content area? Uses <see cref="UniTextBase.ResultSize"/> (content can overflow the RectTransform) plus a margin, and a log-free screen→local projection.</summary>
    public static class InspectionHitTest
    {
        public static bool ScreenInsideContent(UniTextBase text, Vector2 screen, Camera camera)
        {
            if (text == null) return false;
            var rt = text.rectTransform;
            if (rt == null) return false;

            if (!TryScreenToLocal(rt, screen, camera, out var local))
                return false;

            var rect = text.GetPaddedRect();
            var size = text.ResultSize;
            var overflowX = Mathf.Max(0f, size.x - rect.width);
            var overflowY = Mathf.Max(0f, size.y - rect.height);
            var pad = Mathf.Max(rect.width, rect.height) * 0.1f;

            return local.x >= rect.xMin - overflowX - pad && local.x <= rect.xMax + overflowX + pad &&
                   local.y >= rect.yMin - overflowY - pad && local.y <= rect.yMax + overflowY + pad;
        }

        private static bool TryScreenToLocal(RectTransform rt, Vector2 screen, Camera camera, out Vector2 local)
        {
            if (camera == null)
            {
                var p = rt.InverseTransformPoint(new Vector3(screen.x, screen.y, 0f));
                local = new Vector2(p.x, p.y);
                return true;
            }

            var ray = camera.ScreenPointToRay(screen);
            var plane = new Plane(rt.rotation * Vector3.forward, rt.position);
            if (!plane.Raycast(ray, out var enter))
            {
                local = default;
                return false;
            }

            var world = ray.GetPoint(enter);
            var lp = rt.InverseTransformPoint(world);
            local = new Vector2(lp.x, lp.y);
            return true;
        }
    }
}
