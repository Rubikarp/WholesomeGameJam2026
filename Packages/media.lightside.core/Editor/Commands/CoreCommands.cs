namespace LightSide
{
    /// <summary>
    /// Core's own palette entries. They are project-wide tools rather than commands acting on a
    /// selection, so they appear only in the shortcut's palette, where a package offers everything.
    /// </summary>
    internal sealed class CoreCommands : ILightSideCommands
    {
        public void Populate(CommandMenu menu, CommandContext context)
        {
            if (context.Surface != CommandSurface.Global) return;

            menu.GroupIcon = "tune";
            menu.Add("Log Zones", LogZonesWindow.ShowWindow, "list");
            menu.Add("Noise Generator", NoiseGeneratorWindow.Open, "gradient");
            menu.Add("Package Patcher", PackagePatcherWindow.ShowWindow, "utility");
        }
    }
}
