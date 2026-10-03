using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Marks a serialized field as positional parameter metadata: its C# type, declaration order,
    /// name, and initializer drive the editor schema and runtime fallback.
    /// </summary>
    /// <remarks>
    /// The runtime never reflects. Its <c>ParameterReader</c> read order MUST match the reflected
    /// declaration order: own-type fields first, then inherited base-type fields.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ParameterAttribute : Attribute
    {
        /// <summary>
        /// Name of a static <c>bool (ReadOnlySpan&lt;char&gt;, out T)</c> method declaring this
        /// parameter's own markup-token vocabulary in place of the type's default parsing.
        /// </summary>
        public string Parser { get; set; }

        /// <summary>
        /// Name of a static <c>IParameterOps&lt;T&gt;</c> field or property on the field's declaring
        /// type that replaces the value type's default parameter operations. An explicitly named
        /// <see cref="Parser"/> remains the parsing authority.
        /// </summary>
        public string Operations { get; set; }

        /// <summary>
        /// False keeps the field a positional markup slot and editor schema entry while declaring
        /// no descriptor: value resolution belongs to the modifier's own parse code, with no
        /// cascade, ownership, or driving surface.
        /// </summary>
        public bool Descriptor { get; set; } = true;

        /// <summary>
        /// Name of an instance parameterless method raising this parameter's invalidation, for
        /// fields whose state callback needs a value transition and cannot be raised on its own.
        /// </summary>
        public string Invalidate { get; set; }
    }

    /// <summary>
    /// Marks a state field as a parameter with no markup presence: it joins the modifier's
    /// generated parameter surface — cascade, ownership, effects — without occupying a positional
    /// markup slot.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SlotlessParameterAttribute : Attribute
    {
        /// <inheritdoc cref="ParameterAttribute.Parser"/>
        public string Parser { get; set; }

        /// <inheritdoc cref="ParameterAttribute.Operations"/>
        public string Operations { get; set; }

        /// <inheritdoc cref="ParameterAttribute.Invalidate"/>
        public string Invalidate { get; set; }
    }

    /// <summary>
    /// Emits the modifier's parameter surface from its <see cref="ParameterAttribute"/> and
    /// <see cref="SlotlessParameterAttribute"/> fields:
    /// the nested <c>Param</c> descriptor class, <c>Param.All</c>, and the <c>Descriptors</c>
    /// override.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GenerateParametersAttribute : Attribute { }

    /// <summary>
    /// Flattens the marked serializable reference object's direct <see cref="ParameterAttribute"/>
    /// fields into the owning modifier's positional parameter schema. The container must be a
    /// non-null reference type; nested parameter containers are not supported.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ParameterContainerAttribute : Attribute
    {
        /// <summary>
        /// Name of an instance parameterless method on the owning modifier raising the
        /// invalidation shared by every generated descriptor of this container's parameters.
        /// </summary>
        public string Invalidate { get; set; }
    }

    /// <summary>
    /// Declares the unit choices offered for a <see cref="UnitValue"/> / <see cref="UnitVector2"/>
    /// parameter field, pipe-separated (e.g. <c>"px|%|delta"</c>, <c>"px|em"</c>). Each entry is one
    /// of <c>px</c>, <c>abs</c>, <c>%</c>, <c>em</c>, <c>delta</c>, optionally with a slider range —
    /// <c>em(0,4)</c> for floats, <c>abs[1,1000]</c> for integers. The field's own value carries the
    /// authored default unit; this only sets which units the dropdown lists.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UnitAttribute : Attribute
    {
        public string Units { get; }
        public UnitAttribute(string units) => Units = units;
    }

    /// <summary>
    /// Populates a <c>string</c> parameter field's dropdown from a registered parameter provider
    /// (e.g. <c>"@paints"</c>, <c>"@fonts"</c>) instead of a fixed enum.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptionsAttribute : Attribute
    {
        public string Key { get; }
        public OptionsAttribute(string key) => Key = key;
    }

    /// <summary>
    /// A discriminated-union editor for a parameter field: pipe-separated
    /// <c>Label=subtype</c> options (e.g. <c>"Color=color:#000000FF|Swatch=enum:@paints"</c>), where a
    /// subtype is a literal, a scalar type (<c>color</c>/<c>float</c>/<c>int</c>/<c>bool</c>), or an
    /// <c>enum:@provider</c>. Compound values can name their enum discriminator; its declaration
    /// order must match the option order.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VariantAttribute : Attribute
    {
        public string Spec { get; }

        /// <summary>
        /// Name of the enum field or readable property that stores the active option for a compound
        /// value. The enum declaration order must match the option order in <see cref="Spec"/>.
        /// </summary>
        public string Discriminator { get; set; }

        public VariantAttribute(string spec) => Spec = spec;
    }

    /// <summary>
    /// Shows this parameter field only when another parameter field's value equals
    /// <paramref name="label"/> — for enum/variant triggers, the option label; otherwise the token
    /// verbatim.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class VisibleWhenAttribute : Attribute
    {
        public string Field { get; }
        public string Label { get; }

        public VisibleWhenAttribute(string field, string label)
        {
            Field = field;
            Label = label;
        }
    }
}
