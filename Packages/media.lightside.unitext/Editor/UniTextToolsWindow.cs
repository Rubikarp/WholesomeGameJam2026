using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal partial class UniTextToolsWindow : EditorWindow
    {
        private enum Tab
        {
            CreateAsset,
            Subsetter,
            DictionaryBuilder
        }

        /// <summary>Opens the UniText asset preparation tools.</summary>
        [MenuItem(UniTextMenu.Tools.Window)]
        public static void ShowWindow()
        {
            var window = GetWindow<UniTextToolsWindow>("UniText Tools");
            window.minSize = new Vector2(450, 550);
        }

        private Tab currentTab = Tab.CreateAsset;
        private static readonly string[] tabLabels = { "Create Font Asset", "Font Subsetter", "Dictionary Builder" };

        /// <summary>Builds the retained-mode font and dictionary tools.</summary>
        public void CreateGUI()
        {
            CreateToolkitGUI();
        }

        private static void CopyAllCharacters(byte[] fontData, int faceIndex)
        {
            var codepoints = FontSubsetter.GetCodepoints(fontData, faceIndex);
            if (codepoints == null || codepoints.Length == 0)
            {
                Debug.LogWarning("FontTools: No codepoints found in font.");
                return;
            }

            var sb = new StringBuilder(codepoints.Length);
            for (int i = 0; i < codepoints.Length; i++)
            {
                var cp = (int)codepoints[i];
                if (!UnicodeData.IsC0ControlOrDelete(cp) && cp <= UnicodeData.MaxCodepoint)
                    sb.Append(char.ConvertFromUtf32(cp));
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log($"Copied <b>{codepoints.Length}</b> codepoints to clipboard.");
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1024 * 1024 => $"{bytes / (1024f * 1024f):F2} MB",
            >= 1024 => $"{bytes / 1024f:F1} KB",
            _ => $"{bytes} bytes"
        };


        private struct FileRowInfo
        {
            public string name;
            public long size;
            public bool pinned;
        }

        private static void TryLoadFontFromPath(string path, out byte[] bytes, out string name, out long size)
        {
            bytes = null;
            name = null;
            size = 0;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".ttf" or ".otf" or ".ttc" or ".otc"))
                return;

            try
            {
                bytes = File.ReadAllBytes(path);
                name = Path.GetFileName(path);
                size = bytes.Length;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load font: {e.Message}");
            }
        }

        private static bool IsValidFontData(byte[] data)
        {
            if (data == null || data.Length < 12)
                return false;

            uint magic = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
            return magic is
                0x00010000 or 0x74727565 or 0x4F54544F or 0x74746366;
        }



        private class FontSource
        {
            public UnityEngine.Object asset;
            public string path = "";
            public byte[] bytes;
            public string name;
            public long size;

            public bool HasData => bytes != null && bytes.Length > 0;

            public void LoadFromAsset()
            {
                bytes = null;
                name = null;
                size = 0;

                if (asset == null) return;

                if (asset is UniTextFont uniFont && uniFont.HasFontData)
                {
                    bytes = uniFont.FontData;
                    name = uniFont.name;
                    size = bytes.Length;
                    return;
                }

                var assetPath = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(assetPath))
                    TryLoadFontFromPath(Path.GetFullPath(assetPath), out bytes, out name, out size);
            }

            public void LoadFromPath()
            {
                TryLoadFontFromPath(path, out bytes, out name, out size);
            }
        }

        private readonly FontSource subsetSource = new();


        private const string PrefCreateSave = "UniText_CreateAsset_SaveDir";
        private const string PrefSubsetBrowse = "UniText_Subsetter_BrowseDir";
        private const string PrefSubsetSave = "UniText_Subsetter_SaveDir";

        private static string GetPrefDir(string key) => EditorPrefs.GetString(key, "");

        private static void SavePrefDir(string key, string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
                EditorPrefs.SetString(key, Path.GetDirectoryName(filePath));
        }

        private static bool IsValidFontObject(UnityEngine.Object obj)
        {
            if (obj is UniTextFont || obj is Font) return true;
            var path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".ttf" or ".otf" or ".ttc" or ".otc";
        }



        private readonly List<BatchEntry> batchEntries = new();
        private bool isCreating;
        private const string PrefBrowseDir = "UniText_CreateAsset_BrowseDir";

        private class BatchEntry
        {
            public string name;
            public long size;
            public byte[] bytes;
            public string assetPath;
            public Font sourceFont;
            public bool fromSelection;
        }

        private void OnSelectionChange()
        {
            if (isCreating || currentTab != Tab.CreateAsset) return;

            for (int i = batchEntries.Count - 1; i >= 0; i--)
                if (batchEntries[i].fromSelection)
                    batchEntries.RemoveAt(i);

            foreach (var obj in Selection.objects)
            {
                if (obj == null) continue;
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                    TryAddFont(path, obj is Font f ? f : null, true);
            }

            RenderToolkitTools();
        }


        private void BrowseFiles()
        {
            var paths = NativeFileDialog.OpenFiles("Select Font Files", "ttf,otf,ttc,otc", GetPrefDir(PrefBrowseDir));
            if (paths == null || paths.Length == 0) return;

            SavePrefDir(PrefBrowseDir, paths[0]);

            foreach (var path in paths)
                TryAddFont(path, null, false);

            RenderToolkitTools();
        }

        private void TryAddFont(string path, Font sourceFont, bool fromSelection)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".ttf" or ".otf" or ".ttc" or ".otc")) return;

            var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            if (!File.Exists(fullPath)) return;

            var name = Path.GetFileName(path);
            for (int i = 0; i < batchEntries.Count; i++)
                if (batchEntries[i].name == name) return;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read font '{fullPath}': {exception.Message}");
                return;
            }

            if (!IsValidFontData(bytes)) return;

            batchEntries.Add(new BatchEntry
            {
                name = name,
                size = bytes.Length,
                bytes = bytes,
                assetPath = !Path.IsPathRooted(path) ? path : null,
                sourceFont = sourceFont,
                fromSelection = fromSelection
            });
        }

        private void CreateBatchAssets()
        {
            if (batchEntries.Count == 0) return;

            isCreating = true;
            try
            {
                string externalFolder = null;
                bool hasExternal = false;
                for (int i = 0; i < batchEntries.Count; i++)
                    if (batchEntries[i].assetPath == null) { hasExternal = true; break; }

                if (hasExternal)
                {
                    externalFolder = EditorUtility.SaveFolderPanel(
                        "Save External Font Assets To",
                        GetPrefDir(PrefCreateSave).Length > 0 ? GetPrefDir(PrefCreateSave) : Application.dataPath,
                        "");
                    if (string.IsNullOrEmpty(externalFolder)) return;
                    if (!externalFolder.StartsWith(Application.dataPath))
                    {
                        Debug.LogError("UniText Font Assets must be saved inside the Assets folder.");
                        return;
                    }
                    externalFolder = "Assets" + externalFolder.Substring(Application.dataPath.Length);
                    SavePrefDir(PrefCreateSave, externalFolder + Path.DirectorySeparatorChar);
                }

                var created = new List<UnityEngine.Object>();

                for (int i = 0; i < batchEntries.Count; i++)
                {
                    var entry = batchEntries[i];
                    var baseName = Path.GetFileNameWithoutExtension(entry.name) + ".asset";

                    string savePath;
                    if (entry.assetPath != null)
                    {
                        var dir = Path.GetDirectoryName(entry.assetPath);
                        savePath = Path.Combine(dir, baseName).Replace("\\", "/");
                    }
                    else
                    {
                        savePath = Path.Combine(externalFolder, baseName).Replace("\\", "/");
                    }

                    savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

                    var fontAsset = UniTextFont.CreateFontAsset(entry.bytes);
                    if (fontAsset == null)
                    {
                        Debug.LogError($"Failed to create font asset from {entry.name}");
                        continue;
                    }

                    if (entry.sourceFont != null)
                        fontAsset.sourceFont = entry.sourceFont;

                    UniTextFontAssetPersistence.Create(fontAsset, savePath);
                    created.Add(fontAsset);
                }

                if (created.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                    Selection.objects = created.ToArray();
                    EditorGUIUtility.PingObject(created[^1]);
                    Debug.Log($"Created {created.Count} UniText Font Asset(s)");
                    batchEntries.Clear();
                }
            }
            finally
            {
                isCreating = false;
            }
        }



        private enum SubsetMode { Remove, Keep }

        [Flags]
        private enum CharacterSet
        {
            None = 0,

            BasicLatin      = 1 << 0,
            LatinExtended   = 1 << 1,
            Vietnamese      = 1 << 2,

            Cyrillic        = 1 << 3,
            Greek           = 1 << 4,
            Arabic          = 1 << 5,
            Hebrew          = 1 << 6,
            Thai            = 1 << 7,
            Hiragana        = 1 << 8,
            Katakana        = 1 << 9,

            Digits          = 1 << 10,
            Punctuation     = 1 << 11,
            Currency        = 1 << 12,
            Math            = 1 << 13,
            Arrows          = 1 << 14,
            BoxDrawing      = 1 << 15,

            Devanagari      = 1 << 16,
            Bengali         = 1 << 17,
            Tamil           = 1 << 18,
            Telugu          = 1 << 19,
            Kannada         = 1 << 20,
            Malayalam       = 1 << 21,
            Gujarati        = 1 << 22,
            Gurmukhi        = 1 << 23,
            Sinhala         = 1 << 24,
            Myanmar         = 1 << 25,
            Khmer           = 1 << 26,
            Lao             = 1 << 27,
            Georgian        = 1 << 28,
            Armenian        = 1 << 29,
            Tibetan         = 1 << 30,
        }

        private SubsetMode subsetMode = SubsetMode.Remove;
        private string subsetInputText = "";
        private CharacterSet selectedSets;

        private int subsetFaceIndex;
        private string[] subsetFaceLabels;
        private byte[] subsetFaceLabelsFor;

        private readonly HashSet<int> collectedCodepoints = new();
        private int removeCodepointCount;
        private int removeCompositionCount;
        private string subsetInputError;


        private static readonly (string label, CharacterSet[] sets)[] scriptTableRows =
        {
            ("Latin",    new[] { CharacterSet.BasicLatin, CharacterSet.LatinExtended, CharacterSet.Vietnamese }),
            ("European", new[] { CharacterSet.Cyrillic, CharacterSet.Greek, CharacterSet.Armenian, CharacterSet.Georgian }),
            ("Semitic",  new[] { CharacterSet.Arabic, CharacterSet.Hebrew }),
            ("N. Indic", new[] { CharacterSet.Devanagari, CharacterSet.Bengali, CharacterSet.Gujarati, CharacterSet.Gurmukhi }),
            ("S. Indic", new[] { CharacterSet.Tamil, CharacterSet.Telugu, CharacterSet.Kannada, CharacterSet.Malayalam }),
            ("SE Asian", new[] { CharacterSet.Thai, CharacterSet.Lao, CharacterSet.Myanmar, CharacterSet.Khmer }),
            ("E. Asian", new[] { CharacterSet.Hiragana, CharacterSet.Katakana }),
            ("Other",    new[] { CharacterSet.Sinhala, CharacterSet.Tibetan }),
            ("Symbols",  new[] { CharacterSet.Digits, CharacterSet.Punctuation, CharacterSet.Currency,
                CharacterSet.Math, CharacterSet.Arrows, CharacterSet.BoxDrawing }),
        };

        private static string FormatSetName(CharacterSet set) => set switch
        {
            CharacterSet.BasicLatin => "Basic",
            CharacterSet.LatinExtended => "Extended",
            CharacterSet.Vietnamese => "Vietnamese",
            CharacterSet.BoxDrawing => "Box Drawing",
            _ => set.ToString()
        };

        private bool HasSubsetInput => subsetMode == SubsetMode.Keep
            ? collectedCodepoints.Count > 0
            : selectedSets != CharacterSet.None || subsetInputText.Length > 0;


        private bool CollectCodepoints()
        {
            collectedCodepoints.Clear();
            removeCodepointCount = 0;
            removeCompositionCount = 0;
            subsetInputError = null;

            var target = subsetMode == SubsetMode.Keep
                ? collectedCodepoints
                : new HashSet<int>();
            if (!TryParseCustomTextAsCodepoints(subsetInputText, target, out var invalidIndex))
            {
                subsetInputError = InvalidSubsetTextMessage(invalidIndex);
                return false;
            }
            AddSelectedRanges(target);

            if (subsetMode == SubsetMode.Keep) return true;

            removeCodepointCount = target.Count;
            var clusters = ParseTextIntoClusters(subsetInputText);
            for (int i = 0; i < clusters.Count; i++)
                if (clusters[i].Length > 1) removeCompositionCount++;

            return true;
        }

        private static bool TryParseCustomTextAsCodepoints(
            string text, HashSet<int> target, out int invalidIndex)
        {
            for (int i = 0; i < text.Length; i++)
            {
                var codepoint = Utf16.DecodeAt(text, i, out var size);
                if (!Utf16.IsUnicodeScalar(codepoint))
                {
                    invalidIndex = i;
                    return false;
                }
                target.Add(codepoint);
                i += size - 1;
            }

            invalidIndex = -1;
            return true;
        }

        private static string InvalidSubsetTextMessage(int index)
            => $"Custom text contains an unpaired UTF-16 surrogate at index {index}.";

        private static List<int[]> ParseTextIntoClusters(string text)
        {
            var result = new List<int[]>();
            if (string.IsNullOrEmpty(text))
                return result;

            var codepoints = new List<int>();
            for (int i = 0; i < text.Length; i++)
            {
                codepoints.Add((int)UnicodeData.DecodeAt(text, i, out var size));
                i += size - 1;
            }

            if (codepoints.Count == 0)
                return result;

            var provider = UnicodeData.Provider;
            var breaker = new GraphemeBreaker(provider);
            var cpArray = codepoints.ToArray();
            var breaks = new bool[cpArray.Length + 1];
            breaker.GetBreakOpportunities(cpArray, breaks);

            int clusterStart = 0;
            for (int i = 1; i <= cpArray.Length; i++)
            {
                if (breaks[i])
                {
                    int len = i - clusterStart;
                    var cluster = new int[len];
                    Array.Copy(cpArray, clusterStart, cluster, 0, len);
                    result.Add(cluster);
                    clusterStart = i;
                }
            }

            return result;
        }

        private void AddSelectedRanges(HashSet<int> target)
        {
            if (Has(CharacterSet.BasicLatin))     AddRangeTo(target, 0x0020, 0x007E);
            if (Has(CharacterSet.LatinExtended))  { AddRangeTo(target, 0x00A0, 0x00FF); AddRangeTo(target, 0x0100, 0x017F); AddRangeTo(target, 0x0180, 0x024F); }
            if (Has(CharacterSet.Vietnamese))     { AddRangeTo(target, 0x1EA0, 0x1EF9); AddRangeTo(target, 0x0300, 0x0303); AddRangeTo(target, 0x0306, 0x0323); }

            if (Has(CharacterSet.Cyrillic))       AddRangeTo(target, 0x0400, 0x04FF);
            if (Has(CharacterSet.Greek))          AddRangeTo(target, 0x0370, 0x03FF);
            if (Has(CharacterSet.Armenian))       AddRangeTo(target, 0x0530, 0x058F);
            if (Has(CharacterSet.Georgian))       { AddRangeTo(target, 0x10A0, 0x10FF); AddRangeTo(target, 0x2D00, 0x2D2F); AddRangeTo(target, 0x1C90, 0x1CBF); }

            if (Has(CharacterSet.Arabic))         { AddRangeTo(target, 0x0600, 0x06FF); AddRangeTo(target, 0x0750, 0x077F); }
            if (Has(CharacterSet.Hebrew))         AddRangeTo(target, 0x0590, 0x05FF);

            if (Has(CharacterSet.Devanagari))     { AddRangeTo(target, 0x0900, 0x097F); AddRangeTo(target, 0xA8E0, 0xA8FF); AddRangeTo(target, 0x1CD0, 0x1CFF); }
            if (Has(CharacterSet.Bengali))        AddRangeTo(target, 0x0980, 0x09FF);
            if (Has(CharacterSet.Gujarati))       AddRangeTo(target, 0x0A80, 0x0AFF);
            if (Has(CharacterSet.Gurmukhi))       AddRangeTo(target, 0x0A00, 0x0A7F);
            if (Has(CharacterSet.Tamil))          AddRangeTo(target, 0x0B80, 0x0BFF);
            if (Has(CharacterSet.Telugu))         AddRangeTo(target, 0x0C00, 0x0C7F);
            if (Has(CharacterSet.Kannada))        AddRangeTo(target, 0x0C80, 0x0CFF);
            if (Has(CharacterSet.Malayalam))       AddRangeTo(target, 0x0D00, 0x0D7F);
            if (Has(CharacterSet.Sinhala))        AddRangeTo(target, 0x0D80, 0x0DFF);

            if (Has(CharacterSet.Thai))           AddRangeTo(target, 0x0E00, 0x0E7F);
            if (Has(CharacterSet.Lao))            AddRangeTo(target, 0x0E80, 0x0EFF);
            if (Has(CharacterSet.Myanmar))        { AddRangeTo(target, 0x1000, 0x109F); AddRangeTo(target, 0xAA60, 0xAA7F); AddRangeTo(target, 0xA9E0, 0xA9FF); }
            if (Has(CharacterSet.Khmer))          { AddRangeTo(target, 0x1780, 0x17FF); AddRangeTo(target, 0x19E0, 0x19FF); }

            if (Has(CharacterSet.Hiragana))       AddRangeTo(target, 0x3040, 0x309F);
            if (Has(CharacterSet.Katakana))       { AddRangeTo(target, 0x30A0, 0x30FF); AddRangeTo(target, 0x31F0, 0x31FF); }

            if (Has(CharacterSet.Tibetan))        AddRangeTo(target, 0x0F00, 0x0FFF);

            if (Has(CharacterSet.Digits))         AddRangeTo(target, 0x0030, 0x0039);
            if (Has(CharacterSet.Punctuation))    { AddRangeTo(target, 0x0021, 0x002F); AddRangeTo(target, 0x003A, 0x0040); AddRangeTo(target, 0x005B, 0x0060); AddRangeTo(target, 0x007B, 0x007E); AddRangeTo(target, 0x2000, 0x206F); }
            if (Has(CharacterSet.Currency))       { AddRangeTo(target, 0x20A0, 0x20CF); AddCodepointsTo(target, 0x24, 0xA2, 0xA3, 0xA4, 0xA5); }
            if (Has(CharacterSet.Math))           { AddRangeTo(target, 0x2200, 0x22FF); AddRangeTo(target, 0x2070, 0x209F); AddCodepointsTo(target, 0xB1, 0xD7, 0xF7); }
            if (Has(CharacterSet.Arrows))         { AddRangeTo(target, 0x2190, 0x21FF); AddRangeTo(target, 0x27F0, 0x27FF); }
            if (Has(CharacterSet.BoxDrawing))     { AddRangeTo(target, 0x2500, 0x257F); AddRangeTo(target, 0x2580, 0x259F); }
        }

        private bool Has(CharacterSet set) => (selectedSets & set) != 0;

        private static void AddRangeTo(HashSet<int> set, int start, int end)
        {
            for (int i = start; i <= end; i++)
                set.Add(i);
        }

        private static void AddCodepointsTo(HashSet<int> set, params int[] cps)
        {
            foreach (int cp in cps)
                set.Add(cp);
        }



        private byte[] BuildSubsetBytes()
            => subsetMode == SubsetMode.Keep ? CreateKeepSubset() : CreateRemoveSubset();

        private void CreateSubsetFile()
        {
            if (!subsetSource.HasData || !HasSubsetInput || subsetInputError != null)
                return;

            string defaultName = string.IsNullOrEmpty(subsetSource.name)
                ? "subset.ttf"
                : Path.GetFileNameWithoutExtension(subsetSource.name) + "_subset.ttf";

            var subsetSaveDir = GetPrefDir(PrefSubsetSave);
            if (string.IsNullOrEmpty(subsetSaveDir))
                subsetSaveDir = Application.dataPath;

            string savePath = EditorUtility.SaveFilePanel("Save Subset Font", subsetSaveDir, defaultName, "ttf");
            if (string.IsNullOrEmpty(savePath))
                return;

            SavePrefDir(PrefSubsetSave, savePath);

            try
            {
                var subsetBytes = BuildSubsetBytes();
                if (subsetBytes == null || subsetBytes.Length == 0)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create subset font.", "OK");
                    return;
                }

                File.WriteAllBytes(savePath, subsetBytes);
                LogSubsetResult(subsetBytes.Length, savePath);
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create subset: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        private void CreateSubsetAsset()
        {
            if (!subsetSource.HasData || !HasSubsetInput || subsetInputError != null)
                return;

            string defaultName = string.IsNullOrEmpty(subsetSource.name)
                ? "subset"
                : Path.GetFileNameWithoutExtension(subsetSource.name) + "_subset";

            string savePath = EditorUtility.SaveFilePanelInProject(
                "Save Subset UniText Font", defaultName, "asset",
                "Choose where to save the UniText Font asset.");
            if (string.IsNullOrEmpty(savePath))
                return;

            try
            {
                var subsetBytes = BuildSubsetBytes();
                if (subsetBytes == null || subsetBytes.Length == 0)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create subset font.", "OK");
                    return;
                }

                var fontAsset = UniTextFont.CreateFontAsset(subsetBytes);
                if (fontAsset == null)
                {
                    EditorUtility.DisplayDialog("Error", "Failed to create UniText Font from the subset.", "OK");
                    return;
                }

                UniTextFontAssetPersistence.Create(fontAsset, savePath);
                AssetDatabase.SaveAssets();
                Selection.activeObject = fontAsset;
                EditorGUIUtility.PingObject(fontAsset);
                LogSubsetResult(subsetBytes.Length, savePath);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to create subset: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        private void LogSubsetResult(int subsetSize, string path)
        {
            long reduction = 100 - (subsetSize * 100L / subsetSource.size);
            Debug.Log($"Subset created!\n" +
                      $"Original: <b>{FormatSize(subsetSource.size)}</b>\n" +
                      $"Subset: <b>{FormatSize(subsetSize)}</b>\n" +
                      $"Reduction: <b>{reduction}%</b>\n" +
                      $"Path: {path}");
        }

        /// <summary>
        /// Keep mode: single-pass codepoint-based subset.
        /// GSUB closure automatically includes all needed composed glyphs.
        /// </summary>
        private byte[] CreateKeepSubset()
        {
            return FontSubsetter.Subset(subsetSource.bytes, collectedCodepoints.ToList(), subsetFaceIndex);
        }

        /// <summary>
        /// Remove mode: two-pass subset.
        /// Pass 1: Remove codepoints (scripts + non-composition custom text) with GSUB closure.
        /// Pass 2: Remove composition glyphs (unique to multi-codepoint clusters) without GSUB closure.
        /// Classification uses shape-comparison: shape(cluster) vs shape(each codepoint individually).
        /// Glyphs unique to the cluster = composition. No hardcoded character lists.
        /// For a .ttc/.otc, the chosen face drives pass 1; its output is a standalone single-face
        /// font, so pass 2 runs on face 0.
        /// </summary>
        private byte[] CreateRemoveSubset()
        {
            var fontData = subsetSource.bytes;
            int curFace = subsetFaceIndex;

            var codepointsToRemove = new HashSet<int>();
            AddSelectedRanges(codepointsToRemove);

            var compositionClusters = new List<int[]>();
            var clusters = ParseTextIntoClusters(subsetInputText);

            foreach (var cluster in clusters)
                ClassifyCluster(fontData, cluster, codepointsToRemove, compositionClusters, curFace);

            if (codepointsToRemove.Count > 0)
            {
                fontData = FontSubsetter.RemoveCodepoints(fontData, codepointsToRemove, curFace);
                if (fontData == null || fontData.Length == 0)
                    return null;
                curFace = 0;
            }

            if (compositionClusters.Count > 0)
            {
                var glyphsToRemove = new HashSet<uint>();

                foreach (var cluster in compositionClusters)
                {
                    var unique = FindCompositionGlyphs(fontData, cluster, curFace);
                    if (unique != null)
                        glyphsToRemove.UnionWith(unique);
                }

                if (glyphsToRemove.Count > 0)
                {
                    var arr = new uint[glyphsToRemove.Count];
                    glyphsToRemove.CopyTo(arr);

                    fontData = FontSubsetter.RemoveGlyphs(fontData, arr, curFace);
                    if (fontData == null || fontData.Length == 0)
                        return null;
                }
            }

            return fontData;
        }

        /// <summary>
        /// Classifies a grapheme cluster by comparing shape(cluster) vs shape(each codepoint).
        /// If the cluster produces glyphs not found in any individual codepoint — it's a composition.
        /// Otherwise, the visible codepoints go to codepoint removal.
        /// </summary>
        private static void ClassifyCluster(byte[] fontData, int[] cluster,
            HashSet<int> codepointsToRemove, List<int[]> compositionClusters, int faceIndex)
        {
            var clusterGlyphs = ShapeToGlyphSet(fontData, cluster, faceIndex);

            var componentGlyphs = new HashSet<uint>();
            var visibleCodepoints = new List<int>();

            for (int i = 0; i < cluster.Length; i++)
            {
                var gs = FontSubsetter.ShapeText(fontData, new[] { cluster[i] }, faceIndex);
                if (gs == null) continue;
                bool visible = false;
                for (int j = 0; j < gs.Length; j++)
                {
                    if (gs[j] != 0) { componentGlyphs.Add(gs[j]); visible = true; }
                }
                if (visible) visibleCodepoints.Add(cluster[i]);
            }

            bool isComposition = false;
            foreach (var g in clusterGlyphs)
            {
                if (!componentGlyphs.Contains(g)) { isComposition = true; break; }
            }

            if (isComposition)
                compositionClusters.Add(cluster);
            else
                for (int i = 0; i < visibleCodepoints.Count; i++)
                    codepointsToRemove.Add(visibleCodepoints[i]);
        }

        private static HashSet<uint> FindCompositionGlyphs(byte[] fontData, int[] cluster, int faceIndex)
        {
            var clusterGlyphs = ShapeToGlyphSet(fontData, cluster, faceIndex);

            var componentGlyphs = new HashSet<uint>();
            for (int i = 0; i < cluster.Length; i++)
            {
                var gs = FontSubsetter.ShapeText(fontData, new[] { cluster[i] }, faceIndex);
                if (gs != null)
                    for (int j = 0; j < gs.Length; j++)
                        if (gs[j] != 0) componentGlyphs.Add(gs[j]);
            }

            clusterGlyphs.ExceptWith(componentGlyphs);
            return clusterGlyphs.Count > 0 ? clusterGlyphs : null;
        }

        private static HashSet<uint> ShapeToGlyphSet(byte[] fontData, int[] codepoints, int faceIndex)
        {
            var result = new HashSet<uint>();
            var gs = FontSubsetter.ShapeText(fontData, codepoints, faceIndex);
            if (gs != null)
                for (int i = 0; i < gs.Length; i++)
                    if (gs[i] != 0) result.Add(gs[i]);
            return result;
        }





        private struct DictFileEntry
        {
            public string name;
            public string fullPath;
            public long size;
            public UnicodeScript script;
            public string error;
        }

        private struct DictBuildEntry
        {
            public UnicodeScript script;
            public int wordCount;
            public byte[] trieData;
            public string path;
        }

        private const string PrefDictBrowseDir = "UniText_DictBuilder_BrowseDir";
        private const string PrefDictSaveDir = "UniText_DictBuilder_SaveDir";
        private readonly List<DictFileEntry> dictFiles = new();
        private string dictStatus;
        private string dictError;

        private void BrowseDictFiles()
        {
            var paths = NativeFileDialog.OpenFiles("Select Word List Files", "txt", GetPrefDir(PrefDictBrowseDir));
            if (paths == null || paths.Length == 0) return;

            SavePrefDir(PrefDictBrowseDir, paths[0]);

            foreach (var path in paths)
                TryAddDictFile(path);

            RenderToolkitTools();
        }

        private void TryAddDictFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".txt") return;

            var fullPath = Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
            if (!File.Exists(fullPath)) return;

            for (int i = 0; i < dictFiles.Count; i++)
                if (dictFiles[i].fullPath == fullPath) return;

            var entry = new DictFileEntry
            {
                name = Path.GetFileName(fullPath),
                fullPath = fullPath,
                size = new FileInfo(fullPath).Length
            };
            ValidateDictionaryFile(ref entry);
            dictFiles.Add(entry);
            dictStatus = null;
            UpdateDictionaryError();
        }

        private void BuildDictionaryAssets()
        {
            dictStatus = null;
            try
            {
                if (!TryReadDictionaryGroups(out var groups))
                {
                    EditorUtility.DisplayDialog("Invalid Word Lists", dictError, "OK");
                    return;
                }

                var scripts = groups.Keys.OrderBy(script => script).ToList();
                var savePaths = SelectDictionarySavePaths(scripts);
                if (savePaths == null) return;
                if (scripts.Count > 1)
                {
                    var replacements = savePaths
                        .Where(path => AssetDatabase.LoadAssetAtPath<WordSegmentationDictionary>(path) != null)
                        .ToArray();
                    if (replacements.Length > 0 && !EditorUtility.DisplayDialog(
                            "Replace Dictionary Assets",
                            "The following dictionary assets already exist and will be replaced:\n\n" +
                            string.Join("\n", replacements),
                            "Replace", "Cancel"))
                        return;
                }

                var builds = new List<DictBuildEntry>(scripts.Count);
                for (var i = 0; i < scripts.Count; i++)
                {
                    var script = scripts[i];
                    var words = groups[script];
                    var trieData = WordSegmentationDictionaryCompiler.Compile(words, out var detectedScript);
                    if (detectedScript != script)
                        throw new InvalidOperationException(
                            $"Dictionary script changed from {script} to {detectedScript} during compilation.");

                    var existing = AssetDatabase.LoadMainAssetAtPath(savePaths[i]);
                    if (existing != null && existing is not WordSegmentationDictionary)
                        throw new InvalidOperationException(
                            $"'{savePaths[i]}' already contains a {existing.GetType().Name} asset.");

                    builds.Add(new DictBuildEntry
                    {
                        script = script,
                        wordCount = words.Count,
                        trieData = trieData,
                        path = savePaths[i]
                    });
                }

                foreach (var build in builds)
                    SaveDictionaryAsset(build);

                AssetDatabase.SaveAssets();

                var summary = string.Join("\n", builds.Select(build =>
                    $"{DisplayDictionaryScript(build.script)}: {build.wordCount:N0} entries, " +
                    $"{build.trieData.Length:N0} bytes"));
                dictStatus = $"Built {builds.Count:N0} dictionary asset(s):\n{summary}";
                dictError = null;

                Debug.Log($"UniText Dictionary Builder: {dictStatus}\n" +
                          string.Join("\n", builds.Select(build => build.path)));
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"Failed to build dictionaries: {e.Message}", "OK");
                Debug.LogException(e);
            }
        }

        private void RefreshDictionaryFiles()
        {
            TryReadDictionaryGroups(out _);
            dictStatus = null;
            RenderToolkitTools();
        }

        private bool TryReadDictionaryGroups(
            out Dictionary<UnicodeScript, HashSet<string>> groups)
        {
            groups = new Dictionary<UnicodeScript, HashSet<string>>();
            for (var i = 0; i < dictFiles.Count; i++)
            {
                var entry = dictFiles[i];
                try
                {
                    var words = ReadDictionaryFile(entry.fullPath);
                    entry.script = WordSegmentationDictionaryCompiler.DetectScript(words);
                    entry.size = new FileInfo(entry.fullPath).Length;
                    entry.error = null;

                    if (!groups.TryGetValue(entry.script, out var group))
                    {
                        group = new HashSet<string>(StringComparer.Ordinal);
                        groups.Add(entry.script, group);
                    }

                    group.UnionWith(words);
                }
                catch (Exception exception)
                {
                    entry.script = UnicodeScript.Unknown;
                    entry.error = exception.Message;
                }

                dictFiles[i] = entry;
            }

            UpdateDictionaryError();
            return string.IsNullOrEmpty(dictError) && groups.Count > 0;
        }

        private static HashSet<string> ReadDictionaryFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Word list file was not found.", path);

            var words = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                var value = line.Trim();
                if (value.Length > 0 && value[0] != '#')
                    words.Add(value);
            }

            return words;
        }

        private static void ValidateDictionaryFile(ref DictFileEntry entry)
        {
            try
            {
                entry.script = WordSegmentationDictionaryCompiler.DetectScript(
                    ReadDictionaryFile(entry.fullPath));
                entry.error = null;
            }
            catch (Exception exception)
            {
                entry.script = UnicodeScript.Unknown;
                entry.error = exception.Message;
            }
        }

        private void UpdateDictionaryError()
        {
            var errors = new StringBuilder();
            foreach (var entry in dictFiles)
            {
                if (string.IsNullOrEmpty(entry.error)) continue;
                if (errors.Length > 0) errors.AppendLine();
                errors.Append(entry.name).Append(": ").Append(entry.error);
            }

            dictError = errors.Length > 0 ? errors.ToString() : null;
        }

        private string GetDictionaryOutputSummary()
        {
            var scripts = dictFiles
                .Where(entry => string.IsNullOrEmpty(entry.error))
                .Select(entry => entry.script)
                .Distinct()
                .OrderBy(script => script)
                .Select(GetDictionaryAssetName)
                .ToArray();
            return scripts.Length == 0 ? "None" : string.Join(", ", scripts);
        }

        private static string DisplayDictionaryScript(UnicodeScript script)
            => script == UnicodeScript.Unknown
                ? "Invalid"
                : script == UnicodeScript.Han
                    ? "CJK (Han)"
                    : ObjectNames.NicifyVariableName(script.ToString());

        private static string GetDictionaryAssetName(UnicodeScript script)
            => script == UnicodeScript.Han ? "CJKDictionary" : $"{script}Dictionary";

        private static List<string> SelectDictionarySavePaths(List<UnicodeScript> scripts)
        {
            var preferredFolder = GetPrefDir(PrefDictSaveDir);
            if (scripts.Count == 1)
            {
                var path = EditorUtility.SaveFilePanelInProject(
                    "Save Dictionary Asset",
                    GetDictionaryAssetName(scripts[0]),
                    "asset",
                    "Choose where to save the dictionary asset.",
                    preferredFolder.Length > 0 ? preferredFolder : "Assets");
                if (string.IsNullOrEmpty(path)) return null;
                EditorPrefs.SetString(PrefDictSaveDir, Path.GetDirectoryName(path) ?? "Assets");
                return new List<string> { path };
            }

            var folder = EditorUtility.SaveFolderPanel(
                "Save Dictionary Assets",
                GetAbsoluteDictionarySaveFolder(preferredFolder),
                "");
            if (string.IsNullOrEmpty(folder)) return null;
            if (!TryGetAssetFolder(folder, out var assetFolder))
            {
                EditorUtility.DisplayDialog(
                    "Invalid Folder", "Dictionary assets must be saved inside the Assets folder.", "OK");
                return null;
            }

            EditorPrefs.SetString(PrefDictSaveDir, assetFolder);
            return scripts.Select(script =>
                Path.Combine(assetFolder, GetDictionaryAssetName(script) + ".asset")
                    .Replace("\\", "/")).ToList();
        }

        private static string GetAbsoluteDictionarySaveFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder)) return Application.dataPath;
            if (Path.IsPathRooted(assetFolder)) return assetFolder;
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectRoot ?? "", assetFolder));
        }

        private static bool TryGetAssetFolder(string folder, out string assetFolder)
        {
            var assetsPath = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var selectedPath = Path.GetFullPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(selectedPath, assetsPath, comparison))
            {
                assetFolder = "Assets";
                return true;
            }

            var prefix = assetsPath + Path.DirectorySeparatorChar;
            if (selectedPath.StartsWith(prefix, comparison))
            {
                assetFolder = ("Assets" + selectedPath.Substring(assetsPath.Length)).Replace("\\", "/");
                return true;
            }

            assetFolder = null;
            return false;
        }

        private static void SaveDictionaryAsset(DictBuildEntry build)
        {
            var existing = AssetDatabase.LoadAssetAtPath<WordSegmentationDictionary>(build.path);
            if (existing != null)
            {
                WordSegmentationDictionaryEditor.SetCompiledData(
                    existing, build.script, build.trieData);
                return;
            }

            var asset = CreateInstance<WordSegmentationDictionary>();
            asset.SetCompiledData(build.script, build.trieData);
            AssetDatabase.CreateAsset(asset, build.path);
        }
    }
}
