using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class GradientPopupWindow : InspectorPopupWindow
    {
        private const float WindowWidth = 340f;
        private const float WindowHeight = 260f;
        private const float TrackTop = 12f;
        private const float TrackHeight = 26f;
        private const float StopWidth = 14f;
        private const float StopExtent = StopWidth * 0.5f;

        [NonSerialized] private UnityEngine.Object[] targets;
        [NonSerialized] private string propertyPath;
        [NonSerialized] private SerializedObject serializedObject;
        [NonSerialized] private SerializedPropertyBinding binding;

        private VisualElement bar;
        private VisualElement handles;
        private Image ramp;
        private int selected;
        private bool dragging;
        private int dragUndoGroup = -1;

        /// <summary>Opens an editor bound to the supplied serialized gradient on every selected target.</summary>
        public static void Show(Rect anchor, SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var window = CreateInstance<GradientPopupWindow>();
            window.targets = property.serializedObject.targetObjects;
            window.propertyPath = property.propertyPath;
            window.serializedObject = new SerializedObject(window.targets);
            window.binding = new SerializedPropertyBinding(
                InspectorHelpers.RequireProperty(window.serializedObject, window.propertyPath));
            var screenRect = InspectorVisuals.CaptureScreenRect(anchor);
            window.ShowAtScreenRect(screenRect, WindowWidth, WindowHeight);
        }

        /// <summary>Builds the retained-mode gradient editor.</summary>
        public void CreateGUI()
        {
            if (!TryResolve(out var property))
            {
                Close();
                return;
            }

            var root = CreatePopupRoot("lightside-gradient-popup");
            root.Add(SerializedPropertyField.CreateRelative(property, "interpolation", "Interpolation"));

            bar = new VisualElement
            {
                focusable = true,
                tabIndex = 0,
            };
            bar.style.height = TrackTop + TrackHeight + 6f;
            bar.style.position = Position.Relative;

            var checker = new Image
            {
                image = GradientDrawer.CheckerTexture(),
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.StretchToFill,
            };
            FillTrack(checker);
            checker.RegisterCallback<GeometryChangedEvent>(evt =>
                checker.uv = new Rect(0f, 0f,
                    evt.newRect.width / 12f, evt.newRect.height / 12f));
            ramp = new Image
            {
                pickingMode = PickingMode.Ignore,
                scaleMode = ScaleMode.StretchToFill,
            };
            FillTrack(ramp);
            handles = new VisualElement { pickingMode = PickingMode.Ignore };
            handles.style.position = Position.Absolute;
            handles.style.left = 0f;
            handles.style.right = 0f;
            handles.style.top = 0f;
            handles.style.bottom = 0f;
            bar.Add(checker);
            bar.Add(ramp);
            bar.Add(handles);
            bar.RegisterCallback<PointerDownEvent>(OnPointerDown);
            bar.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            bar.RegisterCallback<PointerUpEvent>(OnPointerUp);
            bar.RegisterCallback<PointerCaptureOutEvent>(_ => FinishDrag());
            bar.RegisterCallback<KeyDownEvent>(OnKeyDown);
            bar.RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            root.Add(bar);

            var scroll = InspectorVisuals.CreateScrollStack();
            scroll.style.flexGrow = 1f;
            scroll.Add(SerializedPropertyField.CreateCollection(
                InspectorHelpers.RequireRelative(property, "stops"), "Stops",
                (_, insert) => insert(new GradientStop(1f, Color.white))));
            root.Add(scroll);
            root.TrackPropertyValue(property, _ => Refresh());
            Refresh();
        }

        private static void FillTrack(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = StopExtent;
            element.style.right = StopExtent;
            element.style.top = TrackTop;
            element.style.height = TrackHeight;
        }

        private bool TryResolve(out SerializedProperty property)
        {
            property = null;
            if (targets == null || targets.Length == 0 ||
                string.IsNullOrEmpty(propertyPath)) return false;
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] == null) return false;
            serializedObject ??= new SerializedObject(targets);
            serializedObject.UpdateIfRequiredOrScript();
            property = serializedObject.FindProperty(propertyPath);
            return property != null;
        }

        private void Refresh()
        {
            if (!TryResolve(out var property))
            {
                Close();
                return;
            }

            EnsureSorted(property);
            ClampSelection(InspectorHelpers.RequireRelative(property, "stops").arraySize);
            ramp.image = GradientDrawer.GetPreviewTexture(Snapshot(property));
            RebuildHandles(property);
        }

        private void RebuildHandles(SerializedProperty property)
        {
            handles.Clear();
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            var width = bar.resolvedStyle.width;
            if (float.IsNaN(width) || width <= StopWidth) return;
            var trackWidth = width - StopWidth;
            for (var i = 0; i < stops.arraySize; i++)
            {
                var stop = stops.GetArrayElementAtIndex(i);
                var time = InspectorHelpers.RequireRelative(stop, "time").floatValue;
                var color = InspectorHelpers.RequireRelative(stop, "color").colorValue;
                var border = i == selected ? EditorResources.ToggleAccent : Color.white;
                var handle = new GradientStopHandle(color, border)
                    { tooltip = $"{Mathf.RoundToInt(time * 100f)}%" };
                handle.style.position = Position.Absolute;
                handle.style.left = Mathf.Clamp01(time) * trackWidth;
                handle.style.top = 0f;
                handle.style.width = StopWidth;
                handle.style.height = TrackTop + 3f;
                handles.Add(handle);
            }
        }

        private sealed class GradientStopHandle : VisualElement
        {
            private readonly Color color;
            private readonly Color border;

            public GradientStopHandle(Color color, Color border)
            {
                this.color = color;
                this.border = border;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                var width = contentRect.width;
                var height = contentRect.height;
                if (width <= 0f || height <= 0f) return;
                DrawMarker(context.painter2D, width, height, border, 0f);
                DrawMarker(context.painter2D, width, height, color, 2f);
            }

            private static void DrawMarker(Painter2D painter, float width, float height,
                Color fill, float inset)
            {
                var point = height - inset;
                var baseY = height - 5f - inset;
                painter.BeginPath();
                painter.MoveTo(new Vector2(inset, inset));
                painter.LineTo(new Vector2(width - inset, inset));
                painter.LineTo(new Vector2(width - inset, baseY));
                painter.LineTo(new Vector2(width * 0.5f, point));
                painter.LineTo(new Vector2(inset, baseY));
                painter.ClosePath();
                painter.fillColor = fill;
                painter.Fill();
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!TryResolve(out var property)) return;
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            var hit = HitTest(stops, evt.localPosition.x);
            if (evt.button == 1)
            {
                RemoveAt(property, hit);
                evt.StopPropagation();
                Refresh();
                return;
            }
            if (evt.button != 0) return;

            if (hit >= 0)
            {
                selected = hit;
                if (evt.clickCount == 2)
                {
                    OpenColorPicker(hit);
                    evt.StopPropagation();
                    return;
                }
            }
            else if (evt.localPosition.y < TrackTop ||
                     evt.localPosition.y > TrackTop + TrackHeight)
            {
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Change Gradient Stop");
            dragUndoGroup = Undo.GetCurrentGroup();
            if (hit < 0) AddStopAt(property, TimeAt(evt.localPosition.x));
            dragging = true;
            bar.CapturePointer(evt.pointerId);
            bar.Focus();
            evt.StopPropagation();
            Refresh();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!dragging || !bar.HasPointerCapture(evt.pointerId) ||
                !TryResolve(out var property)) return;
            SetTime(property, selected, TimeAt(evt.localPosition.x));
            evt.StopPropagation();
            Refresh();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!dragging || !bar.HasPointerCapture(evt.pointerId)) return;
            bar.ReleasePointer(evt.pointerId);
            FinishDrag();
            evt.StopPropagation();
        }

        private void FinishDrag()
        {
            if (!dragging) return;
            dragging = false;
            if (dragUndoGroup >= 0)
                Undo.CollapseUndoOperations(dragUndoGroup);
            dragUndoGroup = -1;
        }

        protected override void OnDisable()
        {
            FinishDrag();
            base.OnDisable();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode is not (KeyCode.Delete or KeyCode.Backspace) ||
                !TryResolve(out var property)) return;
            RemoveAt(property, selected);
            evt.StopPropagation();
            Refresh();
        }

        private int HitTest(SerializedProperty stops, float x)
        {
            var best = -1;
            var bestDistance = StopWidth * 0.75f;
            var width = Mathf.Max(1f, bar.resolvedStyle.width - StopWidth);
            for (var i = 0; i < stops.arraySize; i++)
            {
                var time = InspectorHelpers.RequireRelative(
                    stops.GetArrayElementAtIndex(i), "time").floatValue;
                var distance = Mathf.Abs(x -
                    (StopExtent + Mathf.Clamp01(time) * width));
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = i;
            }
            return best;
        }

        private float TimeAt(float x)
            => Mathf.Clamp01((x - StopExtent) /
                             Mathf.Max(1f, bar.resolvedStyle.width - StopWidth));

        private void SetTime(SerializedProperty property, int index, float time)
        {
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            if ((uint)index >= (uint)stops.arraySize) return;
            var color = ReadStop(stops.GetArrayElementAtIndex(index)).color;
            binding.TransformValue(value =>
            {
                var gradient = (Gradient)value;
                if ((uint)index >= (uint)gradient.Stops.Count) return gradient;
                var stop = gradient.Stops[index];
                stop.time = time;
                return gradient.WithStop(index, stop);
            }, "Move Gradient Stop");
            SelectStop(time, color);
        }

        private void AddStopAt(SerializedProperty property, float time)
        {
            var color = Snapshot(property).Evaluate(time);
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            selected = stops.arraySize;
            binding.TransformValue(value =>
            {
                var gradient = (Gradient)value;
                return gradient.WithAddedStop(new GradientStop(time, gradient.Evaluate(time)));
            }, "Add Gradient Stop");
            SelectStop(time, color);
        }

        private void RemoveAt(SerializedProperty property, int index)
        {
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            if (stops.arraySize <= 1 || (uint)index >= (uint)stops.arraySize) return;
            binding.TransformValue(value =>
            {
                var gradient = (Gradient)value;
                return gradient.Stops.Count <= 1 || (uint)index >= (uint)gradient.Stops.Count
                    ? gradient
                    : gradient.WithoutStopAt(index);
            }, "Remove Gradient Stop");
            if (TryResolve(out var current))
                ClampSelection(InspectorHelpers.RequireRelative(current, "stops").arraySize);
        }

        private void OpenColorPicker(int index)
        {
            if (!TryResolve(out var property)) return;
            var path = InspectorHelpers.RequireRelative(property, "stops").propertyPath;
            var currentTargets = targets;
            var color = InspectorHelpers.RequireRelative(
                InspectorHelpers.RequireRelative(property, "stops").GetArrayElementAtIndex(index),
                "color").colorValue;
            EditorColorPicker.Show(next =>
            {
                if (currentTargets == null || currentTargets.Length == 0) return;
                for (var i = 0; i < currentTargets.Length; i++)
                    if (currentTargets[i] == null) return;
                var state = new SerializedObject(currentTargets);
                var gradientPath = path.Substring(0, path.Length - ".stops".Length);
                var gradientProperty = InspectorHelpers.RequireProperty(state, gradientPath);
                var currentBinding = new SerializedPropertyBinding(gradientProperty);
                currentBinding.TransformValue(value =>
                {
                    var gradient = (Gradient)value;
                    if ((uint)index >= (uint)gradient.Stops.Count) return gradient;
                    var stop = gradient.Stops[index];
                    stop.color = next;
                    return gradient.WithStop(index, stop);
                }, "Change Gradient Stop Color");
            }, color);
        }

        private void ClampSelection(int count)
            => selected = Mathf.Clamp(selected, 0, Mathf.Max(0, count - 1));

        private void EnsureSorted(SerializedProperty property)
        {
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            var count = stops.arraySize;
            if (count < 2) return;
            var ordered = true;
            for (var i = 1; i < count && ordered; i++)
                ordered = InspectorHelpers.RequireRelative(
                              stops.GetArrayElementAtIndex(i), "time").floatValue >=
                          InspectorHelpers.RequireRelative(
                              stops.GetArrayElementAtIndex(i - 1), "time").floatValue;
            if (ordered) return;

            var selectedValue = selected >= 0 && selected < count
                ? ReadStop(stops.GetArrayElementAtIndex(selected))
                : default;
            var hasSelection = selected >= 0 && selected < count;
            binding.TransformValue(value => value, "Sort Gradient Stops");

            if (!hasSelection) return;
            SelectStop(selectedValue.time, selectedValue.color);
        }

        private void SelectStop(float time, Color color)
        {
            if (!TryResolve(out var property)) return;
            var stops = InspectorHelpers.RequireRelative(property, "stops");
            for (var i = 0; i < stops.arraySize; i++)
            {
                var value = ReadStop(stops.GetArrayElementAtIndex(i));
                if (value.time != time || value.color != color) continue;
                selected = i;
                return;
            }
        }

        private static GradientStop ReadStop(SerializedProperty stop)
            => new(InspectorHelpers.RequireRelative(stop, "time").floatValue,
                InspectorHelpers.RequireRelative(stop, "color").colorValue);

        private static Gradient Snapshot(SerializedProperty property) =>
            (Gradient)SerializedPropertyBinding.Read(property, typeof(Gradient));

    }
}
