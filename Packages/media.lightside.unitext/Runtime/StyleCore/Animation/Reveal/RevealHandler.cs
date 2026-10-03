using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Serializable glyph-appearance policy for <see cref="RevealModifier"/>. Custom handlers must
    /// transform only the current quad supplied to <see cref="Apply"/>, remain worker-thread safe,
    /// and resolve to the identity transform at <see cref="RevealGlyphInfo.Progress"/> = 1.
    /// </summary>
    [Serializable]
    [StateSource]
    [TypeMenuSuffix("RevealHandler")]
    public abstract partial class RevealHandler
    {
        /// <summary>Seconds this effect takes to play for one glyph; 0 makes the change instant.</summary>
        [SerializeField, Min(0f), NumberStateProperty(Min = 0)]
        private float duration = 0.25f;

        /// <summary>Transforms the current glyph quad for its normalized appearance state.</summary>
        public abstract void Apply(in RevealGlyphInfo info);

        /// <summary>
        /// Granularities this effect is meaningful at. A reveal counting in one outside the set still
        /// plays the effect — the mismatch is reported, not corrected — so declare what the effect
        /// reads: an effect that only transforms its own quad accepts every granularity, while one
        /// that treats <see cref="RevealGlyphInfo.unit"/> as a body needs
        /// <see cref="TextUnits.Grouping"/>.
        /// </summary>
        public virtual TextUnits SupportedUnits => TextUnits.All;

        /// <summary>
        /// Resolves a normalized pivot against the current glyph quad, <c>(0,0)</c> being its
        /// bottom-left corner and <c>(1,1)</c> its top-right, as everywhere else in Unity. Values
        /// outside the unit square land outside the glyph, which is what an effect orbiting a point
        /// beside the glyph wants. Bilinear, so a sheared or rotated quad keeps the pivot where the
        /// glyph's own axes put it.
        /// </summary>
        protected static Vector2 ResolvePivot(in RevealGlyphInfo info, Vector2 pivot)
        {
            var verts = info.generator.Vertices;
            var i = info.generator.faceBaseIdx;
            ref readonly var bl = ref verts[i];
            ref readonly var tl = ref verts[i + 1];
            ref readonly var tr = ref verts[i + 2];
            ref readonly var br = ref verts[i + 3];
            var bottomX = bl.x + (br.x - bl.x) * pivot.x;
            var bottomY = bl.y + (br.y - bl.y) * pivot.x;
            var topX = tl.x + (tr.x - tl.x) * pivot.x;
            var topY = tl.y + (tr.y - tl.y) * pivot.x;
            return new Vector2(bottomX + (topX - bottomX) * pivot.y,
                bottomY + (topY - bottomY) * pivot.y);
        }

        /// <summary>Offsets the current glyph quad without changing its final layout cell.</summary>
        protected static void ApplyOffset(in RevealGlyphInfo info, float x, float y)
            => GlyphQuad.Offset(info.generator.Vertices, info.generator.faceBaseIdx, x, y);

        /// <summary>Scales the current glyph quad around a normalized pivot.</summary>
        protected static void ApplyScale(in RevealGlyphInfo info, float x, float y, Vector2 pivot)
        {
            var p = ResolvePivot(in info, pivot);
            GlyphQuad.Scale(info.generator.Vertices, info.generator.faceBaseIdx, p.x, p.y, x, y);
        }

        /// <summary>Rotates the current glyph quad around a normalized pivot.</summary>
        protected static void ApplyRotation(in RevealGlyphInfo info, float degrees, Vector2 pivot)
        {
            var p = ResolvePivot(in info, pivot);
            GlyphQuad.Rotate(info.generator.Vertices, info.generator.faceBaseIdx,
                p.x, p.y, degrees * Mathf.Deg2Rad);
        }

        /// <summary>Multiplies the current glyph quad's authored alpha by a normalized opacity.</summary>
        protected static void ApplyFade(in RevealGlyphInfo info, float opacity)
        {
            opacity = Mathf.Clamp01(opacity);
            var colors = info.generator.Colors;
            var baseIdx = info.generator.faceBaseIdx;
            for (var i = 0; i < 4; i++)
            {
                ref var color = ref colors[baseIdx + i];
                color.a = (byte)Mathf.RoundToInt(color.a * opacity);
            }
        }

        /// <summary>Blends a multiplicative tint into the current glyph quad's authored colour.</summary>
        protected static void ApplyTint(in RevealGlyphInfo info, Color32 tint, float amount)
        {
            amount = Mathf.Clamp01(amount);
            var multiplier = Color32.Lerp(new Color32(255, 255, 255, 255), tint, amount);
            var colors = info.generator.Colors;
            var baseIdx = info.generator.faceBaseIdx;
            for (var i = 0; i < 4; i++)
            {
                ref var color = ref colors[baseIdx + i];
                color.r = (byte)(color.r * multiplier.r / 255);
                color.g = (byte)(color.g * multiplier.g / 255);
                color.b = (byte)(color.b * multiplier.b / 255);
                color.a = (byte)(color.a * multiplier.a / 255);
            }
        }

        /// <summary>Applies horizontal shear to the current glyph quad around a normalized pivot.</summary>
        protected static void ApplySkewX(in RevealGlyphInfo info, float shear, Vector2 pivot)
        {
            var p = ResolvePivot(in info, pivot);
            var verts = info.generator.Vertices;
            var baseIdx = info.generator.faceBaseIdx;
            for (var i = 0; i < 4; i++)
                verts[baseIdx + i].x += (verts[baseIdx + i].y - p.y) * shear;
        }
    }

    /// <summary>Base for handlers whose authored easing remaps <see cref="RevealGlyphInfo.Progress"/>.</summary>
    [Serializable]
    public abstract partial class EasedRevealHandler : RevealHandler
    {
        /// <summary>Progress curve used by this appearance effect.</summary>
        [SerializeField, StateProperty]
        private Ease easing = Ease.Of(EasingType.CubicOut);

        /// <summary>Returns the current appearance progress after the authored easing curve.</summary>
        protected float Progress(in RevealGlyphInfo info)
            => easing.Evaluate(info.Progress);
    }

    /// <summary>
    /// Base for eased effects that transform the glyph quad around a fixed point, which the author
    /// chooses. A subclass whose effect reads best from elsewhere sets <c>Pivot</c> in its own
    /// constructor, which only seeds the default an author then overrides.
    /// </summary>
    [Serializable]
    public abstract partial class GeometricRevealHandler : EasedRevealHandler
    {
        /// <summary>
        /// Fixed point this effect's scale, rotation and shear turn around, normalized over the glyph
        /// quad: <c>(0,0)</c> its bottom-left, <c>(1,1)</c> its top-right, outside the unit square
        /// beside the glyph.
        /// </summary>
        [SerializeField, VectorDragField, StateProperty] private Vector2 pivot = new(0.5f, 0.5f);

        /// <summary>The authored pivot resolved against the current glyph quad.</summary>
        protected Vector2 PivotPoint(in RevealGlyphInfo info) => ResolvePivot(in info, pivot);

        /// <summary>Scales the current glyph quad around the authored pivot.</summary>
        protected void ApplyScale(in RevealGlyphInfo info, float x, float y)
            => ApplyScale(in info, x, y, pivot);

        /// <summary>Rotates the current glyph quad around the authored pivot.</summary>
        protected void ApplyRotation(in RevealGlyphInfo info, float degrees)
            => ApplyRotation(in info, degrees, pivot);

        /// <summary>Applies horizontal shear to the current glyph quad around the authored pivot.</summary>
        protected void ApplySkewX(in RevealGlyphInfo info, float shear)
            => ApplySkewX(in info, shear, pivot);
    }

    /// <summary>Applies several reveal handlers in authored order while occupying one modifier slot.</summary>
    [Serializable]
    [TypeGroup("Composition", 1)]
    [TypeDescription("Stacks several appearance effects in order.")]
    public sealed partial class CompositeRevealHandler : RevealHandler
    {
        /// <summary>Appearance effects applied in order to the same glyph quad.</summary>
        [SerializeReference, TypeSelector, StateList(nameof(ApplyHandlersChange), Owned = true,
            AllowNullItems = false, AllowDuplicateReferences = false)]
        private RevealHandler[] handlers = Array.Empty<RevealHandler>();

        [NonSerialized] private ReferenceBinding<RevealHandler> binding;
        [NonSerialized] private StateChangedHandler childChangedCallback;

        private void ApplyHandlersChange()
        {
            EnsureChildBinding();
            PublishStateChange(Members.Handlers);
        }

        /// <summary>Subscribes the current children (recursively) so their edits republish through this composite.</summary>
        internal void EnsureChildBinding()
        {
            binding ??= new ReferenceBinding<RevealHandler>(Connect, Disconnect);
            binding.Reconcile(handlers);
        }

        /// <summary>Releases every child subscription taken by <see cref="EnsureChildBinding"/>.</summary>
        internal void ReleaseChildBinding() => binding?.Clear();

        private void Connect(RevealHandler value)
        {
            value.Changed += childChangedCallback ??= OnChildChanged;
            (value as CompositeRevealHandler)?.EnsureChildBinding();
        }

        private void Disconnect(RevealHandler value)
        {
            value.Changed -= childChangedCallback;
            (value as CompositeRevealHandler)?.ReleaseChildBinding();
        }

        private void OnChildChanged(IStateChangeSource source, in StateChange change)
            => PublishStateChange(Members.Handlers);

        /// <summary>
        /// Applies every child against its own duration inside this composite's slot: a shorter
        /// child settles early, a child at zero starts settled, and a duration at or beyond the
        /// composite's rides the slot timeline — the slot length itself always comes from the
        /// composite's <c>Duration</c>.
        /// </summary>
        public override void Apply(in RevealGlyphInfo info)
        {
            var slotDuration = Duration;
            for (var i = 0; i < handlers.Length; i++)
            {
                var child = handlers[i];
                var childDuration = child.Duration;
                if (slotDuration <= 0f || childDuration >= slotDuration)
                {
                    child.Apply(in info);
                    continue;
                }
                var progress = childDuration <= 0f
                    ? 1f
                    : Mathf.Clamp01(info.Progress * slotDuration / childDuration);
                var childInfo = info.WithProgress(progress);
                child.Apply(in childInfo);
            }
        }
    }

    /// <summary>
    /// Named appearance effect of an <see cref="IRevealHandlerProvider"/> catalog, selected per
    /// range by the tag parameter (<c>&lt;reveal=name&gt;</c>). An empty name marks the entry
    /// used by ranges that select no name.
    /// </summary>
    [Serializable]
    [StateSource]
    public sealed partial class RevealHandlerEntry
    {
        /// <summary>Name matched against the tag parameter (case-insensitive); empty serves ranges that select no name.</summary>
        [SerializeField, StateProperty] private string name;

        /// <summary>Appearance effect applied to ranges that select this entry.</summary>
        [SerializeReference, TypeSelector,
         StateProperty(nameof(ApplyHandlerSwap), Owned = true), StateLink]
        private RevealHandler handler;

        /// <summary>
        /// Effect played while a cluster of this entry leaves the visible part of its range; unset
        /// replays <see cref="Handler"/> itself, which every effect supports since
        /// <see cref="RevealGlyphInfo.Progress"/> is the settled glyph at 1 in both directions.
        /// </summary>
        [SerializeReference, TypeSelector,
         StateProperty(nameof(ApplyHandlerSwap), Owned = true), StateLink]
        private RevealHandler hideHandler;

        private void ApplyHandlerSwap(StateMember member, RevealHandler previous,
            RevealHandler current)
        {
            if (observed)
            {
                (previous as CompositeRevealHandler)?.ReleaseChildBinding();
                (current as CompositeRevealHandler)?.EnsureChildBinding();
            }
            PublishStateChange(member);
        }

        [NonSerialized] private bool observed;

        internal void OnObserved()
        {
            observed = true;
            (handler as CompositeRevealHandler)?.EnsureChildBinding();
            (hideHandler as CompositeRevealHandler)?.EnsureChildBinding();
        }

        internal void OnUnobserved()
        {
            observed = false;
            (handler as CompositeRevealHandler)?.ReleaseChildBinding();
            (hideHandler as CompositeRevealHandler)?.ReleaseChildBinding();
        }
    }
}
