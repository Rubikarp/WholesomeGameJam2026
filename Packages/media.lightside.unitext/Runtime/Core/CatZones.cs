using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>
    /// UniText's log-zone catalog and logging identity: zones toggle per area in the Log Zones window,
    /// and the shared file logger writes to <c>Logs/unitext.log</c>. The name is claimed from the
    /// earliest load hooks, before <see cref="Cat"/> opens the file.
    /// </summary>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal static class CatZones
    {
        private const string LogFileName = "unitext";

        static CatZones() => Cat.FileBaseName = LogFileName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClaimLogFile() => Cat.FileBaseName = LogFileName;

        /// <summary>Global mute across every zone without touching per-zone states — see <see cref="CatZoneRegistry.MuteAll"/>.</summary>
        internal static bool MuteAll
        {
            get => CatZoneRegistry.MuteAll;
            set => CatZoneRegistry.MuteAll = value;
        }

        internal static readonly CatZone glyphAtlas = Cat.Zone("GlyphAtlas");
        internal static readonly CatZone meshGenerator = Cat.Zone("MeshGenerator");
        internal static readonly CatZone layout = Cat.Zone("Layout");
        internal static readonly CatZone fontProvider = Cat.Zone("FontProvider");
        internal static readonly CatZone systemFont = Cat.Zone("SystemFont");
        internal static readonly CatZone fontBackend = Cat.Zone("FontBackend");
        internal static readonly CatZone native = Cat.Zone("Native");
        internal static readonly CatZone input = Cat.Zone("Input");
        internal static readonly CatZone emoji = Cat.Zone("Emoji");
        internal static readonly CatZone material = Cat.Zone("Material");
        internal static readonly CatZone component = Cat.Zone("Component");
        internal static readonly CatZone dirty = Cat.Zone("Dirty");
        internal static readonly CatZone lifecycle = Cat.Zone("Lifecycle");
        internal static readonly CatZone raster = Cat.Zone("Raster");
        internal static readonly CatZone frameFlow = Cat.Zone("FrameFlow");
        internal static readonly CatZone unicode = Cat.Zone("Unicode");
        internal static readonly CatZone build = Cat.Zone("Build");
    }
}
