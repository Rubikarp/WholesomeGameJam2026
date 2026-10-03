using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace JeffGrawAssets.FlexibleUI
{
public static class FlexibleGlassMenuItems
{
#if UNITY_6000_3_OR_NEWER
    [MenuItem("GameObject/UI (Canvas)/UI Glass", false, 4)]
#else
    [MenuItem("GameObject/UI/UI Glass", false, 4)]
#endif
    private static void CreateUIGlass(MenuCommand menuCommand)
    {
        var parent = menuCommand.context as GameObject;
        if (!parent || !parent.GetComponentInParent<Canvas>())
            parent = MenuItemCommon.GetOrCreateCanvasGameObject();

        var go = new GameObject(GameObjectUtility.GetUniqueNameForSibling(parent.transform, "UI Glass"), typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create " + go.name);
        Undo.SetTransformParent(go.transform, parent.transform, "Parent " + go.name);
        GameObjectUtility.SetParentAndAlign(go, parent);
        var rectTransform = (RectTransform)go.transform;
        rectTransform.sizeDelta = new Vector2(240f, 120f);
        var glass = Undo.AddComponent<UIGlass>(go);
        TrySetGlassCamera(glass, go);
        Selection.activeGameObject = go;
    }

    internal static void TrySetGlassCamera(UIGlass glass, GameObject go)
    {
        if (glass.cameraReference)
            return;

        var canvas = go.GetComponentInParent<Canvas>();
        if (!canvas)
            return;

        var provider = canvas.GetComponent<GlassReferenceProvider>();
        if (provider)
        {
            glass.referenceSource = GlassReferenceSource.ReferenceProvider;
            return;
        }

        if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.ScreenSpaceOverlay || !canvas.worldCamera)
        {
            glass.cameraReference = Camera.main;
            return;
        }

#if UNITY_6000_4_OR_NEWER
        foreach (var camera in Object.FindObjectsByType<Camera>())
#else
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
#endif
        {
            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData.renderType == CameraRenderType.Overlay)
                continue;

            var stackIndex = cameraData.cameraStack.IndexOf(canvas.worldCamera);
            if (stackIndex < 0)
                continue;

            glass.cameraReference = stackIndex == 0 ? camera : cameraData.cameraStack[stackIndex - 1];
            return;
        }

        glass.cameraReference = canvas.worldCamera;
    }

    internal static void TrySetGlassCamera(GlassImage glass, GameObject go)
    {
        if (glass.cameraReference)
            return;

        var canvas = go.GetComponentInParent<Canvas>();
        if (!canvas)
            return;

        var provider = canvas.GetComponent<GlassReferenceProvider>();
        if (provider)
        {
            glass.referenceSource = GlassReferenceSource.ReferenceProvider;
            return;
        }

        if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.ScreenSpaceOverlay || !canvas.worldCamera)
        {
            glass.cameraReference = Camera.main;
            return;
        }

#if UNITY_6000_4_OR_NEWER
        foreach (var camera in Object.FindObjectsByType<Camera>())
#else
        foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
#endif
        {
            var cameraData = camera.GetUniversalAdditionalCameraData();
            if (cameraData.renderType == CameraRenderType.Overlay)
                continue;

            var stackIndex = cameraData.cameraStack.IndexOf(canvas.worldCamera);
            if (stackIndex < 0)
                continue;

            glass.cameraReference = stackIndex == 0 ? camera : cameraData.cameraStack[stackIndex - 1];
            return;
        }

        glass.cameraReference = canvas.worldCamera;
    }
}
}
