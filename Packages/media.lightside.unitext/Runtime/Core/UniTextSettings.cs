using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LightSide
{
    /// <summary>Receives one exact project-settings transition.</summary>
    public delegate void UniTextSettingsChangedHandler(in StateChange change);

    /// <summary>
    /// Global settings ScriptableObject for UniText configuration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Access via Edit → Project Settings → UniText.
    /// Contains editor-only default configurations for new UniText components.
    /// </para>
    /// </remarks>
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public sealed partial class UniTextSettings : ScriptableObject
    {
        private const string ResourcePath = "UniTextSettings";
        private const string UnicodeDataPath = "UnicodeData";

        private static TextAsset cachedUnicodeData;
        
        [SerializeField, StateField(nameof(ApplyPaintsChange))]
        [Tooltip("Named paint swatches (colour/gradient/texture) for paint parameters.")]
        private UniTextPaints paints;

        /// <summary>Gets or sets the project-wide named paint swatch catalog.</summary>
        public static UniTextPaints Paints
        {
            get => Instance.paints;
            set => Instance.SetPaintsState(value);
        }

        [SerializeField, StateField(nameof(ApplyGlobalStylePresetChange))]
        [Tooltip("Project-wide StylePreset applied to every UniText component. " +
                 "Behaves identically to a per-component preset added last: local Styles " +
                 "and per-component StylePresets register first and override the global one " +
                 "when parse rules collide.")]
        private StylePreset globalStylePreset;

        /// <summary>
        /// Project-wide <see cref="StylePreset"/> applied to every <see cref="UniTextBase"/>
        /// component without per-component opt-in. Behaves as one extra entry appended after
        /// each component's local <c>StylePresets</c> list, so local <c>Styles</c> and
        /// per-component <c>StylePresets</c> register first and win parse-rule conflicts.
        /// </summary>
        /// <remarks>
        /// Assigning fires <see cref="Changed"/> with <see cref="Members.GlobalStylePreset"/>;
        /// subscribed components reconcile their runtime style bindings through that same state
        /// transition. Editing the preset's styles list at runtime
        /// (via the preset's own <see cref="StylePreset.Styles"/>) is
        /// delivered through the asset's own <see cref="StylePreset.Changed"/> event, picked up
        /// by every component that has it subscribed (local <c>StylePresets</c> entry or this
        /// global slot).
        /// </remarks>
        public static StylePreset GlobalStylePreset
        {
            get => Instance != null ? Instance.globalStylePreset : null;
            set { if (Instance != null) Instance.SetGlobalStylePresetState(value); }
        }

        [SerializeField, StateField(nameof(ApplySystemFontChange))]
        [Tooltip("Optional project-wide default system font. Used as the primary for components with " +
                 "no Font and no Font Stack, and resolves real Bold/Italic from OS style cuts. When unset, " +
                 "UniText falls back to the OS default sans-serif. The always-on OS fallback that fills " +
                 "coverage gaps for individual glyphs is unaffected either way.")]
        private UniTextSystemFont systemFont;

        /// <summary>
        /// Project-wide system font. Serves two roles, both via <see cref="LightSide.SystemFont.Default"/>:
        /// the always-on final fallback that fills coverage gaps after the assigned fonts, and the primary
        /// for components with no Font and no Font Stack. When null, the OS default sans-serif fills both.
        /// </summary>
        public static UniTextSystemFont SystemFont
        {
            get => Instance != null ? Instance.systemFont : null;
            set { if (Instance != null) Instance.SetSystemFontState(value); }
        }

        [NonSerialized] private UniTextSystemFont subscribedSystemFont;
        [NonSerialized] private ReferenceBinding<WordSegmentationDictionary> boundDictionaries;

        private void OnEnable()
        {
            if (language == null) SetLanguageState(string.Empty);
            if (this != instance) return;
            BindSystemFontSubscription();
            BindDictionaries();
            ApplyRuntimeMirrors();
        }

        private void OnDisable()
        {
            if (subscribedSystemFont != null) subscribedSystemFont.Changed -= OnSystemFontInternalChanged;
            subscribedSystemFont = null;
            boundDictionaries?.Clear();
        } 
        
        private void BindSystemFontSubscription()
        {
            if (subscribedSystemFont == systemFont) return;
            if (subscribedSystemFont != null) subscribedSystemFont.Changed -= OnSystemFontInternalChanged;
            subscribedSystemFont = systemFont;
            if (subscribedSystemFont != null) subscribedSystemFont.Changed += OnSystemFontInternalChanged;
        }

        private void OnSystemFontInternalChanged(IStateChangeSource source,
            in StateChange change)
        {
            if (this == instance) Publish(Members.SystemFont);
        }

        [SerializeField, StateField(nameof(ApplySystemFontDisabledChange))]
        [Tooltip("Disable the automatic OS font fallback for codepoints assigned fonts don't cover. Explicitly assigned UniTextSystemFonts keep working.")]
        private bool systemFontDisabled;

        [SerializeField, StateField(nameof(ApplyEmojiDisabledChange))]
        [Tooltip("Disable emoji rendering globally.")]
        private bool emojiDisabled;

        /// <summary>Persisted mirror of <see cref="LightSide.SystemFont.Disabled"/>. The setter pushes the value to the runtime flag; the saved value is pushed back on editor load (static constructor).</summary>
        public static bool SystemFontDisabled
        {
            get => Instance != null && Instance.systemFontDisabled;
            set { if (Instance != null) Instance.SetSystemFontDisabledState(value); }
        }

        /// <summary>Persisted mirror of <see cref="EmojiFont.Disabled"/>. The setter pushes the value to the runtime flag; the saved value is pushed back on editor load (static constructor).</summary>
        public static bool EmojiDisabled
        {
            get => Instance != null && Instance.emojiDisabled;
            set { if (Instance != null) Instance.SetEmojiDisabledState(value); }
        }

#if UNITY_EDITOR
        static UniTextSettings()
        {
            EditorApplication.delayCall += PushSettingsToRuntime;
        }
#endif
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void PushSettingsToRuntime()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
#endif
            if (Instance == null) return;
            Instance.ApplyRuntimeMirrors();
        }

        private void ApplyPaintsChange(StateMember member)
        {
            if (this == instance) Publish(member);
        }

        private void ApplyGlobalStylePresetChange(StateMember member)
        {
            if (this == instance) Publish(member);
        }

        private void ApplySystemFontChange(StateMember member)
        {
            if (this != instance) return;
            BindSystemFontSubscription();
            Publish(member);
        }

        private void ApplySystemFontDisabledChange()
        {
            if (this == instance) LightSide.SystemFont.Disabled = systemFontDisabled;
        }

        private void ApplyEmojiDisabledChange()
        {
            if (this == instance) EmojiFont.Disabled = emojiDisabled;
        }

        private void ApplyRuntimeMirrors()
        {
            LightSide.SystemFont.Disabled = systemFontDisabled;
            EmojiFont.Disabled = emojiDisabled;
            LayoutSettings.DefaultLineHeightMode = lineHeightMode;
            LayoutSettings.DefaultLineHeightScale = lineHeightScale;
        }

        /// <summary>
        /// Occurs when a settings member has been mutated. The payload carries its generated
        /// <see cref="StateMember"/>, allowing subscribers to select their own exact reaction.
        /// <see cref="StateChangeKind.Reset"/> means the settings instance itself was replaced.
        /// Serialized editor changes are reconciled through the same generated transitions as
        /// runtime properties.
        /// </summary>
        /// <remarks>
        /// Subscribers typically use <see cref="Affects"/> to test an exact member while retaining
        /// reset handling in one place.
        /// </remarks>
        public static event UniTextSettingsChangedHandler Changed;

        /// <summary>
        /// Returns <see langword="true"/> when a <see cref="Changed"/> payload identifies
        /// <paramref name="interestedMember"/> or represents complete instance replacement.
        /// </summary>
        public static bool Affects(in StateChange change, StateMember interestedMember)
            => change.Kind == StateChangeKind.Reset || change.Member == interestedMember;

        private static void Publish(StateMember member)
        {
            var change = new StateChange(member);
            Changed?.Invoke(in change);
        }


    #if UNITY_EDITOR
        [SerializeField, StatePassive]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Text. Falls back to code creation if null.")]
        private GameObject textPrefab;

        [SerializeField, StatePassive]
        [Tooltip("Prefab instantiated by GameObject > UI (World) > UniText - World Text. Falls back to code creation if null.")]
        private GameObject worldTextPrefab;

        [SerializeField, StatePassive]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Button. Falls back to code creation if null.")]
        private GameObject buttonPrefab;

        [SerializeField, StatePassive]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Selectable Text. Falls back to code creation if null.")]
        private GameObject selectableTextPrefab;

        [SerializeField, StatePassive]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Editable Text. Falls back to code creation if null.")]
        private GameObject editableTextPrefab;

        [SerializeField, StatePassive]
        [Tooltip("Prefab instantiated by GameObject > UI > UniText - Input Field. Falls back to code creation if null.")]
        private GameObject inputFieldPrefab;

        /// <summary>Gets the prefab for creating Text UI objects (Editor only).</summary>
        public static GameObject TextPrefab => Instance?.textPrefab;

        /// <summary>Gets the prefab for creating world-space text objects (Editor only).</summary>
        public static GameObject WorldTextPrefab => Instance?.worldTextPrefab;

        /// <summary>Gets the prefab for creating Button UI objects (Editor only).</summary>
        public static GameObject ButtonPrefab => Instance?.buttonPrefab;

        /// <summary>Gets the prefab for creating selectable text objects (Editor only).</summary>
        public static GameObject SelectableTextPrefab => Instance?.selectableTextPrefab;

        /// <summary>Gets the prefab for creating editable text objects (Editor only).</summary>
        public static GameObject EditableTextPrefab => Instance?.editableTextPrefab;

        /// <summary>Gets the prefab for creating input field objects (Editor only).</summary>
        public static GameObject InputFieldPrefab => Instance?.inputFieldPrefab;

#endif

        /// <summary>Gets the compiled Unicode data asset, loaded from Resources.</summary>
        internal static TextAsset UnicodeDataAsset
        {
            get
            {
                if (cachedUnicodeData == null)
                {
                    cachedUnicodeData = Resources.Load<TextAsset>(UnicodeDataPath);
                    if (cachedUnicodeData == null)
                        Debug.LogError($"UnicodeData not found at Resources/{UnicodeDataPath}.bytes");
                }
                return cachedUnicodeData;
            }
        }

        private static UniTextSettings instance;

        /// <summary>Returns true if the instance is already loaded (without triggering load).</summary>
        internal static bool IsNull => instance == null;

        /// <summary>
        /// Loads the singleton without logging when it is absent. Editor-init cascades and
        /// domain reload can hit this before the asset is loadable; the transient
        /// null is expected there, and every caller already degrades to defaults.
        /// </summary>
        internal static UniTextSettings InstanceSilent
        {
            get
            {
                if (instance != null) return instance;
                instance = Resources.Load<UniTextSettings>(ResourcePath);
                if (instance == null) return null;
                instance.BindSystemFontSubscription();
                instance.ApplyRuntimeMirrors();
                return instance;
            }
        }

        /// <summary>Gets the singleton settings instance, loading from Resources if needed.</summary>
        public static UniTextSettings Instance
        {
            get
            {
                if (InstanceSilent == null)
                    Debug.LogError(
                        $"UniTextSettings not found at Resources/{ResourcePath}.asset. " +
                        "Create it via Assets > Create > UniText > Settings and place in Resources folder.");

                return instance;
            }
        }

        /// <summary>
        /// Manually replaces the singleton settings asset (used by tests and custom
        /// initialization paths). Raises <see cref="Changed"/> with
        /// <see cref="StateChangeKind.Reset"/> because every reactive member may differ.
        /// </summary>
        /// <param name="settings">The settings instance to use.</param>
        public static void SetInstance(UniTextSettings settings)
        {
            if (instance != null && instance.subscribedSystemFont != null)
            {
                instance.subscribedSystemFont.Changed -= instance.OnSystemFontInternalChanged;
                instance.subscribedSystemFont = null;
            }
            instance?.boundDictionaries?.Clear();
            instance = settings;
            if (settings != null)
            {
                settings.BindSystemFontSubscription();
                settings.BindDictionaries();
                settings.ApplyRuntimeMirrors();
            }
            var change = new StateChange(default, StateChangeKind.Reset);
            Changed?.Invoke(in change);
        }

        [SerializeField, StateList(nameof(ApplyDictionariesChange), Name = "DictionaryState",
            Validator = nameof(ValidateDictionariesMutation), IsPublic = false)]
        [Tooltip("Dictionary assets for contextual word segmentation (for example Thai, Lao, Khmer, Myanmar, Chinese, and Japanese).")]
        private StyledList<WordSegmentationDictionary> dictionaries = new();

        /// <summary>Gets the configured word segmentation dictionaries.</summary>
        public static StateList<WordSegmentationDictionary> Dictionaries
            => Instance != null ? Instance.DictionaryState : default;

        private void ApplyDictionariesChange(in StateListMutation<WordSegmentationDictionary> mutation)
        {
            BindDictionaries();
            if (this == instance) Publish(Members.Dictionaries);
        }

        private void BindDictionaries()
        {
            boundDictionaries ??= new ReferenceBinding<WordSegmentationDictionary>(
                ConnectDictionary, DisconnectDictionary);
            boundDictionaries.Reconcile(dictionaries);
        }

        private void ConnectDictionary(WordSegmentationDictionary dictionary)
            => dictionary.Changed += OnDictionaryChanged;

        private void DisconnectDictionary(WordSegmentationDictionary dictionary)
            => dictionary.Changed -= OnDictionaryChanged;

        private void OnDictionaryChanged(IStateChangeSource source, in StateChange change)
        {
            if (this == instance) Publish(Members.Dictionaries);
        }

        private void ValidateDictionariesMutation(in StateListMutation<WordSegmentationDictionary> mutation)
        {
            for (var i = 0; i < mutation.Count; i++)
                if (mutation[i] == null)
                    throw new InvalidOperationException($"Word segmentation dictionary slot {i} is empty.");
        }

        [SerializeField, StatePassive]
        [Tooltip("Seconds of inactivity before the caret stops blinking and stays visible. 0 = blink forever (default).")]
        private float caretBlinkTimeout;

        [SerializeField, StatePassive]
        [Tooltip("Caret blink half-period in seconds (on→off or off→on).")]
        private float caretBlinkInterval = 0.5f;

        internal const float DefaultUndoCoalesceTimeout = 0.5f;
        internal const int DefaultUndoMemoryLimitBytes = 1024 * 1024;

        [SerializeField, StatePassive]
        [Tooltip("Seconds within which consecutive same-type edits merge into a single undo entry. " +
                 "Default 0.5 matches ProseMirror and CodeMirror 6 (newGroupDelay). Lower values " +
                 "(~0.05–0.1) approximate per-keystroke undo for editors that need finer-grained " +
                 "history; higher values (~1–2 s) coalesce more aggressively at the cost of losing " +
                 "intra-word edit boundaries.")]
        internal float undoCoalesceTimeout = DefaultUndoCoalesceTimeout;

        [SerializeField, StatePassive]
        [Tooltip("Memory ceiling for one field's undo history, in bytes of stored text. Oldest " +
                 "entries are dropped past the limit; the newest entry always survives. 0 = unlimited.")]
        internal int undoMemoryLimit = DefaultUndoMemoryLimitBytes;

        /// <summary>
        /// Coalesce window for undo grouping, in seconds. Degrades to the default when the
        /// settings asset is absent — safe on per-keystroke paths (no error log).
        /// </summary>
        internal static float UndoCoalesceTimeout
        {
            get
            {
                var inst = InstanceSilent;
                return inst != null ? inst.undoCoalesceTimeout : DefaultUndoCoalesceTimeout;
            }
        }

        /// <summary>
        /// Memory ceiling for one field's undo history, in bytes of stored text; 0 = unlimited.
        /// Degrades to the default when the settings asset is absent.
        /// </summary>
        internal static int UndoMemoryLimitBytes
        {
            get
            {
                var inst = InstanceSilent;
                return inst != null ? inst.undoMemoryLimit : DefaultUndoMemoryLimitBytes;
            }
        }

        /// <summary>
        /// Target vertex capacity per shard mesh in the world-space batcher. A component's segments always
        /// share one shard, so this bounds how much unrelated text one structural rebuild re-bakes; a single
        /// component larger than the budget inflates its own shard instead of splitting.
        /// </summary>
        public static int WorldBatcherShardTargetVertexCount
        {
            get => UniTextWorldBatcher.shardTargetVertexCount;
            set => UniTextWorldBatcher.shardTargetVertexCount = Mathf.Max(64, value);
        }

        /// <summary>Seconds of inactivity before the caret stops blinking. Reads <see cref="InstanceSilent"/> — polled from the caret update loop, where a missing settings asset must degrade without per-tick error logs.</summary>
        public static float CaretBlinkTimeout
        {
            get
            {
                var inst = InstanceSilent;
                return inst != null ? inst.caretBlinkTimeout : 0f;
            }
        }

        /// <summary>Caret blink half-period in seconds. Reads <see cref="InstanceSilent"/> (see <see cref="CaretBlinkTimeout"/>).</summary>
        public static float CaretBlinkInterval
        {
            get
            {
                var inst = InstanceSilent;
                return inst != null ? inst.caretBlinkInterval : 0.5f;
            }
        }

        private const float DefaultLongPressDuration = 0.5f;
        private const float DefaultDragSlop = 10f;
        private const float DefaultMultiTapWindow = 0.3f;
        private const float DefaultMultiTapSlop = 100f;
        private const float DefaultMultiClickInterval = 0.5f;
        private const float DefaultMultiClickSlop = 8f;

        [SerializeField, StatePassive]
        [Tooltip("Hold duration in seconds that promotes a press into a long-press gesture (iOS / Android default: 0.5).")]
        private float longPressDuration = DefaultLongPressDuration;

        [SerializeField, StatePassive]
        [Tooltip("Pointer travel in dp beyond which a press becomes a drag instead of a tap / hold (Android touch-slop scale).")]
        private float dragSlop = DefaultDragSlop;

        [SerializeField, StatePassive]
        [Tooltip("Maximum interval between chained touch taps, seconds (Android DOUBLE_TAP_TIMEOUT / Flutter kDoubleTapTimeout: 0.3).")]
        private float multiTapWindow = DefaultMultiTapWindow;

        [SerializeField, StatePassive]
        [Tooltip("Maximum distance between chained touch taps, in dp (Android DOUBLE_TAP_SLOP / Flutter kDoubleTapSlop: 100).")]
        private float multiTapSlop = DefaultMultiTapSlop;

        [SerializeField, StatePassive]
        [Tooltip("Maximum interval between chained desktop clicks, seconds (Windows GetDoubleClickTime / macOS default: 0.5).")]
        private float multiClickInterval = DefaultMultiClickInterval;

        [SerializeField, StatePassive]
        [Tooltip("Maximum distance between chained desktop clicks, in dp.")]
        private float multiClickSlop = DefaultMultiClickSlop;

        internal static float LongPressDuration
        {
            get { var inst = InstanceSilent; return inst != null ? inst.longPressDuration : DefaultLongPressDuration; }
        }

        internal static float DragSlopDp
        {
            get { var inst = InstanceSilent; return inst != null ? inst.dragSlop : DefaultDragSlop; }
        }

        internal static float MultiTapWindow
        {
            get { var inst = InstanceSilent; return inst != null ? inst.multiTapWindow : DefaultMultiTapWindow; }
        }

        internal static float MultiTapSlopDp
        {
            get { var inst = InstanceSilent; return inst != null ? inst.multiTapSlop : DefaultMultiTapSlop; }
        }

        /// <summary>Setters write the live settings instance (project-wide configuration; e.g. matching a
        /// user-configured OS double-click time at startup) and no-op without a settings asset.</summary>
        internal static float MultiClickInterval
        {
            get { var inst = InstanceSilent; return inst != null ? inst.multiClickInterval : DefaultMultiClickInterval; }
            set { var inst = InstanceSilent; if (inst != null) inst.multiClickInterval = value; }
        }

        internal static float MultiClickSlopDp
        {
            get { var inst = InstanceSilent; return inst != null ? inst.multiClickSlop : DefaultMultiClickSlop; }
            set { var inst = InstanceSilent; if (inst != null) inst.multiClickSlop = value; }
        }

        [SerializeField, StateField(nameof(ApplyLanguageChange))]
        [Tooltip("Project-wide BCP 47 language tag (e.g. zh-Hans, zh-Hant, ja, ko, en-US). " +
                 "Applied to any codepoint that has no component-level UniText.Language and no " +
                 "per-range <lang=...> override. Drives the OpenType 'locl' feature and " +
                 "FontFamily.preferredLanguage selection. Leave empty to disable.")]
        private string language = "";

        /// <summary>
        /// Gets or sets the project-wide BCP 47 language tag. Applied to any codepoint that has no
        /// component-level <c>UniText.Language</c> and no per-range <c>&lt;lang&gt;</c> override.
        /// </summary>
        public static string Language
        {
            get => Instance != null ? Instance.language : null;
            set { if (Instance != null) Instance.SetLanguageState(value); }
        }

        [SerializeField, StateField(nameof(ApplyLineHeightModeChange))]
        [Tooltip("Default line-height mode for every UniText component without a per-range line-height override.\n" +
                 "Scaled — fixed Scale x fontSize: uniform rows, matches web/app UIs (recommended for UI).\n" +
                 "Content — grow to the tallest font on each line: never clips fallback glyphs, matches CSS line-height:normal.\n" +
                 "Primary — primary font metrics only.")]
        private LineHeightMode lineHeightMode = LineHeightMode.Scaled;

        [SerializeField, StateField(nameof(ApplyLineHeightScaleChange))]
        [Tooltip("Line height as a multiple of font size when the default mode is Scaled. 1.4 matches typical web/app UIs (1.375–1.5).")]
        private float lineHeightScale = 1.4f;

        [SerializeField, StateField(nameof(ApplyFontNormalizeMetricChange))]
        [Tooltip("Default font-size normalization (CSS font-size-adjust): scales fallback fonts to a shared x-height / cap-height. None matches every major platform default.")]
        private FontNormalizeMetric fontNormalizeMetric = FontNormalizeMetric.None;

        [SerializeField, StateField(nameof(ApplyFontNormalizeTargetChange))]
        [Tooltip("Normalization target aspect (metric ÷ font size). 0 = match the primary font. Ignored when the metric is None.")]
        private float fontNormalizeTarget;

        /// <summary>Project-wide default line-height mode applied to every component without a per-range override. Mirrored to <see cref="LayoutSettings.DefaultLineHeightMode"/>.</summary>
        public static LineHeightMode DefaultLineHeightMode
        {
            get => Instance != null ? Instance.lineHeightMode : LineHeightMode.Scaled;
            set { if (Instance != null) Instance.SetLineHeightModeState(value); }
        }

        /// <summary>Line height as a multiple of font size when <see cref="DefaultLineHeightMode"/> is <see cref="LineHeightMode.Scaled"/>. Mirrored to <see cref="LayoutSettings.DefaultLineHeightScale"/>.</summary>
        public static float LineHeightScale
        {
            get => Instance != null ? Instance.lineHeightScale : 1.4f;
            set { if (Instance != null) Instance.SetLineHeightScaleState(value); }
        }

        /// <summary>Project-wide default font normalization metric (CSS <c>font-size-adjust</c>), applied to each component's font provider on init. <see cref="FontNormalizeMetric.None"/> matches platform defaults.</summary>
        public static FontNormalizeMetric FontNormalizeMetric
        {
            get => Instance != null ? Instance.fontNormalizeMetric : FontNormalizeMetric.None;
            set { if (Instance != null) Instance.SetFontNormalizeMetricState(value); }
        }

        /// <summary>Target aspect for <see cref="FontNormalizeMetric"/> (metric ÷ font size); 0 matches the primary font.</summary>
        public static float FontNormalizeTarget
        {
            get => Instance != null ? Instance.fontNormalizeTarget : 0f;
            set { if (Instance != null) Instance.SetFontNormalizeTargetState(value); }
        }

        private void ApplyLanguageChange(StateMember member, string previous, ref string current)
        {
            current ??= string.Empty;
            if (string.Equals(previous, current, StringComparison.Ordinal)) return;
            if (this == instance) Publish(member);
        }

        private void ApplyLineHeightModeChange(StateMember member)
        {
            if (this != instance) return;
            LayoutSettings.DefaultLineHeightMode = lineHeightMode;
            Publish(member);
        }

        private void ApplyLineHeightScaleChange(StateMember member, float previous, ref float current)
        {
            if (Mathf.Approximately(previous, current))
            {
                current = previous;
                return;
            }
            if (this != instance) return;
            LayoutSettings.DefaultLineHeightScale = current;
            Publish(member);
        }

        private void ApplyFontNormalizeMetricChange(StateMember member)
        {
            if (this == instance) Publish(member);
        }

        private void ApplyFontNormalizeTargetChange(StateMember member, float previous, ref float current)
        {
            if (Mathf.Approximately(previous, current))
            {
                current = previous;
                return;
            }
            if (this == instance) Publish(member);
        }
    }
}
