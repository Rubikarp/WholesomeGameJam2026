using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// Scans the project for TMP usage. All scanning is file-based (YAML/text search)
    /// — no scene loading, no TMP assembly dependency. Safe and fast.
    /// </summary>
    /// <remarks>
    /// A file is read as bytes and matched against ASCII markers first; only a file that can
    /// possibly contain TMP is decoded to text and parsed, which is what keeps a multi-megabyte
    /// font atlas or dictionary asset from costing anything. Files whose timestamp and length are
    /// unchanged since the previous scan are answered from that scan instead of being read at all.
    /// Reading and matching run on worker threads; every <c>AssetDatabase</c> lookup is resolved
    /// afterwards on the main thread.
    /// </remarks>
    internal class ProjectAnalyzer
    {
        public List<MigrationFinding> Findings { get; } = new();
        public List<FontMappingEntry> DiscoveredFonts { get; } = new();
        public bool IsScanning { get; private set; }
        public bool WasCancelled { get; private set; }

        /// <summary>
        /// Whether the scan stopped on an error rather than finishing or being cancelled. Kept
        /// apart from <see cref="WasCancelled"/>: a crash and a user's stop call for different
        /// words and different next steps.
        /// </summary>
        public bool Failed { get; private set; }

        /// <summary>What stopped the scan, when <see cref="Failed"/> is set.</summary>
        public string FailureMessage { get; private set; }

        /// <summary>How many assets this scan could not read at all.</summary>
        public int UnreadableFiles { get; private set; }

        /// <summary>
        /// Whether the scan stopped before its end — cancelled or failed — so the files it never
        /// reached are unknown. A file it reached and could not read is not this: that file is
        /// reported as a finding of its own, and the rest of the project is fully described.
        /// </summary>
        public bool WasInterrupted => WasCancelled || Failed;
        public float Progress { get; private set; }
        public string CurrentFile { get; private set; }

        public Dictionary<string, List<string>> PrefabDependencies { get; } = new();

        /// <summary>
        /// Which tags of the shared vocabulary the project actually writes, gathered from every
        /// text the scan decoded. These belong in one project-wide Style preset rather than on
        /// each component.
        /// </summary>
        public HashSet<string> SharedVocabularyTags { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// TMP font asset GUIDs listed as project-wide fallbacks in <c>TMP_Settings</c>, in order.
        /// They apply to every text, so they become one shared stack rather than a per-font chain.
        /// </summary>
        public List<string> GlobalFallbackFontGuids { get; } = new();

        /// <summary>Per-file stamps and raw matches of this scan, to answer the next one without reading.</summary>
        public List<ScannedFileRecord> ScannedFiles { get; } = new();

        /// <summary>How many files this scan answered from the previous one instead of reading.</summary>
        public int ReusedFiles { get; private set; }

        /// <summary>Bytes this scan did not have to read because the file was unchanged.</summary>
        public long ReusedBytes { get; private set; }

        readonly List<string> excludedPaths;
        readonly Dictionary<string, CachedFile> cache = new();
        readonly List<FontSource> fontSources = new();

        Action onComplete;
        List<FileEntry> files;
        int fileIndex;
        long totalBytes;
        long processedBytes;

        const int MaxChunkFiles = 256;
        const long MaxChunkBytes = 64L * 1024 * 1024;

        static readonly Regex scriptGuidRegex = new(@"m_Script:\s*\{[^}]*guid:\s*([a-f0-9]{32})", RegexOptions.Compiled);
        static readonly Regex fileIdRegex = new(@"---\s*!u!\d+\s*&(\d+)", RegexOptions.Compiled);
        static readonly Regex gameObjectNameRegex =
            new(@"^[ \t]*m_Name:[ \t]*([^\r\n]*)", RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex gameObjectDocumentRegex =
            new(@"^--- !u!1 &(-?\d+)", RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex ownerRegex =
            new(@"^[ \t]*m_GameObject:[ \t]*\{fileID:[ \t]*(-?\d+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex textValueRegex =
            new(@"^[ \t]*m_text:[ \t]*", RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex richTextRegex =
            new(@"^[ \t]*m_isRichText:[ \t]*(\d)", RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly char[] lineBreaks = { '\r', '\n' };

        static readonly Regex spriteAssetRegex =
            new(@"^[ \t]*m_spriteAsset:[ \t]*\{fileID:[ \t]*(-?\d+)",
                RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex nestedPrefabRegex = new(@"m_SourcePrefab:\s*\{[^}]*guid:\s*([a-f0-9]{32})", RegexOptions.Compiled);
        static readonly Regex tmpScriptRegex = new(@"\bTMPro\b|\bTextMeshPro\b|\bTMP_\w+", RegexOptions.Compiled);
        static readonly Regex preprocessorTmpRegex = new(@"#if\b.*\bTEXTMESHPRO", RegexOptions.Compiled);
        static readonly Regex tmpAsmRefRegex = new(@"""Unity\.TextMeshPro""", RegexOptions.Compiled);

        static readonly Regex missingScriptRegex =
            new(@"m_Script:\s*\{fileID:\s*0\}", RegexOptions.Compiled);

        static readonly Regex alignmentRegex = new(@"m_textAlignment:\s*(\d+)", RegexOptions.Compiled);

        static readonly Regex horizontalAlignmentRegex =
            new(@"^[ \t]*m_HorizontalAlignment:[ \t]*(\d+)",
                RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex verticalAlignmentRegex =
            new(@"^[ \t]*m_VerticalAlignment:[ \t]*(\d+)",
                RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex fontStyleRegex = new(@"m_fontStyle:\s*(\d+)", RegexOptions.Compiled);
        static readonly Regex overflowRegex = new(@"m_overflowMode:\s*(\d+)", RegexOptions.Compiled);
        static readonly Regex fontWeightRegex = new(@"m_fontWeight:\s*(\d+)", RegexOptions.Compiled);
        static readonly Regex characterSpacingRegex = new(@"m_characterSpacing:\s*([-\d.eE+]+)", RegexOptions.Compiled);
        static readonly Regex wordSpacingRegex = new(@"m_wordSpacing:\s*([-\d.eE+]+)", RegexOptions.Compiled);
        static readonly Regex lineSpacingRegex = new(@"m_lineSpacing:\s*([-\d.eE+]+)", RegexOptions.Compiled);
        static readonly Regex paragraphSpacingRegex = new(@"m_paragraphSpacing:\s*([-\d.eE+]+)", RegexOptions.Compiled);
        static readonly Regex assetNameRegex =
            new(@"^[ \t]*m_Name:[ \t]*([^\r\n]*)", RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex familyNameRegex =
            new(@"^[ \t]*m_FamilyName:[ \t]*([^\r\n]*)",
                RegexOptions.Compiled | RegexOptions.Multiline);
        static readonly Regex referenceGuidRegex = new(@"guid:\s*([a-f0-9]{32})", RegexOptions.Compiled);

        static readonly byte[][] componentGuidMarkers = ToMarkers(MigrationMapping.AllTmpComponentGuids);
        static readonly byte[][] assetGuidMarkers = ToMarkers(MigrationMapping.TmpAssetGuids);
        static readonly byte[][] scriptMarkers = { Ascii("TMP"), Ascii("TextMeshPro") };
        static readonly byte[][] shaderMarkers = { Ascii("TextMeshPro/"), Ascii("TMPro/") };
        static readonly byte[] nestedPrefabMarker = Ascii("m_SourcePrefab:");
        static readonly byte[] assemblyMarker = Ascii("Unity.TextMeshPro");
        const byte TagOpen = (byte)'<';

        public ProjectAnalyzer(List<string> excludedPaths, MigrationSessionData previous = null)
        {
            this.excludedPaths = excludedPaths ?? new List<string>();
            if (previous != null) BuildCache(previous);
        }

        public void StartScan(Action onComplete)
        {
            this.onComplete = onComplete;
            Findings.Clear();
            DiscoveredFonts.Clear();
            PrefabDependencies.Clear();
            SharedVocabularyTags.Clear();
            GlobalFallbackFontGuids.Clear();
            ScannedFiles.Clear();
            fontSources.Clear();
            IsScanning = true;
            WasCancelled = false;
            Progress = 0f;
            ReusedFiles = 0;
            ReusedBytes = 0;

            Failed = false;
            FailureMessage = null;
            UnreadableFiles = 0;
            finished = false;

            try
            {
                files = new List<FileEntry>();
                CollectAssets(files);
                files.Sort(static (a, b) => string.CompareOrdinal(a.path, b.path));
                fontSources.Sort(static (a, b) => string.CompareOrdinal(a.path, b.path));

                totalBytes = 0;
                for (int i = 0; i < files.Count; i++) totalBytes += files[i].length;
                processedBytes = 0;
                fileIndex = 0;
            }
            catch (Exception exception)
            {
                Fail($"The project's assets could not be listed: {exception.Message}");
                FinishScan();
                return;
            }

            EditorApplication.delayCall += ProcessChunk;
        }

        public void Cancel()
        {
            WasCancelled = true;
        }

        /// <summary>
        /// Reads and matches one chunk on worker threads, then folds the results in on the main
        /// thread. The chunk is bounded by bytes as well as by count, so one huge asset cannot
        /// stall the editor for longer than the rest of a chunk would.
        /// </summary>
        void ProcessChunk()
        {
            if (WasCancelled || fileIndex >= files.Count)
            {
                FinishScan();
                return;
            }

            int start = fileIndex;
            long chunkBytes = 0;
            while (fileIndex < files.Count &&
                   fileIndex - start < MaxChunkFiles &&
                   (chunkBytes < MaxChunkBytes || fileIndex == start))
            {
                chunkBytes += files[fileIndex].length;
                fileIndex++;
            }

            int count = fileIndex - start;
            CurrentFile = files[start].path;

            try
            {
                var results = new FileScan[count];
                if (count > 1)
                    Parallel.For(0, count, i => results[i] = ScanEntry(files[start + i]));
                else
                    results[0] = ScanEntry(files[start]);

                for (int i = 0; i < count; i++)
                    Integrate(files[start + i], results[i]);
            }
            catch (Exception exception)
            {
                Fail($"The scan stopped at '{CurrentFile}': {exception.Message}");
                FinishScan();
                return;
            }

            processedBytes += chunkBytes;
            Progress = totalBytes <= 0 ? 1f : (float)((double)processedBytes / totalBytes);

            EditorApplication.delayCall += ProcessChunk;
        }

        void Fail(string message)
        {
            Failed = true;
            FailureMessage = message;
            UnityEngine.Debug.LogError($"[UniText] {message}");
        }

        /// <summary>
        /// Ends the scan exactly once and always hands control back. Re-entrant by contract: a
        /// failure path calls it from inside the chunk that failed, and the natural end calls it
        /// again on the following tick.
        /// </summary>
        void FinishScan()
        {
            if (finished) return;
            finished = true;
            try
            {
                ScanCompiledAssemblies();
            }
            catch (Exception exception)
            {
                Fail("The compiled assemblies could not be inspected: " + exception.Message);
            }
            finally
            {
                IsScanning = false;
                Progress = WasCancelled || Failed ? Progress : 1f;
                CurrentFile = null;
                onComplete?.Invoke();
            }
        }

        void BuildCache(MigrationSessionData previous)
        {
            var byPath = new Dictionary<string, List<MigrationFinding>>();
            for (int i = 0; i < previous.findings.Count; i++)
            {
                var finding = previous.findings[i];
                if (finding.type == FindingType.CompiledDependency) continue;
                if (!byPath.TryGetValue(finding.filePath, out var list))
                    byPath[finding.filePath] = list = new List<MigrationFinding>();
                list.Add(finding);
            }

            for (int i = 0; i < previous.scannedFiles.Count; i++)
            {
                var record = previous.scannedFiles[i];
                if (string.IsNullOrEmpty(record.path)) continue;
                byPath.TryGetValue(record.path, out var list);
                cache[record.path] = new CachedFile(record, list);
            }
        }

        // ── collection ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Collects what the scan will read, straight from the asset database. Enumerating assets
        /// rather than walking the disk is what keeps a package's hidden <c>Samples~</c> out — it
        /// is not an asset, has no GUID and cannot be opened — while still reaching a local
        /// package, whose virtual <c>Packages/</c> path has no filesystem counterpart at all.
        /// </summary>
        void CollectAssets(List<FileEntry> result)
        {
            foreach (var target in MigrationScope.CollectWhere(IsScannable, excludedPaths))
            {
                var kind = KindOf(target.assetPath);
                if (kind == FileKind.Font)
                {
                    fontSources.Add(new FontSource(target.assetPath));
                    continue;
                }

                FileInfo info;
                try { info = new FileInfo(target.fsPath); }
                catch (Exception exception) when (exception is IOException or
                                                     UnauthorizedAccessException or
                                                     ArgumentException)
                {
                    RecordUnreadable(target.assetPath, exception.Message);
                    continue;
                }
                result.Add(new FileEntry(target.assetPath, target.fsPath, kind,
                    info.LastWriteTimeUtc.Ticks, info.Length));
            }
        }

        /// <summary>Largest asset the scan reads; anything past it is reported, never loaded.</summary>
        const long MaxFileBytes = 256L * 1024 * 1024;

        /// <summary>Guards <see cref="FinishScan"/> against the failure and natural paths both firing.</summary>
        bool finished;

        static bool IsScannable(string assetPath) => KindOf(assetPath) != FileKind.None;

        /// <summary>
        /// Reports the components whose script no longer resolves. Unity refuses to save a prefab
        /// that carries one, so a file holding both a TMP component and a missing script cannot be
        /// migrated until the missing script is dealt with. Only the <c>fileID: 0</c> form is
        /// visible in text; a reference to a deleted script asset is caught when the file is
        /// opened for migration.
        /// </summary>
        static void ScanMissingScripts(string path, string content, FileScan scan, bool isPrefab)
        {
            var count = missingScriptRegex.Matches(content).Count;
            if (count == 0) return;

            scan.Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.MissingScript),
                filePath = path,
                type = FindingType.MissingScript,
                complexity = MigrationComplexity.Manual,
                details = isPrefab
                    ? $"At least {count} missing script(s) — this prefab cannot be saved, and so " +
                      "cannot be migrated, until they are restored or removed"
                    : $"At least {count} missing script(s) on objects in this scene",
            });
        }

        bool IsExcluded(string path) => MigrationScope.Excludes(path, excludedPaths);

        static FileKind KindOf(string path)
        {
            var dot = path.LastIndexOf('.');
            if (dot < 0) return FileKind.None;
            var ext = path.Substring(dot);
            if (Is(ext, ".unity")) return FileKind.Scene;
            if (Is(ext, ".prefab")) return FileKind.Prefab;
            if (Is(ext, ".cs")) return FileKind.Script;
            if (Is(ext, ".asset")) return FileKind.Asset;
            if (Is(ext, ".mat")) return FileKind.Material;
            if (Is(ext, ".anim")) return FileKind.Animation;
            if (Is(ext, ".asmdef")) return FileKind.AssemblyDef;
            if (Is(ext, ".csv") || Is(ext, ".json") || Is(ext, ".txt")) return FileKind.TextContent;
            if (Is(ext, ".ttf") || Is(ext, ".otf")) return FileKind.Font;
            return FileKind.None;
        }

        static bool Is(string extension, string expected)
            => extension.Equals(expected, StringComparison.OrdinalIgnoreCase);

        // ── per-file scanning (worker threads: no Unity API below this line) ────────────────

        FileScan ScanEntry(FileEntry entry)
        {
            if (cache.TryGetValue(entry.path, out var cached) && cached.Matches(entry))
                return cached.ToScan();
            return ScanFile(entry);
        }

        /// <summary>
        /// Reads the file once as bytes and answers the cheap ASCII question first. Only a file
        /// that can carry TMP is decoded and parsed.
        /// </summary>
        FileScan ScanFile(FileEntry entry)
        {
            if (entry.length > MaxFileBytes)
                return new FileScan
                {
                    Failure = $"the asset is {entry.length / (1024 * 1024)} MB, past the " +
                              $"{MaxFileBytes / (1024 * 1024)} MB the scan reads",
                };

            byte[] bytes;
            try { bytes = File.ReadAllBytes(entry.fsPath); }
            catch (Exception exception)
            {
                return new FileScan { Failure = exception.Message };
            }

            var data = new ReadOnlySpan<byte>(bytes);
            var scan = new FileScan();

            switch (entry.kind)
            {
                case FileKind.Scene:
                case FileKind.Prefab:
                {
                    bool hasComponent = ContainsAny(data, componentGuidMarkers);
                    bool hasNested = entry.kind == FileKind.Prefab &&
                                     data.IndexOf(nestedPrefabMarker) >= 0;
                    if (!hasComponent && !hasNested) return scan;
                    var content = Decode(bytes);
                    if (hasComponent)
                    {
                        ScanComponents(entry.path, content, scan);
                        ScanMissingScripts(entry.path, content, scan,
                            entry.kind == FileKind.Prefab);
                        CollectSharedTags(content, scan);
                    }
                    if (hasNested) CollectNestedPrefabs(content, scan);
                    return scan;
                }

                case FileKind.Script:
                {
                    if (!ContainsAny(data, scriptMarkers)) return scan;
                    ScanScript(entry.path, Decode(bytes), scan);
                    return scan;
                }

                case FileKind.Asset:
                {
                    bool hasTmpAsset = ContainsAny(data, assetGuidMarkers);
                    bool hasMarkup = data.IndexOf(TagOpen) >= 0;
                    if (!hasTmpAsset && !hasMarkup) return scan;
                    var content = Decode(bytes);
                    if (hasTmpAsset) ScanTmpAsset(entry.path, content, scan);
                    if (hasMarkup) ScanTags(entry.path, content, scan);
                    return scan;
                }

                case FileKind.Material:
                {
                    if (!ContainsAny(data, shaderMarkers)) return scan;
                    ScanMaterial(entry.path, Decode(bytes), scan);
                    return scan;
                }

                case FileKind.Animation:
                {
                    if (!ContainsAny(data, componentGuidMarkers)) return scan;
                    ScanAnimation(entry.path, Decode(bytes), scan);
                    return scan;
                }

                case FileKind.AssemblyDef:
                {
                    if (data.IndexOf(assemblyMarker) < 0) return scan;
                    ScanAssemblyDef(entry.path, Decode(bytes), scan);
                    return scan;
                }

                case FileKind.TextContent:
                {
                    if (data.IndexOf(TagOpen) < 0) return scan;
                    ScanTags(entry.path, Decode(bytes), scan);
                    return scan;
                }
            }

            return scan;
        }

        static bool ContainsAny(ReadOnlySpan<byte> data, byte[][] markers)
        {
            for (int i = 0; i < markers.Length; i++)
                if (data.IndexOf(new ReadOnlySpan<byte>(markers[i])) >= 0) return true;
            return false;
        }

        static string Decode(byte[] bytes)
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

        static byte[][] ToMarkers(IEnumerable<string> values)
        {
            var list = new List<byte[]>();
            foreach (var value in values) list.Add(Ascii(value));
            return list.ToArray();
        }

        void ScanComponents(string path, string content, FileScan scan)
        {
            Dictionary<string, string> gameObjectNames = null;
            foreach (Match match in scriptGuidRegex.Matches(content))
            {
                var guid = match.Groups[1].Value;

                if (MigrationMapping.SubMeshGuids.Contains(guid))
                    continue;

                if (!MigrationMapping.AllTmpComponentGuids.Contains(guid))
                    continue;

                var blockStart = DocumentStart(content, match.Index);
                if (IsStrippedDocument(content, blockStart)) continue;
                var blockLength = ComponentBlockEnd(content, match.Index) - blockStart;
                var fileID = ExtractFileID(content, blockStart);
                gameObjectNames ??= CollectGameObjectNames(content);
                var objectName = ExtractObjectName(content, blockStart, blockLength,
                    gameObjectNames);

                var tmpName = MigrationMapping.GetTmpName(guid);
                var targetName = MigrationMapping.GetTargetName(guid);
                var details = targetName != "(none)"
                    ? $"{tmpName} → {targetName} on '{objectName}'"
                    : $"{tmpName} on '{objectName}' (no equivalent)";

                var complexity = DetermineComponentComplexity(guid, content, blockStart,
                    blockLength);
                var warnings = CollectComponentWarnings(guid, content, blockStart, blockLength);
                AnalyzeComponentMarkup(content, blockStart, blockLength, ref complexity,
                    ref warnings);

                scan.Findings.Add(new MigrationFinding
                {
                    id = MigrationFinding.ComputeId(path, guid, fileID),
                    filePath = path,
                    type = FindingType.Component,
                    complexity = complexity,
                    details = details,
                    objectPath = objectName,
                    scriptGuid = guid,
                    fileID = fileID,
                    warnings = warnings,
                });
            }
        }

        /// <summary>
        /// Folds what the component's own serialized text needs into its finding, using the same
        /// converter the migration runs — so the scan's verdict and the rewrite's verdict cannot
        /// drift apart.
        /// </summary>
        static void AnalyzeComponentMarkup(string content, int start, int length,
            ref MigrationComplexity complexity, ref List<string> warnings)
        {
            var text = ReadScalar(content, start, length, textValueRegex);
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return;

            var richText = richTextRegex.Match(content, start, length);
            var richTextOff = richText.Success && richText.Groups[1].Value == "0";

            var converted = RichTextConverter.Convert(text);
            if (converted.warnings is { Count: > 0 })
            {
                warnings ??= new List<string>();
                for (var i = 0; i < converted.warnings.Count; i++)
                    warnings.Add("Markup: " + converted.warnings[i]);
            }

            if (richTextOff)
            {
                warnings ??= new List<string>();
                warnings.Add("Rich text is off in TMP but the serialized text carries markup — " +
                             "UniText has no rich-text switch, so these tags start rendering");
            }

            if ((converted.warnings is { Count: > 0 } || richTextOff) &&
                complexity == MigrationComplexity.Simple)
                complexity = MigrationComplexity.Moderate;
        }

        /// <summary>
        /// One YAML scalar property inside a document, unescaped. Unity writes a text carrying
        /// newlines or quotes in the double-quoted form, where a raw read of the line would cut
        /// markup in half and take escapes for characters.
        /// </summary>
        static string ReadScalar(string content, int start, int length, Regex property)
        {
            var match = property.Match(content, start, length);
            if (!match.Success) return null;

            var limit = start + length;
            var at = match.Index + match.Length;
            if (at >= limit) return string.Empty;

            var quote = content[at];
            if (quote != '"' && quote != '\'')
            {
                var lineEnd = content.IndexOfAny(lineBreaks, at);
                if (lineEnd < 0 || lineEnd > limit) lineEnd = limit;
                return content.Substring(at, lineEnd - at).TrimEnd();
            }

            var builder = new StringBuilder();
            for (var i = at + 1; i < limit; i++)
            {
                var c = content[i];
                if (quote == '\'')
                {
                    if (c != '\'') { builder.Append(c); continue; }
                    if (i + 1 < limit && content[i + 1] == '\'') { builder.Append('\''); i++; continue; }
                    break;
                }
                if (c == '"') break;
                if (c != '\\') { builder.Append(c); continue; }
                if (++i >= limit) break;
                switch (content[i])
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case '0': builder.Append('\0'); break;
                    case 'u' when i + 4 < limit &&
                                  ushort.TryParse(content.Substring(i + 1, 4),
                                      System.Globalization.NumberStyles.HexNumber,
                                      System.Globalization.CultureInfo.InvariantCulture,
                                      out var code):
                        builder.Append((char)code);
                        i += 4;
                        break;
                    default: builder.Append(content[i]); break;
                }
            }
            return builder.ToString();
        }

        /// <summary>
        /// Every GameObject name in the file, by local file id. Built in one pass over text the
        /// scan already decoded: a component's own name is the name of the GameObject it names in
        /// <c>m_GameObject</c>, which no backward text search can answer reliably — a MonoBehaviour
        /// writes <c>m_Name</c> after <c>m_Script</c>, and an empty one belongs to another object.
        /// </summary>
        static Dictionary<string, string> CollectGameObjectNames(string content)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match document in gameObjectDocumentRegex.Matches(content))
            {
                var end = ComponentBlockEnd(content, document.Index);
                var name = gameObjectNameRegex.Match(content, document.Index,
                    end - document.Index);
                if (name.Success) names[document.Groups[1].Value] = CleanName(name.Groups[1].Value);
            }
            return names;
        }

        /// <summary>Unity's serialized name, with its quoted-empty form read as absent.</summary>
        static string CleanName(string value)
        {
            var trimmed = value.Trim();
            if (trimmed == "''" || trimmed == "\"\"") return string.Empty;
            return trimmed;
        }

        void CollectNestedPrefabs(string content, FileScan scan)
        {
            foreach (Match match in nestedPrefabRegex.Matches(content))
                scan.NestedPrefabGuids.Add(match.Groups[1].Value);
        }

        /// <summary>
        /// Where the YAML document containing <paramref name="matchIndex"/> ends. Only a line that
        /// actually opens a document counts — a horizontal rule inside a serialized text literal
        /// would otherwise cut the document short.
        /// </summary>
        static int ComponentBlockEnd(string content, int matchIndex)
        {
            int blockEnd = content.IndexOf("\n--- !u!", matchIndex + 1, StringComparison.Ordinal);
            return blockEnd < 0 ? content.Length : blockEnd;
        }

        /// <summary>
        /// Where that document begins. A component must be read from its header, not from its
        /// <c>m_Script</c> line: Unity writes <c>m_GameObject</c> — the component's own identity —
        /// several fields ahead of it.
        /// </summary>
        static int DocumentStart(string content, int matchIndex)
        {
            var at = content.LastIndexOf("\n--- !u!", matchIndex, StringComparison.Ordinal);
            return at < 0 ? 0 : at + 1;
        }

        /// <summary>
        /// Whether the document at <paramref name="blockStart"/> is a prefab-instance stub. Its
        /// component is serialized in the source prefab, and only that file can migrate it; here
        /// it is a reference, not a component.
        /// </summary>
        static bool IsStrippedDocument(string content, int blockStart)
        {
            var lineEnd = content.IndexOf('\n', blockStart);
            if (lineEnd < 0) lineEnd = content.Length;
            return content.AsSpan(blockStart, lineEnd - blockStart).TrimEnd('\r')
                .EndsWith(" stripped".AsSpan(), StringComparison.Ordinal);
        }

        /// <summary>The <c>&amp;fileID</c> the document at <paramref name="blockStart"/> declares.</summary>
        static string ExtractFileID(string content, int blockStart)
        {
            var match = fileIdRegex.Match(content, blockStart,
                Math.Min(64, content.Length - blockStart));
            return match.Success ? match.Groups[1].Value : "0";
        }

        /// <summary>
        /// Name of the GameObject this component is attached to, read from its own
        /// <c>m_GameObject</c> reference. A component whose owner has no document in this file —
        /// a stripped prefab-instance component — is deliberately "(unknown)": nothing in the file
        /// names it.
        /// </summary>
        static string ExtractObjectName(string content, int blockStart, int blockLength,
            Dictionary<string, string> gameObjectNames)
        {
            var owner = ownerRegex.Match(content, blockStart, blockLength);
            if (!owner.Success ||
                !gameObjectNames.TryGetValue(owner.Groups[1].Value, out var name) ||
                string.IsNullOrEmpty(name))
                return "(unknown)";
            return name;
        }

        static MigrationComplexity DetermineComponentComplexity(string guid, string content,
            int start, int length)
        {
            if (guid == MigrationMapping.TmpDropdownGuid)
                return MigrationComplexity.Manual;

            if (guid == MigrationMapping.TmpInputFieldGuid)
                return MigrationComplexity.Complex;

            if (guid == MigrationMapping.TmpText3DGuid)
                return MigrationComplexity.Complex;

            // Simple means the migrator reproduces the component exactly. A setting the migrator
            // converts losslessly — any Left/Center/Right × Top/Middle/Bottom alignment, justified
            // and flush, the seven mapped font-style flags, ellipsis and truncate overflow — does
            // not make a component harder, only different from TMP's defaults.
            bool lossy = false;

            if (TryReadAlignment(content, start, length, out var alignment))
            {
                var (_, _, _, warning) = MigrationMapping.DecomposeAlignment(alignment);
                lossy |= warning != null;
            }

            var styleMatch = fontStyleRegex.Match(content, start, length);
            if (styleMatch.Success)
                lossy |= (int.Parse(styleMatch.Groups[1].Value) &
                          MigrationMapping.UnmappedFontStyles) != 0;

            var overflowMatch = overflowRegex.Match(content, start, length);
            if (overflowMatch.Success)
            {
                int mode = int.Parse(overflowMatch.Groups[1].Value);
                lossy |= mode != 0 && mode != 1 && mode != 3;
            }

            // Converted, but on a scale UniText measures differently — the result wants an eye.
            var weightMatch = fontWeightRegex.Match(content, start, length);
            bool needsReview =
                HasNonZero(characterSpacingRegex, content, start, length) ||
                HasNonZero(wordSpacingRegex, content, start, length) ||
                HasNonZero(lineSpacingRegex, content, start, length) ||
                HasNonZero(paragraphSpacingRegex, content, start, length) ||
                weightMatch.Success && weightMatch.Groups[1].Value != "400" ||
                Holds(content, start, length, "m_enableVertexGradient: 1") ||
                HasAssignedSpriteAsset(content, start, length);

            if (Holds(content, start, length, "<sprite") &&
                Holds(content, start, length, "anim="))
                return MigrationComplexity.Manual;

            return lossy || needsReview
                ? MigrationComplexity.Moderate
                : MigrationComplexity.Simple;
        }

        /// <summary>
        /// TMP's alignment as its two live fields spell it. <c>m_textAlignment</c> is the legacy
        /// field TMP keeps only for upgrading older assets — its current value is the sentinel
        /// 0xFFFF — so it answers only where the other two are absent.
        /// </summary>
        static bool TryReadAlignment(string content, int start, int length, out int alignment)
        {
            var horizontal = horizontalAlignmentRegex.Match(content, start, length);
            var vertical = verticalAlignmentRegex.Match(content, start, length);
            if (horizontal.Success && vertical.Success)
            {
                alignment = int.Parse(horizontal.Groups[1].Value) |
                            int.Parse(vertical.Groups[1].Value);
                return true;
            }

            var legacy = alignmentRegex.Match(content, start, length);
            alignment = legacy.Success ? int.Parse(legacy.Groups[1].Value) : 0;
            return legacy.Success && alignment != 0xFFFF;
        }

        static bool HasNonZero(Regex regex, string content, int start, int length)
        {
            var match = regex.Match(content, start, length);
            return match.Success && match.Groups[1].Value != "0";
        }

        static bool Holds(string content, int start, int length, string value)
            => content.IndexOf(value, start, length, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Whether the component points at a TMP sprite asset rather than at nothing.</summary>
        static bool HasAssignedSpriteAsset(string content, int start, int length)
        {
            var match = spriteAssetRegex.Match(content, start, length);
            return match.Success && match.Groups[1].Value != "0";
        }

        static List<string> CollectComponentWarnings(string guid, string content, int start,
            int length)
        {
            var warnings = new List<string>();

            if (TryReadAlignment(content, start, length, out var alignment))
            {
                var (_, _, flushLastLine, warning) = MigrationMapping.DecomposeAlignment(alignment);
                if (warning != null) warnings.Add(warning);
                if (flushLastLine)
                    warnings.Add("Flush alignment → Justify + AlignmentModifier(lastLine=Justify)");
            }

            var cspaceMatch = characterSpacingRegex.Match(content, start, length);
            if (cspaceMatch.Success && cspaceMatch.Groups[1].Value != "0")
                warnings.Add($"characterSpacing={cspaceMatch.Groups[1].Value} → will add LetterSpacingModifier");

            var lspaceMatch = lineSpacingRegex.Match(content, start, length);
            if (lspaceMatch.Success && lspaceMatch.Groups[1].Value != "0")
                warnings.Add($"lineSpacing={lspaceMatch.Groups[1].Value}% → will add LineHeightModifier");

            var overflowMatch = overflowRegex.Match(content, start, length);
            if (overflowMatch.Success)
            {
                int mode = int.Parse(overflowMatch.Groups[1].Value);
                if (mode == 1) warnings.Add("Overflow=Ellipsis → will add EllipsisModifier");
                else if (mode == 3) warnings.Add("Overflow=Truncate → will add TruncateModifier");
                else if (mode >= 2) warnings.Add($"Overflow mode {mode} has no UniText equivalent");
            }

            var weightWarning = fontWeightRegex.Match(content, start, length);
            if (weightWarning.Success && weightWarning.Groups[1].Value != "400")
                warnings.Add($"fontWeight={weightWarning.Groups[1].Value} → VariationModifier on the " +
                             "wght axis; a static font ignores it");

            if (Holds(content, start, length, "m_enableVertexGradient: 1"))
                warnings.Add("Vertex gradient enabled — apply a gradient paint swatch with a whole-text FillModifier in UniText (different model)");

            var hasSpriteTag = Holds(content, start, length, "<sprite");
            if (hasSpriteTag)
            {
                warnings.Add(Holds(content, start, length, "anim=")
                    ? "Animated TMP sprite tags block automatic component migration"
                    : "TMP sprite indices keep their values; attribute forms are normalized and their UniTextSprites catalogs are generated automatically");
            }
            else if (HasAssignedSpriteAsset(content, start, length))
                warnings.Add("A TMP sprite asset is assigned but the serialized text writes no " +
                             "<sprite> tag — no catalog is generated; text assigned at runtime " +
                             "needs a SpriteModifier bound through the Style");

            var paraMatch = paragraphSpacingRegex.Match(content, start, length);
            if (paraMatch.Success && paraMatch.Groups[1].Value != "0")
                warnings.Add($"paragraphSpacing={paraMatch.Groups[1].Value} → will add ParagraphSpacingModifier");

            return warnings.Count > 0 ? warnings : null;
        }

        static void ScanScript(string path, string content, FileScan scan)
        {
            int tmpRefCount = tmpScriptRegex.Matches(content).Count;
            if (tmpRefCount == 0) return;

            var warnings = new List<string>();
            var complexity = MigrationComplexity.Simple;

            if (content.Contains("TextAlignmentOptions"))
            {
                complexity = MigrationComplexity.Moderate;
                warnings.Add("Uses TextAlignmentOptions — needs decomposition into HorizontalAlignment + VerticalAlignment");
            }
            if (content.Contains("TMP_SpriteAsset") || content.Contains("TMP_Dropdown") || content.Contains("textInfo"))
                complexity = MigrationComplexity.Manual;
            if (preprocessorTmpRegex.IsMatch(content))
                warnings.Add("Contains #if TEXTMESHPRO blocks — review manually");

            scan.Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.ScriptReference),
                filePath = path,
                type = FindingType.ScriptReference,
                complexity = complexity,
                details = $"{tmpRefCount} TMP reference{(tmpRefCount > 1 ? "s" : "")}",
                warnings = warnings.Count > 0 ? warnings : null,
            });
        }

        void ScanTmpAsset(string path, string content, FileScan scan)
        {
            foreach (var assetGuid in MigrationMapping.TmpAssetGuids)
            {
                if (!content.Contains(assetGuid)) continue;

                if (assetGuid == MigrationMapping.TmpFontAssetGuid)
                {
                    var nameMatch = assetNameRegex.Match(content);
                    var familyMatch = familyNameRegex.Match(content);
                    var serializedName = nameMatch.Success
                        ? CleanName(nameMatch.Groups[1].Value)
                        : null;
                    var fontName = string.IsNullOrEmpty(serializedName)
                        ? Path.GetFileNameWithoutExtension(path)
                        : serializedName;
                    var serializedFamily = familyMatch.Success
                        ? CleanName(familyMatch.Groups[1].Value)
                        : null;
                    var familyName = string.IsNullOrEmpty(serializedFamily)
                        ? fontName
                        : serializedFamily;

                    scan.FontName = fontName;
                    scan.FontFamily = familyName;
                    scan.HasFont = true;
                    scan.FontFallbackGuids =
                        ReadGuidList(content, MigrationMapping.FontFallbackField) ??
                        ReadGuidList(content, MigrationMapping.LegacyFontFallbackField);

                    var sourcePath = TryFindTtfSource(fontName, familyName);
                    var fallbackCount = scan.FontFallbackGuids?.Count ?? 0;
                    scan.Findings.Add(new MigrationFinding
                    {
                        id = MigrationFinding.ComputeIdForAsset(path, FindingType.FontAsset),
                        filePath = path,
                        type = FindingType.FontAsset,
                        complexity = MigrationComplexity.Moderate,
                        details = $"TMP_FontAsset '{fontName}' (family: {familyName})"
                                  + (sourcePath != null ? $" — source: {sourcePath}" : " — no source TTF/OTF found")
                                  + (fallbackCount > 0 ? $" — {fallbackCount} fallback(s)" : string.Empty),
                    });
                }
                else if (assetGuid != MigrationMapping.TmpSpriteAssetGuid)
                {
                    if (assetGuid == MigrationMapping.TmpSettingsGuid)
                        scan.GlobalFallbackGuids =
                            ReadGuidList(content, MigrationMapping.GlobalFallbackField);

                    var globals = scan.GlobalFallbackGuids?.Count ?? 0;
                    scan.Findings.Add(new MigrationFinding
                    {
                        id = MigrationFinding.ComputeIdForAsset(path, FindingType.TmpAsset),
                        filePath = path,
                        type = FindingType.TmpAsset,
                        complexity = MigrationComplexity.Manual,
                        details = $"{MigrationMapping.GetTmpName(assetGuid)} — no direct UniText equivalent"
                                  + (globals > 0
                                      ? $"; carries {globals} project-wide fallback font(s), rebuilt as one shared stack"
                                      : string.Empty),
                    });
                }

                break;
            }
        }

        /// <summary>
        /// The ordered GUIDs of a YAML object-reference list, or null when the field is absent or
        /// empty. The field name must start a line, so <c>fallbackFontAssets</c> never answers for
        /// <c>m_fallbackFontAssets</c>.
        /// </summary>
        static List<string> ReadGuidList(string content, string field)
        {
            int at = FindFieldLine(content, field);
            if (at < 0) return null;

            int lineEnd = content.IndexOf('\n', at);
            if (lineEnd < 0) lineEnd = content.Length;
            if (content.IndexOf("[]", at, lineEnd - at, StringComparison.Ordinal) >= 0) return null;

            var result = new List<string>();
            int index = lineEnd + 1;
            while (index < content.Length)
            {
                int end = content.IndexOf('\n', index);
                if (end < 0) end = content.Length;
                int item = index;
                while (item < end && (content[item] == ' ' || content[item] == '\t')) item++;
                if (item >= end || content[item] != '-') break;
                var match = referenceGuidRegex.Match(content, item, end - item);
                if (!match.Success) break;
                result.Add(match.Groups[1].Value);
                index = end + 1;
            }
            return result.Count > 0 ? result : null;
        }

        /// <summary>Index just past <c>field:</c> where the name starts a line, or -1.</summary>
        static int FindFieldLine(string content, string field)
        {
            var key = field + ":";
            int at = 0;
            while (true)
            {
                at = content.IndexOf(key, at, StringComparison.Ordinal);
                if (at < 0) return -1;
                var before = at == 0 ? '\n' : content[at - 1];
                if (before == '\n' || before == ' ' || before == '\t')
                    return at + key.Length;
                at += key.Length;
            }
        }

        /// <summary>Nearest font file whose name carries the family, ignoring spacing and case.</summary>
        /// <summary>
        /// The font file a TMP font asset was built from, guessed by name. The asset's own name
        /// decides first, and the longest source name inside it wins — a family name alone cannot
        /// tell Dosis-Bold from Dosis-ExtraBold, and picking either for both silently converts one
        /// weight into the other.
        /// </summary>
        string TryFindTtfSource(string fontName, string familyName)
        {
            var assetName = FontSource.Normalize(fontName);
            if (assetName.Length != 0)
            {
                string best = null;
                var bestLength = 0;
                for (int i = 0; i < fontSources.Count; i++)
                {
                    var candidate = fontSources[i].normalized;
                    if (candidate.Length <= bestLength || !assetName.Contains(candidate)) continue;
                    best = fontSources[i].path;
                    bestLength = candidate.Length;
                }
                if (best != null) return best;
            }

            var family = FontSource.Normalize(familyName);
            if (family.Length == 0) return null;
            for (int i = 0; i < fontSources.Count; i++)
                if (fontSources[i].normalized.Contains(family))
                    return fontSources[i].path;
            return null;
        }

        static void ScanMaterial(string path, string content, FileScan scan)
        {
            var warnings = new List<string>();

            foreach (var recipe in MigrationMapping.MaterialRecipes)
            {
                int at = content.IndexOf(recipe.shaderProperty, StringComparison.Ordinal);
                if (at < 0) continue;
                var lineEnd = content.IndexOf('\n', at);
                if (lineEnd < 0) lineEnd = content.Length;
                var colon = content.IndexOf(':', at, lineEnd - at);
                var valStr = colon < 0 ? "?" : content.Substring(colon + 1, lineEnd - colon - 1).Trim();
                warnings.Add($"{recipe.description} ({recipe.shaderProperty}={valStr}) → {recipe.uniTextEquivalent}");
            }

            scan.Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.Material),
                filePath = path,
                type = FindingType.Material,
                complexity = MigrationComplexity.Moderate,
                details = "TMP material — will be unused after migration",
                warnings = warnings.Count > 0 ? warnings : null,
            });
        }

        static void ScanAnimation(string path, string content, FileScan scan)
        {
            var remappableProps = new List<string>();
            foreach (var kvp in MigrationMapping.AnimationPropertyMap)
            {
                if (content.Contains(kvp.Key))
                    remappableProps.Add($"{kvp.Key} → {kvp.Value}");
            }

            scan.Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.Animation),
                filePath = path,
                type = FindingType.Animation,
                complexity = MigrationComplexity.Complex,
                details = remappableProps.Count > 0
                    ? $"Targets TMP properties: {string.Join(", ", remappableProps)}"
                    : "References TMP component type",
            });
        }

        static void ScanAssemblyDef(string path, string content, FileScan scan)
        {
            if (!tmpAsmRefRegex.IsMatch(content)) return;

            scan.Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.AssemblyDef),
                filePath = path,
                type = FindingType.AssemblyDef,
                complexity = MigrationComplexity.Simple,
                details = "References Unity.TextMeshPro assembly",
            });
        }

        /// <summary>
        /// Every distinct opening tag in the text, in one pass. A name must end at '=', '&gt;' or a
        /// space, so &lt;s&gt; never matches &lt;size=&gt; and &lt;margin&gt; never matches
        /// &lt;margin-left=&gt;.
        /// </summary>
        static List<string> CollectTagNames(string content)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int index = 0;

            while (true)
            {
                index = content.IndexOf('<', index);
                if (index < 0) break;
                int start = ++index;
                int end = start;
                while (end < content.Length)
                {
                    var c = content[end];
                    if (c == '=' || c == '>' || c == ' ') break;
                    if (c == '<' || c == '\n' || c == '\r') { end = start; break; }
                    end++;
                }
                if (end <= start || end >= content.Length) continue;

                var name = content.Substring(start, end - start);
                if (seen.Add(name)) names.Add(name);
            }

            return names;
        }

        static void CollectSharedTags(string content, FileScan scan)
        {
            if (content.IndexOf('<') < 0) return;
            var names = CollectTagNames(content);
            for (int i = 0; i < names.Count; i++)
                if (MigrationMapping.TagVocabulary.ContainsKey(names[i]))
                    scan.SharedTags.Add(names[i]);
        }

        static void ScanTags(string path, string content, FileScan scan)
        {
            var unsupported = new List<string>();
            var unclassified = new List<string>();
            var styleNeeded = new List<string>();
            int manualTags = 0;

            var names = CollectTagNames(content);
            for (int i = 0; i < names.Count; i++)
            {
                var name = names[i];

                if (MigrationMapping.TagVocabulary.ContainsKey(name))
                    scan.SharedTags.Add(name);

                if (MigrationMapping.TagsOwnedElsewhere.Contains(name))
                {
                    styleNeeded.Add($"<{name}> → needs component sprite-asset context; component " +
                                    "migration resolves it only where the tag is in the " +
                                    "component's own serialized text");
                    manualTags++;
                }
                else if (MigrationMapping.UnsupportedTags.Contains(name))
                {
                    unsupported.Add($"<{name}>");
                }
                else if (MigrationMapping.TagVocabulary.TryGetValue(name, out var binding))
                {
                    var source = binding.GraphPresetName != null
                        ? $"the {binding.GraphPresetName} modifier graph"
                        : binding.StandaloneRuleTypeName ?? binding.ModifierTypeName;
                    styleNeeded.Add($"<{name}> → {source}, from the project-wide Style preset");
                }
                else if (MigrationMapping.TagsNeedingManualSetup.TryGetValue(name, out var advice))
                {
                    styleNeeded.Add($"<{name}> → {advice}");
                    manualTags++;
                }
                else if (MigrationMapping.TmpTags.Contains(name))
                {
                    unclassified.Add($"<{name}>");
                }
            }

            if (unsupported.Count == 0 && unclassified.Count == 0 && styleNeeded.Count == 0) return;

            unsupported.Sort(StringComparer.Ordinal);
            unclassified.Sort(StringComparer.Ordinal);
            styleNeeded.Sort(StringComparer.Ordinal);

            var details = new List<string>();
            if (unsupported.Count > 0)
                details.Add($"No UniText modifier: {string.Join(", ", unsupported)}");
            if (unclassified.Count > 0)
                details.Add("TMP markup this migration has no entry for, carried over unchanged: " +
                            string.Join(", ", unclassified));
            if (styleNeeded.Count > 0)
                details.Add($"Needs Style entries: {styleNeeded.Count}");

            scan.Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.RichTextContent),
                filePath = path,
                type = FindingType.RichTextContent,
                complexity = unsupported.Count > 0 || manualTags > 0
                    ? MigrationComplexity.Moderate
                    : MigrationComplexity.Simple,
                details = string.Join("; ", details),
                warnings = styleNeeded.Count > 0 ? styleNeeded : null,
            });
        }

        // ── main thread ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Folds one worker result into the scan, resolving the asset identities a worker cannot
        /// touch, and records the stamp that lets the next scan skip this file.
        /// </summary>
        void Integrate(FileEntry entry, FileScan scan)
        {
            if (scan == null) return;

            if (scan.Failure != null)
            {
                RecordUnreadable(entry.path, scan.Failure);
                return;
            }

            if (scan.FromCache)
            {
                ReusedFiles++;
                ReusedBytes += entry.length;
            }

            for (int i = 0; i < scan.Findings.Count; i++)
                Findings.Add(scan.Findings[i]);

            for (int i = 0; i < scan.SharedTags.Count; i++)
                SharedVocabularyTags.Add(scan.SharedTags[i]);

            if (scan.NestedPrefabGuids.Count > 0)
            {
                var deps = new List<string>();
                for (int i = 0; i < scan.NestedPrefabGuids.Count; i++)
                {
                    var nestedPath = AssetDatabase.GUIDToAssetPath(scan.NestedPrefabGuids[i]);
                    if (!string.IsNullOrEmpty(nestedPath) &&
                        nestedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        deps.Add(nestedPath);
                }
                if (deps.Count > 0) PrefabDependencies[entry.path] = deps;
            }

            if (scan.HasFont)
            {
                DiscoveredFonts.Add(new FontMappingEntry
                {
                    tmpFontGuid = AssetDatabase.AssetPathToGUID(entry.path),
                    tmpFontPath = entry.path,
                    tmpFontName = scan.FontName,
                    tmpFamilyName = scan.FontFamily,
                    sourceTtfPath = TryFindTtfSource(scan.FontName, scan.FontFamily),
                    fallbackGuids = scan.FontFallbackGuids,
                });
            }

            if (scan.GlobalFallbackGuids != null)
            {
                for (int i = 0; i < scan.GlobalFallbackGuids.Count; i++)
                    if (!GlobalFallbackFontGuids.Contains(scan.GlobalFallbackGuids[i]))
                        GlobalFallbackFontGuids.Add(scan.GlobalFallbackGuids[i]);
            }

            ScannedFiles.Add(new ScannedFileRecord
            {
                path = entry.path,
                writeTicks = entry.ticks,
                length = entry.length,
                nestedPrefabGuids = scan.NestedPrefabGuids.Count > 0 ? scan.NestedPrefabGuids : null,
                sharedTags = scan.SharedTags.Count > 0 ? scan.SharedTags : null,
                fontName = scan.HasFont ? scan.FontName : null,
                fontFamily = scan.HasFont ? scan.FontFamily : null,
                fontFallbackGuids = scan.FontFallbackGuids,
                globalFallbackGuids = scan.GlobalFallbackGuids,
                hasFont = scan.HasFont,
            });
        }

        /// <summary>
        /// Returns prefabs in bottom-up migration order (leaves first, parents last).
        /// </summary>
        public List<string> GetPrefabMigrationOrder()
        {
            var visited = new HashSet<string>();
            var order = new List<string>();

            void Visit(string prefab)
            {
                if (!visited.Add(prefab)) return;
                if (PrefabDependencies.TryGetValue(prefab, out var deps))
                {
                    foreach (var dep in deps)
                        Visit(dep);
                }
                order.Add(prefab);
            }

            foreach (var prefab in PrefabDependencies.Keys)
                Visit(prefab);

            foreach (var f in Findings)
            {
                if (f.type == FindingType.Component &&
                    f.filePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    visited.Add(f.filePath))
                    order.Add(f.filePath);
            }

            return order;
        }

        /// <summary>
        /// Records an asset the scan could not read. No <see cref="ScannedFileRecord"/> is stamped
        /// for it — a stamp would answer the next scan from a reading that never happened — and
        /// the finding keeps the file visible instead of letting it pass for clean.
        /// </summary>
        void RecordUnreadable(string path, string reason)
        {
            UnreadableFiles++;
            Findings.Add(new MigrationFinding
            {
                id = MigrationFinding.ComputeIdForAsset(path, FindingType.UnreadableFile),
                filePath = path,
                type = FindingType.UnreadableFile,
                complexity = MigrationComplexity.Manual,
                details = $"'{path}' could not be read — {reason}",
            });
            UnityEngine.Debug.LogWarning($"[UniText] Migration scan could not read '{path}': {reason}");
        }

        void ScanCompiledAssemblies()
        {
            var tmpTypes = new HashSet<string>
            {
                "TMPro.TMP_Text", "TMPro.TextMeshPro", "TMPro.TextMeshProUGUI",
                "TMPro.TMP_InputField", "TMPro.TMP_FontAsset", "TMPro.TMP_Dropdown",
                "TMPro.TMP_SpriteAsset",
            };

            var plugins = CollectPluginAssemblies();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = asm.GetName().Name;
                if (asmName.Contains("TextMeshPro")) continue;
                if (asm.IsDynamic) continue;

                string loc;
                try { loc = asm.Location; }
                catch (NotSupportedException) { continue; }
                if (string.IsNullOrEmpty(loc)) continue;
                if (!plugins.TryGetValue(NormalizePath(loc), out var assetPath)) continue;
                if (IsExcluded(assetPath)) continue;

                try
                {
                    var types = ExportedTypes(asm, out var partial);
                    bool hasTmpRef = false;
                    var tmpMembers = new List<string>();

                    for (int t = 0; t < types.Count; t++)
                    {
                        try
                        {
                            foreach (var method in types[t].GetMethods(
                                         System.Reflection.BindingFlags.Public |
                                         System.Reflection.BindingFlags.Instance |
                                         System.Reflection.BindingFlags.Static))
                            {
                                if (tmpTypes.Contains(method.ReturnType.FullName ?? ""))
                                {
                                    hasTmpRef = true;
                                    tmpMembers.Add($"{types[t].Name}.{method.Name}() → {method.ReturnType.Name}");
                                }
                                foreach (var param in method.GetParameters())
                                {
                                    if (tmpTypes.Contains(param.ParameterType.FullName ?? ""))
                                    {
                                        hasTmpRef = true;
                                        tmpMembers.Add($"{types[t].Name}.{method.Name}({param.ParameterType.Name} {param.Name})");
                                    }
                                }
                            }
                        }
                        catch (Exception) { partial = true; }
                    }

                    if (hasTmpRef || partial)
                    {
                        Findings.Add(new MigrationFinding
                        {
                            id = MigrationFinding.ComputeIdForAsset(asmName, FindingType.CompiledDependency),
                            filePath = assetPath,
                            type = FindingType.CompiledDependency,
                            complexity = MigrationComplexity.Manual,
                            details = hasTmpRef && !partial
                                ? $"Compiled assembly '{asmName}' has public API referencing TMP types"
                                : hasTmpRef
                                    ? $"Compiled assembly '{asmName}' has public API referencing " +
                                      "TMP types, and part of it could not be inspected"
                                    : $"Compiled assembly '{asmName}' could not be fully " +
                                      "inspected — its TMP usage is unknown",
                            warnings = tmpMembers.Count > 0 ? tmpMembers : null,
                        });
                    }
                }
                catch (Exception exception)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[UniText] Migration scan could not inspect '{asmName}': {exception.Message}");
                }
            }
        }

        /// <summary>
        /// Every precompiled plugin the asset database knows, by the filesystem path the CLR
        /// reports for a loaded assembly. Identity comes from the asset database, so Unity's own
        /// compile output under <c>Library/ScriptAssemblies</c> — the project's own scripts —
        /// cannot be mistaken for a plugin somebody else ships.
        /// </summary>
        static Dictionary<string, string> CollectPluginAssemblies()
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var packageRoots = new Dictionary<string, string>();
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!assetPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                string fsPath;
                try
                {
                    fsPath = assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                        ? Path.GetFullPath(assetPath)
                        : ResolveAnyPackagePath(assetPath, packageRoots);
                }
                catch (ArgumentException) { continue; }
                if (fsPath == null) continue;
                result[NormalizePath(fsPath)] = assetPath;
            }
            return result;
        }

        /// <summary>
        /// Where a package asset lives on disk, for any package source. Unlike the writable-file
        /// resolver this accepts registry and cached packages too: an assembly only has to be
        /// recognised here, never rewritten.
        /// </summary>
        static string ResolveAnyPackagePath(string assetPath, Dictionary<string, string> cache)
        {
            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal)) return null;
            var slash = assetPath.IndexOf('/', "Packages/".Length);
            var root = slash < 0 ? assetPath : assetPath.Substring(0, slash);
            if (!cache.TryGetValue(root, out var resolvedRoot))
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                resolvedRoot = info?.resolvedPath;
                cache[root] = resolvedRoot;
            }
            return resolvedRoot == null ? null : resolvedRoot + assetPath.Substring(root.Length);
        }

        static string NormalizePath(string path) => path.Replace('\\', '/');

        /// <summary>
        /// The assembly's exported types, keeping whatever loaded when some did not.
        /// <paramref name="partial"/> reports that the list is incomplete, so a clean result is
        /// never mistaken for proof that nothing references TMP.
        /// </summary>
        static List<Type> ExportedTypes(System.Reflection.Assembly assembly, out bool partial)
        {
            partial = false;
            try
            {
                return new List<Type>(assembly.GetExportedTypes());
            }
            catch (System.Reflection.ReflectionTypeLoadException exception)
            {
                partial = true;
                var types = new List<Type>();
                if (exception.Types == null) return types;
                for (int i = 0; i < exception.Types.Length; i++)
                    if (exception.Types[i] != null) types.Add(exception.Types[i]);
                return types;
            }
        }

        // ── supporting types ────────────────────────────────────────────────────────────────

        enum FileKind : byte
        {
            None,
            Scene,
            Prefab,
            Script,
            Asset,
            Material,
            Animation,
            AssemblyDef,
            TextContent,
            Font,
        }

        /// <summary>
        /// One file the scan will read. <c>path</c> is the asset path every finding and every
        /// editor call is keyed by; <c>fsPath</c> is where the bytes actually are, which differs
        /// for a local package.
        /// </summary>
        readonly struct FileEntry
        {
            public readonly string path;
            public readonly string fsPath;
            public readonly FileKind kind;
            public readonly long ticks;
            public readonly long length;

            public FileEntry(string path, string fsPath, FileKind kind, long ticks, long length)
            {
                this.path = path;
                this.fsPath = fsPath;
                this.kind = kind;
                this.ticks = ticks;
                this.length = length;
            }
        }

        readonly struct FontSource
        {
            public readonly string path;
            public readonly string normalized;

            public FontSource(string path)
            {
                this.path = path;
                normalized = Normalize(Path.GetFileNameWithoutExtension(path));
            }

            public static string Normalize(string value)
            {
                if (string.IsNullOrEmpty(value)) return string.Empty;
                var builder = new StringBuilder(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    var c = value[i];
                    if (c == ' ' || c == '-' || c == '_' || c == '.') continue;
                    builder.Append(char.ToLowerInvariant(c));
                }
                return builder.ToString();
            }
        }

        /// <summary>What one file contributed, before its asset identities are resolved.</summary>
        sealed class FileScan
        {
            public static readonly FileScan Empty = new();

            /// <summary>Why the file was not read, or null when it was. Set per entry, never on <see cref="Empty"/>.</summary>
            public string Failure;

            public readonly List<MigrationFinding> Findings = new();
            public readonly List<string> NestedPrefabGuids = new();
            public readonly List<string> SharedTags = new();
            public List<string> FontFallbackGuids;
            public List<string> GlobalFallbackGuids;
            public string FontName;
            public string FontFamily;
            public bool HasFont;
            public bool FromCache;
        }

        readonly struct CachedFile
        {
            readonly ScannedFileRecord record;
            readonly List<MigrationFinding> findings;

            public CachedFile(ScannedFileRecord record, List<MigrationFinding> findings)
            {
                this.record = record;
                this.findings = findings;
            }

            public bool Matches(FileEntry entry)
                => record != null && record.writeTicks == entry.ticks && record.length == entry.length;

            public FileScan ToScan()
            {
                var scan = new FileScan { FromCache = true };
                if (findings != null) scan.Findings.AddRange(findings);
                if (record.nestedPrefabGuids != null)
                    scan.NestedPrefabGuids.AddRange(record.nestedPrefabGuids);
                if (record.sharedTags != null)
                    scan.SharedTags.AddRange(record.sharedTags);
                scan.FontFallbackGuids = record.fontFallbackGuids;
                scan.GlobalFallbackGuids = record.globalFallbackGuids;
                scan.HasFont = record.hasFont;
                scan.FontName = record.fontName;
                scan.FontFamily = record.fontFamily;
                return scan;
            }
        }
    }
}
