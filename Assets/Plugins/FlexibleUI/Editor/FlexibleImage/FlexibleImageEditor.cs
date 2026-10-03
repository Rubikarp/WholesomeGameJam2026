using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UI;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(FlexibleImage))]
[CanEditMultipleObjects]
public partial class FlexibleImageEditor : ImageEditor
{
    private enum RaycastTargetType { Disabled, Standard, Advanced }

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

    private static readonly GUIContent RaycastFlagsContent = new("Raycast Flags", "The procedural properties that will be taken into account for the advanced raycasting mode.");
    private static readonly GUIContent RaycastPaddingContent = new ("Raycast Padding");
    private static readonly GUIContent RaycastTargetContent = new ("Raycast Target", "Whether the element interacts with event systems. Standard works the same as a regular Image component. Advanced lets you specify how raycasting works in regards to procedural elements (eg. not capturing pointers that are aimed at invisible areas)");
    private static readonly GUIContent RaycastTargetContentAdvanced = new ("Raycast Target*", "Due to unfortunate Unity engine code outside my control, advanced mode may interfere with raycasting on child Image components that are outside the rect bounds of this component (or any other location that would return false for this component) unless child components are also ProceduralBlurredImages. For more information, see rant in comments of the the FlexibleImage class.");
    private static readonly GUIContent DataPresetContent = new ("Data Preset", "Reference to a ScriptableObject preset that controls procedural elements.");
    private static readonly GUIContent DeleteInstanceDataContent = new ("Delete Instance Data", $"Deletes instance data from the component, which will reduce its serialized size in scenes and prefabs. If the component is ever set to anything other then Data Mode - {FlexibleImage.QuadDataMode.Preset}, new instance data will be created.");

    private SerializedProperty selectableProperty;
    private SerializedProperty animationModeProperty;
    private SerializedProperty spriteProperty;
    private SerializedProperty preserveAspectProperty;
    private SerializedProperty imageTypeProperty;
    private SerializedProperty normalRaycastPaddingProperty;
    private SerializedProperty mobileRaycastPaddingProperty;
    private SerializedProperty useAdvancedRaycastProperty;
    private SerializedProperty advancedRaycastFlagsProperty;
    private SerializedProperty flexibleImageDataPresetProperty;
    private SerializedProperty dataModeProperty;
    private SerializedProperty quadDataContainerProperty;
    private SerializedProperty quadDataListProperty;
    private SerializedProperty primaryQuadIdxProperty;
    private SerializedProperty quadDataPresetContainerProperty;
    private SerializedProperty selectedQuadIdxProperty;
    private SerializedProperty quadDataListPropertyPreset;
    private SerializedProperty quadPrimaryIdxPropertyPreset;
    private SerializedProperty selectedQuadIdxPropertyPreset;

    private const string ShowBlurSectionKey = "jg_showBlurSection";
    private const string ShowPaddingKey = "jg_showPadding";

    private FlexibleImage flexibleImage;
    private SerializedObject dataPresetSO;
    private ReorderableList quadDataReorderableList;
    private QuadDataEditor quadDataEditor;
    private RaycastTargetType raycastTargetType;
    private List<FlexibleImage> allFlexibleImages;
    private Dictionary<FlexibleImage, List<BaseFollower>> followersDict = new();
    private bool previewingAnimation, doRaycastAreaDirtyFix, forceUpdatePreset;

