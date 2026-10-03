using System;
using UnityEngine;

namespace LightSide.Inspection
{
    [AddComponentMenu("")]
    internal sealed class InspectorHotkeyListener : MonoBehaviour
    {
        private Action tickCallback;
        private TickHandle tickHandle;

        private void OnEnable() =>
            CoreLoop.Updating.Toggle(ref tickHandle, tickCallback ??= OnTick, Application.isPlaying);

        private void OnDisable() =>
            CoreLoop.Updating.Toggle(ref tickHandle, tickCallback, false);

        private void OnTick()
        {
            if (InputUtils.GetKeyDown(UniTextInspector.ToggleKey))
                UniTextInspector.Toggle();
        }
    }
}
