using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    internal sealed class FontRuntimeSlot : IDisposable
    {
        private readonly object gate = new();
        private Func<UniTextFont.Core> factory;
        private UniTextFont.Core runtime;
        private bool initialized;
        private bool disposed;

        internal FontRuntimeSlot(Func<UniTextFont.Core> factory)
            => this.factory = factory ?? throw new ArgumentNullException(nameof(factory));

        internal FontRuntimeSlot(UniTextFont.Core runtime)
        {
            this.runtime = runtime;
            initialized = true;
        }

        internal UniTextFont.Core ExistingRuntime => Volatile.Read(ref runtime);
        internal bool IsMaterialized => Volatile.Read(ref initialized);

        internal UniTextFont.Core Runtime
        {
            get
            {
                if (Volatile.Read(ref disposed))
                    throw new ObjectDisposedException(nameof(FontRuntimeSlot));
                if (Volatile.Read(ref initialized)) return Volatile.Read(ref runtime);
                lock (gate)
                {
                    if (disposed) throw new ObjectDisposedException(nameof(FontRuntimeSlot));
                    if (initialized) return runtime;
                    var created = factory();
                    factory = null;
                    Volatile.Write(ref runtime, created);
                    Volatile.Write(ref initialized, true);
                    return created;
                }
            }
        }

        internal void ReplaceFactory(Func<UniTextFont.Core> value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(FontRuntimeSlot));
                if (!initialized) factory = value;
            }
        }

        public void Dispose()
        {
            UniTextFont.Core previous;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                previous = runtime;
                runtime = null;
                factory = null;
                Volatile.Write(ref initialized, true);
            }
            previous?.Dispose();
        }
    }

#if UNITY_EDITOR || UNITY_WEBGL
    internal sealed class CompressedArrayFontSource : FontSource
    {
        private static long nextIdentity;
        private readonly object gate = new();
        private byte[] stored;
        private readonly string identity;
        private readonly int length;
        private byte[] data;
        private ArrayFontSource source;

        internal CompressedArrayFontSource(byte[] stored)
        {
            this.stored = stored ?? throw new ArgumentNullException(nameof(stored));
            if (!Zstd.IsCompressed(stored))
                throw new ArgumentException("Font data is not Zstd-compressed.", nameof(stored));
            length = checked((int)Zstd.GetFrameContentSize(stored));
            if (length <= 0)
                throw new InvalidOperationException("Compressed font source is empty.");
            identity = $"compressed-array:{Interlocked.Increment(ref nextIdentity):X16}";
        }

        internal override string Identity => identity;
        internal override int Length => length;
        internal override long OwnedByteCount
            => (Volatile.Read(ref stored)?.LongLength ?? 0L)
               + (Volatile.Read(ref data)?.LongLength ?? 0L);
        internal override FontBackingLease Open() => GetOrCreateSource().Open();

        internal byte[] GetOrCreateBytes()
        {
            var current = Volatile.Read(ref data);
            if (current != null) return current;
            lock (gate)
            {
                return GetOrCreateBytesLocked();
            }
        }

        private ArrayFontSource GetOrCreateSource()
        {
            var current = Volatile.Read(ref source);
            if (current != null) return current;
            lock (gate)
            {
                if (source != null) return source;
                current = new ArrayFontSource(GetOrCreateBytesLocked());
                Volatile.Write(ref source, current);
                return current;
            }
        }

        private byte[] GetOrCreateBytesLocked()
        {
            if (data != null) return data;
            var current = Zstd.Decompress(stored);
            Volatile.Write(ref data, current);
#if !UNITY_EDITOR
            Volatile.Write(ref stored, null);
#endif
            return current;
        }
    }
