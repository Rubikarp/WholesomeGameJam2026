using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace LightSide
{
    /// <summary>
    /// Project Settings → LightSide: the rendering options shared by every LightSide package, and the
    /// shaders they hold for the build.
    /// </summary>
    /// <remarks>
    /// The shader list is maintained by <see cref="LightSideSettingsProvider"/>, not authored here — a
    /// package declares which of its shaders ship, and this page reports the result. An entry with no
    /// shader is a build-configuration failure waiting to happen, which is what it shows.
    /// </remarks>
    internal static class LightSideSettingsPage
    {
        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider(LightSideMenu.ProjectSettingsRoot, SettingsScope.Project)
            {
                label = "LightSide",
                keywords = new HashSet<string>
                {
                    "LightSide", "Shader", "Rendering", "Lit", "UniText", "UniShapes", "UniLottie",
                },
                activateHandler = (_, root) => Build(root),
            };
        }

        private static void Build(VisualElement panelRoot)
        {
            var root = InspectorVisuals.CreateWindowRoot(panelRoot);

            var rendering = InspectorVisuals.CreateSection("Rendering");
            var lit = new InspectorToggle("Include Lit Shaders")
            {
                value = LightSideSettings.IncludeLitShaders,
                tooltip = "Keep the lit world-space surface shader in builds. Turn it off when nothing " +
                          "in the project is lit — unlit world surfaces use their own shader and are " +
                          "unaffected. Lighting is the one expensive axis of the shader family.",
            };
            lit.RegisterValueChangedCallback(e =>
            {
                LightSideSettings.IncludeLitShaders = e.newValue;
                LightSideSettingsProvider.Refresh();
            });
            rendering.Add(lit);
            root.Add(rendering);

            var shaders = InspectorVisuals.CreateSection("Shaders in builds");
            shaders.Add(new HelpBox(
                "A shader reaches a build only through a reference held here. Each package declares its " +
                "own; an option above may deliberately drop one, and it then reads as excluded.",
                HelpBoxMessageType.Info));

            var list = InspectorVisuals.CreateStack();
            shaders.Add(list);

            var rescan = new InspectorPillButton { text = "Rescan packages" };
            rescan.clicked += () =>
            {
                LightSideSettingsProvider.Refresh();
                Fill(list);
            };
            shaders.Add(rescan);
            root.Add(shaders);

            Fill(list);
        }

        private static void Fill(VisualElement list)
        {
            list.Clear();
            var settings = LightSideSettings.Instance;
            if (settings == null || settings.shaders.Length == 0)
            {
                list.Add(new HelpBox("No settings asset yet — it is created on the next editor load.",
                    HelpBoxMessageType.Warning));
                return;
            }

            for (var i = 0; i < settings.shaders.Length; i++)
            {
                var entry = settings.shaders[i];
                var row = InspectorVisuals.CreateEqualRow();
                row.Add(new Label(entry.name));
                row.Add(new Label(entry.shader != null ? "included" : "excluded"));
                list.Add(row);
            }
        }
    }
}
