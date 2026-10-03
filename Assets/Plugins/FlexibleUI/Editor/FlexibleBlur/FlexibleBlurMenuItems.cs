using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public class FlexibleBlurMenuItems : MonoBehaviour
{
#if UNITY_6000_3_OR_NEWER
    [MenuItem("GameObject/UI (Canvas)/Blurred Image", false, 2)]
#else
    [MenuItem("GameObject/UI/Blurred Image", false, 2)]
#endif
    private static void CreateBlurredImage(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("BlurredImage");
        PlaceUIElementRoot(go, menuCommand);
        go.AddComponent<RectTransform>();
        var blurredImage = go.AddComponent<BlurredImage>();
        TrySetBlurCamera(blurredImage.Common, go);
    } 

#if UNITY_6000_3_OR_NEWER
    [MenuItem("GameObject/UI (Canvas)/UIBlur", false, 3)]
#else
    [MenuItem("GameObject/UI/UIBlur", false, 3)]
#endif
    private static void CreateUIBlur(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("UIBlur");
        PlaceUIElementRoot(go, menuCommand);
        go.AddComponent<RectTransform>();
        var uiBlur = go.AddComponent<UIBlur>();
        TrySetBlurCamera(uiBlur.Common, go);
    }

    [MenuItem("CONTEXT/BlurredImage/Convert to Image", false, 99)]
    private static void FromBlurredImageToImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as BlurredImage;
        var go = source.gameObject;

        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<Image>(go);
        MenuItemCommon.CopyImageProps(source, target);
        HandleMask(go);
    }

    [MenuItem("CONTEXT/Image/Convert to BlurredImage", false, 1)]
    private static void FromImageToBlurredImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as Image;
        if (source is BlurredImage)
            return;

        var go = source.gameObject;
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<BlurredImage>(go);
        MenuItemCommon.CopyImageProps(source, target);
        target.Common.ValidateBlur();
        _ = target.materialForRendering;
        TrySetBlurCamera(target.Common, go);
        HandleMask(go);
    }

     [MenuItem("CONTEXT/BlurredImage/Convert to UIBlur", false, 2)]
     private static void FromBlurredImageToUIBlur(MenuCommand menuCommand)
     {
         var source = menuCommand.context as BlurredImage;
         var go = source.gameObject;

         if (go.GetComponent<UIBlur>() != null)
         {
             Debug.LogError($"Cannot convert from {nameof(BlurredImage)} to {nameof(UIBlur)}. {go.name} already contains {nameof(UIBlur)} component!");
             return;
         }

         Undo.DestroyObjectImmediate(source);
         var target = Undo.AddComponent<UIBlur>(go);
         CopyBlurProps(source, target);
         target.Common.ValidateBlur();
         TrySetBlurCamera(target.Common, go);
         HandleMask(go);
     }

     [MenuItem("CONTEXT/UIBlur/Convert to BurredImage", false, 1)]
     private static void FromUIBlurToBlurredImage(MenuCommand menuCommand)
     {
         var source = menuCommand.context as UIBlur;
         var go = source.gameObject;

         if (go.GetComponent<Image>() != null)
         {
             Debug.LogError($"Cannot convert from {nameof(UIBlur)} to {nameof(BlurredImage)}. {go.name} already contains Image (or derived) component!");
             return;
         }

         Undo.DestroyObjectImmediate(source);
         var target = Undo.AddComponent<BlurredImage>(go);
         CopyBlurProps(source, target);
         target.Common.ValidateBlur();
         TrySetBlurCamera(target.Common, go);
         HandleMask(go);
     }

     public static void TrySetBlurCamera(UIBlurCommon blur, GameObject go)
     {
         if (blur.cameraReference != null)
             return;

         var canvas = go.GetComponentInParent<Canvas>();
         if (canvas == null)
             return;

         var blurReferenceProvider = canvas.GetComponent<BlurReferenceProvider>();
         if (blurReferenceProvider != null)
             blur.blurReferencesFrom = UIBlurCommon.BlurReferencesFrom.ReferenceProvider;

         if (!canvas.isRootCanvas || canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.worldCamera == null)
             return;

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

             blur.cameraReference = idx == 0 ? camera : cameraStack[idx - 1];
             break;
         }
     }

     public static void CopyBlurProps(IBlur source, IBlur target)
     {
         var (sourceCommon, targetCommon) = (source.Common, target.Common);
         targetCommon.blurInstanceSettings.CopySettings(sourceCommon.blurInstanceSettings);
         targetCommon.blurPreset = sourceCommon.blurPreset;
         targetCommon.blurReferencesFrom = sourceCommon.blurReferencesFrom;
         if (targetCommon.cameraReference != null)
            targetCommon.cameraReference = sourceCommon.cameraReference;
         targetCommon.featureNumber = sourceCommon.featureNumber;
         targetCommon.unrankedLayer = sourceCommon.unrankedLayer;
         targetCommon.priority = sourceCommon.priority;
     }

     private static void PlaceUIElementRoot(GameObject element, MenuCommand menuCommand)
     {
         GameObject parent = menuCommand.context as GameObject;
         if (parent == null || parent.GetComponentInParent<Canvas>() == null)
             parent = MenuItemCommon.GetOrCreateCanvasGameObject();

         string uniqueName = GameObjectUtility.GetUniqueNameForSibling(parent.transform, element.name);
         element.name = uniqueName;
         Undo.RegisterCreatedObjectUndo(element, "Create " + element.name);
         Undo.SetTransformParent(element.transform, parent.transform, "Parent " + element.name);
         GameObjectUtility.SetParentAndAlign(element, parent);
         Selection.activeGameObject = element;
     }

     private static void HandleMask(GameObject go)
     {
         var mask = go.GetComponent<Mask>();
         if (mask == null)
             return;

         var (enabled, showGraphic) = (mask.enabled, mask.showMaskGraphic);
         DestroyImmediate(mask);
         mask = go.AddComponent<Mask>();
         (mask.enabled, mask.showMaskGraphic) = (enabled, showGraphic);
     }
}
}
