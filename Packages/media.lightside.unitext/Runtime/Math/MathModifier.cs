using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Controls the initial OpenType math style used for a formula.</summary>
    public enum MathMode : byte
    {
        /// <summary>Uses compact text-style fractions, operators, and limits.</summary>
        Inline,

        /// <summary>Uses display-style fractions, large operators, and stacked limits.</summary>
        Display
    }

    /// <summary>
    /// Typesets formulas captured by <see cref="MathParseRule"/> with metrics and stretchy glyphs
    /// from a dedicated OpenType MATH font.
    /// </summary>
    [Serializable]
    [TypeGroup("Inline", 6)]
    [TypeDescription("Typesets formulas from MathParseRule with an OpenType MATH font.")]
    public partial class MathModifier : BaseModifier
    {
        private struct FormulaEntry
        {
            public string formula;
            public int cluster;
        }

        private struct FormulaLayout
        {
            public int cluster;
            public MathLayoutResult layout;
        }

        [SerializeField, StateProperty(nameof(MarkTextDirty))]
        private UniTextFont mathFont;

        [SerializeField, StateProperty(nameof(MarkTextDirty))]
        private MathMode mode;

        private PooledList<FormulaEntry> entries;
        private PooledList<FormulaLayout> layouts;
        private MathNodeList nodes;
        private PooledBuffer<MathRulePlacement> rules;
        private float layoutFontSize;
        private UniTextFontProvider providerOwner;
        private UniTextFont.Core providerFont;
        private UniTextMathGlyphProvider glyphProvider;
        private MathFontMetrics cachedMetrics;
        private UniTextFont.Core preparedMathFont;
        private string preparedMathFontName;
        private bool hasPreparedMathFont;

        private Action shapedCallback;
        private OrderedEventHandler<LineHeightContext> lineHeightCallback;
        private Action beforeMeshCallback;
        private Action mainPassCallback;

        protected override void OnEnable()
        {
            entries ??= new PooledList<FormulaEntry>(8);
            entries.FakeClear();
            layouts ??= new PooledList<FormulaLayout>(8);
            layouts.FakeClear();
            nodes.Rent(64);
            rules.Rent(16);

            shapedCallback ??= OnShaped;
            lineHeightCallback ??= OnCalculateLineHeight;
            beforeMeshCallback ??= OnBeforeGenerateMesh;
            mainPassCallback ??= OnMainPassComplete;

            uniText.TextProcessor.Shaped.Subscribe(shapedCallback, -1000);
            uniText.TextProcessor.OnCalculateLineHeight.Subscribe(lineHeightCallback);
            uniText.BeforeGenerateMesh.Subscribe(beforeMeshCallback);
            uniText.MeshGenerator.onMainPassComplete.Subscribe(mainPassCallback);
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            uniText.TextProcessor.OnCalculateLineHeight.Unsubscribe(lineHeightCallback);
            uniText.BeforeGenerateMesh.Unsubscribe(beforeMeshCallback);
            uniText.MeshGenerator.onMainPassComplete.Unsubscribe(mainPassCallback);
            ClearReplacementFlags();
            ReturnLayouts();
            ClearFormulaReferences();
            rules.FakeClear();
            DisposeGlyphProvider();
        }

        protected override void OnDestroy()
        {
            ReturnLayouts();
            ClearFormulaReferences();
            entries?.Return();
            entries = null;
            layouts?.Return();
            layouts = null;
            nodes.Return();
            rules.Return();
            DisposeGlyphProvider();
        }

        protected override void BeforeApply()
        {
            ClearFormulaReferences();
            entries.FakeClear();
        }

        /// <summary>Captures the Unity font asset as a worker-safe runtime before shaping starts.</summary>
        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            var asset = mathFont;
            hasPreparedMathFont = asset != null;
            preparedMathFontName = hasPreparedMathFont ? asset.name : null;
            preparedMathFont = hasPreparedMathFont ? asset.Runtime : null;
        }

        protected override void OnApply(in RangeApplyContext context)
        {
            var formula = context.PrimaryValue
                ?? throw new InvalidOperationException("MathModifier requires formula data from MathParseRule.");
            entries.Add(new FormulaEntry
            {
                formula = formula,
                cluster = context.Segment.Range.start
            });
        }

        private void OnShaped()
        {
            ClearReplacementFlags();
            ReturnLayouts();

            if (entries.Count == 0)
            {
                DisposeGlyphProvider();
                return;
            }

            if (!hasPreparedMathFont)
                throw new InvalidOperationException("MathModifier requires a math font.");

            var runtime = preparedMathFont
                ?? throw new InvalidOperationException($"Math font '{preparedMathFontName}' has no runtime font data.");
            if (runtime.IsColor)
                throw new InvalidOperationException($"Math font '{runtime.Name}' must be an outline font, not a color font.");
            if (runtime.UnitsPerEm <= 0)
                throw new InvalidOperationException($"Math font '{runtime.Name}' has an invalid units-per-em value.");

            var fontProvider = uniText.FontProvider;
            var fontId = UniTextFontProvider.GetFontId(runtime);
            EnsureGlyphProvider(fontProvider, runtime, fontId);

            layoutFontSize = buffers.shapingFontSize;
            if (!(layoutFontSize > 0f) || float.IsNaN(layoutFontSize) || float.IsInfinity(layoutFontSize))
                throw new InvalidOperationException("Math layout requires a finite positive font size.");

            var unitScale = fontProvider.MetricScale(fontId, 1f);
            if (!(unitScale > 0f) || float.IsNaN(unitScale) || float.IsInfinity(unitScale))
                throw new InvalidOperationException($"Math font '{runtime.Name}' has an invalid metric scale.");

            var ctx = new MathLayoutContext
            {
                style = mode == MathMode.Display ? MathStyle.Display : MathStyle.Text,
                fontSize = layoutFontSize,
                fontId = fontId,
                unitsPerEm = runtime.UnitsPerEm,
                unitScale = unitScale,
                metrics = cachedMetrics
            };
            ctx.UpdatePixelScale();

            try
            {
                var hasRules = false;
                for (var i = 0; i < entries.Count; i++)
                {
                    ref var entry = ref entries[i];
                    MathParser.Parse(entry.formula.AsSpan(), ref nodes);
                    var layoutContext = ctx;
                    var engine = new MathLayoutEngine();
                    var result = engine.Layout(ref nodes, ref layoutContext, glyphProvider, entry.formula);
                    hasRules |= RequestLayoutGlyphs(ref result);
                    layouts.Add(new FormulaLayout { cluster = entry.cluster, layout = result });
                }

                if (hasRules)
                    buffers.RequestVirtualCodepoint('_');
                ReplacePlaceholders();
            }
            catch
            {
                ReturnLayouts();
                throw;
            }
        }

        private void ReplacePlaceholders()
        {
            var glyphs = buffers.shapedGlyphs.data;
            var glyphCount = buffers.shapedGlyphs.count;
            var hidden = buffers.PrepareHiddenClusters();

            for (var i = 0; i < layouts.Count; i++)
            {
                ref var entry = ref layouts[i];
                if ((uint)entry.cluster < (uint)hidden.Length)
                    hidden[entry.cluster] |= HiddenClusterBits.Replacement;

                var found = false;
                for (var g = 0; g < glyphCount; g++)
                {
                    if (glyphs[g].cluster != entry.cluster)
                        continue;

                    glyphs[g].glyphId = ShapedGlyph.NoGlyph;
                    glyphs[g].advanceX = found ? 0f : entry.layout.width;
                    glyphs[g].offsetX = 0f;
                    glyphs[g].offsetY = 0f;
                    found = true;
                }

                if (!found)
                    throw new InvalidOperationException($"Math placeholder at cluster {entry.cluster} was not shaped.");
            }

            var runs = buffers.shapedRuns.data;
            for (var r = 0; r < buffers.shapedRuns.count; r++)
            {
                ref var run = ref runs[r];
                var end = run.glyphStart + run.glyphCount;
                var width = 0f;
                for (var g = run.glyphStart; g < end; g++)
                    width += glyphs[g].advanceX;
                run.width = width;
            }
        }

        private bool RequestLayoutGlyphs(ref MathLayoutResult layout)
        {
            var hasRules = false;
            for (var i = 0; i < layout.buffer.Count; i++)
            {
                ref var box = ref layout.buffer[i];
                if (box.type == MathBoxType.Glyph)
                {
                    buffers.RequestVirtualGlyph(box.fontId, (uint)box.glyphId);
                    continue;
                }

                if (box.type == MathBoxType.Rule)
                {
                    hasRules = true;
                    continue;
                }

                if (box.type != MathBoxType.Delimiter)
                    continue;

                if (box.delimiterGlyphId >= 0)
                    buffers.RequestVirtualGlyph(box.delimiterFontId, (uint)box.delimiterGlyphId);

                var partEnd = box.assemblyIndex + box.assemblyPartCount;
                for (var p = box.assemblyIndex; p >= 0 && p < partEnd; p++)
                    buffers.RequestVirtualGlyph(box.delimiterFontId,
                        (uint)layout.buffer.assemblyParts[p].glyphId);
            }

            return hasRules;
        }

        private void OnCalculateLineHeight(ref LineHeightContext context)
        {
            if (layouts.Count == 0 || layoutFontSize <= 0f)
                return;

            uniText.FontProvider.GetLineMetrics(context.fontSize, out var ascent, out var descent, out _);
            var scale = context.fontSize / layoutFontSize;
            var over = 0f;
            var under = 0f;
            var rangeScales = buffers.GetAttributeData<PooledArrayAttribute<float>>(AttributeKeys.Size);

            for (var i = 0; i < layouts.Count; i++)
            {
                ref var entry = ref layouts[i];
                if (entry.cluster < context.startCluster || entry.cluster >= context.endCluster)
                    continue;

                var entryScale = scale * GetRangeScale(rangeScales, entry.cluster);
                var entryOver = entry.layout.height * entryScale - ascent;
                var entryUnder = entry.layout.depth * entryScale - Math.Abs(descent);
                if (entryOver > over) over = entryOver;
                if (entryUnder > under) under = entryUnder;
            }

            if (over > 0f || under > 0f)
                uniText.TextProcessor.ReserveLineSpace(context.lineIndex, Math.Max(0f, over), Math.Max(0f, under));
        }

        private void OnBeforeGenerateMesh()
        {
            rules.FakeClear();
            if (layouts.Count == 0 || layoutFontSize <= 0f)
                return;

            var positioned = buffers.positionedGlyphs;
            var hidden = buffers.hiddenClusters.data;
            var hiddenCount = buffers.hiddenClusters.count;
            var layoutScale = buffers.GetGlyphScale(uniText.CurrentFontSize);
            var rangeScales = buffers.GetAttributeData<PooledArrayAttribute<float>>(AttributeKeys.Size);

            for (var i = 0; i < layouts.Count; i++)
            {
                ref var entry = ref layouts[i];
                if ((uint)entry.cluster < (uint)hiddenCount
                    && (hidden[entry.cluster] & ~HiddenClusterBits.Replacement) != 0)
                    continue;

                for (var g = 0; g < positioned.count; g++)
                {
                    ref var glyph = ref positioned[g];
                    if (glyph.cluster != entry.cluster)
                        continue;

                    entry.layout.CollectRenderData(ref buffers.virtualPositionedGlyphs,
                        ref rules, glyph.left, glyph.y, entry.cluster, layoutFontSize,
                        layoutScale * GetRangeScale(rangeScales, entry.cluster));
                    break;
                }
            }
        }

        private static float GetRangeScale(PooledArrayAttribute<float> attribute, int cluster)
        {
            if (attribute == null || (uint)cluster >= (uint)attribute.buffer.Capacity)
                return 1f;
            var scale = attribute.buffer[cluster];
            return scale > 0f ? scale : 1f;
        }

        private void OnMainPassComplete()
        {
            if (rules.count == 0)
                return;

            var gen = uniText.MeshGenerator;
            var fontProvider = uniText.FontProvider;
            var ruleFontId = fontProvider.FindFontForCodepoint('_');
            var ruleFont = fontProvider.GetFont(ruleFontId);
            if (ruleFont == null || ruleFont.GetGlyphIndexForUnicode('_') == 0)
                throw new InvalidOperationException("No font can render mathematical rule lines.");
            var varHash = buffers.ResolveVarHash48(ruleFontId, ruleFont);

            for (var i = 0; i < rules.count; i++)
            {
                ref var rule = ref rules[i];
                LineRenderHelper.DrawSemanticLine(gen, fontProvider,
                    gen.offsetX + rule.startX, gen.offsetX + rule.endX,
                    gen.offsetY - rule.centerY, 0f, rule.cluster, varHash,
                    rule.thickness);
            }
        }

        private void ReturnLayouts()
        {
            if (layouts == null)
                return;

            for (var i = 0; i < layouts.Count; i++)
                layouts[i].layout.buffer.Return();
            layouts.FakeClear();
        }

        private void ClearReplacementFlags()
        {
            if (layouts == null)
                return;

            var hidden = buffers.hiddenClusters.data;
            var hiddenCount = buffers.hiddenClusters.count;
            for (var i = 0; i < layouts.Count; i++)
            {
                var cluster = layouts[i].cluster;
                if ((uint)cluster < (uint)hiddenCount)
                    hidden[cluster] &= unchecked((byte)~HiddenClusterBits.Replacement);
            }
        }

        private void ClearFormulaReferences()
        {
            if (entries == null)
                return;
            for (var i = 0; i < entries.Count; i++)
                entries[i].formula = null;
        }

        private void EnsureGlyphProvider(UniTextFontProvider fontProvider,
            UniTextFont.Core runtime, int fontId)
        {
            if (ReferenceEquals(providerOwner, fontProvider) && ReferenceEquals(providerFont, runtime))
                return;

            DisposeGlyphProvider();
            fontProvider.RegisterFont(fontId, runtime);
            var replacement = new UniTextMathGlyphProvider(fontProvider, uniText.TextProcessor, fontId);
            try
            {
                cachedMetrics = replacement.LoadMetrics();
                glyphProvider = replacement;
                providerOwner = fontProvider;
                providerFont = runtime;
            }
            catch
            {
                replacement.Dispose();
                throw;
            }
        }

        private void DisposeGlyphProvider()
        {
            glyphProvider?.Dispose();
            glyphProvider = null;
            providerOwner = null;
            providerFont = null;
        }
    }
}
