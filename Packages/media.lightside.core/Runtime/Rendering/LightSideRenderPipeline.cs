using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightSide
{
    /// <summary>Identifies the active render pipeline family without referencing pipeline assemblies.</summary>
    public static class LightSideRenderPipeline
    {
        private const string HdrpAssetTypeName = "UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset";
        private const string HdrpMaterialTypeName =
            "UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime";

        private static MethodInfo validateMaterial;
        private static bool validateMaterialResolved;

        /// <summary>Whether the active render pipeline is HDRP.</summary>
        public static bool IsHdrp
        {
            get
            {
                var pipeline = GraphicsSettings.currentRenderPipeline;
                return pipeline != null && pipeline.GetType().FullName == HdrpAssetTypeName;
            }
        }

        /// <summary>
        /// Runs HDRP's material validation on a script-created material. HDRP shaders keep their
        /// blend, stencil and pass state in material properties whose serialized defaults are opaque —
        /// a material that skips validation renders without transparency.
        /// </summary>
        /// <exception cref="InvalidOperationException">HDRP's runtime material API is unavailable.</exception>
        public static void ValidateHdrpMaterial(Material material)
        {
            if (!validateMaterialResolved)
            {
                validateMaterialResolved = true;
                validateMaterial = Type.GetType(HdrpMaterialTypeName, false)
                    ?.GetMethod("ValidateMaterial", BindingFlags.Static | BindingFlags.Public, null,
                        new[] { typeof(Material) }, null);
            }

            if (validateMaterial == null)
                throw new InvalidOperationException(
                    "HDMaterial.ValidateMaterial is unavailable — the installed HDRP version does not " +
                    "expose the runtime material API this material requires.");
            if (!(bool)validateMaterial.Invoke(null, new object[] { material }))
                throw new InvalidOperationException(
                    $"HDRP did not recognize '{material.shader.name}' as an HDRP shader; the material " +
                    "cannot reach a valid render state.");
        }
    }
}
