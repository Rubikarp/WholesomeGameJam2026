using System;

namespace LightSide
{
    using Core = UniTextFont.Core;

    [Flags]
    internal enum FontStyleRealization : byte
    {
        None = 0,
        RealWeight = 1 << 0,
        RealSlant = 1 << 1,
        SyntheticWeight = 1 << 2,
        SyntheticSlant = 1 << 3,
    }

    internal readonly struct FontFaceRequest : IEquatable<FontFaceRequest>
    {
        internal readonly int weight;
        internal readonly bool requestsWeight;
        internal readonly bool requestsSlant;
        internal readonly bool allowsRealWeight;
        internal readonly bool allowsRealSlant;
        internal readonly bool allowsSyntheticWeight;
        internal readonly bool allowsSyntheticSlant;
        internal readonly VariationConfig variation;
        internal readonly bool hasVariation;

        internal FontFaceRequest(int weight, bool requestsWeight, bool requestsSlant,
            bool allowsRealWeight, bool allowsRealSlant,
            bool allowsSyntheticWeight, bool allowsSyntheticSlant,
            in VariationConfig variation, bool hasVariation)
        {
            this.weight = weight;
            this.requestsWeight = requestsWeight;
            this.requestsSlant = requestsSlant;
            this.allowsRealWeight = allowsRealWeight;
            this.allowsRealSlant = allowsRealSlant;
            this.allowsSyntheticWeight = allowsSyntheticWeight;
            this.allowsSyntheticSlant = allowsSyntheticSlant;
            this.variation = variation;
            this.hasVariation = hasVariation;
        }

        internal bool NeedsFaceResolution => hasVariation || allowsRealWeight || allowsRealSlant;

        public bool Equals(FontFaceRequest other)
            => weight == other.weight
               && requestsWeight == other.requestsWeight
               && requestsSlant == other.requestsSlant
               && allowsRealWeight == other.allowsRealWeight
               && allowsRealSlant == other.allowsRealSlant
               && allowsSyntheticWeight == other.allowsSyntheticWeight
               && allowsSyntheticSlant == other.allowsSyntheticSlant
               && hasVariation == other.hasVariation
               && (!hasVariation || variation.Equals(other.variation));

        public override bool Equals(object obj) => obj is FontFaceRequest other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(weight, requestsWeight, requestsSlant, allowsRealWeight,
                allowsRealSlant, allowsSyntheticWeight, allowsSyntheticSlant,
                hasVariation ? variation.GetHashCode() : 0);
    }

    internal struct ResolvedFontInstance
    {
        internal Core font;
        internal int fontId;
        internal long varHash48;
        internal HB.hb_variation_t[] hbVariations;
        internal int[] ftCoords;
        internal FontStyleRealization realization;
        internal ushort effectiveWeight;

        internal bool IsVariableInstance => varHash48 != 0;
    }

    internal struct SystemFontSourceMatch
    {
        /// <summary>Platform cache identity of the materialized source: a font-file path on path-based backends or an opaque CoreText backing key on Apple platforms. Produced by a platform backend; never synthesized.</summary>
        internal string descriptor;
        internal string postScriptName;
        internal FontSource fontSource;
        internal IGlyphOutlineSource glyphOutlineSource;
        internal int faceIndex;
        internal int requestedWeight;
        internal bool requestedItalic;
        internal UniTextFont.AxisDefault[] axes;
        internal int coveredUtf16Length;
        internal float scale;
    }

    internal struct SystemFontMaterializedSource
    {
        internal FontSource fontSource;
        internal FaceInfo faceInfo;
        internal IGlyphOutlineSource glyphOutlineSource;
    }
}
