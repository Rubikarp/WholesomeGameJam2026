using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LightSide.Inspection
{
    /// <summary>
    /// Shared orchestration for runtime and editor inspection surfaces: draw-list rebuild and
    /// statistics refresh. Rendering and input remain owned by each surface.
    /// </summary>
    internal static class InspectionOverlayCore
    {
        /// <summary>Whether the snapshot and active inspection options require geometry rendering.</summary>
        internal static bool ShouldDraw(in TextInspectionSnapshot snapshot)
            => snapshot.hit || UniTextInspector.Filter != InspectionFilter.None || UniTextInspector.ShowBiDi;

        /// <summary>Cheap key over the inputs that affect overlay geometry: target, hovered glyph, active layers/filter/bidi, and a layout sample (glyph count + edge positions).</summary>
        internal static int ContentSignature(UniTextBase text, in TextInspectionSnapshot snap)
        {
            var h = new HashCode();
            h.Add(ObjectUtils.GetInstanceIdCompat(text));
            h.Add(snap.hit);
            h.Add(snap.glyph.glyphIndex);
            h.Add(snap.glyph.cluster);
            h.Add((int)UniTextInspector.Layers);
            h.Add((int)UniTextInspector.Filter);
            h.Add(UniTextInspector.ShowBiDi);
            h.Add(UniTextInspector.ShowStats);

            var g = text.ResultGlyphs;
            h.Add(g.Length);
            if (g.Length > 0)
            {
                h.Add(g[0].x); h.Add(g[0].y);
                ref readonly var last = ref g[g.Length - 1];
                h.Add(last.x); h.Add(last.y);
            }
            return h.ToHashCode();
        }

        /// <summary>
        /// Rebuilds cached inspection geometry when its signature changes. Hover geometry uses
        /// <paramref name="hitTarget"/> while filter and BiDi sweeps use <paramref name="sweepTarget"/>.
        /// </summary>
        internal static void BuildDrawList(InspectionDrawList drawList, UniTextBase sweepTarget, UniTextBase hitTarget,
            in TextInspectionSnapshot snapshot, List<ModifierInspection> modifiers, int sig)
        {
            if (!drawList.NeedsRebuild(sig)) return;
            drawList.Begin(sweepTarget.rectTransform, sig);
            if (hitTarget != null && snapshot.hit)
                InspectionGeometry.Draw(hitTarget, snapshot, modifiers, UniTextInspector.Layers, InspectionPalette.Default, drawList);
            if (UniTextInspector.Filter != InspectionFilter.None)
                InspectionFilterDraw.Draw(sweepTarget, UniTextInspector.Filter, drawList);
            if (UniTextInspector.ShowBiDi)
                InspectionBiDiDraw.Draw(sweepTarget, drawList);
        }

        /// <summary>Reformats the statistics card only when its content signature changes.</summary>
        internal static void RefreshStats(UniTextBase target, int sig, ref int statsSig, ref string statsCard, StringBuilder sb)
        {
            if (sig == statsSig) return;
            statsCard = InspectionStatsFormatter.Format(target, sb);
            statsSig = sig;
        }

    }
}
