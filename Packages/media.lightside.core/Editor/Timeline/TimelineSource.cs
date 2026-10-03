using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>One clip as the timeline presents it; <see cref="id"/> is the stable identity every mutation call refers to.</summary>
    public readonly struct TimelineClipInfo
    {
        /// <summary>Stable identity of the clip within its source.</summary>
        public readonly object id;
        /// <summary>Start of the clip's window, in seconds.</summary>
        public readonly float start;
        /// <summary>Length of the clip's window, in seconds.</summary>
        public readonly float duration;
        /// <summary>Shortest window resizing may produce, in seconds.</summary>
        public readonly float minDuration;
        /// <summary>Label drawn on the clip.</summary>
        public readonly string label;
        /// <summary>Semantic identity colouring the clip, resolved through the shared accent palette.</summary>
        public readonly object accentKey;
        /// <summary>Optional leading icon.</summary>
        public readonly Texture icon;
        /// <summary>Whether the clip is muted — kept but not applied.</summary>
        public readonly bool muted;

        public TimelineClipInfo(object id, float start, float duration, float minDuration,
            string label, object accentKey, Texture icon, bool muted = false)
        {
            this.id = id;
            this.start = start;
            this.duration = duration;
            this.minDuration = minDuration;
            this.label = label;
            this.accentKey = accentKey;
            this.icon = icon;
            this.muted = muted;
        }
    }

    /// <summary>Optional playhead of a timeline source: absolute seconds plus transport control.</summary>
    public interface ITimelineTransport
    {
        /// <summary>Playhead position in seconds; setting it scrubs.</summary>
        float Time { get; set; }
        /// <summary>Whether the playhead is advancing on its own.</summary>
        bool IsPlaying { get; }
        void Play();
        void Pause();
        void Stop();
    }

    /// <summary>
    /// The data a <see cref="TimelineWindow"/> edits: tracks of clips with timing mutations. All
    /// mutations must be undoable by the source's own means; the window refreshes itself after
    /// undo, redo, and any observed object change. All calls arrive on the main thread.
    /// </summary>
    public interface ITimelineSource
    {
        /// <summary>Window title context, e.g. the edited object's name.</summary>
        string Title { get; }
        /// <summary>Length of the authored content in seconds; the ruler and fit extend past it freely.</summary>
        float ContentDuration { get; }
        /// <summary>Whether the authored content duration may be edited.</summary>
        bool CanEditDuration { get; }
        /// <summary>Sets the authored content duration in seconds; clips may still extend past it.</summary>
        void SetContentDuration(float seconds);
        /// <summary>Playhead and transport, or null when the source has none.</summary>
        ITimelineTransport Transport { get; }

        int TrackCount { get; }
        string TrackLabel(int track);
        /// <summary>Appends every clip of one track to <paramref name="results"/>.</summary>
        void CollectClips(int track, List<TimelineClipInfo> results);

        /// <summary>Whether tracks may be added and removed.</summary>
        bool CanEditTracks { get; }
        void AddTrack();
        bool CanRemoveTrack(int track);
        void RemoveTrack(int track);

        /// <summary>Commits one clip's window and track in a single edit.</summary>
        void SetClipTiming(object clipId, int track, float start, float duration);
        /// <summary>Mutes or unmutes one clip; a muted clip is kept but not applied.</summary>
        void SetClipMuted(object clipId, bool muted);
        /// <summary>Creates a clip and returns its identity.</summary>
        object AddClip(int track, float start);
        /// <summary>Removes every listed clip as one edit.</summary>
        void RemoveClips(IReadOnlyList<object> clipIds);
        /// <summary>
        /// Inserts copies in place — same windows, same tracks — as one edit, and returns their
        /// identities in input order. A single edit keeps every returned identity valid; separate
        /// structural edits may invalidate identities issued between them.
        /// </summary>
        IReadOnlyList<object> DuplicateClips(IReadOnlyList<object> clipIds);
        /// <summary>
        /// Splits one clip at an absolute time inside its window into two adjoining clips whose
        /// values meet at the split point; a no-op when the time lies outside the window.
        /// </summary>
        void SplitClip(object clipId, float time);

        /// <summary>Publishes the listed clips to the shared property clipboard as one item.</summary>
        void CopyClips(IReadOnlyList<object> clipIds);
        /// <summary>Whether the shared clipboard holds pasteable clips.</summary>
        bool CanPasteClip { get; }
        /// <summary>
        /// Inserts the newest pasteable clips as one edit, placing the earliest at
        /// <paramref name="start"/> on <paramref name="track"/> and the rest at their original
        /// offsets from it; returns their identities.
        /// </summary>
        IReadOnlyList<object> PasteClips(int track, float start);

        /// <summary>Creates the full settings editor shown in the clip's popup.</summary>
        VisualElement CreateClipEditor(object clipId);
        /// <summary>Creates the shared editor for two or more clips; edits fan out to all of them.</summary>
        VisualElement CreateClipsEditor(IReadOnlyList<object> clipIds);
    }
}
