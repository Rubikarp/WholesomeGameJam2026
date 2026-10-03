using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Editor window that generates grayscale noise textures (value noise / FBM) with seamless tiling
    /// and saves them as importable PNG assets — for procedural effect shaders (dissolve, hologram,
    /// plasma…) and any other sampling need.
    /// </summary>
    public sealed class NoiseGeneratorWindow : EditorWindow
    {
        private enum NoiseKind
        {
            Value,
            Fbm,
        }

        private NoiseKind kind = NoiseKind.Fbm;
        private int size = 512;
        private int seed;
        private int frequency = 8;
        private int octaves = 4;
        private int lacunarity = 2;
        private float gain = 0.5f;
        private bool tileable = true;
        private bool invert;

        private Texture2D preview;
        private byte[] pixelBuffer;
        private bool dirty = true;

        private static readonly int[] sizeOptions = { 64, 128, 256, 512, 1024 };
        private static readonly string[] sizeLabels = { "64", "128", "256", "512", "1024" };

        [MenuItem(LightSideMenu.Tools.NoiseGenerator, false, 100)]
        internal static void Open()
        {
            var win = GetWindow<NoiseGeneratorWindow>("Noise Generator");
            win.minSize = new Vector2(320, 560);
        }

        private void OnEnable()
        {
            dirty = true;
        }

        private void OnDisable()
        {
            if (preview != null)
            {
                DestroyImmediate(preview);
                preview = null;
            }
            pixelBuffer = null;
        }

        /// <summary>Builds the retained-mode noise generator workspace.</summary>
        public void CreateGUI()
        {
            var root = InspectorVisuals.CreateWindowRoot(rootVisualElement);
            root.style.paddingLeft = 8f;
            root.style.paddingRight = 8f;
            root.style.paddingTop = 8f;
            root.style.paddingBottom = 8f;

            var title = new Label("Generator");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(title);

            var previewImage = new Image
            {
                scaleMode = ScaleMode.ScaleToFit,
            };
            previewImage.style.minHeight = 260f;
            previewImage.style.flexGrow = 1f;
            var save = new Button(SaveAsset) { text = "Save as PNG asset…" };
            save.style.height = 28f;
            var generateQueued = false;
            void QueueGenerate()
            {
                dirty = true;
                if (generateQueued) return;
                generateQueued = true;
                root.schedule.Execute(() =>
                {
                    generateQueued = false;
                    if (!dirty) return;
                    Generate();
                    dirty = false;
                    previewImage.image = preview;
                    save.SetEnabled(preview != null);
                });
            }

            var kindField = new EnumSelectorField("Type", kind);
            var sizes = new List<string>(sizeLabels);
            var sizeField = new SelectorField<string>("Size", sizes,
                Mathf.Max(0, Array.IndexOf(sizeOptions, size)));
            var seedField = new IntegerField("Seed") { value = seed };
            var frequencyField = new IntegerField("Frequency (cells)") { value = frequency };
            var fbm = InspectorVisuals.CreateStack();
            var octavesField = new InspectorSliderInt("Octaves", 1, 8) { value = octaves };
            var lacunarityField = new IntegerField("Lacunarity") { value = lacunarity };
            var gainField = new FloatField("Gain") { value = gain };
            fbm.Add(octavesField);
            fbm.Add(lacunarityField);
            fbm.Add(gainField);
            var tileableField = new InspectorToggle("Tileable (seamless)") { value = tileable };
            var invertField = new InspectorToggle("Invert") { value = invert };
            root.Add(kindField);
            root.Add(sizeField);
            root.Add(seedField);
            root.Add(frequencyField);
            root.Add(fbm);
            root.Add(tileableField);
            root.Add(invertField);

            kindField.RegisterValueChangedCallback(evt =>
            {
                kind = (NoiseKind)evt.newValue;
                InspectorMotion.SetExpanded(fbm, kind == NoiseKind.Fbm);
                QueueGenerate();
            });
            sizeField.RegisterValueChangedCallback(evt =>
            {
                var index = sizes.IndexOf(evt.newValue);
                if ((uint)index >= (uint)sizeOptions.Length) return;
                size = sizeOptions[index];
                QueueGenerate();
            });
            seedField.RegisterValueChangedCallback(evt =>
            {
                seed = evt.newValue;
                QueueGenerate();
            });
            frequencyField.RegisterValueChangedCallback(evt =>
            {
                frequency = Mathf.Max(1, evt.newValue);
                frequencyField.SetValueWithoutNotify(frequency);
                QueueGenerate();
            });
            octavesField.RegisterValueChangedCallback(evt =>
            {
                octaves = evt.newValue;
                QueueGenerate();
            });
            lacunarityField.RegisterValueChangedCallback(evt =>
            {
                lacunarity = Mathf.Max(2, evt.newValue);
                lacunarityField.SetValueWithoutNotify(lacunarity);
                QueueGenerate();
            });
            gainField.RegisterValueChangedCallback(evt =>
            {
                gain = Mathf.Clamp(evt.newValue, 0.01f, 1f);
                gainField.SetValueWithoutNotify(gain);
                QueueGenerate();
            });
            tileableField.RegisterValueChangedCallback(evt =>
            {
                tileable = evt.newValue;
                QueueGenerate();
            });
            invertField.RegisterValueChangedCallback(evt =>
            {
                invert = evt.newValue;
                QueueGenerate();
            });

            var regenerate = new Button(QueueGenerate) { text = "Regenerate Preview" };
            regenerate.style.height = InspectorVisuals.ControlHeight;
            root.Add(regenerate);
            var previewTitle = new Label("Preview");
            previewTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(previewTitle);
            root.Add(previewImage);
            save.SetEnabled(preview != null);
            root.Add(save);
            root.Add(new HelpBox(
                "Seamless tiling requires integer frequency and lacunarity. Saved PNG imports " +
                "as linear, bilinear, repeating, uncompressed texture without mipmaps.",
                HelpBoxMessageType.None));
            InspectorMotion.SetExpanded(fbm, kind == NoiseKind.Fbm, animate: false);
            QueueGenerate();
        }

        private void Generate()
        {
            if (preview == null || preview.width != size)
            {
                if (preview != null) DestroyImmediate(preview);
                preview = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
                {
                    name = "Noise Preview",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                pixelBuffer = new byte[size * size * 4];
            }

            var hashSeed = unchecked((uint)seed);
            float invSize = 1f / size;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = x * invSize * frequency;
                    var v = y * invSize * frequency;
                    var n = Sample(u, v, hashSeed);
                    if (invert) n = 1f - n;
                    var b = (byte)(Mathf.Clamp01(n) * 255f);
                    var idx = (y * size + x) * 4;
                    pixelBuffer[idx + 0] = b;
                    pixelBuffer[idx + 1] = b;
                    pixelBuffer[idx + 2] = b;
                    pixelBuffer[idx + 3] = 255;
                }
            }

            preview.SetPixelData(pixelBuffer, 0);
            preview.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private float Sample(float u, float v, uint hashSeed)
        {
            var wrap = tileable ? frequency : 0;
            if (kind == NoiseKind.Value)
                return ValueNoise(u, v, hashSeed, wrap);

            float sum = 0f, amp = 1f, freq = 1f, norm = 0f;
            for (var i = 0; i < octaves; i++)
            {
                var w = tileable ? frequency * (int)freq : 0;
                sum += amp * ValueNoise(u * freq, v * freq, hashSeed + (uint)i * 7919u, w);
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        private static float ValueNoise(float x, float y, uint seed, int wrapSize)
        {
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = x - x0;
            var fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            var x1 = x0 + 1;
            var y1 = y0 + 1;

            if (wrapSize > 0)
            {
                x0 = Mod(x0, wrapSize);
                y0 = Mod(y0, wrapSize);
                x1 = Mod(x1, wrapSize);
                y1 = Mod(y1, wrapSize);
            }

            var h00 = Hash01(x0, y0, seed);
            var h10 = Hash01(x1, y0, seed);
            var h01 = Hash01(x0, y1, seed);
            var h11 = Hash01(x1, y1, seed);

            return Lerp(Lerp(h00, h10, fx), Lerp(h01, h11, fx), fy);
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Hash01(int x, int y, uint seed)
        {
            var h = unchecked((uint)x * 0x27d4eb2du ^ (uint)y * 0x165667b1u ^ seed);
            h ^= h >> 15;
            h = unchecked(h * 0x2c1b3c6du);
            h ^= h >> 12;
            h = unchecked(h * 0x297a2d39u);
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / (float)0xFFFFFFu;
        }

        private void SaveAsset()
        {
            if (preview == null) return;

            var path = EditorUtility.SaveFilePanelInProject(
                "Save Noise Texture",
                "Noise",
                "png",
                "Choose a location inside the Assets folder.");
            if (string.IsNullOrEmpty(path)) return;

            var bytes = preview.EncodeToPNG();
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (saved != null)
            {
                EditorGUIUtility.PingObject(saved);
                Selection.activeObject = saved;
            }
        }
    }
}
