using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Shared LightSide UI Toolkit primitives and theme installation for editor surfaces.</summary>
    public static class InspectorVisuals
    {
        /// <summary>Standard height for editable inspector controls.</summary>
        public const float ControlHeight = 24f;

        /// <summary>Standard minimum height for collection and compound-field rows.</summary>
        public const float CollectionRowHeight = 28f;

        /// <summary>Marks the boundary that owns a control's tooltip metadata.</summary>
        public const string TooltipOwnerClass = "lightside-tooltip-owner";

        /// <summary>Marks a control that owns mouse-wheel input instead of scrolling an ancestor.</summary>
        public const string WheelOwnerClass = "lightside-wheel-owner";

        private const string StyleSheetPath = "LightSide/Inspector";
        private const string RegisteredClass = "lightside-inspector-styles--registered";
        private const string ScopeClass = "lightside-inspector-styles--scope";
        private const string AppliedClass = "lightside-inspector-styles--applied";
        private const string ServicesClass = "lightside-inspector-services--installed";
        private const string TooltipClass = "lightside-tooltip";
        private const string DarkClass = "lightside-inspector--dark";
        private const string LightClass = "lightside-inspector--light";
        private const string FocusedFieldClass = "lightside-field--focused";
        private const string InspectorElementClass = "unity-inspector-element";
        internal const string FormAxisClass = "lightside-form-axis";

        /// <summary>
        /// Marks a labeled field whose label follows the shared form-axis share of the field's own
        /// width. Deliberately distinct from the engine's aligned-field class: that one activates
        /// the engine's inspector-wide alignment, which subtracts the field's x-offset and
        /// collapses labels inside row cells.
        /// </summary>
        public const string FieldAxisClass = "lightside-field-axis";

        /// <summary>Marks the editing control of a parameter row, so it takes the width the row's trailing actions leave.</summary>
        public const string ParameterFieldClass = "lightside-parameter-row__field";

        private const string ParameterRowClass = "lightside-parameter-row";
        private const string HierarchyBodyClass = "lightside-hierarchy-body";
        private const string HoveredHierarchyBodyClass = HierarchyBodyClass + "--hovered";
        private const string HeaderHierarchyBodyClass = HierarchyBodyClass + "--header";
        private const string FieldHierarchyBodyClass = HierarchyBodyClass + "--field";
        private const string StringFieldClass = "lightside-string-field";
        private const string PlaceholderVisibleClass = StringFieldClass + "--placeholder";
        private const string LabeledStringFieldClass = StringFieldClass + "--labeled";
        private const string LabelResolvedClass = StringFieldClass + "--resolved";
        private const string StringFieldLabelsClass = "lightside-string-field-labels";
        private const string ValueDragClass = "lightside-value-drag";
        private const string ValueDragLockedClass = ValueDragClass + "--locked";
#if !UNITY_2023_1_OR_NEWER
        private const string PlaceholderLabelClass = StringFieldClass + "__placeholder";
#endif

        private static readonly ConditionalWeakTable<VisualElement, TooltipMetadata>
            tooltipMetadata = new();
#if !UNITY_2023_1_OR_NEWER
        private static readonly ConditionalWeakTable<TextField, Label> textFieldPlaceholders = new();
#endif
        private static StyleSheet styleSheet;

        internal static void SetTooltipTitle(VisualElement element, string value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            tooltipMetadata.Remove(element);
            if (!string.IsNullOrWhiteSpace(value))
                tooltipMetadata.Add(element, new TooltipMetadata(value.Trim()));
        }

        /// <summary>Creates a vertically spaced inspector root with the shared LightSide theme attached.</summary>
        public static InspectorStack CreateRoot()
        {
            var root = CreateSectionStack();
            root.AddToClassList(InspectorElementClass);
            root.AddToClassList(FormAxisClass);
            root.AddToClassList("lightside-inspector-root");
            return root;
        }

        /// <summary>Aligns every labeled field in a hierarchy to the shared form axis.</summary>
        public static T AlignFields<T>(T root) where T : VisualElement
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (!HasFormAxis(root)) root.AddToClassList(FormAxisClass);
            AlignFieldHierarchy(root);
            return root;
        }

        /// <summary>
        /// Presents string fields under a hierarchy with the label beside the value on the form
        /// axis instead of folding it into the input as a placeholder. A field reads the mark when
        /// it enters a panel: mark before attaching, and content built later is covered too.
        /// </summary>
        public static T LabelStringFields<T>(T root) where T : VisualElement
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            root.AddToClassList(StringFieldLabelsClass);
            return root;
        }

        internal static void AlignFieldHierarchy(VisualElement root)
        {
            if (root.ClassListContains(BaseField<int>.ussClassName) &&
                !root.ClassListContains(BaseField<int>.noLabelVariantUssClassName))
            {
                root.AddToClassList(FieldAxisClass);
                EngageFieldAxis(root);
            }
            foreach (var child in root.Children()) AlignFieldHierarchy(child);
        }

        private const string FieldAxisEngagedClass = FieldAxisClass + "--engaged";
        private const float FieldAxisShare = 0.4f;

        /// <summary>Marks a labeled field for the shared label axis and engages its engine.</summary>
        public static void MarkFieldAxis(VisualElement field)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            field.AddToClassList(FieldAxisClass);
            EngageFieldAxis(field);
        }

        /// <summary>
        /// Attaches the shared label-axis engine to one labeled field: the label's width becomes
        /// the axis share of the nearest form-axis container minus the field's own offset inside
        /// it, so every field under one container meets a single value column, while an equal row
        /// cell — itself a form-axis container — carries a local axis. Recomputed from geometry;
        /// removing <see cref="FieldAxisClass"/> disengages it.
        /// </summary>
        internal static void EngageFieldAxis(VisualElement field)
        {
            if (field.ClassListContains(FieldAxisEngagedClass)) return;
            field.AddToClassList(FieldAxisEngagedClass);
            field.RegisterCallback<GeometryChangedEvent>(
                static evt => ReflowFieldAxis((VisualElement)evt.target));
            field.RegisterCallback<AttachToPanelEvent>(
                static evt => ReflowFieldAxis((VisualElement)evt.target));
        }

        private static void ReflowFieldAxis(VisualElement field)
        {
            if (!field.ClassListContains(FieldAxisClass)) return;
            Label label = null;
            foreach (var child in field.Children())
            {
                if (child is not Label candidate ||
                    !candidate.ClassListContains("unity-base-field__label")) continue;
                label = candidate;
                break;
            }
            if (label == null) return;

            VisualElement axis = null;
            for (var current = field; current != null; current = current.parent)
            {
                if (!current.ClassListContains(FormAxisClass) &&
                    current.parent is InspectorRow row &&
                    row.ClassListContains(InspectorRow.EqualClass))
                    row.SynchronizeItems();
                if (!current.ClassListContains(FormAxisClass)) continue;
                axis = current;
                break;
            }
            if (axis == null) return;

            var axisWidth = axis.resolvedStyle.width;
            if (float.IsNaN(axisWidth) || axisWidth <= 0f) return;
            var offset = field.worldBound.x - axis.worldBound.x;
            if (float.IsNaN(offset)) return;
            var target = Mathf.Max(0f, axisWidth * FieldAxisShare - offset);
            if (Mathf.Abs(label.resolvedStyle.width - target) < 0.5f) return;
            label.style.width = target;
            label.style.minWidth = 0f;
            label.style.flexGrow = 0f;
            label.style.flexShrink = 0f;
        }

        /// <summary>Creates a card section, optionally with a disclosure header.</summary>
        public static InspectorSection CreateSection(string title,
            bool collapsible = false, bool initiallyExpanded = true)
        {
            if (string.IsNullOrEmpty(title))
                throw new ArgumentException("A section title is required.", nameof(title));
            return new InspectorSection(title, collapsible, initiallyExpanded);
        }

        /// <summary>Creates a titleless card using the shared section surface.</summary>
        public static InspectorSection CreateCard()
            => new InspectorSection(null, false, true);

        /// <summary>Creates a persistent section whose expanded title occupies a vertical rail.</summary>
        public static InspectorRailSection CreateRailSection(string title, string prefsKey = null)
        {
            if (string.IsNullOrEmpty(title))
                throw new ArgumentException("A section title is required.", nameof(title));
            return new InspectorRailSection(title, prefsKey ?? title);
        }

        /// <summary>Creates a standard secondary heading for content within a larger surface.</summary>
        public static Label CreateSubheading(string text)
        {
            if (string.IsNullOrEmpty(text))
                throw new ArgumentException("A subheading title is required.", nameof(text));
            var heading = new Label(text);
            heading.AddToClassList("lightside-subheading");
            Attach(heading);
            return heading;
        }

        /// <summary>Creates a logical parent whose direct children use the shared vertical rhythm.</summary>
        public static InspectorStack CreateStack() => new();

        /// <summary>Creates and mounts a vertically spaced content root in an editor panel.</summary>
        public static InspectorStack CreateWindowRoot(VisualElement panelRoot)
        {
            if (panelRoot == null) throw new ArgumentNullException(nameof(panelRoot));
            var root = CreateStack();
            root.style.flexGrow = 1f;
            panelRoot.Add(root);
            return root;
        }

        /// <summary>Creates a vertical layout using the shared spacing between inspector sections.</summary>
        public static InspectorStack CreateSectionStack()
        {
            var stack = CreateStack();
            stack.UseSectionSpacing();
            return stack;
        }

        /// <summary>Creates a vertical scroll view whose logical children use the shared vertical rhythm.</summary>
        public static InspectorScrollStack CreateScrollStack() => new();

        /// <summary>Creates a scrolling layout using the shared spacing between inspector sections.</summary>
        public static InspectorScrollStack CreateScrollSectionStack()
        {
            var scroll = CreateScrollStack();
            scroll.UseSectionSpacing();
            return scroll;
        }

        /// <summary>Creates a horizontally aligned row with shared spacing.</summary>
        public static InspectorRow CreateRow() => InspectorRow.Create(false, false);

        /// <summary>Creates a row whose direct children share the available width equally.</summary>
        public static InspectorRow CreateEqualRow()
        {
            var row = CreateRow();
            row.AddToClassList(InspectorRow.EqualClass);
            return row;
        }

        /// <summary>Creates a horizontal row that wraps controls while preserving shared spacing.</summary>
        public static InspectorRow CreateWrapRow() => InspectorRow.Create(false, true);

        /// <summary>Creates a label-free horizontal row sized for compact collection items.</summary>
        public static InspectorRow CreateCompactRow() => InspectorRow.Create(true, false);

        /// <summary>
        /// Creates the row of one opt-in parameter: a compact row indented to clear its list's guide,
        /// carrying the editing control (marked <see cref="ParameterFieldClass"/>) and the row's own
        /// trailing actions.
        /// </summary>
        public static InspectorRow CreateParameterRow()
        {
            var row = CreateCompactRow();
            row.AddToClassList(ParameterRowClass);
            return row;
        }

        /// <summary>Creates a compact collection row that wraps controls with compact spacing.</summary>
        public static InspectorRow CreateCompactWrapRow() => InspectorRow.Create(true, true);

        /// <summary>Creates a nested body whose guide is aligned with its disclosure control.</summary>
        public static VisualElement CreateHierarchyBody(VisualElement header)
        {
            if (header is not InspectorFoldoutHeader && header is not InspectorFoldoutField)
                throw new ArgumentException(
                    "A hierarchy body requires an Inspector foldout header or field.", nameof(header));
            var body = CreateStack();
            body.AddToClassList(HierarchyBodyClass);
            body.AddToClassList(header is InspectorFoldoutField
                ? FieldHierarchyBodyClass
                : HeaderHierarchyBodyClass);
            var hoverTarget = header is InspectorFoldoutField field
                ? field.labelElement
                : header;
            hoverTarget.RegisterCallback<PointerEnterEvent>(_ =>
                body.AddToClassList(HoveredHierarchyBodyClass));
            hoverTarget.RegisterCallback<PointerLeaveEvent>(_ =>
                body.RemoveFromClassList(HoveredHierarchyBodyClass));
            AlignHierarchyGuide(header, body);
            return body;
        }

        /// <summary>
        /// Keeps the body's guide under the centre of its disclosure control, measured rather than
        /// assumed: a header composes its own leading chrome, so the control's offset is known only
        /// from layout.
        /// </summary>
        private static void AlignHierarchyGuide(VisualElement header, VisualElement body)
        {
            var toggle = header.Q<VisualElement>(
                             className: InspectorFoldoutHeader.ToggleUssClassName) ??
                         header.Q<VisualElement>(
                             className: InspectorFoldoutField.ToggleUssClassName);
            if (toggle == null) return;

            void Align()
            {
                if (body.panel == null || body.resolvedStyle.display == DisplayStyle.None) return;
                var control = toggle.worldBound;
                var guide = body.worldBound;
                if (control.width <= 0f || float.IsNaN(guide.x)) return;
                var delta = control.center.x - guide.x;
                if (Mathf.Abs(delta) < 0.5f) return;
                body.style.marginLeft = body.resolvedStyle.marginLeft + delta;
            }

            header.RegisterCallback<GeometryChangedEvent>(_ => Align());
            body.RegisterCallback<GeometryChangedEvent>(_ => Align());
            body.RegisterCallback<AttachToPanelEvent>(_ => Align());
        }

        /// <summary>
        /// Synchronizes a hierarchy body's visibility and disclosure state with its content.
        /// Pass <paramref name="collapsible"/> false for a body that is the whole presentation,
        /// where no disclosure affordance is shown.
        /// </summary>
        public static void RefreshHierarchyBody(
            VisualElement header, VisualElement body, bool expanded, bool collapsible = true)
        {
            var content = LogicalContent(body);
            var hasContent = false;
            for (var i = 0; i < content.hierarchy.childCount; i++)
            {
                var child = content.hierarchy[i];
                if (child.style.display.value == DisplayStyle.None ||
                    child.resolvedStyle.display == DisplayStyle.None ||
                    child.style.visibility.value != Visibility.Visible ||
                    child.resolvedStyle.visibility != Visibility.Visible) continue;
                hasContent = true;
                break;
            }

            var showBody = hasContent && expanded;
            switch (header)
            {
                case InspectorFoldoutHeader foldoutHeader:
                    foldoutHeader.SetCollapsible(hasContent && collapsible);
                    foldoutHeader.SetExpandedWithoutNotify(showBody);
                    break;
                case InspectorFoldoutField foldoutField:
                    foldoutField.SetCollapsible(hasContent && collapsible);
                    foldoutField.SetExpandedWithoutNotify(showBody);
                    break;
                default:
                    throw new ArgumentException(
                        "A hierarchy body requires an Inspector foldout header or field.",
                        nameof(header));
            }
            InspectorMotion.SetExpanded(body, showBody);
        }

        /// <summary>
        /// Removes dynamic child content after releasing its serialized bindings while preserving
        /// any tracking registered on the container itself.
        /// </summary>
        public static void ClearContent(VisualElement container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            var content = LogicalContent(container);
            foreach (var child in content.Children()) child.Unbind();
            content.Clear();
            if (content is InspectorRow row) row.SynchronizeItems();
            if (content is InspectorStack stack) stack.SynchronizeItems();
        }

        /// <summary>
        /// Creates a row carrying one field and a square trailing action locked to the control height. The
        /// field spans the full row so its label keeps the shared form axis; the action overlays the
        /// reserved right margin. Toggle <c>lightside-field-action-row--plain</c> on the row to reclaim that
        /// margin while the action is hidden.
        /// </summary>
        /// <remarks>
        /// For an action, not a value: a trailing control that carries data belongs in
        /// <see cref="CreateInlineValueRow"/>, which gives it the inline-value width instead.
        /// </remarks>
        public static VisualElement CreateFieldActionRow(VisualElement field, VisualElement action)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (action == null) throw new ArgumentNullException(nameof(action));
            var row = CreateCompactRow();
            row.AddToClassList("lightside-field-action-row");
            field.AddToClassList("lightside-field-action__field");
            field.AddToClassList(FieldAxisClass);
            action.AddToClassList("lightside-field-action");
            row.Add(field);
            row.Add(action);
            return row;
        }

        /// <summary>
        /// Creates a compact compound field whose primary control grows while the trailing value
        /// keeps the shared inline-value width.
        /// </summary>
        public static VisualElement CreateInlineValueRow(
            VisualElement primary, VisualElement trailingValue)
        {
            if (primary == null) throw new ArgumentNullException(nameof(primary));
            if (trailingValue == null)
                throw new ArgumentNullException(nameof(trailingValue));
            var row = CreateCompactRow();
            row.AddToClassList("lightside-inline-value-row");
            row.AddToClassList("lightside-inline-value-row--split");
            primary.AddToClassList("lightside-inline-value-row__primary");
            trailingValue.AddToClassList("lightside-inline-value-row__value");
            row.Add(primary);
            row.Add(trailingValue);
            return row;
        }

        /// <summary>
        /// Creates the shared leading-toggle layout used by fields that can inherit or override a
        /// value. An optional inactive presentation occupies the same flexible value column.
        /// </summary>
        public static VisualElement CreateOverrideRow(
            VisualElement toggle, VisualElement value, VisualElement inactive = null)
        {
            if (toggle == null) throw new ArgumentNullException(nameof(toggle));
            if (value == null) throw new ArgumentNullException(nameof(value));
            var row = CreateCompactRow();
            row.AddToClassList("lightside-override-row");
            toggle.AddToClassList("lightside-override-row__toggle");
            value.AddToClassList("lightside-override-row__value");
            row.Add(toggle);
            row.Add(value);
            if (inactive == null) return row;
            inactive.AddToClassList("lightside-override-row__value");
            inactive.AddToClassList("lightside-override-row__inactive");
            row.Add(inactive);
            return row;
        }

        /// <summary>Converts an editor panel rect into a screen-space rectangle.</summary>
        public static Rect ToScreenRect(Rect panelRect) => ScreenRect.FromPanel(panelRect).Value;

        /// <summary>Captures an editor panel rectangle in the typed screen-space form required by popup windows.</summary>
        public static ScreenRect CaptureScreenRect(Rect panelRect) => ScreenRect.FromPanel(panelRect);

        /// <summary>Ensures the shared theme is present whenever the element belongs to an editor panel.</summary>
        public static void Attach(VisualElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            PrepareFieldHierarchy(element);
            if (!element.ClassListContains(ScopeClass))
            {
                element.AddToClassList(ScopeClass);
                element.AddToClassList(RegisteredClass);
                element.RegisterCallback<AttachToPanelEvent>(OnAttached);
            }
            if (element.panel != null)
            {
                element.parent?.AddToClassList(RegisteredClass);
                Apply(element);
            }
        }

        private static void OnAttached(AttachToPanelEvent evt)
        {
            var element = (VisualElement)evt.currentTarget;
            element.parent?.AddToClassList(RegisteredClass);
            Apply(element);
        }

        private static VisualElement LogicalContent(VisualElement owner)
        {
            var content = owner;
            while (content.contentContainer != content) content = content.contentContainer;
            return content;
        }

        /// <summary>Read-only colour chip over the shared alpha checker, for selector rows and summaries.</summary>
        public static VisualElement CreateColorSwatch(Color color)
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList("lightside-color-swatch");
            var checkerImage = new Image
            {
                image = GradientDrawer.CheckerTexture(),
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
            };
            checkerImage.AddToClassList("lightside-color-swatch__layer");
            checkerImage.RegisterCallback<GeometryChangedEvent>(evt =>
                checkerImage.uv = new Rect(0f, 0f,
                    evt.newRect.width / 12f, evt.newRect.height / 12f));
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("lightside-color-swatch__layer");
            fill.style.backgroundColor = color;
            root.Add(checkerImage);
            root.Add(fill);
            return root;
        }

        internal static VisualElement CreateDropdownArrow()
        {
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.AddToClassList("lightside-dropdown-arrow");
            arrow.Add(new DropdownArrowShape());
            return arrow;
        }

        /// <summary>
        /// Creates a bold single-line status label in <paramref name="color"/>. The colour is passed
        /// through <see cref="EditorResources.ForSkin"/>, so a value picked for one editor skin stays
        /// legible on the other.
        /// </summary>
        public static Label CreateStatusLabel(string text, Color color)
        {
            var label = new Label(text);
            label.style.color = EditorResources.ForSkin(color);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        internal static Label CreateCountChip()
        {
            var chip = new Label();
            chip.AddToClassList("lightside-count-chip");
            return chip;
        }

        internal static VisualElement CreateCheckmark()
        {
            var mark = new VisualElement { pickingMode = PickingMode.Ignore };
            mark.AddToClassList("lightside-checkmark");
            var shortStroke = new VisualElement { pickingMode = PickingMode.Ignore };
            shortStroke.AddToClassList("lightside-checkmark__short");
            var longStroke = new VisualElement { pickingMode = PickingMode.Ignore };
            longStroke.AddToClassList("lightside-checkmark__long");
            mark.Add(shortStroke);
            mark.Add(longStroke);
            return mark;
        }

        private sealed class DropdownArrowShape : VisualElement
        {
            public DropdownArrowShape()
            {
                pickingMode = PickingMode.Ignore;
                AddToClassList("lightside-dropdown-arrow__shape");
                generateVisualContent += Draw;
            }

            private void Draw(MeshGenerationContext context)
            {
                var width = contentRect.width;
                var height = contentRect.height;
                if (width <= 0f || height <= 0f) return;
                var painter = context.painter2D;
                painter.BeginPath();
                painter.MoveTo(Vector2.zero);
                painter.LineTo(new Vector2(width, 0f));
                painter.LineTo(new Vector2(width * 0.5f, height));
                painter.ClosePath();
                painter.fillColor = resolvedStyle.color;
                painter.Fill();
            }
        }

        private sealed class SmoothScroll
        {
            private const float ResponseSeconds = 0.055f;
            private const float SnapDistance = 0.15f;
            private const float ExternalChangeDistance = 0.1f;

            private static readonly ConditionalWeakTable<ScrollView, SmoothScroll> states = new();

            private readonly ScrollView scrollView;
            private readonly IVisualElementScheduledItem animation;
            private Vector2 target;
            private Vector2 lastApplied;
            private double lastTickTime;
            private bool active;

            private SmoothScroll(ScrollView scrollView)
            {
                this.scrollView = scrollView;
                animation = scrollView.schedule.Execute(Tick).Every(1);
                animation.Pause();
            }

            public static void Install(VisualElement panelRoot)
            {
                panelRoot.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
                panelRoot.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            }

            private static void OnWheel(WheelEvent evt)
            {
                if (evt.target is not VisualElement targetElement ||
                    !HasAttachedAncestor(targetElement)) return;
                var activeAtBoundary = false;
                for (var current = targetElement; current != null; current = current.parent)
                {
                    if (current.ClassListContains(WheelOwnerClass)) return;
                    if (current is not ScrollView scrollView) continue;
                    var state = states.GetValue(scrollView, view => new SmoothScroll(view));
                    if (state.TryScroll(evt))
                    {
                        Consume(evt);
                        return;
                    }
                    activeAtBoundary |= state.active;
                }
                if (activeAtBoundary) Consume(evt);
            }

            private static void OnPointerDown(PointerDownEvent evt)
            {
                if (evt.target is not VisualElement targetElement ||
                    !HasAttachedAncestor(targetElement)) return;
                for (var current = targetElement; current != null; current = current.parent)
                    if (current is ScrollView scrollView && states.TryGetValue(scrollView, out var state))
                        state.Stop();
            }

            private static void Consume(WheelEvent evt)
            {
#if !UNITY_2023_2_OR_NEWER
                evt.PreventDefault();
#endif
                evt.StopPropagation();
            }

            private bool TryScroll(WheelEvent evt)
            {
                var current = scrollView.scrollOffset;
                if (active && (current - lastApplied).sqrMagnitude >
                    ExternalChangeDistance * ExternalChangeDistance)
                    Stop();

                var delta = WheelDelta(evt) * scrollView.mouseWheelScrollSize;
                var origin = active ? target : current;
                var next = Clamp(origin + delta);
                if ((next - origin).sqrMagnitude <= Mathf.Epsilon) return false;

                target = next;
                lastApplied = current;
                if (!active)
                {
                    lastTickTime = EditorApplication.timeSinceStartup;
                    active = true;
                    animation.Resume();
                }
                return true;
            }

            private Vector2 WheelDelta(WheelEvent evt)
            {
                var x = evt.delta.x;
                var y = evt.delta.y;
                if (scrollView.mode == ScrollViewMode.Vertical) return new Vector2(0f, y);
                if (scrollView.mode == ScrollViewMode.Horizontal)
                    return new Vector2(Mathf.Approximately(x, 0f) ? y : x, 0f);
                if (evt.shiftKey && Mathf.Approximately(x, 0f)) return new Vector2(y, 0f);
                return new Vector2(x, y);
            }

            private Vector2 Clamp(Vector2 value)
            {
                if (scrollView.mode != ScrollViewMode.Vertical)
                    value.x = ClampAxis(value.x, scrollView.horizontalScroller);
                if (scrollView.mode != ScrollViewMode.Horizontal)
                    value.y = ClampAxis(value.y, scrollView.verticalScroller);
                return value;
            }

            private static float ClampAxis(float value, Scroller scroller)
            {
                var low = scroller.lowValue;
                return Mathf.Clamp(value, low, Mathf.Max(low, scroller.highValue));
            }

            private void Tick()
            {
                if (scrollView.panel == null)
                {
                    Stop();
                    return;
                }

                var current = scrollView.scrollOffset;
                if ((current - lastApplied).sqrMagnitude >
                    ExternalChangeDistance * ExternalChangeDistance)
                {
                    Stop();
                    return;
                }

                target = Clamp(target);
                var now = EditorApplication.timeSinceStartup;
                var elapsed = Mathf.Min((float)(now - lastTickTime), 0.05f);
                lastTickTime = now;
                var next = Vector2.LerpUnclamped(current, target,
                    1f - Mathf.Exp(-elapsed / ResponseSeconds));
                if ((next - target).sqrMagnitude <= SnapDistance * SnapDistance)
                    next = target;
                scrollView.scrollOffset = next;
                lastApplied = next;
                if (next == target) Stop();
            }

            private void Stop()
            {
                active = false;
                animation.Pause();
            }
        }

        private static void Apply(VisualElement element)
        {
            var root = StyleRoot(element);
            if (!root.ClassListContains(AppliedClass))
            {
                root.styleSheets.Add(SharedStyleSheet);
                root.AddToClassList(AppliedClass);
            }

            root.EnableInClassList(DarkClass, EditorGUIUtility.isProSkin);
            root.EnableInClassList(LightClass, !EditorGUIUtility.isProSkin);

            var panelRoot = element.panel.visualTree;
            if (panelRoot != root && panelRoot.ClassListContains(AppliedClass))
            {
                panelRoot.styleSheets.Remove(SharedStyleSheet);
                panelRoot.RemoveFromClassList(AppliedClass);
            }
            if (!panelRoot.ClassListContains(ServicesClass))
            {
                panelRoot.AddToClassList(ServicesClass);
                panelRoot.RegisterCallback<FocusInEvent>(OnFieldFocusIn, TrickleDown.TrickleDown);
                panelRoot.RegisterCallback<FocusOutEvent>(OnFieldFocusOut, TrickleDown.TrickleDown);
                panelRoot.RegisterCallback<AttachToPanelEvent>(OnStyledElementAttached,
                    TrickleDown.TrickleDown);
                panelRoot.Query<TextField>().ForEach(field =>
                {
                    if (HasAttachedAncestor(field)) PrepareTextField(field);
                });
                var tooltip = new InspectorTooltip(panelRoot);
                tooltip.styleSheets.Add(SharedStyleSheet);
                panelRoot.Add(tooltip);
                SmoothScroll.Install(panelRoot);
            }
        }

        private static StyleSheet SharedStyleSheet =>
            styleSheet ??= Resources.Load<StyleSheet>(StyleSheetPath)
                           ?? throw new InvalidOperationException(
                               $"Required inspector stylesheet '{StyleSheetPath}' is missing.");

        private static VisualElement StyleRoot(VisualElement element)
        {
            var root = element;
            for (var current = element.parent; current != null; current = current.parent)
                if (current.ClassListContains(ScopeClass)) root = current;
            return root;
        }

        private static void OnStyledElementAttached(AttachToPanelEvent evt)
        {
            if (evt.target is VisualElement element && HasAttachedAncestor(element))
                PrepareFieldHierarchy(element);
        }

        private static bool HasAttachedAncestor(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.ClassListContains(ScopeClass)) return true;
            return false;
        }

        private static bool HasFormAxis(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.ClassListContains(FormAxisClass)) return true;
            return false;
        }

        internal static void PrepareFieldHierarchy(VisualElement root)
        {
            if (root is TextField field) PrepareTextField(field);
            else PrepareValueField(root);
            for (var i = 0; i < root.hierarchy.childCount; i++)
                PrepareFieldHierarchy(root.hierarchy[i]);
        }

        private static void PrepareValueField(VisualElement element)
        {
            if (InstallValueDragger<int>(element)) return;
            if (InstallValueDragger<float>(element)) return;
            if (InstallValueDragger<long>(element)) return;
            if (InstallValueDragger<double>(element)) return;
            if (InstallValueDragger<uint>(element)) return;
            InstallValueDragger<ulong>(element);
        }

        private static bool InstallValueDragger<T>(VisualElement element)
        {
            if (element is not BaseField<T> field || element is not IValueField<T> driven) return false;
            if (element is TextInputBaseField<T> box) ValueDragger<T, T>.Install(field, driven, box);
            else if (element.Q<TextField>(className: BaseSlider<float>.textFieldClassName) is { } text)
                ValueDragger<T, string>.Install(field, driven, text);
            return true;
        }

        /// <summary>
        /// Makes a control's value box its only drag zone, replacing the engine's label dragger: the box runs the
        /// shared <see cref="ValueScrub{TValue}"/>, and a press that stays under the drag threshold falls through
        /// to text editing with the value selected. A read-only or disabled control, and one already being typed
        /// into, never drags. The box is the control itself for a numeric field and a separate text field for a
        /// slider, so the dragged value and the edited text carry their own types.
        /// </summary>
        private sealed class ValueDragger<TValue, TText>
        {
            private readonly BaseField<TValue> field;
            private readonly TextInputBaseField<TText> box;
            private readonly VisualElement input;
            private bool editing;

            public static void Install(BaseField<TValue> field, IValueField<TValue> driven,
                TextInputBaseField<TText> box)
            {
                var input = box.Q(TextInputBaseField<TText>.textInputUssName);
                if (input == null || input.ClassListContains(ValueDragClass)) return;
                new ValueDragger<TValue, TText>(field, driven, box, input);
            }

            private ValueDragger(BaseField<TValue> field, IValueField<TValue> driven,
                TextInputBaseField<TText> box, VisualElement input)
            {
                this.field = field;
                this.box = box;
                this.input = input;
                input.AddToClassList(ValueDragClass);
                SyncAffordances();
                field.RegisterCallback<PointerEnterEvent>(_ => SyncAffordances());
                field.RegisterCallback<PointerDownEvent>(OnLabelPointerDown, TrickleDown.TrickleDown);
                input.RegisterCallback<FocusInEvent>(_ => editing = true);
                input.RegisterCallback<FocusOutEvent>(_ => editing = false);
                new ValueScrub<TValue>(input, driven, () => Draggable && !editing, horizontal: true, clicked: EditText);
            }

            private void EditText()
            {
                box.Focus();
                box.SelectAll();
            }

            private bool Draggable => !box.isReadOnly && field.enabledInHierarchy;

            /// <summary>
            /// Settles what the control advertises before a press reads it: the value box offers
            /// the drag only while the control takes one, and the label never advertises the
            /// engine's dragger, which the control restores whenever its read-only state changes.
            /// </summary>
            private void SyncAffordances()
            {
                input.EnableInClassList(ValueDragLockedClass, !Draggable);
                field.labelElement.RemoveFromClassList(BaseField<TValue>.labelDraggerVariantUssClassName);
            }

            private void OnLabelPointerDown(PointerDownEvent evt)
            {
                if (evt.button != 0 || evt.target is not VisualElement target) return;
                var label = field.labelElement;
                if (target == label || label.Contains(target)) evt.StopPropagation();
            }
        }

        private static void PrepareTextField(TextField field)
        {
            if (field.ClassListContains(StringFieldClass)) return;
            field.AddToClassList(StringFieldClass);
            field.RemoveFromClassList(FieldAxisClass);
            field.AddToClassList(BaseField<int>.noLabelVariantUssClassName);
#if UNITY_6000_3_OR_NEWER
            field.Q<VisualElement>(TextInputBaseField<string>.textInputUssName)
                .style.unityTextGenerator = TextGeneratorType.Standard;
#endif

            if (!string.IsNullOrWhiteSpace(field.label))
                SetTooltipTitle(field, field.label);
            field.RegisterValueChangedCallback(_ => RefreshTextFieldPresentation(field));
            field.RegisterCallback<AttachToPanelEvent>(_ => ResolveTextFieldLabel(field));
            ResolveTextFieldLabel(field);
        }

        /// <summary>
        /// Settles one string field's label presentation once its ancestors are known, which is
        /// why it runs on the field's first panel attach rather than at construction.
        /// </summary>
        private static void ResolveTextFieldLabel(TextField field)
        {
            if (field.panel != null && !field.ClassListContains(LabelResolvedClass))
            {
                field.AddToClassList(LabelResolvedClass);
                if (!string.IsNullOrWhiteSpace(field.label) && HasStringFieldLabels(field))
                {
                    field.AddToClassList(LabeledStringFieldClass);
                    field.RemoveFromClassList(BaseField<int>.noLabelVariantUssClassName);
                    MarkFieldAxis(field);
                }
                else PrepareTextFieldPlaceholder(field, field.label);
            }
            RefreshTextFieldPresentation(field);
        }

        private static bool HasStringFieldLabels(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current.ClassListContains(StringFieldLabelsClass)) return true;
            return false;
        }

        /// <summary>Synchronizes placeholder styling after a silent value or mixed-state update.</summary>
        public static void RefreshTextFieldPresentation(TextField field)
        {
            if (field == null || !field.ClassListContains(StringFieldClass)) return;
            field.EnableInClassList(PlaceholderVisibleClass,
                !field.showMixedValue && string.IsNullOrEmpty(field.value) &&
                HasTextFieldPlaceholder(field));
        }

        private static void PrepareTextFieldPlaceholder(TextField field, string label)
        {
#if UNITY_2023_1_OR_NEWER
            if (!string.IsNullOrWhiteSpace(label))
            {
                field.textEdition.placeholder = label;
                field.label = null;
            }
            if (!string.IsNullOrEmpty(field.textEdition.placeholder))
                field.textEdition.hidePlaceholderOnFocus = false;
#else
            if (string.IsNullOrWhiteSpace(label)) return;
            var placeholder = new Label(label) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList(PlaceholderLabelClass);
            field.hierarchy.Add(placeholder);
            textFieldPlaceholders.Add(field, placeholder);
            field.label = null;
#endif
        }

        private static bool HasTextFieldPlaceholder(TextField field)
        {
#if UNITY_2023_1_OR_NEWER
            return !string.IsNullOrEmpty(field.textEdition.placeholder);
#else
            return textFieldPlaceholders.TryGetValue(field, out _);
#endif
        }

        private static void OnFieldFocusIn(FocusInEvent evt)
        {
            if (evt.target is not VisualElement target || !HasAttachedAncestor(target)) return;
            for (var current = evt.target as VisualElement; current != null; current = current.parent)
                if (current.ClassListContains(BaseField<int>.ussClassName))
                    current.AddToClassList(FocusedFieldClass);
        }

        private static void OnFieldFocusOut(FocusOutEvent evt)
        {
            if (evt.target is not VisualElement target || !HasAttachedAncestor(target)) return;
            var next = evt.relatedTarget as VisualElement;
            for (var current = evt.target as VisualElement; current != null; current = current.parent)
            {
                if (!current.ClassListContains(BaseField<int>.ussClassName)) continue;
                if (next != null && (current == next || current.Contains(next))) continue;
                current.RemoveFromClassList(FocusedFieldClass);
            }
        }

        private sealed class InspectorTooltip : VisualElement
        {
            private const float Edge = 8f;
            private const float PointerOffset = 14f;
            private const float MaximumWidth = 420f;
            private const long DelayMilliseconds = 500;

            private readonly VisualElement root;
#if UNITY_6000_5_OR_NEWER
            private readonly Label title;
            private readonly Label description;
#else
            private readonly IMGUIContainer legacyText;
            private GUIStyle legacyTitleStyle;
            private GUIStyle legacyDescriptionStyle;
            private float legacyMaximumWidth;
#endif
            private VisualElement pointerTarget;
            private VisualElement hoverTarget;
            private IVisualElementScheduledItem pendingShow;
            private Vector2 pointerPosition;
            private string currentTitle;
            private string currentDescription;
            private bool isVisible;

            public InspectorTooltip(VisualElement root)
            {
                this.root = root;
                pickingMode = PickingMode.Ignore;
                AddToClassList(RegisteredClass);
                AddToClassList(TooltipClass);
                EnableInClassList(DarkClass, EditorGUIUtility.isProSkin);
                EnableInClassList(LightClass, !EditorGUIUtility.isProSkin);

#if UNITY_6000_5_OR_NEWER
                title = new Label
                {
                    enableRichText = true,
                    pickingMode = PickingMode.Ignore,
                };
                title.AddToClassList(TooltipClass + "__title");
                Add(title);

                description = new Label
                {
                    enableRichText = true,
                    pickingMode = PickingMode.Ignore,
                };
                description.AddToClassList(TooltipClass + "__description");
                Add(description);
#else
                legacyText = new IMGUIContainer(DrawLegacyText)
                {
                    focusable = false,
                    pickingMode = PickingMode.Ignore,
                };
                Add(legacyText);
#endif

                root.Query<TextElement>().ForEach(element =>
                {
                    if (HasAttachedAncestor(element)) PrepareTextElement(element);
                });
                root.RegisterCallback<AttachToPanelEvent>(OnElementAttached,
                    TrickleDown.TrickleDown);
                root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
                root.RegisterCallback<PointerDownEvent>(_ => ResetHover(), TrickleDown.TrickleDown);
                root.RegisterCallback<WheelEvent>(_ => ResetHover(), TrickleDown.TrickleDown);
                root.RegisterCallback<TooltipEvent>(OnTooltip, TrickleDown.TrickleDown);
                RegisterCallback<GeometryChangedEvent>(_ => Position());
            }

#if !UNITY_6000_5_OR_NEWER
            private static GUIStyle CreateLegacyTextStyle(
                int fontSize, FontStyle fontStyle, float opacity)
            {
                var style = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    richText = true,
                    wordWrap = true,
                    fontSize = fontSize,
                    fontStyle = fontStyle,
                    margin = new RectOffset(),
                    padding = new RectOffset(),
                };
                var color = EditorGUIUtility.isProSkin
                    ? new Color(220f / 255f, 222f / 255f, 225f / 255f, opacity)
                    : new Color(42f / 255f, 44f / 255f, 48f / 255f, opacity);
                style.normal.textColor = color;
                return style;
            }

            /// <summary>
            /// Creates the legacy styles on the first IMGUI pass. <see cref="EditorStyles"/> members are
            /// unset until the editor renders IMGUI for the first time after a domain reload, so the styles
            /// cannot be built at construction or from UI Toolkit callbacks.
            /// </summary>
            private void EnsureLegacyStyles()
            {
                if (legacyTitleStyle != null) return;
                legacyTitleStyle = CreateLegacyTextStyle(11, FontStyle.Bold, 1f);
                legacyDescriptionStyle = CreateLegacyTextStyle(12, FontStyle.Normal, 0.72f);
            }

            private void DrawLegacyText()
            {
                EnsureLegacyStyles();
                var maximumWidth = Mathf.Max(1f, legacyMaximumWidth);
                GUILayout.Label(currentTitle ?? string.Empty, legacyTitleStyle,
                    GUILayout.MaxWidth(maximumWidth));
                if (string.IsNullOrWhiteSpace(currentDescription)) return;
                GUILayout.Space(6f);
                GUILayout.Label(currentDescription, legacyDescriptionStyle,
                    GUILayout.MaxWidth(maximumWidth));
            }

            private float PreferredLegacyTextWidth()
            {
                var width = legacyTitleStyle.CalcSize(
                    new GUIContent(currentTitle ?? string.Empty)).x;
                if (!string.IsNullOrWhiteSpace(currentDescription))
                    width = Mathf.Max(width, legacyDescriptionStyle.CalcSize(
                        new GUIContent(currentDescription)).x);
                return Mathf.Max(1f, Mathf.Min(legacyMaximumWidth, Mathf.Ceil(width)));
            }

            /// <summary>
            /// Sizes the legacy text container to its measured text width. Until the first IMGUI pass has
            /// built the styles, measuring is impossible; the width is then applied one tick later, after
            /// the container's own measure pass has built them. Hiding the tooltip cancels the retry.
            /// </summary>
            private void ApplyLegacyTextWidth()
            {
                if (!isVisible) return;
                if (legacyTitleStyle == null)
                {
                    legacyText.schedule.Execute(ApplyLegacyTextWidth);
                    return;
                }
                legacyText.style.width = PreferredLegacyTextWidth();
            }
