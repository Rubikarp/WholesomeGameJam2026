using System;

namespace LightSide
{
    /// <summary>
    /// Names the "none" entry the editor type selector shows for fields of this base type — for a
    /// category whose null value carries meaning (e.g. "applies everywhere") instead of plain absence.
    /// Declare on the category root (base class or interface); derived base types inherit it.
    /// Unannotated categories show "(None)".
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
    public sealed class SelectorNoneLabelAttribute : Attribute
    {
        public string Label { get; }

        public SelectorNoneLabelAttribute(string label) => Label = label;
    }
}
