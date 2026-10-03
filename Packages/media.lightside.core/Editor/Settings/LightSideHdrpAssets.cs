using System.IO;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Delivers the HDRP Shader Graph assets: copies them from the package's hidden
    /// <c>HdrpAssets~</c> folder into <c>Assets/LightSide/HDRP</c> while HDRP is active.
    /// The graphs cannot live in the imported package tree — a <c>.shadergraph</c> without
    /// com.unity.shadergraph installed raises import errors in Built-in-only projects — and the
    /// hidden folder is a delivery staging area, never a Package Manager sample: importing a second
    /// copy through the samples UI would duplicate the shader in the project.
    /// </summary>
    internal static class LightSideHdrpAssets
    {
        private const string SourceFolder = "Packages/media.lightside.core/HdrpAssets~";
        private const string TargetFolder = "Assets/LightSide/HDRP";

        /// <summary>
        /// Copies any missing HDRP shader assets into the project and imports them. No-op outside
        /// HDRP and once the shaders resolve; files the project already has are never overwritten.
        /// </summary>
        internal static void Ensure()
        {
            if (!LightSideRenderPipeline.IsHdrp) return;
            if (Shader.Find(LightSideShaderNames.WorldLitHdrp) != null) return;

            var source = FileUtil.GetPhysicalPath(SourceFolder);
            if (!Directory.Exists(source))
            {
                Debug.LogError($"[LightSide] HDRP shader sources are missing from the package: {SourceFolder}");
                return;
            }

            Directory.CreateDirectory(TargetFolder);
            var copied = false;
            foreach (var file in Directory.GetFiles(source))
            {
                var target = Path.Combine(TargetFolder, Path.GetFileName(file));
                if (File.Exists(target)) continue;
                File.Copy(file, target);
                copied = true;
            }

            if (copied)
                AssetDatabase.ImportAsset(TargetFolder, ImportAssetOptions.ImportRecursive);
        }
    }
}
