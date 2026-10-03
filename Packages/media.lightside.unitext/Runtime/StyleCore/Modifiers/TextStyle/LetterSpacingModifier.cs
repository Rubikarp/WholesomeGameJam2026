using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies character spacing (tracking) adjustments to text ranges.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parameter: spacing value with unit.
    /// <list type="bullet">
    /// <item><c>10</c> — add 10 pixels between characters</item>
    /// <item><c>-5</c> — reduce spacing by 5 pixels</item>
    /// <item><c>0.5em</c> — add 0.5 em (relative to font size)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Overlapping ranges resolve innermost-wins and never compound, across modifiers as well as
    /// across nested tags: one tracking value reaches each character, whichever range set it.
    /// </para>
    /// <para>
    /// For cursive joining scripts (Arabic, Syriac, N'Ko, Adlam, etc.), visual kashida
    /// (tatweel) bars are rendered between connected letter pairs using 9-slice SDF rendering,
    /// preserving the appearance of cursive connections at any spacing value.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Text Style", 0)]
    [TypeDescription("Adjusts the spacing between characters.")]
    [GenerateParameters]
    public partial class LetterSpacingModifier : PooledAttributeModifier<float>
    {
        /// <summary>Extra tracking between characters used when a range does not override it.</summary>
        [SerializeField, Parameter, Unit("px|em"), StateProperty(nameof(MarkTextDirty))]
        private UnitValue spacing = UnitValue.Absolute(0.3f);

        protected sealed override string AttributeKey => AttributeKeys.LetterSpacing;

        protected override AttributeChannel CreateChannel() => new Channel();

        protected override void OnApply(in RangeApplyContext context)
        {
            var value = Param.Spacing.Resolve(this, in context);

            var baseSize = buffers.shapingFontSize > 0 ? buffers.shapingFontSize : uniText.FontSize;
            var resolved = value.ResolvePx(baseSize);

            attribute.FillRange(context.Segment.Range, resolved);

            buffers.RequestVirtualCodepoint((uint)UnicodeData.ArabicTatweel);
        }

        private sealed class Channel : AttributeChannel
        {
            private const string KashidaAttributeKey = "cspace.kashida";
            private const string ScaleAttributeKey = "cspace.scale";
            private const string LigatureExemptAttributeKey = "cspace.ligexempt";

            private PooledArrayAttribute<float> attribute;
            private PooledArrayAttribute<byte> kashidaAttribute;
            private PooledArrayAttribute<float> scaleAttribute;

            /// <summary>
            /// Marks clusters that carry no tracking in the shaped advances regardless of what the
            /// spacing attribute holds; produced and owned exclusively by the shaping pass.
            /// </summary>
            private PooledArrayAttribute<byte> ligatureExemptAttribute;

            private bool hasCompressionScales;

            private struct KashidaSegment
            {
                public float startX;
                public float endX;
                public float baselineY;
                public int fontId;
                public long varHash48;
                public int cluster;
            }

            private KashidaSegment[] kashidaSegments;
            private int kashidaSegmentCount;
            private int kashidaSegmentCapacity;

            private Action shapedCallback;
            private Action linesBrokenCallback;
            private Action meshGlyphCallback;
            private Action mainPassCompleteCallback;

            protected override void OnActivate()
            {
                attribute = buffers.GetAttributeData<PooledArrayAttribute<float>>(Key);
                buffers.PrepareAttribute(ref kashidaAttribute, KashidaAttributeKey);
                buffers.PrepareAttribute(ref scaleAttribute, ScaleAttributeKey);
                buffers.PrepareAttribute(ref ligatureExemptAttribute, LigatureExemptAttributeKey);
                hasCompressionScales = false;

                if (kashidaSegments == null)
                {
                    kashidaSegments = ArrayPool<KashidaSegment>.Rent(32);
                    kashidaSegmentCapacity = 32;
                }
                kashidaSegmentCount = 0;

                shapedCallback ??= OnShaped;
                uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
                linesBrokenCallback ??= OnLinesBroken;
                uniText.TextProcessor.LinesBroken.Subscribe(linesBrokenCallback, -1000);
                meshGlyphCallback ??= OnMeshGlyph;
                uniText.MeshGenerator.onGlyph.Subscribe(meshGlyphCallback);
                mainPassCompleteCallback ??= OnMainPassComplete;
                uniText.MeshGenerator.onMainPassComplete.Subscribe(mainPassCompleteCallback);
            }

            protected override void OnDeactivate()
            {
                uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
                uniText.TextProcessor.LinesBroken.Unsubscribe(linesBrokenCallback);
                uniText.MeshGenerator.onGlyph.Unsubscribe(meshGlyphCallback);
                uniText.MeshGenerator.onMainPassComplete.Unsubscribe(mainPassCompleteCallback);
            }

            protected override void OnRelease()
            {
                buffers?.ReleaseAttributeData(KashidaAttributeKey);
                buffers?.ReleaseAttributeData(ScaleAttributeKey);
                buffers?.ReleaseAttributeData(LigatureExemptAttributeKey);
                attribute = null;
                kashidaAttribute = null;
                scaleAttribute = null;
                ligatureExemptAttribute = null;

                if (kashidaSegments != null)
                {
                    ArrayPool<KashidaSegment>.Return(kashidaSegments);
                    kashidaSegments = null;
                }
            }

            private void OnShaped()
            {
                if (attribute == null)
                    return;

                var buffer = attribute.buffer.data;
                if (buffer == null)
                    return;

                var buf = buffers;
                var glyphs = buf.shapedGlyphs.data;
                var runs = buf.shapedRuns.data;
                var runCount = buf.shapedRuns.count;
                var bufLen = buffer.Length;

                ApplySimpleSpacing(buffer, bufLen, glyphs, runs, runCount);

                var codepoints = buf.codepoints.data;
                var scripts = buf.scripts.data;
                var cpCount = buf.codepoints.count;
                FlagKashidaPairs(glyphs, runs, runCount, buffer, bufLen,
                    codepoints, scripts, cpCount, UnicodeData.Provider);

                ComputeCompressionScales(glyphs, runs, runCount, buffer, bufLen, scripts, cpCount);
            }

            private void OnLinesBroken()
            {
                var spacingBuf = attribute?.buffer.data;
                if (spacingBuf == null) return;

                var lines = buffers.lines.data;
                var lineCount = buffers.lines.count;
                if (lineCount == 0) return;

                var codepoints = buffers.codepoints.data;
                var cpCount = buffers.codepoints.count;
                var kashidaFlags = kashidaAttribute?.buffer.data;
                var exemptFlags = ligatureExemptAttribute?.buffer.data;
                var spacingLen = spacingBuf.Length;

                for (var i = 0; i < lineCount; i++)
                {
                    ref var line = ref lines[i];
                    var rangeStart = line.range.start;
                    var rangeEnd = rangeStart + line.range.length - 1;
                    if (rangeEnd < rangeStart || (uint)rangeEnd >= (uint)cpCount) continue;

                    var lastCp = -1;
                    for (var cp = rangeEnd; cp >= rangeStart; cp--)
                    {
                        if (uniText.TextProcessor.IsClusterHiddenFromLayout(cp)) continue;
                        if (!LineBreaker.IsHangingWhitespace(codepoints[cp]))
                        {
                            lastCp = cp;
                            break;
                        }
                    }
                    if (lastCp < 0) continue;

                    if ((uint)lastCp >= (uint)spacingLen) continue;
                    var spacing = spacingBuf[lastCp];
                    if (spacing == 0f) continue;

                    if (kashidaFlags.HasFlag(lastCp) || exemptFlags.HasFlag(lastCp))
                        continue;

                    line.width -= spacing;
                }
            }

            /// <summary>
            /// Adds spacing to glyph advances for all runs. Skips zero-advance marks.
            /// </summary>
            private static void ApplySimpleSpacing(float[] buffer, int bufLen,
                ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount)
            {
                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var glyphEnd = run.glyphStart + run.glyphCount;
                    float width = 0f;

                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        var cluster = glyphs[g].cluster;

                        if ((uint)cluster < (uint)bufLen)
                        {
                            var spacing = buffer[cluster];
                            if (spacing != 0f && glyphs[g].advanceX != 0f)
                                glyphs[g].advanceX += spacing;
                        }

                        width += glyphs[g].advanceX;
                    }

                    run.width = width;
                }
            }

            private void FlagKashidaPairs(
                ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount,
                float[] buffer, int bufLen,
                int[] codepoints, UnicodeScript[] scripts, int cpCount,
                UnicodeDataProvider provider)
            {
                var flags = kashidaAttribute?.buffer.data;
                var exempt = ligatureExemptAttribute?.buffer.data;
                if (flags == null || exempt == null) return;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    if (run.glyphCount < 2) continue;

                    var script = GetRunScript(ref run, scripts, cpCount);
                    if (!script.IsCursiveJoining()) continue;

                    var glyphEnd = run.glyphStart + run.glyphCount;

                    for (var g = run.glyphStart; g < glyphEnd - 1; g++)
                    {
                        if (glyphs[g].advanceX == 0f) continue;

                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster >= (uint)bufLen || buffer[cluster] == 0f ||
                            exempt.HasFlag(cluster))
                            continue;

                        var nextG = g + 1;
                        while (nextG < glyphEnd && glyphs[nextG].advanceX == 0f)
                            nextG++;
                        if (nextG >= glyphEnd) break;

                        var nextCluster = glyphs[nextG].cluster;
                        if (cluster == nextCluster) continue;

                        if (AreConnected(cluster, nextCluster, codepoints, cpCount, provider))
                        {
                            if (IsLamAlefLigature(nextCluster, codepoints, cpCount))
                            {
                                if ((uint)cluster >= (uint)exempt.Length)
                                    continue;

                                var spacing = buffer[cluster];
                                glyphs[g].advanceX -= spacing;
                                run.width -= spacing;
                                exempt[cluster] = 1;
                                continue;
                            }

                            if ((uint)cluster < (uint)flags.Length)
                                flags[cluster] = 1;
                        }
                    }
                }
            }

            /// <summary>
            /// For cursive scripts with negative spacing, computes per-cluster horizontal
            /// scale factors. Glyphs are compressed instead of overlapping, preserving connections.
            /// </summary>
            private void ComputeCompressionScales(
                ShapedGlyph[] glyphs, ShapedRun[] runs, int runCount,
                float[] spacingBuf, int bufLen, UnicodeScript[] scripts, int cpCount)
            {
                hasCompressionScales = false;
                var scaleBuf = scaleAttribute?.buffer.data;
                if (scaleBuf == null) return;

                var exempt = ligatureExemptAttribute?.buffer.data;

                for (var r = 0; r < runCount; r++)
                {
                    ref var run = ref runs[r];
                    var script = GetRunScript(ref run, scripts, cpCount);
                    if (!script.IsCursiveJoining()) continue;

                    var glyphEnd = run.glyphStart + run.glyphCount;
                    for (var g = run.glyphStart; g < glyphEnd; g++)
                    {
                        if (glyphs[g].advanceX == 0f) continue;

                        var cluster = glyphs[g].cluster;
                        if ((uint)cluster >= (uint)bufLen || exempt.HasFlag(cluster)) continue;

                        var spacing = spacingBuf[cluster];
                        if (spacing >= 0f) continue;

                        var advance = glyphs[g].advanceX;
                        var original = advance - spacing;
                        if (original < 0.001f) continue;

                        var scale = advance / original;
                        if (scale < 0.1f) scale = 0.1f;

                        if ((uint)cluster < (uint)scaleBuf.Length)
                        {
                            scaleBuf[cluster] = scale;
                            hasCompressionScales = true;
                        }
                    }
                }
            }

            /// <summary>
            /// Per-glyph callback: compresses quad vertices for cursive glyphs with negative spacing.
            /// </summary>
            private void OnMeshGlyph()
            {
                if (!hasCompressionScales) return;

                var gen = uniText.MeshGenerator;
                var scaleBuf = scaleAttribute?.buffer.data;
                if (scaleBuf == null) return;

                var cluster = gen.currentCluster;
                if ((uint)cluster >= (uint)scaleBuf.Length) return;

                var scale = scaleBuf[cluster];
                if (scale == 0f) return;

                var verts = gen.Vertices;
                var vi = gen.faceBaseIdx;

                var centerX = (verts[vi].x + verts[vi + 2].x) * 0.5f;
                var leftX = centerX + (verts[vi].x - centerX) * scale;
                var rightX = centerX + (verts[vi + 2].x - centerX) * scale;

                verts[vi].x = leftX;
                verts[vi + 1].x = leftX;
                verts[vi + 2].x = rightX;
                verts[vi + 3].x = rightX;
            }

            /// <summary>
            /// Emits every kashida bar after the main glyph pass. Deferred out of the per-glyph
            /// <c>onGlyph</c> (where it would re-enter and clobber the base glyph's face-quad state
            /// the deferred painting system reads post-<c>onGlyph</c>); each bar still fires
            /// <c>onGlyph</c>, so color and effect layers decorate it like a primary glyph.
            /// </summary>
            private void OnMainPassComplete()
            {
                var gen = uniText.MeshGenerator;
                ComputeKashidaSegments(gen);
                if (kashidaSegmentCount == 0) return;

                var fontProvider = uniText.FontProvider;
                var atlas = GlyphAtlas.GetInstance(gen.RenderMode);
                for (var i = 0; i < kashidaSegmentCount; i++)
                    DrawKashida(gen, fontProvider, atlas, ref kashidaSegments[i]);
            }

            private void ComputeKashidaSegments(UniTextMeshGenerator gen)
            {
                kashidaSegmentCount = 0;

                var flags = kashidaAttribute?.buffer.data;
                if (!flags.HasAnyFlags()) return;

                var spacingBuf = attribute?.buffer.data;
                if (spacingBuf == null) return;

                var allGlyphs = buffers.positionedGlyphs.data;
                var glyphCount = buffers.positionedGlyphs.count;
                if (glyphCount == 0) return;

                var shapedGlyphs = buffers.shapedGlyphs.data;
                var offsetX = gen.offsetX;
                var offsetY = gen.offsetY;
                var fontProvider = uniText.FontProvider;

                for (var i = 0; i < glyphCount; i++)
                {
                    ref readonly var glyph = ref allGlyphs[i];
                    var cluster = glyph.cluster;

                    if ((uint)cluster >= (uint)flags.Length || flags[cluster] == 0)
                        continue;

                    var spacing = ((uint)cluster < (uint)spacingBuf.Length) ? spacingBuf[cluster] : 0f;
                    if (spacing == 0f) continue;

                    var shapedIdx = glyph.shapedGlyphIndex;
                    if (shapedIdx < 0) continue;

                    var shapedAdvance = shapedGlyphs[shapedIdx].advanceX;
                    if (shapedAdvance < 0.001f) continue;

                    var posAdvance = glyph.right - glyph.left;
                    var spacingScaled = posAdvance * (spacing / shapedAdvance);

                    var kashidaEnd = offsetX + glyph.right;
                    var kashidaStart = kashidaEnd - spacingScaled;

                    if (kashidaEnd <= kashidaStart + 0.01f) continue;

                    var baselineY = offsetY - glyph.y;

                    var glyphFont = fontProvider.GetFont(glyph.fontId);
                    var varHash = glyphFont != null ? buffers.ResolveVarHash48(glyph.fontId, glyphFont) : 0L;

                    AddKashidaSegment(kashidaStart, kashidaEnd, baselineY, glyph.fontId, varHash, cluster);
                }
            }

            private void AddKashidaSegment(float startX, float endX, float baselineY, int fontId, long varHash48, int cluster)
            {
                ArrayPool<KashidaSegment>.GrowDouble(ref kashidaSegments, ref kashidaSegmentCapacity, kashidaSegmentCount);

                kashidaSegments[kashidaSegmentCount++] = new KashidaSegment
                {
                    startX = startX,
                    endX = endX,
                    baselineY = baselineY,
                    fontId = fontId,
                    varHash48 = varHash48,
                    cluster = cluster
                };
            }

            /// <summary>
            /// Draws a single kashida bar using 9-slice SDF rendering of the tatweel glyph.
            /// Each kashida is split into 3 horizontal slices: left cap (UV.x: -sdfPadding..centerX),
            /// center stretch (UV.x: centerX constant), right cap (UV.x: centerX..aspect+sdfPadding).
            /// This keeps tatweel's SDF cap shape on the ends while stretching the middle, so an
            /// outline modifier (which dilates the SDF) draws a clean periphery on every kashida.
            /// If the kashida is too narrow to fit two caps, falls back to one fully stretched quad.
            /// </summary>
            private static void DrawKashida(UniTextMeshGenerator gen, UniTextFontProvider fontProvider,
                GlyphAtlas atlas, ref KashidaSegment seg)
            {
                var font = fontProvider.GetFont(seg.fontId);
                if (font == null) return;

                var tatweelIndex = font.GetGlyphIndexForUnicode((uint)UnicodeData.ArabicTatweel);
                if (tatweelIndex == 0) return;

                var varHash = seg.varHash48;
                var glyphKey = GlyphAtlas.MakeKey(varHash, tatweelIndex);
                if (!atlas.TryGetEntry(glyphKey, out var entry) || entry.encodedTile < 0)
                    return;

                var glyphLookup = font.GlyphLookupTable;
                if (glyphLookup == null ||
                    !glyphLookup.TryGetValue(glyphKey, out var glyphData))
                    return;

                gen.TrackGlyphKey(glyphKey);

                var metrics = glyphData.metrics;
                var upem = (float)font.UnitsPerEm;
                var metricsFactor = fontProvider.MetricScale(font, gen.FontSize) * upem;

                var glyphH = metrics.height / upem;
                if (glyphH < 1e-6f) return;
                var glyphW = metrics.width / upem;
                var aspect = glyphW / glyphH;

                const float sdfPadding = 0.02f;
                var padEm = sdfPadding * glyphH;
                var bearingXNorm = metrics.horizontalBearingX / upem;
                var bearingYNorm = metrics.horizontalBearingY / upem;
                var advanceNorm = metrics.horizontalAdvance / upem;

                var topY = seg.baselineY + (bearingYNorm + padEm) * metricsFactor;
                var bottomY = topY - (1f + sdfPadding * 2f) * glyphH * metricsFactor;
                var quadHeight = topY - bottomY;

                var leftPad = (bearingXNorm - padEm) * metricsFactor;
                var rightPad = (bearingXNorm + glyphW + padEm - advanceNorm) * metricsFactor;
                var quadLeftX = seg.startX + leftPad;
                var quadRightX = seg.endX + rightPad;

                var tileIdx = (float)entry.handle;
                var centerX = aspect * 0.5f;
                var glyphHeightLocal = metrics.height * (metricsFactor / upem);
                var capWidth = (centerX + sdfPadding) * glyphHeightLocal;
                var totalWidth = quadRightX - quadLeftX;

                const float uvBottom = -sdfPadding;
                const float uvTop = 1f + sdfPadding;
                var uvLeftCap = -sdfPadding;
                var uvRightCap = aspect + sdfPadding;

                if (totalWidth < 2f * capWidth)
                {
                    EmitKashidaQuad(gen, ref seg, font, metricsFactor, quadHeight,
                        quadLeftX, quadRightX, bottomY, topY,
                        uvLeftCap, uvRightCap, uvBottom, uvTop, tileIdx, glyphH, aspect,
                        glyphKey, tatweelIndex, varHash, in entry);
                    return;
                }

                var leftCapEnd = quadLeftX + capWidth;
                var rightCapStart = quadRightX - capWidth;

                EmitKashidaQuad(gen, ref seg, font, metricsFactor, quadHeight,
                    quadLeftX, leftCapEnd, bottomY, topY,
                    uvLeftCap, centerX, uvBottom, uvTop, tileIdx, glyphH, aspect,
                    glyphKey, tatweelIndex, varHash, in entry);

                EmitKashidaQuad(gen, ref seg, font, metricsFactor, quadHeight,
                    leftCapEnd, rightCapStart, bottomY, topY,
                    centerX, centerX, uvBottom, uvTop, tileIdx, glyphH, aspect,
                    glyphKey, tatweelIndex, varHash, in entry);

                EmitKashidaQuad(gen, ref seg, font, metricsFactor, quadHeight,
                    rightCapStart, quadRightX, bottomY, topY,
                    centerX, uvRightCap, uvBottom, uvTop, tileIdx, glyphH, aspect,
                    glyphKey, tatweelIndex, varHash, in entry);
            }

            private static void EmitKashidaQuad(UniTextMeshGenerator gen, ref KashidaSegment seg,
                UniTextFont.Core font, float metricsFactor, float quadHeight,
                float leftX, float rightX, float bottomY, float topY,
                float uvLeft, float uvRight, float uvBottom, float uvTop,
                float tileIdx, float glyphH, float aspect,
                long glyphKey, uint tatweelIndex, long varHash, in GlyphAtlas.GlyphEntry entry)
            {
                gen.EnsureCapacity(4, 6);

                var verts = gen.Vertices;
                var uvData = gen.Uvs0;
                var uv1Data = gen.Uvs1;
                var cols = gen.Colors;

                var vertIdx = gen.vertexCount;

                verts[vertIdx]     = new Vector3(leftX, bottomY, 0);
                verts[vertIdx + 1] = new Vector3(leftX, topY, 0);
                verts[vertIdx + 2] = new Vector3(rightX, topY, 0);
                verts[vertIdx + 3] = new Vector3(rightX, bottomY, 0);

                uvData[vertIdx]     = new Vector4(uvLeft, uvBottom, tileIdx, glyphH);
                uvData[vertIdx + 1] = new Vector4(uvLeft, uvTop, tileIdx, glyphH);
                uvData[vertIdx + 2] = new Vector4(uvRight, uvTop, tileIdx, glyphH);
                uvData[vertIdx + 3] = new Vector4(uvRight, uvBottom, tileIdx, glyphH);

                var uv1wBias = gen.TextUv1wBias;
                uv1Data[vertIdx]     = new Vector4(aspect, 0, seg.cluster, uv1wBias);
                uv1Data[vertIdx + 1] = new Vector4(aspect, 0, seg.cluster, uv1wBias);
                uv1Data[vertIdx + 2] = new Vector4(aspect, 0, seg.cluster, uv1wBias + 1);
                uv1Data[vertIdx + 3] = new Vector4(aspect, 0, seg.cluster, uv1wBias + 1);

                var defaultColor = gen.defaultColor;
                cols[vertIdx]     = defaultColor;
                cols[vertIdx + 1] = defaultColor;
                cols[vertIdx + 2] = defaultColor;
                cols[vertIdx + 3] = defaultColor;

                gen.vertexCount += 4;

                gen.font = font;
                gen.fontMetricFactor = metricsFactor;
                gen.height = quadHeight;
                gen.currentCluster = seg.cluster;
                gen.cursorX = leftX;
                gen.baselineY = seg.baselineY;
                gen.faceBaseIdx = vertIdx;
                gen.ResetPerGlyphState();
                gen.isVirtualGlyph = true;
                gen.InvokeGlyphModifiersAndComplete(-1);
                if (!gen.baseFaceClaimed)
                    gen.AddSdfQuad(gen.claimedFillSequence, vertIdx, gen.claimedFillBlend);

                gen.RequestTierUpgradeIfNeeded(glyphKey, tatweelIndex, in entry,
                    font, varHash, null, glyphH, aspect);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static UnicodeScript GetRunScript(ref ShapedRun run,
                UnicodeScript[] scripts, int cpCount)
            {
                var start = run.range.start;
                return (uint)start < (uint)cpCount ? scripts[start] : UnicodeScript.Common;
            }

            /// <summary>
            /// Determines whether two codepoints form a cursive connection
            /// per the Unicode Arabic Joining Algorithm.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool AreConnected(int clusterA, int clusterB,
                int[] codepoints, int cpCount, UnicodeDataProvider provider)
            {
                var earlier = Math.Min(clusterA, clusterB);
                var later = Math.Max(clusterA, clusterB);

                if ((uint)earlier >= (uint)cpCount || (uint)later >= (uint)cpCount)
                    return false;

                var jtEarlier = provider.GetJoiningType(codepoints[earlier]);
                var jtLater = provider.GetJoiningType(codepoints[later]);

                return jtEarlier.JoinsFollowing() && jtLater.JoinsPreceding();
            }

            /// <summary>
            /// Returns true if the codepoint at the given cluster is lam (U+0644) followed
            /// by an alef variant, forming a mandatory lam-alef ligature with diagonal connection.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool IsLamAlefLigature(int cluster, int[] codepoints, int cpCount)
            {
                if ((uint)cluster >= (uint)cpCount || codepoints[cluster] != UnicodeData.ArabicLam)
                    return false;

                var next = cluster + 1;
                if ((uint)next >= (uint)cpCount)
                    return false;

                var cp = codepoints[next];
                return cp == UnicodeData.ArabicAlef
                    || cp == UnicodeData.ArabicAlefMaddaAbove
                    || cp == UnicodeData.ArabicAlefHamzaAbove
                    || cp == UnicodeData.ArabicAlefHamzaBelow
                    || cp == UnicodeData.ArabicAlefWasla;
            }
        }
    }
}
