using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LightSide
{
    /// <summary>
    /// World-space pointer raycaster for <see cref="UniTextWorld"/> components.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attach to a <see cref="Camera"/> to route pointer events (clicks, hover) to all active
    /// <see cref="UniTextWorld"/> components visible from that camera. Analogous to
    /// <see cref="UnityEngine.UI.GraphicRaycaster"/> for Canvas, but uses direct ray/plane/rect
    /// math instead of colliders — no physics dependency at the text level.
    /// </para>
    /// <para>
    /// Hit detection: rect containment in each component's local <see cref="RectTransform"/> rect.
    /// The full rect is the clickable area (like Canvas <c>raycastTarget</c>); finer-grained
    /// glyph-accurate hit testing happens inside the component's pointer handling via
    /// <see cref="UniTextBase.HitTestRange(Vector2, Camera, float)"/>.
    /// </para>
    /// <para>
    /// Occlusion: results carry <c>distance</c>, so Unity's <see cref="EventSystem"/> sorts them
    /// against any other raycaster (<see cref="UnityEngine.UI.GraphicRaycaster"/>,
    /// <c>PhysicsRaycaster</c>, <c>Physics2DRaycaster</c>) automatically. For scenes without a
    /// PhysicsRaycaster but with physical geometry that should block clicks, set
    /// <see cref="BlockingObjects"/> to <c>TwoD</c>/<c>ThreeD</c>/<c>All</c> — the raycaster will
    /// test for blocking colliders between the camera and the hit point before accepting the hit.
    /// </para>
    /// </remarks>
    [AddComponentMenu(UniTextMenu.AddComponent.WorldRaycaster)]
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class UniTextWorldRaycaster : BaseRaycaster
    {
        /// <summary>Physics systems consulted for occlusion between the camera and a text hit.</summary>
        public enum BlockingObjects
        {
            /// <summary>No occlusion check. Other raycasters (if present) still participate via distance sort.</summary>
            None,
            /// <summary>Check 2D colliders (<c>Physics2D</c>).</summary>
            TwoD,
            /// <summary>Check 3D colliders (<c>Physics</c>).</summary>
            ThreeD,
            /// <summary>Check both 2D and 3D colliders.</summary>
            All
        }

        #region Serialized Fields

        [SerializeField, StatePassive]
        [Tooltip("Physical colliders that occlude text hits between the camera and the hit point. Leave as None if a PhysicsRaycaster is also on this camera — distance sort handles it.")]
        private BlockingObjects blockingObjects = BlockingObjects.None;

        [SerializeField, StatePassive]
        [Tooltip("Layer mask used for blocking collider checks. Default includes all layers.")]
        private LayerMask blockingMask = ~0;

        [SerializeField, StatePassive]
        [Tooltip("Raycaster sort priority. Higher priority wins when sorting orders conflict.")]
        private int sortPriority;

        [SerializeField, StatePassive]
        [Tooltip("Raycaster render priority. Secondary tie-breaker in sort comparison.")]
        private int renderPriority;

        #endregion

        #region Runtime State

        private Camera cachedCamera;

        #endregion

        #region Public API

        /// <summary>Gets or sets which physics systems are consulted for occlusion.</summary>
        public BlockingObjects BlockingMode
        {
            get => blockingObjects;
            set => blockingObjects = value;
        }

        /// <summary>Gets or sets the layer mask for blocking collider queries.</summary>
        public LayerMask BlockingMask
        {
            get => blockingMask;
            set => blockingMask = value;
        }

        #endregion

        #region BaseRaycaster

        /// <inheritdoc/>
        public override Camera eventCamera
        {
            get
            {
                if (cachedCamera == null) cachedCamera = GetComponent<Camera>();
                return cachedCamera;
            }
        }

        /// <inheritdoc/>
        public override int sortOrderPriority => sortPriority;

        /// <inheritdoc/>
        public override int renderOrderPriority => renderPriority;

        /// <inheritdoc/>
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            var cam = eventCamera;
            if (cam == null) return;

            var screenPos = eventData.position;
            if (!cam.pixelRect.Contains(screenPos)) return;

            var ray = cam.ScreenPointToRay(screenPos);
            var active = UniTextWorld.Active;
            var count = active.Count;

            for (var i = 0; i < count; i++)
            {
                var world = active[i];
                if (world == null) continue;
                if (!world.IsVisible) continue;
                if (!world.RaycastTarget) continue;
                if (!TryRaycastInstance(world, ray, out var worldHit, out var distance))
                    continue;

                if (blockingObjects != BlockingObjects.None &&
                    IsOccluded(ray.origin, worldHit, distance))
                    continue;

                resultAppendList.Add(new RaycastResult
                {
                    gameObject = world.gameObject,
                    module = this,
                    distance = distance,
                    worldPosition = worldHit,
                    worldNormal = world.transform.forward,
                    screenPosition = screenPos,
                    displayIndex = cam.targetDisplay,
                    sortingLayer = world.SortingLayerID,
                    sortingOrder = world.SortingOrder,
                    depth = 0,
                    index = resultAppendList.Count,
                });
            }
        }

        #endregion

        #region Hit Testing

        private static bool TryRaycastInstance(UniTextWorld world, Ray ray,
            out Vector3 worldHit, out float distance)
        {
            worldHit = default;
            distance = 0f;

            var wt = world.transform;
            var forward = wt.forward;
            var denom = Vector3.Dot(ray.direction, forward);
            if (Mathf.Abs(denom) < 1e-6f) return false;

            var t = Vector3.Dot(wt.position - ray.origin, forward) / denom;
            if (t <= 0f) return false;

            worldHit = ray.origin + ray.direction * t;

            var local = wt.InverseTransformPoint(worldHit);
            var rect = world.rectTransform.rect;
            if (!rect.Contains(new Vector2(local.x, local.y))) return false;

            distance = t;
            return true;
        }

        private bool IsOccluded(Vector3 rayOrigin, Vector3 worldHit, float hitDistance)
        {
            var direction = worldHit - rayOrigin;
            var fullDistance = direction.magnitude;
            if (fullDistance <= 1e-6f) return false;
            direction /= fullDistance;

            var probeDistance = Mathf.Min(hitDistance, fullDistance);

#if UNITEXT_HAS_PHYSICS
            if (blockingObjects == BlockingObjects.ThreeD || blockingObjects == BlockingObjects.All)
            {
                if (Physics.Raycast(rayOrigin, direction, out var hit3D, probeDistance, blockingMask,
                        QueryTriggerInteraction.Ignore)
                    && hit3D.distance < probeDistance - 1e-4f)
                    return true;
            }
#endif
#if UNITEXT_HAS_PHYSICS2D
            if (blockingObjects == BlockingObjects.TwoD || blockingObjects == BlockingObjects.All)
            {
                var hit2D = Physics2D.Raycast(rayOrigin, direction, probeDistance, blockingMask);
                if (hit2D.collider != null && hit2D.distance < probeDistance - 1e-4f)
                    return true;
            }
#endif
            return false;
        }

        #endregion
    }
}
