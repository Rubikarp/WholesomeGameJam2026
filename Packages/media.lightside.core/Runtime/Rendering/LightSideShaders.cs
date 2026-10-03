using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// One shader a package needs present in every build, and whether the project currently includes it.
    /// </summary>
    public readonly struct LightSideShaderRequest
    {
        /// <summary>The shader's declared name, e.g. <c>UniText/SDF</c>.</summary>
        public readonly string Name;

        /// <summary>Whether the project keeps this shader; <see langword="false"/> drops it from builds and makes <see cref="LightSideShaders.Require"/> throw for it.</summary>
        public readonly bool Included;

        public LightSideShaderRequest(string name, bool included = true)
        {
            Name = name;
            Included = included;
        }
    }

    /// <summary>
    /// Declares the shaders a package cannot render without. Implementations are discovered by the
    /// editor, which holds a reference to each included shader in the shared render-settings asset so
    /// it reaches builds; the package itself never manages build inclusion.
    /// </summary>
    /// <remarks>Implementations must have a public parameterless constructor and no state.</remarks>
    public interface ILightSideShaderSet
    {
        /// <summary>The package's shaders, in any order.</summary>
        IEnumerable<LightSideShaderRequest> Shaders { get; }
    }

    /// <summary>
    /// Resolves LightSide shaders by name through <see cref="LightSideSettings"/>, which holds the
    /// references that reach builds. <c>Shader.Find</c> is the remainder and reaches nothing extra in a
    /// player, so an unresolved name is a build-configuration failure, not a state callers branch on.
    /// </summary>
    public static class LightSideShaders
    {
        /// <summary>Resolves <paramref name="shaderName"/>, or <see langword="null"/> when the project holds no reference to it and the build contains none.</summary>
        public static Shader Find(string shaderName)
        {
            var shader = LightSideSettings.FindShader(shaderName);
            if (shader == null) shader = Shader.Find(shaderName);
            return shader;
        }

        /// <summary>Resolves a shader the caller cannot render without.</summary>
        /// <exception cref="InvalidOperationException">The project holds no reference to the shader and the build contains none.</exception>
        public static Shader Require(string shaderName)
        {
            var shader = Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException(
                    $"Shader '{shaderName}' is unavailable. LightSideSettings holds the reference that " +
                    "ships it with builds; an empty entry means the project excluded it — see " +
                    "Project Settings > LightSide.");
            return shader;
        }
    }
}
