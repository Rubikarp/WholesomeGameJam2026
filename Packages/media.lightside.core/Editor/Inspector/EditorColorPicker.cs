using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>Opens Unity's editor colour picker on demand — there is no public API, so it binds <c>UnityEditor.ColorPicker.Show</c> by reflection and is resilient to its varying trailing bool parameters across versions.</summary>
    internal static class EditorColorPicker
    {
        private static Type type;
        private static MethodInfo show;
        private static bool resolved;
        private static PropertyInfo eyeDropperOpened;
        private static bool eyeDropperResolved;

        internal static EditorWindow OpenWindow
        {
            get
            {
                Resolve();
                if (type == null) return null;
                var instances = Resources.FindObjectsOfTypeAll(type);
                for (var i = 0; i < instances.Length; i++)
                    if (instances[i] is EditorWindow window)
                        return window;
                return null;
            }
        }

        /// <summary>Whether the screen-colour eye dropper is capturing, during which no editor window holds focus.</summary>
        internal static bool EyeDropperOpen
        {
            get
            {
                if (!eyeDropperResolved)
                {
                    eyeDropperResolved = true;
                    eyeDropperOpened = typeof(Editor).Assembly.GetType("UnityEditor.EyeDropper")
                        ?.GetProperty("IsOpened", BindingFlags.Public | BindingFlags.Static);
                }
                return eyeDropperOpened != null && (bool)eyeDropperOpened.GetValue(null);
            }
        }

        internal static void Show(Action<Color> onChanged, Color color, bool showAlpha = true)
        {
            Resolve();
            if (show == null) return;

            var pars = show.GetParameters();
            var args = new object[pars.Length];
            args[0] = onChanged;
            args[1] = color;
            for (var i = 2; i < pars.Length; i++)
                args[i] = i == 2 ? showAlpha
                    : pars[i].HasDefaultValue ? pars[i].DefaultValue
                    : pars[i].ParameterType.IsValueType ? Activator.CreateInstance(pars[i].ParameterType) : null;
            show.Invoke(null, args);
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            type = typeof(Editor).Assembly.GetType("UnityEditor.ColorPicker");
            if (type == null) return;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                var parameters = method.GetParameters();
                if (method.Name != "Show" || parameters.Length < 4 ||
                    parameters[0].ParameterType != typeof(Action<Color>)) continue;
                show = method;
                return;
            }
        }
    }
}
