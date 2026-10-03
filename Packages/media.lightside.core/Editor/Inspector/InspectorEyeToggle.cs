using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// The header eye toggle of a layered element: lit while any selected target is on, one click
    /// resolves a mixed selection to on and flips otherwise, and the owning foldout header dims while
    /// every target is off.
    /// </summary>
    public sealed class InspectorEyeToggle
    {
        private const string HeaderOffClass = "lightside-eye-toggle__header--off";

        private readonly SerializedPropertyBinding element;
        private readonly string relativePath;
        private readonly bool storesDisabled;
        private readonly string noun;
        private readonly InspectorFoldoutHeader header;
        private readonly Action changed;

        /// <summary>The button to place into a header's actions.</summary>
        public InspectorIconButton Button { get; } = new();

        /// <summary>
        /// Creates the toggle over the bool at <paramref name="relativePath"/> of the element
        /// <paramref name="element"/> resolves. <paramref name="storesDisabled"/> flips the stored
        /// polarity for a field that serializes "disabled"; <paramref name="noun"/> names the thing in
        /// tooltips and undo entries. The dimmed header is <paramref name="header"/>, or the button's
        /// enclosing <see cref="InspectorFoldoutHeader"/> when omitted; <paramref name="changed"/> runs
        /// after a click writes the new state.
        /// </summary>
        public InspectorEyeToggle(SerializedPropertyBinding element, string relativePath,
            bool storesDisabled, string noun, InspectorFoldoutHeader header = null,
            Action changed = null)
        {
            this.element = element ?? throw new ArgumentNullException(nameof(element));
            this.relativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            this.storesDisabled = storesDisabled;
            this.noun = noun ?? throw new ArgumentNullException(nameof(noun));
            this.header = header;
            this.changed = changed;
            Button.AddToClassList("lightside-eye-toggle");
            Button.clicked += Toggle;
        }

        /// <summary>Re-reads the selection and reflects it on the button and its header.</summary>
        public void Refresh()
        {
            var stored = Resolve();
            if (stored == null) return;
            var targets = stored.SerializedObject.targetObjects;
            var onCount = 0;
            for (var i = 0; i < targets.Length; i++)
                if ((bool)stored.GetValue(targets[i]) != storesDisabled)
                    onCount++;
            var anyOn = onCount > 0;
            var allOn = onCount == targets.Length;
            Button.SetState(anyOn, EditorResources.ToggleAccent, anyOn ? "eye" : "eye-off");
            Button.tooltip = targets.Length == 1
                ? (anyOn ? "Disable " : "Enable ") + noun
                : (allOn ? "Disable all selected " : "Enable all selected ") + noun + "s";
            (header ?? Button.GetFirstAncestorOfType<InspectorFoldoutHeader>())
                ?.EnableInClassList(HeaderOffClass, !anyOn);
        }

        private void Toggle()
        {
            var stored = Resolve();
            if (stored == null) return;
            var on = stored.HasMultipleValues || (bool)stored.Value == storesDisabled;
            stored.SetValue(on != storesDisabled,
                (on ? "Enable " : "Disable ") + char.ToUpperInvariant(noun[0]) + noun.Substring(1));
            Refresh();
            changed?.Invoke();
        }

        private SerializedPropertyBinding Resolve()
        {
            var current = element.FindSerializedProperty();
            return current == null
                ? null
                : new SerializedPropertyBinding(InspectorHelpers.RequireRelative(current, relativePath));
        }
    }
}
