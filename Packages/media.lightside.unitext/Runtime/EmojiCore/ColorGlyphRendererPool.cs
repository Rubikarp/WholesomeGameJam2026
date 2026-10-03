#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LightSide
{
    internal sealed class ColorGlyphRendererPool : IDisposable
    {
        internal struct RenderedGlyph
        {
            public bool isValid;
            public int width;
            public int height;
            public float bearingX;
            public float bearingY;
            public float advanceX;
            public float renderScale;
            public bool renderedByCOLRv1;
            public byte[] rgbaPixels;
        }

        private sealed class Renderer : IDisposable
        {
            private readonly FreeTypeFace face;
            private readonly int pixelSize;
            private readonly bool canRenderCOLRv1;
            private readonly bool hasFixedSizes;
            private readonly int bestFixedSizeIndex;
            private COLRv1Renderer colrV1Renderer;

            internal Renderer(FontSource fontSource, int faceIndex, int pixelSize,
                bool canRenderCOLRv1)
            {
                face = FreeTypeFace.TryCreate(fontSource, faceIndex)
                    ?? throw new InvalidOperationException("Failed to create FreeType face");
                this.pixelSize = pixelSize;

                var info = FT.GetFaceInfo(face.Pointer);
                hasFixedSizes = info.numFixedSizes > 0;
                this.canRenderCOLRv1 = canRenderCOLRv1 && info.IsScalable;
                if (!hasFixedSizes) return;

                int bestDifference = int.MaxValue;
                for (int i = 0; i < info.numFixedSizes; i++)
                {
                    int difference = Math.Abs(FT.GetFixedSize(face.Pointer, i) - pixelSize);
                    if (difference >= bestDifference) continue;
                    bestDifference = difference;
                    bestFixedSizeIndex = i;
                }
            }

            internal bool TryRenderGlyph(uint glyphIndex, int colrPixelSize, out RenderedGlyph result)
            {
                result = default;
                if (glyphIndex == 0) return false;

                var pointer = face.Pointer;
                bool freeTypeSizeIsSet = false;

                if (canRenderCOLRv1)
                {
                    if (!FT.SetPixelSize(pointer, colrPixelSize)) return false;
                    freeTypeSizeIsSet = !hasFixedSizes && colrPixelSize == pixelSize;

                    if (FT.GetColorGlyphPaint(pointer, glyphIndex, true, out var rootPaint))
                    {
                        colrV1Renderer ??= new COLRv1Renderer(pointer);
                        if (!colrV1Renderer.TryRenderGlyph(glyphIndex, colrPixelSize, rootPaint,
                                out var pixels, out int width, out int height,
                                out float bearingX, out float bearingY, out float renderScale))
                            return false;

                        result = new RenderedGlyph
                        {
                            isValid = true,
                            width = width,
                            height = height,
                            bearingX = bearingX,
                            bearingY = bearingY,
                            advanceX = width,
                            renderScale = renderScale,
                            renderedByCOLRv1 = true,
                            rgbaPixels = pixels
                        };
                        return true;
                    }
                }

                if (!freeTypeSizeIsSet && !SetFreeTypeSize(pointer)) return false;
                return TryRenderFreeType(pointer, glyphIndex, out result);
            }

            private bool SetFreeTypeSize(IntPtr pointer)
            {
                return hasFixedSizes
                    ? FT.SelectFixedSize(pointer, bestFixedSizeIndex)
                    : FT.SetPixelSize(pointer, pixelSize);
            }

            private static bool TryRenderFreeType(IntPtr pointer, uint glyphIndex,
                out RenderedGlyph result)
            {
                result = default;

                bool loaded = FT.LoadGlyph(pointer, glyphIndex, FT.LOAD_COLOR | FT.LOAD_RENDER);
                if (!loaded)
                {
                    loaded = FT.LoadGlyph(pointer, glyphIndex, FT.LOAD_RENDER);
                    if (!loaded)
                    {
                        loaded = FT.LoadGlyph(pointer, glyphIndex, FT.LOAD_DEFAULT);
                        if (!loaded || !FT.RenderGlyph(pointer)) return false;
                    }
                }

                var metrics = FT.GetGlyphMetrics(pointer);
                var bitmap = FT.GetBitmapData(pointer);
                if (bitmap.width <= 0 || bitmap.height <= 0) return false;

                int pixelDataSize = checked(bitmap.width * bitmap.height * 4);
                byte[] pixels = ArrayPool<byte>.Rent(pixelDataSize);
                bool transferred = false;
                try
                {
                    if (!FT.CopyBitmapAsRGBA(pointer, pixels)) return false;

                    result = new RenderedGlyph
                    {
                        isValid = true,
                        width = bitmap.width,
                        height = bitmap.height,
                        bearingX = metrics.bearingX,
                        bearingY = FT.GetBitmapTop(pointer),
                        advanceX = metrics.advanceX / 64f,
                        renderScale = 1f,
                        rgbaPixels = pixels
                    };
                    transferred = true;
                    return true;
                }
                finally
                {
                    if (!transferred) ArrayPool<byte>.Return(pixels);
                }
            }

            public void Dispose() => face.Dispose();
        }

        private const int ParallelThreshold = 16;

        private readonly FontSource fontSource;
        private readonly int faceIndex;
        private readonly int pixelSize;
        private readonly bool canRenderCOLRv1;
        private readonly ConcurrentBag<Renderer> availableRenderers = new();
        private readonly List<Renderer> allRenderers;
        private readonly object createLock = new();
        private readonly int maxRenderers;
        private int activeOperations;
        private bool disposed;

        internal ColorGlyphRendererPool(FontSource fontSource, int faceIndex, int pixelSize,
            bool canRenderCOLRv1, int maxRenderers = 0)
        {
            this.fontSource = fontSource ?? throw new ArgumentNullException(nameof(fontSource));
            this.faceIndex = faceIndex;
            this.pixelSize = pixelSize;
            this.canRenderCOLRv1 = canRenderCOLRv1;
            this.maxRenderers = maxRenderers > 0 ? maxRenderers : Environment.ProcessorCount;
            allRenderers = new List<Renderer>(this.maxRenderers);
        }

        internal RenderedGlyph[] RenderGlyphsBatch(PooledBuffer<uint> glyphIndices,
            int colrPixelSize, bool allowParallel)
        {
            int count = glyphIndices.count;
            var results = new RenderedGlyph[count];
            if (count == 0) return results;

            BeginOperation();
            try
            {
                if (!allowParallel || count < ParallelThreshold)
                    RenderSequential(glyphIndices, colrPixelSize, results);
                else
                    RenderParallel(glyphIndices, colrPixelSize, results);
                return results;
            }
            catch
            {
                ReleaseResults(results);
                throw;
            }
            finally
            {
                EndOperation();
            }
        }

        private Renderer RentRenderer()
        {
            if (availableRenderers.TryTake(out var renderer)) return renderer;

            lock (createLock)
            {
                if (allRenderers.Count >= maxRenderers)
                {
                    SpinWait spin = default;
                    while (!availableRenderers.TryTake(out renderer)) spin.SpinOnce();
                    return renderer;
                }

                renderer = new Renderer(fontSource, faceIndex, pixelSize, canRenderCOLRv1);
                allRenderers.Add(renderer);
                return renderer;
            }
        }

        private void ReturnRenderer(Renderer renderer)
        {
            if (renderer != null) availableRenderers.Add(renderer);
        }

        private static void ReleaseResults(RenderedGlyph[] results)
        {
            for (int i = 0; i < results.Length; i++)
            {
                if (results[i].rgbaPixels == null) continue;
                ArrayPool<byte>.Return(results[i].rgbaPixels);
                results[i].rgbaPixels = null;
            }
        }

        private void RenderSequential(PooledBuffer<uint> glyphIndices, int colrPixelSize,
            RenderedGlyph[] results)
        {
            var renderer = RentRenderer();
            try
            {
                for (int i = 0; i < glyphIndices.count; i++)
                    renderer.TryRenderGlyph(glyphIndices[i], colrPixelSize, out results[i]);
            }
            finally
            {
                ReturnRenderer(renderer);
            }
        }

        private void RenderParallel(PooledBuffer<uint> glyphIndices, int colrPixelSize,
            RenderedGlyph[] results)
        {
            int count = glyphIndices.count;
            int workerCount = Math.Min(maxRenderers, count);
            int chunkSize = (count + workerCount - 1) / workerCount;

            Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                workerId =>
                {
                    int start = workerId * chunkSize;
                    int end = Math.Min(start + chunkSize, count);
                    if (start >= end) return;

                    var renderer = RentRenderer();
                    try
                    {
                        for (int i = start; i < end; i++)
                            renderer.TryRenderGlyph(glyphIndices[i], colrPixelSize, out results[i]);
                    }
                    finally
                    {
                        ReturnRenderer(renderer);
                    }
                });
        }

        public void Dispose()
        {
            lock (createLock)
            {
                if (disposed) return;
                disposed = true;
                while (activeOperations != 0) Monitor.Wait(createLock);
                foreach (var renderer in allRenderers) renderer.Dispose();
                allRenderers.Clear();
            }

            while (availableRenderers.TryTake(out _)) { }
        }

        private void BeginOperation()
        {
            lock (createLock)
            {
                if (disposed) throw new ObjectDisposedException(nameof(ColorGlyphRendererPool));
                activeOperations++;
            }
        }

        private void EndOperation()
        {
            lock (createLock)
            {
                activeOperations--;
                if (disposed && activeOperations == 0) Monitor.PulseAll(createLock);
            }
        }
    }
}
#endif
