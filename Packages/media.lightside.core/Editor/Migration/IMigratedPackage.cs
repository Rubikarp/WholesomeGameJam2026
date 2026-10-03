namespace LightSide
{
    /// <summary>
    /// Registers a package with the auto-migration trigger. Discovered by <c>TypeCache</c> the same way as
    /// <see cref="IMigration"/> — non-abstract, public parameterless constructor. The trigger compares each
    /// package's <see cref="Version"/> against its ledger stamp and runs all migrations when any differs.
    /// </summary>
    public interface IMigratedPackage
    {
        /// <summary>Package name the ledger stamp is keyed by, e.g. <c>media.lightside.core</c>.</summary>
        string PackageName { get; }

        /// <summary>Installed version (see <see cref="PackageVersion.Resolve"/>); null or empty skips the package.</summary>
        string Version { get; }
    }

    internal sealed class CoreMigratedPackage : IMigratedPackage
    {
        public string PackageName => "media.lightside.core";
        public string Version => PackageVersion.Resolve(typeof(CoreMigratedPackage).Assembly, "LightSide.Core.asmdef");
    }
}
