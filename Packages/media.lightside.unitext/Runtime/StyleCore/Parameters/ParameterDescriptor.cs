using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Deterministic operation combining an owned value with a parameter's cascade result.</summary>
    public enum ParameterComposition : byte
    {
        /// <summary>The owned value fully replaces the cascade result; highest priority wins.</summary>
        Replace,
        /// <summary>Owned values add to the cascade result in stable order.</summary>
        Add,
        /// <summary>Owned values multiply the cascade result in stable order.</summary>
        Multiply,
        /// <summary>The descriptor's declared custom operation.</summary>
        Custom,
    }

    /// <summary>Composition capabilities a parameter supports.</summary>
    [Flags]
    public enum ParameterCompositions : byte
    {
        None = 0,
        Replace = 1 << 0,
        Add = 1 << 1,
        Multiply = 1 << 2,
        Custom = 1 << 3,
    }

    /// <summary>Parses one markup token into a parameter value; false leaves the cascade at the next stage.</summary>
    public delegate bool ParameterTokenParser<TValue>(ReadOnlySpan<char> token, out TValue value);

    /// <summary>
    /// One modifier parameter: the unit through which markup tokens, rule defaults, the modifier
    /// field, and per-range ownership resolve to a single value.
    /// </summary>
    /// <remarks>
    /// A descriptor is a static identity declared once per (modifier type, parameter) in the
    /// modifier's nested <c>Param</c> class and aggregated by <see cref="BaseModifier.Descriptors"/>.
    /// The cascade resolves, weakest first: the modifier field, the rule's default token, the
    /// explicit markup token, and finally owned values composed on top. Its invalidation is the
    /// field's own declared change notification, so a write through any stage raises exactly what
    /// a field write raises.
    /// </remarks>
    public abstract class ParameterDescriptor
    {
        /// <summary>Stable identifier persisted by rule bindings — the backing field name.</summary>
        public string Id { get; }

        /// <summary>Human-readable name for editor surfaces.</summary>
        public string DisplayName { get; }

        /// <summary>Positional markup slot, or -1 when the parameter has no markup presence.</summary>
        public int Slot { get; }

        /// <summary>Concrete value type.</summary>
        public abstract Type ValueType { get; }

        /// <summary>Modifier type declaring this parameter.</summary>
        public abstract Type ModifierType { get; }

        /// <summary>Operations owned values may compose with.</summary>
        public ParameterCompositions SupportedCompositions { get; private protected set; }

        /// <summary>Shared empty descriptor set.</summary>
        public static readonly ParameterDescriptor[] None = Array.Empty<ParameterDescriptor>();

        private protected ParameterDescriptor(string id, int slot)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("Parameter id is empty.", nameof(id));
            Id = id;
            Slot = slot;
            DisplayName = ToDisplayName(id);
        }

        /// <summary>Raises the owning modifier's declared invalidation for this parameter.</summary>
        public abstract void Invalidate(BaseModifier modifier);

        /// <summary>Concatenates an inherited descriptor set with a declaring type's own, inherited first.</summary>
        public static ParameterDescriptor[] Concat(ParameterDescriptor[] inherited,
            ParameterDescriptor[] own)
        {
            if (inherited == null || inherited.Length == 0) return own ?? None;
            if (own == null || own.Length == 0) return inherited;
            var result = new ParameterDescriptor[inherited.Length + own.Length];
            Array.Copy(inherited, result, inherited.Length);
            Array.Copy(own, 0, result, inherited.Length, own.Length);
            return result;
        }

        /// <summary>Finds a descriptor by <see cref="Id"/> in a modifier's declared set, or null.</summary>
        public static ParameterDescriptor Find(BaseModifier modifier, string id)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (string.IsNullOrEmpty(id)) return null;
            var set = modifier.Descriptors;
            for (var i = 0; i < set.Length; i++)
                if (string.Equals(set[i].Id, id, StringComparison.Ordinal))
                    return set[i];
            return null;
        }

        /// <summary>
        /// Creates a slotted descriptor: the markup slot is the accessor's position among
        /// <paramref name="marked"/> parameter accessors, reordered to the markup contract —
        /// own-type fields first, then base-type fields.
        /// </summary>
        public static ParameterDescriptor<TModifier, TValue> From<TModifier, TValue>(
            StateAccessor[] marked, StateAccessor<TModifier, TValue> accessor,
            Action<TModifier> invalidate = null)
            where TModifier : BaseModifier
            => new(accessor, SlotOf(marked, accessor), invalidate);

        /// <summary>Creates a slotless descriptor for a parameter with no markup presence.</summary>
        public static ParameterDescriptor<TModifier, TValue> From<TModifier, TValue>(
            StateAccessor<TModifier, TValue> accessor, Action<TModifier> invalidate = null)
            where TModifier : BaseModifier
            => new(accessor, -1, invalidate);

        /// <summary>
        /// Creates a descriptor over explicit delegates, for parameters whose root lives in
        /// nested state rather than a direct field of the modifier.
        /// </summary>
        public static ParameterDescriptor<TModifier, TValue> Custom<TModifier, TValue>(
            string id, Func<TModifier, TValue> getRoot, Action<TModifier, TValue> setRoot,
            Action<TModifier> invalidate, int slot = -1)
            where TModifier : BaseModifier
            => new(id, slot, getRoot, setRoot, invalidate);

        /// <summary>Creates a slotted descriptor for an enum parameter, parsed by member name or unique initial.</summary>
        public static ParameterDescriptor<TModifier, TValue> FromEnum<TModifier, TValue>(
            StateAccessor[] marked, StateAccessor<TModifier, TValue> accessor,
            Action<TModifier> invalidate = null)
            where TModifier : BaseModifier
            where TValue : struct, Enum
            => new(accessor, SlotOf(marked, accessor), invalidate, new EnumParameterOps<TValue>());

        /// <summary>Creates a slotless descriptor for an enum parameter with no markup presence.</summary>
        public static ParameterDescriptor<TModifier, TValue> FromEnum<TModifier, TValue>(
            StateAccessor<TModifier, TValue> accessor, Action<TModifier> invalidate = null)
            where TModifier : BaseModifier
            where TValue : struct, Enum
            => new(accessor, -1, invalidate, new EnumParameterOps<TValue>());

        /// <summary>
        /// Positional markup slot of this parameter on <paramref name="modifier"/>'s concrete
        /// type. A subclass's own parameters precede inherited ones in the markup contract, so a
        /// base-declared parameter sits later on a subclass range than <see cref="Slot"/> says.
        /// </summary>
        public int SlotOn(BaseModifier modifier)
        {
            if (Slot < 0 || modifier == null || modifier.GetType() == ModifierType ||
                modifier is not IStateAccessSource source)
                return Slot;
            var marked = source.MarkedStateAccessors;
            var slot = 0;
            var found = false;
            var ownerDepth = Depth(ModifierType);
            for (var i = 0; i < marked.Length; i++)
            {
                var accessor = marked[i];
                if (accessor.OwnerType == ModifierType &&
                    string.Equals(accessor.Name, Id, StringComparison.Ordinal))
                {
                    found = true;
                    continue;
                }
                var depth = Depth(accessor.OwnerType);
                if (depth > ownerDepth || (depth == ownerDepth && !found)) slot++;
            }
            return found ? slot : Slot;
        }

        /// <summary>
        /// Returns whether the range's raw markup token at this parameter's slot equals
        /// <paramref name="token"/> (ordinal); an empty expected token matches an absent slot.
        /// </summary>
        internal bool RawTokenMatches(BaseModifier modifier, in ModifierParameters parameters,
            string token)
        {
            var slot = SlotOn(modifier);
            if (slot < 0) return false;
            var reader = parameters.GetReader();
            for (var i = 0; i < slot; i++)
                if (!reader.Next(out _))
                    return string.IsNullOrEmpty(token);
            if (!reader.Next(out var current) || current.IsEmpty)
                return string.IsNullOrEmpty(token);
            return current.SequenceEqual((token ?? string.Empty).AsSpan());
        }

        internal virtual bool TokenMatches(BaseModifier modifier, in ModifierParameters parameters,
            object expected)
            => false;

        internal abstract IRangeRuleOutput Bind(UniTextRanges runtime,
            RangeRuleInstance instance, BaseModifier modifier, ParameterRule definition,
            object targetValue);

        internal abstract IParameterDrive CreateDrive(in RangeQuery query, object from, object to,
            ParameterComposition composition, int priority);

        /// <summary>Interpolates two boxed values of this parameter's type; a type mismatch returns <paramref name="from"/>.</summary>
        internal abstract object LerpBoxed(object from, object to, float t);

        /// <summary>Resolves this parameter on one materialized range and formats the value for
        /// diagnostic surfaces, or returns null when the range's modifier is of another type.</summary>
        internal abstract string DescribeOn(in ModifierRange range);

        private static int SlotOf(StateAccessor[] marked, StateAccessor accessor)
        {
            if (marked == null) throw new ArgumentNullException(nameof(marked));
            var slot = 0;
            var found = false;
            var ownerDepth = Depth(accessor.OwnerType);
            for (var i = 0; i < marked.Length; i++)
            {
                if (ReferenceEquals(marked[i], accessor))
                {
                    found = true;
                    continue;
                }
                var depth = Depth(marked[i].OwnerType);
                if (depth > ownerDepth || (depth == ownerDepth && !found)) slot++;
            }
            if (!found)
                throw new ArgumentException(
                    $"{accessor} is not part of the marked accessor set.", nameof(accessor));
            return slot;
        }

        private static int Depth(Type type)
        {
            var depth = 0;
            for (var current = type; current != null; current = current.BaseType) depth++;
            return depth;
        }

        private static string ToDisplayName(string id)
        {
            Span<char> buffer = stackalloc char[id.Length * 2];
            var length = 0;
            for (var i = 0; i < id.Length; i++)
            {
                var c = id[i];
                if (i == 0)
                {
                    buffer[length++] = char.ToUpperInvariant(c);
                    continue;
                }
                if (char.IsUpper(c) && !char.IsUpper(id[i - 1])) buffer[length++] = ' ';
                buffer[length++] = c;
            }
            return new string(buffer[..length]);
        }
    }

    /// <summary>Typed descriptor of one <typeparamref name="TModifier"/> parameter.</summary>
    public sealed class ParameterDescriptor<TModifier, TValue> : ParameterDescriptor
        where TModifier : BaseModifier
    {
        private readonly Func<TModifier, TValue> getRoot;
        private readonly Action<TModifier, TValue> setRoot;
        private readonly Action<TModifier> invalidate;
        private IParameterOps<TValue> ops;
        private Func<TValue, TValue, TValue> customComposer;
        private TValue customIdentity;
        private ParameterTokenParser<TValue> tokenParser;

        /// <inheritdoc/>
        public override Type ValueType => typeof(TValue);

        /// <inheritdoc/>
        public override Type ModifierType => typeof(TModifier);

        internal ParameterDescriptor(StateAccessor<TModifier, TValue> accessor, int slot,
            Action<TModifier> invalidate, IParameterOps<TValue> ops = null)
            : this(accessor?.Name ?? throw new ArgumentNullException(nameof(accessor)), slot,
                accessor.Get, accessor.Set,
                invalidate ?? (accessor.CanInvalidate ? accessor.Invalidate : null), ops)
        {
        }

        internal ParameterDescriptor(string id, int slot, Func<TModifier, TValue> getRoot,
            Action<TModifier, TValue> setRoot, Action<TModifier> invalidate,
            IParameterOps<TValue> ops = null)
            : base(id, slot)
        {
            this.getRoot = getRoot ?? throw new ArgumentNullException(nameof(getRoot));
            this.setRoot = setRoot ?? throw new ArgumentNullException(nameof(setRoot));
            this.invalidate = invalidate ?? throw new InvalidOperationException(
                $"Parameter {typeof(TModifier).Name}.{id} declares no standalone invalidation; pass one explicitly.");
            this.ops = ops ?? ParameterOps.For<TValue>();
            SupportedCompositions = CompositionsOf(this.ops);
        }

        /// <summary>Declares the custom composition operation and its neutral element.</summary>
        public ParameterDescriptor<TModifier, TValue> WithCustom(
            Func<TValue, TValue, TValue> composer, TValue identity = default)
        {
            customComposer = composer ?? throw new ArgumentNullException(nameof(composer));
            customIdentity = identity;
            SupportedCompositions |= ParameterCompositions.Custom;
            return this;
        }

        /// <summary>
        /// Replaces the value type's default parsing, interpolation, and arithmetic contract while
        /// preserving any declared parser and custom composition.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="operations"/> is null.</exception>
        public ParameterDescriptor<TModifier, TValue> WithOperations(
            IParameterOps<TValue> operations)
        {
            if (operations == null) throw new ArgumentNullException(nameof(operations));
            var compositions = CompositionsOf(operations);
            if (customComposer != null) compositions |= ParameterCompositions.Custom;
            ops = operations;
            SupportedCompositions = compositions;
            return this;
        }

        /// <summary>Declares this parameter's own markup-token vocabulary in place of the type's default parsing.</summary>
        public ParameterDescriptor<TModifier, TValue> WithParser(ParameterTokenParser<TValue> parser)
        {
            tokenParser = parser ?? throw new ArgumentNullException(nameof(parser));
            return this;
        }

        /// <summary>Reads the root (field) value.</summary>
        public TValue ReadRoot(TModifier modifier)
            => getRoot(modifier ?? throw new ArgumentNullException(nameof(modifier)));

        /// <summary>Writes the root (field) value through the modifier's own transition.</summary>
        public void SetRoot(TModifier modifier, TValue value)
            => setRoot(modifier ?? throw new ArgumentNullException(nameof(modifier)), value);

        /// <inheritdoc/>
        public override void Invalidate(BaseModifier modifier)
        {
            if (modifier is not TModifier typed)
                throw new ArgumentException(
                    $"{modifier?.GetType().Name ?? "null"} is not a {typeof(TModifier).Name}.",
                    nameof(modifier));
            invalidate(typed);
        }

        internal override object LerpBoxed(object from, object to, float t)
            => from is TValue typedFrom && to is TValue typedTo
                ? Lerp(typedFrom, typedTo, t)
                : from;

        internal override IParameterDrive CreateDrive(in RangeQuery query, object from, object to,
            ParameterComposition composition, int priority)
        {
            if (from is not TValue typedFrom || to is not TValue typedTo)
                throw new ArgumentException(
                    $"Drive endpoints for {typeof(TModifier).Name}.{Id} must be of type " +
                    $"{typeof(TValue).Name}.");
            var set = query.Own(this, composition, priority);
            return new ParameterDrive<TModifier, TValue>(set, this, typedFrom, typedTo);
        }

        internal override bool TokenMatches(BaseModifier modifier, in ModifierParameters parameters,
            object expected)
            => expected is TValue typed &&
               TryParseAt(modifier, in parameters, default, out var parsed) &&
               ValueEquality<TValue>.Same(parsed, typed);

        internal override IRangeRuleOutput Bind(UniTextRanges runtime,
            RangeRuleInstance instance, BaseModifier modifier, ParameterRule definition,
            object targetValue)
            => new ParameterRuleOutput<TModifier, TValue>(runtime, instance,
                (TModifier)modifier, this, definition, (TValue)targetValue);

        /// <summary>
        /// Resolves the full cascade for one applied range: explicit markup token, rule default,
        /// modifier field — then owned values composed on top.
        /// </summary>
        public TValue Resolve(TModifier modifier, in RangeApplyContext context)
        {
            var cascade = ResolveCascade(modifier, in context);
            return ApplyOwned(modifier, context.Identity, context.Segment.Id, cascade);
        }

        /// <summary>Resolves markup token, rule default and field — without owned values.</summary>
        public TValue ResolveCascade(TModifier modifier, in RangeApplyContext context)
            => CascadeFrom(modifier, context.Parameters);

        internal TValue CascadeFrom(TModifier modifier, in ModifierParameters parameters)
        {
            TryParseAt(modifier, in parameters, getRoot(modifier), out var value);
            return value;
        }

        /// <summary>
        /// Parses this parameter's own markup slot; false leaves <paramref name="value"/> at
        /// <paramref name="fallback"/> — the slot is absent, unreachable, or unparsable.
        /// </summary>
        private bool TryParseAt(BaseModifier modifier, in ModifierParameters parameters,
            TValue fallback, out TValue value)
        {
            value = fallback;
            var slot = SlotOn(modifier);
            if (slot < 0) return false;
            var reader = parameters.GetReader();
            for (var i = 0; i < slot; i++)
                if (!reader.Next(out _))
                    return false;
            return TryParseNext(ref reader, fallback, out value);
        }

        private bool TryParseNext(ref ParameterReader reader, TValue fallback, out TValue value)
        {
            if (tokenParser == null) return ops.TryParse(ref reader, fallback, out value);
            value = fallback;
            if (!reader.Next(out var token) || token.IsEmpty ||
                !tokenParser(token, out var parsed)) return false;
            value = parsed;
            return true;
        }

        internal override string DescribeOn(in ModifierRange range)
        {
            if (range.Modifier is not TModifier typed) return null;
            var value = UniTextRanges.ResolveFor(typed.Owner, typed, this, range.Identity,
                range.Segment, CascadeFrom(typed, range.Parameters));
            return value?.ToString() ?? "null";
        }

        /// <summary>Composes the owned values of one range on top of its cascade result.</summary>
        public TValue ApplyOwned(TModifier modifier, RangeIdentity entity, RangeSegmentId segment,
            TValue cascade)
            => UniTextRanges.ResolveFor(modifier.Owner, modifier, this, entity, segment, cascade);

        /// <summary>
        /// Resolves one slot at the reader's current position — for parsers whose token stream
        /// carries a prefix the fixed slot index cannot express. Advances the reader by one slot.
        /// </summary>
        public TValue ResolveNext(ref ParameterReader reader, TModifier modifier,
            in RangeApplyContext context)
        {
            TryParseNext(ref reader, ReadRoot(modifier), out var cascade);
            return ApplyOwned(modifier, context.Identity, context.Segment.Id, cascade);
        }

        /// <summary>
        /// Resolves like <see cref="Resolve"/> and reports whether the value was set for the range —
        /// by a markup token, a rule default, or ownership — rather than falling back to the root.
        /// </summary>
        public bool TryResolve(TModifier modifier, in RangeApplyContext context, out TValue value)
        {
            var explicitToken = TryParseAt(modifier, context.Parameters, ReadRoot(modifier),
                out var cascade);
            value = ApplyOwned(modifier, context.Identity, context.Segment.Id, cascade);
            return explicitToken ||
                   UniTextRanges.HasOwned(modifier.Owner, modifier, this,
                       context.Identity, context.Segment.Id);
        }

        /// <summary>Interpolates between two values under this parameter's type contract.</summary>
        public TValue Lerp(TValue from, TValue to, float t) => ops.Lerp(from, to, Mathf.Clamp01(t));

        /// <summary>Combines one owned value with the running result, or throws when unsupported.</summary>
        public TValue Compose(TValue current, TValue owned, ParameterComposition composition)
        {
            switch (composition)
            {
                case ParameterComposition.Replace:
                    return owned;
                case ParameterComposition.Add when ops.CanAdd:
                    return ops.Add(current, owned);
                case ParameterComposition.Multiply when ops.CanMultiply:
                    return ops.Multiply(current, owned);
                case ParameterComposition.Custom when customComposer != null:
                    return customComposer(current, owned);
                default:
                    throw new NotSupportedException(
                        $"Parameter {typeof(TModifier).Name}.{Id} does not support {composition}.");
            }
        }

        /// <summary>Returns the neutral owned value for a composition, or throws when unsupported.</summary>
        public TValue Identity(ParameterComposition composition)
            => composition switch
            {
                ParameterComposition.Replace => default,
                ParameterComposition.Add when ops.CanAdd => ops.AddIdentity,
                ParameterComposition.Multiply when ops.CanMultiply => ops.MultiplyIdentity,
                ParameterComposition.Custom when customComposer != null => customIdentity,
                _ => throw new NotSupportedException(
                    $"Parameter {typeof(TModifier).Name}.{Id} has no {composition} identity."),
            };

        private static ParameterCompositions CompositionsOf(IParameterOps<TValue> operations)
            => ParameterCompositions.Replace |
               (operations.CanAdd ? ParameterCompositions.Add : ParameterCompositions.None) |
               (operations.CanMultiply
                   ? ParameterCompositions.Multiply
                   : ParameterCompositions.None);
    }

    /// <summary>Stable typed modifier-node reference used by code-first rule lookup.</summary>
    public readonly struct ModifierKey<TModifier> : IEquatable<ModifierKey<TModifier>>
        where TModifier : BaseModifier
    {
        internal ModifierNodeId NodeId { get; }

        /// <summary>Addresses the supplied modifier within the rule owner's graph.</summary>
        public ModifierKey(TModifier modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            NodeId = modifier.NodeId;
        }

        /// <inheritdoc/>
        public bool Equals(ModifierKey<TModifier> other) => NodeId == other.NodeId;

        /// <inheritdoc/>
        public override bool Equals(object obj)
            => obj is ModifierKey<TModifier> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => NodeId.GetHashCode();

        /// <summary>Compares two typed keys.</summary>
        public static bool operator ==(ModifierKey<TModifier> left, ModifierKey<TModifier> right)
            => left.Equals(right);

        /// <summary>Compares two typed keys.</summary>
        public static bool operator !=(ModifierKey<TModifier> left, ModifierKey<TModifier> right)
            => !left.Equals(right);
    }

    internal sealed class EnumParameterOps<TValue> : IParameterOps<TValue>
        where TValue : struct, Enum
    {
        public bool CanAdd => false;
        public bool CanMultiply => false;
        public TValue AddIdentity => default;
        public TValue MultiplyIdentity => default;
        public TValue Lerp(TValue from, TValue to, float t) => t < 1f ? from : to;
        public TValue Add(TValue current, TValue value) => throw new NotSupportedException();
        public TValue Multiply(TValue current, TValue value) => throw new NotSupportedException();

        public bool TryParse(ref ParameterReader reader, TValue fallback, out TValue value)
            => reader.NextEnum(out value, fallback);
    }

    /// <summary>
    /// Provides one parameter value type's parsing, interpolation, and arithmetic contract;
    /// reported capabilities remain stable for the provider's lifetime.
    /// </summary>
    public interface IParameterOps<TValue>
    {
        /// <summary>Whether additive composition and its identity are supported.</summary>
        bool CanAdd { get; }

        /// <summary>Whether multiplicative composition and its identity are supported.</summary>
        bool CanMultiply { get; }

        /// <summary>
        /// Owned value that leaves the current value unchanged under <see cref="Add"/> when
        /// <see cref="CanAdd"/> is true.
        /// </summary>
        TValue AddIdentity { get; }

        /// <summary>
        /// Owned value that leaves the current value unchanged under <see cref="Multiply"/> when
        /// <see cref="CanMultiply"/> is true.
        /// </summary>
        TValue MultiplyIdentity { get; }

        /// <summary>
        /// Interpolates from <paramref name="from"/> to <paramref name="to"/> at normalized
        /// progress, with zero selecting the former and one selecting the latter.
        /// </summary>
        TValue Lerp(TValue from, TValue to, float t);

        /// <summary>Combines the current and owned values when <see cref="CanAdd"/> is true.</summary>
        TValue Add(TValue current, TValue value);

        /// <summary>Combines the current and owned values when <see cref="CanMultiply"/> is true.</summary>
        TValue Multiply(TValue current, TValue value);

        /// <summary>
        /// Attempts to parse one value, returning <paramref name="fallback"/> through
        /// <paramref name="value"/> on failure.
        /// </summary>
        bool TryParse(ref ParameterReader reader, TValue fallback, out TValue value);
    }

    /// <summary>Per-type parse, interpolation and composition operations of the parameter cascade.</summary>
    internal static class ParameterOps
    {
        internal static IParameterOps<TValue> For<TValue>() => Table<TValue>.Ops;

        private static class Table<TValue>
        {
            internal static readonly IParameterOps<TValue> Ops = Create();

            private static IParameterOps<TValue> Create()
            {
                object ops = null;
                if (typeof(TValue) == typeof(float)) ops = new FloatOps();
                else if (typeof(TValue) == typeof(int)) ops = new IntOps();
                else if (typeof(TValue) == typeof(Vector2)) ops = new Vector2Ops();
                else if (typeof(TValue) == typeof(Color32)) ops = new Color32Ops();
                else if (typeof(TValue) == typeof(UnitValue)) ops = new UnitValueOps();
                else if (typeof(TValue) == typeof(UnitVector2)) ops = new UnitVector2Ops();
                else if (typeof(TValue) == typeof(bool)) ops = new BoolOps();
                else if (typeof(TValue) == typeof(string)) ops = new StringOps();
                else if (typeof(TValue).IsEnum)
                    ops = Activator.CreateInstance(
                        typeof(EnumParameterOps<>).MakeGenericType(typeof(TValue)));
                return ops != null ? (IParameterOps<TValue>)ops : new OpaqueOps<TValue>();
            }
        }

        private sealed class FloatOps : IParameterOps<float>
        {
            public bool CanAdd => true;
            public bool CanMultiply => true;
            public float AddIdentity => 0f;
            public float MultiplyIdentity => 1f;
            public float Lerp(float from, float to, float t) => Mathf.LerpUnclamped(from, to, t);
            public float Add(float current, float value) => current + value;
            public float Multiply(float current, float value) => current * value;
            public bool TryParse(ref ParameterReader reader, float fallback, out float value)
                => reader.NextFloat(out value, fallback);
        }

        private sealed class IntOps : IParameterOps<int>
        {
            public bool CanAdd => true;
            public bool CanMultiply => true;
            public int AddIdentity => 0;
            public int MultiplyIdentity => 1;
            public int Lerp(int from, int to, float t)
                => Mathf.RoundToInt(Mathf.LerpUnclamped(from, to, t));
            public int Add(int current, int value) => current + value;
            public int Multiply(int current, int value) => current * value;
            public bool TryParse(ref ParameterReader reader, int fallback, out int value)
                => reader.NextInt(out value, fallback);
        }

        private sealed class Vector2Ops : IParameterOps<Vector2>
        {
            public bool CanAdd => true;
            public bool CanMultiply => true;
            public Vector2 AddIdentity => Vector2.zero;
            public Vector2 MultiplyIdentity => Vector2.one;
            public Vector2 Lerp(Vector2 from, Vector2 to, float t)
                => Vector2.LerpUnclamped(from, to, t);
            public Vector2 Add(Vector2 current, Vector2 value) => current + value;
            public Vector2 Multiply(Vector2 current, Vector2 value)
                => new(current.x * value.x, current.y * value.y);
            public bool TryParse(ref ParameterReader reader, Vector2 fallback, out Vector2 value)
                => reader.NextVector2(out value, fallback);
        }

        private sealed class Color32Ops : IParameterOps<Color32>
        {
            public bool CanAdd => false;
            public bool CanMultiply => true;
            public Color32 AddIdentity => default;
            public Color32 MultiplyIdentity => new(255, 255, 255, 255);
            public Color32 Lerp(Color32 from, Color32 to, float t)
                => TextPaint.LerpColor(from, to, t);
            public Color32 Add(Color32 current, Color32 value) => throw new NotSupportedException();
            public Color32 Multiply(Color32 current, Color32 value)
                => TextPaint.MultiplyColor(current, value);
            public bool TryParse(ref ParameterReader reader, Color32 fallback, out Color32 value)
                => reader.NextColor(out value, fallback);
        }

        private sealed class UnitValueOps : IParameterOps<UnitValue>
        {
            public bool CanAdd => true;
            public bool CanMultiply => false;
            public UnitValue AddIdentity => default;
            public UnitValue MultiplyIdentity => default;

            /// <summary>Mixed units cannot interpolate; the transition steps to its target at completion.</summary>
            public UnitValue Lerp(UnitValue from, UnitValue to, float t)
                => from.unit == to.unit
                    ? new UnitValue(Mathf.LerpUnclamped(from.value, to.value, t), from.unit)
                    : t < 1f ? from : to;

            /// <summary>Mixed units cannot add; the owned value wins whole.</summary>
            public UnitValue Add(UnitValue current, UnitValue value)
                => current.unit == value.unit
                    ? new UnitValue(current.value + value.value, current.unit)
                    : value;

            public UnitValue Multiply(UnitValue current, UnitValue value)
                => throw new NotSupportedException();

            public bool TryParse(ref ParameterReader reader, UnitValue fallback, out UnitValue value)
            {
                var parsed = reader.NextUnitFloat(out var number, out var unit, fallback);
                value = new UnitValue(number, unit);
                return parsed;
            }
        }

        private sealed class UnitVector2Ops : IParameterOps<UnitVector2>
        {
            public bool CanAdd => true;
            public bool CanMultiply => false;
            public UnitVector2 AddIdentity => default;
            public UnitVector2 MultiplyIdentity => default;

            /// <summary>Mixed units cannot interpolate; the transition steps to its target at completion.</summary>
            public UnitVector2 Lerp(UnitVector2 from, UnitVector2 to, float t)
                => from.unit == to.unit
                    ? new UnitVector2(Vector2.LerpUnclamped(from.value, to.value, t), from.unit)
                    : t < 1f ? from : to;

            /// <summary>Mixed units cannot add; the owned value wins whole.</summary>
            public UnitVector2 Add(UnitVector2 current, UnitVector2 value)
                => current.unit == value.unit
                    ? new UnitVector2(current.value + value.value, current.unit)
                    : value;

            public UnitVector2 Multiply(UnitVector2 current, UnitVector2 value)
                => throw new NotSupportedException();

            public bool TryParse(ref ParameterReader reader, UnitVector2 fallback,
                out UnitVector2 value)
            {
                var parsed = reader.NextUnitVector2(out var vector, out var unit, fallback);
                value = new UnitVector2(vector, unit);
                return parsed;
            }
        }

        /// <summary>Replace-only contract of types without arithmetic: parsing exists only through a declared parser.</summary>
        private sealed class OpaqueOps<TValue> : IParameterOps<TValue>
        {
            public bool CanAdd => false;
            public bool CanMultiply => false;
            public TValue AddIdentity => default;
            public TValue MultiplyIdentity => default;
            public TValue Lerp(TValue from, TValue to, float t) => t < 1f ? from : to;
            public TValue Add(TValue current, TValue value) => throw new NotSupportedException();
            public TValue Multiply(TValue current, TValue value) => throw new NotSupportedException();

            public bool TryParse(ref ParameterReader reader, TValue fallback, out TValue value)
            {
                value = fallback;
                return false;
            }
        }

        private sealed class BoolOps : IParameterOps<bool>
        {
            public bool CanAdd => false;
            public bool CanMultiply => false;
            public bool AddIdentity => false;
            public bool MultiplyIdentity => false;
            public bool Lerp(bool from, bool to, float t) => t < 1f ? from : to;
            public bool Add(bool current, bool value) => throw new NotSupportedException();
            public bool Multiply(bool current, bool value) => throw new NotSupportedException();

            public bool TryParse(ref ParameterReader reader, bool fallback, out bool value)
            {
                value = fallback;
                return reader.Next(out var token) && !token.IsEmpty &&
                       ParameterTokenExtensions.TryParseBool(token, out value);
            }
        }

        private sealed class StringOps : IParameterOps<string>
        {
            public bool CanAdd => false;
            public bool CanMultiply => false;
            public string AddIdentity => null;
            public string MultiplyIdentity => null;
            public string Lerp(string from, string to, float t) => t < 1f ? from : to;
            public string Add(string current, string value) => throw new NotSupportedException();
            public string Multiply(string current, string value) => throw new NotSupportedException();

            public bool TryParse(ref ParameterReader reader, string fallback, out string value)
            {
                value = fallback;
                if (!reader.Next(out var token) || token.IsEmpty) return false;
                value = SpanIntern.Get(token);
                return true;
            }
        }
    }
}
