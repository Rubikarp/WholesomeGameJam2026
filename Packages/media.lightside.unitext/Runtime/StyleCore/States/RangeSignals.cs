using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>Stable non-generic identity for one project or built-in range signal.</summary>
    public abstract class RangeSignal : IEquatable<RangeSignal>
    {
        /// <summary>Stable namespaced identifier.</summary>
        public string Id { get; }
        /// <summary>Signal value type.</summary>
        public abstract Type ValueType { get; }

        internal RangeSignal(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Signal identifier is empty.", nameof(id));
            Id = id;
        }

        /// <inheritdoc/>
        public bool Equals(RangeSignal other)
            => other != null && ValueType == other.ValueType &&
               string.Equals(Id, other.Id, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RangeSignal other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(ValueType, Id);
    }

    /// <summary>Typed stable range signal usable by external producers and selectors.</summary>
    public sealed class RangeSignal<T> : RangeSignal
    {
        internal readonly Func<RangeSignalState, T> getter;
        internal readonly Func<RangeSignalState, T, bool> setter;

        /// <inheritdoc/>
        public override Type ValueType => typeof(T);
        /// <summary>Value returned before a producer writes this signal.</summary>
        public T DefaultValue { get; }

        /// <summary>Creates a namespaced typed signal.</summary>
        public RangeSignal(string id, T defaultValue = default) : base(id)
        {
            if (id.StartsWith("unitext.", StringComparison.Ordinal))
                throw new ArgumentException(
                    "The 'unitext.' signal namespace is reserved for RangeSignals.", nameof(id));
            DefaultValue = defaultValue;
        }

        internal RangeSignal(string id, T defaultValue, Func<RangeSignalState, T> getter,
            Func<RangeSignalState, T, bool> setter) : base(id)
        {
            DefaultValue = defaultValue;
            this.getter = getter;
            this.setter = setter;
        }
    }

    /// <summary>Built-in signals. Project code may declare additional static <see cref="RangeSignal{T}"/> values.</summary>
    public static class RangeSignals
    {
        /// <summary>At least one pointer rests over the entity.</summary>
        public static readonly RangeSignal<bool> Hovered = new("unitext.interaction.hovered", false,
            state => state.hovered, (state, value) => RangeSignalState.SetBool(ref state.hovered, value));
        /// <summary>At least one pointer owns an uncanceled press lease.</summary>
        public static readonly RangeSignal<bool> Pressed = new("unitext.interaction.pressed", false,
            state => state.pressed, (state, value) => RangeSignalState.SetBool(ref state.pressed, value));
        /// <summary>Keyboard or gamepad navigation addresses the entity.</summary>
        public static readonly RangeSignal<bool> Focused = new("unitext.interaction.focused", false,
            state => state.focused, (state, value) => RangeSignalState.SetBool(ref state.focused, value));
        /// <summary>The entity is present but rejects interaction.</summary>
        public static readonly RangeSignal<bool> Disabled = new("unitext.interaction.disabled", false,
            state => state.disabled, (state, value) => RangeSignalState.SetBool(ref state.disabled, value));
        /// <summary>Normalized touch/pen hold progress.</summary>
        public static readonly RangeSignal<float> LongPressProgress = new(
            "unitext.interaction.longPressProgress", 0f, state => state.longPressProgress,
            (state, value) => RangeSignalState.SetFloat(ref state.longPressProgress, value));
        /// <summary>Document selection includes the entity.</summary>
        public static readonly RangeSignal<bool> Selected = new("unitext.document.selected", false,
            state => state.selected, (state, value) => RangeSignalState.SetBool(ref state.selected, value));
        /// <summary>The entity is the current item in an application-defined sequence.</summary>
        public static readonly RangeSignal<bool> Current = new("unitext.application.current", false,
            state => state.current, (state, value) => RangeSignalState.SetBool(ref state.current, value));
        /// <summary>The entity has previously been visited.</summary>
        public static readonly RangeSignal<bool> Visited = new("unitext.application.visited", false,
            state => state.visited, (state, value) => RangeSignalState.SetBool(ref state.visited, value));
        /// <summary>Normalized asynchronous loading progress.</summary>
        public static readonly RangeSignal<float> LoadingProgress = new(
            "unitext.application.loadingProgress", 0f, state => state.loadingProgress,
            (state, value) => RangeSignalState.SetFloat(ref state.loadingProgress, value));
        /// <summary>Normalized project-defined drag progress.</summary>
        public static readonly RangeSignal<float> DragProgress = new("unitext.gesture.dragProgress", 0f,
            state => state.dragProgress,
            (state, value) => RangeSignalState.SetFloat(ref state.dragProgress, value));
        /// <summary>Normalized voice or karaoke playback progress.</summary>
        public static readonly RangeSignal<float> VoiceProgress = new("unitext.media.voiceProgress", 0f,
            state => state.voiceProgress,
            (state, value) => RangeSignalState.SetFloat(ref state.voiceProgress, value));
    }

    internal sealed class RangeSignalState
    {
        private interface ISignalSlot { }

        private sealed class SignalSlot<T> : ISignalSlot
        {
            public T value;

            public SignalSlot(T value) => this.value = value;
        }

        public bool hovered;
        public bool pressed;
        public bool focused;
        public bool disabled;
        public bool selected;
        public bool current;
        public bool visited;
        public float longPressProgress;
        public float loadingProgress;
        public float dragProgress;
        public float voiceProgress;
        private Dictionary<RangeSignal, ISignalSlot> custom;

        public T Get<T>(RangeSignal<T> signal)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            if (signal.getter != null) return signal.getter(this);
            if (custom != null && custom.TryGetValue(signal, out var value))
                return ((SignalSlot<T>)value).value;
            return signal.DefaultValue;
        }

        public bool Set<T>(RangeSignal<T> signal, T value)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            if (signal.setter != null) return signal.setter(this, value);
            custom ??= new Dictionary<RangeSignal, ISignalSlot>();
            if (custom.TryGetValue(signal, out var previous))
            {
                var slot = (SignalSlot<T>)previous;
                if (ValueEquality<T>.Same(slot.value, value)) return false;
                slot.value = value;
                return true;
            }
            custom[signal] = new SignalSlot<T>(value);
            return true;
        }

        internal static bool SetBool(ref bool field, bool value)
        {
            if (field == value) return false;
            field = value;
            return true;
        }

        internal static bool SetFloat(ref float field, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value)) return false;
            field = value;
            return true;
        }
    }

    /// <summary>Read-only typed view of every signal currently published for one entity.</summary>
    public readonly struct RangeSignalReader
    {
        private readonly RangeSignalState state;

        internal RangeSignalReader(RangeSignalState state) => this.state = state;

        /// <summary>Reads a signal or its declared default when no producer has written it.</summary>
        public T Get<T>(RangeSignal<T> signal)
        {
            if (signal == null) throw new ArgumentNullException(nameof(signal));
            return state == null ? signal.DefaultValue : state.Get(signal);
        }
    }

    /// <summary>Serialized or code-first predicate over an entity's current typed signals.</summary>
    [Serializable]
    public abstract class RangeStateSelector : RangeConfigurationObject
    {
        /// <summary>Evaluates the complete current signal set without mutating it.</summary>
        protected internal abstract bool Matches(in RangeSignalReader signals);
    }

    /// <summary>
    /// Code-first typed selector for project-defined boolean, enum, scalar or immutable value
    /// signals. Inspector-authored selectors use concrete serializable subclasses.
    /// </summary>
    [HideFromTypeSelector]
    public sealed class RangeSignalSelector<T> : RangeStateSelector
    {
        [NonSerialized] private readonly RangeSignal<T> signal;
        [NonSerialized] private readonly Func<T, bool> predicate;

        /// <summary>Creates a selector without registering the signal in a global string table.</summary>
        public RangeSignalSelector(RangeSignal<T> signal, Func<T, bool> predicate)
        {
            this.signal = signal ?? throw new ArgumentNullException(nameof(signal));
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
            => predicate(signals.Get(signal));
    }

    /// <summary>Built-in scalar signals selectable from serialized rule graphs.</summary>
    public enum BuiltInScalarSignal : byte
    {
        /// <summary>Touch or pen hold progress.</summary>
        LongPressProgress,
        /// <summary>Application loading progress.</summary>
        LoadingProgress,
        /// <summary>Custom drag recognizer progress.</summary>
        DragProgress,
        /// <summary>Voice, karaoke or transcript playback progress.</summary>
        VoiceProgress,
    }

    /// <summary>Comparison used by a serialized scalar selector.</summary>
    public enum ScalarSignalComparison : byte
    {
        /// <summary>Value is strictly below the threshold.</summary>
        Less,
        /// <summary>Value is at most the threshold.</summary>
        LessOrEqual,
        /// <summary>Value is approximately the threshold.</summary>
        Equal,
        /// <summary>Value is at least the threshold.</summary>
        GreaterOrEqual,
        /// <summary>Value is strictly above the threshold.</summary>
        Greater,
    }

    /// <summary>Serializable threshold predicate for the built-in float signals.</summary>
    [Serializable]
    [TypeDescription("Scalar signal: compare progress with a threshold")]
    public sealed partial class ScalarRangeStateSelector : RangeStateSelector
    {
        /// <summary>Scalar signal being tested.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private BuiltInScalarSignal signal;
        /// <summary>Threshold comparison.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private ScalarSignalComparison comparison = ScalarSignalComparison.Greater;
        /// <summary>Comparison threshold.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private float threshold;

        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
        {
            var value = signal switch
            {
                BuiltInScalarSignal.LongPressProgress => signals.Get(RangeSignals.LongPressProgress),
                BuiltInScalarSignal.LoadingProgress => signals.Get(RangeSignals.LoadingProgress),
                BuiltInScalarSignal.DragProgress => signals.Get(RangeSignals.DragProgress),
                BuiltInScalarSignal.VoiceProgress => signals.Get(RangeSignals.VoiceProgress),
                _ => throw new ArgumentOutOfRangeException(),
            };
            return comparison switch
            {
                ScalarSignalComparison.Less => value < threshold,
                ScalarSignalComparison.LessOrEqual => value <= threshold,
                ScalarSignalComparison.Equal => Mathf.Approximately(value, threshold),
                ScalarSignalComparison.GreaterOrEqual => value >= threshold,
                ScalarSignalComparison.Greater => value > threshold,
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }

    /// <summary>Matches only when every child selector matches.</summary>
    [Serializable]
    [TypeDescription("All: logical AND of child selectors")]
    public sealed partial class AllRangeStateSelector : RangeStateSelector
    {
        /// <summary>Child predicates that must all match.</summary>
        [SerializeReference, TypeSelector, StateList(nameof(ApplySelectorsChange))] private RangeStateSelector[] selectors =
            Array.Empty<RangeStateSelector>();
        [NonSerialized] private ReferenceBinding<RangeStateSelector> subscribedSelectors;

        private void ApplySelectorsChange()
        {
            if (HasConfigurationListeners) BindSelectors();
            NotifyConfigurationChanged();
        }

        protected override void OnConfigurationBound() => BindSelectors();

        protected override void OnConfigurationUnbound() => subscribedSelectors?.Clear();

        private void BindSelectors()
        {
            subscribedSelectors ??= new ReferenceBinding<RangeStateSelector>(
                BindConfigurationChild, UnbindConfigurationChild);
            subscribedSelectors.Reconcile(selectors);
        }

        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
        {
            var items = Selectors;
            for (var i = 0; i < items.Count; i++)
            {
                var selector = items[i] ?? throw new InvalidOperationException(
                    $"All selector child {i} is null.");
                if (!selector.Matches(in signals)) return false;
            }
            return true;
        }
    }

    /// <summary>Matches when at least one child selector matches.</summary>
    [Serializable]
    [TypeDescription("Any: logical OR of child selectors")]
    public sealed partial class AnyRangeStateSelector : RangeStateSelector
    {
        /// <summary>Child predicates of which at least one must match.</summary>
        [SerializeReference, TypeSelector, StateList(nameof(ApplySelectorsChange))] private RangeStateSelector[] selectors =
            Array.Empty<RangeStateSelector>();
        [NonSerialized] private ReferenceBinding<RangeStateSelector> subscribedSelectors;

        private void ApplySelectorsChange()
        {
            if (HasConfigurationListeners) BindSelectors();
            NotifyConfigurationChanged();
        }

        protected override void OnConfigurationBound() => BindSelectors();

        protected override void OnConfigurationUnbound() => subscribedSelectors?.Clear();

        private void BindSelectors()
        {
            subscribedSelectors ??= new ReferenceBinding<RangeStateSelector>(
                BindConfigurationChild, UnbindConfigurationChild);
            subscribedSelectors.Reconcile(selectors);
        }

        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
        {
            var items = Selectors;
            for (var i = 0; i < items.Count; i++)
            {
                var selector = items[i] ?? throw new InvalidOperationException(
                    $"Any selector child {i} is null.");
                if (selector.Matches(in signals)) return true;
            }
            return false;
        }
    }

    /// <summary>Negates one child selector.</summary>
    [Serializable]
    [TypeDescription("Not: logical negation of one selector")]
    public sealed partial class NotRangeStateSelector : RangeStateSelector
    {
        /// <summary>Predicate whose result is negated.</summary>
        [SerializeReference, TypeSelector, StateProperty(nameof(ApplySelectorChange))] private RangeStateSelector selector;

        private void ApplySelectorChange(RangeStateSelector previous, RangeStateSelector current)
            => ApplyConfigurationChildChange(previous, current);

        protected override void OnConfigurationBound() => BindConfigurationChild(selector);
        protected override void OnConfigurationUnbound() => UnbindConfigurationChild(selector);

        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
            => !(selector ?? throw new InvalidOperationException("Not selector requires a child."))
                .Matches(in signals);
    }

    /// <summary>Built-in boolean flags addressable without a custom selector subclass.</summary>
    [Flags]
    public enum InteractionSignalMask : byte
    {
        /// <summary>No condition.</summary>
        None = 0,
        /// <summary>Hovered must match.</summary>
        Hovered = 1 << 0,
        /// <summary>Pressed must match.</summary>
        Pressed = 1 << 1,
        /// <summary>Focused must match.</summary>
        Focused = 1 << 2,
        /// <summary>Disabled must match.</summary>
        Disabled = 1 << 3,
    }

    /// <summary>Matches required and excluded built-in interaction signals.</summary>
    [Serializable]
    [TypeDescription("Interaction signals: required/excluded Hovered, Pressed, Focused, Disabled")]
    public sealed partial class InteractionRangeStateSelector : RangeStateSelector
    {
        /// <summary>Signals that must all be true.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private InteractionSignalMask required;
        /// <summary>Signals that must all be false.</summary>
        [SerializeField, StateProperty(nameof(NotifyConfigurationChanged))] private InteractionSignalMask excluded;

        /// <inheritdoc/>
        protected internal override bool Matches(in RangeSignalReader signals)
        {
            var current = InteractionSignalMask.None;
            if (signals.Get(RangeSignals.Hovered)) current |= InteractionSignalMask.Hovered;
            if (signals.Get(RangeSignals.Pressed)) current |= InteractionSignalMask.Pressed;
            if (signals.Get(RangeSignals.Focused)) current |= InteractionSignalMask.Focused;
            if (signals.Get(RangeSignals.Disabled)) current |= InteractionSignalMask.Disabled;
            return (current & required) == required && (current & excluded) == 0;
        }
    }
}