    partial void OnEnableBlur();
    protected override void OnEnable()
    {
        if (Undo.isProcessing)
            QuadDataEditor.KeepExistingAnimationIndices = 2;

        flexibleImage = serializedObject.targetObject as FlexibleImage;
        allFlexibleImages = serializedObject.targetObjects.OfType<FlexibleImage>().ToList();
        followersDict.Clear();
        foreach (var otherFlexibleImage in allFlexibleImages)
            followersDict.Add(otherFlexibleImage, otherFlexibleImage.GetComponentsInChildren<BaseFollower>().ToList());

        flexibleImageDataPresetProperty = serializedObject.FindProperty(FlexibleImage.FlexibleImageDataPresetFieldName);
        dataModeProperty = serializedObject.FindProperty(FlexibleImage.DataModeFieldName);
        quadDataContainerProperty = serializedObject.FindProperty(FlexibleImage.QuadDataContainerFieldName);
        quadDataListProperty = quadDataContainerProperty.FindPropertyRelative(QuadDataContainer.QuadDataListFieldName);
        primaryQuadIdxProperty = quadDataContainerProperty.FindPropertyRelative(nameof(QuadDataContainer.primaryQuadIdx));
        selectedQuadIdxProperty = quadDataContainerProperty.FindPropertyRelative(nameof(QuadDataContainer.editorSelectedQuadIdx));
        if (selectedQuadIdxProperty.intValue > quadDataListProperty.arraySize - 1)
            selectedQuadIdxProperty.intValue = primaryQuadIdxProperty.intValue;

        selectableProperty = serializedObject.FindProperty(nameof(FlexibleImage.selectable));
        animationModeProperty = serializedObject.FindProperty(nameof(FlexibleImage.animationMode));
        spriteProperty = serializedObject.FindProperty("m_Sprite");
        preserveAspectProperty = serializedObject.FindProperty("m_PreserveAspect");
        imageTypeProperty = serializedObject.FindProperty("m_Type");
        normalRaycastPaddingProperty = serializedObject.FindProperty(FlexibleImage.NormalRaycastPaddingFieldName);
        mobileRaycastPaddingProperty = serializedObject.FindProperty(FlexibleImage.MobileRaycastPaddingFieldName);
        useAdvancedRaycastProperty = serializedObject.FindProperty(FlexibleImage.UseAdvancedRaycastFieldName);
        advancedRaycastFlagsProperty = serializedObject.FindProperty(FlexibleImage.AdvancedRaycastFlagsFieldName);
        InitDataPresetSerializedProperties();

        OnEnableBlur();
        base.OnEnable();

        raycastTargetType =
            !m_RaycastTarget.boolValue
                ? RaycastTargetType.Disabled
                : useAdvancedRaycastProperty.boolValue
                    ? RaycastTargetType.Advanced
                    : RaycastTargetType.Standard;
    }

    partial void OnDisableBlur();
    protected override void OnDisable()
    {
        OnDisableBlur();
        base.OnDisable();

        quadDataEditor?.DisposeColorPresetEditor();
        if (flexibleImage == null)
            return;

        foreach (var fi in allFlexibleImages)
        {
            if (!fi.DisplayAnimationStateFromInspector(0, 0))
                continue;

            EditorUtility.SetDirty(fi);
            HandleFollowers(followersDict[fi]);
        }
    }

    private void HandleFollowers(List<BaseFollower> followers)
    {
        if (followers.Count <= 0)
            return;

        flexibleImage.ForceMeshUpdateInEditor();
        followers.ForEach(x =>
        {
            x.RefreshFromInspector();
            EditorUtility.SetDirty(x);
        });
    }

    private void InitDataPresetSerializedProperties()
    {
        if (flexibleImageDataPresetProperty.objectReferenceValue == null)
            return;

        dataPresetSO = new SerializedObject(flexibleImageDataPresetProperty.objectReferenceValue);
        quadDataPresetContainerProperty = dataPresetSO.FindProperty(nameof(QuadDataPreset.quadDataContainer));
        quadDataListPropertyPreset = quadDataPresetContainerProperty.FindPropertyRelative(QuadDataContainer.QuadDataListFieldName);
        quadPrimaryIdxPropertyPreset = quadDataPresetContainerProperty.FindPropertyRelative(nameof(QuadDataContainer.primaryQuadIdx));
        selectedQuadIdxPropertyPreset = quadDataPresetContainerProperty.FindPropertyRelative(nameof(QuadDataContainer.editorSelectedQuadIdx));
        if (selectedQuadIdxPropertyPreset.intValue > quadDataListPropertyPreset.arraySize - 1)
            selectedQuadIdxPropertyPreset.intValue = quadPrimaryIdxPropertyPreset.intValue;
    }

    partial void BlurInspectorGUI(Color originalGUIBgColor, float originalLabelWidth, GUIStyle sectionStyle, ref bool earlyReturn);

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        if (doRaycastAreaDirtyFix)
        {
            allFlexibleImages.ForEach(x => x.SetRaycastAreaDirty());
            doRaycastAreaDirtyFix = false;
        }

