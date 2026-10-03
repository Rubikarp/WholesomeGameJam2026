using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LightSide
{
    /// <summary>
    /// Resolves a package's installed version. For a registered package
    /// <see cref="PackageInfo.FindForAssembly"/> answers directly; when the package lives under
    /// <c>Assets/</c> (e.g. as a dev-host submodule, not a package) it falls back to reading
    /// <c>package.json</c> at the package root located from the given runtime asmdef file name.
    /// </summary>
    public static class PackageVersion
    {
        static readonly Regex versionRegex = new("\"version\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>
        /// Version of the package containing <paramref name="assembly"/>, or the one whose runtime asmdef
        /// is named <paramref name="asmdefFileName"/> when the assembly resolves to no package. Null when
        /// neither answers.
        /// </summary>
        public static string Resolve(Assembly assembly, string asmdefFileName)
        {
            var pkg = PackageInfo.FindForAssembly(assembly);
            if (!string.IsNullOrEmpty(pkg?.version)) return pkg.version;
            return ReadFromPackageJson(asmdefFileName);
        }

        static string ReadFromPackageJson(string asmdefFileName)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/" + asmdefFileName)) continue;

                var root = Path.GetDirectoryName(Path.GetDirectoryName(path));
                var packageJson = Path.Combine(root ?? "", "package.json");
                if (!File.Exists(packageJson)) continue;

                var m = versionRegex.Match(File.ReadAllText(packageJson));
                if (m.Success) return m.Groups[1].Value;
            }
            return null;
        }
    }
}
