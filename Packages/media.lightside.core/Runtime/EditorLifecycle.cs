#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>Editor Play Mode entry and ordered assembly-reload teardown phases.</summary>
    [InitializeOnLoad]
    public static class EditorLifecycle
    {
        /// <summary>Occurs at subsystem registration before a Play Mode scene starts.</summary>
        public static event Action PlayModeEntering;

        /// <summary>Stops managed work before an assembly reload.</summary>
        public static event Action ManagedCleaning;

        /// <summary>Runs after every managed assembly-reload cleanup handler.</summary>
        public static event Action ManagedCleaned;

        /// <summary>Disposes resource owners while their native libraries are still available.</summary>
        public static event Action UnmanagedCleaning;

        /// <summary>Runs after every resource owner has been disposed.</summary>
        public static event Action UnmanagedCleaned;

        /// <summary>Shuts down low-level libraries after all dependent resources are gone.</summary>
        public static event Action LibraryShutdown;

        internal static event Action ReloadCleanupCompleted;

        /// <summary>Whether the current domain is tearing down for an assembly reload.</summary>
        public static bool IsReloading { get; private set; }

        static EditorLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void OnPlayModeEntering() => PlayModeEntering.SafeInvoke();

        private static void OnBeforeAssemblyReload()
        {
            if (IsReloading) return;
            IsReloading = true;

            ManagedCleaning.SafeInvoke();
            ManagedCleaned.SafeInvoke();

            UnmanagedCleaning.SafeInvoke();
            UnmanagedCleaned.SafeInvoke();

            LibraryShutdown.SafeInvoke();
            ReloadCleanupCompleted.SafeInvoke();
        }
    }
}
#endif
