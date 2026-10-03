using UnityEditor;

namespace LightSide
{
    internal static class ModifierGraphDuplication
    {
        [InitializeOnLoadMethod]
        private static void Register()
            => PropertyClipboard.RegisterDuplicateFinalizer<BaseModifier>(
                BaseModifier.RegenerateGraphIdentities);
    }
}
