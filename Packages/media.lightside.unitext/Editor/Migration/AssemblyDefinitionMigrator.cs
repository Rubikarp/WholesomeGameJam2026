using System;
using System.IO;

namespace LightSide
{
    /// <summary>
    /// Adds the UniText assembly reference to an assembly definition so the scripts it compiles
    /// can name UniText types. The TMP reference is left in place — a file the rewrite does not
    /// touch, or a use with no UniText counterpart, still compiles against it — and the rest of
    /// the document is written back byte for byte, so key order and formatting survive.
    /// </summary>
    internal static class AssemblyDefinitionMigrator
    {
        public const string UniTextAssemblyName = "LightSide.UniText";
        public const string UniTextAssemblyGuid = "6572381e8157783499bf6ce8f56b9292";

        private const string ReferencesKey = "\"references\"";

        /// <summary>Whether the document already names the UniText assembly, by name or by GUID.</summary>
        public static bool ReferencesUniText(string content)
        {
            return content.IndexOf("\"" + UniTextAssemblyName + "\"", StringComparison.Ordinal) >= 0 ||
                   content.IndexOf(UniTextAssemblyGuid, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Adds the reference and reports what happened. Succeeds without writing when the
        /// reference is already there. The original is kept as a sibling <c>.bak</c>.
        /// </summary>
        public static (bool success, string backupPath, string error) AddUniTextReference(
            string filePath, bool createBackup)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch (Exception ex) { return (false, null, $"Cannot read {filePath}: {ex.Message}"); }

            if (ReferencesUniText(content)) return (true, null, null);

            if (!TryInsert(content, out var rewritten, out var error)) return (false, null, error);

            string backupPath = null;
            if (createBackup)
            {
                backupPath = filePath + ".bak";
                try { File.Copy(filePath, backupPath, true); }
                catch (Exception ex) { return (false, null, $"Cannot create backup: {ex.Message}"); }
            }

            try { File.WriteAllText(filePath, rewritten); }
            catch (Exception ex)
            {
                return (false, backupPath, $"Cannot write {filePath}: {ex.Message}");
            }

            return (true, backupPath, null);
        }

        /// <summary>
        /// Places the entry inside the existing <c>references</c> array, or writes the array when
        /// the document has none. The entry takes the form the file already uses: a document whose
        /// references are GUIDs gets a GUID, one that spells assembly names gets the name.
        /// </summary>
        private static bool TryInsert(string content, out string rewritten, out string error)
        {
            rewritten = null;
            error = null;

            var key = content.IndexOf(ReferencesKey, StringComparison.Ordinal);
            if (key < 0) return TryInsertArray(content, out rewritten, out error);

            var open = content.IndexOf('[', key + ReferencesKey.Length);
            if (open < 0)
            {
                error = "The references key is not an array.";
                return false;
            }

            var close = FindArrayEnd(content, open);
            if (close < 0)
            {
                error = "The references array is not closed.";
                return false;
            }

            var body = content.Substring(open + 1, close - open - 1);
            var entry = "\"" + (UsesGuids(body) ? "GUID:" + UniTextAssemblyGuid : UniTextAssemblyName) + "\"";
            var multiLine = body.IndexOf('\n') >= 0;

            if (body.Trim().Length == 0)
            {
                var indent = LineIndent(content, key);
                rewritten = multiLine || body.Length > 0
                    ? content.Substring(0, open + 1) + "\n" + indent + "    " + entry + "\n" +
                      indent + content.Substring(close)
                    : content.Substring(0, open + 1) + entry + content.Substring(close);
                return true;
            }

            var lastEntry = close;
            while (lastEntry > open + 1 && char.IsWhiteSpace(content[lastEntry - 1])) lastEntry--;

            rewritten = multiLine
                ? content.Substring(0, lastEntry) + ",\n" + LineIndent(content, lastEntry - 1) +
                  entry + content.Substring(lastEntry)
                : content.Substring(0, lastEntry) + ", " + entry + content.Substring(lastEntry);
            return true;
        }

        /// <summary>Writes a references array into a document that declares none.</summary>
        private static bool TryInsertArray(string content, out string rewritten, out string error)
        {
            rewritten = null;
            error = null;

            var brace = content.IndexOf('{');
            if (brace < 0)
            {
                error = "The file is not a JSON object.";
                return false;
            }

            var firstKey = content.IndexOf('"', brace + 1);
            var indent = firstKey > 0 ? LineIndent(content, firstKey) : "    ";

            rewritten = content.Substring(0, brace + 1) + "\n" +
                        indent + ReferencesKey + ": [\n" +
                        indent + "    \"" + UniTextAssemblyName + "\"\n" +
                        indent + "]," +
                        content.Substring(brace + 1);
            return true;
        }

        /// <summary>The closing bracket of the array opened at <paramref name="open"/>, quotes respected.</summary>
        private static int FindArrayEnd(string content, int open)
        {
            var inString = false;
            for (var at = open + 1; at < content.Length; at++)
            {
                var c = content[at];
                if (inString)
                {
                    if (c == '\\') at++;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == ']') return at;
            }
            return -1;
        }

        private static bool UsesGuids(string body)
        {
            return body.IndexOf("\"GUID:", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>The leading whitespace of the line holding <paramref name="index"/>.</summary>
        private static string LineIndent(string content, int index)
        {
            var start = index;
            while (start > 0 && content[start - 1] != '\n') start--;
            var end = start;
            while (end < content.Length && (content[end] == ' ' || content[end] == '\t')) end++;
            return content.Substring(start, end - start);
        }
    }
}
