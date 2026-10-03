using System;

namespace LightSide
{
    /// <summary>
    /// Class-name suffix(es) stripped from this type and its subtypes in the editor type-selector menu —
    /// e.g. <c>[TypeMenuSuffix("Behavior")]</c> on the category root renders <c>PasswordBehavior</c> as
    /// "Password". Declare it on the category root (base class or interface), not on each leaf. When
    /// several suffixes match, the longest one wins.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface, Inherited = false)]
    public class TypeMenuSuffixAttribute : Attribute
    {
        public string[] Suffixes { get; }

        public TypeMenuSuffixAttribute(params string[] suffixes)
        {
            Suffixes = suffixes ?? Array.Empty<string>();
            Array.Sort(Suffixes, (a, b) => b.Length.CompareTo(a.Length));
        }
    }
}
