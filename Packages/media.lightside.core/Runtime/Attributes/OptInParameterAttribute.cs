using System;

namespace LightSide
{
    /// <summary>
    /// Puts a serialized field behind its inspector's opt-in "Parameters" list instead of the always-visible
    /// body: the field shows once its value differs from the type's default or the author adds it from the
    /// list's menu, and removing it there writes the default back. Purely an inspector arrangement — the field
    /// serializes and behaves the same either way. The declaring type must have a parameterless constructor,
    /// which supplies the defaults the list compares against.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptInParameterAttribute : Attribute { }
}
