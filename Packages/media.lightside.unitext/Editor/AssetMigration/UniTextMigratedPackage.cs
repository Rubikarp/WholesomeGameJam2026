namespace LightSide
{
    internal sealed class UniTextMigratedPackage : IMigratedPackage
    {
        public string PackageName => "media.lightside.unitext";
        public string Version => PackageVersion.Resolve(typeof(UniText).Assembly, "LightSide.UniText.asmdef");
    }
}
