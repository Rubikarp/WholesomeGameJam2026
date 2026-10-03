using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    /// <summary>
    /// What a command palette was opened on: the surface, the selection with its asset paths already
    /// resolved, and the GameObject it acts on. Collections are empty rather than null, so a provider
    /// reads the context without guarding it. Passed by value so a command's closure can capture it.
    /// </summary>
    public readonly struct CommandContext
    {
        private static readonly Object[] noObjects = Array.Empty<Object>();
        private static readonly string[] noPaths = Array.Empty<string>();

        private readonly Object[] selection;
        private readonly string[] assetPaths;

        internal CommandContext(CommandSurface surface, ScreenRect anchor, Object[] selection,
            GameObject target)
        {
            Surface = surface;
            Anchor = anchor;
            this.selection = selection ?? noObjects;
            Target = target != null ? target : FirstGameObject(this.selection);
            assetPaths = ResolvePaths(this.selection);
        }

        /// <summary>The surface the palette was opened from.</summary>
        public CommandSurface Surface { get; }

        /// <summary>Screen-space rectangle the palette opens against.</summary>
        public ScreenRect Anchor { get; }

        /// <summary>
        /// The GameObject the palette acts on — the hierarchy item under the pointer, or the first
        /// selected one — which a created object parents to; null when the palette reached neither.
        /// Carries the meaning of <see cref="MenuCommand.context"/>.
        /// </summary>
        public GameObject Target { get; }

        /// <summary>Whether a scene object created here would land somewhere the user is looking: the Hierarchy, a Scene view, or the shortcut's palette.</summary>
        public bool CreatesObjects => Surface is CommandSurface.Hierarchy
            or CommandSurface.SceneView or CommandSurface.Global;

        /// <summary>Whether a project asset created here would land somewhere the user is looking: the Project window or the shortcut's palette.</summary>
        public bool CreatesAssets => Surface is CommandSurface.Project or CommandSurface.Global;

        /// <summary>The editor selection the palette opened over.</summary>
        public IReadOnlyList<Object> Selection => selection;

        /// <summary>Asset paths of the selection in selection order; selected objects that are not assets are omitted.</summary>
        public IReadOnlyList<string> AssetPaths => assetPaths;

        /// <summary>Whether <see cref="First{T}"/> finds anything.</summary>
        public bool Has<T>() where T : Object => First<T>() != null;

        /// <summary>
        /// The first selected object that is a <typeparamref name="T"/>, or the first
        /// <typeparamref name="T"/> on a selected GameObject; null when the selection holds neither.
        /// </summary>
        public T First<T>() where T : Object
        {
            foreach (var candidate in selection)
            {
                if (candidate is T match) return match;
                if (candidate is GameObject go && go.GetComponent(typeof(T)) is T component)
                    return component;
            }
            return null;
        }

        private static GameObject FirstGameObject(Object[] selection)
        {
            foreach (var candidate in selection)
                if (candidate is GameObject go) return go;
            return null;
        }

        private static string[] ResolvePaths(Object[] selection)
        {
            if (selection.Length == 0) return noPaths;
            var paths = new List<string>(selection.Length);
            foreach (var candidate in selection)
            {
                var path = AssetDatabase.GetAssetPath(candidate);
                if (!string.IsNullOrEmpty(path)) paths.Add(path);
            }
            return paths.Count == 0 ? noPaths : paths.ToArray();
        }
    }
}
