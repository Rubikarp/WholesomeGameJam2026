using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Pasting a bare URL over a non-collapsed selection turns the selection into a link instead of replacing
    /// it with the URL text — the ProseMirror / WordPress paste-link convention. Uses the field's own link
    /// style (the style whose modifier speaks Markdown link syntax, composites included); with no link style
    /// in the component's <c>Styles</c> the URL pastes as plain text. A collapsed caret pastes normally, and
    /// non-URL payloads pass through.
    /// </summary>
    [Serializable]
    [TypeDescription("Paste a URL over selected text to turn the selection into a link")]
    [TypeGroup("Formatting", 1)]
    public sealed class LinkOnPasteBehavior : InputBehavior
    {
        [NonSerialized] private List<StyleBinding> bindingScratch;

        protected override void OnEnable() => editable.InputFilter.Subscribe(Filter);
        protected override void OnDisable() => editable.InputFilter.Unsubscribe(Filter);

        private void Filter(ref InputEdit edit)
        {
            if (edit.Rejected || edit.Reason != TextChangeReason.Paste) return;
            if (edit.replacedCodepoints <= 0 || string.IsNullOrEmpty(edit.text)) return;
            if (!RawUrlParseRule.IsBareUrl(edit.text.AsSpan(), out var url)) return;

            bindingScratch ??= new List<StyleBinding>();
            bindingScratch.Clear();
            SourceMarkup.CollectStyleBindings(editable.TextComponent, bindingScratch);
            for (int i = 0; i < bindingScratch.Count; i++)
            {
                var b = bindingScratch[i];
                if (ClipboardModifierBindMap.GetSchema(b.modifier)?.Markdown?.Kind != MarkdownSyntaxKind.Link) continue;
                var parameter = SourceMarkup.SlotParameter(b.childIndex, url);
                editable.ApplyStyleRange(b.modifier, b.rule,
                    edit.insertCodepointIndex, edit.insertCodepointIndex + edit.replacedCodepoints, parameter);
                edit.Rejected = true;
                return;
            }
        }
    }
}