#endif

    /// <summary>
    /// Serialized font definition that owns face and rendering metadata and resolves its immutable source lazily.
    /// Embedded sources stay in the asset in the Editor and WebGL; other players resolve build-generated disk-backed
    /// sources. The <see cref="Core"/> materializes only when glyph or shaping work first needs this face.
    /// </summary>
    [Serializable]
    [StateSource]
    public partial class UniTextFont : ScriptableObject
    {
        #region Serialized Fields

#if UNITY_EDITOR || UNITY_WEBGL
        [SerializeField, StatePassive]
        [Tooltip("Hidden sub-asset carrying the compressed font file data (TTF/OTF bytes).")]
        private UniTextFontPayload payload;
#endif

        [SerializeField, StateField(nameof(RebuildRuntime))]
        [Tooltip("Hash of font data for identification.")]
        protected int fontDataHash;

        [SerializeField, HideInInspector, StatePassive]
        private byte[] payloadSourceHash;

        [SerializeField, HideInInspector, StatePassive]
        private int payloadRawLength;

        /// <summary>Synthetic-italic slant used when a font stores none (value 0), notably OS system-font fallbacks built at runtime. The italic modifier reads it as a horizontal shear in percent of height.</summary>
        public const float DefaultItalicStyle = 30f;

        [SerializeField, StateField(nameof(ApplyRuntimeMetricsChange))]
        [Tooltip("Synthetic italic slant, in percent of height (30 leans the top right by 0.30 of the height, ~16.7°, not 30°). 0 falls back to the default slant; negative slants lean the other way.")]
        private float italicStyle = DefaultItalicStyle;

        [SerializeField, StateField(nameof(ApplyRuntimeMetricsChange))]
        [Tooltip("Baseline spacing added to every glyph's advance during shaping, in font design units (relative to UnitsPerEm). Use to compensate for fonts that render too tight or too loose by design. Layered with style-level letter-spacing.")]
        private int spacingOffset;

        /// <summary>Sentinel for <see cref="spaceAdvance"/> meaning "not initialized from the font yet".</summary>
        internal const int SpaceAdvanceUninitialized = -100;

        [SerializeField, StateField(nameof(ApplyRuntimeMetricsChange))]
        [Tooltip("Advance width of the space U+0020 for this font, in design units (relative to UnitsPerEm). Replaces the width HarfBuzz reports — including its synthesized fallback when the font has no space glyph. -100 = auto: filled from the font (or HarfBuzz's quarter-em fallback) on first load.")]
        private int spaceAdvance = SpaceAdvanceUninitialized;

        [SerializeField, StateField(nameof(ApplyRuntimeMetricsChange))]
        [Tooltip("Baseline synthetic bold applied to every glyph of this font. Measured in CSS weight steps from the font's own weight (1 ≈ Regular → Bold). Renders thicker via SDF dilate and compensates glyph advance to keep layout consistent. Use to give light/regular faces extra weight when a true bold cut is unavailable.")]
        [Range(0f, 2f)]
        private float fakeBoldWeight;

        [SerializeField, StateField(nameof(RebuildRuntime))]
        [Tooltip("Font face metrics (ascender, descender, line height, etc.).")]
        internal FaceInfo faceInfo;

        [SerializeField, StateField(nameof(ApplyUnitsPerEmChange))]
        [Tooltip("Font design units per em (typically 1000 or 2048).")]
        internal int unitsPerEm = 1000;

        [SerializeField, StateField(nameof(ApplyFontScaleChange))]
        [Tooltip("Visual scale multiplier for this font. Use to normalize fonts that appear too small or too large by design (e.g. Dongle). Applied after all metric conversions.")]
        [Range(0.1f, 5f)]
        internal float fontScale = 1f;

        [SerializeField, StateField(nameof(ApplyRuntimeMetricsChange))]
        [Tooltip("Include this font in font-size normalization (matching its x-height/cap-height to the primary font). Disable for decorative fonts whose proportions are intentional.")]
        internal bool participatesInNormalization = true;

        [SerializeField, StateField(nameof(ApplySdfDetailMultiplierChange))]
        [Tooltip("SDF tile detail multiplier. Higher values force larger tiles for better quality on fonts with thin strokes (e.g. calligraphic). Default 1.0.")]
        [Range(0.25f, 8f)]
        internal float sdfDetailMultiplier = 1f;

        [SerializeField, StateField(nameof(ApplyTileSizeOffsetChange))]
        [Tooltip("Step offset applied after tile classification along the {64, 128, 256} hierarchy. +1 picks the next larger tile, -1 the next smaller. Clamped at hierarchy bounds. Per-glyph overrides ignore this offset.")]
        [Range(-2, 2)]
        internal int tileSizeOffset;

        /// <summary>Per-glyph overrides for one glyph index: SDF tile size plus advance, position and size adjustments applied wherever this glyph renders in the font.</summary>
        [Serializable]
        public struct GlyphOverride
        {
            public uint glyphIndex;
            [Tooltip("0 = auto, 64/128/256 = forced tile size.")]
            public int tileSizeOverride;
            [Tooltip("Advance (layout width) multiplier. 0 or 1 = unchanged; e.g. 0.3 shrinks an over-wide glyph such as an icon-font space. Affects line breaking and caret positions.")]
            public float advanceScale;
            [VectorDragField]
            [Tooltip("Render shift in em. Visual only — does not change layout width. Y positive = up.")]
            public Vector2 offset;
            [Tooltip("Render size multiplier around the glyph's pen position. 0 or 1 = unchanged. Visual only.")]
            public float scale;
        }

        /// <summary>Per-glyph raster tile-size overrides.</summary>
        [SerializeField, StateList(nameof(RebuildGlyphOverrides))]
        [Tooltip("Per-glyph tile size overrides for fine-tuning quality on specific glyphs.")]
        internal List<GlyphOverride> glyphOverrides;

        /// <summary>One variable-font axis pinned to a custom default value, keyed by its OpenType tag (4-char tag packed big-endian, same bits as the HarfBuzz uint tag).</summary>
        [Serializable]
        public struct AxisDefault
        {
            public int tag;
            public float value;
        }

        /// <summary>Default values overriding individual variable-font axes.</summary>
        [SerializeField, StateList(nameof(RebuildAxisDefaults))]
        [Tooltip("Custom default values for individual variable-font axes. Overrides the font's built-in fvar default for each listed axis; axes left out keep their fvar default. Drives rendering and shaping when no <var> tag is active, and is the base for <var> percentage/delta.")]
        private List<AxisDefault> axisDefaults;

        [Serializable]
        private struct VariableAxisMetadata
        {
            [SerializeField] internal int tag;
            [SerializeField] internal float minValue;
            [SerializeField] internal float defaultValue;
            [SerializeField] internal float maxValue;
        }

        [SerializeField, HideInInspector, StatePassive]
        private VariableAxisMetadata[] variableAxes;

#if UNITY_EDITOR
        [SerializeField, HideInInspector, StatePassive]
        private int faceCount;
#endif

        #endregion

        #region Runtime instance

        [NonSerialized] private Core runtime;
        [NonSerialized] private FontRuntimeSlot runtimeSlot;
#if UNITY_EDITOR || UNITY_WEBGL
        [NonSerialized] private byte[] embeddedRuntimeFontData;
#endif
        [NonSerialized] private FontSource embeddedRuntimeFontSource;
        [NonSerialized] private int cachedInstanceId;
        [NonSerialized] private string cachedName;
        [NonSerialized] private int metadataMutationDepth;

        private protected readonly struct RuntimeSnapshot
        {
            internal readonly FaceInfo faceInfo;
            internal readonly int unitsPerEm;
            internal readonly float fontScale;
            internal readonly bool participatesInNormalization;
            internal readonly float sdfDetailMultiplier;
            internal readonly int tileSizeOffset;
            internal readonly float italicStyle;
            internal readonly int spacingOffset;
            internal readonly int spaceAdvance;
            internal readonly float fakeBoldWeight;
            internal readonly GlyphOverride[] glyphOverrides;
            internal readonly string name;
            internal readonly AxisDefault[] axisDefaults;

            internal RuntimeSnapshot(FaceInfo faceInfo, int unitsPerEm, float fontScale,
                bool participatesInNormalization, float sdfDetailMultiplier,
                int tileSizeOffset, float italicStyle, int spacingOffset,
                int spaceAdvance, float fakeBoldWeight, GlyphOverride[] glyphOverrides,
                string name, AxisDefault[] axisDefaults)
            {
                this.faceInfo = faceInfo;
                this.unitsPerEm = unitsPerEm;
                this.fontScale = fontScale;
                this.participatesInNormalization = participatesInNormalization;
                this.sdfDetailMultiplier = sdfDetailMultiplier;
                this.tileSizeOffset = tileSizeOffset;
                this.italicStyle = italicStyle;
                this.spacingOffset = spacingOffset;
                this.spaceAdvance = spaceAdvance;
                this.fakeBoldWeight = fakeBoldWeight;
                this.glyphOverrides = glyphOverrides;
                this.name = name;
                this.axisDefaults = axisDefaults;
            }

            internal Core Create(FontSource source)
                => new Core(
                    source, faceInfo, unitsPerEm, fontScale, sdfDetailMultiplier,
                    tileSizeOffset, italicStyle, spacingOffset, fakeBoldWeight,
                    glyphOverrides, name, axisDefaults, spaceAdvance)
                { ParticipatesInNormalization = participatesInNormalization };
        }

        /// <summary>Lazy-built runtime that owns the FT face, glyph tables and atlas pipeline. First access after creation or source invalidation must occur on the main thread unless a provider has already captured the worker-safe slot.</summary>
        public Core Runtime
        {
            get
            {
                var slot = runtimeSlot;
                if (slot != null) return slot.Runtime;
                if (runtime != null) return runtime;
                runtime = CreateRuntime();
                return runtime;
            }
        }

        /// <summary>Subclass hook: produce the Core, or null if bytes unavailable.</summary>
        protected virtual Core CreateRuntime()
        {
            var source = CaptureEmbeddedFontSource();
            return source == null
                ? null
                : BuildRuntimeFromSource(source, typeof(UniTextFont));
        }

        internal virtual FontSource CaptureFontSource()
            => GetType() == typeof(UniTextFont)
                ? CaptureEmbeddedFontSource()
                : Runtime?.Source;

        /// <summary>True when this asset's serialized font payload must travel with Player and SBP content.</summary>
        protected internal virtual bool UsesEmbeddedSource => true;

        private protected FontSource CaptureEmbeddedFontSource()
        {
            if (embeddedRuntimeFontSource != null) return embeddedRuntimeFontSource;
#if UNITY_EDITOR || UNITY_WEBGL
            var bytes = embeddedRuntimeFontData ?? payload?.data;
            if (bytes == null || bytes.Length == 0) return null;
            if (Zstd.IsCompressed(bytes))
            {
                embeddedRuntimeFontSource = new CompressedArrayFontSource(bytes);
#if !UNITY_EDITOR
                payload.data = null;
#endif
                return embeddedRuntimeFontSource;
            }
            embeddedRuntimeFontData = bytes;
            return embeddedRuntimeFontSource = new ArrayFontSource(bytes);
#else
            if (fontDataHash == 0) return null;
            return embeddedRuntimeFontSource = EmbeddedFontCatalog.Resolve(
                fontDataHash, payloadSourceHash, payloadRawLength);
#endif
        }

        /// <summary>True if Runtime is already materialized. Avoids triggering lazy build.</summary>
        protected bool HasRuntime => ExistingRuntime != null;

        private Core ExistingRuntime => runtimeSlot?.ExistingRuntime ?? runtime;

        /// <summary>Tears down the current runtime; next <see cref="Runtime"/> access rebuilds.</summary>
        protected void InvalidateRuntime()
        {
            var previousSlot = runtimeSlot;
            runtimeSlot = null;
            var previous = runtime;
            runtime = null;
            var slotted = previousSlot?.ExistingRuntime;
            previousSlot?.Dispose();
            if (previous != null && !ReferenceEquals(previous, slotted)) previous.Dispose();
        }

        protected void NotifyConfigurationChanged()
        {
            var change = new StateChange(default, StateChangeKind.Reset);
            PublishStateChange(this, in change);
        }

        protected void SetRawFontDataHash(int value) => SetFontDataHashState(value);

        /// <summary>Updates coupled face metadata without exposing an observable half-updated runtime.</summary>
        protected void SetResolvedMetadata(FaceInfo valueFaceInfo, int valueUnitsPerEm,
            int? valueSpaceAdvance = null)
        {
            metadataMutationDepth++;
            try
            {
                SetFaceInfoState(valueFaceInfo);
                SetUnitsPerEmState(valueUnitsPerEm);
                if (valueSpaceAdvance.HasValue) SetSpaceAdvanceState(valueSpaceAdvance.Value);
            }
            finally
            {
                metadataMutationDepth--;
            }
        }

        private void RebuildRuntime(StateMember member)
        {
            if (metadataMutationDepth != 0) return;
            if (member == Members.FontDataHash)
            {
                embeddedRuntimeFontSource = null;
#if UNITY_EDITOR || UNITY_WEBGL
                embeddedRuntimeFontData = null;
#endif
            }
            InvalidateRuntime();
            PublishStateChange(member);
        }

        private void RebuildGlyphOverrides(in StateListMutation<GlyphOverride> mutation)
            => RebuildRuntime(Members.GlyphOverrides);

        private void RebuildAxisDefaults(in StateListMutation<AxisDefault> mutation)
            => RebuildRuntime(Members.AxisDefaults);

        private void ApplyRuntimeMetricsChange(StateMember member)
        {
            var current = ExistingRuntime;
            if (current != null)
            {
                current.FontScale = fontScale;
                current.ParticipatesInNormalization = participatesInNormalization;
                current.SpacingOffset = spacingOffset;
                current.SpaceAdvance = spaceAdvance;
                current.FakeBoldWeight = fakeBoldWeight;
                current.ItalicStyle = italicStyle;
            }
            else if (runtimeSlot != null)
            {
                RefreshUnmaterializedRuntimeSlot();
            }
            PublishStateChange(member);
        }

        private void ApplyUnitsPerEmChange(StateMember member, int previous, ref int current)
        {
            if (current <= 0) current = 1000;
            if (current == previous) return;
            RebuildRuntime(member);
        }

        private void ApplyFontScaleChange(StateMember member, float previous, ref float current)
        {
            if (current <= 0f) current = 1f;
            if (current.Equals(previous)) return;
            ApplyRuntimeMetricsChange(member);
        }

        private void ApplySdfDetailMultiplierChange(StateMember member, float previous, ref float current)
        {
            if (current <= 0f) current = 1f;
            if (current.Equals(previous)) return;
            RebuildRuntime(member);
        }

        private void ApplyTileSizeOffsetChange(StateMember member, int previous, ref int current)
        {
            current = Mathf.Clamp(current, -2, 2);
            if (current == previous) return;
            RebuildRuntime(member);
        }

        /// <summary>Subclass hook: instantiate a Core from <paramref name="bytes"/>. Default uses serialized config.</summary>
        protected virtual Core BuildRuntime(byte[] bytes)
            => BuildRuntime(GetOrCreateSource(bytes));

        private protected Core BuildRuntimeFromSource(FontSource source, Type builtInType)
            => GetType() == builtInType
                ? BuildRuntime(source)
                : BuildRuntime(source.CopyBytes());

        internal virtual FontRuntimeSlot CaptureRuntimeSlot()
        {
            if (runtimeSlot != null) return runtimeSlot;
            if (GetType() != typeof(UniTextFont)) return CaptureEagerRuntimeSlot();
            return CaptureEmbeddedRuntimeSlot(CreateStandardRuntime);
        }

        private static Core CreateStandardRuntime(FontSource source, RuntimeSnapshot snapshot)
            => snapshot.Create(source);

        private protected FontRuntimeSlot CaptureEmbeddedRuntimeSlot(
            Func<FontSource, RuntimeSnapshot, Core> factory)
        {
            if (runtimeSlot != null) return runtimeSlot;
            if (runtime != null) return runtimeSlot = new FontRuntimeSlot(runtime);
            return runtimeSlot = new FontRuntimeSlot(CaptureEmbeddedRuntimeFactory(factory));
        }

        private protected Func<Core> CaptureEmbeddedRuntimeFactory(
            Func<FontSource, RuntimeSnapshot, Core> factory)
        {
            var source = CaptureEmbeddedFontSource();
            var snapshot = CaptureRuntimeSnapshot();
            return () => source == null ? null : factory(source, snapshot);
        }

        private protected FontRuntimeSlot CaptureLazyRuntimeSlot(Func<Core> factory)
        {
            if (runtimeSlot != null) return runtimeSlot;
            if (runtime != null) return runtimeSlot = new FontRuntimeSlot(runtime);
            return runtimeSlot = new FontRuntimeSlot(factory);
        }

        private protected void ReplaceUnmaterializedRuntimeFactory(Func<Core> factory)
            => runtimeSlot?.ReplaceFactory(factory);

        internal virtual void RefreshUnmaterializedRuntimeSlot()
        {
            if (runtimeSlot == null || runtimeSlot.IsMaterialized) return;
            if (GetType() != typeof(UniTextFont)) return;
            runtimeSlot.ReplaceFactory(CaptureEmbeddedRuntimeFactory(CreateStandardRuntime));
        }

        private protected FontRuntimeSlot CaptureEagerRuntimeSlot()
        {
            if (runtimeSlot != null) return runtimeSlot;
            var current = Runtime;
            return runtimeSlot ??= new FontRuntimeSlot(current);
        }

        private protected RuntimeSnapshot CaptureRuntimeSnapshot()
        {
            cachedName = name;
            return new RuntimeSnapshot(
                faceInfo, unitsPerEm, fontScale, participatesInNormalization,
                sdfDetailMultiplier, tileSizeOffset, italicStyle, spacingOffset,
                spaceAdvance, fakeBoldWeight,
                glyphOverrides?.ToArray(), cachedName, axisDefaults?.ToArray());
        }

        internal bool TryCaptureVariableAxisMetadata(out HB.hb_ot_var_axis_info_t[] axes,
            out float[] defaults)
        {
            axes = null;
            defaults = null;
            if (variableAxes == null) return false;
            if (variableAxes.Length == 0) return true;

            axes = new HB.hb_ot_var_axis_info_t[variableAxes.Length];
            for (var i = 0; i < variableAxes.Length; i++)
            {
                var axis = variableAxes[i];
                axes[i] = new HB.hb_ot_var_axis_info_t
                {
                    axisIndex = (uint)i,
                    tag = (uint)axis.tag,
                    minValue = axis.minValue,
                    defaultValue = axis.defaultValue,
                    maxValue = axis.maxValue,
                };
            }
            defaults = Core.BuildDefaultAxisValues(axes, axisDefaults, out _);
            return true;
        }

        private protected bool SetVariableAxisMetadata(HB.hb_ot_var_axis_info_t[] axes)
        {
            var next = axes == null || axes.Length == 0
                ? Array.Empty<VariableAxisMetadata>()
                : new VariableAxisMetadata[axes.Length];
            for (var i = 0; i < next.Length; i++)
            {
                next[i] = new VariableAxisMetadata
                {
                    tag = (int)axes[i].tag,
                    minValue = axes[i].minValue,
                    defaultValue = axes[i].defaultValue,
                    maxValue = axes[i].maxValue,
                };
            }

            if (VariableAxisMetadataEquals(variableAxes, next)) return false;
            variableAxes = next;
            return true;
        }

        private protected void InvalidateVariableAxisMetadata() => variableAxes = null;

#if UNITY_EDITOR
        private bool SetFaceCountMetadata(int value)
        {
            value = Math.Max(1, value);
            if (faceCount == value) return false;
            faceCount = value;
            return true;
        }
#endif

        private static bool VariableAxisMetadataEquals(VariableAxisMetadata[] left,
            VariableAxisMetadata[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i].tag != right[i].tag
                    || left[i].minValue != right[i].minValue
                    || left[i].defaultValue != right[i].defaultValue
                    || left[i].maxValue != right[i].maxValue)
                    return false;
            }
            return true;
        }

        private protected static HB.hb_ot_var_axis_info_t[] ReadVariableAxes(
            FontSource source, int faceIndex)
        {
            if (source == null) return null;
            FontBackingLease backing = null;
            try
            {
                backing = source.Open();
                using var cache = new Shaper.FontCacheEntry(backing, Math.Max(0, faceIndex));
                backing = null;
                return cache.GetAxisInfos();
            }
            finally
            {
                backing?.Dispose();
            }
        }

        private protected FontSource GetOrCreateSource(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
#if UNITY_EDITOR || UNITY_WEBGL
            if (!ReferenceEquals(bytes, embeddedRuntimeFontData)) return new ArrayFontSource(bytes);
            return embeddedRuntimeFontSource ??= new ArrayFontSource(bytes);
#else
            return new ArrayFontSource(bytes);
#endif
        }

        private protected virtual Core BuildRuntime(FontSource source)
        {
            return new Core(
                source, faceInfo, unitsPerEm, fontScale, sdfDetailMultiplier,
                tileSizeOffset, italicStyle, spacingOffset, fakeBoldWeight,
                glyphOverrides == null ? null : (IReadOnlyList<GlyphOverride>)GlyphOverrides,
                name,
                axisDefaults == null ? null : (IReadOnlyList<AxisDefault>)AxisDefaults,
                spaceAdvance)
            { ParticipatesInNormalization = participatesInNormalization };
        }

#if UNITY_EDITOR || UNITY_WEBGL
        private byte[] DecompressedFontData()
        {
            if (embeddedRuntimeFontData != null) return embeddedRuntimeFontData;
            var stored = payload?.data;
            if (stored == null || stored.Length == 0) return null;
            embeddedRuntimeFontData = embeddedRuntimeFontSource is CompressedArrayFontSource compressed
                ? compressed.GetOrCreateBytes()
                : Zstd.IsCompressed(stored) ? Zstd.Decompress(stored) : stored;
#if !UNITY_EDITOR
            payload.data = null;
#endif
            return embeddedRuntimeFontData;
        }
