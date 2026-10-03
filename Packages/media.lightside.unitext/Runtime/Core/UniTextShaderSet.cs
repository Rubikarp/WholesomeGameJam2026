using System.Collections.Generic;

namespace LightSide
{
    /// <summary>Shader names UniText renders through; the keys its build references are held under.</summary>
    internal static class UniTextShaderNames
    {
        public const string Canvas = LightSideShaderNames.Ui;
        public const string World = LightSideShaderNames.World;
        public const string WorldLit = LightSideShaderNames.WorldLit;
    }

    /// <summary>Declares the surfaces UniText renders through. They are Core's, so Core declares them; this exists only to keep the dependency visible when UniText ships without another package.</summary>
    internal sealed class UniTextShaderSet : ILightSideShaderSet
    {
        public IEnumerable<LightSideShaderRequest> Shaders
        {
            get
            {
                yield return new LightSideShaderRequest(UniTextShaderNames.Canvas);
                yield return new LightSideShaderRequest(UniTextShaderNames.World);
            }
        }
    }
}
