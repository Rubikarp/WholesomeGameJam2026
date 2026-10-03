namespace LightSide
{
    internal sealed class OutlineToStrokeMigration : RenameManagedType
    {
        public OutlineToStrokeMigration() : base(
            new TypeSignature("OutlineModifier", "LightSide", "LightSide.UniText"), "StrokeModifier") { }
    }

    internal sealed class GradientToFillMigration : RenameManagedType
    {
        public GradientToFillMigration() : base(
            new TypeSignature("GradientModifier", "LightSide", "LightSide.UniText"), "FillModifier") { }
    }

    internal sealed class WobbleToWaveMigration : RenameManagedType
    {
        public WobbleToWaveMigration() : base(
            new TypeSignature("WobbleAnimationModifier", "LightSide", "LightSide.UniText"), "WaveModifier") { }
    }

    internal sealed class RangeRuleToFixedRangeSourceMigration : RenameManagedType
    {
        public RangeRuleToFixedRangeSourceMigration() : base(
            new TypeSignature("RangeRule", "LightSide", "LightSide.UniText"),
            "FixedRangeSource") { }
    }

    internal sealed class GlobalGradientToPaintProviderMigration : RenameManagedType
    {
        public GlobalGradientToPaintProviderMigration() : base(
            new TypeSignature("GlobalSettingsGradientProvider", "LightSide", "LightSide.UniText"), "GlobalSettingsPaintProvider") { }
    }

    /// <summary>
    /// The carried-over <c>asset</c> reference points at the legacy <c>UniTextGradients</c> asset and resolves
    /// to none after the rename; <see cref="GradientsCatalogToPaintsMigration"/> produces the
    /// <c>UniTextPaints</c> sibling to re-point it to (logged there).
    /// </summary>
    internal sealed class AssetGradientToPaintProviderMigration : RenameManagedType
    {
        public AssetGradientToPaintProviderMigration() : base(
            new TypeSignature("AssetGradientProvider", "LightSide", "LightSide.UniText"), "AssetPaintProvider") { }
    }
}
