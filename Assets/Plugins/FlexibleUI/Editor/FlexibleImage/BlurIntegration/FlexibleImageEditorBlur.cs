using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleImageEditor
{
    private static readonly GUIContent AlphaBlendContent = new ("Alpha Blend", $"Controls how the {nameof(FlexibleImage)} reacts to alpha. 0 does not alpha blend at all, but rather adjusts the strength of the blur. 1 only alpha blends, and does not touch blur strength. Blur strength is generally a much nicer way to fade in/out, but occludes UI elements behind the {nameof(FlexibleImage)}. If both high quality fade and non-occlusion are desired, an intermediate value can be set.");
    private static readonly GUIContent AdditionalBlurPaddingContent = new ("Additional Padding", $"Increases the dimensions of the blur that is computed onto the RenderTexture that {nameof(FlexibleImage)} grabs from. Can slightly change how the edge of the blur appears, and can fix issues with black outlines in VR with fast movement. If too much padding is added, {nameof(FlexibleImage)}'s with different blurs that share a layer are more likely to overwrite one another in the RenderTexture.");
    private static readonly GUIContent FillEntireRenderTextureContent = new ("Fill RenderTexture", $"Only available for batched {nameof(FlexibleImage)}s, instead of filling a section of the RenderTexture that corresponds to the outer perimeter of the batched {nameof(FlexibleImage)}s, fill the entire RenderTexture with the blur. This can be useful when to avoid noise when the perimeter of the batched {nameof(FlexibleImage)}'s is constantly changing, and may improve compatibility in certain edge cases. Effects a group of batched {nameof(FlexibleImage)}'s when any one of them have this flag set, even if there is only one {nameof(FlexibleImage)} in the group.");
    private static readonly GUIContent BatchWithSimilarContent = new ("Batch With Similar", $"Batch with other {nameof(FlexibleImage)} components sharing the same preset and priority, drawing a single large blur as opposed to each blur individually. The trade-off is fewer draw/compute calls, but you also end up blurring more pixels. To the extent that batchable blurs are numerous and close together, batching is recommended and can greatly improve performance. An example of where batching could hurt performance is if you only had two small blurs on opposite screen corners--with batching you would be performing work on every pixel between both corners. (Note there may be a small visual difference with batching, especially along the edges of the {nameof(FlexibleImage)} due to the way sampling is clamped inside of the blurred area.)");

    private SerializedProperty blurInstanceSettings;
    private SerializedProperty referencesFromProperty;
    private SerializedProperty cameraReferenceProperty;
    private SerializedProperty featureNumberProperty;
    private SerializedProperty blurStrengthProperty;
    private SerializedProperty layerProperty;
    private SerializedProperty priorityProperty;
    private SerializedProperty blurPresetProperty;
    private SerializedProperty blurEnabledProperty;
    private SerializedProperty sourceImageFadeProperty;
    private SerializedProperty alphaBlendProperty;
    private SerializedProperty additionalBlurPaddingProperty;
    private SerializedProperty batchWithSimilarProperty;
    private SerializedProperty fillEntireRenderTextureProperty;

    private ReorderableList downscaleSectionList;
    private ReorderableList blurSectionList;

    private BlurPreset prevBlurPrest;
    private BlurPresetEditor _blurEditor;
    private BlurPresetEditor BlurEditor => _blurEditor != null
                                         ? _blurEditor
                                         : _blurEditor = target is IBlur blur && blur.Common.blurPreset != null
                                             ? (BlurPresetEditor)CreateEditor(blur.Common.blurPreset) 
                                             : null;

    partial void OnEnableBlur()
    {
        referencesFromProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurReferencesFrom)}");
        cameraReferenceProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.cameraReference)}");
        featureNumberProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.featureNumber)}");
        blurStrengthProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurStrength)}");
        layerProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.unrankedLayer)}");
        priorityProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.priority)}");
        blurPresetProperty = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurPreset)}");
        blurInstanceSettings = serializedObject.FindProperty($"{FlexibleImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurInstanceSettings)}");
        blurEnabledProperty = serializedObject.FindProperty(FlexibleImage.BlurEnabledFieldName);
        sourceImageFadeProperty = serializedObject.FindProperty(FlexibleImage.SourceImageFadeFieldName);
        alphaBlendProperty = serializedObject.FindProperty(FlexibleImage.AlphaBlendFieldName);
        additionalBlurPaddingProperty = serializedObject.FindProperty(nameof(FlexibleImage.additionalBlurPadding));
        batchWithSimilarProperty = serializedObject.FindProperty(nameof(FlexibleImage.tryBatchWithSimilar));
        fillEntireRenderTextureProperty = serializedObject.FindProperty(nameof(FlexibleImage.fillEntireRenderTexture));
    }
    
    partial void OnDisableBlur()
    {
        if (_blurEditor == null)
            return;

        DestroyImmediate(_blurEditor);
        _blurEditor = null;
    }

    partial void BlurInspectorGUI(Color originalGUIBgColor, float originalLabelWidth, GUIStyle sectionStyle, ref bool earlyReturn)
    {
        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(blurEnabledProperty);
        var blurEnabled = blurEnabledProperty.boolValue;
        // We'll try to autopopulate the blur camera in some common situations blur gets enabled.
        if (EditorGUI.EndChangeCheck() && blurEnabled && cameraReferenceProperty.objectReferenceValue == null)
        {
            var canvas = flexibleImage.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.isRootCanvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            {
#if UNITY_6000_4_OR_NEWER
                foreach (var camera in FindObjectsByType<Camera>())
#else
                foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
#endif
                {
                    var cameraData = camera.GetUniversalAdditionalCameraData();
                    if (cameraData.renderType == CameraRenderType.Overlay)
                        continue;

                    var cameraStack = cameraData.cameraStack;
                    var idx = cameraStack.IndexOf(canvas.worldCamera);
                    if (idx < 0)
                        continue;

                    cameraReferenceProperty.objectReferenceValue = idx == 0 ? camera : cameraStack[idx - 1];
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    break;
                }
            }
        }
        if (blurEnabled)
        {
            if (!UIBlurEditor.DrawBlurCommonPropertiesOne(referencesFromProperty, cameraReferenceProperty, featureNumberProperty, ((FlexibleImage)serializedObject.targetObject).gameObject, nameof(FlexibleImage)))
            {
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.PropertyField(blurStrengthProperty, UIBlurEditor.BlurStrengthContent);
            EditorGUILayout.PropertyField(sourceImageFadeProperty, BlurredImageEditor.SourceImageFadeContent);
            EditorGUILayout.PropertyField(alphaBlendProperty, AlphaBlendContent);
            EditorGUILayout.EndVertical();

            GUI.backgroundColor = Color.cyan;
            EditorGUIUtility.labelWidth = EditorGUIUtility.currentViewWidth;
            var sectionRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            SessionState.SetBool(ShowBlurSectionKey, EditorGUI.Foldout(sectionRect, SessionState.GetBool(ShowBlurSectionKey, false), "Advanced Blur", sectionStyle));
            if (GUI.Button(sectionRect, "", GUIStyle.none))
                SessionState.SetBool(ShowBlurSectionKey, !SessionState.GetBool(ShowBlurSectionKey, false));

            EditorGUIUtility.labelWidth = originalLabelWidth;
            GUI.backgroundColor = originalGUIBgColor;
            if (SessionState.GetBool(ShowBlurSectionKey, false))
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.PropertyField(layerProperty, UIBlurEditor.LayerContent);
                EditorGUILayout.PropertyField(priorityProperty, UIBlurEditor.PriorityContent);
                EditorGUILayout.EndVertical();
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.PropertyField(additionalBlurPaddingProperty, AdditionalBlurPaddingContent);
                EditorGUILayout.EndVertical();
                EditorGUILayout.BeginVertical(GUI.skin.box);
                var inPresetMode = blurPresetProperty.objectReferenceValue as BlurPreset != null;
                if (inPresetMode)
                {
                    EditorGUILayout.PropertyField(batchWithSimilarProperty, BatchWithSimilarContent);
                    if (batchWithSimilarProperty.boolValue)
                        EditorGUILayout.PropertyField(fillEntireRenderTextureProperty, FillEntireRenderTextureContent);
                }

                _ = BlurEditor;
                if (!UIBlurEditor.DrawBlurCommonPropertiesTwo(serializedObject, blurPresetProperty, blurInstanceSettings, originalLabelWidth, ref _blurEditor, ref prevBlurPrest, ref downscaleSectionList, ref blurSectionList))
                    earlyReturn = true;
            }
            EditorGUILayout.Space(2);
        }
        else
        {
            EditorGUILayout.EndVertical();
        }
    }
}
}
