#if !UNITEXT
using UnityEditor;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class UniTextDefineSetup
    {
        private const string Define = "UNITEXT";

        static UniTextDefineSetup()
        {
            ScriptingDefines.SetDefined(Define, true);
        }
    }
}
#endif