        var originalGUIBgColor = GUI.backgroundColor;
        var originalLabelWidth = EditorGUIUtility.labelWidth;
        var originalFieldWidth = EditorGUIUtility.fieldWidth;

        SectionStyle.fixedWidth = EditorGUIUtility.currentViewWidth - 13;
        var earlyReturnBlur = false;
        BlurInspectorGUI(originalGUIBgColor, originalLabelWidth, SectionStyle, ref earlyReturnBlur);
        if (earlyReturnBlur)
            return;

        EditorGUILayout.Space(1);

        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();

        SpriteGUI();
        EditorGUILayout.Space(2);

        var hasSprite = spriteProperty.objectReferenceValue != null;
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

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(dataModeProperty);
        EditorGUILayout.Space(1.5f);


        if (EditorGUI.EndChangeCheck())
        {
            flexibleImage.UnsubscribeFromActiveDataContainerFromInspector();
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
            flexibleImage.SubscribeToActiveDataContainerFromInspector();
        }
        else
        {
            serializedObject.ApplyModifiedProperties();
            serializedObject.Update();
        }

        var usingDataPreset = false;
        if (dataModeProperty.enumValueIndex == (int)FlexibleImage.QuadDataMode.Preset)
        {
            EditorGUI.BeginChangeCheck();
            if (flexibleImageDataPresetProperty.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(flexibleImageDataPresetProperty, DataPresetContent);
                if (quadDataListProperty.arraySize > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button(DeleteInstanceDataContent))
                        quadDataListProperty.arraySize = 0;
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                var propertyRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth - 40, EditorGUIUtility.singleLineHeight);
                var buttonRect = propertyRect;
                buttonRect.width = 50;
                buttonRect.x = originalLabelWidth + 20;
                EditorGUIUtility.labelWidth += 52;
                EditorGUI.PropertyField(propertyRect, flexibleImageDataPresetProperty, DataPresetContent);
                EditorGUIUtility.labelWidth = originalLabelWidth;
                if (GUI.Button(buttonRect, "New"))
                {
                    var preset = CreateInstance<QuadDataPreset>();
                    var presetContainer = preset.quadDataContainer;
                    for (int i = 0; i < quadDataListProperty.arraySize; i++)
                    {
                        if (i >= presetContainer.Count)
                            presetContainer.AddQuadData();

                        var existingQuad = quadDataListProperty.GetArrayElementAtIndex(i).boxedValue as QuadData;
                        presetContainer[i].Copy(existingQuad);
                        presetContainer[i].name = existingQuad.name;
                    }
                    presetContainer.primaryQuadIdx = primaryQuadIdxProperty.intValue;

                    flexibleImage.UnsubscribeFromActiveDataContainerFromInspector();
                    flexibleImageDataPresetProperty.objectReferenceValue = preset;
                    serializedObject.ApplyModifiedProperties();
                    InitDataPresetSerializedProperties();
                    flexibleImage.SubscribeToActiveDataContainerFromInspector();

                    var path = PresetSavePath.GetPresetSavePath("New FlexibleImageData Preset.asset");
                    AssetDatabase.CreateAsset(preset, path);
                    AssetDatabase.SaveAssets();
                    return;
                }
            }
            if (EditorGUI.EndChangeCheck())
            {
                flexibleImage.UnsubscribeFromActiveDataContainerFromInspector();
                serializedObject.ApplyModifiedProperties();
                InitDataPresetSerializedProperties();
                flexibleImage.SubscribeToActiveDataContainerFromInspector();
                serializedObject.Update();
                if (flexibleImageDataPresetProperty.objectReferenceValue != null)
                {
                    dataPresetSO = new SerializedObject(flexibleImageDataPresetProperty.objectReferenceValue);
                    quadDataPresetContainerProperty = dataPresetSO.FindProperty(nameof(QuadDataPreset.quadDataContainer));
                    quadDataListPropertyPreset = quadDataPresetContainerProperty.FindPropertyRelative(QuadDataContainer.QuadDataListFieldName);
                    quadPrimaryIdxPropertyPreset = quadDataPresetContainerProperty.FindPropertyRelative(nameof(QuadDataContainer.primaryQuadIdx));
                }
            }

