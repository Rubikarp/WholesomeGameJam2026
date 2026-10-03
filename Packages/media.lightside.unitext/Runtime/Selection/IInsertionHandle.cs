using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Insertion-handle capability on touch platforms: a single draggable handle under a
    /// collapsed caret that the user drags to reposition it, taps to raise the context menu,
    /// or releases to confirm placement. It is optional and independent of
    /// <see cref="ISelectionHandles"/>; one entity may implement either capability or both.
    /// </summary>
    /// <remarks>
    /// <see cref="Show"/> appears the handle at the current caret, <see cref="UpdatePosition"/>
    /// follows it while visible, and <see cref="Hide"/> removes it when the caret is gone,
    /// a selection forms, or focus is lost.
    /// </remarks>
    public interface IInsertionHandle : ITouchHandles
    {
        /// <summary>Shows the insertion handle at the current caret.</summary>
        void Show();

        /// <summary>Hides the insertion handle.</summary>
        void Hide();

        /// <summary>Updates the handle from the current owner state.</summary>
        void UpdatePosition();

        /// <summary>
        /// Occurs when the user is dragging the insertion handle. The parameter is the drag position
        /// in screen coordinates, to be hit-tested into the new caret codepoint.
        /// </summary>
        event Action<Vector2> InsertionHandleDragged;

        /// <summary>Occurs when the user has tapped the insertion handle (requests the context menu).</summary>
        event Action InsertionHandleTapped;

        /// <summary>Occurs when an insertion-handle drag begins — a cue to hide transient UI such as the context menu for the duration of the drag.</summary>
        event Action InsertionHandleDragStarted;

        /// <summary>Occurs when an insertion-handle drag has ended (confirms placement).</summary>
        event Action InsertionHandleDragEnded;
    }
}
