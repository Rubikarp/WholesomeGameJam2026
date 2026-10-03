using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Borderless UI Toolkit popup for editing four-sided padding through numeric fields or
    /// draggable box edges. Typed values replace one axis across the selection; drag callbacks emit
    /// incremental deltas so the caller can preserve distinct multi-selection values.
    /// </summary>
    public sealed class PaddingBoxPopupWindow : InspectorPopupWindow
    {
        private const float BaseWindowWidth = 210f;
        private const float WindowHeight = 146f;
        private const float EdgeGrabThickness = 8f;
        private const float EdgeThicknessIdle = 2f;
        private const float EdgeThicknessHover = 3f;
        private const float EdgeThicknessDrag = 4f;

        private static readonly Color edgeColor = new(0.92f, 0.27f, 0.21f, 1f);
        private static readonly Color boxFillColor = new(0.10f, 0.10f, 0.10f, 0.5f);
        private static readonly Color boxFillHoverColor = new(0.92f, 0.27f, 0.21f, 0.12f);

        private Func<Vector4> getValue;
        private Func<int, bool> isMixedAxis;
        private Action<int, float> onTypedCommit;
        private Action onDragBegin;
        private Action<Vector4> onDragDelta;
        private Action onDragEnd;

        private readonly FloatField[] fields = new FloatField[4];
        private readonly VisualElement[] edgeHandles = new VisualElement[4];
        private readonly VisualElement[] edgeLines = new VisualElement[4];
        private VisualElement box;
        private int activeAxis = -1;
        private int hoverAxis = -1;
        private int activePointer = -1;
        private Vector2 dragStart;
        private float previousTotalDelta;

        /// <summary>
        /// Opens the popup below the supplied inspector-space anchor and connects it to the owning
        /// padding value. The owner controls undo grouping through the drag begin/end callbacks.
        /// </summary>
        public static void Show(Rect anchorRect,
            Func<Vector4> getValue,
            Func<int, bool> isMixedAxis,
            Action<Vector4> onTypedCommit,
            Action onDragBegin,
            Action<Vector4> onDragDelta,
            Action onDragEnd)
        {
            if (onTypedCommit == null)
                throw new ArgumentNullException(nameof(onTypedCommit));
            Show(anchorRect, getValue, isMixedAxis,
                (axis, value) =>
                {
                    var padding = getValue();
                    padding[axis] = value;
                    onTypedCommit(padding);
                }, onDragBegin, onDragDelta, onDragEnd);
        }

        internal static void Show(Rect anchorRect,
            Func<Vector4> getValue,
            Func<int, bool> isMixedAxis,
            Action<int, float> onTypedCommit,
            Action onDragBegin,
            Action<Vector4> onDragDelta,
            Action onDragEnd)
        {
            var window = CreateInstance<PaddingBoxPopupWindow>();
            window.titleContent = GUIContent.none;
            window.getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
            window.isMixedAxis = isMixedAxis ??
                                 throw new ArgumentNullException(nameof(isMixedAxis));
            window.onTypedCommit = onTypedCommit ??
                                   throw new ArgumentNullException(nameof(onTypedCommit));
            window.onDragBegin = onDragBegin ??
                                 throw new ArgumentNullException(nameof(onDragBegin));
            window.onDragDelta = onDragDelta ??
                                 throw new ArgumentNullException(nameof(onDragDelta));
            window.onDragEnd = onDragEnd ??
                               throw new ArgumentNullException(nameof(onDragEnd));
            var screenRect = InspectorVisuals.CaptureScreenRect(anchorRect);
            window.ShowAtScreenRect(screenRect, BaseWindowWidth, WindowHeight);
        }

        /// <summary>Builds the retained-mode padding editor.</summary>
        public void CreateGUI()
        {
            var root = PreparePopup("lightside-padding-popup");

            var layout = new VisualElement();
            layout.AddToClassList("lightside-padding-popup__layout");
            root.Add(layout);

            layout.Add(CreateField(3));
            var middle = new VisualElement();
            middle.AddToClassList("lightside-padding-popup__middle");
            middle.Add(CreateField(0));

            var boxContainer = new VisualElement();
            boxContainer.AddToClassList("lightside-padding-popup__box-container");
            middle.Add(boxContainer);

            box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("lightside-padding-popup__box");
            box.style.position = Position.Absolute;
            box.style.backgroundColor = boxFillColor;
            boxContainer.Add(box);
            for (var axis = 0; axis < 4; axis++)
            {
                var capturedAxis = axis;
                var handle = new VisualElement();
                handle.AddToClassList(axis is 0 or 2
                    ? "lightside-padding-popup__edge--horizontal"
                    : "lightside-padding-popup__edge--vertical");
                handle.style.position = Position.Absolute;
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.style.position = Position.Absolute;
                line.style.backgroundColor = edgeColor;
                handle.Add(line);
                handle.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    hoverAxis = capturedAxis;
                    RefreshEdges();
                });
                handle.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (hoverAxis == capturedAxis) hoverAxis = -1;
                    RefreshEdges();
                });
                handle.RegisterCallback<PointerDownEvent>(evt =>
                    BeginDrag(capturedAxis, handle, evt));
                handle.RegisterCallback<PointerMoveEvent>(evt =>
                    ContinueDrag(capturedAxis, handle, evt));
                handle.RegisterCallback<PointerUpEvent>(evt =>
                    EndDrag(capturedAxis, handle, evt));
                handle.RegisterCallback<PointerCaptureOutEvent>(_ => FinishDrag());
                edgeHandles[axis] = handle;
                edgeLines[axis] = line;
                boxContainer.Add(handle);
            }

            PositionHandles();
            middle.Add(CreateField(2));
            layout.Add(middle);
            layout.Add(CreateField(1));

            root.schedule.Execute(RefreshFields).Every(100);
            RefreshFields();
            RefreshEdges();
        }

        private FloatField CreateField(int axis)
        {
            var field = new FloatField();
            field.AddToClassList("lightside-padding-popup__field");
            field.RegisterValueChangedCallback(evt =>
            {
                onTypedCommit(axis, evt.newValue);
                RefreshFields();
            });
            fields[axis] = field;
            return field;
        }

        private void PositionHandles()
        {
            var outside = new StyleLength(-EdgeGrabThickness * 0.5f);
            var auto = new StyleLength(StyleKeyword.Auto);
            PlaceVerticalHandle(edgeHandles[0], outside, auto);
            PlaceHorizontalHandle(edgeHandles[1], auto, outside);
            PlaceVerticalHandle(edgeHandles[2], auto, outside);
            PlaceHorizontalHandle(edgeHandles[3], outside, auto);
        }

        private static void PlaceVerticalHandle(VisualElement handle,
            StyleLength left, StyleLength right)
        {
            handle.style.left = left;
            handle.style.right = right;
            handle.style.top = 0f;
            handle.style.bottom = 0f;
            handle.style.width = EdgeGrabThickness;
        }

        private static void PlaceHorizontalHandle(VisualElement handle,
            StyleLength top, StyleLength bottom)
        {
            handle.style.left = 0f;
            handle.style.right = 0f;
            handle.style.top = top;
            handle.style.bottom = bottom;
            handle.style.height = EdgeGrabThickness;
        }

        private void RefreshFields()
        {
            var value = getValue();
            for (var axis = 0; axis < fields.Length; axis++)
            {
                fields[axis].showMixedValue = isMixedAxis(axis);
                fields[axis].SetValueWithoutNotify(value[axis]);
            }
        }

        private void RefreshEdges()
        {
            if (box == null) return;
            box.style.backgroundColor = activeAxis >= 0 || hoverAxis >= 0
                ? boxFillHoverColor
                : boxFillColor;
            for (var axis = 0; axis < edgeLines.Length; axis++)
            {
                var thickness = axis == activeAxis
                    ? EdgeThicknessDrag
                    : axis == hoverAxis ? EdgeThicknessHover : EdgeThicknessIdle;
                var vertical = axis is 0 or 2;
                var line = edgeLines[axis];
                if (vertical)
                {
                    line.style.left = (EdgeGrabThickness - thickness) * 0.5f;
                    line.style.top = 0f;
                    line.style.width = thickness;
                    line.style.height = Length.Percent(100f);
                }
                else
                {
                    line.style.left = 0f;
                    line.style.top = (EdgeGrabThickness - thickness) * 0.5f;
                    line.style.width = Length.Percent(100f);
                    line.style.height = thickness;
                }
            }
        }

        private void BeginDrag(int axis, VisualElement handle, PointerDownEvent evt)
        {
            if (evt.button != 0 || activePointer >= 0) return;
            activeAxis = axis;
            activePointer = evt.pointerId;
            dragStart = evt.position;
            previousTotalDelta = 0f;
            handle.CapturePointer(evt.pointerId);
            onDragBegin();
            RefreshEdges();
            evt.StopPropagation();
        }

        private void ContinueDrag(int axis, VisualElement handle, PointerMoveEvent evt)
        {
            if (activeAxis != axis || activePointer != evt.pointerId ||
                !handle.HasPointerCapture(evt.pointerId)) return;
            var horizontal = axis is 0 or 2;
            var sign = axis is 0 or 3 ? 1f : -1f;
            var distance = horizontal
                ? evt.position.x - dragStart.x
                : evt.position.y - dragStart.y;
            var total = distance * sign * (evt.shiftKey ? 10f : 1f);
            var delta = total - previousTotalDelta;
            if (!Mathf.Approximately(delta, 0f))
            {
                var value = Vector4.zero;
                value[axis] = delta;
                onDragDelta(value);
                previousTotalDelta = total;
                RefreshFields();
            }
            evt.StopPropagation();
        }

        private void EndDrag(int axis, VisualElement handle, PointerUpEvent evt)
        {
            if (activeAxis != axis || activePointer != evt.pointerId ||
                !handle.HasPointerCapture(evt.pointerId)) return;
            handle.ReleasePointer(evt.pointerId);
            FinishDrag();
            evt.StopPropagation();
        }

        private void FinishDrag()
        {
            if (activePointer < 0) return;
            activePointer = -1;
            activeAxis = -1;
            onDragEnd();
            RefreshEdges();
        }

        protected override void OnDisable()
        {
            FinishDrag();
            base.OnDisable();
        }
    }
}
