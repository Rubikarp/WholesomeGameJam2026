

namespace LightSide
{
    /// <summary>
    /// Represents a parsed text range with associated tag information.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="ParseRule"/> implementations to communicate which
    /// text regions should be affected by modifiers and where tags should be stripped.
    /// </remarks>
    public struct ParsedRange
    {
        /// <summary>Start index of the content (after opening tag).</summary>
        public int start;
        /// <summary>End index of the content (before closing tag).</summary>
        public int end;
        /// <summary>Start index of the opening tag, or -1 if no tag.</summary>
        public int openStart;
        /// <summary>End index of the opening tag.</summary>
        public int openEnd;
        /// <summary>Start index of the closing tag. -1 when the syntax has no close marker by design (list items) — distinct from an unclosed pair.</summary>
        public int closeStart;
        /// <summary>End index of the closing tag, or -1 with <see cref="closeStart"/>.</summary>
        public int closeEnd;
        /// <summary>Parameter extracted from the tag (e.g., color value).</summary>
        public string parameter;
        /// <summary>Optional <c>#label</c> anchor authored on the opening tag, or null.</summary>
        public string label;

        internal string defaultParameter;
        internal ModifierParameterMode parameterMode;

        internal readonly ModifierParameters Parameters
        {
            get
            {
                var parameters = parameterMode == ModifierParameterMode.OpaquePrimary
                    ? ModifierParameters.OpaquePrimary(parameter, defaultParameter)
                    : ModifierParameters.Positional(parameter, defaultParameter);
                return label == null ? parameters : parameters.WithLabel(label);
            }
        }
        /// <summary>String to insert for self-closing tags.</summary>
        public string insertString;
        /// <summary>
        /// The leaf rule that produced this range — stamped by aggregating rules
        /// (<see cref="CompositeParseRule"/>) so per-syntax consumers (markup chrome) resolve
        /// against the actual syntax, not the aggregate. Null = the matching rule itself.
        /// </summary>
        public ParseRule sourceRule;

        /// <summary>Optional stable key used to reconcile this logical match across revisions.</summary>
        public string matchKey;

        /// <summary>Optional semantic routing channel for the produced range entity.</summary>
        public RangeChannel channel;

        /// <summary>Optional semantic payload validated against <see cref="channel"/>.</summary>
        public object payload;

        /// <summary>Returns true if this range has associated tags.</summary>
        public bool HasMarkup => openStart >= 0;
        /// <summary>Returns true if this is a self-closing tag with inserted content.</summary>
        public bool IsSelfClosing => insertString != null;
        /// <summary>A removable content pair — tagged and not self-closing (what gets stripped, matched, and chromed).</summary>
        public bool IsRemovableMarkup => HasMarkup && !IsSelfClosing;

        /// <summary>Creates a simple range without tag information.</summary>
        public ParsedRange(int start, int end, string parameter)
        {
            this.start = start;
            this.end = end;
            openStart = -1;
            openEnd = -1;
            closeStart = -1;
            closeEnd = -1;
            this.parameter = parameter;
            label = null;
            defaultParameter = null;
            parameterMode = ModifierParameterMode.Positional;
            insertString = null;
            sourceRule = null;
            matchKey = null;
            channel = null;
            payload = null;
        }

        /// <summary>Creates a range with opening and closing tag positions.</summary>
        public ParsedRange(int openStart, int openEnd, int closeStart, int closeEnd, string parameter = null)
        {
            this.openStart = openStart;
            this.openEnd = openEnd;
            this.closeStart = closeStart;
            this.closeEnd = closeEnd;
            this.parameter = parameter;
            label = null;
            defaultParameter = null;
            parameterMode = ModifierParameterMode.Positional;
            insertString = null;
            start = openEnd;
            end = closeStart;
            sourceRule = null;
            matchKey = null;
            channel = null;
            payload = null;
        }

        /// <summary>Creates a self-closing tag range that inserts content.</summary>
        public static ParsedRange SelfClosing(int openStart, int openEnd, string insertString,
            string parameter = null, string label = null)
        {
            return new ParsedRange
            {
                openStart = openStart,
                openEnd = openEnd,
                closeStart = openEnd,
                closeEnd = openEnd,
                start = openStart,
                end = openStart,
                parameter = parameter,
                label = label,
                defaultParameter = null,
                parameterMode = ModifierParameterMode.Positional,
                insertString = insertString,
                matchKey = null,
                channel = null,
                payload = null
            };
        }

        internal void SetDefaultParameter(string value)
        {
            defaultParameter = value;
        }

        internal void SetOpaquePrimary(string value)
        {
            parameterMode = ModifierParameterMode.OpaquePrimary;
            defaultParameter = value;
        }
    }
}
