using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// Rewrites TMP references in C# source. Planning belongs to <see cref="ScriptRewriter"/>,
    /// which reads the file lexically; this type owns reading, writing and the backup that makes
    /// a rewrite reversible.
    /// </summary>
    internal static class ScriptMigrator
    {
        /// <summary>
        /// Every edit and report the rewrite has for one file. A file that cannot be read comes
        /// back as one blocking report rather than an empty plan, which the caller would otherwise
        /// present as "nothing to rewrite".
        /// </summary>
        public static List<ScriptReplacement> AnalyzeFile(string filePath)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch (Exception ex)
            {
                return new List<ScriptReplacement>
                {
                    new()
                    {
                        lineNumber = 1,
                        isWarningOnly = true,
                        blocksFile = true,
                        isSelected = false,
                        warningMessage = $"Cannot read {filePath}: {ex.Message}",
                    },
                };
            }
            return ScriptRewriter.Analyze(content);
        }

        public static bool HasBlockers(List<ScriptReplacement> replacements)
        {
            for (var i = 0; i < replacements.Count; i++)
                if (replacements[i].blocksFile) return true;
            return false;
        }

        /// <summary>
        /// Generates a UI-agnostic unified diff string with context lines.
        /// </summary>
        public static string GenerateDiff(string filePath, List<ScriptReplacement> replacements)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch { return "Error: cannot read file"; }

            var lines = content.Split('\n');
            var sb = new StringBuilder();
            const int contextLines = 3;

            var changedLines = new HashSet<int>();
            foreach (var r in replacements)
                changedLines.Add(r.lineNumber);

            var sortedLines = new List<int>(changedLines);
            sortedLines.Sort();

            int lastPrintedLine = -1;

            foreach (var lineNum in sortedLines)
            {
                int idx = lineNum - 1;

                int contextStart = Math.Max(0, idx - contextLines);
                if (lastPrintedLine >= 0 && contextStart <= lastPrintedLine + 1)
                    contextStart = lastPrintedLine + 1;
                else if (lastPrintedLine >= 0)
                    sb.AppendLine("  ...");

                for (int i = contextStart; i < idx; i++)
                {
                    sb.AppendLine($"  {i + 1,4}  {lines[i].TrimEnd('\r')}");
                    lastPrintedLine = i;
                }

                var lineReplacements = new List<ScriptReplacement>();
                foreach (var r in replacements)
                {
                    if (r.lineNumber == lineNum)
                        lineReplacements.Add(r);
                }

                var originalLine = idx < lines.Length ? lines[idx].TrimEnd('\r') : "";

                foreach (var r in lineReplacements)
                {
                    if (r.isWarningOnly)
                    {
                        sb.AppendLine($"  {lineNum,4}  {originalLine}");
                        sb.AppendLine($"       ^ {(r.blocksFile ? "BLOCKS FILE: " : string.Empty)}" +
                                      r.warningMessage);
                    }
                    else
                    {
                        sb.AppendLine($"- {lineNum,4}  {r.original}");
                        sb.AppendLine($"+ {lineNum,4}  {r.replacement}");
                    }
                }

                lastPrintedLine = idx;

                int contextEnd = Math.Min(lines.Length - 1, idx + contextLines);
                for (int i = idx + 1; i <= contextEnd; i++)
                {
                    sb.AppendLine($"  {i + 1,4}  {lines[i].TrimEnd('\r')}");
                    lastPrintedLine = i;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Apply selected replacements to the file. Returns true on success.
        /// Creates .bak backup if <paramref name="createBackup"/> is true.
        /// </summary>
        public static (bool success, string backupPath, string error) ApplyReplacements(
            string filePath,
            List<ScriptReplacement> replacements,
            bool createBackup)
        {
            const string blockerError =
                "TMP_InputField has uses that require hand migration; no part of the file was written.";
            if (HasBlockers(replacements))
                return (false, null, blockerError);

            string content;
            try { content = File.ReadAllText(filePath); }
            catch (Exception ex) { return (false, null, $"Cannot read {filePath}: {ex.Message}"); }
            if (HasBlockers(ScriptRewriter.Analyze(content)))
                return (false, null, blockerError);

            var selected = new List<ScriptReplacement>();
            foreach (var r in replacements)
            {
                if (r.isSelected && !r.isWarningOnly && r.replacement != null)
                    selected.Add(r);
            }

            if (selected.Count == 0)
                return (true, null, null);

            var lines = content.Split('\n');

            foreach (var r in selected)
            {
                var idx = r.lineNumber - 1;
                if (idx < 0 || idx >= lines.Length || r.original == null ||
                    r.columnStart < 0 || r.columnEnd < r.columnStart ||
                    r.columnEnd > lines[idx].Length ||
                    r.original.Length != r.columnEnd - r.columnStart ||
                    string.CompareOrdinal(lines[idx], r.columnStart,
                        r.original, 0, r.original.Length) != 0)
                    return (false, null,
                        $"{filePath} changed since it was analysed; re-select the file.");
            }

            string backupPath = null;
            if (createBackup)
            {
                backupPath = filePath + ".bak";
                try { File.Copy(filePath, backupPath, true); }
                catch (Exception ex) { return (false, null, $"Cannot create backup: {ex.Message}"); }
            }

            var byLine = new Dictionary<int, List<ScriptReplacement>>();
            foreach (var r in selected)
            {
                int idx = r.lineNumber - 1;
                if (!byLine.ContainsKey(idx))
                    byLine[idx] = new List<ScriptReplacement>();
                byLine[idx].Add(r);
            }

            foreach (var kvp in byLine)
            {
                var idx = kvp.Key;
                var line = lines[idx];
                kvp.Value.Sort((a, b) => b.columnStart.CompareTo(a.columnStart));

                foreach (var r in kvp.Value)
                    line = line.Substring(0, r.columnStart) + r.replacement + line.Substring(r.columnEnd);

                lines[idx] = line;
            }

            try
            {
                File.WriteAllText(filePath, string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                return (false, backupPath, $"Cannot write {filePath}: {ex.Message}");
            }

            return (true, backupPath, null);
        }

        /// <summary>
        /// Restore a file from its .bak backup.
        /// </summary>
        public static bool RestoreFromBackup(string backupPath)
        {
            if (!File.Exists(backupPath)) return false;

            var originalPath = backupPath.Substring(0, backupPath.Length - 4);
            try
            {
                File.Copy(backupPath, originalPath, true);
                File.Delete(backupPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
