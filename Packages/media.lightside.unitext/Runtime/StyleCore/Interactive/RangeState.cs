namespace LightSide
{
    /// <summary>
    /// Interaction state of an interactive range — the machine <see cref="InteractiveModifier"/>
    /// runs per range: <c>Normal → Hovered → Pressed → back</c>, the CSS LVHA / UIKit
    /// <c>UITextItem</c> model. Activation (a confirmed primary click / tap) is a transition
    /// event, not a state — it is raised as <see cref="RangeInteractionKind.Activated"/>
    /// between <see cref="Pressed"/> and the release state.
    /// </summary>
    public enum RangeState : byte
    {
        /// <summary>No pointer interaction with the range.</summary>
        Normal,

        /// <summary>The pointer rests over the range without a press. Touch passes through here only transiently (uGUI delivers enter before down).</summary>
        Hovered,

        /// <summary>A primary press is held inside the range. Entered instantly on press for every pointer kind — delayed touch feedback reads as lag.</summary>
        Pressed,

        /// <summary>The range does not react to input because <see cref="InteractiveModifier.IsRangeEnabled"/> returned <see langword="false"/>.</summary>
        Disabled
    }
}
