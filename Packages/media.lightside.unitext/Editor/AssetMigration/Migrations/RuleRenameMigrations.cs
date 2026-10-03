namespace LightSide
{
    internal sealed class PropertyEffectToParameterRuleMigration : RenameManagedType
    {
        public PropertyEffectToParameterRuleMigration() : base(
            new TypeSignature("PropertyEffect", "LightSide", "LightSide.UniText"), "ParameterRule") { }
    }

    internal sealed class ModifierEffectToModifierRuleMigration : RenameManagedType
    {
        public ModifierEffectToModifierRuleMigration() : base(
            new TypeSignature("ModifierEffect", "LightSide", "LightSide.UniText"), "ModifierRule") { }
    }

    internal sealed class ScalarSelectorToStateSelectorMigration : RenameManagedType
    {
        public ScalarSelectorToStateSelectorMigration() : base(
            new TypeSignature("ScalarRangeEffectSelector", "LightSide", "LightSide.UniText"),
            "ScalarRangeStateSelector") { }
    }

    internal sealed class AllSelectorToStateSelectorMigration : RenameManagedType
    {
        public AllSelectorToStateSelectorMigration() : base(
            new TypeSignature("AllRangeEffectSelector", "LightSide", "LightSide.UniText"),
            "AllRangeStateSelector") { }
    }

    internal sealed class AnySelectorToStateSelectorMigration : RenameManagedType
    {
        public AnySelectorToStateSelectorMigration() : base(
            new TypeSignature("AnyRangeEffectSelector", "LightSide", "LightSide.UniText"),
            "AnyRangeStateSelector") { }
    }

    internal sealed class NotSelectorToStateSelectorMigration : RenameManagedType
    {
        public NotSelectorToStateSelectorMigration() : base(
            new TypeSignature("NotRangeEffectSelector", "LightSide", "LightSide.UniText"),
            "NotRangeStateSelector") { }
    }

    internal sealed class InteractionSelectorToStateSelectorMigration : RenameManagedType
    {
        public InteractionSelectorToStateSelectorMigration() : base(
            new TypeSignature("InteractionRangeEffectSelector", "LightSide", "LightSide.UniText"),
            "InteractionRangeStateSelector") { }
    }

    internal sealed class SpoilerSelectorToStateSelectorMigration : RenameManagedType
    {
        public SpoilerSelectorToStateSelectorMigration() : base(
            new TypeSignature("SpoilerConcealedRangeEffectSelector", "LightSide", "LightSide.UniText"),
            "SpoilerConcealedRangeStateSelector") { }
    }

    internal sealed class InstantDriverToPlaybackMigration : RenameManagedType
    {
        public InstantDriverToPlaybackMigration() : base(
            new TypeSignature("InstantEffectDriver", "LightSide", "LightSide.UniText"),
            "InstantPlayback") { }
    }

    internal sealed class PropertyDriverToTransitionPlaybackMigration : RenameManagedType
    {
        public PropertyDriverToTransitionPlaybackMigration() : base(
            new TypeSignature("BuiltInPropertyDriver", "LightSide", "LightSide.UniText"),
            "TransitionPlayback") { }
    }

    internal sealed class SignalProgressDriverToPlaybackMigration : RenameManagedType
    {
        public SignalProgressDriverToPlaybackMigration() : base(
            new TypeSignature("SignalProgressEffectDriver", "LightSide", "LightSide.UniText"),
            "SignalProgressPlayback") { }
    }

    internal sealed class ManualDriverToPlaybackMigration : RenameManagedType
    {
        public ManualDriverToPlaybackMigration() : base(
            new TypeSignature("ManualEffectDriver", "LightSide", "LightSide.UniText"),
            "ManualPlayback") { }
    }

    internal sealed class FloatEffectValueToRuleValueMigration : RenameManagedType
    {
        public FloatEffectValueToRuleValueMigration() : base(
            new TypeSignature("FloatRangeEffectValue", "LightSide", "LightSide.UniText"),
            "FloatRuleValue") { }
    }

    internal sealed class UnitEffectValueToRuleValueMigration : RenameManagedType
    {
        public UnitEffectValueToRuleValueMigration() : base(
            new TypeSignature("UnitRangeEffectValue", "LightSide", "LightSide.UniText"),
            "UnitRuleValue") { }
    }

    internal sealed class Vector2EffectValueToRuleValueMigration : RenameManagedType
    {
        public Vector2EffectValueToRuleValueMigration() : base(
            new TypeSignature("Vector2RangeEffectValue", "LightSide", "LightSide.UniText"),
            "Vector2RuleValue") { }
    }

    internal sealed class UnitVector2EffectValueToRuleValueMigration : RenameManagedType
    {
        public UnitVector2EffectValueToRuleValueMigration() : base(
            new TypeSignature("UnitVector2RangeEffectValue", "LightSide", "LightSide.UniText"),
            "UnitVector2RuleValue") { }
    }

    internal sealed class ColorEffectValueToRuleValueMigration : RenameManagedType
    {
        public ColorEffectValueToRuleValueMigration() : base(
            new TypeSignature("ColorRangeEffectValue", "LightSide", "LightSide.UniText"),
            "ColorRuleValue") { }
    }

    internal sealed class InteractiveRulesFieldMigration : RenameManagedField
    {
        public InteractiveRulesFieldMigration() : base("effects", "rules", typeof(InteractiveModifier)) { }
    }

    /// <summary>Lists both sides of the rule type renames, so this rename holds whichever order they run in.</summary>
    internal sealed class RulePlaybackFieldMigration : RenameManagedField
    {
        public RulePlaybackFieldMigration() : base("driver", "playback",
            new TypeSignature("PropertyEffect", "LightSide", "LightSide.UniText"),
            new TypeSignature("ParameterRule", "LightSide", "LightSide.UniText"),
            new TypeSignature("ModifierEffect", "LightSide", "LightSide.UniText"),
            new TypeSignature("ModifierRule", "LightSide", "LightSide.UniText")) { }
    }

    /// <summary>Lists both sides of the rule type rename, so this rename holds whichever order they run in.</summary>
    internal sealed class ParameterRuleIdMigration : RenameManagedField
    {
        public ParameterRuleIdMigration() : base("propertyId", "parameterId",
            new TypeSignature("PropertyEffect", "LightSide", "LightSide.UniText"),
            new TypeSignature("ParameterRule", "LightSide", "LightSide.UniText")) { }
    }
}
