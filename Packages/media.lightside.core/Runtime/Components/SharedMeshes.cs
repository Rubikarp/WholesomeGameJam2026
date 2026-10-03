using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Shared mesh instances for UI rendering.
    /// </summary>
    /// <remarks>
    /// CanvasRenderer.SetMesh() copies data, so the same mesh instances can be reused
    /// across all consumers. No pooling needed - just a simple array.
    /// </remarks>
    public static class SharedMeshes
    {
        private static readonly List<Mesh> meshes = new(4);

        /// <summary>
        /// Gets a shared mesh by index, creating it if necessary.
        /// </summary>
        /// <param name="index">The mesh index (typically submesh index).</param>
        /// <returns>A reusable Mesh instance.</returns>
        public static Mesh Get(int index)
        {
            while (meshes.Count <= index)
                meshes.Add(null);

            var mesh = meshes[index];
            if (mesh == null)
            {
                mesh = new Mesh { name = "LightSide Shared Mesh" };
                meshes[index] = mesh;
            }
            return mesh;
        }

    #if UNITY_EDITOR
        static SharedMeshes()
        {
            EditorLifecycle.UnmanagedCleaning += DestroyAll;
        }

        private static void DestroyAll()
        {
            for (var i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] != null)
                    Object.DestroyImmediate(meshes[i]);
            }
            meshes.Clear();
        }
    #endif
    }
}
