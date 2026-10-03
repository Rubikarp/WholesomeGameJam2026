using System;

namespace LightSide
{
    /// <summary>
    /// Structured payload of the <see cref="UniTextSourceClipboardAdapter"/> channel: the selection's visible
    /// text plus the markup that was live on it. Each span identifies its modifier by
    /// <see cref="BaseModifier.Signature"/>, so paste resolves the destination's modifier by identity rather
    /// than by source syntax. Serialised with <see cref="UnityEngine.JsonUtility"/>.
    /// </summary>
    [Serializable]
    internal sealed class UniTextClipboardFragment
    {
        public string text;
        public Span[] spans;

        /// <summary>
        /// One markup occurrence over <see cref="text"/>: a styled range, or — when <see cref="selfClosing"/> —
        /// an inline object occupying one Object Replacement Character.
        /// </summary>
        [Serializable]
        public struct Span
        {
            public int offset;
            public int length;
            public string signature;
            public string parameter;
            public bool selfClosing;
        }
    }
}
