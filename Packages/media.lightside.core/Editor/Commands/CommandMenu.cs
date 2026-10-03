using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Collects one package's commands for a single palette opening. Every command lands in that
    /// package's group; a slash in a command's path nests it further inside that group.
    /// </summary>
    public sealed class CommandMenu
    {
        private readonly DropdownMenu menu;
        private readonly string group;
        private readonly int order;
        private readonly object accent;

        internal CommandMenu(DropdownMenu menu, string group, int order, object accent)
        {
            this.menu = menu;
            this.group = group;
            this.order = order;
            this.accent = accent;
        }

        private string groupIconName;
        private Texture groupIcon;

        /// <summary>
        /// Name of the registered icon on the package's group header, none by default. Set it before
        /// appending: the header takes its icon from the first command that reaches it. A header
        /// cannot recolour at draw time, so a single-tone glyph is theme-tinted here instead.
        /// </summary>
        public string GroupIcon
        {
            get => groupIconName;
            set
            {
                groupIconName = value;
                groupIcon = string.IsNullOrEmpty(value) ? null
                    : EditorResources.IsMonochrome(value) ? EditorResources.GetTintedTexture(value)
                    : EditorResources.GetTexture(value);
            }
        }

        /// <summary>How many commands have been appended.</summary>
        public int Count { get; private set; }

        /// <summary>Appends a command.</summary>
        public void Add(string path, Action command)
            => Append(path, command, default, DropdownMenuAction.Status.Normal);

        /// <summary>
        /// Appends a command carrying the registered icon <paramref name="iconName"/>. A single-tone
        /// glyph is recoloured to the package accent; artwork keeps the colours it was authored with.
        /// </summary>
        public void Add(string path, Action command, string iconName)
            => Append(path, command, Icon(iconName), DropdownMenuAction.Status.Normal);

        /// <summary>
        /// Appends a command drawn from <paramref name="presentation"/>, which supplies appearance
        /// only — icon, description, chip, secondary text, and the decorator, trailing-control and
        /// expandable-body factories. The row's label, group, value and checked state belong to the
        /// command and replace whatever the template carries.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
        public void Add(string path, Action command, Selector.SelectorItem presentation)
            => Append(path, command, presentation, DropdownMenuAction.Status.Normal);

        /// <summary>Appends a command that draws the palette checkmark while <paramref name="on"/>.</summary>
        public void AddToggle(string path, Action command, bool on)
            => AddToggle(path, command, on, default(Selector.SelectorItem));

        /// <summary>Appends a checkable command carrying the registered icon <paramref name="iconName"/>, as <see cref="Add(string, Action, string)"/> treats it.</summary>
        public void AddToggle(string path, Action command, bool on, string iconName)
            => AddToggle(path, command, on, Icon(iconName));

        /// <summary>Appends a checkable command drawn from <paramref name="presentation"/>, as <see cref="Add(string, Action, Selector.SelectorItem)"/> describes it.</summary>
        public void AddToggle(string path, Action command, bool on, Selector.SelectorItem presentation)
            => Append(path, command, presentation, on
                ? DropdownMenuAction.Status.Normal | DropdownMenuAction.Status.Checked
                : DropdownMenuAction.Status.Normal);

        /// <summary>
        /// Draws a divider before the next command appended to <paramref name="path"/>, or to the
        /// package's own group when <paramref name="path"/> is null. A divider that ends up first in
        /// its group is dropped rather than drawn.
        /// </summary>
        public void Separator(string path = null)
            => menu.AppendSeparator(string.IsNullOrEmpty(path) ? group : group + "/" + path);

        /// <summary>
        /// The row an icon name asks for. The texture stays as authored — a glyph is recoloured by
        /// the row's own accent at draw time, so pre-tinting it here would multiply the two.
        /// </summary>
        private static Selector.SelectorItem Icon(string iconName) => new()
        {
            icon = EditorResources.GetTexture(iconName),
            tintIcon = EditorResources.IsMonochrome(iconName),
        };

        private void Append(string path, Action command, Selector.SelectorItem presentation,
            DropdownMenuAction.Status status)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A command needs a path.", nameof(path));
            if (command == null) throw new ArgumentNullException(nameof(command));

            presentation.groupOrder = order;
            presentation.groupIcon = groupIcon;
            presentation.groupAccentKey = accent;
            presentation.accentKey ??= accent;

            menu.AppendAction(group + "/" + path, _ => command(), _ => status, presentation);
            Count++;
        }
    }
}