#endif

        #endregion

        #region Forwarding API

        /// <summary>
        /// Creates a managed snapshot of the raw TTF/OTF file. This compatibility property allocates
        /// the complete file; rendering and shaping use the source backing directly.
        /// </summary>
        public virtual byte[] FontData => CopyFontData();

        /// <summary>Creates a managed snapshot of the complete raw font file.</summary>
        public virtual byte[] CopyFontData() => CaptureFontSource()?.CopyBytes();

#if UNITY_EDITOR
        /// <summary>Editor-only: byte length of the serialized (Zstd-compressed) embedded font data — the asset's on-disk footprint. 0 when nothing is embedded (e.g. a runtime-resolved system font).</summary>
        internal virtual int CompressedFontDataSize => payload?.data?.Length ?? 0;
        internal virtual int RawFontDataSize
        {
            get
            {
                if (embeddedRuntimeFontData is { Length: > 0 })
                    return embeddedRuntimeFontData.Length;
                var stored = payload?.data;
                if (stored == null || stored.Length == 0) return 0;
                return Zstd.IsCompressed(stored)
                    ? checked((int)Zstd.GetFrameContentSize(stored))
                    : stored.Length;
            }
        }
        internal int FaceCount => faceCount;
        internal UniTextFontPayload EditorPayload => payload;
        internal HB.hb_ot_var_axis_info_t[] EditorVariableAxes
            => TryCaptureVariableAxisMetadata(out var axes, out _) ? axes : null;
        internal bool TryGetEmbeddedBuildSource(out int token, out byte[] stored)
        {
            token = fontDataHash;
            stored = payload?.data;
            return stored is { Length: > 0 };
        }
#endif
        /// <summary>True when font bytes are available (either serialized or supplied by a subclass).</summary>
        public virtual bool HasFontData
        {
            get
            {
                var current = ExistingRuntime;
                if (current != null) return current.HasFontData;
                if (embeddedRuntimeFontSource != null) return embeddedRuntimeFontSource.Length > 0;
#if UNITY_EDITOR || UNITY_WEBGL
                return (embeddedRuntimeFontData ?? payload?.data) is { Length: > 0 };
#else
                return fontDataHash != 0;
#endif
            }
        }
        /// <summary>Process-unique runtime identity used by atlas and shaping registries.</summary>
        public virtual int FontDataHash => Runtime?.FontDataHash ?? 0;
        /// <summary>Raw bytes-derived hash. Subclasses whose bytes are resolved late override to forward to the bytes source.</summary>
        protected internal virtual int RawFontDataHash
            => ExistingRuntime?.RawFontDataHash ?? fontDataHash;

        /// <summary>Font face metrics (ascender, descender, line height, etc.).</summary>
        public FaceInfo FaceInfo
        {
            get => HasRuntime ? Runtime.FaceInfo : faceInfo;
            internal set => SetFaceInfoState(value);
        }

        /// <summary>Font design units per em (typically 1000 or 2048). Fundamental scaling unit: scale = fontSize / unitsPerEm.</summary>
        public int UnitsPerEm
        {
            get => HasRuntime ? Runtime.UnitsPerEm : (unitsPerEm > 0 ? unitsPerEm : 1000);
            internal set => SetUnitsPerEmState(value);
        }

        /// <summary>Visual scale multiplier. Compensates for fonts that render visually smaller/larger than peers at the same size (e.g. Dongle).</summary>
        public float FontScale
        {
            get => HasRuntime ? Runtime.FontScale : (fontScale > 0f ? fontScale : 1f);
            set => SetFontScaleState(value);
        }

        /// <summary>Whether font-size normalization may scale this font to match the primary font. Disable to keep a decorative font's intrinsic proportions.</summary>
        public bool ParticipatesInNormalization
        {
            get => HasRuntime ? Runtime.ParticipatesInNormalization : participatesInNormalization;
            set => SetParticipatesInNormalizationState(value);
        }

        /// <summary>Synthetic italic slant in percent of height (30 leans the top right by 0.30 of the height, ≈16.7°, not 30°), applied as a horizontal skew when an italic tag is set. 0 falls back to <see cref="DefaultItalicStyle"/>.</summary>
        public float ItalicStyle
        {
            get => HasRuntime ? Runtime.ItalicStyle : italicStyle;
            set => SetItalicStyleState(value);
        }

        /// <summary>Baseline spacing added to every glyph's advance during shaping, in font design units. Layered additively with style-level letter-spacing.</summary>
        public int SpacingOffset
        {
            get => HasRuntime ? Runtime.SpacingOffset : spacingOffset;
            set => SetSpacingOffsetState(value);
        }

        /// <summary>Space (U+0020) advance for this font, in design units; replaces the width HarfBuzz reports. <see cref="SpaceAdvanceUninitialized"/> = use the font's own / HarfBuzz fallback.</summary>
        public int SpaceAdvance
        {
            get => HasRuntime ? Runtime.SpaceAdvance : spaceAdvance;
            set => SetSpaceAdvanceState(value);
        }

        /// <summary>Baseline synthetic bold in CSS weight steps from the font's own weight (1 ≈ Regular → Bold). Renders thicker via SDF dilate and compensates advance to keep layout stable. No effect on color emoji fonts.</summary>
        public float FakeBoldWeight
        {
            get => HasRuntime ? Runtime.FakeBoldWeight : fakeBoldWeight;
            set => SetFakeBoldWeightState(value);
        }

        /// <summary>Atlas gutter in pixels around each glyph tile, prevents bilinear bleeding.</summary>
        public virtual int AtlasPadding => ExistingRuntime?.AtlasPadding
                                           ?? (UsesLazyAssetMetadata
                                               ? 1
                                               : Runtime?.AtlasPadding ?? 1);
        /// <summary>True if this font defines OpenType variable axes.</summary>
        public bool IsVariable => UsesLazyAssetMetadata
            ? variableAxes != null
                ? variableAxes.Length > 0
                : Runtime?.IsVariable ?? false
            : Runtime?.IsVariable ?? false;
        internal HB.hb_ot_var_axis_info_t[] VariableAxes => Runtime?.VariableAxes;
        /// <summary>True for color (emoji/COLR) fonts — they bypass the SDF pipeline.</summary>
        public bool IsColor => ExistingRuntime?.IsColor
                               ?? (UsesLazyAssetMetadata
                                   ? GetType() == typeof(UniTextColorFont)
                                   : Runtime?.IsColor ?? false);

        /// <summary>Cached name string usable on worker threads (Unity's <c>name</c> getter is main-thread-only).</summary>
        public string CachedName => ExistingRuntime?.Name
                                    ?? (UsesLazyAssetMetadata
                                        ? cachedName ?? name
                                        : Runtime?.Name ?? name);

        private bool UsesLazyAssetMetadata
        {
            get
            {
                var type = GetType();
                return type == typeof(UniTextFont)
                       || type == typeof(UniTextColorFont)
                       || type == typeof(UniTextFontVariant);
            }
        }

        /// <summary>(varHash48, glyphIndex) → atlas-resident <see cref="Glyph"/>. Null until the font's lookup tables are built.</summary>
        public Dictionary<long, Glyph> GlyphLookupTable => Runtime?.GlyphLookupTable;
        internal Dictionary<uint, UniTextCharacter> CharacterLookupTable => Runtime?.CharacterLookupTable;
        internal int MaterializedGlyphCount => ExistingRuntime?.MaterializedGlyphCount ?? 0;
        internal int MaterializedCharacterCount => ExistingRuntime?.MaterializedCharacterCount ?? 0;

        /// <summary>Cached Unity instance id, suitable for use as a dictionary key.</summary>
        public virtual int GetCachedInstanceId()
        {
            if (cachedInstanceId == 0)
                cachedInstanceId = ObjectUtils.GetInstanceIdCompat(this);
            return cachedInstanceId;
        }

        /// <summary>Loads the underlying FreeType face. Returns the resulting error code, or <see cref="UniTextFontError.Success"/> if already loaded.</summary>
        public virtual UniTextFontError LoadFontFace() => Runtime?.LoadFontFace() ?? UniTextFontError.InvalidFile;

        /// <summary>Implicit access to the runtime Core. Returns null for unresolved/empty/destroyed fonts.</summary>
        public static implicit operator Core(UniTextFont font)
        {
            if (font == null) return null;
            return font.Runtime;
        }

        /// <summary>Builds glyph and character lookup dictionaries and synthesizes invisible control characters (tab, line separators, BOM marks).</summary>
        public void ReadFontAssetDefinition() => Runtime?.ReadFontDefinition();

        /// <summary>Resolves a codepoint to a glyph index via HarfBuzz, with controlled fallbacks for NBSP/soft-hyphen.</summary>
        public uint GetGlyphIndexForUnicode(uint unicode) => Runtime?.GetGlyphIndexForUnicode(unicode) ?? 0;

        /// <summary>Adds (unicode, glyphIndex) pairs to the character table for entries discovered after the initial font load (e.g. variation selectors, emoji sequences).</summary>
        public void RegisterCharacterEntries(List<(uint unicode, uint glyphIndex)> entries) => Runtime?.RegisterCharacterEntries(entries);

        /// <summary>Returns whether the glyph has a committed or pending entry in the selected distance-field atlas for this font's default variation.</summary>
        public bool HasGlyphInAtlas(uint glyphIndex, UniTextRenderMode mode) =>
            Runtime?.HasGlyphInAtlas(glyphIndex, mode) ?? false;

        /// <summary>Drops the FT face, glyph caches and atlas entries owned by this font. Next access rebuilds on demand.</summary>
        public virtual void ClearDynamicData() => Runtime?.ClearDynamicData();

        /// <summary>Manually fires the <see cref="Changed"/> event. Use after editor edits that affect rendering but not atlas residency.</summary>
        public void InvokeChanged() => NotifyConfigurationChanged();

        #endregion

        #region Lifecycle

        protected virtual void OnEnable()
        {
            if (!UsesEmbeddedSource) return;
            if (GetType() == typeof(UniTextFont)) CaptureRuntimeSlot();
            else CaptureEmbeddedFontSource();
        }

        protected virtual void OnDisable()
        {
            InvalidateRuntime();
        }

        protected virtual void OnDestroy()
        {
            InvalidateRuntime();
#if UNITY_EDITOR || UNITY_WEBGL
            embeddedRuntimeFontData = null;
#endif
            embeddedRuntimeFontSource = null;
            cachedName = null;
        }

    #if UNITY_EDITOR
        static UniTextFont() => EditorLifecycle.UnmanagedCleaning += ClearRuntimeDataForReload;
    #endif

        /// <summary>Releases native and atlas data for all live runtimes while preserving their managed font definitions, then invalidates shared resolution caches.</summary>
        public static void ClearRuntimeData() => ClearRuntimeData(true);

        private static void ClearRuntimeDataForReload() => ClearRuntimeData(false);

        private static void ClearRuntimeData(bool notify)
        {
            Core.ClearAllLiveDynamicData(notify);
            SharedFontCache.Clear();
        }

        #endregion

        #region Hash Helpers

        /// <summary>Cheap content hash of font bytes (FNV-1a variant, sampling step on large files). Stable across runs.</summary>
        public static int ComputeFontDataHash(byte[] data)
            => data == null ? 0 : ComputeFontDataHash((ReadOnlySpan<byte>)data);

        internal static int ComputeFontDataHash(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0) return 0;
            unchecked
            {
                var hash = -2128831035;
                var len = data.Length;
                var step = len > 4096 ? len / 1024 : 1;
                for (var i = 0; i < len; i += step)
                    hash = (hash ^ data[i]) * 16777619;
                return (hash ^ len) * 16777619;
            }
        }

        protected void ResetInstanceFontDataHash() => Runtime?.ResetInstanceFontDataHash();

        #endregion

        #region Static Creation

        /// <summary>Builds a runtime <see cref="UniTextFont"/> from font file bytes. FT face is loaded once to populate <see cref="FaceInfo"/> and units-per-em.</summary>
        public static UniTextFont CreateFontAsset(byte[] fontBytes) => CreateFontAsset<UniTextFont>(fontBytes);

        /// <summary>Builds a font asset of the given <see cref="UniTextFont"/> subtype from font file bytes, populating its source, hash, units-per-em and face metrics identically to <see cref="CreateFontAsset(byte[])"/>.</summary>
        public static T CreateFontAsset<T>(byte[] fontBytes) where T : UniTextFont
        {
            if (fontBytes == null || fontBytes.Length == 0)
            {
                Debug.LogError("UniTextFontAsset: Cannot create font asset from null or empty byte array.");
                return null;
            }

            if (!FT.IsInitialized) FT.Initialize();
            var face = FT.LoadFace(fontBytes, 0);
            if (face == IntPtr.Zero)
            {
                Debug.LogError("UniTextFontAsset: Failed to load font face from byte array.");
                return null;
            }

            var fontAsset = CreateInstance<T>();
            fontAsset.SetFontDataHashState(ComputeFontDataHash(fontBytes));
            fontAsset.embeddedRuntimeFontSource = new ArrayFontSource(fontBytes);
#if UNITY_EDITOR
            fontAsset.payload = UniTextFontPayload.Create(fontAsset.fontDataHash,
                Zstd.Compress(fontBytes));
            fontAsset.payloadSourceHash = fontAsset.payload.sourceHash;
            fontAsset.payloadRawLength = fontAsset.payload.rawLength;
#elif UNITY_WEBGL
            fontAsset.payload = UniTextFontPayload.Create(fontAsset.fontDataHash, fontBytes);
#endif

            int realUpem = Shaper.GetUpemFromFontData(fontBytes);
            fontAsset.SetResolvedMetadata(Core.BuildFullFaceInfo(face), realUpem);
#if UNITY_EDITOR
            fontAsset.SetFaceCountMetadata(Math.Max(1, FT.GetFaceInfo(face).numFaces));
#endif

            FT.UnloadFace(face);

            fontAsset.ReadFontAssetDefinition();
            fontAsset.SetVariableAxisMetadata(fontAsset.Runtime?.VariableAxes);
            return fontAsset;
        }

        #endregion

        #region Editor Support

