using System;
using System.Collections.Generic;
using System.Text;

namespace LightSide
{
    public enum YamlKind { Scalar, Map, Sequence }

    /// <summary>
    /// A parsed YAML node carrying its exact char span in the source (<see cref="Start"/>..<see cref="End"/>,
    /// End exclusive). Spans are what make byte-preserving edits possible: navigate the tree, then splice the
    /// original text at a node's span rather than re-emitting the document.
    /// </summary>
    public sealed class YamlNode
    {
        public YamlKind Kind;
        public int Start;
        public int End;
        public string Scalar;
        public List<YamlEntry> Entries;
        public List<YamlNode> Items;

        public bool IsScalar => Kind == YamlKind.Scalar;

        public YamlNode this[string key] => Entry(key)?.Value;

        public YamlEntry Entry(string key)
        {
            if (Entries == null) return null;
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Key == key) return Entries[i];
            return null;
        }
    }

    public sealed class YamlEntry
    {
        public string Key;
        public int KeyStart;
        public int KeyEnd;
        public YamlNode Value;

        /// <summary>Full span of the entry including all its lines and trailing newline — the unit to move or delete.</summary>
        public int Start;
        public int End;
    }

    /// <summary>
    /// Accumulates span replacements and applies them to the source in one pass. Ops must not overlap;
    /// they are applied back-to-front so earlier splices don't shift later offsets.
    /// </summary>
    public sealed class YamlEdit
    {
        readonly List<(int start, int end, string text, int order)> ops = new();

        public YamlEdit(string source) => Source = source;

        public string Source { get; }

        public bool HasChanges => ops.Count > 0;

        public string Slice(int start, int end) => Source.Substring(start, end - start);

        public void Replace(int start, int end, string text) => ops.Add((start, end, text, ops.Count));
        public void Insert(int at, string text) => ops.Add((at, at, text, ops.Count));
        public void Delete(int start, int end) => ops.Add((start, end, "", ops.Count));

        public string Apply()
        {
            ops.Sort((a, b) =>
            {
                var byStart = b.start.CompareTo(a.start);
                if (byStart != 0) return byStart;
                var byEnd = b.end.CompareTo(a.end);
                return byEnd != 0 ? byEnd : b.order.CompareTo(a.order);
            });

            int rightStart = Source.Length;
            foreach (var (start, end, _, _) in ops)
            {
                if (start < 0 || end < start || end > Source.Length)
                    throw new InvalidOperationException(
                        $"YAML edit span {start}..{end} is outside source length {Source.Length}.");
                if (end > rightStart)
                    throw new InvalidOperationException(
                        $"YAML edit span {start}..{end} overlaps another edit.");
                rightStart = start;
            }

            var sb = new StringBuilder(Source);
            foreach (var (start, end, text, _) in ops)
            {
                sb.Remove(start, end - start);
                sb.Insert(start, text);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// One Unity object document: the <c>--- !u!classId &amp;fileId</c> block and its parsed tree. Exposes the
    /// stable handles migrations select on — object type, script guid, managed references.
    /// </summary>
    public sealed class YamlDocument
    {
        public long ClassId;
        public long FileId;
        public bool Stripped;
        public YamlNode Root;

        /// <summary>Serialized body below the Unity object type.</summary>
        public YamlNode Body => Root?.Entries is { Count: > 0 } ? Root.Entries[0].Value : null;

        /// <summary>Serialized object kind, e.g. <c>MonoBehaviour</c>, <c>GameObject</c>, <c>PrefabInstance</c>.</summary>
        public string ObjectType => Root?.Entries is { Count: > 0 } ? Root.Entries[0].Key : null;

        /// <summary>Guid of the backing script for <c>MonoBehaviour</c>/<c>ScriptableObject</c> documents; null otherwise.</summary>
        public string ScriptGuid => Body?["m_Script"]?["guid"]?.Scalar;

        /// <summary>Each <c>[SerializeReference]</c> managed-reference item (has <c>type</c> + <c>data</c>).</summary>
        public IEnumerable<YamlNode> ManagedReferences()
        {
            if (Body?["references"]?["RefIds"] is { Kind: YamlKind.Sequence, Items: { } items })
                foreach (var item in items)
                    if (item.Kind == YamlKind.Map)
                        yield return item;
        }

        /// <summary>
        /// Each prefab-instance modification that names the type of an instance-owned managed reference.
        /// Those references never appear in <see cref="ManagedReferences"/>: their identity lives in the
        /// modification value and their fields in sibling <c>managedReferences[rid].field</c> entries, so a
        /// migration that rewrites a serialized type must cover both shapes.
        /// </summary>
        public IEnumerable<ManagedReferenceOverride> ManagedReferenceOverrides()
        {
            if (Body?["m_Modification"]?["m_Modifications"] is
                { Kind: YamlKind.Sequence, Items: { } items })
                foreach (var item in items)
                {
                    if (item.Kind != YamlKind.Map) continue;
                    var value = item["value"];
                    if (value is not { IsScalar: true } ||
                        !NamesManagedReference(item["propertyPath"]?.Scalar) ||
                        !TypeSignature.TryParse(value.Scalar, out var type))
                        continue;
                    yield return new ManagedReferenceOverride(value, type);
                }
        }

        static bool NamesManagedReference(string propertyPath)
        {
            const string prefix = "managedReferences[";
            if (propertyPath == null) return false;

            var path = UnityYaml.Unquote(propertyPath);
            if (path.Length <= prefix.Length + 1 || path[path.Length - 1] != ']' ||
                !path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            for (int i = prefix.Length; i < path.Length - 1; i++)
                if ((path[i] < '0' || path[i] > '9') && !(i == prefix.Length && path[i] == '-'))
                    return false;
            return true;
        }
    }

    /// <summary>
    /// A prefab-instance modification that assigns the type of an instance-owned managed reference: the
    /// scalar node carrying <see cref="TypeSignature.Identity"/> and the type it names.
    /// </summary>
    public readonly struct ManagedReferenceOverride
    {
        /// <summary>Scalar holding the whole identity string — the span to splice on a rename.</summary>
        public readonly YamlNode Value;

        /// <summary>Managed type the modification names.</summary>
        public readonly TypeSignature Type;

        /// <summary>Pairs an identity scalar with the type parsed from it.</summary>
        public ManagedReferenceOverride(YamlNode value, TypeSignature type)
        {
            Value = value;
            Type = type;
        }
    }

    /// <summary>
    /// Minimal reader for Unity's text-serialized YAML (2021+). Indentation-driven, so it tolerates Unity's
    /// exact column choices across versions. Parses whole documents into node trees with source spans for
    /// byte-preserving splices. Not a general YAML implementation — covers what Unity emits.
    /// </summary>
    public static class UnityYaml
    {
        /// <summary>Finds the document header that owns a parsed object.</summary>
        public static int DocumentStart(string source, YamlDocument document)
        {
            var marker = source.LastIndexOf("\n--- !u!", document.Root.Start,
                StringComparison.Ordinal);
            if (marker >= 0) return marker + 1;
            if (source.StartsWith("--- !u!", StringComparison.Ordinal)) return 0;
            throw new InvalidOperationException(
                $"Cannot find YAML header for object {document.FileId}.");
        }

        /// <summary>Reads a Unity object reference fileID, or zero when absent.</summary>
        public static long FileId(YamlNode node)
            => node?["fileID"]?.Scalar is { } value && long.TryParse(value, out var id)
                ? id
                : 0;

        /// <summary>Claims the first unused identifier at or below the supplied candidate.</summary>
        public static long AllocateId(HashSet<long> used, long candidate)
        {
            while (!used.Add(candidate)) candidate--;
            return candidate;
        }

        /// <summary>Strips the surrounding quotes Unity adds to a scalar when its content needs them.</summary>
        public static string Unquote(string value) =>
            value != null && value.Length >= 2 && (value[0] == '\'' || value[0] == '"') &&
            value[value.Length - 1] == value[0]
                ? value.Substring(1, value.Length - 2)
                : value;

        /// <summary>Returns the leading spaces of the source line containing a position.</summary>
        public static string Indent(string source, int position)
        {
            var line = position > 0 ? source.LastIndexOf('\n', position - 1) + 1 : 0;
            var end = line;
            while (end < source.Length && source[end] == ' ') end++;
            return source.Substring(line, end - line);
        }

        /// <summary>Appends an existing YAML span with every content line indented by the requested amount.</summary>
        public static void AppendReindented(string span, int spaces, StringBuilder output)
        {
            var position = 0;
            while (position < span.Length)
            {
                var newline = span.IndexOf('\n', position);
                var lineEnd = newline < 0 ? span.Length : newline + 1;
                if (HasContent(span, position, lineEnd)) output.Append(' ', spaces);
                output.Append(span, position, lineEnd - position);
                position = lineEnd;
            }
        }

        private static bool HasContent(string value, int from, int to)
        {
            for (var i = from; i < to; i++)
                if (value[i] != ' ' && value[i] != '\r' && value[i] != '\n') return true;
            return false;
        }

        /// <summary>Appends items to a block or empty inline sequence.</summary>
        public static void AppendSequence(MigrationContext context, YamlEntry entry,
            string additions, string missingMessage, string invalidShapeMessage = null)
        {
            if (entry?.Value == null)
                throw new InvalidOperationException(missingMessage);
            if (entry.Value.Kind == YamlKind.Sequence)
            {
                context.Edit.Insert(entry.Value.End, additions);
                return;
            }
            if (entry.Value.Scalar != "[]")
                throw new InvalidOperationException(invalidShapeMessage ?? missingMessage);
            var pad = Indent(context.Edit.Source, entry.Start);
            context.Edit.Replace(entry.Start, entry.End,
                pad + entry.Key + ":\n" + additions);
        }

        /// <summary>Parses every Unity object document in the file (each <c>--- !u!classId &amp;fileId</c> block).</summary>
        public static List<YamlDocument> ParseDocuments(string text)
        {
            var lines = SplitLines(text);
            var docs = new List<YamlDocument>();
            int i = 0;
            while (i < lines.Count)
            {
                var ln = lines[i];
                if (ln.IsBlank || ln.IsComment || (ln.IsMarker && ln.First == '%')) { i++; continue; }

                if (ln.IsMarker && ln.First == '-')
                {
                    var doc = new YamlDocument();
                    ParseHeader(text, ln, doc);
                    i++;
                    int before = i;
                    doc.Root = ParseNode(text, lines, ref i, -1);
                    if (i == before) i++;
                    docs.Add(doc);
                    continue;
                }

                i++;
            }
            return docs;
        }

        static void ParseHeader(string text, Line ln, YamlDocument doc)
        {
            int end = ln.ContentEnd;
            int bang = IndexOf(text, ln.ContentStart, end, "!u!");
            if (bang >= 0) doc.ClassId = ReadLong(text, bang + 3, end);
            int amp = IndexOf(text, ln.ContentStart, end, "&");
            if (amp >= 0) doc.FileId = ReadLong(text, amp + 1, end);
            doc.Stripped = IndexOf(text, ln.ContentStart, end, "stripped") >= 0;
        }

        static int IndexOf(string text, int from, int to, string token)
        {
            int limit = to - token.Length;
            for (int i = from; i <= limit; i++)
            {
                bool hit = true;
                for (int k = 0; k < token.Length; k++)
                    if (text[i + k] != token[k]) { hit = false; break; }
                if (hit) return i;
            }
            return -1;
        }

        static long ReadLong(string text, int from, int to)
        {
            while (from < to && text[from] == ' ') from++;
            int start = from;
            while (from < to && text[from] >= '0' && text[from] <= '9') from++;
            return from > start && long.TryParse(text.Substring(start, from - start), out var v) ? v : 0;
        }

        readonly struct Line
        {
            public readonly int Start;
            public readonly int ContentStart;
            public readonly int ContentEnd;
            public readonly int End;
            public readonly int Indent;
            public readonly char First;
            public readonly bool IsBlank;
            public readonly bool IsComment;
            public readonly bool IsMarker;

            public Line(int start, int contentStart, int contentEnd, int end, char first, bool isMarker)
            {
                Start = start;
                ContentStart = contentStart;
                ContentEnd = contentEnd;
                End = end;
                Indent = contentStart - start;
                IsBlank = contentEnd <= contentStart;
                First = first;
                IsComment = first == '#';
                IsMarker = isMarker;
            }
        }

        static List<Line> SplitLines(string text)
        {
            var result = new List<Line>(1024);
            int n = text.Length, pos = 0;
            while (pos < n)
            {
                int start = pos;
                int end = text.IndexOf('\n', pos);
                if (end < 0) end = n; else end++;

                int contentStart = start;
                while (contentStart < end && text[contentStart] == ' ') contentStart++;

                int contentEnd = end;
                while (contentEnd > contentStart && (text[contentEnd - 1] == '\n' || text[contentEnd - 1] == '\r' || text[contentEnd - 1] == ' '))
                    contentEnd--;

                char first = contentEnd > contentStart ? text[contentStart] : '\0';
                bool isMarker = first == '%'
                                || (first == '-' && contentEnd - contentStart >= 3
                                    && text[contentStart + 1] == '-' && text[contentStart + 2] == '-');
                result.Add(new Line(start, contentStart, contentEnd, end, first, isMarker));
                pos = end;
            }
            return result;
        }

        static YamlNode ParseNode(string text, List<Line> lines, ref int i, int parentIndent)
        {
            SkipTrivia(lines, ref i);
            if (i >= lines.Count) return null;

            var ln = lines[i];
            if (ln.IsMarker || ln.Indent <= parentIndent) return null;

            if (ln.First == '-' && IsSequenceDash(text, ln))
                return ParseSequence(text, lines, ref i, ln.Indent);

            if (HasKeyColon(text, ln, out _))
                return ParseMap(text, lines, ref i, ln.Indent);

            var scalar = new YamlNode
            {
                Kind = YamlKind.Scalar,
                Start = ln.ContentStart,
                End = ln.ContentEnd,
                Scalar = text.Substring(ln.ContentStart, ln.ContentEnd - ln.ContentStart),
            };
            i++;
            return scalar;
        }

        static YamlNode ParseMap(string text, List<Line> lines, ref int i, int indent)
        {
            var node = new YamlNode { Kind = YamlKind.Map, Entries = new List<YamlEntry>() };
            int start = lines[i].Start, end = lines[i].End;

            while (i < lines.Count)
            {
                SkipTrivia(lines, ref i);
                if (i >= lines.Count) break;
                var ln = lines[i];
                if (ln.IsMarker || ln.Indent != indent) break;
                if (ln.First == '-' && IsSequenceDash(text, ln)) break;
                if (!HasKeyColon(text, ln, out int colon)) break;

                var entry = ParseEntry(text, lines, ref i, ln, ln.ContentStart, colon, indent);
                node.Entries.Add(entry);
                end = entry.End;
            }

            node.Start = start;
            node.End = end;
            return node;
        }

        static YamlEntry ParseEntry(string text, List<Line> lines, ref int i, Line ln, int keyStart, int colon, int indent)
        {
            var entry = new YamlEntry
            {
                Key = text.Substring(keyStart, colon - keyStart),
                KeyStart = keyStart,
                KeyEnd = colon,
                Start = ln.Start,
            };

            int valueStart = colon + 1;
            while (valueStart < ln.ContentEnd && text[valueStart] == ' ') valueStart++;

            if (valueStart >= ln.ContentEnd)
            {
                i++;
                entry.Value = ParseChildOfKey(text, lines, ref i, indent);
                entry.End = entry.Value?.End ?? ln.End;
            }
            else if (TryParseMultilineQuoted(text, lines, ref i, valueStart, ln, out var quoted, out var entryEnd))
            {
                entry.Value = quoted;
                entry.End = entryEnd;
            }
            else
            {
                entry.Value = ParseInlineValue(text, valueStart, ln.ContentEnd);
                entry.End = ln.End;
                i++;
            }
            return entry;
        }

        /// <summary>
        /// Unity wraps string values containing line breaks as multi-line quoted scalars whose continuation
        /// lines carry arbitrary indentation — the one place indentation-driven parsing must yield to the
        /// quote. Consumes through the closing quote so the rest of the document keeps parsing.
        /// </summary>
        static bool TryParseMultilineQuoted(string text, List<Line> lines, ref int i, int valueStart, Line ln, out YamlNode value, out int end)
        {
            value = null;
            end = 0;
            char q = text[valueStart];
            if (q != '\'' && q != '"') return false;

            int close = FindQuoteEnd(text, valueStart);
            if (close < ln.ContentEnd) return false;

            i++;
            while (i < lines.Count && lines[i].Start <= close) i++;
            var last = lines[i - 1];
            value = new YamlNode
            {
                Kind = YamlKind.Scalar,
                Start = valueStart,
                End = last.ContentEnd,
                Scalar = text.Substring(valueStart, last.ContentEnd - valueStart),
            };
            end = last.End;
            return true;
        }

        static int FindQuoteEnd(string text, int quoteStart)
        {
            char q = text[quoteStart];
            for (int k = quoteStart + 1; k < text.Length; k++)
            {
                char c = text[k];
                if (q == '\'')
                {
                    if (c != '\'') continue;
                    if (k + 1 < text.Length && text[k + 1] == '\'') { k++; continue; }
                    return k;
                }
                if (c == '\\') { k++; continue; }
                if (c == '"') return k;
            }
            return text.Length - 1;
        }

        /// <summary>
        /// Parses a key's block value. A child mapping/scalar must be indented deeper than the key, but a
        /// block sequence may sit at the same indent as its key (Unity emits <c>RefIds:</c> exactly so).
        /// </summary>
        static YamlNode ParseChildOfKey(string text, List<Line> lines, ref int i, int keyIndent)
        {
            SkipTrivia(lines, ref i);
            if (i >= lines.Count) return null;

            var ln = lines[i];
            if (ln.IsMarker) return null;

            if (ln.First == '-' && IsSequenceDash(text, ln) && ln.Indent >= keyIndent)
                return ParseSequence(text, lines, ref i, ln.Indent);

            if (ln.Indent > keyIndent)
                return ParseNode(text, lines, ref i, keyIndent);

            return null;
        }

        static YamlNode ParseSequence(string text, List<Line> lines, ref int i, int indent)
        {
            var node = new YamlNode { Kind = YamlKind.Sequence, Items = new List<YamlNode>() };
            int start = lines[i].Start, end = lines[i].End;

            while (i < lines.Count)
            {
                SkipTrivia(lines, ref i);
                if (i >= lines.Count) break;
                var ln = lines[i];
                if (ln.IsMarker || ln.Indent != indent) break;
                if (!(ln.First == '-' && IsSequenceDash(text, ln))) break;

                var item = ParseSeqItem(text, lines, ref i, ln, indent);
                node.Items.Add(item);
                if (item != null) end = item.End;
            }

            node.Start = start;
            node.End = end;
            return node;
        }

        static YamlNode ParseSeqItem(string text, List<Line> lines, ref int i, Line ln, int dashIndent)
        {
            int after = ln.ContentStart + 1;
            while (after < ln.ContentEnd && text[after] == ' ') after++;
            int contentCol = after - ln.Start;

            if (after >= ln.ContentEnd)
            {
                i++;
                var child = ParseNode(text, lines, ref i, dashIndent);
                if (child != null) child.Start = ln.Start;
                return child;
            }

            if (text[after] == '{' || text[after] == '[')
            {
                var flow = ParseInlineValue(text, after, ln.ContentEnd);
                flow.Start = ln.Start;
                i++;
                return flow;
            }

            if (HasKeyColonAt(text, after, ln.ContentEnd, out int colon))
            {
                var map = new YamlNode { Kind = YamlKind.Map, Entries = new List<YamlEntry>(), Start = ln.Start };
                var first = ParseEntry(text, lines, ref i, ln, after, colon, contentCol);
                map.Entries.Add(first);
                int end = first.End;

                while (i < lines.Count)
                {
                    SkipTrivia(lines, ref i);
                    if (i >= lines.Count) break;
                    var cur = lines[i];
                    if (cur.IsMarker || cur.Indent != contentCol) break;
                    if (cur.First == '-' && IsSequenceDash(text, cur)) break;
                    if (!HasKeyColon(text, cur, out int c)) break;
                    var e = ParseEntry(text, lines, ref i, cur, cur.ContentStart, c, contentCol);
                    map.Entries.Add(e);
                    end = e.End;
                }

                map.End = end;
                return map;
            }

            if (TryParseMultilineQuoted(text, lines, ref i, after, ln, out var quotedItem, out var quotedEnd))
            {
                quotedItem.Start = ln.Start;
                quotedItem.End = quotedEnd;
                return quotedItem;
            }

            var scalar = new YamlNode
            {
                Kind = YamlKind.Scalar,
                Start = ln.Start,
                End = ln.End,
                Scalar = text.Substring(after, ln.ContentEnd - after),
            };
            i++;
            return scalar;
        }

        static YamlNode ParseInlineValue(string text, int valueStart, int lineEnd)
        {
            if (text[valueStart] == '{')
                return ParseFlowMap(text, valueStart, lineEnd);

            return new YamlNode
            {
                Kind = YamlKind.Scalar,
                Start = valueStart,
                End = lineEnd,
                Scalar = text.Substring(valueStart, lineEnd - valueStart),
            };
        }

        static YamlNode ParseFlowMap(string text, int open, int limit)
        {
            var node = new YamlNode { Kind = YamlKind.Map, Entries = new List<YamlEntry>(), Start = open };
            int depth = 0, close = limit;
            for (int k = open; k < limit; k++)
            {
                char c = text[k];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') { depth--; if (depth == 0) { close = k; break; } }
            }

            int segStart = open + 1;
            depth = 0;
            for (int k = open + 1; k <= close; k++)
            {
                char c = k < close ? text[k] : ',';
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    AddFlowEntry(text, segStart, k, node.Entries);
                    segStart = k + 1;
                }
            }

            node.End = close + 1;
            return node;
        }

        static void AddFlowEntry(string text, int start, int end, List<YamlEntry> entries)
        {
            while (start < end && text[start] == ' ') start++;
            if (start >= end) return;
            if (!HasKeyColonAt(text, start, end, out int colon)) return;

            int valueStart = colon + 1;
            while (valueStart < end && text[valueStart] == ' ') valueStart++;
            int valueEnd = end;
            while (valueEnd > valueStart && text[valueEnd - 1] == ' ') valueEnd--;

            entries.Add(new YamlEntry
            {
                Key = text.Substring(start, colon - start),
                KeyStart = start,
                KeyEnd = colon,
                Start = start,
                End = end,
                Value = new YamlNode
                {
                    Kind = YamlKind.Scalar,
                    Start = valueStart,
                    End = valueEnd,
                    Scalar = text.Substring(valueStart, valueEnd - valueStart),
                },
            });
        }

        static bool IsSequenceDash(string text, Line ln)
        {
            int c = ln.ContentStart;
            return c < ln.ContentEnd && text[c] == '-' &&
                   (c + 1 >= ln.ContentEnd || text[c + 1] == ' ');
        }

        static bool HasKeyColon(string text, Line ln, out int colon) =>
            HasKeyColonAt(text, ln.ContentStart, ln.ContentEnd, out colon);

        static bool HasKeyColonAt(string text, int from, int to, out int colon)
        {
            for (int k = from; k < to; k++)
            {
                char c = text[k];
                if (c == '{' || c == '[' || c == '"' || c == '\'' || c == '#') break;
                if (c == ':' && (k + 1 >= to || text[k + 1] == ' '))
                {
                    colon = k;
                    return k > from;
                }
            }
            colon = -1;
            return false;
        }

        static void SkipTrivia(List<Line> lines, ref int i)
        {
            while (i < lines.Count && (lines[i].IsBlank || lines[i].IsComment)) i++;
        }
    }
}
