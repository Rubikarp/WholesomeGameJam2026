using System;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// One entry of a <see cref="UniTextContextMenu"/>: the binding between a control you built in the
    /// scene (a Button, Toggle, …) and an action. The menu wires the control's event, and shows or hides
    /// the control per <see cref="IsApplicable"/>. Items carry no visuals — the control's look, layout,
    /// label, and icon are entirely yours. An item whose control is not assigned never counts as
    /// applicable, so an unwired menu cannot open as an invisible input-blocking panel.
    /// </summary>
    [Serializable]
    [StateHierarchy]
    public abstract class ContextMenuItem
    {
        /// <summary>Whether the item applies to the presented state. Non-applicable controls are hidden.</summary>
        public abstract bool IsApplicable(in ContextMenuCapabilities capabilities);

        /// <summary>Hooks the bound control's event to this item's action. Called once by the menu.</summary>
        public abstract void Wire(UniTextContextMenu menu);

        /// <summary>Shows or hides the bound control for the current request.</summary>
        public abstract void SetVisible(bool visible);

        /// <summary>Whether a scene control is assigned. Unassigned items are treated as not applicable.</summary>
        public abstract bool HasControl { get; }

        internal virtual void Unwire() { }
    }

    /// <summary>
    /// Binds an existing <see cref="Button"/> to an action run through the presenting menu. Subclasses
    /// supply the action and when it applies; the button itself — label, icon, style — is yours.
    /// </summary>
    [Serializable]
    public abstract partial class ButtonContextMenuItem : ContextMenuItem
    {
        /// <summary>Button that invokes this menu item.</summary>
        [SerializeField, StateProperty(nameof(ApplyButtonChange))]
        [Tooltip("The button in your menu hierarchy this command runs from.")]
        private Button button;
        [NonSerialized] private UniTextContextMenu wiredMenu;

        protected abstract void Execute(UniTextContextMenu menu);

        public override void Wire(UniTextContextMenu menu)
        {
            Unwire();
            wiredMenu = menu ?? throw new ArgumentNullException(nameof(menu));
            if (button != null) button.onClick.AddListener(OnClicked);
        }

        internal override void Unwire()
        {
            if (button != null) button.onClick.RemoveListener(OnClicked);
            wiredMenu = null;
        }

        private void ApplyButtonChange(Button previous, Button current)
        {
            if (wiredMenu == null) return;
            if (previous != null) previous.onClick.RemoveListener(OnClicked);
            if (current != null) current.onClick.AddListener(OnClicked);
        }

        private void OnClicked()
        {
            if (wiredMenu == null || !wiredMenu.IsVisible) return;
            Execute(wiredMenu);
            wiredMenu.Hide();
        }

        public override void SetVisible(bool visible)
        {
            if (button != null) button.gameObject.SetActive(visible);
        }

        public override bool HasControl => button != null;
    }
}
