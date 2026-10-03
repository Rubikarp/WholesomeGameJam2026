using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>A disclosure header whose label toggles expansion while trailing controls remain independent actions.</summary>
    public class InspectorFoldoutHeader : VisualElement
    {
        /// <summary>Root USS class.</summary>
        public const string UssClassName = "lightside-foldout-header";
        /// <summary>Disclosure-toggle USS class.</summary>
        public const string ToggleUssClassName = UssClassName + "__toggle";
        /// <summary>Optional icon USS class.</summary>
        public const string IconUssClassName = UssClassName + "__icon";
        /// <summary>Header-label USS class.</summary>
        public const string LabelUssClassName = UssClassName + "__label";
        /// <summary>Trailing-actions USS class.</summary>
        public const string ActionsUssClassName = UssClassName + "__actions";
        /// <summary>Disclosure, icon, and label container USS class.</summary>
        public const string LeadingUssClassName = UssClassName + "__leading";

        private readonly Toggle toggle;
        private readonly Image icon;
        private readonly Label label;
        private readonly VisualElement leading;
        private readonly InspectorRow actions;
        private bool expanded;
        private bool collapsible = true;

        /// <summary>Creates a header with an optional label.</summary>
        public InspectorFoldoutHeader(string text = null)
        {
            InspectorVisuals.Attach(this);
            focusable = true;
            AddToClassList(UssClassName);

            leading = new VisualElement { pickingMode = PickingMode.Ignore };
            leading.AddToClassList(LeadingUssClassName);
            toggle = CreateDisclosureToggle();
            toggle.AddToClassList(ToggleUssClassName);
            icon = EditorResources.CreateIcon((Texture)null);
            icon.AddToClassList(IconUssClassName);
            label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = true };
            label.AddToClassList(LabelUssClassName);
            actions = InspectorRow.CreateActions();
            actions.AddToClassList(ActionsUssClassName);

            leading.Add(toggle);
            leading.Add(icon);
            leading.Add(label);
            hierarchy.Add(leading);
            hierarchy.Add(actions);
            SetContent(text);

            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || IsAction(evt.target) || !collapsible) return;
                Focus();
                SetExpanded(!expanded);
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (IsAction(evt.target) || !collapsible ||
                    evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                SetExpanded(!expanded);
                evt.StopPropagation();
            });
        }

        /// <summary>Creates the shared passive disclosure control.</summary>
        public static Toggle CreateDisclosureToggle()
        {
            return new DisclosureToggle();
        }

        /// <summary>Raised when the user changes the expansion state.</summary>
        public event Action<bool> Changed;

        /// <summary>Container for controls that must not toggle expansion.</summary>
        public VisualElement Actions => actions;

        internal InspectorRow ActionRow => actions;

        /// <summary>Updates the label and optional leading icon.</summary>
        public void SetContent(string text, Texture image = null)
        {
            label.text = text;
            icon.image = image;
            icon.style.display = image != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Shows or hides the disclosure control and header toggle behavior.</summary>
        public void SetCollapsible(bool value)
        {
            collapsible = value;
            toggle.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Updates the disclosure state without raising <see cref="Changed"/>.</summary>
        public void SetExpandedWithoutNotify(bool value)
        {
            expanded = value;
            toggle.SetValueWithoutNotify(value);
        }

        private void SetExpanded(bool value)
        {
            if (expanded == value) return;
            var previous = expanded;
            SetExpandedWithoutNotify(value);
            Changed?.Invoke(value);
            using var evt = ChangeEvent<bool>.GetPooled(previous, value);
            evt.target = this;
            SendEvent(evt);
        }

        private bool IsAction(object target) =>
            target is VisualElement element && (element == actions || actions.Contains(element));

        private sealed class DisclosureToggle : Toggle
        {
            private readonly Label glyph;

            public DisclosureToggle()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList(BaseField<bool>.noLabelVariantUssClassName);
                var input = this.Q<VisualElement>(className: Toggle.inputUssClassName) ??
                            throw new InvalidOperationException("Toggle input is missing.");
                input.pickingMode = PickingMode.Ignore;
                var checkmark = this.Q<VisualElement>(className: Toggle.checkmarkUssClassName) ??
                                throw new InvalidOperationException("Toggle checkmark is missing.");
                checkmark.style.display = DisplayStyle.None;
                glyph = new Label { pickingMode = PickingMode.Ignore };
                glyph.AddToClassList("lightside-disclosure__glyph");
                input.Add(glyph);
                SetValueWithoutNotify(false);
            }

            public override void SetValueWithoutNotify(bool newValue)
            {
                base.SetValueWithoutNotify(newValue);
                if (glyph != null) glyph.text = newValue ? "▼" : "▶";
            }
        }
    }

    /// <summary>A disclosure header with a count badge and an independent add action.</summary>
    public sealed class InspectorListHeader : InspectorFoldoutHeader
    {
        private const string HeaderUssClassName = "lightside-list__header";
        private const string ExpandedUssClassName = HeaderUssClassName + "--expanded";
        private const string HasBodyUssClassName = HeaderUssClassName + "--has-body";
        private const string CountUssClassName = "lightside-list__count";
        private const string AddUssClassName = "lightside-list__add";

        private readonly Label count;
        private bool expanded;
        private bool hasBody = true;

        /// <summary>Creates a list header whose add action remains independent from disclosure.</summary>
        public InspectorListHeader(string title, string addTooltip) : base(title)
        {
            AddToClassList(HeaderUssClassName);
            AddToClassList(HasBodyUssClassName);
            count = InspectorVisuals.CreateCountChip();
            count.AddToClassList(CountUssClassName);
            ActionRow.Add(count);
            SetCount(0);
            AddButton = new Button { text = "+", tooltip = addTooltip };
            AddButton.AddToClassList(AddUssClassName);
            ActionRow.Add(AddButton);
            Changed += value =>
            {
                expanded = value;
                RefreshBodyState();
            };
        }

        /// <summary>The trailing add action owned by this header.</summary>
        public Button AddButton { get; }

        /// <summary>Updates the displayed item count.</summary>
        public void SetCount(int value) => SetCount(value.ToString(), value > 0);

        internal void SetCount(int minimum, int maximum) => SetCount(
            minimum == maximum ? minimum.ToString() : $"{minimum}\u2013{maximum}",
            maximum > 0);

        private void SetCount(string value, bool hasItems)
        {
            count.text = value;
            count.style.display = hasItems ? DisplayStyle.Flex : DisplayStyle.None;
            HasItems = hasItems;
        }

        internal bool HasItems { get; private set; }

        /// <summary>Updates disclosure without raising <see cref="InspectorFoldoutHeader.Changed"/>.</summary>
        public void SetExpandedState(bool value)
        {
            expanded = value;
            SetExpandedWithoutNotify(value);
            RefreshBodyState();
        }

        /// <summary>Hides disclosure when the list has no body rows.</summary>
        public void SetHasBody(bool value)
        {
            hasBody = value;
            EnableInClassList(HasBodyUssClassName, value);
            SetCollapsible(value);
            RefreshBodyState();
        }

        private void RefreshBodyState() =>
            EnableInClassList(ExpandedUssClassName, hasBody && expanded);
    }

    /// <summary>A native-aligned Inspector field whose label controls disclosure and whose input hosts actions.</summary>
    public sealed class InspectorFoldoutField : BaseField<bool>
    {
        private const string RootUssClassName = "lightside-foldout-field";
        private const string LeafUssClassName = RootUssClassName + "--leaf";
        private const string LabelUssClassName = RootUssClassName + "__label";
        /// <summary>Disclosure-toggle USS class.</summary>
        public const string ToggleUssClassName = RootUssClassName + "__toggle";
        private const string TextUssClassName = RootUssClassName + "__text";
        private const string ActionsUssClassName = RootUssClassName + "__actions";

        private readonly Toggle toggle;
        private readonly Image icon;
        private readonly Label text;
        private readonly InspectorRow actions;
        private bool collapsible = true;

        /// <summary>Creates an aligned disclosure field with an optional label.</summary>
        public InspectorFoldoutField(string label = null) : this(InspectorRow.CreateActions(), label) { }

        private InspectorFoldoutField(InspectorRow input, string label) : base(label, input)
        {
            InspectorVisuals.Attach(this);
            actions = input;
            focusable = true;
            AddToClassList(RootUssClassName);
            InspectorVisuals.MarkFieldAxis(this);
            labelElement.text = string.Empty;
            labelElement.AddToClassList(LabelUssClassName);
            actions.AddToClassList(ActionsUssClassName);

            toggle = InspectorFoldoutHeader.CreateDisclosureToggle();
            toggle.AddToClassList(ToggleUssClassName);
            icon = EditorResources.CreateIcon((Texture)null);
            icon.AddToClassList(RootUssClassName + "__icon");
            text = new Label(label)
            {
                pickingMode = PickingMode.Ignore,
                enableRichText = true,
            };
            text.AddToClassList(TextUssClassName);
            labelElement.Add(toggle);
            labelElement.Add(icon);
            labelElement.Add(text);
            SetContent(label);

            labelElement.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || !collapsible) return;
                Focus();
                SetExpanded(!value);
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.target != this || !collapsible ||
                    evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                SetExpanded(!value);
                evt.StopPropagation();
            });
        }

        /// <summary>Input column for controls that remain independent from disclosure.</summary>
        public VisualElement Actions => actions;

        internal InspectorRow ActionRow => actions;

        /// <summary>Updates the label and clears its leading icon.</summary>
        public void SetContent(string value) => SetContent(value, null);

        /// <summary>Updates the label and leading icon.</summary>
        public void SetContent(string value, Texture image)
        {
            text.text = value;
            icon.image = image;
            icon.style.display = image != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Shows or hides disclosure behavior.</summary>
        public void SetCollapsible(bool value)
        {
            collapsible = value;
            EnableInClassList(LeafUssClassName, !value);
            toggle.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Updates disclosure without raising a change event.</summary>
        public void SetExpandedWithoutNotify(bool value)
        {
            base.SetValueWithoutNotify(value);
            toggle.SetValueWithoutNotify(value);
        }

        private void SetExpanded(bool next)
        {
            if (value == next) return;
            var previous = value;
            SetExpandedWithoutNotify(next);
            using var evt = ChangeEvent<bool>.GetPooled(previous, next);
            evt.target = this;
            SendEvent(evt);
        }
    }
}
