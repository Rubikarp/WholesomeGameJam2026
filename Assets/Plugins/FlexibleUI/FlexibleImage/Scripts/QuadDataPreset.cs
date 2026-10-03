using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[ExecuteAlways]
[CreateAssetMenu(menuName = "FlexibleUI/QuadDataPreset")]
public class QuadDataPreset : ScriptableObject
{
    public QuadDataContainer quadDataContainer = new();
}
}

