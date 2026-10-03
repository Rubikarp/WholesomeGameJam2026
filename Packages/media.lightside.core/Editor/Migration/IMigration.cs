using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// One serialized-data migration. Discovered by <c>TypeCache</c> across every editor assembly, so
    /// packages and consumer projects author migrations the same way. Asset-local migrations declare the tokens
    /// that make a document a candidate, then transform it in <see cref="Migrate"/>. Project-wide invariants that
    /// must be decided before any write additionally implement <see cref="IMigrationPreparation"/>.
    /// </summary>
    /// <remarks>
    /// Non-abstract, public parameterless constructor. Runs editor-only when a registered package's version
    /// changes (<see cref="IMigratedPackage"/>). Idempotent asset-local work commits one candidate at a time;
    /// one-shot and prepared work commits all candidates together. A migration touches only the passed asset's
    /// own bytes and queued asset actions, and must be a no-op on any document that doesn't match the exact old
    /// shape.
    /// </remarks>
    public interface IMigration
    {
        /// <summary>Stable key for the one-shot completion stamp and logging. Never reuse across changes.</summary>
        string Id { get; }

        /// <summary>
        /// True = safe to run every pass because the transform stops applying once done (renames, moves,
        /// output-exists checks). False = the result is indistinguishable from valid current data (e.g. a
        /// value-default rewrite); gated behind a journaled per-project stamp so it runs exactly once.
        /// Non-idempotent migrations may edit YAML but cannot queue asset actions.
        /// </summary>
        bool Idempotent { get; }

        /// <summary>
        /// Discovery tokens (see <see cref="MigrationTokens"/>). Only assets containing one of these are read.
        /// May be empty when all work is performed through <see cref="IMigrationPreparation"/>.
        /// </summary>
        IReadOnlyList<string> Tokens { get; }

        void Migrate(MigrationContext ctx);
    }
}
