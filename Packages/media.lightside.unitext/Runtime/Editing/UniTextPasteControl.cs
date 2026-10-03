using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LightSide
{
    /// <summary>Apple UIPasteControl content style (iOS 16+ only).</summary>
    public enum PasteControlDisplayMode
    {
        /// <summary>Clipboard glyph + localised "Paste" label. Apple default.</summary>
        IconAndLabel = 0,
        /// <summary>Glyph only — compact toolbars (chat composer ribbon, etc.).</summary>
        IconOnly = 1,
        /// <summary>Localised "Paste" text only.</summary>
        LabelOnly = 2,
    }

    /// <summary>Apple UIPasteControl corner radius style (iOS 16+ only).</summary>
    public enum PasteControlCornerStyle
    {
        /// <summary>Fully rounded pill. Apple default, share-sheet aesthetic.</summary>
        Capsule = 0,
        /// <summary>Radius scales with OS-wide control-size setting.</summary>
        Dynamic = 1,
        /// <summary>Fixed radius regardless of OS settings.</summary>
        Fixed = 2,
        /// <summary>Small radius — compact toolbar buttons.</summary>
        Small = 3,
    }

    /// <summary>
    /// Cross-platform Paste widget. On iOS 16+ overlays a native UIPasteControl on top of
    /// this RectTransform (bypasses the system paste-permission prompt). Elsewhere triggers
    /// <see cref="UniTextEditable.Paste"/> through an optional sibling/child Button.
    /// </summary>
    [AddComponentMenu(UniTextMenu.AddComponent.PasteControl)]
    [RequireComponent(typeof(RectTransform))]
    public sealed partial class UniTextPasteControl : MonoBehaviour
    {
        /// <summary>Editable text receiving pasted content.</summary>
        [SerializeField, StateProperty(nameof(ApplyTargetChange))] private UniTextEditable target;
        /// <summary>Unity button used when a native paste control is unavailable.</summary>
        [SerializeField, StateProperty(nameof(ApplyFallbackButtonChange))] private Button fallbackButton;
#pragma warning disable 0414
        /// <summary>Content displayed by the native iOS paste control.</summary>
        [SerializeField, StateProperty(nameof(ApplyNativeStyleChange))] private PasteControlDisplayMode displayMode = PasteControlDisplayMode.IconAndLabel;
        /// <summary>Corner treatment used by the native iOS paste control.</summary>
        [SerializeField, StateProperty(nameof(ApplyNativeStyleChange))] private PasteControlCornerStyle cornerStyle = PasteControlCornerStyle.Capsule;
#pragma warning restore 0414

        /// <summary>Occurs when a paste has succeeded (native overlay tap or fallback click).</summary>
        public event Action Pasted;

        private RectTransform rectTransform;
        private Canvas canvas;
        private bool controlBound;

#if UNITY_IOS && !UNITY_EDITOR
        private int nativeHandle;
        private bool nativeUsed;
        private bool nativeShown;
        private Vector4 lastSentFrame = new(float.MinValue, 0, 0, 0);
        private Action nativeSyncCallback;
        private TickHandle nativeSyncHandle;

        private void RefreshNativeSync() =>
            CoreLoop.CanvasPreRendering.Toggle(ref nativeSyncHandle,
                nativeSyncCallback ??= SyncNativeFrame, controlBound && nativeUsed);
#endif

        private void Awake() => rectTransform = transform as RectTransform;

        private void OnEnable()
        {
            if (target == null) Target = GetComponentInParent<UniTextEditable>();
            if (fallbackButton == null) FallbackButton = GetComponentInChildren<Button>(true);
            if (fallbackButton != null) fallbackButton.onClick.AddListener(TriggerPaste);

#if UNITY_IOS && !UNITY_EDITOR
            if (PasteControlIOS.IsSupported())
            {
                nativeHandle = PasteControlIOS.Create(EnsureUniqueName(), (int)displayMode, (int)cornerStyle);
                nativeUsed = nativeHandle != 0;
                if (nativeUsed && fallbackButton != null) fallbackButton.gameObject.SetActive(false);
            }
#endif
            controlBound = true;
#if UNITY_IOS && !UNITY_EDITOR
            RefreshNativeSync();
#endif
        }

        private void OnDisable()
        {
            controlBound = false;
#if UNITY_IOS && !UNITY_EDITOR
            RefreshNativeSync();
#endif
            if (fallbackButton != null) fallbackButton.onClick.RemoveListener(TriggerPaste);

#if UNITY_IOS && !UNITY_EDITOR
            if (nativeHandle != 0)
            {
                PasteControlIOS.SetHidden(nativeHandle, true);
                PasteControlIOS.Destroy(nativeHandle);
                nativeHandle = 0;
                nativeUsed = false;
                nativeShown = false;
                lastSentFrame = new Vector4(float.MinValue, 0, 0, 0);
            }
#endif
        }

        private void ApplyTargetChange() { }

        private void ApplyFallbackButtonChange(Button previous, Button current)
        {
            if (!controlBound) return;
            if (previous != null) previous.onClick.RemoveListener(TriggerPaste);
            if (current != null) current.onClick.AddListener(TriggerPaste);
#if UNITY_IOS && !UNITY_EDITOR
            if (nativeUsed && current != null) current.gameObject.SetActive(false);
#endif
        }

        private void ApplyNativeStyleChange()
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (!controlBound || !PasteControlIOS.IsSupported()) return;
            if (nativeHandle != 0) PasteControlIOS.Destroy(nativeHandle);
            nativeHandle = PasteControlIOS.Create(EnsureUniqueName(), (int)displayMode, (int)cornerStyle);
            nativeUsed = nativeHandle != 0;
            nativeShown = false;
            lastSentFrame = new Vector4(float.MinValue, 0, 0, 0);
            if (fallbackButton != null) fallbackButton.gameObject.SetActive(!nativeUsed);
            RefreshNativeSync();
#endif
        }

        /// <summary>
        /// Programmatic paste through <see cref="UniTextEditable.Paste"/>. On iOS 16+
        /// outside a user-action context this surfaces the system prompt — prefer the
        /// native overlay (real tap) on that OS.
        /// </summary>
        public void TriggerPaste()
        {
            if (target == null) return;
            target.Paste();
            Pasted?.Invoke();
        }

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>
        /// UnitySendMessage target invoked by the native UIPasteControl tap (dispatched by GameObject
        /// name — see <see cref="EnsureUniqueName"/>). No managed callers; [Preserve] keeps IL2CPP
        /// managed stripping from removing it.
        /// </summary>
        [UnityEngine.Scripting.Preserve]
        private void OnNativePasteReady(string _)
        {
            if (target == null) { PasteControlIOS.ClearCapturedPayload(); return; }

            var items = new List<ClipboardItem>(3);
            var plain = PasteControlIOS.GetCapturedPayload("text/plain");
            if (!string.IsNullOrEmpty(plain)) items.Add(new ClipboardItem(ClipboardFormat.PlainText, plain));
            var html = PasteControlIOS.GetCapturedPayload("text/html");
            if (!string.IsNullOrEmpty(html)) items.Add(new ClipboardItem(ClipboardFormat.Html, html));
            var uniText = PasteControlIOS.GetCapturedPayload("application/vnd.lightside.unitext");
            if (!string.IsNullOrEmpty(uniText)) items.Add(new ClipboardItem(ClipboardFormat.UniTextSource, uniText));

            PasteControlIOS.ClearCapturedPayload();
            target.PasteFromItems(items);
            Pasted?.Invoke();
        }

        /// <summary>
        /// Pushes the control's screen rect to the native overlay when it changes, and unhides the
        /// overlay after the first valid frame — the overlay is created hidden
        /// (<c>PasteControlIOS.Create</c> contract) so it never flashes at (0,0).
        /// </summary>
        private void SyncNativeFrame()
        {
            if (rectTransform == null) return;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
            Camera cam = CanvasUtil.GetCanvasCamera(canvas);

            var screen = CanvasUtil.WorldCornersToScreenRect(rectTransform, cam);
            if (screen.width < 1f || screen.height < 1f) return;

            var rect = new Vector4(screen.x, screen.y, screen.width, screen.height);
            if ((rect - lastSentFrame).sqrMagnitude < 0.25f) return;
            lastSentFrame = rect;

            PasteControlIOS.SetFrame(nativeHandle, screen.x, screen.y, screen.width, screen.height);
            if (!nativeShown)
            {
                nativeShown = true;
                PasteControlIOS.SetHidden(nativeHandle, false);
            }
        }

        private string EnsureUniqueName()
        {
            if (name.IndexOf('#') < 0) name = $"{name}#{ObjectUtils.GetInstanceIdCompat(this)}";
            return name;
        }
#endif
    }
}
