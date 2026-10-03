using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// How an icon's source pixels are combined with a tint colour.
    /// </summary>
    public enum TintMode
    {
        /// <summary>Per-channel multiply: <c>result.rgb = source.rgb * tint.rgb</c>. Preserves source shading; white source produces exact tint.</summary>
        Multiply,
        /// <summary>RGB replacement with source as alpha mask: <c>result.rgb = tint.rgb; result.a = source.a * tint.a</c>. Always produces the exact tint colour regardless of source brightness.</summary>
        Replace,
    }

    /// <summary>
    /// Shared editor icon and inspector-accent surface: loads textures from registered
    /// <c>Resources</c> roots (<see cref="RegisterIconSource"/>), caches theme- and
    /// colour-tinted variants, and hands out stable per-type accent colours.
    /// </summary>
    public static class EditorResources
    {
        private static readonly Dictionary<string, Texture2D> textureCache = new();
        private static readonly Dictionary<string, Texture2D> tintedCache = new();
        private static readonly Dictionary<string, Texture2D> coloredCache = new();
        private static readonly Dictionary<string, bool> monochromeCache = new();
        private static bool tintedForProSkin;

        /// <summary>Alpha below which a pixel carries no colour worth judging, anti-aliased edges included.</summary>
        private const byte OpaqueEnough = 8;

        /// <summary>Largest channel spread still read as grey, absorbing the fringing compression leaves on anti-aliased edges.</summary>
        private const int GreyTolerance = 8;

        private static readonly Color darkThemeTint = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color lightThemeTint = new(0.2f, 0.2f, 0.2f, 1f);

        /// <summary>
        /// Palette used to colour-tint per-type icons and element labels. Vivid Material-accent
        /// tones — high saturation, high value, well distributed across hue so adjacent types are
        /// easy to tell apart at a glance. Type identity (FullName) hashes into a stable slot —
        /// same type, same colour everywhere.
        /// </summary>
        private static readonly Color[] palette =
        {
            new Color32(255, 130, 130, 255), new Color32( 99, 230, 199, 255), new Color32(255, 158, 236, 255), new Color32(110, 235, 106, 255), new Color32(180, 150, 255, 255), new Color32(255, 240, 110, 255), new Color32(130, 165, 255, 255), new Color32(255, 160,  90, 255), new Color32( 90, 220, 240, 255), new Color32(255, 160, 200, 255), new Color32( 90, 230, 150, 255), new Color32(235, 160, 250, 255), new Color32(170, 240, 100, 255), new Color32(135, 135, 255, 255), new Color32(255, 210,  80, 255), new Color32( 90, 190, 250, 255),
        };

        private static readonly Dictionary<Type, Color> typeColorCache = new();

        private const float LightSkinAccentLuminance = 0.08f;
        private const float InkPivotLuminance = 0.179f;

        private static readonly Color darkInk = new Color32(40, 40, 40, 255);
        private static readonly Color lightInk = new Color32(244, 245, 246, 255);

        /// <summary>
        /// The accent as the active editor skin needs it: unchanged on the dark skin; on the light skin
        /// deepened to a fixed relative luminance. The scaling is linear, so hue and saturation carry
        /// over untouched. Colours already at or below that luminance pass through, which makes the
        /// call idempotent. Alpha is preserved.
        /// Apply to any accent that reaches the light skin as text, icon tint, or fill.
        /// </summary>
        public static Color ForSkin(Color color)
        {
            if (EditorGUIUtility.isProSkin) return color;

            var linear = color.linear;
            var luminance = Luminance(linear);
            if (luminance <= LightSkinAccentLuminance) return color;

            var scale = LightSkinAccentLuminance / luminance;
            var deepened = new Color(linear.r * scale, linear.g * scale, linear.b * scale, 1f).gamma;
            return new Color(deepened.r, deepened.g, deepened.b, color.a);
        }

        /// <summary>Ink that stays legible on <paramref name="background"/> — near-black or near-white, whichever contrasts more.</summary>
        public static Color ForegroundOn(Color background)
            => Luminance(background.linear) > InkPivotLuminance ? darkInk : lightInk;

        private static float Luminance(Color linear)
            => 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;

        /// <summary>Returns a stable palette colour for the given type, adapted to the active skin by <see cref="ForSkin"/>. Same type always returns the same colour. A <see cref="TypeColorAttribute"/> on the type pins an explicit colour, overriding the hash.</summary>
        public static Color GetTypeColor(Type type)
        {
            if (type == null) return ForSkin(Color.white);
            if (typeColorCache.TryGetValue(type, out var cached)) return ForSkin(cached);

            var pinned = type.GetCustomAttribute<TypeColorAttribute>();
            var color = pinned?.Color ?? palette[StableHash(type.FullName ?? type.Name) % palette.Length];
            typeColorCache[type] = color;
            return ForSkin(color);
        }

        /// <summary>Returns the stable accent assigned to a selector value.</summary>
        public static Color GetAccentColor(object value, string fallbackIdentity = null)
        {
            if (value is ISelectorAccentSource source)
                value = source.SelectorAccentKey;
            if (value is Type type)
                return GetTypeColor(type);
            if (value == null)
                return PaletteColor("null:" + (fallbackIdentity ?? string.Empty));

            var valueType = value.GetType();
            if (valueType.GetCustomAttribute<TypeColorAttribute>() != null)
                return GetTypeColor(valueType);

            string identity;
            if (value is string text)
                identity = text;
            else if (value is Enum enumValue)
                identity = enumValue.ToString();
            else if (!string.IsNullOrEmpty(fallbackIdentity))
                identity = fallbackIdentity;
            else if (value is IFormattable formattable)
                identity = formattable.ToString(null, CultureInfo.InvariantCulture);
            else
                identity = value.ToString();
            return PaletteColor((valueType.FullName ?? valueType.Name) + ":" + identity);
        }

        private static Color PaletteColor(string identity)
            => ForSkin(palette[StableHash(identity) % palette.Length]);

        private static int StableHash(string s)
        {
            unchecked
            {
                var hash = 2166136261u;
                for (var i = 0; i < s.Length; i++)
                {
                    hash ^= s[i];
                    hash *= 16777619;
                }
                return (int)(hash & 0x7FFFFFFF);
            }
        }

        /// <summary>Icons Core ships for its own shared commands.</summary>
        private const string CoreIconRoot = "LightSide/Icons/";

        private static readonly List<string> resourceRoots = new();
        private static readonly Dictionary<string, string> groupIconMap = new();
        private static readonly Dictionary<Type, string> typeIconMap = new();

        /// <summary>
        /// Registers an icon source: the <c>Resources</c> folder its textures load from, plus optional
        /// type → icon-name and group → icon-name maps consumed by <see cref="GetTypeIcon"/> /
        /// <see cref="GetGroupIcon"/>. Multiple packages may register — maps merge (a later
        /// registration wins on key collision) and roots are probed in registration order. Call from
        /// an <c>[InitializeOnLoad]</c> static constructor; registration clears the texture caches so
        /// earlier misses re-resolve against the new root.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterCoreIcons() => RegisterIconSource(CoreIconRoot);

        public static void RegisterIconSource(string resourcesRoot,
            IReadOnlyDictionary<Type, string> typeIcons = null,
            IReadOnlyDictionary<string, string> groupIcons = null)
        {
            if (!string.IsNullOrEmpty(resourcesRoot))
            {
                var root = resourcesRoot.EndsWith("/") ? resourcesRoot : resourcesRoot + "/";
                if (!resourceRoots.Contains(root)) resourceRoots.Add(root);
            }

            if (typeIcons != null)
                foreach (var pair in typeIcons)
                    typeIconMap[pair.Key] = pair.Value;

            if (groupIcons != null)
                foreach (var pair in groupIcons)
                    groupIconMap[pair.Key] = pair.Value;

            ClearCache();
        }

        /// <summary>Theme-tinted icon registered for the selector group <paramref name="groupName"/>, or null when the group has no registered icon.</summary>
        public static Texture2D GetGroupIcon(string groupName)
        {
            return !string.IsNullOrEmpty(groupName) && groupIconMap.TryGetValue(groupName, out var iconName)
                ? GetTintedTexture(iconName)
                : null;
        }

        /// <summary>Icon registered for <paramref name="type"/>, coloured with its <see cref="GetTypeColor"/> accent; null when the type has no registered icon.</summary>
        public static Texture2D GetTypeIcon(Type type)
        {
            if (type == null || !typeIconMap.TryGetValue(type, out var iconName))
                return null;
            return GetColoredTexture(iconName, GetTypeColor(type));
        }

        /// <summary>Returns the icon-resource name registered for <paramref name="type"/>, or <see langword="null"/> if the type has no registered icon. Use when you need the raw name string (e.g. to feed into a preset descriptor) rather than the loaded texture.</summary>
        public static string GetTypeIconName(Type type)
        {
            return type != null && typeIconMap.TryGetValue(type, out var iconName) ? iconName : null;
        }

        /// <summary>Recessed neutral background shared by toggle-like editor controls.</summary>
        public static Color TogglePanelColor => EditorGUIUtility.isProSkin
            ? new Color32(48, 48, 48, 255)
            : new Color32(224, 224, 224, 255);

        /// <summary>Neutral icon colour for the active editor skin.</summary>
        public static Color IconColor => EditorGUIUtility.isProSkin
            ? darkThemeTint
            : lightThemeTint;

        /// <summary>Green accent for an "on"/active editor toggle, adapted to the active skin. Single source so accents stay consistent across every toggle-like control.</summary>
        public static Color ToggleAccent => ForSkin(new Color32(120, 175, 130, 255));

        /// <summary>Accent of a status line reporting a satisfied condition, adapted to the active skin.</summary>
        public static Color StatusSuccess => ForSkin(new Color(0.33f, 1f, 0.39f));

        /// <summary>Accent of a status line reporting a condition the user should act on, adapted to the active skin.</summary>
        public static Color StatusWarning => ForSkin(new Color(1f, 0.8f, 0.2f));

        /// <summary>Accent of a status line reporting a failed condition, adapted to the active skin.</summary>
        public static Color StatusError => ForSkin(new Color(1f, 0.35f, 0.28f));

        /// <summary>Accent of a status line carrying supplementary detail, adapted to the active skin.</summary>
        public static Color StatusInfo => ForSkin(new Color(0.45f, 0.78f, 1f));


        /// <summary>Returns the icon tinted with an explicit colour. Cached per (name, colour, mode) tuple. Default mode is <see cref="TintMode.Replace"/> — the source's RGB is ignored and only its alpha is used as a mask.</summary>
        public static Texture2D GetColoredTexture(string name, Color color, TintMode mode = TintMode.Replace)
        {
            var key = $"{name}:{ColorUtility.ToHtmlStringRGB(color)}:{(int)mode}";
            if (coloredCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var source = GetTexture(name);
            if (source == null) return null;

            var tinted = TintTexture(source, color, mode);
            tinted.name = key;
            coloredCache[key] = tinted;
            return tinted;
        }

        /// <summary>Raw (untinted) texture <paramref name="name"/> from the registered resource roots, probed in registration order; null when no root provides it. Cached, including misses — <see cref="RegisterIconSource"/> resets the cache.</summary>
        public static Texture2D GetTexture(string name)
        {
            if (textureCache.TryGetValue(name, out var cached))
                return cached;

            Texture2D tex = null;
            for (var i = 0; i < resourceRoots.Count && tex == null; i++)
                tex = Resources.Load<Texture2D>(resourceRoots[i] + name);
            textureCache[name] = tex;
            return tex;
        }

        /// <summary>Registered icon texture in its authored colours, untinted; null when missing.</summary>
        public static Texture2D GetIcon(string name) => GetTexture(name);

        /// <summary>
        /// Whether an icon is a single-tone glyph, which a caller may recolour freely, rather than
        /// artwork whose authored colours carry meaning. An icon Unity will not hand pixels for —
        /// Read/Write off — counts as artwork, so it is shown exactly as authored. Cached per name.
        /// </summary>
        public static bool IsMonochrome(string name)
        {
            if (monochromeCache.TryGetValue(name, out var cached)) return cached;
            var monochrome = Monochrome(GetTexture(name));
            monochromeCache[name] = monochrome;
            return monochrome;
        }

        private static bool Monochrome(Texture2D texture)
        {
            if (texture == null || !texture.isReadable) return false;
            foreach (var pixel in texture.GetPixels32())
            {
                if (pixel.a < OpaqueEnough) continue;
                var low = Mathf.Min(pixel.r, Mathf.Min(pixel.g, pixel.b));
                var high = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                if (high - low > GreyTolerance) return false;
            }
            return true;
        }

        /// <summary>
        /// Creates a decorative icon: scaled to fit, and transparent to pointer events so it never takes a
        /// click from the control it decorates.
        /// </summary>
        /// <param name="name">Icon name, resolved through the registered icon sources.</param>
        /// <param name="tint">An explicit colour, or null for the theme's own tint.</param>
        public static Image CreateIcon(string name, Color? tint = null) =>
            CreateIcon(tint.HasValue ? GetColoredTexture(name, tint.Value) : GetTintedTexture(name));

        /// <inheritdoc cref="CreateIcon(string,Color?)"/>
        /// <param name="image">The texture to show.</param>
        /// <param name="tint">A colour multiplied into the texture, or null to leave it as it is.</param>
        public static Image CreateIcon(Texture image, Color? tint = null)
        {
            var icon = new Image
            {
                image = image,
                scaleMode = ScaleMode.ScaleToFit,
                pickingMode = PickingMode.Ignore,
            };
            if (tint.HasValue) icon.tintColor = tint.Value;
            return icon;
        }

        /// <summary>Icon tinted for the active editor theme (light icons on the dark skin, dark on the light skin). The tint cache flushes automatically when the skin changes.</summary>
        public static Texture2D GetTintedTexture(string name)
        {
            if (tintedForProSkin != EditorGUIUtility.isProSkin)
            {
                DestroyAndClear(tintedCache);
                tintedForProSkin = EditorGUIUtility.isProSkin;
            }

            if (tintedCache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            var source = GetTexture(name);
            if (source == null) return null;

            var tint = EditorGUIUtility.isProSkin ? darkThemeTint : lightThemeTint;
            var tinted = TintTexture(source, tint, TintMode.Multiply);
            tinted.name = name;
            tintedCache[name] = tinted;
            return tinted;
        }


        /// <summary>Drops every cached texture and content (destroying generated tint textures) so the next lookup reloads from Resources.</summary>
        public static void ClearCache()
        {
            textureCache.Clear();
            monochromeCache.Clear();
            DestroyAndClear(tintedCache);
            DestroyAndClear(coloredCache);
        }

        private static void DestroyAndClear(Dictionary<string, Texture2D> cache)
        {
            foreach (var tex in cache.Values)
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            cache.Clear();
        }

        private static Texture2D TintTexture(Texture2D source, Color tint, TintMode mode)
        {
            var pixels = source.GetPixels();
            if (mode == TintMode.Replace)
            {
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(tint.r, tint.g, tint.b, pixels[i].a * tint.a);
            }
            else
            {
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color(
                        pixels[i].r * tint.r,
                        pixels[i].g * tint.g,
                        pixels[i].b * tint.b,
                        pixels[i].a * tint.a
                    );
            }

            var tinted = new Texture2D(source.width, source.height, TextureFormat.ARGB32, true);
            tinted.SetPixels(pixels);
            tinted.Apply(true);
            tinted.filterMode = FilterMode.Trilinear;
            return tinted;
        }
    }
}
