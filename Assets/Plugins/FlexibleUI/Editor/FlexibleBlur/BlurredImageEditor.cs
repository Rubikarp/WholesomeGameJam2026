using UnityEditor;
using UnityEditor.UI;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(BlurredImage))]
[CanEditMultipleObjects]
public class BlurredImageEditor : ImageEditor
{
    private static GUIStyle _sectionStyle;
    private static GUIStyle SectionStyle
    {
        get
        {
            if (_sectionStyle == null && EditorStyles.foldoutHeader != null)
                _sectionStyle = new GUIStyle(EditorStyles.foldoutHeader) { normal = { textColor = Color.white }, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            return _sectionStyle ?? GUIStyle.none;
        }
    }

    public static readonly GUIContent SourceImageFadeContent = new ("Source Image Fade", "Controls the extent to which the source Image Sprite is drawn over the blur effect. (Color alpha channel fades the blur effect itself, not the Sprite).");
    private static readonly GUIContent AlphaBlendContent = new ("Alpha Blend", $"Controls how the {nameof(BlurredImage)} reacts to alpha. 0 does not alpha blend at all, but rather adjusts the strength of the blur. 1 only alpha blends, and does not touch blur strength. Blur strength is generally a much nicer way to fade in/out, but occludes UI elements behind the {nameof(BlurredImage)}. If both high quality fade and non-occlusion are desired, an intermediate value can be set.");
    private static readonly GUIContent BatchWithSimilarContent = new ("Batch With Similar", $"Batch with other {nameof(BlurredImage)} components sharing the same preset and priority, drawing a single large blur as opposed to each blur individually. The trade-off is fewer draw/compute calls, but you also end up blurring more pixels. To the extent that batchable blurs are numerous and close together, batching is recommended and can greatly improve performance. An example of where batching could hurt performance is if you only had two small blurs on opposite screen corners--with batching you would be performing work on every pixel between both corners. (Note there may be a small visual difference with batching, especially along the edges of the {nameof(BlurredImage)} due to the way sampling is clamped inside of the blurred area.)");
    private static readonly GUIContent AdditionalBlurPaddingContent = new ("Additional Padding", $"Increases the dimensions of the blur that is computed onto the RenderTexture that {nameof(BlurredImage)} grabs from. Can slightly change how the edge of the blur appears, and can fix issues with black outlines in VR with fast movement. If too much padding is added, {nameof(BlurredImage)}'s with different blurs that share a layer are more likely to overwrite one another in the RenderTexture.");
    private static readonly GUIContent FillEntireRenderTextureContent = new ("Fill RenderTexture", $"Only available for batched {nameof(BlurredImage)}s, instead of filling a section of the RenderTexture that corresponds to the outer perimeter of the batched {nameof(BlurredImage)}s, fill the entire RenderTexture with the blur. This can be useful when to avoid noise when the perimeter of the batched {nameof(BlurredImage)}'s is constantly changing, and may improve compatibility in certain edge cases. Effects a group of batched {nameof(BlurredImage)}'s when any one of them have this flag set, even if there is only one {nameof(BlurredImage)} in the group.");

    private SerializedProperty spriteProperty;
    private SerializedProperty colorProperty;
    private SerializedProperty preserveAspectProperty;
    private SerializedProperty useSpriteMeshProperty;
    private SerializedProperty imageTypeProperty;
    private SerializedProperty normalRaycastPaddingProperty;
    private SerializedProperty mobileRaycastAreaExpansionProperty;

    private SerializedProperty blurInstanceSettings;
    private SerializedProperty referencesFromProperty;
    private SerializedProperty cameraReferenceProperty;
    private SerializedProperty featureNumberProperty;
    private SerializedProperty blurStrengthProperty;
    private SerializedProperty layerProperty;
    private SerializedProperty priorityProperty;
    private SerializedProperty blurPresetProperty;
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

    private const string ShowBlurSectionKey = "jg_showBlurSection";

    protected override void OnEnable()
    {
        spriteProperty = serializedObject.FindProperty("m_Sprite");
        colorProperty = serializedObject.FindProperty("m_Color");
        preserveAspectProperty = serializedObject.FindProperty("m_PreserveAspect");
        useSpriteMeshProperty = serializedObject.FindProperty("m_UseSpriteMesh");
        imageTypeProperty = serializedObject.FindProperty("m_Type");
        referencesFromProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurReferencesFrom)}");
        cameraReferenceProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.cameraReference)}");
        featureNumberProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.featureNumber)}");
        blurStrengthProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurStrength)}");
        layerProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.unrankedLayer)}");
        priorityProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.priority)}");
        blurPresetProperty = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurPreset)}");
        blurInstanceSettings = serializedObject.FindProperty($"{BlurredImage.BlurCommonFieldName}.{nameof(UIBlurCommon.blurInstanceSettings)}");
        sourceImageFadeProperty = serializedObject.FindProperty(BlurredImage.SourceImageFadeFieldName);
        alphaBlendProperty = serializedObject.FindProperty(BlurredImage.AlphaBlendFieldName);
        additionalBlurPaddingProperty = serializedObject.FindProperty(nameof(BlurredImage.additionalBlurPadding));
        batchWithSimilarProperty = serializedObject.FindProperty(nameof(BlurredImage.tryBatchWithSimilar));
        fillEntireRenderTextureProperty = serializedObject.FindProperty(nameof(BlurredImage.fillEntireRenderTexture));
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        if (_blurEditor != null)
        {
            DestroyImmediate(_blurEditor);
            _blurEditor = null;
        }
        base.OnDisable();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var originalGUIBgColor = GUI.backgroundColor;
        var originalLabelWidth = EditorGUIUtility.labelWidth;

        EditorGUILayout.BeginVertical(GUI.skin.box);

        if (!UIBlurEditor.DrawBlurCommonPropertiesOne(referencesFromProperty, cameraReferenceProperty, featureNumberProperty, ((BlurredImage)serializedObject.targetObject).gameObject, nameof(BlurredImage)))
        {
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.PropertyField(blurStrengthProperty, UIBlurEditor.BlurStrengthContent);
        EditorGUILayout.PropertyField(sourceImageFadeProperty, SourceImageFadeContent);
        EditorGUILayout.PropertyField(alphaBlendProperty, AlphaBlendContent);
        EditorGUILayout.EndVertical();

        GUI.backgroundColor = Color.cyan;
        SectionStyle.fixedWidth = EditorGUIUtility.currentViewWidth - 13;
        var sectionRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
        SessionState.SetBool(ShowBlurSectionKey, EditorGUI.Foldout(sectionRect, SessionState.GetBool(ShowBlurSectionKey, false), "Advanced Blur", SectionStyle));
        if (GUI.Button(sectionRect, "", GUIStyle.none))
            SessionState.SetBool(ShowBlurSectionKey, !SessionState.GetBool(ShowBlurSectionKey, false));

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
                return;
        }
        EditorGUILayout.Space(2);

        EditorGUILayout.Space(1);

        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();

        SpriteGUI();
        EditorGUILayout.PropertyField(colorProperty);
        EditorGUILayout.Space(2);

        var hasSprite = spriteProperty.objectReferenceValue != null;

        if (hasSprite && imageTypeProperty.enumValueIndex == (int)Image.Type.Simple)
            EditorGUILayout.PropertyField(useSpriteMeshProperty);

        if (hasSprite && imageTypeProperty.enumValueIndex is (int)Image.Type.Simple or (int)Image.Type.Filled)
            EditorGUILayout.PropertyField(preserveAspectProperty);

        RaycastControlsGUI();
        MaskableControlsGUI();

        TypeGUI();
        if (!hasSprite && imageTypeProperty.enumValueIndex is (int)Image.Type.Sliced or (int)Image.Type.Tiled)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("No Sprite assigned. Defaulting to \"Simple\" Image type.", MessageType.Warning);
            EditorGUI.indentLevel--;
        }

        SetShowNativeSize(hasSprite, true);
        NativeSizeButtonGUI();

        serializedObject.ApplyModifiedProperties();
    }
}
}