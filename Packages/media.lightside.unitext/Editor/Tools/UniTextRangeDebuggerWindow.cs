using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    internal sealed class UniTextRangeDebuggerWindow : EditorWindow
    {
        private readonly List<ModifierGraphIssue> graphIssues = new();
        private UniTextBase text;
        private UniTextRangeDebugSnapshot snapshot;
        private bool autoRefresh = true;
        private bool drawSceneOverlay = true;
        private bool applyToAll = true;
        private int selectedEntity;
        private bool hovered;
        private bool pressed;
        private bool focused;
        private bool disabled;
        private bool selected;
        private bool current;
        private bool visited;
        private float longPress;
        private float loading;
        private float drag;
        private float voice;
        private float manualStep = 1f / 60f;

        private InspectorObjectField textField;
        private VisualElement validation;
        private VisualElement details;
        private SelectorField<string> entityField;
        private Foldout entitiesFoldout;
        private Foldout rulesFoldout;
        private double nextAutoRefresh;

        [MenuItem(UniTextMenu.Tools.RangeDebugger, false, 102)]
        internal static void Open()
        {
            var window = GetWindow<UniTextRangeDebuggerWindow>("Range Debugger");
            window.minSize = new Vector2(430f, 320f);
            window.TryUseSelection();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        /// <summary>Builds the retained-mode range diagnostics workspace.</summary>
        public void CreateGUI()
        {
            var panel = rootVisualElement;
            UniTextInspectorTheme.Initialize(panel);
            var root = InspectorVisuals.CreateWindowRoot(panel);
            root.style.paddingLeft = 6f;
            root.style.paddingRight = 6f;
            root.style.paddingTop = 6f;
            root.style.paddingBottom = 6f;
            var targetRow = InspectorVisuals.CreateRow();
            textField = new InspectorObjectField("UniText", typeof(UniTextBase), true)
            {
                value = text,
            };
            textField.style.flexGrow = 1f;
            var selectionButton = new Button(() =>
            {
                TryUseSelection();
                textField.SetValueWithoutNotify(text);
                Refresh();
            }) { text = "Selection" };
            targetRow.Add(textField);
            targetRow.Add(selectionButton);
            root.Add(targetRow);

            var options = InspectorVisuals.CreateRow();
            var auto = new InspectorToggle("Auto refresh") { value = autoRefresh };
            var scene = new InspectorToggle("Scene geometry") { value = drawSceneOverlay };
            var refresh = new Button(() =>
            {
                Capture();
                Refresh();
            }) { text = "Refresh" };
            options.Add(auto);
            options.Add(scene);
            options.Add(refresh);
            root.Add(options);
            validation = InspectorVisuals.CreateStack();
            root.Add(validation);
            root.Add(CreatePreview());
            details = InspectorVisuals.CreateScrollStack();
            details.style.flexGrow = 1f;
            root.Add(details);

            textField.RegisterValueChangedCallback(evt =>
            {
                text = evt.newValue as UniTextBase;
                snapshot = null;
                Refresh();
            });
            auto.RegisterValueChangedCallback(evt => autoRefresh = evt.newValue);
            scene.RegisterValueChangedCallback(evt =>
            {
                drawSceneOverlay = evt.newValue;
                SceneView.RepaintAll();
            });
            Refresh();
        }

        private void Refresh()
        {
            if (details == null) return;
            validation.Clear();
            details.Clear();
            if (text == null)
            {
                UpdateEntityChoices();
                details.Add(new HelpBox(
                    "Assign a UniText component or select one in the Hierarchy.",
                    HelpBoxMessageType.Info));
                return;
            }
            Capture();
            UpdateEntityChoices();
            graphIssues.Clear();
            ModifierGraphValidator.Validate(text, graphIssues);
            ModifierGraphIssueRenderer.Append(validation, graphIssues);
            details.Add(CreateEntities());
            details.Add(CreateRules());
        }

        private VisualElement CreatePreview()
        {
            var root = new InspectorFoldout { text = "State Preview", value = true };
            var apply = new InspectorToggle("Apply To All") { value = applyToAll };
            entityField = new SelectorField<string>("Entity", new[] { "No entities" }, 0);
            root.Add(apply);
            root.Add(entityField);
            AddToggle(root, "Hovered", hovered, value => hovered = value);
            AddToggle(root, "Pressed", pressed, value => pressed = value);
            AddToggle(root, "Focused", focused, value => focused = value);
            AddToggle(root, "Disabled", disabled, value => disabled = value);
            AddToggle(root, "Selected", selected, value => selected = value);
            AddToggle(root, "Current", current, value => current = value);
            AddToggle(root, "Visited", visited, value => visited = value);
            AddSlider(root, "Long Press", longPress, value => longPress = value);
            AddSlider(root, "Loading", loading, value => loading = value);
            AddSlider(root, "Drag", drag, value => drag = value);
            AddSlider(root, "Voice", voice, value => voice = value);
            var actions = InspectorVisuals.CreateRow();
            var applySignals = new Button(ApplySignals) { text = "Apply Signals" };
            applySignals.style.flexGrow = 1f;
            var step = new FloatField("Manual Δt") { value = manualStep };
            step.AddToClassList("unitext-range-debugger__step");
            var advance = new Button(() =>
                UniTextRanges.For(text).AdvanceManual(Mathf.Max(0f, manualStep)))
            {
                text = "Step",
            };
            actions.Add(applySignals);
            actions.Add(step);
            actions.Add(advance);
            root.Add(actions);
            apply.RegisterValueChangedCallback(evt =>
            {
                applyToAll = evt.newValue;
                UpdateEntityChoices();
            });
            entityField.RegisterValueChangedCallback(evt =>
                selectedEntity = entityField.Index);
            step.RegisterValueChangedCallback(evt => manualStep = evt.newValue);
            UpdateEntityChoices();
            return root;
        }

        private void UpdateEntityChoices()
        {
            if (entityField == null) return;
            var labels = new List<string>();
            var entityCount = snapshot == null ? 0 : snapshot.Entities.Length;
            if (snapshot != null)
            {
                var entities = snapshot.Entities;
                for (var i = 0; i < entities.Length; i++)
                    labels.Add($"{entities[i].Identity} · {entities[i].ModifierType.Name}");
            }
            if (labels.Count == 0) labels.Add("No entities");
            selectedEntity = Mathf.Clamp(selectedEntity, 0, labels.Count - 1);
            entityField.SetChoices(labels, selectedEntity);
            entityField.style.display = applyToAll || entityCount == 0
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private static void AddToggle(VisualElement root, string label, bool value,
            System.Action<bool> changed)
        {
            var field = new InspectorToggle(label) { value = value };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            root.Add(field);
        }

        private static void AddSlider(VisualElement root, string label, float value,
            System.Action<float> changed)
        {
            var field = new InspectorSlider(label, 0f, 1f) { value = value };
            field.RegisterValueChangedCallback(evt => changed(evt.newValue));
            root.Add(field);
        }

        private VisualElement CreateEntities()
        {
            var entities = snapshot.Entities;
            entitiesFoldout = new InspectorFoldout
            {
                text = $"Entities ({entities.Length})",
                value = entitiesFoldout?.value ?? true,
            };
            var interactions = UniTextInteractions.For(text);
            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var section = new InspectorFoldout
                {
                    text = $"{i}. {entity.Identity} · {entity.ModifierType.Name}",
                };
                section.Add(new Label($"Channel: {entity.Channel?.name ?? "—"}"));
                section.Add(new Label($"Source: {entity.Source?.GetType().Name ?? "—"}"));
                section.Add(new Label($"Primary Value: {entity.PrimaryValue ?? "—"}"));
                section.Add(new Label($"Payload: {entity.Payload.Type?.FullName ?? "—"}"));
                section.Add(new Label($"State: {entity.State}" +
                                      (entity.Focused ? " · Focused" : string.Empty)));
                section.Add(new Label(
                    $"Order: priority {entity.Priority}, style {entity.StyleOrder}, " +
                    $"registration {entity.RegistrationOrder}"));
                section.Add(new Label(
                    $"Geometry: {entity.Segments.Length} segments, " +
                    $"{entity.HitFragments.Length} fragments, {entity.Bounds}"));
                for (var segment = 0; segment < entity.Segments.Length; segment++)
                {
                    var value = entity.Segments[segment];
                    section.Add(new Label(
                        $"segment {value.Id}: {value.Range.start}..{value.Range.End}"));
                }
                var identity = entity.Identity;
                var actions = InspectorVisuals.CreateRow();
                actions.Add(new Button(() => interactions.Focus(identity)) { text = "Focus" });
                actions.Add(new Button(() => interactions.ScrollIntoView(identity))
                    { text = "Reveal" });
                section.Add(actions);
                entitiesFoldout.Add(section);
            }
            return entitiesFoldout;
        }

        private VisualElement CreateRules()
        {
            var ranges = snapshot.Rules;
            rulesFoldout = new InspectorFoldout
            {
                text = $"Rules ({ranges.Length})",
                value = rulesFoldout?.value ?? true,
            };
            for (var i = 0; i < ranges.Length; i++)
            {
                var rule = ranges[i];
                var section = new InspectorFoldout
                {
                    text = $"{i}. {rule.EffectType.Name} · {rule.Target}",
                };
                section.Add(new Label($"Entity: {rule.Entity} / segment {rule.Segment}"));
                section.Add(new Label(
                    $"Playback: {rule.PlaybackType.Name}, weight {rule.Weight:0.###}, " +
                    $"matched {rule.SelectorMatched}, transitioning {rule.Transitioning}"));
                section.Add(new Label(
                    $"Signals: H:{rule.Hovered} P:{rule.Pressed} F:{rule.Focused} " +
                    $"D:{rule.Disabled} · hold {rule.LongPressProgress:0.##}, " +
                    $"load {rule.LoadingProgress:0.##}, drag {rule.DragProgress:0.##}, " +
                    $"voice {rule.VoiceProgress:0.##}"));
                rulesFoldout.Add(section);
            }
            return rulesFoldout;
        }

        private void ApplySignals()
        {
            var entities = snapshot.Entities;
            var ranges = UniTextRanges.For(text);
            if (applyToAll)
            {
                ranges.SetSignalForAll(RangeSignals.Hovered, hovered);
                ranges.SetSignalForAll(RangeSignals.Pressed, pressed);
                ranges.SetSignalForAll(RangeSignals.Focused, focused);
                ranges.SetSignalForAll(RangeSignals.Disabled, disabled);
                ranges.SetSignalForAll(RangeSignals.Selected, selected);
                ranges.SetSignalForAll(RangeSignals.Current, current);
                ranges.SetSignalForAll(RangeSignals.Visited, visited);
                ranges.SetSignalForAll(RangeSignals.LongPressProgress, longPress);
                ranges.SetSignalForAll(RangeSignals.LoadingProgress, loading);
                ranges.SetSignalForAll(RangeSignals.DragProgress, drag);
                ranges.SetSignalForAll(RangeSignals.VoiceProgress, voice);
            }
            else if (entities.Length > 0)
            {
                var identity = entities[Mathf.Clamp(selectedEntity, 0, entities.Length - 1)].Identity;
                ranges.SetSignal(identity, RangeSignals.Hovered, hovered);
                ranges.SetSignal(identity, RangeSignals.Pressed, pressed);
                ranges.SetSignal(identity, RangeSignals.Focused, focused);
                ranges.SetSignal(identity, RangeSignals.Disabled, disabled);
                ranges.SetSignal(identity, RangeSignals.Selected, selected);
                ranges.SetSignal(identity, RangeSignals.Current, current);
                ranges.SetSignal(identity, RangeSignals.Visited, visited);
                ranges.SetSignal(identity, RangeSignals.LongPressProgress, longPress);
                ranges.SetSignal(identity, RangeSignals.LoadingProgress, loading);
                ranges.SetSignal(identity, RangeSignals.DragProgress, drag);
                ranges.SetSignal(identity, RangeSignals.VoiceProgress, voice);
            }
            Capture();
            Refresh();
        }

        private void Capture()
        {
            if (text != null) snapshot = UniTextRangeDebugSnapshot.Capture(text);
        }

        private void TryUseSelection()
        {
            if (Selection.activeGameObject == null) return;
            text = Selection.activeGameObject.GetComponent<UniTextBase>();
            snapshot = null;
        }

        private void OnEditorUpdate()
        {
            if (!autoRefresh || EditorApplication.timeSinceStartup < nextAutoRefresh) return;
            nextAutoRefresh = EditorApplication.timeSinceStartup + 0.25;
            Refresh();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!drawSceneOverlay || text == null || snapshot == null) return;
            var previous = Handles.color;
            Handles.color = new Color(0.1f, 0.85f, 1f, 0.9f);
            var entities = snapshot.Entities;
            for (var i = 0; i < entities.Length; i++)
            {
                var fragments = entities[i].HitFragments;
                for (var j = 0; j < fragments.Length; j++) DrawRect(fragments[j]);
            }
            Handles.color = previous;
        }

        private void DrawRect(Rect rect)
        {
            var transform = text.transform;
            var a = transform.TransformPoint(new Vector3(rect.xMin, rect.yMin));
            var b = transform.TransformPoint(new Vector3(rect.xMin, rect.yMax));
            var c = transform.TransformPoint(new Vector3(rect.xMax, rect.yMax));
            var d = transform.TransformPoint(new Vector3(rect.xMax, rect.yMin));
            Handles.DrawLine(a, b);
            Handles.DrawLine(b, c);
            Handles.DrawLine(c, d);
            Handles.DrawLine(d, a);
        }
    }
}
