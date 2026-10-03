using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The rendering-independent interaction orchestrator for editing a group of points: ties
    /// <see cref="PointSelection"/>, direct drag, and the modal <see cref="ModalTransform"/> (G/S/R)
    /// together over an <see cref="IEditablePointSet"/> and an <see cref="IEditSurface"/>, with
    /// coalesced undo. SceneView feeds <see cref="HandleEvent(Event)"/>; a retained surface builds its own
    /// <see cref="PointEditEvent"/> and feeds <see cref="HandleEvent(PointEditEvent)"/>. The consumer owns
    /// point rendering and calls <see cref="DrawBoxSelect"/> for the marquee.
    /// </summary>
    /// <remarks>
    /// Drag and modal transforms share one path: a plane-space <see cref="Func{T,TResult}"/> applied to a snapshot
    /// of the affected points taken at gesture start (so there is no feedback drift). "Affected" is the selection
    /// plus each selected point's rigid dependents (a knot's handles), de-duplicated, so a selected parent carries
    /// its children while an independently-grabbed child moves alone. Deletion is data-specific and raised through
    /// <see cref="DeleteRequested"/> for the consumer to service.
    /// </remarks>
    public sealed class PointEditor
    {
        private readonly IEditablePointSet points;
        private readonly IEditSurface surface;
        private readonly PointSelection selection = new();
        private readonly ModalTransform modal = new();

        private readonly List<int> affected = new();
        private readonly HashSet<int> affectedSet = new();
        private readonly Dictionary<int, Vector2> snapshotPlane = new();
        private readonly List<int> selectionSnapshot = new();
        private readonly List<int> depBuffer = new();

        private bool dragging;
        private bool boxSelecting;
        private int downHitIndex = -1;
        private bool downMoved;
        private Vector2 gestureStartPlane;
        private Vector2 boxStartPlane;

        /// <summary>Point pick radius in screen pixels.</summary>
        public float PickPixelRadius { get; set; } = 10f;

        /// <summary>Fill of the box-select marquee; hosts inside themed chrome restyle it to their accent.</summary>
        public Color BoxSelectFill { get; set; } = new(0.3f, 0.5f, 1f, 0.1f);

        /// <summary>Outline of the box-select marquee.</summary>
        public Color BoxSelectOutline { get; set; } = new(0.35f, 0.55f, 1f, 0.9f);

        /// <summary>The live selection, for the consumer to render (which points are highlighted).</summary>
        public PointSelection Selection => selection;

        /// <summary>Whether a gesture (drag, box-select, or modal transform) is in progress.</summary>
        public bool IsBusy => modal.IsActive || dragging || boxSelecting;

        /// <summary>HUD text for the active modal transform ("Move X: 12"), or empty.</summary>
        public string StatusText => modal.Describe();

        /// <summary>Raised after any edit or selection change, so the consumer can repaint / mark its object dirty.</summary>
        public event Action Changed;

        /// <summary>Raised when the user presses Delete with a non-empty selection; the consumer removes the points.</summary>
        public event Action<IReadOnlyList<int>> DeleteRequested;

        /// <summary>Raised by a click on empty space that never became a box-select — the host decides what, beyond the points, that dismisses.</summary>
        public event Action ClickedEmpty;

        /// <summary>Raised whenever selected point positions actually change (a drag or modal apply) — the hook for post-edit fix-ups such as Bézier tangent-mode enforcement. Not fired for selection-only changes.</summary>
        public event Action Edited;

        /// <summary>Creates an editor over one required point set and its coordinate surface.</summary>
        public PointEditor(IEditablePointSet points, IEditSurface surface)
        {
            this.points = points ?? throw new ArgumentNullException(nameof(points));
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        }

        /// <summary>Adapts one SceneView event to the rendering-independent point editor.</summary>
        public void HandleEvent(Event e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(id);
                return;
            }

            var input = PointEditEvent.From(e);
            if (input.Type == PointEditEventType.None) return;
            var consumed = HandleEvent(input);
            if (consumed && input.Type == PointEditEventType.PointerDown)
                GUIUtility.hotControl = id;
            if (input.Type == PointEditEventType.PointerUp && GUIUtility.hotControl == id)
                GUIUtility.hotControl = 0;
            if (consumed) e.Use();
        }

        /// <summary>
        /// Processes one neutral event — the entry point for a retained host, which builds its own
        /// <see cref="PointEditEvent"/> rather than owning an IMGUI <see cref="Event"/>. Returns whether the event
        /// was consumed and must not reach another handler.
        /// </summary>
        public bool HandleEvent(PointEditEvent e)
        {
            var pointer = surface.PointerPlane;

            if (modal.IsActive)
            {
                var step = modal.HandleEvent(e, pointer);
                if (step.Changed) { ApplyPlaneTransform(modal.Build(pointer)); Edited?.Invoke(); }
                if (step.Committed) { modal.Reset(); ClearGesture(); Changed?.Invoke(); }
                else if (step.Cancelled) { RestoreSnapshot(); modal.Reset(); ClearGesture(); Changed?.Invoke(); }
                return step.Consumed;
            }

            bool noMods = !e.Control && !e.Alt && !e.Command && !e.Shift;

            switch (e.Type)
            {
                case PointEditEventType.KeyDown:
                    if (e.KeyCode == KeyCode.A && noMods && points.Count > 0)
                    {
                        selection.SelectAll(points.Count);
                        Changed?.Invoke();
                        return true;
                    }
                    if ((e.KeyCode == KeyCode.Delete || e.KeyCode == KeyCode.Backspace) &&
                        selection.Count > 0)
                    {
                        DeleteRequested?.Invoke(selection.Indices);
                        Changed?.Invoke();
                        return true;
                    }
                    if (noMods && selection.Count > 0 &&
                        IsTransformKey(e.KeyCode, out var mode))
                    {
                        BeginModal(mode, pointer);
                        return true;
                    }
                    break;

                case PointEditEventType.PointerDown:
                    if (e.Button != 0) break;
                    downHitIndex = PickNearest();
                    downMoved = false;
                    if (downHitIndex >= 0)
                    {
                        selection.HandleClick(downHitIndex, e.Shift);
                        gestureStartPlane = pointer;
                        BuildAffected();
                        SnapshotAffected();
                    }
                    else
                    {
                        boxSelecting = true;
                        boxStartPlane = pointer;
                    }
                    Changed?.Invoke();
                    return true;

                case PointEditEventType.PointerMove:
                    if (boxSelecting)
                    {
                        downMoved = true;
                        Changed?.Invoke();
                        return true;
                    }
                    if (downHitIndex >= 0)
                    {
                        if (!downMoved)
                        {
                            downMoved = true;
                            dragging = true;
                            RecordUndo("Move Points");
                        }
                        var delta = pointer - gestureStartPlane;
                        ApplyPlaneTransform(p => p + delta);
                        Edited?.Invoke();
                        Changed?.Invoke();
                        return true;
                    }
                    break;

                case PointEditEventType.PointerUp:
                    if (e.Button != 0) break;
                    if (boxSelecting)
                    {
                        selection.SelectInRect(RectFromCorners(boxStartPlane, pointer),
                            points.Count, PlanePos, e.Shift);
                        bool clicked = !downMoved;
                        Changed?.Invoke();
                        ClearGesture();
                        if (clicked) ClickedEmpty?.Invoke();
                        return true;
                    }
                    ClearGesture();
                    return true;
            }
            return false;
        }

        /// <summary>Draws the marquee rectangle while box-selecting. Call during Repaint. The surface renders it in its own space.</summary>
        public void DrawBoxSelect()
        {
            if (!boxSelecting) return;
            surface.DrawMarquee(boxStartPlane, surface.PointerPlane, BoxSelectFill, BoxSelectOutline);
        }

        private void BeginModal(TransformMode mode, Vector2 pointer)
        {
            selection.Snapshot(selectionSnapshot);
            BuildAffected();
            SnapshotAffected();
            RecordUndo("Transform Points");
            modal.Begin(mode, SelectionBoundsPlane(), pointer);
        }

        /// <summary>The point under the pointer within <see cref="PickPixelRadius"/>, or -1 — what a host aims a context menu or a hover highlight at.</summary>
        public int PickPoint() => PickNearest();

        private int PickNearest()
        {
            int best = -1;
            float bestDist = PickPixelRadius;
            for (int i = 0; i < points.Count; i++)
            {
                float d = surface.PointerScreenDistance(points.GetPosition(i));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }
            return best;
        }

        private void BuildAffected()
        {
            affected.Clear();
            affectedSet.Clear();
            foreach (var idx in selection.Indices)
            {
                if (affectedSet.Add(idx)) affected.Add(idx);
                depBuffer.Clear();
                points.GetRigidDependents(idx, depBuffer);
                for (int i = 0; i < depBuffer.Count; i++)
                    if (affectedSet.Add(depBuffer[i])) affected.Add(depBuffer[i]);
            }
        }

        private void SnapshotAffected()
        {
            snapshotPlane.Clear();
            for (int i = 0; i < affected.Count; i++)
            {
                int idx = affected[i];
                snapshotPlane[idx] = surface.WorldToPlane(points.GetPosition(idx));
            }
        }

        private void ApplyPlaneTransform(Func<Vector2, Vector2> xform)
        {
            for (int i = 0; i < affected.Count; i++)
            {
                int idx = affected[i];
                points.SetPosition(idx, surface.PlaneToWorld(xform(snapshotPlane[idx])));
            }
        }

        private void RestoreSnapshot()
        {
            for (int i = 0; i < affected.Count; i++)
            {
                int idx = affected[i];
                points.SetPosition(idx, surface.PlaneToWorld(snapshotPlane[idx]));
            }
            selection.Restore(selectionSnapshot);
        }

        private Rect SelectionBoundsPlane()
        {
            bool any = false;
            float minX = 0, minY = 0, maxX = 0, maxY = 0;
            foreach (var idx in selection.Indices)
            {
                var p = surface.WorldToPlane(points.GetPosition(idx));
                if (!any)
                {
                    minX = maxX = p.x;
                    minY = maxY = p.y;
                    any = true;
                }
                else
                {
                    minX = Mathf.Min(minX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxX = Mathf.Max(maxX, p.x);
                    maxY = Mathf.Max(maxY, p.y);
                }
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private Vector2 PlanePos(int i) => surface.WorldToPlane(points.GetPosition(i));

        private void RecordUndo(string name)
        {
            var context = points.UndoContext;
            if (context != null) Undo.RegisterCompleteObjectUndo(context, name);
        }

        private void ClearGesture()
        {
            dragging = false;
            boxSelecting = false;
            downHitIndex = -1;
            downMoved = false;
            affected.Clear();
            affectedSet.Clear();
            snapshotPlane.Clear();
        }

        private static Rect RectFromCorners(Vector2 a, Vector2 b)
            => Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));

        private static bool IsTransformKey(KeyCode key, out TransformMode mode)
        {
            switch (key)
            {
                case KeyCode.G: mode = TransformMode.Translate; return true;
                case KeyCode.S: mode = TransformMode.Scale; return true;
                case KeyCode.R: mode = TransformMode.Rotate; return true;
                default: mode = TransformMode.None; return false;
            }
        }
    }
}
