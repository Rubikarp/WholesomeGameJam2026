using System;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization.Components;

namespace LightSide.Localization
{
    internal static class UniTextLocalizeMenu
    {
        [MenuItem(UniTextMenu.Context.TextLocalize)]
        [MenuItem(UniTextMenu.Context.WorldLocalize)]
        private static void Localize(MenuCommand command)
        {
            if (command.context is UniTextBase target)
                SetupForLocalization(target);
        }

        /// <summary>
        /// Adds a <see cref="LocalizeStringEvent"/> to the GameObject and wires
        /// <c>OnUpdateString</c> to <see cref="UniTextBase.SetText(string)"/>.
        /// </summary>
        /// <remarks>
        /// The persistent listener targets the runtime-only <c>SetText</c> overload, not the
        /// <see cref="UniTextBase.Text"/> property setter. <c>SetText</c> overlays the
        /// localized value without writing to the serialized text field, so edit-mode
        /// previews triggered under <c>[ExecuteAlways]</c> do not mark the scene or prefab
        /// dirty. Unity's own TMP integration relies on the internal
        /// <c>LocalizationPropertyDriver</c> to suppress that side-effect; that driver is
        /// not part of the package's public API, so UniText avoids the problem at the
        /// wiring site instead.
        /// </remarks>
        public static LocalizeStringEvent SetupForLocalization(UniTextBase target)
        {
            var comp = Undo.AddComponent<LocalizeStringEvent>(target.gameObject);

            var setTextMethod = typeof(UniTextBase).GetMethod(
                nameof(UniTextBase.SetText), new[] { typeof(string) });
            var listener = (UnityAction<string>)Delegate.CreateDelegate(
                typeof(UnityAction<string>), target, setTextMethod);

            UnityEventTools.AddPersistentListener(comp.OnUpdateString, listener);
            comp.OnUpdateString.SetPersistentListenerState(0, UnityEventCallState.EditorAndRuntime);
            return comp;
        }
    }
}
