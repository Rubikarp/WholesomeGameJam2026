using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;

namespace LightSide
{
    /// <summary>
    /// Reads and toggles scripting define symbols. Reads report the ACTIVE build target group (what is
    /// compiled right now); writes apply to EVERY valid group so an editor-managed define does not
    /// silently vanish when the user switches build targets.
    /// </summary>
    public static class ScriptingDefines
    {
        private static readonly List<string> debugDefines = new List<string> { "LIGHTSIDE_DEBUG" };

        /// <summary>Debug defines the master switch toggles together. Core registers LIGHTSIDE_DEBUG; packages append their own via <see cref="RegisterDebugDefine"/>.</summary>
        public static IReadOnlyList<string> DebugDefines => debugDefines;

        /// <summary>Adds a package's debug define to the master switch. Call from an [InitializeOnLoad] static constructor so the define is registered before any toggle UI draws.</summary>
        public static void RegisterDebugDefine(string symbol)
        {
            if (!debugDefines.Contains(symbol)) debugDefines.Add(symbol);
        }

        /// <summary>True when every registered debug define is set on the active build target group.</summary>
        public static bool DebugDefinesOn
        {
            get
            {
                foreach (var symbol in debugDefines)
                    if (!IsDefined(symbol)) return false;
                return true;
            }
        }

        /// <summary>Adds or removes every registered debug define on every valid build target group; a real change triggers a recompile.</summary>
        public static void SetDebugDefines(bool value)
        {
            foreach (var symbol in debugDefines) SetDefined(symbol, value);
        }

        /// <summary>True when the registered debug defines disagree — with each other or between build target groups (state left by an older version). One <see cref="SetDebugDefines"/> re-syncs everything.</summary>
        public static bool HasDebugMismatch
        {
            get
            {
                var on = DebugDefinesOn;
                foreach (var symbol in debugDefines)
                    if (IsDefined(symbol) != on || HasGroupMismatch(symbol)) return true;
                return false;
            }
        }

        /// <summary>True when <paramref name="symbol"/> is set on the active build target group.</summary>
        public static bool IsDefined(string symbol)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            return group != BuildTargetGroup.Unknown && IsDefinedOn(group, symbol);
        }

        /// <summary>True when some other valid group disagrees with the active group about <paramref name="symbol"/> — legacy per-group state; one <see cref="SetDefined"/> re-syncs all groups.</summary>
        public static bool HasGroupMismatch(string symbol)
        {
            var active = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (active == BuildTargetGroup.Unknown) return false;

            var activeState = IsDefinedOn(active, symbol);
            foreach (var group in ValidGroups())
                if (IsDefinedOn(group, symbol) != activeState)
                    return true;
            return false;
        }

        /// <summary>Adds or removes <paramref name="symbol"/> on every valid build target group; a real change triggers a recompile. No-op when every group is already in the requested state.</summary>
        public static void SetDefined(string symbol, bool value)
        {
            foreach (var group in ValidGroups())
            {
                var set = new HashSet<string>(Get(group).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                var changed = value ? set.Add(symbol) : set.Remove(symbol);
                if (changed)
                    Set(group, string.Join(";", set));
            }
        }

        private static bool IsDefinedOn(BuildTargetGroup group, string symbol)
        {
            foreach (var d in Get(group).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (d == symbol) return true;
            return false;
        }

        private static List<BuildTargetGroup> validGroups;

        /// <summary>Non-obsolete groups the defines API accepts — probed once, because reserved enum entries make <c>NamedBuildTarget.FromBuildTargetGroup</c> throw.</summary>
        private static List<BuildTargetGroup> ValidGroups()
        {
            if (validGroups != null) return validGroups;

            validGroups = new List<BuildTargetGroup>();
            foreach (var field in typeof(BuildTargetGroup).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsDefined(typeof(ObsoleteAttribute), false)) continue;
                var group = (BuildTargetGroup)field.GetValue(null);
                if (group == BuildTargetGroup.Unknown) continue;
                try { Get(group); } catch { continue; }
                validGroups.Add(group);
            }
            return validGroups;
        }

        private static string Get(BuildTargetGroup group)
        {
#if UNITY_2023_1_OR_NEWER
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group));
#else
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
        }

        private static void Set(BuildTargetGroup group, string defines)
        {
#if UNITY_2023_1_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(group), defines);
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, defines);
#endif
        }
    }
}
