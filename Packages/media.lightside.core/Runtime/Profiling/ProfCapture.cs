using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace LightSide
{
    /// <summary>
    /// A finished capture: one aggregated call tree per thread plus named counters. Self time/alloc is the
    /// node's inclusive total minus its children's. Serializes to a self-contained JSON for offline analysis.
    /// </summary>
    public sealed class ProfCapture
    {
        public double ticksPerMs;
        public bool allocAvailable;

        /// <summary>Calibrated cost one instrumented call adds to its parent zone (probe + timestamp + append). Already subtracted from the tree; kept for transparency.</summary>
        public double overheadTicksPerCall;

        public readonly List<Thread> threads = new();

        /// <summary>Per-frame wall time and allocation, sampled via <see cref="Prof.SampleFrame"/>. Empty when nobody drove frame sampling during the capture.</summary>
        public readonly List<Frame> frames = new();

        public struct Frame { public double ms; public long bytes; }

        /// <summary>Per-frame Unity counter series (memory, rendering) from <see cref="ProfCounters"/>, index-aligned with <see cref="frames"/>.</summary>
        public readonly List<CounterTrack> counterTracks = new();

        public sealed class CounterTrack { public string name; public List<long> values = new(); }

        public sealed class Thread
        {
            public int threadId;
            public string threadName;
            public Node root;
            public readonly List<Counter> counters = new();
        }

        public struct Counter { public string name; public long value; }

        public sealed class Node
        {
            public string name;
            public long calls;
            public long totalTicks, selfTicks;
            public long totalBytes, selfBytes;

            /// <summary>Bytes of this zone's first completed call. Steady-state garbage allocates every call — compare <c>(totalBytes-firstBytes)/(calls-1)</c> against <c>firstBytes</c> to split one-time warmup (buffer growth, pool priming) from per-call garbage.</summary>
            public long firstBytes;

            public readonly List<Node> children = new();

            public double TotalMs(double tpm) => totalTicks / tpm;
            public double SelfMs(double tpm) => selfTicks / tpm;
        }

        internal static ProfCapture Build(List<Prof.ThreadState> states, string[] names, bool allocAvailable,
            List<long> frameTicks, List<long> frameBytes, double overheadTicksPerCall)
        {
            var cap = new ProfCapture
            {
                ticksPerMs = Stopwatch.Frequency / 1000.0,
                allocAvailable = allocAvailable,
                overheadTicksPerCall = overheadTicksPerCall
            };

            foreach (var st in states)
            {
                var th = new Thread { threadId = st.threadId, threadName = st.threadName };
                th.root = Reconstruct(st.log, names, overheadTicksPerCall);
                for (int i = 0; i < st.counter.Length; i++)
                    if (st.counter[i] != 0)
                        th.counters.Add(new Counter { name = Name(names, i), value = st.counter[i] });
                cap.threads.Add(th);
            }

            for (int i = 0; i < frameTicks.Count; i++)
                cap.frames.Add(new Frame { ms = frameTicks[i] / cap.ticksPerMs, bytes = frameBytes[i] });
            return cap;
        }

        /// <summary>Replays a thread's event log into an aggregated call tree — the offline half of the event-stream sink. A ZoneEnd with no open zone (a capture that began mid-zone) is dropped; a ZoneBegin still open at capture stop is closed at the thread's last event timestamp so self time stays non-negative.</summary>
        private static Node Reconstruct(ProfThreadLog log, string[] names, double overheadTicksPerCall)
        {
            var node = new Prof.Node[256];
            int nodeCount = 1;
            node[0] = new Prof.Node { id = Prof.RootId, parent = -1, firstChild = -1, nextSibling = -1 };

            int[] stkNode = new int[64];
            long[] stkTicks = new long[64];
            long[] stkAlloc = new long[64];
            int depth = 0, current = 0;
            long lastTicks = 0, lastValue = 0;

            long total = log.Count;
            for (long i = 0; i < total; i++)
            {
                ref var e = ref log.At(i);
                lastTicks = e.ticks;
                lastValue = e.value;
                if (e.type == ProfEvType.ZoneBegin)
                {
                    int child = ChildNode(ref node, ref nodeCount, current, e.id);
                    if (depth == stkNode.Length)
                    {
                        Array.Resize(ref stkNode, depth * 2);
                        Array.Resize(ref stkTicks, depth * 2);
                        Array.Resize(ref stkAlloc, depth * 2);
                    }
                    stkNode[depth] = child;
                    stkTicks[depth] = e.ticks;
                    stkAlloc[depth] = e.value;
                    depth++;
                    current = child;
                }
                else
                {
                    if (depth == 0) continue;
                    depth--;
                    current = Close(node, stkNode[depth], e.ticks - stkTicks[depth], e.value - stkAlloc[depth], true);
                }
            }

            while (depth > 0)
            {
                depth--;
                current = Close(node, stkNode[depth], lastTicks - stkTicks[depth], lastValue - stkAlloc[depth], false);
            }

            return BuildNode(node, 0, names, overheadTicksPerCall, out _);
        }

        private static int Close(Prof.Node[] node, int idx, long dt, long da, bool complete)
        {
            if (da < 0) da = 0;
            if (complete && node[idx].calls == 0) node[idx].firstAlloc = da;
            node[idx].calls++;
            node[idx].totalTicks += dt;
            node[idx].totalAlloc += da;
            int parent = node[idx].parent;
            node[parent].childTicks += dt;
            node[parent].childAlloc += da;
            return parent;
        }

        private static int ChildNode(ref Prof.Node[] node, ref int nodeCount, int parent, int id)
        {
            for (int c = node[parent].firstChild; c >= 0; c = node[c].nextSibling)
                if (node[c].id == id) return c;
            if (nodeCount == node.Length) Array.Resize(ref node, node.Length * 2);
            int idx = nodeCount++;
            node[idx] = new Prof.Node { id = id, parent = parent, firstChild = -1, nextSibling = node[parent].firstChild };
            node[parent].firstChild = idx;
            return idx;
        }

        /// <summary>Builds the public node, subtracting the calibrated per-call overhead: a parent's window absorbs ~one Enter/Exit cost per direct child call, and its total absorbs one per call anywhere below it.</summary>
        private static Node BuildNode(Prof.Node[] arr, int idx, string[] names, double overhead, out long callsBelow)
        {
            ref var n = ref arr[idx];
            var node = new Node
            {
                name = Name(names, n.id),
                calls = n.calls,
                totalBytes = n.totalAlloc,
                selfBytes = n.totalAlloc - n.childAlloc,
                firstBytes = n.firstAlloc
            };
            callsBelow = 0;
            long directCalls = 0;
            for (int c = n.firstChild; c >= 0; c = arr[c].nextSibling)
            {
                node.children.Add(BuildNode(arr, c, names, overhead, out var below));
                callsBelow += arr[c].calls + below;
                directCalls += arr[c].calls;
            }
            node.totalTicks = Math.Max(0, n.totalTicks - (long)(overhead * callsBelow));
            node.selfTicks = Math.Min(Math.Max(0, n.totalTicks - n.childTicks - (long)(overhead * directCalls)), node.totalTicks);
            node.children.Sort((a, b) => b.selfTicks.CompareTo(a.selfTicks));
            return node;
        }

        private static string Name(string[] names, int id) => (uint)id < (uint)names.Length ? names[id] : "?";

        /// <summary>Writes the capture JSON to <paramref name="path"/>.</summary>
        public void Save(string path)
        {
            File.WriteAllText(path, ToJson());
        }

        /// <summary>
        /// Plain-text report: per-thread call trees as aligned columns (total/self ms, calls, self/total/first-call
        /// bytes) plus counters and the frame summary — a human-readable hand-off and offline analysis format.
        /// </summary>
        public string ToText()
        {
            var sb = new StringBuilder(8192);
            double tpm = ticksPerMs;
            sb.Append("capture: ").Append(threads.Count).Append(" thread(s)")
              .Append(" | alloc: ").Append(allocAvailable ? "on" : "off (time only)")
              .Append(" | subtracted overhead/call: ").Append((overheadTicksPerCall / tpm * 1000.0).ToString("F3", CultureInfo.InvariantCulture)).Append(" us")
              .AppendLine();

            foreach (var th in threads)
            {
                sb.AppendLine();
                sb.Append("== ").Append(th.threadName).Append(" (#").Append(th.threadId).Append(") ==").AppendLine();
                sb.AppendLine("  total ms    self ms      calls      self B     total B  1st call B  name");
                foreach (var c in th.root.children)
                    TextNode(sb, c, tpm, 0);
                foreach (var c in th.counters)
                    sb.Append("  counter ").Append(c.name).Append(" = ").Append(c.value).AppendLine();
            }

            if (frames.Count > 0)
            {
                double totalMs = 0, maxMs = 0;
                long totalB = 0;
                foreach (var f in frames)
                {
                    totalMs += f.ms;
                    if (f.ms > maxMs) maxMs = f.ms;
                    if (f.bytes > 0) totalB += f.bytes;
                }
                sb.AppendLine();
                sb.Append("frames: ").Append(frames.Count)
                  .Append(" | total ").Append(totalMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                  .Append(" | peak ").Append(maxMs.ToString("F2", CultureInfo.InvariantCulture)).Append(" ms")
                  .Append(" | alloc ").Append(totalB).Append(" B").AppendLine();
            }

            foreach (var t in counterTracks)
            {
                long last = 0, peak = 0;
                foreach (var v in t.values) { last = v; if (v > peak) peak = v; }
                sb.Append("track ").Append(t.name).Append(": last ").Append(last).Append(", peak ").Append(peak).AppendLine();
            }
            return sb.ToString();
        }

        private void TextNode(StringBuilder sb, Node n, double tpm, int depth)
        {
            sb.Append((n.totalTicks / tpm).ToString("F3", CultureInfo.InvariantCulture).PadLeft(10))
              .Append((n.selfTicks / tpm).ToString("F3", CultureInfo.InvariantCulture).PadLeft(11))
              .Append(n.calls.ToString().PadLeft(11))
              .Append(n.selfBytes.ToString().PadLeft(12))
              .Append(n.totalBytes.ToString().PadLeft(12))
              .Append(n.firstBytes.ToString().PadLeft(12))
              .Append("  ");
            for (int i = 0; i < depth; i++) sb.Append("· ");
            sb.Append(n.name).AppendLine();
            foreach (var c in n.children)
                TextNode(sb, c, tpm, depth + 1);
        }

        /// <summary>Writes a Chrome Trace Event JSON that opens directly in ui.perfetto.dev, speedscope.app and chrome://tracing.</summary>
        public void SaveChromeTrace(string path)
        {
            File.WriteAllText(path, ToChromeTrace());
        }

        /// <summary>
        /// Chrome Trace Event JSON for external viewers. The aggregate tree carries no per-call timestamps, so each
        /// thread's zones are synthesized as strictly-nested duration events whose widths are proportional to total
        /// time — a flame graph, not a wall-clock timeline. Timestamps are microseconds per the format.
        /// </summary>
        public string ToChromeTrace()
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"displayTimeUnit\":\"ms\",\"traceEvents\":[");
            bool first = true;
            foreach (var th in threads)
            {
                ChromeMeta(sb, th, ref first);
                double cursor = 0;
                foreach (var c in th.root.children)
                    EmitChrome(sb, c, th.threadId, ref cursor, ref first);
            }
            EmitChromeCounters(sb, ref first);
            sb.Append("]}");
            return sb.ToString();
        }

        private void EmitChromeCounters(StringBuilder sb, ref bool first)
        {
            if (counterTracks.Count == 0) return;
            int n = 0;
            foreach (var t in counterTracks) if (t.values.Count > n) n = t.values.Count;
            double cursor = 0;
            for (int i = 0; i < n; i++)
            {
                foreach (var t in counterTracks)
                {
                    if (i >= t.values.Count) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"ph\":\"C\",\"pid\":1,\"tid\":0,\"ts\":").Append(cursor.ToString("R", CultureInfo.InvariantCulture))
                      .Append(",\"name\":").Append(Str(t.name))
                      .Append(",\"args\":{\"v\":").Append(t.values[i]).Append("}}");
                }
                cursor += (i < frames.Count ? frames[i].ms : 1.0) * 1000.0;
            }
        }

        private void ChromeMeta(StringBuilder sb, Thread th, ref bool first)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"ph\":\"M\",\"name\":\"thread_name\",\"pid\":1,\"tid\":").Append(th.threadId)
              .Append(",\"args\":{\"name\":").Append(Str(th.threadName)).Append("}}");
        }

        private void EmitChrome(StringBuilder sb, Node n, int tid, ref double cursor, ref bool first)
        {
            double us = n.totalTicks / ticksPerMs * 1000.0;
            double start = cursor;
            ChromeEvent(sb, "B", n, tid, start, ref first);
            double childCursor = start;
            foreach (var c in n.children)
                EmitChrome(sb, c, tid, ref childCursor, ref first);
            ChromeEvent(sb, "E", n, tid, start + us, ref first);
            cursor = start + us;
        }

        private void ChromeEvent(StringBuilder sb, string ph, Node n, int tid, double tsUs, ref bool first)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"ph\":\"").Append(ph).Append("\",\"pid\":1,\"tid\":").Append(tid)
              .Append(",\"ts\":").Append(tsUs.ToString("R", CultureInfo.InvariantCulture))
              .Append(",\"name\":").Append(Str(n.name));
            if (ph == "B")
                sb.Append(",\"args\":{\"calls\":").Append(n.calls)
                  .Append(",\"selfBytes\":").Append(n.selfBytes)
                  .Append(",\"totalBytes\":").Append(n.totalBytes).Append('}');
            sb.Append('}');
        }

        public string ToJson()
        {
            var sb = new StringBuilder(4096);
            sb.Append("{\"ticksPerMs\":").Append(ticksPerMs.ToString("R", CultureInfo.InvariantCulture))
              .Append(",\"allocAvailable\":").Append(allocAvailable ? "true" : "false")
              .Append(",\"overheadTicksPerCall\":").Append(overheadTicksPerCall.ToString("R", CultureInfo.InvariantCulture))
              .Append(",\"threads\":[");
            for (int i = 0; i < threads.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var th = threads[i];
                sb.Append("{\"threadId\":").Append(th.threadId)
                  .Append(",\"threadName\":").Append(Str(th.threadName))
                  .Append(",\"counters\":[");
                for (int c = 0; c < th.counters.Count; c++)
                {
                    if (c > 0) sb.Append(',');
                    sb.Append("{\"name\":").Append(Str(th.counters[c].name)).Append(",\"value\":").Append(th.counters[c].value).Append('}');
                }
                sb.Append("],\"root\":");
                WriteNode(sb, th.root);
                sb.Append('}');
            }
            sb.Append("],\"frames\":[");
            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"ms\":").Append(frames[i].ms.ToString("R", CultureInfo.InvariantCulture))
                  .Append(",\"bytes\":").Append(frames[i].bytes).Append('}');
            }
            sb.Append("],\"counterTracks\":[");
            for (int i = 0; i < counterTracks.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var t = counterTracks[i];
                sb.Append("{\"name\":").Append(Str(t.name)).Append(",\"values\":[");
                for (int v = 0; v < t.values.Count; v++)
                {
                    if (v > 0) sb.Append(',');
                    sb.Append(t.values[v]);
                }
                sb.Append("]}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Parses a capture back from <see cref="ToJson"/> output.</summary>
        public static ProfCapture FromJson(string json)
        {
            int i = 0;
            var root = (Dictionary<string, object>)ParseValue(json, ref i);
            var cap = new ProfCapture
            {
                ticksPerMs = ToDouble(root["ticksPerMs"]),
                allocAvailable = root.TryGetValue("allocAvailable", out var a) && a is bool b && b,
                overheadTicksPerCall = root.TryGetValue("overheadTicksPerCall", out var o) ? ToDouble(o) : 0
            };
            foreach (Dictionary<string, object> t in (List<object>)root["threads"])
            {
                var th = new Thread
                {
                    threadId = (int)ToDouble(t["threadId"]),
                    threadName = t["threadName"] as string,
                    root = ReadNode((Dictionary<string, object>)t["root"])
                };
                if (t.TryGetValue("counters", out var cs))
                    foreach (Dictionary<string, object> c in (List<object>)cs)
                        th.counters.Add(new Counter { name = c["name"] as string, value = (long)ToDouble(c["value"]) });
                cap.threads.Add(th);
            }
            if (root.TryGetValue("frames", out var fs))
                foreach (Dictionary<string, object> f in (List<object>)fs)
                    cap.frames.Add(new Frame { ms = ToDouble(f["ms"]), bytes = (long)ToDouble(f["bytes"]) });
            if (root.TryGetValue("counterTracks", out var cts))
                foreach (Dictionary<string, object> t in (List<object>)cts)
                {
                    var track = new CounterTrack { name = t["name"] as string };
                    foreach (var v in (List<object>)t["values"]) track.values.Add((long)ToDouble(v));
                    cap.counterTracks.Add(track);
                }
            return cap;
        }

        private static Node ReadNode(Dictionary<string, object> o)
        {
            var n = new Node
            {
                name = o["name"] as string,
                calls = (long)ToDouble(o["calls"]),
                totalTicks = (long)ToDouble(o["totalTicks"]),
                selfTicks = (long)ToDouble(o["selfTicks"]),
                totalBytes = (long)ToDouble(o["totalBytes"]),
                selfBytes = (long)ToDouble(o["selfBytes"]),
                firstBytes = o.TryGetValue("firstBytes", out var fb) ? (long)ToDouble(fb) : 0
            };
            foreach (Dictionary<string, object> c in (List<object>)o["children"])
                n.children.Add(ReadNode(c));
            return n;
        }

        private static double ToDouble(object o) => System.Convert.ToDouble(o, CultureInfo.InvariantCulture);

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': i += 4; return true;
                case 'f': i += 5; return false;
                case 'n': i += 4; return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var o = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                i++;
                o[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++;
                return o;
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var a = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return a; }
            while (true)
            {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                i++;
                return a;
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    sb.Append(e switch
                    {
                        'n' => '\n', 'r' => '\r', 't' => '\t', '"' => '"', '\\' => '\\', '/' => '/',
                        'u' => (char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        _ => e
                    });
                    if (e == 'u') i += 4;
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        private static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && Ascii.IsWhitespace(s[i])) i++;
        }

        private void WriteNode(StringBuilder sb, Node n)
        {
            sb.Append("{\"name\":").Append(Str(n.name))
              .Append(",\"calls\":").Append(n.calls)
              .Append(",\"totalTicks\":").Append(n.totalTicks)
              .Append(",\"selfTicks\":").Append(n.selfTicks)
              .Append(",\"totalBytes\":").Append(n.totalBytes)
              .Append(",\"selfBytes\":").Append(n.selfBytes)
              .Append(",\"firstBytes\":").Append(n.firstBytes)
              .Append(",\"children\":[");
            for (int i = 0; i < n.children.Count; i++)
            {
                if (i > 0) sb.Append(',');
                WriteNode(sb, n.children[i]);
            }
            sb.Append("]}");
        }

        private static string Str(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
