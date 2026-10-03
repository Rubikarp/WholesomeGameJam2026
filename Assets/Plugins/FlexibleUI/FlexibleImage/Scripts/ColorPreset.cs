using System;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[CreateAssetMenu(menuName = "FlexibleUI/ColorPreset")]
public class ColorPreset : ScriptableObject
{
#if UNITY_EDITOR
    public static readonly string PrimaryColorFieldName = nameof(_primaryColor);
    public static readonly string OutlineColorFieldName = nameof(_outlineColor);
    public static readonly string ProceduralGradientColorFieldName = nameof(_proceduralGradientColor);
    public static readonly string PatternColorFieldName = nameof(_patternColor);
#endif

    public event Action ColorChangeEvent;

    [SerializeField] private Color _primaryColor = Color.white;
    public Color PrimaryColor 
    { 
        get => _primaryColor;
        set
        {
            _primaryColor = value;
            ColorChangeEvent?.Invoke();
        }
    }

    [SerializeField] private Color _outlineColor = Color.black;
    public Color OutlineColor 
    { 
        get => _outlineColor;
        set
        {
            _outlineColor = value;
            ColorChangeEvent?.Invoke();
        }
    }

    [SerializeField] private Color _proceduralGradientColor = Color.black;
    public Color ProceduralGradientColor 
    { 
        get => _proceduralGradientColor;
        set
        {
            _proceduralGradientColor = value;
            ColorChangeEvent?.Invoke();
        }
    }

    [SerializeField] private Color _patternColor = Color.black;
    public Color PatternColor 
    { 
        get => _patternColor;
        set
        {
            _patternColor = value;
            ColorChangeEvent?.Invoke();
        }
    }

    public void CopyFrom(ColorPreset preset)
    {
        (_primaryColor, _outlineColor, _proceduralGradientColor, _patternColor) = (preset.PrimaryColor, preset.OutlineColor, preset.ProceduralGradientColor, preset.PatternColor);
        ColorChangeEvent?.Invoke();
    }

    public void ForceInvokeColorChangeEvent() => ColorChangeEvent?.Invoke();
}
}
