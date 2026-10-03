using System;

namespace LightSide
{
    /// <summary>Immutable rendered Unicode text and the revision that owns its coordinates.</summary>
    public readonly struct TextSnapshot
    {
        private readonly string text;

        /// <summary>The revision that must accompany ranges computed from this text.</summary>
        public TextRevision Revision { get; }

        /// <summary>Rendered UTF-16 text with source markup removed.</summary>
        public ReadOnlyMemory<char> Text => (text ?? string.Empty).AsMemory();

        /// <summary>Number of Unicode codepoints addressable by <see cref="TextRange"/>.</summary>
        public int CodepointCount { get; }

        /// <summary>Whether this snapshot came from a completed parse.</summary>
        public bool IsValid => Revision.IsValid;

        internal TextSnapshot(string text, TextRevision revision)
        {
            if (!revision.IsValid) throw new ArgumentException("Text revision is invalid.", nameof(revision));
            this.text = text ?? string.Empty;
            Revision = revision;
            CodepointCount = UnicodeData.CountCodepoints(this.text.AsSpan());
        }

        /// <inheritdoc/>
        public override string ToString() => text ?? string.Empty;
    }
}
