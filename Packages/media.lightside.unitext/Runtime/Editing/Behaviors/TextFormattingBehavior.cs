using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// User-driven formatting commands, each binding a style source (<see cref="IFormatStyleSource"/>) to an
    /// optional primary-modifier shortcut (Ctrl on Windows/Linux, Cmd on macOS). A command toggles its style
    /// on the selection, or as a typing style at a collapsed caret (<see cref="UniTextEditable.ToggleStyle{T}"/>
    /// semantics), in the syntax the resolved style's rule speaks. Toolbar buttons
    /// and context menus call <see cref="Toggle(string)"/> / <see cref="Toggle(int)"/>; button state comes
    /// from <see cref="UniTextEditable.IsStyleActive{T}"/> via <see cref="CaretContextBehavior"/>. Default
    /// commands: bold (B), italic (I), underline (U), clear formatting (<c>\</c>).
    /// </summary>
    [Serializable]
    [TypeDescription("Formatting: style commands (shortcuts / buttons)")]
    [TypeGroup("Formatting", 1)]
    public sealed partial class TextFormattingBehavior : InputBehavior
    {
        [Serializable]
        public sealed partial class FormatCommand
        {
            /// <summary>Stable command name used by toolbar and context-menu calls.</summary>
            [Tooltip("Name for Toggle(string) lookups from toolbar buttons / context menus.")]
            [SerializeField, StateProperty(nameof(NotifyMemberChanged))]
            private string name;

            /// <summary>Style source toggled by this command.</summary>
            [SerializeReference, TypeSelector, StateProperty(nameof(ApplyTargetChange), Owned = true)]
            private IFormatStyleSource target;

            /// <summary>Shortcut key combined with the platform primary modifier.</summary>
            [Tooltip("Shortcut key, combined with the platform primary modifier (Ctrl / Cmd). None = no shortcut.")]
            [SerializeField, StateProperty(nameof(NotifyMemberChanged))]
            private NativeKeyCode key = NativeKeyCode.None;

            /// <summary>Whether the shortcut additionally requires Shift.</summary>
            [Tooltip("Require Shift in addition to the primary modifier.")]
            [SerializeField, StateProperty(nameof(NotifyMemberChanged))]
            private bool shift;

            [NonSerialized] private TextFormattingBehavior owner;

            internal TextFormattingBehavior Owner => owner;

            internal void SetOwner(TextFormattingBehavior behavior)
            {
                if (ReferenceEquals(owner, behavior))
                    return;
                if (target != null) target.Changed -= NotifyStructureChanged;
                owner = behavior;
                if (behavior != null && target != null)
                {
                    target.Changed += NotifyStructureChanged;
                }
            }

            private void ApplyTargetChange(IFormatStyleSource previous, IFormatStyleSource current)
            {
                if (ReferenceEquals(previous, current)) return;
                if (previous != null) previous.Changed -= NotifyStructureChanged;
                if (owner != null && current != null)
                {
                    current.Changed -= NotifyStructureChanged;
                    current.Changed += NotifyStructureChanged;
                }
                NotifyStructureChanged();
            }

            private void NotifyMemberChanged(StateMember member)
                => owner?.OnCommandChanged(this, member);

            private void NotifyStructureChanged()
                => owner?.OnCommandStructureChanged();
        }

        /// <summary>Formatting commands in shortcut resolution order.</summary>
        [SerializeField, StateList(nameof(ApplyCommandsChange), Owned = true,
            AllowNullItems = false, AllowDuplicateReferences = false)]
        private StyledList<FormatCommand> commands = new()
        {
            new FormatCommand { Name = "bold", Target = new ReferencedStyle { Modifier = new BoldModifier() }, Key = NativeKeyCode.B },
            new FormatCommand { Name = "italic", Target = new ReferencedStyle { Modifier = new ItalicModifier() }, Key = NativeKeyCode.I },
            new FormatCommand { Name = "underline", Target = new ReferencedStyle { Modifier = new UnderlineModifier() }, Key = NativeKeyCode.U },
        };

        [NonSerialized] private ReferenceBinding<FormatCommand> boundCommands;
        [NonSerialized] private ReferenceBinding<ChromeRule> boundChromeRules;

        /// <summary>Primary-modifier shortcut that clears formatting; None disables it.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Clear-formatting shortcut (primary modifier + key): strips every style from the selection; at a caret the next typed text is plain. None = no shortcut.")]
        private NativeKeyCode clearFormattingKey = NativeKeyCode.Backslash;

        /// <summary>How authored markup characters are exposed while editing.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("How markup tags display: Hidden (styled, tags invisible), RevealActiveRange (tags show around the caret), Raw (tags always visible).")]
        private MarkupVisibility markupVisibility = MarkupVisibility.Hidden;

        /// <summary>Rules styling visible markup characters in matching precedence order.</summary>
        [SerializeField, StateList(nameof(ApplyMarkupChromeChange), Owned = true,
            AllowNullItems = false, AllowDuplicateReferences = false)]
        [Tooltip("Styles for the visible markup tag characters (e.g. dim color), shown under RevealActiveRange / Raw. Each entry's selector picks which markup it targets; the most specific match wins.")]
        private StyledList<ChromeRule> markupChrome = new();

        /// <summary>Whether structured clipboard formatting is accepted on paste.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("Off: paste always inserts the clipboard's plain text — HTML/Markdown/UniText formatting from the source app is ignored.")]
        private bool richPaste = true;

        /// <summary>How plain clipboard text is interpreted by the markup parser.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("How plain-text paste is interpreted: Auto (by tag visibility — Raw parses, else literal), Literal (verbatim), Parse (as this field's markup).")]
        private PlainTextPastePolicy plainTextPaste = PlainTextPastePolicy.Auto;

        /// <summary>Whether markup typed by the user is parsed or inserted literally.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("How hand-typed markup is treated: Parse (typed <b> styles), Literal (typed <b> stays literal text). Commands, programmatic styling and paste are unaffected.")]
        private TypingMarkupPolicy typingMarkup = TypingMarkupPolicy.Parse;

        /// <summary>Whether a repeated outward arrow clears retained typing formatting.</summary>
        [SerializeField, StateProperty(nameof(NotifyChanged))]
        [Tooltip("A repeated outward arrow at a formatting boundary clears an explicitly retained typing context. Range edges are outside formatting by default.")]
        private bool arrowKeyEscape = true;

        [NonSerialized] private Saved<MarkupVisibility> savedMarkupVisibility;
        [NonSerialized] private Saved<bool> savedRichPaste;
        [NonSerialized] private Saved<PlainTextPastePolicy> savedPlainTextPaste;
        [NonSerialized] private Saved<TypingMarkupPolicy> savedTypingMarkup;
        [NonSerialized] private Saved<bool> savedArrowKeyEscape;

        internal override void OnChangeSinkChanged(
            IInputBehaviorChangeSink previous, IInputBehaviorChangeSink current)
        {
            if (current != null)
            {
                BindCommands();
                BindChromeRules();
            }
            else
            {
                UnbindCommands();
                UnbindChromeRules();
            }
        }

        protected override void OnEnable()
        {
            editable.KeyResolver.Subscribe(Resolve);
            editable.MarkupVisibility = savedMarkupVisibility.Capture(editable.MarkupVisibility, markupVisibility);
            editable.RichPaste = savedRichPaste.Capture(editable.RichPaste, richPaste);
            editable.PlainTextPaste = savedPlainTextPaste.Capture(editable.PlainTextPaste, plainTextPaste);
            editable.TypingMarkup = savedTypingMarkup.Capture(editable.TypingMarkup, typingMarkup);
            editable.ArrowKeyEscapesFormatting = savedArrowKeyEscape.Capture(editable.ArrowKeyEscapesFormatting, arrowKeyEscape);
            for (int i = 0; i < markupChrome.Count; i++)
                if (markupChrome[i] != null && !editable.MarkupChrome.Contains(markupChrome[i])) editable.MarkupChrome.Add(markupChrome[i]);
            editable.RefreshMarkup();
        }

        protected override void OnDisable()
        {
            editable.KeyResolver.Unsubscribe(Resolve);
            editable.MarkupVisibility = savedMarkupVisibility.Restore();
            editable.RichPaste = savedRichPaste.Restore();
            editable.PlainTextPaste = savedPlainTextPaste.Restore();
            editable.TypingMarkup = savedTypingMarkup.Restore();
            editable.ArrowKeyEscapesFormatting = savedArrowKeyEscape.Restore();
            for (int i = 0; i < markupChrome.Count; i++)
                if (markupChrome[i] != null) editable.MarkupChrome.Remove(markupChrome[i]);
            editable.RefreshMarkup();
        }

        private void Resolve(ref KeyResolve key)
        {
            if (key.action != EditAction.None) return;

            for (int i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (command?.Target == null) continue;
                if (command.Key == NativeKeyCode.None || command.Key != key.key) continue;
                var required = PlatformKeySemantics.PrimaryModifier | (command.Shift ? NativeModifiers.Shift : NativeModifiers.None);
                if (key.modifiers != required) continue;

                command.Target.Toggle(editable);
                key.action = EditAction.Ignore;
                return;
            }

            if (clearFormattingKey != NativeKeyCode.None && key.key == clearFormattingKey
                && key.modifiers == PlatformKeySemantics.PrimaryModifier)
            {
                editable.ClearFormatting();
                key.action = EditAction.Ignore;
            }
        }

        /// <summary>Toggles the command at <paramref name="index"/>. Returns whether the style is now on.</summary>
        public bool Toggle(int index)
        {
            if (editable == null || index < 0 || index >= commands.Count) return false;
            var target = commands[index]?.Target;
            return target != null && target.Toggle(editable);
        }

        /// <summary>Toggles the command named <paramref name="name"/> (case-insensitive).</summary>
        public bool Toggle(string name)
        {
            if (editable == null || string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < commands.Count; i++)
            {
                var command = commands[i];
                if (command?.Target == null) continue;
                if (!string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                return command.Target.Toggle(editable);
            }
            return false;
        }

        private void ApplyCommandsChange()
        {
            if (HasChangeSink) BindCommands();
            NotifyStructureChanged(Members.Commands);
        }

        private void ApplyMarkupChromeChange()
        {
            if (HasChangeSink) BindChromeRules();
            if (IsEnabled) editable.RefreshMarkup();
            NotifyStructureChanged(Members.MarkupChrome);
        }

        private void OnCommandChanged(FormatCommand command, StateMember member)
            => NotifyNestedChanged(command, member);

        private void OnCommandStructureChanged()
            => NotifyStructureChanged(Members.Commands);

        internal override bool ReplayNestedStateMember(InputBehavior authoredBehavior,
            IStateMemberReplay source, StateMember member)
        {
            if (authoredBehavior is not TextFormattingBehavior authored ||
                source is not FormatCommand sourceCommand)
                return false;
            var index = ReferenceIdentity.IndexOf(authored.commands, sourceCommand);
            return (uint)index < (uint)commands.Count &&
                   commands[index] is IStateMemberReplay target &&
                   target.ReplayStateMember(source, member);
        }

        private void BindCommands()
        {
            boundCommands ??= new ReferenceBinding<FormatCommand>(ConnectCommand, DisconnectCommand);
            boundCommands.Reconcile(commands);
        }

        private void UnbindCommands() => boundCommands?.Clear();

        private void ConnectCommand(FormatCommand command) => command.SetOwner(this);

        private void DisconnectCommand(FormatCommand command)
        {
            if (ReferenceEquals(command.Owner, this)) command.SetOwner(null);
        }

        private void BindChromeRules()
        {
            boundChromeRules ??= new ReferenceBinding<ChromeRule>(ConnectChromeRule, DisconnectChromeRule);
            boundChromeRules.Reconcile(markupChrome);
        }

        private void UnbindChromeRules() => boundChromeRules?.Clear();

        private void ConnectChromeRule(ChromeRule rule)
        {
            rule.SetChangeCallback(NotifyChromeChanged);
            if (IsEnabled && !editable.MarkupChrome.Contains(rule)) editable.MarkupChrome.Add(rule);
        }

        private void DisconnectChromeRule(ChromeRule rule)
        {
            if (!rule.HasChangeCallback(NotifyChromeChanged)) return;
            if (IsEnabled) editable.MarkupChrome.Remove(rule);
            rule.SetChangeCallback(null);
        }

        private void NotifyChromeChanged() => NotifyChanged();

    }
}
