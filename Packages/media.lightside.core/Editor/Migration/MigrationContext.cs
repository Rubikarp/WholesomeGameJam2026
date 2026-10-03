using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Everything a migration edits for one asset: its parsed documents (whole trees, not just references),
    /// the splice buffer for byte-preserving text edits, and queued asset-file actions.
    /// </summary>
    public sealed class MigrationContext
    {
        public string AssetPath { get; }
        public IReadOnlyList<YamlDocument> Documents { get; }
        public YamlEdit Edit { get; }
        public IAssetActions Assets { get; }

        public MigrationContext(string assetPath, IReadOnlyList<YamlDocument> documents, YamlEdit edit, IAssetActions assets)
        {
            AssetPath = assetPath;
            Documents = documents;
            Edit = edit;
            Assets = assets;
        }
    }

    /// <summary>
    /// Asset-file side effects a migration can request. They are queued until that migration's YAML edits
    /// are ready; paths and preconditions must make every action safe to retry after an interrupted pass.
    /// </summary>
    public interface IAssetActions
    {
        /// <summary>Queues creation after the migration's YAML edits are ready. The target must not exist.</summary>
        void Create(UnityEngine.Object asset, string assetPath);

        /// <summary>
        /// Queues removal after every reversible action. One transaction can remove at most one asset.
        /// </summary>
        void Delete(string assetPath);

        /// <summary>Queues a move after the migration's YAML edits are ready. The target must not exist.</summary>
        void Move(string fromAssetPath, string toAssetPath);
    }
}
