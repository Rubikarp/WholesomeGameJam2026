using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Reusable track-and-clip timeline: a scrubbable ruler, zoom and pan, single and marquee
    /// selection, clips dragged, resized, nudged and moved across tracks with snapping and live
    /// value readout, and a click-through popup carrying each clip's full editor. All data flows
    /// through an <see cref="ITimelineSource"/>; the view owns only presentation and gestures.
    /// </summary>
    /// <remarks>
    /// Shortcuts: wheel scrolls, Shift+wheel pans, Ctrl+wheel zooms at the pointer, middle-drag
    /// pans, F or A fits, Home rewinds the pan, Space toggles playback, Delete removes the
    /// selection, Ctrl+D duplicates it, arrows nudge by the minor grid (Shift — major), Alt
    /// bypasses snapping while dragging, Escape clears the selection.
    /// </remarks>
    public sealed class TimelineView : VisualElement
    {
        private const float HeaderWidth = 150f;
        private const float TrackHeight = 32f;
        private const float MinPixelsPerSecond = 4f;
        private const float MaxPixelsPerSecond = 2000f;
        private const float EdgeGrabWidth = 6f;
        private const float SnapPixels = 6f;
        private const float AutoPanMargin = 24f;
        private const float AutoPanStep = 12f;
        private const float TailSeconds = 1f;

        private readonly VisualElement content;
        private readonly VisualElement rulerHost;
        private readonly Ruler ruler;
        private readonly ScrollView body;
        private readonly PanBar panBar;
        private readonly VisualElement playhead;
        private readonly VisualElement endMarker;
        private readonly VisualElement durationHandle;
        private readonly VisualElement playRangeOverlay;
        private readonly VisualElement snapGuide;
        private readonly VisualElement marquee;
        private readonly Label dragReadout;
        private readonly VisualElement emptyState;
        private readonly VisualElement addTrackRow;
        private readonly VisualElement addTrackButton;
        private readonly List<VisualElement> trackRows = new();
        private readonly List<VisualElement> lanes = new();
        private readonly List<TimelineClipInfo> clipScratch = new();
        private readonly List<float> snapEdges = new();
        private readonly HashSet<object> selected = new();

        private ITimelineSource source;
        private float pixelsPerSecond = 120f;
        private float viewStart;
        private float playheadTime;
        private bool refreshSuppressed;
        private bool rangeEnabled;
        private float rangeStart;
        private float rangeEnd;
        private bool rangeSelecting;
        private float rangeAnchor;

        private ClipElement dragged;
        private readonly List<GroupMember> dragGroup = new();
        private byte dragMode;
        private bool dragActive;
        private int dragClickCount;
        private Vector2 dragOrigin;
        private float dragStart;
        private float dragDuration;
        private int dragTrack;
        private int dropTrack;
        private int undoGroup = -1;
        private float snapGuideTime = float.NaN;

        private bool marqueeArmed;
        private bool marqueeActive;
        private bool marqueeAdditive;
        private Vector2 marqueeOrigin;
        private Vector2 marqueeEnd;

        private const byte DragMove = 0;
        private const byte DragLeft = 1;
        private const byte DragRight = 2;

        private readonly struct GroupMember
        {
            internal readonly ClipElement element;
            internal readonly float start;
            internal readonly int track;

            internal GroupMember(ClipElement element, float start, int track)
            {
                this.element = element;
                this.start = start;
                this.track = track;
            }
        }

        /// <summary>Whether drags snap to the grid, clip edges, the playhead and zero. Alt bypasses it per gesture.</summary>
        public bool SnapEnabled { get; set; } = true;

        /// <summary>Session key persisting zoom, pan and the play range; null disables persistence.</summary>
        public string PersistenceKey { get; set; }

        public TimelineView()
        {
            AddToClassList("lightside-timeline");
            InspectorVisuals.Attach(this);
            focusable = true;

            content = new VisualElement();
            content.AddToClassList("lightside-timeline__content");
            Add(content);

            var headerRow = new VisualElement();
            headerRow.AddToClassList("lightside-timeline__header-row");
            var corner = new VisualElement();
            corner.AddToClassList("lightside-timeline__corner");
            corner.style.width = HeaderWidth;
            var cornerLabel = new Label("Tracks");
            cornerLabel.AddToClassList("lightside-timeline__corner-label");
            corner.Add(cornerLabel);
            headerRow.Add(corner);
            rulerHost = new VisualElement();
            rulerHost.AddToClassList("lightside-timeline__ruler-host");
            ruler = new Ruler(this);
            ruler.AddToClassList("lightside-timeline__ruler");
            rulerHost.Add(ruler);
            headerRow.Add(rulerHost);
            content.Add(headerRow);

            body = new ScrollView(ScrollViewMode.Vertical);
            body.AddToClassList("lightside-timeline__body");
            content.Add(body);

            addTrackRow = new VisualElement();
            addTrackRow.AddToClassList("lightside-timeline__add-track-row");
            addTrackButton = new VisualElement();
            addTrackButton.AddToClassList("lightside-timeline__add-track");
            addTrackButton.style.width = HeaderWidth;
            var addIcon = EditorResources.CreateIcon("plus");
            addIcon.AddToClassList("lightside-timeline__add-track-icon");
            addTrackButton.Add(addIcon);
            var addLabel = new Label("Add Track") { pickingMode = PickingMode.Ignore };
            addLabel.AddToClassList("lightside-timeline__add-track-label");
            addTrackButton.Add(addLabel);
            addTrackButton.RegisterCallback<ClickEvent>(_ =>
            {
                if (source == null || !source.CanEditTracks) return;
                source.AddTrack();
                Refresh();
            });
            addTrackRow.Add(addTrackButton);
            body.Add(addTrackRow);

            playRangeOverlay = new VisualElement { pickingMode = PickingMode.Ignore };
            playRangeOverlay.AddToClassList("lightside-timeline__play-range");
            playRangeOverlay.style.display = DisplayStyle.None;
            content.Add(playRangeOverlay);

            endMarker = new VisualElement { pickingMode = PickingMode.Ignore };
            endMarker.AddToClassList("lightside-timeline__end-marker");
            content.Add(endMarker);

            durationHandle = new VisualElement();
            durationHandle.AddToClassList("lightside-timeline__duration-handle");
            durationHandle.tooltip = "Sequence duration";
            durationHandle.RegisterCallback<PointerDownEvent>(OnDurationDown);
            durationHandle.RegisterCallback<PointerMoveEvent>(OnDurationMove);
            durationHandle.RegisterCallback<PointerUpEvent>(OnDurationUp);
            content.Add(durationHandle);

            snapGuide = new VisualElement { pickingMode = PickingMode.Ignore };
            snapGuide.AddToClassList("lightside-timeline__snap-guide");
            snapGuide.style.display = DisplayStyle.None;
            content.Add(snapGuide);

            playhead = new VisualElement { pickingMode = PickingMode.Ignore };
            playhead.AddToClassList("lightside-timeline__playhead");
            content.Add(playhead);

            marquee = new VisualElement { pickingMode = PickingMode.Ignore };
            marquee.AddToClassList("lightside-timeline__marquee");
            marquee.style.display = DisplayStyle.None;
            content.Add(marquee);

            dragReadout = new Label { pickingMode = PickingMode.Ignore };
            dragReadout.AddToClassList("lightside-timeline__drag-readout");
            dragReadout.style.display = DisplayStyle.None;
            content.Add(dragReadout);

            emptyState = new VisualElement { pickingMode = PickingMode.Ignore };
            emptyState.AddToClassList("lightside-timeline__empty");
            var emptyIcon = EditorResources.CreateIcon("timeline");
            emptyIcon.AddToClassList("lightside-timeline__empty-icon");
            emptyState.Add(emptyIcon);
            var emptyLabel = new Label("No timeline source.");
            emptyLabel.AddToClassList("lightside-timeline__empty-label");
            emptyState.Add(emptyLabel);
            Add(emptyState);

            var scrollerRow = new VisualElement();
            scrollerRow.AddToClassList("lightside-timeline__scroller-row");
            var spacer = new VisualElement();
            spacer.style.width = HeaderWidth;
            scrollerRow.Add(spacer);
            panBar = new PanBar(start =>
            {
                viewStart = Mathf.Max(0f, start);
                Reposition();
            });
            panBar.AddToClassList("lightside-timeline__pan-bar");
            scrollerRow.Add(panBar);
            Add(scrollerRow);

            RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            content.RegisterCallback<PointerDownEvent>(_ => Focus(),
                TrickleDown.TrickleDown);
            content.RegisterCallback<PointerDownEvent>(OnContentDown);
            content.RegisterCallback<PointerMoveEvent>(OnContentMove);
            content.RegisterCallback<PointerUpEvent>(OnContentUp);
            rulerHost.RegisterCallback<PointerDownEvent>(OnRulerDown);
            rulerHost.RegisterCallback<PointerMoveEvent>(OnRulerMove);
            rulerHost.RegisterCallback<PointerUpEvent>(OnRulerUp);
            rulerHost.RegisterCallback<GeometryChangedEvent>(_ => Reposition());
            schedule.Execute(PollTransport).Every(10);
        }

        /// <summary>Binds the view to a source; null empties it.</summary>
        public void SetSource(ITimelineSource value)
        {
            source = value;
            selected.Clear();
            Refresh();
            if (!RestoreSession()) FitToContent();
        }

        private bool RestoreSession()
        {
            if (PersistenceKey == null) return false;
            var saved = SessionState.GetFloat(PersistenceKey + ".pps", -1f);
            if (saved <= 0f) return false;
            pixelsPerSecond = Mathf.Clamp(saved, MinPixelsPerSecond, MaxPixelsPerSecond);
            viewStart = Mathf.Max(0f, SessionState.GetFloat(PersistenceKey + ".start", 0f));
            rangeEnabled = SessionState.GetBool(PersistenceKey + ".range", false);
            rangeStart = SessionState.GetFloat(PersistenceKey + ".rangeStart", 0f);
            rangeEnd = SessionState.GetFloat(PersistenceKey + ".rangeEnd", 0f);
            Reposition();
            return true;
        }

        private void SaveSession()
        {
            if (PersistenceKey == null) return;
            SessionState.SetFloat(PersistenceKey + ".pps", pixelsPerSecond);
            SessionState.SetFloat(PersistenceKey + ".start", viewStart);
            SessionState.SetBool(PersistenceKey + ".range", rangeEnabled);
            SessionState.SetFloat(PersistenceKey + ".rangeStart", rangeStart);
            SessionState.SetFloat(PersistenceKey + ".rangeEnd", rangeEnd);
        }

        /// <summary>Zooms and pans so the whole content is visible.</summary>
        public void FitToContent()
        {
            if (source == null) return;
            var width = ViewportWidth;
            if (width <= 0f)
            {
                schedule.Execute(FitToContent);
                return;
            }
            var extent = Mathf.Max(ContentEnd, 1f) + TailSeconds;
            pixelsPerSecond = Mathf.Clamp(width / extent, MinPixelsPerSecond, MaxPixelsPerSecond);
            viewStart = 0f;
            Reposition();
        }

        /// <summary>Rebuilds tracks and clips from the source; deferred while a drag is in flight.</summary>
        public void Refresh()
        {
            if (refreshSuppressed) return;
            var hasSource = source != null;
            emptyState.style.display = hasSource ? DisplayStyle.None : DisplayStyle.Flex;
            content.style.display = hasSource ? DisplayStyle.Flex : DisplayStyle.None;
            panBar.parent.style.display = hasSource ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasSource) return;

            var tracks = source.TrackCount;
            while (trackRows.Count > tracks)
            {
                var last = trackRows.Count - 1;
                trackRows[last].RemoveFromHierarchy();
                trackRows.RemoveAt(last);
                lanes.RemoveAt(last);
            }
            while (trackRows.Count < tracks)
                AppendTrackRow();

            for (var track = 0; track < tracks; track++)
            {
                var row = trackRows[track];
                row.EnableInClassList("lightside-timeline__track-row--odd", (track & 1) == 1);
                row.Q<Label>(className: "lightside-timeline__track-label").text =
                    source.TrackLabel(track);
                var lane = lanes[track];
                clipScratch.Clear();
                source.CollectClips(track, clipScratch);
                row.Q<Label>(className: "lightside-timeline__track-count").text =
                    clipScratch.Count.ToString();
                if (LaneShows(lane, clipScratch)) continue;
                lane.Clear();
                for (var i = 0; i < clipScratch.Count; i++)
                    lane.Add(new ClipElement(this, clipScratch[i], track));
            }
            RefreshSelectionVisuals();
            addTrackRow.style.display = source.CanEditTracks
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            addTrackRow.BringToFront();
            Reposition();
        }

        /// <summary>
        /// Whether a lane's elements already present exactly these clips, so their teardown and
        /// rebuild can be skipped. Every field <see cref="ClipElement"/> builds itself from belongs
        /// in the comparison — one left out leaves an element showing state that no longer exists.
        /// Selection is not among them: <see cref="RefreshSelectionVisuals"/> owns it and runs over
        /// retained and rebuilt lanes alike.
        /// </summary>
        private static bool LaneShows(VisualElement lane, List<TimelineClipInfo> clips)
        {
            if (lane.childCount != clips.Count) return false;
            for (var i = 0; i < clips.Count; i++)
                if (lane.ElementAt(i) is not ClipElement element || !Shows(element.Info, clips[i]))
                    return false;
            return true;
        }

        private static bool Shows(in TimelineClipInfo shown, in TimelineClipInfo clip)
            => ReferenceEquals(shown.id, clip.id) && shown.start == clip.start &&
               shown.duration == clip.duration && shown.minDuration == clip.minDuration &&
               shown.muted == clip.muted && ReferenceEquals(shown.accentKey, clip.accentKey) &&
               shown.icon == clip.icon &&
               string.Equals(shown.label, clip.label, StringComparison.Ordinal);

        private void AppendTrackRow()
        {
            var row = new VisualElement();
            row.AddToClassList("lightside-timeline__track-row");
            row.style.height = TrackHeight;
            var header = new VisualElement();
            header.AddToClassList("lightside-timeline__track-header");
            header.style.width = HeaderWidth;
            var label = new Label();
            label.AddToClassList("lightside-timeline__track-label");
            header.Add(label);
            var count = InspectorVisuals.CreateCountChip();
            count.AddToClassList("lightside-timeline__track-count");
            header.Add(count);
            header.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1 || source == null) return;
                ShowTrackMenu(trackRows.IndexOf(row), evt.position);
                evt.StopPropagation();
            });
            row.Add(header);
            var lane = new VisualElement();
            lane.AddToClassList("lightside-timeline__lane");
            lane.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (source == null) return;
                var track = trackRows.IndexOf(row);
                var time = Snap(TimeAt(lane.WorldToLocal(evt.position).x), false, out _);
                if (evt.button == 1)
                {
                    ShowLaneMenu(track, time, evt.position);
                    evt.StopPropagation();
                    return;
                }
                if (evt.button != 0 || evt.clickCount != 2) return;
                CreateClip(track, time);
                evt.StopPropagation();
            });
            row.Add(lane);
            body.Add(row);
            trackRows.Add(row);
            lanes.Add(lane);
        }

        private float ViewportWidth => rulerHost.resolvedStyle.width is var w && !float.IsNaN(w)
            ? w
            : 0f;

        private float ContentEnd
        {
            get
            {
                var end = source?.ContentDuration ?? 0f;
                for (var track = 0; track < lanes.Count; track++)
                    foreach (var child in lanes[track].Children())
                        if (child is ClipElement clip)
                            end = Mathf.Max(end, clip.Start + clip.Duration);
                return end;
            }
        }

        private void Reposition()
        {
            ruler.MarkDirtyRepaint();
            ruler.SyncLabels();
            for (var track = 0; track < lanes.Count; track++)
                foreach (var child in lanes[track].Children())
                    (child as ClipElement)?.Reposition();
            PositionMarker(playhead, playheadTime, source?.Transport != null);
            var contentDuration = source?.ContentDuration ?? 0f;
            var canEditDuration = source is { CanEditDuration: true };
            PositionMarker(endMarker, contentDuration,
                source != null && (contentDuration > 0f || canEditDuration));
            var endX = (contentDuration - viewStart) * pixelsPerSecond;
            durationHandle.style.display =
                canEditDuration && endX >= -4f && endX <= ViewportWidth + 4f
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            durationHandle.style.left = HeaderWidth + endX - 3f;
            if (!float.IsNaN(snapGuideTime) && dragActive)
                PositionMarker(snapGuide, snapGuideTime, true);
            RepositionPlayRange();
            SyncScroller();
            SaveSession();
        }

        private void RepositionPlayRange()
        {
            if (!rangeEnabled || source?.Transport == null)
            {
                playRangeOverlay.style.display = DisplayStyle.None;
                return;
            }
            var width = ViewportWidth;
            var left = Mathf.Clamp((rangeStart - viewStart) * pixelsPerSecond, 0f, width);
            var right = Mathf.Clamp((rangeEnd - viewStart) * pixelsPerSecond, 0f, width);
            playRangeOverlay.style.display = right - left > 0.5f
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            playRangeOverlay.style.left = HeaderWidth + left;
            playRangeOverlay.style.width = right - left;
        }

        private void PositionMarker(VisualElement marker, float time, bool enabled)
        {
            var x = (time - viewStart) * pixelsPerSecond;
            var visible = enabled && x >= 0f && x <= ViewportWidth;
            marker.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            marker.style.left = HeaderWidth + x;
        }

        private void PollTransport()
        {
            var transport = source?.Transport;
            if (transport == null) return;
            var time = transport.Time;
            if (rangeEnabled && transport.IsPlaying &&
                (time >= rangeEnd || time < rangeStart - 0.001f))
            {
                transport.Time = rangeStart;
                time = rangeStart;
            }
            if (Mathf.Approximately(time, playheadTime)) return;
            playheadTime = time;
            if (transport.IsPlaying &&
                (playheadTime - viewStart) * pixelsPerSecond > ViewportWidth)
            {
                viewStart = playheadTime;
                Reposition();
                return;
            }
            PositionMarker(playhead, playheadTime, true);
            ruler.MarkDirtyRepaint();
        }

        private void SyncScroller()
        {
            var viewportSeconds = ViewportWidth / pixelsPerSecond;
            var total = Mathf.Max(ContentEnd + TailSeconds, viewStart + viewportSeconds);
            panBar.SetRange(viewStart, viewportSeconds, total);
        }

        private float TimeAt(float laneLocalX)
            => Mathf.Max(0f, viewStart + laneLocalX / pixelsPerSecond);

        private float Snap(float time, bool bypass, out bool snapped)
        {
            snapped = false;
            if (!SnapEnabled || bypass) return Mathf.Max(0f, time);
            var threshold = SnapPixels / pixelsPerSecond;
            var best = float.MaxValue;
            var result = time;
            void Consider(float candidate)
            {
                var distance = Mathf.Abs(candidate - time);
                if (distance < threshold && distance < best)
                {
                    best = distance;
                    result = candidate;
                }
            }
            Consider(0f);
            if (source?.Transport != null) Consider(playheadTime);
            for (var i = 0; i < snapEdges.Count; i++) Consider(snapEdges[i]);
            if (best < float.MaxValue)
            {
                snapped = true;
                return Mathf.Max(0f, result);
            }
            var step = ruler.MinorStep;
            if (step > 0f) result = Mathf.Round(time / step) * step;
            return Mathf.Max(0f, result);
        }

        private void CollectSnapEdges(object excludedClip)
        {
            snapEdges.Clear();
            if (source == null) return;
            for (var track = 0; track < source.TrackCount; track++)
            {
                clipScratch.Clear();
                source.CollectClips(track, clipScratch);
                for (var i = 0; i < clipScratch.Count; i++)
                {
                    if (Equals(clipScratch[i].id, excludedClip) ||
                        selected.Contains(clipScratch[i].id)) continue;
                    snapEdges.Add(clipScratch[i].start);
                    snapEdges.Add(clipScratch[i].start + clipScratch[i].duration);
                }
            }
        }

        private void OnWheel(WheelEvent evt)
        {
            if (source == null) return;
            var horizontal = Mathf.Abs(evt.delta.x) > Mathf.Abs(evt.delta.y);
            if (evt.ctrlKey || evt.commandKey)
            {
                ZoomAt(rulerHost.WorldToLocal(evt.mousePosition).x,
                    Mathf.Exp(-evt.delta.y * 0.06f));
                evt.StopPropagation();
                return;
            }
            if (evt.shiftKey || horizontal)
            {
                var delta = horizontal ? evt.delta.x : evt.delta.y;
                viewStart = Mathf.Max(0f, viewStart + delta * 12f / pixelsPerSecond);
                Reposition();
                evt.StopPropagation();
            }
        }

        private void ZoomAt(float pointerX, float factor)
        {
            var timeUnderPointer = viewStart + pointerX / pixelsPerSecond;
            pixelsPerSecond = Mathf.Clamp(pixelsPerSecond * factor,
                MinPixelsPerSecond, MaxPixelsPerSecond);
            viewStart = Mathf.Max(0f, timeUnderPointer - pointerX / pixelsPerSecond);
            Reposition();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (source == null) return;
            var handled = true;
            switch (evt.keyCode)
            {
                case KeyCode.A when InspectorGestures.Action(evt.modifiers):
                    SelectAll();
                    break;
                case KeyCode.F:
                case KeyCode.A:
                    FitToContent();
                    break;
                case KeyCode.Home:
                    viewStart = 0f;
                    Reposition();
                    break;
                case KeyCode.Space:
                {
                    var transport = source.Transport;
                    if (transport == null) handled = false;
                    else if (transport.IsPlaying) transport.Pause();
                    else transport.Play();
                    break;
                }
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    DeleteSelection();
                    break;
                case KeyCode.D when InspectorGestures.Action(evt.modifiers):
                    DuplicateSelection();
                    break;
                case KeyCode.Escape:
                    ClearSelection();
                    break;
                case KeyCode.M:
                    ToggleMuteSelection();
                    break;
                case KeyCode.S:
                    SplitSelection();
                    break;
                case KeyCode.C when InspectorGestures.Action(evt.modifiers):
                    CopySelection();
                    break;
                case KeyCode.V when InspectorGestures.Action(evt.modifiers):
                    PasteAtPlayhead();
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    OpenSelectionEditor();
                    break;
                case KeyCode.LeftArrow:
                    NudgeSelection(-(evt.shiftKey ? ruler.MinorStep * 5f : ruler.MinorStep));
                    break;
                case KeyCode.RightArrow:
                    NudgeSelection(evt.shiftKey ? ruler.MinorStep * 5f : ruler.MinorStep);
                    break;
                case KeyCode.Equals:
                case KeyCode.KeypadPlus:
                    ZoomAt(ViewportWidth * 0.5f, 1.25f);
                    break;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    ZoomAt(ViewportWidth * 0.5f, 0.8f);
                    break;
                default:
                    handled = false;
                    break;
            }
            if (handled) evt.StopPropagation();
        }

        private void DeleteSelection()
        {
            if (selected.Count == 0) return;
            source.RemoveClips(new List<object>(selected));
            selected.Clear();
            Refresh();
        }

        private void DuplicateSelection()
        {
            var members = CollectSelectedElements();
            if (members.Count == 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Duplicate Clips");
            var group = Undo.GetCurrentGroup();
            var ids = new List<object>(members.Count);
            foreach (var member in members) ids.Add(member.Id);
            var copies = source.DuplicateClips(ids);
            selected.Clear();
            for (var i = 0; i < copies.Count; i++)
            {
                if (copies[i] == null) continue;
                source.SetClipTiming(copies[i], members[i].Track,
                    members[i].Start + members[i].Duration, members[i].Duration);
                selected.Add(copies[i]);
            }
            Undo.CollapseUndoOperations(group);
            Refresh();
        }

        private void NudgeSelection(float delta)
        {
            if (selected.Count == 0 || delta == 0f) return;
            var members = CollectSelectedElements();
            if (delta < 0f)
                foreach (var member in members)
                    delta = Mathf.Max(delta, -member.Start);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Nudge Clips");
            var group = Undo.GetCurrentGroup();
            foreach (var member in members)
                source.SetClipTiming(member.Id, member.Track,
                    member.Start + delta, member.Duration);
            Undo.CollapseUndoOperations(group);
            Refresh();
        }

        /// <summary>Mutes the selection when any member still plays, otherwise unmutes it all.</summary>
        private void ToggleMuteSelection()
        {
            var members = CollectSelectedElements();
            if (members.Count == 0) return;
            var mute = false;
            foreach (var member in members)
                if (!member.Muted)
                {
                    mute = true;
                    break;
                }
            SetSelectionMuted(mute);
        }

        private void SetSelectionMuted(bool muted)
        {
            var members = CollectSelectedElements();
            if (members.Count == 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(muted ? "Mute Clips" : "Unmute Clips");
            var group = Undo.GetCurrentGroup();
            foreach (var member in members)
                source.SetClipMuted(member.Id, muted);
            Undo.CollapseUndoOperations(group);
            Refresh();
        }

        private void SplitSelection()
        {
            var members = CollectSelectedElements();
            if (members.Count == 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Split Clips");
            var group = Undo.GetCurrentGroup();
            foreach (var member in members)
                source.SplitClip(member.Id, playheadTime);
            Undo.CollapseUndoOperations(group);
            Refresh();
        }

        private void CopySelection()
        {
            var members = CollectSelectedElements();
            if (members.Count == 0) return;
            var ids = new List<object>(members.Count);
            foreach (var member in members) ids.Add(member.Id);
            source.CopyClips(ids);
        }

        private void PasteAtPlayhead()
        {
            var members = CollectSelectedElements();
            PasteClips(members.Count > 0 ? members[0].Track : 0, playheadTime);
        }

        private void PasteClips(int track, float time)
        {
            if (!source.CanPasteClip) return;
            var pasted = source.PasteClips(track, time);
            selected.Clear();
            for (var i = 0; i < pasted.Count; i++)
                if (pasted[i] != null)
                    selected.Add(pasted[i]);
            Refresh();
        }

        private void OpenSelectionEditor()
        {
            var members = CollectSelectedElements();
            if (members.Count == 0) return;
            if (members.Count == 1) OpenClipEditor(members[0]);
            else OpenClipsEditor(members[0]);
        }

        private List<ClipElement> CollectSelectedElements()
        {
            var result = new List<ClipElement>(selected.Count);
            for (var track = 0; track < lanes.Count; track++)
                foreach (var child in lanes[track].Children())
                    if (child is ClipElement clip && selected.Contains(clip.Id))
                        result.Add(clip);
            return result;
        }

        private void ClearSelection()
        {
            if (selected.Count == 0) return;
            selected.Clear();
            RefreshSelectionVisuals();
        }

        private void SelectAll()
        {
            for (var track = 0; track < lanes.Count; track++)
                foreach (var child in lanes[track].Children())
                    if (child is ClipElement clip)
                        selected.Add(clip.Id);
            RefreshSelectionVisuals();
        }

        private void RefreshSelectionVisuals()
        {
            for (var track = 0; track < lanes.Count; track++)
                foreach (var child in lanes[track].Children())
                    if (child is ClipElement clip)
                        clip.SetSelected(selected.Contains(clip.Id));
        }

        private Vector2 panOrigin;
        private float panViewStart;
        private bool panning;

        private void OnContentDown(PointerDownEvent evt)
        {
            if (evt.button == 2 || (evt.button == 0 && evt.altKey))
            {
                panning = true;
                panOrigin = evt.position;
                panViewStart = viewStart;
                content.CapturePointer(evt.pointerId);
                evt.StopPropagation();
                return;
            }
            if (evt.button != 0 || evt.clickCount != 1) return;
            if (evt.target is not VisualElement element || !IsEmptySurface(element)) return;
            marqueeArmed = true;
            marqueeActive = false;
            marqueeAdditive = InspectorGestures.Additive(evt.modifiers);
            marqueeOrigin = content.WorldToLocal(evt.position);
            content.CapturePointer(evt.pointerId);
        }

        /// <summary>
        /// Whether a press landed on empty timeline surface — anything that is not a clip, a track
        /// header, the add-track control or the ruler.
        /// </summary>
        private static bool IsEmptySurface(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
            {
                if (current is ClipElement ||
                    current.ClassListContains("lightside-timeline__track-header") ||
                    current.ClassListContains("lightside-timeline__add-track") ||
                    current.ClassListContains("lightside-timeline__ruler-host")) return false;
                if (current.ClassListContains("lightside-timeline__content")) return true;
            }
            return false;
        }

        private void OnContentMove(PointerMoveEvent evt)
        {
            if (panning && content.HasPointerCapture(evt.pointerId))
            {
                var dx = evt.position.x - panOrigin.x;
                viewStart = Mathf.Max(0f, panViewStart - dx / pixelsPerSecond);
                Reposition();
                return;
            }
            if (!marqueeArmed || !content.HasPointerCapture(evt.pointerId)) return;
            var local = content.WorldToLocal(evt.position);
            if (!marqueeActive)
            {
                if (!InspectorGestures.ExceedsDragThreshold(local - marqueeOrigin)) return;
                marqueeActive = true;
                marquee.style.display = DisplayStyle.Flex;
            }
            marqueeEnd = local;
            var min = Vector2.Min(marqueeOrigin, local);
            var max = Vector2.Max(marqueeOrigin, local);
            marquee.style.left = min.x;
            marquee.style.top = min.y;
            marquee.style.width = max.x - min.x;
            marquee.style.height = max.y - min.y;
        }

        private void OnContentUp(PointerUpEvent evt)
        {
            if (panning)
            {
                panning = false;
                if (content.HasPointerCapture(evt.pointerId))
                    content.ReleasePointer(evt.pointerId);
                return;
            }
            if (!marqueeArmed) return;
            marqueeArmed = false;
            if (content.HasPointerCapture(evt.pointerId))
                content.ReleasePointer(evt.pointerId);
            if (!marqueeActive)
            {
                if (!marqueeAdditive) ClearSelection();
                return;
            }
            marqueeActive = false;
            marquee.style.display = DisplayStyle.None;
            if (!marqueeAdditive) selected.Clear();
            var min = Vector2.Min(marqueeOrigin, marqueeEnd);
            var max = Vector2.Max(marqueeOrigin, marqueeEnd);
            var rect = content.LocalToWorld(
                new Rect(min.x, min.y, max.x - min.x, max.y - min.y));
            for (var track = 0; track < lanes.Count; track++)
            {
                if (!lanes[track].worldBound.Overlaps(rect)) continue;
                foreach (var child in lanes[track].Children())
                    if (child is ClipElement clip && clip.worldBound.Overlaps(rect))
                        selected.Add(clip.Id);
            }
            RefreshSelectionVisuals();
        }

        private bool durationDragging;
        private int durationUndoGroup = -1;

        private void OnDurationDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || source == null || !source.CanEditDuration) return;
            durationDragging = true;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Change Duration");
            durationUndoGroup = Undo.GetCurrentGroup();
            CollectSnapEdges(null);
            durationHandle.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnDurationMove(PointerMoveEvent evt)
        {
            if (!durationDragging || !durationHandle.HasPointerCapture(evt.pointerId)) return;
            var localX = rulerHost.WorldToLocal(evt.position).x;
            var time = Snap(Mathf.Max(0f, viewStart + localX / pixelsPerSecond),
                evt.altKey, out _);
            source.SetContentDuration(time);
            ShowDragReadout(evt.position, time.ToString("0.00") + "s");
            Reposition();
        }

        private void OnDurationUp(PointerUpEvent evt)
        {
            if (!durationDragging) return;
            durationDragging = false;
            if (durationHandle.HasPointerCapture(evt.pointerId))
                durationHandle.ReleasePointer(evt.pointerId);
            dragReadout.style.display = DisplayStyle.None;
            Undo.CollapseUndoOperations(durationUndoGroup);
            Refresh();
        }

        private bool scrubbing;

        private void OnRulerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || source?.Transport == null) return;
            if (evt.shiftKey)
            {
                rangeSelecting = true;
                rangeEnabled = false;
                rangeAnchor = Mathf.Max(0f, viewStart + evt.localPosition.x / pixelsPerSecond);
                rulerHost.CapturePointer(evt.pointerId);
                RepositionPlayRange();
                ruler.MarkDirtyRepaint();
                evt.StopPropagation();
                return;
            }
            scrubbing = true;
            rulerHost.CapturePointer(evt.pointerId);
            ScrubTo(evt.localPosition.x);
            evt.StopPropagation();
        }

        private void OnRulerMove(PointerMoveEvent evt)
        {
            if (!rulerHost.HasPointerCapture(evt.pointerId)) return;
            if (rangeSelecting)
            {
                var time = Mathf.Max(0f, viewStart + evt.localPosition.x / pixelsPerSecond);
                rangeStart = Mathf.Min(rangeAnchor, time);
                rangeEnd = Mathf.Max(rangeAnchor, time);
                rangeEnabled = rangeEnd - rangeStart > 0.01f;
                RepositionPlayRange();
                ruler.MarkDirtyRepaint();
                return;
            }
            if (scrubbing) ScrubTo(evt.localPosition.x);
        }

        private void OnRulerUp(PointerUpEvent evt)
        {
            if (rangeSelecting)
            {
                rangeSelecting = false;
                if (rulerHost.HasPointerCapture(evt.pointerId))
                    rulerHost.ReleasePointer(evt.pointerId);
                RepositionPlayRange();
                ruler.MarkDirtyRepaint();
                SaveSession();
                return;
            }
            if (!scrubbing) return;
            scrubbing = false;
            if (rulerHost.HasPointerCapture(evt.pointerId))
                rulerHost.ReleasePointer(evt.pointerId);
        }

        private void ScrubTo(float localX)
        {
            var transport = source?.Transport;
            if (transport == null) return;
            playheadTime = Mathf.Max(0f, viewStart + localX / pixelsPerSecond);
            transport.Time = playheadTime;
            PositionMarker(playhead, playheadTime, true);
            ruler.MarkDirtyRepaint();
        }

        private static void ShowMenu(Vector2 panelPosition, params Selector.SelectorItem[] items)
            => Selector.Show(new Rect(panelPosition, Vector2.one), items, null,
                value => (value as Action)?.Invoke(), showSearch: false);

        private static Selector.SelectorItem MenuItem(string label, string icon, Action action,
            bool enabled = true) => new()
        {
            displayName = label,
            icon = icon == null ? null : EditorResources.GetTintedTexture(icon),
            disabled = !enabled,
            value = action,
        };

        private void CreateClip(int track, float time)
        {
            var id = source.AddClip(track, time);
            Refresh();
            OpenClipEditorById(id);
        }

        private void OpenClipEditorById(object id)
        {
            if (id == null) return;
            for (var track = 0; track < lanes.Count; track++)
                foreach (var child in lanes[track].Children())
                    if (child is ClipElement clip && Equals(clip.Id, id))
                    {
                        OpenClipEditor(clip);
                        return;
                    }
        }

        private void ShowTrackMenu(int track, Vector2 panelPosition)
        {
            if (!source.CanEditTracks) return;
            ShowMenu(panelPosition,
                MenuItem("Remove Track", "trash", () =>
                {
                    source.RemoveTrack(track);
                    Refresh();
                }, source.CanRemoveTrack(track)));
        }

        private void ShowLaneMenu(int track, float time, Vector2 panelPosition)
            => ShowMenu(panelPosition,
                MenuItem("Add Clip", "plus", () => CreateClip(track, time)),
                MenuItem("Paste", "paste", () => PasteClips(track, time),
                    source.CanPasteClip));

        /// <summary>
        /// Clip commands act on the whole selection; a press on a clip outside it selects that
        /// clip first, so the menu always describes exactly what it will change.
        /// </summary>
        private void ShowClipMenu(ClipElement clip, Vector2 panelPosition)
        {
            if (!selected.Contains(clip.Id))
            {
                selected.Clear();
                selected.Add(clip.Id);
                RefreshSelectionVisuals();
            }
            var count = selected.Count;
            var suffix = count > 1 ? $" {count} Clips" : string.Empty;
            var items = new List<Selector.SelectorItem>
            {
                MenuItem($"Edit{suffix}...", "edit", OpenSelectionEditor),
                MenuItem((clip.Muted ? "Unmute" : "Mute") + suffix,
                    clip.Muted ? "eye" : "eye-off", () => SetSelectionMuted(!clip.Muted)),
                MenuItem("Copy" + suffix, "copy", CopySelection),
                MenuItem("Duplicate" + suffix, "duplicate", DuplicateSelection),
                MenuItem("Split at Playhead", null, SplitSelection,
                    source.Transport != null && AnySelectionSpans(playheadTime)),
            };
            if (source.CanEditTracks)
                items.Add(MenuItem("Move to New Track", "plus", MoveSelectionToNewTrack));
            items.Add(MenuItem("Delete" + suffix, "trash", DeleteSelection));
            ShowMenu(panelPosition, items.ToArray());
        }

        private bool AnySelectionSpans(float time)
        {
            foreach (var member in CollectSelectedElements())
                if (time > member.Start && time < member.Start + member.Duration)
                    return true;
            return false;
        }

        private void MoveSelectionToNewTrack()
        {
            var members = CollectSelectedElements();
            if (members.Count == 0 || !source.CanEditTracks) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Move Clips To New Track");
            var group = Undo.GetCurrentGroup();
            source.AddTrack();
            var track = source.TrackCount - 1;
            foreach (var member in members)
                source.SetClipTiming(member.Id, track, member.Start, member.Duration);
            Undo.CollapseUndoOperations(group);
            Refresh();
        }

        private void OpenClipsEditor(ClipElement anchor)
        {
            var ids = new List<object>(selected);
            anchor.SetOpen(true);
            TimelineClipPopup.Show(anchor.worldBound, () => source.CreateClipsEditor(ids),
                () => anchor.SetOpen(false));
        }

        private void OpenClipEditor(ClipElement clip)
        {
            if (source == null) return;
            clip.SetOpen(true);
            TimelineClipPopup.Show(clip.worldBound, () => source.CreateClipEditor(clip.Id),
                () => clip.SetOpen(false));
        }

        private void BeginClipDrag(ClipElement clip, byte mode, PointerDownEvent evt)
        {
            dragged = clip;
            dragMode = mode;
            dragActive = false;
            dragClickCount = evt.clickCount;
            dragOrigin = evt.position;
            dragStart = clip.Start;
            dragDuration = clip.Duration;
            dragTrack = clip.Track;
            dropTrack = clip.Track;
            clip.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void ActivateClipDrag(ClipElement clip, bool copyRequested)
        {
            dragActive = true;
            refreshSuppressed = true;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Move Clips");
            undoGroup = Undo.GetCurrentGroup();
            if (!selected.Contains(clip.Id) || dragMode != DragMove)
            {
                if (dragMode == DragMove)
                {
                    selected.Clear();
                    selected.Add(clip.Id);
                    RefreshSelectionVisuals();
                }
            }
            dragGroup.Clear();
            if (dragMode == DragMove)
                foreach (var member in CollectSelectedElements())
                    if (!ReferenceEquals(member, clip))
                        dragGroup.Add(new GroupMember(member, member.Start, member.Track));

            if (copyRequested && dragMode == DragMove)
            {
                var ids = new List<object>(dragGroup.Count + 1) { clip.Id };
                foreach (var member in dragGroup) ids.Add(member.element.Id);
                var copies = source.DuplicateClips(ids);
                if (copies.Count == ids.Count && copies[0] != null)
                {
                    SpawnGhost(clip);
                    clip.Rebind(copies[0]);
                    for (var i = 0; i < dragGroup.Count; i++)
                    {
                        if (copies[i + 1] == null) continue;
                        SpawnGhost(dragGroup[i].element);
                        dragGroup[i].element.Rebind(copies[i + 1]);
                    }
                    selected.Clear();
                    foreach (var copy in copies)
                        if (copy != null)
                            selected.Add(copy);
                }
            }
            CollectSnapEdges(clip.Id);
            clip.AddToClassList("lightside-timeline__clip--dragging");
            clip.BringToFront();
        }

        /// <summary>Leaves a non-interactive stand-in where a dragged clip was; the drag's final refresh clears it.</summary>
        private void SpawnGhost(ClipElement clip)
        {
            if ((uint)clip.Track >= (uint)lanes.Count) return;
            var ghost = new ClipElement(this, clip.Info, clip.Track)
            {
                pickingMode = PickingMode.Ignore,
            };
            ghost.AddToClassList("lightside-timeline__clip--ghost");
            lanes[clip.Track].Insert(0, ghost);
        }

        private void UpdateClipDrag(ClipElement clip, PointerMoveEvent evt)
        {
            if (dragged != clip || !clip.HasPointerCapture(evt.pointerId)) return;
            var delta = (Vector2)evt.position - dragOrigin;
            if (!dragActive)
            {
                if (!InspectorGestures.ExceedsDragThreshold(delta)) return;
                ActivateClipDrag(clip, evt.shiftKey);
            }

            AutoPan(evt.position);
            var bypassSnap = evt.altKey;
            var dt = delta.x / pixelsPerSecond;
            snapGuideTime = float.NaN;
            switch (dragMode)
            {
                case DragLeft:
                {
                    var end = dragStart + dragDuration;
                    var start = Snap(dragStart + dt, bypassSnap, out var snapHit);
                    start = Mathf.Min(start, end - clip.MinDuration);
                    if (snapHit) snapGuideTime = start;
                    clip.SetTiming(start, end - start);
                    source.SetClipTiming(clip.Id, dragTrack, start, end - start);
                    ShowDragReadout(evt.position,
                        $"{end - start:0.00}s  @ {start:0.00}s");
                    break;
                }
                case DragRight:
                {
                    var end = Snap(dragStart + dragDuration + dt, bypassSnap, out var snapHit);
                    end = Mathf.Max(end, dragStart + clip.MinDuration);
                    if (snapHit) snapGuideTime = end;
                    clip.SetTiming(dragStart, end - dragStart);
                    source.SetClipTiming(clip.Id, dragTrack, dragStart, end - dragStart);
                    ShowDragReadout(evt.position, $"{end - dragStart:0.00}s");
                    break;
                }
                default:
                {
                    var raw = dragStart + dt;
                    var start = Snap(raw, bypassSnap, out var startHit);
                    var byEnd = Snap(raw + dragDuration, bypassSnap, out var endHit)
                                - dragDuration;
                    if (endHit && (!startHit ||
                                   Mathf.Abs(byEnd - raw) < Mathf.Abs(start - raw)))
                    {
                        start = Mathf.Max(0f, byEnd);
                        snapGuideTime = start + dragDuration;
                    }
                    else if (startHit)
                    {
                        snapGuideTime = start;
                    }
                    var groupDelta = start - dragStart;
                    foreach (var member in dragGroup)
                        groupDelta = Mathf.Max(groupDelta, -member.start);
                    start = dragStart + groupDelta;
                    clip.SetTiming(start, dragDuration);
                    source.SetClipTiming(clip.Id, dragTrack, start, dragDuration);
                    foreach (var member in dragGroup)
                    {
                        member.element.SetTiming(member.start + groupDelta,
                            member.element.Duration);
                        source.SetClipTiming(member.element.Id, member.track,
                            member.start + groupDelta, member.element.Duration);
                    }
                    UpdateDropTrack(evt.position);
                    ShowDragReadout(evt.position, dragGroup.Count > 0
                        ? $"{dragGroup.Count + 1} clips  @ {start:0.00}s"
                        : $"{start:0.00}s");
                    break;
                }
            }
            if (float.IsNaN(snapGuideTime)) snapGuide.style.display = DisplayStyle.None;
            else PositionMarker(snapGuide, snapGuideTime, true);
        }

        private void ShowDragReadout(Vector2 worldPosition, string text)
        {
            var local = content.WorldToLocal(worldPosition);
            dragReadout.style.display = DisplayStyle.Flex;
            dragReadout.style.left = local.x + 14f;
            dragReadout.style.top = local.y - 22f;
            dragReadout.text = text;
        }

        private void AutoPan(Vector2 worldPosition)
        {
            var local = rulerHost.WorldToLocal(worldPosition);
            if (local.x < AutoPanMargin && viewStart > 0f)
                viewStart = Mathf.Max(0f, viewStart - AutoPanStep / pixelsPerSecond);
            else if (local.x > ViewportWidth - AutoPanMargin)
                viewStart += AutoPanStep / pixelsPerSecond;
            else return;
            Reposition();
        }

        private void UpdateDropTrack(Vector2 worldPosition)
        {
            var allowNew = dragGroup.Count == 0 && source.CanEditTracks;
            var target = dragTrack;
            for (var i = 0; i < lanes.Count; i++)
            {
                var bound = lanes[i].worldBound;
                if (worldPosition.y >= bound.yMin && worldPosition.y <= bound.yMax)
                {
                    target = i;
                    break;
                }
            }
            if (allowNew && addTrackRow.style.display == DisplayStyle.Flex &&
                worldPosition.y >= addTrackRow.worldBound.yMin)
                target = lanes.Count;
            if (target == dropTrack) return;
            SetDropHighlight(dropTrack, false);
            dropTrack = target;
            SetDropHighlight(dropTrack, true);
        }

        private void SetDropHighlight(int track, bool value)
        {
            if (track == dragTrack) return;
            if (track == lanes.Count)
                addTrackRow.EnableInClassList("lightside-timeline__add-track-row--drop", value);
            else if (track >= 0 && track < lanes.Count)
                lanes[track].EnableInClassList("lightside-timeline__lane--drop", value);
        }

        private void EndClipDrag(ClipElement clip, PointerUpEvent evt)
        {
            if (dragged != clip) return;
            if (clip.HasPointerCapture(evt.pointerId)) clip.ReleasePointer(evt.pointerId);
            var wasActive = dragActive;
            dragged = null;
            dragActive = false;
            refreshSuppressed = false;
            clip.RemoveFromClassList("lightside-timeline__clip--dragging");
            SetDropHighlight(dropTrack, false);
            snapGuide.style.display = DisplayStyle.None;
            dragReadout.style.display = DisplayStyle.None;

            if (!wasActive)
            {
                if (evt.button != 0) return;
                if (InspectorGestures.Action(evt.modifiers))
                {
                    if (!selected.Add(clip.Id)) selected.Remove(clip.Id);
                    RefreshSelectionVisuals();
                    return;
                }
                if (dragClickCount > 1)
                {
                    if (selected.Count > 1 && selected.Contains(clip.Id))
                    {
                        OpenClipsEditor(clip);
                        return;
                    }
                    selected.Clear();
                    selected.Add(clip.Id);
                    RefreshSelectionVisuals();
                    OpenClipEditor(clip);
                    return;
                }
                selected.Clear();
                selected.Add(clip.Id);
                RefreshSelectionVisuals();
                return;
            }

            var targetTrack = dropTrack;
            if (targetTrack == lanes.Count)
            {
                source.AddTrack();
                targetTrack = source.TrackCount - 1;
            }
            var trackDelta = targetTrack - dragTrack;
            source.SetClipTiming(clip.Id, targetTrack, clip.Start, clip.Duration);
            foreach (var member in dragGroup)
                source.SetClipTiming(member.element.Id,
                    Mathf.Clamp(member.element.Track + trackDelta, 0, source.TrackCount - 1),
                    member.element.Start, member.element.Duration);
            Undo.CollapseUndoOperations(undoGroup);
            dragGroup.Clear();
            Refresh();
        }

        /// <summary>
        /// Horizontal pan bar: a thumb whose size reports the visible share of the content and
        /// whose position reports the viewport start. Deliberately not the engine scroller — that
        /// one carries the inspector's slider chrome, sized and animated for form controls.
        /// </summary>
        private sealed class PanBar : VisualElement
        {
            private const float MinThumbWidth = 24f;

            private readonly VisualElement thumb;
            private readonly Action<float> changed;
            private float start;
            private float viewport = 1f;
            private float total = 1f;
            private float grabOffset;

            internal PanBar(Action<float> changed)
            {
                this.changed = changed;
                thumb = new VisualElement();
                thumb.AddToClassList("lightside-timeline__pan-thumb");
                Add(thumb);
                InspectorGestures.HoldPressedState(thumb);
                RegisterCallback<GeometryChangedEvent>(_ => Reposition());
                thumb.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    grabOffset = evt.position.x - thumb.worldBound.x;
                    thumb.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                });
                thumb.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (!thumb.HasPointerCapture(evt.pointerId)) return;
                    MoveThumbTo(evt.position.x - worldBound.x - grabOffset);
                });
                thumb.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (thumb.HasPointerCapture(evt.pointerId))
                        thumb.ReleasePointer(evt.pointerId);
                });
                RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || evt.target == thumb) return;
                    MoveThumbTo(evt.localPosition.x - ThumbWidth * 0.5f);
                });
            }

            private float TrackWidth => resolvedStyle.width is var w && !float.IsNaN(w) ? w : 0f;

            private float ThumbWidth => total <= 0f
                ? TrackWidth
                : Mathf.Max(MinThumbWidth, TrackWidth * Mathf.Clamp01(viewport / total));

            /// <summary>Reports the visible window over the content, all in source units.</summary>
            internal void SetRange(float viewStart, float viewportSize, float totalSize)
            {
                start = viewStart;
                viewport = Mathf.Max(0.0001f, viewportSize);
                total = Mathf.Max(viewport, totalSize);
                Reposition();
            }

            private void MoveThumbTo(float left)
            {
                var track = TrackWidth;
                var width = ThumbWidth;
                var span = Mathf.Max(0f, track - width);
                var ratio = span <= 0f ? 0f : Mathf.Clamp01(left / span);
                changed(ratio * Mathf.Max(0f, total - viewport));
            }

            private void Reposition()
            {
                var track = TrackWidth;
                if (track <= 0f) return;
                var width = ThumbWidth;
                var span = Mathf.Max(0f, total - viewport);
                var ratio = span <= 0f ? 0f : Mathf.Clamp01(start / span);
                thumb.style.width = width;
                thumb.style.left = ratio * Mathf.Max(0f, track - width);
            }
        }

        /// <summary>Adaptive tick ruler; labels ride pooled children, ticks paint directly.</summary>
        private sealed class Ruler : VisualElement
        {
            private readonly TimelineView view;
            private readonly List<Label> labels = new();

            internal float MinorStep { get; private set; } = 0.1f;

            internal Ruler(TimelineView view)
            {
                this.view = view;
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            private float MajorStep => AxisScale.NiceStep(80f / view.pixelsPerSecond);

            internal void SyncLabels()
            {
                var width = resolvedStyle.width;
                if (float.IsNaN(width) || width <= 0f) return;
                var major = MajorStep;
                MinorStep = major / 5f;
                var first = Mathf.Floor(view.viewStart / major) * major;
                var count = Mathf.CeilToInt(width / (major * view.pixelsPerSecond)) + 1;
                while (labels.Count < count)
                {
                    var label = new Label { pickingMode = PickingMode.Ignore };
                    label.AddToClassList("lightside-timeline__ruler-label");
                    Add(label);
                    labels.Add(label);
                }
                for (var i = 0; i < labels.Count; i++)
                {
                    var label = labels[i];
                    if (i >= count)
                    {
                        label.style.display = DisplayStyle.None;
                        continue;
                    }
                    var time = first + i * major;
                    var x = (time - view.viewStart) * view.pixelsPerSecond;
                    label.style.display = time < 0f || x > width
                        ? DisplayStyle.None
                        : DisplayStyle.Flex;
                    label.style.left = x + 4f;
                    label.text = FormatTime(time, major);
                }
            }

            private static string FormatTime(float time, float step)
                => step >= 1f
                    ? time.ToString("0") + "s"
                    : time.ToString(step >= 0.1f ? "0.0" : "0.00") + "s";

            private void Draw(MeshGenerationContext context)
            {
                var rect = contentRect;
                if (rect.width <= 0f) return;
                var painter = context.painter2D;
                if (view.source?.Transport != null)
                {
                    var accent = EditorResources.ToggleAccent;
                    var playedX = Mathf.Clamp(
                        (view.playheadTime - view.viewStart) * view.pixelsPerSecond,
                        0f, rect.width);
                    if (playedX > 0f)
                    {
                        accent.a = 0.06f;
                        painter.fillColor = accent;
                        painter.BeginPath();
                        FillRect(painter, new Rect(rect.x, rect.y, playedX, rect.height));
                        painter.Fill();
                    }
                    if (view.rangeEnabled)
                    {
                        var left = Mathf.Clamp(
                            (view.rangeStart - view.viewStart) * view.pixelsPerSecond,
                            0f, rect.width);
                        var right = Mathf.Clamp(
                            (view.rangeEnd - view.viewStart) * view.pixelsPerSecond,
                            0f, rect.width);
                        if (right > left)
                        {
                            accent.a = 0.16f;
                            painter.fillColor = accent;
                            painter.BeginPath();
                            FillRect(painter,
                                new Rect(rect.x + left, rect.y, right - left, rect.height));
                            painter.Fill();
                        }
                    }
                }
                var major = MajorStep;
                var minor = major / 5f;
                var majorColor = EditorResources.IconColor;
                majorColor.a *= 0.8f;
                var minorColor = majorColor;
                minorColor.a *= 0.35f;

                painter.lineWidth = 1f;
                painter.strokeColor = minorColor;
                painter.BeginPath();
                DrawTicks(painter, rect, minor, rect.height - 6f, major);
                painter.Stroke();

                painter.strokeColor = majorColor;
                painter.BeginPath();
                DrawTicks(painter, rect, major, rect.height - 11f, 0f);
                painter.Stroke();

                if (view.source?.Transport != null)
                {
                    var x = (view.playheadTime - view.viewStart) * view.pixelsPerSecond + 1f;
                    if (x >= 0f && x <= rect.width)
                    {
                        painter.fillColor = EditorResources.ToggleAccent;
                        painter.BeginPath();
                        painter.Arc(new Vector2(x, rect.y + 7f), 4f, 0f, 360f);
                        painter.Fill();
                    }
                }
            }

            private static void FillRect(Painter2D painter, Rect rect)
            {
                painter.MoveTo(new Vector2(rect.xMin, rect.yMin));
                painter.LineTo(new Vector2(rect.xMax, rect.yMin));
                painter.LineTo(new Vector2(rect.xMax, rect.yMax));
                painter.LineTo(new Vector2(rect.xMin, rect.yMax));
                painter.ClosePath();
            }

            private void DrawTicks(Painter2D painter, Rect rect, float step, float top,
                float skipMultiple)
            {
                if (step <= 0f) return;
                var first = Mathf.Max(0f, Mathf.Floor(view.viewStart / step) * step);
                for (var time = first; ; time += step)
                {
                    var x = (time - view.viewStart) * view.pixelsPerSecond;
                    if (x > rect.width) break;
                    if (x < 0f) continue;
                    if (skipMultiple > 0f &&
                        Mathf.Abs(Mathf.Round(time / skipMultiple) * skipMultiple - time) <
                        step * 0.25f) continue;
                    painter.MoveTo(new Vector2(x, rect.y + top));
                    painter.LineTo(new Vector2(x, rect.yMax));
                }
            }
        }

        /// <summary>One clip chip: accent-tinted card with icon and label, drag body plus resize edges.</summary>
        private sealed class ClipElement : VisualElement
        {
            private readonly TimelineView view;
            private readonly Color accent;
            private bool hovered;
            private bool open;
            private bool isSelected;

            internal object Id { get; private set; }
            internal float Start { get; private set; }
            internal float Duration { get; private set; }
            internal float MinDuration { get; }
            internal int Track { get; }
            internal bool Muted { get; }
            internal TimelineClipInfo Info { get; }

            internal ClipElement(TimelineView view, in TimelineClipInfo info, int track)
            {
                this.view = view;
                Info = info;
                Id = info.id;
                Start = info.start;
                Duration = info.duration;
                MinDuration = Mathf.Max(0.01f, info.minDuration);
                Track = track;
                Muted = info.muted;
                AddToClassList("lightside-timeline__clip");
                EnableInClassList("lightside-timeline__clip--muted", info.muted);

                accent = info.accentKey != null
                    ? EditorResources.GetAccentColor(info.accentKey, info.label)
                    : EditorResources.IconColor;
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList("lightside-timeline__clip-bar");
                bar.style.backgroundColor = accent;
                Add(bar);
                if (info.icon != null)
                {
                    var icon = EditorResources.CreateIcon(info.icon);
                    icon.AddToClassList("lightside-timeline__clip-icon");
                    Add(icon);
                }
                var label = new Label(info.label) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("lightside-timeline__clip-label");
                Add(label);

                var left = new VisualElement();
                left.AddToClassList("lightside-timeline__clip-edge");
                left.AddToClassList("lightside-timeline__clip-edge--left");
                left.style.width = EdgeGrabWidth;
                Add(left);
                var right = new VisualElement();
                right.AddToClassList("lightside-timeline__clip-edge");
                right.AddToClassList("lightside-timeline__clip-edge--right");
                right.style.width = EdgeGrabWidth;
                Add(right);

                RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 1)
                    {
                        view.ShowClipMenu(this, evt.position);
                        evt.StopPropagation();
                        return;
                    }
                    if (evt.button != 0) return;
                    view.BeginClipDrag(this, DragMove, evt);
                });
                left.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    view.BeginClipDrag(this, DragLeft, evt);
                });
                right.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0) return;
                    view.BeginClipDrag(this, DragRight, evt);
                });
                RegisterCallback<PointerMoveEvent>(evt => view.UpdateClipDrag(this, evt));
                RegisterCallback<PointerUpEvent>(evt => view.EndClipDrag(this, evt));
                RegisterCallback<PointerEnterEvent>(_ =>
                {
                    hovered = true;
                    UpdateChrome();
                });
                RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    hovered = false;
                    UpdateChrome();
                });
                tooltip = info.label;
                UpdateChrome();
                Reposition();
            }

            internal void SetTiming(float start, float duration)
            {
                Start = start;
                Duration = duration;
                Reposition();
            }

            internal void SetOpen(bool value)
            {
                open = value;
                UpdateChrome();
            }

            /// <summary>Redirects the element at a fresh identity — the drag-copy gesture drags the copy.</summary>
            internal void Rebind(object id) => Id = id;

            internal void SetSelected(bool value)
            {
                isSelected = value;
                UpdateChrome();
            }

            private void UpdateChrome()
            {
                var strong = isSelected || open;
                var fill = accent;
                fill.a = strong ? 0.26f : hovered ? 0.20f : 0.13f;
                var border = accent;
                border.a = strong ? 0.95f : hovered ? 0.60f : 0.30f;
                style.backgroundColor = fill;
                style.borderLeftColor = border;
                style.borderRightColor = border;
                style.borderTopColor = border;
                style.borderBottomColor = border;
            }

            internal void Reposition()
            {
                style.left = (Start - view.viewStart) * view.pixelsPerSecond;
                style.width = Mathf.Max(8f, Duration * view.pixelsPerSecond);
            }
        }
    }
}
