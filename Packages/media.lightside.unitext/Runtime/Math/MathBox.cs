using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LightSide
{
    /// <summary>
    /// Math typesetting styles following TeX conventions.
    /// Each style determines font size scaling and layout rules for math elements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The eight styles form four pairs (display/text/script/scriptScript), each with a
    /// cramped variant. Cramped styles shift superscripts lower to reduce vertical extent.
    /// </para>
    /// <para>
    /// Style transitions follow TeX rules: superscripts use the next smaller style,
    /// subscripts use the next smaller cramped style, fractions use specific style mappings.
    /// </para>
    /// </remarks>
    internal enum MathStyle : byte
    {
        /// <summary>Standalone formula on its own line (largest).</summary>
        Display = 0,

        /// <summary>Cramped variant of display style (limits superscript height).</summary>
        DisplayCramped = 1,

        /// <summary>Inline formula within text.</summary>
        Text = 2,

        /// <summary>Cramped variant of text style.</summary>
        TextCramped = 3,

        /// <summary>Superscripts and subscripts (scaled down by scriptPercentScaleDown).</summary>
        Script = 4,

        /// <summary>Cramped variant of script style.</summary>
        ScriptCramped = 5,

        /// <summary>Scripts on scripts (scaled down by scriptScriptPercentScaleDown).</summary>
        ScriptScript = 6,

        /// <summary>Cramped variant of scriptScript style.</summary>
        ScriptScriptCramped = 7
    }


    /// <summary>
    /// Identifies the type of content stored in a <see cref="MathBox"/>.
    /// </summary>
    internal enum MathBoxType : byte
    {
        /// <summary>A single glyph (character, operator, symbol).</summary>
        Glyph = 0,

        /// <summary>Horizontal box: children laid out left-to-right.</summary>
        HBox = 1,

        /// <summary>Vertical box: children stacked top-to-bottom (fractions, limits).</summary>
        VBox = 2,

        /// <summary>Fixed horizontal or vertical spacing.</summary>
        Kern = 3,

        /// <summary>Horizontal or vertical line (fraction bar, radical overline).</summary>
        Rule = 4,

        /// <summary>Stretchy delimiter built from size variants or glyph assembly.</summary>
        Delimiter = 5
    }


    /// <summary>
    /// A laid-out math element in the box tree. This is the intermediate representation
    /// between the math AST and final positioned glyphs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MathBox uses <see cref="LayoutKind.Explicit"/> to overlay type-specific fields,
    /// keeping the struct compact (48 bytes) for cache-friendly traversal in flat arrays.
    /// </para>
    /// <para>
    /// The box model follows TeX conventions:
    /// <list type="bullet">
    /// <item><b>width</b> — total horizontal extent</item>
    /// <item><b>height</b> — ascent above the baseline (positive up)</item>
    /// <item><b>depth</b> — descent below the baseline (positive down)</item>
    /// <item><b>shift</b> — vertical displacement from parent baseline</item>
    /// </list>
    /// </para>
    /// <para>
    /// Children of HBox/VBox nodes are stored contiguously in the <see cref="MathBoxBuffer"/>
    /// flat array. The parent references them via <see cref="firstChild"/> and <see cref="childCount"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="MathBoxBuffer"/>
    /// <seealso cref="MathLayoutResult"/>
    [StructLayout(LayoutKind.Explicit, Size = 48)]
    internal struct MathBox
    {
        /// <summary>The type of this box.</summary>
        [FieldOffset(0)]
        public MathBoxType type;

        /// <summary>Total width in pixels.</summary>
        [FieldOffset(4)]
        public float width;

        /// <summary>Ascent above the baseline in pixels (positive up).</summary>
        [FieldOffset(8)]
        public float height;

        /// <summary>Descent below the baseline in pixels (positive down).</summary>
        [FieldOffset(12)]
        public float depth;

        /// <summary>Vertical shift from parent baseline in pixels.</summary>
        [FieldOffset(16)]
        public float shift;

        /// <summary>Italic correction in pixels (extra width for slanted glyphs).</summary>
        [FieldOffset(20)]
        public float italic;

        /// <summary>[Glyph] Glyph index in the font.</summary>
        [FieldOffset(24)]
        public int glyphId;

        /// <summary>[Glyph] Font identifier for this glyph.</summary>
        [FieldOffset(28)]
        public int fontId;

        /// <summary>[Glyph] Effective font size at this nesting level in pixels.</summary>
        [FieldOffset(32)]
        public float fontSize;

        /// <summary>[Glyph] Horizontal HarfBuzz positioning offset in pixels.</summary>
        [FieldOffset(36)]
        public float glyphOffsetX;

        /// <summary>[Glyph] Vertical HarfBuzz positioning offset in pixels, positive upward.</summary>
        [FieldOffset(40)]
        public float glyphOffsetY;

        /// <summary>[HBox/VBox] Index of the first child in the flat array.</summary>
        [FieldOffset(24)]
        public int firstChild;

        /// <summary>[HBox/VBox] Number of children.</summary>
        [FieldOffset(28)]
        public int childCount;

        /// <summary>[Rule] Line thickness in pixels.</summary>
        [FieldOffset(24)]
        public float ruleThickness;

        /// <summary>[Delimiter] Glyph index of the chosen size variant, or -1 if using assembly.</summary>
        [FieldOffset(24)]
        public int delimiterGlyphId;

        /// <summary>[Delimiter] Font identifier for the delimiter.</summary>
        [FieldOffset(28)]
        public int delimiterFontId;

        /// <summary>[Delimiter] Effective font size for the delimiter.</summary>
        [FieldOffset(32)]
        public float delimiterFontSize;

        /// <summary>[Delimiter] Index into external assembly parts buffer, or -1 if using a single variant.</summary>
        [FieldOffset(36)]
        public int assemblyIndex;

        /// <summary>[Delimiter] Number of assembly parts.</summary>
        [FieldOffset(40)]
        public int assemblyPartCount;

        /// <summary>[Delimiter] True if assembly parts are laid out horizontally (LTR), false for vertical (BTT).</summary>
        [FieldOffset(44)]
        public byte assemblyIsHorizontal;

        /// <summary>[Delimiter variant] Glyph-baseline displacement used to center vertical ink on the math axis.</summary>
        [FieldOffset(44)]
        public float delimiterBaselineOffset;


        /// <summary>
        /// Creates a glyph box.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateGlyph(int glyphId, int fontId, float fontSize,
            float width, float height, float depth, float italic,
            float offsetX = 0f, float offsetY = 0f)
        {
            var box = new MathBox();
            box.type = MathBoxType.Glyph;
            box.glyphId = glyphId;
            box.fontId = fontId;
            box.fontSize = fontSize;
            box.width = width;
            box.height = height;
            box.depth = depth;
            box.italic = italic;
            box.glyphOffsetX = offsetX;
            box.glyphOffsetY = offsetY;
            return box;
        }

        /// <summary>
        /// Creates a horizontal box. Children must be added contiguously to the buffer,
        /// then <see cref="firstChild"/> and <see cref="childCount"/> set accordingly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateHBox()
        {
            var box = new MathBox();
            box.type = MathBoxType.HBox;
            return box;
        }

        /// <summary>
        /// Creates a vertical box. Children must be added contiguously to the buffer,
        /// then <see cref="firstChild"/> and <see cref="childCount"/> set accordingly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateVBox()
        {
            var box = new MathBox();
            box.type = MathBoxType.VBox;
            return box;
        }

        /// <summary>
        /// Creates a kern (spacing) box.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateKern(float amount)
        {
            var box = new MathBox();
            box.type = MathBoxType.Kern;
            box.width = amount;
            return box;
        }

        /// <summary>
        /// Creates a rule (line) box.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateRule(float width, float height, float depth, float thickness)
        {
            var box = new MathBox();
            box.type = MathBoxType.Rule;
            box.width = width;
            box.height = height;
            box.depth = depth;
            box.ruleThickness = thickness;
            return box;
        }

        /// <summary>
        /// Creates a delimiter box using a pre-made size variant glyph.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateDelimiterVariant(int glyphId, int fontId, float fontSize,
            float width, float height, float depth, float italic)
        {
            var box = new MathBox();
            box.type = MathBoxType.Delimiter;
            box.delimiterGlyphId = glyphId;
            box.delimiterFontId = fontId;
            box.delimiterFontSize = fontSize;
            box.width = width;
            box.height = height;
            box.depth = depth;
            box.italic = italic;
            box.assemblyIndex = -1;
            return box;
        }

        /// <summary>
        /// Creates a delimiter box assembled from multiple glyph parts.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MathBox CreateDelimiterAssembly(int fontId, float fontSize,
            float width, float height, float depth,
            int assemblyIndex, int assemblyPartCount, bool isHorizontal = false)
        {
            var box = new MathBox();
            box.type = MathBoxType.Delimiter;
            box.delimiterGlyphId = -1;
            box.delimiterFontId = fontId;
            box.delimiterFontSize = fontSize;
            box.width = width;
            box.height = height;
            box.depth = depth;
            box.assemblyIndex = assemblyIndex;
            box.assemblyPartCount = assemblyPartCount;
            box.assemblyIsHorizontal = isHorizontal ? (byte)1 : (byte)0;
            return box;
        }
    }


    /// <summary>
    /// A part of a glyph assembly used to construct stretchy delimiters.
    /// </summary>
    /// <remarks>
    /// Corresponds to the GlyphPart record in the OpenType MATH table's GlyphAssembly subtable.
    /// Parts are concatenated in the direction of extension (left-to-right or bottom-to-top).
    /// </remarks>
    internal struct MathAssemblyPart
    {
        /// <summary>Glyph index of this part.</summary>
        public int glyphId;

        /// <summary>Advance offset of this part from the assembly origin, in pixels.</summary>
        public float offset;

        public float width;
        public float height;
        public float depth;
    }


    /// <summary>
    /// Flat array storage for the <see cref="MathBox"/> tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The box tree is stored as a flat array for cache-friendly traversal. Children of any
    /// HBox/VBox are stored contiguously so that the parent can reference them by
    /// (firstChild, childCount).
    /// </para>
    /// <para>
    /// <b>Building pattern</b>: To build a compound box (e.g., a fraction), first add all
    /// children, note their indices, then add the parent with firstChild/childCount pointing
    /// to the children region. Because children must be added before the parent, the tree
    /// is built bottom-up. The root is always the last box added.
    /// </para>
    /// <para>
    /// Uses <see cref="PooledBuffer{T}"/> for zero-allocation steady-state operation.
    /// </para>
    /// </remarks>
    /// <seealso cref="MathBox"/>
    /// <seealso cref="MathLayoutResult"/>
    internal struct MathBoxBuffer
    {
        /// <summary>The underlying flat storage of boxes.</summary>
        public PooledBuffer<MathBox> boxes;

        /// <summary>Assembly parts for delimiter boxes that use glyph assembly.</summary>
        public PooledBuffer<MathAssemblyPart> assemblyParts;

        /// <summary>Gets the number of boxes currently stored.</summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => boxes.count;
        }

        /// <summary>Gets a reference to the box at the specified index.</summary>
        /// <param name="i">The zero-based index.</param>
        /// <returns>A reference to the box.</returns>
        public ref MathBox this[int i]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref boxes[i];
        }

        /// <summary>
        /// Rents pooled arrays with the specified initial capacity.
        /// </summary>
        /// <param name="boxCapacity">Initial capacity for boxes.</param>
        /// <param name="assemblyCapacity">Initial capacity for assembly parts.</param>
        public void Rent(int boxCapacity, int assemblyCapacity = 16)
        {
            boxes.Rent(boxCapacity);
            assemblyParts.Rent(assemblyCapacity);
        }

        /// <summary>
        /// Returns all pooled arrays.
        /// </summary>
        public void Return()
        {
            boxes.Return();
            assemblyParts.Return();
        }

        /// <summary>
        /// Adds a box to the buffer and returns its index.
        /// </summary>
        /// <param name="box">The box to add.</param>
        /// <returns>The index of the added box.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Add(MathBox box)
        {
            var index = boxes.count;
            boxes.Add(box);
            return index;
        }

        /// <summary>
        /// Adds an assembly part and returns its index.
        /// </summary>
        /// <param name="part">The assembly part to add.</param>
        /// <returns>The index of the added part.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int AddAssemblyPart(MathAssemblyPart part)
        {
            var index = assemblyParts.count;
            assemblyParts.Add(part);
            return index;
        }

    }


    /// <summary>
    /// The complete result of math formula layout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Contains the box tree produced by the math layout engine. The root box is at
    /// <see cref="rootIndex"/> in the <see cref="buffer"/>. To render the formula,
    /// call <see cref="CollectRenderData"/> to flatten the tree into positioned glyphs
    /// for mesh generation.
    /// </para>
    /// <para>
    /// This struct does not own the buffer memory. Call <see cref="MathBoxBuffer.Return"/>
    /// separately when done.
    /// </para>
    /// </remarks>
    /// <seealso cref="MathBoxBuffer"/>
    /// <seealso cref="MathBox"/>
    internal struct MathLayoutResult
    {
        /// <summary>The box tree.</summary>
        public MathBoxBuffer buffer;

        /// <summary>Index of the root box in the buffer.</summary>
        public int rootIndex;

        /// <summary>Total formula width in pixels.</summary>
        public float width;

        /// <summary>Total formula height (ascent above baseline) in pixels.</summary>
        public float height;

        /// <summary>Total formula depth (descent below baseline) in pixels.</summary>
        public float depth;

        /// <summary>
        /// Flattens the box tree into positioned glyphs for mesh generation.
        /// Performs a depth-first traversal, accumulating position offsets.
        /// </summary>
        /// <param name="glyphs">Buffer to append positioned glyphs to.</param>
        /// <param name="rules">Buffer to append fraction bars and radical overbars to.</param>
        /// <param name="originX">X origin of the formula.</param>
        /// <param name="originY">Y origin of the formula (baseline).</param>
        /// <param name="positionScale">Layout pixels → final render pixels, carrying both the
        /// component's glyph scale and the range's size scale. Every length in the tree — offsets,
        /// extents and rule thickness alike — passes through this one factor, so the formula stays
        /// similar to itself at any size.</param>
        public void CollectRenderData(ref PooledBuffer<PositionedGlyph> glyphs,
            ref PooledBuffer<MathRulePlacement> rules, float originX, float originY,
            int cluster, float baseFontSize, float positionScale)
        {
            if (buffer.Count == 0) return;
            CollectRecursive(ref glyphs, ref rules, rootIndex, 0f, 0f,
                originX, originY, cluster, baseFontSize, positionScale);
        }

        private void CollectRecursive(ref PooledBuffer<PositionedGlyph> output,
            ref PooledBuffer<MathRulePlacement> rules, int boxIndex, float x, float y,
            float originX, float originY, int cluster, float baseFontSize,
            float positionScale)
        {
            ref var box = ref buffer[boxIndex];

            switch (box.type)
            {
                case MathBoxType.Glyph:
                    output.Add(new PositionedGlyph
                    {
                        glyphId = box.glyphId,
                        fontId = box.fontId,
                        cluster = cluster,
                        scale = box.fontSize / baseFontSize,
                        x = originX + (x + box.glyphOffsetX) * positionScale,
                        y = originY - (y - box.shift + box.glyphOffsetY) * positionScale,
                        shapedGlyphIndex = -1,
                        left = originX + x * positionScale,
                        top = originY - (y - box.shift + box.height) * positionScale,
                        right = originX + (x + box.width) * positionScale,
                        bottom = originY - (y - box.shift - box.depth) * positionScale
                    });
                    break;

                case MathBoxType.HBox:
                {
                    var childX = x;
                    var childY = y - box.shift;
                    var end = box.firstChild + box.childCount;
                    for (var i = box.firstChild; i < end; i++)
                    {
                        ref var child = ref buffer[i];
                        CollectRecursive(ref output, ref rules, i, childX, childY,
                            originX, originY, cluster, baseFontSize, positionScale);
                        childX += child.width;
                    }
                    break;
                }

                case MathBoxType.VBox:
                {
                    var childY = y - box.shift + box.height;
                    var end = box.firstChild + box.childCount;
                    for (var i = box.firstChild; i < end; i++)
                    {
                        ref var child = ref buffer[i];

                        if (child.type == MathBoxType.Kern)
                        {
                            childY -= child.width;
                            continue;
                        }

                        childY -= child.height;
                        CollectRecursive(ref output, ref rules, i, x, childY,
                            originX, originY, cluster, baseFontSize, positionScale);
                        childY -= child.depth;
                    }
                    break;
                }

                case MathBoxType.Delimiter:
                {
                    if (box.delimiterGlyphId >= 0)
                    {
                        var glyphBaselineY = y - box.shift + box.delimiterBaselineOffset;
                        output.Add(new PositionedGlyph
                        {
                            glyphId = box.delimiterGlyphId,
                            fontId = box.delimiterFontId,
                            cluster = cluster,
                            scale = box.delimiterFontSize / baseFontSize,
                            x = originX + x * positionScale,
                            y = originY - glyphBaselineY * positionScale,
                            shapedGlyphIndex = -1,
                            left = originX + x * positionScale,
                            top = originY - (y - box.shift + box.height) * positionScale,
                            right = originX + (x + box.width) * positionScale,
                            bottom = originY - (y - box.shift - box.depth) * positionScale
                        });
                    }
                    else if (box.assemblyPartCount > 0)
                    {
                        var baseX = x;
                        var baseY = y - box.shift;
                        var isHoriz = box.assemblyIsHorizontal != 0;
                        var end = box.assemblyIndex + box.assemblyPartCount;
                        for (var i = box.assemblyIndex; i < end; i++)
                        {
                            ref var part = ref buffer.assemblyParts[i];
                            var partX = isHoriz
                                ? baseX + part.offset
                                : baseX;
                            var partBaselineY = isHoriz
                                ? baseY
                                : baseY - box.depth + part.offset + part.depth;
                            output.Add(new PositionedGlyph
                            {
                                glyphId = part.glyphId,
                                fontId = box.delimiterFontId,
                                cluster = cluster,
                                scale = box.delimiterFontSize / baseFontSize,
                                x = originX + partX * positionScale,
                                y = originY - partBaselineY * positionScale,
                                shapedGlyphIndex = -1,
                                left = originX + partX * positionScale,
                                top = originY - (partBaselineY + part.height) * positionScale,
                                right = originX + (partX + part.width) * positionScale,
                                bottom = originY - (partBaselineY - part.depth) * positionScale
                            });
                        }
                    }
                    break;
                }

                case MathBoxType.Kern:
                    break;

                case MathBoxType.Rule:
                {
                    var ruleStartX = originX + x * positionScale;
                    rules.Add(new MathRulePlacement
                    {
                        startX = ruleStartX,
                        endX = ruleStartX + box.width * positionScale,
                        centerY = originY - (y - box.shift
                            + (box.height - box.depth) * 0.5f) * positionScale,
                        thickness = box.ruleThickness * positionScale,
                        cluster = cluster
                    });
                    break;
                }
            }
        }
    }

    /// <summary>
    /// A fraction bar or radical overbar, fully specified in final render space: origin, extent and
    /// thickness all carry the layout scale and the range's size scale, so the emitted quads take no
    /// further per-glyph scaling.
    /// </summary>
    internal struct MathRulePlacement
    {
        public float startX;
        public float endX;
        public float centerY;
        public float thickness;
        public int cluster;
    }


    /// <summary>
    /// Context passed during recursive math layout. Contains the current style,
    /// font size, font ID, and cached font metrics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Passed by <see langword="ref"/> to layout methods to avoid copying. Modified during
    /// recursive descent (e.g., style changes for superscripts) and restored on return.
    /// </para>
    /// <para>
    /// Not a <see langword="ref struct"/> because layout methods may need to store a snapshot
    /// of the context (e.g., when processing numerator and denominator with different styles).
    /// </para>
    /// </remarks>
    /// <seealso cref="MathFontMetrics"/>
    /// <seealso cref="MathStyle"/>
    internal struct MathLayoutContext
    {
        /// <summary>Current math style (display, text, script, scriptScript, and cramped variants).</summary>
        public MathStyle style;

        /// <summary>Current font size in pixels.</summary>
        public float fontSize;

        /// <summary>Current math font identifier.</summary>
        public int fontId;

        /// <summary>Font design units per em for the current math font.</summary>
        public int unitsPerEm;

        public float unitScale;

        /// <summary>
        /// Cached conversion from the font's HarfBuzz coordinate scale to pixels.
        /// Updated whenever <see cref="fontSize"/> changes.
        /// </summary>
        public float pixelScale;

        public float EmSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => unitsPerEm * pixelScale;
        }

        /// <summary>Cached MATH table constants for the current font.</summary>
        public MathFontMetrics metrics;

        /// <summary>
        /// Updates <see cref="pixelScale"/> from the current font size and metric scale.
        /// Must be called after setting either value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void UpdatePixelScale()
        {
            pixelScale = fontSize * unitScale;
        }

        /// <summary>
        /// Converts font design units to pixels at the current font size.
        /// Uses cached <see cref="pixelScale"/> (single multiply, no division).
        /// </summary>
        /// <param name="designUnits">Value in font design units.</param>
        /// <returns>Value in pixels.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToPixels(int designUnits)
        {
            return designUnits * pixelScale;
        }

        /// <summary>
        /// Sets <see cref="fontSize"/> for the current style level, applying script scale-down
        /// factors, and updates <see cref="pixelScale"/> accordingly.
        /// </summary>
        /// <param name="baseFontSize">The original (display/text) font size.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyScaledFontSize(float baseFontSize)
        {
            var level = MathStyleUtil.SizeLevel(style);
            fontSize = level == 0
                ? baseFontSize
                : level == 1
                    ? baseFontSize * metrics.scriptPercentScaleDown / 100f
                    : baseFontSize * metrics.scriptScriptPercentScaleDown / 100f;
            UpdatePixelScale();
        }

        /// <summary>Returns the superscript style for the current style.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathStyle SuperscriptStyle() => MathStyleUtil.Superscript(style);

        /// <summary>Returns the subscript style for the current style.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathStyle SubscriptStyle() => MathStyleUtil.Subscript(style);

        /// <summary>Returns the numerator style for fractions.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathStyle NumeratorStyle() => MathStyleUtil.Numerator(style);

        /// <summary>Returns the denominator style for fractions (always cramped).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathStyle DenominatorStyle() => MathStyleUtil.Denominator(style);

        /// <summary>Returns the cramped variant of the current style.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MathStyle CrampedStyle() => MathStyleUtil.Cramped(style);

        /// <summary>Whether the current style is a display style (Display or DisplayCramped).</summary>
        public bool IsDisplay
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MathStyleUtil.IsDisplay(style);
        }

        /// <summary>Whether the current style is cramped.</summary>
        public bool IsCramped
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MathStyleUtil.IsCramped(style);
        }
    }


    /// <summary>
    /// Cached constants from the OpenType MATH table's MathConstants subtable.
    /// All values are in font design units. Convert to pixels using
    /// <see cref="MathLayoutContext.ToPixels(int)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pre-fetched at formula layout time to avoid repeated table lookups during recursive layout.
    /// Contains only constants consumed by the implemented layout primitives.
    /// </para>
    /// <para>
    /// Naming conventions from the spec:
    /// <list type="bullet">
    /// <item><b>Height</b> — distance from the main baseline</item>
    /// <item><b>Kern</b> — fixed empty space to introduce</item>
    /// <item><b>Gap</b> — empty space that may increase to meet criteria</item>
    /// <item><b>Drop/Rise</b> — relative positioning adjustment between two elements</item>
    /// <item><b>Shift</b> — vertical adjustment from a baseline position</item>
    /// <item><b>Dist</b> — distance between baselines</item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <seealso cref="MathLayoutContext"/>
    internal struct MathFontMetrics
    {
        /// <summary>Percentage of scaling down for level 1 (script) superscripts and subscripts. Typical: 80.</summary>
        public int scriptPercentScaleDown;

        /// <summary>Percentage of scaling down for level 2 (scriptScript) superscripts and subscripts. Typical: 60.</summary>
        public int scriptScriptPercentScaleDown;

        /// <summary>Minimum height of n-ary operators (integral, summation) in display mode, in design units.</summary>
        public int displayOperatorMinHeight;

        /// <summary>Height of the math axis (horizontal reference line for centering), in design units.</summary>
        public int axisHeight;

        /// <summary>Maximum accent base height that does not require raising accents, in design units.</summary>
        public int accentBaseHeight;

        /// <summary>Base height above which flattened accent forms are preferred, in design units.</summary>
        public int flattenedAccentBaseHeight;

        /// <summary>Standard shift down for subscripts (positive = downward), in design units.</summary>
        public int subscriptShiftDown;

        /// <summary>Maximum allowed top of subscript ink without further shift, in design units.</summary>
        public int subscriptTopMax;

        /// <summary>Minimum drop of subscript baseline below base bottom, in design units.</summary>
        public int subscriptBaselineDropMin;

        /// <summary>Standard shift up for superscripts, in design units.</summary>
        public int superscriptShiftUp;

        /// <summary>Shift up for superscripts in cramped styles, in design units.</summary>
        public int superscriptShiftUpCramped;

        /// <summary>Minimum allowed bottom of superscript ink, in design units.</summary>
        public int superscriptBottomMin;

        /// <summary>Maximum drop of superscript baseline below base top, in design units.</summary>
        public int superscriptBaselineDropMax;

        /// <summary>Minimum gap between superscript and subscript ink, in design units.</summary>
        public int subSuperscriptGapMin;

        /// <summary>Maximum level to push superscript bottom before moving subscript down, in design units.</summary>
        public int superscriptBottomMaxWithSubscript;

        /// <summary>Extra space after each sub/superscript, in design units.</summary>
        public int spaceAfterScript;

        /// <summary>Minimum gap between upper limit bottom ink and base operator top ink, in design units.</summary>
        public int upperLimitGapMin;

        /// <summary>Minimum distance between upper limit baseline and base operator top ink, in design units.</summary>
        public int upperLimitBaselineRiseMin;

        /// <summary>Minimum gap between lower limit top ink and base operator bottom ink, in design units.</summary>
        public int lowerLimitGapMin;

        /// <summary>Minimum distance between lower limit baseline and base operator bottom ink, in design units.</summary>
        public int lowerLimitBaselineDropMin;

        /// <summary>Shift up for top element of a stack, in design units.</summary>
        public int stackTopShiftUp;

        /// <summary>Shift up for top element of a stack in display style, in design units.</summary>
        public int stackTopDisplayStyleShiftUp;

        /// <summary>Shift down for bottom element of a stack (positive = downward), in design units.</summary>
        public int stackBottomShiftDown;

        /// <summary>Shift down for bottom element of a stack in display style, in design units.</summary>
        public int stackBottomDisplayStyleShiftDown;

        /// <summary>Minimum gap between stack top bottom ink and stack bottom top ink, in design units.</summary>
        public int stackGapMin;

        /// <summary>Minimum gap between stack elements in display style, in design units.</summary>
        public int stackDisplayStyleGapMin;

        /// <summary>Shift up for fraction numerator, in design units.</summary>
        public int fractionNumeratorShiftUp;

        /// <summary>Shift up for fraction numerator in display style, in design units.</summary>
        public int fractionNumeratorDisplayStyleShiftUp;

        /// <summary>Shift down for fraction denominator (positive = downward), in design units.</summary>
        public int fractionDenominatorShiftDown;

        /// <summary>Shift down for fraction denominator in display style, in design units.</summary>
        public int fractionDenominatorDisplayStyleShiftDown;

        /// <summary>Minimum gap between numerator bottom ink and fraction bar, in design units.</summary>
        public int fractionNumeratorGapMin;

        /// <summary>Minimum gap between numerator bottom ink and fraction bar in display style, in design units.</summary>
        public int fractionNumDisplayStyleGapMin;

        /// <summary>Thickness of the fraction bar, in design units.</summary>
        public int fractionRuleThickness;

        /// <summary>Minimum gap between denominator top ink and fraction bar, in design units.</summary>
        public int fractionDenominatorGapMin;

        /// <summary>Minimum gap between denominator top ink and fraction bar in display style, in design units.</summary>
        public int fractionDenomDisplayStyleGapMin;

        /// <summary>Gap between a base and an overbar, in design units.</summary>
        public int overbarVerticalGap;

        /// <summary>Thickness of an overbar, in design units.</summary>
        public int overbarRuleThickness;

        /// <summary>Extra space above an overbar, in design units.</summary>
        public int overbarExtraAscender;

        /// <summary>Gap between a base and an underbar, in design units.</summary>
        public int underbarVerticalGap;

        /// <summary>Thickness of an underbar, in design units.</summary>
        public int underbarRuleThickness;

        /// <summary>Extra space below an underbar, in design units.</summary>
        public int underbarExtraDescender;

        /// <summary>Gap between expression top ink and radical overbar, in design units.</summary>
        public int radicalVerticalGap;

        /// <summary>Gap between expression top ink and radical overbar in display style, in design units.</summary>
        public int radicalDisplayStyleVerticalGap;

        /// <summary>Thickness of radical rule, in design units.</summary>
        public int radicalRuleThickness;

        /// <summary>Extra space above radical, in design units.</summary>
        public int radicalExtraAscender;

        /// <summary>Extra horizontal kern before radical degree, in design units.</summary>
        public int radicalKernBeforeDegree;

        /// <summary>Negative kern after radical degree, in design units.</summary>
        public int radicalKernAfterDegree;

        /// <summary>Height of radical degree bottom as percentage of radical sign height. Typical: 60.</summary>
        public int radicalDegreeBottomRaisePercent;

    }
}
