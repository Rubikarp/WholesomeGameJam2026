using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// A <see cref="UniTextFont"/> that takes only the raw font bytes from a <see cref="Source"/> font
    /// and defines everything else itself — face metrics, units-per-em, render settings and glyph
    /// overrides are fully independent and overridable. Use to render the same TTF/OTF with a different
    /// look or metrics without duplicating the bytes. Each variant has its own atlas and shaper cache
    /// (the base mixes the asset instance id into <see cref="UniTextFont.FontDataHash"/>), so variants
    /// and their source coexist without overwriting each other's glyphs. On first assignment the face
    /// metrics are seeded from the source font; after that they are owned by the variant.
    /// </summary>
    public partial class UniTextFontVariant : UniTextFont
    {
        protected internal override bool UsesEmbeddedSource => false;

        /// <summary>Font supplying the raw bytes shared by this variant.</summary>
        [SerializeField, StateProperty(nameof(ApplySourceChange))]
        [Tooltip("Font asset to take the raw font bytes from. Every other setting is defined by this variant.")]
        private UniTextFont source;

        /// <summary>Face selected from the source font file.</summary>
        [SerializeField, StateProperty(nameof(ApplyFaceIndexChange))]
        [Tooltip("Face index within the source's font file. 0 for a normal font; > 0 selects a sub-face of a TrueType Collection (.ttc) or other multi-face file. The source's bytes are shared, not duplicated.")]
        private int faceIndex;

        [NonSerialized] private UniTextFont subscribedSource;

        public override byte[] FontData => CopyFontData();
        public override byte[] CopyFontData() => ResolveRawSource()?.CopyFontData();
        public override bool HasFontData => ResolveRawSource()?.HasFontData == true;
        protected internal override int RawFontDataHash => ResolveRawSource()?.RawFontDataHash ?? 0;

        protected override Core CreateRuntime()
        {
            var fontSource = ResolveRawSource()?.CaptureFontSource();
            return fontSource == null
                ? null
                : BuildRuntimeFromSource(fontSource, typeof(UniTextFontVariant));
        }

        internal override FontRuntimeSlot CaptureRuntimeSlot()
            => GetType() == typeof(UniTextFontVariant)
                ? CaptureLazyRuntimeSlot(CaptureVariantRuntimeFactory())
                : CaptureEagerRuntimeSlot();

        internal override void RefreshUnmaterializedRuntimeSlot()
            => ReplaceUnmaterializedRuntimeFactory(CaptureVariantRuntimeFactory());

        private Func<Core> CaptureVariantRuntimeFactory()
        {
            var fontSource = ResolveRawSource()?.CaptureFontSource();
            var snapshot = CaptureRuntimeSnapshot();
            return () => fontSource == null ? null : snapshot.Create(fontSource);
        }

        internal override FontSource CaptureFontSource()
            => GetType() == typeof(UniTextFontVariant)
                ? ResolveRawSource()?.CaptureFontSource()
                : Runtime?.Source;

        protected override void OnEnable()
        {
            _ = ResolveRawSource();
            base.OnEnable();
            if (faceIndex < 0) FaceIndex = 0;
            SubscribeToSource();
            CaptureRuntimeSlot();
        }

        protected override void OnDisable()
        {
            UnsubscribeFromSource();
            base.OnDisable();
        }

#if UNITY_EDITOR
        internal override int CompressedFontDataSize
            => ResolveRawSource()?.CompressedFontDataSize ?? 0;
        internal override int RawFontDataSize
            => ResolveRawSource()?.RawFontDataSize ?? 0;

        protected override void EnsureFaceInfoFromFont()
        {
            if (!SeedFaceInfo(true)) return;
            InvalidateRuntime();
            NotifyConfigurationChanged();
            UnityEditor.EditorUtility.SetDirty(this);
        }

        internal override bool TryReadFaceInfo(int index, out FaceInfo nextFaceInfo,
            out int nextUnitsPerEm) => TryReadSourceFaceInfo(
            index, out nextFaceInfo, out nextUnitsPerEm);
#endif

        private void ApplySourceChange(StateMember member, UniTextFont previous,
            ref UniTextFont current)
        {
            if (current == previous) return;
            try { _ = ResolveRawSource(current, this); }
            catch
            {
                current = previous;
                throw;
            }
            UnsubscribeFromSource();
            SeedFaceInfo(true);
            InvalidateRuntime();
            SubscribeToSource();
            PublishStateChange(member);
        }

        private void ApplyFaceIndexChange(StateMember member, int previous, ref int current)
        {
            if (current < 0) current = 0;
            if (current == previous) return;
            SeedFaceInfo(true);
            InvalidateRuntime();
            PublishStateChange(member);
        }

        /// <summary>
        /// Reads the selected <see cref="FaceIndex"/> from the source's bytes and copies that face's metrics
        /// (and faceIndex) into this variant. <paramref name="force"/> overwrites existing metrics; otherwise it
        /// seeds only when unset or pointing at a different face, preserving user edits for the current face.
        /// </summary>
        private bool SeedFaceInfo(bool force)
        {
            if (source == null) return false;
            if (!force && faceInfo.unitsPerEm > 0 && faceInfo.faceIndex == faceIndex)
                return false;

            if (TryReadSourceFaceInfo(faceIndex, out var nextFaceInfo,
                    out var nextUnitsPerEm))
            {
                var changed = !faceInfo.Equals(nextFaceInfo)
                              || unitsPerEm != nextUnitsPerEm;
                SetResolvedMetadata(nextFaceInfo, nextUnitsPerEm);
                return RefreshVariableAxisMetadata() || changed;
            }
            return false;
        }

        private bool RefreshVariableAxisMetadata(bool useSourceMetadata = true)
        {
            if (source == null) return false;
            HB.hb_ot_var_axis_info_t[] axes;
            if (useSourceMetadata && faceIndex == source.FaceInfo.faceIndex
                && source.TryCaptureVariableAxisMetadata(out axes, out _))
                return SetVariableAxisMetadata(axes);

            var fontSource = ResolveRawSource()?.CaptureFontSource();
            return fontSource != null
                   && SetVariableAxisMetadata(ReadVariableAxes(fontSource, faceIndex));
        }

        private bool TryReadSourceFaceInfo(int index, out FaceInfo nextFaceInfo,
            out int nextUnitsPerEm)
        {
            nextFaceInfo = default;
            nextUnitsPerEm = 0;
            if (source == null) return false;

            if (index <= 0)
            {
                nextFaceInfo = source.FaceInfo;
                nextUnitsPerEm = source.UnitsPerEm;
                return true;
            }

            var fontSource = ResolveRawSource()?.CaptureFontSource();
            if (fontSource == null) return false;

            if (!FT.IsInitialized) FT.Initialize();
            using var backing = fontSource.Open();
            var face = FT.LoadFace(backing.Pointer, backing.Length, index);
            if (face == IntPtr.Zero) return false;

            nextFaceInfo = Core.BuildFullFaceInfo(face);
            nextUnitsPerEm = nextFaceInfo.unitsPerEm > 0
                ? nextFaceInfo.unitsPerEm
                : source.UnitsPerEm;
            FT.UnloadFace(face);
            return true;
        }

        private UniTextFont ResolveRawSource()
            => ResolveRawSource(source, this);

        private static UniTextFont ResolveRawSource(UniTextFont candidate,
            UniTextFontVariant owner)
        {
            if (candidate == null) return null;

            var slow = candidate;
            var fast = candidate;
            while (true)
            {
                slow = NextVariantSource(slow, owner);
                fast = NextVariantSource(fast, owner);
                if (fast != null) fast = NextVariantSource(fast, owner);
                if (slow == null || fast == null) break;
                if (ReferenceEquals(slow, fast))
                    throw new InvalidOperationException(
                        $"Font variant '{owner.name}' has a cyclic source chain.");
            }

            var resolved = candidate;
            while (resolved is UniTextFontVariant variant) resolved = variant.source;
            return resolved;
        }

        private static UniTextFont NextVariantSource(UniTextFont value,
            UniTextFontVariant owner)
        {
            if (ReferenceEquals(value, owner))
                throw new InvalidOperationException(
                    $"Font variant '{owner.name}' cannot reference itself through its source chain.");
            return value is UniTextFontVariant variant ? variant.source : null;
        }

        private void SubscribeToSource()
        {
            if (source == null || subscribedSource == source) return;
            source.Changed += OnSourceChanged;
            subscribedSource = source;
        }

        private void UnsubscribeFromSource()
        {
            if (subscribedSource == null) return;
            subscribedSource.Changed -= OnSourceChanged;
            subscribedSource = null;
        }

        private void OnSourceChanged(IStateChangeSource source, in StateChange change)
        {
            if (this == null)
            {
                UnsubscribeFromSource();
                return;
            }
            if (!InvalidatesRawSource(in change)) return;
            InvalidateVariableAxisMetadata();
            _ = RefreshVariableAxisMetadata(false);
            InvalidateRuntime();
            PublishStateChange(Members.Source);
        }

        private static bool InvalidatesRawSource(in StateChange change)
            => change.Kind == StateChangeKind.Reset ||
               change.Member == UniTextFont.Members.FontDataHash ||
               change.Member == Members.Source ||
               change.Member == UniTextSystemFont.Members.Common ||
               change.Member == UniTextSystemFont.Members.Windows ||
               change.Member == UniTextSystemFont.Members.Macos ||
               change.Member == UniTextSystemFont.Members.Linux ||
               change.Member == UniTextSystemFont.Members.Ios ||
               change.Member == UniTextSystemFont.Members.Android;
    }
}
