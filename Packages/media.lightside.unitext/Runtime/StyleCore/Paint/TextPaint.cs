using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Frame a gradient/texture is mapped against — how the paint spreads over the text.</summary>
    public enum PaintMapping : byte
    {
        /// <summary>No value at this layer — resolves down the override chain (tag → default parameters → modifier field → swatch); a fully unresolved chain means <see cref="Block"/>.</summary>
        Inherit,
        /// <summary>One mapping across the whole text block.</summary>
        Block,
        /// <summary>Restarts per line.</summary>
        Line,
        /// <summary>Restarts per glyph.</summary>
        Glyph,
        /// <summary>Spans the styled range the paint was applied to.</summary>
        Range,
    }

    /// <summary>
    /// A resolved paint — flat colour, gradient, or texture — shared by every paint consumer.
    /// Built per application from a parameter token (<see cref="Resolve"/>); it is a runtime value,
    /// never a serialized field on a modifier. Invariant: projection fields carry only resolved
    /// values — never <c>Inherit</c>/<c>NaN</c> — so emitters and the shader contract read them
    /// without sentinel checks.
    /// </summary>
    public struct TextPaint
    {
        /// <summary>Resolved solid, gradient, or texture source kind.</summary>
        public PaintSourceKind kind;

        /// <summary>Resolved solid colour or tint multiplied with the sampled source.</summary>
        public Color32 color;

        /// <summary>Resolved gradient payload when <see cref="kind"/> is <see cref="PaintSourceKind.Gradient"/>.</summary>
        public Gradient gradient;

        /// <summary>Resolved geometric projection kind.</summary>
        public PaintProjectionKind shape;

        /// <summary>Resolved texture when <see cref="kind"/> is <see cref="PaintSourceKind.Texture"/>.</summary>
        public Texture2D texture;

        /// <summary>Text bounds used as the projection frame.</summary>
        public PaintMapping mapping;

        /// <summary>Resolved texture fitting policy.</summary>
        public PaintFit fit;

        /// <summary>Resolved gradient continuation outside the projection frame.</summary>
        public PaintSpread spread;

        /// <summary>Resolved projection rotation in degrees.</summary>
        public float angle;

        /// <summary>Resolved projection zoom or texture repeat count.</summary>
        public float scale;

        /// <summary>Resolved normalized projection-origin offset.</summary>
        public Vector2 offset;

        /// <summary>Resolved compositing mode.</summary>
        public LayerBlend blend;

        /// <summary>Texture pixel dimensions, populated on the main thread before parallel layout; consumed by worker-thread aspect-fit.</summary>
        public int texWidth, texHeight;

        /// <summary>Creates a runtime solid paint with the engine-default projection.</summary>
        public static TextPaint Solid(Color32 color) => new()
        {
            kind = PaintSourceKind.Solid,
            color = color,
            mapping = PaintMapping.Block,
            shape = PaintProjectionKind.Linear,
            fit = PaintFit.Stretch,
            spread = PaintSpread.Clamp,
            scale = 1f,
            blend = LayerBlend.Normal,
        };

        internal void ApplyTint(Color32 tint)
        {
            if (tint.r == byte.MaxValue && tint.g == byte.MaxValue &&
                tint.b == byte.MaxValue && tint.a == byte.MaxValue)
                return;
            color = MultiplyColor(color, tint);
        }

        internal static Color32 MultiplyColor(Color32 first, Color32 second)
            => new(
                (byte)((first.r * second.r + 127) / 255),
                (byte)((first.g * second.g + 127) / 255),
                (byte)((first.b * second.b + 127) / 255),
                (byte)((first.a * second.a + 127) / 255));

        internal static Color32 LerpColor(Color32 from, Color32 to, float t)
            => new(
                (byte)Mathf.RoundToInt(Mathf.LerpUnclamped(from.r, to.r, t)),
                (byte)Mathf.RoundToInt(Mathf.LerpUnclamped(from.g, to.g, t)),
                (byte)Mathf.RoundToInt(Mathf.LerpUnclamped(from.b, to.b, t)),
                (byte)Mathf.RoundToInt(Mathf.LerpUnclamped(from.a, to.a, t)));

        /// <summary>
        /// Resolves one paint token: a swatch name (via <paramref name="swatches"/>) carrying the paint
        /// appearance (kind/colour/gradient/texture) and its full projection (the base layer of the
        /// override hierarchy — see <see cref="ApplyProjection"/>), or a colour literal
        /// (<c>#RRGGBBAA</c> / named) → solid with engine-default projection. The name is tried first
        /// unless the token is an explicit <c>#</c> hex; an unresolved token falls back to opaque white.
        /// Touches no Unity APIs (worker-thread safe); texture pixel dimensions are filled separately
        /// on the main thread.
        /// </summary>
        public static TextPaint Resolve(ReadOnlySpan<char> token, INamedCatalog<PaintSwatch> swatches)
        {
            var paint = Solid(new Color32(255, 255, 255, 255));
            if (token.IsEmpty)
                return paint;

            if (swatches != null && token[0] != '#')
            {
                if (swatches.TryGet(SpanIntern.Get(token), out var swatch))
                {
                    var authored = swatch.paint;
                    paint.kind = authored.source.kind;
                    if (paint.kind != PaintSourceKind.Solid &&
                        paint.kind != PaintSourceKind.Gradient &&
                        paint.kind != PaintSourceKind.Texture)
                        throw new ArgumentOutOfRangeException(nameof(swatch), paint.kind,
                            "Unknown paint source kind.");
                    paint.color = authored.source.color;
                    if (paint.kind != PaintSourceKind.Solid && paint.color.a == 0)
                        paint.color = new Color32(255, 255, 255, 255);
                    paint.gradient = authored.source.gradient;
                    paint.texture = authored.source.texture;
                    paint.blend = authored.blend.Resolved();
                    if (swatch.mapping != PaintMapping.Inherit) paint.mapping = swatch.mapping;
                    paint.shape = authored.projection.kind.Resolved();
                    paint.fit = authored.projection.fit.Resolved();
                    paint.spread = authored.projection.spread.Resolved();
                    paint.angle = authored.projection.angle;
                    if (authored.projection.scale > 0f) paint.scale = authored.projection.scale;
                    paint.offset = authored.projection.offset;
                    return paint;
                }
            }

            if (ColorParsing.TryParse(token, out var c))
                paint.color = c;
            return paint;
        }

        /// <summary>Resolves an authored <see cref="PaintRef"/> by its explicit kind — a colour literal, a
        /// named swatch (via <paramref name="swatches"/>), or opaque white for the default (the layer swaps
        /// in its own default colour afterward).</summary>
        public static TextPaint Resolve(in PaintRef paint, INamedCatalog<PaintSwatch> swatches) => paint.kind switch
        {
            PaintRefKind.Color => Solid(paint.color),
            PaintRefKind.Swatch => Resolve(paint.swatch.AsSpan(), swatches),
            _ => Solid(new Color32(255, 255, 255, 255))
        };

        /// <summary>
        /// The single projection-override resolve: layers a modifier's serialized fields and the
        /// remaining per-range tokens of <paramref name="reader"/> over the values this paint already
        /// carries from its swatch (or the engine defaults for a colour literal). Per field, strongest
        /// wins: tag/default-parameter token → modifier field → swatch. <c>Inherit</c> enum members and
        /// <c>NaN</c> floats/axes mean "no value at that layer"; a non-positive resolved scale
        /// normalizes to 1.
        /// </summary>
        public void ApplyProjection(ref ParameterReader reader, PaintMapping mappingField,
            PaintProjectionKind shapeField, PaintFit fitField, float angleField, float scaleField,
            Vector2 offsetField)
        {
            reader.NextEnum(out PaintMapping m, mappingField);
            reader.NextEnum(out PaintProjectionKind s, shapeField);
            reader.NextEnum(out PaintFit f, fitField);
            reader.NextFloat(out var a, angleField);
            reader.NextFloat(out var sc, scaleField);
            reader.NextVector2(out var off, offsetField);
            ApplyProjection(m, s, f, a, sc, off);
        }

        /// <summary>
        /// Applies already-resolved projection overrides, strongest layer last: a non-sentinel value
        /// replaces what this paint carries from its swatch; <c>Inherit</c> enum members and
        /// <c>NaN</c> floats/axes keep the swatch value. A non-positive scale normalizes to 1.
        /// </summary>
        public void ApplyProjection(PaintMapping mappingValue, PaintProjectionKind shapeValue,
            PaintFit fitValue, float angleValue, float scaleValue, Vector2 offsetValue)
        {
            if (mappingValue != PaintMapping.Inherit) mapping = mappingValue;
            ApplyProjectionDetails(shapeValue, fitValue, angleValue, scaleValue, offsetValue);
        }

        /// <summary>
        /// Applies already-resolved non-mapping projection overrides under the same sentinel
        /// contract as <see cref="ApplyProjection(PaintMapping,PaintProjectionKind,PaintFit,float,float,Vector2)"/> —
        /// for consumers whose mapping vocabulary differs from <see cref="PaintMapping"/> and is
        /// resolved separately.
        /// </summary>
        public void ApplyProjectionDetails(PaintProjectionKind shapeValue, PaintFit fitValue,
            float angleValue, float scaleValue, Vector2 offsetValue)
        {
            if (shapeValue != PaintProjectionKind.Inherit) shape = shapeValue;
            if (fitValue != PaintFit.Inherit) fit = fitValue;
            if (!float.IsNaN(angleValue)) angle = angleValue;
            if (!float.IsNaN(scaleValue)) scale = scaleValue > 0f ? scaleValue : 1f;
            if (!float.IsNaN(offsetValue.x)) offset.x = offsetValue.x;
            if (!float.IsNaN(offsetValue.y)) offset.y = offsetValue.y;
        }

        /// <summary>Applies the modifier/default/tag blend override, retaining the swatch mode when inherited.</summary>
        public void ApplyBlend(ref ParameterReader reader, LayerBlend field)
        {
            reader.NextEnum(out LayerBlend resolved, field);
            if (resolved != LayerBlend.Inherit) blend = resolved;
        }

        /// <summary>
        /// Applies the spread override, retaining the swatch value when inherited. Occupies the final
        /// slot of its consumer's positional parameter list: call it after every other parameter that
        /// consumer reads, since slot positions are part of the authored contract.
        /// </summary>
        public void ApplySpread(ref ParameterReader reader, PaintSpread field)
        {
            reader.NextEnum(out PaintSpread resolved, field);
            if (resolved != PaintSpread.Inherit) spread = resolved;
        }
    }
}
