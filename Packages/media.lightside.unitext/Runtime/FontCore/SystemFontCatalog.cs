using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Catalog of fonts considered guaranteed-present per platform, plus logical
    /// cross-platform families for the Common tab. Drives <see cref="UniTextSystemFont"/>.
    /// </summary>
    /// <remarks>
    /// Entries name families or platform roles, never files: every platform resolves them through
    /// its own font backend, which owns where the bytes live. A family entry is rejected when the
    /// platform substitutes a different family; a role entry accepts whatever the OS binds to it.
    /// When nothing resolves, the runtime walks the remaining entries and then asks the platform
    /// for its default face, warning to the console.
    /// </remarks>
    internal static class SystemFontCatalog
    {
        /// <summary>Single font selectable from a platform tab.</summary>
        public struct Entry
        {
            public string displayName;
            /// <summary>OS family name to request, when it differs from <see cref="displayName"/>.</summary>
            public string familyName;
            /// <summary>The requested name is a platform role rather than a face identity, so whatever family the OS binds to it is the correct answer.</summary>
            public bool isRole;
        }

        /// <summary>Cross-platform logical family selectable from the Common tab.</summary>
        public struct CommonEntry
        {
            public string displayName;
            public string windowsName;
            public string macOSName;
            public string linuxName;
            public string iOSName;
            public string androidName;
        }

        public const string CommonSansSerif = "System Sans-Serif";
        public const string CommonSerif = "System Serif";
        public const string CommonMonospace = "System Monospace";

        /// <summary>CoreText family of the Apple system UI font. It is a virtual font: no descriptor search matches it, and only the UI-font API creates it.</summary>
        public const string AppleSystemUIFamily = ".AppleSystemUIFont";

        public static readonly CommonEntry[] Common =
        {
            new() {
                displayName = CommonSansSerif,
                windowsName = "Segoe UI",
                macOSName = "Helvetica Neue",
                linuxName = "DejaVu Sans",
                iOSName = "Helvetica Neue",
                androidName = "Roboto",
            },
            new() {
                displayName = CommonSerif,
                windowsName = "Times New Roman",
                macOSName = "Times",
                linuxName = "DejaVu Serif",
                iOSName = "Times New Roman",
                androidName = "Noto Serif",
            },
            new() {
                displayName = CommonMonospace,
                windowsName = "Consolas",
                macOSName = "Menlo",
                linuxName = "DejaVu Sans Mono",
                iOSName = "Menlo",
                androidName = "Droid Sans Mono",
            },
        };

        public static readonly Entry[] Windows =
        {
            Family("Segoe UI"),
            Family("Arial"),
            Family("Tahoma"),
            Family("Verdana"),
            Family("Calibri"),
            Family("Cambria"),
            Family("Candara"),
            Family("Consolas"),
            Family("Constantia"),
            Family("Corbel"),
            Family("Courier New"),
            Family("Georgia"),
            Family("Impact"),
            Family("Lucida Console"),
            Family("Lucida Sans Unicode"),
            Family("Microsoft Sans Serif"),
            Family("Palatino Linotype"),
            Family("Times New Roman"),
            Family("Trebuchet MS"),
            Family("Comic Sans MS"),
        };

        public static readonly Entry[] MacOS =
        {
            Family("Helvetica Neue"),
            Family("Helvetica"),
            Family("San Francisco",      AppleSystemUIFamily),
            Family("Arial"),
            Family("Times"),
            Family("Times New Roman"),
            Family("Courier"),
            Family("Menlo"),
            Family("Monaco"),
            Family("Geneva"),
            Family("Georgia"),
            Family("Verdana"),
            Family("Optima"),
            Family("Palatino"),
            Family("Lucida Grande"),
        };

        public static readonly Entry[] Linux =
        {
            Family("DejaVu Sans"),
            Family("DejaVu Serif"),
            Family("DejaVu Sans Mono"),
            Family("Liberation Sans"),
            Family("Liberation Serif"),
            Family("Liberation Mono"),
            Family("Noto Sans"),
            Family("Noto Serif"),
            Family("Noto Mono"),
            Family("FreeSans"),
            Family("FreeSerif"),
            Family("FreeMono"),
        };

        public static readonly Entry[] iOS =
        {
            Family("Helvetica Neue"),
            Family("Helvetica"),
            Family("San Francisco",      AppleSystemUIFamily),
            Family("Arial"),
            Family("Times New Roman"),
            Family("Courier"),
            Family("Menlo"),
            Family("Geneva"),
            Family("Georgia"),
            Family("Verdana"),
        };

        public static readonly Entry[] Android =
        {
            Role("Roboto",             "sans-serif"),
            Role("Roboto Mono",        "monospace"),
            Role("Roboto Condensed",   "sans-serif-condensed"),
            Role("Noto Sans",          "sans-serif"),
            Role("Noto Serif",         "serif"),
            Role("Droid Sans",         "sans-serif"),
            Role("Droid Sans Mono",    "monospace"),
            Role("Droid Serif",        "serif"),
        };

        private static Entry Family(string displayName, string familyName = null)
            => new() { displayName = displayName, familyName = familyName };

        private static Entry Role(string displayName, string roleName)
            => new() { displayName = displayName, familyName = roleName, isRole = true };

        /// <summary>OS font family name a Common logical font maps to on <paramref name="platform"/>, or null if unmapped.</summary>
        public static string CommonFamilyName(string commonName, UniTextSystemFont.FontPlatform platform)
        {
            for (int i = 0; i < Common.Length; i++)
            {
                if (Common[i].displayName != commonName) continue;
                return platform switch
                {
                    UniTextSystemFont.FontPlatform.Windows => Common[i].windowsName,
                    UniTextSystemFont.FontPlatform.MacOS   => Common[i].macOSName,
                    UniTextSystemFont.FontPlatform.Linux   => Common[i].linuxName,
                    UniTextSystemFont.FontPlatform.iOS     => Common[i].iOSName,
                    UniTextSystemFont.FontPlatform.Android => Common[i].androidName,
                    _ => null,
                };
            }
            return null;
        }

        /// <summary>Returns the platform-specific entry that corresponds to a Common logical name.</summary>
        public static Entry ResolveCommon(string commonName, UniTextSystemFont.FontPlatform platform)
        {
            var perPlatformName = CommonFamilyName(commonName, platform);
            return perPlatformName == null ? default : FindByName(EntriesFor(platform), perPlatformName);
        }

        public static Entry[] EntriesFor(UniTextSystemFont.FontPlatform platform) => platform switch
        {
            UniTextSystemFont.FontPlatform.Windows => Windows,
            UniTextSystemFont.FontPlatform.MacOS   => MacOS,
            UniTextSystemFont.FontPlatform.Linux   => Linux,
            UniTextSystemFont.FontPlatform.iOS     => iOS,
            UniTextSystemFont.FontPlatform.Android => Android,
            _ => Array.Empty<Entry>(),
        };

        public static Entry FindByName(Entry[] table, string name)
        {
            if (string.IsNullOrEmpty(name)) return default;
            for (int i = 0; i < table.Length; i++)
                if (table[i].displayName == name)
                    return table[i];
            return default;
        }

        /// <summary>OS family name to request from the platform for an entry.</summary>
        public static string FamilyOf(Entry entry)
            => string.IsNullOrEmpty(entry.familyName) ? entry.displayName : entry.familyName;

        /// <summary>Whether a face the platform bound to an entry satisfies it: a role entry accepts any family, a face entry only its own.</summary>
        public static bool Satisfies(Entry entry, string resolvedFamilyName)
            => entry.isRole || SystemFont.FamilyMatches(resolvedFamilyName, FamilyOf(entry));

        /// <summary>Returns the current host platform mapped to a tab id.</summary>
        public static UniTextSystemFont.FontPlatform CurrentPlatform()
        {
#if UNITY_EDITOR
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor: return UniTextSystemFont.FontPlatform.Windows;
                case RuntimePlatform.OSXEditor:     return UniTextSystemFont.FontPlatform.MacOS;
                case RuntimePlatform.LinuxEditor:   return UniTextSystemFont.FontPlatform.Linux;
            }
#endif
#if UNITY_STANDALONE_WIN
            return UniTextSystemFont.FontPlatform.Windows;
#elif UNITY_STANDALONE_OSX
            return UniTextSystemFont.FontPlatform.MacOS;
#elif UNITY_STANDALONE_LINUX
            return UniTextSystemFont.FontPlatform.Linux;
#elif UNITY_IOS
            return UniTextSystemFont.FontPlatform.iOS;
#elif UNITY_ANDROID
            return UniTextSystemFont.FontPlatform.Android;
#else
            return UniTextSystemFont.FontPlatform.Common;
#endif
        }
    }
}
