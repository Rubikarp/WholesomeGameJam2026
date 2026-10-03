#if UNITY_EDITOR
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Ownership record for the components a SceneView inline-editing session adds to a text object.
    /// Lives on that object under the same hide flags as everything it owns, so the record and the
    /// components it accounts for always share a fate — neither can outlive the other — and holds
    /// ownership as value state rather than references, so it survives every serialization round trip
    /// the editor performs. Reclaiming therefore needs nothing but the record itself, and any editor
    /// state a session does not survive still leaves the host's own component graph recoverable.
    /// A scaffold is garbage unless a session running in this domain has claimed it: the claim is
    /// unserialized, so neither a duplicate of the edited object nor a domain reload can inherit one.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    internal sealed class SceneEditScaffold : MonoBehaviour
    {
        /// <summary>Flags carried by the scaffold and by every component it owns: out of the inspector, never written to a scene or prefab.</summary>
        internal const HideFlags SessionHideFlags = HideFlags.HideInInspector | HideFlags.DontSave;

        [SerializeField] private bool hadEditable;
        [SerializeField] private bool hadSelectable;
        [SerializeField] private bool hadFocusable;
        [SerializeField] private bool hadCaret;
        [SerializeField] private bool restoreMarkup;
        [SerializeField] private MarkupVisibility markup;
        [System.NonSerialized] private bool claimed;

        /// <summary>
        /// Records the host's pre-session component set and returns the scaffold that owns whatever the
        /// session adds next; call before adding anything. A stale scaffold found on the host is reclaimed first.
        /// </summary>
        internal static SceneEditScaffold Attach(GameObject host)
        {
            var stale = host.GetComponent<SceneEditScaffold>();
            if (stale != null) stale.Reclaim();

            var editable = host.GetComponent<UniTextEditable>();
            var scaffold = host.AddComponent<SceneEditScaffold>();
            scaffold.hideFlags = SessionHideFlags;
            scaffold.claimed = true;
            scaffold.hadEditable = editable != null;
            scaffold.hadCaret = editable != null && editable.CaretRenderer != null;
            scaffold.hadSelectable = host.GetComponent<UniTextSelectable>() != null;
            scaffold.hadFocusable = host.GetComponent<UniTextFocusable>() != null;
            return scaffold;
        }

        /// <summary>Stamps <see cref="SessionHideFlags"/> on the components the session added; call once the additions are in place.</summary>
        internal void StampOwned()
        {
            if (!hadEditable) Stamp(GetComponent<UniTextEditable>());
            if (!hadSelectable) Stamp(GetComponent<UniTextSelectable>());
        }

        /// <summary>Remembers the markup visibility to put back, which applies only to an editable the session found rather than created.</summary>
        internal void CaptureMarkupVisibility(UniTextEditable editable)
        {
            if (!hadEditable || editable == null) return;
            restoreMarkup = true;
            markup = editable.MarkupVisibility;
        }

        /// <summary>
        /// Removes every object the session added, restores what it changed, and removes the record,
        /// leaving the host's own component graph as it was. One object failing to go does not stop the
        /// rest, and the record outlives anything that would not go — unclaimed, so a later sweep
        /// reclaims it. The scaffold is destroyed once nothing it owns is left, and must not be used after that call.
        /// </summary>
        internal void Reclaim()
        {
            claimed = false;
            var host = gameObject;
            var editable = host.GetComponent<UniTextEditable>();
            if (restoreMarkup && editable != null) editable.MarkupVisibility = markup;

            var caret = editable != null ? editable.CaretRenderer : null;
            if (!hadCaret && caret != null && caret.transform.parent == host.transform)
                Remove(caret.gameObject);
            if (!hadEditable) Remove(editable);
            if (!hadFocusable) Remove(host.GetComponent<UniTextFocusable>());
            if (!hadSelectable) Remove(host.GetComponent<UniTextSelectable>());
            if (Owns(host)) return;
            Remove(this);
        }

        /// <summary>
        /// Reclaims every scaffold no session in this domain has claimed — a copy carried onto a
        /// duplicate of the edited object, or one that outlived its session. Reaches inactive objects,
        /// hidden components and prefab stages; a running session's own scaffold is left alone.
        /// </summary>
        internal static void ReclaimUnclaimed()
        {
            var scaffolds = Resources.FindObjectsOfTypeAll<SceneEditScaffold>();
            for (int i = 0; i < scaffolds.Length; i++)
            {
                var scaffold = scaffolds[i];
                if (scaffold != null && !scaffold.claimed) scaffold.Reclaim();
            }
        }

        private bool Owns(GameObject host)
            => (!hadEditable && host.GetComponent<UniTextEditable>() != null)
            || (!hadFocusable && host.GetComponent<UniTextFocusable>() != null)
            || (!hadSelectable && host.GetComponent<UniTextSelectable>() != null);

        private static void Stamp(Component component)
        {
            if (component != null) component.hideFlags |= SessionHideFlags;
        }

        private static void Remove(Object obj)
        {
            if (obj == null) return;
            try
            {
                ObjectUtils.SafeDestroy(obj);
            }
            catch (System.Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}
#endif
