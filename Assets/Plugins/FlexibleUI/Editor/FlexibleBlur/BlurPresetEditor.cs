using System;
using UnityEngine;
using UnityEditor;
using UnityEditorInternal;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(BlurPreset))]
public class BlurPresetEditor : Editor
{
    private static GUIStyle _qualityBoxStyle;
    private static GUIStyle QualityBoxStyle
    {
        get
        {
            if (_qualityBoxStyle == null && GUI.skin?.box != null)
                _qualityBoxStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(15, 15, 0, 5) };
            return _qualityBoxStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _qualityFontStyle;
    private static GUIStyle QualityFontStyle
    {
        get
        {
            if (_qualityFontStyle == null && GUI.skin?.label != null)
                _qualityFontStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            return _qualityFontStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _qualityToggleStyle;
    private static GUIStyle QualityToggleStyle
    {
        get
        {
            if (_qualityToggleStyle == null && GUI.skin?.toggle != null)
                _qualityToggleStyle = new GUIStyle(GUI.skin.toggle) { fontSize = 14, fontStyle = FontStyle.Bold };
            return _qualityToggleStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _qualityButtonStyle;
    private static GUIStyle QualityButtonStyle
    {
        get
        {
            if (_qualityButtonStyle == null && GUI.skin?.button != null)
                _qualityButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
            return _qualityButtonStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _sliderBackgroundStyle;
    private static GUIStyle SliderBackgroundStyle
    {
        get
        {
            if (_sliderBackgroundStyle == null && GUI.skin?.horizontalSlider != null)
                _sliderBackgroundStyle = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = 10 };
            return _sliderBackgroundStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _sliderThumbStyle;
    private static GUIStyle SliderThumbStyle
    {
        get
        {
            if (_sliderThumbStyle == null && GUI.skin?.horizontalSliderThumb != null)
                _sliderThumbStyle = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedHeight = 20, fixedWidth = 80 };
            return _sliderThumbStyle ?? GUIStyle.none;
        }
    }
    private static GUIStyle _sectionLabelStyle;
    private static GUIStyle SectionLabelStyle
    {
        get
        {
            if (_sectionLabelStyle == null && GUI.skin?.label != null)
                _sectionLabelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            return _sectionLabelStyle ?? GUIStyle.none;
        }
    }

    private static readonly GUIContent AdditionalDistanceContent = new ("Additional Distance/Iteration");

    private SerializedProperty settingsProperty;
    private BlurPreset blurPreset;
    private ReorderableList downscaleSectionList, blurSectionList;
    private int selectedQualityLevel;
    private bool preview;

    private void OnEnable()
    {
        blurPreset = serializedObject.targetObject as BlurPreset;
        settingsProperty = serializedObject.FindProperty(BlurPreset.SettingsFieldName);
        selectedQualityLevel = QualitySettings.GetQualityLevel();
    }

    private void OnDisable()
    {
        preview = false;
        blurPreset.preview = -1;
    }

    public override void OnInspectorGUI() => DrawGUI();

    public void DrawGUI()
    {
        serializedObject.Update();

        bool addedQualitySetting = blurPreset.TryFillSettings();
        if (addedQualitySetting)
        {
            EditorUtility.SetDirty(target);
            serializedObject.ApplyModifiedProperties();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            return;
        }

        var origBackgroundColor = GUI.backgroundColor;
        GUI.backgroundColor = EditorGUIUtility.isProSkin ? Color.black : new Color(0.9f, 1f, 1f);

        EditorGUILayout.BeginVertical(QualityBoxStyle);
        GUI.backgroundColor = Color.cyan;
        selectedQualityLevel = (int)GUILayout.HorizontalSlider(selectedQualityLevel, 0, (float)BlurPreset.GetQualitySettingsCount() - 1, SliderBackgroundStyle, SliderThumbStyle);
        EditorGUILayout.Space(22);
        EditorGUILayout.BeginHorizontal();

        EditorGUILayout.BeginVertical();

        EditorGUILayout.LabelField($"Quality: {QualitySettings.names[selectedQualityLevel]}", QualityFontStyle);

        var prevPreviewVal = blurPreset.preview;

        GUI.color = Color.white;


        if (selectedQualityLevel != QualitySettings.GetQualityLevel())
        {
            preview = GUILayout.Toggle(preview, "Preview", QualityToggleStyle);
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.EndVertical();

        blurPreset.preview = preview ? selectedQualityLevel : -1;

        if (blurPreset.preview != prevPreviewVal)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Apply to all", QualityButtonStyle, GUILayout.Width(120), GUILayout.Height(34)))
        {
            var currentProperties = blurPreset.Settings[selectedQualityLevel];
            foreach (var qualityLevel in blurPreset.Settings)
                qualityLevel.CopySettings(currentProperties);

            EditorUtility.SetDirty(target);
            settingsProperty.serializedObject.ApplyModifiedProperties();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            return;
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        GUI.backgroundColor = origBackgroundColor;

        var currentSettings = settingsProperty.GetArrayElementAtIndex(selectedQualityLevel);
        DrawBlurProperties(currentSettings, ref downscaleSectionList, ref blurSectionList);;

        serializedObject.ApplyModifiedProperties();
    }

    public static void DrawBlurProperties(SerializedProperty settings, ref ReorderableList downscaleSectionList, ref ReorderableList blurSectionList)
    {
        var originalLabelWidth = EditorGUIUtility.labelWidth;

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUI.BeginChangeCheck();
        var refResProp = settings.FindPropertyRelative(nameof(BlurSettings.referenceResolution));
        EditorGUILayout.PropertyField(refResProp);
        if (EditorGUI.EndChangeCheck())
            refResProp.intValue = Mathf.Max(refResProp.intValue, 0);

        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.hqResample)));
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Downscale Pass", SectionLabelStyle);

        var downscaleSectionsProp = settings.FindPropertyRelative(nameof(BlurSettings.downscaleSections));
        downscaleSectionList ??= new ReorderableList(settings.serializedObject, downscaleSectionsProp, true, false, true, true)
        {
            drawElementCallback = (rect, index, _, _) => OnDrawBlurItems(rect, index, 8, downscaleSectionsProp),
            elementHeightCallback = index => GetElementHeight(index, downscaleSectionsProp),
            drawElementBackgroundCallback = OnDrawBackground
        };
        downscaleSectionList.DoLayoutList();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Blur Pass", SectionLabelStyle);
        var blurSectionsProp = settings.FindPropertyRelative(nameof(BlurSettings.blurSections));
        blurSectionList ??= new ReorderableList(settings.serializedObject, blurSectionsProp, true, false, true, true)
        {
            drawElementCallback = (rect, index, _, _) => OnDrawBlurItems(rect, index, 128, blurSectionsProp),
            elementHeightCallback = index => GetElementHeight(index, blurSectionsProp),
            onAddCallback = OnAddToList,
            drawElementBackgroundCallback = OnDrawBackground
        };
        blurSectionList.DoLayoutList();
        EditorGUILayout.Space(2);
        EditorGUIUtility.labelWidth = originalLabelWidth;
        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.blurAdditionalDistancePerIteration)), AdditionalDistanceContent);
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Image Adjustment", SectionLabelStyle);
        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.ditherStrength)));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.brightness)));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.contrast)));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.vibrancy)));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative(nameof(BlurSettings.tint)));
        EditorGUILayout.EndVertical();

        float GetElementHeight(int index, SerializedProperty listProp)
        {
            var element = listProp.GetArrayElementAtIndex(index);
            var algoIdx = element.FindPropertyRelative($"{BlurSection.AlgorithmIdxFieldName}").intValue;
            var isSeparable = BlurAlgorithm.All[algoIdx].SecondKernelIdx >= 0;
            if (isSeparable)
            {
                return EditorGUIUtility.singleLineHeight * 2 + 6f;
            }
            return EditorGUIUtility.singleLineHeight + 3;
        }

        void OnDrawBackground(Rect backgroundRect, int index, bool isActive, bool isFocused)
        {
            if (isFocused)
                EditorGUI.DrawRect(backgroundRect, EditorGUIUtility.isProSkin ? new Color(0.3f,0.45f,0.45f,1f) : new Color(0.7f, 0.7f, 0.75f, 1f));
            else
                ReorderableList.defaultBehaviours.DrawElementBackground(backgroundRect, index, isActive, isFocused, true);
        }

        void OnDrawBlurItems(Rect rect, int index, int maxIterations, SerializedProperty listProp)
        {
            var isTwoRowRect = rect.height > EditorGUIUtility.singleLineHeight * 2;
            rect.y += 2.5f;
            rect.height -= 6.5f;

            if (isTwoRowRect)
                rect = new Rect(rect.position, new Vector2(rect.width, rect.height * 0.5f));

            var (firstLabelWidth, secondLabelWidth, thirdLabelWidth, firstLabelName, secondLabelName, thirdLabelName) = rect.width < 400 
                ? (27.5f, 22f, 24f, "Algo", "Str", "Iter")
                : (57.5f, 52.5f, 60f, "Algorithm", "Strength", "Iterations");
  
            var firstSection = (firstLabelWidth, 65f, 155f);
            var secondSection = (secondLabelWidth, 20f, 60f);
            var thirdSection = (thirdLabelWidth, 20f, 60f);
            var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, rect, 4f, float.MaxValue, 2f, firstSection, secondSection, thirdSection);
            var element = listProp.GetArrayElementAtIndex(index);
            var blurSection = element.boxedValue as BlurSection;
            var algoIdx = Array.IndexOf(BlurAlgorithm.All, blurSection.BlurAlgorithm);
            EditorGUIUtility.labelWidth = firstLabelWidth;
            EditorGUI.BeginChangeCheck();

            algoIdx = EditorGUI.Popup(rectArr[0], firstLabelName, algoIdx, BlurAlgorithm.Names);
            if (EditorGUI.EndChangeCheck())
            {
                element.boxedValue = new BlurSection(algoIdx, blurSection.iterations, blurSection.sampleDistance, blurSection.horizontalSamplesPerSide, blurSection.verticalSamplesPerSide);;
                element.serializedObject.ApplyModifiedProperties();
                element.serializedObject.Update();
            }

            EditorGUIUtility.labelWidth = secondLabelWidth;
            var strengthProp = element.FindPropertyRelative($"{nameof(BlurSection.sampleDistance)}");
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rectArr[1], strengthProp, new GUIContent(secondLabelName));
            if (EditorGUI.EndChangeCheck())
                strengthProp.floatValue = Mathf.Max(strengthProp.floatValue, 0f);

            EditorGUIUtility.labelWidth = thirdLabelWidth;
            var iterationsProp = element.FindPropertyRelative($"{nameof(BlurSection.iterations)}");
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rectArr[2], iterationsProp, new GUIContent(thirdLabelName));
            if (EditorGUI.EndChangeCheck())
                iterationsProp.intValue = Mathf.Clamp(iterationsProp.intValue, 0, maxIterations);

            if (!isTwoRowRect)
            {
                EditorGUIUtility.labelWidth = originalLabelWidth;
                return;
            }

            var horSamplesProp = element.FindPropertyRelative($"{nameof(BlurSection.horizontalSamplesPerSide)}");
            var vertSamplesProp = element.FindPropertyRelative($"{nameof(BlurSection.verticalSamplesPerSide)}");
            var sizeReadout = new GUIContent($"({(horSamplesProp.intValue > 0 ? 2 * horSamplesProp.intValue + 1 : 0)} + {(vertSamplesProp.intValue > 0 ? 2 * vertSamplesProp.intValue + 1 : 0)} Taps)");
            var readoutLabelWidth = EditorStyles.label.CalcSize(sizeReadout).x;

            (firstLabelWidth, secondLabelWidth, firstLabelName, secondLabelName) = rect.width < 100 + readoutLabelWidth
                ? (12.5f, 12.5f, "H", "V")
                : (22.5f, 26f, "Hor", "Vert");

            firstSection = (firstLabelWidth, 22f, 25f);
            secondSection = (secondLabelWidth, 22f, 25f);
            thirdSection = (readoutLabelWidth, 0f, 0f);

            rect.y += EditorGUIUtility.singleLineHeight + 3;
            rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, rect, 4f, 8f, 2f, firstSection, secondSection, thirdSection);

            EditorGUIUtility.labelWidth = firstLabelWidth;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rectArr[0], horSamplesProp, new GUIContent(firstLabelName));
            if (EditorGUI.EndChangeCheck())
                horSamplesProp.intValue = Mathf.Clamp(horSamplesProp.intValue, 0, 32);

            EditorGUIUtility.labelWidth = secondLabelWidth;
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rectArr[1], vertSamplesProp, new GUIContent(secondLabelName));
            if (EditorGUI.EndChangeCheck())
                vertSamplesProp.intValue = Mathf.Clamp(vertSamplesProp.intValue, 0, 32);

            EditorGUIUtility.labelWidth = readoutLabelWidth;
            EditorGUI.LabelField(rectArr[2], sizeReadout);
            EditorGUIUtility.labelWidth = originalLabelWidth;
        }

        void OnAddToList(ReorderableList list)
        {
            var oldArraySize = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            BlurSection newItem;
            if (oldArraySize == 0)
            {
                newItem = new BlurSection(0, 1, 1.5f);
            }
            else
            {
                var lastItem = list.serializedProperty.GetArrayElementAtIndex(oldArraySize - 1).boxedValue as BlurSection;
                newItem = new BlurSection(lastItem.BlurAlgorithm, lastItem.iterations, lastItem.sampleDistance, lastItem.horizontalSamplesPerSide, lastItem.verticalSamplesPerSide);
            }
            list.serializedProperty.GetArrayElementAtIndex(oldArraySize).boxedValue = newItem;
        }
    }
}
}