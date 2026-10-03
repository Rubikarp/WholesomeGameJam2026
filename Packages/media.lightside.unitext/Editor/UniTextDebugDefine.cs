using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Registers UniText's debug define with the shared master switch, so one toggle compiles
    /// logging in or out for Core and UniText together. A project where UNITEXT_DEBUG is set but the
    /// sibling defines are not (state left by an older version) is re-synced on, keeping logging alive.
    /// </summary>
    [InitializeOnLoad]
    internal static class UniTextDebugDefine
    {
        internal const string Define = "UNITEXT_DEBUG";

        static UniTextDebugDefine()
        {
            ScriptingDefines.RegisterDebugDefine(Define);
            if (ScriptingDefines.IsDefined(Define) && !ScriptingDefines.DebugDefinesOn)
                ScriptingDefines.SetDebugDefines(true);
        }
    }
}
