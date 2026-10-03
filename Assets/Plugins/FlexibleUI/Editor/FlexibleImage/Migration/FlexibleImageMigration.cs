using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace JeffGrawAssets.FlexibleUI
{
internal static class FlexibleImageMigration
{
    private const string FlexibleImageGuid = "1a59bc799b067784ca9e98acde554c86";
    private const string QuadDataPresetGuid = "4e04b80f4b9eff24aa16871a1d789bbb";
    private const string ManagedReferenceNamespace = "JeffGrawAssets.FlexibleUI";
    private const string ManagedReferenceAssembly = "JeffGrawAssets.FlexibleUI.Runtime";

    internal enum ResultType { Ready, Version3, Binary, Blocked, Failed }
    internal enum AssetType { Scene, Prefab, Preset, Animation }
    private sealed class Move
    {
        public readonly string[] oldNames;
        public readonly string newName;
        public readonly string defaultValue;

        public Move(string oldNames, string newName, string defaultValue)
        {
            this.oldNames = string.IsNullOrEmpty(oldNames) ? Array.Empty<string>() : oldNames.Split('|');
            this.newName = newName;
            this.defaultValue = defaultValue;
        }
    }

    private sealed class ModuleInfo
    {
        public readonly string configField;
        public readonly string configClass;
        public readonly string animField;
        public readonly string animClass;
        public readonly Move[] configMoves;
        public readonly Move[] animMoves;

        public ModuleInfo(string configField, string configClass, string animField, string animClass, Move[] configMoves, Move[] animMoves)
        {
            this.configField = configField;
            this.configClass = configClass;
            this.animField = animField;
            this.animClass = animClass;
            this.configMoves = configMoves;
            this.animMoves = animMoves;
        }
    }

    private sealed class Property
    {
        public string name;
        public int start;
        public int end;
        public string value;
    }

    private sealed class Reference
    {
        public long rid;
        public string type;
        public List<string> data;
    }

    internal sealed class MigrationResult
    {
        public string assetPath;
        public string fullPath;
        public AssetType assetType;
        public ResultType type;
        public string detail;
        public string output;
        public string sourceHash;
        public bool hasBom;
        public int hosts;
        public int modules;
        public int omitted;
        public long bytes;
        public bool selected = true;
    }

    private static readonly ModuleInfo[] Modules =
    {
        new("_outlineConfig", "OutlineConfig", "outlineAnim", "OutlineAnimData",
            new[]
            {
                new Move("_outlineExpandsOutward", "expandsOutward", "0"), new Move("_outlineAccommodatesCollapsedEdge", "accommodatesCollapsedEdge", "0"),
                new Move("_outlineFadeTowardsPerimeter", "fadeTowardsPerimeter", "0"), new Move("_outlineAdjustsChamfer", "adjustsChamfer", "0"),
                new Move("_addInteriorOutline", "addInteriorOutline", "0"), new Move("_outlineAlphaIsBlend", "alphaIsBlend", "0"),
                new Move("_outlineColorDimensions", "colorDimensions", "{x: 1, y: 1}"), new Move("_outlineColorWrapModeX", "colorWrapModeX", "0"),
                new Move("_outlineColorWrapModeY", "colorWrapModeY", "0"), new Move("_outlineColorPresetMix", "colorPresetMix", "1")
            },
            new[]
            {
                new Move("outlineWidth", "outlineWidth", "0"), new Move("outlineColors", "outlineColors", "black9"),
                new Move("outlineColorOffset", "outlineColorOffset", "{x: 0, y: 0}"), new Move("outlineColorRotation", "outlineColorRotation", "0"),
                new Move("outlineColorScale", "outlineColorScale", "{x: 1, y: 1}")
            }),
        new("_gradientConfig", "GradientConfig", "gradientAnim", "GradientAnimData",
            new[]
            {
                new Move("_proceduralGradientType", "gradientType", "0"), new Move("_proceduralGradientAlphaIsBlend", "alphaIsBlend", "0"),
                new Move("_proceduralGradientAffectsInterior", "affectsInterior", "1"), new Move("_proceduralGradientAffectsOutline", "affectsOutline", "0"),
                new Move("_proceduralGradientAspectCorrection", "aspectCorrection", "0"), new Move("_proceduralGradientPositionFromPointer", "positionFromPointer", "0"),
                new Move("_proceduralGradientInvert", "invert", "0"), new Move("_noiseGradientAlternateMode", "noiseAlternateMode", "0"),
                new Move("_screenSpaceProceduralGradient", "screenSpace", "0"), new Move("_proceduralGradientColorDimensions", "colorDimensions", "{x: 1, y: 1}"),
                new Move("_proceduralGradientColorWrapModeX", "colorWrapModeX", "0"), new Move("_proceduralGradientColorWrapModeY", "colorWrapModeY", "0"),
                new Move("_proceduralGradientColorPresetMix", "colorPresetMix", "1"), new Move("_proceduralGradientEnabled", "", "0")
            },
            new[]
            {
                new Move("proceduralGradientColors", "proceduralGradientColors", "black9"), new Move("proceduralGradientColorOffset", "proceduralGradientColorOffset", "{x: 0, y: 0}"),
                new Move("proceduralGradientColorRotation", "proceduralGradientColorRotation", "0"), new Move("proceduralGradientColorScale", "proceduralGradientColorScale", "{x: 1, y: 1}"),
                new Move("proceduralGradientPosition", "proceduralGradientPosition", "{x: 0.5, y: 0.5}"), new Move("radialGradientSize", "radialGradientSize", "{x: 0.5, y: 0.5}"),
                new Move("radialGradientStrength", "radialGradientStrength", "0.5"), new Move("angleGradientStrength", "angleGradientStrength", "{x: 0.5, y: 0.5}"),
                new Move("proceduralGradientAngle|angleGradientAngle", "proceduralGradientAngle", "0"), new Move("sdfGradientInnerDistance", "sdfGradientInnerDistance", "0"),
                new Move("sdfGradientOuterDistance", "sdfGradientOuterDistance", "0"), new Move("sdfGradientInnerReach", "sdfGradientInnerReach", "0"),
                new Move("sdfGradientOuterReach", "sdfGradientOuterReach", "0"), new Move("proceduralGradientPointerStrength|sdfGradientPointerStrength", "proceduralGradientPointerStrength", "0.5"),
                new Move("conicalGradientCurvature", "conicalGradientCurvature", "0"), new Move("conicalGradientTailStrength", "conicalGradientTailStrength", "0.5"),
                new Move("noiseSeed", "noiseSeed", "0"), new Move("noiseScale", "noiseScale", "0.5"), new Move("noiseEdge", "noiseEdge", "0.5"),
                new Move("noiseStrength", "noiseStrength", "0.5")
            }),
        new("_patternConfig", "PatternConfig", "patternAnim", "PatternAnimData",
            new[]
            {
                new Move("_pattern", "patternType", "3"), new Move("_patternOriginPos", "originPos", "0"),
                new Move("_scanlinePatternSpeedIsStaticOffset", "scanlineSpeedIsStaticOffset", "0"), new Move("_softPattern", "softPattern", "0"),
                new Move("_screenSpacePattern", "screenSpace", "0"), new Move("_spritePatternRotationMode", "spriteRotationMode", "0"),
                new Move("_spritePatternOffsetDirectionDegrees", "spriteOffsetDirection", "0"), new Move("_patternAffectsInterior", "affectsInterior", "1"),
                new Move("_patternAffectsOutline", "affectsOutline", "0"), new Move("_patternColorAlphaIsBlend", "alphaIsBlend", "0"),
                new Move("_patternColorDimensions", "colorDimensions", "{x: 1, y: 1}"), new Move("_patternColorWrapModeX", "colorWrapModeX", "0"),
                new Move("_patternColorWrapModeY", "colorWrapModeY", "0"), new Move("_patternColorPresetMix", "colorPresetMix", "1")
            },
            new[]
            {
                new Move("patternColors", "patternColors", "black9"), new Move("patternColorOffset", "patternColorOffset", "{x: 0, y: 0}"),
                new Move("patternColorRotation", "patternColorRotation", "0"), new Move("patternColorScale", "patternColorScale", "{x: 1, y: 1}"),
                new Move("patternDensity", "patternDensity", "0"), new Move("patternSpeed", "patternSpeed", "0"),
                new Move("patternCellParam", "patternCellParam", "0.5"), new Move("patternLineThickness", "patternLineThickness", "127"),
                new Move("patternSpriteRotation", "patternSpriteRotation", "0")
            }),
        new("_cutoutConfig", "CutoutConfig", "cutoutAnim", "CutoutAnimData",
            new[]
            {
                new Move("_cutout", "cutout", "0"), new Move("_simpleCutoutEdgeEnabled|_cutoutEnabled", "simpleCutoutEdgeEnabled", "false4"),
                new Move("_simpleCutoutRule|_cutoutRule", "simpleCutoutRule", "0"), new Move("_sdfCutoutBehaviour", "sdfCutoutBehaviour", "0"),
                new Move("_sdfCutoutChamferNormalize", "sdfCutoutChamferNormalize", "1"), new Move("_sdfCutoutIsSquircle", "sdfCutoutIsSquircle", "0"),
                new Move("_sdfCutoutMirror", "sdfCutoutMirror", "0"), new Move("_sdfCutoutMirrorIsDiagonal", "sdfCutoutMirrorIsDiagonal", "0"),
                new Move("_sdfCutoutPositionIsAbsolute", "sdfCutoutPositionIsAbsolute", "0"), new Move("_sdfCutoutSizeIsAbsolute", "sdfCutoutSizeIsAbsolute", "0"),
                new Move("", "sdfCutoutUsesAnchors", "0"), new Move("", "sdfCutoutAnchorMin", "{x: 0.5, y: 0.5}"),
                new Move("", "sdfCutoutAnchorMax", "{x: 0.5, y: 0.5}"), new Move("", "sdfCutoutPivot", "{x: 0.5, y: 0.5}"),
                new Move("_cutoutPositionIgnoresExpandedOutlines", "cutoutPositionIgnoresExpandedOutlines", "0"),
                new Move("_cutoutOnlyAffectsOutline", "cutoutOnlyAffectsOutline", "0"), new Move("_invertCutout", "invertCutout", "0")
            },
            new[]
            {
                new Move("simpleCutout|cutout", "simpleCutout", "{x: 0, y: 0, z: 0, w: 0}"), new Move("sdfCutoutChamfer", "sdfCutoutChamfer", "{x: 0, y: 0, z: 0, w: 0}"),
                new Move("sdfCutoutConcavity", "sdfCutoutConcavity", "{x: 0, y: 0, z: 0, w: 0}"), new Move("sdfCutoutPosition", "sdfCutoutPosition", "{x: 0.5, y: 0.5}"),
                new Move("sdfCutoutSize", "sdfCutoutSize", "{x: 0, y: 0}"), new Move("sdfCutoutRotation", "sdfCutoutRotation", "0")
            }),
        new("_strokeConfig", "StrokeConfig", "strokeAnim", "StrokeAnimData",
            new[] { new Move("_strokeOrigin", "strokeOrigin", "0") }, new[] { new Move("stroke", "stroke", "0") }),
        new("_skewConfig", "SkewConfig", "skewAnim", "SkewAnimData",
            new[]
            {
                new Move("_collapsedEdge", "collapsedEdge", "0"), new Move("_collapseIntoParallelogram", "collapseIntoParallelogram", "0"),
                new Move("_mirrorCollapse", "mirrorCollapse", "0"), new Move("_edgeCollapseAmountIsAbsolute", "edgeCollapseAmountIsAbsolute", "0")
            },
            new[]
            {
                new Move("collapseEdgeAmount", "collapseEdgeAmount", "0"), new Move("collapseEdgeAmountAbsolute", "collapseEdgeAmountAbsolute", "0"),
                new Move("collapseEdgePosition", "collapseEdgePosition", "0"), new Move("collapsedCornerChamfer", "collapsedCornerChamfer", "0"),
                new Move("collapsedCornerConcavity", "collapsedCornerConcavity", "0")
            })
    };
    private static readonly string[] LegacyConfigNames = Modules.SelectMany(module => module.configMoves).SelectMany(move => move.oldNames).Distinct().ToArray();

    internal static string[] FindPaths(string[] scopePaths)
    {
        return AssetDatabase.FindAssets("t:Prefab", scopePaths).Concat(AssetDatabase.FindAssets("t:Scene", scopePaths))
            .Concat(AssetDatabase.FindAssets("t:AnimationClip", scopePaths)).Concat(AssetDatabase.FindAssets("t:QuadDataPreset", scopePaths))
            .Select(AssetDatabase.GUIDToAssetPath).Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && Path.GetExtension(path).ToLowerInvariant() is ".prefab" or ".unity" or ".anim" or ".asset")
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path).ToArray();
    }

    internal static MigrationResult Discover(string fullPath)
    {
        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        var assetType = extension switch { ".unity" => AssetType.Scene, ".prefab" => AssetType.Prefab, ".anim" => AssetType.Animation, _ => AssetType.Preset };
        var assetPath = fullPath.Replace('\\', '/').Substring(Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/').Length + 1);
        using (var stream = File.OpenRead(fullPath))
        {
            var header = new byte[Math.Min(6, stream.Length)];
            stream.Read(header, 0, header.Length);
            var offset = header.Length >= 3 && header[0] == 0xef && header[1] == 0xbb && header[2] == 0xbf ? 3 : 0;
            if (header.Length - offset < 5 || Encoding.ASCII.GetString(header, offset, 5) != "%YAML")
                return new MigrationResult { assetPath = assetPath, fullPath = fullPath, assetType = assetType, type = ResultType.Binary, detail = "Binary assets must be converted to Force Text while using Flexible Image v2." };
        }

        if (assetType == AssetType.Animation)
        {
            return File.ReadLines(fullPath).Any(line => IsMovedPath(line, "attribute:")) ? new MigrationResult { assetPath = assetPath, fullPath = fullPath, assetType = assetType, type = ResultType.Blocked, detail = "Contains a direct animation binding to a moved v2 property." } : null;
        }

        var relevant = false;
        var overrideHazard = false;
        foreach (var line in File.ReadLines(fullPath))
        {
            relevant |= line.Contains("guid: " + FlexibleImageGuid) || line.Contains("guid: " + QuadDataPresetGuid);
            overrideHazard |= IsMovedPath(line, "propertyPath:");
            if (relevant && overrideHazard) break;
        }
        if (!relevant && !overrideHazard) return null;
        if (overrideHazard) return new MigrationResult { assetPath = assetPath, fullPath = fullPath, assetType = assetType, type = ResultType.Blocked, detail = "Contains prefab overrides targeting moved v2 fields. This version will not rewrite them without resolving the source prefab." };
        var bytes = File.ReadAllBytes(fullPath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
        var text = Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        var documents = RelevantDocuments(text).ToArray();
        if (documents.Length == 0) return null;
        var legacyDocuments = documents.Count(HasLegacyData);
        var modularDocuments = documents.Count(document => !HasLegacyData(document));
        if (documents.Any(document => HasLegacyData(document) && Modules.Any(module => document.Contains(module.configField + ":") || document.Contains(module.animField + ":"))) || legacyDocuments > 0 && modularDocuments > 0)
            return new MigrationResult { assetPath = assetPath, fullPath = fullPath, assetType = assetType, type = ResultType.Blocked, detail = "Contains mixed v2 and v3 data." };
        if (legacyDocuments == 0)
            return new MigrationResult { assetPath = assetPath, fullPath = fullPath, assetType = assetType, type = ResultType.Version3 };
        return new MigrationResult { assetPath = assetPath, fullPath = fullPath, assetType = assetType, sourceHash = Hash(bytes), hasBom = hasBom, type = ResultType.Ready };
    }

    private static string Transform(string text, MigrationResult result)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        for (var documentEnd = lines.Count; documentEnd > 0;)
        {
            var documentStart = documentEnd - 1;
            while (documentStart > 0 && !lines[documentStart].StartsWith("--- !u!", StringComparison.Ordinal)) documentStart--;
            var document = string.Join("\n", lines.Skip(documentStart).Take(documentEnd - documentStart));
            if ((document.Contains("guid: " + FlexibleImageGuid) || document.Contains("guid: " + QuadDataPresetGuid)) && document.Contains("_quadDataList:"))
            {
                var references = new List<Reference>();
                var usedRids = FindRids(lines, documentStart, documentEnd);
                var nextRid = 1L;
                var listIndex = documentStart;
                while (listIndex < documentEnd && !lines[listIndex].TrimStart().StartsWith("_quadDataList:", StringComparison.Ordinal)) listIndex++;
                if (listIndex == documentEnd) throw new InvalidDataException("Could not locate QuadData list.");
                var listIndent = Indent(lines[listIndex]);
                var quadStarts = new List<int>();
                for (var i = listIndex + 1; i < documentEnd; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (Indent(lines[i]) < listIndent || Indent(lines[i]) == listIndent && !trimmed.StartsWith("- ", StringComparison.Ordinal)) break;
                    if (Indent(lines[i]) == listIndent && trimmed.StartsWith("- ", StringComparison.Ordinal)) quadStarts.Add(i);
                }

                for (var q = quadStarts.Count - 1; q >= 0; q--)
                {
                    var start = quadStarts[q];
                    var end = q + 1 < quadStarts.Count ? quadStarts[q + 1] : FindQuadEnd(lines, start, documentEnd, listIndent);
                    var before = lines.Count;
                    TransformQuad(lines, start, end, references, usedRids, ref nextRid, result);
                    documentEnd += lines.Count - before;
                }

                AppendReferences(lines, documentStart, ref documentEnd, references);
                result.hosts++;
            }
            documentEnd = documentStart;
        }
        return string.Join(newline, lines);
    }

    private static void TransformQuad(List<string> lines, int start, int end, List<Reference> references, HashSet<long> usedRids, ref long nextRid, MigrationResult result)
    {
        var quadIndent = Indent(lines[start]);
        var configProperties = ReadProperties(lines, start, end, quadIndent + 2);
        var propertyBlocks = new List<(int start, int end, Dictionary<string, Property> properties)>();
        for (var i = start; i < end; i++)
        {
            if (!lines[i].TrimStart().StartsWith("- interpolationType:", StringComparison.Ordinal)) continue;
            var blockEnd = i + 1;
            var propertyIndent = Indent(lines[i]) + 2;
            while (blockEnd < end && (string.IsNullOrWhiteSpace(lines[blockEnd]) || Indent(lines[blockEnd]) >= propertyIndent)) blockEnd++;
            propertyBlocks.Add((i, blockEnd, ReadProperties(lines, i, blockEnd, propertyIndent)));
            i = blockEnd - 1;
        }

        var enabled = new Dictionary<ModuleInfo, bool>();
        foreach (var module in Modules)
        {
            var configured = module.configMoves.Any(move => !IsDefault(lines, Find(configProperties, move), move.newName == "patternType" && configProperties.ContainsKey("_gridPatternIsDiamond") ? "0" : move.defaultValue));
            var animated = propertyBlocks.Any(block => module.animMoves.Any(move => !IsDefault(lines, Find(block.properties, move), move.defaultValue)));
            enabled[module] = configured || animated;
            if (enabled[module]) result.modules++; else result.omitted++;
        }

        for (var p = propertyBlocks.Count - 1; p >= 0; p--)
        {
            var block = propertyBlocks[p];
            var insertions = new List<string>();
            foreach (var module in Modules.Where(module => enabled[module]))
            {
                var rid = AllocateRid(usedRids, ref nextRid);
                insertions.Add($"{module.animField}: {{rid: {rid}}}");
                references.Add(new Reference { rid = rid, type = module.animClass, data = BuildData(lines, block.properties, module.animMoves) });
            }
            ReplaceProperties(lines, block.start, block.end, block.properties, Modules.SelectMany(module => module.animMoves), insertions, true);
        }

        configProperties = ReadProperties(lines, start, FindQuadEnd(lines, start, lines.Count, quadIndent), quadIndent + 2);
        var configInsertions = new List<string>();
        foreach (var module in Modules.Where(module => enabled[module]))
        {
            var rid = AllocateRid(usedRids, ref nextRid);
            configInsertions.Add($"{module.configField}: {{rid: {rid}}}");
            references.Add(new Reference { rid = rid, type = module.configClass, data = BuildData(lines, configProperties, module.configMoves) });
        }
        var currentEnd = FindQuadEnd(lines, start, lines.Count, quadIndent);
        ReplaceProperties(lines, start, currentEnd, configProperties, Modules.SelectMany(module => module.configMoves), configInsertions, false);
    }

    private static Dictionary<string, Property> ReadProperties(List<string> lines, int start, int end, int propertyIndent)
    {
        var properties = new Dictionary<string, Property>(StringComparer.Ordinal);
        var starts = new List<int>();
        if (start < end && lines[start].TrimStart().StartsWith("- ", StringComparison.Ordinal)) starts.Add(start);
        for (var i = start + 1; i < end; i++)
            if (Indent(lines[i]) == propertyIndent && !lines[i].TrimStart().StartsWith("- ", StringComparison.Ordinal) && lines[i].Contains(':')) starts.Add(i);
        for (var i = 0; i < starts.Count; i++)
        {
            var line = lines[starts[i]].TrimStart();
            if (line.StartsWith("- ", StringComparison.Ordinal)) line = line.Substring(2);
            var colon = line.IndexOf(':');
            if (colon < 0) continue;
            var property = new Property { name = line.Substring(0, colon), start = starts[i], end = i + 1 < starts.Count ? starts[i + 1] : end, value = line.Substring(colon + 1).Trim() };
            properties[property.name] = property;
        }
        return properties;
    }

    private static Property Find(Dictionary<string, Property> properties, Move move)
    {
        foreach (var name in move.oldNames)
            if (properties.TryGetValue(name, out var property)) return property;
        return null;
    }

    private static bool IsDefault(List<string> lines, Property property, string expected)
    {
        if (property == null) return true;
        if (expected == "false4") return property.value.Length > 0 && property.value.All(character => character == '0');
        if (expected == "black9")
        {
            var values = lines.Skip(property.start + 1).Take(property.end - property.start - 1).Where(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal)).ToArray();
            return values.Length == 9 && values.All(line => InlineMapEquals(line.TrimStart().Substring(2), "{r: 0, g: 0, b: 0, a: 1}"));
        }
        if (expected.StartsWith("{", StringComparison.Ordinal)) return InlineMapEquals(property.value, expected);
        return NumberEquals(property.value, expected);
    }

    private static bool InlineMapEquals(string value, string expected)
    {
        var a = ParseMap(value);
        var b = ParseMap(expected);
        if (a.Count == 0 && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scalar)) return b.Count > 0 && b.Values.All(number => Math.Abs(number - scalar) < 0.000001);
        return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var number) && Math.Abs(pair.Value - number) < 0.000001);
    }

    private static Dictionary<string, double> ParseMap(string value)
    {
        var result = new Dictionary<string, double>();
        value = value.Trim().TrimStart('{').TrimEnd('}');
        foreach (var part in value.Split(','))
        {
            var pair = part.Split(':');
            if (pair.Length == 2 && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) result[pair[0].Trim()] = number;
        }
        return result;
    }

    private static bool NumberEquals(string value, string expected) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && Math.Abs(a - b) < 0.000001;

    private static List<string> BuildData(List<string> lines, Dictionary<string, Property> properties, Move[] moves)
    {
        var data = new List<string>();
        foreach (var move in moves)
        {
            if (string.IsNullOrEmpty(move.newName)) continue;
            var property = Find(properties, move);
            if (property == null)
            {
                data.Add(move.newName + ": " + DefaultYaml(move.defaultValue));
                if (move.defaultValue == "black9") for (var i = 0; i < 9; i++) data.Add("- {r: 0, g: 0, b: 0, a: 1}");
                continue;
            }
            data.Add(move.newName + ":" + (string.IsNullOrEmpty(property.value) ? "" : " " + property.value));
            var sourceIndent = Indent(lines[property.start]);
            for (var i = property.start + 1; i < property.end; i++) data.Add(lines[i].Length >= sourceIndent ? lines[i].Substring(sourceIndent) : lines[i].TrimStart());
        }
        return data;
    }

    private static string DefaultYaml(string value) => value switch { "black9" => "", "false4" => "00000000", _ => value };

    private static void ReplaceProperties(List<string> lines, int start, int end, Dictionary<string, Property> properties, IEnumerable<Move> moves, List<string> insertions, bool listItem)
    {
        var ranges = moves.Select(move => Find(properties, move)).Where(property => property != null).Distinct().OrderByDescending(property => property.start).ToArray();
        foreach (var property in ranges) lines.RemoveRange(property.start, property.end - property.start);
        var insertAt = start;
        if (ranges.Any(property => property.start == start)) insertAt = Math.Min(start, lines.Count);
        else if (!listItem)
        {
            insertAt = start + 1;
            while (insertAt < lines.Count && (lines[insertAt].TrimStart().StartsWith("editorSelectedAnimation", StringComparison.Ordinal))) insertAt++;
        }
        if (insertions.Count == 0) return;
        if (listItem)
        {
            var first = lines[insertAt];
            var indent = new string(' ', Indent(first));
            var content = first.TrimStart().Substring(2);
            lines[insertAt] = indent + "- " + insertions[0];
            for (var i = 1; i < insertions.Count; i++) lines.Insert(insertAt + i, indent + "  " + insertions[i]);
            lines.Insert(insertAt + insertions.Count, indent + "  " + content);
        }
        else
        {
            var indent = new string(' ', Indent(lines[start]) + 2);
            for (var i = 0; i < insertions.Count; i++) lines.Insert(insertAt + i, indent + insertions[i]);
        }
    }

    private static HashSet<long> FindRids(List<string> lines, int start, int end)
    {
        var result = new HashSet<long>();
        for (var i = start; i < end; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("- rid: ", StringComparison.Ordinal) && long.TryParse(trimmed.Substring(7), out var rid)) result.Add(rid);
        }
        return result;
    }

    private static long AllocateRid(HashSet<long> used, ref long next)
    {
        while (used.Contains(next)) next++;
        used.Add(next);
        return next++;
    }

    private static void AppendReferences(List<string> lines, int documentStart, ref int documentEnd, List<Reference> references)
    {
        if (references.Count == 0) return;
        var referenceIndex = -1;
        for (var i = documentStart; i < documentEnd; i++) if (lines[i] == "  references:") referenceIndex = i;
        if (referenceIndex >= 0)
        {
            var refIds = referenceIndex + 1;
            var version2 = false;
            while (refIds < documentEnd && lines[refIds].Trim() != "RefIds:" && lines[refIds].Trim() != "RefIds: []")
            {
                if (lines[refIds].Trim() == "version: 2") version2 = true;
                refIds++;
            }
            if (!version2 || refIds == documentEnd) throw new InvalidDataException("Unsupported managed-reference registry.");
            if (lines[refIds].Trim() == "RefIds: []") lines[refIds] = "    RefIds:";
        }
        else
        {
            lines.Insert(documentEnd++, "  references:");
            lines.Insert(documentEnd++, "    version: 2");
            lines.Insert(documentEnd++, "    RefIds:");
        }
        foreach (var reference in references)
        {
            lines.Insert(documentEnd++, $"    - rid: {reference.rid}");
            lines.Insert(documentEnd++, $"      type: {{class: {reference.type}, ns: {ManagedReferenceNamespace}, asm: {ManagedReferenceAssembly}}}");
            lines.Insert(documentEnd++, "      data:");
            foreach (var line in reference.data) lines.Insert(documentEnd++, "        " + line);
        }
    }

    private static int FindQuadEnd(List<string> lines, int start, int end, int indent)
    {
        for (var i = start + 1; i < end; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var currentIndent = Indent(lines[i]);
            if (currentIndent < indent || currentIndent == indent && !lines[i].TrimStart().StartsWith("- ", StringComparison.Ordinal)) return i;
            if (currentIndent == indent && lines[i].TrimStart().StartsWith("- ", StringComparison.Ordinal)) return i;
        }
        return end;
    }

    private static int Indent(string line)
    {
        var i = 0;
        while (i < line.Length && line[i] == ' ') i++;
        return i;
    }

    private static bool IsMovedPath(string line, string prefix)
    {
        return line.TrimStart().StartsWith(prefix, StringComparison.Ordinal)
            && (line.Contains("_quadDataContainer") || line.Contains("_instanceQuadDataContainer") || line.Contains("quadDataContainer._quadDataList"))
            && Modules.Any(module => !line.Contains(module.configField) && !line.Contains(module.animField)
                && (module.configMoves.SelectMany(move => move.oldNames).Any(line.Contains) || module.animMoves.SelectMany(move => move.oldNames).Any(line.Contains)));
    }

    private static bool HasLegacyData(string text)
    {
        var body = text.Replace("\r\n", "\n");
        var references = body.IndexOf("\n  references:", StringComparison.Ordinal);
        if (references >= 0) body = body.Substring(0, references);
        return body.Split('\n').Any(line => LegacyConfigNames.Any(name => line.TrimStart().StartsWith(name + ":", StringComparison.Ordinal)));
    }

    private static IEnumerable<string> RelevantDocuments(string text)
    {
        var starts = new HashSet<int>();
        foreach (var guid in new[] { FlexibleImageGuid, QuadDataPresetGuid })
        {
            for (var index = text.IndexOf("guid: " + guid, StringComparison.Ordinal); index >= 0; index = text.IndexOf("guid: " + guid, index + guid.Length, StringComparison.Ordinal))
            {
                var start = text.LastIndexOf("\n--- !u!", index, StringComparison.Ordinal);
                start = start < 0 ? 0 : start + 1;
                if (!starts.Add(start)) continue;
                var end = text.IndexOf("\n--- !u!", index, StringComparison.Ordinal);
                var document = text.Substring(start, end < 0 ? text.Length - start : end - start);
                if (document.Contains("_quadDataList:")) yield return document;
            }
        }
    }

    private static void Validate(string text)
    {
        foreach (var document in RelevantDocuments(text))
        {
            if (HasLegacyData(document)) throw new InvalidDataException("Migrated output still contains v2 module data.");
            var pointers = Regex.Matches(document, @"\{rid: (-?\d+)\}").Cast<Match>().Select(match => long.Parse(match.Groups[1].Value)).Where(rid => rid > 0).ToArray();
            var entries = Regex.Matches(document, @"(?m)^\s*- rid: (-?\d+)\s*$").Cast<Match>().Select(match => long.Parse(match.Groups[1].Value)).ToArray();
            if (entries.Distinct().Count() != entries.Length || pointers.Any(rid => entries.Count(entry => entry == rid) != 1)) throw new InvalidDataException("Migrated output contains an invalid managed-reference registry.");
        }
    }

    internal static void Migrate(IReadOnlyCollection<MigrationResult> results)
    {
        var selected = results.Where(result => result.type == ResultType.Ready && result.selected).ToArray();
        if (results.Any(result => result.type is ResultType.Blocked or ResultType.Failed))
        {
            EditorUtility.DisplayDialog("Flexible Image Migration", "Resolve blocked scan results before migrating this scope.", "OK");
            return;
        }
        var deselected = new HashSet<string>(results.Where(result => result.type == ResultType.Ready && !result.selected).Select(result => result.assetPath));
        var omittedDependency = AssetDatabase.GetDependencies(selected.Select(result => result.assetPath).ToArray(), true).FirstOrDefault(deselected.Contains);
        if (!string.IsNullOrEmpty(omittedDependency))
        {
            EditorUtility.DisplayDialog("Flexible Image Migration", omittedDependency + " is a migratable dependency but is not selected.", "OK");
            return;
        }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Flexible Image Migration", "Exit Play Mode before migrating.", "OK");
            return;
        }
        var openScenes = Enumerable.Range(0, EditorSceneManager.sceneCount).Select(EditorSceneManager.GetSceneAt).Select(scene => scene.path).Where(path => !string.IsNullOrEmpty(path)).ToArray();
        var openAffected = selected.FirstOrDefault(result => openScenes.Contains(result.assetPath));
        var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (openAffected != null || prefabStage != null && selected.Any(result => result.assetPath == prefabStage.assetPath))
        {
            EditorUtility.DisplayDialog("Flexible Image Migration", "Close affected scenes and Prefab Mode before migrating. Do not save affected v2 assets under v3.", "OK");
            return;
        }
        var dirtyPreset = Resources.FindObjectsOfTypeAll<QuadDataPreset>().FirstOrDefault(preset => EditorUtility.IsDirty(preset) && selected.Any(result => result.assetPath == AssetDatabase.GetAssetPath(preset)));
        if (dirtyPreset)
        {
            EditorUtility.DisplayDialog("Flexible Image Migration", "An affected Quad Data preset has unsaved changes. Restore or save it under v2 before migrating.", "OK");
            return;
        }
        try
        {
            for (var i = 0; i < selected.Length; i++)
            {
                EditorUtility.DisplayProgressBar("Preparing Flexible Image Migration", selected[i].assetPath, (float)i / selected.Length);
                var bytes = File.ReadAllBytes(selected[i].fullPath);
                if (Hash(bytes) != selected[i].sourceHash) throw new InvalidDataException(selected[i].assetPath + " changed after the scan. Scan again before migrating.");
                var text = Encoding.UTF8.GetString(bytes, selected[i].hasBom ? 3 : 0, bytes.Length - (selected[i].hasBom ? 3 : 0));
                selected[i].hosts = selected[i].modules = selected[i].omitted = 0;
                selected[i].output = Transform(text, selected[i]);
                Validate(selected[i].output);
                if (selected[i].hosts == 0) throw new InvalidDataException("No Flexible Image data hosts were found.");
                selected[i].detail = $"{selected[i].hosts} data host{(selected[i].hosts == 1 ? "" : "s")}; {selected[i].modules} modules preserved; {selected[i].omitted} unused modules omitted.";
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Flexible Image Migration", "Could not prepare the selected assets. See the Console for details.", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        var binaryCount = results.Count(result => result.type == ResultType.Binary);
        var binaryWarning = binaryCount == 0 ? "" : $"\n\n{binaryCount} binary assets cannot be inspected and will not be migrated or validated. Convert affected assets to Force Text while using Flexible Image v2.";
        if (!EditorUtility.DisplayDialog("Flexible Image Migration", $"Rewrite {selected.Length} selected assets? {selected.Sum(result => result.omitted)} unused modules will be omitted and {selected.Sum(result => result.modules)} configured modules will be preserved. Back up or commit the project before continuing.{binaryWarning}", "Migrate", "Cancel"))
        {
            foreach (var result in selected) result.output = null;
            return;
        }

        var backupRoot = Path.GetFullPath($"Library/FlexibleImage/MigrationBackups/{DateTime.Now:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupRoot);
        var manifest = new StringBuilder();
        var written = new List<MigrationResult>();
        var refreshDisabled = false;
        var assetEditing = false;
        try
        {
            AssetDatabase.DisallowAutoRefresh();
            refreshDisabled = true;
            AssetDatabase.StartAssetEditing();
            assetEditing = true;
            foreach (var result in selected)
            {
                var backup = Path.Combine(backupRoot, result.assetPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(result.fullPath, backup, true);
                manifest.AppendLine(result.assetPath + "|" + result.sourceHash);
            }
            File.WriteAllText(Path.Combine(backupRoot, "manifest.txt"), manifest.ToString(), new UTF8Encoding(false));
            foreach (var result in selected)
            {
                var temporary = result.fullPath + ".flexibleimage-migration";
                File.WriteAllText(temporary, result.output, new UTF8Encoding(result.hasBom));
                File.Replace(temporary, result.fullPath, null);
                written.Add(result);
            }
        }
        catch (Exception exception)
        {
            var restoreFailures = Restore(written, backupRoot);
            Debug.LogException(exception);
            EditorUtility.DisplayDialog("Flexible Image Migration Failed", restoreFailures.Count == 0 ? "The migration failed and changed files were restored. See the Console for details." : "The migration failed and some files could not be restored: " + string.Join(", ", restoreFailures), "OK");
            return;
        }
        finally
        {
            if (assetEditing)
            {
                try { AssetDatabase.StopAssetEditing(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            if (refreshDisabled) AssetDatabase.AllowAutoRefresh();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        var invalid = selected.Where(result => Discover(result.fullPath)?.type != ResultType.Version3).ToArray();
        if (invalid.Length > 0)
        {
            var restoreFailures = Restore(selected, backupRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorUtility.DisplayDialog("Flexible Image Migration Failed", restoreFailures.Count == 0 ? "Validation failed and the original files were restored." : "Validation failed and some files could not be restored: " + string.Join(", ", restoreFailures), "OK");
            return;
        }

        var binary = results.Count(result => result.type == ResultType.Binary);
        Debug.Log($"Flexible Image migration complete: {selected.Length} files, {selected.Sum(result => result.hosts)} data hosts, {selected.Sum(result => result.modules)} modules migrated, {selected.Sum(result => result.omitted)} unused modules omitted, {binary} binary assets unchanged. Backup: {backupRoot}");
        EditorUtility.DisplayDialog(binary == 0 ? "Flexible Image Migration Complete" : "Flexible Image Migration Incomplete", binary == 0 ? $"Migrated {selected.Length} assets. Backup: {backupRoot}" : $"Migrated {selected.Length} text assets. {binary} binary assets were not changed; convert them to Force Text while using Flexible Image v2. Backup: {backupRoot}", "OK");
        foreach (var result in selected)
        {
            result.type = ResultType.Version3;
            result.selected = false;
            result.output = null;
            result.detail = "Migrated.";
        }
    }

    private static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private static List<string> Restore(IEnumerable<MigrationResult> results, string backupRoot)
    {
        var failures = new List<string>();
        foreach (var result in results)
        {
            try
            {
                var backup = Path.Combine(backupRoot, result.assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(backup)) File.Copy(backup, result.fullPath, true);
            }
            catch (Exception exception)
            {
                failures.Add(result.assetPath);
                Debug.LogException(exception);
            }
        }
        return failures;
    }
}
}
