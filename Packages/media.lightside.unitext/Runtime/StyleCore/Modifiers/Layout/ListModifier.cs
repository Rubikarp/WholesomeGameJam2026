using System;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Contains information about a single list item for rendering.
    /// </summary>
    public struct ListItemInfo
    {
        public int start;
        public int end;
        public int nestingLevel;
        public int displayNumber;
    }

    /// <summary>
    /// Specifies the numbering style for ordered list markers.
    /// </summary>
    public enum OrderedMarkerStyle
    {
        /// <summary>Decimal numbers (1, 2, 3...)</summary>
        Decimal,
        /// <summary>Lowercase letters (a, b, c...)</summary>
        LowerAlpha,
        /// <summary>Uppercase letters (A, B, C...)</summary>
        UpperAlpha,
        /// <summary>Lowercase Roman numerals (i, ii, iii...)</summary>
        LowerRoman,
        /// <summary>Uppercase Roman numerals (I, II, III...)</summary>
        UpperRoman
    }

    /// <summary>
    /// Controls how the list marker is placed relative to the content.
    /// </summary>
    public enum ListMarkerPlacement : byte
    {
        /// <summary>
        /// Marker takes inline space at the line start; content begins immediately past the
        /// marker, so items with different marker widths (e.g. <c>1.</c> and <c>10.</c>) start
        /// at different positions. Default — matches Google Docs, MS Word, and Discord visual
        /// behavior. Equivalent to CSS <c>list-style-position: inside</c>. Direction-aware:
        /// the marker sits on the leading side of the line for both LTR and RTL paragraphs.
        /// </summary>
        Inside,
        /// <summary>
        /// Marker is aligned against a fixed content column — its trailing edge meets the
        /// content start, so all items have content at the same position regardless of marker
        /// width. Markers wider than the reserved column hang past the line start into the
        /// leading margin (left in LTR paragraphs, right in RTL). Equivalent to CSS
        /// <c>list-style-position: outside</c>. Use when a strictly aligned content column is
        /// required and the indent is wide enough to fit the widest marker.
        /// </summary>
        Outside
    }

    /// <summary>
    /// Renders list markers (bullets or numbers) for text items with automatic indentation.
    /// </summary>
    /// <remarks>
    /// Parameter: optional <c>level:number</c> for numbered lists. Without parameter, renders a bullet.
    ///
    /// Features:
    /// - Supports nested lists with configurable indentation per level
    /// - Bullet markers are customizable per nesting level
    /// - Ordered lists support decimal, alphabetic, and Roman numeral styles
    /// - Automatically handles RTL text direction
    /// </remarks>
    /// <seealso cref="MarkdownListParseRule"/>
    /// <seealso cref="OrderedMarkerStyle"/>
    [Serializable]
    [TypeGroup("Layout", 4)]
    [TypeDescription("Formats text as a bulleted or numbered list.")]
    [GenerateParameters]
    public partial class ListModifier : BaseModifier
    {
        /// <summary>Nesting level default. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private int level;
        /// <summary>Display number for ordered items; -1 renders a bullet. A per-range value overrides it; this is the default for a bare tag or whole-text use.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkTextDirty))] private int number = -1;

        private PooledList<ListItemInfo> items;
        private PooledBuffer<float> markerWidths;
        private UniTextFontProvider fontProvider;

        [ThreadStatic] private static StringBuilder sharedBuilder;

        /// <summary>Places markers inline or against a fixed content column. Changing it reflows the list.</summary>
        [SerializeField, StateProperty(nameof(MarkTextDirty))] private ListMarkerPlacement markerPlacement = ListMarkerPlacement.Inside;
        /// <summary>Bullet-marker glyphs by nesting level.</summary>
        [SerializeField, StateList(nameof(ApplyBulletMarkersChange), Validator = nameof(ValidateBulletMarkersMutation))]
        private StyledList<string> bulletMarkers = new("•", "-", "·");
        /// <summary>Numbering styles by nesting level.</summary>
        [SerializeField, StateList(nameof(ApplyOrderedStylesChange), Validator = nameof(ValidateOrderedStylesMutation))]
        private StyledList<OrderedMarkerStyle> orderedStyles = new(
            OrderedMarkerStyle.Decimal, OrderedMarkerStyle.LowerAlpha, OrderedMarkerStyle.LowerRoman);

        private void ValidateBulletMarkersMutation(in StateListMutation<string> mutation)
        {
            if (mutation.Count == 0)
                throw new InvalidOperationException("A list modifier requires at least one bullet marker.");
        }

        private void ValidateOrderedStylesMutation(in StateListMutation<OrderedMarkerStyle> mutation)
        {
            if (mutation.Count == 0)
                throw new InvalidOperationException("A list modifier requires at least one ordered marker style.");
        }

        private void ApplyBulletMarkersChange()
            => MarkTextDirty();

        private void ApplyOrderedStylesChange()
            => MarkTextDirty();

        private Action shapedCallback;
        private Action injectMarkersCallback;

        protected override void OnEnable()
        {
            items ??= new PooledList<ListItemInfo>(32);
            items.FakeClear();
            markerWidths.Rent(32);
            fontProvider = uniText.FontProvider;
            sharedBuilder ??= new StringBuilder(32);

            shapedCallback ??= OnShaped;
            uniText.TextProcessor.Shaped.Subscribe(shapedCallback);
            injectMarkersCallback ??= InjectMarkerGlyphs;
            uniText.BeforeGenerateMesh.Subscribe(injectMarkersCallback);
        }

        protected override void OnDisable()
        {
            uniText.TextProcessor.Shaped.Unsubscribe(shapedCallback);
            uniText.BeforeGenerateMesh.Unsubscribe(injectMarkersCallback);
        }

        protected override void OnDestroy()
        {
            items?.Return();
            items = null;
            markerWidths.Return();
            fontProvider = null;
        }

        protected override void BeforeApply() => items?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            items.Add(new ListItemInfo
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                nestingLevel = Param.Level.Resolve(this, in context),
                displayNumber = Param.Number.Resolve(this, in context),
            });
        }

        private const int MaxTrackedNestingLevels = 16;
        private static readonly Comparison<ListItemInfo> CompareByStart = (a, b) => a.start.CompareTo(b.start);

        private void OnShaped()
        {
            if (items.Count == 0) return;

            items.Sort(CompareByStart);

            markerWidths.EnsureCapacity(items.Count);
            var widths = markerWidths.data;

            var maxMarkerWidth = 0f;
            for (var i = 0; i < items.Count; i++)
            {
                var w = MeasureMarkerWidthForLayout(items[i]);
                widths[i] = w;
                if (w > maxMarkerWidth) maxMarkerWidth = w;
            }

            if (markerPlacement == ListMarkerPlacement.Outside)
            {
                for (var i = 0; i < items.Count; i++)
                    ApplyMargins(items[i], (items[i].nestingLevel + 1) * maxMarkerWidth);
                return;
            }

            Span<float> ancestorWidths = stackalloc float[MaxTrackedNestingLevels];
            ancestorWidths.Clear();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var level = Math.Clamp(item.nestingLevel, 0, MaxTrackedNestingLevels - 1);

                var ancestorSum = 0f;
                for (var k = 0; k < level; k++) ancestorSum += ancestorWidths[k];
                ApplyMargins(item, ancestorSum + widths[i]);

                ancestorWidths[level] = widths[i];
                for (var k = level + 1; k < MaxTrackedNestingLevels; k++) ancestorWidths[k] = 0;
            }
        }

        private float MeasureMarkerWidthForLayout(ListItemInfo item)
        {
            var buf = buffers;
            var fontSize = buf.shapingFontSize > 0 ? buf.shapingFontSize : fontProvider.FontSize;

            sharedBuilder ??= new StringBuilder(32);
            sharedBuilder.Clear();
            BuildMarkerWithSpace(item, false, sharedBuilder);

            var totalWidth = 0f;
            var len = sharedBuilder.Length;
            for (var i = 0; i < len; i++)
            {
                uint codepoint = sharedBuilder[i];
                buffers.RequestVirtualCodepoint(codepoint);
                if (buffers.TryResolveInjectedGlyph(fontProvider, codepoint, item.start, fontSize, out var marker))
                    totalWidth += marker.Advance;
            }

            return totalWidth;
        }

        private void ApplyMargins(ListItemInfo item, float contentIndent)
        {
            var buf = buffers;
            buf.PrepareStartMargins();
            var margins = buf.startMargins.data;
            if (margins == null) return;

            var safeEnd = Math.Min(item.end, buf.codepoints.count);
            for (var i = item.start; i < safeEnd; i++)
                if (contentIndent > margins[i])
                    margins[i] = contentIndent;
        }

        private void InjectMarkerGlyphs()
        {
            if (items == null || items.Count == 0) return;

            for (var i = 0; i < items.Count; i++)
                InjectMarkerForItem(items[i]);
        }

        private void InjectMarkerForItem(ListItemInfo item)
        {
            var isRtl = IsItemRtl(item.start);
            var buf = buffers;

            var lineStart = item.start;
            var lineEnd = item.end;
            for (var i = 0; i < buf.lines.count; i++)
            {
                ref readonly var line = ref buf.lines[i];
                var lStart = line.range.start;
                var lEnd = lStart + line.range.length;
                if (item.start < lStart || item.start >= lEnd) continue;
                lineStart = lStart;
                lineEnd = Math.Min(lEnd, item.end);
                break;
            }

            var leftmostX = float.PositiveInfinity;
            var rightmostX = float.NegativeInfinity;
            float baselineY = 0;
            var found = false;
            var hiddenFlags = buf.hiddenClusters.data;
            var hiddenCount = buf.hiddenClusters.count;
            for (var i = 0; i < buf.positionedGlyphs.count; i++)
            {
                ref readonly var g = ref buf.positionedGlyphs[i];
                if (g.cluster < lineStart || g.cluster >= lineEnd) continue;
                if ((uint)g.cluster < (uint)hiddenCount && hiddenFlags[g.cluster] != 0) continue;

                if (g.left < leftmostX) leftmostX = g.left;
                if (g.right > rightmostX) rightmostX = g.right;
                if (!found) { baselineY = g.y; found = true; }
            }
            if (!found) return;

            sharedBuilder ??= new StringBuilder(32);
            sharedBuilder.Clear();
            BuildMarkerWithSpace(item, isRtl, sharedBuilder);

            var fontSize = uniText.CurrentFontSize;
            var len = sharedBuilder.Length;

            var injectedStart = buf.virtualPositionedGlyphs.count;
            var curX = 0f;
            for (var c = 0; c < len; c++)
            {
                uint codepoint = sharedBuilder[c];
                if (!buf.TryResolveInjectedGlyph(fontProvider, codepoint, item.start, fontSize, out var marker))
                    continue;

                buf.virtualPositionedGlyphs.Add(new PositionedGlyph
                {
                    glyphId = (int)marker.GlyphIndex,
                    cluster = item.start,
                    x = curX,
                    y = baselineY,
                    fontId = marker.FontId,
                    shapedGlyphIndex = -1,
                    left = curX,
                    right = curX + marker.Advance,
                    top = baselineY,
                    bottom = baselineY
                });
                curX += marker.Advance;
            }

            var offsetX = isRtl ? rightmostX : leftmostX - curX;

            var injectedEnd = buf.virtualPositionedGlyphs.count;
            var data = buf.virtualPositionedGlyphs.data;
            for (var i = injectedStart; i < injectedEnd; i++)
            {
                data[i].x += offsetX;
                data[i].left += offsetX;
                data[i].right += offsetX;
            }
        }

        private void BuildMarkerWithSpace(ListItemInfo item, bool isRtl, StringBuilder sb)
        {
            if (isRtl) sb.Append(' ');

            if (item.displayNumber < 0)
            {
                sb.Append(bulletMarkers[Math.Max(0, Math.Min(item.nestingLevel, bulletMarkers.Length - 1))]);
            }
            else
            {
                var level = Math.Max(0, Math.Min(item.nestingLevel, orderedStyles.Length - 1));
                if (isRtl) sb.Append('.');
                AppendOrderedNumber(sb, item.displayNumber, orderedStyles[level]);
                if (!isRtl) sb.Append('.');
            }

            if (!isRtl) sb.Append(' ');
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsItemRtl(int cluster)
        {
            var levels = buffers.bidiLevels.data;
            return (uint)cluster < (uint)levels.Length && (levels[cluster] & 1) == 1;
        }

        private static void AppendOrderedNumber(StringBuilder sb, int n, OrderedMarkerStyle style)
        {
            switch (style)
            {
                case OrderedMarkerStyle.Decimal:
                    AppendInt(sb, n);
                    break;
                case OrderedMarkerStyle.LowerAlpha:
                    sb.Append(n > 0 ? (char)('a' + (n - 1) % 26) : '?');
                    break;
                case OrderedMarkerStyle.UpperAlpha:
                    sb.Append(n > 0 ? (char)('A' + (n - 1) % 26) : '?');
                    break;
                case OrderedMarkerStyle.LowerRoman:
                    AppendRoman(sb, n, true);
                    break;
                case OrderedMarkerStyle.UpperRoman:
                    AppendRoman(sb, n, false);
                    break;
                default:
                    AppendInt(sb, n);
                    break;
            }
        }

        private static void AppendInt(StringBuilder sb, int n)
        {
            if (n == 0)
            {
                sb.Append('0');
                return;
            }

            if (n < 0)
            {
                sb.Append('-');
                n = -n;
            }

            var start = sb.Length;
            while (n > 0)
            {
                sb.Append((char)('0' + n % 10));
                n /= 10;
            }

            var end = sb.Length - 1;
            while (start < end)
            {
                (sb[start], sb[end]) = (sb[end], sb[start]);
                start++;
                end--;
            }
        }

        private static readonly int[] RomanValues = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        private static readonly string[] RomanLower = { "m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i" };
        private static readonly string[] RomanUpper = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        private static void AppendRoman(StringBuilder sb, int n, bool lower)
        {
            if (n <= 0 || n > 3999)
            {
                AppendInt(sb, n);
                return;
            }

            var symbols = lower ? RomanLower : RomanUpper;
            for (var i = 0; i < RomanValues.Length; i++)
            {
                while (n >= RomanValues[i])
                {
                    sb.Append(symbols[i]);
                    n -= RomanValues[i];
                }
            }
        }
    }
}
