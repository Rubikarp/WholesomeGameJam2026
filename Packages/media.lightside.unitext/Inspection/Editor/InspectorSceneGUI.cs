using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide.Inspection
{
    [InitializeOnLoad]
    internal static class InspectorSceneGUI
    {
        private const string MenuPath = "Tools/UniText/Inspection Mode %#i";

        private static readonly List<ModifierInspection> modifiers = new(8);
        private static readonly HandlesDebugDraw draw = new();
        private static readonly InspectionDrawList drawList = new();
        private static readonly StringBuilder cardBuilder = new(256);
        private static readonly StringBuilder statsBuilder = new(512);
        private static readonly Dictionary<int, SceneCards> cardsByView = new();

        private static bool pinned;
        private static TextInspectionSnapshot frozen;
        private static UniTextBase frozenTarget;
        private static Vector2 frozenCardPosition;
        private static string statsCard = string.Empty;
        private static int statsSignature = -1;

        static InspectorSceneGUI()
        {
            SceneView.duringSceneGui += OnScene;
        }

        [MenuItem(MenuPath)]
        private static void ToggleMenu() => UniTextInspector.Toggle();

        [MenuItem(MenuPath, true)]
        private static bool ValidateMenu()
        {
            Menu.SetChecked(MenuPath, UniTextInspector.Enabled);
            return true;
        }

        private static void OnScene(SceneView view)
        {
            var cards = GetCards(view);
            if (!UniTextInspector.Enabled)
            {
                cards.Hide();
                return;
            }

            var input = Event.current;
            if (input.type == EventType.KeyDown && input.keyCode == UniTextInspector.PinKey)
            {
                pinned = !pinned;
                input.Use();
            }
            if (!pinned && (input.type == EventType.MouseMove || input.type == EventType.MouseDrag))
                Probe(view, input.mousePosition);

            var sweepTarget = frozenTarget != null
                ? frozenTarget
                : UniTextInspector.Target != null
                    ? UniTextInspector.Target
                    : ResolveSelected();
            if (sweepTarget != null)
            {
                var signature = InspectionOverlayCore.ContentSignature(sweepTarget, frozen);
                if (InspectionOverlayCore.ShouldDraw(frozen))
                {
                    InspectionOverlayCore.BuildDrawList(
                        drawList, sweepTarget, frozenTarget, frozen, modifiers, signature);
                    drawList.Render(draw);
                }
                if (UniTextInspector.ShowStats)
                {
                    InspectionOverlayCore.RefreshStats(sweepTarget, signature,
                        ref statsSignature, ref statsCard, statsBuilder);
                    cards.SetStats(statsCard);
                }
                else
                {
                    cards.SetStats(null);
                }
            }
            else
            {
                cards.SetStats(null);
            }

            if (frozenTarget != null && frozen.hit)
            {
                var text = (pinned ? "📌 PINNED (P)\n\n" : string.Empty) +
                           InspectionCardFormatter.Format(frozen, modifiers, cardBuilder);
                cards.SetHover(text, frozenCardPosition);
            }
            else
            {
                cards.SetHover(null, default);
            }
            view.Repaint();
        }

        private static void Probe(SceneView view, Vector2 mousePosition)
        {
            frozenCardPosition = mousePosition;
            var target = UniTextInspector.Target != null
                ? UniTextInspector.Target
                : ResolveSelected();
            if (target == null)
            {
                frozen = default;
                frozenTarget = null;
                return;
            }
            var screen = HandleUtility.GUIPointToScreenPixelCoordinate(mousePosition);
            if (InspectionHitTest.ScreenInsideContent(target, screen, view.camera))
            {
                var hit = target.HitTestRange(screen, view.camera, 0f);
                if (target != frozenTarget || hit.hit != frozen.hit ||
                    hit.hit && hit.glyphIndex != frozen.glyph.glyphIndex)
                    frozen = TextInspectionProbe.Probe(target, hit, modifiers);
            }
            else if (frozen.hit)
            {
                frozen = default;
            }
            frozenTarget = target;
        }

        private static SceneCards GetCards(SceneView view)
        {
            var id = ObjectUtils.GetInstanceIdCompat(view);
            if (cardsByView.TryGetValue(id, out var cards)) return cards;
            cards = new SceneCards(view.rootVisualElement);
            cardsByView.Add(id, cards);
            view.rootVisualElement.RegisterCallback<DetachFromPanelEvent>(_ =>
                cardsByView.Remove(id));
            return cards;
        }

        private static UniTextBase ResolveSelected()
        {
            var selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponent<UniTextBase>() : null;
        }

        private sealed class SceneCards
        {
            private readonly VisualElement host;
            private readonly Label stats;
            private readonly Label hover;
            private Vector2 hoverAnchor;

            public SceneCards(VisualElement parent)
            {
                host = new VisualElement { pickingMode = PickingMode.Ignore };
                host.style.position = Position.Absolute;
                host.style.left = 0f;
                host.style.right = 0f;
                host.style.top = 0f;
                host.style.bottom = 0f;
                stats = CreateCard();
                stats.style.left = 12f;
                stats.style.top = 28f;
                hover = CreateCard();
                hover.RegisterCallback<GeometryChangedEvent>(_ => PositionHover());
                host.Add(stats);
                host.Add(hover);
                parent.Add(host);
                Hide();
            }

            public void Hide()
            {
                stats.style.display = DisplayStyle.None;
                hover.style.display = DisplayStyle.None;
            }

            public void SetStats(string text)
            {
                var visible = !string.IsNullOrEmpty(text);
                stats.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (visible) stats.text = text;
            }

            public void SetHover(string text, Vector2 anchor)
            {
                var visible = !string.IsNullOrEmpty(text);
                hover.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) return;
                hover.text = text;
                hoverAnchor = anchor;
                PositionHover();
            }

            private void PositionHover()
            {
                if (hover.resolvedStyle.display == DisplayStyle.None) return;
                const float offset = 20f;
                var width = hover.resolvedStyle.width;
                var height = hover.resolvedStyle.height;
                var left = hoverAnchor.x + offset;
                var top = hoverAnchor.y + offset;
                var bounds = host.contentRect;
                if (left + width > bounds.width) left = hoverAnchor.x - width - offset;
                if (top + height > bounds.height) top = bounds.height - height - 8f;
                hover.style.left = Mathf.Max(0f, left);
                hover.style.top = Mathf.Max(0f, top);
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
        }
    }
}
