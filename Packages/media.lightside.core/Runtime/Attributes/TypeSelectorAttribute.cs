using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Draws a <c>[SerializeReference]</c> field with a grouped type picker, or declares a <c>List&lt;T&gt;</c>'s
    /// elements polymorphic. Types passed to the constructor are left out of this field's picker — for a field
    /// whose contract refuses them — while every other surface still sees them.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class TypeSelectorAttribute : PropertyAttribute
    {
        /// <summary>Subtypes (with everything assignable to them) this field's picker does not offer.</summary>
        public Type[] Exclude { get; }

        public TypeSelectorAttribute(params Type[] exclude) => Exclude = exclude ?? Type.EmptyTypes;
    }

    /// <summary>Hides a type from every TypeSelector dropdown — or an enum member from enum dropdowns — while keeping it deserializable.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)]
    public class HideFromTypeSelectorAttribute : Attribute { }
}
