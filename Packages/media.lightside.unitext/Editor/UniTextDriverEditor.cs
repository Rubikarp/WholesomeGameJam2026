using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomEditor(typeof(UniTextDriver))]
    [CanEditMultipleObjects]
    internal sealed class UniTextDriverEditor : FullWidthEditor
    {
        public override VisualElement CreateInspectorGUI()
        {
            serializedObject.Update();
            var root = InspectorVisuals.CreateRoot();
            UniTextInspectorTheme.Initialize(root);

            AddField(root, "playOnEnable");

            var timing = InspectorVisuals.CreateEqualRow();
            timing.Add(SerializedPropertyField.Create(serializedObject, "clock"));
            timing.Add(SerializedPropertyField.Create(serializedObject, "speed"));
            timing.Add(SerializedPropertyField.Create(serializedObject, "duration"));

            var repeat = MotionInspectorFields.CreateRepeat(
                InspectorHelpers.RequireProperty(serializedObject, "cycles"),
                InspectorHelpers.RequireProperty(serializedObject, "cycleMode"),
                playhead: true);

            var progressProp = InspectorHelpers.RequireProperty(serializedObject, "progress");
            var progress = new InspectorFillSlider(0, 100,
                Mathf.RoundToInt(progressProp.floatValue * 100f),
                percent => $"Progress {percent}%");
            progress.style.flexGrow = 1f;
            progress.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                progressProp.floatValue = evt.newValue / 100f;
                serializedObject.ApplyModifiedProperties();
            });
            SerializedPropertyField.OnChange(progress,
                () => progress.SetValueWithoutNotify(
                    Mathf.RoundToInt(progressProp.floatValue * 100f)), progressProp);
            progress.schedule.Execute(() =>
            {
                if (targets.Length == 0 || targets[0] is not UniTextDriver driver) return;
                var value = Mathf.RoundToInt(driver.Progress * 100f);
                if (progress.value != value) progress.SetValueWithoutNotify(value);
            }).Every(60);

            var transport = new InspectorTransportBar(AnyPlaying,
                playing =>
                {
                    foreach (var driver in Drivers())
                        if (playing) driver.Play();
                        else driver.Pause();
                },
                () =>
                {
                    foreach (var driver in Drivers()) driver.Stop();
                });
            transport.Add(progress);
            var timeline = new Button(() => TimelineWindow.Open(target))
            {
                tooltip = "Open Timeline",
            };
            timeline.style.width = 24f;
            timeline.style.paddingLeft = 0f;
            timeline.style.paddingRight = 0f;
            timeline.style.paddingTop = 0f;
            timeline.style.paddingBottom = 0f;
            var timelineIcon = EditorResources.CreateIcon("timeline");
            timelineIcon.style.width = 14f;
            timelineIcon.style.height = 14f;
            timelineIcon.style.alignSelf = Align.Center;
            timeline.style.justifyContent = Justify.Center;
            timeline.Add(timelineIcon);
            transport.Add(timeline);
            root.Add(transport);
            root.Add(timing);
            root.Add(repeat);
            AddField(root, "clips");

            return root;
        }

        private void AddField(VisualElement root, string name)
            => root.Add(SerializedPropertyField.Create(serializedObject, name));

        /// <summary>The selected drivers the inspector may drive; an inactive component ignores transport.</summary>
        private IEnumerable<UniTextDriver> Drivers()
        {
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] is UniTextDriver driver && driver.isActiveAndEnabled)
                    yield return driver;
        }

        private bool AnyPlaying()
        {
            foreach (var driver in Drivers())
                if (driver.IsPlaying) return true;
            return false;
        }

    }
}
