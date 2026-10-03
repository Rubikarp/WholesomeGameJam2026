using System;
using System.Runtime.CompilerServices;

namespace LightSide
{
    public sealed partial class TextProcessor
    {
        private void DoFirstPass(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            if (!BeginFirstPass(text, settings)) return;
            for (var i = 0; i < pendingShapes.count; i++)
                RunShapeJob(i);
            FinishFirstPass();
        }

        /// <summary>
        /// Whole-text prefix of the first pass (parse → attributes → Unicode analysis →
        /// dictionary tailoring → font faces) plus paragraph fingerprints and the shape-miss job queue.
        /// False = nothing to shape (empty text). Between this and <see cref="FinishFirstPass"/>
        /// the queued jobs may run on any threads; everything else stays untouched.
        /// </summary>
        internal bool BeginFirstPass(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            if (!BeginFirstPassA(text, settings)) return false;
            UniTextDebug.BeginSample("TextProcessor.AnalyzeJobs");
            for (var i = 0; i < pendingAnalyses.count; i++)
                RunAnalysisJob(i);
            UniTextDebug.EndSample();
            BeginFirstPassB();
            return true;
        }

        /// <summary>Runs the ConfigureSettings hooks and publishes <c>buf.baseDirection</c> for text with nothing to analyze, so consumers positioning an empty line still get the configured direction. <see cref="TextDirection.Auto"/> resolves to LTR — UAX #9 P3 with no strong character.</summary>
        private void ResolveEmptyBaseDirection(TextProcessSettings settings)
        {
            configureSettings?.Invoke(ref settings);
            buf.baseDirection = settings.baseDirection == TextDirection.RightToLeft
                ? TextDirection.RightToLeft
                : TextDirection.LeftToRight;
        }

        /// <summary>First half of the prefix: parse, Parsed/ConfigureSettings hooks, and the analysis PREPARE (split + fingerprint + queue misses). Between this and <see cref="BeginFirstPassB"/> the queued analysis jobs may run on any threads (<see cref="RunAnalysisJob"/>); false = empty text, no B/jobs apply.</summary>
        internal bool BeginFirstPassA(ReadOnlySpan<char> text, TextProcessSettings settings)
        {
            UniTextDebug.Increment(ref UniTextDebug.TextProcessor_DoFullShapingCount);

            hasFontIdOverrides = false;
            fontIdOverrides.FakeClear();
            effectiveWidths.FakeClear();
            suppressedLayoutBreaks.FakeClear();
            pendingHiddenMask = 0;
            layoutHiddenMask = 0;
            collectingHiddenLayout = false;
            applyingHiddenLayout = false;
            buf.fontStyleRealizations.FakeClear();
            buf.fontStyleWeights.FakeClear();
            variationMap?.Clear();
            buf.variationMap = null;
            buf.shapingFontSize = settings.fontSize;

            RefreshSettingsLanguage();

            UniTextDebug.BeginSample("TextProcessor.Parse");
            Parse(text);
            buf.PrepareAttributes();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.Parsed.Invoke()");
            parsed?.Invoke();
            UniTextDebug.EndSample();

            configureSettings?.Invoke(ref settings);

            if (buf.codepoints.count == 0)
            {
                buf.baseDirection = settings.baseDirection == TextDirection.RightToLeft
                    ? TextDirection.RightToLeft
                    : TextDirection.LeftToRight;
                hasValidFirstPassData = false;
                linesCache.valid = false;
                positionsCache.valid = false;
                return false;
            }

            UniTextDebug.BeginSample("TextProcessor.AnalyzePrepare");
            AnalyzeParagraphsPrepare(settings.baseDirection);
            UniTextDebug.EndSample();
            return true;
        }

