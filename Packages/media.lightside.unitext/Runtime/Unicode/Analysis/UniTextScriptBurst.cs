using System.Runtime.CompilerServices;
using Unity.Burst;

namespace LightSide
{
    /// <summary>
    /// Burst kernel for UAX #24 script classification and Common/Inherited resolution.
    /// </summary>
    [BurstCompile]
    internal static unsafe class UniTextScriptBurst
    {
        /// <summary>Classifies + resolves <paramref name="length"/> codepoints into the pinned <paramref name="outScripts"/> buffer. Callable from any thread.</summary>
        internal static void Resolve(int* codePoints, int length,
            byte* bmpScript, ScriptRangeEntry* scriptRanges, int scriptRangesLen, byte* outScripts)
            => ResolveEntry(codePoints, length, bmpScript, scriptRanges, scriptRangesLen, outScripts);

        private const int BmpSize = 65536;

        /// <summary>
        /// UAX #24 classification and Common/Inherited forward/backward resolution.
        /// </summary>
        [BurstCompile(CompileSynchronously = true)]
        internal static void ResolveEntry(int* codePoints, int length,
            byte* bmpScript, ScriptRangeEntry* scriptRanges, int scriptRangesLen, byte* outScripts)
        {
            for (var i = 0; i < length; i++)
            {
                var cp = codePoints[i];
                if ((uint)cp < BmpSize)
                {
                    outScripts[i] = bmpScript[cp];
                }
                else
                {
                    var idx = FindRange(scriptRanges, scriptRangesLen, cp);
                    outScripts[i] = idx >= 0 ? (byte)scriptRanges[idx].script : (byte)UnicodeScript.Unknown;
                }
            }

            const byte common = (byte)UnicodeScript.Common;
            const byte inherited = (byte)UnicodeScript.Inherited;
            const byte unknown = (byte)UnicodeScript.Unknown;

            var lastRealScript = unknown;
            for (var i = 0; i < length; i++)
            {
                var script = outScripts[i];
                if (script == common || script == inherited)
                {
                    if (lastRealScript != unknown) outScripts[i] = lastRealScript;
                }
                else
                {
                    lastRealScript = script;
                }
            }

            lastRealScript = unknown;
            for (var i = length - 1; i >= 0; i--)
            {
                var script = outScripts[i];
                if (script == common || script == inherited)
                {
                    if (lastRealScript != unknown) outScripts[i] = lastRealScript;
                }
                else
                {
                    lastRealScript = script;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ScriptRangeEntry* entries, int length, int codePoint)
        {
            var lo = 0;
            var hi = length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var e = entries[mid];
                if (codePoint < e.startCodePoint) hi = mid - 1;
                else if (codePoint > e.endCodePoint) lo = mid + 1;
                else return mid;
            }
            return -1;
        }
    }
}
