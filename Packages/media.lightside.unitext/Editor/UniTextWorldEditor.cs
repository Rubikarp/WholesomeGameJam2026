using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextWorld))]
    [CanEditMultipleObjects]
    internal class UniTextWorldEditor : UniTextBaseEditor
    {
        private SerializedProperty sortingOrderProp;
        private SerializedProperty sortingLayerIDProp;
        private SerializedProperty raycastTargetProp;
        private SerializedProperty litProp;
        private SerializedProperty castShadowsProp;
        private SerializedProperty standaloneProp;

        protected override void OnEnable()
        {
            base.OnEnable();
            sortingOrderProp = InspectorHelpers.RequireProperty(serializedObject, "sortingOrder");
            sortingLayerIDProp = InspectorHelpers.RequireProperty(serializedObject, "sortingLayerID");
            raycastTargetProp = InspectorHelpers.RequireProperty(serializedObject, "raycastTarget");
            litProp = InspectorHelpers.RequireProperty(serializedObject, "lit");
            castShadowsProp = InspectorHelpers.RequireProperty(serializedObject, "castShadows");
            standaloneProp = InspectorHelpers.RequireProperty(serializedObject, "standalone");
        }

        protected override void AddOtherFields(VisualElement container)
        {
            container.Add(CreateToggle(
                "Raycast Target",
                raycastTargetProp));
        }

        protected override void AddVariantSections(VisualElement root)
        {
            var section = CreateToolkitSection("Rendering");
            var layers = SortingLayer.layers;
            var layerNames = new string[layers.Length];
            var selected = 0;
            for (int i = 0; i < layers.Length; i++)
            {
                layerNames[i] = layers[i].name;
                if (layers[i].id == sortingLayerIDProp.intValue)
                    selected = i;
            }

            var layer = new SelectorField<string>(
                "Sorting Layer",
                layerNames,
                selected);
            var layerBinding = new SerializedPropertyBinding(sortingLayerIDProp);
            layer.tooltip = "Sorting layer used by the renderer.";
            InspectorVisuals.MarkFieldAxis(layer);

            string LayerName(object value)
            {
                var id = (int)value;
                for (var i = 0; i < layers.Length; i++)
                    if (layers[i].id == id)
                        return layers[i].name;
                return layerNames[0];
            }

            int LayerId(string name)
            {
                for (var i = 0; i < layers.Length; i++)
                    if (layers[i].name == name) return layers[i].id;
                throw new System.InvalidOperationException(
                    $"Sorting layer '{name}' is unavailable.");
            }

            section.Add(new SerializedPropertyContext(
                    layerBinding, sortingLayerIDProp, "Sorting Layer")
                .Bind(layer, name => LayerId(name),
                    read: LayerName,
                    undoName: "Change Sorting Layer"));
            var order = SerializedPropertyField.Create<IntegerField>(
                sortingOrderProp, "Order in Layer");
            order.tooltip = "Rendering order within the selected sorting layer.";
            section.Add(order);

            var litWarning = new HelpBox(
                "Lit world text needs the Lit shader, which Project Settings > LightSide > Rendering " +
                "currently keeps out of builds. Enable Include Lit Shaders, or turn Lit off.",
                HelpBoxMessageType.Warning);
            var hdrpWarning = new HelpBox(
                "HDRP lit world text renders unlit until the LightSide HDRP shader imports " +
                "(Assets/LightSide/HDRP — copied automatically for HDRP projects).",
                HelpBoxMessageType.Warning);

            void RefreshWarnings()
            {
                var anyLit = AnyLit();
                litWarning.style.display = !LightSideSettings.IncludeLitShaders && anyLit
                    ? DisplayStyle.Flex : DisplayStyle.None;
                var hdrpMissing = anyLit && LightSideRenderPipeline.IsHdrp &&
                                  LightSideShaders.Find(LightSideShaderNames.WorldLitHdrp) == null;
                hdrpWarning.style.display = hdrpMissing ? DisplayStyle.Flex : DisplayStyle.None;
            }

            section.Add(CreateToggleRow(
                CreateToggle(
                    "Lit",
                    litProp,
                    RefreshWarnings),
                CreateToggle(
                    "Cast Shadows",
                    castShadowsProp),
                CreateToggle(
                    "Standalone",
                    standaloneProp)));
            section.Add(litWarning);
            section.Add(hdrpWarning);
            RefreshWarnings();
            litWarning.schedule.Execute(RefreshWarnings).Every(500);
            root.Add(section);
        }

        private bool AnyLit()
        {
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] is UniTextWorld world && world.Lit)
                    return true;
            return false;
        }
    }
}
