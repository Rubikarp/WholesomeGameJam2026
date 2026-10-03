using System;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>
    /// Lightweight UI graphic that renders the text input caret as a filled rectangle.
    /// This is the caret extension point: subclass and override <see cref="OnPopulateMesh"/>
    /// (reading <see cref="CaretRect"/> / <see cref="BlinkVisible"/>) for custom shapes —
    /// block cursor, underline, gradient — then assign via
    /// <see cref="UniTextEditable.CaretRenderer"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inherits <see cref="MaskableGraphic"/> so the caret participates in
    /// <see cref="RectMask2D"/> clipping within the input field viewport.
    /// </para>
    /// <para>
    /// Blink behavior: the caret toggles visibility every <see cref="UniTextSettings.CaretBlinkInterval"/>
    /// seconds. After <see cref="UniTextSettings.CaretBlinkTimeout"/> seconds of inactivity the caret
    /// stops blinking and remains visible. Any user interaction (typing, cursor movement,
    /// selection change, focus) should call <see cref="ResetBlink"/> to restart the cycle.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    public class InputCaretRenderer : MaskableGraphic
    {
        /// <summary>Default caret width as a fraction of the caret (line) height.</summary>
        private const float DefaultCaretWidthRatio = 0.06f;

        private static bool prefersNonBlinkingCaret;

        /// <summary>
        /// Application-wide accessibility flag: when <see langword="true"/>, caret renderers
        /// suppress the blink animation and keep the caret continuously visible. Mirrors iOS 17+
        /// "Prefer Non-Blinking Cursor" and the macOS Reduce Motion cursor sub-setting — surface
        /// the platform pref here from your integration layer, alongside
        /// <see cref="Accessibility.PrefersReducedMotion"/>.
        /// </summary>
        public static bool PrefersNonBlinkingCaret
        {
            get => prefersNonBlinkingCaret;
            set
            {
                if (prefersNonBlinkingCaret == value) return;
                prefersNonBlinkingCaret = value;
                PrefersNonBlinkingCaretChanged?.Invoke();
            }
        }

        /// <summary>
        /// Occurs when <see cref="PrefersNonBlinkingCaret"/> has changed so observers can
        /// re-arm visuals. <see cref="InputCaretRenderer"/> subscribes automatically.
        /// </summary>
        public static event Action PrefersNonBlinkingCaretChanged;

        /// <summary>
        /// Caret rectangle in local coordinates. Updated via <see cref="SetCaretPosition"/>.
        /// </summary>
        private Rect caretRect;

        /// <summary>Current blink state. When <see langword="false"/> the caret is invisible.</summary>
        private bool visible = true;

        /// <summary>Accumulated time within the current blink half-cycle.</summary>
        private float blinkTimer;

        /// <summary>
        /// Seconds elapsed since the last user interaction.
        /// When this exceeds <see cref="UniTextSettings.CaretBlinkTimeout"/> the caret stays visible.
        /// </summary>
        private float timeSinceLastInput;

        /// <summary>Caret width as a fraction of the caret (line) height.</summary>
        private float caretWidthRatio = DefaultCaretWidthRatio;

        /// <summary>
        /// Caret width as a fraction of the caret's height (which tracks the component's font size),
        /// so the caret keeps the same proportion to the text at any canvas scale or resolution. The
        /// default renderer draws the rectangle this wide; custom shapes may ignore or reinterpret it.
        /// </summary>
        public float CaretWidthRatio
        {
            get => caretWidthRatio;
            set
            {
                if (Mathf.Approximately(caretWidthRatio, value))
                    return;
                caretWidthRatio = value;
                SetVerticesDirty();
            }
        }

        /// <summary>Latest caret rectangle in local coordinates, for subclass mesh builders.</summary>
        protected Rect CaretRect => caretRect;

        /// <summary>Current blink phase, for subclass mesh builders: false = the caret is in its invisible half-cycle.</summary>
        protected bool BlinkVisible => visible;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        private void OnPrefersNonBlinkingCaretChanged()
        {
            if (PrefersNonBlinkingCaret && !visible)
            {
                visible = true;
                SetVerticesDirty();
            }
        }

        /// <summary>Updates the caret rectangle in local coordinates and repaints.</summary>
        /// <remarks>
        /// Rebuilds the mesh immediately instead of via <see cref="Graphic.SetVerticesDirty"/>. The caret rect
        /// is recomputed in the editable's post-text-mesh callback, which runs during the canvas rebuild pass;
        /// a dirty flag registered there is only drawn next frame — the one-frame caret lag after typing.
        /// Building the four-vert quad now sidesteps that rebuild-order dependency.
        /// </remarks>
        public virtual void SetCaretPosition(Rect rect)
        {
            if (caretRect == rect)
                return;

            caretRect = rect;
            if (visible)
                UpdateGeometry();
            else
                SetVerticesDirty();
        }

        /// <summary>
        /// Restarts the blink cycle and forces the caret visible. The editable calls this on
        /// every user interaction (typing, cursor movement, selection change, focus gained).
        /// </summary>
        public virtual void ResetBlink()
        {
            blinkTimer = 0f;
            timeSinceLastInput = 0f;

            if (!visible)
            {
                visible = true;
                SetVerticesDirty();
            }
        }

        private Action blinkTickCallback;
        private TickHandle blinkTickHandle;

        private void OnBlinkTick()
        {
            if (PrefersNonBlinkingCaret) return;

            var blinkInterval = UniTextSettings.CaretBlinkInterval;
            if (blinkInterval <= 0f)
                return;

            var timeout = UniTextSettings.CaretBlinkTimeout;
            if (timeout > 0f)
            {
                timeSinceLastInput += CoreLoop.UnscaledDeltaTime;
                if (timeSinceLastInput >= timeout)
                {
                    if (!visible)
                    {
                        visible = true;
                        SetVerticesDirty();
                    }
                    blinkTimer = 0f;
                    return;
                }
            }

            blinkTimer += CoreLoop.UnscaledDeltaTime;

            if (blinkTimer >= blinkInterval)
            {
                blinkTimer -= blinkInterval;

                if (blinkTimer >= blinkInterval)
                    blinkTimer = 0f;

                visible = !visible;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (!visible)
                return;

            var halfW = caretRect.height * caretWidthRatio * 0.5f;
            var centerX = caretRect.x + caretRect.width * 0.5f;

            var xMin = centerX - halfW;
            var xMax = centerX + halfW;
            var yMin = caretRect.yMin;
            var yMax = caretRect.yMax;

            var c = color;

            vh.AddVert(new Vector3(xMin, yMin), c, Vector2.zero);
            vh.AddVert(new Vector3(xMin, yMax), c, Vector2.up);
            vh.AddVert(new Vector3(xMax, yMax), c, Vector2.one);
            vh.AddVert(new Vector3(xMax, yMin), c, Vector2.right);

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            PrefersNonBlinkingCaretChanged += OnPrefersNonBlinkingCaretChanged;
            CoreLoop.Updating.Toggle(ref blinkTickHandle, blinkTickCallback ??= OnBlinkTick, true);
            ResetBlink();
            OnPrefersNonBlinkingCaretChanged();
        }

        protected override void OnDisable()
        {
            PrefersNonBlinkingCaretChanged -= OnPrefersNonBlinkingCaretChanged;
            CoreLoop.Updating.Toggle(ref blinkTickHandle, blinkTickCallback, false);
            base.OnDisable();
            visible = true;
            blinkTimer = 0f;
            timeSinceLastInput = 0f;
        }
    }
}
