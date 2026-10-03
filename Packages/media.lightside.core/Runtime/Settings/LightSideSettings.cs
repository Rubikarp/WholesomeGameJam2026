using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Project-wide settings shared by every LightSide package. Currently carries the references that
    /// bring required shaders into builds, keyed by shader name.
    /// </summary>
    /// <remarks>
    /// Lives at <c>Resources/LightSideSettings</c>. The editor keeps the shader entries in step with the
    /// <see cref="ILightSideShaderSet"/> implementations it discovers; nothing at runtime writes to it.
    /// A shader reachable only through <c>Shader.Find</c> does not survive a build, so an entry here is
    /// the one thing that ships it.
    /// </remarks>
    public sealed class LightSideSettings : ScriptableObject
    {
        internal const string ResourcePath = "LightSideSettings";

        [Serializable]
        internal struct ShaderEntry
        {
            public string name;
            public Shader shader;
        }

        [SerializeField, HideInInspector]
        internal ShaderEntry[] shaders = Array.Empty<ShaderEntry>();

        [SerializeField]
        private bool includeLitShaders = true;

        /// <summary>
        /// Whether the project keeps <see cref="LightSideShaderNames.WorldLit"/>. Lighting is the one
        /// expensive axis of the surface family — thousands of variants — so a project with no lit
        /// world content drops it. Unlit world surfaces have their own shader and are unaffected.
        /// </summary>
        /// <remarks>Project-wide because the shader is: text, shapes, decorations and vector animation all render through it.</remarks>
        public static bool IncludeLitShaders
        {
            get
            {
                var settings = Instance;
                return settings == null || settings.includeLitShaders;
            }
            set
            {
                var settings = Instance;
                if (settings == null || settings.includeLitShaders == value) return;
                settings.includeLitShaders = value;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(settings);
#endif
            }
        }

        private static LightSideSettings instance;
        private static bool loadAttempted;

        [NonSerialized] private Dictionary<string, Shader> lookup;

        /// <summary>The loaded settings instance, or <see langword="null"/> when the project has none.</summary>
        internal static LightSideSettings Instance
        {
            get
            {
                if (instance != null) return instance;
                if (loadAttempted) return null;
                loadAttempted = true;
                instance = Resources.Load<LightSideSettings>(ResourcePath);
                return instance;
            }
        }

        /// <summary>Re-points the singleton, dropping any cached lookup. For the editor, which creates or repairs the asset.</summary>
        internal static void SetInstance(LightSideSettings settings)
        {
            instance = settings;
            loadAttempted = true;
            if (settings != null) settings.lookup = null;
        }

        /// <summary>The shader held under <paramref name="shaderName"/>, or <see langword="null"/> when the project holds none.</summary>
        internal static Shader FindShader(string shaderName)
        {
            var settings = Instance;
            return settings == null ? null : settings.Resolve(shaderName);
        }

        private Shader Resolve(string shaderName)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, Shader>(shaders.Length, StringComparer.Ordinal);
                for (var i = 0; i < shaders.Length; i++)
                {
                    var entry = shaders[i];
                    if (!string.IsNullOrEmpty(entry.name))
                        lookup[entry.name] = entry.shader;
                }
            }
            return lookup.TryGetValue(shaderName, out var shader) ? shader : null;
        }

        private void OnDisable() => lookup = null;
    }
}
