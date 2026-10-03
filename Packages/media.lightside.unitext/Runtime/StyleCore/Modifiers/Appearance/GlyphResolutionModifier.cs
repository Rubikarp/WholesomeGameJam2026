using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Raises the atlas tile resolution of the glyphs in its range by up to two size classes above the font
    /// default. Grow-only and shared: the atlas key ignores tile size, so every component that shows the glyph
    /// reads one tile at the highest boost any range requested, and the tile only ever grows.
    /// </summary>
    /// <remarks>
    /// Cost scales with the promoted tile: each class up is ~4× the tile memory (a +2 glyph is 16× a 64px one),
    /// and a promoted glyph stays large while referenced (reclaimed by eviction once unused). Scope it to the
    /// glyphs that need the crispness (a heading, an emphasized word), not whole paragraphs.
    /// Parameter form: <c>resolutionBoost</c> (0–2), defaulting to the serialized field.
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 8)]
    [TypeColor("#7FB2FF")]
    [TypeDescription("Raises atlas tile resolution for the glyphs in range (grow-only, shared).")]
    [GenerateParameters]
    public partial class GlyphResolutionModifier : BaseModifier, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;
        /// <summary>Size-class boost applied to the glyph's tile: 0 = font default, 1/2 = one/two classes crisper. Grow-only and shared across components.</summary>
        [SerializeField, Parameter, Range(0, 2), NumberStateProperty(nameof(MarkMeshDirty), Min = 0, Max = 2)] private int resolutionBoost;

        private struct ResRange { public int start, end, boost; }

        private PooledList<ResRange> ranges;
        private Action onGlyphCallback;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<ResRange>(8);
            ranges.FakeClear();
            onGlyphCallback ??= OnGlyph;
            uniText.MeshGenerator.onGlyph.Subscribe(onGlyphCallback);
        }

        protected override void OnDisable() => uniText.MeshGenerator.onGlyph.Unsubscribe(onGlyphCallback);

        protected override void OnDestroy()
        {
            ranges?.Return();
            ranges = null;
            onGlyphCallback = null;
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var boost = Param.ResolutionBoost.Resolve(this, in context);
            boost = boost < 0 ? 0 : boost > 2 ? 2 : boost;
            if (boost == 0) return;
            ranges.Add(new ResRange
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                boost = boost
            });
        }

        private void OnGlyph()
        {
            var gen = uniText.MeshGenerator;
            if (gen.font.IsColor && !gen.HasColorFaceField) return;

            int cluster = gen.currentCluster;
            for (int i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                if (cluster < r.start || cluster >= r.end) continue;
                if (r.boost > gen.currentTileSizeBoost) gen.currentTileSizeBoost = r.boost;
            }
        }
    }
}