            if (flexibleImageDataPresetProperty.objectReferenceValue != null)
            {
                usingDataPreset = true;
                var dirtied = QuadDataPresetEditor.DrawQuadSelector(dataPresetSO, quadDataListPropertyPreset, quadPrimaryIdxPropertyPreset, selectedQuadIdxPropertyPreset, flexibleImage.QuadDataPreset.quadDataContainer, ref quadDataReorderableList, ref quadDataEditor, flexibleImage.rectTransform);
                if (dirtied)
                    allFlexibleImages.ForEach(x => x.QuadDataPreset.quadDataContainer.MessageVerticesDirty());
            }
            else
            {
                EditorGUILayout.HelpBox("No preset selected—using instance (multiple mode) settings.", MessageType.Warning);
                if (quadDataListProperty.arraySize == 0)
                {
                    quadDataListProperty.arraySize = 1;
                    quadDataListProperty.GetArrayElementAtIndex(0).boxedValue = new QuadData(flexibleImage.InstanceQuadDataContainer, "Quad0");
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }

                if (quadDataListProperty.arraySize == 0)
                {
                    quadDataListProperty.arraySize = 1;
                    quadDataListProperty.GetArrayElementAtIndex(0).boxedValue = new QuadData(flexibleImage.InstanceQuadDataContainer, "Quad0");
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }

                if (quadDataListProperty.arraySize > 1)
                {
                    QuadDataPresetEditor.DrawQuadSelector(serializedObject, quadDataListProperty, primaryQuadIdxProperty, selectedQuadIdxProperty, flexibleImage.ActiveQuadDataContainer, ref quadDataReorderableList, ref quadDataEditor, flexibleImage.rectTransform);
                }
                else if (quadDataEditor == null || quadDataEditor.quadData != flexibleImage.PrimaryQuadData)
                {
                    quadDataEditor = new QuadDataEditor(flexibleImage.PrimaryQuadData, quadDataListProperty, flexibleImage.ActiveQuadDataContainer.primaryQuadIdx);
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }
            }
        }
        else
        {
            if (quadDataListProperty.arraySize == 0)
            {
                quadDataListProperty.arraySize = 1;
                quadDataListProperty.GetArrayElementAtIndex(0).boxedValue = new QuadData(flexibleImage.InstanceQuadDataContainer, "Quad0");
                serializedObject.ApplyModifiedProperties();
                serializedObject.Update();
            }

            if (dataModeProperty.enumValueIndex == (int)FlexibleImage.QuadDataMode.Single)
            {
                if (quadDataEditor == null || quadDataEditor.quadData != flexibleImage.PrimaryQuadData)
                {
                    quadDataEditor = new QuadDataEditor(flexibleImage.PrimaryQuadData, quadDataListProperty, flexibleImage.ActiveQuadDataContainer.primaryQuadIdx);
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                }
            }
            else
            {
                QuadDataPresetEditor.DrawQuadSelector(serializedObject, quadDataListProperty, primaryQuadIdxProperty, selectedQuadIdxProperty, flexibleImage.ActiveQuadDataContainer, ref quadDataReorderableList, ref quadDataEditor, flexibleImage.rectTransform);
            }
        }

        EditorGUILayout.Space(2.5f);
        var repaint = quadDataEditor.DrawAnimationEditor(usingDataPreset ? dataPresetSO : serializedObject, originalLabelWidth, animationModeProperty, selectableProperty, flexibleImage, allFlexibleImages, SectionStyle);
        if (repaint)
        {
            foreach (var fi in allFlexibleImages)
                HandleFollowers(followersDict[fi]);

            Repaint();
        }

        serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
        
        EditorGUILayout.Space(1.5f);
        var (verticesDirty, raycastAreaDirty, earlyReturn) = quadDataEditor.DrawQuadDataEditor(usingDataPreset ? dataPresetSO : serializedObject, originalLabelWidth, originalFieldWidth, flexibleImage, spriteProperty: spriteProperty, sectionStyleOverride: SectionStyle);

        if (earlyReturn)
            return;

        if (verticesDirty)
        {
            if (usingDataPreset)
                flexibleImage.QuadDataPreset.quadDataContainer.MessageVerticesDirty();
            else
                allFlexibleImages.ForEach(x => x.SetVerticesDirty());

            allFlexibleImages.ForEach(x => HandleFollowers(followersDict[x]));
        }

        if (raycastAreaDirty)
            allFlexibleImages.ForEach(x => x.SetRaycastAreaDirty());

        // Captures undo/redo, and context menu items.
        if (usingDataPreset && (Event.current.type == EventType.Used || Event.current.type == EventType.ValidateCommand))
            flexibleImage.QuadDataPreset.quadDataContainer.MessageVerticesDirty();
    }

    private new void RaycastControlsGUI()
    {
        raycastTargetType = 
            !m_RaycastTarget.boolValue
                ? RaycastTargetType.Disabled
                : useAdvancedRaycastProperty.boolValue
                    ? RaycastTargetType.Advanced
                    : RaycastTargetType.Standard;

        EditorGUI.showMixedValue = m_RaycastTarget.hasMultipleDifferentValues || useAdvancedRaycastProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        raycastTargetType = (RaycastTargetType)EditorGUILayout.EnumPopup(raycastTargetType == RaycastTargetType.Advanced ? RaycastTargetContentAdvanced : RaycastTargetContent, raycastTargetType);
        if (EditorGUI.EndChangeCheck())
        {
            m_RaycastTarget.boolValue = raycastTargetType != RaycastTargetType.Disabled;
            useAdvancedRaycastProperty.boolValue = raycastTargetType == RaycastTargetType.Advanced;
            if (target is Graphic graphic)
                graphic.SetRaycastDirty();
        }
        EditorGUI.showMixedValue = false;

        if (raycastTargetType == RaycastTargetType.Disabled)
            return;

        if (raycastTargetType == RaycastTargetType.Advanced)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(advancedRaycastFlagsProperty, RaycastFlagsContent);
            if (EditorGUI.EndChangeCheck())
                flexibleImage.SetRaycastAreaDirty();
        }

        var height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
        if (SessionState.GetBool(ShowPaddingKey, false))
            height += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 11f;

        var rect = EditorGUILayout.GetControlRect(true, height);
        EditorGUI.BeginProperty(rect, RaycastPaddingContent, m_RaycastPadding);
        rect.height = EditorGUIUtility.singleLineHeight;

        using (var check = new EditorGUI.ChangeCheckScope())
        {
            SessionState.SetBool(ShowPaddingKey, EditorGUI.Foldout(rect, SessionState.GetBool(ShowPaddingKey, false), "Raycast Padding", true));
            if (check.changed)
                SceneView.RepaintAll();
        }

        if (SessionState.GetBool(ShowPaddingKey, false))
        {
            using var check = new EditorGUI.ChangeCheckScope();

            EditorGUI.indentLevel++;
            var newNormalPadding = normalRaycastPaddingProperty.vector4Value;
            var newMobileExpansion = mobileRaycastPaddingProperty.vector4Value;

            rect.y += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 1.25f;
            EditorGUI.LabelField(rect, "Base Padding");

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newNormalPadding.x = EditorGUI.FloatField(rect, "Left", newNormalPadding.x);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newNormalPadding.y = EditorGUI.FloatField(rect, "Bottom", newNormalPadding.y);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newNormalPadding.z = EditorGUI.FloatField(rect, "Right", newNormalPadding.z);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newNormalPadding.w = EditorGUI.FloatField(rect, "Top", newNormalPadding.w);

            rect.y += (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 1.25f;
            EditorGUI.LabelField(rect, "Extra IOS/Android Padding");

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newMobileExpansion.x = EditorGUI.FloatField(rect, "Left", newMobileExpansion.x);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newMobileExpansion.y = EditorGUI.FloatField(rect, "Bottom", newMobileExpansion.y);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newMobileExpansion.z = EditorGUI.FloatField(rect, "Right", newMobileExpansion.z);

            rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            newMobileExpansion.w = EditorGUI.FloatField(rect, "Top", newMobileExpansion.w);

            if (check.changed)
            {
                normalRaycastPaddingProperty.vector4Value = newNormalPadding;
                mobileRaycastPaddingProperty.vector4Value = newMobileExpansion;
                flexibleImage.SetRaycastAreaDirty();
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}
}
