using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Carries copied managed references behind serialized fields so the shared fields can render
    /// them; value types live in <see cref="ClipboardValueHost{T}"/> subclasses instead. Never
    /// persisted — an instance exists only while a clipboard preview is on screen.
    /// </summary>
    internal sealed class ClipboardPreviewHost : ScriptableObject
    {
        [SerializeReference] internal object value;
        [SerializeReference] internal List<object> values = new();
    }
}
