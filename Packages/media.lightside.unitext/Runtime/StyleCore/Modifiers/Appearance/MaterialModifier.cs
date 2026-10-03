using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Applies a user-authored <see cref="Material"/> to a text range by emitting a dedicated sub-mesh
    /// with its own <c>CanvasRenderer</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The custom shader must include <c>UniText_Custom.cginc</c> — see
    /// <c>Assets/Create/UniText/Custom Material Shader</c> for a working template. The glyph atlases are
    /// global shader bindings and must NOT appear in the shader's Properties block; per-glyph user data is written into
    /// <c>TEXCOORD2</c> / <c>TEXCOORD3</c> (see <see cref="constantUv2"/>, <see cref="constantUv3"/>,
    /// <see cref="glyphDataWriter"/> and <see cref="OnWriteGlyphUV"/>).
    /// </para>
    /// <para>
    /// Default <see cref="renderOrder"/> is <see cref="MaterialRenderOrder.Replace"/>: the base pass is
    /// suppressed on the range (the face quad is claimed and not recorded into the main mesh). Use
    /// <see cref="MaterialRenderOrder.Keep"/> to keep the base pass
    /// and stack the custom material as its own layer, ordered by this modifier's position in Styles.
    /// </para>
    /// <para>
    /// Parameter: optional tint color. Format: <c>&lt;mat=#RRGGBBAA&gt;</c> or <c>&lt;mat=red&gt;</c>.
    /// The tint multiplies the vertex color that the shader receives.
    /// </para>
    /// <para>
    /// <b>Replace</b> suppresses the base face structurally: the glyph's base quad is not recorded, so
    /// the result is independent of where the modifier sits among other Styles. Paint layers (stroke,
    /// shadow, glow) are separate quads at their own sequence and still render over/under it. Emoji
    /// glyphs are handled identically to text — the unified vertex-mode contract lets the one custom
    /// material render every glyph mode.
    /// </para>
    /// </remarks>
    [Serializable]
    [TypeGroup("Appearance", 10)]
    [TypeDescription("Applies a custom Material to a text range via a dedicated sub-mesh and CanvasRenderer.")]
    [GenerateParameters]
    public partial class MaterialModifier : SubMeshModifier, IModifierCommitChanges
    {
        UniTextCommitChanges IModifierCommitChanges.CommitChanges
            => UniTextCommitChanges.Appearance;
        /// <summary>Controls whether the custom material replaces the base text pass on the range or renders alongside it.</summary>
        public enum MaterialRenderOrder : byte
        {
            /// <summary>Base text pass is suppressed on the range (the face quad is not recorded into the main mesh); only the custom material shows.</summary>
            Replace = 0,
            /// <summary>Base text pass is kept; the custom material renders as its own layer, stacked by this modifier's position in <c>Styles</c>.</summary>
            Keep = 1,
        }

        /// <summary>Per-glyph context passed to <see cref="glyphDataWriter"/> and <see cref="OnWriteGlyphUV"/>.</summary>
        public readonly ref struct MaterialGlyphContext
        {
            public readonly int cluster;
            /// <summary>
            /// Monotonic 0-based index of this glyph across <b>all</b> ranges selected by this modifier in
            /// the current rebuild. Useful for staggered per-glyph animations. Not reset between ranges.
            /// </summary>
            public readonly int glyphIndexInSelection;
            public readonly float cursorX;
            public readonly float baselineY;
            public readonly float glyphHeight;
            public readonly string parameter;

            public MaterialGlyphContext(int cluster, int glyphIndexInSelection, float cursorX, float baselineY, float glyphHeight, string parameter)
            {
                this.cluster = cluster;
                this.glyphIndexInSelection = glyphIndexInSelection;
                this.cursorX = cursorX;
                this.baselineY = baselineY;
                this.glyphHeight = glyphHeight;
                this.parameter = parameter;
            }
        }

        /// <summary>Delegate signature for writing per-glyph custom UV data at sub-mesh build time.</summary>
        public delegate void MaterialGlyphWriter(in MaterialGlyphContext ctx, out Vector4 uv2, out Vector4 uv3);

        /// <summary>Custom material applied to this modifier's ranges.</summary>
        [Tooltip("Custom material to apply to the range. The shader must include UniText_Custom.cginc.")]
        [SerializeField, StateProperty(nameof(ApplyMaterialChange))] private Material material;

        /// <summary>How the custom material composes with the base text pass.</summary>
        [Tooltip("How the custom material composes with the base text pass. Replace suppresses the base pass on the range.")]
        [SerializeField, StateProperty(nameof(MarkMeshDirty))] private MaterialRenderOrder renderOrder = MaterialRenderOrder.Replace;

        public override bool ClaimsFill => renderOrder == MaterialRenderOrder.Replace;

        /// <summary>Tie-break used to order this modifier's sub-mesh.</summary>
        [Tooltip("Tie-break for this modifier's sub-mesh (lower renders first). Order relative to other modifiers follows their position in Styles.")]
        [SerializeField, StateProperty(nameof(MarkMeshDirty))] private int sortIndex;

        /// <summary>Constant TEXCOORD2 value written to every glyph vertex.</summary>
        [Tooltip("Constant value written to TEXCOORD2 of every glyph vertex in the sub-mesh.")]
        [SerializeField, StateProperty(nameof(MarkMeshDirty))] private Vector4 constantUv2;

        /// <summary>Constant TEXCOORD3 value written to every glyph vertex.</summary>
        [Tooltip("Constant value written to TEXCOORD3 of every glyph vertex in the sub-mesh.")]
        [SerializeField, StateProperty(nameof(MarkMeshDirty))] private Vector4 constantUv3;

        /// <summary>Optional em-space expansion applied to emitted glyph quads.</summary>
        [Tooltip("Expand each glyph quad outward by this many em-units so shader effects (glow, edge, etc.) " +
                 "aren't clipped by the glyph's tight bounding box. Negative = read from shader's " +
                 "_UniTextMeshPadding property (shader-driven default).")]
        [SerializeField, StateProperty(nameof(ApplyQuadPaddingChange))] private float quadPaddingOverride = -1f;

        /// <summary>Whether the source material is cloned before UniText binds runtime state.</summary>
        [Tooltip("When true (default) the material is cloned via UniTextCustomMaterialCache so runtime " +
                 "keyword / texture changes are isolated from the source asset and other usages. " +
                 "When false the source material is used directly — shader property edits (material.SetColor " +
                 "etc.) are reflected immediately across all MaterialModifiers that reference it. " +
                 "The atlas textures are bound automatically in both modes.")]
        [SerializeField, StateProperty(nameof(ApplyCloneMaterialChange))] private bool cloneMaterial = true;

        /// <summary>Optional per-glyph data writer. Takes precedence over <see cref="OnWriteGlyphUV"/>.</summary>
        [NonSerialized] public MaterialGlyphWriter glyphDataWriter;

        private void ApplyMaterialChange()
        {
            shaderValidationLogged = false;
            ValidateShader();
            ChangeSink?.MarkModifierChanged(this, UniTextDirty.Mesh);
        }

        private void ApplyCloneMaterialChange()
            => ChangeSink?.MarkModifierChanged(this, UniTextDirty.Mesh);

        private void ApplyQuadPaddingChange(float previous, ref float current)
        {
            if (Mathf.Approximately(previous, current)) current = previous;
            else MarkMeshDirty();
        }

        /// <summary>Color multiplied into vertices emitted for this modifier.</summary>
        [SerializeField, Parameter, StateProperty(nameof(MarkMeshDirty))] private Color32 tint = new Color32(255, 255, 255, 255);

        private struct EffectRange
        {
            public int start;
            public int end;
            public Color32 tint;
            public bool hasTint;
            public string parameter;
        }

        private PooledList<EffectRange> ranges;

        private bool shaderValid;
        private bool shaderValidationLogged;

        private int glyphIndexInSelection;
        private int emittedCountThisFrame;
        private EffectRange currentActiveRange;
        private bool hasCurrentActiveRange;

        private bool writesUv2;
        private bool writesUv3;

        private float activePadding;

        private MaterialCloneRef<CustomKey> textClone;

        protected override bool ShouldIncludeCurrentGlyph()
        {
            if (!shaderValid) return false;

            var gen = uniText.MeshGenerator;
            var cluster = gen.currentCluster;
            var count = ranges.buffer.count;
            var data = ranges.buffer.data;

            for (var i = 0; i < count; i++)
            {
                ref var r = ref data[i];
                if (cluster < r.start || cluster >= r.end) continue;

                currentActiveRange = r;
                hasCurrentActiveRange = true;
                glyphIndexInSelection = emittedCountThisFrame;
                return true;
            }

            hasCurrentActiveRange = false;
            return false;
        }

        protected override Material GetMaterial()
        {
            if (!shaderValid) return null;

            if (material == null) { textClone.Release(); return null; }
            if (!cloneMaterial)
            {
                textClone.Release();
                return UniTextCustomMaterialCache.Instance.BindSourceDirect(material);
            }
            var textKey = new CustomKey(ObjectUtils.GetInstanceIdCompat(material));
            return textClone.Bind(UniTextCustomMaterialCache.Instance, textKey, material);
        }

        protected override int GetSortIndex() => sortIndex;

        protected override void OnQuadAppended(int vertexStart, UniTextMeshGenerator gen)
        {
            if (activePadding > 0f)
                ExpandSubMeshQuad(vertexStart, activePadding);

            if (hasCurrentActiveRange && currentActiveRange.hasTint)
                TintSubMeshQuad(vertexStart, currentActiveRange.tint);

            if (renderOrder == MaterialRenderOrder.Replace)
                gen.baseFaceClaimed = true;
            else if (hasCurrentActiveRange && currentActiveRange.hasTint)
                TintFaceQuad(gen, currentActiveRange.tint);

            if (writesUv2 || writesUv3)
            {
                Vector4 uv2 = constantUv2;
                Vector4 uv3 = constantUv3;

                if (glyphDataWriter != null || UseVirtualWriter)
                {
                    var ctx = new MaterialGlyphContext(
                        cluster: gen.currentCluster,
                        glyphIndexInSelection: glyphIndexInSelection,
                        cursorX: gen.cursorX,
                        baselineY: gen.baselineY,
                        glyphHeight: gen.height,
                        parameter: hasCurrentActiveRange ? currentActiveRange.parameter : null);

                    if (glyphDataWriter != null)
                        glyphDataWriter(in ctx, out uv2, out uv3);
                    else
                        OnWriteGlyphUV(in ctx, out uv2, out uv3);
                }

                if (writesUv2) SetSubMeshUv2Quad(vertexStart, uv2);
                if (writesUv3) SetSubMeshUv3Quad(vertexStart, uv3);
            }

            emittedCountThisFrame++;
        }

        /// <summary>
        /// Override to write per-glyph data into the sub-mesh's TEXCOORD2 / TEXCOORD3. Called once per
        /// included glyph. Default implementation returns <see cref="constantUv2"/> / <see cref="constantUv3"/>.
        /// </summary>
        /// <remarks>
        /// Ignored when <see cref="glyphDataWriter"/> is set. Marked <c>virtual</c> instead of <c>abstract</c>
        /// so that a basic <see cref="MaterialModifier"/> with only inspector-level configuration is usable
        /// as-is without subclassing.
        /// </remarks>
        protected virtual void OnWriteGlyphUV(in MaterialGlyphContext ctx, out Vector4 uv2, out Vector4 uv3)
        {
            uv2 = constantUv2;
            uv3 = constantUv3;
        }

        /// <summary>When true, <see cref="OnQuadAppended"/> calls <see cref="OnWriteGlyphUV"/> even if
        /// <see cref="glyphDataWriter"/> is null. Set to <see langword="true"/> in subclasses that override
        /// <see cref="OnWriteGlyphUV"/>.</summary>
        protected virtual bool UseVirtualWriter => false;

        protected override void OnEnable()
        {
            ranges ??= new PooledList<EffectRange>();
            ranges.FakeClear();
            base.OnEnable();
        }

        protected override void OnBeforeRebuild()
        {
            emittedCountThisFrame = 0;
            hasCurrentActiveRange = false;

            var useWriter = glyphDataWriter != null || UseVirtualWriter;
            writesUv2 = useWriter || IsNonZero(constantUv2);
            writesUv3 = useWriter || IsNonZero(constantUv3);
        }

        protected internal override void PrepareForParallel()
        {
            base.PrepareForParallel();
            ValidateShader();
            activePadding = ResolveQuadPadding();
        }

        private float ResolveQuadPadding()
        {
            if (quadPaddingOverride >= 0f) return quadPaddingOverride;
            if (material != null && material.HasProperty(ShaderIds.Custom.MeshPadding))
                return Mathf.Max(0f, material.GetFloat(ShaderIds.Custom.MeshPadding));
            return 0f;
        }

        private static bool IsNonZero(Vector4 v) =>
            v.x != 0f || v.y != 0f || v.z != 0f || v.w != 0f;

        protected override void OnDestroy()
        {
            textClone.Release();
            ranges?.Return();
            ranges = null;
            base.OnDestroy();
        }

        protected override void BeforeApply() => ranges?.FakeClear();

        protected override void OnApply(in RangeApplyContext context)
        {
            var range = new EffectRange
            {
                start = context.Segment.Range.start,
                end = context.Segment.Range.End,
                parameter = context.Parameters.ResolvedValue,
            };

            range.tint = Param.Tint.Resolve(this, in context);
            range.hasTint = range.tint.r != 255 || range.tint.g != 255 || range.tint.b != 255 || range.tint.a != 255;

            ranges.Add(range);
        }

        private static void TintFaceQuad(UniTextMeshGenerator gen, Color32 tint)
        {
            var baseIdx = gen.faceBaseIdx;
            if (baseIdx < 0) return;

            MultiplyQuadColor(gen.Colors, baseIdx, tint);
        }

        private void TintSubMeshQuad(int vertexStart, Color32 tint)
        {
            MultiplyQuadColor(GetSubMeshColors(), vertexStart, tint);
        }

        private static void MultiplyQuadColor(Color32[] colors, int baseIdx, Color32 tint)
        {
            for (var i = 0; i < 4; i++)
            {
                ref var c = ref colors[baseIdx + i];
                c.r = (byte)(c.r * tint.r / 255);
                c.g = (byte)(c.g * tint.g / 255);
                c.b = (byte)(c.b * tint.b / 255);
                c.a = (byte)(c.a * tint.a / 255);
            }
        }

        private void ValidateShader()
        {
            shaderValid = false;

            if (material == null)
            {
                if (!shaderValidationLogged)
                {
                    Debug.LogError($"[MaterialModifier] material is not assigned on {uniText?.name}.");
                    shaderValidationLogged = true;
                }
                return;
            }

            shaderValid = true;
            shaderValidationLogged = false;
        }
    }
}
