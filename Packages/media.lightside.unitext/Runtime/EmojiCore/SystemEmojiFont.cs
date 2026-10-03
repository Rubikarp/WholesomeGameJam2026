#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Xml;
#endif

namespace LightSide
{
    /// <summary>
    /// Utility for locating file-addressable system emoji fonts across platforms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Provides automatic file-path detection of color emoji fonts on:
    /// <list type="bullet">
    /// <item>Windows: Segoe UI Emoji (seguiemj.ttf)</item>
    /// <item>macOS: Apple Color Emoji (.ttc)</item>
    /// <item>Android: NotoColorEmoji, Samsung/Huawei vendor fonts (via fonts.xml or API)</item>
    /// <item>Linux: Noto Color Emoji, Symbola</item>
    /// </list>
    /// </para>
    /// <para>On iOS, system emoji is exposed only as an opaque native face and has no font-file path.</para>
    /// <para>
    /// In the Unity Editor, also searches for emoji fonts in the EmojiCore/Editor folder.
    /// </para>
    /// </remarks>
    /// <seealso cref="EmojiFont"/>
    public static class SystemEmojiFont
    {
    #if UNITY_EDITOR
        private static string editorFontPath;
        private static string emojiCoreEditorFolder;
    #endif

        
        /// <summary>Gets the path to the default system emoji font for the current platform.</summary>
        /// <returns>Full path to the emoji font file, or null if not found.</returns>
        /// <remarks>
        /// In Editor, first checks EmojiCore/Editor folder for bundled fonts.
        /// Validates fonts by checking for color glyph support and basic emoji (U+1F600).
        /// On iOS, the system emoji face is available only through the native emoji runtime,
        /// so this method returns null.
        /// </remarks>
        public static string GetDefaultEmojiFont()
            => TryGetDefaultEmojiFont(out var path, out _) ? path : null;

        internal static bool TryGetDefaultEmojiFont(out string path, out int faceIndex)
        {
            path = null;
            faceIndex = 0;

            //USEFUL: path = GetEditorEmojiFont();
    #if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            path = GetWindowsEmojiFont();
    #elif UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
            path = GetMacOSEmojiFont(out faceIndex);
    #elif UNITY_ANDROID
            path = GetAndroidEmojiFont(out faceIndex);
    #elif UNITY_EDITOR_LINUX || UNITY_STANDALONE_LINUX
            path = GetLinuxEmojiFont();
    #endif

            if (path != null && !ValidateEmojiFont(path, faceIndex))
            {
                CatZones.emoji.MeowWarn($"[SystemEmojiFont] Font validation failed: {path}");
                path = null;
                faceIndex = 0;
            }

            return path != null;
        }

    #if UNITY_EDITOR
        private static string GetEditorEmojiFont()
        {
            if (editorFontPath != null)
                return editorFontPath.Length > 0 ? editorFontPath : null;

            if (emojiCoreEditorFolder == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets("SystemEmojiFont t:Script");
                foreach (var guid in guids)
                {
                    var scriptPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (scriptPath.EndsWith("SystemEmojiFont.cs"))
                    {
                        var emojiCoreDir = Path.GetDirectoryName(scriptPath);
                        emojiCoreEditorFolder = Path.Combine(emojiCoreDir, "Editor").Replace("\\", "/");
                        break;
                    }
                }
                emojiCoreEditorFolder ??= "";
            }

            if (string.IsNullOrEmpty(emojiCoreEditorFolder))
            {
                editorFontPath = "";
                return null;
            }

            var searchFolder = new[] { emojiCoreEditorFolder };
            var fontGuids = UnityEditor.AssetDatabase.FindAssets("g-emoji", searchFolder);

            foreach (var guid in fontGuids)
            {
                var assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith(".ttc") || assetPath.EndsWith(".ttf"))
                {
                    editorFontPath = Path.GetFullPath(assetPath);
                    return editorFontPath;
                }
            }

            editorFontPath = "";
            return null;
        }
    #endif

