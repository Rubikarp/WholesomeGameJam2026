# Editable Text Sample

Demonstrates the interactive-text feature set: selectable text and editable fields with
behaviors, decorators, clipboard, and a formatting toolbar.

## Features Demonstrated

### Component ladder
- `UniTextSelectable` — read-only selection (click, drag, double/triple-click, copy)
- `UniTextEditable` — the complete editing surface: document model, undo/redo, IME, clipboard,
  styles, focus lifecycle, and form-field behaviors
- Standard Unity UI components — field background, clipping viewport, layout sizing, and height limits

### Input behaviors (`[SerializeReference]` type picker)
- `PasswordBehavior` + show-password toggle (`PasswordVisibility.cs`)
- `InputBehaviorPreset` — a reusable behavior bundle asset (`BehaviorPreset.asset`)
- Filters/validators and field decorators (placeholder, counter, supporting text)

### Rich-text formatting
- `FormattingPanelHandler.cs` — a `CaretContextHandler` driving B/I/U toggles + a color
  button the Google-Docs way: toggles mirror the caret state (pending typing styles
  included), clicks call `ToggleStyle<T>` / `ApplyStyle<ColorModifier>`

### Media paste
- `MediaPasteAttachments.cs` — a `MediaInputBehavior` that turns pasted/dropped images and
  files into attachment cards above the field (`AttachmentCard.cs` + prefab), chat-style

## Scene

Open `EditableText.unity`. To create your own field from scratch:
`GameObject → UI (Canvas) → UniText → Input Field` (or `Editable Text` / `Selectable Text`).

## Scripts

- `FormattingPanelHandler.cs` — formatting toolbar handler (caret-context queries)
- `MediaPasteAttachments.cs` — media clipboard consumer
- `AttachmentCard.cs` — pending-attachment card (binds image/file, frees textures)
- `PasswordVisibility.cs` — show-password eye toggle
