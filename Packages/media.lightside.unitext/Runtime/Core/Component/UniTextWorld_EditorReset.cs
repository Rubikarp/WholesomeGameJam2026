#if UNITY_EDITOR
using System;
using UnityEditor;

namespace LightSide
{
    public partial class UniTextWorld
    {
        /// <summary>
        /// Editor-only Unity callback fired when the component is freshly added (Add Component)
        /// or reset from the inspector. Seeds serialized fields from
        /// <see cref="UniTextSettings.WorldTextPrefab"/>'s own <see cref="UniTextWorld"/>
        /// component so new instances inherit the project's configured defaults. No-op if the
        /// prefab is unset or has no <see cref="UniTextWorld"/> component on it.
        /// </summary>
        protected override void Reset()
        {
            base.Reset();
            var prefab = UniTextSettings.WorldTextPrefab;
            if (prefab == null) return;
            var template = prefab.GetComponentInChildren<UniTextWorld>(true);
            if (template == null || template == this) return;
            EditorUtility.CopySerialized(template, this);
            if (isActiveAndEnabled)
            {
                ProcessingStarted += OnBeforeProcess;
            }

            void OnBeforeProcess()
            {
                ProcessingStarted -= OnBeforeProcess;
                sourceText = (Text ?? "").AsMemory();
                enabled = false;
                enabled = true;
            }
        }
    }
}
#endif
