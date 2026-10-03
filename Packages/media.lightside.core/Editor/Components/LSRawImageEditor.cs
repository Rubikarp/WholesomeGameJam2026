#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// UI Toolkit inspector for <see cref="LSRawImage"/>, including texture sizing, inherited
    /// graphic fields, aspect preservation, and shared orientation controls.
    /// </summary>
    [CustomEditor(typeof(LSRawImage), true)]
    [CanEditMultipleObjects]
    public class LSRawImageEditor : RawImageEditor
    {
        private SerializedProperty texture;
        private SerializedProperty preserveAspectRatio;
        private SerializedProperty rotateId;
        private SerializedProperty flip;

        protected virtual bool HasEditableTexture => true;
        protected virtual bool HasEditableMaterial => true;

        /// <inheritdoc/>
        public sealed override bool UseDefaultMargins() => false;

        protected override void OnEnable()
        {
            base.OnEnable();
            texture = InspectorHelpers.RequireProperty(serializedObject, "m_Texture");
            preserveAspectRatio = InspectorHelpers.RequireProperty(serializedObject, "preserveAspectRatio");
            rotateId = InspectorHelpers.RequireProperty(serializedObject, "rotateId");
            flip = InspectorHelpers.RequireProperty(serializedObject, "flip");
        }

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            var root = InspectorVisuals.CreateRoot();
            var content = InspectorVisuals.CreateRailSection("Content");
            var textureField = SerializedPropertyField.Create(texture);
            var size = new Vector2IntField("Size");
            size.SetEnabled(false);
            var nativeSize = new UnityEngine.UIElements.Button(SetNativeSize)
            {
                text = "Set Native Size",
                tooltip = "Sets the size to match the content.",
            };

            void RefreshTexture()
            {
                var hasTexture = false;
                var hasNoTexture = false;
                var mixedSize = false;
                var firstSize = default(Vector2Int);
                for (var i = 0; i < targets.Length; i++)
                {
                    var candidate = ((LSRawImage)targets[i]).texture;
                    if (candidate == null)
                    {
                        hasNoTexture = true;
                        continue;
                    }
                    var candidateSize = new Vector2Int(candidate.width, candidate.height);
                    if (!hasTexture)
                    {
                        firstSize = candidateSize;
                    }
                    else if (candidateSize != firstSize)
                        mixedSize = true;
                    hasTexture = true;
                }
                var visible = hasTexture;
                InspectorMotion.SetExpanded(size, visible);
                InspectorMotion.SetExpanded(nativeSize, visible);
                if (visible)
                    size.SetValueWithoutNotify(firstSize);
                size.showMixedValue = mixedSize || hasTexture && hasNoTexture;
            }

            content.Add(textureField);
            content.Add(size);
            content.Add(nativeSize);
            if (HasEditableTexture) root.Add(content);
            var appearance = InspectorVisuals.CreateRailSection("Appearance");
            GraphicInspectorFields.AddAppearance(
                appearance, serializedObject, HasEditableMaterial);
            root.Add(appearance);
            var layout = InspectorVisuals.CreateRailSection("Layout");
            layout.Add(SerializedPropertyField.Create(
                preserveAspectRatio, "Preserve Aspect Ratio"));
            layout.Add(OrientationInspectorFields.CreateFlip(flip));
            layout.Add(OrientationInspectorFields.CreateQuarterTurns(rotateId));
            root.Add(layout);
            var interaction = InspectorVisuals.CreateRailSection("Interaction");
            GraphicInspectorFields.AddInteraction(interaction, serializedObject);
            root.Add(interaction);
            return SerializedPropertyField.Observe(root, RefreshTexture, texture);
        }

        private void SetNativeSize()
        {
            foreach (var item in targets)
            {
                var graphic = (LSRawImage)item;
                if (graphic.texture == null) continue;
                var rectTransform = graphic.rectTransform;
                Undo.RecordObject(rectTransform, "Set Native Size");
                graphic.SetNativeSize();
                PrefabUtility.RecordPrefabInstancePropertyModifications(rectTransform);
            }
        }
    }
}
#endif
