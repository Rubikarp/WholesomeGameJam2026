using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace JeffGrawAssets.FlexibleUI
{
    [CustomEditor(typeof(FlexibleBlurFeature))]
    public class FlexibleBlurFeatureEditor : Editor
    {
        private static readonly GUIContent GlobalBlurPauseContent = new ("Pause (global)", "Ceases blur processing altogether (existing blurred areas will remain. When a blur layer is added, blurs will process again during that frame)");
        private static readonly GUIContent BlurTypesHandledContent = new ("Blur Types Handled", $"Which types of blur the feature handles. If you want different Render Pass Events for {nameof(UIBlur)}s and blurred Images, add separate FlexibleBlurFeatures.");
        private static readonly GUIContent RenderPassEventContent = new ("Render Pass Event", "What stage of the rendering pipeline blurs are drawn in. Incorrect settings can cause blowout.");
        private static readonly GUIContent UIBlurLayersSeeLowerContent = new ("UIBlur Layers See Lower", $"Higher {nameof(UIBlur)} layers blur results of lower layers. Costs one blit per layer.");
        private static readonly GUIContent blurredImagesSeeUIBlursContent = new ("Blurred Images See UIBlurs", $"Blurred Images will blur the results of {nameof(UIBlur)}s.");
        private static readonly GUIContent blurredImageLayersSeeLowerContent = new ("Blurred Images See Lower", "Higher blurred Image layers blur results of lower layers. Costs one blit and texture per layer.");
        private static readonly GUIContent UseComputeShadersContent = new ("Use Compute Shaders", "Use compute instead of fragment shaders for blur computation. Compute shaders save draw calls and are likely faster on most platforms, but it is recommended to test. Does not affect blits.");
        private static readonly GUIContent ResultFormatContent = new ("Result Format", "Color format for the final texture. Generally don't change unless needed.");
        private static readonly GUIContent BlurFormatContent = new ("Blur Format", "Format for blur computation. Lower precision can be faster, but may introduce banding when differences in the source texture are subtle.");
        private static readonly GUIContent Format32BitContent = new ("32-bit", "Set both formats to 32-bit. Most performant.");
        private static readonly GUIContent Format32BitResult64BitBlurContent = new ("32/64-bit", "Sets the result format to 32-bit and blur format to 64-bit. Marginal quality improvement.");
        private static readonly GUIContent LayerResolutionRatioContent = new ("Layer Resolution Ratio", "The size of blur layer result textures relative to camera resolution. For example, at 2160p camera resolution and 0.5 ratio, the layer resolution will be 1080p. ");
        private static readonly GUIContent MaxLayerResolutionContent = new ("Max Layer Resolution", "The maximum resolution of blur layer result textures. Regardless of ratio, layer textures will not exceed this value.");
        private static readonly GUIContent OverlayCanvasCompatibilityFixContent = new ("Overlay Compatibility Fix", "Uses a less optimized blit method for overlay canvases. Leave this disabled unless you notice issues.");
        private static readonly GUIContent MaxStabilizationPixelsContent = new ("Max Stabilization Pixels", "Maximum processing-space pixels added per edge to stabilize moving image-based blur regions. 0 disables stabilization. Does not affect UIBlur.");

        private static readonly string ApplyToAllButtonText = "Apply To All";
        private static readonly string PlatformSettingsText = "Platform Settings";

        private SerializedProperty renderPassEventProperty;
        private SerializedProperty destinationFilterModeProperty;
        private SerializedProperty uiBlurLayersSeeLowerProperty;
        private SerializedProperty blurredImagesSeeUIBlursProperty;
        private SerializedProperty blurredImageLayersSeeLowerProperty;
        private SerializedProperty useComputeShadersProperty;
        private SerializedProperty resultFormatProperty;
        private SerializedProperty blurFormatProperty;
        private SerializedProperty layerResolutionRatioProperty;
        private SerializedProperty maxLayerResolutionProperty;
        private SerializedProperty platformDataProperty;
        private SerializedProperty overlayCompatibilityFixProperty;
        private SerializedProperty maxStabilizationPixelsProperty;

        private Dictionary<string, (bool useComputeShaders, GraphicsFormat resultFormat, GraphicsFormat blurFormat, float layerResolutionRatio, int maxLayerResolution)> platformDataDict;
        private string[] platformNames;
        private int platformIdx;

        private void OnEnable()
        {
            renderPassEventProperty = serializedObject.FindProperty(FlexibleBlurFeature.RenderPassEventFieldName);
            destinationFilterModeProperty = serializedObject.FindProperty(FlexibleBlurFeature.DestinationFilterModeFieldName);
            uiBlurLayersSeeLowerProperty = serializedObject.FindProperty(FlexibleBlurFeature.UIBlurLayersSeeLowerFieldName);
            blurredImagesSeeUIBlursProperty = serializedObject.FindProperty(FlexibleBlurFeature.BlurredImagesSeeUIBlursFieldName);
            blurredImageLayersSeeLowerProperty = serializedObject.FindProperty(FlexibleBlurFeature.BlurredImageLayersSeeLowerFieldName);
            useComputeShadersProperty = serializedObject.FindProperty(FlexibleBlurFeature.UseComputeShadersFieldName);
            resultFormatProperty = serializedObject.FindProperty(FlexibleBlurFeature.ResultFormatFieldName);
            blurFormatProperty = serializedObject.FindProperty(FlexibleBlurFeature.BlurFormatFieldName);
            layerResolutionRatioProperty = serializedObject.FindProperty(FlexibleBlurFeature.LayerResolutionRatioFieldName);
            maxLayerResolutionProperty = serializedObject.FindProperty(FlexibleBlurFeature.MaxLayerResolutionFieldName);
            platformDataProperty = serializedObject.FindProperty(FlexibleBlurFeature.PlatformDataFieldName);
            overlayCompatibilityFixProperty = serializedObject.FindProperty(FlexibleBlurFeature.OverlayCompatibilityFixFieldName);
            maxStabilizationPixelsProperty = serializedObject.FindProperty(FlexibleBlurFeature.MaxStabilizationPixelsFieldName);
            platformDataDict = FlexibleBlurFeature.DecodePlatformData(platformDataProperty.stringValue);

            var platformNamesList = new List<string>();
            foreach (BuildTarget buildTarget in Enum.GetValues(typeof(BuildTarget)))
            {
                var group = BuildPipeline.GetBuildTargetGroup(buildTarget);
                if (group != BuildTargetGroup.Unknown && BuildPipeline.IsBuildTargetSupported(group, buildTarget))
                    platformNamesList.Add(BuildPipeline.GetBuildTargetName(buildTarget));
            }

            platformNames = new string[platformNamesList.Count];
            platformNamesList.CopyTo(platformNames);
            var selectedBuildTarget = BuildPipeline.GetBuildTargetName(GetEditorBuildTarget());
            platformIdx = Array.IndexOf(platformNames, selectedBuildTarget);

            var defaultResultFormat = FlexibleBlurFeature.VerifyResultFormat(PlayerSettings.colorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, true);
            var defaultBlurFormat = FlexibleBlurFeature.VerifyResultFormat(PlayerSettings.colorSpace == ColorSpace.Linear ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm, true);
            foreach (var key in platformNames)
            {
                if (!platformDataDict.ContainsKey(key))
                    platformDataDict[key] = (false, defaultResultFormat, defaultBlurFormat, 1f, 1080);
            }

            var currentPlatformData = platformDataDict[selectedBuildTarget];
            useComputeShadersProperty.boolValue = currentPlatformData.useComputeShaders;
            resultFormatProperty.enumValueIndex = (int)currentPlatformData.resultFormat;
            blurFormatProperty.enumValueIndex = (int)currentPlatformData.blurFormat;
            layerResolutionRatioProperty.floatValue = currentPlatformData.layerResolutionRatio;
            maxLayerResolutionProperty.intValue = currentPlatformData.maxLayerResolution;
            platformDataProperty.stringValue = FlexibleBlurFeature.EncodePlatformData(platformDataDict);
            SceneView.RepaintAll();
        }

        public void OnDisable()
        {
            var feature = (FlexibleBlurFeature)target;
            if (feature?.platformData == null || feature.platformData.Length == 0)
                return;

            feature.UsePlatformSettings(GetEditorBuildTarget());
            feature.Dispose();
            feature.Create();
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            FlexibleBlurFeature.GloballyPaused = EditorGUILayout.Toggle(GlobalBlurPauseContent, FlexibleBlurFeature.GloballyPaused);

            var originalLabelWidth = EditorGUIUtility.labelWidth;
            var modifiedLabelWidth = originalLabelWidth;
            if (originalLabelWidth < 160)
                modifiedLabelWidth = Mathf.Min(160, Mathf.Max(120, EditorGUIUtility.currentViewWidth - 55));

            EditorGUILayout.PropertyField(renderPassEventProperty, RenderPassEventContent);
            EditorGUILayout.PropertyField(destinationFilterModeProperty);

            EditorGUIUtility.labelWidth = modifiedLabelWidth;
            EditorGUILayout.PropertyField(overlayCompatibilityFixProperty, OverlayCanvasCompatibilityFixContent);
            EditorGUILayout.PropertyField(uiBlurLayersSeeLowerProperty, UIBlurLayersSeeLowerContent);
            EditorGUILayout.PropertyField(blurredImagesSeeUIBlursProperty, blurredImagesSeeUIBlursContent);
            EditorGUILayout.PropertyField(blurredImageLayersSeeLowerProperty, blurredImageLayersSeeLowerContent);
            EditorGUILayout.PropertyField(maxStabilizationPixelsProperty, MaxStabilizationPixelsContent);
            maxStabilizationPixelsProperty.intValue = Mathf.Max(0, maxStabilizationPixelsProperty.intValue);
            EditorGUIUtility.labelWidth = originalLabelWidth;

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField(PlatformSettingsText);

            var prevPlatformIdx = platformIdx;

            EditorGUILayout.BeginHorizontal();
            platformIdx = EditorGUILayout.Popup(platformIdx, platformNames);

            var selectedBuildTarget = platformNames[platformIdx];
            var currentPlatformData = platformDataDict[selectedBuildTarget];

            if (GUILayout.Button(ApplyToAllButtonText))
            {
                foreach (var key in platformDataDict.Keys.ToList())
                    platformDataDict[key] = currentPlatformData;
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginVertical(GUI.skin.box);

            modifiedLabelWidth = originalLabelWidth;
            if (originalLabelWidth < 135)
                modifiedLabelWidth = Mathf.Min(135, Mathf.Max(120, EditorGUIUtility.currentViewWidth - 55));

            EditorGUIUtility.labelWidth = modifiedLabelWidth;
            useComputeShadersProperty.boolValue = currentPlatformData.useComputeShaders;
            EditorGUILayout.PropertyField(useComputeShadersProperty, UseComputeShadersContent);
            var useComputeShadersValue = useComputeShadersProperty.boolValue;
            EditorGUIUtility.labelWidth = originalLabelWidth;

            layerResolutionRatioProperty.floatValue = currentPlatformData.layerResolutionRatio;
            EditorGUILayout.PropertyField(layerResolutionRatioProperty, LayerResolutionRatioContent);
            layerResolutionRatioProperty.floatValue = Mathf.Clamp(layerResolutionRatioProperty.floatValue, 0.1f, 1.0f);
            var layerResolutionRatioValue = layerResolutionRatioProperty.floatValue;

            maxLayerResolutionProperty.intValue = currentPlatformData.maxLayerResolution;
            EditorGUILayout.PropertyField(maxLayerResolutionProperty, MaxLayerResolutionContent);
            maxLayerResolutionProperty.intValue = Mathf.Max(maxLayerResolutionProperty.intValue, 64);
            var maxLayerResolutionValue = maxLayerResolutionProperty.intValue;

            var resultFormatValue = (GraphicsFormat)EditorGUILayout.EnumPopup(ResultFormatContent, currentPlatformData.resultFormat);
            if (FlexibleBlurFeature.ResultFormatFallbackDict.TryGetValue(resultFormatValue, out var fallbackResultValue) && fallbackResultValue != resultFormatValue)
                EditorGUILayout.HelpBox($"Result format {resultFormatValue} is not supported in your PC/Editor configuration, but will be attempted in builds. Falling back to {fallbackResultValue}", MessageType.Warning);
            
            var blurFormatValue = (GraphicsFormat)EditorGUILayout.EnumPopup(BlurFormatContent, currentPlatformData.blurFormat);
            if (FlexibleBlurFeature.BlurFormatFallbackDict.TryGetValue(blurFormatValue, out var fallbackBlurValue) && fallbackBlurValue != blurFormatValue)
                EditorGUILayout.HelpBox($"Blur format {blurFormatValue} is not supported in your PC/Editor configuration, but will be attempted in builds. Falling back to {fallbackBlurValue}", MessageType.Warning);

            var defaultResultFormat = FlexibleBlurFeature.VerifyResultFormat(PlayerSettings.colorSpace == ColorSpace.Linear ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm, true);
            var defaultBlurFormat = FlexibleBlurFeature.VerifyResultFormat(PlayerSettings.colorSpace == ColorSpace.Linear ? GraphicsFormat.B10G11R11_UFloatPack32 : GraphicsFormat.R8G8B8A8_UNorm, true);

            var isSetTo32Bit = resultFormatValue == defaultResultFormat && blurFormatValue == defaultBlurFormat;
            var isSetTo32BitAnd64Bit = resultFormatValue == defaultResultFormat && blurFormatValue == GraphicsFormat.R16G16B16A16_SFloat;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.Space(0, true);

            EditorGUI.BeginDisabledGroup(isSetTo32Bit);
            if (GUILayout.Button(Format32BitContent))
            {
                resultFormatValue = defaultResultFormat;
                blurFormatValue = defaultBlurFormat;
            }

            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(isSetTo32BitAnd64Bit);
            if (GUILayout.Button(Format32BitResult64BitBlurContent))
            {
                resultFormatValue = defaultResultFormat;
                blurFormatValue = GraphicsFormat.R16G16B16A16_SFloat;
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(0, true);
            EditorGUILayout.EndHorizontal();

            var formatTuple = (useComputeShadersValue, resultFormatValue, blurFormatValue, layerResolutionRatioValue, maxLayerResolutionValue);
            if (currentPlatformData != formatTuple)
            {
                platformDataDict[selectedBuildTarget] = formatTuple;
                platformDataProperty.stringValue = FlexibleBlurFeature.EncodePlatformData(platformDataDict);
            }
            if (currentPlatformData != formatTuple || prevPlatformIdx != platformIdx)
            {
                useComputeShadersProperty.boolValue = useComputeShadersValue;
                resultFormatProperty.enumValueFlag = (int)resultFormatValue;
                blurFormatProperty.enumValueFlag = (int)blurFormatValue;
                layerResolutionRatioProperty.floatValue = layerResolutionRatioValue;
                maxLayerResolutionProperty.intValue = maxLayerResolutionValue;
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private BuildTarget GetEditorBuildTarget()
        {
            const BuildTarget ret =
#if UNITY_EDITOR_WIN
                BuildTarget.StandaloneWindows64;
#elif UNITY_EDITOR_OSX
                BuildTarget.StandaloneOSX;
#elif UNITY_EDITOR_LINUX
                BuildTarget.StandaloneLinux64;
#endif
            var retName = BuildPipeline.GetBuildTargetName(ret);
            return Array.IndexOf(platformNames, retName) != -1 ? ret : EditorUserBuildSettings.activeBuildTarget;
        }
    }
}
