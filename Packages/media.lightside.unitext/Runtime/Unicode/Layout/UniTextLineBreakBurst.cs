using System.Runtime.CompilerServices;
using Unity.Burst;

namespace LightSide
{
    /// <summary>
    /// Burst kernel for the ordered UAX #14 LB4–LB30b rule cascade.
    /// </summary>
    [BurstCompile]
    internal static unsafe class UniTextLineBreakBurst
    {
        /// <summary>Computes line-break opportunities for <paramref name="length"/> codepoints into the pinned <paramref name="outBreaks"/> buffer (one <see cref="LineBreakType"/> byte per position, length+1 slots); <paramref name="length"/> must be ≥ 1. Callable from any thread.</summary>
        internal static void Resolve(int* codePoints, int length,
            byte* bmpLineBreak, LineBreakRangeEntry* lineBreakRanges, int lineBreakRangesLen,
            byte* bmpGeneralCategory, GeneralCategoryRangeEntry* generalCategoryRanges, int generalCategoryRangesLen,
            byte* bmpEastAsianWidth, EastAsianWidthRangeEntry* eastAsianWidthRanges, int eastAsianWidthRangesLen,
            byte* bmpExtendedPictographic, ExtendedPictographicRangeEntry* extendedPictographicRanges, int extendedPictographicRangesLen,
            byte* bmpScript, ScriptRangeEntry* scriptRanges, int scriptRangesLen,
            byte* outBreaks)
            => ResolveEntry(codePoints, length,
                bmpLineBreak, lineBreakRanges, lineBreakRangesLen,
                bmpGeneralCategory, generalCategoryRanges, generalCategoryRangesLen,
                bmpEastAsianWidth, eastAsianWidthRanges, eastAsianWidthRangesLen,
                bmpExtendedPictographic, extendedPictographicRanges, extendedPictographicRangesLen,
                bmpScript, scriptRanges, scriptRangesLen,
                outBreaks);

        private const int BmpSize = 65536;

        /// <summary>The read-only Unicode tables consumed by the shared rule core. Bulk analysis reaches it through the Burst kernel; random-access queries call the same rules for one boundary.</summary>
        internal struct Tables
        {
            public byte* bmpLb;
            public LineBreakRangeEntry* lbRanges;
            public int lbLen;
            public byte* bmpGc;
            public GeneralCategoryRangeEntry* gcRanges;
            public int gcLen;
            public byte* bmpEaw;
            public EastAsianWidthRangeEntry* eawRanges;
            public int eawLen;
            public byte* bmpExtPict;
            public ExtendedPictographicRangeEntry* extPictRanges;
            public int extPictLen;
            public byte* bmpScript;
            public ScriptRangeEntry* scriptRanges;
            public int scriptLen;
        }

        [BurstCompile(CompileSynchronously = true)]
        private static void ResolveEntry(int* codePoints, int length,
            byte* bmpLineBreak, LineBreakRangeEntry* lineBreakRanges, int lineBreakRangesLen,
            byte* bmpGeneralCategory, GeneralCategoryRangeEntry* generalCategoryRanges, int generalCategoryRangesLen,
            byte* bmpEastAsianWidth, EastAsianWidthRangeEntry* eastAsianWidthRanges, int eastAsianWidthRangesLen,
            byte* bmpExtendedPictographic, ExtendedPictographicRangeEntry* extendedPictographicRanges, int extendedPictographicRangesLen,
            byte* bmpScript, ScriptRangeEntry* scriptRanges, int scriptRangesLen,
            byte* outBreaks)
        {
            var t = new Tables
            {
                bmpLb = bmpLineBreak, lbRanges = lineBreakRanges, lbLen = lineBreakRangesLen,
                bmpGc = bmpGeneralCategory, gcRanges = generalCategoryRanges, gcLen = generalCategoryRangesLen,
                bmpEaw = bmpEastAsianWidth, eawRanges = eastAsianWidthRanges, eawLen = eastAsianWidthRangesLen,
                bmpExtPict = bmpExtendedPictographic, extPictRanges = extendedPictographicRanges, extPictLen = extendedPictographicRangesLen,
                bmpScript = bmpScript, scriptRanges = scriptRanges, scriptLen = scriptRangesLen
            };

            outBreaks[0] = (byte)LineBreakType.None;
            outBreaks[length] = (byte)LineBreakType.Mandatory;

            var beforeRaw = GetLineBreakClass(in t, codePoints[0]);

            for (var i = 0; i < length - 1; i++)
            {
                var afterRaw = GetLineBreakClass(in t, codePoints[i + 1]);
                outBreaks[i + 1] = (byte)GetBreakTypeCore(in t, codePoints, length, i, beforeRaw, afterRaw);
                beforeRaw = afterRaw;
            }
        }

