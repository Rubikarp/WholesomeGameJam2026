using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// The graphical editor behind a custom <see cref="Ease"/>: a themed canvas carrying the curve over an
    /// adaptive grid, driven by the shared <see cref="PointEditor"/> so selection, dragging, G/S/R and undo
    /// behave as they do everywhere else.
    /// </summary>
    /// <remarks>
    /// Serves every shape from one implementation: <see cref="EaseKind.Cubic"/> is the two-knot case with its
    /// anchors pinned and dead outer handles hidden, <see cref="EaseKind.Keyed"/> adds interior knots
    /// (double-click inserts one, Delete removes, 1-5 set tangent modes), and <see cref="EaseKind.Builtin"/>
    /// renders read-only. Edits go through a detached working copy committed per gesture through the binding,
    /// one undo step per gesture.
    /// </remarks>
    public sealed class EaseCurveField : VisualElement
    {
        private const float Margin = 18f;
        private const float SplitPickPixels = 14f;
        private const float SnapStep = 0.05f;
        private const float NudgeStep = 0.01f;
        private const float NudgeStepLarge = 0.05f;
        private const int CurveSamples = 256;

        private static readonly CustomStyleProperty<Color> gridMinorProperty = new("--lightside-hierarchy-guide");
        private static readonly CustomStyleProperty<Color> gridMajorProperty = new("--lightside-control-border");
        private static readonly CustomStyleProperty<Color> accentHoverProperty = new("--lightside-accent-hover");

        private readonly SerializedPropertyBinding binding;
        private readonly RectEditSurface surface = new();
        private readonly List<Label> axisLabels = new();

        private Ease value;
        private BezierKnot[] working;
        private EasePointSet points;
        private PointEditor editor;

        private int undoGroup = -1;
        private bool pointerHeld;
        private bool hovering;
        private Vector2 hoverData;
        private int hoveredPoint = -1;
        private EventModifiers lastModifiers;

        private Color accent;
        private Color accentHover;
        private Color gridMinor;
        private Color gridMajor;

        /// <summary>Raised whenever the curve, the selection or the hover state changed — the host syncs its chrome from it.</summary>
        public event Action Changed;

        /// <summary>The value currently shown, kept in step with every commit.</summary>
        public Ease Value => value;

        /// <summary>Whether a gesture (drag, box-select or modal transform) is in progress.</summary>
        public bool IsBusy => pointerHeld || (editor?.IsBusy ?? false);

        /// <summary>Binds the canvas to an <see cref="Ease"/> field on every selected target.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
        public EaseCurveField(SerializedPropertyBinding binding)
        {
            this.binding = binding ?? throw new ArgumentNullException(nameof(binding));

            AddToClassList("lightside-curve-canvas");
            focusable = true;
            tabIndex = 0;
            generateVisualContent += Paint;

            accent = EditorResources.ToggleAccent;
            accentHover = accent;
            var pro = EditorGUIUtility.isProSkin;
            gridMinor = pro ? new Color(0.745f, 0.745f, 0.745f, 0.18f) : new Color(0.196f, 0.196f, 0.196f, 0.34f);
            gridMajor = pro ? new Color(0.133f, 0.133f, 0.133f) : new Color(0.627f, 0.627f, 0.627f);

            RegisterCallback<CustomStyleResolvedEvent>(OnStylesResolved);
            RegisterCallback<GeometryChangedEvent>(_ => MarkDirtyRepaint());
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerCaptureOutEvent>(_ => { pointerHeld = false; CloseSessionIfIdle(); });
            RegisterCallback<PointerLeaveEvent>(_ => { SetHover(false, hoverData); });
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<AttachToPanelEvent>(_ => Undo.undoRedoPerformed += OnUndoRedo);
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Undo.undoRedoPerformed -= OnUndoRedo;
                pointerHeld = false;
                CloseSessionIfIdle();
            });
            this.AddManipulator(new InspectorContextMenuManipulator(PopulateMenu));

            Reload();
        }

        /// <summary>Reloads the canvas from the bound value; a no-op while a gesture is in progress.</summary>
        public void Refresh()
        {
            if (IsBusy) return;
            Reload();
        }

        /// <summary>The line the host's readout shows: modal HUD, drag coordinates, or the hovered curve sample.</summary>
        public string ReadoutText
        {
            get
            {
                if (editor != null && editor.IsBusy && editor.StatusText.Length > 0)
                    return editor.StatusText;
                if (TryGetSoloPoint(out var solo) && pointerHeld)
                    return $"{solo.x:0.###}, {solo.y:0.###}";
                if (hovering)
                    return $"t {Mathf.Clamp01(hoverData.x):0.##} → {value.Evaluate(Mathf.Clamp01(hoverData.x)):0.##}";
                return value.Kind == EaseKind.Keyed ? "Double-click adds a key" : string.Empty;
            }
        }

        /// <summary>The single selected point's data coordinates, when exactly one point is selected.</summary>
        public bool TryGetSoloPoint(out Vector2 position)
        {
            position = default;
            if (editor == null || editor.Selection.Count != 1) return false;
            var world = points.GetPosition(editor.Selection.Indices[0]);
            position = new Vector2(world.x, world.y);
            return true;
        }

        /// <summary>Moves the single selected point to exact coordinates — the numeric-field entry path.</summary>
        public void MoveSoloPoint(Vector2 position)
        {
            if (editor == null || editor.Selection.Count != 1) return;
            var index = editor.Selection.Indices[0];
            points.SetPosition(index, new Vector3(position.x, position.y, 0f));
            EnforceTangents();
            OpenSession();
            CommitWorking();
            CloseSessionIfIdle();
        }

        private void OnStylesResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(gridMinorProperty, out var minor)) gridMinor = minor;
            if (evt.customStyle.TryGetValue(gridMajorProperty, out var major)) gridMajor = major;
            if (evt.customStyle.TryGetValue(accentHoverProperty, out var hover)) accentHover = hover;
            MarkDirtyRepaint();
        }

        private void OnUndoRedo()
        {
            undoGroup = -1;
            Refresh();
        }

        private void Reload()
        {
            value = binding.Value is Ease ease ? ease : default;
            var count = value.KnotCount;
            if (count == 0)
            {
                working = null;
                points = null;
                editor = null;
            }
            else
            {
                var knots = new BezierKnot[count];
                for (var i = 0; i < count; i++) knots[i] = value.GetAuthoredKnot(i);
                BindEditor(knots);
            }
            MarkDirtyRepaint();
            Changed?.Invoke();
        }

        private void BindEditor(BezierKnot[] knots, int selectPoint = -1)
        {
            working = knots;
            points = new EasePointSet(working, value.Kind == EaseKind.Cubic, null);
            editor = new PointEditor(points, surface)
            {
                BoxSelectFill = BezierKnotVisuals.MarqueeFill(accent),
                BoxSelectOutline = BezierKnotVisuals.MarqueeOutline(accent),
            };
            editor.Changed += () =>
            {
                MarkDirtyRepaint();
                Changed?.Invoke();
            };
            editor.Edited += OnEdited;
            editor.DeleteRequested += OnDeleteRequested;
            if (selectPoint >= 0 && selectPoint < points.Count)
                editor.Selection.Add(selectPoint);
        }

        private void OnEdited()
        {
            EnforceTangents();
            if ((lastModifiers & (EventModifiers.Control | EventModifiers.Command)) != 0)
                SnapSelection();
            CommitWorking();
        }

        private void EnforceTangents()
        {
            if (editor == null) return;
            foreach (var index in editor.Selection.Indices)
            {
                var knot = EasePointSet.KnotIndex(index);
                if (EasePointSet.IsAnchor(index))
                {
                    RecomputeDerived(knot);
                    RecomputeDerived(knot - 1);
                    RecomputeDerived(knot + 1);
                    continue;
                }

                var side = index % 3 == 1 ? HandleSide.In : HandleSide.Out;
                ref var k = ref working[knot];
                switch (k.mode)
                {
                    case TangentMode.Free:
                        break;
                    case TangentMode.Aligned:
                        BezierKnotEdit.AlignOpposite(ref k, side, keepLength: true);
                        break;
                    case TangentMode.Mirrored:
                        BezierKnotEdit.AlignOpposite(ref k, side, keepLength: false);
                        break;
                    default:
                        k.mode = TangentMode.Aligned;
                        BezierKnotEdit.AlignOpposite(ref k, side, keepLength: true);
                        break;
                }
            }
        }

        private void RecomputeDerived(int knot)
        {
            if (knot < 0 || knot >= working.Length) return;
            ref var k = ref working[knot];
            var previous = working[Mathf.Max(0, knot - 1)].position;
            var next = working[Mathf.Min(working.Length - 1, knot + 1)].position;
            if (k.mode == TangentMode.Auto) BezierKnotEdit.Auto(ref k, previous, next);
            else if (k.mode == TangentMode.Vector) BezierKnotEdit.Vector(ref k, previous, next);
        }

        private void SnapSelection()
        {
            foreach (var index in editor.Selection.Indices)
            {
                var world = points.GetPosition(index);
                points.SetPosition(index, new Vector3(
                    Mathf.Round(world.x / SnapStep) * SnapStep,
                    Mathf.Round(world.y / SnapStep) * SnapStep, 0f));
            }
        }

        private void CommitWorking()
        {
            var built = value.Kind == EaseKind.Cubic
                ? Ease.Cubic(working[0].outHandle.x, working[0].outHandle.y,
                    working[1].inHandle.x, working[1].inHandle.y)
                : Ease.Keyed(working);
            OpenSession();
            binding.TransformValue(_ => built, "Edit Ease Curve");
            value = built;
            MarkDirtyRepaint();
            Changed?.Invoke();
        }

        private void OpenSession()
        {
            if (undoGroup >= 0) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Edit Ease Curve");
            undoGroup = Undo.GetCurrentGroup();
        }

        private void CloseSessionIfIdle()
        {
            if (undoGroup < 0 || pointerHeld || (editor?.IsBusy ?? false)) return;
            Undo.CollapseUndoOperations(undoGroup);
            undoGroup = -1;
        }

        /// <summary>
        /// Ends a gesture: re-commits the working copy — after a cancelled modal transform it holds the
        /// restored positions while the binding holds the last mid-gesture write — then collapses the session.
        /// </summary>
        private void SettleGesture()
        {
            if (undoGroup >= 0 && !pointerHeld && editor != null && !editor.IsBusy)
                CommitWorking();
            CloseSessionIfIdle();
        }

        private void OnDeleteRequested(IReadOnlyList<int> selection)
        {
            if (value.Kind != EaseKind.Keyed || working.Length <= 2) return;

            var doomed = new SortedSet<int>();
            foreach (var index in selection)
            {
                var knot = EasePointSet.KnotIndex(index);
                if (EasePointSet.IsAnchor(index) && knot > 0 && knot < working.Length - 1)
                    doomed.Add(knot);
            }
            if (doomed.Count == 0 || working.Length - doomed.Count < 2) return;

            var next = new BezierKnot[working.Length - doomed.Count];
            var write = 0;
            for (var i = 0; i < working.Length; i++)
                if (!doomed.Contains(i))
                    next[write++] = working[i];

            BindEditor(next);
            CommitWorking();
        }

        private void TryAddKey(Vector2 guiPosition)
        {
            if (value.Kind != EaseKind.Keyed || working == null) return;
            if (!FindCurvePoint(guiPosition, out var segment, out var t)) return;

            var a = working[segment];
            var b = working[segment + 1];
            var middle = BezierKnotEdit.Split(ref a, ref b, t);

            var next = new BezierKnot[working.Length + 1];
            Array.Copy(working, next, segment);
            next[segment] = a;
            next[segment + 1] = middle;
            next[segment + 2] = b;
            Array.Copy(working, segment + 2, next, segment + 3, working.Length - segment - 2);

            BindEditor(next, (segment + 1) * 3);
            CommitWorking();
        }

        private bool FindCurvePoint(Vector2 guiPosition, out int segment, out float t)
        {
            segment = -1;
            t = 0f;
            var best = SplitPickPixels;
            for (var seg = 0; seg < working.Length - 1; seg++)
            {
                var a = working[seg];
                var b = working[seg + 1];
                for (var i = 0; i <= 24; i++)
                {
                    var tt = i / 24f;
                    var gui = surface.DataToGui(Sample(a, b, tt));
                    var distance = Vector2.Distance(gui, guiPosition);
                    if (distance < best)
                    {
                        best = distance;
                        segment = seg;
                        t = tt;
                    }
                }
            }
            return segment >= 0;
        }

        private static Vector2 Sample(in BezierKnot a, in BezierKnot b, float t)
        {
            var u = 1f - t;
            return u * u * u * a.position + 3f * u * u * t * a.outHandle
                 + 3f * u * t * t * b.inHandle + t * t * t * b.position;
        }

        private void PopulateMenu(DropdownMenu menu)
        {
            if (value.Kind == EaseKind.Keyed && editor != null)
            {
                if (hovering && FindCurvePoint(surface.DataToGui(hoverData), out _, out _))
                    InspectorContextMenu.AppendCommand(menu, "Add Key",
                        EditorResources.GetTexture("plus"),
                        _ => TryAddKey(surface.DataToGui(hoverData)));

                if (HasDeletableSelection())
                    InspectorContextMenu.AppendCommand(menu, "Delete Key",
                        EditorResources.GetTexture("trash"),
                        _ => OnDeleteRequested(editor.Selection.Indices));

                if (editor.Selection.Count > 0)
                {
                    AppendTangentMode(menu, "Tangents/Free", TangentMode.Free);
                    AppendTangentMode(menu, "Tangents/Aligned", TangentMode.Aligned);
                    AppendTangentMode(menu, "Tangents/Mirrored", TangentMode.Mirrored);
                    AppendTangentMode(menu, "Tangents/Auto", TangentMode.Auto);
                    AppendTangentMode(menu, "Tangents/Vector", TangentMode.Vector);
                }
            }

            InspectorContextMenu.AppendCommand(menu, "Copy", InspectorContextMenu.CopyIcon,
                _ => PropertyClipboard.CopyObject(value));
            PropertyClipboardMenu.AppendPaste(menu, "Paste", typeof(Ease), pasted =>
            {
                binding.TransformValue(_ => (Ease)pasted, "Paste Ease");
                Reload();
            });
        }

        private bool HasDeletableSelection()
        {
            if (editor == null) return false;
            foreach (var index in editor.Selection.Indices)
            {
                var knot = EasePointSet.KnotIndex(index);
                if (EasePointSet.IsAnchor(index) && knot > 0 && knot < working.Length - 1)
                    return true;
            }
            return false;
        }

        private void AppendTangentMode(DropdownMenu menu, string path, TangentMode mode)
            => menu.AppendAction(path,
                _ => SetSelectedModes(mode),
                _ => SelectionModesAre(mode)
                    ? DropdownMenuAction.Status.Checked
                    : DropdownMenuAction.Status.Normal);

        private bool SelectionModesAre(TangentMode mode)
        {
            var any = false;
            foreach (var index in editor.Selection.Indices)
            {
                any = true;
                if (working[EasePointSet.KnotIndex(index)].mode != mode) return false;
            }
            return any;
        }

        private void SetSelectedModes(TangentMode mode)
        {
            if (editor == null || editor.Selection.Count == 0) return;
            foreach (var index in editor.Selection.Indices)
            {
                var knot = EasePointSet.KnotIndex(index);
                ref var k = ref working[knot];
                k.mode = mode;
                switch (mode)
                {
                    case TangentMode.Auto:
                    case TangentMode.Vector:
                        RecomputeDerived(knot);
                        break;
                    case TangentMode.Mirrored:
                        BezierKnotEdit.AlignOpposite(ref k, HandleSide.Out, keepLength: false);
                        break;
                    case TangentMode.Aligned:
                        BezierKnotEdit.AlignOpposite(ref k, HandleSide.Out, keepLength: true);
                        break;
                }
            }
            OpenSession();
            CommitWorking();
            CloseSessionIfIdle();
        }

        private void NudgeSelection(Vector2 delta)
        {
            if (editor == null || editor.Selection.Count == 0) return;

            var affected = new List<int>();
            var seen = new HashSet<int>();
            var dependents = new List<int>();
            foreach (var index in editor.Selection.Indices)
            {
                if (seen.Add(index)) affected.Add(index);
                dependents.Clear();
                points.GetRigidDependents(index, dependents);
                foreach (var dependent in dependents)
                    if (seen.Add(dependent))
                        affected.Add(dependent);
            }

            foreach (var index in affected)
            {
                var world = points.GetPosition(index);
                points.SetPosition(index, world + new Vector3(delta.x, delta.y, 0f));
            }

            EnforceTangents();
            OpenSession();
            CommitWorking();
            CloseSessionIfIdle();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (editor == null) return;
            lastModifiers = evt.modifiers;
            surface.SetPointer(evt.localPosition);

            if (evt.button == 0 && evt.clickCount >= 2 && value.Kind == EaseKind.Keyed &&
                PickNearestPoint(editor.PickPixelRadius) < 0)
                TryAddKey(evt.localPosition);

            if (evt.button != 0) return;
            if (!editor.HandleEvent(new PointEditEvent(
                    PointEditEventType.PointerDown, evt.button, modifiers: evt.modifiers)))
                return;
            pointerHeld = true;
            Focus();
            this.CapturePointer(evt.pointerId);
            Consume(evt);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            lastModifiers = evt.modifiers;
            surface.SetPointer(evt.localPosition);
            SetHover(true, surface.PointerPlane);
            UpdateHoveredPoint();

            if (editor == null) return;
            if (!editor.HandleEvent(new PointEditEvent(
                    PointEditEventType.PointerMove, evt.button, modifiers: evt.modifiers)))
                return;
            Consume(evt);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            lastModifiers = evt.modifiers;
            surface.SetPointer(evt.localPosition);
            var consumed = editor != null && editor.HandleEvent(new PointEditEvent(
                PointEditEventType.PointerUp, evt.button, modifiers: evt.modifiers));
            pointerHeld = false;
            if (this.HasPointerCapture(evt.pointerId)) this.ReleasePointer(evt.pointerId);
            SettleGesture();
            if (consumed) Consume(evt);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            lastModifiers = evt.modifiers;

            if (editor != null && !editor.IsBusy)
            {
                var nudge = evt.shiftKey ? NudgeStepLarge : NudgeStep;
                Vector2 delta = evt.keyCode switch
                {
                    KeyCode.LeftArrow => new Vector2(-nudge, 0f),
                    KeyCode.RightArrow => new Vector2(nudge, 0f),
                    KeyCode.UpArrow => new Vector2(0f, nudge),
                    KeyCode.DownArrow => new Vector2(0f, -nudge),
                    _ => Vector2.zero,
                };
                if (delta != Vector2.zero && editor.Selection.Count > 0)
                {
                    NudgeSelection(delta);
                    Consume(evt);
                    return;
                }

                if (value.Kind == EaseKind.Keyed && editor.Selection.Count > 0 &&
                    evt.modifiers == EventModifiers.None &&
                    TangentModeOf(evt.keyCode, out var mode))
                {
                    SetSelectedModes(mode);
                    Consume(evt);
                    return;
                }
            }

            if (editor == null) return;
            if (!editor.HandleEvent(new PointEditEvent(
                    PointEditEventType.KeyDown, keyCode: evt.keyCode,
                    character: evt.character, modifiers: evt.modifiers)))
            {
                SettleGesture();
                return;
            }
            SettleGesture();
            Consume(evt);
        }

        private static bool TangentModeOf(KeyCode key, out TangentMode mode)
        {
            switch (key)
            {
                case KeyCode.Alpha1: mode = TangentMode.Free; return true;
                case KeyCode.Alpha2: mode = TangentMode.Aligned; return true;
                case KeyCode.Alpha3: mode = TangentMode.Mirrored; return true;
                case KeyCode.Alpha4: mode = TangentMode.Auto; return true;
                case KeyCode.Alpha5: mode = TangentMode.Vector; return true;
                default: mode = TangentMode.Free; return false;
            }
        }

        private void SetHover(bool state, Vector2 data)
        {
            if (hovering == state && (!state || (data - hoverData).sqrMagnitude < 1e-8f)) return;
            hovering = state;
            hoverData = data;
            if (!state) hoveredPoint = -1;
            MarkDirtyRepaint();
            Changed?.Invoke();
        }

        private void UpdateHoveredPoint()
        {
            var previous = hoveredPoint;
            hoveredPoint = points != null && !pointerHeld ? PickNearestPoint(9f) : -1;
            if (previous != hoveredPoint) MarkDirtyRepaint();
        }

        private int PickNearestPoint(float pixelRadius)
        {
            var best = pixelRadius;
            var found = -1;
            for (var i = 0; i < points.Count; i++)
            {
                var distance = surface.PointerScreenDistance(points.GetPosition(i));
                if (distance < best)
                {
                    best = distance;
                    found = i;
                }
            }
            return found;
        }

        private Rect DataBounds()
        {
            float minX = 0f, maxX = 1f, minY = 0f, maxY = 1f;
            if (working != null)
            {
                foreach (var knot in working)
                {
                    minX = Mathf.Min(minX, knot.inHandle.x, knot.outHandle.x);
                    maxX = Mathf.Max(maxX, knot.inHandle.x, knot.outHandle.x);
                    minY = Mathf.Min(minY, knot.position.y, knot.inHandle.y, knot.outHandle.y);
                    maxY = Mathf.Max(maxY, knot.position.y, knot.inHandle.y, knot.outHandle.y);
                }
            }
            for (var i = 0; i <= CurveSamples; i++)
            {
                var y = value.Evaluate(i / (float)CurveSamples);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private void Paint(MeshGenerationContext context)
        {
            var view = contentRect;
            if (view.width <= 4f || view.height <= 4f) return;

            var boundsRect = DataBounds();
            surface.SetView(view, boundsRect, Margin, preserveAspect: false);
            var painter = context.painter2D;

            PaintOvershootWash(painter, boundsRect);
            PaintGrid(painter, boundsRect);
            PaintLinearGhost(painter);
            PaintCurve(painter);
            PaintHover(painter);

            if (editor != null && working != null)
            {
                surface.BeginDraw(painter);
                try
                {
                    var options = BezierKnotVisuals.Accented(accent, accentHover);
                    options.showHandles = true;
                    options.showOuterHandles = false;
                    options.showCurve = false;
                    PaintKnotOverlay(painter, options);
                    editor.DrawBoxSelect();
                }
                finally
                {
                    surface.EndDraw();
                }
            }

            SyncAxisLabels(boundsRect);
        }

        private void PaintKnotOverlay(Painter2D painter, BezierKnotVisuals.Options options)
        {
            BezierKnotVisuals.Draw(surface, points.Knots, false, editor.Selection, options,
                Matrix4x4.identity);
            if (hoveredPoint < 0) return;

            var world = points.GetPosition(hoveredPoint);
            var gui = surface.DataToGui(new Vector2(world.x, world.y));
            painter.strokeColor = accentHover;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.Arc(gui, 7f, 0f, 360f);
            painter.Stroke();
        }

        private void PaintOvershootWash(Painter2D painter, Rect boundsRect)
        {
            var wash = new Color(0f, 0f, 0f, 0.07f);
            if (boundsRect.yMax > 1f)
                FillDataRect(painter, Rect.MinMaxRect(0f, 1f, 1f, boundsRect.yMax), wash);
            if (boundsRect.yMin < 0f)
                FillDataRect(painter, Rect.MinMaxRect(0f, boundsRect.yMin, 1f, 0f), wash);
        }

        private void FillDataRect(Painter2D painter, Rect dataRect, Color color)
        {
            var a = surface.DataToGui(new Vector2(dataRect.xMin, dataRect.yMin));
            var b = surface.DataToGui(new Vector2(dataRect.xMax, dataRect.yMax));
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(a.x, a.y));
            painter.LineTo(new Vector2(b.x, a.y));
            painter.LineTo(new Vector2(b.x, b.y));
            painter.LineTo(new Vector2(a.x, b.y));
            painter.ClosePath();
            painter.Fill();
        }

        private void PaintGrid(Painter2D painter, Rect boundsRect)
        {
            var xStep = AxisScale.NiceStep(40f / Mathf.Max(1f, surface.Scale.x));
            var yStep = AxisScale.NiceStep(28f / Mathf.Max(1f, surface.Scale.y));

            painter.strokeColor = gridMinor;
            painter.lineWidth = 1f;
            for (var x = 0f; x <= 1f + 1e-4f; x += xStep)
                StrokeDataLine(painter, new Vector2(x, boundsRect.yMin), new Vector2(x, boundsRect.yMax));
            var firstY = Mathf.Ceil(boundsRect.yMin / yStep) * yStep;
            for (var y = firstY; y <= boundsRect.yMax + 1e-4f; y += yStep)
                StrokeDataLine(painter, new Vector2(0f, y), new Vector2(1f, y));

            painter.strokeColor = gridMajor;
            StrokeDataLine(painter, new Vector2(0f, boundsRect.yMin), new Vector2(0f, boundsRect.yMax));
            StrokeDataLine(painter, new Vector2(1f, boundsRect.yMin), new Vector2(1f, boundsRect.yMax));
            StrokeDataLine(painter, Vector2.zero, Vector2.right);
            StrokeDataLine(painter, Vector2.up, Vector2.one);
        }

        private void StrokeDataLine(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(surface.DataToGui(from));
            painter.LineTo(surface.DataToGui(to));
            painter.Stroke();
        }

        private void PaintLinearGhost(Painter2D painter)
        {
            painter.strokeColor = gridMinor;
            painter.lineWidth = 1f;
            StrokeDataLine(painter, Vector2.zero, Vector2.one);
        }

        private void PaintCurve(Painter2D painter)
        {
            painter.fillColor = new Color(accent.r, accent.g, accent.b, 0.08f);
            painter.BeginPath();
            painter.MoveTo(surface.DataToGui(new Vector2(0f, 0f)));
            AppendCurvePath(painter, moveToStart: false);
            painter.LineTo(surface.DataToGui(new Vector2(1f, 0f)));
            painter.ClosePath();
            painter.Fill();

            painter.strokeColor = accent;
            painter.lineWidth = 2f;
            painter.lineJoin = LineJoin.Round;
            painter.lineCap = LineCap.Round;
            painter.BeginPath();
            AppendCurvePath(painter, moveToStart: true);
            painter.Stroke();
        }

        /// <summary>Appends the runtime curve — what evaluation produces, whatever the authored handles say.</summary>
        private void AppendCurvePath(Painter2D painter, bool moveToStart)
        {
            var first = surface.DataToGui(new Vector2(0f, value.Evaluate(0f)));
            if (moveToStart) painter.MoveTo(first);
            else painter.LineTo(first);
            for (var i = 1; i <= CurveSamples; i++)
            {
                var t = i / (float)CurveSamples;
                painter.LineTo(surface.DataToGui(new Vector2(t, value.Evaluate(t))));
            }
        }

        private void PaintHover(Painter2D painter)
        {
            if (!hovering || pointerHeld || hoveredPoint >= 0) return;
            var x = Mathf.Clamp01(hoverData.x);
            var gui = surface.DataToGui(new Vector2(x, value.Evaluate(x)));
            painter.fillColor = accentHover;
            painter.BeginPath();
            painter.Arc(gui, 3f, 0f, 360f);
            painter.Fill();
        }

        private void SyncAxisLabels(Rect boundsRect)
        {
            var needed = 0;
            PlaceLabel(ref needed, "0", new Vector2(0f, boundsRect.yMin), new Vector2(2f, -12f));
            PlaceLabel(ref needed, "0.5", new Vector2(0.5f, boundsRect.yMin), new Vector2(-8f, -12f));
            PlaceLabel(ref needed, "1", new Vector2(1f, boundsRect.yMin), new Vector2(-8f, -12f));
            PlaceLabel(ref needed, "0", new Vector2(0f, 0f), new Vector2(-12f, -6f));
            PlaceLabel(ref needed, "1", new Vector2(0f, 1f), new Vector2(-12f, -6f));

            for (var i = needed; i < axisLabels.Count; i++)
                axisLabels[i].style.display = DisplayStyle.None;
        }

        private void PlaceLabel(ref int used, string text, Vector2 data, Vector2 offset)
        {
            while (axisLabels.Count <= used)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("lightside-curve-canvas__label");
                label.style.position = Position.Absolute;
                Add(label);
                axisLabels.Add(label);
            }

            var gui = surface.DataToGui(data) + offset;
            var label2 = axisLabels[used++];
            label2.text = text;
            label2.style.display = DisplayStyle.Flex;
            label2.style.left = gui.x;
            label2.style.top = gui.y;
        }

        private static void Consume(EventBase evt)
        {
#if !UNITY_6000_0_OR_NEWER
            evt.PreventDefault();
#endif
            evt.StopImmediatePropagation();
        }
    }
}
