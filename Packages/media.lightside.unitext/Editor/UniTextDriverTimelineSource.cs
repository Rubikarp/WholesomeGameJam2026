using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Timeline source over a <see cref="UniTextDriver"/>: clips arrange on organizational lanes,
    /// window edits write start, duration and lane through the driver's serialization, and the
    /// clip popup hosts the clip's full inspector drawer.
    /// </summary>
    internal sealed class UniTextDriverTimelineSource : ITimelineSource, IDisposable
    {
        private const float MinMemberDuration = 0.05f;

        private static readonly (string path, string display)[] sharedFields =
        {
            ("start", "Start"),
            ("memberDuration", "Duration"),
            ("stagger", "Stagger"),
            ("easing", "Easing"),
            ("composition", "Composition"),
            ("priority", "Priority"),
            ("disabled", "Muted"),
            ("query.label", "Query Label"),
            ("query.skip", "Skip"),
            ("query.take", "Take"),
        };

        private readonly UniTextDriver driver;
        private readonly SerializedObject serialized;
        private readonly SerializedProperty clips;
        private readonly SerializedProperty tracks;
        private readonly DriverTransport transport;

        [InitializeOnLoadMethod]
        private static void Register()
            => TimelineWindow.RegisterSource<UniTextDriver>(
                driver => new UniTextDriverTimelineSource(driver));

        private UniTextDriverTimelineSource(UniTextDriver driver)
        {
            this.driver = driver;
            serialized = new SerializedObject(driver);
            clips = serialized.FindProperty("clips");
            tracks = serialized.FindProperty("timelineTracks");
            transport = new DriverTransport(driver);
        }

        public void Dispose() => serialized.Dispose();

        public string Title => driver == null ? "" : $"{driver.name} — UniText Driver";

        public float ContentDuration => driver == null ? 0f : driver.TimelineLength;

        public bool CanEditDuration => true;

        public void SetContentDuration(float seconds)
        {
            serialized.Update();
            serialized.FindProperty("duration").floatValue = Mathf.Max(0f, seconds);
            serialized.ApplyModifiedProperties();
        }

        public ITimelineTransport Transport => transport;

        public int TrackCount
        {
            get
            {
                var count = Mathf.Max(1, driver.TimelineTracks);
                var list = driver.Clips;
                for (var i = 0; i < list.Count; i++)
                    if (list[i] != null)
                        count = Mathf.Max(count, list[i].TimelineTrack + 1);
                return count;
            }
        }

        public string TrackLabel(int track) => $"Track {track + 1}";

        public void CollectClips(int track, List<TimelineClipInfo> results)
        {
            var list = driver.Clips;
            UniTextDriverClipDrawer.CollectBindings(driver);
            for (var i = 0; i < list.Count; i++)
            {
                var clip = list[i];
                if (clip == null || clip.TimelineTrack != track) continue;
                var length = driver.ClipLength(clip);
                var staggerTail = length - clip.MemberDuration;
                var bound = UniTextDriverClipDrawer.TryDescribeBinding(clip.TargetNode,
                    clip.ParameterId, out var label, out var nodeType);
                results.Add(new TimelineClipInfo(
                    clip,
                    clip.Start,
                    length,
                    staggerTail + MinMemberDuration,
                    bound ? label : "Empty clip",
                    nodeType,
                    nodeType != null ? EditorResources.GetTypeIcon(nodeType) : null,
                    clip.Disabled));
            }
        }

        public bool CanEditTracks => true;

        public void AddTrack()
        {
            serialized.Update();
            tracks.intValue = TrackCount + 1;
            serialized.ApplyModifiedProperties();
        }

        public bool CanRemoveTrack(int track)
        {
            if (TrackCount <= 1) return false;
            var list = driver.Clips;
            for (var i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].TimelineTrack == track)
                    return false;
            return true;
        }

        public void RemoveTrack(int track)
        {
            if (!CanRemoveTrack(track)) return;
            serialized.Update();
            var list = driver.Clips;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == null || list[i].TimelineTrack <= track) continue;
                Element(i).FindPropertyRelative("track").intValue = list[i].TimelineTrack - 1;
            }
            tracks.intValue = TrackCount - 1;
            serialized.ApplyModifiedProperties();
        }

        public void SetClipTiming(object clipId, int track, float start, float duration)
        {
            var index = IndexOf(clipId);
            if (index < 0) return;
            var clip = driver.Clips[index];
            serialized.Update();
            var element = Element(index);
            element.FindPropertyRelative("start").floatValue = Mathf.Max(0f, start);
            element.FindPropertyRelative("track").intValue = Mathf.Max(0, track);
            var staggerTail = driver.ClipLength(clip) - clip.MemberDuration;
            element.FindPropertyRelative("memberDuration").floatValue =
                Mathf.Max(MinMemberDuration, duration - staggerTail);
            serialized.ApplyModifiedProperties();
        }

        public void SetClipMuted(object clipId, bool muted)
        {
            var index = IndexOf(clipId);
            if (index < 0) return;
            serialized.Update();
            Element(index).FindPropertyRelative("disabled").boolValue = muted;
            serialized.ApplyModifiedProperties();
        }

        public object AddClip(int track, float start)
        {
            serialized.Update();
            var index = clips.arraySize;
            clips.arraySize = index + 1;
            var element = Element(index);
            element.FindPropertyRelative("start").floatValue = Mathf.Max(0f, start);
            element.FindPropertyRelative("track").intValue = Mathf.Max(0, track);
            element.FindPropertyRelative("memberDuration").floatValue = 0.5f;
            element.FindPropertyRelative("stagger").floatValue = 0f;
            element.FindPropertyRelative("priority").intValue = 0;
            element.FindPropertyRelative("disabled").boolValue = false;
            element.FindPropertyRelative("composition").intValue =
                (int)ParameterComposition.Replace;
            element.FindPropertyRelative("easing.kind").intValue = (int)EaseKind.Builtin;
            element.FindPropertyRelative("easing.type").intValue = (int)EasingType.Linear;
            element.FindPropertyRelative("targetNode").boxedValue = default(ModifierNodeId);
            element.FindPropertyRelative("parameterId").stringValue = string.Empty;
            element.FindPropertyRelative("from").managedReferenceValue = null;
            element.FindPropertyRelative("to").managedReferenceValue = null;
            var query = element.FindPropertyRelative("query");
            query.FindPropertyRelative("label").stringValue = string.Empty;
            query.FindPropertyRelative("primaryValue").stringValue = string.Empty;
            query.FindPropertyRelative("filterToken").stringValue = string.Empty;
            query.FindPropertyRelative("bareOnly").boolValue = false;
            query.FindPropertyRelative("channel").objectReferenceValue = null;
            query.FindPropertyRelative("area").intValue = (int)RangeQueryArea.All;
            query.FindPropertyRelative("areaStart").intValue = 0;
            query.FindPropertyRelative("areaLength").intValue = 0;
            query.FindPropertyRelative("skip").intValue = 0;
            query.FindPropertyRelative("take").intValue = 0;
            serialized.ApplyModifiedProperties();
            return driver.Clips[index];
        }

        public void RemoveClips(IReadOnlyList<object> clipIds)
        {
            var indices = new List<int>(clipIds.Count);
            for (var i = 0; i < clipIds.Count; i++)
            {
                var index = IndexOf(clipIds[i]);
                if (index >= 0) indices.Add(index);
            }
            if (indices.Count == 0) return;
            indices.Sort();
            serialized.Update();
            for (var i = indices.Count - 1; i >= 0; i--)
                Element(indices[i]).DeleteCommand();
            serialized.ApplyModifiedProperties();
        }

        public IReadOnlyList<object> DuplicateClips(IReadOnlyList<object> clipIds)
        {
            var order = new List<(int index, int input)>(clipIds.Count);
            for (var i = 0; i < clipIds.Count; i++)
            {
                var index = IndexOf(clipIds[i]);
                if (index >= 0) order.Add((index, i));
            }
            if (order.Count == 0) return Array.Empty<object>();
            order.Sort((left, right) => left.index.CompareTo(right.index));

            serialized.Update();
            for (var k = order.Count - 1; k >= 0; k--)
                PropertyClipboard.DuplicateElement(clips, order[k].index);
            serialized.ApplyModifiedProperties();

            var copies = new object[clipIds.Count];
            for (var k = 0; k < order.Count; k++)
                copies[order[k].input] = driver.Clips[order[k].index + k + 1];
            return copies;
        }

        public void SplitClip(object clipId, float time)
        {
            var index = IndexOf(clipId);
            if (index < 0) return;
            var clip = driver.Clips[index];
            var length = driver.ClipLength(clip);
            var start = clip.Start;
            if (time <= start + MinMemberDuration || time >= start + length - MinMemberDuration)
                return;

            var eased = clip.Easing.Evaluate((time - start) / length);
            var parameter = driver.FindBoundParameter(clip);
            var mid = parameter != null && clip.From != null && clip.To != null
                ? parameter.LerpBoxed(clip.From.BoxedValue, clip.To.BoxedValue, eased)
                : null;
            var staggerTail = length - clip.MemberDuration;

            serialized.Update();
            PropertyClipboard.DuplicateElement(clips, index);
            var left = Element(index);
            var right = Element(index + 1);
            left.FindPropertyRelative("memberDuration").floatValue =
                Mathf.Max(MinMemberDuration, time - start - staggerTail);
            right.FindPropertyRelative("start").floatValue = time;
            right.FindPropertyRelative("memberDuration").floatValue =
                Mathf.Max(MinMemberDuration, start + length - time - staggerTail);
            if (mid != null)
            {
                WritePayload(left, "to", mid);
                WritePayload(right, "from", mid);
            }
            serialized.ApplyModifiedProperties();
        }

        private static void WritePayload(SerializedProperty clip, string endpoint, object value)
        {
            var payload = clip.FindPropertyRelative(endpoint);
            if (payload.managedReferenceValue is not RuleValue) return;
            payload.FindPropertyRelative("value").boxedValue = value;
        }

        public void CopyClips(IReadOnlyList<object> clipIds)
        {
            var values = new List<object>(clipIds.Count);
            for (var i = 0; i < clipIds.Count; i++)
            {
                var index = IndexOf(clipIds[i]);
                if (index >= 0) values.Add(driver.Clips[index]);
            }
            PropertyClipboard.CopyObjects(values, typeof(UniTextDriverClip));
        }

        public bool CanPasteClip => PropertyClipboard.CanPasteObjects(typeof(UniTextDriverClip));

        public IReadOnlyList<object> PasteClips(int track, float start)
        {
            var values = PropertyClipboard.PasteObjects(typeof(UniTextDriverClip));
            if (values.Length == 0) return Array.Empty<object>();

            var earliest = float.MaxValue;
            var earliestTrack = 0;
            for (var i = 0; i < values.Length; i++)
                if (values[i] is UniTextDriverClip clip && clip.Start < earliest)
                {
                    earliest = clip.Start;
                    earliestTrack = clip.TimelineTrack;
                }

            serialized.Update();
            var first = clips.arraySize;
            clips.arraySize = first + values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] is not UniTextDriverClip pasted) continue;
                var element = Element(first + i);
                element.boxedValue = pasted;
                element.FindPropertyRelative("start").floatValue =
                    Mathf.Max(0f, start + pasted.Start - earliest);
                element.FindPropertyRelative("track").intValue =
                    Mathf.Max(0, track + pasted.TimelineTrack - earliestTrack);
            }
            serialized.ApplyModifiedProperties();

            var result = new object[values.Length];
            for (var i = 0; i < values.Length; i++) result[i] = driver.Clips[first + i];
            return result;
        }

        public VisualElement CreateClipEditor(object clipId)
        {
            var index = IndexOf(clipId);
            if (index < 0) return new Label("The clip is gone.");
            serialized.Update();
            return SerializedPropertyField.Create(Element(index));
        }

        public VisualElement CreateClipsEditor(IReadOnlyList<object> clipIds)
        {
            var indices = new List<int>(clipIds.Count);
            for (var i = 0; i < clipIds.Count; i++)
            {
                var index = IndexOf(clipIds[i]);
                if (index >= 0) indices.Add(index);
            }
            if (indices.Count == 0) return new Label("The clips are gone.");
            serialized.Update();

            var root = InspectorVisuals.CreateRoot();
            UniTextInspectorTheme.Initialize(root);
            var heading = InspectorVisuals.CreateSubheading($"{indices.Count} clips");
            root.Add(heading);
            foreach (var (path, display) in sharedFields)
            {
                var first = Element(indices[0]).FindPropertyRelative(path);
                var field = SerializedPropertyField.Create(first, display);
                SerializedPropertyField.OnChange(field, () =>
                {
                    serialized.Update();
                    var value = Element(indices[0]).FindPropertyRelative(path).boxedValue;
                    for (var i = 1; i < indices.Count; i++)
                        Element(indices[i]).FindPropertyRelative(path).boxedValue = value;
                    serialized.ApplyModifiedProperties();
                }, first);
                root.Add(field);
            }
            return root;
        }

        private SerializedProperty Element(int index) => clips.GetArrayElementAtIndex(index);

        private int IndexOf(object clipId)
        {
            var list = driver.Clips;
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], clipId))
                    return i;
            return -1;
        }

        private sealed class DriverTransport : ITimelineTransport
        {
            private readonly UniTextDriver driver;

            internal DriverTransport(UniTextDriver driver) => this.driver = driver;

            public float Time
            {
                get => driver == null ? 0f : driver.Playhead;
                set
                {
                    if (driver != null) driver.Playhead = value;
                }
            }

            public bool IsPlaying => driver != null && driver.IsPlaying;

            public void Play()
            {
                if (driver != null && driver.isActiveAndEnabled) driver.Play();
            }

            public void Pause()
            {
                if (driver != null) driver.Pause();
            }

            public void Stop()
            {
                if (driver != null && driver.isActiveAndEnabled) driver.Stop();
            }
        }
    }
}
