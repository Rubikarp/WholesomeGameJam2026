using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace LightSide
{
    /// <summary>
    /// The LightSide command palette: gathers every <see cref="ILightSideCommands"/> provider in the
    /// project, asks each what it can do in the current context, and presents the result as one
    /// searchable menu grouped by package. Groups are ordered by package display name with Core last.
    /// </summary>
    public static class LightSideCommands
    {
        private const string CorePackage = "media.lightside.core";

        /// <summary>
        /// The palette never opens narrower than this. Its rows carry a nested command path and an
        /// icon, which the selector's own compact measurement — sized for value dropdowns — has no
        /// reason to allow for. Above this the content still decides.
        /// </summary>
        internal const float MinimumWidth = 380f;

        private sealed class PackageGroup
        {
            public string Name;
            public string DisplayName;
            public bool Core;
            public readonly List<ILightSideCommands> Providers = new();
        }

        private static PackageGroup[] groups;

        /// <summary>
        /// Opens the palette for <paramref name="surface"/> against a screen-space anchor, over the
        /// current editor selection. Opening from a layout pass requires the anchor to have been
        /// captured there — see <see cref="ScreenRect.FromPanel"/>.
        /// </summary>
        public static void Open(CommandSurface surface, ScreenRect anchor)
            => Open(surface, anchor, null);

        internal static void Open(CommandSurface surface, ScreenRect anchor, GameObject target)
            => Open(new CommandContext(surface, anchor, Selection.objects, target));

        internal static void Open(in CommandContext context)
        {
            var menu = new DropdownMenu();
            var all = Groups();
            for (var i = 0; i < all.Length; i++)
            {
                var commands = new CommandMenu(menu, all[i].DisplayName, i, all[i].Name);
                foreach (var provider in all[i].Providers)
                    provider.Populate(commands, context);
            }
            InspectorContextMenu.ShowAtScreenRect(Widened(context.Anchor), menu);
        }

        /// <summary>The anchor the palette opens against: the selector takes the wider of the anchor and its own measurement, so the floor is set here.</summary>
        private static ScreenRect Widened(ScreenRect anchor)
        {
            var rect = anchor.Value;
            if (rect.width >= MinimumWidth) return anchor;
            rect.width = MinimumWidth;
            return new ScreenRect(rect);
        }

        private static PackageGroup[] Groups()
        {
            if (groups != null) return groups;

            var byPackage = new Dictionary<string, PackageGroup>(StringComparer.Ordinal);
            foreach (var type in TypeCache.GetTypesDerivedFrom<ILightSideCommands>())
            {
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null) continue;

                var package = PackageInfo.FindForAssembly(type.Assembly);
                var name = string.IsNullOrEmpty(package?.name)
                    ? type.Assembly.GetName().Name
                    : package.name;
                if (!byPackage.TryGetValue(name, out var group))
                    byPackage[name] = group = new PackageGroup
                    {
                        Name = name,
                        DisplayName = GroupName(package, name),
                        Core = name == CorePackage,
                    };
                group.Providers.Add((ILightSideCommands)Activator.CreateInstance(type));
            }

            var ordered = new List<PackageGroup>(byPackage.Values);
            ordered.Sort(Compare);
            return groups = ordered.ToArray();
        }

        /// <summary>A package's header text. A slash in the manifest's display name would split the group, so it is folded away.</summary>
        private static string GroupName(PackageInfo package, string fallback)
        {
            var name = string.IsNullOrEmpty(package?.displayName) ? fallback : package.displayName;
            return name.IndexOf('/') < 0 ? name : name.Replace('/', '-');
        }

        private static int Compare(PackageGroup a, PackageGroup b)
            => a.Core != b.Core
                ? a.Core ? 1 : -1
                : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
    }
}
