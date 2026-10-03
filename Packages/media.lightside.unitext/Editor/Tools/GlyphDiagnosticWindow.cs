using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    /// <summary>
    /// Live glyph inspector: type text, shape it with HarfBuzz, and study every glyph's outline
    /// in a crisp vector viewport — before/after <see cref="ContourUnionBurst"/>, control points,
    /// junctions, brute-force SDF ground truth, and variable-font axes. Editor-only debug tool.
    /// </summary>
    internal unsafe partial class GlyphDiagnosticWindow : EditorWindow
    {
        [MenuItem(UniTextMenu.Tools.GlyphDiagnostic, false, 100)]
        internal static void Open()
        {
            var w = GetWindow<GlyphDiagnosticWindow>("Glyph Diagnostic");
            w.minSize = new Vector2(900, 640);
        }


        UniTextFont font;
        string text = "Rag";

        enum Dir { Auto, LTR, RTL, TTB, BTT }
        Dir direction = Dir.Auto;

        static readonly (string name, uint tag)[] scripts =
        {
            ("Auto", HB.Script.Common), ("Latin", HB.Script.Latin), ("Arabic", HB.Script.Arabic),
            ("Hebrew", HB.Script.Hebrew), ("Cyrillic", HB.Script.Cyrillic), ("Greek", HB.Script.Greek),
            ("Devanagari", HB.Script.Devanagari), ("Bengali", HB.Script.Bengali), ("Han", HB.Script.Han),
            ("Hangul", HB.Script.Hangul), ("Hiragana", HB.Script.Hiragana), ("Katakana", HB.Script.Katakana),
            ("Thai", HB.Script.Thai), ("Armenian", HB.Script.Armenian), ("Georgian", HB.Script.Georgian),
            ("Tamil", HB.Script.Tamil), ("Telugu", HB.Script.Telugu), ("Khmer", HB.Script.Khmer),
        };
        int scriptIdx;

        struct Feat { public string tag; public bool on; }
        Feat[] featureToggles =
        {
            new Feat { tag = "liga", on = true }, new Feat { tag = "calt", on = true },
            new Feat { tag = "kern", on = true }, new Feat { tag = "dlig", on = false },
            new Feat { tag = "smcp", on = false }, new Feat { tag = "onum", on = false },
            new Feat { tag = "frac", on = false }, new Feat { tag = "ss01", on = false },
        };
        string customFeatures = "";
        bool showFeatures;
        bool showAxes = true;

        HB.hb_ot_var_axis_info_t[] axisInfos;
        float[] axisValues;

        enum ViewMode { Single, Line }
        ViewMode viewMode = ViewMode.Single;
        float viewScale = 1f;
        Vector2 viewOffset;
        bool frameQueued = true;
        Vector2 lastMouse;

        bool showRaw = true, showResolved = true, showFill = true;
        bool showControlPts = true, showOnCurve = true, showDirection, showIndices, showJunctions = true, showMetrics = true;

        enum Panel { Data, Sdf, Union }
        Panel panel = Panel.Data;
        int sdfSourceRes;
        static readonly int[] tileSizes = { 64, 128, 256 };
        static readonly string[] tileSizeLabels = { "64", "128", "256" };
        int tileSizeIdx = 1;

        struct RunGlyph
        {
            public uint glyphId;
            public int cluster;
            public float xAdvance, yAdvance, xOffset, yOffset;
            public uint srcCp;
        }
        RunGlyph[] run = Array.Empty<RunGlyph>();
        string shapeError;
        int selected;

        readonly Dictionary<uint, GlyphExtract> extractCache = new();
        int privateFaceHash;
        FreeTypeFace privateFace;

        bool dirtyShape = true;

        Texture2D sdfGray, sdfContour, sdfSeg, sdfWinding, fillTex;
        float[] bfDist;
        int[] bfSeg;
        byte[] bfWind;
        string dataText = "";
        bool fillResolved;

        uint viewedGlyphId = uint.MaxValue;
        int viewedSource = -1;
        bool viewForceRefresh;

        DiagSegment[] segments;
        int segmentCount, contourCount;
        int[] contourEndIndices;
        float aspect = 1f, glyphH = 1f;
        GlyphCurveCache.GlyphCurveData curveData;
        JunctionInfo[] junctions;




        struct DiagSegment
        {
            public float p0x, p0y, p1x, p1y, p2x, p2y;
            public int contourIndex;
            public bool isDegenerate;
        }

        struct JunctionInfo
        {
            public int segA, segB;
            public Vector2 posA, posB;
            public float posError;
            public Vector2 tangentA, tangentB;
            public float angleDeg;
        }

        sealed class GlyphExtract
        {
            public uint glyphId;
            public bool valid;

            public DiagSegment[] design; public int[] designEnds; public int designCount, designContours;
            public float minX, minY, maxX, maxY, bboxW, bboxH;
            public int designW, designH, upem;
            public float advance, bearingX, bearingY;

            public DiagSegment[] raw; public int[] rawEnds; public int rawCount, rawContours;
            public DiagSegment[] res; public int[] resEnds; public int resCount, resContours;
            public bool unionResolved, unionChanged;
            public string unionInfo;

            public JunctionInfo[] junctions;
        }

        static readonly Color[] segColors =
        {
            new Color(1f, 0.35f, 0.35f), new Color(0.35f, 0.85f, 0.4f), new Color(0.4f, 0.6f, 1f),
            new Color(1f, 0.82f, 0.2f), new Color(0.2f, 0.9f, 0.9f), new Color(1f, 0.5f, 0.85f),
            new Color(0.95f, 0.6f, 0.2f), new Color(0.6f, 1f, 0.6f), new Color(0.7f, 0.45f, 1f),
            new Color(1f, 1f, 0.55f), new Color(0.55f, 0.82f, 1f), new Color(1f, 0.68f, 0.68f),
        };

        static readonly Color BgColor = new(0.13f, 0.13f, 0.16f, 1f);
        static readonly Color RawColor = new(0.5f, 0.55f, 0.62f, 1f);
        static readonly Color ResColor = new(0.25f, 0.85f, 1f, 1f);



        void OnDisable() => Cleanup();
        void OnDestroy() => Cleanup();

        void Cleanup()
        {
            DestroyTex(ref sdfGray); DestroyTex(ref sdfContour); DestroyTex(ref sdfSeg);
            DestroyTex(ref sdfWinding); DestroyTex(ref fillTex);
            privateFace?.Dispose();
            privateFace = null;
            extractCache.Clear();
        }

        static void DestroyTex(ref Texture2D t) { if (t != null) { DestroyImmediate(t); t = null; } }



        /// <summary>Builds the retained-mode glyph diagnostics workspace.</summary>
        public void CreateGUI()
        {
            CreateToolkitGUI();
        }

        void OnFontChanged()
        {
            extractCache.Clear();
            DropSelectedTextures();
            viewForceRefresh = true;
            run = Array.Empty<RunGlyph>();
            selected = 0;
            frameQueued = true;

            axisInfos = font != null && font.HasFontData ? Shaper.GetVariableAxisInfos(font) : null;
            if (axisInfos != null)
            {
                axisValues = new float[axisInfos.Length];
                for (int i = 0; i < axisInfos.Length; i++) axisValues[i] = axisInfos[i].defaultValue;
            }
            else axisValues = null;

            privateFace?.Dispose();
            privateFace = null;
            privateFaceHash = 0;
        }

        void EnsurePrivateFace()
        {
            int hash = font.FontDataHash;
            if (privateFace != null && hash == privateFaceHash) return;
            privateFace?.Dispose();
            privateFace = FreeTypeFace.TryCreate(font.CaptureFontSource(), font.FaceInfo.faceIndex);
            privateFaceHash = hash;

            if (axisInfos == null)
            {
                axisInfos = Shaper.GetVariableAxisInfos(font);
                if (axisInfos != null)
                {
                    axisValues = new float[axisInfos.Length];
                    for (int i = 0; i < axisInfos.Length; i++) axisValues[i] = axisInfos[i].defaultValue;
                }
            }
        }



        void Shape()
        {
            shapeError = null;
            run = Array.Empty<RunGlyph>();

            if (string.IsNullOrEmpty(text)) return;

            var cache = Shaper.GetOrCreateCoreCache(font);
            if (cache == null || !cache.IsValid) { shapeError = "Failed to create HarfBuzz font"; return; }

            var cps = new int[text.Length];
            int cpCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                cps[cpCount++] = (int)UnicodeData.DecodeAt(text, i, out int size);
                i += size - 1;
            }

            uint scriptTag = scripts[scriptIdx].tag;
            int dir = ResolveDirection(scriptTag);

            var variations = BuildHbVariations();
            var feats = BuildFeatures();
            if (!cache.TryAcquire(out var fontLease)) { shapeError = "HarfBuzz font was retired"; return; }

            Shaper.FontCacheEntry.SubFontLease subFontLease = default;
            var buf = IntPtr.Zero;
            try
            {
                IntPtr shapingFont = fontLease.Font;
                if (variations != null && variations.Length > 0)
                {
                    int key = HashVariations(variations);
                    subFontLease = cache.AcquireSubFont(fontLease, key, variations);
                    shapingFont = subFontLease.Pointer;
                }

                buf = HB.CreateBuffer();
                if (buf == IntPtr.Zero) throw new InvalidOperationException("[HarfBuzz] Failed to create a diagnostic shaping buffer.");
                HB.SetDirection(buf, dir);
                HB.SetScript(buf, scriptTag);
                HB.AddCodepoints(buf, new ReadOnlySpan<int>(cps, 0, cpCount), 0, cpCount);
                HB.Shape(shapingFont, buf, feats.Length > 0 ? feats : null);

                var glyphs = HB.GetGlyphInfos(buf);
                run = new RunGlyph[glyphs.Length];
                for (int i = 0; i < glyphs.Length; i++)
                {
                    var g = glyphs[i];
                    run[i] = new RunGlyph
                    {
                        glyphId = g.glyphId,
                        cluster = (int)g.cluster,
                        xAdvance = g.xAdvance,
                        yAdvance = g.yAdvance,
                        xOffset = g.xOffset,
                        yOffset = g.yOffset,
                        srcCp = (uint)((g.cluster < (uint)cpCount) ? cps[g.cluster] : 0)
                    };
                }
            }
            finally
            {
                subFontLease.Dispose();
                if (buf != IntPtr.Zero) HB.DestroyBuffer(buf);
                fontLease.Dispose();
            }

            if (selected >= run.Length) selected = Mathf.Max(0, run.Length - 1);
        }

        int ResolveDirection(uint scriptTag)
        {
            switch (direction)
            {
                case Dir.LTR: return HB.DIRECTION_LTR;
                case Dir.RTL: return HB.DIRECTION_RTL;
                case Dir.TTB: return HB.DIRECTION_TTB;
                case Dir.BTT: return HB.DIRECTION_BTT;
                default:
                    return (scriptTag == HB.Script.Arabic || scriptTag == HB.Script.Hebrew)
                        ? HB.DIRECTION_RTL : HB.DIRECTION_LTR;
            }
        }

        HB.hb_variation_t[] BuildHbVariations()
        {
            if (axisInfos == null || axisValues == null) return null;
            var list = new List<HB.hb_variation_t>();
            for (int i = 0; i < axisInfos.Length; i++)
                if (Mathf.Abs(axisValues[i] - axisInfos[i].defaultValue) > 0.001f)
                    list.Add(new HB.hb_variation_t { tag = axisInfos[i].tag, value = axisValues[i] });
            return list.ToArray();
        }

        static int HashVariations(HB.hb_variation_t[] v)
        {
            int h = 17;
            foreach (var x in v)
            {
                h = h * 31 + (int)x.tag;
                h = h * 31 + BitConverter.SingleToInt32Bits(x.value);
            }
            return h & 0x7FFFFFFF;
        }

        HB.hb_feature_t[] BuildFeatures()
        {
            var list = new List<HB.hb_feature_t>();
            foreach (var f in featureToggles)
                list.Add(new HB.hb_feature_t
                {
                    tag = Tag4(f.tag), value = f.on ? 1u : 0u,
                    start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END
                });

            if (!string.IsNullOrWhiteSpace(customFeatures))
            {
                foreach (var part in customFeatures.Split(',', ' '))
                {
                    var p = part.Trim();
                    if (p.Length == 0) continue;
                    uint value = 1;
                    string tag = p;
                    int eq = p.IndexOf('=');
                    if (eq > 0) { tag = p[..eq]; uint.TryParse(p[(eq + 1)..], out value); }
                    else if (p.EndsWith("-")) { tag = p[..^1]; value = 0; }
                    else if (p.EndsWith("+")) { tag = p[..^1]; value = 1; }
                    if (tag.Length >= 1 && tag.Length <= 4)
                        list.Add(new HB.hb_feature_t { tag = Tag4(tag), value = value, start = HB.hb_feature_t.GLOBAL_START, end = HB.hb_feature_t.GLOBAL_END });
                }
            }
            return list.ToArray();
        }

        static uint Tag4(string tag)
        {
            Span<char> c = stackalloc char[4] { ' ', ' ', ' ', ' ' };
            for (int i = 0; i < tag.Length && i < 4; i++) c[i] = tag[i];
            return HB.MakeTag(c[0], c[1], c[2], c[3]);
        }

        static string TagToString(uint tag) =>
            new(new[] { (char)((tag >> 24) & 0xFF), (char)((tag >> 16) & 0xFF), (char)((tag >> 8) & 0xFF), (char)(tag & 0xFF) });



        GlyphExtract GetExtract(uint glyphId)
        {
            if (extractCache.TryGetValue(glyphId, out var ex)) return ex;
            ex = ExtractGlyph(glyphId);
            extractCache[glyphId] = ex;
            return ex;
        }

        /// <summary>
        /// Extracts one glyph in two spaces: FreeType design units (y-up) for line layout, and the
        /// height-normalized [0,1] space that <see cref="ContourUnionBurst"/> and the SDF pipeline consume —
        /// so the before/after-union comparison here matches exactly what the engine rasterizes.
        /// </summary>
        GlyphExtract ExtractGlyph(uint glyphId)
        {
            var ex = new GlyphExtract { glyphId = glyphId };
            if (privateFace == null) return ex;

            ApplyVarCoords();

            var rawCurves = stackalloc float[2048 * 8];
            var rawTypes = stackalloc int[2048];
            var rawContoursPtr = stackalloc int[256];
            int curveCount, cCount;

            int err = FT.OutlineDecompose(privateFace.Pointer, glyphId,
                rawCurves, rawTypes, &curveCount, 2048,
                rawContoursPtr, &cCount, 256,
                out int bearingX, out int bearingY, out int advanceX, out int width, out int height);

            if (err != 0 || curveCount == 0) return ex;

            int upem = Mathf.Max(font.UnitsPerEm, 1);
            ex.upem = upem;
            ex.designW = width; ex.designH = height;
            ex.advance = advanceX; ex.bearingX = bearingX; ex.bearingY = bearingY;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                for (int j = 0; j < 3; j++)
                {
                    float x = c[j * 2], y = c[j * 2 + 1];
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
            ex.minX = minX; ex.minY = minY; ex.maxX = maxX; ex.maxY = maxY;
            ex.bboxW = maxX - minX; ex.bboxH = Mathf.Max(maxY - minY, 1e-6f);

            ex.design = new DiagSegment[curveCount];
            ex.designEnds = BuildEnds(rawContoursPtr, cCount);
            ex.designCount = curveCount; ex.designContours = cCount;
            for (int i = 0; i < curveCount; i++)
            {
                float* c = rawCurves + i * 8;
                ex.design[i] = MakeSeg(c[0], c[1], c[2], c[3], c[4], c[5]);
            }
            AssignContours(ex.design, ex.designEnds, cCount);

            float invScale = 1f / ex.bboxH;
            ex.raw = new DiagSegment[curveCount];
            ex.rawEnds = (int[])ex.designEnds.Clone();
            ex.rawCount = curveCount; ex.rawContours = cCount;
            for (int i = 0; i < curveCount; i++)
            {
                var d = ex.design[i];
                ex.raw[i] = MakeSeg(
                    (d.p0x - minX) * invScale, (d.p0y - minY) * invScale,
                    (d.p1x - minX) * invScale, (d.p1y - minY) * invScale,
                    (d.p2x - minX) * invScale, (d.p2y - minY) * invScale);
            }
            AssignContours(ex.raw, ex.rawEnds, cCount);

            ResolveUnion(ex, rawContoursPtr, cCount);

            ex.junctions = AnalyzeJunctions(ex.raw, ex.rawEnds, ex.rawContours);
            ex.valid = true;
            return ex;
        }

        void ResolveUnion(GlyphExtract ex, int* rawContoursPtr, int cCount)
        {
            var buf = new PooledBuffer<GlyphCurveCache.Segment>();
            buf.EnsureCapacity(ex.rawCount);
            for (int i = 0; i < ex.rawCount; i++)
            {
                var s = ex.raw[i];
                buf.data[i] = new GlyphCurveCache.Segment
                {
                    p0x = s.p0x, p0y = s.p0y, p1x = s.p1x, p1y = s.p1y, p2x = s.p2x, p2y = s.p2y
                };
            }
            buf.count = ex.rawCount;

            int segCount = ex.rawCount;
            int contourCount = cCount;
            var ends = new int[256];
            for (int i = 0; i < cCount; i++) ends[i] = rawContoursPtr[i];

            bool resolved;
            try
            {
                fixed (int* pe = ends)
                    resolved = ContourUnionBurst.TryResolve(ref buf, 0, ref segCount, pe, ref contourCount);
            }
            catch (Exception e)
            {
                resolved = false;
                ex.unionInfo = "threw: " + e.Message;
            }

            ex.unionResolved = resolved;
            if (resolved)
            {
                ex.res = new DiagSegment[segCount];
                for (int i = 0; i < segCount; i++)
                {
                    var s = buf.data[i];
                    ex.res[i] = MakeSeg(s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y);
                }
                ex.resEnds = new int[contourCount];
                Array.Copy(ends, ex.resEnds, contourCount);
                ex.resCount = segCount; ex.resContours = contourCount;
                AssignContours(ex.res, ex.resEnds, contourCount);
                ex.unionChanged = segCount != ex.rawCount || contourCount != ex.rawContours || GeometryDiffers(ex.raw, ex.res, segCount);
                ex.unionInfo ??= ex.unionChanged ? "resolved (geometry rewritten)" : "resolved (no change needed)";
            }
            else
            {
                ex.res = ex.raw; ex.resEnds = ex.rawEnds; ex.resCount = ex.rawCount; ex.resContours = ex.rawContours;
                ex.unionChanged = false;
                ex.unionInfo ??= "bailed (legacy internal marking)";
            }

            buf.Return();
        }

        static bool GeometryDiffers(DiagSegment[] a, DiagSegment[] b, int n)
        {
            for (int i = 0; i < n; i++)
            {
                var x = a[i]; var y = b[i];
                if (Mathf.Abs(x.p0x - y.p0x) > 1e-6f || Mathf.Abs(x.p0y - y.p0y) > 1e-6f ||
                    Mathf.Abs(x.p1x - y.p1x) > 1e-6f || Mathf.Abs(x.p1y - y.p1y) > 1e-6f ||
                    Mathf.Abs(x.p2x - y.p2x) > 1e-6f || Mathf.Abs(x.p2y - y.p2y) > 1e-6f)
                    return true;
            }
            return false;
        }

        void ApplyVarCoords()
        {
            if (axisInfos == null || axisValues == null || axisInfos.Length == 0) return;
            var coords = new int[axisInfos.Length];
            for (int i = 0; i < axisInfos.Length; i++) coords[i] = (int)(axisValues[i] * 65536f);
            fixed (int* p = coords) FT.SetVarDesignCoordinates(privateFace.Pointer, p, coords.Length);
        }

        static int[] BuildEnds(int* rawContours, int cCount)
        {
            var ends = new int[cCount];
            for (int c = 0; c < cCount; c++) ends[c] = rawContours[c];
            return ends;
        }

        static void AssignContours(DiagSegment[] segs, int[] ends, int cCount)
        {
            int cStart = 0;
            for (int c = 0; c < cCount; c++)
            {
                int cEnd = ends[c];
                for (int i = cStart; i <= cEnd && i < segs.Length; i++) segs[i].contourIndex = c;
                cStart = cEnd + 1;
            }
        }

        static DiagSegment MakeSeg(float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
        {
            var s = new DiagSegment { p0x = p0x, p0y = p0y, p1x = p1x, p1y = p1y, p2x = p2x, p2y = p2y };
            float midX = (p0x + p2x) * 0.5f, midY = (p0y + p2y) * 0.5f;
            float dx = p1x - midX, dy = p1y - midY;
            s.isDegenerate = dx * dx + dy * dy < 1e-8f * Mathf.Max(1f, (p2x - p0x) * (p2x - p0x) + (p2y - p0y) * (p2y - p0y));
            return s;
        }

        JunctionInfo[] AnalyzeJunctions(DiagSegment[] segs, int[] ends, int cCount)
        {
            var list = new List<JunctionInfo>();
            int cStart = 0;
            for (int c = 0; c < cCount; c++)
            {
                int cEnd = ends[c];
                int edgeCount = cEnd - cStart + 1;
                for (int i = 0; i < edgeCount; i++)
                {
                    int idxA = cStart + i;
                    int idxB = cStart + (i + 1) % edgeCount;
                    var sA = segs[idxA]; var sB = segs[idxB];
                    Vector2 posA = new(sA.p2x, sA.p2y);
                    Vector2 posB = new(sB.p0x, sB.p0y);
                    Vector2 tanA = new(2f * (sA.p2x - sA.p1x), 2f * (sA.p2y - sA.p1y));
                    Vector2 tanB = new(2f * (sB.p1x - sB.p0x), 2f * (sB.p1y - sB.p0y));
                    float magA = tanA.magnitude, magB = tanB.magnitude, angle = 0f;
                    if (magA > 1e-6f && magB > 1e-6f)
                    {
                        tanA /= magA; tanB /= magB;
                        angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(tanA, tanB), -1f, 1f)) * Mathf.Rad2Deg;
                    }
                    list.Add(new JunctionInfo
                    {
                        segA = idxA, segB = idxB, posA = posA, posB = posB,
                        posError = Vector2.Distance(posA, posB), tangentA = tanA, tangentB = tanB, angleDeg = angle
                    });
                }
                cStart = cEnd + 1;
            }
            return list.ToArray();
        }

        /// <summary>
        /// Cheap per-frame refresh: only re-points the active source and invalidates the heavy SDF/fill/data
        /// artifacts when the selected glyph id, SDF source, or font/variation actually changes — so typing
        /// (which keeps the selected glyph) never triggers regeneration.
        /// </summary>
        void RefreshViewedGlyph()
        {
            if (run.Length == 0) return;
            selected = Mathf.Clamp(selected, 0, run.Length - 1);
            var ex = GetExtract(run[selected].glyphId);
            if (!ex.valid) return;

            bool glyphChanged = ex.glyphId != viewedGlyphId || viewForceRefresh;
            if (glyphChanged || sdfSourceRes != viewedSource)
            {
                viewedGlyphId = ex.glyphId;
                viewedSource = sdfSourceRes;
                viewForceRefresh = false;
                SetActiveSource(ex, sdfSourceRes == 1);
                DropSelectedTextures();
                if (glyphChanged) frameQueued = true;
            }
        }

        void DropSelectedTextures()
        {
            DestroyTex(ref sdfGray); DestroyTex(ref sdfContour); DestroyTex(ref sdfSeg);
            DestroyTex(ref sdfWinding); DestroyTex(ref fillTex);
            dataText = "";
        }

        void SetActiveSource(GlyphExtract ex, bool useRaw)
        {
            segments = useRaw ? ex.raw : ex.res;
            segmentCount = useRaw ? ex.rawCount : ex.resCount;
            contourEndIndices = useRaw ? ex.rawEnds : ex.resEnds;
            contourCount = useRaw ? ex.rawContours : ex.resContours;
            aspect = ex.bboxW / ex.bboxH;
            glyphH = ex.designH / (float)ex.upem;
            junctions = ex.junctions;
            curveData = new GlyphCurveCache.GlyphCurveData
            {
                bboxMinX = ex.minX, bboxMinY = ex.minY, bboxMaxX = ex.maxX, bboxMaxY = ex.maxY,
                bearingX = ex.bearingX / ex.upem, bearingY = ex.bearingY / ex.upem, advanceX = ex.advance / ex.upem,
                designWidth = ex.designW, designHeight = ex.designH, isEmpty = false
            };
        }



        static float AspectOf(GlyphExtract ex) => ex.bboxW / ex.bboxH;

        static string SafeChar(uint cp) =>
            cp >= 0x20 && Utf16.IsUnicodeScalar((int)cp) ? char.ConvertFromUtf32((int)cp) : "·";



        void FrameView(Rect rect, GlyphExtract ex)
        {
            float w, h, cx, cy;
            if (viewMode == ViewMode.Single)
            {
                w = Mathf.Max(AspectOf(ex), 0.1f); h = 1f; cx = w * 0.5f; cy = 0.5f;
            }
            else
            {
                ComputeLineExtents(out float minx, out float maxx, out float miny, out float maxy);
                w = Mathf.Max(maxx - minx, 0.1f); h = Mathf.Max(maxy - miny, 0.1f);
                cx = (minx + maxx) * 0.5f; cy = (miny + maxy) * 0.5f;
            }
            float pad = 0.85f;
            viewScale = Mathf.Min((rect.width - 44f) / w, (rect.height - 44f) / h) * pad;
            viewOffset.x = rect.width * 0.5f - cx * viewScale;
            viewOffset.y = rect.height * 0.5f + cy * viewScale;
        }



        void ComputeLineExtents(out float minX, out float maxX, out float minY, out float maxY)
        {
            minX = 0; maxX = 0; minY = 0; maxY = 0;
            float penX = 0, penY = 0;
            bool any = false;
            bool rtl = ResolveDirection(scripts[scriptIdx].tag) == HB.DIRECTION_RTL;
            for (int i = 0; i < run.Length; i++)
            {
                var g = run[i];
                var ex = GetExtract(g.glyphId);
                float upem = ex.valid ? ex.upem : Mathf.Max(font.UnitsPerEm, 1);
                float ox = (penX + g.xOffset) / upem;
                float oy = (penY + g.yOffset) / upem;
                if (ex.valid)
                {
                    float gx0 = ox + ex.minX / upem, gx1 = ox + ex.maxX / upem;
                    float gy0 = oy + ex.minY / upem, gy1 = oy + ex.maxY / upem;
                    if (!any) { minX = gx0; maxX = gx1; minY = gy0; maxY = gy1; any = true; }
                    else { minX = Mathf.Min(minX, gx0); maxX = Mathf.Max(maxX, gx1); minY = Mathf.Min(minY, gy0); maxY = Mathf.Max(maxY, gy1); }
                }
                penX += rtl ? -g.xAdvance : g.xAdvance;
                penY += g.yAdvance;
            }
            if (!any) { minX = 0; maxX = 1; minY = 0; maxY = 1; }
            minY = Mathf.Min(minY, 0); maxY = Mathf.Max(maxY, 1);
        }

        void PickLineGlyph(Vector2 local)
        {
            float penX = 0, penY = 0;
            bool rtl = ResolveDirection(scripts[scriptIdx].tag) == HB.DIRECTION_RTL;
            Vector2 g = ToolkitInvMap(local);
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < run.Length; i++)
            {
                var rg = run[i];
                var ex = GetExtract(rg.glyphId);
                float upem = ex.valid ? ex.upem : Mathf.Max(font.UnitsPerEm, 1);
                float ox = (penX + rg.xOffset) / upem;
                if (ex.valid)
                {
                    float cx = ox + (ex.minX + ex.maxX) * 0.5f / upem;
                    float d = Mathf.Abs(g.x - cx);
                    if (d < bestD) { bestD = d; best = i; }
                }
                penX += rtl ? -rg.xAdvance : rg.xAdvance;
                penY += rg.yAdvance;
            }
            if (best >= 0) selected = best;
        }





        struct Mono { public float p0x, p0y, p1x, p1y, p2x, p2y; public float yMin, yMax; public int dir; public bool linear; }

        /// <summary>
        /// Supersampled nonzero-winding coverage of the glyph fill. Row 0 stores y=0 (bottom), matching
        /// <see cref="Texture2D.SetPixels"/>' origin so the retained-mode preview remains upright.
        /// </summary>
        void GenerateFillTexture(GlyphExtract ex)
        {
            var src = showResolved ? ex.res : ex.raw;
            int srcCount = showResolved ? ex.resCount : ex.rawCount;
            float asp = AspectOf(ex);

            int H = 320;
            int W = Mathf.Clamp(Mathf.RoundToInt(H * Mathf.Max(asp, 0.05f)), 8, 1024);
            const int SS = 1;

            var mono = new List<Mono>(srcCount * 2);
            for (int i = 0; i < srcCount; i++) SplitMono(src[i], mono);

            var px = new Color[W * H];
            var fillCol = new Color(0.55f, 0.72f, 0.95f, 1f);
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        float gx = (c + (sx + 0.5f) / SS) / W * asp;
                        float gy = (r + (sy + 0.5f) / SS) / H;
                        if (WindingAt(mono, gx, gy) != 0) inside++;
                    }
                    float cov = inside / (float)(SS * SS);
                    px[r * W + c] = new Color(fillCol.r, fillCol.g, fillCol.b, cov * 0.22f);
                }
            }

            DestroyTex(ref fillTex);
            fillTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            fillTex.SetPixels(px);
            fillTex.Apply();
        }

        static void SplitMono(DiagSegment s, List<Mono> outp)
        {
            float denom = s.p0y - 2f * s.p1y + s.p2y;
            float tSplit = Mathf.Abs(denom) > 1e-10f ? (s.p0y - s.p1y) / denom : -1f;
            if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
            {
                float t = tSplit, mt = 1f - t;
                float m01x = mt * s.p0x + t * s.p1x, m01y = mt * s.p0y + t * s.p1y;
                float m12x = mt * s.p1x + t * s.p2x, m12y = mt * s.p1y + t * s.p2y;
                float mx = mt * m01x + t * m12x, my = mt * m01y + t * m12y;
                AddMono(outp, s.p0x, s.p0y, m01x, m01y, mx, my);
                AddMono(outp, mx, my, m12x, m12y, s.p2x, s.p2y);
            }
            else AddMono(outp, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y);
        }

        static void AddMono(List<Mono> outp, float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
        {
            var m = new Mono
            {
                p0x = p0x, p0y = p0y, p1x = p1x, p1y = p1y, p2x = p2x, p2y = p2y,
                yMin = Mathf.Min(p0y, p2y), yMax = Mathf.Max(p0y, p2y), dir = p2y > p0y ? 1 : -1
            };
            float d01x = p1x - p0x, d01y = p1y - p0y, d02x = p2x - p0x, d02y = p2y - p0y;
            m.linear = Mathf.Abs(d01x * d02y - d01y * d02x) < 1e-5f;
            outp.Add(m);
        }

        static int WindingAt(List<Mono> mono, float px, float py)
        {
            int w = 0;
            for (int i = 0; i < mono.Count; i++)
            {
                var m = mono[i];
                if (py < m.yMin || py >= m.yMax) continue;
                float xHit;
                if (m.linear)
                {
                    float dy = m.p2y - m.p0y;
                    if (Mathf.Abs(dy) < 1e-9f) continue;
                    float t = (py - m.p0y) / dy;
                    xHit = m.p0x + t * (m.p2x - m.p0x);
                }
                else
                {
                    float a = m.p0y - 2f * m.p1y + m.p2y;
                    float b = 2f * (m.p1y - m.p0y);
                    float c = m.p0y - py;
                    if (SolveQuad(a, b, c, out float t0, out float t1) == 0) continue;
                    float t = t0 >= 0f && t0 <= 1f ? t0 : t1;
                    if (t < 0f || t > 1f) continue;
                    float mt = 1f - t;
                    xHit = mt * mt * m.p0x + 2f * mt * t * m.p1x + t * t * m.p2x;
                }
                if (xHit > px) w += m.dir;
            }
            return w;
        }



        struct MonoPiece { public float p0x, p0y, p1x, p1y, p2x, p2y; public float yMin, yMax; public int windingDir; public bool isLinear; }

        void GenerateBruteForceSdf()
        {
            int ts = tileSizes[tileSizeIdx];

            float padGlyph = GlyphAtlas.TileFitPad;
            float maxDim = Mathf.Max(aspect, 1f);
            float baseExtent = maxDim + 2f * padGlyph;
            float gutter = baseExtent / ts;
            float totalExtent = baseExtent + 2f * gutter;
            float scale = ts / totalExtent;
            float offsetX = (maxDim - aspect) * 0.5f + padGlyph + gutter;
            float offsetY = (maxDim - 1f) * 0.5f + padGlyph + gutter;
            float invScale = 1f / scale;

            int pixelCount = ts * ts;
            bfDist = new float[pixelCount];
            bfSeg = new int[pixelCount];
            bfWind = new byte[pixelCount];

            var pieces = new List<MonoPiece>();
            for (int si = 0; si < segmentCount; si++) YMonoSplit(segments[si], pieces);
            ComputeWinding(pieces, ts, scale, offsetX, offsetY);

            for (int y = 0; y < ts; y++)
            for (int x = 0; x < ts; x++)
            {
                float px = (x + 0.5f) * invScale - offsetX;
                float py = (y + 0.5f) * invScale - offsetY;
                float bestDist2 = float.MaxValue; int bestSeg = -1;
                for (int si = 0; si < segmentCount; si++)
                {
                    float d2 = ClosestDistSq(px, py, segments[si]);
                    if (d2 < bestDist2) { bestDist2 = d2; bestSeg = si; }
                }
                int idx = y * ts + x;
                float sign = bfWind[idx] != 0 ? -1f : 1f;
                bfDist[idx] = Mathf.Clamp01(sign * Mathf.Sqrt(bestDist2) * glyphH + 0.5f);
                bfSeg[idx] = bestSeg;
            }

            BuildSdfTextures(ts);
        }

        void BuildSdfTextures(int ts)
        {
            DestroyTex(ref sdfGray); DestroyTex(ref sdfContour); DestroyTex(ref sdfSeg); DestroyTex(ref sdfWinding);
            sdfGray = NewTex(ts); sdfContour = NewTex(ts); sdfSeg = NewTex(ts); sdfWinding = NewTex(ts);

            var g = new Color[ts * ts]; var ct = new Color[ts * ts]; var sg = new Color[ts * ts]; var wd = new Color[ts * ts];
            for (int y = 0; y < ts; y++)
            for (int x = 0; x < ts; x++)
            {
                int idx = y * ts + x;
                float v = bfDist[idx];
                int seg = bfSeg[idx];
                bool inside = bfWind[idx] != 0;
                g[idx] = new Color(v, v, v, 1f);
                sg[idx] = seg >= 0 ? segColors[seg % segColors.Length] : Color.black;
                wd[idx] = inside ? new Color(0.12f, 0.32f, 0.6f) : new Color(0.9f, 0.9f, 0.9f);
                bool edge = (x > 0 && (v - 0.5f) * (bfDist[idx - 1] - 0.5f) < 0) ||
                            (y > 0 && (v - 0.5f) * (bfDist[(y - 1) * ts + x] - 0.5f) < 0);
                ct[idx] = edge ? Color.red : new Color(v, v, v, 1f);
            }
            sdfGray.SetPixels(g); sdfGray.Apply();
            sdfContour.SetPixels(ct); sdfContour.Apply();
            sdfSeg.SetPixels(sg); sdfSeg.Apply();
            sdfWinding.SetPixels(wd); sdfWinding.Apply();
        }

        static Texture2D NewTex(int ts) => new(ts, ts, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        static void YMonoSplit(DiagSegment s, List<MonoPiece> outp)
        {
            float denom = s.p0y - 2f * s.p1y + s.p2y;
            float tSplit = Mathf.Abs(denom) > 1e-10f ? (s.p0y - s.p1y) / denom : -1f;
            if (tSplit > 1e-6f && tSplit < 1f - 1e-6f)
            {
                float t = tSplit, mt = 1f - t;
                float m01x = mt * s.p0x + t * s.p1x, m01y = mt * s.p0y + t * s.p1y;
                float m12x = mt * s.p1x + t * s.p2x, m12y = mt * s.p1y + t * s.p2y;
                float mx = mt * m01x + t * m12x, my = mt * m01y + t * m12y;
                AddPiece(outp, s.p0x, s.p0y, m01x, m01y, mx, my);
                AddPiece(outp, mx, my, m12x, m12y, s.p2x, s.p2y);
            }
            else AddPiece(outp, s.p0x, s.p0y, s.p1x, s.p1y, s.p2x, s.p2y);
        }

        static void AddPiece(List<MonoPiece> outp, float p0x, float p0y, float p1x, float p1y, float p2x, float p2y)
        {
            var m = new MonoPiece
            {
                p0x = p0x, p0y = p0y, p1x = p1x, p1y = p1y, p2x = p2x, p2y = p2y,
                yMin = Mathf.Min(p0y, p2y), yMax = Mathf.Max(p0y, p2y), windingDir = p2y > p0y ? 1 : -1
            };
            float d01x = p1x - p0x, d01y = p1y - p0y, d02x = p2x - p0x, d02y = p2y - p0y;
            m.isLinear = Mathf.Abs(d01x * d02y - d01y * d02x) < 1e-5f;
            outp.Add(m);
        }

        void ComputeWinding(List<MonoPiece> pieces, int ts, float scale, float offsetX, float offsetY)
        {
            float invScale = 1f / scale;
            for (int y = 0; y < ts; y++)
            {
                float py = (y + 0.5f) * invScale - offsetY;
                var windingRow = new int[ts];
                for (int mi = 0; mi < pieces.Count; mi++)
                {
                    var seg = pieces[mi];
                    if (py < seg.yMin || py >= seg.yMax) continue;
                    float xPx;
                    if (seg.isLinear)
                    {
                        float dy = seg.p2y - seg.p0y;
                        if (Mathf.Abs(dy) < 1e-9f) continue;
                        float t = (py - seg.p0y) / dy;
                        xPx = (seg.p0x + t * (seg.p2x - seg.p0x) + offsetX) * scale;
                    }
                    else
                    {
                        float a = seg.p0y - 2f * seg.p1y + seg.p2y;
                        float b = 2f * (seg.p1y - seg.p0y);
                        float c = seg.p0y - py;
                        if (SolveQuad(a, b, c, out float t0, out _) == 0 || t0 < 0f || t0 > 1f) continue;
                        float mt = 1f - t0;
                        xPx = (mt * mt * seg.p0x + 2f * mt * t0 * seg.p1x + t0 * t0 * seg.p2x + offsetX) * scale;
                    }
                    int ix = (int)(xPx + 0.5f);
                    if (ix >= 0 && ix < ts) windingRow[ix] += seg.windingDir;
                }
                int winding = 0;
                for (int x = 0; x < ts; x++) { winding += windingRow[x]; bfWind[y * ts + x] = winding != 0 ? (byte)1 : (byte)0; }
            }
        }

        static int SolveQuad(float a, float b, float c, out float t0, out float t1)
        {
            t0 = t1 = -1f;
            if (Mathf.Abs(a) < 1e-8f)
            {
                if (Mathf.Abs(b) < 1e-8f) return 0;
                t0 = -c / b;
                return t0 >= 0f && t0 <= 1f ? 1 : 0;
            }
            float disc = b * b - 4f * a * c;
            if (disc < -1e-7f) return 0;
            if (disc < 0f) disc = 0f;
            float sq = Mathf.Sqrt(disc);
            float q = -0.5f * (b + (b >= 0f ? sq : -sq));
            if (Mathf.Abs(q) < 1e-12f) { t0 = 0f; t1 = -b / a; }
            else { t0 = q / a; t1 = c / q; }
            bool v0 = t0 >= 0f && t0 <= 1f, v1 = t1 >= 0f && t1 <= 1f;
            if (v0 && v1) return 2;
            if (v0) return 1;
            if (v1) { t0 = t1; return 1; }
            return 0;
        }

        static float ClosestDistSq(float px, float py, DiagSegment s)
        {
            float bestT = 0f, bestD2 = float.MaxValue;
            const int steps = 20;
            for (int j = 0; j <= steps; j++)
            {
                float t = j / (float)steps, mt = 1f - t;
                float bx = mt * mt * s.p0x + 2f * mt * t * s.p1x + t * t * s.p2x;
                float by = mt * mt * s.p0y + 2f * mt * t * s.p1y + t * t * s.p2y;
                float dx = bx - px, dy = by - py, d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; bestT = t; }
            }
            for (int it = 0; it < 4; it++) bestT = NewtonStep(px, py, s, bestT);
            float mtn = 1f - bestT;
            float vx = mtn * mtn * s.p0x + 2f * mtn * bestT * s.p1x + bestT * bestT * s.p2x - px;
            float vy = mtn * mtn * s.p0y + 2f * mtn * bestT * s.p1y + bestT * bestT * s.p2y - py;
            return vx * vx + vy * vy;
        }

        static float NewtonStep(float px, float py, DiagSegment s, float t)
        {
            float ax = s.p0x, ay = s.p0y, bx = s.p1x, by = s.p1y, cx = s.p2x, cy = s.p2y;
            float mt = 1f - t;
            float dpx = 2f * ((bx - ax) + (ax - 2f * bx + cx) * t);
            float dpy = 2f * ((by - ay) + (ay - 2f * by + cy) * t);
            float btx = mt * mt * ax + 2f * mt * t * bx + t * t * cx;
            float bty = mt * mt * ay + 2f * mt * t * by + t * t * cy;
            float diffx = btx - px, diffy = bty - py;
            float dpSq = dpx * dpx + dpy * dpy;
            if (dpSq < 1e-6f) return t;
            float ddpx = 2f * (ax - 2f * bx + cx), ddpy = 2f * (ay - 2f * by + cy);
            float f = diffx * dpx + diffy * dpy;
            float fp = dpSq + diffx * ddpx + diffy * ddpy;
            if (Mathf.Abs(fp) < 1e-12f) return t;
            float tn = t - f / fp;
            return tn < 0f ? 0f : tn > 1f ? 1f : tn;
        }






        void BuildDataText(GlyphExtract ex)
        {
            var sb = new StringBuilder();
            var g = run[selected];
            sb.AppendLine("═══ GLYPH ═══");
            sb.AppendLine($"run index : {selected} / {run.Length}");
            sb.AppendLine($"glyph id  : {ex.glyphId}");
            sb.AppendLine($"cluster   : {g.cluster}   source U+{g.srcCp:X4}");
            sb.AppendLine($"advance   : {g.xAdvance} (shaped)   {ex.advance} (design)");
            sb.AppendLine($"offset    : ({g.xOffset}, {g.yOffset})");
            sb.AppendLine($"bearing   : ({ex.bearingX}, {ex.bearingY})   design {ex.designW}x{ex.designH}");
            sb.AppendLine($"bbox      : ({ex.minX:F0},{ex.minY:F0}) - ({ex.maxX:F0},{ex.maxY:F0})   upem {ex.upem}");
            sb.AppendLine($"aspect    : {aspect:F4}   glyphH {glyphH:F4}");
            sb.AppendLine();

            sb.AppendLine("═══ UNION ═══");
            sb.AppendLine($"status  : {(ex.unionResolved ? (ex.unionChanged ? "rewritten" : "no-op") : "BAILED")}");
            sb.AppendLine($"info    : {ex.unionInfo}");
            sb.AppendLine($"raw     : {ex.rawCount} segs / {ex.rawContours} contours");
            sb.AppendLine($"union   : {ex.resCount} segs / {ex.resContours} contours");
            sb.AppendLine();

            sb.AppendLine($"═══ SEGMENTS ({(sdfSourceRes == 1 ? "raw" : "union")}) ═══");
            sb.AppendLine($"{"idx",4} {"ctr",3} {"type",5}  p0 / ctrl / p2");
            for (int i = 0; i < segmentCount; i++)
            {
                var s = segments[i];
                sb.AppendLine($"{i,4} {s.contourIndex,3} {(s.isDegenerate ? "line " : "curve")}  " +
                    $"({s.p0x:F4},{s.p0y:F4}) ({s.p1x:F4},{s.p1y:F4}) ({s.p2x:F4},{s.p2y:F4})");
            }

            sb.AppendLine();
            sb.AppendLine("═══ JUNCTIONS ═══");
            if (junctions != null)
                foreach (var j in junctions)
                {
                    string st = j.posError > 1e-3f ? "GAP" : j.angleDeg > 5f ? "CORNER" : j.angleDeg > 1f ? "KINK" : "smooth";
                    sb.AppendLine($"  {j.segA,3}->{j.segB,-3} angle {j.angleDeg,7:F2}  gap {j.posError:E2}  {st}");
                }

            dataText = sb.ToString();
        }
    }
}
