using UnityEditor;
using UnityEngine;

namespace LightSide.SceneEditing
{
    /// <summary>
    /// Editor-side formatting operations shared by the settings overlay and the right-click menu, so
    /// applying B/I/U/color or a picked style has one implementation. Every op drives the runtime
    /// editing API (<see cref="UniTextEditable.ToggleStyle(BaseModifier,string)"/>, <c>ApplyStyle</c>,
    /// <c>ClearFormatting</c>) and only registers a component style when the selection needs a wrappable
    /// rule the component doesn't yet carry — the same reference-only contract the inspector uses.
    /// </summary>
    internal static class SceneTextFormatting
    {
        internal static bool IsActive(UniTextEditable editable, BaseModifier exemplar) => editable.IsStyleActive(exemplar);

        internal static bool ToggleModifier(UniTextBase target, UniTextEditable editable, BaseModifier exemplar, string defaultTag)
        {
            EnsureWrappable(target, exemplar, defaultTag);
            var on = editable.ToggleStyle(exemplar);
            SceneView.RepaintAll();
            return on;
        }

        internal static void ClearFormatting(UniTextEditable editable)
        {
            editable.ClearFormatting();
            SceneView.RepaintAll();
        }

        /// <summary>
        /// Applies a picked style to the selection. A wrappable rule scopes to the range and reuses
        /// any rule already resolved from component, preset, or global styles; a whole-text style is
        /// added to the component.
        /// </summary>
        internal static void ApplyPickedStyle(UniTextBase target, UniTextEditable editable, Style style)
        {
            var modifier = style?.Modifier;
            if (modifier == null) return;

            if (style.Source is ParseRule rule && rule.CanWrap)
            {
                if (target.ResolveWrapRule(modifier) == null)
                    AddStyle(target, style);
                editable.ToggleStyle(modifier, style.DefaultParameter);
            }
            else
            {
                AddStyle(target, style);
            }
            SceneView.RepaintAll();
        }

        internal static bool TryGetCaretColor(UniTextEditable editable, out Color color)
        {
            if (editable.TryGetStyleParameter<ColorModifier>(out var hex) && ColorParsing.TryParse(hex, out var parsed))
            {
                color = parsed;
                return true;
            }
            color = Color.white;
            return false;
        }

        /// <summary>
        /// Replaces the selected range's existing color instead of nesting color spans. A collapsed
        /// caret delegates replacement to the editable typing-style state.
        /// </summary>
        internal static void ApplyColor(UniTextBase target, UniTextEditable editable, Color color)
        {
            EnsureWrappable(target, new ColorModifier(), "color");
            if (!editable.Selection.IsCollapsed)
                editable.RemoveStyleRange<ColorModifier>(editable.SelectionStart, editable.SelectionEnd);
            editable.ApplyStyle<ColorModifier>(ToHex(color));
            SceneView.RepaintAll();
        }

        /// <summary>Formatting is reference-only: without a rule that can wrap a range the edit is inert, so register a wrappable tag style first — but only when none already resolves (a whole-text style of the same type does not count).</summary>
        private static void EnsureWrappable(UniTextBase target, BaseModifier exemplar, string defaultTag)
        {
            if (target.ResolveWrapRule(exemplar) != null) return;
            AddStyle(target, new Style { Modifier = exemplar, Source = new TagRule(defaultTag) });
        }

        private static void AddStyle(UniTextBase target, Style style) =>
            UniTextBaseEditor.InsertSerializedStyle(target, "styles.items", target.Styles.Count, style);

        private static string ToHex(Color color) =>
            "#" + (color.a >= 0.999f ? ColorUtility.ToHtmlStringRGB(color) : ColorUtility.ToHtmlStringRGBA(color));
    }
}
