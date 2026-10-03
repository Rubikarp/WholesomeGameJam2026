using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace LightSide.Inspection
{
    [Overlay(typeof(SceneView), "unitext-inspection", "UniText Inspection")]
    internal sealed class InspectorSettingsOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = InspectorVisuals.CreateRoot();
            var enabled = new InspectorToggle("Enabled") { value = UniTextInspector.Enabled };
            var layers = new EnumSelectorField("Layers", UniTextInspector.Layers);
            var filter = new EnumSelectorField("Filter", UniTextInspector.Filter);
            var bidi = new InspectorToggle("BiDi arrows") { value = UniTextInspector.ShowBiDi };
            var stats = new InspectorToggle("Statistics") { value = UniTextInspector.ShowStats };
            root.Add(enabled);
            root.Add(layers);
            root.Add(filter);
            root.Add(bidi);
            root.Add(stats);
            InspectorVisuals.AlignFields(root);
            enabled.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) UniTextInspector.Enable();
                else UniTextInspector.Disable();
                SceneView.RepaintAll();
            });
            layers.RegisterValueChangedCallback(evt =>
            {
                UniTextInspector.Layers = (InspectionLayers)evt.newValue;
                SceneView.RepaintAll();
            });
            filter.RegisterValueChangedCallback(evt =>
            {
                UniTextInspector.Filter = (InspectionFilter)evt.newValue;
                SceneView.RepaintAll();
            });
            bidi.RegisterValueChangedCallback(evt =>
            {
                UniTextInspector.ShowBiDi = evt.newValue;
                SceneView.RepaintAll();
            });
            stats.RegisterValueChangedCallback(evt =>
            {
                UniTextInspector.ShowStats = evt.newValue;
                SceneView.RepaintAll();
            });
            return root;
        }
    }
}
