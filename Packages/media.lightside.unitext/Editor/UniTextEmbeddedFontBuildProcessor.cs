using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Content;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_ANDROID
using UnityEditor.Android;
#endif

namespace LightSide
{
    internal sealed class UniTextEmbeddedFontBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            UniTextEmbeddedFontPlayerBuild.Begin(report);
        }
    }

    internal sealed class UniTextEmbeddedFontBuildPostprocessor : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            UniTextEmbeddedFontPlayerBuild.Complete(report);
        }
    }

#if UNITY_ANDROID
    internal sealed class UniTextEmbeddedFontAndroidBuildPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            UniTextEmbeddedFontPlayerBuild.CompleteAndroidProject(path);
        }
    }
#endif

    internal static class UniTextEmbeddedFontPlayerBuild
    {
        private const string FontsDirectoryName = "Fonts";
        private const string UniTextDirectoryName = "UniText";
        private static readonly MethodInfo setBuildResultMethod = FindBuildReportMethod(
            "SetBuildResult", typeof(BuildResult));
        private static readonly MethodInfo addMessageMethod = FindBuildReportMethod(
            "AddMessage", typeof(LogType), typeof(string), typeof(string));
        private static readonly MethodInfo recordFilesAddedRecursiveMethod = FindBuildReportMethod(
            "RecordFilesAddedRecursive", typeof(string), typeof(string));
        private static readonly MethodInfo recordFilesDeletedRecursiveMethod = FindBuildReportMethod(
            "RecordFilesDeletedRecursive", typeof(string));

        private static BuildReport currentReport;
        private static Exception androidFailure;
        private static bool androidProcessed;
        private static bool skip;

        internal static void Begin(BuildReport report)
        {
            currentReport = report ?? throw new ArgumentNullException(nameof(report));
            androidFailure = null;
            androidProcessed = false;
            skip = ShouldSkip(report);
            if (skip) return;

            Run(report, ValidateProjectDestination);
        }

        internal static void Complete(BuildReport report)
        {
            try
            {
                if (skip || ShouldSkip(report)) return;
                if (report.summary.platform == BuildTarget.Android)
                {
                    if (androidFailure != null)
                    {
                        MarkFailed(report, androidFailure);
                        ExceptionDispatchInfo.Capture(androidFailure).Throw();
                    }
                    if (!androidProcessed)
                        Run(report, () => throw new BuildFailedException(
                            "UniText's Android font payload was not added to the Gradle project."));
                    return;
                }

                Run(report, () => Publish(report, GetPlayerDestination(report)));
            }
            finally
            {
                currentReport = null;
                androidFailure = null;
                androidProcessed = false;
                skip = false;
            }
        }

#if UNITY_ANDROID
        internal static void CompleteAndroidProject(string path)
        {
            var report = currentReport;
            if (report == null)
                throw new BuildFailedException(
                    "UniText received an Android Gradle project without its Player build report.");
            if (skip)
            {
                androidProcessed = true;
                return;
            }

            try
            {
                Run(report, () => Publish(report, Path.Combine(path, "src", "main", "assets",
                    UniTextDirectoryName, FontsDirectoryName)));
                androidProcessed = true;
            }
            catch (Exception exception)
            {
                androidFailure = exception;
                throw;
            }
        }
#endif

        private static void ValidateProjectDestination()
        {
            var path = Path.Combine(Application.dataPath, "StreamingAssets",
                UniTextDirectoryName, FontsDirectoryName);
            if (Directory.Exists(path) || File.Exists(path))
                throw new BuildFailedException(
                    $"'{path}' conflicts with UniText's generated font payload destination.");
        }

        private static bool ShouldSkip(BuildReport report)
            => report.summary.platform == BuildTarget.WebGL
               || (report.summary.options
                   & (BuildOptions.BuildScriptsOnly | BuildOptions.PatchPackage)) != 0;

        private static void Publish(BuildReport report, string destination)
        {
            destination = ValidateOwnedDestination(destination);
            var publishedRoot = PublishedRoot(report.summary.platform);
            var describesContent = DescribesPackedContent(report);
            if (!describesContent && Directory.Exists(publishedRoot))
            {
                ApplyPayload(report, destination, publishedRoot, HasCatalog(publishedRoot));
                return;
            }

            var fonts = describesContent
                ? CollectPackedFonts(report)
                : CollectProjectFonts(report);
            var stagingRoot = Path.GetFullPath(Path.Combine("Library", "UniText", "FontBuild",
                "Player", report.summary.platform.ToString()));
            ResetDirectory(stagingRoot);

            var hasPayload = false;
            try
            {
                if (fonts.Count != 0)
                {
                    Directory.CreateDirectory(stagingRoot);
                    UniTextFontContentBuilder.Write(fonts, stagingRoot);
                    hasPayload = Directory.EnumerateFiles(stagingRoot, "*.ufontz",
                        SearchOption.TopDirectoryOnly).Any();
                }

                ApplyPayload(report, destination, stagingRoot, hasPayload);
                ReplaceOwnedDirectory(stagingRoot, publishedRoot, hasPayload);
                Directory.CreateDirectory(publishedRoot);
            }
            finally
            {
                ResetDirectory(stagingRoot);
            }
        }

        private static void ApplyPayload(BuildReport report, string destination, string source,
            bool hasPayload)
        {
            var hadPayload = Directory.Exists(destination) || File.Exists(destination);
            var recordOutputFiles = report.summary.platform != BuildTarget.Android;
            if (hadPayload && recordOutputFiles)
                recordFilesDeletedRecursiveMethod.Invoke(report, new object[] { destination });
            ReplaceOwnedDirectory(source, destination, hasPayload);
            if (hasPayload && recordOutputFiles)
                recordFilesAddedRecursiveMethod.Invoke(report,
                    new object[] { destination, CommonRoles.streamingAsset });
            if ((hadPayload || hasPayload)
                && report.summary.platform == BuildTarget.StandaloneOSX)
                SignMacApplication(destination);
            if (report.summary.platform == BuildTarget.WSAPlayer)
                SyncWsaProjectItems(destination, hasPayload);
        }

        /// <summary>
        /// Directory holding the payload published for the content the incremental pipeline
        /// currently caches, which is the exact payload again whenever that content is reused.
        /// Existing with no catalog means that content needs no payload.
        /// </summary>
        private static string PublishedRoot(BuildTarget platform)
            => Path.GetFullPath(Path.Combine("Library", "UniText", "FontBuild", "Published",
                platform.ToString()));

        private static bool HasCatalog(string directory)
            => File.Exists(Path.Combine(directory, UniTextFontContentBuilder.CatalogFileName));

        /// <summary>
        /// Whether the report enumerates the content the player ships. The incremental pipeline
        /// reuses the previous build's content on its own, not only under
        /// <see cref="BuildOptions.BuildScriptsOnly"/>, and then reports no packed asset contents;
        /// the font payload published for that content is the one that still matches it.
        /// </summary>
        private static bool DescribesPackedContent(BuildReport report)
        {
            var packedAssets = report.packedAssets;
            for (var i = 0; i < packedAssets.Length; i++)
                if (packedAssets[i].contents.Length != 0) return true;
            return false;
        }

        /// <summary>
        /// Every embedded font in the project. The payload is then a superset of what the player
        /// needs: correct for content this build cannot enumerate, and larger than the exact
        /// report-driven payload.
        /// </summary>
        private static List<UniTextFont> CollectProjectFonts(BuildReport report)
        {
            var result = new List<UniTextFont>();
            foreach (var guid in new EmbeddedFontAssetIndex().All)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid.ToString());
                if (string.IsNullOrEmpty(path)) continue;
                var objects = AssetDatabase.LoadAllAssetsAtPath(path);
                for (var i = 0; i < objects.Length; i++)
                    if (objects[i] is UniTextFont font) result.Add(font);
            }

            const string notice =
                "This build reused player content without reporting what it packed, so UniText " +
                "packaged every embedded font in the project instead of only the fonts the player " +
                "ships. A clean build restores the minimal payload.";
            Debug.LogWarning(notice);
            addMessageMethod.Invoke(report, new object[]
            {
                LogType.Warning, notice, typeof(UniTextEmbeddedFontPlayerBuild).FullName
            });
            return result;
        }

        private static List<UniTextFont> CollectPackedFonts(BuildReport report)
        {
            var fontAssets = new EmbeddedFontAssetIndex();
            var assets = new Dictionary<GUID, string>();
            var packedAssets = report.packedAssets;
            for (var i = 0; i < packedAssets.Length; i++)
            {
                var contents = packedAssets[i].contents;
                for (var j = 0; j < contents.Length; j++)
                {
                    var guid = contents[j].sourceAssetGUID;
                    if (guid.Empty() || !fontAssets.Contains(guid)) continue;
                    var path = contents[j].sourceAssetPath;
                    if (string.IsNullOrEmpty(path))
                        throw new BuildFailedException(
                            "The Player build report contains a UniText font without a source asset identity.");
                    path = path.Replace('\\', '/');
                    if (assets.TryGetValue(guid, out var existingPath)
                        && !string.Equals(existingPath, path, StringComparison.Ordinal))
                        throw new BuildFailedException(
                            $"UniText source {guid} is reported at both '{existingPath}' and '{path}'.");
                    assets[guid] = path;
                }
            }

            var result = new List<UniTextFont>(assets.Count);
            foreach (var asset in assets)
            {
                if (asset.Value.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var notice = $"UniText font '{asset.Value}' lives in a Resources folder:"
                                 + " Resources packs its whole file including the payload carrier,"
                                 + " duplicating the compressed font already published to StreamingAssets.";
                    Debug.LogWarning(notice);
                    addMessageMethod.Invoke(report, new object[]
                    {
                        LogType.Warning, notice, typeof(UniTextEmbeddedFontPlayerBuild).FullName
                    });
                }

                UniTextFont found = null;
                var identifiers = ContentBuildInterface.GetPlayerObjectIdentifiersInAsset(
                    asset.Key, report.summary.platform);
                for (var i = 0; i < identifiers.Length; i++)
                {
                    if (ObjectIdentifier.ToObject(identifiers[i]) is not UniTextFont font)
                        continue;
                    if (found != null)
                        throw new BuildFailedException(
                            $"'{asset.Value}' contains multiple UniText fonts. Store every UniTextFont in its own asset so Player payload inclusion remains exact.");
                    found = font;
                }
                if (found == null)
                    throw new BuildFailedException(
                        $"The Player build report identifies '{asset.Value}' as a UniText font, but its source object is unavailable.");
                result.Add(found);
            }
            return result;
        }

        private static string GetPlayerDestination(BuildReport report)
        {
            string bootConfigPath = null;
            var files = report.GetFiles();
            for (var i = 0; i < files.Length; i++)
            {
                if (!string.Equals(files[i].role, CommonRoles.bootConfig,
                        StringComparison.Ordinal)
                    && !string.Equals(Path.GetFileName(files[i].path), "boot.config",
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (bootConfigPath != null
                    && !string.Equals(Path.GetFullPath(bootConfigPath),
                        Path.GetFullPath(files[i].path), StringComparison.OrdinalIgnoreCase))
                    throw new BuildFailedException(
                        "The Player build report contains multiple boot configuration files.");
                bootConfigPath = files[i].path;
            }
            if (string.IsNullOrEmpty(bootConfigPath))
                throw new BuildFailedException(
                    "The Player build report does not contain a boot configuration file.");

            var dataRoot = Path.GetDirectoryName(Path.GetFullPath(bootConfigPath));
            if (string.IsNullOrEmpty(dataRoot))
                throw new BuildFailedException(
                    $"Cannot resolve the Player data directory from '{bootConfigPath}'.");
            var container = report.summary.platform is BuildTarget.iOS or BuildTarget.tvOS
                ? "Raw"
                : report.summary.platform == BuildTarget.WSAPlayer
                  || report.summary.platformGroup == BuildTargetGroup.Standalone
                    ? "StreamingAssets"
                    : throw new BuildFailedException(
                        $"UniText has no generated font payload destination for {report.summary.platform}.");
            return Path.Combine(dataRoot, container, UniTextDirectoryName, FontsDirectoryName);
        }

        private static string ValidateOwnedDestination(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new BuildFailedException("UniText's generated font payload destination is empty.");
            var fullPath = Path.GetFullPath(path);
            var fonts = new DirectoryInfo(fullPath);
            var uniText = fonts.Parent;
            var container = uniText?.Parent;
            if (!string.Equals(fonts.Name, FontsDirectoryName, StringComparison.Ordinal)
                || !string.Equals(uniText?.Name, UniTextDirectoryName, StringComparison.Ordinal)
                || container == null
                || !IsPayloadContainer(container.Name))
                throw new BuildFailedException(
                    $"'{fullPath}' is not a valid UniText-owned Player payload destination.");
            return fullPath;
        }

        private static bool IsPayloadContainer(string name)
            => string.Equals(name, "StreamingAssets", StringComparison.Ordinal)
               || string.Equals(name, "Raw", StringComparison.Ordinal)
               || string.Equals(name, "assets", StringComparison.Ordinal);

        private static void ReplaceOwnedDirectory(string source, string destination, bool hasPayload)
        {
            ResetDirectory(destination);
            if (!hasPayload) return;

            Directory.CreateDirectory(destination);
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            for (var i = 0; i < files.Length; i++)
            {
                var relativePath = files[i].Substring(source.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var target = Path.Combine(destination, relativePath);
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                File.Copy(files[i], target, true);
            }
        }

        private static void ResetDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }

        private static void SignMacApplication(string destination)
        {
            var directory = new DirectoryInfo(destination);
            while (directory != null
                   && !directory.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                directory = directory.Parent;
            if (directory == null)
                throw new BuildFailedException(
                    $"Cannot locate the macOS application bundle containing '{destination}'.");

            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "UnityEditor.OSXStandalone.MacOSCodeSigning", false))
                .FirstOrDefault(value => value != null);
            var method = type?.GetMethod("CodeSignAppBundle",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string) }, null);
            if (method == null)
                throw new MissingMethodException(
                    "UnityEditor.OSXStandalone.MacOSCodeSigning.CodeSignAppBundle is unavailable.");
            try
            {
                method.Invoke(null, new object[] { directory.FullName });
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// Synchronizes the generated UWP data project with the published payload: the generated
        /// project deploys only the files its items list, while a project-less (Executable Only)
        /// layout ships its folder content as-is and needs no registration.
        /// </summary>
        private static void SyncWsaProjectItems(string destination, bool hasPayload)
        {
            var product = new DirectoryInfo(destination).Parent.Parent.Parent.Parent;
            var items = product.GetFiles("*.vcxitems", SearchOption.TopDirectoryOnly);
            if (items.Length > 1)
                throw new BuildFailedException(
                    $"'{product.FullName}' contains multiple .vcxitems files, so the Unity data"
                    + " project holding UniText's font payload items cannot be identified: "
                    + string.Join(", ", items.Select(value => value.Name)));
            if (items.Length == 0)
            {
                if (hasPayload
                    && product.GetFiles("*.vcxproj", SearchOption.TopDirectoryOnly).Length != 0)
                    throw new BuildFailedException(
                        $"'{product.FullName}' is a generated UWP project without a .vcxitems data"
                        + " project to register UniText's font payload in.");
                return;
            }

            var path = items[0].FullName;
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3
                         && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var offset = hasBom ? 3 : 0;
            var text = new UTF8Encoding(false, true)
                .GetString(bytes, offset, bytes.Length - offset);
            var original = text;
            text = RemoveWsaItemGroups(text, path);
            text = RemoveWsaStrayItems(text, path);
            if (hasPayload) text = InsertWsaItemGroup(text, destination, product.FullName, path);
            if (string.Equals(text, original, StringComparison.Ordinal)) return;
            File.WriteAllText(path, text, new UTF8Encoding(hasBom));
        }

        private static string RemoveWsaItemGroups(string text, string path)
        {
            const string open = "<ItemGroup Label=\"UniTextEmbeddedFonts\">";
            const string close = "</ItemGroup>";
            while (true)
            {
                var start = text.IndexOf(open, StringComparison.Ordinal);
                if (start < 0) return text;
                var end = text.IndexOf(close, start + open.Length, StringComparison.Ordinal);
                if (end < 0)
                    throw new BuildFailedException(
                        $"'{path}' contains an unterminated UniText font payload item group.");
                text = RemoveWsaSpan(text, start, end + close.Length);
            }
        }

        /// <summary>
        /// Removes payload items outside UniText's own item group: a payload file listed twice
        /// fails MSBuild packaging on duplicate destinations.
        /// </summary>
        private static string RemoveWsaStrayItems(string text, string path)
        {
            var searchFrom = 0;
            while (true)
            {
                var marker = IndexOfWsaPayloadPath(text, searchFrom);
                if (marker < 0) return text;
                var start = text.LastIndexOf('<', marker);
                if (start < 0 || text[start + 1] == '/' || text[start + 1] == '!'
                    || text[start + 1] == '?')
                {
                    searchFrom = marker + 1;
                    continue;
                }

                var openEnd = text.IndexOf('>', marker);
                if (openEnd < 0)
                    throw new BuildFailedException(
                        $"'{path}' contains an unterminated element referencing UniText's font payload.");
                int end;
                if (text[openEnd - 1] == '/') end = openEnd + 1;
                else
                {
                    var nameEnd = start + 1;
                    while (!char.IsWhiteSpace(text[nameEnd]) && text[nameEnd] != '>') nameEnd++;
                    var closeTag = "</" + text.Substring(start + 1, nameEnd - start - 1) + ">";
                    var closeStart = text.IndexOf(closeTag, openEnd, StringComparison.Ordinal);
                    if (closeStart < 0)
                        throw new BuildFailedException(
                            $"'{path}' contains an unterminated element referencing UniText's font payload.");
                    end = closeStart + closeTag.Length;
                }
                text = RemoveWsaSpan(text, start, end);
                searchFrom = start;
            }
        }

        private static string InsertWsaItemGroup(string text, string destination,
            string productDirectory, string path)
        {
            var files = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var builder = new StringBuilder();
            builder.Append("  <ItemGroup Label=\"UniTextEmbeddedFonts\">").Append(newline);
            for (var i = 0; i < files.Length; i++)
                builder.Append("    <None Include=\"$(MSBuildThisFileDirectory)")
                    .Append(Path.GetRelativePath(productDirectory, files[i])).Append("\">")
                    .Append(newline)
                    .Append("      <DeploymentContent>true</DeploymentContent>").Append(newline)
                    .Append("      <ExcludeFromResourceIndex>true</ExcludeFromResourceIndex>")
                    .Append(newline)
                    .Append("    </None>").Append(newline);
            builder.Append("  </ItemGroup>").Append(newline);

            var anchor = text.LastIndexOf("</Project>", StringComparison.Ordinal);
            if (anchor < 0)
                throw new BuildFailedException(
                    $"'{path}' has no closing Project element to register UniText's font payload in.");
            return text.Insert(anchor, builder.ToString());
        }

        private static string RemoveWsaSpan(string text, int start, int end)
        {
            while (start > 0 && (text[start - 1] == ' ' || text[start - 1] == '\t')) start--;
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            return text.Remove(start, end - start);
        }

        private static int IndexOfWsaPayloadPath(string text, int startIndex)
        {
            var backslash = text.IndexOf(@"StreamingAssets\UniText\Fonts\", startIndex,
                StringComparison.Ordinal);
            var forward = text.IndexOf("StreamingAssets/UniText/Fonts/", startIndex,
                StringComparison.Ordinal);
            if (backslash < 0) return forward;
            return forward < 0 ? backslash : Math.Min(backslash, forward);
        }

        private static void Run(BuildReport report, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                MarkFailed(report, exception);
                throw;
            }
        }

        private static void MarkFailed(BuildReport report, Exception exception)
        {
            setBuildResultMethod.Invoke(report, new object[] { BuildResult.Failed });
            addMessageMethod.Invoke(report, new object[]
            {
                LogType.Error,
                exception.Message,
                exception.GetType().FullName
            });
        }

        private static MethodInfo FindBuildReportMethod(string name, params Type[] parameters)
            => typeof(BuildReport).GetMethod(name,
                   BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                   null, parameters, null)
               ?? throw new MissingMethodException(typeof(BuildReport).FullName, name);
    }

    internal static class UniTextFontContentBuilder
    {
        internal const string CatalogFileName = "catalog.bin";
        private const int CatalogMagic = 0x31465455;
        private const int CatalogVersion = 1;

        internal static void Write(IEnumerable<UniTextFont> fonts, string outputDirectory)
        {
            if (fonts == null) throw new ArgumentNullException(nameof(fonts));
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("Output directory is empty.", nameof(outputDirectory));

            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            var payloads = new EmbeddedFontBuildPayloadSet();
            foreach (var requestedFont in fonts)
                payloads.TryAdd(requestedFont, out _);

            foreach (var source in payloads.Sources.Values)
                WriteFile(Path.Combine(outputDirectory, source.SourceId + ".ufontz"),
                    source.Compressed);

            var catalogPath = Path.Combine(outputDirectory, CatalogFileName);
            byte[] catalog;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(CatalogMagic);
                writer.Write(CatalogVersion);
                writer.Write(payloads.Entries.Count);
                foreach (var entry in payloads.Entries.Values.OrderBy(value => value.Token))
                {
                    writer.Write(entry.Token);
                    writer.Write(entry.RawLength);
                    writer.Write(entry.SourceHash);
                }
                writer.Flush();
                catalog = stream.ToArray();
            }
            WriteFile(catalogPath, catalog);
        }

        private static void WriteFile(string path, byte[] data)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew,
                       FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
            {
                stream.Write(data, 0, data.Length);
                stream.Flush(true);
            }
        }
    }
}
