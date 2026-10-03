using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Reusable UI Toolkit list chrome for compact, variable-height Inspector collections.</summary>
    public sealed class InspectorListView : VisualElement
    {
        private readonly Func<VisualElement> makeItem;
        private readonly Action<VisualElement, int> bindItem;
        private readonly Action<VisualElement, int> refreshItem;
        private readonly Action<VisualElement> unbindItem;
        private readonly Func<int, object> itemKey;
        private readonly bool reorderable;
        private readonly VisualElement body;
        private readonly List<VisualElement> rows = new();
        private readonly Dictionary<VisualElement, object> boundKeys = new();
        private readonly Dictionary<VisualElement, int> boundIndices = new();
        private readonly HashSet<VisualElement> materialized = new();
        private readonly List<object> rebuildKeys = new();
        private readonly HashSet<object> uniqueKeys = new();
        private bool isRebuilding;
        private (int count, bool expanded)? queuedRebuild;
        private readonly List<ScrollView> scrollHosts = new();
        private readonly Dictionary<VisualElement, InspectorMotion.Ticker> rowMotions = new();
        private readonly List<VisualElement> pendingReveals = new();
        private bool animateReconcile;
        private int pendingRemovalIndex = -1;
        private int ghostCount;
        private bool lastExpanded;
        private int deferredCount = -1;
        private bool updatingWindow;
        private VisualElement draggedRow;
        private VisualElement dragPlaceholder;
        private VisualElement dragHandle;
        private VisualElement dragCursorRoot;
        private float[] dragTops;
        private float[] dragBottoms;
        private float dragPointerOffset;
        private float dragRowHeight;
        private float dragCurrentWorldTop;
        private int dragPointerId = -1;
        private int dragFrom = -1;
        private int dragTo = -1;


        /// <summary>
        /// Creates a retained list with caller-owned row contents and binding. Supply
        /// <paramref name="itemKey"/> when elements have stable identities so reordering can preserve
        /// their rows; null or duplicate keys use positional rebinding. Supply
        /// <paramref name="refreshItem"/> to synchronize retained row contents without replacing them.
        /// </summary>
        public InspectorListView(string label, Func<VisualElement> makeItem,
            Action<VisualElement, int> bindItem, Action<VisualElement> unbindItem = null,
            bool reorderable = true, Func<int, object> itemKey = null,
            Action<VisualElement, int> refreshItem = null)
        {
            InspectorVisuals.Attach(this);
            this.makeItem = makeItem ?? throw new ArgumentNullException(nameof(makeItem));
            this.bindItem = bindItem ?? throw new ArgumentNullException(nameof(bindItem));
            this.refreshItem = refreshItem;
            this.unbindItem = unbindItem;
            this.reorderable = reorderable;
            this.itemKey = itemKey;

            Header = new InspectorListHeader(label, $"Add {label}");
            Add(Header);

            body = new VisualElement();
            body.AddToClassList("lightside-list__body");
            body.style.display = DisplayStyle.None;
            body.RegisterCallback<GeometryChangedEvent>(_ => UpdateVisibleWindow());
            Add(body);
            RegisterCallback<AttachToPanelEvent>(_ => TrackViewport());
            RegisterCallback<DetachFromPanelEvent>(_ => UntrackViewport());

            Header.Changed += value =>
            {
                SetExpanded(value, true);
                ExpandedChanged?.Invoke(value);
            };
            Header.AddManipulator(new InspectorContextMenuManipulator(menu =>
            {
                HeaderMenuOpening?.Invoke(menu);
                if (!Header.HasItems || ClearRequested == null) return;
                menu.AppendSeparator(string.Empty);
                InspectorContextMenu.AppendCommand(menu, "Clear",
                    InspectorContextMenu.ClearIcon, _ => ClearRequested.Invoke());
            }));
        }

        /// <summary>Header containing disclosure, count, and add controls.</summary>
        public InspectorListHeader Header { get; }

        /// <summary>Number of retained rows.</summary>
        public int Count => rows.Count;

        /// <summary>Raised after the user reorders a row.</summary>
        public event Action<int, int> ItemIndexChanged;

        /// <summary>Raised after the user changes the disclosure state.</summary>
        public event Action<bool> ExpandedChanged;

        /// <summary>Raised to populate actions before the header's Clear command.</summary>
        public event Action<DropdownMenu> HeaderMenuOpening;

        /// <summary>
        /// Raised to populate the context menu of the row at the supplied index, including its
        /// reorder handle and surrounding chrome. Without a handler a row opens no menu; it never
        /// falls back to the containing list's own menu.
        /// </summary>
        public event Action<DropdownMenu, int> RowMenuOpening;

        /// <summary>Raised when the user requests removal of every list item.</summary>
        public event Action ClearRequested;

        /// <summary>Synchronizes the retained rows with the collection and its disclosure state.</summary>
        public void Rebuild(int count, bool isExpanded)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (isRebuilding)
            {
                queuedRebuild = (count, isExpanded);
                return;
            }

            isRebuilding = true;
            try
            {
                while (true)
                {
                    queuedRebuild = null;
                    RebuildCore(count, isExpanded);
                    if (!queuedRebuild.HasValue) break;
                    (count, isExpanded) = queuedRebuild.Value;
                }
            }
            finally
            {
                isRebuilding = false;
                queuedRebuild = null;
                rebuildKeys.Clear();
                uniqueKeys.Clear();
            }
        }

        private void RebuildCore(int count, bool isExpanded)
        {
            CancelDrag();

            Header.SetCount(count);
            Header.SetHasBody(count > 0);
            if (isExpanded)
            {
                deferredCount = -1;
                if (count > 0)
                    InspectorMotion.SetExpanded(body, true, animate: false);
                ReconcileRows(count, animate: true);
            }
            else
                deferredCount = count;
            SetExpanded(isExpanded);
        }

        /// <summary>
        /// Synchronizes the retained rows with the model. Rows are structural only; their contents
        /// are bound later by <see cref="UpdateVisibleWindow"/> once the row is near the viewport.
        /// With <paramref name="animate"/>, an attached list unrolls rows the model gained and
        /// folds rows it lost, whatever caused the change — a button, a paste, an undo.
        /// </summary>
        private void ReconcileRows(int count, bool animate)
        {
            animateReconcile = animate && panel != null;
            var countChanged = count != rows.Count;
            var distinctKeys = CollectDistinctKeys(count);
            if (distinctKeys)
                RebuildKeyed(count);
            else
                RebuildPositional(count, !countChanged);
            pendingRemovalIndex = -1;
            rebuildKeys.Clear();
            animateReconcile = false;
            UpdateVisibleWindow();
            for (var i = 0; i < pendingReveals.Count; i++)
            {
                var row = pendingReveals[i];
                row.style.display = DisplayStyle.None;
                InspectorMotion.SetExpanded(row, true);
            }
            pendingReveals.Clear();
        }

        private bool CollectDistinctKeys(int count)
        {
            rebuildKeys.Clear();
            uniqueKeys.Clear();
            if (itemKey == null) return false;

            var distinct = true;
            for (var index = 0; index < count; index++)
            {
                var key = itemKey(index);
                rebuildKeys.Add(key);
                if (key == null || !uniqueKeys.Add(key)) distinct = false;
            }
            uniqueKeys.Clear();
            return distinct;
        }

        private void RebuildPositional(int count, bool rebindChanged)
        {
            if (pendingRemovalIndex >= 0 && count == rows.Count - 1 &&
                pendingRemovalIndex < rows.Count)
            {
                var removed = rows[pendingRemovalIndex];
                rows.RemoveAt(pendingRemovalIndex);
                ReleaseRow(removed);
                for (var i = pendingRemovalIndex; i < rows.Count; i++)
                    Rematerialize(rows[i], i);
            }
            pendingRemovalIndex = -1;
            while (rows.Count > count)
                RemoveLastRow();
            while (rows.Count < count)
                AddRow();

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var key = itemKey == null ? index : rebuildKeys[index] ?? index;
                var identityChanged = !boundKeys.TryGetValue(row, out var previousKey) ||
                                      (rebindChanged &&
                                       (!Equals(previousKey, key) ||
                                        !boundIndices.TryGetValue(row, out var previousIndex) ||
                                        previousIndex != index));
                PlaceRow(row, index, key);
                if (identityChanged)
                    Rematerialize(row, index);
                else if (materialized.Contains(row))
                    refreshItem?.Invoke(row, index);
            }
        }

        /// <summary>
        /// Extracts rows whose key left the model before matching, so survivors keep their slots
        /// and a removed row folds closed in place instead of sinking to the tail.
        /// </summary>
        private void RebuildKeyed(int count)
        {
            uniqueKeys.Clear();
            for (var i = 0; i < count; i++)
                if (rebuildKeys[i] != null) uniqueKeys.Add(rebuildKeys[i]);
            for (var i = rows.Count - 1; i >= 0; i--)
            {
                if (!boundKeys.TryGetValue(rows[i], out var bound) ||
                    uniqueKeys.Contains(bound)) continue;
                var removed = rows[i];
                rows.RemoveAt(i);
                ReleaseRow(removed);
            }
            uniqueKeys.Clear();

            for (var index = 0; index < count; index++)
            {
                var key = rebuildKeys[index];
                var match = -1;
                for (var candidate = index; candidate < rows.Count; candidate++)
                {
                    if (!boundKeys.TryGetValue(rows[candidate], out var bound) ||
                        !Equals(bound, key)) continue;
                    match = candidate;
                    break;
                }

                if (match < 0)
                {
                    var row = CreateRow();
                    rows.Insert(index, row);
                    body.Insert(DomIndexFor(index), row);
                    PlaceRow(row, index, key);
                    if (animateReconcile) pendingReveals.Add(row);
                    continue;
                }

                var matchedRow = rows[match];
                if (match != index)
                {
                    rows.RemoveAt(match);
                    rows.Insert(index, matchedRow);
                    matchedRow.RemoveFromHierarchy();
                    body.Insert(DomIndexFor(index), matchedRow);
                }
                var moved = !boundIndices.TryGetValue(matchedRow, out var previousIndex) ||
                            previousIndex != index;
                PlaceRow(matchedRow, index, key);
                if (!materialized.Contains(matchedRow)) continue;
                if (moved && refreshItem == null)
                    Rematerialize(matchedRow, index);
                else
                    refreshItem?.Invoke(matchedRow, index);
            }

            while (rows.Count > count)
                RemoveLastRow();
        }

        /// <summary>
        /// Notes the index the model is about to lose, so the next rebuild folds that row closed
        /// in place even when elements carry no stable identity. Keyed rebuilds identify removals
        /// themselves; the note is consumed by the first rebuild and only when exactly one row
        /// left.
        /// </summary>
        public void NoteRemoval(int index) => pendingRemovalIndex = index;

        /// <summary>
        /// Updates disclosure without raising <see cref="ExpandedChanged"/>.
        /// <paramref name="animate"/> unrolls the body instead of switching it.
        /// </summary>
        public void SetExpanded(bool value, bool animate = false)
        {
            if (value && deferredCount >= 0)
            {
                var count = deferredCount;
                deferredCount = -1;
                ReconcileRows(count, animate: false);
            }
            lastExpanded = value;
            Header.SetExpandedState(value);
            InspectorMotion.SetExpanded(body,
                value && (rows.Count > 0 || ghostCount > 0), animate);
        }

        /// <summary>Creates the standard row removal action.</summary>
        public static Button CreateRemoveButton(Action clicked, string tooltip)
        {
            if (clicked == null) throw new ArgumentNullException(nameof(clicked));
            var button = new Button(clicked) { text = "−", tooltip = tooltip };
            button.AddToClassList("lightside-list__remove");
            return button;
        }

        private void AddRow()
        {
            var row = CreateRow();
            body.Add(row);
            rows.Add(row);
            if (animateReconcile) pendingReveals.Add(row);
        }

        /// <summary>
        /// Position in <see cref="body"/> just before the row that follows
        /// <paramref name="index"/>, so ghosts of removed rows never shift insertions.
        /// </summary>
        private int DomIndexFor(int index)
        {
            for (var i = index + 1; i < rows.Count; i++)
            {
                var at = body.IndexOf(rows[i]);
                if (at >= 0) return at;
            }
            return body.childCount;
        }

        private VisualElement CreateRow()
        {
            var row = makeItem() ??
                      throw new InvalidOperationException("List row factory returned null.");
            row.AddToClassList("lightside-list__row");
            row.AddManipulator(new InspectorContextMenuManipulator(menu =>
            {
                if (row.userData is int index && index >= 0)
                    RowMenuOpening?.Invoke(menu, index);
            }));
            if (reorderable)
                row.Insert(0, CreateReorderHandle(row));
            return row;
        }

        private void RemoveLastRow()
        {
            var row = rows[^1];
            rows.RemoveAt(rows.Count - 1);
            ReleaseRow(row);
        }

        /// <summary>
        /// Retires a row the model no longer contains: bindings release immediately, and while the
        /// list is on screen the row stays as a disabled ghost folding closed in place before
        /// leaving the hierarchy. The body outlives its last row until the final ghost finishes.
        /// </summary>
        private void ReleaseRow(VisualElement row)
        {
            StopRowMotion(row);
            Dematerialize(row);
            boundKeys.Remove(row);
            boundIndices.Remove(row);
            if (animateReconcile && row.style.display.value != DisplayStyle.None)
            {
                row.SetEnabled(false);
                ghostCount++;
                InspectorMotion.SetExpanded(row, false, onCompleted: () =>
                {
                    ghostCount--;
                    row.RemoveFromHierarchy();
                    if (ghostCount == 0 && rows.Count == 0) SetExpanded(lastExpanded);
                });
                return;
            }
            row.RemoveFromHierarchy();
        }

        private void PlaceRow(VisualElement row, int index, object key)
        {
            row.userData = index;
            boundKeys[row] = key;
            boundIndices[row] = index;
            row.EnableInClassList("lightside-list__row--odd", (index & 1) != 0);
        }

        private void Materialize(VisualElement row, int index)
        {
            if (!materialized.Add(row)) return;
            bindItem(row, index);
            if (row is InspectorRow inspectorRow) inspectorRow.SynchronizeItems();
        }

        private void Dematerialize(VisualElement row)
        {
            if (!materialized.Remove(row)) return;
            unbindItem?.Invoke(row);
        }

        /// <summary>
        /// Rebinds a row that already carries content, so a reorder or identity change never shows an
        /// empty row for a frame. A row that was never bound stays deferred to the viewport pass.
        /// </summary>
        private void Rematerialize(VisualElement row, int index)
        {
            if (!materialized.Remove(row)) return;
            unbindItem?.Invoke(row);
            Materialize(row, index);
        }

        /// <summary>
        /// Binds every row whose placeholder geometry reaches the viewport, extended by one screen
        /// in each direction. Binding changes row heights; the geometry pass that follows re-enters
        /// here until the window settles.
        /// </summary>
        private void UpdateVisibleWindow()
        {
            if (updatingWindow || panel == null || rows.Count == materialized.Count) return;
            if (body.style.display.value == DisplayStyle.None) return;
            var viewport = Viewport();
            if (viewport.height <= 0f) return;
            var top = viewport.yMin - viewport.height;
            var bottom = viewport.yMax + viewport.height;
            updatingWindow = true;
            try
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (materialized.Contains(row)) continue;
                    var geometry = row.worldBound;
                    if (geometry.height <= 0f) continue;
                    if (geometry.yMax < top || geometry.yMin > bottom) continue;
                    Materialize(row, i);
                }
            }
            finally
            {
                updatingWindow = false;
            }
        }

        private Rect Viewport()
        {
            if (scrollHosts.Count > 0) return scrollHosts[0].contentViewport.worldBound;
            var tree = panel?.visualTree;
            return tree != null ? tree.worldBound : body.worldBound;
        }

        /// <summary>
        /// Subscribes to every enclosing scroll view, not only the innermost one: an outer scroll
        /// moves this list through its own viewport without changing any local geometry.
        /// </summary>
        private void TrackViewport()
        {
            if (scrollHosts.Count > 0) return;
            for (var ancestor = hierarchy.parent; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (ancestor is not ScrollView scroll) continue;
                scrollHosts.Add(scroll);
                scroll.verticalScroller.valueChanged += OnHostScrolled;
            }
            UpdateVisibleWindow();
        }

        private void UntrackViewport()
        {
            for (var i = 0; i < scrollHosts.Count; i++)
                scrollHosts[i].verticalScroller.valueChanged -= OnHostScrolled;
            scrollHosts.Clear();
        }

        private void OnHostScrolled(float _) => UpdateVisibleWindow();

        private VisualElement CreateReorderHandle(VisualElement row)
        {
            var handle = new VisualElement { tooltip = "Drag to reorder" };
            handle.AddToClassList("lightside-list__reorder");
            for (var i = 0; i < 3; i++)
            {
                var bar = new VisualElement();
                bar.AddToClassList("lightside-list__reorder-bar");
                handle.Add(bar);
            }

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 || row.userData is not int index || index < 0) return;
                BeginDrag(row, handle, index, evt.pointerId, evt.position.y);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (dragHandle != handle || !handle.HasPointerCapture(evt.pointerId)) return;
                MoveDrag(evt.position.y);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (dragHandle != handle || !handle.HasPointerCapture(evt.pointerId)) return;
                var from = dragFrom;
                var to = dragTo;
                CompleteDrag();
                handle.ReleasePointer(evt.pointerId);
                if (from >= 0 && to >= 0 && from != to)
                    ItemIndexChanged?.Invoke(from, to);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (dragHandle == handle) CancelDrag();
            });
            return handle;
        }

        /// <summary>
        /// Captures row slots in layout space, so rows still sliding home from an earlier drop do
        /// not distort the geometry a new drag is measured against.
        /// </summary>
        private void BeginDrag(VisualElement row, VisualElement handle, int index,
            int pointerId, float pointerY)
        {
            CancelDrag();
            var bodyTop = body.worldBound.yMin;
            dragTops = new float[rows.Count];
            dragBottoms = new float[rows.Count];
            for (var i = 0; i < rows.Count; i++)
            {
                dragTops[i] = bodyTop + rows[i].layout.yMin;
                dragBottoms[i] = bodyTop + rows[i].layout.yMax;
            }

            dragFrom = index;
            dragTo = index;
            draggedRow = row;
            dragHandle = handle;
            dragPointerId = pointerId;
            dragRowHeight = row.layout.height;
            dragCurrentWorldTop = dragTops[index] + row.resolvedStyle.translate.y;
            dragPointerOffset = pointerY - dragCurrentWorldTop;

            StopRowMotion(row);
            dragPlaceholder = new VisualElement();
            dragPlaceholder.AddToClassList("lightside-list__placeholder");
            dragPlaceholder.style.height = dragRowHeight;
            body.Insert(body.IndexOf(row), dragPlaceholder);
            row.RemoveFromHierarchy();
            body.Add(row);
            row.style.position = Position.Absolute;
            row.style.left = 0f;
            row.style.right = 0f;
            row.style.top = dragCurrentWorldTop - bodyTop;
            row.style.height = dragRowHeight;
            row.style.translate = VerticalOffset(0f);
            handle.AddToClassList("lightside-list__reorder--dragging");
            dragCursorRoot = panel?.visualTree;
            dragCursorRoot?.AddToClassList("lightside-list-dragging");
            handle.CapturePointer(pointerId);
        }

        private void MoveDrag(float pointerY)
        {
            var bodyTop = body.worldBound.yMin;
            var bodyBottom = body.worldBound.yMax;
            dragCurrentWorldTop = Mathf.Clamp(pointerY - dragPointerOffset,
                bodyTop, Mathf.Max(bodyTop, bodyBottom - dragRowHeight));
            draggedRow.style.top = dragCurrentWorldTop - bodyTop;
            var dragBottom = dragCurrentWorldTop + dragRowHeight;
            var target = dragFrom;
            if (dragCurrentWorldTop < dragTops[dragFrom])
            {
                for (var i = dragFrom - 1; i >= 0; i--)
                {
                    if (dragCurrentWorldTop >
                        (dragTops[i] + dragBottoms[i]) * 0.5f) break;
                    target = i;
                }
            }
            else if (dragBottom > dragBottoms[dragFrom])
            {
                for (var i = dragFrom + 1; i < rows.Count; i++)
                {
                    if (dragBottom <
                        (dragTops[i] + dragBottoms[i]) * 0.5f) break;
                    target = i;
                }
            }

            if (dragTo == target) return;
            dragTo = target;
            for (var i = 0; i < rows.Count; i++)
            {
                if (i == dragFrom) continue;
                AnimateRowOffset(rows[i], rows[i].resolvedStyle.translate.y,
                    DragOffset(i, dragFrom, dragTo));
            }
        }

        private float DragOffset(int index, int from, int to)
        {
            if (to > from && index > from && index <= to) return -dragRowHeight;
            if (to < from && index >= to && index < from) return dragRowHeight;
            return 0f;
        }

        /// <summary>
        /// Commits the reorder while preserving every row's on-screen position: the layout jump of
        /// the reorder is countered by an equal translate, which then slides to rest. A row that
        /// already reached its slot therefore does not move at all.
        /// </summary>
        private void CompleteDrag()
        {
            var row = draggedRow;
            var from = dragFrom;
            var to = dragTo;
            for (var i = 0; i < rows.Count; i++)
            {
                if (i == from) continue;
                AnimateRowOffset(rows[i],
                    rows[i].resolvedStyle.translate.y - DragOffset(i, from, to), 0f);
            }
            var slotTop = to > from ? dragBottoms[to] - dragRowHeight : dragTops[to];
            dragPlaceholder.RemoveFromHierarchy();
            row.RemoveFromHierarchy();
            rows.RemoveAt(from);
            rows.Insert(to, row);
            body.Insert(DomIndexFor(to), row);
            RefreshRowOrder();
            ResetDraggedLayout(row);
            AnimateRowOffset(row, dragCurrentWorldTop - slotTop, 0f);
            ClearDragState();
        }

        private void CancelDrag()
        {
            if (draggedRow == null) return;
            var row = draggedRow;
            var handle = dragHandle;
            var pointerId = dragPointerId;
            for (var i = 0; i < rows.Count; i++)
            {
                if (i == dragFrom) continue;
                AnimateRowOffset(rows[i], rows[i].resolvedStyle.translate.y, 0f);
            }
            dragPlaceholder.RemoveFromHierarchy();
            row.RemoveFromHierarchy();
            body.Insert(DomIndexFor(dragFrom), row);
            ResetDraggedLayout(row);
            AnimateRowOffset(row, dragCurrentWorldTop - dragTops[dragFrom], 0f);
            ClearDragState();
            if (handle.HasPointerCapture(pointerId)) handle.ReleasePointer(pointerId);
        }

        /// <summary>
        /// Moves a row to <paramref name="from"/> immediately, in the same frame as the caller's
        /// layout change, then slides it to <paramref name="to"/>.
        /// </summary>
        private void AnimateRowOffset(VisualElement row, float from, float to)
        {
            if (Mathf.Approximately(from, to))
            {
                StopRowMotion(row);
                row.style.translate = VerticalOffset(from);
                return;
            }
            if (!rowMotions.TryGetValue(row, out var motion))
                rowMotions[row] = motion = new InspectorMotion.Ticker();
            motion.Play(row, eased =>
                row.style.translate = VerticalOffset(Mathf.Lerp(from, to, eased)));
        }

        private void StopRowMotion(VisualElement row)
        {
            if (rowMotions.Remove(row, out var motion)) motion.Stop();
        }

        private void RefreshRowOrder()
        {
            for (var i = 0; i < rows.Count; i++)
            {
                rows[i].userData = i;
                if (itemKey == null)
                    boundKeys[rows[i]] = i;
                rows[i].EnableInClassList("lightside-list__row--odd", (i & 1) != 0);
            }
        }

        private static void ResetDraggedLayout(VisualElement row)
        {
            row.style.position = Position.Relative;
            row.style.left = StyleKeyword.Null;
            row.style.right = StyleKeyword.Null;
            row.style.top = StyleKeyword.Null;
            row.style.height = StyleKeyword.Null;
        }

        private static StyleTranslate VerticalOffset(float value) =>
            new(new Translate(new Length(0f), new Length(value), 0f));

        private void ClearDragState()
        {
            dragHandle?.RemoveFromClassList("lightside-list__reorder--dragging");
            dragCursorRoot?.RemoveFromClassList("lightside-list-dragging");
            draggedRow = null;
            dragPlaceholder = null;
            dragHandle = null;
            dragCursorRoot = null;
            dragTops = null;
            dragBottoms = null;
            dragPointerId = -1;
            dragFrom = -1;
            dragTo = -1;
        }
    }
}
