using System.Collections.Generic;
using UnityEngine.UIElements;

namespace LightSide
{
    internal static class ModifierGraphIssueRenderer
    {
        public static void Append(VisualElement container,
            IReadOnlyList<ModifierGraphIssue> issues, string owner = null)
        {
            var ownerPrefix = string.IsNullOrEmpty(owner) ? string.Empty : $"{owner}: ";
            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                var pathPrefix = string.IsNullOrEmpty(issue.path)
                    ? string.Empty
                    : $"{issue.path}: ";
                container.Add(new HelpBox($"{ownerPrefix}{pathPrefix}{issue.message}",
                    HelpBoxMessageType.Error));
            }
        }
    }
}
