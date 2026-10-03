using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Tags <see cref="WorldBatcher"/>'s hidden mesh GameObjects so the orphan sweep can find and destroy
    /// stale ones. MUST stay a top-level class in its own matching file: a nested MonoBehaviour has no
    /// MonoScript, loses its script binding on domain reload, and the sweep stops seeing survivors.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class BatchMarker : MonoBehaviour { }
}
