using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LightSide
{
    internal static class WordSegmentationDictionaryCompiler
    {
        private const int SparseMagic = 0x32445357;

        internal readonly struct WordEntry
        {
            public readonly string Word;
            public readonly int Cost;

            public WordEntry(string word, int cost)
            {
                Word = word;
                Cost = cost;
            }
        }

        public static byte[] Compile(IEnumerable<string> source, out UnicodeScript script)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var entries = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var value in source)
            {
                if (!TryParse(value, out var word, out var cost)) continue;
                if (!entries.TryGetValue(word, out var current) || cost < current)
                    entries[word] = cost;
            }

            return Compile(entries, out script, nameof(source));
        }

        public static byte[] Compile(IEnumerable<WordEntry> source, out UnicodeScript script)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var entries = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in source)
            {
                var word = entry.Word?.Trim();
                if (string.IsNullOrEmpty(word)) continue;
                if (entry.Cost < 0)
                    throw new FormatException($"Invalid dictionary cost for '{word}'.");
                ValidateUnicodeScalars(word);
                if (!entries.TryGetValue(word, out var current) || entry.Cost < current)
                    entries[word] = entry.Cost;
            }

            return Compile(entries, out script, nameof(source));
        }

        public static UnicodeScript DetectScript(IEnumerable<string> source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));

            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in source)
                if (TryParse(value, out var word, out _))
                    words.Add(word);

            if (words.Count == 0)
                throw new ArgumentException("A dictionary must contain at least one word.", nameof(source));

            return DetectWordsScript(words);
        }

        public static List<WordEntry> Decompile(byte[] data)
        {
            if (data == null || data.Length < 12)
                throw new ArgumentException("Invalid trie data: too short.", nameof(data));

            return ReadInt32(data, 0) == SparseMagic
                ? DecompileSparse(data)
                : DecompileLegacy(data);
        }

        private static byte[] Compile(
            Dictionary<string, int> entries, out UnicodeScript script, string parameterName)
        {
            if (entries.Count == 0)
                throw new ArgumentException("A dictionary must contain at least one word.", parameterName);

            script = DetectWordsScript(entries.Keys);
            return Build(entries);
        }

        private static byte[] Build(Dictionary<string, int> entries)
        {
            var nodes = new List<Node> { new() };
            var orderedEntries = new List<KeyValuePair<string, int>>(entries);
            orderedEntries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

            foreach (var entry in orderedEntries)
            {
                var state = 0;
                foreach (var codepoint in EnumerateCodepoints(entry.Key))
                {
                    var children = nodes[state].children;
                    int next;
                    if (children == null || !children.TryGetValue(codepoint, out next))
                    {
                        next = nodes.Count;
                        children ??= nodes[state].children = new SortedDictionary<int, int>();
                        children.Add(codepoint, next);
                        nodes.Add(new Node());
                    }

                    state = next;
                }

                nodes[state].wordCost = entry.Value;
            }

            var edgeCount = 0;
            for (var i = 0; i < nodes.Count; i++) edgeCount += nodes[i].children?.Count ?? 0;

            var result = new byte[checked(12 + nodes.Count * 12 + edgeCount * 8)];
            WriteInt32(result, 0, SparseMagic);
            WriteInt32(result, 4, nodes.Count);
            WriteInt32(result, 8, edgeCount);

            var stateOffset = 12;
            var firstEdge = 0;
            for (var state = 0; state < nodes.Count; state++)
            {
                var node = nodes[state];
                var childCount = node.children?.Count ?? 0;
                WriteInt32(result, stateOffset, firstEdge);
                WriteInt32(result, stateOffset + 4, childCount);
                WriteInt32(result, stateOffset + 8, node.wordCost);
                stateOffset += 12;
                firstEdge += childCount;
            }

            var edgeOffset = 12 + nodes.Count * 12;
            for (var state = 0; state < nodes.Count; state++)
            {
                if (nodes[state].children == null) continue;
                foreach (var edge in nodes[state].children)
                {
                    WriteInt32(result, edgeOffset, edge.Key);
                    WriteInt32(result, edgeOffset + 4, edge.Value);
                    edgeOffset += 8;
                }
            }

            return result;
        }

        private static UnicodeScript DetectWordsScript(IEnumerable<string> words)
        {
            var provider = UnicodeData.Provider;
            var scripts = new Dictionary<UnicodeScript, string>();
            var contextualScripts = new Dictionary<UnicodeScript, string>();
            var wordScripts = new List<UnicodeScript>(4);

            foreach (var word in words)
            {
                CollectWordScripts(word, provider, wordScripts);
                for (var i = 0; i < wordScripts.Count; i++)
                {
                    var script = wordScripts[i];
                    scripts.TryAdd(script, word);
                    if (WordSegmentationProcessor.IsContextualDictionaryScript(script))
                        contextualScripts.TryAdd(script, word);
                }
            }

            var target = ResolveTargetScript(scripts, contextualScripts);
            var issues = new List<string>();
            var issueCount = 0;

            foreach (var word in words)
            {
                CollectWordScripts(word, provider, wordScripts);
                if (wordScripts.Count == 0 || wordScripts.Contains(target)) continue;
                issueCount++;
                if (issues.Count < 20)
                    issues.Add($"Dictionary word '{word}' does not contain the detected target " +
                               $"{DisplayScript(target)}; found {DisplayScripts(wordScripts)}.");
            }

            if (issueCount > issues.Count)
                issues.Add($"...and {issueCount - issues.Count:N0} more incompatible words.");
            if (issues.Count > 0)
                throw new FormatException(string.Join("\n", issues));

            return target;
        }

        private static UnicodeScript ResolveTargetScript(
            Dictionary<UnicodeScript, string> scripts,
            Dictionary<UnicodeScript, string> contextualScripts)
        {
            if (contextualScripts.Count == 1)
                foreach (var script in contextualScripts.Keys)
                    return script;

            var candidates = contextualScripts.Count > 1 ? contextualScripts : scripts;
            if (candidates.Count == 0)
                throw new FormatException(
                    "Dictionary script cannot be inferred because it contains no script-specific characters.");
            if (candidates.Count > 1)
            {
                var samples = new List<string>(candidates.Count);
                foreach (var pair in candidates)
                    samples.Add($"{DisplayScript(pair.Key)} in '{pair.Value}'");
                samples.Sort(StringComparer.Ordinal);
                throw new FormatException(
                    "Dictionary contains multiple target scripts: " + string.Join(", ", samples) + ".");
            }

            foreach (var script in candidates.Keys) return script;
            throw new InvalidOperationException("Dictionary script inference produced no result.");
        }

        private static void CollectWordScripts(
            string word, UnicodeDataProvider provider, List<UnicodeScript> result)
        {
            result.Clear();
            foreach (var codepoint in EnumerateCodepoints(word))
            {
                var script = provider.GetScript(codepoint);
                if (IsImplicitScript(script)) continue;
                AddScript(result, script);
            }

            if (result.Count > 0) return;

            foreach (var codepoint in EnumerateCodepoints(word))
            {
                var extensions = provider.GetScriptExtensions(codepoint);
                for (var i = 0; i < extensions.Length; i++)
                {
                    var script = extensions[i];
                    if (IsImplicitScript(script)) continue;
                    AddScript(result, script);
                }
            }
        }

        private static void AddScript(List<UnicodeScript> scripts, UnicodeScript script)
        {
            script = WordSegmentationProcessor.CanonicalizeDictionaryScript(script);
            if (!scripts.Contains(script)) scripts.Add(script);
        }

        private static bool IsImplicitScript(UnicodeScript script)
            => script == UnicodeScript.Unknown ||
               script == UnicodeScript.Common ||
               script == UnicodeScript.Inherited;

        private static string DisplayScript(UnicodeScript script)
            => script == UnicodeScript.Han ? "CJK (Han)" : script.ToString();

        private static string DisplayScripts(List<UnicodeScript> scripts)
        {
            var names = new string[scripts.Count];
            for (var i = 0; i < scripts.Count; i++) names[i] = DisplayScript(scripts[i]);
            Array.Sort(names, StringComparer.Ordinal);
            return string.Join(", ", names);
        }

        private static List<WordEntry> DecompileSparse(byte[] data)
        {
            var stateCount = ReadInt32(data, 4);
            var edgeCount = ReadInt32(data, 8);
            var expectedSize = 12L + stateCount * 12L + edgeCount * 8L;
            if (stateCount <= 0 || edgeCount < 0 || expectedSize != data.Length)
                throw new ArgumentException("Invalid sparse trie data.", nameof(data));

            var firstEdges = new int[stateCount];
            var edgeCounts = new int[stateCount];
            var wordCosts = new int[stateCount];
            var offset = 12;
            for (var state = 0; state < stateCount; state++)
            {
                firstEdges[state] = ReadInt32(data, offset);
                edgeCounts[state] = ReadInt32(data, offset + 4);
                wordCosts[state] = ReadInt32(data, offset + 8);
                offset += 12;
                if (firstEdges[state] < 0 || edgeCounts[state] < 0 ||
                    (long)firstEdges[state] + edgeCounts[state] > edgeCount ||
                    wordCosts[state] < -1)
                    throw new ArgumentException("Invalid sparse trie state.", nameof(data));
            }
            if (wordCosts[0] != -1)
                throw new ArgumentException("Invalid sparse trie root.", nameof(data));

            var parentStates = new int[stateCount];
            var parentCodepoints = new int[stateCount];
            Array.Fill(parentStates, -1);
            parentStates[0] = 0;

            for (var edge = 0; edge < edgeCount; edge++)
            {
                var codepoint = ReadInt32(data, offset);
                var target = ReadInt32(data, offset + 4);
                offset += 8;
                if (!Utf16.IsUnicodeScalar(codepoint) || (uint)target >= (uint)stateCount ||
                    target == 0 || parentStates[target] != -1)
                    throw new ArgumentException("Invalid sparse trie edge.", nameof(data));
                parentStates[target] = -2;
                parentCodepoints[target] = codepoint;
            }

            var expectedFirstEdge = 0;
            for (var state = 0; state < stateCount; state++)
            {
                var first = firstEdges[state];
                var count = edgeCounts[state];
                if (first != expectedFirstEdge)
                    throw new ArgumentException("Invalid sparse trie edge ranges.", nameof(data));
                expectedFirstEdge += count;
                var previousCodepoint = -1;
                for (var edge = first; edge < first + count; edge++)
                {
                    var edgeOffset = 12 + stateCount * 12 + edge * 8;
                    var codepoint = ReadInt32(data, edgeOffset);
                    if (codepoint <= previousCodepoint)
                        throw new ArgumentException("Unsorted sparse trie edges.", nameof(data));
                    previousCodepoint = codepoint;

                    var target = ReadInt32(data, edgeOffset + 4);
                    parentStates[target] = state;
                }
            }

            if (expectedFirstEdge != edgeCount)
                throw new ArgumentException("Unowned sparse trie edges.", nameof(data));

            for (var state = 1; state < stateCount; state++)
                if (parentStates[state] < 0)
                    throw new ArgumentException("Disconnected sparse trie state.", nameof(data));

            var reachable = new bool[stateCount];
            var stack = new int[stateCount];
            var stackCount = 1;
            var reachableCount = 0;
            stack[0] = 0;
            while (stackCount > 0)
            {
                var state = stack[--stackCount];
                if (reachable[state]) continue;
                reachable[state] = true;
                reachableCount++;
                for (var edge = firstEdges[state]; edge < firstEdges[state] + edgeCounts[state]; edge++)
                    stack[stackCount++] = ReadInt32(data,
                        12 + stateCount * 12 + edge * 8 + 4);
            }
            if (reachableCount != stateCount)
                throw new ArgumentException("Disconnected sparse trie state.", nameof(data));

            var result = new List<WordEntry>();
            var reversedCodepoints = new List<int>();
            var builder = new StringBuilder();
            for (var state = 1; state < stateCount; state++)
            {
                if (wordCosts[state] < 0) continue;
                reversedCodepoints.Clear();
                var current = state;
                for (var depth = 0; current != 0; depth++)
                {
                    if (depth >= stateCount || parentStates[current] < 0)
                        throw new ArgumentException("Invalid sparse trie path.", nameof(data));
                    reversedCodepoints.Add(parentCodepoints[current]);
                    current = parentStates[current];
                }

                builder.Clear();
                for (var i = reversedCodepoints.Count - 1; i >= 0; i--)
                    builder.Append(char.ConvertFromUtf32(reversedCodepoints[i]));
                result.Add(new WordEntry(builder.ToString(), wordCosts[state]));
            }

            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Word, right.Word));
            return result;
        }

        private static List<WordEntry> DecompileLegacy(byte[] data)
        {
            var stateCount = ReadInt32(data, 0);
            var codepointBase = ReadInt32(data, 4);
            var codepointRange = ReadInt32(data, 8);
            var expectedSize = 12L + stateCount * 8L;

            if (stateCount <= 0 || codepointRange <= 0 ||
                codepointBase < 0 || (long)codepointBase + codepointRange > 0x110000 ||
                expectedSize != data.Length)
                throw new ArgumentException("Invalid legacy trie data.", nameof(data));

            var baseArray = new int[stateCount];
            var checkArray = new int[stateCount];
            Buffer.BlockCopy(data, 12, baseArray, 0, stateCount * 4);
            Buffer.BlockCopy(data, 12 + stateCount * 4, checkArray, 0, stateCount * 4);

            var words = new List<WordEntry>();
            var reversedCodepoints = new List<int>();
            var builder = new StringBuilder();

            for (var state = 1; state < stateCount; state++)
            {
                if (baseArray[state] >= 0) continue;

                reversedCodepoints.Clear();
                var current = state;
                for (var depth = 0; current != 0; depth++)
                {
                    if (depth >= stateCount)
                        throw new ArgumentException("Invalid legacy trie path.", nameof(data));

                    var parent = checkArray[current];
                    if ((uint)parent >= (uint)stateCount)
                        throw new ArgumentException("Invalid legacy trie state.", nameof(data));

                    var parentBase = baseArray[parent];
                    if (parentBase < 0) parentBase = ~parentBase;
                    var index = current - parentBase;
                    if ((uint)index >= (uint)codepointRange)
                        throw new ArgumentException("Invalid legacy trie transition.", nameof(data));

                    reversedCodepoints.Add(codepointBase + index);
                    current = parent;
                }

                builder.Clear();
                for (var i = reversedCodepoints.Count - 1; i >= 0; i--)
                    builder.Append(char.ConvertFromUtf32(reversedCodepoints[i]));
                words.Add(new WordEntry(builder.ToString(), 1));
            }

            words.Sort((left, right) => StringComparer.Ordinal.Compare(left.Word, right.Word));
            return words;
        }

        private static bool TryParse(string value, out string word, out int cost)
        {
            word = value?.Trim();
            cost = 1;
            if (string.IsNullOrEmpty(word)) return false;

            var tab = word.LastIndexOf('\t');
            if (tab >= 0)
            {
                if (tab == 0 || !int.TryParse(word.AsSpan(tab + 1), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsedCost) || parsedCost < 0)
                    throw new FormatException($"Invalid dictionary cost in '{word}'.");

                word = word.Substring(0, tab).Trim();
                cost = parsedCost;
                if (word.Length == 0) return false;
            }

            ValidateUnicodeScalars(word);
            return true;
        }

        private static void ValidateUnicodeScalars(string word)
        {
            for (var i = 0; i < word.Length; i++)
            {
                var codepoint = Utf16.DecodeAt(word, i, out var size);
                if (!Utf16.IsUnicodeScalar(codepoint))
                    throw new FormatException(
                        $"Dictionary word contains an unpaired UTF-16 surrogate at index {i}.");
                i += size - 1;
            }
        }

        private static IEnumerable<int> EnumerateCodepoints(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                yield return (int)UnicodeData.DecodeAt(text, i, out var size);
                i += size - 1;
            }
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return data[offset]
                 | data[offset + 1] << 8
                 | data[offset + 2] << 16
                 | data[offset + 3] << 24;
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private sealed class Node
        {
            public SortedDictionary<int, int> children;
            public int wordCost = -1;
        }
    }
}
