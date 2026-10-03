using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniText))]
    [CanEditMultipleObjects]
    internal class UniTextEditor : UniTextBaseEditor
    {
        protected override void OnEnable()
        {
            base.OnEnable();
            SceneView.duringSceneGui += DrawRaycastAreaOnSceneView;
        }

        protected override void OnDisable()
        {
            SceneView.duringSceneGui -= DrawRaycastAreaOnSceneView;
            base.OnDisable();
        }

        private void DrawRaycastAreaOnSceneView(SceneView sceneView)
        {
            if (!target || targets.Length > 1) return;
            if (!sceneView.drawGizmos) return;

            var graphic = target as UniText;
            if (graphic == null || !graphic.raycastTarget) return;

            var rt = graphic.rectTransform;
            Handles.color = Handles.UIColliderHandleColor;
            DrawInsetRect(rt.rect, rt.transform, graphic.raycastPadding);
        }

        protected override void AddOtherFields(VisualElement container)
        {
            GraphicInspectorFields.AddInteraction(container, serializedObject, false);
            SerializedPropertyField.OnChange(container, SceneView.RepaintAll,
                InspectorHelpers.RequireProperty(serializedObject, "m_RaycastTarget"),
                InspectorHelpers.RequireProperty(serializedObject, "m_RaycastPadding"));
        }

#if UNITEXT_DEBUG
        protected override void AddDebugSections(VisualElement root)
        {
            if (targets.Length != 1) return;
            var ut = (UniText)uniText;
            var section = CreateToolkitSection("Debug");
            var index = 0;
            foreach (var renderer in ut.CanvasRenderers)
            {
                if (renderer == null)
                {
                    index++;
                    continue;
                }

                var material = renderer.materialCount > 0 ? renderer.GetMaterial(0) : null;
                var shaderName = material != null ? material.shader.name : "no material";
                if (shaderName.StartsWith("Hidden/LightSide/"))
                    shaderName = shaderName.Substring("Hidden/LightSide/".Length);
                else if (shaderName.StartsWith("LightSide/"))
                    shaderName = shaderName.Substring("LightSide/".Length);

                var active = renderer.gameObject.activeSelf ? "" : " [inactive]";
                var captured = renderer;
                var visible = new InspectorToggle(
                    $"CR {index}:  {shaderName}  |  mats: {renderer.materialCount}{active}")
                {
                    value = !renderer.cull,
                };
                visible.RegisterValueChangedCallback(evt => captured.cull = !evt.newValue);
                section.Add(visible);
                index++;
            }

            if (index == 0)
                section.Add(new Label("No active sub-mesh renderers"));
            root.Add(section);
        }
#endif
    }
}
