using System;
using System.Collections.Generic;

namespace LightSide
{
    /// <summary>
    /// Reads TMP markup and reports what the text needs, leaving the text itself untouched: a
    /// UniText TagRule carries whatever name the markup already uses, so a tag with a modifier
    /// behind it only needs a Style entry on the component.
    /// </summary>
    internal static class RichTextConverter
    {
        public struct ConversionResult
        {
            public string text;
            public List<string> warnings;
            /// <summary>TMP tag names found that need Style entries (modifier + TagRule) on the component.</summary>
            public List<RequiredStyle> requiredStyles;
            /// <summary>Tag names found whose modifier a shared Style source is expected to supply.</summary>
            public List<string> sharedTags;
        }

        /// <summary>A Style entry that the migrator should add to the UniText component.</summary>
        public struct RequiredStyle
        {
            public string tagName;
            public string modifierTypeName;
            public string defaultParameter;
            /// <summary>Set instead of <see cref="modifierTypeName"/> when the rule needs no modifier.</summary>
            public string standaloneRuleTypeName;
        }

        /// <summary>
        /// Analyze TMP rich text. Text is returned UNMODIFIED.
        /// Only collects warnings (unsupported tags) and required styles (tags needing a modifier).
        /// </summary>
        public static ConversionResult Convert(string input)
        {
            if (string.IsNullOrEmpty(input))
                return new ConversionResult { text = input };

            var warnings = new List<string>();
            var requiredStyles = new List<RequiredStyle>();
            var sharedTags = new List<string>();
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;

            while (i < input.Length)
            {
                if (input[i] == '<')
                {
                    var tag = ParseTag(input, i);
                    if (tag.valid)
                    {
                        AnalyzeTag(tag, warnings, requiredStyles, sharedTags, seenTags);
                        i = tag.endIndex;
                        continue;
                    }
                }
                i++;
            }

            return new ConversionResult
            {
                text = input,
                warnings = warnings.Count > 0 ? warnings : null,
                requiredStyles = requiredStyles.Count > 0 ? requiredStyles : null,
                sharedTags = sharedTags.Count > 0 ? sharedTags : null,
            };
        }

        struct TagParseResult
        {
            public bool valid;
            public string tagName;
            public string parameter;
            public int endIndex;
        }

        static TagParseResult ParseTag(string text, int start)
        {
            var result = new TagParseResult();

            if (start >= text.Length || text[start] != '<')
                return result;

            int end = text.IndexOf('>', start);
            if (end < 0)
                return result;

            result.endIndex = end + 1;

            int pos = start + 1;

            if (pos < end && text[pos] == '/')
                pos++;

            int nameStart = pos;
            while (pos < end && text[pos] != '=' && text[pos] != ' ' && text[pos] != '/')
                pos++;

            if (pos == nameStart)
                return result;

            result.tagName = text.Substring(nameStart, pos - nameStart).ToLowerInvariant();

            if (pos < end && text[pos] == '=')
            {
                pos++;
                int paramStart = pos;

                if (pos < end && (text[pos] == '"' || text[pos] == '\''))
                {
                    char quote = text[pos];
                    pos++;
                    paramStart = pos;
                    while (pos < end && text[pos] != quote)
                        pos++;
                    result.parameter = text.Substring(paramStart, pos - paramStart);
                }
                else
                {
                    while (pos < end && text[pos] != '/' && text[pos] != ' ')
                        pos++;
                    result.parameter = text.Substring(paramStart, pos - paramStart);
                }
            }

            result.valid = true;
            return result;
        }

        static void AnalyzeTag(TagParseResult tag, List<string> warnings,
            List<RequiredStyle> requiredStyles, List<string> sharedTags, HashSet<string> seenTags)
        {
            var name = tag.tagName;

            if (MigrationMapping.TagVocabulary.TryGetValue(name, out var binding))
            {
                if (!sharedTags.Contains(name)) sharedTags.Add(name);
                if (seenTags.Add(name) && binding.GraphPresetName == null)
                {
                    requiredStyles.Add(new RequiredStyle
                    {
                        tagName = name,
                        modifierTypeName = binding.ModifierTypeName,
                        defaultParameter = binding.DefaultParameter ?? tag.parameter,
                        standaloneRuleTypeName = binding.StandaloneRuleTypeName,
                    });
                }

                if (MigrationMapping.TagValueNotes.TryGetValue(name, out var note))
                {
                    var value = $"<{name}>: {note}";
                    if (!warnings.Contains(value)) warnings.Add(value);
                }
                return;
            }

            if (MigrationMapping.TagsNeedingManualSetup.TryGetValue(name, out var setup))
            {
                var manual = $"<{name}>: {setup}";
                if (!warnings.Contains(manual)) warnings.Add(manual);
                return;
            }

            if (MigrationMapping.UnsupportedTags.Contains(name))
            {
                var w = tag.parameter != null
                    ? $"<{name}={tag.parameter}> has no UniText modifier — will appear as literal text"
                    : $"<{name}> has no UniText modifier — will appear as literal text";
                if (!warnings.Contains(w))
                    warnings.Add(w);
                return;
            }

            if (!MigrationMapping.TmpTags.Contains(name) ||
                MigrationMapping.TagsOwnedElsewhere.Contains(name)) return;

            var unclassified =
                $"<{name}> is TMP markup this migration has no entry for — it is carried over " +
                "unchanged and will appear as literal text. Report it: the tag tables are " +
                "missing a case.";
            if (!warnings.Contains(unclassified))
                warnings.Add(unclassified);
        }
    }
}
