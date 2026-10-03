using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// The shared surface materials — one instance per shader, for the whole process.
    /// </summary>
    /// <remarks>
    /// Sharing the <em>instance</em>, not merely the shader, is what lets a Canvas run of mixed LightSide
    /// elements collapse into one draw call: uGUI batches by material and texture, so two materials over
    /// the same shader still cost two draws. Every sampled atlas is a global binding for the same reason.
    /// Created on first access (main thread) and kept for the application lifetime.
    /// </remarks>
    public static class LightSideMaterials
    {
        private static Material ui;
        private static readonly Material[] world = new Material[2];

#if UNITY_EDITOR
        static LightSideMaterials()
        {
            EditorLifecycle.UnmanagedCleaning += () =>
            {
                ObjectUtils.SafeDestroy(ui); ui = null;
                for (var i = 0; i < world.Length; i++)
                {
                    ObjectUtils.SafeDestroy(world[i]);
                    world[i] = null;
                }
            };
            UnityEngine.Rendering.RenderPipelineManager.activeRenderPipelineTypeChanged += DropWorldMaterials;
        }

        /// <summary>
        /// Drops the world materials without destroying them: renderers keep the old instance until
        /// their next rebuild, and a destroyed material would leave them pink for that window.
        /// </summary>
        private static void DropWorldMaterials()
        {
            for (var i = 0; i < world.Length; i++)
                world[i] = null;
        }
#endif

        /// <summary>The Canvas surface material every LightSide Graphic renders through.</summary>
        /// <exception cref="System.InvalidOperationException">The shader is absent from the build.</exception>
        public static Material Ui
        {
            get
            {
                if (ui == null) ui = Create(LightSideShaders.Require(LightSideShaderNames.Ui), "LightSide UI");
                return ui;
            }
        }

        /// <summary>The world-space surface material, unlit or lit. Under HDRP the lit material uses the HDRP Shader Graph when the project holds it.</summary>
        /// <exception cref="System.InvalidOperationException">The shader is absent from the build.</exception>
        public static Material World(bool lit)
        {
            var index = lit ? 1 : 0;
            if (world[index] != null) return world[index];

            if (!lit)
                return world[index] = Create(LightSideShaders.Require(LightSideShaderNames.World), "LightSide World");

            var hdrpShader = LightSideRenderPipeline.IsHdrp
                ? LightSideShaders.Find(LightSideShaderNames.WorldLitHdrp)
                : null;
            var material = Create(
                hdrpShader != null ? hdrpShader : LightSideShaders.Require(LightSideShaderNames.WorldLit),
                "LightSide World Lit");
            if (hdrpShader != null)
                LightSideRenderPipeline.ValidateHdrpMaterial(material);
            return world[index] = material;
        }

        private static Material Create(Shader shader, string materialName)
            => new(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = materialName,
            };
    }
}
