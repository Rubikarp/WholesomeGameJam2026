using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>Base editor popup that keeps an owning popup alive while a nested popup is open.</summary>
    public abstract class InspectorPopupWindow : EditorWindow
    {
        /// <summary>Block class every popup panel carries.</summary>
        public const string PopupClass = "lightside-popup";

        /// <summary>Drops the window's own 100×100 clamp for a popup that sizes itself; the popup-container floor of ~56px remains.</summary>
        private static readonly Vector2 MinimumSize = new(1f, 1f);
        private static readonly Vector2 MaximumSize = new(8192f, 8192f);

        private static readonly MethodInfo popupRectMethod;
        private static readonly Array belowLocations;
        private static readonly Array aboveLocations;

        private InspectorPopupWindow owner;
        private InspectorPopupWindow child;
        private EditorWindow auxiliaryChild;
        private IVisualElementScheduledItem auxiliaryChildWatch;
        private bool returnFocus;
        private ScreenRect anchorRect;
        private bool anchored;
        private bool shown;
        private bool placed;
        private float lastRequestedHeight;
        private float committedHeight;
        private bool requestChanged;
        private ScreenRect currentFitRect;
        private Array lockedLocations;
        private ScreenRect hostArea;
        private ScrollView fittedContent;
        private ScrollView fittedScroller;
        private Func<float> heightProvider;
        private readonly InspectorMotion.Ticker heightGlide = new();

        /// <summary>Resolves Unity's dropdown geometry without adopting its close-on-focus-loss policy.</summary>
        static InspectorPopupWindow()
        {
            var locationType = typeof(EditorWindow).Assembly.GetType("UnityEditor.PopupLocation")
                               ?? throw new TypeLoadException("UnityEditor.PopupLocation");
            popupRectMethod = typeof(EditorWindow).GetMethod("ShowAsDropDownFitToScreen",
                                  BindingFlags.Instance | BindingFlags.NonPublic, null,
                                  new[] { typeof(Rect), typeof(Vector2), locationType.MakeArrayType() },
                                  null)
                              ?? throw new MissingMethodException(typeof(EditorWindow).FullName,
                                  "ShowAsDropDownFitToScreen");
            belowLocations = Array.CreateInstance(locationType, 1);
            belowLocations.SetValue(Enum.Parse(locationType, "Below"), 0);
            aboveLocations = Array.CreateInstance(locationType, 1);
            aboveLocations.SetValue(Enum.Parse(locationType, "Above"), 0);
        }

        /// <summary>Shows the popup at a rectangle already expressed in screen space.</summary>
        protected void ShowPopup(Rect rect) => ShowAtScreenRect(new ScreenRect(rect));

        /// <summary>Shows the popup at a typed screen-space rectangle.</summary>
        protected void ShowAtScreenRect(ScreenRect rect)
        {
            position = rect.Value;
            committedHeight = rect.Height;
            owner = focusedWindow as InspectorPopupWindow;
            if (owner != null) owner.child = this;
            base.ShowPopup();
            shown = true;
            Focus();
        }

        /// <summary>Fits and shows the popup against a rectangle already expressed in screen space.</summary>
        protected void ShowPopup(Rect anchorRect, float requestedWidth, float requestedHeight) =>
            ShowAtScreenRect(new ScreenRect(anchorRect), requestedWidth, requestedHeight);

        /// <summary>Fits and shows the popup against a typed screen-space rectangle.</summary>
        protected void ShowAtScreenRect(ScreenRect anchorRect, float requestedWidth,
            float requestedHeight)
        {
            this.anchorRect = anchorRect;
            anchored = true;
            hostArea = HostArea(anchorRect);
            minSize = MinimumSize;
            maxSize = MaximumSize;
            var rect = PopupScreenRect(anchorRect, requestedWidth, requestedHeight);
            lastRequestedHeight = rect.Height;
            currentFitRect = rect;
            ShowAtScreenRect(rect);
            if (heightProvider != null || fittedContent != null) Refit();
        }

        /// <summary>Fits a popup rectangle against an anchor already expressed in screen space.</summary>
        protected Rect PopupRect(Rect anchorRect, float requestedWidth, float requestedHeight) =>
            PopupScreenRect(new ScreenRect(anchorRect), requestedWidth, requestedHeight).Value;

        /// <summary>
        /// Popup rectangle above the anchor when the anchor sits in the lower half of the editor
        /// window, below it otherwise. The side is chosen on first call and held while the popup
        /// settles into its opening size. The rectangle stays within the editor window holding the
        /// anchor whenever it fits there, so a popup wider than the space beside its anchor opens
        /// inward instead of past the window edge.
        /// </summary>
        protected ScreenRect PopupScreenRect(ScreenRect anchorRect, float requestedWidth,
            float requestedHeight)
        {
            lockedLocations ??=
                anchorRect.Center.y > EditorGUIUtility.GetMainWindowPosition().center.y
                    ? aboveLocations
                    : belowLocations;
            return ClampToHost(DropDownRect(focusedWindow != null ? focusedWindow : this,
                anchorRect, requestedWidth, requestedHeight, lockedLocations));
        }

        /// <summary>
        /// Matches the window height to what <paramref name="view"/> lays out, up to the screen.
        /// Call once after the content is built; later changes follow on their own. Requires the
        /// anchored <see cref="ShowAtScreenRect(ScreenRect, float, float)"/> overload.
        /// </summary>
        protected void FitHeightToContent(ScrollView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            RequireAnchor();
            fittedContent = view;
            fittedScroller = view;
            view.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => Refit());
            view.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Refit());
        }

        /// <summary>
        /// Overload for content that cannot be measured but can be computed, such as a virtualized
        /// list: <paramref name="desiredHeight"/> returns the wanted window height, and
        /// <see cref="RequestFit"/> must be called whenever that height changes. The initial window
        /// height stays the one given to <see cref="ShowAtScreenRect(ScreenRect, float, float)"/>.
        /// <paramref name="scroller"/> lets the fit drive
        /// the content's scroller, shown only when the window cannot reach the desired height.
        /// </summary>
        protected void FitHeightToContent(Func<float> desiredHeight, ScrollView scroller = null)
        {
            heightProvider = desiredHeight ?? throw new ArgumentNullException(nameof(desiredHeight));
            fittedScroller = scroller;
            RequireAnchor();
        }

        /// <summary>Re-fits the window after a change it cannot observe by itself.</summary>
        protected void RequestFit() => Refit();

        /// <summary>
        /// Attaches the shared stylesheets to this window's panel and marks it as a popup, plus the given
        /// block classes, then returns the panel.
        /// </summary>
        /// <param name="blockClasses">Block classes this popup styles itself with, applied in order.</param>
        /// <exception cref="ArgumentNullException"><paramref name="blockClasses"/> is <see langword="null"/>.</exception>
        protected internal VisualElement PreparePopup(params string[] blockClasses)
        {
            if (blockClasses == null) throw new ArgumentNullException(nameof(blockClasses));
            var panel = rootVisualElement;
            InspectorVisuals.Attach(panel);
            panel.AddToClassList(PopupClass);
            foreach (var blockClass in blockClasses) panel.AddToClassList(blockClass);
            return panel;
        }

        /// <summary>
        /// Prepares the panel as <see cref="PreparePopup"/> does and returns the window root that content
        /// is added to, for a popup whose body is ordinary window content rather than its own layout.
        /// </summary>
        protected internal VisualElement CreatePopupRoot(params string[] blockClasses)
            => InspectorVisuals.CreateWindowRoot(PreparePopup(blockClasses));

        /// <summary>
        /// Runs the panel's style and layout passes immediately, so content changed in the current
        /// event is measured before the frame paints.
        /// </summary>
        protected void FitNow() => InspectorMotion.CompleteLayout(rootVisualElement.panel);

        private void RequireAnchor()
        {
            if (!anchored)
                throw new InvalidOperationException(
                    "Fitting a popup to its content requires anchored popup placement.");
        }

        /// <summary>Sizes the window to what the content lays out or the provider computes.</summary>
        private void Refit()
        {
            float desired;
            var slack = 0f;
            if (heightProvider != null)
            {
                desired = heightProvider();
            }
            else
            {
                var viewportHeight = fittedContent.contentViewport.layout.height;
                if (float.IsNaN(viewportHeight)) return;
                var extent = InspectorMotion.ContentExtent(fittedContent.contentContainer);
                desired = rootVisualElement.layout.height - viewportHeight + extent;
                slack = viewportHeight - extent;
            }
            ApplyFit(desired, slack);
        }

        /// <summary>
        /// Requests the fitted rectangle and aligns content by the height the window has actually
        /// reached, not the one just requested. The window repositions freely only until its first
        /// request is met — the pre-paint corrections of opening; from then on the top edge is
        /// fixed and only the bottom glides, within the screen, so content never leaves the pointer.
        /// A request the window refuses outright — the editor holds a floor of ~56px, met only by
        /// short menus that never resize again — is distinguished from a resize in flight by
        /// whether any request ever changed.
        /// </summary>
        private void ApplyFit(float desired, float slack)
        {
            if (!shown) return;
            var fitted = placed
                ? ClampToHost(DropDownRect(focusedWindow != null ? focusedWindow : this,
                    new ScreenRect(new Rect(currentFitRect.Value.x, currentFitRect.Value.y,
                        currentFitRect.Width, 0f)),
                    currentFitRect.Width, desired, belowLocations))
                : PopupScreenRect(anchorRect, currentFitRect.Width, desired);
            if (Mathf.Abs(fitted.Height - lastRequestedHeight) >= 1f)
            {
                lastRequestedHeight = fitted.Height;
                requestChanged = true;
                currentFitRect = fitted;
                if (placed)
                {
                    GlideTo(fitted.Value);
                }
                else
                {
                    committedHeight = fitted.Height;
                    position = fitted.Value;
                }
            }
            var actual = rootVisualElement.layout.height;
            var settled = !float.IsNaN(actual) && Mathf.Abs(fitted.Height - actual) < 1f;
            if (settled) placed = true;
            CenterSlack((settled || !requestChanged) && slack > 1f);
            if (fittedScroller != null)
                fittedScroller.verticalScrollerVisibility = fitted.Height < desired - 1f
                    ? ScrollerVisibility.Auto
                    : ScrollerVisibility.Hidden;
            if (fittedContent == null) return;
            var offset = fittedContent.scrollOffset;
            var limit = Mathf.Max(0f, -slack);
            if (offset.y > limit)
                fittedContent.scrollOffset = new Vector2(offset.x, limit);
        }

        /// <summary>
        /// Walks the window height to the target in per-tick steps. A visible window resized in
        /// one jump is composited before the editor presents a frame of the new size: the stale
        /// surface shows as a squashed frame on macOS and an unpainted band on Windows. Per-tick
        /// steps keep every mismatch beneath notice and replaced within a frame.
        /// </summary>
        private void GlideTo(Rect target)
        {
            var from = committedHeight;
            heightGlide.Play(rootVisualElement, eased =>
            {
                var height = Mathf.Lerp(from, target.height, eased);
                if (eased < 1f && Mathf.Abs(height - committedHeight) < 0.5f) return;
                committedHeight = height;
                position = new Rect(target.x, target.y, target.width, height);
            });
        }

        /// <summary>
        /// Centres content in slack the window cannot give up: the editor refuses to make a popup
        /// shorter than ~56px whatever it asks for.
        /// </summary>
        private void CenterSlack(bool center)
        {
            if (fittedContent == null) return;
            fittedContent.contentViewport.style.justifyContent =
                center ? Justify.Center : Justify.FlexStart;
        }

        private static ScreenRect DropDownRect(EditorWindow host, ScreenRect anchorRect,
            float requestedWidth, float requestedHeight, Array locations)
            => new((Rect)popupRectMethod.Invoke(host, new object[]
            {
                anchorRect.Value,
                new Vector2(requestedWidth, requestedHeight),
                locations
            }));

        /// <summary>The editor window an anchor belongs to: the main window, or a floating host.</summary>
        private static ScreenRect HostArea(ScreenRect anchorRect)
        {
            var main = EditorGUIUtility.GetMainWindowPosition();
            if (main.Contains(anchorRect.Center)) return new ScreenRect(main);
            var host = focusedWindow;
            return new ScreenRect(host != null ? host.position : main);
        }

        /// <summary>
        /// Slides a fitted rectangle back inside the host area horizontally. The size is never changed,
        /// and a rectangle the host area cannot hold keeps the position the dropdown geometry gave it.
        /// </summary>
        private ScreenRect ClampToHost(ScreenRect rect)
        {
            var value = rect.Value;
            if (hostArea.Width < value.width) return rect;
            var area = hostArea.Value;
            value.x = Mathf.Clamp(value.x, area.xMin, area.xMax - value.width);
            return new ScreenRect(value);
        }

        protected void CloseToOwner()
        {
            returnFocus = true;
            Close();
        }

        protected void TrackAuxiliaryChild(EditorWindow window)
        {
            if (window == null) return;
            auxiliaryChild = window;
            auxiliaryChildWatch?.Pause();
            auxiliaryChildWatch = rootVisualElement.schedule.Execute(() =>
            {
                if (auxiliaryChild != null) return;
                auxiliaryChild = null;
                auxiliaryChildWatch?.Pause();
                auxiliaryChildWatch = null;
                if (this != null && focusedWindow != this)
                    OnLostFocus();
            }).Every(50);
        }

        /// <summary>
        /// Closes when focus durably leaves the popup chain. Unity's colour picker and its screen
        /// eye dropper are part of the chain: any popup survives a colour edit started from its
        /// content, however the picker was opened.
        /// </summary>
        protected virtual void OnLostFocus()
        {
            rootVisualElement.schedule.Execute(() =>
            {
                if (this == null || focusedWindow == this || child != null ||
                    auxiliaryChild != null) return;
                if (EditorColorPicker.EyeDropperOpen) return;
                var picker = EditorColorPicker.OpenWindow;
                if (picker != null && focusedWindow == picker)
                {
                    TrackAuxiliaryChild(picker);
                    return;
                }
                if (owner != null && focusedWindow == owner)
                    returnFocus = true;
                Close();
            });
        }

        protected virtual void OnDisable()
        {
            auxiliaryChildWatch?.Pause();
            auxiliaryChildWatch = null;
            var previousAuxiliaryChild = auxiliaryChild;
            auxiliaryChild = null;
            if (previousAuxiliaryChild != null)
                previousAuxiliaryChild.Close();

            if (child != null)
            {
                var previousChild = child;
                child = null;
                previousChild.owner = null;
                previousChild.Close();
            }

            if (owner == null) return;
            var previousOwner = owner;
            owner = null;
            previousOwner.child = null;
            if (returnFocus)
                previousOwner.Focus();
            else
                previousOwner.Close();
        }
    }
}
