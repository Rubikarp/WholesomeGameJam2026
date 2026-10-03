using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Inspects every clipboard channel recognized by UniText, including rich text, media, and
    /// arbitrary MIME payloads.
    /// </summary>
    internal sealed class UniTextClipboardInspectorWindow : EditorWindow
    {
        private readonly struct Channel
        {
            public readonly string label;
            public readonly ClipboardFormat format;

            public Channel(string label, ClipboardFormat format)
            {
                this.label = label;
                this.format = format;
            }
        }

        private sealed class Snapshot
        {
            public bool present;
            public string text;
            public bool expanded;
        }

        private static readonly Channel[] channels =
        {
            new("Plain Text", ClipboardFormat.PlainText),
            new("UniText Source", ClipboardFormat.UniTextSource),
            new("HTML", ClipboardFormat.Html),
            new("Markdown", ClipboardFormat.Markdown),
            new("URL", ClipboardFormat.Url),
        };

        private static readonly ClipboardFormat[] imageProbe =
        {
            ClipboardFormat.Png,
            ClipboardFormat.Jpeg,
            ClipboardFormat.Gif,
        };

        private const int MaxDisplayChars = 8000;

        private readonly Snapshot custom = new() { expanded = true };
        private Snapshot[] snapshots;
        private string customId = "application/vnd.";
        private string providerName = "—";
        private byte[] imageBytes;
        private bool imagePresent;
        private bool imageExpanded = true;
        private ClipboardFormat imageFormat;
        private Texture2D imagePreview;
        private string[] files;
        private bool filesExpanded = true;
        private bool autoRefresh;
        private bool showWhitespace;
        private int lastSignal;
        private Label providerLabel;
        private Label summaryLabel;
        private ScrollView content;

        [MenuItem(UniTextMenu.Tools.ClipboardInspector, false, 101)]
        internal static void Open()
        {
            var window = GetWindow<UniTextClipboardInspectorWindow>("Clipboard");
            window.minSize = new Vector2(360f, 380f);
        }

        private void OnEnable()
        {
            snapshots = new Snapshot[channels.Length];
            for (var i = 0; i < snapshots.Length; i++) snapshots[i] = new Snapshot();
            Refresh();
            for (var i = 0; i < snapshots.Length; i++)
                snapshots[i].expanded = snapshots[i].present;
            imageExpanded = imagePresent;
            filesExpanded = files != null && files.Length > 0;
        }

        private void OnDisable()
        {
            if (imagePreview == null) return;
            DestroyImmediate(imagePreview);
            imagePreview = null;
        }

        private void OnFocus()
        {
            RefreshAndRender();
        }

        /// <summary>Builds the retained-mode clipboard inspection surface.</summary>
        public void CreateGUI()
        {
            var panel = rootVisualElement;
            UniTextInspectorTheme.Initialize(panel);
            var root = InspectorVisuals.CreateWindowRoot(panel);
            var overview = InspectorVisuals.CreateSection("System Clipboard");
            var status = InspectorVisuals.CreateRow();
            providerLabel = CreateMetaLabel(string.Empty);
            providerLabel.AddToClassList("unitext-clipboard__provider");
            summaryLabel = new Label();
            summaryLabel.AddToClassList("unitext-clipboard__meta");
            summaryLabel.AddToClassList("unitext-clipboard__summary");
            status.Add(providerLabel);
            status.Add(summaryLabel);
            overview.Add(status);
            overview.Add(CreateActions());
            root.Add(overview);

            content = InspectorVisuals.CreateScrollSectionStack();
            content.AddToClassList("unitext-clipboard__channels");
            content.style.flexGrow = 1f;
            root.Add(content);
            root.schedule.Execute(PollClipboard).Every(500);
            Render();
        }

        private VisualElement CreateActions()
        {
            var actions = InspectorVisuals.CreateCompactWrapRow();
            actions.Add(new Button(RefreshAndRender) { text = "Refresh" });
            var auto = new Button { text = "Auto Refresh" };
            auto.AddToClassList("lightside-choice-chip");
            auto.EnableInClassList("lightside-choice-chip--selected", autoRefresh);
            auto.clicked += () =>
            {
                autoRefresh = !autoRefresh;
                auto.EnableInClassList("lightside-choice-chip--selected", autoRefresh);
            };
            actions.Add(auto);
            var whitespace = new Button { text = "Whitespace" };
            whitespace.AddToClassList("lightside-choice-chip");
            whitespace.EnableInClassList("lightside-choice-chip--selected", showWhitespace);
            whitespace.clicked += () =>
            {
                showWhitespace = !showWhitespace;
                whitespace.EnableInClassList("lightside-choice-chip--selected", showWhitespace);
                Render();
            };
            actions.Add(whitespace);
            var clear = new Button(() =>
            {
                UniTextClipboard.SetText(string.Empty);
                RefreshAndRender();
            }) { text = "Clear" };
            clear.AddToClassList("unitext-clipboard__clear");
            actions.Add(clear);
            return actions;
        }

        private void PollClipboard()
        {
            if (!autoRefresh) return;
            var signal = ComputeChangeSignal();
            if (signal == lastSignal) return;
            lastSignal = signal;
            RefreshAndRender();
        }

        private int ComputeChangeSignal()
        {
            var provider = UniTextClipboard.Provider;
            if (provider == null) return 0;
            var hash = new System.HashCode();
            provider.TryGetText(ClipboardFormat.PlainText, out var plain);
            hash.Add(plain);
            for (var i = 0; i < channels.Length; i++)
                hash.Add(provider.HasFormat(channels[i].format));
            for (var i = 0; i < imageProbe.Length; i++)
                hash.Add(provider.HasFormat(imageProbe[i]));
            return hash.ToHashCode();
        }

        private void RefreshAndRender()
        {
            Refresh();
            Render();
        }

        private void Refresh()
        {
            if (snapshots == null) return;
            var provider = UniTextClipboard.Provider;
            providerName = provider != null ? provider.GetType().Name : "—";
            for (var i = 0; i < channels.Length; i++)
                Read(provider, channels[i].format, snapshots[i]);
            Read(provider, ClipboardFormat.Custom(customId ?? string.Empty), custom);
            ReadMedia(provider);
            lastSignal = ComputeChangeSignal();
        }

        private void ReadMedia(IClipboardProvider provider)
        {
            imageBytes = null;
            imagePresent = false;
            imageFormat = default;
            if (imagePreview != null)
            {
                DestroyImmediate(imagePreview);
                imagePreview = null;
            }
            files = (provider as IMediaClipboardProvider)?.GetFiles();
            if (provider == null) return;
            for (var i = 0; i < imageProbe.Length; i++)
            {
                var format = imageProbe[i];
                if (!provider.HasFormat(format)) continue;
                imagePresent = true;
                imageFormat = format;
                if (!imageExpanded) break;
                var data = provider.GetData(format);
                if (data == null || data.Length == 0) continue;
                imageBytes = data;
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(data)) imagePreview = texture;
                else DestroyImmediate(texture);
                break;
            }
        }

        private static void Read(IClipboardProvider provider, ClipboardFormat format, Snapshot into)
        {
            into.text = null;
            into.present = provider != null
                           && provider.TryGetText(format, out into.text)
                           && !string.IsNullOrEmpty(into.text);
        }

        private void Render()
        {
            if (content == null || snapshots == null) return;
            var present = 0;
            for (var i = 0; i < snapshots.Length; i++)
                if (snapshots[i].present) present++;
            if (imagePresent) present++;
            if (files != null && files.Length > 0) present++;
            if (custom.present) present++;
            summaryLabel.text = $"{present}/{channels.Length + 3} formats present";
            providerLabel.text = $"Provider · {providerName}";
            InspectorVisuals.ClearContent(content);
            for (var i = 0; i < channels.Length; i++)
                content.Add(CreateChannel(channels[i], snapshots[i]));
            content.Add(CreateImageCard());
            content.Add(CreateFilesCard());
            content.Add(CreateCustomCard());
        }

        private VisualElement CreateChannel(Channel channel, Snapshot snapshot)
        {
            return CreateFormatSection(channel.label, Counts(snapshot), snapshot.expanded,
                value => snapshot.expanded = value, section =>
                {
                    section.Add(CreateMetaLabel(channel.format.Identifier));
                    section.Add(snapshot.present
                        ? CreatePayload(channel.label, channel.format.Identifier, snapshot.text)
                        : CreateAbsentLabel());
                });
        }

        private VisualElement CreateCustomCard()
        {
            return CreateFormatSection("Custom", Counts(custom), custom.expanded,
                value => custom.expanded = value, section =>
                {
                    var row = InspectorVisuals.CreateRow();
                    var id = new TextField("MIME Type") { value = customId };
                    InspectorVisuals.Attach(id);
                    id.AddToClassList("unitext-clipboard__custom-id");
                    var inspect = new Button(() =>
                    {
                        customId = id.value;
                        Read(UniTextClipboard.Provider,
                            ClipboardFormat.Custom(customId ?? string.Empty), custom);
                        Render();
                    }) { text = "Inspect" };
                    id.RegisterValueChangedCallback(evt => customId = evt.newValue);
                    row.Add(id);
                    row.Add(inspect);
                    section.Add(row);
                    section.Add(custom.present
                        ? CreatePayload("Custom", customId, custom.text)
                        : CreateAbsentLabel());
                });
        }

        private VisualElement CreateImageCard()
        {
            var details = !imagePresent
                ? "Not present"
                : imageBytes != null
                    ? $"{imageFormat.Identifier} · {imageBytes.Length} B"
                    : imageFormat.Identifier;
            return CreateFormatSection("Image", details, imageExpanded, value =>
                {
                    imageExpanded = value;
                    if (value) RefreshAndRender();
                }, section =>
                {
                    if (imagePreview != null)
                    {
                        var preview = new Image
                        {
                            image = imagePreview,
                            scaleMode = ScaleMode.ScaleToFit,
                        };
                        preview.AddToClassList("unitext-clipboard__image");
                        preview.style.height = Mathf.Min(360f, imagePreview.height);
                        section.Add(preview);
                    }
                    else if (!imagePresent)
                    {
                        section.Add(CreateAbsentLabel());
                    }
                });
        }

        private VisualElement CreateFilesCard()
        {
            var present = files != null && files.Length > 0;
            var details = present
                ? $"{files.Length} file{(files.Length == 1 ? string.Empty : "s")}"
                : "Not present";
            return CreateFormatSection("Files", details, filesExpanded,
                value => filesExpanded = value, section =>
                {
                    if (present)
                    {
                        for (var i = 0; i < files.Length; i++)
                            section.Add(CreateMetaLabel(files[i]));
                    }
                    else
                    {
                        section.Add(CreateAbsentLabel());
                    }
                });
        }

        private VisualElement CreatePayload(string label, string identifier, string value)
        {
            var display = value.Length > MaxDisplayChars
                ? value.Substring(0, MaxDisplayChars) +
                  $"\n… (truncated, {value.Length} chars total)"
                : value;
            if (showWhitespace) display = Visualize(display);
            var root = InspectorVisuals.CreateStack();
            var field = new InspectorTextArea
            {
                value = display,
                isReadOnly = true,
            };
            field.AddToClassList("unitext-clipboard__payload");
            field.style.maxHeight = 360f;
            root.Add(field);
            var log = new Button(() => Debug.Log($"[Clipboard · {label}] {identifier}\n{value}"))
            {
                text = "Log Payload",
            };
            log.AddToClassList("unitext-clipboard__log");
            root.Add(log);
            return root;
        }

        private static VisualElement CreateFormatSection(string title, string details,
            bool expanded, System.Action<bool> expandedChanged,
            System.Action<VisualElement> populate)
        {
            var section = InspectorVisuals.CreateSection(
                $"{title} · {details}", true, expanded);
            section.AddToClassList("unitext-clipboard__format");
            section.Changed += expandedChanged;
            populate(section);
            return section;
        }

        private static Label CreateAbsentLabel()
        {
            var label = new Label("No data in this clipboard format.");
            label.AddToClassList("unitext-clipboard__absent");
            return label;
        }

        private static Label CreateMetaLabel(string value)
        {
            var label = new Label(value);
            label.AddToClassList("unitext-clipboard__meta");
            return label;
        }

        private static string Counts(Snapshot snapshot)
        {
            if (!snapshot.present) return "Not present";
            var lines = 1;
            for (var i = 0; i < snapshot.text.Length; i++)
                if (snapshot.text[i] == '\n') lines++;
            return $"{snapshot.text.Length:N0} chars · " +
                   $"{Encoding.UTF8.GetByteCount(snapshot.text):N0} B · {lines:N0} lines";
        }

        private static string Visualize(string value)
        {
            var result = new StringBuilder(value.Length + 16);
            for (var i = 0; i < value.Length; i++)
            {
                switch (value[i])
                {
                    case ' ': result.Append('·'); break;
                    case '\t': result.Append('→'); break;
                    case '\r': result.Append('↩'); break;
                    case '\n': result.Append('↵').Append('\n'); break;
                    default: result.Append(value[i]); break;
                }
            }
            return result.ToString();
        }
    }
}