#if UNITY_EDITOR
        [SerializeField, StatePassive]
        [Tooltip("Unity Font asset to sync with (Editor only).")]
        public Font sourceFont;

        protected virtual void EnsureFaceInfoFromFont()
        {
            if (payload?.data is not { Length: > 0 }) return;

            var bytes = DecompressedFontData();
            if (bytes == null) return;

            if (!FT.IsInitialized) FT.Initialize();
            var face = FT.LoadFace(bytes, faceInfo.faceIndex < 0 ? 0 : faceInfo.faceIndex);
            if (face == IntPtr.Zero) return;

            var fresh = Core.BuildFullFaceInfo(face);
            var nextFaceInfo = faceInfo;
            var nextFaceCount = Math.Max(1, FT.GetFaceInfo(face).numFaces);

            nextFaceInfo.familyName = fresh.familyName;
            nextFaceInfo.styleName = fresh.styleName;
            nextFaceInfo.weightClass = fresh.weightClass;
            nextFaceInfo.isItalic = fresh.isItalic;

            var nextSpaceAdvance = spaceAdvance;

            if (spaceAdvance == SpaceAdvanceUninitialized)
            {
                var spaceGlyph = FT.GetCharIndex(face, (uint)UnicodeData.Space);
                nextSpaceAdvance = spaceGlyph != 0
                    ? FT.GetGlyphAdvanceUnscaled(face, ' ')
                    : Mathf.Max(1, fresh.unitsPerEm / 4);
            }

            FT.UnloadFace(face);

            var faceChanged = !faceInfo.Equals(nextFaceInfo);
            var spaceChanged = spaceAdvance != nextSpaceAdvance;
            var axesChanged = SetVariableAxisMetadata(ReadVariableAxes(
                new ArrayFontSource(bytes), nextFaceInfo.faceIndex));
            var countChanged = SetFaceCountMetadata(nextFaceCount);
            if (faceChanged || spaceChanged || axesChanged || countChanged)
            {
                SetResolvedMetadata(nextFaceInfo, unitsPerEm, nextSpaceAdvance);
                InvalidateRuntime();
                if (faceChanged || axesChanged || countChanged)
                    PublishStateChange(Members.FaceInfo);
                if (spaceChanged) PublishStateChange(Members.SpaceAdvance);
                EditorUtility.SetDirty(this);
            }
        }

        internal void EditorEnsureFaceInfo() => EnsureFaceInfoFromFont();

        internal virtual bool TryReadFaceInfo(int index, out FaceInfo nextFaceInfo,
            out int nextUnitsPerEm)
        {
            nextFaceInfo = default;
            nextUnitsPerEm = 0;
            if (payload?.data is not { Length: > 0 }) return false;

            var bytes = DecompressedFontData();
            if (bytes == null) return false;

            if (!FT.IsInitialized) FT.Initialize();
            int idx = index < 0 ? 0 : index;
            var face = FT.LoadFace(bytes, idx);
            if (face == IntPtr.Zero) return false;

            nextFaceInfo = Core.BuildFullFaceInfo(face);
            nextFaceInfo.faceIndex = idx;
            nextUnitsPerEm = nextFaceInfo.unitsPerEm > 0
                ? nextFaceInfo.unitsPerEm
                : Shaper.GetUpemFromFontData(bytes);
            FT.UnloadFace(face);
            return true;
        }
