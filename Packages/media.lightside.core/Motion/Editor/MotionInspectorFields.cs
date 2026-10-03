using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Inspector pieces shared by everything that repeats a timeline.</summary>
    public static class MotionInspectorFields
    {
        /// <summary>
        /// How often a timeline repeats and what each repeat does: a count, a loop switch that stands for
        /// repeating until stopped, and the repeat mode. The mode disappears while nothing repeats, because
        /// what a repeat does is meaningless without one.
        /// </summary>
        /// <remarks>
        /// The switch sits between the two fields at its own width, which they share what is left of. It is not
        /// a field action: that slot is one control height square and would clip a word.
        /// </remarks>
        /// <param name="cycles">The <see cref="int"/> count property; negative repeats until stopped.</param>
        /// <param name="mode">The <see cref="MotionCycle"/> property.</param>
        /// <param name="playhead">
        /// Whether the owner repeats a playhead rather than a value. A playhead carries no easing of its own, so
        /// <see cref="MotionCycle.Yoyo"/> would be the same function as <see cref="MotionCycle.PingPong"/> and
        /// <see cref="MotionCycle.Incremental"/> would have nothing to accumulate; both are left off the menu
        /// rather than offered and quietly folded.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="cycles"/> or <paramref name="mode"/> is <see langword="null"/>.</exception>
        public static VisualElement CreateRepeat(SerializedProperty cycles, SerializedProperty mode,
            bool playhead = false)
        {
            if (cycles == null) throw new ArgumentNullException(nameof(cycles));
            if (mode == null) throw new ArgumentNullException(nameof(mode));

            var cyclesPath = cycles.propertyPath;
            var owner = cycles.serializedObject;
            var binding = new SerializedPropertyBinding(cycles);
            var finite = 2;

            var row = InspectorVisuals.CreateRow();
            var countField = SerializedPropertyField.Create(cycles, "Cycles");
            var modeField = SerializedPropertyField.Create(mode, "Repeat", context =>
            {
                var field = new EnumSelectorField(context.Label, Current(context), Include);
                return context.Bind(field, undoName: "Change Repeat");
            });

            var loop = new InspectorPillButton { text = "Loop", tooltip = "Repeat until stopped." };
            loop.clicked += () =>
            {
                var current = binding.RequireSerializedProperty();
                var disable = !current.hasMultipleDifferentValues && current.intValue < 0;
                if (!disable && current.intValue >= 0)
                    finite = UnityEngine.Mathf.Max(1, current.intValue);
                var next = disable ? finite : MotionCycles.Infinite;
                binding.EditSerializedProperties(each => each.intValue = next, "Change Cycles");
                Refresh();
            };

            countField.style.flexGrow = 1f;
            modeField.style.flexGrow = 1f;

            row.Add(countField);
            row.Add(loop);
            row.Add(modeField);

            return SerializedPropertyField.Observe(row, Refresh, cycles);

            void Refresh()
            {
                var current = owner.FindProperty(cyclesPath);
                var mixed = current != null && current.hasMultipleDifferentValues;
                var count = current == null ? 1 : current.intValue;
                var endless = !mixed && count < 0;

                loop.SetState(endless, mixed, EditorResources.ToggleAccent);
                countField.SetEnabled(!endless);
                modeField.style.display = mixed || count != 1 ? DisplayStyle.Flex : DisplayStyle.None;
            }

            bool Include(Enum candidate) => !playhead ||
                                            (MotionCycle)candidate is MotionCycle.Restart or MotionCycle.PingPong;

            static Enum Current(SerializedPropertyContext context) =>
                context.Value as Enum ?? MotionCycle.Restart;
        }
    }
}
