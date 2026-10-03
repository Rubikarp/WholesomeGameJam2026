using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Presents a <see cref="DropdownMenu"/> through the shared <see cref="Selector"/> popup instead
    /// of the platform menu, so commands and choices share one appearance, search, and keyboard
    /// navigation. An action name may carry a slash-separated group path and
    /// <see cref="DropdownMenuAction.Status.Checked"/> draws the selector checkmark. An action's
    /// <c>userData</c> supplies its semantic accent, or a whole <see cref="Selector.SelectorItem"/>
    /// when the command needs the richer row — icon, decorator, trailing control — so a command can
    /// look exactly like the value it acts on; its name, group, value, and checked state still come
    /// from the action. A command that is not currently available is omitted rather than shown
    /// unusable, so the menu only ever offers what it can do.
    /// </summary>
    public static class InspectorContextMenu
    {
        private const DropdownMenuAction.Status UnavailableStatus =
            DropdownMenuAction.Status.Disabled | DropdownMenuAction.Status.Hidden;

        /// <summary>Icon of the shared Copy command, so it reads the same in every menu.</summary>
        public static Texture2D CopyIcon => EditorResources.GetTexture("copy");
        /// <summary>Icon of the shared Paste command.</summary>
        public static Texture2D PasteIcon => EditorResources.GetTexture("paste");
        /// <summary>Icon of the shared Duplicate command.</summary>
        public static Texture2D DuplicateIcon => EditorResources.GetTexture("duplicate");
        /// <summary>Icon of the shared Clear command.</summary>
        public static Texture2D ClearIcon => EditorResources.GetTexture("clear");
        /// <summary>Icon of the clipboard itself, for controls that open it.</summary>
        public static Texture2D ClipboardIcon => EditorResources.GetTexture("clipboard");

        /// <summary>Appends an always-available command whose row carries an icon.</summary>
        public static void AppendCommand(DropdownMenu menu, string name, Texture icon,
            Action<DropdownMenuAction> action)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            menu.AppendAction(name, action, _ => DropdownMenuAction.Status.Normal,
                new Selector.SelectorItem { icon = icon, tintIcon = true });
        }

        /// <summary>
        /// Opens <paramref name="menu"/> at <paramref name="anchor"/>, given in panel coordinates.
        /// Action statuses are resolved against <paramref name="triggerEvent"/>; a menu without
        /// visible actions opens nothing.
        /// </summary>
        public static void Show(Rect anchor, DropdownMenu menu, EventBase triggerEvent = null)
            => ShowAtScreenRect(ScreenRect.FromPanel(anchor), menu, triggerEvent);

        /// <summary>
        /// Opens <paramref name="menu"/> at an anchor already in screen coordinates. This is the form
        /// a deferred opener needs: panel coordinates only convert from inside the layout pass that
        /// produced them, so a menu opened after that pass has ended must carry the screen rectangle
        /// captured earlier.
        /// </summary>
        public static void ShowAtScreenRect(ScreenRect anchor, DropdownMenu menu,
            EventBase triggerEvent = null)
        {
            if (menu == null) throw new ArgumentNullException(nameof(menu));
            menu.PrepareForDisplay(triggerEvent);
            var items = CreateItems(menu);
            if (items.Length == 0) return;
            Selector.ShowAtScreenRect(anchor, items, null,
                value => ((DropdownMenuAction)value).Execute());
        }

        private static Selector.SelectorItem[] CreateItems(DropdownMenu menu)
        {
            var entries = menu.MenuItems();
            var items = new List<Selector.SelectorItem>(entries.Count);
            var pendingSeparator = false;
            string pendingSeparatorGroup = null;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] is DropdownMenuSeparator separator)
                {
                    pendingSeparator = items.Count > 0;
                    pendingSeparatorGroup = separator.subMenuPath;
                    continue;
                }
                if (entries[i] is not DropdownMenuAction action ||
                    (action.status & UnavailableStatus) != 0) continue;

                if (pendingSeparator)
                {
                    pendingSeparator = false;
                    items.Add(new Selector.SelectorItem
                    {
                        separator = true,
                        groupName = pendingSeparatorGroup,
                    });
                }

                var name = action.name ?? string.Empty;
                var leaf = name.LastIndexOf('/');
                var item = action.userData is Selector.SelectorItem template
                    ? template
                    : new Selector.SelectorItem { accentKey = action.userData };
                item.displayName = leaf < 0 ? name : name.Substring(leaf + 1);
                item.groupName = leaf < 0 ? null : name.Substring(0, leaf);
                item.value = action;
                item.selected = (action.status & DropdownMenuAction.Status.Checked) != 0;
                items.Add(item);
            }
            return items.ToArray();
        }
    }

    /// <summary>
    /// Opens a LightSide context menu for its target on secondary click, or on the keyboard menu
    /// key. Replaces <see cref="ContextualMenuManipulator"/>: the menu is authored the same way and
    /// the innermost target owns the click, but it is presented by
    /// <see cref="InspectorContextMenu"/> rather than the platform menu.
    /// </summary>
    public sealed class InspectorContextMenuManipulator : Manipulator
    {
        /// <summary>Marks an element that owns a LightSide context menu.</summary>
        public const string OwnerUssClassName = "lightside-context-menu-owner";

        private readonly Action<DropdownMenu> populate;

        /// <summary>Creates a manipulator whose callback fills the menu each time it opens.</summary>
        public InspectorContextMenuManipulator(Action<DropdownMenu> populate)
            => this.populate = populate ?? throw new ArgumentNullException(nameof(populate));

        /// <summary>Secondary click opens on press on macOS and on release elsewhere, matching each platform's menu convention.</summary>
        private static bool OpensOnPress => Application.platform == RuntimePlatform.OSXEditor;

        protected override void RegisterCallbacksOnTarget()
        {
            target.AddToClassList(OwnerUssClassName);
            target.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.RemoveFromClassList(OwnerUssClassName);
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            target.UnregisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Whether an owner nested between the clicked element and this one should answer instead.
        /// Ownership is resolved on the way down, so a Unity control's own menu on an inner element
        /// never wins over the LightSide field containing it.
        /// </summary>
        private bool NestedOwnerClaims(EventBase evt)
        {
            for (var element = evt.target as VisualElement;
                 element != null && element != target;
                 element = element.hierarchy.parent)
                if (element.ClassListContains(OwnerUssClassName)) return true;
            return false;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!IsSecondaryClick(evt) || NestedOwnerClaims(evt)) return;
            if (OpensOnPress) Display(evt, evt.position);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!IsSecondaryClick(evt) || NestedOwnerClaims(evt)) return;
            if (!OpensOnPress) Display(evt, evt.position);
            evt.StopPropagation();
        }

        private void OnKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode != KeyCode.Menu || NestedOwnerClaims(evt)) return;
            var bounds = target.worldBound;
            Display(evt, new Vector2(bounds.xMin, bounds.yMax));
            evt.StopPropagation();
        }

        private static bool IsSecondaryClick(IPointerEvent evt)
            => evt.button == 1 || (OpensOnPress && evt.button == 0 && evt.ctrlKey);

        private void Display(EventBase triggerEvent, Vector2 position)
        {
            var menu = new DropdownMenu();
            populate(menu);
            InspectorContextMenu.Show(new Rect(position, Vector2.zero), menu, triggerEvent);
        }
    }
}