        private static bool ValidateEmojiFont(string path, int faceIndex)
        {
            FileFontSource source;
            try { source = FileFontSource.OpenFile(path); }
            catch { return false; }
            if (!FreeType.LoadFontFromSource(source, faceIndex))
                return false;
            try
            {
                var info = FreeType.GetFaceInfo();

                if (!info.hasColor)
                {
                    CatZones.emoji.MeowWarn($"[SystemEmojiFont] Font has no color support: {path}");
                    return false;
                }

                uint glyphIndex = FreeType.GetGlyphIndex(UnicodeData.GrinningFaceEmoji);
                if (glyphIndex == 0)
                {
                    CatZones.emoji.MeowWarn($"[SystemEmojiFont] Font missing basic emoji U+1F600: {path}");
                    return false;
                }

                return true;
            }
            finally { FreeType.UnloadIfCurrent(source, faceIndex); }
        }

        private static string GetWindowsEmojiFont()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf"),
                @"C:\Windows\Fonts\seguiemj.ttf"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

    #if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        private static string GetMacOSEmojiFont(out int faceIndex)
        {
            faceIndex = 0;
            if (!NativeFontReader.TryResolveSystemEmojiFont(out var source,
                    out var resolvedFaceIndex, out _)
                || source is not FileFontSource fileSource)
                return null;
            faceIndex = resolvedFaceIndex;
            return fileSource.FilePath;
        }
    #endif

