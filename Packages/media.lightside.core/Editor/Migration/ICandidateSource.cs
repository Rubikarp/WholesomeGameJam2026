using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Supplies the asset paths a migration might need to touch, given its discovery tokens. Abstracts
    /// discovery so the strategy (persistent index vs. targeted scan) can change without touching migrations.
    /// </summary>
    internal interface ICandidateSource
    {
        IEnumerable<string> FindCandidates(IReadOnlyList<string> tokens);
    }
}
