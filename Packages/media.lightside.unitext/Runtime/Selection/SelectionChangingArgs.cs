namespace LightSide
{
    /// <summary>
    /// Vetoable payload of <see cref="UniTextSelectable.SelectionChanging"/>. Subscribers
    /// inspect <see cref="Proposed"/> and may set <see cref="Cancel"/> to <see langword="true"/>
    /// to block the selection change before it is applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference type with a mutable <see cref="Cancel"/> flag — .NET
    /// <see cref="System.ComponentModel.CancelEventArgs"/> pattern. A fresh instance is
    /// allocated per <see cref="UniTextSelectable.SelectionChanging"/> emission so subscribers
    /// can synchronously trigger another mutation from inside the handler (re-entrancy is
    /// safe). Veto fires only on user-driven mutations (click / move / extend / select-word
    /// etc.) — text-edit side effects use the internal non-vetoable path, so the selection
    /// hot path remains zero-alloc.
    /// </para>
    /// <para>
    /// Real use cases (P2.A atomic tokens, integrator-defined read-only ranges): block
    /// selection from landing inside an atomic pill, or constrain selection to stay within
    /// editable regions. Standard shouldX-pattern across UIKit
    /// (<c>shouldChangeTextInRange</c>), Web (<c>beforeinput</c> with <c>preventDefault</c>),
    /// WPF (<c>PreviewKeyDown</c>) — see D-010.
    /// </para>
    /// <para>
    /// <see cref="UserEvent"/> uses the same hierarchical strings as
    /// <see cref="SelectionChangedArgs.UserEvent"/>.
    /// </para>
    /// </remarks>
    public sealed class SelectionChangingArgs
    {
        /// <summary>Selection state before the change.</summary>
        public TextSelection Previous { get; }

        /// <summary>Selection state that will be applied if not cancelled.</summary>
        public TextSelection Proposed { get; set; }

        /// <summary>Hierarchical category string — see <see cref="SelectionChangedArgs"/>.</summary>
        public string UserEvent { get; }

        /// <summary>
        /// When set to <see langword="true"/>, the selection change is aborted; previous
        /// state is preserved and <see cref="UniTextSelectable.SelectionChanged"/> does not
        /// fire.
        /// </summary>
        public bool Cancel { get; set; }

        internal SelectionChangingArgs(TextSelection previous, TextSelection proposed, string userEvent)
        {
            Previous = previous;
            Proposed = proposed;
            UserEvent = userEvent;
        }
    }
}
