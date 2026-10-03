using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Marks a string field as a default parameter for a parse rule.
    /// Draws the opt-in parameter list generated from the paired modifier's <c>[Parameter]</c> fields.
    /// </summary>
    public sealed class DefaultParameterAttribute : PropertyAttribute { }
}
