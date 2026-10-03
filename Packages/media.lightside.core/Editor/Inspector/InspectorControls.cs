using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Multiline editor text input with local and serialized undo support.</summary>
    public sealed class InspectorTextArea : TextField
    {
        private const double UndoCoalesceTimeout = 0.5;

        private readonly LocalUndo localUndo = new();
        private SerializedUndo serializedUndo;
        private Action<string, string> serializedChanged;
        private Undo.UndoRedoCallback serializedUndoReset;
        private bool localUndoActive;
        private bool isolateChange;

        /// <summary>
        /// Creates a multiline input whose optional label becomes its placeholder, unless
        /// <see cref="InspectorVisuals.LabelStringFields{T}"/> governs the hierarchy it enters.
        /// </summary>
        public InspectorTextArea(string label = null) : base(label)
        {
            InspectorVisuals.Attach(this);
            multiline = true;
#if UNITY_2023_1_OR_NEWER
            verticalScrollerVisibility = ScrollerVisibility.Auto;
#else
            SetVerticalScrollerVisibility(ScrollerVisibility.Auto);
#endif
            AddToClassList("lightside-text-area");
            this.RegisterValueChangedCallback(OnValueChanged);
            RegisterCallback<PointerDownEvent>(_ => BreakUndo(), TrickleDown.TrickleDown);
            RegisterCallback<FocusOutEvent>(_ => BreakUndo());
            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            RegisterCallback<ValidateCommandEvent>(OnValidateCommand, TrickleDown.TrickleDown);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand, TrickleDown.TrickleDown);
            RegisterCallback<AttachToPanelEvent>(_ => AttachUndo());
            RegisterCallback<DetachFromPanelEvent>(_ => DetachUndo());
        }

        internal void RegisterSerializedValueChanged(Action<string, string> changed)
        {
            if (changed == null) throw new ArgumentNullException(nameof(changed));
            if (serializedUndo != null)
                throw new InvalidOperationException("Serialized text binding is already registered.");

            serializedUndo = new SerializedUndo();
            serializedChanged = changed;
            serializedUndoReset = ResetUndo;
            localUndoActive = false;
            localUndo.Clear();
            if (panel != null) Undo.undoRedoPerformed += serializedUndoReset;
        }

        private void OnValueChanged(ChangeEvent<string> evt)
        {
            if (serializedUndo != null)
            {
                var group = serializedUndo.Begin(evt.previousValue, evt.newValue);
                serializedChanged(evt.previousValue, evt.newValue);
                Undo.CollapseUndoOperations(group);
            }
            else if (localUndoActive)
            {
                localUndo.Record(evt.previousValue, evt.newValue, cursorIndex, selectIndex);
            }

            if (!isolateChange) return;
            isolateChange = false;
            BreakUndo();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (serializedUndo == null && localUndoActive)
            {
                if (evt.actionKey && evt.keyCode is KeyCode.Z or KeyCode.Y)
                {
                    var redo = evt.keyCode == KeyCode.Y || evt.shiftKey;
                    if (redo ? localUndo.Redo(this) : localUndo.Undo(this))
                    {
                        evt.StopImmediatePropagation();
#if !UNITY_2023_2_OR_NEWER
                        evt.PreventDefault();
#endif
                        return;
                    }
                }
                localUndo.CaptureSelection(cursorIndex, selectIndex);
            }

            if (evt.keyCode is KeyCode.LeftArrow or KeyCode.RightArrow
                or KeyCode.UpArrow or KeyCode.DownArrow or KeyCode.Home or KeyCode.End
                or KeyCode.PageUp or KeyCode.PageDown)
                BreakUndo();
        }

        private void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (serializedUndo != null || !localUndoActive) return;
            if ((evt.commandName == "Undo" && localUndo.CanUndo)
                || (evt.commandName == "Redo" && localUndo.CanRedo))
            {
                evt.StopPropagation();
            }
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if ((evt.commandName == "Undo" || evt.commandName == "Redo")
                && serializedUndo == null && localUndoActive)
            {
                var applied = evt.commandName == "Redo" ? localUndo.Redo(this) : localUndo.Undo(this);
                if (applied)
                {
                    evt.StopImmediatePropagation();
#if !UNITY_2023_2_OR_NEWER
                    evt.PreventDefault();
#endif
                }
                return;
            }

            if (evt.commandName == "UndoRedoPerformed")
            {
                if (serializedUndo != null) ResetUndo();
                else localUndo.Break();
                return;
            }
            if (evt.commandName is not ("Cut" or "Paste" or "Delete")) return;
            BreakUndo();
            if (serializedUndo == null && localUndoActive)
                localUndo.CaptureSelection(cursorIndex, selectIndex);
            isolateChange = true;
            schedule.Execute(() => isolateChange = false);
        }

        private void AttachUndo()
        {
            if (serializedUndo != null)
            {
                Undo.undoRedoPerformed += serializedUndoReset;
                return;
            }
            localUndo.Clear();
            localUndoActive = true;
        }

        private void DetachUndo()
        {
            if (serializedUndo != null) Undo.undoRedoPerformed -= serializedUndoReset;
            localUndoActive = false;
            localUndo.Clear();
            ResetUndo();
        }

        private void BreakUndo()
        {
            isolateChange = false;
            if (serializedUndo != null) serializedUndo.Break();
            else localUndo.Break();
        }

        private void ResetUndo()
        {
            isolateChange = false;
            serializedUndo?.Reset();
        }

        private enum EditOperation : byte { Insert, Delete, Replace }
        private readonly struct CoalescingEdit
        {
            public readonly EditOperation Operation;
            public readonly int CharIndex;
            public readonly int RemovedChars;
            public readonly int AddedChars;
            public readonly int Index;
            public readonly int CaretAfter;
            public readonly int RemovedCodepoints;
            public readonly Utf16TokenClass TokenClass;

            private CoalescingEdit(EditOperation operation, int charIndex, int removedChars,
                int addedChars, int index, int caretAfter, int removedCodepoints,
                Utf16TokenClass tokenClass)
            {
                Operation = operation;
                CharIndex = charIndex;
                RemovedChars = removedChars;
                AddedChars = addedChars;
                Index = index;
                CaretAfter = caretAfter;
                RemovedCodepoints = removedCodepoints;
                TokenClass = tokenClass;
            }

            public static CoalescingEdit FromChange(string before, string after)
            {
                before ??= string.Empty;
                after ??= string.Empty;
                Utf16.GetChangedRange(before.AsSpan(), after.AsSpan(), out var prefix,
                    out var removedLength, out var addedLength);
                var removed = before.AsSpan(prefix, removedLength);
                var added = after.AsSpan(prefix, addedLength);
                var index = Utf16.CountCodepoints(before.AsSpan(0, prefix));
                var operation = removed.IsEmpty
                    ? EditOperation.Insert
                    : added.IsEmpty ? EditOperation.Delete : EditOperation.Replace;
                var tokenClass = operation == EditOperation.Insert
                    ? Utf16.ClassifyToken(added)
                    : operation == EditOperation.Delete
                        ? Utf16.ClassifyToken(removed)
                        : Utf16TokenClass.Mixed;
                return new CoalescingEdit(operation, prefix, removed.Length, added.Length,
                    index, index + Utf16.CountCodepoints(added),
                    Utf16.CountCodepoints(removed), tokenClass);
            }

            public bool CanFollow(in CoalescingEdit previous, double elapsed)
            {
                if (Operation != previous.Operation || elapsed > UndoCoalesceTimeout
                    || TokenClass == Utf16TokenClass.Mixed || TokenClass != previous.TokenClass)
                    return false;
                if (Operation == EditOperation.Insert) return Index == previous.CaretAfter;
                return Operation == EditOperation.Delete
                       && (Index == previous.Index || Index == previous.Index - RemovedCodepoints);
            }

        }

        private sealed class CoalescingSession
        {
            private bool hasPrevious;
            private CoalescingEdit previous;
            private double timestamp;

            public bool Next(in CoalescingEdit current)
            {
                var now = EditorApplication.timeSinceStartup;
                var continues = hasPrevious && current.CanFollow(in previous, now - timestamp);
                previous = current;
                timestamp = now;
                hasPrevious = current.Operation != EditOperation.Replace
                              && current.TokenClass != Utf16TokenClass.Mixed;
                return continues;
            }

            public void Reset() => hasPrevious = false;
        }

        private sealed class SerializedUndo
        {
            private readonly CoalescingSession coalescing = new();
            private int group = -1;

            public int Begin(string before, string after)
            {
                var current = CoalescingEdit.FromChange(before, after);
                if (!coalescing.Next(in current))
                {
                    Undo.IncrementCurrentGroup();
                    Undo.SetCurrentGroupName("Drag Value");
                    group = Undo.GetCurrentGroup();
                }
                return group;
            }

            public void Break()
            {
                if (group >= 0) Undo.IncrementCurrentGroup();
                Reset();
            }

            public void Reset()
            {
                group = -1;
                coalescing.Reset();
            }
        }

        private sealed class LocalUndo
        {
            private const int EntryLimit = 256;

            private readonly CoalescingSession coalescing = new();
            private readonly List<Entry> entries = new();
            private int pointer;
            private bool replaying;
            private bool hasSelection;
            private int cursorBefore;
            private int selectionBefore;

            public bool CanUndo => pointer > 0;
            public bool CanRedo => pointer < entries.Count;

            public void CaptureSelection(int cursor, int selection)
            {
                cursorBefore = cursor;
                selectionBefore = selection;
                hasSelection = true;
            }

            public void Record(string before, string after, int cursorAfter, int selectionAfter)
            {
                if (replaying) return;
                before ??= string.Empty;
                after ??= string.Empty;
                var edit = CoalescingEdit.FromChange(before, after);
                var continues = coalescing.Next(in edit);
                if (pointer < entries.Count)
                {
                    entries.RemoveRange(pointer, entries.Count - pointer);
                    continues = false;
                }

                var removed = before.Substring(edit.CharIndex, edit.RemovedChars);
                var added = after.Substring(edit.CharIndex, edit.AddedChars);
                if (continues && pointer > 0)
                {
                    var entry = entries[pointer - 1];
                    if (edit.Operation == EditOperation.Insert)
                    {
                        entry.added += added;
                    }
                    else if (edit.CharIndex == entry.index)
                    {
                        entry.removed += removed;
                    }
                    else
                    {
                        entry.index = edit.CharIndex;
                        entry.removed = removed + entry.removed;
                    }
                    entry.cursorAfter = cursorAfter;
                    entry.selectionAfter = selectionAfter;
                    entries[pointer - 1] = entry;
                }
                else
                {
                    entries.Add(new Entry
                    {
                        index = edit.CharIndex,
                        removed = removed,
                        added = added,
                        cursorBefore = hasSelection ? cursorBefore : edit.CharIndex + edit.RemovedChars,
                        selectionBefore = hasSelection ? selectionBefore : edit.CharIndex + edit.RemovedChars,
                        cursorAfter = cursorAfter,
                        selectionAfter = selectionAfter,
                    });
                    pointer = entries.Count;
                    if (entries.Count > EntryLimit)
                    {
                        entries.RemoveAt(0);
                        pointer--;
                    }
                }
                hasSelection = false;
            }

            public bool Undo(InspectorTextArea field)
            {
                if (!CanUndo) return false;
                var entry = entries[--pointer];
                return Apply(field, in entry, true);
            }

            public bool Redo(InspectorTextArea field)
            {
                if (!CanRedo) return false;
                var entry = entries[pointer++];
                return Apply(field, in entry, false);
            }

            public void Break()
            {
                coalescing.Reset();
                hasSelection = false;
            }

            public void Clear()
            {
                entries.Clear();
                pointer = 0;
                replaying = false;
                Break();
            }

            private bool Apply(InspectorTextArea field, in Entry entry, bool undo)
            {
                var current = field.value ?? string.Empty;
                var remove = undo ? entry.added : entry.removed;
                var add = undo ? entry.removed : entry.added;
                if ((uint)entry.index > (uint)current.Length
                    || remove.Length > current.Length - entry.index
                    || !current.AsSpan(entry.index, remove.Length).SequenceEqual(remove.AsSpan()))
                {
                    Clear();
                    return false;
                }

                var result = current.Remove(entry.index, remove.Length).Insert(entry.index, add);
                replaying = true;
                try
                {
                    field.value = result;
                    var cursor = undo ? entry.cursorBefore : entry.cursorAfter;
                    var selection = undo ? entry.selectionBefore : entry.selectionAfter;
                    field.SelectRange(Math.Clamp(cursor, 0, result.Length),
                        Math.Clamp(selection, 0, result.Length));
                }
                finally
                {
                    replaying = false;
                    coalescing.Reset();
                }
                return true;
            }

            private struct Entry
            {
                public int index;
                public string removed;
                public string added;
                public int cursorBefore;
                public int selectionBefore;
                public int cursorAfter;
                public int selectionAfter;
            }
        }
    }

    /// <summary>Search input with a crisp text-only surface and a lightweight placeholder.</summary>
    public sealed class InspectorSearchField : VisualElement, INotifyValueChanged<string>
    {
        private readonly TextField field;
        private readonly Label placeholder;
        private readonly Button clear;

        /// <inheritdoc/>
        public string value
        {
            get => field.value;
            set => field.value = value ?? string.Empty;
        }

        /// <summary>Creates a search input using the supplied empty-value hint.</summary>
        public InspectorSearchField(string hint = "Search…")
        {
            InspectorVisuals.Attach(this);
            AddToClassList("lightside-search");
            focusable = true;
            delegatesFocus = true;
            field = new TextField();
            field.AddToClassList("lightside-search__field");
            placeholder = new Label(hint) { pickingMode = PickingMode.Ignore };
            placeholder.AddToClassList("lightside-search__placeholder");
            clear = new Button(() => value = string.Empty)
            {
                text = "×",
                tooltip = "Clear search",
            };
            clear.AddToClassList("lightside-search__clear");
            hierarchy.Add(field);
            hierarchy.Add(placeholder);
            hierarchy.Add(clear);
            field.RegisterValueChangedCallback(evt =>
            {
                evt.StopPropagation();
                RefreshPlaceholder();
                using var forwarded = ChangeEvent<string>.GetPooled(
                    evt.previousValue, evt.newValue);
                forwarded.target = this;
                SendEvent(forwarded);
            });
            RefreshPlaceholder();
        }

        /// <inheritdoc/>
        public void SetValueWithoutNotify(string newValue)
        {
            newValue ??= string.Empty;
            if (field.value != newValue) field.SetValueWithoutNotify(newValue);
            RefreshPlaceholder();
        }

        /// <summary>Moves keyboard focus into the editable text input.</summary>
        public new void Focus() => field.Focus();

        private void RefreshPlaceholder()
        {
            placeholder.style.display = string.IsNullOrEmpty(field.value)
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            clear.style.display = string.IsNullOrEmpty(field.value)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
    }

    /// <summary>LightSide checkbox with an explicit themed checked-state mark.</summary>
    public sealed class InspectorToggle : Toggle
    {
        /// <summary>Creates a themed checkbox with an optional label.</summary>
        public InspectorToggle(string label = null) : base(label)
        {
            InspectorVisuals.Attach(this);
            AddToClassList("lightside-toggle");
            var checkmark = this.Q<VisualElement>(className: Toggle.checkmarkUssClassName) ??
                            throw new InvalidOperationException("Toggle checkmark is missing.");
            var mark = InspectorVisuals.CreateCheckmark();
            mark.AddToClassList("lightside-toggle__mark");
            checkmark.Add(mark);
        }
    }

    /// <summary>Compact stateful button used for accent actions and mode switches.</summary>
    public class InspectorPillButton : Button
    {
        private bool hovered;
        private bool selected;
        private Color accent;

        /// <summary>Creates a pill button with an optional click handler.</summary>
        public InspectorPillButton(Action clicked = null) : base(clicked)
        {
            InspectorVisuals.Attach(this);
            AddToClassList("lightside-pill");
            RegisterCallback<PointerEnterEvent>(_ =>
            {
                hovered = true;
                RefreshOutline();
            });
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hovered = false;
                RefreshOutline();
            });
        }

        /// <summary>Updates the active, mixed, and optional icon presentation without changing layout.</summary>
        public virtual void SetState(bool active, bool mixed, Color accent, string iconName = null)
        {
            var panel = EditorResources.TogglePanelColor;
            this.accent = accent;
            selected = active || mixed;
            var background = panel;
            if (active)
                background = accent;
            else if (mixed)
                background = Color.Lerp(panel, accent, 0.5f);
            var foreground = selected ? EditorResources.ForegroundOn(background) : accent;

            style.backgroundColor = background;
            style.color = foreground;
            RefreshOutline();

            EnableInClassList("lightside-pill--icon", !string.IsNullOrEmpty(iconName));
            if (!string.IsNullOrEmpty(iconName))
            {
                var texture = EditorResources.GetColoredTexture(iconName, foreground);
                style.backgroundImage = texture != null
                    ? new StyleBackground(texture)
                    : new StyleBackground(StyleKeyword.None);
            }
            else
            {
                style.backgroundImage = new StyleBackground(StyleKeyword.None);
            }
        }

        private void RefreshOutline()
        {
            var color = Color.clear;
            if (hovered && enabledInHierarchy)
                color = selected ? Color.Lerp(accent, Color.white, 0.24f) : accent;
            style.borderLeftColor = color;
            style.borderRightColor = color;
            style.borderTopColor = color;
            style.borderBottomColor = color;
        }
    }

    /// <summary>Selector button whose foreground and interaction outline follow its current value.</summary>
    public sealed class InspectorSelectorButton : InspectorPillButton
    {
        /// <summary>Creates a compact arrow-only selector button.</summary>
        public static InspectorSelectorButton IconOnly(Action clicked = null)
        {
            var button = new InspectorSelectorButton(clicked);
            button.AddToClassList("lightside-selector-button--icon-only");
            return button;
        }

        private readonly Image valueIcon;

        /// <summary>Creates a selector button with the shared dropdown indicator.</summary>
        public InspectorSelectorButton(Action clicked = null) : base(clicked)
        {
            RemoveFromClassList(Button.ussClassName);
            AddToClassList("unity-base-popup-field__input");
            AddToClassList("unity-popup-field__input");
            valueIcon = EditorResources.CreateIcon((Texture)null);
            valueIcon.AddToClassList("lightside-selector-field__icon");
            valueIcon.style.display = DisplayStyle.None;
            Add(valueIcon);
            ValueElement = new TextElement();
            ValueElement.AddToClassList("unity-base-popup-field__text");
            Add(ValueElement);
            Arrow = InspectorVisuals.CreateDropdownArrow();
            Arrow.AddToClassList("lightside-selector-field__arrow");
            Add(Arrow);
        }

        /// <summary>Leading icon of the displayed value; null removes it.</summary>
        public void SetValueIcon(Texture icon)
        {
            valueIcon.image = icon;
            valueIcon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Text displayed before the dropdown indicator.</summary>
        public new string text
        {
            get => ValueElement.text;
            set => ValueElement.text = value ?? string.Empty;
        }

        internal TextElement ValueElement { get; }

        /// <summary>Dropdown indicator owned by this button.</summary>
        public VisualElement Arrow { get; }

        /// <inheritdoc/>
        public override void SetState(bool active, bool mixed, Color accent, string iconName = null)
        {
            base.SetState(active, mixed, accent, iconName);
            ValueElement.style.color = style.color;
        }

        /// <summary>Updates and returns the accent resolved from the semantic selector value.</summary>
        public Color SetValueAccent(object value, string fallbackIdentity = null)
        {
            var color = EditorResources.GetAccentColor(value, fallbackIdentity);
            SetState(false, false, color);
            return color;
        }
    }

    /// <summary>Icon-only action whose interaction states are expressed through tint.</summary>
    public sealed class InspectorIconButton : Button
    {
        private bool active;
        private bool hovered;
        private bool pressed;
        private Color accent;
        private string iconName;

        /// <summary>Creates an icon-only action with an optional click handler.</summary>
        public InspectorIconButton(Action clicked = null) : base(clicked)
        {
            InspectorVisuals.Attach(this);
            AddToClassList("lightside-icon-button");
            RegisterCallback<PointerEnterEvent>(_ =>
            {
                hovered = true;
                Refresh();
            });
            RegisterCallback<PointerLeaveEvent>(_ =>
            {
                hovered = false;
                pressed = false;
                Refresh();
            });
            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                pressed = true;
                Refresh();
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0) return;
                pressed = false;
                Refresh();
            });
            RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                pressed = false;
                Refresh();
            });
        }

        /// <summary>Updates the active state, accent, and icon without changing layout.</summary>
        public void SetState(bool active, Color accent, string iconName)
        {
            this.active = active;
            this.accent = accent;
            this.iconName = iconName;
            EnableInClassList("lightside-icon-button--active", active);
            Refresh();
        }

        private void Refresh()
        {
            var panel = EditorResources.TogglePanelColor;
            var color = active ? accent : EditorResources.IconColor;
            if (hovered)
                color = active
                    ? Color.Lerp(accent, EditorResources.ForegroundOn(panel), 0.24f)
                    : accent;
            if (pressed) color = Color.Lerp(accent, panel, 0.18f);
            var texture = string.IsNullOrEmpty(iconName)
                ? null
                : EditorResources.GetColoredTexture(iconName, color);
            style.backgroundImage = texture != null
                ? new StyleBackground(texture)
                : new StyleBackground(StyleKeyword.None);
            style.backgroundColor = Color.clear;
            style.borderLeftColor = Color.clear;
            style.borderRightColor = Color.clear;
            style.borderTopColor = Color.clear;
            style.borderBottomColor = Color.clear;
        }
    }

    /// <summary>Numeric slider that always exposes its editable value field.</summary>
    public sealed class InspectorSlider : Slider
    {
        /// <summary>Creates a labelled floating-point slider.</summary>
        public InspectorSlider(string label, float lowValue, float highValue)
            : base(label, lowValue, highValue)
        {
            InspectorVisuals.Attach(this);
            showInputField = true;
        }

        /// <summary>Creates an unlabeled floating-point slider.</summary>
        public InspectorSlider(float lowValue, float highValue)
            : base(lowValue, highValue)
        {
            InspectorVisuals.Attach(this);
            showInputField = true;
        }
    }

    /// <summary>Integer slider that always exposes its editable value field.</summary>
    public sealed class InspectorSliderInt : SliderInt
    {
        /// <summary>Creates a labelled integer slider.</summary>
        public InspectorSliderInt(string label, int lowValue, int highValue)
            : base(label, lowValue, highValue)
        {
            InspectorVisuals.Attach(this);
            showInputField = true;
        }

        /// <summary>Creates an unlabeled integer slider.</summary>
        public InspectorSliderInt(int lowValue, int highValue)
            : base(lowValue, highValue)
        {
            InspectorVisuals.Attach(this);
            showInputField = true;
        }
    }

    /// <summary>Compact integer slider whose accent fill and centered label share one surface.</summary>
    public sealed class InspectorFillSlider : VisualElement, INotifyValueChanged<int>
    {
        private readonly int min;
        private readonly int max;
        private readonly Func<int, string> format;
        private readonly VisualElement fill;
        private readonly Label label;
        private readonly VisualElement fillClip;
        private readonly Label fillLabel;
        private int current;

        /// <inheritdoc/>
        public int value
        {
            get => current;
            set
            {
                var next = Mathf.Clamp(value, min, max);
                if (next == current) return;
                var previous = current;
                SetValueWithoutNotify(next);
                using var evt = ChangeEvent<int>.GetPooled(previous, next);
                evt.target = this;
                SendEvent(evt);
            }
        }

        /// <summary>
        /// Creates a slider constrained to the inclusive integer range; <paramref name="format"/>
        /// renders the centered surface text, defaulting to the bare value.
        /// </summary>
        public InspectorFillSlider(int min, int max, int value, Func<int, string> format = null)
        {
            if (max < min) throw new ArgumentOutOfRangeException(nameof(max));
            InspectorVisuals.Attach(this);
            this.min = min;
            this.max = max;
            this.format = format;
            focusable = true;
            AddToClassList("lightside-fill-slider");

            label = new Label();
            label.AddToClassList("lightside-fill-slider__label");
            hierarchy.Add(label);

            fillClip = new VisualElement();
            fillClip.AddToClassList("lightside-fill-slider__clip");
            fill = new VisualElement();
            fill.AddToClassList("lightside-fill-slider__fill");
            fillClip.Add(fill);
            fillLabel = new Label();
            fillLabel.AddToClassList("lightside-fill-slider__label");
            fillClip.Add(fillLabel);
            hierarchy.Add(fillClip);

            RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                Focus();
                this.CapturePointer(evt.pointerId);
                SetFromPosition(evt.localPosition.x);
                evt.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!this.HasPointerCapture(evt.pointerId)) return;
                SetFromPosition(evt.localPosition.x);
                evt.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!this.HasPointerCapture(evt.pointerId)) return;
                this.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.LeftArrow || evt.keyCode == KeyCode.DownArrow)
                    this.value--;
                else if (evt.keyCode == KeyCode.RightArrow || evt.keyCode == KeyCode.UpArrow)
                    this.value++;
                else
                    return;
                evt.StopPropagation();
            });
            RegisterCallback<GeometryChangedEvent>(_ => Refresh());

            var accent = EditorResources.ToggleAccent;
            style.backgroundColor = EditorResources.TogglePanelColor;
            fill.style.backgroundColor = accent;
            label.style.color = accent;
            fillLabel.style.color = EditorResources.ForegroundOn(accent);
            SetValueWithoutNotify(value);
        }

        /// <inheritdoc/>
        public void SetValueWithoutNotify(int newValue)
        {
            current = Mathf.Clamp(newValue, min, max);
            Refresh();
        }

        private void SetFromPosition(float position)
        {
            if (contentRect.width <= 0f) return;
            value = Mathf.RoundToInt(Mathf.Lerp(min, max,
                Mathf.Clamp01(position / contentRect.width)));
        }

        private void Refresh()
        {
            var ratio = max == min ? 0f : (float)(current - min) / (max - min);
            var width = Mathf.Max(0f, contentRect.width * ratio);
            fill.style.width = contentRect.width;
            fillClip.style.width = width;
            label.style.width = contentRect.width;
            fillLabel.style.width = contentRect.width;
            var text = format?.Invoke(current) ?? current.ToString();
            label.text = text;
            fillLabel.text = text;
        }
    }
}