        #region Inline Helpers (pure — ports of LineBreakAlgorithm's inline helpers)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCM(LineBreakClass cls)
        {
            return cls == LineBreakClass.CM || cls == LineBreakClass.ZWJ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsLB9Exception(LineBreakClass cls)
        {
            return cls == LineBreakClass.SP || cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                   cls == LineBreakClass.LF || cls == LineBreakClass.NL || cls == LineBreakClass.ZW ||
                   cls == LineBreakClass.ZWJ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAlphabetic(LineBreakClass cls)
        {
            return cls == LineBreakClass.AL || cls == LineBreakClass.HL;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsAksara(LineBreakClass cls)
        {
            return cls == LineBreakClass.AK || cls == LineBreakClass.AS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsVirama(LineBreakClass cls)
        {
            return cls == LineBreakClass.VF || cls == LineBreakClass.VI;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNumericAffix(LineBreakClass cls)
        {
            return cls == LineBreakClass.PO || cls == LineBreakClass.PR;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsKorean(LineBreakClass cls)
        {
            return cls == LineBreakClass.JL || cls == LineBreakClass.JV || cls == LineBreakClass.JT ||
                   cls == LineBreakClass.H2 || cls == LineBreakClass.H3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEastAsianWide(EastAsianWidth eaw)
        {
            return eaw == EastAsianWidth.W || eaw == EastAsianWidth.F;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEastAsianForLB19a(EastAsianWidth eaw)
        {
            return eaw == EastAsianWidth.F || eaw == EastAsianWidth.W || eaw == EastAsianWidth.H;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static LineBreakClass ResolveClass(LineBreakClass cls)
        {
            switch (cls)
            {
                case LineBreakClass.CM:
                case LineBreakClass.ZWJ:
                case LineBreakClass.AI:
                case LineBreakClass.SG:
                case LineBreakClass.XX:
                case LineBreakClass.SA:
                    return LineBreakClass.AL;
                case LineBreakClass.CJ:
                    return LineBreakClass.NS;
                default:
                    return cls;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsEffectivelyCombining(in Tables t, LineBreakClass cls, int cp)
        {
            if (IsCM(cls)) return true;
            if (cls == LineBreakClass.SA)
            {
                var gc = GetGeneralCategory(in t, cp);
                return gc == GeneralCategory.Mn || gc == GeneralCategory.Mc;
            }

            return false;
        }

        #endregion

        #region Table lookups (bmp fast-path else binary search — mirrors UnicodeDataProvider)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static LineBreakClass GetLineBreakClass(in Tables t, int cp)
        {
            if ((uint)cp < BmpSize)
                return (LineBreakClass)t.bmpLb[cp];

            var idx = FindRange(t.lbRanges, t.lbLen, cp);
            return idx >= 0 ? t.lbRanges[idx].lineBreakClass : LineBreakClass.XX;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static GeneralCategory GetGeneralCategory(in Tables t, int cp)
        {
            if ((uint)cp < BmpSize)
                return (GeneralCategory)t.bmpGc[cp];

            var idx = FindRange(t.gcRanges, t.gcLen, cp);
            return idx >= 0 ? t.gcRanges[idx].generalCategory : GeneralCategory.Cn;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static EastAsianWidth GetEastAsianWidth(in Tables t, int cp)
        {
            if ((uint)cp < BmpSize)
                return (EastAsianWidth)t.bmpEaw[cp];

            var idx = FindRange(t.eawRanges, t.eawLen, cp);
            return idx >= 0 ? t.eawRanges[idx].eastAsianWidth : EastAsianWidth.N;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsExtendedPictographic(in Tables t, int cp)
        {
            if ((uint)cp < BmpSize)
                return t.bmpExtPict[cp] != 0;

            return FindRange(t.extPictRanges, t.extPictLen, cp) >= 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static UnicodeScript GetScript(in Tables t, int cp)
        {
            if ((uint)cp < BmpSize)
                return (UnicodeScript)t.bmpScript[cp];

            var idx = FindRange(t.scriptRanges, t.scriptLen, cp);
            return idx >= 0 ? t.scriptRanges[idx].script : UnicodeScript.Unknown;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDottedCircle(int cp)
        {
            return cp == UnicodeData.DottedCircle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsBrahmicForLB28a(in Tables t, int cp)
        {
            var script = GetScript(in t, cp);
            return script == UnicodeScript.Balinese ||
                   script == UnicodeScript.Batak ||
                   script == UnicodeScript.Buginese ||
                   script == UnicodeScript.Javanese ||
                   script == UnicodeScript.KayahLi ||
                   script == UnicodeScript.Makasar ||
                   script == UnicodeScript.Mandaic ||
                   script == UnicodeScript.Modi ||
                   script == UnicodeScript.Nandinagari ||
                   script == UnicodeScript.Sundanese ||
                   script == UnicodeScript.TaiLe ||
                   script == UnicodeScript.NewTaiLue ||
                   script == UnicodeScript.Takri ||
                   script == UnicodeScript.Tibetan;
        }

        #endregion

        /// <summary>
        /// Verbatim port of <see cref="LineBreakAlgorithm"/>'s <c>GetBreakTypeCore</c> — the full LB rule
        /// cascade for one adjacent codepoint pair at <paramref name="index"/>. The order of the rules is a
        /// contract (later rules assume earlier ones already returned), so it is reproduced statement for
        /// statement; every <c>dataProvider.Get*</c>/<c>Is*</c> call maps to the raw-pointer lookups above.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static LineBreakType GetBreakTypeCore(in Tables t, int* codePoints, int length, int index,
            LineBreakClass beforeRaw, LineBreakClass afterRaw)
        {
            var beforeCp = codePoints[index];
            var afterCp = codePoints[index + 1];

            if ((afterRaw == LineBreakClass.CM || afterRaw == LineBreakClass.ZWJ) && !IsLB9Exception(beforeRaw))
                return LineBreakType.None;

            if (beforeRaw == LineBreakClass.AL)
            {
                if (afterRaw == LineBreakClass.AL || afterRaw == LineBreakClass.NU)
                    return LineBreakType.None;
            }
            else if (beforeRaw == LineBreakClass.NU)
            {
                if (afterRaw == LineBreakClass.AL || afterRaw == LineBreakClass.NU)
                    return LineBreakType.None;
            }

            var afterIsCombining = afterRaw == LineBreakClass.SA &&
                                   IsEffectivelyCombining(in t, afterRaw, afterCp);

            if (afterIsCombining && !IsLB9Exception(beforeRaw))
                return LineBreakType.None;

            var effectiveBeforeRaw = beforeRaw;
            var effectiveIndex = index;
            var effectiveCp = beforeCp;

            while (effectiveIndex > 0 && IsEffectivelyCombining(in t, effectiveBeforeRaw, effectiveCp))
            {
                effectiveIndex--;
                effectiveCp = codePoints[effectiveIndex];
                effectiveBeforeRaw = GetLineBreakClass(in t, effectiveCp);
            }

            if (IsEffectivelyCombining(in t, effectiveBeforeRaw, effectiveCp))
                effectiveBeforeRaw = LineBreakClass.AL;

            if (IsLB9Exception(effectiveBeforeRaw) && IsEffectivelyCombining(in t, beforeRaw, beforeCp))
                effectiveBeforeRaw = LineBreakClass.AL;

            if (beforeRaw == LineBreakClass.ZWJ)
                return LineBreakType.None;

            var before = ResolveClass(effectiveBeforeRaw);
            var after = ResolveClass(afterRaw);

            if (before == LineBreakClass.BK) return LineBreakType.Mandatory;
            if (before == LineBreakClass.CR && after == LineBreakClass.LF) return LineBreakType.None;
            if (before == LineBreakClass.CR || before == LineBreakClass.LF || before == LineBreakClass.NL) return LineBreakType.Mandatory;
            if (after == LineBreakClass.BK || after == LineBreakClass.CR ||
                after == LineBreakClass.LF || after == LineBreakClass.NL) return LineBreakType.None;

            if (after == LineBreakClass.SP || after == LineBreakClass.ZW) return LineBreakType.None;

            if (before == LineBreakClass.ZW) return LineBreakType.Optional;
            if (before == LineBreakClass.SP)
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (cls == LineBreakClass.SP) continue;
                    if (cls == LineBreakClass.ZW) return LineBreakType.Optional;
                    break;
                }

            if (before == LineBreakClass.WJ || after == LineBreakClass.WJ) return LineBreakType.None;

            if (before == LineBreakClass.GL) return LineBreakType.None;

            if (after == LineBreakClass.GL && before != LineBreakClass.SP && before != LineBreakClass.BA &&
                before != LineBreakClass.HH && before != LineBreakClass.HY)
                return LineBreakType.None;

            if (after == LineBreakClass.CL || after == LineBreakClass.CP ||
                after == LineBreakClass.EX || after == LineBreakClass.SY)
                return LineBreakType.None;

            if (before == LineBreakClass.OP) return LineBreakType.None;
            if (before == LineBreakClass.SP)
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (IsCM(cls)) continue;
                    var resolved = ResolveClass(cls);
                    if (resolved == LineBreakClass.OP) return LineBreakType.None;
                    if (resolved != LineBreakClass.SP) break;
                }

            if (after == LineBreakClass.OP && CheckLB15(in t, codePoints, effectiveIndex, before))
                return LineBreakType.None;

            if (before == LineBreakClass.SP && after == LineBreakClass.IS)
                if (LookAheadGetClass(in t, codePoints, length, index + 2) == LineBreakClass.NU)
                    return LineBreakType.Optional;

            if (after == LineBreakClass.IS) return LineBreakType.None;

            if (after == LineBreakClass.NS && CheckClosingBeforeNS(in t, codePoints, effectiveIndex, before))
                return LineBreakType.None;

            if (after == LineBreakClass.B2 && CheckB2Pattern(in t, codePoints, effectiveIndex, before))
                return LineBreakType.None;

            if (before == LineBreakClass.SP)
                return HandleLB18(in t, codePoints, length, index, after, afterCp) ? LineBreakType.Optional : LineBreakType.None;

            if (after == LineBreakClass.QU && !CanBreakBeforeQU(in t, codePoints, length, index, afterCp, effectiveCp))
                return LineBreakType.None;
            if (before == LineBreakClass.QU && !CanBreakAfterQU(in t, codePoints, effectiveIndex, effectiveCp, afterCp))
                return LineBreakType.None;

            if (before == LineBreakClass.CB || after == LineBreakClass.CB) return LineBreakType.Optional;

            if ((before == LineBreakClass.HY || before == LineBreakClass.HH) &&
                IsWordInitialHyphen(in t, codePoints, effectiveIndex) && IsAlphabetic(after))
                return LineBreakType.None;

            if (after == LineBreakClass.BA || after == LineBreakClass.HY ||
                after == LineBreakClass.HH || after == LineBreakClass.NS)
                return LineBreakType.None;
            if (before == LineBreakClass.BB) return LineBreakType.None;

            if ((before == LineBreakClass.HY || before == LineBreakClass.HH) &&
                after != LineBreakClass.HL && IsHLBeforeHyphen(in t, codePoints, effectiveIndex))
                return LineBreakType.None;

            if (before == LineBreakClass.SY && after == LineBreakClass.HL) return LineBreakType.None;

            if (after == LineBreakClass.IN) return LineBreakType.None;

            if (IsAlphabetic(before) && after == LineBreakClass.NU) return LineBreakType.None;
            if (before == LineBreakClass.NU && IsAlphabetic(after)) return LineBreakType.None;

            if (before == LineBreakClass.PR &&
                (after == LineBreakClass.ID || after == LineBreakClass.EB || after == LineBreakClass.EM))
                return LineBreakType.None;
            if ((before == LineBreakClass.ID || before == LineBreakClass.EB || before == LineBreakClass.EM) &&
                after == LineBreakClass.PO)
                return LineBreakType.None;

            if (IsNumericAffix(before) && IsAlphabetic(after)) return LineBreakType.None;
            if (IsAlphabetic(before) && IsNumericAffix(after)) return LineBreakType.None;

            if (!CheckLB25(in t, codePoints, length, index, effectiveIndex, before, after))
                return LineBreakType.None;

            if (before == LineBreakClass.JL &&
                (after == LineBreakClass.JL || after == LineBreakClass.JV ||
                 after == LineBreakClass.H2 || after == LineBreakClass.H3))
                return LineBreakType.None;
            if ((before == LineBreakClass.JV || before == LineBreakClass.H2) &&
                (after == LineBreakClass.JV || after == LineBreakClass.JT))
                return LineBreakType.None;
            if ((before == LineBreakClass.JT || before == LineBreakClass.H3) && after == LineBreakClass.JT)
                return LineBreakType.None;

            if (IsKorean(before) && after == LineBreakClass.PO) return LineBreakType.None;
            if (before == LineBreakClass.PR && IsKorean(after)) return LineBreakType.None;

            if (IsAlphabetic(before) && IsAlphabetic(after)) return LineBreakType.None;

            if (!CheckLB28a(in t, codePoints, length, index, effectiveIndex, before, after,
                    beforeRaw, effectiveBeforeRaw, beforeCp, afterCp, effectiveCp))
                return LineBreakType.None;

            if (before == LineBreakClass.IS && IsAlphabetic(after)) return LineBreakType.None;

            if ((IsAlphabetic(before) || before == LineBreakClass.NU) && after == LineBreakClass.OP &&
                !IsEastAsianWide(GetEastAsianWidth(in t, afterCp)))
                return LineBreakType.None;
            if (before == LineBreakClass.CP && (IsAlphabetic(after) || after == LineBreakClass.NU))
                return LineBreakType.None;

            if ((effectiveBeforeRaw == LineBreakClass.RI || before == LineBreakClass.RI) &&
                after == LineBreakClass.RI)
            {
                var riCount = 1;
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (cls == LineBreakClass.RI) riCount++;
                    else break;
                }

                if ((riCount & 1) == 1) return LineBreakType.None;
            }

            if (before == LineBreakClass.EB && after == LineBreakClass.EM) return LineBreakType.None;
            if (after == LineBreakClass.EM && IsExtendedPictographic(in t, effectiveCp) &&
                GetGeneralCategory(in t, effectiveCp) == GeneralCategory.Cn)
                return LineBreakType.None;

            return LineBreakType.Optional;
        }

        #region Rule Handlers (ports of LineBreakAlgorithm's context helpers)

        private static LineBreakClass LookAheadGetClass(in Tables t, int* codePoints, int length, int start)
        {
            for (var i = start; i < length; i++)
            {
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (!IsCM(cls)) return ResolveClass(cls);
            }

            return LineBreakClass.XX;
        }

        private static bool CheckLB15(in Tables t, int* codePoints, int effectiveIndex, LineBreakClass before)
        {
            var prev = before;
            int i = effectiveIndex, quPos = -1;

            while (prev == LineBreakClass.SP && i > 0)
            {
                i--;
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (IsCM(cls)) continue;
                prev = ResolveClass(cls);
                if (prev == LineBreakClass.QU) quPos = i;
            }

            if (prev != LineBreakClass.QU || quPos < 0 ||
                GetGeneralCategory(in t, codePoints[quPos]) != GeneralCategory.Pi)
                return false;

            if (quPos == 0) return true;

            for (var j = quPos - 1; j >= 0; j--)
            {
                var cls = GetLineBreakClass(in t, codePoints[j]);
                if (IsCM(cls)) continue;
                return cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                       cls == LineBreakClass.LF || cls == LineBreakClass.NL ||
                       cls == LineBreakClass.SP || cls == LineBreakClass.ZW ||
                       cls == LineBreakClass.CB || cls == LineBreakClass.GL;
            }

            return false;
        }

        private static bool CheckClosingBeforeNS(in Tables t, int* codePoints, int effectiveIndex, LineBreakClass before)
        {
            var prev = before;
            for (var i = effectiveIndex; (prev == LineBreakClass.SP || prev == LineBreakClass.AL) && i > 0;)
            {
                i--;
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (IsCM(cls)) continue;
                prev = ResolveClass(cls);
                if (prev != LineBreakClass.SP) break;
            }

            return prev == LineBreakClass.CL || prev == LineBreakClass.CP;
        }

        private static bool CheckB2Pattern(in Tables t, int* codePoints, int effectiveIndex, LineBreakClass before)
        {
            var prev = before;
            for (var i = effectiveIndex; (prev == LineBreakClass.SP || prev == LineBreakClass.AL) && i > 0;)
            {
                i--;
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (IsCM(cls)) continue;
                prev = ResolveClass(cls);
                if (prev != LineBreakClass.SP) break;
            }

            return prev == LineBreakClass.B2;
        }

        private static bool HandleLB18(in Tables t, int* codePoints, int length, int index, LineBreakClass after, int afterCp)
        {
            if (after == LineBreakClass.QU && GetGeneralCategory(in t, afterCp) == GeneralCategory.Pf)
            {
                var nextCls = LookAheadGetClass(in t, codePoints, length, index + 2);
                var eot = true;
                for (var i = index + 2; i < length; i++)
                {
                    var c = GetLineBreakClass(in t, codePoints[i]);
                    if (!IsCM(c))
                    {
                        eot = false;
                        break;
                    }
                }

                if (eot || nextCls == LineBreakClass.SP || nextCls == LineBreakClass.GL ||
                    nextCls == LineBreakClass.WJ || nextCls == LineBreakClass.CL ||
                    nextCls == LineBreakClass.QU || nextCls == LineBreakClass.CP ||
                    nextCls == LineBreakClass.EX || nextCls == LineBreakClass.IS ||
                    nextCls == LineBreakClass.SY || nextCls == LineBreakClass.BK ||
                    nextCls == LineBreakClass.CR || nextCls == LineBreakClass.LF ||
                    nextCls == LineBreakClass.NL || nextCls == LineBreakClass.ZW)
                    return false;
            }

            for (var i = index - 1; i >= 0; i--)
            {
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (cls == LineBreakClass.SP || IsCM(cls)) continue;

                var isOpOrQuPi = cls == LineBreakClass.OP ||
                                 (cls == LineBreakClass.QU &&
                                  GetGeneralCategory(in t, codePoints[i]) == GeneralCategory.Pi);

                if (isOpOrQuPi)
                {
                    if (i == 0) return false;
                    for (var j = i - 1; j >= 0; j--)
                    {
                        var prevCls = GetLineBreakClass(in t, codePoints[j]);
                        if (IsCM(prevCls)) continue;
                        return !(prevCls == LineBreakClass.BK || prevCls == LineBreakClass.CR ||
                                 prevCls == LineBreakClass.LF || prevCls == LineBreakClass.NL ||
                                 prevCls == LineBreakClass.OP || prevCls == LineBreakClass.QU ||
                                 prevCls == LineBreakClass.GL || prevCls == LineBreakClass.SP ||
                                 prevCls == LineBreakClass.ZW || prevCls == LineBreakClass.CB);
                    }
                }

                break;
            }

            return true;
        }

        private static bool CanBreakBeforeQU(in Tables t, int* codePoints, int length, int index, int afterCp, int effectiveCp)
        {
            if (GetGeneralCategory(in t, afterCp) != GeneralCategory.Pi) return false;
            if (!IsEastAsianForLB19a(GetEastAsianWidth(in t, effectiveCp))) return false;

            for (var i = index + 2; i < length; i++)
            {
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (IsCM(cls)) continue;
                return IsEastAsianForLB19a(GetEastAsianWidth(in t, codePoints[i]));
            }

            return false;
        }

        private static bool CanBreakAfterQU(in Tables t, int* codePoints, int effectiveIndex, int effectiveCp, int afterCp)
        {
            if (GetGeneralCategory(in t, effectiveCp) != GeneralCategory.Pf) return false;
            if (!IsEastAsianForLB19a(GetEastAsianWidth(in t, afterCp))) return false;

            for (var i = effectiveIndex - 1; i >= 0; i--)
            {
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (IsCM(cls)) continue;
                return IsEastAsianForLB19a(GetEastAsianWidth(in t, codePoints[i]));
            }

            return false;
        }

        private static bool IsWordInitialHyphen(in Tables t, int* codePoints, int effectiveIndex)
        {
            if (effectiveIndex == 0) return true;
            var prev = ResolveClass(GetLineBreakClass(in t, codePoints[effectiveIndex - 1]));
            return prev == LineBreakClass.BK || prev == LineBreakClass.CR ||
                   prev == LineBreakClass.LF || prev == LineBreakClass.NL ||
                   prev == LineBreakClass.SP || prev == LineBreakClass.ZW ||
                   prev == LineBreakClass.CB || prev == LineBreakClass.GL;
        }

        private static bool IsHLBeforeHyphen(in Tables t, int* codePoints, int effectiveIndex)
        {
            for (var i = effectiveIndex - 1; i >= 0; i--)
            {
                var cls = GetLineBreakClass(in t, codePoints[i]);
                if (IsCM(cls)) continue;
                return ResolveClass(cls) == LineBreakClass.HL;
            }

            return false;
        }

        private static bool CheckLB25(in Tables t, int* codePoints, int length, int index, int effectiveIndex,
            LineBreakClass before, LineBreakClass after)
        {
            if (before == LineBreakClass.NU && IsNumericAffix(after)) return false;
            if (IsNumericAffix(before) && after == LineBreakClass.NU) return false;
            if ((before == LineBreakClass.HY || before == LineBreakClass.IS) && after == LineBreakClass.NU) return false;
            if (before == LineBreakClass.NU &&
                (after == LineBreakClass.NU || after == LineBreakClass.SY || after == LineBreakClass.IS))
                return false;

            if (before == LineBreakClass.SY && after == LineBreakClass.NU)
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.SY || cls == LineBreakClass.IS) continue;
                    break;
                }

            if (IsNumericAffix(after))
                for (var i = effectiveIndex; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.SY || cls == LineBreakClass.IS ||
                        cls == LineBreakClass.CL || cls == LineBreakClass.CP) continue;
                    break;
                }

            if ((before == LineBreakClass.CL || before == LineBreakClass.CP) && IsNumericAffix(after))
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.OP || cls == LineBreakClass.BK ||
                        cls == LineBreakClass.CR || cls == LineBreakClass.LF ||
                        cls == LineBreakClass.NL || cls == LineBreakClass.SP) break;
                }

            if (IsNumericAffix(before) && after == LineBreakClass.OP)
                for (var i = index + 2; i < length; i++)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (cls == LineBreakClass.NU) return false;
                    if (cls == LineBreakClass.CL || cls == LineBreakClass.CP ||
                        cls == LineBreakClass.BK || cls == LineBreakClass.CR ||
                        cls == LineBreakClass.LF || cls == LineBreakClass.NL) break;
                }

            return true;
        }

        private static bool CheckLB28a(in Tables t, int* codePoints, int length, int index, int effectiveIndex,
            LineBreakClass before, LineBreakClass after,
            LineBreakClass beforeRaw, LineBreakClass effectiveBeforeRaw,
            int beforeCp, int afterCp, int effectiveCp)
        {
            if (before == LineBreakClass.AP && IsAksara(after)) return false;
            if (IsAksara(before) && IsVirama(after)) return false;

            if (before == LineBreakClass.VI && IsAksara(after))
                for (var i = effectiveIndex - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (IsAksara(cls) || cls == LineBreakClass.VI) return false;
                    break;
                }

            if (beforeRaw == LineBreakClass.CM && IsAksara(after) &&
                IsAksara(effectiveBeforeRaw) && IsBrahmicForLB28a(in t, beforeCp))
            {
                var nextCls = LookAheadGetClass(in t, codePoints, length, index + 2);
                if (IsVirama(nextCls))
                {
                    var foundCM = false;
                    for (var i = effectiveIndex - 1; i >= 0; i--)
                    {
                        var cls = GetLineBreakClass(in t, codePoints[i]);
                        if (IsCM(cls))
                        {
                            foundCM = true;
                            continue;
                        }

                        if (IsVirama(cls) && foundCM) return true;
                        break;
                    }

                    return false;
                }
            }

            if (before == LineBreakClass.AP && IsDottedCircle(afterCp)) return false;
            if (IsDottedCircle(effectiveCp) && IsVirama(after)) return false;

            if (beforeRaw == LineBreakClass.VI)
                for (var i = index - 1; i >= 0; i--)
                {
                    var cls = GetLineBreakClass(in t, codePoints[i]);
                    if (IsCM(cls)) continue;
                    if (IsAksara(cls) || IsDottedCircle(codePoints[i])) return false;
                    break;
                }

            return true;
        }

        #endregion

        #region Concrete binary searches (one per range table)

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(LineBreakRangeEntry* entries, int length, int codePoint)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(GeneralCategoryRangeEntry* entries, int length, int codePoint)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(EastAsianWidthRangeEntry* entries, int length, int codePoint)
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindRange(ExtendedPictographicRangeEntry* entries, int length, int codePoint)
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

        #endregion
    }
}
