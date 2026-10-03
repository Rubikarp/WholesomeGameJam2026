using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Toggles which <see cref="CatZone"/> areas emit info/warn logs. State persists in EditorPrefs.
    /// Errors are never filtered. Zones come from package catalogs plus any seen at runtime.
    /// </summary>
    internal sealed class LogZonesWindow : EditorWindow
    {
        [MenuItem(LightSideMenu.Tools.LogZones)]
        public static void ShowWindow()
        {
            var window = GetWindow<LogZonesWindow>("Log Zones");
            window.minSize = new Vector2(280, 240);
        }

        /// <summary>Builds the retained-mode log-zone controls.</summary>
        public void CreateGUI()
        {
            var root = InspectorVisuals.CreateWindowRoot(rootVisualElement);
            var debugOn = ScriptingDefines.DebugDefinesOn;
            var debug = new InspectorToggle("Debug logging (master switch)")
            {
                value = debugOn,
                tooltip = "Compiles logging in or out for every LightSide package (" +
                          string.Join(", ", ScriptingDefines.DebugDefines) +
                          "). Toggling triggers a recompile.",
            };
            debug.RegisterValueChangedCallback(evt =>
                ScriptingDefines.SetDebugDefines(evt.newValue));
            root.Add(debug);
            if (ScriptingDefines.HasDebugMismatch)
                root.Add(new HelpBox(
                    "Debug defines differ between packages or build target groups. Toggle once " +
                    "to re-sync everything.", HelpBoxMessageType.Warning));
            if (!debugOn)
                root.Add(new HelpBox(
                    "Logging is compiled out. Zone toggles are saved and apply once debug " +
                    "logging is on.", HelpBoxMessageType.Info));

            var capture = new InspectorToggle("Capture external Debug.Log")
            {
                value = Cat.CaptureExternal,
                tooltip = "Also record logs that don't go through Cat.",
            };
            capture.RegisterValueChangedCallback(evt => Cat.CaptureExternal = evt.newValue);
            root.Add(capture);
            var stack = new InspectorToggle("Stack traces in file")
            {
                value = Cat.IncludeStack,
                tooltip = "Append a cleaned stack trace under every file entry.",
            };
            stack.RegisterValueChangedCallback(evt => Cat.IncludeStack = evt.newValue);
            root.Add(stack);

            var zones = CollectZones();
            var zoneToggles = new List<Toggle>(zones.Count);
            void SetZones(bool value)
            {
                SetAll(zones, value);
                for (var i = 0; i < zoneToggles.Count; i++)
                    zoneToggles[i].SetValueWithoutNotify(value);
            }
            var actions = InspectorVisuals.CreateRow();
            var allOn = new Button(() => SetZones(true)) { text = "All On" };
            var allOff = new Button(() => SetZones(false)) { text = "All Off" };
            allOn.style.flexGrow = 1f;
            allOff.style.flexGrow = 1f;
            actions.Add(allOn);
            actions.Add(allOff);
            root.Add(actions);

            var list = InspectorVisuals.CreateScrollStack();
            list.style.flexGrow = 1f;
            foreach (var zone in zones)
            {
                var capturedZone = zone;
                var toggle = new InspectorToggle(zone) { value = CatZoneRegistry.IsEnabled(zone) };
                toggle.RegisterValueChangedCallback(evt =>
                    CatZoneRegistry.SetEnabled(capturedZone, evt.newValue));
                zoneToggles.Add(toggle);
                list.Add(toggle);
            }
            root.Add(list);
            InspectorVisuals.AlignFields(root);
        }

        private static void SetAll(List<string> zones, bool value)
        {
            foreach (var zone in zones) CatZoneRegistry.SetEnabled(zone, value);
        }

        /// <summary>All registered zones (declared ones are registered eagerly, plus any seen at runtime or a
        /// prior session), sorted. Uses the canonical registered names so persistence keys stay case-consistent.</summary>
        private static List<string> CollectZones()
        {
            var zones = new List<string>(CatZoneRegistry.KnownNames());
            zones.Sort(StringComparer.OrdinalIgnoreCase);
            return zones;
        }
    }
}
