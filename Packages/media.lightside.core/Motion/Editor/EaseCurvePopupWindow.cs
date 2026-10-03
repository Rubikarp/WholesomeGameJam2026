using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// The curve editor an <see cref="Ease"/> field opens: a live motion preview, the editing canvas, a
    /// readout row with exact-coordinate fields, and a preset gallery.
    /// </summary>
    internal sealed class EaseCurvePopupWindow : InspectorPopupWindow
    {
        private const float WindowWidth = 340f;
        private const float WindowHeight = 296f;

        [NonSerialized] private UnityEngine.Object[] targets;
        [NonSerialized] private string propertyPath;
        [NonSerialized] private SerializedObject serializedObject;
        [NonSerialized] private SerializedPropertyBinding binding;

        private EaseCurveField canvas;
        private EasePreviewStrip preview;
        private EasePresetStrip presets;
        private Label readout;
        private FloatField coordX;
        private FloatField coordY;

        /// <summary>Opens an editor bound to the supplied serialized ease on every selected target.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
        public static void Show(Rect anchor, SerializedProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var window = CreateInstance<EaseCurvePopupWindow>();
            window.targets = property.serializedObject.targetObjects;
            window.propertyPath = property.propertyPath;
            window.serializedObject = new SerializedObject(window.targets);
            window.binding = new SerializedPropertyBinding(
                InspectorHelpers.RequireProperty(window.serializedObject, window.propertyPath));
            window.ShowAtScreenRect(InspectorVisuals.CaptureScreenRect(anchor), WindowWidth, WindowHeight);
        }

        /// <summary>Builds the retained-mode curve editor.</summary>
        public void CreateGUI()
        {
            if (!TryResolve(out var property))
            {
                Close();
                return;
            }

            var root = CreatePopupRoot("lightside-ease-popup");

            preview = new EasePreviewStrip();
            root.Add(preview);

            canvas = new EaseCurveField(binding);
            root.Add(canvas);

            var footer = new VisualElement();
            footer.AddToClassList("lightside-ease-popup__footer");
            readout = new Label();
            readout.AddToClassList("lightside-ease-popup__readout");
            footer.Add(readout);
            footer.Add(CoordLabel("X"));
            coordX = Coord();
            footer.Add(coordX);
            footer.Add(CoordLabel("Y"));
            coordY = Coord();
            footer.Add(coordY);
            root.Add(footer);

            presets = new EasePresetStrip();
            root.Add(presets);

            canvas.Changed += SyncChrome;
            presets.Applied += ApplyPreset;
            coordX.RegisterValueChangedCallback(_ => CommitCoords());
            coordY.RegisterValueChangedCallback(_ => CommitCoords());
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnPanelKeyDown);
            root.TrackPropertyValue(property, _ => RefreshAll());
            RefreshAll();
        }

        private static Label CoordLabel(string text)
        {
            var label = new Label(text);
            label.AddToClassList("lightside-ease-popup__coord-label");
            return label;
        }

        private static FloatField Coord()
        {
            var field = new FloatField { isDelayed = true };
            field.AddToClassList("lightside-ease-popup__coord");
            return field;
        }

        private bool TryResolve(out SerializedProperty property)
        {
            property = null;
            if (targets == null || targets.Length == 0 ||
                string.IsNullOrEmpty(propertyPath)) return false;
            for (var i = 0; i < targets.Length; i++)
                if (targets[i] == null) return false;
            serializedObject ??= new SerializedObject(targets);
            serializedObject.UpdateIfRequiredOrScript();
            property = serializedObject.FindProperty(propertyPath);
            return property != null;
        }

        private void RefreshAll()
        {
            if (!TryResolve(out _))
            {
                Close();
                return;
            }
            canvas.Refresh();
            var current = canvas.Value;
            preview.SetEase(in current);
            presets.SetCurrent(in current);
            SyncChrome();
        }

        private void SyncChrome()
        {
            readout.text = canvas.ReadoutText;
            var current = canvas.Value;
            preview.SetEase(in current);
            presets.SetCurrent(in current);

            var solo = canvas.TryGetSoloPoint(out var position);
            coordX.SetEnabled(solo);
            coordY.SetEnabled(solo);
            if (solo && !canvas.IsBusy)
            {
                coordX.SetValueWithoutNotify(position.x);
                coordY.SetValueWithoutNotify(position.y);
            }
        }

        private void CommitCoords()
        {
            if (!canvas.TryGetSoloPoint(out _)) return;
            canvas.MoveSoloPoint(new Vector2(coordX.value, coordY.value));
        }

        private void ApplyPreset(Ease preset)
        {
            if (!TryResolve(out _)) return;
            binding.TransformValue(_ => preset, "Apply Ease Preset");
            RefreshAll();
            preview.Replay();
        }

        private void OnPanelKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape || canvas.IsBusy) return;
            evt.StopImmediatePropagation();
            CloseToOwner();
        }
    }
}
