using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// The shared surface shaders. Three assets, split on the two axes a shader asset cannot straddle:
    /// render context (Canvas needs the uGUI stencil block and <c>unity_GUIZTestMode</c>) and lighting
    /// (a shader asset is Unity's unit of build inclusion, and the lighting keyword set is the largest
    /// cost in the family). Content is not an axis — the surface kind rides the vertex stream.
    /// HDRP adds a Shader Graph variant of the lit world surface, copied into the project while HDRP
    /// is active; the ShaderLab lit shader renders unlit there and the graph carries the lighting.
    /// </summary>
    public static class LightSideShaderNames
    {
        /// <summary>Canvas surface, unlit.</summary>
        public const string Ui = "LightSide/UI";

        /// <summary>World-space surface, unlit, casts shadows.</summary>
        public const string World = "LightSide/World";

        /// <summary>World-space surface that receives scene lighting; optional in a build.</summary>
        public const string WorldLit = "LightSide/World Lit";

        /// <summary>World-space lit surface for HDRP — a Shader Graph living at <c>Assets/LightSide/HDRP</c>; present only in HDRP projects.</summary>
        public const string WorldLitHdrp = "LightSide/World Lit HDRP";
    }

    /// <summary>Declares the shared surface shaders to <see cref="LightSideSettings"/>; the lit ones follow the project's Include Lit Shaders setting, and the HDRP graph is requested only while HDRP is active.</summary>
    internal sealed class LightSideCoreShaderSet : ILightSideShaderSet
    {
        public IEnumerable<LightSideShaderRequest> Shaders
        {
            get
            {
                yield return new LightSideShaderRequest(LightSideShaderNames.Ui);
                yield return new LightSideShaderRequest(LightSideShaderNames.World);
                yield return new LightSideShaderRequest(LightSideShaderNames.WorldLit,
                    LightSideSettings.IncludeLitShaders);
                yield return new LightSideShaderRequest(LightSideShaderNames.WorldLitHdrp,
                    LightSideSettings.IncludeLitShaders && LightSideRenderPipeline.IsHdrp);
            }
        }
    }
}
