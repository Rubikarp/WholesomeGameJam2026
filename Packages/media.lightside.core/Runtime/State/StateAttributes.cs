using System;

namespace LightSide
{
    /// <summary>
    /// Requires every Unity-serialized field declared by an implementing or derived class to state
    /// an explicit mutation or passive contract.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = true)]
    public sealed class StateHierarchyAttribute : Attribute { }

    /// <summary>
    /// Enables generated Player deserialization capture for types that support live serialized
    /// overwrites. Initial data establishes a silent baseline; later candidates are replayed on
    /// the main thread through the same transitions as public properties and state lists.
    /// Apply it once to the highest participating type that needs this behavior; generated derived
    /// state joins the same baseline. Without it, generated serialization callbacks exist only in
    /// the Editor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class StateRuntimeDeserializationAttribute : Attribute { }

    /// <summary>
    /// Generates an <see cref="IStateChangeSource"/> event for this state hierarchy. A state field
    /// with no handler name publishes its generated member through that event.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class StateSourceAttribute : Attribute { }

    /// <summary>
    /// Connects a scalar state field to its nested <see cref="IStateChangeSource"/> graph.
    /// Apply it alongside <see cref="StatePropertyAttribute"/> or <see cref="StateFieldAttribute"/>.
    /// Linked changes are forwarded through the enclosing state source or delivered to the
    /// declared handler while the graph is retained. Link graphs must be acyclic.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateLinkAttribute : Attribute
    {
        /// <summary>Forwards linked changes through the enclosing generated state source.</summary>
        public StateLinkAttribute() { }

        /// <summary>Declares the package method that receives linked changes.</summary>
        public StateLinkAttribute(string methodName) => MethodName = methodName;

        /// <summary>The compile-time-resolved handler, or null for generated forwarding.</summary>
        public string MethodName { get; }
    }

    /// <summary>
    /// Generates a public property that compares, assigns, and publishes or invokes the declared
    /// consequence for every real value transition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class StatePropertyAttribute : Attribute
    {
        /// <summary>Publishes the generated member through the hierarchy's generated state source.</summary>
        public StatePropertyAttribute() { }

        /// <summary>Declares the package method that receives real transitions of this field.</summary>
        public StatePropertyAttribute(string methodName) => MethodName = methodName;

        /// <summary>The compile-time-resolved method, or null for generated state-source publication.</summary>
        public string MethodName { get; }

        /// <summary>Names an optional pure <c>void M(T candidate)</c> method that may reject before mutation.</summary>
        public string Validator { get; set; }

        /// <summary>Declares that this field exclusively owns its referenced value.</summary>
        public bool Owned { get; set; }
    }

    /// <summary>
    /// A <see cref="StatePropertyAttribute"/> for numeric fields whose candidates the generated
    /// transition canonicalizes into the declared range before the equality gate — a write that
    /// clamps back to the current value publishes nothing.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NumberStatePropertyAttribute : StatePropertyAttribute
    {
        /// <summary>Publishes the generated member through the hierarchy's generated state source.</summary>
        public NumberStatePropertyAttribute() { }

        /// <summary>Declares the package method that receives real transitions of this field.</summary>
        public NumberStatePropertyAttribute(string methodName) : base(methodName) { }

        /// <summary>Inclusive lower bound applied to every candidate.</summary>
        public double Min { get; set; } = double.NegativeInfinity;

        /// <summary>Inclusive upper bound applied to every candidate.</summary>
        public double Max { get; set; } = double.PositiveInfinity;

        /// <summary>Clamps candidates to the normalized range (shorthand for <c>Min = 0, Max = 1</c>).</summary>
        public bool Clamp01 { get; set; }

        /// <summary>
        /// Period of a cyclic quantity: every candidate is wrapped into <c>[0, Wrap)</c> before the
        /// equality gate, so writing a full period over zero publishes nothing. Exclusive with
        /// <see cref="Min"/>, <see cref="Max"/> and <see cref="Clamp01"/>.
        /// </summary>
        public double Wrap { get; set; }
    }

    /// <summary>
    /// Generates a typed mutation method for a field exposed through a handwritten API. Real value
    /// transitions publish or invoke the declared consequence. Arrays use reference-replacement semantics;
    /// mutate authored collection contents through <see cref="StateListAttribute"/> instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateFieldAttribute : Attribute
    {
        /// <summary>Publishes the generated member through the hierarchy's generated state source.</summary>
        public StateFieldAttribute() { }

        /// <summary>Declares the package method that receives real transitions of this field.</summary>
        public StateFieldAttribute(string methodName) => MethodName = methodName;

        /// <summary>The compile-time-resolved method, or null for generated state-source publication.</summary>
        public string MethodName { get; }

        /// <summary>Names an optional pure <c>void M(T candidate)</c> method that may reject before mutation.</summary>
        public string Validator { get; set; }

        /// <summary>Declares that this field exclusively owns its referenced value.</summary>
        public bool Owned { get; set; }
    }

    /// <summary>
    /// Generates structural collection tracking with generated publication or a named package method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateCollectionAttribute : Attribute
    {
        /// <summary>Publishes structural transitions through the hierarchy's generated state source.</summary>
        public StateCollectionAttribute() { }

        /// <summary>Declares the package method that receives structural transitions of this field.</summary>
        public StateCollectionAttribute(string methodName) => MethodName = methodName;

        /// <summary>The compile-time-resolved method, or null for generated state-source publication.</summary>
        public string MethodName { get; }

        /// <summary>Declares that this field exclusively owns every referenced element.</summary>
        public bool Owned { get; set; }
    }

    /// <summary>Generates a mutable public list view over serialized collection state.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StateListAttribute : Attribute
    {
        /// <summary>Publishes structural transitions through the hierarchy's generated state source.</summary>
        public StateListAttribute() { }

        /// <summary>Declares the package method that receives structural transitions of this field.</summary>
        public StateListAttribute(string methodName) => MethodName = methodName;

        /// <summary>The compile-time-resolved method, or null for generated state-source publication.</summary>
        public string MethodName { get; }

        /// <summary>Overrides the generated list property name.</summary>
        public string Name { get; set; }

        /// <summary>Names an optional pure <c>void M(in StateListMutation&lt;T&gt;)</c> method that validates the projected collection before mutation.</summary>
        public string Validator { get; set; }

        /// <summary>Controls whether the generated list property is public or private.</summary>
        public bool IsPublic { get; set; } = true;

        /// <summary>Controls whether null reference items are valid.</summary>
        public bool AllowNullItems { get; set; } = true;

        /// <summary>Controls whether the same reference may occur more than once.</summary>
        public bool AllowDuplicateReferences { get; set; } = true;

        /// <summary>Declares that this field exclusively owns every referenced element.</summary>
        public bool Owned { get; set; }
    }

    /// <summary>Marks serialized state that intentionally has no live mutation consequence.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class StatePassiveAttribute : Attribute { }

    /// <summary>Marks synchronous pre-capture normalization that establishes the serialized-state baseline.</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
    internal sealed class StateInitializationAttribute : Attribute { }

    /// <summary>Declares that a state collection serializes its elements as managed references.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    internal sealed class StateCollectionElementReferenceAttribute : Attribute { }
}
