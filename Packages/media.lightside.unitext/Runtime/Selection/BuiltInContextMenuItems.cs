using System;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Binds a button to one standard <see cref="ContextMenuAction"/>, raised through
    /// <see cref="UniTextContextMenu.RequestAction"/>, which routes to the presenter recorded by the
    /// last <c>Show</c> — the editable / selectable that opened the menu executes it, so paste can
    /// route through the platform-correct pipeline.
    /// </summary>
    [Serializable]
    public abstract class CommandContextMenuItem : ButtonContextMenuItem
    {
        protected abstract ContextMenuAction Action { get; }

        protected sealed override void Execute(UniTextContextMenu menu) => menu.RequestAction(Action);
    }

    /// <summary>Copies the selection. Shown when <see cref="ContextMenuCapabilities.CanCopy"/>.</summary>
    [Serializable]
    public sealed class CopyContextMenuItem : CommandContextMenuItem
    {
        public override bool IsApplicable(in ContextMenuCapabilities capabilities) => capabilities.CanCopy;
        protected override ContextMenuAction Action => ContextMenuAction.Copy;
    }

    /// <summary>Cuts the selection. Shown when <see cref="ContextMenuCapabilities.CanCut"/>.</summary>
    [Serializable]
    public sealed class CutContextMenuItem : CommandContextMenuItem
    {
        public override bool IsApplicable(in ContextMenuCapabilities capabilities) => capabilities.CanCut;
        protected override ContextMenuAction Action => ContextMenuAction.Cut;
    }

    /// <summary>Pastes clipboard content. Shown when <see cref="ContextMenuCapabilities.CanPaste"/>.</summary>
    [Serializable]
    public sealed class PasteContextMenuItem : CommandContextMenuItem
    {
        public override bool IsApplicable(in ContextMenuCapabilities capabilities) => capabilities.CanPaste;
        protected override ContextMenuAction Action => ContextMenuAction.Paste;
    }

    /// <summary>Selects all text. Shown when <see cref="ContextMenuCapabilities.CanSelectAll"/>.</summary>
    [Serializable]
    public sealed class SelectAllContextMenuItem : CommandContextMenuItem
    {
        public override bool IsApplicable(in ContextMenuCapabilities capabilities) => capabilities.CanSelectAll;
        protected override ContextMenuAction Action => ContextMenuAction.SelectAll;
    }

    /// <summary>
    /// Binds a button to a runtime callback. Set <see cref="onlyWithSelection"/> to hide it on a
    /// bare caret.
    /// </summary>
    [Serializable]
    public sealed class ActionContextMenuItem : ButtonContextMenuItem
    {
        [SerializeField, StatePassive] private bool onlyWithSelection;

        /// <summary>Occurs when this item is activated while its menu is visible.</summary>
        public event Action Invoked;

        public override bool IsApplicable(in ContextMenuCapabilities capabilities)
            => !onlyWithSelection || capabilities.HasSelection;

        protected override void Execute(UniTextContextMenu menu) => Invoked?.Invoke();
    }

    /// <summary>Binds a <see cref="Toggle"/> to a custom on/off action.</summary>
    [Serializable]
    public sealed partial class ToggleContextMenuItem : ContextMenuItem
    {
        /// <summary>Toggle that invokes this menu item.</summary>
        [SerializeField, StateProperty(nameof(ApplyToggleChange))]
        [Tooltip("The toggle in your menu hierarchy this binds to.")]
        private Toggle toggle;

        [SerializeField, StatePassive] private bool onlyWithSelection;
        [NonSerialized] private UniTextContextMenu wiredMenu;

        /// <summary>Occurs when the bound toggle changes while its menu is visible.</summary>
        public event Action<bool> ValueChanged;

        public override bool IsApplicable(in ContextMenuCapabilities capabilities)
            => !onlyWithSelection || capabilities.HasSelection;

        public override void Wire(UniTextContextMenu menu)
        {
            Unwire();
            wiredMenu = menu ?? throw new ArgumentNullException(nameof(menu));
            if (toggle != null) toggle.onValueChanged.AddListener(OnValueChanged);
        }

        internal override void Unwire()
        {
            if (toggle != null) toggle.onValueChanged.RemoveListener(OnValueChanged);
            wiredMenu = null;
        }

        private void ApplyToggleChange(Toggle previous, Toggle current)
        {
            if (wiredMenu == null) return;
            if (previous != null) previous.onValueChanged.RemoveListener(OnValueChanged);
            if (current != null) current.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnValueChanged(bool value)
        {
            if (wiredMenu != null && wiredMenu.IsVisible) ValueChanged?.Invoke(value);
        }

        public override void SetVisible(bool visible)
        {
            if (toggle != null) toggle.gameObject.SetActive(visible);
        }

        public override bool HasControl => toggle != null;
    }
}
