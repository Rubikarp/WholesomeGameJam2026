using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    internal abstract class UniTextNamedCatalogEditor : NamedCatalogEditor
    {
        /// <inheritdoc/>
        protected override VisualElement CreateRoot() => UniTextInspectorTheme.CreateRoot();
    }

    [CustomEditor(typeof(UniTextPaints))]
    [CanEditMultipleObjects]
    internal sealed class UniTextPaintsEditor : UniTextNamedCatalogEditor
    {
    }

    [CustomEditor(typeof(UniTextObjects))]
    [CanEditMultipleObjects]
    internal sealed class UniTextObjectsEditor : UniTextNamedCatalogEditor
    {
    }

    [CustomEditor(typeof(UniTextSprites))]
    [CanEditMultipleObjects]
    internal sealed class UniTextSpritesEditor : UniTextNamedCatalogEditor
    {
    }
}
