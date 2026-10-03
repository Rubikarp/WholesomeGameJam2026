using System;
using System.Collections.Generic;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// A logging zone handle: the same Meow* surface as <see cref="Cat"/>, but informational and warning
    /// output is dropped when the zone is disabled (see the Log Zones editor window). Errors always pass.
    /// Obtain handles from <see cref="Cat.Zone"/>; every call compiles out without LIGHTSIDE_DEBUG.
    /// </summary>
    public readonly struct CatZone
    {
        private readonly int index;

        internal CatZone(int index) => this.index = index;

        /// <summary>Whether this zone currently emits info/warn logs.</summary>
        public bool Enabled => CatZoneRegistry.IsEnabled(index);

        [Conditional("LIGHTSIDE_DEBUG")]
        public void Meow(object message)
        {
            if (CatZoneRegistry.IsEnabled(index)) Debug.Log(message);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void Meow(object message, Object context)
        {
            if (CatZoneRegistry.IsEnabled(index)) Debug.Log(message, context);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowFormat(string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(index)) Debug.LogFormat(format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowOnce(string key, string format, params object[] args)
        {
            if (!CatZoneRegistry.IsEnabled(index)) return;
            var text = string.Format(format, args);
            if (Cat.OnceShouldLog(key, text)) Debug.Log(text);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowWarn(object message)
        {
            if (CatZoneRegistry.IsEnabled(index)) Debug.LogWarning(message);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowWarnFormat(string format, params object[] args)
        {
            if (CatZoneRegistry.IsEnabled(index)) Debug.LogWarningFormat(format, args);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowWarnOnce(string key, string format, params object[] args)
        {
            if (!CatZoneRegistry.IsEnabled(index)) return;
            var text = string.Format(format, args);
            if (Cat.OnceShouldLog(key, text)) Debug.LogWarning(text);
        }

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowError(object message) => Debug.LogError(message);

        [Conditional("LIGHTSIDE_DEBUG")]
        public void MeowErrorFormat(string format, params object[] args) => Debug.LogErrorFormat(format, args);
    }

    /// <summary>
    /// Registry of logging zones and their enabled state. Registration is thread-safe (zone handles are
    /// built from worker-thread static initializers); the enabled state persists per zone in EditorPrefs
    /// and is edited through the Log Zones window. A zone defaults to enabled the first time it is seen.
    /// </summary>
    public static class CatZoneRegistry
    {
        private static readonly object sync = new();
        private static readonly Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> saved = new(StringComparer.OrdinalIgnoreCase);
        private static string[] names = Array.Empty<string>();
        private static bool[] enabled = Array.Empty<bool>();

        /// <summary>Name of the fallback zone the plain <see cref="Cat"/> info/warn calls route through.</summary>
        public const string GeneralName = "General";

        /// <summary>Index of the fallback zone the plain <see cref="Cat"/> info/warn calls route through.</summary>
        internal static readonly int generalIndex = Register(GeneralName);

        /// <summary>Returns the zone's index, registering it (enabled, unless a saved state says otherwise) on first sight.</summary>
        public static int Register(string name)
        {
            if (string.IsNullOrEmpty(name)) name = GeneralName;
            lock (sync)
            {
                if (map.TryGetValue(name, out var i)) return i;

                i = names.Length;
                Array.Resize(ref names, i + 1);
                Array.Resize(ref enabled, i + 1);
                names[i] = name;
                enabled[i] = !saved.TryGetValue(name, out var on) || on;
                saved[name] = enabled[i];
                map[name] = i;
                return i;
            }
        }

        /// <summary>Global mute across every zone without touching per-zone states. The measurement switch: zones default to enabled in players, so benchmarks set this to keep log formatting out of timed frames.</summary>
        public static bool MuteAll;

        /// <summary>True when the zone at <paramref name="index"/> emits info/warn logs and <see cref="MuteAll"/> is off.</summary>
        public static bool IsEnabled(int index)
        {
            if (MuteAll) return false;
            var arr = enabled;
            return (uint)index < (uint)arr.Length && arr[index];
        }

        /// <summary>True when the named zone emits info/warn logs, registering it on first sight.</summary>
        public static bool IsEnabled(string name) => IsEnabled(Register(name));

        /// <summary>Enables or disables the named zone and persists the state.</summary>
        public static void SetEnabled(string name, bool value)
        {
            var i = Register(name);
            lock (sync) { enabled[i] = value; saved[name] = value; }
            Persist(name, value);
        }

        /// <summary>All zones seen this session or remembered from a previous one — the set the window lists.</summary>
        public static string[] KnownNames()
        {
            lock (sync)
            {
                var arr = new string[saved.Count];
                saved.Keys.CopyTo(arr, 0);
                return arr;
            }
        }

#if UNITY_EDITOR
        private const string PrefPrefix = "LightSide.CatZone.";
        private const string KnownKey = "LightSide.CatZones.Known";

        [UnityEditor.InitializeOnLoadMethod]
        private static void LoadFromPrefs()
        {
            var known = UnityEditor.EditorPrefs.GetString(KnownKey, "");
            if (known.Length == 0) return;

            foreach (var n in known.Split('|'))
            {
                if (n.Length == 0) continue;
                var on = UnityEditor.EditorPrefs.GetBool(PrefPrefix + n, true);
                lock (sync)
                {
                    saved[n] = on;
                    if (map.TryGetValue(n, out var i)) enabled[i] = on;
                }
            }
        }

        private static void Persist(string name, bool value)
        {
            UnityEditor.EditorPrefs.SetBool(PrefPrefix + name, value);
            lock (sync)
                UnityEditor.EditorPrefs.SetString(KnownKey, string.Join("|", saved.Keys));
        }
#else
        private static void Persist(string name, bool value) { }
#endif
    }
}
