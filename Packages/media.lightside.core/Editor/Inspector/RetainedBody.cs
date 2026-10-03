using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// The retained structural-rebuild contract behind a rebuildable inspector body: the body is cleared and
    /// rebuilt only when the structure key changes, so value edits flow into retained bound fields and never
    /// sever an open picker, drag, or focus. The key names everything the body's LAYOUT depends on — kinds,
    /// mode flags, mixed-value flags — and nothing a bound field tracks by itself; return a value tuple so
    /// equality is structural.
    /// </summary>
    public sealed class RetainedBody
    {
        private readonly VisualElement root;
        private readonly Func<SerializedProperty, object> structure;
        private readonly Action<SerializedProperty> build;
        private object rendered;
        private bool hasKey;
        private bool built;

        /// <summary>Creates the contract over <paramref name="root"/>: <paramref name="structure"/> computes the key, <paramref name="build"/> fills the cleared root.</summary>
        /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
        public RetainedBody(VisualElement root, Func<SerializedProperty, object> structure,
            Action<SerializedProperty> build)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));
            this.structure = structure ?? throw new ArgumentNullException(nameof(structure));
            this.build = build ?? throw new ArgumentNullException(nameof(build));
        }

        /// <summary>
        /// Recomputes the key against <paramref name="property"/>; when it changed, clears the root and —
        /// unless <paramref name="build"/> is <see langword="false"/> — rebuilds it. Returns whether the
        /// key changed. Passing <see langword="false"/> accepts the key but defers the rebuild to the next
        /// building call — for a body that stays empty while collapsed.
        /// </summary>
        public bool Refresh(SerializedProperty property, bool build = true)
        {
            var key = structure(property);
            var changed = !hasKey || !Equals(key, rendered);
            if (changed)
            {
                hasKey = true;
                rendered = key;
                built = false;
                InspectorVisuals.ClearContent(root);
            }
            if (build && !built)
            {
                built = true;
                this.build(property);
            }
            return changed;
        }

        /// <summary>Empties the root and forgets the key, for a property that no longer resolves.</summary>
        public void Clear()
        {
            hasKey = false;
            built = false;
            rendered = null;
            InspectorVisuals.ClearContent(root);
        }

        /// <summary>Forces the next <see cref="Refresh"/> to rebuild — for an input outside the serialized object, such as a catalog edit.</summary>
        public void Invalidate() => hasKey = false;
    }
}
