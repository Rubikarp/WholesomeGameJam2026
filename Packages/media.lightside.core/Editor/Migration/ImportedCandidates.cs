using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Candidate source restricted to assets that just entered the project, resolved through the tokens the
    /// index already holds for them. Bounds an incremental pass to the import that provoked it rather than
    /// re-walking the project.
    /// </summary>
    internal sealed class ImportedCandidates : ICandidateSource
    {
        readonly List<(string assetPath, IReadOnlyList<string> tokens)> imported = new();

        public ImportedCandidates(MigrationIndex index, IEnumerable<string> assetPaths)
        {
            foreach (var assetPath in assetPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid)) continue;
                var tokens = index.TokensOf(guid);
                if (tokens.Count > 0) imported.Add((assetPath, tokens));
            }
        }

        public bool IsEmpty => imported.Count == 0;

        public IEnumerable<string> FindCandidates(IReadOnlyList<string> tokens)
        {
            foreach (var (assetPath, assetTokens) in imported)
                for (int i = 0; i < tokens.Count; i++)
                    if (Contains(assetTokens, tokens[i]))
                    {
                        yield return assetPath;
                        break;
                    }
        }

        static bool Contains(IReadOnlyList<string> tokens, string token)
        {
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i] == token) return true;
            return false;
        }
    }
}
