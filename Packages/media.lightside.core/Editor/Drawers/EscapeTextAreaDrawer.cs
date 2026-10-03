using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    [CustomPropertyDrawer(typeof(EscapeTextAreaAttribute))]
    internal sealed class EscapeTextAreaDrawer : LightSidePropertyDrawer<EscapeTextAreaAttribute>
    {
        private const string ToggleTooltip =
            "Process escape sequences (\\n, \\r, \\t, \\uXXXX, \\xXX)";

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var attribute = context.Binding.SerializedField
                .GetCustomAttribute<EscapeTextAreaAttribute>();
            var preference = GetPrefKey(context.Property);
            var escapeEnabled = EditorPrefs.GetBool(preference, attribute.ProcessEscapes);
            var row = InspectorVisuals.CreateRow();
            row.AddToClassList("lightside-escape-text-area");
            var field = new InspectorTextArea(context.Label);
            field.AddToClassList("lightside-escape-text-area__field");
            field.style.minHeight = Mathf.Max(1, attribute.MinLines) * 18f;
            var toggle = new Button { text = "E", tooltip = ToggleTooltip };
            toggle.AddToClassList("lightside-escape-text-area__toggle");
            row.Add(field);
            row.Add(toggle);

            void Refresh()
            {
                var stored = (string)context.Binding.Value ?? string.Empty;
                var value = escapeEnabled ? ToDisplayString(stored) : stored;
                if (field.value != value) field.SetValueWithoutNotify(value);
                field.showMixedValue = context.Binding.HasMultipleValues;
                InspectorVisuals.RefreshTextFieldPresentation(field);
                var background = escapeEnabled
                    ? EditorResources.ToggleAccent
                    : EditorResources.TogglePanelColor;
                toggle.style.backgroundColor = background;
                toggle.style.color = EditorResources.ForegroundOn(background);
            }

            field.RegisterSerializedValueChanged((_, value) => context.SetValue(
                escapeEnabled ? FromDisplayString(value) : value));
            toggle.clicked += () =>
            {
                escapeEnabled = !escapeEnabled;
                EditorPrefs.SetBool(preference, escapeEnabled);
                Refresh();
            };
            return context.Observe(row, Refresh);
        }

        private static string GetPrefKey(SerializedProperty property)
        {
            return $"EscapeTextArea_{property.propertyPath}_{ObjectUtils.GetInstanceIdCompat(property.serializedObject.targetObject)}";
        }

        internal static string ToDisplayString(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;

            var sb = new StringBuilder(stored.Length + 16);
            foreach (var c in stored)
            {
                switch (c)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\0': sb.Append("\\0"); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (char.IsControl(c) || c > 127 && !char.IsLetterOrDigit(c) && !char.IsPunctuation(c) && !char.IsWhiteSpace(c))
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        internal static string FromDisplayString(string display)
        {
            if (string.IsNullOrEmpty(display) || display.IndexOf('\\') < 0)
                return display;

            var sb = new StringBuilder(display.Length);
            for (var i = 0; i < display.Length; i++)
            {
                if (display[i] != '\\' || i + 1 >= display.Length)
                {
                    sb.Append(display[i]);
                    continue;
                }

                var next = display[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); i++; break;
                    case 'r': sb.Append('\r'); i++; break;
                    case 't': sb.Append('\t'); i++; break;
                    case '0': sb.Append('\0'); i++; break;
                    case '\\': sb.Append('\\'); i++; break;
                    case 'u':
                        if (i + 5 < display.Length && TryParseHex(display, i + 2, 4, out var unicodeValue))
                        {
                            sb.Append((char)unicodeValue);
                            i += 5;
                        }
                        else
                        {
                            sb.Append('\\');
                        }
                        break;
                    case 'x':
                        if (i + 3 < display.Length && TryParseHex(display, i + 2, 2, out var hexValue))
                        {
                            sb.Append((char)hexValue);
                            i += 3;
                        }
                        else
                        {
                            sb.Append('\\');
                        }
                        break;
                    default:
                        sb.Append('\\');
                        break;
                }
            }
            return sb.ToString();
        }

        private static bool TryParseHex(string str, int start, int length, out int value)
        {
            value = 0;
            if (start + length > str.Length) return false;

            for (var i = 0; i < length; i++)
            {
                if (!Ascii.TryHexDigit(str[start + i], out var digit)) return false;

                value = value * 16 + digit;
            }
            return true;
        }
    }

}
