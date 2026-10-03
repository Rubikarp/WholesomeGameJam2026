namespace LightSide
{
    /// <summary>Declares which observable outputs a modifier changes when reapplied.</summary>
    public interface IModifierCommitChanges
    {
        /// <summary>Output categories changed by this modifier.</summary>
        UniTextCommitChanges CommitChanges { get; }
    }

    /// <summary>Routes modifier changes through UniText's normal invalidation pipeline.</summary>
    public static class UniTextModifierInvalidation
    {
        /// <summary>Queues a modifier reapplication and infers its observable output changes.</summary>
        public static void MarkModifierDirty(this UniTextBase owner, BaseModifier modifier, UniTextDirty flags)
        {
            if (owner == null || modifier == null || flags == UniTextDirty.None) return;

            if ((flags & UniTextDirty.FullRebuild) == 0)
                owner.AttributeParser?.QueueReapply(modifier);

            if (modifier is IModifierCommitChanges source)
                owner.SetDirty(flags, source.CommitChanges);
            else
                owner.SetDirty(flags);
        }

        /// <summary>Queues a modifier reapplication with an exact observable-change contract.</summary>
        public static void MarkModifierDirty(this UniTextBase owner, BaseModifier modifier, UniTextDirty flags,
            UniTextCommitChanges changes)
        {
            if (owner == null || modifier == null || flags == UniTextDirty.None) return;

            if ((flags & UniTextDirty.FullRebuild) == 0)
                owner.AttributeParser?.QueueReapply(modifier);

            owner.SetDirty(flags, changes);
        }
    }
}
