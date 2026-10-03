using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
internal static class FlexibleGlassEditorUtility
{
    public static void RefreshPreview()
    {
        RepaintPreview();
        if (!Application.isPlaying)
        {
            RenderPipelineManager.endContextRendering -= FinishPreviewRefresh;
            RenderPipelineManager.endContextRendering += FinishPreviewRefresh;
        }
    }

    private static void FinishPreviewRefresh(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
    {
        RenderPipelineManager.endContextRendering -= FinishPreviewRefresh;
        // The first render republishes feature-owned materials; the next canvas render binds them.
        EditorApplication.delayCall -= RepaintPreview;
        EditorApplication.delayCall += RepaintPreview;
    }

    private static void RepaintPreview()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        InternalEditorUtility.RepaintAllViews();
    }

    public static readonly GUIContent CameraContent = new("Camera", "Camera whose color is captured behind this Glass group.");
    public static readonly GUIContent FeatureNumberContent = new("Feature #", $"Zero-based {nameof(FlexibleGlassFeature)} number on the selected renderer. Other renderer-feature types are not counted.");

    private static readonly GUIContent[] ReferenceOptions = { new("This Component"), new("Canvas Provider") };
    private static readonly GUIContent NormalizeCornersContent = new("Normalize", "Scales overlapping chamfers to fit within the element.");
    private static readonly GUIContent CornerChamferContent = new("Chamfer", "The size of the chamfer at each corner.");
    private static readonly GUIContent IsSquircleContent = new("Squircle", "Instead of concavity, corners can be smoothed to create squircles.");
    private static readonly GUIContent CornerConcavityContent = new("Concavity", "Whether the chamfer is convex/rounded (0), flat (1), or concave (2).");
    private static readonly GUIContent CornerSmoothingContent = new("Smoothing", "Squircle smoothing. 0 is circular, 0.4 is the recommended soft squircle, and 1 is squarer.");
    private static readonly GUIContent[] UIGlassShapeOptions =
    {
        new("Canonical", "A rounded rectangle with one size-independent corner radius and a shared corner curve."),
        new("Per Corner", "Flexible Image-style independent chamfer and corner-shape controls joined to straight edges."),
        new("Sprite", "Uses the Image sprite alpha as the glass silhouette without applying the procedural corner field.")
    };
    private static GUIStyle sectionStyle;

    private static GUIStyle SectionStyle => sectionStyle ??= new GUIStyle(EditorStyles.foldoutHeader)
    {
        fontStyle = FontStyle.Bold
    };

    public static bool DrawSectionHeader(string title, string sessionKey, bool defaultExpanded = true)
    {
        var expanded = SessionState.GetBool(sessionKey, defaultExpanded);
        var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
        expanded = EditorGUI.Foldout(rect, expanded, title, true, SectionStyle);
        SessionState.SetBool(sessionKey, expanded);
        return expanded;
    }