        /// <summary>Second half of the prefix (after analysis jobs ran): splice + populate the analysis cache, then word segmentation, the <c>Analyzed</c> hook, font-face resolution, and the shape PREPARE (which queues shape jobs). Must run single-threaded per component after all its analysis jobs completed.</summary>
        internal void BeginFirstPassB()
        {
            UniTextDebug.BeginSample("TextProcessor.AnalyzeFinish");
            AnalyzeParagraphsFinish();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.WordSegmentation");
            ApplyWordSegmentation();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.Analyzed.Invoke()");
            analyzed?.Invoke();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.ResolveFontFaces");
            ResolveFontFaces();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.PrepareShapePass");
            PrepareShapePass();
            UniTextDebug.EndSample();
        }

        /// <summary>Closes the first pass after all shape jobs ran: splices paragraphs, fires <c>Shaped</c> whole-text, computes cp widths.</summary>
        internal void FinishFirstPass()
        {
            UniTextDebug.BeginSample("TextProcessor.SpliceShapedParagraphs");
            SpliceShapedParagraphs();
            UniTextDebug.EndSample();

            lineWidthReserve = 0f;

            UniTextDebug.BeginSample("TextProcessor.Shaped.Invoke()");
            shaped?.Invoke();
            UniTextDebug.EndSample();

            UniTextDebug.BeginSample("TextProcessor.ComputeCpWidths");
            ComputeCpWidths();
            UniTextDebug.EndSample();

            hasValidFirstPassData = true;

            CatZones.layout.MeowFormat("[TextProcessor] FirstPass: {0} codepoints, {1} runs, {2} glyphs",
                buf.codepoints.count, buf.shapedRuns.count, buf.shapedGlyphs.count);
        }

        private void RefreshSettingsLanguage()
        {
            var tag = UniTextSettings.Language ?? string.Empty;
            if (cachedSettingsLanguageTag == tag) return;
            cachedSettingsLanguageTag = tag;
            cachedSettingsLanguageIndex = LanguageRegistry.Register(tag);
        }

