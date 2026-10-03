using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Runtime resolution of the host platform's keyboard convention — the single authority every
    /// shortcut consumer (platform key map, formatting commands, custom behaviors) reads instead of
    /// compile-time platform symbols. Detected once per session: macOS player/editor, iOS/iPadOS
    /// (hardware keyboards send Cmd), and WebGL running in a browser on an Apple OS all get Command
    /// semantics; everything else gets Control.
    /// </summary>
    public static class PlatformKeySemantics
    {
        private static int cached;

        /// <summary>Whether the primary shortcut modifier on this host is Command rather than Control.</summary>
        public static bool PrimaryModifierIsCommand
        {
            get
            {
                if (cached == 0) cached = Detect() ? 1 : 2;
                return cached == 1;
            }
        }

        /// <summary>The primary shortcut modifier bit for this host: Cmd on Apple platforms, Ctrl elsewhere.</summary>
        public static NativeModifiers PrimaryModifier
            => PrimaryModifierIsCommand ? NativeModifiers.Cmd : NativeModifiers.Ctrl;

        private static bool Detect()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.IPhonePlayer:
                    return true;
                case RuntimePlatform.WebGLPlayer:
#if UNITY_WEBGL && !UNITY_EDITOR
                    return NativeInputWebGL.IsApplePlatform;
#else
                    var os = SystemInfo.operatingSystem ?? string.Empty;
                    return os.IndexOf("Mac", StringComparison.OrdinalIgnoreCase) >= 0
                           || os.IndexOf("iPhone", StringComparison.OrdinalIgnoreCase) >= 0
                           || os.IndexOf("iPad", StringComparison.OrdinalIgnoreCase) >= 0
                           || os.IndexOf("iOS", StringComparison.OrdinalIgnoreCase) >= 0;
#endif
                default:
                    return false;
            }
        }
    }
}