    public static bool DrawGlassSource(SerializedProperty referenceSourceProperty, SerializedProperty cameraProperty, SerializedProperty featureNumberProperty, GameObject go, string componentName)
    {
        var isPreviewScene = EditorSceneManager.IsPreviewScene(go.scene);
        var canvas = go.GetComponentInParent<Canvas>(true);
        if (!isPreviewScene && !canvas)
        {
            EditorGUILayout.HelpBox($"No parent Canvas detected. {componentName} only works inside uGUI.", MessageType.Error);
            return false;
        }

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Glass Source", EditorStyles.boldLabel);
        EditorGUI.showMixedValue = referenceSourceProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var referenceIndex = EditorGUILayout.Popup(new GUIContent("References", "Use this component's camera and feature number, or share them from the Canvas."), referenceSourceProperty.enumValueIndex, ReferenceOptions);
        if (EditorGUI.EndChangeCheck())
            referenceSourceProperty.enumValueIndex = referenceIndex;
        EditorGUI.showMixedValue = false;

        var useInstanceSettings = true;
        var actualCamera = cameraProperty.objectReferenceValue as Camera;
        var actualFeatureNumber = Mathf.Max(0, featureNumberProperty.intValue);
        if (!referenceSourceProperty.hasMultipleDifferentValues && referenceSourceProperty.enumValueIndex == (int)GlassReferenceSource.ReferenceProvider)
        {
            var provider = canvas ? canvas.GetComponent<GlassReferenceProvider>() : null;
            if (provider && provider.CameraReference)
            {
                useInstanceSettings = false;
                actualCamera = provider.CameraReference;
                actualFeatureNumber = provider.FeatureNumber;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(CameraContent, provider.CameraReference, typeof(Camera), true);
                    EditorGUILayout.IntField(FeatureNumberContent, provider.FeatureNumber);
                }
                if (GUILayout.Button("Edit Canvas Provider"))
                    Selection.activeObject = provider;
            }
            else
            {
                var message = provider
                    ? $"{nameof(GlassReferenceProvider)} has no Camera. Falling back to this component's source."
                    : $"No {nameof(GlassReferenceProvider)} is present on this Canvas. Falling back to this component's source.";
                EditorGUILayout.HelpBox(message, MessageType.Warning);
                EditorGUILayout.BeginHorizontal();
                if (!provider && canvas && GUILayout.Button("Add Canvas Provider"))
                {
                    provider = Undo.AddComponent<GlassReferenceProvider>(canvas.gameObject);
                    provider.CameraReference = actualCamera ? actualCamera : Camera.main;
                    provider.FeatureNumber = actualFeatureNumber;
                    Selection.activeObject = provider;
                }
                else if (provider && GUILayout.Button("Edit Canvas Provider"))
                {
                    Selection.activeObject = provider;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        if (useInstanceSettings)
        {
            EditorGUILayout.PropertyField(cameraProperty, CameraContent);
            actualCamera = cameraProperty.objectReferenceValue as Camera;
            if (!actualCamera && Camera.main && GUILayout.Button("Assign Camera.main"))
            {
                cameraProperty.objectReferenceValue = Camera.main;
                actualCamera = Camera.main;
            }

            var featureCount = GetFeatureCount(actualCamera ? actualCamera : Camera.main, out _);
            if (featureCount > 1 || featureNumberProperty.intValue != 0)
                EditorGUILayout.PropertyField(featureNumberProperty, FeatureNumberContent);
            featureNumberProperty.intValue = actualFeatureNumber = Mathf.Max(0, featureNumberProperty.intValue);
        }

        DrawDiagnostics(actualCamera, actualFeatureNumber);
        EditorGUILayout.EndVertical();
        return true;
    }

    public static void DrawGlassImageShape(SerializedProperty shapeTypeProperty, SerializedProperty shapeProperty, SerializedProperty exponentProperty, SerializedProperty radiusProperty, SerializedProperty spriteProperty, SerializedProperty alphaThresholdProperty, string sessionKey)
    {
        if (!DrawSectionHeader("Glass Shape", sessionKey))
            return;

        EditorGUILayout.BeginVertical(GUI.skin.box);
        var mixedShape = shapeTypeProperty.hasMultipleDifferentValues;
        EditorGUI.showMixedValue = mixedShape;
        EditorGUI.BeginChangeCheck();
        var shapeType = GUILayout.Toolbar(mixedShape ? -1 : shapeTypeProperty.enumValueIndex, UIGlassShapeOptions);
        if (EditorGUI.EndChangeCheck() && shapeType >= 0)
        {
            shapeTypeProperty.enumValueIndex = shapeType;
            mixedShape = false;
        }
        EditorGUI.showMixedValue = false;

        if (!mixedShape && shapeTypeProperty.enumValueIndex == (int)GlassImageShapeType.Canonical)
        {
            DrawCanonicalShapeControls(radiusProperty, exponentProperty);
        }
        else if (!mixedShape && shapeTypeProperty.enumValueIndex == (int)GlassImageShapeType.PerCorner)
        {
            DrawCornerShapeControls(shapeProperty);
        }
        else if (!mixedShape)
        {
            EditorGUILayout.PropertyField(spriteProperty, new GUIContent("Shape Sprite", "Only the Sprite alpha channel defines the glass silhouette; its color is ignored."));
            EditorGUILayout.PropertyField(alphaThresholdProperty, new GUIContent("Alpha Threshold", "Sprite alpha value treated as the retained field perimeter."));
        }
        EditorGUILayout.EndVertical();
    }

    private static void DrawCanonicalShapeControls(SerializedProperty radiusProperty, SerializedProperty exponentProperty)
    {
        EditorGUILayout.PropertyField(radiusProperty, new GUIContent("Corner Radius", radiusProperty.tooltip));
        if (!radiusProperty.hasMultipleDifferentValues)
            radiusProperty.floatValue = Mathf.Max(0f, radiusProperty.floatValue);
        EditorGUI.showMixedValue = exponentProperty.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var roundness = EditorGUILayout.Slider(new GUIContent("Roundness", "Corner curve within the requested radius. 1 gives circular corners; lower values flatten the transition into the straight sides."), Mathf.InverseLerp(16f, 2f, exponentProperty.floatValue), 0f, 1f);
        if (EditorGUI.EndChangeCheck())
            exponentProperty.floatValue = Mathf.Lerp(16f, 2f, roundness);
        EditorGUI.showMixedValue = false;
    }

    private static void DrawCornerShapeControls(SerializedProperty shapeProperty)
    {
        var normalizeProperty = shapeProperty.FindPropertyRelative(nameof(GlassShapeSettings.normalizeCorners));
        var squircleProperty = shapeProperty.FindPropertyRelative(nameof(GlassShapeSettings.squircle));
        var radiiProperty = shapeProperty.FindPropertyRelative(nameof(GlassShapeSettings.cornerRadii));
        var roundnessProperty = shapeProperty.FindPropertyRelative(nameof(GlassShapeSettings.cornerRoundness));

        EditorGUILayout.PropertyField(normalizeProperty, NormalizeCornersContent);
        DrawCornersSection(radiiProperty, CornerChamferContent, 0f, float.PositiveInfinity, 5f, 0.5f);

        var mixedSquircle = squircleProperty.hasMultipleDifferentValues;
        var wasSquircle = squircleProperty.boolValue;
        EditorGUI.showMixedValue = mixedSquircle;
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(squircleProperty, IsSquircleContent);
        if (EditorGUI.EndChangeCheck() && !mixedSquircle && wasSquircle != squircleProperty.boolValue)
        {
            var values = Vector4.one - roundnessProperty.vector4Value;
            roundnessProperty.vector4Value = squircleProperty.boolValue
                ? Vector4.Min(Vector4.one, Vector4.Max(Vector4.zero, values))
                : Vector4.Min(Vector4.one, Vector4.Max(-Vector4.one, values));
        }
        EditorGUI.showMixedValue = false;
        DrawCornersSection(roundnessProperty, squircleProperty.boolValue ? CornerSmoothingContent : CornerConcavityContent, 0f, squircleProperty.boolValue ? 1f : 2f, 0.5f, 0.01f, !squircleProperty.boolValue);
    }

    public static void DrawUIGlassShape(SerializedProperty sdfSourceProperty, SerializedProperty shapeTypeProperty, SerializedProperty shapeProperty, SerializedProperty exponentProperty, SerializedProperty radiusProperty, SerializedProperty spriteProperty, SerializedProperty alphaThresholdProperty, string sessionKey)
    {
        if (!DrawSectionHeader("Glass Shape", sessionKey))
            return;

        EditorGUILayout.BeginVertical(GUI.skin.box);
        var mixedShape = sdfSourceProperty.hasMultipleDifferentValues || shapeTypeProperty.hasMultipleDifferentValues;
        var sprite = !mixedShape && sdfSourceProperty.enumValueIndex == (int)GlassSdfSource.SpriteAlpha;
        var canonical = !mixedShape && !sprite && shapeTypeProperty.intValue == (int)GlassShapeType.Canonical;
        EditorGUI.showMixedValue = mixedShape;
        EditorGUI.BeginChangeCheck();
        var shapeType = GUILayout.Toolbar(mixedShape ? -1 : sprite ? 2 : canonical ? 0 : 1, UIGlassShapeOptions);
        if (EditorGUI.EndChangeCheck() && shapeType >= 0)
        {
            sdfSourceProperty.enumValueIndex = shapeType == 2 ? (int)GlassSdfSource.SpriteAlpha : (int)GlassSdfSource.Shape;
            if (shapeType != 2)
                shapeTypeProperty.intValue = shapeType == 0 ? (int)GlassShapeType.Canonical : (int)GlassShapeType.PerCorner;
            sprite = shapeType == 2;
            canonical = shapeType == 0;
            mixedShape = false;
        }
        EditorGUI.showMixedValue = false;

        if (canonical)
        {
            DrawCanonicalShapeControls(radiusProperty, exponentProperty);
        }
        else if (!mixedShape && !sprite)
            DrawCornerShapeControls(shapeProperty);
        else if (!mixedShape)
        {
            EditorGUILayout.PropertyField(spriteProperty, new GUIContent("Shape Sprite", "Only the Sprite alpha channel defines the glass silhouette; its color is ignored."));
            EditorGUILayout.PropertyField(alphaThresholdProperty, new GUIContent("Alpha Threshold", "Sprite alpha value treated as the retained field perimeter."));
        }
        EditorGUILayout.EndVertical();
    }

    public static void DrawAppearance(SerializedProperty appearanceProperty, string sessionKey, SerializedProperty refractionStrengthProperty, SerializedProperty refractiveIndexProperty, SerializedProperty abbeNumberProperty, SerializedProperty surfaceSmoothnessModeProperty = null, SerializedProperty surfaceSmoothnessProperty = null, bool automaticSurfaceSmoothnessSupported = false, SerializedProperty colorProperty = null, SerializedProperty depthFallbackProperty = null)
    {
        if (!DrawSectionHeader("Glass Appearance", sessionKey))
            return;

        EditorGUILayout.BeginVertical(GUI.skin.box);
        EditorGUILayout.LabelField("Surface", EditorStyles.boldLabel);
        DrawGlassColor(colorProperty ?? appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.color)));
        EditorGUILayout.PropertyField(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.colorMix)));
        EditorGUILayout.PropertyField(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.transmission)));
        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Optical Lip", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.magnification)));
        DrawPhysicalLensDepth(refractionStrengthProperty);
        refractionStrengthProperty.floatValue = Mathf.Clamp(refractionStrengthProperty.floatValue, 0f, UIGlass.MaxLensDepth);
        EditorGUILayout.PropertyField(refractiveIndexProperty, new GUIContent("Refractive Index", refractiveIndexProperty.tooltip));
        EditorGUILayout.PropertyField(abbeNumberProperty, new GUIContent("Abbe Number", abbeNumberProperty.tooltip));
        var thicknessUnitsProperty = appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.thicknessUnits));
        var opticalLipProperty = appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.thickness));
        EditorGUILayout.PropertyField(thicknessUnitsProperty, new GUIContent("Optical Lip Units", "Measure the lip in Canvas units or as a percentage of either element side. The resolved width is always clamped against both axes."));
        if (thicknessUnitsProperty.hasMultipleDifferentValues || thicknessUnitsProperty.enumValueIndex == (int)GlassThicknessUnits.AbsoluteCanvasUnits)
        {
            EditorGUILayout.PropertyField(opticalLipProperty);
            if (!opticalLipProperty.hasMultipleDifferentValues)
                opticalLipProperty.floatValue = Mathf.Max(0f, opticalLipProperty.floatValue);
        }
        else
        {
            EditorGUI.showMixedValue = opticalLipProperty.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            var thickness = EditorGUILayout.Slider(new GUIContent("Optical Lip (%)", "Percentage of the selected side. Values that would cross the center on either axis are clamped."), opticalLipProperty.floatValue, 0f, 50f);
            if (EditorGUI.EndChangeCheck())
                opticalLipProperty.floatValue = thickness;
            EditorGUI.showMixedValue = false;
        }
        if (surfaceSmoothnessProperty != null)
        {
            if (automaticSurfaceSmoothnessSupported && surfaceSmoothnessModeProperty != null)
            {
                EditorGUILayout.PropertyField(surfaceSmoothnessModeProperty, new GUIContent("Surface Smoothness", surfaceSmoothnessModeProperty.tooltip));
                if (surfaceSmoothnessModeProperty.hasMultipleDifferentValues || surfaceSmoothnessModeProperty.enumValueIndex == (int)GlassSurfaceSmoothnessMode.Custom)
                {
                    EditorGUILayout.PropertyField(surfaceSmoothnessProperty, new GUIContent("Smoothness", surfaceSmoothnessProperty.tooltip));
                }
                else if (TryGetResolvedSurfaceSmoothness(surfaceSmoothnessProperty.serializedObject.targetObjects, out var resolvedSmoothness, out var mixedSmoothness))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUI.showMixedValue = mixedSmoothness;
                        EditorGUILayout.FloatField(new GUIContent("Smoothness", "The value currently calculated by Auto."), resolvedSmoothness);
                        EditorGUI.showMixedValue = false;
                    }
                }
            }
            else
                EditorGUILayout.PropertyField(surfaceSmoothnessProperty, new GUIContent("Surface Smoothness", surfaceSmoothnessProperty.tooltip));
        }
        if (depthFallbackProperty != null)
            EditorGUILayout.PropertyField(depthFallbackProperty, new GUIContent("Depth Fallback", depthFallbackProperty.tooltip));
        using (new EditorGUI.DisabledScope(!opticalLipProperty.hasMultipleDifferentValues && opticalLipProperty.floatValue <= 0f))
        {
            var lipUnits = appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.lipLightUnits));
            EditorGUILayout.PropertyField(lipUnits, new GUIContent("Lip Light Units", "Units for both lip-light widths. Neither rendered band can exceed the Optical Lip."));
            using (new EditorGUI.DisabledScope(lipUnits.hasMultipleDifferentValues))
            {
                var percent = lipUnits.enumValueIndex == (int)GlassLipLightUnits.PercentOfOpticalLip;
                DrawLipLightWidth(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.innerEdgeLightThickness)), "Inner Lip Light", percent);
                DrawLipLightWidth(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.outerEdgeLightThickness)), "Outer Lip Light", percent);
            }
        }
        EditorGUILayout.Space(3f);
        EditorGUILayout.LabelField("Shadow", EditorStyles.boldLabel);
        var shadowColorProperty = appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.shadowColor));
        EditorGUILayout.PropertyField(shadowColorProperty);
        using (new EditorGUI.DisabledScope(!shadowColorProperty.hasMultipleDifferentValues && shadowColorProperty.colorValue.a <= 0f))
        {
            EditorGUILayout.PropertyField(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.shadowSize)));
            EditorGUILayout.PropertyField(appearanceProperty.FindPropertyRelative(nameof(GlassAppearance.shadowOffset)));
        }
        EditorGUILayout.EndVertical();
    }

    private static bool TryGetResolvedSurfaceSmoothness(Object[] targets, out float resolved, out bool mixed)
    {
        resolved = 0f;
        mixed = false;
        var hasValue = false;
        foreach (var target in targets)
        {
            float value;
            switch (target)
            {
                case UIGlass glass when glass.transform is RectTransform rect:
                    value = glass.GetResolvedSurfaceSmoothness(rect.rect.size);
                    break;
                case GlassImage image:
                    value = image.GetResolvedSurfaceSmoothness(image.rectTransform.rect.size);
                    break;
                default:
                    continue;
            }

            if (!hasValue)
            {
                resolved = value;
                hasValue = true;
            }
            else if (!Mathf.Approximately(resolved, value))
            {
                mixed = true;
            }
        }
        return hasValue;
    }

    private static void DrawLipLightWidth(SerializedProperty property, string label, bool percent)
    {
        var content = new GUIContent(label + (percent ? " (%)" : ""), percent
            ? "Width as a percentage of the Optical Lip. 0 disables the band; 100 spans the lip."
            : "Width in Canvas units. 0 disables the band; rendering clamps the width to the Optical Lip.");
        var rect = EditorGUILayout.GetControlRect();
        EditorGUI.BeginProperty(rect, content, property);
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var value = percent ? EditorGUI.Slider(rect, content, property.floatValue * 100f, 0f, 100f) : EditorGUI.FloatField(rect, content, property.floatValue);
        if (EditorGUI.EndChangeCheck())
            property.floatValue = percent ? value * 0.01f : Mathf.Max(0f, value);
        EditorGUI.showMixedValue = false;
        EditorGUI.EndProperty();
    }

    private static void DrawGlassColor(SerializedProperty property)
    {
        var value = property.colorValue;
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var rgb = EditorGUILayout.ColorField(new GUIContent("Color", "Glass color before Color Mix is applied."), value, true, false, false);
        if (EditorGUI.EndChangeCheck())
        {
            rgb.a = value.a;
            property.colorValue = rgb;
            value = rgb;
        }

        EditorGUI.BeginChangeCheck();
        var opacity = EditorGUILayout.Slider(new GUIContent("Opacity", "Conventional Graphic opacity. Canvas Group alpha is applied afterwards."), value.a, 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            value.a = opacity;
            property.colorValue = value;
        }
        EditorGUI.showMixedValue = false;
    }

    private static void DrawPhysicalLensDepth(SerializedProperty property)
    {
        var content = new GUIContent("Lens Depth", "Virtual travel depth relative to Optical Lip. 0 disables refraction, 1 is the normal physical range, and values above 1 can deliberately create optical folds.");
        var rect = EditorGUILayout.GetControlRect();
        var controlRect = EditorGUI.PrefixLabel(rect, content);
        var fieldWidth = Mathf.Min(52f, controlRect.width * 0.3f);
        var sliderRect = new Rect(controlRect.x, controlRect.y, controlRect.width - fieldWidth - 4f, controlRect.height);
        var fieldRect = new Rect(sliderRect.xMax + 4f, controlRect.y, fieldWidth, controlRect.height);
        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        var sliderValue = GUI.HorizontalSlider(sliderRect, Mathf.Clamp(property.floatValue, 0f, 4f), 0f, 4f);
        var sliderChanged = EditorGUI.EndChangeCheck();
        EditorGUI.BeginChangeCheck();
        var fieldValue = EditorGUI.FloatField(fieldRect, property.floatValue);
        var fieldChanged = EditorGUI.EndChangeCheck();
        EditorGUI.showMixedValue = false;
        if (fieldChanged)
            property.floatValue = Mathf.Clamp(fieldValue, 0f, UIGlass.MaxLensDepth);
        else if (sliderChanged)
            property.floatValue = sliderValue;
    }

    private static void DrawCornersSection(SerializedProperty property, GUIContent content, float minimum, float maximum, float quickStep, float scrubSensitivity, bool invert = false)
    {
        var value = invert ? Vector4.one - property.vector4Value : property.vector4Value;
        var controlRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight * 2f + 2f);
        var indent = EditorGUI.indentLevel * 15f;
        controlRect.x += indent;
        controlRect.width -= indent;

        var titleWidth = Mathf.Min(EditorGUIUtility.labelWidth - indent, controlRect.width * 0.45f);
        var titleRect = new Rect(controlRect.x, controlRect.y, titleWidth - 4f, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(titleRect, content);
        var scrubAdjustment = EditorHelpers.Scrub(titleRect) * scrubSensitivity;

        var quickRect = new Rect(titleRect.x, titleRect.y + EditorGUIUtility.singleLineHeight + 2f, Mathf.Min(80f, titleRect.width), EditorGUIUtility.singleLineHeight);
        var leftQuick = new Rect(quickRect.x, quickRect.y, quickRect.width * 0.5f, quickRect.height);
        var rightQuick = new Rect(leftQuick.xMax, quickRect.y, quickRect.width - leftQuick.width, quickRect.height);
        var adjustment = GUI.Button(leftQuick, $"-{quickStep:0.#}", EditorStyles.miniButtonLeft) ? -quickStep : 0f;
        if (GUI.Button(rightQuick, $"+{quickStep:0.#}", EditorStyles.miniButtonRight))
            adjustment = quickStep;

        var fieldsRect = new Rect(controlRect.x + titleWidth, controlRect.y, controlRect.width - titleWidth, controlRect.height);
        var fieldWidth = (fieldsRect.width - 4f) * 0.5f;
        var topLeft = new Rect(fieldsRect.x, fieldsRect.y, fieldWidth, EditorGUIUtility.singleLineHeight);
        var topRight = new Rect(fieldsRect.x + fieldWidth + 4f, fieldsRect.y, fieldWidth, EditorGUIUtility.singleLineHeight);
        var bottomLeft = new Rect(topLeft.x, topLeft.y + EditorGUIUtility.singleLineHeight + 2f, fieldWidth, EditorGUIUtility.singleLineHeight);
        var bottomRight = new Rect(topRight.x, bottomLeft.y, fieldWidth, EditorGUIUtility.singleLineHeight);

        if (!Mathf.Approximately(adjustment, 0f))
            value = ClampCorners(value + Vector4.one * adjustment, minimum, maximum);
        if (!Mathf.Approximately(scrubAdjustment, 0f))
            value = ClampCorners(value + Vector4.one * scrubAdjustment, minimum, maximum);

        EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        value.x = DrawScrubbableFloatField(topLeft, new GUIContent("NW", "Top Left"), value.x, scrubSensitivity, minimum, maximum);
        value.y = DrawScrubbableFloatField(topRight, new GUIContent("NE", "Top Right"), value.y, scrubSensitivity, minimum, maximum);
        value.z = DrawScrubbableFloatField(bottomLeft, new GUIContent("SW", "Bottom Left"), value.z, scrubSensitivity, minimum, maximum);
        value.w = DrawScrubbableFloatField(bottomRight, new GUIContent("SE", "Bottom Right"), value.w, scrubSensitivity, minimum, maximum);
        var changed = EditorGUI.EndChangeCheck() || !Mathf.Approximately(adjustment, 0f) || !Mathf.Approximately(scrubAdjustment, 0f);
        EditorGUI.showMixedValue = false;
        if (changed)
        {
            value = ClampCorners(value, minimum, maximum);
            property.vector4Value = invert ? Vector4.one - value : value;
        }
    }

    private static float DrawScrubbableFloatField(Rect rect, GUIContent content, float value, float sensitivity, float minimum, float maximum)
    {
        var labelRect = new Rect(rect.x, rect.y, Mathf.Min(24f, rect.width), rect.height);
        var fieldRect = new Rect(labelRect.xMax, rect.y, rect.width - labelRect.width, rect.height);
        EditorGUI.LabelField(labelRect, content);
        var scrubAdjustment = EditorHelpers.Scrub(labelRect) * sensitivity;
        if (!Mathf.Approximately(scrubAdjustment, 0f))
        {
            value += scrubAdjustment;
            GUI.changed = true;
        }
        return Mathf.Clamp(EditorGUI.FloatField(fieldRect, value), minimum, maximum);
    }

    private static Vector4 ClampCorners(Vector4 value, float minimum, float maximum) => new(
        Mathf.Clamp(value.x, minimum, maximum),
        Mathf.Clamp(value.y, minimum, maximum),
        Mathf.Clamp(value.z, minimum, maximum),
        Mathf.Clamp(value.w, minimum, maximum));

    public static int GetFeatureCount(Camera camera, out ScriptableRendererData rendererData)
    {
        rendererData = null;
        if (!camera)
            return 0;

        var cameraData = camera.GetUniversalAdditionalCameraData();
        var rendererIndexField = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        if (rendererIndexField?.GetValue(cameraData) is not int rendererIndex)
            return 0;

        var pipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (!pipelineAsset)
            pipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (!pipelineAsset)
            return 0;

        if (rendererIndex == -1)
        {
            var defaultRendererField = typeof(UniversalRenderPipelineAsset).GetField("m_DefaultRendererIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            if (defaultRendererField?.GetValue(pipelineAsset) is not int defaultRendererIndex)
                return 0;
            rendererIndex = defaultRendererIndex;
        }

#if UNITY_2023_2_OR_NEWER
        if (rendererIndex < 0 || rendererIndex >= pipelineAsset.rendererDataList.Length)
            return 0;
        rendererData = pipelineAsset.rendererDataList[rendererIndex];
#else
        var rendererDataListField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (rendererDataListField?.GetValue(pipelineAsset) is not ScriptableRendererData[] rendererDataList || rendererIndex < 0 || rendererIndex >= rendererDataList.Length)
            return 0;
        rendererData = rendererDataList[rendererIndex];
#endif
        if (!rendererData)
            return 0;

        var count = 0;
        foreach (var feature in rendererData.rendererFeatures)
            if (feature is FlexibleGlassFeature)
                count++;
        return count;
    }

    public static int GetFeatureNumber(FlexibleGlassFeature target, out ScriptableRendererData rendererData)
    {
        rendererData = null;
        var pipelineAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (!pipelineAsset)
            pipelineAsset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (!pipelineAsset)
            return -1;

#if UNITY_2023_2_OR_NEWER
        var rendererDataList = pipelineAsset.rendererDataList;
#else
        var rendererDataListField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (rendererDataListField?.GetValue(pipelineAsset) is not ScriptableRendererData[] rendererDataList)
            return -1;
#endif
        foreach (var candidate in rendererDataList)
        {
            if (!candidate)
                continue;
            var featureNumber = 0;
            foreach (var feature in candidate.rendererFeatures)
            {
                if (feature == target)
                {
                    rendererData = candidate;
                    return featureNumber;
                }
                if (feature is FlexibleGlassFeature)
                    featureNumber++;
            }
        }
        return -1;
    }

    public static void DrawDiagnostics(Camera camera, int featureNumber)
    {
        if (!camera)
        {
            EditorGUILayout.HelpBox(Camera.main ? "No Camera reference supplied. Using Camera.main." : "No Camera reference supplied.", Camera.main ? MessageType.Warning : MessageType.Error);
            camera = Camera.main;
        }
        if (!camera)
            return;

        var count = GetFeatureCount(camera, out var rendererData);
        if (count == 0)
        {
            EditorGUILayout.HelpBox($"{camera.name} is missing {nameof(FlexibleGlassFeature)}!", MessageType.Error);
            if (rendererData && GUILayout.Button("Open Renderer?", GUILayout.Height(28)))
            {
                EditorGUIUtility.PingObject(rendererData);
                Selection.activeObject = rendererData;
            }
            EditorGUILayout.Space(24);
        }
        else if (featureNumber < 0 || featureNumber >= count)
        {
            EditorGUILayout.HelpBox($"Invalid feature #. {nameof(FlexibleGlassFeature)} count is {count}.", MessageType.Warning);
        }
        foreach (var settings in camera.GetComponents<FlexibleGlassCameraOverride>())
        {
            if (!settings.isActiveAndEnabled || settings.FeatureNumber != featureNumber || Selection.activeGameObject == camera.gameObject)
                continue;
            if (GUILayout.Button(new GUIContent("Edit Camera Override", "This camera overrides settings from the selected FlexibleGlassFeature.")))
                Selection.activeObject = camera.gameObject;
            break;
        }
    }
}
}
