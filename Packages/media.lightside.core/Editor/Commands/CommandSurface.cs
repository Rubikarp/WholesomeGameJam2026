namespace LightSide
{
    /// <summary>
    /// The editor surface a command palette was opened from. A provider gates its commands on this
    /// so a palette only ever offers what the surface can carry out.
    /// </summary>
    public enum CommandSurface
    {
        /// <summary>Opened by shortcut, claimed by no surface; a package offers everything it has.</summary>
        Global,

        /// <summary>Opened over the Hierarchy window.</summary>
        Hierarchy,

        /// <summary>Opened over the Project window.</summary>
        Project,

        /// <summary>Opened over a Scene view.</summary>
        SceneView,
    }
}
