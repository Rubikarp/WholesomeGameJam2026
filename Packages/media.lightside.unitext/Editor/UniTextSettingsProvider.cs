using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    internal class UniTextSettingsProvider : SettingsProvider
    {
        private const string DefaultProjectFolder = "Assets/UniText";
        private const string UrpPackageName = "com.unity.render-pipelines.universal";
        private const string WorldShaderName = LightSideShaderNames.WorldLit;
        private const string UniversalPipelineAssetType = "UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset";
        private const string UniversalPipelineTag = "UniversalPipeline";
        private static readonly ShaderTagId renderPipelineTag = new("RenderPipeline");

        internal static string AssetPath
        {
            get
            {
                var existing = FindSettingsInProject();
                return existing ?? DefaultProjectFolder + "/Resources/UniTextSettings.asset";
            }
        }

        private SerializedObject serializedSettings;

        private UniTextSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        #region Settings UI

        /// <inheritdoc/>
        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var settings = EnsureDefaults() ?? throw new InvalidOperationException(
                "UniText settings could not be loaded or created.");
            serializedSettings = new SerializedObject(settings);
            serializedSettings.Update();
            InspectorVisuals.ClearContent(rootElement);
            var scroll = InspectorVisuals.CreateScrollStack();
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.style.flexGrow = 1f;
            rootElement.Add(scroll);
            var content = UniTextInspectorTheme.CreateRoot();
            scroll.Add(content);
            BuildSettings(content, settings);
            content.TrackSerializedObjectValue(serializedSettings, _ =>
                LightSideSettingsGuard.Capture(settings, BackupName, GuardedFields));
        }

        private void BuildSettings(VisualElement root, UniTextSettings settings)
        {
            var styling = Section("Styling");
            styling.Add(Field("paints"));
            styling.Add(Field("globalStylePreset"));
            root.Add(styling);

            var textDefaults = Section("Text Defaults");
            var lhMode = InspectorHelpers.RequireProperty(serializedSettings, "lineHeightMode");
            textDefaults.Add(CreateEnumWithInlineValue(lhMode, "Line Height Mode",
                InspectorHelpers.RequireProperty(serializedSettings, "lineHeightScale"),
                value => value == (int)LineHeightMode.Scaled));
            var fsMatch = InspectorHelpers.RequireProperty(
                serializedSettings, "fontNormalizeMetric");
            textDefaults.Add(CreateEnumWithInlineValue(fsMatch, "Font Size Match",
                InspectorHelpers.RequireProperty(serializedSettings, "fontNormalizeTarget"),
                value => value != (int)FontNormalizeMetric.None));
            root.Add(textDefaults);

            var fonts = Section("Fonts");
            fonts.Add(Field("systemFont", "Default System Font"));
            fonts.Add(Field("systemFontDisabled", "Disable System Font Fallback"));
            fonts.Add(Field("emojiDisabled", "Disable Emoji"));
            root.Add(fonts);

            var editorOnly = Section("Editor Only");
            AddFields(editorOnly, "textPrefab", "worldTextPrefab", "buttonPrefab",
                "selectableTextPrefab", "editableTextPrefab", "inputFieldPrefab");
            root.Add(editorOnly);

            var localization = Section("Localization");
            localization.Add(Field("language"));
            root.Add(localization);

            var segmentation = Section("Word Segmentation");
            segmentation.Add(Field("dictionaries"));
            root.Add(segmentation);

            var editing = Section("Editing");
            AddFields(editing, "caretBlinkTimeout", "caretBlinkInterval",
                "undoCoalesceTimeout", "undoMemoryLimit");
            root.Add(editing);

            var gestures = Section("Gestures");
            AddFields(gestures, "longPressDuration", "dragSlop", "multiTapWindow",
                "multiTapSlop", "multiClickInterval", "multiClickSlop");
            root.Add(gestures);

            var debug = Section("Debug");
            var debugOn = ScriptingDefines.DebugDefinesOn;
            var debugToggle = new InspectorToggle("Enable Debug Logging") { value = debugOn };
            debugToggle.tooltip =
                "Compiles diagnostic logging in or out. Toggling triggers a recompile; filter areas in Tools → LightSide → Log Zones.";
            InspectorVisuals.MarkFieldAxis(debugToggle);
            debugToggle.RegisterValueChangedCallback(evt =>
                ScriptingDefines.SetDebugDefines(evt.newValue));
            debug.Add(debugToggle);
            if (ScriptingDefines.HasDebugMismatch)
                debug.Add(new HelpBox(
                    "Debug defines differ between packages or build target groups (state left by an older version). " +
                    "Toggle once to re-sync everything.",
                    HelpBoxMessageType.Warning));
            root.Add(debug);

            root.Add(new Button(() =>
            {
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }) { text = "Select Settings Asset" });
        }

        private static VisualElement Section(string title) =>
            InspectorVisuals.CreateSection(title);

        private VisualElement Field(string name, string label = null)
            => SerializedPropertyField.Create(serializedSettings, name, label);

        private void AddFields(VisualElement parent, params string[] names)
        {
            for (var i = 0; i < names.Length; i++) parent.Add(Field(names[i]));
        }

        private static VisualElement CreateEnumWithInlineValue(
            SerializedProperty enumProperty, string label,
            SerializedProperty valueProperty, System.Func<int, bool> showValue)
        {
            if (enumProperty == null) throw new ArgumentNullException(nameof(enumProperty));
            if (valueProperty == null) throw new ArgumentNullException(nameof(valueProperty));
            if (showValue == null) throw new ArgumentNullException(nameof(showValue));
            var enumField = SerializedPropertyField.Create(enumProperty, label);
            var valueField = (FloatField)SerializedPropertyField.Create(
                valueProperty, string.Empty);
            var row = InspectorVisuals.CreateInlineValueRow(enumField, valueField);
            var dragHandle = CreateFloatDragHandle(valueField);
            row.Insert(1, dragHandle);

            void Refresh()
            {
                var current = InspectorHelpers.RequireProperty(
                    enumProperty.serializedObject, enumProperty.propertyPath);
                var display = showValue(current.enumValueIndex)
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                row.EnableInClassList("lightside-inline-value-row--split",
                    display == DisplayStyle.Flex);
                dragHandle.style.display = display;
                valueField.style.display = display;
            }

            enumField.RegisterCallback<ChangeEvent<System.Enum>>(_ => Refresh());
            return new SerializedPropertyContext(enumProperty, label)
                .Observe(row, Refresh);
        }

        private static VisualElement CreateFloatDragHandle(FloatField field)
        {
            var handle = new VisualElement();
            handle.AddToClassList("lightside-inline-value-row__drag-handle");
            new ValueScrub<float>(handle, field, () => field.enabledInHierarchy, horizontal: true);
            return handle;
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new UniTextSettingsProvider(UniTextMenu.Settings, SettingsScope.Project)
            {
                keywords = new[]
                {
                    "UniText", "Text", "Unicode", "RTL", "Arabic", "Hebrew",
                    "Font", "Thai", "Dictionary", "Segmentation"
                }
            };
        }

        #endregion

        #region Initialization

        private static bool isEnsuring;
        private static bool ensureScheduled;
        private static bool assetsReady;
        private static bool urpShaderRecoveryAttempted;

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            UniTextSettings.Changed -= OnSettingsChanged;
            UniTextSettings.Changed += OnSettingsChanged;
            LightSideSettingsWatch.Register(typeof(UniTextSettings), OnAssetsProcessed);
            UnityEditor.PackageManager.Events.registeredPackages -= OnPackagesRegistered;
            UnityEditor.PackageManager.Events.registeredPackages += OnPackagesRegistered;
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnRenderPipelineTypeChanged;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnRenderPipelineTypeChanged;
        }

        private static void OnPackagesRegistered(UnityEditor.PackageManager.PackageRegistrationEventArgs _)
        {
            urpShaderRecoveryAttempted = false;
            ScheduleEnsureDefaults();
        }

        private static void OnRenderPipelineTypeChanged()
        {
            urpShaderRecoveryAttempted = false;
            ScheduleEnsureDefaults();
        }

        internal static void ScheduleEnsureDefaults()
        {
            if (!assetsReady) return;
            if (ensureScheduled) return;
            ensureScheduled = true;
            EditorApplication.delayCall += () =>
            {
                ensureScheduled = false;
                EnsureDefaults();
            };
        }

        private static void OnAssetsProcessed(bool ensureDefaults)
        {
            assetsReady = true;
            if (ensureDefaults) EnsureDefaults();
        }

        private const string AssetName = "UniTextSettings.asset";
        private const string PackageFolder = "Packages/media.lightside.unitext";
        private const string BackupName = "UniText/Settings.json";

        /// <summary>Fields whose assignment survives deletion of the settings asset.</summary>
        private static readonly string[] GuardedFields = { "paints" };

        private static void OnSettingsChanged(in StateChange change)
        {
            if (UniTextSettings.IsNull) return;
            LightSideSettingsGuard.Capture(UniTextSettings.Instance, BackupName, GuardedFields);
        }

        #endregion

        #region EnsureDefaults — main pipeline

        internal static UniTextSettings EnsureDefaults()
        {
            if (isEnsuring)
                return AssetDatabase.LoadAssetAtPath<UniTextSettings>(AssetPath);

            isEnsuring = true;
            try
            {
                var projectFolder = GetProjectFolder(FindSettingsInProject());
                var settings = LightSideSettingsGuard.Ensure<UniTextSettings>(
                    AssetName, projectFolder, PackageFolder, BackupName, GuardedFields);
                if (settings == null) return null;

                EnsureUrpShaderImports();

                return settings;
            }
            finally
            {
                isEnsuring = false;
            }
        }

        #endregion

        #region Step 1: Copy missing defaults


        #endregion

        #region Step 2: Ensure UniTextSettings in Resources


        #endregion

        #region Step 3: Fill missing references


        #endregion

        #region Step 4: Ensure shaders

        /// <summary>
        /// Repairs shader artifacts imported before Unity finishes registering URP. In that state
        /// <c>PackageRequirements</c> incorrectly excludes the URP SubShader without producing a shader error.
        /// </summary>
        private static void EnsureUrpShaderImports()
        {
            if (urpShaderRecoveryAttempted ||
                GraphicsSettings.currentRenderPipeline?.GetType().FullName != UniversalPipelineAssetType ||
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath($"Packages/{UrpPackageName}/package.json") == null)
                return;

            var shader = Shader.Find(WorldShaderName);
            if (shader == null || HasActiveUrpSubShader(shader)) return;

            urpShaderRecoveryAttempted = true;
            var shaderPath = AssetDatabase.GetAssetPath(shader);
            var shaderFolder = Path.GetDirectoryName(shaderPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(shaderFolder))
            {
                Debug.LogError($"[UniText] Cannot locate shader asset '{WorldShaderName}' for URP import recovery.");
                return;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Shader", new[] { shaderFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".shader"))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        private static bool HasActiveUrpSubShader(Shader shader)
        {
            var data = ShaderUtil.GetShaderData(shader);
            var index = data.ActiveSubshaderIndex;
            return index >= 0 && data.GetSubshader(index).FindTagValue(renderPipelineTag).name == UniversalPipelineTag;
        }

        #endregion

        #region Utilities

        private static string FindSettingsInProject()
        {
            var guids = AssetDatabase.FindAssets("t:UniTextSettings");
            string result = null;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/")) continue;
                var dir = Path.GetDirectoryName(path).Replace('\\', '/');
                if (!dir.EndsWith("/Resources") && dir != "Resources") continue;
                if (result != null)
                {
                    Debug.LogWarning(
                        $"[UniText] Multiple UniTextSettings in Resources. Using: {result}. " +
                        $"Remove duplicate: {path}");
                    break;
                }
                result = path;
            }
            return result;
        }

        private static string GetProjectFolder(string settingsPath)
        {
            if (settingsPath == null) return DefaultProjectFolder;
            var resourcesFolder = Path.GetDirectoryName(settingsPath)?.Replace('\\', '/');
            return Path.GetDirectoryName(resourcesFolder)?.Replace('\\', '/')
                   ?? DefaultProjectFolder;
        }



        #endregion
    }
}
