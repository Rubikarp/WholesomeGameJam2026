using System;

namespace LightSide
{
    internal enum ModifierParameterMode : byte
    {
        Positional,
        OpaquePrimary
    }

    /// <summary>
    /// Carries source output and source defaults separately until a concrete modifier consumes
    /// them. Composites project both layers by child without materializing a merged string.
    /// </summary>
    public readonly struct ModifierParameters
    {
        private readonly ParameterSlice explicitValue;
        private readonly ParameterSlice defaultValue;
        private readonly string label;

        private ModifierParameters(string explicitValue, string defaultValue, ModifierParameterMode mode)
            : this(new ParameterSlice(explicitValue, mode == ModifierParameterMode.OpaquePrimary),
                new ParameterSlice(defaultValue)) { }

        private ModifierParameters(ParameterSlice explicitValue, ParameterSlice defaultValue,
            string label = null)
        {
            this.explicitValue = explicitValue;
            this.defaultValue = defaultValue;
            this.label = label;
        }

        internal string Label => label;

        internal ModifierParameters WithLabel(string value)
            => new(explicitValue, defaultValue, value);

        internal static ModifierParameters Explicit(string value)
            => new(value, null, ModifierParameterMode.Positional);

        internal static ModifierParameters Default(string value)
            => new(null, value, ModifierParameterMode.Positional);

        internal static ModifierParameters Positional(string explicitValue, string defaultValue)
            => new(explicitValue, defaultValue, ModifierParameterMode.Positional);

        internal static ModifierParameters OpaquePrimary(string value, string defaultValue)
            => new(value, defaultValue, ModifierParameterMode.OpaquePrimary);

        internal string ResolvedValue
        {
            get
            {
                if (defaultValue.IsEmpty) return explicitValue.Materialize();
                if (explicitValue.IsEmpty) return defaultValue.Materialize();
                return explicitValue.IsOpaque
                    ? ParameterLayerResolver.ApplyOpaqueFirst(explicitValue.Span, defaultValue.Span)
                    : ParameterLayerResolver.Apply(explicitValue.Span, defaultValue.Span);
            }
        }

        internal bool HasExplicitTokens => !explicitValue.IsEmpty;

        internal string PrimaryValue => explicitValue.IsOpaque
            ? explicitValue.Materialize()
            : null;

        /// <summary>Reads resolved tokens using explicit values before source defaults.</summary>
        public ParameterReader GetReader() => explicitValue.IsOpaque
            ? new ParameterReader(default, defaultValue.Span)
            : new ParameterReader(explicitValue.Span, defaultValue.Span);

        internal ModifierParameters Child(int index)
        {
            if (index < 0) return default;

            if (explicitValue.IsOpaque && !explicitValue.IsEmpty)
                return new ModifierParameters(default, defaultValue.Segment(index), label);

            return new ModifierParameters(explicitValue.Segment(index),
                defaultValue.Segment(index), label);
        }

        private readonly struct ParameterSlice
        {
            private readonly string value;
            private readonly int start;
            private readonly int encodedLength;

            public ParameterSlice(string value, bool opaque = false)
            {
                this.value = value;
                start = 0;
                var length = value?.Length ?? 0;
                encodedLength = opaque ? ~length : length;
            }

            private ParameterSlice(string value, int start, int length)
            {
                this.value = value;
                this.start = start;
                encodedLength = length;
            }

            private int Length => encodedLength < 0 ? ~encodedLength : encodedLength;

            public bool IsEmpty => Length == 0;

            public bool IsOpaque => encodedLength < 0;

            public ReadOnlySpan<char> Span => value == null
                ? default
                : value.AsSpan(start, Length);

            public string Materialize()
            {
                var length = Length;
                if (length == 0) return null;
                if (start == 0 && length == value.Length) return value;
                return SpanIntern.Get(Span);
            }

            public ParameterSlice Segment(int index)
            {
                var length = Length;
                if (index < 0 || length == 0) return default;

                var remaining = Span;
                var consumed = 0;
                for (var i = 0; i <= index; i++)
                {
                    if (remaining.IsEmpty) return default;
                    var segmentLength = ParameterTokenizer.Next(remaining, ';', out var segment);
                    if (i == index)
                        return segment.IsEmpty
                            ? default
                            : new ParameterSlice(value, start + consumed, segment.Length);
                    remaining = remaining.Slice(segmentLength);
                    consumed += segmentLength;
                }

                return default;
            }
        }
    }
}
