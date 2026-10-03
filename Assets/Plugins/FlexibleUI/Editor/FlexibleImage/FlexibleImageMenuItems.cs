using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleImageMenuItems : MonoBehaviour
{
#if UNITY_6000_3_OR_NEWER
    [MenuItem("GameObject/UI (Canvas)/Flexible Image", false, 1)]
#else
    [MenuItem("GameObject/UI/Flexible Image", false, 1)]
#endif
    private static void CreateFlexibleImage(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("FlexibleImage");
        PlaceUIElementRoot(go, menuCommand);
        go.AddComponent<RectTransform>();
        var fi = go.AddComponent<FlexibleImage>();
        CreateFlexibleImageBlur(fi);
    }

    static partial void CreateFlexibleImageBlur(FlexibleImage fi);

    [MenuItem("CONTEXT/FlexibleImage/Convert to Image", false, 99)]
    private static void FromFlexibleImageToImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as FlexibleImage;
        var go = source.gameObject;

        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<Image>(go);
        MenuItemCommon.CopyImageProps(source, target);
        target.color = source.PrimaryProceduralProperties.primaryColors[0];
        HandleMask(go);
    }

    [MenuItem("CONTEXT/Image/Convert to FlexibleImage", false, 0)]
    private static void FromImageToFlexibleImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as Image;
        if (source is FlexibleImage)
            return;

        var go = source.gameObject;
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<FlexibleImage>(go);
        MenuItemCommon.CopyImageProps(source, target);
        target.PrimaryProceduralProperties.primaryColors[0] = source.color;
        _ = target.materialForRendering;
        CreateFlexibleImageBlur(target);
        HandleMask(go);
    }

    private static void PlaceUIElementRoot(GameObject element, MenuCommand menuCommand)
    {
        GameObject parent = menuCommand.context as GameObject;
        if (parent == null || parent.GetComponentInParent<Canvas>() == null)
        {
            parent = MenuItemCommon.GetOrCreateCanvasGameObject();
        }

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