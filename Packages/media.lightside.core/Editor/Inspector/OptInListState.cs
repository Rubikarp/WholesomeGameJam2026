using System.Collections.Generic;
using UnityEditor;

namespace LightSide
{
    /// <summary>
    /// The editor-session reveal and expand state behind an opt-in parameter list: which
    /// default-valued rows the user revealed (persisted per session under a package prefix) and
    /// whether each list is expanded (in-memory, open by default).
    /// </summary>
    public sealed class OptInListState
    {
        private readonly HashSet<string> revealed = new();
        private readonly Dictionary<string, bool> expanded = new();
        private readonly string sessionPrefix;

        /// <summary>Creates a store whose reveal keys persist under <paramref name="sessionPrefix"/>.</summary>
        public OptInListState(string sessionPrefix)
            => this.sessionPrefix = sessionPrefix;

        /// <summary>Managed references use their identity so UI state follows list reorders; ordinary serialized values have no independent identity and use their owning object and property path.</summary>
        public static string StateKey(SerializedProperty property)
        {
            var targetId = ObjectUtils.GetInstanceIdCompat(property.serializedObject.targetObject);
            if (property.propertyType != SerializedPropertyType.ManagedReference)
                return $"{targetId}#{property.propertyPath}";
            return $"{targetId}#{property.managedReferenceId}";
        }

        /// <summary>Loads the session-persisted reveal flags of one list into the in-memory set.</summary>
        public void RestoreRevealed(string key, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var revealKey = key + ":" + i;
                if (SessionState.GetBool(sessionPrefix + revealKey, false))
                    revealed.Add(revealKey);
            }
        }

        /// <summary>Whether the row at <paramref name="index"/> of list <paramref name="key"/> is revealed.</summary>
        public bool IsRevealed(string key, int index) => revealed.Contains(key + ":" + index);

        /// <summary>Reveals or hides one row, persisting the flag for the session.</summary>
        public void SetRevealed(string key, int index, bool value)
        {
            var revealKey = key + ":" + index;
            if (value) revealed.Add(revealKey);
            else revealed.Remove(revealKey);
            SessionState.SetBool(sessionPrefix + revealKey, value);
        }

        /// <summary>Whether the list at <paramref name="key"/> is expanded; open by default.</summary>
        public bool GetExpanded(string key) => !expanded.TryGetValue(key, out var e) || e;

        /// <summary>Stores the expand state of one list.</summary>
        public void SetExpanded(string key, bool value) => expanded[key] = value;
    }
}
