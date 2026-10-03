using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The reusable Bézier-path editing brain: wraps a <see cref="PointEditor"/> over a <see cref="BezierPath"/>
    /// (through <see cref="BezierPathPointSet"/>) and adds the path-specific operations the generic point editor
    /// doesn't own — tangent-mode enforcement after a move, knot deletion, segment split, close toggle,
    /// and per-knot handle mode (1–5). One controller drives every surface: the SceneView tool and an inspector
    /// preview share this exact logic, differing only in their <see cref="IEditSurface"/> and how they render.
    /// </summary>
    public sealed class BezierPathEditController
    {
        private readonly BezierPath path;
        private readonly UnityEngine.Object undoContext;
        private readonly IEditSurface surface;
        private readonly BezierPathPointSet points;
        private readonly PointEditor editor;
        private readonly List<int> tempKnots = new();

        /// <summary>Creates one controller over the required path and presentation surface.</summary>
        /// <param name="path">The live path instance owned by the edited object.</param>
        /// <param name="localToWorld">Frame the path's points are placed in for SceneView editing, or the identity to edit in the path's own 2-D space (paired with a 2-D surface such as <see cref="RectEditSurface"/>).</param>
        /// <param name="undoContext">Object recorded for undo (the owning component or asset), or null to skip undo.</param>
        /// <param name="surface">Coordinate and pointer adapter for the host editor surface.</param>
        public BezierPathEditController(BezierPath path, Matrix4x4 localToWorld, UnityEngine.Object undoContext, IEditSurface surface)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.undoContext = undoContext;
            this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
            points = new BezierPathPointSet(path, localToWorld, undoContext);
            editor = new PointEditor(points, surface);
            editor.Edited += EnforceTangents;
            editor.DeleteRequested += DeleteKnots;
            editor.Changed += RaiseChanged;
        }

        /// <summary>The underlying generic point editor.</summary>
        public PointEditor Editor => editor;

        /// <summary>Frame the path's points are placed in; reassign whenever the host's frame moves so picking and dragging stay on what is drawn.</summary>
        public Matrix4x4 LocalToWorld
        {
            get => points.LocalToWorld;
            set => points.LocalToWorld = value;
        }

        /// <summary>The live point selection, for the consumer to render.</summary>
        public PointSelection Selection => editor.Selection;

        /// <summary>The edited path.</summary>
        public BezierPath Path => path;

        /// <summary>HUD text for the active modal transform ("Move X: 12"), or empty.</summary>
        public string StatusText => editor.StatusText;

        /// <summary>Whether a gesture (drag / box-select / modal) is in progress — freeze the view fit while true.</summary>
        public bool IsBusy => editor.IsBusy;

        /// <summary>Raised after any edit or selection change — the consumer repaints and marks its object dirty.</summary>
        public event Action Changed;

        /// <summary>Processes one editor event through the generic point editor (select / drag / G-S-R / box-select / delete / select-all).</summary>
        public void HandleEvent(Event e) => editor.HandleEvent(e);

        /// <summary>Processes one already-adapted event — what a UI Toolkit host dispatches, having no IMGUI <see cref="Event"/> to hand.</summary>
        public bool HandleEvent(PointEditEvent e) => editor.HandleEvent(e);

        /// <summary>Draws the box-select marquee (call during Repaint); the surface renders it in its own space.</summary>
        public void DrawBoxSelect() => editor.DrawBoxSelect();

        /// <summary>
        /// Path-specific keys: <c>C</c> toggles closed, <c>1</c>–<c>5</c> set the selected knots' handle mode
        /// (Free / Aligned / Mirrored / Auto / Vector). Returns true when it consumed <paramref name="e"/>.
        /// </summary>
        public bool HandlePathKeys(Event e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            var consumed = HandlePathKeys(PointEditEvent.From(e));
            if (consumed) e.Use();
            return consumed;
        }

        /// <summary>Processes one already-adapted key event, for a host that dispatches <see cref="PointEditEvent"/> rather than IMGUI events.</summary>
        public bool HandlePathKeys(PointEditEvent e)
        {
            if (e.Type != PointEditEventType.KeyDown) return false;
            switch (e.KeyCode)
            {
                case KeyCode.C: ToggleClosed(); return true;
                case KeyCode.Alpha1: SetSelectedMode(TangentMode.Free); return true;
                case KeyCode.Alpha2: SetSelectedMode(TangentMode.Aligned); return true;
                case KeyCode.Alpha3: SetSelectedMode(TangentMode.Mirrored); return true;
                case KeyCode.Alpha4: SetSelectedMode(TangentMode.Auto); return true;
                case KeyCode.Alpha5: SetSelectedMode(TangentMode.Vector); return true;
                default: return false;
            }
        }

        /// <summary>
        /// Splits the segment under the pointer at its nearest point, when the pointer is within
        /// <paramref name="pixelThreshold"/> pixels of the curve — the action a host binds to its insert gesture.
        /// Returns true when it split (and consumed the event).
        /// </summary>
        public bool TrySplit(Event e, float pixelThreshold = 12f)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (!TrySplit(pixelThreshold)) return false;
            e.Use();
            return true;
        }

        /// <summary>Splits without an event to consume, for a host that decides on its own whether the gesture was an insert.</summary>
        public bool TrySplit(float pixelThreshold = 12f)
        {
            if (path.SegmentCount == 0) return false;

            Vector2 local = surface.PointerPlane;
            path.ClosestPoint(local, out int seg, out float t);
            if (seg < 0) return false;

            var lp = path.Evaluate(seg, t);
            if (surface.PointerScreenDistance(surface.PlaneToWorld(lp)) > pixelThreshold) return false;

            RecordUndo("Split Bézier Segment");
            int newKnot = path.SplitSegment(seg, t);
            editor.Selection.Clear();
            editor.Selection.Add(BezierPathPointSet.AnchorPoint(newKnot));
            RaiseChanged();
            return true;
        }

        /// <summary>The point under the pointer, or -1 — the knot or handle a context menu would act on.</summary>
        public int PickPoint() => editor.PickPoint();

        /// <summary>Selects <paramref name="point"/> alone unless it is already selected, so a menu opened on it acts on what the pointer names.</summary>
        public void SelectForMenu(int point)
        {
            if (point < 0 || editor.Selection.Contains(point)) return;
            editor.Selection.Clear();
            editor.Selection.Add(point);
            RaiseChanged();
        }

        /// <summary>The handle type every selected knot stands in, or <see langword="null"/> where they differ or nothing is selected — the value a picker opens on.</summary>
        public TangentMode? SelectionMode
        {
            get
            {
                TangentMode? common = null;
                foreach (var idx in editor.Selection.Indices)
                {
                    int k = BezierPathPointSet.KnotIndex(idx);
                    if (k < 0 || k >= path.Count) continue;
                    var mode = path[k].mode;
                    if (common.HasValue && common.Value != mode) return null;
                    common = mode;
                }
                return common;
            }
        }

        /// <summary>Sets the handle type of every selected knot, as one undo step. Does nothing on an empty selection.</summary>
        public void SetTangentMode(TangentMode mode) => SetSelectedMode(mode);

        /// <summary>Joins the last knot back to the first, or parts them — what turns a filled outline into a stroked line and back.</summary>
        public void ToggleClosed()
        {
            RecordUndo("Toggle Close Path");
            path.Closed = !path.Closed;
            RaiseChanged();
        }

        private void SetSelectedMode(TangentMode mode)
        {
            if (editor.Selection.Count == 0) return;

            RecordUndo("Set Handle Type");
            tempKnots.Clear();
            foreach (var idx in editor.Selection.Indices)
            {
                int k = BezierPathPointSet.KnotIndex(idx);
                if (tempKnots.Contains(k)) continue;
                tempKnots.Add(k);
                if (k >= 0 && k < path.Count) path.SetMode(k, mode);
            }
            RaiseChanged();
        }

        private void EnforceTangents()
        {
            foreach (var idx in editor.Selection.Indices)
            {
                int k = BezierPathPointSet.KnotIndex(idx);
                if (k < 0 || k >= path.Count) continue;
                if (BezierPathPointSet.Part(idx) == 0) path.OnAnchorMoved(k);
                else path.OnHandleMoved(k, BezierPathPointSet.Part(idx) == 1 ? HandleSide.In : HandleSide.Out);
            }
        }

        private void DeleteKnots(IReadOnlyList<int> indices)
        {
            tempKnots.Clear();
            foreach (var idx in indices)
            {
                int k = BezierPathPointSet.KnotIndex(idx);
                if (!tempKnots.Contains(k)) tempKnots.Add(k);
            }
            if (tempKnots.Count == 0) return;
            tempKnots.Sort();

            RecordUndo("Delete Bézier Knot");
            for (int i = tempKnots.Count - 1; i >= 0; i--)
            {
                int k = tempKnots[i];
                if (k >= 0 && k < path.Count) path.RemoveAt(k);
            }
            editor.Selection.Clear();
            RaiseChanged();
        }

        private void RecordUndo(string label)
        {
            if (undoContext != null) Undo.RegisterCompleteObjectUndo(undoContext, label);
        }

        private void RaiseChanged() => Changed?.Invoke();
    }
}
