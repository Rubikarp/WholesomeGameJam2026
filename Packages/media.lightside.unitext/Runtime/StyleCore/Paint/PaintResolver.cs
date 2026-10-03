using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared worker-safe paint resolution and gradient-ramp lifetime. Live providers and texture
    /// dimensions are captured on the main thread; range and glyph emitters consume only this snapshot.
    /// </summary>
    internal sealed class PaintResolver
    {
        private struct FilteredRow
        {
            public Gradient gradient;
            public ColorMatrix combined;
            public int row;
        }

        private sealed class TextureRefComparer : IEqualityComparer<Texture2D>
        {
            public static readonly TextureRefComparer Instance = new();
            public bool Equals(Texture2D x, Texture2D y) => ReferenceEquals(x, y);
            public int GetHashCode(Texture2D value) => RuntimeHelpers.GetHashCode(value);
        }

        private sealed class SnapshotCatalog : INamedCatalog<PaintSwatch>
        {
            public readonly CatalogSnapshot<PaintSwatch> catalog = new();

            event NamedCatalogChangedHandler<PaintSwatch> INamedCatalog<PaintSwatch>.Changed
            {
                add { }
                remove { }
            }

            public bool TryGet(string name, out PaintSwatch entry) => catalog.TryGet(name, out entry);
            public IEnumerable<PaintSwatch> Enumerate() => catalog.Values;
        }

        private static readonly Func<PaintSwatch, string> swatchNameOf = static swatch => swatch.name;

        private readonly SnapshotCatalog snapshot = new();
        private readonly Dictionary<Texture2D, Vector2Int> textureDimensions =
            new(TextureRefComparer.Instance);
        private readonly HashSet<string> referencedNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<int> heldRampRows = new();
        private readonly List<FilteredRow> filteredRows = new();
        private bool hasProvider;
        private bool warnedMissingTexture;

        public void MarkSourceDirty() => snapshot.catalog.MarkDirty();

        public void PrepareForParallel(IPaintProvider provider)
        {
            foreach (var texture in textureDimensions.Keys)
            {
                if (texture == null)
                {
                    snapshot.catalog.MarkDirty();
                    break;
                }
            }

            if (!snapshot.catalog.Prepare(provider, swatchNameOf)) return;
            textureDimensions.Clear();
            hasProvider = provider != null;
            if (!hasProvider) return;
            foreach (var swatch in snapshot.catalog.Values)
                if (swatch.paint.source.kind == PaintSourceKind.Texture &&
                    swatch.paint.source.texture != null)
                    textureDimensions[swatch.paint.source.texture] =
                        new Vector2Int(swatch.paint.source.texture.width,
                            swatch.paint.source.texture.height);
        }

        public TextPaint ResolvePaint(ReadOnlySpan<char> token, out int rampRow)
        {
            if (!token.IsEmpty) referencedNames.Add(SpanIntern.Get(token));
            return Finalize(TextPaint.Resolve(token, hasProvider ? snapshot : null), out rampRow);
        }

        public TextPaint ResolvePaint(in PaintRef paint, out int rampRow)
        {
            if (paint.kind == PaintRefKind.Swatch && !string.IsNullOrEmpty(paint.swatch))
                referencedNames.Add(paint.swatch);
            return Finalize(TextPaint.Resolve(in paint, hasProvider ? snapshot : null), out rampRow);
        }

        public bool IsPaintToken(ReadOnlySpan<char> token)
        {
            if (token.IsEmpty) return false;
            referencedNames.Add(SpanIntern.Get(token));
            if (token[0] == '#') return true;
            if (hasProvider && snapshot.catalog.ContainsKey(SpanIntern.Get(token))) return true;
            return ColorParsing.TryParse(token, out _);
        }

        public bool ApplySourceChange(in NamedCatalogChange<PaintSwatch> change)
        {
            MarkSourceDirty();
            if (change.Kind is StateChangeKind.Clear or StateChangeKind.ReplaceAll or
                StateChangeKind.Reset)
                return referencedNames.Count != 0;
            return (!string.IsNullOrEmpty(change.Name) && referencedNames.Contains(change.Name)) ||
                   (!string.IsNullOrEmpty(change.PreviousName) &&
                    referencedNames.Contains(change.PreviousName));
        }

        public void ResetResolution()
        {
            referencedNames.Clear();
            for (var i = 0; i < heldRampRows.Count; i++)
                GradientRampAtlas.Instance.Release(heldRampRows[i]);
            heldRampRows.Clear();
            ReleaseFilteredRows();
        }

        /// <summary>
        /// Releases the rows held for colour-filtered gradients. Their content depends on the filter
        /// composed per mesh rebuild, not on the apply cycle, so the owner calls this at rebuild
        /// start: the re-run emission re-acquires immediately, which reuses and re-bakes the same row
        /// instead of accumulating one row per animated value.
        /// </summary>
        public void ReleaseFilteredRows()
        {
            for (var i = 0; i < filteredRows.Count; i++)
                GradientRampAtlas.Instance.Release(filteredRows[i].row);
            filteredRows.Clear();
        }

        /// <summary>
        /// Acquires a ramp row baked as <c>filter(sample × tint)</c> — the row a colour-filtered
        /// gradient quad renders with while its vertex colour carries only alpha. Rows are memoized
        /// per rebuild and released by <see cref="ReleaseFilteredRows"/>.
        /// </summary>
        public int AcquireFiltered(in Gradient gradient, Color32 tint, in ColorMatrix filter)
        {
            var combined = filter.Tinted(tint);
            for (var i = 0; i < filteredRows.Count; i++)
            {
                var entry = filteredRows[i];
                if (entry.combined.Equals(combined) && entry.gradient.Equals(gradient))
                    return entry.row;
            }

            var row = GradientRampAtlas.Instance.Acquire(in gradient, in combined);
            if (row != GradientRampAtlas.InvalidRow)
                filteredRows.Add(new FilteredRow { gradient = gradient, combined = combined, row = row });
            return row;
        }

        public void Return() => ResetResolution();

        private TextPaint Finalize(TextPaint paint, out int rampRow)
        {
            if (paint.kind == PaintSourceKind.Texture)
            {
                if (paint.texture is not null &&
                    textureDimensions.TryGetValue(paint.texture, out var dimensions))
                {
                    paint.texWidth = dimensions.x;
                    paint.texHeight = dimensions.y;
                }
                else
                {
                    paint.kind = PaintSourceKind.Solid;
                    paint.color = new Color32(255, 0, 255, 255);
                    if (!warnedMissingTexture)
                    {
                        warnedMissingTexture = true;
                        CatZones.material.MeowWarn(
                            "[UniText] Texture paint has a null or destroyed texture — rendering magenta. Assign a texture to the swatch (or fix the destroyed reference).");
                    }
                }
            }

            rampRow = GradientRampAtlas.InvalidRow;
            if (paint.kind == PaintSourceKind.Gradient)
            {
                rampRow = GradientRampAtlas.Instance.Acquire(in paint.gradient);
                if (rampRow == GradientRampAtlas.InvalidRow)
                    paint.kind = PaintSourceKind.Solid;
                else
                    heldRampRows.Add(rampRow);
            }
            return paint;
        }
    }
}
