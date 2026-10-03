using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// Editor window hosting a <see cref="TimelineView"/> over any registered source. A consumer
    /// registers a factory for its target type once; <see cref="Open"/> then resolves the source
    /// from the target object, and the binding survives domain reloads through the serialized
    /// target reference.
    /// </summary>
    public sealed class TimelineWindow : EditorWindow
    {
        private static readonly Dictionary<Type, Func<Object, ITimelineSource>> factories = new();

        [SerializeField] private Object target;
        [SerializeField] private bool snap = true;

            private ITimelineSource source;
        private TimelineView view;
        private InspectorTransportGlyph playGlyph;
        private Undo.UndoRedoCallback undoCallback;
        private Action<IReadOnlyCollection<Object>> objectsChangedCallback;

        /// <summary>Registers the timeline source factory for one target type.</summary>
        public static void RegisterSource<T>(Func<T, ITimelineSource> factory) where T : Object
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            factories[typeof(T)] = candidate => factory((T)candidate);
        }

        /// <summary>
        /// Opens (or focuses) the timeline for one target whose type has a registered source
        /// factory.
        /// </summary>
        public static void Open(Object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            foreach (var window in Resources.FindObjectsOfTypeAll<TimelineWindow>())
                if (window.target == target)
                {
                    window.Show();
                    window.Focus();
                    window.FocusView();
                    return;
                }
            var created = CreateInstance<TimelineWindow>();
            created.target = target;
            created.titleContent = new GUIContent("Timeline");
            created.minSize = new Vector2(480f, 180f);
            created.Show();
        }

        private static ITimelineSource Resolve(Object target)
        {
            if (target == null) return null;
            for (var type = target.GetType(); type != null; type = type.BaseType)
                if (factories.TryGetValue(type, out var factory))
                    return factory(target);
            return null;
        }

        private void OnEnable()
        {
            undoCallback ??= RefreshView;
            objectsChangedCallback ??= _ => RefreshView();
            Undo.undoRedoPerformed += undoCallback;
            SerializedStateEditorDriver.ObjectsChanged += objectsChangedCallback;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= undoCallback;
            SerializedStateEditorDriver.ObjectsChanged -= objectsChangedCallback;
            (source as IDisposable)?.Dispose();
            source = null;
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            InspectorVisuals.Attach(root);
            root.AddToClassList("lightside-timeline-window");
            Rebuild();
        }

        private void Rebuild()
        {
            var root = rootVisualElement;
            root.Clear();
            (source as IDisposable)?.Dispose();
            source = Resolve(target);
            if (source == null)
            {
                var missing = new Label(target == null
                    ? "The timeline target is gone."
                    : $"No timeline source is registered for {target.GetType().Name}.");
                missing.AddToClassList("lightside-timeline__empty");
                root.Add(missing);
                return;
            }

            root.Add(BuildToolbar());
            view = new TimelineView
            {
                SnapEnabled = snap,
                PersistenceKey = "LightSide.Timeline." + ObjectUtils.GetInstanceIdCompat(target),
            };
            view.style.flexGrow = 1f;
            view.SetSource(source);
            root.Add(view);
            FocusView();
        }

        /// <summary>
        /// Gives the tracks the keyboard, so the window's shortcuts work the moment it opens. Without it the
        /// panel hands its first focus to a toolbar control and every shortcut goes there instead.
        /// </summary>
        private void FocusView() => view?.schedule.Execute(() => view.Focus());

        private VisualElement BuildToolbar()
        {
            var toolbar = new VisualElement();
            toolbar.AddToClassList("lightside-timeline-toolbar");

            var titleChip = new Button(() =>
            {
                if (target == null) return;
                EditorGUIUtility.PingObject(target);
                Selection.activeObject = target;
            })
            {
                tooltip = "Ping the edited object",
            };
            titleChip.AddToClassList("lightside-timeline-toolbar__title-chip");
            var badge = StateIcon("timeline", EditorResources.ToggleAccent);
            badge.AddToClassList("lightside-timeline-toolbar__badge");
            titleChip.Add(badge);
            var title = new Label(source.Title) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("lightside-timeline-toolbar__title");
            titleChip.Add(title);
            toolbar.Add(titleChip);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);

            var transport = source.Transport;
            if (transport != null)
            {
                var group = ToolbarGroup();
                Button playPause = null;
                playPause = InspectorTransportGlyph.CreateButton(
                    InspectorTransportGlyph.Shape.Play, "Play / Pause (Space)", () =>
                    {
                        if (transport.IsPlaying) transport.Pause();
                        else transport.Play();
                        RefreshPlayGlyph(transport, playPause, playGlyph);
                    }, out playGlyph);
                group.Add(playPause);
                group.Add(InspectorTransportGlyph.CreateButton(
                    InspectorTransportGlyph.Shape.Stop, "Stop", transport.Stop, out var stopGlyph));
                stopGlyph.parent.AddToClassList("lightside-button--danger");
                RefreshPlayGlyph(transport, playPause, playGlyph);
                playPause.schedule.Execute(
                    () => RefreshPlayGlyph(transport, playPause, playGlyph)).Every(200);
                var time = new Label();
                time.AddToClassList("lightside-timeline-toolbar__time");
                time.schedule.Execute(() =>
                    time.text = transport.Time.ToString("0.00") + "s").Every(100);
                group.Add(time);
                toolbar.Add(group);
            }

            var tools = ToolbarGroup();
            var snapIcon = StateIcon("magnet", SnapTint);
            Button snapButton = null;
            snapButton = ToolbarButton(snapIcon, "Snap to grid and clip edges", () =>
            {
                snap = !snap;
                view.SnapEnabled = snap;
                snapIcon.tintColor = SnapTint;
                snapButton.EnableInClassList("lightside-timeline-toolbar__button--active", snap);
            });
            snapButton.EnableInClassList("lightside-timeline-toolbar__button--active", snap);
            tools.Add(snapButton);
            tools.Add(ToolbarButton(EditorResources.CreateIcon("maximize"),
                "Fit the whole timeline (F)", () => view.FitToContent()));
            Button help = null;
            help = new Button(() => TimelineClipPopup.Show(help.worldBound, BuildHelp))
            {
                text = "?",
                tooltip = "Shortcuts",
            };
            help.AddToClassList("lightside-timeline-toolbar__button");
            tools.Add(help);
            toolbar.Add(tools);
            return toolbar;
        }

        private static VisualElement BuildHelp()
        {
            var root = new VisualElement();
            root.AddToClassList("lightside-timeline-help");
            void Row(string keys, string action)
            {
                var row = new VisualElement();
                row.AddToClassList("lightside-timeline-help__row");
                var shortcut = new Label(keys);
                shortcut.AddToClassList("lightside-timeline-help__keys");
                row.Add(shortcut);
                var what = new Label(action);
                what.AddToClassList("lightside-timeline-help__action");
                row.Add(what);
                root.Add(row);
            }
            Row("Drag ruler", "Scrub");
            Row("Shift+Drag ruler", "Set play range; Shift+Click clears");
            Row("Space", "Play / Pause");
            Row("Ctrl+Wheel", "Zoom at pointer");
            Row("Shift+Wheel / Middle / Alt+Drag", "Pan");
            Row("F / A", "Fit  ·  Home — rewind  ·  +/− — zoom");
            Row("Click clip", "Select");
            Row("Double-click clip", "Edit (whole selection when multiple)");
            Row("Ctrl+Click", "Toggle in selection");
            Row("Ctrl+A", "Select all");
            Row("Drag on lane", "Marquee select");
            Row("Drag clip / edges", "Move / resize (Alt bypasses snap)");
            Row("Shift+Drag clip", "Drag a copy");
            Row("Drag end marker", "Set sequence duration");
            Row("Ctrl+C / Ctrl+V", "Copy / paste");
            Row("Ctrl+D", "Duplicate selection");
            Row("M / S / Delete", "Mute  ·  Split at playhead  ·  Remove");
            Row("← / → (Shift)", "Nudge by grid (coarse)");
            Row("Enter", "Edit selection");
            return root;
        }

        private static VisualElement ToolbarGroup()
        {
            var group = new VisualElement();
            group.AddToClassList("lightside-timeline-toolbar__group");
            return group;
        }

        private Color SnapTint => snap ? EditorResources.ToggleAccent : EditorResources.IconColor;

        private static void RefreshPlayGlyph(ITimelineTransport transport, Button button,
            InspectorTransportGlyph glyph)
        {
            var playing = transport.IsPlaying;
            glyph.Set(playing
                ? InspectorTransportGlyph.Shape.Pause
                : InspectorTransportGlyph.Shape.Play);
            button.EnableInClassList("lightside-button--warning", playing);
        }

        /// <summary>An icon whose tint the caller keeps changing, so the colour stays a live channel.</summary>
        private static Image StateIcon(string name, Color tint)
            => EditorResources.CreateIcon(EditorResources.GetIcon(name), tint);

        private static Button ToolbarButton(Image icon, string tooltip, Action clicked)
        {
            var button = new Button(clicked) { tooltip = tooltip };
            button.AddToClassList("lightside-timeline-toolbar__button");
            icon.AddToClassList("lightside-timeline-toolbar__icon");
            button.Add(icon);
            return button;
        }

        private void RefreshView()
        {
            if (view == null || source == null) return;
            if (target == null)
            {
                Rebuild();
                return;
            }
            view.Refresh();
        }
    }

    /// <summary>Popup carrying one clip's full editor, anchored to the clip element.</summary>
    internal sealed class TimelineClipPopup : InspectorPopupWindow
    {
        private Action closed;

        internal static void Show(Rect anchorPanelRect, Func<VisualElement> content,
            Action onClosed = null)
        {
            var window = CreateInstance<TimelineClipPopup>();
            window.closed = onClosed;
            var root = window.PreparePopup("lightside-timeline-popup");
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
            };
            scroll.AddToClassList("lightside-timeline-popup__content");
            scroll.Add(content());
            root.Add(scroll);
            var screen = InspectorVisuals.CaptureScreenRect(anchorPanelRect);
            window.ShowAtScreenRect(screen, Mathf.Max(anchorPanelRect.width, 380f), 420f);
            window.FitHeightToContent(scroll);
        }

        private void OnDestroy() => closed?.Invoke();
    }
}
