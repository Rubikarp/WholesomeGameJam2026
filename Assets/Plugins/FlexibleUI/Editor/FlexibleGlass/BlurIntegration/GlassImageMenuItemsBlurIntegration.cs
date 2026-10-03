using UnityEditor;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
public static partial class GlassImageMenuItems
{
    [MenuItem("CONTEXT/BlurredImage/Convert to GlassImage", false, 1)]
    private static void FromBlurredImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as BlurredImage;
        if (!source)
            return;

        var go = source.gameObject;
        var image = new ImageSnapshot(source);
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<GlassImage>(go);
        image.Apply(target);
        target.shapeType = GlassImageShapeType.Sprite;
        Finish(target);
    }

    [MenuItem("CONTEXT/GlassImage/Convert to BlurredImage", false, 1)]
    private static void ToBlurredImage(MenuCommand menuCommand)
    {
        var source = menuCommand.context as GlassImage;
        if (!source)
            return;

        var go = source.gameObject;
        var image = new ImageSnapshot(source);
        Undo.DestroyObjectImmediate(source);
        var target = Undo.AddComponent<BlurredImage>(go);
        image.Apply(target);
        target.Common.ValidateBlur();
        FlexibleBlurMenuItems.TrySetBlurCamera(target.Common, go);
        _ = target.materialForRendering;
        HandleMask(go);
    }
}
}
