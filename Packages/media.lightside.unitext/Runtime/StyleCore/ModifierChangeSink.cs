namespace LightSide
{
    internal readonly struct StyleChange
    {
        internal readonly Style Style;
        internal readonly BaseModifier Modifier;
        internal readonly RangeSource RangeSource;
        internal readonly UniTextDirty Flags;
        internal readonly IStateMemberReplay StateSource;
        internal readonly StateMember Member;
        internal readonly bool Structural;

        internal StyleChange(Style style, BaseModifier modifier = null,
            RangeSource rangeSource = null,
            UniTextDirty flags = UniTextDirty.Text, IStateMemberReplay stateSource = null,
            StateMember member = default, bool structural = false)
        {
            Style = style;
            Modifier = modifier;
            RangeSource = rangeSource;
            Flags = flags;
            StateSource = stateSource;
            Member = member;
            Structural = structural;
        }
    }

    internal interface IModifierChangeSink
    {
        void MarkModifierChanged(BaseModifier modifier, UniTextDirty flags,
            IStateMemberReplay source = null, StateMember member = default,
            bool structural = false);
    }

    internal interface IRangeSourceChangeSink
    {
        void MarkRangeSourceChanged(RangeSource source);
    }

    internal interface IStyleChangeSink
    {
        void MarkStyleChanged(in StyleChange change);
    }
}