        private void Parse(ReadOnlySpan<char> text)
        {
            buf.codepoints.count = 0;
            buf.EnsureCodepointCapacity(text.Length);

            var i = 0;
            while (i < text.Length) AddCharacter(text, ref i);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCharacter(ReadOnlySpan<char> text, ref int i)
        {
            var cp = UnicodeData.DecodeAt(text, i, out var size);
            AddCodepoint((int)cp);
            i += size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AddCodepoint(int cp)
        {
            var count = buf.codepoints.count;
            if (count >= buf.codepoints.Capacity)
                buf.EnsureCodepointCapacity(count + 1);
            buf.codepoints[count] = cp;
            buf.codepoints.count = count + 1;
        }

        private ParagraphAnalysisCache analysisCache;
        private BidiParagraphDirection analysisDirection;

        /// <summary>Per-thread grapheme kernel scratch (UAX #29 GCB state), reused across paragraphs to keep the hot analysis path pool-free — pinned per call since Burst cannot read managed thread-static state.</summary>
        [ThreadStatic] private static byte[] graphemeScratch;
        [ThreadStatic] private static byte[] wordScratch;

        private struct PendingAnalysis
        {
            public int paraIdx;
            public byte baseLevel;
            public byte[] levels;
            public UnicodeScript[] scripts;
            public LineBreakType[] breaks;
            public bool[] graphemes;
            public bool[] words;
        }

        private PooledBuffer<PendingAnalysis> pendingAnalyses;

        /// <summary>Number of queued analysis jobs for the current pass (0 outside a pass).</summary>
        internal int PendingAnalysisJobCount => pendingAnalyses.count;

        /// <summary>
        /// Per-paragraph analysis prefix (bidi levels, resolved scripts, and line, grapheme, and word boundaries),
        /// split into the same three-phase pattern as shaping so it can run inline (serial) or dispatched
        /// per-paragraph across workers: <see cref="AnalyzeParagraphsPrepare"/> fingerprints and queues misses,
        /// <see cref="RunAnalysisJob"/> computes one paragraph into per-slot arrays (no shared writes, no cache
        /// touch), <see cref="AnalyzeParagraphsFinish"/> splices + populates <see cref="analysisCache"/> single-
        /// threaded. Each stage is paragraph-scoped by spec, so slice results are byte-identical to a whole-text
        /// run — EXCEPT script resolution of leading Common/Inherited codepoints, which now resolves within the
        /// paragraph instead of inheriting across the separator (the correct, self-consistent behaviour).
        /// </summary>
        private void AnalyzeParagraphs(TextDirection requestedDirection)
        {
            AnalyzeParagraphsPrepare(requestedDirection);
            for (var i = 0; i < pendingAnalyses.count; i++)
                RunAnalysisJob(i);
            AnalyzeParagraphsFinish();
        }

        /// <summary>Phase 1 (per component): split paragraphs, size whole-text buffers, fingerprint each paragraph into <c>para.analysisHash</c>, and queue cache misses as jobs. Touches only this processor's state.</summary>
        internal void AnalyzeParagraphsPrepare(TextDirection requestedDirection)
        {
            var cpCount = buf.codepoints.count;
            buf.bidiLevels.EnsureCapacity(cpCount);
            buf.scripts.EnsureCapacity(cpCount);
            buf.breakOpportunities.EnsureCount(cpCount + 1);
            buf.graphemeBreaks.EnsureCount(cpCount + 1);
            buf.wordBoundaries.EnsureCount(cpCount + 1);
            buf.bidiLevels.count = cpCount;
            buf.scripts.count = cpCount;

            SplitParagraphs(cpCount);

            analysisDirection = requestedDirection switch
            {
                TextDirection.RightToLeft => BidiParagraphDirection.RightToLeft,
                TextDirection.LeftToRight => BidiParagraphDirection.LeftToRight,
                _ => BidiParagraphDirection.Auto
            };
            var seed = XxHash64.Combine(AnalysisSeedSalt, (ulong)(uint)analysisDirection);

            buf.breakOpportunities.data[0] = LineBreakType.None;
            buf.graphemeBreaks.data[0] = true;
            buf.wordBoundaries.data[0] = true;

            analysisCache ??= new ParagraphAnalysisCache();
            analysisCache.BeginPass(buf.paragraphs.count);
            ReturnPendingAnalysisArrays();

            var cps = buf.codepoints.data;
            var table = buf.paragraphs.data;
            for (var p = 0; p < buf.paragraphs.count; p++)
            {
                ref var para = ref table[p];
                para.analysisHash = XxHash64.Hash<int>(cps.AsSpan(para.cpStart, para.cpCount), seed);
                if (!analysisCache.Peek(para.analysisHash, para.cpCount))
                    pendingAnalyses.Add(new PendingAnalysis { paraIdx = p });
            }
        }

        /// <summary>Job: computes one missed paragraph's bidi, scripts, line, grapheme, and word boundaries into per-slot rented arrays. Reads only shared read-only buffers; writes only its own slot; never touches the cache.</summary>
        internal void RunAnalysisJob(int missIdx)
        {
            ref var job = ref pendingAnalyses[missIdx];
            ref readonly var para = ref buf.paragraphs[job.paraIdx];
            var len = para.cpCount;
            var slice = buf.codepoints.data.AsSpan(para.cpStart, len);
            byte[] levels = null;
            UnicodeScript[] scripts = null;
            LineBreakType[] breaks = null;
            bool[] graphemes = null;
            bool[] words = null;
            try
            {
                UniTextDebug.BeginSample("Analyze.Bidi");
                levels = ArrayPool<byte>.Rent(Math.Max(len, 1));
                var baseLevel = AnalyzeBidi(slice, levels);
                UniTextDebug.EndSample();

                UniTextDebug.BeginSample("Analyze.Scripts");
                scripts = ArrayPool<UnicodeScript>.Rent(Math.Max(len, 1));
                AnalyzeScripts(slice, scripts);
                UniTextDebug.EndSample();

                UniTextDebug.BeginSample("Analyze.Breaks");
                breaks = ArrayPool<LineBreakType>.Rent(len + 1);
                AnalyzeBreaks(slice, breaks.AsSpan(0, len + 1));
                UniTextDebug.EndSample();

                UniTextDebug.BeginSample("Analyze.Graphemes");
                graphemes = ArrayPool<bool>.Rent(len + 1);
                AnalyzeGraphemes(slice, graphemes.AsSpan(0, len + 1));
                UniTextDebug.EndSample();

                UniTextDebug.BeginSample("Analyze.Words");
                words = ArrayPool<bool>.Rent(len + 1);
                AnalyzeWords(slice, words.AsSpan(0, len + 1));
                UniTextDebug.EndSample();

                job.baseLevel = baseLevel;
                job.levels = levels;
                job.scripts = scripts;
                job.breaks = breaks;
                job.graphemes = graphemes;
                job.words = words;
                levels = null;
                scripts = null;
                breaks = null;
                graphemes = null;
                words = null;
            }
            finally
            {
                if (levels != null) ArrayPool<byte>.Return(levels);
                if (scripts != null) ArrayPool<UnicodeScript>.Return(scripts);
                if (breaks != null) ArrayPool<LineBreakType>.Return(breaks);
                if (graphemes != null) ArrayPool<bool>.Return(graphemes);
                if (words != null) ArrayPool<bool>.Return(words);
            }
        }

        /// <summary>Runs UAX #29 default word boundaries before dictionary tailoring.</summary>
        private unsafe void AnalyzeWords(ReadOnlySpan<int> codepoints, Span<bool> breaks)
        {
            var len = codepoints.Length;
            if (len == 0)
            {
                if (breaks.Length > 0) breaks[0] = true;
                return;
            }

            var provider = UnicodeData.Provider;
            if (wordScratch == null || wordScratch.Length < len)
                wordScratch = new byte[Math.Max(len, 64)];
            fixed (int* cp = codepoints)
            fixed (bool* bp = breaks)
            fixed (byte* ws = wordScratch)
                UniTextWordBurst.Resolve(cp, len,
                    provider.BmpWordBreakPtr, provider.WordBreakRangesPtr, provider.WordBreakRangesLength,
                    provider.BmpExtendedPictographicPtr, provider.ExtendedPictographicRangesPtr,
                    provider.ExtendedPictographicRangesLength,
                    ws, (byte*)bp);
        }

        /// <summary>Runs UAX #24 script resolution through its Burst kernel.</summary>
        private unsafe void AnalyzeScripts(ReadOnlySpan<int> codepoints, UnicodeScript[] scripts)
        {
            var len = codepoints.Length;
            if (len == 0) return;

            var provider = UnicodeData.Provider;
            fixed (int* cp = codepoints)
            fixed (UnicodeScript* sp = scripts)
                UniTextScriptBurst.Resolve(cp, len, provider.BmpScriptPtr,
                    provider.ScriptRangesPtr, provider.ScriptRangesLength, (byte*)sp);
        }

        /// <summary>Runs UAX #29 grapheme boundaries through its Burst kernel.</summary>
        private unsafe void AnalyzeGraphemes(ReadOnlySpan<int> codepoints, Span<bool> breaks)
        {
            var len = codepoints.Length;
            if (len == 0)
            {
                if (breaks.Length > 0) breaks[0] = true;
                return;
            }

            var provider = UnicodeData.Provider;
            if (graphemeScratch == null || graphemeScratch.Length < len)
                graphemeScratch = new byte[Math.Max(len, 64)];
            fixed (int* cp = codepoints)
            fixed (bool* bp = breaks)
            fixed (byte* gs = graphemeScratch)
                UniTextGraphemeBurst.Resolve(cp, len,
                    provider.BmpGraphemeBreakPtr, provider.GraphemeBreakRangesPtr, provider.GraphemeBreakRangesLength,
                    provider.BmpIndicConjunctBreakPtr, provider.IndicConjunctBreakRangesPtr, provider.IndicConjunctBreakRangesLength,
                    provider.BmpExtendedPictographicPtr, provider.ExtendedPictographicRangesPtr, provider.ExtendedPictographicRangesLength,
                    gs, (byte*)bp);
        }

        /// <summary>Runs UAX #14 line boundaries through its Burst kernel.</summary>
        private unsafe void AnalyzeBreaks(ReadOnlySpan<int> codepoints, Span<LineBreakType> breaks)
        {
            var len = codepoints.Length;
            if (len == 0)
            {
                if (breaks.Length > 0) breaks[0] = LineBreakType.Mandatory;
                return;
            }

            var provider = UnicodeData.Provider;
            fixed (int* cp = codepoints)
            fixed (LineBreakType* bp = breaks)
                UniTextLineBreakBurst.Resolve(cp, len,
                    provider.BmpLineBreakPtr, provider.LineBreakRangesPtr, provider.LineBreakRangesLength,
                    provider.BmpGeneralCategoryPtr, provider.GeneralCategoryRangesPtr, provider.GeneralCategoryRangesLength,
                    provider.BmpEastAsianWidthPtr, provider.EastAsianWidthRangesPtr, provider.EastAsianWidthRangesLength,
                    provider.BmpExtendedPictographicPtr, provider.ExtendedPictographicRangesPtr, provider.ExtendedPictographicRangesLength,
                    provider.BmpScriptPtr, provider.ScriptRangesPtr, provider.ScriptRangesLength,
                    (byte*)bp);
        }

        /// <summary>Runs UAX #9 bidi resolution through its Burst kernel.</summary>
        private unsafe byte AnalyzeBidi(ReadOnlySpan<int> codepoints, byte[] levels)
        {
            var len = codepoints.Length;
            if (len == 0) return 0;

            var provider = UnicodeData.Provider;
            var dir = analysisDirection switch
            {
                BidiParagraphDirection.LeftToRight => 0,
                BidiParagraphDirection.RightToLeft => 1,
                _ => 2
            };

            var s = BidiScratch.Get(len);
            byte baseLevel = 0;
            var paragraphCount = 0;

            fixed (int* cp = codepoints)
            fixed (byte* lv = levels)
            fixed (byte* bc = s.bidiClasses)
            fixed (byte* oc = s.originalClasses)
            fixed (int* i2p = s.isolateToPdi)
            fixed (int* p2i = s.pdiToIsolate)
            fixed (int* ist = s.isolateStack)
            fixed (BidiLevelRun* lr = s.levelRuns)
            fixed (int* rip = s.runIndexByPosition)
            fixed (int* sb = s.seqBuffer)
            fixed (int* si = s.sequenceIndices)
            fixed (BidiIsoSeq* sq = s.sequences)
            fixed (BidiBracketPair* bpr = s.bracketPairs)
            fixed (int* os = s.openStack)
            fixed (BidiParagraph* po = s.paragraphsOut)
            {
                UniTextBidiBurst.Resolve(cp, len, dir,
                    provider.BmpBidiClassPtr, provider.BidiClassRangesPtr, provider.BidiClassRangesLength,
                    provider.BracketsPtr, provider.BracketsLength,
                    bc, oc, i2p, p2i, ist, lr, rip, sb, si, sq, bpr, os,
                    lv, po, &paragraphCount);
                baseLevel = paragraphCount > 0 ? po[0].baseLevel : (byte)0;
            }

            return baseLevel;
        }

        /// <summary>Phase 3 (per component, single-threaded): walks paragraphs in order, splicing each job's output or replaying a cache hit into the whole-text buffers, and populates the cache (move-in for jobs, copy for the consumed-duplicate inline path). Sole writer of <see cref="analysisCache"/> and the whole-text analysis buffers.</summary>
        internal void AnalyzeParagraphsFinish()
        {
            var cps = buf.codepoints.data;
            var table = buf.paragraphs.data;
            var pend = 0;

            for (var p = 0; p < buf.paragraphs.count; p++)
            {
                ref var para = ref table[p];
                var start = para.cpStart;
                var len = para.cpCount;

                if (pend < pendingAnalyses.count && pendingAnalyses[pend].paraIdx == p)
                {
                    ref var job = ref pendingAnalyses[pend];
                    pend++;
                    para.baseLevel = job.baseLevel;
                    SpliceParagraph(start, len, job.levels, job.scripts, job.breaks, job.graphemes, job.words);
                    analysisCache.StoreMoved(para.analysisHash, len, job.baseLevel,
                        job.levels, job.scripts, job.breaks, job.graphemes, job.words);
                    job.levels = null;
                    job.scripts = null;
                    job.breaks = null;
                    job.graphemes = null;
                    job.words = null;
                }
                else if (analysisCache.TryConsume(para.analysisHash, len, out var e))
                {
                    para.baseLevel = e.baseLevel;
                    SpliceParagraph(start, len, e.levels, e.scripts, e.breaks, e.graphemes, e.words);
                }
                else
                {
                    var slice = cps.AsSpan(start, len);
                    var levels = ArrayPool<byte>.Rent(Math.Max(len, 1));
                    var baseLevel = AnalyzeBidi(slice, levels);
                    var scripts = ArrayPool<UnicodeScript>.Rent(Math.Max(len, 1));
                    var breaks = ArrayPool<LineBreakType>.Rent(len + 1);
                    var graphemes = ArrayPool<bool>.Rent(len + 1);
                    var words = ArrayPool<bool>.Rent(len + 1);
                    AnalyzeScripts(slice, scripts);
                    AnalyzeBreaks(slice, breaks.AsSpan(0, len + 1));
                    AnalyzeGraphemes(slice, graphemes.AsSpan(0, len + 1));
                    AnalyzeWords(slice, words.AsSpan(0, len + 1));

                    para.baseLevel = baseLevel;
                    SpliceParagraph(start, len, levels, scripts, breaks, graphemes, words);
                    analysisCache.Store(para.analysisHash, len, baseLevel,
                        levels.AsSpan(0, len), scripts.AsSpan(0, len),
                        breaks.AsSpan(0, len + 1), graphemes.AsSpan(0, len + 1),
                        words.AsSpan(0, len + 1));

                    ArrayPool<byte>.Return(levels);
                    ArrayPool<UnicodeScript>.Return(scripts);
                    ArrayPool<LineBreakType>.Return(breaks);
                    ArrayPool<bool>.Return(graphemes);
                    ArrayPool<bool>.Return(words);
                }
            }

            pendingAnalyses.FakeClear();
            analysisCache.EndPass();

            buf.baseDirection = buf.paragraphs.count > 0 && (table[0].baseLevel & 1) != 0
                ? TextDirection.RightToLeft
                : TextDirection.LeftToRight;
        }

        private void ReturnPendingAnalysisArrays()
        {
            for (var i = 0; i < pendingAnalyses.count; i++)
            {
                ref var job = ref pendingAnalyses[i];
                if (job.levels != null) ArrayPool<byte>.Return(job.levels);
                if (job.scripts != null) ArrayPool<UnicodeScript>.Return(job.scripts);
                if (job.breaks != null) ArrayPool<LineBreakType>.Return(job.breaks);
                if (job.graphemes != null) ArrayPool<bool>.Return(job.graphemes);
                if (job.words != null) ArrayPool<bool>.Return(job.words);
                job.levels = null;
                job.scripts = null;
                job.breaks = null;
                job.graphemes = null;
                job.words = null;
            }
            pendingAnalyses.FakeClear();
        }

        /// <summary>A fixed salt so analysis fingerprints never alias shape fingerprints (separate caches, but cheap insurance).</summary>
        private const ulong AnalysisSeedSalt = 0x9E3779B97F4A7C15UL;

        /// <summary>Splits codepoints into paragraphs at bidi class B separators; a CRLF pair is one separator, and the separator is the last codepoint of its paragraph. Runs before bidi so paragraphs can be content-addressed. The pair must stay in one paragraph — the per-paragraph UAX #14 and UAX #29 passes can only apply LB5 and GB3 (CR × LF) within it.</summary>
        private void SplitParagraphs(int cpCount)
        {
            var provider = UnicodeData.Provider;
            var cps = buf.codepoints.data.AsSpan(0, cpCount);
            buf.paragraphs.EnsureCapacity(cpCount);
            var table = buf.paragraphs.data;

            var n = 0;
            var paraStart = 0;
            for (var i = 0; i < cpCount; i++)
                if (provider.GetBidiClass(cps[i]) == BidiClass.ParagraphSeparator)
                {
                    if (UnicodeData.IsCrlfAt(cps, i)) i++;
                    table[n++] = new Paragraph { cpStart = paraStart, cpCount = i - paraStart + 1 };
                    paraStart = i + 1;
                }

            if (paraStart < cpCount)
                table[n++] = new Paragraph { cpStart = paraStart, cpCount = cpCount - paraStart };

            buf.paragraphs.count = n;
        }

        /// <summary>
        /// Writes one paragraph's LOCAL analysis slices into the whole-document buffers at <paramref name="start"/>.
        /// levels/scripts are per-codepoint (direct copy). Boundary arrays are length len+1 with local slot 0 =
        /// BOT (None / boundary): slot 0 is dropped — the whole-text boundary slot at <paramref name="start"/> is
        /// owned by the PREVIOUS paragraph's mandatory end slot (or the global BOT set once by the caller), so only
        /// local [1..len] map to global [start+1..start+len].
        /// </summary>
        private void SpliceParagraph(int start, int len,
            byte[] levels, UnicodeScript[] scripts, LineBreakType[] breaks, bool[] graphemes, bool[] words)
        {
            levels.AsSpan(0, len).CopyTo(buf.bidiLevels.data.AsSpan(start, len));
            scripts.AsSpan(0, len).CopyTo(buf.scripts.data.AsSpan(start, len));

            var gb = buf.breakOpportunities.data;
            var gg = buf.graphemeBreaks.data;
            var gw = buf.wordBoundaries.data;
            for (var i = 1; i <= len; i++)
            {
                gb[start + i] = breaks[i];
                gg[start + i] = graphemes[i];
                gw[start + i] = words[i];
            }
        }

        private void ApplyWordSegmentation()
        {
            var ws = SharedPipelineComponents.WordSegmentation;
            if (!ws.HasSegmenters) return;

            var cpCount = buf.codepoints.count;
            ws.Process(
                buf.codepoints.Span,
                buf.scripts.data.AsSpan(0, cpCount),
                buf.breakOpportunities.data.AsSpan(0, cpCount + 1),
                buf.wordBoundaries.data.AsSpan(0, cpCount + 1),
                buf.graphemeBreaks.data.AsSpan(0, cpCount + 1));
        }

        private void ComputeCpWidths()
        {
            var cpCount = buf.codepoints.count;
            buf.cpWidths.EnsureCount(cpCount);

            var widths = buf.cpWidths.data;
            Array.Clear(widths, 0, cpCount);

            var runs = buf.shapedRuns.data;
            var runCount = buf.shapedRuns.count;
            var glyphs = buf.shapedGlyphs.data;

            for (var r = 0; r < runCount; r++)
            {
                ref readonly var run = ref runs[r];
                var end = run.glyphStart + run.glyphCount;
                for (var g = run.glyphStart; g < end; g++)
                {
                    var cpIdx = glyphs[g].cluster;
                    if ((uint)cpIdx < (uint)cpCount)
                        widths[cpIdx] += glyphs[g].advanceX;
                }
            }
        }

        /// <summary>
        /// Resolves the effective language for a codepoint: the per-codepoint override from the
        /// attribute span if present, otherwise the project-wide language from <see cref="UniTextSettings"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private byte ResolveLanguage(byte[] langData, int index)
        {
            if (langData != null && (uint)index < (uint)langData.Length)
            {
                var v = langData[index];
                if (v != 0) return v;
            }
            return cachedSettingsLanguageIndex;
        }

        private byte[] GetLanguageData()
        {
            var attr = buf.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.Language);
            return attr?.buffer.data;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int[] GetFontOverrideData()
        {
            var attr = buf.GetAttributeData<PooledArrayAttribute<int>>(AttributeKeys.Font);
            return attr?.buffer.data;
        }
    }
}
