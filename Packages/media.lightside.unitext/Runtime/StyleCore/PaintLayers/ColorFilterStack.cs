using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// The generator's per-component registry of active <see cref="FilterModifier"/>s and the
    /// per-rebuild table of composed filter matrices. Emitters ask <see cref="ResolveIndex"/> for the
    /// transform covering a cluster above a layer sequence (0 = none); the returned index resolves to
    /// a matrix for CPU folding (<see cref="GetMatrix"/>) or a lazily acquired
    /// <see cref="ColorMatrixAtlas"/> row for shader paths (<see cref="GetAtlasRow"/>). Single-threaded
    /// with its component's rebuild pipeline.
    /// </summary>
    internal sealed class ColorFilterStack
    {
        private readonly List<FilterModifier> modifiers = new();
        private readonly List<ColorMatrix> composed = new();
        private readonly List<int> composedRows = new();
        private int lastCluster = -1;
        private int lastSequence;
        private int lastResult;

        private const int RowNotAcquired = -2;

        public void Register(FilterModifier modifier)
        {
            if (!modifiers.Contains(modifier)) modifiers.Add(modifier);
        }

        public void Unregister(FilterModifier modifier) => modifiers.Remove(modifier);

        /// <summary>Whether no registered filter carries a span this cycle — the zero-cost gate every emitter checks first.</summary>
        public bool IsEmpty
        {
            get
            {
                for (var i = 0; i < modifiers.Count; i++)
                    if (modifiers[i].HasSpans)
                        return false;
                return true;
            }
        }

        /// <summary>
        /// Resets the per-rebuild composed table: releases the previous rebuild's atlas rows (the
        /// atlas sweep's grace period protects the currently displayed mesh) and re-orders the
        /// registered filters by their freshly stamped layer sequences.
        /// </summary>
        public void BeginRebuild()
        {
            for (var i = 0; i < composedRows.Count; i++)
                if (composedRows[i] >= 0)
                    ColorMatrixAtlas.Instance.Release(composedRows[i]);
            composedRows.Clear();
            composed.Clear();
            lastCluster = -1;

            for (var i = 1; i < modifiers.Count; i++)
            {
                var current = modifiers[i];
                var j = i - 1;
                while (j >= 0 && modifiers[j].LayerSequence > current.LayerSequence)
                {
                    modifiers[j + 1] = modifiers[j];
                    j--;
                }
                modifiers[j + 1] = current;
            }
        }

        /// <summary>
        /// The composed filter index for <paramref name="cluster"/> above the layer stamped at
        /// <paramref name="belowSequence"/>: 0 when no filter covers it, otherwise a 1-based index
        /// into the composed table. Covering filters compose bottom-up by stamped sequence, and
        /// nested spans of one filter compose outer-to-inner.
        /// </summary>
        public int ResolveIndex(int cluster, int belowSequence)
        {
            if (modifiers.Count == 0) return 0;
            if (cluster == lastCluster && belowSequence == lastSequence) return lastResult;

            var matrix = ColorMatrix.Identity;
            var any = false;
            for (var i = 0; i < modifiers.Count; i++)
                modifiers[i].AccumulateFilter(cluster, belowSequence, ref matrix, ref any);

            var result = 0;
            if (any && !matrix.IsIdentity) result = Intern(in matrix);
            lastCluster = cluster;
            lastSequence = belowSequence;
            lastResult = result;
            return result;
        }

        private int Intern(in ColorMatrix matrix)
        {
            for (var i = 0; i < composed.Count; i++)
                if (composed[i].Equals(matrix))
                    return i + 1;
            composed.Add(matrix);
            composedRows.Add(RowNotAcquired);
            return composed.Count;
        }

        /// <summary>The composed matrix behind a non-zero <see cref="ResolveIndex"/> result.</summary>
        public ColorMatrix GetMatrix(int index) => composed[index - 1];

        /// <summary>
        /// The <see cref="ColorMatrixAtlas"/> row behind a non-zero <see cref="ResolveIndex"/> result,
        /// acquired on first use and held until the next <see cref="BeginRebuild"/>.
        /// </summary>
        public int GetAtlasRow(int index)
        {
            var row = composedRows[index - 1];
            if (row == RowNotAcquired)
            {
                row = ColorMatrixAtlas.Instance.Acquire(composed[index - 1]);
                composedRows[index - 1] = row;
            }
            return row;
        }

        /// <summary>
        /// Applies the filters covering the current glyph to its finished face quad — called by the
        /// generator after every per-glyph modifier ran. A claimed face is skipped (the claiming
        /// layer folded the filter into its own paint), and so is a painted (non-solid) face, whose
        /// paint owner is responsible. A colour-glyph face samples a bitmap the CPU cannot recolour,
        /// so it carries its matrix row in the paint channel for the shader instead.
        /// </summary>
        public void ApplyToFace(UniTextMeshGenerator gen)
        {
            if (modifiers.Count == 0) return;
            if (gen.fillClaimedThisGlyph) return;

            var index = ResolveIndex(gen.currentCluster, UniTextMeshGenerator.DefaultFillSequence);
            if (index == 0) return;

            var baseIdx = gen.faceBaseIdx;
            if (gen.font.IsColor)
            {
                var row = GetAtlasRow(index);
                if (row == ColorMatrixAtlas.InvalidRow) return;
                gen.EnsureUvBuffer(3);
                var uvs3 = gen.Uvs3;
                var code = row + 1f;
                uvs3[baseIdx].z = code;
                uvs3[baseIdx + 1].z = code;
                uvs3[baseIdx + 2].z = code;
                uvs3[baseIdx + 3].z = code;
            }
            else
            {
                var uvs3 = gen.Uvs3;
                if (uvs3 != null && uvs3[baseIdx].w != 0f) return;
                var matrix = GetMatrix(index);
                var cols = gen.Colors;
                cols[baseIdx] = matrix.Transform(cols[baseIdx]);
                cols[baseIdx + 1] = matrix.Transform(cols[baseIdx + 1]);
                cols[baseIdx + 2] = matrix.Transform(cols[baseIdx + 2]);
                cols[baseIdx + 3] = matrix.Transform(cols[baseIdx + 3]);
            }
        }
    }
}
