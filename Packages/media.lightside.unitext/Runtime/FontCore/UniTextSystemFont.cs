using System;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// UniTextFont that resolves its font bytes from the host OS at runtime
    /// from a per-platform pick (Common / Windows / macOS / Linux / iOS / Android). WebGL is unsupported.
    /// </summary>
    /// <remarks>
    /// A regular font: it participates only when added to a font stack. Automatic gap-filling for
    /// codepoints no font covers is a separate, always-on mechanism — <see cref="SystemFont"/> —
    /// independent of this asset.
    /// FaceInfo, SDF detail multiplier and tile-size offset can be overridden per platform.
    /// Each field defaults to an "unset" sentinel; unset fields use values extracted from
    /// the loaded font. Glyph overrides are not supported — they reference indices specific
    /// to a particular font file, which a system font does not pin.
    /// </remarks>
    public partial class UniTextSystemFont : UniTextFont
    {
        protected internal override bool UsesEmbeddedSource => false;

        public const int UnsetInt = int.MinValue;
        public const float UnsetFloat = float.NaN;

        public enum FontPlatform
        {
            Common,
            Windows,
            MacOS,
            Linux,
            iOS,
            Android,
        }

        /// <summary>Optional FaceInfo override. Every field defaults to the unset sentinel.</summary>
        [Serializable]
        public struct FaceInfoOverride
        {
            public string familyName;
            public string styleName;
            /// <summary>Tri-state italic: -1 unset, 0 false, 1 true.</summary>
            public int italicTriState;
            public int weightClass;
            public int unitsPerEm;
            public int lineHeight;
            public int ascentLine;
            public int capLine;
            public int meanLine;
            public int descentLine;
            public int superscriptOffset;
            public int superscriptSize;
            public int subscriptOffset;
            public int subscriptSize;
            public int underlineOffset;
            public int underlineThickness;
            public int strikethroughOffset;
            public int strikethroughThickness;
            public int tabWidth;

            public static FaceInfoOverride Unset => new()
            {
                familyName = null,
                styleName = null,
                italicTriState = -1,
                weightClass = UnsetInt,
                unitsPerEm = UnsetInt,
                lineHeight = UnsetInt,
                ascentLine = UnsetInt,
                capLine = UnsetInt,
                meanLine = UnsetInt,
                descentLine = UnsetInt,
                superscriptOffset = UnsetInt,
                superscriptSize = UnsetInt,
                subscriptOffset = UnsetInt,
                subscriptSize = UnsetInt,
                underlineOffset = UnsetInt,
                underlineThickness = UnsetInt,
                strikethroughOffset = UnsetInt,
                strikethroughThickness = UnsetInt,
                tabWidth = UnsetInt,
            };
        }

        [Serializable]
        public struct PlatformConfig
        {
            [Tooltip("Font name from the platform catalog. Empty = inherit from the Common tab.")]
            public string fontName;

            public FaceInfoOverride faceInfo;

            [Tooltip("SDF tile detail multiplier override. NaN = inherit.")]
            public float sdfDetailMultiplier;

            [Tooltip("Tile size offset override. int.MinValue = inherit.")]
            public int tileSizeOffsetOverride;

            public static PlatformConfig Unset => new()
            {
                fontName = "",
                faceInfo = FaceInfoOverride.Unset,
                sdfDetailMultiplier = UnsetFloat,
                tileSizeOffsetOverride = UnsetInt,
            };
        }

        [SerializeField, StateField(nameof(ApplyPlatformConfigurationChange))]
        private PlatformConfig common = PlatformConfig.Unset;

        [SerializeField, StateField(nameof(ApplyPlatformConfigurationChange))]
        private PlatformConfig windows = PlatformConfig.Unset;

        [SerializeField, StateField(nameof(ApplyPlatformConfigurationChange))]
        private PlatformConfig macos = PlatformConfig.Unset;

        [SerializeField, StateField(nameof(ApplyPlatformConfigurationChange))]
        private PlatformConfig linux = PlatformConfig.Unset;

        [SerializeField, StateField(nameof(ApplyPlatformConfigurationChange))]
        private PlatformConfig ios = PlatformConfig.Unset;

        [SerializeField, StateField(nameof(ApplyPlatformConfigurationChange))]
        private PlatformConfig android = PlatformConfig.Unset;

        [NonSerialized] private FontSource runtimeFontSource;
        [NonSerialized] private int runtimeFontDataHash;
        [NonSerialized] private AxisDefault[] runtimeAxes;
        [NonSerialized] private IGlyphOutlineSource runtimeOutlineSource;
        [NonSerialized] private float resolvedSdfDetailMultiplier = 1f;
        [NonSerialized] private int resolvedTileSizeOffset;
        [NonSerialized] private bool resolved;
        [NonSerialized] private bool resolveFailed;
        [NonSerialized] private string resolvedPath;
        [NonSerialized] private string resolvedFontName;
        [NonSerialized] private FontPlatform resolvedPlatform;

        /// <summary>Path of the resolved font file, or the CoreText source key on iOS. Null if unresolved.</summary>
        public string ResolvedPath => resolvedPath;
        /// <summary>Catalog display name actually used at runtime. Null if unresolved.</summary>
        public string ResolvedFontName => resolvedFontName;
        /// <summary>Platform tab whose configuration drove the resolution.</summary>
        public FontPlatform ResolvedPlatform => resolvedPlatform;
        /// <summary>True if resolution ran without producing a usable font source.</summary>
        public bool ResolveFailed => resolveFailed;

        /// <summary>Returns the configuration for one platform tab.</summary>
        public PlatformConfig GetConfig(FontPlatform p)
        {
            switch (p)
            {
                case FontPlatform.Windows: return windows;
                case FontPlatform.MacOS: return macos;
                case FontPlatform.Linux: return linux;
                case FontPlatform.iOS: return ios;
                case FontPlatform.Android: return android;
                default: return common;
            }
        }

        /// <summary>Replaces one platform tab and invalidates the resolved operating-system font.</summary>
        public void SetConfig(FontPlatform platform, PlatformConfig value)
        {
            switch (platform)
            {
                case FontPlatform.Windows: SetWindowsState(value); break;
                case FontPlatform.MacOS: SetMacosState(value); break;
                case FontPlatform.Linux: SetLinuxState(value); break;
                case FontPlatform.iOS: SetIosState(value); break;
                case FontPlatform.Android: SetAndroidState(value); break;
                default: SetCommonState(value); break;
            }
        }

#if UNITY_EDITOR
        internal override int RawFontDataSize
        {
            get
            {
                EnsureResolved();
                return runtimeFontSource?.Length ?? 0;
            }
        }

        protected override void EnsureFaceInfoFromFont()
        {
            if (runtimeFontSource == null) return;
            if (!FT.IsInitialized) FT.Initialize();
            using var face = FreeTypeFace.TryCreate(runtimeFontSource, 0);
            if (face == null) return;

            var fresh = Core.BuildFullFaceInfo(face.Pointer);
            var nextFaceInfo = ApplyFaceInfoOverrides(fresh);
            SetResolvedMetadata(nextFaceInfo, nextFaceInfo.unitsPerEm > 0 ? nextFaceInfo.unitsPerEm : 1000);
            UnityEditor.EditorUtility.SetDirty(this);

        }
#endif

        protected override Core CreateRuntime()
        {
            EnsureResolved();
            if (runtimeFontSource == null || runtimeFontSource.Length == 0) return null;
            return new Core(
                runtimeFontSource, faceInfo, unitsPerEm, FontScale, resolvedSdfDetailMultiplier,
                resolvedTileSizeOffset, ItalicStyle, SpacingOffset, FakeBoldWeight, null, name,
                runtimeAxes, SpaceAdvance, runtimeOutlineSource)
            { isSystemFont = true };
        }

        /// <summary>Gets a managed snapshot of the resolved operating-system font data.</summary>
        public override byte[] FontData
            => CopyFontData();

        /// <summary>Creates a managed snapshot of the resolved operating-system font data.</summary>
        public override byte[] CopyFontData()
        {
            EnsureResolved();
            return runtimeFontSource?.CopyBytes();
        }

        public override bool HasFontData
        {
            get
            {
                EnsureResolved();
                return runtimeFontSource is { Length: > 0 };
            }
        }

        protected internal override int RawFontDataHash
        {
            get
            {
                EnsureResolved();
                return runtimeFontDataHash;
            }
        }

        /// <summary>Forces re-resolution on the next access. Call after changing config at runtime.</summary>
        public void Invalidate()
        {
            InvalidateResolution();
            NotifyConfigurationChanged();
        }

        private void InvalidateResolution()
        {
            ReleaseResolvedSource();
            InvalidateRuntime();
        }

        private void ReleaseResolvedSource()
        {
            resolved = false;
            resolveFailed = false;
            runtimeFontSource = null;
            runtimeFontDataHash = 0;
            runtimeAxes = null;
            runtimeOutlineSource?.Dispose();
            runtimeOutlineSource = null;
            resolvedPath = null;
            resolvedFontName = null;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ReleaseResolvedSource();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            ReleaseResolvedSource();
        }

        private void ApplyPlatformConfigurationChange(StateMember member)
        {
            InvalidateResolution();
            PublishStateChange(member);
        }

        private void EnsureResolved()
        {
            if (resolved) return;
            resolved = true;
            resolveFailed = true;

#if UNITY_WEBGL && !UNITY_EDITOR
            CatZones.systemFont.MeowWarn("[UniTextSystemFont] WebGL has no OS font access — assign a regular UniTextFont for WebGL builds.");
            return;
#else
            var platform = ResolutionPlatform();
            resolvedPlatform = platform;

            if (!TryAcquireSource(platform, out var source, out var axes, out var descriptor,
                    out var fontName))
                return;

            runtimeFontSource = source.fontSource;
            runtimeFontDataHash = runtimeFontSource.ComputeLegacyHash();
            runtimeAxes = axes;
            runtimeOutlineSource = source.glyphOutlineSource;
            resolvedPath = descriptor;
            resolvedFontName = fontName;

            var resolvedFaceInfo = ApplyFaceInfoOverrides(source.faceInfo);
            var nextUnitsPerEm = resolvedFaceInfo.unitsPerEm > 0
                ? resolvedFaceInfo.unitsPerEm
                : SystemFontFaces.GetUpem(source.fontSource,
                    Math.Max(0, source.faceInfo.faceIndex));
            SetResolvedMetadata(resolvedFaceInfo, nextUnitsPerEm > 0 ? nextUnitsPerEm : unitsPerEm);

            ApplyRenderingOverrides();
            InvalidateRuntime();
            resolveFailed = false;
            CatZones.systemFont.Meow(
                $"[UniTextSystemFont] {name}: resolved '{fontName}' on {platform} ({source.fontSource.Length:N0} bytes) from {descriptor}");
#endif
        }

        private FontPlatform ResolutionPlatform() => SystemFontCatalog.CurrentPlatform();

        private bool TryAcquireSource(FontPlatform platform, out SystemFontMaterializedSource source,
            out AxisDefault[] axes, out string descriptor, out string fontName)
        {
            source = default;
            axes = null;
            descriptor = null;

            var entries = SystemFontCatalog.EntriesFor(platform);
            var primary = GetConfig(platform).fontName;
            var commonName = common.fontName;

            if (!string.IsNullOrEmpty(primary)
                && TryAcquireEntry(SystemFontCatalog.FindByName(entries, primary),
                    ref source, ref axes, ref descriptor))
            {
                fontName = primary;
                return true;
            }

            if (!string.IsNullOrEmpty(commonName)
                && TryAcquireEntry(SystemFontCatalog.ResolveCommon(commonName, platform),
                    ref source, ref axes, ref descriptor))
            {
                fontName = commonName;
                return true;
            }

            var requested = !string.IsNullOrEmpty(primary) ? primary : commonName;
            for (var i = 0; i < entries.Length; i++)
                if (TryAcquireEntry(entries[i], ref source, ref axes, ref descriptor))
                {
                    fontName = entries[i].displayName;
                    if (!string.IsNullOrEmpty(requested))
                        CatZones.systemFont.MeowWarn(
                            $"[UniTextSystemFont] {name}: requested font '{requested}' not found on {platform}; falling back to {fontName}");
                    return true;
                }

            if (SystemFont.TryMaterializeFamily(null, out source, out axes, out descriptor))
            {
                fontName = source.faceInfo.familyName;
                CatZones.systemFont.MeowWarn(
                    $"[UniTextSystemFont] {name}: no catalog font found on {platform}; falling back to the platform default '{fontName}'");
                return true;
            }

            fontName = requested;
            CatZones.systemFont.MeowWarn(
                $"[UniTextSystemFont] {name}: no usable font found on {platform}. Text will not render.");
            return false;
        }

        private static bool TryAcquireEntry(SystemFontCatalog.Entry entry,
            ref SystemFontMaterializedSource source, ref AxisDefault[] axes, ref string descriptor)
        {
            if (!SystemFont.TryMaterializeFamily(SystemFontCatalog.FamilyOf(entry),
                    out source, out axes, out descriptor))
                return false;
            if (SystemFontCatalog.Satisfies(entry, source.faceInfo.familyName)) return true;

            source.glyphOutlineSource?.Dispose();
            source = default;
            axes = null;
            descriptor = null;
            return false;
        }

        private FaceInfo ApplyFaceInfoOverrides(FaceInfo source)
        {
            var fi = source;
            var ov = MergedFaceInfo();

            if (!string.IsNullOrEmpty(ov.familyName)) fi.familyName = ov.familyName;
            if (!string.IsNullOrEmpty(ov.styleName)) fi.styleName = ov.styleName;
            if (ov.italicTriState >= 0) fi.isItalic = ov.italicTriState == 1;

            ApplyIntOverride(ov.weightClass, ref fi.weightClass);
            ApplyIntOverride(ov.unitsPerEm, ref fi.unitsPerEm);
            ApplyIntOverride(ov.lineHeight, ref fi.lineHeight);
            ApplyIntOverride(ov.ascentLine, ref fi.ascentLine);
            ApplyIntOverride(ov.capLine, ref fi.capLine);
            ApplyIntOverride(ov.meanLine, ref fi.meanLine);
            ApplyIntOverride(ov.descentLine, ref fi.descentLine);
            ApplyIntOverride(ov.superscriptOffset, ref fi.superscriptOffset);
            ApplyIntOverride(ov.superscriptSize, ref fi.superscriptSize);
            ApplyIntOverride(ov.subscriptOffset, ref fi.subscriptOffset);
            ApplyIntOverride(ov.subscriptSize, ref fi.subscriptSize);
            ApplyIntOverride(ov.underlineOffset, ref fi.underlineOffset);
            ApplyIntOverride(ov.underlineThickness, ref fi.underlineThickness);
            ApplyIntOverride(ov.strikethroughOffset, ref fi.strikethroughOffset);
            ApplyIntOverride(ov.strikethroughThickness, ref fi.strikethroughThickness);
            ApplyIntOverride(ov.tabWidth, ref fi.tabWidth);
            return fi;
        }

        private static void ApplyIntOverride(int src, ref int dst)
        {
            if (src != UnsetInt) dst = src;
        }

        private FaceInfoOverride MergedFaceInfo()
        {
            var platformCfg = GetConfig(ResolutionPlatform());
            var merged = common.faceInfo;
            ref var pf = ref platformCfg.faceInfo;

            if (!string.IsNullOrEmpty(pf.familyName)) merged.familyName = pf.familyName;
            if (!string.IsNullOrEmpty(pf.styleName)) merged.styleName = pf.styleName;
            if (pf.italicTriState >= 0) merged.italicTriState = pf.italicTriState;

            ApplyIntOverride(pf.weightClass, ref merged.weightClass);
            ApplyIntOverride(pf.unitsPerEm, ref merged.unitsPerEm);
            ApplyIntOverride(pf.lineHeight, ref merged.lineHeight);
            ApplyIntOverride(pf.ascentLine, ref merged.ascentLine);
            ApplyIntOverride(pf.capLine, ref merged.capLine);
            ApplyIntOverride(pf.meanLine, ref merged.meanLine);
            ApplyIntOverride(pf.descentLine, ref merged.descentLine);
            ApplyIntOverride(pf.superscriptOffset, ref merged.superscriptOffset);
            ApplyIntOverride(pf.superscriptSize, ref merged.superscriptSize);
            ApplyIntOverride(pf.subscriptOffset, ref merged.subscriptOffset);
            ApplyIntOverride(pf.subscriptSize, ref merged.subscriptSize);
            ApplyIntOverride(pf.underlineOffset, ref merged.underlineOffset);
            ApplyIntOverride(pf.underlineThickness, ref merged.underlineThickness);
            ApplyIntOverride(pf.strikethroughOffset, ref merged.strikethroughOffset);
            ApplyIntOverride(pf.strikethroughThickness, ref merged.strikethroughThickness);
            ApplyIntOverride(pf.tabWidth, ref merged.tabWidth);
            return merged;
        }

        private void ApplyRenderingOverrides()
        {
            resolvedSdfDetailMultiplier = 1f;
            resolvedTileSizeOffset = 0;

            var platformCfg = GetConfig(ResolutionPlatform());

            float sdf = platformCfg.sdfDetailMultiplier;
            if (float.IsNaN(sdf) || sdf <= 0f) sdf = common.sdfDetailMultiplier;
            if (!float.IsNaN(sdf) && sdf > 0f) resolvedSdfDetailMultiplier = sdf;

            int tileOff = platformCfg.tileSizeOffsetOverride;
            if (tileOff == UnsetInt) tileOff = common.tileSizeOffsetOverride;
            if (tileOff != UnsetInt) resolvedTileSizeOffset = Mathf.Clamp(tileOff, -2, 2);
        }
    }
}
