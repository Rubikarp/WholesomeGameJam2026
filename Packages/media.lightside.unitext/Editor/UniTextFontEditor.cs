using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LightSide
{
    [CustomEditor(typeof(UniTextFont), editorForChildClasses: true)]
    [CanEditMultipleObjects]
    internal partial class UniTextFontEditor : FullWidthEditor
    {
        private SerializedProperty sourceFontProp;
        private SerializedProperty variantSourceProp;
        private SerializedProperty faceIndexProp;
        private SerializedProperty faceInfoProp;
        private SerializedProperty italicStyleProp;
        private SerializedProperty spacingOffsetProp;
        private SerializedProperty spaceAdvanceProp;
        private SerializedProperty fakeBoldWeightProp;
        private SerializedProperty fontScaleProp;
        private SerializedProperty participatesInNormalizationProp;
        private SerializedProperty sdfDetailMultiplierProp;
        private SerializedProperty tileSizeOffsetProp;
        private SerializedProperty colorPixelSizeProp;
        private SerializedProperty glyphOverridesProp;
        private SerializedProperty axisDefaultsProp;

        private UniTextFont cachedFaceSource;
        private string[] cachedFaceLabels;
        private bool cachedFaceBuilt;

        private string glyphPickerText = "";
        private List<GlyphPickerEntry> glyphPickerEntries = new();
        private HashSet<int> glyphPickerSelection = new();

        private struct GlyphPickerEntry
        {
            public int glyphIndex;
            public string label;
            public Texture2D preview;
        }

#if UNITEXT_DEBUG
        private int debugSdfSlice;
        private int debugMsdfSlice;
#endif

        private void OnEnable()
        {
            sourceFontProp = InspectorHelpers.RequireProperty(serializedObject, "sourceFont");
            variantSourceProp = serializedObject.FindProperty("source");
            faceIndexProp = serializedObject.FindProperty("faceIndex");
            if ((variantSourceProp == null) != (faceIndexProp == null))
                throw new InvalidOperationException(
                    "A font variant must serialize both source and faceIndex.");
            faceInfoProp = InspectorHelpers.RequireProperty(serializedObject, "faceInfo");
            italicStyleProp = InspectorHelpers.RequireProperty(serializedObject, "italicStyle");
            spacingOffsetProp = InspectorHelpers.RequireProperty(serializedObject, "spacingOffset");
            spaceAdvanceProp = InspectorHelpers.RequireProperty(serializedObject, "spaceAdvance");
            fakeBoldWeightProp = InspectorHelpers.RequireProperty(serializedObject, "fakeBoldWeight");
            fontScaleProp = InspectorHelpers.RequireProperty(serializedObject, "fontScale");
            participatesInNormalizationProp = InspectorHelpers.RequireProperty(
                serializedObject, "participatesInNormalization");
            sdfDetailMultiplierProp = InspectorHelpers.RequireProperty(
                serializedObject, "sdfDetailMultiplier");
            tileSizeOffsetProp = InspectorHelpers.RequireProperty(serializedObject, "tileSizeOffset");
            colorPixelSizeProp = serializedObject.FindProperty("colorPixelSize");
            glyphOverridesProp = InspectorHelpers.RequireProperty(serializedObject, "glyphOverrides");
            axisDefaultsProp = InspectorHelpers.RequireProperty(serializedObject, "axisDefaults");
            cachedFaceSource = null;
            cachedFaceLabels = null;
            cachedFaceBuilt = false;
            serializedObject.Update();
        }

        private string[] GetFaceLabels(UniTextFont source)
        {
            if (cachedFaceBuilt && source == cachedFaceSource) return cachedFaceLabels;
            cachedFaceSource = source;
            cachedFaceBuilt = true;
            cachedFaceLabels = BuildFaceLabels(source.CopyFontData());
            return cachedFaceLabels;
        }

        /// <summary>Dropdown labels (<c>"i: Family Style"</c>) for each face of a .ttc/.otc collection, or null when the file holds a single face. Shared with the tools window.</summary>
        internal static string[] BuildFaceLabels(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return null;
            if (!FT.IsInitialized) FT.Initialize();

            var probe = FT.LoadFace(bytes, 0);
            if (probe == System.IntPtr.Zero) return null;
            int count = System.Math.Max(1, FT.GetFaceInfo(probe).numFaces);
            FT.UnloadFace(probe);
            if (count <= 1) return null;

            var labels = new string[count];
            for (int i = 0; i < count; i++)
            {
                string name = $"Face {i}";
                var face = FT.LoadFace(bytes, i);
                if (face != System.IntPtr.Zero)
                {
                    var fi = UniTextFont.Core.BuildFullFaceInfo(face);
                    FT.UnloadFace(face);
                    if (!string.IsNullOrEmpty(fi.familyName))
                        name = string.IsNullOrEmpty(fi.styleName) ? fi.familyName : $"{fi.familyName} {fi.styleName}";
                }
                labels[i] = $"{i}: {name}";
            }
            return labels;
        }

        private void RebuildGlyphPicker()
        {
            foreach (var entry in glyphPickerEntries)
                if (entry.preview != null) DestroyImmediate(entry.preview);
            glyphPickerEntries.Clear();

            if (string.IsNullOrEmpty(glyphPickerText)) return;

            serializedObject.UpdateIfRequiredOrScript();
            glyphOverridesProp = InspectorHelpers.RequireProperty(
                serializedObject, "glyphOverrides");

            var font = (UniTextFont)target;
            if (!font.HasFontData) return;

            var codepoints = StringToCodepoints(glyphPickerText);
            var glyphIndices = ShapeToGlyphIndices(font, codepoints);

            var seen = new HashSet<int>();
            var uniqueGlyphs = new List<(int glyphIndex, string label)>();

            int cpIdx = 0;
            for (int i = 0; i < glyphIndices.Count; i++)
            {
                int gid = glyphIndices[i];
                if (gid == 0 || !seen.Add(gid)) { cpIdx++; continue; }

                string label = $"#{gid}";
                uniqueGlyphs.Add((gid, label));
                cpIdx++;
            }

            var fontData = font.CopyFontData();
            var face = FT.LoadFace(fontData, font.FaceInfo.faceIndex);
            if (face == System.IntPtr.Zero) return;

            FT.SetPixelSize(face, 40);

            foreach (var (glyphIndex, label) in uniqueGlyphs)
            {
                Texture2D preview = null;
                if (FT.LoadGlyph(face, (uint)glyphIndex) && FT.RenderGlyph(face))
                {
                    var bitmap = FT.GetBitmapData(face);
                    var top = FT.GetBitmapTop(face);
                    if (bitmap.width > 0 && bitmap.height > 0 && bitmap.buffer != System.IntPtr.Zero)
                        preview = BitmapToFixedTexture(bitmap, top, 48);
                }

                glyphPickerEntries.Add(new GlyphPickerEntry
                {
                    glyphIndex = glyphIndex,
                    label = label,
                    preview = preview,
                });
            }

            FT.UnloadFace(face);

            glyphPickerSelection.Clear();
            for (int i = 0; i < glyphOverridesProp.arraySize; i++)
            {
                var gid = InspectorHelpers.RequireRelative(
                    glyphOverridesProp.GetArrayElementAtIndex(i), "glyphIndex").intValue;
                glyphPickerSelection.Add(gid);
            }
        }

        private static List<int> StringToCodepoints(string text)
        {
            var result = new List<int>(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                result.Add((int)UnicodeData.DecodeAt(text, i, out var size));
                i += size - 1;
            }
            return result;
        }

        private static List<int> ShapeToGlyphIndices(UniTextFont font, List<int> codepoints)
        {
            var result = new List<int>(codepoints.Count);
            for (int i = 0; i < codepoints.Count; i++)
            {
                var gid = Shaper.GetGlyphIndex(font, (uint)codepoints[i]);
                result.Add((int)gid);
            }
            return result;
        }

        private static Texture2D BitmapToFixedTexture(FT.BitmapData bitmap, int bitmapTop, int texSize)
        {
            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var pixels = new Color32[texSize * texSize];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            int bw = bitmap.width;
            int bh = bitmap.height;

            int baseline = texSize * 3 / 4;
            int ox = (texSize - bw) / 2;
            int oy = baseline - bitmapTop;

            unsafe
            {
                byte* src = (byte*)bitmap.buffer;
                for (int y = 0; y < bh; y++)
                {
                    byte* row = src + y * bitmap.pitch;
                    int dstY = oy + y;
                    if (dstY < 0 || dstY >= texSize) continue;
                    int flippedY = texSize - 1 - dstY;
                    for (int x = 0; x < bw; x++)
                    {
                        int dstX = ox + x;
                        if (dstX < 0 || dstX >= texSize) continue;
                        byte a = row[x];
                        pixels[flippedY * texSize + dstX] = new Color32(255, 255, 255, a);
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private void OnDisable()
        {
            foreach (var entry in glyphPickerEntries)
                if (entry.preview != null) DestroyImmediate(entry.preview);
            glyphPickerEntries.Clear();
        }
#if UNITEXT_DEBUG
        private static void SaveAtlasSliceAsPng(Texture arr, int slice, bool isMsdf, string defaultName)
        {
            var path = EditorUtility.SaveFilePanel("Save Atlas Page as PNG", "", defaultName, "png");
            if (string.IsNullOrEmpty(path)) return;

            var mat = GetPreviewMaterial();
            mat.SetFloat(ShaderIds.AtlasPreview.SliceIndex, slice);
            mat.SetFloat(ShaderIds.AtlasPreview.Mode, isMsdf ? 1f : 0f);

            var rt = RenderTexture.GetTemporary(arr.width, arr.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(arr, rt, mat);

            var readable = new Texture2D(arr.width, arr.height, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            readable.ReadPixels(new Rect(0, 0, arr.width, arr.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(path, readable.EncodeToPNG());
            DestroyImmediate(readable);
            Debug.Log($"Atlas page saved to: {path}");
        }
#endif

        #region Preview

        private int previewTab;
        private int previewPageIndex;
        private readonly int[] previewTabIds = new int[3];
        private readonly int[] previewTabPages = new int[3];
        private static Material atlasPreviewMat;
        private static bool previewRendered;

        public override bool HasPreviewGUI()
        {
            var hasAtlas = false;
            GlyphAtlas.ForEachInstance(atlas =>
            {
                if (atlas.PageCount > 0) hasAtlas = true;
            });
            return hasAtlas || GlyphAtlas.Color?.PageCount > 0;
        }

        public override GUIContent GetPreviewTitle() => new("Atlas Preview");

        public override void OnPreviewSettings()
        {
            var tabCount = 0;
            AddTab(0, GlyphAtlas.GetInstance(UniTextRenderMode.SDF).PageCount);
            AddTab(1, GlyphAtlas.GetInstance(UniTextRenderMode.MSDF).PageCount);
            AddTab(2, GlyphAtlas.Color?.PageCount ?? 0);
            if (tabCount == 0) return;

            if (tabCount > 1)
            {
                var labels = new string[tabCount];
                var selected = 0;
                for (var i = 0; i < tabCount; i++)
                {
                    labels[i] = previewTabIds[i] switch
                    {
                        0 => "SDF",
                        1 => "MSDF",
                        _ => "Color",
                    };
                    if (previewTabIds[i] == previewTab) selected = i;
                }
                var widths = new[] { 90, 120, 150 };
                previewTab = previewTabIds[GUILayout.Toolbar(selected, labels,
                    EditorStyles.miniButton, GUILayout.Width(widths[tabCount - 1]))];
            }
            else
            {
                previewTab = previewTabIds[0];
            }

            var current = 0;
            for (var i = 0; i < tabCount; i++)
                if (previewTabIds[i] == previewTab) current = i;
            var pageCount = previewTabPages[current];
            previewPageIndex = Mathf.Clamp(previewPageIndex, 0, pageCount - 1);
            if (pageCount > 1)
            {
                GUILayout.Label($"{previewPageIndex + 1}/{pageCount}",
                    EditorStyles.miniLabel, GUILayout.Width(35));
                previewPageIndex = Mathf.RoundToInt(GUILayout.HorizontalSlider(
                    previewPageIndex, 0, pageCount - 1, GUILayout.Width(80)));
            }
            if (previewTab < 2)
                previewRendered = GUILayout.Toggle(previewRendered, "Rendered",
                    EditorStyles.miniButton, GUILayout.Width(65));

            void AddTab(int id, int pages)
            {
                if (pages <= 0) return;
                previewTabIds[tabCount] = id;
                previewTabPages[tabCount] = pages;
                tabCount++;
            }
        }

        public override void OnPreviewGUI(Rect rect, GUIStyle background)
        {
            Texture texture;
            string info;
            var material = GetPreviewMaterial();
            if (previewTab == 0)
            {
                var atlas = GlyphAtlas.GetInstance(UniTextRenderMode.SDF);
                if (atlas.AtlasTexture == null || atlas.PageCount == 0) return;
                var index = Mathf.Clamp(previewPageIndex, 0, atlas.PageCount - 1);
                texture = atlas.AtlasTexture;
                info = $"SDF: slice {index + 1}/{atlas.PageCount}  " +
                       $"{texture.width}x{texture.height}  RHalf";
                material.SetFloat(ShaderIds.AtlasPreview.SliceIndex, index);
                material.SetFloat(ShaderIds.AtlasPreview.Mode, 0f);
            }
            else if (previewTab == 1)
            {
                var atlas = GlyphAtlas.GetInstance(UniTextRenderMode.MSDF);
                if (atlas.AtlasTexture == null || atlas.PageCount == 0) return;
                var index = Mathf.Clamp(previewPageIndex, 0, atlas.PageCount - 1);
                texture = atlas.AtlasTexture;
                info = $"MSDF: slice {index + 1}/{atlas.PageCount}  " +
                       $"{texture.width}x{texture.height}  RGBAHalf";
                material.SetFloat(ShaderIds.AtlasPreview.SliceIndex, index);
                material.SetFloat(ShaderIds.AtlasPreview.Mode, 1f);
            }
            else
            {
                var atlas = GlyphAtlas.Color;
                if (atlas == null || atlas.AtlasTexture == null || atlas.PageCount == 0) return;
                var index = Mathf.Clamp(previewPageIndex, 0, atlas.PageCount - 1);
                texture = atlas.AtlasTexture;
                info = $"Color: slice {index + 1}/{atlas.PageCount}  " +
                       $"{texture.width}x{texture.height}  RGBA32";
                material.SetFloat(ShaderIds.AtlasPreview.SliceIndex, index);
                material.SetFloat(ShaderIds.AtlasPreview.Mode, 2f);
            }

            material.SetFloat(ShaderIds.AtlasPreview.Rendered,
                previewRendered && previewTab < 2 ? 1f : 0f);
            var textureAspect = texture.AspectRatio();
            var rectAspect = rect.width / rect.height;
            Rect textureRect;
            if (textureAspect > rectAspect)
            {
                var height = rect.width / textureAspect;
                textureRect = new Rect(rect.x, rect.y + (rect.height - height) * 0.5f,
                    rect.width, height);
            }
            else
            {
                var width = rect.height * textureAspect;
                textureRect = new Rect(rect.x + (rect.width - width) * 0.5f,
                    rect.y, width, rect.height);
            }
            EditorGUI.DrawPreviewTexture(textureRect, texture, material, ScaleMode.ScaleToFit);
            EditorGUI.DropShadowLabel(
                new Rect(rect.x + 4, rect.yMax - 18, rect.width - 8, 16),
                info, EditorStyles.miniLabel);
        }

        private static Material GetPreviewMaterial()
        {
            if (atlasPreviewMat == null)
            {
                var shader = Shader.Find("Hidden/LightSide/AtlasPreview") ??
                             throw new InvalidOperationException(
                                 "Required shader 'Hidden/LightSide/AtlasPreview' is unavailable.");
                atlasPreviewMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            return atlasPreviewMat;
        }

        #endregion

        [MenuItem(UniTextMenu.Create.FontAsset, true)]
        internal static bool CreateFontAssetValidate()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Font) return true;
                var path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".ttf" or ".otf" or ".ttc" or ".otc") return true;
                }
            }
            return false;
        }

        [MenuItem(UniTextMenu.Create.FontAsset, false, 100)]
        internal static void CreateFontAsset()
            => CreateFontAssetsFromSelection(UniTextFont.CreateFontAsset, "", "font");

        [MenuItem(UniTextMenu.Create.ColorFontAsset, true)]
        private static bool CreateColorFontAssetValidate() => CreateFontAssetValidate();

        [MenuItem(UniTextMenu.Create.ColorFontAsset, false, 100)]
        internal static void CreateColorFontAsset()
            => CreateFontAssetsFromSelection(UniTextFont.CreateFontAsset<UniTextColorFont>, " (Color)", "color font");

        /// <summary>Creates a font asset of the chosen kind from every selected font file (or Unity <see cref="Font"/>), reading its bytes through <paramref name="factory"/>. Shared by the plain and color-font create menus.</summary>
        private static void CreateFontAssetsFromSelection(Func<byte[], UniTextFont> factory, string nameSuffix, string label)
        {
            var created = new List<Object>();

            foreach (var obj in Selection.objects)
            {
                var assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) continue;

                bool isFont = obj is Font;
                if (!isFont)
                {
                    var ext = Path.GetExtension(assetPath).ToLowerInvariant();
                    if (ext is not (".ttf" or ".otf" or ".ttc" or ".otc")) continue;
                }

                var fullPath = Path.GetFullPath(assetPath);
                if (!File.Exists(fullPath)) continue;

                byte[] fontBytes;
                try { fontBytes = File.ReadAllBytes(fullPath); }
                catch { continue; }

                var holder = factory(fontBytes);
                if (holder == null)
                {
                    Debug.LogError($"Failed to create {label} asset from {Path.GetFileName(assetPath)}");
                    continue;
                }

                if (obj is Font font)
                    holder.sourceFont = font;

                var dir = Path.GetDirectoryName(assetPath);
                var name = Path.GetFileNameWithoutExtension(assetPath);
                var holderPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(dir, name + nameSuffix + ".asset").Replace("\\", "/"));
                UniTextFontAssetPersistence.Create(holder, holderPath);
                created.Add(holder);
            }

            if (created.Count == 0) return;

            AssetDatabase.SaveAssets();
            Selection.objects = created.ToArray();
            EditorGUIUtility.PingObject(created[^1]);
            Debug.Log($"Created {created.Count} UniText {label} asset(s)");
        }

        /// <summary>Groups fonts by family name into <see cref="FontFamily"/> entries, choosing the cut closest to Regular-upright as each family's primary and the rest as its faces.</summary>
        private static FontFamily[] BuildFamilies(List<UniTextFont> fonts)
        {
            var groups = new Dictionary<string, List<UniTextFont>>();
            var order = new List<string>();
            foreach (var f in fonts)
            {
                var key = f.FaceInfo.familyName ?? "";
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<UniTextFont>();
                    groups[key] = list;
                    order.Add(key);
                }
                list.Add(f);
            }

            var families = new List<FontFamily>();
            foreach (var key in order)
            {
                var group = groups[key];
                int bestIdx = 0, bestScore = int.MaxValue;
                for (int i = 0; i < group.Count; i++)
                {
                    var fi = group[i].FaceInfo;
                    var w = fi.weightClass > 0 ? fi.weightClass : 400;
                    var score = System.Math.Abs(w - 400) + (fi.isItalic ? 1000 : 0);
                    if (score < bestScore) { bestScore = score; bestIdx = i; }
                }

                var primary = group[bestIdx];
                UniTextFont[] faces = null;
                if (group.Count > 1)
                {
                    faces = new UniTextFont[group.Count - 1];
                    int j = 0;
                    for (int i = 0; i < group.Count; i++)
                        if (i != bestIdx) faces[j++] = group[i];
                }

                families.Add(new FontFamily(null, primary, faces));
            }
            return families.ToArray();
        }

        private static UniTextFontStack CreateStackAsset(FontFamily[] families, string dir, string baseName)
        {
            if (families == null || families.Length == 0) return null;

            var stack = CreateInstance<UniTextFontStack>();
            stack.Families.ReplaceAll(families);

            bool allVariable = true, anyVariable = false;
            foreach (var family in families)
            {
                if (family.primary != null && family.primary.IsVariable) anyVariable = true;
                else allVariable = false;
            }
            var suffix = allVariable ? "Variable" : anyVariable ? "Mixed" : "Static";
            var savePath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(dir, baseName + "-" + suffix + ".asset").Replace("\\", "/"));
            AssetDatabase.CreateAsset(stack, savePath);
            return stack;
        }

        [MenuItem(UniTextMenu.Create.FontStackCombined, true)]
        internal static bool CreateFontsCombinedAssetValidate()
        {
            bool firstFound = false;

            foreach (var obj in Selection.objects)
            {
                if (obj is UniTextFont)
                {
                    if (firstFound)
                    {
                        return true;
                    }

                    firstFound = true;
                }
            }

            return false;
        }

        [MenuItem(UniTextMenu.Create.FontStackPerFont, true)]
        internal static bool CreateFontsAssetValidate()
        {
            foreach (var obj in Selection.objects)
                if (obj is UniTextFont) return true;
            return false;
        }

        [MenuItem(UniTextMenu.Create.FontStackCombined, false, 101)]
        internal static void CreateFontsCombined()
        {
            var fonts = new List<UniTextFont>();
            foreach (var obj in Selection.objects)
                if (obj is UniTextFont font)
                    fonts.Add(font);

            if (fonts.Count == 0) return;

            var families = BuildFamilies(fonts);
            var names = new List<string>();
            foreach (var family in families)
                names.Add(family.primary != null ? (family.primary.FaceInfo.familyName ?? "") : "");
            var baseName = string.Join("+", names).Replace(" ", "-");

            var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(fonts[0]));
            var stack = CreateStackAsset(families, dir, baseName);
            if (stack == null) return;

            AssetDatabase.SaveAssets();
            Selection.activeObject = stack;
            EditorGUIUtility.PingObject(stack);
        }

        [MenuItem(UniTextMenu.Create.FontStackPerFont, false, 102)]
        internal static void CreateFontsPerFont()
        {
            var created = new List<Object>();

            foreach (var obj in Selection.objects)
            {
                if (obj is not UniTextFont font) continue;

                var fontsAsset = CreateInstance<UniTextFontStack>();
                fontsAsset.Families.ReplaceAll(new[] { new FontFamily { primary = font } });

                var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(font));
                var savePath = Path.Combine(dir, font.name + " FontStack.asset").Replace("\\", "/");
                savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

                AssetDatabase.CreateAsset(fontsAsset, savePath);
                created.Add(fontsAsset);
            }

            if (created.Count == 0) return;

            AssetDatabase.SaveAssets();
            Selection.objects = created.ToArray();
            EditorGUIUtility.PingObject(created[^1]);
        }

        [MenuItem(UniTextMenu.Create.FontVariant, true)]
        internal static bool CreateFontVariantValidate()
        {
            foreach (var obj in Selection.objects)
                if (obj is UniTextFont) return true;
            return false;
        }

        [MenuItem(UniTextMenu.Create.FontVariant, false, 103)]
        internal static void CreateFontVariant()
        {
            var created = new List<Object>();

            foreach (var obj in Selection.objects)
            {
                if (obj is not UniTextFont font) continue;

                var variant = CreateInstance<UniTextFontVariant>();
                variant.Source = font;

                var dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(font));
                var savePath = Path.Combine(dir, font.name + " Variant.asset").Replace("\\", "/");
                savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);

                AssetDatabase.CreateAsset(variant, savePath);
                created.Add(variant);
            }

            if (created.Count == 0) return;

            AssetDatabase.SaveAssets();
            Selection.objects = created.ToArray();
            EditorGUIUtility.PingObject(created[^1]);
        }
    }

}
