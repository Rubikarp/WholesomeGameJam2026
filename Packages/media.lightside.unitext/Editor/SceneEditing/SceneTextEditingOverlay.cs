using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace LightSide.SceneEditing
{
    /// <summary>
    /// Floating SceneView panel shown while inline-editing text. Formatting actions reuse the
    /// runtime editing API, while component styles reuse the same serialized Toolkit field as the inspector.
    /// </summary>
    [Overlay(typeof(SceneView), "unitext-text-editing", "UniText Text")]
    internal sealed class SceneTextEditingOverlay : Overlay
    {
        private readonly List<(InspectorPillButton button, BaseModifier modifier)> formatButtons = new();
        private SerializedObject styleObject;
        private UniTextBase styleObjectTarget;
        private VisualElement root;
        private EnumSelectorField markup;
        private ColorField color;
        private bool subscribed;

        /// <inheritdoc/>
        public override VisualElement CreatePanelContent()
        {
            root = InspectorVisuals.CreateStack();
            UniTextInspectorTheme.Initialize(root);
            root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (subscribed) return;
                subscribed = true;
                SceneTextEditSession.Changed += Rebuild;
            });
            root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (!subscribed) return;
                subscribed = false;
                SceneTextEditSession.Changed -= Rebuild;
            });
            root.schedule.Execute(RefreshLiveState).Every(100);
            Rebuild();
            return root;
        }

        private void Rebuild()
        {
            if (root == null) return;
            InspectorVisuals.ClearContent(root);
            formatButtons.Clear();
            markup = null;
            color = null;
            if (!SceneTextEditSession.Active)
            {
                root.Add(new Label(
                    "Double-click a UniText in the Scene, or select one and press F2, to edit."));
                return;
            }

            var target = SceneTextEditSession.Target;
            var editable = SceneTextEditSession.Editable;
            if (target == null || editable == null) return;

            markup = new EnumSelectorField("Markup", editable.MarkupVisibility);
            InspectorVisuals.MarkFieldAxis(markup);
            markup.RegisterValueChangedCallback(evt =>
            {
                editable.MarkupVisibility = (MarkupVisibility)evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(markup);

            var formatting = InspectorVisuals.CreateRow();
            formatting.Add(FormatButton(target, editable, "B", new BoldModifier(), "b"));
            formatting.Add(FormatButton(target, editable, "I", new ItalicModifier(), "i"));
            formatting.Add(FormatButton(target, editable, "U", new UnderlineModifier(), "u"));
            color = new ColorField
            {
                showAlpha = true,
                hdr = false,
            };
            color.AddToClassList("unitext-scene-edit__color");
            color.RegisterValueChangedCallback(evt =>
                SceneTextFormatting.ApplyColor(target, editable, evt.newValue));
            formatting.Add(color);
            var clear = new Button(() =>
                SceneTextFormatting.ClearFormatting(editable)) { text = "Clear" };
            formatting.Add(clear);
            root.Add(formatting);

            EnsureStyleObject(target);
            var apply = new Button
            {
                text = "Apply Style to Selection…",
            };
            apply.clicked += () =>
                UniTextBaseEditor.ShowStylePresetSelector(
                    apply.worldBound,
                    new UnityEngine.Object[] { target },
                    styleObject,
                    (value, style) => SceneTextFormatting.ApplyPickedStyle(
                        (UniTextBase)value, editable, style),
                    wrappableOnly: true,
                    includeComponentStyles: true);
            root.Add(apply);

            var styles = InspectorHelpers.RequireProperty(styleObject, "styles");
            root.Add(SerializedPropertyField.Create(styles, "Component Styles"));
            root.Add(new Button(SceneTextEditSession.End) { text = "Done  (Esc)" });
            RefreshLiveState();
        }

        private VisualElement FormatButton(UniTextBase target, UniTextEditable editable,
            string label, BaseModifier modifier, string tag)
        {
            var button = new InspectorPillButton(() =>
            {
                SceneTextFormatting.ToggleModifier(target, editable, modifier, tag);
                RefreshLiveState();
            }) { text = label };
            button.AddToClassList("unitext-scene-edit__format");
            formatButtons.Add((button, modifier));
            return button;
        }

        private void RefreshLiveState()
        {
            if (!SceneTextEditSession.Active) return;
            var editable = SceneTextEditSession.Editable;
            if (editable == null) return;
            markup?.SetValueWithoutNotify(editable.MarkupVisibility);
            for (var i = 0; i < formatButtons.Count; i++)
            {
                var item = formatButtons[i];
                item.button.SetState(
                    SceneTextFormatting.IsActive(editable, item.modifier),
                    false,
                    EditorResources.ToggleAccent);
            }
            if (color != null && SceneTextFormatting.TryGetCaretColor(editable, out var value))
                color.SetValueWithoutNotify(value);
        }

        private void EnsureStyleObject(UniTextBase target)
        {
            if (styleObject != null && styleObjectTarget == target) return;
            styleObject = new SerializedObject(target);
            styleObjectTarget = target;
        }
    }
}
