using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LightSide
{
    /// <summary>
    /// World-space text component. Renders via <see cref="UniTextWorldBatcher"/> with full
    /// Unicode support — bidirectional scripts, complex shaping, color emoji, markup.
    /// </summary>
    /// <remarks>For Canvas-space (UI) text, use <see cref="UniText"/>.</remarks>
    [ExecuteAlways]
    public partial class UniTextWorld : UniTextBase
    {
        #region World State

        /// <summary>Gets or sets the sorting order for render ordering.</summary>
        [SerializeField, StateProperty(nameof(RaiseSortingChanged))]
        [Tooltip("Sorting order within the current sorting layer.")]
        private int sortingOrder;

        /// <summary>Gets or sets the sorting layer ID.</summary>
        [SerializeField, StateProperty(nameof(RaiseSortingChanged))]
        [Tooltip("Sorting layer ID.")]
        private int sortingLayerID;

        [SerializeField, StatePassive]
        [Tooltip("Receive pointer events (clicks, hover, interactive ranges) from " +
                 "UniTextWorldRaycaster. Disable for purely decorative text.")]
        private new bool raycastTarget = true;

        /// <summary>Gets or sets whether this text receives scene lighting.</summary>
        [SerializeField, StateProperty(nameof(SetAppearanceDirty))]
        [Tooltip("Receive scene lighting. Off (default) renders flat/unlit. " +
                 "Per-component, so lit and unlit world text can sit side by side.")]
        private bool lit;

        /// <summary>Gets or sets whether this text casts shadows onto other objects.</summary>
        [SerializeField, StateProperty(nameof(RaiseSortingChanged))]
        [Tooltip("Cast shadows onto other objects. Off by default. Batched per sorting context: if any " +
                 "world text sharing the context casts, the shared renderer casts.")]
        private bool castShadows;

        /// <summary>Gets or sets whether this text renders through a renderer of its own instead of joining the shared batch mesh.</summary>
        /// <remarks>Batched text shares one renderer — and so one depth-sorting position — with every other
        /// world text in its sorting context, and nothing can sort between two texts of one renderer. Standalone
        /// text sorts by its own distance to the camera against every other transparent renderer in the scene,
        /// at the cost of one draw call.</remarks>
        [SerializeField, StateProperty(nameof(RaiseSortingChanged))]
        [Tooltip("Render through this component's own renderer instead of merging into the shared batch mesh. " +
                 "Gives this text its own depth-sorting position, so sprites and other transparent renderers " +
                 "can sort between it and neighbouring texts. Costs one draw call.")]
        private bool standalone;

        #endregion

        #region Events

        /// <summary>Occurs when a <see cref="UniTextWorld"/> instance has become active (<c>OnEnable</c>).</summary>
        /// <remarks>Fires after <see cref="UniTextBase.OnEnable"/> base work has completed.</remarks>
        public static event Action<UniTextWorld> Activated;

        /// <summary>Occurs when a <see cref="UniTextWorld"/> instance has become inactive (<c>OnDisable</c>).</summary>
        /// <remarks>Fires after <see cref="UniTextBase.OnDisable"/> base work has completed.</remarks>
        public static event Action<UniTextWorld> Deactivated;

        /// <summary>
        /// Occurs when the component's mesh has been generated and render data is available
        /// through <see cref="UniTextBase.MeshGenerator"/>.<see cref="UniTextMeshGenerator.CollectRenderData"/>.
        /// </summary>
        /// <remarks>
        /// <b>Lifetime:</b> pooled buffers backing the render data are only valid until the next
        /// rebuild on the same generator. Consumers must read/copy the data during this callback
        /// and must not retain the arrays.
        /// </remarks>
        public event Action<UniTextWorld> RenderDataAvailable;

        /// <summary>Occurs when the component's render data should be discarded (e.g. empty text, disabled).</summary>
        public event Action<UniTextWorld> RenderDataCleared;

        /// <summary>Occurs when <see cref="UniTextBase.RenderSuppressed"/> has toggled (user Hide/Show or scene-visibility). The batcher reads the live state and flips this component's batch membership without a structural rebuild.</summary>
        public event Action<UniTextWorld> RenderSuppressionChanged;

        /// <summary>Occurs when a sorting-context input has changed: <see cref="SortingOrder"/>, <see cref="SortingLayerID"/>, <see cref="CastShadows"/> or <see cref="Standalone"/> (all are batch-key components).</summary>
        public event Action<UniTextWorld> SortingChanged;

        /// <summary>Occurs when the component's transform parent has changed (may invalidate <c>SortingGroup</c> inheritance).</summary>
        public event Action<UniTextWorld> ParentChanged;

        /// <summary>Re-announces a change to one or more sorting-context inputs.</summary>
        internal void RaiseSortingChanged() => SortingChanged?.Invoke(this);

        #endregion

        #region Active Registry

        private static readonly List<UniTextWorld> activeInstances = new(16);
        private static readonly ReadOnlyList<UniTextWorld> activeView = new(activeInstances);

        /// <summary>
        /// All currently enabled <see cref="UniTextWorld"/> instances, maintained by
        /// <c>OnEnable</c> / <c>OnDisable</c>. Iterate this instead of <c>FindObjectsOfType</c>.
        /// </summary>
        public static IReadOnlyList<UniTextWorld> Active => activeView;

        private readonly struct ReadOnlyList<T> : IReadOnlyList<T>
        {
            private readonly List<T> inner;
            public ReadOnlyList(List<T> inner) { this.inner = inner; }
            public int Count => inner.Count;
            public T this[int index] => inner[index];
            IEnumerator<T> IEnumerable<T>.GetEnumerator() => inner.GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => inner.GetEnumerator();
        }

        #endregion

        #region Public API

        /// <inheritdoc/>
        public override bool IsWorldSpace => true;

        /// <summary>
        /// When <see langword="true"/>, this text receives pointer events
        /// (clicks, hover, interactive ranges) from <see cref="UniTextWorldRaycaster"/>.
        /// Disable for purely decorative text — the raycaster will skip it.
        /// </summary>
        public bool RaycastTarget
        {
            get => raycastTarget;
            set => raycastTarget = value;
        }

        #endregion

        #region Canvas Pipeline Suppression

        protected override void OnCanvasHierarchyChanged() { }
        protected override void UpdateGeometry() { }

        #endregion

        #region Abstract Implementations

        protected override void UpdateRendering()
        {
            var hasDecorations = PrepareDecorations();
            if (hasDecorations || (renderData != null && renderData.Count > 0))
                RenderDataAvailable?.Invoke(this);
            else
                ClearAllRenderers();
        }

        protected override void ClearAllRenderers()
        {
            RenderDataCleared?.Invoke(this);
            ReleaseBlendMaterials();
        }

        /// <inheritdoc/>
        protected override void ApplyVisibility() => RenderSuppressionChanged?.Invoke(this);

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            CleanupLegacySubMeshes();
            if (!activeInstances.Contains(this))
                activeInstances.Add(this);
            Activated?.Invoke(this);
            WarnIfNoRaycaster();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            activeInstances.Remove(this);
            Deactivated?.Invoke(this);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CleanupLegacySubMeshes();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();
            ParentChanged?.Invoke(this);
        }

        /// <summary>
        /// Removes legacy child GameObjects from the old per-object rendering system.
        /// </summary>
        private void CleanupLegacySubMeshes()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("-_UTWSM_-") || child.name.StartsWith("-_DEBUG_-"))
                    ObjectUtils.SafeDestroy(child.gameObject);
            }
        }

        private static Scene raycasterCheckedScene;

        private void WarnIfNoRaycaster()
        {
            if (!Application.isPlaying) return;
            if (!raycastTarget) return;
            var scene = gameObject.scene;
            if (raycasterCheckedScene == scene) return;
            raycasterCheckedScene = scene;
            if (ObjectUtils.FindAny<UniTextWorldRaycaster>() != null) return;
            Debug.LogWarning(
                $"[UniText] '{name}' is an interactive UniTextWorld but no UniTextWorldRaycaster was found in the scene. " +
                "Pointer events (clicks, hover, interactive ranges) will not fire. " +
                "Add a UniTextWorldRaycaster component to the camera that should pick up these events, " +
                "or set RaycastTarget = false on this text if it is purely decorative.",
                this);
        }

        #endregion
    }
}
