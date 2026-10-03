namespace LightSide
{
    /// <summary>
    /// Contributes a package's commands to the LightSide palette. Discovered by <c>TypeCache</c> the
    /// same way as <see cref="IMigratedPackage"/> — non-abstract, public parameterless constructor.
    /// Every provider found across one package's assemblies shares that package's group, which the
    /// palette names and orders; a provider outside any package is grouped by its assembly name.
    /// </summary>
    public interface ILightSideCommands
    {
        /// <summary>
        /// Appends the commands valid for <paramref name="context"/>. Appending nothing leaves the
        /// package out of that palette entirely. Called on the main thread, once per opening, and
        /// must not open windows or change the project by itself — that is the command's job.
        /// </summary>
        void Populate(CommandMenu menu, CommandContext context);
    }
}
