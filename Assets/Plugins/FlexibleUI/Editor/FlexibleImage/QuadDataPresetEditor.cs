using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[CustomEditor(typeof(QuadDataPreset))]
public class QuadDataPresetEditor : Editor
{
    private static GUIStyle _rightAlignedTextFieldStyle;
    private static GUIStyle RightAlignedTextFieldStyle
    {
        get
        {
            if (_rightAlignedTextFieldStyle == null && EditorStyles.textField != null)
                _rightAlignedTextFieldStyle = new GUIStyle(EditorStyles.textField) { alignment = TextAnchor.MiddleRight };
            return _rightAlignedTextFieldStyle ?? GUIStyle.none;
        }
    }

    private static readonly GUIContent AdvancedQuadOptionsContent = new ("Flags", $"Special settings for this quad:\n\n<B>{nameof(QuadData.QuadModifiers.DisableSprite)}</B> disables drawing any sprite (<I>Source Image</I>) that has been assigned to the component, as well as blur when <I>Flexible Blur integration</I> is available.\n\n<B>{nameof(QuadData.QuadModifiers.ForceSimpleMesh)}</B> forces the quad to behave as if the component is set to <I>Image Type-Simple</I>, even if it is set to <I>Sliced</I>, <I>Tiled</I>, or <I>Filled</I>.");
    private static readonly GUIContent SetPrimaryQuadContent = new("Set Primary", "The primary quad is the one that is used for input/raycasting."); 
    private static readonly GUIContent CopyContent = new ("Copy");
    private static readonly GUIContent PasteContent = new ("Paste");
    private static QuadData quadDataClipBoard;
    private static Vector2 referenceParentSize = new (500, 500);
    internal static Vector2 ReferenceParentSize => referenceParentSize;

    private SerializedProperty quadDataContainerProperty;
    private SerializedProperty quadDataListProperty;
    private SerializedProperty primaryQuadIdxProperty;
    private SerializedProperty selectedQuadIdxProperty;
    private QuadDataPreset dataPreset;
    private ReorderableList quadDataReorderableList;
    private QuadDataContainer quadDataContainer;
    private QuadDataEditor quadDataEditor;

    private void OnEnable()
    {
        if (Undo.isProcessing)
            QuadDataEditor.KeepExistingAnimationIndices = 2;

        dataPreset = serializedObject.targetObject as QuadDataPreset;
        quadDataContainer = dataPreset.quadDataContainer;
        quadDataContainerProperty = serializedObject.FindProperty(nameof(QuadDataPreset.quadDataContainer));
        quadDataListProperty = quadDataContainerProperty.FindPropertyRelative(QuadDataContainer.QuadDataListFieldName);
        primaryQuadIdxProperty = quadDataContainerProperty.FindPropertyRelative(nameof(QuadDataContainer.primaryQuadIdx));
        selectedQuadIdxProperty = quadDataContainerProperty.FindPropertyRelative(nameof(quadDataContainer.editorSelectedQuadIdx));
        if (selectedQuadIdxProperty.intValue > quadDataListProperty.arraySize - 1)
            selectedQuadIdxProperty.intValue = primaryQuadIdxProperty.intValue;
    }

    private void OnDisable() => quadDataEditor?.DisposeColorPresetEditor();

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var originalLabelWidth = EditorGUIUtility.labelWidth;
        var originalFieldWidth = EditorGUIUtility.fieldWidth;

