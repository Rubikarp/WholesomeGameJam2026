namespace LightSide
{
    /// <summary>
    /// Performs project-wide validation and queues asset actions before any migration writes begin.
    /// Implement on an <see cref="IMigration"/> only when its invariant cannot be decided from one
    /// candidate document, such as selecting a unique project-owned asset.
    /// </summary>
    public interface IMigrationPreparation
    {
        /// <summary>
        /// Validates the complete pre-migration state and queues asset operations. Throwing aborts this
        /// migration without applying its YAML edits or queued asset operations.
        /// </summary>
        void Prepare(IAssetActions assets);
    }
}
