using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace LightSide
{
    public sealed partial class TextProcessor
    {
        /// <summary>
        /// Resolves every grapheme cluster once into an exact font instance shared by itemization,
        /// HarfBuzz and FreeType. Formatting attributes are immutable inputs; residual synthesis is
        /// written to a separate realization channel.
        /// </summary>
        private void ResolveFontFaces()
        {
            const byte resolvedState = 1;
            const byte skippedState = 2;
            const byte stateMask = 3;
            const byte coherenceChecked = 4;
            using var variationPass = fontProvider.BeginVariationPass();
            var cpCount = buf.codepoints.count;
            variationMap?.Clear();
            if (cpCount == 0)
            {
                fontIdOverrides.FakeClear();
                hasFontIdOverrides = false;
                buf.fontStyleRealizations.FakeClear();
                buf.fontStyleWeights.FakeClear();
                buf.variationMap = variationMap;
                return;
            }

            var variationAttribute = buf.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.Variation);
            var boldAttribute = buf.GetAttributeData<PooledArrayAttribute<ushort>>(AttributeKeys.Bold);
            var italicAttribute = buf.GetAttributeData<PooledArrayAttribute<byte>>(AttributeKeys.Italic);

            var variationData = variationAttribute?.buffer.data;
            var boldData = boldAttribute?.buffer.data;
            var italicData = italicAttribute?.buffer.data;
            var languageData = GetLanguageData();
            var fontData = GetFontOverrideData();
            var codepoints = buf.codepoints.data;
            var graphemeBreaks = buf.graphemeBreaks.Span;
            var wordBoundaries = buf.wordBoundaries.Span;
            var scripts = buf.scripts.data;
            var hidden = buf.hiddenClusters.Span;
            var resolvesCoherentSequences = fontProvider.SupportsCoherentSequenceResolution;
            var wordSegmentation = resolvesCoherentSequences
                ? SharedPipelineComponents.WordSegmentation
                : null;

            fontIdOverrides.EnsureCount(cpCount);
            hasFontIdOverrides = true;

            var tracksStyleRealization = boldData != null || italicData != null;
            if (tracksStyleRealization)
            {
                buf.fontStyleRealizations.EnsureCount(cpCount);
            }
            else
            {
                buf.fontStyleRealizations.FakeClear();
            }

            if (boldData != null)
            {
                buf.fontStyleWeights.EnsureCount(cpCount);
            }
            else
            {
                buf.fontStyleWeights.FakeClear();
            }

            Array.Clear(fontIdOverrides.data, 0, cpCount);
            if (tracksStyleRealization)
                Array.Clear(buf.fontStyleRealizations.data, 0, cpCount);
            if (boldData != null)
                Array.Clear(buf.fontStyleWeights.data, 0, cpCount);

            var resolutionStates = ArrayPool<byte>.Rent(cpCount);
            Array.Clear(resolutionStates, 0, cpCount);
            using var resolutionBatch = SystemFont.BeginResolutionBatch();
            try
            {
                void ApplyResolvedInstance(int rangeStart, int rangeEnd,
                    in ResolvedFontInstance resolvedInstance)
                {
                    for (var i = rangeStart; i < rangeEnd; i++)
                    {
                        resolutionStates[i] = resolvedState;
                        fontIdOverrides[i] = resolvedInstance.fontId;
                        if (tracksStyleRealization)
                            buf.fontStyleRealizations[i] = (byte)resolvedInstance.realization;
                        if (boldData != null)
                            buf.fontStyleWeights[i] = resolvedInstance.effectiveWeight;
                    }

                    if (!resolvedInstance.IsVariableInstance) return;
                    variationMap ??= new Dictionary<int, VariationRunInfo>();
                    variationMap[resolvedInstance.fontId] = new VariationRunInfo
                    {
                        baseFontHash = resolvedInstance.font.FontDataHash,
                        varHash48 = resolvedInstance.varHash48,
                        hbVariations = resolvedInstance.hbVariations,
                        ftCoords = resolvedInstance.ftCoords,
                    };
                }

                while (true)
                {
                    var deferredAny = false;
                    var previousFontId = 0;
                    var previousDeferred = false;
                    byte previousLanguage = 0;
                    byte previousLevel = 0;
                    for (var start = 0; start < cpCount;)
                    {
                        var end = FindNextClusterStart(graphemeBreaks, start + 1, cpCount);
                        var state = resolutionStates[start];
                        switch (state & stateMask)
                        {
                            case resolvedState:
                                previousFontId = fontIdOverrides[start];
                                previousDeferred = false;
                                previousLanguage = ResolveLanguage(languageData, start);
                                previousLevel = buf.bidiLevels[start];
                                start = end;
                                continue;
                            case skippedState:
                                previousFontId = 0;
                                previousDeferred = false;
                                start = end;
                                continue;
                        }

                        var cluster = codepoints.AsSpan(start, end - start);
                        if (state == 0 && (UnicodeData.IsMandatoryBreakChar(cluster[0])
                                         || IsShapingExcluded(hidden, start)
                                         || IsEmojiClusterForResolution(cluster)))
                        {
                            previousFontId = 0;
                            previousDeferred = false;
                            resolutionStates[start] = skippedState;
                            start = end;
                            continue;
                        }

                        var request = CreateFontFaceRequestAt(start, variationData, boldData, italicData);
                        var language = ResolveLanguage(languageData, start);
                        var level = buf.bidiLevels[start];

                        var explicitFontId = fontData != null && (uint)start < (uint)fontData.Length
                            ? fontData[start]
                            : 0;
                        if (resolvesCoherentSequences && (state & coherenceChecked) == 0
                            && WordSegmentationProcessor.IsWordCharacter(cluster[0]))
                        {
                            var sequenceScript = WordSegmentationProcessor.CanonicalizeDictionaryScript(
                                scripts[start]);
                            var sequenceEnd = end;
                            var sequenceTailStart = start;
                            while (sequenceEnd < cpCount)
                            {
                                var candidateEnd = FindNextClusterStart(graphemeBreaks,
                                    sequenceEnd + 1, cpCount);
                                var candidate = codepoints.AsSpan(sequenceEnd,
                                    candidateEnd - sequenceEnd);
                                var candidateFontId = fontData != null
                                                      && (uint)sequenceEnd < (uint)fontData.Length
                                    ? fontData[sequenceEnd]
                                    : 0;
                                if (UnicodeData.IsMandatoryBreakChar(candidate[0])
                                    || IsShapingExcluded(hidden, sequenceEnd)
                                    || IsEmojiClusterForResolution(candidate)
                                    || IsCoherentSequenceSeparator(candidate[0])
                                    || WordSegmentationProcessor.CanonicalizeDictionaryScript(
                                        scripts[sequenceEnd]) != sequenceScript
                                    || ResolveLanguage(languageData, sequenceEnd) != language
                                    || candidateFontId != explicitFontId
                                    || !CreateFontFaceRequestAt(sequenceEnd, variationData,
                                        boldData, italicData).Equals(request))
                                    break;
                                if (wordBoundaries[sequenceEnd])
                                {
                                    var beforeScript = scripts[sequenceEnd - 1];
                                    var afterScript = scripts[sequenceEnd];
                                    var bridgesDefaultBoundary =
                                        WordSegmentationProcessor.IsWordCharacter(
                                            codepoints[sequenceEnd - 1])
                                        && WordSegmentationProcessor.IsWordCharacter(candidate[0])
                                        && WordSegmentationProcessor.IsContextualDictionaryScript(beforeScript)
                                        && !wordSegmentation.HasSegmenter(beforeScript)
                                        && !wordSegmentation.HasSegmenter(afterScript);
                                    if (!bridgesDefaultBoundary) break;
                                }
                                sequenceTailStart = sequenceEnd;
                                sequenceEnd = candidateEnd;
                            }

                            if (sequenceEnd > end)
                            {
                                var sequence = codepoints.AsSpan(start, sequenceEnd - start);
                                if (fontProvider.TryResolveCoveringSequence(sequence, language, explicitFontId,
                                        in request, start, out var sequenceInstance,
                                        out var sequenceDeferred))
                                {
                                    ApplyResolvedInstance(start, sequenceEnd, in sequenceInstance);
                                    previousFontId = sequenceInstance.fontId;
                                    previousDeferred = false;
                                    previousLanguage = language;
                                    previousLevel = buf.bidiLevels[sequenceTailStart];
                                    start = sequenceEnd;
                                    continue;
                                }
                                if (sequenceDeferred)
                                {
                                    deferredAny = true;
                                    previousFontId = 0;
                                    previousDeferred = true;
                                    previousLanguage = language;
                                    previousLevel = buf.bidiLevels[sequenceTailStart];
                                    start = sequenceEnd;
                                    continue;
                                }
                                for (var i = start; i < sequenceEnd; i++)
                                    resolutionStates[i] |= coherenceChecked;
                            }
                        }

                        if (explicitFontId == 0 && previousDeferred && IsSpaceSeparator(cluster[0])
                            && language == previousLanguage && level == previousLevel
                            && !UnicodeData.IsMandatoryBreakChar(codepoints[start - 1]))
                        {
                            deferredAny = true;
                            previousLanguage = language;
                            previousLevel = level;
                            start = end;
                            continue;
                        }
                        if (explicitFontId == 0 && previousFontId != 0 && IsSpaceSeparator(cluster[0])
                            && language == previousLanguage && level == previousLevel
                            && !UnicodeData.IsMandatoryBreakChar(codepoints[start - 1]))
                            explicitFontId = previousFontId;

                        if (fontProvider.ResolveSequence(cluster, language, explicitFontId, in request,
                                start, resolvesCoherentSequences,
                                out var instance, out var deferred))
                        {
                            ApplyResolvedInstance(start, end, in instance);

                            previousFontId = instance.fontId;
                            previousDeferred = false;
                            previousLanguage = language;
                            previousLevel = level;
                        }
                        else
                        {
                            previousFontId = 0;
                            previousDeferred = deferred;
                            deferredAny |= deferred;
                            if (deferred)
                            {
                                previousLanguage = language;
                                previousLevel = level;
                            }
                            if (!deferred) resolutionStates[start] = skippedState;
                        }

                        start = end;
                    }

                    if (!deferredAny || !resolutionBatch.ResolvePending()) break;
                }
            }
            finally
            {
                ArrayPool<byte>.Return(resolutionStates);
            }

            buf.variationMap = variationMap;
        }

        private static FontFaceRequest CreateFontFaceRequest(ushort bold, byte italic,
            in VariationConfig variation, bool hasVariation)
        {
            var requestsWeight = bold != 0;
            var boldMode = (ushort)(bold & FontStyleEncoding.BoldModeMask);
            var allowsRealWeight = requestsWeight && boldMode != FontStyleEncoding.BoldModeFake;
            var allowsSyntheticWeight = requestsWeight && boldMode != FontStyleEncoding.BoldModeRealOnly;
            var weight = requestsWeight ? FontStyleEncoding.DecodeCssWeight(bold) : 400;

            var requestsSlant = italic != 0;
            var allowsRealSlant = italic == FontStyleEncoding.ItalicAuto
                                  || italic == FontStyleEncoding.ItalicRealOnly;
            var allowsSyntheticSlant = requestsSlant && italic != FontStyleEncoding.ItalicRealOnly;

            return new FontFaceRequest(weight, requestsWeight, requestsSlant,
                allowsRealWeight, allowsRealSlant,
                allowsSyntheticWeight, allowsSyntheticSlant,
                in variation, hasVariation);
        }

        private FontFaceRequest CreateFontFaceRequestAt(int index, byte[] variationData,
            ushort[] boldData, byte[] italicData)
        {
            var variationIndex = variationData != null && (uint)index < (uint)variationData.Length
                ? variationData[index]
                : (byte)0;
            var bold = boldData != null && (uint)index < (uint)boldData.Length
                ? boldData[index]
                : (ushort)0;
            var italic = italicData != null && (uint)index < (uint)italicData.Length
                ? italicData[index]
                : (byte)0;
            var hasVariation = variationIndex > 0 && variationIndex <= buf.variationConfigs.count;
            var variation = hasVariation ? buf.variationConfigs[variationIndex - 1] : default;
            return CreateFontFaceRequest(bold, italic, in variation, hasVariation);
        }

        private static bool IsEmojiClusterForResolution(ReadOnlySpan<int> cluster)
        {
            if (!EmojiFont.IsAvailable || cluster.IsEmpty) return false;
            if (cluster.Length == 1)
                return (uint)cluster[0] >= UnicodeData.EmojiRangeThreshold
                       && IsSingleCodepointEmoji(cluster[0]);
            return EmojiSequenceClassifier.IsEmojiCluster(cluster);
        }

        /// <summary>
        /// Splits the codepoint range [<paramref name="rangeStart"/>, <paramref name="rangeEnd"/>) into shaping
        /// runs, breaking at BiDi level, script, language, font and mandatory-break boundaries (Common/Inherited
        /// carry the current script per UAX #24; a space separator inherits the preceding font, Pango rule).
        /// Appends to <paramref name="outRuns"/> with indices in the spans' own coordinate space — callers pass
        /// whole-document spans for body paragraphs and local spans for isolated annotation runs.
        /// <paramref name="langData"/>, <paramref name="fontData"/> and <paramref name="resolvedOverrides"/> are
        /// null when the caller has no per-codepoint language/font data. Codepoints flagged in
        /// <paramref name="hidden"/> with <see cref="HiddenClusterBits.Collapse"/> are dropped from runs
        /// the same way mandatory breaks are — they never reach the shaper; no other hidden bit does.
        /// </summary>
        private void ItemizeRuns(int rangeStart, int rangeEnd,
            Span<int> cp, ReadOnlySpan<byte> levels, ReadOnlySpan<UnicodeScript> scripts,
            Span<bool> graphemeBreaks, byte[] langData, int[] fontData, int[] resolvedOverrides,
            ReadOnlySpan<byte> hidden, ref PooledBuffer<TextRun> outRuns,
            int uniformFontOverride = 0)
        {
            var cpCount = rangeEnd;
            if (rangeStart >= rangeEnd) return;

            if (!emojiDiagSnapshotLogged)
            {
                emojiDiagSnapshotLogged = true;
                CatZones.fontProvider.Meow($"[EmojiDiag] first runs-build: thread={System.Threading.Thread.CurrentThread.ManagedThreadId}, unicodeInit={UnicodeData.IsInitialized}, emojiInstance={EmojiFont.HasInstance}");
            }

            var fp = fontProvider;

            var runStart = rangeStart;
            while (runStart < cpCount && (UnicodeData.IsMandatoryBreakChar(cp[runStart]) || IsShapingExcluded(hidden, runStart)))
                runStart++;

            if (runStart >= cpCount) return;

            var currentLevel = levels[runStart];
            var currentScript = scripts[runStart];
            var currentLanguage = ResolveLanguage(langData, runStart);
            var currentIsReal = IsRealScript(currentScript);

            var clusterStart = runStart;
            var clusterEnd = FindNextClusterStart(graphemeBreaks, runStart + 1, cpCount);
            var currentFontOverride = fontData != null && (uint)runStart < (uint)fontData.Length
                ? fontData[runStart]
                : uniformFontOverride;
            var currentFontId = GetFontIdForCluster(cp, clusterStart, clusterEnd, fp, resolvedOverrides, currentLanguage, currentFontOverride);

            for (var i = clusterEnd; i < cpCount; i++)
            {
                if (!graphemeBreaks[i])
                    continue;

                if (UnicodeData.IsMandatoryBreakChar(cp[i]) || IsShapingExcluded(hidden, i))
                {
                    if (i > runStart)
                        AddRun(ref outRuns, runStart, i - runStart, currentLevel, currentScript, currentFontId, currentLanguage);

                    runStart = i + 1;
                    while (runStart < cpCount && (UnicodeData.IsMandatoryBreakChar(cp[runStart]) || IsShapingExcluded(hidden, runStart)))
                        runStart++;

                    if (runStart >= cpCount) return;

                    currentLevel = levels[runStart];
                    currentScript = scripts[runStart];
                    currentLanguage = ResolveLanguage(langData, runStart);
                    currentIsReal = IsRealScript(currentScript);
                    clusterStart = runStart;
                    clusterEnd = FindNextClusterStart(graphemeBreaks, runStart + 1, cpCount);
                    currentFontOverride = fontData != null && (uint)runStart < (uint)fontData.Length
                        ? fontData[runStart]
                        : uniformFontOverride;
                    currentFontId = GetFontIdForCluster(cp, clusterStart, clusterEnd, fp, resolvedOverrides, currentLanguage, currentFontOverride);
                    i = runStart;
                    continue;
                }

                var level = levels[i];
                var script = scripts[i];
                var language = ResolveLanguage(langData, i);

                clusterStart = i;
                clusterEnd = FindNextClusterStart(graphemeBreaks, i + 1, cpCount);
                var fontOverride = fontData != null && (uint)i < (uint)fontData.Length
                    ? fontData[i]
                    : uniformFontOverride;
                var fontId = GetFontIdForCluster(cp, clusterStart, clusterEnd, fp, resolvedOverrides, language, fontOverride);

                var scriptIsReal = IsRealScript(script);
                var scriptChanged = currentIsReal && scriptIsReal && currentScript != script;
                var languageChanged = language != currentLanguage;

                if (resolvedOverrides == null
                    && fontId != currentFontId && level == currentLevel && !scriptChanged && !languageChanged
                    && IsSpaceSeparator(cp[clusterStart])
                    && !EmojiFont.IsSystemEmojiFont(fp.GetFont(currentFontId)))
                {
                    fontId = currentFontId;
                }

                if (level != currentLevel || scriptChanged || languageChanged || fontId != currentFontId)
                {
                    if (i > runStart)
                        AddRun(ref outRuns, runStart, i - runStart, currentLevel, currentScript, currentFontId, currentLanguage);

                    runStart = i;
                    currentLevel = level;
                    currentScript = scriptIsReal ? script : currentScript;
                    currentIsReal = scriptIsReal || currentIsReal;
                    currentLanguage = language;
                    currentFontId = fontId;
                }
                else if (scriptIsReal && !currentIsReal)
                {
                    currentScript = script;
                    currentIsReal = true;
                }
            }

            if (runStart < cpCount)
                AddRun(ref outRuns, runStart, cpCount - runStart, currentLevel, currentScript, currentFontId, currentLanguage);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsShapingExcluded(ReadOnlySpan<byte> hidden, int index)
            => (uint)index < (uint)hidden.Length && (hidden[index] & HiddenClusterBits.Collapse) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddRun(ref PooledBuffer<TextRun> outRuns, int start, int length, byte bidiLevel, UnicodeScript script, int fontId, byte language)
        {
            outRuns.Add(new TextRun
            {
                range = new TextRange(start, length),
                bidiLevel = bidiLevel,
                language = language,
                script = script,
                fontId = fontId
            });
        }

        /// <summary>
        /// Returns true if script is a "real" script (not Common or Inherited).
        /// Common/Inherited scripts are compatible with any other script per UAX #24.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsRealScript(UnicodeScript script)
        {
            return script != UnicodeScript.Common && script != UnicodeScript.Inherited;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCoherentSequenceSeparator(int codepoint)
            => UnicodeData.IsWhiteSpace(codepoint) || codepoint == UnicodeData.ZeroWidthSpace;

        /// <summary>
        /// Returns true if the codepoint is a space separator that should inherit the font
        /// of the preceding character (Pango standard). Uses General Category Zs
        /// which covers all Unicode space separators (U+0020, U+00A0, U+2000–U+200A, etc.).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSpaceSeparator(int cp)
        {
            return UnicodeData.Provider.GetGeneralCategory(cp) == GeneralCategory.Zs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FindNextClusterStart(Span<bool> graphemeBreaks, int from, int cpCount)
        {
            for (int i = from; i < cpCount; i++)
            {
                if (graphemeBreaks[i])
                    return i;
            }
            return cpCount;
        }

        private static int emojiDiagMissBudget = 24;
        private static int emojiDiagRouteBudget = 6;
        private static int emojiShapeDiagBudget = 8;
        private static bool emojiDiagSnapshotLogged;

        private static int EmojiDiagRoute(int cp, int len, int fontId)
        {
            if (emojiDiagRouteBudget > 0)
            {
                emojiDiagRouteBudget--;
                CatZones.fontProvider.Meow($"[EmojiDiag] emoji cluster routed: U+{cp:X4} len={len} -> fontId={fontId}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
            }
            return fontId;
        }

        private static void EmojiDiagMiss(int cp, int len, bool availAtGate)
        {
            if (emojiDiagMissBudget <= 0 || (uint)cp < UnicodeData.EmojiRangeThreshold) return;
            var p = UnicodeData.Provider;
            bool ep = p.IsEmojiPresentation(cp), xp = p.IsExtendedPictographic(cp);
            if (!ep && !xp) return;
            emojiDiagMissBudget--;
            CatZones.fontProvider.MeowWarn($"[EmojiDiag] emoji cluster NOT color-routed: U+{cp:X4} len={len} avail@gate={availAtGate} avail@log={EmojiFont.HasInstance} ep={ep} xp={xp} thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetFontIdForCluster(Span<int> cpSpan, int start, int end, UniTextFontProvider fp, int[] resolvedOverrides, byte language = 0, int fontOverride = 0)
        {
            var clusterLength = end - start;
            var firstCp = cpSpan[start];
            var cluster = cpSpan.Slice(start, clusterLength);

            var emojiGate = EmojiFont.IsAvailable;
            if (emojiGate)
            {
                if (clusterLength == 1)
                {
                    if ((uint)firstCp >= UnicodeData.EmojiRangeThreshold && IsSingleCodepointEmoji(firstCp))
                        return EmojiDiagRoute(firstCp, clusterLength, fp.ResolveColorClusterFontId(cluster));
                }
                else
                {
                    if (EmojiSequenceClassifier.IsEmojiCluster(cluster))
                        return EmojiDiagRoute(firstCp, clusterLength, fp.ResolveColorClusterFontId(cluster));
                }
            }

            EmojiDiagMiss(firstCp, clusterLength, emojiGate);

            if (resolvedOverrides != null
                && (uint)start < (uint)resolvedOverrides.Length && resolvedOverrides[start] != 0)
                return resolvedOverrides[start];

            var request = default(FontFaceRequest);
            return fp.ResolveSequence(cluster, language, fontOverride,
                in request, out var instance)
                ? instance.fontId
                : fp.PrimaryFontId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsSingleCodepointEmoji(int cp)
        {
            if (UnicodeData.IsRegionalIndicator(cp))
                return false;

            var provider = UnicodeData.Provider;
            return provider.IsEmojiPresentation(cp) || provider.IsExtendedPictographic(cp);
        }

        [ThreadStatic] private static HB.hb_feature_t[] featureScratch;
        [ThreadStatic] private static PooledBuffer<TextRun> isolatedRuns;

        private struct PendingShape
        {
            public int paraIdx;
            public ShapedRun[] runs;
            public int runCount;
            public ShapedGlyph[] glyphs;
            public int glyphCount;
        }

        private PooledBuffer<PendingShape> pendingShapes;
        private byte[] passLangData;
        private int[] passFontData;
        private int[] passOverrides;
        private byte[] passFeatureIds;

        [ThreadStatic] private static PooledBuffer<TextRun> jobRunScratch;

        private ReadOnlySpan<byte> PassHiddenSpan()
        {
            var cpCount = buf.codepoints.count;
            return buf.hiddenClusters.count >= cpCount
                ? (ReadOnlySpan<byte>)buf.hiddenClusters.data.AsSpan(0, cpCount)
                : ReadOnlySpan<byte>.Empty;
        }

        /// <summary>
        /// Opens the paragraph shape pass: fingerprints every paragraph and queues the misses as
        /// independent jobs (<see cref="pendingShapes"/>). Hits are replayed and misses spliced in
        /// <see cref="SpliceShapedParagraphs"/>; between the two, <see cref="RunShapeJob"/> may run
        /// the queued jobs on any thread — each writes only its own slot.
        /// </summary>
        private void PrepareShapePass()
        {
            buf.shapedRuns.count = 0;
            buf.shapedGlyphs.count = 0;
            buf.runs.count = 0;
            ReturnPendingShapeArrays();

            passFeatureIds = buf.FontFeatureIds;
            passLangData = GetLanguageData();
            passFontData = GetFontOverrideData();
            passOverrides = hasFontIdOverrides ? fontIdOverrides.data : null;

            buf.shapedGlyphs.EnsureCapacity(buf.codepoints.count);

            var seed = ComputeShapeSeed();
            shapeCache ??= new ParagraphShapeCache();
            shapeCache.BeginPass(buf.paragraphs.count);
            WordShapeCache.Prune(SharedFontCache.Version);

            var hidden = PassHiddenSpan();
            var table = buf.paragraphs.data;
            var paragraphCount = buf.paragraphs.count;

            for (var p = 0; p < paragraphCount; p++)
            {
                ref var para = ref table[p];
                para.shapeHash = ComputeShapeHash(seed, para.cpStart, para.cpCount,
                    passLangData, passFontData, passOverrides, hidden, passFeatureIds);
                if (!shapeCache.Peek(para.shapeHash, para.cpCount))
                    pendingShapes.Add(new PendingShape { paraIdx = p });
            }
        }

        /// <summary>Number of queued shape jobs for the current pass (0 outside a pass).</summary>
        internal int PendingShapeJobCount => pendingShapes.count;

        /// <summary>
        /// Itemizes and shapes one missed paragraph into job-owned pooled arrays (absolute indices,
        /// job-local glyph offsets). Safe to run concurrently with other jobs of the same processor:
        /// shared buffers are read-only here, the job writes only its own <see cref="pendingShapes"/>
        /// slot, and shaper scratch is per-thread.
        /// </summary>
        internal void RunShapeJob(int missIdx)
        {
            ref var job = ref pendingShapes[missIdx];
            ref readonly var para = ref buf.paragraphs[job.paraIdx];

            var cpCount = buf.codepoints.count;
            var cp = buf.codepoints.Span;
            var hidden = PassHiddenSpan();

            jobRunScratch.FakeClear();
            UniTextDebug.BeginSample("Shape.Itemize");
            ItemizeRuns(para.cpStart, para.CpEnd, cp,
                buf.bidiLevels.data.AsSpan(0, cpCount),
                buf.scripts.data.AsSpan(0, cpCount),
                buf.graphemeBreaks.Span,
                passLangData, passFontData, passOverrides, hidden, ref jobRunScratch);
            UniTextDebug.EndSample();

            var localRuns = new PooledBuffer<ShapedRun>();
            var localGlyphs = new PooledBuffer<ShapedGlyph>();
            try
            {
                ShapeRunRange(jobRunScratch.data.AsSpan(0, jobRunScratch.count), cp,
                    para.cpStart, para.CpEnd, hidden, passFeatureIds,
                    ref localRuns, ref localGlyphs);

                localRuns.EnsureCapacity(1);
                localGlyphs.EnsureCapacity(1);
                job.runs = localRuns.data;
                job.runCount = localRuns.count;
                job.glyphs = localGlyphs.data;
                job.glyphCount = localGlyphs.count;
                localRuns = default;
                localGlyphs = default;
            }
            finally
            {
                localRuns.Return();
                localGlyphs.Return();
            }
        }

        /// <summary>Returns job arrays that never reached the splice (exception-aborted pass, teardown) and clears the queue.</summary>
        private void ReturnPendingShapeArrays()
        {
            for (var i = 0; i < pendingShapes.count; i++)
            {
                ref var job = ref pendingShapes[i];
                if (job.runs != null) ArrayPool<ShapedRun>.Return(job.runs);
                if (job.glyphs != null) ArrayPool<ShapedGlyph>.Return(job.glyphs);
                job.runs = null;
                job.glyphs = null;
            }
            pendingShapes.FakeClear();
        }

        /// <summary>
        /// Closes the shape pass: per paragraph replays the cache hit or splices the job output
        /// (job arrays are then localized in place and moved into the cache — no extra copy). A
        /// duplicate-content paragraph whose single-use entry was already consumed re-shapes inline.
        /// </summary>
        private void SpliceShapedParagraphs()
        {
            var cpCount = buf.codepoints.count;
            var cp = buf.codepoints.Span;
            var hidden = PassHiddenSpan();
            var table = buf.paragraphs.data;
            var paragraphCount = buf.paragraphs.count;

            var pend = 0;
            for (var p = 0; p < paragraphCount; p++)
            {
                ref var para = ref table[p];
                para.runStart = buf.shapedRuns.count;
                para.glyphStart = buf.shapedGlyphs.count;

                if (pend < pendingShapes.count && pendingShapes[pend].paraIdx == p)
                {
                    ref var job = ref pendingShapes[pend];
                    pend++;

                    var glyphBase = buf.shapedGlyphs.count;
                    buf.shapedGlyphs.EnsureCapacity(glyphBase + job.glyphCount);
                    if (job.glyphCount > 0)
                        job.glyphs.AsSpan(0, job.glyphCount).CopyTo(buf.shapedGlyphs.data.AsSpan(glyphBase));
                    buf.shapedGlyphs.count = glyphBase + job.glyphCount;

                    buf.shapedRuns.EnsureCapacity(buf.shapedRuns.count + job.runCount);
                    for (var r = 0; r < job.runCount; r++)
                    {
                        var run = job.runs[r];
                        run.glyphStart += glyphBase;
                        buf.shapedRuns.data[buf.shapedRuns.count++] = run;
                    }

                    for (var g = 0; g < job.glyphCount; g++)
                        job.glyphs[g].cluster -= para.cpStart;
                    for (var r = 0; r < job.runCount; r++)
                        job.runs[r].range = new TextRange(job.runs[r].range.start - para.cpStart, job.runs[r].range.length);

                    shapeCache.StoreMoved(para.shapeHash, para.cpCount, job.runs, job.runCount, job.glyphs, job.glyphCount);
                    job.runs = null;
                    job.glyphs = null;
                }
                else if (!shapeCache.TryAppend(para.shapeHash, para.cpCount, para.cpStart,
                             ref buf.shapedRuns, ref buf.shapedGlyphs))
                {
                    var runFirst = buf.runs.count;
                    ItemizeRuns(para.cpStart, para.CpEnd, cp,
                        buf.bidiLevels.data.AsSpan(0, cpCount),
                        buf.scripts.data.AsSpan(0, cpCount),
                        buf.graphemeBreaks.Span,
                        passLangData, passFontData, passOverrides, hidden, ref buf.runs);
                    ShapeRunRange(buf.runs.data.AsSpan(runFirst, buf.runs.count - runFirst), cp,
                        para.cpStart, para.CpEnd, hidden, passFeatureIds,
                        ref buf.shapedRuns, ref buf.shapedGlyphs);
                    shapeCache.Store(para.shapeHash, para.cpStart, para.cpCount,
                        buf.shapedRuns.data.AsSpan(para.runStart, buf.shapedRuns.count - para.runStart),
                        para.glyphStart,
                        buf.shapedGlyphs.data.AsSpan(para.glyphStart, buf.shapedGlyphs.count - para.glyphStart));
                }

                para.runCount = buf.shapedRuns.count - para.runStart;
                para.glyphCount = buf.shapedGlyphs.count - para.glyphStart;
            }

            pendingShapes.FakeClear();
            shapeCache.EndPass();
        }

        private ulong ComputeShapeSeed()
        {
            var seed = XxHash64.Combine((ulong)shapeEpoch,
                (ulong)(uint)BitConverter.SingleToInt32Bits(buf.shapingFontSize));
            seed = XxHash64.Combine(seed, cachedSettingsLanguageIndex);
            return XxHash64.Combine(seed, EmojiFont.IsAvailable ? 1UL : 0UL);
        }

        private ulong ComputeShapeHash(ulong seed, int start, int count,
            byte[] langData, int[] fontData, int[] overrides, ReadOnlySpan<byte> hidden,
            byte[] featureIds)
        {
            var end = start + count;
            var h = XxHash64.Hash<int>(buf.codepoints.data.AsSpan(start, count), seed);
            h = XxHash64.Hash<byte>(buf.bidiLevels.data.AsSpan(start, count), h);
            h = XxHash64.Hash<UnicodeScript>(buf.scripts.data.AsSpan(start, count), h);
            if (langData != null && langData.Length >= end) h = XxHash64.Hash<byte>(langData.AsSpan(start, count), h);
            if (fontData != null && fontData.Length >= end) h = XxHash64.Hash<int>(fontData.AsSpan(start, count), h);
            if (overrides != null && overrides.Length >= end) h = XxHash64.Hash<int>(overrides.AsSpan(start, count), h);
            h = CombineCollapsed(h, hidden, start, end);
            if (featureIds != null && featureIds.Length >= end) h = XxHash64.Hash<byte>(featureIds.AsSpan(start, count), h);
            return h;
        }

        /// <summary>
        /// Folds the paragraph's collapsed clusters into <paramref name="hash"/>. Only
        /// <see cref="HiddenClusterBits.Collapse"/> reaches the shaper, so the other hidden bits stay
        /// out of the fingerprint: a reveal, scramble or roll that merely stops drawing glyphs leaves
        /// the paragraph's shaping cached.
        /// </summary>
        private static ulong CombineCollapsed(ulong hash, ReadOnlySpan<byte> hidden, int start, int end)
        {
            if (hidden.IsEmpty) return hash;

            var collapsed = 0UL;
            var any = false;
            for (var i = start; i < end; i++)
            {
                if ((hidden[i] & HiddenClusterBits.Collapse) == 0) continue;
                collapsed = XxHash64.Combine(collapsed, (ulong)(uint)(i - start));
                any = true;
            }

            return any ? XxHash64.Combine(hash, collapsed) : hash;
        }

        private void ShapeRunRange(ReadOnlySpan<TextRun> runs, Span<int> cp, int paraStart, int paraEnd,
            ReadOnlySpan<byte> hidden, byte[] featureIds,
            ref PooledBuffer<ShapedRun> outRuns, ref PooledBuffer<ShapedGlyph> outGlyphs)
        {
            var shaper = Shaper;
            int lastShapeFontId = int.MinValue;
            float shapeScale = 0;
            int shapeSpacingOffsetUnits = 0;
            float shapeFakeBoldAdvancePx = 0f;
            HB.hb_variation_t[] shapeDefaultVariations = null;

            for (var i = 0; i < runs.Length; i++)
            {
                ref readonly var run = ref runs[i];
                var isEmojiRun = EmojiFont.IsSystemEmojiFont(fontProvider.GetFont(run.fontId));

                if (run.fontId != lastShapeFontId)
                {
                    lastShapeFontId = run.fontId;
                    shapeScale = shaper.ComputeShapeParams(fontProvider, run.fontId,
                        out shapeSpacingOffsetUnits, out shapeFakeBoldAdvancePx);
                    shapeDefaultVariations = isEmojiRun
                        ? null
                        : fontProvider.GetFont(run.fontId)?.DefaultHbVariations;
                }

                HB.hb_variation_t[] runVariations = shapeDefaultVariations;
                if (variationMap != null && variationMap.TryGetValue(run.fontId, out var varInfo))
                    runVariations = varInfo.hbVariations;

                var glyphStart = outGlyphs.count;

                var languageHandle = LanguageRegistry.GetHandle(run.language);

                var shapingScript = isEmojiRun ? UnicodeScript.Common : run.script;
                var shapingDirection = isEmojiRun ? TextDirection.LeftToRight : run.Direction;

                var ctxStart = paraStart;
                var ctxEnd = paraEnd;
                if (!hidden.IsEmpty && !isEmojiRun)
                {
                    ctxStart = run.range.start;
                    while (ctxStart > paraStart && (hidden[ctxStart - 1] & HiddenClusterBits.Collapse) == 0)
                        ctxStart--;
                    ctxEnd = run.range.End;
                    while (ctxEnd < paraEnd && (hidden[ctxEnd] & HiddenClusterBits.Collapse) == 0)
                        ctxEnd++;
                }

                var featureCount = CollectRunFeatures(featureIds, run.range.start, run.range.length, ctxStart);
                var runFeatures = featureCount > 0 ? featureScratch : null;

                int glyphCount;
                float runAdvance;
                if (!isEmojiRun && ScriptAnalyzer.IsWordCacheable(run.script))
                {
                    glyphCount = shaper.ShapeWordCachedRun(
                        ref outGlyphs, cp, run.range.start, run.range.length, ctxStart, ctxEnd,
                        fontProvider, run.fontId, shapingScript, shapingDirection,
                        shapeScale, shapeSpacingOffsetUnits, shapeFakeBoldAdvancePx,
                        run.language, runVariations, featureIds, runFeatures, featureCount,
                        out runAdvance);
                }
                else
                {
                    glyphCount = shaper.ShapeInto(
                        ref outGlyphs,
                        cp.Slice(ctxStart, ctxEnd - ctxStart),
                        run.range.start - ctxStart,
                        run.range.length,
                        fontProvider,
                        run.fontId,
                        shapingScript,
                        shapingDirection,
                        shapeScale,
                        shapeSpacingOffsetUnits,
                        shapeFakeBoldAdvancePx,
                        out runAdvance,
                        runVariations,
                        runFeatures,
                        featureCount,
                        languageHandle,
                        ctxStart);
                }

                if (isEmojiRun && emojiShapeDiagBudget > 0)
                {
                    emojiShapeDiagBudget--;
                    int gid0 = 0;
                    var shaped = outGlyphs.data;
                    for (var k = glyphStart; k < glyphStart + glyphCount; k++)
                        if (shaped[k].glyphId == 0) gid0++;
                    CatZones.fontProvider.Meow($"[EmojiDiag] emoji run shaped: glyphs={glyphCount}, gid0={gid0}, thread={System.Threading.Thread.CurrentThread.ManagedThreadId}");
                }

                outRuns.Add(new ShapedRun
                {
                    range = run.range,
                    glyphStart = glyphStart,
                    glyphCount = glyphCount,
                    width = runAdvance,
                    direction = shapingDirection,
                    bidiLevel = run.bidiLevel,
                    language = run.language,
                    fontId = run.fontId
                });
            }
        }

        /// <summary>
        /// Shapes isolated text through the engine's real analysis — UAX#9 bidi,
        /// UAX#24 script, grapheme clustering, per-cluster font resolution, language and HarfBuzz — exactly
        /// like body text, but as its own bidi isolate. Emits glyphs in visual order into
        /// <paramref name="outGlyphs"/>, appending the resolving font id per glyph to
        /// <paramref name="outGlyphFonts"/> (kept parallel). Layout (placement over/under the base) is the
        /// caller's job. <paramref name="sizeScale"/> is applied on top of each font's shaping scale.
        /// A nonzero <paramref name="uniformFontId"/> requests one font for every cluster, while
        /// <paramref name="features"/> applies one OpenType feature set to every emitted run.
        /// </summary>
        internal void ShapeIsolatedRun(Span<int> codepoints, float sizeScale,
            ref PooledBuffer<ShapedGlyph> outGlyphs, ref PooledBuffer<int> outGlyphFonts,
            out float totalWidth, int uniformFontId = 0, HB.hb_feature_t[] features = null)
        {
            totalWidth = 0f;
            var n = codepoints.Length;
            if (n == 0) return;

            var bidi = BidiEngine.Process(codepoints, BidiParagraphDirection.Auto);
            var baseRtl = bidi.Direction == BidiDirection.RightToLeft;

            var scripts = ArrayPool<UnicodeScript>.Rent(n);
            var graphemes = ArrayPool<bool>.Rent(n + 1);
            try
            {
                AnalyzeScripts(codepoints, scripts);
                var gSpan = graphemes.AsSpan(0, n + 1);
                AnalyzeGraphemes(codepoints, gSpan);

                isolatedRuns.count = 0;
                ItemizeRuns(0, n, codepoints, bidi.levels.AsSpan(0, n), scripts.AsSpan(0, n), gSpan,
                    null, null, null, ReadOnlySpan<byte>.Empty, ref isolatedRuns, uniformFontId);

                var shaper = Shaper;
                var langHandle = LanguageRegistry.GetHandle(cachedSettingsLanguageIndex);
                var runCount = isolatedRuns.count;
                for (var r = 0; r < runCount; r++)
                {
                    ref readonly var run = ref isolatedRuns[baseRtl ? runCount - 1 - r : r];
                    var scale = shaper.ComputeShapeParams(fontProvider, run.fontId,
                        out var spacingOffsetUnits, out var fakeBoldAdvancePx) * sizeScale;
                    var variations = fontProvider.GetFont(run.fontId)?.DefaultHbVariations;
                    var gc = shaper.ShapeInto(ref outGlyphs, codepoints, run.range.start, run.range.length, fontProvider,
                        run.fontId, run.script, run.Direction, scale, spacingOffsetUnits,
                        fakeBoldAdvancePx * sizeScale, out var adv, variations, features, -1, langHandle);
                    for (var g = 0; g < gc; g++)
                        outGlyphFonts.Add(run.fontId);
                    totalWidth += adv;
                }
            }
            finally
            {
                ArrayPool<UnicodeScript>.Return(scripts);
                ArrayPool<bool>.Return(graphemes);
            }
        }

        /// <summary>
        /// Expands the run's per-codepoint feature-set ids into <see cref="featureScratch"/> as one
        /// <see cref="HB.hb_feature_t"/> per setting per maximal same-id span. Feature extents are cluster
        /// values, so they are stated relative to <paramref name="ctxStart"/> — the start of the codepoint
        /// window the run is shaped in, not the run.
        /// </summary>
        private static int CollectRunFeatures(byte[] featureIds, int runStart, int runLen, int ctxStart)
        {
            if (featureIds == null) return 0;

            featureScratch ??= new HB.hb_feature_t[16];
            var count = 0;
            var runEnd = runStart + runLen;

            var i = runStart;
            while (i < runEnd)
            {
                var id = FeatureIdAt(featureIds, i);
                var spanStart = i;
                do i++;
                while (i < runEnd && FeatureIdAt(featureIds, i) == id);

                if (id == FontFeatureRegistry.Unset) continue;

                var features = FontFeatureRegistry.Get(id);
                var start = (uint)(spanStart - ctxStart);
                var end = (uint)(i - ctxStart);
                for (var f = 0; f < features.Length; f++)
                {
                    if (count >= featureScratch.Length)
                        Array.Resize(ref featureScratch, featureScratch.Length * 2);

                    featureScratch[count++] = new HB.hb_feature_t
                    {
                        tag = features[f].Tag, value = features[f].Value,
                        start = start, end = end
                    };
                }
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte FeatureIdAt(byte[] featureIds, int index)
            => (uint)index < (uint)featureIds.Length ? featureIds[index] : FontFeatureRegistry.Unset;
    }
}
