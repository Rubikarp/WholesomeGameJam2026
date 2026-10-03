using Alchemy.Inspector;
using UnityEngine.UI;
using UnityEngine;

public class ToggleImage : MonoBehaviour
{
    [Header("Setting")]
    [SerializeField] private bool _isOn = true;
    public virtual bool IsOn
    {
        get => _isOn;
        private set
        {
            _isOn = value;

            if (Image != null) Image.sprite = value ? visualOn : visualOff;
            if (Image != null) Image.color = value ? colorOn : colorOff;
            if (Image != null) Image.raycastTarget = !value;
        }
    }

    [Header("Visuals")]
    [SerializeField] private Sprite visualOn;
    [SerializeField] private Color colorOn = Color.white;
    [Space]
    [SerializeField] private Sprite visualOff;
    [SerializeField] private Color colorOff = Color.white;

    [Header("Components")]
    [field: SerializeField, Required] public Image Image { get; private set; }

    private void OnValidate()
    {
        IsOn = _isOn;
    }

    [Button] public void Toogle() => IsOn = !_isOn;
    public void SetState(bool state) => IsOn = state;
}
