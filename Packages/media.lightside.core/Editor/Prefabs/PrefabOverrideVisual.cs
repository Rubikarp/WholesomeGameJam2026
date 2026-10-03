using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LightSide
{
    [InitializeOnLoad]
    internal static class PrefabOverrideVisual
    {
        private const string InputUssClassName = "unity-base-field__input";
        private static readonly CustomStyleProperty<Color> colorProperty =
            new("--lightside-prefab-override");
        private static readonly ConditionalWeakTable<VisualElement, HostTrackers> hosts = new();
        private static readonly ConditionalWeakTable<SerializedObject, OverrideContext> contexts = new();
        private static readonly ConditionalWeakTable<Object, TargetOverrides> targets = new();
        private static readonly HashSet<Tracker> active = new();
        private static bool refreshScheduled;
        private static int generation;

        static PrefabOverrideVisual()
        {
            Undo.undoRedoPerformed += Invalidate;
            PrefabUtility.prefabInstanceUpdated += _ => Invalidate();
            ObjectChangeEvents.changesPublished += OnObjectChanges;
            EditorApplication.projectChanged += Invalidate;
            SerializedPropertyBinding.Written += _ => Invalidate();
        }

        internal static void Attach(VisualElement host, SerializedPropertyBinding binding,
            VisualElement anchor = null, bool? includeChildren = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            if (!SupportsPrefabOverrides(binding.SerializedObject)) return;
            anchor ??= FindAnchor(host);
            var trackers = hosts.GetValue(host, _ => new HostTrackers());
            var context = contexts.GetValue(binding.SerializedObject,
                serializedObject => new OverrideContext(serializedObject));
            var tracker = trackers.Find(anchor, context);
            if (tracker == null)
            {
                tracker = new Tracker(host, anchor, context);
                trackers.Items.Add(tracker);
                tracker.Register();
            }
            tracker.Add(binding.PropertyPath, includeChildren ?? true);
        }

        private static bool SupportsPrefabOverrides(SerializedObject serializedObject)
        {
            var selectedTargets = serializedObject.targetObjects;
            for (var i = 0; i < selectedTargets.Length; i++)
                if (selectedTargets[i] is GameObject or Component) return true;
            return false;
        }

        internal static void AttachDefault(VisualElement host, SerializedPropertyBinding binding,
            VisualElement anchor = null)
        {
            if (hosts.TryGetValue(host, out var trackers) && trackers.Items.Count > 0) return;
            Attach(host, binding, anchor);
        }

        internal static void Invalidate()
        {
            if (refreshScheduled || active.Count == 0) return;
            refreshScheduled = true;
            EditorApplication.delayCall += Refresh;
        }

        private static void OnObjectChanges(ref ObjectChangeEventStream _) => Invalidate();

        internal static VisualElement FindAnchor(VisualElement host)
        {
            if (host.ClassListContains(BaseField<int>.ussClassName))
                return host.Q<VisualElement>(className: InputUssClassName) ?? host;
            foreach (var child in host.Children())
                if (child is InspectorFoldoutHeader || child is InspectorFoldoutField)
                    return child;
            return host;
        }

        private static void Refresh()
        {
            refreshScheduled = false;
            generation++;
            foreach (var tracker in active) tracker.Refresh(generation);
        }

        internal enum OverrideState
        {
            None,
            All,
            Mixed,
        }

        internal readonly struct OverrideQuery
        {
            public OverrideQuery(string propertyPath, bool includeChildren)
            {
                PropertyPath = propertyPath;
                IncludeChildren = includeChildren;
            }

            public string PropertyPath { get; }
            public bool IncludeChildren { get; }
        }

        private sealed class HostTrackers
        {
            public readonly List<Tracker> Items = new();

            public Tracker Find(VisualElement anchor, OverrideContext context)
            {
                for (var i = 0; i < Items.Count; i++)
                    if (Items[i].Anchor == anchor && Items[i].Context == context)
                        return Items[i];
                return null;
            }
        }

        private sealed class Tracker
        {
            private readonly VisualElement host;
            private readonly OverrideContext context;
            private readonly List<OverrideQuery> queries = new();
            private readonly VisualElement paintTarget;
            private VisualElement firstSegment;
            private VisualElement secondSegment;
            private OverrideState state;
            private Color color = new Color32(55, 145, 255, 255);

            public Tracker(VisualElement host, VisualElement anchor, OverrideContext context)
            {
                this.host = host;
                Anchor = anchor;
                this.context = context;
                if (anchor == host)
                {
                    paintTarget = anchor;
                }
                else
                {
                    paintTarget = new VisualElement { pickingMode = PickingMode.Ignore };
                    paintTarget.style.position = Position.Absolute;
                    paintTarget.style.width = 2f;
                    paintTarget.style.borderTopLeftRadius = 1f;
                    paintTarget.style.borderTopRightRadius = 1f;
                    paintTarget.style.borderBottomLeftRadius = 1f;
                    paintTarget.style.borderBottomRightRadius = 1f;
                    host.hierarchy.Add(paintTarget);
                }
            }

            public VisualElement Anchor { get; }
            public OverrideContext Context => context;

            public void Add(string propertyPath, bool includeChildren)
            {
                for (var i = 0; i < queries.Count; i++)
                    if (queries[i].PropertyPath == propertyPath &&
                        queries[i].IncludeChildren == includeChildren)
                        return;
                queries.Add(new OverrideQuery(propertyPath, includeChildren));
                if (host.panel != null) Invalidate();
            }

            public void Register()
            {
                host.RegisterCallback<AttachToPanelEvent>(OnAttach);
                host.RegisterCallback<DetachFromPanelEvent>(OnDetach);
                Anchor.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
                if (paintTarget != Anchor)
                    Anchor.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
                else
                    paintTarget.generateVisualContent += Draw;
                if (host.panel != null) Activate();
            }

            public void Refresh(int currentGeneration)
            {
                var next = context.Evaluate(queries, currentGeneration);
                if (state == next) return;
                state = next;
                Repaint();
            }

            private void OnAttach(AttachToPanelEvent _) => Activate();

            private void OnDetach(DetachFromPanelEvent _)
            {
                active.Remove(this);
                SetState(OverrideState.None);
            }

            private void Activate()
            {
                active.Add(this);
                if (paintTarget != Anchor)
                {
                    Resize();
                    paintTarget.BringToFront();
                }
                Invalidate();
            }

            private void OnGeometryChanged(GeometryChangedEvent _) => Resize();

            private void Resize()
            {
                var anchorHeight = Anchor.contentRect.height;
                var height = Mathf.Max(0f, Mathf.Min(12f, anchorHeight - 4f));
                var origin = Anchor.ChangeCoordinatesTo(host, Vector2.zero);
                paintTarget.style.left = origin.x + 1f;
                paintTarget.style.height = height;
                paintTarget.style.top = origin.y + (anchorHeight <= 32f
                    ? (anchorHeight - height) * 0.5f
                    : 6f);
                if (firstSegment == null) return;
                var segmentHeight = height * 0.32f;
                firstSegment.style.height = segmentHeight;
                secondSegment.style.height = segmentHeight;
            }

            private void SetState(OverrideState value)
            {
                if (state == value) return;
                state = value;
                Repaint();
            }

            private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
            {
                if (!evt.customStyle.TryGetValue(colorProperty, out var next) || color == next)
                    return;
                color = next;
                if (state != OverrideState.None) Repaint();
            }

            private void Repaint()
            {
                if (paintTarget == Anchor)
                {
                    paintTarget.MarkDirtyRepaint();
                    return;
                }

                paintTarget.style.display = state == OverrideState.None
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
                if (state == OverrideState.All)
                {
                    paintTarget.style.backgroundColor = color;
                    if (firstSegment != null)
                    {
                        firstSegment.style.display = DisplayStyle.None;
                        secondSegment.style.display = DisplayStyle.None;
                    }
                    return;
                }

                paintTarget.style.backgroundColor = Color.clear;
                firstSegment ??= CreateSegment(0f, float.NaN);
                secondSegment ??= CreateSegment(float.NaN, 0f);
                firstSegment.style.backgroundColor = color;
                secondSegment.style.backgroundColor = color;
                firstSegment.style.display = DisplayStyle.Flex;
                secondSegment.style.display = DisplayStyle.Flex;
                Resize();
            }

            private VisualElement CreateSegment(float top, float bottom)
            {
                var segment = new VisualElement { pickingMode = PickingMode.Ignore };
                segment.style.position = Position.Absolute;
                segment.style.left = 0f;
                segment.style.right = 0f;
                if (!float.IsNaN(top)) segment.style.top = top;
                if (!float.IsNaN(bottom)) segment.style.bottom = bottom;
                paintTarget.hierarchy.Add(segment);
                return segment;
            }

            private void Draw(MeshGenerationContext mesh)
            {
                if (state == OverrideState.None || paintTarget.contentRect.height <= 4f) return;
                var height = Mathf.Min(12f, paintTarget.contentRect.height - 4f);
                var top = paintTarget.contentRect.height <= 32f
                    ? (paintTarget.contentRect.height - height) * 0.5f
                    : 6f;
                var painter = mesh.painter2D;
                painter.lineWidth = 2f;
                painter.lineCap = LineCap.Round;
                painter.strokeColor = color;
                if (state == OverrideState.All)
                {
                    Stroke(painter, top, top + height);
                    return;
                }

                var segment = height * 0.32f;
                Stroke(painter, top, top + segment);
                Stroke(painter, top + height - segment, top + height);
            }

            private static void Stroke(Painter2D painter, float top, float bottom)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(2f, top));
                painter.LineTo(new Vector2(2f, bottom));
                painter.Stroke();
            }
        }

        internal sealed class OverrideContext
        {
            private readonly Object[] selectedTargets;

            public OverrideContext(SerializedObject serializedObject)
            {
                selectedTargets = serializedObject.targetObjects;
            }

            public OverrideState Evaluate(string propertyPath, bool includeChildren,
                int currentGeneration)
                => Evaluate(new[] { new OverrideQuery(propertyPath, includeChildren) },
                    currentGeneration);

            internal OverrideState Evaluate(IReadOnlyList<OverrideQuery> queries,
                int currentGeneration)
            {
                var overridden = 0;
                for (var i = 0; i < selectedTargets.Length; i++)
                {
                    var target = selectedTargets[i];
                    if (target == null) continue;
                    var targetOverrides = targets.GetValue(target, _ => new TargetOverrides());
                    for (var j = 0; j < queries.Count; j++)
                    {
                        var query = queries[j];
                        if (!targetOverrides.Contains(target, query.PropertyPath,
                                query.IncludeChildren, currentGeneration)) continue;
                        overridden++;
                        break;
                    }
                }
                if (overridden == 0) return OverrideState.None;
                return overridden == selectedTargets.Length
                    ? OverrideState.All
                    : OverrideState.Mixed;
            }

        }

        private sealed class TargetOverrides
        {
            private readonly Dictionary<(string path, bool includeChildren), bool> results = new();
            private SerializedObject serializedObject;
            private HashSet<string> aggregateOverrides;
            private bool prefabInstance;
            private int evaluatedGeneration = -1;

            public bool Contains(Object target, string propertyPath, bool includeChildren,
                int currentGeneration)
            {
                if (evaluatedGeneration != currentGeneration)
                {
                    results.Clear();
                    aggregateOverrides = null;
                    prefabInstance = target is GameObject or Component &&
                                     PrefabUtility.IsPartOfPrefabInstance(target);
                    if (prefabInstance)
                    {
                        serializedObject ??= new SerializedObject(target);
                        serializedObject.UpdateIfRequiredOrScript();
                    }
                    evaluatedGeneration = currentGeneration;
                }
                if (!prefabInstance) return false;
                var key = (propertyPath, includeChildren);
                if (results.TryGetValue(key, out var result)) return result;
                result = IsOverridden(propertyPath, includeChildren);
                results[key] = result;
                return result;
            }

            private bool IsOverridden(string propertyPath, bool includeChildren)
            {
                var property = serializedObject.FindProperty(propertyPath);
                if (property == null) return false;
                if (IsOverridden(property)) return true;
                if (!includeChildren || !property.hasChildren) return false;
                BuildAggregateOverrides();
                return aggregateOverrides.Contains(propertyPath);
            }

            private void BuildAggregateOverrides()
            {
                if (aggregateOverrides != null) return;
                aggregateOverrides = new HashSet<string>();
                var property = serializedObject.GetIterator();
                if (!property.Next(true)) return;
                do
                {
                    if (IsOverridden(property)) AddPathAndParents(property.propertyPath);
                }
                while (property.Next(true));
            }

            private void AddPathAndParents(string path)
            {
                while (true)
                {
                    aggregateOverrides.Add(path);
                    var separator = path.LastIndexOf('.');
                    if (separator < 0) return;
                    path = path.Substring(0, separator);
                }
            }

            private static bool IsOverridden(SerializedProperty property)
                => property.prefabOverride;
        }
    }
}