#endif

        #endregion

        #region Core

        /// <summary>
        /// Plain runtime for a font: owns face access, glyph tables, lookup dictionaries, curve cache and
        /// atlas pipeline. Constructable on worker threads (no Unity object lifecycle). Dispose instances
        /// constructed directly; a <see cref="UniTextFont"/> owns and disposes the runtime exposed by its asset.
        /// </summary>
        public class Core : IDisposable
        {
            #region Construction

            private FontSource fontSource;
            private IFontFaceBackend fontBackend;
            private FaceInfo faceInfoField;
            private int unitsPerEmField;
            private float fontScaleField;
            private bool participatesInNormalizationField = true;
            private float sdfDetailMultiplierField;
            private int tileSizeOffsetField;
            private float italicStyleField;
            private int spacingOffsetField;
            private int spaceAdvanceField = SpaceAdvanceUninitialized;
            private float fakeBoldWeightField;
            private readonly List<GlyphOverride> glyphOverridesField;
            private string nameField;
            private readonly List<AxisDefault> axisDefaultsField;
            private readonly IGlyphOutlineSource glyphOutlineSource;
            private readonly Core styleSource;
            private readonly Core styledFallbackSource;
            private readonly float styleBaseScale = 1f;

            internal Core() => Register();

            /// <summary>Constructs an independently owned runtime from font bytes and the rendering settings normally supplied by the <see cref="UniTextFont"/> wrapper. The byte array is shared without copying and must not be modified while the runtime is alive. Safe to call from worker threads; the caller must dispose it.</summary>
            public Core(byte[] bytes, FaceInfo faceInfo, int unitsPerEm, float fontScale,
                float sdfDetailMultiplier, int tileSizeOffset, float italicStyle,
                int spacingOffset, float fakeBoldWeight,
                List<GlyphOverride> glyphOverrides = null, string name = null,
                List<AxisDefault> axisDefaults = null, int spaceAdvance = SpaceAdvanceUninitialized)
                : this(bytes, faceInfo, unitsPerEm, fontScale, sdfDetailMultiplier,
                    tileSizeOffset, italicStyle, spacingOffset, fakeBoldWeight,
                    (IReadOnlyList<GlyphOverride>)glyphOverrides, name,
                    (IReadOnlyList<AxisDefault>)axisDefaults, spaceAdvance)
            {
            }

            internal Core(byte[] bytes, FaceInfo faceInfo, int unitsPerEm, float fontScale,
                float sdfDetailMultiplier, int tileSizeOffset, float italicStyle,
                int spacingOffset, float fakeBoldWeight,
                IReadOnlyList<GlyphOverride> glyphOverrides, string name,
                IReadOnlyList<AxisDefault> axisDefaults, int spaceAdvance,
                IGlyphOutlineSource glyphOutlineSource = null)
                : this(bytes == null || bytes.Length == 0 ? null : new ArrayFontSource(bytes),
                    faceInfo, unitsPerEm, fontScale, sdfDetailMultiplier, tileSizeOffset,
                    italicStyle, spacingOffset, fakeBoldWeight, glyphOverrides, name,
                    axisDefaults, spaceAdvance, glyphOutlineSource)
            {
            }

            internal Core(FontSource source, FaceInfo faceInfo, int unitsPerEm, float fontScale,
                float sdfDetailMultiplier, int tileSizeOffset, float italicStyle,
                int spacingOffset, float fakeBoldWeight,
                IReadOnlyList<GlyphOverride> glyphOverrides, string name,
                IReadOnlyList<AxisDefault> axisDefaults, int spaceAdvance,
                IGlyphOutlineSource glyphOutlineSource = null)
            {
                fontSource = source;
                faceInfoField = faceInfo;
                unitsPerEmField = unitsPerEm > 0 ? unitsPerEm : 1000;
                fontScaleField = fontScale > 0f ? fontScale : 1f;
                sdfDetailMultiplierField = sdfDetailMultiplier > 0f ? sdfDetailMultiplier : 1f;
                tileSizeOffsetField = tileSizeOffset;
                italicStyleField = italicStyle;
                spacingOffsetField = spacingOffset;
                spaceAdvanceField = spaceAdvance;
                fakeBoldWeightField = fakeBoldWeight;
                glyphOverridesField = glyphOverrides != null ? new List<GlyphOverride>(glyphOverrides) : null;
                nameField = name;
                axisDefaultsField = axisDefaults != null ? new List<AxisDefault>(axisDefaults) : null;
                this.glyphOutlineSource = glyphOutlineSource?.Retain();
                Register();
            }

            /// <summary>
            /// System-font companion that renders <paramref name="original"/>'s glyphs but reads its layout style
            /// (scale, italic, spacing, fake-bold) live from <paramref name="styleSource"/>, so a runtime style change on
            /// the source needs no copy-back. Raster params (SDF detail, tile offset) are fixed here — they only change
            /// through a full rebuild, which replaces this clone anyway.
            /// </summary>
            private Core(Core original, Core styleSource)
            {
                fontSource = original.fontSource;
                fontBackend = original.fontBackend?.Retain();
                try
                {
                    faceInfoField = original.faceInfoField;
                    unitsPerEmField = original.unitsPerEmField;
                    nameField = original.nameField;
                    axisDefaultsField = original.axisDefaultsField != null
                        ? new List<AxisDefault>(original.axisDefaultsField)
                        : null;
                    glyphOutlineSource = original.glyphOutlineSource?.Retain();
                    sdfDetailMultiplierField = styleSource.sdfDetailMultiplierField;
                    tileSizeOffsetField = styleSource.tileSizeOffsetField;
                    isSystemFont = original.isSystemFont;
                    this.styleSource = styleSource;
                    styledFallbackSource = original.SystemFontSource;
                    styleBaseScale = original.FontScale;
                    Register();
                }
                catch
                {
                    glyphOutlineSource?.Dispose();
                    fontBackend?.Dispose();
                    fontBackend = null;
                    throw;
                }
            }

            #endregion

            #region Properties

            /// <summary>Creates a managed snapshot of the complete raw font file.</summary>
            public virtual byte[] FontData => CopyFontData();
            public virtual byte[] CopyFontData() => fontSource?.CopyBytes();
            public virtual bool HasFontData => fontSource is { Length: > 0 };
            internal FontSource Source => fontSource;
            internal FontBackingLease OpenFontData() => fontSource?.Open();
            internal bool HasFontBackend => HasFontData || fontBackend != null;
            internal IFontFaceBackend FontBackend => fontBackend;
            internal string FontIdentity => fontBackend?.Identity ?? fontSource?.Identity;

            private protected void SetFontSource(FontSource source)
            {
                if (fontSource != null || fontBackend != null)
                    throw new InvalidOperationException("Font source is already assigned.");
                fontSource = source ?? throw new ArgumentNullException(nameof(source));
            }

            /// <summary>Retains a data-independent face backend; ownership of the supplied reference remains with the caller.</summary>
            private protected void SetFontBackend(IFontFaceBackend backend)
            {
                if (fontSource != null || fontBackend != null)
                    throw new InvalidOperationException("Font backend is already assigned.");
                var value = backend ?? throw new ArgumentNullException(nameof(backend));
                fontBackend = value.Retain()
                    ?? throw new InvalidOperationException("Font backend retain returned no owner.");
            }

            private int instanceFontDataHash;
            private int rawFontDataHashCache;
            private static int nextRuntimeFontId;

            /// <summary>Process-unique runtime identity, so distinct font sources and faces can never alias in shaping or atlas registries.</summary>
            public virtual int FontDataHash
            {
                get
                {
                    var existing = Volatile.Read(ref instanceFontDataHash);
                    if (existing != 0) return existing;
                    if (!HasFontBackend) return 0;
                    var id = AllocateRuntimeFontId();
                    existing = Interlocked.CompareExchange(ref instanceFontDataHash, id, 0);
                    return existing != 0 ? existing : id;
                }
            }

            /// <summary>Claims the next process-unique runtime identity from the space every runtime's <see cref="FontDataHash"/> draws on.</summary>
            /// <exception cref="InvalidOperationException">The identity space is exhausted.</exception>
            internal static int AllocateRuntimeFontId()
            {
                var id = Interlocked.Increment(ref nextRuntimeFontId);
                if (id <= 0)
                    throw new InvalidOperationException("UniText runtime font identity space exhausted.");
                return id;
            }

            /// <summary>Raw bytes-derived hash. Subclasses override to forward to their bytes source.</summary>
            public virtual int RawFontDataHash
            {
                get
                {
                    if (rawFontDataHashCache != 0) return rawFontDataHashCache;
                    rawFontDataHashCache = fontSource?.ComputeLegacyHash() ?? 0;
                    return rawFontDataHashCache;
                }
            }

            internal void ResetInstanceFontDataHash() { instanceFontDataHash = 0; rawFontDataHashCache = 0; }

            public FaceInfo FaceInfo { get => faceInfoField; internal set => faceInfoField = value; }

            public int UnitsPerEm
            {
                get => unitsPerEmField > 0 ? unitsPerEmField : 1000;
                internal set => unitsPerEmField = value > 0 ? value : 1000;
            }

            public float FontScale
            {
                get
                {
                    var f = styleSource != null
                        ? styleSource.fontScaleField * styleBaseScale
                        : fontScaleField;
                    return f > 0f ? f : 1f;
                }
                set => fontScaleField = value > 0f ? value : 1f;
            }

            public bool ParticipatesInNormalization
            {
                get => (styleSource ?? this).participatesInNormalizationField;
                set => participatesInNormalizationField = value;
            }

            public float ItalicStyle
            {
                get { var v = (styleSource ?? this).italicStyleField; return v != 0f ? v : DefaultItalicStyle; }
                set => italicStyleField = value;
            }

            public int SpacingOffset
            {
                get => (styleSource ?? this).spacingOffsetField;
                set => spacingOffsetField = value;
            }

            public int SpaceAdvance
            {
                get => (styleSource ?? this).spaceAdvanceField;
                set => spaceAdvanceField = value;
            }

            public float FakeBoldWeight
            {
                get => (styleSource ?? this).fakeBoldWeightField;
                set => fakeBoldWeightField = value;
            }

            internal float SdfDetailMultiplier => sdfDetailMultiplierField;
            internal int TileSizeOffset => tileSizeOffsetField;

            internal bool GlyphOverridesDiffer(List<GlyphOverride> other)
            {
                int a = glyphOverridesField?.Count ?? 0;
                int b = other?.Count ?? 0;
                if (a != b) return true;
                for (int i = 0; i < a; i++)
                    if (glyphOverridesField[i].glyphIndex != other[i].glyphIndex
                        || glyphOverridesField[i].tileSizeOverride != other[i].tileSizeOverride)
                        return true;
                return false;
            }

            internal bool AxisDefaultsDiffer(List<AxisDefault> other)
            {
                int a = axisDefaultsField?.Count ?? 0;
                int b = other?.Count ?? 0;
                if (a != b) return true;
                for (int i = 0; i < a; i++)
                    if (axisDefaultsField[i].tag != other[i].tag
                        || !AxisOverrideValuesEqual(axisDefaultsField[i].value, other[i].value))
                        return true;
                return false;
            }

            private static bool AxisOverrideValuesEqual(float left, float right)
            {
                var leftInvalid = float.IsNaN(left) || float.IsInfinity(left);
                var rightInvalid = float.IsNaN(right) || float.IsInfinity(right);
                return leftInvalid || rightInvalid
                    ? leftInvalid == rightInvalid
                    : FontVariation.ToFixed(left) == FontVariation.ToFixed(right);
            }

            /// <summary>Worker-thread-safe display name (mirrors <see cref="UniTextFont.CachedName"/>).</summary>
            public string Name { get => nameField; set => nameField = value; }

            public virtual int AtlasPadding => 1;
            public virtual bool IsColor => false;

            public virtual int GetCachedInstanceId() => FontDataHash;

            /// <summary>True when this runtime is backed by an OS-installed font — the always-on <see cref="SystemFont"/> fallback or a <see cref="UniTextSystemFont"/> asset (and styled clones of either). Lets the face-resolution pass pull a real OS bold/italic cut before synthesizing.</summary>
            internal bool isSystemFont;
            internal Core SystemFontSource => styledFallbackSource ?? (isSystemFont ? this : null);

            private Dictionary<int, Core> styledFallbacks;
            private readonly object styledFallbacksLock = new object();

            private bool HasDefaultStyle
                => fontScaleField == 1f && sdfDetailMultiplierField == 1f && tileSizeOffsetField == 0
                   && participatesInNormalizationField && italicStyleField == 0f && spacingOffsetField == 0
                   && spaceAdvanceField == SpaceAdvanceUninitialized && fakeBoldWeightField == 0f;

            /// <summary>
            /// Clone of system-font <paramref name="original"/> carrying this font's custom render and layout settings
            /// plus normalization policy while sharing the original's face access and <see cref="FaceInfo"/>. Glyph overrides
            /// are not copied — system-font
            /// glyph ids are platform-specific. Cached per original and owned by this Core; when this font's style is
            /// default the original is returned unchanged so identical styles share one runtime.
            /// </summary>
            internal Core GetStyledFallback(Core original)
            {
                if (original == null || HasDefaultStyle) return original;
                int key = original.GetCachedInstanceId();
                lock (styledFallbacksLock)
                {
                    styledFallbacks ??= new Dictionary<int, Core>();
                    if (styledFallbacks.TryGetValue(key, out var clone)) return clone;
                    clone = new Core(original, this);
                    styledFallbacks[key] = clone;
                    return clone;
                }
            }

            internal int ExistingRuntimeFontId => Volatile.Read(ref instanceFontDataHash);

            private void ClearStyledFallbacks(bool disposing = false)
            {
                Core[] fallbacks;
                lock (styledFallbacksLock)
                {
                    if (styledFallbacks == null || styledFallbacks.Count == 0) return;
                    fallbacks = new Core[styledFallbacks.Count];
                    styledFallbacks.Values.CopyTo(fallbacks, 0);
                    styledFallbacks.Clear();
                }
                for (var i = 0; i < fallbacks.Length; i++)
                    if (disposing) fallbacks[i]?.Dispose();
                    else fallbacks[i]?.ClearDynamicData();
            }

            private void ClearStyledFallbackFor(Core original)
            {
                if (original == null) return;
                Core fallback = null;
                lock (styledFallbacksLock)
                {
                    if (styledFallbacks == null) return;
                    var key = original.GetCachedInstanceId();
                    if (!styledFallbacks.TryGetValue(key, out fallback)) return;
                    styledFallbacks.Remove(key);
                }
                fallback.ClearDynamicData();
            }

            #endregion

            #region Glyph state

            internal Dictionary<long, Glyph> glyphLookupDictionary;
            internal Dictionary<uint, UniTextCharacter> characterLookupDictionary;
            internal List<Glyph> glyphTable = new();
            internal List<UniTextCharacter> characterTable = new();

            private Dictionary<uint, GlyphOverride> glyphOverrideLookup;
            private bool glyphMetricOverridesPresent;
            private Dictionary<uint, GlyphOverride> GlyphOverrideLookup
            {
                get
                {
                    if (glyphOverrideLookup == null && glyphOverridesField is { Count: > 0 })
                    {
                        glyphOverrideLookup = new Dictionary<uint, GlyphOverride>(glyphOverridesField.Count);
                        foreach (var ov in glyphOverridesField)
                        {
                            glyphOverrideLookup[ov.glyphIndex] = ov;
                            if (HasMetricChange(ov)) glyphMetricOverridesPresent = true;
                        }
                    }
                    return glyphOverrideLookup;
                }
            }

            private static bool HasMetricChange(in GlyphOverride ov)
                => (ov.advanceScale > 0f && ov.advanceScale != 1f)
                   || ov.offset.x != 0f || ov.offset.y != 0f
                   || (ov.scale > 0f && ov.scale != 1f);

            /// <summary>True when some glyph carries an advance, offset or size override (tile-size-only overrides excluded). Gates per-glyph override work in shaping and mesh build.</summary>
            internal bool HasGlyphMetricOverrides
            {
                get { _ = GlyphOverrideLookup; return glyphMetricOverridesPresent; }
            }

            internal float GetGlyphAdvanceScale(uint glyphIndex)
            {
                var lookup = GlyphOverrideLookup;
                if (lookup != null && lookup.TryGetValue(glyphIndex, out var ov) && ov.advanceScale > 0f)
                    return ov.advanceScale;
                return 1f;
            }

            /// <summary>Applies this glyph's advance override to a raw advance, in whatever unit it is expressed (design units or pixels — the scale is dimensionless). The single point every non-shaped advance read must funnel through so injected glyphs (list markers, ellipsis dots) honor Glyph Overrides exactly as shaped text does; returns the advance unchanged when no glyph carries an advance override.</summary>
            internal float ApplyAdvanceOverride(uint glyphIndex, float advance)
                => HasGlyphMetricOverrides ? advance * GetGlyphAdvanceScale(glyphIndex) : advance;

            /// <summary>Render size and em offsets for <paramref name="glyphIndex"/>; false when the glyph has no visual override.</summary>
            internal bool TryGetGlyphQuadOverride(uint glyphIndex, out float scale, out float offsetX, out float offsetY)
            {
                var lookup = GlyphOverrideLookup;
                if (lookup != null && lookup.TryGetValue(glyphIndex, out var ov))
                {
                    scale = ov.scale > 0f ? ov.scale : 1f;
                    offsetX = ov.offset.x;
                    offsetY = ov.offset.y;
                    return scale != 1f || offsetX != 0f || offsetY != 0f;
                }
                scale = 1f; offsetX = 0f; offsetY = 0f;
                return false;
            }

            /// <summary>Refreshes per-glyph advance/offset/size override values in place and drops the cached lookup, so layout/render override edits update the live runtime instead of forcing an atlas re-rasterization (and a new font id). Count changes go through a full rebuild, so a null field here means there is nothing to refresh.</summary>
            internal void UpdateGlyphOverrides(List<GlyphOverride> overrides)
            {
                if (glyphOverridesField == null) return;
                glyphOverridesField.Clear();
                if (overrides != null) glyphOverridesField.AddRange(overrides);
                glyphOverrideLookup = null;
                glyphMetricOverridesPresent = false;
            }

            internal long DefaultVarHash48 => GlyphAtlas.DefaultVarHash(FontDataHash);
            internal long GlyphKey(uint glyphIndex) => GlyphAtlas.MakeKey(DefaultVarHash48, glyphIndex);

            private HB.hb_ot_var_axis_info_t[] cachedVariableAxes;
            private bool variableAxesQueried;
            private int[] cachedDefaultFtCoords;
            private float[] cachedDefaultAxisValues;
            private HB.hb_variation_t[] cachedDefaultHbVariations;
            private bool defaultAxisStateBuilt;
            private readonly object variationStateLock = new();

            internal HB.hb_ot_var_axis_info_t[] VariableAxes
            {
                get
                {
                    if (Volatile.Read(ref variableAxesQueried))
                        return Volatile.Read(ref cachedVariableAxes);
                    lock (variationStateLock)
                    {
                        if (!variableAxesQueried)
                        {
                            var axes = Shaper.GetVariableAxisInfos(this);
                            Volatile.Write(ref cachedVariableAxes, axes);
                            Volatile.Write(ref variableAxesQueried, true);
                        }
                        return cachedVariableAxes;
                    }
                }
            }

            public bool IsVariable => VariableAxes != null;

            /// <summary>
            /// Effective per-axis default values, aligned to <see cref="VariableAxes"/>: the configured
            /// <c>axisDefaults</c> override (clamped to the axis range) where present, otherwise the font's own
            /// fvar default. This is the instance rendered and shaped when no &lt;var&gt; tag is active, and the
            /// base the variation tag layers percentage/delta on top of. Null for non-variable fonts.
            /// </summary>
            internal float[] DefaultAxisValues
            {
                get { EnsureDefaultAxisState(); return cachedDefaultAxisValues; }
            }

            /// <summary>Natural CSS weight of the default runtime instance, including a configured variable <c>wght</c> coordinate.</summary>
            internal int DefaultWeight
            {
                get
                {
                    GetDefaultStyle(out var weight, out _);
                    return weight;
                }
            }

            /// <summary>Whether the default runtime instance is intrinsically italic or oblique, including configured variable <c>ital</c>/<c>slnt</c> coordinates.</summary>
            internal bool DefaultIsSlanted
            {
                get
                {
                    GetDefaultStyle(out _, out var slanted);
                    return slanted;
                }
            }

            /// <summary>Resolves weight and slant together from the canonical default axis coordinates, falling back to static face metadata only when the corresponding axes do not exist.</summary>
            internal void GetDefaultStyle(out int weight, out bool slanted)
                => ResolveDefaultStyle(in faceInfoField, VariableAxes, DefaultAxisValues,
                    out weight, out slanted);

            internal static void ResolveDefaultStyle(in FaceInfo faceInfo,
                HB.hb_ot_var_axis_info_t[] axes, float[] values,
                out int weight, out bool slanted)
            {
                weight = faceInfo.weightClass > 0
                    ? Math.Clamp(faceInfo.weightClass, 1, 1000)
                    : 400;
                slanted = faceInfo.isItalic;

                if (axes == null || values == null) return;

                var hasSlantAxis = false;
                slanted = false;
                for (var i = 0; i < axes.Length && i < values.Length; i++)
                {
                    var value = values[i];
                    if (float.IsNaN(value) || float.IsInfinity(value)) continue;
                    if (axes[i].tag == FontVariation.axisTags[0])
                        weight = Math.Clamp((int)Math.Round(value), 1, 1000);
                    else if (axes[i].tag == FontVariation.axisTags[2])
                    {
                        hasSlantAxis = true;
                        slanted |= value >= 0.5f;
                    }
                    else if (axes[i].tag == FontVariation.axisTags[3])
                    {
                        hasSlantAxis = true;
                        slanted |= Math.Abs(value) > 0.01f;
                    }
                }
                if (!hasSlantAxis) slanted = faceInfo.isItalic;
            }

            /// <summary>
            /// HarfBuzz variations encoding <see cref="DefaultAxisValues"/> against the true fvar defaults — only the
            /// axes that actually differ, since HarfBuzz resets unlisted axes to their fvar default. Null when nothing
            /// is overridden, so a plain run shapes on the unmodified font.
            /// </summary>
            internal HB.hb_variation_t[] DefaultHbVariations
            {
                get { EnsureDefaultAxisState(); return cachedDefaultHbVariations; }
            }

            private void EnsureDefaultAxisState()
            {
                if (Volatile.Read(ref defaultAxisStateBuilt)) return;
                lock (variationStateLock)
                {
                    if (defaultAxisStateBuilt) return;

                    var axes = VariableAxes;
                    if (axes == null)
                    {
                        Volatile.Write(ref defaultAxisStateBuilt, true);
                        return;
                    }

                    var values = BuildDefaultAxisValues(axes, axisDefaultsField,
                        out var diffCount);
                    Volatile.Write(ref cachedDefaultAxisValues, values);

                    if (diffCount != 0)
                    {
                        var variations = new HB.hb_variation_t[diffCount];
                        int k = 0;
                        for (int i = 0; i < axes.Length; i++)
                            if (FontVariation.ToFixed(values[i]) != FontVariation.ToFixed(axes[i].defaultValue))
                                variations[k++] = new HB.hb_variation_t { tag = axes[i].tag, value = values[i] };
                        Volatile.Write(ref cachedDefaultHbVariations, variations);
                    }
                    Volatile.Write(ref defaultAxisStateBuilt, true);
                }
            }

            internal static float[] BuildDefaultAxisValues(
                HB.hb_ot_var_axis_info_t[] axes, IReadOnlyList<AxisDefault> overrides,
                out int differenceCount)
            {
                differenceCount = 0;
                if (axes == null) return null;
                var values = new float[axes.Length];
                for (var i = 0; i < axes.Length; i++)
                {
                    var value = axes[i].defaultValue;
                    if (TryGetAxisDefault(overrides, axes[i].tag, out var configured))
                        value = float.IsNaN(configured) || float.IsInfinity(configured)
                            ? axes[i].defaultValue
                            : Math.Clamp(configured, axes[i].minValue, axes[i].maxValue);
                    value = FontVariation.FromFixed(FontVariation.ToFixed(value));
                    values[i] = value;
                    if (FontVariation.ToFixed(value)
                        != FontVariation.ToFixed(axes[i].defaultValue))
                        differenceCount++;
                }
                return values;
            }

            private static bool TryGetAxisDefault(IReadOnlyList<AxisDefault> overrides,
                uint tag, out float value)
            {
                value = 0f;
                if (overrides == null) return false;
                for (var i = 0; i < overrides.Count; i++)
                {
                    if ((uint)overrides[i].tag != tag) continue;
                    value = overrides[i].value;
                    return true;
                }
                return false;
            }

            internal int[] DefaultFtCoords
            {
                get
                {
                    var cached = Volatile.Read(ref cachedDefaultFtCoords);
                    if (cached != null) return cached;
                    lock (variationStateLock)
                    {
                        if (cachedDefaultFtCoords != null) return cachedDefaultFtCoords;
                        var values = DefaultAxisValues;
                        if (values == null) return null;
                        var coordinates = new int[values.Length];
                        for (int i = 0; i < values.Length; i++)
                            coordinates[i] = FontVariation.ToFixed(values[i]);
                        Volatile.Write(ref cachedDefaultFtCoords, coordinates);
                        return coordinates;
                    }
                }
            }

            #endregion

            #region FT face

            private FreeTypeFace ftFace;
            private GlyphCurveCache glyphCurveCache;
            private int cachedFaceIndex = -1;

            protected IntPtr EnsureFTFace()
            {
                if (ftFace != null) return ftFace.Pointer;
                if (!HasFontData) return IntPtr.Zero;
                if (!FT.IsInitialized) FT.Initialize();
                if (cachedFaceIndex < 0) cachedFaceIndex = faceInfoField.faceIndex;
                ftFace = FreeTypeFace.TryCreate(fontSource,
                    cachedFaceIndex < 0 ? 0 : cachedFaceIndex);
                return ftFace?.Pointer ?? IntPtr.Zero;
            }

            protected void ReleaseFTFace()
            {
                ftFace?.Dispose();
                ftFace = null;
            }

            public virtual UniTextFontError LoadFontFace()
                => fontBackend != null || glyphOutlineSource != null || EnsureFTFace() != IntPtr.Zero
                    ? UniTextFontError.Success
                    : UniTextFontError.InvalidFile;

            internal GlyphCurveCache CurveCache
            {
                get
                {
                    if (glyphCurveCache != null) return glyphCurveCache;
                    if (glyphOutlineSource != null)
                    {
                        var axes = VariableAxes;
                        int[] tags = null;
                        if (axes != null)
                        {
                            tags = new int[axes.Length];
                            for (var i = 0; i < axes.Length; i++) tags[i] = (int)axes[i].tag;
                        }
                        return glyphCurveCache = new GlyphCurveCache(glyphOutlineSource, tags);
                    }
                    var face = EnsureFTFace();
                    if (face == IntPtr.Zero) return null;
                    int fi = cachedFaceIndex < 0 ? 0 : cachedFaceIndex;
                    glyphCurveCache = new GlyphCurveCache(face, fontSource, fi);
                    return glyphCurveCache;
                }
            }

            #endregion

            #region Lookup tables

            /// <summary>(varHash48, glyphIndex) → atlas-resident glyph. Lazy-built on first access.</summary>
            public Dictionary<long, Glyph> GlyphLookupTable
            {
                get
                {
                    if (glyphLookupDictionary == null) ReadFontDefinition();
                    return glyphLookupDictionary;
                }
            }

            internal Dictionary<uint, UniTextCharacter> CharacterLookupTable
            {
                get
                {
                    if (characterLookupDictionary == null) ReadFontDefinition();
                    return characterLookupDictionary;
                }
            }

            internal int MaterializedGlyphCount => glyphLookupDictionary?.Count ?? 0;
            internal int MaterializedCharacterCount => characterLookupDictionary?.Count ?? 0;

            /// <summary>Rebuilds the runtime glyph and character lookup tables.</summary>
            public virtual void ReadFontDefinition()
            {
                InitializeGlyphLookupDictionary();
                InitializeCharacterLookupDictionary();
                AddSynthesizedCharacters();
            }

            private void InitializeGlyphLookupDictionary()
            {
                glyphLookupDictionary ??= new Dictionary<long, Glyph>();
                glyphLookupDictionary.Clear();
                if (glyphTable == null) return;
                for (var i = 0; i < glyphTable.Count; i++)
                {
                    var glyph = glyphTable[i];
                    glyphLookupDictionary.TryAdd(GlyphKey(glyph.index), glyph);
                }
            }

            private void InitializeCharacterLookupDictionary()
            {
                characterLookupDictionary ??= new Dictionary<uint, UniTextCharacter>();
                characterLookupDictionary.Clear();
                if (characterTable == null) return;
                for (var i = 0; i < characterTable.Count; i++)
                {
                    var character = characterTable[i];
                    var unicode = character.unicode;
                    if (characterLookupDictionary.TryAdd(unicode, character))
                    {
                        if (glyphLookupDictionary.TryGetValue(GlyphKey(character.glyphIndex), out var glyph))
                            character.glyph = glyph;
                    }
                }
            }

            private void AddSynthesizedCharacters()
            {
                var fontLoaded = LoadFontFace() == UniTextFontError.Success;
                AddSynthesizedCharacter(UnicodeData.Tab, fontLoaded, true);
                AddSynthesizedCharacter(UnicodeData.LineFeed, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.CarriageReturn, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.ZeroWidthSpace, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.LeftToRightMark, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.RightToLeftMark, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.LineSeparator, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.ParagraphSeparator, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.WordJoiner, fontLoaded);
                AddSynthesizedCharacter(UnicodeData.ArabicLetterMark, fontLoaded);
            }

            private void AddSynthesizedCharacter(int unicode, bool fontLoaded, bool addImmediately = false)
            {
                var cp = (uint)unicode;
                if (characterLookupDictionary.ContainsKey(cp)) return;
                Glyph glyph;
                if (fontLoaded)
                {
                    var glyphIdx = Shaper.GetGlyphIndex(this, cp);
                    if (glyphIdx != 0)
                    {
                        if (!addImmediately) return;
                        if (fontBackend != null || glyphOutlineSource != null)
                        {
                            var advance = Shaper.GetGlyphAdvance(this, glyphIdx);
                            glyph = new Glyph(glyphIdx,
                                new GlyphMetrics(0, 0, 0, 0, advance), GlyphRect.zero, 0);
                            characterLookupDictionary.Add(cp, new UniTextCharacter(cp, glyph));
                        }
                        else
                        {
                            var face = EnsureFTFace();
                            if (face != IntPtr.Zero)
                            {
                                FT.SetPixelSize(face, unitsPerEmField);
                                if (FT.LoadGlyph(face, glyphIdx, FT.LOAD_DEFAULT | FT.LOAD_NO_BITMAP))
                                {
                                    var ftMetrics = FT.GetGlyphMetrics(face);
                                    var advance = ftMetrics.advanceX / 64f;
                                    glyph = new Glyph(glyphIdx,
                                        new GlyphMetrics(ftMetrics.width, ftMetrics.height, ftMetrics.bearingX, ftMetrics.bearingY, advance),
                                        GlyphRect.zero, 0);
                                    characterLookupDictionary.Add(cp, new UniTextCharacter(cp, glyph));
                                }
                            }
                        }
                        return;
                    }
                }
                glyph = new Glyph(0, new GlyphMetrics(0, 0, 0, 0, 0), GlyphRect.zero, 0);
                characterLookupDictionary.Add(cp, new UniTextCharacter(cp, glyph));
            }

            public uint GetGlyphIndexForUnicode(uint unicode)
            {
                uint glyphIndex = 0;
                if (HasFontBackend) glyphIndex = Shaper.GetGlyphIndex(this, unicode);
                if (glyphIndex == 0)
                {
                    uint specialCodepoint = unicode switch
                    {
                        UnicodeData.NoBreakSpace => UnicodeData.Space,
                        UnicodeData.SoftHyphen => UnicodeData.Hyphen,
                        UnicodeData.NonBreakingHyphen => UnicodeData.Hyphen,
                        _ => 0
                    };
                    if (specialCodepoint != 0 && HasFontBackend)
                        glyphIndex = Shaper.GetGlyphIndex(this, specialCodepoint);
                }
                return glyphIndex;
            }

            public void RegisterCharacterEntries(List<(uint unicode, uint glyphIndex)> entries)
            {
                if (entries == null || entries.Count == 0) return;
                if (characterLookupDictionary == null) ReadFontDefinition();
                characterTable ??= new List<UniTextCharacter>();
                for (int i = 0; i < entries.Count; i++)
                {
                    var (unicode, glyphIndex) = entries[i];
                    if (characterLookupDictionary.ContainsKey(unicode)) continue;
                    if (!glyphLookupDictionary.TryGetValue(GlyphKey(glyphIndex), out var glyph)) continue;
                    var character = new UniTextCharacter(unicode, glyphIndex) { glyph = glyph };
                    characterTable.Add(character);
                    characterLookupDictionary[unicode] = character;
                }
            }

            /// <summary>Returns whether the glyph has an entry in the selected distance-field atlas for this font's default variation.</summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public virtual bool HasGlyphInAtlas(uint glyphIndex, UniTextRenderMode mode) =>
                GlyphAtlas.TryGetExistingInstance(mode, out var atlas)
                && atlas.TryGetEntry(DefaultVarHash48, glyphIndex, out _);

            /// <summary>
            /// A colour glyph whose pixels live outside the glyph atlases: the texture and the region of
            /// it the glyph shows, with the glyph's design metrics. False for every glyph the atlases hold;
            /// a true result means the mesh samples <paramref name="texture"/> through its own sub-mesh.
            /// </summary>
            internal virtual bool TryGetColorTexture(uint glyphIndex, out Texture2D texture,
                out Vector2 uvMin, out Vector2 uvMax, out GlyphMetrics metrics)
            {
                texture = null;
                uvMin = uvMax = default;
                metrics = default;
                return false;
            }

            /// <summary>
            /// True when the glyph was extracted before and has real ink — so a missing
            /// atlas entry for it means eviction (re-rasterizable), not an empty outline.
            /// </summary>
            internal bool IsNonEmptyGlyph(long key)
                => glyphLookupDictionary != null
                   && glyphLookupDictionary.TryGetValue(key, out var g)
                   && g.metrics.width > 0 && g.metrics.height > 0;

            [ThreadStatic] private static HashSet<uint> toAddSet;
            [ThreadStatic] private static List<uint> toAddList;

            protected List<uint> FilterNewGlyphs(List<uint> glyphIndices)
            {
                toAddSet ??= new HashSet<uint>();
                toAddSet.Clear();
                for (var i = 0; i < glyphIndices.Count; i++)
                {
                    var idx = glyphIndices[i];
                    if (glyphLookupDictionary == null || !glyphLookupDictionary.ContainsKey(GlyphKey(idx)))
                        toAddSet.Add(idx);
                }
                if (toAddSet.Count == 0) return null;
                toAddList ??= new List<uint>(256);
                toAddList.Clear();
                foreach (var idx in toAddSet) toAddList.Add(idx);
                return toAddList;
            }

            #endregion

            #region Batch pipeline

            internal struct PreparedBatch
            {
                public PooledBuffer<uint> filteredGlyphs;
                /// <summary>Silhouette-field request per filtered glyph (see <see cref="ColorGlyphField"/>); unallocated or 0 when only the glyph itself renders.</summary>
                public PooledBuffer<byte> fieldExtents;
                public long varHash48;
                public int[] ftCoords;

                /// <summary>Returns both pooled buffers.</summary>
                public void Return()
                {
                    filteredGlyphs.Return();
                    fieldExtents.Return();
                }
            }

            private PooledBuffer<uint> filteredForBatch;

            /// <summary>
            /// Filters a collected glyph set down to the glyphs that must rasterize this frame and pins
            /// the rest for the batch. <paramref name="fieldRequests"/> carries silhouette-field requests
            /// per glyph index; outline fonts ignore them, colour fonts honour them.
            /// </summary>
            internal virtual PreparedBatch? PrepareGlyphBatch(
                List<uint> glyphIndices, UniTextRenderMode mode,
                long varHash48 = 0, int[] ftCoords = null, FastIntDictionary<byte> fieldRequests = null)
            {
                if (glyphIndices == null || glyphIndices.Count == 0) return null;
                if (glyphLookupDictionary == null) ReadFontDefinition();
                var cache = CurveCache;
                if (cache == null) return null;
                _ = GlyphOverrideLookup;
                if (ftCoords == null)
                    _ = DefaultFtCoords;

                var atlas = GlyphAtlas.GetInstance(mode);
                var varHash = varHash48 != 0 ? varHash48 : DefaultVarHash48;

                toAddSet ??= new HashSet<uint>();
                toAddSet.Clear();
                for (int i = 0; i < glyphIndices.Count; i++)
                    toAddSet.Add(glyphIndices[i]);

                filteredForBatch.FakeClear();
                if (filteredForBatch.data == null)
                    filteredForBatch.EnsureCapacity(toAddSet.Count);

                int diagNewRaster = 0, diagReRasterEvicted = 0, diagEmptySkipped = 0;
                foreach (var glyphIndex in toAddSet)
                {
                    var key = GlyphAtlas.MakeKey(varHash, glyphIndex);
                    if (glyphLookupDictionary.TryGetValue(key, out var known))
                    {
                        bool hasInk = known.metrics.width > 0f && known.metrics.height > 0f;
                        if (!hasInk)
                        {
                            diagEmptySkipped++;
                            continue;
                        }
                        if (atlas.TryGetEntry(key, out var existingEntry))
                        {
                            if (existingEntry.refCount == 0)
                                atlas.ProtectForBatch(key);
                            continue;
                        }
                        diagReRasterEvicted++;
                    }
                    else
                    {
                        diagNewRaster++;
                    }
                    filteredForBatch.Add(glyphIndex);
                }

                CatZones.raster.MeowFormat("[Raster prepare] {0} varHash=0x{1:X}: render={2} (new={3}, reRaster-evicted={4}, empty-skipped={5}) of requested={6}",
                    Name, varHash, filteredForBatch.count, diagNewRaster, diagReRasterEvicted, diagEmptySkipped, toAddSet.Count);

                if (filteredForBatch.count == 0) return null;
                var owned = filteredForBatch;
                filteredForBatch = default;
                return new PreparedBatch
                {
                    filteredGlyphs = owned,
                    varHash48 = varHash,
                    ftCoords = ftCoords
                };
            }

            private const int ParallelThreshold = 16;

            private struct ExtractedGlyph
            {
                public uint glyphIndex;
                public GlyphCurveCache.GlyphCurveData curveData;
                public int segmentBufferIndex;
                public int segmentOffset;
                public int segmentCount;
                public bool isNew;
                public int tileSize;
                public float aspect;
            }

            private int ApplyTileSizeOverride(uint glyphIndex, int tileSize)
            {
                var lookup = GlyphOverrideLookup;
                if (lookup != null && lookup.TryGetValue(glyphIndex, out var ov) && ov.tileSizeOverride > 0)
                    return GlyphAtlas.OffsetTileSize(ov.tileSizeOverride, 0);
                return GlyphAtlas.OffsetTileSize(tileSize, tileSizeOffsetField);
            }

            private class RenderedBatch
            {
                public ExtractedGlyph[] extracted;
                public int count;
                public PooledBuffer<GlyphCurveCache.Segment>[] segmentBuffers;
                public int segmentBufferCount;
            }

            internal virtual object RenderPreparedBatch(PreparedBatch batch)
            {
                var cache = CurveCache;
                if (cache == null) return null;

                var glyphs = batch.filteredGlyphs;
                var varHash = batch.varHash48 != 0 ? batch.varHash48 : DefaultVarHash48;
                bool useParallel =
#if UNITY_WEBGL && !UNITY_EDITOR
                    false;
#else
                    !GlyphAtlas.forceSingleThreaded && glyphs.count >= ParallelThreshold;
#endif
                int bufferCount = useParallel ? Math.Min(Environment.ProcessorCount, glyphs.count) : 1;

                var result = new RenderedBatch
                {
                    extracted = new ExtractedGlyph[glyphs.count],
                    count = glyphs.count,
                    segmentBuffers = new PooledBuffer<GlyphCurveCache.Segment>[bufferCount],
                    segmentBufferCount = bufferCount
                };

                try
                {
                    if (!useParallel)
                        RenderSequential(cache, glyphs, result, varHash, batch.ftCoords);
                    else
                        RenderParallel(cache, glyphs, result, varHash, batch.ftCoords);
                    return result;
                }
                catch
                {
                    ReleaseRenderedBatch(result);
                    throw;
                }
            }

            private void RenderSequential(GlyphCurveCache cache, PooledBuffer<uint> glyphs, RenderedBatch result,
                long varHash, int[] ftCoords)
            {
                var face = cache.RentFace(ftCoords ?? DefaultFtCoords);
                var buf = new PooledBuffer<GlyphCurveCache.Segment>();
                try
                {
                    for (int i = 0; i < glyphs.count; i++)
                    {
                        uint gi = glyphs[i];
                        int segStart = buf.count;
                        var data = cache.ExtractWithFace(face, gi, ref buf);
                        int segCount = buf.count - segStart;
                        float tileAspect = GlyphAtlas.ComputeAspect(in data);
                        float tileGlyphH = data.designHeight / (float)UnitsPerEm;
                        result.extracted[i] = new ExtractedGlyph
                        {
                            glyphIndex = gi,
                            curveData = data,
                            segmentBufferIndex = 0,
                            segmentOffset = segStart,
                            segmentCount = segCount,
                            isNew = !glyphLookupDictionary.ContainsKey(GlyphAtlas.MakeKey(varHash, gi)),
                            tileSize = ApplyTileSizeOverride(gi, GlyphAtlas.ClassifyTileSize(
                                buf.data.AsSpan(segStart, segCount), tileAspect, tileGlyphH, sdfDetailMultiplierField)),
                            aspect = tileAspect
                        };
                    }
                }
                finally
                {
                    result.segmentBuffers[0] = buf;
                    cache.ReturnFace(face);
                }
            }

            private void RenderParallel(GlyphCurveCache cache, PooledBuffer<uint> glyphs, RenderedBatch result,
                long varHash, int[] ftCoords)
            {
                int count = glyphs.count;
                int workerCount = result.segmentBufferCount;
                int chunkSize = (count + workerCount - 1) / workerCount;

                Parallel.For(0, workerCount, workerId =>
                {
                    int start = workerId * chunkSize;
                    int end = Math.Min(start + chunkSize, count);
                    if (start >= end) return;

                    var face = cache.RentFace(ftCoords ?? DefaultFtCoords);
                    var buf = new PooledBuffer<GlyphCurveCache.Segment>();
                    try
                    {
                        for (int i = start; i < end; i++)
                        {
                            uint gi = glyphs[i];
                            int segStart = buf.count;
                            var data = cache.ExtractWithFace(face, gi, ref buf);
                            int segCount = buf.count - segStart;
                            float tileAspect = GlyphAtlas.ComputeAspect(in data);
                            float tileGlyphH = data.designHeight / (float)UnitsPerEm;
                            result.extracted[i] = new ExtractedGlyph
                            {
                                glyphIndex = gi,
                                curveData = data,
                                segmentBufferIndex = workerId,
                                segmentOffset = segStart,
                                segmentCount = segCount,
                                isNew = !glyphLookupDictionary.ContainsKey(GlyphAtlas.MakeKey(varHash, gi)),
                                tileSize = ApplyTileSizeOverride(gi, GlyphAtlas.ClassifyTileSize(
                                    buf.data.AsSpan(segStart, segCount), tileAspect, tileGlyphH, sdfDetailMultiplierField)),
                                aspect = tileAspect
                            };
                        }
                    }
                    finally
                    {
                        result.segmentBuffers[workerId] = buf;
                        cache.ReturnFace(face);
                    }
                });
            }

            internal virtual int PackRenderedBatch(object renderedObj, PreparedBatch batch, UniTextRenderMode mode)
            {
                if (renderedObj is not RenderedBatch rendered) return 0;

                var atlas = GlyphAtlas.GetInstance(mode);
                var varHash = batch.varHash48 != 0 ? batch.varHash48 : DefaultVarHash48;
                var fontHash = FontDataHash;
                bool mutationStarted = false;
                try
                {
                    glyphLookupDictionary ??= new Dictionary<long, Glyph>();
                    glyphTable ??= new List<Glyph>();

                    int totalSegments = 0;
                    for (int i = 0; i < rendered.count; i++)
                        totalSegments += rendered.extracted[i].segmentCount;
                    atlas.ReservePendingSegments(totalSegments);

                    int added = 0;
                    for (int i = 0; i < rendered.count; i++)
                    {
                        ref var eg = ref rendered.extracted[i];
                        var buf = rendered.segmentBuffers[eg.segmentBufferIndex];
                        var span = buf.data.AsSpan(eg.segmentOffset, eg.segmentCount);
                        float glyphH = eg.curveData.designHeight / (float)UnitsPerEm;
                        var metrics = new GlyphMetrics(
                            eg.curveData.bboxMaxX - eg.curveData.bboxMinX,
                            eg.curveData.bboxMaxY - eg.curveData.bboxMinY,
                            eg.curveData.bearingX,
                            eg.curveData.bearingY,
                            eg.curveData.advanceX
                        );
                        mutationStarted = true;
                        atlas.EnsureGlyph(varHash, eg.glyphIndex, fontHash, in eg.curveData,
                            span, eg.tileSize, glyphH, eg.aspect, in metrics);

                        if (!eg.isNew) continue;
                        var glyph = new Glyph(eg.glyphIndex, metrics, GlyphRect.zero, 0);
                        if (varHash == DefaultVarHash48) glyphTable.Add(glyph);
                        glyphLookupDictionary[GlyphAtlas.MakeKey(varHash, eg.glyphIndex)] = glyph;
                        added++;
                    }

                    return added;
                }
                catch (Exception failure)
                {
                    if (mutationStarted)
                        atlas.RecoverAfterFailedMutation(failure);
                    throw;
                }
            }

            internal virtual void ReleaseRenderedBatch(object renderedObj)
            {
                if (renderedObj is not RenderedBatch rendered) return;
                for (int i = 0; i < rendered.segmentBufferCount; i++)
                    rendered.segmentBuffers[i].Return();
            }

            /// <summary>Re-sources a glyph and re-rasterizes its atlas tile at <paramref name="requiredTier"/>; a colour font re-renders the bitmap and upgrades the silhouette field the key addresses.</summary>
            internal virtual void ReExtractForTierUpgrade(
                uint glyphIndex, long varHash48, int[] ftCoords,
                UniTextRenderMode mode, byte requiredTier)
            {
                var cache = CurveCache;
                if (cache == null) return;
                var atlas = GlyphAtlas.GetInstance(mode);
                var face = cache.RentFace(ftCoords ?? DefaultFtCoords);
                var buf = new PooledBuffer<GlyphCurveCache.Segment>();
                bool mutationStarted = false;
                try
                {
                    var curveData = cache.ExtractWithFace(face, glyphIndex, ref buf);
                    float glyphH = curveData.designHeight / (float)UnitsPerEm;
                    float aspect = GlyphAtlas.ComputeAspect(in curveData);
                    atlas.ReservePendingSegments(buf.count);
                    mutationStarted = true;
                    atlas.UpgradeGlyphTier(
                        GlyphAtlas.MakeKey(varHash48, glyphIndex),
                        buf.Span, glyphH, aspect, requiredTier);
                }
                catch (Exception failure)
                {
                    if (mutationStarted)
                        atlas.RecoverAfterFailedMutation(failure);
                    throw;
                }
                finally
                {
                    buf.Return();
                    cache.ReturnFace(face);
                }
            }

            /// <summary>Re-extracts a glyph and grows its atlas tile by <paramref name="tileSizeBoost"/> size classes above its default classification (grow-only, see <see cref="GlyphAtlas.UpgradeGlyphTileSize"/>). Returns whether the tile actually grew. A colour font grows the silhouette field the key addresses.</summary>
            internal virtual bool ReExtractForTileSizeUpgrade(
                uint glyphIndex, long varHash48, int[] ftCoords,
                UniTextRenderMode mode, int tileSizeBoost)
            {
                var cache = CurveCache;
                if (cache == null) return false;
                var atlas = GlyphAtlas.GetInstance(mode);
                var face = cache.RentFace(ftCoords ?? DefaultFtCoords);
                var buf = new PooledBuffer<GlyphCurveCache.Segment>();
                bool mutationStarted = false;
                try
                {
                    var curveData = cache.ExtractWithFace(face, glyphIndex, ref buf);
                    float glyphH = curveData.designHeight / (float)UnitsPerEm;
                    float aspect = GlyphAtlas.ComputeAspect(in curveData);
                    int target = GlyphAtlas.OffsetTileSize(
                        GlyphAtlas.ClassifyTileSize(buf.Span, aspect, glyphH),
                        tileSizeBoost);
                    atlas.ReservePendingSegments(buf.count);
                    mutationStarted = true;
                    bool grew = atlas.UpgradeGlyphTileSize(
                        GlyphAtlas.MakeKey(varHash48, glyphIndex),
                        buf.Span, glyphH, aspect, target);
                    return grew;
                }
                catch (Exception failure)
                {
                    if (mutationStarted)
                        atlas.RecoverAfterFailedMutation(failure);
                    throw;
                }
                finally
                {
                    buf.Return();
                    cache.ReturnFace(face);
                }
            }

            #endregion

            #region Atlas state

            internal event Action Changed;

            /// <summary>
            /// Fired after a font's atlas entries are removed. All UniText components subscribe
            /// because fallback, system, and emoji glyph holders have no per-font subscription.
            /// Trackers discard only keys that disappeared, preserving unrelated atlas refs.
            /// </summary>
            internal static event Action AnyAtlasEntriesCleared;

            [NonSerialized] internal Shaper.FontCacheEntry shaperCache;

            public virtual void ClearDynamicData()
            {
                glyphTable?.Clear();
                characterTable?.Clear();
                glyphLookupDictionary?.Clear();
                characterLookupDictionary?.Clear();

                glyphCurveCache?.Dispose();
                glyphCurveCache = null;
                ReleaseFTFace();

                filteredForBatch.Return();

                ClearAtlasEntries();
                Shaper.ClearCache(this);
            }

            internal void InvalidateAtlasData()
            {
                glyphTable?.Clear();
                characterTable?.Clear();
                glyphLookupDictionary?.Clear();
                characterLookupDictionary?.Clear();
                glyphOverrideLookup = null;
                glyphMetricOverridesPresent = false;
                ClearAtlasEntries();
            }

            public void InvokeChanged() => Changed?.Invoke();

            private void ClearAtlasEntries()
            {
#if UNITY_EDITOR
                if (EditorLifecycle.IsReloading) return;
#endif
                if (suppressAtlasInvalidation) return;
                Changed?.Invoke();
                GlyphAtlas.ForEachInstance(a => a.ClearForFont(FontDataHash));
                AnyAtlasEntriesCleared?.Invoke();
            }

            #endregion

            #region Live registry

            private static readonly object liveCoresLock = new();
            private static readonly List<WeakReference<Core>> liveCores = new();
            private static bool suppressAtlasInvalidation;

            private void Register()
            {
                lock (liveCoresLock)
                {
                    if (liveCores.Count >= 1024 && (liveCores.Count & 255) == 0)
                        for (var i = liveCores.Count - 1; i >= 0; i--)
                            if (!liveCores[i].TryGetTarget(out _)) liveCores.RemoveAt(i);
                    liveCores.Add(new WeakReference<Core>(this));
                }
            }

            internal void ClearVariation(long varHash48)
            {
                if (glyphLookupDictionary == null || glyphLookupDictionary.Count == 0) return;
                var keys = ArrayPool<long>.Rent(glyphLookupDictionary.Count);
                var count = 0;
                foreach (var pair in glyphLookupDictionary)
                    if (((pair.Key >> 16) & 0xFFFF_FFFFFFFFL)
                        == (varHash48 & 0xFFFF_FFFFFFFFL))
                        keys[count++] = pair.Key;
                for (var i = 0; i < count; i++) glyphLookupDictionary.Remove(keys[i]);
                ArrayPool<long>.Return(keys);
                if (count >= 256 && count >= glyphLookupDictionary.Count)
                    glyphLookupDictionary.TrimExcess();
            }

            private bool disposed;

            /// <summary>
            /// Releases the face handle when disposal never happened. The glyph curve cache is
            /// deliberately left alone: finalization order is undefined, so touching a managed
            /// member here can hit one that is already finalized — its face pool is a
            /// <see cref="System.Collections.Concurrent.ConcurrentBag{T}"/> whose thread-local
            /// storage throws once disposed, and its disposal waits on a lock, which on the
            /// finalizer thread would stall every remaining finalizer. Nothing leaks: the cache owns
            /// no unmanaged memory, and every face it pools finalizes itself.
            /// </summary>
            ~Core()
            {
                if (disposed) return;
                if (FT.IsInitialized) ReleaseFTFace();
                glyphCurveCache = null;
                Interlocked.Exchange(ref shaperCache, null)?.Dispose();
                glyphOutlineSource?.Dispose();
                Interlocked.Exchange(ref fontBackend, null)?.Dispose();
            }

            /// <summary>Releases font faces, outline sources, curve caches, batch buffers, atlas entries and styled fallback clones, and unregisters from the live registry. Idempotent.</summary>
            public virtual void Dispose()
            {
                if (disposed) return;
                disposed = true;
                GC.SuppressFinalize(this);
                lock (liveCoresLock)
                    for (var i = liveCores.Count - 1; i >= 0; i--)
                        if (!liveCores[i].TryGetTarget(out var core) || ReferenceEquals(core, this))
                            liveCores.RemoveAt(i);

                glyphCurveCache?.Dispose();
                glyphCurveCache = null;
                ReleaseFTFace();
                filteredForBatch.Return();
                ClearAtlasEntries();
                Shaper.ClearCache(this);
                glyphOutlineSource?.Dispose();
                Interlocked.Exchange(ref fontBackend, null)?.Dispose();

                ClearStyledFallbacks(true);
                fontSource = null;
            }

            /// <summary>Clears a runtime and every styled clone derived from it under one invalidation scope.</summary>
            internal static void ClearDynamicData(Core original, bool notify)
            {
                if (original == null) return;
                var previous = suppressAtlasInvalidation;
                suppressAtlasInvalidation |= !notify;
                try
                {
                    foreach (var core in GetLiveCores())
                        core.ClearStyledFallbackFor(original);
                    original.ClearDynamicData();
                }
                finally
                {
                    suppressAtlasInvalidation = previous;
                }
            }

            internal static void ClearAllLiveDynamicData(bool notify)
            {
                var previous = suppressAtlasInvalidation;
                suppressAtlasInvalidation |= !notify;
                try
                {
                    var cores = GetLiveCores();
                    foreach (var core in cores)
                        core.ClearStyledFallbacks();
                    foreach (var core in cores)
                        core.ClearDynamicData();
                }
                finally
                {
                    suppressAtlasInvalidation = previous;
                }
            }

            private static Core[] GetLiveCores()
            {
                lock (liveCoresLock)
                {
                    var result = new List<Core>(liveCores.Count);
                    for (var i = liveCores.Count - 1; i >= 0; i--)
                    {
                        if (liveCores[i].TryGetTarget(out var core)) result.Add(core);
                        else liveCores.RemoveAt(i);
                    }
                    return result.ToArray();
                }
            }

            #endregion

            #region FaceInfo builder

            /// <summary>Reads a full <see cref="FaceInfo"/> from an open FT face. Pulls OS/2 metrics when present, falls back to glyph-bearing measurements (H, x, space) when not.</summary>
            internal static FaceInfo BuildFullFaceInfo(IntPtr face)
            {
                var ftInfo = FT.GetFaceInfo(face);
                var ext = FT.GetExtendedFaceInfo(face);

                var fi = new FaceInfo
                {
                    faceIndex = ftInfo.faceIndex,
                    familyName = ext.familyName,
                    styleName = ext.styleName,
                    unitsPerEm = ftInfo.unitsPerEm,
                    ascentLine = ftInfo.ascender,
                    descentLine = ftInfo.descender,
                    lineHeight = ftInfo.height,
                    underlineOffset = ext.underlinePosition,
                    underlineThickness = ext.underlineThickness,
                    weightClass = ext.weightClass > 0 ? ext.weightClass : 400,
                    isItalic = (ext.styleFlags & 1) != 0,
                };

                if (fi.lineHeight <= 0)
                    fi.lineHeight = Mathf.RoundToInt((fi.ascentLine - fi.descentLine) * 1.2f);

                if (ext.hasOS2)
                {
                    fi.capLine = ext.capHeight;
                    fi.meanLine = ext.xHeight;
                    fi.strikethroughOffset = ext.strikeoutPosition;
                    fi.strikethroughThickness = ext.strikeoutSize;
                    fi.superscriptOffset = ext.superscriptYOffset;
                    fi.superscriptSize = ext.superscriptYSize;
                    fi.subscriptOffset = ext.subscriptYOffset;
                    fi.subscriptSize = ext.subscriptYSize;
                    fi.typoAscent = ext.typoAscender;
                    fi.typoDescent = ext.typoDescender;
                    fi.typoLineGap = ext.typoLineGap;
                    fi.winAscent = ext.winAscent;
                    fi.winDescent = -ext.winDescent;
                    fi.useTypoMetrics = (ext.fsSelection & 0x80) != 0;
                }
                else
                {
                    int capBearingY = FT.GetGlyphBearingYUnscaled(face, 'H');
                    fi.capLine = capBearingY > 0 ? capBearingY : Mathf.RoundToInt(fi.ascentLine * 0.75f);
                    int xBearingY = FT.GetGlyphBearingYUnscaled(face, 'x');
                    fi.meanLine = xBearingY > 0 ? xBearingY : Mathf.RoundToInt(fi.ascentLine * 0.5f);
                    fi.strikethroughOffset = Mathf.RoundToInt(fi.meanLine * 0.5f);
                    fi.strikethroughThickness = fi.underlineThickness > 0
                        ? fi.underlineThickness
                        : Mathf.RoundToInt(fi.ascentLine * 0.05f);
                    fi.superscriptOffset = fi.ascentLine;
                    fi.superscriptSize = fi.unitsPerEm;
                    fi.subscriptOffset = fi.descentLine;
                    fi.subscriptSize = fi.unitsPerEm;
                }

                int spaceAdvance = FT.GetGlyphAdvanceUnscaled(face, ' ');
                fi.tabWidth = spaceAdvance > 0 ? spaceAdvance : fi.ascentLine;
                return fi;
            }

            #endregion
        }

        #endregion
    }

    [Serializable]
    internal class UniTextCharacter
    {
        public uint unicode;
        public uint glyphIndex;
        [NonSerialized] public Glyph glyph;

        public UniTextCharacter() { }

        public UniTextCharacter(uint unicode, uint glyphIndex)
        {
            this.unicode = unicode;
            this.glyphIndex = glyphIndex;
        }

        public UniTextCharacter(uint unicode, Glyph glyph)
        {
            this.unicode = unicode;
            this.glyph = glyph;
            glyphIndex = glyph.index;
        }
    }
}
