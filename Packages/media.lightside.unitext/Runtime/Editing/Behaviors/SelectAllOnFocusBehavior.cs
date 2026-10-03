using System;

namespace LightSide
{
    /// <summary>Selects all text when the editor gains focus and preserves it through the focusing pointer gesture.</summary>
    [Serializable]
    [TypeDescription("Select all text when the field gains focus")]
    [TypeGroup("Field", 4)]
    public sealed class SelectAllOnFocusBehavior : InputBehavior
    {
        [NonSerialized] private bool protectFocusSelection;

        protected override void OnEnable()
        {
            editable.Focused += OnFocused;
            editable.Selectable.SelectionChanging.Subscribe(OnSelectionChanging);
        }

        protected override void OnDisable()
        {
            editable.Focused -= OnFocused;
            editable.Selectable.SelectionChanging.Unsubscribe(OnSelectionChanging);
            editable.FrameTicked -= FinishFocus;
            protectFocusSelection = false;
        }

        private void OnFocused()
        {
            editable.SelectAll();
            if (editable.SelectionStart != 0 || editable.SelectionEnd != editable.CodepointCount) return;

            protectFocusSelection = true;
            editable.FrameTicked -= FinishFocus;
            editable.FrameTicked += FinishFocus;
        }

        private void OnSelectionChanging(SelectionChangingArgs args)
        {
            if (!protectFocusSelection) return;

            protectFocusSelection = false;
            editable.FrameTicked -= FinishFocus;

            if (IsPointerGesture(args.UserEvent))
                args.Cancel = true;
        }

        private void FinishFocus(float _)
        {
            editable.FrameTicked -= FinishFocus;
            protectFocusSelection = false;
        }

        private static bool IsPointerGesture(string reason)
            => reason == SelectionChangeReason.Pointer
               || reason == SelectionChangeReason.Extend
               || reason == SelectionChangeReason.Word
               || reason == SelectionChangeReason.Line
               || reason == SelectionChangeReason.Paragraph;
    }
}
