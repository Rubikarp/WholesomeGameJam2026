using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightSide
{
    /// <summary>
    /// Common entry-data base for inline-flowed media (<see cref="InlineSprite"/>,
    /// <see cref="InlineObject"/>). Carries the name and shared presentation data; the
    /// renderable payload is added by the concrete subclass.
    /// </summary>
    /// <remarks>
    /// Pure shared data: one entry may be referenced by many components at once (e.g. through a
    /// shared <see cref="UniTextSprites"/> asset), so it holds no per-occurrence runtime state —
    /// the host <see cref="InlineMediaModifier{TEntry, TWrapper}"/> owns a wrapper pool per component.
    /// <para>All metrics are in em units (relative to <c>fontSize</c>):</para>
    /// <list type="bullet">
    /// <item><c>size</c> = (1,1) means the media is <c>fontSize</c> x <c>fontSize</c> pixels</item>
    /// <item><c>advance</c> = 1 means the cursor moves by <c>fontSize</c> pixels after this media</item>
    /// <item><c>bearingOffset</c> shifts the media relative to <c>fontSize</c></item>
    /// </list>
    /// <para><c>pivot</c> uses normalized RectTransform coordinates and <c>rotation</c> uses degrees.</para>
    /// <para>These presentation values are the weakest layer; keyed overrides, default
    /// parameters, and tag attributes can replace them per occurrence.</para>
    /// </remarks>
    [Serializable]
    [StateHierarchy]
    [StateSource]
    public abstract partial class InlineMedia
    {
        /// <summary>Lookup name used in the tag parameter (e.g. <c>jump</c> in <c>&lt;sprite=jump&gt;</c>). Case-insensitive.</summary>
        [SerializeField, StateProperty]
        private string name;

        /// <summary>Size in em units (1 = fontSize on each axis).</summary>
        [SerializeField, VectorDragField, StateProperty]
        private Vector2 size = Vector2.one;

        /// <summary>Offset from the layout position in em units.</summary>
        [SerializeField, VectorDragField, StateProperty]
        private Vector2 bearingOffset;

        /// <summary>Normalized rotation pivot; (0,0) is bottom-left and (1,1) is top-right.</summary>
        [SerializeField, VectorDragField, StateProperty]
        private Vector2 pivot;

        /// <summary>Z-axis rotation in degrees around <see cref="Pivot"/>.</summary>
        [SerializeField, StateProperty]
        private float rotation;

        /// <summary>Advance width in em units (1 = fontSize).</summary>
        [SerializeField, StateProperty]
        private float advance = 1;

        /// <summary>Extra height reserved above lines containing this media, in em units.</summary>
        [SerializeField, FormerlySerializedAs("lineHeight"), StateProperty]
        private float lineHeightAbove;

        /// <summary>Extra height reserved below lines containing this media, in em units.</summary>
        [SerializeField, StateProperty]
        private float lineHeightBelow;

    }

    /// <summary>Local presentation overrides for one provider entry selected by <see cref="Key"/>.</summary>
    [Serializable]
    [StateHierarchy]
    [StateSource]
    public abstract partial class InlineMediaOverride
    {
        /// <summary>Provider key whose entry this row overrides; the same key is used in tags.</summary>
        [SerializeField, StateProperty]
        private string key;

        /// <summary>Size override in em; NaN per axis inherits the provider entry.</summary>
        [SerializeField, Parameter, VectorDragField, Inheritable(1f, 1f), StateProperty]
        private Vector2 size = new(float.NaN, float.NaN);

        /// <summary>Bearing-offset override in em; NaN per axis inherits the provider entry.</summary>
        [SerializeField, Parameter, VectorDragField, Inheritable(0f, 0f), StateProperty]
        private Vector2 bearingOffset = new(float.NaN, float.NaN);

        /// <summary>Advance-width override in em; NaN inherits the provider entry.</summary>
        [SerializeField, Parameter, Inheritable(1f), StateProperty]
        private float advance = float.NaN;

        /// <summary>Extra line space above in em; NaN inherits the provider entry.</summary>
        [SerializeField, Parameter, Inheritable(0f), StateProperty]
        private float lineHeightAbove = float.NaN;

        /// <summary>Extra line space below in em; NaN inherits the provider entry.</summary>
        [SerializeField, Parameter, Inheritable(0f), StateProperty]
        private float lineHeightBelow = float.NaN;

        /// <summary>Normalized rotation-pivot override; NaN per axis inherits the provider entry.</summary>
        [SerializeField, Parameter, Inheritable(0f, 0f), StateProperty]
        private Vector2 pivot = new(float.NaN, float.NaN);

        /// <summary>Z-axis rotation override in degrees; NaN inherits the provider entry.</summary>
        [SerializeField, Parameter, Inheritable(0f), StateProperty]
        private float rotation = float.NaN;

    }
}
