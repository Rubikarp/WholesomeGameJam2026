using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace JeffGrawAssets.FlexibleUI
{
public partial class FlexibleImageMenuItems
{
    static partial void CreateFlexibleImageBlur(FlexibleImage fi) =>
        FlexibleBlurMenuItems.TrySetBlurCamera(fi.Common, fi.gameObject);

    [MenuItem("CONTEXT/FlexibleImage/Convert to UIBlur", false, 2)]
    private static void FromFlexibleImageToUIBlur(MenuCommand menuCommand)
    {
        var source = menuCommand.context as FlexibleImage;
        var go = source.gameObject;

        if (go.GetComponent<UIBlur>() != null)
        {
            Debug.LogError($"Cannot convert from {nameof(FlexibleImage)} to {nameof(UIBlur)}. {go.name} already contains {nameof(UIBlur)} component!");
            return;
        }

        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<UIBlur>(go);
        FlexibleBlurMenuItems.CopyBlurProps(source, target);
        FlexibleBlurMenuItems.TrySetBlurCamera(target.Common, go);
        target.Common.ValidateBlur();
        HandleMask(go);
    }

    [MenuItem("CONTEXT/UIBlur/Convert to FlexibleImage", false, 0)]
    private static void FromUIBlurToFlexibleImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as UIBlur;
        var go = source.gameObject;

        if (go.GetComponent<Image>() != null)
        {
            Debug.LogError($"Cannot convert from {nameof(UIBlur)} to {nameof(FlexibleImage)}. {go.name} already contains Image (or derived) component!");
            return;
        }

        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<FlexibleImage>(go);
        FlexibleBlurMenuItems.CopyBlurProps(source, target);
        FlexibleBlurMenuItems.TrySetBlurCamera(target.Common, go);
        target.BlurEnabled = true;
        target.Common.ValidateBlur();
        HandleMask(go);
    }
    
    [MenuItem("CONTEXT/FlexibleImage/Convert to BlurredImage", false, 0)]
    private static void FromFlexibleImageToBlurredImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as FlexibleImage;
        var go = source.gameObject;
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<BlurredImage>(go);
        MenuItemCommon.CopyImageProps(source, target);
        FlexibleBlurMenuItems.CopyBlurProps(source, target);
        FlexibleBlurMenuItems.TrySetBlurCamera(target.Common, go);
        target.color = source.PrimaryProceduralProperties.primaryColors[0];
        target.additionalBlurPadding = source.additionalBlurPadding;
        target.Common.ValidateBlur();
        HandleMask(go);
    }
    
    [MenuItem("CONTEXT/BlurredImage/Convert to FlexibleImage", false, 0)]
    private static void FromBlurredImageToFlexibleImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as BlurredImage;
        var go = source.gameObject;
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<FlexibleImage>(go);
        MenuItemCommon.CopyImageProps(source, target);
        FlexibleBlurMenuItems.CopyBlurProps(source, target);
        FlexibleBlurMenuItems.TrySetBlurCamera(target.Common, go);
        target.PrimaryProceduralProperties.primaryColors[0] = source.color;
        target.additionalBlurPadding = source.additionalBlurPadding;
        target.BlurEnabled = true;
        target.Common.ValidateBlur();
        HandleMask(go);
    }
}
}