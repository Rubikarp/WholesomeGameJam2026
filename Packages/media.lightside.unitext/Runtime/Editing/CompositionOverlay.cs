using System;
using UnityEngine;

namespace LightSide
{
    internal struct CompositionOverlay
    {
        public char[] textBuffer;
        public int textLength;
        public int insertionCodepoint;
        public int cursorCharOffset;
        public CompositionClause[] clauses;
        public int clauseCount;
        public int codepointCount;

        public bool IsActive => textLength > 0;
        public bool HasCursor => cursorCharOffset >= 0;

        public void Set(
            int insertionCp,
            ReadOnlySpan<char> text,
            ReadOnlySpan<CompositionClause> clauseSpan,
            int cursorChar)
        {
            insertionCodepoint = insertionCp;

            if (textBuffer == null || textBuffer.Length < text.Length)
                textBuffer = new char[Mathf.NextPowerOfTwo(Mathf.Max(text.Length, 32))];
            text.CopyTo(textBuffer);
            textLength = text.Length;
            codepointCount = UnicodeData.CountCodepoints(text);

            cursorCharOffset = cursorChar < 0 ? -1 : Mathf.Clamp(cursorChar, 0, textLength);

            if (clauseSpan.Length > 0)
            {
                if (clauses == null || clauses.Length < clauseSpan.Length)
                    clauses = new CompositionClause[Mathf.NextPowerOfTwo(Mathf.Max(clauseSpan.Length, 4))];
                clauseSpan.CopyTo(clauses);
                clauseCount = clauseSpan.Length;
            }
            else
            {
                clauseCount = 0;
            }
        }

        public void Clear()
        {
            textLength = 0;
            insertionCodepoint = 0;
            cursorCharOffset = 0;
            codepointCount = 0;
            clauseCount = 0;
        }

        public int CursorCodepointOffset()
        {
            if (textLength == 0) return 0;
            if (cursorCharOffset < 0) return codepointCount;
            if (cursorCharOffset == 0) return 0;
            if (cursorCharOffset >= textLength) return codepointCount;
            return UnicodeData.CountCodepoints(new ReadOnlySpan<char>(textBuffer, 0, cursorCharOffset));
        }
    }
}
