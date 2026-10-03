using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// UI Toolkit Vector2 field whose label scrubs both axes at once, each axis moving exactly as it would when
    /// dragged on its own box — same hidden anchored pointer, same magnitude-scaled sensitivity, same Shift and Alt
    /// speeds. A double-click on the label resets the value to zero, and each drag collapses into one undo step.
    /// </summary>
    public sealed class EditorVector2DragField : BaseField<Vector2>, IValueField<Vector2>
    {
        /// <summary>Root USS class.</summary>
        public const string UssClassName = "lightside-vector2-drag-field";
        /// <summary>Compatibility USS class applied to the complete field.</summary>
        public const string ContentUssClassName = UssClassName + "__content";
        /// <summary>Label-column USS class.</summary>
        public const string LabelColumnUssClassName = UssClassName + "__label-column";
        /// <summary>Draggable-label USS class.</summary>
        public const string HandleUssClassName = UssClassName + "__handle";
        /// <summary>Vector input USS class.</summary>
        public const string InputUssClassName = UssClassName + "__input";

        private readonly Label handle;
        private readonly Vector2Field input;
        private readonly IValueField<float> x;
        private readonly IValueField<float> y;

        /// <summary>Creates a natively aligned Vector2 field with a draggable label.</summary>
        public EditorVector2DragField(string label)
            : this(new Content(label)) { }

        private EditorVector2DragField(Content content)
            : base(content.Label, content.Input)
        {
            InspectorVisuals.Attach(this);
            handle = new Label(content.Label);
            input = content.Input;
            var axes = input.Query<FloatField>().ToList();
            x = axes[0];
            y = axes[1];

            AddToClassList(UssClassName);
            AddToClassList(ContentUssClassName);
            InspectorVisuals.MarkFieldAxis(this);
            labelElement.text = string.Empty;
            labelElement.AddToClassList(LabelColumnUssClassName);
            handle.AddToClassList(HandleUssClassName);
            labelElement.Add(handle);
            input.RegisterValueChangedCallback(evt =>
            {
                value = evt.newValue;
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerDownEvent>(OnHandlePointerDown, TrickleDown.TrickleDown);
            new ValueScrub<Vector2>(handle, this, () => enabledInHierarchy, horizontal: false);
        }

        /// <summary>Updates the value and both axis controls without sending a change event.</summary>
        public override void SetValueWithoutNotify(Vector2 newValue)
        {
            base.SetValueWithoutNotify(newValue);
            input.SetValueWithoutNotify(newValue);
        }

        /// <inheritdoc/>
        public void StartDragging()
        {
            x.StartDragging();
            y.StartDragging();
        }

        /// <inheritdoc/>
        public void StopDragging()
        {
            x.StopDragging();
            y.StopDragging();
        }

        /// <summary>
        /// Applies pointer travel to both axes, handing each its own component along the axis a single field
        /// scrubs on, so each moves at the sensitivity its own value earns and by the same step a drag on its own
        /// box would give. Travel down the screen reads as a decrease.
        /// </summary>
        public void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, Vector2 startValue)
        {
            x.ApplyInputDeviceDelta(new Vector3(delta.x, 0f, 0f), speed, startValue.x);
            y.ApplyInputDeviceDelta(new Vector3(-delta.y, 0f, 0f), speed, startValue.y);
        }

        protected override void UpdateMixedValueContent()
        {
            if (input != null) input.showMixedValue = showMixedValue;
        }

        private void OnHandlePointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 2) return;
            value = Vector2.zero;
            evt.StopImmediatePropagation();
        }

        private sealed class Content
        {
            public Content(string label)
            {
                Label = label;
                Input = new Vector2Field();
                Input.AddToClassList(InputUssClassName);
            }

            public string Label { get; }
            public Vector2Field Input { get; }
        }
    }
}
