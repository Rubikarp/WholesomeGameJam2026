using System;

namespace LightSide
{
    /// <summary>
    /// Base for value validators — judge the whole current value and return a <see cref="ValidationState"/>
    /// (open status + message) that an <see cref="AutoValidateBehavior"/> publishes to
    /// <see cref="UniTextEditable.Validation"/> for decorators to show. A separate concern from input filtering
    /// (<see cref="InputFilterBase"/>), which rejects characters as they are typed.
    /// </summary>
    [Serializable]
    [TypeMenuSuffix("Validator")]
    public abstract class InputValidatorBase : InputBehavior
    {
        /// <summary>Verdict for the whole current value. An empty <see cref="ValidationState.Status"/> means valid.</summary>
        public abstract ValidationState Validate(ITextDocument document);
    }
}
