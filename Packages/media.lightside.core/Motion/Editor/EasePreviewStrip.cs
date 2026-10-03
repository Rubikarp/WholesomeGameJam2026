using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// A live motion preview of one <see cref="Ease"/>: a dot travelling on the curve above a linear ghost,
    /// replaying whenever the curve changes and on click.
    /// </summary>
    public sealed class EasePreviewStrip : VisualElement
    {
        private const float TravelSeconds = 1.0f;
        private const float HoldSeconds = 0.45f;
        private const float Inset = 12f;

        private Ease ease = Ease.Linear;
        private double startTime;

        public EasePreviewStrip()
        {
            AddToClassList("lightside-ease-preview");
            pickingMode = PickingMode.Position;
            generateVisualContent += Paint;
            RegisterCallback<PointerDownEvent>(_ => Replay());
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Replay();
                schedule.Execute(MarkDirtyRepaint).Every(16);
            });
            tooltip = "Click replays the motion";
        }

        /// <summary>Shows a curve and restarts the run.</summary>
        public void SetEase(in Ease value)
        {
            if (ease.Equals(value)) return;
            ease = value;
            Replay();
        }

        /// <summary>Restarts the run from the left edge.</summary>
        public void Replay()
        {
            startTime = EditorApplication.timeSinceStartup;
            MarkDirtyRepaint();
        }

        private void Paint(MeshGenerationContext context)
        {
            var rect = contentRect;
            if (rect.width <= Inset * 2f || rect.height <= 4f) return;

            var cycle = (float)(EditorApplication.timeSinceStartup - startTime)
                        % (TravelSeconds + HoldSeconds);
            var t = Mathf.Clamp01(cycle / TravelSeconds);

            var left = rect.x + Inset;
            var width = rect.width - Inset * 2f;
            var easedLane = rect.y + rect.height * 0.34f;
            var linearLane = rect.y + rect.height * 0.72f;

            var painter = context.painter2D;
            var accent = EditorResources.ToggleAccent;
            var muted = new Color(0.5f, 0.5f, 0.5f, 0.55f);

            painter.strokeColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);
            painter.lineWidth = 1f;
            Track(painter, left, width, easedLane);
            Track(painter, left, width, linearLane);

            painter.fillColor = muted;
            painter.BeginPath();
            painter.Arc(new Vector2(left + width * t, linearLane), 3f, 0f, 360f);
            painter.Fill();

            painter.fillColor = accent;
            painter.BeginPath();
            painter.Arc(new Vector2(left + width * Mathf.Clamp01(ease.Evaluate(t)), easedLane),
                4f, 0f, 360f);
            painter.Fill();
        }

        private static void Track(Painter2D painter, float left, float width, float y)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(left, y));
            painter.LineTo(new Vector2(left + width, y));
            painter.Stroke();
        }
    }
}
