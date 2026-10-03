using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LightSide
{
    internal sealed class TmpSpriteMigrator
    {
        const string CatalogLabelPrefix = "UniText-TMP-Sprite-";
        const string CatalogRevisionLabelPrefix = "UniText-TMP-Sprite-Revision-";
        const string CatalogFolderName = "Migrated TMP Sprites";

        readonly Type spriteAssetType;
        readonly Type settingsType;
        readonly MethodInfo hashMethod;
        readonly MethodInfo searchByHashMethod;
        readonly string projectFolder;
        readonly List<LogEntry> log;
        readonly Dictionary<string, UniTextSprites> catalogByGuid =
            new(StringComparer.OrdinalIgnoreCase);

        internal readonly struct SpriteStyle
        {
            public readonly string TagName;
            public readonly SpriteModifier Modifier;

            public SpriteStyle(string tagName, SpriteModifier modifier)
            {
                TagName = tagName;
                Modifier = modifier;
            }
        }

        internal struct ConversionResult
        {
            public string text;
            public List<SpriteStyle> styles;
            public List<string> warnings;
            public List<string> createdAssetPaths;
        }

        sealed class SpriteAssetData
        {
            public UnityEngine.Object asset;
            public string guid;
            public string path;
            public string name;
            public Texture2D sheet;
            public FaceData face;
            public List<CharacterData> characters;
        }

        sealed class CharacterData
        {
            public int index;
            public string name;
            public float scale;
            public GlyphData glyph;
        }

        sealed class GlyphData
        {
            public uint index;
            public float width;
            public float height;
            public float bearingX;
            public float bearingY;
            public float advance;
            public float scale;
            public Rect rect;
            public Sprite sprite;
        }

        readonly struct FaceData
        {
            public readonly float pointSize;
            public readonly float scale;
            public readonly float ascent;
            public readonly float descent;
            public readonly float baseline;

            public FaceData(float pointSize, float scale, float ascent, float descent,
                float baseline)
            {
                this.pointSize = pointSize;
                this.scale = scale;
                this.ascent = ascent;
                this.descent = descent;
                this.baseline = baseline;
            }

            public bool IsUsable => pointSize > 0f;
            public float AscentEm => ascent * scale / pointSize;
            public float DescentEm => descent * scale / pointSize;
        }

        sealed class TagData
        {
            public int start;
            public int end;
            public string primary;
            public bool hasPrimary;
            public bool primaryNumeric;
            public bool legacyNumericOnly;
            public readonly List<AttributeData> attributes = new();
        }

        readonly struct AttributeData
        {
            public readonly string name;
            public readonly string value;

            public AttributeData(string name, string value)
            {
                this.name = name;
                this.value = value;
            }
        }

        sealed class ResolvedTag
        {
            public TagData tag;
            public SpriteAssetData asset;
            public CharacterData character;
            public string ruleTag;
            public string colorToken;
            public bool preserveSource;
        }

        sealed class StyleGroup
        {
            public SpriteAssetData asset;
            public string tagName;
            public readonly Dictionary<int, CharacterData> usedCharacters = new();
        }

        public TmpSpriteMigrator(Type spriteAssetType, Type settingsType,
            string projectFolder, List<LogEntry> log)
        {
            this.spriteAssetType = spriteAssetType;
            this.settingsType = settingsType;
            this.projectFolder = projectFolder;
            this.log = log;

            var assembly = spriteAssetType.Assembly;
            var utilities = assembly.GetType("TMPro.TMP_TextUtilities");
            hashMethod = utilities?.GetMethod("GetHashCode",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string) }, null);
            searchByHashMethod = spriteAssetType.GetMethod("SearchForSpriteByHashCode",
                BindingFlags.Public | BindingFlags.Static, null,
                new[]
                {
                    spriteAssetType, typeof(int), typeof(bool), typeof(int).MakeByRefType(),
                }, null);
        }

        public bool TryConvert(string input, UnityEngine.Object assignedSpriteAsset,
            UnityEngine.Object fontAsset, Color componentColor, bool tintAll,
            bool hasVertexGradient,
            out ConversionResult result, out string error)
            => TryConvertCore(input, assignedSpriteAsset, fontAsset, componentColor, tintAll,
                hasVertexGradient, true, out result, out error);

        public bool TryValidate(string input, UnityEngine.Object assignedSpriteAsset,
            UnityEngine.Object fontAsset, Color componentColor, bool tintAll,
            bool hasVertexGradient, out string error)
        {
            return TryConvertCore(input, assignedSpriteAsset, fontAsset, componentColor, tintAll,
                hasVertexGradient, false, out _, out error);
        }

        bool TryConvertCore(string input, UnityEngine.Object assignedSpriteAsset,
            UnityEngine.Object fontAsset, Color componentColor, bool tintAll,
            bool hasVertexGradient, bool createAssets, out ConversionResult result,
            out string error)
        {
            result = new ConversionResult { text = input };
            error = null;

            if (string.IsNullOrEmpty(input) ||
                input.IndexOf("<sprite", StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            if (!TryParseTags(input, out var tags, out error)) return false;
            if (tags.Count == 0) return true;

            var warnings = new List<string>();
            var resolved = new List<ResolvedTag>(tags.Count);
            var defaultAsset = assignedSpriteAsset ?? ResolveSettingsDefaultAsset();
            fontAsset ??= ResolveSettingsDefaultFontAsset();
            if (!TryReadFace(fontAsset, out var fontFace))
            {
                error = fontAsset == null
                    ? "Sprite metrics need a TMP font, and neither this component nor TMP " +
                      "Settings names one."
                    : $"TMP font '{fontAsset.name}' reports a point size of zero, and sprite " +
                      "metrics are measured against it. Regenerate that font asset in TMP.";
                return false;
            }

            for (var i = 0; i < tags.Count; i++)
            {
                var tag = tags[i];
                if (HasActiveTagAt(input, tag.start, "font") ||
                    HasActiveTagAt(input, tag.start, "sup") ||
                    HasActiveTagAt(input, tag.start, "sub") ||
                    HasActiveTagAt(input, tag.start, "voffset"))
                {
                    error = "TMP sprite metrics inside font, script-position, or vertical-offset " +
                            "markup depend on that occurrence and cannot be stored as one keyed override.";
                    return false;
                }

                var hasRangeColor = HasActiveTagAt(input, tag.start, "color", true);
                var hasRangeGradient = HasActiveTagAt(input, tag.start, "gradient");
                var hasAlphaMarkup = HasOpeningTagBefore(input, tag.start, "alpha");
                if (!TryResolveTag(tag, defaultAsset, componentColor, tintAll,
                        hasVertexGradient, hasRangeColor, hasRangeGradient, hasAlphaMarkup,
                        out var item, out error))
                    return false;
                resolved.Add(item);
            }

            var groups = new Dictionary<string, StyleGroup>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < resolved.Count; i++)
            {
                var item = resolved[i];
                var isDefault = defaultAsset != null &&
                                ObjectUtils.GetInstanceIdCompat(item.asset.asset) ==
                                ObjectUtils.GetInstanceIdCompat(defaultAsset);
                item.ruleTag = isDefault ? "sprite" : $"sprite_{item.asset.guid}";
                item.preserveSource = isDefault && item.tag.legacyNumericOnly &&
                                      item.colorToken == null &&
                                      string.Equals(item.tag.primary,
                                          item.character.index.ToString(CultureInfo.InvariantCulture),
                                          StringComparison.Ordinal);

                if (!groups.TryGetValue(item.asset.guid, out var group))
                {
                    group = new StyleGroup { asset = item.asset, tagName = item.ruleTag };
                    groups.Add(item.asset.guid, group);
                }
                group.usedCharacters[item.character.index] = item.character;
            }

            var plans = new List<(StyleGroup group, SpriteModifier modifier)>(groups.Count);
            foreach (var pair in groups)
            {
                var group = pair.Value;
                if (!TryValidateUsedSprites(group.asset, group.usedCharacters, out error))
                    return false;
                var modifier = new SpriteModifier();

                foreach (var used in group.usedCharacters)
                {
                    var key = used.Key.ToString(CultureInfo.InvariantCulture);
                    AddComponentMetrics(modifier, key, group.asset, used.Value, fontFace);
                }
                plans.Add((group, modifier));
            }

            List<SpriteStyle> styles = null;
            List<string> createdAssetPaths = null;
            if (!createAssets)
            {
                for (var i = 0; i < plans.Count; i++)
                {
                    var source = plans[i].group.asset;
                    if (!TryGetCatalog(source, false, out var catalog, out _, out error) ||
                        catalog != null && !TryValidateCatalog(catalog, source, out error))
                        return false;
                }
            }
            else
            {
                styles = new List<SpriteStyle>(plans.Count);
                createdAssetPaths = new List<string>();
                for (var i = 0; i < plans.Count; i++)
                {
                    var plan = plans[i];
                    if (!TryGetCatalog(plan.group.asset, true, out var catalog,
                            out var createdAssetPath, out error))
                    {
                        RollbackCreated(createdAssetPaths);
                        return false;
                    }
                    if (createdAssetPath != null) createdAssetPaths.Add(createdAssetPath);
                    if (!TryValidateCatalog(catalog, plan.group.asset, out error))
                    {
                        RollbackCreated(createdAssetPaths);
                        return false;
                    }
                    if (createdAssetPath == null) RefreshRevisionLabel(catalog, plan.group.asset);

                    foreach (var used in plan.group.usedCharacters)
                    {
                        var key = used.Key.ToString(CultureInfo.InvariantCulture);
                        if (!catalog.TryGet(key, out var entry) || entry?.Sprite == null)
                        {
                            error = $"Generated catalog '{AssetDatabase.GetAssetPath(catalog)}' " +
                                    $"does not contain a renderable '{key}' entry.";
                            RollbackCreated(createdAssetPaths);
                            return false;
                        }
                    }

                    plan.modifier.Provider = new AssetSpriteProvider { Asset = catalog };
                    styles.Add(new SpriteStyle(plan.group.tagName, plan.modifier));
                }
            }

            var output = new StringBuilder(input.Length + resolved.Count * 16);
            var cursor = 0;
            for (var i = 0; i < resolved.Count; i++)
            {
                var item = resolved[i];
                output.Append(input, cursor, item.tag.start - cursor);
                if (item.preserveSource)
                {
                    output.Append(input, item.tag.start, item.tag.end - item.tag.start);
                }
                else
                {
                    output.Append('<').Append(item.ruleTag).Append('=')
                        .Append(item.character.index.ToString(CultureInfo.InvariantCulture));
                    if (item.colorToken != null) output.Append(',').Append(item.colorToken);
                    output.Append('>');
                }
                cursor = item.tag.end;
            }
            output.Append(input, cursor, input.Length - cursor);

            if (resolved.Exists(item => !item.preserveSource))
                warnings.Add("TMP sprite attribute tags were normalized to UniText inline tags.");

            result = new ConversionResult
            {
                text = output.ToString(),
                styles = styles,
                warnings = warnings.Count == 0 ? null : warnings,
                createdAssetPaths = createdAssetPaths is { Count: > 0 }
                    ? createdAssetPaths
                    : null,
            };
            return true;
        }

        public void RollbackCreated(IReadOnlyList<string> assetPaths)
        {
            if (assetPaths == null || assetPaths.Count == 0) return;
            for (var i = assetPaths.Count - 1; i >= 0; i--)
            {
                var path = assetPaths[i];
                if (string.IsNullOrEmpty(path) ||
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null)
                    continue;
                if (AssetDatabase.DeleteAsset(path))
                    log.Add(new LogEntry(LogSeverity.Info,
                        $"Removed uncommitted generated sprite catalog: {path}"));
                else
                    log.Add(new LogEntry(LogSeverity.Error,
                        $"Could not remove uncommitted generated sprite catalog: {path}"));
            }
            catalogByGuid.Clear();
        }

        bool TryResolveTag(TagData tag, UnityEngine.Object defaultAsset, Color componentColor,
            bool tintAll, bool hasVertexGradient, bool hasRangeColor,
            bool hasRangeGradient, bool hasAlphaMarkup, out ResolvedTag resolved,
            out string error)
        {
            resolved = null;
            error = null;

            UnityEngine.Object currentAsset;
            var selectedIndex = -1;
            var tint = false;
            string color = null;
            Color32 explicitColor = default;

            if (tag.hasPrimary && !tag.primaryNumeric)
            {
                if (!TryResolveNamedAsset(tag.primary, out currentAsset, out error)) return false;
            }
            else
            {
                if (tag.hasPrimary && !TryParseIndex(tag.primary, out selectedIndex))
                {
                    error = $"Invalid TMP sprite index '{tag.primary}'.";
                    return false;
                }
                currentAsset = defaultAsset;
                if (currentAsset == null)
                {
                    error = "A <sprite> tag uses the component/default TMP sprite asset, but none is assigned.";
                    return false;
                }
            }

            if (!TryReadAsset(currentAsset, out var currentData, out error)) return false;
            CharacterData character = null;
            if (selectedIndex >= 0 && !TryGetCharacter(currentData, selectedIndex, out character, out error))
                return false;

            for (var i = 0; i < tag.attributes.Count; i++)
            {
                var attribute = tag.attributes[i];
                switch (attribute.name.ToLowerInvariant())
                {
                    case "name":
                        if (string.IsNullOrEmpty(attribute.value))
                        {
                            error = "A TMP sprite name attribute has no value.";
                            return false;
                        }
                        if (!TryResolveName(currentData, attribute.value,
                                out currentData, out character, out error))
                            return false;
                        break;
                    case "index":
                        if (i != 0)
                        {
                            error = "TMP reads an index attribute from the first attribute slot; " +
                                    "this tag depends on that parser quirk and requires manual migration.";
                            return false;
                        }
                        if (!TryParseIndex(attribute.value, out selectedIndex) || selectedIndex < 0 ||
                            !TryGetCharacter(currentData, selectedIndex, out character, out error))
                        {
                            error ??= $"Invalid TMP sprite index '{attribute.value}'.";
                            return false;
                        }
                        break;
                    case "tint":
                        if (!TryParseNumber(attribute.value, out var tintValue))
                        {
                            error = $"Invalid TMP sprite tint value '{attribute.value}'.";
                            return false;
                        }
                        tint = tintValue != 0f;
                        break;
                    case "color":
                        color = NormalizeColor(attribute.value, out explicitColor);
                        break;
                    case "anim":
                        error = "Animated TMP <sprite anim=...> tags have no SpriteModifier equivalent.";
                        return false;
                    default:
                        error = $"TMP <sprite> attribute '{attribute.name}' is not supported by TMP's static sprite contract.";
                        return false;
                }
            }

            if (character == null)
            {
                error = "A TMP <sprite> tag does not select a valid name or index.";
                return false;
            }

            var effectiveTint = tintAll || tint;
            if (effectiveTint && color != null)
            {
                error = "TMP sprite color+tint multiplication cannot be represented by SpriteModifier without changing its result.";
                return false;
            }
            if (hasRangeColor || hasAlphaMarkup)
            {
                error = "TMP sprite alpha inside color or alpha markup cannot be represented " +
                        "without changing its result.";
                return false;
            }
            if (effectiveTint && (hasVertexGradient || hasRangeGradient))
            {
                error = "TMP sprite tint under range colors or vertex gradients cannot be represented by SpriteModifier.";
                return false;
            }

            if (!effectiveTint)
            {
                var componentAlpha = ((Color32)componentColor).a;
                if (color == null)
                {
                    if (componentAlpha < byte.MaxValue)
                        color = ColorToken(new Color32(byte.MaxValue, byte.MaxValue,
                            byte.MaxValue, componentAlpha));
                }
                else if (explicitColor.a > componentAlpha)
                {
                    explicitColor.a = componentAlpha;
                    color = ColorToken(explicitColor);
                }
            }

            resolved = new ResolvedTag
            {
                tag = tag,
                asset = currentData,
                character = character,
                colorToken = effectiveTint ? "i" : color,
            };
            return true;
        }

        bool TryResolveName(SpriteAssetData root, string name, out SpriteAssetData asset,
            out CharacterData character, out string error)
        {
            asset = null;
            character = null;
            error = null;

            if (hashMethod == null || searchByHashMethod == null)
            {
                error = "The installed TMP version does not expose its sprite-name resolver.";
                return false;
            }

            try
            {
                var hash = (int)hashMethod.Invoke(null, new object[] { name });
                var args = new object[] { root.asset, hash, true, -1 };
                var found = searchByHashMethod.Invoke(null, args) as UnityEngine.Object;
                if (found == null)
                {
                    error = $"TMP sprite name '{name}' does not resolve from '{root.name}'.";
                    return false;
                }
                if (!TryReadAsset(found, out asset, out error)) return false;
                return TryGetCharacter(asset, (int)args[3], out character, out error);
            }
            catch (TargetInvocationException exception)
            {
                error = exception.InnerException?.Message ?? exception.Message;
                return false;
            }
        }

        bool TryResolveNamedAsset(string name, out UnityEngine.Object asset, out string error)
        {
            asset = null;
            error = null;
            try
            {
                var path = settingsType?.GetProperty("defaultSpriteAssetPath",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
                asset = Resources.Load((path ?? "Sprite Assets/") + name, spriteAssetType);
            }
            catch (TargetInvocationException exception)
            {
                error = exception.InnerException?.Message ?? exception.Message;
                return false;
            }
            if (asset != null) return true;
            error = $"TMP sprite asset '{name}' is not available from TMP's Resources path; " +
                    "callback-only sprite assets require manual migration.";
            return false;
        }

        UnityEngine.Object ResolveSettingsDefaultAsset()
        {
            UnityEngine.Object asset = null;
            try
            {
                asset = settingsType?.GetProperty("defaultSpriteAsset",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as UnityEngine.Object;
            }
            catch (TargetInvocationException)
            {
                asset = null;
            }

            return asset ?? Resources.Load("Sprite Assets/Default Sprite Asset", spriteAssetType);
        }

        UnityEngine.Object ResolveSettingsDefaultFontAsset()
        {
            try
            {
                return settingsType?.GetProperty("defaultFontAsset",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as UnityEngine.Object;
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        bool TryReadAsset(UnityEngine.Object asset, out SpriteAssetData data, out string error)
        {
            data = null;
            error = null;
            if (asset == null || !spriteAssetType.IsInstanceOfType(asset))
            {
                error = "The resolved sprite source is not a TMP_SpriteAsset.";
                return false;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(guid))
            {
                error = $"TMP sprite asset '{asset.name}' is not a persistent project asset.";
                return false;
            }

            var serialized = new SerializedObject(asset);
            var characters = serialized.FindProperty("m_SpriteCharacterTable");
            var glyphs = serialized.FindProperty("m_GlyphTable") ??
                         serialized.FindProperty("m_SpriteGlyphTable");
            if (characters == null || !characters.isArray || glyphs == null || !glyphs.isArray)
            {
                error = $"TMP sprite asset '{asset.name}' has no readable character/glyph tables.";
                return false;
            }

            var glyphByIndex = new Dictionary<uint, GlyphData>();
            for (var i = 0; i < glyphs.arraySize; i++)
            {
                var element = glyphs.GetArrayElementAtIndex(i);
                if (!TryReadGlyph(element, out var glyph, out error))
                {
                    error = $"TMP sprite asset '{asset.name}', glyph {i}: {error}";
                    return false;
                }
                if (!glyphByIndex.ContainsKey(glyph.index)) glyphByIndex.Add(glyph.index, glyph);
            }

            var characterList = new List<CharacterData>(characters.arraySize);
            for (var i = 0; i < characters.arraySize; i++)
            {
                var element = characters.GetArrayElementAtIndex(i);
                var glyphIndex = (uint)ReadLong(element.FindPropertyRelative("m_GlyphIndex"));
                if (!glyphByIndex.TryGetValue(glyphIndex, out var glyph))
                {
                    error = $"TMP sprite asset '{asset.name}' character {i} references missing glyph {glyphIndex}.";
                    return false;
                }
                characterList.Add(new CharacterData
                {
                    index = i,
                    name = element.FindPropertyRelative("m_Name")?.stringValue,
                    scale = ReadFloat(element.FindPropertyRelative("m_Scale"), 1f),
                    glyph = glyph,
                });
            }

            data = new SpriteAssetData
            {
                asset = asset,
                guid = guid,
                path = path,
                name = asset.name,
                sheet = serialized.FindProperty("spriteSheet")?.objectReferenceValue as Texture2D,
                face = ReadFace(serialized.FindProperty("m_FaceInfo")),
                characters = characterList,
            };
            return true;
        }

        static bool TryReadGlyph(SerializedProperty element, out GlyphData glyph, out string error)
        {
            glyph = null;
            error = null;
            var metrics = element.FindPropertyRelative("m_Metrics");
            var rect = element.FindPropertyRelative("m_GlyphRect");
            if (metrics == null || rect == null)
            {
                error = "missing metrics or glyph rect";
                return false;
            }

            var width = ReadFloat(metrics.FindPropertyRelative("m_Width"));
            var height = ReadFloat(metrics.FindPropertyRelative("m_Height"));
            var rectWidth = ReadLong(rect.FindPropertyRelative("m_Width"));
            var rectHeight = ReadLong(rect.FindPropertyRelative("m_Height"));
            if (width <= 0f || height <= 0f || rectWidth <= 0 || rectHeight <= 0)
            {
                error = "non-positive dimensions";
                return false;
            }

            glyph = new GlyphData
            {
                index = (uint)ReadLong(element.FindPropertyRelative("m_Index")),
                width = width,
                height = height,
                bearingX = ReadFloat(metrics.FindPropertyRelative("m_HorizontalBearingX")),
                bearingY = ReadFloat(metrics.FindPropertyRelative("m_HorizontalBearingY")),
                advance = ReadFloat(metrics.FindPropertyRelative("m_HorizontalAdvance")),
                scale = ReadFloat(element.FindPropertyRelative("m_Scale"), 1f),
                rect = new Rect(
                    ReadLong(rect.FindPropertyRelative("m_X")),
                    ReadLong(rect.FindPropertyRelative("m_Y")),
                    rectWidth, rectHeight),
                sprite = element.FindPropertyRelative("sprite")?.objectReferenceValue as Sprite,
            };
            return true;
        }

        static bool TryGetCharacter(SpriteAssetData data, int index,
            out CharacterData character, out string error)
        {
            if ((uint)index < (uint)data.characters.Count)
            {
                character = data.characters[index];
                error = null;
                return true;
            }
            character = null;
            error = $"TMP sprite index {index} is outside '{data.name}' ({data.characters.Count} entries).";
            return false;
        }

        /// <summary>
        /// The generated catalog for one TMP sprite asset, creating it when
        /// <paramref name="create"/> is set and none exists. Only the ownership label decides
        /// which catalog belongs to which asset: the revision label records what the catalog was
        /// built from and changes with any re-import, which says nothing about whether the entries
        /// still match. Content is settled by <see cref="TryValidateCatalog"/>, entry by entry.
        /// </summary>
        bool TryGetCatalog(SpriteAssetData source, bool create,
            out UniTextSprites catalog, out string createdAssetPath, out string error)
        {
            createdAssetPath = null;
            error = null;
            var label = CatalogLabelPrefix + source.guid;
            if (catalogByGuid.TryGetValue(source.guid, out catalog))
            {
                var labels = catalog == null ? Array.Empty<string>() : AssetDatabase.GetLabels(catalog);
                if (Array.IndexOf(labels, label) >= 0) return true;
                catalogByGuid.Remove(source.guid);
                catalog = null;
            }

            var matches = AssetDatabase.FindAssets($"l:{label}");
            for (var i = 0; i < matches.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(matches[i]);
                var candidate = AssetDatabase.LoadAssetAtPath<UniTextSprites>(path);
                var labels = candidate == null
                    ? Array.Empty<string>()
                    : AssetDatabase.GetLabels(candidate);
                if (candidate == null || Array.IndexOf(labels, label) < 0)
                    continue;
                if (catalog != null)
                {
                    error = $"More than one UniTextSprites catalog is labelled for TMP asset {source.guid}.";
                    return false;
                }
                catalog = candidate;
            }

            if (catalog != null)
            {
                catalogByGuid[source.guid] = catalog;
                return true;
            }

            if (!create) return true;
            return TryCreateCatalog(source, label, RevisionLabel(source), out catalog,
                out createdAssetPath, out error);
        }

        static string RevisionLabel(SpriteAssetData source)
            => CatalogRevisionLabelPrefix + AssetDatabase.GetAssetDependencyHash(source.path);

        /// <summary>
        /// Re-stamps the catalog with the revision of the TMP asset it was just validated against,
        /// keeping every label the user put on it. Writes a <c>.meta</c> file, so only the pass
        /// that is allowed to create assets may call it.
        /// </summary>
        static void RefreshRevisionLabel(UniTextSprites catalog, SpriteAssetData source)
        {
            var revision = RevisionLabel(source);
            var existing = AssetDatabase.GetLabels(catalog);
            var kept = new List<string>(existing.Length + 1);
            for (var i = 0; i < existing.Length; i++)
            {
                if (existing[i].StartsWith(CatalogRevisionLabelPrefix, StringComparison.Ordinal))
                    continue;
                kept.Add(existing[i]);
            }
            if (kept.Count == existing.Length && Array.IndexOf(existing, revision) >= 0) return;
            kept.Add(revision);
            AssetDatabase.SetLabels(catalog, kept.ToArray());
        }

        bool TryCreateCatalog(SpriteAssetData source, string label, string revision,
            out UniTextSprites catalog, out string createdAssetPath, out string error)
        {
            catalog = ScriptableObject.CreateInstance<UniTextSprites>();
            createdAssetPath = null;
            error = null;
            var generatedSprites = new Dictionary<uint, Sprite>();
            var transientSprites = new List<Sprite>();
            string assetPath = null;

            try
            {
                var unrenderable = 0;
                for (var i = 0; i < source.characters.Count; i++)
                {
                    var character = source.characters[i];
                    if (!TryResolveSprite(source, character.glyph, generatedSprites,
                            transientSprites, out var sprite, out var glyphError))
                    {
                        unrenderable++;
                        log.Add(new LogEntry(LogSeverity.Warning,
                            $"  Left out of the '{source.name}' catalog: {glyphError}"));
                        continue;
                    }
                    catalog.Entries.Add(CreateEntry(
                        i.ToString(CultureInfo.InvariantCulture), source, character, sprite));
                }

                var folder = EnsureCatalogFolder();
                var fileName = SanitizeFileName(source.name) + " [" + source.guid + "].asset";
                assetPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + fileName);
                AssetDatabase.CreateAsset(catalog, assetPath);
                for (var i = 0; i < transientSprites.Count; i++)
                    AssetDatabase.AddObjectToAsset(transientSprites[i], catalog);
                AssetDatabase.SetLabels(catalog, new[] { label, revision });
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssetIfDirty(catalog);
                catalogByGuid[source.guid] = catalog;
                createdAssetPath = assetPath;
                log.Add(new LogEntry(LogSeverity.Info,
                    unrenderable == 0
                        ? $"Created UniTextSprites for '{source.name}': {assetPath}"
                        : $"Created UniTextSprites for '{source.name}': {assetPath} — " +
                          $"{unrenderable} glyph(s) the TMP asset cannot render are absent, and " +
                          "only the text that writes them is refused."));
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (!string.IsNullOrEmpty(assetPath) &&
                    AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }
                else
                {
                    for (var i = 0; i < transientSprites.Count; i++)
                        ObjectUtils.SafeDestroy(transientSprites[i]);
                    ObjectUtils.SafeDestroy(catalog);
                }
                catalog = null;
                createdAssetPath = null;
                return false;
            }
        }

        static InlineSprite CreateEntry(string name, SpriteAssetData source,
            CharacterData character, Sprite sprite)
        {
            var factor = EntryMetricFactor(source, character);
            var glyph = character.glyph;
            return new InlineSprite
            {
                Name = name,
                Sprite = sprite,
                Color = Color.white,
                PreserveAspect = false,
                Size = new Vector2(glyph.width * factor, glyph.height * factor),
                BearingOffset = new Vector2(
                    glyph.bearingX * factor,
                    (glyph.bearingY - glyph.height) * factor),
                Advance = glyph.advance * factor,
                Pivot = Vector2.zero,
            };
        }

        static float EntryMetricFactor(SpriteAssetData source, CharacterData character)
        {
            var glyph = character.glyph;
            if (source.face.IsUsable)
                return character.scale * glyph.scale * source.face.scale / source.face.pointSize;
            return character.scale * glyph.scale / glyph.height;
        }

        /// <summary>
        /// Whether every sprite this text actually writes can be rendered. A TMP sprite asset can
        /// carry glyphs whose rect no longer fits its sheet — replacing the texture leaves the old
        /// layout behind — and such a glyph refuses only the occurrences that name it. The sheet
        /// itself is asset-wide: with none, nothing in the asset resolves.
        /// </summary>
        static bool TryValidateUsedSprites(SpriteAssetData source,
            Dictionary<int, CharacterData> used, out string error)
        {
            if (source.sheet == null)
            {
                error = $"TMP sprite asset '{source.name}' has no Texture2D sprite sheet.";
                return false;
            }
            foreach (var character in used)
                if (!TryValidateSprite(source, character.Value.glyph, out error)) return false;
            error = null;
            return true;
        }

        static bool TryValidateSprite(SpriteAssetData source, GlyphData glyph, out string error)
        {
            if (source.sheet == null)
            {
                error = $"TMP sprite asset '{source.name}' has no Texture2D sprite sheet.";
                return false;
            }
            if (glyph.rect.xMin < 0f || glyph.rect.yMin < 0f ||
                glyph.rect.xMax > source.sheet.width || glyph.rect.yMax > source.sheet.height)
            {
                error = $"TMP sprite glyph {glyph.index} sits at " +
                        $"({glyph.rect.x}, {glyph.rect.y}) {glyph.rect.width}x{glyph.rect.height}, " +
                        $"which reaches past '{source.sheet.name}' at " +
                        $"{source.sheet.width}x{source.sheet.height}. Fix that row's Glyph Rect " +
                        "in the TMP Sprite Asset's Glyph Table, or rebuild the asset from the " +
                        "texture.";
                return false;
            }
            error = null;
            return true;
        }

        static bool TryValidateCatalog(UniTextSprites catalog, SpriteAssetData source,
            out string error)
        {
            for (var i = 0; i < source.characters.Count; i++)
            {
                var key = i.ToString(CultureInfo.InvariantCulture);
                var character = source.characters[i];
                var glyph = character.glyph;

                if (!TryValidateSprite(source, glyph, out _))
                {
                    if (!catalog.TryGet(key, out var unrenderable) || unrenderable == null)
                        continue;
                    error = $"Generated catalog '{AssetDatabase.GetAssetPath(catalog)}' still " +
                            $"carries a '{key}' entry that its TMP sprite asset can no longer " +
                            "render. Delete the catalog and migrate again.";
                    return false;
                }

                if (!catalog.TryGet(key, out var entry) || entry == null)
                {
                    error = $"Generated catalog '{AssetDatabase.GetAssetPath(catalog)}' has no '{key}' entry.";
                    return false;
                }

                var factor = EntryMetricFactor(source, character);
                if (!IsExactSprite(entry.Sprite, source.sheet, glyph.rect) ||
                    !Approximately(entry.Size,
                        new Vector2(glyph.width * factor, glyph.height * factor)) ||
                    !Approximately(entry.BearingOffset, new Vector2(glyph.bearingX * factor,
                        (glyph.bearingY - glyph.height) * factor)) ||
                    !Mathf.Approximately(entry.Advance, glyph.advance * factor) ||
                    entry.Color != Color.white || entry.PreserveAspect ||
                    entry.Pivot != Vector2.zero || !Mathf.Approximately(entry.Rotation, 0f) ||
                    !Mathf.Approximately(entry.LineHeightAbove, 0f) ||
                    !Mathf.Approximately(entry.LineHeightBelow, 0f))
                {
                    error = $"Generated catalog '{AssetDatabase.GetAssetPath(catalog)}' has a " +
                            $"modified or stale '{key}' entry.";
                    return false;
                }
            }
            error = null;
            return true;
        }

        static bool IsExactSprite(Sprite sprite, Texture2D sheet, Rect rect)
        {
            if (sprite == null || sheet == null || sprite.texture != sheet ||
                sprite.packingRotation != SpritePackingRotation.None)
                return false;
            try
            {
                return Approximately(sprite.textureRect, rect);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        static bool Approximately(Vector2 left, Vector2 right)
            => Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);

        static bool Approximately(Rect left, Rect right)
            => Approximately(left.position, right.position) &&
               Approximately(left.size, right.size);

        static bool TryResolveSprite(SpriteAssetData source, GlyphData glyph,
            Dictionary<uint, Sprite> generated, List<Sprite> transient,
            out Sprite sprite, out string error)
        {
            error = null;
            if (generated.TryGetValue(glyph.index, out sprite)) return true;
            if (!TryValidateSprite(source, glyph, out error)) return false;
            if (IsExactSprite(glyph.sprite, source.sheet, glyph.rect))
            {
                sprite = glyph.sprite;
                return true;
            }

            sprite = Sprite.Create(source.sheet, glyph.rect, new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect);
            sprite.name = $"TMP Sprite {glyph.index}";
            generated.Add(glyph.index, sprite);
            transient.Add(sprite);
            return true;
        }

        static void AddComponentMetrics(SpriteModifier modifier, string key,
            SpriteAssetData source, CharacterData character, FaceData fontFace)
        {
            var glyph = character.glyph;
            var fontDependent = !source.face.IsUsable;
            var item = new InlineSpriteOverride { Key = key };
            var needed = false;
            float factor;
            float baseline;
            if (fontDependent)
            {
                factor = character.scale * glyph.scale * fontFace.AscentEm / glyph.height;
                baseline = fontFace.baseline * fontFace.scale * fontFace.scale /
                           fontFace.pointSize;
                item.Size = new Vector2(glyph.width * factor, glyph.height * factor);
                item.BearingOffset = new Vector2(
                    glyph.bearingX * factor,
                    baseline + (glyph.bearingY - glyph.height) * factor);
                item.Advance = glyph.advance * factor;
                needed = true;
            }
            else
            {
                factor = EntryMetricFactor(source, character);
                baseline = source.face.baseline * fontFace.scale / fontFace.pointSize *
                           source.face.scale;
                if (Mathf.Abs(baseline) > 0.0001f)
                {
                    item.BearingOffset = new Vector2(
                        glyph.bearingX * factor,
                        baseline + (glyph.bearingY - glyph.height) * factor);
                    needed = true;
                }
            }

            var spriteAscent = fontDependent
                ? fontFace.AscentEm
                : source.face.ascent * character.scale * glyph.scale *
                  source.face.scale / source.face.pointSize;
            var spriteDescent = fontDependent
                ? fontFace.DescentEm
                : source.face.descent * character.scale * glyph.scale *
                  source.face.scale / source.face.pointSize;
            var above = Mathf.Max(0f, spriteAscent - fontFace.AscentEm);
            var below = Mathf.Max(0f, fontFace.DescentEm - spriteDescent);
            if (above > 0.0001f)
            {
                item.LineHeightAbove = above;
                needed = true;
            }
            if (below > 0.0001f)
            {
                item.LineHeightBelow = below;
                needed = true;
            }

            if (needed) modifier.Overrides.Add(item);
        }

        string EnsureCatalogFolder()
        {
            var folder = projectFolder + "/" + CatalogFolderName;
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(projectFolder, CatalogFolderName);
            return folder;
        }

        static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "TMP Sprites";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == '/' || chars[i] == '\\')
                    chars[i] = '_';
            return new string(chars);
        }

        static bool HasActiveTagAt(string input, int limit, string name,
            bool includeColorShorthand = false)
        {
            var depth = 0;
            var index = 0;
            while (index < limit)
            {
                var start = input.IndexOf('<', index);
                if (start < 0 || start >= limit) break;
                if (includeColorShorthand && start + 1 < limit && input[start + 1] == '#')
                {
                    depth++;
                }
                else if (StartsWithTagName(input, start, name, out _, out var closing))
                {
                    if (closing)
                        depth = Mathf.Max(0, depth - 1);
                    else
                        depth++;
                }
                var end = input.IndexOf('>', start + 1);
                index = end < 0 || end >= limit ? start + 1 : end + 1;
            }
            return depth > 0;
        }

        static bool HasOpeningTagBefore(string input, int limit, string name)
        {
            var index = 0;
            while (index < limit)
            {
                var start = input.IndexOf('<', index);
                if (start < 0 || start >= limit) return false;
                if (StartsWithTagName(input, start, name, out _, out var closing) && !closing)
                    return true;
                index = start + 1;
            }
            return false;
        }

        static bool TryParseTags(string input, out List<TagData> tags, out string error)
        {
            tags = new List<TagData>();
            error = null;
            var index = 0;
            while (index < input.Length)
            {
                var start = input.IndexOf('<', index);
                if (start < 0) break;
                if (!StartsWithTagName(input, start, "sprite", out var afterName, out var closing))
                {
                    index = start + 1;
                    continue;
                }
                if (closing)
                {
                    error = "Closing </sprite> is not a valid TMP sprite tag and would change under inline semantics.";
                    return false;
                }
                if (HasActiveTagAt(input, start, "noparse"))
                {
                    error = "A TMP <sprite> token inside <noparse> is literal text and would be " +
                            "consumed by UniText inline semantics.";
                    return false;
                }
                if (!TryParseTag(input, start, afterName, out var tag, out error)) return false;
                tags.Add(tag);
                index = tag.end;
            }
            return true;
        }

        static bool StartsWithTagName(string input, int start, string name,
            out int afterName, out bool closing)
        {
            afterName = start + 1;
            closing = false;
            if (afterName < input.Length && input[afterName] == '/')
            {
                closing = true;
                afterName++;
            }
            if (afterName + name.Length > input.Length ||
                !input.AsSpan(afterName, name.Length).Equals(
                    name.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return false;
            afterName += name.Length;
            if (afterName >= input.Length) return false;
            var boundary = input[afterName];
            return boundary == '=' || boundary == '>' || boundary == '/' || boundary == ' ';
        }

        static bool TryParseTag(string input, int start, int afterName,
            out TagData tag, out string error)
        {
            tag = new TagData { start = start };
            error = null;
            if (!TryFindTagEnd(input, afterName, out var end))
            {
                error = "Unterminated TMP <sprite> tag.";
                return false;
            }
            tag.end = end + 1;
            if (end - start > 128)
            {
                error = "TMP sprite tag exceeds TMP's 128-character parser limit.";
                return false;
            }

            var bodyEnd = end;
            if (bodyEnd > afterName && input[bodyEnd - 1] == '/')
            {
                if (bodyEnd - 1 > afterName && input[bodyEnd - 2] == ' ')
                {
                    error = "TMP does not treat whitespace before '/>' as sprite self-closing syntax.";
                    return false;
                }
                bodyEnd--;
            }

            var position = afterName;
            if (position < bodyEnd && input[position] == '=')
            {
                position++;
                if (!TryReadValue(input, ref position, bodyEnd, out tag.primary,
                        out var quoted, out error))
                    return false;
                tag.hasPrimary = true;
                tag.primaryNumeric = !quoted && IsTmpNumericStart(tag.primary[0]);
                if (!tag.primaryNumeric && !quoted && position < bodyEnd)
                {
                    error = "An explicit TMP sprite asset followed by attributes must use double quotes.";
                    return false;
                }
            }

            while (position < bodyEnd)
            {
                while (position < bodyEnd && input[position] == ' ') position++;
                if (position >= bodyEnd) break;
                var nameStart = position;
                while (position < bodyEnd && input[position] != ' ' &&
                       input[position] != '=') position++;
                if (position == nameStart)
                {
                    error = "Malformed TMP <sprite> attribute.";
                    return false;
                }
                var name = input.Substring(nameStart, position - nameStart);
                while (position < bodyEnd && input[position] == ' ') position++;
                string value = null;
                var quoted = false;
                if (position < bodyEnd && input[position] == '=')
                {
                    position++;
                    if (!TryReadValue(input, ref position, bodyEnd, out value,
                            out quoted, out error))
                        return false;
                }
                tag.attributes.Add(new AttributeData(name, value));
                if (tag.attributes.Count > 7)
                {
                    error = "TMP sprite tag exceeds TMP's attribute limit.";
                    return false;
                }
                if (string.Equals(name, "name", StringComparison.OrdinalIgnoreCase) &&
                    !quoted && position < bodyEnd)
                {
                    error = "A TMP sprite name followed by attributes must use double quotes.";
                    return false;
                }
            }

            tag.legacyNumericOnly = tag.hasPrimary && tag.primaryNumeric &&
                                    tag.attributes.Count == 0 &&
                                    TryParseIndex(tag.primary, out _);
            return true;
        }

        static bool TryFindTagEnd(string input, int start, out int end)
        {
            end = input.IndexOf('>', start);
            return end >= 0;
        }

        static bool TryReadValue(string input, ref int position, int end,
            out string value, out bool quoted, out string error)
        {
            value = null;
            quoted = false;
            error = null;
            if (position >= end)
            {
                error = "TMP <sprite> attribute has no value.";
                return false;
            }

            if (input[position] == '\'')
            {
                error = "TMP sprite values use double quotes; single quotes are literal characters.";
                return false;
            }
            if (input[position] == '"')
            {
                quoted = true;
                position++;
                var start = position;
                while (position < end && input[position] != '"') position++;
                if (position >= end)
                {
                    error = "Unterminated quoted TMP <sprite> value.";
                    return false;
                }
                value = input.Substring(start, position - start);
                position++;
                return true;
            }

            var valueStart = position;
            while (position < end && input[position] != ' ') position++;
            value = input.Substring(valueStart, position - valueStart);
            return value.Length > 0;
        }

        static bool IsTmpNumericStart(char value)
            => value == '+' || value == '-' || value == '.' || value is >= '0' and <= '9';

        static bool TryParseIndex(string value, out int index)
        {
            index = -1;
            if (!TryParseNumber(value, out var number) || float.IsNaN(number) ||
                float.IsInfinity(number) || number < 0f || number > short.MaxValue)
                return false;
            index = (int)number;
            return true;
        }

        static bool TryParseNumber(string value, out float number)
            => float.TryParse(value,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out number);

        /// <summary>
        /// The canonical token for a TMP <c>color</c> attribute. Never refuses: TMP itself reads
        /// anything that is not a 6- or 8-digit hex triple as opaque white, and a refusal here
        /// would block markup TMP renders.
        /// </summary>
        static string NormalizeColor(string value, out Color32 color)
        {
            color = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
            if (value?.Length == 7)
            {
                color.r = HexByte(value[1], value[2]);
                color.g = HexByte(value[3], value[4]);
                color.b = HexByte(value[5], value[6]);
            }
            else if (value?.Length == 9)
            {
                color.r = HexByte(value[1], value[2]);
                color.g = HexByte(value[3], value[4]);
                color.b = HexByte(value[5], value[6]);
                color.a = HexByte(value[7], value[8]);
            }
            return ColorToken(color);
        }

        static byte HexByte(char high, char low) => (byte)(HexNibble(high) * 16 + HexNibble(low));

        static byte HexNibble(char value) => value switch
        {
            >= '0' and <= '9' => (byte)(value - '0'),
            >= 'A' and <= 'F' => (byte)(value - 'A' + 10),
            >= 'a' and <= 'f' => (byte)(value - 'a' + 10),
            _ => 15,
        };

        static string ColorToken(Color32 color)
            => $"#{color.r:X2}{color.g:X2}{color.b:X2}{color.a:X2}";

        static bool TryReadFace(UnityEngine.Object asset, out FaceData face)
        {
            face = default;
            if (asset == null) return false;
            face = ReadFace(new SerializedObject(asset).FindProperty("m_FaceInfo"));
            return face.IsUsable;
        }

        static FaceData ReadFace(SerializedProperty property)
        {
            if (property == null) return default;
            return new FaceData(
                ReadNumeric(property.FindPropertyRelative("m_PointSize")),
                ReadFloat(property.FindPropertyRelative("m_Scale"), 1f),
                ReadFloat(property.FindPropertyRelative("m_AscentLine")),
                ReadFloat(property.FindPropertyRelative("m_DescentLine")),
                ReadFloat(property.FindPropertyRelative("m_Baseline")));
        }

        static float ReadFloat(SerializedProperty property, float fallback = 0f)
            => property == null ? fallback : property.floatValue;

        /// <summary>
        /// Reads a face metric whose serialized width is not fixed across editor versions —
        /// <c>FaceInfo.m_PointSize</c> is an integer before Unity 2023.3 and a float from it on.
        /// The runtime property type decides, so no version branch is needed.
        /// </summary>
        static float ReadNumeric(SerializedProperty property, float fallback = 0f)
            => property == null ? fallback
                : property.propertyType == SerializedPropertyType.Float ? property.floatValue
                : property.propertyType == SerializedPropertyType.Integer ? property.longValue
                : fallback;

        static long ReadLong(SerializedProperty property)
            => property == null ? 0L : property.longValue;
    }
}
