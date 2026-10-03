using UnityEditor;

namespace LightSide
{
    /// <summary>Routes unit values through the shared serialized UI Toolkit renderer.</summary>
    [CustomPropertyDrawer(typeof(UnitValue))]
    [CustomPropertyDrawer(typeof(UnitVector2))]
    internal sealed class UnitValueDrawer : LightSidePropertyBridge
    {
    }
}
