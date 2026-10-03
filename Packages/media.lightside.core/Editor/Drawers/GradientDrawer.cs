using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Inspector field for <see cref="Gradient"/>: a single-line gradient preview that opens an
    /// interactive stop editor on click and provides retained-mode previews for selector rows.
    /// </summary>
    [CustomPropertyDrawer(typeof(Gradient))]
    public sealed class GradientDrawer : LightSidePropertyDrawer<Gradient>
    {
        private const int PreviewWidth = 256;

        static GradientDrawer() => EditorLifecycle.UnmanagedCleaning += Cleanup;

        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var field = new ToolkitGradientField(context.Label);
            field.Preview.clicked += () =>
            {
                var property = context.Binding.FindSerializedProperty();
                if (property != null)
                    GradientPopupWindow.Show(field.Preview.worldBound, property);
            };

            void Refresh()
            {
                field.SetGradient((Gradient)context.Binding.Value);
                var mixed = context.Binding.HasMultipleValues;
                field.showMixedValue = mixed;
                field.Preview.SetEnabled(!mixed);
                field.Preview.tooltip = mixed
                    ? "Selected gradients differ. Copy and paste one value to unify them before editing stops."
                    : "Edit gradient stops";
            }
            return context.Observe(field, Refresh);
        }

        /// <summary>Creates a read-only UI Toolkit preview strip for selector rows and summaries.</summary>
        public static VisualElement CreatePreview(in Gradient gradient)
        {
            var root = new VisualElement();
            root.style.position = Position.Relative;
            root.style.overflow = Overflow.Hidden;
            var checkerImage = new Image
            {
                image = CheckerTexture(),
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
            };
            var rampImage = new Image
            {
                image = GetPreviewTexture(gradient),
                scaleMode = ScaleMode.StretchToFill,
                pickingMode = PickingMode.Ignore,
            };
            FillPreview(checkerImage);
            FillPreview(rampImage);
            checkerImage.RegisterCallback<GeometryChangedEvent>(evt =>
                checkerImage.uv = new Rect(0f, 0f,
                    evt.newRect.width / 12f, evt.newRect.height / 12f));
            root.Add(checkerImage);
            root.Add(rampImage);
            return root;
        }

        private static void FillPreview(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0f;
            element.style.right = 0f;
            element.style.top = 0f;
            element.style.bottom = 0f;
        }

        private static readonly Dictionary<int, (Gradient gradient, Texture2D texture, int stamp)> previewCache = new();
        private static int accessStamp;

        private sealed class ToolkitGradientField : BaseField<Gradient>
        {
            private readonly Image background;
            private readonly Image ramp;
            private readonly Label mixed;
            private Texture2D texture;

            public ToolkitGradientField(string label)
                : this(label, new Button())
            {
            }

            private ToolkitGradientField(string label, Button preview)
                : base(label, preview)
            {
                InspectorVisuals.Attach(this);
                AddToClassList("lightside-gradient-field");
                Preview = preview;
                preview.AddToClassList("lightside-gradient-field__input");

                background = new Image
                {
                    image = CheckerTexture(),
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.StretchToFill,
                };
                Fill(background);
                background.RegisterCallback<GeometryChangedEvent>(evt =>
                    background.uv = new Rect(0f, 0f,
                        evt.newRect.width / 12f, evt.newRect.height / 12f));
                ramp = new Image
                {
                    pickingMode = PickingMode.Ignore,
                    scaleMode = ScaleMode.StretchToFill,
                };
                Fill(ramp);
                mixed = new Label
                {
                    pickingMode = PickingMode.Ignore,
                };
                Fill(mixed);
                mixed.style.unityTextAlign = TextAnchor.MiddleCenter;
                preview.Add(background);
                preview.Add(ramp);
                preview.Add(mixed);
                RegisterCallback<AttachToPanelEvent>(_ =>
                {
                    if (texture == null) SetGradient(value);
                });
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (texture != null) DeferDestroy(texture);
                    texture = null;
                });
            }

            public Button Preview { get; }

            public void SetGradient(in Gradient gradient)
            {
                SetValueWithoutNotify(gradient);
                texture ??= NewRamp(PreviewWidth);
                Bake(texture, gradient);
                ramp.image = texture;
            }

            protected override void UpdateMixedValueContent()
            {
                if (mixed == null) return;
                var display = showMixedValue ? DisplayStyle.None : DisplayStyle.Flex;
                background.style.display = display;
                ramp.style.display = display;
                mixed.style.display = showMixedValue ? DisplayStyle.Flex : DisplayStyle.None;
                mixed.text = mixedValueLabel.text;
            }

            private static void Fill(VisualElement element)
            {
                element.style.position = Position.Absolute;
                element.style.left = 0f;
                element.style.right = 0f;
                element.style.top = 0f;
                element.style.bottom = 0f;
            }
        }

        /// <summary>
        /// Cache hits verify full equality against the stored gradient — the cache lives for the whole
        /// session, so a bare-hash key would serve a colliding gradient's ramp forever. A verified
        /// collision replaces the entry (last one wins; the loser re-bakes on its next repaint).
        /// </summary>
        internal static Texture2D GetPreviewTexture(in Gradient gradient)
        {
            var key = gradient.GetHashCode();
            if (previewCache.TryGetValue(key, out var entry) && entry.texture != null && entry.gradient.Equals(gradient))
            {
                previewCache[key] = (entry.gradient, entry.texture, ++accessStamp);
                return entry.texture;
            }

            return BakeCached(key, gradient);
        }

        private static Texture2D BakeCached(int key, in Gradient gradient)
        {
            if (previewCache.TryGetValue(key, out var stale) && stale.texture != null)
                DeferDestroy(stale.texture);

            if (previewCache.Count > 128)
                EvictOldest(32);

            var tex = NewRamp(PreviewWidth);
            Bake(tex, gradient);
            previewCache[key] = (gradient.Clone(), tex, ++accessStamp);
            return tex;
        }

        /// <summary>Least-recently-used eviction — a wholesale purge would rebake the entire visible set every repaint once the population exceeds capacity.</summary>
        private static void EvictOldest(int count)
        {
            var order = new List<(int stamp, int key)>(previewCache.Count);
            foreach (var kvp in previewCache)
                order.Add((kvp.Value.stamp, kvp.Key));
            order.Sort((a, b) => a.stamp.CompareTo(b.stamp));

            for (var i = 0; i < count && i < order.Count; i++)
            {
                if (previewCache.TryGetValue(order[i].key, out var e) && e.texture != null)
                    DeferDestroy(e.texture);
                previewCache.Remove(order[i].key);
            }
        }

        private static readonly List<Texture2D> destroyQueue = new();
        private static bool destroyScheduled;

        /// <summary>Defers destruction so a texture already submitted for the current editor event remains valid.</summary>
        private static void DeferDestroy(Texture2D tex)
        {
            destroyQueue.Add(tex);
            if (destroyScheduled) return;
            destroyScheduled = true;
            EditorApplication.delayCall += FlushDestroyQueue;
        }

        private static void FlushDestroyQueue()
        {
            destroyScheduled = false;
            foreach (var t in destroyQueue)
                if (t != null) UnityEngine.Object.DestroyImmediate(t);
            destroyQueue.Clear();
        }

        private static Texture2D NewRamp(int width) => new(width, 1, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        private static void Bake(Texture2D tex, in Gradient gradient)
        {
            var w = tex.width;
            var row = new Color32[w];
            for (var i = 0; i < w; i++)
                row[i] = gradient.Evaluate(i / (float)(w - 1));
            tex.SetPixels32(row);
            tex.Apply(false, false);
        }

        private static Texture2D checker;

        internal static Texture2D CheckerTexture()
        {
            if (checker != null) return checker;
            checker = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            var a = new Color(0.76f, 0.76f, 0.76f);
            var b = new Color(0.55f, 0.55f, 0.55f);
            checker.SetPixels(new[] { a, b, b, a });
            checker.Apply();
            return checker;
        }

        private static void Cleanup()
        {
            foreach (var e in previewCache.Values)
                if (e.texture != null) UnityEngine.Object.DestroyImmediate(e.texture);
            previewCache.Clear();
            FlushDestroyQueue();
            if (checker != null)
            {
                UnityEngine.Object.DestroyImmediate(checker);
                checker = null;
            }
        }

    }

    /// <summary>Compact UI Toolkit row for a gradient stop.</summary>
    [CustomPropertyDrawer(typeof(GradientStop))]
    internal sealed class GradientStopDrawer : LightSidePropertyDrawer<GradientStop>
    {
        protected override VisualElement CreateToolkit(SerializedPropertyContext context)
        {
            var property = context.Binding.FindSerializedProperty();
            if (property == null)
                throw new InvalidOperationException("Gradient stop property is unavailable.");
            var timeProperty = InspectorHelpers.RequireRelative(property, "time");
            var colorProperty = InspectorHelpers.RequireRelative(property, "color");
            var timeBinding = new SerializedPropertyBinding(timeProperty);
            var colorBinding = new SerializedPropertyBinding(colorProperty);
            var row = InspectorVisuals.CreateCompactRow();
            row.AddToClassList("lightside-gradient-stop");

            var time = new IntegerField { isDelayed = true };
            time.AddToClassList("lightside-gradient-stop__number");
            var timeUnit = new Label("%");
            timeUnit.AddToClassList("lightside-gradient-stop__unit");
            var color = new ColorField { showAlpha = false };
            color.AddToClassList("lightside-gradient-stop__color");
            var hex = new TextField { isDelayed = true };
            hex.AddToClassList("lightside-gradient-stop__hex");
            var alpha = new IntegerField { isDelayed = true };
            alpha.AddToClassList("lightside-gradient-stop__number");
            var alphaUnit = new Label("%");
            alphaUnit.AddToClassList("lightside-gradient-stop__unit");
            row.Add(time);
            row.Add(timeUnit);
            row.Add(color);
            row.Add(hex);
            row.Add(alpha);
            row.Add(alphaUnit);
            PrefabOverrideVisual.Attach(row, timeBinding,
                PrefabOverrideVisual.FindAnchor(time), false);
            PrefabOverrideVisual.Attach(row, colorBinding,
                PrefabOverrideVisual.FindAnchor(color), false);

            void Refresh()
            {
                var current = (Color)colorBinding.Value;
                time.SetValueWithoutNotify(Mathf.RoundToInt(
                    Mathf.Clamp01((float)timeBinding.Value) * 100f));
                color.SetValueWithoutNotify(current);
                hex.SetValueWithoutNotify(ColorUtility.ToHtmlStringRGB(current));
                alpha.SetValueWithoutNotify(Mathf.RoundToInt(Mathf.Clamp01(current.a) * 100f));
                time.showMixedValue = timeBinding.HasMultipleValues;
                color.showMixedValue = colorBinding.HaveDifferentValues(value =>
                {
                    var currentColor = (Color)value;
                    return new Color(currentColor.r, currentColor.g, currentColor.b, 1f);
                });
                hex.showMixedValue = color.showMixedValue;
                alpha.showMixedValue = colorBinding.HaveDifferentValues(
                    value => ((Color)value).a);
            }

            time.RegisterValueChangedCallback(evt => timeBinding.SetValue(
                Mathf.Clamp(evt.newValue, 0, 100) / 100f, "Change Gradient Stop"));
            color.RegisterValueChangedCallback(evt =>
            {
                colorBinding.TransformValue(value =>
                {
                    var current = (Color)value;
                    current.r = evt.newValue.r;
                    current.g = evt.newValue.g;
                    current.b = evt.newValue.b;
                    return current;
                }, "Change Gradient Stop");
            });
            hex.RegisterValueChangedCallback(evt =>
            {
                if (!ColorUtility.TryParseHtmlString("#" + evt.newValue, out var next))
                {
                    Refresh();
                    return;
                }
                colorBinding.TransformValue(value =>
                {
                    var current = (Color)value;
                    current.r = next.r;
                    current.g = next.g;
                    current.b = next.b;
                    return current;
                }, "Change Gradient Stop");
            });
            alpha.RegisterValueChangedCallback(evt =>
            {
                colorBinding.TransformValue(value =>
                {
                    var current = (Color)value;
                    current.a = Mathf.Clamp(evt.newValue, 0, 100) / 100f;
                    return current;
                }, "Change Gradient Stop");
            });
            new SerializedPropertyContext(timeBinding, timeProperty, "Time")
                .Observe(row, Refresh);
            return new SerializedPropertyContext(colorBinding, colorProperty, "Color")
                .Observe(row, Refresh);
        }

    }
}