#endif

            private static void PrepareTextElement(TextElement element)
                => element.displayTooltipWhenElided = false;

            private static void OnElementAttached(AttachToPanelEvent evt)
            {
                if (evt.target is TextElement element && HasAttachedAncestor(element))
                    PrepareTextElement(element);
            }

            private void OnPointerMove(PointerMoveEvent evt)
            {
                pointerTarget = evt.target as VisualElement;
                pointerPosition = evt.position;
                if (!HasAttachedAncestor(pointerTarget))
                {
                    ResetHover();
                    return;
                }
                if (!TryResolve(out var titleText, out var descriptionText,
                        out var tooltipTarget))
                {
                    ResetHover();
                    return;
                }
                if (tooltipTarget != hoverTarget)
                {
                    pendingShow?.Pause();
                    Hide();
                    hoverTarget = tooltipTarget;
                    pendingShow = schedule.Execute(ShowPending)
                        .StartingIn(DelayMilliseconds);
                    return;
                }
                if (isVisible) Show(titleText, descriptionText);
            }

            private void OnPointerLeave(PointerLeaveEvent evt)
            {
                if (evt.target == root) ResetHover();
            }

            private void OnTooltip(TooltipEvent evt)
            {
                if (evt.target is not VisualElement element || !HasAttachedAncestor(element))
                    return;
                evt.tooltip = string.Empty;
                evt.StopImmediatePropagation();
            }

            private void ShowPending()
            {
                pendingShow = null;
                if (!TryResolve(out var titleText, out var descriptionText,
                        out var tooltipTarget) || tooltipTarget != hoverTarget)
                {
                    ResetHover();
                    return;
                }
                Show(titleText, descriptionText);
            }

            private bool TryResolve(out string titleText, out string descriptionText,
                out VisualElement tooltipTarget)
            {
                var textElement = FindTextElement(pointerTarget, pointerPosition);
                if (IsMultilineInput(textElement ?? pointerTarget, pointerPosition))
                {
                    titleText = null;
                    descriptionText = null;
                    tooltipTarget = null;
                    return false;
                }

                var controlTarget = FindControl(textElement ?? pointerTarget);
                var field = FindField(textElement ?? pointerTarget);
                var fieldLabel = field?.Q<TextElement>(
                    className: BaseField<int>.labelUssClassName);
                var fieldValue = field != null &&
                                 (fieldLabel == null ||
                                  !IsVisibleAt(fieldLabel, pointerPosition));
                var visibleFieldLabel = fieldLabel != null &&
                                        !string.IsNullOrWhiteSpace(fieldLabel.text) &&
                                        fieldLabel.resolvedStyle.display != DisplayStyle.None &&
                                        fieldLabel.resolvedStyle.visibility == Visibility.Visible &&
                                        fieldLabel.worldBound.width > 0.5f &&
                                        fieldLabel.worldBound.height > 0.5f;
                var semanticTitle = TooltipTitle(controlTarget);
                titleText = fieldValue
                    ? (visibleFieldLabel ? fieldLabel.text.Trim() : semanticTitle)
                    : textElement?.text?.Trim();
                titleText ??= semanticTitle;
                descriptionText = FindDescription(textElement ?? pointerTarget, titleText,
                    controlTarget, out var descriptionTarget);
                tooltipTarget = controlTarget ?? descriptionTarget ?? pointerTarget ?? textElement;

                var hasDescription = !string.IsNullOrWhiteSpace(descriptionText);
                if (string.IsNullOrWhiteSpace(titleText))
                {
                    titleText = descriptionText;
                    descriptionText = null;
                    return !string.IsNullOrWhiteSpace(titleText);
                }

                return hasDescription || IsTextClipped(textElement) ||
                       fieldValue && !visibleFieldLabel &&
                       !string.IsNullOrWhiteSpace(semanticTitle);
            }

            private static VisualElement FindField(VisualElement start)
            {
                for (var current = start; current != null; current = current.parent)
                    if (current.ClassListContains(BaseField<int>.ussClassName))
                        return current;
                return null;
            }

            /// <summary>
            /// Whether the pointer sits over multiline text the tooltip would occlude. An empty
            /// area occludes nothing, so it keeps documenting itself like a single-line control —
            /// the only surface it has when its label is collapsed into a placeholder.
            /// </summary>
            private bool IsMultilineInput(VisualElement start, Vector2 position)
            {
                for (var current = start; current != null; current = current.parent)
                {
                    if (current is TextField { multiline: true } field)
                        return !string.IsNullOrEmpty(field.value) &&
                               (field.labelElement == null ||
                                !IsVisibleAt(field.labelElement, position));
                    if (current == root) break;
                }
                return false;
            }

            private VisualElement FindControl(VisualElement start)
            {
                VisualElement field = null;
                Button button = null;
                VisualElement focusable = null;
                for (var current = start; current != null; current = current.parent)
                {
                    if (current.ClassListContains(TooltipOwnerClass))
                        return current;
                    if (field == null &&
                        current.ClassListContains(BaseField<int>.ussClassName))
                        field = current;
                    button ??= current as Button;
                    if (focusable == null && current.focusable) focusable = current;
                    if (current == root) break;
                }
                return field ?? button ?? focusable;
            }

            private TextElement FindTextElement(VisualElement target, Vector2 position)
            {
                if (target == null) return null;
                TextElement result = null;
                var resultArea = float.PositiveInfinity;

                void Consider(TextElement candidate)
                {
                    if (candidate == null) return;
#if UNITY_6000_5_OR_NEWER
                    if (candidate == title || candidate == description) return;
#endif
                    if (string.IsNullOrWhiteSpace(candidate.text) ||
                        !IsVisibleAt(candidate, position))
                        return;
                    var bounds = candidate.worldBound;
                    var area = bounds.width * bounds.height;
                    if (area >= resultArea) return;
                    result = candidate;
                    resultArea = area;
                }

                for (var current = target; current != null; current = current.parent)
                {
                    if (current is TextElement candidate) Consider(candidate);
                    if (current == root) break;
                }

                if (target != root) target.Query<TextElement>().ForEach(Consider);
                return result;
            }

            private bool IsVisibleAt(VisualElement element, Vector2 position)
            {
                for (var current = element; current != null; current = current.parent)
                {
                    var style = current.resolvedStyle;
                    if (style.display == DisplayStyle.None ||
                        style.visibility != Visibility.Visible ||
                        !current.worldBound.Contains(position))
                        return false;
                    if (current == root) return true;
                }
                return false;
            }

            private string FindDescription(VisualElement start, string elementText,
                VisualElement controlTarget, out VisualElement descriptionTarget)
            {
                descriptionTarget = null;
                for (var current = start; current != null; current = current.parent)
                {
                    var candidate = current.tooltip?.Trim();
                    if (!string.IsNullOrWhiteSpace(candidate) &&
                        !string.Equals(candidate, elementText, StringComparison.Ordinal) &&
                        (current is not TextElement textElement ||
                         !string.Equals(candidate, textElement.text?.Trim(),
                             StringComparison.Ordinal)))
                    {
                        descriptionTarget = current;
                        return candidate;
                    }
                    if (current == controlTarget || current == root) break;
                }
                return null;
            }

            private static bool IsTextClipped(TextElement element)
            {
                if (element == null) return false;
                if (element.isElided) return true;
                var size = element.contentRect.size;
                if (size.x <= 0f || size.y <= 0f) return false;
                if (element.resolvedStyle.whiteSpace == WhiteSpace.NoWrap)
                {
                    var measured = element.MeasureTextSize(element.text, 0f,
                        MeasureMode.Undefined, 0f, MeasureMode.Undefined);
                    return measured.x > size.x + 0.5f || measured.y > size.y + 0.5f;
                }
                var wrapped = element.MeasureTextSize(element.text, size.x,
                    MeasureMode.Exactly, 0f, MeasureMode.Undefined);
                return wrapped.y > size.y + 0.5f;
            }

            private void Show(string titleText, string descriptionText)
            {
                var wasVisible = isVisible;
                descriptionText ??= string.Empty;
                var changed = !string.Equals(currentTitle, titleText, StringComparison.Ordinal) ||
                              !string.Equals(currentDescription, descriptionText,
                                  StringComparison.Ordinal);
                if (changed)
                {
                    currentTitle = titleText;
                    currentDescription = descriptionText;
#if UNITY_6000_5_OR_NEWER
                    title.text = titleText;
                    description.text = descriptionText;
                    description.style.display = string.IsNullOrWhiteSpace(descriptionText)
                        ? DisplayStyle.None
                        : DisplayStyle.Flex;
#else
                    legacyText.onGUIHandler = null;
                    legacyText.onGUIHandler = DrawLegacyText;
#endif
                }
                if (!wasVisible)
                {
                    var maximumWidth = Mathf.Max(0f,
                        Mathf.Min(MaximumWidth, root.contentRect.width - Edge * 2f));
                    style.maxWidth = maximumWidth;
#if !UNITY_6000_5_OR_NEWER
                    legacyMaximumWidth = Mathf.Max(0f, maximumWidth - 20f);
#endif
                    style.display = DisplayStyle.Flex;
                }
                isVisible = true;
#if !UNITY_6000_5_OR_NEWER
                if (!wasVisible || changed) ApplyLegacyTextWidth();
#endif
                BringToFront();
                Position();
            }

            private void ResetHover()
            {
                pendingShow?.Pause();
                pendingShow = null;
                hoverTarget = null;
                Hide();
            }

            private void Position()
            {
                if (!isVisible) return;
                var local = root.WorldToLocal(pointerPosition);
                var width = layout.width;
                var height = layout.height;
                var left = local.x + PointerOffset;
                var top = local.y + PointerOffset;
                if (left + width + Edge > root.contentRect.width)
                    left = local.x - width - PointerOffset;
                if (top + height + Edge > root.contentRect.height)
                    top = local.y - height - PointerOffset;
                style.left = Mathf.Clamp(left, Edge,
                    Mathf.Max(Edge, root.contentRect.width - width - Edge));
                style.top = Mathf.Clamp(top, Edge,
                    Mathf.Max(Edge, root.contentRect.height - height - Edge));
            }

            private void Hide()
            {
                isVisible = false;
                style.display = DisplayStyle.None;
            }
        }

        private static string TooltipTitle(VisualElement element) =>
            element != null && tooltipMetadata.TryGetValue(element, out var metadata)
                ? metadata.Title
                : null;

        private sealed class TooltipMetadata
        {
            public TooltipMetadata(string title) => Title = title;

            public string Title { get; }
        }

    }

    /// <summary>Arranges its logical children vertically with the shared inspector spacing.</summary>
    public sealed class InspectorStack : VisualElement
    {
        private const string ItemClass = "lightside-stack__item";
        private const string LastItemClass = ItemClass + "--last";
        private const string SectionItemClass = "lightside-section-stack__item";

        /// <summary>Creates an empty vertical inspector layout.</summary>
        public InspectorStack()
        {
            AddToClassList("lightside-stack");
            AddToClassList("lightside-stack__content");
            InspectorVisuals.Attach(this);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttached, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(OnDetached, TrickleDown.TrickleDown);
        }

        /// <summary>Adds a child and immediately refreshes the vertical spacing contract.</summary>
        public new void Add(VisualElement child)
        {
            base.Add(child);
            SynchronizeItems();
        }

        /// <summary>Inserts a child and immediately refreshes the vertical spacing contract.</summary>
        public new void Insert(int index, VisualElement child)
        {
            base.Insert(index, child);
            SynchronizeItems();
        }

        internal void UseSectionSpacing()
        {
            AddToClassList("lightside-section-stack");
            SynchronizeItems();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt) => SynchronizeItems();

        private void OnAttached(AttachToPanelEvent evt) => SynchronizeItems();

        private void OnDetached(DetachFromPanelEvent evt) => SynchronizeItems();

        internal void SynchronizeItems()
        {
            var sectionSpacing = ClassListContains("lightside-section-stack");
            VisualElement lastVisible = null;
            for (var i = hierarchy.childCount - 1; i >= 0; i--)
            {
                var child = hierarchy[i];
                if (child.style.display.value == DisplayStyle.None ||
                    child.resolvedStyle.display == DisplayStyle.None) continue;
                lastVisible = child;
                break;
            }
            for (var i = 0; i < hierarchy.childCount; i++)
            {
                var child = hierarchy[i];
                InspectorVisuals.PrepareFieldHierarchy(child);
                child.AddToClassList(ItemClass);
                child.EnableInClassList(LastItemClass, child == lastVisible);
                child.EnableInClassList(SectionItemClass, sectionSpacing);
            }
        }
    }

    /// <summary>Arranges its logical children horizontally with the shared inspector spacing.</summary>
    public class InspectorRow : VisualElement
    {
        private const string ItemClass = "lightside-row__item";
        private const string LastItemClass = ItemClass + "--last";
        private const string LastLineItemClass = ItemClass + "--last-line";
        internal const string EqualClass = "lightside-row--equal";

        /// <summary>Creates a standard horizontal inspector row.</summary>
        public InspectorRow() : this(false, false) { }

        /// <summary>Creates a row specialization with compact or wrapping layout semantics.</summary>
        protected InspectorRow(bool compact, bool wrap) : this(compact, wrap, compact) { }

        private InspectorRow(bool compact, bool wrap, bool collectionHeight)
        {
            AddToClassList("lightside-row");
            AddToClassList("lightside-row__content");
            if (compact) AddToClassList("lightside-row--compact");
            if (collectionHeight) AddToClassList("lightside-compact-row");
            if (wrap) AddToClassList("lightside-row--wrap");
            InspectorVisuals.Attach(this);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            RegisterCallback<AttachToPanelEvent>(OnAttached, TrickleDown.TrickleDown);
            RegisterCallback<DetachFromPanelEvent>(OnDetached, TrickleDown.TrickleDown);
        }

        /// <summary>Adds an item and immediately refreshes the spacing between direct children.</summary>
        public new void Add(VisualElement child)
        {
            base.Add(child);
            SynchronizeItems();
        }

        /// <summary>Inserts an item and immediately refreshes the spacing between direct children.</summary>
        public new void Insert(int index, VisualElement child)
        {
            base.Insert(index, child);
            SynchronizeItems();
        }

        internal static InspectorRow Create(bool compact, bool wrap) => new(compact, wrap);

        internal static InspectorRow CreateActions() => new(true, false, false);

        private void OnGeometryChanged(GeometryChangedEvent evt) => SynchronizeItems();

        private void OnAttached(AttachToPanelEvent evt) => SynchronizeItems();

        private void OnDetached(DetachFromPanelEvent evt) => SynchronizeItems();

        internal void SynchronizeItems()
        {
            var wrap = ClassListContains("lightside-row--wrap");
            VisualElement lastVisible = null;
            for (var i = hierarchy.childCount - 1; i >= 0; i--)
            {
                var child = hierarchy[i];
                if (child.style.display.value == DisplayStyle.None ||
                    child.resolvedStyle.display == DisplayStyle.None) continue;
                lastVisible = child;
                break;
            }
            var lastLineY = lastVisible?.layout.center.y ?? 0f;
            var equal = ClassListContains(EqualClass);
            for (var i = 0; i < hierarchy.childCount; i++)
            {
                var child = hierarchy[i];
                InspectorVisuals.PrepareFieldHierarchy(child);
                child.AddToClassList(ItemClass);
                child.EnableInClassList(InspectorVisuals.FormAxisClass, equal);
                child.EnableInClassList(LastItemClass, child == lastVisible);
                child.EnableInClassList(LastLineItemClass,
                    wrap && child.style.display.value != DisplayStyle.None &&
                    child.resolvedStyle.display != DisplayStyle.None &&
                    child.layout.yMin <= lastLineY && child.layout.yMax >= lastLineY);
            }
        }
    }

    /// <summary>Provides a vertical scroll view whose logical content is an inspector stack.</summary>
    public sealed class InspectorScrollStack : ScrollView
    {
        private readonly InspectorStack content;

        /// <inheritdoc/>
        public override VisualElement contentContainer => content ?? base.contentContainer;

        /// <summary>Creates an empty vertical scrolling inspector layout.</summary>
        public InspectorScrollStack() : base(ScrollViewMode.Vertical)
        {
            content = new InspectorStack();
            base.contentContainer.Add(content);
            InspectorVisuals.Attach(this);
        }

        /// <summary>Adds a child to the vertically spaced scroll content.</summary>
        public new void Add(VisualElement child) => content.Add(child);

        /// <summary>Inserts a child into the vertically spaced scroll content.</summary>
        public new void Insert(int index, VisualElement child) => content.Insert(index, child);

        internal void UseSectionSpacing() => content.UseSectionSpacing();
    }

    /// <summary>Provides a foldout whose logical content is an inspector stack.</summary>
    public sealed class InspectorFoldout : Foldout
    {
        private readonly InspectorStack content;

        /// <inheritdoc/>
        public override VisualElement contentContainer => content ?? base.contentContainer;

        /// <summary>Creates an empty foldout with vertically spaced content.</summary>
        public InspectorFoldout()
        {
            content = new InspectorStack();
            base.contentContainer.Add(content);
            InspectorVisuals.Attach(this);
        }

        /// <summary>Adds a child to the vertically spaced foldout content.</summary>
        public new void Add(VisualElement child) => content.Add(child);

        /// <summary>Inserts a child into the vertically spaced foldout content.</summary>
        public new void Insert(int index, VisualElement child) => content.Insert(index, child);
    }

    /// <summary>
    /// Shared inspector card whose optional disclosure changes visibility without rebuilding its
    /// contents.
    /// </summary>
    public sealed class InspectorSection : VisualElement
    {
        private readonly InspectorStack content;
        private readonly Toggle disclosure;
        private bool expanded;

        /// <inheritdoc/>
        public override VisualElement contentContainer => (VisualElement)content ?? this;

        /// <summary>Current disclosure state.</summary>
        public bool Expanded
        {
            get => expanded;
            set => SetExpanded(value, true);
        }

        /// <summary>Raised when the disclosure state changes.</summary>
        public event Action<bool> Changed;

        internal InspectorSection(string title, bool collapsible, bool initiallyExpanded)
        {
            AddToClassList("lightside-section");
            content = new InspectorStack();
            content.AddToClassList("lightside-section__content");

            Label heading = null;
            if (!string.IsNullOrEmpty(title))
            {
                heading = new Label(title);
                heading.AddToClassList("lightside-section__title");
            }
            if (collapsible)
            {
                if (heading == null)
                    throw new ArgumentException(
                        "A collapsible section requires a title.", nameof(title));
                var header = new VisualElement { focusable = true };
                header.AddToClassList("lightside-section__header");
                disclosure = InspectorFoldoutHeader.CreateDisclosureToggle();
                disclosure.focusable = false;
                disclosure.AddToClassList("lightside-foldout-header__toggle");
                disclosure.AddToClassList("lightside-section__disclosure");
                header.Add(disclosure);
                header.Add(heading);
                hierarchy.Add(header);
                header.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    SetExpanded(!expanded, true);
                    evt.StopPropagation();
                });
                header.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                    SetExpanded(!expanded, true);
                    evt.StopPropagation();
                });
            }
            else if (heading != null)
            {
                hierarchy.Add(heading);
            }

            hierarchy.Add(content);
            InspectorVisuals.Attach(this);
            SetExpanded(!collapsible || initiallyExpanded, false);
        }

        /// <summary>Adds a child to the vertically spaced section content.</summary>
        public new void Add(VisualElement child) => content.Add(child);

        /// <summary>Inserts a child into the vertically spaced section content.</summary>
        public new void Insert(int index, VisualElement child) => content.Insert(index, child);

        /// <summary>Updates disclosure state without raising <see cref="Changed"/>.</summary>
        public void SetExpandedWithoutNotify(bool value) => SetExpanded(value, false);

        private void SetExpanded(bool value, bool notify)
        {
            if (expanded == value && notify) return;
            expanded = value;
            disclosure?.SetValueWithoutNotify(value);
            InspectorMotion.SetExpanded(content, value, notify);
            EnableInClassList("lightside-section--collapsed", !value);
            if (notify) Changed?.Invoke(value);
        }
    }

    /// <summary>A persistent inspector section with a vertical title rail while expanded.</summary>
    public sealed class InspectorRailSection : VisualElement
    {
        private const string PrefsKeyPrefix = "LightSide.Section.";

        private readonly string prefsKey;
        private readonly VisualElement header;
        private readonly Label title;
        private readonly InspectorStack content;
        private readonly InspectorMotion.Ticker sweep = new();
        private readonly InspectorMotion.Ticker titleFade = new();
        private bool expanded;

        /// <inheritdoc/>
        public override VisualElement contentContainer => (VisualElement)content ?? this;

        /// <summary>The clickable section header.</summary>
        public VisualElement Header => header;

        internal InspectorRailSection(string text, string key)
        {
            prefsKey = PrefsKeyPrefix + key;
            expanded = EditorPrefs.GetBool(prefsKey, true);
            AddToClassList("lightside-section");
            AddToClassList("lightside-rail-section");

            header = new VisualElement { focusable = true };
            header.AddToClassList("lightside-rail-section__header");
            title = new Label(text);
            title.AddToClassList("lightside-rail-section__title");
            header.Add(title);

            content = InspectorVisuals.CreateStack();
            content.AddToClassList("lightside-section__content");
            content.AddToClassList("lightside-rail-section__content");
            hierarchy.Add(header);
            hierarchy.Add(content);
            InspectorVisuals.Attach(this);

            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                SetExpanded(!expanded);
                evt.StopPropagation();
            });
            header.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.Space) return;
                SetExpanded(!expanded);
                evt.StopPropagation();
            });
            header.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (expanded) title.style.maxWidth = evt.newRect.height;
            });

            SetExpanded(expanded, false);
        }

        /// <summary>Adds a child to the vertically spaced rail-section content.</summary>
        public new void Add(VisualElement child) => content.Add(child);

        /// <summary>Inserts a child into the vertically spaced rail-section content.</summary>
        public new void Insert(int index, VisualElement child) => content.Insert(index, child);

        /// <summary>Changes the title shown by the section rail.</summary>
        public void SetTitle(string value) => title.text = value;

        private void SetExpanded(bool value, bool persist = true)
        {
            expanded = value;
            if (!value) title.style.maxWidth = new StyleLength(StyleKeyword.Null);
            if (persist) EditorPrefs.SetBool(prefsKey, value);
            if (persist && panel != null)
            {
                Animate(value);
                return;
            }
            sweep.Stop();
            titleFade.Stop();
            ClearMotionStyles();
            ApplyState(value);
        }

        private void ApplyState(bool value)
        {
            EnableInClassList("lightside-rail-section--expanded", value);
            EnableInClassList("lightside-rail-section--collapsed", !value);
            content.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Sweeps the opaque header across the section while the section resizes: collapsing
        /// stretches the rail over the content into the collapsed strip, expanding retracts it.
        /// Titles hand over by opacity. Both end sizes are measured before the first frame, and a
        /// toggle mid-sweep continues from the current geometry.
        /// </summary>
        private void Animate(bool value)
        {
            titleFade.Stop();
            var startHeight = layout.height;
            var startHeaderWidth = header.layout.width;
            if (float.IsNaN(startHeight) || float.IsNaN(startHeaderWidth))
            {
                ClearMotionStyles();
                ApplyState(value);
                return;
            }

            ClearMotionStyles();
            ApplyState(true);
            InspectorMotion.CompleteLayout(panel);
            var fullHeight = layout.height;
            var fullWidth = layout.width;
            var railWidth = header.layout.width;
            var contentHeight = content.layout.height;
            ApplyState(false);
            InspectorMotion.CompleteLayout(panel);
            var stripHeight = layout.height;

            ApplyState(true);
            content.style.height = contentHeight;
            content.style.marginLeft = railWidth;
            header.style.position = Position.Absolute;
            header.style.left = 0f;
            header.style.top = 0f;
            header.style.bottom = 0f;
            header.BringToFront();

            var targetHeight = value ? fullHeight : stripHeight;
            var targetHeaderWidth = value ? railWidth : fullWidth;
            sweep.Play(this, eased =>
            {
                style.height = Mathf.Lerp(startHeight, targetHeight, eased);
                header.style.width = Mathf.Lerp(startHeaderWidth, targetHeaderWidth, eased);
                title.style.opacity = value ? eased : 1f - eased;
                if (eased < 1f) return;
                ClearMotionStyles();
                ApplyState(value);
                if (value) return;
                title.style.opacity = 0f;
                titleFade.Play(this, faded => title.style.opacity = faded);
            });
        }

        private void ClearMotionStyles()
        {
            style.height = StyleKeyword.Null;
            header.style.position = StyleKeyword.Null;
            header.style.left = StyleKeyword.Null;
            header.style.top = StyleKeyword.Null;
            header.style.bottom = StyleKeyword.Null;
            header.style.width = StyleKeyword.Null;
            header.SendToBack();
            content.style.height = StyleKeyword.Null;
            content.style.marginLeft = StyleKeyword.Null;
            title.style.opacity = StyleKeyword.Null;
        }
    }
}
