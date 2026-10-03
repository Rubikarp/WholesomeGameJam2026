using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    public partial class UniTextEditable
    {
        private const float ScrollSensitivity = 30f;

        /// <summary>
        /// How much the text container is shifted relative to the viewport.
        /// Negative x = content shifted left (viewing text further right).
        /// Positive y = content shifted up (viewing text further down).
        /// </summary>
        private Vector2 scrollOffset;
        private int lastScrollCaretCodepoint = -1;
        private CaretAffinity lastScrollCaretAffinity;

        /// <summary>Scroll state exists only when this editor owns its clipping viewport (mask = direct parent,
        /// the field layout); standalone or nested under an outer scroller (ScrollRect Content), every
        /// scroll mutator stands down so no phantom offset accumulates or fights the outer layout.</summary>
        private bool CanScroll => OwnsScrollViewport;

        /// <summary>
        /// Whether a drag should be captured to scroll THIS field rather than pass through: it owns a
        /// clipping viewport AND its content actually overflows that viewport on some axis. A field with a
        /// viewport but content that fits — or no viewport at all — has nothing to scroll, so the drag is
        /// left for an enclosing pan / scroll handler instead of being swallowed.
        /// </summary>
        private bool HasScrollableOverflow()
        {
            if (!OwnsScrollViewport) return false;
            var view = GetViewportRect();
            var extent = ContentExtent();
            return extent.x > view.width + 1f || extent.y > view.height + 1f;
        }

        /// <summary>
        /// Rendered content extent for scrolling and content sizing: the processor's bare text
        /// measure plus <see cref="UniTextBase.Padding"/> — mirroring UniText's own
        /// <c>ILayoutElement</c> contract, so the padded inset scrolls fully into view.
        /// </summary>
        private Vector2 ContentExtent()
        {
            var size = TextComponent.ResultSize;
            var pad = TextComponent.Padding;
            return new Vector2(size.x + pad.x + pad.z, size.y + pad.y + pad.w);
        }

        /// <summary>
        /// Cached viewport size from the previous frame. When this changes (responsive layout,
        /// screen rotation, split-screen), scrollOffset is revalidated and EnsureCaretVisible
        /// is called to prevent the caret from drifting outside the visible area.
        /// </summary>
        private Vector2 cachedViewportSize;

        /// <summary>Cached caret rect in text-local space, computed once per dirty cycle.</summary>
        private Rect cachedCaretRect;

        /// <summary>The codepoint index for which <see cref="cachedCaretRect"/> was computed.</summary>
        private int cachedCaretCodepointIndex = -1;

        /// <summary>Glyph count at the time <see cref="cachedCaretRect"/> was computed. Used to
        /// detect layout changes that invalidate the cache.</summary>
        private int cachedCaretGlyphCount = -1;

        private float cachedLineHeight;

        private float GetCaretLineHeight()
            => cachedLineHeight > 0f ? cachedLineHeight : TextComponent.StrutLineHeight;

        /// <summary>
        /// Computes the caret rectangle in the text component's local coordinate space
        /// for the given codepoint index.
        /// </summary>
        /// <param name="codepointIndex">Codepoint index where the caret should appear.</param>
        /// <returns>
        /// A <see cref="Rect"/> in the text component's RectTransform local space
        /// representing the caret position and height.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Uses <see cref="PositionedGlyph.cluster"/> to map codepoint indices to visual
        /// positions. The caret is placed at the left edge of the glyph at the codepoint
        /// (between-glyphs positioning), or at the right edge of the preceding glyph when
        /// at the end of text or end of a line.
        /// </para>
        /// <para>
        /// For empty text, returns a rect at the first line's start edge (direction-aware)
        /// with the caret line height.
        /// </para>
        /// </remarks>
        private Rect CalculateCaretRect(int codepointIndex)
        {
            var glyphs = TextComponent.ResultGlyphs;

            if (glyphs.Length == 0)
                return TextComponent.GetEmptyLineRect();

            var textRect = TextComponent.GetPaddedRect();
            var totalCp = RenderedCodepointCount;
            EnsureCaretGeometry(glyphs);

            if (codepointIndex >= totalCp)
            {
                var buffers = TextComponent.Buffers;
                var lineCount = buffers.lines.count;
                if (lineCount > 0)
                {
                    var lastLine = buffers.lines[lineCount - 1];
                    if (lastLine.range.length == 0)
                    {
                        var lastLineIdx = lineCount - 1;
                        return ComputeEmptyLineCaretRect(lastLineIdx, glyphs, buffers, textRect);
                    }
                }

                int gEnd = GlyphBeforeCluster(totalCp);
                if (gEnd < 0) gEnd = glyphs.Length - 1;

                ref readonly var last = ref glyphs[gEnd];
                bool endRtl = TextComponent.Buffers.IsRtlLevelAt(last.cluster);
                return new Rect(textRect.xMin + (endRtl ? clusterLeft[last.cluster] : clusterRight[last.cluster]),
                    textRect.yMax - last.bottom, 0f, last.bottom - last.top);
            }

            if (!isComposing)
            {
                int caretCi = ViewText.CodepointToCharIndex(RenderedToDocument(codepointIndex, MarkupViewStick.Before));
                if (caretCi < ViewText.Length && ViewText[caretCi] == '\n')
                {
                    var buffers = TextComponent.Buffers;
                    if (buffers.lines.count > 0)
                    {
                        var lineIdx = SelectionHitTest.FindLineAtCodepoint(codepointIndex, buffers.lines, false);
                        var lineStart = buffers.lines[lineIdx].range.start;

                        int lastOnLine = GlyphBeforeCluster(codepointIndex);
                        if (lastOnLine >= 0 && glyphs[lastOnLine].cluster >= lineStart)
                        {
                            ref readonly var g = ref glyphs[lastOnLine];
                            return new Rect(textRect.xMin + g.right,
                                textRect.yMax - g.bottom, 0f, g.bottom - g.top);
                        }

                        return ComputeEmptyLineCaretRect(lineIdx, glyphs, buffers, textRect);
                    }
                }
            }

            var caretBuffers = TextComponent.Buffers;

            int gAfter = GlyphAtCluster(codepointIndex);
            int gBefore = GlyphBeforeCluster(codepointIndex);

            bool downstream = Selection.Affinity != CaretAffinity.Upstream;

            if (gAfter >= 0 && (downstream || gBefore < 0))
                return LeadingCaretRect(in glyphs[gAfter], codepointIndex, caretBuffers, textRect);

            if (gBefore >= 0)
            {
                ref readonly var g = ref glyphs[gBefore];
                int cb = g.cluster;
                bool rtl = caretBuffers.IsRtlLevelAt(cb);
                var h = g.bottom - g.top;
                cachedLineHeight = h;
                float cl = clusterLeft[cb], cr = clusterRight[cb];

                if (gAfter < 0 && codepointIndex > cb)
                {
                    int span = PositionedClusterSpan(cb, NextClusterAfter(cb), codepointIndex, out var caretOffset);
                    if (span > 1)
                    {
                        var t = Mathf.Clamp01((float)caretOffset / span);
                        var lerpX = rtl ? cr - (cr - cl) * t : cl + (cr - cl) * t;
                        return new Rect(textRect.xMin + lerpX, textRect.yMax - g.bottom, 0f, h);
                    }
                }

                return new Rect(textRect.xMin + (rtl ? cl : cr),
                    textRect.yMax - g.bottom, 0f, h);
            }

            if (gAfter >= 0)
                return LeadingCaretRect(in glyphs[gAfter], codepointIndex, caretBuffers, textRect);

            ref readonly var lastGlyph = ref glyphs[^1];
            return new Rect(textRect.xMin + lastGlyph.right, textRect.yMax - lastGlyph.bottom,
                0f, lastGlyph.bottom - lastGlyph.top);
        }

        private Rect LeadingCaretRect(in PositionedGlyph g, int codepointIndex,
            UniTextBuffers buffers, Rect textRect)
        {
            bool rtl = buffers.IsRtlLevelAt(codepointIndex);
            var h = g.bottom - g.top;
            cachedLineHeight = h;
            return new Rect(textRect.xMin + (rtl ? clusterRight[codepointIndex] : clusterLeft[codepointIndex]),
                textRect.yMax - g.bottom, 0f, h);
        }

        /// <summary>
        /// Computes a caret rect for a line that has no glyphs (empty line containing only \n,
        /// or a trailing empty line after the last \n). Uses the first glyph's Y as anchor and
        /// stacks the preceding lines through <see cref="TextLine.advancePrefix"/>.
        /// </summary>
        private Rect ComputeEmptyLineCaretRect(
            int lineIndex, ReadOnlySpan<PositionedGlyph> glyphs, UniTextBuffers buffers, Rect textRect)
        {
            float h = TextComponent.CurrentFontSize;
            float anchorBottom = h;

            if (glyphs.Length > 0)
            {
                h = glyphs[0].bottom - glyphs[0].top;
                anchorBottom = glyphs[0].bottom;
            }

            var lines = buffers.lines;
            var yOffset = lineIndex > 0 && lineIndex <= lines.count
                ? lines[lineIndex - 1].advancePrefix
                : 0f;

            bool rtlLine = lineIndex < lines.count && lines[lineIndex].IsRtl;
            float x = rtlLine ? textRect.xMax : textRect.xMin;
            float top = textRect.yMax - anchorBottom - yOffset;
            return new Rect(x, top, 0f, h);
        }

        /// <summary>
        /// Returns the cached caret rect if the cache is still valid for the given codepoint index
        /// and current layout state. Otherwise computes, caches, and returns it.
        /// </summary>
        /// <param name="codepointIndex">Codepoint index where the caret should appear.</param>
        /// <returns>Caret rect in text-local space.</returns>
        private Rect GetOrComputeCaretRect(int codepointIndex)
        {
            if (codepointIndex == cachedCaretCodepointIndex && cachedCaretGlyphCount == TextComponent.ResultGlyphs.Length)
                return cachedCaretRect;

            cachedCaretRect = CalculateCaretRect(codepointIndex);
            cachedCaretCodepointIndex = codepointIndex;
            cachedCaretGlyphCount = TextComponent.ResultGlyphs.Length;
            return cachedCaretRect;
        }

        /// <summary>
        /// Invalidates the cached caret rect and caret line height, forcing recomputation on the
        /// next call to <see cref="GetOrComputeCaretRect"/> / <see cref="GetCaretLineHeight"/>.
        /// Called at the start of <see cref="UpdateCaretAndSelection"/> to ensure freshness after
        /// text, selection, or style changes.
        /// </summary>
        private void InvalidateCaretRectCache()
        {
            cachedCaretGlyphCount = -1;
            cachedLineHeight = 0f;
        }

        /// <summary>
        /// Adjusts <see cref="scrollOffset"/> so that the caret is visible within the viewport,
        /// maintaining <see cref="caretScrollMargin"/> pixels of clearance from each edge.
        /// </summary>
        /// <param name="caretRect">Pre-computed caret rect in text-local space.</param>
        /// <remarks>
        /// <para>
        /// Single-line fields scroll horizontally only. Multi-line fields scroll both axes
        /// (vertical only when word wrap is enabled, since horizontal overflow does not occur
        /// with wrapping).
        /// </para>
        /// <para>
        /// After adjusting the offset, calls <see cref="ApplyScrollOffset"/> to move the
        /// text container.
        /// </para>
        /// </remarks>
        private void EnsureCaretVisible(Rect caretRect)
        {
            if (!CanScroll) return;
            if (isDragging) return;
            var viewRect = GetViewportRect();
            var margin = CaretScrollMargin;

            var vc = TextRectToViewport(caretRect);

            if (vc.xMax > viewRect.xMax - margin)
                scrollOffset.x += viewRect.xMax - margin - vc.xMax;

            if (vc.xMin < viewRect.xMin + margin)
                scrollOffset.x += viewRect.xMin + margin - vc.xMin;

            if (vc.yMin < viewRect.yMin + margin)
                scrollOffset.y += viewRect.yMin + margin - vc.yMin;

            if (vc.yMax > viewRect.yMax - margin)
                scrollOffset.y += viewRect.yMax - margin - vc.yMax;

            ClampScrollOffset();
            ApplyScrollOffset();
        }

        private void ClampScrollOffset()
        {
            if (!CanScroll) return;

            var viewRect = GetViewportRect();
            var extent = ContentExtent();

            if (scrollOffset.x > 0f)
                scrollOffset.x = 0f;

            if (extent.x <= viewRect.width)
            {
                scrollOffset.x = 0f;
            }
            else
            {
                var minScrollX = viewRect.width - extent.x;
                if (scrollOffset.x < minScrollX)
                    scrollOffset.x = minScrollX;
            }

            if (scrollOffset.y < 0f)
                scrollOffset.y = 0f;

            if (extent.y <= viewRect.height)
            {
                scrollOffset.y = 0f;
            }
            else
            {
                var maxScrollY = extent.y - viewRect.height;
                if (scrollOffset.y > maxScrollY)
                    scrollOffset.y = maxScrollY;
            }
        }

        /// <summary>
        /// Applies the current <see cref="scrollOffset"/> to the text container's
        /// <see cref="RectTransform.anchoredPosition"/>, shifting the rendered text
        /// within the viewport.
        /// </summary>
        private void ApplyScrollOffset()
        {
            if (!CanScroll) return;

            var position = new Vector2(
                scrollOffset.x,
                scrollOffset.y);
            if (TextComponent.rectTransform.anchoredPosition == position) return;
            TextComponent.rectTransform.anchoredPosition = position;
            InputGeometryChanged?.Invoke();
        }

        /// <summary>
        /// Grows the UniText RectTransform after processing when clipped — inside an input field's
        /// <see cref="UnityEngine.UI.RectMask2D"/> viewport it expands the content to at least the
        /// viewport so the mask clips a stable overlap while scrolling. Standalone the RectTransform is
        /// left untouched: the integrator sizes it, or a <see cref="UnityEngine.UI.ContentSizeFitter"/> /
        /// layout group drives it through this editor's <see cref="UnityEngine.UI.ILayoutElement"/>.
        /// Called from <see cref="OnCommitted"/> after text processing is complete.
        /// </summary>
        private void FitUniTextToContent()
        {
            if (!CanScroll) return;

            var rt = TextComponent.rectTransform;
            var viewRect = GetViewportRect();
            var textSize = ContentExtent();

            var targetHeight = Mathf.Max(viewRect.height, textSize.y);
            var targetWidth = TextComponent.WordWrap ? viewRect.width : Mathf.Max(viewRect.width, textSize.x);

            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
        }

        /// <summary>
        /// Recalculates the caret position, updates the selection highlight,
        /// scrolls a moved caret into view without overriding manual scrolling on geometry-only
        /// refreshes, and resets the caret blink timer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called after any operation that changes the selection state: cursor movement,
        /// text insertion/deletion, mouse click, drag selection, or undo/redo.
        /// </para>
        /// <para>
        /// The update order is important: caret rect must be computed first (it depends
        /// on the current glyph positions), then selection highlight (also depends on
        /// glyph positions), then scroll offset (depends on the caret rect).
        /// </para>
        /// </remarks>
        internal void UpdateCaretAndSelection(bool forceCaretReveal = false)
        {
            InvalidateCaretRectCache();

            var caretCodepoint = GetVisibleCaretCodepoint();
            var caretAffinity = Selection.Affinity;
            var caretMoved = forceCaretReveal || caretCodepoint != lastScrollCaretCodepoint
                || caretAffinity != lastScrollCaretAffinity;
            var caretRect = GetOrComputeCaretRect(caretCodepoint);

            if (isActive && caretMoved)
            {
                EnsureCaretVisible(caretRect);
            }
            else
            {
                ClampScrollOffset();
                ApplyScrollOffset();
            }
            lastScrollCaretCodepoint = caretCodepoint;
            lastScrollCaretAffinity = caretAffinity;

            if (caretRenderer != null)
            {
                caretRenderer.enabled = isActive && Selection.IsCollapsed
                    && (!isComposing || compositionOverlay.HasCursor);
                if (Selection.IsCollapsed)
                {
                    caretRenderer.SetCaretPosition(caretRect);
                    caretRenderer.ResetBlink();
                }
            }

            UpdateSelectionHighlight();
        }

        private void RefreshCaretVisual()
        {
            if (caretRenderer == null) return;
            var caretRect = GetOrComputeCaretRect(GetVisibleCaretCodepoint());
            caretRenderer.SetCaretPosition(caretRect);
        }

        private void UpdateSelectionHighlight() => Selectable?.RenderSelection();

        /// <summary>
        /// Handles mouse wheel scroll. Arrives through the editor's own
        /// <see cref="UnityEngine.EventSystems.IScrollHandler"/>; custom gesture pipelines call
        /// directly. No-op standalone (no clipping viewport) or while the content fits the viewport.
        /// </summary>
        public void HandleScroll(PointerEventData eventData)
        {
            if (!CanScroll) return;

            if (ContentExtent().y <= GetViewportRect().height)
                return;

            scrollOffset.y += -eventData.scrollDelta.y * ScrollSensitivity;
            ClampScrollOffset();
            ApplyScrollOffset();
            RefreshCaretVisual();
        }

        /// <summary>
        /// Resets the horizontal scroll offset on focus loss for single-line fields (web
        /// <c>&lt;input&gt;</c> scrolls back to the value start when it loses focus).
        /// Document-style editors (newlines accepted) keep their scroll position.
        /// </summary>
        internal void ResetScrollOnDefocus()
        {
            if (!CanScroll) return;

            if (!AcceptsNewlines)
            {
                scrollOffset.x = 0f;
            }

            ApplyScrollOffset();
        }

        /// <summary>
        /// Checks whether the viewport size changed since the last frame. If so,
        /// revalidates the scroll offset and ensures the caret remains visible.
        /// Called from <see cref="ProcessFrame"/> before processing text/selection dirty flags.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This handles responsive layouts, screen rotation, and split-screen scenarios
        /// where the RectTransform is resized at runtime. Without this check, the
        /// scrollOffset becomes stale — the caret may be outside the visible area,
        /// and single-line fields may show empty space on the right after shrinking.
        /// </para>
        /// </remarks>
        private void CheckViewportResize()
        {
            if (!isActive) return;

            var viewRect = GetViewportRect();
            var currentSize = new Vector2(viewRect.width, viewRect.height);

            if (cachedViewportSize == Vector2.zero)
            {
                cachedViewportSize = currentSize;
                return;
            }

            if (currentSize == cachedViewportSize) return;

            cachedViewportSize = currentSize;

            InvalidateCaretRectCache();
            var caretRect = GetOrComputeCaretRect(GetVisibleCaretCodepoint());
            EnsureCaretVisible(caretRect);

            selectionDirty = true;
        }

        /// <summary>
        /// Returns the visible viewport rect (the parent container that clips the content)
        /// in that container's local space.
        /// </summary>
        private Rect GetViewportRect()
        {
            var rt = ViewportRt();
            if (rt == null) return Rect.zero;

            return rt.rect;
        }

        /// <summary>
        /// The viewport that clips the scrolling text: the ancestor carrying the
        /// <see cref="UnityEngine.UI.RectMask2D"/> when one exists (a field box or a
        /// ScrollRect's Viewport further up). The editor shares its GameObject with
        /// <see cref="UniTextBase"/>, so its own transform IS the scrolling content, never the
        /// viewport. Falls back to the direct parent, then self (standalone, unclipped).
        /// </summary>
        private RectTransform ViewportRt()
        {
            EnsureClipCache();
            if (clipViewportCache != null) return clipViewportCache;
            var p = transform.parent as RectTransform;
            return p != null ? p : (RectTransform)transform;
        }

        /// <summary>
        /// The visible field box: a direct clipping parent when one exists, otherwise the editable itself.
        /// A more distant mask belongs to an outer scroller rather than to this field.
        /// </summary>
        internal RectTransform GetFieldRectTransform()
        {
            EnsureClipCache();
            return clipViewportCache != null && ReferenceEquals(transform.parent, clipViewportCache)
                ? clipViewportCache
                : (RectTransform)transform;
        }

        /// <summary>The visible field's rect in screen space.</summary>
        internal Rect GetFieldScreenRect()
            => CanvasUtil.WorldCornersToScreenRect(GetFieldRectTransform(), CanvasUtil.GetCanvasCamera(TextComponent.canvas));

        /// <summary>
        /// The visible (RectMask2D-clipped) viewport in screen space — the field's own transform is the
        /// scrolling content, so this is the box a scrolled-away caret leaves. Touch UI clips against it so
        /// a handle hides the moment its caret scrolls behind the mask.
        /// </summary>
        internal Rect GetViewportScreenRect()
            => CanvasUtil.WorldCornersToScreenRect(ViewportRt(), CanvasUtil.GetCanvasCamera(TextComponent.canvas));

        /// <summary>
        /// Screen-space caret position for <paramref name="codepointIndex"/> (document space) —
        /// <paramref name="bottom"/> picks the caret rect's bottom or top edge. The geometry
        /// query selection-UI implementations (<see cref="ISelectionHandles"/>,
        /// <see cref="IMagnifier"/>, system toolbars) issue against
        /// <see cref="ActiveEditor"/> to place themselves.
        /// </summary>
        public Vector2 GetCaretScreenPosition(int codepointIndex, bool bottom)
        {
            var caretRect = CaretRectAtSource(codepointIndex);
            return LocalToScreen(caretRect.x, bottom ? caretRect.yMin : caretRect.yMax);
        }

        /// <summary>The one text-local→screen projection (transform + canvas camera) every caret/handle/IME geometry query goes through.</summary>
        internal Vector2 LocalToScreen(float x, float y)
        {
            var world = TextComponent.rectTransform.TransformPoint(new Vector3(x, y, 0f));
            var cam = CanvasUtil.GetCanvasCamera(TextComponent.canvas);
            return RectTransformUtility.WorldToScreenPoint(cam, world);
        }


        /// <summary>
        /// Maps a content-local rect into the viewport (parent) local space, reflecting the live
        /// scroll (the content is positioned within the viewport by <see cref="ApplyScrollOffset"/>).
        /// Used for caret-visibility and selection-visibility tests against <see cref="GetViewportRect"/>.
        /// </summary>
        private Rect TextRectToViewport(Rect textLocal)
        {
            var textRt = TextComponent.rectTransform;
            var viewRt = ViewportRt();
            var min = (Vector2)viewRt.InverseTransformPoint(
                textRt.TransformPoint(new Vector3(textLocal.xMin, textLocal.yMin)));
            var max = (Vector2)viewRt.InverseTransformPoint(
                textRt.TransformPoint(new Vector3(textLocal.xMax, textLocal.yMax)));
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

    }
}