        private static string GetLinuxEmojiFont()
        {
            var paths = new[]
            {
                "/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf",
                "/usr/share/fonts/google-noto-emoji/NotoColorEmoji.ttf",
                "/usr/share/fonts/noto-emoji/NotoColorEmoji.ttf",
                "/usr/share/fonts/truetype/ancient-scripts/Symbola_hint.ttf",
                "/usr/share/fonts/TTF/Symbola.ttf"
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static string GetAndroidEmojiFont(out int faceIndex)
        {
            faceIndex = 0;
    #if UNITY_ANDROID && !UNITY_EDITOR
            Span<int> defaultEmoji = stackalloc int[1];
            defaultEmoji[0] = UnicodeData.GrinningFaceEmoji;
            if (TryResolveAndroidEmojiCluster(defaultEmoji, out var match))
            {
                faceIndex = match.faceIndex;
                CatZones.emoji.Meow($"[SystemEmojiFont] AFontMatcher resolved: {match.descriptor}");
                return match.descriptor;
            }

            var fontFromApi = GetEmojiFromSystemFontsApi();
            if (fontFromApi != null)
                return fontFromApi;

            var fontFromXml = ParseAndroidFontsXml();
            if (fontFromXml != null)
                return fontFromXml;
    #endif
            var paths = new[]
            {
                "/system/fonts/SamsungColorEmoji.ttf",
                "/product/fonts/SamsungColorEmoji.ttf",
                "/system/fonts/HuaweiColorEmoji.ttf",
                "/product/fonts/HuaweiColorEmoji.ttf",
                "/system/fonts/NotoColorEmoji.ttf",
                "/product/fonts/NotoColorEmoji.ttf",
                "/system/fonts/NotoColorEmojiLegacy.ttf",
                "/system/fonts/ColorEmoji.ttf",
                "/system/fonts/Emoji.ttf",
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            var scanned = ScanForEmojiFont("/system/fonts/");
    #if UNITY_ANDROID && !UNITY_EDITOR
            if (scanned == null)
                scanned = ScanForEmojiFont("/product/fonts/");
    #endif
            return scanned;
        }

    #if UNITY_ANDROID && !UNITY_EDITOR
        private static bool TryResolveAndroidEmojiCluster(ReadOnlySpan<int> cluster,
            out SystemFontSourceMatch match)
        {
            match = default;
            if (cluster.IsEmpty) return false;
            var key = new SystemFont.CodepointSequenceKey(cluster);
            return AndroidFontBackend.TryResolve(key.GetText(), "und-Zsye", "sans-serif",
                400, false, out match);
        }

        static string GetEmojiFromSystemFontsApi()
        {
            try
            {
                using var versionClass = new AndroidJavaClass("android.os.Build$VERSION");
                int sdkInt = versionClass.GetStatic<int>("SDK_INT");

                if (sdkInt < 29)
                    return null;

                using var systemFontsClass = new AndroidJavaClass("android.graphics.fonts.SystemFonts");
                using var fontSet = systemFontsClass.CallStatic<AndroidJavaObject>("getAvailableFonts");

                if (fontSet == null)
                    return null;

                var fontArray = fontSet.Call<AndroidJavaObject[]>("toArray");
                if (fontArray == null)
                    return null;

                string bestMatch = null;
                int bestPriority = int.MaxValue;

                for (int i = 0; i < fontArray.Length; i++)
                {
                    var font = fontArray[i];
                    if (font == null) continue;

                    try
                    {
                        using var file = font.Call<AndroidJavaObject>("getFile");
                        if (file == null) continue;

                        string path = file.Call<string>("getAbsolutePath");
                        if (string.IsNullOrEmpty(path)) continue;

                        string lowerPath = path.ToLower();

                        if (lowerPath.Contains("samsungcoloremoji") && bestPriority > 1)
                        {
                            bestMatch = path;
                            bestPriority = 1;
                        }
                        else if (lowerPath.Contains("huaweicoloremoji") && bestPriority > 2)
                        {
                            bestMatch = path;
                            bestPriority = 2;
                        }
                        else if (lowerPath.Contains("notocoloremoji") && bestPriority > 3)
                        {
                            bestMatch = path;
                            bestPriority = 3;
                        }
                        else if (lowerPath.Contains("coloremoji") && bestPriority > 4)
                        {
                            bestMatch = path;
                            bestPriority = 4;
                        }
                        else if (lowerPath.Contains("emoji") && bestPriority > 5)
                        {
                            bestMatch = path;
                            bestPriority = 5;
                        }
                    }
                    finally
                    {
                        font.Dispose();
                    }
                }

                return bestMatch;
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string ParseAndroidFontsXml()
        {
            var xmlPaths = new[]
            {
                "/system/etc/font_fallback.xml",
                "/system/etc/fonts.xml",
                "/vendor/etc/fonts.xml"
            };

            var fontDirs = new[] { "/system/fonts/", "/product/fonts/" };

            foreach (var xmlPath in xmlPaths)
            {
                if (!File.Exists(xmlPath)) continue;

                try
                {
                    var xml = new XmlDocument();
                    xml.Load(xmlPath);

                    var familyNodes = xml.SelectNodes("//family[@lang='und-Zsye']");
                    if (familyNodes == null) continue;

                    foreach (XmlNode familyNode in familyNodes)
                    {
                        var ignoreAttr = familyNode.Attributes?["ignore"];
                        if (ignoreAttr != null && ignoreAttr.Value.EqualsIgnoreCase("true"))
                            continue;

                        var fontNode = familyNode.SelectSingleNode("font");
                        if (fontNode == null) continue;

                        var fontName = fontNode.InnerText?.Trim();
                        if (string.IsNullOrEmpty(fontName)) continue;

                        foreach (var fontDir in fontDirs)
                        {
                            var fullPath = Path.Combine(fontDir, fontName);
                            if (File.Exists(fullPath))
                                return fullPath;
                        }
                    }

                    var allFontNodes = xml.SelectNodes("//font");
                    if (allFontNodes != null)
                    {
                        foreach (XmlNode node in allFontNodes)
                        {
                            var fontName = node.InnerText?.Trim();
                            if (string.IsNullOrEmpty(fontName)) continue;

                            var lowerName = fontName.ToLower();
                            if (lowerName.Contains("emoji") && lowerName.Contains("color"))
                            {
                                foreach (var fontDir in fontDirs)
                                {
                                    var fullPath = Path.Combine(fontDir, fontName);
                                    if (File.Exists(fullPath))
                                        return fullPath;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    CatZones.emoji.MeowWarn($"[SystemEmojiFont] Failed to parse {xmlPath}: {e.Message}");
                }
            }

            return null;
        }
    #endif

        private static string ScanForEmojiFont(string directory)
        {
            if (!Directory.Exists(directory)) return null;

            try
            {
                var files = Directory.GetFiles(directory, "*.ttf");

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file).ToLower();
                    if (name.Contains("coloremoji")) return file;
                }

                foreach (var file in files)
                {
                    var name = Path.GetFileName(file).ToLower();
                    if (name.Contains("emoji")) return file;
                }
            }
            catch (Exception e)
            {
                CatZones.emoji.MeowWarn($"Failed to scan {directory}: {e.Message}");
            }

            return null;
        }
    }
}
#endif
