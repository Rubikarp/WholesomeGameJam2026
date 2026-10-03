using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class NativePluginRestartPrompt
    {
        const string CorePackageName = "media.lightside.core";

        static NativePluginRestartPrompt()
        {
            Events.registeredPackages -= OnPackagesRegistered;
            Events.registeredPackages += OnPackagesRegistered;
        }

        static void OnPackagesRegistered(PackageRegistrationEventArgs changes)
        {
            if (Application.isBatchMode) return;

            var change = DescribeCoreRegistration(changes);
            if (change == null) return;

            EditorApplication.delayCall += () => EditorUtility.DisplayDialog(
                "LightSide Core: Restart Required",
                $"LightSide Core {change}.\n\n" +
                "The Editor process cannot load the package's native GPU plugin until it exits. " +
                "Restart the Unity Editor before continuing, otherwise rendering fails with " +
                "\"GPU delivery is unavailable\".",
                "OK");
        }

        static string DescribeCoreRegistration(PackageRegistrationEventArgs changes)
        {
            for (int i = 0; i < changes.changedTo.Count; i++)
                if (changes.changedTo[i].name == CorePackageName)
                    return $"was updated from {changes.changedFrom[i].version} to {changes.changedTo[i].version}";

            for (int i = 0; i < changes.added.Count; i++)
                if (changes.added[i].name == CorePackageName)
                    return $"{changes.added[i].version} was installed";

            return null;
        }
    }
}
