using System;

namespace LightSide
{
    internal readonly struct NativeFieldPresentationState : IEquatable<NativeFieldPresentationState>
    {
        internal readonly string PresenterId;
        internal readonly string Placeholder;
        internal readonly string Identifier;
        internal readonly string PresenterData;

        internal NativeFieldPresentationState(string presenterId, string placeholder,
            string identifier, string presenterData)
        {
            PresenterId = presenterId;
            Placeholder = placeholder;
            Identifier = identifier;
            PresenterData = presenterData;
        }

        public bool Equals(NativeFieldPresentationState other)
            => PresenterId == other.PresenterId
               && Placeholder == other.Placeholder
               && Identifier == other.Identifier
               && PresenterData == other.PresenterData;

        public override bool Equals(object obj)
            => obj is NativeFieldPresentationState other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(PresenterId, Placeholder, Identifier, PresenterData);
    }

    internal enum NativeFieldCompositionPhase
    {
        Update,
        End,
        Cancel,
    }

    /// <summary>
    /// What a native control is built from: whether the editor wraps its text, whether it accepts
    /// line breaks, and what its return key declares. Wrapping alone selects a multi-line control
    /// whose return key still carries its declared action.
    /// </summary>
    internal readonly struct NativeFieldShape : IEquatable<NativeFieldShape>
    {
        internal readonly bool Wraps;
        internal readonly bool AcceptsNewlines;
        /// <summary>
        /// The declared return key, forwarded to the platform as its integer value. The bridges
        /// mirror <see cref="ReturnKeyType"/> itself; its values are serialized in consumer scenes
        /// and are therefore fixed.
        /// </summary>
        internal readonly ReturnKeyType ReturnKey;

        internal NativeFieldShape(bool wraps, bool acceptsNewlines, ReturnKeyType returnKey)
        {
            Wraps = wraps;
            AcceptsNewlines = acceptsNewlines;
            ReturnKey = returnKey;
        }

        internal static NativeFieldShape For(UniTextEditable editor, NativeKeyboardConfig config)
            => new(editor.TextComponent.WordWrap, editor.AcceptsNewlines,
                config?.ReturnKeyType ?? ReturnKeyType.Default);

        /// <summary>Whether the platform must instantiate its multi-line control.</summary>
        internal bool MultiLineControl => Wraps || AcceptsNewlines;

        public bool Equals(NativeFieldShape other)
            => Wraps == other.Wraps && AcceptsNewlines == other.AcceptsNewlines &&
               ReturnKey == other.ReturnKey;

        public override bool Equals(object obj) => obj is NativeFieldShape other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Wraps, AcceptsNewlines, ReturnKey);
    }

    internal readonly struct NativeFieldOpenRequest
    {
        internal readonly int SessionId;
        internal readonly int AuthorityRevision;
        internal readonly NativeKeyboardConfig Config;
        internal readonly string Text;
        internal readonly int SelectionStart;
        internal readonly int SelectionEnd;
        internal readonly bool PasswordMode;
        internal readonly NativeFieldShape Shape;
        internal readonly bool ReadOnly;
        internal readonly bool CopyAllowed;
        internal readonly NativeFieldPresentationState Presentation;

        internal NativeFieldOpenRequest(int sessionId, int authorityRevision,
            NativeKeyboardConfig config, string text, int selectionStart, int selectionEnd,
            bool passwordMode, in NativeFieldShape shape, bool readOnly, bool copyAllowed,
            in NativeFieldPresentationState presentation)
        {
            SessionId = sessionId;
            AuthorityRevision = authorityRevision;
            Config = config;
            Text = text;
            SelectionStart = selectionStart;
            SelectionEnd = selectionEnd;
            PasswordMode = passwordMode;
            Shape = shape;
            ReadOnly = readOnly;
            CopyAllowed = copyAllowed;
            Presentation = presentation;
        }
    }

    internal readonly struct NativeFieldUpdateRequest
    {
        internal readonly int SessionId;
        internal readonly int AuthorityRevision;
        internal readonly NativeKeyboardConfig Config;
        internal readonly bool PasswordMode;
        internal readonly NativeFieldShape Shape;
        internal readonly bool ReadOnly;
        internal readonly bool CopyAllowed;
        internal readonly NativeFieldPresentationState Presentation;

        internal NativeFieldUpdateRequest(int sessionId, int authorityRevision,
            NativeKeyboardConfig config, bool passwordMode, in NativeFieldShape shape,
            bool readOnly, bool copyAllowed,
            in NativeFieldPresentationState presentation)
        {
            SessionId = sessionId;
            AuthorityRevision = authorityRevision;
            Config = config;
            PasswordMode = passwordMode;
            Shape = shape;
            ReadOnly = readOnly;
            CopyAllowed = copyAllowed;
            Presentation = presentation;
        }
    }

    internal readonly struct NativeFieldEditIntent
    {
        internal readonly int SessionId;
        internal readonly int NativeRevision;
        internal readonly int AuthorityRevision;
        internal readonly int Start;
        internal readonly int Length;
        internal readonly string Text;
        internal readonly int SelectionStart;
        internal readonly int SelectionEnd;

        internal NativeFieldEditIntent(int sessionId, int nativeRevision, int authorityRevision,
            int start, int length, string text, int selectionStart, int selectionEnd)
        {
            SessionId = sessionId;
            NativeRevision = nativeRevision;
            AuthorityRevision = authorityRevision;
            Start = start;
            Length = length;
            Text = text;
            SelectionStart = selectionStart;
            SelectionEnd = selectionEnd;
        }
    }

    internal readonly struct NativeFieldCompositionIntent
    {
        internal readonly int SessionId;
        internal readonly int NativeRevision;
        internal readonly int AuthorityRevision;
        internal readonly NativeFieldCompositionPhase Phase;
        internal readonly int Start;
        internal readonly int Length;
        internal readonly string Text;
        internal readonly int Cursor;

        internal NativeFieldCompositionIntent(int sessionId, int nativeRevision, int authorityRevision,
            NativeFieldCompositionPhase phase, int start, int length, string text, int cursor)
        {
            SessionId = sessionId;
            NativeRevision = nativeRevision;
            AuthorityRevision = authorityRevision;
            Phase = phase;
            Start = start;
            Length = length;
            Text = text;
            Cursor = cursor;
        }
    }

    internal readonly struct NativeFieldSelectionIntent
    {
        internal readonly int SessionId;
        internal readonly int NativeRevision;
        internal readonly int AuthorityRevision;
        internal readonly int Start;
        internal readonly int End;

        internal NativeFieldSelectionIntent(int sessionId, int nativeRevision, int authorityRevision,
            int start, int end)
        {
            SessionId = sessionId;
            NativeRevision = nativeRevision;
            AuthorityRevision = authorityRevision;
            Start = start;
            End = end;
        }
    }

    internal readonly struct NativeFieldActionIntent
    {
        internal readonly int SessionId;
        internal readonly int NativeRevision;
        internal readonly int AuthorityRevision;
        internal readonly string Action;
        /// <summary>Modifier state of the key press behind the action, or none for a control.</summary>
        internal readonly NativeModifiers Modifiers;

        internal NativeFieldActionIntent(int sessionId, int nativeRevision,
            int authorityRevision, string action, NativeModifiers modifiers)
        {
            SessionId = sessionId;
            NativeRevision = nativeRevision;
            AuthorityRevision = authorityRevision;
            Action = action;
            Modifiers = modifiers;
        }
    }

    internal readonly struct NativeInputQuiescedIntent
    {
        internal readonly int SessionId;
        internal readonly int NativeRevision;
        internal readonly int AuthorityRevision;

        internal NativeInputQuiescedIntent(int sessionId, int nativeRevision,
            int authorityRevision)
        {
            SessionId = sessionId;
            NativeRevision = nativeRevision;
            AuthorityRevision = authorityRevision;
        }
    }

    internal interface INativeFieldIntentSink
    {
        void Apply(in NativeFieldEditIntent intent);
        void Apply(in NativeFieldCompositionIntent intent);
        void Apply(in NativeFieldSelectionIntent intent);
        void Apply(in NativeFieldActionIntent intent);
        void Apply(in NativeInputQuiescedIntent intent);
        void Fault(int sessionId, int nativeRevision);
    }
}