        referenceParentSize = EditorGUILayout.Vector2Field("Reference Parent Size", referenceParentSize);
        EditorGUILayout.Space(4f);
        DrawQuadSelector(serializedObject, quadDataListProperty, primaryQuadIdxProperty, selectedQuadIdxProperty, quadDataContainer, ref quadDataReorderableList, ref quadDataEditor);
        EditorGUILayout.Space(1f);
        quadDataEditor.DrawAnimationEditor(serializedObject, originalLabelWidth);
        EditorGUILayout.Space(1f);
        quadDataEditor.DrawQuadDataEditor(serializedObject, originalLabelWidth, originalFieldWidth);
        quadDataContainer.MessageVerticesDirty();
    }

    public static bool DrawQuadSelector(SerializedObject so, SerializedProperty quadDataListProperty, SerializedProperty primaryQuadIdxProperty, SerializedProperty selectedQuadIdxProperty, QuadDataContainer quadDataContainer, ref ReorderableList quadDataReorderableList, ref QuadDataEditor quadDataEditor, RectTransform parentRectTransform = null)
    {
        var dirtied = false;
        so.Update();
        var originalLabelWidth = EditorGUIUtility.labelWidth;

        if (quadDataReorderableList == null || quadDataReorderableList.serializedProperty != quadDataListProperty)
        {
            quadDataReorderableList = new ReorderableList(so, quadDataListProperty, true, false, true, quadDataListProperty.arraySize > 1)
            {
                drawElementCallback = OnDrawListItems,
                onAddCallback = OnAddToList,
                onRemoveCallback =  OnRemoveFromList,
                drawElementBackgroundCallback = OnDrawBackground,
                onReorderCallbackWithDetails = OnReorder
            };
            quadDataReorderableList.Select(0);
        }

        if (selectedQuadIdxProperty.intValue > quadDataListProperty.arraySize - 1)
        {
            selectedQuadIdxProperty.intValue = primaryQuadIdxProperty.intValue;
            if (selectedQuadIdxProperty.intValue > quadDataListProperty.arraySize - 1)
                selectedQuadIdxProperty.intValue = primaryQuadIdxProperty.intValue = 0;
        }

        EditorGUILayout.BeginHorizontal();

        quadDataReorderableList.index = selectedQuadIdxProperty.intValue;
        quadDataReorderableList.DoLayoutList();
        var layoutListRect = GUILayoutUtility.GetLastRect();
        layoutListRect.y += layoutListRect.height - EditorGUIUtility.singleLineHeight;

        layoutListRect.height = EditorGUIUtility.singleLineHeight;
        layoutListRect.width = Mathf.Clamp(layoutListRect.width - 70, 0, 90);

        var isPrimary = primaryQuadIdxProperty.intValue == quadDataReorderableList.index;
        GUI.enabled = !isPrimary;
        if (GUI.Button(layoutListRect, new GUIContent(SetPrimaryQuadContent)))
            primaryQuadIdxProperty.intValue = quadDataReorderableList.index;
        GUI.enabled = true;

        dirtied |= so.hasModifiedProperties;
        so.ApplyModifiedProperties();
        so.Update();
        selectedQuadIdxProperty.intValue = quadDataReorderableList.index;

        if (quadDataEditor == null || !ReferenceEquals(quadDataEditor.quadData, quadDataContainer[quadDataReorderableList.index]))
        {
            quadDataEditor = new QuadDataEditor(quadDataContainer[selectedQuadIdxProperty.intValue], quadDataListProperty, quadDataReorderableList.index);
            dirtied |= so.hasModifiedProperties;
            so.ApplyModifiedProperties();
        }

        if (quadDataEditor != null)
        {
            var cRect = EditorGUILayout.GetControlRect(false, 25f + EditorGUIUtility.singleLineHeight * 6, GUILayout.Width(Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.45f, 180f, 350f)));

            cRect.height = EditorGUIUtility.singleLineHeight;
            cRect.x += 4f;
            cRect.width -= 8f;
            var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, cRect, 2f, 8f, 0f, (45f, 15f, 9999f), (45f, 15f, 9999f));

            var advancedOptionsRect = rectArr[0];
            advancedOptionsRect.width = cRect.width;
            EditorGUIUtility.labelWidth = 25f;
            EditorGUIUtility.labelWidth = 45f;
            EditorGUI.PropertyField(advancedOptionsRect, quadDataEditor.advancedQuadSettingsProperty, AdvancedQuadOptionsContent);

            rectArr[0].y += 5f + EditorGUIUtility.singleLineHeight;
            rectArr[1].y += 5f + EditorGUIUtility.singleLineHeight;
            EditorGUIUtility.labelWidth = 45f;

            DrawPositionAndSizeFields(ref rectArr[0], ref rectArr[1], quadDataEditor.anchorMinProperty, quadDataEditor.anchorMaxProperty, quadDataEditor.anchoredPositionProperty, quadDataEditor.sizeDeltaProperty, quadDataEditor.pivotProperty);
            cRect.y += 13f + EditorGUIUtility.singleLineHeight * 3;
            rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Left, cRect, 2f, 8f, 0f, (75f, 0, 0), (12.5f, 15f, 9999f), (12.5f, 15f, 9999f));
            EditorGUIUtility.labelWidth = 12.5f;
            DrawAnchorFields(ref rectArr[0], ref rectArr[1], ref rectArr[2], quadDataEditor.anchorMinProperty, quadDataEditor.anchorMaxProperty, quadDataEditor.anchoredPositionProperty, quadDataEditor.sizeDeltaProperty, quadDataEditor.pivotProperty, parentRectTransform?.rect.size ?? referenceParentSize);
            DrawPivotField(ref rectArr[0], ref rectArr[1], ref rectArr[2], quadDataEditor.anchoredPositionProperty, quadDataEditor.sizeDeltaProperty, quadDataEditor.pivotProperty);
            EditorGUIUtility.labelWidth = originalLabelWidth;
        }
        EditorGUILayout.EndHorizontal();

        dirtied |= so.hasModifiedProperties;
        so.ApplyModifiedProperties();
        return dirtied;

        void OnDrawListItems(Rect rect, int index, bool isActive, bool isFocused)
        {
            rect.y += 2.5f;
            rect.height -= 5f;
            var fullPrimary = rect.width > 150f;
            var rectArr = EditorHelpers.DivideRect(EditorHelpers.Alignment.Center, rect, 4f, float.MaxValue, 2f, (fullPrimary ? 47.5f : 12.5f, 0f, 0f), (0f, 0f, 9999f), (10f, 0f, 0f));

            var quadData = quadDataListProperty.GetArrayElementAtIndex(index);
            var quadDataNameProp = quadData.FindPropertyRelative(nameof(QuadData.name));

            if (index == primaryQuadIdxProperty.intValue)
            {
                var originalColor = GUI.color;
                GUI.color = isActive || isFocused ? new Color(0.6784314f, 0.8470589f, 0.9019608f) : new Color(0.65f, 0.65f, 0.65f);
                EditorGUI.LabelField(rectArr[0], fullPrimary ? "Primary" : "P");
                GUI.color = originalColor;
            }

            quadDataNameProp.stringValue = EditorGUI.TextField(rectArr[1], quadDataNameProp.stringValue, RightAlignedTextFieldStyle);
            EditorGUIUtility.labelWidth = originalLabelWidth;

            rectArr[2].width += 5f;
            var enabledProp = quadData.FindPropertyRelative(QuadData.EnabledFieldName);
            enabledProp.boolValue = EditorGUI.Toggle(rectArr[2], enabledProp.boolValue);

            if (Event.current.type != EventType.ContextClick || !rect.Contains(Event.current.mousePosition))
                return;

            var menu = new GenericMenu();
            menu.AddItem(CopyContent, false, () =>
            {
                var selectedQuadData = quadData.boxedValue as QuadData;
                quadDataClipBoard ??= new QuadData();
                quadDataClipBoard.Copy(selectedQuadData, false);

            });
            if (quadDataClipBoard != null)
            {
                menu.AddItem(PasteContent, false, () =>
                {
                    var oldName = quadDataNameProp.stringValue;
                    var newQuadData = new QuadData(quadDataContainer, quadDataClipBoard);
                    quadData.boxedValue = newQuadData;
                    quadDataNameProp.stringValue = oldName;
                    so.ApplyModifiedProperties();
                    so.Update();
                });
            }
            else
            {
                menu.AddDisabledItem(PasteContent);
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Reset"), false, () =>
            {
                var oldName = quadDataNameProp.stringValue;
                var newProps = new QuadData(quadDataContainer);
                quadData.boxedValue = newProps;
                quadDataNameProp.stringValue = oldName;
                so.ApplyModifiedProperties();
                so.Update();
            });

            menu.ShowAsContext();
            Event.current.Use();
        }

        void OnAddToList(ReorderableList list)
        {
            var copyFrom = list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1).boxedValue as QuadData;
            var newItem = new QuadData(quadDataContainer, copyFrom) { name = $"Quad{list.serializedProperty.arraySize}" };
            list.serializedProperty.arraySize++;
            list.serializedProperty.GetArrayElementAtIndex(list.serializedProperty.arraySize - 1).boxedValue = newItem;
            list.displayRemove = true;
            so.ApplyModifiedProperties();
            so.Update();
        }

        void OnRemoveFromList(ReorderableList list)
        {
            quadDataListProperty.DeleteArrayElementAtIndex(list.index);
            if (list.serializedProperty.arraySize <= 1)
                list.displayRemove = false;

            if (list.index > list.count - 1)
                list.index--;

            so.ApplyModifiedProperties();
            so.Update();
        }

        void OnDrawBackground(Rect backgroundRect, int index, bool isActive, bool isFocused)
        {
            backgroundRect.width -= 2f;

            var enabled = quadDataContainer[index].Enabled;

            if (isFocused || isActive)
            {
                if (EditorGUIUtility.isProSkin)
                    EditorGUI.DrawRect(backgroundRect, enabled ? new Color(0.3f, 0.45f, 0.45f, 1f) : new Color(0.225f, 0.333f, 0.333f, 1f));
                else
                    EditorGUI.DrawRect(backgroundRect, enabled ? new Color(0.7f, 0.7f, 0.75f, 1f) : new Color(0.666f, 0.666f, 0.7f, 1f));
            }
            else if (!enabled)
            {
                EditorGUI.DrawRect(backgroundRect, EditorGUIUtility.isProSkin ? new Color(0.2f, 0.2f, 0.2f, 1f) : new Color(0.555f, 0.555f, 0.6f, 1f));
            }
            else
            {
                ReorderableList.defaultBehaviours.DrawElementBackground(backgroundRect, index, isActive, isFocused, true);
            }
        }

        void OnReorder(ReorderableList list, int oldIndex, int newIndex)
        {
            var primaryIdx = primaryQuadIdxProperty.intValue;
            if (primaryIdx == oldIndex)
                primaryQuadIdxProperty.intValue = newIndex;
            else if (oldIndex <= primaryIdx && newIndex >= primaryIdx)
                primaryQuadIdxProperty.intValue--;
            else if (oldIndex >= primaryIdx && newIndex <= primaryIdx)
                primaryQuadIdxProperty.intValue++;
        }
    }

    internal static void DrawPositionAndSizeFields(ref Rect leftRect, ref Rect rightRect, SerializedProperty anchorMin, SerializedProperty anchorMax, SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot, bool axisLabels = false)
    {
        var stretchedX = !Mathf.Approximately(anchorMin.vector2Value.x, anchorMax.vector2Value.x);
        var stretchedY = !Mathf.Approximately(anchorMin.vector2Value.y, anchorMax.vector2Value.y);

        if (!stretchedX)
            EditorGUI.PropertyField(leftRect, anchoredPosition.FindPropertyRelative("x"), new GUIContent(axisLabels ? "X" : "Pos X"));
        else
            DrawOffsetField(leftRect, "Left", 0, useMax: false, sign: 1f);

        if (!stretchedY)
            EditorGUI.PropertyField(rightRect, anchoredPosition.FindPropertyRelative("y"), new GUIContent(axisLabels ? "Y" : "Pos Y"));
        else
            DrawOffsetField(rightRect, "Top", 1, useMax: true, sign: -1f);

        leftRect.y += 1.5f + EditorGUIUtility.singleLineHeight;
        rightRect.y += 1.5f + EditorGUIUtility.singleLineHeight;

        if (!stretchedX)
            EditorGUI.PropertyField(leftRect, sizeDelta.FindPropertyRelative("x"), new GUIContent(axisLabels ? "X" : "Width"));
        else
            DrawOffsetField(leftRect, "Right", 0, useMax: true, sign: -1f);

        if (!stretchedY)
            EditorGUI.PropertyField(rightRect, sizeDelta.FindPropertyRelative("y"), new GUIContent(axisLabels ? "Y" : "Height"));
        else
            DrawOffsetField(rightRect, "Bottom", 1, useMax: false, sign: 1f);

        void DrawOffsetField(Rect rect, string label, int axis, bool useMax, float sign)
        {
            var offset = useMax ? GetOffsetMax(anchoredPosition, sizeDelta, pivot) : GetOffsetMin(anchoredPosition, sizeDelta, pivot);
            var value = offset[axis] * sign;

            EditorGUI.BeginChangeCheck();
            value = EditorGUI.FloatField(rect, label, value);
            if (!EditorGUI.EndChangeCheck())
                return;

            offset[axis] = value * sign;
            if (useMax)
                SetOffsetMax(anchoredPosition, sizeDelta, pivot, offset);
            else
                SetOffsetMin(anchoredPosition, sizeDelta, pivot, offset);
        }
    }

    internal static void DrawAnchorFields(ref Rect leftRect, ref Rect centerRect, ref Rect rightRect, SerializedProperty anchorMin, SerializedProperty anchorMax, SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot, Vector2 parentSize)
    {
        EditorGUI.LabelField(leftRect, "Anchor Min");

        var oldMin = anchorMin.vector2Value;
        EditorGUI.PropertyField(centerRect, anchorMin.FindPropertyRelative("x"), new GUIContent("X"));
        EditorGUI.PropertyField(rightRect, anchorMin.FindPropertyRelative("y"), new GUIContent("Y"));
        if (oldMin != anchorMin.vector2Value)
        {
            SetAnchorSmart(anchoredPosition, sizeDelta, pivot, parentSize, 0, false, oldMin.x, anchorMin.vector2Value.x);
            SetAnchorSmart(anchoredPosition, sizeDelta, pivot, parentSize, 1, false, oldMin.y, anchorMin.vector2Value.y);
        }

        leftRect.y += 1.5f + EditorGUIUtility.singleLineHeight;
        centerRect.y += 1.5f + EditorGUIUtility.singleLineHeight;
        rightRect.y += 1.5f + EditorGUIUtility.singleLineHeight;

        EditorGUI.LabelField(leftRect, "Anchor Max");

        var oldMax = anchorMax.vector2Value;
        EditorGUI.PropertyField(centerRect, anchorMax.FindPropertyRelative("x"), new GUIContent("X"));
        EditorGUI.PropertyField(rightRect, anchorMax.FindPropertyRelative("y"), new GUIContent("Y"));
        
        if (oldMax != anchorMax.vector2Value)
        {
            SetAnchorSmart(anchoredPosition, sizeDelta, pivot, parentSize, 0, true, oldMax.x, anchorMax.vector2Value.x);
            SetAnchorSmart(anchoredPosition, sizeDelta, pivot, parentSize, 1, true, oldMax.y, anchorMax.vector2Value.y);
        }

        leftRect.y += 8f + EditorGUIUtility.singleLineHeight;
        centerRect.y += 8f + EditorGUIUtility.singleLineHeight;
        rightRect.y += 8f + EditorGUIUtility.singleLineHeight;
    }

    internal static void DrawPivotField(ref Rect leftRect, ref Rect centerRect, ref Rect rightRect, SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot)
    {
        var oldPivot = pivot.vector2Value;
        EditorGUI.LabelField(leftRect, "Pivot");
        EditorGUI.PropertyField(centerRect, pivot.FindPropertyRelative("x"), new GUIContent("X"));
        EditorGUI.PropertyField(rightRect, pivot.FindPropertyRelative("y"), new GUIContent("Y"));
        if (oldPivot != pivot.vector2Value)
        {
            SetPivotSmart(anchoredPosition, sizeDelta, pivot, 0, oldPivot.x, pivot.vector2Value.x);
            SetPivotSmart(anchoredPosition, sizeDelta, pivot, 1, oldPivot.y, pivot.vector2Value.y);
        }
    }

    private static Vector2 GetOffsetMin(SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot) =>
        anchoredPosition.vector2Value - Vector2.Scale(sizeDelta.vector2Value, pivot.vector2Value);

    private static Vector2 GetOffsetMax(SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot) =>
        anchoredPosition.vector2Value + Vector2.Scale(sizeDelta.vector2Value, Vector2.one - pivot.vector2Value);

    private static void SetOffsetMin(SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot, Vector2 value)
    {
        var offset = value - GetOffsetMin(anchoredPosition, sizeDelta, pivot);
        sizeDelta.vector2Value -= offset;
        anchoredPosition.vector2Value += Vector2.Scale(offset, Vector2.one - pivot.vector2Value);
    }

    private static void SetOffsetMax(SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot, Vector2 value)
    {
        var offset = value - GetOffsetMax(anchoredPosition, sizeDelta, pivot);
        sizeDelta.vector2Value += offset;
        anchoredPosition.vector2Value += Vector2.Scale(offset, pivot.vector2Value);
    }

    private static void SetAnchorSmart(SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot, Vector2 parentSize, int axis, bool isMax, float oldValue, float newValue)
    {
        var offsetSizePixels = (newValue - oldValue) * parentSize[axis];
        var offsetPositionPixels = offsetSizePixels * (isMax ? pivot.vector2Value[axis] : 1f - pivot.vector2Value[axis]);
        var pos = anchoredPosition.vector2Value;
        pos[axis] -= offsetPositionPixels;
        anchoredPosition.vector2Value = pos;
        var size = sizeDelta.vector2Value;
        size[axis] += offsetSizePixels * (isMax ? -1 : 1);
        sizeDelta.vector2Value = size;
    }

    private static void SetPivotSmart(SerializedProperty anchoredPosition, SerializedProperty sizeDelta, SerializedProperty pivot, int axis, float oldValue, float newValue)
    {
        var deltaPivot = newValue - oldValue;
        var deltaPosition = deltaPivot * sizeDelta.vector2Value[axis];
        var pos = anchoredPosition.vector2Value;
        pos[axis] += deltaPosition;
        anchoredPosition.vector2Value = pos;
    }
}
}
