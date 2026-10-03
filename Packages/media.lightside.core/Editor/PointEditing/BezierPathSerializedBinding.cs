using System;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Writes a whole <see cref="BezierPath"/> back through the serialized property that holds it, so an editor
    /// can work on a detached copy and commit it as one undoable edit. Also the comparison and copy every such
    /// editor needs to tell an outside change from its own.
    /// </summary>
    public sealed class BezierPathSerializedBinding
    {
        private readonly SerializedPropertyBinding path;

        /// <summary>Binds to the serialized <see cref="BezierPath"/> at <paramref name="property"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="property"/> is not a Bézier path.</exception>
        public BezierPathSerializedBinding(SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            InspectorHelpers.RequireRelative(property, "knots");
            InspectorHelpers.RequireRelative(property, "closed");
            path = new SerializedPropertyBinding(property);
        }

        /// <summary>Writes <paramref name="value"/> into the property as one undo step named <paramref name="undoName"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public void SetValue(BezierPath value, string undoName)
            => SetValue(value, undoName, false);

        /// <summary>Writes <paramref name="value"/> into the gesture's existing undo group, so a drag collapses to one step instead of one per pointer move.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public void SetValueInCurrentUndoGroup(BezierPath value, string undoName)
            => SetValue(value, undoName, true);

        private void SetValue(BezierPath value, string undoName, bool currentUndoGroup)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            void Edit(SerializedProperty property)
            {
                var knots = InspectorHelpers.RequireRelative(property, "knots");
                knots.arraySize = value.Count;
                for (var i = 0; i < value.Count; i++)
                    knots.GetArrayElementAtIndex(i).boxedValue = value[i];
                InspectorHelpers.RequireRelative(property, "closed").boolValue = value.Closed;
            }

            if (currentUndoGroup)
                path.EditSerializedPropertiesInCurrentUndoGroup(Edit, undoName);
            else
                path.EditSerializedProperties(Edit, undoName);
        }

        /// <summary>A detached copy of <paramref name="value"/> — what an editor edits while the serialized original stands.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
        public static BezierPath Copy(BezierPath value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var copy = new BezierPath();
            copy.Replace(value.Knots, value.Closed);
            return copy;
        }

        /// <summary>Whether two paths hold the same knots in the same order, closed the same way. A null is equal only to another null.</summary>
        public static bool AreEqual(BezierPath left, BezierPath right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Closed != right.Closed ||
                left.Count != right.Count)
                return false;
            for (var i = 0; i < left.Count; i++)
                if (!left[i].Equals(right[i])) return false;
            return true;
        }
    }
}
