using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(UIBlur))]
[CanEditMultipleObjects]
public class UIBlurEditor : Editor
{
    private static GUIStyle _warningStyle;
    private static GUIStyle WarningStyle
    {
        get
        {
            if (_warningStyle == null && EditorStyles.textField != null)
                _warningStyle = new GUIStyle(EditorStyles.textField) { normal = { textColor = Color.yellow }, alignment = TextAnchor.MiddleCenter };
            return _warningStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _errorStyle;
    private static GUIStyle ErrorStyle
    {
        get
        {
            if (_errorStyle == null && EditorStyles.textField != null)
                _errorStyle = new GUIStyle(EditorStyles.textField) { normal = { textColor = Color.red }, alignment = TextAnchor.MiddleCenter };
            return _errorStyle ?? GUIStyle.none;
        }
    }

    public static readonly GUIContent LayerContent = new ("Layer", $"The layer to which this Blur belongs. Lower layers are drawn first. {nameof(FlexibleBlurFeature)}.{FlexibleBlurFeature.UIBlurLayersSeeLowerFieldName}, {nameof(FlexibleBlurFeature)}.{FlexibleBlurFeature.BlurredImagesSeeUIBlursFieldName}, and {nameof(FlexibleBlurFeature)}.{FlexibleBlurFeature.BlurredImageLayersSeeLowerFieldName} control how layers interact with one another.");
    public static readonly GUIContent PriorityContent = new ("Priority", "The order in which Blurs are processed within a layer (lower values are drawn first).");
    public static readonly GUIContent BlurStrengthContent = new ("Blur Strength", "The overall strength of the blur effect. For static blurs (eg. blurs where the strength property never changes), it is *always* preferable to leave this at 1 and modify downscale/blur passes to achieve the desired outcome. Blurs with different blur strengths can never be batched.");
    private static readonly GUIContent StayActiveAtZeroCanvasAlphaContent = new ("Active At 0 Canvas Alpha", $"By default, when Canvas alpha is 0 the {nameof(UIBlur)} will turn off to conserve performance. If you're trying to achieve a \"subtractive blur\" effect, this may be unwanted.");
    public static readonly GUIContent BlurReferencesFromContent = new ("Blur References", $"Whether this component will supply its own references to the Camera to blur, and the {nameof(FlexibleBlurFeature)} on that Camera's renderer which will do the blurring, or whether those references will be supplied by a {nameof(BlurReferenceProvider)} component (which should be placed on the same GameObject as the Canvas).");
    private static readonly GUIContent CameraReferenceContent = new ("Camera", "The Camera that supplies the blur effect. In other words the Camera whose output is seen by the component.");
    public static readonly GUIContent FeatureNumberContent = new("Feature #", $"Which {nameof(FlexibleBlurFeature)} to use (there may be more than one belonging to the same renderer). Determined by the order of {nameof(FlexibleBlurFeature)}s in the renderer's feature list. Does not count any other type of feature (eg. if there are two {nameof(FlexibleBlurFeature)}s in the feature list, and 10 other features in between them in the list, then the first {nameof(FlexibleBlurFeature)} is #0 and the second is still #1).");

    private ReorderableList downscaleSectionList;
    private ReorderableList blurSectionList;

    private BlurPresetEditor _blurEditor;
    private BlurPresetEditor BlurEditor => _blurEditor != null
                                         ? _blurEditor
                                         : _blurEditor = target is IBlur blur && blur.Common.blurPreset != null
                                             ? (BlurPresetEditor)CreateEditor(blur.Common.blurPreset) 
                                             : null;

    private SerializedProperty stayActiveAtZeroCanvasAlphaProperty;
    private SerializedProperty referencesFromProperty;
    private SerializedProperty cameraReferencePoperty;
    private SerializedProperty featureNumberProperty;
    private SerializedProperty blurStrengthProperty;
    private SerializedProperty priorityProperty;
    private SerializedProperty layerProperty;
    private SerializedProperty blurPresetProperty;
    private SerializedProperty blurInstanceSettingsProperty;
    private BlurPreset prevBlurPreset;

    void OnEnable()
    {
        stayActiveAtZeroCanvasAlphaProperty = serializedObject.FindProperty(nameof(UIBlur.zeroCanvasAlphaActive));
        referencesFromProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.blurReferencesFrom)}");
        cameraReferencePoperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.cameraReference)}");
        featureNumberProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.featureNumber)}");
        blurStrengthProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.blurStrength)}");
        layerProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.unrankedLayer)}");
        priorityProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.priority)}");
        blurPresetProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.blurPreset)}");
        blurInstanceSettingsProperty = serializedObject.FindProperty($"{UIBlur.BlurCommonFieldName}.{nameof(UIBlurCommon.blurInstanceSettings)}");
    }
    
    void OnDisable()
    {
        if (_blurEditor == null)
            return;

        DestroyImmediate(_blurEditor);
        _blurEditor = null;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(stayActiveAtZeroCanvasAlphaProperty, StayActiveAtZeroCanvasAlphaContent);
        if (!DrawBlurCommonPropertiesOne(referencesFromProperty, cameraReferencePoperty, featureNumberProperty, ((UIBlur)serializedObject.targetObject).gameObject, nameof(UIBlur)))
            return;

        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
        EditorGUILayout.PropertyField(blurStrengthProperty, BlurStrengthContent);

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.PropertyField(layerProperty, LayerContent);
        EditorGUILayout.PropertyField(priorityProperty, PriorityContent);
        EditorGUILayout.EndVertical();

        _ = BlurEditor;
        EditorGUILayout.BeginVertical(GUI.skin.box);
        DrawBlurCommonPropertiesTwo(serializedObject, blurPresetProperty, blurInstanceSettingsProperty, EditorGUIUtility.labelWidth, ref _blurEditor, ref prevBlurPreset, ref downscaleSectionList, ref blurSectionList);
        serializedObject.ApplyModifiedProperties();
    }

    public static bool DrawBlurCommonPropertiesOne(SerializedProperty referencesFrom, SerializedProperty cameraReferenceSelf, SerializedProperty featureNumber, GameObject go, string componentName)
    {
        var isPreviewScene = EditorSceneManager.IsPreviewScene(go.scene);
        var canvas = go.GetComponentInParent<Canvas>(true);
        if (!isPreviewScene && !canvas)
        {
            EditorGUILayout.HelpBox($"No parent canvas detected!\n{componentName} only works inside a UGUI UI.", MessageType.Error);
            return false;
        }

        var usingInstanceSettings = true;
        var cameraReference = cameraReferenceSelf.objectReferenceValue as Camera;

        if (referencesFrom != null)
        {
            EditorGUILayout.PropertyField(referencesFrom, BlurReferencesFromContent);
            if (referencesFrom.enumValueFlag == (int)UIBlurCommon.BlurReferencesFrom.ReferenceProvider)
            {
                var referenceProvider = canvas.GetComponent<BlurReferenceProvider>();
                if (referenceProvider == null)
                {
                    EditorGUILayout.HelpBox($"No {nameof(BlurReferenceProvider)} component on canvas.\n Defaulting to instance settings.", MessageType.Warning);
                }
                else
                {
                    usingInstanceSettings = false;
                    cameraReference = referenceProvider.CameraReference;
                }
            }
        }

        var actualUsedCamera = cameraReference != null ? cameraReference : Camera.main; // can't use null coalescing due to Unity overloads.
        var numberOfFeaturesPresent = GetNumberOfFlexibleBlurFeatures(actualUsedCamera, out var rendererData);

        if (usingInstanceSettings)
        {
            EditorGUILayout.PropertyField(cameraReferenceSelf, CameraReferenceContent);
            if (!cameraReference)
            {
                if (Camera.main)
                    EditorGUILayout.HelpBox("No Camera reference supplied. Using Camera.main.", MessageType.Warning);
                else
                    EditorGUILayout.HelpBox("No Camera reference supplied.", MessageType.Error);
            }
            if (numberOfFeaturesPresent > 1 || featureNumber.intValue != 0)
            {
                EditorGUILayout.PropertyField(featureNumber, FeatureNumberContent);
                featureNumber.intValue = Mathf.Max(featureNumber.intValue, 0);
                if (featureNumber.intValue >= numberOfFeaturesPresent)
                    EditorGUILayout.HelpBox($"Invalid feature #. {nameof(FlexibleBlurFeature)} count is {numberOfFeaturesPresent}.", MessageType.Warning);
            }
        }

        if (numberOfFeaturesPresent == 0)
        {
            EditorGUILayout.HelpBox($"{actualUsedCamera.name} is missing {nameof(FlexibleBlurFeature)}!", MessageType.Error);
            if (GUILayout.Button("Open Renderer?", GUILayout.Height(28)))
            {
                EditorGUIUtility.PingObject(rendererData); 
                Selection.activeObject = rendererData;
            }
            EditorGUILayout.Space(24);
        }

        return true;
    }

    public static bool DrawBlurCommonPropertiesTwo(SerializedObject serializedObject, SerializedProperty blurPresetProperty, SerializedProperty blurInstanceSettings, float originalLabelWidth, ref BlurPresetEditor presetEditor, ref BlurPreset previousBlurPreset, ref ReorderableList downscaleSections, ref ReorderableList blurSections)
    {
        var blurPreset = blurPresetProperty.objectReferenceValue as BlurPreset;
        if (blurPreset == null)
        {
            var propertyRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 35, EditorGUIUtility.singleLineHeight);
            var buttonRect = propertyRect;
            buttonRect.width = 50;
            buttonRect.x = originalLabelWidth + 20;
            EditorGUIUtility.labelWidth += 50;

            EditorGUI.PropertyField(propertyRect, blurPresetProperty);
            EditorGUIUtility.labelWidth = originalLabelWidth;

            if (GUI.Button(buttonRect, "New"))
            {
                presetEditor = null;
                blurPreset = CreateInstance<BlurPreset>();
                blurPreset.TryFillSettings();

                var instanceSettings = blurInstanceSettings.boxedValue as BlurSettings;
                foreach (var qualityLevelSettings in blurPreset.Settings)
                    qualityLevelSettings.CopySettings(instanceSettings);

                blurPresetProperty.objectReferenceValue = blurPreset;

                serializedObject.ApplyModifiedProperties();
                var path = PresetSavePath.GetPresetSavePath("New Blur Preset.asset");
                AssetDatabase.CreateAsset(blurPreset, path);
                AssetDatabase.SaveAssets();
                // Create/Save assets seems to dispose the whole SerializedObject in recent versions of Unity. But I also see bug reports from years back. Seems to be a problem that gets reintroduced every so often. So just return to be safe.
                return false; 
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox("No preset selected—using instance settings.", MessageType.Warning);
            BlurPresetEditor.DrawBlurProperties(blurInstanceSettings.Copy(), ref downscaleSections, ref blurSections);
        }
        else
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(blurPresetProperty);
            if (EditorGUI.EndChangeCheck())
                blurPreset.TryFillSettings();

            EditorGUILayout.EndVertical();

            if (blurPreset == previousBlurPreset)
                presetEditor.DrawGUI();
            else
                presetEditor = null;
        }
        previousBlurPreset = blurPreset;
        return true;
    }

    public static int GetNumberOfFlexibleBlurFeatures(Camera camera, out ScriptableRendererData rendererData)
    {
        rendererData = null;
        if (camera == null)
            return -1;

        var data = camera.GetUniversalAdditionalCameraData();

        var rendererIdxField = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        var rendererIdxFieldObj = rendererIdxField?.GetValue(data);
        if (rendererIdxFieldObj is not int rendererIdx)
            return -1;

        var currentPipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (currentPipelineAsset == null)
            currentPipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

        if (currentPipelineAsset == null)
            return -1;

        // -1 signifies the default renderer
        if (rendererIdx == -1)
        {
            var defaultRendererIdxField = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            var defaultRendererIdxFieldObj = defaultRendererIdxField?.GetValue(currentPipelineAsset);
            if (defaultRendererIdxFieldObj is not int actualRendererIdx)
                return -1;

            rendererIdx = actualRendererIdx;
        }

#if UNITY_2023_2_OR_NEWER
        rendererData = currentPipelineAsset.rendererDataList[rendererIdx];
#else
        var dataListField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        var dataListObj = dataListField?.GetValue(currentPipelineAsset);
        if (dataListObj is not ScriptableRendererData[] dataList)
            return -1;
        rendererData = dataList[rendererIdx];
#endif

        int featureCount = 0;
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (feature is FlexibleBlurFeature)
                featureCount++;
        }
        return featureCount;
    }
}
}
