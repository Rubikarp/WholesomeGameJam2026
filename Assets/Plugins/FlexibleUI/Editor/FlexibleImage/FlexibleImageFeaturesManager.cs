using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
[Serializable] public class ShaderFeatureEntry
{
    public bool IsEnabled;
    //public bool IsFoldoutOpen;
    public string Define;
    public List<ShaderFeatureEntry> SubFeatures = new();
    public bool HasSubFeatures => SubFeatures != null && SubFeatures.Any();
}

public enum ProceduralGradientPatternOrder
{
    ProceduralGradientBeforePattern,
    PatternBeforeProceduralGradient
}

public class FlexibleImageAssetPostprocessor : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (importedAssets.Any(x => x.Contains("ProceduralBlurredImage.shader", StringComparison.InvariantCultureIgnoreCase)))
            FlexibleImageFeatureManager.RestoreFromSessionCache();
    }
}

[InitializeOnLoad] public static class FlexibleImageFeatureManager
{
    private const int SUB_FEATURE_INDENT_SPACES = 12;
    private const string SessionKey = "FlexibleUI_FeatureStates";

    public const string SkewFeatureID = "FeatureSkew";
    public const string StrokeFeatureID = "FeatureStroke";
    public const string CutoutFeatureID = "FeatureCutout";
    public const string CutoutSimpleSubFeatureID = "SubFeatureCutoutSimple";
    public const string CutoutSdfSubFeatureID = "SubFeatureCutoutSDF";
    public const string OutlineFeatureID = "FeatureOutline";
    public const string ProceduralGradientFeatureID = "FeatureProceduralGradient";
    public const string ProceduralGradientSDFSubFeatureID = "SubFeatureProceduralGradientSDF";
    public const string ProceduralGradientAngleSubFeatureID = "SubFeatureProceduralGradientAngle";
    public const string ProceduralGradientRadialSubFeatureID = "SubFeatureProceduralGradientRadial";
    public const string ProceduralGradientConicalSubFeatureID = "SubFeatureProceduralGradientConical";
    public const string ProceduralGradientNoiseSubFeatureID = "SubFeatureProceduralGradientNoise";
    public const string ProceduralGradientScreenSpaceSubFeatureID = "SubFeatureProceduralGradientScreenSpaceOption";
    public const string ProceduralGradientPointerAdjustPosSubFeatureID = "SubFeatureProceduralGradientPointerAdjustPosOption";
    public const string PatternFeatureID = "FeaturePattern";
    public const string PatternLineSubFeatureID = "SubFeaturePatternLine";
    public const string PatternShapeSubFeatureID = "SubFeaturePatternShape";
    public const string PatternGridSubFeatureID = "SubFeaturePatternGrid";
    public const string PatternFractalSubFeatureID = "SubFeaturePatternFractal";
    public const string PatternSpriteSubFeatureID = "SubFeaturePatternSprite";
    public const string PatternScreenSpaceSubFeatureID = "SubFeaturePatternScreenSpaceOption";

    private const string ShaderName = "Hidden/JeffGrawAssets/ProceduralBlurredImage";
    private const string BlockStart = "/// Feature Flags";
    private const string BlockEnd = "/// End Feature Flags";
    private const string SubPrefix = "SubFeature";
    private const string SectionGradient = "ProceduralGradient";
    private const string SectionPattern = "Pattern";

    private static List<ShaderFeatureEntry> cachedFeatures;
    
    private static Dictionary<string, string> cachedShaderPaths = new();
    private static ProceduralGradientPatternOrder? cachedOrder;
    private static readonly Regex DefineRegex = new(@"^(?<indent>\s*)(?<disabled>//\s*)?#define\s+(?<name>\w+)\s*;?\s*$", RegexOptions.Compiled);
    private static readonly Regex SectionMarker = new(@"^\s*///\s*\[SECTION:(?<name>\w+):(?<tag>BEGIN|END)\]", RegexOptions.Compiled);

    public const string SoftMaskConditionalID = "SOFTMASK";
    private static Dictionary<string, bool> cachedConditionalBlocks;
    private static readonly Regex ConditionalMarker = new(@"^\s*///\s+(?<name>\w+?)_(?<tag>NOT_PRESENT|PRESENT|END)\s*$", RegexOptions.Compiled);

    static FlexibleImageFeatureManager() => WriteSessionCache();

    public static List<ShaderFeatureEntry> GetFeatures()
    {
        if (cachedFeatures == null)
            Reload();

        return cachedFeatures;
    }

    public static bool IsFeatureEnabled(string defineName)
    {
        foreach (var f in GetFeatures())
        {
            if (f.Define == defineName)
                return f.IsEnabled;

            foreach (var s in f.SubFeatures)
                if (s.Define == defineName)
                    return s.IsEnabled;
        }

        return false;
    }

    public static void SetFeatureEnabled(string defineName, bool enabled, bool recompile = true)
    {
        var path = GetShaderPath();
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError($"Could not locate shader '{ShaderName}'.");
            return;
        }

        var lines = File.ReadAllLines(path);
        var changed = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = DefineRegex.Match(line);

            if (!match.Success || match.Groups["name"].Value != defineName)
                continue;

            if (string.IsNullOrWhiteSpace(match.Groups["disabled"].Value) == enabled)
                return;

            var indent = match.Groups["indent"].Value;
            var baseDefineLine = $"#define {defineName}";
            var targetIndent = defineName.StartsWith(SubPrefix) ? new string(' ', SUB_FEATURE_INDENT_SPACES) : indent;
            var newLine = enabled ? $"{targetIndent}{baseDefineLine}" : $"{targetIndent}// {baseDefineLine}";

            lines[i] = newLine;
            changed = true;
            break;
        }

        if (!changed)
            return;

        File.WriteAllLines(path, lines, Encoding.UTF8);
        UpdateCacheEntry(defineName, enabled);
        if (recompile)
            RecompileShader(path);
    }

    public static void RecompileShader(string path = null)
    {
        lastWriteShaderFrame = Time.frameCount;
        AssetDatabase.ImportAsset(path ?? GetShaderPath());
    }

    public static ProceduralGradientPatternOrder GetSectionOrder()
    {
        if (cachedOrder.HasValue)
            return cachedOrder.Value;

        var path = GetShaderPath();
        if (string.IsNullOrEmpty(path))
        {
            cachedOrder = ProceduralGradientPatternOrder.ProceduralGradientBeforePattern;
            return cachedOrder.Value;
        }

        foreach (var line in File.ReadLines(path))
        {
            var m = SectionMarker.Match(line);
            if (!m.Success || m.Groups["tag"].Value != "BEGIN")
                continue;

            cachedOrder = m.Groups["name"].Value == SectionGradient
                ? ProceduralGradientPatternOrder.ProceduralGradientBeforePattern
                : ProceduralGradientPatternOrder.PatternBeforeProceduralGradient;

            return cachedOrder.Value;
        }

        cachedOrder = ProceduralGradientPatternOrder.ProceduralGradientBeforePattern;
        return cachedOrder.Value;
    }

    public static void SetSectionOrder(ProceduralGradientPatternOrder order, bool recompile = true)
    {
        if (GetSectionOrder() == order)
            return;

        var path = GetShaderPath();
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[ShaderFeatureManager] Could not locate shader for section reorder.");
            return;
        }

        var allText = File.ReadAllText(path);

        var gradientBlock = ExtractSection(allText, SectionGradient);
        var patternBlock = ExtractSection(allText, SectionPattern);

        if (gradientBlock == null || patternBlock == null)
        {
            Debug.LogError("[ShaderFeatureManager] Could not find section markers in shader.");
            return;
        }

        var first  = order == ProceduralGradientPatternOrder.ProceduralGradientBeforePattern ? gradientBlock : patternBlock;
        var second = order == ProceduralGradientPatternOrder.ProceduralGradientBeforePattern ? patternBlock  : gradientBlock;

        var spanStart = FindSectionSpanStart(allText, SectionGradient, SectionPattern);
        var spanEnd   = FindSectionSpanEnd(allText, SectionGradient, SectionPattern);

        if (spanStart < 0 || spanEnd < 0)
        {
            Debug.LogError("[ShaderFeatureManager] Marker span detection failed.");
            return;
        }

        var lineEnding = DetectLineEnding(allText);
        var newText = allText.Substring(0, spanStart)
                      + first + lineEnding
                      + second
                      + allText.Substring(spanEnd);

        cachedOrder = order;
        File.WriteAllText(path, newText, Encoding.UTF8);
        if (recompile) 
            RecompileShader(path);
    }

    public static bool IsConditionalBlockEnabled(string featureName)
    {
        if (cachedConditionalBlocks != null && cachedConditionalBlocks.TryGetValue(featureName, out var cached))
            return cached;

        var result = ReadConditionalBlockState(featureName);
        (cachedConditionalBlocks ??= new())[featureName] = result;
        return result;
    }

    public static void SetConditionalBlockEnabled(string featureName, bool enabled, bool recompile = true)
    {
        if (IsConditionalBlockEnabled(featureName) == enabled)
            return;

        var path = GetShaderPath();
        var lines = File.ReadAllLines(path);
        ApplyConditionalBlock(lines, featureName, enabled);
        File.WriteAllLines(path, lines, Encoding.UTF8);
        cachedConditionalBlocks[featureName] = enabled;
        if (recompile)
            RecompileShader(path);
    }

    public static void Reload()
    {
        cachedShaderPaths.Clear();
        cachedConditionalBlocks = null;
        cachedFeatures = null;
        cachedOrder = null;

        string path = GetShaderPath();
        cachedFeatures = string.IsNullOrEmpty(path) ? new List<ShaderFeatureEntry>() : ParseFeatureBlock(File.ReadAllLines(path));
    }

    public static string GetShaderPath(string shaderName = ShaderName)
    {
        if (cachedShaderPaths.TryGetValue(shaderName, out var shaderPath))
            return shaderPath;

        var shader = Shader.Find(ShaderName);
        if (shader != null)
            return cachedShaderPaths[ShaderName] = AssetDatabase.GetAssetPath(shader);

        var guids = AssetDatabase.FindAssets("t:Shader");
        foreach (string guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var candidate = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (candidate == null || candidate.name != ShaderName)
                continue;

            return cachedShaderPaths[ShaderName] = path;
        }
        return null;
    }

    private static List<ShaderFeatureEntry> ParseFeatureBlock(string[] lines)
    {
        var features = new List<ShaderFeatureEntry>();
        var inBlock = false;

        foreach (string rawLine in lines)
        {
            var trimmed = rawLine.TrimStart();
            if (!inBlock)
            {
                if (trimmed.StartsWith(BlockStart))
                    inBlock = true;

                continue;
            }

            if (trimmed.StartsWith(BlockEnd))
                break;

            var m = DefineRegex.Match(rawLine);
            if (!m.Success)
                continue;

            var name = m.Groups["name"].Value;
            var enabled = string.IsNullOrWhiteSpace(m.Groups["disabled"].Value);

            var entry = new ShaderFeatureEntry
            {
                Define = name,
                IsEnabled = enabled
            };

            if (name.StartsWith(SubPrefix))
            {
                if (features.Count > 0)
                    features[^1].SubFeatures.Add(entry);
            }
            else
            {
                features.Add(entry);
            }
        }
        return features;
    }

    private static void UpdateCacheEntry(string defineName, bool enabled)
    {
        if (cachedFeatures == null)
            return;

        foreach (var feature in cachedFeatures)
        {
            if (feature.Define == defineName)
            {
                feature.IsEnabled = enabled;
                return;
            }

            foreach (var subFeature in feature.SubFeatures)
            {
                if (subFeature.Define == defineName)
                {
                    subFeature.IsEnabled = enabled;
                    return;
                }
            }
        }
    }

    private static string ExtractSection(string text, string sectionName)
    {
        var beginTag = $"/// [SECTION:{sectionName}:BEGIN]";
        var endTag   = $"/// [SECTION:{sectionName}:END]";

        var s = text.IndexOf(beginTag, StringComparison.Ordinal);
        var e = text.IndexOf(endTag,   StringComparison.Ordinal);
        if (s < 0 || e < 0)
            return null;

        e += endTag.Length;
        return text.Substring(s, e - s);
    }

    private static int FindSectionSpanStart(string text, string a, string b)
    {
        var ia = text.IndexOf($"/// [SECTION:{a}:BEGIN]", StringComparison.Ordinal);
        var ib = text.IndexOf($"/// [SECTION:{b}:BEGIN]", StringComparison.Ordinal);
        if (ia < 0)
            return ib;
        if (ib < 0)
            return ia;
        return Math.Min(ia, ib);
    }

    private static int FindSectionSpanEnd(string text, string a, string b)
    {
        var endA = $"/// [SECTION:{a}:END]";
        var endB = $"/// [SECTION:{b}:END]";
        var ia = text.IndexOf(endA, StringComparison.Ordinal);
        var ib = text.IndexOf(endB, StringComparison.Ordinal);
        return Math.Max(ia + endA.Length, ib + endB.Length);
    }

    private static string DetectLineEnding(string text)
    {
        var i = text.IndexOf('\n');
        return i > 0 && text[i - 1] == '\r' ? "\r\n" : "\n";
    }

    private static bool ReadConditionalBlockState(string featureName)
    {
        var path = GetShaderPath();
        if (string.IsNullOrEmpty(path))
            return false;

        var inPresent = false;
        foreach (var line in File.ReadLines(path))
        {
            var m = ConditionalMarker.Match(line);
            if (m.Success && m.Groups["name"].Value == featureName)
            {
                var tag = m.Groups["tag"].Value;
                inPresent = tag == "PRESENT";
                if (tag == "END") break;
                continue;
            }

            if (inPresent)
                return !line.TrimStart().StartsWith("//");
        }
        return false;
    }

    private static void ApplyConditionalBlock(string[] lines, string featureName, bool enabled)
    {
        var section = "";
        for (var i = 0; i < lines.Length; i++)
        {
            var m = ConditionalMarker.Match(lines[i]);
            if (m.Success && m.Groups["name"].Value == featureName)
            {
                section = m.Groups["tag"].Value == "END" ? "" : m.Groups["tag"].Value;
                continue;
            }
            if (string.IsNullOrEmpty(section))
                continue;

            lines[i] = SetLineCommented(lines[i], section == "PRESENT" ? !enabled : enabled);
        }
    }

    private static string SetLineCommented(string line, bool commented)
    {
        var trimmed = line.TrimStart();
        var indent = line[..^trimmed.Length];
        var isCommented = trimmed.StartsWith("//");

        if (commented == isCommented)
            return line;
        if (commented)
            return $"{indent}// {trimmed}";

        var content = trimmed[2..];
        if (content.StartsWith(" ")) content = content[1..];
        return $"{indent}{content}";
    }

    private static void WriteSessionCache()
    {
        try
        {
            var states = GetFeatures()
                .SelectMany(f => f.SubFeatures.Append(f))
                .Select(f => $"{f.Define}={f.IsEnabled}")
                .Append($"{SoftMaskConditionalID}={IsConditionalBlockEnabled(SoftMaskConditionalID)}")
                .Append($"SectionOrder={GetSectionOrder()}");
            SessionState.SetString(SessionKey, string.Join(",", states));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FlexibleImage] Failed to write session cache: {e.Message}");
        }
    }

    private static int lastWriteShaderFrame = -1;
    public static void RestoreFromSessionCache()
    {
        if (lastWriteShaderFrame == Time.frameCount)
            return;

        try
        {
            var raw = SessionState.GetString(SessionKey, "");
            if (string.IsNullOrEmpty(raw))
                return;

            Reload();

            var existing = GetFeatures()
                .SelectMany(f => f.SubFeatures.Append(f))
                .Select(f => f.Define)
                .ToHashSet();

            var anyWritten = false;
            foreach (var entry in raw.Split(','))
            {
                var parts = entry.Split('=');
                if (parts.Length != 2) continue;

                var (key, value) = (parts[0], parts[1]);
                try
                {
                    if (key == "SectionOrder" && Enum.TryParse<ProceduralGradientPatternOrder>(value, out var order))
                    {
                        SetSectionOrder(order, false);
                        anyWritten = true;
                    }
                    else if (bool.TryParse(value, out var enabled))
                    {
                        if (key == SoftMaskConditionalID)
                            SetConditionalBlockEnabled(key, enabled, false);
                        else if (existing.Contains(key))
                            SetFeatureEnabled(key, enabled, false);
                        else
                            continue;

                        anyWritten = true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FlexibleImage] Could not restore global setting '{entry}': {e.Message}");
                }
            }

            if (anyWritten)
                RecompileShader();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FlexibleImage] Restoring old global settings failed: {e.Message}");
        }
    }
}
}
