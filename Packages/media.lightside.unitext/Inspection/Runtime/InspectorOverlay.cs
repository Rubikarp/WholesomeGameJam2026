using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.Inspection
{
    [AddComponentMenu("")]
    internal sealed class InspectorOverlay : MonoBehaviour
    {
        private readonly List<ModifierInspection> modifiers = new(8);
        private readonly UIToolkitDebugDraw draw = new();
        private readonly InspectionDrawList drawList = new();
        private readonly StringBuilder cardBuilder = new(256);
        private readonly StringBuilder statsBuilder = new(512);

        private TextInspectionSnapshot snapshot;
        private UniTextBase current;
        private string card = string.Empty;
        private string statsCard = string.Empty;
        private int statsSignature = -1;
        private UniTextBase[] cache;
        private int refreshCountdown;
        private bool pinned;
        private Vector2 lastCardPosition;
        private VisualElement geometry;
        private Label hoverCard;
        private Label stats;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>() ??
                           throw new InvalidOperationException(
                               "The UniText inspection overlay requires a UIDocument.");
            var root = document.rootVisualElement;
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0f;
            root.style.right = 0f;
            root.style.top = 0f;
            root.style.bottom = 0f;

            geometry = new VisualElement { pickingMode = PickingMode.Ignore };
            FillRoot(geometry);
            geometry.generateVisualContent += PaintGeometry;
            root.Add(geometry);
            stats = CreateCard();
            stats.style.left = 12f;
            stats.style.top = 12f;
            root.Add(stats);
            hoverCard = CreateCard();
            root.Add(hoverCard);
            hoverCard.RegisterCallback<GeometryChangedEvent>(_ => PositionHoverCard());
            RefreshVisuals();
            CoreLoop.Updating.Toggle(ref tickHandle, tickCallback ??= OnTick, Application.isPlaying);
        }

        private Action tickCallback;
        private TickHandle tickHandle;

        private void OnTick()
        {
            if (InputUtils.GetKeyDown(UniTextInspector.PinKey)) pinned = !pinned;
            if (!pinned) ProbeCurrentTarget();
            RefreshVisuals();
        }

        private void OnDisable()
        {
            CoreLoop.Updating.Toggle(ref tickHandle, tickCallback, false);
            ResetInspection();
        }

        private void ProbeCurrentTarget()
        {
            var screen = InputUtils.MousePosition;
            lastCardPosition = new Vector2(screen.x, Screen.height - screen.y);
            TextHitResult hit;
            UniTextBase target;
            if (UniTextInspector.Target != null)
            {
                target = UniTextInspector.Target;
                hit = target.HitTestRange(screen, ResolveCamera(target), 0f);
            }
            else
            {
                target = ResolveUnderCursor(screen, out hit);
            }
            if (target == null)
            {
                ResetInspection();
                return;
            }
            if (target == current && hit.hit == snapshot.hit &&
                (!hit.hit || hit.glyphIndex == snapshot.glyph.glyphIndex))
                return;
            current = target;
            snapshot = TextInspectionProbe.Probe(target, hit, modifiers);
            card = snapshot.hit
                ? InspectionCardFormatter.Format(snapshot, modifiers, cardBuilder)
                : string.Empty;
        }

        private void RefreshVisuals()
        {
            if (hoverCard == null) return;
            var enabled = UniTextInspector.Enabled;
            var target = current != null ? current : UniTextInspector.Target;
            var showHover = enabled && !string.IsNullOrEmpty(card);
            hoverCard.style.display = showHover ? DisplayStyle.Flex : DisplayStyle.None;
            if (showHover)
            {
                hoverCard.text = pinned
                    ? "📌 PINNED (press P to release)\n\n" + card
                    : card;
                PositionHoverCard();
            }
            var showStats = enabled && target != null && UniTextInspector.ShowStats;
            stats.style.display = showStats ? DisplayStyle.Flex : DisplayStyle.None;
            if (showStats)
            {
                var signature = InspectionOverlayCore.ContentSignature(target, snapshot);
                InspectionOverlayCore.RefreshStats(target, signature,
                    ref statsSignature, ref statsCard, statsBuilder);
                stats.text = statsCard;
            }
            geometry.style.display = enabled && target != null
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            geometry.MarkDirtyRepaint();
        }

        private void PaintGeometry(MeshGenerationContext context)
        {
            if (!UniTextInspector.Enabled) return;
            var target = current != null ? current : UniTextInspector.Target;
            if (target == null || !InspectionOverlayCore.ShouldDraw(snapshot)) return;
            var signature = InspectionOverlayCore.ContentSignature(target, snapshot);
            InspectionOverlayCore.BuildDrawList(
                drawList, target, current, snapshot, modifiers, signature);
            draw.Begin(context.painter2D, ResolveCamera(target), geometry.contentRect.height);
            drawList.Render(draw);
            draw.End();
        }

        private void PositionHoverCard()
        {
            if (hoverCard == null || hoverCard.resolvedStyle.display == DisplayStyle.None) return;
            const float offset = 18f;
            var width = hoverCard.resolvedStyle.width;
            var height = hoverCard.resolvedStyle.height;
            var left = lastCardPosition.x + offset;
            var top = lastCardPosition.y + offset;
            if (left + width > Screen.width) left = lastCardPosition.x - width - offset;
            if (top + height > Screen.height) top = Screen.height - height - 8f;
            hoverCard.style.left = Mathf.Max(0f, left);
            hoverCard.style.top = Mathf.Max(0f, top);
        }

        private void ResetInspection()
        {
            current = null;
            card = string.Empty;
            snapshot = default;
            pinned = false;
        }

        private static Label CreateCard()
        {
            var label = new Label
            {
                enableRichText = true,
                pickingMode = PickingMode.Ignore,
            };
            label.style.position = Position.Absolute;
            InspectionCardVisuals.Apply(label);
            return label;
        }

        private static void FillRoot(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }

        private static Camera ResolveCamera(UniTextBase text)
        {
            var canvas = text.canvas;
            if (canvas != null)
                return canvas.renderMode == RenderMode.ScreenSpaceOverlay
                    ? null
                    : canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
            return Camera.main;
        }

        private UniTextBase ResolveUnderCursor(Vector2 screen, out TextHitResult hit)
        {
            hit = TextHitResult.None;
            if (cache == null || refreshCountdown-- <= 0)
            {
                cache = ObjectUtils.FindAll<UniTextBase>();
                refreshCountdown = 30;
            }
            UniTextBase best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < cache.Length; i++)
            {
                var text = cache[i];
                if (text == null || !text.isActiveAndEnabled) continue;
                var camera = ResolveCamera(text);
                if (!InspectionHitTest.ScreenInsideContent(text, screen, camera)) continue;
                var candidate = text.HitTestRange(screen, camera, 0f);
                if (!candidate.hit || candidate.distance >= bestDistance) continue;
                bestDistance = candidate.distance;
                best = text;
                hit = candidate;
            }
            return best;
        }
    }
}
