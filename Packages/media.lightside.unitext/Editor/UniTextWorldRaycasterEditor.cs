using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextWorldRaycaster))]
    [CanEditMultipleObjects]
    internal class UniTextWorldRaycasterEditor : FullWidthEditor
    {
        private SerializedProperty blockingObjects;
        private SerializedProperty blockingMask;
        private SerializedProperty sortPriority;
        private SerializedProperty renderPriority;

        private void OnEnable()
        {
            blockingObjects = InspectorHelpers.RequireProperty(serializedObject, "blockingObjects");
            blockingMask = InspectorHelpers.RequireProperty(serializedObject, "blockingMask");
            sortPriority = InspectorHelpers.RequireProperty(serializedObject, "sortPriority");
            renderPriority = InspectorHelpers.RequireProperty(serializedObject, "renderPriority");
        }

        public override VisualElement CreateInspectorGUI()
        {
            var root = UniTextInspectorTheme.CreateRoot();
            var blocking = InspectorVisuals.CreateSection("Blocking");
            var modeBinding = new SerializedPropertyBinding(blockingObjects);
            var mode = SerializedPropertyField.Create(blockingObjects);
            var mask = SerializedPropertyField.Create(blockingMask);
            blocking.Add(mode);
#if !UNITEXT_HAS_PHYSICS
            var physicsWarning = new HelpBox(
                "3D Physics module is disabled for this project. The 3D occlusion check has no effect.",
                HelpBoxMessageType.Warning);
            blocking.Add(physicsWarning);
#endif
#if !UNITEXT_HAS_PHYSICS2D
            var physics2DWarning = new HelpBox(
                "2D Physics module is disabled for this project. The 2D occlusion check has no effect.",
                HelpBoxMessageType.Warning);
            blocking.Add(physics2DWarning);
#endif
            blocking.Add(mask);
            root.Add(blocking);
            var sorting = InspectorVisuals.CreateSection("Sorting");
            sorting.Add(SerializedPropertyField.Create(sortPriority));
            sorting.Add(SerializedPropertyField.Create(renderPriority));
            root.Add(sorting);

            void RefreshBlocking()
            {
                var value = (UniTextWorldRaycaster.BlockingObjects)modeBinding.Value;
                var mixed = modeBinding.HasMultipleValues;
                InspectorMotion.SetExpanded(mask,
                    mixed || value != UniTextWorldRaycaster.BlockingObjects.None);
#if !UNITEXT_HAS_PHYSICS
                InspectorMotion.SetExpanded(physicsWarning, mixed ||
                    value is UniTextWorldRaycaster.BlockingObjects.ThreeD or
                        UniTextWorldRaycaster.BlockingObjects.All);
#endif
#if !UNITEXT_HAS_PHYSICS2D
                InspectorMotion.SetExpanded(physics2DWarning, mixed ||
                    value is UniTextWorldRaycaster.BlockingObjects.TwoD or
                        UniTextWorldRaycaster.BlockingObjects.All);
#endif
            }

            mode.RegisterCallback<ChangeEvent<Enum>>(_ => RefreshBlocking());
            return new SerializedPropertyContext(modeBinding, blockingObjects,
                    blockingObjects.displayName)
                .Observe(root, RefreshBlocking);
        }

    }
}
