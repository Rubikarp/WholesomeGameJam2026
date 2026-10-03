using UnityEngine;
using UnityEngine.UI;

namespace LightSide.Samples
{
    /// <summary>
    /// Show-password eye toggle: drives <see cref="PasswordBehavior.Revealed"/> on the field's
    /// behavior from a UI <see cref="Toggle"/>.
    /// </summary>
    public class PasswordVisibility : MonoBehaviour
    {
        public UniTextEditable editable;
        public Toggle toggle;

        private void Awake()
        {
            toggle.onValueChanged.AddListener(OnToggle);
            OnToggle(toggle.isOn);
        }

        private void OnDestroy()
        {
            if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggle);
        }

        private void OnToggle(bool active)
        {
            toggle.targetGraphic.enabled = !active;
            var password = editable != null ? editable.GetBehavior<PasswordBehavior>() : null;
            if (password != null) password.Revealed = active;
        }
    }
}
