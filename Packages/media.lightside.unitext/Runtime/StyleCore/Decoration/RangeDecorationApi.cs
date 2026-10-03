using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>Corner mask used by built-in and custom rounded range decorations.</summary>
    [Flags]
    public enum RangeDecorationCorners : byte
    {
        /// <summary>No corners are rounded.</summary>
        None = 0,
        /// <summary>Round the top-left corner.</summary>
        TopLeft = 1,
        /// <summary>Round the top-right corner.</summary>
        TopRight = 2,
        /// <summary>Round the bottom-right corner.</summary>
        BottomRight = 4,
        /// <summary>Round the bottom-left corner.</summary>
        BottomLeft = 8,
        /// <summary>Round both left corners.</summary>
        Left = TopLeft | BottomLeft,
        /// <summary>Round both right corners.</summary>
        Right = TopRight | BottomRight,
        /// <summary>Round all corners.</summary>
        All = TopLeft | TopRight | BottomRight | BottomLeft
    }

    /// <summary>Conventional draw-order anchors shared by built-in and custom range decorations.</summary>
    public static class RangeDecorationPriorities
    {
        /// <summary>Interactive state feedback below document annotations.</summary>
        public const int Interactive = -10;
        /// <summary>Default priority for authored and project-defined decorations.</summary>
        public const int Custom = 0;
        /// <summary>Text selection.</summary>
        public const int Selection = 100;
        /// <summary>Current search result or comparable active document annotation.</summary>
        public const int FindCurrent = 200;
        /// <summary>Spell, grammar, lint or comparable diagnostic presentation.</summary>
        public const int Diagnostics = 300;
    }

    internal interface IRangeDecorationMeshOwner
    {
        RangeDecorationMesh GetMesh(RangeDecorationOrder order);
    }

    /// <summary>How final mesh deformation relates to one visual range fragment.</summary>
    public enum RangeVisualTransformKind : byte
    {
        /// <summary>The fragment kept its layout geometry.</summary>
        Identity,
        /// <summary>Every glyph in the fragment shares one resolved transform.</summary>
        Uniform,
        /// <summary>
        /// Glyphs have different transforms. <see cref="RangeVisualFragment.Bounds"/> remains a
        /// conservative final-space envelope; consume visual glyphs for their exact topology.
        /// </summary>
        NonUniform,
    }

    /// <summary>
    /// Maps component-local layout coordinates to the final glyph quad after mesh modifiers. The
    /// mapping is bilinear, so it also represents custom non-parallelogram face deformation.
    /// </summary>
    public readonly struct RangeVisualTransform
    {
        private readonly GlyphFaceTransform value;

        /// <summary>Maps one component-local layout point to its final component-local position.</summary>
        public Vector2 TransformPoint(Vector2 point) => value.TransformPoint(point);

        internal bool IsIdentity => value.IsIdentity;
        internal GlyphFace Final => value.final;

        internal RangeVisualTransform(in GlyphVisualGeometry geometry)
        {
            value = geometry.transform;
        }

        internal bool EquivalentTo(in RangeVisualTransform other)
            => value.EquivalentTo(in other.value);

        internal Rect TransformBounds(Rect bounds) => value.TransformBounds(bounds);
    }

    /// <summary>One rendered primary or modifier-injected glyph face after every mesh modifier has run.</summary>
    public readonly struct RangeVisualGlyph
    {
        private readonly RangeVisualTransform transform;

        /// <summary>Rendered codepoint cluster represented by this glyph.</summary>
        public int Cluster { get; }

        /// <summary>Layout line containing the source positioned glyph.</summary>
        public int LineIndex { get; }

        /// <summary>Final bottom-left face corner in component-local coordinates.</summary>
        public Vector2 BottomLeft => transform.Final.bottomLeft;

        /// <summary>Final top-left face corner in component-local coordinates.</summary>
        public Vector2 TopLeft => transform.Final.topLeft;

        /// <summary>Final top-right face corner in component-local coordinates.</summary>
        public Vector2 TopRight => transform.Final.topRight;

        /// <summary>Final bottom-right face corner in component-local coordinates.</summary>
        public Vector2 BottomRight => transform.Final.bottomRight;

        /// <summary>Layout-to-final transform for this glyph face.</summary>
        public RangeVisualTransform Transform => transform;

        internal RangeVisualGlyph(in GlyphVisualGeometry geometry, int lineIndex)
        {
            Cluster = geometry.cluster;
            LineIndex = lineIndex;
            transform = new RangeVisualTransform(in geometry);
        }
    }

    /// <summary>One final visual fragment of a logical text range.</summary>
    public readonly struct RangeVisualFragment
    {
        /// <summary>Final component-local axis-aligned bounds after mesh deformation.</summary>
        public Rect Bounds { get; }

        /// <summary>Component-local bounds before mesh deformation.</summary>
        public Rect LayoutBounds { get; }

        /// <summary>Whether one transform can represent every glyph in this fragment.</summary>
        public RangeVisualTransformKind TransformKind { get; }

        /// <summary>
        /// Resolved layout-to-final transform. Valid for Identity and Uniform fragments. For a
        /// NonUniform fragment, use <see cref="Bounds"/> as an envelope or request visual glyphs
        /// when exact topology matters.
        /// </summary>
        public RangeVisualTransform Transform { get; }

        /// <summary>Layout line containing this fragment.</summary>
        public int LineIndex { get; }

        /// <summary>Resolved direction of the fragment's shaping run.</summary>
        public bool IsRightToLeft { get; }

        /// <summary>Whether this fragment contains the logical start of the selected range.</summary>
        public bool ContainsRangeStart { get; }

        /// <summary>Whether this fragment contains the logical end of the selected range.</summary>
        public bool ContainsRangeEnd { get; }

        /// <summary>Physical side on which the logical range start lies.</summary>
        public bool RangeStartOnRight { get; }

        /// <summary>Physical side on which the logical range end lies.</summary>
        public bool RangeEndOnRight { get; }

        /// <summary>First selected cluster represented by this fragment.</summary>
        public int ClusterStart { get; }

        /// <summary>Exclusive selected cluster end represented by this fragment.</summary>
        public int ClusterEnd { get; }

        internal RangeVisualFragment(Rect bounds, int lineIndex, bool isRightToLeft,
            bool containsRangeStart, bool containsRangeEnd, bool rangeStartOnRight,
            bool rangeEndOnRight, int clusterStart, int clusterEnd)
            : this(bounds, bounds, RangeVisualTransformKind.Identity, default, lineIndex,
                isRightToLeft, containsRangeStart, containsRangeEnd, rangeStartOnRight,
                rangeEndOnRight, clusterStart, clusterEnd)
        {
        }

        internal RangeVisualFragment(Rect layoutBounds, Rect bounds,
            RangeVisualTransformKind transformKind, RangeVisualTransform transform,
            int lineIndex, bool isRightToLeft, bool containsRangeStart, bool containsRangeEnd,
            bool rangeStartOnRight, bool rangeEndOnRight, int clusterStart, int clusterEnd)
        {
            Bounds = bounds;
            LayoutBounds = layoutBounds;
            TransformKind = transformKind;
            Transform = transform;
            LineIndex = lineIndex;
            IsRightToLeft = isRightToLeft;
            ContainsRangeStart = containsRangeStart;
            ContainsRangeEnd = containsRangeEnd;
            RangeStartOnRight = rangeStartOnRight;
            RangeEndOnRight = rangeEndOnRight;
            ClusterStart = clusterStart;
            ClusterEnd = clusterEnd;
        }
    }

    /// <summary>
    /// Immutable logical range data supplied while a range-decoration modifier builds final geometry.
    /// Fragment spans are borrowed scratch views: consume one before requesting another.
    /// </summary>
    public readonly ref struct RangeDecorationContext
    {
        private readonly RangeGeometryIndex geometry;

        internal RangeGeometryIndex Geometry => geometry;

        /// <summary>Stable source-scoped identity shared by every segment of the entity.</summary>
        public RangeIdentity Identity { get; }

        /// <summary>The entity segment being decorated, in rendered codepoint space.</summary>
        public RangeSegment Segment { get; }

        /// <summary>Optional semantic channel associated with the entity.</summary>
        public RangeChannel Channel { get; }

        /// <summary>Optional immutable semantic value associated with the entity.</summary>
        public RangePayloadView Payload { get; }

        /// <summary>The range source that owns this entity.</summary>
        public RangeSource Source { get; }

        /// <summary>The rendered-text revision against which this segment is valid.</summary>
        public TextRevision Revision { get; }

        /// <summary>Explicit and source-default presentation parameters for this modifier node.</summary>
        public ModifierParameters Parameters { get; }

        /// <summary>Normalized weight contributed by a transient modifier application.</summary>
        public float RuleWeight { get; }

        internal RangeDecorationContext(RangeGeometryIndex geometry,
            in RangeDecorationApplication application)
        {
            this.geometry = geometry;
            Identity = application.identity;
            Segment = application.segment;
            Channel = application.channel;
            Payload = new RangePayloadView(application.payload);
            Source = application.source;
            Revision = application.revision;
            Parameters = application.parameters;
            RuleWeight = application.ruleWeight;
        }

        /// <summary>
        /// Returns final per-line/BiDi fragments at the requested vertical extent. The span stays valid
        /// only until the next fragment query made through this context.
        /// </summary>
        public ReadOnlySpan<RangeVisualFragment> LineFragments(RangeHeight height = RangeHeight.LineBox)
            => geometry.GetLineFragments(Segment.Range.start, Segment.Range.End, height);

        /// <summary>
        /// Returns one final fragment per shaped cluster group. Ligatures and multiple glyphs belonging
        /// to one cluster produce one fragment. The span stays valid only until the next fragment query.
        /// </summary>
        public ReadOnlySpan<RangeVisualFragment> GlyphFragments(RangeHeight height = RangeHeight.Content)
            => geometry.GetGlyphFragments(Segment.Range.start, Segment.Range.End, height);

        /// <summary>
        /// Returns final face quads for every rendered primary or modifier-injected glyph in this segment. Use this
        /// finer view when <see cref="RangeVisualFragment.TransformKind"/> is NonUniform. The span
        /// stays valid only until the next fragment or glyph query through this context.
        /// </summary>
        public ReadOnlySpan<RangeVisualGlyph> VisualGlyphs()
            => geometry.GetVisualGlyphs(Segment.Range.start, Segment.Range.End);

        /// <summary>Returns the union bounds of the entire rendered text block at the requested height.</summary>
        public Rect TextBlockBounds(RangeHeight height = RangeHeight.LineBox)
            => geometry.GetTextBlockBounds(height);
    }

    /// <summary>A resolved paint together with the gradient-atlas row owned by its modifier.</summary>
    public readonly struct RangeDecorationPaint
    {
        private readonly TextPaint value;
        private readonly int rampRow;

        /// <summary>Resolved paint kind.</summary>
        public PaintSourceKind Kind => value.kind;

        /// <summary>Resolved solid colour or gradient/texture tint.</summary>
        public Color32 Color => value.color;

        /// <summary>Resolved projection mapping.</summary>
        public PaintMapping Mapping => value.mapping;

        /// <summary>Resolved gradient shape.</summary>
        public PaintProjectionKind Shape => value.shape;

        /// <summary>Resolved texture fitting mode.</summary>
        public PaintFit Fit => value.fit;

        /// <summary>Resolved compositing mode.</summary>
        public LayerBlend Blend => value.blend;

        internal TextPaint Value => value;
        internal int RampRow => rampRow;

        internal RangeDecorationPaint(in TextPaint value, int rampRow)
        {
            this.value = value;
            this.rampRow = rampRow;
        }
    }

    /// <summary>
    /// Synchronous writer for one modifier's owned range-decoration buffers. Geometry emitted with
    /// different texture paints or blend modes is split into render-key batches automatically.
    /// </summary>
    public ref struct RangeMeshWriter
    {
        private readonly IRangeDecorationMeshOwner owner;
        private RangeDecorationMesh mesh;
        private TextPaint paint;
        private PaintFrame frame;
        private int rampRow;
        private bool hasPaint;

        internal RangeMeshWriter(IRangeDecorationMeshOwner owner)
        {
            this.owner = owner;
            mesh = null;
            paint = default;
            frame = default;
            rampRow = -1;
            hasPaint = false;
        }

        /// <summary>
        /// Selects paint, projection frame and render side for subsequent vertices. Changing a texture
        /// closes the previous render batch while compatible consecutive output remains batched.
        /// </summary>
        public void SetPaint(in RangeDecorationPaint decorationPaint, in PaintFrame paintFrame,
            RangeDecorationOrder order = RangeDecorationOrder.Behind)
            => SetPaint(in decorationPaint, in paintFrame, order,
                new Color32(255, 255, 255, 255));

        /// <summary>Selects paint with a colour multiplier for subsequent vertices.</summary>
        public void SetPaint(in RangeDecorationPaint decorationPaint, in PaintFrame paintFrame,
            RangeDecorationOrder order, Color32 tint)
        {
            var target = owner.GetMesh(order);
            if (!ReferenceEquals(mesh, target))
                mesh?.CompleteBatch();
            mesh = target;
            paint = decorationPaint.Value;
            paint.ApplyTint(tint);
            frame = paintFrame;
            rampRow = decorationPaint.RampRow;
            mesh.Begin(paint.kind == PaintSourceKind.Texture ? paint.texture : null, paint.blend);
            hasPaint = true;
        }

        /// <summary>Builds the projection frame from bounds, then selects it for subsequent vertices.</summary>
        public void SetPaint(in RangeDecorationPaint decorationPaint, Rect mappingBounds,
            RangeDecorationOrder order = RangeDecorationOrder.Behind)
        {
            var value = decorationPaint.Value;
            var paintFrame = PaintMappingMath.BuildFrame(in value, mappingBounds);
            SetPaint(in decorationPaint, in paintFrame, order);
        }

        /// <summary>Builds the projection frame from bounds, then selects it with a colour multiplier.</summary>
        public void SetPaint(in RangeDecorationPaint decorationPaint, Rect mappingBounds,
            RangeDecorationOrder order, Color32 tint)
        {
            var value = decorationPaint.Value;
            var paintFrame = PaintMappingMath.BuildFrame(in value, mappingBounds);
            SetPaint(in decorationPaint, in paintFrame, order, tint);
        }

        /// <summary>Adds one painted vertex and returns its writer-local index.</summary>
        public int AddVertex(Vector2 position) => AddVertex(position, new Color32(255, 255, 255, 255));

        /// <summary>Adds one painted vertex with a colour multiplier and returns its writer-local index.</summary>
        public int AddVertex(Vector2 position, Color32 tint)
            => AddVertex(position, tint, default, default);

        /// <summary>
        /// Adds a vertex carrying analytic edge coverage. Use 1 on the contour and 0 on a narrow
        /// exterior fringe; the decoration shader converts the interpolation into a screen-space
        /// antialiased edge with derivatives.
        /// </summary>
        public int AddCoverageVertex(Vector2 position, float coverage)
            => AddVertex(position, new Color32(255, 255, 255, 255), default,
                new Vector4(0f, 0f, -1f, Mathf.Clamp01(coverage)));

        /// <summary>Adds a triangle referencing vertices emitted under the current paint batch.</summary>
        public void AddTriangle(int a, int b, int c)
        {
            RequirePaint();
            mesh.AddTriangle(a, b, c);
        }

        /// <summary>Adds a flat painted rectangle.</summary>
        public void AddRect(Rect bounds) => AddRoundedRect(bounds, 0f, RangeDecorationCorners.None);

        /// <summary>Adds a painted rounded rectangle with independently selectable corners.</summary>
        public void AddRoundedRect(Rect bounds, float cornerRadius,
            RangeDecorationCorners corners = RangeDecorationCorners.All)
            => AddRoundedRectSection(bounds, bounds, cornerRadius, corners);

        /// <summary>Adds a rounded rectangle mapped through one resolved final glyph transform.</summary>
        public void AddRoundedRect(Rect bounds, in RangeVisualTransform transform,
            float cornerRadius, RangeDecorationCorners corners = RangeDecorationCorners.All)
            => AddRoundedRectSection(bounds, bounds, in transform, cornerRadius, corners);

        /// <summary>
        /// Adds one rectangular section of a larger rounded box. Shape coordinates remain relative to
        /// <paramref name="fullBounds"/>, allowing a box to be subdivided for independent paint frames
        /// without introducing rounding seams.
        /// </summary>
        public void AddRoundedRectSection(Rect fullBounds, Rect sectionBounds, float cornerRadius,
            RangeDecorationCorners corners = RangeDecorationCorners.All)
            => AddRoundedRectSection(fullBounds, sectionBounds, default, false, cornerRadius, corners);

        /// <summary>
        /// Adds one section of a rounded rectangle after applying a resolved glyph transform.
        /// Shape coordinates stay in the undeformed rectangle, preserving corner radius and Paint
        /// interpolation across affine and bilinear quad deformation.
        /// </summary>
        public void AddRoundedRectSection(Rect fullBounds, Rect sectionBounds,
            in RangeVisualTransform transform, float cornerRadius,
            RangeDecorationCorners corners = RangeDecorationCorners.All)
            => AddRoundedRectSection(fullBounds, sectionBounds, in transform, true, cornerRadius, corners);

        private void AddRoundedRectSection(Rect fullBounds, Rect sectionBounds,
            in RangeVisualTransform transform, bool applyTransform, float cornerRadius,
            RangeDecorationCorners corners)
        {
            RequirePaint();
            if (sectionBounds.width <= 0f || sectionBounds.height <= 0f) return;
            if (sectionBounds.xMin < fullBounds.xMin || sectionBounds.xMax > fullBounds.xMax ||
                sectionBounds.yMin < fullBounds.yMin || sectionBounds.yMax > fullBounds.yMax)
                throw new ArgumentOutOfRangeException(nameof(sectionBounds),
                    "A rounded-box section must stay inside its full bounds.");

            var halfW = fullBounds.width * 0.5f;
            var halfH = fullBounds.height * 0.5f;
            var radius = Mathf.Min(Mathf.Max(0f, cornerRadius), Mathf.Min(halfW, halfH));
            var rounded = radius > 0f && corners != RangeDecorationCorners.None
                ? new Vector4(halfW, halfH, radius, (int)corners)
                : default;

            var invW = fullBounds.width > 0f ? 1f / fullBounds.width : 0f;
            var invH = fullBounds.height > 0f ? 1f / fullBounds.height : 0f;
            var u0 = (sectionBounds.xMin - fullBounds.xMin) * invW;
            var u1 = (sectionBounds.xMax - fullBounds.xMin) * invW;
            var v0 = (sectionBounds.yMin - fullBounds.yMin) * invH;
            var v1 = (sectionBounds.yMax - fullBounds.yMin) * invH;

            var white = new Color32(255, 255, 255, 255);
            var blPosition = new Vector2(sectionBounds.xMin, sectionBounds.yMin);
            var tlPosition = new Vector2(sectionBounds.xMin, sectionBounds.yMax);
            var trPosition = new Vector2(sectionBounds.xMax, sectionBounds.yMax);
            var brPosition = new Vector2(sectionBounds.xMax, sectionBounds.yMin);
            if (applyTransform)
            {
                blPosition = transform.TransformPoint(blPosition);
                tlPosition = transform.TransformPoint(tlPosition);
                trPosition = transform.TransformPoint(trPosition);
                brPosition = transform.TransformPoint(brPosition);
            }

            var bl = AddVertex(blPosition, white,
                new Vector4(u0, v0), rounded);
            var tl = AddVertex(tlPosition, white,
                new Vector4(u0, v1), rounded);
            var tr = AddVertex(trPosition, white,
                new Vector4(u1, v1), rounded);
            var br = AddVertex(brPosition, white,
                new Vector4(u1, v0), rounded);
            mesh.AddTriangle(bl, tl, tr);
            mesh.AddTriangle(tr, br, bl);
        }

        internal void Complete()
        {
            mesh?.CompleteBatch();
            mesh = null;
            hasPaint = false;
        }

        private int AddVertex(Vector2 position, Color32 tint, Vector4 shape, Vector4 rounded)
        {
            RequirePaint();
            var color = TextPaint.MultiplyColor(paint.color, tint);
            var kind = CoverageQuadOps.PaintKindCode(in paint);
            var coord = kind == 0f
                ? default
                : PaintProjectionMath.Coord(in frame, position.x, position.y);
            var half = new Vector2(rounded.x, rounded.y);
            var radius = rounded.z > 0f ? rounded.z : 0f;
            var mask = Mathf.RoundToInt(rounded.w);

            var analytic = half.x > 0f && half.y > 0f;
            var vertexCoverage = rounded.z < -0.5f ? rounded.w : 1f;

            return mesh.AddVertex(new RangeDecorationVertex
            {
                position = new Vector3(position.x, position.y),
                color = color,
                uv0 = new Vector4((shape.x - 0.5f) * half.x * 2f, (shape.y - 0.5f) * half.y * 2f,
                    half.x, half.y),
                uv1 = new Vector4(0f, 0f, 0f, LightSideSurface.Pack(LightSideSurfaceKind.Shape)),
                uv2 = analytic
                    ? new Vector4(16f * (float)ShapeKind.RoundedRect, 0f, 0f, 0f)
                    : new Vector4(LightSideShapeCoverageMode.VertexCoverage, vertexCoverage, 0f, 0f),
                uv3 = new Vector4(coord.x, coord.y, rampRow, kind),
                tangent = new Vector4(
                    (mask & 1) != 0 ? radius : 0f,
                    (mask & 2) != 0 ? radius : 0f,
                    (mask & 4) != 0 ? radius : 0f,
                    (mask & 8) != 0 ? radius : 0f),
            });
        }

        private void RequirePaint()
        {
            if (!hasPaint)
                throw new InvalidOperationException("SetPaint must be called before emitting range-decoration geometry.");
        }
    }
}
